using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

namespace SpatialMAS.XR
{
    /// <summary>
    /// Multiplatform Schmitt-trigger (dual-threshold hysteresis) pinch reader for XR Interaction Toolkit 3.6.
    /// Supports physical XRHandSubsystem joint pinch on Meta Quest 3, Apple Vision Pro, and OpenXR,
    /// with seamless fallback to XRI Input Actions (Vision Pro Spatial Pointer Gaze-Pinch, 6DoF Controllers, or Simulator).
    /// </summary>
    [DefaultExecutionOrder(UnityEngine.XR.Interaction.Toolkit.XRInteractionUpdateOrder.k_XRInputDeviceButtonReader)]
    public class XRHandPinchHysteresis : MonoBehaviour, IXRInputButtonReader
    {
        [SerializeField] private Handedness _handedness = Handedness.Right;
        [SerializeField, Range(0.005f, 0.05f)] private float _activateDistanceMeters = 0.028f;
        [SerializeField, Range(0.015f, 0.08f)] private float _releaseDistanceMeters = 0.045f;
        [SerializeField] private XRInputButtonReader _fallbackInput = new XRInputButtonReader("Fallback Select");
        [SerializeField] private Transform _trackingOrigin;
        [SerializeField] private int _physicalGrabVersion;

        private bool _isPerformed;
        private bool _wasPerformedThisFrame;
        private bool _wasCompletedThisFrame;
        private float _currentValue;
        private Vector3 _lastPinchMidpointWorld;
        private bool _isHandTracked;
        private bool _indexLatched, _middleLatched, _palmLatched;
        private bool _hasAnchor, _releaseOpening;
        private Pose _interactionPose;
        private Vector3 _wristLocalOffset;
        private float _previousPinchDistance;
        private readonly XRHandTrackingProcessor.AnchorFilter _anchorFilter = new XRHandTrackingProcessor.AnchorFilter();
        private bool _uiPerformed, _uiStarted, _uiCompleted;
        private float _uiValue;

        private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        public Handedness Handedness
        {
            get => _handedness;
            set => _handedness = value;
        }

        public bool IsPinching => _isPerformed;
        public bool IsHandTracked => _isHandTracked;
        public Vector3 LastPinchPositionWorld => _lastPinchMidpointWorld;
        public bool IsPalmGrasp => _palmLatched && !_indexLatched && !_middleLatched;
        public bool HasPhysicalAnchor => _hasAnchor;
        public bool TryGetInteractionPoseWorld(out Pose pose)
        {
            if (_hasAnchor) { pose = _interactionPose; return true; }
            return TryGetTrackedPoseWorld(out pose);
        }

        public bool ReadUIIsPerformed() => _uiPerformed;
        public bool ReadUIWasPerformedThisFrame() => _uiStarted;
        public bool ReadUIWasCompletedThisFrame() => _uiCompleted;
        public float ReadUIValue() => _uiValue;

        public XRInputButtonReader FallbackInput
        {
            get => _fallbackInput;
            set => _fallbackInput = value;
        }

        public void ConfigureTrackingOrigin(Transform trackingOrigin)
        {
            _trackingOrigin = trackingOrigin;
            UpgradeLegacyDefaults();
        }

        private void UpgradeLegacyDefaults()
        {
            if (_physicalGrabVersion != 0) return;
            if (Mathf.Approximately(_activateDistanceMeters, 0.022f) && Mathf.Approximately(_releaseDistanceMeters, 0.04f))
            {
                _activateDistanceMeters = 0.028f;
                _releaseDistanceMeters = 0.045f;
            }
            _physicalGrabVersion = 1;
        }

        public void EnsureControllerFallbackBindings()
        {
            if (_fallbackInput == null) _fallbackInput = new XRInputButtonReader("Fallback Select");
            bool configured = _fallbackInput.inputSourceMode == XRInputButtonReader.InputSourceMode.ObjectReference ||
                _fallbackInput.inputSourceMode == XRInputButtonReader.InputSourceMode.ManualValue ||
                _fallbackInput.inputActionReferencePerformed != null ||
                (_fallbackInput.inputActionPerformed != null && _fallbackInput.inputActionPerformed.bindings.Count > 0);
            if (configured) return;
            string hand = _handedness == Handedness.Left ? "LeftHand" : "RightHand";
            _fallbackInput.inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction;
            var press = new InputAction("Controller Select", InputActionType.Button);
            press.AddBinding("<XRController>{" + hand + "}/triggerPressed");
            press.AddBinding("<XRController>{" + hand + "}/gripPressed");
            _fallbackInput.inputActionPerformed = press;
            _fallbackInput.inputActionValue = new InputAction("Controller Select Value", InputActionType.Value,
                "<XRController>{" + hand + "}/trigger");
            if (isActiveAndEnabled) _fallbackInput.EnableDirectActionIfModeUsed();
        }

        private Transform TrackingOrigin
        {
            get
            {
                if (_trackingOrigin == null) _trackingOrigin = XRInteractionRigSetup.GetTrackingOrigin(Camera.main);
                return _trackingOrigin;
            }
        }

        public bool TryGetTrackedHand(out XRHand hand)
        {
            SubsystemManager.GetSubsystems(s_Subsystems);
            foreach (var subsystem in s_Subsystems)
            {
                if (subsystem == null || !subsystem.running) continue;
                hand = _handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
                if (hand.isTracked) return true;
            }
            hand = default;
            return false;
        }

        private bool TryGetMetaAim(out MetaAimHand aim)
        {
            aim = _handedness == Handedness.Left ? MetaAimHand.left : MetaAimHand.right;
            return aim != null && aim.added && aim.isTracked.isPressed &&
                (((MetaAimFlags)aim.aimFlags.ReadValue()) & MetaAimFlags.Valid) != 0;
        }

        /// <summary>Native pointing pose is for the far ray; physical hand telemetry retains the joint/root pose.</summary>
        public bool TryGetAimPoseWorld(out Pose pose)
        {
            if (TryGetMetaAim(out MetaAimHand aim))
            {
                pose = XRInteractionRigSetup.TrackingPoseToWorld(
                    new Pose(aim.devicePosition.ReadValue(), aim.deviceRotation.ReadValue()), TrackingOrigin);
                return true;
            }
            pose = default;
            return false;
        }

        public bool TryGetHandPoseWorld(out Pose pose)
        {
            if (TryGetTrackedHand(out XRHand hand))
            {
                Pose localPose = hand.rootPose;
                if (hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumb) &&
                    hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose index))
                    localPose.position = (thumb.position + index.position) * 0.5f;
                pose = XRInteractionRigSetup.TrackingPoseToWorld(localPose, TrackingOrigin);
                return true;
            }
            pose = default;
            return false;
        }

        public bool TryGetTrackedPoseWorld(out Pose pose)
        {
            if (TryGetHandPoseWorld(out pose)) return true;
            var device = InputDevices.GetDeviceAtXRNode(_handedness == Handedness.Left ? XRNode.LeftHand : XRNode.RightHand);
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && tracked &&
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 position) &&
                device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rotation))
            {
                pose = XRInteractionRigSetup.TrackingPoseToWorld(new Pose(position, rotation), TrackingOrigin);
                return true;
            }
            pose = default;
            return false;
        }

        private void OnEnable()
        {
            UpgradeLegacyDefaults();
            _fallbackInput?.EnableDirectActionIfModeUsed();
        }

        private void OnDisable()
        {
            _fallbackInput?.DisableDirectActionIfModeUsed();
            _isPerformed = false;
            _isHandTracked = false;
            _currentValue = 0f;
            ResetPhysicalState();
            _uiPerformed = _uiStarted = _uiCompleted = false;
            _wasPerformedThisFrame = _wasCompletedThisFrame = false;
            _uiValue = 0f;
        }

        private void ResetPhysicalState()
        {
            _indexLatched = _middleLatched = _palmLatched = false;
            _hasAnchor = _releaseOpening = false;
            _anchorFilter.Reset();
        }

        private void Update()
        {
            bool prev = _isPerformed;
            bool hasJointData = false;
            _isHandTracked = false;

            if (TryGetTrackedHand(out XRHand hand))
            {
                if (hand.isTracked)
                {
                    _isHandTracked = true;
                    if (hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out Pose thumbPose) &&
                        hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose indexPose) &&
                        (hand.GetJoint(XRHandJointID.Palm).TryGetPose(out Pose wristPose) ||
                         hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out wristPose)))
                    {
                        hasJointData = true;
                        _lastPinchMidpointWorld = XRInteractionRigSetup.TrackingPoseToWorld(
                            new Pose((thumbPose.position + indexPose.position) * 0.5f, Quaternion.identity), TrackingOrigin).position;
                        bool hasMiddle = hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out Pose middlePose);
                        bool hasRing = hand.GetJoint(XRHandJointID.RingTip).TryGetPose(out Pose ringPose);
                        Vector3 palmCenter = wristPose.position;
                        if (hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out Pose proximal) &&
                            hand.GetJoint(XRHandJointID.ThumbMetacarpal).TryGetPose(out Pose metacarpal))
                            palmCenter = (proximal.position + metacarpal.position) * 0.5f;
                        EvaluatePhysicalSample(wristPose, thumbPose.position, indexPose.position,
                            middlePose.position, ringPose.position, palmCenter, hasMiddle, hasRing,
                            Mathf.Max(Time.unscaledDeltaTime, 0.0001f));
                    }
                }
            }

            if (!hasJointData) ResetPhysicalState();
            bool previousUI = _uiPerformed;
            if (TryGetMetaAim(out MetaAimHand aim))
            {
                // The runtime's gesture classifier supplies deliberate pinch activation and suppresses
                // the palm-facing system gesture. This is more reliable for far UI than raw joint distance.
                bool systemGesture = (((MetaAimFlags)aim.aimFlags.ReadValue()) & MetaAimFlags.SystemGesture) != 0;
                _uiPerformed = !systemGesture && (aim.indexPressed.isPressed || aim.middlePressed.isPressed);
                _uiValue = systemGesture ? 0f : Mathf.Max(aim.pinchStrengthIndex.ReadValue(), aim.pinchStrengthMiddle.ReadValue());
                if (!hasJointData || systemGesture)
                {
                    _isPerformed = _uiPerformed;
                    _currentValue = _uiValue;
                }
                if (systemGesture) ResetPhysicalState();
            }
            else if (!hasJointData && _fallbackInput != null)
            {
                _isPerformed = _fallbackInput.ReadIsPerformed();
                _currentValue = _fallbackInput.ReadValue();
                _lastPinchMidpointWorld = TryGetTrackedPoseWorld(out Pose pose) ? pose.position : transform.position;
            }
            else if (!hasJointData)
            {
                _isPerformed = false;
                _currentValue = 0f;
            }

            if (!TryGetMetaAim(out _))
            {
                // Whole-hand grasp is a 3D object gesture, never a UI button press.
                _uiPerformed = hasJointData ? _indexLatched || _middleLatched : _isPerformed;
                _uiValue = _uiPerformed ? 1f : 0f;
            }
            _uiStarted = !previousUI && _uiPerformed;
            _uiCompleted = previousUI && !_uiPerformed;

            _wasPerformedThisFrame = !prev && _isPerformed;
            _wasCompletedThisFrame = prev && !_isPerformed;
        }

        // Inputs remain in tracking space. Only the resulting interaction pose is filtered;
        // TryGetHandPoseWorld, visual joints, recording and OSC continue to read provider poses.
        private void EvaluatePhysicalSample(Pose wrist, Vector3 thumb, Vector3 index, Vector3 middle,
            Vector3 ring, Vector3 palmCenter, bool hasMiddle, bool hasRing, float dt)
        {
            float indexDistance = Vector3.Distance(thumb, index);
            float middleDistance = hasMiddle ? Vector3.Distance(thumb, middle) : float.PositiveInfinity;
            _indexLatched = Latch(_indexLatched, indexDistance, _activateDistanceMeters, _releaseDistanceMeters);
            _middleLatched = hasMiddle && Latch(_middleLatched, middleDistance, _activateDistanceMeters, _releaseDistanceMeters);
            float middleCurl = Vector3.Distance(middle, palmCenter);
            float palmCurl = Mathf.Max(middleCurl, Vector3.Distance(ring, palmCenter));
            float indexCurl = Vector3.Distance(index, palmCenter);
            bool poke = indexCurl > 0.082f && indexCurl > middleCurl + 0.028f;
            _palmLatched = hasMiddle && hasRing && !poke && Latch(_palmLatched, palmCurl, 0.058f, 0.076f);
            bool previous = _isPerformed;
            _isPerformed = _indexLatched || _middleLatched || _palmLatched;
            float pinchDistance = Mathf.Min(indexDistance, middleDistance);
            _currentValue = _isPerformed ? 1f : Mathf.Clamp01((_releaseDistanceMeters - pinchDistance) /
                Mathf.Max(0.001f, _releaseDistanceMeters - _activateDistanceMeters));

            Vector3 tip = _indexLatched && _middleLatched ? (index + middle) * 0.5f :
                _middleLatched || (!_indexLatched && middleDistance < indexDistance) ? middle : index;
            Vector3 anchor = IsPalmGrasp ? Vector3.Lerp(palmCenter, (thumb + middle) * 0.5f, 0.35f) : (thumb + tip) * 0.5f;
            bool opening = IsPalmGrasp ? palmCurl > 0.058f :
                pinchDistance > _activateDistanceMeters && (pinchDistance - _previousPinchDistance) / dt > 0.025f;
            if (previous && _isPerformed && _hasAnchor)
            {
                // Stay locked throughout the release band, including a pause partway through opening.
                _releaseOpening = opening || (_releaseOpening && (IsPalmGrasp ? palmCurl > 0.058f : pinchDistance > _activateDistanceMeters));
                if (_releaseOpening) anchor = wrist.position + wrist.rotation * _wristLocalOffset;
            }
            else _releaseOpening = false;
            if (!_releaseOpening)
                _wristLocalOffset = Quaternion.Inverse(wrist.rotation) * (anchor - wrist.position);
            _previousPinchDistance = pinchDistance;
            Pose worldPose = XRInteractionRigSetup.TrackingPoseToWorld(new Pose(anchor, wrist.rotation), TrackingOrigin);
            _interactionPose = _anchorFilter.Filter(worldPose, dt);
            _hasAnchor = true;
        }

        public static bool Latch(bool latched, float distance, float activate, float release) =>
            latched ? distance < release : distance <= activate;

        public bool ReadIsPerformed() => _isPerformed;
        public bool ReadWasPerformedThisFrame() => _wasPerformedThisFrame;
        public bool ReadWasCompletedThisFrame() => _wasCompletedThisFrame;
        public float ReadValue() => _currentValue;

        public bool TryReadValue(out float value)
        {
            value = _currentValue;
            return true;
        }
    }
}
