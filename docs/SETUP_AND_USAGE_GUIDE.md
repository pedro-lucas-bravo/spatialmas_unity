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

The world panel also loads `Assets/_SpatialMAS/Resources/Mat_SpatialMAS_WorldUI.mat`, which uses the project's stereo UI shader.

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
3. Without an active XR display, [`XRPlatformAdapter`](../Assets/_SpatialMAS/Runtime/XR/XRPlatformAdapter.cs) selects desktop controls and disables tracked camera input; [`XRModeSwitcher`](../Assets/_SpatialMAS/Runtime/XR/XRModeSwitcher.cs) keeps AR managers disabled. Both recheck runtime availability so a headset that finishes initializing later can take over. Desktop MR is a grid preview without a camera feed.

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
   - **Meta Quest: Session**, **Meta Quest: Camera (Passthrough)**, and **Composition Layers Support** enabled
4. Connect your Meta Quest 3 via USB-C and build:
   - Via UI: `SpatialMAS > Build Target > Build Meta Quest & Android OpenXR (.apk)` or `File > Build And Run`
   - Via Unity CLI: `unity command build run --target Android --outputPath Builds/Android_OpenXR/SpatialMAS_Quest.apk --confirm true`
5. **In-Headset Operation**:
   - Use the world-space researcher panel to choose scenarios/presets, pause/reset, toggle MR/VR or recording, show IDs, select the next physical layout/spawn center, and show/unlock/move/rotate/lock the calibration reference. Point with a tracked hand/controller and pinch/press to activate its controls.
   - The app requests **MR Passthrough** by default. The panel reports **MR passthrough**, **MR starting**, **MR preview (no headset)**, or **MR unavailable (check OpenXR / Link settings)** according to the detected display and camera provider. An unavailable provider retains the preview grid.
   - Walk physically around the swarm to inspect agents and hear true 3D Doppler and distance-attenuated spatial audio.
   - **Hands and selection**: Tracked hands use the same Quest skinned hand meshes and translucent **Unity_Hand_Medium** / **DepthOnly** materials as the `laser-xr` reference scene. A valid **Meta Aim** pose supplies the pointing ray and the runtime's index/middle pinch classifier supplies selection; joint-distance pinch hysteresis is the fallback (default 22 mm activation / 40 mm release). Point at panel controls and pinch, or reach toward an interactive cube and pinch to grab it. Controller input is also supported. Moving a grabbed agent broadcasts `/agents/position/id`.
   - **Hand alignment**: Visuals, interactions, and recordings share the rig's tracking-origin frame. No separate wrist/aim-parent transform or manual hand-calibration offset is applied. Provider pose comparisons check coordinate consistency; physical overlap of the mesh with the passthrough hand still requires an in-headset retest.

### 4.1.1 Quest Link Passthrough in the Windows Editor

Quest Link supports development passthrough with this OpenXR stack. In the Meta Horizon/Quest Link PC app, set Meta Link as the **active OpenXR runtime**, then enable **Developer Runtime Features**, **Passthrough over Meta Horizon Link**, and **Spatial data over Meta Horizon Link**. Older Link versions place these under **Beta** instead of **Developer**. Restart Unity after changing these options. See [Meta's Link development setup](https://developers.meta.com/vr/documentation/unity/unity-link/) and [Unity's Meta OpenXR 2.6 Link setup](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.6/manual/get-started/link.html).

In Unity's **Windows, Mac, Linux / Standalone** XR settings, select OpenXR, disable XR Simulation for this workflow, and enable the Oculus Touch profile plus the Meta Session, Camera (Passthrough), Hand Tracking/Aim, and Composition Layers features. **Editor Play uses Standalone XR settings even when Android is the selected build target.** The SpatialMAS configuration command explicitly enables Composition Layers for both targets.

The scene uses `ARCameraManager` and a transparent solid camera background. Camera and pipeline HDR are disabled; MR also disables camera post-processing to preserve alpha. `ARCameraBackground` is not required for Meta's compositor passthrough. See [Unity's passthrough configuration](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.6/manual/features/camera/passthrough.html) and [URP graphics guidance](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.6/manual/get-started/graphics-settings.html).

**Inspect passthrough inside the headset.** Unity's Game view does not include its camera feed, so a black Game-view background can be normal. If the headset also shows the grid, check the panel's MR status and the settings above. The world panel has an explicit stereo UI material and uses tracked hand/controller rays; desktop-only mode retains mouse controls. Verify both-eye panel rendering, button selection, hand tracking, cube grabs, and MR/VR switching on the connected headset before an experiment. Automated tests do not establish hardware support or performance.

### 4.2 Apple Vision Pro (`visionOS` Target, Experimental)
The build menu requests a visionOS Xcode project, but the repository does not include a visionOS XR provider or a validated spatial-pointer rig. Platform detection alone does not supply these features. Complete provider, input, rendering, signing, and device validation on a Mac before treating this as a supported deployment target.

### 4.3 Generic OpenXR Headsets (Pico 4 Ultra, Android XR, HTC Vive, SteamVR / PCVR)
- **Standalone Android OpenXR (Pico / Android XR)**: Build for `Android` with the **OpenXR** loader enabled. Hand tracking and passthrough depend on the device's provider and enabled features; a transparent camera background alone does not guarantee passthrough.
- **PCVR**: Enable OpenXR under the Standalone XR settings and use the runtime for your connected headset, then press **Play** in the Editor or launch `Builds/Windows64/SpatialMAS.exe`. Quest Link passthrough requires the Meta runtime and the additional setup in section 4.1.1; other PCVR runtimes are not assumed to provide Meta passthrough.

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

The demonstration uses explicit Unity-world millimeters (50 mm spheres, 70 mm cubes, a 600 mm boundary radius centered at `(0,1200,800)` mm), with no arena scale setting. The verifier removes its agents (IDs `0..17`) and boundary (`1`) on completion, Ctrl+C, or a setup/streaming failure after instantiation. Add `--keep-scene` to retain these demonstration objects for inspection. Cleanup uses targeted OSC removals and leaves Unity in Play mode, without resetting recording state or calibration settings. As before, starting the demonstration replaces existing agents and sets scenario `0`; it does not restore a previous experiment.

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
2. **Recorded Files** (all under `Application.persistentDataPath/SpatialMAS_Logs/`, sharing a session ID):
   - `osc_traffic_*.csv`: Raw protocol traffic.
   - `xr_embodiment_*.csv`: Actual head/hand world poses, full quaternions and tracking flags, scenario/layout/center, reference revision and tracking-origin pose/scale.
   - `object_state_*.csv`: Actual agent/boundary/arrow poses and physical dimensions at the same sample timestamps.
   - `calibration_*.csv`: Initial reference snapshot and every change.
   - `session_events_*.csv`: Scenario/parameter/preset and calibration/tracking events.
   - `session_metadata_*.json`: Schema version 2, units/axes/quaternion conventions, configuration snapshot and file inventory.
   - Join object and participant rows on `(SessionId, SampleId)`. Sampling targets 50 Hz with at most one observation per frame. Use `ElapsedMs` for analysis rather than assuming fixed intervals. All pose data stays in Unity-world meters and quaternion `x,y,z,w`; OSC traffic uses the units documented for each endpoint. Files use unique names and never truncate earlier sessions.
3. **Download over Wi-Fi**: Open `http://<device-ip>:8080/` (or localhost on desktop) and download all six session files. `/api/logs` lists CSV and JSON downloads.
4. **Calibration workflow**: Unlock the reference, place/rotate it to match a known physical frame observed by the external system, then lock it. Hiding the marker leaves its frame active. Convert world data to reference coordinates using the matching calibration revision, as specified in [Architecture sections 5–6](ARCHITECTURE.md#5-physical-coordinate-and-calibration-contract). Reference pose changes do not move the simulation. Revalidate alignment after tracking-origin changes; spatial alignment does not synchronize external clocks.

Old world-scale/origin JSON fields are ignored and the old `/xr/scale`/`/xr/origin` OSC commands return `/error`. Existing legacy scripts now use actual physical millimeters without the former `.20` multiplier or chest-height offset. Use `/world/...` for Unity XYZ messages, or keep the legacy Y/Z axis adapter explicitly. Old recordings are left unchanged; use their original interpretation rather than the new schema.

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
