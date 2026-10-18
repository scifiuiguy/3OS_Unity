using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Host-side Multi-Choice: Documents root ingest, palm-up open, widget viz, Add/Jump/favorite.
    /// </summary>
    internal sealed class ThreeOSMultiChoiceHost
    {
        private readonly ThreeOSStorageController _host;
        private readonly List<(ulong id, string name, string path)> _roots =
            new List<(ulong, string, string)>();
        private ulong _nextRootId = 9000;
        private readonly Dictionary<ulong, string> _rootPaths = new Dictionary<ulong, string>();
        private readonly Dictionary<ulong, string> _favoriteNames = new Dictionary<ulong, string>();

        // 2×2×2 @ 3″ voxels → ~6″ Multi-Choice cube.
        private const float VoxelM = 3f * 0.0254f;          // 0.0762
        private const float HalfExtentM = VoxelM * 0.5f;   // slot centers at ±1.5″
        private const float SlotSizeM = VoxelM * 0.82f;     // fill most of each voxel
        // Translucent so in-slot labels stay readable.
        private const float DisabledAlpha = 0.10f;
        private const float EnabledAlpha = 0.32f;
        private const float HoverAlpha = 0.48f;

        /// <summary>
        /// Fill index 0..7 → geometric octant bits (matches core
        /// <c>octant_from_slot_index</c>): TLF,TRF,BLF,BRF,TLB,TRB,BLB,BRB.
        /// </summary>
        private static readonly int[] SlotToOctant = { 2, 3, 0, 1, 6, 7, 4, 5 };

        private GameObject _widgetRoot;
        private readonly GameObject[] _slotNodes = new GameObject[8];
        private readonly Renderer[] _slotRends = new Renderer[8];
        private readonly TextMesh[] _slotLabels = new TextMesh[8];
        private GameObject _pageBack;
        private GameObject _pageFwd;
        private Renderer _pageBackShellRend;
        private Renderer _pageFwdShellRend;
        private Transform _pageBackArrow;
        private Transform _pageFwdArrow;
        private Renderer _pageBackArrowRend;
        private Renderer _pageFwdArrowRend;
        private static Mesh _cubeMesh;
        private static bool _cubeMeshAttempted;
        private GameObject _confirmRoot;
        private GameObject _confirmAdd;
        private GameObject _confirmJump;
        private Renderer _confirmAddRend;
        private Renderer _confirmJumpRend;
        private TextMesh _confirmAddLabel;
        private TextMesh _confirmJumpLabel;
        private TextMesh _titleText;
        private TextMesh _hoverText;
        private const float InchM = 0.0254f;
        private bool _primaryWasDown;
        private bool _mindPalaceMenuWasActive;
        private InteropMultiChoiceState _lastState;
        private static Mesh _slotMesh;
        private static bool _slotMeshAttempted;
        private static Mesh _arrowMesh;
        private static bool _arrowMeshAttempted;
        private static Mesh _heartMesh;
        private static Mesh _heartFilledMesh;
        private static bool _heartMeshesAttempted;

        internal ThreeOSMultiChoiceHost(ThreeOSStorageController host) { _host = host; }

        private ThreeOSBridge Bridge => _host.Bridge;
        private ThreeOSInputRouter Input => _host.Input;

        internal void Tick()
        {
            if (Bridge == null || !Bridge.IsLoaded || Input == null)
                return;

            RefreshDriveRootsIfNeeded();
            HandleOpenGestures();
            TickHoverAndConfirm();
            HandleActions();
            SyncViz();
        }

        internal void RefreshDriveRootsIfNeeded()
        {
            var docs = EnumerateDocumentsRoots();
            if (docs.Count == 0)
                return;

            var changed = docs.Count != _roots.Count;
            if (!changed)
            {
                for (var i = 0; i < docs.Count; i++)
                {
                    if (docs[i].name != _roots[i].name || docs[i].path != _roots[i].path)
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed && _roots.Count > 0)
                return;

            _roots.Clear();
            _rootPaths.Clear();
            var arr = new InteropDriveRoot[docs.Count];
            for (var i = 0; i < docs.Count; i++)
            {
                var id = _nextRootId++;
                _roots.Add((id, docs[i].name, docs[i].path));
                _rootPaths[id] = docs[i].path;
                arr[i] = new InteropDriveRoot { id = id, name = Truncate(docs[i].name, 31) };
            }

            Bridge.SetDriveRoots(arr);
        }

        private static List<(string name, string path)> EnumerateDocumentsRoots()
        {
            var list = new List<(string, string)>();
#if UNITY_ANDROID && !UNITY_EDITOR
            const string docsRoot = "/storage/emulated/0/Documents";
#else
            var docsRoot = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            // Editor stand-in: also scan project-adjacent demo folders.
            TryAddDir(list, Path.Combine(Application.dataPath, "..", "3OS_Demo"));
            TryAddDir(list, Path.Combine(Application.dataPath, "..", "3OS_Demo2"));
#endif
            try
            {
                if (Directory.Exists(docsRoot))
                {
                    foreach (var dir in Directory.GetDirectories(docsRoot))
                    {
                        var name = Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(name) || name.StartsWith("."))
                            continue;
                        list.Add((name, dir));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[3OS] Documents enumerate: {ex.Message}");
            }

            // Always include known demo folders if present.
            TryAddDir(list, ThreeOSDemoRootBootstrap.DocumentsPath(ThreeOSStorageController.DemoFolderName));
            TryAddDir(list, ThreeOSDemoRootBootstrap.DocumentsPath(ThreeOSStorageController.DemoFolderName2));
            return list;
        }

        private static void TryAddDir(List<(string name, string path)> list, string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                path = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            if (!Directory.Exists(path) && !ThreeOSAndroidStorage.Exists(path))
                return;
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, '/'));
            for (var i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].path, path, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            list.Add((name, path));
        }

        private void HandleOpenGestures()
        {
            if (!Bridge.TryGetIntents(out var intents))
                return;

            var mindPalaceMenu = (intents.flags & (uint)InteractionIntentFlags.MindPalaceMenu) != 0;
            var rising = mindPalaceMenu && !_mindPalaceMenuWasActive;
            _mindPalaceMenuWasActive = mindPalaceMenu;
            if (!rising)
                return;

            var hand = intents.handedness <= 1 ? intents.handedness : 1u;
            if (!Bridge.TryGetCursor(hand, out var cursor) || cursor.active == 0)
                return;

            var center = WidgetPoseFromCursor(cursor, Input.LastFrame.head);
            // Box hit under cursor → folder context; else drive picker.
            var boxId = FindBoxNearCursor(InteropStructs.OpenXrToUnityPosition(
                new Vector3(cursor.position.x, cursor.position.y, cursor.position.z)));
            if (boxId != 0)
            {
                if (_host.Glyphs.Views.TryGetValue(boxId, out var view) && view != null)
                    _favoriteNames[boxId] = view.NameLabel != null ? view.NameLabel.text : $"Box {boxId}";
                Bridge.OpenMultiChoiceFolder(hand, center, boxId);
            }
            else
            {
                Bridge.OpenMultiChoiceDrive(hand, center);
            }
        }

        /// <summary>
        /// OpenXR-only widget pose: same floor-yaw as core <c>yaw_billboard_toward</c>
        /// (local +Z faces the head). Unity only converts at draw via
        /// <see cref="InteropStructs.ToUnityPose"/>.
        /// </summary>
        private static ThreeOSPose WidgetPoseFromCursor(InteropPointCursor cursor, ThreeOSPose head)
        {
            var pos = new Vector3(cursor.position.x, cursor.position.y + 0.02f, cursor.position.z);
            var toHead = new Vector3(head.position.x - pos.x, 0f, head.position.z - pos.z);
            if (toHead.sqrMagnitude < 1e-6f)
                toHead = new Vector3(0f, 0f, -1f);
            toHead.Normalize();
            // quat_from_yaw(atan2(to.x, to.z)): local −Z → −toHead, so +Z faces head.
            var yaw = Mathf.Atan2(toHead.x, toHead.z);
            var half = yaw * 0.5f;
            return new ThreeOSPose
            {
                position = new ThreeOSVec3 { x = pos.x, y = pos.y, z = pos.z },
                orientation = new ThreeOSQuat
                {
                    x = 0f, y = Mathf.Sin(half), z = 0f, w = Mathf.Cos(half)
                }
            };
        }

        /// <summary>
        /// Core octant local (sx, sy, −sz) in OpenXR → Unity parent-local (X-flip only).
        /// Must match <c>MultiChoiceEngine::octant_local_offset</c> so viz and hover agree.
        /// </summary>
        private static Vector3 OctantUnityLocal(float sx, float sy, float sz, float extentM) =>
            InteropStructs.OpenXrToUnityPosition(new Vector3(sx, sy, -sz) * extentM);

        /// <summary>OpenXR local offset → Unity child local (X-flip).</summary>
        private static Vector3 OpenXrLocalToUnity(Vector3 openXrLocal) =>
            InteropStructs.OpenXrToUnityPosition(openXrLocal);

        private ulong FindBoxNearCursor(Vector3 unityCursor)
        {
            const float r = 0.12f;
            ulong best = 0;
            var bestD = r * r;
            foreach (var kv in _host.Glyphs.Views)
            {
                var view = kv.Value;
                if (view?.Root == null || !view.Root.activeInHierarchy)
                    continue;
                if (!_host.TryFindItem(kv.Key, out var item) || item.kind != 1u)
                    continue;
                var p = view.Root.transform.position;
                var d = (p - unityCursor).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = kv.Key;
                }
            }

            return best;
        }

        private void TickHoverAndConfirm()
        {
            if (!Bridge.TryGetMultiChoiceState(out _lastState) || _lastState.open == 0)
            {
                DestroyViz();
                _primaryWasDown = false;
                return;
            }

            var hand = _lastState.hand;
            if (Bridge.TryGetCursor(hand, out var cursor) && cursor.active != 0)
                Bridge.TickMultiChoice(cursor.position.x, cursor.position.y, cursor.position.z);

            var frame = Input.LastFrame;
            var primaryDown = hand == 0
                ? frame.leftTrigger >= _host.TriggerAxisThreshold
                : frame.rightTrigger >= _host.TriggerAxisThreshold;
            // Also honor intent primary held.
            if (Bridge.TryGetIntents(out var intents) &&
                (intents.flags & (uint)InteractionIntentFlags.PrimaryHeld) != 0 &&
                intents.handedness == hand)
                primaryDown = true;

            if (primaryDown && !_primaryWasDown)
                Bridge.ConfirmMultiChoice();
            _primaryWasDown = primaryDown;

            Bridge.TryGetMultiChoiceState(out _lastState);
        }

        private void HandleActions()
        {
            if (!Bridge.PollMultiChoiceAction(out var action, out var targetId, out var kind))
                return;
            if (action == MultiChoiceAction.None)
                return;

            var head = Input.LastFrame.head;
            if (action == MultiChoiceAction.ToggleFavorite)
            {
                var favored = Bridge.IsFavorite(targetId);
                Debug.Log($"[3OS] favorite box={targetId} favored={favored}");
                return;
            }

            if (kind == MultiChoiceSlotKind.DriveRoot)
            {
                if (!_rootPaths.TryGetValue(targetId, out var path))
                {
                    Debug.LogWarning($"[3OS] unknown drive root {targetId}");
                    return;
                }

                var name = Path.GetFileName(path);
                if (action == MultiChoiceAction.JumpToRoot)
                {
                    Bridge.StorageClearField();
                    Bridge.MindPalaceClear();
                    if (!Bridge.TryRegisterMindPalace(name, head, out var mindPalaceId))
                    {
                        Debug.LogWarning($"[3OS] Jump register: {Bridge.LastError}");
                        return;
                    }

                    _host.LoadDemoDirectoryPublic(path, mindPalaceId, clearFirst: false);
                    if (!Bridge.MindPalaceLayoutNext(mindPalaceId, head, name))
                        Debug.LogWarning($"[3OS] Jump layout: {Bridge.LastError}");
                    _host.Glyphs.SyncViewsFromKernel();
                    return;
                }

                if (action == MultiChoiceAction.Add)
                {
                    if (!Bridge.TryRegisterMindPalace(name, head, out var mindPalaceId))
                    {
                        Debug.LogWarning($"[3OS] Add register: {Bridge.LastError}");
                        return;
                    }

                    _host.LoadDemoDirectoryPublic(path, mindPalaceId, clearFirst: false);
                    if (!Bridge.MindPalaceLayoutNext(mindPalaceId, head, name))
                        Debug.LogWarning($"[3OS] Add layout: {Bridge.LastError}");
                    Bridge.FieldResolveOverlaps(head);
                    _host.Glyphs.SyncViewsFromKernel();
                }

                return;
            }

            if (kind == MultiChoiceSlotKind.FavoriteBox)
            {
                // Favorite Add/Jump: expand box into field as its own MindPalace cluster of children.
                if (action == MultiChoiceAction.JumpToRoot)
                {
                    Bridge.StorageClearField();
                    Bridge.ExpandBox(targetId);
                    Bridge.FieldResolveOverlaps(head);
                    _host.Glyphs.SyncViewsFromKernel();
                    return;
                }

                if (action == MultiChoiceAction.Add)
                {
                    Bridge.ExpandBox(targetId);
                    Bridge.FieldResolveOverlaps(head);
                    _host.Glyphs.SyncViewsFromKernel();
                }
            }
        }

        private void SyncViz()
        {
            if (_lastState.open == 0)
            {
                DestroyViz();
                return;
            }

            EnsureViz();
            var unityPose = InteropStructs.ToUnityPose(_lastState.center);
            _widgetRoot.transform.SetPositionAndRotation(unityPose.position, unityPose.rotation);

            if (_titleText != null)
                _titleText.text = _lastState.Title;
            // Leave blank when hovering disabled/empty (avoid repeating the title).
            if (_hoverText != null)
                _hoverText.text = _lastState.HoverLabel ?? string.Empty;

            for (var i = 0; i < 8; i++)
            {
                var slot = _lastState.SlotAt(i);
                var node = _slotNodes[i];
                if (node == null)
                    continue;
                var (sx, sy, sz) = OctantSigns(SlotToOctant[i]);
                // Core OpenXR octant (sx,sy,−sz) → Unity local; signed scale keeps notch→center.
                var hover = _lastState.hoveredSlot == i;
                var size = SlotSizeM * (hover ? 1.08f : 1f);
                var lp = OctantUnityLocal(sx, sy, sz, HalfExtentM);
                node.transform.localPosition = lp;
                node.transform.localScale = new Vector3(
                    Mathf.Sign(lp.x) * size, Mathf.Sign(lp.y) * size, Mathf.Sign(lp.z) * size);
                node.SetActive(true);

                var enabled = slot.enabled != 0;
                var fav = slot.kind == (uint)MultiChoiceSlotKind.ActionFavorite;
                var filled = fav && Bridge.IsFavorite(_lastState.contextBoxId);
                Color baseCol;
                if (fav)
                    baseCol = filled ? new Color(0.92f, 0.18f, 0.28f) : new Color(0.85f, 0.55f, 0.60f);
                else if (hover && enabled)
                    baseCol = new Color(1f, 0.92f, 0.35f);
                else
                    baseCol = new Color(0.45f, 0.78f, 0.95f);
                var a = !enabled ? DisabledAlpha : (hover ? HoverAlpha : EnabledAlpha);
                SetNodeColor(_slotRends[i], baseCol, a);

                if (fav)
                {
                    EnsureHeartMeshes();
                    var mf = node.GetComponent<MeshFilter>();
                    var mesh = filled ? _heartFilledMesh : _heartMesh;
                    if (mf != null && mesh != null)
                        mf.sharedMesh = mesh;
                    else if (mf != null)
                        mf.sharedMesh = EnsureSlotMesh();
                }
                else
                {
                    var mf = node.GetComponent<MeshFilter>();
                    if (mf != null)
                        mf.sharedMesh = EnsureSlotMesh();
                }

                // In-slot label (sibling under widget so signed mesh scale does not mirror text).
                var label = _slotLabels[i];
                if (label != null)
                {
                    label.gameObject.SetActive(enabled && !string.IsNullOrEmpty(slot.Label));
                    if (enabled)
                    {
                        label.text = slot.Label ?? string.Empty;
                        label.transform.localPosition = lp;
                        // Counter OpenXR→Unity X-mirror so glyphs read forward.
                        label.transform.localScale = new Vector3(-1f, 1f, 1f);
                        label.color = hover ? Color.black : Color.white;
                    }
                }
            }

            // Pagination: squashed cuboid shells + arrow glyphs (pitch 90, yaw 180, 5×).
            var pageHalf = SlotSizeM * 0.25f;
            var shellScale = new Vector3(SlotSizeM * 0.5f, SlotSizeM * 0.85f, SlotSizeM * 0.85f);
            var arrowSize = SlotSizeM * 0.55f * 5f;
            if (_pageBack != null)
            {
                _pageBack.SetActive(true);
                _pageBack.transform.localPosition = OpenXrLocalToUnity(new Vector3(-pageHalf, 0f, 0f));
                _pageBack.transform.localRotation = Quaternion.identity;
                _pageBack.transform.localScale = Vector3.one;
                var h = _lastState.hoveredSlot == 8;
                var en = _lastState.pageBackEnabled != 0;
                var shell = _pageBack.transform.Find("Shell");
                if (shell != null)
                    shell.localScale = shellScale * (h ? 1.08f : 1f);
                SetNodeColor(_pageBackShellRend,
                    h && en ? new Color(1f, 0.92f, 0.35f) : new Color(0.7f, 0.75f, 0.85f),
                    en ? (h ? HoverAlpha : EnabledAlpha) : DisabledAlpha);
                if (_pageBackArrow != null)
                {
                    // Face was +Y; pitch 90 toward user, yaw 180 to fix back↔fwd swap.
                    _pageBackArrow.localRotation = Quaternion.Euler(90f, 180f, 0f);
                    _pageBackArrow.localScale = Vector3.one * (arrowSize * (h ? 1.08f : 1f));
                    // White → black on hover (even if page disabled, for clear feedback).
                    SetNodeColor(_pageBackArrowRend, h ? Color.black : Color.white,
                        en ? 1f : Mathf.Max(DisabledAlpha, 0.35f));
                    SetArrowDrawInFront(_pageBackArrowRend, h);
                }
            }

            if (_pageFwd != null)
            {
                _pageFwd.SetActive(true);
                _pageFwd.transform.localPosition = OpenXrLocalToUnity(new Vector3(pageHalf, 0f, 0f));
                _pageFwd.transform.localRotation = Quaternion.identity;
                _pageFwd.transform.localScale = Vector3.one;
                var h = _lastState.hoveredSlot == 9;
                var en = _lastState.pageForwardEnabled != 0;
                var shell = _pageFwd.transform.Find("Shell");
                if (shell != null)
                    shell.localScale = shellScale * (h ? 1.08f : 1f);
                SetNodeColor(_pageFwdShellRend,
                    h && en ? new Color(1f, 0.92f, 0.35f) : new Color(0.7f, 0.75f, 0.85f),
                    en ? (h ? HoverAlpha : EnabledAlpha) : DisabledAlpha);
                if (_pageFwdArrow != null)
                {
                    _pageFwdArrow.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    _pageFwdArrow.localScale = Vector3.one * (arrowSize * (h ? 1.08f : 1f));
                    SetNodeColor(_pageFwdArrowRend, h ? Color.black : Color.white,
                        en ? 1f : Mathf.Max(DisabledAlpha, 0.35f));
                    SetArrowDrawInFront(_pageFwdArrowRend, h);
                }
            }

            if (_confirmRoot != null)
            {
                _confirmRoot.SetActive(_lastState.confirmOpen != 0);
                if (_lastState.confirmOpen != 0)
                {
                    var cc = InteropStructs.ToUnityPose(_lastState.confirmCenter);
                    _confirmRoot.transform.SetPositionAndRotation(cc.position, unityPose.rotation);
                    var confScale = SlotSizeM * 0.7f;
                    SyncConfirmNode(_confirmAdd, _confirmAddRend, _confirmAddLabel,
                        OpenXrLocalToUnity(new Vector3(-HalfExtentM * 0.7f, 0f, 0f)),
                        "Add", _lastState.confirmHovered == 0, confScale,
                        new Color(0.3f, 0.9f, 0.4f));
                    SyncConfirmNode(_confirmJump, _confirmJumpRend, _confirmJumpLabel,
                        OpenXrLocalToUnity(new Vector3(HalfExtentM * 0.7f, 0f, 0f)),
                        "Jump to Root", _lastState.confirmHovered == 1, confScale,
                        new Color(0.4f, 0.6f, 1f));
                }
            }
        }

        private static void SyncConfirmNode(GameObject node, Renderer rend, TextMesh label,
            Vector3 localPos, string text, bool hover, float size, Color baseCol)
        {
            if (node == null)
                return;
            node.transform.localPosition = localPos;
            node.transform.localScale = Vector3.one * (size * (hover ? 1.1f : 1f));
            SetNodeColor(rend, baseCol, hover ? HoverAlpha : EnabledAlpha);
            if (label == null)
                return;
            label.gameObject.SetActive(true);
            label.text = text;
            label.transform.localPosition = localPos;
            label.transform.localScale = new Vector3(-1f, 1f, 1f);
            label.color = hover ? Color.black : Color.white;
        }

        private static (float sx, float sy, float sz) OctantSigns(int octant)
        {
            var sx = (octant & 1) != 0 ? 1f : -1f;
            var sy = (octant & 2) != 0 ? 1f : -1f;
            var sz = (octant & 4) != 0 ? 1f : -1f;
            return (sx, sy, sz);
        }

        private void EnsureViz()
        {
            if (_widgetRoot != null)
                return;
            EnsureSlotMesh();
            EnsureArrowMesh();
            _widgetRoot = new GameObject("MultiChoiceWidget");
            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(_widgetRoot.transform, false);
            // +1″ above prior placement so title/status clear the cuboids.
            titleGo.transform.localPosition = new Vector3(0f, VoxelM * 1.15f + InchM, 0f);
            // Negate X to undo OpenXR→Unity mirror so text reads forward.
            titleGo.transform.localScale = new Vector3(-1f, 1f, 1f);
            _titleText = titleGo.AddComponent<TextMesh>();
            _titleText.fontSize = 28;
            _titleText.characterSize = 0.006f;
            _titleText.anchor = TextAnchor.MiddleCenter;
            _titleText.color = Color.white;

            var hoverGo = new GameObject("Hover");
            hoverGo.transform.SetParent(_widgetRoot.transform, false);
            hoverGo.transform.localPosition = new Vector3(0f, VoxelM * 0.95f + InchM, 0f);
            hoverGo.transform.localScale = new Vector3(-1f, 1f, 1f);
            _hoverText = hoverGo.AddComponent<TextMesh>();
            _hoverText.fontSize = 24;
            _hoverText.characterSize = 0.005f;
            _hoverText.anchor = TextAnchor.MiddleCenter;
            _hoverText.color = new Color(1f, 0.92f, 0.35f);

            for (var i = 0; i < 8; i++)
            {
                _slotNodes[i] = MakeSlotNode(_widgetRoot.transform, $"Slot{i}",
                    new Color(0.45f, 0.78f, 0.95f, EnabledAlpha));
                _slotRends[i] = _slotNodes[i].GetComponent<Renderer>();
                _slotLabels[i] = MakeSlotLabel(_widgetRoot.transform, $"SlotLabel{i}");
            }

            _pageBack = MakePageButton(_widgetRoot.transform, "PageBack", tipBack: true,
                out _pageBackShellRend, out _pageBackArrow, out _pageBackArrowRend);
            _pageFwd = MakePageButton(_widgetRoot.transform, "PageFwd", tipBack: false,
                out _pageFwdShellRend, out _pageFwdArrow, out _pageFwdArrowRend);

            _confirmRoot = new GameObject("ConfirmStrip");
            _confirmAdd = MakeSlotNode(_confirmRoot.transform, "Add",
                new Color(0.3f, 0.9f, 0.4f, EnabledAlpha));
            _confirmJump = MakeSlotNode(_confirmRoot.transform, "Jump",
                new Color(0.4f, 0.6f, 1f, EnabledAlpha));
            _confirmAddRend = _confirmAdd.GetComponent<Renderer>();
            _confirmJumpRend = _confirmJump.GetComponent<Renderer>();
            _confirmAddLabel = MakeSlotLabel(_confirmRoot.transform, "AddLabel");
            _confirmJumpLabel = MakeSlotLabel(_confirmRoot.transform, "JumpLabel");
            _confirmRoot.SetActive(false);
        }

        private static GameObject MakeSlotNode(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            // mc_slot notch is authored facing −Z; yaw 180 so the cut faces widget center / user.
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = EnsureSlotMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ThreeOSGlyphViewRegistry.CreateTranslucentUnlitMaterial(color);
            return go;
        }

        /// <summary>
        /// Center pagination control: un-notched cuboid shell (50% X) + arrow glyph child.
        /// </summary>
        private static GameObject MakePageButton(Transform parent, string name, bool tipBack,
            out Renderer shellRend, out Transform arrowXf, out Renderer arrowRend)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var shell = new GameObject("Shell");
            shell.transform.SetParent(go.transform, false);
            var smf = shell.AddComponent<MeshFilter>();
            smf.sharedMesh = EnsureCubeMesh();
            shellRend = shell.AddComponent<MeshRenderer>();
            shellRend.sharedMaterial = ThreeOSGlyphViewRegistry.CreateTranslucentUnlitMaterial(
                new Color(0.7f, 0.75f, 0.85f, EnabledAlpha));

            var arrow = new GameObject("Arrow");
            arrow.transform.SetParent(go.transform, false);
            // Pitch 90 so face aims at user; yaw 180 fixes prior back↔fwd swap.
            arrow.transform.localRotation = tipBack
                ? Quaternion.Euler(90f, 180f, 0f)
                : Quaternion.Euler(90f, 0f, 0f);
            var amf = arrow.AddComponent<MeshFilter>();
            amf.sharedMesh = EnsureArrowMesh();
            arrowRend = arrow.AddComponent<MeshRenderer>();
            arrowRend.sharedMaterial = ThreeOSGlyphViewRegistry.CreateTranslucentUnlitMaterial(
                Color.white);
            arrowXf = arrow.transform;
            return go;
        }

        private static Mesh EnsureCubeMesh()
        {
            if (_cubeMeshAttempted)
                return _cubeMesh;
            _cubeMeshAttempted = true;
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(cube);
            return _cubeMesh;
        }

        private static TextMesh MakeSlotLabel(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(-1f, 1f, 1f);
            var tm = go.AddComponent<TextMesh>();
            tm.fontSize = 36;
            tm.characterSize = 0.0022f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            tm.text = string.Empty;
            go.SetActive(false);
            return tm;
        }

        private static void SetNodeColor(Renderer rend, Color rgb, float alpha)
        {
            if (rend == null)
                return;
            var c = new Color(rgb.r, rgb.g, rgb.b, alpha);
            // PropertyBlock avoids losing tint when sharedMaterial is swapped/batched.
            var block = new MaterialPropertyBlock();
            rend.GetPropertyBlock(block);
            block.SetColor("_Color", c);
            block.SetColor("_BaseColor", c);
            rend.SetPropertyBlock(block);
            var mat = rend.material;
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", c);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", c);
            mat.color = c;
        }

        /// <summary>
        /// On hover, draw the arrow after the translucent shell (like TextMesh over geo).
        /// </summary>
        private static void SetArrowDrawInFront(Renderer rend, bool inFront)
        {
            if (rend == null)
                return;
            var mat = rend.material;
            if (inFront)
            {
                mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
            }
            else
            {
                mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
        }

        private static Mesh EnsureSlotMesh()
        {
            if (_slotMeshAttempted)
                return _slotMesh;
            _slotMeshAttempted = true;
            try
            {
                byte[] bytes = null;
                var ta = Resources.Load<TextAsset>("3OS/glyphs/mc_slot");
                if (ta != null && ta.bytes != null && ta.bytes.Length > 0)
                    bytes = ta.bytes;
                if (bytes != null && ThreeOSGlbMesh.TryLoadFirstMesh(bytes, out var mesh, out _))
                {
                    _slotMesh = mesh;
                    _slotMesh.name = "mc_slot";
                    return _slotMesh;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[3OS] mc_slot mesh load: {ex.Message}");
            }

            // Fallback: unit cube (still sized via transform).
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _slotMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.Destroy(cube);
            return _slotMesh;
        }

        private static Mesh EnsureArrowMesh()
        {
            if (_arrowMeshAttempted)
                return _arrowMesh;
            _arrowMeshAttempted = true;
            _arrowMesh = LoadGlyphMesh("arrow");
            if (_arrowMesh != null)
            {
                _arrowMesh.name = "arrow";
                return _arrowMesh;
            }

            // Procedural tip-+X triangle prism fallback.
            var mesh = new Mesh { name = "arrow_fallback" };
            const float h = 0.5f;
            const float w = 0.4f;
            const float d = 0.12f;
            mesh.vertices = new[]
            {
                new Vector3(h, 0f, -d), new Vector3(-h, w, -d), new Vector3(-h, -w, -d),
                new Vector3(h, 0f, d), new Vector3(-h, w, d), new Vector3(-h, -w, d)
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 3, 5, 4, 0, 3, 4, 0, 4, 1, 0, 2, 5, 0, 5, 3, 1, 4, 5, 1, 5, 2
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _arrowMesh = mesh;
            return _arrowMesh;
        }

        private void DestroyViz()
        {
            if (_widgetRoot != null)
            {
                UnityEngine.Object.Destroy(_widgetRoot);
                _widgetRoot = null;
            }

            if (_confirmRoot != null)
            {
                UnityEngine.Object.Destroy(_confirmRoot);
                _confirmRoot = null;
            }

            for (var i = 0; i < _slotNodes.Length; i++)
            {
                _slotNodes[i] = null;
                _slotRends[i] = null;
                _slotLabels[i] = null;
            }

            _pageBack = null;
            _pageFwd = null;
            _pageBackShellRend = null;
            _pageFwdShellRend = null;
            _pageBackArrow = null;
            _pageFwdArrow = null;
            _pageBackArrowRend = null;
            _pageFwdArrowRend = null;
            _confirmAdd = null;
            _confirmJump = null;
            _confirmAddRend = null;
            _confirmJumpRend = null;
            _confirmAddLabel = null;
            _confirmJumpLabel = null;
            _titleText = null;
            _hoverText = null;
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Length <= max ? s : s.Substring(0, max);
        }

        private static void EnsureHeartMeshes()
        {
            if (_heartMeshesAttempted)
                return;
            _heartMeshesAttempted = true;
            _heartMesh = LoadGlyphMesh("heart");
            _heartFilledMesh = LoadGlyphMesh("heart_filled");
        }

        private static Mesh LoadGlyphMesh(string resourceName)
        {
            try
            {
                byte[] bytes = null;
                var ta = Resources.Load<TextAsset>("3OS/glyphs/" + resourceName);
                if (ta != null && ta.bytes != null && ta.bytes.Length > 0)
                    bytes = ta.bytes;
                if (bytes == null)
                {
                    var path = Path.Combine(Application.streamingAssetsPath, "3OS", "glyphs",
                        resourceName + ".3glyph");
                    if (File.Exists(path))
                        bytes = File.ReadAllBytes(path);
                }

                if (bytes == null || bytes.Length == 0)
                    return null;
                if (ThreeOSGlbMesh.TryLoadFirstMesh(bytes, out var mesh, out _))
                    return mesh;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[3OS] heart glyph load {resourceName}: {ex.Message}");
            }

            return null;
        }
    }
}
