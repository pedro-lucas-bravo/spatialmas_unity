# SpatialMAS — Complete Setup & Usage Guide

This guide covers the **SpatialMAS research prototype** in the Unity Editor, desktop builds, and Android/PC OpenXR targets, including local automation through **Unity CLI (`unity` / `com.unity.pipeline`)**. Headset passthrough, interaction, performance, and builds must be verified on the target device. The visionOS build menu is scaffolding, not a complete Apple Vision Pro integration.

---

## 1. Prerequisites & Installed Tools

### 1.1 Unity Editor & Modules
- **Unity Version**: `6000.6.6f1` (Unity 6)
- **Required Unity Hub Modules** (install the modules matching your target hardware):
  - **Windows / Mac / Linux Dedicated Build Support** (for Desktop mode & Editor testing)
  - **Android Build Support** + **OpenJDK** + **Android SDK & NDK Tools** (for Meta Quest 3 / 3S / Pro, Pico 4 Ultra, Android XR)
  - **visionOS Build Support** (on macOS with Xcode 16+, for Apple Vision Pro)

### 1.2 UPM Packages (Auto-Resolved via `Packages/manifest.json`)
Opening the project automatically resolves all required packages:
- `com.unity.xr.management` (`4.7.0`)
- `com.unity.xr.openxr` (`1.18.0`)
- `com.unity.xr.meta-openxr` (`2.6.1`)
- `com.unity.xr.interaction.toolkit` (`3.6.1`)
- `com.unity.xr.hands` (`1.9.0`)
- `com.unity.xr.arfoundation` (`6.6.2`)
- `com.unity.pipeline` (`0.9.0-exp.1`) — Unity CLI Editor & Runtime automation server
- `com.unity.test-framework` (`1.4.6`)

### 1.3 External Companion Tools (Optional, for External OSC Testing)
- **Python 3.9+** with `python-osc`:
  ```bash
  pip install -r ExternalTools/requirements.txt
  ```

---

## 2. Project Initialization & 1-Click Scene Generation

SpatialMAS includes an automated Editor builder ([`SpatialMASEditorBuilder.cs`](../Assets/_SpatialMAS/Editor/SpatialMASEditorBuilder.cs)) that deterministically generates all required materials, procedural agent prefabs (`Agent_Sphere.prefab`, `Agent_Cube.prefab`, `Agent_Pyramid.prefab`), configuration assets, OpenXR feature settings, and the main scene (`Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity`).

You can trigger the builder either from the **Unity Editor UI** or directly from your terminal using the **Unity CLI (`unity`)**.

### Option A: From the Unity Editor Menu
1. Open the project in Unity `6000.6.6f1`.
2. In the top menu bar, click:
   - **`SpatialMAS > 1. Build Complete Project (Materials, Prefabs, OpenXR & Main Scene)`** *(or the alias **`SpatialMAS > Build Full SpatialMAS Scene & Prefabs`**)*
   - **`SpatialMAS > 2. Configure OpenXR & Multiplatform XR Settings`** *(or **`SpatialMAS > Configure Multiplatform Build Settings`**)*
3. Verify in the Project window that the following assets are populated:
   - `Assets/_SpatialMAS/SpatialMASConfig.asset` & `Assets/_SpatialMAS/Resources/DefaultSpatialMASConfig.asset`
   - `Assets/_SpatialMAS/Materials/` (and mirrored in `Assets/_SpatialMAS/Resources/`):
     - `Mat_SpatialMAS_AgentInstanced.mat`
     - `Mat_SpatialMAS_BoundaryTranslucent.mat`
     - `Mat_SpatialMAS_Wavefront.mat`
     - `Mat_SpatialMAS_Arrow.mat`
     - `Mat_SpatialMAS_VRFloor.mat`
     - `Mat_SpatialMAS_VRGridLine.mat`
   - `Assets/_SpatialMAS/Prefabs/`:
     - `Agent_Sphere.prefab`
     - `Agent_Cube.prefab`
     - `Agent_Pyramid.prefab`
   - `Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity`

### Option B: Using the Unity CLI (`unity` / `com.unity.pipeline`)
With the Unity Editor open on the project, run the following commands from PowerShell or bash:

```powershell
# 1. Check that the Unity CLI is connected to the live Editor instance
unity command editor status

# 2. Keep the Editor ticking even when unfocused in the background
unity command editor set_auto_tick --enable true

# 3. Generate the complete SpatialMAS scene, prefabs, materials, and OpenXR settings
unity command script run --file Assets/_SpatialMAS/Editor/SpatialMASEditorBuilder.cs --entry SpatialMAS.Editor.SpatialMASEditorBuilder.BuildAll

# 4. Or invoke the Editor menu item directly once compiled
unity command editor invoke_menu --path "SpatialMAS/1. Build Complete Project (Materials, Prefabs, OpenXR & Main Scene)"
```

---

## 3. Running in Desktop / Editor Mode (Game-Like 3D Workflow)

You can test scenarios, OSC control, procedural audio, desktop interaction, and CSV export in the Editor or a desktop build. Optical hand input, headset tracking, passthrough, and device performance require an XR runtime and hardware.

1. Open `Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity`.
2. Press **Play** in the Unity Editor (or run `unity command editor.playmode start` from the terminal).
3. [`XRPlatformAdapter`](../Assets/_SpatialMAS/Runtime/XR/XRPlatformAdapter.cs) automatically detects that no XR HMD is active, disables `ARSession`/`TrackedPoseDriver` so `XRSimulation` never hijacks the camera, and activates the game-like [`DesktopInteractionRig`](../Assets/_SpatialMAS/Runtime/XR/DesktopInteractionRig.cs).

### Desktop Keyboard & Mouse Controls

| Input | Action |
| :--- | :--- |
| **`W` / `A` / `S` / `D` (or Arrow Keys)** | Walk / fly smoothly in 3D space (**always active** — no need to hold Right Mouse Button!) |
| **`E` / `Q` (or `PageUp` / `PageDown`)** | Move vertically Up (`+Y`) / Down (`-Y`) |
| **`Left Shift` (Hold)** | Sprint speed (`2.4x`) |
| **`Left Ctrl` (Hold)** | Precision slow-walk (`0.35x`) for close-up inspection |
| **`Right Mouse Button` (Hold) + Mouse Move** | Rotate camera look direction (`Pitch` & `Yaw`) while keeping cursor free when released |
| **`F`** | Toggle **First-Person Game Look Mode** (locks cursor for effortless mouse-look without holding `RMB`; press **`F`** or **`Esc`** to unlock) |
| **`Middle Mouse Button` (Hold) + Mouse Move** | Pan camera laterally / vertically in the view plane (`HS-ims` style) |
| **`Mouse Scroll Wheel`** | Zoom / dolly camera forward & backward (or push/pull a grabbed agent along the view ray while dragging) |
| **`Left Mouse Button` (Drag on Cube)** | Grab and drag any interactive agent in 3D space (hold **`Alt + LMB`** to force-drag any autonomous agent) |
| **`Left / Right Click` (on any Agent)** | Emits `HS-ims` `/agents/control <id> <0=LMB,1=RMB> <1=press,0=release>` |
| **`C`** | **Center Camera** — resets camera to the default arena overview position |
| **`Space`** | **Pause / Resume** the active simulation scenario |
| **`R`** | **Reset Scenario** (re-seeds active scenario; or when dragging a cube, snap-rotates it by $90^\circ$) |
| **`Tab` or `M`** | Toggle **MR Passthrough** $\leftrightarrow$ **VR Immersive Studio** |
| **`I`** | Toggle floating 3D **Agent ID Labels** (`Show Agents IDs`) |
| **`H`** | Show / Hide the **Researcher Control Panel** HUD overlay |
| **`L`** | Start / Stop **CSV Telemetry Recording** (`osc_traffic_*.csv` & `xr_embodiment_*.csv`) |
| **`0` – `5`** | Switch active Scenario (`0`=External OSC, `1`=Crowd Avoidance, `2`=3D Swarmalators, `3`=Attractors & Repellers, `4`=Musical Encounters, `5`=Self-Assembly) |

---

## 4. Deploying to XR Headsets (Multiplatform OpenXR)

### 4.1 Meta Quest 3 / 3S / Pro (`Android` Target)
1. Enable **Developer Mode** on your Meta Quest headset via the Meta Horizon mobile app.
2. In Unity, build directly from the **`SpatialMAS > Build Target > Build Meta Quest & Android OpenXR (.apk)`** menu, or switch to the **Android** build target:
   - Via UI: `File > Build Profiles > Android > Switch Platform`
   - Via Unity CLI: `unity command build.target switch --target Android --confirm true`
3. Verify OpenXR settings (`Edit > Project Settings > XR Plug-in Management > OpenXR` under the **Android** tab):
   - **OpenXR** checked
   - **Meta Quest Support** feature group enabled
   - **Hand Tracking Subsystem** & **Meta Hand Tracking Aim** enabled
   - **AR Session / Passthrough** enabled
4. Connect your Meta Quest 3 via USB-C and build:
   - Via UI: `SpatialMAS > Build Target > Build Meta Quest & Android OpenXR (.apk)` or `File > Build And Run`
   - Via Unity CLI: `unity command build run --target Android --outputPath Builds/Android_OpenXR/SpatialMAS_Quest.apk --confirm true`
5. **In-Headset Operation**:
   - Use the world-space researcher panel to choose scenarios/presets, pause/reset, toggle MR/VR or recording, show IDs, adjust world scale, and center the arena ahead. Point with a tracked hand/controller and pinch/press to activate its controls.
   - The app launches in **MR Passthrough Mode** by default (`CameraClearFlags.SolidColor` with `RGBA(0,0,0,0)`), showing virtual swarm agents floating inside your physical laboratory.
   - Walk physically around the swarm to inspect agents and hear true 3D Doppler and distance-attenuated spatial audio.
   - **Hand Pinch Grab**: Reach toward any interactive Cube agent and pinch your **Thumb + Index/Middle Finger** (`< 20 mm` pinch distance to grab, `> 32 mm` to release). Moving your pinched hand repositions the agent in real time and broadcasts its updated pose over OSC (`/agents/position/id`).

### 4.2 Apple Vision Pro (`visionOS` Target, Experimental)
The build menu requests a visionOS Xcode project, but the repository does not include a visionOS XR provider or a validated spatial-pointer rig. Platform detection alone does not supply these features. Complete provider, input, rendering, signing, and device validation on a Mac before treating this as a supported deployment target.

### 4.3 Generic OpenXR Headsets (Pico 4 Ultra, Android XR, HTC Vive, SteamVR / PCVR)
- **Standalone Android OpenXR (Pico / Android XR)**: Build for `Android` with the **OpenXR** loader enabled. Hand tracking and passthrough depend on the device's provider and enabled features; a transparent camera background alone does not guarantee passthrough.
- **PCVR (Quest Link / AirLink / SteamVR / Windows Mixed Reality)**: Keep the build target on `StandaloneWindows64`, ensure your PC OpenXR runtime (Meta Quest Link or SteamVR) is active, and press **Play** in the Editor or launch `Builds/Windows64/SpatialMAS.exe`.

---

## 5. Connecting External Multi-Agent Simulators via OSC

SpatialMAS implements the legacy signatures listed in [the OSC specification](OSC_API_SPECIFICATION.md), with focused regression coverage, plus **v2 SpatialMAS XR extensions**. Validate your external controller's actual message sequence before collecting research data.

### 5.1 Network Setup
1. Ensure your workstation (running Python / Max / C++ / ROS 2) and your XR headset (or Desktop PC) are on the **same local Wi-Fi / LAN subnet**.
2. Check the **Researcher HUD** in SpatialMAS to see the local IP address and active **OSC Listen Port** (default: **`6011`**).
3. Switch SpatialMAS to **Scenario 0 (External OSC Mode)** by pressing **`0`** or clicking **`0. External OSC Mode`** on the HUD.

### 5.2 Running the Automated Python OSC Compatibility Verifier & `human_swarm_examples`
From your workstation terminal, run the included verification suite:

```bash
# Test against Unity running on the same machine (loopback)
python ExternalTools/verify_osc_compatibility.py --ip 127.0.0.1 --port 6011 --duration 5

# Test against an OpenXR device on Wi-Fi (replace with its IP)
python ExternalTools/verify_osc_compatibility.py --ip 192.168.1.42 --port 6011 --duration 30
```

The verifier detects the workstation reply IP automatically; use `--reply-ip <workstation-LAN-IP>` to override it. A missing Unity handshake is an error. The subsequent demonstration traffic requires visual/audio inspection; it is not a full compatibility test. For offline packet validation, run `python ExternalTools/verify_osc_compatibility.py --dry-run`.

You can also run any script from `helping_sources/human_swarm_examples/python/` (e.g., `swarmalators/main.py` or `attractors_repellers/main.py`) directly against port `6011`!

### 5.3 Receiving Live Human XR Embodiment Telemetry in Python (`Port 6012`)
While a user wears the headset (or navigates in Desktop mode), [`XRUserEmbodimentBridge`](../Assets/_SpatialMAS/Runtime/XR/XRUserEmbodimentBridge.cs) and [`OscProtocolRouter`](../Assets/_SpatialMAS/Runtime/Networking/OscProtocolRouter.cs) stream live interaction and 6DoF pose telemetry via UDP OSC to `RemoteIp:RemotePort` (default `127.0.0.1:6012`, updated automatically when `/connect` or `/test/alive` is received):
- `/xr/user/pose <x_mm> <y_mm> <z_mm> <qx> <qy> <qz> <qw>` (25 Hz head 6DoF in arena `mm`)
- `/xr/hands/pose <hand:0=L,1=R> <x_mm> <y_mm> <z_mm> <isPinching:0|1>` (25 Hz hand poses)
- `/agents/position/id <agentId> <x_mm> <y_mm> <z_mm>` (streamed while user grabs/drags an agent)
- `/agents/control <agentId> <controlType:0=LMB,1=RMB> <controlValue:1=press,0=release>`
- `/agents/touch/id <agentId> <touchState:1|0> <otherAgentId>`
- `/boundary/event/id <agentId> <boundaryId> <0=enter,1=exit>` & `/boundaries/Touched <agentId> <boundaryId>`
- `/agents/localcomm/recv <receiverId> <senderId> <msg>` & `/agents/localcomm/id <type> <senderId> <receiverId> <dist_mm> ...`

---

## 6. Recording & Wireless Wi-Fi CSV Telemetry Export

SpatialMAS eliminates the need for USB `adb pull` cables when running user studies on standalone headsets:

1. **Start / Stop Recording**: Press **`L`** or click **`Start CSV [L]` / `Stop CSV [L]`** on the HUD (or send `/logging/state 1` or `/recorder/start` over OSC).
2. **Recorded Files** (saved in `Application.persistentDataPath/SpatialMAS_Logs/`):
   - **`osc_traffic_<timestamp>_<session-id>.csv`**: Timestamped log of inbound/outbound OSC commands.
   - **`xr_embodiment_<timestamp>_<session-id>.csv`**: Time-series log of `HeadPos`, `HeadRot`, `LHandPos`, `LPinch`, `RHandPos`, `RPinch`, `Scenario`, `XRMode`, `WorldScale`, `ActiveAgents`, and `FrameTimeMs`. Sampling targets 50 Hz and is limited by frame rate. Hand rotations are not currently included.
   - Paired files share a session ID. Rapid stop/start creates new files; existing sessions are never truncated. Optional C# session tags are sanitized for filenames. CSV text fields containing commas, quotes, or newlines are quoted.
3. **Download over Wi-Fi**:
   - Open any web browser on a laptop connected to the same Wi-Fi network.
   - Navigate to **`http://<headset-ip>:8080/`** (or `http://127.0.0.1:8080/` when running locally on Desktop).
   - Click **`[Download CSV]`** next to any `.csv` file in the HTML dashboard to download it directly to your laptop, or fetch `http://<headset-ip>:8080/api/logs` as JSON for automated analysis pipelines.

---

## 7. Running Automated Tests & CI/CD

### 7.1 Via the Unity CLI (`unity`)
With the Unity Editor open:
```powershell
# List all discovered EditMode and PlayMode tests
unity command test list --mode all

# Run EditMode unit tests (OSC codec, coordinate mapper, DSP synth, scenario math, CSV logger)
unity command test run --mode editor

# Run PlayMode integration tests (async across domain reload)
unity command test run --mode playmode --async_tests true
unity command test status
```

### 7.2 Via the Unity Editor Test Runner Window
1. Open **`Window > General > Test Runner`**.
2. Select **EditMode** or **PlayMode** and click **Run All**.

### 7.3 Local Batch Runs and Lightweight GitHub CI

With the Editor closed, `unity test . --mode EditMode --output Logs/editmode-results.xml` and `unity test . --mode PlayMode --output Logs/playmode-results.xml` launch the installed Editor automatically. Keep hardware testing separate from these regressions.

GitHub Actions checks JSON, assembly definitions, Unity `.meta` pairing, Python packet encoding, and feedback acceptance/timeout handling. It installs only `python-osc`; Unity and device tests run locally. Run the same Python regressions with `python -m unittest discover -s ExternalTools -p 'test_*.py'`.
