# 🗺️ 3OS_Unity: Implementation Roadmap (Paired with 3OS_Core)

This repo is the **Quest / Editor verification host** and eventual UPM SDK for **[3OS](https://github.com/scifiuiguy/3OS)**. Core owns voxel math, proxies, kinematics, and enclosure policy. Unity owns OpenXR session (via Unity’s plugin), hand/controller telemetry, P/Invoke, rendering, and on-device feel checks.

Phases share version numbers with core. Walk them **in lockstep**: finish the core milestone’s headless/ABI work, then the matching Unity milestone’s Quest gate, then advance both.

```text
Core 0.0 → … → 0.5 (hands) → 0.6 (voodoo look) → 0.7 → 0.8 → 1.0 → 2.0 (collab room / VDI + edge)
                │               │                      │                 │     │     │
         Unity … → 0.5 (hands) → 0.6 (voodoo look) → 0.7 → 0.8 → 1.0 → 2.0 (enterprise host + room displays)
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
| **0.4** | Glyph registry, yaw-level cubic matrix, sticky snap-insert + cut, expand/collapse ABI, selection | Dual-root demo dir; glyphs/labels; snap-insert anim; FS cut; select rim; Box expand/X | Level matrix; nested open/close; sticky drop-in **cuts**; select + far possess |
| **0.5** | Multi-pinch + palm-mode facade ABI | Meta XR hand/controller geo; XR Hands + trigger/grip/A·X; palm modes | Hands-only + controllers-only; meshes; non-primary or palm chord |
| **0.6** | Voodoo volume descriptors + ghost/proxy draw contract | Product Half-Dome / lattice / ghost look (not debug cubes) | Dome → lattice reads as intentional voodoo volumes |
| **0.7** | Layouts, anchors, selection frustum | Mode toggles + frustum viz | Matrix/cluster; multi-select; ego/allo switch |
| **0.8** | Full enclosure planes + AI command ABI | `UnitySemanticMask` + command hook from host | Wall clip looks right; one voice/command action |
| **1.0** | Native OpenXR path in core (parity) | UPM polish, samples, docs, CI binaries | Full demo scene acceptance (same checklist as core 1.0) |
| **2.0** | Multi-user sessions, display registry, spectator routing, media sync | Enterprise login host; room display/compositor binding; shared-workspace UX | 2+ users; snap-to-monitor; spectator cams; handheld pull-in |

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

**Quest gate:** Menu opens Half-Dome → Select opens Double-Proxy lattices → Grab/release portal-drags the magenta test cube far ↔ near / cross-proxy — **pending device run**. *(Product voodoo look — translucent dome/lattice/ghosts — is Phase 0.6.)*

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

### 📂 Phase 0.4: Glyphs, FS sync, selection & Box drill-down
**Pairs with:** Core 0.4  
**Goal:** Unity loads the Quest/Editor demo directory, draws typed glyphs + labels in a **yaw-level** cubic matrix field, supports drag-snap insert (Object→Box and Box→Box) with open/closed anim, write-through **cut** via kernel events (incl. nested paths), single-select far telekinesis with edge-rim highlight, and global Box expand / nested X close.

**Host notes (see core 0.4 for package design)**  
- `.3glyph` = GLB bytes + embedded `THREEOS_glyph` association metadata (single file).  
- **Schema + pack JSON + injector live in core:** `docs/THREEOS_glyph.md`, `docs/glyph_packs/`, `tools/inject_3glyph.py`.  
- Unity installs packs into the kernel and draws **procedural semi-transparent glyph cubes** via `ThreeOS/Glyph` (edge Fresnel rim); kernel owns extension → glyph_id, snap/insert, mutation events, selection, expand/collapse.  
- Authored packs (Blender): glTF-logo glyph for `.glb`/`.gltf` **content**; `box` + `box_open` glyphs for Boxes.  
- **Glyphs render semi-transparent** (URP Transparent / Quest-safe path). Not opaque blocks.

**Quest demo directory (operator-staged)**  
Documented path: **`/storage/emulated/0/Documents/3OS_Demo`** (Editor: `Documents/3OS_Demo`). Host also uses app **persistentDataPath/3OS_Demo** as the writable root and **additively syncs** Documents ↔ persistent (never wipes Documents). Minimal fixture remains `simple.glb` + `Test1/`; any additional files/folders under the root are listed as Objects/Boxes (nested folders supported on open).

**Expected first paint**

- Each root file → Object glyph (glTF pack id for `.glb`) with name label below.  
- Each root folder → Box glyph (closed `box`) with name + secondary child-count label.  
- Kernel `StorageLayoutDemo(head)` places all roots in a **cubic matrix** 1 m in front of the user; **yaw-only / world-up** so head roll at start does not crook the field.

**Drop-into-Box UX (host viz + anim; kernel snap/commit)**

- Drag Object **or Box** near a Box within **0.2 m** → glyph snaps just above Box; show ↓ arrow under the name label (insert pending).  
- **On snap enter:** kernel zeros velocity and re-anchors stick `start_hand` (grip stays down). Escape pose/velocity keep integrating while display holds. **Sticky** first Box; **no snap during lerp catch-up**.  
- Leave threshold → unsnap with lerp catch-up to escape pose; **keep** stick-built velocity (no re-anchor / zero). Hide arrow.  
- Release while snapped → play sequence: Box `box_open` → scale bump **+10%** then return → closed `box`; dragged glyph leaves the field; Box secondary label updates. Expand UI tears down before rebuild (no stale X / destroyed-child refs).  
- Handle kernel `ObjectMovedIntoBox`: **cut/move** file or folder on each demo root using **demo-relative paths** (`Parent/Child`); request `MANAGE_EXTERNAL_STORAGE` when Documents cut needs it; ack success/failure so core can commit or roll back (prior parent restored on failure).

**Box expand / collapse UX**

- Double short-trigger on a Box → ensure children from disk (rel path via `parent_box` chain), kernel `expand_box`, ephemeral cluster tween from X, grey X sphere.  
- X click → close **deepest expanded descendants first**, then the selected Box.  
- X-hover: light-blue edge Fresnel on expanded children (same rim path as white selection; `_Selected` is rim gate only, not ABI selection). Owned materials + `DisableBatching` keep multi-glyph blue rim stable.

- [x] **Task U4.1: Demo directory wiring & dual-root sync**
  - [x] Document Quest `Documents/3OS_Demo` + Editor fallback; persistent writable root ([`docs/QUEST_DEMO_DIR.md`](../docs/QUEST_DEMO_DIR.md) / Android storage helpers).
  - [x] On start, list union of Documents + persistent into ABI (`storage_add_*`); folders → Boxes with child counts; files → Objects.
  - [x] Additive Documents→persistent sync for MTP-staged content; soft-fail HUD if listing empty/missing.
- [x] **Task U4.2: Glyph pack install**
  - [x] Ship sample packs (`gltf_mark`, `box`, `box_open`) under package Resources/Runtime Glyphs; register with kernel at startup via `InstallGlyphPack`.
  - [ ] Editor helper: `.glb` → inject `THREEOS_glyph` → `.3glyph` (optional; core `tools/inject_3glyph.py` covers authoring).
- [x] **Task U4.3: Glyph + label instantiation + matrix field**
  - [x] Spawn Object/Box visuals from kernel workspace items (procedural cubes sized to authored glyph scale; pack ids/tints drive look).
  - [x] Force **semi-transparent** materials on glyph instances (`ThreeOS/Glyph` edge-rim shader; Quest-safe Transparent path).
  - [x] World-space labels: Object name; Box name + smaller child-count line (billboarded, opaque/readable).
  - [x] Layout: call `StorageLayoutDemo` cubic matrix for all in-field roots; kernel yaw-only / world-up keeps the field level if the user rolls their head at start.
- [x] **Task U4.4: Drag, snap affordance, insert anim**
  - [x] Drive Object/Box drag via possess/telekinesis (index-trigger hold); feed poses so kernel evaluates Box proximity.
  - [x] When kernel reports insert-pending: snap visual to hold pose; show insert arrow (enter: vel clear / stick re-anchor; sticky first Box; escape: catch-up keeps velocity; no snap during lerp).
  - [x] On commit event: teardown any open expand UI; play Box open → +10% bump → closed; hide dragged glyph; refresh count label from kernel; `RebuildViews` safe vs destroyed Roots.
- [x] **Task U4.5: FS event sink (cut write-through)**
  - [x] Poll storage mutation events; cut/move file **or folder** into Box on Android/Editor using nested relative paths; ack success/failure; rollback path on failure.
  - [x] Quest Documents cut gated on all-files access (`ThreeOSAndroidStorage`); persistent root always writable; relaunch listing shows content inside the Box.
- [x] **Task U4.6: Single selection (0.4 tack-on)**
  - [x] Index trigger → ABI `selection_set` / empty miss → `selection_clear` (skip while dome/lattice owns Select).
  - [x] Selection highlight = glyph edge Fresnel rim (`_Selected` rim gate + white `_GlowColor`); not a separate floor-parallel box mesh.
  - [x] While selected, trigger-hold telekinesis works beyond near-field possess distance.
- [x] **Task U4.7: Association / demo smoke**
  - [x] Debug HUD: storage status, snap/insert messages, `Sel:<name|none>`; content `.glb` resolves to glTF pack id (not Box packs).
- [x] **Task U4.8: Box expand / collapse (drill-down)**
  - [x] Double short-trigger on a Box opens contents (global mode); X dismiss closes (not a second double-tap).
  - [x] Nested X close: deepest expanded descendants collapse first (leaves → parents), then the selected X’s box.
  - [x] Nested open uses demo-relative path (`Parent/Child`) reconstructed from `parent_box` (not leaf name alone); nested child counts include files **and** subfolders.
  - [x] Host lists box folder, ensures workspace children (`parent_box` / `in_field`), kernel `expand_box` / `collapse_box` matrix above parent (cell-hash collision).
  - [x] Ephemeral cluster open/close anim; grey X sphere; X-hover light-blue child edge Fresnel (same rim as white select; owned materials + `DisableBatching` for multi-glyph stability).
  - [x] No lasting transform parenting between open parent and children after the tween.

**Quest gate (smoke on latest APK, Phase04 XR 51+):** Staged `3OS_Demo` shows glyphs + Boxes in a **level** cubic matrix; drag-snap insert (Object/Box) with sticky hold + escape catch-up; file/folder **cut** into folder on disk (grant all-files on first Documents drop); nested pre-staged folders open recursively; index-trigger select shows white edge rim; empty trigger clears; selected glyph stays far-draggable until deselect; double-tap Box expand + nested X close (deepest-first). *(Rename via keyboard is **not** required for 0.4 — see 1.0.)*

---

### ✋ Phase 0.5: Hand & Controller Integration
**Pairs with:** Core 0.5  
**Goal:** Quest tracked hands and Touch controllers drive the **same** semantic actions through `ThreeOSInputRouter` + the 0.5 input ABI — **primary / secondary / ternary** pinches, **palm up / sideways / down** modifiers, and visible hand/controller geometry — no Trigger-only special case.

**Scope note:** 0.1–0.4 already feed frames and use index-trigger heavily for select/drag. 0.5 adds hand-tracking parity, multi-pinch + palm mapping, Meta (or equivalent) hand/controller meshes, and soft-fail when either source drops.

**Pinch ↔ controller defaults** (see core 0.5): primary = index–thumb / **Trigger**; secondary = middle–thumb / **Grip**; ternary = ring–thumb / **primaryButton (A·X)**. Palm mode modifies which facade action fires.

**Locked bindings:** **Select** = primary press + any palm; **Drag** = primary hold + palm down/sideways; **Voodoo volume** = primary hold + palm up (intent mapped now; full UX in 0.6). **Cursor:** one sphere/hand; `d_thresh = 2.5″`; scale from shortest in-threshold finger pair only. **Gate device:** Quest 2; keep Quest 3–ready OpenXR paths.

- [x] **Task U5.0: Meta XR SDK for hand & controller geometry**
  - [x] **Need:** Yes for product hand meshes + Touch controller models. Unity **XR Hands** alone covers joints/pinches, not Meta skinned hands / official controller geo.
  - [x] **What “Core + Interaction” vs “All-in-One” means:** *All-in-One* is Meta’s kitchen-sink UPM/Asset Store bundle (Core, Interaction, Voice, Haptics, MRUK, …). *Core + Interaction* is the lighter pair we actually need for hands/controllers/interaction helpers. **Prefer Core + Interaction** for 0.5; use All-in-One only if resolve is easier and package weight is acceptable.
  - [x] **Install:** Agent can add Meta scoped registry + `com.meta.xr` package entries to the sample `Packages/manifest.json`; you open Unity once for first fetch. Fallback: Asset Store All-in-One or `.tgz` (manual).
  - [ ] Wire hand + controller meshes into the Quest sample (open Unity once to resolve Meta packages / Building Blocks); keep 3OS input on OpenXR / XR Hands / Input System (vendor-neutral core).
- [x] **Task U5.1: XR Hands + controller sourcing**
  - [x] Wire joints, index/middle/ring–thumb distances + edges, palm normal; controllers trigger / grip / primaryButton + pose → core 0.5 frame.
  - [x] Quantize palm → `Up` / `Sideways` / `Down` with hysteresis (controllers from device orientation).
  - [x] Tracking-lost soft-fail; Quest 2 gate + Quest 3–ready profiles where feasible.
- [x] **Task U5.2: Semantic action parity**
  - [x] Host binding table = locked defaults (select / drag / voodoo stub); dual-source without double-fire.
- [x] **Task U5.3: Interaction point + cursor**
  - [x] One unparented white-sphere cursor per hand; `cursor0` from driving finger pair (shortest `d ≤ 2.5″`, else index–thumb); +4″ floor-forward + 2″ up = hover/select/snap probe (core 5.3).
- [x] **Task U5.3b: Cursor sphere scale interpolation**
  - [x] Shortest in-threshold pair drives 100%→50% scale; on that channel’s pinch trigger, fast-lerp to 100% (core 5.3b). Controllers: same via trigger/grip/primaryButton approach.
- [ ] **Task U5.4: Device smoke**
  - [ ] HUD: source, pinch channel, palm mode, facade action (incl. voodoo stub).
  - [ ] Quest 2: storage path hands-only and controllers-only; white spheres; select vs drag (palm) vs palm-up voodoo stub.

**Quest gate (Quest 2; Quest 3–ready code):** Controllers-only and hands-only complete select → drag → snap-insert → Box expand/X; meshes visible; cursors are probes; palm-up primary-hold shows voodoo-volume intent (stub OK).

---

### 🧿 Phase 0.6: Voodoo Volume Look Development
**Pairs with:** Core 0.6  
**Goal:** Replace functional/debug proxy stand-ins with **product-shaped** World Proxy Half-Dome and Double-Proxy Lattice visuals — translucent volumes, ghost copies, and readable portal bloom — driven by core voodoo descriptors.

**Scope note:** Portal teleport math already works from Unity 0.2. This phase is look + binding to 0.6 descriptors, not new remapping logic. Enclosure wall-mask shaders wait for 0.8.

- [ ] **Task U6.1: Half-Dome product visual**
  - [ ] Translucent Half-Dome mesh/material driven by kernel dome descriptor (center, radius/extents, open state).
  - [ ] Enter/hold/place affordance reads clearly (not a solid debug primitive).
- [ ] **Task U6.2: Double-proxy lattice look**
  - [ ] Near-field lattice shows ~3" cell structure (or parameterized cell size) as a see-through volume.
  - [ ] Bind open lattice to core descriptor + remap transforms each frame / on dirty.
- [ ] **Task U6.3: Ghosts & cursors**
  - [ ] Ghost copies of far-field participants inside the near-field proxy; optional far-field ghost hand/cursor.
  - [ ] Keep glyphs/storage field visuals distinct from proxy ghosts (no double-draw confusion).
- [ ] **Task U6.4: Portal bloom polish**
  - [ ] Animate dome → lattice open/close using core portal affordance state (no parallel host-only state machine).
  - [ ] Quest smoke: portal drag still teleports; look passes a “readable at a glance” check.

**Quest gate:** Dome → region → lattice sequence looks like intentional voodoo volumes; ghosts convey near↔far linkage during a portal drag (teleport still correct).

---

### 🗂️ Phase 0.7: Layouts, Anchors & Frustum UX
**Pairs with:** Core 0.7  
**Goal:** Expose workspace modes with Quest-friendly controls. *(Deferred from the original Unity 0.5 slot.)*

- [ ] **Task U7.1: Mode toggles**
  - [ ] Matrix / Cluster / Real Anchor controls (debug panel or gestures). *(0.4 already applies a demo cubic matrix via `StorageLayoutDemo`; 0.7 owns user-facing mode switching.)*
- [ ] **Task U7.2: Anchor modes**
  - [ ] Ego / allo / object-locked parenting behaves correctly under XR rig motion.
- [ ] **Task U7.3: Selection frustum viz**
  - [ ] Render frustum; multi-select group moves rigidly under telekinesis/portals. *(Uses 0.5 dual-hand / dual-controller pinch.)*

**Quest gate:** Toggle layout; switch ego↔allo; multi-select via frustum and move as a group.

---

### 🧱 Phase 0.8: Semantic Mask & Command Hook
**Pairs with:** Core 0.8  
**Goal:** Engine-side wall clip + typed commands from Unity (stand-in for OEM AI). *(Deferred from the original Unity 0.6 slot.)*

- [ ] **Task U8.1: `UnitySemanticMask.shader`**
  - [ ] Consume plane equations from core; crisp wall/ceiling cut in URP.
- [ ] **Task U8.2: Command bridge**
  - [ ] Debug string/enum → core AI/command ABI (e.g. “matrix layout”).
- [ ] **Task U8.3: Optional voice**
  - [ ] Wire platform speech-to-text *in Unity* to the same command entry (transport stays in host).

**Quest gate:** Object masked at a wall plane; one command changes layout or opens a proxy.

---

### 🚀 Phase 1.0: UPM Product + Parity with Core Native OpenXR
**Pairs with:** Core 1.0  
**Goal:** Ship-quality package; Quest demo matches the core acceptance checklist. Core’s standalone OpenXR path is **parity**, not a prerequisite for this demo.

- [ ] **Task U9.1: Packaging**
  - [ ] Install-from-git works; samples import cleanly; CI builds/copies Windows + Android natives.
- [ ] **Task U9.2: Demo scene completion**
  - [ ] Prefabs, bridge, documented Quest 2 setup in README quickstart.
- [ ] **Task U9.3: Text injection (deferred from 0.4)**
  - [ ] Unity system keyboard / TMP / debug field → core text router → rename label updates.
- [ ] **Task U9.4: Acceptance checklist (Quest 2)**
  - [ ] Half-Dome macro-target on empty far field (product look from 0.6).
  - [ ] Double-proxy micro-resolve + CREATE in empty voxels.
  - [ ] Portal teleport near ↔ far and cross-proxy.
  - [ ] Hands **and** controllers complete the demo path (from 0.5).
  - [ ] Velocity glide + friction; floor clamp holds.
  - [ ] Wall masking correct (from 0.8).
  - [ ] Storage glyphs + rename via host text.
  - [ ] Layout toggle + one anchor-mode switch in-session (from 0.7).

---

### 🏢 Phase 2.0: Collaborative Room Workspaces
**Pairs with:** Core 2.0  
**Goal:** Unity (and/or companion enterprise hosts) as the interactive client and room-integration layer for multi-user 3OS — login to on-prem **VDI** or home **edge** sessions, bind every room display (rail monitors, 4K guest screens, logged-in handhelds), drive spectator compositing, and expose shared-workspace UX (invite graphs, mask, snap-to-monitor, shared possession). Design write-up: core [`docs/COLLABORATIVE_ROOMS.md`](../docs/COLLABORATIVE_ROOMS.md); physical reference room and console/desk specs live there and in core Phase 2.0.

- [ ] **Task U10.1: Enterprise / edge session host**
  - [ ] Login flow into 3OS enterprise (VDI) or home-edge session; land in **personal** workspace first.
  - [ ] UI to create/join **shared** workspaces (pair, small group, full-room) per core invite ABI.
  - [ ] Presence roster; soft reconnect when eyewear or network drops mid-session.
- [ ] **Task U10.2: Room display binding**
  - [ ] Register wall monitors + 4K guest screens with live pose from server/IMU stream; show portrait/landscape correctly.
  - [ ] Handheld (tablet/phone) join when device has session creds; visualize localized pose in the shared field.
  - [ ] Editor/sim fixtures for the 20×20 reference room when hardware is unavailable.
- [ ] **Task U10.3: Spectator compositor client**
  - [ ] Consume server spectator feeds (four corner cameras → opposite-wall defaults); do not require each headset to render all wall outputs.
  - [ ] Full-room workspace controls can reassign which camera drives which monitors; guest 4Ks prioritized for couch viewing.
- [ ] **Task U10.4: Snap-to-monitor & possession UX**
  - [ ] Gesture/transform path: snap volumetric content onto a display → object-focused view by content type (3D / video / image / text).
  - [ ] Possess monitor; peer invitees can interact or release → restore spectator default.
  - [ ] Legacy station mode: face wall, possess the two monitors above the user’s console; dual-monitor desktop until release.
- [ ] **Task U10.5: Solo-in-shared masking & pull-in**
  - [ ] Honor core mask rules so peer volumetric content stays hidden in eyewear during solo focus inside a shared session.
  - [ ] Gesture to pull a localized logged-in handheld surface into the shared space as a live mirror.
- [ ] **Task U10.6: Open as copy vs open as reference (flyweight)**
  - [ ] UI/affordance when opening an asset into a workspace: **copy** (isolated instance) vs **reference** (shared server object); never imply mode from filename alone. Copy path for isolation; flyweight path to **avoid diff/merge** when the team wants one live truth (often same-room).
  - [ ] Flyweight: show live object/attribute updates from other workspaces holding the same reference; **no merge UI** for live edits (one singleton). Copy: no cross-workspace object mutation.
  - [ ] Honor locality: offer/allow flyweight inside room (and building/campus when on the same low-latency fabric); by default force or recommend **copy** for cross-region peers.
  - [ ] If tenant has **site-to-site flyweight** enabled (paid MMO-class sync), allow cross-site reference opens with the same co-editor roster / VOIP / avatar fly-in; otherwise keep cross-site on the copy + VCS path.
  - [ ] **Co-editor roster on flyweight open/join:** always show who else is editing that asset (name, room/station if known, workspace).
  - [ ] Roster **VOIP conference** into 3OS with selected co-editors; **bidirectional avatar fly-in** (their avatars in your room field, yours in theirs) for cross-room campus collab on the same flyweight.
  - [ ] Default place primary content at room center on open; keep placement independent of copy-vs-reference.
- [ ] **Task U10.6b: VCS UX for simultaneous copies**
  - [ ] When multiple workspaces hold **copies** (including cross-site), surface branch-like status (source revision, dirty, ahead/behind) and volumetric actions: promote/check-in, get-latest, resolve conflict — plumb Git/Perforce/Plastic/etc. (not silent overwrite).
  - [ ] Conflict/merge UI for divergent copies; optional lock/lease when merge would be unsafe.
  - [ ] Do not invent merge chrome for true same-domain flyweight live editing.
- [ ] **Task U10.7: View vs object transform UX**
  - [ ] Map gestures clearly: orbit/tumble/“flip for looking” → **view/camera**; spin/translate the asset → **object anchor** (writes flyweight or local copy per open mode).
  - [ ] Parallel-workspace smoke: camera flip in A leaves B’s object upright; object spin on a flyweight flips in B; object spin on a copy does not.
  - [ ] Spectator / focused monitor views follow workspace cameras without baking view into asset transforms.
- [ ] **Task U10.8: Media sync presentation**
  - [ ] Apply session-scoped updates for text, images, video, and 3D assets on eyewear and possessed displays (honor copy vs reference + mask rules).
  - [ ] Respect server LOD/quality ladders under 12-user VDI load vs smaller edge nodes (including flyweight sharing vs duplicated instances).
- [ ] **Task U10.9: Deployment samples**
  - [ ] Sample “room sim” scene + docs for VDI client vs edge client configuration.
  - [ ] Device smoke / lab gate aligned with core 2.0 gate (2+ users, snap/release, spectator restore, handheld pull-in, flyweight vs copy + view vs object, copy diverge → VCS promote/merge).

**Quest / lab gate (2.0):** Two authenticated clients in one shared workspace; at least one real or simulated wall display shows spectator then focused content after snap; second client can release possession; masked solo mode hides peer glyphs; parallel workspaces demonstrate flyweight object sync vs camera-only view changes and **copy** isolation with a VCS promote/merge path; documented path for VDI vs edge host config.

---

### 📝 Parking lot (ideas without tasks yet)

Use this section for design notes, UX instincts, and “we’ll need this later” observations that are **not** ready to become checklist tasks. No ownership, no sequencing — just capture the thought so it is not lost. When an item is ready to plan, promote it into a real phase (including 2.x) with tasks and a Quest/lab gate.

**How to add a note**

- One bullet per idea; optional one-line “why / context.”
- Prefer product intent over implementation detail.
- If core must own part of it, mirror a short pointer in core’s parking-lot section.

**Notes**

- *(Selection-gated telekinesis promoted into Phase 0.4 Task U4.6.)*
- [ ] **Glyph pack GUI (artist tool):** Simple GUI (Editor window and/or small desktop app) to generate `.3glyph` packs without CLI — pick GLB, fill association/tint fields, run inject. Mirrors core Post-1.0 tooling note; optional path for the deferred U4.2 Editor helper.
- [ ] **Global field spacing + glyph scale:** When core exposes `get/set_field_spacing`, drive it from Unity inspector/settings together with `GlyphSizeM` (keep ~2.2× ratio or one master cell size) and optionally re-layout / rescale the visible field.
- [ ] **Matrix padding / margins:** When core exposes padding/snap-margin knobs, surface them in Unity settings with field spacing (dense stacks should feel intentional, not sticky-snap-only).

---

## Working agreement

1. **ABI changes are append-only** after 0.1 freeze unless both repos bump a shared ABI version in the same PR pair.
2. **Core merges first** for a version bump when native symbols change; Unity consumes the new binary the same day when possible.
3. **Headless green ≠ phase done** for 0.2+ — Quest gate in this doc must pass (or be explicitly waived with reason).
4. **Full SDK polish** (samples quality, docs, CI) can trail feature work but must land by Unity 1.0.
5. **Phase 2.0** may introduce companion session/compositor services; keep display/workspace ABI append-only and document VDI vs edge host responsibilities beside the UPM package.
