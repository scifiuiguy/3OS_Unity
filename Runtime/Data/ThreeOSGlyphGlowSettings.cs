using UnityEngine;

namespace ThreeOS
{
    /// <summary>
    /// Look-dev knobs for <c>ThreeOS/Glyph</c> selection edge glow (object-space rim).
    /// Create via Assets → Create → 3OS → Glyph Glow Settings.
    /// Defaults synced from sample <c>FresnelTest2</c> look-dev.
    /// </summary>
    [CreateAssetMenu(fileName = "GlyphGlowSettings", menuName = "3OS/Glyph Glow Settings", order = 10)]
    public sealed class ThreeOSGlyphGlowSettings : ScriptableObject
    {
        public Color GlowColor = Color.white;

        [Range(0f, 8f)] public float GlowIntensity = 0.5f;

        [Tooltip("Higher = thinner rim / faster falloff into base color.")]
        [Range(0.5f, 64f)] public float GlowPower = 3f;

        [Tooltip("How far glow reaches from the face border toward the center (0–1). Smaller = thinner rim.")]
        [Range(0.005f, 0.5f)] public float GlowWidth = 0.5f;

        [Range(0f, 4f)] public float GlowBoost = 4f;

        [Tooltip("0 = geometric edge only (best for flat prisms). Raise to add view-angle Fresnel.")]
        [Range(0f, 1f)] public float GlowViewMix = 0.5f;
    }
}
