using System;
using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Public Act I agent UI bridge (Speak + widget scale). MultiChoiceHost is internal;
    /// Final Demo scripts in Assembly-CSharp talk through this facade.
    /// </summary>
    public static class ThreeOSAgentSession
    {
        public static event Action SpeakSelected;

        /// <summary>Fired on rising edge of Agent Multi-Choice open (voodoo FocusAnchor release).</summary>
        public static event Action AgentOpened;

        /// <summary>Live agent widget root while open; null when destroyed/closed.</summary>
        public static Transform WidgetRoot { get; private set; }

        /// <summary>True while kernel Multi-Choice is open in Agent mode.</summary>
        public static bool IsAgentOpen { get; private set; }

        private static Action<float> _requestShrink;
        private static Action _requestPopIn;
        private static float _lastSpeakNotifyUnscaled = -999f;
        const float SpeakNotifyDebounceSec = 0.45f;

        internal static void Bind(Action<float> requestShrink, Action requestPopIn)
        {
            _requestShrink = requestShrink;
            _requestPopIn = requestPopIn;
        }

        internal static void NotifySpeak()
        {
            var now = Time.unscaledTime;
            if (now - _lastSpeakNotifyUnscaled < SpeakNotifyDebounceSec)
            {
                Debug.Log("[3OS][7.3] SpeakSelected debounced");
                return;
            }

            _lastSpeakNotifyUnscaled = now;
            SpeakSelected?.Invoke();
        }

        internal static void SetWidgetRoot(Transform root, bool agentOpen)
        {
            WidgetRoot = root;
            var nowOpen = agentOpen && root != null;
            var rising = nowOpen && !IsAgentOpen;
            IsAgentOpen = nowOpen;
            if (rising)
                AgentOpened?.Invoke();
        }

        /// <summary>Shrink agent widget toward 0 (no deallocate).</summary>
        public static void RequestShrink(float durationSec = 1f)
        {
            _requestShrink?.Invoke(Mathf.Max(0.05f, durationSec));
        }

        /// <summary>Replay 0→overshoot→1 pop-in (also used by Act I reset).</summary>
        public static void RequestPopIn()
        {
            _requestPopIn?.Invoke();
        }
    }
}
