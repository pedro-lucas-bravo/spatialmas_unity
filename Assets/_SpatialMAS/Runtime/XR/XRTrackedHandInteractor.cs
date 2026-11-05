using SpatialMAS.UI;
using SpatialMAS.Core;
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
        private LineRenderer _panelEndpoint;
        private bool _endpointShowsMove;
        private SpatialMASConfig _panelMarkerConfig;
        private Gradient _defaultUiRayGradient;
        private Gradient _panelRayGradient;
        private Color _appliedPanelRayTint = Color.white;

        // Sizes are in world meters. The two symbols share a single per-hand renderer.
        private static readonly Vector3[] PanelPoint =
        {
            new(-.0025f, 0f, 0f), new(0f, .0025f, 0f),
            new(.0025f, 0f, 0f), new(0f, -.0025f, 0f), new(-.0025f, 0f, 0f)
        };

        private static readonly Vector3[] PanelMove =
        {
            Vector3.zero, new(0f, .008f, 0f), new(-.003f, .005f, 0f), new(0f, .008f, 0f),
            new(.003f, .005f, 0f), new(0f, .008f, 0f), Vector3.zero,
            new(.008f, 0f, 0f), new(.005f, .003f, 0f), new(.008f, 0f, 0f),
            new(.005f, -.003f, 0f), new(.008f, 0f, 0f), Vector3.zero,
            new(0f, -.008f, 0f), new(.003f, -.005f, 0f), new(0f, -.008f, 0f),
            new(-.003f, -.005f, 0f), new(0f, -.008f, 0f), Vector3.zero,
            new(-.008f, 0f, 0f), new(-.005f, -.003f, 0f), new(-.008f, 0f, 0f),
            new(-.005f, .003f, 0f), new(-.008f, 0f, 0f)
        };

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
            bool visible = _tracked && ShouldShowRayFeedback(_interactor, transform.position);
            SetRayVisible(visible);
            if (visible) UpdatePanelEndpoint();
        }

        private void OnDisable() => SetRayVisible(false);

        private void SetRayVisible(bool visible)
        {
            if (_visual != null) _visual.enabled = visible;
            if (_line != null) _line.enabled = visible;
            if (!visible && _panelEndpoint != null) _panelEndpoint.enabled = false;
            if (!visible) ApplyPanelRayTint(Color.white);
        }

        private void ApplyPanelRayTint(Color tint)
        {
            if (_visual == null || _defaultUiRayGradient == null || tint == _appliedPanelRayTint) return;
            _appliedPanelRayTint = tint;
            Gradient gradient = _defaultUiRayGradient;
            if (tint != Color.white)
            {
                _panelRayGradient ??= new Gradient();
                var colors = _defaultUiRayGradient.colorKeys;
                var alphas = _defaultUiRayGradient.alphaKeys;
                for (int i = 0; i < colors.Length; i++) colors[i].color *= tint;
                for (int i = 0; i < alphas.Length; i++) alphas[i].alpha *= tint.a;
                _panelRayGradient.SetKeys(colors, alphas);
                gradient = _panelRayGradient;
            }
            _visual.uiHitProperties.gradient = gradient;
            _visual.uiPressHitProperties.gradient = gradient;
        }

        private void UpdatePanelEndpoint()
        {
            if (!_interactor.TryGetCurrentUIRaycastResult(out var hit) ||
                hit.gameObject == null || !hit.gameObject.activeInHierarchy)
            {
                if (_panelEndpoint != null) _panelEndpoint.enabled = false;
                ApplyPanelRayTint(Color.white);
                return;
            }

            var canvas = hit.gameObject.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.isActiveAndEnabled || canvas.name != "SpatialResearcherWorldCanvas")
            {
                if (_panelEndpoint != null) _panelEndpoint.enabled = false;
                ApplyPanelRayTint(Color.white);
                return;
            }

            bool move = hit.gameObject.TryGetComponent<XRPanelMoveHandle>(out _);
            if (_panelEndpoint == null)
            {
                var material = Resources.Load<Material>("Mat_SpatialMAS_PanelEndpoint");
                if (material == null) return;
                var endpoint = new GameObject("Researcher Panel Ray Endpoint");
                endpoint.transform.SetParent(transform, false);
                _panelEndpoint = endpoint.AddComponent<LineRenderer>();
                var bootstrapper = FindFirstObjectByType<SpatialMASBootstrapper>();
                _panelMarkerConfig = bootstrapper != null ? bootstrapper.Config : null;
                _panelEndpoint.sharedMaterial = material;
                _panelEndpoint.useWorldSpace = false;
                _panelEndpoint.positionCount = 0;
                _panelEndpoint.startWidth = _panelEndpoint.endWidth = .0012f;
                _panelEndpoint.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _panelEndpoint.receiveShadows = false;
            }

            if (_panelEndpoint.positionCount == 0 || _endpointShowsMove != move)
            {
                var points = move ? PanelMove : PanelPoint;
                _panelEndpoint.positionCount = points.Length;
                _panelEndpoint.SetPositions(points);
                _endpointShowsMove = move;
            }

            float markerSize = _panelMarkerConfig != null ? _panelMarkerConfig.researcherPanelMoveMarkerSize : .032f;
            float stroke = _panelMarkerConfig != null ? _panelMarkerConfig.researcherPanelMoveMarkerStroke : .0026f;

            Vector3 markerScale = Vector3.one * (move ? Mathf.Max(.005f, markerSize) / .016f : 1f);
            float markerStroke = move ? Mathf.Max(.0005f, stroke) : .0012f;
            var palette = _panelMarkerConfig?.researcherPanelPalette;
            ApplyPanelRayTint(palette?.panelRay ?? Color.white);
            var control = hit.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>();

            Color markerColor = move ? palette?.moveMarker ?? new Color32(104, 240, 255, 255)
                : control != null && !control.IsInteractable()
                    ? palette?.disabledEndpoint ?? new Color32(184, 196, 208, 255)
                    : palette?.enabledEndpoint ?? new Color32(244, 246, 248, 255);
            if (_panelEndpoint.transform.localScale != markerScale) _panelEndpoint.transform.localScale = markerScale;
            if (_panelEndpoint.startWidth != markerStroke) _panelEndpoint.startWidth = _panelEndpoint.endWidth = markerStroke;
            if (_panelEndpoint.startColor != markerColor) _panelEndpoint.startColor = _panelEndpoint.endColor = markerColor;

            _panelEndpoint.transform.SetPositionAndRotation(hit.worldPosition, canvas.transform.rotation);
            _panelEndpoint.enabled = true;
        }

        public static bool ShouldShowRayFeedback(NearFarInteractor interactor, Vector3 anchor)
        {
            if (interactor == null || !interactor.isActiveAndEnabled || interactor.hasSelection) return false;
            // Menu pointing remains visible; it does not imply an object can be picked up.
            if (interactor.TryGetCurrentUIRaycastResult(out var uiHit) &&
                uiHit.gameObject != null && uiHit.gameObject.activeInHierarchy) return true;
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
            _defaultUiRayGradient = gradient;
            _appliedPanelRayTint = Color.white;
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
