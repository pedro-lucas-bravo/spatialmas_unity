# SpatialMAS — Researcher 10-Minute Quickstart Guide (`USER_GUIDE_RESEARCHERS.md`)

**SpatialMAS** provides ready-made multi-agent scenarios on desktop and an OpenXR implementation for headset testing, plus Python, Max/MSP, or C++ control over Wi-Fi. Validate your headset build before an experiment. Apple Vision Pro integration remains experimental and requires a platform provider.

---

## 1. Launching & Navigating in Under 2 Minutes

### On a Configured Meta Quest / OpenXR Headset
1. Put on the headset and launch **SpatialMAS**.
2. The application starts in **Mixed Reality (MR Passthrough) Mode** so you can see your physical laboratory room, with the simulation arena centered at chest height ($1.2\text{ m}$ high, $0.8\text{ m}$ in front of you).
3. **Physical Head Listening**: Your headset position automatically drives the 3D `AudioListener`. Walk around the arena or turn your head to hear spatialized sound synthesis from each agent.
4. **Tracked-Hand Interaction**: With optical hand tracking available, reach out and pinch an interactive cube to grab it. The generated rig also accepts tracked controller trigger/grip input. Gaze-pinch support is provider-specific.

### On Desktop (Windows / macOS / Linux — Game-Like Controls)
1. Press **Play** in Unity or launch the standalone executable.
2. **Walk / Fly Around Arena**: Press **`W` `A` `S` `D`** (or **Arrow Keys**) and **`E` / `Q`** (Up/Down) at any time—**no need to hold a mouse button to walk**! Hold **`Left Shift`** to sprint or **`Left Ctrl`** for slow precision movement.
3. **Camera Look, Pan & Zoom**:
   - Hold **Right Mouse Button (`RMB`) + Mouse Move** to look around, OR press **`F`** to toggle **First-Person Game Look Mode** (locks cursor for effortless mouse-look; press **`F`** or **`Esc`** to unlock).
   - Hold **Middle Mouse Button (`MMB`) + Mouse Move** to pan laterally/vertically, and use the **Scroll Wheel** to zoom in/out.
   - Press **`C`** at any time to center and reset the camera.
4. **Grab & Click Agents**:
   - **Hover** over any agent to inspect its ID, shape, and 3D coordinates at the bottom of the screen.
   - **Left-Click and Drag** any interactive Cube agent (or hold **`Alt + Left-Click`** to force-drag any autonomous agent). Use the **Scroll Wheel** while dragging to push/pull the cube deeper or closer, and press **`R`** while holding a cube to snap-rotate it by $90^\circ$.

---

## 2. Switching Scenarios, MR/VR Mode & Arena Scale

Use either the on-screen **Researcher Control Panel** or quick keyboard shortcuts:

On a configured headset, point at the world-space panel and pinch/press its buttons. It includes scenario/preset selection, pause/reset, MR/VR, recording, IDs, scale, and arena placement controls; keyboard shortcuts below are for desktop.

- **Keys `0` – `5` (or Scenario Buttons)**:
  - **`0`**: *External OSC Mode* (Waits for Python/Max/C++ commands on UDP port `6011`)
  - **`1`**: *Crowd Navigation & Human Avoidance* (Walk through the crowd to observe agents dispersing around your body!)
  - **`2`**: *3D Swarmalators* (Coupled swarming & color/musical phase synchronization, with 5 instant preset buttons)
  - **`3`**: *Orbital Attractors & Repellers* (Grab the Cyan Attractor cubes and Red Repeller cubes to reshape agent orbits)
  - **`4`**: *Musical Encounters* (Move the Golden cubes inside the sphere to trigger musical encounters)
  - **`5`**: *Self-Assembly* (Watch 6-face musical cubes assemble autonomously, or grab and snap them together yourself!)
- **Key `Space`**: Pause / Resume the active scenario.
- **Key `R`**: Reset and re-seed the active scenario.
- **Key `Tab` or `M` (or `[MR / VR]` button)**:
  - Switches instantly between **MR Passthrough** and **VR Immersive Studio** (dark studio backdrop with a holographic reference floor grid).
- **Key `I` (or `[Show IDs]` button)**: Toggle floating 3D Agent ID labels above every agent.
- **Key `H` (or `[Hide]` button)**: Show / Hide the Researcher Control Panel.
- **World Scale Slider (`0.05x – 1.50x`)**:
  - Rescales the entire simulation from a compact **Tabletop scale (`0.15x – 0.25x`)** to full **Room-Scale (`1.0x`)**.

---

## 3. Downloading CSV Telemetry Logs Over Wi-Fi (No USB Cable Needed!)

SpatialMAS automatically records two synchronized, timestamped CSV files during your experiment:
1. **`osc_traffic_<timestamp>_<session-id>.csv`**: Incoming and outgoing OSC messages (`TimestampISO, ElapsedMs, Direction, OscAddress, Arguments`).
2. **`xr_embodiment_<timestamp>_<session-id>.csv`**: Targets **50 Hz** (limited by frame rate), capturing **head position/rotation**, **hand positions**, **pinch state**, **scenario**, **world scale**, **agent count**, and **frame time**. Hand rotations are not included.

Both files share a unique session ID. Restarting recording creates a new pair without overwriting the previous session.

### How to Download Logs to Your Laptop:
1. Look at the **SpatialMAS Researcher HUD** inside the app to see the Wi-Fi Log URL (for example: `http://192.168.1.42:8080/`).
2. Open that URL in any web browser on a laptop connected to the same Wi-Fi network.
3. Click **`[Download CSV]`** next to any session file to save it directly to your laptop!
