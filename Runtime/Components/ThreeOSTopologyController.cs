using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Phase 0.2 Quest verification: Menu/Select/Grab drive dome → lattice → portal teleports.
    /// </summary>
    public sealed class ThreeOSTopologyController : MonoBehaviour
    {
        public const ulong ProxyA = 1;
        public const ulong ProxyB = 2;

        private ThreeOSBridge _bridge;
        private ThreeOSInputRouter _input;
        private ThreeOSWorldProxyDome _domeVisual;
        private ThreeOSDoubleProxyLattice _latticeA;
        private ThreeOSDoubleProxyLattice _latticeB;
        private ThreeOSMovable _cube;

        private bool _menuWasDown;
        private bool _selectWasDown;
        private bool _grabWasDown;
        private bool _holding;
        private bool _objectInFar;
        private string _status = "idle";

        public string Status => _status;
        public bool DomeActive { get; private set; }
        public bool LatticeActive { get; private set; }
        public int PortalCount { get; private set; }

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
            _input = GetComponent<ThreeOSInputRouter>() ?? FindFirstObjectByType<ThreeOSInputRouter>();

            _domeVisual = FindOrCreateDome();
            _latticeA = FindOrCreateLattice("3OS_DoubleProxy_A");
            _latticeB = FindOrCreateLattice("3OS_DoubleProxy_B");
            _cube = FindOrCreateCube();
        }

        private void Update()
        {
            if (_bridge == null || !_bridge.IsLoaded || _input == null)
            {
                return;
            }

            var frame = _input.LastFrame;
            var menu = (frame.buttonFlags & (uint)ButtonFlags.Menu) != 0;
            var select = (frame.buttonFlags & (uint)(ButtonFlags.RightSelect | ButtonFlags.LeftSelect)) != 0;
            var grab = (frame.buttonFlags & (uint)(ButtonFlags.RightGrab | ButtonFlags.LeftGrab)) != 0;

            if (menu && !_menuWasDown)
            {
                ToggleDome(frame.head);
            }

            if (select && !_selectWasDown && DomeActive)
            {
                OpenLattices(frame.head);
            }

            // Portal grab only while lattices are open; otherwise kinematics owns grip.
            if (LatticeActive)
            {
                if (grab && !_grabWasDown)
                {
                    BeginHold();
                }

                if (_holding && grab)
                {
                    FollowAim(frame);
                }

                if (!grab && _grabWasDown && _holding)
                {
                    EndHoldPortal();
                }
            }
            else if (_holding)
            {
                _holding = false;
            }

            _menuWasDown = menu;
            _selectWasDown = select;
            _grabWasDown = grab;

            SyncVisuals();
        }

        private void ToggleDome(ThreeOSPose head)
        {
            if (DomeActive)
            {
                _bridge.CloseDome();
                DomeActive = false;
                _status = "dome closed";
                return;
            }

            if (_bridge.OpenDome(head))
            {
                DomeActive = true;
                _status = "dome open — Select opens lattice";
            }
            else
            {
                _status = $"dome open failed: {_bridge.LastError}";
            }
        }

        private void OpenLattices(ThreeOSPose head)
        {
            var farA = new ThreeOSAabb
            {
                min = new ThreeOSVec3 { x = -1f, y = 0f, z = -6f },
                max = new ThreeOSVec3 { x = 1f, y = 2f, z = -4f }
            };
            var farB = new ThreeOSAabb
            {
                min = new ThreeOSVec3 { x = 3f, y = 0f, z = -6f },
                max = new ThreeOSVec3 { x = 5f, y = 2f, z = -4f }
            };

            var okA = _bridge.OpenDoubleProxy(ProxyA, farA, head);
            var okB = _bridge.OpenDoubleProxy(ProxyB, farB, head);
            LatticeActive = okA || okB;
            _status = LatticeActive
                ? "lattice open — Grab cube, release in yellow near box to portal"
                : $"lattice failed: {_bridge.LastError}";
        }

        private void BeginHold()
        {
            if (_cube == null) return;
            _holding = true;
            _status = "holding";
        }

        private void FollowAim(InteropInputFrame frame)
        {
            if (_cube == null) return;
            var aim = (frame.trackingFlags & (uint)TrackingFlags.RightAim) != 0
                ? frame.rightAim
                : frame.leftAim;
            var pose = InteropStructs.ToUnityPose(aim);
            _cube.transform.position = pose.position + pose.rotation * Vector3.forward * 0.25f;
            _cube.transform.rotation = pose.rotation;
        }

        private void EndHoldPortal()
        {
            _holding = false;
            if (_cube == null || _bridge == null || !LatticeActive)
            {
                _status = "released";
                return;
            }

            var nearPose = _cube.CurrentOpenXrPose();

            if (!_objectInFar)
            {
                if (_bridge.PortalNearToFar(ProxyA, nearPose, out var farPose))
                {
                    _cube.ApplyOpenXrPose(farPose);
                    _objectInFar = true;
                    PortalCount++;
                    _status = $"portaled near→far A (#{PortalCount})";
                    return;
                }

                if (_bridge.PortalNearToFar(ProxyB, nearPose, out farPose))
                {
                    _cube.ApplyOpenXrPose(farPose);
                    _objectInFar = true;
                    PortalCount++;
                    _status = $"portaled near→far B (#{PortalCount})";
                    return;
                }

                _status = "release outside near lattice (no portal)";
                return;
            }

            // Object was in far; grip pulled it to the hand (near). Release decides destination.
            if (_bridge.TryGetDoubleProxy(ProxyB, out var proxyB) &&
                Contains(proxyB.nearAabb, nearPose.position) &&
                _bridge.PortalCross(ProxyA, ProxyB, nearPose, out var crossFar))
            {
                _cube.ApplyOpenXrPose(crossFar);
                PortalCount++;
                _status = $"cross-proxy A→B (#{PortalCount})";
                return;
            }

            if (_bridge.TryGetDoubleProxy(ProxyA, out var proxyA) &&
                Contains(proxyA.nearAabb, nearPose.position) &&
                _bridge.PortalNearToFar(ProxyA, nearPose, out var againFar))
            {
                _cube.ApplyOpenXrPose(againFar);
                PortalCount++;
                _status = $"re-dropped near→far A (#{PortalCount})";
                return;
            }

            // Retrieved into near field (leave at hand).
            _objectInFar = false;
            PortalCount++;
            _status = $"retrieved to near (#{PortalCount})";
        }

        private void SyncVisuals()
        {
            if (_bridge.TryGetDome(out var dome))
            {
                DomeActive = dome.active != 0;
                _domeVisual?.Apply(dome);
            }

            if (_bridge.TryGetDoubleProxy(ProxyA, out var a))
            {
                _latticeA?.Apply(a);
            }

            if (_bridge.TryGetDoubleProxy(ProxyB, out var b))
            {
                _latticeB?.Apply(b);
            }
        }

        private static bool Contains(ThreeOSAabb aabb, ThreeOSVec3 p) =>
            p.x >= aabb.min.x && p.x <= aabb.max.x &&
            p.y >= aabb.min.y && p.y <= aabb.max.y &&
            p.z >= aabb.min.z && p.z <= aabb.max.z;

        private static ThreeOSWorldProxyDome FindOrCreateDome()
        {
            var existing = FindFirstObjectByType<ThreeOSWorldProxyDome>();
            if (existing != null) return existing;
            var go = new GameObject("3OS_WorldProxyDome");
            return go.AddComponent<ThreeOSWorldProxyDome>();
        }

        private static ThreeOSDoubleProxyLattice FindOrCreateLattice(string name)
        {
            var go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);
            return go.GetComponent<ThreeOSDoubleProxyLattice>() ?? go.AddComponent<ThreeOSDoubleProxyLattice>();
        }

        private static ThreeOSMovable FindOrCreateCube()
        {
            var existing = FindFirstObjectByType<ThreeOSMovable>();
            if (existing != null) return existing;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "3OS_PortalCube";
            go.transform.position = new Vector3(0f, 1.2f, 1.2f);
            go.transform.localScale = Vector3.one * 0.15f;
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = new Material(Shader.Find("Sprites/Default")) { color = Color.magenta };
            }
            return go.AddComponent<ThreeOSMovable>();
        }
    }
}
