# SpatialMAS — Unity 6 Developer Guide (`DEVELOPER_GUIDE.md`)

## 1. Repository & Assembly Structure

SpatialMAS uses strict **Assembly Definitions (`.asmdef`)** under `Assets/_SpatialMAS/` for fast incremental compilation and clean test isolation:

- **`SpatialMAS.Runtime.asmdef`** (`Assets/_SpatialMAS/Runtime/`):
  - `Core/`: `SpatialMASBootstrapper`, `SimulationCommandBus`, `SpatialCoordinateMapper`, `SpatialMASConfig`
  - `Networking/`: `OscPacketCodec`, `OscUdpServer`, `OscUdpClient`, `OscProtocolRouter`
  - `Audio/`: `IAgentAudioSynth`, `NativeDspSynthesizer`, `SpatialAudioBankPlayer`, `AudioListenerProxy`
  - `Agents/`: `AgentManager`, `AgentEntity`, `AgentVisualController`, `ProceduralMeshFactory`
  - `Environment/`: `BoundaryManager`, `EnvironmentArrowManager`
  - `LocalComm/`: `LocalCommManager`
  - `XR/`: `XRPlatformAdapter`, `XRModeSwitcher`, `XRInteractionRigSetup`, `XRTrackedHandInteractor`, `XRHandTrackingProcessor`, `XRHandPinchHysteresis`, `XRUserEmbodimentBridge`, `DesktopInteractionRig`
  - `Scenarios/`: `IScenarioModule`, `ScenarioManager`, and `BuiltIn/` scenarios (`0..5`)
  - `Logging/`: `DataRecordingService`, `EmbodimentTelemetrySampler`, `WifiLogExportServer`
  - `UI/`: `SpatialResearcherHUD`
- **`SpatialMAS.Editor.asmdef`** (`Assets/_SpatialMAS/Editor/`):
  - `SpatialMASEditorBuilder.cs`: Deterministic 1-click generator for all materials, prefabs, OpenXR settings, and `SpatialMAS_Main.unity`.
  - `MultiPlatformBuildProfileHelper.cs`: 1-click player builders for Meta Quest 3 (`Android`), Apple Vision Pro (`VisionOS`), Windows, macOS, and Linux.
- **`SpatialMAS.Tests.EditMode.asmdef`** & **`SpatialMAS.Tests.PlayMode.asmdef`** (`Assets/_SpatialMAS/Tests/`):
  - Automated NUnit test suites.

---

## 2. One-Click Project & Scene Generation

You never need to manually wire GameObjects in the Unity Inspector. After opening the project in **Unity 6 (`6000.6.6f1`)**:
1. In the top menu bar, click **`SpatialMAS > 1. Build Complete Project (Materials, Prefabs, OpenXR & Main Scene)`** (or **`SpatialMAS > Build Full SpatialMAS Scene & Prefabs`**).
2. This executes [`SpatialMASEditorBuilder.BuildAll()`](../Assets/_SpatialMAS/Editor/SpatialMASEditorBuilder.cs), which deterministically creates:
   - `Assets/_SpatialMAS/SpatialMASConfig.asset` & `Assets/_SpatialMAS/Resources/DefaultSpatialMASConfig.asset`
   - `Assets/_SpatialMAS/Materials/` (and `Assets/_SpatialMAS/Resources/`): `Mat_SpatialMAS_AgentInstanced.mat`, `Mat_SpatialMAS_BoundaryTranslucent.mat`, `Mat_SpatialMAS_Wavefront.mat`, `Mat_SpatialMAS_Arrow.mat`, `Mat_SpatialMAS_VRFloor.mat`, `Mat_SpatialMAS_VRGridLine.mat`
   - `Assets/_SpatialMAS/Prefabs/Agent_Sphere.prefab`, `Agent_Cube.prefab`, `Agent_Pyramid.prefab`
   - `Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity` (complete with `ARSession`, `XROrigin`, `TrackedPoseDriver`, `XRHandPinchHysteresis`, and `SpatialMASBootstrapper`).

You can also run this via the Unity CLI (`com.unity.pipeline`):
```powershell
unity command script run --file Assets/_SpatialMAS/Editor/SpatialMASEditorBuilder.cs --entry SpatialMAS.Editor.SpatialMASEditorBuilder.BuildAll
```

---

## 3. Building for Multiplatform Headsets (Quest 3, Apple Vision Pro, OpenXR & Desktop)

Use the **`SpatialMAS > Build Target`** menu in Unity Editor:
- **Meta Quest 3 / 3S / Pro & Android OpenXR**: `SpatialMAS > Build Target > Build Meta Quest & Android OpenXR (.apk)`
- **Apple Vision Pro (`visionOS`, experimental scaffolding)**: The build menu exists, but a visionOS XR provider, input integration, and device validation are still required.
- **Windows / macOS / Linux Standalone**: Select the corresponding desktop target under `SpatialMAS > Build Target`.

### How Multiplatform Differences Are Handled Automatically
- [`XRPlatformAdapter.cs`](../Assets/_SpatialMAS/Runtime/XR/XRPlatformAdapter.cs) detects whether the app is running on **Meta Quest (`Android`)**, **Apple Vision Pro (`visionOS`)**, **Generic OpenXR**, or **Desktop** at startup. On Desktop without an HMD, it disables `TrackedPoseDriver` and `ARSession` so `XRSimulation` never interferes with [`DesktopInteractionRig.cs`](../Assets/_SpatialMAS/Runtime/XR/DesktopInteractionRig.cs).
- [`XRHandPinchHysteresis.cs`](../Assets/_SpatialMAS/Runtime/XR/XRHandPinchHysteresis.cs) reads optical finger joints or fallback controller actions. Hand poses are transformed from tracking space through the rig's Camera Offset. Missing hand tracking does not create a repulsion field at a stale position.
- The generated world panel uses `TrackedDeviceGraphicRaycaster` and `XRUIInputModule` for scenario, preset, pause/reset, MR/VR, recording, IDs, scale, and arena placement controls. The desktop IMGUI panel remains available.

---

## 4. Authoring a New Modular Scenario ("Logic Pack")

To add a new scenario (or commercial Logic Pack):
1. Create a class implementing [`IScenarioModule`](../Assets/_SpatialMAS/Runtime/Scenarios/IScenarioModule.cs):
   ```csharp
   using SpatialMAS.Core;
   using SpatialMAS.Scenarios;

   public class CustomFlockingLogicPack : IScenarioModule
   {
       public int ScenarioId => 6;
       public string DisplayName => "6. Custom Flocking Logic Pack";
       public string ShortDescription => "Custom behavioral experiment.";

       private ScenarioContext _ctx;

       public void OnScenarioStart(ScenarioContext context)
       {
           _ctx = context;
           _ctx.Bus.InstantiateAgents(AgentShapeType.Sphere, AgentMovementMode.Teleport, 20, 0.06f, "127.0.0.1", 6012);
       }

       public void OnScenarioTick(float deltaTime)
       {
           // Update agent positions, colors, and synth notes via _ctx.Bus
       }

       public void OnScenarioStop() => _ctx.Bus?.SetAgentMute(-1, true);
       public void SetParameter(string paramName, float value) { }
       public void ApplyPreset(int presetIndex) { }
   }
   ```
2. Register it in `ScenarioManager.Initialize()` via `RegisterScenario(new CustomFlockingLogicPack())`.

If a scenario caches world-space state, also implement `ISpatialFrameAwareScenario.ApplySpatialFrameChange(SpatialFrameChange change)`. Transform cached positions with `TransformPosition`, velocities with `TransformVector`, orientations with `TransformRotation`, and scalar sizes with `ScaleRatio`. World managers already transform the entities, so a scenario should rebase only its own caches.

---

## 5. Plugging in an Optional Custom Audio Engine (e.g., `CsoundUnity` or `FMOD`)

Every agent currently uses [`NativeDspSynthesizer.cs`](../Assets/_SpatialMAS/Runtime/Audio/NativeDspSynthesizer.cs). `IAgentAudioSynth` defines an adapter contract, but `AgentEntity` still holds a concrete native synth reference. A custom backend requires changing that binding and construction path; attaching another component alone does not replace it.

## 6. Regression Tests

Keep Unity NUnit regressions in the existing EditMode/PlayMode assemblies and run them locally through Unity CLI or Test Runner. Coverage includes recording restarts, continuous-tone ordering, XR settings and generated inputs/UI, listener ownership, desktop click/drag lifecycle, OSC targeting/movement/boundaries, arena rebasing, and scenario controls/assembly graphs. Hardware interaction and platform performance remain device checks.

GitHub CI intentionally runs only asset/JSON integrity and Python packet/feedback tests. It installs `python-osc` and requires no Unity license, Unity Docker image, .NET SDK, NumPy, or device simulator. The live OSC verifier first requires a matching handshake and reports traffic transmission separately from runtime behavior; `--dry-run` sends no traffic.
