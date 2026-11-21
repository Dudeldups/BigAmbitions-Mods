#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using BAModAPI;
using BusinessLayoutSets;
using NWH.VehiclePhysics2.Damage;
using UnityEngine;

/// <summary>
/// Owns Bentley-only, per-instance deformation for paint and approved exterior
/// trim meshes. NWH remains the authority for valid collision contacts,
/// vehicle condition, and repair state.
/// </summary>
[DisallowMultipleComponent]
internal sealed class BentleyContinentalGTSpeedDamageController : MonoBehaviour
{
    internal static bool DamageDebugEnabled { get; set; } = false;

    private const float MaximumDisplacement = .16f;
    private const float ImpactDepthPerExcessMps = .0055f;
    private const float SideRadius = .62f;
    private const float EndLateralRadius = .96f;
    private const float EndVerticalRadius = .72f;
    private const float FrontLongitudinalRadius = .92f;
    private const float RearLongitudinalRadius = 1.02f;
    private const float EndContactMinimumOffset = 1.65f;
    private const float CollisionCooldown = .35f;
    private const float RepairedDamageThreshold = .001f;
    private const float MaximumNativeDamageIntensity = .99f;
    private const int MaximumPendingImpacts = 4;
    private const int VertexBudgetPerFrame = 2048;
    private const double MillisecondBudget = 2d;

    private static readonly string[] ExcludedNameMarkers =
    {
        "wheel", "tire", "tyre", "brake", "caliper", "calliper",
        "interior", "seat", "dashboard", "window",
    };

    private static readonly string[] ExteriorLampNameMarkers =
    {
        "light", "lamp", "headlight", "taillight",
    };

    // These are exact source material names from the Bentley GLB's prepared
    // materials. The splitters and other exterior details are separate meshes
    // from Paint/OEMPaint and must follow the same local impact deformation.
    // Cabin glass, Engine, Interior, wheel, and brake materials are purposely
    // absent; lamp lenses are enabled only for a named exterior lamp assembly.
    private static readonly string[] ExteriorTrimMaterialNames =
    {
        "Bentley_Base_Prepared",
        "Bentley_Coloured_Prepared",
        "Bentley_BadgeB_Prepared",
        "Bentley_Grille1A_Prepared",
        "Bentley_Grille2A_Prepared",
        "Bentley_Grille3A_Prepared",
        "Bentley_Grille4A_Prepared",
        "Bentley_Grille5A_Prepared",
        "Bentley_Grille6A_Prepared",
        "Bentley_Grille7A_Prepared",
        "Bentley_Grille8A_Prepared",
        "Bentley_Grille9A_Prepared",
        "Bentley_TexturedA_Prepared",
        "Bentley_Textured2A_Prepared",
        "Bentley_ManufacturerPlateA_Prepared",
        "Bentley_LightA_Prepared",
        "Bentley_LampEmitters_Prepared",
    };

    private sealed class Surface
    {
        internal Renderer Renderer = null!;
        internal Mesh SourceMesh = null!;
        internal Mesh RuntimeMesh = null!;
        internal Vector3[] OriginalVertices = null!;
        internal Vector3[] WorkingVertices = null!;
        internal Vector3[] OriginalNormals = Array.Empty<Vector3>();
        internal Vector4[] OriginalTangents = Array.Empty<Vector4>();
        internal int[] EligibleIndices = Array.Empty<int>();
        internal Bounds SourceMeshBounds;
        internal Bounds SafeMeshBounds;
        internal Bounds SourceRendererBounds;
        internal Bounds SafeRendererBounds;
        internal BoneWeight[] BoneWeights = Array.Empty<BoneWeight>();
        internal Matrix4x4[] Bindposes = Array.Empty<Matrix4x4>();
        internal Transform[] Bones = Array.Empty<Transform>();
    }

    private readonly struct Contact
    {
        internal readonly Vector3 Point;
        internal readonly Vector3 Normal;

        internal Contact(Vector3 point, Vector3 normal)
        {
            Point = point;
            Normal = normal;
        }
    }

    private sealed class Impact
    {
        internal Contact[] Contacts = Array.Empty<Contact>();
        internal float ClosingSpeed;
        internal float Depth;
        internal string Other = "unknown";
    }

    private sealed class NativeDamageObservation
    {
        internal float ClosingSpeed;
        internal float RelativeSpeed;
        internal float Intensity;
        internal float ImpulseMagnitude;
        internal float FixedDeltaTime;
        internal float RigidbodyMass;
        internal float DamageHandlerBefore;
        internal float VehicleInstanceBefore;
        internal float PotentialRawDamageIncrement;
        internal string Other = "unknown";
    }

    private readonly List<Surface> surfaces = new List<Surface>();
    private readonly Queue<Impact> pending = new Queue<Impact>();
    private readonly List<VehicleDeformationController.VehicleDeformation> acceptedImpacts =
        new List<VehicleDeformationController.VehicleDeformation>();

    private VehicleController? vehicle;
    private DamageHandler? damageHandler;
    private ModContext? context;
    private Coroutine? deformationWorker;
    private Coroutine? repairWorker;
    private Coroutine? savedRecordWorker;
    private Coroutine? nativeDamageObservationWorker;
    private readonly Queue<NativeDamageObservation> nativeDamageObservations =
        new Queue<NativeDamageObservation>();
    private float thresholdMps;
    private float nextImpactAt;
    private float previousDamage;
    private bool initialized;
    private bool failureReported;
    private bool previousNativeMeshDeform;
    private bool nativeMeshDeformCaptured;

    /// <summary>Bind one spawned Bentley and clone its marked body mesh data.</summary>
    internal bool Initialize(VehicleController controller, ModContext? modContext)
    {
        if (controller == null)
            throw new ArgumentNullException(nameof(controller));

        if (initialized)
        {
            if (vehicle == controller)
                return true;
            Cleanup();
        }

        vehicle = controller;
        context = modContext;
        damageHandler = controller.GetComponentInChildren<DamageHandler>(true);
        if (damageHandler == null)
        {
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed damage vehicle={controller.GetInstanceID()}: " +
                "native NWH DamageHandler is missing; visual deformation was not initialized.");
            return false;
        }

        try
        {
            thresholdMps = Mathf.Max(1f, damageHandler.decelerationThreshold / 100f);
            previousDamage = ReadCurrentDamage();
            DisableLegacyDeformation(controller);
            foreach (var renderer in controller.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || IsExcludedByName(renderer))
                    continue;

                var sourceMesh = GetSharedMesh(renderer);
                if (sourceMesh == null)
                    continue;

                int[] indices;
                try
                {
                    if (!TryGetDeformableExteriorVertices(renderer, sourceMesh, out indices))
                        continue;
                }
                catch (Exception exception)
                {
                    context?.Logger.Warn(
                        $"BentleyContinentalGTSpeed damage vehicle={controller.GetInstanceID()}: " +
                        $"could not inspect exterior mesh renderer='{renderer.name}', mesh='{sourceMesh.name}': " +
                        $"{exception.GetType().Name}: {exception.Message}");
                    continue;
                }

                var surface = CreateSurface(renderer, sourceMesh, indices);
                if (surface == null)
                {
                    context?.Logger.Warn(
                        $"BentleyContinentalGTSpeed damage vehicle={controller.GetInstanceID()}: " +
                        $"could not clone readable exterior renderer='{renderer.name}', mesh='{sourceMesh.name}'.");
                    continue;
                }
                surfaces.Add(surface);
                SetSharedMesh(renderer, surface.RuntimeMesh);
                SetSafeBounds(surface);

                DamageInfo(
                    $"BentleyContinentalGTSpeed damage ownership vehicle={controller.GetInstanceID()}, " +
                    $"renderer='{renderer.name}', mesh='{sourceMesh.name}', " +
                    $"exteriorVertices={indices.Length}/{sourceMesh.vertexCount}.");
            }

            if (surfaces.Count == 0)
            {
                context?.Logger.Warn(
                    $"BentleyContinentalGTSpeed damage vehicle={controller.GetInstanceID()}: " +
                    "no readable renderer with an approved exterior submesh was found; " +
                    "native condition damage remains enabled but visual deformation is unavailable.");
                Cleanup();
                return false;
            }

            previousNativeMeshDeform = damageHandler.meshDeform;
            nativeMeshDeformCaptured = true;
            damageHandler.meshDeform = false;
            GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
            initialized = true;
            ReplaySavedImpacts();
            DamageInfo(
                $"BentleyContinentalGTSpeed damage initialized vehicle={controller.GetInstanceID()}, " +
                $"surfaces={surfaces.Count}, threshold={thresholdMps:0.00}mps, " +
                $"nativeMeshDeform={damageHandler.meshDeform}, " +
                $"nativeDamageIntensity={damageHandler.damageIntensity:0.###}, " +
                $"damageEnabled={damageHandler.enabled}.");
            return true;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed damage vehicle={controller.GetInstanceID()} initialization failed: " +
                $"{exception.GetType().Name}: {exception.Message}");
            Cleanup();
            return false;
        }
    }

    /// <summary>Release subscriptions and return every renderer to its source mesh.</summary>
    internal void Cleanup()
    {
        if (initialized)
            GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;

        if (nativeMeshDeformCaptured && damageHandler != null)
            damageHandler.meshDeform = previousNativeMeshDeform;

        StopAllCoroutines();
        deformationWorker = repairWorker = savedRecordWorker = null;
        nativeDamageObservationWorker = null;
        pending.Clear();
        acceptedImpacts.Clear();
        nativeDamageObservations.Clear();

        foreach (var surface in surfaces)
        {
            if (surface.Renderer != null && surface.SourceMesh != null)
            {
                SetSharedMesh(surface.Renderer, surface.SourceMesh);
                if (surface.Renderer is SkinnedMeshRenderer skinned)
                    skinned.localBounds = surface.SourceRendererBounds;
            }
            if (surface.RuntimeMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(surface.RuntimeMesh);
                else
                    DestroyImmediate(surface.RuntimeMesh);
            }
        }
        surfaces.Clear();

        initialized = false;
        vehicle = null;
        damageHandler = null;
        context = null;
        thresholdMps = 0f;
        previousDamage = 0f;
        nextImpactAt = 0f;
        failureReported = false;
        previousNativeMeshDeform = false;
        nativeMeshDeformCaptured = false;
    }

    private static Mesh? GetSharedMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
            return skinned.sharedMesh;
        var filter = renderer.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    private static void SetSharedMesh(Renderer renderer, Mesh mesh)
    {
        if (renderer is SkinnedMeshRenderer skinned)
        {
            skinned.sharedMesh = mesh;
            return;
        }

        var filter = renderer.GetComponent<MeshFilter>();
        if (filter != null)
            filter.sharedMesh = mesh;
    }

    private static Surface? CreateSurface(Renderer renderer, Mesh sourceMesh, int[] eligibleIndices)
    {
        try
        {
            var sourceVertices = sourceMesh.vertices;
            if (sourceVertices.Length == 0 || eligibleIndices.Length == 0)
                return null;

            var runtimeMesh = Instantiate(sourceMesh);
            runtimeMesh.name = sourceMesh.name + "_BentleyRuntimeDamage";
            runtimeMesh.MarkDynamic();
            var sourceRendererBounds = renderer is SkinnedMeshRenderer skinned
                ? skinned.localBounds
                : sourceMesh.bounds;
            var sourceMeshBounds = sourceMesh.bounds;
            var safeRendererBounds = sourceRendererBounds;
            var safeMeshBounds = sourceMeshBounds;
            safeRendererBounds.Expand(MaximumDisplacement * 2f);
            safeMeshBounds.Expand(MaximumDisplacement * 2f);

            var surface = new Surface
            {
                Renderer = renderer,
                SourceMesh = sourceMesh,
                RuntimeMesh = runtimeMesh,
                OriginalVertices = sourceVertices,
                WorkingVertices = (Vector3[])sourceVertices.Clone(),
                OriginalNormals = sourceMesh.normals,
                OriginalTangents = sourceMesh.tangents,
                EligibleIndices = eligibleIndices,
                SourceMeshBounds = sourceMeshBounds,
                SafeMeshBounds = safeMeshBounds,
                SourceRendererBounds = sourceRendererBounds,
                SafeRendererBounds = safeRendererBounds,
            };

            if (renderer is SkinnedMeshRenderer skinnedRenderer)
            {
                surface.Bones = skinnedRenderer.bones ?? Array.Empty<Transform>();
                surface.Bindposes = sourceMesh.bindposes;
                surface.BoneWeights = sourceMesh.boneWeights;
            }

            runtimeMesh.bounds = safeMeshBounds;
            return surface;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryGetDeformableExteriorVertices(Renderer renderer, Mesh mesh, out int[] eligibleIndices)
    {
        eligibleIndices = Array.Empty<int>();
        if (IsProtectedRearCabinGlass(renderer, mesh))
            return false;

        var materials = renderer.sharedMaterials;
        if (materials == null || materials.Length == 0 || mesh.subMeshCount == 0)
            return false;

        var deformableSubmeshes = new List<int>();
        var lampGlassSubmeshes = new List<int>();
        for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            if (submesh >= materials.Length)
                continue;
            if (IsBodyPaintMaterial(materials[submesh]) || IsExteriorTrimMaterial(materials[submesh]) ||
                IsVerifiedTrunkBacking(renderer, mesh, materials[submesh]))
                deformableSubmeshes.Add(submesh);
            else if (IsExteriorLampGlassMaterial(materials[submesh]))
                lampGlassSubmeshes.Add(submesh);
        }
        if (deformableSubmeshes.Count == 0 && lampGlassSubmeshes.Count == 0)
            return false;

        var paintVertices = new HashSet<int>();
        var protectedVertices = new HashSet<int>();
        Vector3[] lampVertexPositions = Array.Empty<Vector3>();
        if (lampGlassSubmeshes.Count > 0)
        {
            var vehicle = renderer.GetComponentInParent<VehicleController>(true);
            var vehicleRoot = vehicle != null ? vehicle.transform : renderer.transform.root;
            if (!TryBuildLampGlassVertexPositions(renderer, mesh, vehicleRoot, out lampVertexPositions))
                return false;
        }

        for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            var triangles = mesh.GetTriangles(submesh);
            if (deformableSubmeshes.Contains(submesh))
            {
                foreach (var vertex in triangles)
                    paintVertices.Add(vertex);
            }
            else if (lampGlassSubmeshes.Contains(submesh))
            {
                for (var triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                {
                    var first = triangles[triangle];
                    var second = triangles[triangle + 1];
                    var third = triangles[triangle + 2];
                    if (IsLampGlassVertex(lampVertexPositions, first) &&
                        IsLampGlassVertex(lampVertexPositions, second) &&
                        IsLampGlassVertex(lampVertexPositions, third))
                    {
                        paintVertices.Add(first);
                        paintVertices.Add(second);
                        paintVertices.Add(third);
                    }
                    else
                    {
                        protectedVertices.Add(first);
                        protectedVertices.Add(second);
                        protectedVertices.Add(third);
                    }
                }
            }
            else
            {
                foreach (var vertex in triangles)
                    protectedVertices.Add(vertex);
            }
        }

        paintVertices.ExceptWith(protectedVertices);
        if (paintVertices.Count == 0)
            return false;

        var result = new int[paintVertices.Count];
        paintVertices.CopyTo(result);
        Array.Sort(result);
        eligibleIndices = result;
        return true;
    }

    private static bool IsProtectedRearCabinGlass(Renderer renderer, Mesh mesh)
    {
        if (!string.Equals(renderer.name, "Object_55", StringComparison.Ordinal) || mesh.vertexCount != 370 ||
            mesh.subMeshCount != 1)
            return false;
        var materials = renderer.sharedMaterials;
        if (materials == null || materials.Length != 1 || materials[0] == null ||
            !string.Equals(NormalizeMaterialName(materials[0].name), "Bentley_GlassMtl_Prepared", StringComparison.OrdinalIgnoreCase))
            return false;

        var bounds = mesh.bounds;
        return Mathf.Abs(bounds.center.y - 1.0876f) <= .03f &&
               Mathf.Abs(bounds.center.z + .4202f) <= .03f &&
               Mathf.Abs(bounds.extents.x - .7196f) <= .03f &&
               Mathf.Abs(bounds.extents.y - .3164f) <= .03f &&
               Mathf.Abs(bounds.extents.z - 1.3794f) <= .03f;
    }

    private static bool IsBodyPaintMaterial(Material? material)
    {
        if (material == null)
            return false;

        var name = NormalizeMaterialName(material.name);
        return string.Equals(name, "Paint", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "OEMPaint", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Bentley_Paint_Prepared", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Bentley_OEMPaint_Prepared", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "BentleyMaterial_026", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "BentleyMaterial_027", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVerifiedTrunkBacking(Renderer renderer, Mesh mesh, Material? material)
    {
        if (material == null || !string.Equals(renderer.name, "Object_238", StringComparison.Ordinal) ||
            !string.Equals(NormalizeMaterialName(material.name), "Bentley_InteriorA_Prepared", StringComparison.OrdinalIgnoreCase) ||
            mesh.vertexCount != 2473)
            return false;

        var bounds = mesh.bounds;
        return Mathf.Abs(bounds.center.y - .8663f) <= .03f &&
               Mathf.Abs(bounds.center.z + 1.9173f) <= .03f &&
               Mathf.Abs(bounds.extents.x - .6014f) <= .03f &&
               Mathf.Abs(bounds.extents.y - .2082f) <= .03f &&
               Mathf.Abs(bounds.extents.z - .3747f) <= .03f;
    }

    private static bool IsExteriorTrimMaterial(Material? material)
    {
        if (material == null)
            return false;

        var name = NormalizeMaterialName(material.name);
        foreach (var exteriorMaterialName in ExteriorTrimMaterialNames)
        {
            if (string.Equals(name, exteriorMaterialName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsExteriorLampGlassMaterial(Material? material)
    {
        if (material == null)
            return false;
        var name = NormalizeMaterialName(material.name);
        return string.Equals(name, "Bentley_GlassMtl_Prepared", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Bentley_GlassAmber_Prepared", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Bentley_GlassRed_Prepared", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryBuildLampGlassVertexPositions(
        Renderer renderer,
        Mesh mesh,
        Transform vehicleRoot,
        out Vector3[] vehicleLocalVertices)
    {
        var vertices = mesh.vertices;
        vehicleLocalVertices = new Vector3[vertices.Length];
        if (vertices.Length == 0)
            return false;

        var skinned = renderer as SkinnedMeshRenderer;
        var bones = skinned != null ? skinned.bones : Array.Empty<Transform>();
        var bindposes = mesh.bindposes;
        var weights = mesh.boneWeights;
        var boneMatrices = Array.Empty<Matrix4x4>();
        if (bones.Length > 0 && bindposes.Length > 0 && weights.Length == vertices.Length)
        {
            var count = Mathf.Min(bones.Length, bindposes.Length);
            boneMatrices = new Matrix4x4[count];
            for (var boneIndex = 0; boneIndex < count; boneIndex++)
            {
                var bone = bones[boneIndex];
                boneMatrices[boneIndex] = bone != null
                    ? bone.localToWorldMatrix * bindposes[boneIndex]
                    : renderer.transform.localToWorldMatrix;
            }
        }

        var worldToVehicle = vehicleRoot.worldToLocalMatrix;
        var rendererToWorld = renderer.transform.localToWorldMatrix;
        for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            var vertexToWorld = rendererToWorld;
            if (boneMatrices.Length > 0)
                vertexToWorld = GetWeightedBoneMatrix(weights[vertexIndex], boneMatrices, rendererToWorld);
            var vehicleVertex = worldToVehicle.MultiplyPoint3x4(
                vertexToWorld.MultiplyPoint3x4(vertices[vertexIndex]));
            vehicleLocalVertices[vertexIndex] = vehicleVertex;
        }
        return true;
    }

    private static Matrix4x4 GetWeightedBoneMatrix(
        BoneWeight weights,
        Matrix4x4[] boneMatrices,
        Matrix4x4 fallback)
    {
        var matrix = Matrix4x4.zero;
        var sum = 0f;
        AddWeightedBone(ref matrix, boneMatrices, weights.boneIndex0, weights.weight0, ref sum);
        AddWeightedBone(ref matrix, boneMatrices, weights.boneIndex1, weights.weight1, ref sum);
        AddWeightedBone(ref matrix, boneMatrices, weights.boneIndex2, weights.weight2, ref sum);
        AddWeightedBone(ref matrix, boneMatrices, weights.boneIndex3, weights.weight3, ref sum);
        if (sum <= .0001f)
            return fallback;

        for (var element = 0; element < 16; element++)
            matrix[element] /= sum;
        return matrix;
    }

    private static void AddWeightedBone(
        ref Matrix4x4 matrix,
        Matrix4x4[] boneMatrices,
        int boneIndex,
        float weight,
        ref float sum)
    {
        if (weight <= 0f || boneIndex < 0 || boneIndex >= boneMatrices.Length)
            return;
        for (var element = 0; element < 16; element++)
            matrix[element] += boneMatrices[boneIndex][element] * weight;
        sum += weight;
    }

    private static bool IsLampGlassVertex(Vector3[] vehicleLocalVertices, int vertexIndex)
    {
        if (vertexIndex < 0 || vertexIndex >= vehicleLocalVertices.Length)
            return false;
        var vertex = vehicleLocalVertices[vertexIndex];
        return Mathf.Abs(vertex.z) >= 1.65f && vertex.y >= 0.25f && vertex.y <= 1.15f;
    }

    private static string NormalizeMaterialName(string name)
    {
        var normalized = name.Trim();
        const string cloneSuffix = " (Clone)";
        while (normalized.EndsWith(cloneSuffix, StringComparison.Ordinal))
            normalized = normalized.Substring(0, normalized.Length - cloneSuffix.Length);
        return normalized;
    }

    private static bool IsExcludedByName(Renderer renderer)
    {
        var isLamp = IsExteriorLamp(renderer.transform);
        for (var node = renderer.transform; node != null; node = node.parent)
        {
            var name = node.name;
            foreach (var marker in ExcludedNameMarkers)
            {
                if (isLamp && string.Equals(marker, "window", StringComparison.Ordinal))
                    continue;
                if (name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            if (!isLamp && name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool IsExteriorLamp(Transform transform)
    {
        for (var node = transform; node != null; node = node.parent)
        {
            foreach (var marker in ExteriorLampNameMarkers)
            {
                if (node.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        return false;
    }

    private void DisableLegacyDeformation(VehicleController controller)
    {
        foreach (var component in controller.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || !string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
                continue;

            component.enabled = false;
            var type = component.GetType();
            SetLegacyArray(type, component, "meshFilters", Array.Empty<MeshFilter>());
            SetLegacyArray(type, component, "originalMeshes", Array.Empty<Mesh>());
            ClearLegacyQueue(type, component, "_deformationQueue");
        }
    }

    private static void SetLegacyArray(Type type, object instance, string name, Array value)
    {
        var field = FindField(type, name);
        if (field != null && field.FieldType.IsArray && field.FieldType.GetElementType() == value.GetType().GetElementType())
            field.SetValue(instance, value);
    }

    private static void ClearLegacyQueue(Type type, object instance, string name)
    {
        var field = FindField(type, name);
        if (field?.GetValue(instance) is IList list && !list.IsReadOnly && !list.IsFixedSize)
            list.Clear();
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
    }

    private void ReplaySavedImpacts()
    {
        var records = vehicle?.vehicleInstance?.deformations;
        if (records == null)
            return;

        foreach (var record in records)
        {
            if (record.points == null || record.decelerationMagnitude / 100f < thresholdMps)
                continue;

            var contacts = new List<Contact>(4);
            foreach (var saved in record.points)
            {
                if (contacts.Count >= 4)
                    break;
                var normal = saved.normal;
                if (normal.y > .65f)
                    continue;
                var normalVector = new Vector3(normal.x, normal.y, normal.z).normalized;
                contacts.Add(new Contact(saved.point, normalVector));
            }
            if (contacts.Count == 0)
                continue;

            acceptedImpacts.Add(record);
            pending.Enqueue(new Impact
            {
                Contacts = contacts.ToArray(),
                ClosingSpeed = record.decelerationMagnitude / 100f,
                Depth = Mathf.Clamp(
                    (record.decelerationMagnitude / 100f - thresholdMps) * ImpactDepthPerExcessMps,
                    .012f,
                    MaximumDisplacement),
                Other = "saved impact",
            });
        }

        if (pending.Count > 0)
            deformationWorker = StartCoroutine(ProcessPendingImpacts());
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || vehicle == null || damageHandler == null || collision == null)
            return;

        ScheduleSavedRecordFlush();
        if (collision.contactCount == 0 || Time.unscaledTime < nextImpactAt ||
            (SaveGameManager.Current != null && SaveGameManager.Current.gameVariables.disableVehicleDamage) ||
            !DamageHandler.IsCollisionValid(collision))
            return;

        var contacts = SelectContacts(collision);
        if (contacts.Length == 0)
            return;

        var closing = ClosingSpeed(collision);
        if (closing < thresholdMps)
        {
            DamageInfo(
                $"BentleyContinentalGTSpeed ignored damage vehicle={vehicle.GetInstanceID()}, " +
                $"other='{collision.collider?.name ?? "unknown"}', closing={closing:0.00}mps threshold={thresholdMps:0.00}mps.");
            return;
        }

        nextImpactAt = Time.unscaledTime + CollisionCooldown;
        var impact = new Impact
        {
            Contacts = contacts,
            ClosingSpeed = closing,
            Depth = Mathf.Clamp((closing - thresholdMps) * ImpactDepthPerExcessMps, .012f, MaximumDisplacement),
            Other = collision.collider != null ? collision.collider.name : "unknown",
        };

        QueueNativeDamageObservation(collision, impact);

        StoreAcceptedImpact(impact);
        if (pending.Count >= MaximumPendingImpacts)
        {
            pending.Dequeue();
            DamageInfo("BentleyContinentalGTSpeed deformation queue coalesced its oldest pending impact.");
        }
        pending.Enqueue(impact);
        if (deformationWorker == null)
            deformationWorker = StartCoroutine(ProcessPendingImpacts());
    }

    private Contact[] SelectContacts(Collision collision)
    {
        var contacts = new List<Contact>(4);
        for (var index = 0; index < collision.contactCount && contacts.Count < 4; index++)
        {
            var contact = collision.GetContact(index);
            var localPoint = transform.InverseTransformPoint(contact.point);
            var localNormal = transform.InverseTransformDirection(contact.normal).normalized;
            if (localNormal.sqrMagnitude < .5f)
                continue;

            var duplicate = false;
            foreach (var selected in contacts)
            {
                if ((selected.Point - localPoint).sqrMagnitude < .0225f)
                {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate)
                contacts.Add(new Contact(localPoint, localNormal));
        }
        return contacts.ToArray();
    }

    private static float ClosingSpeed(Collision collision)
    {
        var closing = 0f;
        for (var index = 0; index < collision.contactCount; index++)
        {
            var worldNormal = collision.GetContact(index).normal.normalized;
            closing = Mathf.Max(closing, Mathf.Max(0f, Vector3.Dot(collision.relativeVelocity, worldNormal)));
        }
        return closing;
    }

    private void StoreAcceptedImpact(Impact impact)
    {
        var record = new VehicleDeformationController.VehicleDeformation
        {
            decelerationMagnitude = impact.ClosingSpeed * 100f,
            points = Array.ConvertAll(impact.Contacts, contact =>
                new VehicleDeformationController.VehicleDeformation.VehicleDeformationPoint
                {
                    point = contact.Point,
                    normal = contact.Normal,
                    deformationRandomness = 0f,
                }),
        };
        acceptedImpacts.Add(record);
        if (acceptedImpacts.Count > 24)
            acceptedImpacts.RemoveAt(0);
        ScheduleSavedRecordFlush();
    }

    private void QueueNativeDamageObservation(Collision collision, Impact impact)
    {
        if (!DamageDebugEnabled || !BentleyContinentalGTSpeedDiagnostics.DebugEnabled ||
            damageHandler == null || vehicle == null)
            return;

        var observation = new NativeDamageObservation
        {
            ClosingSpeed = impact.ClosingSpeed,
            RelativeSpeed = collision.relativeVelocity.magnitude,
            Intensity = damageHandler.damageIntensity,
            DamageHandlerBefore = damageHandler.Damage,
            VehicleInstanceBefore = vehicle.vehicleInstance?.damage ?? 0f,
            Other = impact.Other,
        };
        var body = vehicle.GetComponent<Rigidbody>();
        observation.ImpulseMagnitude = collision.impulse.magnitude;
        observation.FixedDeltaTime = Time.fixedDeltaTime;
        observation.RigidbodyMass = body != null ? body.mass : 0f;
        if (observation.FixedDeltaTime > 0f && observation.RigidbodyMass > 0f)
        {
            observation.PotentialRawDamageIncrement = observation.ImpulseMagnitude /
                                                       (observation.FixedDeltaTime * observation.RigidbodyMass * 10f) *
                                                       Mathf.Clamp(observation.Intensity, 0f, MaximumNativeDamageIntensity) * .005f;
        }
        if (nativeDamageObservations.Count >= 8)
            nativeDamageObservations.Dequeue();
        nativeDamageObservations.Enqueue(observation);
        if (nativeDamageObservationWorker == null)
            nativeDamageObservationWorker = StartCoroutine(FlushNativeDamageObservations());
    }

    private IEnumerator FlushNativeDamageObservations()
    {
        // The game's CarController copies DamageHandler.Damage to the saved
        // VehicleInstance after one frame; sample after that callback.
        yield return null;
        yield return null;

        while (nativeDamageObservations.Count > 0)
        {
            var observation = nativeDamageObservations.Dequeue();
            var handlerAfter = damageHandler != null ? damageHandler.Damage : 0f;
            var instanceAfter = vehicle?.vehicleInstance?.damage ?? 0f;
            DamageInfo(
                $"BentleyContinentalGTSpeed native condition collision vehicle={vehicle?.GetInstanceID()}, " +
                $"other='{observation.Other}', closing={observation.ClosingSpeed:0.00}mps, " +
                $"relative={observation.RelativeSpeed:0.00}mps, threshold={thresholdMps:0.00}mps, " +
                $"impulse={observation.ImpulseMagnitude:0.###}Ns, mass={observation.RigidbodyMass:0.##}kg, " +
                $"intensity={observation.Intensity:0.###}, potentialRawIncrement={observation.PotentialRawDamageIncrement:0.####}, " +
                $"damageHandler={observation.DamageHandlerBefore:0.####}->{handlerAfter:0.####}, " +
                $"vehicleInstance={observation.VehicleInstanceBefore:0.####}->{instanceAfter:0.####}.");
        }
        nativeDamageObservationWorker = null;
    }

    private void ScheduleSavedRecordFlush()
    {
        if (savedRecordWorker != null)
            StopCoroutine(savedRecordWorker);
        savedRecordWorker = StartCoroutine(FlushSavedRecordsAfterNativeCallbacks());
    }

    private IEnumerator FlushSavedRecordsAfterNativeCallbacks()
    {
        yield return null;
        var records = vehicle?.vehicleInstance?.deformations;
        if (records != null)
        {
            records.Clear();
            records.AddRange(acceptedImpacts);
        }
        savedRecordWorker = null;
    }

    private IEnumerator ProcessPendingImpacts()
    {
        var work = DeformPendingImpacts();
        while (true)
        {
            bool more;
            object? next;
            try
            {
                more = work.MoveNext();
                next = more ? work.Current : null;
            }
            catch (Exception exception)
            {
                if (!failureReported)
                {
                    failureReported = true;
                    context?.Logger.Warn(
                        $"BentleyContinentalGTSpeed deformation vehicle={vehicle?.GetInstanceID()} failed: " +
                        $"{exception.GetType().Name}: {exception.Message}");
                }
                pending.Clear();
                break;
            }

            if (!more)
                break;
            yield return next;
        }

        deformationWorker = null;
    }

    private IEnumerator DeformPendingImpacts()
    {
        // Defer work so native collision callbacks stay small and worker state
        // is established before a full-mesh pass begins.
        yield return null;
        var inspected = 0;
        var sliceStarted = Stopwatch.GetTimestamp();

        while (pending.Count > 0)
        {
            var impact = pending.Dequeue();
            var changedMeshes = 0;
            var changedVertices = 0;
            var logRearTargets = DamageDebugEnabled && BentleyContinentalGTSpeedDiagnostics.DebugEnabled;
            var rearContactSummary = logRearTargets
                ? string.Join(";", impact.Contacts.Select(contact =>
                    FormattableString.Invariant($"{contact.Point.x:0.00},{contact.Point.y:0.00},{contact.Point.z:0.00}/{contact.Normal.x:0.00},{contact.Normal.y:0.00},{contact.Normal.z:0.00}")))
                : string.Empty;

            foreach (var surface in surfaces)
            {
                if (surface.Renderer == null || surface.RuntimeMesh == null)
                    continue;

                var boneMatrices = BuildBoneMatrices(surface);
                var dirty = false;
                var isRearDiagnosticTarget = logRearTargets && IsRearDiagnosticTarget(surface.Renderer);
                var eligibleCount = 0;
                var determinantSkipped = 0;
                var influencedCount = 0;
                var changedOnSurface = 0;
                foreach (var vertexIndex in surface.EligibleIndices)
                {
                    if (vertexIndex < 0 || vertexIndex >= surface.WorkingVertices.Length)
                        continue;
                    eligibleCount++;

                    var vertexToWorld = GetVertexToWorld(surface, vertexIndex, boneMatrices);
                    if (Mathf.Abs(vertexToWorld.determinant) < .0000001f)
                    {
                        if (isRearDiagnosticTarget)
                            determinantSkipped++;
                        continue;
                    }
                    var worldToVertex = vertexToWorld.inverse;
                    var currentWorld = vertexToWorld.MultiplyPoint3x4(surface.WorkingVertices[vertexIndex]);
                    var originalWorld = vertexToWorld.MultiplyPoint3x4(surface.OriginalVertices[vertexIndex]);
                    var currentVehicle = transform.InverseTransformPoint(currentWorld);
                    var originalVehicle = transform.InverseTransformPoint(originalWorld);

                    var strongest = 0f;
                    var direction = Vector3.zero;
                    foreach (var contact in impact.Contacts)
                    {
                        var candidateDirection = InwardDirection(contact);
                        var weight = Influence(currentVehicle, contact);
                        if (weight <= strongest)
                            continue;
                        strongest = weight;
                        direction = candidateDirection;
                    }

                    if (strongest > 0f && direction.sqrMagnitude > .5f)
                    {
                        if (isRearDiagnosticTarget)
                            influencedCount++;
                        var deformedVehicle = CalculateDeformedVehiclePoint(
                            currentVehicle, originalVehicle, direction, impact.Depth, strongest);
                        var deformedWorld = transform.TransformPoint(deformedVehicle);
                        var deformedVertex = worldToVertex.MultiplyPoint3x4(deformedWorld);
                        if ((deformedVertex - surface.WorkingVertices[vertexIndex]).sqrMagnitude > .00000001f)
                        {
                            surface.WorkingVertices[vertexIndex] = deformedVertex;
                            dirty = true;
                            changedVertices++;
                            changedOnSurface++;
                        }
                    }

                    inspected++;
                    if (inspected >= VertexBudgetPerFrame || (inspected % 64 == 0 &&
                        (Stopwatch.GetTimestamp() - sliceStarted) * 1000d / Stopwatch.Frequency >= MillisecondBudget))
                    {
                        yield return null;
                        inspected = 0;
                        sliceStarted = Stopwatch.GetTimestamp();
                    }
                }

                if (!dirty)
                {
                    if (isRearDiagnosticTarget)
                        DamageInfo(
                            $"Bentley rear impact target vehicle={vehicle?.GetInstanceID()}, renderer='{surface.Renderer.name}', " +
                            $"contacts=[{rearContactSummary}], depth={impact.Depth:0.000}, eligible={eligibleCount}, " +
                            $"determinantSkipped={determinantSkipped}, influenced={influencedCount}, changed={changedOnSurface}.");
                    continue;
                }

                surface.RuntimeMesh.vertices = surface.WorkingVertices;
                surface.RuntimeMesh.bounds = surface.SafeMeshBounds;
                SetSharedMesh(surface.Renderer, surface.RuntimeMesh);
                SetSafeBounds(surface);
                changedMeshes++;
                if (isRearDiagnosticTarget)
                    DamageInfo(
                        $"Bentley rear impact target vehicle={vehicle?.GetInstanceID()}, renderer='{surface.Renderer.name}', " +
                        $"contacts=[{rearContactSummary}], depth={impact.Depth:0.000}, eligible={eligibleCount}, " +
                        $"determinantSkipped={determinantSkipped}, influenced={influencedCount}, changed={changedOnSurface}.");
            }

            DamageInfo(
                $"BentleyContinentalGTSpeed deformation vehicle={vehicle?.GetInstanceID()}, " +
                $"other='{impact.Other}', closing={impact.ClosingSpeed:0.00}mps, " +
                $"changedMeshes={changedMeshes}, changedVertices={changedVertices}.");
        }

    }

    private static bool IsRearDiagnosticTarget(Renderer renderer) =>
        string.Equals(renderer.name, "Object_86", StringComparison.Ordinal) ||
        string.Equals(renderer.name, "Object_51", StringComparison.Ordinal) ||
        string.Equals(renderer.name, "Object_238", StringComparison.Ordinal) ||
        string.Equals(renderer.name, "Object_242", StringComparison.Ordinal) ||
        string.Equals(renderer.name, "Object_240", StringComparison.Ordinal) ||
        string.Equals(renderer.name, "Object_234", StringComparison.Ordinal) ||
        string.Equals(renderer.name, "Object_53", StringComparison.Ordinal);

    private Matrix4x4[] BuildBoneMatrices(Surface surface)
    {
        if (surface.Bones.Length == 0 || surface.Bindposes.Length == 0)
            return Array.Empty<Matrix4x4>();

        var count = Mathf.Min(surface.Bones.Length, surface.Bindposes.Length);
        var matrices = new Matrix4x4[count];
        for (var index = 0; index < count; index++)
        {
            var bone = surface.Bones[index];
            matrices[index] = bone != null
                ? bone.localToWorldMatrix * surface.Bindposes[index]
                : surface.Renderer.transform.localToWorldMatrix;
        }
        return matrices;
    }

    private static Matrix4x4 GetVertexToWorld(Surface surface, int vertexIndex, Matrix4x4[] boneMatrices)
    {
        if (surface.BoneWeights.Length != surface.WorkingVertices.Length || boneMatrices.Length == 0)
            return surface.Renderer.transform.localToWorldMatrix;

        var weights = surface.BoneWeights[vertexIndex];
        var matrix = Matrix4x4.zero;
        var sum = 0f;
        AddBoneMatrix(ref matrix, boneMatrices, weights.boneIndex0, weights.weight0, ref sum);
        AddBoneMatrix(ref matrix, boneMatrices, weights.boneIndex1, weights.weight1, ref sum);
        AddBoneMatrix(ref matrix, boneMatrices, weights.boneIndex2, weights.weight2, ref sum);
        AddBoneMatrix(ref matrix, boneMatrices, weights.boneIndex3, weights.weight3, ref sum);
        if (sum > .0001f)
            return ScaleMatrix(matrix, 1f / sum);
        return surface.Renderer.transform.localToWorldMatrix;
    }

    private static void AddBoneMatrix(
        ref Matrix4x4 accumulator,
        Matrix4x4[] boneMatrices,
        int boneIndex,
        float weight,
        ref float sum)
    {
        if (weight <= 0f || boneIndex < 0 || boneIndex >= boneMatrices.Length)
            return;
        for (var element = 0; element < 16; element++)
            accumulator[element] += boneMatrices[boneIndex][element] * weight;
        sum += weight;
    }

    private static Matrix4x4 ScaleMatrix(Matrix4x4 matrix, float scale)
    {
        for (var element = 0; element < 16; element++)
            matrix[element] *= scale;
        return matrix;
    }

    private static Vector3 InwardDirection(Contact contact)
    {
        var isEnd = Mathf.Abs(contact.Point.z) >= EndContactMinimumOffset &&
                    Mathf.Abs(contact.Point.z) > Mathf.Abs(contact.Point.x);
        if (isEnd)
            return contact.Point.z >= 0f ? Vector3.back : Vector3.forward;

        var direction = contact.Normal.normalized;
        if (Vector3.Dot(direction, -contact.Point) < 0f)
            direction = -direction;
        return direction;
    }

    private static float Influence(Vector3 point, Contact contact)
    {
        var delta = point - contact.Point;
        var isEnd = Mathf.Abs(contact.Point.z) >= EndContactMinimumOffset &&
                    Mathf.Abs(contact.Point.z) > Mathf.Abs(contact.Point.x);
        float normalizedDistance;
        if (isEnd)
        {
            var longitudinalRadius = contact.Point.z >= 0f ? FrontLongitudinalRadius : RearLongitudinalRadius;
            normalizedDistance = Mathf.Sqrt(
                delta.x * delta.x / (EndLateralRadius * EndLateralRadius) +
                delta.y * delta.y / (EndVerticalRadius * EndVerticalRadius) +
                delta.z * delta.z / (longitudinalRadius * longitudinalRadius));
        }
        else
        {
            normalizedDistance = delta.magnitude / SideRadius;
        }

        if (normalizedDistance >= 1f)
            return 0f;
        var falloff = 1f - normalizedDistance;
        return falloff * falloff;
    }

    private static Vector3 CalculateDeformedVehiclePoint(
        Vector3 currentVehicle,
        Vector3 originalVehicle,
        Vector3 direction,
        float impactDepth,
        float influence)
    {
        var requestedVehicle = currentVehicle + direction * (impactDepth * influence);
        var displacement = Vector3.ClampMagnitude(requestedVehicle - originalVehicle, MaximumDisplacement);
        return originalVehicle + displacement;
    }

    private void SetSafeBounds(Surface surface)
    {
        surface.RuntimeMesh.bounds = surface.SafeMeshBounds;
        if (surface.Renderer is SkinnedMeshRenderer skinned)
            skinned.localBounds = surface.SafeRendererBounds;
    }

    private void HandleVehicleVariablesChanged()
    {
        if (!initialized || damageHandler == null || vehicle == null)
            return;

        var currentDamage = ReadCurrentDamage();
        if ((previousDamage > RepairedDamageThreshold || acceptedImpacts.Count > 0) &&
            currentDamage <= RepairedDamageThreshold)
        {
            pending.Clear();
            acceptedImpacts.Clear();
            if (savedRecordWorker != null)
                StopCoroutine(savedRecordWorker);
            savedRecordWorker = null;
            if (deformationWorker != null)
                StopCoroutine(deformationWorker);
            deformationWorker = null;
            if (repairWorker != null)
                StopCoroutine(repairWorker);
            repairWorker = StartCoroutine(RestoreAfterNativeRepair());
        }
        previousDamage = currentDamage;
    }

    private float ReadCurrentDamage()
    {
        var handlerDamage = damageHandler != null ? damageHandler.Damage : 0f;
        var instanceDamage = vehicle?.vehicleInstance?.damage ?? 0f;
        return Mathf.Max(handlerDamage, instanceDamage);
    }

    private IEnumerator RestoreAfterNativeRepair()
    {
        // Native repair may reset its renderer bindings later in this frame.
        yield return null;
        foreach (var surface in surfaces)
        {
            if (surface.Renderer == null || surface.RuntimeMesh == null)
                continue;

            Array.Copy(surface.OriginalVertices, surface.WorkingVertices, surface.OriginalVertices.Length);
            surface.RuntimeMesh.vertices = surface.WorkingVertices;
            if (surface.OriginalNormals.Length == surface.WorkingVertices.Length)
                surface.RuntimeMesh.normals = surface.OriginalNormals;
            if (surface.OriginalTangents.Length == surface.WorkingVertices.Length)
                surface.RuntimeMesh.tangents = surface.OriginalTangents;
            surface.RuntimeMesh.bounds = surface.SourceMeshBounds;
            SetSharedMesh(surface.Renderer, surface.RuntimeMesh);
            if (surface.Renderer is SkinnedMeshRenderer skinned)
                skinned.localBounds = surface.SourceRendererBounds;
        }

        var records = vehicle?.vehicleInstance?.deformations;
        records?.Clear();
        repairWorker = null;
        DamageInfo($"BentleyContinentalGTSpeed repair restored exact body mesh baselines vehicle={vehicle?.GetInstanceID()}.");
    }

    private void DamageInfo(string message)
    {
        if (BentleyContinentalGTSpeedDiagnostics.DebugEnabled && DamageDebugEnabled)
            context?.Logger.Info(message);
    }

    private void OnDestroy() => Cleanup();
}
