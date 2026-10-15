#nullable enable
using UnityEngine;

// Serialized submesh bindings survive native material cloning and damage setup.
[AddComponentMenu("")]
public sealed class AudiR8AppearanceBindings : MonoBehaviour
{
    [SerializeField] private Renderer bodyRenderer = null!;
    [SerializeField] private int paintMaterialIndex = 0;
    [SerializeField] private int stripeMaterialIndex = 1;

    public Renderer BodyRenderer => bodyRenderer;
    public int PaintMaterialIndex => paintMaterialIndex;
    public int StripeMaterialIndex => stripeMaterialIndex;
}
