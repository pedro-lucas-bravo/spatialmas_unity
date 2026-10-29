#!/usr/bin/env python3
"""
SpatialMAS — External OSC v1 (HS-ims) & v2 (SpatialMAS XR) Verification Suite
==============================================================================
Checks a live OSC handshake and sends representative v1/v2 traffic.
Use --dry-run for packet validation without a Unity instance or network traffic.
This smoke check does not prove full protocol or headset compatibility.

Usage:
    python ExternalTools/verify_osc_compatibility.py --ip 127.0.0.1 --port 6011
"""

import argparse
import math
import socket
import time
from pythonosc import udp_client, osc_bundle_builder, osc_message_builder, osc_packet


class DryRunClient:
    """Build and decode each packet, without opening a socket."""

    def __init__(self):
        self.packet_count = 0
        self.message_count = 0

    def send_message(self, address, values):
        builder = osc_message_builder.OscMessageBuilder(address=address)
        for value in values:
            builder.add_arg(value)
        self.send(builder.build())

    def send(self, packet):
        messages = osc_packet.OscPacket(packet.dgram).messages
        if not messages:
            raise ValueError("OSC packet contains no messages")
        self.packet_count += 1
        self.message_count += len(messages)


def wait_for_handshake(receiver, reply_ip, reply_port, timeout):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        receiver.settimeout(max(0.001, deadline - time.monotonic()))
        try:
            data, _ = receiver.recvfrom(65535)
        except socket.timeout:
            break
        for timed_message in osc_packet.OscPacket(data).messages:
            message = timed_message.message
            if message.address == "/test/alive/" and message.params == [reply_ip, reply_port, "OK"]:
                return
    raise TimeoutError("No matching Unity /test/alive/ reply; check Play mode, IP, ports and firewall.")


def run_verification(ip, port, local_port, duration_sec, *, dry_run=False, reply_ip=None, timeout=3.0):
    if dry_run:
        client = DryRunClient()
        send_sequence(client, "127.0.0.1", local_port, duration_sec, dry_run=True)
        print(f"[SpatialMAS Verifier] Dry run validated {client.packet_count} packets / {client.message_count} messages. No runtime was tested.")
        return

    print(f"[SpatialMAS Verifier] Connecting to SpatialMAS at {ip}:{port}...")
    if reply_ip is None:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as route:
            route.connect((ip, port))
            reply_ip = route.getsockname()[0]
    client = udp_client.SimpleUDPClient(ip, port)
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as receiver:
        receiver.bind(("0.0.0.0", local_port))
        reply_port = receiver.getsockname()[1]
        client.send_message("/connect", [reply_ip, reply_port])
        wait_for_handshake(receiver, reply_ip, reply_port, timeout)
        print("[SpatialMAS Verifier] Unity handshake acknowledged.")
        send_sequence(client, reply_ip, reply_port, duration_sec)
    print("[SpatialMAS Verifier] Handshake passed; traffic sequence sent. Inspect Unity for visual/audio behavior.")


def send_sequence(client, reply_ip, local_port, duration_sec, *, dry_run=False):

    # 1. Switch to Scenario 0 (External OSC Mode), set World Scale, and test HS-ims /connect + /test/alive
    client.send_message("/scenario/load", [0])
    client.send_message("/xr/scale", [0.20])
    client.send_message("/connect", [reply_ip, local_port])
    client.send_message("/test/alive", [reply_ip, local_port, 1.0])

    # 2. Instantiate 16 Sphere Agents + 2 Interactive Cube Agents (Both HS-ims v1 & v2 signatures)
    n_spheres = 16
    print(f"[SpatialMAS Verifier] Instantiating {n_spheres} Sphere agents + 2 Interactive Cubes...")
    client.send_message("/agents/instantiate", [0, 0, n_spheres, 0.25, reply_ip, local_port])
    client.send_message("/agents/add/id", [1, 0, n_spheres, n_spheres + 1])
    client.send_message("/agents/Interactable", [n_spheres, 1])
    client.send_message("/agents/Interactable", [n_spheres + 1, 1])
    client.send_message("/agents/color/id", [n_spheres, "#00F5D4", 1.0])
    client.send_message("/agents/color/id", [n_spheres + 1, "#FF2A85", 1.0])

    # 3. Add a Translucent Spherical Boundary (radius = 3000 mm = 3.0 m unscaled)
    client.send_message("/boundaries/add", [1, 0, 0.0, 0.0, 0.0, 3000.0])

    # 4. Configure Dual-Compatible DSP Synthesizer Parameters
    # Tests both 3-arg (agentId, ccId, val) and 4-arg HS-ims (agentId, synthIdx, ccId, val)
    pentatonic_midi = [60, 62, 64, 67, 69, 72, 74, 76]
    for i in range(n_spheres):
        waveform = i % 4
        cc_wave = 0 if (i % 2 == 0) else 1
        cc_attack = 82 if (i % 2 == 0) else 74
        client.send_message("/agents/audio/synth/param_val", [i, 0, cc_wave, float(waveform)])
        client.send_message("/agents/audio/synth/param_val", [i, cc_attack, 0.03])
        client.send_message("/agents/audio/synth/param_val", [i, 93, 3800.0])  # LPF Cutoff
        client.send_message("/agents/audio/synth/param_val", [i, 19, 0.20])    # Reverb
        client.send_message("/agents/audio/synth/param_val", [i, 16, 0.15])    # Gain

    # 5. Stream 60 Hz Orbital Positions & Periodic Notes via OSC #bundle
    if dry_run:
        print("[SpatialMAS Verifier] Validating one representative OSC bundle...")
    else:
        print(f"[SpatialMAS Verifier] Streaming 60Hz OSC bundles for {duration_sec:.1f}s...")
    t0 = time.monotonic()
    step = 0
    while (dry_run and step == 0) or (not dry_run and time.monotonic() - t0 < duration_sec):
        elapsed = 0.0 if dry_run else time.monotonic() - t0
        bundle = osc_bundle_builder.OscBundleBuilder(osc_bundle_builder.IMMEDIATELY)

        for i in range(n_spheres):
            angle = elapsed * 1.8 + (2.0 * math.pi * i / n_spheres)
            radius_mm = 1800.0
            x_mm = radius_mm * math.cos(angle)
            y_mm = radius_mm * math.sin(angle)
            z_mm = 600.0 * math.sin(elapsed * 2.2 + i * 0.5)

            pos_msg = osc_message_builder.OscMessageBuilder(address="/agents/position/id")
            pos_msg.add_arg(i)
            pos_msg.add_arg(float(x_mm))
            pos_msg.add_arg(float(y_mm))
            pos_msg.add_arg(float(z_mm))
            bundle.add_content(pos_msg.build())

            hue = (i / float(n_spheres) + elapsed * 0.15) % 1.0
            col_msg = osc_message_builder.OscMessageBuilder(address="/agents/color/id")
            col_msg.add_arg(i)
            col_msg.add_arg(float(0.5 + 0.5 * math.sin(2.0 * math.pi * hue)))
            col_msg.add_arg(float(0.5 + 0.5 * math.sin(2.0 * math.pi * (hue + 0.33))))
            col_msg.add_arg(float(0.5 + 0.5 * math.sin(2.0 * math.pi * (hue + 0.66))))
            col_msg.add_arg(1.0)
            bundle.add_content(col_msg.build())

        client.send(bundle.build())

        if step % 30 == 0:
            active_agent = (step // 30) % n_spheres
            midi_note = pentatonic_midi[active_agent % len(pentatonic_midi)]
            client.send_message("/agents/audio/synth/note", [active_agent, 1, midi_note, 100])
            client.send_message("/agents/localcomm", ["ping", active_agent, 1200.0, 0.35, elapsed])

        step += 1
        if not dry_run:
            time.sleep(1.0 / 60.0)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="SpatialMAS OSC handshake and traffic smoke check.")
    parser.add_argument("--ip", default="127.0.0.1", help="Target IP of SpatialMAS headset or desktop")
    parser.add_argument("--port", type=int, default=6011, help="Target UDP port (default: 6011)")
    parser.add_argument("--local-port", type=int, default=6012, help="Local UDP port for outgoing feedback")
    parser.add_argument("--duration", type=float, default=5.0, help="Duration of orbital streaming test in seconds")
    parser.add_argument("--dry-run", action="store_true", help="Validate packet encoding without network access")
    parser.add_argument("--reply-ip", help="This workstation's IP reachable by Unity (normally detected automatically)")
    parser.add_argument("--timeout", type=float, default=3.0, help="Seconds to wait for the Unity handshake")
    args = parser.parse_args()
    if args.duration < 0 or args.timeout <= 0:
        parser.error("duration must be nonnegative and timeout must be positive")
    try:
        run_verification(args.ip, args.port, args.local_port, args.duration,
                         dry_run=args.dry_run, reply_ip=args.reply_ip, timeout=args.timeout)
    except (OSError, TimeoutError) as error:
        parser.exit(1, f"[SpatialMAS Verifier] FAILED: {error}\n")
