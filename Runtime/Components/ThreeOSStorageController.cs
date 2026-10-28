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
    [DefaultExecutionOrder(100)]
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
        /// <summary>
        /// After Voodoo Dismiss / Dismiss All, suppress empty-air deselect briefly. Dual secondary
        /// often also bumps primary; portals are already gone so homePortal==0 would ClearSelection
        /// and drop the far-field object the user meant to keep for MR carry / telekinesis.
        /// </summary>
        private float _dismissKeepSelectionUntil;
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
        /// <summary>Origin used for the last Mind Palace fixture load (spawn / layout).</summary>
        private ThreeOSPose _palaceLayoutOrigin;
        private bool _palaceLayoutOriginValid;
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
        /// <summary>
        /// Set for the duration of a stay-soft ForceReload so LoadDualMindPalaceFixtureRoutine
        /// does not clear SoftDismissed at the end (which would let SyncViews spawn glyphs in MR).
        /// </summary>
        private bool _palaceReloadStaySoft;
        /// <summary>
        /// After Act I FS restore + soft remount in MR: next Palace reveal must ForceReload from
        /// disk (not TryRestorePalaceField) so Demo1/2 match the restored Documents tree.
        /// </summary>
        private bool _actIResetNeedsFullReveal;
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
        public Vector3 MrPersistedWorldPos => _mrPersistedWorldPos;
        public Quaternion MrPersistedWorldRot => _mrPersistedWorldRot;

        /// <summary>
        /// MR world pose is the source of truth while a selection is carried into the office.
        /// Call whenever the live glyph moves so MR→VR restore does not jump to a stale VR pose.
        /// </summary>
        public void CaptureMrPersistedFromLive(Transform live)
        {
            if (live == null || _mrPersistedSelectionId == 0)
                return;
            _mrPersistedWorldPos = live.position;
            _mrPersistedWorldRot = live.rotation;
        }

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
        uint _lastVoodooPortalCount;
        /// After a portal parks, near BBs bump-lerp (~0.22s) — follow ghost poses only then.
        float _ghostFollowUntil = -1f;
        /// After multi-summon, SyncViews every frame while pose-lerps (~0.2s) so all glyphs travel.
        float _summonPoseFollowUntil = -1f;
        readonly System.Collections.Generic.List<ulong> _summonFollowIds =
            new System.Collections.Generic.List<ulong>(16);
        /// <summary>Snapshot of multi-select ids for the current telekinesis drag (355).</summary>
        readonly System.Collections.Generic.List<ulong> _multiSelectTelekinesisIds =
            new System.Collections.Generic.List<ulong>(16);
        bool _multiSelectTelekinesisActive;
        ulong _multiSelectLeaderTrackedId;
        Vector3 _multiSelectLeaderLastWorldPos;
        bool _multiSelectLeaderPoseValid;

        // Collaborator access
        internal ThreeOSBridge Bridge => _bridge;
        internal ThreeOSInputRouter Input => _input;
        internal ThreeOSTopologyController Topology => _topology;
        internal ThreeOSGlyphViewRegistry Glyphs => _glyphs;
        internal ThreeOSDismissAffordances Dismiss => _dismiss;
        internal ThreeOSStorageWriteThrough Fs => _fs;
        internal ThreeOSExpandedBoxController Expanded => _expanded;
        internal ThreeOSStorageInteraction Interaction => _interaction;
        ThreeOSSelectionGestures _gestures;
        internal ThreeOSSelectionGestures Gestures => _gestures ??= new ThreeOSSelectionGestures(this);

        /// <summary>Solo kernel before <see cref="ThreeOSBridge.Tick"/> — see InputRouter.</summary>
        internal void RunSoloDropKernelBeforeTick(ref InteropInputFrame frame, ref InteropHandSample left,
            ref InteropHandSample right)
        {
            if (!DemoActive || _bridge == null || !_bridge.IsLoaded || GhostDragging || Possessing)
                return;
            Gestures.TickSoloKernel(ref frame, ref left, ref right);
        }
        internal Dictionary<ulong, ThreeOSExpandedBoxUi> ExpandedBoxes => _expanded.ExpandedBoxes;
        internal HashSet<ulong> AnimLockedIds => _animLockedIds;
        internal string StatusField { get => _status; set => _status = value; }
        internal string SelectionLabelField { get => _selectionLabel; set => _selectionLabel = value; }
        internal string DemoRoot2Field => _demoRoot2;
        internal string DemoRoot3Field => _demoRoot3;

        /// <summary>
        /// Absolute demo-root folder for an entity (Mind Palace 1/2/3), falling back to
        /// probing which root still contains <paramref name="relativePath"/>.
        /// </summary>
        internal string ResolveFsRootForEntity(ulong entityId, string relativePath)
        {
            if (_entityMindPalace.TryGetValue(entityId, out var mpId))
            {
                if (mpId == _mpLeftId && !string.IsNullOrEmpty(_demoRoot))
                    return _demoRoot;
                if (mpId == _mpRightId && !string.IsNullOrEmpty(_demoRoot2))
                    return _demoRoot2;
                if (mpId == _mpCenterId && !string.IsNullOrEmpty(_demoRoot3))
                    return _demoRoot3;
            }

            return ResolveFsRootContaining(relativePath) ?? _demoRoot;
        }

        /// <summary>First of Demo1/2/3 (docs or persistent) that contains this relative path.</summary>
        internal string ResolveFsRootContaining(string relativePath)
        {
            var rel = ThreeOSStorageWriteThrough.NormalizeDemoRelPublic(relativePath);
            if (string.IsNullOrEmpty(rel))
                return null;

            foreach (var root in new[] { _demoRoot, _demoRoot2, _demoRoot3 })
            {
                if (string.IsNullOrEmpty(root))
                    continue;
                var full = CombineRootRel(root, rel);
                if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(full) ||
                    ThreeOSDemoRootBootstrap.FileLooksPresent(full))
                    return root;
            }

            // Also probe Documents/persistent mirrors for each leaf.
            foreach (var leaf in new[] { DemoFolderName, DemoFolderName2, DemoFolderName3 })
            {
                foreach (var root in _fs.CandidateDemoRootsFor(leaf))
                {
                    var full = CombineRootRel(root, rel);
                    if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(full) ||
                        ThreeOSDemoRootBootstrap.FileLooksPresent(full))
                        return root;
                }
            }

            return null;
        }
        internal bool WaitingForAllFilesAccess { get => _waitingForAllFilesAccess; set => _waitingForAllFilesAccess = value; }
        internal bool InsertAnimBusy { get => _insertAnimBusy; set => _insertAnimBusy = value; }
        internal bool BoxAnimBusy { get => _boxAnimBusy; set => _boxAnimBusy = value; }
        internal ulong XHoverBoxId { get => _xHoverBoxId; set => _xHoverBoxId = value; }
        internal ThreeOSArrowVisual InsertArrow { get => _insertArrow; set => _insertArrow = value; }
        internal bool TriggerWasDown { get => _triggerWasDown; set => _triggerWasDown = value; }
        internal float TriggerDownTime { get => _triggerDownTime; set => _triggerDownTime = value; }
        internal bool TriggerDragActive { get => _triggerDragActive; set => _triggerDragActive = value; }
        internal bool TriggerGestureConsumed { get => _triggerGestureConsumed; set => _triggerGestureConsumed = value; }
        internal float DismissKeepSelectionUntil
        {
            get => _dismissKeepSelectionUntil;
            set => _dismissKeepSelectionUntil = value;
        }
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
        public static string PreferredDocumentsDemoPath3() =>
            ThreeOSDemoRootBootstrap.DocumentsPath(DemoFolderName3);
        public static string DocumentsDemoPath(string folderLeaf) =>
            ThreeOSDemoRootBootstrap.DocumentsPath(folderLeaf);
        public static string FallbackPersistentDemoPath2() =>
            ThreeOSDemoRootBootstrap.PersistentPath(DemoFolderName2);

        internal Coroutine StartCoroutinePublic(IEnumerator routine) => StartCoroutine(routine);
        internal static string CombineRootRelPublic(string root, string rel) => CombineRootRel(root, rel);

        internal void MarkViewsDirty() => _viewsDirty = true;
        internal bool HasVoodooPortals =>
            _bridge != null && _bridge.IsLoaded && _bridge.VoodooPortalCount > 0;

        float _suppressSafetyReconcileUntil;

        /// <summary>Pause the 0.35s full MarkStorageVisualsDirty pulse (paste settle).</summary>
        internal void SuppressSafetyReconcileFor(float seconds)
        {
            _suppressSafetyReconcileUntil =
                Mathf.Max(_suppressSafetyReconcileUntil, Time.unscaledTime + Mathf.Max(0f, seconds));
        }

        /// <summary>
        /// Drop Act I directory-name precache after a successful paste so any later remount
        /// re-lists Documents instead of resurrecting pre-paste folder sets.
        /// </summary>
        internal void InvalidatePalaceNamePrecache()
        {
            _palaceNamesPrecached = false;
            _precacheFiles1 = null;
            _precacheDirs1 = null;
            _precacheFiles2 = null;
            _precacheDirs2 = null;
            _precacheFiles3 = null;
            _precacheDirs3 = null;
            Debug.Log("[3OS] Palace name precache invalidated after paste");
        }
        internal void MarkGhostsDirty() => _ghostsDirty = true;

        /// <summary>Keep real glyph SyncViews hot while kernel summon pose-lerps finish.</summary>
        internal void BeginSummonPoseFollow(ulong[] ids, int count, float seconds = 0.35f)
        {
            _summonFollowIds.Clear();
            for (var i = 0; i < count && ids != null; i++)
            {
                if (ids[i] != 0) _summonFollowIds.Add(ids[i]);
            }
            _summonPoseFollowUntil = Time.unscaledTime + Mathf.Max(0.05f, seconds);
            _viewsDirty = true;
        }

        void ApplySummonFollowPoses()
        {
            if (Time.unscaledTime >= _summonPoseFollowUntil || _summonFollowIds.Count == 0)
                return;
            // ABI TransformDelta only carries one lerp id — push every followed pose from
            // workspace so the whole multi-summon cluster travels together.
            for (var i = 0; i < _summonFollowIds.Count; i++)
                PushWorkspacePoseToFarView(_summonFollowIds[i], skipKinematicLeader: true);
        }

        /// <summary>
        /// After kernel tick: mirror workspace poses for multi-select drag followers (Batch C).
        /// Summon already uses <see cref="BeginSummonPoseFollow"/>; possess/ghost-drag need this
        /// same frame because TransformDelta is single-entity.
        /// </summary>
        internal void SyncMultiSelectDragPosesAfterKernelTick()
        {
            if (!DemoActive || _bridge == null || !_bridge.IsLoaded)
                return;
            if (!Possessing && !GhostDragging)
                return;
            // Follower Unity poses follow the possessed leader in LateUpdate; kernel still
            // mirrors the cluster via translate_selected_followers for release/coast.
            if (_multiSelectTelekinesisActive && _multiSelectTelekinesisIds.Count > 1)
                return;

            var n = _bridge.GetSelectionIds(out var ids);
            if (n <= 1)
                return;
            for (var i = 0; i < n; i++)
                PushWorkspacePoseToFarView(ids[i], skipKinematicLeader: true);
        }

        void PushWorkspacePoseToFarView(ulong id, bool skipKinematicLeader)
        {
            if (id == 0 || !_glyphs.Views.TryGetValue(id, out var view) || view?.Root == null)
                return;
            if (skipKinematicLeader && _interaction.IsKinematicallyDriven(id))
                return;
            if (!TryFindItem(id, out var item))
                return;
            var pose = InteropStructs.ToUnityPose(item.pose);
            view.Root.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        /// <summary>Lock the current kernel selection set for group telekinesis (355).</summary>
        internal void RefreshMultiSelectTelekinesisFollow()
        {
            _multiSelectTelekinesisIds.Clear();
            var n = _bridge.GetSelectionIds(out var ids);
            for (var i = 0; i < n; i++)
            {
                if (ids[i] != 0)
                    _multiSelectTelekinesisIds.Add(ids[i]);
            }

            _multiSelectTelekinesisActive = _multiSelectTelekinesisIds.Count > 1;
            _multiSelectLeaderPoseValid = false;
            _multiSelectLeaderTrackedId = 0;
        }

        internal void ClearMultiSelectTelekinesisFollow()
        {
            _multiSelectTelekinesisActive = false;
            _multiSelectTelekinesisIds.Clear();
            _multiSelectLeaderPoseValid = false;
            _multiSelectLeaderTrackedId = 0;
        }

        /// <summary>
        /// During group telekinesis, do not let SyncViewsFromKernel overwrite follower transforms
        /// with stale workspace poses (ABI publishes one kinematic delta per tick).
        /// </summary>
        internal bool ShouldSkipKernelViewPoseSync(ulong entityId)
        {
            if (!_multiSelectTelekinesisActive || entityId == 0 || (!Possessing && !GhostDragging))
                return false;
            if (entityId == _possessedId)
                return true;
            return _multiSelectTelekinesisIds.Contains(entityId);
        }

        void ApplyMultiSelectTelekinesisRigidFollow()
        {
            if (!_multiSelectTelekinesisActive || (!Possessing && !GhostDragging))
            {
                _multiSelectLeaderPoseValid = false;
                return;
            }

            var leader = _possessedId;
            if (leader == 0 || !_glyphs.Views.TryGetValue(leader, out var leaderView) ||
                leaderView?.Root == null)
            {
                _multiSelectLeaderPoseValid = false;
                return;
            }

            var pos = leaderView.Root.transform.position;
            if (!_multiSelectLeaderPoseValid || _multiSelectLeaderTrackedId != leader)
            {
                _multiSelectLeaderTrackedId = leader;
                _multiSelectLeaderLastWorldPos = pos;
                _multiSelectLeaderPoseValid = true;
                return;
            }

            var delta = pos - _multiSelectLeaderLastWorldPos;
            _multiSelectLeaderLastWorldPos = pos;
            if (delta.sqrMagnitude < 1e-12f)
                return;

            for (var i = 0; i < _multiSelectTelekinesisIds.Count; i++)
            {
                var id = _multiSelectTelekinesisIds[i];
                if (id == leader || id == 0 ||
                    !_glyphs.Views.TryGetValue(id, out var view) || view?.Root == null)
                {
                    continue;
                }

                view.Root.transform.position += delta;
            }
        }

        private void LateUpdate()
        {
            if (!DemoActive)
                return;
            ApplyMultiSelectTelekinesisRigidFollow();
        }

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

            yield return null;
            Debug.Log($"[3OS][BOOT] InstallPacks begin t={Time.realtimeSinceStartup:F2}");
            yield return InstallPacks();
            Debug.Log($"[3OS][BOOT] InstallPacks end t={Time.realtimeSinceStartup:F2}");
            yield return null;
            yield return null;
            Debug.Log($"[3OS][BOOT] EnsureDemoRoot1 begin t={Time.realtimeSinceStartup:F2}");
            yield return ThreeOSDemoRootBootstrap.EnsureDemoRoot(
                DemoFolderName, DemoSeedPolicy.RequireContent, null,
                r => _demoRoot = r, s => _status = s);
            Debug.Log($"[3OS][BOOT] EnsureDemoRoot1 end t={Time.realtimeSinceStartup:F2}");
            yield return null;
            yield return null;
            Debug.Log($"[3OS][BOOT] EnsureDemoRoot2 begin t={Time.realtimeSinceStartup:F2}");
            yield return ThreeOSDemoRootBootstrap.EnsureDemoRoot(
                DemoFolderName2, DemoSeedPolicy.SoftSeedMindPalaceMarker, _demoRoot,
                r => _demoRoot2 = r, s => _status = s);
            Debug.Log($"[3OS][BOOT] EnsureDemoRoot2 end t={Time.realtimeSinceStartup:F2}");
            yield return null;
            yield return null;
            Debug.Log($"[3OS][BOOT] EnsureDemoRoot3 begin t={Time.realtimeSinceStartup:F2}");
            yield return ThreeOSDemoRootBootstrap.EnsureDemoRoot(
                DemoFolderName3, DemoSeedPolicy.SoftSeedMindPalaceMarker, _demoRoot,
                r => _demoRoot3 = r, s => _status = s);
            Debug.Log($"[3OS][BOOT] EnsureDemoRoot3 end t={Time.realtimeSinceStartup:F2}");
            if (string.IsNullOrEmpty(_demoRoot))
            {
                Debug.LogError($"[3OS] demo load aborted: {_status}");
                yield break;
            }

            Debug.Log($"[3OS][BOOT] HideNonGlyphMovables+HeadPose begin t={Time.realtimeSinceStartup:F2}");
            HideNonGlyphMovables();
            yield return WaitForHeadPose();
            Debug.Log($"[3OS][BOOT] HideNonGlyphMovables+HeadPose end t={Time.realtimeSinceStartup:F2}");
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
            // Final Demo MR cold open has no palace glyphs yet — skip empty RebuildViews hitch.
            if (!deferPalace)
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
            if (!GhostDragging && !Possessing) Gestures.TickSoloPostKernel();
            _multiChoice?.Tick();
            _interaction.UpdateInsertArrow();
            _fs.HandleStorageEvents();

            _bridge.TryGetSelection(out var selectedId);
            var selSig = selectedId;
            var selCount = _bridge.GetSelectionIds(out var selArr);
            for (var i = 0; i < selCount; i++) selSig = selSig * 1099511628211UL + selArr[i];
            if (selSig != _lastSelectionId)
            {
                _lastSelectionId = selSig;
                _selectionMatsDirty = true;
            }

            // Ghosts: event-driven only (drag / snap / portal open / explicit MarkGhostsDirty).
            // Idle open portals must not resync every frame — that was the Quest FPS sink.
            var snapPending = _bridge.TryGetSnap(out var snap) && snap.pending != 0;
            var portalCount = _bridge.VoodooPortalCount;
            if (portalCount != _lastVoodooPortalCount)
            {
                _ghostsDirty = true;
                // New/removed portal → cover near-BB bump-lerp while seats settle.
                if (portalCount > _lastVoodooPortalCount)
                    _ghostFollowUntil = Time.unscaledTime + 0.28f;
                _lastVoodooPortalCount = portalCount;
            }

            if (GhostDragging || snapPending || Time.unscaledTime < _ghostFollowUntil)
                _ghostsDirty = true;
            // Box open/close and locked anims need per-frame pose sync. Paste FS write-through
            // must NOT — that was a full SyncViewsFromKernel every frame for seconds (Quest spikes).
            // Multi-summon also needs per-frame sync — ABI publishes one TransformDelta at a time.
            if (BoxAnimBusy || _animLockedIds.Count > 0 || Time.unscaledTime < _summonPoseFollowUntil)
                _viewsDirty = true;

            if (_viewsDirty)
            {
                var swSync = System.Diagnostics.Stopwatch.StartNew();
                _glyphs.SyncViewsFromKernel();
                _viewsDirty = false;
                if (swSync.ElapsedMilliseconds >= 6)
                    Debug.Log($"[3OS][SYNC] SyncViewsFromKernel {swSync.ElapsedMilliseconds}ms f={Time.frameCount}");
            }

            ApplySummonFollowPoses();

            if (_ghostsDirty)
            {
                _glyphs.SyncGhostViewsFromKernel();
                _ghostsDirty = false;
                // Ghost sync rewrites base color with rim cleared — reapply selection same frame.
                _selectionMatsDirty = true;
            }

            // Hover chrome only while something can be hovered.
            // Always run portal dismiss sync when chrome exists — last portal dismiss
            // drops count to 0 and must still tear down orphaned X / scissors.
            if (_expanded.ExpandedBoxes.Count > 0 || _dismiss.HasGhostDismissX)
                _dismiss.UpdateExpandedDismissAffordances();
            if (_bridge.VoodooPortalCount > 0 || _dismiss.PortalDismiss.Count > 0)
                _dismiss.UpdatePortalDismissAffordances();

            if (_selectionMatsDirty)
            {
                _glyphs.SyncSelectionMaterials();
                _selectionMatsDirty = false;
            }

            var billboardSel = selectedId;
            _glyphs.BillboardSelectedAndVoodooLabels(billboardSel);

            // Catch missed dirty marks (portal open, kernel layout) without 72 Hz scans.
            // Skip during paste settle — coalesced SyncViews was dipping Meta Start under 60.
            if (Time.unscaledTime >= _nextSafetyReconcileAt &&
                Time.unscaledTime >= _suppressSafetyReconcileUntil &&
                !_insertAnimBusy)
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
            var resourcePackNames = new[]
            {
                "box", "box_open", "gltf_mark", "blender_mark", "scissors",
                "heart", "heart_filled", "arrow", "mic", "mr", "vr"
            };
            foreach (var stem in resourcePackNames)
            {
                var request = Resources.LoadAsync<TextAsset>($"3OS/glyphs/{stem}");
                yield return request;
                var pack = request.asset as TextAsset;
                if (pack == null)
                    continue;
                var bytes = pack.bytes;
                if (bytes == null || bytes.Length == 0)
                    continue;

                var succeeded = false;
                yield return InstallGlyphPackOnWorker(stem, bytes, value => succeeded = value);
                if (succeeded)
                {
                    installed++;
                    installedNames.Add(stem);
                }
            }

            Debug.Log($"[3OS] installed {installed} glyph pack resource(s)");

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
                    var bytes = req.downloadHandler.data;
                    var succeeded = false;
                    yield return InstallGlyphPackOnWorker(stem, bytes, value => succeeded = value);
                    if (succeeded)
                    {
                        installed++;
                        installedNames.Add(stem);
                        Debug.Log(
                            $"[3OS] installed StreamingAssets pack {fileName} ({bytes.Length} bytes)");
                    }
                }
#else
                if (File.Exists(url))
                {
                    var bytes = File.ReadAllBytes(url);
                    var succeeded = false;
                    yield return InstallGlyphPackOnWorker(stem, bytes, value => succeeded = value);
                    if (succeeded)
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

        private IEnumerator InstallGlyphPackOnWorker(string name, byte[] bytes, Action<bool> onComplete)
        {
            var install = _bridge.InstallGlyphPackAsync(bytes);
            while (!install.IsCompleted)
                yield return null;

            if (install.IsFaulted)
            {
                Debug.LogWarning(
                    $"[3OS] failed pack {name}: {install.Exception?.GetBaseException().Message}");
                onComplete(false);
                yield break;
            }

            if (install.IsCanceled)
            {
                Debug.LogWarning($"[3OS] install cancelled for pack {name}");
                onComplete(false);
                yield break;
            }

            var succeeded = install.Result;
            if (!succeeded)
                Debug.LogWarning($"[3OS] failed pack {name}: {_bridge.LastError}");
            onComplete(succeeded);
        }

        private bool LoadDualMindPalaceFixture(ThreeOSPose origin)
        {
            _palaceLayoutOrigin = origin;
            _palaceLayoutOriginValid = true;
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

            // Prefer Act I precache only when we still need a disk LoadDual. Soft restore
            // already has a kernel fixture — waiting here added ~2s after Act I reset.
            if (!_palaceNamesPrecached && !string.IsNullOrEmpty(_demoRoot) &&
                !(_palaceFixtureLoaded && _palaceSoftDismissed))
            {
                var waitPc = Time.realtimeSinceStartup + 2f;
                while (!_palaceNamesPrecached && Time.realtimeSinceStartup < waitPc)
                    yield return null;
            }

            if (_palaceFixtureLoaded && _palaceSoftDismissed && !_actIResetNeedsFullReveal)
            {
                _palacePendingScaleIn = true;
                if (!TryRestorePalaceField(spawn))
                    Debug.LogError($"[3OS] Palace restore failed: {_status}");
                HidePalaceGlyphsPendingScaleIn(_mrPersistedSelectionId);
                yield break;
            }

            // Fallback only when fixture was never remounted in MR (rare).
            if (_actIResetNeedsFullReveal)
            {
                Debug.Log("[3OS] RevealPalace: Act I reset → full disk remount (no soft fixture)");
                _palaceFixtureLoaded = false;
                _palaceSoftDismissed = false;
                _actIResetNeedsFullReveal = false;
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
            _palaceLayoutOrigin = origin;
            _palaceLayoutOriginValid = true;
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
            // Stay-soft remount (Act I reset in MR): keep SoftDismissed latched so SyncViews
            // cannot spawn Demo/Demo3 glyphs into the office between load and SoftDismiss.
            if (!_palaceReloadStaySoft)
                _palaceSoftDismissed = false;
            Debug.Log(
                $"[3OS] mind-palace fixture L={mpL} R={mpR} C={_mpCenterId} entities={_palaceEntityIds.Count}" +
                $" staySoft={_palaceReloadStaySoft}");
            yield return null;
        }

        IEnumerator PrecachePalaceDirectoryNamesRoutine()
        {
            // Plenty of Act I time — delay and spread so cold open stays smooth.
            yield return new WaitForSecondsRealtime(4.0f);
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

        /// <summary>Post-paste / remount: rebuild glyphs a few per frame (avoids one-frame hitch).</summary>
        internal IEnumerator RebuildViewsSpreadPublic(int viewsPerFrame = 4) =>
            RebuildViewsSpreadRoutine(viewsPerFrame);

        IEnumerator RebuildViewsSpreadRoutine(int viewsPerFrame)
        {
            _expanded.TeardownAllExpandedBoxes(collapseKernel: true);
            yield return null;

            // Destroy old glyphs in small batches — nuking everything in one frame was a spike.
            var doomed = new List<ThreeOSGlyphView>(_glyphs.Views.Values);
            _glyphs.Views.Clear();
            var per = Mathf.Max(1, viewsPerFrame);
            for (var i = 0; i < doomed.Count; i++)
            {
                _glyphs.DestroyGlyphView(doomed[i]);
                if ((i + 1) % per == 0)
                    yield return null;
            }

            _glyphs.ClearGhostViews();
            _dismiss.ClearPortalDismissUi();
            yield return null;

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

            // Re-link persisted selection to original Palace centroid. MR world pose always
            // wins over whatever palace-field pose was cached before the VR→MR dismiss.
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
                    // Live MR transform is authoritative — refresh cache from it; do not
                    // overwrite with the dismiss-time VR snapshot.
                    CaptureMrPersistedFromLive(selView.Root.transform);
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
                // Snapshot MR pose before rebuild; live root stays at MR world position.
                CaptureMrPersistedFromLive(persistRoot.transform);
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
                // Re-assert MR pose (klobbers any palace layout pose RebuildViews may have set
                // on a duplicate). Anim-lock keeps SyncViews from writing the old VR anchor.
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
                // World-rooted dismiss-X spheres must not linger inside scaling box glyphs.
                _expanded?.TeardownAllExpandedBoxes(collapseKernel: false);
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
            // Snapshot selection BEFORE collapsing expanded boxes. Kernel collapse_box clears
            // selected_id_ for children of the open box — that was dropping DemonA on the first
            // VR→MR wipe whenever DemonProject (or any parent) was still expanded.
            ulong keepId = 0;
            if (_bridge != null && _bridge.TryGetSelection(out var sel) && sel != 0)
                keepId = sel;

            GameObject keepRoot = null;
            if (keepId != 0 && _glyphs.Views.TryGetValue(keepId, out var keepView) &&
                keepView.Root != null)
            {
                keepRoot = keepView.Root;
                keepRoot.transform.SetParent(null, true);
                _mrPersistedWorldPos = keepRoot.transform.position;
                _mrPersistedWorldRot = keepRoot.transform.rotation;
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

            // Collapse X is a world-rooted GO (not parented under the box glyph). Tear it down
            // after persist snapshot so CollapseBox cannot clear the keep selection first.
            _expanded?.TeardownAllExpandedBoxes(collapseKernel: true);

            // Teardown clears anim locks and collapse sets in_field=false on box children —
            // re-assert the MR carry so SyncViews / soft-dismiss keep the glyph.
            if (keepId != 0)
            {
                _bridge.StorageSetInField(keepId, true);
                _bridge.SetSelection(keepId);
                _animLockedIds.Add(keepId);
                if (keepRoot != null)
                {
                    keepRoot.SetActive(true);
                    keepRoot.transform.SetPositionAndRotation(_mrPersistedWorldPos, _mrPersistedWorldRot);
                }
            }

            // Drop portals/ghosts first — open portals were leaking Palace near-field into MR.
            DismissAllVoodooPortals();
            _fs?.CancelPasteProgressUi();

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

            // Force EVERY non-persisted workspace item out of field — paste rollbacks can leave
            // glb/blend glyphs in_field without palace-id tags, and they otherwise follow into MR.
            var count = _bridge.StorageItemCount();
            for (uint i = 0; i < count; i++)
            {
                if (!_bridge.TryGetWorkspaceItem(i, out var item))
                    continue;
                if (item.entityId == 0 || item.entityId == keepId)
                    continue;
                _bridge.StorageSetInField(item.entityId, false);
                if (_glyphs.Views.TryGetValue(item.entityId, out var stray) && stray.Root != null)
                    stray.Root.SetActive(false);
            }

            _palaceSoftDismissed = true;
            _palacePendingScaleIn = false;
            EnsurePalaceSoftDismissedVisuals();
            // Destroy every glyph except the MR carry — belt-and-suspenders vs multi-file leak.
            var doomed = new List<ulong>();
            foreach (var kv in _glyphs.Views)
            {
                if (kv.Key != keepId && kv.Key != _mrPersistedSelectionId)
                    doomed.Add(kv.Key);
            }

            for (var i = 0; i < doomed.Count; i++)
            {
                var id = doomed[i];
                if (_glyphs.Views.TryGetValue(id, out var view))
                {
                    _glyphs.DestroyGlyphView(view);
                    _glyphs.Views.Remove(id);
                }
            }

            MarkStorageVisualsDirty();
        }

        public void CancelPasteProgressUi() => _fs?.CancelPasteProgressUi();

        /// <summary>
        /// Hide/destroy Palace glyphs while soft-dismissed in MR.
        /// Does not dismiss live voodoo portals or clear ghosts — MR office still allows
        /// far-field voodoo (DemonA near-BB ghost) after the wipe settles.
        /// </summary>
        public void EnsurePalaceSoftDismissedVisuals()
        {
            if (!_palaceSoftDismissed)
                return;
            _expanded?.TeardownAllExpandedBoxes(collapseKernel: true);
            var remove = new List<ulong>();
            foreach (var kv in _glyphs.Views)
            {
                if (kv.Key == _mrPersistedSelectionId)
                    continue;
                if (!IsPalaceEntity(kv.Key) && !_entityMindPalace.ContainsKey(kv.Key))
                    continue;
                remove.Add(kv.Key);
            }

            for (var i = 0; i < remove.Count; i++)
            {
                var id = remove[i];
                if (_glyphs.Views.TryGetValue(id, out var view))
                {
                    _glyphs.DestroyGlyphView(view);
                    _glyphs.Views.Remove(id);
                }
            }

            DestroyOrphanGlyphRoots();
            _dismiss.ClearPortalDismissUi();
        }

        /// <summary>
        /// After a remount while still in MR: keep disk/kernel fixture loaded but strip all
        /// Unity glyphs so Act I reset does not flash Mind Palace into the office.
        /// </summary>
        void SoftDismissPalaceAfterRemount()
        {
            if (_bridge != null && _bridge.IsLoaded)
            {
                _expanded?.TeardownAllExpandedBoxes(collapseKernel: true);
                DismissAllVoodooPortals();
                _fs?.CancelPasteProgressUi();

                var count = _bridge.StorageItemCount();
                for (uint i = 0; i < count; i++)
                {
                    if (!_bridge.TryGetWorkspaceItem(i, out var item) || item.entityId == 0)
                        continue;
                    if (item.entityId == _mrPersistedSelectionId)
                        continue;
                    _bridge.StorageSetInField(item.entityId, false);
                }
            }

            var doomed = new List<ulong>(_glyphs.Views.Keys);
            for (var i = 0; i < doomed.Count; i++)
            {
                var id = doomed[i];
                if (id == _mrPersistedSelectionId)
                    continue;
                if (_glyphs.Views.TryGetValue(id, out var view))
                {
                    _glyphs.DestroyGlyphView(view);
                    _glyphs.Views.Remove(id);
                }
            }

            // Orphans: a cancelled RebuildViewsSpread can Clear() the dict after queuing Destroy
            // on only some roots — leftover Glyph_TestW / Glyph_DemonProject then float in MR
            // even though disk/kernel already match the correct Demo set.
            DestroyOrphanGlyphRoots();

            _palaceSoftDismissed = true;
            _palacePendingScaleIn = false;
            _glyphs.ClearGhostViews();
            _dismiss.ClearPortalDismissUi();
            MarkStorageVisualsDirty();
            Debug.Log("[3OS] SoftDismissPalaceAfterRemount — palace fixture kept, glyphs hidden in MR");
        }

        /// <summary>
        /// Destroy scene roots named Glyph_* that are no longer tracked in Views (except MR carry).
        /// </summary>
        void DestroyOrphanGlyphRoots()
        {
            string keepName = null;
            if (_mrPersistedSelectionId != 0 &&
                _glyphs.Views.TryGetValue(_mrPersistedSelectionId, out var keep) &&
                keep?.Root != null)
                keepName = keep.Root.name;

            var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t.parent != null)
                    continue;
                var n = t.name;
                if (string.IsNullOrEmpty(n) || !n.StartsWith("Glyph_", StringComparison.Ordinal))
                    continue;
                if (keepName != null && n == keepName)
                    continue;
                // Still tracked — leave for Views teardown / SyncViews.
                var tracked = false;
                foreach (var kv in _glyphs.Views)
                {
                    if (kv.Value?.Root == t.gameObject)
                    {
                        tracked = true;
                        break;
                    }
                }

                if (tracked)
                    continue;
                Destroy(t.gameObject);
            }
        }

        void DismissAllVoodooPortals()
        {
            if (_bridge == null || !_bridge.IsLoaded)
                return;
            _bridge.VoodooCutCancel();
            // Dismiss from the end — indices shift as portals close.
            for (var guard = 0; guard < 32; guard++)
            {
                var n = _bridge.VoodooPortalCount;
                if (n == 0)
                    break;
                if (!_bridge.TryGetVoodooPortal(n - 1, out var portal) || portal.id == 0)
                    break;
                if (!_bridge.DismissVoodooPortal(portal.id))
                    break;
            }

            _glyphs.ClearGhostViews();
            _dismiss.ClearPortalDismissUi();
            _lastVoodooPortalCount = 0;
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

        /// <summary>
        /// After a successful FS paste into <paramref name="boxId"/>, point the moved entity
        /// at the destination Mind Palace (cross-cluster Demo1/2/3).
        /// </summary>
        internal void RetargetEntityMindPalace(ulong entityId, ulong boxId)
        {
            if (entityId == 0 || boxId == 0)
                return;
            if (!_entityMindPalace.TryGetValue(boxId, out var mp) || mp == 0)
                return;
            _bridge.SetMindPalaceRoot(entityId, mp);
            _entityMindPalace[entityId] = mp;
        }

        /// <summary>
        /// Debug (Act I reset): undo the last paste batch on disk and remount Mind Palace
        /// from the restored folders. Returns false if nothing was recorded.
        /// </summary>
        public bool TryUndoLastPasteDebug()
        {
            if (_fs == null || !_fs.HasLastPasteBatch)
                return false;
            StartCoroutine(_fs.UndoLastPasteRoutine());
            return true;
        }

        /// <summary>
        /// Drop the MR-persisted selection glyph (e.g. DemonA.blend carried into MR) so Act I
        /// reset can remount a clean palace. Does not rewrite disk paths.
        /// </summary>
        public void ClearMrPersistedSelectionVisual()
        {
            if (_mrPersistedSelectionId != 0)
            {
                var id = _mrPersistedSelectionId;
                if (_glyphs.Views.TryGetValue(id, out var view))
                {
                    _glyphs.DestroyGlyphView(view);
                    _glyphs.Views.Remove(id);
                }

                _animLockedIds.Remove(id);
                if (_bridge != null && _bridge.IsLoaded)
                {
                    _bridge.StorageSetInField(id, false);
                    _bridge.SetMindPalaceRoot(id, 0);
                }
            }

            _mrPersistedSelectionId = 0;
            _mrPersistedOriginalMpId = 0;
        }

        public void ClearLastPasteBatch() => _fs?.ClearLastPasteBatch();

        /// <summary>
        /// Public remount for Act I reset after offline snapshot restore.
        /// When <paramref name="remainHiddenInMr"/> is true (user still in MR office), disk/kernel
        /// remount runs but glyphs stay soft-dismissed so Mind Palace does not appear in MR.
        /// </summary>
        public IEnumerator RemountPalaceAfterDemoResetRoutine(bool remainHiddenInMr = false)
        {
            InvalidatePalaceNamePrecache();
            var staySoft = remainHiddenInMr || _palaceSoftDismissed;
            if (_palaceFixtureLoaded || staySoft)
            {
                yield return ForceReloadMindPalaceRoutine(remainSoftDismissed: staySoft);
                // ForceReload already rescanned restored Demo dirs. Do NOT force a second
                // LoadDual on the next MR→Palace wipe — soft restore rebuilds glyphs only.
                _actIResetNeedsFullReveal = false;
                StartCoroutine(RefreshPalaceNamePrecacheFastRoutine());
            }
        }

        /// <summary>
        /// Refill directory-name precache after Act I remount without the cold-open 4s delay.
        /// </summary>
        IEnumerator RefreshPalaceNamePrecacheFastRoutine()
        {
            yield return PrecacheOneRootRoutine(_demoRoot, files => _precacheFiles1 = files,
                dirs => _precacheDirs1 = dirs);
            yield return PrecacheOneRootRoutine(_demoRoot2, files => _precacheFiles2 = files,
                dirs => _precacheDirs2 = dirs);
            yield return PrecacheOneRootRoutine(_demoRoot3, files => _precacheFiles3 = files,
                dirs => _precacheDirs3 = dirs);
            _palaceNamesPrecached = true;
            Debug.Log("[3OS] Palace names precache refreshed (post Act I remount)");
        }

        /// <summary>
        /// After a paste batch: re-scan dest box folders from the correct Demo1/2/3 root so
        /// DemonProject (Demo3) is not stuck showing 0 children when disk actually filled.
        /// Spread across frames — a sync Documents list at bar=100% was the paste FPS spike.
        /// </summary>
        internal void RequestPostPasteChildHeal()
        {
            StartCoroutine(RequestPostPasteChildHealRoutine());
        }

        internal IEnumerator RequestPostPasteChildHealRoutine()
        {
            if (_fs == null || !_fs.HasLastPasteBatch || _bridge == null || !_bridge.IsLoaded)
                yield break;

            var destBoxIds = new HashSet<ulong>();
            // Prefer the actual paste destination box ids recorded during the batch.
            foreach (var id in _fs.LastPasteDestBoxIds)
                destBoxIds.Add(id);

            if (destBoxIds.Count == 0)
            {
                var count = _bridge.StorageItemCount();
                for (uint i = 0; i < count; i++)
                {
                    if (!_bridge.TryGetWorkspaceItem(i, out var item) || item.kind != 1)
                        continue;
                    if (string.Equals(item.Name, "DemonProject", StringComparison.OrdinalIgnoreCase) ||
                        (_entityMindPalace.TryGetValue(item.entityId, out var mp) && mp == _mpCenterId &&
                         _mpCenterId != 0))
                    {
                        destBoxIds.Add(item.entityId);
                    }
                }
            }

            foreach (var boxId in destBoxIds)
            {
                var rel = _expanded.ResolveBoxRelativePath(boxId);
                if (string.IsNullOrEmpty(rel))
                    rel = "DemonProject";
                // nestedCount=0 — skip per-dir CountFilesInDirectory / ListDirectoryNames
                // (those were the remaining sub-60 paste hitch on Quest Documents).
                if (_expanded.EnsureBoxChildren(boxId, rel, out var kids, skipNestedCounts: true))
                    Debug.Log($"[3OS] post-paste heal box={boxId} rel={rel} children={kids.Count}");
                // One Documents list per frame — keeps the post-100% hitch off a single frame.
                yield return null;
            }

            // Dirty deferred by InsertSequence after extra yields — avoid double SyncViews.
        }

        /// <summary>
        /// Re-scan Demo1/2/3 from disk into a fresh Mind Palace fixture (keeps prior layout origin).
        /// Pass <paramref name="remainSoftDismissed"/> when remounting from MR so glyphs do not
        /// flash into the office after the rebuild.
        /// </summary>
        internal IEnumerator ForceReloadMindPalaceRoutine(bool remainSoftDismissed = false)
        {
            if (_bridge == null || !_bridge.IsLoaded)
            {
                Debug.LogError("[3OS] ForceReload: bridge not loaded");
                yield break;
            }

            // Capture before clearing — paste-undo remounts while soft-dismissed in MR too.
            var staySoft = remainSoftDismissed || _palaceSoftDismissed;

            var origin = _palaceLayoutOriginValid ? _palaceLayoutOrigin : _layoutHeadPose;
            // Soft-dismiss must stay latched for SyncViews during the remount when still in MR —
            // otherwise LoadDual's end-of-routine SoftDismissed=false + RebuildViewsSpread
            // flashes (and can orphan) Palace glyphs into the office. Kernel load itself does
            // not need SoftDismissed cleared.
            _palaceFixtureLoaded = false;
            _palaceSoftDismissed = staySoft;
            _palacePendingScaleIn = false;
            _palaceReloadStaySoft = staySoft;
            _bridge.VoodooCutCancel();
            // Drop any live/orphan Glyph_* roots before the scan so a cancelled prior rebuild
            // cannot leave TestW-style names floating in MR after reset.
            if (staySoft)
                SoftDismissPalaceAfterRemount();
            yield return LoadDualMindPalaceFixtureRoutine(origin);
            _palaceReloadStaySoft = false;
            if (!_palaceFixtureLoaded)
            {
                Debug.LogError($"[3OS] ForceReload failed: {_status}");
                if (staySoft)
                    SoftDismissPalaceAfterRemount();
                yield break;
            }

            if (staySoft)
            {
                // Kernel fixture is current; do NOT RebuildViews — that was recreating Demo /
                // Demo3 glyphs in the office (and orphaning stale Glyph_TestW… roots).
                SoftDismissPalaceAfterRemount();
                _status = "palace remounted (soft-dismissed in MR)";
                Debug.Log("[3OS] ForceReload Mind Palace complete (kept soft-dismissed, no glyph rebuild)");
            }
            else
            {
                yield return RebuildViewsSpreadRoutine(viewsPerFrame: 6);
                MarkGhostsDirty();
                _status = "palace remounted from disk";
                Debug.Log("[3OS] ForceReload Mind Palace complete");
            }
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
