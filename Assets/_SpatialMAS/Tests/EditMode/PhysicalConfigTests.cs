using NUnit.Framework;
using SpatialMAS.Core;
using UnityEngine;

namespace SpatialMAS.Tests.EditMode
{
    public class PhysicalConfigTests
    {
        [Test]
        public void LegacyScaleAndOriginOverridesCannotChangePhysicalCoordinateContract()
        {
            var config = ScriptableObject.CreateInstance<SpatialMASConfig>();
            try
            {
                config.scenarioCenter = new Vector3(2f, 1f, 3f);
                config.ApplyJsonOverrides("{\"worldScale\":0.01,\"originX\":100,\"oscListenPort\":7001}");
                Assert.AreEqual(new Vector3(2f, 1f, 3f), config.scenarioCenter);
                Assert.AreEqual(Quaternion.identity, config.calibrationRotation);
                Assert.IsTrue(config.calibrationLocked);
                Assert.AreEqual(7001, config.oscListenPort);
                Assert.IsNull(typeof(SpatialMASConfig).GetField("worldScale"));
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void PartialReferenceOverridePreservesScenarioSettingsAndNormalizesRotation()
        {
            var config = ScriptableObject.CreateInstance<SpatialMASConfig>();
            try
            {
                config.scenarioLayout = ScenarioLayout.RoomScale;
                config.ApplyJsonOverrides("{\"calibrationPosition\":{\"x\":2,\"y\":3,\"z\":4},\"calibrationRotation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":2}}");
                Assert.AreEqual(ScenarioLayout.RoomScale, config.scenarioLayout);
                Assert.AreEqual(new Vector3(2f, 3f, 4f), config.calibrationPosition);
                Assert.AreEqual(Quaternion.identity, config.calibrationRotation);
            }
            finally { Object.DestroyImmediate(config); }
        }
    }
}
