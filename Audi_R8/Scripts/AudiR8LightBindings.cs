#nullable enable
using System;
using UnityEngine;

// Authored meshes use the production source renderer's local coordinates.
[AddComponentMenu("")]
public sealed class AudiR8LightBindings : MonoBehaviour
{
    [Serializable]
    public sealed class Lamp
    {
        public string group = "";
        public MeshRenderer source = null!;
        public Mesh mesh = null!;
    }

    [SerializeField] private Lamp[] lamps = Array.Empty<Lamp>();
    public Lamp[] Lamps => lamps;
}
