using System.Runtime.InteropServices;
using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Blittable mirrors of core <c>threeos_abi.h</c> (Phase 0.3; layout freeze from 0.1).
    /// Kernel space is OpenXR right-handed Y-up, meters. Convert at the host boundary.
    /// </summary>
    public static class InteropStructs
    {
        public const uint AbiVersion = 3;
        public const int InputFrameBytes = 192;
        public const int TransformDeltaBytes = 48;
        public const int DomeStateBytes = 48;
        public const int DoubleProxyStateBytes = 64;
        public const int KineticStateBytes = 48;
        public const int StickDebugBytes = 64;

        public static void AssertLayout()
        {
            var inputSize = Marshal.SizeOf<InteropInputFrame>();
            var deltaSize = Marshal.SizeOf<InteropTransformDelta>();
            var domeSize = Marshal.SizeOf<InteropDomeState>();
            var proxySize = Marshal.SizeOf<InteropDoubleProxyState>();
            var kineticSize = Marshal.SizeOf<InteropKineticState>();
            var stickDebugSize = Marshal.SizeOf<InteropStickDebug>();
            Debug.Assert(inputSize == InputFrameBytes, $"InteropInputFrame size {inputSize} != {InputFrameBytes}");
            Debug.Assert(deltaSize == TransformDeltaBytes, $"InteropTransformDelta size {deltaSize} != {TransformDeltaBytes}");
            Debug.Assert(domeSize == DomeStateBytes, $"InteropDomeState size {domeSize} != {DomeStateBytes}");
            Debug.Assert(proxySize == DoubleProxyStateBytes, $"InteropDoubleProxyState size {proxySize} != {DoubleProxyStateBytes}");
            Debug.Assert(kineticSize == KineticStateBytes, $"InteropKineticState size {kineticSize} != {KineticStateBytes}");
            Debug.Assert(stickDebugSize == StickDebugBytes, $"InteropStickDebug size {stickDebugSize} != {StickDebugBytes}");
        }

        /// <summary>Unity left-handed Y-up → OpenXR right-handed Y-up.</summary>
        public static Vector3 UnityToOpenXrPosition(Vector3 unity) =>
            new Vector3(-unity.x, unity.y, unity.z);

        public static Quaternion UnityToOpenXrRotation(Quaternion unity) =>
            new Quaternion(-unity.x, unity.y, unity.z, -unity.w);

        public static Vector3 OpenXrToUnityPosition(Vector3 openXr) =>
            new Vector3(-openXr.x, openXr.y, openXr.z);

        public static Quaternion OpenXrToUnityRotation(Quaternion openXr) =>
            new Quaternion(-openXr.x, openXr.y, openXr.z, -openXr.w);

        public static ThreeOSPose ToOpenXrPose(Pose unityPose) =>
            new ThreeOSPose
            {
                position = UnityToOpenXrPosition(unityPose.position),
                orientation = UnityToOpenXrRotation(unityPose.rotation)
            };

        public static Pose ToUnityPose(ThreeOSPose openXr) =>
            new Pose(
                OpenXrToUnityPosition(new Vector3(openXr.position.x, openXr.position.y, openXr.position.z)),
                OpenXrToUnityRotation(new Quaternion(openXr.orientation.x, openXr.orientation.y,
                    openXr.orientation.z, openXr.orientation.w)));
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ThreeOSVec3
    {
        public float x;
        public float y;
        public float z;

        public static implicit operator ThreeOSVec3(Vector3 v) =>
            new ThreeOSVec3 { x = v.x, y = v.y, z = v.z };

        public Vector3 ToVector3() => new Vector3(x, y, z);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ThreeOSQuat
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public static implicit operator ThreeOSQuat(Quaternion q) =>
            new ThreeOSQuat { x = q.x, y = q.y, z = q.z, w = q.w };

        public Quaternion ToQuaternion() => new Quaternion(x, y, z, w);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ThreeOSPose
    {
        public ThreeOSVec3 position;
        public ThreeOSQuat orientation;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ThreeOSVoxelCoord
    {
        public int x;
        public int y;
        public int z;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ThreeOSAabb
    {
        public ThreeOSVec3 min;
        public ThreeOSVec3 max;
    }

    [System.Flags]
    public enum TrackingFlags : uint
    {
        None = 0,
        Head = 1 << 0,
        LeftAim = 1 << 1,
        RightAim = 1 << 2,
        LeftGrip = 1 << 3,
        RightGrip = 1 << 4,
    }

    [System.Flags]
    public enum ButtonFlags : uint
    {
        None = 0,
        LeftSelect = 1 << 0,
        RightSelect = 1 << 1,
        LeftGrab = 1 << 2,
        RightGrab = 1 << 3,
        Menu = 1 << 4,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropInputFrame
    {
        public ulong frameIndex;
        public double timeSeconds;
        public uint trackingFlags;
        public uint buttonFlags;
        public ThreeOSPose head;
        public ThreeOSPose leftAim;
        public ThreeOSPose rightAim;
        public ThreeOSPose leftGrip;
        public ThreeOSPose rightGrip;
        public float leftTrigger;
        public float rightTrigger;
        public float leftGripValue;
        public float rightGripValue;
        public uint reserved0;
        public uint reserved1;
        public uint reserved2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropTransformDelta
    {
        public ulong entityId;
        public ThreeOSPose pose;
        public uint flags;
        public uint reserved0;
        public uint reserved1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropDomeState
    {
        public uint active;
        public float scaleRatio;
        public ThreeOSPose pose;
        public float radiusM;
        public uint reserved0;
        public uint reserved1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropDoubleProxyState
    {
        public ulong id;
        public uint active;
        public float scaleRatio;
        public ThreeOSAabb farAabb;
        public ThreeOSAabb nearAabb;
    }

    [System.Flags]
    public enum TransformFlags : uint
    {
        None = 0,
        Portal = 1 << 0,
        Kinematic = 1 << 1,
        FloorClamp = 1 << 2,
    }

    public enum KineticPhase : uint
    {
        Idle = 0,
        Possessed = 1,
        Coasting = 2,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropKineticState
    {
        public ulong entityId;
        public uint phase;
        public float velX;
        public float velY;
        public float velZ;
        public float floorY;
        public uint reserved0;
        public uint reserved1;
        public uint reserved2;
        public uint reserved3;
        public uint reserved4;
    }

    /// <summary>Kernel stick-velocity debug ray (OpenXR meters). Host draws only.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropStickDebug
    {
        public uint active;
        public uint inDeadzone;
        public float magnitude;
        public float unused0;
        public ThreeOSVec3 stick;
        public ThreeOSVec3 rayOrigin;
        public ThreeOSVec3 rayTip;
        public uint reserved0;
        public uint reserved1;
        public uint reserved2;
    }
}
