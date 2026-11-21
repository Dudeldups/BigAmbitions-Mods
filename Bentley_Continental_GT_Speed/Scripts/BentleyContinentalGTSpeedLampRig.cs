#nullable enable
using UnityEngine;

/// <summary>Authored emitter geometry and native-Gley material; no state polling.</summary>
public sealed class BentleyContinentalGTSpeedLampRig : MonoBehaviour
{
    public MeshRenderer emitters = null!;
    public SkinnedMeshRenderer[] playerEmitters = System.Array.Empty<SkinnedMeshRenderer>();
    public Material npcMaterial = null!;
    public Vector3 npcBeamPosition;
}
