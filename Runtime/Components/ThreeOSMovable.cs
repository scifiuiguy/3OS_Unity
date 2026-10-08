using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Kernel-registered movable: applies kinematic / portal transform deltas from the bridge.
    /// </summary>
    public sealed class ThreeOSMovable : MonoBehaviour
    {
        public ulong EntityId = 1001;
        public float HalfExtent = 0.075f;

        private ThreeOSBridge _bridge;

        private void Awake()
        {
            _bridge = FindFirstObjectByType<ThreeOSBridge>();
        }

        private void LateUpdate()
        {
            if (_bridge == null || !_bridge.IsLoaded)
            {
                return;
            }

            var delta = _bridge.LastDelta;
            if (delta.entityId != EntityId)
            {
                return;
            }

            if ((delta.flags & (uint)(TransformFlags.Kinematic | TransformFlags.Portal)) == 0)
            {
                return;
            }

            ApplyOpenXrPose(delta.pose);
        }

        public void ApplyOpenXrPose(ThreeOSPose openXrPose)
        {
            var unity = InteropStructs.ToUnityPose(openXrPose);
            transform.SetPositionAndRotation(unity.position, unity.rotation);
        }

        public ThreeOSPose CurrentOpenXrPose() =>
            InteropStructs.ToOpenXrPose(new Pose(transform.position, transform.rotation));
    }
}
