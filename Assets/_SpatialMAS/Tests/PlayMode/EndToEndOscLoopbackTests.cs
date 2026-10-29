using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Environment;
using SpatialMAS.LocalComm;
using SpatialMAS.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpatialMAS.Tests.PlayMode
{
    public class EndToEndOscLoopbackTests
    {
        [UnityTest]
        public IEnumerator OscProtocolRouter_ProcessesInstantiatePositionColorAndSynthMessages()
        {
            var bus = new SimulationCommandBus();
            var mapper = new SpatialCoordinateMapper(worldScale: 1.0f, originOffset: Vector3.zero);
            var server = new OscUdpServer();
            var client = new OscUdpClient("127.0.0.1", 6099);
            var router = new OscProtocolRouter(bus, mapper, server, client);

            var rootGo = new GameObject("EndToEndOscRoot");
            var agentMgr = rootGo.AddComponent<AgentManager>();
            agentMgr.Initialize(bus);

            // Inject incoming OSC v1 messages into server queue
            server.InjectTestMessage(new OscMessage("/agents/instantiate", 0, 0, 5, 0.1f, "127.0.0.1", 6099));
            server.InjectTestMessage(new OscMessage("/agents/position/id", 2, 1000f, 2000f, 500f));
            server.InjectTestMessage(new OscMessage("/agents/color/id", 2, 0.9f, 0.2f, 0.1f, 1.0f));
            server.InjectTestMessage(new OscMessage("/agents/audio/synth/param_val", 2, 82, 0.08f));

            router.ProcessIncomingMessages();
            yield return null;

            Assert.AreEqual(5, agentMgr.ActiveCount);
            Assert.IsTrue(agentMgr.TryGetAgent(2, out AgentEntity agent2));

            // (1000mm, 2000mm, 500mm) -> Unity (1.0m, 0.5m, 2.0m)
            Assert.AreEqual(1.0f, agent2.transform.position.x, 0.001f);
            Assert.AreEqual(0.5f, agent2.transform.position.y, 0.001f);
            Assert.AreEqual(2.0f, agent2.transform.position.z, 0.001f);
            Assert.AreEqual(0.9f, agent2.Visuals.CurrentColor.r, 0.01f);
            Assert.AreEqual(0.08f, agent2.Synth.AttackSeconds, 0.001f);

            // Verify HS-ims v1 multi-agent position batching, hex color strings, and 4-arg synth param_val
            server.InjectTestMessage(new OscMessage("/agents/position/id", 0, 500f, 0f, 0f, 1, -500f, 0f, 1000f));
            server.InjectTestMessage(new OscMessage("/agents/color/id", 0, "#00FF00", 0.8f));
            server.InjectTestMessage(new OscMessage("/agents/audio/synth/param_val", 0, 0, 74, 0.15f));
            router.ProcessIncomingMessages();
            yield return null;

            Assert.IsTrue(agentMgr.TryGetAgent(0, out AgentEntity agent0));
            Assert.IsTrue(agentMgr.TryGetAgent(1, out AgentEntity agent1));
            Assert.AreEqual(0.5f, agent0.transform.position.x, 0.001f);
            Assert.AreEqual(-0.5f, agent1.transform.position.x, 0.001f);
            Assert.AreEqual(1.0f, agent1.transform.position.y, 0.001f);
            Assert.AreEqual(1.0f, agent0.Visuals.CurrentColor.g, 0.01f);
            Assert.AreEqual(0.15f, agent0.Synth.AttackSeconds, 0.001f);

            router.Dispose();
            server.Dispose();
            client.Dispose();
            Object.Destroy(rootGo);
        }
    }

    public class LegacyOscAndFrameRegressionTests
    {
        private GameObject _root;
        private SimulationCommandBus _bus;
        private SpatialCoordinateMapper _mapper;
        private AgentManager _agents;
        private BoundaryManager _boundaries;
        private LocalCommManager _localComm;
        private OscUdpServer _server;
        private OscUdpClient _client;
        private OscProtocolRouter _router;

        [SetUp]
        public void SetUp()
        {
            _bus = new SimulationCommandBus();
            _mapper = new SpatialCoordinateMapper();
            _mapper.OnFrameChanged += _bus.ApplySpatialFrameChange;
            _root = new GameObject("OscRegressionRoot");
            _agents = _root.AddComponent<AgentManager>();
            _agents.Initialize(_bus, mapper: _mapper);
            _boundaries = _root.AddComponent<BoundaryManager>();
            _boundaries.Initialize(_bus, _agents);
            _root.AddComponent<EnvironmentArrowManager>().Initialize(_bus);
            _localComm = _root.AddComponent<LocalCommManager>();
            _localComm.Initialize(_bus, _agents);
            _server = new OscUdpServer();
            _client = new OscUdpClient();
            _router = new OscProtocolRouter(_bus, _mapper, _server, _client);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _router.Dispose();
            _server.Dispose();
            _client.Dispose();
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RemoveCountAndFilters_PreserveUnselectedAgentsAndExplicitIds()
        {
            _router.DispatchMessage(new OscMessage("/agents/instantiate", 5));
            _router.DispatchMessage(new OscMessage("/agents/remove", 2));
            Assert.AreEqual(3, _agents.ActiveCount);
            Assert.IsTrue(_agents.TryGetAgent(2, out _));
            Assert.IsFalse(_agents.TryGetAgent(4, out _));

            _router.DispatchMessage(new OscMessage("/agents/add/id", 1, 0, 10, 11));
            _router.DispatchMessage(new OscMessage("/agents/remove", 1, 1, 0));
            Assert.IsTrue(_agents.TryGetAgent(10, out _));
            Assert.IsFalse(_agents.TryGetAgent(11, out _));
            _router.DispatchMessage(new OscMessage("/agents/remove", 1, -1, -1));
            Assert.AreEqual(3, _agents.ActiveCount);
            _router.DispatchMessage(new OscMessage("/agents/remove/id", 1));
            Assert.IsFalse(_agents.TryGetAgent(1, out _));
            Assert.IsTrue(_agents.TryGetAgent(0, out _));
            yield return null;
        }

        [UnityTest]
        public IEnumerator AddInSameFrame_AllocatesDistinctIdsAndAppliesInitialPositions()
        {
            _mapper.OriginOffset = new Vector3(2f, 1f, 3f);
            _router.DispatchMessage(new OscMessage("/agents/add", 2, 0, 0));
            _router.DispatchMessage(new OscMessage("/agents/add", 2, 0, 0, 2,
                1000f, 2000f, 500f, -1000f, 0f, 0f));
            Assert.AreEqual(4, _agents.ActiveCount);
            Assert.IsTrue(_agents.TryGetAgent(0, out AgentEntity first));
            Assert.IsTrue(_agents.TryGetAgent(2, out AgentEntity explicitPosition));
            Assert.That(Vector3.Distance(first.transform.position, _mapper.OriginOffset), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(explicitPosition.transform.position,
                _mapper.OscMillimetersToUnityMeters(1000f, 2000f, 500f)), Is.LessThan(0.0001f));
            _router.DispatchMessage(new OscMessage("/agents/add", 1, 0, 0, 1, 2f));
            Assert.IsTrue(_agents.TryGetAgent(4, out AgentEntity randomPosition));
            Assert.That(Vector3.Distance(randomPosition.transform.position, _mapper.OriginOffset), Is.LessThanOrEqualTo(2f));
            _router.DispatchMessage(new OscMessage("/agents/instantiate", 1, 0, 1));
            Assert.IsTrue(_agents.TryGetAgent(0, out AgentEntity reusedFollowAgent));
            Assert.That(Vector3.Distance(reusedFollowAgent.transform.position, _mapper.OriginOffset), Is.LessThan(0.0001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SynthFlushAndRemove_OnlyAffectSelectedAgents()
        {
            _router.DispatchMessage(new OscMessage("/agents/instantiate", 3));
            var flushed = new List<int>();
            _bus.OnFlushAgentSynth += flushed.Add;
            _router.DispatchMessage(new OscMessage("/agents/audio/synth/flush", 1, 0));
            CollectionAssert.AreEqual(new[] { 1 }, flushed);
            _router.DispatchMessage(new OscMessage("/agents/audio/synth/remove/id", 2));
            Assert.IsTrue(_agents.TryGetAgent(2, out AgentEntity removed));
            Assert.IsTrue(_agents.TryGetAgent(0, out AgentEntity unaffected));
            Assert.IsTrue(removed.Synth.IsMuted);
            Assert.IsFalse(unaffected.Synth.IsMuted);
            CollectionAssert.AreEqual(new[] { 1, 2 }, flushed);
            _router.DispatchMessage(new OscMessage("/agents/audio/synth/add/id", 2));
            Assert.IsFalse(removed.Synth.IsMuted);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BoundaryCommands_PreserveSphereRadiusCubeAxesAndAppearance()
        {
            _router.DispatchMessage(new OscMessage("/boundary/add/id", 7, 0, 0f, 0f, 0f, 1000f, 0.3f, "ff0000"));
            _router.DispatchMessage(new OscMessage("/boundary/scale/id", 7, 1000f));
            BoundaryInstance sphere = _boundaries.ActiveBoundaries[7];
            Assert.AreEqual(1f, sphere.SizeMeters, 0.0001f);
            Assert.AreEqual(2f, sphere.GameObject.transform.localScale.x, 0.0001f);
            Assert.AreEqual(0.3f, sphere.Color.a, 0.0001f);
            Assert.AreEqual(1f, sphere.Color.r);
            Assert.AreEqual(0f, sphere.Color.g);

            _router.DispatchMessage(new OscMessage("/boundary/add/id", 8, 1, 0f, 0f, 0f,
                2000f, 4000f, 6000f, 0.2f, "00ff00"));
            BoundaryInstance cube = _boundaries.ActiveBoundaries[8];
            Assert.That(Vector3.Distance(new Vector3(2f, 6f, 4f), cube.GameObject.transform.localScale), Is.LessThan(0.0001f));
            Assert.IsTrue(BoundaryManager.IsPositionInsideBoundary(new Vector3(0f, 2.9f, 1.9f), cube));
            Assert.IsFalse(BoundaryManager.IsPositionInsideBoundary(new Vector3(1.1f, 0f, 0f), cube));
            _router.DispatchMessage(new OscMessage("/boundary/scale/id", 8, 1000f, 3000f, 5000f));
            Assert.That(Vector3.Distance(new Vector3(1f, 5f, 3f), cube.GameObject.transform.localScale), Is.LessThan(0.0001f));
            Assert.AreEqual(0.2f, cube.Color.a, 0.0001f);
            Assert.AreEqual(1f, cube.Color.g);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LegacyCubeCreation_InitializesMembershipBeforeEmittingResizeCrossings()
        {
            _router.DispatchMessage(new OscMessage("/agents/instantiate", 1));
            _router.DispatchMessage(new OscMessage("/agents/position/id", 0, 1500f, 0f, 0f));
            int transitions = 0;
            int lastTransition = -1;
            _bus.OnBoundaryTransitionEvent += (agent, boundary, transition) =>
            {
                transitions++;
                lastTransition = transition;
            };

            _router.DispatchMessage(new OscMessage("/boundary/add/id", 8, 1, 0f, 0f, 0f, 2000f, 2000f, 2000f));
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(0, transitions, "An agent outside a newly created cube has not crossed its surface.");

            _router.DispatchMessage(new OscMessage("/boundary/scale/id", 8, 4000f, 2000f, 2000f));
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, transitions, "A later resize must still report the real outside-to-inside transition.");
            Assert.AreEqual(0, lastTransition);
        }

        [UnityTest]
        public IEnumerator VelocityPositionAndFollowZeroSpeed_RespectExplicitCommands()
        {
            _router.DispatchMessage(new OscMessage("/agents/instantiate/id", 0, 2, 1));
            _router.DispatchMessage(new OscMessage("/agents/position/id", 1, 1000f, 2000f, 3000f));
            Assert.IsTrue(_agents.TryGetAgent(1, out AgentEntity velocityAgent));
            Assert.That(Vector3.Distance(new Vector3(1f, 3f, 2f), velocityAgent.transform.position), Is.LessThan(0.0001f));
            _router.DispatchMessage(new OscMessage("/agents/instantiate/id", 0, 1, 2));
            Assert.IsTrue(_agents.TryGetAgent(2, out AgentEntity followAgent));
            followAgent.PlaceImmediately(Vector3.zero);
            _router.DispatchMessage(new OscMessage("/agents/position/id", 2, 1000f, 0f, 0f));
            _router.DispatchMessage(new OscMessage("/agents/speed/id", 2, 0f));
            yield return null;
            yield return null;
            Assert.AreEqual(Vector3.zero, followAgent.transform.position);
            _router.DispatchMessage(new OscMessage("/agents/speed/id", 2, 1000f));
            yield return null;
            yield return null;
            Assert.That(followAgent.transform.position.x, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator FrameChanges_RebaseExistingWorldSizesTargetsAndWavefronts()
        {
            _router.DispatchMessage(new OscMessage("/agents/instantiate", 2));
            _router.DispatchMessage(new OscMessage("/agents/position/id", 0, 0f, 0f, 0f, 1, 500f, 0f, 0f));
            _router.DispatchMessage(new OscMessage("/agents/radius/id", 1, 1000f, 0.2f, "ffffff"));
            _router.DispatchMessage(new OscMessage("/boundary/add/id", 7, 0, 0f, 0f, 0f, 1000f));
            _router.DispatchMessage(new OscMessage("/environment/set_arrow", 4, 0f, 0f, 0f, 1000f, 0f, 0f, 20f));
            _bus.ConfigureLocalComm(1f, 0.04f, false);
            int deliveries = 0;
            _bus.OnLocalCommDeliveredUnity += (type, sender, receiver, distance, args) => deliveries++;
            _bus.TriggerLocalComm("test", 0, 0f, 0f, new object[] { "payload" });
            _mapper.WorldScale = 0.5f;
            _mapper.OriginOffset = new Vector3(2f, 1f, -3f);
            _mapper.OriginRotation = Quaternion.Euler(0f, 90f, 0f);

            Assert.IsTrue(_agents.TryGetAgent(1, out AgentEntity agent));
            Assert.That(Vector3.Distance(agent.transform.position,
                _mapper.OscMillimetersToUnityMeters(500f, 0f, 0f)), Is.LessThan(0.0001f));
            Assert.AreEqual(0.14f, agent.BaseScaleMeters, 0.0001f);
            Assert.AreEqual(1f, agent.transform.Find("RadiusVisualizer").lossyScale.x, 0.0001f);
            Assert.AreEqual(0.5f, _boundaries.ActiveBoundaries[7].SizeMeters, 0.0001f);
            Transform arrow = _root.transform.Find("EnvironmentArrow_4");
            Assert.AreEqual(0.5f, arrow.localScale.z, 0.0001f);
            Assert.That(Vector3.Distance(arrow.position, _mapper.OriginOffset), Is.LessThan(0.0001f));
            Assert.AreEqual(0.5f, _localComm.DefaultMaxRangeMeters, 0.0001f);
            yield return new WaitForSeconds(0.08f);
            Assert.AreEqual(1, deliveries, "A pending packet must follow the transformed arena and reach the same neighbor.");
        }
    }
}
