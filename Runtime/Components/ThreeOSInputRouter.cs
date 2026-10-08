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
        private ThreeOSBridge _bridge;
        private Transform _headTransform;

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
        }

        private void Update()
        {
            if (_bridge == null || !_bridge.IsLoaded)
            {
                LastTickOk = false;
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
                    if (primaryButton != null && primaryButton.isPressed)
                        frame.buttonFlags |= (uint)ButtonFlags.LeftSelect;
                    if (gripButton != null && gripButton.isPressed)
                        frame.buttonFlags |= (uint)ButtonFlags.LeftGrab;
                }
                else
                {
                    if (trigger != null) frame.rightTrigger = trigger.ReadValue();
                    if (grip != null) frame.rightGripValue = grip.ReadValue();
                    if (primaryButton != null && primaryButton.isPressed)
                        frame.buttonFlags |= (uint)ButtonFlags.RightSelect;
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
