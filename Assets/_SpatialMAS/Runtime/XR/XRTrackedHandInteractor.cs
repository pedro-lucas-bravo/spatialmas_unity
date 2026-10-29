using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

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
            _pinch.ConfigureTrackingOrigin(_trackingOrigin);
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
            bool tracked = _pinch.TryGetTrackedPoseWorld(out Pose worldPose);
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
            if (_visual != null && _visual.enabled != tracked) _visual.enabled = tracked;
            if (_line != null) _line.enabled = tracked;
        }
    }
}
