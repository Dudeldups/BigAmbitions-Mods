#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// The imported EngineA mesh also contains front reservoirs and rear supports.
// Partition whole welded triangle islands, retaining the central engine.
public static class McLarenSennaSupportGeometry
{
    private const string Path = "Assets/Mods/McLaren_Senna/Models/GeneratedMeshes/McLarenImpactSupports.asset";
    public static void Author(GameObject root)
    {
        if (root.GetComponentsInChildren<MeshFilter>(true).Any(f => f.name == "McLarenDamageSupports_Front")) return;
        var source = root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.sharedMesh != null && f.name.Contains("Kit2_Engine_Geo"));
        var mesh = source.sharedMesh;
        if (mesh.subMeshCount != 1) throw new InvalidOperationException("Unexpected EngineA submesh layout.");
        var points = mesh.vertices.Select(p => root.transform.InverseTransformPoint(source.transform.TransformPoint(p))).ToArray();
        var triangles = mesh.triangles;
        var parent = Enumerable.Range(0, points.Length).ToArray();
        int Find(int v) { while (parent[v] != v) { parent[v] = parent[parent[v]]; v = parent[v]; } return v; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }
        var welded = new Dictionary<Vector3Int, int>();
        for (var i = 0; i < points.Length; i++)
        {
            var key = new Vector3Int(Mathf.RoundToInt(points[i].x * 100000f), Mathf.RoundToInt(points[i].y * 100000f), Mathf.RoundToInt(points[i].z * 100000f));
            if (welded.TryGetValue(key, out var other)) Union(i, other); else welded.Add(key, i);
        }
        for (var i = 0; i < triangles.Length; i += 3) { Union(triangles[i], triangles[i+1]); Union(triangles[i], triangles[i+2]); }
        var bounds = new Dictionary<int, Bounds>();
        for (var i = 0; i < points.Length; i++)
        {
            var id = Find(i);
            if (!bounds.TryGetValue(id, out var b)) b = new Bounds(points[i], Vector3.zero);
            b.Encapsulate(points[i]); bounds[id] = b;
        }
        var regions = new[] { new List<int>(), new List<int>(), new List<int>() };
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var b = bounds[Find(triangles[i])];
            var region = b.center.z >= 1.35f ? 0 : b.center.z <= -1.35f ? 1 : 2;
            regions[region].AddRange(new[] { triangles[i], triangles[i+1], triangles[i+2] });
        }
        if (regions.Any(r => r.Count == 0) || regions.Sum(r => r.Count) != triangles.Length)
            throw new InvalidOperationException("Impact support partition is incomplete.");
        var names = new[] { "McLarenDamageSupports_Front", "McLarenDamageSupports_Rear", "McLarenEngineCore" };
        var stored = AssetDatabase.LoadAllAssetsAtPath(Path).OfType<Mesh>().ToArray();
        var generated = new Mesh[3];
        for (var i = 0; i < 3; i++)
        {
            var indices = regions[i].Distinct().ToArray();
            var remap = indices.Select((original, index) => new { original, index }).ToDictionary(x => x.original, x => x.index);
            var subset = new Mesh { name = names[i], indexFormat = IndexFormat.UInt32 };
            subset.vertices = indices.Select(v => mesh.vertices[v]).ToArray();
            var normals = mesh.normals; var tangents = mesh.tangents; var colors = mesh.colors;
            if (normals.Length == points.Length) subset.normals = indices.Select(v => normals[v]).ToArray();
            if (tangents.Length == points.Length) subset.tangents = indices.Select(v => tangents[v]).ToArray();
            if (colors.Length == points.Length) subset.colors = indices.Select(v => colors[v]).ToArray();
            for (var channel = 0; channel < 8; channel++)
            {
                var uv = new List<Vector4>(); mesh.GetUVs(channel, uv);
                if (uv.Count == points.Length) subset.SetUVs(channel, indices.Select(v => uv[v]).ToList());
            }
            subset.triangles = regions[i].Select(v => remap[v]).ToArray(); subset.RecalculateBounds();
            var persistent = stored.FirstOrDefault(m => m.name == names[i]);
            if (persistent == null) { if (i == 0 && !System.IO.File.Exists(Path)) AssetDatabase.CreateAsset(subset, Path); else AssetDatabase.AddObjectToAsset(subset, Path); persistent = subset; }
            else { EditorUtility.CopySerialized(subset, persistent); UnityEngine.Object.DestroyImmediate(subset); }
            generated[i] = persistent; EditorUtility.SetDirty(persistent);
        }
        var originalRenderer = source.GetComponent<MeshRenderer>();
        source.sharedMesh = generated[2];
        for (var i = 0; i < 2; i++)
        {
            var host = new GameObject(names[i]) { layer = source.gameObject.layer }; host.transform.SetParent(source.transform, false);
            host.AddComponent<MeshFilter>().sharedMesh = generated[i];
            var renderer = host.AddComponent<MeshRenderer>(); renderer.sharedMaterials = originalRenderer.sharedMaterials;
            renderer.renderingLayerMask = originalRenderer.renderingLayerMask; renderer.shadowCastingMode = originalRenderer.shadowCastingMode;
            renderer.receiveShadows = originalRenderer.receiveShadows; renderer.lightProbeUsage = originalRenderer.lightProbeUsage; renderer.reflectionProbeUsage = originalRenderer.reflectionProbeUsage;
        }
        Debug.Log($"Senna impact supports authored from {bounds.Count} welded islands: front={generated[0].vertexCount}, rear={generated[1].vertexCount}, engineCore={generated[2].vertexCount}; triangles={triangles.Length/3}, all UV channels/normals/tangents retained, no triangles lost or duplicated.");
    }
}
