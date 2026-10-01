#nullable enable

using UnityEngine;

/// <summary>
/// Supplies the native warehouse transition with the fitted Revuelto body's
/// dimensions. BuildingManager reads the first MeshCollider's mesh bounds to
/// choose its exterior position, independently of the active body colliders.
/// This disabled collider is therefore a placement contract only and never
/// participates in vehicle physics.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class LamborghiniRevueltoWarehouseBoundsController : MonoBehaviour
{
    // The physical Revuelto body spans 4.82m after its bumper-contact boxes.
    // Include the same conservative door-trigger clearance used by the
    // other vehicle safeguards: 1.30m at each end before normal
    // driving resumes outside.
    private static readonly Vector3 BoundsSize = new Vector3(1.96f, 1.20f, 7.42f);

    private Mesh? spawnMesh;
    private MeshCollider? spawnCollider;
    private Mesh? placementMesh;
    private MeshCollider? placementCollider;

    internal void Initialize()
    {
        if (placementCollider != null)
            return;

        placementMesh = CreatePlacementMesh(BoundsSize);
        placementMesh.name = "LamborghiniRevuelto_WarehousePlacementBounds";
        placementMesh.hideFlags = HideFlags.DontSave;

        // Place the collider directly on the vehicle root. GetComponentInChildren
        // checks root components before the model's visual/collision children.
        // A spawned instance inherits the prefab collider, but owns a fresh mesh.
        placementCollider = GetComponent<MeshCollider>() ?? gameObject.AddComponent<MeshCollider>();
        placementCollider.enabled = false;
        placementCollider.convex = true;
        placementCollider.sharedMesh = placementMesh;
        placementCollider.hideFlags = HideFlags.DontSave;

        // Native CreateAndSpawnVehicle/DestroyBlockingVehicles reads this exact
        // child path, unlike BuildingManager's first-MeshCollider lookup.
        var body = transform.Find("CarHolder/Colliders/BodyCollider");
        if (body == null)
            throw new System.InvalidOperationException("LamborghiniRevuelto: native body collider path is missing.");
        spawnMesh = CreatePlacementMesh(new Vector3(1.96f, 1.20f, 4.82f));
        spawnMesh.name = "LamborghiniRevuelto_NativeSpawnBounds";
        spawnMesh.hideFlags = HideFlags.DontSave;
        spawnCollider = body.GetComponent<MeshCollider>() ?? body.gameObject.AddComponent<MeshCollider>();
        spawnCollider.enabled = false;
        spawnCollider.convex = true;
        spawnCollider.sharedMesh = spawnMesh;
        spawnCollider.hideFlags = HideFlags.DontSave;
    }

    private void OnDestroy()
    {
        if (spawnCollider != null)
            Destroy(spawnCollider);
        if (spawnMesh != null)
            Destroy(spawnMesh);
        if (placementCollider != null)
            Destroy(placementCollider);
        if (placementMesh != null)
            Destroy(placementMesh);
    }

    private static Mesh CreatePlacementMesh(Vector3 size)
    {
        var half = size * .5f;
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
