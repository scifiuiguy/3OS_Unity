using System;
using System.Collections.Generic;
using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Phase 0.6: dual-hand wireframe far/near voodoo BBs, yellow drag arrows, snap-mode dots.
    /// Publishes named WorldMesh* colliders as solid enclosure OBBs before tick.
    /// </summary>
    // -150: Update publishes WorldMesh boxes before InputRouter tick (-100).
    [DefaultExecutionOrder(-150)]
    public sealed class ThreeOSVoodooController : MonoBehaviour
    {
        private const float DeadzoneM = 0.015f;
        private const float FarWireWidthM = 0.008f;
        private const string WorldMeshPrefix = "WorldMesh";
        private const int MaxEnclosureBoxes = 64;
        private static readonly Color FarWire = new Color(1f, 0.85f, 0.2f, 0.5f);
        private static readonly Color NearWire = new Color(0.4f, 0.85f, 1f, 0.5f);
        private static readonly Color SnapIdle = new Color(1f, 0.92f, 0.35f, 0.75f);
        private static readonly Color SnapLocked = new Color(1f, 0.55f, 0.1f, 0.95f);

        private ThreeOSBridge _bridge;
        private readonly HandViz[] _hands = new HandViz[2];
        private Material _matSnapIdle;
        private Material _matSnapLocked;
        private readonly List<Transform> _portalFar = new List<Transform>();
        private readonly List<LineRenderer> _portalFarLr = new List<LineRenderer>();
        private readonly List<Transform> _portalNear = new List<Transform>();
        private readonly List<LineRenderer> _portalNearLr = new List<LineRenderer>();
        private readonly List<Collider> _worldMeshColliders = new List<Collider>();
        private float _nextMeshRefresh = -1f;
        private readonly List<InteropVoodooEnclosureBox> _boxScratch =
            new List<InteropVoodooEnclosureBox>(MaxEnclosureBoxes);

        private sealed class HandViz
        {
            public ThreeOSArrowVisual Arrow;
            public ThreeOSArrowVisual GhostArrow;
            public Transform FarRoot;
            public Transform NearRoot;
            public LineRenderer FarLines;
            public LineRenderer NearLines;
            public readonly List<Transform> SnapDots = new List<Transform>();
            public bool LoggedOpen;
        }

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
            _matSnapIdle = CreateLineMaterial(SnapIdle);
            _matSnapLocked = CreateLineMaterial(SnapLocked);
            for (var h = 0; h < 2; h++)
            {
                _hands[h] = new HandViz
                {
                    Arrow = ThreeOSArrowVisual.Create($"3OS_VoodooDragArrow_{(h == 0 ? "L" : "R")}", 0.008f),
                    GhostArrow = ThreeOSArrowVisual.Create(
                        $"3OS_GhostDragArrow_{(h == 0 ? "L" : "R")}", 0.008f),
                    FarRoot = CreateWireBox($"3OS_VoodooFarBB_{(h == 0 ? "L" : "R")}", FarWire, out var farLr),
                    NearRoot = CreateWireBox($"3OS_VoodooNearBB_{(h == 0 ? "L" : "R")}", NearWire, out var nearLr),
                };
                _hands[h].FarLines = farLr;
                _hands[h].NearLines = nearLr;
            }

            // Final Demo MR: probe room AABB and flex voodoo field to room diagonal.
            if (IsFinalDemoScene() && FindFirstObjectByType<ThreeOSMrRoomProbe>() == null)
            {
                var host = _bridge != null ? _bridge.gameObject : gameObject;
                if (host.GetComponent<ThreeOSMrRoomProbe>() == null)
                    host.AddComponent<ThreeOSMrRoomProbe>();
            }
        }

        private static bool IsFinalDemoScene()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var mb = behaviours[i];
                if (mb != null && mb.GetType().Name == "ThreeOSFinalDemoStage")
                    return true;
            }

            return false;
        }

        private void OnDestroy()
        {
            if (_matSnapIdle != null) Destroy(_matSnapIdle);
            if (_matSnapLocked != null) Destroy(_matSnapLocked);
        }

        private void Update()
        {
            // Before InputRouter tick (-100): publish enclosure so kernel clamps far BB
            // during drag and on the release/park frame.
            if (_bridge == null || !_bridge.IsLoaded || _bridge.NativeAbiVersion < 6)
                return;
            PublishWorldMeshBoxes();
        }

        private void LateUpdate()
        {
            if (_bridge == null || !_bridge.IsLoaded || _bridge.NativeAbiVersion < 6)
            {
                HideAll();
                return;
            }

            var snapArmed = _bridge.TryGetSnap(out var snap) && snap.pending != 0;

            for (uint h = 0; h < 2; h++)
            {
                UpdateHand(h);
                UpdateGhostDragSticks(h, _hands[h], snapArmed);
            }

            UpdatePortals();
        }

        private void PublishWorldMeshBoxes()
        {
            if (Time.unscaledTime >= _nextMeshRefresh)
            {
                RefreshWorldMeshColliders();
                _nextMeshRefresh = Time.unscaledTime + 1f;
            }

            _boxScratch.Clear();
            for (var i = 0; i < _worldMeshColliders.Count; i++)
            {
                var col = _worldMeshColliders[i];
                if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                    continue;
                if (!TryColliderObb(col, out var center, out var half, out var rot))
                    continue;

                var oxPose = InteropStructs.ToOpenXrPose(new Pose(center, rot));
                // Half-extents are lengths along local axes (sign flip from LH↔RH is irrelevant).
                var oxHalf = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));
                _boxScratch.Add(new InteropVoodooEnclosureBox
                {
                    center = oxPose.position,
                    halfExtents = oxHalf,
                    orientation = oxPose.orientation,
                    kind = 0
                });
                if (_boxScratch.Count >= MaxEnclosureBoxes)
                    break;
            }

            if (_boxScratch.Count == 0)
                _bridge.SetVoodooEnclosureBoxes(null);
            else
                _bridge.SetVoodooEnclosureBoxes(_boxScratch.ToArray());
        }

        private void RefreshWorldMeshColliders()
        {
            _worldMeshColliders.Clear();
            var cols = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            for (var i = 0; i < cols.Length; i++)
            {
                var col = cols[i];
                if (col == null)
                    continue;
                if (IsWorldMeshName(col.gameObject.name))
                    _worldMeshColliders.Add(col);
            }
        }

        /// <summary>
        /// Any GO named WorldMesh, WorldMesh (N), WorldMesh-Ceiling, WorldMesh-Floor, etc.
        /// </summary>
        private static bool IsWorldMeshName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            if (n == WorldMeshPrefix)
                return true;
            if (n.StartsWith(WorldMeshPrefix + " (", StringComparison.Ordinal) && n.EndsWith(")"))
                return true;
            if (n.StartsWith(WorldMeshPrefix + "-", StringComparison.Ordinal))
                return true;
            return false;
        }

        /// <summary>
        /// Finite OBB for a collider. BoxCollider keeps orientation; others use world AABB.
        /// </summary>
        private static bool TryColliderObb(Collider col, out Vector3 center, out Vector3 halfExtents,
            out Quaternion rotation)
        {
            center = default;
            halfExtents = default;
            rotation = Quaternion.identity;
            if (col is BoxCollider box)
            {
                var t = col.transform;
                center = t.TransformPoint(box.center);
                rotation = t.rotation;
                var lossy = t.lossyScale;
                halfExtents = new Vector3(
                    Mathf.Abs(box.size.x * lossy.x) * 0.5f,
                    Mathf.Abs(box.size.y * lossy.y) * 0.5f,
                    Mathf.Abs(box.size.z * lossy.z) * 0.5f);
            }
            else
            {
                var b = col.bounds;
                center = b.center;
                halfExtents = b.extents;
                rotation = Quaternion.identity;
            }

            return halfExtents.x > 1e-5f || halfExtents.y > 1e-5f || halfExtents.z > 1e-5f;
        }

        private bool UpdateHand(uint hand)
        {
            var viz = _hands[hand];
            if (!_bridge.TryGetVoodooSession(hand, out var session) || session.open == 0)
            {
                if (viz.LoggedOpen)
                {
                    Debug.Log($"[3OS] voodoo hand={hand} session closed");
                    viz.LoggedOpen = false;
                }

                HideHand(viz);
                return false;
            }

            if (!viz.LoggedOpen)
            {
                Debug.Log(
                    $"[3OS] voodoo hand={hand} open mode={session.mode} " +
                    $"far={session.farBb.open} near={session.nearBb.open} " +
                    $"ext=({session.farBb.extents.x:F2},{session.farBb.extents.y:F2},{session.farBb.extents.z:F2})");
                viz.LoggedOpen = true;
            }

            // Dragging: show live far BB. Parked portals are drawn from portal list.
            var dragging = session.nearBb.open == 0;
            if (dragging)
            {
                // Focus-anchor lock: thicken far wire so the snap reads clearly.
                var farWidth = IsSnappedToFocusAnchor(hand) ? FarWireWidthM * 2f : FarWireWidthM;
                UpdateWireBox(viz.FarRoot, viz.FarLines, session.farBb, farWidth);
            }
            else if (viz.FarRoot != null)
            {
                viz.FarRoot.gameObject.SetActive(false);
            }

            if (viz.NearRoot != null)
            {
                viz.NearRoot.gameObject.SetActive(false);
            }

            // Ghost-drag velocity sticks are drawn in UpdateGhostDragSticks.
            if (!_bridge.GhostDragActive(hand))
            {
                UpdateYellowArrow(hand, viz);
            }
            else
            {
                viz.Arrow?.Hide();
            }

            UpdateSnapDots(hand, viz, session.mode == (uint)VoodooMode.Snap && dragging);
            return true;
        }

        private void UpdatePortals()
        {
            var n = (int)_bridge.VoodooPortalCount;
            EnsurePortalWireCount(n);
            for (var i = 0; i < n; i++)
            {
                if (!_bridge.TryGetVoodooPortal((uint)i, out var portal))
                {
                    _portalFar[i].gameObject.SetActive(false);
                    _portalNear[i].gameObject.SetActive(false);
                    continue;
                }

                UpdateWireBox(_portalFar[i], _portalFarLr[i], portal.farBb, FarWireWidthM);
                UpdateWireBox(_portalNear[i], _portalNearLr[i], portal.nearBb, FarWireWidthM);
            }

            for (var i = n; i < _portalFar.Count; i++)
            {
                _portalFar[i].gameObject.SetActive(false);
                _portalNear[i].gameObject.SetActive(false);
            }
        }

        private void EnsurePortalWireCount(int n)
        {
            while (_portalFar.Count < n)
            {
                var i = _portalFar.Count;
                _portalFar.Add(CreateWireBox($"3OS_VoodooPortalFar_{i}", FarWire, out var farLr));
                _portalFarLr.Add(farLr);
                _portalNear.Add(CreateWireBox($"3OS_VoodooPortalNear_{i}", NearWire, out var nearLr));
                _portalNearLr.Add(nearLr);
            }
        }

        private void UpdateSnapDots(uint hand, HandViz viz, bool snapMode)
        {
            if (!snapMode)
            {
                HideSnapDots(viz);
                return;
            }

            var n = (int)_bridge.VoodooSnapNodeCount(hand);
            EnsureSnapDotCount(viz, hand, n);
            for (var i = 0; i < n; i++)
            {
                if (!_bridge.TryGetVoodooSnapNode(hand, (uint)i, out var node))
                {
                    viz.SnapDots[i].gameObject.SetActive(false);
                    continue;
                }

                var t = viz.SnapDots[i];
                t.position = InteropStructs.OpenXrToUnityPosition(node.worldPosition.ToVector3());
                // Visual only ΓÇö kernel snap radius (gravity) stays unchanged.
                var r = Mathf.Max(0.01f, node.radiusM);
                var scale = (node.snapped != 0 ? r * 2.4f : r * 2f) / 3f;
                t.localScale = Vector3.one * scale;
                var mr = t.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sharedMaterial = node.snapped != 0 ? _matSnapLocked : _matSnapIdle;
                }

                t.gameObject.SetActive(true);
            }

            for (var i = n; i < viz.SnapDots.Count; i++)
            {
                viz.SnapDots[i].gameObject.SetActive(false);
            }
        }

        private void EnsureSnapDotCount(HandViz viz, uint hand, int n)
        {
            while (viz.SnapDots.Count < n)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"3OS_VoodooSnap_{(hand == 0 ? "L" : "R")}_{viz.SnapDots.Count}";
                foreach (var col in go.GetComponents<Collider>())
                {
                    Destroy(col);
                }

                go.SetActive(false);
                viz.SnapDots.Add(go.transform);
            }
        }

        private static void HideSnapDots(HandViz viz)
        {
            foreach (var t in viz.SnapDots)
            {
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        private bool IsSnappedToFocusAnchor(uint hand)
        {
            var n = (int)_bridge.VoodooSnapNodeCount(hand);
            for (var i = 0; i < n; i++)
            {
                if (!_bridge.TryGetVoodooSnapNode(hand, (uint)i, out var node))
                    continue;
                if (node.snapped != 0 && node.kind == (uint)CentroidKind.FocusAnchor)
                    return true;
            }

            return false;
        }

        private void UpdateWireBox(Transform root, LineRenderer lr, InteropVoodooBoundingBox bb,
            float lineWidthM)
        {
            if (root == null || lr == null)
            {
                return;
            }

            if (bb.open == 0)
            {
                root.gameObject.SetActive(false);
                return;
            }

            var pose = InteropStructs.ToUnityPose(bb.pose);
            var extents = InteropStructs.OpenXrToUnityPosition(bb.extents.ToVector3());
            root.SetPositionAndRotation(pose.position, pose.rotation);
            root.localScale = Vector3.one;
            lr.widthMultiplier = lineWidthM;
            SetWireCubePositions(lr, new Vector3(
                Mathf.Abs(extents.x) * 0.5f,
                Mathf.Abs(extents.y) * 0.5f,
                Mathf.Abs(extents.z) * 0.5f));
            root.gameObject.SetActive(true);
        }

        private void UpdateYellowArrow(uint hand, HandViz viz)
        {
            if (viz.Arrow == null || !_bridge.TryGetVoodooDragArrow(hand, out var arrow) ||
                arrow.active == 0)
            {
                viz.Arrow?.Hide();
                return;
            }

            DrawVelocityArrow(viz.Arrow, arrow, new Color(1f, 0.85f, 0.15f, 0.95f),
                new Color(1f, 0.9f, 0.2f, 0.25f), new Color(1f, 0.92f, 0.35f, 0.75f));
        }

        private void UpdateBlueGhostArrow(uint hand, HandViz viz)
        {
            if (viz.GhostArrow == null || !_bridge.TryGetGhostDragArrow(hand, out var arrow) ||
                arrow.active == 0)
            {
                viz.GhostArrow?.Hide();
                return;
            }

            DrawVelocityArrow(viz.GhostArrow, arrow, new Color(0.35f, 0.75f, 1f, 0.95f),
                new Color(0.4f, 0.8f, 1f, 0.25f), new Color(0.55f, 0.88f, 1f, 0.75f));
        }

        private static void DrawVelocityArrow(ThreeOSArrowVisual visual, InteropVoodooDragArrow arrow,
            Color shaft, Color deadzoneSphere, Color deadzoneGhost)
        {
            var from = InteropStructs.OpenXrToUnityPosition(arrow.rayOrigin.ToVector3());
            var to = InteropStructs.OpenXrToUnityPosition(arrow.rayTip.ToVector3());
            if (arrow.deadzoneArmed != 0 && arrow.inDeadzone != 0)
            {
                visual.SetDeadzone(from, to, deadzoneSphere, deadzoneGhost, DeadzoneM * 2f);
            }
            else
            {
                visual.Set(from, to, shaft);
            }
        }

        /// <summary>
        /// Ghost-drag velocity sticks: none on direct grab; static snap arrow is separate.
        /// Near BB: blue (near) + yellow (far). Outside near BB: yellow far only.
        /// </summary>
        private void UpdateGhostDragSticks(uint hand, HandViz viz, bool snapArmed)
        {
            viz.GhostArrow?.Hide();
            if (!_bridge.GhostDragActive(hand))
            {
                return;
            }

            if (snapArmed || !_bridge.TryGetGhostDragArrow(hand, out var policy))
            {
                viz.Arrow?.Hide();
                return;
            }

            // reserved1 = direct_follow, reserved2 = near_stick_visible (kernel ABI).
            if (policy.reserved1 != 0)
            {
                viz.Arrow?.Hide();
                return;
            }

            UpdateYellowArrow(hand, viz);
            if (policy.reserved2 != 0)
            {
                UpdateBlueGhostArrow(hand, viz);
            }
        }

        private void HideAll()
        {
            for (var h = 0; h < 2; h++)
            {
                HideHand(_hands[h]);
            }

            foreach (var t in _portalFar)
            {
                if (t != null) t.gameObject.SetActive(false);
            }

            foreach (var t in _portalNear)
            {
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        private static void HideHand(HandViz viz)
        {
            if (viz == null)
            {
                return;
            }

            viz.Arrow?.Hide();
            viz.GhostArrow?.Hide();
            HideSnapDots(viz);
            if (viz.FarRoot != null)
            {
                viz.FarRoot.gameObject.SetActive(false);
            }

            if (viz.NearRoot != null)
            {
                viz.NearRoot.gameObject.SetActive(false);
            }
        }

        private static Transform CreateWireBox(string name, Color color, out LineRenderer lr)
        {
            var existing = GameObject.Find(name);
            if (existing != null)
            {
                Destroy(existing);
            }

            var go = new GameObject(name);
            lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.positionCount = 16; // 12 edges as 16 verts with shared corners walk
            lr.widthMultiplier = FarWireWidthM;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.material = CreateLineMaterial(color);
            lr.startColor = color;
            lr.endColor = color;
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>
        /// Unit half-extents box as a continuous line path covering all 12 edges.
        /// </summary>
        private static void SetWireCubePositions(LineRenderer lr, Vector3 half)
        {
            // Walk: bottom square, up, top square, then verticals via extra segments.
            var p = new Vector3[16];
            var i = 0;
            // Bottom ring
            p[i++] = new Vector3(-half.x, -half.y, -half.z);
            p[i++] = new Vector3(half.x, -half.y, -half.z);
            p[i++] = new Vector3(half.x, -half.y, half.z);
            p[i++] = new Vector3(-half.x, -half.y, half.z);
            p[i++] = new Vector3(-half.x, -half.y, -half.z);
            // Up to top front-left, top ring
            p[i++] = new Vector3(-half.x, half.y, -half.z);
            p[i++] = new Vector3(half.x, half.y, -half.z);
            p[i++] = new Vector3(half.x, half.y, half.z);
            p[i++] = new Vector3(-half.x, half.y, half.z);
            p[i++] = new Vector3(-half.x, half.y, -half.z);
            // Drop missing verticals: BR bottomΓåÉtop, then FR, then BL already done
            p[i++] = new Vector3(half.x, half.y, -half.z);
            p[i++] = new Vector3(half.x, -half.y, -half.z);
            p[i++] = new Vector3(half.x, -half.y, half.z);
            p[i++] = new Vector3(half.x, half.y, half.z);
            p[i++] = new Vector3(-half.x, half.y, half.z);
            p[i++] = new Vector3(-half.x, -half.y, half.z);
            lr.positionCount = 16;
            lr.SetPositions(p);
        }

        private static Material CreateLineMaterial(Color c)
        {
            // Prefer Sprites/Default so LineRenderer alpha is respected on Quest/URP.
            var shader = Shader.Find("Sprites/Default") ??
                         Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            mat.color = c;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", c);
            }

            mat.renderQueue = 3000;
            return mat;
        }
    }
}
