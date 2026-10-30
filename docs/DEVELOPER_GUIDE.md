# SpatialMAS — Unity 6 Developer Guide (`DEVELOPER_GUIDE.md`)

## 1. Repository & Assembly Structure

SpatialMAS uses strict **Assembly Definitions (`.asmdef`)** under `Assets/_SpatialMAS/` for fast incremental compilation and clean test isolation:

- **`SpatialMAS.Runtime.asmdef`** (`Assets/_SpatialMAS/Runtime/`):
  - `Core/`: `SpatialMASBootstrapper`, `SimulationCommandBus`, `SpatialCoordinateMapper`, `SpatialMASConfig`, `SpatialCalibrationReference`
  - `Networking/`: `OscPacketCodec`, `OscUdpServer`, `OscUdpClient`, `OscProtocolRouter`
  - `Audio/`: `IAgentAudioSynth`, `NativeDspSynthesizer`, `SpatialAudioBankPlayer`, `AudioListenerProxy`
  - `Agents/`: `AgentManager`, `AgentEntity`, `AgentVisualController`, `ProceduralMeshFactory`
  - `Environment/`: `BoundaryManager`, `EnvironmentArrowManager`
  - `LocalComm/`: `LocalCommManager`
  - `XR/`: `XRPlatformAdapter`, `XRModeSwitcher`, `XRInteractionRigSetup`, `XRTrackedHandInteractor`, `XRHandSkeletonVisual`, `XRHandTrackingProcessor`, `XRHandPinchHysteresis`, `XRUserEmbodimentBridge`, `DesktopInteractionRig`
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
- [`XRPlatformAdapter.cs`](../Assets/_SpatialMAS/Runtime/XR/XRPlatformAdapter.cs) rechecks running displays, including delayed Link initialization, and enables tracked camera input only with an active headset. Desktop navigation yields to headset tracking. [`XRModeSwitcher.cs`](../Assets/_SpatialMAS/Runtime/XR/XRModeSwitcher.cs) independently checks Meta passthrough capability/provider availability before enabling AR managers and removing the preview grid. `DisplayStatus` distinguishes VR, MR starting, active MR, desktop preview, and unavailable MR; a requested MR mode alone does not mean passthrough is running.
- [`XRHandPinchHysteresis.cs`](../Assets/_SpatialMAS/Runtime/XR/XRHandPinchHysteresis.cs) prefers valid native Meta Aim pointing poses and classified index/middle pinch input, suppressing system gestures. Optical joint pinch hysteresis and configured controller actions provide fallbacks. Physical hand telemetry retains joint/root poses transformed through Camera Offset; it does not substitute the aiming pose or a stale untracked hand.
- [`XRHandSkeletonVisual.cs`](../Assets/_SpatialMAS/Runtime/XR/XRHandSkeletonVisual.cs) retains its component name/GUID for scene compatibility, but now instantiates the Quest skinned hand meshes used by `helping_sources/laser-xr/Assets/Scenes/MR_HandsInteraction.unity`. The minimal asset closure is under `Assets/_SpatialMAS/Resources/Hands/`: `Models/LeftHand.fbx` and `RightHand.fbx`, the reference translucent **Unity_Hand_Medium** and **DepthOnly** materials, shaders, four shader subgraphs, and the highlight mask texture. Setup is programmatic; it does not import the complete demo rig or visual prefabs. The medium material preserves the reference settings, while the depth shader includes explicit URP stereo support. Asset provenance and license notices are in `Assets/_SpatialMAS/ThirdPartyNotices/`. `XRHandTrackingEvents` supplies Dynamic and BeforeRender updates to `XRHandSkeletonDriver` and `XRHandMeshController`, including visibility changes on tracking acquisition/loss.
- Hand visuals share the same tracking-origin frame as interactions and recorded physical poses. The visual container stays at identity under that origin; its skeleton driver applies the tracked wrist/root pose. Do not also position its parent at the wrist/aim pose or add a manual calibration offset, which would apply tracking twice. Provider wrist/tip pose comparisons validate this coordinate path; visual overlap with real hands in passthrough remains an in-headset check.
- `AgentEntity` explicitly registers its trigger collider with `XRGrabInteractable`; XRI's automatic collider discovery excludes triggers. If adding a collider after an interactable is enabled, disable the interactable before updating its collider list and re-enable it afterward so the interaction manager refreshes its collider mapping. Keep agent colliders as triggers and the near/far casters configured to hit triggers. PlayMode regressions exercise near and far selection, movement, release, and pooled reuse through the interaction manager.
- The generated world panel uses `TrackedDeviceGraphicRaycaster`, `XRUIInputModule`, and tracked ray/button input. Its Text/Image graphics use `Resources/Mat_SpatialMAS_WorldUI.mat` with the `SpatialMAS/World UI Stereo` shader and explicit single-pass instanced stereo handling. Runtime setup repairs canvas camera/input references and pending interactor registrations. Initial placement waits for tracked head poses, then leaves the panel anchored in the world; **Move Panel Here** repositions it. Scenario, preset, pause/reset, MR/VR, recording, IDs, next physical layout/placement, and calibration reference controls are available, alongside the desktop IMGUI panel.

### Quest Link Configuration and Passthrough Diagnostics

`SpatialMASEditorBuilder` enables **Composition Layers Support** explicitly alongside Meta Session/Camera features for both Standalone and Android; enabling the camera feature alone is insufficient. Editor Play uses Standalone XR settings regardless of the selected build target. Camera/pipeline HDR are disabled, and MR disables camera post-processing while clearing to transparent black. Meta's `ARCameraManager` starts the compositor passthrough layer; `ARCameraBackground` is unnecessary. See [Unity's passthrough setup](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.6/manual/features/camera/passthrough.html) and [graphics settings](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.6/manual/get-started/graphics-settings.html).

Windows Link testing additionally requires the Meta OpenXR runtime and Link's developer passthrough/spatial-data options; restart the Editor after changing them. The headset receives compositor passthrough, while Game view can remain black behind virtual content. See [Unity's Link requirements](https://docs.unity3d.com/Packages/com.unity.xr.meta-openxr@2.6/manual/get-started/link.html) and the [setup guide's Link workflow](SETUP_AND_USAGE_GUIDE.md#411-quest-link-passthrough-in-the-windows-editor). Provider/status and generated-rig regressions are automated checks; visual passthrough, stereo UI, hand/controller interactions, and performance require a headset test.

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

Scenarios store world-meter state and use `ScenarioContext.ScenarioCenter` plus `ScenarioContext.Layout` for the next spawn. Layouts contain literal physical dimensions and motion defaults. There is no global scale or frame-rebasing interface. Calibration reference changes must not affect simulation state. `ScenarioManager.ParameterApplied` and `PresetApplied` record direct UI/module changes as session events. See [the coordinate and recording contract](ARCHITECTURE.md#5-physical-coordinate-and-calibration-contract).

---

## 5. Plugging in an Optional Custom Audio Engine (e.g., `CsoundUnity` or `FMOD`)

Every agent currently uses [`NativeDspSynthesizer.cs`](../Assets/_SpatialMAS/Runtime/Audio/NativeDspSynthesizer.cs). `IAgentAudioSynth` defines an adapter contract, but `AgentEntity` still holds a concrete native synth reference. A custom backend requires changing that binding and construction path; attaching another component alone does not replace it.

## 6. Regression Tests

Keep Unity NUnit regressions in the existing EditMode/PlayMode assemblies and run them locally through Unity CLI or Test Runner. Coverage includes recording restarts and synchronized world samples, calibration independence/locking/history, physical layout reset stability, canonical and legacy OSC units, continuous-tone ordering, XR settings and generated inputs/UI, listener ownership, desktop click/drag lifecycle, OSC targeting/movement/boundaries, explicit physical sizes/placement, and scenario controls/assembly graphs. Synthetic hand/mouse input tests temporarily clone the input settings to ignore window focus and restore the original settings afterward, allowing CLI runs with an unfocused Game view. Hardware interaction and platform performance remain device checks.

GitHub CI intentionally runs only asset/JSON integrity and Python packet/feedback tests. It installs `python-osc` and requires no Unity license, Unity Docker image, .NET SDK, NumPy, or device simulator. The live OSC verifier first requires a matching handshake and reports traffic transmission separately from runtime behavior; `--dry-run` sends no traffic.
