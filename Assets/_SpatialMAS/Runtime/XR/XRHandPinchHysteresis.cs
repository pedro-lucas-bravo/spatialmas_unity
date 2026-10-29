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
        [SerializeField, Range(0.005f, 0.05f)] private float _activateDistanceMeters = 0.022f;
        [SerializeField, Range(0.015f, 0.08f)] private float _releaseDistanceMeters = 0.040f;
        [SerializeField] private XRInputButtonReader _fallbackInput = new XRInputButtonReader("Fallback Select");
        [SerializeField] private Transform _trackingOrigin;

        private bool _isPerformed;
        private bool _wasPerformedThisFrame;
        private bool _wasCompletedThisFrame;
        private float _currentValue;
        private Vector3 _lastPinchMidpointWorld;
        private bool _isHandTracked;

        private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        public Handedness Handedness
        {
            get => _handedness;
            set => _handedness = value;
        }

        public bool IsPinching => _isPerformed;
        public bool IsHandTracked => _isHandTracked;
        public Vector3 LastPinchPositionWorld => _lastPinchMidpointWorld;

        public XRInputButtonReader FallbackInput
        {
            get => _fallbackInput;
            set => _fallbackInput = value;
        }

        public void ConfigureTrackingOrigin(Transform trackingOrigin) => _trackingOrigin = trackingOrigin;

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

        private bool TryGetTrackedHand(out XRHand hand)
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

        private void OnEnable() => _fallbackInput?.EnableDirectActionIfModeUsed();

        private void OnDisable()
        {
            _fallbackInput?.DisableDirectActionIfModeUsed();
            _isPerformed = false;
            _isHandTracked = false;
            _currentValue = 0f;
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
                    var thumb = hand.GetJoint(XRHandJointID.ThumbTip);
                    var index = hand.GetJoint(XRHandJointID.IndexTip);
                    var middle = hand.GetJoint(XRHandJointID.MiddleTip);

                    if (thumb.TryGetPose(out Pose thumbPose) && index.TryGetPose(out Pose indexPose))
                    {
                        hasJointData = true;
                        _lastPinchMidpointWorld = XRInteractionRigSetup.TrackingPoseToWorld(
                            new Pose((thumbPose.position + indexPose.position) * 0.5f, Quaternion.identity), TrackingOrigin).position;
                        float dist = Vector3.Distance(thumbPose.position, indexPose.position);

                        if (middle.TryGetPose(out Pose middlePose))
                        {
                            float midDist = Vector3.Distance(thumbPose.position, middlePose.position);
                            if (midDist < dist) dist = midDist;
                        }

                        if (!prev)
                        {
                            _isPerformed = dist <= _activateDistanceMeters;
                        }
                        else
                        {
                            _isPerformed = dist < _releaseDistanceMeters;
                        }

                        float span = Mathf.Max(0.001f, _releaseDistanceMeters - _activateDistanceMeters);
                        _currentValue = _isPerformed ? 1.0f : Mathf.Clamp01(1.0f - ((dist - _activateDistanceMeters) / span));
                    }
                }
            }

            if (!hasJointData && _fallbackInput != null)
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

            _wasPerformedThisFrame = !prev && _isPerformed;
            _wasCompletedThisFrame = prev && !_isPerformed;
        }

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
