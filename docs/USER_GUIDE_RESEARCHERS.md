# SpatialMAS researcher guide

**SpatialMAS** provides ready-made multi-agent scenarios on desktop and an OpenXR implementation for headset testing, plus Python, Max/MSP, or C++ control over Wi-Fi. Validate your headset build before an experiment. Apple Vision Pro integration remains experimental and requires a platform provider.

---

## 1. Launching & Navigating in Under 2 Minutes

### On a Configured Meta Quest / OpenXR Headset
1. Put on the headset and launch **SpatialMAS**.
2. The application starts in **Mixed Reality (MR Passthrough) Mode** so you can see your physical laboratory room, with the simulation arena centered at chest height (1.2 m high, 0.8 m in front of you).
3. **Physical Head Listening**: Your headset position automatically drives the 3D `AudioListener`. Walk around the arena or turn your head to hear spatialized sound synthesis from each agent.
4. **Tracked hands.** Reach out and pinch an interactive cube to grab it. In Swarmalators, you can also aim at a distant cube and pinch when the faint grab ray appears. The cube comes directly to your hand. Rotate your wrist to turn it and release the pinch to let go. The rig also accepts tracked controller input.

### On Desktop (Windows / macOS / Linux — Game-Like Controls)
1. Press **Play** in Unity or launch the standalone executable.
2. **Walk / Fly Around Arena**: Press **`W` `A` `S` `D`** (or **Arrow Keys**) and **`E` / `Q`** (Up/Down) at any time—**no need to hold a mouse button to walk**! Hold **`Left Shift`** to sprint or **`Left Ctrl`** for slow precision movement.
3. **Camera Look, Pan & Zoom**:
   - Desktop starts in the isometric **Orthographic** view. Hold **Right Mouse Button (`RMB`)** and move the mouse to orbit. Scroll to zoom. Choose **Perspective** in the **View** row for a first-person view. In Perspective, right drag looks around and **`F`** toggles cursor lock.
   - Hold **Middle Mouse Button (`MMB`) + Mouse Move** to pan laterally/vertically, and use the **Scroll Wheel** to zoom in/out.
   - Press **`C`** at any time to center and reset the camera.
4. **Grab & Click Agents**:
   - **Hover** over any agent to inspect its ID, shape, and 3D coordinates at the bottom of the screen.
   - In the **Drag** row, choose **Face plane** or **Camera plane** independently of the view. Left-drag a movable object. Hold **`Alt + Left-Click`** to force-drag an autonomous agent.
   - **Face plane** previews the selected face and its two movement axes. Non-cubes use a world-aligned box preview. A top face keeps height fixed. A side face keeps motion in that side's plane. The chosen plane and grab offset are fixed until release. Scroll depth, camera navigation, and object rotation are suspended during face-plane capture.
   - **Camera plane** follows the current view. Scroll while holding an object to change its depth, or press **`R`** while holding an agent to rotate it by 90°. Dragging a non-cube shows the translucent camera-facing cue until release.

---

## 2. Use the researcher panel in XR

### Open, close, and select controls

Bring your right index fingertip toward the back of your left hand. When the raised launcher appears, touch its outward face and hold for about one second until the progress bar fills. Repeat the gesture to close an open panel or recall a closed panel in front of you. You can also select **Close** in the panel header.

Point at a control and pinch to select it. With a tracked controller, aim and press the trigger. The header shows the active scenario, whether it is running or paused, and whether recording is active.

### Move the panel

1. Aim at the title or a blank part of the panel until the four-arrow marker appears.
2. Pinch and hold. Sweep sideways to move the panel around you, aim higher or lower to adjust height, or push your hand away from your head or pull it closer to change distance. A sideways sweep at a steady hand-to-head radius preserves panel distance.
3. Release to finish placing the panel. You can continue dragging if your ray leaves the panel while you hold the pinch.

In follow mode, the panel responds gradually to larger changes in head position and direction while retaining your chosen distance. Select **Pin in room** to anchor it at its current pose. Select **Follow user** to resume following. **Recenter** brings the panel in front of you at the configured default distance, normally 1.25 m.

### Find the right tab

- **Run** opens first. Choose a scenario or an available preset, pause or resume the simulation, or reset the active scenario.
- **Setup** holds the layout and spawn controls for the next scenario load or reset, along with the calibration reference, **MR / VR**, and **Agent IDs** controls. **Clear All** asks for confirmation before clearing agents.
- **Session** holds **Start recording** or **Stop recording**, session information, and the Wi-Fi download address. Select **Show Diagnostics** to inspect device, frame-rate, OSC, and agent information.

### Hand the headset to a participant

Select **Participant Mode** to hide the panel and its hand launcher. To return to researcher mode, hold **X and Y together on the left Touch controller** for about two seconds, then release both. If you are operating the Unity Editor or PC app from a keyboard, use **F8**. Recovery recalls the panel in front of you.

Simulation and recording continue in their current states when you enter Participant Mode. To end a participant recording, recover the panel and select **Stop recording** in **Session**.

## 3. Scenarios, physical layouts, and calibration reference

Use the researcher panel on the headset or the keyboard shortcuts below on desktop.

- **Keys `0` – `5` (or Scenario Buttons)**:
  - **`0`**: *External OSC Mode* (Waits for Python/Max/C++ commands on UDP port `6011`)
  - **`1`**: *Crowd Navigation & Human Avoidance* (Walk through the crowd to observe agents dispersing around your body!)
  - **`2`**: *3D Swarmalators* (Coupled swarming & color/musical phase synchronization, with 5 instant preset buttons)
  - **`3`**: *Orbital Attractors & Repellers* (Grab the Cyan Attractor cubes and Red Repeller cubes to reshape agent orbits)
  - **`4`**: *Musical Encounters*. Move either red cube near another musical agent until its translucent detection sphere turns yellow. Hold it in range to hear its note sustain, or pull it away to hear the note release. Grab the green center diamond to move the arena and its autonomous spheres together. The two red cubes stay where you placed them.
  - **`5`**: *Self-Assembly* (Watch 6-face musical cubes assemble autonomously, or grab and snap them together yourself!)
- **Key `Space`**: Pause / Resume the active scenario.
- **Key `R`**: Reset and re-seed the active scenario.
- **Key `Tab` or `M` (or `[MR / VR]` button)**:
  - Switches instantly between **MR Passthrough** and **VR Immersive Studio** (dark studio backdrop with a holographic reference floor grid).
- **Key `I` (or `[Show IDs]` button)**: Toggle floating 3D Agent ID labels above every agent.
- **Key `H` (or `[Hide]` button)**: Show / Hide the Researcher Control Panel.
- **Next Compact / Next Room Layout**: Choose a physical configuration for the next scenario load/reset. These presets specify sizes and speeds in meters and meters/second; they do not resize the current scene or participant.
- **Spawn Next Scenario Here**: Sets the next spawn center ahead of you. Load/reset to apply it.
- **Reference controls**: The reference starts visible and locked at world zero. Unlock it, use Reference Ahead or drag the marker (desktop scroll adjusts depth; R rotates 90 degrees), then lock it again. XR users can grab the unlocked marker. Hide/Show changes only its graphics. Reference movement leaves all raw world coordinates and existing objects unchanged.

---

## 4. Record and download session data

Open **Session** on the XR panel and check the recording status. If recording is already active, the panel shows **Stop recording**. Select **Start recording** to start a new session when recording is off. On desktop, press **L** to start or stop recording.

SpatialMAS records five CSV files and one metadata JSON per session:

1. `osc_traffic_`: Raw incoming/outgoing OSC messages.
2. `xr_embodiment_`: Head/hand world poses, full rotations, tracking validity, simulation/desktop source, scenario/layout, and calibration/tracking revisions.
3. `object_state_`: Sampled agents, boundaries, and arrows with actual world poses and physical dimensions.
4. `calibration_`: Initial reference pose and subsequent changes, including lock/visibility.
5. `session_events_`: Scenario, parameter/preset, calibration, and tracking-frame changes.
6. `session_metadata_*.json`: Schema version, coordinate conventions, configuration and file inventory.

Download all six files for a session. Join object and participant rows by `(SessionId, SampleId)`; their timestamps match. Positions and dimensions are Unity-world meters, rotations are world quaternions `x,y,z,w`. Sampling targets 50 Hz but is frame-limited; use actual `ElapsedMs` values. Exclude untracked-hand samples from measured-hand analysis. Changing the reference never rewrites raw world data. See the [coordinate/recording contract](ARCHITECTURE.md#5-physical-coordinate-and-calibration-contract) for reference-relative conversion and motion-capture alignment.

Each time you start recording, SpatialMAS creates a new session and keeps previous recordings.

### Download logs to your laptop

1. Stop recording to finalize the session files. Leave SpatialMAS open so it can serve the downloads.
2. Find the Wi-Fi download address on **Session** in XR or on the desktop researcher HUD. On a laptop connected to the same network, open that address in a browser.
3. Download the five CSV files and the metadata JSON for the same session.
