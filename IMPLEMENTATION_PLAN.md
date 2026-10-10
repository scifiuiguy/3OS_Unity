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
| **0.4** | Glyph registry, cubic demo matrix, Box insert + cut events, selection | Dual-root demo dir; glyphs/labels; snap-insert anim; FS cut; select/highlight | Matrix field; drop-in **cuts** to disk; trigger select + far possess |
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

### 📂 Phase 0.4: Glyphs, FS sync & selection
**Pairs with:** Core 0.4  
**Goal:** Unity loads the Quest/Editor demo directory, draws typed glyphs + labels in a cubic matrix field, supports drag-snap insert into a Box with open/closed anim, write-through **cut** via kernel events, and single-select far telekinesis.

**Host notes (see core 0.4 for package design)**  
- `.3glyph` = GLB bytes + embedded `THREEOS_glyph` association metadata (single file).  
- **Schema + pack JSON + injector live in core:** `docs/THREEOS_glyph.md`, `docs/glyph_packs/`, `tools/inject_3glyph.py`.  
- Unity installs packs into the kernel and draws **procedural semi-transparent glyph cubes** (authored `.3glyph` mesh decode can replace cubes later); kernel owns extension → glyph_id, snap/insert, mutation events, selection.  
- Authored packs (Blender): glTF-logo glyph for `.glb`/`.gltf` **content**; `box` + `box_open` glyphs for Boxes.  
- **Glyphs render semi-transparent** (Unlit/Sprites alpha path on Quest). Not opaque blocks.

**Quest demo directory (operator-staged)**  
Documented path: **`/storage/emulated/0/Documents/3OS_Demo`** (Editor: `Documents/3OS_Demo`). Host also uses app **persistentDataPath/3OS_Demo** as the writable root and **additively syncs** Documents ↔ persistent (never wipes Documents). Minimal fixture remains `simple.glb` + `Test1/`; any additional files/folders under the root are listed as Objects/Boxes.

**Expected first paint**

- Each root file → Object glyph (glTF pack id for `.glb`) with name label below.  
- Each root folder → Box glyph (closed `box`) with name + secondary child-count label.  
- Kernel `StorageLayoutDemo(head)` places all roots in a **cubic matrix** 1 m in front of the user (not a fixed two-item side-by-side row).

**Drop-into-Box UX (host viz + anim; kernel snap/commit)**

- Drag Object near Box within **0.2 m** → glyph snaps just above Box; show ↓ arrow under the name label (insert pending).  
- **On snap:** kernel zeros Object velocity and re-anchors stick `start_hand` to the **current** hand pose (grip stays down). Same re-anchor on unsnap.  
- Leave threshold → unsnap; hide arrow.  
- Release while snapped → play sequence: Box `box_open` → scale bump **+10%** then return → closed `box`; Object glyph leaves the field; Box secondary label updates (`1 object` / `N objects`).  
- Handle kernel `ObjectMovedIntoBox`: **cut/move** the file into the Box folder on each demo root (Documents first on Quest when present); request `MANAGE_EXTERNAL_STORAGE` when Documents cut needs it; ack success/failure so core can commit or roll back. First drop may copy until all-files access is granted, then subsequent drops cut.

- [x] **Task U4.1: Demo directory wiring & dual-root sync**
  - [x] Document Quest `Documents/3OS_Demo` + Editor fallback; persistent writable root ([`docs/QUEST_DEMO_DIR.md`](../docs/QUEST_DEMO_DIR.md) / Android storage helpers).
  - [x] On start, list union of Documents + persistent into ABI (`storage_add_*`); folders → Boxes with child counts; files → Objects.
  - [x] Additive Documents→persistent sync for MTP-staged content; soft-fail HUD if listing empty/missing.
- [x] **Task U4.2: Glyph pack install**
  - [x] Ship sample packs (`gltf_mark`, `box`, `box_open`) under package Resources/Runtime Glyphs; register with kernel at startup via `InstallGlyphPack`.
  - [ ] Editor helper: `.glb` → inject `THREEOS_glyph` → `.3glyph` (optional; core `tools/inject_3glyph.py` covers authoring).
- [x] **Task U4.3: Glyph + label instantiation + matrix field**
  - [x] Spawn Object/Box visuals from kernel workspace items (procedural cubes sized to authored glyph scale; pack ids/tints drive look).
  - [x] Force **semi-transparent** materials on glyph instances (Quest-safe Unlit/Sprites path).
  - [x] World-space labels: Object name; Box name + smaller child-count line (billboarded, opaque/readable).
  - [x] Layout: call `StorageLayoutDemo` cubic matrix for all in-field roots (replaces two-entry side-by-side).
- [x] **Task U4.4: Drag, snap affordance, insert anim**
  - [x] Drive Object/Box drag via possess/telekinesis; feed poses so kernel evaluates Box proximity.
  - [x] When kernel reports insert-pending: snap visual to hold pose; show insert arrow (kernel vel clear / stick re-anchor).
  - [x] On commit event: play Box open → +10% bump → closed; hide Object glyph; refresh count label from kernel.
- [x] **Task U4.5: FS event sink (cut write-through)**
  - [x] Poll storage mutation events; cut/move into Box on Android/Editor; ack success/failure; rollback path on failure.
  - [x] Quest Documents cut gated on all-files access (`ThreeOSAndroidStorage`); persistent root always writable; relaunch listing shows file inside the Box.
- [x] **Task U4.6: Single selection (0.4 tack-on)**
  - [x] Index trigger → ABI `selection_set` / empty miss → `selection_clear` (skip while dome/lattice owns Select).
  - [x] Yaw-aligned floor-parallel highlight box (matte white, alpha 0.2).
  - [x] While selected, grip telekinesis works beyond near-field possess distance.
- [x] **Task U4.7: Association / demo smoke**
  - [x] Debug HUD: storage status, snap/insert messages, `Sel:<name|none>`; content `.glb` resolves to glTF pack id (not Box packs).

**Quest gate:** Staged `3OS_Demo` shows glyphs + Boxes in cubic matrix; drag-snap insert with open/bump/close; file **cut** into folder on disk (APK 24+; grant all-files on first Documents drop); index-trigger select shows highlight; empty trigger clears; selected object stays far-draggable until deselect (APK 25+). *(Rename via keyboard is **not** required for 0.4 — see 1.0.)*

---

### 🗂️ Phase 0.5: Layouts, Anchors & Frustum UX
**Pairs with:** Core 0.5  
**Goal:** Expose workspace modes with Quest-friendly controls.

- [ ] **Task U5.1: Mode toggles**
  - [ ] Matrix / Cluster / Real Anchor controls (debug panel or gestures). *(0.4 already applies a demo cubic matrix via `StorageLayoutDemo`; 0.5 owns user-facing mode switching.)*
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
- [ ] **Task U7.3: Text injection (deferred from 0.4)**
  - [ ] Unity system keyboard / TMP / debug field → core text router → rename label updates.
- [ ] **Task U7.4: Acceptance checklist (Quest 2)**
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

- *(Selection-gated telekinesis promoted into Phase 0.4 Task U4.6.)*
- [ ] **Glyph pack GUI (artist tool):** Simple GUI (Editor window and/or small desktop app) to generate `.3glyph` packs without CLI — pick GLB, fill association/tint fields, run inject. Mirrors core Post-1.0 tooling note; optional path for the deferred U4.2 Editor helper.

---

## Working agreement

1. **ABI changes are append-only** after 0.1 freeze unless both repos bump a shared ABI version in the same PR pair.
2. **Core merges first** for a version bump when native symbols change; Unity consumes the new binary the same day when possible.
3. **Headless green ≠ phase done** for 0.2+ — Quest gate in this doc must pass (or be explicitly waived with reason).
4. **Full SDK polish** (samples quality, docs, CI) can trail feature work but must land by Unity 1.0.
