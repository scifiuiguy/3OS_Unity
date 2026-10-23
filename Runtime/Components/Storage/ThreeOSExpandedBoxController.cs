using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ThreeOS
{
    internal sealed class ThreeOSExpandedBoxController
    {
        private readonly ThreeOSStorageController _host;
        internal readonly Dictionary<ulong, ThreeOSExpandedBoxUi> ExpandedBoxes =
            new Dictionary<ulong, ThreeOSExpandedBoxUi>();
        internal ThreeOSExpandedBoxController(ThreeOSStorageController host) { _host = host; }

        private ThreeOSBridge _bridge => _host.Bridge;
        private Dictionary<ulong, ThreeOSGlyphView> _views => _host.Glyphs.Views;
        private HashSet<ulong> _animLockedIds => _host.AnimLockedIds;
        private bool _boxAnimBusy { get => _host.BoxAnimBusy; set => _host.BoxAnimBusy = value; }
        private string _status { get => _host.StatusField; set => _host.StatusField = value; }
        private string _demoRoot => _host.DemoRoot;
        private ulong _xHoverBoxId { get => _host.XHoverBoxId; set => _host.XHoverBoxId = value; }
        private float BoxExpandAnimSeconds => _host.BoxExpandAnimSeconds;
        private Coroutine StartCoroutine(IEnumerator rut) => _host.StartCoroutinePublic(rut);
        private void Destroy(UnityEngine.Object o) { if (o != null) UnityEngine.Object.Destroy(o); }

        internal void TeardownAllExpandedBoxes(bool collapseKernel)
        {
            if (ExpandedBoxes.Count == 0)
            {
                _xHoverBoxId = 0;
                _animLockedIds.Clear();
                return;
            }

            var boxIds = new List<ulong>(ExpandedBoxes.Keys);
            foreach (var boxId in boxIds)
            {
                if (!ExpandedBoxes.TryGetValue(boxId, out var ui))
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

            ExpandedBoxes.Clear();
            _xHoverBoxId = 0;
            _animLockedIds.Clear();
        }
        internal IEnumerator OpenBoxSequence(ulong boxId)
        {
            if (_boxAnimBusy || ExpandedBoxes.ContainsKey(boxId))
            {
                yield break;
            }

            if (!_host.TryFindItem(boxId, out var boxItem) || boxItem.kind != 1)
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
                    if (!_host.TryFindItem(childId, out var childItem) || (childItem.flags & 1u) == 0)
                    {
                        continue;
                    }

                    var unity = InteropStructs.ToUnityPose(childItem.pose);
                    targets[childId] = unity;
                    centroid += unity.position;
                    n++;
                }

                var xPos = boxView.Root.transform.position +
                           boxView.Root.transform.up * _host.BoxDismissXOffsetM;
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
                        if (_host.TryFindItem(childId, out var item))
                        {
                            _views[childId] = _host.Glyphs.CreateView(item);
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
                var ui = new ThreeOSExpandedBoxUi { BoxId = boxId };
                ui.ChildIds.AddRange(childIds);
                ui.XRoot = ThreeOSDismissAffordances.CreateDismissX(xPos);
                ui.XVisual = ui.XRoot.transform.Find("XVisual");
                ExpandedBoxes[boxId] = ui;
                _status = $"opened {boxName} ({childIds.Count})";
                Debug.Log($"[3OS] {_status}");
            }
            finally
            {
                _animLockedIds.Clear();
                _boxAnimBusy = false;
                _host.MarkStorageVisualsDirty();
            }
        }

        internal IEnumerator CloseBoxSequence(ulong boxId)
        {
            if (_boxAnimBusy || !ExpandedBoxes.ContainsKey(boxId))
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
                _host.MarkStorageVisualsDirty();
                _boxAnimBusy = false;
            }
        }

        /// <summary>
        /// Folder relative path for a box (e.g. <c>Parent/Nested</c>), walked via parent links.
        /// Nested opens used to pass only <see cref="InteropWorkspaceItem.name"/> and looked in
        /// the wrong directory, so children never spawned.
        /// </summary>
        internal string ResolveBoxRelativePath(ulong boxId)
        {
            var parts = new List<string>();
            var guard = 0;
            var id = boxId;
            while (id != 0 && guard++ < 64)
            {
                if (!_host.TryFindItem(id, out var item))
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

        internal bool EnsureBoxChildren(ulong boxId, string boxRelPath, out List<ulong> childIds)
        {
            if (string.IsNullOrEmpty(boxRelPath))
            {
                boxRelPath = ResolveBoxRelativePath(boxId);
            }

            CollectBoxFolderEntries(boxId, boxRelPath, out var files, out var dirs);

            // Reuse existing workspace children under this box (inserts + prior expands).
            var existingByName = new Dictionary<string, ulong>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var id in CollectChildIds(boxId))
            {
                if (_host.TryFindItem(id, out var item))
                {
                    existingByName[item.Name] = id;
                }
            }

            var boxFsRoot = _host.ResolveFsRootForEntity(boxId, boxRelPath);
            var boxLeaf = string.IsNullOrEmpty(boxFsRoot)
                ? ThreeOSStorageController.DemoFolderName
                : Path.GetFileName(boxFsRoot.TrimEnd('/', '\\'));

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

                if (!string.IsNullOrEmpty(boxFsRoot))
                    _host.RetargetEntityMindPalace(id, boxId);
            }

            foreach (var name in dirs)
            {
                if (existingByName.ContainsKey(name))
                {
                    continue;
                }

                var rel = $"{boxRelPath}/{name}";
                uint nestedCount = 0;
                foreach (var root in _host.Fs.CandidateDemoRootsFor(boxLeaf))
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

                if (!string.IsNullOrEmpty(boxFsRoot))
                    _host.RetargetEntityMindPalace(id, boxId);
            }

            childIds = CollectChildIds(boxId);
            return true;
        }

        /// <summary>
        /// Close <paramref name="rootBoxId"/> and every expanded descendant, deepest first,
        /// each with the full shrink-to-X animation.
        /// </summary>
        internal IEnumerator CloseExpandedChainDeepestFirst(ulong rootBoxId)
        {
            var chain = new List<ulong>();
            foreach (var id in ExpandedBoxes.Keys)
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
                if (ExpandedBoxes.ContainsKey(id))
                {
                    yield return AnimateCollapseOneExpandedBox(id);
                }
            }
        }

        /// <summary>
        /// One expanded box: cluster-shrink children into the X, hide, collapse kernel.
        /// Does not recurse — caller orders deepest-first.
        /// </summary>
        internal IEnumerator AnimateCollapseOneExpandedBox(ulong boxId)
        {
            if (!ExpandedBoxes.TryGetValue(boxId, out var ui))
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
                xPos = boxView.Root.transform.position + boxView.Root.transform.up * _host.BoxDismissXOffsetM;
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

            ExpandedBoxes.Remove(boxId);
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
        internal void TeardownExpandedDescendants(ulong ancestorBoxId, bool collapseKernel)
        {
            var toClose = new List<ulong>();
            foreach (var id in ExpandedBoxes.Keys)
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

        internal bool IsBoxUnderAncestor(ulong boxId, ulong ancestorBoxId)
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

        internal int BoxDepth(ulong boxId)
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

        internal void ForceTeardownExpandedBox(ulong boxId, bool collapseKernel)
        {
            if (!ExpandedBoxes.TryGetValue(boxId, out var ui))
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

            ExpandedBoxes.Remove(boxId);
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

        internal List<ulong> CollectChildIds(ulong boxId)
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

        internal void CollectBoxFolderEntries(string boxRelPath, out List<string> files, out List<string> dirs) =>
            CollectBoxFolderEntries(0, boxRelPath, out files, out dirs);

        /// <summary>
        /// List files/dirs under <paramref name="boxRelPath"/> using the Mind Palace root for
        /// <paramref name="boxId"/> (Demo1/2/3). Previously only scanned 3OS_Demo, so Demo3's
        /// DemonProject always looked empty after a cross-palace paste.
        /// </summary>
        internal void CollectBoxFolderEntries(ulong boxId, string boxRelPath, out List<string> files,
            out List<string> dirs)
        {
            var fileSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var dirSet = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var rel = (boxRelPath ?? string.Empty).Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            var roots = new List<string>();
            var mpRoot = boxId != 0 ? _host.ResolveFsRootForEntity(boxId, boxRelPath) : null;
            if (!string.IsNullOrEmpty(mpRoot))
            {
                var leaf = Path.GetFileName(mpRoot.TrimEnd('/', '\\'));
                foreach (var r in _host.Fs.CandidateDemoRootsFor(leaf))
                {
                    if (!string.IsNullOrEmpty(r) && !roots.Contains(r))
                        roots.Add(r);
                }
            }

            // Fallback: probe all three demo leaves (paste may have landed before mp map update).
            if (roots.Count == 0)
            {
                foreach (var leaf in new[]
                         {
                             ThreeOSStorageController.DemoFolderName,
                             ThreeOSStorageController.DemoFolderName2,
                             ThreeOSStorageController.DemoFolderName3
                         })
                {
                    foreach (var r in _host.Fs.CandidateDemoRootsFor(leaf))
                    {
                        if (!string.IsNullOrEmpty(r) && !roots.Contains(r))
                            roots.Add(r);
                    }
                }
            }

            foreach (var root in roots)
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
                    if (!string.IsNullOrEmpty(name) && !name.StartsWith("."))
                        fileSet.Add(name);
                }

                foreach (var name in ThreeOSAndroidStorage.ListDirectoryNames(folder))
                {
                    if (!string.IsNullOrEmpty(name) && !name.StartsWith("."))
                        dirSet.Add(name);
                }
            }

            files = new List<string>(fileSet);
            dirs = new List<string>(dirSet);
            files.Sort(System.StringComparer.OrdinalIgnoreCase);
            dirs.Sort(System.StringComparer.OrdinalIgnoreCase);
        }

    }
}
