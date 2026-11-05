# Researcher panel prototype

This iteration tests panel access and movement while retaining the existing researcher controls. The laptop dashboard, transport, participant panels, and content redesign remain later work. The prototype uses the existing OpenXR/XRI and world-space Unity UI infrastructure; it does not import the Human-Swarm Oculus capsule architecture.

The reference is `helping_sources/human_swarm_ims/Assets/Scripts/MR Behaviour/BraceletMenu.cs` and its MR-Swarmalators scene: left wrist, opposite index touch, one-second hold, proximity 0.1 m, approach angle 95 degrees. SpatialMAS must cancel activation on exit, lost tracking, an incompatible grab, or a mode change, and require release before another activation.

Panel visibility and placement do not issue simulation, pause, recording, calibration, or coordinate commands. The panel remains in Unity world space after placement. Researcher/participant mode is a local visibility policy, not authentication. Participants receive no researcher launcher or configuration controls.

## Prototype controls

### Joined tabs, drag distance and palette

Run, Setup and Session join the top edge of one inset surface. The active tab shares the inset color and keeps its cyan marker. Inactive tabs are darker. A separate outline marks hover. Status, Close, Follow/Pin and Recenter sit outside the inset.

Pinch and hold the title or blank space. Sweep sideways to orbit, tilt the pointing ray up or down to adjust height, and push your hand away from your head or draw it back to adjust distance. The depth axis is the horizontal direction from your head to the panel at pinch start. Shared head and hand translation cancels out. The first 3 cm of depth travel is a dead zone, and further travel changes distance meter for meter. The bounds default to 0.65–2.25 m. Follow retains the released distance, Pin holds the released pose, and Recenter restores the configured default of 1.25 m.

Edit **Researcher Panel** on the bootstrapper's referenced `SpatialMASConfig`. The distance fields are `researcherPanelMinDistance`, `researcherPanelRadius` (default), `researcherPanelMaxDistance`, and `researcherPanelDepthDragDeadZone`, all in meters. The original radius field keeps its serialized name and authored value. Invalid ordering is clamped, and distance edits during a press or drag apply after release.

Expand **Researcher Panel Palette** for these color roles. Colors are RGBA and the button state colors multiply the base button color.

| Roles | Default colors |
| --- | --- |
| `shell`, `inset` and joined tab, `inactiveTab`, `button` | `#141C26FA`, `#1E2A38FF`, `#19232FFF`, `#243242FF` |
| `buttonHoverTint`, `buttonPressedTint`, `buttonSelectedTint`, `buttonDisabledTint` | `#F5F5F5FF`, `#C8C8C8FF`, `#F5F5F5FF`, `#C8C8C880` |
| `primaryText`, `secondaryText` | `#F4F6F8FF`, `#B8C4D0FF` |
| `accent`, `tabMarker`, `selectionMarker`, `hoverOutline` | `#68D9E6FF` each |
| `running`, `paused`, `pending`, `recording`, `notRecording`, `confirmation` | `#F4F6F8FF`, `#FFC66DFF`, `#FFC66DFF`, `#FB6E76FF`, `#B8C4D0FF`, `#FFC66DFF` |
| `dragOutline`, `panelRay` tint, `enabledEndpoint`, `disabledEndpoint` | `#68D9E69B`, `#FFFFFFFF`, `#F4F6F8FF`, `#B8C4D0FF` |

The move endpoint color is **Move Marker** in the palette and defaults to `#68F0FFFF`. Its size and stroke have separate fields under **Researcher Panel**. The panel ray tint multiplies the existing gradient and its alpha only on panel hits. Object ghost rays and the dorsal launcher are separate visuals. Edit the palette during Editor Play to compare colors in Link, then save the config asset to retain the changes for builds.

The user tested and accepted the joined tabs, near/far movement and live palette in Meta Link on 2026-11-05. Native validation remains deferred.

### Three-tab researcher layout

Run opens by default with scenario selection, named presets, Pause or Resume, and a separate Reset. Setup groups next-load layout and spawn placement separately from current calibration and display controls. Clear All is a secondary action with a confirmation step. Session contains recording controls, available session details, the log download address, and expandable Diagnostics.

The header shows active scenario, run state, recording state, and Close. Recenter, Follow/Pin and Participant Mode are shared across tabs. Aim anywhere on the visible panel to see the hand ray. Blank space and the plain title show a cyan four-direction move marker. Pinch there to capture the existing arc drag, with a subtle outline while held. Controls retain separate targets. Enabled controls highlight, and disabled controls show a neutral marker without starting a drag. A first-open hint explains the gesture.

The user accepted the corrected TMP comparison in Link and chose TMP for the full panel. World-space labels use Liberation Sans SDF and a shared material with the SpatialMAS URP stereo UI pass. Diagnostics retains live device, FPS, OSC and agent information. Check label fit and both-eye readability across Run, Setup and Session in Link.

Tune the four-arrow endpoint in the referenced `SpatialMASConfig` asset under **Researcher Panel**. **Move Marker** in the palette defaults to bright cyan, **Researcher Panel Move Marker Size** is 0.032 m tip to tip, and **Stroke** is 0.0026 m. Changes show on the next visible marker update during Editor Play. Save the asset to keep your values.

### Independent follow channels and live Editor tuning (current iteration)

Recentering correction: released sideways placement is a world azimuth, not a permanent angle added to future head yaw. The panel stays at that azimuth after release until the head turns beyond the configured angular dead zone relative to its release heading. It then resumes ordinary front-follow without the old drag bias. Looking toward a 90° side placement therefore does not move its target another 90° away. The normal angular dead zone still allows its configured small residual; pin mode remains world-fixed.

On 2026-11-04 the user confirmed “Recentering works correctly” through Meta Link. All ten follow regressions passed for this correction. Native validation remains deferred.

The config asset's **Researcher Panel** section now has three independent channels:

| Channel | Start threshold | Inner settle threshold | Catch-up time |
| --- | --- | --- | --- |
| Horizontal head position (all XZ directions) | 0.10 m | 0.05 m | 0.25 s |
| Vertical head position (crouch/stand; not pitch) | 0.08 m | 0.04 m | 0.25 s |
| Head yaw | 8° | continuous angular boundary | 0.25 s |

The fixed panel radius remains 1.25 m. Horizontal yaw orbits the smoothed follow center, not instantaneous head position, so turning cannot bypass the positional dead zone. A position channel starts after its outer threshold is exceeded, then smoothly reduces residual error toward its smaller inner threshold. The active latch settles there rather than repeatedly crossing the outer boundary. Separate catch-up time constants control response speed; larger means slower, not a fixed delay or a guaranteed arrival time. Vertical placement now stores a preferred head-height offset, allowing large crouch/stand movements to follow while small motion remains stable. This supersedes earlier world-height-only following.

Hover alone never stops follow. Deliberate panel presses freeze all automatic channels and pose; captured dragging owns deliberate arc movement. On release the arc center and preferred height offset synchronize to the actual placement, avoiding snapback. Pin preserves world pose. Tracking loss preserves pose and suspends follow. The new zone behavior still needs Link acceptance.

In the Editor, select the actual config asset referenced by `SpatialMASBootstrapper` (normally `Assets/_SpatialMAS/SpatialMASConfig.asset`), not the runtime component. Edit the same **Researcher Panel** values either outside Play or during Play. An allocation-free comparison in `SpatialMASBootstrapper.Update`, compiled only under `UNITY_EDITOR`, forwards only changed sanitized values without rebuilding the HUD. Changes made during an active panel press/drag queue until release; radius then eases to its new target. Mode, placement, scenario, recording, and calibration are not reset. NaN/infinite values fall back safely, times are clamped positive, and inner thresholds cannot exceed outer thresholds.

ScriptableObject asset edits remain after Play; save the edited asset/project explicitly to persist them to disk. The implementation does not save assets or copy runtime Inspector values back. Player builds read the saved startup config and contain no live-config polling. The existing serialized `researcherPanelCatchUpSeconds` field is retained as **Angular Catch Up Seconds**, preserving authored values; new horizontal/vertical catch-up fields default to 0.25 s. The Resources fallback config exposes the same controls.

Verification: nine `PanelFollowTests`, eight `WorldPanelInteractionTests` (including live settings/state preservation and capture deferral), and five `PanelDragCaptureTests` passed: 22 focused PlayMode checks. Source preprocessing check confirms the player path retains only startup application, with per-frame polling excluded by `UNITY_EDITOR`. No native build was started. Link acceptance of the independent zones/live-tuning feel remains pending.

### Fixed-radius arc iteration (supersedes planar lateral placement below)

Latest user feedback accepted the arc/walking behavior but rejected accidental hover stopping follow. Hover alone now does not freeze follow: only a deliberate panel press or captured drag pauses auto-yaw. Horizontal walking behavior remains as described below. This supersedes the earlier hover-freeze comparison. A proposed distance dead zone is still a design discussion and has not been implemented.

Editor tuning: select the bootstrapper-referenced `SpatialMASConfig` asset, then **Researcher Panel**. **Researcher Panel Radius** defaults to 1.25 m, **Researcher Panel Angular Dead Zone** to 8°, and **Researcher Panel Catch Up Seconds** to 0.25 s. Edit these outside Play Mode and restart Play to apply through the bootstrapper. The fallback Resources config exposes the same fields. Catch-up is an exponential smoothing time constant, not a fixed waiting period; yaw has two smoothed stages, so its total visible response is not a single exact time-to-arrival. Larger catch-up values feel slower. The dead zone tolerates angular error before catch-up responds, with a continuous boundary.

The user clarified that excessive lateral distance happened mainly while dragging and approved a fixed-radius arc comparison. Configure **Researcher Panel → Researcher Panel Radius** on the `SpatialMASConfig` asset referenced by the main bootstrapper (normally `Assets/_SpatialMAS/SpatialMASConfig.asset`). Default is 1.25 m; `Assets/_SpatialMAS/Resources/DefaultSpatialMASConfig.asset` provides the fallback. The serialized config supplies the built value through the bootstrapper; drag does not change it. This is a panel preference, not world scale.

Lateral drag changes azimuth around the tracked head; vertical drag changes the panel's stable world height. Persistent pointer capture remains. Follow user tracks horizontal walking and retains the released azimuth/height; Pin in room keeps the released world pose. Recall uses the configured radius and retains the selected mode. Arc/follow behavior is awaiting Link acceptance; earlier acceptance applies to the previous movement mechanics. Native tests remain deferred.

Mapping uses the change in captured pointer-ray azimuth (unwrapped across ±180°) and elevation slope relative to the initial press, preserving initial placement/height without requiring continued panel hits. Near-vertical ray directions pause movement, retaining capture; slope is bounded to avoid pole blowups. Current horizontal head position supplies the arc center while dragging. Follow yaw is suspended then; the move handle alone updates pose. With ordinary UI hover/press, yaw and orientation freeze but head-horizontal translation still carries the panel, preventing it being stranded during walking. Pin mode never carries the panel after release. An off-radius pinned pose eases onto the configured radius when dragged/unpinned, rather than teleporting on press; ordinary lateral dragging does not grow radius.

Validation for this iteration: all six `PanelFollowTests` and all five `PanelDragCaptureTests` passed, including configured radius during turns/walking, pinning, large radial drag with vertical adjustment, release persistence, and tracking-loss cancellation. New arc/follow headset acceptance is pending. An initial stalled runner session was cancelled; only completed test results are counted.

### Horizontal follow comparison

Default mode is Follow user. It uses tracked head yaw projected onto world horizontal space and follows horizontal walking, with an 8° angular dead zone and 0.25-second exponential catch-up. The dead-zone boundary response is continuous. Pitch/roll do not move the panel vertically; its chosen world height stays fixed. Near vertical gaze (horizontal forward magnitude below 0.1) retains the last valid heading. The canvas stays upright with its front toward the user.

Pin in room freezes position and orientation. Its button then reads Follow user; unpin derives offsets from the current panel pose. Moving the panel retains the accepted plane drag: release remembers horizontal distance/lateral offset relative to current head yaw and absolute world height, rather than snapping to an old target. Recall resets the comfortable offset to 1.25 m ahead and 0.1 m below tracked head height while retaining pinned/follow mode. Launcher toggling is unchanged.

Follow freezes both position and orientation while an active pointer aims at this panel UI, while a press begun there remains held, and throughout captured movement. Hover is checked against the current ray and panel bounds, with inactive sources ignored; it does not depend on a permanent enter flag. Release/aim exit resumes smoothing. Hidden panels do not follow. Tracking loss preserves the last pose; reacquisition no longer forces automatic recall and follow resumes only outside interaction. This new behavior awaits Link acceptance; native tests remain deferred.

Automated follow checks: all five `PanelFollowTests` passed (yaw/pitch/roll/near-vertical heading, continuous dead zone/smoothing, freeze/tracking-loss/resume, released-placement offsets and walking, pin/unpin/recall). All seven `WorldPanelInteractionTests` also passed after integration. These deterministic tests establish mechanics; actual pointer-hover freeze and comfort still need the Link test.

- Researcher mode starts enabled for setup. Bring the right index fingertip within 10 cm of the raised button on the back of the left hand, then hold at its outward top surface for one second. Visibility uses proximity and a hand-relative palm-side rejection plane, allowing side/oblique approaches in any orientation. Activation checks the bounded contact surface. The bar fills during the hold. Completion closes an open panel, or recalls a hidden panel ahead of the tracked head. Exit cancels progress. A completed or cancelled activation must be released before rearming. This toggle behavior is the user's explicit design decision.
- Close Panel hides the panel; repeat the wrist hold to recall it. The bracelet remains available in researcher mode even if the old panel is out of sight.
- Hold/pinch and drag the bottom Move Handle to translate the panel in its original plane. Press captures that pointer until release/cancellation; leaving the rectangle must not require catching the handle again. Release leaves it anchored. This initial handle does not change depth or rotation. Move Panel Here recalls ahead of the head.
- Participant Mode hides the panel and bracelet. F8 recovers researcher mode and recalls the panel locally. Native controller fallback: hold the left controller menu button for two seconds, then release. Availability of that button through the Meta runtime needs headset validation; the prototype does not implement bare-hand recovery from participant mode.
- Newly revealed controls wait for wrist contact, mouse, UI selection and grab presses to release and a neutral frame to pass. Hidden panels are inactive, cannot raycast, and discard queued panel actions. A mode change or tracking/eligibility loss cancels wrist progress. Active grabs block bracelet activation.

The initial UI retains the existing world-space Unity UI and stereo material. Choosing a replacement UI stack and reorganizing content remains a design decision after this mechanics test. Local implementation components are `XRResearcherPanelLifecycle`, `XRBraceletLauncher`, and `XRPanelMoveHandle`; `SpatialResearcherHUD` supplies content and recall placement.

## Validation record

### Persistent movement capture

On 2026-11-04 the user confirmed “Movement now works correctly” through Meta Link after testing fast motion outside the handle while held and anchoring on release. This accepts the current plane-translation movement model through Link; native verification remains deferred.

User accepted abstract UI plane translation but reported losing the move region during fast motion. The previous handler rejected intersections beyond the finite visual ray and cancelled on a miss. XRI also skips drag events when UI screen coordinates stop changing. The correction captures on the initial press, keeps the initial plane and offset fixed, and polls the captured XR input model independently of hover. Its aiming ray projects onto the plane without using visual ray length as a limit. Parallel/behind-plane rays pause translation without ending capture. Release, source disable/unregistration, panel hide or mode change cancel it; another pointer cannot take over. A temporary selection filter prevents the captured hand from grabbing scenario objects during the UI drag. No physical grab handle, depth translation or free rotation is introduced. Link acceptance of this correction remains pending; native testing is deferred by user preference.

### Back-of-hand correction requested after the first Link trial

The user reported wristwatch interference, a flat launcher instead of a volume, an underside label, and a poorly placed contact region. The reference scene's interactive cube (`1723736300`) is 30 × 10 × 30 mm. Its BoxCollider is a trigger; its local size `(1,4,1)` and center `(0,-1.5,0)` extend contact beyond the mesh. The original script uses the button's `-up` face as the approach normal. SpatialMAS will preserve the raised-button interaction intent rather than copy the legacy collider's underside extension or Oculus capsule dependencies. Back-of-hand clearance and label readability require renewed headset observations.

The correction derives a left-hand frame from middle metacarpal-to-knuckle direction and little-to-index knuckle span. The anchor is 60% along the middle metacarpal, with the top surface 22 mm outward from the joint plane; no wrist pose offset is used. A 42 × 34 × 8 mm volume sits below that surface, with label front facing outward and label top toward the fingers. The analytic contact slab shares its footprint and extends only 2 mm below the top to 12 mm above it. It has no physics collider that could intercept object rays. These are initial tolerances, not a guarantee of watch clearance across hand sizes. The body changes color on contact, and the progress bar fills during the hold.

Follow-up contact refinement: the tracked IndexTip is a joint center, not skin. Contact adds a 5 mm pad allowance around the footprint and below the top, plus 3 mm exit hysteresis once touching. It still rejects the palm side and does not bridge tracking loss. Reveal rejects points more than 16 mm behind the raised top (6 mm outward from the default joint plane), without an angular cone. Busy cancellation now checks selected XRI objects rather than any raw pinch/curl classification, matching the reference's occupied-hand test. These tolerances need Link/native user validation, particularly edge contact and fingertip occlusion.

On 2026-11-04 the user confirmed the final Link refinement: palm-side rejection, side/angled approaches, steady hold/edge feedback and reliable open/close all work. The five geometry/contact PlayMode tests passed; the toggle regression passed separately. Native Quest verification of this prototype remains pending. Participant-mode recovery and panel movement need separate explicit acceptance; the launcher confirmation does not establish those flows.

Unity 6000.6.8f1 compiled the prototype. All six `WorldPanelInteractionTests` PlayMode checks passed: existing stereo/raycast repair, tracked placement anchoring, actual tracked press/release scenario action, hold cancellation/rearm, participant-mode queued-command cancellation/recovery, and close/recall policy. Meta Link and native Quest acceptance require a person wearing the headset; neither is established by compilation or automated tests. Hardware validation is pending.

Test access, hold progress and cancellation, opening with the finger still touching, movement/release, Close and recall from elsewhere, participant mode and independent recovery. Repeat while holding a Swarmalators cube and during tracking loss. Verify that hidden controls cannot receive a delayed press, accepted cube picking and rays still work, and scenario/pause/recording/calibration state persists through panel operations.
