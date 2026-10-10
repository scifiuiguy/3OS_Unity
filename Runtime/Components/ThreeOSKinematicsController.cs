using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Phase 0.3 Quest gate: possess nearby cube with grip (when lattices are off),
    /// grab-anchored stick velocity via kernel, release to coast onto the floor plane.
    /// Stick debug ray endpoints come from the kernel; Unity only draws.
    /// </summary>
    public sealed class ThreeOSKinematicsController : MonoBehaviour
    {
        public float PossessDistance = 0.45f;
        public float FloorY = 0f;
        /// <summary>Stick meters → object m/s (gain 10 ⇒ ~1 ft stick ≈ 3 m/s).</summary>
        public float Gain = 10f;
        /// <summary>Min |hand - grabOrigin| before drive engages.</summary>
        public float DeadzoneM = 0.01f;
        public float Friction = 3.5f;

        private ThreeOSBridge _bridge;
        private ThreeOSInputRouter _input;
        private ThreeOSTopologyController _topology;
        private ThreeOSMovable _target;
        private GameObject _floorVisual;
        private LineRenderer _stickRay;

        private bool _grabWasDown;
        private bool _possessing;
        private string _status = "idle — grab near cube; stick = hand - grab origin";

        public string Status => _status;
        public bool IsPossessing => _possessing;
        public KineticPhase Phase { get; private set; }

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
            _input = GetComponent<ThreeOSInputRouter>() ?? FindFirstObjectByType<ThreeOSInputRouter>();
            _topology = GetComponent<ThreeOSTopologyController>() ??
                        FindFirstObjectByType<ThreeOSTopologyController>();
            _target = FindFirstObjectByType<ThreeOSMovable>();
            EnsureFloorVisual();
            EnsureStickRay();
        }

        private void Start()
        {
            if (_bridge != null && _bridge.IsLoaded)
            {
                _bridge.SetKinematicsParams(Gain, DeadzoneM, Friction, FloorY);
            }
        }

        private void Update()
        {
            if (_bridge == null || !_bridge.IsLoaded || _input == null || _target == null)
            {
                HideStickRay();
                return;
            }

            // Phase 0.4 storage demo owns grip for workspace glyphs (stick ray still via LateUpdate).
            var storage = FindFirstObjectByType<ThreeOSStorageController>();
            if (storage != null && storage.DemoActive)
            {
                SyncPhase();
                return;
            }

            // Portal lattice mode owns grip; kinematics yields.
            if (_topology != null && _topology.LatticeActive)
            {
                if (_possessing)
                {
                    _bridge.ReleasePossessed();
                    _possessing = false;
                    _status = "yielded to portal lattice";
                }
                HideStickRay();
                SyncPhase();
                return;
            }

            var frame = _input.LastFrame;
            var grab = (frame.buttonFlags & (uint)(ButtonFlags.RightGrab | ButtonFlags.LeftGrab)) != 0;
            var aim = AimPose(frame);

            if (grab && !_grabWasDown)
            {
                TryPossess(aim);
            }

            if (!grab && _grabWasDown && _possessing)
            {
                _bridge.ReleasePossessed();
                _possessing = false;
                _status = "released — coasting";
                HideStickRay();
            }

            _grabWasDown = grab;
            SyncPhase();
        }

        private void LateUpdate()
        {
            // After InputRouter.Tick so kernel stick_debug matches this frame.
            UpdateStickRay();
        }

        private void TryPossess(ThreeOSPose aim)
        {
            var aimUnity = InteropStructs.ToUnityPose(aim).position;
            if (Vector3.Distance(aimUnity, _target.transform.position) > PossessDistance)
            {
                _status = "grab missed (too far)";
                return;
            }

            if (_bridge.Possess(_target.EntityId, _target.CurrentOpenXrPose()))
            {
                _possessing = true;
                _status = "possessed — pull from grab origin to glide";
            }
            else
            {
                _status = $"possess failed: {_bridge.LastError}";
            }
        }

        private void SyncPhase()
        {
            if (_bridge.TryGetKineticState(out var ks))
            {
                Phase = (KineticPhase)ks.phase;
                if (Phase == KineticPhase.Coasting)
                {
                    _status = "coasting";
                }
                else if (Phase == KineticPhase.Idle && !_possessing)
                {
                    if (_status.StartsWith("coasting") || _status.StartsWith("released"))
                    {
                        _status = "settled";
                    }
                }
            }
        }

        private void UpdateStickRay()
        {
            // Drive from kernel stick_debug (works for StorageController possess too —
            // do not require this component's local _possessing flag).
            if (_stickRay == null || _bridge == null || !_bridge.IsLoaded)
            {
                HideStickRay();
                return;
            }

            if (!_bridge.TryGetStickDebug(out var dbg) || dbg.active == 0)
            {
                HideStickRay();
                return;
            }

            var rayStart = InteropStructs.OpenXrToUnityPosition(dbg.rayOrigin.ToVector3());
            var rayEnd = InteropStructs.OpenXrToUnityPosition(dbg.rayTip.ToVector3());

            _stickRay.enabled = true;
            _stickRay.positionCount = 2;
            _stickRay.SetPosition(0, rayStart);
            _stickRay.SetPosition(1, rayEnd);

            var color = dbg.inDeadzone != 0
                ? new Color(0.4f, 0.75f, 1f, 0.35f)
                : new Color(0.15f, 0.95f, 1f, 0.95f);
            _stickRay.startColor = _stickRay.endColor = color;
        }

        private void EnsureStickRay()
        {
            var go = GameObject.Find("3OS_StickVelocityRay");
            if (go == null)
            {
                go = new GameObject("3OS_StickVelocityRay");
            }

            _stickRay = go.GetComponent<LineRenderer>();
            if (_stickRay == null)
            {
                _stickRay = go.AddComponent<LineRenderer>();
            }

            _stickRay.material = new Material(Shader.Find("Sprites/Default"));
            _stickRay.widthMultiplier = 0.008f;
            _stickRay.useWorldSpace = true;
            _stickRay.loop = false;
            _stickRay.positionCount = 0;
            _stickRay.numCapVertices = 4;
            _stickRay.enabled = false;
        }

        private void HideStickRay()
        {
            if (_stickRay == null)
            {
                return;
            }

            _stickRay.enabled = false;
            _stickRay.positionCount = 0;
        }

        private static ThreeOSPose AimPose(InteropInputFrame frame)
        {
            if ((frame.trackingFlags & (uint)TrackingFlags.RightAim) != 0)
            {
                return frame.rightAim;
            }
            if ((frame.trackingFlags & (uint)TrackingFlags.LeftAim) != 0)
            {
                return frame.leftAim;
            }
            return frame.head;
        }

        private void EnsureFloorVisual()
        {
            var existing = GameObject.Find("3OS_FloorPlane");
            if (existing != null)
            {
                _floorVisual = existing;
                return;
            }

            _floorVisual = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _floorVisual.name = "3OS_FloorPlane";
            _floorVisual.transform.position = new Vector3(0f, FloorY, 0f);
            _floorVisual.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
            var rend = _floorVisual.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.material = new Material(Shader.Find("Sprites/Default"))
                {
                    color = new Color(0.35f, 0.35f, 0.4f, 0.55f)
                };
            }
            var col = _floorVisual.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }
    }
}
