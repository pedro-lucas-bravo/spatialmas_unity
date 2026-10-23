# SpatialMAS — Researcher 10-Minute Quickstart Guide (`USER_GUIDE_RESEARCHERS.md`)

Welcome to **SpatialMAS**! You can run multi-agent spatial audio experiments on **Meta Quest 3**, **Apple Vision Pro**, **OpenXR headsets**, or a **Desktop computer** without writing a single line of code—or connect your existing Python, Max/MSP, or C++ scripts over Wi-Fi.

---

## 1. Launching & Navigating in Under 2 Minutes

### On Meta Quest 3 / Apple Vision Pro / OpenXR Headset
1. Put on the headset and launch **SpatialMAS**.
2. The application starts in **Mixed Reality (MR Passthrough) Mode** so you can see your physical laboratory room, with the simulation arena centered at chest height ($1.2\text{ m}$ high, $0.8\text{ m}$ in front of you).
3. **Physical Head Listening**: Your headset position automatically drives the 3D `AudioListener`. Walk around the arena or turn your head to hear spatialized sound synthesis from each agent.
4. **Bare-Hand / Gaze-Pinch Interaction**: Reach out and **pinch** (Thumb + Index/Middle finger) any interactive Cube agent to grab, move, or rotate it.

### On Desktop (Windows / macOS / Linux)
1. Press **Play** in Unity or launch the standalone executable.
2. **Fly / Walk Around Arena**: Hold **Right Mouse Button (RMB)** and press **`W` `A` `S` `D` `Q` `E`** to walk and look around the 3D space.
3. **Grab Interactive Cubes**: **Left-Click and Drag** any Cube agent with your mouse. Use the **Scroll Wheel** to push/pull the cube deeper or closer, and press **`R`** while holding a cube to snap-rotate it by $90^\circ$.

---

## 2. Switching Scenarios, MR/VR Mode & Arena Scale

Use either the on-screen **Researcher Control Panel** or quick keyboard shortcuts:
- **Keys `0` – `5` (or Scenario Buttons)**:
  - **`0`**: *External OSC Mode* (Waits for Python/Max/C++ commands on UDP port `6011`)
  - **`1`**: *Crowd Navigation & Human Avoidance* (Walk through the crowd to observe agents dispersing around your body!)
  - **`2`**: *3D Swarmalators* (Coupled swarming & color/musical phase synchronization)
  - **`3`**: *Orbital Attractors & Repellers* (Grab the Cyan Attractor cubes and Red Repeller cubes to reshape agent orbits)
  - **`4`**: *Musical Encounters* (Move the Golden cubes inside the sphere to trigger musical encounters)
  - **`5`**: *Self-Assembly* (Watch 6-face musical cubes assemble autonomously, or grab and snap them together yourself!)
- **Key `M` (or `[Toggle MR / VR]` button)**:
  - Switches instantly between **MR Passthrough** (physical room visible) and **VR Immersive Studio** (dark studio backdrop with a holographic reference floor grid).
- **World Scale Slider (`0.05x – 1.50x`)**:
  - Rescales the entire simulation from a compact **Tabletop scale (`0.15x – 0.25x`)** to full **Room-Scale (`1.0x`)**.

---

## 3. Downloading CSV Telemetry Logs Over Wi-Fi (No USB Cable Needed!)

SpatialMAS automatically records two synchronized, timestamped CSV files during your experiment:
1. **`osc_traffic_YYYY-MM-DD_HH-mm-ss.csv`**: Every incoming and outgoing OSC message (`TimestampISO, ElapsedMs, Direction, OscAddress, Arguments`).
2. **`xr_embodiment_YYYY-MM-DD_HH-mm-ss.csv`**: Sampled at **50 Hz**, capturing the participant's **Head 6DoF pose**, **Left/Right Hand 6DoF poses**, **Pinch events**, **Active Scenario**, **World Scale**, **Active Agent Count**, and **Frametime (ms)**.

### How to Download Logs to Your Laptop:
1. Look at the **SpatialMAS Researcher HUD** inside the app to see the Wi-Fi Log URL (for example: `http://192.168.1.42:8080/`).
2. Open that URL in any web browser on a laptop connected to the same Wi-Fi network.
3. Click **`[Download CSV]`** next to any session file to save it directly to your laptop!
