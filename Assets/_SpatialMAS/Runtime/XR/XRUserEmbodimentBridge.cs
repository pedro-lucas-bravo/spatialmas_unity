using System.Collections.Generic;
using SpatialMAS.Core;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace SpatialMAS.XR
{
    /// <summary>
    /// Tracks the user's physical Head 6DoF (MainCamera) and Left/Right Hands across all XR platforms
    /// (Meta Quest 3, Apple Vision Pro, Generic OpenXR, and Desktop fallback).
    /// Provides real-time human repeller vectors for Crowd Navigation / Musical Encounters and streams
    /// /xr/user/pose and /xr/hands/pose over OSC at 25 Hz.
    /// </summary>
    public class XRUserEmbodimentBridge : MonoBehaviour
    {
        [SerializeField] private float _oscPoseStreamRateHz = 25.0f;

        private SimulationCommandBus _bus;
        private Transform _headCameraTransform;
        private XRHandPinchHysteresis _leftPinchReader;
        private XRHandPinchHysteresis _rightPinchReader;

        private Vector3 _lastHeadPosition;
        private float _streamTimer;

        public Vector3 HeadPositionWorld { get; private set; }
        public Quaternion HeadRotationWorld { get; private set; } = Quaternion.identity;
        public Vector3 HeadVelocityWorld { get; private set; }

        public Vector3 LeftHandPositionWorld { get; private set; }
        public Quaternion LeftHandRotationWorld { get; private set; } = Quaternion.identity;
        public bool IsLeftHandPinching { get; private set; }
        public bool IsLeftHandTracked { get; private set; }

        public Vector3 RightHandPositionWorld { get; private set; }
        public Quaternion RightHandRotationWorld { get; private set; } = Quaternion.identity;
        public bool IsRightHandPinching { get; private set; }
        public bool IsRightHandTracked { get; private set; }

        public bool UserRepellerEnabled { get; set; } = true;
        public float UserRepellerRadiusMeters { get; set; } = 0.65f;
        public float UserRepellerForce { get; set; } = 1.5f;

        private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        public void Initialize(
            SimulationCommandBus bus,
            Transform headCameraTransform,
            XRHandPinchHysteresis leftPinch = null,
            XRHandPinchHysteresis rightPinch = null)
        {
            _bus = bus;
            _headCameraTransform = headCameraTransform;
            _leftPinchReader = leftPinch;
            _rightPinchReader = rightPinch;

            if (_headCameraTransform != null)
            {
                HeadPositionWorld = _headCameraTransform.position;
                HeadRotationWorld = _headCameraTransform.rotation;
                _lastHeadPosition = HeadPositionWorld;
            }

            if (_bus != null)
            {
                _bus.OnConfigureUserRepeller += HandleConfigureUserRepeller;
            }
        }

        private void OnDestroy()
        {
            if (_bus != null)
            {
                _bus.OnConfigureUserRepeller -= HandleConfigureUserRepeller;
            }
        }

        private void HandleConfigureUserRepeller(bool enabled, float radiusMeters, float force)
        {
            UserRepellerEnabled = enabled;
            UserRepellerRadiusMeters = Mathf.Clamp(radiusMeters, 0.1f, 5.0f);
            UserRepellerForce = Mathf.Clamp(force, 0f, 20.0f);
        }

        private void Update()
        {
            if (_headCameraTransform == null && Camera.main != null)
            {
                _headCameraTransform = Camera.main.transform;
            }

            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            if (_headCameraTransform != null)
            {
                Vector3 currentPos = _headCameraTransform.position;
                HeadVelocityWorld = (currentPos - _lastHeadPosition) / dt;
                _lastHeadPosition = currentPos;
                HeadPositionWorld = currentPos;
                HeadRotationWorld = _headCameraTransform.rotation;
            }

            SampleHands();

            // Stream /xr/user/pose and /xr/hands/pose at 25 Hz
            _streamTimer += Time.unscaledDeltaTime;
            float interval = 1.0f / Mathf.Max(1.0f, _oscPoseStreamRateHz);
            if (_streamTimer >= interval)
            {
                _streamTimer = 0f;
                _bus?.EmitUserHeadPoseUnity(HeadPositionWorld, HeadRotationWorld);
                _bus?.EmitUserHandPoseUnity(0, LeftHandPositionWorld, IsLeftHandPinching);
                _bus?.EmitUserHandPoseUnity(1, RightHandPositionWorld, IsRightHandPinching);
            }
        }

        private void SampleHands()
        {
            bool leftTracked = false;
            bool rightTracked = false;
            Transform trackingOrigin = XRInteractionRigSetup.GetTrackingOrigin(
                _headCameraTransform != null ? _headCameraTransform.GetComponent<Camera>() : Camera.main);

            SubsystemManager.GetSubsystems(s_Subsystems);
            foreach (var sub in s_Subsystems)
            {
                if (sub == null || !sub.running) continue;
                if (sub.leftHand.isTracked)
                {
                    leftTracked = true;
                    Pose pose = XRInteractionRigSetup.TrackingPoseToWorld(sub.leftHand.rootPose, trackingOrigin);
                    LeftHandPositionWorld = pose.position;
                    LeftHandRotationWorld = pose.rotation;
                }
                if (sub.rightHand.isTracked)
                {
                    rightTracked = true;
                    Pose pose = XRInteractionRigSetup.TrackingPoseToWorld(sub.rightHand.rootPose, trackingOrigin);
                    RightHandPositionWorld = pose.position;
                    RightHandRotationWorld = pose.rotation;
                }
            }

            if (_leftPinchReader != null)
            {
                if (!leftTracked && _leftPinchReader.TryGetTrackedPoseWorld(out Pose pose))
                {
                    leftTracked = true;
                    LeftHandPositionWorld = pose.position;
                    LeftHandRotationWorld = pose.rotation;
                }
                IsLeftHandPinching = leftTracked && _leftPinchReader.IsPinching;
            }
            if (_rightPinchReader != null)
            {
                if (!rightTracked && _rightPinchReader.TryGetTrackedPoseWorld(out Pose pose))
                {
                    rightTracked = true;
                    RightHandPositionWorld = pose.position;
                    RightHandRotationWorld = pose.rotation;
                }
                IsRightHandPinching = rightTracked && _rightPinchReader.IsPinching;
            }

            // Fallback on desktop when no XR hands are active: place virtual hands slightly in front of camera
            if (!leftTracked && _headCameraTransform != null)
            {
                LeftHandPositionWorld = _headCameraTransform.TransformPoint(new Vector3(-0.2f, -0.15f, 0.45f));
            }
            if (!rightTracked && _headCameraTransform != null)
            {
                RightHandPositionWorld = _headCameraTransform.TransformPoint(new Vector3(0.2f, -0.15f, 0.45f));
            }
            IsLeftHandTracked = leftTracked;
            IsRightHandTracked = rightTracked;
            if (!leftTracked) IsLeftHandPinching = false;
            if (!rightTracked) IsRightHandPinching = false;
        }

        /// <summary>
        /// Computes the repulsive Helbing social-force vector exerted by the XR user's head & hands
        /// on an autonomous agent at agentPosWorld.
        /// </summary>
        public Vector3 ComputeRepulsionForceOnAgent(Vector3 agentPosWorld)
        {
            if (!UserRepellerEnabled || UserRepellerForce <= 0.001f)
                return Vector3.zero;

            Vector3 totalForce = ComputeSingleRepeller(agentPosWorld, HeadPositionWorld, UserRepellerRadiusMeters, UserRepellerForce);
            if (IsLeftHandTracked)
                totalForce += ComputeSingleRepeller(agentPosWorld, LeftHandPositionWorld, UserRepellerRadiusMeters * 0.55f, UserRepellerForce * 0.8f);
            if (IsRightHandTracked)
                totalForce += ComputeSingleRepeller(agentPosWorld, RightHandPositionWorld, UserRepellerRadiusMeters * 0.55f, UserRepellerForce * 0.8f);
            return totalForce;
        }

        private static Vector3 ComputeSingleRepeller(Vector3 agentPos, Vector3 repellerPos, float radius, float strength)
        {
            Vector3 diff = agentPos - repellerPos;
            float dist = diff.magnitude;
            if (dist >= radius || dist < 0.0001f) return Vector3.zero;

            float falloff = Mathf.Exp((radius - dist) / Mathf.Max(0.05f, radius * 0.35f)) - 1.0f;
            return (diff / dist) * (strength * falloff);
        }
    }
}
