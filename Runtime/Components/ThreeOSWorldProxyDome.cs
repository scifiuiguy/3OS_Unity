using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Runtime World Proxy Half-Dome visual driven by kernel dome state (not a local fake).
    /// </summary>
    public sealed class ThreeOSWorldProxyDome : MonoBehaviour
    {
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private bool _built;

        private void Awake()
        {
            EnsureMesh();
            gameObject.SetActive(false);
        }

        public void Apply(InteropDomeState dome)
        {
            EnsureMesh();
            if (dome.active == 0)
            {
                gameObject.SetActive(false);
                return;
            }

            var unityPose = InteropStructs.ToUnityPose(dome.pose);
            transform.SetPositionAndRotation(unityPose.position, unityPose.rotation);
            var r = Mathf.Max(0.05f, dome.radiusM);
            transform.localScale = new Vector3(r * 2f, r, r * 2f);
            gameObject.SetActive(true);
        }

        private void EnsureMesh()
        {
            if (_built) return;
            _filter = gameObject.GetComponent<MeshFilter>() ?? gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.GetComponent<MeshRenderer>() ?? gameObject.AddComponent<MeshRenderer>();
            _filter.sharedMesh = BuildHemisphere(24, 12);
            _renderer.sharedMaterial = new Material(Shader.Find("Sprites/Default"))
            {
                color = new Color(0.2f, 0.85f, 1f, 0.35f)
            };
            _built = true;
        }

        private static Mesh BuildHemisphere(int lon, int lat)
        {
            var mesh = new Mesh { name = "WorldProxyHalfDome" };
            var verts = new Vector3[(lat + 1) * (lon + 1)];
            var tris = new int[lat * lon * 6];
            var i = 0;
            for (var y = 0; y <= lat; y++)
            {
                var v = y / (float)lat;
                var phi = v * Mathf.PI * 0.5f; // upper hemisphere
                for (var x = 0; x <= lon; x++)
                {
                    var u = x / (float)lon;
                    var theta = u * Mathf.PI * 2f;
                    verts[i++] = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta));
                }
            }

            var t = 0;
            for (var y = 0; y < lat; y++)
            {
                for (var x = 0; x < lon; x++)
                {
                    var a = y * (lon + 1) + x;
                    var b = a + lon + 1;
                    tris[t++] = a;
                    tris[t++] = b;
                    tris[t++] = a + 1;
                    tris[t++] = a + 1;
                    tris[t++] = b;
                    tris[t++] = b + 1;
                }
            }

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
