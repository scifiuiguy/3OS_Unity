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
        public const uint AbiVersion = 6;
        public const int InputFrameBytes = 192;
        public const int TransformDeltaBytes = 48;
        public const int DomeStateBytes = 48;
        public const int DoubleProxyStateBytes = 64;
        public const int KineticStateBytes = 48;
        public const int StickDebugBytes = 64;
        public const int WorkspaceItemBytes = 200;
        public const int SnapStateBytes = 64;
        public const int StorageEventBytes = 344;
        public const int HandSampleBytes = 128;
        public const int PointCursorBytes = 32;
        public const int InteractionIntentsBytes = 16;
        public const int VoodooFieldParamsBytes = 32;
        public const int VoodooBoundingBoxBytes = 80;
        public const int VoodooSessionBytes = 176;
        public const int VoodooDragArrowBytes = 64;
        public const int CentroidBytes = 64;
        public const int VoodooSnapNodeBytes = 48;
        public const int VoodooPortalBytes = 200;
        public const int VoodooGhostBytes = 88;
        public const int MultiChoiceSlotBytes = 40;
        public const int MultiChoiceStateBytes = 528;
        public const int DriveRootBytes = 40;

        public static void AssertLayout()
        {
            var inputSize = Marshal.SizeOf<InteropInputFrame>();
            var deltaSize = Marshal.SizeOf<InteropTransformDelta>();
            var domeSize = Marshal.SizeOf<InteropDomeState>();
            var proxySize = Marshal.SizeOf<InteropDoubleProxyState>();
            var kineticSize = Marshal.SizeOf<InteropKineticState>();
            var stickDebugSize = Marshal.SizeOf<InteropStickDebug>();
            var workspaceSize = Marshal.SizeOf<InteropWorkspaceItem>();
            var snapSize = Marshal.SizeOf<InteropSnapState>();
            var storageEventSize = Marshal.SizeOf<InteropStorageEvent>();
            var handSize = Marshal.SizeOf<InteropHandSample>();
            var cursorSize = Marshal.SizeOf<InteropPointCursor>();
            var intentsSize = Marshal.SizeOf<InteropInteractionIntents>();
            var voodooParamsSize = Marshal.SizeOf<InteropVoodooFieldParams>();
            var voodooBbSize = Marshal.SizeOf<InteropVoodooBoundingBox>();
            var voodooSessionSize = Marshal.SizeOf<InteropVoodooSession>();
            var voodooArrowSize = Marshal.SizeOf<InteropVoodooDragArrow>();
            var centroidSize = Marshal.SizeOf<InteropCentroid>();
            var voodooSnapNodeSize = Marshal.SizeOf<InteropVoodooSnapNode>();
            var voodooPortalSize = Marshal.SizeOf<InteropVoodooPortal>();
            var voodooGhostSize = Marshal.SizeOf<InteropVoodooGhost>();
            var mcSlotSize = Marshal.SizeOf<InteropMultiChoiceSlot>();
            var mcStateSize = Marshal.SizeOf<InteropMultiChoiceState>();
            var driveRootSize = Marshal.SizeOf<InteropDriveRoot>();
            Debug.Assert(inputSize == InputFrameBytes, $"InteropInputFrame size {inputSize} != {InputFrameBytes}");
            Debug.Assert(deltaSize == TransformDeltaBytes, $"InteropTransformDelta size {deltaSize} != {TransformDeltaBytes}");
            Debug.Assert(domeSize == DomeStateBytes, $"InteropDomeState size {domeSize} != {DomeStateBytes}");
            Debug.Assert(proxySize == DoubleProxyStateBytes, $"InteropDoubleProxyState size {proxySize} != {DoubleProxyStateBytes}");
            Debug.Assert(kineticSize == KineticStateBytes, $"InteropKineticState size {kineticSize} != {KineticStateBytes}");
            Debug.Assert(stickDebugSize == StickDebugBytes, $"InteropStickDebug size {stickDebugSize} != {StickDebugBytes}");
            Debug.Assert(workspaceSize == WorkspaceItemBytes, $"InteropWorkspaceItem size {workspaceSize} != {WorkspaceItemBytes}");
            Debug.Assert(snapSize == SnapStateBytes, $"InteropSnapState size {snapSize} != {SnapStateBytes}");
            Debug.Assert(storageEventSize == StorageEventBytes, $"InteropStorageEvent size {storageEventSize} != {StorageEventBytes}");
            Debug.Assert(handSize == HandSampleBytes, $"InteropHandSample size {handSize} != {HandSampleBytes}");
            Debug.Assert(cursorSize == PointCursorBytes, $"InteropPointCursor size {cursorSize} != {PointCursorBytes}");
            Debug.Assert(intentsSize == InteractionIntentsBytes, $"InteropInteractionIntents size {intentsSize} != {InteractionIntentsBytes}");
            Debug.Assert(voodooParamsSize == VoodooFieldParamsBytes, $"InteropVoodooFieldParams size {voodooParamsSize} != {VoodooFieldParamsBytes}");
            Debug.Assert(voodooBbSize == VoodooBoundingBoxBytes, $"InteropVoodooBoundingBox size {voodooBbSize} != {VoodooBoundingBoxBytes}");
            Debug.Assert(voodooSessionSize == VoodooSessionBytes, $"InteropVoodooSession size {voodooSessionSize} != {VoodooSessionBytes}");
            Debug.Assert(voodooArrowSize == VoodooDragArrowBytes, $"InteropVoodooDragArrow size {voodooArrowSize} != {VoodooDragArrowBytes}");
            Debug.Assert(centroidSize == CentroidBytes, $"InteropCentroid size {centroidSize} != {CentroidBytes}");
            Debug.Assert(voodooSnapNodeSize == VoodooSnapNodeBytes, $"InteropVoodooSnapNode size {voodooSnapNodeSize} != {VoodooSnapNodeBytes}");
            Debug.Assert(voodooPortalSize == VoodooPortalBytes, $"InteropVoodooPortal size {voodooPortalSize} != {VoodooPortalBytes}");
            Debug.Assert(voodooGhostSize == VoodooGhostBytes, $"InteropVoodooGhost size {voodooGhostSize} != {VoodooGhostBytes}");
            Debug.Assert(mcSlotSize == MultiChoiceSlotBytes, $"InteropMultiChoiceSlot size {mcSlotSize} != {MultiChoiceSlotBytes}");
            Debug.Assert(mcStateSize == MultiChoiceStateBytes, $"InteropMultiChoiceState size {mcStateSize} != {MultiChoiceStateBytes}");
            Debug.Assert(driveRootSize == DriveRootBytes, $"InteropDriveRoot size {driveRootSize} != {DriveRootBytes}");
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
        LeftTernary = 1 << 5,
        RightTernary = 1 << 6,
    }

    [System.Flags]
    public enum HandTipFlags : uint
    {
        None = 0,
        Thumb = 1 << 0,
        Index = 1 << 1,
        Middle = 1 << 2,
        Ring = 1 << 3,
        IndexProximal = 1 << 4,
    }

    public enum PalmMode : uint
    {
        Up = 0,
        Sideways = 1,
        Down = 2,
    }

    public enum PinchChannel : uint
    {
        None = 0,
        Primary = 1,
        Secondary = 2,
        Ternary = 3,
    }

    [System.Flags]
    public enum InteractionIntentFlags : uint
    {
        None = 0,
        SelectPulse = 1 << 0,
        DragActive = 1 << 1,
        VoodooSnap = 1 << 2,
        PrimaryHeld = 1 << 3,
        SecondaryHeld = 1 << 4,
        TernaryHeld = 1 << 5,
        VoodooFree = 1 << 6,
        MindPalaceMenu = 1 << 7,
    }

    public enum MultiChoiceMode : uint
    {
        Closed = 0,
        DrivePicker = 1,
        FolderContext = 2,
        Agent = 3,
    }

    public enum MultiChoiceSlotKind : uint
    {
        Empty = 0,
        DriveRoot = 1,
        FavoriteBox = 2,
        ActionFavorite = 3,
        ConfirmAdd = 4,
        ConfirmJump = 5,
        ActionSpeak = 6,
        ActionDemoChrome = 7,
    }

    public enum MultiChoiceAction : uint
    {
        None = 0,
        Add = 1,
        JumpToRoot = 2,
        ToggleFavorite = 3,
        Speak = 4,
    }

    public enum VoodooMode : uint
    {
        None = 0,
        Snap = 1,
        FreeRange = 2,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct ThreeOSIVec3
    {
        public int x;
        public int y;
        public int z;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropVoodooFieldParams
    {
        public float voxelSizeM;
        public ThreeOSIVec3 nearBbVoxels;
        public float fieldRadiusM;
        public float fieldHeightM;
        public float armReachM;
        public uint reserved0;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropVoodooBoundingBox
    {
        public uint open;
        public uint reserved0;
        public ThreeOSPose pose;
        public ThreeOSVec3 extents;
        public ThreeOSIVec3 aspectVoxels;
        public uint reserved1;
        public ulong linkedCentroidId;
        public uint reserved2;
        public uint reserved3;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropVoodooSession
    {
        public uint mode;
        public uint open;
        public uint hand;
        public uint reserved0;
        public InteropVoodooBoundingBox farBb;
        public InteropVoodooBoundingBox nearBb;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropVoodooDragArrow
    {
        public uint active;
        public uint inDeadzone;
        public float magnitude;
        public float unused0;
        public ThreeOSVec3 stick;
        public ThreeOSVec3 rayOrigin;
        public ThreeOSVec3 rayTip;
        public uint deadzoneArmed;
        public uint reserved1;
        public uint reserved2;
    }

    /// <summary>MR enclosure solid OBB (48 bytes). Finite WorldMesh collider volume.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropVoodooEnclosureBox
    {
        public ThreeOSVec3 center;
        public ThreeOSVec3 halfExtents;
        public ThreeOSQuat orientation;
        public uint kind;
        public uint pad;
    }

    public enum CentroidKind : uint
    {
        None = 0,
        MindPalace = 1,
        Box = 2,
        Temporary = 3,
        FocusAnchor = 4,
    }

    /// <summary>CharSet.Ansi + ByValTStr matches native char[16] (IL2CPP-safe).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
    public struct InteropCentroid
    {
        public ulong id;
        public uint kind;
        public uint memberCount;
        public ulong linkedEntityId;
        public ThreeOSVec3 position;
        public float unused0;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string name;
        public uint reserved0;
        public uint reserved1;
    }

    /// <summary>Task 6.3 snap-mode node near the yellow arrow (48 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropVoodooSnapNode
    {
        public ulong centroidId;
        public uint kind;
        public uint snapped;
        public ThreeOSVec3 worldPosition;
        public float radiusM;
        public ThreeOSVec3 fieldPosition;
        public float unused0;
    }

    /// <summary>Task 6.4 parked far+near portal (200 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropVoodooPortal
    {
        public ulong id;
        public uint mode;
        public uint creatingHand;
        public InteropVoodooBoundingBox farBb;
        public InteropVoodooBoundingBox nearBb;
        public ulong persistentCentroidId;
        public ulong temporaryCentroidId;
        public uint ghostCount;
        public uint reserved0;
    }

    /// <summary>Task 6.4 near-portal ghost (88 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropVoodooGhost
    {
        public ulong entityId;
        public uint isParentBox;
        public uint reserved0;
        public ThreeOSPose farPose;
        public ThreeOSPose nearPose;
        public float scale;
        public float unused0;
        public float unused1;
        public float unused2;
    }

    /// <summary>128-byte Phase 0.5 hand sample (OpenXR meters).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropHandSample
    {
        public uint tracked;
        public uint tipFlags;
        public ThreeOSVec3 thumbTip;
        public ThreeOSVec3 indexTip;
        public ThreeOSVec3 middleTip;
        public ThreeOSVec3 ringTip;
        public ThreeOSVec3 palmNormal;
        public uint palmMode;
        public uint palmModeValid;
        public float primaryValue;
        public float secondaryValue;
        public float ternaryValue;
        public uint primaryPressed;
        public uint secondaryPressed;
        public uint ternaryPressed;
        public ThreeOSVec3 indexProximal;
        public uint reserved3;
        public uint reserved4;
        public uint reserved5;
        public uint reserved6;
    }

    /// <summary>32-byte point cursor probe.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropPointCursor
    {
        public uint active;
        public uint drivingChannel;
        public float scale;
        public float drivingDistanceM;
        public ThreeOSVec3 position;
        public uint palmMode;
    }

    /// <summary>16-byte aggregated interaction intents.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropInteractionIntents
    {
        public uint flags;
        public uint palmMode;
        public uint activeChannel;
        public uint handedness;
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
        public uint deadzoneArmed;
        public uint reserved1;
        public uint reserved2;
    }

    /// <summary>CharSet.Ansi + ByValTStr matches native char[] (IL2CPP-safe vs ByValArray).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
    public struct InteropWorkspaceItem
    {
        public ulong entityId;
        public uint kind;
        public uint flags;
        public ThreeOSPose pose;
        public uint childCount;
        public uint boxVisual;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string secondaryLabel;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string glyphId;
        public float tintR;
        public float tintG;
        public float tintB;
        public float tintA;
        public uint reserved0;

        public string Name => name ?? string.Empty;
        public string SecondaryLabel => secondaryLabel ?? string.Empty;
        public string GlyphId => glyphId ?? string.Empty;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct InteropSnapState
    {
        public uint pending;
        public uint reserved0;
        public ulong objectId;
        public ulong boxId;
        public ThreeOSPose holdPose;
        public uint reserved1;
        public uint reserved2;
        public uint reserved3;
    }

    /// <summary>
    /// CharSet.Ansi + ByValTStr matches native char[160] paths (IL2CPP-safe).
    /// Prefer this over ByValArray byte[] which often arrives empty on Android.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
    public struct InteropStorageEvent
    {
        public uint type;
        public uint awaitingAck;
        public ulong objectId;
        public ulong boxId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 160)]
        public string sourceRel;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 160)]
        public string destRel;

        public string SourceRel => sourceRel ?? string.Empty;
        public string DestRel => destRel ?? string.Empty;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
    public struct InteropMultiChoiceSlot
    {
        public uint kind;
        public uint enabled;
        public ulong targetId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 24)]
        public string label;
        public string Label => label ?? string.Empty;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
    public struct InteropMultiChoiceState
    {
        public uint open;
        public uint mode;
        public uint section;
        public uint hand;
        public ThreeOSPose center;
        public float minRadiusM;
        public float maxRadiusM;
        public int hoveredSlot;
        public uint pageIndex;
        public uint pageBackEnabled;
        public uint pageForwardEnabled;
        public uint confirmOpen;
        public int confirmHovered;
        public uint reserved0;
        public ThreeOSPose confirmCenter;
        public float confirmMinM;
        public float confirmMaxM;
        public uint reserved1;
        public ulong contextBoxId;
        public ulong pendingTargetId;
        public uint pendingKind;
        public uint cursorInside;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string title;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string hoverLabel;
        public InteropMultiChoiceSlot slot0;
        public InteropMultiChoiceSlot slot1;
        public InteropMultiChoiceSlot slot2;
        public InteropMultiChoiceSlot slot3;
        public InteropMultiChoiceSlot slot4;
        public InteropMultiChoiceSlot slot5;
        public InteropMultiChoiceSlot slot6;
        public InteropMultiChoiceSlot slot7;

        public string Title => title ?? string.Empty;
        public string HoverLabel => hoverLabel ?? string.Empty;

        public InteropMultiChoiceSlot SlotAt(int i) => i switch
        {
            0 => slot0, 1 => slot1, 2 => slot2, 3 => slot3,
            4 => slot4, 5 => slot5, 6 => slot6, 7 => slot7,
            _ => default
        };
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4, CharSet = CharSet.Ansi)]
    public struct InteropDriveRoot
    {
        public ulong id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string name;
        public string Name => name ?? string.Empty;
    }
}
