using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace ThreeOS
{
    internal sealed class ThreeOSStorageWriteThrough
    {
        private readonly ThreeOSStorageController _host;
        internal ThreeOSStorageWriteThrough(ThreeOSStorageController host) { _host = host; }

        private ThreeOSBridge _bridge => _host.Bridge;
        private Dictionary<ulong, ThreeOSGlyphView> _views => _host.Glyphs.Views;
        private Dictionary<ulong, ThreeOSGlyphView> _ghostViews => _host.Glyphs.GhostViews;
        private HashSet<ulong> _animLockedIds => _host.AnimLockedIds;
        private void TeardownAllExpandedBoxes(bool collapseKernel) =>
            _host.Expanded.TeardownAllExpandedBoxes(collapseKernel);
        private bool _waitingForAllFilesAccess { get => _host.WaitingForAllFilesAccess; set => _host.WaitingForAllFilesAccess = value; }
        private bool _insertAnimBusy { get => _host.InsertAnimBusy; set => _host.InsertAnimBusy = value; }
        private string _status { get => _host.StatusField; set => _host.StatusField = value; }
        private string _demoRoot => _host.DemoRoot;
        private string _demoRoot2 => _host.DemoRoot2Field;
        private Coroutine StartCoroutine(IEnumerator rut) => _host.StartCoroutinePublic(rut);
        private void Destroy(UnityEngine.Object o) { if (o != null) UnityEngine.Object.Destroy(o); }

        internal IEnumerator EnsureAllFilesAccessForCut(string waitingStatus)
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
        internal void HandleStorageEvents()
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

        internal IEnumerator InsertSequence(InteropStorageEvent ev)
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
                    var sourceIsBox = _host.TryFindItem(ev.objectId, out var srcItem) && srcItem.kind == 1;
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

            _host.RebuildViews();
        }

        /// <summary>
        /// Resolve leaf names plus full relative paths for FS cut. Nested children
        /// (e.g. <c>Parent/ChildBox</c>) must not be treated as root-level names.
        /// </summary>
        internal void ResolveInsertPaths(InteropStorageEvent ev, out string objName, out string boxName,
            out string sourceRel, out string destRel)
        {
            objName = string.Empty;
            boxName = string.Empty;
            sourceRel = string.Empty;
            destRel = string.Empty;

            if (_host.TryFindItem(ev.objectId, out var obj) && !string.IsNullOrEmpty(obj.Name))
            {
                objName = obj.Name;
            }

            if (_host.TryFindItem(ev.boxId, out var box) && !string.IsNullOrEmpty(box.Name))
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

        internal static string NormalizeDemoRel(string rel) => NormalizeDemoRelPublic(rel);

        internal static string NormalizeDemoRelPublic(string rel)
        {
            if (string.IsNullOrEmpty(rel))
            {
                return string.Empty;
            }

            return rel.Replace('\\', '/').Trim().Trim('/');
        }

        internal struct InsertFsResult
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
        internal IEnumerator CommitInsertToDisk(string objName, string boxName, bool sourceIsBox,
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

            var docs = ThreeOSDemoRootBootstrap.PreferredDocumentsDemoPath();
            var persistent = ThreeOSDemoRootBootstrap.FallbackPersistentDemoPath();
            var writeRoot = string.IsNullOrEmpty(_demoRoot) ? persistent : _demoRoot;

            try
            {
                Directory.CreateDirectory(writeRoot);
                var destParent = Path.GetDirectoryName(ThreeOSStorageController.CombineRootRelPublic(writeRoot, destRel));
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

            result.dst = ThreeOSStorageController.CombineRootRelPublic(writeRoot, destRel);
            result.src = ThreeOSStorageController.CombineRootRelPublic(writeRoot, sourceRel);

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
                         ThreeOSStorageController.CombineRootRelPublic(docs, sourceRel),
                         ThreeOSStorageController.CombineRootRelPublic(persistent, sourceRel),
                         ThreeOSStorageController.CombineRootRelPublic(writeRoot, sourceRel),
                         Path.Combine(docs, objName),
                         Path.Combine(persistent, objName),
                         Path.Combine(writeRoot, objName),
                     })
            {
                if (string.IsNullOrEmpty(candidate) || !ThreeOSDemoRootBootstrap.FileLooksPresent(candidate))
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
                        var candidate = ThreeOSStorageController.CombineRootRelPublic(root, rel);
                        if (!ThreeOSDemoRootBootstrap.FileLooksPresent(candidate))
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

            var docsSrc = ThreeOSStorageController.CombineRootRelPublic(docs, sourceRel);
            var needsDocumentsCut = ThreeOSDemoRootBootstrap.FileLooksPresent(docsSrc) ||
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

            var docsDst = ThreeOSStorageController.CombineRootRelPublic(docs, destRel);
            var persDst = ThreeOSStorageController.CombineRootRelPublic(persistent, destRel);
            var destLanded = DestFileOk(docsDst) || DestFileOk(persDst) || DestFileOk(result.dst);

            if (DestFileOk(docsDst))
            {
                result.dst = docsDst;
                result.src = docsSrc;
            }
            else if (DestFileOk(persDst))
            {
                result.dst = persDst;
                result.src = ThreeOSStorageController.CombineRootRelPublic(persistent, sourceRel);
            }

            result.ok = cutDocs || cutPers || destLanded;
            Debug.Log(
                $"[3OS] insert cut docs={cutDocs} pers={cutPers} destOk={destLanded} from={loadedFrom} → {result.dst}");

            // Only scrub source AFTER a box destination exists — never delete the only copy.
            if (destLanded || DestFileOk(docsDst) || DestFileOk(persDst))
            {
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    TryDeleteFile(ThreeOSStorageController.CombineRootRelPublic(docs, sourceRel));
                    TryDeleteFile(ThreeOSStorageController.CombineRootRelPublic(persistent, sourceRel));
                    // MediaStore: nested relative under Documents/DemoFolder.
                    var mediaDir = "Documents/" + ThreeOSStorageController.DemoFolderName + "/";
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

                    if (!ThreeOSDemoRootBootstrap.FileLooksPresent(ThreeOSStorageController.CombineRootRelPublic(docs, sourceRel)) &&
                        !ThreeOSDemoRootBootstrap.FileLooksPresent(ThreeOSStorageController.CombineRootRelPublic(persistent, sourceRel)))
                    {
                        break;
                    }

                    yield return null;
                }

                if (ThreeOSDemoRootBootstrap.FileLooksPresent(ThreeOSStorageController.CombineRootRelPublic(docs, sourceRel)) &&
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

        internal IEnumerator CommitInsertFolderToDisk(string sourceRel, string destRel, string docs,
            string persistent, string writeRoot, InsertFsResult result, System.Action<InsertFsResult> done)
        {
            var docsSrc = ThreeOSStorageController.CombineRootRelPublic(docs, sourceRel);
            var needsDocumentsCut = ThreeOSDemoRootBootstrap.DirectoryLooksPresent(docsSrc) ||
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

            var docsDst = ThreeOSStorageController.CombineRootRelPublic(docs, destRel);
            var persDst = ThreeOSStorageController.CombineRootRelPublic(persistent, destRel);
            var destLanded = ThreeOSDemoRootBootstrap.DirectoryLooksPresent(docsDst) || ThreeOSDemoRootBootstrap.DirectoryLooksPresent(persDst) ||
                             ThreeOSDemoRootBootstrap.DirectoryLooksPresent(result.dst);

            if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(docsDst))
            {
                result.dst = docsDst;
                result.src = docsSrc;
            }
            else if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(persDst))
            {
                result.dst = persDst;
                result.src = ThreeOSStorageController.CombineRootRelPublic(persistent, sourceRel);
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

        internal static IEnumerator AnimateScaleBump(Transform t, Vector3 baseScale, float peak,
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
        internal static bool DestFileOk(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (ThreeOSDemoRootBootstrap.FileLooksPresent(path))
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

        internal bool RootLevelCopyExists(string objName)
        {
            foreach (var root in CandidateDemoRoots())
            {
                if (ThreeOSDemoRootBootstrap.FileLooksPresent(Path.Combine(root, objName)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Move <c>{root}/{sourceRel}</c> → <c>{root}/{destRel}</c> (box-into-box, nested OK).
        /// </summary>
        internal bool TryCutRelFolder(string root, string sourceRel, string destRel)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(sourceRel) ||
                string.IsNullOrEmpty(destRel))
            {
                return false;
            }

            var src = ThreeOSStorageController.CombineRootRelPublic(root, sourceRel);
            var dst = ThreeOSStorageController.CombineRootRelPublic(root, destRel);
            if (!ThreeOSDemoRootBootstrap.DirectoryLooksPresent(src))
            {
                return ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst);
            }

            if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst))
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
                if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst))
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
                if (!ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst))
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

        internal static void CopyDirectoryRecursive(string src, string dst)
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
        internal bool TryCutRelFile(string root, string sourceRel, string destRel, byte[] data)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(sourceRel) ||
                string.IsNullOrEmpty(destRel))
            {
                return false;
            }

            var src = ThreeOSStorageController.CombineRootRelPublic(root, sourceRel);
            var dst = ThreeOSStorageController.CombineRootRelPublic(root, destRel);
            var leaf = Path.GetFileName(sourceRel.Replace('\\', '/'));
            var isDocumentsRoot = string.Equals(root, ThreeOSDemoRootBootstrap.PreferredDocumentsDemoPath(),
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

            var hasSrc = ThreeOSDemoRootBootstrap.FileLooksPresent(src);

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
                return ThreeOSDemoRootBootstrap.FileLooksPresent(dst);
            }

            // Destination confirmed — now remove source (cut).
            TryDeleteFile(src);
            if (isDocumentsRoot)
            {
                ThreeOSAndroidStorage.MediaStoreDelete(src);
                var mediaDir = "Documents/" + ThreeOSStorageController.DemoFolderName + "/";
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
        internal void ScrubRootObject(string root, string objName)
        {
            var path = Path.Combine(root, objName);
            TryDeleteFile(path);
            ThreeOSAndroidStorage.MediaStoreDelete(path);
            if (string.Equals(root, ThreeOSDemoRootBootstrap.PreferredDocumentsDemoPath(), System.StringComparison.OrdinalIgnoreCase))
            {
                ThreeOSAndroidStorage.MediaStoreDeleteByRelativeName(
                    "Documents/" + ThreeOSStorageController.DemoFolderName + "/", objName);
            }
        }

        internal IEnumerable<string> CandidateDemoRoots() =>
            CandidateDemoRootsFor(ThreeOSStorageController.DemoFolderName);

        internal IEnumerable<string> CandidateDemoRootsFor(string folderLeaf)
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

            if (string.Equals(folderLeaf, ThreeOSStorageController.DemoFolderName, System.StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(_demoRoot))
            {
                yield return _demoRoot;
            }

            if (string.Equals(folderLeaf, ThreeOSStorageController.DemoFolderName2, System.StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(_demoRoot2))
            {
                yield return _demoRoot2;
            }
        }

        /// <summary>
        /// Delete <c>{root}/{objName}</c> for every known demo root. Does not touch
        /// <c>{root}/{box}/…</c> inserts.
        /// </summary>
        internal void DeleteAllRootLevelCopies(string objName)
        {
            foreach (var root in CandidateDemoRoots())
            {
                ScrubRootObject(root, objName);
            }
        }

        internal static bool TryDeleteFile(string path)
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

        internal static string ShortPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "?";
            }

            var marker = ThreeOSStorageController.DemoFolderName + Path.DirectorySeparatorChar;
            var idx = path.Replace('/', Path.DirectorySeparatorChar)
                .IndexOf(marker, System.StringComparison.OrdinalIgnoreCase);
            return idx >= 0 ? path.Substring(idx) : Path.GetFileName(path);
        }

    }
}
