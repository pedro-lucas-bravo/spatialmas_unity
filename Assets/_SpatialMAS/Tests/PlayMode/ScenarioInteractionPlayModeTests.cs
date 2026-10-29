using System.Collections;
using NUnit.Framework;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Scenarios;
using SpatialMAS.Scenarios.BuiltIn;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpatialMAS.Tests.PlayMode
{
    public class ScenarioInteractionPlayModeTests
    {
        [UnityTest]
        public IEnumerator SelfAssembly_DockingAppliesRootRotationToVisibleChild()
        {
            var root = new GameObject("SelfAssemblyRotationRegression");
            try
            {
                var bus = new SimulationCommandBus();
                var agents = root.AddComponent<AgentManager>();
                agents.Initialize(bus);
                var scenario = new SelfAssemblyScenario();
                scenario.OnScenarioStart(new ScenarioContext
                {
                    Bus = bus,
                    AgentManager = agents,
                    ScenarioCenter = Vector3.zero
                });
                foreach (var node in scenario.Nodes)
                {
                    node.StateTimer = 100f;
                    node.Velocity = Vector3.zero;
                }
                var parent = scenario.Nodes[0];
                var child = scenario.Nodes[1];
                parent.State = child.State = SelfAssemblyScenario.AssemblyState.Joined;
                parent.Rotation = Quaternion.Euler(0f, 90f, 0f);
                parent.ConnectedNeighborIds[0] = child.Id;
                child.ConnectedNeighborIds[1] = parent.Id;
                child.RootStructureId = parent.Id;
                child.TargetParentId = parent.Id;
                child.TargetSlotIndex = 0;

                scenario.OnScenarioTick(0.02f);

                Assert.IsTrue(agents.TryGetAgent(child.Id, out var entity));
                Assert.Less(Quaternion.Angle(parent.Rotation, entity.transform.rotation), 0.001f);
                Vector3 expected = SelfAssemblyScenario.GetSlotWorldPosition(parent.Position, parent.Rotation, 0, entity.BaseScaleMeters);
                Assert.Less(Vector3.Distance(expected, entity.transform.position), 0.0001f);
                scenario.OnScenarioStop();
                yield return null;
            }
            finally
            {
                Object.Destroy(root);
            }
        }

        [UnityTest]
        public IEnumerator NextPlacementAndLayout_DoNotMoveLiveEntitiesUntilReset()
        {
            var root = new GameObject("PhysicalScenarioPlacementRegression");
            try
            {
                var bus = new SimulationCommandBus();
                var agents = root.AddComponent<AgentManager>();
                agents.Initialize(bus);
                var manager = root.AddComponent<ScenarioManager>();
                manager.Initialize(bus, Vector3.zero, agents, null, 4);
                manager.SetPaused(true);
                Assert.IsTrue(agents.TryGetAgent(0, out var agent));
                Vector3 originalPosition = agent.transform.position;
                float originalSize = agent.BaseScaleMeters;
                Vector3 newCenter = new Vector3(4f, 2f, 3f);
                bus.SetScenarioCenter(newCenter);
                bus.SetScenarioLayout(ScenarioLayout.RoomScale);
                Assert.AreEqual(originalPosition, agent.transform.position);
                Assert.AreEqual(originalSize, agent.BaseScaleMeters);
                Assert.AreEqual(ScenarioLayout.Compact, manager.ActiveLayout);
                Assert.AreEqual(ScenarioLayout.RoomScale, manager.NextLayout);
                Assert.AreEqual(newCenter, manager.NextScenarioCenter);

                manager.ResetActiveScenario();
                manager.SetPaused(true);
                Assert.AreEqual(ScenarioLayout.RoomScale, manager.ActiveLayout);
                Assert.IsTrue(agents.TryGetAgent(0, out var restarted));
                Assert.AreEqual(0.14f, restarted.BaseScaleMeters, 0.000001f);
                Assert.Less(Vector3.Distance(restarted.transform.position, newCenter), 2.5f);
                Vector3 restartedPosition = restarted.transform.position;
                manager.ResetActiveScenario();
                manager.SetPaused(true);
                Assert.IsTrue(agents.TryGetAgent(0, out var repeated));
                Assert.Less(Vector3.Distance(restartedPosition, repeated.transform.position), 0.00001f);
                Assert.AreEqual(0.14f, repeated.BaseScaleMeters, 0.000001f);
                yield return null;
            }
            finally { Object.Destroy(root); }
        }

        [UnityTest]
        public IEnumerator ExternalOscTransfer_PreservesAndUnmutesExistingAgents()
        {
            var root = new GameObject("ExternalOscTransferRegression");
            try
            {
                var bus = new SimulationCommandBus();
                var agents = root.AddComponent<AgentManager>();
                agents.Initialize(bus);
                var manager = root.AddComponent<ScenarioManager>();
                manager.Initialize(bus, Vector3.zero, agents, null, 1);
                Assert.IsTrue(agents.TryGetAgent(0, out var preservedAgent));
                int count = agents.ActiveCount;

                bus.LoadScenario(0);

                Assert.AreEqual(count, agents.ActiveCount);
                Assert.IsTrue(agents.TryGetAgent(0, out var transferredAgent));
                Assert.AreSame(preservedAgent, transferredAgent);
                foreach (var pair in agents.ActiveAgents) Assert.IsFalse(pair.Value.Synth.IsMuted);
                yield return null;
            }
            finally
            {
                Object.Destroy(root);
            }
        }
    }
}
