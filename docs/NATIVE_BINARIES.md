# Native binary pipeline (`3os_kernel` → `Plugins/`)

`3OS_Unity` does not compile C++ itself. It loads a prebuilt shared library from:

| Platform | Plugin path | Artifact from core |
| --- | --- | --- |
| Windows Editor / PC VR | `Plugins/Windows/3os_kernel.dll` | `build/Release/3os_kernel.dll` (MSVC) |
| Android / Quest | `Plugins/Android/lib3os_kernel.so` | NDK / CI Android build of `3os_kernel` |

## Local Windows copy (Phase 0.1+)

From the **3OS** core repo (sibling of this package when using `3OS_Unity_Sample_Project`):

```powershell
cmake -S . -B build
cmake --build build --config Release
Copy-Item build\Release\3os_kernel.dll `
  ..\3OS_Unity\Plugins\Windows\3os_kernel.dll -Force
```

## Android / Quest (arm64)

Using Unity’s NDK + Ninja (paths from `GLOBAL_ATTRIBUTES` / Hub install):

```powershell
$ndk = "C:\Program Files\Unity\Hub\Editor\6000.0.60f1\Editor\Data\PlaybackEngines\AndroidPlayer\NDK"
$cmake = "...\AndroidPlayer\SDK\cmake\3.22.1\bin\cmake.exe"
$ninja = "...\AndroidPlayer\SDK\cmake\3.22.1\bin\ninja.exe"
cmake -S . -B build-android-arm64 -G Ninja `
  -DCMAKE_TOOLCHAIN_FILE="$ndk\build\cmake\android.toolchain.cmake" `
  -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-29 `
  -DANDROID_STL=c++_shared -DCMAKE_BUILD_TYPE=Release `
  -DCMAKE_MAKE_PROGRAM="$ninja"
cmake --build build-android-arm64
Copy-Item build-android-arm64\lib3os_kernel.so `
  ..\3OS_Unity\Plugins\Android\lib3os_kernel.so -Force
```

Committed `PluginImporter` `.meta` files mark `lib3os_kernel.so` and `libc++_shared.so` as **Android / ARM64** only (required for packaging into the Quest APK). Do not replace those metas with stub GUID-only files.

CI for core (`.github/workflows/build-binaries.yml`) will eventually publish both artifacts for this package to consume.
