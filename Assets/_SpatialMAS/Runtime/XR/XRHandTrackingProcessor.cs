using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Hands.Processing;

namespace SpatialMAS.XR
{
    /// <summary>Compatibility host for old scenes. Provider joints and recorded poses are never modified.</summary>
    public class XRHandTrackingProcessor : MonoBehaviour, IXRHandProcessor
    {
        public int callbackOrder => -10;
        public void ProcessJoints(XRHandSubsystem subsystem, XRHandSubsystem.UpdateSuccessFlags successFlags,
            XRHandSubsystem.UpdateType updateType) { }

        /// <summary>Adaptive 1-Euro filtering exclusively for held-object anchors, from the Laser XR reference.</summary>
        public sealed class AnchorFilter
        {
            private bool _initialized;
            private Pose _previous;
            private Vector3 _velocity;
            private float _angularSpeed;
            public void Reset() => _initialized = false;
            public Pose Filter(Pose raw, float dt)
            {
                if (!_initialized || dt <= 0f || dt > 0.1f)
                {
                    _initialized = true;
                    _previous = raw;
                    _velocity = Vector3.zero;
                    _angularSpeed = 0f;
                    return raw;
                }
                Vector3 delta = raw.position - _previous.position;
                float distance = delta.magnitude;
                if (distance < 0.00035f) raw.position = Vector3.Lerp(_previous.position, raw.position,
                    Mathf.Pow(distance / 0.00035f, 2f));
                _velocity = Vector3.Lerp(_velocity, delta / dt, Alpha(1f, dt));
                float angle = Quaternion.Angle(_previous.rotation, raw.rotation);
                _angularSpeed = Mathf.Lerp(_angularSpeed, angle * Mathf.Deg2Rad / dt, Alpha(1f, dt));
                if (angle < 0.2f) raw.rotation = Quaternion.Slerp(_previous.rotation, raw.rotation,
                    Mathf.Pow(angle / 0.2f, 2f));
                _previous = new Pose(
                    Vector3.Lerp(_previous.position, raw.position, Alpha(2f + 3f * _velocity.magnitude, dt)),
                    Quaternion.Slerp(_previous.rotation, raw.rotation, Alpha(0.6f + 6f * _angularSpeed, dt)));
                return _previous;
            }
            private static float Alpha(float cutoff, float dt) => 1f / (1f + 1f / (2f * Mathf.PI * cutoff * dt));
        }
    }
}
