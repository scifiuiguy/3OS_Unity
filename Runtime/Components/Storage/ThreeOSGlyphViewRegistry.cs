using System.Collections.Generic;
using UnityEngine;

namespace ThreeOS
{
    internal sealed class ThreeOSGlyphViewRegistry
    {
        internal readonly Dictionary<ulong, ThreeOSGlyphView> Views = new Dictionary<ulong, ThreeOSGlyphView>();
        internal readonly Dictionary<ulong, ThreeOSGlyphView> GhostViews = new Dictionary<ulong, ThreeOSGlyphView>();
        private readonly ThreeOSStorageController _host;
        private Material _sharedBoxMaterial;
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
        private static readonly Dictionary<string, Mesh> GlyphMeshCache = new Dictionary<string, Mesh>();

        internal ThreeOSGlyphViewRegistry(ThreeOSStorageController host)
        {
            _host = host;
            _glyphShaderTemplate = Resources.Load<Material>("3OS/GlyphDefault");
            if (_glyphShaderTemplate == null || _glyphShaderTemplate.shader == null ||
                _glyphShaderTemplate.shader.name != "ThreeOS/Glyph")
            {
                Debug.LogError(
                    "[3OS] Missing Resources/3OS/GlyphDefault.mat (ThreeOS/Glyph). " +
                    "Selection Fresnel will not render on device.");
            }
        }

        private ThreeOSBridge _bridge => _host.Bridge;
        private ThreeOSGlyphGlowSettings GlyphGlowSettings => _host.GlyphGlowSettings;
        private HashSet<ulong> _animLockedIds => _host.AnimLockedIds;
        private Dictionary<ulong, ThreeOSGlyphView> _views => Views;
        private Dictionary<ulong, ThreeOSGlyphView> _ghostViews => GhostViews;
        private Dictionary<ulong, ThreeOSExpandedBoxUi> _expandedBoxes => _host.ExpandedBoxes;
        private ulong _xHoverBoxId => _host.XHoverBoxId;
        private string _selectionLabel { get => _host.SelectionLabelField; set => _host.SelectionLabelField = value; }
        private static readonly Color ExpandHoverGlowColor = new Color(0.45f, 0.75f, 1f, 1f);
        private Color ResolvedGlowColor => GlyphGlowSettings != null ? GlyphGlowSettings.GlowColor : Color.white;
        private float ResolvedGlowIntensity => GlyphGlowSettings != null ? GlyphGlowSettings.GlowIntensity : 0.5f;
        private float ResolvedGlowPower => GlyphGlowSettings != null ? GlyphGlowSettings.GlowPower : 3f;
        private float ResolvedGlowWidth => GlyphGlowSettings != null ? GlyphGlowSettings.GlowWidth : 0.5f;
        private float ResolvedGlowBoost => GlyphGlowSettings != null ? GlyphGlowSettings.GlowBoost : 4f;
        private float ResolvedGlowViewMix => GlyphGlowSettings != null ? GlyphGlowSettings.GlowViewMix : 0.5f;
        private void Destroy(UnityEngine.Object o) { if (o != null) UnityEngine.Object.Destroy(o); }
        private List<ulong> CollectChildIds(ulong boxId) => _host.Expanded.CollectChildIds(boxId);

        /// <summary>
        /// Face labels toward the HMD. Default: selected far-field + all voodoo ghosts.
        /// When <see cref="ThreeOSStorageController.BillboardAllFarFieldLabels"/> (Final Demo
        /// Palace), billboard every far glyph label so fixed-orientation TextMesh backfaces
        /// do not vanish based on head yaw.
        /// </summary>
        internal void BillboardSelectedAndVoodooLabels(ulong selectedId)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var camPos = cam.transform.position;
            BillboardLabelSet(_ghostViews, camPos);
            if (ThreeOSStorageController.BillboardAllFarFieldLabels)
            {
                BillboardLabelSet(_views, camPos);
                return;
            }

            if (selectedId != 0 && _views.TryGetValue(selectedId, out var view) &&
                view.Root != null && view.Root.activeInHierarchy)
            {
                FaceCamera(view.NameLabel, camPos);
                FaceCamera(view.SecondaryLabel, camPos);
            }
        }

        internal static void BillboardLabelSet(Dictionary<ulong, ThreeOSGlyphView> set, Vector3 camPos)
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

        internal static void FaceCamera(TextMesh label, Vector3 camPos)
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
        internal ThreeOSGlyphView CreateView(InteropWorkspaceItem item)
        {
            var go = new GameObject($"Glyph_{item.Name}");
            var movable = go.AddComponent<ThreeOSMovable>();
            movable.EntityId = item.entityId;
            movable.HalfExtent = ThreeOSStorageController.GlyphSizeM * 0.5f;

            // Unity's builtin cube mesh is 1 m; scale to authored glyph size (0.1 m).
            // Pack meshes (e.g. blender_mark) replace the cube when a .3glyph/.bytes loads.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = GlyphVisualScale(item.GlyphId);
            TryApplyPackMesh(visual, item.GlyphId);

            var col = visual.GetComponent<BoxCollider>();
            if (col != null)
            {
                // Collider is on the scaled child; keep it for aim hits.
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
            var labelY = -(ThreeOSStorageController.GlyphSizeM * 0.5f + 0.025f);
            var nameLabel = CreateLabel(go.transform, item.Name, new Vector3(0f, labelY, 0f), 0.0072f);
            TextMesh secondary = null;
            if (item.kind == 1)
            {
                secondary = CreateLabel(go.transform, item.SecondaryLabel,
                    new Vector3(0f, labelY - 0.028f, 0f), 0.0054f);
            }

            return new ThreeOSGlyphView
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

        internal void SyncGhostViewsFromKernel()
        {
            if (_bridge == null || !_bridge.IsLoaded || _bridge.NativeAbiVersion < 6)
            {
                ClearGhostViews();
                return;
            }

            // Soft-dismiss only hides Palace *real* glyphs (SyncViewsFromKernel). Voodoo near-BB
            // ghosts must still sync in MR so a far-field DemonA (or any in-field object) appears
            // when the user opens a volume in the office.

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

                    if (!_host.TryFindItem(ghost.entityId, out var item))
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
                            EnsureLabelPill(view.NameLabel.transform, view.NameLabel,
                                view.NameLabel.characterSize);
                        }

                        if (view.SecondaryLabel != null)
                        {
                            view.SecondaryLabel.text = item.SecondaryLabel ?? string.Empty;
                            EnsureLabelPill(view.SecondaryLabel.transform, view.SecondaryLabel,
                                view.SecondaryLabel.characterSize);
                        }
                    }

                    // Task 6.6: cut ducks opacity *by* 50% (not scale). Flag must survive
                    // SyncSelectionMaterials, which re-applies GlyphBaseColor every frame.
                    view.CutOpacityDuck = ghost.isParentBox == 0 && ghost.unused0 > 0.5f;
                    ApplyGhostCutOpacity(view, item);
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

        internal ThreeOSGlyphView CreateGhostView(InteropWorkspaceItem item, InteropVoodooGhost ghost)
        {
            var go = new GameObject($"Ghost_{item.Name}");
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            visual.transform.SetParent(go.transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = GlyphVisualScale(item.GlyphId);
            TryApplyPackMesh(visual, item.GlyphId);
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

            var labelY = -(ThreeOSStorageController.GlyphSizeM * 0.5f + 0.025f);
            var nameLabel = CreateLabel(go.transform, item.Name, new Vector3(0f, labelY, 0f), 0.0072f);
            TextMesh secondary = null;
            if (item.kind == 1)
            {
                secondary = CreateLabel(go.transform, item.SecondaryLabel,
                    new Vector3(0f, labelY - 0.028f, 0f), 0.0054f);
            }

            return new ThreeOSGlyphView
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

        internal void ClearGhostViews()
        {
            foreach (var kv in _ghostViews)
            {
                DestroyGlyphView(kv.Value);
            }

            _ghostViews.Clear();
        }

        internal static ulong GhostViewKey(ulong portalId, ulong entityId, bool parentBox) =>
            (portalId * 1000003UL) ^ (entityId * 97UL) ^ (parentBox ? 1UL : 0UL);

        internal static Vector3 GlyphVisualScale(string glyphId)
        {
            // CreatePrimitive cube is 1×1×1 m; shrink to ThreeOSStorageController.GlyphSizeM.
            if (glyphId == "box_open")
            {
                return new Vector3(ThreeOSStorageController.GlyphSizeM * 1.05f, ThreeOSStorageController.GlyphSizeM * 0.85f, ThreeOSStorageController.GlyphSizeM * 1.05f);
            }

            if (glyphId == "gltf_mark" || glyphId == "blender_mark" || glyphId == "unreal_mark")
            {
                // Slightly smaller mark so Object ≠ Box silhouette.
                return Vector3.one * (ThreeOSStorageController.GlyphSizeM * 0.85f);
            }

            return Vector3.one * ThreeOSStorageController.GlyphSizeM;
        }

        /// <summary>
        /// Swap the cube MeshFilter for a pack GLB mesh when Resources has <c>3OS/glyphs/{id}</c>.
        /// </summary>
        internal static void TryApplyPackMesh(GameObject visual, string glyphId)
        {
            if (visual == null || string.IsNullOrEmpty(glyphId) ||
                glyphId.StartsWith("procedural") || glyphId.StartsWith("box"))
                return;

            var mesh = LoadPackMesh(glyphId);
            if (mesh == null)
                return;

            var mf = visual.GetComponent<MeshFilter>();
            if (mf != null)
                mf.sharedMesh = mesh;

            // Fit mesh local AABB to box glyph size so blender_mark / marks match boxes.
            var size = mesh.bounds.size;
            var maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (maxDim > 1e-8f)
            {
                var target = ThreeOSStorageController.GlyphSizeM;
                visual.transform.localScale = Vector3.one * (target / maxDim);
            }

            // blender_mark was authored facing +Y (ceiling). Roll 90° then yaw 90° so the
            // logo faces the player along the layout forward. Spec debt: glyph front = +Z
            // (see IMPLEMENTATION_PLAN parking lot).
            if (glyphId == "blender_mark")
            {
                visual.transform.localRotation =
                    Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(0f, 0f, 90f);
            }
        }

        internal static Mesh LoadPackMesh(string glyphId)
        {
            if (GlyphMeshCache.TryGetValue(glyphId, out var cached) && cached != null)
                return cached;

            var ta = Resources.Load<TextAsset>("3OS/glyphs/" + glyphId);
            if (ta == null || ta.bytes == null || ta.bytes.Length == 0)
                return null;

            if (!ThreeOSGlbMesh.TryLoadFirstMesh(ta.bytes, out var mesh, out var err) || mesh == null)
            {
                Debug.LogWarning($"[3OS] glyph mesh '{glyphId}': {err}");
                return null;
            }

            mesh.name = "GlyphMesh_" + glyphId;
            GlyphMeshCache[glyphId] = mesh;
            return mesh;
        }

        internal void ResolveGlyphMaterial(InteropWorkspaceItem item, out Material mat, out bool ownsMaterial)
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

        internal void EnsureSharedBoxMaterial()
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
            // Match Final Demo Palace keep-list (5100) so glyphs clear Overlay sky.
            mat.renderQueue = 5100;
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
        internal static Color GhostDisplayColor(InteropWorkspaceItem item, bool cutOpacityDuck)
        {
            var c = GlyphBaseColor(item);
            // Duck *by* 50% of the normal ghost alpha (50% → 25%, etc.), never set absolute.
            if (cutOpacityDuck)
            {
                c.a *= 0.5f;
            }

            return c;
        }

        internal void ApplyGhostCutOpacity(ThreeOSGlyphView view, InteropWorkspaceItem item)
        {
            if (view?.Renderer == null)
            {
                return;
            }

            ApplyGlyphSelection(view.Renderer, rimOn: false,
                GhostDisplayColor(item, view.CutOpacityDuck), null,
                view.OwnsMaterial ? view.Material : null);
        }

        internal void ApplyGlyphSelection(MeshRenderer renderer, bool rimOn, Color baseColor,
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

        internal static Color GlyphBaseColor(InteropWorkspaceItem item)
        {
            if (item.GlyphId == "box_closed" || item.GlyphId == "box_open" || item.kind == 1)
            {
                return new Color(0.55f, 0.36f, 0.18f, 0.55f);
            }

            if (item.GlyphId == "gltf_mark")
            {
                return new Color(0.10f, 0.72f, 0.78f, 0.55f);
            }

            if (item.GlyphId == "blender_mark")
            {
                // Blender brand orange — readable on Palace sky.
                return new Color(0.91f, 0.49f, 0.08f, 0.7f);
            }

            if (item.GlyphId == "unreal_mark")
            {
                // White Unreal mark — black was unreadable on dark Palace sky / office geo.
                return new Color(1f, 1f, 1f, 0.75f);
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

        internal void AssignGlyphMaterials(ThreeOSGlyphView view, InteropWorkspaceItem item)
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

        internal void DestroyGlyphView(ThreeOSGlyphView view)
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
        internal static TextMesh CreateLabel(Transform parent, string text, Vector3 localPos, float charSize)
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

            // Keep labels readable in front of transparent glyphs.
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                // Instance font mat + Cull Off: shared GUI/Text backfaces were vanishing with
                // head yaw (same class of view-dependent disappear as early selection glow).
                if (tm.font != null && tm.font.material != null)
                {
                    // Instance so queue/cull tweaks do not mutate the shared font asset.
                    var mat = new Material(tm.font.material);
                    mat.name = "3OS_GlyphLabelFont";
                    // Match Final Demo Palace keep-list queue (5100) so labels clear the sky Overlay.
                    mat.renderQueue = 5100;
                    mr.material = mat;
                }
            }

            EnsureLabelPill(go.transform, tm, charSize);
            return tm;
        }

        /// <summary>
        /// Soft gray capsule behind a TextMesh so Palace labels stay readable on the sky.
        /// </summary>
        internal static void EnsureLabelPill(Transform labelTf, TextMesh tm, float charSize)
        {
            if (labelTf == null || tm == null)
                return;

            const string pillName = "LabelPill";
            var pillTf = labelTf.Find(pillName);
            GameObject pillGo;
            if (pillTf != null)
            {
                pillGo = pillTf.gameObject;
            }
            else
            {
                pillGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                pillGo.name = pillName;
                var col = pillGo.GetComponent<Collider>();
                if (col != null)
                    UnityEngine.Object.Destroy(col);
                pillGo.transform.SetParent(labelTf, false);
                // Capsule default axis = Y; roll so length runs along local X under the text.
                pillGo.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var sh = Shader.Find("ThreeOS/EqBar") ?? Shader.Find("Sprites/Default") ??
                         Shader.Find("Unlit/Color");
                var mat = new Material(sh);
                var c = LabelPillColor;
                mat.color = c;
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", c);
                mat.renderQueue = 5090; // just under label font (5100)
                var pMr = pillGo.GetComponent<MeshRenderer>();
                pMr.sharedMaterial = mat;
                pMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pMr.receiveShadows = false;
            }

            // Fit X to TextMesh glyph AABB + small L/R pad. Capsule mesh height is 2, so
            // scale.y = desiredLength/2. Prior textW*1.2 ignored that and overshot.
            MeasureLabelLocalSize(tm, charSize, out var textW, out var textH);
            var pillHeight = Mathf.Max(textH * 1.1f, charSize * 6.5f);
            // ~4% pad each side; /2 converts desired world length → capsule scale.y.
            var pillLengthScale = textW * 1.08f * 0.5f;
            // Capsule default long axis = Y; after Z-roll 90° that maps to label X.
            pillGo.transform.localScale = new Vector3(pillHeight, pillLengthScale, pillHeight);
            // +Z is away from camera (FaceCamera LookRotation); sit just behind the glyphs.
            pillGo.transform.localPosition = new Vector3(0f, 0f, 0.012f);

            var pillMr = pillGo.GetComponent<MeshRenderer>();
            if (pillMr != null && pillMr.sharedMaterial != null)
            {
                var c = LabelPillColor;
                var mat = pillMr.material;
                mat.color = c;
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", c);
            }
        }

        // Prior mid-gray ~0.58; 20% darker for contrast on Palace sky.
        static readonly Color LabelPillColor = new Color(0.464f, 0.464f, 0.48f, 0.78f);

        static void MeasureLabelLocalSize(TextMesh tm, float charSize, out float width, out float height)
        {
            width = charSize * (Mathf.Max(2, (tm.text ?? "?").Length) * 0.7f + 1.5f);
            height = charSize * 9.5f;
            if (tm == null)
                return;
            var r = tm.GetComponent<Renderer>();
            if (r == null)
                return;
            // Force mesh rebuild for current string, then read local AABB (XY plane).
            var text = tm.text;
            tm.text = text ?? "?";
            var lb = r.localBounds;
            if (lb.size.x > 1e-5f)
                width = lb.size.x;
            if (lb.size.y > 1e-5f)
                height = lb.size.y;
        }
        internal void SyncViewsFromKernel()
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
                var isPersistedMr = item.entityId != 0 &&
                                    item.entityId == _host.MrPersistedSelectionId;

                // Soft-dismissed in MR: never recreate Palace glyphs (fixes delayed MR leak).
                if (_host.PalaceSoftDismissed && !isPersistedMr)
                {
                    if (_views.TryGetValue(item.entityId, out var softHide))
                    {
                        DestroyGlyphView(softHide);
                        _views.Remove(item.entityId);
                    }

                    continue;
                }

                if (!inField)
                {
                    // Destroy (don't leave inactive) — large pastes used to leave dozens of
                    // hidden glyphs alive, and full RebuildViews to clean them hung the Quest.
                    if (_views.TryGetValue(item.entityId, out var hide))
                    {
                        if (!animLocked)
                        {
                            DestroyGlyphView(hide);
                            _views.Remove(item.entityId);
                        }
                        else if (hide.Root != null)
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

                // Pending Palace scale-in: stay inactive so SyncViews cannot flash full-size
                // glyphs for a frame before AnimatePalaceClustersScale parents them.
                if (_host.PalacePendingScaleIn && _host.IsPalaceEntity(item.entityId) &&
                    item.entityId != _host.MrPersistedSelectionId && !animLocked)
                {
                    view.Root.SetActive(false);
                    continue;
                }

                view.Root.SetActive(true);
                // MR-persisted selection: keep the MR world cache in sync with the live glyph so
                // MR→VR restore klobbers any stale palace-field pose with the office anchor.
                if (isPersistedMr)
                    _host.CaptureMrPersistedFromLive(view.Root.transform);

                // While kernel kinematics drives this entity (possess or coast), Movable owns pose.
                // Overwriting from workspace here caused post-release snaps to a stale layout pose.
                // Expand/collapse / MR-carry anim-lock also owns pose until cleared.
                var kineticDriven = _host.IsKinematicallyDriven(item.entityId);
                if (!kineticDriven && !animLocked && !_host.ShouldSkipKernelViewPoseSync(item.entityId))
                {
                    // Soft-dismissed MR carry: never write palace/workspace pose over the office
                    // anchor — MR always takes precedence until the user telekinesis-moves it.
                    if (isPersistedMr && _host.PalaceSoftDismissed)
                        continue;

                    var pose = InteropStructs.ToUnityPose(item.pose);
                    view.Root.transform.SetPositionAndRotation(pose.position, pose.rotation);
                }

                if (view.NameLabel != null)
                {
                    view.NameLabel.text = item.Name;
                    EnsureLabelPill(view.NameLabel.transform, view.NameLabel,
                        view.NameLabel.characterSize);
                }

                if (view.SecondaryLabel != null)
                {
                    view.SecondaryLabel.text = item.SecondaryLabel;
                    EnsureLabelPill(view.SecondaryLabel.transform, view.SecondaryLabel,
                        view.SecondaryLabel.characterSize);
                }

                if (view.GlyphId != item.GlyphId)
                {
                    view.GlyphId = item.GlyphId;
                    AssignGlyphMaterials(view, item);
                    if (view.Renderer != null)
                    {
                        view.Renderer.transform.localScale = GlyphVisualScale(item.GlyphId);
                        TryApplyPackMesh(view.Renderer.gameObject, item.GlyphId);
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
        internal void RemoveGhostViewsForEntity(ulong entityId)
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
        readonly HashSet<ulong> _selSet = new HashSet<ulong>();

        internal void SyncSelectionMaterials()
        {
            _bridge.TryGetSelection(out var selectedId);
            _selSet.Clear();
            var selN = _bridge.GetSelectionIds(out var selIds);
            for (var i = 0; i < selN; i++) _selSet.Add(selIds[i]);
            if (selectedId == 0)
            {
                _selectionLabel = "none";
            }
            else if (_host.TryFindItem(selectedId, out var selectedItem))
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

        internal void ApplySelectionRim(ulong entityId, ThreeOSGlyphView view, ulong selectedId,
            HashSet<ulong> blueChildren)
        {
            if (view.Renderer == null)
            {
                return;
            }

            var abiSelected = (selectedId != 0 && entityId == selectedId) || _selSet.Contains(entityId);
            var blueHover = blueChildren != null && blueChildren.Contains(entityId);
            var selectedOrHover = abiSelected || blueHover;
            var owned = view.OwnsMaterial ? view.Material : null;
            if (!_host.TryFindItem(entityId, out var item))
            {
                var fallback = Color.white;
                if (view.CutOpacityDuck)
                {
                    fallback.a *= 0.5f;
                }

                // Unknown item: keep Fresnel rim (cube-shaped fallback).
                ApplyGlyphSelection(view.Renderer, selectedOrHover, fallback,
                    blueHover ? ExpandHoverGlowColor : (Color?)null, owned);
                return;
            }

            var baseColor = GhostDisplayColor(item, view.CutOpacityDuck);
            // Non-cube pack meshes (blender_mark, etc.): Fresnel rim looks wrong — brighten instead.
            if (selectedOrHover && !UsesFresnelSelection(item.GlyphId))
            {
                ApplyGlyphSelection(view.Renderer, rimOn: false,
                    BrightenForSelection(baseColor),
                    blueHover ? ExpandHoverGlowColor : (Color?)null, owned);
                return;
            }

            ApplyGlyphSelection(view.Renderer, selectedOrHover, baseColor,
                blueHover ? ExpandHoverGlowColor : (Color?)null, owned);
        }

        /// <summary>Cube / box glyphs keep Fresnel rim; authored pack meshes use brightness only.</summary>
        internal static bool UsesFresnelSelection(string glyphId)
        {
            if (string.IsNullOrEmpty(glyphId))
                return true;
            if (glyphId.StartsWith("box") || glyphId.StartsWith("procedural"))
                return true;
            return false;
        }

        const float SelectionBrightnessBoost = 1.65f;

        internal static Color BrightenForSelection(Color c)
        {
            return new Color(
                Mathf.Min(1f, c.r * SelectionBrightnessBoost),
                Mathf.Min(1f, c.g * SelectionBrightnessBoost),
                Mathf.Min(1f, c.b * SelectionBrightnessBoost),
                c.a);
        }
        /// <summary>
        /// Translucent shell for dismiss X / scissors. Prefer Resources
        /// <c>3OS/GlyphDefault</c> (same shader that draws Palace GLB cubes on Quest) —
        /// Shader.Find-only mats were invisible on device spheres.
        /// </summary>
        internal static Material CreateTranslucentUnlitMaterial(Color color)
        {
            Material mat = null;
            var glyphTmpl = Resources.Load<Material>("3OS/GlyphDefault");
            if (glyphTmpl != null && glyphTmpl.shader != null)
            {
                mat = new Material(glyphTmpl);
            }
            else
            {
                var eqTmpl = Resources.Load<Material>("3OS/EqBar");
                if (eqTmpl != null && eqTmpl.shader != null)
                {
                    mat = new Material(eqTmpl);
                }
            }

            if (mat == null)
            {
                var shader = Shader.Find("ThreeOS/Glyph")
                             ?? Shader.Find("ThreeOS/EqBar")
                             ?? Shader.Find("Unlit/Color");
                mat = new Material(shader);
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }

            if (mat.HasProperty("_Selected"))
            {
                mat.SetFloat("_Selected", 0f);
            }

            mat.color = color;
            // Match Final Demo Palace keep-list (5100) so dismiss shells clear Overlay sky.
            mat.renderQueue = 5100;
            return mat;
        }

    }
}
