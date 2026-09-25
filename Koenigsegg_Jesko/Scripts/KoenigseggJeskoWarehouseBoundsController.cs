#nullable enable

using UnityEngine;

/// <summary>
/// Supplies native warehouse placement with bounds matching the fitted Jesko.
/// The disabled root collider is a placement contract only and never
/// participates in vehicle physics.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class KoenigseggJeskoWarehouseBoundsController : MonoBehaviour
{
    // The fitted body is about 4.61m long. Add 1.30m clearance at each end so
    // native warehouse exit placement clears the physical body and trigger.
    private static readonly Vector3 BoundsSize = new Vector3(2.03f, 1.20f, 7.21f);

    private Mesh? placementMesh;
    private MeshCollider? placementCollider;

    internal void Initialize()
    {
        if (placementCollider != null)
            return;

        placementMesh = CreatePlacementMesh();
        placementMesh.name = "KoenigseggJesko_WarehousePlacementBounds";
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
