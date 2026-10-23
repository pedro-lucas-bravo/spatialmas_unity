# SpatialMAS — Complete OSC API Specification (v1 `HS-ims` & v2 `SpatialMAS XR`)

SpatialMAS listens on **UDP Port `6011`** (configurable in `SpatialMASConfig` or `spatialmas_config.json`) and supports both single OSC 1.0 messages and recursive **`#bundle`** packets.

It is **100% backward-compatible** with the published ACSOS 2025 `HS-ims` protocol (`human_swarm_examples`) while adding **v2 SpatialMAS XR & Scenario Control** endpoints.

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

### 2.1 v1 `HS-ims` Endpoints

| OSC Address | Arguments & Types | Description |
| :--- | :--- | :--- |
| `/test/alive` | `(string ip, int port, float testVal)` | Registers `(ip, port)` as the remote destination for outgoing OSC feedback. |
| `/agents/instantiate` | `(int shape, int movMode, int count, float scale, string ip, int port)` | Clears all existing agents and spawns `count` agents (`0 .. count-1`). `shape`: `0`=Sphere, `1`=Cube, `2`=Pyramid. `movMode`: `0`=Teleport, `1`=Smooth Velocity. `scale`: base size in meters (multiplied by `WorldScale`). |
| `/agents/add` | `(int shape, int movMode, float scale, string ip, int port, int id1, ...)` | Adds one or more specific agent IDs without clearing existing agents. |
| `/agents/remove` | `()` or `(int id1, int id2, ...)` | Removes all agents (if 0 args) or the specified agent IDs. |
| `/agents/position/id` | `(int id, float x_mm, float y_mm, float z_mm)` | Moves agent `id` to mapped `(x_mm, y_mm, z_mm)`. Ignored while agent `id` is actively grabbed by the user's hand. |
| `/agents/position` | `(float x0, float y0, float z0, float x1, ...)` | Batch updates positions for all ordered agents (`3 * N` floats). |
| `/agents/color/id` | `(int id, float r, float g, float b, float a)` | Sets RGBA color (`0.0–1.0`) of agent `id`. |
| `/agents/color` | `(float r0, float g0, float b0, float a0, ...)` | Batch updates RGBA colors for all ordered agents (`4 * N` floats). |
| `/agents/Interactable` | `(int id, int interactable)` | `1` = Enables XR Hand/Gaze-Pinch & Desktop Mouse grabbing on agent `id`; `0` = Disables. |
| `/agents/localcomm` | `(string type, int senderId, float range_mm, float latency_s, ...extra)` | Spawns an expanding spherical wavefront at `senderId` reaching `range_mm` in `latency_s` seconds. Delivers `/agents/localcomm/id` as each neighbor is reached. |
| `/agents/audio/mute` | `(int muteAll)` or `(int id, int mute)` | Mutes (`1`) or unmutes (`0`) all agents (1 arg) or agent `id` (2 args). |
| `/agents/audio/synth/note` | `(int id, int action, int midiNote, int velocity)` | `action=1`: Note On ($f = 440 \cdot 2^{(m-69)/12}$ Hz), `action=0`: Note Off. |
| `/agents/audio/synth/param_val` | `(int id, int ccId, float value)` | Sets DSP synthesizer parameter `ccId` on agent `id` (see **Section 4**). |
| `/agents/audio/bank` | `(int id, int clipIdx, int action, float gain)` | Plays (`1`) or stops (`0`) soundbank clip `clipIdx` (`0`=Bell, `1`=Click) at `gain` (`0.0–1.0`). |
| `/boundaries/add` | `(int id, int shape, float x_mm, float y_mm, float z_mm, float size_mm)` | Spawns translucent boundary `id` (`0`=Sphere, `1`=Cube) with radius/half-extent `size_mm`. |
| `/boundaries/update` | `(int id, float x_mm, float y_mm, float z_mm, float size_mm)` | Updates position and size of boundary `id`. |
| `/boundaries/remove` | `()` or `(int id)` | Removes all boundaries (0 args) or boundary `id`. |
| `/listener/position` | `(float x_mm, float y_mm, float z_mm)` | Moves `AudioListenerProxy` to `(x_mm, y_mm, z_mm)` when unlocked from XR head (`/xr/listener/lock_to_head 0`). |
| `/listener/rotation` | `(float pitch_deg, float yaw_deg, float roll_deg)` | Rotates `AudioListenerProxy` when unlocked from XR head. |
| `/environment/set_arrow` | `(int id, float x1, float y1, float z1, float x2, float y2, float z2, float w_mm)` | Renders a 3D directional arrow `id` from `(x1,y1,z1)` to `(x2,y2,z2)` with width `w_mm`. |
| `/environment/remove_arrow` | `()` or `(int id)` | Removes all arrows (0 args) or arrow `id`. |

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
| `/agents/position/id` | `(int id, float x_mm, float y_mm, float z_mm)` | Emitted continuously while user grabs and moves agent `id` in XR or Desktop. |
| `/agents/touch/id` | `(int id, int touchState, int otherAgentId)` | `touchState`: `1`=Touch Enter, `0`=Touch Exit. `otherAgentId = -1` if touched by user hand/pointer, or `>= 0` if agent-to-agent collision. |
| `/agents/localcomm/id` | `(string type, int senderId, int receiverId, float dist_mm, ...extra)` | Emitted when an expanding `/agents/localcomm` sphere from `senderId` reaches `receiverId`. |
| `/boundaries/Touched` | `(int agentId, int boundaryId)` | Emitted when `agentId` crosses the surface of `boundaryId`. |
| `/xr/user/pose` *(v2)* | `(float x_mm, float y_mm, float z_mm, float qx, float qy, float qz, float qw)` | Streamed at `25 Hz` with the user's physical head position (in arena `mm`) and rotation quaternion. |
| `/xr/hands/pose` *(v2)* | `(int hand, float x_mm, float y_mm, float z_mm, int isPinching)` | Streamed at `25 Hz` for Left (`0`) and Right (`1`) hands. |

---

## 4. Dual-Compatible Synthesizer CC Parameter Table

[`NativeDspSynthesizer.cs`](../Assets/_SpatialMAS/Runtime/Audio/NativeDspSynthesizer.cs) resolves the historical CC ID mismatch between `human_swarm_examples/README.md` and `CsoundBasicSynthesizer.cs` by accepting **both** CC numbers for every parameter:

| Parameter | Primary `cc_id` | Legacy Alias `cc_id` | Range | Default | Description |
| :--- | :---: | :---: | :--- | :---: | :--- |
| **Waveform** | `1` | `0` | `0`=Sine, `1`=Square, `2`=Saw, `3`=Triangle | `0` | Anti-aliased PolyBLEP oscillator waveform. |
| **Frequency (Hz)** | `2` | — | `20.0 – 16000.0` | `440.0` | Instantaneous pitch in Hz (also wakes continuous drone mode when Gain > 0). |
| **Gain / Volume** | `16` | `7` | `0.0 – 1.0` | `0.0` | Linear amplitude multiplier. |
| **Attack (s)** | `82` | `74` | `0.001 – 10.0` | `0.04` | ADSR envelope attack time in seconds. |
| **Decay (s)** | `83` | `71` | `0.001 – 10.0` | `0.10` | ADSR envelope decay time in seconds. |
| **Sustain** | `85` | `72` | `0.0 – 1.0` | `0.80` | ADSR sustain amplitude ratio. |
| **Release (s)** | `17` | `73` | `0.001 – 10.0` | `0.20` | ADSR envelope release time in seconds. |
| **LPF Cutoff (Hz)** | `93` | — | `20.0 – 20000.0` | `12000.0` | 12 dB/oct Biquad Low-Pass Filter cutoff frequency. |
| **LPF Resonance ($Q$)** | `18` | — | `0.5 – 12.0` | `0.707` | Biquad filter resonance quality factor. |
| **Reverb Mix** | `19` | `91` | `0.0 – 1.0` | `0.12` | Schroeder Comb/Allpass diffuser wet mix. |
