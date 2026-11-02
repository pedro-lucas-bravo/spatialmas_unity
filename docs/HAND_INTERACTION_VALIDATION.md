# Hand interaction validation

This iteration ports the updated Laser XR `MR_HandsInteraction` reference into an isolated scene and applies its physical-anchor picking approach to SpatialMAS. Hardware acceptance requires observations in both Editor Play through Meta Link and a native Quest build; automated tests alone do not establish interaction feel.

## Scenes

- Isolated reference: `Assets/_SpatialMAS/InteractionLab/Reference/Scenes/MR_HandsInteraction.unity`.
- Production integration: `Assets/_SpatialMAS/Scenes/SpatialMAS_Main.unity`, scenario **2 — Swarmalators**.

The lab is isolated from SpatialMAS networking, scenarios, and recording. Its source scene and imported dependencies retain reference GUIDs. See the lab provenance notes for adaptations and missing-source checks.

For Link, the desktop simulator must remain inactive whenever a hardware XR loader is initialized. Headset presence alone is insufficient: it can be false while Link connects or the headset is unworn. The lab guard runs before the SDK simulator lifecycle, preserves real HMD devices, and checks loader ownership before allowing simulated input. Diagnose missing overlays by checking the running OpenXR hand subsystem and its tracking flags; a running `Hand-Capture-Playback` provider or `XRSimulatedHMD` indicates simulator interference.

## Native test builds

Use the **SpatialMAS > Interaction Lab** menu to create/select the two Unity 6 Android Build Profiles and build the isolated APK or the main-scene Swarmalators test APK. Each profile overrides the global scene list with its respective scene. The helpers use separate application identifiers and restore the original build preferences and active profile afterward. They temporarily select a dedicated short Gradle user home through Unity's Android External Tools API; this avoids a stale user-level `org.gradle.java.home` without editing global Gradle configuration. SDK, NDK, JDK, and Gradle come from the current Unity installation. Meta Camera passthrough requires Android API 32 or newer; project generation preserves that minimum.

The first IL2CPP build can take substantial time compiling the project's installed package shaders and native code. Local reports are under `Logs/PickingIteration`; a command-request timeout alone does not establish a build failure. Check the final report and the actual APK before installing.

On Windows, the scoped Gradle user home is `%USERPROFILE%/.smg` to keep cached native headers below Ninja's 260-character path limit. A home inside the checkout could exceed that limit even when the library filename itself fit. The helper restores the original Gradle home after each build; global Gradle properties and Windows long-path settings remain untouched.

## Acceptance checklist

Repeat each check through Link and in the native Quest app, first in the isolated scene and then with Swarmalators cubes:

1. Passthrough and the translucent hand overlay appear in both eyes. Check fingertips and wrist alignment while still and moving.
2. Reach to the cube surface and pinch; the cube follows wrist translation and rotation closely. Pinching empty space near its surface must not accidentally pick it up.
3. Shift between index and middle pinch while holding. Deliberately open the fingers to release; watch for a sideways release twitch.
4. Point at a distant cube and pinch. The default distance interaction must put the cube at the physical hand immediately, with no visible travel interval or retained ray tether.
5. Move and rotate the retrieved cube, then release. In Swarmalators it must return to scenario control. Repeat after resetting the scenario to exercise pooled agents.
6. Activate the main scene's world menu using its pointing ray; physical grabbing must not break UI input.
7. In the main scene, the object-picking ray appears only while hovering a selectable object whose collider surface is at least 22 cm from the physical grab anchor. It disappears in empty space, at near-contact distances, throughout the near-miss exclusion zone, and immediately when an object is held. Its translucent gradient matches the lab's hover ray. A separate valid menu hit can still show pointing feedback for UI use.

Record four separate outcomes: lab/Link, lab/native, Swarmalators/Link, Swarmalators/native. All four were confirmed by the user in this iteration; see the verification table below.

Link verification on 2026-11-02: the user confirmed visible hands and working interaction in the lab, then correctly aligned ray feedback in both eyes after a Link restart and a live replacement of the legacy line material with URP Particles/Unlit. That material is now shared by the lab and production rig setup. The attempted ray-origin change did not improve alignment and was not retained. The user subsequently confirmed main-scene Link interaction, the target-only ghost ray refinement, and both native apps. A local 180-frame Editor sample averaged approximately 35 ms per frame; this can contribute movement latency, but does not establish the cause of a stationary stereo discrepancy.

## Coordinate and recording boundary

Unity-world XYZ metres and quaternion XYZW remain authoritative. Calibration stays an independent reference. Canonical OSC remains XYZ millimetres; legacy OSC retains its documented axis adapter. No world scale or hand-size offsets are introduced.

Interaction anchor filtering must not replace the physical poses sampled by embodiment recording or OSC telemetry. Object samples continue to record the object's actual world pose, including the immediate position change caused by a distance pickup. Recording schema, sample joins, validity flags, and session identity retain their existing meaning.

The reference's optional release glide is not the default for scenario agents: built-in simulations retain authority after release. Native hand-tracking frequency metadata is a runtime request, not proof of a particular sensor rate or latency.

## Verified outcomes — 2026-11-02

| Scene | Editor through Link | Native Quest |
| --- | --- | --- |
| MR_HandsInteraction | User confirmed | User confirmed |
| Main scene / Swarmalators | User confirmed, including target-only ghost ray | User confirmed |

Both APKs were built with the installed Unity 6000.6.6f1 Android toolchain and real Unity 6 Build Profiles, then installed and launched through ADB. Outputs are `Builds/InteractionLab/SpatialMAS_InteractionLab.apk` (65,661,602 bytes; `com.spatialmas.interactionlab`) and `Builds/InteractionLab/SpatialMAS_Swarmalators.apk` (57,543,786 bytes; `com.spatialmas.pickingtest`). Reports are in `Logs/PickingIteration`.

After switching Gradle homes on this workstation, a stale `.utmp/RelWithDebInfo/6w3u4uz5/arm64-v8a/.ninja_deps` still recorded an obsolete long header path. Removing that generated dependency file after the failed build allowed a fresh dependency scan and a successful main build. Do not delete the global Gradle caches or traverse cache junctions when cleaning local build artifacts.

Validation used the existing Unity Test Framework: the earlier full picking suite passed 53/53; focused checks after the Link fixes passed all three XR setup tests, the generated-rig stereo-material test, and the new distance-ray visibility regression. No additional CI tools or packages were installed.
