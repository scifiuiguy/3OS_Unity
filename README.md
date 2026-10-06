# 3OS_Unity

A drop-in Unity SDK for **3OS (Three-Oh-Ess)**, a voxel-first spatial operating system interface. This repository provides a Unity Package Manager (UPM) compliant wrapper that marshals high-performance native C++ volumetric interactions directly into the Unity Engine runtime.

## 🚀 Key Features

* **Biomechanical Subordination:** Rest your arms. All processing maps down to wrist, finger, and gaze intent vectors, completely neutralizing shoulder (coracobrachialis) fatigue.
* **Telekinetic Velocity Core:** Move or rotate assets across local voxel grids effortlessly using high-fidelity velocity/acceleration rate-control algorithms rather than exhausting 1:1 physical tracking.
* **Scale-Space Spatial Teleportation:** Utilize near-field dynamic proxies (World Proxy Half-Dome & Double-Proxy Lattices) to navigate, select, and rearrange far-field assets via coordinate-remapping portals.
* **Blittable P/Invoke Bridge:** Features an ultra-low-overhead C# marshalling layer built to sync hardware hand telemetry with native C++ kernel loops at high frame rates.

## 📦 Installation via Unity Package Manager

You can install this SDK directly into your Unity project using the Git URL.

1. Open your Unity project (**Unity 2022.3 LTS or newer recommended**).
2. Navigate to **Window** > **Package Manager**.
3. Click the **+** (plus) icon in the top-left corner and select **Add package from git URL...**
4. Paste the following URL:
   `https://github.com`
5. Click **Add**.

*Alternatively, add the following line directly to your project's `Packages/manifest.json`:*
```json
"com.scifiuiguy.threeos.unity": "https://github.com"
```

## 📂 File Structure Outline

```text
3OS_Unity/
├── package.json                    # Unity Package Manager metadata & dependency manifest
├── README.md                       # Package documentation & architectural quickstart
├── Native~/                        # Git Submodule tracking the core C++ source engine
│   └── 3os-core/
├── Plugins/
│   ├── Android/
│   │   └── lib3os_kernel.so        # Pre-compiled native binary optimized for Quest 2 / Snapdragon XR2
│   └── Windows/
│       └── 3os_kernel.dll          # Pre-compiled native binary for PC VR & Editor Link execution
├── Runtime/
│   ├── ThreeOS.Runtime.asmdef      # Assembly Definition mapping strict compilation isolation bounds
│   ├── Components/
│   │   ├── ThreeOSBridge.cs        # Primary P/Invoke native library handle & lifecycle container
│   │   ├── ThreeOSInputRouter.cs   # Translates Unity XR Hands/Controllers to 3OS Input Frame payloads
│   │   └── ThreeOSMovable.cs       # Component attached to target GameObjects to execute velocity metrics
│   └── Data/
│       └── InteropStructs.cs       # Byte-aligned C# mirrors matching native C structures
├── Shaders/
│   └── UnitySemanticMask.shader    # Universal Render Pipeline (URP) vertex shader for non-clipping walls
└── Samples~/                       # Optional sandbox templates (ignored by production builds)
    ├── Prefabs/
    │   ├── WorldProxyDome.prefab
    │   └── DoubleProxyLattice.prefab
    └── 3OS_DemoScene.unity         # Ready-to-test Quest 2 OpenXR evaluation environment
```

## 🛠 Quickstart Guide

1. Ensure you have an active OpenXR pipeline configured in your **Project Settings** > **XR Plug-in Management**.
2. Drop the `3OS_Core_Bridge` prefab (found in `Samples~/Prefabs`) into your scene hierarchy.
3. Reference your active hand tracking transforms or controller anchors within the `ThreeOSInputRouter` inspector slots.
4. Add the `ThreeOSMovable` component to any 3D asset in your scene to instantly grant it telekinetic velocity properties and subject it to system-level floor and wall enclosure constraints.

## 📄 License
This project is open-source software licensed under the MIT License.
