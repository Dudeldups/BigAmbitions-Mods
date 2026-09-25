#nullable enable

using UnityEngine;

/// <summary>
/// Supplies native warehouse placement with bounds matching the fitted SF90Spider.
/// The disabled root collider is a placement contract only and never
/// participates in vehicle physics.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class FerrariSF90SpiderWarehouseBoundsController : MonoBehaviour
{
    // SF90 Spider overall footprint is about 4.704m x 1.973m. Add generous
    // longitudinal clearance so native warehouse exit placement clears the body.
    private static readonly Vector3 BoundsSize = new Vector3(2.03f, 1.18f, 7.30f);

    private Mesh? placementMesh;
    private MeshCollider? placementCollider;

    internal void Initialize()
    {
        if (placementCollider != null)
            return;

        placementMesh = CreatePlacementMesh();
        placementMesh.name = "FerrariSF90Spider_WarehousePlacementBounds";
        placementMesh.hideFlags = HideFlags.DontSave;

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
        var half = BoundsSize * 0.5f;
        var mesh = new Mesh
        {
            vertices = new[]
            {
                new Vector3(-half.x, -half.y, -half.z),
                new Vector3( half.x, -half.y, -half.z),
                new Vector3( half.x,  half.y, -half.z),
                new Vector3(-half.x,  half.y, -half.z),
                new Vector3(-half.x, -half.y,  half.z),
                new Vector3( half.x, -half.y,  half.z),
                new Vector3( half.x,  half.y,  half.z),
                new Vector3(-half.x,  half.y,  half.z),
            },
            triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7,
            },
        };
        mesh.RecalculateBounds();
        return mesh;
    }
}
