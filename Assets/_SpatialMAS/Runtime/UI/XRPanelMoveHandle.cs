using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SpatialMAS.UI
{
    /// <summary>Deliberate UI handle translates a world panel along its initial plane; no grab/telemetry changes.</summary>
    public sealed class XRPanelMoveHandle : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IInitializePotentialDragHandler
    {
        private XRResearcherPanelLifecycle _owner;
        private Plane _plane;
        private Vector3 _offset;
        private int _pointer;
        private bool _dragging;
        private XRPanelFollow _follow;
        private Func<Pose?> _headPose;
        private float _lastPointerYaw, _dragAngle, _initialHeight, _initialSlope, _dragRadius;
        public bool IsDragging => _dragging;
        private IUIInteractor _source;
        private XRUIInputModule _module;
        private XRBaseInteractor _xrSource;
        private readonly XRSelectFilterDelegate _captureFilter = new XRSelectFilterDelegate((_, __) => false);

        public void Initialize(XRResearcherPanelLifecycle owner) => _owner = owner;
        public void ConfigureRadial(XRPanelFollow follow, Func<Pose?> headPose) { _follow = follow; _headPose = headPose; }
        public void OnInitializePotentialDrag(PointerEventData data) => data.useDragThreshold = false;
        public void OnPointerDown(PointerEventData data) => Capture(data);
        public void OnBeginDrag(PointerEventData data) => Capture(data);

        private void Capture(PointerEventData data)
        {
            if (_owner == null || !_owner.InputReady || _dragging || data.button != PointerEventData.InputButton.Left) return;
            var tracked = data as TrackedDeviceEventData;
            var source = tracked?.interactor;
            if (source is Behaviour behaviour && !behaviour.isActiveAndEnabled) return;
            if (source is XRBaseInteractor xr && xr.hasSelection) return;
            _plane = new Plane(_owner.Panel.forward, _owner.Panel.position);
            if (_follow != null)
            {
                Pose? head = _headPose?.Invoke();
                if (!head.HasValue || !TryDataRay(data, out Ray ray) || !TryAngles(ray, out _lastPointerYaw, out _initialSlope)) return;
                Vector3 relative = Vector3.ProjectOnPlane(_owner.Panel.position - head.Value.position, Vector3.up);
                _dragAngle = Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg;
                _dragRadius = relative.magnitude;
                _initialHeight = _owner.Panel.position.y;
            }
            else
            {
                if (!TryHit(data, out Vector3 hit)) return;
                _offset = _owner.Panel.position - hit;
            }
            _pointer = data.pointerId;
            _source = source;
            _module = data.currentInputModule as XRUIInputModule;
            _xrSource = source as XRBaseInteractor;
            // A held UI press must not turn into an object grab after hover exits.
            if (_xrSource != null) _xrSource.selectFilters.Add(_captureFilter);
            _dragging = true;
        }

        private void LateUpdate()
        {
            if (!_dragging) return;
            if (_owner == null || !_owner.InputReady) { CancelCapture(); return; }
            if (_follow != null && !_headPose().HasValue) { CancelCapture(); return; }
            if (_source == null) return; // Mouse/event-driven input uses OnDrag and pointer-up.
            if ((_source is Behaviour source && !source.isActiveAndEnabled) ||
                _module == null || !_module.isActiveAndEnabled ||
                !_module.GetTrackedDeviceModel(_source, out TrackedDeviceModel model) ||
                model.pointerId != _pointer || !model.select || (_xrSource != null && _xrSource.hasSelection))
            { CancelCapture(); return; }
            // XRI may stop OnDrag events when no canvas is hit and screen position
            // stops changing. The captured input model keeps updating independently.
            if (_follow != null)
            {
                if (TryPointsRay(model.raycastPoints, out Ray ray)) MoveRadial(ray);
            }
            else if (TryRayHit(model.raycastPoints, out Vector3 hit)) _owner.Panel.position = hit + _offset;
        }

        public void OnDrag(PointerEventData data)
        {
            if (!_dragging || data.pointerId != _pointer) return;
            if (_owner == null || !_owner.InputReady) { CancelCapture(); return; }
            if (_source != null) return; // LateUpdate owns tracked movement.
            if (_follow != null)
            {
                if (TryDataRay(data, out Ray ray)) MoveRadial(ray);
            }
            else if (TryHit(data, out Vector3 hit)) _owner.Panel.position = hit + _offset;
            // A parallel/behind-plane ray pauses movement without surrendering capture.
        }

        public void OnPointerUp(PointerEventData data) { if (data.pointerId == _pointer) CancelCapture(); }
        public void OnEndDrag(PointerEventData data) { if (data.pointerId == _pointer) CancelCapture(); }
        private void OnDisable() => CancelCapture();

        private void CancelCapture()
        {
            if (_xrSource != null) _xrSource.selectFilters.Remove(_captureFilter);
            _dragging = false;
            _source = null;
            _module = null;
            _xrSource = null;
        }

        private static bool TryAngles(Ray ray, out float yaw, out float slope)
        {
            Vector3 direction = ray.direction.normalized;
            float horizontal = new Vector2(direction.x, direction.z).magnitude;
            yaw = slope = 0f;
            if (horizontal < .1f) return false;
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            slope = Mathf.Clamp(direction.y / horizontal, -2f, 2f);
            return true;
        }

        private void MoveRadial(Ray ray)
        {
            Pose? head = _headPose?.Invoke();
            if (!head.HasValue) { CancelCapture(); return; }
            if (!TryAngles(ray, out float yaw, out float slope)) return;
            _dragAngle += Mathf.DeltaAngle(_lastPointerYaw, yaw);
            _lastPointerYaw = yaw;
            // An old pinned pose can be off-radius after walking. Ease onto the
            // configured arc rather than teleporting it on the initial press.
            _dragRadius = Mathf.Lerp(_dragRadius, _follow.Radius, 1f - Mathf.Exp(-Time.unscaledDeltaTime / .25f));
            Vector3 radial = Quaternion.Euler(0f, _dragAngle, 0f) * Vector3.forward;
            Vector3 position = head.Value.position + radial * _dragRadius;
            position.y = _initialHeight + (slope - _initialSlope) * _follow.Radius;
            _owner.Panel.SetPositionAndRotation(position, Quaternion.LookRotation(radial, Vector3.up));
        }

        private static bool TryPointsRay(IReadOnlyList<Vector3> points, out Ray ray)
        {
            ray = default;
            if (points == null || points.Count < 2) return false;
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 direction = points[i] - points[0];
                if (direction.sqrMagnitude < .000001f) continue;
                ray = new Ray(points[0], direction.normalized);
                return true;
            }
            return false;
        }

        private static bool TryDataRay(PointerEventData data, out Ray ray)
        {
            if (data is TrackedDeviceEventData tracked) return TryPointsRay(tracked.rayPoints, out ray);
            Camera camera = data.pressEventCamera != null ? data.pressEventCamera : Camera.main;
            ray = camera == null ? default : camera.ScreenPointToRay(data.position);
            return camera != null;
        }

        private bool TryRayHit(IReadOnlyList<Vector3> points, out Vector3 hit)
        {
            hit = default;
            if (points == null || points.Count < 2) return false;
            Vector3 start = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 direction = points[i] - start;
                if (direction.sqrMagnitude < 0.000001f) continue;
                // Capture uses an infinite aiming ray, independent of visual length
                // or whether the pointer still hits the panel's current rectangle.
                return TryPlaneHit(new Ray(start, direction.normalized), out hit);
            }
            return false;
        }

        private bool TryPlaneHit(Ray ray, out Vector3 hit)
        {
            hit = default;
            if (!_plane.Raycast(ray, out float distance) || distance < 0f) return false;
            hit = ray.GetPoint(distance);
            return true;
        }

        private bool TryHit(PointerEventData data, out Vector3 hit)
        {
            hit = default;
            if (data is TrackedDeviceEventData tracked) return TryRayHit(tracked.rayPoints, out hit);
            Camera camera = data.pressEventCamera != null ? data.pressEventCamera : Camera.main;
            if (camera == null) return false;
            return TryPlaneHit(camera.ScreenPointToRay(data.position), out hit);
        }
    }
}
