using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ThreeOS
{
    internal sealed class ThreeOSDismissAffordances
    {
        private readonly ThreeOSStorageController _host;
        internal readonly Dictionary<ulong, ThreeOSPortalDismissUi> PortalDismiss =
            new Dictionary<ulong, ThreeOSPortalDismissUi>();
        private readonly ulong[] _xHoverPortalHand = new ulong[2];
        private readonly ulong[] _scissorsHoverPortalHand = new ulong[2];
        private static Mesh _scissorsGlyphMesh;
        private static bool _scissorsGlyphMeshAttempted;

        internal ThreeOSDismissAffordances(ThreeOSStorageController host) { _host = host; }

        private ThreeOSBridge _bridge => _host.Bridge;
        private ThreeOSInputRouter _input => _host.Input;
        private Dictionary<ulong, ThreeOSExpandedBoxUi> _expandedBoxes => _host.ExpandedBoxes;
        private Dictionary<ulong, ThreeOSGlyphView> _views => _host.Glyphs.Views;
        private bool _boxAnimBusy => _host.BoxAnimBusy;
        private bool _ghostDragging => _host.GhostDragging;
        private int _selectHand => _host.SelectHandField;
        private string _status { get => _host.StatusField; set => _host.StatusField = value; }
        private ulong _xHoverBoxId { get => _host.XHoverBoxId; set => _host.XHoverBoxId = value; }
        private float BoxDismissXOffsetM => _host.BoxDismissXOffsetM;
        private void Destroy(UnityEngine.Object o) { if (o != null) UnityEngine.Object.Destroy(o); }
        private static Material CreateTranslucentUnlitMaterial(Color color) =>
            ThreeOSGlyphViewRegistry.CreateTranslucentUnlitMaterial(color);
        private void ReleaseDragFromTrigger() => _host.Interaction.ReleaseDragFromTrigger();
        private Coroutine StartCoroutine(System.Collections.IEnumerator rut) =>
            _host.StartCoroutinePublic(rut);

        internal bool TryBeginCloseOnXHover()
        {
            // Portal X is handled per-hand in HandleTriggerSelectAndDrag.
            if (_boxAnimBusy || _xHoverBoxId == 0 || !_expandedBoxes.ContainsKey(_xHoverBoxId))
            {
                return false;
            }

            StartCoroutine(_host.Expanded.CloseBoxSequence(_xHoverBoxId));
            return true;
        }

        internal bool TryDismissHoveredPortal(int hand)
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

        internal void UpdatePortalDismissAffordances()
        {
            _xHoverPortalHand[0] = 0;
            _xHoverPortalHand[1] = 0;
            _scissorsHoverPortalHand[0] = 0;
            _scissorsHoverPortalHand[1] = 0;
            if (_bridge == null || !_bridge.IsLoaded || _bridge.NativeAbiVersion < 6 || _input == null)
            {
                ClearPortalDismissUi();
                return;
            }

            var n = (int)_bridge.VoodooPortalCount;
            var live = new HashSet<ulong>();
            var enterRadius = ThreeOSStorageController.DismissXDiameterM * 0.5f;
            var exitRadius = enterRadius * 2.25f;
            var cutPortal = _bridge.VoodooCutPortal;

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
                leftPos = _host.SelectAimUnity(out _).position;
                haveL = true;
            }

            for (var i = 0; i < n; i++)
            {
                if (!_bridge.TryGetVoodooPortal((uint)i, out var portal) || portal.nearBb.open == 0)
                {
                    continue;
                }

                live.Add(portal.id);
                if (!PortalDismiss.TryGetValue(portal.id, out var ui) || ui.XRoot == null)
                {
                    var xPos = PortalCloseButtonUnity(portal.nearBb, portal.creatingHand);
                    ui = new ThreeOSPortalDismissUi
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

                    PortalDismiss[portal.id] = ui;
                }

                var pos = PortalCloseButtonUnity(portal.nearBb, portal.creatingHand);
                // Cut button is parented in Unity space directly under the visible X (not a
                // second OpenXR edge solve) so it cannot drift / disappear relative to dismiss.
                var scPos = pos + Vector3.down * (ThreeOSStorageController.DismissXDiameterM * 1.85f);

                // Always ensure scissors chrome exists (upgrade path if only X was created).
                if (ui.ScissorsRoot == null)
                {
                    try
                    {
                        ui.ScissorsRoot = CreateScissorsButton(scPos);
                        if (ui.ScissorsRoot != null)
                        {
                            ui.ScissorsRoot.name = $"3OS_PortalScissors_{portal.id}";
                            var sVis = ui.ScissorsRoot.transform.Find("ScissorsVisual");
                            ui.ScissorsVisual = sVis != null ? sVis : ui.ScissorsRoot.transform;
                            Debug.Log($"[3OS] created portal scissors button for portal {portal.id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[3OS] CreateScissorsButton failed: {ex}");
                        ui.ScissorsRoot = null;
                        ui.ScissorsVisual = null;
                    }
                }

                ui.XRoot.transform.position = pos;
                var cam = Camera.main;
                if (cam != null)
                {
                    var xAway = cam.transform.position - pos;
                    if (xAway.sqrMagnitude > 1e-8f)
                    {
                        ui.XRoot.transform.rotation =
                            Quaternion.LookRotation(xAway.normalized, Vector3.up);
                    }
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
                    var lim = exitRadius;
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

                // Scissors cut button — just below X.
                if (ui.ScissorsRoot != null)
                {
                    ui.ScissorsRoot.transform.position = scPos;
                    ui.ScissorsRoot.SetActive(true);
                    if (cam != null)
                    {
                        var sAway = cam.transform.position - scPos;
                        if (sAway.sqrMagnitude > 1e-8f)
                        {
                            ui.ScissorsRoot.transform.rotation =
                                Quaternion.LookRotation(sAway.normalized, Vector3.up);
                        }
                    }

                    var sDistL = haveL ? Vector3.Distance(leftPos, scPos) : float.MaxValue;
                    var sDistR = haveR ? Vector3.Distance(rightPos, scPos) : float.MaxValue;
                    var sDist = Mathf.Min(sDistL, sDistR);
                    if (ui.ScissorsHovered)
                    {
                        ui.ScissorsHovered = sDist <= exitRadius;
                    }
                    else
                    {
                        ui.ScissorsHovered = sDist <= enterRadius;
                    }

                    // Prefer X when both overlap; scissors only if X not hovered this frame.
                    if (ui.ScissorsHovered && !ui.Hovered)
                    {
                        var lim = exitRadius;
                        if (sDistL <= lim)
                        {
                            _scissorsHoverPortalHand[0] = portal.id;
                        }

                        if (sDistR <= lim)
                        {
                            _scissorsHoverPortalHand[1] = portal.id;
                        }
                    }

                    if (ui.ScissorsVisual != null)
                    {
                        var armed = cutPortal == portal.id;
                        var scale = ui.ScissorsHovered || armed ? 2f : 1f;
                        ui.ScissorsVisual.localScale = Vector3.one * scale;
                    }
                }
            }

            // Tear down chrome for portals that were dismissed / no longer reported.
            var stale = new List<ulong>();
            foreach (var kv in PortalDismiss)
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
        internal static Vector3 PortalCloseButtonUnity(in InteropVoodooBoundingBox bb,
            uint creatingHand)
        {
            return PortalEdgeButtonUnity(bb, creatingHand, yOffsetM: 0f);
        }

        /// <summary>Scissors cut button — same edge as X, just below.</summary>
        internal static Vector3 PortalScissorsButtonUnity(in InteropVoodooBoundingBox bb,
            uint creatingHand)
        {
            // Clear gap under X so the cut control is obvious on-device.
            return PortalEdgeButtonUnity(bb, creatingHand, yOffsetM: -(ThreeOSStorageController.DismissXDiameterM * 1.75f));
        }

        internal static Vector3 PortalEdgeButtonUnity(in InteropVoodooBoundingBox bb,
            uint creatingHand, float yOffsetM)
        {
            var pad = ThreeOSStorageController.DismissXDiameterM * 0.35f;
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
                bb.pose.position.y + yOffsetM,
                bb.pose.position.z + wz);
            return InteropStructs.OpenXrToUnityPosition(openXr);
        }

        internal bool TryCutHoveredPortal(int hand)
        {
            if (hand < 0 || hand > 1 || _bridge == null || !_bridge.IsLoaded)
            {
                return false;
            }

            var pid = _scissorsHoverPortalHand[hand];
            if (pid == 0)
            {
                return false;
            }

            if (_ghostDragging && _selectHand == hand)
            {
                ReleaseDragFromTrigger();
            }

            if (!_bridge.VoodooCutArm(pid))
            {
                return false;
            }

            _status = hand == 0 ? "cut armed (L)" : "cut armed (R)";
            return true;
        }

        internal void DestroyPortalDismissUi(ulong portalId)
        {
            if (!PortalDismiss.TryGetValue(portalId, out var ui))
            {
                return;
            }

            if (ui.XRoot != null)
            {
                Destroy(ui.XRoot);
            }

            if (ui.ScissorsRoot != null)
            {
                Destroy(ui.ScissorsRoot);
            }

            PortalDismiss.Remove(portalId);
        }

        internal void ClearPortalDismissUi()
        {
            foreach (var kv in PortalDismiss)
            {
                if (kv.Value.XRoot != null)
                {
                    Destroy(kv.Value.XRoot);
                }

                if (kv.Value.ScissorsRoot != null)
                {
                    Destroy(kv.Value.ScissorsRoot);
                }
            }

            PortalDismiss.Clear();
            _xHoverPortalHand[0] = 0;
            _xHoverPortalHand[1] = 0;
            _scissorsHoverPortalHand[0] = 0;
            _scissorsHoverPortalHand[1] = 0;
        }
        internal void UpdateExpandedDismissAffordances()
        {
            _xHoverBoxId = 0;
            if (_expandedBoxes.Count == 0 || _input == null)
            {
                return;
            }

            // Test BOTH point cursors so left hand can hover/close X (not right-only).
            var enterRadius = ThreeOSStorageController.DismissXDiameterM * 0.5f;
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
                var aimUnity = _host.SelectAimUnity(out _);
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
        internal static GameObject CreateDismissX(Vector3 worldPos)
        {
            var root = new GameObject("3OS_BoxDismissX");
            root.transform.position = worldPos;

            var visual = new GameObject("XVisual");
            visual.transform.SetParent(root.transform, false);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Sphere";
            sphere.transform.SetParent(visual.transform, false);
            sphere.transform.localScale = Vector3.one * ThreeOSStorageController.DismissXDiameterM;
            var sphereCol = sphere.GetComponent<Collider>();
            if (sphereCol != null)
            {
                UnityEngine.Object.Destroy(sphereCol);
            }

            var sphereMr = sphere.GetComponent<MeshRenderer>();
            // Translucent shell under chrome (queue 5100 / sort 0). X bars draw later.
            sphereMr.sharedMaterial = CreateTranslucentUnlitMaterial(new Color(0.72f, 0.72f, 0.78f, 0.28f));
            sphereMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sphereMr.receiveShadows = false;
            sphereMr.allowOcclusionWhenDynamic = false;
            sphereMr.sortingOrder = 0;
            sphereMr.enabled = true;

            CreateXBar(visual.transform, 45f);
            CreateXBar(visual.transform, -45f);
            return root;
        }

        internal static GameObject CreateScissorsButton(Vector3 worldPos)
        {
            // Same construction path as CreateDismissX (proven on Quest). Glyph mesh is optional
            // and never blocks the sphere from spawning.
            var root = new GameObject("3OS_PortalScissors");
            root.transform.position = worldPos;

            var visual = new GameObject("ScissorsVisual");
            visual.transform.SetParent(root.transform, false);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Sphere";
            sphere.transform.SetParent(visual.transform, false);
            sphere.transform.localScale = Vector3.one * (ThreeOSStorageController.DismissXDiameterM * 1.1f);
            var sphereCol = sphere.GetComponent<Collider>();
            if (sphereCol != null)
            {
                UnityEngine.Object.Destroy(sphereCol);
            }

            var sphereMr = sphere.GetComponent<MeshRenderer>();
            // Translucent cyan shell under scissors chrome (queue 5100 / sort 0).
            sphereMr.sharedMaterial = CreateTranslucentUnlitMaterial(new Color(0.15f, 0.85f, 1f, 0.32f));
            sphereMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sphereMr.receiveShadows = false;
            sphereMr.allowOcclusionWhenDynamic = false;
            sphereMr.sortingOrder = 0;
            sphereMr.enabled = true;

            try
            {
                var mesh = EnsureScissorsGlyphMesh();
                if (mesh != null)
                {
                    var glyph = new GameObject("ScissorsGlyph");
                    glyph.transform.SetParent(visual.transform, false);
                    var mf = glyph.AddComponent<MeshFilter>();
                    var mr = glyph.AddComponent<MeshRenderer>();
                    mf.sharedMesh = mesh;
                    var maxDim = Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z);
                    var target = ThreeOSStorageController.DismissXDiameterM * 0.72f;
                    var s = maxDim > 1e-5f ? target / maxDim : 1f;
                    glyph.transform.localScale = Vector3.one * s;
                    // Model is authored blade-along-Y; billboard faces camera on +Z — pitch
                    // so the open scissors silhouette reads face-on instead of tip-on.
                    glyph.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    var glyphMat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ??
                                                Shader.Find("Universal Render Pipeline/Unlit"));
                    if (glyphMat != null)
                    {
                        var c = new Color(0.08f, 0.08f, 0.1f, 1f);
                        if (glyphMat.HasProperty("_Color"))
                        {
                            glyphMat.SetColor("_Color", c);
                        }

                        if (glyphMat.HasProperty("_BaseColor"))
                        {
                            glyphMat.SetColor("_BaseColor", c);
                        }

                        // After shell (5100) so blades stay fully readable.
                        glyphMat.renderQueue = 5120;
                        mr.sharedMaterial = glyphMat;
                    }

                    mr.sortingOrder = 30;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.allowOcclusionWhenDynamic = false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[3OS] scissors mesh skipped: {ex.Message}");
            }

            return root;
        }

        internal static Mesh EnsureScissorsGlyphMesh()
        {
            if (_scissorsGlyphMeshAttempted)
            {
                return _scissorsGlyphMesh;
            }

            _scissorsGlyphMeshAttempted = true;
            try
            {
                byte[] bytes = null;
                // Resources works on Quest player builds (StreamingAssets is jar-only async).
                var ta = Resources.Load<TextAsset>("3OS/glyphs/scissors");
                if (ta != null && ta.bytes != null && ta.bytes.Length > 0)
                {
                    bytes = ta.bytes;
                }

                if (bytes == null)
                {
                    var path = Path.Combine(Application.streamingAssetsPath, "3OS", "glyphs",
                        "scissors.3glyph");
                    if (File.Exists(path))
                    {
                        bytes = File.ReadAllBytes(path);
                    }
                }

                if (bytes != null)
                {
                    if (ThreeOSGlbMesh.TryLoadFirstMesh(bytes, out var mesh, out var err))
                    {
                        _scissorsGlyphMesh = mesh;
                        Debug.Log($"[3OS] loaded scissors glyph mesh ({mesh.vertexCount} verts)");
                    }
                    else
                    {
                        Debug.LogWarning($"[3OS] scissors glyph parse failed: {err}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[3OS] scissors glyph load exception: {ex.Message}");
                _scissorsGlyphMesh = null;
            }

            return _scissorsGlyphMesh;
        }

        internal static void CreateXBar(Transform parent, float zAngle)
        {
            var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "XBar";
            bar.transform.SetParent(parent, false);
            // Scaled with ThreeOSStorageController.DismissXDiameterM (baseline was 0.02 m → 0.016 / 0.0022 bars).
            var s = ThreeOSStorageController.DismissXDiameterM / 0.02f;
            bar.transform.localScale = new Vector3(0.016f * s, 0.0022f * s, 0.0022f * s);
            bar.transform.localRotation = Quaternion.Euler(0f, 0f, zAngle);
            var col = bar.GetComponent<Collider>();
            if (col != null)
            {
                UnityEngine.Object.Destroy(col);
            }

            var mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ??
                                   Shader.Find("Universal Render Pipeline/Unlit"));
            var barMr = bar.GetComponent<MeshRenderer>();
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

                // After shell (5100) so the X stays fully on top of the translucent sphere.
                mat.renderQueue = 5120;
                barMr.sharedMaterial = mat;
            }

            barMr.sortingOrder = 30;
            barMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            barMr.receiveShadows = false;
            barMr.allowOcclusionWhenDynamic = false;
        }

    }
}
