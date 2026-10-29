using UnityEngine;

namespace SpatialMAS.Core
{
    /// <summary>
    /// Unit and axis adapters at the OSC boundary. All runtime positions are Unity world meters.
    /// Legacy HS-ims uses X=right, Y=depth, Z=up; /world messages use Unity XYZ axes.
    /// No adapter applies an origin offset, calibration pose, or world-scale multiplier.
    /// </summary>
    public class SpatialCoordinateMapper
    {
        public const float MillimetersToMeters = 0.001f;
        public const float MetersToMillimeters = 1000f;

        public Vector3 OscMillimetersToUnityMeters(float xMm, float yMm, float zMm)
            => new Vector3(xMm, zMm, yMm) * MillimetersToMeters;

        public Vector3 UnityMetersToOscMillimeters(Vector3 worldMeters)
            => new Vector3(worldMeters.x, worldMeters.z, worldMeters.y) * MetersToMillimeters;

        public Vector3 WorldMillimetersToUnityMeters(float xMm, float yMm, float zMm)
            => new Vector3(xMm, yMm, zMm) * MillimetersToMeters;

        public Vector3 UnityMetersToWorldMillimeters(Vector3 worldMeters)
            => worldMeters * MetersToMillimeters;

        public float OscMillimeterLengthToUnityMeters(float lengthMm) => lengthMm * MillimetersToMeters;
        public float UnityMeterLengthToOscMillimeters(float lengthMeters) => lengthMeters * MetersToMillimeters;

        /// <summary>Legacy Euler signature only; canonical orientation endpoints use Unity quaternions.</summary>
        public Quaternion OscEulerToUnityRotation(float pitchDeg, float yawDeg, float rollDeg)
            => Quaternion.Euler(pitchDeg, rollDeg, yawDeg);
    }
}
