# SpatialMAS — Complete OSC API Specification (v1 `HS-ims` & v2 `SpatialMAS XR`)

SpatialMAS listens on **UDP Port `6011`** (configurable in `SpatialMASConfig` or `spatialmas_config.json`) and supports both single OSC 1.0 messages and recursive **`#bundle`** packets.

It implements the legacy signatures below from the ACSOS 2025 `HS-ims` examples, plus **v2 SpatialMAS XR & Scenario Control** endpoints. Focused regressions cover lifecycle, synth targeting, boundaries, movement, and coordinate changes; this is not a certification of every external application.

---

## 1. Coordinate System & Scale Mapping

### 1.1 Wire Format (`v1` Compatibility)
External scripts send and receive coordinates in **millimeters (`mm`)** with **Y and Z swapped** relative to Unity's left-handed coordinate system:
- **External `X` (`mm`)**: Right ($+X$) / Left ($-X$)
- **External `Y` (`mm`)**: Forward/Depth ($+Z$ in Unity) / Back ($-Z$ in Unity)
- **External `Z` (`mm`)**: Up/Height ($+Y$ in Unity) / Down ($-Y$ in Unity)

### 1.2 Arena Calibration (`WorldScale` & `OriginOffset`)
In [`SpatialCoordinateMapper.cs`](../Assets/_SpatialMAS/Runtime/Core/SpatialCoordinateMapper.cs), incoming millimeter coordinates $(x_{\text{mm}}, y_{\text{mm}}, z_{\text{mm}})$ are mapped to Unity world-space meters $\mathbf{p}_{\text{unity}}$ as:

$$\mathbf{p}_{\text{unity}} = \mathbf{O}_{\text{offset}} + S_{\text{world}} \cdot \begin{bmatrix} x_{\text{mm}} \times 0.001 \\ z_{\text{mm}} \times 0.001 \\ y_{\text{mm}} \times 0.001 \end{bmatrix}$$

And outgoing Unity positions (e.g. when a user grabs an agent in XR) are converted back via the exact inverse:

$$\begin{bmatrix} x_{\text{mm}} \\ y_{\text{mm}} \\ z_{\text{mm}} \end{bmatrix}_{\text{OSC}} = \begin{bmatrix} (\mathbf{p}_{\text{unity}, x} - \mathbf{O}_{x}) / S_{\text{world}} \times 1000 \\ (\mathbf{p}_{\text{unity}, z} - \mathbf{O}_{z}) / S_{\text{world}} \times 1000 \\ (\mathbf{p}_{\text{unity}, y} - \mathbf{O}_{y}) / S_{\text{world}} \times 1000 \end{bmatrix}$$

> **Why this matters for XR Headsets**: Many desktop scripts in `human_swarm_examples` use a boundary radius of `10000 mm` ($10\text{ m}$). With the default XR `WorldScale = 0.20` and `OriginOffset = (0, 1.2, 0.8)`, that $10\text{ m}$ arena automatically fits as a comfortable $2.0\text{ m}$ sphere centered at chest height right in front of the user—while the external Python/Max script still receives the exact `[-10000, +10000]` millimeter coordinates it expects!

---

## 2. Incoming OSC Endpoints (External Controller $\to$ SpatialMAS `:6011`)

### 2.1 v1 `HS-ims` Endpoints (Dual-Signature Compatible)

| OSC Address | Arguments & Types | Description |
| :--- | :--- | :--- |
| `/connect` or `/test/alive` | `(string ip, int port, [float testVal])` | Registers `(ip, port)` as the remote destination for outgoing OSC feedback and replies with `/test/alive/ <ip> <port> "OK"`. |
| `/agents/instantiate` | **`HS-ims`**: `(int count, [int shape=0], [int movMode=0])`<br>**`v2`**: `(int shape, int movMode, int count, float scale, string ip, int port)` | Clears all existing agents and spawns `count` agents (`0 .. count-1`). `shape`: `0`=Sphere, `1`=Cube, `2`=Pyramid. `movMode`: `0`=Teleport, `1`=Follow/Smooth, `2`=Velocity. |
| `/agents/instantiate/id` | `(int shape, int movMode, int id0, int id1, ...)` | Spawns or reconfigures the specified agent IDs with `shape` and `movMode` (used by `swarmalators/main.py` & `attractors_repellers/main.py`). |
| `/agents/add` | **`HS-ims`**: `(int count, [int shape=0], [int movMode=0], [int initPosMode=0], ...)`<br>**`v2`**: `(int shape, int movMode, float scale, string ip, int port, int id0, ...)` | Adds without clearing. Legacy additions allocate unused IDs; `initPosMode=1` uses a random-radius argument in meters, and `2` uses explicit position triples in mm. |
| `/agents/add/id` | `(int shape, int movMode, int id0, int id1, ...)` or the v2 add signature above | Adds or reconfigures the supplied IDs. |
| `/agents/remove` | `()` or `(int count, [int shape=-1], [int movMode=-1])` | Zero arguments removes all. Otherwise removes the last `count` matching agents; `-1` filters mean any shape/movement, not "remove all". |
| `/agents/remove/id` | `(int id0, int id1, ...)` | Removes exactly the supplied IDs. |
| `/agents/position/id` | `(int id0, float x0_mm, float y0_mm, float z0_mm, [int id1, ...])` | Moves one or more agents to mapped `(x_mm, y_mm, z_mm)` (`4 * K` args). Ignored while an agent is actively grabbed by the user. |
| `/agents/position` | `(float x0, float y0, float z0, float x1, ...)` | Batch updates positions for all ordered agents (`3 * N` floats). |
| `/agents/speed/id` | `(int id0, float speed0_mm_s, [int id1, float speed1_mm_s, ...])` | Sets movement speed in `mm/s` for `Follow` (`1`) or `Velocity` (`2`) mode (`2 * K` args). |
| `/agents/direction/id` | `(int id0, float dx0, float dy0, float dz0, ...)` | Sets movement direction vector for `Velocity` (`2`) mode (`4 * K` args). |
| `/agents/velocity/id` | `(int id0, float vx0_mm_s, float vy0_mm_s, float vz0_mm_s, ...)` | Sets velocity vector in `mm/s` for `Velocity` (`2`) mode (`4 * K` args). |
| `/agents/color/id` | **`HS-ims`**: `(int id0, string hex0, [int id1, string hex1, ...])`<br>**`v2`**: `(int id, float r, float g, float b, [float a=1.0])` | Sets agent color via 6-char hex strings (`"00e5ff"`, `"#ff0000"`) in pairs OR float RGBA (`0.0–1.0`). |
| `/agents/color` | `(float r0, float g0, float b0, float a0, ...)` or `(int N, int shape, int action, string hex0, ...)` | Batch updates colors for all ordered agents. |
| `/agents/radius/id` | `(int id0, float radius0_mm, float alpha0, string hex0, ...)` | Displays or hides (`radius=0`) a translucent influence/detection sphere around each agent (`4 * K` args). |
| `/agents/Interactable` | `(int id, int interactable)` | `1` = Enables grabbing through configured XR hand/controller input or the desktop mouse; `0` = Disables. |
| `/agents/localcomm` | **`HS-ims Config`**: `(float maxRange_mm, float latency_ms, int showVisuals)`<br>**`v2 Broadcast`**: `(string type, int senderId, float range_mm, float latency_s, ...extra)` | Configures global localcomm defaults (3 numeric args) OR triggers an expanding spherical wavefront from `senderId` (4+ args). |
| `/agents/localcomm/send` | `(int senderId0, string msg0, [int senderId1, string msg1, ...])` | Broadcasts `msg` from `senderId` using configured localcomm range & latency; delivers `/agents/localcomm/recv` & `/agents/localcomm/id`. |
| `/agents/audio/mute` | `(int muteAll)` or `(int id, int mute)` | Mutes (`1`) or unmutes (`0`) all agents (1 arg) or agent `id` (2 args). |
| `/agents/audio/synth/add` & `/remove` | `()` or `(int count, [int shape=-1], [int movMode=-1])` | Selects the first matching agents. Add unmutes; remove flushes and mutes. Zero arguments selects all. |
| `/agents/audio/synth/add/id` & `/remove/id` | `(int id0, int id1, ...)` | Add/unmute or flush/mute exactly these agents. |
| `/agents/audio/synth/flush` | `()` or `(int agentId, [int bank=0])` | Flushes a single agent's envelope/filter/reverb state; no arguments flushes all. Native DSP provides one synth bank per agent. |
| `/agents/audio/synth/note` | `(int id, int bankOrAction, int midiNote, int velocity)` | `velocity > 0`: Note On ($f = 440 \cdot 2^{(m-69)/12}$ Hz); `velocity == 0`: Note Off. Supports both `HS-ims` (`bank=0`) and `v2` (`action=1|0`). |
| `/agents/audio/synth/param_val` | **`HS-ims`**: `(int id, int bank, int ccId, float value)`<br>**`v2`**: `(int id, int ccId, float value)` | Sets DSP synthesizer parameter `ccId` on agent `id` (see **Section 4**). |
| `/agents/audio/synth/param_midi` | `(int id, [int bank], int ccId, float midiVal_0_127)` | Sets DSP synthesizer parameter `ccId` mapped from MIDI range `0..127`. |
| `/agents/audio/bank` | `(int id, int clipIdx, int action, float gain)` | Plays (`1`) or stops (`0`) soundbank clip `clipIdx` (`0`=Bell, `1`=Click) at `gain` (`0.0–1.0`). |
| `/boundaries/add` | `(int id, int shape, float x_mm, float y_mm, float z_mm, float radiusOrHalfExtent_mm)` | Compact v2 boundary (`0`=Sphere, `1`=Cube). |
| `/boundary/add/id` | Sphere: `(id, 0, x, y, z, radius, [alpha], [hex])`<br>Cube: `(id, 1, x, y, z, fullX, fullY, fullZ, [alpha], [hex])` | Legacy positions/dimensions are mm. Cube extents follow the same wire-axis swap as positions; color/alpha are preserved. |
| `/boundaries/update`, `/boundary/position/id`, `/boundary/scale/id`, `/boundary/alpha/id`, `/boundary/color/id` | `(int id, ...)` | Updates position, size, opacity (`0.0–1.0`), or hex color of boundary `id`. |
| `/boundaries/remove` or `/boundary/remove/id` | `()` or `(int id)` | Removes all boundaries (0 args) or boundary `id`. |
| `/listener/position` or `/audio/listener/position` | `(float x_mm, float y_mm, float z_mm)` | Moves `AudioListenerProxy` to `(x_mm, y_mm, z_mm)` when unlocked from XR head (`/xr/listener/lock_to_head 0`). |
| `/listener/rotation` | `(float pitch_deg, float yaw_deg, float roll_deg)` | Rotates `AudioListenerProxy` when unlocked from XR head. |
| `/arrow` or `/environment/set_arrow` | **`HS-ims`**: `(x, y, z, dx, dy, dz, scale)`<br>**`v2`**: `(id, x1, y1, z1, x2, y2, z2, w_mm)` | Renders a 3D directional arrow in the arena. |
| `/arrow/remove` or `/environment/remove_arrow` | `()` or `(int id)` | Removes all arrows (0 args) or arrow `id`. |
| `/recorder/start` & `/recorder/stop` | `()` | Starts or stops buffered CSV recording (alias for `/logging/state 1|0`). Session tags are available through the C# recorder API. |

### 2.2 v2 `SpatialMAS` XR & Scenario Endpoints

| OSC Address | Arguments & Types | Description |
| :--- | :--- | :--- |
| `/xr/mode` | `(int mode)` | `0` = **MR Passthrough** (transparent camera background, physical room visible); `1` = **VR Immersive Studio** (dark studio backdrop + holographic reference grid). |
| `/xr/scale` | `(float worldScale)` | Sets `SpatialCoordinateMapper.WorldScale` ($\in [0.05, 5.0]$). |
| `/xr/origin` | `(float x_m, float y_m, float z_m)` | Sets `SpatialCoordinateMapper.OriginOffset` in Unity meters. |
| `/xr/listener/lock_to_head` | `(int locked)` | `1` = Locks 3D `AudioListener` to headset camera 6DoF (default); `0` = Detaches for manual `/listener/position` control. |
| `/xr/user/repeller` | `(int enabled, float radius_m, float force)` | Configures the XR participant's physical head/hands as a repulsive social-force field. |
| `/scenario/load` | `(int scenarioId)` | Switches active scenario: `0`=External OSC, `1`=Crowd Avoidance, `2`=3D Swarmalators, `3`=Attractors & Repellers, `4`=Musical Encounters, `5`=Self-Assembly. |
| `/scenario/param` | `(string name, float value)` | Updates a live scenario parameter (e.g. `"J"`, `"K"`, `"speed"`, `"personalSpace"`, `"tempo"`). |
| `/logging/state` | `(int recording)` | `1` = Starts a new CSV recording session; `0` = Flushes and stops recording. |

---

## 3. Outgoing OSC Endpoints (SpatialMAS $\to$ External Controller)

| OSC Address | Arguments Sent | Trigger Condition |
| :--- | :--- | :--- |
| `/test/alive/` | `(string ip, int port, string "OK")` | Sent in reply to `/connect` or `/test/alive` handshake. |
| `/agents/position/id` | `(int id, float x_mm, float y_mm, float z_mm)` | Emitted continuously while user grabs and moves agent `id` in XR or Desktop. |
| `/agents/control` | `(int id, int controlType, int controlValue)` | `HS-ims` click/grab feedback (`controlType`: `0`=Left/Primary, `1`=Right/Secondary; `controlValue`: `1`=Press, `0`=Release). |
| `/agents/touch/id` | `(int id, int touchState, int otherAgentId)` | `touchState`: `1`=Touch Enter, `0`=Touch Exit (`otherAgentId = -1` for user hand/mouse). |
| `/agents/localcomm/recv` | `(int receiverId, int senderId, string msg)` | `HS-ims` localcomm delivery when an expanding wavefront reaches `receiverId`. |
| `/agents/localcomm/id` | `(string type, int senderId, int receiverId, float dist_mm, ...extra)` | `v2` extended localcomm delivery including exact Euclidean distance in `mm`. |
| `/boundary/event/id` | `(int agentId, int boundaryId, int eventType)` | `HS-ims` boundary crossing (`eventType`: `0`=Enter, `1`=Exit). |
| `/boundaries/Touched` | `(int agentId, int boundaryId)` | `v2` boundary crossing notification. |
| `/xr/user/pose` *(v2)* | `(float x_mm, float y_mm, float z_mm, float qx, float qy, float qz, float qw)` | Streamed at `25 Hz` with the user's physical head position (in arena `mm`) and rotation quaternion. |
| `/xr/hands/pose` *(v2)* | `(int hand, float x_mm, float y_mm, float z_mm, int isPinching)` | Streamed at `25 Hz` for Left (`0`) and Right (`1`) hands. |

---

## 4. Dual-Compatible Synthesizer CC Parameter Table

[`NativeDspSynthesizer.cs`](../Assets/_SpatialMAS/Runtime/Audio/NativeDspSynthesizer.cs) resolves the historical CC ID mismatch between `human_swarm_examples/README.md` and `CsoundBasicSynthesizer.cs` by accepting **both** CC numbers for every parameter:

| Parameter | Primary `cc_id` | Legacy Alias `cc_id` | Range | Default | Description |
| :--- | :---: | :---: | :--- | :---: | :--- |
| **Waveform** | `1` | `0` | `0`=Sine, `1`=Square, `2`=Saw, `3`=Triangle | `0` | Anti-aliased PolyBLEP oscillator waveform. |
| **Frequency (Hz)** | `2` | — | `20.0 – 16000.0` | `440.0` | Selects continuous playback. Frequency and gain may arrive in either order; positive gain starts/resumes the tone. Note messages return to note-driven ADSR behavior. |
| **Gain / Volume** | `16` | `7` | `0.0 – 1.0` | `0.0` | Linear amplitude multiplier. |
| **Attack (s)** | `82` | `74` | `0.001 – 10.0` | `0.04` | ADSR envelope attack time in seconds. |
| **Decay (s)** | `83` | `71` | `0.001 – 10.0` | `0.10` | ADSR envelope decay time in seconds. |
| **Sustain** | `85` | `72` | `0.0 – 1.0` | `0.80` | ADSR sustain amplitude ratio. |
| **Release (s)** | `17` | `73` | `0.001 – 10.0` | `0.20` | ADSR envelope release time in seconds. |
| **LPF Cutoff (Hz)** | `93` | — | `20.0 – 20000.0` | `12000.0` | 12 dB/oct Biquad Low-Pass Filter cutoff frequency. |
| **LPF Resonance ($Q$)** | `18` | — | `0.5 – 12.0` | `0.707` | Biquad filter resonance quality factor. |
| **Reverb Mix** | `19` | `91` | `0.0 – 1.0` | `0.12` | Schroeder Comb/Allpass diffuser wet mix. |

Live `/xr/scale` and `/xr/origin` changes rebase existing agents, boundaries, arrows, communication wavefronts, and active scenario state together. Physical head/hand poses stay in room space. An unlocked audio listener follows the arena frame; a head-locked listener follows the physical camera.

`/boundary/scale/id` takes an existing sphere's **radius**, or a cube's three **full dimensions**, in mm. Velocity-mode agents accept explicit position corrections; Follow speed `0` holds position.
