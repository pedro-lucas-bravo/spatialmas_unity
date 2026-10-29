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

        public void Configure(XRHandPinchHysteresis pinch, Transform trackingOrigin, NearFarInteractor interactor,
            LineRenderer line, CurveVisualController visual)
        {
            _pinch = pinch;
            _trackingOrigin = trackingOrigin;
            _interactor = interactor;
            _line = line;
            _visual = visual;
        }

        private void Update()
        {
            if (_pinch == null) return;
            bool tracked = _pinch.TryGetTrackedPoseWorld(out Pose worldPose);
            if (tracked) transform.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            // Disabling an interactor cancels an active selection when tracking disappears.
            if (_interactor != null && _interactor.enabled != tracked) _interactor.enabled = tracked;
            if (_visual != null && _visual.enabled != tracked) _visual.enabled = tracked;
            if (_line != null) _line.enabled = tracked;
        }
    }
}
