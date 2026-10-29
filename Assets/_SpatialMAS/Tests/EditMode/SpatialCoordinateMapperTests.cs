using NUnit.Framework;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Tests.EditMode
{
    public class SpatialCoordinateMapperTests
    {
        [Test]
        public void LegacyPositions_OnlyConvertMillimetersAndSwapDepthHeight()
        {
            var mapper = new SpatialCoordinateMapper();
            Vector3 world = mapper.OscMillimetersToUnityMeters(1000f, 2000f, 500f);
            Assert.Less(Vector3.Distance(new Vector3(1f, 0.5f, 2f), world), 0.00001f);
            Assert.Less(Vector3.Distance(new Vector3(1000f, 2000f, 500f), mapper.UnityMetersToOscMillimeters(world)), 0.01f);
        }

        [Test]
        public void CanonicalPositions_PreserveUnityAxesAndPhysicalDistances()
        {
            var mapper = new SpatialCoordinateMapper();
            Vector3 world = mapper.WorldMillimetersToUnityMeters(1000f, 2000f, 500f);
            Assert.Less(Vector3.Distance(new Vector3(1f, 2f, 0.5f), world), 0.00001f);
            Assert.Less(Vector3.Distance(new Vector3(1000f, 2000f, 500f), mapper.UnityMetersToWorldMillimeters(world)), 0.01f);
            Assert.AreEqual(10f, mapper.OscMillimeterLengthToUnityMeters(10000f), 0.00001f);
            Assert.AreEqual(10000f, mapper.UnityMeterLengthToOscMillimeters(10f), 0.01f);
        }
    }
}
