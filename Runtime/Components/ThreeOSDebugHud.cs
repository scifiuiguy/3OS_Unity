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
        private ThreeOSStorageController _storage;
        private ThreeOSVoodooController _voodoo;
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
            _storage = GetComponent<ThreeOSStorageController>() ??
                       FindFirstObjectByType<ThreeOSStorageController>();
            _voodoo = GetComponent<ThreeOSVoodooController>() ??
                      FindFirstObjectByType<ThreeOSVoodooController>();
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
                ? $"Dome:{_topology.DomeActive} Lattice:{_topology.LatticeActive} {_topology.Status}"
                : "Topology: n/a";
            var kin = _kinematics != null
                ? $"Kinetic:{_kinematics.Phase} {_kinematics.Status}"
                : "Kinematics: n/a";
            var storage = _storage != null
                ? $"Storage:{(_storage.DemoActive ? "demo" : "off")} {_storage.Status}"
                : "Storage: n/a";
            var sel = _storage != null ? $"Sel:{_storage.SelectionLabel}" : "Sel:n/a";
            var palmL = _inputRouter != null ? PalmName(_inputRouter.LastLeftCursor.palmMode) : "?";
            var palmR = _inputRouter != null ? PalmName(_inputRouter.LastRightCursor.palmMode) : "?";
            var intents = _inputRouter != null ? _inputRouter.LastIntents : default;
            var intentBits = IntentSummary(intents.flags);
            var voodooLine = "Voodoo: n/a";
            if (_bridge.NativeAbiVersion >= 6)
            {
                string HandBit(uint h)
                {
                    if (!_bridge.TryGetVoodooSession(h, out var vs) || vs.open == 0)
                    {
                        return "off";
                    }

                    return $"{(VoodooMode)vs.mode} f:{vs.farBb.open} n:{vs.nearBb.open}";
                }

                voodooLine = $"Voodoo L:{HandBit(0)} R:{HandBit(1)}";
            }

            var text =
                "3OS Phase 0.6\n" +
                $"Plugin loaded: {_bridge.IsLoaded}\n" +
                $"Native version: {ThreeOSBridge.FormatVersion(_bridge.NativeVersion)}\n" +
                $"ABI version: {_bridge.NativeAbiVersion}\n" +
                $"Ticks: {_bridge.TickCount} ok:{ticksOk}\n" +
                $"Track: 0x{flags:X}\n" +
                $"Palm L:{palmL} R:{palmR}\n" +
                $"Intent:{intentBits}\n" +
                $"{voodooLine}\n" +
                $"{topo}\n" +
                $"{storage}\n" +
                $"{sel}\n" +
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

        private static string PalmName(uint mode) => mode switch
        {
            (uint)PalmMode.Up => "Up",
            (uint)PalmMode.Sideways => "Side",
            (uint)PalmMode.Down => "Down",
            _ => mode.ToString()
        };

        private static string IntentSummary(uint flags)
        {
            if (flags == 0)
            {
                return "-";
            }

            var parts = new System.Collections.Generic.List<string>(4);
            if ((flags & (uint)InteractionIntentFlags.SelectPulse) != 0) parts.Add("Sel");
            if ((flags & (uint)InteractionIntentFlags.DragActive) != 0) parts.Add("Drag");
            if ((flags & (uint)InteractionIntentFlags.VoodooSnap) != 0) parts.Add("Snap");
            if ((flags & (uint)InteractionIntentFlags.VoodooFree) != 0) parts.Add("Free");
            if ((flags & (uint)InteractionIntentFlags.MindPalaceMenu) != 0) parts.Add("MindPalace");
            if ((flags & (uint)InteractionIntentFlags.ModeToggleArmed) != 0) parts.Add("ModeArm");
            if ((flags & (uint)InteractionIntentFlags.ModeTogglePulse) != 0) parts.Add("ModePulse");
            if ((flags & (uint)InteractionIntentFlags.PrimaryHeld) != 0) parts.Add("P1");
            if ((flags & (uint)InteractionIntentFlags.SecondaryHeld) != 0) parts.Add("P2");
            return parts.Count > 0 ? string.Join("+", parts) : $"0x{flags:X}";
        }
    }
}
