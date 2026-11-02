using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SpatialMAS.XR
{
    /// <summary>Drives the generated near/far interactor from optical hands or a tracked XR controller.</summary>
    [DefaultExecutionOrder(-30000)]
    public class XRTrackedHandInteractor : MonoBehaviour
    {
        [SerializeField] private XRHandPinchHysteresis _pinch;
        [SerializeField] private Transform _trackingOrigin;
        [SerializeField] private NearFarInteractor _interactor;
        [SerializeField] private LineRenderer _line;
        [SerializeField] private CurveVisualController _visual;
        [SerializeField] private Transform _aimOrigin;
        [SerializeField] private XRHandSkeletonVisual _handVisual;
        public XRHandPinchHysteresis Pinch => _pinch;
        private bool _tracked;

        public void Configure(XRHandPinchHysteresis pinch, Transform trackingOrigin, NearFarInteractor interactor,
            LineRenderer line, CurveVisualController visual)
        {
            _pinch = pinch;
            _trackingOrigin = trackingOrigin;
            _interactor = interactor;
            _line = line;
            _visual = visual;
            EnsureHandSupport();
        }

        private void Awake() => EnsureHandSupport();

        public void EnsureHandSupport()
        {
            if (_pinch == null || _trackingOrigin == null || _interactor == null) return;
            XRInteractionRigSetup.ConfigureRayVisualMaterial(_visual);
            ConfigureGhostRay();
            _pinch.ConfigureTrackingOrigin(_trackingOrigin);
            var uiReader = _pinch.GetComponent<XRHandUISelectReader>();
            if (uiReader == null) uiReader = _pinch.gameObject.AddComponent<XRHandUISelectReader>();
            uiReader.Configure(_pinch);
            _interactor.uiPressInput = XRInteractionRigSetup.ObjectReader("Hand UI Press", uiReader);
            _interactor.farAttachMode = InteractorFarAttachMode.Near;
            if (_interactor.interactionAttachController is InteractionAttachController attach)
            {
                attach.transformToFollow = transform;
                attach.motionStabilizationMode = MotionStabilizationMode.Never;
                attach.useMomentum = false;
                attach.useDistanceBasedVelocityScaling = false;
                attach.smoothOffset = false;
            }
            if (_interactor.nearInteractionCaster is SphereInteractionCaster near) near.castRadius = 0.04f;
            if (_aimOrigin == null)
            {
                _aimOrigin = transform.Find("Aim Pose");
                if (_aimOrigin == null)
                {
                    _aimOrigin = new GameObject("Aim Pose").transform;
                    _aimOrigin.SetParent(transform, false);
                }
            }
            // Near casting/attachment stays at the fingers; the far ray gets the runtime's aim pose.
            if (_interactor.farInteractionCaster != null) _interactor.farInteractionCaster.castOrigin = _aimOrigin;
            if (_handVisual == null)
            {
                string name = _pinch.Handedness + " Tracked Hand Visual";
                var existing = _trackingOrigin.Find(name);
                if (existing == null)
                {
                    existing = new GameObject(name).transform;
                    existing.SetParent(_trackingOrigin, false);
                }
                _handVisual = existing.GetComponent<XRHandSkeletonVisual>();
                if (_handVisual == null) _handVisual = existing.gameObject.AddComponent<XRHandSkeletonVisual>();
            }
            _handVisual.Configure(_pinch);
        }

        private void Update()
        {
            if (_pinch == null) return;
            bool tracked = _pinch.TryGetInteractionPoseWorld(out Pose worldPose);
            _tracked = tracked;
            if (tracked) transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            if (_aimOrigin != null)
            {
                if (_pinch.TryGetAimPoseWorld(out Pose aimPose))
                    _aimOrigin.SetPositionAndRotation(aimPose.position, aimPose.rotation);
                else
                    _aimOrigin.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }
            // Disabling an interactor cancels an active selection when tracking disappears.
            if (_interactor != null && _interactor.enabled != tracked) _interactor.enabled = tracked;
            if (!tracked) SetRayVisible(false);
        }

        private void LateUpdate()
        {
            // Hover/selection is resolved after this driver's early pose update.
            SetRayVisible(_tracked && ShouldShowRayFeedback(_interactor, transform.position));
        }

        private void OnDisable() => SetRayVisible(false);

        private void SetRayVisible(bool visible)
        {
            if (_visual != null) _visual.enabled = visible;
            if (_line != null) _line.enabled = visible;
        }

        public static bool ShouldShowRayFeedback(NearFarInteractor interactor, Vector3 anchor)
        {
            if (interactor == null || !interactor.isActiveAndEnabled || interactor.hasSelection) return false;
            // Menu pointing remains visible; it does not imply an object can be picked up.
            if (interactor.TryGetCurrentUIRaycastResult(out _)) return true;
            if (!interactor.enableFarCasting || interactor.interactionManager == null) return false;
            foreach (var hovered in interactor.interactablesHovered)
            {
                if (!(hovered is IXRSelectInteractable selectable) ||
                    !interactor.interactionManager.IsSelectPossible(interactor, selectable)) continue;
                float distance = float.PositiveInfinity;
                foreach (var collider in hovered.colliders)
                    if (collider != null && collider.enabled)
                        distance = Mathf.Min(distance, Vector3.Distance(anchor, collider.ClosestPoint(anchor)));
                if (!float.IsPositiveInfinity(distance) &&
                    distance >= XRInteractionRigSetup.MinimumDistanceGrabSurfaceDistance) return true;
            }
            return false;
        }

        private void ConfigureGhostRay()
        {
            if (_visual == null) return;
            // Same translucent warm-white gradient and width as the hand interaction sample.
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.78431374f, 0.5686275f), 0.25f),
                new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.49019608f, 0.21177f),
                    new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            _visual.customizeLinePropertiesForState = true;
            foreach (var properties in new[] { _visual.hoverHitProperties, _visual.uiHitProperties,
                _visual.uiPressHitProperties })
            {
                properties.adjustGradient = true;
                properties.gradient = gradient;
                properties.adjustWidth = true;
                properties.startWidth = properties.endWidth = 0.004f;
                properties.lineBendRatio = 0.25f;
            }
            _visual.restingVisualLineLength = 0f;
            _visual.extendLineToEmptyHit = false;
            _visual.extensionRate = 0f;
            _visual.endPointExpansionRate = 0f;
            _visual.linePropertyAnimationSpeed = 0f;
        }
    }
}
