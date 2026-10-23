using NUnit.Framework;
using SpatialMAS.Scenarios.BuiltIn;
using UnityEngine;

namespace SpatialMAS.Tests.EditMode
{
    public class ScenariosMathTests
    {
        [Test]
        public void SwarmalatorsStep_PositiveKSynchronizesPhases()
        {
            Vector3[] positions = { new Vector3(-0.2f, 0f, 0f), new Vector3(0.2f, 0f, 0f) };
            float[] phases = { 0.2f, 1.4f };
            float[] naturalFreqs = { 0f, 0f };

            float initialDiff = Mathf.Abs(phases[1] - phases[0]);
            for (int step = 0; step < 20; step++)
            {
                SwarmalatorsScenario.StepSwarmalators(positions, phases, naturalFreqs, J: 0.1f, K: 1.0f, dt: 0.02f);
            }

            float finalDiff = Mathf.Abs(phases[1] - phases[0]);
            Assert.Less(finalDiff, initialDiff, "Positive K coupling must reduce phase difference between Swarmalators.");
        }

        [Test]
        public void CrowdAvoidanceStep_HumanPositionRepelsNearbyAgent()
        {
            Vector3 humanPos = new Vector3(0f, 1.2f, 0f);
            Vector3[] positions = { new Vector3(0.2f, 1.2f, 0f) }; // 20 cm to the right (+X) of human
            Vector3[] velocities = { Vector3.zero };

            CrowdAvoidanceScenario.StepBoidsAndHumanAvoidance(
                positions,
                velocities,
                arenaCenter: new Vector3(0f, 1.2f, 0f),
                arenaRadius: 5.0f,
                humanPosition: humanPos,
                personalSpaceRadius: 0.8f,
                humanRepulsionStrength: 3.0f,
                targetSpeed: 0.5f,
                dt: 0.05f);

            Assert.Greater(positions[0].x, 0.2f, "Agent within personalSpaceRadius must be pushed away (+X) from the human.");
        }

        [Test]
        public void SelfAssemblySlots_ComputeOrthogonalWorldOffsets()
        {
            Vector3 center = new Vector3(1f, 2f, 3f);
            float size = 0.1f;

            Vector3 plusX = SelfAssemblyScenario.GetSlotWorldPosition(center, Quaternion.identity, 0, size);
            Vector3 minusX = SelfAssemblyScenario.GetSlotWorldPosition(center, Quaternion.identity, 1, size);
            Vector3 plusY = SelfAssemblyScenario.GetSlotWorldPosition(center, Quaternion.identity, 4, size);

            Assert.AreEqual(1.1f, plusX.x, 0.0001f);
            Assert.AreEqual(0.9f, minusX.x, 0.0001f);
            Assert.AreEqual(2.1f, plusY.y, 0.0001f);
        }
    }
}
