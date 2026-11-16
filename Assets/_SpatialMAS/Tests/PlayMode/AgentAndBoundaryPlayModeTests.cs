using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Environment;
using SpatialMAS.LocalComm;
using SpatialMAS.Scenarios;
using SpatialMAS.Scenarios.BuiltIn;
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
        public IEnumerator MusicalEncounters_TranslatesSpheresAndSustainsNotesUntilExit()
        {
            var bus = new SimulationCommandBus();
            var root = new GameObject("MusicalEncountersTest");
            var agents = root.AddComponent<AgentManager>();
            agents.Initialize(bus);
            var boundaries = root.AddComponent<BoundaryManager>();
            boundaries.Initialize(bus, agents);
            var scenario = new MusicalEncountersScenario();
            scenario.OnScenarioStart(new ScenarioContext { Bus = bus, AgentManager = agents });

            Vector3[] sphereStarts = new Vector3[5];
            for (int i = 0; i < sphereStarts.Length; i++) sphereStarts[i] = agents.ActiveAgents[i].transform.position;
            var cube = agents.ActiveAgents[5];
            Vector3 cubeStart = cube.transform.position;
            Vector3 delta = new Vector3(0.5f, 0.25f, 0.125f);
            var handle = boundaries.ActiveBoundaries[1].Handle;
            Assert.AreEqual(BoundaryHandleShape.Diamond, handle.Shape);
            Assert.AreEqual(7, agents.ActiveCount);
            handle.MoveByUser(delta);
            Assert.AreEqual(delta, boundaries.ActiveBoundaries[1].CenterUnity);
            Assert.AreEqual(delta, handle.transform.position);
            Assert.AreEqual(cubeStart, cube.transform.position);
            for (int i = 0; i < sphereStarts.Length; i++)
            {
                Assert.That(Vector3.Distance(sphereStarts[i] + delta, agents.ActiveAgents[i].transform.position), Is.LessThan(0.0001f));
                Assert.AreEqual(0.6f, agents.ActiveAgents[i].transform.Find("RadiusVisualizer").lossyScale.x, 0.0001f);
            }

            Vector3 externalCenter = new Vector3(-0.4f, 0.15f, 0.7f);
            bus.UpdateBoundaryUnity(1, externalCenter, -1f);
            Assert.AreEqual(externalCenter, handle.transform.position);
            Assert.AreEqual(cubeStart, cube.transform.position);
            for (int i = 0; i < sphereStarts.Length; i++)
                Assert.That(Vector3.Distance(sphereStarts[i] + externalCenter, agents.ActiveAgents[i].transform.position), Is.LessThan(0.0001f));

            var notes = new List<(int action, int pitch)>();
            bus.OnSetAgentSynthNote += (id, action, pitch, _) => { if (id == 5) notes.Add((action, pitch)); };
            bus.SetAgentPositionUnity(5, new Vector3(4f, 0f, 0f));
            bus.SetAgentPositionUnity(6, new Vector3(4.1f, 0f, 0f));
            for (int i = 0; i < 20; i++) scenario.OnScenarioTick(0.05f);
            Assert.AreEqual(1, notes.Count);
            Assert.AreEqual(1, notes[0].action);
            bus.SetAgentPositionUnity(6, new Vector3(5f, 0f, 0f));
            scenario.OnScenarioTick(0.05f);
            Assert.AreEqual(2, notes.Count);
            Assert.AreEqual((0, notes[0].pitch), notes[1]);

            scenario.OnScenarioStop();
            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ScenarioManager_LoadsAndStepsAllSixScenariosWithoutErrors()
        {
            var bus = new SimulationCommandBus();
            var rootGo = new GameObject("ScenarioTestRoot");

            var agentMgr = rootGo.AddComponent<AgentManager>();
            agentMgr.Initialize(bus);

            var boundMgr = rootGo.AddComponent<BoundaryManager>();
            boundMgr.Initialize(bus, agentMgr);

            var localCommMgr = rootGo.AddComponent<LocalCommManager>();
            localCommMgr.Initialize(bus, agentMgr);

            var embodimentBridge = rootGo.AddComponent<XRUserEmbodimentBridge>();
            var scenarioMgr = rootGo.AddComponent<ScenarioManager>();
            scenarioMgr.Initialize(bus, Vector3.zero, agentMgr, embodimentBridge, 0);

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
