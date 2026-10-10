using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ThreeOS
{
    /// <summary>
    /// Phase 0.4: at Start, list demo dir and instantiate semi-transparent glyphs + labels,
    /// snap-insert into Box with open/bump/close anim, write-through FS move.
    /// </summary>
    public sealed class ThreeOSStorageController : MonoBehaviour
    {
        public const string DemoFolderName = "3OS_Demo";
        /// <summary>Glyph visual size in meters (matches Blender-authored 0.1 m packs).</summary>
        public const float GlyphSizeM = 0.1f;
        public float PossessDistance = 0.45f;

        private ThreeOSBridge _bridge;
        private ThreeOSInputRouter _input;
        private ThreeOSTopologyController _topology;

        private readonly Dictionary<ulong, GlyphView> _views = new Dictionary<ulong, GlyphView>();
        private LineRenderer _insertArrow;
        private bool _grabWasDown;
        private bool _possessing;
        private ulong _possessedId;
        private bool _insertAnimBusy;
        private string _status = "storage idle";
        private string _demoRoot = string.Empty;
        private ThreeOSPose _layoutHeadPose;
        private bool _waitingForAllFilesAccess;

        public bool DemoActive { get; private set; }
        public string Status => _status;
        public string DemoRoot => _demoRoot;

        private sealed class GlyphView
        {
            public GameObject Root;
            public ThreeOSMovable Movable;
            public MeshRenderer Renderer;
            public BoxCollider Collider;
            public TextMesh NameLabel;
            public TextMesh SecondaryLabel;
            public string GlyphId;
        }

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
            _input = GetComponent<ThreeOSInputRouter>() ?? FindFirstObjectByType<ThreeOSInputRouter>();
            _topology = GetComponent<ThreeOSTopologyController>() ??
                        FindFirstObjectByType<ThreeOSTopologyController>();
            EnsureInsertArrow();
        }

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
            if (string.IsNullOrEmpty(_demoRoot) || !LoadDemoDirectory(_demoRoot))
            {
                Debug.LogError($"[3OS] demo load aborted: {_status}");
                yield break;
            }

            // Hide Phase 0.3 magenta cube as soon as workspace has items (before layout).
            HideLegacyPhase03Cube();

            yield return WaitForHeadPose();
            if (!_bridge.StorageLayoutDemo(_layoutHeadPose))
            {
                Debug.LogWarning($"[3OS] layout warn: {_bridge.LastError} (continuing with default poses)");
            }

            DemoActive = true;
            RebuildViews();
            _status = $"demo {_views.Count} glyphs @ {ShortPath(_demoRoot)}";
            Debug.Log(
                $"[3OS] demo loaded views={_views.Count} kernelItems={_bridge.StorageItemCount()} root={_demoRoot}");

            // Ask before the first drop so cut isn't attempted without delete rights.
            yield return EnsureAllFilesAccessForCut("grant All files access, then drag to insert");
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
            // Prefer real XR head tracking — Camera.main can exist before tracking and sit off to
            // the side, which used to place the whole demo field beside the user.
            var deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_input != null)
                {
                    var frame = _input.LastFrame;
                    if ((frame.trackingFlags & (uint)TrackingFlags.Head) != 0)
                    {
                        _layoutHeadPose = frame.head;
                        Debug.Log("[3OS] layout head from XR tracking");
                        yield break;
                    }
                }

                yield return null;
            }

            var cam = Camera.main;
            if (cam != null)
            {
                _layoutHeadPose =
                    InteropStructs.ToOpenXrPose(new Pose(cam.transform.position, cam.transform.rotation));
                Debug.LogWarning("[3OS] layout head from Camera.main (no XR head flag yet)");
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
            HandleGrab();
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
            foreach (var kv in _views)
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

            // Quest: persistent is the writable root (Documents often listable but not writable).
            // LoadDemoDirectory unions both roots, so MTP Documents folders still appear.
#if UNITY_ANDROID && !UNITY_EDITOR
            _demoRoot = fallback;
#else
            _demoRoot = DemoRootLooksPopulated(docs) ? docs : fallback;
#endif

            try
            {
                Directory.CreateDirectory(_demoRoot);
            }
            catch (System.Exception ex)
            {
                _status = $"cannot create demo dir: {ex.Message}";
                Debug.LogError($"[3OS] {_status}");
                _demoRoot = string.Empty;
                yield break;
            }

            // Folders alone are valid demo content (empty Boxes). Use Java-capable listing —
            // .NET Directory.GetFiles often returns empty on Quest Documents.
            var persistentPopulated = DemoRootLooksPopulated(fallback);
            var docsPopulated = DemoRootLooksPopulated(docs);

            // Always ensure a writable simple.glb under persistent when missing. MTP files on
            // Documents are often invisible to File.list / MediaStore until all-files access.
            if (!FileLooksPresent(Path.Combine(fallback, "simple.glb")))
            {
                yield return SeedRootObject(fallback, "simple.glb");
            }

            persistentPopulated = DemoRootLooksPopulated(fallback) ||
                                  FileLooksPresent(Path.Combine(fallback, "simple.glb"));

            if (!persistentPopulated && docsPopulated)
            {
                // User tree lives only under Documents — list from there.
                _demoRoot = docs;
            }

            if (!DemoRootLooksPopulated(_demoRoot) && !docsPopulated && !persistentPopulated)
            {
                _status = "no demo content — add files/folders under 3OS_Demo";
                Debug.LogError($"[3OS] {_status} (persistent={fallback} docs={docs})");
                _demoRoot = string.Empty;
                yield break;
            }

            // Additive sync only (never delete). On Quest, pull Documents → persistent so
            // MTP-staged simple.glb becomes visible/writable under the app files root.
#if UNITY_ANDROID && !UNITY_EDITOR
            TryMirrorDemoTree(docs, fallback);
            TryMirrorDemoTree(fallback, docs);
            // Dirs visible but zero root files → likely scoped storage hiding MTP objects.
            if (DemoRootLooksPopulated(docs) &&
                ThreeOSAndroidStorage.ListFileNames(docs).Count == 0 &&
                !ThreeOSAndroidStorage.TryProbeFileName(docs, "simple.glb") &&
                !ThreeOSAndroidStorage.IsExternalStorageManager())
            {
                Debug.LogWarning(
                    "[3OS] Documents has folders but no listable files — requesting all-files access");
                ThreeOSAndroidStorage.RequestManageAllFilesAccess();
            }
#else
            TryMirrorDemoTree(_demoRoot, _demoRoot == docs ? fallback : docs);
#endif

            Debug.Log(
                $"[3OS] demo root = {_demoRoot} (persistentPopulated={persistentPopulated} docsPopulated={docsPopulated} " +
                $"persistentGlb={FileLooksPresent(Path.Combine(fallback, "simple.glb"))} " +
                $"docsGlb={FileLooksPresent(Path.Combine(docs, "simple.glb"))})");
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
        private bool LoadDemoDirectory(string root)
        {
            _bridge.StorageClear();
            if (string.IsNullOrEmpty(root) ||
                (!Directory.Exists(root) && !ThreeOSAndroidStorage.Exists(root)))
            {
                _status = $"demo dir missing: {root}";
                Debug.LogError($"[3OS] {_status}");
                return false;
            }

            // Union listings across demo roots so Quest Documents + persistent both contribute.
            var fileNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var dirNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var candidateRoot in CandidateDemoRoots())
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

                // Directory listing often omits MTP .glb; probe known/seed object names.
                foreach (var probe in new[] { "simple.glb", "simple.gltf" })
                {
                    if (ThreeOSAndroidStorage.TryProbeFileName(candidateRoot, probe))
                    {
                        fileNames.Add(probe);
                    }
                }
            }

            // Prefer primary root listing order: files then dirs (sorted for stable matrix).
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
                    if (!_bridge.StorageAddObject(name, name, out _))
                    {
                        _status = $"add object failed: {_bridge.LastError}";
                        Debug.LogError($"[3OS] {_status}");
                        return false;
                    }

                    added++;
                    Debug.Log($"[3OS] listed object {name}");
                }

                foreach (var name in dirs)
                {
                    var dirPath = Path.Combine(root, name);
                    var childCount = CountFolderContents(dirPath, name, root);

                    if (!_bridge.StorageAddBox(name, name, childCount, out _))
                    {
                        _status = $"add box failed: {_bridge.LastError}";
                        Debug.LogError($"[3OS] {_status}");
                        return false;
                    }

                    added++;
                    boxes++;
                    Debug.Log($"[3OS] listed box {name} ({childCount} children)");
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
            // Layout (cubic matrix in front of HMD) runs after head pose is available.
            return true;
        }

        /// <summary>
        /// Tally files inside a box folder. Uses Java listing on Android when .NET returns 0
        /// (common for MTP/Documents), and also checks the alternate demo root.
        /// </summary>
        private uint CountFolderContents(string dirPath, string folderName, string primaryRoot)
        {
            var count = ThreeOSAndroidStorage.CountFilesInDirectory(dirPath);
            if (count > 0)
            {
                return count;
            }

            foreach (var altRoot in CandidateDemoRoots())
            {
                if (string.Equals(altRoot, primaryRoot, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var alt = Path.Combine(altRoot, folderName);
                var altCount = ThreeOSAndroidStorage.CountFilesInDirectory(alt);
                if (altCount > count)
                {
                    count = altCount;
                }
            }

            return count;
        }

        private void RebuildViews()
        {
            foreach (var kv in _views)
            {
                if (kv.Value.Root != null)
                {
                    Destroy(kv.Value.Root);
                }
            }

            _views.Clear();
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
            mr.sharedMaterial = MakeGlyphMaterial(item);

            var unityPose = InteropStructs.ToUnityPose(item.pose);
            go.transform.SetPositionAndRotation(unityPose.position, unityPose.rotation);

            // Labels sit below the glyph in local space (half-size + gap).
            var labelY = -(GlyphSizeM * 0.5f + 0.025f);
            var nameLabel = CreateLabel(go.transform, item.Name, new Vector3(0f, labelY, 0f), 0.008f);
            TextMesh secondary = null;
            if (item.kind == 1)
            {
                secondary = CreateLabel(go.transform, item.SecondaryLabel,
                    new Vector3(0f, labelY - 0.028f, 0f), 0.006f);
            }

            return new GlyphView
            {
                Root = go,
                Movable = movable,
                Renderer = mr,
                Collider = col,
                NameLabel = nameLabel,
                SecondaryLabel = secondary,
                GlyphId = item.GlyphId
            };
        }

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

        private static Material MakeGlyphMaterial(InteropWorkspaceItem item)
        {
            // Prefer distinct authored colors; ignore washed-out white tint from packs.
            Color c;
            if (item.GlyphId == "box_closed" || item.GlyphId == "box_open" || item.kind == 1)
            {
                // Brown cardboard
                c = new Color(0.55f, 0.36f, 0.18f, 0.55f);
            }
            else if (item.GlyphId == "gltf_mark")
            {
                // Teal glTF-style mark
                c = new Color(0.10f, 0.72f, 0.78f, 0.55f);
            }
            else
            {
                c = new Color(
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

            // Prefer simple unlit shaders — URP/Lit often shows magenta on Quest if variants strip.
            var shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Hidden/InternalErrorShader");
            }

            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", c);
            }

            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", c);
            }

            mat.color = c;

            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3000;
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            }

            return mat;
        }

        private static TextMesh CreateLabel(Transform parent, string text, Vector3 localPos, float charSize)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = string.IsNullOrEmpty(text) ? "?" : text;
            tm.characterSize = charSize;
            tm.fontSize = 64;
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
                if (!inField)
                {
                    if (_views.TryGetValue(item.entityId, out var hide))
                    {
                        if (hide.Root != null)
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
                var kineticDriven = IsKinematicallyDriven(item.entityId);
                if (!kineticDriven)
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
                    if (view.Renderer != null)
                    {
                        view.Renderer.sharedMaterial = MakeGlyphMaterial(item);
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
                if (_views[id].Root != null)
                {
                    Destroy(_views[id].Root);
                }

                _views.Remove(id);
            }
        }

        private void HandleGrab()
        {
            // Do NOT gate grab on _insertAnimBusy — a stuck insert coroutine used to
            // disable possess for the rest of the session after a failed FS move.

            var frame = _input.LastFrame;
            var grab = (frame.buttonFlags & (uint)(ButtonFlags.RightGrab | ButtonFlags.LeftGrab)) != 0;
            var aim = AimPose(frame);
            var aimUnity = InteropStructs.ToUnityPose(aim).position;

            if (grab && !_grabWasDown)
            {
                TryPossessNearest(aimUnity);
            }

            if (!grab && _grabWasDown && _possessing)
            {
                var hadSnap = _bridge.TryGetSnap(out var snap) && snap.pending != 0;
                if (!_bridge.ReleasePossessed())
                {
                    // Commit can fail if a prior FS event is still awaiting ack; keep trying insert.
                    if (hadSnap && _bridge.TryCommitInsert())
                    {
                        _status = "insert committed (retry)";
                    }
                    else
                    {
                        _status = hadSnap
                            ? $"insert failed: {_bridge.LastError}"
                            : $"released (coast): {_bridge.LastError}";
                    }
                }
                else
                {
                    _status = hadSnap ? "insert — waiting FS" : "released — coasting";
                }

                _possessing = false;
                _possessedId = 0;
            }

            _grabWasDown = grab;
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

            float best = PossessDistance;
            ulong bestId = 0;
            ThreeOSPose bestPose = default;
            foreach (var kv in _views)
            {
                if (kv.Value.Root == null || !kv.Value.Root.activeInHierarchy ||
                    kv.Value.Movable == null)
                {
                    continue;
                }

                // Possess both Objects and Boxes (folders). Kernel snap-insert only
                // applies to Objects dropped onto Boxes — dragging a Box just telekinetics it.
                if (!TryFindItem(kv.Key, out var item) || (item.kind != 0 && item.kind != 1))
                {
                    continue;
                }

                var d = Vector3.Distance(aimUnity, kv.Value.Root.transform.position);
                if (d < best)
                {
                    best = d;
                    bestId = kv.Key;
                    bestPose = kv.Value.Movable.CurrentOpenXrPose();
                }
            }

            if (bestId == 0)
            {
                _status = "grab missed";
                return;
            }

            if (_bridge.Possess(bestId, bestPose))
            {
                _possessing = true;
                _possessedId = bestId;
                var isBox = TryFindItem(bestId, out var possessed) && possessed.kind == 1;
                _status = isBox ? "possessed folder — stick drag" : "possessed — drag onto a folder";
            }
            else
            {
                _status = $"possess failed: {_bridge.LastError}";
            }
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

            if (_bridge.TryGetKineticState(out var ks) && ks.entityId == entityId &&
                (ks.phase == (uint)KineticPhase.Possessed || ks.phase == (uint)KineticPhase.Coasting))
            {
                return true;
            }

            return false;
        }

        private void UpdateInsertArrow()
        {
            if (_insertArrow == null || !_bridge.TryGetSnap(out var snap) || snap.pending == 0)
            {
                if (_insertArrow != null)
                {
                    _insertArrow.enabled = false;
                }

                return;
            }

            if (!_views.TryGetValue(snap.objectId, out var view) || view.Root == null)
            {
                _insertArrow.enabled = false;
                return;
            }

            var tip = view.Root.transform.position + Vector3.down * 0.11f;
            var basePos = tip + Vector3.down * 0.04f;
            _insertArrow.enabled = true;
            _insertArrow.positionCount = 2;
            _insertArrow.SetPosition(0, tip);
            _insertArrow.SetPosition(1, basePos);
            _insertArrow.startColor = _insertArrow.endColor = new Color(1f, 0.85f, 0.2f, 0.9f);
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
            try
            {
                // Always play open / bump / close first.
                if (_views.TryGetValue(ev.boxId, out var boxView) && boxView.Root != null)
                {
                    _bridge.SetBoxOpen(ev.boxId, true);
                    var t = boxView.Root.transform;
                    var baseScale = t.localScale;
                    t.localScale = baseScale * 1.15f;
                    yield return new WaitForSeconds(0.14f);
                    t.localScale = baseScale;
                    yield return new WaitForSeconds(0.1f);
                    _bridge.SetBoxOpen(ev.boxId, false);
                    yield return new WaitForSeconds(0.08f);
                }
                else
                {
                    yield return null;
                }

                ResolveInsertNames(ev, out var objName, out var boxName);
                _status = $"moving {objName} → {boxName}/";
                yield return CommitInsertToDisk(objName, boxName, result =>
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
            finally
            {
                // Always ack + clear busy so a failed/aborted insert cannot disable grab.
                _bridge.AckStorageEvent(ok);
                _insertAnimBusy = false;
            }

            RebuildViews();
        }

        private void ResolveInsertNames(InteropStorageEvent ev, out string objName, out string boxName)
        {
            objName = string.Empty;
            boxName = string.Empty;
            if (TryFindItem(ev.objectId, out var obj) && !string.IsNullOrEmpty(obj.Name))
            {
                objName = obj.Name;
            }

            if (TryFindItem(ev.boxId, out var box) && !string.IsNullOrEmpty(box.Name))
            {
                boxName = box.Name;
            }

            // Prefer event paths only when they look like sane relative demo paths.
            if (!string.IsNullOrEmpty(ev.SourceRel))
            {
                var name = Path.GetFileName(ev.SourceRel.Replace('\\', '/'));
                if (!string.IsNullOrEmpty(name) &&
                    name.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
                {
                    objName = name;
                }
            }

            if (!string.IsNullOrEmpty(ev.DestRel))
            {
                var norm = ev.DestRel.Replace('\\', '/').TrimEnd('/');
                var slash = norm.LastIndexOf('/');
                if (slash > 0)
                {
                    var folder = norm.Substring(0, slash);
                    var leaf = norm.Substring(slash + 1);
                    if (!string.IsNullOrEmpty(folder) && folder.IndexOf('/') < 0)
                    {
                        boxName = folder;
                    }

                    if (!string.IsNullOrEmpty(leaf) &&
                        leaf.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
                    {
                        objName = leaf;
                    }
                }
            }
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
        /// </summary>
        private IEnumerator CommitInsertToDisk(string objName, string boxName, System.Action<InsertFsResult> done)
        {
            var result = new InsertFsResult
            {
                ok = false,
                src = string.Empty,
                dst = string.Empty,
            };

            var docs = PreferredDocumentsDemoPath();
            var persistent = FallbackPersistentDemoPath();
            var writeRoot = string.IsNullOrEmpty(_demoRoot) ? persistent : _demoRoot;

            try
            {
                Directory.CreateDirectory(writeRoot);
                Directory.CreateDirectory(Path.Combine(writeRoot, boxName));
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[3OS] insert mkdir failed: {ex.Message}");
                done?.Invoke(result);
                yield break;
            }

            result.dst = Path.Combine(writeRoot, boxName, objName);
            result.src = Path.Combine(writeRoot, objName);

            byte[] data = null;
            string loadedFrom = null;

            // Prefer reading from Documents (what the user browses), then persistent, then seed.
            foreach (var candidate in new[]
                     {
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
                    var candidate = Path.Combine(root, objName);
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

            var docsSrc = Path.Combine(docs, objName);
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
                cutDocs = TryCutRootFile(docs, objName, boxName, data);
            }

            var cutPers = TryCutRootFile(persistent, objName, boxName, data);
            if (!string.Equals(writeRoot, docs, System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(writeRoot, persistent, System.StringComparison.OrdinalIgnoreCase))
            {
                TryCutRootFile(writeRoot, objName, boxName, data);
            }

            var docsDst = Path.Combine(docs, boxName, objName);
            var persDst = Path.Combine(persistent, boxName, objName);
            var destLanded = DestFileOk(docsDst) || DestFileOk(persDst) || DestFileOk(result.dst);

            if (DestFileOk(docsDst))
            {
                result.dst = docsDst;
                result.src = Path.Combine(docs, objName);
            }
            else if (DestFileOk(persDst))
            {
                result.dst = persDst;
                result.src = Path.Combine(persistent, objName);
            }

            result.ok = cutDocs || cutPers || destLanded;
            Debug.Log(
                $"[3OS] insert cut docs={cutDocs} pers={cutPers} destOk={destLanded} from={loadedFrom} → {result.dst}");

            // Only scrub root AFTER a box destination exists — never delete the only copy.
            if (destLanded || DestFileOk(docsDst) || DestFileOk(persDst))
            {
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    TryDeleteFile(Path.Combine(docs, objName));
                    TryDeleteFile(Path.Combine(persistent, objName));
                    // MediaStore root-only (not the box path).
                    ThreeOSAndroidStorage.MediaStoreDeleteByRelativeName(
                        "Documents/" + DemoFolderName + "/", objName);
                    if (!FileLooksPresent(Path.Combine(docs, objName)) &&
                        !FileLooksPresent(Path.Combine(persistent, objName)))
                    {
                        break;
                    }

                    yield return null;
                }

                if (FileLooksPresent(Path.Combine(docs, objName)) &&
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
        /// Move <c>{root}/{obj}</c> → <c>{root}/{box}/{obj}</c>.
        /// Order is write/move destination first, delete source second. Never deletes the
        /// destination after a successful write (Exists can lie on Quest and caused data loss).
        /// </summary>
        private bool TryCutRootFile(string root, string objName, string boxName, byte[] data)
        {
            if (string.IsNullOrEmpty(root))
            {
                return false;
            }

            var src = Path.Combine(root, objName);
            var dst = Path.Combine(root, boxName, objName);
            var isDocumentsRoot = string.Equals(root, PreferredDocumentsDemoPath(),
                System.StringComparison.OrdinalIgnoreCase);

            try
            {
                Directory.CreateDirectory(Path.Combine(root, boxName));
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

            // Destination confirmed — now remove root source (cut).
            TryDeleteFile(src);
            if (isDocumentsRoot)
            {
                ThreeOSAndroidStorage.MediaStoreDelete(src);
                ThreeOSAndroidStorage.MediaStoreDeleteByRelativeName(
                    "Documents/" + DemoFolderName + "/", objName);
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

        private IEnumerable<string> CandidateDemoRoots()
        {
            if (!string.IsNullOrEmpty(_demoRoot))
            {
                yield return _demoRoot;
            }

            yield return FallbackPersistentDemoPath();

            var docs = PreferredDocumentsDemoPath();
            if (!string.IsNullOrEmpty(docs))
            {
                yield return docs;
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

        private void EnsureInsertArrow()
        {
            var go = GameObject.Find("3OS_InsertArrow");
            if (go == null)
            {
                go = new GameObject("3OS_InsertArrow");
            }

            _insertArrow = go.GetComponent<LineRenderer>();
            if (_insertArrow == null)
            {
                _insertArrow = go.AddComponent<LineRenderer>();
            }

            _insertArrow.material = new Material(Shader.Find("Sprites/Default"));
            _insertArrow.widthMultiplier = 0.01f;
            _insertArrow.useWorldSpace = true;
            _insertArrow.enabled = false;
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
    }
}
