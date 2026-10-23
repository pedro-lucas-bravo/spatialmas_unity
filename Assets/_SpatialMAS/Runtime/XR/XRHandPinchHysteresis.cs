using System.Collections.Generic;
using UnityEngine;
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

            SubsystemManager.GetSubsystems(s_Subsystems);
            if (s_Subsystems.Count > 0 && s_Subsystems[0].running)
            {
                var hand = _handedness == Handedness.Left ? s_Subsystems[0].leftHand : s_Subsystems[0].rightHand;
                if (hand.isTracked)
                {
                    _isHandTracked = true;
                    var thumb = hand.GetJoint(XRHandJointID.ThumbTip);
                    var index = hand.GetJoint(XRHandJointID.IndexTip);
                    var middle = hand.GetJoint(XRHandJointID.MiddleTip);

                    if (thumb.TryGetPose(out Pose thumbPose) && index.TryGetPose(out Pose indexPose))
                    {
                        hasJointData = true;
                        _lastPinchMidpointWorld = (thumbPose.position + indexPose.position) * 0.5f;
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
                _lastPinchMidpointWorld = transform.position;
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
