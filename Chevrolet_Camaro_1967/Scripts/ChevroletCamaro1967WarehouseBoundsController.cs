#nullable enable

using UnityEngine;

/// <summary>
/// Supplies the native warehouse transition with the fitted Camaro body's
/// dimensions. BuildingManager reads the first MeshCollider's mesh bounds to
/// choose its exterior position, independently of the active body colliders.
/// This disabled collider is therefore a placement contract only and never
/// participates in vehicle physics.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
internal sealed class ChevroletCamaro1967WarehouseBoundsController : MonoBehaviour
{
    // The physical Camaro body spans 4.41m after its bumper-contact boxes.
    // BuildingManager's exit point lands within the door trigger for this
    // model, so use a 1.30m rear clearance beyond the body before normal
    // driving resumes outside.
    private static readonly Vector3 BoundsSize = new Vector3(1.90f, 1.20f, 7.01f);

    private Mesh? placementMesh;
    private MeshCollider? placementCollider;

    internal void Initialize()
    {
        if (placementCollider != null)
            return;

        placementMesh = CreatePlacementMesh();
        placementMesh.name = "ChevroletCamaro1967_WarehousePlacementBounds";
        placementMesh.hideFlags = HideFlags.DontSave;

        // Place the collider directly on the vehicle root. GetComponentInChildren
        // checks root components before the model's visual/collision children.
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
