# SpatialMAS — Complete Setup & Usage Guide

This guide provides step-by-step instructions for setting up, building, deploying, and operating **SpatialMAS** across **Unity Editor**, **Desktop Standalone**, **Meta Quest 3 / 3S / Pro**, **Apple Vision Pro (`visionOS`)**, and **Generic OpenXR Headsets**, as well as controlling the Unity Editor via the **Unity CLI (`unity` / `com.unity.pipeline`)**.

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

SpatialMAS includes an automated Editor builder ([`SpatialMASEditorBuilder.cs`](../Assets/_SpatialMAS/Editor/SpatialMASEditorBuilder.cs)) that generates all required materials, procedural agent prefabs (`sphere`, `cube`, `cylinder`, `capsule`, `prism`, `pyramid`), OpenXR settings, and the main scene (`Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity`).

You can trigger the builder either from the **Unity Editor UI** or directly from your terminal using the **Unity CLI (`unity`)**.

### Option A: From the Unity Editor Menu
1. Open the project in Unity `6000.6.6f1`.
2. In the top menu bar, click:
   - **`SpatialMAS > Build Full SpatialMAS Scene & Prefabs`**
   - **`SpatialMAS > Configure Multiplatform Build Settings`**
3. Verify in the Project window that the following assets are populated:
   - `Assets/_SpatialMAS/Resources/DefaultSpatialMASConfig.asset`
   - `Assets/_SpatialMAS/Materials/` (`AgentDefault.mat`, `AgentHighlight.mat`, `BoundaryWall.mat`, `BoundarySolid.mat`, `CommLine.mat`, `EnvironmentArrow.mat`)
   - `Assets/_SpatialMAS/Prefabs/` (`Agent_sphere.prefab`, `Agent_cube.prefab`, `Agent_cylinder.prefab`, `Agent_capsule.prefab`, `Agent_prism.prefab`, `Agent_pyramid.prefab`)
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
unity command editor invoke_menu --path "SpatialMAS/Build Full SpatialMAS Scene & Prefabs"
```

---

## 3. Running in Desktop / Editor Mode (Zero-Hardware Workflow)

You can test every SpatialMAS feature—3D spatial audio, all 5 built-in scenarios, external OSC control, and Wi-Fi CSV log export—directly inside the Unity Editor or a Desktop build without an XR headset connected.

1. Open `Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity`.
2. Press **Play** in the Unity Editor (or run `unity command editor.playmode start` from the terminal).
3. [`XRPlatformAdapter`](../Assets/_SpatialMAS/Runtime/XR/XRPlatformAdapter.cs) automatically detects that no XR HMD is active and enables the [`DesktopInteractionRig`](../Assets/_SpatialMAS/Runtime/XR/DesktopInteractionRig.cs).

### Desktop Keyboard & Mouse Controls

| Input | Action |
| :--- | :--- |
| **`Right Mouse Button` (Hold) + Mouse Move** | Rotate camera look direction (Pitch & Yaw) |
| **`W` / `A` / `S` / `D`** | Move horizontally in 3D space |
| **`E` / `Q`** | Move vertically Up (`+Y`) / Down (`-Y`) |
| **`Left Shift` (Hold)** | Fast fly speed (`8.0 m/s`) |
| **`Left Mouse Button` (Hold on Agent)** | Grab and drag an agent in 3D space (simulates XR hand pinch) |
| **`Mouse Scroll Wheel`** | Push / pull a grabbed agent closer or further along the camera ray |
| **`Tab`** | Toggle **MR Passthrough** (Transparent AR skybox) $\leftrightarrow$ **VR Immersive** (Dark Studio + Grid floor) |
| **`0`** | Switch to **Scenario 0: External OSC Mode** (awaits UDP packets on port `6011`) |
| **`1`** | Switch to **Scenario 1: Crowd Navigation & Human Avoidance** |
| **`2`** | Switch to **Scenario 2: 3D Swarmalators** (Sync + Phase-Coupled Spatial Audio) |
| **`3`** | Switch to **Scenario 3: Orbital Attractors & Repellers** |
| **`4`** | Switch to **Scenario 4: Musical Encounters** (Harmonic Proximity Chords) |
| **`5`** | Switch to **Scenario 5: Self-Assembly** (6-Face Cubic Docking & Polyrhythmic Phasors) |
| **`Space`** | Pause / Resume the active simulation scenario |
| **`R`** | Reset the active scenario and re-seed agents |
| **`H`** | Show / Hide the Researcher HUD overlay |
| **`L`** | Start / Stop CSV Telemetry Recording (`osc_traffic_*.csv` & `xr_embodiment_*.csv`) |

---

## 4. Deploying to XR Headsets (Multiplatform OpenXR)

### 4.1 Meta Quest 3 / 3S / Pro (`Android` Target)
1. Enable **Developer Mode** on your Meta Quest headset via the Meta Horizon mobile app.
2. In Unity, switch to the **Android** build target:
   - Via UI: `File > Build Profiles > Android > Switch Platform`
   - Via Unity CLI: `unity command build.target switch --target Android --confirm true`
3. Verify OpenXR settings (`Edit > Project Settings > XR Plug-in Management > OpenXR` under the **Android** tab):
   - **OpenXR** checked
   - **Meta Quest Support** feature group enabled
   - **Hand Tracking Subsystem** & **Meta Hand Tracking Aim** enabled
   - **AR Session / Passthrough** enabled
4. Connect your Meta Quest 3 via USB-C and build:
   - Via UI: `File > Build And Run`
   - Via Unity CLI: `unity command build run --target Android --outputPath Builds/Android/SpatialMAS.apk --confirm true`
5. **In-Headset Operation**:
   - The app launches in **MR Passthrough Mode** by default (`CameraClearFlags.SolidColor` with `RGBA(0,0,0,0)`), showing virtual swarm agents floating inside your physical laboratory.
   - Walk physically around the swarm to inspect agents and hear true 3D Doppler and distance-attenuated spatial audio.
   - **Hand Pinch Grab**: Reach within `15 cm` of any agent and pinch your **Index Finger + Thumb** (`< 20 mm` pinch distance to grab, `> 32 mm` to release). Moving your pinched hand repositions the agent in real time and broadcasts its updated pose over OSC.

### 4.2 Apple Vision Pro (`visionOS` Target)
1. On a Mac with Xcode 16+ and the Unity `visionOS` module installed, switch the build target to **VisionOS**:
   - Via UI: `File > Build Profiles > visionOS > Switch Platform`
   - Via Unity CLI: `unity command build.target switch --target VisionOS --confirm true`
2. Build the Xcode project (`Builds/visionOS`), open it in Xcode, configure your Apple Developer Team signing, and deploy to Apple Vision Pro (or the visionOS Simulator).
3. [`XRPlatformAdapter`](../Assets/_SpatialMAS/Runtime/XR/XRPlatformAdapter.cs) automatically detects `visionOS`, enables ARKit Hand Tracking (`XRHandSubsystem`), and configures the camera background for Mixed Reality passthrough.

### 4.3 Generic OpenXR Headsets (Pico 4 Ultra, Android XR, HTC Vive, SteamVR / PCVR)
- **Standalone Android OpenXR (Pico / Android XR)**: Build for `Android` with the **OpenXR** loader enabled. Standard `XRHandSubsystem` hand tracking and OpenXR composition-layer passthrough are used automatically.
- **PCVR (Quest Link / AirLink / SteamVR / Windows Mixed Reality)**: Keep the build target on `StandaloneWindows64`, ensure your PC OpenXR runtime (Meta Quest Link or SteamVR) is active, and press **Play** in the Editor or launch the Windows executable.

---

## 5. Connecting External Multi-Agent Simulators via OSC

SpatialMAS is 100% backward-compatible with the **ACSOS 2025 `HS-ims` OSC v1 protocol** (`human_swarm_examples` in Python, Max/MSP, and C++) and adds **v2 SpatialMAS XR extensions**.

### 5.1 Network Setup
1. Ensure your workstation (running Python / Max / C++ / ROS 2) and your XR headset (or Desktop PC) are on the **same local Wi-Fi / LAN subnet**.
2. Check the **Researcher HUD** in SpatialMAS to see the local IP address and active **OSC Listen Port** (default: **`6011`**).
3. Switch SpatialMAS to **Scenario 0 (External OSC Mode)** by pressing **`0`** or clicking **`0: External OSC`** on the HUD.

### 5.2 Running the Automated Python OSC Compatibility Verifier
From your workstation terminal, run the included verification suite:

```bash
# Test against Unity running on the same machine (loopback)
python ExternalTools/verify_osc_compatibility.py --ip 127.0.0.1 --port 6011 --mode test

# Test against a Meta Quest 3 or Vision Pro on Wi-Fi (replace with headset IP shown on HUD)
python ExternalTools/verify_osc_compatibility.py --ip 192.168.1.42 --port 6011 --mode demo --duration 30
```

Look for the output:
```text
[PASS] ALL OSC v1 (HS-ims) & v2 (SpatialMAS XR) PROTOCOL CHECKS SUCCEEDED!
```

### 5.3 Receiving Live Human XR Embodiment Telemetry in Python (`Port 6012`)
While a user wears the headset (or navigates in Desktop mode), [`XRUserEmbodimentBridge`](../Assets/_SpatialMAS/Runtime/XR/XRUserEmbodimentBridge.cs) streams their live movements at `30 Hz` via UDP OSC to `OutboundTargetIp:6012`:
- `/xr/user/head <px> <py> <pz> <qx> <qy> <qz> <qw>`
- `/xr/user/hand/left <tracked> <pinching> <strength> <px> <py> <pz>`
- `/xr/user/hand/right <tracked> <pinching> <strength> <px> <py> <pz>`
- `/xr/agent/grabbed <agentId> <handId> <px> <py> <pz>`
- `/xr/agent/released <agentId> <handId> <px> <py> <pz>`

External swarm simulations can subscribe to port `6012` so simulated agents dynamically react to the human researcher's head and hands in real time.

---

## 6. Recording & Wireless Wi-Fi CSV Telemetry Export

SpatialMAS eliminates the need for USB `adb pull` cables when running user studies on standalone headsets:

1. **Start Recording**: Press **`L`** or click **Start CSV Recording** on the HUD (or send `/spatialmas/log/state 1 "experiment_01"` over OSC).
2. **Recorded Files** (saved in `Application.persistentDataPath/Logs/`):
   - **`osc_traffic_<timestamp>.csv`**: Timestamped log of all inbound/outbound OSC commands.
   - **`xr_embodiment_<timestamp>.csv`**: 50 Hz time-series log of `head_pos`, `head_rot`, `left_hand`, `right_hand`, `pinch_state`, `active_scenario`, `xr_mode`, and `frametime_ms`.
3. **Download over Wi-Fi**:
   - Open any web browser on a laptop connected to the same Wi-Fi network.
   - Navigate to **`http://<headset-ip>:8080/`** (or `http://localhost:8080/` when running locally on Desktop).
   - Click any `.csv` file in the HTML dashboard to download it directly to your laptop, or fetch `http://<headset-ip>:8080/api/logs` as JSON for automated analysis pipelines.

---

## 7. Running Automated Tests & CI/CD

### 7.1 Via the Unity CLI (`unity`)
With the Unity Editor open:
```powershell
# List all discovered EditMode and PlayMode tests
unity command test list --mode all

# Run EditMode unit tests (OSC codec, coordinate mapper, DSP synth, scenario math, CSV logger)
unity command test run --mode editor

# Run PlayMode integration tests (Agent pooling, XR mode switching, Wi-Fi HTTP server, E2E OSC loopback)
unity command test run --mode playmode
```

### 7.2 Via the Unity Editor Test Runner Window
1. Open **`Window > General > Test Runner`**.
2. Select **EditMode** or **PlayMode** and click **Run All**.
