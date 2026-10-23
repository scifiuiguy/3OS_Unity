using UnityEngine;
using UnityEngine.Rendering;

namespace ThreeOS
{
    /// <summary>
    /// Wide track + left-anchored green fill for Mind Palace paste / FS write-through.
    /// Fill is inset to ~90% of track height/depth and grows in X from the left.
    /// </summary>
    internal sealed class ThreeOSPasteProgressBar
    {
        const float TrackWidth = 0.28f;
        const float TrackHeight = 0.022f;
        const float TrackDepth = 0.022f;
        const float Inset = 0.90f;

        readonly Transform _root;
        readonly Transform _fill;
        readonly Material _trackMat;
        readonly Material _fillMat;
        bool _visible;

        ThreeOSPasteProgressBar(Transform root, Transform fill, Material trackMat, Material fillMat)
        {
            _root = root;
            _fill = fill;
            _trackMat = trackMat;
            _fillMat = fillMat;
        }

        internal static ThreeOSPasteProgressBar Create(Transform parent)
        {
            var rootGo = new GameObject("3OS_PasteProgress");
            var root = rootGo.transform;
            if (parent != null)
                root.SetParent(parent, false);

            var trackMat = MakeMat(new Color(0.08f, 0.08f, 0.08f, 0.55f), "3OS_PasteTrack");
            var fillMat = MakeMat(new Color(0.18f, 0.85f, 0.32f, 0.92f), "3OS_PasteFill");

            var track = MakeCube(root, "Track", trackMat);
            track.localScale = new Vector3(TrackWidth, TrackHeight, TrackDepth);
            track.localPosition = Vector3.zero;

            var fill = MakeCube(root, "Fill", fillMat);
            // Start empty at left inner edge.
            ApplyFillScale(fill, 0f);

            rootGo.SetActive(false);
            return new ThreeOSPasteProgressBar(root, fill, trackMat, fillMat);
        }

        /// <summary>
        /// Place in front of the camera (above Palace sky Overlay queue) so the bar stays visible.
        /// </summary>
        internal void ShowInFrontOfCamera(Camera cam, float distance = 0.85f, float up = -0.08f)
        {
            if (_root == null)
                return;
            if (cam == null)
                cam = Camera.main;
            _root.SetParent(null, false);
            if (cam != null)
            {
                _root.position = cam.transform.position + cam.transform.forward * distance +
                                 cam.transform.up * up;
                _root.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
            }

            ApplyFillScale(_fill, 0f);
            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);
            _visible = true;
        }

        internal void ShowAt(Transform anchor, Vector3 worldOffset)
        {
            // Prefer camera-locked placement — world anchors sit under the Palace sky Overlay.
            var cam = Camera.main;
            if (cam != null)
            {
                ShowInFrontOfCamera(cam);
                return;
            }

            if (_root == null)
                return;
            _root.SetParent(null, false);
            if (anchor != null)
            {
                _root.position = anchor.position + worldOffset;
                _root.rotation = Quaternion.LookRotation(
                    Vector3.ProjectOnPlane(anchor.forward, Vector3.up).normalized.sqrMagnitude > 1e-6f
                        ? Vector3.ProjectOnPlane(anchor.forward, Vector3.up).normalized
                        : Vector3.forward,
                    Vector3.up);
            }

            ApplyFillScale(_fill, 0f);
            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);
            _visible = true;
        }

        internal void TickFollowCamera()
        {
            if (!_visible || _root == null || !_root.gameObject.activeSelf)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            _root.position = cam.transform.position + cam.transform.forward * 0.85f +
                             cam.transform.up * -0.08f;
            _root.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
        }

        internal void SetProgress(float t01)
        {
            if (_fill == null)
                return;
            ApplyFillScale(_fill, Mathf.Clamp01(t01));
            TickFollowCamera();
        }

        internal void Hide()
        {
            _visible = false;
            if (_root != null && _root.gameObject.activeSelf)
                _root.gameObject.SetActive(false);
        }

        internal void Dispose()
        {
            if (_root != null)
                Object.Destroy(_root.gameObject);
            if (_trackMat != null)
                Object.Destroy(_trackMat);
            if (_fillMat != null)
                Object.Destroy(_fillMat);
        }

        static void ApplyFillScale(Transform fill, float t01)
        {
            var maxW = TrackWidth * Inset;
            var h = TrackHeight * Inset;
            var d = TrackDepth * Inset;
            var w = Mathf.Max(1e-4f, maxW * Mathf.Clamp01(t01));
            fill.localScale = new Vector3(w, h, d);
            // Left-anchored inside the track: left inner edge at -maxW/2.
            fill.localPosition = new Vector3(-maxW * 0.5f + w * 0.5f, 0f, 0f);
        }

        static Transform MakeCube(Transform parent, string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            var col = go.GetComponent<Collider>();
            if (col != null)
                Object.Destroy(col);
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = mat;
                rend.shadowCastingMode = ShadowCastingMode.Off;
                rend.receiveShadows = false;
            }

            return go.transform;
        }

        static Material MakeMat(Color tint, string name)
        {
            var template = Resources.Load<Material>("3OS/EqBar");
            Material mat;
            if (template != null)
            {
                mat = new Material(template)
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            else
            {
                var shader = Shader.Find("ThreeOS/EqBar") ?? Shader.Find("Unlit/Color") ??
                             Shader.Find("Sprites/Default");
                mat = new Material(shader)
                {
                    name = name,
                    hideFlags = HideFlags.HideAndDontSave
                };
                mat.renderQueue = (int)RenderQueue.Transparent;
            }

            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            else
                mat.color = tint;
            // Above Palace sky Overlay (5000) / keep-list (5100).
            mat.renderQueue = 5200;
            return mat;
        }
    }
}
