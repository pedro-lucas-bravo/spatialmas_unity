using System.Collections;
using NUnit.Framework;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Environment;
using SpatialMAS.LocalComm;
using SpatialMAS.Scenarios;
using SpatialMAS.XR;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpatialMAS.Tests.PlayMode
{
    public class AgentAndBoundaryPlayModeTests
    {
        [UnityTest]
        public IEnumerator InstantiateAgentsAndCrossBoundary_EmitsBoundaryTouchedEvent()
        {
            var bus = new SimulationCommandBus();
            var rootGo = new GameObject("PlayModeTestRoot");
            var agentMgr = rootGo.AddComponent<AgentManager>();
            agentMgr.Initialize(bus);

            var boundMgr = rootGo.AddComponent<BoundaryManager>();
            boundMgr.Initialize(bus, agentMgr);

            // Spawn 3 agents first, place agent 0 at origin, then add spherical boundary ID 7 at origin with radius 1.0m
            bus.InstantiateAgents(AgentShapeType.Sphere, AgentMovementMode.Teleport, 3, 0.1f, "127.0.0.1", 6012);
            bus.SetAgentPositionUnity(0, Vector3.zero);
            bus.AddBoundaryUnity(7, AgentShapeType.Sphere, Vector3.zero, 1.0f);

            int touchedAgentId = -1;
            int touchedBoundaryId = -1;
            bus.OnBoundaryTouchedEvent += (aId, bId) =>
            {
                touchedAgentId = aId;
                touchedBoundaryId = bId;
            };

            yield return new WaitForFixedUpdate();

            // Now move agent 0 outside boundary (x = 2.5m > radius 1.0m)
            bus.SetAgentPositionUnity(0, new Vector3(2.5f, 0f, 0f));

            yield return new WaitForFixedUpdate();

            Assert.AreEqual(0, touchedAgentId);
            Assert.AreEqual(7, touchedBoundaryId);

            Object.Destroy(rootGo);
        }

        [UnityTest]
        public IEnumerator ScenarioManager_LoadsAndStepsAllSixScenariosWithoutErrors()
        {
            var bus = new SimulationCommandBus();
            var mapper = new SpatialCoordinateMapper(worldScale: 0.25f, originOffset: Vector3.zero);
            var rootGo = new GameObject("ScenarioTestRoot");

            var agentMgr = rootGo.AddComponent<AgentManager>();
            agentMgr.Initialize(bus);

            var boundMgr = rootGo.AddComponent<BoundaryManager>();
            boundMgr.Initialize(bus, agentMgr);

            var localCommMgr = rootGo.AddComponent<LocalCommManager>();
            localCommMgr.Initialize(bus, agentMgr);

            var embodimentBridge = rootGo.AddComponent<XRUserEmbodimentBridge>();
            var scenarioMgr = rootGo.AddComponent<ScenarioManager>();
            scenarioMgr.Initialize(bus, mapper, agentMgr, embodimentBridge, 0);

            int[] scenarioOrder = { 1, 2, 3, 4, 5, 0 };
            foreach (int id in scenarioOrder)
            {
                bus.LoadScenario(id);
                yield return null;
                yield return null;
                Assert.AreEqual(id, scenarioMgr.ActiveScenarioId);
            }

            Object.Destroy(rootGo);
        }
    }
}
