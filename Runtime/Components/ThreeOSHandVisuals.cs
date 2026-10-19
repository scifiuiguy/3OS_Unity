using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Spawns (or adopts) XR Hands sample skinned meshes under the XR Origin Camera Offset
    /// and applies the blue Fresnel ghost look. Prefabs live in Resources/3OS/Hands
    /// (Mind Palace Left/RightHandTracking — not the AndroidHands test SegHand meshes).
    /// </summary>
    public sealed class ThreeOSHandVisuals : MonoBehaviour
    {
        private const string LeftSceneName = "Left Hand Tracking";
        private const string RightSceneName = "Right Hand Tracking";

        private GameObject _left;
        private GameObject _right;
        private Renderer[] _leftRenderers = System.Array.Empty<Renderer>();
        private Renderer[] _rightRenderers = System.Array.Empty<Renderer>();
        private static Material _ghostMat;

        private void OnEnable()
        {
            var parent = FindCameraOffset();
            if (parent == null)
            {
                Debug.LogWarning(
                    "[3OS] Hand visuals: no XR Origin Camera Offset — hands will not track with the rig.");
            }

            // Prefer Mind Palace XR Hands meshes already under the origin; else spawn there.
            _left = AdoptOrSpawn(
                parent,
                LeftSceneName,
                new[] { "3OS/Hands/LeftHandTracking", "3OS/Hands/AndroidHand_L_Tracking" },
                "Left Hand Tracking",
                out _leftRenderers);
            _right = AdoptOrSpawn(
                parent,
                RightSceneName,
                new[] { "3OS/Hands/RightHandTracking", "3OS/Hands/AndroidHand_R_Tracking" },
                "Right Hand Tracking",
                out _rightRenderers);

            if (_left == null && _right == null)
            {
                Debug.LogWarning(
                    "[3OS] Hand mesh prefabs missing (Resources/3OS/Hands/LeftHandTracking|RightHandTracking).");
            }
            else
            {
                Debug.Log(
                    $"[3OS] Hand visuals ready L={(_left != null)} R={(_right != null)} " +
                    $"parent={(parent != null ? parent.name : "null")}");
            }
        }

        private void LateUpdate()
        {
            // XRHandMeshController can restore the prefab material when tracking is acquired.
            ApplyGhostMaterial(_leftRenderers);
            ApplyGhostMaterial(_rightRenderers);
        }

        private void OnDisable()
        {
            // Only destroy hands we spawned at runtime (not scene/prefab instances).
            DestroyIfOwned(_left);
            DestroyIfOwned(_right);
            _left = null;
            _right = null;
            _leftRenderers = System.Array.Empty<Renderer>();
            _rightRenderers = System.Array.Empty<Renderer>();
        }

        private static void DestroyIfOwned(GameObject go)
        {
            if (go == null)
                return;
            // Scene/prefab hands under XR Origin are kept; only destroy runtime clones.
            if (go.name.StartsWith("3OS_Runtime_"))
                Destroy(go);
        }

        private static GameObject AdoptOrSpawn(Transform parent, string existingName,
            string[] resourcePaths, string spawnName, out Renderer[] renderers)
        {
            renderers = System.Array.Empty<Renderer>();

            // Already under Camera Offset (editor-placed / prefabbed)?
            if (parent != null)
            {
                var existing = parent.Find(existingName);
                if (existing == null)
                {
                    // Also accept our previous runtime names.
                    existing = parent.Find("3OS_LeftHandMesh");
                    if (existing == null && existingName.StartsWith("Right"))
                        existing = parent.Find("3OS_RightHandMesh");
                    if (existing == null)
                        existing = parent.Find("3OS_Runtime_" + spawnName);
                }

                if (existing != null)
                {
                    renderers = existing.GetComponentsInChildren<Renderer>(true);
                    ApplyGhostMaterial(renderers);
                    return existing.gameObject;
                }
            }

            return SpawnFirst(resourcePaths, "3OS_Runtime_" + spawnName, parent, out renderers);
        }

        private static Transform FindCameraOffset()
        {
            // Prefer live XR Origin (Final Demo names the instance "XR Origin (VR)").
            Transform origin = null;
            var byName = GameObject.Find("XR Origin (VR)");
            if (byName == null)
                byName = GameObject.Find("XR_Origin_FinalDemo");
            if (byName != null)
                origin = byName.transform;

            if (origin == null)
            {
                // XROrigin component if package present (avoid hard asmref — search by type name).
                var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
                for (var i = 0; i < behaviours.Length; i++)
                {
                    var mb = behaviours[i];
                    if (mb != null && mb.GetType().Name == "XROrigin")
                    {
                        origin = mb.transform;
                        break;
                    }
                }
            }

            if (origin == null)
                return null;

            var offset = origin.Find("Camera Offset");
            return offset != null ? offset : origin;
        }

        private static GameObject SpawnFirst(string[] resourcePaths, string name, Transform parent,
            out Renderer[] renderers)
        {
            renderers = System.Array.Empty<Renderer>();
            if (resourcePaths == null)
                return null;

            for (var i = 0; i < resourcePaths.Length; i++)
            {
                var go = Spawn(resourcePaths[i], name, parent, out renderers);
                if (go != null)
                    return go;
            }

            return null;
        }

        private static GameObject Spawn(string resourcePath, string name, Transform parent,
            out Renderer[] renderers)
        {
            renderers = System.Array.Empty<Renderer>();
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
                return null;

            var go = parent != null
                ? Instantiate(prefab, parent, false)
                : Instantiate(prefab);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            renderers = go.GetComponentsInChildren<Renderer>(true);
            ApplyGhostMaterial(renderers);
            return go;
        }

        private static void ApplyGhostMaterial(Renderer[] renderers)
        {
            var mat = GhostMaterial();
            if (mat == null || renderers == null)
                return;

            for (var i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                    continue;
                if (r.sharedMaterial != mat)
                    r.sharedMaterial = mat;
            }
        }

        private static Material GhostMaterial()
        {
            if (_ghostMat != null)
                return _ghostMat;

            var template = Resources.Load<Material>("3OS/HandGhost");
            if (template != null && template.shader != null &&
                template.shader.name == "ThreeOS/HandGhost")
            {
                _ghostMat = new Material(template)
                {
                    name = "3OS_HandGhost_Runtime",
                    hideFlags = HideFlags.HideAndDontSave
                };
                return _ghostMat;
            }

            var shader = Shader.Find("ThreeOS/HandGhost");
            if (shader == null)
            {
                Debug.LogWarning(
                    "[3OS] Missing Resources/3OS/HandGhost.mat (ThreeOS/HandGhost) — leaving sample hand material");
                return null;
            }

            _ghostMat = new Material(shader)
            {
                name = "3OS_HandGhost_Runtime",
                hideFlags = HideFlags.HideAndDontSave
            };
            return _ghostMat;
        }
    }
}
