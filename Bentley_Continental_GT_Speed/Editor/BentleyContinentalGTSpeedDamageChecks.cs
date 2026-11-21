#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Prefab checks for Bentley's per-instance exterior damage selector.</summary>
public static class BentleyContinentalGTSpeedDamageChecks
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private const float EndRegionMinimum = 1.65f;

    public static void Verify(GameObject prefab)
    {
        if (prefab == null)
            throw new ArgumentNullException(nameof(prefab));

        var runtimeType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("BentleyContinentalGTSpeedDamageController", false))
            .FirstOrDefault(type => type != null);
        if (runtimeType == null)
            throw new InvalidOperationException("Bentley damage controller type is unavailable to the Editor checks.");

        var excludedByName = RequireMethod(runtimeType, "IsExcludedByName", typeof(Renderer));
        var selector = RequireMethod(runtimeType, "TryGetDeformableExteriorVertices", typeof(Renderer), typeof(Mesh), typeof(int[]).MakeByRefType());
        var buildLampVertexMask = RequireMethod(
            runtimeType, "TryBuildLampGlassVertexPositions", typeof(Renderer), typeof(Mesh), typeof(Transform), typeof(Vector3[]).MakeByRefType());
        var influence = RequireMethod(runtimeType, "Influence", typeof(Vector3), runtimeType.GetNestedType("Contact", BindingFlags.NonPublic)!);
        var inwardDirection = RequireMethod(runtimeType, "InwardDirection", runtimeType.GetNestedType("Contact", BindingFlags.NonPublic)!);
        var deformPoint = RequireMethod(
            runtimeType, "CalculateDeformedVehiclePoint", typeof(Vector3), typeof(Vector3), typeof(Vector3), typeof(float), typeof(float));
        var frontZ = FindWheelZ(prefab.transform, "FrontLeft_WheelController", "FrontRight_WheelController");
        var rearZ = FindWheelZ(prefab.transform, "RearLeft_WheelController", "RearRight_WheelController");
        if (Mathf.Abs(frontZ - rearZ) < 0.25f)
            throw new InvalidOperationException("Bentley damage checks cannot distinguish front and rear wheel anchors.");
        var frontSign = Mathf.Sign(frontZ - rearZ);

        var selectedPaintVertices = 0;
        var selectedFrontTrimVertices = 0;
        var selectedRearTrimVertices = 0;
        var selectedSideTrimVertices = 0;
        var selectedFrontMaterials = new HashSet<string>(StringComparer.Ordinal);
        var selectedRearMaterials = new HashSet<string>(StringComparer.Ordinal);
        var selectedLampGlassVertices = 0;
        var rearLampGlassFound = false;
        var mixedGlassSelectedVertices = 0;
        var mixedGlassProtectedVertices = 0;
        var mixedGlassFrontVertices = 0;
        var mixedGlassRearVertices = 0;
        var excludedWheels = 0;
        var excludedBrakes = 0;
        var protectedInterior = 0;
        var protectedCabinGlass = 0;

        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;
            var mesh = GetSharedMesh(renderer);
            if (mesh == null)
                continue;

            var wasExcluded = (bool)excludedByName.Invoke(null, new object[] { renderer })!;
            var materialNames = renderer.sharedMaterials
                .Where(material => material != null)
                .Select(material => NormalizeName(material!.name))
                .ToArray();
            var verifiedTrunkBacking = IsVerifiedTrunkBacking(renderer, mesh, materialNames);
            var hasGlassMaterial = materialNames.Any(IsGlassMaterial);
            var lampVertexPositions = Array.Empty<Vector3>();
            if (hasGlassMaterial)
            {
                var nativeVehicle = renderer.GetComponentInParent<VehicleController>(true);
                var vehicleRoot = nativeVehicle != null ? nativeVehicle.transform : prefab.transform;
                var vertexArguments = new object?[] { renderer, mesh, vehicleRoot, null };
                if (!(bool)buildLampVertexMask.Invoke(null, vertexArguments)!)
                    throw new InvalidOperationException("Bentley damage could not map glass mesh vertices to vehicle space: " + renderer.name);
                lampVertexPositions = vertexArguments[3] as Vector3[] ?? Array.Empty<Vector3>();
            }
            var isExteriorLampLens = lampVertexPositions.Any(IsLampGlassVertex);
            var protection = GetProtectionKind(renderer.transform);
            var isInterior = !verifiedTrunkBacking &&
                             (protection == "interior" || materialNames.Any(IsInteriorMaterial));
            var hasProtectedGlassVertices = hasGlassMaterial && lampVertexPositions.Any(vertex => !IsLampGlassVertex(vertex));
            var isCabinGlass = protection == "glass" || hasProtectedGlassVertices;
            var isBrakeMaterial = materialNames.Any(name => ContainsAny(name, "caliper", "calliper", "brake"));
            if ((protection == "brake" || isBrakeMaterial) && wasExcluded)
                excludedBrakes++;
            else if (protection == "wheel" && wasExcluded)
                excludedWheels++;
            else if (isInterior)
                protectedInterior++;
            else if (isCabinGlass)
                protectedCabinGlass++;

            if (wasExcluded)
                continue;

            var output = new object?[] { renderer, mesh, null };
            if (!(bool)selector.Invoke(null, output)!)
                continue;
            var eligible = output[2] as int[] ?? Array.Empty<int>();
            if (eligible.Length == 0)
                throw new InvalidOperationException("Bentley damage selector returned an empty vertex set for " + renderer.name);

            var materialList = string.Join(",", materialNames);
            if (hasGlassMaterial)
            {
                for (var vertexIndex = 0; vertexIndex < eligible.Length; vertexIndex++)
                {
                    var index = eligible[vertexIndex];
                    if (index < 0 || index >= lampVertexPositions.Length || !IsLampGlassVertex(lampVertexPositions[index]))
                        throw new InvalidOperationException(
                            "Bentley damage selected a cabin-region glass vertex: " + renderer.name +
                            " / " + materialList + " / vertex=" + index);
                }

                var eligibleSet = new HashSet<int>(eligible);
                var glassMaterials = renderer.sharedMaterials;
                for (var submesh = 0; submesh < mesh.subMeshCount && submesh < glassMaterials.Length; submesh++)
                {
                    if (glassMaterials[submesh] == null || !IsGlassMaterial(NormalizeName(glassMaterials[submesh]!.name)))
                        continue;
                    var triangles = mesh.GetTriangles(submesh);
                    for (var triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
                    {
                        var first = triangles[triangle];
                        var second = triangles[triangle + 1];
                        var third = triangles[triangle + 2];
                        var allLamp = IsLampGlassVertex(lampVertexPositions[first]) &&
                                      IsLampGlassVertex(lampVertexPositions[second]) &&
                                      IsLampGlassVertex(lampVertexPositions[third]);
                        if (allLamp)
                            continue;
                        if (eligibleSet.Contains(first) || eligibleSet.Contains(second) || eligibleSet.Contains(third))
                            throw new InvalidOperationException(
                                "Bentley damage selected a vertex shared with protected cabin glass: " +
                                renderer.name + " / " + materialList);
                    }
                }

                if (string.Equals(renderer.name, "Object_51", StringComparison.Ordinal))
                {
                    mixedGlassSelectedVertices += eligible.Length;
                    mixedGlassProtectedVertices += lampVertexPositions.Count(vertex => !IsLampGlassVertex(vertex));
                    foreach (var index in eligible)
                    {
                        if (lampVertexPositions[index].z * frontSign > 0f)
                            mixedGlassFrontVertices++;
                        else
                            mixedGlassRearVertices++;
                    }
                }
            }

            if (materialNames.Any(IsBodyPaintMaterial))
            {
                selectedPaintVertices += eligible.Length;
            }
            else if (isExteriorLampLens)
            {
                selectedLampGlassVertices += eligible.Length;
                if (string.Equals(renderer.name, "Object_234", StringComparison.Ordinal) &&
                    materialNames.Contains("Bentley_GlassMtl_Prepared", StringComparer.Ordinal))
                    rearLampGlassFound = true;
            }
            else
            {
                var nativeVehicle = renderer.GetComponentInParent<VehicleController>(true);
                var vehicleRoot = nativeVehicle != null ? nativeVehicle.transform : prefab.transform;
                var arguments = new object?[] { renderer, mesh, vehicleRoot, null };
                if (!(bool)buildLampVertexMask.Invoke(null, arguments)!)
                    throw new InvalidOperationException("Bentley trim vertex mapping failed: " + renderer.name);
                var positions = arguments[3] as Vector3[] ?? Array.Empty<Vector3>();
                foreach (var index in eligible)
                {
                    var point = positions[index];
                    if (Mathf.Abs(point.z) < EndRegionMinimum || Mathf.Abs(point.z) <= Mathf.Abs(point.x))
                    {
                        selectedSideTrimVertices++;
                        continue;
                    }
                    if (point.z * frontSign > 0f)
                    {
                        selectedFrontTrimVertices++;
                        selectedFrontMaterials.Add(materialList);
                    }
                    else
                    {
                        selectedRearTrimVertices++;
                        selectedRearMaterials.Add(materialList);
                    }
                }
            }

            if (protection == "wheel" || protection == "brake" || isInterior ||
                (isCabinGlass && !hasGlassMaterial))
                throw new InvalidOperationException(
                    "Bentley damage selected protected " +
                    (isInterior ? "interior" : isCabinGlass ? "cabin glass" : protection) + " geometry: " +
                    renderer.name + " / " + materialList);
        }

        if (selectedPaintVertices == 0)
            throw new InvalidOperationException("Bentley damage selector lost all Paint/OEMPaint vertices.");
        if (selectedFrontTrimVertices == 0 || selectedRearTrimVertices == 0)
            throw new InvalidOperationException(
                "Bentley damage must cover non-paint exterior geometry at both ends; " +
                $"front={selectedFrontTrimVertices}, rear={selectedRearTrimVertices}.");
        if (!rearLampGlassFound || selectedLampGlassVertices == 0)
            throw new InvalidOperationException(
                "Bentley rear lamp GlassMtl mesh Object_234 was not admitted by its narrow end-geometry selector.");
        if (mixedGlassSelectedVertices == 0 || mixedGlassProtectedVertices == 0 ||
            mixedGlassFrontVertices == 0 || mixedGlassRearVertices == 0)
            throw new InvalidOperationException(
                "Bentley mixed GlassMtl mesh Object_51 must contain eligible front/rear lamp vertices and protected cabin vertices; " +
                $"front={mixedGlassFrontVertices}, rear={mixedGlassRearVertices}, protected={mixedGlassProtectedVertices}.");
        if (excludedWheels == 0 || excludedBrakes == 0 || protectedInterior == 0 || protectedCabinGlass == 0)
            throw new InvalidOperationException(
                "Bentley damage checks did not find all protected prefab categories; " +
                $"wheels={excludedWheels}, brakes={excludedBrakes}, interior={protectedInterior}, cabinGlass={protectedCabinGlass}.");

        LogRearRendererInventory(prefab, excludedByName, selector, buildLampVertexMask);
        VerifyProtectedRearCabinGlass(prefab, selector, buildLampVertexMask);
        VerifyTrunkBackingRig(prefab);
        BentleyContinentalGTSpeedEngineGeometrySetup.VerifyDisabled(prefab);
        VerifyRearPartImpactResponse(
            runtimeType, selector, buildLampVertexMask, excludedByName, influence, inwardDirection, deformPoint, prefab);
        VerifyRuntimeRearImpact(runtimeType, prefab);
        VerifyInstanceMeshClone(runtimeType, selector, prefab);
        Debug.Log(
            "Bentley damage coverage: " +
            $"paintVertices={selectedPaintVertices}, frontTrimVertices={selectedFrontTrimVertices} " +
            $"materials=[{string.Join(";", selectedFrontMaterials.OrderBy(value => value, StringComparer.Ordinal))}], " +
            $"rearTrimVertices={selectedRearTrimVertices} " +
            $"materials=[{string.Join(";", selectedRearMaterials.OrderBy(value => value, StringComparer.Ordinal))}], " +
            $"sideTrimVertices={selectedSideTrimVertices}, " +
            $"lampGlassVertices={selectedLampGlassVertices}, Object_51(front={mixedGlassFrontVertices}, " +
            $"rear={mixedGlassRearVertices}, protected={mixedGlassProtectedVertices}); excluded wheels={excludedWheels}, " +
            $"brakes={excludedBrakes}, interior={protectedInterior}, cabinGlass={protectedCabinGlass}.");
    }

    private static void LogRearRendererInventory(
        GameObject prefab,
        MethodInfo excludedByName,
        MethodInfo selector,
        MethodInfo buildVertexPositions)
    {
        var vehicle = prefab.GetComponentInChildren<VehicleController>(true);
        var vehicleRoot = vehicle != null ? vehicle.transform : prefab.transform;
        var entries = new List<string>();
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;
            var mesh = GetSharedMesh(renderer);
            if (mesh == null || mesh.vertexCount == 0)
                continue;
            var positionArguments = new object?[] { renderer, mesh, vehicleRoot, null };
            if (!(bool)buildVertexPositions.Invoke(null, positionArguments)!)
                continue;
            var positions = positionArguments[3] as Vector3[] ?? Array.Empty<Vector3>();
            var rearUpper = positions.Where(point => point.z < -1.4f && point.y > .5f).ToArray();
            if (rearUpper.Length == 0)
                continue;

            var isExcluded = (bool)excludedByName.Invoke(null, new object[] { renderer })!;
            var selectionArguments = new object?[] { renderer, mesh, null };
            var selected = !isExcluded && (bool)selector.Invoke(null, selectionArguments)!;
            var eligible = selected ? selectionArguments[2] as int[] ?? Array.Empty<int>() : Array.Empty<int>();
            var materials = string.Join(",", renderer.sharedMaterials
                .Where(material => material != null)
                .Select(material => NormalizeName(material!.name)));
            var minX = rearUpper.Min(point => point.x);
            var maxX = rearUpper.Max(point => point.x);
            var minY = rearUpper.Min(point => point.y);
            var maxY = rearUpper.Max(point => point.y);
            var minZ = rearUpper.Min(point => point.z);
            var maxZ = rearUpper.Max(point => point.z);
            entries.Add(
                $"{GetTransformPath(renderer.transform)} mesh={mesh.name} material=[{materials}] " +
                $"upperRearVerts={rearUpper.Length} bounds=({minX:0.00}..{maxX:0.00},{minY:0.00}..{maxY:0.00},{minZ:0.00}..{maxZ:0.00}) " +
                $"excluded={isExcluded} eligible={eligible.Length}/{mesh.vertexCount}");
        }
        Debug.Log("Bentley rear renderer inventory: " + string.Join("; ", entries.OrderBy(value => value, StringComparer.Ordinal)));
    }

    private static string GetTransformPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var node = transform; node != null; node = node.parent)
            names.Push(node.name);
        return string.Join("/", names);
    }

    private static bool IsVerifiedTrunkBacking(Renderer renderer, Mesh mesh, string[] materials)
    {
        if (!string.Equals(renderer.name, "Object_238", StringComparison.Ordinal) ||
            !materials.Contains("Bentley_InteriorA_Prepared", StringComparer.Ordinal) || mesh.vertexCount != 2473)
            return false;
        var bounds = mesh.bounds;
        return Mathf.Abs(bounds.center.y - .8663f) <= .03f &&
               Mathf.Abs(bounds.center.z + 1.9173f) <= .03f &&
               Mathf.Abs(bounds.extents.x - .6014f) <= .03f &&
               Mathf.Abs(bounds.extents.y - .2082f) <= .03f &&
               Mathf.Abs(bounds.extents.z - .3747f) <= .03f;
    }

    private static void VerifyTrunkBackingRig(GameObject prefab)
    {
        var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var backing = renderers.SingleOrDefault(renderer => renderer != null && renderer.name == "Object_238") ??
                      throw new InvalidOperationException("Bentley trunk inner backing Object_238 is missing or duplicated.");
        var outer = renderers.SingleOrDefault(renderer => renderer != null && renderer.name == "Object_242") ??
                    throw new InvalidOperationException("Bentley outer trunk paint Object_242 is missing or duplicated.");
        var backingMesh = backing.sharedMesh ?? throw new InvalidOperationException("Bentley trunk inner backing has no mesh.");
        var outerMesh = outer.sharedMesh ?? throw new InvalidOperationException("Bentley outer trunk paint has no mesh.");
        var backingMaterials = backing.sharedMaterials.Where(material => material != null)
            .Select(material => NormalizeName(material!.name)).ToArray();
        if (!IsVerifiedTrunkBacking(backing, backingMesh, backingMaterials))
            throw new InvalidOperationException("Bentley trunk inner backing did not match the exact Object_238 source mesh/material/bounds allowlist.");

        if (backing.bones.Length != outer.bones.Length || backingMesh.bindposes.Length != outerMesh.bindposes.Length ||
            backingMesh.boneWeights.Length != backingMesh.vertexCount || outerMesh.boneWeights.Length != outerMesh.vertexCount)
            throw new InvalidOperationException("Bentley inner/outer trunk panels have incompatible skinning data.");
        for (var index = 0; index < backing.bones.Length; index++)
        {
            if (backing.bones[index] != outer.bones[index])
                throw new InvalidOperationException($"Bentley inner/outer trunk panel bone mismatch at index {index}.");
        }
        for (var index = 0; index < backingMesh.bindposes.Length; index++)
        {
            for (var element = 0; element < 16; element++)
            {
                if (Mathf.Abs(backingMesh.bindposes[index][element] - outerMesh.bindposes[index][element]) > .0001f)
                    throw new InvalidOperationException($"Bentley inner/outer trunk panel bindpose mismatch at index {index}.");
            }
        }
        var backingWeightedBones = ValidateSkinWeights("Object_238", backingMesh.boneWeights, backing.bones.Length);
        var outerWeightedBones = ValidateSkinWeights("Object_242", outerMesh.boneWeights, outer.bones.Length);

        Debug.Log(
            $"Bentley trunk inner backing rig verified: inner={backing.name}/{backingMesh.name} " +
            $"({backingMesh.vertexCount} vertices, bones={backing.bones.Length}, bindposes={backingMesh.bindposes.Length}), " +
            $"weightedBones=[{string.Join(",", backingWeightedBones)}], outer={outer.name}/{outerMesh.name} " +
            $"(bones={outer.bones.Length}, bindposes={outerMesh.bindposes.Length}, " +
            $"weightedBones=[{string.Join(",", outerWeightedBones)}]); full bone ordering and bindpose matrices match.");
    }

    private static void VerifyProtectedRearCabinGlass(
        GameObject prefab,
        MethodInfo selector,
        MethodInfo buildVertexPositions)
    {
        var matches = prefab.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer != null && string.Equals(renderer.name, "Object_55", StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException($"Bentley cabin inside-glass Object_55 must be unique; found={matches.Length}.");
        var renderer = matches[0];
        var mesh = GetSharedMesh(renderer) ??
                   throw new InvalidOperationException("Bentley cabin inside-glass Object_55 has no mesh.");
        var materialNames = renderer.sharedMaterials.Where(material => material != null)
            .Select(material => NormalizeName(material!.name)).ToArray();
        if (!string.Equals(materialNames.SingleOrDefault(), "Bentley_GlassMtl_Prepared", StringComparison.Ordinal) ||
            mesh.vertexCount != 370)
            throw new InvalidOperationException("Bentley Object_55 no longer matches its exact ChassisGlassInside_62 source mesh/material.");

        var selectionArguments = new object?[] { renderer, mesh, null };
        if ((bool)selector.Invoke(null, selectionArguments)! ||
            (selectionArguments[2] as int[] ?? Array.Empty<int>()).Length != 0)
            throw new InvalidOperationException("Bentley cabin inside-glass Object_55 must have zero damage-eligible vertices.");
        var vehicle = renderer.GetComponentInParent<VehicleController>(true);
        var vehicleRoot = vehicle != null ? vehicle.transform : prefab.transform;
        var positionsArguments = new object?[] { renderer, mesh, vehicleRoot, null };
        if (!(bool)buildVertexPositions.Invoke(null, positionsArguments)!)
            throw new InvalidOperationException("Bentley Object_55 source vertices could not be mapped to vehicle coordinates.");
        var positions = positionsArguments[3] as Vector3[] ?? Array.Empty<Vector3>();
        var rearGlassVertices = positions.Count(point => point.z < -1.4f && point.y > .5f);
        var protectedGlassVertices = positions.Count(point => Mathf.Abs(point.z) < 1.65f || point.y > 1.15f);
        if (rearGlassVertices == 0 || protectedGlassVertices == 0)
            throw new InvalidOperationException(
                $"Bentley Object_55 cabin-glass classification lost its rear/lower-rear window evidence; " +
                $"rear={rearGlassVertices}, protected={protectedGlassVertices}.");
        Debug.Log(
            $"Bentley cabin inside-glass protection verified: Object_55/{mesh.name}, material={materialNames[0]}, " +
            $"eligible=0/{mesh.vertexCount}, rearGlassVertices={rearGlassVertices}, protectedGlassVertices={protectedGlassVertices}.");
    }

    private static int[] ValidateSkinWeights(string rendererName, BoneWeight[] weights, int boneCount)
    {
        var usedBones = new HashSet<int>();
        foreach (var weight in weights)
        {
            var sum = weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
            if (sum < .99f || sum > 1.01f)
                throw new InvalidOperationException($"Bentley {rendererName} contains an invalid skin weight sum {sum:0.000}.");
            Add(weight.boneIndex0, weight.weight0);
            Add(weight.boneIndex1, weight.weight1);
            Add(weight.boneIndex2, weight.weight2);
            Add(weight.boneIndex3, weight.weight3);

            void Add(int boneIndex, float value)
            {
                if (value <= .0001f)
                    return;
                if (boneIndex < 0 || boneIndex >= boneCount)
                    throw new InvalidOperationException(
                        $"Bentley {rendererName} references skin bone {boneIndex} outside its {boneCount}-bone palette.");
                usedBones.Add(boneIndex);
            }
        }
        return usedBones.OrderBy(index => index).ToArray();
    }

    private static void VerifyRearPartImpactResponse(
        Type runtimeType,
        MethodInfo selector,
        MethodInfo buildLampVertexPositions,
        MethodInfo excludedByName,
        MethodInfo influence,
        MethodInfo inwardDirection,
        MethodInfo deformPoint,
        GameObject prefab)
    {
        var targets = new[]
        {
            (Name: "Object_238", Material: "Bentley_InteriorA_Prepared", Label: "trunk inner backing"),
            (Name: "Object_242", Material: "Bentley_Paint_Prepared", Label: "bootlid"),
            (Name: "Object_240", Material: "Bentley_LightA_Prepared", Label: "rear light detail"),
            (Name: "Object_234", Material: "Bentley_GlassMtl_Prepared", Label: "rear lamp lens"),
            (Name: "Object_53", Material: "Bentley_GlassRed_Prepared", Label: "rear red lamp detail"),
        };
        var contactType = runtimeType.GetNestedType("Contact", BindingFlags.NonPublic) ??
                          throw new InvalidOperationException("Bentley damage contact type is unavailable to the Editor checks.");
        var contactConstructor = contactType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(Vector3), typeof(Vector3) }, null) ??
                                throw new InvalidOperationException("Bentley damage contact constructor is unavailable to the Editor checks.");
        var vehicle = prefab.GetComponentInChildren<VehicleController>(true);
        var vehicleRoot = vehicle != null ? vehicle.transform : prefab.transform;
        var bootlid = prefab.GetComponentsInChildren<Renderer>(true)
            .SingleOrDefault(renderer => renderer != null && string.Equals(renderer.name, "Object_242", StringComparison.Ordinal)) ??
                       throw new InvalidOperationException("Bentley bootlid renderer Object_242 is missing.");
        var bootlidMesh = GetSharedMesh(bootlid) ??
                          throw new InvalidOperationException("Bentley bootlid renderer Object_242 has no mesh.");
        var bootlidSelection = new object?[] { bootlid, bootlidMesh, null };
        if (!(bool)selector.Invoke(null, bootlidSelection)!)
            throw new InvalidOperationException("Bentley bootlid renderer Object_242 was not selected for damage.");
        var bootlidPositions = new object?[] { bootlid, bootlidMesh, vehicleRoot, null };
        if (!(bool)buildLampVertexPositions.Invoke(null, bootlidPositions)!)
            throw new InvalidOperationException("Bentley bootlid vertices could not be mapped to vehicle space.");
        var bootlidVehicleVertices = bootlidPositions[3] as Vector3[] ?? Array.Empty<Vector3>();
        var bootlidRearZ = (bootlidSelection[2] as int[] ?? Array.Empty<int>())
            .Select(index => bootlidVehicleVertices[index].z)
            .DefaultIfEmpty(float.PositiveInfinity)
            .Min();
        if (float.IsInfinity(bootlidRearZ))
            throw new InvalidOperationException("Bentley bootlid has no eligible source vertices for the rear impact probe.");
        var rearContactPoints = new[] { 0f, -.55f, .55f }
            .Select(x => new Vector3(x, .8f, bootlidRearZ - .03f))
            .ToArray();
        var rearContacts = rearContactPoints
            .Select(point => contactConstructor.Invoke(new object[] { point, Vector3.back }))
            .ToArray();
        var summary = new List<string>();

        foreach (var target in targets)
        {
            var matches = prefab.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null && string.Equals(renderer.name, target.Name, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException(
                    $"Bentley rear {target.Label} renderer '{target.Name}' must be unique; found={matches.Length}.");

            var renderer = matches[0];
            var materials = renderer.sharedMaterials
                .Where(material => material != null)
                .Select(material => NormalizeName(material!.name))
                .ToArray();
            if (!materials.Contains(target.Material, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Bentley rear {target.Label} renderer '{target.Name}' lost expected material '{target.Material}'; " +
                    $"actual=[{string.Join(",", materials)}].");
            if ((bool)excludedByName.Invoke(null, new object[] { renderer })!)
                throw new InvalidOperationException("Bentley rear damage target was excluded by name: " + target.Name);

            var mesh = GetSharedMesh(renderer);
            if (mesh == null)
                throw new InvalidOperationException("Bentley rear damage target has no mesh: " + target.Name);
            var selectionArguments = new object?[] { renderer, mesh, null };
            if (!(bool)selector.Invoke(null, selectionArguments)!)
                throw new InvalidOperationException("Bentley rear damage target was not selected: " + target.Name);
            var eligible = selectionArguments[2] as int[] ?? Array.Empty<int>();
            var positionsArguments = new object?[] { renderer, mesh, vehicleRoot, null };
            if (!(bool)buildLampVertexPositions.Invoke(null, positionsArguments)!)
                throw new InvalidOperationException("Bentley rear damage target positions could not be mapped: " + target.Name);
            var vehiclePositions = positionsArguments[3] as Vector3[] ?? Array.Empty<Vector3>();

            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            foreach (var index in eligible)
            {
                if (index < 0 || index >= vehiclePositions.Length)
                    throw new InvalidOperationException("Bentley rear damage target returned an invalid vertex: " + target.Name);
                min = Vector3.Min(min, vehiclePositions[index]);
            }
            if (eligible.Length == 0 || min.z > -1.65f)
                throw new InvalidOperationException(
                    $"Bentley rear {target.Label} has no selected rear-end vertices; " +
                    $"eligible={eligible.Length}, zMin={min.z:0.000}.");

            var positiveInfluence = 0;
            var displaced = 0;
            const float probeDepth = .12f;
            foreach (var index in eligible)
            {
                var strongest = 0f;
                var direction = Vector3.zero;
                foreach (var contact in rearContacts)
                {
                    var amount = (float)influence.Invoke(null, new object[] { vehiclePositions[index], contact })!;
                    if (amount <= strongest)
                        continue;
                    strongest = amount;
                    direction = (Vector3)inwardDirection.Invoke(null, new[] { contact })!;
                }
                if (strongest <= 0f || direction.sqrMagnitude < .5f || direction.z <= 0f)
                    continue;
                positiveInfluence++;
                var deformed = (Vector3)deformPoint.Invoke(
                    null, new object[] { vehiclePositions[index], vehiclePositions[index], direction, probeDepth, strongest })!;
                if ((deformed - vehiclePositions[index]).sqrMagnitude > .00000001f)
                    displaced++;
            }
            if (positiveInfluence == 0 || displaced == 0)
                throw new InvalidOperationException(
                    $"Bentley rear {target.Label} does not respond to a rear contact; " +
                    $"renderer={target.Name}, eligible={eligible.Length}, influenced={positiveInfluence}, displaced={displaced}, " +
                    $"contacts=[{string.Join(";", rearContactPoints)}].");
            summary.Add(
                $"{target.Label}/{target.Name} selected={eligible.Length}, influenced={positiveInfluence}, displaced={displaced}");
        }

        Debug.Log(
            "Bentley rear impact response using bootlid-height center/corner contacts " +
            $"[{string.Join(";", rearContactPoints)}]: " + string.Join("; ", summary) + ".");
    }

    private static void VerifyRuntimeRearImpact(Type runtimeType, GameObject prefab)
    {
        var clone = UnityEngine.Object.Instantiate(prefab);
        clone.name = prefab.name + "_BentleyDamageCheck";
        object? damageController = null;
        try
        {
            var vehicle = clone.GetComponentInChildren<VehicleController>(true) ??
                          throw new InvalidOperationException("Bentley runtime damage check could not clone its VehicleController.");
            damageController = vehicle.gameObject.AddComponent(runtimeType);
            var initialize = runtimeType.GetMethod(
                "Initialize", BindingFlags.Instance | BindingFlags.NonPublic) ??
                             throw new InvalidOperationException("Bentley runtime damage initializer is unavailable to Editor checks.");
            if (!(bool)initialize.Invoke(damageController, new object?[] { vehicle, null })!)
                throw new InvalidOperationException("Bentley runtime damage initialization failed on the temporary prefab instance.");

            var surfacesField = RequireInstanceField(runtimeType, "surfaces");
            var surfaces = surfacesField.GetValue(damageController) as System.Collections.IList ??
                           throw new InvalidOperationException("Bentley runtime damage surface registry is unavailable to Editor checks.");
            var targets = new HashSet<string>(new[]
            {
                "Object_238", "Object_242", "Object_240", "Object_234", "Object_53", "Object_86", "Object_51", "BentleyLampEmitters",
            }, StringComparer.Ordinal);
            var targetSurfaces = surfaces.Cast<object>()
                .Where(surface =>
                {
                    var renderer = GetSurfaceField(surface, "Renderer") as Renderer;
                    return renderer != null && targets.Contains(renderer.name);
                })
                .ToArray();
            if (targetSurfaces.Length != targets.Count)
            {
                var foundNames = targetSurfaces
                    .Select(surface => (GetSurfaceField(surface, "Renderer") as Renderer)?.name ?? "<missing renderer>");
                throw new InvalidOperationException(
                    "Bentley runtime damage initialization did not register every rear target surface; " +
                    $"expected={string.Join(",", targets.OrderBy(name => name, StringComparer.Ordinal))}, " +
                    $"found={string.Join(",", foundNames)}.");
            }

            var mixedCabin = clone.GetComponentsInChildren<Renderer>(true)
                .SingleOrDefault(renderer => renderer != null && string.Equals(renderer.name, "Object_76", StringComparison.Ordinal)) ??
                               throw new InvalidOperationException("Bentley mixed cabin renderer Object_76 is missing or duplicated.");
            var cabinSourceMesh = GetSharedMesh(mixedCabin) ??
                                  throw new InvalidOperationException("Bentley mixed cabin renderer Object_76 has no mesh.");
            var cabinSelection = new object?[] { mixedCabin, cabinSourceMesh, null };
            var selectedCabin = (bool)runtimeType.GetMethod(
                "TryGetDeformableExteriorVertices", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, cabinSelection)!;
            if (selectedCabin || surfaces.Cast<object>().Any(surface =>
                    string.Equals((GetSurfaceField(surface, "Renderer") as Renderer)?.name, "Object_76", StringComparison.Ordinal)))
                throw new InvalidOperationException("Bentley mixed cabin renderer Object_76 must remain outside the damage surface registry.");

            var rearCabinGlass = clone.GetComponentsInChildren<Renderer>(true)
                .SingleOrDefault(renderer => renderer != null && string.Equals(renderer.name, "Object_55", StringComparison.Ordinal)) ??
                                  throw new InvalidOperationException("Bentley inside-glass renderer Object_55 is missing or duplicated.");
            var rearCabinGlassMesh = GetSharedMesh(rearCabinGlass) ??
                                     throw new InvalidOperationException("Bentley inside-glass renderer Object_55 has no mesh.");
            var rearCabinGlassSelection = new object?[] { rearCabinGlass, rearCabinGlassMesh, null };
            if ((bool)runtimeType.GetMethod(
                    "TryGetDeformableExteriorVertices", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, rearCabinGlassSelection)! ||
                (rearCabinGlassSelection[2] as int[] ?? Array.Empty<int>()).Length != 0 ||
                surfaces.Cast<object>().Any(surface =>
                    string.Equals((GetSurfaceField(surface, "Renderer") as Renderer)?.name, "Object_55", StringComparison.Ordinal)))
                throw new InvalidOperationException("Bentley inside-glass Object_55 must have zero eligible vertices and stay outside the damage registry.");

            var buildBoneMatrices = runtimeType.GetMethod("BuildBoneMatrices", BindingFlags.Instance | BindingFlags.NonPublic) ??
                                    throw new InvalidOperationException("Bentley runtime bone matrix builder is unavailable to Editor checks.");
            var surfaceType = runtimeType.GetNestedType("Surface", BindingFlags.NonPublic) ??
                              throw new InvalidOperationException("Bentley runtime surface type is unavailable to Editor checks.");
            var getVertexToWorld = runtimeType.GetMethod(
                "GetVertexToWorld", BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { surfaceType, typeof(int), typeof(Matrix4x4[]) }, null) ??
                                   throw new InvalidOperationException("Bentley runtime vertex transform is unavailable to Editor checks.");
            var summaries = new List<string>();
            foreach (var surface in targetSurfaces)
            {
                var renderer = GetSurfaceField(surface, "Renderer") as Renderer ??
                               throw new InvalidOperationException("Bentley runtime rear surface lost its renderer.");
                var sourceMesh = GetSurfaceField(surface, "SourceMesh") as Mesh ??
                                 throw new InvalidOperationException("Bentley runtime rear surface lost its source mesh: " + renderer.name);
                var eligible = GetSurfaceField(surface, "EligibleIndices") as int[] ?? Array.Empty<int>();
                var boneMatrices = buildBoneMatrices.Invoke(damageController, new[] { surface }) as Matrix4x4[] ??
                                   Array.Empty<Matrix4x4>();
                var minimumDeterminant = float.PositiveInfinity;
                var maximumDeterminant = 0f;
                var rejectedDeterminants = 0;
                foreach (var index in eligible)
                {
                    var matrix = (Matrix4x4)getVertexToWorld.Invoke(null, new object[] { surface, index, boneMatrices })!;
                    var determinant = Mathf.Abs(matrix.determinant);
                    minimumDeterminant = Mathf.Min(minimumDeterminant, determinant);
                    maximumDeterminant = Mathf.Max(maximumDeterminant, determinant);
                    if (determinant < .0000001f)
                        rejectedDeterminants++;
                }
                summaries.Add(
                    $"{renderer.name}/{sourceMesh.name} eligible={eligible.Length}, " +
                    $"|det|=[{minimumDeterminant:0.000000000},{maximumDeterminant:0.000000000}], " +
                    $"runtimeSkippedByDeterminant={rejectedDeterminants}");
            }

            var bootlidSurface = targetSurfaces.Single(surface =>
                string.Equals((GetSurfaceField(surface, "Renderer") as Renderer)?.name, "Object_242", StringComparison.Ordinal));
            var bootlidIndices = GetSurfaceField(bootlidSurface, "EligibleIndices") as int[] ?? Array.Empty<int>();
            var bootlidPositions = new object?[]
            {
                GetSurfaceField(bootlidSurface, "Renderer"),
                GetSurfaceField(bootlidSurface, "SourceMesh"),
                vehicle.transform,
                null,
            };
            var positionsMethod = runtimeType.GetMethod(
                "TryBuildLampGlassVertexPositions", BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(Renderer), typeof(Mesh), typeof(Transform), typeof(Vector3[]).MakeByRefType() }, null) ??
                                  throw new InvalidOperationException("Bentley vehicle-local vertex mapper is unavailable to Editor checks.");
            if (!(bool)positionsMethod.Invoke(null, bootlidPositions)!)
                throw new InvalidOperationException("Bentley runtime bootlid contact position could not be mapped.");
            var mappedBootlid = bootlidPositions[3] as Vector3[] ?? Array.Empty<Vector3>();
            var bootlidRearZ = bootlidIndices
                .Select(index => mappedBootlid[index].z)
                .DefaultIfEmpty(float.PositiveInfinity)
                .Min();
            if (float.IsInfinity(bootlidRearZ))
                throw new InvalidOperationException("Bentley runtime bootlid surface has no eligible vertices.");
            var frontZ = FindWheelZ(vehicle.transform, "FrontLeft_WheelController", "FrontRight_WheelController");
            var rearZ = FindWheelZ(vehicle.transform, "RearLeft_WheelController", "RearRight_WheelController");
            if (Mathf.Abs(frontZ - rearZ) < .25f)
                throw new InvalidOperationException("Bentley runtime damage check cannot distinguish front and rear wheel anchors.");
            var frontSign = Mathf.Sign(frontZ - rearZ);
            var contactType = runtimeType.GetNestedType("Contact", BindingFlags.NonPublic) ??
                              throw new InvalidOperationException("Bentley damage contact type is unavailable to the runtime Editor check.");
            var contactConstructor = contactType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(Vector3), typeof(Vector3) }, null) ??
                                    throw new InvalidOperationException("Bentley damage contact constructor is unavailable to the runtime Editor check.");
            var impactType = runtimeType.GetNestedType("Impact", BindingFlags.NonPublic) ??
                             throw new InvalidOperationException("Bentley runtime impact type is unavailable to Editor checks.");
            var pendingField = RequireInstanceField(runtimeType, "pending");
            var pending = pendingField.GetValue(damageController) ??
                          throw new InvalidOperationException("Bentley runtime pending impact queue is unavailable.");
            var clearPending = pending.GetType().GetMethod("Clear") ??
                               throw new InvalidOperationException("Bentley runtime impact queue has no clear method.");
            var enqueue = pending.GetType().GetMethod("Enqueue") ??
                          throw new InvalidOperationException("Bentley runtime impact queue has no enqueue method.");
            var deform = runtimeType.GetMethod("DeformPendingImpacts", BindingFlags.Instance | BindingFlags.NonPublic) ??
                         throw new InvalidOperationException("Bentley runtime deformation worker is unavailable to Editor checks.");
            var heightSummaries = new List<string>();
            foreach (var contactHeight in new[] { .25f, .35f, .45f, .8f })
            {
                foreach (var surface in targetSurfaces)
                {
                    var before = GetSurfaceField(surface, "OriginalVertices") as Vector3[] ?? Array.Empty<Vector3>();
                    var working = GetSurfaceField(surface, "WorkingVertices") as Vector3[] ?? Array.Empty<Vector3>();
                    Array.Copy(before, working, Mathf.Min(before.Length, working.Length));
                    var runtimeMesh = GetSurfaceField(surface, "RuntimeMesh") as Mesh;
                    if (runtimeMesh != null)
                        runtimeMesh.vertices = working;
                }

                var contactPoints = new[] { 0f, -.55f, .55f }
                    .Select(x => new Vector3(x, contactHeight, bootlidRearZ - .03f))
                    .ToArray();
                var contactArray = Array.CreateInstance(contactType, contactPoints.Length);
                for (var i = 0; i < contactPoints.Length; i++)
                    contactArray.SetValue(contactConstructor.Invoke(new object[] { contactPoints[i], Vector3.back }), i);
                var impact = Activator.CreateInstance(impactType, true) ??
                             throw new InvalidOperationException("Bentley runtime impact could not be created for Editor checks.");
                RequireInstanceField(impactType, "Contacts").SetValue(impact, contactArray);
                RequireInstanceField(impactType, "Depth").SetValue(impact, .12f);
                RequireInstanceField(impactType, "Other").SetValue(impact, "Editor rear height response check");
                clearPending.Invoke(pending, null);
                enqueue.Invoke(pending, new[] { impact });
                var worker = deform.Invoke(damageController, null) as System.Collections.IEnumerator ??
                             throw new InvalidOperationException("Bentley runtime deformation worker could not be started in the Editor check.");
                while (worker.MoveNext())
                {
                    // Drain the production coroutine synchronously in the temporary instance.
                }

                var results = new List<string>();
                foreach (var surface in targetSurfaces)
                {
                    var renderer = GetSurfaceField(surface, "Renderer") as Renderer ??
                                   throw new InvalidOperationException("Bentley runtime rear surface lost its renderer after deformation.");
                    var before = GetSurfaceField(surface, "OriginalVertices") as Vector3[] ?? Array.Empty<Vector3>();
                    var after = GetSurfaceField(surface, "WorkingVertices") as Vector3[] ?? Array.Empty<Vector3>();
                    var changed = 0;
                    var eligible = GetSurfaceField(surface, "EligibleIndices") as int[] ?? Array.Empty<int>();
                    var mappedArguments = new object?[] { renderer, GetSurfaceField(surface, "SourceMesh"), vehicle.transform, null };
                    if (!(bool)positionsMethod.Invoke(null, mappedArguments)!)
                        throw new InvalidOperationException("Bentley runtime rear target position mapping failed: " + renderer.name);
                    var mappedPositions = mappedArguments[3] as Vector3[] ?? Array.Empty<Vector3>();
                    var rearEligible = eligible
                        .Where(index => index >= 0 && index < mappedPositions.Length && mappedPositions[index].z * frontSign < -EndRegionMinimum)
                        .ToArray();
                    var rearEligibleSet = new HashSet<int>(rearEligible);
                    var changedRear = 0;
                    foreach (var index in eligible)
                    {
                        if ((before[index] - after[index]).sqrMagnitude > .00000001f)
                        {
                            changed++;
                            if (rearEligibleSet.Contains(index))
                                changedRear++;
                        }
                    }
                    results.Add($"{renderer.name} changed={changed}/{eligible.Length}, rear={changedRear}/{rearEligible.Length}");
                    var isRearLampCoverageTarget =
                        string.Equals(renderer.name, "Object_86", StringComparison.Ordinal) ||
                        string.Equals(renderer.name, "Object_51", StringComparison.Ordinal);
                    if (contactHeight >= .8f && !isRearLampCoverageTarget && changed == 0)
                        throw new InvalidOperationException(
                            $"Bentley runtime rear impact produced no changed vertices on {renderer.name}; " +
                            $"eligible={eligible.Length}, contacts=[{string.Join(";", contactPoints)}].");
                    if (contactHeight >= .8f &&
                        (string.Equals(renderer.name, "Object_86", StringComparison.Ordinal) ||
                         string.Equals(renderer.name, "Object_51", StringComparison.Ordinal)) &&
                        (rearEligible.Length == 0 || changedRear == 0))
                        throw new InvalidOperationException(
                            $"Bentley runtime rear impact did not deform rear-eligible vertices on {renderer.name}; " +
                            $"rearChanged={changedRear}/{rearEligible.Length}, contacts=[{string.Join(";", contactPoints)}].");

                    if (string.Equals(renderer.name, "Object_51", StringComparison.Ordinal))
                    {
                        var protectedCabinIndices = Enumerable.Range(0, mappedPositions.Length)
                            .Where(index => !IsLampGlassVertex(mappedPositions[index]))
                            .ToArray();
                        if (protectedCabinIndices.Length == 0)
                            throw new InvalidOperationException("Bentley mixed GlassMtl Object_51 has no protected cabin vertices in the runtime check.");
                        var changedProtectedCabin = protectedCabinIndices.Count(index =>
                            index < before.Length && index < after.Length &&
                            (before[index] - after[index]).sqrMagnitude > .00000001f);
                        if (changedProtectedCabin != 0)
                            throw new InvalidOperationException(
                                $"Bentley rear impact changed protected cabin vertices on mixed GlassMtl Object_51; " +
                                $"changed={changedProtectedCabin}/{protectedCabinIndices.Length}, contacts=[{string.Join(";", contactPoints)}].");
                    }
                }
                heightSummaries.Add($"y={contactHeight:0.00} contacts=[{string.Join(";", contactPoints)}] " + string.Join("; ", results));
            }

            var restore = runtimeType.GetMethod("RestoreAfterNativeRepair", BindingFlags.Instance | BindingFlags.NonPublic) ??
                          throw new InvalidOperationException("Bentley runtime repair restore worker is unavailable to Editor checks.");
            var restoreWorker = restore.Invoke(damageController, null) as System.Collections.IEnumerator ??
                                throw new InvalidOperationException("Bentley runtime repair restore worker could not be started in Editor checks.");
            while (restoreWorker.MoveNext())
            {
                // Drain the production restore coroutine after the measured impacts.
            }
            foreach (var surface in targetSurfaces)
            {
                var original = GetSurfaceField(surface, "OriginalVertices") as Vector3[] ?? Array.Empty<Vector3>();
                var working = GetSurfaceField(surface, "WorkingVertices") as Vector3[] ?? Array.Empty<Vector3>();
                if (!original.SequenceEqual(working))
                    throw new InvalidOperationException(
                        "Bentley repair did not restore exact runtime vertices on " +
                        ((GetSurfaceField(surface, "Renderer") as Renderer)?.name ?? "<missing renderer>"));
            }
            if (GetSharedMesh(mixedCabin) != cabinSourceMesh)
                throw new InvalidOperationException("Bentley damaged rear panels replaced the protected Object_76 cabin mesh.");
            if (GetSharedMesh(rearCabinGlass) != rearCabinGlassMesh)
                throw new InvalidOperationException("Bentley damaged rear panels replaced the protected Object_55 cabin-glass mesh.");

            Debug.Log(
                "Bentley actual runtime rear deformation check: " + string.Join("; ", summaries) +
                "; measured low-to-high contact response: " + string.Join(" | ", heightSummaries) +
                "; trunk backing/outer panel skinning matched; repair restored all changed surfaces; " +
                "Object_76 cabin and Object_55 inside glass stayed source-bound.");
        }
        finally
        {
            if (damageController != null)
            {
                var cleanup = runtimeType.GetMethod("Cleanup", BindingFlags.Instance | BindingFlags.NonPublic);
                cleanup?.Invoke(damageController, null);
                UnityEngine.Object.DestroyImmediate(damageController as UnityEngine.Object);
            }
            UnityEngine.Object.DestroyImmediate(clone);
        }
    }

    private static FieldInfo RequireInstanceField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        throw new InvalidOperationException("Bentley runtime damage field is missing: " + name);
    }

    private static object? GetSurfaceField(object surface, string name) =>
        surface.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(surface);

    private static void VerifyInstanceMeshClone(Type runtimeType, MethodInfo selector, GameObject prefab)
    {
        var createSurface = runtimeType.GetMethod("CreateSurface", PrivateStatic);
        if (createSurface == null)
            throw new InvalidOperationException("Bentley damage surface factory is unavailable to the Editor checks.");

        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;
            var mesh = GetSharedMesh(renderer);
            if (mesh == null)
                continue;
            var output = new object?[] { renderer, mesh, null };
            if (!(bool)selector.Invoke(null, output)!)
                continue;
            var originalVertices = mesh.vertices;
            var surface = createSurface.Invoke(null, new[] { (object)renderer, mesh, output[2]! });
            if (surface == null)
                throw new InvalidOperationException("Bentley damage could not clone eligible mesh " + renderer.name);

            Mesh? runtimeMesh = null;
            try
            {
                var surfaceType = surface.GetType();
                var sourceMesh = surfaceType.GetField("SourceMesh", PrivateStatic | BindingFlags.Instance)?.GetValue(surface) as Mesh;
                runtimeMesh = surfaceType.GetField("RuntimeMesh", PrivateStatic | BindingFlags.Instance)?.GetValue(surface) as Mesh;
                var cloneVertices = runtimeMesh != null ? runtimeMesh.vertices : Array.Empty<Vector3>();
                var sourceAfter = mesh.vertices;
                if (sourceMesh != mesh || runtimeMesh == null || runtimeMesh == mesh ||
                    cloneVertices.Length != originalVertices.Length || sourceAfter.Length != originalVertices.Length)
                    throw new InvalidOperationException("Bentley damage mesh ownership is not per-instance on " + renderer.name);
                for (var i = 0; i < originalVertices.Length; i++)
                {
                    if ((originalVertices[i] - sourceAfter[i]).sqrMagnitude > 0.0000001f ||
                        (originalVertices[i] - cloneVertices[i]).sqrMagnitude > 0.0000001f)
                        throw new InvalidOperationException("Bentley damage clone changed source/baseline vertices on " + renderer.name);
                }
            }
            finally
            {
                if (runtimeMesh != null)
                    UnityEngine.Object.DestroyImmediate(runtimeMesh);
            }
            return;
        }

        throw new InvalidOperationException("Bentley damage clone check found no eligible renderer.");
    }

    private static MethodInfo RequireMethod(Type runtimeType, string name, params Type[] parameters) =>
        runtimeType.GetMethod(name, PrivateStatic, null, parameters, null) ??
        throw new InvalidOperationException("Bentley damage selector method is missing: " + name);

    private static Mesh? GetSharedMesh(Renderer renderer)
    {
        if (renderer is SkinnedMeshRenderer skinned)
            return skinned.sharedMesh;
        var filter = renderer.GetComponent<MeshFilter>();
        return filter != null ? filter.sharedMesh : null;
    }

    private static float FindWheelZ(Transform root, params string[] names)
    {
        foreach (var name in names)
        {
            var wheel = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => string.Equals(transform.name, name, StringComparison.Ordinal));
            if (wheel != null)
                return root.InverseTransformPoint(wheel.position).z;
        }
        throw new InvalidOperationException("Bentley damage checks cannot find wheel anchor " + string.Join(" or ", names));
    }

    private static string GetProtectionKind(Transform transform)
    {
        for (var node = transform; node != null; node = node.parent)
        {
            var name = node.name;
            if (ContainsAny(name, "wheel", "tire", "tyre")) return "wheel";
            if (ContainsAny(name, "brake", "caliper", "calliper")) return "brake";
            if (ContainsAny(name, "interior", "seat", "dashboard")) return "interior";
            if (ContainsAny(name, "window", "glass") && !ContainsAny(name, "light", "lamp", "headlight", "taillight"))
                return "glass";
        }
        return string.Empty;
    }

    private static bool IsNamedLamp(Transform transform)
    {
        for (var node = transform; node != null; node = node.parent)
            if (ContainsAny(node.name, "light", "lamp", "headlight", "taillight"))
                return true;
        return false;
    }

    private static bool IsBodyPaintMaterial(string name) =>
        name.Equals("Paint", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("OEMPaint", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Bentley_Paint_Prepared", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Bentley_OEMPaint_Prepared", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("BentleyMaterial_026", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("BentleyMaterial_027", StringComparison.OrdinalIgnoreCase);

    private static bool IsInteriorMaterial(string name) =>
        name.StartsWith("Bentley_Interior", StringComparison.OrdinalIgnoreCase);

    private static bool IsGlassMaterial(string name) =>
        name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsLampGlassVertex(Vector3 vertex) =>
        Mathf.Abs(vertex.z) >= 1.65f && vertex.y >= 0.25f && vertex.y <= 1.15f;

    private static string NormalizeName(string name)
    {
        var value = name.Trim();
        const string cloneSuffix = " (Clone)";
        while (value.EndsWith(cloneSuffix, StringComparison.Ordinal))
            value = value.Substring(0, value.Length - cloneSuffix.Length);
        return value;
    }

    private static bool ContainsAny(string value, params string[] markers) =>
        markers.Any(marker => value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
}
