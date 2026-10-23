using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Processing;

namespace SpatialMAS.XR
{
    /// <summary>
    /// Zero-lag adaptive deadband hand tracking smoothing processor (implementing IXRHandProcessor).
    /// Suppresses sub-millimeter optical tracking jitter when hands are stationary while passing through
    /// active hand motion with zero added latency across Meta Quest 3, Apple Vision Pro, and OpenXR.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class XRHandTrackingProcessor : MonoBehaviour, IXRHandProcessor
    {
        [SerializeField] private float _rootPositionDeadband = 0.0012f;
        [SerializeField] private float _rootPositionMotionThreshold = 0.0035f;
        [SerializeField] private float _rootMinVelocityForInstantPass = 0.04f;
        [SerializeField] private float _rootRotationDeadband = 0.6f;
        [SerializeField] private float _rootRotationMotionThreshold = 2.0f;
        [SerializeField] private float _rootMinAngularSpeedForInstantPass = 25.0f;

        public int callbackOrder => -10;

        private XRHandSubsystem _subsystem;
        private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();
        private readonly HandFilterState _leftState = new HandFilterState();
        private readonly HandFilterState _rightState = new HandFilterState();

        private void OnEnable() => TryRegisterSubsystem();

        private void OnDisable()
        {
            if (_subsystem != null)
            {
                _subsystem.UnregisterProcessor(this);
                _subsystem = null;
            }
            _leftState.Reset();
            _rightState.Reset();
        }

        private void Update()
        {
            if (_subsystem == null || !_subsystem.running)
            {
                TryRegisterSubsystem();
            }
        }

        private void TryRegisterSubsystem()
        {
            SubsystemManager.GetSubsystems(s_Subsystems);
            for (int i = 0; i < s_Subsystems.Count; i++)
            {
                var sub = s_Subsystems[i];
                if (sub != null && sub.running)
                {
                    if (_subsystem != sub)
                    {
                        _subsystem?.UnregisterProcessor(this);
                        _subsystem = sub;
                        _subsystem.RegisterProcessor(this);
                    }
                    return;
                }
            }
        }

        public void ProcessJoints(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags successFlags, XRHandSubsystem.UpdateType updateType)
        {
            if (!Application.isEditor && updateType != XRHandSubsystem.UpdateType.BeforeRender)
                return;

            FilterHand(subsystem.leftHand, subsystem, _leftState);
            FilterHand(subsystem.rightHand, subsystem, _rightState);
        }

        private void FilterHand(XRHand hand, XRHandSubsystem subsystem, HandFilterState state)
        {
            if (!hand.isTracked)
            {
                state.Reset();
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (!state.WasTracked || state.LastTime <= 0)
            {
                state.WasTracked = true;
                state.LastTime = now;
                state.LastPos = hand.rootPose.position;
                state.LastRot = hand.rootPose.rotation;
                return;
            }

            float dt = (float)(now - state.LastTime);
            if (dt < 0.002f) return;
            state.LastTime = now;

            Vector3 rawPos = hand.rootPose.position;
            Quaternion rawRot = hand.rootPose.rotation;

            Vector3 deltaPos = rawPos - state.LastPos;
            float dist = deltaPos.magnitude;
            float speed = dist / dt;

            Vector3 smoothPos;
            if (dist >= _rootPositionMotionThreshold || speed >= _rootMinVelocityForInstantPass)
            {
                smoothPos = rawPos;
            }
            else if (dist <= _rootPositionDeadband)
            {
                smoothPos = state.LastPos;
            }
            else
            {
                float t = (dist - _rootPositionDeadband) / (_rootPositionMotionThreshold - _rootPositionDeadband);
                smoothPos = Vector3.Lerp(state.LastPos, rawPos, t * t * (3f - 2f * t));
            }

            float angle = Quaternion.Angle(state.LastRot, rawRot);
            float angSpeed = angle / dt;
            Quaternion smoothRot;
            if (angle >= _rootRotationMotionThreshold || angSpeed >= _rootMinAngularSpeedForInstantPass)
            {
                smoothRot = rawRot;
            }
            else if (angle <= _rootRotationDeadband)
            {
                smoothRot = state.LastRot;
            }
            else
            {
                float t = (angle - _rootRotationDeadband) / (_rootRotationMotionThreshold - _rootRotationDeadband);
                smoothRot = Quaternion.Slerp(state.LastRot, rawRot, t * t * (3f - 2f * t));
            }

            state.LastPos = smoothPos;
            state.LastRot = smoothRot;

            hand.SetRootPose(new Pose(smoothPos, smoothRot));
            subsystem.SetCorrespondingHand(hand);
        }

        private class HandFilterState
        {
            public bool WasTracked;
            public double LastTime;
            public Vector3 LastPos;
            public Quaternion LastRot = Quaternion.identity;

            public void Reset()
            {
                WasTracked = false;
                LastTime = 0;
            }
        }
    }
}
