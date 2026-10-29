# SpatialMAS — Multiplatform XR Multi-Agent Interaction & Spatial Sonification Platform

**SpatialMAS** is a Mixed Reality (MR) and Virtual Reality (VR) multi-agent simulation and procedural spatial audio research prototype built in **Unity 6 (`6000.6.6f1`)**.

Developed under the University of Oslo (UiO) Qualification Grant (*SpatialMAS*, 2026), it provides:
1. **Desktop and OpenXR implementation**: Includes desktop controls and Android/PC OpenXR setup for head tracking and tracked-hand interaction. Headset passthrough, performance, and platform builds require validation on the target hardware. The **Apple Vision Pro (`visionOS`) build menu is experimental scaffolding**; a visionOS XR provider and device validation are still required.
2. **`HS-ims` OSC v1 Compatibility + v2 XR Extensions**: Implements the legacy signatures documented in the [OSC API specification](./docs/OSC_API_SPECIFICATION.md) on UDP port `6011`, plus **MR/VR switching** (`/xr/mode`), **live arena scale and origin changes** (`/xr/scale`, `/xr/origin`), and **head/hand pose streaming** (`/xr/user/pose`, `/xr/hands/pose`). Regression tests cover selected compatibility cases; full Python/Max/C++ application compatibility is not yet certified.
3. **Zero-Dependency Native C# Procedural DSP Synthesizer (`NativeDspSynthesizer`)**: Per-agent 3D spatialized audio synthesis (`OnAudioFilterRead`) featuring anti-aliased PolyBLEP oscillators (Sine, Square, Saw, Triangle), ADSR envelopes, 12 dB/oct Biquad Low-Pass Filter with Resonance, Schroeder Reverb, and **dual CC ID compatibility** (`0/1` Waveform, `82/74` Attack, `83/71` Decay, `85/72` Sustain, `17/73` Release).
4. **Five Built-In No-Code Interactive Scenarios**:
   - **Scenario 0**: *External OSC Controller Mode (`HS-ims` Compatible)*
   - **Scenario 1**: *Crowd Navigation & Human Avoidance* (Reynolds Boids + Helbing Social Force human avoidance + proximity sonification)
   - **Scenario 2**: *3D Swarmalators* (Coupled 3D spatial swarming $J$ and Kuramoto phase synchronization $K$ with 5 macro-state presets)
   - **Scenario 3**: *Orbital Attractors & Repellers* (Interactive Cyan Attractor & Red Repeller cubes + expanding wavefront `/agents/localcomm`)
   - **Scenario 4**: *Musical Encounters* (SysMus25 workshop elastic boundary reflection + radial-quantized musical encounters)
   - **Scenario 5**: *Self-Assembly* (ACSOS 2024 autonomous & hand-assemblable 6-face musical cubes with $90^\circ$ face snapping and coupled polyrhythmic phasors)
5. **Dual Buffered CSV Telemetry + Wi-Fi HTTP Log Export Server (`:8080`)**: Records `osc_traffic_*.csv` and `xr_embodiment_*.csv` (head position/rotation, hand positions, pinch state, and frame time). Every session uses a unique ID and refuses to overwrite an existing file. Telemetry targets 50 Hz, limited by frame rate. Download logs from `http://<device-ip>:8080/` over the local network.

---

## Quickstart (Unity 6000.6.6f1)

1. Open this repository in **Unity `6000.6.6f1`**.
2. In the top Unity menu bar, click:
   **`SpatialMAS -> 1. Build Complete Project (Materials, Prefabs, OpenXR & Main Scene)`**
   *(Alias: `SpatialMAS -> Build Full SpatialMAS Scene & Prefabs`)*
   - Deterministically generates all 6 URP materials (`Assets/_SpatialMAS/Materials/` & `Assets/_SpatialMAS/Resources/`), 3 agent prefabs (`Agent_Sphere.prefab`, `Agent_Cube.prefab`, `Agent_Pyramid.prefab`), `SpatialMASConfig.asset`, and `Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity`.
3. Press **Play** in the Editor to explore the **Game-Like 3D Desktop Viewport** (`WASD`/`Arrow` keys to walk/fly at any time, `[F]` or `RMB (Hold)` to look around, `MMB` to pan, `Scroll` to zoom, `Alt + LMB` to grab agents, `[Space]` pause, `[R]` reset, `[I]` toggle 3D Agent IDs, `[H]` hide HUD), or build under **`SpatialMAS -> Build Target`**:
   - `Build Meta Quest 3 / Android OpenXR (.apk)`
   - `Build Apple Vision Pro (visionOS Xcode Project)`
   - `Build Windows Standalone (x64 .exe)` / `Build macOS Standalone (.app)` / `Build Linux Standalone (x64)`

---

## Documentation (`/docs`)

- **[Complete Setup & Usage Guide (`docs/SETUP_AND_USAGE_GUIDE.md`)](./docs/SETUP_AND_USAGE_GUIDE.md)** *(Start here for step-by-step Unity setup, Unity CLI commands, Meta Quest 3 / Apple Vision Pro deployment, external OSC, and Wi-Fi CSV export)*
- **[System Architecture & ADRs (`docs/ARCHITECTURE.md`)](./docs/ARCHITECTURE.md)**
- **[Complete OSC v1 & v2 API Specification (`docs/OSC_API_SPECIFICATION.md`)](./docs/OSC_API_SPECIFICATION.md)**
- **[Unity 6 Developer & Logic Pack Guide (`docs/DEVELOPER_GUIDE.md`)](./docs/DEVELOPER_GUIDE.md)**
- **[Scenarios & DSP Audio Mathematical Reference (`docs/SCENARIOS_AND_AUDIO.md`)](./docs/SCENARIOS_AND_AUDIO.md)**
- **[Researcher 10-Minute Quickstart Guide (`docs/USER_GUIDE_RESEARCHERS.md`)](./docs/USER_GUIDE_RESEARCHERS.md)**
- **[Master AI Implementation Plan (`SPATIALMAS_IMPLEMENTATION_PLAN.md`)](./SPATIALMAS_IMPLEMENTATION_PLAN.md)**

---

## Running Automated Tests & Python OSC Verification

### Unity CLI (`com.unity.pipeline`) Automation
With the Unity Editor open, you can build the scene and run tests directly from the terminal:
```powershell
# Generate all materials, prefabs, OpenXR settings, and SpatialMAS_Main.unity
unity command script run --file Assets/_SpatialMAS/Editor/SpatialMASEditorBuilder.cs --entry SpatialMAS.Editor.SpatialMASEditorBuilder.BuildAll

# Run EditMode unit tests
unity command test run --mode editor

# Run PlayMode integration tests (async across domain reload)
unity command test run --mode playmode --async_tests true
unity command test status
```

### Unity NUnit Test Suites (`EditMode` & `PlayMode`)
Open **Window -> General -> Test Runner** in Unity 6 to run:
- `SpatialMAS.Tests.EditMode` (OSC codec & `#bundle` unpacking, `(X,Z,Y)` millimeter-to-meter coordinate mapping, Native DSP synth waveforms & dual CC routing, Swarmalator/Crowd/SelfAssembly math, Buffered CSV writer)
- `SpatialMAS.Tests.PlayMode` (Pooled agents & boundary triggers, MR/VR camera mode switching, Wi-Fi HTTP log server download, End-to-End OSC loopback)

### Companion Python OSC Verifier
```bash
pip install -r ExternalTools/requirements.txt
python ExternalTools/verify_osc_compatibility.py --ip 127.0.0.1 --port 6011
```

The live verifier requires an actual `/test/alive/` acknowledgment before sending its demonstration sequence and exits with an error if Unity does not reply. It checks connectivity and sends traffic; inspect Unity for rendering/audio behavior.

GitHub CI installs only `python-osc` and runs asset/JSON integrity checks, four Python feedback/packet tests, and `--dry-run` packet validation. It does not install Unity, containers, NumPy, or a .NET SDK. Run Unity regressions locally:

```powershell
unity test . --mode EditMode --output Logs/editmode-results.xml
unity test . --mode PlayMode --output Logs/playmode-results.xml
```

These commands launch the installed project Editor in batch mode. If the project is already open, use the connected `unity command test run` commands above instead.
