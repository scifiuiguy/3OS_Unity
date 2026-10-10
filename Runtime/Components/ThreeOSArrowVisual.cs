using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// World-space shaft + conical head; deadzone shows a sphere plus a thin ghost line.
    /// </summary>
    public sealed class ThreeOSArrowVisual
    {
        private readonly LineRenderer _shaft;
        private readonly LineRenderer _ghostLine;
        private readonly Transform _head;
        private readonly MeshRenderer _headRenderer;
        private readonly Transform _deadzoneSphere;
        private readonly MeshRenderer _sphereRenderer;
        private Material _material;
        private Material _ghostMaterial;
        private Material _sphereMaterial;
        private static Mesh _coneMesh;

        private ThreeOSArrowVisual(LineRenderer shaft, LineRenderer ghostLine, Transform head,
            MeshRenderer headRenderer, Transform deadzoneSphere, MeshRenderer sphereRenderer,
            Material material, Material ghostMaterial, Material sphereMaterial)
        {
            _shaft = shaft;
            _ghostLine = ghostLine;
            _head = head;
            _headRenderer = headRenderer;
            _deadzoneSphere = deadzoneSphere;
            _sphereRenderer = sphereRenderer;
            _material = material;
            _ghostMaterial = ghostMaterial;
            _sphereMaterial = sphereMaterial;
        }

        public static ThreeOSArrowVisual Create(string name, float shaftWidth)
        {
            var root = GameObject.Find(name);
            if (root == null)
            {
                root = new GameObject(name);
            }

            var shaft = root.GetComponent<LineRenderer>();
            if (shaft == null)
            {
                shaft = root.AddComponent<LineRenderer>();
            }

            var mat = new Material(Shader.Find("Sprites/Default"));
            shaft.sharedMaterial = mat;
            shaft.widthMultiplier = shaftWidth;
            shaft.useWorldSpace = true;
            shaft.loop = false;
            shaft.positionCount = 0;
            shaft.numCapVertices = 4;
            shaft.enabled = false;

            var ghostGo = EnsureChild(root.transform, "GhostStick");
            var ghost = ghostGo.GetComponent<LineRenderer>();
            if (ghost == null)
            {
                ghost = ghostGo.AddComponent<LineRenderer>();
            }

            var ghostMat = new Material(Shader.Find("Sprites/Default"));
            ghost.sharedMaterial = ghostMat;
            ghost.widthMultiplier = 0.0025f;
            ghost.useWorldSpace = true;
            ghost.loop = false;
            ghost.positionCount = 0;
            ghost.numCapVertices = 2;
            ghost.sortingOrder = 20;
            ghost.enabled = false;

            var headGo = EnsureChild(root.transform, "Head");
            var mf = headGo.GetComponent<MeshFilter>();
            if (mf == null)
            {
                mf = headGo.AddComponent<MeshFilter>();
            }

            mf.sharedMesh = SharedConeMesh();
            var mr = headGo.GetComponent<MeshRenderer>();
            if (mr == null)
            {
                mr = headGo.AddComponent<MeshRenderer>();
            }

            mr.sharedMaterial = mat;
            headGo.SetActive(false);

            var sphereGo = EnsureChild(root.transform, "DeadzoneSphere");
            var smf = sphereGo.GetComponent<MeshFilter>();
            var smr = sphereGo.GetComponent<MeshRenderer>();
            if (smf == null || smr == null || smf.sharedMesh == null)
            {
                foreach (var c in sphereGo.GetComponents<Collider>())
                {
                    Object.Destroy(c);
                }

                if (smf == null)
                {
                    smf = sphereGo.AddComponent<MeshFilter>();
                }

                if (smr == null)
                {
                    smr = sphereGo.AddComponent<MeshRenderer>();
                }

                var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                smf.sharedMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmp);
            }

            var sphereMat = CreateTransparentSphereMaterial();
            smr.sharedMaterial = sphereMat;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            smr.receiveShadows = false;
            sphereGo.transform.localScale = Vector3.one * 0.015f;
            sphereGo.SetActive(false);

            return new ThreeOSArrowVisual(shaft, ghost, headGo.transform, mr, sphereGo.transform, smr,
                mat, ghostMat, sphereMat);
        }

        public void Set(Vector3 from, Vector3 to, Color color)
        {
            HideDeadzone();
            var delta = to - from;
            var len = delta.magnitude;
            if (len < 1e-4f)
            {
                HideArrow();
                return;
            }

            var dir = delta / len;
            var headLen = Mathf.Clamp(len * 0.4f, 0.012f, 0.032f);
            var headRad = headLen * 0.42f;
            if (headLen > len * 0.85f)
            {
                headLen = len * 0.85f;
                headRad = headLen * 0.42f;
            }

            var shaftEnd = to - dir * headLen;
            ApplyColor(color);

            _shaft.enabled = true;
            _shaft.positionCount = 2;
            _shaft.SetPosition(0, from);
            _shaft.SetPosition(1, shaftEnd);
            _shaft.startColor = _shaft.endColor = color;
            _shaft.widthMultiplier = Mathf.Clamp(headRad * 0.55f, 0.004f, 0.012f);

            _head.gameObject.SetActive(true);
            _head.position = to;
            _head.rotation = Quaternion.FromToRotation(Vector3.up, dir);
            _head.localScale = new Vector3(headRad / 0.5f, headLen, headRad / 0.5f);
            if (_headRenderer != null)
            {
                _headRenderer.enabled = true;
            }
        }

        /// <summary>
        /// Deadzone affordance: semitransparent sphere + thin ghost line to hand tip.
        /// <paramref name="sphereDiameterM"/> should be 2× deadzone radius so the tip
        /// reaches the shell exactly at exit.
        /// </summary>
        public void SetDeadzone(Vector3 from, Vector3 to, Color sphereColor, Color ghostColor,
            float sphereDiameterM = 0.03f)
        {
            HideArrow();
            if (_deadzoneSphere != null)
            {
                _deadzoneSphere.gameObject.SetActive(true);
                _deadzoneSphere.position = from;
                _deadzoneSphere.localScale = Vector3.one * Mathf.Max(1e-4f, sphereDiameterM);
                if (_sphereMaterial != null)
                {
                    // Keep the shell see-through so the ghost stick stays readable inside.
                    var c = sphereColor;
                    c.a = Mathf.Min(c.a, 0.25f);
                    ApplyTransparentColor(_sphereMaterial, c);
                }

                if (_sphereRenderer != null)
                {
                    _sphereRenderer.enabled = true;
                    _sphereRenderer.sharedMaterial = _sphereMaterial;
                }
            }

            if (_ghostLine == null)
            {
                return;
            }

            if (_ghostMaterial != null)
            {
                _ghostMaterial.color = ghostColor;
            }

            _ghostLine.enabled = true;
            _ghostLine.positionCount = 2;
            _ghostLine.SetPosition(0, from);
            _ghostLine.SetPosition(1, to);
            _ghostLine.startColor = _ghostLine.endColor = ghostColor;
            _ghostLine.widthMultiplier = 0.0025f;
            _ghostLine.sortingOrder = 20;
        }

        public void Hide()
        {
            HideArrow();
            HideDeadzone();
        }

        private void HideArrow()
        {
            if (_shaft != null)
            {
                _shaft.enabled = false;
                _shaft.positionCount = 0;
            }

            if (_head != null)
            {
                _head.gameObject.SetActive(false);
            }
        }

        private void HideDeadzone()
        {
            if (_deadzoneSphere != null)
            {
                _deadzoneSphere.gameObject.SetActive(false);
            }

            if (_ghostLine != null)
            {
                _ghostLine.enabled = false;
                _ghostLine.positionCount = 0;
            }
        }

        private void ApplyColor(Color color)
        {
            if (_material != null)
            {
                _material.color = color;
            }
        }

        private static void ApplyTransparentColor(Material mat, Color c)
        {
            mat.color = c;
            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", c);
            }

            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", c);
            }
        }

        private static Material CreateTransparentSphereMaterial()
        {
            // Prefer shaders that alpha-blend without opaque Z-write so the ghost line shows through.
            var shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Transparent")
                         ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                         ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader);
            var c = new Color(0.4f, 0.75f, 1f, 0.25f);
            ApplyTransparentColor(mat, c);
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f);
            }

            if (mat.HasProperty("_ZWrite"))
            {
                mat.SetFloat("_ZWrite", 0f);
            }

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        private static GameObject EnsureChild(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null)
            {
                return t.gameObject;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Mesh SharedConeMesh()
        {
            if (_coneMesh != null)
            {
                return _coneMesh;
            }

            const int segments = 14;
            var verts = new Vector3[segments + 2];
            var norms = new Vector3[segments + 2];
            verts[0] = Vector3.zero;
            norms[0] = Vector3.up;
            verts[1] = new Vector3(0f, -1f, 0f);
            norms[1] = Vector3.down;
            for (var i = 0; i < segments; i++)
            {
                var a = (Mathf.PI * 2f * i) / segments;
                verts[i + 2] = new Vector3(Mathf.Cos(a) * 0.5f, -1f, Mathf.Sin(a) * 0.5f);
                norms[i + 2] = new Vector3(Mathf.Cos(a), 0.35f, Mathf.Sin(a)).normalized;
            }

            var tris = new int[segments * 6];
            var t = 0;
            for (var i = 0; i < segments; i++)
            {
                var cur = i + 2;
                var next = (i + 1) % segments + 2;
                tris[t++] = 0;
                tris[t++] = cur;
                tris[t++] = next;
                tris[t++] = 1;
                tris[t++] = next;
                tris[t++] = cur;
            }

            _coneMesh = new Mesh { name = "ThreeOSArrowCone" };
            _coneMesh.vertices = verts;
            _coneMesh.normals = norms;
            _coneMesh.triangles = tris;
            _coneMesh.RecalculateBounds();
            return _coneMesh;
        }
    }
}
