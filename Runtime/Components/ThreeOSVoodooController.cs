using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Phase 0.6: dual-hand wireframe far/near voodoo BBs + yellow drag arrows.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public sealed class ThreeOSVoodooController : MonoBehaviour
    {
        private const float DeadzoneM = 0.015f;
        private static readonly Color FarWire = new Color(1f, 0.85f, 0.2f, 0.95f);
        private static readonly Color NearWire = new Color(0.4f, 0.85f, 1f, 0.95f);

        private ThreeOSBridge _bridge;
        private readonly HandViz[] _hands = new HandViz[2];

        private sealed class HandViz
        {
            public ThreeOSArrowVisual Arrow;
            public Transform FarRoot;
            public Transform NearRoot;
            public LineRenderer FarLines;
            public LineRenderer NearLines;
            public bool LoggedOpen;
        }

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
            for (var h = 0; h < 2; h++)
            {
                _hands[h] = new HandViz
                {
                    Arrow = ThreeOSArrowVisual.Create($"3OS_VoodooDragArrow_{(h == 0 ? "L" : "R")}", 0.008f),
                    FarRoot = CreateWireBox($"3OS_VoodooFarBB_{(h == 0 ? "L" : "R")}", FarWire, out var farLr),
                    NearRoot = CreateWireBox($"3OS_VoodooNearBB_{(h == 0 ? "L" : "R")}", NearWire, out var nearLr),
                };
                _hands[h].FarLines = farLr;
                _hands[h].NearLines = nearLr;
            }
        }

        private void LateUpdate()
        {
            if (_bridge == null || !_bridge.IsLoaded || _bridge.NativeAbiVersion < 6)
            {
                HideAll();
                return;
            }

            var anyOpen = false;
            for (uint h = 0; h < 2; h++)
            {
                anyOpen |= UpdateHand(h);
            }

            if (!anyOpen)
            {
                // keep quiet
            }
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

            UpdateWireBox(viz.FarRoot, viz.FarLines, session.farBb);
            UpdateWireBox(viz.NearRoot, viz.NearLines, session.nearBb);
            UpdateYellowArrow(hand, viz);
            return true;
        }

        private void UpdateWireBox(Transform root, LineRenderer lr, InteropVoodooBoundingBox bb)
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

            var from = InteropStructs.OpenXrToUnityPosition(arrow.rayOrigin.ToVector3());
            var to = InteropStructs.OpenXrToUnityPosition(arrow.rayTip.ToVector3());
            var yellow = new Color(1f, 0.85f, 0.15f, 0.95f);
            if (arrow.deadzoneArmed != 0 && arrow.inDeadzone != 0)
            {
                var sphere = new Color(1f, 0.9f, 0.2f, 0.25f);
                var ghost = new Color(1f, 0.92f, 0.35f, 0.75f);
                viz.Arrow.SetDeadzone(from, to, sphere, ghost, DeadzoneM * 2f);
            }
            else
            {
                viz.Arrow.Set(from, to, yellow);
            }
        }

        private void HideAll()
        {
            for (var h = 0; h < 2; h++)
            {
                HideHand(_hands[h]);
            }
        }

        private static void HideHand(HandViz viz)
        {
            if (viz == null)
            {
                return;
            }

            viz.Arrow?.Hide();
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
            lr.widthMultiplier = 0.008f;
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
            // Drop missing verticals: BR bottom←top, then FR, then BL already done
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
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Sprites/Default");
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
