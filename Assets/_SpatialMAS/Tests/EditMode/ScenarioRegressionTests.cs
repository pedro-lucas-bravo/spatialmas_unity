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
                    ScenarioCenter = Vector3.zero,
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

        [TestCase(ScenarioLayout.Compact)]
        [TestCase(ScenarioLayout.RoomScale)]
        public void PhysicalLayouts_ResetAtSameCenterPreservesDimensionsAndPositions(ScenarioLayout layout)
        {
            foreach (IScenarioModule scenario in new IScenarioModule[]
            {
                new CrowdAvoidanceScenario(), new SwarmalatorsScenario(), new AttractorsRepellersScenario(),
                new MusicalEncountersScenario(), new SelfAssemblyScenario()
            })
            {
                var bus = new SimulationCommandBus();
                var positions = new Dictionary<int, Vector3>();
                var dimensions = new List<float>();
                bus.OnSetAgentPositionUnity += (id, position) => positions[id] = position;
                bus.OnInstantiateAgents += (shape, mode, count, size, ip, port) => dimensions.Add(size);
                bus.OnAddAgents += (shape, mode, size, ip, port, ids) => dimensions.Add(size);
                bus.OnAddBoundaryUnity += (id, shape, center, radius) => dimensions.Add(radius);
                var context = new ScenarioContext
                {
                    Bus = bus, ScenarioCenter = new Vector3(2f, 1.2f, 3f), Layout = layout
                };
                scenario.OnScenarioStart(context);
                var expectedPositions = new Dictionary<int, Vector3>(positions);
                var expectedDimensions = dimensions.ToArray();
                scenario.OnScenarioTick(0.02f);
                scenario.OnScenarioStop();
                positions.Clear();
                dimensions.Clear();
                scenario.OnScenarioStart(context);
                CollectionAssert.AreEqual(expectedDimensions, dimensions, scenario.DisplayName);
                Assert.AreEqual(expectedPositions.Count, positions.Count, scenario.DisplayName);
                foreach (var pair in expectedPositions)
                    Assert.Less(Vector3.Distance(pair.Value, positions[pair.Key]), 0.00001f, scenario.DisplayName);
                scenario.OnScenarioStop();
            }
        }

        [Test]
        public void PhysicalLayouts_SwitchingLayoutsDoesNotCompoundDimensionsOrInitialPositions()
        {
            foreach (IScenarioModule scenario in new IScenarioModule[]
            {
                new CrowdAvoidanceScenario(), new SwarmalatorsScenario(), new AttractorsRepellersScenario(),
                new MusicalEncountersScenario(), new SelfAssemblyScenario()
            })
            {
                var bus = new SimulationCommandBus();
                var positions = new Dictionary<int, Vector3>();
                var dimensions = new List<float>();
                bus.OnSetAgentPositionUnity += (id, position) => positions[id] = position;
                bus.OnInstantiateAgents += (shape, mode, count, size, ip, port) => dimensions.Add(size);
                bus.OnAddAgents += (shape, mode, size, ip, port, ids) => dimensions.Add(size);
                bus.OnAddBoundaryUnity += (id, shape, center, radius) => dimensions.Add(radius);
                var context = new ScenarioContext { Bus = bus, Layout = ScenarioLayout.RoomScale };
                scenario.OnScenarioStart(context);
                var expectedPositions = new Dictionary<int, Vector3>(positions);
                var expectedDimensions = dimensions.ToArray();
                scenario.OnScenarioStop();
                context.Layout = ScenarioLayout.Compact;
                scenario.OnScenarioStart(context);
                scenario.OnScenarioTick(0.02f);
                scenario.OnScenarioStop();
                positions.Clear();
                dimensions.Clear();
                context.Layout = ScenarioLayout.RoomScale;
                scenario.OnScenarioStart(context);
                CollectionAssert.AreEqual(expectedDimensions, dimensions, scenario.DisplayName);
                foreach (var pair in expectedPositions)
                    Assert.Less(Vector3.Distance(pair.Value, positions[pair.Key]), 0.00001f, scenario.DisplayName);
                scenario.OnScenarioStop();
            }
        }

        [Test]
        public void PhysicalLayouts_UseLiteralDimensionsAndMetersPerSecond()
        {
            foreach (var layout in new[] { ScenarioLayout.Compact, ScenarioLayout.RoomScale })
            {
                var context = new ScenarioContext { Bus = new SimulationCommandBus(), Layout = layout };
                var crowd = new CrowdAvoidanceScenario();
                crowd.OnScenarioStart(context);
                Assert.AreEqual(layout == ScenarioLayout.RoomScale ? 3f : 1.3f, GetField<float>(crowd, "_arenaRadiusMeters"));
                Assert.AreEqual(layout == ScenarioLayout.RoomScale ? 1.2f : 0.65f, GetField<float>(crowd, "_cruiseSpeed"));
                var encounters = new MusicalEncountersScenario();
                encounters.OnScenarioStart(context);
                Assert.AreEqual(layout == ScenarioLayout.RoomScale ? 2.5f : 1f, GetField<float>(encounters, "_boundaryRadiusMeters"));
                Assert.AreEqual(layout == ScenarioLayout.RoomScale ? 1.1f : 0.65f, GetField<float>(encounters, "_speed"));
                var assembly = new SelfAssemblyScenario();
                assembly.OnScenarioStart(context);
                Assert.AreEqual(layout == ScenarioLayout.RoomScale ? 0.15f : 0.084f, GetField<float>(assembly, "_cubeSizeMeters"));
            }
        }

        [Test]
        public void ScenarioPlacement_TranslatesPositionsWithoutChangingDimensions()
        {
            Vector3 translation = new Vector3(3f, 2f, -4f);
            foreach (IScenarioModule scenario in new IScenarioModule[]
            {
                new CrowdAvoidanceScenario(), new SwarmalatorsScenario(), new AttractorsRepellersScenario(),
                new MusicalEncountersScenario(), new SelfAssemblyScenario()
            })
            {
                var bus = new SimulationCommandBus();
                var positions = new Dictionary<int, Vector3>();
                var dimensions = new List<float>();
                bus.OnSetAgentPositionUnity += (id, position) => positions[id] = position;
                bus.OnInstantiateAgents += (shape, mode, count, size, ip, port) => dimensions.Add(size);
                bus.OnAddAgents += (shape, mode, size, ip, port, ids) => dimensions.Add(size);
                var context = new ScenarioContext { Bus = bus, ScenarioCenter = Vector3.zero };
                scenario.OnScenarioStart(context);
                var expectedPositions = new Dictionary<int, Vector3>(positions);
                var expectedDimensions = dimensions.ToArray();
                scenario.OnScenarioStop();
                positions.Clear();
                dimensions.Clear();
                context.ScenarioCenter = translation;
                scenario.OnScenarioStart(context);
                CollectionAssert.AreEqual(expectedDimensions, dimensions, scenario.DisplayName);
                foreach (var pair in expectedPositions)
                    Assert.Less(Vector3.Distance(pair.Value + translation, positions[pair.Key]), 0.00001f, scenario.DisplayName);
                scenario.OnScenarioStop();
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
