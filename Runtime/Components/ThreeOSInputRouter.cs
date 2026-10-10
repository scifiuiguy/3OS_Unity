using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;

namespace ThreeOS
{
    /// <summary>
    /// XR head / controllers → <see cref="InteropInputFrame"/> (OpenXR space).
    /// Soft-fails when tracking is missing.
    /// </summary>
    public sealed class ThreeOSInputRouter : MonoBehaviour
    {
        /// <summary>Placeholder hand tip diameter (1/3 of dismiss-X sphere).</summary>
        public const float HandTipDiameterM = ThreeOSStorageController.DismissXDiameterM / 3f;

        private ThreeOSBridge _bridge;
        private Transform _headTransform;
        private Transform _leftTip;
        private Transform _rightTip;

        public InteropInputFrame LastFrame { get; private set; }
        public bool LastTickOk { get; private set; }

        private ulong _frameIndex;

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>();
            if (_bridge == null)
            {
                _bridge = FindFirstObjectByType<ThreeOSBridge>();
            }

            if (Camera.main != null)
            {
                _headTransform = Camera.main.transform;
            }

            _leftTip = CreateHandTipSphere("3OS_LeftHandTip").transform;
            _rightTip = CreateHandTipSphere("3OS_RightHandTip").transform;
        }

        private void OnDestroy()
        {
            if (_leftTip != null)
            {
                Destroy(_leftTip.gameObject);
            }

            if (_rightTip != null)
            {
                Destroy(_rightTip.gameObject);
            }
        }

        private void Update()
        {
            if (_bridge == null || !_bridge.IsLoaded)
            {
                LastTickOk = false;
                SetTipActive(_leftTip, false);
                SetTipActive(_rightTip, false);
                return;
            }

            var frame = new InteropInputFrame
            {
                frameIndex = _frameIndex++,
                timeSeconds = Time.timeAsDouble
            };

            if (_headTransform != null)
            {
                frame.head = InteropStructs.ToOpenXrPose(new Pose(_headTransform.position, _headTransform.rotation));
                frame.trackingFlags |= (uint)TrackingFlags.Head;
            }

            FillController(XRController.leftHand, isLeft: true, ref frame);
            FillController(XRController.rightHand, isLeft: false, ref frame);

            LastFrame = frame;
            LastTickOk = _bridge.Tick(ref frame);
            UpdateHandTipVisuals(frame);
        }

        private void UpdateHandTipVisuals(InteropInputFrame frame)
        {
            UpdateTip(_leftTip, (frame.trackingFlags & (uint)TrackingFlags.LeftAim) != 0, frame.leftAim);
            UpdateTip(_rightTip, (frame.trackingFlags & (uint)TrackingFlags.RightAim) != 0, frame.rightAim);
        }

        private static void UpdateTip(Transform tip, bool tracked, ThreeOSPose openXrPose)
        {
            if (tip == null)
            {
                return;
            }

            if (!tracked)
            {
                SetTipActive(tip, false);
                return;
            }

            var unity = InteropStructs.ToUnityPose(openXrPose);
            tip.SetPositionAndRotation(unity.position, unity.rotation);
            SetTipActive(tip, true);
        }

        private static void SetTipActive(Transform tip, bool active)
        {
            if (tip != null && tip.gameObject.activeSelf != active)
            {
                tip.gameObject.SetActive(active);
            }
        }

        private static GameObject CreateHandTipSphere(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.localScale = Vector3.one * HandTipDiameterM;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            var mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default") ??
                                   Shader.Find("Universal Render Pipeline/Unlit"));
            var c = new Color(1f, 1f, 1f, 0.9f);
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", c);
            }

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", c);
            }

            mat.color = c;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.SetActive(false);
            return go;
        }

        private static void FillController(XRController controller, bool isLeft, ref InteropInputFrame frame)
        {
            if (controller == null)
            {
                return;
            }

            Vector3 pos;
            Quaternion rot;
            try
            {
                pos = controller.devicePosition.ReadValue();
                rot = controller.deviceRotation.ReadValue();
            }
            catch
            {
                return;
            }

            // Treat zeroed/uninitialized poses as untracked.
            if (pos.sqrMagnitude < 1e-8f && rot == Quaternion.identity && !controller.added)
            {
                return;
            }

            var pose = InteropStructs.ToOpenXrPose(new Pose(pos, rot));
            if (isLeft)
            {
                frame.leftAim = pose;
                frame.leftGrip = pose;
                frame.trackingFlags |= (uint)(TrackingFlags.LeftAim | TrackingFlags.LeftGrip);
            }
            else
            {
                frame.rightAim = pose;
                frame.rightGrip = pose;
                frame.trackingFlags |= (uint)(TrackingFlags.RightAim | TrackingFlags.RightGrip);
            }

            try
            {
                var trigger = controller.TryGetChildControl<AxisControl>("trigger");
                var grip = controller.TryGetChildControl<AxisControl>("grip");
                var primaryButton = controller.TryGetChildControl<ButtonControl>("primaryButton");
                var secondaryButton = controller.TryGetChildControl<ButtonControl>("secondaryButton");
                var menuButton = controller.TryGetChildControl<ButtonControl>("menuButton");
                var gripButton = controller.TryGetChildControl<ButtonControl>("gripButton");

                if (isLeft)
                {
                    if (trigger != null) frame.leftTrigger = trigger.ReadValue();
                    if (grip != null) frame.leftGripValue = grip.ReadValue();
                    // Index trigger drives Select (selection / lattice); A/X still works as backup.
                    var leftSelect = (trigger != null && trigger.ReadValue() >= 0.55f) ||
                                     (primaryButton != null && primaryButton.isPressed);
                    if (leftSelect)
                        frame.buttonFlags |= (uint)ButtonFlags.LeftSelect;
                    // Grip still flags Grab for topology portals; storage drag uses trigger hold.
                    if (gripButton != null && gripButton.isPressed)
                        frame.buttonFlags |= (uint)ButtonFlags.LeftGrab;
                }
                else
                {
                    if (trigger != null) frame.rightTrigger = trigger.ReadValue();
                    if (grip != null) frame.rightGripValue = grip.ReadValue();
                    var rightSelect = (trigger != null && trigger.ReadValue() >= 0.55f) ||
                                      (primaryButton != null && primaryButton.isPressed);
                    if (rightSelect)
                        frame.buttonFlags |= (uint)ButtonFlags.RightSelect;
                    // Grip still flags Grab for topology portals; storage drag uses trigger hold.
                    if (gripButton != null && gripButton.isPressed)
                        frame.buttonFlags |= (uint)ButtonFlags.RightGrab;
                }

                if ((menuButton != null && menuButton.isPressed) ||
                    (secondaryButton != null && secondaryButton.isPressed))
                {
                    frame.buttonFlags |= (uint)ButtonFlags.Menu;
                }
            }
            catch
            {
                // Optional controls.
            }
        }
    }
}
