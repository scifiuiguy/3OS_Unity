using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ThreeOS
{
    /// <summary>
    /// Phase 0.4: list demo dir, cubic matrix glyphs + labels, trigger tap select / hold drag,
    /// snap-insert with open/bump/close, write-through FS cut, glow selection materials.
    /// </summary>
    [DefaultExecutionOrder(0)]
    public sealed class ThreeOSStorageController : MonoBehaviour
    {
        public const string DemoFolderName = "3OS_Demo";
        /// <summary>Second god-root folder for dual-centroid Quest fixture (Task 6.2).</summary>
        public const string DemoFolderName2 = "3OS_Demo2";
        /// <summary>Dual-god snap-test range from user (10 ft).</summary>
        public const float GodFixtureRangeM = 10f * 0.3048f;
        /// <summary>Dual-god snap-test height above head (2.5 ft; Quest-tuned from 10→5→2.5).</summary>
        public const float GodFixtureHeightM = 2.5f * 0.3048f;
        public const float GodFixtureAzimuthDeg = 15f;
        /// <summary>Glyph visual size in meters (matches Blender-authored 0.1 m packs).</summary>
        public const float GlyphSizeM = 0.1f;
        /// <summary>Dismiss-X sphere diameter (meters).</summary>
        public const float DismissXDiameterM = 0.06f;
        public float PossessDistance = 0.45f;
        /// <summary>Trigger held at least this long starts drag (telekinesis); shorter tap selects.</summary>
        public float TriggerHoldDragSeconds = 0.22f;
        /// <summary>
        /// Selected ghost + pinch outside home BB is summon on short release. Drag must
        /// wait longer than a normal pinch (~0.12–0.25 s) or the HUD steals summon.
        /// </summary>
        public float SummonZoneHoldDragSeconds = 0.40f;
        public float TriggerAxisThreshold = 0.55f;
        /// <summary>Second short trigger on the same Box within this window opens it.</summary>
        public float BoxDoubleTapSeconds = 0.4f;
        public float BoxExpandAnimSeconds = 0.3f;
        /// <summary>World +up offset for dismiss X above the parent box.</summary>
        public float BoxDismissXOffsetM = 0.053333f;

        [Header("Glyph selection glow (ThreeOS/Glyph)")]
        [Tooltip("Look-dev asset (Create → 3OS → Glyph Glow Settings). Per-glyph base tint is separate.")]
        public ThreeOSGlyphGlowSettings GlyphGlowSettings;

        private static readonly Color ExpandHoverGlowColor = new Color(0.45f, 0.75f, 1f, 1f);

        private ThreeOSBridge _bridge;
        private ThreeOSInputRouter _input;
        private ThreeOSTopologyController _topology;

        private readonly Dictionary<ulong, GlyphView> _views = new Dictionary<ulong, GlyphView>();
        /// <summary>Task 6.4 near-portal ghosts (labels + Fresnel); key = portalId&lt;&lt;32 | entityId low bits + parent flag.</summary>
        private readonly Dictionary<ulong, GlyphView> _ghostViews = new Dictionary<ulong, GlyphView>();
        private readonly Dictionary<ulong, ExpandedBoxUi> _expandedBoxes = new Dictionary<ulong, ExpandedBoxUi>();
        /// <summary>Task 6.7 near-BB X-close affordances (key = portal id).</summary>
        private readonly Dictionary<ulong, PortalDismissUi> _portalDismiss = new Dictionary<ulong, PortalDismissUi>();
        private readonly HashSet<ulong> _animLockedIds = new HashSet<ulong>();
        private ThreeOSArrowVisual _insertArrow;
        private bool _triggerWasDown;
        private float _triggerDownTime;
        private bool _triggerDragActive;
        private bool _triggerGestureConsumed;
        /// Cursor at trigger-down (OpenXR); used to ignore summon when the press was a drag swipe.
        private Vector3 _triggerDownCursorXr;
        private bool _triggerDownCursorValid;
        /// <summary>Hand that started the current trigger press: 0=left, 1=right, -1=none.</summary>
        private int _selectHand = -1;
        private bool _possessing;
        private ulong _possessedId;
        private bool _ghostDragging;

        /// <summary>
        /// While telekinesis is active, which hand must drive stick/kinematics (0/1), else -1.
        /// InputRouter reads this before <c>Tick</c> to mute the other hand.
        /// Ghost-scale drag also latches the drive hand so the other cursor is muted.
        /// </summary>
        public int PossessDriveHand =>
            (_possessing || _ghostDragging) && _selectHand >= 0 ? _selectHand : -1;
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
        /// <summary>Portal X under left / right point cursor (0 = none). Independent so both hands can dismiss.</summary>
        private readonly ulong[] _xHoverPortalHand = new ulong[2];
        private bool _leftTriggerWasDown;
        private bool _rightTriggerWasDown;

        // All Boxes share one ThreeOS/Glyph material; selection is _Selected via MPB.
        private Material _sharedBoxMaterial;
        /// <summary>Resources material that references <c>ThreeOS/Glyph</c> so the shader is not stripped.</summary>
        private Material _glyphShaderTemplate;
        private bool _loggedMissingGlyphShader;
        private MaterialPropertyBlock _glyphMpb;
        private static readonly int SelectedProp = Shader.PropertyToID("_Selected");
        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int GlowColorProp = Shader.PropertyToID("_GlowColor");
        private static readonly int GlowIntensityProp = Shader.PropertyToID("_GlowIntensity");
        private static readonly int GlowPowerProp = Shader.PropertyToID("_GlowPower");
        private static readonly int GlowWidthProp = Shader.PropertyToID("_GlowWidth");
        private static readonly int GlowBoostProp = Shader.PropertyToID("_GlowBoost");
        private static readonly int GlowViewMixProp = Shader.PropertyToID("_GlowViewMix");

        public bool DemoActive { get; private set; }
        public string Status => _status;
        public string DemoRoot => _demoRoot;
        /// <summary>HUD smoke label for ABI selection (<c>none</c> or entity name).</summary>
        public string SelectionLabel => _selectionLabel;

        private sealed class GlyphView
        {
            public ulong EntityId;
            public GameObject Root;
            public ThreeOSMovable Movable;
            public MeshRenderer Renderer;
            public BoxCollider Collider;
            public TextMesh NameLabel;
            public TextMesh SecondaryLabel;
            public string GlyphId;
            /// <summary>Shared or owned <c>ThreeOS/Glyph</c> material (Fresnel is a shader pass, not a swap).</summary>
            public Material Material;
            /// <summary>False for shared Box material — do not Destroy on view teardown.</summary>
            public bool OwnsMaterial;
        }

        private sealed class ExpandedBoxUi
        {
            public ulong BoxId;
            public readonly List<ulong> ChildIds = new List<ulong>();
            public GameObject XRoot;
            public Transform XVisual;
            /// <summary>Sticky hover (distance hysteresis); not physics/colliders.</summary>
            public bool Hovered;
        }

        private sealed class PortalDismissUi
        {
            public ulong PortalId;
            public GameObject XRoot;
            public Transform XVisual;
            public bool Hovered;
        }

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
            _input = GetComponent<ThreeOSInputRouter>() ?? FindFirstObjectByType<ThreeOSInputRouter>();
            _topology = GetComponent<ThreeOSTopologyController>() ??
                        FindFirstObjectByType<ThreeOSTopologyController>();
            if (GlyphGlowSettings == null)
            {
                GlyphGlowSettings = Resources.Load<ThreeOSGlyphGlowSettings>("3OS/GlyphGlowSettings");
            }

            // Must be a Resources material referencing ThreeOS/Glyph — Shader.Find is unreliable
            // on Quest player builds and silently fell back to Unlit (no Fresnel).
            _glyphShaderTemplate = Resources.Load<Material>("3OS/GlyphDefault");
            if (_glyphShaderTemplate == null || _glyphShaderTemplate.shader == null ||
                _glyphShaderTemplate.shader.name != "ThreeOS/Glyph")
            {
                Debug.LogError(
                    "[3OS] Missing Resources/3OS/GlyphDefault.mat (ThreeOS/Glyph). " +
                    "Selection Fresnel will not render on device.");
            }

            _insertArrow = ThreeOSArrowVisual.Create("3OS_InsertArrow", 0.01f);
            if (GetComponent<ThreeOSVoodooController>() == null)
            {
                gameObject.AddComponent<ThreeOSVoodooController>();
            }

            if (GetComponent<ThreeOSCentroidDebug>() == null)
            {
                gameObject.AddComponent<ThreeOSCentroidDebug>();
            }
        }

        private Color ResolvedGlowColor =>
            GlyphGlowSettings != null ? GlyphGlowSettings.GlowColor : Color.white;

        // Fallbacks match FresnelTest2 / ThreeOSGlyphGlowSettings defaults.
        private float ResolvedGlowIntensity =>
            GlyphGlowSettings != null ? GlyphGlowSettings.GlowIntensity : 0.5f;

        private float ResolvedGlowPower =>
            GlyphGlowSettings != null ? GlyphGlowSettings.GlowPower : 3f;

        private float ResolvedGlowWidth =>
            GlyphGlowSettings != null ? GlyphGlowSettings.GlowWidth : 0.5f;

        private float ResolvedGlowBoost =>
            GlyphGlowSettings != null ? GlyphGlowSettings.GlowBoost : 4f;

        private float ResolvedGlowViewMix =>
            GlyphGlowSettings != null ? GlyphGlowSettings.GlowViewMix : 0.5f;

        private IEnumerator Start()
        {
            // Bridge loads in Awake; wait a few frames in case script order races.
            var bridgeDeadline = Time.realtimeSinceStartup + 3f;
            while ((_bridge == null || !_bridge.IsLoaded) && Time.realtimeSinceStartup < bridgeDeadline)
            {
                if (_bridge == null)
                {
                    _bridge = FindFirstObjectByType<ThreeOSBridge>();
                }

                yield return null;
            }

            if (_bridge == null || !_bridge.IsLoaded)
            {
                _status = $"bridge not loaded: {(_bridge != null ? _bridge.LastError : "missing")}";
                Debug.LogError($"[3OS] {_status}");
                yield break;
            }

            // Packs only tint/resolve glyph ids — procedural cubes work without them.
            yield return InstallPacks();
            yield return EnsureDemoDirectory();
            yield return EnsureDemoDirectory2();
            if (string.IsNullOrEmpty(_demoRoot))
            {
                Debug.LogError($"[3OS] demo load aborted: {_status}");
                yield break;
            }

            // Hide Phase 0.3 magenta cube as soon as workspace has items (before layout).
            HideLegacyPhase03Cube();

            yield return WaitForHeadPose();
            if (!LoadDualGodFixture(_layoutHeadPose))
            {
                Debug.LogError($"[3OS] dual-god load aborted: {_status}");
                yield break;
            }

            DemoActive = true;
            var kin = GetComponent<ThreeOSKinematicsController>() ??
                      FindFirstObjectByType<ThreeOSKinematicsController>();
            kin?.PushKinematicsParams();
            RebuildViews();
            _status = $"demo {_views.Count} glyphs dual-god @ {ShortPath(_demoRoot)}";
            Debug.Log(
                $"[3OS] demo loaded views={_views.Count} kernelItems={_bridge.StorageItemCount()} root={_demoRoot}");

            // Do not open MANAGE_EXTERNAL_STORAGE settings here — on Quest that
            // leaves immersive XR (session STOPPING / app quit). Request on first cut/drop.
            if (!ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                Debug.Log(
                    "[3OS] all-files access not granted yet (OK for boot; will prompt on cut/drop)");
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus || !_waitingForAllFilesAccess)
            {
                return;
            }

            if (ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                _waitingForAllFilesAccess = false;
                _status = "All files access OK — drop again to cut";
                Debug.Log("[3OS] MANAGE_EXTERNAL_STORAGE granted");
            }
        }

        /// <summary>
        /// Quest Documents cut needs all-files access. Request it and wait briefly if the
        /// settings UI returns quickly; otherwise leave a HUD hint and let the user retry.
        /// </summary>
        private IEnumerator EnsureAllFilesAccessForCut(string waitingStatus)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                _waitingForAllFilesAccess = false;
                yield break;
            }

            _waitingForAllFilesAccess = true;
            _status = waitingStatus;
            ThreeOSAndroidStorage.RequestManageAllFilesAccess();

            // Poll while the system settings sheet is open / returning (no activity result callback).
            var deadline = Time.realtimeSinceStartup + 45f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (ThreeOSAndroidStorage.IsExternalStorageManager())
                {
                    _waitingForAllFilesAccess = false;
                    _status = "All files access OK";
                    Debug.Log("[3OS] all-files access granted during wait");
                    yield break;
                }

                yield return null;
            }

            if (!ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                _status = "All files access still off — enable it, return here, drop again";
            }
#else
            yield break;
#endif
        }

        private static void HideLegacyPhase03Cube()
        {
            foreach (var movable in FindObjectsByType<ThreeOSMovable>(FindObjectsSortMode.None))
            {
                if (movable.gameObject.name.StartsWith("Glyph_"))
                {
                    continue;
                }

                movable.gameObject.SetActive(false);
            }
        }

        private IEnumerator WaitForHeadPose()
        {
            // Prefer real XR head tracking — Camera.main can exist before tracking and sit on
            // the floor origin, which plants the whole demo field in the ground.
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
                {
                    _layoutHeadPose.position.y = 1.5f;
                }

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

        private void Update()
        {
            if (!DemoActive || _bridge == null || !_bridge.IsLoaded || _input == null)
            {
                return;
            }

            if (_topology != null && _topology.LatticeActive)
            {
                return;
            }

            SyncViewsFromKernel();
            SyncGhostViewsFromKernel();
            // X hover before trigger so left-hand press can close on the same frame.
            UpdateExpandedDismissAffordances();
            UpdatePortalDismissAffordances();
            HandleTriggerSelectAndDrag();
            SyncSelectionMaterials();
            UpdateInsertArrow();
            HandleStorageEvents();
            BillboardLabels();
        }

        private void BillboardLabels()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var camPos = cam.transform.position;
            BillboardLabelSet(_views, camPos);
            BillboardLabelSet(_ghostViews, camPos);
        }

        private static void BillboardLabelSet(Dictionary<ulong, GlyphView> set, Vector3 camPos)
        {
            foreach (var kv in set)
            {
                var view = kv.Value;
                if (view.Root == null || !view.Root.activeInHierarchy)
                {
                    continue;
                }

                FaceCamera(view.NameLabel, camPos);
                FaceCamera(view.SecondaryLabel, camPos);
            }
        }

        private static void FaceCamera(TextMesh label, Vector3 camPos)
        {
            if (label == null)
            {
                return;
            }

            // Y-up billboard: yaw only so forward stays parallel to the floor.
            var t = label.transform;
            var away = t.position - camPos;
            away.y = 0f;
            if (away.sqrMagnitude < 1e-8f)
            {
                return;
            }

            t.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }

        public static string PreferredDocumentsDemoPath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return "/storage/emulated/0/Documents/" + DemoFolderName;
#else
            return Path.Combine(System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.MyDocuments), DemoFolderName);
#endif
        }

        public static string FallbackPersistentDemoPath() =>
            Path.Combine(Application.persistentDataPath, DemoFolderName);

        /// <summary>
        /// Install glyph packs from Resources and/or StreamingAssets. Soft-fails: demo cubes
        /// still spawn with procedural colors when packs are missing.
        /// </summary>
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
                    {
                        continue;
                    }

                    if (_bridge.InstallGlyphPack(pack.bytes))
                    {
                        installed++;
                        Debug.Log($"[3OS] installed glyph pack resource {pack.name} ({pack.bytes.Length} bytes)");
                    }
                    else
                    {
                        Debug.LogWarning($"[3OS] failed pack {pack.name}: {_bridge.LastError}");
                    }
                }
            }

            if (installed < 3)
            {
                foreach (var fileName in new[] { "box.3glyph", "box_open.3glyph", "gltf_mark.3glyph" })
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
                        {
                            Debug.LogWarning($"[3OS] failed StreamingAssets pack {fileName}: {_bridge.LastError}");
                        }
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
                        {
                            Debug.LogWarning($"[3OS] failed StreamingAssets pack {fileName}: {_bridge.LastError}");
                        }
                    }

                    yield return null;
#endif
                }
            }

            if (installed < 3)
            {
                Debug.LogWarning(
                    $"[3OS] only {installed}/3 glyph packs installed — continuing with procedural glyphs");
            }
        }

        private IEnumerator EnsureDemoDirectory()
        {
            var docs = PreferredDocumentsDemoPath();
            var fallback = FallbackPersistentDemoPath();

            // Ensure both roots exist; inserts still prefer persistent when Documents is RO.
            try
            {
                Directory.CreateDirectory(fallback);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] persistent demo create failed: {ex.Message}");
            }

            try
            {
                Directory.CreateDirectory(docs);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] Documents demo create failed: {ex.Message}");
            }

            // Folders alone are valid demo content (empty Boxes). Use Java-capable listing —
            // .NET Directory.GetFiles often returns empty on Quest Documents.
            var docsPopulated = DemoRootLooksPopulated(docs);
            var persistentPopulated = DemoRootLooksPopulated(fallback);

            // Always ensure a writable simple.glb under persistent when missing.
            if (!FileLooksPresent(Path.Combine(fallback, "simple.glb")))
            {
                yield return SeedRootObject(fallback, "simple.glb");
            }

            persistentPopulated = DemoRootLooksPopulated(fallback) ||
                                  FileLooksPresent(Path.Combine(fallback, "simple.glb"));

            // Priority: Documents when populated (MTP staging), else persistent.
            _demoRoot = PickDemoRoot(docs, fallback, docsPopulated, persistentPopulated);
            if (string.IsNullOrEmpty(_demoRoot))
            {
                _status = "no demo content — add files/folders under 3OS_Demo";
                Debug.LogError($"[3OS] {_status} (persistent={fallback} docs={docs})");
                yield break;
            }

            // Additive sync only (never delete). Documents ↔ persistent so MTP trees show
            // and inserts still have a writable mirror under app files.
#if UNITY_ANDROID && !UNITY_EDITOR
            TryMirrorDemoTree(docs, fallback);
            TryMirrorDemoTree(fallback, docs);
            if (DemoRootLooksPopulated(docs) &&
                ThreeOSAndroidStorage.ListFileNames(docs).Count == 0 &&
                !ThreeOSAndroidStorage.TryProbeFileName(docs, "simple.glb") &&
                !ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                Debug.LogWarning(
                    "[3OS] Documents has folders but no listable files — grant all-files access later if needed");
            }
#else
            TryMirrorDemoTree(_demoRoot, _demoRoot == docs ? fallback : docs);
#endif

            // Re-pick after mirror in case Documents just became the richer tree.
            docsPopulated = DemoRootLooksPopulated(docs);
            persistentPopulated = DemoRootLooksPopulated(fallback);
            _demoRoot = PickDemoRoot(docs, fallback, docsPopulated, persistentPopulated);

            Debug.Log(
                $"[3OS] demo root = {_demoRoot} (priority=Documents-when-populated; " +
                $"persistentPopulated={persistentPopulated} docsPopulated={docsPopulated} " +
                $"persistentGlb={FileLooksPresent(Path.Combine(fallback, "simple.glb"))} " +
                $"docsGlb={FileLooksPresent(Path.Combine(docs, "simple.glb"))})");
        }

        private IEnumerator EnsureDemoDirectory2()
        {
            var docs2 = PreferredDocumentsDemoPath2();
            var fallback2 = FallbackPersistentDemoPath2();
            try { Directory.CreateDirectory(fallback2); }
            catch (System.Exception ex) { Debug.LogWarning($"[3OS] Demo2 persistent create: {ex.Message}"); }
            try { Directory.CreateDirectory(docs2); }
            catch (System.Exception ex) { Debug.LogWarning($"[3OS] Demo2 Documents create: {ex.Message}"); }

            var docsPopulated = DemoRootLooksPopulated(docs2);
            var persistentPopulated = DemoRootLooksPopulated(fallback2);

#if UNITY_ANDROID && !UNITY_EDITOR
            TryMirrorDemoTree(docs2, fallback2);
            TryMirrorDemoTree(fallback2, docs2);
            docsPopulated = DemoRootLooksPopulated(docs2);
            persistentPopulated = DemoRootLooksPopulated(fallback2);
#endif

            _demoRoot2 = PickDemoRoot(docs2, fallback2, docsPopulated, persistentPopulated);
            if (string.IsNullOrEmpty(_demoRoot2))
            {
                _demoRoot2 = fallback2;
                // Ensure at least one object so the god centroid is not omitted.
                try
                {
                    var dest = Path.Combine(_demoRoot2, "simple.glb");
                    var src = !string.IsNullOrEmpty(_demoRoot)
                        ? Path.Combine(_demoRoot, "simple.glb")
                        : string.Empty;
                    if (!string.IsNullOrEmpty(src) && File.Exists(src))
                    {
                        File.Copy(src, dest, overwrite: false);
                    }
                    else if (!File.Exists(dest) &&
                             !File.Exists(Path.Combine(_demoRoot2, "marker.txt")))
                    {
                        File.WriteAllText(Path.Combine(_demoRoot2, "marker.txt"), "3OS_Demo2");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[3OS] Demo2 seed failed: {ex.Message}");
                }
            }

            Debug.Log(
                $"[3OS] demo2 root = {_demoRoot2} (docsPopulated={docsPopulated} " +
                $"persistentPopulated={persistentPopulated})");
            yield return null;
        }

        /// <summary>
        /// Prefer Documents when it has content (MTP staging); otherwise persistent.
        /// Empty string if neither is usable.
        /// </summary>
        private static string PickDemoRoot(string docs, string persistent, bool docsPopulated,
            bool persistentPopulated)
        {
            if (docsPopulated)
            {
                return docs;
            }

            if (persistentPopulated)
            {
                return persistent;
            }

            return string.Empty;
        }

        public static string PreferredDocumentsDemoPath2()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return "/storage/emulated/0/Documents/" + DemoFolderName2;
#else
            return Path.Combine(System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.MyDocuments), DemoFolderName2);
#endif
        }

        public static string FallbackPersistentDemoPath2() =>
            Path.Combine(Application.persistentDataPath, DemoFolderName2);

        /// <summary>
        /// Task 6.2 Quest fixture: two gods at 10 ft, ±15° azimuth, 2.5 ft up.
        /// </summary>
        private bool LoadDualGodFixture(ThreeOSPose head)
        {
            _bridge.StorageClear();
            _bridge.GodClear();

            var leftAnchor = GodFixturePose(head, -GodFixtureAzimuthDeg);
            var rightAnchor = GodFixturePose(head, GodFixtureAzimuthDeg);
            if (!_bridge.TryRegisterGod(DemoFolderName, leftAnchor, out var godL) ||
                !_bridge.TryRegisterGod(DemoFolderName2, rightAnchor, out var godR))
            {
                _status = $"god register failed: {_bridge.LastError}";
                return false;
            }

            if (!LoadDemoDirectory(_demoRoot, godL, clearFirst: false))
            {
                return false;
            }

            if (string.IsNullOrEmpty(_demoRoot2) ||
                !LoadDemoDirectory(_demoRoot2, godR, clearFirst: false))
            {
                Debug.LogWarning($"[3OS] Demo2 load weak: {_status} — continuing with god L only");
            }

            if (!_bridge.LayoutGod(godL, leftAnchor))
            {
                Debug.LogWarning($"[3OS] layout god L: {_bridge.LastError}");
            }

            if (!_bridge.LayoutGod(godR, rightAnchor))
            {
                Debug.LogWarning($"[3OS] layout god R: {_bridge.LastError}");
            }

            Debug.Log($"[3OS] dual-god fixture L={godL} R={godR}");
            return true;
        }

        private static ThreeOSPose GodFixturePose(ThreeOSPose head, float azimuthDeg)
        {
            // OpenXR yaw-only forward (−Z) and right (+X).
            var q = new Quaternion(head.orientation.x, head.orientation.y, head.orientation.z,
                head.orientation.w);
            var fwd = q * new Vector3(0f, 0f, -1f);
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = new Vector3(0f, 0f, -1f);
            }

            fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            var rad = azimuthDeg * Mathf.Deg2Rad;
            var dir = (Mathf.Cos(rad) * fwd + Mathf.Sin(rad) * right).normalized;
            var pos = new Vector3(
                head.position.x + dir.x * GodFixtureRangeM,
                head.position.y + GodFixtureHeightM,
                head.position.z + dir.z * GodFixtureRangeM);
            // Face user: yaw from dir toward −dir (billboard-ish).
            var face = -dir;
            var yaw = Mathf.Atan2(face.x, -face.z);
            var half = yaw * 0.5f;
            return new ThreeOSPose
            {
                position = new ThreeOSVec3 { x = pos.x, y = pos.y, z = pos.z },
                orientation = new ThreeOSQuat
                {
                    x = 0f,
                    y = Mathf.Sin(half),
                    z = 0f,
                    w = Mathf.Cos(half)
                }
            };
        }

        /// <summary>
        /// True when a demo root has any listable file or folder (Java on Android, .NET elsewhere).
        /// Empty folders count — they become Box glyphs.
        /// </summary>
        private static bool DemoRootLooksPopulated(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            if (!Directory.Exists(root) && !ThreeOSAndroidStorage.Exists(root))
            {
                return false;
            }

            if (ThreeOSAndroidStorage.ListDirectoryNames(root).Count > 0)
            {
                return true;
            }

            return ThreeOSAndroidStorage.ListFileNames(root).Count > 0;
        }

        private static bool FileLooksPresent(string path) =>
            !string.IsNullOrEmpty(path) &&
            (File.Exists(path) || ThreeOSAndroidStorage.Exists(path));

        private static bool DirectoryLooksPresent(string path) =>
            !string.IsNullOrEmpty(path) &&
            (Directory.Exists(path) || ThreeOSAndroidStorage.Exists(path));

        /// <summary>True when root has any file (root or one level down). Folders alone do not count.</summary>
        private static bool DemoTreeHasAnyContent(string root)
        {
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            if (ThreeOSAndroidStorage.ListFileNames(root).Count > 0)
            {
                return true;
            }

            foreach (var dir in ThreeOSAndroidStorage.ListDirectoryNames(root))
            {
                if (ThreeOSAndroidStorage.ListFileNames(Path.Combine(root, dir)).Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator SeedRootObject(string root, string fileName)
        {
            var dest = Path.Combine(root, fileName);
            var docs = Path.Combine(PreferredDocumentsDemoPath(), fileName);
            if (File.Exists(docs))
            {
                try
                {
                    File.Copy(docs, dest, true);
                    yield break;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[3OS] seed copy from Documents: {ex.Message}");
                }
            }

            // Already-inserted copy under any box folder counts as content (leave it there).
            foreach (var dir in ThreeOSAndroidStorage.ListDirectoryNames(root))
            {
                if (File.Exists(Path.Combine(root, dir, fileName)))
                {
                    yield break;
                }
            }

            var seedUrl = Path.Combine(Application.streamingAssetsPath, "3OS", "seed", fileName);
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var req = UnityWebRequest.Get(seedUrl))
            {
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success &&
                    req.downloadHandler?.data != null &&
                    req.downloadHandler.data.Length > 0)
                {
                    try
                    {
                        Directory.CreateDirectory(root);
                        File.WriteAllBytes(dest, req.downloadHandler.data);
                        Debug.Log($"[3OS] seeded {dest} ({req.downloadHandler.data.Length} bytes)");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[3OS] seed write failed {dest}: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[3OS] seed download failed: {req.error} url={seedUrl}");
                }
            }
#else
            if (File.Exists(seedUrl))
            {
                File.Copy(seedUrl, dest, true);
            }

            yield break;
#endif
        }

        /// <summary>
        /// Additive copy of files + box folders from→to. Never deletes destination entries
        /// (Quest Documents must not be wiped when persistent is the primary write root).
        /// </summary>
        private static void TryMirrorDemoTree(string fromRoot, string toRoot)
        {
            if (string.IsNullOrEmpty(fromRoot) || string.IsNullOrEmpty(toRoot) ||
                string.Equals(fromRoot, toRoot, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!Directory.Exists(fromRoot) && !ThreeOSAndroidStorage.Exists(fromRoot))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(toRoot);
                foreach (var name in ThreeOSAndroidStorage.ListDirectoryNames(fromRoot))
                {
                    if (string.IsNullOrEmpty(name) || name.StartsWith("."))
                    {
                        continue;
                    }

                    var fromDir = Path.Combine(fromRoot, name);
                    var toDir = Path.Combine(toRoot, name);
                    Directory.CreateDirectory(toDir);
                    foreach (var fileName in ThreeOSAndroidStorage.ListFileNames(fromDir))
                    {
                        TryCopyFile(Path.Combine(fromDir, fileName), Path.Combine(toDir, fileName));
                    }
                }

                foreach (var fileName in ThreeOSAndroidStorage.ListFileNames(fromRoot))
                {
                    TryCopyFile(Path.Combine(fromRoot, fileName), Path.Combine(toRoot, fileName));
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] TryMirrorDemoTree: {ex.Message}");
            }
        }

        private static void TryCopyFile(string from, string to)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) ||
                !FileLooksPresent(from) || FileLooksPresent(to))
            {
                return;
            }

            try
            {
                var parent = Path.GetDirectoryName(to);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(from, to, true);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] TryCopyFile {from} → {to}: {ex.Message}");
            }
        }

        private static bool DirectoryIsWritable(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".3os_write_probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Build workspace from the real directory listing — root files are field Objects,
        /// root folders are Boxes (with child counts). Files already inside a box are not
        /// re-instanced in the near field.
        /// </summary>
        private bool LoadDemoDirectory(string root) =>
            LoadDemoDirectory(root, godRootId: 0, clearFirst: true);

        private bool LoadDemoDirectory(string root, ulong godRootId, bool clearFirst)
        {
            if (clearFirst)
            {
                _bridge.StorageClear();
            }

            if (string.IsNullOrEmpty(root) ||
                (!Directory.Exists(root) && !ThreeOSAndroidStorage.Exists(root)))
            {
                _status = $"demo dir missing: {root}";
                Debug.LogError($"[3OS] {_status}");
                return false;
            }

            // Union listings for this folder name across Documents + persistent mirrors.
            var folderLeaf = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            var fileNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var dirNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var candidateRoot in CandidateDemoRootsFor(folderLeaf))
            {
                if (string.IsNullOrEmpty(candidateRoot))
                {
                    continue;
                }

                if (!Directory.Exists(candidateRoot) && !ThreeOSAndroidStorage.Exists(candidateRoot))
                {
                    continue;
                }

                foreach (var name in ThreeOSAndroidStorage.ListFileNames(candidateRoot))
                {
                    fileNames.Add(name);
                }

                foreach (var name in ThreeOSAndroidStorage.ListDirectoryNames(candidateRoot))
                {
                    dirNames.Add(name);
                }

                foreach (var probe in new[] { "simple.glb", "simple.gltf", "marker.txt" })
                {
                    if (ThreeOSAndroidStorage.TryProbeFileName(candidateRoot, probe))
                    {
                        fileNames.Add(probe);
                    }
                }
            }

            var files = new List<string>(fileNames);
            var dirs = new List<string>(dirNames);
            files.Sort(System.StringComparer.OrdinalIgnoreCase);
            dirs.Sort(System.StringComparer.OrdinalIgnoreCase);

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

                    if (godRootId != 0 && !_bridge.SetGodRoot(eid, godRootId))
                    {
                        Debug.LogWarning($"[3OS] set god root failed: {_bridge.LastError}");
                    }

                    added++;
                    Debug.Log($"[3OS] listed object {name} god={godRootId}");
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

                    if (godRootId != 0 && !_bridge.SetGodRoot(bid, godRootId))
                    {
                        Debug.LogWarning($"[3OS] set god root failed: {_bridge.LastError}");
                    }

                    added++;
                    boxes++;
                    Debug.Log($"[3OS] listed box {name} ({childCount} children) god={godRootId}");
                }
            }
            catch (System.Exception ex)
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

        /// <summary>
        /// Tally direct children (files + subfolders) inside a box folder. Unions all demo
        /// roots and uses Java listing on Android when .NET under-reports (MTP/Documents).
        /// </summary>
        private uint CountFolderContents(string dirPath, string folderName, string primaryRoot)
        {
            var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            void AddFrom(string folder)
            {
                if (string.IsNullOrEmpty(folder))
                {
                    return;
                }

                foreach (var file in ThreeOSAndroidStorage.ListFileNames(folder))
                {
                    names.Add(file);
                }

                foreach (var dir in ThreeOSAndroidStorage.ListDirectoryNames(folder))
                {
                    names.Add(dir);
                }
            }

            AddFrom(dirPath);
            foreach (var candidateRoot in CandidateDemoRoots())
            {
                if (string.IsNullOrEmpty(candidateRoot))
                {
                    continue;
                }

                AddFrom(Path.Combine(candidateRoot, folderName));
            }

            // Nested paths (box/sub) — also probe under each root with the relative suffix.
            if (!string.IsNullOrEmpty(dirPath) && !string.IsNullOrEmpty(primaryRoot) &&
                dirPath.StartsWith(primaryRoot, System.StringComparison.OrdinalIgnoreCase))
            {
                var rel = dirPath.Substring(primaryRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrEmpty(rel) &&
                    !string.Equals(rel, folderName, System.StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var candidateRoot in CandidateDemoRoots())
                    {
                        if (string.IsNullOrEmpty(candidateRoot))
                        {
                            continue;
                        }

                        AddFrom(Path.Combine(candidateRoot, rel));
                    }
                }
            }

            return (uint)names.Count;
        }

        private void RebuildViews()
        {
            // Insert/rebuild destroys glyph Roots; expanded UI must not keep X / ChildIds
            // pointing at destroyed objects (Quest MissingReference crash).
            TeardownAllExpandedBoxes(collapseKernel: true);

            foreach (var kv in _views)
            {
                DestroyGlyphView(kv.Value);
            }

            _views.Clear();
            ClearGhostViews();
            ClearPortalDismissUi();
            var count = _bridge.StorageItemCount();
            for (uint i = 0; i < count; i++)
            {
                if (!_bridge.TryGetWorkspaceItem(i, out var item))
                {
                    continue;
                }

                if ((item.flags & 1u) == 0)
                {
                    continue;
                }

                _views[item.entityId] = CreateView(item);
            }
        }

        private GlyphView CreateView(InteropWorkspaceItem item)
        {
            var go = new GameObject($"Glyph_{item.Name}");
            var movable = go.AddComponent<ThreeOSMovable>();
            movable.EntityId = item.entityId;
            movable.HalfExtent = GlyphSizeM * 0.5f;

            // Unity's builtin cube mesh is 1 m; scale to authored glyph size (0.1 m).
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = GlyphVisualScale(item.GlyphId);

            var col = visual.GetComponent<BoxCollider>();
            if (col != null)
            {
                // Collider is on the scaled child; keep it.
            }

            var mr = visual.GetComponent<MeshRenderer>();
            ResolveGlyphMaterial(item, out var mat, out var ownsMaterial);
            mr.sharedMaterial = mat;
            mr.allowOcclusionWhenDynamic = false;
            ApplyGlyphSelection(mr, rimOn: false, GlyphBaseColor(item), null, ownsMaterial ? mat : null);

            var unityPose = InteropStructs.ToUnityPose(item.pose);
            go.transform.SetPositionAndRotation(unityPose.position, unityPose.rotation);

            // Labels sit below the glyph in local space (half-size + gap).
            // Character sizes are pre-ducked 10% from the original 0.008 / 0.006.
            var labelY = -(GlyphSizeM * 0.5f + 0.025f);
            var nameLabel = CreateLabel(go.transform, item.Name, new Vector3(0f, labelY, 0f), 0.0072f);
            TextMesh secondary = null;
            if (item.kind == 1)
            {
                secondary = CreateLabel(go.transform, item.SecondaryLabel,
                    new Vector3(0f, labelY - 0.028f, 0f), 0.0054f);
            }

            return new GlyphView
            {
                EntityId = item.entityId,
                Root = go,
                Movable = movable,
                Renderer = mr,
                Collider = col,
                NameLabel = nameLabel,
                SecondaryLabel = secondary,
                GlyphId = item.GlyphId,
                Material = mat,
                OwnsMaterial = ownsMaterial
            };
        }

        private void SyncGhostViewsFromKernel()
        {
            if (_bridge == null || !_bridge.IsLoaded || _bridge.NativeAbiVersion < 6)
            {
                ClearGhostViews();
                return;
            }

            var live = new HashSet<ulong>();
            var portals = _bridge.VoodooPortalCount;
            for (uint pi = 0; pi < portals; pi++)
            {
                if (!_bridge.TryGetVoodooPortal(pi, out var portal))
                {
                    continue;
                }

                var gc = _bridge.VoodooPortalGhostCount(portal.id);
                for (uint gi = 0; gi < gc; gi++)
                {
                    if (!_bridge.TryGetVoodooPortalGhost(portal.id, gi, out var ghost))
                    {
                        continue;
                    }

                    if (!TryFindItem(ghost.entityId, out var item))
                    {
                        continue;
                    }

                    var key = GhostViewKey(portal.id, ghost.entityId, ghost.isParentBox != 0);
                    live.Add(key);
                    if (!_ghostViews.TryGetValue(key, out var view) || view.Root == null)
                    {
                        view = CreateGhostView(item, ghost);
                        _ghostViews[key] = view;
                    }
                    else
                    {
                        var pose = InteropStructs.ToUnityPose(ghost.nearPose);
                        view.Root.transform.SetPositionAndRotation(pose.position, pose.rotation);
                        var s = Mathf.Max(0.15f, ghost.scale);
                        view.Root.transform.localScale = Vector3.one * s;
                        if (view.NameLabel != null)
                        {
                            view.NameLabel.text = item.Name ?? string.Empty;
                        }

                        if (view.SecondaryLabel != null)
                        {
                            view.SecondaryLabel.text = item.SecondaryLabel ?? string.Empty;
                        }
                    }
                }
            }

            var dead = new List<ulong>();
            foreach (var kv in _ghostViews)
            {
                if (!live.Contains(kv.Key))
                {
                    dead.Add(kv.Key);
                }
            }

            foreach (var id in dead)
            {
                DestroyGlyphView(_ghostViews[id]);
                _ghostViews.Remove(id);
            }
        }

        private GlyphView CreateGhostView(InteropWorkspaceItem item, InteropVoodooGhost ghost)
        {
            var go = new GameObject($"Ghost_{item.Name}");
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = GlyphVisualScale(item.GlyphId);
            var col = visual.GetComponent<BoxCollider>();
            if (col != null)
            {
                Destroy(col); // select uses kernel poses, not physics
            }

            var mr = visual.GetComponent<MeshRenderer>();
            ResolveGlyphMaterial(item, out var mat, out var ownsMaterial);
            // Ghosts need their own MPB-friendly material instance for Fresnel.
            if (!ownsMaterial && mat != null)
            {
                mat = new Material(mat);
                ownsMaterial = true;
            }

            mr.sharedMaterial = mat;
            mr.allowOcclusionWhenDynamic = false;
            ApplyGlyphSelection(mr, rimOn: false, GlyphBaseColor(item), null, ownsMaterial ? mat : null);

            var pose = InteropStructs.ToUnityPose(ghost.nearPose);
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            go.transform.localScale = Vector3.one * Mathf.Max(0.15f, ghost.scale);

            var labelY = -(GlyphSizeM * 0.5f + 0.025f);
            var nameLabel = CreateLabel(go.transform, item.Name, new Vector3(0f, labelY, 0f), 0.0072f);
            TextMesh secondary = null;
            if (item.kind == 1)
            {
                secondary = CreateLabel(go.transform, item.SecondaryLabel,
                    new Vector3(0f, labelY - 0.028f, 0f), 0.0054f);
            }

            return new GlyphView
            {
                EntityId = item.entityId,
                Root = go,
                Movable = null,
                Renderer = mr,
                Collider = null,
                NameLabel = nameLabel,
                SecondaryLabel = secondary,
                GlyphId = item.GlyphId,
                Material = mat,
                OwnsMaterial = ownsMaterial
            };
        }

        private void ClearGhostViews()
        {
            foreach (var kv in _ghostViews)
            {
                DestroyGlyphView(kv.Value);
            }

            _ghostViews.Clear();
        }

        private static ulong GhostViewKey(ulong portalId, ulong entityId, bool parentBox) =>
            (portalId * 1000003UL) ^ (entityId * 97UL) ^ (parentBox ? 1UL : 0UL);

        private static Vector3 GlyphVisualScale(string glyphId)
        {
            // CreatePrimitive cube is 1×1×1 m; shrink to GlyphSizeM.
            if (glyphId == "box_open")
            {
                return new Vector3(GlyphSizeM * 1.05f, GlyphSizeM * 0.85f, GlyphSizeM * 1.05f);
            }

            if (glyphId == "gltf_mark")
            {
                // Slightly smaller mark so Object ≠ Box silhouette.
                return Vector3.one * (GlyphSizeM * 0.85f);
            }

            return Vector3.one * GlyphSizeM;
        }

        private void ResolveGlyphMaterial(InteropWorkspaceItem item, out Material mat, out bool ownsMaterial)
        {
            if (item.kind == 1 || item.GlyphId == "box_closed" || item.GlyphId == "box_open")
            {
                EnsureSharedBoxMaterial();
                mat = _sharedBoxMaterial;
                ownsMaterial = false;
                return;
            }

            mat = MakeGlyphMaterial(item);
            ownsMaterial = true;
        }

        private void EnsureSharedBoxMaterial()
        {
            if (_sharedBoxMaterial != null)
            {
                return;
            }

            _sharedBoxMaterial = MakeGlyphMaterialForColor(new Color(0.55f, 0.36f, 0.18f, 0.55f),
                enableInstancing: false);
            _sharedBoxMaterial.name = "3OS_SharedBoxGlyph";
        }

        private Material MakeGlyphMaterial(InteropWorkspaceItem item) =>
            MakeGlyphMaterialForColor(GlyphBaseColor(item), enableInstancing: false);

        /// <summary>
        /// Canonical glyph material: <c>ThreeOS/Glyph</c>. Glow from
        /// <see cref="GlyphGlowSettings"/>; per-glyph <paramref name="c"/> for base tint.
        /// Selection is MPB <c>_Selected</c> (not a second material).
        /// </summary>
        private Material MakeGlyphMaterialForColor(Color c, bool enableInstancing)
        {
            Material mat;
            if (_glyphShaderTemplate != null && _glyphShaderTemplate.shader != null &&
                _glyphShaderTemplate.shader.name == "ThreeOS/Glyph")
            {
                mat = new Material(_glyphShaderTemplate);
            }
            else
            {
                // Editor / last resort. Prefer Resources template so player builds keep the shader.
                var shader = Shader.Find("ThreeOS/Glyph");
                if (shader == null)
                {
                    if (!_loggedMissingGlyphShader)
                    {
                        _loggedMissingGlyphShader = true;
                        Debug.LogError(
                            "[3OS] ThreeOS/Glyph not found — Fresnel disabled. " +
                            "Ensure Runtime/Resources/3OS/GlyphDefault.mat is in the build.");
                    }

                    shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ??
                             Shader.Find("Hidden/InternalErrorShader");
                }

                mat = new Material(shader);
            }

            if (mat.HasProperty(BaseColorProp))
            {
                mat.SetColor(BaseColorProp, c);
            }

            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", c);
            }

            mat.color = c;
            if (mat.HasProperty(SelectedProp))
            {
                mat.SetFloat(SelectedProp, 0f);
            }

            if (mat.HasProperty(GlowColorProp))
            {
                mat.SetColor(GlowColorProp, ResolvedGlowColor);
            }

            if (mat.HasProperty(GlowIntensityProp))
            {
                mat.SetFloat(GlowIntensityProp, ResolvedGlowIntensity);
            }

            if (mat.HasProperty(GlowPowerProp))
            {
                mat.SetFloat(GlowPowerProp, ResolvedGlowPower);
            }

            if (mat.HasProperty(GlowWidthProp))
            {
                mat.SetFloat(GlowWidthProp, ResolvedGlowWidth);
            }

            if (mat.HasProperty(GlowBoostProp))
            {
                mat.SetFloat(GlowBoostProp, ResolvedGlowBoost);
            }

            if (mat.HasProperty(GlowViewMixProp))
            {
                mat.SetFloat(GlowViewMixProp, ResolvedGlowViewMix);
            }

            mat.enableInstancing = enableInstancing;
            mat.renderQueue = 3000;
            return mat;
        }

        /// <param name="rimOn">
        /// Shader rim gate only (property still named <c>_Selected</c>). Not ABI selection.
        /// True for white ABI-select <em>or</em> blue X-hover — same Fresnel, different
        /// <c>_GlowColor</c>.
        /// </param>
        /// <param name="ownedMaterial">
        /// When non-null, also write highlight onto the material instance. MPB alone can
        /// hard-drop the rim gate when a renderer is culled/batched (multi blue hover);
        /// single white select usually stays on-screen so it never showed the bug.
        /// </param>
        private void ApplyGlyphSelection(MeshRenderer renderer, bool rimOn, Color baseColor,
            Color? glowOverride = null, Material ownedMaterial = null)
        {
            if (renderer == null)
            {
                return;
            }

            _glyphMpb ??= new MaterialPropertyBlock();
            // Do not GetPropertyBlock — merging culled/stale blocks was flipping the rim gate.
            _glyphMpb.Clear();

            var glow = glowOverride ?? ResolvedGlowColor;
            var rim01 = rimOn ? 1f : 0f;
            _glyphMpb.SetFloat(SelectedProp, rim01);
            _glyphMpb.SetColor(BaseColorProp, baseColor);
            _glyphMpb.SetColor(GlowColorProp, glow);
            _glyphMpb.SetFloat(GlowIntensityProp, ResolvedGlowIntensity);
            _glyphMpb.SetFloat(GlowPowerProp, ResolvedGlowPower);
            _glyphMpb.SetFloat(GlowWidthProp, ResolvedGlowWidth);
            _glyphMpb.SetFloat(GlowBoostProp, ResolvedGlowBoost);
            _glyphMpb.SetFloat(GlowViewMixProp, ResolvedGlowViewMix);
            renderer.SetPropertyBlock(_glyphMpb);

            if (ownedMaterial == null)
            {
                return;
            }

            if (ownedMaterial.HasProperty(SelectedProp))
            {
                ownedMaterial.SetFloat(SelectedProp, rim01);
            }

            if (ownedMaterial.HasProperty(BaseColorProp))
            {
                ownedMaterial.SetColor(BaseColorProp, baseColor);
            }

            if (ownedMaterial.HasProperty(GlowColorProp))
            {
                ownedMaterial.SetColor(GlowColorProp, glow);
            }

            if (ownedMaterial.HasProperty(GlowIntensityProp))
            {
                ownedMaterial.SetFloat(GlowIntensityProp, ResolvedGlowIntensity);
            }

            if (ownedMaterial.HasProperty(GlowPowerProp))
            {
                ownedMaterial.SetFloat(GlowPowerProp, ResolvedGlowPower);
            }

            if (ownedMaterial.HasProperty(GlowWidthProp))
            {
                ownedMaterial.SetFloat(GlowWidthProp, ResolvedGlowWidth);
            }

            if (ownedMaterial.HasProperty(GlowBoostProp))
            {
                ownedMaterial.SetFloat(GlowBoostProp, ResolvedGlowBoost);
            }

            if (ownedMaterial.HasProperty(GlowViewMixProp))
            {
                ownedMaterial.SetFloat(GlowViewMixProp, ResolvedGlowViewMix);
            }
        }

        private static Color GlyphBaseColor(InteropWorkspaceItem item)
        {
            if (item.GlyphId == "box_closed" || item.GlyphId == "box_open" || item.kind == 1)
            {
                return new Color(0.55f, 0.36f, 0.18f, 0.55f);
            }

            if (item.GlyphId == "gltf_mark")
            {
                return new Color(0.10f, 0.72f, 0.78f, 0.55f);
            }

            return new Color(
                Mathf.Approximately(item.tintR, 1f) && Mathf.Approximately(item.tintG, 1f)
                    ? 0.75f
                    : item.tintR,
                Mathf.Approximately(item.tintR, 1f) && Mathf.Approximately(item.tintG, 1f)
                    ? 0.75f
                    : item.tintG,
                Mathf.Approximately(item.tintR, 1f) && Mathf.Approximately(item.tintG, 1f)
                    ? 0.8f
                    : item.tintB,
                Mathf.Clamp(item.tintA > 0.01f ? item.tintA : 0.55f, 0.2f, 0.85f));
        }

        private void AssignGlyphMaterials(GlyphView view, InteropWorkspaceItem item)
        {
            if (view.OwnsMaterial && view.Material != null)
            {
                Destroy(view.Material);
            }

            ResolveGlyphMaterial(item, out var mat, out var ownsMaterial);
            view.Material = mat;
            view.OwnsMaterial = ownsMaterial;
            if (view.Renderer != null)
            {
                view.Renderer.sharedMaterial = view.Material;
                view.Renderer.allowOcclusionWhenDynamic = false;
                ApplyGlyphSelection(view.Renderer, rimOn: false, GlyphBaseColor(item), null,
                    ownsMaterial ? mat : null);
            }
        }

        private void DestroyGlyphView(GlyphView view)
        {
            if (view == null)
            {
                return;
            }

            if (view.OwnsMaterial && view.Material != null)
            {
                Destroy(view.Material);
            }

            view.Material = null;

            if (view.Root != null)
            {
                Destroy(view.Root);
            }
        }

        /// <summary>
        /// Drop expand UI (X + ChildIds). Optionally collapse kernel so siblings leave the field.
        /// Required before RebuildViews / insert so destroyed Roots are never referenced.
        /// </summary>
        private void TeardownAllExpandedBoxes(bool collapseKernel)
        {
            if (_expandedBoxes.Count == 0)
            {
                _xHoverBoxId = 0;
                _animLockedIds.Clear();
                return;
            }

            var boxIds = new List<ulong>(_expandedBoxes.Keys);
            foreach (var boxId in boxIds)
            {
                if (!_expandedBoxes.TryGetValue(boxId, out var ui))
                {
                    continue;
                }

                if (ui.XRoot != null)
                {
                    Destroy(ui.XRoot);
                    ui.XRoot = null;
                }

                if (collapseKernel && _bridge != null && _bridge.IsLoaded)
                {
                    _bridge.CollapseBox(boxId);
                    _bridge.SetBoxOpen(boxId, false);
                }
            }

            _expandedBoxes.Clear();
            _xHoverBoxId = 0;
            _animLockedIds.Clear();
        }

        private static string CombineRootRel(string root, string rel)
        {
            if (string.IsNullOrEmpty(root))
            {
                return rel ?? string.Empty;
            }

            if (string.IsNullOrEmpty(rel))
            {
                return root;
            }

            var path = root;
            foreach (var part in NormalizeDemoRel(rel).Split('/'))
            {
                if (part.Length == 0)
                {
                    continue;
                }

                path = Path.Combine(path, part);
            }

            return path;
        }

        private void OnDestroy()
        {
            TeardownAllExpandedBoxes(collapseKernel: false);
            ClearPortalDismissUi();
            if (_sharedBoxMaterial != null)
            {
                Destroy(_sharedBoxMaterial);
                _sharedBoxMaterial = null;
            }
        }

        private static TextMesh CreateLabel(Transform parent, string text, Vector3 localPos, float charSize)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = string.IsNullOrEmpty(text) ? "?" : text;
            tm.characterSize = charSize;
            tm.fontSize = 58; // 64 * 0.9 — duck labels ~10%
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = Color.white;
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (tm.font == null)
            {
                tm.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            if (tm.font == null)
            {
                tm.font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "sans-serif" }, 64);
            }

            if (tm.font != null)
            {
                var mrFont = go.GetComponent<MeshRenderer>();
                if (mrFont != null)
                {
                    mrFont.sharedMaterial = tm.font.material;
                }
            }

            // Keep labels readable in front of transparent glyphs.
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            return tm;
        }

        private void SyncViewsFromKernel()
        {
            var count = _bridge.StorageItemCount();
            var seen = new HashSet<ulong>();
            for (uint i = 0; i < count; i++)
            {
                if (!_bridge.TryGetWorkspaceItem(i, out var item))
                {
                    continue;
                }

                seen.Add(item.entityId);
                var inField = (item.flags & 1u) != 0;
                var animLocked = _animLockedIds.Contains(item.entityId);
                if (!inField)
                {
                    if (_views.TryGetValue(item.entityId, out var hide))
                    {
                        if (hide.Root != null && !animLocked)
                        {
                            hide.Root.SetActive(false);
                        }
                    }

                    continue;
                }

                if (!_views.TryGetValue(item.entityId, out var view) || view.Root == null)
                {
                    _views[item.entityId] = CreateView(item);
                    view = _views[item.entityId];
                }

                view.Root.SetActive(true);
                // While kernel kinematics drives this entity (possess or coast), Movable owns pose.
                // Overwriting from workspace here caused post-release snaps to a stale layout pose.
                // Expand/collapse cluster tween also owns pose until the anim node is destroyed.
                var kineticDriven = IsKinematicallyDriven(item.entityId);
                if (!kineticDriven && !animLocked)
                {
                    var pose = InteropStructs.ToUnityPose(item.pose);
                    view.Root.transform.SetPositionAndRotation(pose.position, pose.rotation);
                }

                if (view.NameLabel != null)
                {
                    view.NameLabel.text = item.Name;
                }

                if (view.SecondaryLabel != null)
                {
                    view.SecondaryLabel.text = item.SecondaryLabel;
                }

                if (view.GlyphId != item.GlyphId)
                {
                    view.GlyphId = item.GlyphId;
                    AssignGlyphMaterials(view, item);
                    if (view.Renderer != null)
                    {
                        view.Renderer.transform.localScale = GlyphVisualScale(item.GlyphId);
                    }
                }
            }

            // Cleanup removed
            var remove = new List<ulong>();
            foreach (var id in _views.Keys)
            {
                if (!seen.Contains(id))
                {
                    remove.Add(id);
                }
            }

            foreach (var id in remove)
            {
                DestroyGlyphView(_views[id]);
                _views.Remove(id);
            }
        }

        /// <summary>
        /// Index trigger: short tap = select/deselect; double short on Box = expand;
        /// hold = telekinesis drag. X dismiss consumes the gesture. Grip/grab no longer
        /// drives storage possess (topology may still use Grab for portals).
        /// </summary>
        private void HandleTriggerSelectAndDrag()
        {
            if (_topology != null && (_topology.DomeActive || _topology.LatticeActive))
            {
                if (_triggerDragActive && (_possessing || _ghostDragging))
                {
                    ReleaseDragFromTrigger();
                }

                _triggerWasDown = false;
                _leftTriggerWasDown = false;
                _rightTriggerWasDown = false;
                _triggerDragActive = false;
                _triggerGestureConsumed = false;
                _selectHand = -1;
                return;
            }

            var frame = _input.LastFrame;
            var leftDown = frame.leftTrigger >= TriggerAxisThreshold;
            var rightDown = frame.rightTrigger >= TriggerAxisThreshold;
            var triggerDown = leftDown || rightDown;

            // Per-hand rising edge: each hand can dismiss its hovered near-BB X independently.
            var leftDismissed = false;
            var rightDismissed = false;
            if (leftDown && !_leftTriggerWasDown)
            {
                leftDismissed = TryDismissHoveredPortal(0);
            }

            if (rightDown && !_rightTriggerWasDown)
            {
                rightDismissed = TryDismissHoveredPortal(1);
            }

            _leftTriggerWasDown = leftDown;
            _rightTriggerWasDown = rightDown;

            if (triggerDown && !_triggerWasDown)
            {
                if (leftDown && !rightDown)
                {
                    _selectHand = 0;
                }
                else if (rightDown && !leftDown)
                {
                    _selectHand = 1;
                }
                else
                {
                    _selectHand = frame.leftTrigger >= frame.rightTrigger ? 0 : 1;
                }

                _triggerDownTime = Time.unscaledTime;
                _triggerDragActive = false;
                _triggerGestureConsumed = false;
                _triggerDownCursorXr = SelectHandCursorOpenXr();
                _triggerDownCursorValid = true;
                var dismissedThisHand = (_selectHand == 0 && leftDismissed) ||
                                        (_selectHand == 1 && rightDismissed);
                if (dismissedThisHand || TryBeginCloseOnXHover())
                {
                    _triggerGestureConsumed = true;
                }
            }

            var aimUnity = SelectAimUnity(_selectHand, out var rayDir);

            if (triggerDown && _triggerWasDown && !_triggerDragActive && !_possessing &&
                !_ghostDragging && !_triggerGestureConsumed)
            {
                _bridge.TryGetSelection(out var selForHold);
                var cursorXr = SelectHandCursorOpenXr();
                var homePortal = selForHold != 0 ? _bridge.PortalForGhost(selForHold) : 0UL;
                var portalAtCursor = _bridge.PortalAtNearPoint(cursorXr);
                // Summon zone: selected ghost + cursor outside its home near BB.
                // Use a longer hold so a normal pinch releases as summon, not far-drag.
                var summonZone = homePortal != 0 && portalAtCursor != homePortal;
                float holdSec;
                if (summonZone)
                {
                    holdSec = Mathf.Max(TriggerHoldDragSeconds, SummonZoneHoldDragSeconds);
                }
                else if (selForHold != 0)
                {
                    // Inside home BB / near-real: still prefer drag over re-select.
                    holdSec = Mathf.Min(TriggerHoldDragSeconds, 0.14f);
                }
                else
                {
                    holdSec = TriggerHoldDragSeconds;
                }

                if (Time.unscaledTime - _triggerDownTime >= holdSec)
                {
                    var selectedId = selForHold;
                    // Hover glyph/ghost under the cursor wins over a prior selection so
                    // direct-drag on B works while A is selected. Unselected + palm-down:
                    // leave yellow voodoo (no Windows grab). No hit + selection: drag
                    // selection (palm ignored) — velocity / possess / ghost-scale as before.
                    ulong dragId = 0;
                    if (TryHitTestYawAligned(aimUnity.position, rayDir, out var hitId) &&
                        TryFindItem(hitId, out var hitItem) &&
                        (hitItem.kind == 0 || hitItem.kind == 1) &&
                        (selectedId != 0 || SelectHandPalmMode() != PalmMode.Down))
                    {
                        if (hitId != selectedId && _bridge.SetSelection(hitId))
                        {
                            _selectionLabel = hitItem.Name;
                            _status = $"Sel:{_selectionLabel}";
                        }

                        dragId = hitId;
                    }
                    else if (selectedId != 0)
                    {
                        dragId = selectedId;
                    }

                    if (dragId != 0)
                    {
                        _triggerDragActive = true;
                        TryBeginSelectedDrag(dragId, aimUnity.position);
                    }
                }
            }

            if (!triggerDown && _triggerWasDown)
            {
                if (_triggerGestureConsumed)
                {
                    // X close (or other consumed press) — do not select/drag.
                }
                else if (_triggerDragActive || _possessing || _ghostDragging)
                {
                    ReleaseDragFromTrigger();
                }
                else if (SelectHandPalmMode() == PalmMode.Down &&
                         Time.unscaledTime - _triggerDownTime >= TriggerHoldDragSeconds)
                {
                    // Held palm-down for voodoo — do not treat release as a select tap.
                }
                else if (_triggerDownCursorValid &&
                         Vector3.Distance(SelectHandCursorOpenXr(), _triggerDownCursorXr) >
                         SummonSwipeCancelMeters())
                {
                    // Moved while held but never crossed drag threshold — abort, do not summon.
                    _status = "drag cancelled";
                }
                else
                {
                    // Short press: double-tap Box opens; else select / summon / deselect.
                    HandleShortTriggerTap(aimUnity.position, rayDir);
                }

                _triggerDragActive = false;
                _triggerGestureConsumed = false;
                _triggerDownCursorValid = false;
                _selectHand = -1;
            }

            _triggerWasDown = triggerDown;
        }

        /// <summary>
        /// Cursor travel that cancels a short-release summon. Wider in summon zone so
        /// Quest hand jitter during a deliberate pinch does not abort.
        /// </summary>
        private float SummonSwipeCancelMeters()
        {
            if (!_bridge.TryGetSelection(out var sel) || sel == 0)
            {
                return 0.03f;
            }

            var home = _bridge.PortalForGhost(sel);
            if (home == 0)
            {
                return 0.03f;
            }

            var at = _bridge.PortalAtNearPoint(SelectHandCursorOpenXr());
            return at != home ? 0.08f : 0.03f;
        }

        private void HandleShortTriggerTap(Vector3 rayOrigin, Vector3 rayDir)
        {
            if (_boxAnimBusy || _insertAnimBusy)
            {
                return;
            }

            _bridge.TryGetSelection(out var selectedId);
            var hitSomething = TryHitTestYawAligned(rayOrigin, rayDir, out var hitId);
            var pinchXr = SelectHandCursorOpenXr();
            var homePortal = selectedId != 0 ? _bridge.PortalForGhost(selectedId) : 0UL;
            var portalAtPinch = _bridge.PortalAtNearPoint(pinchXr);
            var hitPortal = hitSomething ? _bridge.PortalForGhost(hitId) : 0UL;

            // Select-drop onto a Box (same or other near BB): insert, don't retarget selection.
            // Near-field matrix without portals still uses tap-to-select (no auto-insert).
            if (selectedId != 0 && hitSomething &&
                TryFindItem(hitId, out var boxHit) && boxHit.kind == 1 && hitId != selectedId &&
                (homePortal != 0 || hitPortal != 0 || portalAtPinch != 0))
            {
                if (_bridge.InsertInto(selectedId, hitId))
                {
                    _bridge.ClearSelection();
                    _selectionLabel = "none";
                    _status = $"insert → {boxHit.Name}";
                    _lastShortTapId = 0;
                    RemoveGhostViewsForEntity(selectedId);
                }
                else
                {
                    _status = $"insert failed: {_bridge.LastError}";
                }

                return;
            }

            // Task 6.5: empty pinch with selection → deselect / summon / insert.
            if (selectedId != 0 && !hitSomething)
            {
                // Selected ghost + empty inside home BB → deselect.
                if (homePortal != 0 && portalAtPinch == homePortal)
                {
                    _bridge.ClearSelection();
                    _selectionLabel = "none";
                    _status = "Sel:none";
                    _lastShortTapId = 0;
                    return;
                }

                // Ghost outside / cross-portal, or near-real into a BB → summon/insert + lerp.
                if (_bridge.VoodooSummon(selectedId, pinchXr))
                {
                    if (homePortal == 0 && portalAtPinch != 0)
                    {
                        _status = "insert → far (lerp)";
                    }
                    else if (portalAtPinch != 0 && portalAtPinch != homePortal)
                    {
                        _status = "summon cross-portal (lerp)";
                    }
                    else
                    {
                        _status = "summon near-field (lerp)";
                    }

                    _lastShortTapId = 0;
                    return;
                }
            }

            if (!hitSomething)
            {
                _lastShortTapId = 0;
                ApplySelectAtAim(rayOrigin, rayDir);
                return;
            }

            var now = Time.unscaledTime;
            var isBox = TryFindItem(hitId, out var hitItem) && hitItem.kind == 1;
            var doubleTap = isBox && hitId == _lastShortTapId &&
                            (now - _lastShortTapTime) <= BoxDoubleTapSeconds &&
                            !_expandedBoxes.ContainsKey(hitId);

            if (doubleTap)
            {
                _lastShortTapId = 0;
                _lastShortTapTime = -10f;
                StartCoroutine(OpenBoxSequence(hitId));
                return;
            }

            _lastShortTapId = hitId;
            _lastShortTapTime = now;
            ApplySelectAtAim(rayOrigin, rayDir);
        }

        private void RemoveGhostViewsForEntity(ulong entityId)
        {
            if (entityId == 0 || _ghostViews.Count == 0)
            {
                return;
            }

            var dead = new List<ulong>();
            foreach (var kv in _ghostViews)
            {
                if (kv.Value.EntityId == entityId)
                {
                    dead.Add(kv.Key);
                }
            }

            foreach (var key in dead)
            {
                DestroyGlyphView(_ghostViews[key]);
                _ghostViews.Remove(key);
            }
        }

        private void ApplySelectAtAim(Vector3 rayOrigin, Vector3 rayDir)
        {
            if (TryHitTestYawAligned(rayOrigin, rayDir, out var hitId))
            {
                if (_bridge.SetSelection(hitId))
                {
                    _selectionLabel = TryFindItem(hitId, out var item) ? item.Name : hitId.ToString();
                    _status = $"Sel:{_selectionLabel}";
                }
                else
                {
                    _status = $"select failed: {_bridge.LastError}";
                }
            }
            else
            {
                _bridge.ClearSelection();
                _selectionLabel = "none";
                _status = "Sel:none";
            }
        }

        private void ReleasePossessedFromTrigger()
        {
            var hadSnap = _bridge.TryGetSnap(out var snap) && snap.pending != 0;
            if (!_bridge.ReleasePossessed())
            {
                // Do not retry TryCommitInsert — kernel owns snap commit/clear on
                // release; host retries caused multi-box FS thrash / frame spikes.
                _status = hadSnap
                    ? $"insert failed: {_bridge.LastError}"
                    : $"released (coast): {_bridge.LastError}";
            }
            else
            {
                _status = hadSnap ? "insert — waiting FS" : "released — coasting";
            }

            _possessing = false;
            _possessedId = 0;
            _triggerDragActive = false;
        }

        private void ReleaseDragFromTrigger()
        {
            if (_ghostDragging && _selectHand >= 0)
            {
                var hadSnap = _bridge.TryGetSnap(out var snap) && snap.pending != 0;
                var pinchXr = SelectHandCursorOpenXr();
                var inside = _bridge.PointInAnyNearBb(pinchXr);
                var ok = _bridge.GhostDragEnd((uint)_selectHand, inside);
                if (hadSnap)
                {
                    // Kernel re-validates near snap range on end; do not retry commit
                    // (avoids double-insert / multi-box FS thrash and frame spikes).
                    _status = ok
                        ? "ghost insert — waiting FS"
                        : $"ghost snap released: {_bridge.LastError}";
                }
                else
                {
                    _status = inside ? "ghost drag end (inside)" : "ghost drag end";
                }

                _ghostDragging = false;
                _triggerDragActive = false;
                return;
            }

            if (_possessing)
            {
                ReleasePossessedFromTrigger();
            }
        }

        private Vector3 SelectHandCursorOpenXr()
        {
            if (_input != null)
            {
                if (_selectHand == 0 && _input.LastLeftCursor.active != 0)
                {
                    var p = _input.LastLeftCursor.position;
                    return new Vector3(p.x, p.y, p.z);
                }

                if (_selectHand == 1 && _input.LastRightCursor.active != 0)
                {
                    var p = _input.LastRightCursor.position;
                    return new Vector3(p.x, p.y, p.z);
                }
            }

            var aim = SelectAimUnity(_selectHand, out _);
            return InteropStructs.UnityToOpenXrPosition(aim.position);
        }

        private void TryBeginSelectedDrag(ulong selectedId, Vector3 aimUnity)
        {
            if (_selectHand < 0)
            {
                return;
            }

            var cursorXr = SelectHandCursorOpenXr();
            var homePortal = _bridge.PortalForGhost(selectedId);
            var portalAtCursor = _bridge.PortalAtNearPoint(cursorXr);

            // Committed ghost inside home BB → ghost-scale drag.
            // Near-field real (no ghost) → audition drag (kernel handles enter/exit BB).
            // Committed ghost outside home BB → far telekinesis of the real.
            if (homePortal != 0 && portalAtCursor == homePortal)
            {
                if (_bridge.GhostDragBegin((uint)_selectHand, selectedId))
                {
                    _ghostDragging = true;
                    _possessedId = selectedId;
                    _status = "ghost drag — blue stick";
                    return;
                }
            }

            if (homePortal == 0)
            {
                if (_bridge.GhostDragBegin((uint)_selectHand, selectedId))
                {
                    _ghostDragging = true;
                    _possessedId = selectedId;
                    // Kernel picks direct (cursor-in-glyph) vs velocity stick for near-field.
                    _status = portalAtCursor != 0
                        ? "audition insert — ghost in BB"
                        : "near drag — direct if in glyph, else stick";
                    return;
                }
            }

            TryPossessNearest(aimUnity);
        }

        private bool PossessForSelectHand(ulong entityId, ThreeOSPose objectPose)
        {
            if (_selectHand >= 0)
            {
                return _bridge.PossessWithHand(entityId, objectPose, _selectHand);
            }

            return _bridge.Possess(entityId, objectPose);
        }

        private PalmMode SelectHandPalmMode()
        {
            if (_input == null)
            {
                return PalmMode.Sideways;
            }

            if (_selectHand == 0 && _input.LastLeftCursor.active != 0)
            {
                return (PalmMode)_input.LastLeftCursor.palmMode;
            }

            if (_selectHand == 1 && _input.LastRightCursor.active != 0)
            {
                return (PalmMode)_input.LastRightCursor.palmMode;
            }

            return (PalmMode)_input.LastIntents.palmMode;
        }

        private void TryPossessNearest(Vector3 aimUnity)
        {
            // Clear a stuck FS event so a prior failed insert cannot block the next grab/commit.
            if (!_insertAnimBusy &&
                _bridge.TryPollStorageEvent(out var stuck) &&
                stuck.awaitingAck != 0)
            {
                Debug.LogWarning("[3OS] clearing stuck storage event before possess");
                _bridge.AckStorageEvent(false);
            }

            _bridge.TryGetSelection(out var selectedId);

            // Selection-gated only — never possess a deselected / nearest glyph.
            if (selectedId == 0 ||
                !TryFindItem(selectedId, out var selectedItem) ||
                (selectedItem.kind != 0 && selectedItem.kind != 1))
            {
                _status = "grab requires selection";
                return;
            }

            // Prefer live view pose; fall back to kernel workspace pose when the far glyph
            // is missing/inactive so outside-home velocity still engages for ghosts.
            ThreeOSPose possessPose = selectedItem.pose;
            if (_views.TryGetValue(selectedId, out var selectedView) &&
                selectedView.Root != null && selectedView.Root.activeInHierarchy &&
                selectedView.Movable != null)
            {
                possessPose = selectedView.Movable.CurrentOpenXrPose();
            }

            if (PossessForSelectHand(selectedId, possessPose))
            {
                _possessing = true;
                _possessedId = selectedId;
                _status = selectedItem.kind == 1
                    ? "far possess folder — stick drag"
                    : "far possess — drag onto a folder";
            }
            else
            {
                _status = $"possess failed: {_bridge.LastError}";
            }
        }

        private bool TryHitTestYawAligned(Vector3 rayOrigin, Vector3 rayDir, out ulong hitId)
        {
            hitId = 0;
            if (rayDir.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            rayDir.Normalize();
            var yaw = FloorYawRotation();
            var half = GlyphSizeM * 0.5f;
            var bestT = float.PositiveInfinity;
            ulong bestId = 0;

            foreach (var kv in _views)
            {
                if (kv.Value.Root == null || !kv.Value.Root.activeInHierarchy)
                {
                    continue;
                }

                var center = kv.Value.Root.transform.position;
                if (!RayHitsYawAlignedAabb(rayOrigin, rayDir, center, yaw, half, out var t) || t < 0f ||
                    t >= bestT)
                {
                    continue;
                }

                bestT = t;
                bestId = kv.Key;
            }

            // Task 6.4/6.5: select ghosts → same far entity id (drag via ghost-scale path).
            if (_bridge != null && _bridge.IsLoaded)
            {
                var portals = _bridge.VoodooPortalCount;
                for (uint pi = 0; pi < portals; pi++)
                {
                    if (!_bridge.TryGetVoodooPortal(pi, out var portal))
                    {
                        continue;
                    }

                    var gc = _bridge.VoodooPortalGhostCount(portal.id);
                    for (uint gi = 0; gi < gc; gi++)
                    {
                        if (!_bridge.TryGetVoodooPortalGhost(portal.id, gi, out var ghost))
                        {
                            continue;
                        }

                        var center = InteropStructs.ToUnityPose(ghost.nearPose).position;
                        var gHalf = half * Mathf.Max(0.15f, ghost.scale);
                        if (!RayHitsYawAlignedAabb(rayOrigin, rayDir, center, yaw, gHalf, out var t) ||
                            t < 0f || t >= bestT)
                        {
                            continue;
                        }

                        bestT = t;
                        bestId = ghost.entityId;
                    }
                }
            }

            if (bestId == 0)
            {
                return false;
            }

            hitId = bestId;
            return true;
        }

        private static bool RayHitsYawAlignedAabb(Vector3 origin, Vector3 dir, Vector3 center,
            Quaternion yaw, float half, out float tHit)
        {
            tHit = 0f;
            var inv = Quaternion.Inverse(yaw);
            var o = inv * (origin - center);
            var d = inv * dir;
            var tMin = float.NegativeInfinity;
            var tMax = float.PositiveInfinity;

            if (!Slab(o.x, d.x, -half, half, ref tMin, ref tMax) ||
                !Slab(o.y, d.y, -half, half, ref tMin, ref tMax) ||
                !Slab(o.z, d.z, -half, half, ref tMin, ref tMax))
            {
                return false;
            }

            tHit = tMin >= 0f ? tMin : tMax;
            return tHit >= 0f;
        }

        private static bool Slab(float o, float d, float min, float max, ref float tMin, ref float tMax)
        {
            const float eps = 1e-8f;
            if (Mathf.Abs(d) < eps)
            {
                return o >= min && o <= max;
            }

            var invD = 1f / d;
            var t0 = (min - o) * invD;
            var t1 = (max - o) * invD;
            if (t0 > t1)
            {
                (t0, t1) = (t1, t0);
            }

            tMin = Mathf.Max(tMin, t0);
            tMax = Mathf.Min(tMax, t1);
            return tMin <= tMax;
        }

        private static Quaternion FloorYawRotation()
        {
            var fwd = Vector3.forward;
            var cam = Camera.main;
            if (cam != null)
            {
                fwd = cam.transform.forward;
            }

            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = Vector3.forward;
            }

            return Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        private void SyncSelectionMaterials()
        {
            _bridge.TryGetSelection(out var selectedId);
            if (selectedId == 0)
            {
                _selectionLabel = "none";
            }
            else if (TryFindItem(selectedId, out var selectedItem))
            {
                _selectionLabel = selectedItem.Name;
            }

            HashSet<ulong> blueChildren = null;
            if (_xHoverBoxId != 0 && _expandedBoxes.TryGetValue(_xHoverBoxId, out var hoverUi))
            {
                blueChildren = new HashSet<ulong>(hoverUi.ChildIds);
                foreach (var id in CollectChildIds(_xHoverBoxId))
                {
                    blueChildren.Add(id);
                }
            }

            foreach (var kv in _views)
            {
                ApplySelectionRim(kv.Key, kv.Value, selectedId, blueChildren);
            }

            // Ghosts share the far entity id for Fresnel — both glow when selected.
            foreach (var kv in _ghostViews)
            {
                ApplySelectionRim(kv.Value.EntityId, kv.Value, selectedId, blueChildren);
            }
        }

        private void ApplySelectionRim(ulong entityId, GlyphView view, ulong selectedId,
            HashSet<ulong> blueChildren)
        {
            if (view.Renderer == null)
            {
                return;
            }

            var abiSelected = selectedId != 0 && entityId == selectedId;
            var blueHover = blueChildren != null && blueChildren.Contains(entityId);
            var rimOn = abiSelected || blueHover;
            var owned = view.OwnsMaterial ? view.Material : null;
            if (!TryFindItem(entityId, out var item))
            {
                ApplyGlyphSelection(view.Renderer, rimOn, Color.white,
                    blueHover ? ExpandHoverGlowColor : (Color?)null, owned);
                return;
            }

            ApplyGlyphSelection(view.Renderer, rimOn, GlyphBaseColor(item),
                blueHover ? ExpandHoverGlowColor : (Color?)null, owned);
        }

        private bool TryFindItem(ulong id, out InteropWorkspaceItem item)
        {
            item = default;
            var count = _bridge.StorageItemCount();
            for (uint i = 0; i < count; i++)
            {
                if (_bridge.TryGetWorkspaceItem(i, out item) && item.entityId == id)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsKinematicallyDriven(ulong entityId)
        {
            if (_possessing && _possessedId == entityId)
            {
                return true;
            }

            // Ghost-drag updates workspace each tick; SyncViews should still apply poses.
            if (_ghostDragging && _possessedId == entityId)
            {
                return false;
            }

            if (_bridge.TryGetKineticState(out var ks) && ks.entityId == entityId &&
                (ks.phase == (uint)KineticPhase.Possessed || ks.phase == (uint)KineticPhase.Coasting))
            {
                return true;
            }

            return false;
        }

        private bool TryFindSnapGlyphRoot(ulong entityId, out Transform root)
        {
            root = null;
            // Prefer near ghosts so the drop affordance sits in the near-BB gap.
            foreach (var kv in _ghostViews)
            {
                if (kv.Value.EntityId == entityId && kv.Value.Root != null &&
                    kv.Value.Root.activeInHierarchy)
                {
                    root = kv.Value.Root.transform;
                    return true;
                }
            }

            if (_views.TryGetValue(entityId, out var view) && view.Root != null &&
                view.Root.activeInHierarchy)
            {
                root = view.Root.transform;
                return true;
            }

            return false;
        }

        private void UpdateInsertArrow()
        {
            if (_insertArrow == null || !_bridge.TryGetSnap(out var snap) || snap.pending == 0)
            {
                _insertArrow?.Hide();
                return;
            }

            if (!TryFindSnapGlyphRoot(snap.objectId, out var objT) ||
                !TryFindSnapGlyphRoot(snap.boxId, out var boxT))
            {
                _insertArrow.Hide();
                return;
            }

            // Static down arrow parked in the hold gap (not the velocity stick).
            var objPos = objT.position;
            var boxPos = boxT.position;
            var x = (objPos.x + boxPos.x) * 0.5f;
            var z = (objPos.z + boxPos.z) * 0.5f;
            var gap = objPos.y - boxPos.y;
            Vector3 shaftStart;
            Vector3 tip;
            if (gap > 0.03f)
            {
                shaftStart = new Vector3(x, boxPos.y + gap * 0.72f, z);
                tip = new Vector3(x, boxPos.y + gap * 0.28f, z);
            }
            else
            {
                // Degenerate / overlapping: still show a compact drop cue.
                var midY = (objPos.y + boxPos.y) * 0.5f;
                shaftStart = new Vector3(x, midY + 0.02f, z);
                tip = new Vector3(x, midY - 0.02f, z);
            }

            var nearSnap = IsNearFieldSnapGlyph(snap.objectId) || IsNearFieldSnapGlyph(snap.boxId);
            var shaftColor = nearSnap
                ? new Color(0.35f, 0.75f, 1f, 0.9f)
                : new Color(1f, 0.85f, 0.2f, 0.9f);
            _insertArrow.Set(shaftStart, tip, shaftColor);
        }

        private bool IsNearFieldSnapGlyph(ulong entityId)
        {
            foreach (var kv in _ghostViews)
            {
                if (kv.Value.EntityId == entityId && kv.Value.Root != null &&
                    kv.Value.Root.activeInHierarchy)
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleStorageEvents()
        {
            if (_insertAnimBusy)
            {
                return;
            }

            if (!_bridge.TryPollStorageEvent(out var ev) || ev.type == 0 || ev.awaitingAck == 0)
            {
                return;
            }

            StartCoroutine(InsertSequence(ev));
        }

        private IEnumerator InsertSequence(InteropStorageEvent ev)
        {
            _insertAnimBusy = true;
            var ok = false;
            var src = string.Empty;
            var dst = string.Empty;
            // Note: C# forbids yield inside try/catch — use try/finally only.
            try
            {
                // Dropping a child from an open cluster invalidates expand UI before views move.
                TeardownAllExpandedBoxes(collapseKernel: true);

                // Prefer far-field glyph; fall back to a near ghost of the destination box.
                Transform bumpT = null;
                if (_views.TryGetValue(ev.boxId, out var boxView) && boxView.Root != null)
                {
                    bumpT = boxView.Root.transform;
                }
                else
                {
                    foreach (var kv in _ghostViews)
                    {
                        if (kv.Value.EntityId == ev.boxId && kv.Value.Root != null)
                        {
                            bumpT = kv.Value.Root.transform;
                            break;
                        }
                    }
                }

                if (bumpT != null)
                {
                    _bridge.SetBoxOpen(ev.boxId, true);
                    var baseScale = bumpT.localScale;
                    yield return AnimateScaleBump(bumpT, baseScale, 1.15f, 0.12f);
                    _bridge.SetBoxOpen(ev.boxId, false);
                    yield return new WaitForSeconds(0.08f);
                }
                else
                {
                    yield return null;
                }

                ResolveInsertPaths(ev, out var objName, out var boxName, out var sourceRel,
                    out var destRel);
                // Same path (already nested under target): kernel parent is updated; skip FS cut.
                if (!string.IsNullOrEmpty(sourceRel) &&
                    string.Equals(NormalizeDemoRel(sourceRel), NormalizeDemoRel(destRel),
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    ok = true;
                    dst = destRel;
                    _status = $"already in {boxName}/";
                }
                else
                {
                    var sourceIsBox = TryFindItem(ev.objectId, out var srcItem) && srcItem.kind == 1;
                    _status = $"moving {objName} → {boxName}/";
                    yield return CommitInsertToDisk(objName, boxName, sourceIsBox, sourceRel, destRel,
                        result =>
                        {
                            ok = result.ok;
                            src = result.src;
                            dst = result.dst;
                        });

                    _status = ok
                        ? $"moved → {ShortPath(dst)}"
                        : $"FS move failed — {ShortPath(string.IsNullOrEmpty(dst) ? src : dst)}";
                    if (!ok)
                    {
                        Debug.LogError($"[3OS] {_status} (src={src} dst={dst} root={_demoRoot})");
                    }
                    else
                    {
                        Debug.Log($"[3OS] {_status} (full={dst})");
                    }
                }
            }
            finally
            {
                // Always ack + clear busy so a failed/aborted insert cannot disable grab.
                _bridge.AckStorageEvent(ok);
                _insertAnimBusy = false;
            }

            RebuildViews();
        }

        /// <summary>
        /// Resolve leaf names plus full relative paths for FS cut. Nested children
        /// (e.g. <c>Parent/ChildBox</c>) must not be treated as root-level names.
        /// </summary>
        private void ResolveInsertPaths(InteropStorageEvent ev, out string objName, out string boxName,
            out string sourceRel, out string destRel)
        {
            objName = string.Empty;
            boxName = string.Empty;
            sourceRel = string.Empty;
            destRel = string.Empty;

            if (TryFindItem(ev.objectId, out var obj) && !string.IsNullOrEmpty(obj.Name))
            {
                objName = obj.Name;
            }

            if (TryFindItem(ev.boxId, out var box) && !string.IsNullOrEmpty(box.Name))
            {
                boxName = box.Name;
            }

            if (!string.IsNullOrEmpty(ev.SourceRel))
            {
                sourceRel = NormalizeDemoRel(ev.SourceRel);
                var leaf = Path.GetFileName(sourceRel.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(leaf))
                {
                    objName = leaf;
                }
            }
            else if (!string.IsNullOrEmpty(objName))
            {
                sourceRel = objName;
            }

            if (!string.IsNullOrEmpty(ev.DestRel))
            {
                destRel = NormalizeDemoRel(ev.DestRel);
                var norm = destRel.Replace('\\', '/');
                var slash = norm.LastIndexOf('/');
                if (slash > 0)
                {
                    var folder = norm.Substring(0, slash);
                    var leaf = norm.Substring(slash + 1);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        // Destination parent may itself be nested (box/sub).
                        boxName = folder;
                    }

                    if (!string.IsNullOrEmpty(leaf))
                    {
                        objName = leaf;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(boxName) && !string.IsNullOrEmpty(objName))
            {
                destRel = $"{boxName}/{objName}";
            }

            if (string.IsNullOrEmpty(sourceRel) && !string.IsNullOrEmpty(objName))
            {
                sourceRel = objName;
            }

            if (string.IsNullOrEmpty(destRel) && !string.IsNullOrEmpty(boxName) &&
                !string.IsNullOrEmpty(objName))
            {
                destRel = $"{boxName}/{objName}";
            }
        }

        private static string NormalizeDemoRel(string rel)
        {
            if (string.IsNullOrEmpty(rel))
            {
                return string.Empty;
            }

            return rel.Replace('\\', '/').Trim().Trim('/');
        }

        private struct InsertFsResult
        {
            public bool ok;
            public string src;
            public string dst;
        }

        /// <summary>
        /// Write-through insert: cut within each demo root (Documents first on Quest so the
        /// MTP-visible original is renamed, not copied). Then scrub every root-level leftover.
        /// Supports file Objects and folder Boxes (box-into-box), including nested rel paths.
        /// </summary>
        private IEnumerator CommitInsertToDisk(string objName, string boxName, bool sourceIsBox,
            string sourceRel, string destRel, System.Action<InsertFsResult> done)
        {
            var result = new InsertFsResult
            {
                ok = false,
                src = string.Empty,
                dst = string.Empty,
            };

            sourceRel = string.IsNullOrEmpty(sourceRel) ? objName : NormalizeDemoRel(sourceRel);
            destRel = string.IsNullOrEmpty(destRel)
                ? NormalizeDemoRel($"{boxName}/{objName}")
                : NormalizeDemoRel(destRel);

            var docs = PreferredDocumentsDemoPath();
            var persistent = FallbackPersistentDemoPath();
            var writeRoot = string.IsNullOrEmpty(_demoRoot) ? persistent : _demoRoot;

            try
            {
                Directory.CreateDirectory(writeRoot);
                var destParent = Path.GetDirectoryName(CombineRootRel(writeRoot, destRel));
                if (!string.IsNullOrEmpty(destParent))
                {
                    Directory.CreateDirectory(destParent);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[3OS] insert mkdir failed: {ex.Message}");
                done?.Invoke(result);
                yield break;
            }

            result.dst = CombineRootRel(writeRoot, destRel);
            result.src = CombineRootRel(writeRoot, sourceRel);

            if (sourceIsBox)
            {
                yield return CommitInsertFolderToDisk(sourceRel, destRel, docs, persistent, writeRoot,
                    result, done);
                yield break;
            }

            byte[] data = null;
            string loadedFrom = null;

            // Prefer reading from Documents (what the user browses), then persistent, then seed.
            // Nested children use sourceRel (e.g. Parent/child.glb), not root leaf alone.
            foreach (var candidate in new[]
                     {
                         CombineRootRel(docs, sourceRel),
                         CombineRootRel(persistent, sourceRel),
                         CombineRootRel(writeRoot, sourceRel),
                         Path.Combine(docs, objName),
                         Path.Combine(persistent, objName),
                         Path.Combine(writeRoot, objName),
                     })
            {
                if (string.IsNullOrEmpty(candidate) || !FileLooksPresent(candidate))
                {
                    continue;
                }

                try
                {
                    data = File.ReadAllBytes(candidate);
                    if (data != null && data.Length > 0)
                    {
                        loadedFrom = candidate;
                        result.src = candidate;
                        break;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[3OS] read {candidate}: {ex.Message}");
                }
            }

            if (data == null || data.Length == 0)
            {
                foreach (var root in CandidateDemoRoots())
                {
                    foreach (var rel in new[] { sourceRel, objName })
                    {
                        var candidate = CombineRootRel(root, rel);
                        if (!FileLooksPresent(candidate))
                        {
                            continue;
                        }

                        try
                        {
                            data = File.ReadAllBytes(candidate);
                            loadedFrom = candidate;
                            result.src = candidate;
                            break;
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"[3OS] read {candidate}: {ex.Message}");
                        }
                    }

                    if (data != null && data.Length > 0)
                    {
                        break;
                    }
                }
            }

            if (data == null || data.Length == 0)
            {
                var seedUrl = Path.Combine(Application.streamingAssetsPath, "3OS", "seed", "simple.glb");
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var req = UnityWebRequest.Get(seedUrl))
                {
                    var op = req.SendWebRequest();
                    var waited = 0f;
                    while (!op.isDone && waited < 5f)
                    {
                        waited += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (op.isDone && req.result == UnityWebRequest.Result.Success)
                    {
                        data = req.downloadHandler.data;
                        loadedFrom = seedUrl;
                    }
                    else if (!op.isDone)
                    {
                        req.Abort();
                    }
                }
#else
                if (File.Exists(seedUrl))
                {
                    data = File.ReadAllBytes(seedUrl);
                    loadedFrom = seedUrl;
                }

                yield return null;
#endif
            }

            if (data == null || data.Length == 0)
            {
                Debug.LogError("[3OS] insert: no source bytes for " + objName);
                done?.Invoke(result);
                yield break;
            }

            var docsSrc = CombineRootRel(docs, sourceRel);
            var needsDocumentsCut = FileLooksPresent(docsSrc) ||
                                    string.Equals(writeRoot, docs, System.StringComparison.OrdinalIgnoreCase) ||
                                    (loadedFrom != null && loadedFrom.StartsWith(docs,
                                        System.StringComparison.OrdinalIgnoreCase));

            // Without all-files access, Documents writes can succeed while deletes fail → copy.
            // Do not touch Documents until permission is granted; roll back the glyph (ack false).
#if UNITY_ANDROID && !UNITY_EDITOR
            if (needsDocumentsCut && !ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                Debug.LogWarning("[3OS] deferring Documents cut until all-files access is granted");
                yield return EnsureAllFilesAccessForCut("grant All files access, then drop again");
                if (!ThreeOSAndroidStorage.IsExternalStorageManager())
                {
                    result.ok = false;
                    _status = "grant All files access, then drop again";
                    done?.Invoke(result);
                    yield break;
                }
            }
#endif

            // Cut Documents only with all-files access (otherwise write can succeed and delete fail).
            var cutDocs = false;
            if (ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                cutDocs = TryCutRelFile(docs, sourceRel, destRel, data);
            }

            var cutPers = TryCutRelFile(persistent, sourceRel, destRel, data);
            if (!string.Equals(writeRoot, docs, System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(writeRoot, persistent, System.StringComparison.OrdinalIgnoreCase))
            {
                TryCutRelFile(writeRoot, sourceRel, destRel, data);
            }

            var docsDst = CombineRootRel(docs, destRel);
            var persDst = CombineRootRel(persistent, destRel);
            var destLanded = DestFileOk(docsDst) || DestFileOk(persDst) || DestFileOk(result.dst);

            if (DestFileOk(docsDst))
            {
                result.dst = docsDst;
                result.src = docsSrc;
            }
            else if (DestFileOk(persDst))
            {
                result.dst = persDst;
                result.src = CombineRootRel(persistent, sourceRel);
            }

            result.ok = cutDocs || cutPers || destLanded;
            Debug.Log(
                $"[3OS] insert cut docs={cutDocs} pers={cutPers} destOk={destLanded} from={loadedFrom} → {result.dst}");

            // Only scrub source AFTER a box destination exists — never delete the only copy.
            if (destLanded || DestFileOk(docsDst) || DestFileOk(persDst))
            {
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    TryDeleteFile(CombineRootRel(docs, sourceRel));
                    TryDeleteFile(CombineRootRel(persistent, sourceRel));
                    // MediaStore: nested relative under Documents/DemoFolder.
                    var mediaDir = "Documents/" + DemoFolderName + "/";
                    var slash = sourceRel.LastIndexOf('/');
                    if (slash > 0)
                    {
                        mediaDir += sourceRel.Substring(0, slash + 1);
                        ThreeOSAndroidStorage.MediaStoreDeleteByRelativeName(mediaDir,
                            sourceRel.Substring(slash + 1));
                    }
                    else
                    {
                        ThreeOSAndroidStorage.MediaStoreDeleteByRelativeName(mediaDir, sourceRel);
                    }

                    if (!FileLooksPresent(CombineRootRel(docs, sourceRel)) &&
                        !FileLooksPresent(CombineRootRel(persistent, sourceRel)))
                    {
                        break;
                    }

                    yield return null;
                }

                if (FileLooksPresent(CombineRootRel(docs, sourceRel)) &&
                    !ThreeOSAndroidStorage.IsExternalStorageManager())
                {
                    _waitingForAllFilesAccess = true;
                    _status = "grant All files access, then drop again";
                }
            }
            else
            {
                Debug.LogError("[3OS] insert: destination never landed — left source intact");
                result.ok = false;
            }

            done?.Invoke(result);
        }

        private IEnumerator CommitInsertFolderToDisk(string sourceRel, string destRel, string docs,
            string persistent, string writeRoot, InsertFsResult result, System.Action<InsertFsResult> done)
        {
            var docsSrc = CombineRootRel(docs, sourceRel);
            var needsDocumentsCut = DirectoryLooksPresent(docsSrc) ||
                                    string.Equals(writeRoot, docs, System.StringComparison.OrdinalIgnoreCase);

#if UNITY_ANDROID && !UNITY_EDITOR
            if (needsDocumentsCut && !ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                Debug.LogWarning("[3OS] deferring folder Documents cut until all-files access");
                yield return EnsureAllFilesAccessForCut("grant All files access, then drop again");
                if (!ThreeOSAndroidStorage.IsExternalStorageManager())
                {
                    result.ok = false;
                    _status = "grant All files access, then drop again";
                    done?.Invoke(result);
                    yield break;
                }
            }
#else
            yield return null;
#endif

            var cutDocs = false;
            if (ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                cutDocs = TryCutRelFolder(docs, sourceRel, destRel);
            }

            var cutPers = TryCutRelFolder(persistent, sourceRel, destRel);
            if (!string.Equals(writeRoot, docs, System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(writeRoot, persistent, System.StringComparison.OrdinalIgnoreCase))
            {
                TryCutRelFolder(writeRoot, sourceRel, destRel);
            }

            var docsDst = CombineRootRel(docs, destRel);
            var persDst = CombineRootRel(persistent, destRel);
            var destLanded = DirectoryLooksPresent(docsDst) || DirectoryLooksPresent(persDst) ||
                             DirectoryLooksPresent(result.dst);

            if (DirectoryLooksPresent(docsDst))
            {
                result.dst = docsDst;
                result.src = docsSrc;
            }
            else if (DirectoryLooksPresent(persDst))
            {
                result.dst = persDst;
                result.src = CombineRootRel(persistent, sourceRel);
            }

            result.ok = cutDocs || cutPers || destLanded;
            Debug.Log(
                $"[3OS] folder insert cut docs={cutDocs} pers={cutPers} destOk={destLanded} {sourceRel} → {result.dst}");

            if (!result.ok)
            {
                Debug.LogError("[3OS] folder insert: destination never landed — left source intact");
            }

            done?.Invoke(result);
        }

        private static IEnumerator AnimateScaleBump(Transform t, Vector3 baseScale, float peak,
            float halfDuration)
        {
            if (t == null || halfDuration <= 1e-4f)
            {
                yield break;
            }

            float e = 0f;
            while (e < halfDuration)
            {
                if (t == null)
                {
                    yield break;
                }

                e += Time.deltaTime;
                var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / halfDuration));
                t.localScale = baseScale * Mathf.Lerp(1f, peak, u);
                yield return null;
            }

            e = 0f;
            while (e < halfDuration)
            {
                if (t == null)
                {
                    yield break;
                }

                e += Time.deltaTime;
                var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / halfDuration));
                t.localScale = baseScale * Mathf.Lerp(peak, 1f, u);
                yield return null;
            }

            if (t != null)
            {
                t.localScale = baseScale;
            }
        }

        /// <summary>True when a written insert destination is present with non-zero size.</summary>
        private static bool DestFileOk(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (FileLooksPresent(path))
            {
                try
                {
                    return new FileInfo(path).Length > 0;
                }
                catch
                {
                    return true;
                }
            }

            return false;
        }

        private bool RootLevelCopyExists(string objName)
        {
            foreach (var root in CandidateDemoRoots())
            {
                if (FileLooksPresent(Path.Combine(root, objName)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Move <c>{root}/{sourceRel}</c> → <c>{root}/{destRel}</c> (box-into-box, nested OK).
        /// </summary>
        private bool TryCutRelFolder(string root, string sourceRel, string destRel)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(sourceRel) ||
                string.IsNullOrEmpty(destRel))
            {
                return false;
            }

            var src = CombineRootRel(root, sourceRel);
            var dst = CombineRootRel(root, destRel);
            if (!DirectoryLooksPresent(src))
            {
                return DirectoryLooksPresent(dst);
            }

            if (DirectoryLooksPresent(dst))
            {
                return true;
            }

            try
            {
                var parent = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] mkdir for folder cut {root}: {ex.Message}");
                return false;
            }

            try
            {
                Directory.Move(src, dst);
                if (DirectoryLooksPresent(dst))
                {
                    Debug.Log($"[3OS] folder Move OK {src} → {dst}");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] folder Move {src}: {ex.Message}");
            }

            try
            {
                CopyDirectoryRecursive(src, dst);
                if (!DirectoryLooksPresent(dst))
                {
                    return false;
                }

                try
                {
                    Directory.Delete(src, true);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[3OS] folder delete after copy {src}: {ex.Message}");
                }

                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] folder copy {src}: {ex.Message}");
                return false;
            }
        }

        private static void CopyDirectoryRecursive(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
            {
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(dst, name), true);
            }

            foreach (var dir in Directory.GetDirectories(src))
            {
                var name = Path.GetFileName(dir);
                CopyDirectoryRecursive(dir, Path.Combine(dst, name));
            }
        }

        /// <summary>
        /// Move <c>{root}/{sourceRel}</c> → <c>{root}/{destRel}</c>.
        /// Order is write/move destination first, delete source second. Never deletes the
        /// destination after a successful write (Exists can lie on Quest and caused data loss).
        /// </summary>
        private bool TryCutRelFile(string root, string sourceRel, string destRel, byte[] data)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(sourceRel) ||
                string.IsNullOrEmpty(destRel))
            {
                return false;
            }

            var src = CombineRootRel(root, sourceRel);
            var dst = CombineRootRel(root, destRel);
            var leaf = Path.GetFileName(sourceRel.Replace('\\', '/'));
            var isDocumentsRoot = string.Equals(root, PreferredDocumentsDemoPath(),
                System.StringComparison.OrdinalIgnoreCase);

            try
            {
                var parent = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] mkdir {root}: {ex.Message}");
                return false;
            }

            var hasSrc = FileLooksPresent(src);

            // 1) Atomic same-volume move.
            if (hasSrc)
            {
                if (ThreeOSAndroidStorage.JavaNioMove(src, dst) ||
                    ThreeOSAndroidStorage.JavaRename(src, dst))
                {
                    TryDeleteFile(src); // belt-and-suspenders if move left a stub
                    return DestFileOk(dst);
                }

                try
                {
                    File.Move(src, dst);
                    if (DestFileOk(dst))
                    {
                        TryDeleteFile(src);
                        Debug.Log($"[3OS] mono Move OK {src} → {dst}");
                        return true;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[3OS] Move {src}: {ex.Message}");
                }
            }

            // 2) Copy bytes to destination FIRST, then delete source.
            byte[] bytes = data;
            if ((bytes == null || bytes.Length == 0) && hasSrc)
            {
                try
                {
                    bytes = File.ReadAllBytes(src);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[3OS] read for cut {src}: {ex.Message}");
                }
            }

            if (bytes == null || bytes.Length == 0)
            {
                // Documents with invisible MTP source: do not scrub — that deletes the only copy.
                return false;
            }

            try
            {
                File.WriteAllBytes(dst, bytes);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] write dest {dst}: {ex.Message}");
                return false;
            }

            if (!DestFileOk(dst))
            {
                Debug.LogWarning($"[3OS] dest write not visible yet {dst} — keeping source");
                // Do not delete source; dest may still appear over MTP shortly.
                return FileLooksPresent(dst);
            }

            // Destination confirmed — now remove source (cut).
            TryDeleteFile(src);
            if (isDocumentsRoot)
            {
                ThreeOSAndroidStorage.MediaStoreDelete(src);
                var mediaDir = "Documents/" + DemoFolderName + "/";
                var slash = sourceRel.LastIndexOf('/');
                if (slash > 0)
                {
                    mediaDir += sourceRel.Substring(0, slash + 1);
                }

                ThreeOSAndroidStorage.MediaStoreDeleteByRelativeName(mediaDir, leaf);
            }

            return true;
        }

        /// <summary>Delete root-level object via path + MediaStore (Documents only by relative name).</summary>
        private void ScrubRootObject(string root, string objName)
        {
            var path = Path.Combine(root, objName);
            TryDeleteFile(path);
            ThreeOSAndroidStorage.MediaStoreDelete(path);
            if (string.Equals(root, PreferredDocumentsDemoPath(), System.StringComparison.OrdinalIgnoreCase))
            {
                ThreeOSAndroidStorage.MediaStoreDeleteByRelativeName(
                    "Documents/" + DemoFolderName + "/", objName);
            }
        }

        private IEnumerable<string> CandidateDemoRoots() =>
            CandidateDemoRootsFor(DemoFolderName);

        private IEnumerable<string> CandidateDemoRootsFor(string folderLeaf)
        {
            if (string.IsNullOrEmpty(folderLeaf))
            {
                yield break;
            }

            // Documents first (MTP staging), then persistent writable mirror, then primary root.
#if UNITY_ANDROID && !UNITY_EDITOR
            yield return "/storage/emulated/0/Documents/" + folderLeaf;
#else
            yield return Path.Combine(System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.MyDocuments), folderLeaf);
#endif
            yield return Path.Combine(Application.persistentDataPath, folderLeaf);

            if (string.Equals(folderLeaf, DemoFolderName, System.StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(_demoRoot))
            {
                yield return _demoRoot;
            }

            if (string.Equals(folderLeaf, DemoFolderName2, System.StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(_demoRoot2))
            {
                yield return _demoRoot2;
            }
        }

        /// <summary>
        /// Delete <c>{root}/{objName}</c> for every known demo root. Does not touch
        /// <c>{root}/{box}/…</c> inserts.
        /// </summary>
        private void DeleteAllRootLevelCopies(string objName)
        {
            foreach (var root in CandidateDemoRoots())
            {
                ScrubRootObject(root, objName);
            }
        }

        private static bool TryDeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            if (!ThreeOSAndroidStorage.Exists(path) && !File.Exists(path))
            {
                return true;
            }

            if (ThreeOSAndroidStorage.JavaDelete(path))
            {
                return true;
            }

            if (ThreeOSAndroidStorage.MediaStoreDelete(path))
            {
                return true;
            }

            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                if (!ThreeOSAndroidStorage.Exists(path) && !File.Exists(path))
                {
                    Debug.Log($"[3OS] deleted root copy {path}");
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] delete {path}: {ex.Message}");
            }

            try
            {
                File.WriteAllBytes(path, System.Array.Empty<byte>());
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                ThreeOSAndroidStorage.JavaDelete(path);
                var gone = !ThreeOSAndroidStorage.Exists(path) && !File.Exists(path);
                Debug.Log(gone
                    ? $"[3OS] deleted root copy (truncate) {path}"
                    : $"[3OS] still present after delete attempts {path}");
                return gone;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] truncate-delete {path}: {ex.Message}");
                return !ThreeOSAndroidStorage.Exists(path) && !File.Exists(path);
            }
        }

        private static string ShortPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "?";
            }

            var marker = DemoFolderName + Path.DirectorySeparatorChar;
            var idx = path.Replace('/', Path.DirectorySeparatorChar)
                .IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? path.Substring(idx) : Path.GetFileName(path);
        }

        private static ThreeOSPose AimPose(InteropInputFrame frame)
        {
            if ((frame.trackingFlags & (uint)TrackingFlags.RightAim) != 0)
            {
                return frame.rightAim;
            }

            if ((frame.trackingFlags & (uint)TrackingFlags.LeftAim) != 0)
            {
                return frame.leftAim;
            }

            return frame.head;
        }

        /// <summary>
        /// Prefer Phase 0.5 point-cursor probe for <paramref name="hand"/> (0=left, 1=right, -1=any);
        /// ray dir uses head forward when that cursor is active.
        /// </summary>
        private Pose SelectAimUnity(int hand, out Vector3 rayDir)
        {
            var aim = _input != null
                ? _input.AimPoseForHand(hand)
                : AimPose(_input != null ? _input.LastFrame : default);
            var pose = InteropStructs.ToUnityPose(aim);
            var cursorActive = false;
            if (_input != null)
            {
                if (hand == 0)
                {
                    cursorActive = _input.LastLeftCursor.active != 0;
                }
                else if (hand == 1)
                {
                    cursorActive = _input.LastRightCursor.active != 0;
                }
                else
                {
                    cursorActive = _input.LastLeftCursor.active != 0 ||
                                   _input.LastRightCursor.active != 0;
                }
            }

            if (cursorActive && Camera.main != null)
            {
                rayDir = Camera.main.transform.forward;
            }
            else
            {
                rayDir = pose.rotation * Vector3.forward;
            }

            return pose;
        }

        private Pose SelectAimUnity(out Vector3 rayDir) => SelectAimUnity(-1, out rayDir);

        private bool TryBeginCloseOnXHover()
        {
            // Portal X is handled per-hand in HandleTriggerSelectAndDrag.
            if (_boxAnimBusy || _xHoverBoxId == 0 || !_expandedBoxes.ContainsKey(_xHoverBoxId))
            {
                return false;
            }

            StartCoroutine(CloseBoxSequence(_xHoverBoxId));
            return true;
        }

        private bool TryDismissHoveredPortal(int hand)
        {
            if (hand < 0 || hand > 1)
            {
                return false;
            }

            var pid = _xHoverPortalHand[hand];
            if (pid == 0)
            {
                return false;
            }

            if (_ghostDragging && _selectHand == hand)
            {
                ReleaseDragFromTrigger();
            }

            if (!_bridge.DismissVoodooPortal(pid))
            {
                return false;
            }

            DestroyPortalDismissUi(pid);
            _xHoverPortalHand[hand] = 0;
            if (_xHoverPortalHand[1 - hand] == pid)
            {
                _xHoverPortalHand[1 - hand] = 0;
            }

            _status = hand == 0 ? "voodoo portal dismissed (L)" : "voodoo portal dismissed (R)";
            return true;
        }

        private void UpdatePortalDismissAffordances()
        {
            _xHoverPortalHand[0] = 0;
            _xHoverPortalHand[1] = 0;
            if (_bridge == null || !_bridge.IsLoaded || _bridge.NativeAbiVersion < 6 || _input == null)
            {
                ClearPortalDismissUi();
                return;
            }

            var n = (int)_bridge.VoodooPortalCount;
            var live = new HashSet<ulong>();
            var enterRadius = DismissXDiameterM * 0.5f;
            var exitRadius = enterRadius * 2.25f;

            var haveL = _input.LastLeftCursor.active != 0;
            var haveR = _input.LastRightCursor.active != 0;
            var leftPos = haveL
                ? InteropStructs.OpenXrToUnityPosition(_input.LastLeftCursor.position.ToVector3())
                : Vector3.zero;
            var rightPos = haveR
                ? InteropStructs.OpenXrToUnityPosition(_input.LastRightCursor.position.ToVector3())
                : Vector3.zero;
            if (!haveL && !haveR)
            {
                leftPos = SelectAimUnity(out _).position;
                haveL = true;
            }

            for (var i = 0; i < n; i++)
            {
                if (!_bridge.TryGetVoodooPortal((uint)i, out var portal) || portal.nearBb.open == 0)
                {
                    continue;
                }

                live.Add(portal.id);
                if (!_portalDismiss.TryGetValue(portal.id, out var ui) || ui.XRoot == null)
                {
                    var xPos = PortalCloseButtonUnity(portal.nearBb, portal.creatingHand);
                    ui = new PortalDismissUi
                    {
                        PortalId = portal.id,
                        XRoot = CreateDismissX(xPos),
                    };
                    if (ui.XRoot != null)
                    {
                        ui.XRoot.name = $"3OS_PortalDismissX_{portal.id}";
                        var visual = ui.XRoot.transform.Find("XVisual");
                        ui.XVisual = visual != null ? visual : ui.XRoot.transform;
                    }

                    _portalDismiss[portal.id] = ui;
                }

                var pos = PortalCloseButtonUnity(portal.nearBb, portal.creatingHand);
                ui.XRoot.transform.position = pos;
                var cam = Camera.main;
                if (cam != null)
                {
                    ui.XRoot.transform.rotation =
                        Quaternion.LookRotation((cam.transform.position - pos).normalized, Vector3.up);
                }

                var distL = haveL ? Vector3.Distance(leftPos, pos) : float.MaxValue;
                var distR = haveR ? Vector3.Distance(rightPos, pos) : float.MaxValue;
                var dist = Mathf.Min(distL, distR);
                if (ui.Hovered)
                {
                    ui.Hovered = dist <= exitRadius;
                }
                else
                {
                    ui.Hovered = dist <= enterRadius;
                }

                if (ui.Hovered)
                {
                    var lim = ui.Hovered ? exitRadius : enterRadius;
                    if (distL <= lim)
                    {
                        _xHoverPortalHand[0] = portal.id;
                    }

                    if (distR <= lim)
                    {
                        _xHoverPortalHand[1] = portal.id;
                    }
                }

                if (ui.XVisual != null)
                {
                    ui.XVisual.localScale = ui.Hovered ? Vector3.one * 2f : Vector3.one;
                }
            }

            // Tear down X for portals that were dismissed / no longer reported.
            var stale = new List<ulong>();
            foreach (var kv in _portalDismiss)
            {
                if (!live.Contains(kv.Key))
                {
                    stale.Add(kv.Key);
                }
            }

            foreach (var id in stale)
            {
                DestroyPortalDismissUi(id);
            }
        }

        /// <summary>
        /// Near-BB X-close at mid-height on the front face: left if R-created, right if
        /// L-created (OpenXR local; front = +Z toward user). Mid-Y so expand stays reachable.
        /// </summary>
        private static Vector3 PortalCloseButtonUnity(in InteropVoodooBoundingBox bb,
            uint creatingHand)
        {
            var pad = DismissXDiameterM * 0.35f;
            var hx = bb.extents.x * 0.5f + pad;
            var hz = bb.extents.z * 0.5f + pad;
            var y = bb.pose.orientation.y;
            var w = bb.pose.orientation.w;
            // R-created → mid-front-left (−X); L-created → mid-front-right (+X).
            var lx = creatingHand == 0u ? hx : -hx;
            var lz = hz;
            var wx = lx * (1f - 2f * y * y) + lz * (2f * w * y);
            var wz = lz * (1f - 2f * y * y) - lx * (2f * w * y);
            var openXr = new Vector3(
                bb.pose.position.x + wx,
                bb.pose.position.y,
                bb.pose.position.z + wz);
            return InteropStructs.OpenXrToUnityPosition(openXr);
        }

        private void DestroyPortalDismissUi(ulong portalId)
        {
            if (!_portalDismiss.TryGetValue(portalId, out var ui))
            {
                return;
            }

            if (ui.XRoot != null)
            {
                Destroy(ui.XRoot);
            }

            _portalDismiss.Remove(portalId);
        }

        private void ClearPortalDismissUi()
        {
            foreach (var kv in _portalDismiss)
            {
                if (kv.Value.XRoot != null)
                {
                    Destroy(kv.Value.XRoot);
                }
            }

            _portalDismiss.Clear();
            _xHoverPortalHand[0] = 0;
            _xHoverPortalHand[1] = 0;
        }

        private void UpdateExpandedDismissAffordances()
        {
            _xHoverBoxId = 0;
            if (_expandedBoxes.Count == 0 || _input == null)
            {
                return;
            }

            // Test BOTH point cursors so left hand can hover/close X (not right-only).
            var enterRadius = DismissXDiameterM * 0.5f;
            var exitRadius = enterRadius * 2.25f;
            var cursors = new Vector3[2];
            var cursorCount = 0;
            if (_input.LastLeftCursor.active != 0)
            {
                cursors[cursorCount++] =
                    InteropStructs.OpenXrToUnityPosition(_input.LastLeftCursor.position.ToVector3());
            }

            if (_input.LastRightCursor.active != 0)
            {
                cursors[cursorCount++] =
                    InteropStructs.OpenXrToUnityPosition(_input.LastRightCursor.position.ToVector3());
            }

            if (cursorCount == 0)
            {
                var aimUnity = SelectAimUnity(out _);
                cursors[cursorCount++] = aimUnity.position;
            }

            foreach (var kv in _expandedBoxes)
            {
                var ui = kv.Value;
                if (ui.XRoot == null || !_views.TryGetValue(ui.BoxId, out var boxView) ||
                    boxView.Root == null)
                {
                    continue;
                }

                var up = boxView.Root.transform.up;
                var xPos = boxView.Root.transform.position + up * BoxDismissXOffsetM;
                ui.XRoot.transform.position = xPos;
                var cam = Camera.main;
                if (cam != null)
                {
                    ui.XRoot.transform.rotation =
                        Quaternion.LookRotation((cam.transform.position - xPos).normalized, Vector3.up);
                }

                var dist = float.MaxValue;
                for (var i = 0; i < cursorCount; i++)
                {
                    dist = Mathf.Min(dist, Vector3.Distance(cursors[i], xPos));
                }

                if (ui.Hovered)
                {
                    ui.Hovered = dist <= exitRadius;
                }
                else
                {
                    ui.Hovered = dist <= enterRadius;
                }

                if (ui.XVisual != null)
                {
                    ui.XVisual.localScale = ui.Hovered ? Vector3.one * 2f : Vector3.one;
                }

                if (ui.Hovered)
                {
                    _xHoverBoxId = ui.BoxId;
                }
            }
        }

        private IEnumerator OpenBoxSequence(ulong boxId)
        {
            if (_boxAnimBusy || _expandedBoxes.ContainsKey(boxId))
            {
                yield break;
            }

            if (!TryFindItem(boxId, out var boxItem) || boxItem.kind != 1)
            {
                _status = "open failed: not a box";
                yield break;
            }

            if (!_views.TryGetValue(boxId, out var boxView) || boxView.Root == null)
            {
                _status = "open failed: missing box view";
                yield break;
            }

            _boxAnimBusy = true;
            var boxName = boxItem.Name;
            var boxRel = ResolveBoxRelativePath(boxId);
            try
            {
                if (!EnsureBoxChildren(boxId, boxRel, out var childIds))
                {
                    _status = $"open failed: {_bridge.LastError}";
                    yield break;
                }

                if (!_bridge.ExpandBox(boxId))
                {
                    _status = $"expand failed: {_bridge.LastError}";
                    yield break;
                }

                childIds = CollectChildIds(boxId);
                var targets = new Dictionary<ulong, Pose>();
                var centroid = Vector3.zero;
                var n = 0;
                foreach (var childId in childIds)
                {
                    _animLockedIds.Add(childId);
                    if (!TryFindItem(childId, out var childItem) || (childItem.flags & 1u) == 0)
                    {
                        continue;
                    }

                    var unity = InteropStructs.ToUnityPose(childItem.pose);
                    targets[childId] = unity;
                    centroid += unity.position;
                    n++;
                }

                var xPos = boxView.Root.transform.position +
                           boxView.Root.transform.up * BoxDismissXOffsetM;
                if (n > 0)
                {
                    centroid /= n;
                }
                else
                {
                    centroid = xPos;
                }

                // Seed at final layout poses, parent under a unit-scale pivot, then snap the
                // pivot to X at a tiny scale and tween out. Never parent under scale=0 (NaN locals).
                const float startScale = 0.001f;
                foreach (var childId in childIds)
                {
                    if (!_views.TryGetValue(childId, out var childView) || childView.Root == null)
                    {
                        if (TryFindItem(childId, out var item))
                        {
                            _views[childId] = CreateView(item);
                            childView = _views[childId];
                        }
                    }

                    if (childView?.Root == null || !targets.TryGetValue(childId, out var targetPose))
                    {
                        continue;
                    }

                    childView.Root.SetActive(true);
                    childView.Root.transform.SetParent(null, true);
                    childView.Root.transform.localScale = Vector3.one;
                    childView.Root.transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
                }

                var animNode = new GameObject($"3OS_ExpandAnim_{boxId}");
                animNode.transform.SetPositionAndRotation(centroid, boxView.Root.transform.rotation);
                animNode.transform.localScale = Vector3.one;
                foreach (var childId in childIds)
                {
                    if (_views.TryGetValue(childId, out var childView) && childView.Root != null &&
                        targets.ContainsKey(childId))
                    {
                        childView.Root.transform.SetParent(animNode.transform, true);
                        childView.Root.transform.localScale = Vector3.one;
                    }
                }

                animNode.transform.position = xPos;
                animNode.transform.localScale = Vector3.one * startScale;

                var duration = Mathf.Max(0.05f, BoxExpandAnimSeconds);
                var elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                    animNode.transform.position = Vector3.Lerp(xPos, centroid, u);
                    animNode.transform.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, u);
                    yield return null;
                }

                animNode.transform.position = centroid;
                animNode.transform.localScale = Vector3.one;
                foreach (var childId in childIds)
                {
                    if (!_views.TryGetValue(childId, out var childView) || childView.Root == null)
                    {
                        continue;
                    }

                    childView.Root.transform.SetParent(null, true);
                    childView.Root.transform.localScale = Vector3.one;
                    if (targets.TryGetValue(childId, out var targetPose))
                    {
                        childView.Root.transform.SetPositionAndRotation(targetPose.position,
                            targetPose.rotation);
                    }
                }

                Destroy(animNode);
                foreach (var childId in childIds)
                {
                    _animLockedIds.Remove(childId);
                }

                _bridge.SetBoxOpen(boxId, true);
                var ui = new ExpandedBoxUi { BoxId = boxId };
                ui.ChildIds.AddRange(childIds);
                ui.XRoot = CreateDismissX(xPos);
                ui.XVisual = ui.XRoot.transform.Find("XVisual");
                _expandedBoxes[boxId] = ui;
                _status = $"opened {boxName} ({childIds.Count})";
                Debug.Log($"[3OS] {_status}");
            }
            finally
            {
                _animLockedIds.Clear();
                _boxAnimBusy = false;
            }
        }

        private IEnumerator CloseBoxSequence(ulong boxId)
        {
            if (_boxAnimBusy || !_expandedBoxes.ContainsKey(boxId))
            {
                yield break;
            }

            _boxAnimBusy = true;
            _xHoverBoxId = 0;
            try
            {
                // Nested opens: animate deepest → shallowest, then this box (not an
                // instant TeardownExpandedDescendants dump).
                yield return CloseExpandedChainDeepestFirst(boxId);
            }
            finally
            {
                _animLockedIds.Clear();
                _boxAnimBusy = false;
            }
        }

        /// <summary>
        /// Folder relative path for a box (e.g. <c>Parent/Nested</c>), walked via parent links.
        /// Nested opens used to pass only <see cref="InteropWorkspaceItem.name"/> and looked in
        /// the wrong directory, so children never spawned.
        /// </summary>
        private string ResolveBoxRelativePath(ulong boxId)
        {
            var parts = new List<string>();
            var guard = 0;
            var id = boxId;
            while (id != 0 && guard++ < 64)
            {
                if (!TryFindItem(id, out var item))
                {
                    break;
                }

                parts.Add(item.Name);
                if (!_bridge.TryGetParentBox(id, out var parent) || parent == 0)
                {
                    break;
                }

                id = parent;
            }

            parts.Reverse();
            return parts.Count > 0 ? string.Join("/", parts) : string.Empty;
        }

        private bool EnsureBoxChildren(ulong boxId, string boxRelPath, out List<ulong> childIds)
        {
            if (string.IsNullOrEmpty(boxRelPath))
            {
                boxRelPath = ResolveBoxRelativePath(boxId);
            }

            CollectBoxFolderEntries(boxRelPath, out var files, out var dirs);

            // Reuse existing workspace children under this box (inserts + prior expands).
            var existingByName = new Dictionary<string, ulong>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var id in CollectChildIds(boxId))
            {
                if (TryFindItem(id, out var item))
                {
                    existingByName[item.Name] = id;
                }
            }

            foreach (var name in files)
            {
                if (existingByName.ContainsKey(name))
                {
                    continue;
                }

                var rel = $"{boxRelPath}/{name}";
                if (!_bridge.StorageAddObject(name, rel, out var id) ||
                    !_bridge.StorageSetParentBox(id, boxId) ||
                    !_bridge.StorageSetInField(id, false))
                {
                    childIds = CollectChildIds(boxId);
                    return false;
                }
            }

            foreach (var name in dirs)
            {
                if (existingByName.ContainsKey(name))
                {
                    continue;
                }

                var rel = $"{boxRelPath}/{name}";
                uint nestedCount = 0;
                foreach (var root in CandidateDemoRoots())
                {
                    if (string.IsNullOrEmpty(root))
                    {
                        continue;
                    }

                    var nestedPath = Path.Combine(root, boxRelPath.Replace('/', Path.DirectorySeparatorChar), name);
                    var c = ThreeOSAndroidStorage.CountFilesInDirectory(nestedPath);
                    // Nested boxes often contain only folders — count dirs too.
                    c += (uint)ThreeOSAndroidStorage.ListDirectoryNames(nestedPath).Count;
                    if (c > nestedCount)
                    {
                        nestedCount = c;
                    }
                }

                if (!_bridge.StorageAddBox(name, rel, nestedCount, out var id) ||
                    !_bridge.StorageSetParentBox(id, boxId) ||
                    !_bridge.StorageSetInField(id, false))
                {
                    childIds = CollectChildIds(boxId);
                    return false;
                }
            }

            childIds = CollectChildIds(boxId);
            return true;
        }

        /// <summary>
        /// Close <paramref name="rootBoxId"/> and every expanded descendant, deepest first,
        /// each with the full shrink-to-X animation.
        /// </summary>
        private IEnumerator CloseExpandedChainDeepestFirst(ulong rootBoxId)
        {
            var chain = new List<ulong>();
            foreach (var id in _expandedBoxes.Keys)
            {
                if (id == rootBoxId || IsBoxUnderAncestor(id, rootBoxId))
                {
                    chain.Add(id);
                }
            }

            chain.Sort((a, b) =>
            {
                var da = BoxDepth(a);
                var db = BoxDepth(b);
                var cmp = db.CompareTo(da);
                return cmp != 0 ? cmp : b.CompareTo(a);
            });

            foreach (var id in chain)
            {
                if (_expandedBoxes.ContainsKey(id))
                {
                    yield return AnimateCollapseOneExpandedBox(id);
                }
            }
        }

        /// <summary>
        /// One expanded box: cluster-shrink children into the X, hide, collapse kernel.
        /// Does not recurse — caller orders deepest-first.
        /// </summary>
        private IEnumerator AnimateCollapseOneExpandedBox(ulong boxId)
        {
            if (!_expandedBoxes.TryGetValue(boxId, out var ui))
            {
                yield break;
            }

            var childIds = new List<ulong>(ui.ChildIds);
            Vector3 centroid = Vector3.zero;
            var n = 0;
            foreach (var childId in childIds)
            {
                if (_views.TryGetValue(childId, out var childView) && childView.Root != null &&
                    childView.Root.activeInHierarchy)
                {
                    centroid += childView.Root.transform.position;
                    n++;
                }
            }

            Vector3 xPos;
            if (_views.TryGetValue(boxId, out var boxView) && boxView.Root != null)
            {
                xPos = boxView.Root.transform.position + boxView.Root.transform.up * BoxDismissXOffsetM;
            }
            else
            {
                xPos = ui.XRoot != null ? ui.XRoot.transform.position : centroid;
            }

            if (n > 0)
            {
                centroid /= n;
            }
            else
            {
                centroid = xPos;
            }

            foreach (var childId in childIds)
            {
                _animLockedIds.Add(childId);
            }

            const float endScale = 0.001f;
            var animNode = new GameObject($"3OS_CollapseAnim_{boxId}");
            animNode.transform.SetPositionAndRotation(centroid, Quaternion.identity);
            animNode.transform.localScale = Vector3.one;
            foreach (var childId in childIds)
            {
                if (_views.TryGetValue(childId, out var childView) && childView.Root != null)
                {
                    childView.Root.transform.SetParent(animNode.transform, true);
                    childView.Root.transform.localScale = Vector3.one;
                }
            }

            var duration = Mathf.Max(0.05f, BoxExpandAnimSeconds);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                animNode.transform.position = Vector3.Lerp(centroid, xPos, u);
                animNode.transform.localScale = Vector3.one * Mathf.Lerp(1f, endScale, u);
                yield return null;
            }

            foreach (var childId in childIds)
            {
                if (_views.TryGetValue(childId, out var childView) && childView.Root != null)
                {
                    childView.Root.transform.SetParent(null, true);
                    childView.Root.transform.localScale = Vector3.one;
                    childView.Root.SetActive(false);
                }

                _animLockedIds.Remove(childId);
            }

            Destroy(animNode);
            if (ui.XRoot != null)
            {
                Destroy(ui.XRoot);
            }

            _expandedBoxes.Remove(boxId);
            if (_xHoverBoxId == boxId)
            {
                _xHoverBoxId = 0;
            }

            if (!_bridge.CollapseBox(boxId))
            {
                _status = $"collapse failed: {_bridge.LastError}";
            }
            else
            {
                _bridge.SetBoxOpen(boxId, false);
                _status = "box closed";
            }
        }

        /// <summary>
        /// Instantly tear down expanded UI for boxes under <paramref name="ancestorBoxId"/>
        /// (not the ancestor itself). Used by rebuild/insert paths, not animated close.
        /// </summary>
        private void TeardownExpandedDescendants(ulong ancestorBoxId, bool collapseKernel)
        {
            var toClose = new List<ulong>();
            foreach (var id in _expandedBoxes.Keys)
            {
                if (id != ancestorBoxId && IsBoxUnderAncestor(id, ancestorBoxId))
                {
                    toClose.Add(id);
                }
            }

            // Deepest first so nested X buttons go before their parents.
            toClose.Sort((a, b) =>
            {
                var da = BoxDepth(a);
                var db = BoxDepth(b);
                return db.CompareTo(da);
            });

            foreach (var id in toClose)
            {
                ForceTeardownExpandedBox(id, collapseKernel);
            }
        }

        private bool IsBoxUnderAncestor(ulong boxId, ulong ancestorBoxId)
        {
            var guard = 0;
            var id = boxId;
            while (id != 0 && guard++ < 64)
            {
                if (!_bridge.TryGetParentBox(id, out var parent) || parent == 0)
                {
                    return false;
                }

                if (parent == ancestorBoxId)
                {
                    return true;
                }

                id = parent;
            }

            return false;
        }

        private int BoxDepth(ulong boxId)
        {
            var depth = 0;
            var id = boxId;
            while (id != 0 && depth < 64)
            {
                if (!_bridge.TryGetParentBox(id, out var parent) || parent == 0)
                {
                    break;
                }

                depth++;
                id = parent;
            }

            return depth;
        }

        private void ForceTeardownExpandedBox(ulong boxId, bool collapseKernel)
        {
            if (!_expandedBoxes.TryGetValue(boxId, out var ui))
            {
                return;
            }

            foreach (var childId in ui.ChildIds)
            {
                if (_views.TryGetValue(childId, out var childView) && childView.Root != null)
                {
                    childView.Root.transform.SetParent(null, true);
                    childView.Root.transform.localScale = Vector3.one;
                    childView.Root.SetActive(false);
                }

                _animLockedIds.Remove(childId);
            }

            if (ui.XRoot != null)
            {
                Destroy(ui.XRoot);
            }

            _expandedBoxes.Remove(boxId);
            if (_xHoverBoxId == boxId)
            {
                _xHoverBoxId = 0;
            }

            if (collapseKernel)
            {
                _bridge.CollapseBox(boxId);
                _bridge.SetBoxOpen(boxId, false);
            }
        }

        private List<ulong> CollectChildIds(ulong boxId)
        {
            var childIds = new List<ulong>();
            var count = _bridge.StorageItemCount();
            for (uint i = 0; i < count; i++)
            {
                if (!_bridge.TryGetWorkspaceItem(i, out var item))
                {
                    continue;
                }

                if (_bridge.TryGetParentBox(item.entityId, out var parent) && parent == boxId)
                {
                    childIds.Add(item.entityId);
                }
            }

            return childIds;
        }

        private void CollectBoxFolderEntries(string boxRelPath, out List<string> files, out List<string> dirs)
        {
            var fileSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var dirSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var rel = (boxRelPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            foreach (var root in CandidateDemoRoots())
            {
                if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(rel))
                {
                    continue;
                }

                var folder = Path.Combine(root, rel);
                if (!Directory.Exists(folder) && !ThreeOSAndroidStorage.Exists(folder))
                {
                    continue;
                }

                foreach (var name in ThreeOSAndroidStorage.ListFileNames(folder))
                {
                    fileSet.Add(name);
                }

                foreach (var name in ThreeOSAndroidStorage.ListDirectoryNames(folder))
                {
                    dirSet.Add(name);
                }
            }

            files = new List<string>(fileSet);
            dirs = new List<string>(dirSet);
            files.Sort(System.StringComparer.OrdinalIgnoreCase);
            dirs.Sort(System.StringComparer.OrdinalIgnoreCase);
        }

        private static GameObject CreateDismissX(Vector3 worldPos)
        {
            var root = new GameObject("3OS_BoxDismissX");
            root.transform.position = worldPos;

            var visual = new GameObject("XVisual");
            visual.transform.SetParent(root.transform, false);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Sphere";
            sphere.transform.SetParent(visual.transform, false);
            sphere.transform.localScale = Vector3.one * DismissXDiameterM;
            var sphereCol = sphere.GetComponent<Collider>();
            if (sphereCol != null)
            {
                Destroy(sphereCol);
            }

            var sphereMr = sphere.GetComponent<MeshRenderer>();
            sphereMr.sharedMaterial = CreateTranslucentUnlitMaterial(new Color(0.65f, 0.65f, 0.7f, 0.35f));
            // Draw shell first so opaque X bars remain readable through it.
            sphereMr.sortingOrder = -1;

            CreateXBar(visual.transform, 45f);
            CreateXBar(visual.transform, -45f);
            return root;
        }

        private static void CreateXBar(Transform parent, float zAngle)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "XBar";
            bar.transform.SetParent(parent, false);
            // Scaled with DismissXDiameterM (baseline was 0.02 m → 0.016 / 0.0022 bars).
            var s = DismissXDiameterM / 0.02f;
            bar.transform.localScale = new Vector3(0.016f * s, 0.0022f * s, 0.0022f * s);
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, zAngle);
            var col = bar.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col);
            }

            var mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ??
                                   Shader.Find("Universal Render Pipeline/Unlit"));
            if (mat != null)
            {
                var c = new Color(0.12f, 0.12f, 0.14f, 1f);
                if (mat.HasProperty("_Color"))
                {
                    mat.SetColor("_Color", c);
                }

                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", c);
                }

                bar.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        private static Material CreateTranslucentUnlitMaterial(Color color)
        {
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Transparent")
                         ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            mat.color = color;
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
            }

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }
    }
}
