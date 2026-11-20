using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SpatialMAS.Core
{
    /// <summary>A rigid reference frame expressed in Unity-world meters; never a parent of simulation objects.</summary>
    public readonly struct CalibrationSnapshot
    {
        public readonly string CalibrationId;
        public readonly int Revision;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly bool Visible;
        public readonly bool Locked;

        public CalibrationSnapshot(string calibrationId, int revision, Vector3 position, Quaternion rotation, bool visible, bool locked)
        {
            CalibrationId = calibrationId;
            Revision = revision;
            Position = position;
            Rotation = rotation;
            Visible = visible;
            Locked = locked;
        }
    }

    [DefaultExecutionOrder(100)]
    public class SpatialCalibrationReference : MonoBehaviour
    {
        private SimulationCommandBus _bus;
        private GameObject _visualRoot;
        private XRGrabInteractable _grab;
        private BoxCollider _collider;
        private Material _material;
        private bool _initialized;
        private CalibrationSnapshot _snapshot;
        private Quaternion _defaultRotation;
        private Transform _axisHighlight;
        private LineRenderer _highlightLine;
        private TextMesh _highlightLabel;
        private readonly LineRenderer[] _rings = new LineRenderer[3];
        private static readonly Vector3[] Axes = { Vector3.right, Vector3.up, Vector3.forward };
        private static readonly Color[] AxisColors = { new Color(1f, 0.3f, 0.3f), new Color(0.3f, 1f, 0.4f), new Color(0.35f, 0.65f, 1f) };
        private const float RingRadius = 0.19f;
        private bool _desktopRings;
        public bool RotateMode { get; private set; }
        public bool WorldAxes { get; private set; }
        public int RotationAxis { get; private set; } = 1;
        public int ManipulationRevision { get; private set; }
        public string RotationAxisLabel => $"{(WorldAxes ? "World" : "Local")} {"XYZ"[RotationAxis]}";

        public CalibrationSnapshot Snapshot => _snapshot;
        public event Action<CalibrationSnapshot> Changed;
        public bool CanManipulate => _initialized && _snapshot.Visible && !_snapshot.Locked;

        public void SetManipulation(bool rotate, bool worldAxes, int axis)
        {
            if (!_initialized || Locked || axis < 0 || axis > 2) return;
            if (RotateMode == rotate && WorldAxes == worldAxes && RotationAxis == axis) return;
            if (_grab != null) _grab.enabled = false;
            RotateMode = rotate;
            WorldAxes = worldAxes;
            RotationAxis = axis;
            ManipulationRevision++;
            UpdateManipulation();
        }

        public void SetDesktopRings(bool enabled)
        {
            if (_desktopRings == enabled) return;
            _desktopRings = enabled;
            UpdateManipulation();
        }

        public Vector3 GetRotationAxis(int axis)
            => WorldAxes ? Axes[axis] : Snapshot.Rotation * Axes[axis];

        public bool TryRotateStep(float degrees)
            => RotateMode && TrySetPose(Snapshot.Position,
                Quaternion.AngleAxis(degrees, GetRotationAxis(RotationAxis)) * Snapshot.Rotation);

        public bool TryResetRotation() => RotateMode && TrySetPose(Snapshot.Position, _defaultRotation);

        public bool TryPickRing(Ray ray, out Vector3 axis, out Vector3 radial, out float distance)
        {
            axis = radial = Vector3.zero;
            distance = float.PositiveInfinity;
            if (!CanManipulate || !RotateMode || !_desktopRings) return false;
            float bestError = 0.025f;
            for (int i = 0; i < Axes.Length; i++)
            {
                Vector3 normal = GetRotationAxis(i);
                if (Mathf.Abs(Vector3.Dot(normal, ray.direction)) < 0.05f ||
                    !new Plane(normal, Snapshot.Position).Raycast(ray, out float enter)) continue;
                Vector3 candidate = ray.GetPoint(enter) - Snapshot.Position;
                float error = Mathf.Abs(candidate.magnitude - RingRadius);
                if (error >= bestError) continue;
                bestError = error;
                axis = normal;
                radial = candidate.normalized;
                distance = enter;
            }
            return distance < float.PositiveInfinity;
        }

        public bool Visible
        {
            get => _snapshot.Visible;
            set { if (value != _snapshot.Visible) Publish(_snapshot.Position, _snapshot.Rotation, value, _snapshot.Locked); }
        }

        public bool Locked
        {
            get => _snapshot.Locked;
            set { if (value != _snapshot.Locked) Publish(_snapshot.Position, _snapshot.Rotation, _snapshot.Visible, value); }
        }

        public void Initialize(SimulationCommandBus bus, Vector3 initialPosition, Quaternion initialRotation,
            bool visible = true, bool locked = true)
        {
            Unsubscribe();
            _bus = bus;
            if (!IsFinite(initialPosition) || !TryNormalize(initialRotation, out initialRotation))
                throw new ArgumentException("Calibration pose must be finite with a nonzero quaternion.");
            transform.localScale = Vector3.one;
            transform.SetPositionAndRotation(initialPosition, initialRotation);
            _defaultRotation = initialRotation;
            _snapshot = new CalibrationSnapshot(Guid.NewGuid().ToString("N"), 0, initialPosition, initialRotation, visible, locked);
            RotateMode = WorldAxes = false;
            RotationAxis = 1;
            ManipulationRevision++;
            BuildMarker();
            _initialized = true;
            UpdateManipulation();
            if (_bus != null)
            {
                _bus.OnSetCalibrationPose += HandlePose;
                _bus.OnSetCalibrationVisible += HandleVisible;
                _bus.OnSetCalibrationLocked += HandleLocked;
                _bus.OnGetCalibration += EmitState;
            }
        }

        public bool TrySetPose(Vector3 position, Quaternion rotation)
        {
            if (!_initialized || _snapshot.Locked || !IsFinite(position) || !TryNormalize(rotation, out rotation)) return false;
            if (Vector3.Distance(position, _snapshot.Position) < 0.000001f && SameRotation(rotation, _snapshot.Rotation))
                return true;
            Publish(position, rotation, _snapshot.Visible, _snapshot.Locked);
            return true;
        }

        public Vector3 WorldToReferencePosition(Vector3 worldPosition)
            => Quaternion.Inverse(_snapshot.Rotation) * (worldPosition - _snapshot.Position);

        public Quaternion WorldToReferenceRotation(Quaternion worldRotation)
            => Quaternion.Inverse(_snapshot.Rotation) * worldRotation;

        private void LateUpdate()
        {
            if (!_initialized) return;
            // Guard Inspector/XRI changes as well as the API. Scale belongs only on marker graphics.
            transform.localScale = Vector3.one;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            if (_grab != null && _grab.isSelected)
            {
                if (RotateMode) position = _snapshot.Position;
                else rotation = _snapshot.Rotation;
            }
            if (_snapshot.Locked || !TrySetPose(position, rotation))
                transform.SetPositionAndRotation(_snapshot.Position, _snapshot.Rotation);
            if (RotateMode && _snapshot.Visible && Camera.main != null)
                _highlightLabel.transform.rotation = Camera.main.transform.rotation;
        }

        private void Publish(Vector3 position, Quaternion rotation, bool visible, bool locked)
        {
            if (visible != _snapshot.Visible || locked != _snapshot.Locked) ManipulationRevision++;
            transform.localScale = Vector3.one;
            transform.SetPositionAndRotation(position, rotation);
            _snapshot = new CalibrationSnapshot(_snapshot.CalibrationId, _snapshot.Revision + 1, position, rotation, visible, locked);
            UpdateManipulation();
            Changed?.Invoke(_snapshot);
            EmitState();
        }

        private void HandlePose(Vector3 position, Quaternion rotation) => TrySetPose(position, rotation);
        private void HandleVisible(bool visible) => Visible = visible;
        private void HandleLocked(bool locked) => Locked = locked;
        private void EmitState() => _bus?.EmitCalibrationState(_snapshot);

        private void UpdateManipulation()
        {
            if (_visualRoot != null) _visualRoot.SetActive(_snapshot.Visible);
            if (_collider != null) _collider.enabled = _snapshot.Visible;
            if (_grab != null)
            {
                _grab.trackPosition = !RotateMode;
                _grab.trackRotation = RotateMode;
                _grab.enabled = CanManipulate;
            }
            if (_axisHighlight == null) return;
            _axisHighlight.gameObject.SetActive(RotateMode);
            _axisHighlight.rotation = WorldAxes ? Quaternion.identity : _snapshot.Rotation;
            Vector3 axis = Axes[RotationAxis];
            _highlightLine.SetPosition(0, -axis * 0.25f);
            _highlightLine.SetPosition(1, axis * 0.25f);
            _highlightLine.startColor = _highlightLine.endColor = AxisColors[RotationAxis];
            _highlightLabel.transform.localPosition = axis * 0.27f;
            _highlightLabel.text = RotationAxisLabel;
            _highlightLabel.color = AxisColors[RotationAxis];
            for (int i = 0; i < _rings.Length; i++)
                _rings[i].gameObject.SetActive(RotateMode && _desktopRings && CanManipulate);
        }

        private void BuildMarker()
        {
            if (_visualRoot != null) return;
            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            _collider = GetComponent<BoxCollider>();
            if (_collider == null) _collider = gameObject.AddComponent<BoxCollider>();
            _collider.size = Vector3.one * 0.16f;

            _visualRoot = new GameObject("Calibration Marker Graphics");
            _visualRoot.transform.SetParent(transform, false);
            _material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            MakeAxis("X", Vector3.right, Color.red);
            MakeAxis("Y", Vector3.up, Color.green);
            MakeAxis("Z", Vector3.forward, Color.blue);
            BuildRotationGraphics();
            var origin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            origin.name = "Reference Origin";
            origin.transform.SetParent(_visualRoot.transform, false);
            origin.transform.localScale = Vector3.one * 0.045f;
            var collider = origin.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            origin.GetComponent<Renderer>().sharedMaterial = _material;

            _grab = GetComponent<XRGrabInteractable>();
            if (_grab == null) _grab = gameObject.AddComponent<XRGrabInteractable>();
            _grab.movementType = XRGrabInteractable.MovementType.Instantaneous;
            _grab.throwOnDetach = false;
            _grab.useDynamicAttach = true;
            _grab.matchAttachRotation = true;
            _grab.trackScale = false;
            _grab.attachEaseInTime = 0f;
        }

        private void BuildRotationGraphics()
        {
            _axisHighlight = new GameObject("Rotation Axis Preview").transform;
            _axisHighlight.SetParent(_visualRoot.transform, false);
            _highlightLine = MakeLine(_axisHighlight, 0.009f);
            _highlightLine.positionCount = 2;
            var label = new GameObject("Selected Rotation Axis");
            label.transform.SetParent(_axisHighlight, false);
            _highlightLabel = label.AddComponent<TextMesh>();
            _highlightLabel.anchor = TextAnchor.MiddleCenter;
            _highlightLabel.characterSize = 0.018f;
            _highlightLabel.fontSize = 48;
            for (int i = 0; i < Axes.Length; i++)
            {
                var ring = new GameObject($"{"XYZ"[i]} Rotation Ring");
                ring.transform.SetParent(_axisHighlight, false);
                var line = _rings[i] = MakeLine(ring.transform, 0.005f);
                line.loop = true;
                line.positionCount = 48;
                line.startColor = line.endColor = AxisColors[i];
                Vector3 start = Axes[(i + 1) % 3] * RingRadius;
                for (int point = 0; point < line.positionCount; point++)
                    line.SetPosition(point, Quaternion.AngleAxis(360f * point / line.positionCount, Axes[i]) * start);
            }
        }

        private LineRenderer MakeLine(Transform owner, float width)
        {
            var line = owner.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.startWidth = line.endWidth = width;
            line.sharedMaterial = _material;
            return line;
        }

        private void MakeAxis(string axis, Vector3 direction, Color color)
        {
            var go = new GameObject(axis + " Axis");
            go.transform.SetParent(_visualRoot.transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, direction * 0.15f);
            line.startWidth = line.endWidth = 0.006f;
            line.sharedMaterial = _material;
            line.startColor = line.endColor = color;
            var label = new GameObject(axis + " Label");
            label.transform.SetParent(_visualRoot.transform, false);
            label.transform.localPosition = direction * 0.18f;
            var text = label.AddComponent<TextMesh>();
            text.text = axis;
            text.characterSize = 0.025f;
            text.fontSize = 48;
            text.anchor = TextAnchor.MiddleCenter;
            text.color = color;
        }

        private static bool IsFinite(Vector3 value)
            => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool SameRotation(Quaternion a, Quaternion b)
        {
            // Quaternion.Angle rounds small angles to zero. Compare components so fine
            // calibration adjustments are retained, while treating q and -q as equivalent.
            float sign = Quaternion.Dot(a, b) < 0f ? -1f : 1f;
            return Mathf.Abs(a.x - sign * b.x) < 0.0000001f && Mathf.Abs(a.y - sign * b.y) < 0.0000001f &&
                   Mathf.Abs(a.z - sign * b.z) < 0.0000001f && Mathf.Abs(a.w - sign * b.w) < 0.0000001f;
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool TryNormalize(Quaternion value, out Quaternion normalized)
        {
            normalized = Quaternion.identity;
            if (!Finite(value.x) || !Finite(value.y) || !Finite(value.z) || !Finite(value.w)) return false;
            float lengthSquared = Quaternion.Dot(value, value);
            if (!Finite(lengthSquared) || lengthSquared < 0.000001f) return false;
            normalized = value.normalized;
            return true;
        }

        private void Unsubscribe()
        {
            if (_bus == null) return;
            _bus.OnSetCalibrationPose -= HandlePose;
            _bus.OnSetCalibrationVisible -= HandleVisible;
            _bus.OnSetCalibrationLocked -= HandleLocked;
            _bus.OnGetCalibration -= EmitState;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (_material != null) { if (Application.isPlaying) Destroy(_material); else DestroyImmediate(_material); }
        }
    }
}
