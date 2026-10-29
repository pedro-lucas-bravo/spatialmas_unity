using System.Collections;
using NUnit.Framework;
using SpatialMAS.Agents;
using SpatialMAS.Core;
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
}
