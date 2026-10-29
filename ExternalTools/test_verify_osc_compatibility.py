"""Lightweight packet/feedback checks; no Unity installation is needed."""

import socket
import unittest
from unittest.mock import Mock, patch

from pythonosc.osc_message_builder import OscMessageBuilder
from pythonosc.osc_packet import OscPacket

import verify_osc_compatibility as verifier


class OscVerifierTests(unittest.TestCase):
    def assert_cleanup_sent(self, client):
        packet = client.send.call_args.args[0]
        messages = [item.message for item in OscPacket(packet.dgram).messages]
        self.assertEqual([message.address for message in messages], ["/agents/remove/id", "/boundaries/remove"])
        self.assertEqual(messages[0].params, list(range(18)))
        self.assertEqual(messages[1].params, [1])

    def test_success_removes_only_demonstration_agents_and_boundary(self):
        client = Mock(wraps=verifier.DryRunClient())
        verifier.send_sequence(client, "127.0.0.1", 6012, 0, dry_run=True)
        self.assert_cleanup_sent(client)
        # The demonstration bundle precedes cleanup; removal uses IDs rather than a global reset.
        self.assertEqual(client.send.call_count, 2)

    def test_partial_setup_failure_and_interrupt_still_cleanup(self):
        for error in (RuntimeError("setup failed"), KeyboardInterrupt()):
            with self.subTest(error=type(error).__name__):
                client = Mock(wraps=verifier.DryRunClient())
                with patch.object(verifier, "configure_and_stream", side_effect=error):
                    with self.assertRaises(type(error)):
                        verifier.send_sequence(client, "127.0.0.1", 6012, 0, dry_run=True)
                client.send_message.assert_any_call("/world/agents/instantiate", [0, 0, 16, 50.0, "127.0.0.1", 6012])
                self.assert_cleanup_sent(client)

    def test_keep_scene_preserves_demonstration_objects(self):
        client = Mock(wraps=verifier.DryRunClient())
        verifier.send_sequence(client, "127.0.0.1", 6012, 0, dry_run=True, keep_scene=True)
        messages = [item.message for item in OscPacket(client.send.call_args.args[0].dgram).messages]
        self.assertFalse(any("remove" in message.address for message in messages))
        self.assertEqual(client.send.call_count, 1)

    def test_failure_before_boundary_creation_does_not_remove_existing_boundary(self):
        client = Mock(wraps=verifier.DryRunClient())

        def fail_add(address, values):
            if address == "/agents/add/id":
                raise RuntimeError("cube setup failed")

        client.send_message.side_effect = fail_add
        with self.assertRaises(RuntimeError):
            verifier.send_sequence(client, "127.0.0.1", 6012, 0, dry_run=True)
        messages = [item.message for item in OscPacket(client.send.call_args.args[0].dgram).messages]
        self.assertEqual([message.address for message in messages], ["/agents/remove/id"])

    def test_failed_handshake_does_not_send_cleanup_to_existing_scene(self):
        client = Mock()
        with patch.object(verifier.socket, "socket") as socket_factory, \
                patch.object(verifier.udp_client, "SimpleUDPClient", return_value=client), \
                patch.object(verifier, "wait_for_handshake", side_effect=TimeoutError()):
            receiver = socket_factory.return_value.__enter__.return_value
            receiver.getsockname.return_value = ("0.0.0.0", 6012)
            with self.assertRaises(TimeoutError):
                verifier.run_verification("127.0.0.1", 6011, 6012, 0, reply_ip="127.0.0.1")
        client.send_message.assert_called_once_with("/connect", ["127.0.0.1", 6012])
        client.send.assert_not_called()

    def test_dry_run_opens_no_network_socket(self):
        with patch.object(verifier.socket, "socket", side_effect=AssertionError("Network used during dry run")):
            verifier.run_verification("127.0.0.1", 6011, 6012, 0, dry_run=True)

    def test_demo_uses_explicit_physical_dimensions_and_unity_axes(self):
        messages = []

        class Collector(verifier.DryRunClient):
            def send(self, packet):
                messages.extend(item.message for item in OscPacket(packet.dgram).messages)
                super().send(packet)

        verifier.send_sequence(Collector(), "127.0.0.1", 6012, 0, dry_run=True)
        addresses = [message.address for message in messages]
        self.assertNotIn("/xr/scale", addresses)
        self.assertNotIn("/xr/origin", addresses)
        sphere_position = next(message for message in messages
                               if message.address == "/world/agents/position/id" and message.params[0] == 0)
        self.assertEqual(sphere_position.params, [0, 360.0, 1200.0, 800.0])
        boundary = next(message for message in messages if message.address == "/world/boundaries/add")
        self.assertEqual(boundary.params, [1, 0, 0.0, 1200.0, 800.0, 600.0])

    def test_missing_runtime_fails_instead_of_reporting_success(self):
        receiver = Mock()
        receiver.recvfrom.side_effect = socket.timeout()
        with self.assertRaises(TimeoutError):
            verifier.wait_for_handshake(receiver, "127.0.0.1", 6012, 0.02)

    def test_matching_handshake_is_accepted(self):
        builder = OscMessageBuilder(address="/test/alive/")
        for value in ("127.0.0.1", 6012, "OK"):
            builder.add_arg(value)
        receiver = Mock()
        receiver.recvfrom.return_value = (builder.build().dgram, ("127.0.0.1", 6011))
        verifier.wait_for_handshake(receiver, "127.0.0.1", 6012, 0.5)

    def test_acknowledgment_for_a_different_port_is_rejected(self):
        builder = OscMessageBuilder(address="/test/alive/")
        for value in ("127.0.0.1", 9999, "OK"):
            builder.add_arg(value)
        receiver = Mock()
        receiver.recvfrom.side_effect = [(builder.build().dgram, ("127.0.0.1", 6011)), socket.timeout()]
        with self.assertRaises(TimeoutError):
            verifier.wait_for_handshake(receiver, "127.0.0.1", 6012, 0.5)


if __name__ == "__main__":
    unittest.main()
