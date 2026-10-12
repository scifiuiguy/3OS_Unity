using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Spawns XR Hands sample skinned meshes and applies the blue Fresnel ghost look
    /// (OVR-style translucent hands). Uses Resources/3OS/HandGhost.mat so the shader
    /// is not stripped on device (same pattern as GlyphDefault).
    /// </summary>
    public sealed class ThreeOSHandVisuals : MonoBehaviour
    {
        private GameObject _left;
        private GameObject _right;
        private Renderer[] _leftRenderers = System.Array.Empty<Renderer>();
        private Renderer[] _rightRenderers = System.Array.Empty<Renderer>();
        private static Material _ghostMat;

        private void OnEnable()
        {
            _left = Spawn("3OS/Hands/LeftHandTracking", "3OS_LeftHandMesh", out _leftRenderers);
            _right = Spawn("3OS/Hands/RightHandTracking", "3OS_RightHandMesh", out _rightRenderers);
            if (_left == null && _right == null)
            {
                Debug.LogWarning(
                    "[3OS] Hand mesh prefabs missing (Resources/3OS/Hands/*).");
            }
        }

        private void LateUpdate()
        {
            // XRHandMeshController can re-enable the renderer with the prefab material
            // when tracking is acquired — keep the ghost look pinned.
            ApplyGhostMaterial(_leftRenderers);
            ApplyGhostMaterial(_rightRenderers);
        }

        private void OnDisable()
        {
            if (_left != null)
            {
                Destroy(_left);
                _left = null;
            }

            if (_right != null)
            {
                Destroy(_right);
                _right = null;
            }

            _leftRenderers = System.Array.Empty<Renderer>();
            _rightRenderers = System.Array.Empty<Renderer>();
        }

        private static GameObject Spawn(string resourcePath, string name, out Renderer[] renderers)
        {
            renderers = System.Array.Empty<Renderer>();
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                return null;
            }

            var go = Instantiate(prefab);
            go.name = name;
            renderers = go.GetComponentsInChildren<Renderer>(true);
            ApplyGhostMaterial(renderers);
            DontDestroyOnLoad(go);
            return go;
        }

        private static void ApplyGhostMaterial(Renderer[] renderers)
        {
            var mat = GhostMaterial();
            if (mat == null || renderers == null)
            {
                return;
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                if (r.sharedMaterial != mat)
                {
                    r.sharedMaterial = mat;
                }
            }
        }

        private static Material GhostMaterial()
        {
            if (_ghostMat != null)
            {
                return _ghostMat;
            }

            // Resources material references ThreeOS/HandGhost → keeps shader in the player.
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
