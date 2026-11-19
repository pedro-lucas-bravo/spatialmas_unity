#!/usr/bin/env python3
"""Guide a headset tester through OSC bridge checks 2–4."""

import argparse
import socket
import threading
import time

from pythonosc import osc_bundle_builder, osc_packet, udp_client
from pythonosc.parsing.osc_types import ParseError

from verify_osc_compatibility import build_message, wait_for_handshake


CUBE = 9101
NEAR_RECEIVER = 9102
FAR_RECEIVER = 9103
BOUNDARY = 9191
ARROW = 9192
CENTER = [200.0, 1200.0, 800.0]
POSE_ADDRESSES = {"/xr/user/pose", "/xr/hands/pose"}
POSITION_ADDRESSES = {"/agents/position/id", "/world/agents/position/id"}
EVENT_ADDRESSES = {
    "/agents/control", "/agents/touch/id", "/agents/localcomm/recv",
    "/agents/localcomm/id", "/boundary/event/id", "/boundaries/Touched", "/error",
}


def print_feedback(receiver, target_ip, stop, show_poses):
    last_sample = {}
    while not stop.is_set():
        try:
            data, peer = receiver.recvfrom(65535)
        except socket.timeout:
            continue
        if peer[0] != target_ip:
            continue
        try:
            messages = osc_packet.OscPacket(data).messages
        except (osc_packet.ParseError, ParseError) as error:
            print(f"Invalid OSC packet from {peer[0]}: {error}", flush=True)
            continue
        for item in messages:
            address, values = item.message.address, item.message.params
            if address in POSE_ADDRESSES:
                if not show_poses.is_set():
                    continue
                key = (address, values[0] if address == "/xr/hands/pose" and values else None)
            elif address in POSITION_ADDRESSES and values and values[0] == CUBE:
                key = (address, CUBE)
            elif address in EVENT_ADDRESSES:
                print(f"  <- {address} {values!r}", flush=True)
                continue
            else:
                continue
            now = time.monotonic()
            if now - last_sample.get(key, float("-inf")) >= 0.25:
                print(f"  <- {address} {values!r}", flush=True)
                last_sample[key] = now


def next_step(instruction):
    input(f"\n{instruction}\nPress Enter to continue. ")


def create_cube(send):
    send("/scenario/load", [0])
    send("/agents/add/id", [1, 0, CUBE])
    send("/world/agents/size/id", [CUBE, 100.0])
    send("/world/agents/position/id", [CUBE, *CENTER])
    send("/agents/color/id", [CUBE, "#00E5FF"])


def check_world(send):
    next_step("Switch to External OSC and create a cyan test cube, boundary and arrow.")
    try:
        create_cube(send)
        send("/world/boundary/add/id", [BOUNDARY, 0, *CENTER, 250.0])
        send("/world/environment/set_arrow", [ARROW, *CENTER, 500.0, 1200.0, 800.0, 15.0])
        next_step("Look near the starting area. Expect a 10 cm cube, a 25 cm radius sphere and an arrow along +X.")
        send("/agents/position/id", [CUBE, 200.0, 800.0, 1200.0])
        next_step("The legacy XZY position maps to the same XYZ point. The cube should stay in place.")
        send("/world/agents/rotation/id", [CUBE, 0.0, 0.3826834, 0.0, 0.9238795])
        next_step("Expect a 45-degree turn about world Y.")
        send("/world/agents/size/id", [CUBE, 150.0])
        next_step("Expect a 15 cm cube with the same center. Next, move it outside the sphere.")
        send("/world/agents/position/id", [CUBE, 700.0, 1200.0, 800.0])
        next_step("Look for /boundary/event/id [9101, 9191, 1] (exit). Next, move back inside.")
        send("/world/agents/position/id", [CUBE, *CENTER])
        next_step("Look for /boundary/event/id [9101, 9191, 0] (enter). Next, send invalid commands.")
        send("/world/agents/rotation/id", [CUBE, 0.0, 0.0, 0.0, 0.0])
        send("/xr/scale", [2.0])
        send("/xr/origin", [0.0, 0.0, 0.0])
        next_step("Expect /error for the zero quaternion and the removed scale and origin commands.")
    finally:
        send("/arrow/remove", [ARROW])
        send("/boundary/remove/id", [BOUNDARY])
        send("/agents/remove/id", [CUBE])


def check_feedback(send):
    next_step("Switch to External OSC and create a grabbable cyan cube.")
    try:
        create_cube(send)
        send("/agents/Interactable", [CUBE, 1])
        next_step("Approach and withdraw from the cube, then grab, move and release it. Check touch events,\n"
                  "/agents/control press and release, and both position streams for ID 9101.\n"
                  "The streams should show the same position with Y and Z exchanged.")
        send("/world/agents/position/id", [CUBE, *CENTER])
        send("/agents/add/id", [0, 0, NEAR_RECEIVER, FAR_RECEIVER])
        send("/world/agents/position/id", [NEAR_RECEIVER, 400.0, 1200.0, 800.0])
        send("/world/agents/position/id", [FAR_RECEIVER, 800.0, 1200.0, 800.0])
        next_step("Look at the cube and two spheres. Next, send a 300 mm local communication wave.")
        send("/agents/localcomm", ["xr_check", CUBE, 300.0, 1.0, "xr-check"])
        next_step("Expect /agents/localcomm/recv and /agents/localcomm/id for receiver 9102 (200 mm).\n"
                  "Receiver 9103 (600 mm) should receive neither. Look for the wavefront if visuals are enabled.")
    finally:
        send("/agents/remove/id", [CUBE, NEAR_RECEIVER, FAR_RECEIVER])


def check_telemetry(client, show_poses):
    send = client.send_message
    next_step("For the next eight seconds, turn your head, move one hand at a time and pinch.\n"
              "Watch /xr/user/pose for XYZ mm and quaternion XYZW. Watch /xr/hands/pose for\n"
              "hand ID 0 (left) or 1 (right), XYZ mm and pinch flag 0 or 1.")
    show_poses.set()
    try:
        time.sleep(8)
    finally:
        show_poses.clear()
    next_step("Review the pose samples. Next, load Crowd Avoidance.")
    try:
        send("/scenario/load", [1])
        next_step("Confirm Crowd Avoidance loaded and observe the initial movement. Next, set speed to 1.5.")
        send("/scenario/param", ["speed", 1.5])
        next_step("Compare the crowd's speed. Next, switch to VR.")
        try:
            send("/xr/mode", [1])
            next_step("Expect the VR studio and grid. Next, switch back to MR.")
        finally:
            send("/xr/mode", [0])
        next_step("Confirm passthrough returned. In Session, confirm recording is off before starting a test recording.")
        try:
            send("/logging/state", [1])
            next_step("Confirm recording is on and move for a few seconds. Next, stop recording and clear the test crowd.")
        finally:
            send("/logging/state", [0])
    finally:
        # Explicit cleanup also supports older builds that preserved objects on this switch.
        cleanup = osc_bundle_builder.OscBundleBuilder(osc_bundle_builder.IMMEDIATELY)
        cleanup.add_content(build_message("/agents/remove", []))
        cleanup.add_content(build_message("/boundaries/remove", []))
        cleanup.add_content(build_message("/scenario/load", [0]))
        client.send(cleanup.build())
    next_step("Confirm External OSC is active, the crowd and boundary are gone, and recording is off.\n"
              "Verify the saved test session at the download address in Session.")


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
        print(f"Handshake acknowledged by {target_ip}. Listening on UDP {reply_port}.")
        print("Use a disposable session. Checks switch scenarios and step 4 creates a recording.")
        receiver.settimeout(0.2)
        stop, show_poses = threading.Event(), threading.Event()
        listener = threading.Thread(target=print_feedback, args=(receiver, target_ip, stop, show_poses))
        listener.start()
        try:
            while True:
                choice = input("\n2. World units and visuals\n3. Grab and local communication\n"
                               "4. XR telemetry and remote controls\nQ. Quit\nSelect: ").strip().lower()
                if choice == "2":
                    check_world(send)
                elif choice == "3":
                    check_feedback(send)
                elif choice == "4":
                    check_telemetry(client, show_poses)
                elif choice == "q":
                    return
                else:
                    print("Choose 2, 3, 4 or Q.")
        finally:
            stop.set()
            listener.join()


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
        parser.exit(1, f"OSC check failed: {error}\n")
