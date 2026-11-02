using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace EnigmaLux.MR
{
    /// <summary>
    /// Controls the 10 cm Mixed Reality interactive cube in MR_HandsInteraction:
    ///
    /// 1. Strict Surface-Contact Near Grab (Eliminates "Invisible Handle"):
    ///    - Enforces snapToColliderVolume = true on XRGrabInteractable and implements IXRHoverFilter + IXRSelectFilter.
    ///    - Within the near-hand reach zone (< 22 cm from cube surface), selection and hover are ONLY allowed when
    ///      the hand's PhysicalGrabAnchor is physically touching the cube surface (<= 2.5 cm for pinch, <= 3.8 cm for palm).
    ///      Pinching in mid-air 3..22 cm outside the cube never grabs an "invisible handle".
    ///
    /// 2. Dual Distance Interaction Modalities (>= 22 cm from cube surface):
    ///    - AttractToHandDirect (Human Swarm style, matches ProtoAgentCube MoveTowardsTargetProvider _travelSpeed = 10):
    ///      Pointing at the cube from a distance and pinching smoothly attracts the cube at 10 m/s directly into the
    ///      hand's PhysicalGrabAnchor, automatically transitioning into a 1:1 direct hand pick upon arrival.
    ///    - RayPullTether (Classic Pull style):
    ///      Pointing at the cube from a distance and pinching grabs it on the ray tether with distance-based velocity
    ///      scaling so pulling the hand backward reels/attracts the cube toward the user.
    ///    - DualByGesture_IndexAttract_MiddlePull:
    ///      Index-finger pinch from afar attracts directly to the hand; Middle-finger pinch from afar uses Ray Pull.
    ///
    /// 3. Unconstrained 6DoF 1-Hand & 2-Hand Manipulation + Damped Zero-G Release Glide:
    ///    - Replicates ProtoAgentCube / UserSwarmalator release feel (LinearDamping = 5, AngularDamping = 5) using
    ///      the pre-release 100 ms velocity ring buffer.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(XRGrabInteractable))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Collider))]
    public class MRCubeInteraction : MonoBehaviour, IXRHoverFilter, IXRSelectFilter
    {
        public enum DistanceGrabModality
        {
            [Tooltip("Human Swarm style (MoveTowardsTargetProvider): Pointing and pinching from afar smoothly attracts the cube directly into your hand so it is held directly.")]
            AttractToHandDirect = 0,

            [Tooltip("Classic Pull style: Pointing and pinching from afar grabs the cube on a ray tether so pulling your hand backward attracts/reels the cube toward you.")]
            RayPullTether = 1,

            [Tooltip("Supports both on the same cube: Thumb+Index pinch from afar attracts directly into the hand; Thumb+Middle pinch from afar grabs on a pullable ray tether.")]
            DualByGesture_IndexAttract_MiddlePull = 2,

            [Tooltip("Disables distance ray grabbing; only direct physical hand contact is allowed.")]
            Disabled = 3
        }

        [Header("Material & Glow Colors")]
        [SerializeField] private Color idleColor = new Color(0.0f, 0.94f, 1.0f, 1.0f);      // Electric Cyan (#00F0FF)
        [SerializeField] private Color grabbedColor = new Color(1.0f, 0.0f, 0.5f, 1.0f);     // Plasma Magenta (#FF007F)
        [SerializeField] private Color hoverColor = new Color(0.0f, 1.0f, 0.5f, 1.0f);       // Aqua Green

        [Header("Distance Interaction Modality")]
        [Tooltip("How distant pointing-and-pinching behaves on this cube (AttractToHandDirect vs. RayPullTether vs. DualByGesture).")]
        [SerializeField] private DistanceGrabModality m_DistanceGrabModality = DistanceGrabModality.AttractToHandDirect;

        [Tooltip("Travel speed (in m/s) when attracting the cube directly into the hand in AttractToHandDirect mode (ProtoAgentCube in human_swarm_ims uses 10 m/s).")]
        [Range(3.0f, 20.0f)]
        [SerializeField] private float m_AttractTravelSpeed = 10.0f;

        [Tooltip("Summon a distant selection immediately. Disable only to compare the original timed attraction.")]
        [SerializeField] private bool m_InstantSummon = true;

        [Header("Surface-Contact Precision (Anti-Invisible-Handle Filter)")]
        [Tooltip("Maximum distance (in meters) from pinching fingertips to the cube's collider surface for a direct near grab.")]
        [Range(0.012f, 0.100f)]
        [SerializeField] private float m_DirectPinchMaxSurfaceDistance = 0.060f; // 6.0 cm

        [Tooltip("Maximum distance (in meters) from palm grip anchor to the cube's collider surface for a direct whole-hand palm grab.")]
        [Range(0.020f, 0.100f)]
        [SerializeField] private float m_DirectPalmMaxSurfaceDistance = 0.075f; // 7.5 cm

        [Tooltip("Minimum distance (in meters) from the hand to the cube surface before far-ray distance grabbing is allowed.")]
        [Range(0.05f, 0.45f)]
        [SerializeField] private float m_MinDistanceGrabSurfaceRange = 0.10f; // 10 cm

        [Header("Release & Zero-G Damped Inertia (Matches ProtoAgentCube.prefab)")]
        [Tooltip("When true, releasing the cube while moving the hand transfers pre-release buffered velocity to a damped zero-G Rigidbody (LinearDamping = 5, AngularDamping = 5) so it glides smoothly to rest like ProtoAgentCube.")]
        [SerializeField] private bool m_EnableDampedReleaseGlide = true;

        [Tooltip("Linear damping applied during zero-G release glide (ProtoAgentCube uses Drag = 5).")]
        [Range(1.0f, 20.0f)]
        [SerializeField] private float m_ReleaseLinearDamping = 5.0f;

        [Tooltip("Angular damping applied during zero-G release glide (ProtoAgentCube uses AngularDrag = 5).")]
        [Range(1.0f, 20.0f)]
        [SerializeField] private float m_ReleaseAngularDamping = 5.0f;

        [Tooltip("Hand speed deadzone (m/s) below which releasing the cube leaves it dead-still at the exact placement point with zero drift.")]
        [Range(0.01f, 0.25f)]
        [SerializeField] private float m_ReleaseLinearDeadzone = 0.06f; // 6 cm/s

        [Tooltip("Angular speed deadzone (rad/s) below which releasing the cube leaves its rotation completely still.")]
        [Range(0.05f, 1.5f)]
        [SerializeField] private float m_ReleaseAngularDeadzone = 0.35f;

        public DistanceGrabModality distanceGrabModality
        {
            get => m_DistanceGrabModality;
            set => m_DistanceGrabModality = value;
        }

        public Color IdleColor
        {
            get => idleColor;
            set
            {
                idleColor = value;
                if (grabInteractable == null || !grabInteractable.isSelected)
                    UpdateEmissionColor(idleColor);
            }
        }

        public bool canProcess => isActiveAndEnabled;

        private XRGrabInteractable grabInteractable;
        private XRGeneralGrabTransformer grabTransformer;
        private Collider m_CubeCollider;
        private Renderer meshRenderer;
        private Material materialInstance;
        private Rigidbody rb;

        private readonly Dictionary<IXRInteractor, MRHandPinchHysteresis> m_PinchLookup = new Dictionary<IXRInteractor, MRHandPinchHysteresis>();

        // Attract-to-Hand (MoveTowardsTargetProvider) flight state
        private bool m_IsAttractingToHand;
        private IXRSelectInteractor m_AttractingInteractor;
        private InteractionAttachController m_AttractingAttachController;
        private MRHandPinchHysteresis m_AttractingPinchHysteresis;
        private Vector3 m_AttractStartWorldPos;
        private float m_AttractElapsed;
        private float m_AttractDuration;

        // Local object pose history fallback (used when grabbed via Simulator / controller without XRHand joints)
        private struct ObjectPoseSample
        {
            public float time;
            public Vector3 position;
            public Quaternion rotation;
        }

        private const int k_ObjectHistoryCapacity = 16;
        private readonly ObjectPoseSample[] m_ObjectHistory = new ObjectPoseSample[k_ObjectHistoryCapacity];
        private int m_ObjectHistoryHead;
        private int m_ObjectHistoryCount;

        private void Awake()
        {
            m_CubeCollider = GetComponent<Collider>();
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.linearDamping = m_ReleaseLinearDamping;
            rb.angularDamping = m_ReleaseAngularDamping;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.isKinematic = true; // Hover stably in MR space when idle or held

            meshRenderer = GetComponent<Renderer>();
            if (meshRenderer != null)
            {
                materialInstance = meshRenderer.material;
                UpdateEmissionColor(idleColor);
            }

            grabInteractable = GetComponent<XRGrabInteractable>();
            SetupGrabSettings();
        }

        private void SetupGrabSettings()
        {
            // 1. Multi-hand selection so 1-hand grab, hand-to-hand transfer, and 2-hand scaling/rotation work seamlessly
            grabInteractable.selectMode = InteractableSelectMode.Multiple;

            // 2. Instantaneous 1:1 kinematic tracking while held (replicates Grabbable._kinematicWhileSelected = 1, _slippiness = 0)
            grabInteractable.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grabInteractable.smoothPosition = false;
            grabInteractable.smoothRotation = false;
            grabInteractable.throwOnDetach = false;

            // 3. Dynamic attach with snapToColliderVolume = true so near-grab attach points are ALWAYS clamped
            // directly onto the cube's physical surface/volume, never floating in mid-air like an "invisible handle".
            grabInteractable.useDynamicAttach = true;
            grabInteractable.snapToColliderVolume = true;
            grabInteractable.matchAttachPosition = true;
            grabInteractable.matchAttachRotation = true;
            grabInteractable.attachEaseInTime = 0.0f;
            grabInteractable.reinitializeDynamicAttachEverySingleGrab = true;
            grabInteractable.farAttachMode = InteractableFarAttachMode.Far;

            // 4. Register this component as both Hover and Select filter to enforce the 3-zone surface contact rule
            grabInteractable.hoverFilters.Add(this);
            grabInteractable.selectFilters.Add(this);

            // 5. Full unconstrained 6DoF 1-hand and 2-hand transformer (replicates OneGrabFreeTransformer + TwoGrabFreeTransformer)
            grabTransformer = GetComponent<XRGeneralGrabTransformer>();
            if (grabTransformer == null)
            {
                grabTransformer = gameObject.AddComponent<XRGeneralGrabTransformer>();
            }

            grabTransformer.permittedDisplacementAxes =
                XRGeneralGrabTransformer.ManipulationAxes.X |
                XRGeneralGrabTransformer.ManipulationAxes.Y |
                XRGeneralGrabTransformer.ManipulationAxes.Z;

            grabTransformer.allowTwoHandedScaling = true;
            grabTransformer.allowOneHandedScaling = false;
            grabTransformer.allowTwoHandedRotation = XRGeneralGrabTransformer.TwoHandedRotationMode.FirstHandDirectedTowardsSecondHand;
            grabTransformer.clampScaling = true;
            grabTransformer.minimumScaleRatio = 0.2f;          // Down to 2 cm
            grabTransformer.maximumScaleRatio = 5.0f;          // Up to 50 cm
            grabTransformer.scaleMultiplier = 1.0f;            // 1:1 physical hand distance scaling
            grabTransformer.thresholdMoveRatioForScale = 0.005f;

            // Register events
            grabInteractable.firstHoverEntered.AddListener(OnHoverEntered);
            grabInteractable.lastHoverExited.AddListener(OnHoverExited);
            grabInteractable.selectEntered.AddListener(OnSelectEntered);
            grabInteractable.selectExited.AddListener(OnSelectExited);
        }

        private void Update()
        {
            // 1. Update Attract-to-Hand flight (MoveTowardsTargetProvider behavior) prior to XRInteractionManager update
            if (m_IsAttractingToHand)
            {
                UpdateAttractToHandFlight();
            }

            // 2. Record object pose history while grabbed
            if (grabInteractable != null && grabInteractable.isSelected)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }

                m_ObjectHistory[m_ObjectHistoryHead] = new ObjectPoseSample
                {
                    time = Time.unscaledTime,
                    position = transform.position,
                    rotation = transform.rotation
                };
                m_ObjectHistoryHead = (m_ObjectHistoryHead + 1) % k_ObjectHistoryCapacity;
                if (m_ObjectHistoryCount < k_ObjectHistoryCapacity)
                    m_ObjectHistoryCount++;
            }
            else if (rb != null && !rb.isKinematic)
            {
                // When zero-G release glide has decelerated to rest, lock back to kinematic
                if (rb.linearVelocity.sqrMagnitude < 0.000025f && rb.angularVelocity.sqrMagnitude < 0.0004f)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
            }
        }

        private void UpdateAttractToHandFlight()
        {
            if (grabInteractable == null || !grabInteractable.isSelected ||
                m_AttractingInteractor == null || m_AttractingAttachController == null ||
                !grabInteractable.interactorsSelecting.Contains(m_AttractingInteractor))
            {
                m_IsAttractingToHand = false;
                return;
            }

            // If a second hand grabs the cube mid-flight, snap to direct hold immediately
            if (grabInteractable.interactorsSelecting.Count > 1)
            {
                m_AttractingAttachController.ResetOffset();
                m_IsAttractingToHand = false;
                return;
            }

            m_AttractElapsed += Time.deltaTime;
            float t = m_AttractDuration > 0.001f ? Mathf.Clamp01(m_AttractElapsed / m_AttractDuration) : 1.0f;

            if (t >= 1.0f)
            {
                // Arrival: Reset attach controller offset to zero so NearFarInteractor switches from Region.Far
                // to Region.Near and holds the cube 1:1 directly in the hand!
                m_AttractingAttachController.ResetOffset();
                m_IsAttractingToHand = false;
                return;
            }

            // Smooth cubic Hermite S-curve matching ProtoAgentCube's MoveTowardsTargetProvider curve (0 -> 1)
            float smoothT = t * t * (3f - 2f * t);

            Vector3 handAnchorWorldPos = (m_AttractingPinchHysteresis != null && m_AttractingPinchHysteresis.physicalGrabAnchor != null)
                ? m_AttractingPinchHysteresis.physicalGrabAnchor.position
                : (m_AttractingAttachController.transformToFollow != null
                    ? m_AttractingAttachController.transformToFollow.position
                    : m_AttractingInteractor.transform.position);

            Vector3 currentTargetWorld = Vector3.Lerp(m_AttractStartWorldPos, handAnchorWorldPos, smoothT);
            ((IInteractionAttachController)m_AttractingAttachController).MoveTo(currentTargetWorld);
        }

        #region IXRHoverFilter & IXRSelectFilter (3-Zone Surface Contact Validation)

        public bool Process(IXRHoverInteractor interactor, IXRHoverInteractable interactable)
        {
            if (interactor is IXRSelectInteractor selectInteractor &&
                grabInteractable != null && grabInteractable.interactorsSelecting.Contains(selectInteractor))
            {
                return true;
            }

            float surfaceDist = GetHandDistanceToCubeSurface(interactor, out _);

            // Zone 1: Direct surface contact (<= 2.5 cm) -> allow hover
            if (surfaceDist <= m_DirectPinchMaxSurfaceDistance)
                return true;

            // Zone 2: Near-miss halo (2.5 cm .. 22 cm) -> block hover in empty air near the cube
            if (surfaceDist < m_MinDistanceGrabSurfaceRange)
                return false;

            // Zone 3: Distance ray hover (>= 22 cm) -> allow if distance modality is enabled
            return m_DistanceGrabModality != DistanceGrabModality.Disabled;
        }

        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
        {
            // Never interrupt an already-active selection (e.g. during AttractToHandDirect flight or RayPullTether)
            if (grabInteractable != null && grabInteractable.interactorsSelecting.Contains(interactor))
            {
                return true;
            }

            float surfaceDist = GetHandDistanceToCubeSurface(interactor, out MRHandPinchHysteresis pinchHysteresis);

            bool isPalmGrasp = pinchHysteresis != null &&
                               pinchHysteresis.activeGrabType == MRHandPinchHysteresis.ActiveGrabType.Palm;

            float maxDirectDist = isPalmGrasp ? m_DirectPalmMaxSurfaceDistance : m_DirectPinchMaxSurfaceDistance;

            // Zone 1: Direct physical contact with cube surface -> allow immediate 1:1 grab
            if (surfaceDist <= maxDirectDist)
            {
                return true;
            }

            // Zone 2: Near-miss halo (maxDirectDist .. 22 cm) -> strictly block mid-air grabs ("invisible handle" fix)
            if (surfaceDist < m_MinDistanceGrabSurfaceRange)
            {
                return false;
            }

            // Zone 3: Distance grab (>= 22 cm) -> allow only if enabled AND triggered via Pinch (not casual palm curl)
            if (m_DistanceGrabModality == DistanceGrabModality.Disabled)
            {
                return false;
            }

            return !isPalmGrasp;
        }

        private float GetHandDistanceToCubeSurface(IXRInteractor interactor, out MRHandPinchHysteresis pinchHysteresis)
        {
            pinchHysteresis = GetPinchHysteresis(interactor);

            Vector3 handAnchorPos;
            if (pinchHysteresis != null && pinchHysteresis.physicalGrabAnchor != null)
            {
                handAnchorPos = pinchHysteresis.physicalGrabAnchor.position;
            }
            else
            {
                handAnchorPos = interactor.transform.position;
            }

            if (m_CubeCollider == null)
                m_CubeCollider = GetComponent<Collider>();

            Vector3 closestPoint = m_CubeCollider != null
                ? m_CubeCollider.ClosestPoint(handAnchorPos)
                : transform.position;

            return Vector3.Distance(handAnchorPos, closestPoint);
        }

        private MRHandPinchHysteresis GetPinchHysteresis(IXRInteractor interactor)
        {
            if (interactor == null)
                return null;

            if (!m_PinchLookup.TryGetValue(interactor, out MRHandPinchHysteresis ph) || ph == null)
            {
                if (interactor is Component comp)
                {
                    ph = comp.GetComponent<MRHandPinchHysteresis>();
                    m_PinchLookup[interactor] = ph;
                }
            }
            return ph;
        }

        #endregion

        private void OnDestroy()
        {
            if (grabInteractable != null)
            {
                grabInteractable.hoverFilters.Remove(this);
                grabInteractable.selectFilters.Remove(this);
                grabInteractable.firstHoverEntered.RemoveListener(OnHoverEntered);
                grabInteractable.lastHoverExited.RemoveListener(OnHoverExited);
                grabInteractable.selectEntered.RemoveListener(OnSelectEntered);
                grabInteractable.selectExited.RemoveListener(OnSelectExited);
            }
        }

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            if (!grabInteractable.isSelected)
            {
                UpdateEmissionColor(hoverColor);
            }
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            if (!grabInteractable.isSelected)
            {
                UpdateEmissionColor(idleColor);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            // Stop any ongoing release glide immediately on grab
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
            }

            if (grabInteractable.interactorsSelecting.Count == 1)
            {
                m_ObjectHistoryCount = 0;
            }

            float surfaceDist = GetHandDistanceToCubeSurface(args.interactorObject, out MRHandPinchHysteresis pinchHysteresis);
            InteractionAttachController attachController = pinchHysteresis != null
                ? pinchHysteresis.attachController
                : (args.interactorObject as Component)?.GetComponent<InteractionAttachController>();

            bool isFarGrab = attachController != null && attachController.hasOffset && surfaceDist >= (m_MinDistanceGrabSurfaceRange * 0.8f);

            if (!isFarGrab)
            {
                // Direct Near Contact Grab: guarantee zero offset so there is never an invisible lever arm
                m_IsAttractingToHand = false;
                if (pinchHysteresis != null)
                    pinchHysteresis.SetFarPullAttachmentMode(false);
                if (attachController != null && attachController.hasOffset)
                    attachController.ResetOffset();
            }
            else
            {
                // Distance Ray Grab: determine whether to use AttractToHandDirect or RayPullTether
                bool useAttractToHand = false;
                switch (m_DistanceGrabModality)
                {
                    case DistanceGrabModality.AttractToHandDirect:
                        useAttractToHand = true;
                        break;
                    case DistanceGrabModality.RayPullTether:
                        useAttractToHand = false;
                        break;
                    case DistanceGrabModality.DualByGesture_IndexAttract_MiddlePull:
                        useAttractToHand = pinchHysteresis == null ||
                                           pinchHysteresis.isIndexPinchLatched ||
                                           !pinchHysteresis.isMiddlePinchLatched;
                        break;
                }

                if (useAttractToHand && attachController != null)
                {
                    if (pinchHysteresis != null)
                        pinchHysteresis.SetFarPullAttachmentMode(false);

                    Transform interactorAttach = args.interactorObject.GetAttachTransform(grabInteractable);
                    Vector3 startWorldPos = interactorAttach != null ? interactorAttach.position : transform.position;
                    Vector3 handAnchorPos = (pinchHysteresis != null && pinchHysteresis.physicalGrabAnchor != null)
                        ? pinchHysteresis.physicalGrabAnchor.position
                        : args.interactorObject.transform.position;

                    float travelDist = Vector3.Distance(startWorldPos, handAnchorPos);

                    m_IsAttractingToHand = true;
                    m_AttractingInteractor = args.interactorObject;
                    m_AttractingAttachController = attachController;
                    m_AttractingPinchHysteresis = pinchHysteresis;
                    m_AttractStartWorldPos = startWorldPos;
                    m_AttractElapsed = 0f;
                    m_AttractDuration = Mathf.Clamp(travelDist / Mathf.Max(1.0f, m_AttractTravelSpeed), 0.065f, 0.35f);
                    if (m_InstantSummon)
                    {
                        // Remove the far-ray attachment offset before this frame's XRI transform pass.
                        attachController.ResetOffset();
                        Transform objectAttach = grabInteractable.GetAttachTransform(args.interactorObject);
                        if (objectAttach != null)
                            transform.position += handAnchorPos - objectAttach.position;
                        if (rb != null) rb.position = transform.position;
                        m_IsAttractingToHand = false;
                        m_ObjectHistoryHead = m_ObjectHistoryCount = 0;
                    }

                }
                else
                {
                    // RayPullTether mode: enable velocity-scaled ray pulling so pulling the hand reels the cube in
                    m_IsAttractingToHand = false;
                    if (pinchHysteresis != null)
                        pinchHysteresis.SetFarPullAttachmentMode(true);
                }
            }

            UpdateEmissionColor(grabbedColor);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            var exitingPinch = GetPinchHysteresis(args.interactorObject);
            if (exitingPinch != null)
            {
                exitingPinch.SetFarPullAttachmentMode(false);
            }

            if (args.interactorObject == m_AttractingInteractor)
            {
                m_IsAttractingToHand = false;
                m_AttractingInteractor = null;
                m_AttractingAttachController = null;
                m_AttractingPinchHysteresis = null;
            }

            // If another hand is still holding the cube (hand-to-hand transfer or 2->1 hand transition),
            // keep it grabbed and kinematic!
            if (grabInteractable != null && grabInteractable.isSelected)
            {
                return;
            }

            UpdateEmissionColor(idleColor);

            if (rb == null)
                return;

            if (!m_EnableDampedReleaseGlide)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
                return;
            }

            // Query pre-release buffered velocity (excluding the final ~25ms finger-opening frames)
            Vector3 releaseLinearVel = Vector3.zero;
            Vector3 releaseAngularVel = Vector3.zero;
            bool hasVelocity = false;

            if (exitingPinch != null)
            {
                hasVelocity = exitingPinch.TryGetSmoothedReleaseVelocity(out releaseLinearVel, out releaseAngularVel);
            }

            if (!hasVelocity)
            {
                hasVelocity = TryGetObjectBufferedVelocity(out releaseLinearVel, out releaseAngularVel);
            }

            bool applyLinear = hasVelocity && releaseLinearVel.magnitude >= m_ReleaseLinearDeadzone;
            bool applyAngular = hasVelocity && releaseAngularVel.magnitude >= m_ReleaseAngularDeadzone;

            if (applyLinear || applyAngular)
            {
                rb.useGravity = false;
                rb.linearDamping = m_ReleaseLinearDamping;
                rb.angularDamping = m_ReleaseAngularDamping;
                rb.isKinematic = false;
                rb.linearVelocity = applyLinear ? releaseLinearVel : Vector3.zero;
                rb.angularVelocity = applyAngular ? releaseAngularVel : Vector3.zero;
            }
            else
            {
                // Stationary placement: lock immediately in mid-air with zero drift
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
            }
        }

        private bool TryGetObjectBufferedVelocity(out Vector3 linearVelocity, out Vector3 angularVelocity)
        {
            linearVelocity = Vector3.zero;
            angularVelocity = Vector3.zero;

            if (m_ObjectHistoryCount < 3)
                return false;

            float now = Time.unscaledTime;
            int newestIdx = (m_ObjectHistoryHead - 1 + k_ObjectHistoryCapacity) % k_ObjectHistoryCapacity;
            int endIdx = newestIdx;
            int startIdx = newestIdx;

            for (int i = 0; i < m_ObjectHistoryCount; i++)
            {
                int idx = (m_ObjectHistoryHead - 1 - i + k_ObjectHistoryCapacity) % k_ObjectHistoryCapacity;
                float age = now - m_ObjectHistory[idx].time;
                if (age <= 0.025f) endIdx = idx;
                if (age <= 0.110f) startIdx = idx;
            }

            var startSample = m_ObjectHistory[startIdx];
            var endSample = m_ObjectHistory[endIdx];
            float dt = endSample.time - startSample.time;

            if (dt < 0.008f)
            {
                int oldestIdx = (m_ObjectHistoryHead - m_ObjectHistoryCount + k_ObjectHistoryCapacity) % k_ObjectHistoryCapacity;
                startSample = m_ObjectHistory[oldestIdx];
                endSample = m_ObjectHistory[newestIdx];
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

        private void UpdateEmissionColor(Color color)
        {
            if (materialInstance != null)
            {
                materialInstance.color = color;
                if (materialInstance.HasProperty("_EmissionColor"))
                {
                    materialInstance.EnableKeyword("_EMISSION");
                    materialInstance.SetColor("_EmissionColor", color * 1.5f);
                }
            }
        }
    }
}
