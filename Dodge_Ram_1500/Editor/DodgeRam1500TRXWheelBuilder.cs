#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal static class DodgeRam1500TRXWheelBuilder
{
    internal static void Attach(
        GameObject root,
        GameObject model,
        IReadOnlyDictionary<string, Vector3> wheelPositions,
        float frontRadius,
        float rearRadius,
        float tireWidth,
        string meshFolder,
        string materialFolder)
    {
        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        var tireSource = FindModelRenderer(renderers, "polySurface1") ??
                         throw new InvalidOperationException("The TRX tire mesh was not found.");
        var rimSource = FindModelRenderer(renderers, "polySurface8441") ??
                        throw new InvalidOperationException("The TRX 18-inch rim mesh was not found.");
        var caliperSources = new List<MeshRenderer>();
        foreach (var renderer in renderers)
        {
            if (HasAncestorName(renderer.transform, "polySurface9497") ||
                HasAncestorName(renderer.transform, "polySurface9559"))
                caliperSources.Add(renderer);
        }
        if (caliperSources.Count != 2)
            throw new InvalidOperationException(
                $"Expected two front/rear TRX caliper meshes; found {caliperSources.Count}.");

        EnsureFolder(meshFolder);
        EnsureFolder(materialFolder);
        var tireMaterial = CreatePartMaterial(
            tireSource.sharedMaterial,
            materialFolder,
            "DodgeRam1500TRXTire",
            new Color(0.24f, 0.24f, 0.24f, 1f),
            0.02f,
            0.28f);
        var rimMaterial = CreatePartMaterial(
            rimSource.sharedMaterial,
            materialFolder,
            "DodgeRam1500TRXRim",
            DodgeRam1500TRXMaterials.RimBaseColor,
            DodgeRam1500TRXMaterials.RimMetallic,
            DodgeRam1500TRXMaterials.RimSmoothness);
        var caliperMaterial = CreatePartMaterial(
            caliperSources[0].sharedMaterial,
            materialFolder,
            "DodgeRam1500TRXCaliper",
            new Color(0.72f, 0.025f, 0.012f, 1f),
            0.58f,
            0.62f);
        var rotorMaterial = CreatePlainMaterial(
            materialFolder,
            "DodgeRam1500TRXBrakeRotor",
            new Color(0.28f, 0.30f, 0.32f, 1f),
            0.78f,
            0.72f);

        foreach (var pair in wheelPositions)
        {
            var corner = pair.Key.Replace("_WheelController", string.Empty);
            var isFront = corner.StartsWith("Front", StringComparison.Ordinal);
            var center = pair.Value;
            var tireRadius = isFront ? frontRadius : rearRadius;
            var tireMesh = BuildCornerMesh(root, new[] { tireSource }, wheelPositions, corner, tireRadius, tireWidth, out var sourceTireBounds);
            var tire = SaveMesh(
                meshFolder,
                "DodgeRam1500TRXTire_" + corner,
                tireMesh);
            var rim = SaveMesh(
                meshFolder,
                "DodgeRam1500TRXRim_" + corner,
                BuildCornerMesh(root, new[] { rimSource }, wheelPositions, corner, 0.2286f, 0.229f, out _));
            var rotorRadius = (isFront ? 0.378f : 0.375f) * 0.5f;
            var rotor = SaveMesh(
                meshFolder,
                "DodgeRam1500TRXBrakeRotor_" + corner,
                CreateBrakeRotor("DodgeRam1500TRXBrakeRotor_" + corner, rotorRadius));
            var caliper = SaveMesh(
                meshFolder,
                "DodgeRam1500TRXCaliper_" + corner,
                BuildCornerMesh(root, caliperSources, wheelPositions, corner, 0f, 0f, out _,
                    sourceTireBounds.center, new Vector3(1f, tireRadius * 2f / sourceTireBounds.size.y, tireRadius * 2f / sourceTireBounds.size.z)));

            var mount = new GameObject("DodgeRam1500TRXWheel" + corner);
            mount.transform.SetParent(root.transform, false);
            mount.transform.localPosition = center;
            AddMeshPart(mount.transform, "DodgeRam1500TRXTire" + corner, tire, tireMaterial);
            AddMeshPart(mount.transform, "DodgeRam1500TRXRim" + corner, rim, rimMaterial);
            AddMeshPart(mount.transform, "DodgeRam1500TRXBrakeRotor" + corner, rotor, rotorMaterial);

            var fixedCaliper = new GameObject("DodgeRam1500TRXFixedCaliper" + corner);
            fixedCaliper.transform.SetParent(root.transform, false);
            fixedCaliper.transform.localPosition = center;
            AddMeshPart(
                fixedCaliper.transform,
                "DodgeRam1500TRXCaliperGeometry" + corner,
                caliper,
                caliperMaterial);

            var controller = FindChild(root.transform, pair.Key) ??
                             throw new InvalidOperationException(
                                 $"NWH wheel controller '{pair.Key}' is missing.");
            controller.localPosition = center;
            AssignWheelVisual(controller, mount);
        }

        RemoveSourceGeometry(tireSource);
        RemoveSourceGeometry(rimSource);
        foreach (var source in caliperSources)
            RemoveSourceGeometry(source);
    }

    private static MeshRenderer? FindModelRenderer(MeshRenderer[] renderers, string hierarchyName)
    {
        foreach (var renderer in renderers)
        {
            if (HasAncestorName(renderer.transform, hierarchyName) &&
                renderer.GetComponent<MeshFilter>()?.sharedMesh != null)
                return renderer;
        }
        return null;
    }

    private static Mesh BuildCornerMesh(
        GameObject root,
        IReadOnlyList<MeshRenderer> sources,
        IReadOnlyDictionary<string, Vector3> wheelPositions,
        string corner,
        float radius,
        float width,
        out Bounds sourceBounds,
        Vector3? axleCenter = null,
        Vector3? axleScale = null)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        var bounds = default(Bounds);
        var hasBounds = false;
        foreach (var renderer in sources)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var source = filter?.sharedMesh;
            if (source == null || !source.isReadable)
                throw new InvalidOperationException(
                    $"Source geometry '{renderer.name}' is missing or not readable.");
            var sourceVertices = source.vertices;
            var sourceUvs = source.uv;
            for (var submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                var sourceIndices = source.GetTriangles(submesh);
                for (var index = 0; index + 2 < sourceIndices.Length; index += 3)
                {
                    var a = root.transform.InverseTransformPoint(
                        renderer.transform.TransformPoint(sourceVertices[sourceIndices[index]]));
                    var b = root.transform.InverseTransformPoint(
                        renderer.transform.TransformPoint(sourceVertices[sourceIndices[index + 1]]));
                    var c = root.transform.InverseTransformPoint(
                        renderer.transform.TransformPoint(sourceVertices[sourceIndices[index + 2]]));
                    if (!string.Equals(NearestCorner((a + b + c) / 3f, wheelPositions), corner,
                            StringComparison.Ordinal))
                        continue;

                    var start = vertices.Count;
                    vertices.Add(a);
                    vertices.Add(b);
                    vertices.Add(c);
                    triangles.Add(start);
                    triangles.Add(start + 1);
                    triangles.Add(start + 2);
                    uvs.Add(ReadUv(sourceUvs, sourceVertices.Length, sourceIndices[index]));
                    uvs.Add(ReadUv(sourceUvs, sourceVertices.Length, sourceIndices[index + 1]));
                    uvs.Add(ReadUv(sourceUvs, sourceVertices.Length, sourceIndices[index + 2]));
                    Include(ref bounds, ref hasBounds, a);
                    Include(ref bounds, ref hasBounds, b);
                    Include(ref bounds, ref hasBounds, c);
                }
            }
        }

        if (!hasBounds || triangles.Count < 3)
            throw new InvalidOperationException($"No source triangles were assigned to {corner}.");

        sourceBounds = bounds;
        var mesh = new Mesh { name = corner };
        mesh.indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        var fit = radius > 0f && width > 0f;
        var scale = fit
            ? new Vector3(
                width / Mathf.Max(0.001f, bounds.size.x),
                (radius * 2f) / Mathf.Max(0.001f, bounds.size.y),
                (radius * 2f) / Mathf.Max(0.001f, bounds.size.z))
            : axleScale ?? Vector3.one;
        for (var index = 0; index < vertices.Count; index++)
            vertices[index] = Vector3.Scale(vertices[index] - (axleCenter ?? bounds.center), scale);
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateNormals();
        if (uvs.Count == vertices.Count)
            mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static string NearestCorner(
        Vector3 point,
        IReadOnlyDictionary<string, Vector3> wheelPositions)
    {
        var bestCorner = string.Empty;
        var bestDistance = float.PositiveInfinity;
        foreach (var pair in wheelPositions)
        {
            var delta = new Vector2(point.x - pair.Value.x, point.z - pair.Value.z);
            if (delta.sqrMagnitude >= bestDistance)
                continue;
            bestDistance = delta.sqrMagnitude;
            bestCorner = pair.Key.Replace("_WheelController", string.Empty);
        }
        return bestCorner;
    }

    private static void Include(ref Bounds bounds, ref bool initialized, Vector3 point)
    {
        if (!initialized)
        {
            bounds = new Bounds(point, Vector3.zero);
            initialized = true;
        }
        else
        {
            bounds.Encapsulate(point);
        }
    }

    private static Vector2 ReadUv(Vector2[] uvs, int vertexCount, int index) =>
        uvs.Length == vertexCount ? uvs[index] : Vector2.zero;

    private static Mesh CreateBrakeRotor(string name, float radius)
    {
        const int segments = 48;
        const float halfThickness = 0.013f;
        const float innerRadius = 0.078f;
        var vertices = new List<Vector3>(segments * 4);
        var triangles = new List<int>(segments * 24);
        for (var index = 0; index < segments; index++)
        {
            var a = index * Mathf.PI * 2f / segments;
            var b = (index + 1) * Mathf.PI * 2f / segments;
            var start = vertices.Count;
            vertices.Add(new Vector3(-halfThickness, Mathf.Sin(a) * radius, Mathf.Cos(a) * radius));
            vertices.Add(new Vector3(-halfThickness, Mathf.Sin(a) * innerRadius, Mathf.Cos(a) * innerRadius));
            vertices.Add(new Vector3(halfThickness, Mathf.Sin(a) * radius, Mathf.Cos(a) * radius));
            vertices.Add(new Vector3(halfThickness, Mathf.Sin(a) * innerRadius, Mathf.Cos(a) * innerRadius));
            triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
            var next = ((index + 1) % segments) * 4;
            triangles.AddRange(new[] { start, next, start + 2, start + 2, next, next + 2 });
            triangles.AddRange(new[] { start + 1, start + 3, next + 1, start + 3, next + 3, next + 1 });
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh SaveMesh(string folder, string name, Mesh generated)
    {
        var path = folder + "/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            generated.name = name;
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }
        EditorUtility.CopySerialized(generated, mesh);
        UnityEngine.Object.DestroyImmediate(generated);
        mesh.name = name;
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    private static Material CreatePartMaterial(
        Material? source,
        string folder,
        string name,
        Color color,
        float metallic,
        float smoothness)
    {
        if (source == null)
            throw new InvalidOperationException($"Source material for '{name}' is missing.");
        var path = folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(source) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.CopyPropertiesFromMaterial(source);
            material.shader = source.shader;
            material.name = name;
        }
        SetMaterialColor(material, color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreatePlainMaterial(
        string folder,
        string name,
        Color color,
        float metallic,
        float smoothness)
    {
        var path = folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("High Definition Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("HDRP/Lit shader is unavailable for the brake rotors.");
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = shader;
            material.name = name;
        }
        SetMaterialColor(material, color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        if (material.HasProperty("_SupportDecals")) material.SetFloat("_SupportDecals", 0f);
        material.EnableKeyword("_DISABLE_DECALS");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", color);
    }

    private static void AddMeshPart(Transform parent, string name, Mesh mesh, Material material)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    private static Transform? FindChild(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        return null;
    }

    private static bool HasAncestorName(Transform transform, string fragment)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static void AssignWheelVisual(Transform controller, GameObject visualObject)
    {
        foreach (var component in controller.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var visual = serialized.FindProperty("wheel")?.FindPropertyRelative("visual");
            if (visual?.propertyType != SerializedPropertyType.ObjectReference)
                continue;
            visual.objectReferenceValue = visualObject;
            var visualTransform = serialized.FindProperty("wheel")?.FindPropertyRelative("visualTransform");
            if (visualTransform?.propertyType == SerializedPropertyType.ObjectReference)
                visualTransform.objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return;
        }
        throw new InvalidOperationException(
            $"NWH wheel controller '{controller.name}' has no visual reference field.");
    }

    private static void RemoveSourceGeometry(MeshRenderer renderer)
    {
        renderer.enabled = false;
        renderer.sharedMaterials = Array.Empty<Material>();
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter != null) filter.sharedMesh = null;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;
        var separator = folder.LastIndexOf('/');
        if (separator <= 0)
            throw new InvalidOperationException($"Invalid asset folder '{folder}'.");
        EnsureFolder(folder.Substring(0, separator));
        AssetDatabase.CreateFolder(folder.Substring(0, separator), folder.Substring(separator + 1));
    }
}
