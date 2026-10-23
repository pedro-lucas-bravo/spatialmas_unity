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
  - `XR/`: `XRPlatformAdapter`, `XRModeSwitcher`, `XRHandTrackingProcessor`, `XRHandPinchHysteresis`, `XRUserEmbodimentBridge`, `DesktopInteractionRig`
  - `Scenarios/`: `IScenarioModule`, `ScenarioManager`, and `BuiltIn/` scenarios (`0..5`)
  - `Logging/`: `DataRecordingService`, `EmbodimentTelemetrySampler`, `WifiLogExportServer`
  - `UI/`: `SpatialResearcherHUD`
- **`SpatialMAS.Editor.asmdef`** (`Assets/_SpatialMAS/Editor/`):
  - `SpatialMASEditorBuilder.cs`: Deterministic 1-click generator for all materials, prefabs, and `SpatialMAS_Main.unity`.
  - `MultiPlatformBuildProfileHelper.cs`: 1-click player builders for Meta Quest 3 (`Android`), Apple Vision Pro (`VisionOS`), Windows, macOS, and Linux.
- **`SpatialMAS.Tests.EditMode.asmdef`** & **`SpatialMAS.Tests.PlayMode.asmdef`** (`Assets/_SpatialMAS/Tests/`):
  - Automated NUnit test suites.

---

## 2. One-Click Project & Scene Generation

You never need to manually wire GameObjects in the Unity Inspector. After opening the project in **Unity 6 (`6000.6.6f1`)**:
1. In the top menu bar, click **`SpatialMAS -> 1. Build Complete Project (Materials, Prefabs & Main Scene)`**.
2. This executes [`SpatialMASEditorBuilder.BuildAll()`](../Assets/_SpatialMAS/Editor/SpatialMASEditorBuilder.cs), which deterministically creates:
   - `Assets/_SpatialMAS/SpatialMASConfig.asset`
   - `Assets/_SpatialMAS/Materials/Mat_SpatialMAS_AgentInstanced.mat` & `Mat_SpatialMAS_BoundaryTranslucent.mat`
   - `Assets/_SpatialMAS/Prefabs/Agent_Sphere.prefab`, `Agent_Cube.prefab`, `Agent_Pyramid.prefab`
   - `Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity` (complete with `ARSession`, `XROrigin`, `XRHandPinchHysteresis`, and `SpatialMASBootstrapper`).

You can also run this headlessly from the command line:
```powershell
Unity.exe -batchmode -nographics -projectPath . -executeMethod SpatialMAS.Editor.SpatialMASEditorBuilder.BuildAll -quit
```

---

## 3. Building for Multiplatform Headsets (Quest 3, Apple Vision Pro, OpenXR & Desktop)

Use the **`SpatialMAS -> Build Target`** menu in Unity Editor:
- **Meta Quest 3 / 3S / Pro & Android OpenXR**: `SpatialMAS -> Build Target -> Build Meta Quest & Android OpenXR (.apk)`
- **Apple Vision Pro (`visionOS`)**: `SpatialMAS -> Build Target -> Build Apple Vision Pro (visionOS Xcode)`
- **Windows / macOS / Linux Standalone**: Select the corresponding desktop target under `SpatialMAS -> Build Target`.

### How Multiplatform Differences Are Handled Automatically
- [`XRPlatformAdapter.cs`](../Assets/_SpatialMAS/Runtime/XR/XRPlatformAdapter.cs) detects whether the app is running on **Meta Quest (`Android`)**, **Apple Vision Pro (`visionOS`)**, **Generic OpenXR**, or **Desktop** at startup.
- [`XRHandPinchHysteresis.cs`](../Assets/_SpatialMAS/Runtime/XR/XRHandPinchHysteresis.cs) reads direct optical finger joint positions from `XRHandSubsystem` when available, and falls back to XRI Input Actions (supporting Apple Vision Pro Spatial Pointer Gaze-and-Pinch, 6DoF controllers, or Desktop Simulator clicks).

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

---

## 5. Plugging in an Optional Custom Audio Engine (e.g., `CsoundUnity` or `FMOD`)

By default, every agent uses [`NativeDspSynthesizer.cs`](../Assets/_SpatialMAS/Runtime/Audio/NativeDspSynthesizer.cs). To use a custom backend, implement [`IAgentAudioSynth`](../Assets/_SpatialMAS/Runtime/Audio/IAgentAudioSynth.cs) on a `MonoBehaviour` and attach it to your agent prefab. `AgentEntity` will automatically discover and route `/agents/audio/synth/note` and `/agents/audio/synth/param_val` commands to your implementation.
