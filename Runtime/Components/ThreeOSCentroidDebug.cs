using System.Collections.Generic;
using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Task 6.2: field-space centroid markers (god/box averages). Hidden while snap dots are active.
    /// </summary>
    [DefaultExecutionOrder(220)]
    public sealed class ThreeOSCentroidDebug : MonoBehaviour
    {
        private ThreeOSBridge _bridge;
        private readonly List<Transform> _markers = new List<Transform>();
        private Material _matGod;
        private Material _matBox;
        private Material _matTemp;

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
            _matGod = MakeMat(new Color(1f, 0.35f, 0.9f, 0.55f));
            _matBox = MakeMat(new Color(0.35f, 0.85f, 1f, 0.55f));
            // Near-portal parent node (center-bottom-center of near BB).
            _matTemp = MakeMat(new Color(0.35f, 0.85f, 1f, 0.75f));
        }

        private void LateUpdate()
        {
            if (_bridge == null || !_bridge.IsLoaded || _bridge.NativeAbiVersion < 6)
            {
                HideAll();
                return;
            }

            // Prefer hand-space snap dots while a snap drag is live.
            if (_bridge.VoodooSnapNodeCount(0) > 0 || _bridge.VoodooSnapNodeCount(1) > 0)
            {
                HideAll();
                return;
            }

            var n = (int)_bridge.CentroidCount;
            EnsureCount(n);
            for (var i = 0; i < n; i++)
            {
                if (!_bridge.TryGetCentroid((uint)i, out var c))
                {
                    _markers[i].gameObject.SetActive(false);
                    continue;
                }

                var t = _markers[i];
                t.position = InteropStructs.OpenXrToUnityPosition(c.position.ToVector3());
                t.localScale = Vector3.one * (0.08f / 3f);
                var r = t.GetComponent<MeshRenderer>();
                if (r != null)
                {
                    if (c.kind == (uint)CentroidKind.God)
                    {
                        r.sharedMaterial = _matGod;
                    }
                    else if (c.kind == (uint)CentroidKind.Temporary)
                    {
                        r.sharedMaterial = _matTemp;
                    }
                    else
                    {
                        r.sharedMaterial = _matBox;
                    }
                }

                t.gameObject.SetActive(true);
            }

            for (var i = n; i < _markers.Count; i++)
            {
                _markers[i].gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_matGod != null) Destroy(_matGod);
            if (_matBox != null) Destroy(_matBox);
            if (_matTemp != null) Destroy(_matTemp);
        }

        private void EnsureCount(int n)
        {
            while (_markers.Count < n)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"3OS_Centroid_{_markers.Count}";
                foreach (var col in go.GetComponents<Collider>())
                {
                    Destroy(col);
                }

                go.SetActive(false);
                _markers.Add(go.transform);
            }
        }

        private void HideAll()
        {
            foreach (var t in _markers)
            {
                if (t != null) t.gameObject.SetActive(false);
            }
        }

        private static Material MakeMat(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                         Shader.Find("Unlit/Color") ??
                         Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            mat.renderQueue = 3000;
            return mat;
        }
    }
}
