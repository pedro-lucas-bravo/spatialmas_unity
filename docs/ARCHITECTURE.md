# SpatialMAS — System Architecture & Technical Design (`ARCHITECTURE.md`)

## 1. Overview & Clean-Room Mandate

**SpatialMAS** is a commercial-grade, multiplatform Mixed Reality (MR) and Virtual Reality (VR) multi-agent interaction and 3D spatial sonification platform built in **Unity 6 (`6000.6.6f1`)**.

Designed under the University of Oslo (UiO) Qualification Grant (*SpatialMAS*, 2026), the codebase is architected from the ground up as a **100% clean-room proprietary implementation** to ensure Freedom to Operate (FTO) and commercial licensing readiness, while preserving **100% backward compatibility** with the published [ACSOS 2025 `HS-ims` OSC Protocol](../helping_sources/ACSOS2025___Human_Swarm_IMS_Artifact_camera_ready-public-INCONFERENCE.pdf).

---

## 2. High-Level C4 Component Architecture

```mermaid
flowchart TD
    subgraph External["External Controllers & Researcher Browser"]
        OSCExt["Python / Max / C++ Scripts\n(UDP Port 6011 In / Dynamic Out)"]
        WebBrowser["Laptop Web Browser\n(http://<headset-ip>:8080/ CSV Download)"]
    end

    subgraph Runtime["SpatialMAS.Runtime Assembly (Unity 6000.6.6f1)"]
        Boot["SpatialMASBootstrapper\n(Composition Root)"]
        Bus["SimulationCommandBus\n(Unified Event & Command Router)"]
        Mapper["SpatialCoordinateMapper\n((X,Z,Y) mm <-> m + WorldScale + OriginOffset)"]

        subgraph Net["Networking & Telemetry"]
            UdpSrv["OscUdpServer\n(Async Socket, 1MB Buffer, ConcurrentQueue)"]
            UdpCli["OscUdpClient\n(Dynamic Remote Endpoint)"]
            Router["OscProtocolRouter\n(v1 HS-ims + v2 SpatialMAS XR)"]
            Rec["DataRecordingService\n(64KB Buffered OSC + Embodiment CSVs)"]
            HttpSrv["WifiLogExportServer\n(TcpListener Port 8080 Dashboard)"]
        end

        subgraph World["Pooled World Entities & Native DSP Audio"]
            AgentMgr["AgentManager & AgentEntity\n(Sphere, Cube, Pyramid)"]
            BoundMgr["BoundaryManager\n(Sphere & Cube Boundaries)"]
            ArrowMgr["EnvironmentArrowManager\n(Procedural 3D Arrows)"]
            LocalComm["LocalCommManager\n(Pooled Expanding Wavefronts)"]
            Synth["NativeDspSynthesizer\n(OnAudioFilterRead PolyBLEP + ADSR + Biquad LPF + Reverb)"]
            Listener["AudioListenerProxy\n(Head-Locked 6DoF / OSC Detachable)"]
        end

        subgraph XR["Multiplatform XR & Desktop Layer"]
            Plat["XRPlatformAdapter\n(Quest 3 / Apple Vision Pro / OpenXR / Desktop)"]
            Mode["XRModeSwitcher\n(MR Passthrough Alpha=0 <-> VR Immersive Studio)"]
            Hands["XRHandTrackingProcessor & XRHandPinchHysteresis\n(Zero-Lag Deadband + Schmitt Trigger Pinch)"]
            Embod["XRUserEmbodimentBridge\n(Head/Hand 6DoF + Helbing Human Repeller)"]
            Desktop["DesktopInteractionRig\n(RMB+WASD Fly & LMB Raycast Grab)"]
        end

        subgraph Scenarios["ScenarioManager & Built-In No-Code Scenarios"]
            S0["0: ExternalOscScenario"]
            S1["1: CrowdAvoidanceScenario"]
            S2["2: SwarmalatorsScenario"]
            S3["3: AttractorsRepellersScenario"]
            S4["4: MusicalEncountersScenario"]
            S5["5: SelfAssemblyScenario"]
        end
    end

    OSCExt <--> UdpSrv
    UdpCli --> OSCExt
    WebBrowser <--> HttpSrv
    UdpSrv --> Router
    Router <--> Bus
    Router --> UdpCli
    Scenarios <--> Bus
    Bus --> Mapper
    Bus --> AgentMgr
    Bus --> BoundMgr
    Bus --> ArrowMgr
    Bus --> LocalComm
    Bus --> Listener
    Bus --> Mode
    Bus --> Rec
    Rec --> HttpSrv
    AgentMgr --> Synth
    Embod --> Bus
```

---

## 3. Architecture Decision Records (ADRs)

### ADR-001: Unified `SimulationCommandBus` for External OSC & Built-In Scenarios
- **Context**: In the academic prototype (`human_swarm_ims`), external OSC scripts and internal C# examples (`SelfAssembly`) were implemented in completely separate scenes and classes, duplicating agent management, audio synthesis, and logging.
- **Decision**: Route **both** external UDP OSC messages (`OscProtocolRouter`) and internal C# scenarios (`IScenarioModule`) through a single `SimulationCommandBus`.
- **Consequences**: All 5 built-in scenarios automatically benefit from spatial audio synthesis, boundary collision events, hand grabbing, and CSV recording—and external researchers can inject live OSC parameter overrides even while a standalone scenario is running on the headset.

### ADR-002: Native C# Procedural DSP Synthesizer (`NativeDspSynthesizer`) with Dual CC Compatibility
- **Context**: `CsoundUnity` in `human_swarm_ims` caused audio dropouts starting at 24 agents on desktop, blocked native Linux builds, and did not support Apple Vision Pro (`visionOS`). Furthermore, an audit of `human_swarm_examples` revealed that `README.md` documented `CC 0` for Waveform and `CC 82/83/85/17` for ADSR, whereas `CsoundBasicSynthesizer.cs` had inspector defaults `1` and `74/71/72/73`.
- **Decision**: Implement `NativeDspSynthesizer` in pure C# using `OnAudioFilterRead` (PolyBLEP oscillators, ADSR, Biquad LPF, Schroeder reverb) behind `IAgentAudioSynth`, and accept **both** primary and legacy alias CC IDs.
- **Consequences**: Compiles natively via IL2CPP to Android ARM64 (Meta Quest 3), visionOS ARM64 (Apple Vision Pro), Windows, macOS, and Linux, scaling to 100+ spatialized agents with zero GC allocations.

### ADR-003: Multiplatform XR Abstraction (`XRPlatformAdapter` + `XRModeSwitcher`)
- **Context**: SpatialMAS must run not only on Meta Quest 3, but also on **Apple Vision Pro (`visionOS`)**, generic OpenXR headsets (Pico 4 Ultra, Android XR, HTC Vive, PCVR), and Desktop laptops.
- **Decision**: Use standard Unity 6 `XROrigin`, `ARSession` / `ARCameraManager`, `XRHandSubsystem`, and `XRGrabInteractable`, isolating vendor-specific calls (such as `MetaOpenXRDisplaySubsystemExtensions`) behind `#if UNITY_ANDROID && !UNITY_EDITOR && SPATIALMAS_META_OPENXR` inside `XRPlatformAdapter.cs`.
- **Consequences**: Single unified scene (`SpatialMAS_Main.unity`) builds cleanly across all 5 target platforms.

### ADR-004: Embedded `TcpListener` Wi-Fi HTTP Log Server (`WifiLogExportServer`)
- **Context**: Pulling recorded CSV telemetry from a Quest 3 or Apple Vision Pro via USB cables (`adb pull` or Xcode Devices window) is slow and impractical during multi-participant lab studies. Moreover, `System.Net.HttpListener` throws permission errors on Android/visionOS IL2CPP.
- **Decision**: Implement an asynchronous HTTP/1.1 server directly over `System.Net.Sockets.TcpListener` on port `8080`.
- **Consequences**: Researchers can open `http://<headset-ip>:8080/` on any laptop/tablet browser on the same Wi-Fi network and download both `osc_traffic_*.csv` and `xr_embodiment_*.csv` with one click.

---

## 4. Threading & Concurrency Model

| Thread | Component | Synchronization Mechanism |
| :--- | :--- | :--- |
| **UDP Receive Thread** | `OscUdpServer.ReceiveLoop` | Decodes packets and enqueues `OscMessage` structs into a lock-free `ConcurrentQueue<OscMessage>`. |
| **Unity Main Thread** | `OscProtocolRouter.ProcessIncomingMessages` | Drains up to 4,096 messages per `Update()` frame and dispatches to `SimulationCommandBus`. |
| **Unity Audio DSP Thread** | `NativeDspSynthesizer.OnAudioFilterRead` | Reads `volatile` synth parameters (`_targetFrequencyHz`, `_targetGain`, `_filterCutoffHz`, `_adsrStageInt`) with zero locks or heap allocations. |
| **HTTP Export Thread Pool** | `WifiLogExportServer.HandleClientAsync` | Calls `DataRecordingService.Flush()` (protected by `_lock`) and reads CSV files using `FileShare.ReadWrite`. |
