# 🗺️ 3OS_Unity: Implementation Roadmap (Paired with 3OS_Core)

This repo is the **Quest / Editor verification host** and eventual UPM SDK for **[3OS](https://github.com/scifiuiguy/3OS)**. Core owns voxel math, proxies, kinematics, and enclosure policy. Unity owns OpenXR session (via Unity’s plugin), hand/controller telemetry, P/Invoke, rendering, and on-device feel checks.

Phases share version numbers with core. Walk them **in lockstep**: finish the core milestone’s headless/ABI work, then the matching Unity milestone’s Quest gate, then advance both.

```text
Core 0.0 ──► Core 0.1 (small ABI freeze) ──► Core 0.2 … ──► Core 1.0 (native OpenXR parity)
                │                              │
                ▼                              ▼
         Unity 0.1 (thin harness) ──► Unity 0.2 … ──► Unity 1.0 (UPM product polish)
```

**Rule of thumb:** Core proves correctness with C++ tests. Unity proves presence on **Quest 2** (and Editor Link when useful). Do not wait for core Phase 1.0 to start Unity — the thin harness begins at **0.1**.

---

## Milestone matrix

| Ver | Core delivers | Unity delivers | Quest 2 gate |
| --- | --- | --- | --- |
| **0.0** | CMake, `3os_kernel`, hello export | UPM skeleton, OpenXR project deps, empty demo scene | *(optional)* empty scene runs on device |
| **0.1** | Types + **frozen small ABI** + stub facade | Bridge + `InteropStructs` + input router + plugin load | Plugin loads; version/ping; frames marshal @ framerate |
| **0.2** | Topology, Half-Dome, double-proxy, portals | Prefab visuals + apply remaps/teleports to scene objects | Dome → lattice → portal drag teleports a cube |
| **0.3** | Velocity telekinesis + floor clamp | `ThreeOSMovable` applies kernel deltas | Wrist micro-glide + coast; floor holds |
| **0.4** | Glyph registry, Box insert + FS events, host text | Demo dir glyphs/labels; snap-insert anim; disk move | `simple.glb` + `Test1` Box; drop-in writes to disk |
| **0.5** | Layouts, anchors, selection frustum | Gestures/UI to toggle modes + frustum viz | Matrix/cluster; multi-select; ego/allo switch |
| **0.6** | Full enclosure planes + AI command ABI | `UnitySemanticMask` + command hook from host | Wall clip looks right; one voice/command action |
| **1.0** | Native OpenXR path in core (parity) | UPM polish, samples, docs, CI binaries | Full demo scene acceptance (same checklist as core 1.0) |

---

### ⚙️ Phase 0.0: Package & Quest Project Scaffold
**Pairs with:** Core 0.0  
**Goal:** Installable package shell and a Quest-capable sample project — no native calls required yet.

- [x] **Task U0.1: UPM skeleton**
  - [x] Real `package.json` (`com.scifiuiguy.threeos.unity`), asmdef, folder layout from README.
  - [x] Fix README Git URL / manifest install line (placeholders today).
  - [x] MIT `LICENSE`, `.gitignore` (Library, Temp, builds, large binaries policy).
- [x] **Task U0.2: Sample project wiring**
  - [x] Unity 6 sample host at sibling `../3OS_Unity_Sample_Project` (created via Editor/CLI only) with OpenXR + XR Interaction / XR Hands as needed for Quest 2.
  - [x] Empty `Samples~/Demo/3OS_DemoScene.unity` (+ host `Assets/Scenes/3OS_DemoScene.unity`) for Quest smoke test.
- [x] **Task U0.3: Native binary pipeline (stubs)**
  - [x] `Native~/` documented path pointing at `scifiuiguy/3OS` (submodule optional later).
  - [x] Document how Windows `3os_kernel.dll` and Android `lib3os_kernel.so` land in `Plugins/` (`docs/NATIVE_BINARIES.md`).

**Quest gate:** Demo scene launches on Quest 2 (black room / XR rig OK) — **passed** (head tracking / immersive XR).

---

### 🧱 Phase 0.1: Thin Harness on Frozen ABI
**Pairs with:** Core 0.1 (**blocks Unity 0.1** — ABI must exist first)  
**Goal:** Verification channel. Not cinematic — prove the pipe.

- [x] **Task U1.1: Blittable C# mirrors**
  - [x] `InteropStructs.cs` matches core layouts; assert sizes in Editor where practical.
  - [x] Document units/handedness parity with core interop docs.
- [x] **Task U1.2: `ThreeOSBridge`**
  - [x] Load `3os_kernel` (Editor Windows DLL + Quest Android SO).
  - [x] Lifecycle: init / tick / shutdown; `threeos_version` (or equivalent) ping.
- [x] **Task U1.3: `ThreeOSInputRouter`**
  - [x] Map XR Hands / controllers → `InteropInputFrame` each frame.
  - [x] Fail soft if tracking lost; no native crashes on null poses.
- [x] **Task U1.4: Debug HUD**
  - [x] On-screen: plugin loaded, ABI version, frame counter, last error.

**Quest gate:** App runs on Quest 2; HUD shows successful ping and continuous frame marshal for ≥30s — **passed** (`Plugin loaded: true`, ticks climbing on device).

---

### 🕸 Phase 0.2: Proxy & Portal Visualization
**Pairs with:** Core 0.2  
**Goal:** See and drive topology on device.

- [x] **Task U2.1: Prefabs**
  - [x] Runtime `ThreeOSWorldProxyDome` / `ThreeOSDoubleProxyLattice` visuals driven by kernel state (procedural stand-ins for prefabs; not fake local-only logic).
- [x] **Task U2.2: Scene sync**
  - [x] Apply proxy open/close and coordinate remaps from native topology APIs to Unity transforms/meshes.
- [x] **Task U2.3: Portal interactions**
  - [x] Near ↔ far and cross-proxy drag update the correct far-field GameObject pose (`ThreeOSTopologyController` + `ThreeOSMovable`).

**Quest gate:** Menu opens Half-Dome → Select opens Double-Proxy lattices → Grab/release portal-drags the magenta test cube far ↔ near / cross-proxy — **pending device run**.

---

### ⚡ Phase 0.3: Telekinesis on Device
**Pairs with:** Core 0.3  
**Goal:** Feel rate-control and floor clamp.

- [x] **Task U3.1: `ThreeOSMovable`**
  - [x] Component registers targets with kernel; applies velocity/transform deltas from native.
- [x] **Task U3.2: Possession UX**
  - [x] Hover/select via proxy cursor; possess → wrist micro-motion glides; release coasts.
- [x] **Task U3.3: Floor debug**
  - [x] Align Unity floor plane with core floor height; confirm clamp (no underground objects).

**Quest gate:** Possess object, glide across room with small wrist motion, release with coast; object rests on floor — **pending device run**.

---

### 📂 Phase 0.4: Glyphs & Host Text
**Pairs with:** Core 0.4  
**Goal:** Unity loads a minimal Quest test directory, draws typed glyphs + labels, supports drag-snap insert into a Box with open/closed anim, and write-through FS moves via kernel events.

**Host notes (see core 0.4 for package design)**  
- `.3glyph` = GLB bytes + embedded `THREEOS_glyph` association metadata (single file).  
- **Schema + pack JSON + injector live in core:** `docs/THREEOS_glyph.md`, `docs/glyph_packs/`, `tools/inject_3glyph.py`.  
- Unity imports/renders meshes; kernel owns extension → glyph_id, snap/insert, and mutation events.  
- Authored packs (Blender): glTF-logo glyph for `.glb`/`.gltf` **content**; `box` + `box_open` glyphs for Boxes.  
- **Glyphs render semi-transparent** (alpha materials / URP Transparent). Not opaque blocks — see-through icons so the field stays readable behind them.

**Quest demo directory (operator-staged before launch)**  
Document a fixed on-device path (e.g. app-accessible folder under Android scoped storage / known Quest path). Operator places **only**:

1. `simple.glb` — content payload (a real GLB file the glyph *represents*, not the glyph asset).  
2. `Test1/` — empty folder.

**Expected first paint**

- `simple.glb` → Object glyph (glTF pack) with label **`simple.glb`** below.  
- Beside it → Box glyph (closed `box`) with primary label **`Test1`** and secondary **`0 objects`**.

**Drop-into-Box UX (host viz + anim; kernel snap/commit)**

- Drag Object near Box within threshold → glyph snaps just above Box; show ↓ arrow icon under the name label (insert pending).  
- **On snap:** kernel zeros Object velocity and re-anchors stick `start_hand` to the **current** hand pose (grip stays down — not a release). Pre-snap stick must not keep driving the Object. Same re-anchor on unsnap.  
- Leave threshold → unsnap; hide arrow.  
- Release while snapped → play sequence: swap Box mesh to `box_open` → scale bump **+10%** then return → swap back to closed `box`; Object glyph leaves the field; Box secondary label → **`1 object`**.  
- Handle kernel `ObjectMovedIntoBox` (or equivalent): move `simple.glb` into `Test1/` on disk; report result so core can commit or roll back.

- [ ] **Task U4.1: Demo directory wiring**
  - [ ] Document agreed Quest test path; Editor fallback path for desk testing.
  - [ ] On start, list that directory into the host ABI (expect `simple.glb` + `Test1` when staged correctly).
  - [ ] Soft-fail HUD if listing empty/missing (prompt to stage files).
- [ ] **Task U4.2: Glyph pack install**
  - [ ] Ship sample packs under Samples/StreamingAssets: glTF-logo `.3glyph`, `box.3glyph`, `box_open.3glyph`; register with kernel at startup.
  - [ ] Editor helper: `.glb` → inject `THREEOS_glyph` → `.3glyph` (optional).
- [ ] **Task U4.3: Glyph + label instantiation**
  - [ ] Spawn Object/Box visuals from kernel descriptors (`.3glyph` GLB load path; treat `.3glyph` as GLB).
  - [ ] Force / preserve **semi-transparent** materials on glyph instances (do not upgrade imported GLB to opaque).
  - [ ] World-space labels: Object name; Box name + smaller child-count line (labels stay opaque/readable).
  - [ ] Layout: Object and Box side-by-side in the near field for the two-entry demo.
- [ ] **Task U4.4: Drag, snap affordance, insert anim**
  - [ ] Drive Object drag via existing possess/telekinesis or a 0.4 grab path; feed poses so kernel can evaluate Box proximity.
  - [ ] When kernel reports insert-pending: snap visual to hold pose; show arrow under Object label (expect vel cleared / stick re-anchored in kernel — no host-side gain hacks).
  - [ ] On commit event: play Box open → +10% bump → closed sequence; despawn/reparent Object glyph; refresh count label.
- [ ] **Task U4.5: FS event sink**
  - [ ] Subscribe to storage mutation events; perform Android/Editor file move into `Test1/`; ack success/failure to kernel.
  - [ ] After success, re-list or trust kernel state so a relaunch shows `simple.glb` inside `Test1` (empty root aside from the Box).
- [ ] **Task U4.6: Text injection**
  - [ ] Unity keyboard / TMP / debug field → core text router → rename label updates.
- [ ] **Task U4.7: Association / demo smoke**
  - [ ] Debug readout: glyph_id, snap state, child count; confirm content `.glb` uses glTF pack (not Box packs).

**Quest gate:** Staged dir shows `simple.glb` glyph + `Test1` Box (`0 objects`); drag-snap shows arrow; release inserts with open/bump/close anim; file lands in `Test1/` on disk; Box shows `1 object`.

---

### 🗂️ Phase 0.5: Layouts, Anchors & Frustum UX
**Pairs with:** Core 0.5  
**Goal:** Expose workspace modes with Quest-friendly controls.

- [ ] **Task U5.1: Mode toggles**
  - [ ] Matrix / Cluster / Real Anchor controls (debug panel or gestures).
- [ ] **Task U5.2: Anchor modes**
  - [ ] Ego / allo / object-locked parenting behaves correctly under XR rig motion.
- [ ] **Task U5.3: Selection frustum viz**
  - [ ] Render frustum; multi-select group moves rigidly under telekinesis/portals.

**Quest gate:** Toggle layout; switch ego↔allo; multi-select via frustum and move as a group.

---

### 🧱 Phase 0.6: Semantic Mask & Command Hook
**Pairs with:** Core 0.6  
**Goal:** Engine-side wall clip + typed commands from Unity (stand-in for OEM AI).

- [ ] **Task U6.1: `UnitySemanticMask.shader`**
  - [ ] Consume plane equations from core; crisp wall/ceiling cut in URP.
- [ ] **Task U6.2: Command bridge**
  - [ ] Debug string/enum → core AI/command ABI (e.g. “matrix layout”).
- [ ] **Task U6.3: Optional voice**
  - [ ] Wire platform speech-to-text *in Unity* to the same command entry (transport stays in host).

**Quest gate:** Object masked at a wall plane; one command changes layout or opens a proxy.

---

### 🚀 Phase 1.0: UPM Product + Parity with Core Native OpenXR
**Pairs with:** Core 1.0  
**Goal:** Ship-quality package; Quest demo matches the core acceptance checklist. Core’s standalone OpenXR path is **parity**, not a prerequisite for this demo.

- [ ] **Task U7.1: Packaging**
  - [ ] Install-from-git works; samples import cleanly; CI builds/copies Windows + Android natives.
- [ ] **Task U7.2: Demo scene completion**
  - [ ] Prefabs, bridge, documented Quest 2 setup in README quickstart.
- [ ] **Task U7.3: Acceptance checklist (Quest 2)**
  - [ ] Half-Dome macro-target on empty far field.
  - [ ] Double-proxy micro-resolve + CREATE in empty voxels.
  - [ ] Portal teleport near ↔ far and cross-proxy.
  - [ ] Velocity glide + friction; floor clamp holds.
  - [ ] Wall masking correct.
  - [ ] Storage glyphs + rename via host text.
  - [ ] Layout toggle + one anchor-mode switch in-session.

---

### 📝 Post-1.0: Parking lot (ideas without tasks yet)

Use this section for design notes, UX instincts, and “we’ll need this later” observations that are **not** ready to become checklist tasks. No ownership, no sequencing — just capture the thought so it is not lost. When an item is ready to plan, promote it into a dated phase (or a new post-1.0 milestone) with real tasks and a Quest/headless gate.

**How to add a note**

- One bullet per idea; optional one-line “why / context.”
- Prefer product intent over implementation detail.
- If core must own part of it, mirror a short pointer in core’s Post-1.0 section.

**Notes**

- **Selection-gated telekinesis (far-field keep-alive).** Grip today both possesses and drives; releasing grip drops possession, so resting a hand after a long telekinesis throw loses control. Intended model: telekinesis is a short-distance drive on a *selected* object. Deselect disables it; while selected, grab can re-drive immediately even from far away (no re-acquire). Full OS would also allow parking a voodoo volume on the object, but selection→drive is the fast path so users are not forced into “move → voodoo → move → voodoo” for far-field play. *(Felt on Quest during 0.3 stick-velocity testing.)*

---

## Working agreement

1. **ABI changes are append-only** after 0.1 freeze unless both repos bump a shared ABI version in the same PR pair.
2. **Core merges first** for a version bump when native symbols change; Unity consumes the new binary the same day when possible.
3. **Headless green ≠ phase done** for 0.2+ — Quest gate in this doc must pass (or be explicitly waived with reason).
4. **Full SDK polish** (samples quality, docs, CI) can trail feature work but must land by Unity 1.0.
