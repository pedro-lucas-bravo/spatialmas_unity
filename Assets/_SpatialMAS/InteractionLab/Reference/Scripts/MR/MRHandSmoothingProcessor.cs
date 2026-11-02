using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Processing;

namespace EnigmaLux.MR
{
    /// <summary>
    /// Low-latency velocity-adaptive 1-Euro + micro-deadband hand smoothing processor for Meta Quest 3 OpenXR.
    ///
    /// Jitter vs Latency Architecture:
    /// 1. Visual Hand Mesh (Passthrough overlay):
    ///    On Meta Quest 3, the OpenXR runtime (xrLocateHandJointsEXT) already performs predictive Kalman
    ///    filtering forward to the exact display photon scan-out time. Applying an exponential moving
    ///    average / 1-Euro filter to raw XRHandSubsystem joints delays the poses by 30-50ms, causing the
    ///    visual hand mesh to visibly lag behind the user's physical hand in Passthrough!
    ///    Therefore, `filterSubsystemJoints` is FALSE by default so visual hands receive native forward-predicted
    ///    poses with 0 ms added filter delay (matching the Horizon OS system environment overlay).
    /// 2. Physical Grab Anchors (Held objects):
    ///    Jitter filtering belongs on interactable object anchors (e.g. `PhysicalGrabAnchor` in `MRHandPinchHysteresis`),
    ///    which uses the provided `OneEuroFilterVec3` and `OneEuroFilterQuat` to eliminate optical tremor
    ///    when holding virtual cubes, without adding lag to the visual hand mesh.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MRHandSmoothingProcessor : MonoBehaviour, IXRHandProcessor
    {
        [Header("Subsystem Joint Interception")]
        [Tooltip("If true, filters raw XRHandSubsystem joints globally. WARNING: Modifying subsystem joints adds phase lag (30-50ms) to visual hand meshes in Passthrough, causing them to trail behind physical hands. For zero visual latency (matching Meta Quest system hands), keep this FALSE and smooth grab anchors instead.")]
        [SerializeField] private bool m_FilterSubsystemJoints = false;

        public bool filterSubsystemJoints
        {
            get => m_FilterSubsystemJoints;
            set
            {
                if (m_FilterSubsystemJoints != value)
                {
                    m_FilterSubsystemJoints = value;
                    if (!m_FilterSubsystemJoints && m_Subsystem != null)
                    {
                        m_Subsystem.UnregisterProcessor(this);
                        m_Subsystem = null;
                        Debug.Log("[MRHandSmoothingProcessor] Unregistered processor from XRHandSubsystem (Visual hands receive 0ms latency forward-predicted poses).");
                    }
                    else if (m_FilterSubsystemJoints && isActiveAndEnabled)
                    {
                        FindAndRegisterSubsystem();
                    }
                }
            }
        }

        [Header("Wrist Root Position 1-Euro Filter (Matches FilteredWristOffset)")]
        [Tooltip("Minimum cutoff frequency (Hz) for wrist position when stationary. Lower = more smoothing at rest (human_swarm_ims uses 2.0).")]
        [SerializeField] private float m_RootPosMinCutoff = 2.0f;
        [Tooltip("Velocity slope (beta) for wrist position. Higher = faster response / zero lag when moving.")]
        [SerializeField] private float m_RootPosBeta = 15.0f;
        [Tooltip("Derivative cutoff frequency (Hz) for velocity estimation.")]
        [SerializeField] private float m_DerivativeCutoff = 1.0f;
        [Tooltip("Sub-millimeter stationary micro-deadband (in meters) to lock optical sensor noise at rest.")]
        [SerializeField] private float m_RootPositionMicroDeadband = 0.00045f; // 0.45 mm

        [Header("Wrist Root Rotation 1-Euro Filter (Matches FilteredWristOffset)")]
        [Tooltip("Minimum cutoff frequency (Hz) for wrist rotation when stationary (human_swarm_ims uses 0.6).")]
        [SerializeField] private float m_RootRotMinCutoff = 0.85f;
        [Tooltip("Velocity slope (beta) for wrist rotation. Higher = instantaneous response.")]
        [SerializeField] private float m_RootRotBeta = 20.0f;
        [Tooltip("Stationary angular micro-deadband (in degrees) to prevent subtle rotational shimmer.")]
        [SerializeField] private float m_RootRotationMicroDeadband = 0.25f; // 0.25 deg

        [Header("Finger Joint Local Filters (Wrist-Space)")]
        [Tooltip("Minimum cutoff frequency (Hz) for local finger joint positions.")]
        [SerializeField] private float m_JointPosMinCutoff = 2.5f;
        [Tooltip("Velocity slope (beta) for local finger joint positions (high value keeps pinch actuation instantaneous with 0ms lag).")]
        [SerializeField] private float m_JointPosBeta = 18.0f;
        [Tooltip("Minimum cutoff frequency (Hz) for local finger joint rotations.")]
        [SerializeField] private float m_JointRotMinCutoff = 1.5f;
        [Tooltip("Velocity slope (beta) for local finger joint rotations.")]
        [SerializeField] private float m_JointRotBeta = 20.0f;

        public int callbackOrder => -10;

        private XRHandSubsystem m_Subsystem;
        private static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        private readonly HandSmoothingState m_LeftState = new HandSmoothingState();
        private readonly HandSmoothingState m_RightState = new HandSmoothingState();

        private void OnEnable()
        {
            FindAndRegisterSubsystem();
        }

        private void OnDisable()
        {
            if (m_Subsystem != null)
            {
                m_Subsystem.UnregisterProcessor(this);
                m_Subsystem = null;
            }
            m_LeftState.Reset();
            m_RightState.Reset();
        }

        private void Update()
        {
            if (m_Subsystem == null || !m_Subsystem.running)
            {
                FindAndRegisterSubsystem();
            }
        }

        private void FindAndRegisterSubsystem()
        {
            if (!m_FilterSubsystemJoints)
            {
                if (m_Subsystem != null)
                {
                    m_Subsystem.UnregisterProcessor(this);
                    m_Subsystem = null;
                }
                return;
            }

            SubsystemManager.GetSubsystems(s_Subsystems);
            for (int i = 0; i < s_Subsystems.Count; ++i)
            {
                var handSubsystem = s_Subsystems[i];
                if (handSubsystem != null && handSubsystem.running)
                {
                    if (m_Subsystem != handSubsystem)
                    {
                        m_Subsystem?.UnregisterProcessor(this);
                        m_Subsystem = handSubsystem;
                        m_Subsystem.RegisterProcessor(this);
                        Debug.Log("[MRHandSmoothingProcessor] Registered 1-Euro + micro-deadband hand smoothing processor on XRHandSubsystem (Dynamic + BeforeRender).");
                    }
                    return;
                }
            }
        }

        public void ProcessJoints(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags successFlags, XRHandSubsystem.UpdateType updateType)
        {
            if (!m_FilterSubsystemJoints)
                return;

            // Process on BOTH UpdateType.Dynamic (so MonoBehaviour.Update / MRHandPinchHysteresis / XRGrabInteractable
            // receive smoothed joint poses) AND UpdateType.BeforeRender (so visual hand rendering aligns at 90 Hz).
            ProcessHand(subsystem.leftHand, subsystem, m_LeftState);
            ProcessHand(subsystem.rightHand, subsystem, m_RightState);
        }

        private void ProcessHand(XRHand hand, XRHandSubsystem subsystem, HandSmoothingState state)
        {
            if (!hand.isTracked)
            {
                state.Reset();
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            if (!state.wasTracked || state.lastUpdateTime <= 0)
            {
                InitializeHandState(hand, state, now);
                return;
            }

            float dt = (float)(now - state.lastUpdateTime);

            // If Dynamic and BeforeRender happen within < 0.4ms of each other, re-apply the cached smoothed pose
            // so the subsystem's raw joint array for this pass is still overwritten with smoothed joints!
            if (dt < 0.0004f)
            {
                ApplyCachedPoseToHand(hand, subsystem, state);
                return;
            }

            // If time gap is large (>100ms, e.g. tracking re-acquired or frame hitch), snap immediately
            if (dt > 0.1f)
            {
                InitializeHandState(hand, state, now);
                return;
            }

            state.lastUpdateTime = now;

            // 1. Filter Root Pose (Wrist) using 1-Euro Filter + micro-deadband
            Vector3 rawRootPos = hand.rootPose.position;
            Quaternion rawRootRot = hand.rootPose.rotation;

            Vector3 smoothRootPos = state.rootPositionFilter.Filter(
                rawRootPos, dt, m_RootPosMinCutoff, m_RootPosBeta, m_DerivativeCutoff, m_RootPositionMicroDeadband);
            Quaternion smoothRootRot = state.rootRotationFilter.Filter(
                rawRootRot, dt, m_RootRotMinCutoff, m_RootRotBeta, m_DerivativeCutoff, m_RootRotationMicroDeadband);

            Pose smoothedRootPose = new Pose(smoothRootPos, smoothRootRot);
            state.lastSmoothedRootPose = smoothedRootPose;

            // 2. Filter Finger Joints in local wrist space (preserving rigid hand motion without phase lag)
            Quaternion invRawRootRot = Quaternion.Inverse(rawRootRot);
            var rawJoints = hand.GetRawJointArray();
            int count = Mathf.Min(rawJoints.Length, state.jointPosFilters.Length);

            for (int i = 1; i < count; i++)
            {
                if (rawJoints[i].TryGetPose(out Pose jointPose))
                {
                    Vector3 localPos = invRawRootRot * (jointPose.position - rawRootPos);
                    Quaternion localRot = invRawRootRot * jointPose.rotation;

                    Vector3 smoothLocalPos = state.jointPosFilters[i].Filter(
                        localPos, dt, m_JointPosMinCutoff, m_JointPosBeta, m_DerivativeCutoff, 0.00025f);
                    Quaternion smoothLocalRot = state.jointRotFilters[i].Filter(
                        localRot, dt, m_JointRotMinCutoff, m_JointRotBeta, m_DerivativeCutoff, 0.15f);

                    Pose smoothJointWorldPose = new Pose(
                        smoothRootRot * smoothLocalPos + smoothRootPos,
                        smoothRootRot * smoothLocalRot
                    );

                    state.lastSmoothedJointPoses[i] = smoothJointWorldPose;
                    state.hasJointPose[i] = true;

                    var j = rawJoints[i];
                    j.SetPose(smoothJointWorldPose);
                    rawJoints[i] = j;
                }
            }

            // 3. Update hand root pose and commit to subsystem
            hand.SetRootPose(smoothedRootPose);
            subsystem.SetCorrespondingHand(hand);
        }

        private static void InitializeHandState(XRHand hand, HandSmoothingState state, double now)
        {
            state.wasTracked = true;
            state.lastUpdateTime = now;

            Vector3 rawRootPos = hand.rootPose.position;
            Quaternion rawRootRot = hand.rootPose.rotation;
            state.rootPositionFilter.Initialize(rawRootPos);
            state.rootRotationFilter.Initialize(rawRootRot);
            state.lastSmoothedRootPose = hand.rootPose;

            Quaternion invRawRootRot = Quaternion.Inverse(rawRootRot);
            var rawJoints = hand.GetRawJointArray();
            int count = Mathf.Min(rawJoints.Length, state.jointPosFilters.Length);

            for (int i = 1; i < count; i++)
            {
                if (rawJoints[i].TryGetPose(out Pose jointPose))
                {
                    Vector3 localPos = invRawRootRot * (jointPose.position - rawRootPos);
                    Quaternion localRot = invRawRootRot * jointPose.rotation;
                    state.jointPosFilters[i].Initialize(localPos);
                    state.jointRotFilters[i].Initialize(localRot);
                    state.lastSmoothedJointPoses[i] = jointPose;
                    state.hasJointPose[i] = true;
                }
            }
        }

        private static void ApplyCachedPoseToHand(XRHand hand, XRHandSubsystem subsystem, HandSmoothingState state)
        {
            var rawJoints = hand.GetRawJointArray();
            int count = Mathf.Min(rawJoints.Length, state.lastSmoothedJointPoses.Length);
            for (int i = 1; i < count; i++)
            {
                if (state.hasJointPose[i])
                {
                    var j = rawJoints[i];
                    j.SetPose(state.lastSmoothedJointPoses[i]);
                    rawJoints[i] = j;
                }
            }
            hand.SetRootPose(state.lastSmoothedRootPose);
            subsystem.SetCorrespondingHand(hand);
        }

        #region 1-Euro Filter Data Structures (Position & Quaternion)

        private class HandSmoothingState
        {
            public bool wasTracked;
            public double lastUpdateTime;
            public Pose lastSmoothedRootPose;

            public readonly OneEuroFilterVec3 rootPositionFilter = new OneEuroFilterVec3();
            public readonly OneEuroFilterQuat rootRotationFilter = new OneEuroFilterQuat();

            public readonly OneEuroFilterVec3[] jointPosFilters = new OneEuroFilterVec3[26];
            public readonly OneEuroFilterQuat[] jointRotFilters = new OneEuroFilterQuat[26];
            public readonly Pose[] lastSmoothedJointPoses = new Pose[26];
            public readonly bool[] hasJointPose = new bool[26];

            public HandSmoothingState()
            {
                for (int i = 0; i < 26; i++)
                {
                    jointPosFilters[i] = new OneEuroFilterVec3();
                    jointRotFilters[i] = new OneEuroFilterQuat();
                }
            }

            public void Reset()
            {
                wasTracked = false;
                lastUpdateTime = 0;
                Array.Clear(hasJointPose, 0, hasJointPose.Length);
            }
        }

        public class OneEuroFilterVec3
        {
            private Vector3 m_PrevValue;
            private Vector3 m_PrevDerivative;
            private bool m_Initialized;

            public void Initialize(Vector3 value)
            {
                m_PrevValue = value;
                m_PrevDerivative = Vector3.zero;
                m_Initialized = true;
            }

            public Vector3 Filter(Vector3 rawValue, float dt, float minCutoff, float beta, float dCutoff, float microDeadband = 0f)
            {
                if (!m_Initialized || dt <= 0.0001f)
                {
                    Initialize(rawValue);
                    return rawValue;
                }

                Vector3 delta = rawValue - m_PrevValue;
                float dist = delta.magnitude;

                // Smooth micro-deadband attenuation to suppress stationary sensor noise without step jumps
                if (microDeadband > 0f && dist < microDeadband)
                {
                    float ratio = dist / microDeadband;
                    rawValue = Vector3.Lerp(m_PrevValue, rawValue, ratio * ratio);
                    delta = rawValue - m_PrevValue;
                }

                Vector3 rawDerivative = delta / dt;
                float dAlpha = ComputeAlpha(dCutoff, dt);
                Vector3 filteredDerivative = Vector3.Lerp(m_PrevDerivative, rawDerivative, dAlpha);
                m_PrevDerivative = filteredDerivative;

                float cutoff = minCutoff + beta * filteredDerivative.magnitude;
                float alpha = ComputeAlpha(cutoff, dt);

                Vector3 filtered = Vector3.Lerp(m_PrevValue, rawValue, alpha);
                m_PrevValue = filtered;
                return filtered;
            }

            private static float ComputeAlpha(float cutoffHz, float dt)
            {
                float tau = 1.0f / (2.0f * Mathf.PI * Mathf.Max(0.01f, cutoffHz));
                return 1.0f / (1.0f + tau / dt);
            }
        }

        public class OneEuroFilterQuat
        {
            private Quaternion m_PrevValue = Quaternion.identity;
            private float m_PrevAngularSpeedRad;
            private bool m_Initialized;

            public void Initialize(Quaternion value)
            {
                m_PrevValue = value;
                m_PrevAngularSpeedRad = 0f;
                m_Initialized = true;
            }

            public Quaternion Filter(Quaternion rawValue, float dt, float minCutoff, float beta, float dCutoff, float microDeadbandDeg = 0f)
            {
                if (!m_Initialized || dt <= 0.0001f)
                {
                    Initialize(rawValue);
                    return rawValue;
                }

                // Ensure shortest-arc hemisphere continuity
                if (Quaternion.Dot(m_PrevValue, rawValue) < 0f)
                {
                    rawValue = new Quaternion(-rawValue.x, -rawValue.y, -rawValue.z, -rawValue.w);
                }

                float angleDeg = Quaternion.Angle(m_PrevValue, rawValue);
                if (microDeadbandDeg > 0f && angleDeg < microDeadbandDeg)
                {
                    float ratio = angleDeg / microDeadbandDeg;
                    rawValue = Quaternion.Slerp(m_PrevValue, rawValue, ratio * ratio);
                    angleDeg = Quaternion.Angle(m_PrevValue, rawValue);
                }

                float rawAngularSpeedRad = (angleDeg * Mathf.Deg2Rad) / dt;
                float dAlpha = ComputeAlpha(dCutoff, dt);
                float filteredSpeedRad = Mathf.Lerp(m_PrevAngularSpeedRad, rawAngularSpeedRad, dAlpha);
                m_PrevAngularSpeedRad = filteredSpeedRad;

                float cutoff = minCutoff + beta * filteredSpeedRad;
                float alpha = ComputeAlpha(cutoff, dt);

                Quaternion filtered = Quaternion.Slerp(m_PrevValue, rawValue, alpha);
                m_PrevValue = filtered;
                return filtered;
            }

            private static float ComputeAlpha(float cutoffHz, float dt)
            {
                float tau = 1.0f / (2.0f * Mathf.PI * Mathf.Max(0.01f, cutoffHz));
                return 1.0f / (1.0f + tau / dt);
            }
        }

        #endregion
    }
}
