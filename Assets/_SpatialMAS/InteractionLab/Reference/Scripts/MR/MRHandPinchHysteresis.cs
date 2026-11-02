using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace EnigmaLux.MR
{
    /// <summary>
    /// Replicates Meta Interaction SDK (HandGrabInteractor / DistanceHandGrabInteractor) pick-and-release
    /// mechanics on top of Unity OpenXR + XR Hands + XR Interaction Toolkit 3.6:
    ///
    /// 1. Multi-Modal Hand Grab Detection (Pinch + Palm/Whole-Hand Grasp):
    ///    - Pinch Grab: Thumb tip against Index and/or Middle tip with Schmitt-trigger hysteresis
    ///      (2.8 cm activate / 4.5 cm release) and 'All' unselect mode (when pinching with both
    ///      Index and Middle, the grab remains locked until both fingers open past release threshold).
    ///    - Palm Grasp: Whole-hand curl where Middle + Ring fingertips curl toward the physical palm
    ///      center ((MiddleProximal + ThumbMetacarpal) * 0.5), guarded against single-finger Poke gestures.
    ///
    /// 2. True Physical Hand Grab Anchor (PinchPoint / GripPoint) + Tight 2.2 cm Contact Sphere:
    ///    - Computes a direct physical grab anchor from XRHand joints (pinch aperture midpoint for pinch,
    ///      palm surface center for palm grasp) with true physical wrist/palm rotation.
    ///    - Unconditionally sets SphereInteractionCaster.castRadius to 2.2 cm (matching ISDK PinchPointRange
    ///      0.025m in MR-Swarmalators.unity, replacing XRI's 10 cm default sphere that caused an "invisible handle").
    ///
    /// 3. FilteredWristOffset 1-Euro Filter on PhysicalGrabAnchor:
    ///    - Matches MR-Swarmalators.unity's FilteredWristOffset (pos: minCutoff=2.0, beta=3.0; rot: minCutoff=0.6,
    ///      beta=6.0) and updates in both Update() and Application.onBeforeRender for rock-solid held objects.
    ///
    /// 4. Release Anti-Twitch Pose Lock + 90 Hz / 100 ms Pre-Release Velocity Ring Buffer:
    ///    - Freezes wrist-local finger offset during the ~40 ms finger-opening window prior to release.
    ///    - Samples hand velocity prior to the finger-opening frames for clean, damped zero-G release inertia.
    /// </summary>
    [DefaultExecutionOrder(UnityEngine.XR.Interaction.Toolkit.XRInteractionUpdateOrder.k_XRInputDeviceButtonReader)]
    public class MRHandPinchHysteresis : MonoBehaviour, IXRInputButtonReader
    {
        public enum PinchFingerMode
        {
            [Tooltip("Pinch using Thumb + Index finger only (maximum precision).")]
            IndexOnly,
            [Tooltip("Pinch using Thumb + Index and/or Thumb + Middle finger (natural 2- or 3-finger chuck pinch).")]
            IndexOrMiddle,
            [Tooltip("Pinch using Thumb + Middle finger only.")]
            MiddleOnly
        }

        public enum ActiveGrabType
        {
            None = 0,
            Pinch = 1,
            Palm = 2,
            Fallback = 3
        }

        [Header("Handedness & Fingers")]
        [Tooltip("Which hand (Left or Right) to monitor.")]
        [SerializeField] private Handedness m_Handedness = Handedness.Right;

        [Tooltip("Which fingers qualify to trigger a pinch against the thumb.")]
        [SerializeField] private PinchFingerMode m_PinchFingerMode = PinchFingerMode.IndexOrMiddle;

        [Header("Pinch Hysteresis Thresholds (Meters)")]
        [Tooltip("Distance between thumb and finger tip (in meters) to ACTIVATE the pinch grab (e.g. 0.028 = 2.8 cm, reliable physical fingertip contact).")]
        [Range(0.005f, 0.05f)]
        [SerializeField] private float m_ActivateDistance = 0.028f; // 2.8 cm - reliable physical fingertip contact

        [Tooltip("Distance between thumb and finger tip (in meters) to RELEASE the pinch grab (e.g. 0.045 = 4.5 cm). Must be greater than activate distance.")]
        [Range(0.015f, 0.08f)]
        [SerializeField] private float m_ReleaseDistance = 0.045f; // 4.5 cm

        [Header("Palm / Whole-Hand Grasp (Meta ISDK PalmGrabRules)")]
        [Tooltip("Allow grabbing objects by curling the fingers into the palm (Middle + Ring curl). Guarded by Anti-Poke.")]
        [SerializeField] private bool m_EnablePalmGrab = true;

        [Tooltip("Distance from Middle & Ring fingertips to physical palm center to ACTIVATE a whole-hand palm grab (e.g. 0.058 = 5.8 cm).")]
        [Range(0.035f, 0.085f)]
        [SerializeField] private float m_PalmActivateDistance = 0.058f; // 5.8 cm

        [Tooltip("Distance from Middle & Ring fingertips to physical palm center to RELEASE a whole-hand palm grab (e.g. 0.076 = 7.6 cm).")]
        [Range(0.045f, 0.110f)]
        [SerializeField] private float m_PalmReleaseDistance = 0.076f; // 7.6 cm

        [Header("Physical Hand Anchor & Surface Contact Precision")]
        [Tooltip("Bind the interactor attach controller directly to the physical hand PinchPoint/GripPoint instead of OpenXR's ray-stabilized Pinch Grab Pose.")]
        [SerializeField] private bool m_UsePhysicalHandGrabAnchor = true;

        [Tooltip("Freeze wrist-local finger offset while fingers are rapidly spreading open to release, eliminating open-hand release twitch.")]
        [SerializeField] private bool m_LockAnchorDuringReleaseOpening = true;

        [Tooltip("Near-field direct grab sphere radius in meters (3.8 cm ensures comfortable grab reach without mid-air phantom grabbing).")]
        [Range(0.012f, 0.060f)]
        [SerializeField] private float m_NearCastRadius = 0.040f; // 4.0 cm contact radius

        [Header("Anchor Jitter Filter (Matches FilteredWristOffset in MR-Swarmalators)")]
        [Tooltip("1-Euro position min cutoff (Hz) on PhysicalGrabAnchor (MR-Swarmalators uses 2.0).")]
        [SerializeField] private float m_AnchorPosMinCutoff = 2.0f;
        [Tooltip("1-Euro position beta slope on PhysicalGrabAnchor (MR-Swarmalators uses 3.0).")]
        [SerializeField] private float m_AnchorPosBeta = 3.0f;
        [Tooltip("1-Euro rotation min cutoff (Hz) on PhysicalGrabAnchor (MR-Swarmalators uses 0.6).")]
        [SerializeField] private float m_AnchorRotMinCutoff = 0.6f;
        [Tooltip("1-Euro rotation beta slope on PhysicalGrabAnchor (MR-Swarmalators uses 6.0).")]
        [SerializeField] private float m_AnchorRotBeta = 6.0f;

        [Header("Fallback Input (In-Editor Simulator & Controllers)")]
        [Tooltip("Fallback input action used when physical hand tracking joints are not active (e.g., Mac Simulator mouse clicks).")]
        [SerializeField] private XRInputButtonReader m_FallbackInput = new XRInputButtonReader("Fallback Select");

        [Header("Live Debug Monitor (Play Mode)")]
        [Tooltip("Live physical distance between Thumb tip and Index tip in centimeters.")]
        [SerializeField] private float m_LiveIndexDistanceCm;

        [Tooltip("Live physical distance between Thumb tip and Middle tip in centimeters.")]
        [SerializeField] private float m_LiveMiddleDistanceCm;

        [Tooltip("Live physical distance between Middle/Ring tips and Palm center in centimeters.")]
        [SerializeField] private float m_LivePalmCurlDistanceCm;

        [Tooltip("Whether this hand is currently actively grabbing via pinch or palm grasp.")]
        [SerializeField] private bool m_IsPinching;

        [Tooltip("Which finger or grasp mode is currently driving the grab.")]
        [SerializeField] private string m_ActiveFinger = "None";

        // Properties for runtime & Inspector configuration
        public Handedness handedness
        {
            get => m_Handedness;
            set => m_Handedness = value;
        }

        public PinchFingerMode pinchFingerMode
        {
            get => m_PinchFingerMode;
            set => m_PinchFingerMode = value;
        }

        public float activateDistance
        {
            get => m_ActivateDistance;
            set
            {
                m_ActivateDistance = Mathf.Clamp(value, 0.005f, 0.05f);
                if (m_ReleaseDistance < m_ActivateDistance + 0.005f)
                    m_ReleaseDistance = m_ActivateDistance + 0.005f;
            }
        }

        public float releaseDistance
        {
            get => m_ReleaseDistance;
            set => m_ReleaseDistance = Mathf.Max(value, m_ActivateDistance + 0.005f);
        }

        public bool enablePalmGrab
        {
            get => m_EnablePalmGrab;
            set => m_EnablePalmGrab = value;
        }

        public float palmActivateDistance
        {
            get => m_PalmActivateDistance;
            set
            {
                m_PalmActivateDistance = Mathf.Clamp(value, 0.035f, 0.085f);
                if (m_PalmReleaseDistance < m_PalmActivateDistance + 0.008f)
                    m_PalmReleaseDistance = m_PalmActivateDistance + 0.008f;
            }
        }

        public float palmReleaseDistance
        {
            get => m_PalmReleaseDistance;
            set => m_PalmReleaseDistance = Mathf.Max(value, m_PalmActivateDistance + 0.008f);
        }

        public bool usePhysicalHandGrabAnchor
        {
            get => m_UsePhysicalHandGrabAnchor;
            set => m_UsePhysicalHandGrabAnchor = value;
        }

        public bool lockAnchorDuringReleaseOpening
        {
            get => m_LockAnchorDuringReleaseOpening;
            set => m_LockAnchorDuringReleaseOpening = value;
        }

        public float nearCastRadius
        {
            get => m_NearCastRadius;
            set
            {
                m_NearCastRadius = Mathf.Clamp(value, 0.012f, 0.035f);
                if (m_SphereCaster != null)
                    m_SphereCaster.castRadius = m_NearCastRadius;
            }
        }

        public XRInputButtonReader fallbackInput
        {
            get => m_FallbackInput;
            set => XRInputReaderUtility.SetInputProperty(ref m_FallbackInput, value, this);
        }

        public ActiveGrabType activeGrabType => m_ActiveGrabType;
        public bool isIndexPinchLatched => m_IndexPinchLatched;
        public bool isMiddlePinchLatched => m_MiddlePinchLatched;
        public bool isPalmGrabLatched => m_PalmGrabLatched;
        public Transform physicalGrabAnchor => m_PhysicalGrabAnchor;
        public InteractionAttachController attachController => m_AttachController;
        public NearFarInteractor nearFarInteractor => m_NearFarInteractor;

        // Static registry of active hand handlers for tactile finger interaction
        public static readonly List<MRHandPinchHysteresis> Instances = new List<MRHandPinchHysteresis>();
        public Vector3 indexTipWorldPos { get; private set; }
        public bool hasIndexTip { get; private set; }
        public Vector3 middleTipWorldPos { get; private set; }
        public bool hasMiddleTip { get; private set; }

        public static bool TryGetActiveIndexTipNear(Vector3 targetPos, float maxRadius, out Vector3 tipPos)
        {
            for (int i = 0; i < Instances.Count; i++)
            {
                var h = Instances[i];
                if (h != null && h.hasIndexTip && Vector3.Distance(h.indexTipWorldPos, targetPos) <= maxRadius)
                {
                    tipPos = h.indexTipWorldPos;
                    return true;
                }
            }
            tipPos = Vector3.zero;
            return false;
        }

        public static bool TryGetActiveFingertipNear(Vector3 targetPos, float maxRadius, out Vector3 tipPos, out Vector3 tipVelocity)
        {
            for (int i = 0; i < Instances.Count; i++)
            {
                var h = Instances[i];
                if (h != null)
                {
                    if (h.hasIndexTip && Vector3.Distance(h.indexTipWorldPos, targetPos) <= maxRadius)
                    {
                        tipPos = h.indexTipWorldPos;
                        h.TryGetSmoothedReleaseVelocity(out tipVelocity, out _);
                        return true;
                    }
                    if (h.hasMiddleTip && Vector3.Distance(h.middleTipWorldPos, targetPos) <= maxRadius)
                    {
                        tipPos = h.middleTipWorldPos;
                        h.TryGetSmoothedReleaseVelocity(out tipVelocity, out _);
                        return true;
                    }
                }
            }
            tipPos = Vector3.zero;
            tipVelocity = Vector3.zero;
            return false;
        }

        // State tracking
        private bool m_IsPerformed;
        private bool m_WasPerformedThisFrame;
        private bool m_WasCompletedThisFrame;
        private float m_CurrentValue;
        private ActiveGrabType m_ActiveGrabType = ActiveGrabType.None;

        // Multi-finger 'All' unselect latch state (matches ISDK FingerUnselectMode.All)
        private bool m_IndexPinchLatched;
        private bool m_MiddlePinchLatched;
        private bool m_PalmGrabLatched;

        // Physical Grab Anchor & Release Anti-Twitch state
        private Transform m_PhysicalGrabAnchor;
        private Transform m_FallbackFollowTransform;
        private InteractionAttachController m_AttachController;
        private NearFarInteractor m_NearFarInteractor;
        private SphereInteractionCaster m_SphereCaster;
        private Vector3 m_LockedWristLocalOffset;
        private bool m_HasValidWristLocalOffset;
        private float m_PrevPinchMetricDist;

        // 1-Euro Filter state on PhysicalGrabAnchor (matches FilteredWristOffset in MR-Swarmalators.unity)
        private readonly MRHandSmoothingProcessor.OneEuroFilterVec3 m_AnchorPosFilter = new MRHandSmoothingProcessor.OneEuroFilterVec3();
        private readonly MRHandSmoothingProcessor.OneEuroFilterQuat m_AnchorRotFilter = new MRHandSmoothingProcessor.OneEuroFilterQuat();
        private double m_LastAnchorFilterTime;

        // 90 Hz / 100 ms Ring Buffer for clean pre-release velocity (matches ISDK HandVelocityCalculator)
        private struct PoseSample
        {
            public float time;
            public Vector3 position;
            public Quaternion rotation;
        }

        private const int k_VelocityBufferCapacity = 16;
        private readonly PoseSample[] m_PoseBuffer = new PoseSample[k_VelocityBufferCapacity];
        private int m_PoseBufferHead;
        private int m_PoseBufferCount;

        private static readonly List<XRHandSubsystem> s_SubsystemsReuse = new List<XRHandSubsystem>();

        private void OnValidate()
        {
            if (m_ReleaseDistance < m_ActivateDistance + 0.005f)
            {
                m_ReleaseDistance = m_ActivateDistance + 0.005f;
            }
            if (m_PalmReleaseDistance < m_PalmActivateDistance + 0.008f)
            {
                m_PalmReleaseDistance = m_PalmActivateDistance + 0.008f;
            }
            m_NearCastRadius = Mathf.Clamp(m_NearCastRadius, 0.015f, 0.060f);
        }

        private void Awake()
        {
            InitializePhysicalGrabAnchorAndInteractor();
        }

        private void OnEnable()
        {
            if (!Instances.Contains(this)) Instances.Add(this);
            m_FallbackInput?.EnableDirectActionIfModeUsed();
            InitializePhysicalGrabAnchorAndInteractor();
            Application.onBeforeRender += OnBeforeRenderUpdate;
        }

        private void OnDisable()
        {
            Instances.Remove(this);
            Application.onBeforeRender -= OnBeforeRenderUpdate;
            m_FallbackInput?.DisableDirectActionIfModeUsed();
            m_IsPerformed = false;
            m_IsPinching = false;
            m_IndexPinchLatched = false;
            m_MiddlePinchLatched = false;
            m_PalmGrabLatched = false;
            m_ActiveGrabType = ActiveGrabType.None;
            m_ActiveFinger = "None";
            m_CurrentValue = 0f;
            m_PoseBufferCount = 0;
            m_LastAnchorFilterTime = 0;
        }

        /// <summary>
        /// Configures the NearFarInteractor, InteractionAttachController, and SphereInteractionCaster
        /// to behave like Meta Interaction SDK's HandGrabInteractor (zero ray-aim stabilization lag,
        /// tight 2.2 cm contact sphere, and direct physical PinchPoint / GripPoint tracking).
        /// </summary>
        public void InitializePhysicalGrabAnchorAndInteractor()
        {
            if (m_NearFarInteractor == null)
                m_NearFarInteractor = GetComponent<NearFarInteractor>();
            if (m_AttachController == null)
                m_AttachController = GetComponent<InteractionAttachController>();
            if (m_SphereCaster == null)
                m_SphereCaster = GetComponentInChildren<SphereInteractionCaster>(true);

            // Create or locate the PhysicalGrabAnchor transform under the hand root
            Transform handParent = transform.parent != null ? transform.parent : transform;
            if (m_PhysicalGrabAnchor == null)
            {
                Transform existing = handParent.Find("PhysicalGrabAnchor");
                if (existing != null)
                {
                    m_PhysicalGrabAnchor = existing;
                }
                else if (Application.isPlaying)
                {
                    var anchorGo = new GameObject("PhysicalGrabAnchor");
                    m_PhysicalGrabAnchor = anchorGo.transform;
                    m_PhysicalGrabAnchor.SetParent(handParent, false);
                }
            }

            if (m_NearFarInteractor != null)
            {
                // Keep far casting enabled so distance pointing works; MRCubeInteraction's 3-zone spatial filter
                // blocks the 2.5cm..22cm near-miss halo so near grabs only occur on true surface contact.
                m_NearFarInteractor.enableNearCasting = true;
                m_NearFarInteractor.enableFarCasting = true;
            }

            if (m_AttachController != null)
            {
                if (m_FallbackFollowTransform == null && m_AttachController.transformToFollow != null && m_AttachController.transformToFollow != m_PhysicalGrabAnchor)
                {
                    m_FallbackFollowTransform = m_AttachController.transformToFollow;
                }

                SetFarPullAttachmentMode(false);

                if (m_UsePhysicalHandGrabAnchor && m_PhysicalGrabAnchor != null)
                {
                    m_AttachController.transformToFollow = m_PhysicalGrabAnchor;
                }
            }

            if (m_SphereCaster != null)
            {
                m_SphereCaster.castRadius = m_NearCastRadius;

                if (m_UsePhysicalHandGrabAnchor && m_PhysicalGrabAnchor != null)
                {
                    m_SphereCaster.castOrigin = m_PhysicalGrabAnchor;
                }
            }
        }

        /// <summary>
        /// Switches InteractionAttachController between:
        /// - Direct / Attract-to-Hand mode (enableRayPullTether = false): zero velocity scaling, zero momentum, 1:1 tracking.
        /// - Ray Pull / Tether mode (enableRayPullTether = true): distance-based velocity scaling + pull bias so pulling
        ///   the hand backward attracts/reels the distant object toward the user along the ray.
        /// </summary>
        public void SetFarPullAttachmentMode(bool enableRayPullTether)
        {
            if (m_AttachController == null)
                return;

            // Always keep MotionStabilizationMode.Never so our 1-Euro filtered PhysicalGrabAnchor drives rotation
            // smoothly without XRI's 25cm / 20deg deadzone lag.
            m_AttachController.motionStabilizationMode = MotionStabilizationMode.Never;
            m_AttachController.smoothOffset = false;

            if (enableRayPullTether)
            {
                m_AttachController.useDistanceBasedVelocityScaling = true;
                m_AttachController.useMomentum = true;
                m_AttachController.pullVelocityBias = 1.45f;
                m_AttachController.pushVelocityBias = 1.15f;
                m_AttachController.zVelocityRampThreshold = 0.18f;
                m_AttachController.minAdditionalVelocityScalar = 0.15f;
                m_AttachController.maxAdditionalVelocityScalar = 2.25f;
                m_AttachController.momentumDecayScale = 1.35f;
            }
            else
            {
                m_AttachController.useDistanceBasedVelocityScaling = false;
                m_AttachController.useMomentum = false;
            }
        }

        private void Update()
        {
            bool prevPerformed = m_IsPerformed;
            bool hasPhysicalHandData = false;

            SubsystemManager.GetSubsystems(s_SubsystemsReuse);
            if (s_SubsystemsReuse.Count > 0 && s_SubsystemsReuse[0].running)
            {
                var subsystem = s_SubsystemsReuse[0];
                var hand = (m_Handedness == Handedness.Left) ? subsystem.leftHand : subsystem.rightHand;

                if (hand.isTracked)
                {
                    hasPhysicalHandData = EvaluatePhysicalHand(hand, prevPerformed, updateButtonState: true);
                }
            }

            // Fallback for In-Editor simulation or controllers when physical hands are not tracked
            if (!hasPhysicalHandData)
            {
                m_IndexPinchLatched = false;
                m_MiddlePinchLatched = false;
                m_PalmGrabLatched = false;
                m_HasValidWristLocalOffset = false;
                m_LastAnchorFilterTime = 0;

                if (m_FallbackFollowTransform != null && m_PhysicalGrabAnchor != null)
                {
                    m_PhysicalGrabAnchor.SetPositionAndRotation(m_FallbackFollowTransform.position, m_FallbackFollowTransform.rotation);
                    RecordPoseSample(m_PhysicalGrabAnchor.position, m_PhysicalGrabAnchor.rotation);
                }

                if (m_FallbackInput != null)
                {
                    m_IsPerformed = m_FallbackInput.ReadIsPerformed();
                    m_CurrentValue = m_FallbackInput.ReadValue();
                    m_IsPinching = m_IsPerformed;
                    m_ActiveGrabType = m_IsPerformed ? ActiveGrabType.Fallback : ActiveGrabType.None;
                    m_ActiveFinger = m_IsPerformed ? "Simulator / Mouse" : "None";
                }
            }

            m_WasPerformedThisFrame = !prevPerformed && m_IsPerformed;
            m_WasCompletedThisFrame = prevPerformed && !m_IsPerformed;
        }

        [BeforeRenderOrder(-50)]
        private void OnBeforeRenderUpdate()
        {
            // Refresh PhysicalGrabAnchor pose right before rendering so XRGrabInteractable's OnBeforeRender pass
            // receives the freshest 90 Hz photon-predicted & 1-Euro-smoothed hand anchor pose.
            SubsystemManager.GetSubsystems(s_SubsystemsReuse);
            if (s_SubsystemsReuse.Count > 0 && s_SubsystemsReuse[0].running)
            {
                var subsystem = s_SubsystemsReuse[0];
                var hand = (m_Handedness == Handedness.Left) ? subsystem.leftHand : subsystem.rightHand;
                if (hand.isTracked)
                {
                    EvaluatePhysicalHand(hand, m_IsPerformed, updateButtonState: false);
                }
            }
        }

        private bool EvaluatePhysicalHand(XRHand hand, bool prevPerformed, bool updateButtonState)
        {
            var thumbTip = hand.GetJoint(XRHandJointID.ThumbTip);
            var indexTip = hand.GetJoint(XRHandJointID.IndexTip);
            var middleTip = hand.GetJoint(XRHandJointID.MiddleTip);

            if (!thumbTip.TryGetPose(out Pose thumbPose))
                return false;

            bool hasIndex = indexTip.TryGetPose(out Pose indexPose);
            bool hasMiddle = middleTip.TryGetPose(out Pose middlePose);
            if (!hasIndex && !hasMiddle)
                return false;

            // Convert hand-space joint poses to world space via XR Origin
            Transform originTransform = GetOriginTransform();
            Vector3 thumbWorld = TransformPointToWorld(originTransform, thumbPose.position);
            Vector3 indexWorld = hasIndex ? TransformPointToWorld(originTransform, indexPose.position) : thumbWorld + Vector3.up;
            Vector3 middleWorld = hasMiddle ? TransformPointToWorld(originTransform, middlePose.position) : thumbWorld + Vector3.up;

            hasIndexTip = hasIndex;
            indexTipWorldPos = indexWorld;
            hasMiddleTip = hasMiddle;
            middleTipWorldPos = middleWorld;

            float distIndex = hasIndex ? Vector3.Distance(thumbWorld, indexWorld) : float.MaxValue;
            float distMiddle = hasMiddle ? Vector3.Distance(thumbWorld, middleWorld) : float.MaxValue;

            if (hasIndex) m_LiveIndexDistanceCm = distIndex * 100f;
            if (hasMiddle) m_LiveMiddleDistanceCm = distMiddle * 100f;

            bool allowIndex = m_PinchFingerMode == PinchFingerMode.IndexOnly || m_PinchFingerMode == PinchFingerMode.IndexOrMiddle;
            bool allowMiddle = m_PinchFingerMode == PinchFingerMode.MiddleOnly || m_PinchFingerMode == PinchFingerMode.IndexOrMiddle;

            bool hasPalmPose = TryGetPhysicalWristAndPalm(hand, originTransform, out Pose wristWorldPose, out Vector3 palmCenterWorld);
            float palmCurlMetric = float.MaxValue;

            if (updateButtonState)
            {
                // 1. Evaluate Pinch Hysteresis with Meta ISDK 'All' FingerUnselectMode
                if (allowIndex)
                {
                    if (!m_IndexPinchLatched)
                    {
                        if (distIndex <= m_ActivateDistance)
                            m_IndexPinchLatched = true;
                    }
                    else
                    {
                        if (distIndex >= m_ReleaseDistance)
                            m_IndexPinchLatched = false;
                    }
                }
                else
                {
                    m_IndexPinchLatched = false;
                }

                if (allowMiddle)
                {
                    if (!m_MiddlePinchLatched)
                    {
                        if (distMiddle <= m_ActivateDistance)
                            m_MiddlePinchLatched = true;
                    }
                    else
                    {
                        if (distMiddle >= m_ReleaseDistance)
                            m_MiddlePinchLatched = false;
                    }
                }
                else
                {
                    m_MiddlePinchLatched = false;
                }

                bool isPinchActive = m_IndexPinchLatched || m_MiddlePinchLatched;

                // 2. Evaluate Palm / Whole-Hand Grasp (Meta ISDK PalmGrabRules + XRUtils.GetCenterPalm)
                if (m_EnablePalmGrab && hasPalmPose && hasMiddle)
                {
                    var ringTip = hand.GetJoint(XRHandJointID.RingTip);
                    if (ringTip.TryGetPose(out Pose ringPose))
                    {
                        Vector3 ringWorld = TransformPointToWorld(originTransform, ringPose.position);
                        float distMiddleToPalm = Vector3.Distance(middleWorld, palmCenterWorld);
                        float distRingToPalm = Vector3.Distance(ringWorld, palmCenterWorld);
                        float distIndexToPalm = hasIndex ? Vector3.Distance(indexWorld, palmCenterWorld) : distMiddleToPalm;

                        // Anti-Poke Guard: In a single-finger Poke gesture, Middle & Ring are curled into the palm
                        // while Index is extended straight out (distIndexToPalm > 8.2 cm and > distMiddleToPalm + 2.8 cm).
                        bool isSingleFingerPokePosture = hasIndex &&
                                                         (distIndexToPalm > 0.082f) &&
                                                         (distIndexToPalm > distMiddleToPalm + 0.028f);

                        palmCurlMetric = Mathf.Max(distMiddleToPalm, distRingToPalm);
                        m_LivePalmCurlDistanceCm = palmCurlMetric * 100f;

                        if (!m_PalmGrabLatched)
                        {
                            if (!isSingleFingerPokePosture && palmCurlMetric <= m_PalmActivateDistance)
                            {
                                m_PalmGrabLatched = true;
                            }
                        }
                        else
                        {
                            float releaseMetric = Mathf.Max(distMiddleToPalm, distRingToPalm);
                            if (releaseMetric >= m_PalmReleaseDistance || isSingleFingerPokePosture)
                            {
                                m_PalmGrabLatched = false;
                            }
                        }
                    }
                }
                else
                {
                    m_PalmGrabLatched = false;
                }

                // Combine Pinch + Palm state
                m_IsPerformed = isPinchActive || m_PalmGrabLatched;
                m_IsPinching = m_IsPerformed;

                float primaryPinchDist = Mathf.Min(
                    allowIndex ? distIndex : float.MaxValue,
                    allowMiddle ? distMiddle : float.MaxValue);

                if (isPinchActive)
                {
                    m_ActiveGrabType = ActiveGrabType.Pinch;
                    if (m_IndexPinchLatched && m_MiddlePinchLatched)
                        m_ActiveFinger = "Index+Middle Pinch";
                    else if (m_IndexPinchLatched)
                        m_ActiveFinger = "Index Pinch";
                    else
                        m_ActiveFinger = "Middle Pinch";
                }
                else if (m_PalmGrabLatched)
                {
                    m_ActiveGrabType = ActiveGrabType.Palm;
                    m_ActiveFinger = "Palm Grasp";
                }
                else
                {
                    m_ActiveGrabType = ActiveGrabType.None;
                    m_ActiveFinger = "None";
                }

                // Analog strength value [0..1]
                if (m_IsPerformed)
                {
                    m_CurrentValue = 1.0f;
                }
                else
                {
                    float pinchSpan = Mathf.Max(0.001f, m_ReleaseDistance - m_ActivateDistance);
                    float pinchStrength = Mathf.Clamp01(1.0f - ((primaryPinchDist - m_ActivateDistance) / pinchSpan));

                    float palmStrength = 0f;
                    if (m_EnablePalmGrab && palmCurlMetric < float.MaxValue)
                    {
                        float palmSpan = Mathf.Max(0.001f, m_PalmReleaseDistance - m_PalmActivateDistance);
                        palmStrength = Mathf.Clamp01(1.0f - ((palmCurlMetric - m_PalmActivateDistance) / palmSpan));
                    }

                    m_CurrentValue = Mathf.Max(pinchStrength, palmStrength);
                }
            }

            // 3. Update Physical Hand Grab Anchor (PinchPoint / GripPoint) with Release Anti-Twitch Lock + 1-Euro Filter
            if (hasPalmPose && m_PhysicalGrabAnchor != null)
            {
                bool isPinchActive = m_IndexPinchLatched || m_MiddlePinchLatched;
                Vector3 rawAnchorWorldPos;
                if (m_ActiveGrabType == ActiveGrabType.Palm && !isPinchActive)
                {
                    // GripPoint: Palm surface center slightly offset toward fingers
                    rawAnchorWorldPos = Vector3.Lerp(palmCenterWorld, (thumbWorld + middleWorld) * 0.5f, 0.35f);
                }
                else
                {
                    // PinchPoint: Midpoint between thumb tip and active pinching finger(s)
                    Vector3 activeTipPos;
                    if (m_IndexPinchLatched && m_MiddlePinchLatched)
                        activeTipPos = (indexWorld + middleWorld) * 0.5f;
                    else if (m_MiddlePinchLatched || (!m_IndexPinchLatched && allowMiddle && distMiddle < distIndex))
                        activeTipPos = middleWorld;
                    else
                        activeTipPos = indexWorld;

                    rawAnchorWorldPos = Vector3.Lerp(thumbWorld, activeTipPos, 0.5f);
                }

                float primaryPinchDist = Mathf.Min(
                    allowIndex ? distIndex : float.MaxValue,
                    allowMiddle ? distMiddle : float.MaxValue);

                Vector3 finalAnchorWorldPos = rawAnchorWorldPos;
                if (updateButtonState)
                {
                    float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                    float openingSpeed = (primaryPinchDist - m_PrevPinchMetricDist) / dt;
                    m_PrevPinchMetricDist = primaryPinchDist;

                    if (m_LockAnchorDuringReleaseOpening && prevPerformed && m_IsPerformed && m_HasValidWristLocalOffset)
                    {
                        bool isOpeningTowardRelease = (isPinchActive && primaryPinchDist > m_ActivateDistance && openingSpeed > 0.025f) ||
                                                      (!isPinchActive && m_PalmGrabLatched && palmCurlMetric > m_PalmActivateDistance);

                        if (isOpeningTowardRelease)
                        {
                            finalAnchorWorldPos = wristWorldPose.position + (wristWorldPose.rotation * m_LockedWristLocalOffset);
                        }
                        else
                        {
                            Vector3 targetLocal = Quaternion.Inverse(wristWorldPose.rotation) * (rawAnchorWorldPos - wristWorldPose.position);
                            m_LockedWristLocalOffset = Vector3.Lerp(m_LockedWristLocalOffset, targetLocal, 0.5f);
                            finalAnchorWorldPos = wristWorldPose.position + (wristWorldPose.rotation * m_LockedWristLocalOffset);
                        }
                    }
                    else
                    {
                        m_LockedWristLocalOffset = Quaternion.Inverse(wristWorldPose.rotation) * (rawAnchorWorldPos - wristWorldPose.position);
                        m_HasValidWristLocalOffset = true;
                    }
                }
                else if (m_HasValidWristLocalOffset)
                {
                    finalAnchorWorldPos = wristWorldPose.position + (wristWorldPose.rotation * m_LockedWristLocalOffset);
                }

                // Apply FilteredWristOffset 1-Euro Filter (from MR-Swarmalators.unity) on the final anchor pose
                double now = Time.realtimeSinceStartupAsDouble;
                float filterDt = m_LastAnchorFilterTime > 0 ? (float)(now - m_LastAnchorFilterTime) : 0f;
                if (m_LastAnchorFilterTime <= 0 || filterDt > 0.1f)
                {
                    m_AnchorPosFilter.Initialize(finalAnchorWorldPos);
                    m_AnchorRotFilter.Initialize(wristWorldPose.rotation);
                    m_LastAnchorFilterTime = now;
                }
                else if (filterDt >= 0.0005f)
                {
                    finalAnchorWorldPos = m_AnchorPosFilter.Filter(
                        finalAnchorWorldPos, filterDt, m_AnchorPosMinCutoff, m_AnchorPosBeta, 1.0f, 0.00035f);
                    wristWorldPose.rotation = m_AnchorRotFilter.Filter(
                        wristWorldPose.rotation, filterDt, m_AnchorRotMinCutoff, m_AnchorRotBeta, 1.0f, 0.20f);
                    m_LastAnchorFilterTime = now;
                }

                m_PhysicalGrabAnchor.SetPositionAndRotation(finalAnchorWorldPos, wristWorldPose.rotation);

                if (updateButtonState)
                {
                    RecordPoseSample(finalAnchorWorldPos, wristWorldPose.rotation);
                }
            }

            return true;
        }

        private bool TryGetPhysicalWristAndPalm(XRHand hand, Transform originTransform, out Pose wristWorldPose, out Vector3 palmCenterWorld)
        {
            wristWorldPose = Pose.identity;
            palmCenterWorld = Vector3.zero;

            var wristJoint = hand.GetJoint(XRHandJointID.Wrist);
            var palmJoint = hand.GetJoint(XRHandJointID.Palm);
            var middleProximal = hand.GetJoint(XRHandJointID.MiddleProximal);
            var thumbMetacarpal = hand.GetJoint(XRHandJointID.ThumbMetacarpal);

            bool hasPalm = palmJoint.TryGetPose(out Pose palmLocalPose);
            bool hasWrist = wristJoint.TryGetPose(out Pose wristLocalPose);

            if (!hasPalm && !hasWrist)
                return false;

            Pose baseLocalPose = hasPalm ? palmLocalPose : wristLocalPose;
            wristWorldPose = new Pose(
                TransformPointToWorld(originTransform, baseLocalPose.position),
                TransformRotationToWorld(originTransform, baseLocalPose.rotation));

            // Replicate XRUtils.GetCenterPalm from human_swarm_ims:
            // CenterPalm = (MiddleProximal + ThumbMetacarpal) * 0.5f
            if (middleProximal.TryGetPose(out Pose midProxPose) && thumbMetacarpal.TryGetPose(out Pose thumbMetaPose))
            {
                Vector3 mpWorld = TransformPointToWorld(originTransform, midProxPose.position);
                Vector3 tmWorld = TransformPointToWorld(originTransform, thumbMetaPose.position);
                palmCenterWorld = (mpWorld + tmWorld) * 0.5f;
            }
            else
            {
                palmCenterWorld = wristWorldPose.position;
            }

            return true;
        }

        private Transform GetOriginTransform()
        {
            if (transform.parent != null && transform.parent.parent != null)
                return transform.parent.parent;
            return null;
        }

        private static Vector3 TransformPointToWorld(Transform originTransform, Vector3 handSpacePos)
        {
            return originTransform != null ? originTransform.TransformPoint(handSpacePos) : handSpacePos;
        }

        private static Quaternion TransformRotationToWorld(Transform originTransform, Quaternion handSpaceRot)
        {
            return originTransform != null ? originTransform.rotation * handSpaceRot : handSpaceRot;
        }

        private void RecordPoseSample(Vector3 worldPos, Quaternion worldRot)
        {
            m_PoseBuffer[m_PoseBufferHead] = new PoseSample
            {
                time = Time.unscaledTime,
                position = worldPos,
                rotation = worldRot
            };
            m_PoseBufferHead = (m_PoseBufferHead + 1) % k_VelocityBufferCapacity;
            if (m_PoseBufferCount < k_VelocityBufferCapacity)
                m_PoseBufferCount++;
        }

        /// <summary>
        /// Computes smoothed hand linear and angular velocity over the [now - 100ms, now - 22ms] window
        /// (matching Meta Interaction SDK's HandVelocityCalculator 0.1s @ 90 Hz buffer while excluding
        /// the final finger-opening frames right at release).
        /// </summary>
        public bool TryGetSmoothedReleaseVelocity(out Vector3 linearVelocity, out Vector3 angularVelocity)
        {
            linearVelocity = Vector3.zero;
            angularVelocity = Vector3.zero;

            if (m_PoseBufferCount < 3)
                return false;

            float now = Time.unscaledTime;
            int newestIdx = (m_PoseBufferHead - 1 + k_VelocityBufferCapacity) % k_VelocityBufferCapacity;
            int endIdx = newestIdx;
            int startIdx = newestIdx;

            for (int i = 0; i < m_PoseBufferCount; i++)
            {
                int idx = (m_PoseBufferHead - 1 - i + k_VelocityBufferCapacity) % k_VelocityBufferCapacity;
                float age = now - m_PoseBuffer[idx].time;

                if (age <= 0.025f)
                {
                    endIdx = idx;
                }
                if (age <= 0.110f)
                {
                    startIdx = idx;
                }
            }

            PoseSample startSample = m_PoseBuffer[startIdx];
            PoseSample endSample = m_PoseBuffer[endIdx];
            float dt = endSample.time - startSample.time;

            if (dt < 0.008f)
            {
                int oldestIdx = (m_PoseBufferHead - m_PoseBufferCount + k_VelocityBufferCapacity) % k_VelocityBufferCapacity;
                startSample = m_PoseBuffer[oldestIdx];
                endSample = m_PoseBuffer[newestIdx];
                dt = endSample.time - startSample.time;
            }

            if (dt < 0.005f)
                return false;

            linearVelocity = (endSample.position - startSample.position) / dt;

            Quaternion deltaRot = endSample.rotation * Quaternion.Inverse(startSample.rotation);
            deltaRot.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (angleDeg > 180f) angleDeg -= 360f;
            if (Mathf.Abs(angleDeg) > 0.01f && !float.IsNaN(axis.x))
            {
                angularVelocity = axis.normalized * (angleDeg * Mathf.Deg2Rad / dt);
            }

            return true;
        }

        #region IXRInputButtonReader Implementation
        public bool ReadIsPerformed() => m_IsPerformed;
        public bool ReadWasPerformedThisFrame() => m_WasPerformedThisFrame;
        public bool ReadWasCompletedThisFrame() => m_WasCompletedThisFrame;
        #endregion

        #region Value Reader Helpers
        public float ReadValue() => m_CurrentValue;

        public bool TryReadValue(out float value)
        {
            value = m_CurrentValue;
            return true;
        }
        #endregion
    }
}
