using System;
using UnityEngine;

namespace SpatialMAS.Core
{
    /// <summary>Rebases cached world-space simulation state when the arena frame changes.</summary>
    public readonly struct SpatialFrameChange
    {
        public readonly Vector3 PreviousOrigin;
        public readonly Vector3 Origin;
        public readonly Quaternion RotationDelta;
        public readonly float ScaleRatio;

        public SpatialFrameChange(Vector3 previousOrigin, Quaternion previousRotation, float previousScale,
            Vector3 origin, Quaternion rotation, float scale)
        {
            PreviousOrigin = previousOrigin;
            Origin = origin;
            RotationDelta = rotation * Quaternion.Inverse(previousRotation);
            ScaleRatio = scale / previousScale;
        }

        public Vector3 TransformPosition(Vector3 position) => Origin + TransformVector(position - PreviousOrigin);
        public Vector3 TransformVector(Vector3 vector) => RotationDelta * vector * ScaleRatio;
        public Vector3 TransformDirection(Vector3 direction) => RotationDelta * direction;
        public Quaternion TransformRotation(Quaternion rotation) => RotationDelta * rotation;
    }

    /// <summary>
    /// Converts bidirectionally between the legacy HS-ims external OSC coordinate frame
    /// (millimeters with X=Right, Y=Forward/Depth, Z=Up) and Unity's left-handed meter world space
    /// (X=Right, Y=Up, Z=Forward), applying a configurable WorldScale and OriginOffset.
    /// </summary>
    public class SpatialCoordinateMapper
    {
        public const float MillimetersToMeters = 0.001f;
        public const float MetersToMillimeters = 1000.0f;

        private float _worldScale = 1.0f;
        private Vector3 _originOffset = Vector3.zero;
        private Quaternion _originRotation = Quaternion.identity;

        public event Action OnTransformFrameChanged;
        public event Action<SpatialFrameChange> OnFrameChanged;

        public float WorldScale
        {
            get => _worldScale;
            set
            {
                float clamped = Mathf.Clamp(value, 0.01f, 10.0f);
                if (!Mathf.Approximately(_worldScale, clamped))
                {
                    var change = new SpatialFrameChange(_originOffset, _originRotation, _worldScale,
                        _originOffset, _originRotation, clamped);
                    _worldScale = clamped;
                    OnFrameChanged?.Invoke(change);
                    OnTransformFrameChanged?.Invoke();
                }
            }
        }

        public Vector3 OriginOffset
        {
            get => _originOffset;
            set
            {
                if (_originOffset != value)
                {
                    var change = new SpatialFrameChange(_originOffset, _originRotation, _worldScale,
                        value, _originRotation, _worldScale);
                    _originOffset = value;
                    OnFrameChanged?.Invoke(change);
                    OnTransformFrameChanged?.Invoke();
                }
            }
        }

        public Quaternion OriginRotation
        {
            get => _originRotation;
            set
            {
                if (_originRotation != value)
                {
                    var change = new SpatialFrameChange(_originOffset, _originRotation, _worldScale,
                        _originOffset, value, _worldScale);
                    _originRotation = value;
                    OnFrameChanged?.Invoke(change);
                    OnTransformFrameChanged?.Invoke();
                }
            }
        }

        public SpatialCoordinateMapper(float worldScale = 1.0f, Vector3 originOffset = default)
        {
            _worldScale = Mathf.Clamp(worldScale, 0.01f, 10.0f);
            _originOffset = originOffset;
            _originRotation = Quaternion.identity;
        }

        /// <summary>
        /// Maps incoming OSC millimeter coordinates (x_mm, y_mm, z_mm) where Y and Z are swapped
        /// into Unity world-space meters: OriginOffset + OriginRotation * (new Vector3(x, z, y) * 0.001f * WorldScale).
        /// </summary>
        public Vector3 OscMillimetersToUnityMeters(float xMm, float yMm, float zMm)
        {
            Vector3 unscaledMeters = new Vector3(
                xMm * MillimetersToMeters,
                zMm * MillimetersToMeters,
                yMm * MillimetersToMeters
            );
            return _originOffset + (_originRotation * (unscaledMeters * _worldScale));
        }

        /// <summary>
        /// Maps Unity world-space position (meters) back to outgoing OSC millimeter coordinates (x_mm, y_mm, z_mm)
        /// with Y and Z swapped to match the HS-ims wire protocol.
        /// </summary>
        public Vector3 UnityMetersToOscMillimeters(Vector3 unityWorldMeters)
        {
            float safeScale = Mathf.Max(0.0001f, _worldScale);
            Vector3 localMeters = Quaternion.Inverse(_originRotation) * (unityWorldMeters - _originOffset) / safeScale;
            return new Vector3(
                localMeters.x * MetersToMillimeters,
                localMeters.z * MetersToMillimeters,
                localMeters.y * MetersToMillimeters
            );
        }

        /// <summary>
        /// Converts a scalar size/radius from OSC millimeters (or raw agent scale) to Unity meters.
        /// Note: In HS-ims, /boundaries/add sends radius in millimeters, whereas /agents/instantiate sends agent scale in meters!
        /// </summary>
        public float OscMillimeterLengthToUnityMeters(float lengthMm)
        {
            return lengthMm * MillimetersToMeters * _worldScale;
        }

        /// <summary>
        /// Converts a Unity meter distance to OSC millimeter distance (unscaled).
        /// </summary>
        public float UnityMeterLengthToOscMillimeters(float lengthMeters)
        {
            float safeScale = Mathf.Max(0.0001f, _worldScale);
            return (lengthMeters / safeScale) * MetersToMillimeters;
        }

        /// <summary>
        /// Scales an agent's base meter scale by the active WorldScale.
        /// </summary>
        public float ScaleAgentMeterRadius(float baseScaleMeters)
        {
            return baseScaleMeters * _worldScale;
        }

        /// <summary>
        /// Maps incoming /listener/rotation Euler angles (pitch, yaw, roll) where Y and Z are swapped in HS-ims.
        /// </summary>
        public Quaternion OscEulerToUnityRotation(float pitchDeg, float yawDeg, float rollDeg)
        {
            return _originRotation * Quaternion.Euler(pitchDeg, rollDeg, yawDeg);
        }
    }
}
