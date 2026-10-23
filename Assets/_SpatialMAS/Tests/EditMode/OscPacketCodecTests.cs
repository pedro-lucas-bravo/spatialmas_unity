using System.Collections.Generic;
using NUnit.Framework;
using SpatialMAS.Networking;

namespace SpatialMAS.Tests.EditMode
{
    public class OscPacketCodecTests
    {
        [Test]
        public void EncodeAndDecodeSingleMessage_PreservesAddressAndMixedTypes()
        {
            var original = new OscMessage("/agents/instantiate", 0, 1, 24, 0.125f, "192.168.1.50", 6012);
            byte[] encoded = OscPacketCodec.EncodeMessage(original);

            Assert.IsNotNull(encoded);
            Assert.AreEqual(0, encoded.Length % 4, "OSC message byte length must be 4-byte aligned.");

            var decoded = new List<OscMessage>();
            OscPacketCodec.DecodePacket(encoded, 0, encoded.Length, decoded);

            Assert.AreEqual(1, decoded.Count);
            OscMessage msg = decoded[0];
            Assert.AreEqual("/agents/instantiate", msg.Address);
            Assert.AreEqual(6, msg.ArgCount);
            Assert.AreEqual(0, msg.GetInt(0));
            Assert.AreEqual(1, msg.GetInt(1));
            Assert.AreEqual(24, msg.GetInt(2));
            Assert.AreEqual(0.125f, msg.GetFloat(3), 0.0001f);
            Assert.AreEqual("192.168.1.50", msg.GetString(4));
            Assert.AreEqual(6012, msg.GetInt(5));
        }

        [Test]
        public void EncodeAndDecodeBundle_UnpacksMultipleMessagesInOrder()
        {
            var m1 = new OscMessage("/agents/position/id", 0, 120.5f, -340.25f, 80.0f);
            var m2 = new OscMessage("/agents/color/id", 0, 1.0f, 0.5f, 0.25f, 1.0f);
            var m3 = new OscMessage("/agents/audio/synth/param_val", 0, 82, 0.045f);

            byte[] bundleBytes = OscPacketCodec.EncodeBundle(new[] { m1, m2, m3 });
            Assert.IsNotNull(bundleBytes);
            Assert.AreEqual(0, bundleBytes.Length % 4);

            var decoded = new List<OscMessage>();
            OscPacketCodec.DecodePacket(bundleBytes, 0, bundleBytes.Length, decoded);

            Assert.AreEqual(3, decoded.Count);
            Assert.AreEqual("/agents/position/id", decoded[0].Address);
            Assert.AreEqual(120.5f, decoded[0].GetFloat(1), 0.001f);
            Assert.AreEqual(-340.25f, decoded[0].GetFloat(2), 0.001f);

            Assert.AreEqual("/agents/color/id", decoded[1].Address);
            Assert.AreEqual(0.5f, decoded[1].GetFloat(2), 0.001f);

            Assert.AreEqual("/agents/audio/synth/param_val", decoded[2].Address);
            Assert.AreEqual(82, decoded[2].GetInt(1));
            Assert.AreEqual(0.045f, decoded[2].GetFloat(2), 0.0001f);
        }
    }
}
