using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// World-space Phase 0.1 HUD (IMGUI OnGUI does not render reliably in Quest XR).
    /// </summary>
    public sealed class ThreeOSDebugHud : MonoBehaviour
    {
        private ThreeOSBridge _bridge;
        private ThreeOSInputRouter _inputRouter;
        private ThreeOSTopologyController _topology;
        private ThreeOSKinematicsController _kinematics;
        private TextMesh _label;
        private Transform _follow;

        private void Awake()
        {
            _bridge = GetComponent<ThreeOSBridge>() ?? FindFirstObjectByType<ThreeOSBridge>();
            _inputRouter = GetComponent<ThreeOSInputRouter>() ?? FindFirstObjectByType<ThreeOSInputRouter>();
            _topology = GetComponent<ThreeOSTopologyController>() ??
                        FindFirstObjectByType<ThreeOSTopologyController>();
            _kinematics = GetComponent<ThreeOSKinematicsController>() ??
                          FindFirstObjectByType<ThreeOSKinematicsController>();
            CreateWorldHud();
        }

        private void LateUpdate()
        {
            if (_follow == null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    _follow = cam.transform;
                    if (_label != null)
                    {
                        _label.transform.SetParent(_follow, false);
                        _label.transform.localPosition = new Vector3(-0.25f, 0.15f, 1.2f);
                        _label.transform.localRotation = Quaternion.identity;
                        _label.transform.localScale = Vector3.one;
                    }
                }
            }

            if (_label == null)
            {
                return;
            }

            _label.text = BuildStatusText();
        }

        private void CreateWorldHud()
        {
            var hudGo = new GameObject("3OS_DebugHud_Label");
            var cam = Camera.main;
            if (cam != null)
            {
                _follow = cam.transform;
                hudGo.transform.SetParent(_follow, false);
            }

            hudGo.transform.localPosition = new Vector3(-0.25f, 0.15f, 1.2f);
            hudGo.transform.localRotation = Quaternion.identity;
            hudGo.transform.localScale = Vector3.one;

            _label = hudGo.AddComponent<TextMesh>();
            _label.fontSize = 48;
            _label.characterSize = 0.01f;
            _label.anchor = TextAnchor.UpperLeft;
            _label.alignment = TextAlignment.Left;
            _label.color = Color.white;
            _label.text = "3OS HUD starting…";
        }

        private string BuildStatusText()
        {
            if (_bridge == null)
            {
                return "3OS Phase 0.1\nBridge: missing";
            }

            var ticksOk = _inputRouter != null && _inputRouter.LastTickOk;
            var flags = _inputRouter != null ? _inputRouter.LastFrame.trackingFlags : 0u;
            var topo = _topology != null
                ? $"Dome:{_topology.DomeActive} Lattice:{_topology.LatticeActive} Portals:{_topology.PortalCount}"
                : "Topology: n/a";
            var kin = _kinematics != null
                ? $"Kinetic:{_kinematics.Phase} {_kinematics.Status}"
                : "Kinematics: n/a";
            var text =
                "3OS Phase 0.3\n" +
                $"Plugin loaded: {_bridge.IsLoaded}\n" +
                $"Native version: {ThreeOSBridge.FormatVersion(_bridge.NativeVersion)}\n" +
                $"ABI version: {_bridge.NativeAbiVersion}\n" +
                $"Ticks: {_bridge.TickCount} ok:{ticksOk}\n" +
                $"Track: 0x{flags:X}\n" +
                $"{topo}\n" +
                kin;

            if (!string.IsNullOrEmpty(_bridge.LastError))
            {
                text += $"\nError: {_bridge.LastError}";
                _label.color = Color.red;
            }
            else
            {
                _label.color = Color.white;
            }

            return text;
        }
    }
}
