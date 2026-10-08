using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// P/Invoke lifecycle for <c>3os_kernel</c> including topology + kinematics.
    /// </summary>
    public sealed class ThreeOSBridge : MonoBehaviour
    {
#if UNITY_IOS || UNITY_TVOS || UNITY_WEBGL
        private const string PluginName = "__Internal";
#elif UNITY_ANDROID
        private const string PluginName = "3os_kernel";
#else
        private const string PluginName = "3os_kernel";
#endif

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_version();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_abi_version();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_init();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void threeos_shutdown();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_tick(ref InteropInputFrame frame, out InteropTransformDelta outDelta);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr threeos_last_error();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_input_frame();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_transform_delta();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_dome_state();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_double_proxy_state();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_kinetic_state();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_stick_debug();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_set_scale(float worldToProxyRatio);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_open_dome(ref ThreeOSPose headPose);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void threeos_topology_close_dome();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_get_dome(out InteropDomeState outDome);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_open_double_proxy(ulong id, ref ThreeOSAabb farAabb,
            ref ThreeOSPose headPose);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_close_double_proxy(ulong id);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_get_double_proxy(ulong id, out InteropDoubleProxyState outProxy);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_select_dome_region(ulong proxyId, ThreeOSVoxelCoord farMin,
            ThreeOSVoxelCoord farMax, ref ThreeOSPose headPose);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_portal_near_to_far(ulong proxyId, ref ThreeOSPose nearPose,
            out ThreeOSPose outFar);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_portal_far_to_near(ulong proxyId, ref ThreeOSPose farPose,
            out ThreeOSPose outNear);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_portal_cross(ulong fromProxyId, ulong toProxyId,
            ref ThreeOSPose nearInTo, out ThreeOSPose outFar);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_topology_dome_drop(ref ThreeOSPose domeLocalPose, out ThreeOSPose outFar);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_set_params(float gain, float deadzoneM, float friction,
            float floorY);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_possess(ulong entityId, ref ThreeOSPose objectPose);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_release();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_get_state(out InteropKineticState outState);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_get_stick_debug(out InteropStickDebug outDebug);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_set_object_pose(ref ThreeOSPose objectPose);

        public bool IsLoaded { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public uint NativeVersion { get; private set; }
        public uint NativeAbiVersion { get; private set; }
        public ulong TickCount { get; private set; }
        public InteropTransformDelta LastDelta { get; private set; }

        private void Awake()
        {
            InteropStructs.AssertLayout();
            try
            {
                NativeVersion = threeos_version();
                NativeAbiVersion = threeos_abi_version();
                if (threeos_sizeof_input_frame() != InteropStructs.InputFrameBytes ||
                    threeos_sizeof_transform_delta() != InteropStructs.TransformDeltaBytes)
                {
                    LastError = "ABI size mismatch between C# and native";
                    IsLoaded = false;
                    Debug.LogError($"[3OS] {LastError}");
                    return;
                }

                if (NativeAbiVersion >= 2)
                {
                    if (threeos_sizeof_dome_state() != InteropStructs.DomeStateBytes ||
                        threeos_sizeof_double_proxy_state() != InteropStructs.DoubleProxyStateBytes)
                    {
                        LastError = "Topology ABI size mismatch";
                        IsLoaded = false;
                        Debug.LogError($"[3OS] {LastError}");
                        return;
                    }
                }

                if (NativeAbiVersion >= 3)
                {
                    if (threeos_sizeof_kinetic_state() != InteropStructs.KineticStateBytes ||
                        threeos_sizeof_stick_debug() != InteropStructs.StickDebugBytes)
                    {
                        LastError = "Kinematics ABI size mismatch";
                        IsLoaded = false;
                        Debug.LogError($"[3OS] {LastError}");
                        return;
                    }
                }

                if (threeos_init() != 0)
                {
                    LastError = ReadLastError();
                    IsLoaded = false;
                    Debug.LogError($"[3OS] init failed: {LastError}");
                    return;
                }

                IsLoaded = true;
                LastError = string.Empty;
                Debug.Log($"[3OS] native ready version={FormatVersion(NativeVersion)} abi={NativeAbiVersion}");
            }
            catch (Exception ex)
            {
                IsLoaded = false;
                LastError = ex.Message;
                Debug.LogError($"[3OS] failed to load 3os_kernel: {ex}");
            }
        }

        private void OnDestroy()
        {
            if (!IsLoaded)
            {
                return;
            }

            try
            {
                threeos_shutdown();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[3OS] shutdown: {ex.Message}");
            }

            IsLoaded = false;
        }

        public bool Tick(ref InteropInputFrame frame)
        {
            if (!IsLoaded)
            {
                return false;
            }

            try
            {
                var rc = threeos_tick(ref frame, out var delta);
                if (rc != 0)
                {
                    LastError = ReadLastError();
                    return false;
                }

                LastDelta = delta;
                TickCount++;
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool SetScale(float worldToProxyRatio) =>
            Call(() => threeos_topology_set_scale(worldToProxyRatio));

        public bool OpenDome(ThreeOSPose headPose) =>
            Call(() => threeos_topology_open_dome(ref headPose));

        public void CloseDome()
        {
            if (!IsLoaded) return;
            try { threeos_topology_close_dome(); }
            catch (Exception ex) { LastError = ex.Message; }
        }

        public bool TryGetDome(out InteropDomeState dome)
        {
            dome = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_topology_get_dome(out dome) != 0)
                {
                    LastError = ReadLastError();
                    return false;
                }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool OpenDoubleProxy(ulong id, ThreeOSAabb farAabb, ThreeOSPose headPose) =>
            Call(() => threeos_topology_open_double_proxy(id, ref farAabb, ref headPose));

        public bool CloseDoubleProxy(ulong id) =>
            Call(() => threeos_topology_close_double_proxy(id));

        public bool TryGetDoubleProxy(ulong id, out InteropDoubleProxyState proxy)
        {
            proxy = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_topology_get_double_proxy(id, out proxy) != 0)
                {
                    LastError = ReadLastError();
                    return false;
                }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool SelectDomeRegion(ulong proxyId, ThreeOSVoxelCoord farMin, ThreeOSVoxelCoord farMax,
            ThreeOSPose headPose) =>
            Call(() => threeos_topology_select_dome_region(proxyId, farMin, farMax, ref headPose));

        public bool PortalNearToFar(ulong proxyId, ThreeOSPose nearPose, out ThreeOSPose farPose)
        {
            farPose = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_topology_portal_near_to_far(proxyId, ref nearPose, out farPose) != 0)
                {
                    LastError = ReadLastError();
                    return false;
                }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool PortalFarToNear(ulong proxyId, ThreeOSPose farPose, out ThreeOSPose nearPose)
        {
            nearPose = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_topology_portal_far_to_near(proxyId, ref farPose, out nearPose) != 0)
                {
                    LastError = ReadLastError();
                    return false;
                }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool PortalCross(ulong fromId, ulong toId, ThreeOSPose nearInTo, out ThreeOSPose farPose)
        {
            farPose = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_topology_portal_cross(fromId, toId, ref nearInTo, out farPose) != 0)
                {
                    LastError = ReadLastError();
                    return false;
                }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool DomeDrop(ThreeOSPose domeLocal, out ThreeOSPose farPose)
        {
            farPose = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_topology_dome_drop(ref domeLocal, out farPose) != 0)
                {
                    LastError = ReadLastError();
                    return false;
                }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool SetKinematicsParams(float gain, float deadzoneM, float friction, float floorY) =>
            Call(() => threeos_kinematics_set_params(gain, deadzoneM, friction, floorY));

        public bool Possess(ulong entityId, ThreeOSPose objectPose) =>
            Call(() => threeos_kinematics_possess(entityId, ref objectPose));

        public bool ReleasePossessed() => Call(threeos_kinematics_release);

        public bool TryGetKineticState(out InteropKineticState state)
        {
            state = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_kinematics_get_state(out state) != 0)
                {
                    LastError = ReadLastError();
                    return false;
                }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool TryGetStickDebug(out InteropStickDebug debug)
        {
            debug = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_kinematics_get_stick_debug(out debug) != 0)
                {
                    LastError = ReadLastError();
                    return false;
                }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public bool SetKinematicObjectPose(ThreeOSPose objectPose) =>
            Call(() => threeos_kinematics_set_object_pose(ref objectPose));

        public static string FormatVersion(uint packed)
        {
            var major = (packed >> 16) & 0xFFu;
            var minor = (packed >> 8) & 0xFFu;
            var patch = packed & 0xFFu;
            return $"{major}.{minor}.{patch}";
        }

        private bool Call(Func<int> fn)
        {
            if (!IsLoaded) return false;
            try
            {
                if (fn() != 0)
                {
                    LastError = ReadLastError();
                    return false;
                }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        private static string ReadLastError()
        {
            var ptr = threeos_last_error();
            return ptr == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(ptr) ?? string.Empty;
        }
    }
}
