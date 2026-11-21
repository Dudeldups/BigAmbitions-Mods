#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Moves source-GLB wheel geometry out of the body-bound skinned meshes and
/// onto the native NWH rolling and upright visual references.
/// </summary>
internal static class BentleyContinentalGTSpeedWheelGeometrySetup
{
    private const string MeshFolder = "Assets/Mods/Bentley_Continental_GT_Speed/Models/GeneratedMeshes";
    private static readonly WheelDefinition[] Wheels =
    {
        new("FrontLeft", "3DWheel Front L", "Wheel Front L_", "Calliper Front L_"),
        new("FrontRight", "3DWheel Front R", "Wheel Front R_", "Calliper Front R_"),
        new("RearLeft", "3DWheel Rear L", "Wheel Rear L_", "Calliper Rear L_"),
        new("RearRight", "3DWheel Rear R", "Wheel Rear R_", "Calliper Rear R_"),
    };

    public static void ExtractAndBind(Transform vehicleRoot, Transform sourceModelRoot)
    {
        if (vehicleRoot == null) throw new ArgumentNullException(nameof(vehicleRoot));
        if (sourceModelRoot == null) throw new ArgumentNullException(nameof(sourceModelRoot));

        var sourceRenderers = sourceModelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(renderer => renderer != null && renderer.sharedMesh != null)
            .OrderBy(renderer => GetHierarchyPath(sourceModelRoot, renderer.transform), StringComparer.Ordinal)
            .ToArray();
        if (sourceRenderers.Length == 0)
            throw new InvalidOperationException("Bentley wheel extraction found no skinned source meshes.");

        var visuals = new Dictionary<string, WheelVisuals>(StringComparer.Ordinal);
        foreach (var wheel in Wheels)
        {
            var rolling = FindTransform(vehicleRoot, wheel.RollingVisualName) ??
                          throw new InvalidOperationException(
                              "Bentley rolling visual is missing: " + wheel.RollingVisualName);
            var controller = FindTransform(vehicleRoot, wheel.Corner + "_WheelController") ??
                            throw new InvalidOperationException(
                                "Bentley wheel controller is missing: " + wheel.Corner);
            rolling.position = controller.position;

            var upright = FindTransform(vehicleRoot, wheel.Corner + "_UprightVisual");
            if (upright == null)
            {
                var uprightObject = new GameObject(wheel.Corner + "_UprightVisual");
                upright = uprightObject.transform;
                upright.SetParent(vehicleRoot, true);
            }
            upright.SetPositionAndRotation(controller.position, controller.rotation);
            visuals.Add(wheel.Corner, new WheelVisuals(wheel, rolling, upright, controller));
        }

        var extractedTriangles = Wheels.ToDictionary(
            wheel => wheel.Corner,
            _ => new GeometryCounts(),
            StringComparer.Ordinal);
        var bodyMeshIndex = 0;
        var outputMeshIndex = 0;

        foreach (var renderer in sourceRenderers)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            var sourceMesh = renderer.sharedMesh;
            var weights = sourceMesh.boneWeights;
            if (weights == null || weights.Length != sourceMesh.vertexCount)
                continue;

            var wheelBones = FindWheelBoneIndices(renderer);
            if (wheelBones.Count == 0)
                continue;

            var bakedMesh = new Mesh { name = sourceMesh.name + "_BentleyWheelBake" };
            try
            {
                renderer.BakeMesh(bakedMesh, false);
                var remainingTriangles = new List<int>[sourceMesh.subMeshCount];
                var extracted = new Dictionary<GeometryTarget, List<int>[]>(GeometryTargetComparer.Instance);
                for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
                {
                    var inputTriangles = sourceMesh.GetTriangles(subMesh);
                    var kept = new List<int>(inputTriangles.Length);
                    remainingTriangles[subMesh] = kept;

                    for (var triangleStart = 0; triangleStart + 2 < inputTriangles.Length; triangleStart += 3)
                    {
                        var a = inputTriangles[triangleStart];
                        var b = inputTriangles[triangleStart + 1];
                        var c = inputTriangles[triangleStart + 2];
                        var target = ResolveTriangleTarget(a, b, c, weights, wheelBones);
                        if (target == null)
                        {
                            kept.Add(a);
                            kept.Add(b);
                            kept.Add(c);
                            continue;
                        }

                        if (!extracted.TryGetValue(target, out var targetTriangles))
                        {
                            targetTriangles = CreateSubmeshTriangleLists(sourceMesh.subMeshCount);
                            extracted.Add(target, targetTriangles);
                        }
                        targetTriangles[subMesh].Add(a);
                        targetTriangles[subMesh].Add(b);
                        targetTriangles[subMesh].Add(c);
                    }
                }

                if (extracted.Count == 0)
                    continue;

                foreach (var entry in extracted.OrderBy(
                             pair => pair.Key.Wheel.Corner + pair.Key.Kind,
                             StringComparer.Ordinal))
                {
                    var geometry = CreateExtractedMesh(
                        renderer, sourceMesh, bakedMesh, entry.Value,
                        entry.Key.Wheel, visuals[entry.Key.Wheel.Corner], entry.Key.Kind);
                    if (geometry == null)
                        continue;

                    var meshName = $"BentleyWheel_{entry.Key.Wheel.Corner}_{entry.Key.Kind}_{outputMeshIndex++:D3}";
                    geometry.name = meshName;
                    var meshAsset = PersistMesh(geometry, meshName);
                    AddGeometryRenderer(
                        entry.Key.Kind == GeometryKind.Rolling
                            ? visuals[entry.Key.Wheel.Corner].Rolling
                            : visuals[entry.Key.Wheel.Corner].Upright,
                        meshAsset,
                        renderer.sharedMaterials,
                        renderer,
                        $"BentleyWheelPiece_{entry.Key.Kind}_{outputMeshIndex:D3}");

                    var triangleCount = CountTriangles(entry.Value);
                    extractedTriangles[entry.Key.Wheel.Corner].Add(entry.Key.Kind, triangleCount);
                }

                var bodyMesh = UnityEngine.Object.Instantiate(sourceMesh);
                bodyMesh.name = $"BentleyWheelSplitBody_{bodyMeshIndex:D3}";
                for (var subMesh = 0; subMesh < bodyMesh.subMeshCount; subMesh++)
                    bodyMesh.SetTriangles(remainingTriangles[subMesh], subMesh, true);
                bodyMesh.bounds = sourceMesh.bounds;
                var bodyAsset = PersistMesh(bodyMesh, bodyMesh.name);
                renderer.sharedMesh = bodyAsset;
                bodyMeshIndex++;

                foreach (var entry in extracted)
                {
                    var wheelMeshTriangles = CountTriangles(entry.Value);
                    if (wheelMeshTriangles <= 0)
                        continue;
                    var remainingWheelBoneIndex = entry.Key.Kind == GeometryKind.Rolling
                        ? wheelBones[entry.Key].RollingBone
                        : wheelBones[entry.Key].UprightBone;
                    var remainingWheelBoneTriangles = CountTrianglesStillWeightedToBone(
                        renderer.sharedMesh, remainingWheelBoneIndex);
                    if (remainingWheelBoneTriangles != 0)
                    {
                        throw new InvalidOperationException(
                            $"Bentley wheel split left {remainingWheelBoneTriangles} triangles on " +
                            $"the static body mesh for {entry.Key.Wheel.Corner}.");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bakedMesh);
            }
        }

        foreach (var wheel in Wheels)
        {
            var counts = extractedTriangles[wheel.Corner];
            if (counts.RollingTriangles == 0)
                throw new InvalidOperationException(
                    "Bentley wheel geometry extraction produced no rolling triangles for " + wheel.Corner + ".");
            if (counts.UprightTriangles == 0)
                throw new InvalidOperationException(
                    "Bentley wheel geometry extraction produced no caliper/upright triangles for " + wheel.Corner + ".");

            var visual = visuals[wheel.Corner];
            var sourceMeasurements = GetRollingMeasurements(visual.Rolling);
            var recentering = -sourceMeasurements.center;
            RecenterGeometry(visual.Rolling, recentering);
            RecenterGeometry(visual.Upright, recentering);
            var controllerComponent = FindWheelController(visual.Controller);
            var serializedWheel = new SerializedObject(controllerComponent);
            var uprightProperty = serializedWheel.FindProperty("wheel.nonRotatingVisual");
            if (uprightProperty == null || uprightProperty.propertyType != SerializedPropertyType.ObjectReference)
                throw new InvalidOperationException(
                    "Could not inspect native non-rotating caliper reference for " + wheel.Corner + ".");
            uprightProperty.objectReferenceValue = visual.Upright.gameObject;
            serializedWheel.ApplyModifiedPropertiesWithoutUndo();
            if (serializedWheel.FindProperty("wheel.nonRotatingVisual")?.objectReferenceValue != visual.Upright.gameObject)
                throw new InvalidOperationException(
                    "Native non-rotating caliper reference did not persist for " + wheel.Corner + ".");

            VerifyVisualHasGeometry(visual.Rolling, wheel.Corner + " rolling");
            VerifyVisualHasGeometry(visual.Upright, wheel.Corner + " upright caliper");
            var rollingMeasurements = GetRollingMeasurements(visual.Rolling);
            var wheelRadius = GetWheelRadius(controllerComponent, wheel.Corner);
            var staticGroundGap = visual.Controller.localPosition.y - wheelRadius;
            if (rollingMeasurements.centerOffset > 0.03f)
                throw new InvalidOperationException(
                    $"Bentley {wheel.Corner} rolling mesh center is offset {rollingMeasurements.centerOffset:F4} m " +
                    "from its authored NWH pivot.");
            if (Mathf.Abs(rollingMeasurements.outerRadius - wheelRadius) > 0.05f)
                throw new InvalidOperationException(
                    $"Bentley {wheel.Corner} extracted tire radius {rollingMeasurements.outerRadius:F4} m " +
                    $"does not match the NWH radius {wheelRadius:F4} m within 5 cm.");
            Debug.Log(
                $"Bentley wheel authoring {wheel.Corner}: extracted {counts.RollingTriangles} rolling and " +
                $"{counts.UprightTriangles} upright triangles; NWH center={visual.Controller.localPosition:F4}, " +
                $"radius={wheelRadius:F4} m, static ground gap={staticGroundGap:F4} m, " +
                $"visual mesh recenter={recentering:F4} m, " +
                $"mesh axial width={rollingMeasurements.width:F4} m, " +
                $"mesh center offset={rollingMeasurements.centerOffset:F4} m, " +
                $"mesh outer radius={rollingMeasurements.outerRadius:F4} m.");
        }

        AssetDatabase.SaveAssets();
    }

    private static Dictionary<GeometryTarget, BonePair> FindWheelBoneIndices(SkinnedMeshRenderer renderer)
    {
        var indices = new Dictionary<GeometryTarget, BonePair>(GeometryTargetComparer.Instance);
        var bones = renderer.bones;
        foreach (var wheel in Wheels)
        {
            var rollingIndex = FindBoneIndex(bones, wheel.BoneNameFragment);
            var uprightIndex = FindBoneIndex(bones, wheel.CaliperBoneNameFragment);
            if (rollingIndex < 0 && uprightIndex < 0)
                continue;
            indices.Add(new GeometryTarget(wheel, GeometryKind.Rolling), new BonePair(rollingIndex, uprightIndex));
            indices.Add(new GeometryTarget(wheel, GeometryKind.Upright), new BonePair(rollingIndex, uprightIndex));
        }
        return indices;
    }

    private static GeometryTarget? ResolveTriangleTarget(
        int a,
        int b,
        int c,
        BoneWeight[] weights,
        Dictionary<GeometryTarget, BonePair> wheelBones)
    {
        foreach (var entry in wheelBones)
        {
            var boneIndex = entry.Key.Kind == GeometryKind.Rolling
                ? entry.Value.RollingBone
                : entry.Value.UprightBone;
            if (boneIndex < 0)
                continue;
            if (DominantBone(weights[a]) == boneIndex &&
                DominantBone(weights[b]) == boneIndex &&
                DominantBone(weights[c]) == boneIndex)
                return entry.Key;
        }
        return null;
    }

    private static Mesh? CreateExtractedMesh(
        SkinnedMeshRenderer sourceRenderer,
        Mesh sourceMesh,
        Mesh bakedMesh,
        List<int>[] sourceTriangles,
        WheelDefinition wheel,
        WheelVisuals visuals,
        GeometryKind kind)
    {
        var triangleCount = CountTriangles(sourceTriangles);
        if (triangleCount == 0)
            return null;

        var target = kind == GeometryKind.Rolling ? visuals.Rolling : visuals.Upright;
        var bakedVertices = bakedMesh.vertices;
        var bakedNormals = bakedMesh.normals;
        var bakedTangents = bakedMesh.tangents;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var tangents = new List<Vector4>();
        var colors = bakedMesh.colors32;
        var outputColors = new List<Color32>();
        var uvChannels = new List<Vector4>[8];
        var outputUvChannels = new List<Vector4>[8];
        for (var channel = 0; channel < uvChannels.Length; channel++)
        {
            uvChannels[channel] = new List<Vector4>();
            outputUvChannels[channel] = new List<Vector4>();
            bakedMesh.GetUVs(channel, uvChannels[channel]);
        }

        var remap = new Dictionary<int, int>();
        var submeshTriangles = CreateSubmeshTriangleLists(sourceTriangles.Length);
        for (var subMesh = 0; subMesh < sourceTriangles.Length; subMesh++)
        {
            foreach (var sourceIndex in sourceTriangles[subMesh])
            {
                if (!remap.TryGetValue(sourceIndex, out var outputIndex))
                {
                    outputIndex = vertices.Count;
                    remap.Add(sourceIndex, outputIndex);
                    var worldPosition = sourceRenderer.transform.TransformPoint(bakedVertices[sourceIndex]);
                    vertices.Add(target.InverseTransformPoint(worldPosition));

                    if (bakedNormals.Length == bakedVertices.Length)
                    {
                        var worldNormal = sourceRenderer.transform.localToWorldMatrix.inverse.transpose
                            .MultiplyVector(bakedNormals[sourceIndex]);
                        normals.Add(target.localToWorldMatrix.transpose.MultiplyVector(worldNormal).normalized);
                    }
                    if (bakedTangents.Length == bakedVertices.Length)
                    {
                        var worldTangent = sourceRenderer.transform.TransformVector(
                            new Vector3(bakedTangents[sourceIndex].x, bakedTangents[sourceIndex].y,
                                bakedTangents[sourceIndex].z));
                        var localTangent = target.InverseTransformVector(worldTangent).normalized;
                        tangents.Add(new Vector4(
                            localTangent.x, localTangent.y, localTangent.z, bakedTangents[sourceIndex].w));
                    }
                    if (colors.Length == bakedVertices.Length)
                        outputColors.Add(colors[sourceIndex]);
                    for (var channel = 0; channel < uvChannels.Length; channel++)
                        if (uvChannels[channel].Count == bakedVertices.Length)
                            outputUvChannels[channel].Add(uvChannels[channel][sourceIndex]);
                }
                submeshTriangles[subMesh].Add(outputIndex);
            }
        }

        var result = new Mesh
        {
            name = $"BentleyWheel_{wheel.Corner}_{kind}",
            indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        result.SetVertices(vertices);
        if (normals.Count == vertices.Count) result.SetNormals(normals);
        if (tangents.Count == vertices.Count) result.SetTangents(tangents);
        if (outputColors.Count == vertices.Count) result.SetColors(outputColors);
        for (var channel = 0; channel < outputUvChannels.Length; channel++)
            if (outputUvChannels[channel].Count == vertices.Count)
                result.SetUVs(channel, outputUvChannels[channel]);
        result.subMeshCount = sourceMesh.subMeshCount;
        for (var subMesh = 0; subMesh < submeshTriangles.Length; subMesh++)
            result.SetTriangles(submeshTriangles[subMesh], subMesh, false);
        result.RecalculateBounds();
        return result;
    }

    private static void AddGeometryRenderer(
        Transform parent,
        Mesh mesh,
        Material[] materials,
        SkinnedMeshRenderer source,
        string objectName)
    {
        var child = new GameObject(objectName);
        child.transform.SetParent(parent, false);
        var filter = child.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;
        renderer.lightProbeUsage = source.lightProbeUsage;
        renderer.reflectionProbeUsage = source.reflectionProbeUsage;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.motionVectorGenerationMode = source.motionVectorGenerationMode;
        renderer.enabled = mesh.triangles.Length > 0;
    }

    private static Mesh PersistMesh(Mesh mesh, string name)
    {
        var path = MeshFolder + "/" + name + ".asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        mesh.name = name;
        AssetDatabase.CreateAsset(mesh, path);
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static void VerifyVisualHasGeometry(Transform visual, string label)
    {
        var renderers = visual.GetComponentsInChildren<MeshRenderer>(true)
            .Where(renderer => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
            .ToArray();
        var triangles = renderers.Sum(renderer =>
        {
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null ? filter.sharedMesh.triangles.Length / 3 : 0;
        });
        if (triangles == 0)
            throw new InvalidOperationException("Bentley NWH " + label + " visual has no enabled rendered triangles.");
    }

    private static (float width, Vector3 center, float centerOffset, float outerRadius) GetRollingMeasurements(
        Transform rollingVisual)
    {
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;
        var minY = float.PositiveInfinity;
        var minZ = float.PositiveInfinity;
        var maxY = float.NegativeInfinity;
        var maxZ = float.NegativeInfinity;
        var minAxle = float.PositiveInfinity;
        var maxAxle = float.NegativeInfinity;
        foreach (var filter in rollingVisual.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.sharedMesh.vertexCount == 0)
                continue;
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                var point = filter.transform == rollingVisual
                    ? vertex
                    : rollingVisual.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minY = Mathf.Min(minY, point.y);
                maxY = Mathf.Max(maxY, point.y);
                minZ = Mathf.Min(minZ, point.z);
                maxZ = Mathf.Max(maxZ, point.z);
                minAxle = Mathf.Min(minAxle, point.x);
                maxAxle = Mathf.Max(maxAxle, point.x);
            }
        }
        if (maxY <= minY || maxZ <= minZ)
            throw new InvalidOperationException("Bentley rolling visual has no measurable tire radius.");
        var centerY = (minY + maxY) * 0.5f;
        var centerZ = (minZ + maxZ) * 0.5f;
        var center = new Vector3((minX + maxX) * 0.5f, centerY, centerZ);
        var centerOffset = Mathf.Sqrt(centerY * centerY + centerZ * centerZ);
        var outerRadius = 0f;
        foreach (var filter in rollingVisual.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;
            foreach (var vertex in filter.sharedMesh.vertices)
            {
                var point = filter.transform == rollingVisual
                    ? vertex
                    : rollingVisual.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                outerRadius = Mathf.Max(outerRadius,
                    Mathf.Sqrt((point.y - centerY) * (point.y - centerY) +
                               (point.z - centerZ) * (point.z - centerZ)));
            }
        }
        return (maxAxle - minAxle, center, centerOffset, outerRadius);
    }

    internal static void CorrectRollingMesh(Transform root)
    {
        foreach (var wheel in Wheels)
        {
            var rolling = root.Find(wheel.RollingVisualName);
            if (rolling == null)
                throw new InvalidOperationException($"Bentley rolling root missing for {wheel.Corner}.");

            foreach (var filter in rolling.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null)
                    continue;
                // Cancel the source model's measured static camber inside the
                // rolling pivot so the tire's axis matches the NWH spin axis.
                float camber = wheel.Corner.StartsWith("Front", StringComparison.Ordinal) ? 1.5f : 1f;
                if (wheel.Corner.EndsWith("Right", StringComparison.Ordinal))
                    camber = -camber;

                // The extracted tires have an elliptical Y/Z envelope. Keep the
                // vertical contact radius and correct the longitudinal radius.
                var bounds = mesh.bounds;
                if (Mathf.Abs(bounds.extents.y - bounds.extents.z) < 0.0001f)
                {
                    filter.transform.localRotation = Quaternion.Euler(0f, 0f, camber);
                    continue;
                }
                if (bounds.extents.z < 0.25f || bounds.extents.y < 0.25f ||
                    Vector3.Distance(filter.transform.localScale, Vector3.one) > 0.0001f ||
                    Quaternion.Angle(filter.transform.localRotation, Quaternion.identity) > 0.01f)
                    throw new InvalidOperationException($"Unexpected Bentley rolling mesh basis: {mesh.name}.");

                var factor = bounds.extents.y / bounds.extents.z;
                var vertices = mesh.vertices;
                for (var i = 0; i < vertices.Length; i++)
                    vertices[i].z *= factor;
                mesh.vertices = vertices;
                var normals = mesh.normals;
                for (var i = 0; i < normals.Length; i++)
                {
                    normals[i].z /= factor;
                    normals[i] = normals[i].normalized;
                }
                mesh.normals = normals;
                var tangents = mesh.tangents;
                for (var i = 0; i < tangents.Length; i++)
                {
                    var direction = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z * factor).normalized;
                    tangents[i] = new Vector4(direction.x, direction.y, direction.z, tangents[i].w);
                }
                mesh.tangents = tangents;
                mesh.RecalculateBounds();
                filter.transform.localRotation = Quaternion.Euler(0f, 0f, camber);
                EditorUtility.SetDirty(mesh);
                Debug.Log($"Bentley {wheel.Corner} spin radii Y/Z: {bounds.extents.y:F5}/{bounds.extents.z:F5} m -> {mesh.bounds.extents.y:F5}/{mesh.bounds.extents.z:F5} m.");
            }
        }
    }

    private static void RecenterGeometry(Transform visual, Vector3 localOffset)
    {
        foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0)
                continue;
            var meshOffset = filter.transform.InverseTransformVector(visual.TransformVector(localOffset));
            var vertices = mesh.vertices;
            for (var index = 0; index < vertices.Length; index++)
                vertices[index] += meshOffset;
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }
    }

    private static float GetWheelRadius(Component wheelController, string corner)
    {
        var serialized = new SerializedObject(wheelController);
        var radius = serialized.FindProperty("wheel.radius");
        if (radius == null || radius.propertyType != SerializedPropertyType.Float)
            throw new InvalidOperationException("Could not inspect NWH radius for " + corner + ".");
        return radius.floatValue;
    }

    private static int CountTrianglesStillWeightedToBone(Mesh mesh, int boneIndex)
    {
        var weights = mesh.boneWeights;
        if (weights == null || weights.Length != mesh.vertexCount)
            return 0;
        var count = 0;
        for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            var indices = mesh.GetIndices(subMesh);
            for (var index = 0; index + 2 < indices.Length; index += 3)
            {
                var a = weights[indices[index]];
                var b = weights[indices[index + 1]];
                var c = weights[indices[index + 2]];
                if (DominantBone(a) == boneIndex && DominantBone(b) == boneIndex &&
                    DominantBone(c) == boneIndex)
                    count++;
            }
        }
        return count;
    }

    private static int FindBoneIndex(Transform[] bones, string nameFragment)
    {
        var matching = Enumerable.Range(0, bones.Length)
            .Where(index => bones[index] != null &&
                            bones[index].name.StartsWith(nameFragment, StringComparison.Ordinal))
            .ToArray();
        if (matching.Length > 1)
            throw new InvalidOperationException(
                "Bentley source renderer has duplicate wheel bones for " + nameFragment + ".");
        return matching.Length == 0 ? -1 : matching[0];
    }

    private static int DominantBone(BoneWeight weight)
    {
        var index = weight.boneIndex0;
        var amount = weight.weight0;
        if (weight.weight1 > amount) { index = weight.boneIndex1; amount = weight.weight1; }
        if (weight.weight2 > amount) { index = weight.boneIndex2; amount = weight.weight2; }
        if (weight.weight3 > amount) { index = weight.boneIndex3; amount = weight.weight3; }
        return amount > 0f ? index : -1;
    }

    private static List<int>[] CreateSubmeshTriangleLists(int submeshCount)
    {
        var result = new List<int>[submeshCount];
        for (var index = 0; index < result.Length; index++) result[index] = new List<int>();
        return result;
    }

    private static int CountTriangles(IEnumerable<List<int>> submeshTriangles) =>
        submeshTriangles.Sum(triangles => triangles.Count / 3);

    private static int CountTriangles(List<int>[] submeshTriangles) =>
        submeshTriangles.Sum(triangles => triangles.Count / 3);

    private static string GetHierarchyPath(Transform root, Transform target)
    {
        var names = new Stack<string>();
        for (var current = target; current != null && current != root; current = current.parent)
            names.Push(current.name);
        return string.Join("/", names);
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        if (string.Equals(root.name, name, StringComparison.Ordinal))
            return root;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        return null;
    }

    private static Component FindWheelController(Transform transform) =>
        transform.GetComponents<Component>().FirstOrDefault(component =>
            component != null && string.Equals(component.GetType().Name, "WheelController", StringComparison.Ordinal)) ??
        throw new InvalidOperationException("Native WheelController is missing on " + transform.name + ".");

    private sealed class WheelDefinition
    {
        public WheelDefinition(string corner, string rollingVisualName, string boneNameFragment,
            string caliperBoneNameFragment)
        {
            Corner = corner;
            RollingVisualName = rollingVisualName;
            BoneNameFragment = boneNameFragment;
            CaliperBoneNameFragment = caliperBoneNameFragment;
        }

        public string Corner { get; }
        public string RollingVisualName { get; }
        public string BoneNameFragment { get; }
        public string CaliperBoneNameFragment { get; }
    }

    private sealed class WheelVisuals
    {
        public WheelVisuals(WheelDefinition definition, Transform rolling, Transform upright, Transform controller)
        {
            Definition = definition;
            Rolling = rolling;
            Upright = upright;
            Controller = controller;
        }

        public WheelDefinition Definition { get; }
        public Transform Rolling { get; }
        public Transform Upright { get; }
        public Transform Controller { get; }
    }

    private sealed class GeometryCounts
    {
        public int RollingTriangles { get; private set; }
        public int UprightTriangles { get; private set; }

        public void Add(GeometryKind kind, int count)
        {
            if (kind == GeometryKind.Rolling) RollingTriangles += count;
            else UprightTriangles += count;
        }
    }

    private sealed class GeometryTarget
    {
        public GeometryTarget(WheelDefinition wheel, GeometryKind kind)
        {
            Wheel = wheel;
            Kind = kind;
        }

        public WheelDefinition Wheel { get; }
        public GeometryKind Kind { get; }
    }

    private sealed class GeometryTargetComparer : IEqualityComparer<GeometryTarget>
    {
        public static readonly GeometryTargetComparer Instance = new();
        public bool Equals(GeometryTarget? left, GeometryTarget? right) =>
            left != null && right != null && left.Wheel == right.Wheel && left.Kind == right.Kind;
        public int GetHashCode(GeometryTarget target) => HashCode.Combine(target.Wheel, target.Kind);
    }

    private readonly struct BonePair
    {
        public BonePair(int rollingBone, int uprightBone)
        {
            RollingBone = rollingBone;
            UprightBone = uprightBone;
        }

        public int RollingBone { get; }
        public int UprightBone { get; }
    }

    private enum GeometryKind
    {
        Rolling,
        Upright,
    }
}
