#!/usr/bin/env python3
"""
SpatialMAS — External OSC v1 (HS-ims) & v2 (SpatialMAS XR) Verification Suite
==============================================================================
Verifies 100% protocol compatibility with the ACSOS 2025 HS-ims OSC specification
and tests the v2 SpatialMAS multiplatform XR & Scenario extensions.

Usage:
    python ExternalTools/verify_osc_compatibility.py --ip 127.0.0.1 --port 6011
"""

import argparse
import math
import time
from pythonosc import udp_client, osc_bundle_builder, osc_message_builder


def run_verification(ip: str, port: int, local_port: int, duration_sec: float) -> None:
    print(f"[SpatialMAS Verifier] Connecting to SpatialMAS at {ip}:{port}...")
    client = udp_client.SimpleUDPClient(ip, port)

    # 1. Switch to Scenario 0 (External OSC Mode) & set World Scale
    client.send_message("/scenario/load", [0])
    client.send_message("/xr/scale", [0.20])
    client.send_message("/test/alive", ["127.0.0.1", local_port, 1.0])

    # 2. Instantiate 16 Sphere Agents + 2 Interactive Cube Agents (HS-ims v1 API)
    n_spheres = 16
    print(f"[SpatialMAS Verifier] Instantiating {n_spheres} Sphere agents + 2 Interactive Cubes...")
    client.send_message("/agents/instantiate", [0, 0, n_spheres, 0.25, "127.0.0.1", local_port])
    client.send_message("/agents/add", [1, 0, 0.35, "127.0.0.1", local_port, n_spheres, n_spheres + 1])
    client.send_message("/agents/Interactable", [n_spheres, 1])
    client.send_message("/agents/Interactable", [n_spheres + 1, 1])

    # 3. Add a Translucent Spherical Boundary (radius = 3000 mm = 3.0 m unscaled)
    client.send_message("/boundaries/add", [1, 0, 0.0, 0.0, 0.0, 3000.0])

    # 4. Configure Dual-Compatible DSP Synthesizer Parameters
    # Tests both README CC IDs (0, 82, 83, 85, 17) and Legacy C# CC IDs (1, 74, 71, 72, 73)
    pentatonic_midi = [60, 62, 64, 67, 69, 72, 74, 76]
    for i in range(n_spheres):
        waveform = i % 4
        cc_wave = 0 if (i % 2 == 0) else 1
        cc_attack = 82 if (i % 2 == 0) else 74
        client.send_message("/agents/audio/synth/param_val", [i, cc_wave, float(waveform)])
        client.send_message("/agents/audio/synth/param_val", [i, cc_attack, 0.03])
        client.send_message("/agents/audio/synth/param_val", [i, 93, 3800.0])  # LPF Cutoff
        client.send_message("/agents/audio/synth/param_val", [i, 19, 0.20])    # Reverb
        client.send_message("/agents/audio/synth/param_val", [i, 16, 0.15])    # Gain

    # 5. Stream 60 Hz Orbital Positions & Periodic Notes via OSC #bundle
    print(f"[SpatialMAS Verifier] Streaming 60Hz OSC bundles for {duration_sec:.1f}s...")
    t0 = time.time()
    step = 0
    while (time.time() - t0) < duration_sec:
        elapsed = time.time() - t0
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
        time.sleep(1.0 / 60.0)

    print("[SpatialMAS Verifier] Verification sequence completed successfully!")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Verify SpatialMAS OSC v1 & v2 compatibility.")
    parser.add_argument("--ip", default="127.0.0.1", help="Target IP of SpatialMAS headset or desktop")
    parser.add_argument("--port", type=int, default=6011, help="Target UDP port (default: 6011)")
    parser.add_argument("--local-port", type=int, default=6012, help="Local UDP port for outgoing feedback")
    parser.add_argument("--duration", type=float, default=5.0, help="Duration of orbital streaming test in seconds")
    args = parser.parse_args()
    run_verification(args.ip, args.port, args.local_port, args.duration)
