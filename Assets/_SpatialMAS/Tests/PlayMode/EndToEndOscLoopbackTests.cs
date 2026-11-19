using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using SpatialMAS.Agents;
using SpatialMAS.Core;
using SpatialMAS.Environment;
using SpatialMAS.LocalComm;
using SpatialMAS.Networking;
using SpatialMAS.Scenarios;
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
            var mapper = new SpatialCoordinateMapper();
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

    public class LegacyOscAndWorldCoordinatesRegressionTests
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
            _root = new GameObject("OscRegressionRoot");
            _agents = _root.AddComponent<AgentManager>();
            _agents.Initialize(_bus);
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
            _router.DispatchMessage(new OscMessage("/agents/add", 2, 0, 0));
            _router.DispatchMessage(new OscMessage("/agents/add", 2, 0, 0, 2,
                1000f, 2000f, 500f, -1000f, 0f, 0f));
            Assert.AreEqual(4, _agents.ActiveCount);
            Assert.IsTrue(_agents.TryGetAgent(0, out AgentEntity first));
            Assert.IsTrue(_agents.TryGetAgent(2, out AgentEntity explicitPosition));
            Assert.That(Vector3.Distance(first.transform.position, Vector3.zero), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(explicitPosition.transform.position,
                _mapper.OscMillimetersToUnityMeters(1000f, 2000f, 500f)), Is.LessThan(0.0001f));
            _router.DispatchMessage(new OscMessage("/agents/add", 1, 0, 0, 1, 2f));
            Assert.IsTrue(_agents.TryGetAgent(4, out AgentEntity randomPosition));
            Assert.That(Vector3.Distance(randomPosition.transform.position, Vector3.zero), Is.LessThanOrEqualTo(2f));
            _router.DispatchMessage(new OscMessage("/agents/instantiate", 1, 0, 1));
            Assert.IsTrue(_agents.TryGetAgent(0, out AgentEntity reusedFollowAgent));
            Assert.That(Vector3.Distance(reusedFollowAgent.transform.position, Vector3.zero), Is.LessThan(0.0001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator CanonicalRandomSpawning_UsesMillimetersWhileLegacyRadiusUsesMeters()
        {
            UnityEngine.Random.State state = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(1234);
                _router.DispatchMessage(new OscMessage("/agents/add", 1, 0, 0, 1, 2f));
                Assert.IsTrue(_agents.TryGetAgent(0, out AgentEntity legacy));
                Vector3 expected = legacy.transform.position;
                UnityEngine.Random.InitState(1234);
                _router.DispatchMessage(new OscMessage("/world/agents/add", 1, 0, 0, 1, 2000f));
                Assert.IsTrue(_agents.TryGetAgent(1, out AgentEntity canonical));
                Assert.Less(Vector3.Distance(expected, canonical.transform.position), 0.0001f);
                Assert.LessOrEqual(canonical.transform.position.magnitude, 2f);
            }
            finally { UnityEngine.Random.state = state; }
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
        public IEnumerator CanonicalCoordinates_UsePhysicalDimensionsAndNeverChangeLegacyAxes()
        {
            _router.DispatchMessage(new OscMessage("/world/agents/instantiate", 0, 0, 2, 250f, "127.0.0.1", 6099));
            _router.DispatchMessage(new OscMessage("/world/agents/position/id", 0, 1000f, 2000f, 3000f));
            _router.DispatchMessage(new OscMessage("/agents/position/id", 1, 1000f, 2000f, 3000f));
            Assert.IsTrue(_agents.TryGetAgent(0, out AgentEntity canonical));
            Assert.IsTrue(_agents.TryGetAgent(1, out AgentEntity legacy));
            Assert.Less(Vector3.Distance(new Vector3(1f, 2f, 3f), canonical.transform.position), 0.0001f);
            Assert.Less(Vector3.Distance(new Vector3(1f, 3f, 2f), legacy.transform.position), 0.0001f);
            Assert.AreEqual(0.25f, canonical.BaseScaleMeters, 0.0001f, "250 wire mm must create a 0.25-meter unit-mesh dimension.");
            Quaternion rotation = Quaternion.Euler(15f, 40f, 5f);
            _router.DispatchMessage(new OscMessage("/world/agents/rotation/id", 0, rotation.x, rotation.y, rotation.z, rotation.w));
            Assert.Less(Quaternion.Angle(rotation, canonical.transform.rotation), 0.001f);
            _router.DispatchMessage(new OscMessage("/world/boundary/add/id", 7, 1, 1000f, 2000f, 3000f, 2000f, 4000f, 6000f));
            Assert.Less(Vector3.Distance(new Vector3(1f, 2f, 3f), _boundaries.ActiveBoundaries[7].CenterUnity), 0.0001f);
            Assert.Less(Vector3.Distance(new Vector3(2f, 4f, 6f), _boundaries.ActiveBoundaries[7].GameObject.transform.localScale), 0.0001f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OscScenarioSwitch_ClearsWorldAndKeepsCommandsAvailable()
        {
            var scenarios = _root.AddComponent<ScenarioManager>();
            scenarios.Initialize(_bus, Vector3.zero, _agents, null, 1, boundaries: _boundaries);
            _router.DispatchMessage(new OscMessage("/world/agents/position/id", 0, 100f, 200f, 300f));
            Assert.That(Vector3.Distance(_agents.ActiveAgents[0].transform.position, new Vector3(0.1f, 0.2f, 0.3f)), Is.LessThan(0.0001f));
            _router.DispatchMessage(new OscMessage("/scenario/load", 0));
            Assert.AreEqual(0, scenarios.ActiveScenarioId);
            Assert.AreEqual(0, _agents.ActiveCount);
            Assert.AreEqual(0, _boundaries.ActiveBoundaries.Count);
            _router.DispatchMessage(new OscMessage("/agents/instantiate/id", 0, 0, 12));
            Assert.IsTrue(_agents.ActiveAgents.ContainsKey(12));
            yield return null;
        }

        [UnityTest]
        public IEnumerator MusicalEncountersOscAgentCount_ResizesLiveAndPreservesCubesAndArena()
        {
            var scenarios = _root.AddComponent<ScenarioManager>();
            scenarios.Initialize(_bus, Vector3.zero, _agents, null);
            _router.DispatchMessage(new OscMessage("/scenario/load", 4));
            Assert.AreEqual(7, _agents.ActiveCount);
            var arena = _boundaries.ActiveBoundaries[1];
            var cube = _agents.ActiveAgents[5];
            var existingSphere = _agents.ActiveAgents[0];
            var cubePosition = new Vector3(2f, 1f, 3f);
            cube.PlaceImmediately(cubePosition);

            _router.DispatchMessage(new OscMessage("/scenario/param", "agentCount", 12));
            Assert.AreEqual(14, _agents.ActiveCount);
            Assert.AreSame(existingSphere, _agents.ActiveAgents[0]);
            Assert.AreSame(cube, _agents.ActiveAgents[5]);
            Assert.AreEqual(cubePosition, cube.transform.position);
            Assert.AreSame(arena, _boundaries.ActiveBoundaries[1]);
            var newSphere = _agents.ActiveAgents[13];
            Assert.AreEqual(AgentShapeType.Sphere, newSphere.ShapeType);
            Assert.IsFalse(newSphere.IsInteractable);
            Assert.IsNotNull(newSphere.transform.Find("RadiusVisualizer"));
            Vector3 initialPosition = newSphere.transform.position;
            scenarios.ActiveScenario.OnScenarioTick(0.02f);
            Assert.AreNotEqual(initialPosition, newSphere.transform.position);

            _router.DispatchMessage(new OscMessage("/scenario/param", "AGENTCOUNT", 0));
            Assert.AreEqual(2, _agents.ActiveCount);
            Assert.AreSame(cube, _agents.ActiveAgents[5]);
            Assert.AreEqual(cubePosition, cube.transform.position);
            Assert.AreSame(arena, _boundaries.ActiveBoundaries[1]);
            scenarios.ResetActiveScenario();
            Assert.AreEqual(2, _agents.ActiveCount);
            _router.DispatchMessage(new OscMessage("/scenario/param", "agentCount", 6));
            Assert.AreEqual(8, _agents.ActiveCount);
            Assert.IsTrue(_agents.ActiveAgents.ContainsKey(7));
            yield return null;
        }

        [UnityTest]
        public IEnumerator BoundaryHandle_ReportsBoundaryCenterAndAcceptsRelativeOffset()
        {
            _router.DispatchMessage(new OscMessage("/world/boundary/add/id", 7, 0, 0f, 0f, 0f, 1000f));
            _router.DispatchMessage(new OscMessage("/boundary/handle/configure/id", 7, 2, 130f, "ff00ff"));
            var boundary = _boundaries.ActiveBoundaries[7];
            var handle = boundary.Handle;
            Assert.AreEqual(BoundaryHandleShape.Diamond, handle.Shape);
            Assert.AreEqual(Color.magenta, handle.Color);
            Assert.AreEqual(0, _agents.ActiveCount);

            using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Assert.IsTrue(_client.SetDestination("127.0.0.1", ((IPEndPoint)receiver.Client.LocalEndPoint).Port));
            Assert.AreEqual(Vector3.zero, handle.transform.position);
            _router.DispatchMessage(new OscMessage("/world/boundary/handle/offset/id", 7, 125f, 500f, -250f));
            var offset = new Vector3(0.125f, 0.5f, -0.25f);
            Assert.AreEqual(offset, handle.transform.position);
            Assert.AreEqual(Vector3.zero, boundary.CenterUnity);
            Assert.AreEqual(0L, _client.TotalMessagesSent);
            var target = new Vector3(0.5f, 0.25f, 0.125f);
            handle.MoveByUser(target + offset);

            var feedback = new List<OscMessage>();
            float deadline = Time.realtimeSinceStartup + 1f;
            while (feedback.Count < 2 && Time.realtimeSinceStartup < deadline)
            {
                if (receiver.Available == 0)
                {
                    yield return null;
                    continue;
                }
                IPEndPoint sender = null;
                byte[] packet = receiver.Receive(ref sender);
                OscPacketCodec.DecodePacket(packet, 0, packet.Length, feedback);
            }
            Assert.AreEqual(2, feedback.Count);
            var legacy = feedback.Find(message => message.Address == "/boundary/position/id");
            Assert.AreEqual("/boundary/position/id", legacy.Address);
            Assert.AreEqual(125f, legacy.GetFloat(2));
            var world = feedback.Find(message => message.Address == "/world/boundary/position/id");
            Assert.AreEqual("/world/boundary/position/id", world.Address);
            Assert.AreEqual(4, world.ArgCount);
            Assert.AreEqual(500f, world.GetFloat(1));
            Assert.AreEqual(250f, world.GetFloat(2));
            Assert.AreEqual(125f, world.GetFloat(3));
            Assert.AreEqual(target + offset, handle.transform.position);
            Assert.AreEqual(7, world.GetInt(0));
            Assert.AreEqual(target, boundary.CenterUnity);
            _router.DispatchMessage(new OscMessage(world.Address, 7, 250f, 125f, 500f));
            Assert.AreEqual(new Vector3(0.25f, 0.125f, 0.5f), boundary.CenterUnity);
            Assert.AreEqual(boundary.CenterUnity + offset, handle.transform.position);
            yield return null;
            Assert.AreEqual(2L, _client.TotalMessagesSent, "Applying an OSC command must not echo as user motion.");
            _router.DispatchMessage(new OscMessage("/boundary/handle/remove/id", 7));
            Assert.IsNull(boundary.Handle);
            Assert.IsFalse(handle.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator ExplicitSizeChange_OnlyChangesGeometryAndPreservesPhysicalInfluenceRadius()
        {
            _router.DispatchMessage(new OscMessage("/world/agents/instantiate", 0, 2, 1, 100f, "127.0.0.1", 6099));
            _router.DispatchMessage(new OscMessage("/world/agents/position/id", 0, 1000f, 2000f, 3000f));
            _router.DispatchMessage(new OscMessage("/world/agents/velocity/id", 0, 1000f, 0f, 0f));
            _router.DispatchMessage(new OscMessage("/world/agents/radius/id", 0, 500f, 0.2f, "ffffff"));
            Assert.IsTrue(_agents.TryGetAgent(0, out AgentEntity agent));
            Vector3 position = agent.transform.position;
            var velocityField = typeof(AgentEntity).GetField("_velocityVector", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Vector3 velocity = (Vector3)velocityField.GetValue(agent);
            Transform influence = agent.transform.Find("RadiusVisualizer");
            Assert.AreEqual(1f, influence.lossyScale.x, 0.0001f);
            _router.DispatchMessage(new OscMessage("/world/agents/size/id", 0, 200f));
            Assert.AreEqual(position, agent.transform.position);
            Assert.AreEqual(velocity, (Vector3)velocityField.GetValue(agent));
            Assert.AreEqual(0.2f, agent.BaseScaleMeters, 0.0001f);
            Assert.AreEqual(0.2f, agent.transform.localScale.x, 0.0001f);
            Assert.AreEqual(1f, influence.lossyScale.x, 0.0001f, "Object resizing must not resize its independent half-meter influence field.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator RemovedGlobalTransforms_ReturnErrorsAndLeaveExistingGeometryUnchanged()
        {
            _router.DispatchMessage(new OscMessage("/agents/instantiate", 1));
            _router.DispatchMessage(new OscMessage("/agents/position/id", 0, 1000f, 2000f, 3000f));
            Assert.IsTrue(_agents.TryGetAgent(0, out AgentEntity agent));
            var position = agent.transform.position;
            float size = agent.BaseScaleMeters;
            int errors = 0;
            _bus.OnOscTrafficLogged += (direction, address, payload) => { if (direction == "OUT" && address == "/error") errors++; };
            const string message = "Global scale/origin transforms were removed. Use actual world dimensions and /calibration/pose for external alignment.";
            LogAssert.Expect(LogType.Warning, "[OscProtocolRouter] /xr/scale: " + message);
            _router.DispatchMessage(new OscMessage("/xr/scale", 0.2f));
            LogAssert.Expect(LogType.Warning, "[OscProtocolRouter] /xr/origin: " + message);
            _router.DispatchMessage(new OscMessage("/xr/origin", 2f, 1f, -3f));
            Assert.AreEqual(position, agent.transform.position);
            Assert.AreEqual(size, agent.BaseScaleMeters);
            Assert.AreEqual(2, errors);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PoseFeedback_UsesWorldPositionAndQuaternionInTheSameBasis()
        {
            var output = new Dictionary<string, string>();
            _bus.OnOscTrafficLogged += (direction, address, args) => { if (direction == "OUT") output[address] = args; };
            Vector3 position = new Vector3(1f, 2f, 3f);
            Quaternion rotation = Quaternion.Euler(20f, 30f, 40f);
            _bus.EmitUserHeadPoseUnity(position, rotation);
            _bus.EmitUserHandPoseUnity(0, position, true);
            _bus.EmitAgentMovedByUserUnity(12, position);
            StringAssert.StartsWith("1000,2000,3000,", output["/xr/user/pose"]);
            StringAssert.StartsWith("0,1000,2000,3000,", output["/xr/hands/pose"]);
            StringAssert.StartsWith("12,1000,2000,3000", output["/world/agents/position/id"]);
            StringAssert.StartsWith("12,1000,3000,2000", output["/agents/position/id"]);
            string[] values = output["/xr/user/pose"].Split(',');
            var recorded = new Quaternion(float.Parse(values[3], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(values[4], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(values[5], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(values[6], System.Globalization.CultureInfo.InvariantCulture)).normalized;
            Assert.Less(Quaternion.Angle(rotation, recorded), 0.01f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CalibrationCommands_UseWorldUnitsWithoutRepositioningAgents()
        {
            _router.DispatchMessage(new OscMessage("/agents/instantiate", 1));
            _router.DispatchMessage(new OscMessage("/world/agents/position/id", 0, 1000f, 2000f, 3000f));
            Assert.IsTrue(_agents.TryGetAgent(0, out AgentEntity agent));
            Vector3 requestedPosition = Vector3.zero;
            Quaternion requestedRotation = Quaternion.identity;
            _bus.OnSetCalibrationPose += (position, rotation) => { requestedPosition = position; requestedRotation = rotation; };
            Quaternion expectedRotation = Quaternion.Euler(0f, 90f, 0f);
            _router.DispatchMessage(new OscMessage("/calibration/pose", 4000f, 5000f, 6000f,
                expectedRotation.x * 2f, expectedRotation.y * 2f, expectedRotation.z * 2f, expectedRotation.w * 2f));
            Assert.Less(Vector3.Distance(new Vector3(4f, 5f, 6f), requestedPosition), 0.0001f);
            Assert.Less(Quaternion.Angle(expectedRotation, requestedRotation), 0.001f);
            Assert.Less(Vector3.Distance(new Vector3(1f, 2f, 3f), agent.transform.position), 0.0001f);
            yield return null;
        }
    }
}
