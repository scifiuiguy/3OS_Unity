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
        private string _demoRoot3 => _host.DemoRoot3Field;
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

        internal struct PasteUndoEntry
        {
            public string SrcRoot;
            public string SourceRel;
            public string DstRoot;
            public string DestRel;
            public bool IsFolder;
        }

        /// <summary>Last successful paste batch (multi-item paste = one batch). Debug undo only.</summary>
        readonly List<PasteUndoEntry> _lastPasteBatch = new List<PasteUndoEntry>(8);
        readonly Queue<PendingFsCut> _pendingFsCuts = new Queue<PendingFsCut>(64);
        readonly object _pendingFsLock = new object();
        ThreeOSPasteProgressBar _pasteBar;
        int _pasteBatchCompleted;
        int _pasteBatchKnownTotal;
        BgCopyProgress _pasteCopyProgress;
        int _pendingFsTotal;
        int _pendingFsDone;
        int _pendingFsFailed;

        struct PendingFsCut
        {
            public bool IsFolder;
            public string SourceRel;
            public string DestRel;
            public string SrcRoot;
            public string DstRoot;
        }

        internal bool HasLastPasteBatch => _lastPasteBatch.Count > 0;

        internal void ClearLastPasteBatch() => _lastPasteBatch.Clear();

        internal IEnumerator InsertSequence(InteropStorageEvent ev, bool isContinuation = false)
        {
            // Hold busy through ack + FS drain so HandleStorageEvents cannot start a second
            // InsertSequence that clears the queue / remounts mid-cut (Demo/Demo2 stayed put).
            if (!isContinuation)
                _insertAnimBusy = true;
            // Optimistic ack: kernel already reparented. Waiting on MTP/Java per item was
            // crushing Quest to 0–1 fps and false-acking (full visual revert) when verify lied.
            var ok = true;
            // Note: C# forbids yield inside try/catch — use try/finally only.
            try
            {
                // First event only: chrome teardown + short bump. Continuations skip —
                // per-item RebuildViews was the double-paste lag/crash storm.
                if (!isContinuation)
                {
                    // New paste session replaces the previous undo snapshot.
                    _lastPasteBatch.Clear();
                    lock (_pendingFsLock)
                    {
                        _pendingFsCuts.Clear();
                        _pendingFsTotal = 0;
                        _pendingFsDone = 0;
                        _pendingFsFailed = 0;
                    }

                    _pasteBatchCompleted = 0;
                    _pasteBatchKnownTotal = 1;
                    TeardownAllExpandedBoxes(collapseKernel: true);

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

                    EnsurePasteBar();
                    if (bumpT != null)
                    {
                        _pasteBar.ShowAt(bumpT, bumpT.up * 0.12f);
                        _bridge.SetBoxOpen(ev.boxId, true);
                        var baseScale = bumpT.localScale;
                        yield return AnimateScaleBump(bumpT, baseScale, 1.08f, 0.04f);
                        _bridge.SetBoxOpen(ev.boxId, false);
                    }
                    else
                    {
                        var cam = Camera.main;
                        if (cam != null)
                            _pasteBar.ShowAt(cam.transform, cam.transform.forward * 0.55f + cam.transform.up * -0.05f);
                    }

                    _pasteBar.SetProgress(0f);
                }

                yield return null;

                ResolveInsertPaths(ev, out var objName, out var boxName, out var sourceRel,
                    out var destRel);
                // Same path (already nested under target): kernel parent is updated; skip FS cut.
                if (!string.IsNullOrEmpty(sourceRel) &&
                    string.Equals(NormalizeDemoRel(sourceRel), NormalizeDemoRel(destRel),
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    _status = $"already in {boxName}/";
                }
                else
                {
                    var sourceIsBox = _host.TryFindItem(ev.objectId, out var srcItem) && srcItem.kind == 1;
                    var srcRoot = _host.ResolveFsRootForEntity(ev.objectId, sourceRel);
                    var dstRoot = _host.ResolveFsRootForEntity(ev.boxId, ParentRel(destRel));
                    if (string.IsNullOrEmpty(dstRoot))
                        dstRoot = _host.ResolveFsRootForEntity(ev.boxId, boxName);
                    if (string.IsNullOrEmpty(srcRoot))
                        srcRoot = _demoRoot;
                    if (string.IsNullOrEmpty(dstRoot))
                        dstRoot = srcRoot;

                    _status = $"queued {objName} → {boxName}/";
                    _lastPasteBatch.Add(new PasteUndoEntry
                    {
                        SrcRoot = srcRoot,
                        SourceRel = sourceRel,
                        DstRoot = dstRoot,
                        DestRel = destRel,
                        IsFolder = sourceIsBox,
                    });
                    _host.RetargetEntityMindPalace(ev.objectId, ev.boxId);

                    lock (_pendingFsLock)
                    {
                        _pendingFsCuts.Enqueue(new PendingFsCut
                        {
                            IsFolder = sourceIsBox,
                            SourceRel = sourceRel,
                            DestRel = destRel,
                            SrcRoot = srcRoot,
                            DstRoot = dstRoot,
                        });
                        _pendingFsTotal++;
                    }

                    Debug.Log($"[3OS] paste enqueue box={sourceIsBox} {sourceRel} → {destRel}");
                }

                _pasteBatchCompleted++;
                RefreshPasteBarProgress(itemFraction: 1f);
            }
            finally
            {
                // Ack immediately so kernel keeps the new parents — FS drains before busy clears.
                _bridge.AckStorageEvent(ok);
            }

            try
            {
                // Drain the kernel event queue fast (no per-item FS wait).
                if (_bridge.TryPollStorageEvent(out var next) && next.type != 0 && next.awaitingAck != 0)
                {
                    _pasteBatchKnownTotal = Mathf.Max(_pasteBatchKnownTotal, _pasteBatchCompleted + 1);
                    RefreshPasteBarProgress(itemFraction: 0f);
                    yield return null;
                    yield return InsertSequence(next, isContinuation: true);
                }
                else
                {
                    // All kernel events acked — drain disk cuts off the main thread.
                    yield return DrainPendingFsCutsRoutine();
                    _pasteBar?.SetProgress(1f);
                    yield return null;
                    if (_lastPasteBatch.Count > 0)
                    {
                        _host.InvalidatePalaceNamePrecache();
                        _host.RequestPostPasteChildHeal();
                    }

                    _host.MarkViewsDirty();
                    _host.MarkGhostsDirty();
                    yield return null;
                    _pasteBar?.Hide();
                }
            }
            finally
            {
                // Must clear even if ShellMv/JNI throws — a stuck busy bit silently drops all
                // later pastes (no enqueue logs; Demo/Demo2 never move).
                if (!isContinuation)
                    _insertAnimBusy = false;
            }
        }

        /// <summary>
        /// Process queued folder/file cuts on the ThreadPool one-at-a-time.
        /// Folder NIO/JNI runs via <see cref="ThreeOSAndroidStorage.RunWithJni"/> so the
        /// Unity main thread only polls progress (no ~80–100ms stalls per folder).
        /// </summary>
        IEnumerator DrainPendingFsCutsRoutine()
        {
            _pasteCopyProgress = new BgCopyProgress();
            while (true)
            {
                PendingFsCut job;
                lock (_pendingFsLock)
                {
                    if (_pendingFsCuts.Count == 0)
                        break;
                    job = _pendingFsCuts.Dequeue();
                }

                var bg = new BgInsertState();
                var jobCap = job;
                var progress = _pasteCopyProgress;

                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        ThreeOSAndroidStorage.RunWithJni(() =>
                        {
                            progress.totalFiles = Mathf.Max(1, progress.totalFiles);
                            if (jobCap.IsFolder)
                            {
                                // Documents folders need Java NIO; managed Move usually fails.
                                bg.ok = TryCutRelFolderCross(jobCap.SrcRoot, jobCap.SourceRel,
                                    jobCap.DstRoot, jobCap.DestRel, progress);
                                if (bg.ok)
                                    ScrubRelFolderMirrors(jobCap.SourceRel, jobCap.SrcRoot);
                            }
                            else
                            {
                                byte[] data = null;
                                var src = ThreeOSStorageController.CombineRootRelPublic(jobCap.SrcRoot,
                                    jobCap.SourceRel);
                                try
                                {
                                    if (ThreeOSDemoRootBootstrap.FileLooksPresent(src))
                                        data = File.ReadAllBytes(src);
                                }
                                catch (System.Exception ex)
                                {
                                    Debug.LogWarning($"[3OS] deferred read {src}: {ex.Message}");
                                }

                                bg.ok = TryCutRelFileCross(jobCap.SrcRoot, jobCap.SourceRel, jobCap.DstRoot,
                                    jobCap.DestRel, data);
                                if (!bg.ok)
                                {
                                    Debug.LogWarning(
                                        $"[3OS] managed cut missed — Java fallback file={jobCap.SourceRel}");
                                    bg.ok = TryCutRelFileCrossJava(jobCap.SrcRoot, jobCap.SourceRel,
                                        jobCap.DstRoot, jobCap.DestRel);
                                }
                            }

                            Debug.Log(
                                $"[3OS] deferred FS ok={bg.ok} folder={jobCap.IsFolder} {jobCap.SourceRel} → {jobCap.DestRel}");
                        });
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError("[3OS] deferred FS failed: " + ex.Message);
                        bg.ok = false;
                    }
                    finally
                    {
                        System.Threading.Thread.MemoryBarrier();
                        bg.done = true;
                    }
                });

                var waited = 0f;
                var lastStallLog = -30f;
                while (!bg.done)
                {
                    int done;
                    int total;
                    lock (_pendingFsLock)
                    {
                        done = _pendingFsDone;
                        total = Mathf.Max(1, _pendingFsTotal);
                    }

                    _pasteBar?.SetProgress(Mathf.Clamp01((done + 0.5f) / total));
                    waited += Time.unscaledDeltaTime;
                    if (waited - lastStallLog >= 30f)
                    {
                        lastStallLog = waited;
                        Debug.Log(
                            $"[3OS] deferred FS still running {waited:0}s {(jobCap.IsFolder ? "folder" : "file")}={jobCap.SourceRel}");
                    }

                    yield return null;
                }

                System.Threading.Thread.MemoryBarrier();

                lock (_pendingFsLock)
                {
                    _pendingFsDone++;
                    if (!bg.ok)
                        _pendingFsFailed++;
                    _pasteBar?.SetProgress(Mathf.Clamp01(_pendingFsDone / (float)Mathf.Max(1, _pendingFsTotal)));
                }
            }

            int failed;
            lock (_pendingFsLock)
            {
                failed = _pendingFsFailed;
            }

            if (failed > 0)
            {
                Debug.LogWarning(
                    $"[3OS] deferred FS finished with {failed} failure(s) — remounting palace from disk");
                yield return _host.RemountPalaceAfterDemoResetRoutine();
            }
            else
            {
                Debug.Log($"[3OS] deferred FS finished ok total={_pendingFsTotal}");
            }
        }

        void EnsurePasteBar()
        {
            if (_pasteBar == null)
                _pasteBar = ThreeOSPasteProgressBar.Create(_host.transform);
        }

        /// <summary>Hide paste UI immediately (mode wipe / soft-dismiss). Does not abort FS IO.</summary>
        internal void CancelPasteProgressUi()
        {
            _pasteBar?.SetProgress(1f);
            _pasteBar?.Hide();
        }

        void RefreshPasteBarProgress(float itemFraction)
        {
            if (_pasteBar == null)
                return;
            // knownTotal grows as the queue drains; denominator is always "done so far + remaining known".
            var total = Mathf.Max(_pasteBatchKnownTotal, _pasteBatchCompleted + 1);
            var done = Mathf.Max(0, _pasteBatchCompleted);
            var t = (done + Mathf.Clamp01(itemFraction)) / total;
            _pasteBar.SetProgress(Mathf.Clamp01(t));
        }

        /// <summary>
        /// Debug: reverse the last paste batch on disk, then remount Mind Palace from FS
        /// so kernel parents match. Invoked from Act I palms-down dual-ternary reset.
        /// </summary>
        internal IEnumerator UndoLastPasteRoutine()
        {
            if (_lastPasteBatch.Count == 0)
            {
                Debug.Log("[3OS] paste undo: nothing recorded");
                yield break;
            }

            // Wait out an in-flight insert so we don't race the ThreadPool cut.
            var wait = 0f;
            while (_insertAnimBusy && wait < 10f)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            var batch = new List<PasteUndoEntry>(_lastPasteBatch);
            _lastPasteBatch.Clear();
            _bridge.VoodooCutCancel();

            // JNI folder moves on ThreadPool (same as DrainPendingFsCutsRoutine).
            for (var i = batch.Count - 1; i >= 0; i--)
            {
                var e = batch[i];
                var bg = new BgInsertState();
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        ThreeOSAndroidStorage.RunWithJni(() =>
                        {
                            if (e.IsFolder)
                                bg.ok = TryCutRelFolderCross(e.DstRoot, e.DestRel, e.SrcRoot, e.SourceRel);
                            else
                                bg.ok = TryCutRelFileCross(e.DstRoot, e.DestRel, e.SrcRoot, e.SourceRel, null);
                        });
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError("[3OS] paste undo FS failed: " + ex.Message);
                        bg.ok = false;
                    }
                    finally
                    {
                        System.Threading.Thread.MemoryBarrier();
                        bg.done = true;
                    }
                });

                while (!bg.done)
                    yield return null;
                System.Threading.Thread.MemoryBarrier();

                Debug.Log(
                    $"[3OS] paste undo {(bg.ok ? "OK" : "FAIL")} {e.DestRel} @ {GuessFolderLeaf(e.DstRoot)} → {e.SourceRel} @ {GuessFolderLeaf(e.SrcRoot)}");
            }

            _host.InvalidatePalaceNamePrecache();
            _status = "paste undone — remounting palace";
            yield return _host.ForceReloadMindPalaceRoutine();
            _status = "paste undone";
            Debug.Log($"[3OS] paste undo complete ({batch.Count} item(s))");
        }

        static string ParentRel(string destRel)
        {
            var norm = NormalizeDemoRelPublic(destRel);
            var slash = norm.LastIndexOf('/');
            return slash > 0 ? norm.Substring(0, slash) : string.Empty;
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
        /// Write-through insert. All Directory/File I/O runs on the ThreadPool — the
        /// coroutine only waits. (Unity Jobs cannot call managed System.IO.)
        /// <paramref name="srcRoot"/> / <paramref name="dstRoot"/> are absolute Mind Palace
        /// roots (Demo1/2/3) so cross-cluster paste lands in the right folder.
        /// </summary>
        internal IEnumerator CommitInsertToDisk(string objName, string boxName, bool sourceIsBox,
            string sourceRel, string destRel, string srcRoot, string dstRoot,
            BgCopyProgress copyProgress, System.Action<InsertFsResult> done)
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

            if (string.IsNullOrEmpty(srcRoot))
                srcRoot = _demoRoot;
            if (string.IsNullOrEmpty(dstRoot))
                dstRoot = srcRoot;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Permission prompt must stay on the main thread.
            var srcDocs = ThreeOSDemoRootBootstrap.DocumentsPath(
                GuessFolderLeaf(srcRoot));
            var needsDocs = ThreeOSDemoRootBootstrap.DirectoryLooksPresent(
                                ThreeOSStorageController.CombineRootRelPublic(srcDocs, sourceRel)) ||
                            ThreeOSDemoRootBootstrap.FileLooksPresent(
                                ThreeOSStorageController.CombineRootRelPublic(srcDocs, sourceRel));
            if (needsDocs && !ThreeOSAndroidStorage.IsExternalStorageManager())
            {
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

            var srcRootCap = srcRoot;
            var dstRootCap = dstRoot;
            var sourceRelCap = sourceRel;
            var destRelCap = destRel;
            var sourceIsBoxCap = sourceIsBox;
            var progress = copyProgress ?? new BgCopyProgress();

            result.src = ThreeOSStorageController.CombineRootRelPublic(srcRootCap, sourceRelCap);
            result.dst = ThreeOSStorageController.CombineRootRelPublic(dstRootCap, destRelCap);

            // Prefer ThreadPool + System.IO for folders AND files so the loading bar does not
            // stall the render thread. Java/JNI rename is main-thread-only and is a fallback
            // when managed Move/Copy cannot relocate MTP Documents folders.
            var bg = new BgInsertState
            {
                src = result.src,
                dst = result.dst,
            };

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    try
                    {
                        var destParent = Path.GetDirectoryName(bg.dst);
                        if (!string.IsNullOrEmpty(destParent))
                            Directory.CreateDirectory(destParent);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogError($"[3OS] insert mkdir failed: {ex.Message}");
                        return;
                    }

                    if (sourceIsBoxCap)
                    {
                        bg.ok = TryCutRelFolderCrossManaged(srcRootCap, sourceRelCap, dstRootCap,
                            destRelCap, progress);
                        if (bg.ok)
                            ScrubRelFolderMirrors(sourceRelCap, srcRootCap);
                    }
                    else
                    {
                        progress.totalFiles = 1;
                        byte[] data = null;
                        try
                        {
                            if (ThreeOSDemoRootBootstrap.FileLooksPresent(bg.src))
                                data = File.ReadAllBytes(bg.src);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"[3OS] read {bg.src}: {ex.Message}");
                        }

                        bg.ok = TryCutRelFileCross(srcRootCap, sourceRelCap, dstRootCap, destRelCap,
                            data);
                        progress.doneFiles = bg.ok ? 1 : 0;
                    }

                    Debug.Log(
                        $"[3OS] bg insert ok={bg.ok} box={sourceIsBoxCap} {bg.src} → {bg.dst}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[3OS] bg insert failed: {ex.Message}");
                    bg.ok = false;
                }
                finally
                {
                    System.Threading.Thread.MemoryBarrier();
                    bg.done = true;
                }
            });

            var waited = 0f;
            while (!bg.done && waited < 45f)
            {
                RefreshPasteBarProgress(progress.Fraction);
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            System.Threading.Thread.MemoryBarrier();
            if (!bg.done)
            {
                Debug.LogError("[3OS] bg insert timed out");
                result.ok = false;
            }
            else
            {
                result.ok = bg.ok;
                result.src = bg.src ?? string.Empty;
                result.dst = bg.dst ?? string.Empty;
            }

            // No Java/JNI fallback here — that path blocked the Quest render thread during
            // multi-item paste. Callers that need Java (rare) use TryCutRelFolderCross on main.
            done?.Invoke(result);
        }

        sealed class BgInsertState
        {
            public volatile bool done;
            public bool ok;
            public string src = string.Empty;
            public string dst = string.Empty;
        }

        internal sealed class BgCopyProgress
        {
            public volatile int totalFiles;
            public volatile int doneFiles;
            public float Fraction
            {
                get
                {
                    var t = totalFiles;
                    if (t <= 0)
                        return 0f;
                    return Mathf.Clamp01(doneFiles / (float)t);
                }
            }
        }

        static string GuessFolderLeaf(string absRoot)
        {
            if (string.IsNullOrEmpty(absRoot))
                return ThreeOSStorageController.DemoFolderName;
            var leaf = Path.GetFileName(absRoot.TrimEnd('/', '\\'));
            return string.IsNullOrEmpty(leaf) ? ThreeOSStorageController.DemoFolderName : leaf;
        }

        /// <summary>
        /// ThreadPool-safe folder cut: System.IO only (no AndroidJavaObject / JNI).
        /// </summary>
        bool TryCutRelFolderCrossManaged(string srcRoot, string sourceRel, string dstRoot,
            string destRel, BgCopyProgress progress = null)
        {
            var src = ThreeOSStorageController.CombineRootRelPublic(srcRoot, sourceRel);
            var dst = ThreeOSStorageController.CombineRootRelPublic(dstRoot, destRel);
            if (!ThreeOSDemoRootBootstrap.DirectoryLooksPresent(src))
                return ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst);

            try
            {
                var parent = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[3OS] folder mkdir " + dst + ": " + ex.Message);
            }

            if (string.Equals(src, dst, System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsPathUnderOrEqual(dst, src))
            {
                Debug.LogError("[3OS] refuse folder paste: destination is under source " + src + " → " + dst);
                return false;
            }

            try
            {
                if (Directory.Exists(dst))
                    Directory.Delete(dst, true);
            }
            catch { /* best-effort */ }

            var moveThrew = false;
            try
            {
                Directory.Move(src, dst);
            }
            catch (System.Exception ex)
            {
                moveThrew = true;
                Debug.LogWarning("[3OS] managed folder move " + src + " → " + dst + ": " + ex.Message);
            }

            // Quest MTP often still reports src after a successful Move. If dest is present,
            // treat as success — never delete dest on a false-negative verify (that caused
            // disk-ok / kernel-ack(false) desync: empty DemonProject in-scene, full on disk).
            if (!moveThrew && ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst))
            {
                if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(src))
                {
                    try { Directory.Delete(src, true); } catch { /* MTP ghost */ }
                }

                MarkFolderCutProgressDone(progress);
                return true;
            }

            try
            {
                if (progress != null && progress.totalFiles <= 0)
                    progress.totalFiles = Mathf.Max(1, CountFilesRecursive(src));
                CopyDirectoryRecursive(src, dst, progress);
                try { Directory.Delete(src, true); } catch { /* best-effort */ }
                if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst))
                {
                    MarkFolderCutProgressDone(progress);
                    return true;
                }

                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[3OS] managed folder copy failed " + src + " → " + dst + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Move/copy a relative folder from srcRoot to dstRoot (may differ).
        /// Safe on a ThreadPool worker after <see cref="ThreeOSAndroidStorage.RunWithJni"/>.
        /// </summary>
        bool TryCutRelFolderCross(string srcRoot, string sourceRel, string dstRoot, string destRel,
            BgCopyProgress progress = null)
        {
            var src = ResolveExistingFolderAbs(srcRoot, sourceRel);
            var dst = ThreeOSStorageController.CombineRootRelPublic(dstRoot, destRel);
            Debug.Log($"[3OS] folder cut abs {src} → {dst}");
            ThreeOSAndroidStorage.AppendPasteFsLog($"folder cut abs {src} → {dst}");
            if (!ThreeOSDemoRootBootstrap.DirectoryLooksPresent(src) &&
                !ThreeOSAndroidStorage.Exists(src))
            {
                Debug.LogWarning($"[3OS] folder cut src missing {sourceRel} (root={srcRoot})");
                ThreeOSAndroidStorage.AppendPasteFsLog($"src missing {sourceRel} root={srcRoot}");
                return ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst) ||
                       ThreeOSAndroidStorage.Exists(dst);
            }

            try
            {
                var parent = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[3OS] folder mkdir " + dst + ": " + ex.Message);
            }

            if (string.Equals(src, dst, System.StringComparison.OrdinalIgnoreCase))
                return true;

            // Pasting a folder into itself (or a descendant) creates DemonProject/DemonProject/...
            // path bombs that break Windows MTP with "device is no longer connected".
            if (IsPathUnderOrEqual(dst, src))
            {
                Debug.LogError("[3OS] refuse folder paste: destination is under source " + src + " → " + dst);
                return false;
            }

            TryRemoveDirectoryBestEffort(dst);

            // NIO once landed Test12 as a dir; Os.rename + ProcessBuilder sh mv next;
            // Runtime.exec(String[]) was the wrong overload and could stick insertAnimBusy.
            var moved = ThreeOSAndroidStorage.JavaNioMove(src, dst);
            ThreeOSAndroidStorage.AppendPasteFsLog($"NIO={moved} {src}");
            if (!moved)
            {
                moved = ThreeOSAndroidStorage.OsRename(src, dst);
                ThreeOSAndroidStorage.AppendPasteFsLog($"OsRename={moved}");
            }

            if (!moved)
            {
                moved = ThreeOSAndroidStorage.ShellMv(src, dst);
                ThreeOSAndroidStorage.AppendPasteFsLog($"ShellMv={moved}");
            }

            if (!moved)
            {
                moved = ThreeOSAndroidStorage.JavaRename(src, dst);
                ThreeOSAndroidStorage.AppendPasteFsLog($"renameTo={moved}");
            }

            if (moved)
            {
                if (FolderDestLanded(src, dst))
                {
                    MarkFolderCutProgressDone(progress);
                    ThreeOSAndroidStorage.AppendPasteFsLog($"folder OK {destRel}");
                    return true;
                }

                Debug.LogWarning("[3OS] Java folder move reported OK but verify failed " + src + " → " + dst);
                ThreeOSAndroidStorage.AppendPasteFsLog($"verify fail {src} → {dst}");
            }

            try
            {
                Directory.Move(src, dst);
                if (FolderDestLanded(src, dst))
                {
                    MarkFolderCutProgressDone(progress);
                    return true;
                }

                Debug.LogWarning("[3OS] Directory.Move returned without relocating " + src + " → " + dst);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[3OS] folder move " + src + " → " + dst + ": " + ex.Message + "; copy+delete");
            }

            try
            {
                if (progress != null && progress.totalFiles <= 0)
                    progress.totalFiles = Mathf.Max(1, CountFilesRecursive(src));
                if (!ThreeOSAndroidStorage.JavaCopyDirectory(src, dst))
                    CopyDirectoryRecursive(src, dst, progress);
                else if (progress != null)
                    MarkFolderCutProgressDone(progress);
                TryRemoveDirectoryBestEffort(src);
                if (FolderDestLanded(src, dst))
                    return true;

                // Dest missing — real failure. Never delete a present dest because MTP still
                // lists src (that wiped successful pastes and remounted Demo/Demo2 in place).
                Debug.LogError("[3OS] folder cut incomplete (dest missing) " + src + " → " + dst);
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[3OS] folder copy failed " + src + " → " + dst + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Dest present = success. If MTP still lists src, scrub it; do not roll back dest.
        /// </summary>
        bool FolderDestLanded(string src, string dst)
        {
            if (!ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst) &&
                !ThreeOSAndroidStorage.Exists(dst))
                return false;

            if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(src) ||
                ThreeOSAndroidStorage.Exists(src))
                TryRemoveDirectoryBestEffort(src);

            return true;
        }

        /// <summary>
        /// Prefer preferredRoot, else Documents/persistent mirrors that actually contain the folder.
        /// </summary>
        string ResolveExistingFolderAbs(string preferredRoot, string sourceRel)
        {
            var primary = ThreeOSStorageController.CombineRootRelPublic(preferredRoot, sourceRel);
            if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(primary) ||
                ThreeOSAndroidStorage.Exists(primary))
                return primary;

            var leaf = GuessFolderLeaf(preferredRoot);
            foreach (var root in CandidateDemoRootsFor(leaf))
            {
                if (string.Equals(root, preferredRoot, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                var cand = ThreeOSStorageController.CombineRootRelPublic(root, sourceRel);
                if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(cand) ||
                    ThreeOSAndroidStorage.Exists(cand))
                {
                    Debug.Log($"[3OS] folder cut retarget src {primary} → {cand}");
                    return cand;
                }
            }

            // Cross-palace: also probe Demo1/2/3 Documents + persistent for this relative path.
            foreach (var folderLeaf in new[]
                     {
                         ThreeOSStorageController.DemoFolderName,
                         ThreeOSStorageController.DemoFolderName2,
                         ThreeOSStorageController.DemoFolderName3,
                     })
            {
                foreach (var root in CandidateDemoRootsFor(folderLeaf))
                {
                    var cand = ThreeOSStorageController.CombineRootRelPublic(root, sourceRel);
                    if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(cand) ||
                        ThreeOSAndroidStorage.Exists(cand))
                    {
                        Debug.Log($"[3OS] folder cut retarget-any src → {cand}");
                        return cand;
                    }
                }
            }

            return primary;
        }

        /// <summary>Main-thread Java/NIO file cut for Documents when System.IO missed.</summary>
        bool TryCutRelFileCrossJava(string srcRoot, string sourceRel, string dstRoot, string destRel)
        {
            var src = ThreeOSStorageController.CombineRootRelPublic(srcRoot, sourceRel);
            var dst = ThreeOSStorageController.CombineRootRelPublic(dstRoot, destRel);
            if (!ThreeOSDemoRootBootstrap.FileLooksPresent(src) && !ThreeOSAndroidStorage.Exists(src))
                return ThreeOSDemoRootBootstrap.FileLooksPresent(dst) || ThreeOSAndroidStorage.Exists(dst);

            try
            {
                var parent = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[3OS] Java file mkdir " + dst + ": " + ex.Message);
            }

            if (string.Equals(src, dst, System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (ThreeOSAndroidStorage.OsRename(src, dst) ||
                ThreeOSAndroidStorage.ShellMv(src, dst) ||
                ThreeOSAndroidStorage.JavaNioMove(src, dst) ||
                ThreeOSAndroidStorage.JavaRename(src, dst))
            {
                if (ThreeOSDemoRootBootstrap.FileLooksPresent(dst) || ThreeOSAndroidStorage.Exists(dst))
                {
                    TryDeleteFile(src);
                    return true;
                }
            }

            // Last resort: managed copy path (may still work for small files).
            byte[] data = null;
            try
            {
                if (ThreeOSDemoRootBootstrap.FileLooksPresent(src))
                    data = File.ReadAllBytes(src);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[3OS] Java file read " + src + ": " + ex.Message);
            }

            return TryCutRelFileCross(srcRoot, sourceRel, dstRoot, destRel, data);
        }

        static bool FolderCutLanded(string src, string dst) =>
            ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst) &&
            !ThreeOSDemoRootBootstrap.DirectoryLooksPresent(src);

        static void MarkFolderCutProgressDone(BgCopyProgress progress)
        {
            if (progress == null)
                return;
            progress.totalFiles = Mathf.Max(1, progress.totalFiles);
            progress.doneFiles = progress.totalFiles;
        }

        static void TryRemoveDirectoryBestEffort(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !ThreeOSDemoRootBootstrap.DirectoryLooksPresent(path))
                return;

            try { Directory.Delete(path, true); }
            catch { /* fall through to Java */ }

            if (!ThreeOSDemoRootBootstrap.DirectoryLooksPresent(path))
                return;

            // Recursive Java delete for MTP leftovers System.IO cannot unlink.
            try
            {
                foreach (var file in Directory.GetFiles(path))
                    ThreeOSAndroidStorage.JavaDelete(file);
                foreach (var dir in Directory.GetDirectories(path))
                    TryRemoveDirectoryBestEffort(dir);
            }
            catch { /* best-effort */ }

            ThreeOSAndroidStorage.JavaDelete(path);
            try { Directory.Delete(path, true); } catch { /* best-effort */ }
        }

        bool TryCutRelFileCross(string srcRoot, string sourceRel, string dstRoot, string destRel, byte[] data)
        {
            var src = ThreeOSStorageController.CombineRootRelPublic(srcRoot, sourceRel);
            var dst = ThreeOSStorageController.CombineRootRelPublic(dstRoot, destRel);

            try
            {
                var parent = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[3OS] file mkdir " + dst + ": " + ex.Message);
            }

            if (data != null && data.Length > 0)
            {
                try
                {
                    File.WriteAllBytes(dst, data);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[3OS] write " + dst + ": " + ex.Message);
                }
            }
            else if (ThreeOSDemoRootBootstrap.FileLooksPresent(src) &&
                     !string.Equals(src, dst, System.StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Copy(src, dst, true);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[3OS] copy " + src + " → " + dst + ": " + ex.Message);
                }
            }

            // Prefer any visible dest — FileInfo.Length can throw/lie on MTP right after write.
            var landed = DestFileOk(dst) || ThreeOSDemoRootBootstrap.FileLooksPresent(dst) ||
                         ThreeOSAndroidStorage.Exists(dst);
            if (landed && !string.Equals(src, dst, System.StringComparison.OrdinalIgnoreCase))
                TryDeleteFile(src);

            return landed;
        }

        void ScrubRelFolderMirrors(string sourceRel, string keepAbsRoot)
        {
            var leaf = GuessFolderLeaf(keepAbsRoot);
            foreach (var root in CandidateDemoRootsFor(leaf))
            {
                if (string.Equals(root, keepAbsRoot, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                var path = ThreeOSStorageController.CombineRootRelPublic(root, sourceRel);
                try
                {
                    if (ThreeOSDemoRootBootstrap.DirectoryLooksPresent(path))
                        Directory.Delete(path, true);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[3OS] scrub mirror " + path + ": " + ex.Message);
                }
            }
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

            if (string.Equals(src, dst, System.StringComparison.OrdinalIgnoreCase))
                return true;

            if (IsPathUnderOrEqual(dst, src))
            {
                Debug.LogError("[3OS] refuse folder cut: destination is under source " + src + " → " + dst);
                return false;
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

            TryRemoveDirectoryBestEffort(dst);

            if (ThreeOSAndroidStorage.JavaNioMove(src, dst) ||
                ThreeOSAndroidStorage.JavaRename(src, dst))
            {
                if (FolderCutLanded(src, dst))
                {
                    Debug.Log($"[3OS] folder Java move OK {src} → {dst}");
                    return true;
                }
            }

            try
            {
                Directory.Move(src, dst);
                if (FolderCutLanded(src, dst))
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
                if (!ThreeOSAndroidStorage.JavaCopyDirectory(src, dst))
                    CopyDirectoryRecursive(src, dst);
                if (!ThreeOSDemoRootBootstrap.DirectoryLooksPresent(dst))
                    return false;

                TryRemoveDirectoryBestEffort(src);
                if (FolderCutLanded(src, dst))
                    return true;

                Debug.LogError($"[3OS] folder cut incomplete (src still present) {src}");
                TryRemoveDirectoryBestEffort(dst);
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[3OS] folder copy {src}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// True when <paramref name="path"/> is the same as <paramref name="ancestor"/> or nested under it.
        /// Used to block self-paste path bombs (folder copied into its own descendant).
        /// </summary>
        internal static bool IsPathUnderOrEqual(string path, string ancestor)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(ancestor))
                return false;
            try
            {
                var p = Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var a = Path.GetFullPath(ancestor)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(p, a, System.StringComparison.OrdinalIgnoreCase))
                    return true;
                var prefix = a + Path.DirectorySeparatorChar;
                return p.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        internal static int CountFilesRecursive(string src)
        {
            var n = 0;
            try
            {
                n += Directory.GetFiles(src).Length;
                foreach (var dir in Directory.GetDirectories(src))
                    n += CountFilesRecursive(dir);
            }
            catch
            {
                /* best-effort */
            }

            return n;
        }

        internal static void CopyDirectoryRecursive(string src, string dst, BgCopyProgress progress = null)
        {
            if (IsPathUnderOrEqual(dst, src))
                throw new System.InvalidOperationException(
                    "refusing recursive copy into source tree: " + src + " → " + dst);

            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
            {
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(dst, name), true);
                if (progress != null)
                    progress.doneFiles++;
            }

            foreach (var dir in Directory.GetDirectories(src))
            {
                var name = Path.GetFileName(dir);
                var childDst = Path.Combine(dst, name);
                if (IsPathUnderOrEqual(childDst, src))
                    continue;
                CopyDirectoryRecursive(dir, childDst, progress);
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

            if (string.Equals(folderLeaf, ThreeOSStorageController.DemoFolderName3, System.StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(_demoRoot3))
            {
                yield return _demoRoot3;
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
