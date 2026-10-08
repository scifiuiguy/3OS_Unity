using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Near-field Double-Proxy lattice wireframe driven by kernel proxy state.
    /// </summary>
    public sealed class ThreeOSDoubleProxyLattice : MonoBehaviour
    {
        private LineRenderer _nearLines;
        private LineRenderer _farLines;
        private bool _built;
        public ulong ProxyId { get; private set; }

        private void Awake()
        {
            EnsureLines();
            gameObject.SetActive(false);
        }

        public void Apply(InteropDoubleProxyState proxy)
        {
            EnsureLines();
            ProxyId = proxy.id;
            if (proxy.active == 0)
            {
                gameObject.SetActive(false);
                return;
            }

            DrawAabb(_nearLines, proxy.nearAabb, new Color(1f, 0.85f, 0.2f, 0.9f));
            DrawAabb(_farLines, proxy.farAabb, new Color(1f, 0.4f, 0.1f, 0.55f));
            gameObject.SetActive(true);
        }

        private void EnsureLines()
        {
            if (_built) return;
            _nearLines = CreateLineChild("NearLattice");
            _farLines = CreateLineChild("FarVolume");
            _built = true;
        }

        private LineRenderer CreateLineChild(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.widthMultiplier = 0.01f;
            lr.useWorldSpace = true;
            lr.loop = false;
            lr.positionCount = 0;
            return lr;
        }

        private static void DrawAabb(LineRenderer lr, ThreeOSAabb aabb, Color color)
        {
            var min = InteropStructs.OpenXrToUnityPosition(aabb.min.ToVector3());
            var max = InteropStructs.OpenXrToUnityPosition(aabb.max.ToVector3());
            // After LH conversion, min/max components may swap on X.
            var x0 = Mathf.Min(min.x, max.x);
            var x1 = Mathf.Max(min.x, max.x);
            var y0 = Mathf.Min(min.y, max.y);
            var y1 = Mathf.Max(min.y, max.y);
            var z0 = Mathf.Min(min.z, max.z);
            var z1 = Mathf.Max(min.z, max.z);

            var c = new Vector3[8];
            c[0] = new Vector3(x0, y0, z0);
            c[1] = new Vector3(x1, y0, z0);
            c[2] = new Vector3(x1, y0, z1);
            c[3] = new Vector3(x0, y0, z1);
            c[4] = new Vector3(x0, y1, z0);
            c[5] = new Vector3(x1, y1, z0);
            c[6] = new Vector3(x1, y1, z1);
            c[7] = new Vector3(x0, y1, z1);

            // 12 edges as a continuous strip with breaks via duplicate hops
            var pts = new[]
            {
                c[0], c[1], c[2], c[3], c[0],
                c[4], c[5], c[6], c[7], c[4],
                c[0], c[4], c[5], c[1], c[2], c[6], c[7], c[3]
            };
            lr.startColor = lr.endColor = color;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
        }
    }
}
