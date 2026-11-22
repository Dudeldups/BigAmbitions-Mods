using System;
using System.Collections.Generic;
using UnityEngine;

// Topology selection in vehicle coordinates, welded across UV/normal seams.
public static class MercedesC63CabrioMeshParts
{
    // Geometry selections were measured when the body underside was at Y=0.
    // Convert only selection bounds; actual meshes stay in their fitted position.
    private const float LegacyBodyHeight = 1.4021f;
    private const float GroundClearance = .14f;
    private const float BodyHeightRatio = (LegacyBodyHeight - GroundClearance) / LegacyBodyHeight;
    public static Vector3 FromLegacyBodyPoint(Vector3 point) =>
        MercedesC63CabrioTuning.BodyPoint(new Vector3(point.x, point.y * BodyHeightRatio + GroundClearance, point.z));
    public static Bounds ToLegacyBodyBounds(Bounds bounds)
    {
        var inverse = MercedesC63CabrioTuning.BodyStance.inverse;
        var unposed = new Bounds(inverse.MultiplyPoint3x4(bounds.center), Vector3.zero);
        for (var corner = 0; corner < 8; corner++)
            unposed.Encapsulate(inverse.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f))));
        return ToLegacyUnposedBodyBounds(unposed);
    }
    public static Bounds ToLegacyUnposedBodyBounds(Bounds bounds)
    {
        var min = bounds.min; var max = bounds.max;
        min.y = (min.y - GroundClearance) / BodyHeightRatio;
        max.y = (max.y - GroundClearance) / BodyHeightRatio;
        bounds.SetMinMax(min, max); return bounds;
    }

    public static List<int> Select(Mesh mesh, Matrix4x4 toVehicle, Func<Bounds, bool> include)
    {
        var vertices = mesh.vertices;
        var parents = new int[vertices.Length];
        var weld = new Dictionary<Vector3Int, int>();
        var positions = new Vector3[vertices.Length];
        for (var i = 0; i < vertices.Length; i++) parents[i] = i;
        for (var i = 0; i < vertices.Length; i++)
        {
            var v = positions[i] = toVehicle.MultiplyPoint3x4(vertices[i]);
            var key = new Vector3Int(Mathf.RoundToInt(v.x * 10000), Mathf.RoundToInt(v.y * 10000), Mathf.RoundToInt(v.z * 10000));
            if (weld.TryGetValue(key, out var other)) parents[Find(parents, i)] = Find(parents, other);
            else weld[key] = i;
        }
        var triangles = mesh.triangles;
        for (var i = 0; i < triangles.Length; i += 3)
        {
            parents[Find(parents, triangles[i])] = Find(parents, triangles[i+1]);
            parents[Find(parents, triangles[i+1])] = Find(parents, triangles[i+2]);
        }
        var bounds = new Dictionary<int, Bounds>();
        for (var i = 0; i < vertices.Length; i++)
        {
            var root = Find(parents, i);
            if (!bounds.TryGetValue(root, out var b)) b = new Bounds(positions[i], Vector3.zero);
            b.Encapsulate(positions[i]); bounds[root] = b;
        }
        var result = new List<int>();
        for (var i = 0; i < triangles.Length; i += 3)
            if (include(bounds[Find(parents, triangles[i])]))
            { result.Add(triangles[i]); result.Add(triangles[i+1]); result.Add(triangles[i+2]); }
        return result;
    }

    public static Mesh Extract(Mesh source, List<int> triangles, string name)
    {
        var map = new Dictionary<int,int>(); var indices = new List<int>(); var used = new List<int>();
        foreach (var old in triangles)
        {
            if (!map.TryGetValue(old, out var next)) { next = used.Count; map[old] = next; used.Add(old); }
            indices.Add(next);
        }
        var mesh = new Mesh { name = name, indexFormat = source.indexFormat };
        mesh.vertices = Remap(source.vertices, used);
        if (source.normals.Length == source.vertexCount) mesh.normals = Remap(source.normals, used);
        if (source.tangents.Length == source.vertexCount) mesh.tangents = Remap(source.tangents, used);
        if (source.colors32.Length == source.vertexCount) mesh.colors32 = Remap(source.colors32, used);
        if (source.uv.Length == source.vertexCount) mesh.uv = Remap(source.uv, used);
        if (source.uv2.Length == source.vertexCount) mesh.uv2 = Remap(source.uv2, used);
        mesh.SetTriangles(indices, 0); mesh.RecalculateBounds(); return mesh;
    }
    private static T[] Remap<T>(T[] source, List<int> used)
    { var result = new T[used.Count]; for (var i = 0; i < used.Count; i++) result[i] = source[used[i]]; return result; }
    private static int Find(int[] parents, int i)
    { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
}
