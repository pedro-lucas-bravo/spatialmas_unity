using UnityEngine;

namespace SpatialMAS.UI
{
    /// <summary>Independent horizontal-center, head-height and yaw follow channels for the panel only.</summary>
    public sealed class XRPanelFollow : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _angularDeadZone = 8f;
        [SerializeField, Min(0.01f)] private float _catchUpSeconds = 0.25f;
        [SerializeField, Min(.35f)] private float _radius = 1.25f;
        private Transform _panel;
        private bool _initialized;
        private float _lastValidYaw, _followYaw, _worldHeight;
        private float _angle, _currentRadius, _manualHeadYaw;
        private bool _manualYawHeld;
        private Vector3 _center;
        private float _heightOffset;
        private float _horizontalOuter = .10f, _horizontalInner = .05f, _horizontalTime = .25f;
        private float _verticalOuter = .08f, _verticalInner = .04f, _verticalTime = .25f;
        private bool _horizontalFollowing, _verticalFollowing;

        public bool Pinned { get; private set; }
        public float Radius => _radius;
        public float AngularDeadZone => _angularDeadZone;
        public float CatchUpSeconds => _catchUpSeconds;

        public void Initialize(Transform panel) => _panel = panel;
        private static float Valid(float value, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        public void ConfigureRadius(float radius) => _radius = Mathf.Max(.35f, Valid(radius, 1.25f));
        public void ConfigureTuning(float angularDeadZone, float catchUpSeconds)
        {
            _angularDeadZone = Mathf.Clamp(Valid(angularDeadZone, 8f), 0f, 180f);
            _catchUpSeconds = Mathf.Max(.01f, Valid(catchUpSeconds, .25f));
        }

        public void ConfigurePositionTuning(float hOuter, float hInner, float hTime, float vOuter, float vInner, float vTime)
        {
            _horizontalOuter = Mathf.Max(0f, Valid(hOuter, .10f));
            _horizontalInner = Mathf.Clamp(Valid(hInner, .05f), 0f, _horizontalOuter);
            _horizontalTime = Mathf.Max(.01f, Valid(hTime, .25f));
            _verticalOuter = Mathf.Max(0f, Valid(vOuter, .08f));
            _verticalInner = Mathf.Clamp(Valid(vInner, .04f), 0f, _verticalOuter);
            _verticalTime = Mathf.Max(.01f, Valid(vTime, .25f));
        }

        private static float Alpha(float dt, float seconds) => 1f - Mathf.Exp(-Mathf.Max(0f, Valid(dt, 0f)) / seconds);

        private float Heading(Pose head)
        {
            Vector3 forward = Vector3.ProjectOnPlane(head.rotation * Vector3.forward, Vector3.up);
            // Near vertical gaze has no stable heading. Keep the last useful yaw.
            if (forward.sqrMagnitude >= 0.01f)
                _lastValidYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            return _lastValidYaw;
        }

        public void Recall(Pose head)
        {
            if (_panel == null) return;
            _angle = _followYaw = Heading(head);
            _manualYawHeld = false;
            _currentRadius = Radius;
            _worldHeight = head.position.y - 0.1f;
            _heightOffset = -.1f;
            _center = Vector3.ProjectOnPlane(head.position, Vector3.up);
            _horizontalFollowing = _verticalFollowing = false;
            _initialized = true;
            Apply();
        }

        /// <summary>Resynchronize the arc center and remember released azimuth and preferred head-height offset.</summary>
        public void CommitPlacement(Pose head)
        {
            if (_panel == null) return;
            _followYaw = Heading(head);
            Vector3 relative = _panel.position - head.position;
            relative.y = 0f;
            _angle = relative.sqrMagnitude > .000001f ? Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg : _followYaw;
            // Manual placement is a world azimuth, not a permanent bias added
            // to every future head heading. Hold it until a deliberate head turn.
            _manualHeadYaw = _followYaw;
            _followYaw = _angle;
            _manualYawHeld = true;
            _currentRadius = relative.magnitude;
            _worldHeight = _panel.position.y;
            _heightOffset = _worldHeight - head.position.y;
            _center = Vector3.ProjectOnPlane(head.position, Vector3.up);
            _horizontalFollowing = _verticalFollowing = false;
            _initialized = true;
        }

        public void SetPinned(bool pinned, Pose head)
        {
            if (Pinned == pinned) return;
            Pinned = pinned;
            if (!pinned) CommitPlacement(head);
        }

        public void Step(Pose head, bool tracked, bool frozen, float dt, bool dragging = false)
        {
            if (_panel == null) return;
            if (!tracked) return;
            float yaw = Heading(head);
            if (Pinned || dragging || frozen) return;
            if (!_initialized) CommitPlacement(head);
            Vector3 horizontalDelta = Vector3.ProjectOnPlane(head.position, Vector3.up) - _center;
            float horizontalError = horizontalDelta.magnitude;
            if (horizontalError > _horizontalOuter) _horizontalFollowing = true;
            if (_horizontalFollowing)
            {
                float residual = Mathf.Max(0f, horizontalError - _horizontalInner);
                _center += horizontalError > .000001f ? horizontalDelta / horizontalError * residual * Alpha(dt, _horizontalTime) : Vector3.zero;
                if (residual <= .0001f) _horizontalFollowing = false;
            }
            float verticalDelta = head.position.y + _heightOffset - _worldHeight;
            float verticalError = Mathf.Abs(verticalDelta);
            if (verticalError > _verticalOuter) _verticalFollowing = true;
            if (_verticalFollowing)
            {
                float residual = Mathf.Max(0f, verticalError - _verticalInner);
                _worldHeight += Mathf.Sign(verticalDelta) * residual * Alpha(dt, _verticalTime);
                if (residual <= .0001f) _verticalFollowing = false;
            }
            float alpha = Alpha(dt, _catchUpSeconds);
            if (_manualYawHeld && Mathf.Abs(Mathf.DeltaAngle(_manualHeadYaw, yaw)) > _angularDeadZone)
                _manualYawHeld = false;
            if (!_manualYawHeld)
            {
                float error = Mathf.DeltaAngle(_followYaw, yaw);
                // Removing the dead-zone boundary gives a continuous response at its edge.
                float beyond = Mathf.Max(0f, Mathf.Abs(error) - _angularDeadZone);
                _followYaw += Mathf.Sign(error) * beyond * alpha;
                _angle = Mathf.LerpAngle(_angle, _followYaw, alpha);
            }
            _currentRadius = Mathf.Lerp(_currentRadius, Radius, alpha);
            Apply();
        }

        private void Apply()
        {
            Vector3 position = _center + Quaternion.Euler(0f, _angle, 0f) * Vector3.forward * _currentRadius;
            position.y = _worldHeight;
            _panel.SetPositionAndRotation(position, Quaternion.Euler(0f, _angle, 0f));
        }
    }
}
