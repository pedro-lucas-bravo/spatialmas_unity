using NUnit.Framework;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Tests.EditMode
{
    public class SpatialCoordinateMapperTests
    {
        [Test]
        public void MillimetersToUnityMeters_SwapsYAndZAndAppliesScaleAndOffset()
        {
            var offset = new Vector3(0f, 1.2f, 0.8f);
            var mapper = new SpatialCoordinateMapper(worldScale: 0.2f, originOffset: offset);

            // External OSC sends (x=1000mm, y=2000mm depth, z=500mm height)
            // Unscaled Unity meters = (1.0, 0.5, 2.0) -> scaled by 0.2 = (0.2, 0.1, 0.4) + offset (0, 1.2, 0.8) = (0.2, 1.3, 1.2)
            Vector3 unityPos = mapper.OscMillimetersToUnityMeters(1000f, 2000f, 500f);

            Assert.AreEqual(0.2f, unityPos.x, 0.0001f);
            Assert.AreEqual(1.3f, unityPos.y, 0.0001f);
            Assert.AreEqual(1.2f, unityPos.z, 0.0001f);

            // Inverse mapping back to OSC millimeters must recover (1000, 2000, 500)
            Vector3 roundTripOscMm = mapper.UnityMetersToOscMillimeters(unityPos);
            Assert.AreEqual(1000f, roundTripOscMm.x, 0.01f);
            Assert.AreEqual(2000f, roundTripOscMm.y, 0.01f);
            Assert.AreEqual(500f, roundTripOscMm.z, 0.01f);
        }

        [Test]
        public void LengthConversion_RespectsWorldScale()
        {
            var mapper = new SpatialCoordinateMapper(worldScale: 0.5f, originOffset: Vector3.zero);
            // 10,000 mm (10m) boundary radius at 0.5x scale = 5.0m in Unity
            float meters = mapper.OscMillimeterLengthToUnityMeters(10000f);
            Assert.AreEqual(5.0f, meters, 0.0001f);

            float mmBack = mapper.UnityMeterLengthToOscMillimeters(meters);
            Assert.AreEqual(10000f, mmBack, 0.01f);
        }
    }
}
