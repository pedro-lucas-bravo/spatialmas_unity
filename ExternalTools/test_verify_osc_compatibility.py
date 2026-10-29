"""Lightweight packet/feedback checks; no Unity installation is needed."""

import socket
import unittest
from unittest.mock import Mock, patch

from pythonosc.osc_message_builder import OscMessageBuilder

import verify_osc_compatibility as verifier


class OscVerifierTests(unittest.TestCase):
    def test_dry_run_opens_no_network_socket(self):
        with patch.object(verifier.socket, "socket", side_effect=AssertionError("Network used during dry run")):
            verifier.run_verification("127.0.0.1", 6011, 6012, 0, dry_run=True)

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
