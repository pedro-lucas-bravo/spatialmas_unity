#!/usr/bin/env python3
"""Guide headset checks for spatial audio, listener pose and OSC sound controls."""

import argparse
import math
import socket
import time

from pythonosc import osc_bundle_builder, osc_packet, udp_client

from check_osc_interactive import next_step
from verify_osc_compatibility import build_message, wait_for_handshake


LEFT = 9201
RIGHT = 9202


def receive_head_pose(receiver, target_ip):
    # Discard queued observations from before the tester chose their starting pose.
    receiver.setblocking(False)
    for _ in range(4096):
        try:
            receiver.recvfrom(65535)
        except BlockingIOError:
            break
    deadline = time.monotonic() + 3
    while (remaining := deadline - time.monotonic()) > 0:
        receiver.settimeout(remaining)
        try:
            data, peer = receiver.recvfrom(65535)
        except socket.timeout:
            break
        if peer[0] != target_ip:
            continue
        for item in osc_packet.OscPacket(data).messages:
            message = item.message
            if message.address == "/xr/user/pose" and len(message.params) == 7:
                position = message.params[:3]
                x, y, z, w = message.params[3:]
                yaw = math.atan2(2 * (w * y + x * z), 1 - 2 * (x * x + y * y))
                return position, yaw
    raise TimeoutError("No head pose from SpatialMAS. Keep the app visible in the awake headset and try again.")


def emitter_position(head, yaw, side_mm, forward_mm):
    x, y, z = head
    return [
        x + side_mm * math.cos(yaw) + forward_mm * math.sin(yaw),
        y - 150.0,
        z - side_mm * math.sin(yaw) + forward_mm * math.cos(yaw),
    ]


def create_sources(send, head, yaw):
    send("/scenario/load", [0])
    send("/xr/listener/lock_to_head", [1])
    send("/agents/add/id", [0, 0, LEFT, RIGHT])
    for agent, side, color in ((LEFT, -750.0, "#26C6DA"), (RIGHT, 750.0, "#FFB347")):
        send("/world/agents/position/id", [agent, *emitter_position(head, yaw, side, 1000.0)])
        send("/world/agents/size/id", [agent, 120.0])
        send("/agents/color/id", [agent, color])
        send("/agents/audio/mute", [agent, 0])
        send("/agents/audio/synth/param_val", [agent, 1, 0.0])  # Sine
        send("/agents/audio/synth/param_val", [agent, 19, 0.0])  # Dry
        send("/agents/audio/synth/param_val", [agent, 85, 0.8])  # Sustain
        send("/agents/audio/synth/param_val", [agent, 17, 0.12])  # Release seconds
        send("/agents/audio/synth/param_val", [agent, 16, 0.22])


def play_notes(send):
    send("/agents/audio/synth/note", [LEFT, 1, 60, 100])
    send("/agents/audio/synth/note", [RIGHT, 1, 76, 100])


def check_spatialization(send):
    play_notes(send)
    next_step("Cyan on your starting left plays the low note. Amber on the right plays the high note.\n"
              "Turn your head with your feet still and listen for the stereo balance to change.\n"
              "Within clear space, approach and retreat from each source. Listen for level changes "
              "and check whether the sounds stay at the visible sources.")


def check_listener(send, head, yaw):
    play_notes(send)
    next_step("Face the two spheres from your starting position. Listen to the head-locked baseline.")
    send("/xr/listener/lock_to_head", [0])
    send("/world/listener/position", head)
    send("/world/listener/rotation", [0.0, math.sin(yaw / 2), 0.0, math.cos(yaw / 2)])
    next_step("Turn your head and return to center. With the listener detached, the mix should stay fixed.\n"
              "For the next changes, keep your head still.")
    send("/world/listener/position", emitter_position(head, yaw, -650.0, 800.0))
    next_step("The virtual listener is now closer to cyan. Check whether the low note gets louder.")
    send("/world/listener/position", head)
    flipped_yaw = yaw + math.pi
    send("/world/listener/rotation", [0.0, math.sin(flipped_yaw / 2), 0.0, math.cos(flipped_yaw / 2)])
    next_step("The listener has turned 180 degrees. Check whether left/right balance swaps without moving your head.")
    send("/xr/listener/lock_to_head", [1])
    next_step("Turn your head again. The mix should now follow you. Listen for silence, duplicate sounds or a stale pose.")


def check_controls(send):
    send("/agents/audio/synth/note", [LEFT, 1, 60, 100])
    next_step("The cyan sphere should play a sustained MIDI note.")
    send("/agents/audio/synth/note", [LEFT, 0, 60, 0])
    next_step("The note should fade out. Next, start two continuous tones using frequency and gain.")
    for agent, frequency in ((LEFT, 262.0), (RIGHT, 659.0)):
        send("/agents/audio/synth/param_val", [agent, 2, frequency])
        send("/agents/audio/synth/param_val", [agent, 16, 0.22])
    next_step("Expect a low tone from cyan and a high tone from amber.")
    send("/agents/audio/mute", [LEFT, 1])
    next_step("Only amber should be audible.")
    send("/agents/audio/mute", [LEFT, 0])
    next_step("Cyan should return.")
    send("/agents/audio/mute", [1])
    next_step("Both sources should go silent.")
    send("/agents/audio/mute", [0])
    next_step("Both sources should return. Next, switch from synth to the sound bank.")
    for agent in (LEFT, RIGHT):
        send("/agents/audio/synth/param_val", [agent, 16, 0.0])
    for agent, clip, name in ((LEFT, 0, "Bell on cyan"), (RIGHT, 1, "Click on amber")):
        next_step(f"Listen for {name}. The script plays it three times.")
        for _ in range(3):
            send("/agents/audio/bank", [agent, clip, 1, 0.35])
            time.sleep(0.7)
    next_step("Confirm Bell and Click were audible. Next, restart the tones, remove both sources and listen for stuck audio.")
    play_notes(send)
    time.sleep(2)


def check_encounters(client):
    try:
        client.send_message("/scenario/load", [4])
        next_step("Watch Musical Encounters. Look for gold flashes when agents come close and listen for notes at the same time.\n"
                  "If needed, move a golden cube near an agent to trigger an encounter. "
                  "Next, clear this demo and return to External OSC.")
    finally:
        cleanup = osc_bundle_builder.OscBundleBuilder(osc_bundle_builder.IMMEDIATELY)
        cleanup.add_content(build_message("/agents/remove", []))
        cleanup.add_content(build_message("/boundaries/remove", []))
        cleanup.add_content(build_message("/scenario/load", [0]))
        client.send(cleanup.build())
    next_step("Confirm the built-in scene is empty and listen for any notes or sounds that continue.")


def run(args):
    target_ip = socket.gethostbyname(args.ip)
    reply_ip = args.reply_ip
    if reply_ip is None:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as route:
            route.connect((target_ip, args.port))
            reply_ip = route.getsockname()[0]
    client = udp_client.SimpleUDPClient(target_ip, args.port)
    send = client.send_message
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as receiver:
        receiver.bind(("0.0.0.0", args.local_port))
        reply_port = receiver.getsockname()[1]
        wait_for_handshake(receiver, reply_ip, reply_port, args.timeout,
                           request=lambda: send("/connect", [reply_ip, reply_port]), target_ip=target_ip)
        print(f"Handshake acknowledged by {target_ip}. Use a disposable scene with headset audio enabled.")
        while True:
            choice = input("\n1. Left/right panning and distance rolloff\n2. Listener detach and relock\n"
                           "3. Synth, mute and Bell/Click controls\n4. Built-in audio and visual cues\n"
                           "Q. Quit\nSelect: ").strip().lower()
            if choice == "q":
                return
            if choice == "4":
                next_step("Load Musical Encounters. This replaces the current scene.")
                check_encounters(client)
                continue
            if choice not in ("1", "2", "3"):
                print("Choose 1, 2, 3, 4 or Q.")
                continue
            next_step("Stand in a clear area and face straight ahead. The test will place two spheres in front of you.")
            head, yaw = receive_head_pose(receiver, target_ip)
            try:
                create_sources(send, head, yaw)
                if choice == "1":
                    check_spatialization(send)
                elif choice == "2":
                    check_listener(send, head, yaw)
                else:
                    check_controls(send)
            finally:
                send("/agents/remove/id", [LEFT, RIGHT])
                send("/xr/listener/lock_to_head", [1])
            next_step("The test spheres have been removed and the listener is head-locked. Listen for any lingering sound.\n"
                      "Record your result for this run, including headset audio output and any localization limits.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ip", default="127.0.0.1", help="SpatialMAS PC or headset IPv4 address")
    parser.add_argument("--port", type=int, default=6011, help="SpatialMAS OSC input port")
    parser.add_argument("--reply-ip", help="PC IPv4 address reachable by SpatialMAS")
    parser.add_argument("--local-port", type=int, default=6012, help="PC UDP port for replies")
    parser.add_argument("--timeout", type=float, default=8.0, help="Handshake timeout in seconds")
    args = parser.parse_args()
    if args.timeout <= 0:
        parser.error("--timeout must be positive")
    try:
        run(args)
    except (EOFError, KeyboardInterrupt):
        print("\nStopped.")
    except OSError as error:
        parser.exit(1, f"Spatial audio check failed: {error}\n")
