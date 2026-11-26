#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using NWH.VehiclePhysics2.Damage;
using UnityEngine;

/// <summary>
/// Collision-driven, per-instance visual damage for the fragmented Aventador GLB.
/// Physics colliders remain independent and all visual meshes are cloned before edits.
/// </summary>
[AddComponentMenu("")]
internal sealed class LamborghiniAventadorVisualDamageController : MonoBehaviour
{
    internal static bool DamageDebugEnabled { get; set; } = false;

    private const float MinimumImpactSpeed = 6.0f;
    private const float CollisionCooldown = 0.42f;
    private const float MaximumDentDepth = 0.11f;
    private const float MaximumCumulativeDisplacement = 0.18f;
    private const float AttachedDetailInfluenceMargin = 0.18f;
    private const int MaximumDiagnosticMessages = 8;

    private enum MixedGeometry { None, FrontLuggageBay, LampCovers }

    private sealed class MeshState
    {
        internal MeshFilter Filter = null!;
        internal Mesh OriginalMesh = null!;
        internal Mesh RuntimeMesh = null!;
        internal Vector3[] OriginalVertices = Array.Empty<Vector3>();
        internal Vector3[] VehicleOriginalVertices = Array.Empty<Vector3>();
        internal Vector3[] Vertices = Array.Empty<Vector3>();
        internal Vector3[] OriginalNormals = Array.Empty<Vector3>();
        internal Vector4[] OriginalTangents = Array.Empty<Vector4>();
        internal Bounds OriginalBounds;
        internal Bounds SafeBounds;
        internal Bounds VehicleBounds;
        internal Matrix4x4 MeshToVehicle;
        internal Matrix4x4 VehicleToMesh;
        internal int[]? DeformableIndices;
        internal bool AttachedDetail;
        internal bool ModifiedDuringImpact;
    }

    private readonly List<MeshState> meshes = new List<MeshState>();
    private readonly List<Mesh> ownedMeshes = new List<Mesh>();
    private readonly List<MeshState> modifiedMeshes = new List<MeshState>();
    private readonly HashSet<string> skipExamples = new HashSet<string>(StringComparer.Ordinal);
    private VehicleController? vehicle;
    private ModContext? context;
    private DamageHandler? nativeDamageHandler;
    private float previousNativeDamage;
    private float previousSavedDamage;
    private float nextImpactTime;
    private int diagnosticMessages;
    private int excludedMeshCount;
    private bool initialized;
    private bool warnedMissingBody;
    private bool warnedNoMeshes;
    private bool warnedNoMixedVertices;
    private bool warnedCollisionFailure;

    /// <summary>Attach once to the root of each spawned Aventador player vehicle.</summary>
    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (initialized && vehicle == controller)
            return;

        ReleaseOwnedMeshes();
        vehicle = controller;
        context = modContext;
        meshes.Clear();
        skipExamples.Clear();
        excludedMeshCount = 0;
        var body = controller.transform.Find("AventadorBody");
        if (body == null)
        {
            WarnOnce(ref warnedMissingBody,
                $"LamborghiniAventador damage vehicle={controller.GetInstanceID()}: AventadorBody is missing; visual deformation is disabled for this instance.");
            initialized = true;
            return;
        }

        nativeDamageHandler = controller.GetComponentInChildren<DamageHandler>(true);
        if (nativeDamageHandler == null)
            context?.Logger.Warn($"LamborghiniAventador damage vehicle={controller.GetInstanceID()}: NWH DamageHandler is missing; collision deformation can run, but native repair state will not be observed.");
        else
        {
            previousNativeDamage = nativeDamageHandler.Damage;
            // Keep NWH health/repair behavior, while preventing its legacy mesh
            // path from editing imported shared geometry alongside this owner.
            nativeDamageHandler.meshDeform = false;
        }
        previousSavedDamage = controller.vehicleInstance?.damage ?? 0f;

        // Lighting creates emissive mesh overlays after model setup. Capture
        // those model children and its one Gley-combined root overlay as well.
        foreach (var filter in controller.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null || !filter.TryGetComponent<Renderer>(out _))
                continue;
            if (!filter.transform.IsChildOf(body) &&
                !string.Equals(filter.name, "Aventador_GleyLampOverlay", StringComparison.Ordinal))
                continue;
            if (IsExcluded(filter))
            {
                RecordSkipped(filter.name);
                continue;
            }

            var clone = Instantiate(filter.sharedMesh);
            clone.name = filter.sharedMesh.name + "_AventadorInstanceDamage";
            clone.MarkDynamic();
            var verts = clone.vertices;
            if (verts.Length == 0)
            {
                Destroy(clone);
                continue;
            }

            var mixedGeometry = GetMixedGeometry(filter);
            var deformableIndices = GetDeformableIndices(verts, mixedGeometry);
            if (deformableIndices != null && deformableIndices.Length == 0)
            {
                Destroy(clone);
                WarnOnce(ref warnedNoMixedVertices,
                    $"LamborghiniAventador damage: authored mixed mesh '{filter.name}' contains no exterior vertices.");
                continue;
            }

            var toVehicle = controller.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var bounds = BoundsOf(verts);
            var vehicleOriginalVertices = new Vector3[verts.Length];
            for (var index = 0; index < verts.Length; index++)
                vehicleOriginalVertices[index] = toVehicle.MultiplyPoint3x4(verts[index]);
            var state = new MeshState
            {
                Filter = filter,
                OriginalMesh = filter.sharedMesh,
                RuntimeMesh = clone,
                OriginalVertices = (Vector3[])verts.Clone(),
                VehicleOriginalVertices = vehicleOriginalVertices,
                Vertices = verts,
                OriginalNormals = clone.normals,
                OriginalTangents = clone.tangents,
                OriginalBounds = clone.bounds,
                SafeBounds = ExpandBounds(bounds, toVehicle.inverse, MaximumCumulativeDisplacement),
                VehicleBounds = deformableIndices == null ? TransformBounds(toVehicle, bounds) :
                    BoundsOf(vehicleOriginalVertices, deformableIndices),
                MeshToVehicle = toVehicle,
                VehicleToMesh = toVehicle.inverse,
                DeformableIndices = deformableIndices,
                AttachedDetail = deformableIndices == null && IsAttachedExteriorDetail(filter)
            };
            clone.bounds = state.SafeBounds;
            filter.sharedMesh = clone;
            meshes.Add(state);
            ownedMeshes.Add(clone);
        }

        initialized = true;
        if (meshes.Count == 0)
            WarnOnce(ref warnedNoMeshes,
                $"LamborghiniAventador damage vehicle={controller.GetInstanceID()}: no eligible exterior meshes were found under AventadorBody; visual deformation is disabled for this instance.");
        else
            DamageInfo($"LamborghiniAventador damage initialized vehicle={controller.GetInstanceID()} deformableMeshes={meshes.Count} excludedMeshes={excludedMeshCount} exclusions=[{string.Join(",", skipExamples)}] nativeDamage={(nativeDamageHandler != null)}.");
    }

    private void OnEnable()
    {
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
    }

    private void OnDisable() => GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;

    private void HandleVehicleVariablesChanged()
    {
        if (!initialized || vehicle == null)
            return;
        var currentNative = nativeDamageHandler != null ? nativeDamageHandler.Damage : 0f;
        var currentSaved = vehicle.vehicleInstance?.damage ?? 0f;
        if ((previousNativeDamage > 0.001f && currentNative <= 0.001f) ||
            (previousSavedDamage > 0.001f && currentSaved <= 0.001f))
            RestoreMeshes("repair-state-reset");
        previousNativeDamage = currentNative;
        previousSavedDamage = currentSaved;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || meshes.Count == 0 || collision == null ||
            Time.unscaledTime < nextImpactTime || collision.contactCount == 0 ||
            collision.relativeVelocity.magnitude < MinimumImpactSpeed ||
            LamborghiniAventadorWarehouseEntryController.IsEntranceBarrier(collision.collider) ||
            !DamageHandler.IsCollisionValid(collision))
            return;

        try
        {
            nextImpactTime = Time.unscaledTime + CollisionCooldown;
            var speed = collision.relativeVelocity.magnitude;
            var count = collision.contactCount;
            var root = transform.worldToLocalMatrix;
            var center = GetComponent<Rigidbody>()?.worldCenterOfMass ?? transform.position;
            var changedMeshes = 0;
            var changedVertices = 0;
            var regionSummary = new HashSet<string>(StringComparer.Ordinal);
            modifiedMeshes.Clear();

            for (var contactIndex = 0; contactIndex < count; contactIndex++)
            {
                var contact = collision.GetContact(contactIndex);
                var point = root.MultiplyPoint3x4(contact.point);
                var toCenter = root.MultiplyVector(center - contact.point).normalized;
                var region = Classify(point);
                var inward = region == ImpactRegion.Front ? Vector3.back :
                    region == ImpactRegion.Rear ? Vector3.forward : toCenter;
                if (inward.sqrMagnitude < 0.25f)
                    inward = -root.MultiplyVector(contact.normal).normalized;
                if (inward.sqrMagnitude < 0.25f)
                    continue;

                regionSummary.Add(region.ToString());
                var depth = ComputeDepth(speed, region);
                var radii = Radii(region);

                changedMeshes += ApplyImpact(point, inward, depth, radii, out var contactVertices);
                changedVertices += contactVertices;
            }

            DamageInfo($"LamborghiniAventador damage impact vehicle={vehicle?.GetInstanceID()} other='{collision.collider?.name ?? "unknown"}' speed={speed:0.0}m/s regions={string.Join("+", regionSummary)} meshes={changedMeshes} vertices={changedVertices} nativeDamage={(nativeDamageHandler?.Damage ?? 0f):0.000}.");
        }
        catch (Exception exception)
        {
            if (!warnedCollisionFailure)
            {
                warnedCollisionFailure = true;
                context?.Logger.Warn($"LamborghiniAventador damage vehicle={vehicle?.GetInstanceID()}: collision deformation failed with {exception.GetType().Name}: {exception.Message}");
            }
        }
        finally
        {
            // A collision can have several contact points on the same fragmented
            // body. Upload each changed mesh once after applying every contact.
            FlushModifiedMeshes();
        }
    }

    private void FlushModifiedMeshes()
    {
        foreach (var state in modifiedMeshes)
        {
            try
            {
                if (state.Filter != null && state.RuntimeMesh != null)
                {
                    state.Filter.sharedMesh = state.RuntimeMesh;
                    state.RuntimeMesh.vertices = state.Vertices;
                    state.RuntimeMesh.bounds = state.SafeBounds;
                }
            }
            catch (Exception exception)
            {
                if (!warnedCollisionFailure)
                {
                    warnedCollisionFailure = true;
                    context?.Logger.Warn(
                        $"LamborghiniAventador damage vehicle={vehicle?.GetInstanceID()}: " +
                        $"mesh upload failed with {exception.GetType().Name}: {exception.Message}");
                }
            }
            finally
            {
                state.ModifiedDuringImpact = false;
            }
        }
        modifiedMeshes.Clear();
    }

    private enum ImpactRegion { Front, Rear, Side, FrontCorner, RearCorner }

    /// <summary>Apply the runtime deformation path for one collision contact.</summary>
    internal int ApplyImpact(Vector3 point, Vector3 inward, float depth, Vector3 radii,
        out int changedVertices)
    {
        changedVertices = 0;
        if (inward.sqrMagnitude < 0.25f)
            return 0;
        inward.Normalize();
        var inverseRadii = new Vector3(1f / (radii.x * radii.x),
            1f / (radii.y * radii.y), 1f / (radii.z * radii.z));
        var changedMeshes = 0;

        foreach (var state in meshes)
        {
            if (state.Filter == null || state.RuntimeMesh == null)
                continue;
            var boundsVehicle = state.VehicleBounds;
            boundsVehicle.Expand(MaximumCumulativeDisplacement * 2f);
            var maxRadius = Mathf.Max(radii.x, Mathf.Max(radii.y, radii.z)) +
                (state.AttachedDetail ? AttachedDetailInfluenceMargin : 0f);
            if (boundsVehicle.SqrDistance(point) > maxRadius * maxRadius)
                continue;

            var changed = false;
            if (state.AttachedDetail)
            {
                // A lamp lens, badge, grille insert, or fastener is often a
                // separate mesh slightly proud of its panel. Sample its center
                // with a small margin, then translate it as one piece so it
                // follows a nearby panel dent without folding its geometry.
                var expandedRadii = radii + Vector3.one * AttachedDetailInfluenceMargin;
                var delta = state.VehicleBounds.center - point;
                var distanceSquared = delta.x * delta.x / (expandedRadii.x * expandedRadii.x) +
                    delta.y * delta.y / (expandedRadii.y * expandedRadii.y) +
                    delta.z * delta.z / (expandedRadii.z * expandedRadii.z);
                if (distanceSquared >= 1f)
                    continue;
                var falloff = Mathf.Pow(1f - Mathf.Sqrt(distanceSquared), 1.45f);
                var displacement = inward * (depth * falloff);
                for (var vertexIndex = 0; vertexIndex < state.Vertices.Length; vertexIndex++)
                {
                    var original = state.VehicleOriginalVertices[vertexIndex];
                    var current = state.MeshToVehicle.MultiplyPoint3x4(state.Vertices[vertexIndex]);
                    var deformed = original + Vector3.ClampMagnitude(current + displacement - original,
                        MaximumCumulativeDisplacement);
                    state.Vertices[vertexIndex] = state.VehicleToMesh.MultiplyPoint3x4(deformed);
                }
                changedVertices += state.Vertices.Length;
                changed = state.Vertices.Length > 0;
            }
            else
            {
                var indices = state.DeformableIndices;
                var count = indices?.Length ?? state.Vertices.Length;
                for (var index = 0; index < count; index++)
                {
                    var vertexIndex = indices == null ? index : indices[index];
                    var original = state.VehicleOriginalVertices[vertexIndex];
                    var delta = original - point;
                    var distanceSquared = delta.x * delta.x * inverseRadii.x +
                        delta.y * delta.y * inverseRadii.y + delta.z * delta.z * inverseRadii.z;
                    if (distanceSquared >= 1f)
                        continue;

                    var falloff = Mathf.Pow(1f - Mathf.Sqrt(distanceSquared), 1.45f);
                    var current = state.MeshToVehicle.MultiplyPoint3x4(state.Vertices[vertexIndex]);
                    var candidate = current + inward * (depth * falloff);
                    var deformedVehiclePoint = original + Vector3.ClampMagnitude(candidate - original,
                        MaximumCumulativeDisplacement);
                    state.Vertices[vertexIndex] = state.VehicleToMesh.MultiplyPoint3x4(deformedVehiclePoint);
                    changed = true;
                    changedVertices++;
                }
            }

            if (!changed)
                continue;
            if (!state.ModifiedDuringImpact)
            {
                state.ModifiedDuringImpact = true;
                modifiedMeshes.Add(state);
                changedMeshes++;
            }
        }
        return changedMeshes;
    }

    private static ImpactRegion Classify(Vector3 point)
    {
        var front = point.z > 1.15f;
        var rear = point.z < -1.15f;
        var side = Mathf.Abs(point.x) > 0.62f;
        if (front && side) return ImpactRegion.FrontCorner;
        if (rear && side) return ImpactRegion.RearCorner;
        if (front) return ImpactRegion.Front;
        if (rear) return ImpactRegion.Rear;
        return ImpactRegion.Side;
    }

    private static Vector3 Radii(ImpactRegion region)
    {
        switch (region)
        {
            case ImpactRegion.Front: return new Vector3(0.98f, 0.76f, 0.84f);
            case ImpactRegion.Rear: return new Vector3(0.95f, 0.78f, 0.86f);
            case ImpactRegion.FrontCorner: return new Vector3(0.76f, 0.72f, 0.72f);
            case ImpactRegion.RearCorner: return new Vector3(0.78f, 0.74f, 0.74f);
            default: return new Vector3(0.58f, 0.70f, 0.86f);
        }
    }

    private static float ComputeDepth(float speed, ImpactRegion region)
    {
        var multiplier = region == ImpactRegion.Side ? 0.78f :
            (region == ImpactRegion.FrontCorner || region == ImpactRegion.RearCorner ? 0.92f : 1f);
        return Mathf.Clamp((speed - MinimumImpactSpeed) * 0.006f, 0.012f, MaximumDentDepth) * multiplier;
    }

    private static MixedGeometry GetMixedGeometry(MeshFilter filter)
    {
        // Exact imported nodes. The material groups combine disconnected
        // luggage-bay or lamp geometry with otherwise fixed cabin geometry.
        return filter.name switch
        {
            "Object_9" when filter.transform.parent?.parent?.name == "Interior_Geo_lodA_4" =>
                MixedGeometry.FrontLuggageBay,
            "Object_12" when filter.transform.parent?.parent?.name == "Window_Geo_lodA_6" =>
                MixedGeometry.LampCovers,
            "Object_40" when filter.transform.parent?.parent?.name == "WindowInside_Geo_lodA_24" =>
                MixedGeometry.LampCovers,
            _ => MixedGeometry.None
        };
    }

    private static int[]? GetDeformableIndices(Vector3[] vertices, MixedGeometry geometry)
    {
        if (geometry == MixedGeometry.None)
            return null;

        var indices = new List<int>();
        for (var index = 0; index < vertices.Length; index++)
        {
            // Source-local Z cuts occupy the gaps between the lamp clusters
            // and the windshield/rear glazing. The luggage bay starts at
            // Z=1.365, forward of the cabin and behind the headlamp clusters.
            var z = vertices[index].z;
            if (geometry == MixedGeometry.FrontLuggageBay ? z >= 1.365f : z >= 1.5f || z <= -1.9f)
                indices.Add(index);
        }
        return indices.ToArray();
    }

    internal static bool IsExcluded(MeshFilter filter)
    {
        if (GetMixedGeometry(filter) != MixedGeometry.None)
            return false;

        // The setup moves all four fragmented source wheel assemblies beneath
        // Wheel_* roots and all caliper geometry to Caliper_* anchors.
        for (var node = filter.transform; node != null; node = node.parent)
        {
            var name = node.name;
            if (name.StartsWith("Wheel_", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Caliper_", StringComparison.OrdinalIgnoreCase) ||
                (name.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0 && !IsLampGeometry(filter)) ||
                (name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 && !IsLampGeometry(filter)) ||
                name.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Seat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Steering", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Dashboard", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        var renderer = filter.GetComponent<Renderer>();
        if (renderer != null)
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                var materialName = material.name;
                if ((materialName.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0 && !IsLampGeometry(filter)) ||
                    (materialName.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 && !IsLampGeometry(filter)) ||
                    materialName.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    materialName.IndexOf("SeatBelt", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool IsLampGeometry(MeshFilter filter)
    {
        if (IsLampOverlay(filter))
            return true;
        for (var node = filter.transform; node != null; node = node.parent)
        {
            var name = node.name;
            if (name.IndexOf("Lamp", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Light", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Headlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Tail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.Equals("Object_14", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Object_42", StringComparison.OrdinalIgnoreCase) || name.Equals("Object_16", StringComparison.OrdinalIgnoreCase) || name.Equals("Object_44", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;
        foreach (var material in renderer.sharedMaterials)
            if (material != null &&
                (material.name.IndexOf("red_glass", StringComparison.OrdinalIgnoreCase) >= 0 || material.name.IndexOf("orange_glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 material.name.IndexOf("light", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
        return false;
    }

    private static bool IsLampOverlay(MeshFilter filter) =>
        filter.name.StartsWith("Aventador_", StringComparison.Ordinal) &&
        filter.transform.parent?.GetComponent<MeshFilter>() != null;

    private static bool IsAttachedExteriorDetail(MeshFilter filter)
    {
        // Authored light overlays use only selected faces of their source mesh.
        // Apply the same per-vertex field even if that subset has tiny bounds.
        if (IsLampOverlay(filter))
            return false;
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;
        var size = renderer.bounds.size;
        var longestSide = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        return longestSide <= 0.32f;
    }

    private void RecordSkipped(string label)
    {
        if (skipExamples.Count < 8) skipExamples.Add(label);
        excludedMeshCount++;
    }

    private void RestoreMeshes(string reason)
    {
        var restored = 0;
        foreach (var state in meshes)
        {
            if (state.Filter == null || state.RuntimeMesh == null)
                continue;
            Array.Copy(state.OriginalVertices, state.Vertices, state.OriginalVertices.Length);
            state.Filter.sharedMesh = state.RuntimeMesh;
            state.RuntimeMesh.vertices = state.Vertices;
            if (state.OriginalNormals.Length == state.Vertices.Length) state.RuntimeMesh.normals = state.OriginalNormals;
            if (state.OriginalTangents.Length == state.Vertices.Length) state.RuntimeMesh.tangents = state.OriginalTangents;
            state.RuntimeMesh.bounds = state.OriginalBounds;
            restored++;
        }
        DamageInfo($"LamborghiniAventador damage repair vehicle={vehicle?.GetInstanceID()} reason='{reason}' restoredMeshes={restored}.");
    }

    private void WarnOnce(ref bool reported, string message)
    {
        if (reported) return;
        reported = true;
        context?.Logger.Warn(message);
    }

    private void DamageInfo(string message)
    {
        if (LamborghiniAventadorDiagnostics.DebugEnabled && DamageDebugEnabled && diagnosticMessages++ < MaximumDiagnosticMessages)
            context?.Logger.Info(message);
    }

    private static Bounds BoundsOf(Vector3[] vertices, int[]? indices = null)
    {
        var bounds = new Bounds(vertices[indices == null ? 0 : indices[0]], Vector3.zero);
        var count = indices?.Length ?? vertices.Length;
        for (var i = 1; i < count; i++) bounds.Encapsulate(vertices[indices == null ? i : indices[i]]);
        return bounds;
    }

    private static Bounds ExpandBounds(Bounds bounds, Matrix4x4 fromVehicle, float distance)
    {
        var ex = new Vector3(fromVehicle.m00, fromVehicle.m01, fromVehicle.m02).magnitude;
        var ey = new Vector3(fromVehicle.m10, fromVehicle.m11, fromVehicle.m12).magnitude;
        var ez = new Vector3(fromVehicle.m20, fromVehicle.m21, fromVehicle.m22).magnitude;
        bounds.Expand(2f * distance * new Vector3(ex, ey, ez));
        return bounds;
    }

    private static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
    {
        var center = matrix.MultiplyPoint3x4(bounds.center);
        var extents = bounds.extents;
        // Bounds expects full size, whereas the transformed values are extents.
        return new Bounds(center, 2f * new Vector3(
            Mathf.Abs(matrix.m00) * extents.x + Mathf.Abs(matrix.m01) * extents.y + Mathf.Abs(matrix.m02) * extents.z,
            Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z,
            Mathf.Abs(matrix.m20) * extents.x + Mathf.Abs(matrix.m21) * extents.y + Mathf.Abs(matrix.m22) * extents.z));
    }

    private void ReleaseOwnedMeshes()
    {
        foreach (var state in meshes)
            if (state.Filter != null && state.RuntimeMesh != null && state.Filter.sharedMesh == state.RuntimeMesh && state.OriginalMesh != null)
                state.Filter.sharedMesh = state.OriginalMesh;
        foreach (var mesh in ownedMeshes)
            if (mesh != null) Destroy(mesh);
        ownedMeshes.Clear();
    }

    private void OnDestroy()
    {
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        ReleaseOwnedMeshes();
    }
}
