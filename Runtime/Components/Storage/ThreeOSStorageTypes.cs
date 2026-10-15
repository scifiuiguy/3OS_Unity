using System.Collections.Generic;
using UnityEngine;

namespace ThreeOS
{
    internal sealed class ThreeOSGlyphView
    {
        public ulong EntityId;
        public GameObject Root;
        public ThreeOSMovable Movable;
        public MeshRenderer Renderer;
        public BoxCollider Collider;
        public TextMesh NameLabel;
        public TextMesh SecondaryLabel;
        public string GlyphId;
        public Material Material;
        public bool OwnsMaterial;
        public bool CutOpacityDuck;
    }

    internal sealed class ThreeOSExpandedBoxUi
    {
        public ulong BoxId;
        public readonly List<ulong> ChildIds = new List<ulong>();
        public GameObject XRoot;
        public Transform XVisual;
        public bool Hovered;
    }

    internal sealed class ThreeOSPortalDismissUi
    {
        public ulong PortalId;
        public GameObject XRoot;
        public Transform XVisual;
        public bool Hovered;
        public GameObject ScissorsRoot;
        public Transform ScissorsVisual;
        public bool ScissorsHovered;
    }
}
