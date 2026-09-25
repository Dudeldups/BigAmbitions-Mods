#nullable enable

using UnityEngine;

/// <summary>
/// Supplies the native warehouse transition with the Escalade's fitted body
/// dimensions. BuildingManager uses the first MeshCollider's mesh bounds for
/// outside placement, independently from the active physical body colliders.
/// This disabled collider is a placement contract only and never participates
/// in vehicle physics.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class CadillacEscaladeWarehouseBoundsController : MonoBehaviour
{
    // The physical Escalade body spans 5.12m. Add 1.30m of clearance at each
    // end so the native exit puts its real body beyond the warehouse entry
    // trigger before the short exit guard restores that trigger.
    private static readonly Vector3 BoundsSize = new Vector3(1.96f, 1.20f, 7.72f);

    private Mesh? placementMesh;
    private MeshCollider? placementCollider;

    internal void Initialize()
    {
        if (placementCollider != null)
            return;

        placementMesh = CreatePlacementMesh();
        placementMesh.name = "CadillacEscalade_WarehousePlacementBounds";
        placementMesh.hideFlags = HideFlags.DontSave;

        // Root components are resolved before the visual/model hierarchy by
        // the native placement lookup, while this disabled collider stays out
        // of all vehicle physics and trigger interactions.
        placementCollider = gameObject.AddComponent<MeshCollider>();
        placementCollider.sharedMesh = placementMesh;
        placementCollider.convex = false;
        placementCollider.enabled = false;
        placementCollider.hideFlags = HideFlags.DontSave;
    }

    private void OnDestroy()
    {
        if (placementMesh != null)
            Destroy(placementMesh);
    }

    private static Mesh CreatePlacementMesh()
    {
        var half = BoundsSize * .5f;
        var mesh = new Mesh();
        mesh.vertices = new[]
        {
            new Vector3(-half.x, -half.y, -half.z),
            new Vector3( half.x, -half.y, -half.z),
            new Vector3( half.x,  half.y, -half.z),
            new Vector3(-half.x,  half.y, -half.z),
            new Vector3(-half.x, -half.y,  half.z),
            new Vector3( half.x, -half.y,  half.z),
            new Vector3( half.x,  half.y,  half.z),
            new Vector3(-half.x,  half.y,  half.z),
        };
        mesh.triangles = new[]
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7,
        };
        mesh.RecalculateBounds();
        return mesh;
    }
}
