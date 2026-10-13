using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
#if THREEOS_XR_HANDS
using UnityEngine.XR.Hands;
#endif

namespace ThreeOS
{
    /// <summary>
    /// XR head / controllers / hands → kernel frame + Phase 0.5 cursors.
    /// </summary>
    /// <summary>
    /// Runs before StorageController so the same frame's LastFrame/intents are fresh.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ThreeOSInputRouter : MonoBehaviour
    {
        public const float CursorBaseDiameterM = 0.015f; // 50% of prior 0.03m

        private ThreeOSBridge _bridge;
        private ThreeOSStorageController _storage;
        private Transform _headTransform;
        private static readonly Color CursorWhite = Color.white;
        private static readonly Color CursorNearBlue = new Color(0.4f, 0.85f, 1f, 1f);

        private Transform _leftCursor;
        private Transform _rightCursor;
        private Material _leftCursorMat;
        private Material _rightCursorMat;
        private ulong _frameIndex;

#if THREEOS_XR_HANDS
        private XRHandSubsystem _hands;
#endif

        public InteropInputFrame LastFrame { get; private set; }
        public InteropPointCursor LastLeftCursor { get; private set; }
        public InteropPointCursor LastRightCursor { get; private set; }
        public InteropInteractionIntents LastIntents { get; private set; }
        public bool LastTickOk { get; private set; }

        /// <summary>Best aim for storage select/drag (prefers active point cursor).</summary>
        public ThreeOSPose PreferredAimPose => AimPoseForHand(-1);

        /// <summary>
        /// Aim pose for a specific hand cursor. <paramref name="hand"/> 0=left, 1=right,
        /// negative = prefer whichever cursor is active (right if both).
        /// </summary>
        public ThreeOSPose AimPoseForHand(int hand)
        {
            if (hand == 0 && LastLeftCursor.active != 0)
            {
                return PoseFromCursor(LastLeftCursor);
            }

            if (hand == 1 && LastRightCursor.active != 0)
            {
                return PoseFromCursor(LastRightCursor);
            }

            if (hand < 0)
            {
                if (LastRightCursor.active != 0)
                {
                    return PoseFromCursor(LastRightCursor);
                }

                if (LastLeftCursor.active != 0)
                {
                    return PoseFromCursor(LastLeftCursor);
                }
            }

            return AimPose(LastFrame);
        }

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
            _storage = GetComponent<ThreeOSStorageController>() ??
                       FindFirstObjectByType<ThreeOSStorageController>();
            if (Camera.main != null)
            {
                _headTransform = Camera.main.transform;
            }

            _leftCursor = CreateSphere("3OS_LeftCursor", CursorBaseDiameterM, CursorWhite, out _leftCursorMat).transform;
            _rightCursor = CreateSphere("3OS_RightCursor", CursorBaseDiameterM, CursorWhite, out _rightCursorMat).transform;

            if (GetComponent<ThreeOSHandVisuals>() == null)
            {
                gameObject.AddComponent<ThreeOSHandVisuals>();
            }
        }

        private void OnDestroy()
        {
            DestroyGo(_leftCursor);
            DestroyGo(_rightCursor);
        }

        private void Update()
        {
            if (_bridge == null || !_bridge.IsLoaded)
            {
                LastTickOk = false;
                SetActive(_leftCursor, false);
                SetActive(_rightCursor, false);
                return;
            }

            if (_storage == null)
            {
                _storage = FindFirstObjectByType<ThreeOSStorageController>();
            }

            // Refresh Camera.main — at Awake it can still be a floor-level placeholder.
            if (_headTransform == null || !_headTransform.gameObject.activeInHierarchy)
            {
                if (Camera.main != null)
                {
                    _headTransform = Camera.main.transform;
                }
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

            var leftHand = default(InteropHandSample);
            var rightHand = default(InteropHandSample);
            var handsFromTracking = false;
#if THREEOS_XR_HANDS
            handsFromTracking = TryFillHandsFromXr(ref leftHand, ref rightHand);
#endif
            FillController(XRController.leftHand, isLeft: true, ref frame);
            FillController(XRController.rightHand, isLeft: false, ref frame);

            if (handsFromTracking)
            {
                // Hands win for aim/trigger when tracked (controllers often still report).
                ApplyHandToFrame(ref frame, leftHand, isLeft: true);
                ApplyHandToFrame(ref frame, rightHand, isLeft: false);
            }

            // While storage telekinesis is active, mute the other hand so kernel
            // stick/intents cannot latch onto the right cursor during a left drag.
            // PossessDriveHand is from the previous frame's Storage Update (one-frame latch);
            // PossessWithHand already set the kernel latch on the possess call.
            var driveHand = _storage != null ? _storage.PossessDriveHand : -1;
            if (driveHand == 0)
            {
                MuteHandSample(ref rightHand);
                frame.rightTrigger = 0f;
                frame.buttonFlags &= ~(uint)(ButtonFlags.RightSelect | ButtonFlags.RightGrab |
                                             ButtonFlags.RightTernary);
            }
            else if (driveHand == 1)
            {
                MuteHandSample(ref leftHand);
                frame.leftTrigger = 0f;
                frame.buttonFlags &= ~(uint)(ButtonFlags.LeftSelect | ButtonFlags.LeftGrab |
                                            ButtonFlags.LeftTernary);
            }

            if (handsFromTracking)
            {
                _bridge.SetHands(ref leftHand, ref rightHand);
            }

            // Drive hand is latched once via PossessWithHand; do not SetDriveHand every
            // frame (that used to reanchor start_hand and kill stick velocity).

            LastTickOk = _bridge.Tick(ref frame);
            LastFrame = frame;

            if (_bridge.NativeAbiVersion >= 5)
            {
                if (_bridge.TryGetCursor(0, out var lc))
                {
                    LastLeftCursor = lc;
                }

                if (_bridge.TryGetCursor(1, out var rc))
                {
                    LastRightCursor = rc;
                }

                if (_bridge.TryGetIntents(out var intents))
                {
                    LastIntents = intents;
                }
            }

            UpdateCursorVisual(_leftCursor, _leftCursorMat, LastLeftCursor);
            UpdateCursorVisual(_rightCursor, _rightCursorMat, LastRightCursor);
        }

        private static void MuteHandSample(ref InteropHandSample hand)
        {
            hand.primaryPressed = 0;
            hand.secondaryPressed = 0;
            hand.ternaryPressed = 0;
            hand.primaryValue = 0f;
            hand.secondaryValue = 0f;
            hand.ternaryValue = 0f;
        }

        private static void ApplyHandToFrame(ref InteropInputFrame frame, InteropHandSample hand, bool isLeft)
        {
            if (hand.tracked == 0)
            {
                return;
            }

            var aimPos = hand.indexTip;
            var pose = new ThreeOSPose
            {
                position = aimPos,
                orientation = frame.head.orientation
            };
            if (isLeft)
            {
                frame.leftAim = pose;
                frame.leftGrip = pose;
                frame.leftTrigger = hand.primaryValue;
                frame.leftGripValue = hand.secondaryValue;
                frame.trackingFlags |= (uint)(TrackingFlags.LeftAim | TrackingFlags.LeftGrip);
                if (hand.primaryPressed != 0)
                {
                    frame.buttonFlags |= (uint)ButtonFlags.LeftSelect;
                }

                if (hand.secondaryPressed != 0)
                {
                    frame.buttonFlags |= (uint)ButtonFlags.LeftGrab;
                }

                if (hand.ternaryPressed != 0)
                {
                    frame.buttonFlags |= (uint)ButtonFlags.LeftTernary;
                }
            }
            else
            {
                frame.rightAim = pose;
                frame.rightGrip = pose;
                frame.rightTrigger = hand.primaryValue;
                frame.rightGripValue = hand.secondaryValue;
                frame.trackingFlags |= (uint)(TrackingFlags.RightAim | TrackingFlags.RightGrip);
                if (hand.primaryPressed != 0)
                {
                    frame.buttonFlags |= (uint)ButtonFlags.RightSelect;
                }

                if (hand.secondaryPressed != 0)
                {
                    frame.buttonFlags |= (uint)ButtonFlags.RightGrab;
                }

                if (hand.ternaryPressed != 0)
                {
                    frame.buttonFlags |= (uint)ButtonFlags.RightTernary;
                }
            }
        }

#if THREEOS_XR_HANDS
        private bool TryFillHandsFromXr(ref InteropHandSample left, ref InteropHandSample right)
        {
            if (_hands == null || !_hands.running)
            {
                var list = new List<XRHandSubsystem>();
                SubsystemManager.GetSubsystems(list);
                _hands = null;
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && list[i].running)
                    {
                        _hands = list[i];
                        break;
                    }
                }

                if (_hands == null && list.Count > 0)
                {
                    _hands = list[0];
                }
            }

            if (_hands == null)
            {
                return false;
            }

            var any = false;
            if (FillOneHand(_hands.leftHand, ref left))
            {
                any = true;
            }

            if (FillOneHand(_hands.rightHand, ref right))
            {
                any = true;
            }

            return any;
        }

        private static bool FillOneHand(XRHand hand, ref InteropHandSample sample)
        {
            if (!hand.isTracked)
            {
                return false;
            }

            sample.tracked = 1;
            uint tips = 0;
            if (TryJointOpenXr(hand, XRHandJointID.ThumbTip, out sample.thumbTip))
            {
                tips |= (uint)HandTipFlags.Thumb;
            }

            if (TryJointOpenXr(hand, XRHandJointID.IndexTip, out sample.indexTip))
            {
                tips |= (uint)HandTipFlags.Index;
            }

            if (TryJointOpenXr(hand, XRHandJointID.MiddleTip, out sample.middleTip))
            {
                tips |= (uint)HandTipFlags.Middle;
            }

            if (TryJointOpenXr(hand, XRHandJointID.RingTip, out sample.ringTip))
            {
                tips |= (uint)HandTipFlags.Ring;
            }

            if (TryJointOpenXr(hand, XRHandJointID.IndexProximal, out sample.indexProximal))
            {
                tips |= (uint)HandTipFlags.IndexProximal;
            }

            sample.tipFlags = tips;

            if (hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palmPose))
            {
                // Meta XR Hands palm joint +Y points out the *back* of the hand (dorsal).
                // Negate so kernel gets the palmar normal: palm to floor → world −Y → Down.
                var n = -palmPose.up;
                sample.palmNormal = InteropStructs.UnityToOpenXrPosition(n);
                sample.palmModeValid = 0;
            }

            // Pinch distances → approach 0..1 and pressed at thresh (kernel also computes).
            // Pressed must match Storage trigger threshold (0.55). Requiring 0.95 meant
            // DragActive often lagged seconds behind a hold the host already counted.
            const float thresh = 0.0635f;
            const float pressedAt = 0.55f;
            sample.primaryValue = Pinch01(sample.thumbTip, sample.indexTip, tips,
                (uint)(HandTipFlags.Thumb | HandTipFlags.Index), thresh);
            sample.secondaryValue = Pinch01(sample.thumbTip, sample.middleTip, tips,
                (uint)(HandTipFlags.Thumb | HandTipFlags.Middle), thresh);
            sample.ternaryValue = Pinch01(sample.thumbTip, sample.ringTip, tips,
                (uint)(HandTipFlags.Thumb | HandTipFlags.Ring), thresh);
            sample.primaryPressed = sample.primaryValue >= pressedAt ? 1u : 0u;
            sample.secondaryPressed = sample.secondaryValue >= pressedAt ? 1u : 0u;
            sample.ternaryPressed = sample.ternaryValue >= pressedAt ? 1u : 0u;
            return true;
        }

        private static bool TryJointOpenXr(XRHand hand, XRHandJointID id, out ThreeOSVec3 openXr)
        {
            openXr = default;
            if (!hand.GetJoint(id).TryGetPose(out var pose))
            {
                return false;
            }

            openXr = InteropStructs.UnityToOpenXrPosition(pose.position);
            return true;
        }

        private static float Pinch01(ThreeOSVec3 a, ThreeOSVec3 b, uint tips, uint need, float thresh)
        {
            if ((tips & need) != need)
            {
                return 0f;
            }

            var d = Vector3.Distance(a.ToVector3(), b.ToVector3());
            if (d >= thresh)
            {
                return 0f;
            }

            return Mathf.Clamp01(1f - d / thresh);
        }
#endif

        private static ThreeOSPose PoseFromCursor(InteropPointCursor c) =>
            new ThreeOSPose
            {
                position = c.position,
                orientation = new ThreeOSQuat { x = 0, y = 0, z = 0, w = 1 }
            };

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

        private void UpdateCursorVisual(Transform tip, Material mat, InteropPointCursor cursor)
        {
            if (tip == null)
            {
                return;
            }

            if (cursor.active == 0)
            {
                SetActive(tip, false);
                return;
            }

            var openXr = cursor.position.ToVector3();
            var unity = InteropStructs.OpenXrToUnityPosition(openXr);
            tip.position = unity;
            var s = CursorBaseDiameterM * Mathf.Max(0.1f, cursor.scale);
            tip.localScale = Vector3.one * s;
            if (mat != null && _bridge != null && _bridge.IsLoaded)
            {
                var inside = _bridge.PointInAnyNearBb(openXr);
                var c = inside ? CursorNearBlue : CursorWhite;
                mat.color = c;
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            }

            SetActive(tip, true);
        }

        private static void SetActive(Transform tip, bool active)
        {
            if (tip != null && tip.gameObject.activeSelf != active)
            {
                tip.gameObject.SetActive(active);
            }
        }

        private static void DestroyGo(Transform t)
        {
            if (t != null)
            {
                Destroy(t.gameObject);
            }
        }

        private static GameObject CreateSphere(string name, float diameter, Color color, out Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.localScale = Vector3.one * diameter;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default"));
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", color);
            }

            mat.color = color;
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
                    var leftSelect = (trigger != null && trigger.ReadValue() >= 0.55f) ||
                                     (primaryButton != null && primaryButton.isPressed);
                    if (leftSelect)
                        frame.buttonFlags |= (uint)ButtonFlags.LeftSelect;
                    if (gripButton != null && gripButton.isPressed)
                        frame.buttonFlags |= (uint)ButtonFlags.LeftGrab;
                    if (primaryButton != null && primaryButton.isPressed)
                        frame.buttonFlags |= (uint)ButtonFlags.LeftTernary;
                }
                else
                {
                    if (trigger != null) frame.rightTrigger = trigger.ReadValue();
                    if (grip != null) frame.rightGripValue = grip.ReadValue();
                    var rightSelect = (trigger != null && trigger.ReadValue() >= 0.55f) ||
                                      (primaryButton != null && primaryButton.isPressed);
                    if (rightSelect)
                        frame.buttonFlags |= (uint)ButtonFlags.RightSelect;
                    if (gripButton != null && gripButton.isPressed)
                        frame.buttonFlags |= (uint)ButtonFlags.RightGrab;
                    if (primaryButton != null && primaryButton.isPressed)
                        frame.buttonFlags |= (uint)ButtonFlags.RightTernary;
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
