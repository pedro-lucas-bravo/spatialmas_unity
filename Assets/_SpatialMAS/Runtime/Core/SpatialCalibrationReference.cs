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

        public CalibrationSnapshot Snapshot => _snapshot;
        public event Action<CalibrationSnapshot> Changed;
        public bool CanManipulate => _initialized && _snapshot.Visible && !_snapshot.Locked;

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
            _snapshot = new CalibrationSnapshot(Guid.NewGuid().ToString("N"), 0, initialPosition, initialRotation, visible, locked);
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
            if (_snapshot.Locked || !TrySetPose(transform.position, transform.rotation))
                transform.SetPositionAndRotation(_snapshot.Position, _snapshot.Rotation);
        }

        private void Publish(Vector3 position, Quaternion rotation, bool visible, bool locked)
        {
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
            if (_grab != null) _grab.enabled = _snapshot.Visible && !_snapshot.Locked;
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
