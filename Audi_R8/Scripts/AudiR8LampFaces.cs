#nullable enable
using UnityEngine;
using System;
using System.Collections.Generic;

// The source GLB has opaque outer lens covers. Keep their existing opening
// separate from emitter selection, which is authored in Blender vertex groups.
internal static class AudiR8LampFaces
{
    private static float V(Vector2 uv) => 1f - uv.y;
    internal static Mesh OpenLensCovers(Mesh source, out int removedFaces)
    {
        var vertices = source.vertices;
        var uv = source.uv;
        var triangles = source.triangles;
        var parents = new int[vertices.Length];
        var welded = new Dictionary<Vector3Int, int>();
        int Root(int i) { while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; } return i; }
        void Join(int a, int b) => parents[Root(a)] = Root(b);
        for (var i = 0; i < vertices.Length; i++) parents[i] = i;
        for (var i = 0; i < vertices.Length; i++)
        {
            var p = vertices[i] * 100000f;
            var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
            if (welded.TryGetValue(key, out var first)) Join(i, first);
            else welded.Add(key, i);
        }
        for (var i = 0; i < triangles.Length; i += 3)
        {
            Join(triangles[i], triangles[i + 1]); Join(triangles[i], triangles[i + 2]);
        }
        var parts = new Dictionary<int, List<int>>();
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var root = Root(triangles[i]);
            if (!parts.TryGetValue(root, out var faces)) parts.Add(root, faces = new List<int>());
            faces.Add(i);
        }
        var covers = new HashSet<int>();
        foreach (var part in parts)
        {
            if (part.Value.Count < 30 || part.Value.Count > 45) continue;
            var bounds = new Bounds(vertices[triangles[part.Value[0]]], Vector3.zero);
            var plainLens = true;
            foreach (var face in part.Value)
                for (var j = 0; j < 3; j++)
                {
                    var index = triangles[face + j]; bounds.Encapsulate(vertices[index]);
                    var u = uv[index];
                    plainLens &= u.x > .433f && u.x < .474f && V(u) > .708f && V(u) < .726f;
                }
            // Four separate broad, untextured opaque cover components. The
            // small projector frames use this same grey swatch, so UV alone
            // would remove useful geometry. Their topology/bounds differ.
            if (plainLens && bounds.size.x > .35f && bounds.size.y > .08f)
                covers.Add(part.Key);
        }
        var kept = new List<int>();
        for (var i = 0; i < triangles.Length; i += 3)
            if (!covers.Contains(Root(triangles[i])))
            { kept.Add(triangles[i]); kept.Add(triangles[i + 1]); kept.Add(triangles[i + 2]); }
        removedFaces = (triangles.Length - kept.Count) / 3;
        var result = UnityEngine.Object.Instantiate(source);
        result.name = "AudiR8OpenLampCovers";
        result.triangles = kept.ToArray();
        result.RecalculateBounds();
        return result;
    }
}
