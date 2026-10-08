using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Phase 0.2+: kernel-owned entity pose applied to a Unity transform.
    /// </summary>
    public sealed class ThreeOSMovable : MonoBehaviour
    {
        public ulong EntityId = 1001;

        public void ApplyOpenXrPose(ThreeOSPose openXrPose)
        {
            var unity = InteropStructs.ToUnityPose(openXrPose);
            transform.SetPositionAndRotation(unity.position, unity.rotation);
        }

        public ThreeOSPose CurrentOpenXrPose() =>
            InteropStructs.ToOpenXrPose(new Pose(transform.position, transform.rotation));
    }
}
