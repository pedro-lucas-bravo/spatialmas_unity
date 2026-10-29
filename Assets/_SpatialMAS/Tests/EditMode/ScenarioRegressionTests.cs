using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SpatialMAS.Core;
using SpatialMAS.Scenarios;
using SpatialMAS.Scenarios.BuiltIn;
using SpatialMAS.XR;
using UnityEngine;

namespace SpatialMAS.Tests.EditMode
{
    public class ScenarioRegressionTests
    {
        [Test]
        public void SelfAssembly_DetachingRootReparentsRemainingComponent()
        {
            var scenario = new SelfAssemblyScenario();
            scenario.OnScenarioStart(new ScenarioContext { Bus = new SimulationCommandBus() });
            foreach (var node in scenario.Nodes)
            {
                node.StateTimer = 100f;
                node.Velocity = Vector3.zero;
            }

            var root = scenario.Nodes[0];
            var middle = scenario.Nodes[1];
            var leaf = scenario.Nodes[2];
            root.State = middle.State = leaf.State = SelfAssemblyScenario.AssemblyState.Joined;
            root.RootStructureId = middle.RootStructureId = leaf.RootStructureId = 0;
            root.ConnectedNeighborIds[0] = 1;
            middle.ConnectedNeighborIds[1] = 0;
            middle.ConnectedNeighborIds[0] = 2;
            leaf.ConnectedNeighborIds[1] = 1;
            middle.TargetParentId = 0;
            middle.TargetSlotIndex = 0;
            leaf.TargetParentId = 1;
            leaf.TargetSlotIndex = 0;
            root.StateTimer = 0.001f;

            scenario.OnScenarioTick(0.02f);

            Assert.AreEqual(-1, middle.ConnectedNeighborIds[1]);
            Assert.AreEqual(-1, middle.TargetParentId, "The remaining component must no longer follow the detached cube.");
            Assert.AreEqual(1, middle.RootStructureId);
            Assert.AreEqual(1, leaf.RootStructureId);
            Assert.AreEqual(1, leaf.TargetParentId);
            Assert.AreEqual(SelfAssemblyScenario.AssemblyState.Wandering, root.State);
        }

        [Test]
        public void SelfAssembly_DetachingLastNeighborMakesRemainingCubeIndependent()
        {
            var scenario = new SelfAssemblyScenario();
            scenario.OnScenarioStart(new ScenarioContext { Bus = new SimulationCommandBus() });
            foreach (var node in scenario.Nodes)
            {
                node.StateTimer = 100f;
                node.Velocity = Vector3.zero;
            }
            var parent = scenario.Nodes[0];
            var child = scenario.Nodes[1];
            parent.State = child.State = SelfAssemblyScenario.AssemblyState.Joined;
            parent.ConnectedNeighborIds[0] = child.Id;
            child.ConnectedNeighborIds[1] = parent.Id;
            child.TargetParentId = parent.Id;
            child.TargetSlotIndex = 0;
            child.RootStructureId = parent.Id;
            parent.StateTimer = 0.001f;

            scenario.OnScenarioTick(0.02f);

            Assert.AreEqual(-1, child.TargetParentId);
            Assert.AreEqual(child.Id, child.RootStructureId);
            Assert.AreEqual(SelfAssemblyScenario.AssemblyState.Wandering, child.State);
        }

        [Test]
        public void SelfAssembly_ConcurrentDockingCannotExceedStructureCapacity()
        {
            var scenario = new SelfAssemblyScenario();
            scenario.OnScenarioStart(new ScenarioContext { Bus = new SimulationCommandBus() });
            scenario.SetParameter("maxsize", 2f);
            foreach (var node in scenario.Nodes)
            {
                node.StateTimer = 100f;
                node.Velocity = Vector3.zero;
            }
            var target = scenario.Nodes[0];
            target.State = SelfAssemblyScenario.AssemblyState.WanderingAndDetecting;
            float cubeSize = GetField<float>(scenario, "_cubeSizeMeters");
            for (int id = 1; id <= 2; id++)
            {
                var seeker = scenario.Nodes[id];
                seeker.State = SelfAssemblyScenario.AssemblyState.ToJoin;
                seeker.TargetParentId = target.Id;
                seeker.TargetSlotIndex = (id - 1) * 2;
                seeker.Position = SelfAssemblyScenario.GetSlotWorldPosition(target.Position, target.Rotation,
                    seeker.TargetSlotIndex, cubeSize);
            }

            scenario.OnScenarioTick(0.02f);

            Assert.AreEqual(SelfAssemblyScenario.AssemblyState.Joined, scenario.Nodes[1].State);
            Assert.AreEqual(SelfAssemblyScenario.AssemblyState.WanderingAndDetecting, scenario.Nodes[2].State);
            Assert.AreEqual(-1, target.ConnectedNeighborIds[2], "The second pending dock must not overfill the structure.");
            Assert.AreEqual(2, scenario.Nodes[2].RootStructureId);
        }

        [Test]
        public void AttractorRepeller_ReversesOnceWhileOrbiterRemainsInside()
        {
            var scenario = new AttractorsRepellersScenario();
            SetField(scenario, "_orbiterCount", 1);
            SetField(scenario, "_attractorCount", 1);
            SetField(scenario, "_repellerCount", 1);
            scenario.OnScenarioStart(new ScenarioContext { Bus = new SimulationCommandBus() });
            GetField<Vector3[]>(scenario, "_repellerPositions")[0] = Vector3.zero;
            float initialVelocity = GetField<float[]>(scenario, "_omegaTheta")[0];

            scenario.OnScenarioTick(0.02f);
            float entryVelocity = GetField<float[]>(scenario, "_omegaTheta")[0];
            scenario.OnScenarioTick(0.02f);

            Assert.AreEqual(-initialVelocity, entryVelocity, 0.00001f);
            Assert.AreEqual(entryVelocity, GetField<float[]>(scenario, "_omegaTheta")[0], 0.00001f,
                "Remaining within a repeller must not flip velocity on every frame.");
        }

        [Test]
        public void Attractors_SpeedAndPresetsImmediatelyChangeExistingAngularVelocities()
        {
            var scenario = new AttractorsRepellersScenario();
            scenario.OnScenarioStart(new ScenarioContext { Bus = new SimulationCommandBus() });
            float initial = GetField<float[]>(scenario, "_omegaTheta")[0];

            scenario.SetParameter("speed", 3.2f);
            Assert.AreEqual(initial * 2f, GetField<float[]>(scenario, "_omegaTheta")[0], 0.00001f);

            scenario.ApplyPreset(1); // 2.4 radians/s, relative to the initial 1.6.
            Assert.AreEqual(initial * 1.5f, GetField<float[]>(scenario, "_omegaTheta")[0], 0.00001f);
            for (int i = 0; i < 10; i++)
            {
                scenario.ApplyPreset(0);
                scenario.ApplyPreset(1);
            }
            Assert.AreEqual(initial * 1.5f, GetField<float[]>(scenario, "_omegaTheta")[0], 0.00001f,
                "Repeated preset switching must not compound the speed multiplier.");
        }

        [Test]
        public void MusicalEncounters_SpeedAndPresetsImmediatelyChangeExistingVelocities()
        {
            var scenario = new MusicalEncountersScenario();
            scenario.OnScenarioStart(new ScenarioContext { Bus = new SimulationCommandBus() });
            Vector3 initial = GetField<Vector3[]>(scenario, "_velocities")[0];

            scenario.SetParameter("speed", 1.3f);
            Assert.Less((GetField<Vector3[]>(scenario, "_velocities")[0] - initial * 2f).magnitude, 0.00001f);

            scenario.ApplyPreset(2); // 0.35 metres/s, relative to the initial 0.65.
            Assert.Less((GetField<Vector3[]>(scenario, "_velocities")[0] - initial * (0.35f / 0.65f)).magnitude, 0.00001f);
            for (int i = 0; i < 10; i++)
            {
                scenario.ApplyPreset(1);
                scenario.ApplyPreset(2);
            }
            Assert.Less((GetField<Vector3[]>(scenario, "_velocities")[0] - initial * (0.35f / 0.65f)).magnitude, 0.00001f,
                "Repeated preset switching must preserve the requested speed.");
        }

        [Test]
        public void Crowd_UsesSharedRepellerEnableRadiusForceAndHandPositions()
        {
            var root = new GameObject("CrowdRepulsionRegression");
            try
            {
                var head = new GameObject("Head");
                head.transform.SetParent(root.transform);
                head.transform.position = Vector3.one * 10f;
                var embodiment = root.AddComponent<XRUserEmbodimentBridge>();
                var bus = new SimulationCommandBus();
                embodiment.Initialize(bus, head.transform);
                // A synthetic tracked hand at the origin is well away from the head.
                SetField(embodiment, "<IsLeftHandTracked>k__BackingField", true);
                var scenario = new CrowdAvoidanceScenario();
                SetField(scenario, "_agentCount", 1);
                scenario.OnScenarioStart(new ScenarioContext
                {
                    Bus = bus,
                    CoordinateMapper = new SpatialCoordinateMapper(0.2f, Vector3.zero),
                    Embodiment = embodiment
                });

                AssertCrowdDisplacement(scenario, bus, false, 1f, 5f, false);
                AssertCrowdDisplacement(scenario, bus, true, 1f, 0f, false);
                AssertCrowdDisplacement(scenario, bus, true, 0.1f, 5f, false);
                AssertCrowdDisplacement(scenario, bus, true, 1f, 5f, true);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MusicalEncounters_FrameChangePreservesCalibratedMotionOnFollowingTick()
        {
            var originalMapper = new SpatialCoordinateMapper(0.2f, Vector3.zero);
            var changedMapper = new SpatialCoordinateMapper(0.2f, Vector3.zero);
            var originalPositions = new Dictionary<int, Vector3>();
            var changedPositions = new Dictionary<int, Vector3>();
            var originalBus = new SimulationCommandBus();
            var changedBus = new SimulationCommandBus();
            originalBus.OnSetAgentPositionUnity += (id, position) => originalPositions[id] = position;
            changedBus.OnSetAgentPositionUnity += (id, position) => changedPositions[id] = position;
            var original = new MusicalEncountersScenario();
            var changed = new MusicalEncountersScenario();
            original.OnScenarioStart(new ScenarioContext { Bus = originalBus, CoordinateMapper = originalMapper });
            changed.OnScenarioStart(new ScenarioContext { Bus = changedBus, CoordinateMapper = changedMapper });
            changedMapper.OnFrameChanged += changed.ApplySpatialFrameChange;

            changedMapper.OriginOffset = new Vector3(2f, 3f, 4f);
            changedMapper.OriginRotation = Quaternion.Euler(0f, 90f, 0f);
            changedMapper.WorldScale = 0.4f;
            original.OnScenarioTick(0.02f);
            changed.OnScenarioTick(0.02f);

            foreach (var pair in originalPositions)
            {
                Vector3 wire = originalMapper.UnityMetersToOscMillimeters(pair.Value);
                Vector3 expected = changedMapper.OscMillimetersToUnityMeters(wire.x, wire.y, wire.z);
                Assert.Less(Vector3.Distance(expected, changedPositions[pair.Key]), 0.0001f,
                    "A later scenario tick must retain translation, rotation, scale, and velocity calibration.");
            }
        }

        private static void AssertCrowdDisplacement(CrowdAvoidanceScenario scenario, SimulationCommandBus bus,
            bool enabled, float radius, float force, bool shouldMove)
        {
            Vector3 start = new Vector3(0.15f, 0f, 0f);
            GetField<Vector3[]>(scenario, "_positions")[0] = start;
            GetField<Vector3[]>(scenario, "_velocities")[0] = Vector3.zero;
            bus.ConfigureUserRepeller(enabled, radius, force);
            scenario.OnScenarioTick(0.02f);
            float displacement = GetField<Vector3[]>(scenario, "_positions")[0].x - start.x;
            if (shouldMove) Assert.Greater(displacement, 0f, "The tracked hands must repel nearby agents even with the head far away.");
            else Assert.AreEqual(0f, displacement, 0.000001f);
        }

        private static T GetField<T>(object target, string name)
            => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
