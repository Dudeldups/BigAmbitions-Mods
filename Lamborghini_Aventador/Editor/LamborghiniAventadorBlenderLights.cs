#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

internal static class LamborghiniAventadorBlenderLights
{
    private const string Models = "Assets/Mods/Lamborghini_Aventador/Models/";

    [Serializable]
    private sealed class Selections
    {
        public int version;
        public string productionSha256 = "";
        public Group[] groups = Array.Empty<Group>();
    }

    [Serializable]
    private sealed class Group
    {
        public string name = "";
        public string source = "";
        public int sourceVertexCount;
        public int sourceFaceCount;
        public int faceCount;
        public int partialNeighborFaces;
    }

    public static void Configure(GameObject root)
    {
        var body = root.transform.Find("AventadorBody");
        var imported = AssetDatabase.LoadAssetAtPath<GameObject>(Models + "AventadorLightOverlays.glb");
        var report = JsonUtility.FromJson<Selections>(File.ReadAllText(Models + "AventadorLightSelections.json"));
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Models + "lamborghini_aventador_svj.glb")))
            .Replace("-", "");
        if (body == null || imported == null || report.version != 1 || report.groups.Length != 11 ||
            !string.Equals(hash, report.productionSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Aventador lamp selections do not match the production GLB.");

        var sources = body.GetComponentsInChildren<MeshRenderer>(true);
        var overlays = imported.GetComponentsInChildren<MeshFilter>(true);
        var sourceFaces = new Dictionary<Mesh, (int UniqueCount, HashSet<(Vector3Int, Vector3Int, Vector3Int)> Geometry)>();
        var groupNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in report.groups)
        {
            if (!groupNames.Add(group.name))
                throw new InvalidOperationException($"Aventador Blender lamp group '{group.name}' is duplicated.");
            var source = sources.Single(renderer => renderer.name == group.source);
            var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var mesh = overlays.Single(filter => filter.name == group.name).sharedMesh;
            if (!sourceFaces.TryGetValue(sourceMesh, out var faces))
            {
                faces = (CountUniqueIndexedFaces(sourceMesh), FaceKeys(sourceMesh).ToHashSet());
                sourceFaces.Add(sourceMesh, faces);
            }
            if (sourceMesh.vertexCount != group.sourceVertexCount || faces.UniqueCount != group.sourceFaceCount ||
                mesh.triangles.Length != group.faceCount * 3)
                throw new InvalidOperationException($"Aventador '{group.name}' face or vertex count differs from the Blender export.");

            if (FaceKeys(mesh).Any(face => !faces.Geometry.Contains(face)))
                throw new InvalidOperationException($"Aventador '{group.name}' does not align with source mesh '{source.name}'.");

            var positions = mesh.vertices.Select(point => root.transform.InverseTransformPoint(source.transform.TransformPoint(point))).ToArray();
            var rear = group.name.StartsWith("Rear_", StringComparison.Ordinal) ||
                group.name.StartsWith("Reverse_", StringComparison.Ordinal) ||
                group.name.StartsWith("High_Mounted_", StringComparison.Ordinal) ||
                group.name is "Indicator_RL" or "Indicator_RR";
            var left = group.name is "Indicator_FL" or "Indicator_RL" or "Indicator_Side_L";
            var right = group.name is "Indicator_FR" or "Indicator_RR" or "Indicator_Side_R";
            if (positions.Any(p => (rear ? p.z >= -1.5f : p.z <= 1.5f) ||
                (left && p.x >= 0f) || (right && p.x <= 0f)))
                throw new InvalidOperationException($"Aventador '{group.name}' has incorrect vehicle-relative bounds.");

            Debug.Log($"Aventador Blender lamps: {group.name} anchors to {source.name}, {group.faceCount} complete faces, " +
                $"{group.partialNeighborFaces} partially selected neighbours excluded.");
            // Unity's native mesh reference survives prefab serialization and
            // nested body cloning. The holder itself has no renderer.
            var holder = new GameObject(LamborghiniAventadorLampGeometry.SourcePrefix + group.name);
            holder.transform.SetParent(source.transform, false);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
        }
    }

    private static int CountUniqueIndexedFaces(Mesh mesh)
    {
        // Blender's mesh importer discards duplicate indexed faces. Unity
        // retains them, including eight duplicate faces in Object_25.
        var triangles = mesh.triangles;
        var faces = new HashSet<(int, int, int)>();
        for (var index = 0; index < triangles.Length; index += 3)
        {
            var a = triangles[index];
            var b = triangles[index + 1];
            var c = triangles[index + 2];
            var min = Mathf.Min(a, Mathf.Min(b, c));
            var max = Mathf.Max(a, Mathf.Max(b, c));
            faces.Add((min, a + b + c - min - max, max));
        }
        return faces.Count;
    }

    private static IEnumerable<(Vector3Int, Vector3Int, Vector3Int)> FaceKeys(Mesh mesh)
    {
        var points = mesh.vertices.Select(point => Vector3Int.RoundToInt(point * 100000f)).ToArray();
        var triangles = mesh.triangles;
        for (var index = 0; index < triangles.Length; index += 3)
        {
            var a = points[triangles[index]];
            var b = points[triangles[index + 1]];
            var c = points[triangles[index + 2]];
            if (Compare(a, b) > 0) (a, b) = (b, a);
            if (Compare(b, c) > 0) (b, c) = (c, b);
            if (Compare(a, b) > 0) (a, b) = (b, a);
            yield return (a, b, c);
        }
    }

    private static int Compare(Vector3Int a, Vector3Int b)
    {
        var result = a.x.CompareTo(b.x);
        if (result == 0) result = a.y.CompareTo(b.y);
        return result == 0 ? a.z.CompareTo(b.z) : result;
    }
}
