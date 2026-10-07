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
| **0.4** | Storage glyphs + host text ABI | Folder pick / injected text → spawn Boxes/Objects | Glyphs appear; rename via Unity text |
| **0.5** | Layouts, anchors, selection frustum | Gestures/UI to toggle modes + frustum viz | Matrix/cluster; multi-select; ego/allo switch |
| **0.6** | Full enclosure planes + AI command ABI | `UnitySemanticMask` + command hook from host | Wall clip looks right; one voice/command action |
| **1.0** | Native OpenXR path in core (parity) | UPM polish, samples, docs, CI binaries | Full demo scene acceptance (same checklist as core 1.0) |

---

### ⚙️ Phase 0.0: Package & Quest Project Scaffold
**Pairs with:** Core 0.0  
**Goal:** Installable package shell and a Quest-capable sample project — no native calls required yet.

- [ ] **Task U0.1: UPM skeleton**
  - [ ] Real `package.json` (`com.scifiuiguy.threeos.unity`), asmdef, folder layout from README.
  - [ ] Fix README Git URL / manifest install line (placeholders today).
  - [ ] MIT `LICENSE`, `.gitignore` (Library, Temp, builds, large binaries policy).
- [ ] **Task U0.2: Sample project wiring**
  - [ ] Unity 2022.3 LTS+ sample with OpenXR + XR Interaction / XR Hands as needed for Quest 2.
  - [ ] Empty `Samples~/3OS_DemoScene.unity` deploys to Quest (smoke test).
- [ ] **Task U0.3: Native binary pipeline (stubs)**
  - [ ] `Native~/` submodule (or documented path) pointing at `scifiuiguy/3OS`.
  - [ ] Document how Windows `3os_kernel.dll` and Android `lib3os_kernel.so` land in `Plugins/`.

**Quest gate:** Demo scene launches on Quest 2 (black room / XR rig OK).

---

### 🧱 Phase 0.1: Thin Harness on Frozen ABI
**Pairs with:** Core 0.1 (**blocks Unity 0.1** — ABI must exist first)  
**Goal:** Verification channel. Not cinematic — prove the pipe.

- [ ] **Task U1.1: Blittable C# mirrors**
  - [ ] `InteropStructs.cs` matches core layouts; assert sizes in Editor where practical.
  - [ ] Document units/handedness parity with core interop docs.
- [ ] **Task U1.2: `ThreeOSBridge`**
  - [ ] Load `3os_kernel` (Editor Windows DLL + Quest Android SO).
  - [ ] Lifecycle: init / tick / shutdown; `threeos_version` (or equivalent) ping.
- [ ] **Task U1.3: `ThreeOSInputRouter`**
  - [ ] Map XR Hands / controllers → `InteropInputFrame` each frame.
  - [ ] Fail soft if tracking lost; no native crashes on null poses.
- [ ] **Task U1.4: Debug HUD**
  - [ ] On-screen: plugin loaded, ABI version, frame counter, last error.

**Quest gate:** App runs on Quest 2; HUD shows successful ping and continuous frame marshal for ≥30s.

---

### 🕸 Phase 0.2: Proxy & Portal Visualization
**Pairs with:** Core 0.2  
**Goal:** See and drive topology on device.

- [ ] **Task U2.1: Prefabs**
  - [ ] `WorldProxyDome.prefab` and `DoubleProxyLattice.prefab` driven by kernel state (not fake local-only logic).
- [ ] **Task U2.2: Scene sync**
  - [ ] Apply proxy open/close and coordinate remaps from native tick results to Unity transforms/meshes.
- [ ] **Task U2.3: Portal interactions**
  - [ ] Near ↔ far and cross-proxy drag update the correct far-field GameObject pose.

**Quest gate:** Spawn Half-Dome → select region → Double-Proxy → portal-drag a test cube far ↔ near.

---

### ⚡ Phase 0.3: Telekinesis on Device
**Pairs with:** Core 0.3  
**Goal:** Feel rate-control and floor clamp.

- [ ] **Task U3.1: `ThreeOSMovable`**
  - [ ] Component registers targets with kernel; applies velocity/transform deltas from native.
- [ ] **Task U3.2: Possession UX**
  - [ ] Hover/select via proxy cursor; possess → wrist micro-motion glides; release coasts.
- [ ] **Task U3.3: Floor debug**
  - [ ] Align Unity floor plane with core floor height; confirm clamp (no underground objects).

**Quest gate:** Possess object, glide across room with small wrist motion, release with coast; object rests on floor.

---

### 📂 Phase 0.4: Glyphs & Host Text
**Pairs with:** Core 0.4  
**Goal:** Unity supplies FS listing / text; core maps glyphs and labels.

- [ ] **Task U4.1: Host file list**
  - [ ] Feed a test directory listing across the host ABI (Quest sandbox–aware paths or Editor folder).
- [ ] **Task U4.2: Glyph instantiation**
  - [ ] Spawn Box/Object meshes from kernel glyph descriptors.
- [ ] **Task U4.3: Text injection**
  - [ ] Unity keyboard / TMP / debug field → core text router → rename label updates.

**Quest gate:** Test folder populates as 3D glyphs; rename one object from Unity text input.

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

## Working agreement

1. **ABI changes are append-only** after 0.1 freeze unless both repos bump a shared ABI version in the same PR pair.
2. **Core merges first** for a version bump when native symbols change; Unity consumes the new binary the same day when possible.
3. **Headless green ≠ phase done** for 0.2+ — Quest gate in this doc must pass (or be explicitly waived with reason).
4. **Full SDK polish** (samples quality, docs, CI) can trail feature work but must land by Unity 1.0.
