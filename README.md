# 3OS_Unity

A drop-in Unity SDK for **3OS (Three-Oh-Ess)**, a voxel-first spatial operating system interface. This repository provides a Unity Package Manager (UPM) compliant wrapper that marshals high-performance native C++ volumetric interactions directly into the Unity Engine runtime.

Core kernel: [scifiuiguy/3OS](https://github.com/scifiuiguy/3OS). Local verification host: sibling folder `3OS_Unity_Sample_Project` (Unity 6, created via Editor/CLI).

## 🚀 Key Features

* **Biomechanical Subordination:** Rest your arms. All processing maps down to wrist, finger, and gaze intent vectors, completely neutralizing shoulder (coracobrachialis) fatigue.
* **Telekinetic Velocity Core:** Move or rotate assets across local voxel grids effortlessly using high-fidelity velocity/acceleration rate-control algorithms rather than exhausting 1:1 physical tracking.
* **Scale-Space Spatial Teleportation:** Utilize near-field dynamic proxies (World Proxy Half-Dome & Double-Proxy Lattices) to navigate, select, and rearrange far-field assets via coordinate-remapping portals.
* **Blittable P/Invoke Bridge:** Features an ultra-low-overhead C# marshalling layer built to sync hardware hand telemetry with native C++ kernel loops at high frame rates.

## 📦 Installation via Unity Package Manager

**Unity 6 (`6000.0`) recommended.**

### Local sibling (dev / Quest smoke test)

In `3OS_Unity_Sample_Project/Packages/manifest.json`:

```json
"com.scifiuiguy.threeos.unity": "file:../../3OS_Unity"
```


### Git URL

1. Open your Unity project.
2. **Window** → **Package Manager** → **+** → **Add package from git URL...**
3. Paste:

```text
https://github.com/scifiuiguy/3OS_Unity.git
```

*Or in `Packages/manifest.json`:*

```json
"com.scifiuiguy.threeos.unity": "https://github.com/scifiuiguy/3OS_Unity.git"
```

## 📂 File Structure Outline

```text
3OS_Unity/
├── package.json
├── README.md
├── LICENSE
├── docs/NATIVE_BINARIES.md         # How 3os_kernel.dll / .so land in Plugins/
├── Native~/                        # Optional git submodule → scifiuiguy/3OS
├── Plugins/
│   ├── Android/                    # lib3os_kernel.so (Quest)
│   └── Windows/                    # 3os_kernel.dll (Editor / PC VR)
├── Runtime/
│   ├── ThreeOS.Runtime.asmdef
│   ├── Components/
│   │   ├── ThreeOSBridge.cs
│   │   ├── ThreeOSInputRouter.cs
│   │   └── ThreeOSMovable.cs
│   └── Data/
│       └── InteropStructs.cs
├── Shaders/
└── Samples~/
    └── Demo/
        └── 3OS_DemoScene.unity     # Smoke-test scene (Editor-generated)
```

## 🛠 Quickstart Guide

1. Open `3OS_Unity_Sample_Project` in **Unity 6** (`6000.0.60f1` or newer 6000.0.x).
2. Confirm the `file:../3OS_Unity` package resolves in Package Manager.
3. Configure **Project Settings** → **XR Plug-in Management** → **OpenXR** (Meta Quest feature group for device builds).
4. Open `Assets/Scenes/3OS_DemoScene` (host copy) or import the package sample **3OS Demo Scene**.
5. Build & Run to Quest 2 when ready (Phase 0.0 gate: empty scene launches).

**Phase 0.1:** Drop `ThreeOSBridge` + `ThreeOSInputRouter` + `ThreeOSDebugHud` on a GameObject (sample host uses `3OS_Harness`). Plugins ship under `Plugins/Windows/3os_kernel.dll` and `Plugins/Android/lib3os_kernel.so` (+ `libc++_shared.so`). HUD should show version `0.1.0`, ABI `1`, and rising tick count.

See [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) and [docs/NATIVE_BINARIES.md](docs/NATIVE_BINARIES.md).

## 📄 License

This project is open-source software licensed under the MIT License.
