using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Phase 0.8.1 host side: drag multi-select box visual and Solo Voodoo Drop driver.
    /// Selection / eligibility / drop logic lives in the kernel; this only feeds input and draws.
    /// </summary>
    internal sealed class ThreeOSSelectionGestures
    {
        static readonly Color BoxGray = new Color(0.85f, 0.85f, 0.85f, 0.6f);
        static readonly Color SoloIdle = new Color(1f, 0.92f, 0.3f, 0.35f);
        const float SoloBoxM = 0.4f;
        /// <summary>Match Final Demo Palace keep-list (above sky Overlay 5000).</summary>
        const int KeepRenderQueue = 5100;

        readonly ThreeOSStorageController _host;
        readonly ulong[] _cands = new ulong[8];
        LineRenderer _boxLine;
        LineRenderer _soloLine;
        Material _lineMat;
        bool _soloWasArmed;

        internal bool BoxActive { get; private set; }
        internal bool SoloArmed => _soloWasArmed;
        internal uint LastSoloEvent { get; private set; }

        internal ThreeOSSelectionGestures(ThreeOSStorageController host) { _host = host; }

        ThreeOSBridge Bridge => _host.Bridge;

        internal void BeginBox(Vector3 startXr, ThreeOSPose head, bool cumulative)
        {
            if (Bridge.IsSnapVoodooDragActive())
                return;
            if (!Bridge.MultiSelectBegin(startXr, head, cumulative)) return;
            BoxActive = true;
            _host.StatusField = "box select";
        }

        internal void UpdateBox(Vector3 handXr, bool cumulative)
        {
            if (!BoxActive) return;
            if (Bridge.IsSnapVoodooDragActive())
            {
                EndBox(true);
                return;
            }
            Bridge.MultiSelectUpdate(handXr, cumulative);
            if (Bridge.TryGetMultiSelectBox(out var box))
            {
                DrawBox(ref _boxLine, "3OS_MultiSelectBox", BoxGray, box.pose, box.extents.ToVector3());
            }
        }

        internal void EndBox(bool cancel)
        {
            if (!BoxActive) return;
            BoxActive = false;
            Bridge.MultiSelectEnd(cancel);
            if (_boxLine != null) _boxLine.gameObject.SetActive(false);
        }

        /// <summary>Per frame: arm / dismiss / drop for each tracked hand.</summary>
        internal bool IsSoloVoodooArmed()
        {
            if (!Bridge.TryGetSoloDropState(out var st))
                return false;
            return st.armed != 0;
        }

        /// <summary>Kernel solo tick — must run before <c>threeos_tick</c> (InputRouter).</summary>
        internal void TickSoloKernel(ref InteropInputFrame frame, ref InteropHandSample left,
            ref InteropHandSample right)
        {
            LastSoloEvent = 0;
            if (BoxActive) return;
            if (Bridge.IsSnapVoodooDragActive() && !Bridge.SoloBlocksVoodooSnap())
            {
                Bridge.SoloDropCancel();
                _soloWasArmed = false;
                return;
            }
            var head = frame.head;
            var n = Bridge.SoloDropCandidates(head, _cands);
            var clear = ResolveSoloClearTarget(head, n);
            Bridge.SoloDropBeginFrame();
            var now = (float)frame.timeSeconds;
            for (var hand = 0; hand < 2; hand++)
            {
                var s = hand == 0 ? left : right;
                if (s.tracked == 0) continue;
                var palmPos = SoloPalmPosition(s);
                var e = Bridge.SoloDropTick(hand, clear, s.palmNormal, s.primaryPressed != 0,
                    s.secondaryPressed != 0, head, palmPos, now);
                if (e != 0) LastSoloEvent = e;
            }
        }

        /// <summary>Draw armed solo BB + post-drop host bookkeeping (after kernel tick).</summary>
        internal void TickSoloPostKernel()
        {
            var ev = LastSoloEvent;
            if (!Bridge.TryGetSoloDropState(out var st))
            {
                if (_soloLine != null) _soloLine.gameObject.SetActive(false);
                _soloWasArmed = false;
                return;
            }
            var armed = st.armed != 0;
            if (armed)
            {
                var c = InteropStructs.OpenXrToUnityPosition(st.farPosition.ToVector3());
                var pose = new Pose(c, Quaternion.identity);
                var ext = st.farExtents.ToVector3();
                var size = (Mathf.Abs(ext.x) > 1e-4f && Mathf.Abs(ext.y) > 1e-4f &&
                            Mathf.Abs(ext.z) > 1e-4f)
                    ? new Vector3(Mathf.Abs(ext.x), Mathf.Abs(ext.y), Mathf.Abs(ext.z))
                    : Vector3.one * SoloBoxM;
                // Name must match Palace keep-list prefixes (3OS_SoloDrop* / 3OS_Voodoo*).
                DrawBox(ref _soloLine, "3OS_SoloDropFarBB", SoloIdle, InteropStructs.ToOpenXrPose(pose),
                    size);
                EnsurePalaceKeepQueue(_soloLine);
            }
            else if (_soloLine != null)
            {
                _soloLine.gameObject.SetActive(false);
            }

            _soloWasArmed = armed;
            if (ev == 3) // DROPPED
            {
                _host.MarkGhostsDirty();
                var dropId = st.targetId;
                if (dropId != 0 && _host.TryFindItem(dropId, out _))
                    Bridge.SetSelection(dropId);
                _host.MarkViewsDirty();
                _host.StatusField = "solo drop";
            }
            else if (ev == 4) // SELECTED (armed solo; kernel set selection)
            {
                _host.MarkViewsDirty();
                _host.StatusField = "solo select";
            }
        }

        internal void TickSolo(ThreeOSInputRouter input)
        {
            if (input == null) return;
            TickSoloPostKernel();
        }

        ulong ResolveSoloClearTarget(ThreeOSPose head, int candidateCount)
        {
            var clear = 0UL;
            var headPos = InteropStructs.OpenXrToUnityPosition(head.position.ToVector3());
            for (var i = 0; i < candidateCount && clear == 0; i++)
            {
                var id = _cands[i];
                if (_host.Glyphs.Views.TryGetValue(id, out var view) && view.Root != null)
                {
                    var isBox = _host.TryFindItem(id, out var item) && item.kind == 1u;
                    if (isBox || !Occluded(headPos, view, id))
                        clear = id;
                }
                else
                    clear = id;
            }
            return clear;
        }

        static ThreeOSVec3 SoloPalmPosition(InteropHandSample s)
        {
            var palmPos = s.palmPosition;
            if (palmPos.x == 0f && palmPos.y == 0f && palmPos.z == 0f)
                palmPos = s.indexProximal.x != 0f || s.indexProximal.y != 0f || s.indexProximal.z != 0f
                    ? s.indexProximal
                    : s.indexTip;
            return palmPos;
        }

        /// <summary>
        /// Occlusion for solo drop: ignore WorldMesh enclosure and the candidate's own
        /// folder contents (parent_box chain). Other far glyphs still block.
        /// </summary>
        bool Occluded(Vector3 from, ThreeOSGlyphView target, ulong candidateId)
        {
            var to = target.Root.transform.position;
            var dir = to - from;
            var dist = dir.magnitude;
            if (dist < 1e-4f) return false;
            var hits = Physics.RaycastAll(from, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore);
            for (var i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(target.Root.transform)) continue;
                if (IsWorldMeshCollider(h.collider)) continue;
                if (IsPalaceSkyCollider(h.collider)) continue;
                if (IsOwnFolderContent(h.collider, candidateId)) continue;
                return true;
            }
            return false;
        }

        bool IsOwnFolderContent(Collider col, ulong candidateId)
        {
            if (candidateId == 0 || _host == null) return false;
            foreach (var kv in _host.Glyphs.Views)
            {
                var view = kv.Value;
                if (view?.Root == null) continue;
                if (!col.transform.IsChildOf(view.Root.transform) && col.transform != view.Root.transform)
                    continue;
                // Walk parent_box chain up to the candidate folder.
                var id = kv.Key;
                for (var depth = 0; depth < 8 && id != 0; depth++)
                {
                    if (id == candidateId) return true;
                    if (!Bridge.TryGetParentBox(id, out var parent) || parent == 0)
                        break;
                    id = parent;
                }
                return false;
            }
            return false;
        }

        static bool IsWorldMeshCollider(Collider col)
        {
            for (var t = col.transform; t != null; t = t.parent)
            {
                var n = t.name;
                if (string.IsNullOrEmpty(n)) continue;
                if (n == "WorldMesh" || n.StartsWith("WorldMesh ", System.StringComparison.Ordinal) ||
                    n.StartsWith("WorldMesh-", System.StringComparison.Ordinal) ||
                    n.StartsWith("WorldMesh (", System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        static bool IsPalaceSkyCollider(Collider col)
        {
            for (var t = col.transform; t != null; t = t.parent)
            {
                var n = t.name;
                if (string.IsNullOrEmpty(n)) continue;
                if (n.IndexOf("PalaceSky", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        static void EnsurePalaceKeepQueue(LineRenderer lr)
        {
            if (lr == null) return;
            var mat = lr.sharedMaterial;
            if (mat != null && mat.renderQueue < KeepRenderQueue)
                mat.renderQueue = KeepRenderQueue;
            lr.sortingOrder = Mathf.Max(lr.sortingOrder, 50);
        }

        void DrawBox(ref LineRenderer lr, string name, Color color, ThreeOSPose xrPose, Vector3 size)
        {
            if (lr == null)
            {
                var go = new GameObject(name);
                lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.loop = false;
                lr.positionCount = 16;
                lr.widthMultiplier = 0.008f;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sortingOrder = 50;
                if (_lineMat == null)
                {
                    var sh = Shader.Find("Sprites/Default") ??
                             Shader.Find("Universal Render Pipeline/Unlit") ??
                             Shader.Find("Unlit/Color");
                    _lineMat = new Material(sh);
                    _lineMat.renderQueue = KeepRenderQueue;
                }
                lr.sharedMaterial = _lineMat;
            }

            lr.gameObject.SetActive(true);
            EnsurePalaceKeepQueue(lr);
            if (_lineMat != null)
            {
                _lineMat.renderQueue = KeepRenderQueue;
                _lineMat.color = color;
                if (_lineMat.HasProperty("_Color")) _lineMat.SetColor("_Color", color);
                if (_lineMat.HasProperty("_BaseColor")) _lineMat.SetColor("_BaseColor", color);
            }
            lr.startColor = lr.endColor = color;
            var p = InteropStructs.ToUnityPose(xrPose);
            var h = size * 0.5f;
            Vector3 C(int sx, int sy, int sz) =>
                p.position + p.rotation * new Vector3(sx * h.x, sy * h.y, sz * h.z);
            var pts = new[]
            {
                C(-1, -1, -1), C(1, -1, -1), C(1, -1, 1), C(-1, -1, 1), C(-1, -1, -1),
                C(-1, 1, -1), C(1, 1, -1), C(1, -1, -1), C(1, 1, -1), C(1, 1, 1), C(1, -1, 1),
                C(1, 1, 1), C(-1, 1, 1), C(-1, -1, 1), C(-1, 1, 1), C(-1, 1, -1),
            };
            lr.SetPositions(pts);
        }
    }
}
