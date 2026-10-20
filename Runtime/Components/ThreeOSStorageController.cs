using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ThreeOS
{
    /// <summary>
    /// Demo workspace host: dual-mind-palace load, glyph views, trigger select/drag, snap-insert,
    /// portal/expanded dismiss chrome, write-through FS cut.
    /// </summary>
    [DefaultExecutionOrder(0)]
    public sealed class ThreeOSStorageController : MonoBehaviour
    {
        public const string DemoFolderName = "3OS_Demo";
        public const string DemoFolderName2 = "3OS_Demo2";
        public const float MindPalaceFixtureRangeM = 10f * 0.3048f;
        public const float MindPalaceFixtureHeightM = 2.5f * 0.3048f;
        public const float MindPalaceFixtureAzimuthDeg = 15f;
        public const float GlyphSizeM = 0.1f;
        public const float DismissXDiameterM = 0.06f;
        public float PossessDistance = 0.45f;
        public float TriggerHoldDragSeconds = 0.22f;
        public float SummonZoneHoldDragSeconds = 0.40f;
        public float TriggerAxisThreshold = 0.55f;
        public float BoxDoubleTapSeconds = 0.4f;
        public float BoxExpandAnimSeconds = 0.3f;
        public float BoxDismissXOffsetM = 0.053333f;

        [Header("Glyph selection glow (ThreeOS/Glyph)")]
        public ThreeOSGlyphGlowSettings GlyphGlowSettings;

        private ThreeOSBridge _bridge;
        private ThreeOSInputRouter _input;
        private ThreeOSTopologyController _topology;
        private ThreeOSGlyphViewRegistry _glyphs;
        private ThreeOSDismissAffordances _dismiss;
        private ThreeOSStorageWriteThrough _fs;
        private ThreeOSExpandedBoxController _expanded;
        private ThreeOSStorageInteraction _interaction;
        private ThreeOSMultiChoiceHost _multiChoice;

        private readonly HashSet<ulong> _animLockedIds = new HashSet<ulong>();
        private ThreeOSArrowVisual _insertArrow;
        private bool _triggerWasDown;
        private float _triggerDownTime;
        private bool _triggerDragActive;
        private bool _triggerGestureConsumed;
        private Vector3 _triggerDownCursorXr;
        private bool _triggerDownCursorValid;
        private int _selectHand = -1;
        private bool _possessing;
        private ulong _possessedId;
        private bool _ghostDragging;
        private bool _insertAnimBusy;
        private bool _boxAnimBusy;
        private ulong _lastShortTapId;
        private float _lastShortTapTime = -10f;
        private string _status = "storage idle";
        private string _demoRoot = string.Empty;
        private string _demoRoot2 = string.Empty;
        private ThreeOSPose _layoutHeadPose;
        private bool _waitingForAllFilesAccess;
        private string _selectionLabel = "none";
        private ulong _xHoverBoxId;
        private bool _leftTriggerWasDown;
        private bool _rightTriggerWasDown;

        public int PossessDriveHand =>
            (_possessing || _ghostDragging) && _selectHand >= 0 ? _selectHand : -1;
        public bool DemoActive { get; private set; }
        public string Status => _status;
        public string DemoRoot => _demoRoot;
        public string SelectionLabel => _selectionLabel;

        // Visual reconcile is dirty-flagged — not a 72 Hz full workspace scan.
        bool _viewsDirty = true;
        bool _ghostsDirty = true;
        bool _selectionMatsDirty = true;
        ulong _lastSelectionId = ulong.MaxValue;
        float _nextSafetyReconcileAt;

        // Collaborator access
        internal ThreeOSBridge Bridge => _bridge;
        internal ThreeOSInputRouter Input => _input;
        internal ThreeOSTopologyController Topology => _topology;
        internal ThreeOSGlyphViewRegistry Glyphs => _glyphs;
        internal ThreeOSDismissAffordances Dismiss => _dismiss;
        internal ThreeOSStorageWriteThrough Fs => _fs;
        internal ThreeOSExpandedBoxController Expanded => _expanded;
        internal ThreeOSStorageInteraction Interaction => _interaction;
        internal Dictionary<ulong, ThreeOSExpandedBoxUi> ExpandedBoxes => _expanded.ExpandedBoxes;
        internal HashSet<ulong> AnimLockedIds => _animLockedIds;
        internal string StatusField { get => _status; set => _status = value; }
        internal string SelectionLabelField { get => _selectionLabel; set => _selectionLabel = value; }
        internal string DemoRoot2Field => _demoRoot2;
        internal bool WaitingForAllFilesAccess { get => _waitingForAllFilesAccess; set => _waitingForAllFilesAccess = value; }
        internal bool InsertAnimBusy { get => _insertAnimBusy; set => _insertAnimBusy = value; }
        internal bool BoxAnimBusy { get => _boxAnimBusy; set => _boxAnimBusy = value; }
        internal ulong XHoverBoxId { get => _xHoverBoxId; set => _xHoverBoxId = value; }
        internal ThreeOSArrowVisual InsertArrow { get => _insertArrow; set => _insertArrow = value; }
        internal bool TriggerWasDown { get => _triggerWasDown; set => _triggerWasDown = value; }
        internal float TriggerDownTime { get => _triggerDownTime; set => _triggerDownTime = value; }
        internal bool TriggerDragActive { get => _triggerDragActive; set => _triggerDragActive = value; }
        internal bool TriggerGestureConsumed { get => _triggerGestureConsumed; set => _triggerGestureConsumed = value; }
        internal Vector3 TriggerDownCursorXr { get => _triggerDownCursorXr; set => _triggerDownCursorXr = value; }
        internal bool TriggerDownCursorValid { get => _triggerDownCursorValid; set => _triggerDownCursorValid = value; }
        internal int SelectHandField { get => _selectHand; set => _selectHand = value; }
        internal bool Possessing { get => _possessing; set => _possessing = value; }
        internal ulong PossessedId { get => _possessedId; set => _possessedId = value; }
        internal bool GhostDragging { get => _ghostDragging; set => _ghostDragging = value; }
        internal ulong LastShortTapId { get => _lastShortTapId; set => _lastShortTapId = value; }
        internal float LastShortTapTime { get => _lastShortTapTime; set => _lastShortTapTime = value; }
        internal bool LeftTriggerWasDown { get => _leftTriggerWasDown; set => _leftTriggerWasDown = value; }
        internal bool RightTriggerWasDown { get => _rightTriggerWasDown; set => _rightTriggerWasDown = value; }

        public static string PreferredDocumentsDemoPath() =>
            ThreeOSDemoRootBootstrap.PreferredDocumentsDemoPath();
        public static string FallbackPersistentDemoPath() =>
            ThreeOSDemoRootBootstrap.FallbackPersistentDemoPath();
        public static string PreferredDocumentsDemoPath2() =>
            ThreeOSDemoRootBootstrap.DocumentsPath(DemoFolderName2);
        public static string FallbackPersistentDemoPath2() =>
            ThreeOSDemoRootBootstrap.PersistentPath(DemoFolderName2);

        internal Coroutine StartCoroutinePublic(IEnumerator routine) => StartCoroutine(routine);
        internal static string CombineRootRelPublic(string root, string rel) => CombineRootRel(root, rel);

        internal void MarkViewsDirty() => _viewsDirty = true;
        internal void MarkGhostsDirty() => _ghostsDirty = true;
        internal void MarkSelectionMatsDirty() => _selectionMatsDirty = true;

        internal void MarkStorageVisualsDirty()
        {
            _viewsDirty = true;
            _ghostsDirty = true;
            _selectionMatsDirty = true;
        }

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
            _input = GetComponent<ThreeOSInputRouter>() ?? FindFirstObjectByType<ThreeOSInputRouter>();
            _topology = GetComponent<ThreeOSTopologyController>() ??
                        FindFirstObjectByType<ThreeOSTopologyController>();
            if (GlyphGlowSettings == null)
                GlyphGlowSettings = Resources.Load<ThreeOSGlyphGlowSettings>("3OS/GlyphGlowSettings");

            _glyphs = new ThreeOSGlyphViewRegistry(this);
            _dismiss = new ThreeOSDismissAffordances(this);
            _fs = new ThreeOSStorageWriteThrough(this);
            _expanded = new ThreeOSExpandedBoxController(this);
            _interaction = new ThreeOSStorageInteraction(this);
            _multiChoice = new ThreeOSMultiChoiceHost(this);

            _insertArrow = ThreeOSArrowVisual.Create("3OS_InsertArrow", 0.01f);
            if (GetComponent<ThreeOSVoodooController>() == null)
                gameObject.AddComponent<ThreeOSVoodooController>();
            if (GetComponent<ThreeOSCentroidDebug>() == null)
                gameObject.AddComponent<ThreeOSCentroidDebug>();
        }

        private IEnumerator Start()
        {
            var bridgeDeadline = Time.realtimeSinceStartup + 3f;
            while ((_bridge == null || !_bridge.IsLoaded) && Time.realtimeSinceStartup < bridgeDeadline)
            {
                if (_bridge == null)
                    _bridge = FindFirstObjectByType<ThreeOSBridge>();
                yield return null;
            }

            if (_bridge == null || !_bridge.IsLoaded)
            {
                _status = $"bridge not loaded: {(_bridge != null ? _bridge.LastError : "missing")}";
                Debug.LogError($"[3OS] {_status}");
                yield break;
            }

            yield return InstallPacks();
            yield return ThreeOSDemoRootBootstrap.EnsureDemoRoot(
                DemoFolderName, DemoSeedPolicy.RequireContent, null,
                r => _demoRoot = r, s => _status = s);
            yield return ThreeOSDemoRootBootstrap.EnsureDemoRoot(
                DemoFolderName2, DemoSeedPolicy.SoftSeedMindPalaceMarker, _demoRoot,
                r => _demoRoot2 = r, s => _status = s);
            if (string.IsNullOrEmpty(_demoRoot))
            {
                Debug.LogError($"[3OS] demo load aborted: {_status}");
                yield break;
            }

            HideNonGlyphMovables();
            yield return WaitForHeadPose();

            // Final Demo Act I (MR cold open): Stage is present → defer dual Mind Palace
            // centroids/glyphs until Act II / Task 7.7. DemoScene has no Stage → load as usual.
            var deferPalace = ShouldDeferMindPalaceFixture();
            if (deferPalace)
            {
                _status = "final-demo MR cold open — Mind Palace load deferred";
                Debug.Log($"[3OS] {_status}");
            }
            else if (!LoadDualMindPalaceFixture(_layoutHeadPose))
            {
                Debug.LogError($"[3OS] dual-mind-palace load aborted: {_status}");
                yield break;
            }

            DemoActive = true;
            var kin = GetComponent<ThreeOSKinematicsController>() ??
                      FindFirstObjectByType<ThreeOSKinematicsController>();
            kin?.PushKinematicsParams();
            RebuildViews();
            if (deferPalace)
            {
                _status =
                    $"final-demo MR ready (0 glyphs; Palace deferred) roots={ThreeOSStorageWriteThrough.ShortPath(_demoRoot)}";
                Debug.Log($"[3OS] {_status}");
            }
            else
            {
                _status =
                    $"demo {_glyphs.Views.Count} glyphs dual-mind-palace @ {ThreeOSStorageWriteThrough.ShortPath(_demoRoot)}";
                Debug.Log(
                    $"[3OS] demo loaded views={_glyphs.Views.Count} kernelItems={_bridge.StorageItemCount()} root={_demoRoot}");
            }

            if (!ThreeOSAndroidStorage.IsExternalStorageManager())
                Debug.Log("[3OS] all-files access not granted yet (OK for boot; will prompt on cut/drop)");
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus || !_waitingForAllFilesAccess)
                return;
            if (ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                _waitingForAllFilesAccess = false;
                _status = "All files access OK — drop again to cut";
                Debug.Log("[3OS] MANAGE_EXTERNAL_STORAGE granted");
            }
        }

        private static void HideNonGlyphMovables()
        {
            foreach (var movable in FindObjectsByType<ThreeOSMovable>(FindObjectsSortMode.None))
            {
                if (movable.gameObject.name.StartsWith("Glyph_"))
                    continue;
                movable.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// True when Final Demo Stage is in the loaded scene (sample Assembly-CSharp marker).
        /// Package cannot hard-reference that type, so match by runtime type name.
        /// </summary>
        private static bool ShouldDeferMindPalaceFixture()
        {
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb != null && mb.GetType().Name == "ThreeOSFinalDemoStage")
                    return true;
            }

            return false;
        }

        private IEnumerator WaitForHeadPose()
        {
            var deadline = Time.realtimeSinceStartup + 8f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_input != null)
                {
                    var frame = _input.LastFrame;
                    if ((frame.trackingFlags & (uint)TrackingFlags.Head) != 0 &&
                        frame.head.position.y > 0.8f)
                    {
                        _layoutHeadPose = frame.head;
                        Debug.Log("[3OS] layout head from XR tracking");
                        yield break;
                    }
                }

                var cam = Camera.main;
                if (cam != null && cam.transform.position.y > 0.8f)
                {
                    _layoutHeadPose =
                        InteropStructs.ToOpenXrPose(new Pose(cam.transform.position, cam.transform.rotation));
                    Debug.Log("[3OS] layout head from Camera.main (elevated)");
                    yield break;
                }

                yield return null;
            }

            if (_input != null && (_input.LastFrame.trackingFlags & (uint)TrackingFlags.Head) != 0)
            {
                _layoutHeadPose = _input.LastFrame.head;
                if (_layoutHeadPose.position.y < 0.8f)
                    _layoutHeadPose.position.y = 1.5f;
                Debug.LogWarning("[3OS] layout head from XR (y clamped)");
                yield break;
            }

            _layoutHeadPose = new ThreeOSPose
            {
                position = new ThreeOSVec3 { x = 0f, y = 1.5f, z = 0f },
                orientation = new ThreeOSQuat { x = 0f, y = 0f, z = 0f, w = 1f },
            };
            Debug.LogWarning("[3OS] layout using fallback head pose");
        }

        #region Update Methods
        private void Update()
        {
            if (!DemoActive || _bridge == null || !_bridge.IsLoaded || _input == null)
                return;
            if (_topology != null && _topology.LatticeActive)
                return;

            // Input must stay per-frame (edges / hold / drag).
            _interaction.HandleTriggerSelectAndDrag();
            _multiChoice?.Tick();
            _interaction.UpdateInsertArrow();
            _fs.HandleStorageEvents();

            if (_bridge.TryGetSelection(out var selectedId) && selectedId != _lastSelectionId)
            {
                _lastSelectionId = selectedId;
                _selectionMatsDirty = true;
            }

            // Ghosts track portal/near-field motion; far views only during box/insert anims.
            var snapPending = _bridge.TryGetSnap(out var snap) && snap.pending != 0;
            if (GhostDragging || snapPending || _bridge.VoodooPortalCount > 0)
                _ghostsDirty = true;
            if (InsertAnimBusy || BoxAnimBusy || _animLockedIds.Count > 0)
                _viewsDirty = true;

            if (_viewsDirty)
            {
                _glyphs.SyncViewsFromKernel();
                _viewsDirty = false;
            }

            if (_ghostsDirty)
            {
                _glyphs.SyncGhostViewsFromKernel();
                _ghostsDirty = false;
            }

            // Hover chrome only while something can be hovered.
            if (_expanded.ExpandedBoxes.Count > 0)
                _dismiss.UpdateExpandedDismissAffordances();
            if (_bridge.VoodooPortalCount > 0)
                _dismiss.UpdatePortalDismissAffordances();

            if (_selectionMatsDirty)
            {
                _glyphs.SyncSelectionMaterials();
                _selectionMatsDirty = false;
            }

            var billboardSel = _lastSelectionId == ulong.MaxValue ? 0UL : _lastSelectionId;
            _glyphs.BillboardSelectedAndVoodooLabels(billboardSel);

            // Catch missed dirty marks (portal open, kernel layout) without 72 Hz scans.
            if (Time.unscaledTime >= _nextSafetyReconcileAt)
            {
                _nextSafetyReconcileAt = Time.unscaledTime + 0.35f;
                MarkStorageVisualsDirty();
            }
        }

        #region Update Helpers
        // Frame helpers live on Glyphs / Dismiss / Interaction / Fs collaborators.
        #endregion

        #endregion

        private IEnumerator InstallPacks()
        {
            var installed = 0;
            var packs = Resources.LoadAll<TextAsset>("3OS/glyphs");
            Debug.Log($"[3OS] Resources.LoadAll(3OS/glyphs) → {packs?.Length ?? 0} TextAsset(s)");
            if (packs != null)
            {
                foreach (var pack in packs)
                {
                    if (pack == null || pack.bytes == null || pack.bytes.Length == 0)
                        continue;
                    if (_bridge.InstallGlyphPack(pack.bytes))
                    {
                        installed++;
                        Debug.Log($"[3OS] installed glyph pack resource {pack.name} ({pack.bytes.Length} bytes)");
                    }
                    else
                        Debug.LogWarning($"[3OS] failed pack {pack.name}: {_bridge.LastError}");
                }
            }

            if (installed < 3)
            {
                foreach (var fileName in new[]
                         { "box.3glyph", "box_open.3glyph", "gltf_mark.3glyph", "scissors.3glyph",
                           "heart.3glyph", "heart_filled.3glyph", "arrow.3glyph", "mic.3glyph",
                           "mr.3glyph", "vr.3glyph" })
                {
                    var url = Path.Combine(Application.streamingAssetsPath, "3OS", "glyphs", fileName);
#if UNITY_ANDROID && !UNITY_EDITOR
                    using (var req = UnityWebRequest.Get(url))
                    {
                        yield return req.SendWebRequest();
                        if (req.result != UnityWebRequest.Result.Success ||
                            req.downloadHandler?.data == null ||
                            req.downloadHandler.data.Length == 0)
                        {
                            Debug.LogWarning($"[3OS] StreamingAssets pack miss {fileName}: {req.error}");
                            continue;
                        }
                        if (_bridge.InstallGlyphPack(req.downloadHandler.data))
                        {
                            installed++;
                            Debug.Log(
                                $"[3OS] installed StreamingAssets pack {fileName} ({req.downloadHandler.data.Length} bytes)");
                        }
                        else
                            Debug.LogWarning($"[3OS] failed StreamingAssets pack {fileName}: {_bridge.LastError}");
                    }
#else
                    if (File.Exists(url))
                    {
                        var bytes = File.ReadAllBytes(url);
                        if (_bridge.InstallGlyphPack(bytes))
                        {
                            installed++;
                            Debug.Log($"[3OS] installed StreamingAssets pack {fileName} ({bytes.Length} bytes)");
                        }
                        else
                            Debug.LogWarning($"[3OS] failed StreamingAssets pack {fileName}: {_bridge.LastError}");
                    }
                    yield return null;
#endif
                }
            }

            if (installed < 3)
                Debug.LogWarning(
                    $"[3OS] only {installed}/3 glyph packs installed — continuing with procedural glyphs");
        }

        private bool LoadDualMindPalaceFixture(ThreeOSPose head)
        {
            _bridge.StorageClear();
            _bridge.MindPalaceClear();
            var leftAnchor = MindPalaceFixturePose(head, -MindPalaceFixtureAzimuthDeg);
            var rightAnchor = MindPalaceFixturePose(head, MindPalaceFixtureAzimuthDeg);
            if (!_bridge.TryRegisterMindPalace(DemoFolderName, leftAnchor, out var mpL) ||
                !_bridge.TryRegisterMindPalace(DemoFolderName2, rightAnchor, out var mpR))
            {
                _status = $"mind palace register failed: {_bridge.LastError}";
                return false;
            }
            if (!LoadDemoDirectory(_demoRoot, mpL, clearFirst: false))
                return false;
            if (string.IsNullOrEmpty(_demoRoot2) ||
                !LoadDemoDirectory(_demoRoot2, mpR, clearFirst: false))
                Debug.LogWarning($"[3OS] Demo2 load weak: {_status} — continuing with MindPalace L only");
            if (!_bridge.LayoutMindPalace(mpL, leftAnchor))
                Debug.LogWarning($"[3OS] layout MindPalace L: {_bridge.LastError}");
            if (!_bridge.LayoutMindPalace(mpR, rightAnchor))
                Debug.LogWarning($"[3OS] layout MindPalace R: {_bridge.LastError}");
            Debug.Log($"[3OS] dual-mind-palace fixture L={mpL} R={mpR}");
            return true;
        }

        private static ThreeOSPose MindPalaceFixturePose(ThreeOSPose head, float azimuthDeg)
        {
            var q = new Quaternion(head.orientation.x, head.orientation.y, head.orientation.z,
                head.orientation.w);
            var fwd = q * new Vector3(0f, 0f, -1f);
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
                fwd = new Vector3(0f, 0f, -1f);
            fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            var rad = azimuthDeg * Mathf.Deg2Rad;
            var dir = (Mathf.Cos(rad) * fwd + Mathf.Sin(rad) * right).normalized;
            var pos = new Vector3(
                head.position.x + dir.x * MindPalaceFixtureRangeM,
                head.position.y + MindPalaceFixtureHeightM,
                head.position.z + dir.z * MindPalaceFixtureRangeM);
            var face = -dir;
            var yaw = Mathf.Atan2(face.x, -face.z);
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

        private bool LoadDemoDirectory(string root) =>
            LoadDemoDirectory(root, mindPalaceId: 0, clearFirst: true);

        internal bool LoadDemoDirectoryPublic(string root, ulong mindPalaceId, bool clearFirst) =>
            LoadDemoDirectory(root, mindPalaceId, clearFirst);

        private bool LoadDemoDirectory(string root, ulong mindPalaceId, bool clearFirst)
        {
            if (clearFirst)
                _bridge.StorageClear();
            if (string.IsNullOrEmpty(root) ||
                (!Directory.Exists(root) && !ThreeOSAndroidStorage.Exists(root)))
            {
                _status = $"demo dir missing: {root}";
                Debug.LogError($"[3OS] {_status}");
                return false;
            }

            var folderLeaf = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidateRoot in _fs.CandidateDemoRootsFor(folderLeaf))
            {
                if (string.IsNullOrEmpty(candidateRoot)) continue;
                if (!Directory.Exists(candidateRoot) && !ThreeOSAndroidStorage.Exists(candidateRoot))
                    continue;
                foreach (var name in ThreeOSAndroidStorage.ListFileNames(candidateRoot))
                    fileNames.Add(name);
                foreach (var name in ThreeOSAndroidStorage.ListDirectoryNames(candidateRoot))
                    dirNames.Add(name);
                foreach (var probe in new[] { "simple.glb", "simple.gltf", "marker.txt" })
                {
                    if (ThreeOSAndroidStorage.TryProbeFileName(candidateRoot, probe))
                        fileNames.Add(probe);
                }
            }

            var files = new List<string>(fileNames);
            var dirs = new List<string>(dirNames);
            files.Sort(StringComparer.OrdinalIgnoreCase);
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            var boxes = 0;
            try
            {
                foreach (var name in files)
                {
                    if (!_bridge.StorageAddObject(name, name, out var eid))
                    {
                        _status = $"add object failed: {_bridge.LastError}";
                        Debug.LogError($"[3OS] {_status}");
                        return false;
                    }
                    if (mindPalaceId != 0 && !_bridge.SetMindPalaceRoot(eid, mindPalaceId))
                        Debug.LogWarning($"[3OS] set MindPalace failed: {_bridge.LastError}");
                    added++;
                }
                foreach (var name in dirs)
                {
                    var dirPath = Path.Combine(root, name);
                    var childCount = CountFolderContents(dirPath, name, root);
                    if (!_bridge.StorageAddBox(name, name, childCount, out var bid))
                    {
                        _status = $"add box failed: {_bridge.LastError}";
                        Debug.LogError($"[3OS] {_status}");
                        return false;
                    }
                    if (mindPalaceId != 0 && !_bridge.SetMindPalaceRoot(bid, mindPalaceId))
                        Debug.LogWarning($"[3OS] set MindPalace failed: {_bridge.LastError}");
                    added++;
                    boxes++;
                }
            }
            catch (Exception ex)
            {
                _status = $"list demo dir failed: {ex.Message}";
                Debug.LogError($"[3OS] {_status}");
                return false;
            }

            if (added == 0)
            {
                _status = $"demo dir empty: {root}";
                Debug.LogError($"[3OS] {_status}");
                return false;
            }
            _status = $"listed {added} ({boxes} folders)";
            Debug.Log($"[3OS] {_status} from {root} (files={files.Count} dirs={dirs.Count})");
            return true;
        }

        private uint CountFolderContents(string dirPath, string folderName, string primaryRoot)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddFrom(string folder)
            {
                if (string.IsNullOrEmpty(folder)) return;
                foreach (var file in ThreeOSAndroidStorage.ListFileNames(folder))
                    names.Add(file);
                foreach (var dir in ThreeOSAndroidStorage.ListDirectoryNames(folder))
                    names.Add(dir);
            }
            AddFrom(dirPath);
            foreach (var candidateRoot in _fs.CandidateDemoRoots())
            {
                if (!string.IsNullOrEmpty(candidateRoot))
                    AddFrom(Path.Combine(candidateRoot, folderName));
            }
            if (!string.IsNullOrEmpty(dirPath) && !string.IsNullOrEmpty(primaryRoot) &&
                dirPath.StartsWith(primaryRoot, StringComparison.OrdinalIgnoreCase))
            {
                var rel = dirPath.Substring(primaryRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrEmpty(rel) &&
                    !string.Equals(rel, folderName, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var candidateRoot in _fs.CandidateDemoRoots())
                    {
                        if (!string.IsNullOrEmpty(candidateRoot))
                            AddFrom(Path.Combine(candidateRoot, rel));
                    }
                }
            }
            return (uint)names.Count;
        }

        internal void RebuildViews()
        {
            _expanded.TeardownAllExpandedBoxes(collapseKernel: true);
            foreach (var kv in _glyphs.Views)
                _glyphs.DestroyGlyphView(kv.Value);
            _glyphs.Views.Clear();
            _glyphs.ClearGhostViews();
            _dismiss.ClearPortalDismissUi();
            var count = _bridge.StorageItemCount();
            for (uint i = 0; i < count; i++)
            {
                if (!_bridge.TryGetWorkspaceItem(i, out var item))
                    continue;
                if ((item.flags & 1u) == 0)
                    continue;
                _glyphs.Views[item.entityId] = _glyphs.CreateView(item);
            }

            MarkStorageVisualsDirty();
        }

        private void OnDestroy()
        {
            _expanded?.TeardownAllExpandedBoxes(collapseKernel: false);
            _dismiss?.ClearPortalDismissUi();
        }

        internal bool TryFindItem(ulong entityId, out InteropWorkspaceItem item) =>
            _interaction.TryFindItem(entityId, out item);

        internal bool IsKinematicallyDriven(ulong entityId) =>
            _interaction.IsKinematicallyDriven(entityId);

        internal Pose SelectAimUnity(int hand, out Vector3 rayDir) =>
            _interaction.SelectAimUnity(hand, out rayDir);

        internal Pose SelectAimUnity(out Vector3 rayDir) =>
            _interaction.SelectAimUnity(out rayDir);

        private static string CombineRootRel(string root, string rel)
        {
            if (string.IsNullOrEmpty(root))
                return rel ?? string.Empty;
            if (string.IsNullOrEmpty(rel))
                return root;
            var path = root;
            foreach (var part in ThreeOSStorageWriteThrough.NormalizeDemoRelPublic(rel).Split('/'))
            {
                if (part.Length == 0)
                    continue;
                path = Path.Combine(path, part);
            }
            return path;
        }
    }
}
