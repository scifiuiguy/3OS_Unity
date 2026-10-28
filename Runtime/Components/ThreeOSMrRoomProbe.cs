using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// MR-only: on start, cast azimuth (+ up/down) rays at WorldMesh* colliders to estimate
    /// a room AABB. Shrinks voodoo field radius to the room diagonal when shorter than the
    /// open-field default, and publishes hard room bounds to the kernel.
    /// </summary>
    public sealed class ThreeOSMrRoomProbe : MonoBehaviour
    {
        private const string WorldMeshPrefix = "WorldMesh";
        private const float DefaultFieldRadiusM = 15.24f; // 50 ft
        private const float DefaultFieldHeightM = 9.144f; // 30 ft
        private const float MaxRayM = 40f;
        private const int AzimuthRays = 36; // every 10°
        private const float ProbeDelayS = 0.35f;

        [SerializeField] private ThreeOSBridge bridge;
        [SerializeField] private bool drawGizmos = true;

        private Bounds _roomBounds;
        private bool _haveRoom;
        private InteropVoodooFieldParams _openFieldParams;
        private bool _haveOpenFieldParams;

        public bool HaveRoom => _haveRoom;
        public Bounds RoomBounds => _roomBounds;

        private void Awake()
        {
            if (bridge == null)
                bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
        }

        private void OnEnable()
        {
            StartCoroutine(ProbeWhenReady());
        }

        private IEnumerator ProbeWhenReady()
        {
            // Wait for XR/camera + WorldMesh colliders to exist.
            yield return new WaitForSecondsRealtime(ProbeDelayS);
            for (var i = 0; i < 90 && (bridge == null || !bridge.IsLoaded); i++)
                yield return null;
            if (bridge == null || !bridge.IsLoaded)
                yield break;

            if (!_haveOpenFieldParams && bridge.TryGetVoodooFieldParams(out _openFieldParams))
                _haveOpenFieldParams = true;

            // Retry a few times if meshes spawn late.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                if (TryProbeRoom(out _roomBounds))
                {
                    _haveRoom = true;
                    ApplyMrFieldAndBounds(_roomBounds);
                    Debug.Log(
                        $"[3OS][MR] Room probe OK size={_roomBounds.size} diagonal={RoomDiagonal(_roomBounds):F2}m");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            Debug.LogWarning("[3OS][MR] Room probe: no WorldMesh hits — keeping open-field params");
        }

        private static float RoomDiagonal(Bounds b)
        {
            var s = b.size;
            return Mathf.Sqrt(s.x * s.x + s.y * s.y + s.z * s.z);
        }

        private void ApplyMrFieldAndBounds(Bounds unityBounds)
        {
            if (bridge == null || !bridge.IsLoaded)
                return;

            var openRadius = _haveOpenFieldParams ? _openFieldParams.fieldRadiusM : DefaultFieldRadiusM;
            var openHeight = _haveOpenFieldParams ? _openFieldParams.fieldHeightM : DefaultFieldHeightM;
            if (openRadius < 1f) openRadius = DefaultFieldRadiusM;
            if (openHeight < 1f) openHeight = DefaultFieldHeightM;

            // Longest room diagonal caps field reach (and thus stick→world sensitivity).
            var diag = RoomDiagonal(unityBounds);
            var radius = Mathf.Min(openRadius, Mathf.Max(diag, 1f));
            // Vertical travel: room height (not open-field 30 ft).
            var height = Mathf.Min(openHeight, Mathf.Max(unityBounds.size.y, 1f));

            if (!bridge.TryGetVoodooFieldParams(out var p))
            {
                p = _haveOpenFieldParams ? _openFieldParams : default;
                if (p.voxelSizeM < 1e-4f)
                {
                    p.voxelSizeM = 0.0762f;
                    p.nearBbVoxels = new ThreeOSIVec3 { x = 4, y = 6, z = 4 };
                    p.armReachM = 0.3048f;
                }
            }

            p.fieldRadiusM = radius;
            p.fieldHeightM = height;
            bridge.SetVoodooFieldParams(p);

            var minU = unityBounds.min;
            var maxU = unityBounds.max;
            var minOx = InteropStructs.UnityToOpenXrPosition(minU);
            var maxOx = InteropStructs.UnityToOpenXrPosition(maxU);
            // After X mirror, re-order min/max.
            var oxMin = new ThreeOSVec3
            {
                x = Mathf.Min(minOx.x, maxOx.x),
                y = Mathf.Min(minOx.y, maxOx.y),
                z = Mathf.Min(minOx.z, maxOx.z)
            };
            var oxMax = new ThreeOSVec3
            {
                x = Mathf.Max(minOx.x, maxOx.x),
                y = Mathf.Max(minOx.y, maxOx.y),
                z = Mathf.Max(minOx.z, maxOx.z)
            };
            bridge.SetVoodooRoomBounds(oxMin, oxMax, true);
        }

        /// <summary>
        /// Cast rays along player azimuth (+ up/down). Hits must be WorldMesh* colliders.
        /// </summary>
        private bool TryProbeRoom(out Bounds bounds)
        {
            bounds = default;
            var cam = Camera.main;
            if (cam == null)
                return false;

            var origin = cam.transform.position;
            // Keep origin slightly above floor if possible.
            var hits = new List<Vector3>(AzimuthRays + 4);
            var yaw0 = cam.transform.eulerAngles.y * Mathf.Deg2Rad;

            for (var i = 0; i < AzimuthRays; i++)
            {
                var a = yaw0 + (i / (float)AzimuthRays) * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                if (RaycastWorldMesh(origin, dir, MaxRayM, out var p))
                    hits.Add(p);
            }

            // Floor / ceiling.
            if (RaycastWorldMesh(origin, Vector3.down, MaxRayM, out var floor))
                hits.Add(floor);
            if (RaycastWorldMesh(origin, Vector3.up, MaxRayM, out var ceil))
                hits.Add(ceil);

            // A few pitched azimuth samples to catch shorter walls / soffits.
            for (var i = 0; i < 8; i++)
            {
                var a = yaw0 + (i / 8f) * Mathf.PI * 2f;
                var flat = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var up = (flat + Vector3.up * 0.35f).normalized;
                var dn = (flat + Vector3.down * 0.35f).normalized;
                if (RaycastWorldMesh(origin, up, MaxRayM, out var pu))
                    hits.Add(pu);
                if (RaycastWorldMesh(origin, dn, MaxRayM, out var pd))
                    hits.Add(pd);
            }

            if (hits.Count < 4)
                return false;

            var min = hits[0];
            var max = hits[0];
            for (var i = 1; i < hits.Count; i++)
            {
                min = Vector3.Min(min, hits[i]);
                max = Vector3.Max(max, hits[i]);
            }

            // Ensure origin is inside the AABB (pads for missed angles).
            min = Vector3.Min(min, origin);
            max = Vector3.Max(max, origin);

            // Slight inset so bounds sit on the inner face of hit walls.
            const float inset = 0.02f;
            min += new Vector3(inset, inset, inset);
            max -= new Vector3(inset, inset, inset);
            if (max.x <= min.x || max.y <= min.y || max.z <= min.z)
                return false;

            bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds.size.sqrMagnitude > 0.25f;
        }

        private static bool RaycastWorldMesh(Vector3 origin, Vector3 dir, float maxDist,
            out Vector3 point)
        {
            point = default;
            var rays = Physics.RaycastAll(origin, dir, maxDist, ~0, QueryTriggerInteraction.Ignore);
            if (rays == null || rays.Length == 0)
                return false;

            System.Array.Sort(rays, (a, b) => a.distance.CompareTo(b.distance));
            for (var i = 0; i < rays.Length; i++)
            {
                var col = rays[i].collider;
                if (col == null)
                    continue;
                if (!IsWorldMeshCollider(col))
                    continue;
                point = rays[i].point;
                return true;
            }

            return false;
        }

        private static bool IsWorldMeshCollider(Collider col)
        {
            for (var t = col.transform; t != null; t = t.parent)
            {
                if (IsWorldMeshName(t.name))
                    return true;
            }

            return false;
        }

        private static bool IsWorldMeshName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            if (n == WorldMeshPrefix)
                return true;
            if (n.StartsWith(WorldMeshPrefix + " ", System.StringComparison.Ordinal))
                return true;
            if (n.StartsWith(WorldMeshPrefix + "-", System.StringComparison.Ordinal))
                return true;
            if (n.StartsWith(WorldMeshPrefix + " (", System.StringComparison.Ordinal))
                return true;
            return false;
        }

        /// <summary>
        /// Storage clear resets kernel room bounds + field params; re-publish MR probe when
        /// WorldMesh enclosure is active again.
        /// </summary>
        public void RestoreKernelMrBounds()
        {
            if (bridge == null)
                bridge = FindFirstObjectByType<ThreeOSBridge>();
            if (bridge == null || !bridge.IsLoaded)
                return;
            if (_haveRoom)
            {
                ApplyMrFieldAndBounds(_roomBounds);
                return;
            }

            if (TryProbeRoom(out _roomBounds))
            {
                _haveRoom = true;
                ApplyMrFieldAndBounds(_roomBounds);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !_haveRoom)
                return;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.35f);
            Gizmos.DrawWireCube(_roomBounds.center, _roomBounds.size);
        }
    }
}
