using System.Collections.Generic;
using SpatialMAS.XR;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace SpatialMAS.UI
{
    /// <summary>One cancellable hold timer. A completed/cancelled activation requires a valid contact release.</summary>
    public sealed class BraceletHoldState
    {
        private bool _armed = true;
        public float Elapsed { get; private set; }
        public void Cancel() { Elapsed = 0f; _armed = false; }
        public bool Step(bool eligible, bool contact, float deltaTime, float duration = 1f)
        {
            if (!eligible) { Cancel(); return false; }
            if (!contact) { Elapsed = 0f; _armed = true; return false; }
            if (!_armed) return false;
            Elapsed += Mathf.Max(0f, deltaTime);
            if (Elapsed < Mathf.Max(0.01f, duration)) return false;
            Elapsed = 0f;
            _armed = false;
            return true;
        }
    }

    public sealed class XRBraceletLauncher : MonoBehaviour
    {
        [SerializeField] private float _holdSeconds = 1f;
        [SerializeField] private float _showDistance = 0.1f;
        [SerializeField] private float _surfaceClearance = 0.022f;
        private static readonly Vector2 ButtonSize = new Vector2(0.042f, 0.034f);
        private readonly BraceletHoldState _hold = new BraceletHoldState();
        private readonly List<XRHandSubsystem> _subsystems = new List<XRHandSubsystem>();
        private XRResearcherPanelLifecycle _panel;
        private GameObject _visual;
        private RectTransform _progress;
        private Renderer _bodyRenderer;
        private Material _bodyMaterial;
        private bool _wasEligible;
        private bool _contactLatched;
        private NearFarInteractor[] _interactors = System.Array.Empty<NearFarInteractor>();
        private float _refreshTime;
        public float Progress => Mathf.Clamp01(_hold.Elapsed / Mathf.Max(0.01f, _holdSeconds));

        public void Initialize(XRResearcherPanelLifecycle panel)
        {
            if (_panel != null) _panel.Hidden -= Cancel;
            _panel = panel;
            _panel.Hidden += Cancel;
            BuildVisual();
        }

        private void LateUpdate()
        {
            bool tracked = TryGetPoses(out Pose surface, out Pose index);
            // Reject the palm half-space, allowing lateral/oblique approaches beside
            // the raised body. This uses the hand frame, never world-up or a cone.
            bool reveal = _panel != null && _panel.ResearcherMode && _panel.HeadsetActive && tracked &&
                CanReveal(surface, index.position, _showDistance);
            bool eligible = reveal && !HandsBusy();
            bool touching = eligible && IsSurfaceContact(surface, index.position, _contactLatched);
            _contactLatched = touching;
            _panel?.SetLauncherContact(touching);
            if (_wasEligible && !eligible) _hold.Cancel();
            _wasEligible = eligible;
            bool fire = _hold.Step(eligible, touching, Time.unscaledDeltaTime, _holdSeconds);
            if (_visual != null)
            {
                _visual.SetActive(reveal);
                if (reveal)
                {
                    _visual.transform.SetPositionAndRotation(surface.position, surface.rotation);
                    _progress.sizeDelta = new Vector2(34f * Progress, 3f);
                    if (_bodyMaterial != null) _bodyMaterial.SetColor("_BaseColor", touching ?
                        Color.Lerp(new Color(0.1f, 0.65f, 0.65f), new Color(0.45f, 1f, 0.7f), Progress) : new Color(0.08f, 0.3f, 0.4f));
                }
            }
            if (fire) _panel.ToggleFromLauncher();
        }

        /// <summary>Left-hand geometry, independent of wrist flexion. UI front (-Z) faces the dorsum.</summary>
        public static bool TryBuildSurface(Pose middleMetacarpal, Pose middleProximal,
            Pose indexProximal, Pose littleProximal, float clearance, out Pose surface)
        {
            surface = default;
            Vector3 forward = middleProximal.position - middleMetacarpal.position;
            Vector3 width = indexProximal.position - littleProximal.position;
            if (forward.sqrMagnitude < 0.000001f || width.sqrMagnitude < 0.000001f) return false;
            Vector3 normal = Vector3.Cross(forward, width).normalized;
            if (normal.sqrMagnitude < 0.5f) return false;
            forward = Vector3.ProjectOnPlane(forward, normal).normalized;
            surface = new Pose(Vector3.Lerp(middleMetacarpal.position, middleProximal.position, 0.6f) +
                normal * Mathf.Max(0.012f, clearance), Quaternion.LookRotation(-normal, forward));
            return true;
        }

        /// <summary>Bounded slab above the visible top. Never accepts contact from the palm side.</summary>
        public static bool CanReveal(Pose surface, Vector3 fingertip, float distance = 0.1f)
        {
            Vector3 local = Quaternion.Inverse(surface.rotation) * (fingertip - surface.position);
            return local.sqrMagnitude <= distance * distance && local.z <= 0.016f;
        }

        public static bool IsSurfaceContact(Pose surface, Vector3 fingertip, bool retaining = false)
        {
            Vector3 local = Quaternion.Inverse(surface.rotation) * (fingertip - surface.position);
            // XR IndexTip is a joint center, not the fingertip's skin. Include a
            // 5mm pad radius and 3mm exit hysteresis rather than demand exact overlap.
            float padding = 0.005f + (retaining ? 0.003f : 0f);
            return Mathf.Abs(local.x) <= ButtonSize.x * 0.5f + padding && Mathf.Abs(local.y) <= ButtonSize.y * 0.5f + padding &&
                local.z >= -0.012f - (retaining ? 0.003f : 0f) && local.z <= 0.002f + padding;
        }

        private bool TryGetPoses(out Pose surface, out Pose index)
        {
            surface = index = default;
            SubsystemManager.GetSubsystems(_subsystems);
            foreach (var subsystem in _subsystems)
            {
                if (subsystem == null || !subsystem.running || !subsystem.leftHand.isTracked || !subsystem.rightHand.isTracked) continue;
                XRHand hand = subsystem.leftHand;
                if (!hand.GetJoint(XRHandJointID.MiddleMetacarpal).TryGetPose(out Pose middleMeta) ||
                    !hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out Pose middleProx) ||
                    !hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out Pose indexProx) ||
                    !hand.GetJoint(XRHandJointID.LittleProximal).TryGetPose(out Pose littleProx) ||
                    !subsystem.rightHand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose localIndex)) continue;
                if (!TryBuildSurface(middleMeta, middleProx, indexProx, littleProx, _surfaceClearance, out Pose localSurface)) continue;
                Transform origin = XRInteractionRigSetup.GetTrackingOrigin(Camera.main);
                surface = XRInteractionRigSetup.TrackingPoseToWorld(localSurface, origin);
                index = XRInteractionRigSetup.TrackingPoseToWorld(localIndex, origin);
                return true;
            }
            return false;
        }

        private bool HandsBusy()
        {
            if (Time.unscaledTime >= _refreshTime)
            {
                _interactors = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
                _refreshTime = Time.unscaledTime + 1f;
            }
            foreach (var interactor in _interactors)
                if (interactor != null && interactor.isActiveAndEnabled && interactor.hasSelection) return true;
            // A raw pinch/curl classification is not an occupied hand. The original
            // reference checks selected interactables; hovering an unselected hand
            // near its opposite finger must not cancel a deliberate poke.
            return false;
        }

        private void Cancel()
        {
            _hold.Cancel();
            _wasEligible = false;
            _contactLatched = false;
            _panel?.SetLauncherContact(false);
            if (_visual != null) _visual.SetActive(false);
        }
        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            if (_panel != null) _panel.Hidden -= Cancel;
            if (_bodyMaterial != null) Destroy(_bodyMaterial);
        }

        private void BuildVisual()
        {
            if (_visual != null) return;
            _visual = new GameObject("Left Hand Researcher Button");
            _visual.transform.SetParent(transform, false);
            // The mesh and analytic contact slab share this surface frame. No physics
            // collider is needed, so object rays cannot accidentally select the launcher.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Raised Button Body";
            body.transform.SetParent(_visual.transform, false);
            body.transform.localScale = new Vector3(ButtonSize.x, ButtonSize.y, 0.008f);
            body.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            var collider = body.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            _bodyRenderer = body.GetComponent<Renderer>();
            var source = Resources.Load<Material>("Mat_SpatialMAS_AgentInstanced");
            if (source != null)
            {
                _bodyMaterial = new Material(source);
                _bodyRenderer.sharedMaterial = _bodyMaterial;
                _bodyMaterial.SetColor("_BaseColor", new Color(0.08f, 0.3f, 0.4f));
            }
            var face = new GameObject("Outward Label Surface", typeof(RectTransform), typeof(Canvas));
            face.transform.SetParent(_visual.transform, false);
            face.transform.localScale = Vector3.one * 0.001f;
            face.transform.localPosition = new Vector3(0f, 0f, -0.0006f);
            face.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)face.transform).sizeDelta = new Vector2(42f, 34f);
            var background = new GameObject("Background", typeof(RectTransform), typeof(Image));
            background.transform.SetParent(face.transform, false);
            var bgRect = (RectTransform)background.transform;
            bgRect.sizeDelta = new Vector2(40f, 32f);
            background.GetComponent<Image>().color = new Color(0.06f, 0.2f, 0.25f, 0.85f);
            background.GetComponent<Image>().raycastTarget = false;
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(face.transform, false);
            var label = labelGo.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 8;
            label.alignment = TextAnchor.MiddleCenter;
            label.text = "PANEL\nHold 1s";
            label.raycastTarget = false;
            label.rectTransform.sizeDelta = new Vector2(40f, 24f);
            label.rectTransform.anchoredPosition = new Vector2(0f, 3f);
            var progressGo = new GameObject("Hold Progress", typeof(RectTransform), typeof(Image));
            progressGo.transform.SetParent(face.transform, false);
            _progress = (RectTransform)progressGo.transform;
            _progress.pivot = new Vector2(0f, 0.5f);
            _progress.anchoredPosition = new Vector2(-17f, -12f);
            _progress.sizeDelta = new Vector2(0f, 3f);
            progressGo.GetComponent<Image>().color = new Color(0.4f, 1f, 0.9f, 0.95f);
            progressGo.GetComponent<Image>().raycastTarget = false;
            Material material = Resources.Load<Material>("Mat_SpatialMAS_WorldUI");
            if (material != null) foreach (var graphic in _visual.GetComponentsInChildren<Graphic>()) graphic.material = material;
            _visual.SetActive(false);
        }
    }
}
