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
        private static extern int threeos_kinematics_possess_with_hand(ulong entityId, ref ThreeOSPose objectPose,
            uint hand);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_set_drive_hand(uint hand);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_release();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_get_state(out InteropKineticState outState);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_get_stick_debug(out InteropStickDebug outDebug);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_kinematics_set_object_pose(ref ThreeOSPose objectPose);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_workspace_item();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_snap_state();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_storage_event();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void threeos_storage_clear();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_add_object([MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPStr)] string relativePath, out ulong outEntityId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_add_box([MarshalAs(UnmanagedType.LPStr)] string name,
            [MarshalAs(UnmanagedType.LPStr)] string relativePath, uint childCount, out ulong outEntityId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_layout_demo(ref ThreeOSPose headPose);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_storage_item_count();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_get_item(uint index, out InteropWorkspaceItem outItem);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_get_snap(out InteropSnapState outSnap);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_poll_event(out InteropStorageEvent outEvent);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_try_commit_insert();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_insert_into(ulong objectId, ulong boxId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_ack_event(int success);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_set_box_open(ulong boxId, int open);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_set_parent_box(ulong entityId, ulong parentBoxId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_set_in_field(ulong entityId, int inField);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_get_parent_box(ulong entityId, out ulong outParentBoxId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_expand_box(ulong boxId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_collapse_box(ulong boxId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_glyph_install_pack(byte[] bytes, uint byteCount);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_selection_get(out ulong outEntityId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_selection_set(ulong entityId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void threeos_selection_clear();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_hand_sample();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_point_cursor();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_interaction_intents();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_interaction_set_hands(ref InteropHandSample left,
            ref InteropHandSample right);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_interaction_get_cursor(uint hand, out InteropPointCursor outCursor);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_interaction_get_intents(out InteropInteractionIntents outIntents);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_interaction_set_pinch_thresh_m(float meters);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_voodoo_field_params();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_voodoo_bounding_box();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_voodoo_session();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_voodoo_drag_arrow();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_get_field_params(out InteropVoodooFieldParams outParams);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_set_field_params(ref InteropVoodooFieldParams parameters);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_set_enclosure_boxes(
            [In] InteropVoodooEnclosureBox[] boxes, uint count);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_set_room_bounds(
            ref ThreeOSVec3 minXyz, ref ThreeOSVec3 maxXyz, int enabled);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_get_session(uint hand, out InteropVoodooSession outSession);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_get_drag_arrow(uint hand, out InteropVoodooDragArrow outArrow);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_centroid();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void threeos_mind_palace_clear();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_mind_palace_register(
            [MarshalAs(UnmanagedType.LPStr)] string name,
            ref ThreeOSPose anchor,
            out ulong outMindPalaceId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_mind_palace_set_anchor(ulong mindPalaceId, ref ThreeOSPose anchor);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_set_mind_palace(ulong entityId, ulong mindPalaceId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_layout_mind_palace(ulong mindPalaceId, ref ThreeOSPose center);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_centroid_count();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_centroid_get(uint index, out InteropCentroid outCentroid);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_voodoo_snap_node();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_voodoo_snap_node_count(uint hand);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_get_snap_node(uint hand, uint index,
            out InteropVoodooSnapNode outNode);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_voodoo_portal();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_voodoo_ghost();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_voodoo_portal_count();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_get_portal(uint index, out InteropVoodooPortal outPortal);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_voodoo_portal_ghost_count(ulong portalId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_get_portal_ghost(ulong portalId, uint index,
            out InteropVoodooGhost outGhost);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_dismiss_portal(ulong portalId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_voodoo_dismiss_gesture_result();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_voodoo_creation_allowed();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_voodoo_point_in_near_bb(float x, float y, float z);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong threeos_voodoo_portal_at_near_point(float x, float y, float z);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong threeos_voodoo_portal_for_ghost(ulong entityId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_ghost_drag_begin(uint hand, ulong entityId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_ghost_drag_end(uint hand, uint releaseInsideNearBb);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_voodoo_ghost_drag_active(uint hand);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong threeos_voodoo_ghost_drag_entity(uint hand);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_get_ghost_drag_arrow(uint hand,
            out InteropVoodooDragArrow outArrow);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_summon(ulong entityId, float x, float y, float z);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_cut_arm(ulong portalId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void threeos_voodoo_cut_cancel();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong threeos_voodoo_cut_portal();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_voodoo_cut_is_armed(ulong portalId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_cut_paste_at(float x, float y, float z);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_cut_paste_into(ulong boxId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_multi_choice_state();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_multi_choice_slot();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_sizeof_drive_root();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_drive_roots_set(
            [In] InteropDriveRoot[] roots, uint count);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_favorite_set(ulong boxId, uint favored);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint threeos_favorite_get(ulong boxId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_multichoice_open_drive(uint hand, ref ThreeOSPose center);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_multichoice_open_folder(uint hand, ref ThreeOSPose center,
            ulong boxId);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_multichoice_open_agent(uint hand, ref ThreeOSPose center);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ulong threeos_focus_anchor_register(string name, ref ThreeOSPose pose);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_focus_anchor_set_pose(ulong id, ref ThreeOSPose pose);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_focus_anchor_clear(ulong id);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_voodoo_near_rest_pose(ref ThreeOSPose head,
            out ThreeOSPose outPose);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void threeos_multichoice_close();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_multichoice_tick(float cursorX, float cursorY, float cursorZ);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_multichoice_confirm();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_multichoice_get_state(out InteropMultiChoiceState outState);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_multichoice_poll_action(out uint outAction,
            out ulong outTargetId, out uint outKind);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_mind_palace_layout_next(ulong mindPalaceId, ref ThreeOSPose head,
            [MarshalAs(UnmanagedType.LPStr)] string displayName);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_storage_clear_field();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int threeos_field_resolve_overlaps(ref ThreeOSPose head);

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

                if (NativeAbiVersion >= 4)
                {
                    if (threeos_sizeof_workspace_item() != InteropStructs.WorkspaceItemBytes ||
                        threeos_sizeof_snap_state() != InteropStructs.SnapStateBytes ||
                        threeos_sizeof_storage_event() != InteropStructs.StorageEventBytes)
                    {
                        LastError = "Storage ABI size mismatch";
                        IsLoaded = false;
                        Debug.LogError($"[3OS] {LastError}");
                        return;
                    }
                }

                if (NativeAbiVersion >= 5)
                {
                    if (threeos_sizeof_hand_sample() != InteropStructs.HandSampleBytes ||
                        threeos_sizeof_point_cursor() != InteropStructs.PointCursorBytes ||
                        threeos_sizeof_interaction_intents() != InteropStructs.InteractionIntentsBytes)
                    {
                        LastError = "Hand-interaction ABI size mismatch";
                        IsLoaded = false;
                        Debug.LogError($"[3OS] {LastError}");
                        return;
                    }
                }

                if (NativeAbiVersion >= 6)
                {
                    if (threeos_sizeof_voodoo_field_params() != InteropStructs.VoodooFieldParamsBytes ||
                        threeos_sizeof_voodoo_bounding_box() != InteropStructs.VoodooBoundingBoxBytes ||
                        threeos_sizeof_voodoo_session() != InteropStructs.VoodooSessionBytes ||
                        threeos_sizeof_voodoo_drag_arrow() != InteropStructs.VoodooDragArrowBytes ||
                        threeos_sizeof_centroid() != InteropStructs.CentroidBytes ||
                        threeos_sizeof_voodoo_snap_node() != InteropStructs.VoodooSnapNodeBytes ||
                        threeos_sizeof_voodoo_portal() != InteropStructs.VoodooPortalBytes ||
                        threeos_sizeof_voodoo_ghost() != InteropStructs.VoodooGhostBytes ||
                        threeos_sizeof_multi_choice_state() != InteropStructs.MultiChoiceStateBytes ||
                        threeos_sizeof_multi_choice_slot() != InteropStructs.MultiChoiceSlotBytes ||
                        threeos_sizeof_drive_root() != InteropStructs.DriveRootBytes)
                    {
                        LastError = "Voodoo/centroid/portal/multichoice ABI size mismatch";
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

        public bool SetHands(ref InteropHandSample left, ref InteropHandSample right)
        {
            if (!IsLoaded || NativeAbiVersion < 5) return false;
            try
            {
                var rc = threeos_interaction_set_hands(ref left, ref right);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool TryGetCursor(uint hand, out InteropPointCursor cursor)
        {
            cursor = default;
            if (!IsLoaded || NativeAbiVersion < 5) return false;
            try
            {
                var rc = threeos_interaction_get_cursor(hand, out cursor);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool TryGetIntents(out InteropInteractionIntents intents)
        {
            intents = default;
            if (!IsLoaded || NativeAbiVersion < 5) return false;
            try
            {
                var rc = threeos_interaction_get_intents(out intents);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool TryGetVoodooSession(uint hand, out InteropVoodooSession session)
        {
            session = default;
            if (!IsLoaded || NativeAbiVersion < 6) return false;
            try
            {
                var rc = threeos_voodoo_get_session(hand, out session);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool TryGetVoodooDragArrow(uint hand, out InteropVoodooDragArrow arrow)
        {
            arrow = default;
            if (!IsLoaded || NativeAbiVersion < 6) return false;
            try
            {
                var rc = threeos_voodoo_get_drag_arrow(hand, out arrow);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool TryGetVoodooFieldParams(out InteropVoodooFieldParams parameters)
        {
            parameters = default;
            if (!IsLoaded || NativeAbiVersion < 6) return false;
            try
            {
                var rc = threeos_voodoo_get_field_params(out parameters);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool SetVoodooFieldParams(InteropVoodooFieldParams parameters) =>
            IsLoaded && NativeAbiVersion >= 6 &&
            Call(() => threeos_voodoo_set_field_params(ref parameters));

        /// <summary>
        /// MR room AABB (OpenXR). Pass enabled=false to clear.
        /// </summary>
        public bool SetVoodooRoomBounds(ThreeOSVec3 minXyz, ThreeOSVec3 maxXyz, bool enabled)
        {
            if (!IsLoaded || NativeAbiVersion < 6)
                return false;
            try
            {
                var rc = threeos_voodoo_set_room_bounds(ref minXyz, ref maxXyz, enabled ? 1 : 0);
                if (rc != 0)
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

        /// <summary>
        /// Publish MR WorldMesh solid OBBs (OpenXR). Empty or null clears.
        /// </summary>
        public bool SetVoodooEnclosureBoxes(InteropVoodooEnclosureBox[] boxes)
        {
            if (!IsLoaded || NativeAbiVersion < 6)
                return false;
            try
            {
                if (boxes == null || boxes.Length == 0)
                {
                    var rcClear = threeos_voodoo_set_enclosure_boxes(null, 0);
                    if (rcClear != 0)
                    {
                        LastError = ReadLastError();
                        return false;
                    }

                    LastError = string.Empty;
                    return true;
                }

                var rc = threeos_voodoo_set_enclosure_boxes(boxes, (uint)boxes.Length);
                if (rc != 0)
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

        public void MindPalaceClear()
        {
            if (!IsLoaded || NativeAbiVersion < 6) return;
            try { threeos_mind_palace_clear(); }
            catch (Exception ex) { LastError = ex.Message; }
        }

        public bool TryRegisterMindPalace(string name, ThreeOSPose anchor, out ulong mindPalaceId)
        {
            mindPalaceId = 0;
            if (!IsLoaded || NativeAbiVersion < 6) return false;
            try
            {
                var rc = threeos_mind_palace_register(name ?? string.Empty, ref anchor, out mindPalaceId);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool SetMindPalaceRoot(ulong entityId, ulong mindPalaceId) =>
            IsLoaded && NativeAbiVersion >= 6 &&
            Call(() => threeos_storage_set_mind_palace(entityId, mindPalaceId));

        public bool LayoutMindPalace(ulong mindPalaceId, ThreeOSPose center) =>
            IsLoaded && NativeAbiVersion >= 6 &&
            Call(() => threeos_storage_layout_mind_palace(mindPalaceId, ref center));

        public uint CentroidCount =>
            IsLoaded && NativeAbiVersion >= 6 ? threeos_centroid_count() : 0u;

        public bool TryGetCentroid(uint index, out InteropCentroid centroid)
        {
            centroid = default;
            if (!IsLoaded || NativeAbiVersion < 6) return false;
            try
            {
                var rc = threeos_centroid_get(index, out centroid);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public uint VoodooSnapNodeCount(uint hand) =>
            IsLoaded && NativeAbiVersion >= 6 ? threeos_voodoo_snap_node_count(hand) : 0u;

        public bool TryGetVoodooSnapNode(uint hand, uint index, out InteropVoodooSnapNode node)
        {
            node = default;
            if (!IsLoaded || NativeAbiVersion < 6) return false;
            try
            {
                var rc = threeos_voodoo_get_snap_node(hand, index, out node);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public uint VoodooPortalCount =>
            IsLoaded && NativeAbiVersion >= 6 ? threeos_voodoo_portal_count() : 0u;

        public bool VoodooCreationAllowed =>
            IsLoaded && NativeAbiVersion >= 6 && threeos_voodoo_creation_allowed() != 0;

        public bool TryGetVoodooPortal(uint index, out InteropVoodooPortal portal)
        {
            portal = default;
            if (!IsLoaded || NativeAbiVersion < 6) return false;
            try
            {
                var rc = threeos_voodoo_get_portal(index, out portal);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public uint VoodooPortalGhostCount(ulong portalId) =>
            IsLoaded && NativeAbiVersion >= 6 ? threeos_voodoo_portal_ghost_count(portalId) : 0u;

        public bool TryGetVoodooPortalGhost(ulong portalId, uint index, out InteropVoodooGhost ghost)
        {
            ghost = default;
            if (!IsLoaded || NativeAbiVersion < 6) return false;
            try
            {
                var rc = threeos_voodoo_get_portal_ghost(portalId, index, out ghost);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool DismissVoodooPortal(ulong portalId) =>
            IsLoaded && NativeAbiVersion >= 6 && Call(() => threeos_voodoo_dismiss_portal(portalId));

        /// <summary>0 none; 1 single-hand dismiss; 2 dismiss-all — last <c>threeos_tick</c>.</summary>
        public uint VoodooDismissGestureResult =>
            IsLoaded && NativeAbiVersion >= 6 ? threeos_voodoo_dismiss_gesture_result() : 0u;

        public bool PointInAnyNearBb(Vector3 openXrPoint) =>
            IsLoaded && NativeAbiVersion >= 6 &&
            threeos_voodoo_point_in_near_bb(openXrPoint.x, openXrPoint.y, openXrPoint.z) != 0;

        public ulong PortalAtNearPoint(Vector3 openXrPoint) =>
            IsLoaded && NativeAbiVersion >= 6
                ? threeos_voodoo_portal_at_near_point(openXrPoint.x, openXrPoint.y, openXrPoint.z)
                : 0UL;

        public ulong PortalForGhost(ulong entityId) =>
            IsLoaded && NativeAbiVersion >= 6 ? threeos_voodoo_portal_for_ghost(entityId) : 0UL;

        public bool GhostDragBegin(uint hand, ulong entityId) =>
            IsLoaded && NativeAbiVersion >= 6 &&
            Call(() => threeos_voodoo_ghost_drag_begin(hand, entityId));

        public bool GhostDragEnd(uint hand, bool releaseInsideNearBb) =>
            IsLoaded && NativeAbiVersion >= 6 &&
            Call(() => threeos_voodoo_ghost_drag_end(hand, releaseInsideNearBb ? 1u : 0u));

        public bool GhostDragActive(uint hand) =>
            IsLoaded && NativeAbiVersion >= 6 && threeos_voodoo_ghost_drag_active(hand) != 0;

        public ulong GhostDragEntity(uint hand) =>
            IsLoaded && NativeAbiVersion >= 6 ? threeos_voodoo_ghost_drag_entity(hand) : 0UL;

        public bool TryGetGhostDragArrow(uint hand, out InteropVoodooDragArrow arrow)
        {
            arrow = default;
            if (!IsLoaded || NativeAbiVersion < 6) return false;
            try
            {
                var rc = threeos_voodoo_get_ghost_drag_arrow(hand, out arrow);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool VoodooSummon(ulong entityId, Vector3 openXrPoint) =>
            IsLoaded && NativeAbiVersion >= 6 &&
            Call(() => threeos_voodoo_summon(entityId, openXrPoint.x, openXrPoint.y, openXrPoint.z));

        public bool VoodooCutArm(ulong portalId) =>
            IsLoaded && NativeAbiVersion >= 6 && Call(() => threeos_voodoo_cut_arm(portalId));

        public void VoodooCutCancel()
        {
            if (IsLoaded && NativeAbiVersion >= 6)
            {
                threeos_voodoo_cut_cancel();
            }
        }

        public ulong VoodooCutPortal =>
            IsLoaded && NativeAbiVersion >= 6 ? threeos_voodoo_cut_portal() : 0UL;

        public bool VoodooCutIsArmed(ulong portalId) =>
            IsLoaded && NativeAbiVersion >= 6 && threeos_voodoo_cut_is_armed(portalId) != 0;

        public bool VoodooCutActive => VoodooCutPortal != 0UL;

        public bool VoodooCutPasteAt(Vector3 openXrPoint) =>
            IsLoaded && NativeAbiVersion >= 6 &&
            Call(() => threeos_voodoo_cut_paste_at(openXrPoint.x, openXrPoint.y, openXrPoint.z));

        public bool VoodooCutPasteInto(ulong boxId) =>
            IsLoaded && NativeAbiVersion >= 6 && Call(() => threeos_voodoo_cut_paste_into(boxId));

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

        /// <summary>Possess and latch stick/drag to hand (0=left, 1=right) atomically.</summary>
        public bool PossessWithHand(ulong entityId, ThreeOSPose objectPose, int hand) =>
            Call(() => threeos_kinematics_possess_with_hand(entityId, ref objectPose,
                hand < 0 ? 0u : (uint)(hand > 1 ? 1 : hand)));

        /// <summary>0=left, 1=right — latch stick/drag to this hand after <see cref="Possess"/>.</summary>
        public bool SetDriveHand(int hand) =>
            Call(() => threeos_kinematics_set_drive_hand(hand < 0 ? 0u : (uint)hand));

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

        public void StorageClear()
        {
            if (!IsLoaded) return;
            try { threeos_storage_clear(); }
            catch (Exception ex) { LastError = ex.Message; }
        }

        public bool StorageAddObject(string name, string relativePath, out ulong entityId)
        {
            entityId = 0;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_storage_add_object(name, relativePath, out entityId) != 0)
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

        public bool StorageAddBox(string name, string relativePath, uint childCount, out ulong entityId)
        {
            entityId = 0;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_storage_add_box(name, relativePath, childCount, out entityId) != 0)
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

        public bool StorageLayoutDemo(ThreeOSPose headPose) =>
            Call(() => threeos_storage_layout_demo(ref headPose));

        public uint StorageItemCount()
        {
            if (!IsLoaded) return 0;
            try { return threeos_storage_item_count(); }
            catch { return 0; }
        }

        public bool TryGetWorkspaceItem(uint index, out InteropWorkspaceItem item)
        {
            item = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_storage_get_item(index, out item) != 0)
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

        public bool TryGetSnap(out InteropSnapState snap)
        {
            snap = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_storage_get_snap(out snap) != 0)
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

        public bool TryPollStorageEvent(out InteropStorageEvent ev)
        {
            ev = default;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_storage_poll_event(out ev) != 0)
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

        public bool TryCommitInsert() => Call(threeos_storage_try_commit_insert);

        public bool InsertInto(ulong objectId, ulong boxId) =>
            Call(() => threeos_storage_insert_into(objectId, boxId));

        public bool AckStorageEvent(bool success) =>
            Call(() => threeos_storage_ack_event(success ? 1 : 0));

        public bool SetBoxOpen(ulong boxId, bool open) =>
            Call(() => threeos_storage_set_box_open(boxId, open ? 1 : 0));

        public bool StorageSetParentBox(ulong entityId, ulong parentBoxId) =>
            Call(() => threeos_storage_set_parent_box(entityId, parentBoxId));

        public bool StorageSetInField(ulong entityId, bool inField) =>
            Call(() => threeos_storage_set_in_field(entityId, inField ? 1 : 0));

        public bool TryGetParentBox(ulong entityId, out ulong parentBoxId)
        {
            parentBoxId = 0;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_storage_get_parent_box(entityId, out parentBoxId) != 0)
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

        public bool ExpandBox(ulong boxId) =>
            Call(() => threeos_storage_expand_box(boxId));

        public bool CollapseBox(ulong boxId) =>
            Call(() => threeos_storage_collapse_box(boxId));

        public bool InstallGlyphPack(byte[] bytes)
        {
            if (!IsLoaded || bytes == null || bytes.Length == 0) return false;
            try
            {
                if (threeos_glyph_install_pack(bytes, (uint)bytes.Length) != 0)
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

        public bool TryGetSelection(out ulong entityId)
        {
            entityId = 0;
            if (!IsLoaded) return false;
            try
            {
                if (threeos_selection_get(out entityId) != 0)
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

        public bool SetSelection(ulong entityId) =>
            Call(() => threeos_selection_set(entityId));

        public void ClearSelection()
        {
            if (!IsLoaded) return;
            try { threeos_selection_clear(); }
            catch (Exception ex) { LastError = ex.Message; }
        }

        public bool SetDriveRoots(InteropDriveRoot[] roots) =>
            Call(() => threeos_drive_roots_set(roots ?? Array.Empty<InteropDriveRoot>(),
                (uint)(roots?.Length ?? 0)));

        public bool SetFavorite(ulong boxId, bool favored) =>
            Call(() => threeos_favorite_set(boxId, favored ? 1u : 0u));

        public bool IsFavorite(ulong boxId) =>
            IsLoaded && NativeAbiVersion >= 6 && threeos_favorite_get(boxId) != 0;

        public bool OpenMultiChoiceDrive(uint hand, ThreeOSPose center) =>
            Call(() => threeos_multichoice_open_drive(hand, ref center));

        public bool OpenMultiChoiceFolder(uint hand, ThreeOSPose center, ulong boxId) =>
            Call(() => threeos_multichoice_open_folder(hand, ref center, boxId));

        public bool OpenMultiChoiceAgent(uint hand, ThreeOSPose center) =>
            Call(() => threeos_multichoice_open_agent(hand, ref center));

        public ulong RegisterFocusAnchor(string name, ThreeOSPose pose)
        {
            if (!IsLoaded || NativeAbiVersion < 6) return 0;
            try { return threeos_focus_anchor_register(name ?? "focus", ref pose); }
            catch (Exception ex) { LastError = ex.Message; return 0; }
        }

        public bool SetFocusAnchorPose(ulong id, ThreeOSPose pose) =>
            id != 0 && Call(() => threeos_focus_anchor_set_pose(id, ref pose));

        public bool ClearFocusAnchor(ulong id) =>
            id != 0 && Call(() => threeos_focus_anchor_clear(id));

        public bool TryGetNearRestPose(ThreeOSPose head, out ThreeOSPose pose)
        {
            pose = default;
            if (!IsLoaded || NativeAbiVersion < 6) return false;
            try
            {
                var rc = threeos_voodoo_near_rest_pose(ref head, out pose);
                return rc == 0;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public void CloseMultiChoice()
        {
            if (!IsLoaded) return;
            try { threeos_multichoice_close(); }
            catch (Exception ex) { LastError = ex.Message; }
        }

        public bool TickMultiChoice(float x, float y, float z) =>
            Call(() => threeos_multichoice_tick(x, y, z));

        public bool ConfirmMultiChoice() => Call(threeos_multichoice_confirm);

        public bool TryGetMultiChoiceState(out InteropMultiChoiceState state)
        {
            state = default;
            if (!IsLoaded) return false;
            try
            {
                var rc = threeos_multichoice_get_state(out state);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool PollMultiChoiceAction(out MultiChoiceAction action, out ulong targetId,
            out MultiChoiceSlotKind kind)
        {
            action = MultiChoiceAction.None;
            targetId = 0;
            kind = MultiChoiceSlotKind.Empty;
            if (!IsLoaded) return false;
            try
            {
                var rc = threeos_multichoice_poll_action(out var a, out targetId, out var k);
                if (rc != 0) { LastError = ReadLastError(); return false; }
                action = (MultiChoiceAction)a;
                kind = (MultiChoiceSlotKind)k;
                LastError = string.Empty;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool MindPalaceLayoutNext(ulong mindPalaceId, ThreeOSPose head, string displayName) =>
            Call(() => threeos_mind_palace_layout_next(mindPalaceId, ref head, displayName ?? string.Empty));

        public bool StorageClearField() => Call(threeos_storage_clear_field);

        public bool FieldResolveOverlaps(ThreeOSPose head) =>
            Call(() => threeos_field_resolve_overlaps(ref head));

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
