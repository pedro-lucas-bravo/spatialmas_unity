# SpatialMAS — Complete OSC API Specification (Legacy `HS-ims` & Physical Unity World)

SpatialMAS listens on **UDP Port `6011`** (configurable in `SpatialMASConfig` or `spatialmas_config.json`) and supports both single OSC 1.0 messages and recursive **`#bundle`** packets.

It implements the legacy signatures below from the ACSOS 2025 `HS-ims` examples, plus **physical Unity-world, calibration, XR and scenario** endpoints. Focused regressions cover lifecycle, synth targeting, boundaries, movement, units and calibration independence; this is not a certification of every external application.

---

## 1. Physical Coordinates and Explicit Wire Adapters

Runtime poses and recorded measurements use **Unity-world meters**, with X=right, Y=up and Z=forward. Quaternions use Unity's basis and component order **`qx,qy,qz,qw`**. Positions and geometry dimensions are independent: changing an object's size never rescales the participant, its position, its velocity, or its independent influence radius.

`SpatialCoordinateMapper` contains stateless unit/axis conversions only. It applies no configurable world scale, origin translation, reference rotation, or calibration transform.

### 1.1 Canonical `/world/...` Messages

Prefix the agent/boundary/listener/arrow endpoints below with `/world` to use **physical millimeters in Unity XYZ axes**. For example:

$$p_\mathrm{Unity,m}=0.001\,(x_\mathrm{mm},y_\mathrm{mm},z_\mathrm{mm})$$

The inverse is `1000 * p_Unity`. Velocity vectors use `mm/s`; direction vectors are unitless. The prefix chooses an adapter for that message only and never switches a persistent coordinate mode.

Canonical six-argument agent instantiation and explicit-size additions use **full mesh dimensions in mm**, unlike their legacy counterparts, which use meters. For a unit sphere that dimension is its diameter; for a unit cube it is its edge length. The minimum uniform agent dimension is 5 mm. `/world/agents/add` with `initPosMode=1` takes a random radius in **mm** (default `10000`); legacy `/agents/add` takes that radius in **meters** (default `10`). Audio values, time values, IDs, colors and scenario parameter units retain their documented meanings.

### 1.2 Legacy HS-ims Messages

Unprefixed legacy spatial endpoints preserve the wire axes X=right, Y=depth, Z=up, with positions in physical mm:

$$p_\mathrm{Unity,m}=0.001\,(x_\mathrm{legacy},z_\mathrm{legacy},y_\mathrm{legacy})$$

Outgoing legacy agent positions use `1000 * (UnityX,UnityZ,UnityY)`. The historical `/listener/rotation` Euler signature also retains its legacy pitch/roll/yaw mapping; use `/world/listener/rotation` for an explicit Unity quaternion.

**Migration from earlier SpatialMAS snapshots:** the old `0.20` multiplier and `(0,1.2,0.8)` mapping offset no longer exist. A `10000 mm` radius now means **10 physical meters**, never an implicit 2-meter tabletop arena. Newly instantiated external agents start at Unity `(0,0,0)` unless explicitly positioned. Adjust external controller geometry/positions to the physical experiment you intend. Old `/xr/scale` and `/xr/origin` commands return `/error` and leave the world unchanged.

### 1.3 Calibration Reference

The calibration reference is an independent rigid pose in Unity world space with scale `(1,1,1)`. Moving, rotating, hiding or locking it does not move agents, boundaries, the XR rig, the listener, or previously saved measurements. Visibility controls marker graphics/manipulation only. The reference starts locked by default; unlock before setting or dragging its pose.

For the reference pose `(t_ref,q_ref)`, derived reference-relative coordinates are:

```text
p_reference_m = inverse(q_ref) * (p_unity_m - t_ref)
q_reference   = inverse(q_ref) * q_unity
```

These expressions assume matching axis conventions. External units, axes and handedness require an explicit external adapter. The reference provides a recorded alignment frame, not automatic motion-capture registration; its physical counterpart/pose must be established in both systems. Raw world coordinates are retained without applying this derived conversion.

---

## 2. Incoming OSC Endpoints (External Controller $\to$ SpatialMAS `:6011`)

### 2.1 v1 `HS-ims` Endpoints (Dual-Signature Compatible)

| OSC Address | Arguments & Types | Description |
| :--- | :--- | :--- |
| `/connect` or `/test/alive` | `(string ip, int port, [float testVal])` | Registers `(ip, port)` as the remote destination for outgoing OSC feedback and replies with `/test/alive/ <ip> <port> "OK"`. |
| `/agents/instantiate` | **`HS-ims`**: `(int count, [int shape=0], [int movMode=0])`<br>**`v2`**: `(int shape, int movMode, int count, float fullDimension_m, string ip, int port)` | Clears all existing agents and spawns `count` agents (`0 .. count-1`) at Unity world zero. The six-argument legacy size is an actual full dimension in meters; `/world` size is mm. `shape`: `0`=Sphere, `1`=Cube, `2`=Pyramid. `movMode`: `0`=Teleport, `1`=Follow/Smooth, `2`=Velocity. |
| `/agents/instantiate/id` | `(int shape, int movMode, int id0, int id1, ...)` | Spawns or reconfigures the specified agent IDs with `shape` and `movMode` (used by `swarmalators/main.py` & `attractors_repellers/main.py`). |
| `/agents/add` | **`HS-ims`**: `(int count, [int shape=0], [int movMode=0], [int initPosMode=0], ...)`<br>**`v2`**: `(int shape, int movMode, float fullDimension_m, string ip, int port, int id0, ...)` | Adds without clearing. Legacy additions allocate unused IDs; `initPosMode=1` uses a random-radius argument in meters, and `2` uses explicit position triples in mm. |
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

### 2.2 Canonical Physical Object Endpoints

The `/world` aliases retain the endpoint signatures in Section 2.1, with physical Unity XYZ mm vectors/dimensions and the size exceptions made explicit below. Neutral operations such as colors, removal, audio or hand-interaction flags keep their meanings.

| OSC Address | Arguments & Types | Description |
| :--- | :--- | :--- |
| `/world/agents/instantiate` | `(int shape, int movMode, int count, float fullDimension_mm, string ip, int port)` | Creates unit-mesh agents with actual full dimension in physical mm. Count-only legacy forms also retain their default physical sizes. |
| `/world/agents/add` or `/world/agents/add/id` | Explicit-size form: `(shape, movMode, fullDimension_mm, ip, port, id0, ...)` | Adds/reconfigures selected agents without clearing others. Count/init-position forms retain the legacy argument structure, with random radius and explicit positions in physical mm. |
| `/world/agents/position/id` or `/world/agents/position` | Same packing as legacy position endpoints | Unity XYZ physical mm. Position corrections apply immediately for Teleport/Velocity; Follow uses its movement target. |
| `/world/agents/velocity/id` | `(id0,vx0_mm_s,vy0_mm_s,vz0_mm_s,...)` | Unity XYZ physical mm/s. |
| `/world/agents/direction/id` | `(id0,dx0,dy0,dz0,...)` | Unitless Unity XYZ direction. `/world/agents/speed/id` retains scalar mm/s. |
| `/world/agents/rotation/id` | `(id0,qx0,qy0,qz0,qw0,...)` | Sets normalized Unity-world quaternion orientation. Non-finite/zero quaternions return `/error`. User-grabbed agents retain user control. |
| `/world/agents/size/id` | `(id0,fullDimension0_mm,...)` | Uniform full mesh dimension (sphere diameter/cube edge), minimum 5 mm. Changes geometry only; preserves pose, movement and physical influence radius. |
| `/world/agents/radius/id` | `(id0,radius0_mm,alpha0,hex0,...)` | Independent physical influence sphere radius in mm. |
| `/world/boundary/add/id` | Sphere: `(id,0,x,y,z,radius,[alpha],[hex])`<br>Cube: `(id,1,x,y,z,fullX,fullY,fullZ,[alpha],[hex])` | Unity XYZ physical mm, including cube dimensions in XYZ order. `/world/boundaries/add` retains the compact scalar radius/half-extent signature. |
| `/world/boundary/position/id`, `/world/boundaries/update`, `/world/boundary/scale/id` | Same packing as the corresponding legacy endpoint | Unity XYZ physical mm. Sphere scale argument is radius; cube arguments are full dimensions. These resize individual boundary geometry, not the world. |
| `/world/listener/position` | `(x_mm,y_mm,z_mm)` | Unity XYZ physical mm; effective only when the listener is unlocked from the head. |
| `/world/listener/rotation` | `(qx,qy,qz,qw)` | Normalized Unity-world quaternion; effective only when unlocked from the head. |
| `/world/environment/set_arrow` | `(id,x1,y1,z1,x2,y2,z2,width_mm)` | Physical Unity XYZ endpoints and width in mm. |

### 2.3 XR, Calibration and Scenario Endpoints

| OSC Address | Arguments & Types | Description |
| :--- | :--- | :--- |
| `/xr/mode` | `(int mode)` | `0` = **MR Passthrough** (transparent camera background, physical room visible); `1` = **VR Immersive Studio** (dark studio backdrop + holographic reference grid). |
| `/xr/scale` | Removed | Returns `/error`; no geometry, tracking or recording transform changes. |
| `/xr/origin` | Removed | Returns `/error`. Use `/scenario/center` for the next spawn placement or `/calibration/pose` for reference metadata. |
| `/xr/listener/lock_to_head` | `(int locked)` | `1` = Locks 3D `AudioListener` to headset camera 6DoF (default); `0` = Detaches for manual `/listener/position` control. |
| `/xr/user/repeller` | `(int enabled, float radius_m, float force)` | Configures the XR participant's physical head/hands as a repulsive social-force field. |
| `/calibration/pose` | `(x_mm,y_mm,z_mm,qx,qy,qz,qw)` | Independent reference pose in physical Unity XYZ mm and Unity quaternion. Unlock first; finite nonzero quaternions are normalized. |
| `/calibration/visible` | `(int visible)` | `1` shows the marker; `0` hides its graphics and interactive collider. Calibration metadata/conversion remains available. |
| `/calibration/locked` | `(int locked)` | `1` prevents pose changes through OSC/dragging; `0` permits them. Visibility can change while locked. |
| `/calibration/get` | `()` | Requests the current `/calibration/state`, including reference ID, revision and lock state. |
| `/scenario/center` | `(x_mm,y_mm,z_mm)` | Physical Unity XYZ spawn center for the **next scenario load/reset**. Existing objects and the reference are unchanged. |
| `/scenario/layout` | `(int layout)` | `0` Compact; `1` RoomScale. Chooses explicit physical parameters for the **next scenario load/reset**; never rescales an active scene. |
| `/scenario/load` | `(int scenarioId)` | Switches active scenario: `0`=External OSC, `1`=Crowd Avoidance, `2`=3D Swarmalators, `3`=Attractors & Repellers, `4`=Musical Encounters, `5`=Self-Assembly. |
| `/scenario/param` | `(string name, float value)` | Updates a live scenario parameter (e.g. `"J"`, `"K"`, `"speed"`, `"personalSpace"`, `"tempo"`). |
| `/logging/state` | `(int recording)` | `1` = Starts a new CSV recording session; `0` = Flushes and stops recording. |

---

## 3. Outgoing OSC Endpoints (SpatialMAS $\to$ External Controller)

| OSC Address | Arguments Sent | Trigger Condition |
| :--- | :--- | :--- |
| `/test/alive/` | `(string ip, int port, string "OK")` | Sent in reply to `/connect` or `/test/alive` handshake. |
| `/agents/position/id` | `(int id,float x_mm,float y_mm,float z_mm)` | Legacy X/depth/up physical mm while a user moves an agent. Emitted alongside the canonical message below. |
| `/world/agents/position/id` | `(int id,float x_mm,float y_mm,float z_mm)` | Same user-moved pose in physical Unity XYZ mm. Neither message is a continuous autonomous-agent recorder. |
| `/agents/control` | `(int id, int controlType, int controlValue)` | `HS-ims` click/grab feedback (`controlType`: `0`=Left/Primary, `1`=Right/Secondary; `controlValue`: `1`=Press, `0`=Release). |
| `/agents/touch/id` | `(int id, int touchState, int otherAgentId)` | `touchState`: `1`=Touch Enter, `0`=Touch Exit (`otherAgentId = -1` for user hand/mouse). |
| `/agents/localcomm/recv` | `(int receiverId, int senderId, string msg)` | `HS-ims` localcomm delivery when an expanding wavefront reaches `receiverId`. |
| `/agents/localcomm/id` | `(string type, int senderId, int receiverId, float dist_mm, ...extra)` | `v2` extended localcomm delivery including exact Euclidean distance in `mm`. |
| `/boundary/event/id` | `(int agentId, int boundaryId, int eventType)` | `HS-ims` boundary crossing (`eventType`: `0`=Enter, `1`=Exit). |
| `/boundaries/Touched` | `(int agentId, int boundaryId)` | `v2` boundary crossing notification. |
| `/xr/user/pose` | `(x_mm,y_mm,z_mm,qx,qy,qz,qw)` | Targets 25 Hz: head position in physical Unity XYZ mm and Unity-world quaternion in the same basis. Earlier snapshots used a different position frame; update receivers accordingly. |
| `/xr/hands/pose` | `(int hand,x_mm,y_mm,z_mm,int isPinching)` | Targets 25 Hz: Left (`0`) or Right (`1`) hand position in physical Unity XYZ mm. This OSC payload does not include hand rotation or tracking validity; consult the CSV tracking-validity fields before analysing saved hand poses. |
| `/calibration/state` | `(string calibrationId,int revision,x_mm,y_mm,z_mm,qx,qy,qz,qw,int visible,int locked)` | Current reference state on `/calibration/get` or reference state changes. Physical Unity XYZ mm and Unity quaternion; revision also changes for visibility/lock updates. |
| `/error` | `(string incomingAddress,string message)` | Reports removed global transform operations, invalid canonical quaternion/size, or invalid calibration/layout requests. |

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

Calibration changes never rebase existing objects or listener poses. A head-locked listener follows the physical camera; a detached listener retains its explicit Unity-world pose until an explicit listener command changes it. Timestamps, musical timing, personal-space radii and participant tracking are independent of scenario layout selection.

`/boundary/scale/id` takes an existing sphere's **radius**, or a cube's three **full dimensions**, in mm. Velocity-mode agents accept explicit position corrections; Follow speed `0` holds position.
