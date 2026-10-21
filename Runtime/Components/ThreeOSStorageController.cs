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
        public const string DemoFolderName3 = "3OS_Demo3";
        public const float MindPalaceFixtureRangeM = 10f * 0.3048f;
        public const float MindPalaceFixtureHeightM = 4.5f * 0.3048f;
        /// <summary>Center Demo3 cluster — ahead of player, near floor.</summary>
        public const float MindPalaceFixtureHeightFloorM = 1.0f * 0.3048f;
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
        private string _demoRoot3 = string.Empty;
        private ThreeOSPose _layoutHeadPose;
        private bool _waitingForAllFilesAccess;
        private string _selectionLabel = "none";
        private ulong _xHoverBoxId;
        private bool _leftTriggerWasDown;
        private bool _rightTriggerWasDown;

        // Phase 0.7 Task 7.8 — Final Demo Palace reveal / soft-dismiss
        public const float PalaceClusterScaleSeconds = 0.5f;
        private bool _palaceFixtureLoaded;
        private bool _palaceSoftDismissed;
        private ulong _mpLeftId;
        private ulong _mpRightId;
        private ulong _mpCenterId;
        private ThreeOSPose _mpLeftAnchor;
        private ThreeOSPose _mpRightAnchor;
        private ThreeOSPose _mpCenterAnchor;
        private readonly List<ulong> _palaceEntityIds = new List<ulong>();
        private readonly Dictionary<ulong, ulong> _entityMindPalace = new Dictionary<ulong, ulong>();
        private ulong _mrPersistedSelectionId;
        private ulong _mrPersistedOriginalMpId;
        private Vector3 _mrPersistedWorldPos;
        private Quaternion _mrPersistedWorldRot = Quaternion.identity;
        private bool _palaceClusterAnimBusy;
        /// <summary>
        /// True from palace view rebuild until cluster scale-in finishes. SyncViews must not
        /// SetActive(true) at full size or glyphs flash before the intro animation.
        /// </summary>
        private bool _palacePendingScaleIn;
        private List<string> _precacheFiles1;
        private List<string> _precacheDirs1;
        private List<string> _precacheFiles2;
        private List<string> _precacheDirs2;
        private List<string> _precacheFiles3;
        private List<string> _precacheDirs3;
        private bool _palaceNamesPrecached;

        public int PossessDriveHand =>
            (_possessing || _ghostDragging) && _selectHand >= 0 ? _selectHand : -1;
        public bool DemoActive { get; private set; }
        public string Status => _status;
        public string DemoRoot => _demoRoot;
        public string SelectionLabel => _selectionLabel;
        public bool PalaceFixtureLoaded => _palaceFixtureLoaded;
        public bool PalaceSoftDismissed => _palaceSoftDismissed;
        public bool PalaceClusterAnimBusy => _palaceClusterAnimBusy;
        public bool PalacePendingScaleIn => _palacePendingScaleIn;
        public ulong MrPersistedSelectionId => _mrPersistedSelectionId;

        internal bool IsPalaceEntity(ulong entityId) =>
            entityId != 0 && _entityMindPalace.ContainsKey(entityId);

        /// <summary>
        /// Final Demo Palace: billboard every far-field label (not only selection) so TextMesh
        /// backfaces do not clip when looking toward the field.
        /// </summary>
        public static bool BillboardAllFarFieldLabels { get; set; }

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

        /// <summary>
        /// Final Demo 7.9 — non-box file double-pinch.
        /// Args: entityId, workspace item, select-hand index (0=L, 1=R).
        /// </summary>
        public System.Action<ulong, InteropWorkspaceItem, int> FileDoublePinchHandler;

        /// <summary>
        /// If set and returns true, a short primary release on that hand is consumed
        /// (e.g. cancel an armed app-drop spinner) and must not select/deselect.
        /// </summary>
        public System.Func<int, bool> ShortPinchConsumeHandler;

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
            yield return null;
            yield return ThreeOSDemoRootBootstrap.EnsureDemoRoot(
                DemoFolderName, DemoSeedPolicy.RequireContent, null,
                r => _demoRoot = r, s => _status = s);
            yield return null;
            yield return ThreeOSDemoRootBootstrap.EnsureDemoRoot(
                DemoFolderName2, DemoSeedPolicy.SoftSeedMindPalaceMarker, _demoRoot,
                r => _demoRoot2 = r, s => _status = s);
            yield return null;
            yield return ThreeOSDemoRootBootstrap.EnsureDemoRoot(
                DemoFolderName3, DemoSeedPolicy.SoftSeedMindPalaceMarker, _demoRoot,
                r => _demoRoot3 = r, s => _status = s);
            if (string.IsNullOrEmpty(_demoRoot))
            {
                Debug.LogError($"[3OS] demo load aborted: {_status}");
                yield break;
            }

            HideNonGlyphMovables();
            yield return WaitForHeadPose();
            yield return null;

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
                // Warm Documents listings during Act I so wipe→Palace does not hitch on FS.
                StartCoroutine(PrecachePalaceDirectoryNamesRoutine());
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
                // Ghost sync rewrites base color with rim cleared — reapply selection same frame.
                _selectionMatsDirty = true;
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
            var installedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Spread glyph installs across frames — double-loading every pack was a cold-boot hitch.
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
                        installedNames.Add(pack.name);
                        Debug.Log($"[3OS] installed glyph pack resource {pack.name} ({pack.bytes.Length} bytes)");
                    }
                    else
                        Debug.LogWarning($"[3OS] failed pack {pack.name}: {_bridge.LastError}");
                    yield return null;
                }
            }

            // StreamingAssets fills gaps only (e.g. blender_mark if Resources missed it).
            foreach (var fileName in new[]
                     { "box.3glyph", "box_open.3glyph", "gltf_mark.3glyph", "blender_mark.3glyph",
                       "scissors.3glyph", "heart.3glyph", "heart_filled.3glyph", "arrow.3glyph",
                       "mic.3glyph", "mr.3glyph", "vr.3glyph" })
            {
                var stem = Path.GetFileNameWithoutExtension(fileName);
                if (installedNames.Contains(stem))
                {
                    yield return null;
                    continue;
                }

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
                        installedNames.Add(stem);
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
                        installedNames.Add(stem);
                        Debug.Log($"[3OS] installed StreamingAssets pack {fileName} ({bytes.Length} bytes)");
                    }
                    else
                        Debug.LogWarning($"[3OS] failed StreamingAssets pack {fileName}: {_bridge.LastError}");
                }
                yield return null;
#endif
            }

            if (installed < 3)
                Debug.LogWarning(
                    $"[3OS] only {installed}/3 glyph packs installed — continuing with procedural glyphs");
        }

        private bool LoadDualMindPalaceFixture(ThreeOSPose origin)
        {
            _bridge.StorageClear();
            _bridge.MindPalaceClear();
            _palaceEntityIds.Clear();
            _entityMindPalace.Clear();
            _mrPersistedSelectionId = 0;
            _mrPersistedOriginalMpId = 0;
            var leftAnchor = MindPalaceFixturePose(origin, -MindPalaceFixtureAzimuthDeg);
            var rightAnchor = MindPalaceFixturePose(origin, MindPalaceFixtureAzimuthDeg);
            var centerAnchor = MindPalaceFixturePose(origin, 0f, MindPalaceFixtureHeightFloorM);
            if (!_bridge.TryRegisterMindPalace(DemoFolderName, leftAnchor, out var mpL) ||
                !_bridge.TryRegisterMindPalace(DemoFolderName2, rightAnchor, out var mpR))
            {
                _status = $"mind palace register failed: {_bridge.LastError}";
                return false;
            }

            _mpLeftId = mpL;
            _mpRightId = mpR;
            _mpLeftAnchor = leftAnchor;
            _mpRightAnchor = rightAnchor;
            _mpCenterId = 0;
            _mpCenterAnchor = centerAnchor;
            if (_bridge.TryRegisterMindPalace(DemoFolderName3, centerAnchor, out var mpC))
                _mpCenterId = mpC;
            else
                Debug.LogWarning($"[3OS] Demo3 MindPalace register weak: {_bridge.LastError}");

            if (!LoadDemoDirectory(_demoRoot, mpL, clearFirst: false, cheapChildCounts: true))
                return false;
            if (string.IsNullOrEmpty(_demoRoot2) ||
                !LoadDemoDirectory(_demoRoot2, mpR, clearFirst: false, cheapChildCounts: true))
                Debug.LogWarning($"[3OS] Demo2 load weak: {_status} — continuing with MindPalace L only");
            if (_mpCenterId != 0 && !string.IsNullOrEmpty(_demoRoot3) &&
                !LoadDemoDirectory(_demoRoot3, _mpCenterId, clearFirst: false, cheapChildCounts: true))
                Debug.LogWarning($"[3OS] Demo3 load weak: {_status}");
            CapturePalaceEntityIds();
            if (!_bridge.LayoutMindPalace(mpL, leftAnchor))
                Debug.LogWarning($"[3OS] layout MindPalace L: {_bridge.LastError}");
            if (!_bridge.LayoutMindPalace(mpR, rightAnchor))
                Debug.LogWarning($"[3OS] layout MindPalace R: {_bridge.LastError}");
            if (_mpCenterId != 0 && !_bridge.LayoutMindPalace(_mpCenterId, centerAnchor))
                Debug.LogWarning($"[3OS] layout MindPalace C: {_bridge.LastError}");
            _palaceFixtureLoaded = true;
            _palaceSoftDismissed = false;
            Debug.Log($"[3OS] mind-palace fixture L={mpL} R={mpR} C={_mpCenterId}");
            return true;
        }

        /// <summary>
        /// Layout origin from a transform's world position + yaw.
        /// Final Demo 7.8.b uses <c>Player_Spawn_Point</c> — not HMD pose.
        /// Kernel / <see cref="MindPalaceFixturePose"/> treat OpenXR <c>-Z</c> as forward; after
        /// Unity↔OX conversion that maps to Unity <c>-Z</c>. Aim Unity look at
        /// <c>-spawn.forward</c> so OX <c>-Z</c> aligns with Unity <c>+Z</c> on the spawn
        /// (Player_Spawn_Point forward).
        /// </summary>
        public static ThreeOSPose PoseFromSpawnTransform(Transform spawn)
        {
            if (spawn == null)
                return default;
            var fwd = spawn.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
                fwd = Vector3.forward;
            fwd.Normalize();
            var unityPose = new Pose(spawn.position, Quaternion.LookRotation(-fwd, Vector3.up));
            return InteropStructs.ToOpenXrPose(unityPose);
        }

        private static ThreeOSPose MindPalaceFixturePose(ThreeOSPose origin, float azimuthDeg,
            float heightM = -1f)
        {
            var q = new Quaternion(origin.orientation.x, origin.orientation.y, origin.orientation.z,
                origin.orientation.w);
            var fwd = q * new Vector3(0f, 0f, -1f);
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
                fwd = new Vector3(0f, 0f, -1f);
            fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            var rad = azimuthDeg * Mathf.Deg2Rad;
            var dir = (Mathf.Cos(rad) * fwd + Mathf.Sin(rad) * right).normalized;
            var h = heightM >= 0f ? heightM : MindPalaceFixtureHeightM;
            var pos = new Vector3(
                origin.position.x + dir.x * MindPalaceFixtureRangeM,
                origin.position.y + h,
                origin.position.z + dir.z * MindPalaceFixtureRangeM);
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

        void CapturePalaceEntityIds()
        {
            // Do not clear _entityMindPalace — LoadDemoDirectory already tagged mp membership.
            _palaceEntityIds.Clear();
            var count = _bridge.StorageItemCount();
            for (uint i = 0; i < count; i++)
            {
                if (!_bridge.TryGetWorkspaceItem(i, out var item))
                    continue;
                _palaceEntityIds.Add(item.entityId);
            }
        }

        /// <summary>
        /// Final Demo 7.8.b — async Palace reveal. Must not run sync on the wipe settle frame
        /// (Quest dies on a multi-second Documents listing + RebuildViews hitch).
        /// </summary>
        public IEnumerator RevealPalaceAtSpawnRoutine(Transform spawn)
        {
            if (_bridge == null || !_bridge.IsLoaded)
            {
                Debug.LogError("[3OS] RevealPalace: bridge not loaded");
                yield break;
            }

            if (spawn == null)
            {
                Debug.LogError("[3OS] RevealPalace: spawn is null");
                yield break;
            }

            // Prefer Act I precache; wait briefly if still warming (names only — not the hitch).
            if (!_palaceNamesPrecached && !string.IsNullOrEmpty(_demoRoot))
            {
                var waitPc = Time.realtimeSinceStartup + 2f;
                while (!_palaceNamesPrecached && Time.realtimeSinceStartup < waitPc)
                    yield return null;
            }

            if (_palaceFixtureLoaded && _palaceSoftDismissed)
            {
                _palacePendingScaleIn = true;
                if (!TryRestorePalaceField(spawn))
                    Debug.LogError($"[3OS] Palace restore failed: {_status}");
                HidePalaceGlyphsPendingScaleIn(_mrPersistedSelectionId);
                yield break;
            }

            if (_palaceFixtureLoaded && !_palaceSoftDismissed)
                yield break;

            var origin = PoseFromSpawnTransform(spawn);
            yield return LoadDualMindPalaceFixtureRoutine(origin);
            if (!_palaceFixtureLoaded)
            {
                Debug.LogError($"[3OS] Palace fixture load failed: {_status}");
                yield break;
            }

            // Hide before first render frame — SyncViews must not flash full-size glyphs.
            // Precache → many views/frame so prepare finishes during the sky wipe.
            _palacePendingScaleIn = true;
            var viewsPer = _palaceNamesPrecached ? 6 : 2;
            yield return RebuildViewsSpreadRoutine(viewsPerFrame: viewsPer);
            HidePalaceGlyphsPendingScaleIn(_mrPersistedSelectionId);
        }

        /// <summary>Sync wrapper kept for non-Final-Demo callers; prefer the routine on device.</summary>
        public bool TryRevealPalaceAtSpawn(Transform spawn)
        {
            if (_bridge == null || !_bridge.IsLoaded || spawn == null)
                return false;
            if (_palaceFixtureLoaded && _palaceSoftDismissed)
                return TryRestorePalaceField(spawn);
            if (_palaceFixtureLoaded && !_palaceSoftDismissed)
                return true;
            var origin = PoseFromSpawnTransform(spawn);
            if (!LoadDualMindPalaceFixture(origin))
                return false;
            RebuildViews();
            return true;
        }

        IEnumerator LoadDualMindPalaceFixtureRoutine(ThreeOSPose origin)
        {
            _bridge.StorageClear();
            _bridge.MindPalaceClear();
            _palaceEntityIds.Clear();
            _entityMindPalace.Clear();
            _mrPersistedSelectionId = 0;
            _mrPersistedOriginalMpId = 0;
            yield return null;

            var leftAnchor = MindPalaceFixturePose(origin, -MindPalaceFixtureAzimuthDeg);
            var rightAnchor = MindPalaceFixturePose(origin, MindPalaceFixtureAzimuthDeg);
            var centerAnchor = MindPalaceFixturePose(origin, 0f, MindPalaceFixtureHeightFloorM);
            if (!_bridge.TryRegisterMindPalace(DemoFolderName, leftAnchor, out var mpL) ||
                !_bridge.TryRegisterMindPalace(DemoFolderName2, rightAnchor, out var mpR))
            {
                _status = $"mind palace register failed: {_bridge.LastError}";
                yield break;
            }

            _mpLeftId = mpL;
            _mpRightId = mpR;
            _mpLeftAnchor = leftAnchor;
            _mpRightAnchor = rightAnchor;
            _mpCenterId = 0;
            _mpCenterAnchor = centerAnchor;
            if (_bridge.TryRegisterMindPalace(DemoFolderName3, centerAnchor, out var mpC))
                _mpCenterId = mpC;
            else
                Debug.LogWarning($"[3OS] Demo3 MindPalace register weak: {_bridge.LastError}");
            yield return null;

            // Gentle adds — Act I already warmed names; wipe gives more time than before.
            var addsPer = _palaceNamesPrecached ? 4 : 2;
            var ok1 = false;
            yield return LoadDemoDirectoryRoutine(_demoRoot, mpL, clearFirst: false,
                addsPerFrame: addsPer, skipChildCounts: true, ok => ok1 = ok);
            if (!ok1)
                yield break;
            Debug.Log("[3OS] Palace Demo1 chunk-loaded");
            yield return null;

            var ok2 = true;
            if (!string.IsNullOrEmpty(_demoRoot2))
            {
                yield return LoadDemoDirectoryRoutine(_demoRoot2, mpR, clearFirst: false,
                    addsPerFrame: addsPer, skipChildCounts: true, ok => ok2 = ok);
                if (!ok2)
                    Debug.LogWarning($"[3OS] Demo2 load weak: {_status} — continuing with MindPalace L only");
            }

            yield return null;
            if (_mpCenterId != 0 && !string.IsNullOrEmpty(_demoRoot3))
            {
                var ok3 = true;
                yield return LoadDemoDirectoryRoutine(_demoRoot3, _mpCenterId, clearFirst: false,
                    addsPerFrame: addsPer, skipChildCounts: true, ok => ok3 = ok);
                if (!ok3)
                    Debug.LogWarning($"[3OS] Demo3 load weak: {_status}");
            }

            CapturePalaceEntityIds();
            yield return null;
            if (!_bridge.LayoutMindPalace(mpL, leftAnchor))
                Debug.LogWarning($"[3OS] layout MindPalace L: {_bridge.LastError}");
            yield return null;
            yield return null;
            if (!_bridge.LayoutMindPalace(mpR, rightAnchor))
                Debug.LogWarning($"[3OS] layout MindPalace R: {_bridge.LastError}");
            yield return null;
            if (_mpCenterId != 0 && !_bridge.LayoutMindPalace(_mpCenterId, centerAnchor))
                Debug.LogWarning($"[3OS] layout MindPalace C: {_bridge.LastError}");
            yield return null;
            _palaceFixtureLoaded = true;
            _palaceSoftDismissed = false;
            Debug.Log(
                $"[3OS] mind-palace fixture L={mpL} R={mpR} C={_mpCenterId} entities={_palaceEntityIds.Count}");
            yield return null;
        }

        IEnumerator PrecachePalaceDirectoryNamesRoutine()
        {
            // Plenty of Act I time — delay and spread so cold open stays smooth.
            yield return new WaitForSecondsRealtime(2.5f);
            yield return PrecacheOneRootRoutine(_demoRoot, files => _precacheFiles1 = files,
                dirs => _precacheDirs1 = dirs);
            yield return new WaitForSecondsRealtime(0.35f);
            yield return PrecacheOneRootRoutine(_demoRoot2, files => _precacheFiles2 = files,
                dirs => _precacheDirs2 = dirs);
            yield return new WaitForSecondsRealtime(0.35f);
            yield return PrecacheOneRootRoutine(_demoRoot3, files => _precacheFiles3 = files,
                dirs => _precacheDirs3 = dirs);
            _palaceNamesPrecached = true;
            Debug.Log(
                $"[3OS] Palace names precached " +
                $"d1=f{_precacheFiles1?.Count ?? 0}/d{_precacheDirs1?.Count ?? 0} " +
                $"d2=f{_precacheFiles2?.Count ?? 0}/d{_precacheDirs2?.Count ?? 0} " +
                $"d3=f{_precacheFiles3?.Count ?? 0}/d{_precacheDirs3?.Count ?? 0}");
        }

        IEnumerator PrecacheOneRootRoutine(string root, Action<List<string>> setFiles,
            Action<List<string>> setDirs)
        {
            var files = new List<string>();
            var dirs = new List<string>();
            if (string.IsNullOrEmpty(root))
            {
                setFiles?.Invoke(files);
                setDirs?.Invoke(dirs);
                yield break;
            }

            yield return null;
            try
            {
                foreach (var name in ThreeOSAndroidStorage.ListFileNames(root))
                    files.Add(name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[3OS] precache ListFileNames: {ex.Message}");
            }

            yield return null;
            try
            {
                foreach (var name in ThreeOSAndroidStorage.ListDirectoryNames(root))
                    dirs.Add(name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[3OS] precache ListDirectoryNames: {ex.Message}");
            }

            yield return null;
            foreach (var probe in new[] { "simple.glb", "simple.gltf", "DemonA.blend", "marker.txt" })
            {
                if (ThreeOSAndroidStorage.TryProbeFileName(root, probe) &&
                    !files.Exists(n => string.Equals(n, probe, StringComparison.OrdinalIgnoreCase)))
                    files.Add(probe);
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            setFiles?.Invoke(files);
            setDirs?.Invoke(dirs);
            yield return null;
        }

        /// <summary>
        /// Chunked demo-root ingest for Final Demo Palace. Uses Act I precache when available.
        /// Skips per-folder child scans; adds ≤addsPerFrame items/frame.
        /// </summary>
        IEnumerator LoadDemoDirectoryRoutine(string root, ulong mindPalaceId, bool clearFirst,
            int addsPerFrame, bool skipChildCounts, Action<bool> done)
        {
            if (clearFirst)
                _bridge.StorageClear();
            if (string.IsNullOrEmpty(root) ||
                (!Directory.Exists(root) && !ThreeOSAndroidStorage.Exists(root)))
            {
                _status = $"demo dir missing: {root}";
                Debug.LogError($"[3OS] {_status}");
                done?.Invoke(false);
                yield break;
            }

            yield return null;

            List<string> fileNames;
            List<string> dirNames;
            var usePrecache = _palaceNamesPrecached &&
                              ((root == _demoRoot && _precacheDirs1 != null) ||
                               (root == _demoRoot2 && _precacheDirs2 != null) ||
                               (root == _demoRoot3 && _precacheDirs3 != null));
            if (usePrecache && root == _demoRoot)
            {
                fileNames = new List<string>(_precacheFiles1 ?? new List<string>());
                dirNames = new List<string>(_precacheDirs1 ?? new List<string>());
                Debug.Log($"[3OS] using precache for Demo1 ({fileNames.Count}f/{dirNames.Count}d)");
            }
            else if (usePrecache && root == _demoRoot2)
            {
                fileNames = new List<string>(_precacheFiles2 ?? new List<string>());
                dirNames = new List<string>(_precacheDirs2 ?? new List<string>());
                Debug.Log($"[3OS] using precache for Demo2 ({fileNames.Count}f/{dirNames.Count}d)");
            }
            else if (usePrecache && root == _demoRoot3)
            {
                fileNames = new List<string>(_precacheFiles3 ?? new List<string>());
                dirNames = new List<string>(_precacheDirs3 ?? new List<string>());
                Debug.Log($"[3OS] using precache for Demo3 ({fileNames.Count}f/{dirNames.Count}d)");
            }
            else
            {
                fileNames = new List<string>();
                dirNames = new List<string>();
                try
                {
                    foreach (var name in ThreeOSAndroidStorage.ListFileNames(root))
                        fileNames.Add(name);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[3OS] ListFileNames({root}): {ex.Message}");
                }

                yield return null;
                try
                {
                    foreach (var name in ThreeOSAndroidStorage.ListDirectoryNames(root))
                        dirNames.Add(name);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[3OS] ListDirectoryNames({root}): {ex.Message}");
                }

                yield return null;
                foreach (var probe in new[] { "simple.glb", "simple.gltf", "DemonA.blend", "marker.txt" })
                {
                    if (ThreeOSAndroidStorage.TryProbeFileName(root, probe) &&
                        !fileNames.Exists(n => string.Equals(n, probe, StringComparison.OrdinalIgnoreCase)))
                        fileNames.Add(probe);
                }

                fileNames.Sort(StringComparer.OrdinalIgnoreCase);
                dirNames.Sort(StringComparer.OrdinalIgnoreCase);
            }

            yield return null;

            var per = Mathf.Max(1, addsPerFrame);
            var added = 0;
            var boxes = 0;
            var burst = 0;

            for (var i = 0; i < fileNames.Count; i++)
            {
                var name = fileNames[i];
                if (!_bridge.StorageAddObject(name, name, out var eid))
                {
                    _status = $"add object failed: {_bridge.LastError}";
                    Debug.LogError($"[3OS] {_status}");
                    done?.Invoke(false);
                    yield break;
                }

                if (mindPalaceId != 0)
                {
                    _bridge.SetMindPalaceRoot(eid, mindPalaceId);
                    _entityMindPalace[eid] = mindPalaceId;
                }

                added++;
                burst++;
                if (burst >= per)
                {
                    burst = 0;
                    yield return null;
                }
            }

            for (var i = 0; i < dirNames.Count; i++)
            {
                var name = dirNames[i];
                var childCount = skipChildCounts
                    ? 0u
                    : CountFolderContentsShallow(Path.Combine(root, name));
                if (!_bridge.StorageAddBox(name, name, childCount, out var bid))
                {
                    _status = $"add box failed: {_bridge.LastError}";
                    Debug.LogError($"[3OS] {_status}");
                    done?.Invoke(false);
                    yield break;
                }

                if (mindPalaceId != 0)
                {
                    _bridge.SetMindPalaceRoot(bid, mindPalaceId);
                    _entityMindPalace[bid] = mindPalaceId;
                }

                added++;
                boxes++;
                burst++;
                if (burst >= per)
                {
                    burst = 0;
                    yield return null;
                }
            }

            if (added == 0)
            {
                _status = $"demo dir empty: {root}";
                Debug.LogError($"[3OS] {_status}");
                done?.Invoke(false);
                yield break;
            }

            _status = $"listed {added} ({boxes} folders)";
            Debug.Log($"[3OS] {_status} from {root} (chunked files={fileNames.Count} dirs={dirNames.Count})");
            done?.Invoke(true);
        }

        IEnumerator RebuildViewsSpreadRoutine(int viewsPerFrame)
        {
            _expanded.TeardownAllExpandedBoxes(collapseKernel: true);
            foreach (var kv in _glyphs.Views)
                _glyphs.DestroyGlyphView(kv.Value);
            _glyphs.Views.Clear();
            _glyphs.ClearGhostViews();
            _dismiss.ClearPortalDismissUi();
            yield return null;

            var per = Mathf.Max(1, viewsPerFrame);
            var made = 0;
            var count = _bridge.StorageItemCount();
            for (uint i = 0; i < count; i++)
            {
                if (!_bridge.TryGetWorkspaceItem(i, out var item))
                    continue;
                if ((item.flags & 1u) == 0)
                    continue;
                var view = _glyphs.CreateView(item);
                _glyphs.Views[item.entityId] = view;
                // Pending scale-in: never allow a full-size render before the pivot tween.
                if (_palacePendingScaleIn && view.Root != null &&
                    item.entityId != _mrPersistedSelectionId)
                    view.Root.SetActive(false);
                made++;
                if (made % per == 0)
                    yield return null;
            }

            MarkStorageVisualsDirty();
            Debug.Log($"[3OS] Palace RebuildViews spread done views={_glyphs.Views.Count}");
        }

        /// <summary>Re-show soft-dismissed Palace field (MR→VR again) without wiping MR selection.</summary>
        public bool TryRestorePalaceField(Transform spawn)
        {
            if (!_palaceFixtureLoaded || _bridge == null)
                return false;
            var origin = spawn != null ? PoseFromSpawnTransform(spawn) : default;
            if (spawn != null)
            {
                _mpLeftAnchor = MindPalaceFixturePose(origin, -MindPalaceFixtureAzimuthDeg);
                _mpRightAnchor = MindPalaceFixturePose(origin, MindPalaceFixtureAzimuthDeg);
                _mpCenterAnchor = MindPalaceFixturePose(origin, 0f, MindPalaceFixtureHeightFloorM);
            }

            for (var i = 0; i < _palaceEntityIds.Count; i++)
            {
                var id = _palaceEntityIds[i];
                if (id == 0 || id == _mrPersistedSelectionId)
                    continue;
                _bridge.StorageSetInField(id, true);
                if (_entityMindPalace.TryGetValue(id, out var mp) && mp != 0)
                    _bridge.SetMindPalaceRoot(id, mp);
            }

            if (_mpLeftId != 0)
                _bridge.LayoutMindPalace(_mpLeftId, _mpLeftAnchor);
            if (_mpRightId != 0)
                _bridge.LayoutMindPalace(_mpRightId, _mpRightAnchor);
            if (_mpCenterId != 0)
                _bridge.LayoutMindPalace(_mpCenterId, _mpCenterAnchor);

            // Re-link persisted selection to original Palace centroid; keep world pose (anim lock).
            if (_mrPersistedSelectionId != 0)
            {
                if (_mrPersistedOriginalMpId != 0)
                    _bridge.SetMindPalaceRoot(_mrPersistedSelectionId, _mrPersistedOriginalMpId);
                _bridge.StorageSetInField(_mrPersistedSelectionId, true);
                _animLockedIds.Add(_mrPersistedSelectionId);
                if (_glyphs.Views.TryGetValue(_mrPersistedSelectionId, out var selView) &&
                    selView.Root != null)
                {
                    selView.Root.SetActive(true);
                    selView.Root.transform.SetPositionAndRotation(_mrPersistedWorldPos,
                        _mrPersistedWorldRot);
                }
            }

            RebuildViewsPreservingPersisted();
            _palaceSoftDismissed = false;
            return true;
        }

        void RebuildViewsPreservingPersisted()
        {
            GameObject persistRoot = null;
            ThreeOSGlyphView persistView = null;
            if (_mrPersistedSelectionId != 0 &&
                _glyphs.Views.TryGetValue(_mrPersistedSelectionId, out persistView) &&
                persistView.Root != null)
            {
                persistRoot = persistView.Root;
                persistRoot.transform.SetParent(null, true);
                _glyphs.Views.Remove(_mrPersistedSelectionId);
            }

            RebuildViews();

            if (_mrPersistedSelectionId != 0 && persistRoot != null)
            {
                if (_glyphs.Views.TryGetValue(_mrPersistedSelectionId, out var dup) &&
                    dup.Root != null && dup.Root != persistRoot)
                    _glyphs.DestroyGlyphView(dup);
                if (persistView != null)
                {
                    persistView.Root = persistRoot;
                    _glyphs.Views[_mrPersistedSelectionId] = persistView;
                }

                persistRoot.SetActive(true);
                persistRoot.transform.SetPositionAndRotation(_mrPersistedWorldPos, _mrPersistedWorldRot);
                _animLockedIds.Add(_mrPersistedSelectionId);
            }
        }

        /// <summary>7.8.b / 7.8.c — scale each MindPalace node cluster from centroid.</summary>
        public IEnumerator AnimatePalaceClustersScale(float fromScale, float toScale, float duration,
            ulong excludeEntityId = 0)
        {
            if (_palaceClusterAnimBusy)
                yield break;
            _palaceClusterAnimBusy = true;
            try
            {
                var clusters = BuildPalaceClusterIdLists(excludeEntityId);
                if (clusters.Count == 0)
                    yield break;

                const float eps = 0.001f;
                var start = Mathf.Max(eps, fromScale);
                var end = Mathf.Max(eps, toScale);
                var pivots = new List<Transform>(clusters.Count);
                foreach (var ids in clusters)
                {
                    if (!TryMakeClusterPivot(ids, start, out var pivot))
                        continue;
                    pivots.Add(pivot);
                }

                var dur = Mathf.Max(0.05f, duration);
                var t = 0f;
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                    var s = Mathf.Lerp(start, end, u);
                    for (var i = 0; i < pivots.Count; i++)
                    {
                        if (pivots[i] != null)
                            pivots[i].localScale = Vector3.one * s;
                    }

                    yield return null;
                }

                for (var i = 0; i < pivots.Count; i++)
                {
                    var pivot = pivots[i];
                    if (pivot == null)
                        continue;
                    pivot.localScale = Vector3.one * end;
                    var childCount = pivot.childCount;
                    for (var c = childCount - 1; c >= 0; c--)
                    {
                        var child = pivot.GetChild(c);
                        child.SetParent(null, true);
                        if (Mathf.Abs(end - 1f) < 1e-3f)
                            child.localScale = Vector3.one;
                    }

                    Destroy(pivot.gameObject);
                }

                _animLockedIds.Clear();
                if (_mrPersistedSelectionId != 0)
                    _animLockedIds.Add(_mrPersistedSelectionId);
            }
            finally
            {
                _palaceClusterAnimBusy = false;
                // Scale-in finished — SyncViews may activate at full size again.
                if (toScale >= fromScale && toScale >= 0.99f)
                    _palacePendingScaleIn = false;
                MarkStorageVisualsDirty();
            }
        }

        List<List<ulong>> BuildPalaceClusterIdLists(ulong excludeEntityId)
        {
            var byMp = new Dictionary<ulong, List<ulong>>();
            foreach (var kv in _entityMindPalace)
            {
                var id = kv.Key;
                if (id == 0 || id == excludeEntityId)
                    continue;
                // Include inactive roots (hidden pending scale-in).
                if (!_glyphs.Views.TryGetValue(id, out var view) || view.Root == null)
                    continue;
                if (!byMp.TryGetValue(kv.Value, out var list))
                {
                    list = new List<ulong>();
                    byMp[kv.Value] = list;
                }

                list.Add(id);
            }

            // Fallback: single cluster of all palace glyphs if tagging failed.
            if (byMp.Count == 0)
            {
                var all = new List<ulong>();
                for (var i = 0; i < _palaceEntityIds.Count; i++)
                {
                    var id = _palaceEntityIds[i];
                    if (id == 0 || id == excludeEntityId)
                        continue;
                    if (_glyphs.Views.TryGetValue(id, out var view) && view.Root != null)
                        all.Add(id);
                }

                if (all.Count > 0)
                    return new List<List<ulong>> { all };
            }

            return new List<List<ulong>>(byMp.Values);
        }

        bool TryMakeClusterPivot(List<ulong> ids, float startScale, out Transform pivot)
        {
            pivot = null;
            var centroid = Vector3.zero;
            var n = 0;
            for (var i = 0; i < ids.Count; i++)
            {
                if (!_glyphs.Views.TryGetValue(ids[i], out var view) || view.Root == null)
                    continue;
                centroid += view.Root.transform.position;
                n++;
            }

            if (n == 0)
                return false;
            centroid /= n;
            var go = new GameObject("3OS_PalaceClusterPivot");
            go.transform.SetPositionAndRotation(centroid, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (!_glyphs.Views.TryGetValue(id, out var view) || view.Root == null)
                    continue;
                _animLockedIds.Add(id);
                view.Root.SetActive(true);
                view.Root.transform.localScale = Vector3.one;
                view.Root.transform.SetParent(go.transform, true);
                view.Root.transform.localScale = Vector3.one;
            }

            go.transform.localScale = Vector3.one * startScale;
            pivot = go.transform;
            return true;
        }

        /// <summary>Hide Palace glyph roots until cluster scale-in parents them (no full-size flash).</summary>
        public void HidePalaceGlyphsPendingScaleIn(ulong excludeEntityId = 0)
        {
            _palacePendingScaleIn = true;
            for (var i = 0; i < _palaceEntityIds.Count; i++)
            {
                var id = _palaceEntityIds[i];
                if (id == 0 || id == excludeEntityId)
                    continue;
                if (_glyphs.Views.TryGetValue(id, out var view) && view.Root != null)
                    view.Root.SetActive(false);
            }
        }

        /// <summary>
        /// 7.8.c — scale-out non-selected clusters, soft-dismiss field, persist selection at MR anchor.
        /// </summary>
        public IEnumerator DismissPalaceToMr(Transform mrAnchorSpawn)
        {
            ulong keepId = 0;
            if (_bridge != null && _bridge.TryGetSelection(out var sel) && sel != 0)
                keepId = sel;

            // Detach selection from cluster before scale-out.
            if (keepId != 0 && _glyphs.Views.TryGetValue(keepId, out var keepView) &&
                keepView.Root != null)
            {
                keepView.Root.transform.SetParent(null, true);
                _mrPersistedWorldPos = keepView.Root.transform.position;
                _mrPersistedWorldRot = keepView.Root.transform.rotation;
                _mrPersistedSelectionId = keepId;
                _entityMindPalace.TryGetValue(keepId, out _mrPersistedOriginalMpId);
                _bridge.SetMindPalaceRoot(keepId, 0);
                _animLockedIds.Add(keepId);
                Debug.Log(
                    $"[3OS] MR persist selection={keepId} mpWas={_mrPersistedOriginalMpId} " +
                    $"anchor={(mrAnchorSpawn != null ? mrAnchorSpawn.name : "null")}");
            }
            else
            {
                _mrPersistedSelectionId = 0;
                _mrPersistedOriginalMpId = 0;
            }

            yield return AnimatePalaceClustersScale(1f, 0.001f, PalaceClusterScaleSeconds, keepId);

            for (var i = 0; i < _palaceEntityIds.Count; i++)
            {
                var id = _palaceEntityIds[i];
                if (id == 0 || id == keepId)
                    continue;
                _bridge.StorageSetInField(id, false);
                if (_glyphs.Views.TryGetValue(id, out var view) && view.Root != null)
                    view.Root.SetActive(false);
            }

            _palaceSoftDismissed = true;
            MarkStorageVisualsDirty();
        }

        private bool LoadDemoDirectory(string root) =>
            LoadDemoDirectory(root, mindPalaceId: 0, clearFirst: true, cheapChildCounts: false);

        internal bool LoadDemoDirectoryPublic(string root, ulong mindPalaceId, bool clearFirst) =>
            LoadDemoDirectory(root, mindPalaceId, clearFirst, cheapChildCounts: false);

        private bool LoadDemoDirectory(string root, ulong mindPalaceId, bool clearFirst,
            bool cheapChildCounts = false)
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
                foreach (var probe in new[] { "simple.glb", "simple.gltf", "DemonA.blend", "marker.txt" })
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
                    if (mindPalaceId != 0)
                    {
                        if (!_bridge.SetMindPalaceRoot(eid, mindPalaceId))
                            Debug.LogWarning($"[3OS] set MindPalace failed: {_bridge.LastError}");
                        _entityMindPalace[eid] = mindPalaceId;
                    }

                    added++;
                }
                foreach (var name in dirs)
                {
                    var dirPath = Path.Combine(root, name);
                    // Full CountFolderContents fans across candidate roots — too slow on Quest
                    // for Final Demo Palace reveal (caused wipe-settle hitch / process kill).
                    var childCount = cheapChildCounts
                        ? CountFolderContentsShallow(dirPath)
                        : CountFolderContents(dirPath, name, root);
                    if (!_bridge.StorageAddBox(name, name, childCount, out var bid))
                    {
                        _status = $"add box failed: {_bridge.LastError}";
                        Debug.LogError($"[3OS] {_status}");
                        return false;
                    }
                    if (mindPalaceId != 0)
                    {
                        if (!_bridge.SetMindPalaceRoot(bid, mindPalaceId))
                            Debug.LogWarning($"[3OS] set MindPalace failed: {_bridge.LastError}");
                        _entityMindPalace[bid] = mindPalaceId;
                    }

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

        private static uint CountFolderContentsShallow(string dirPath)
        {
            uint n = 0;
            if (string.IsNullOrEmpty(dirPath))
                return 0;
            foreach (var _ in ThreeOSAndroidStorage.ListFileNames(dirPath))
                n++;
            foreach (var _ in ThreeOSAndroidStorage.ListDirectoryNames(dirPath))
                n++;
            return n;
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
                var view = _glyphs.CreateView(item);
                _glyphs.Views[item.entityId] = view;
                if (_palacePendingScaleIn && view.Root != null &&
                    item.entityId != _mrPersistedSelectionId)
                    view.Root.SetActive(false);
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
