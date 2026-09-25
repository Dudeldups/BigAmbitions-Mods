#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Generate actual holes instead of relying on alpha-test mipmaps. The authored
// atlas becomes completely opaque at its 64px mip, even after fixing base alpha.
public static class ChevroletCamaro1967GrilleGeometry
{
    private const float GrilleTop = 136f / 512f;
    private static readonly string[] GrilleNames =
    {
        "camaro_ss:LOD_A_CHASSIS_mm_badges_badges_0",
        "camaro_ss:LOD_A_HEADLIGHT_DOWN_mm_badges_badges_0",
        "camaro_ss:LOD_A_HEADLIGHT_UP_mm_badges_badges_0",
    };

    public static void Repair(GameObject target, GameObject source, Texture2D atlas, string meshFolder)
    {
        if (!AssetDatabase.IsValidFolder(meshFolder))
        {
            var parentFolder = Path.GetDirectoryName(meshFolder)?.Replace('\\', '/');
            var folderName = Path.GetFileName(meshFolder);
            if (string.IsNullOrEmpty(parentFolder) || !AssetDatabase.IsValidFolder(parentFolder))
                throw new InvalidOperationException($"Cannot create Camaro grille mesh folder '{meshFolder}': parent folder '{parentFolder}' is missing.");
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parentFolder, folderName)))
                throw new InvalidOperationException($"Unity failed to create Camaro grille mesh folder '{meshFolder}'.");
        }

        var rectangles = GetBarRectangles(atlas);
        var originals = source.GetComponentsInChildren<MeshFilter>(true);
        var repaired = 0;
        foreach (var filter in target.GetComponentsInChildren<MeshFilter>(true))
        {
            var slot = Array.IndexOf(GrilleNames, filter.name);
            if (slot < 0) continue;
            // Always derive from the original mesh, so repeated repairs are idempotent.
            var original = originals.Single(item => item.name == filter.name).sharedMesh;
            var mesh = Perforate(original, rectangles);
            mesh.name = "CamaroPerforatedGrille_" + slot;
            var path = meshFolder + "/" + mesh.name + ".asset";
            var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (persistent == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                persistent = mesh;
            }
            else
            {
                EditorUtility.CopySerialized(mesh, persistent);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            filter.sharedMesh = persistent;
            EditorUtility.SetDirty(persistent);
            repaired++;
            Debug.Log($"Camaro physical grille: {filter.name}, originalTriangles={original.triangles.Length / 3}, barTriangles={persistent.triangles.Length / 3}; original transform and emblem triangles retained.");
        }
        if (repaired != GrilleNames.Length)
            throw new InvalidOperationException($"Expected three Camaro grille meshes, found {repaired}.");
    }

    private static List<Rect> GetBarRectangles(Texture2D atlas)
    {
        var pixels = atlas.GetPixels32();
        var rows = Mathf.RoundToInt(atlas.height * GrilleTop);
        var rectangles = new List<Rect>();
        var previous = new Dictionary<Vector2Int, int>();
        for (var y = 0; y < rows; y++)
        {
            var current = new Dictionary<Vector2Int, int>();
            for (var x = 0; x < atlas.width;)
            {
                if (pixels[y * atlas.width + x].a <= 154) { x++; continue; }
                var start = x;
                while (x < atlas.width && pixels[y * atlas.width + x].a > 154) x++;
                var run = new Vector2Int(start, x);
                if (previous.TryGetValue(run, out var index))
                {
                    var rect = rectangles[index];
                    rect.yMax = (y + 1f) / atlas.height;
                    rectangles[index] = rect;
                }
                else
                {
                    index = rectangles.Count;
                    rectangles.Add(Rect.MinMaxRect(start / (float)atlas.width, y / (float)atlas.height,
                        x / (float)atlas.width, (y + 1f) / atlas.height));
                }
                current.Add(run, index);
            }
            previous = current;
        }
        if (rectangles.Count == 0) throw new InvalidOperationException("No grille bars found in atlas.");
        return rectangles;
    }

    private readonly struct Vertex
    {
        internal readonly Vector3 Position;
        internal readonly Vector3 Normal;
        internal readonly Vector2 UV;
        internal Vertex(Vector3 position, Vector3 normal, Vector2 uv)
        { Position = position; Normal = normal; UV = uv; }
        internal static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex(
            Vector3.LerpUnclamped(a.Position, b.Position, t),
            Vector3.LerpUnclamped(a.Normal, b.Normal, t),
            Vector2.LerpUnclamped(a.UV, b.UV, t));
    }

    private static Mesh Perforate(Mesh source, List<Rect> rectangles)
    {
        if (source.subMeshCount != 1) throw new InvalidOperationException("Unexpected grille submeshes.");
        var positions = source.vertices;
        var normals = source.normals;
        var uv = source.uv;
        var indices = source.triangles;
        var output = new List<Vertex>();
        var originalArea = 0f;
        var barArea = 0f;
        for (var i = 0; i < indices.Length; i += 3)
        {
            var triangle = new List<Vertex>(3);
            for (var j = 0; j < 3; j++)
            {
                var index = indices[i + j];
                triangle.Add(new Vertex(positions[index], normals[index], uv[index]));
            }
            if (triangle.All(v => v.UV.y >= GrilleTop))
            {
                output.AddRange(triangle); // Locks, badges and other non-grille details.
                continue;
            }
            if (triangle.Any(v => v.UV.y > GrilleTop))
                throw new InvalidOperationException("A triangle crosses the grille atlas boundary.");
            originalArea += Area(triangle[0], triangle[1], triangle[2]);
            var min = Vector2.Min(triangle[0].UV, Vector2.Min(triangle[1].UV, triangle[2].UV));
            var max = Vector2.Max(triangle[0].UV, Vector2.Max(triangle[1].UV, triangle[2].UV));
            foreach (var rect in rectangles)
            {
                if (rect.xMin > max.x || rect.xMax < min.x || rect.yMin > max.y || rect.yMax < min.y) continue;
                var polygon = Clip(triangle, 0, rect.xMin, true);
                polygon = Clip(polygon, 0, rect.xMax, false);
                polygon = Clip(polygon, 1, rect.yMin, true);
                polygon = Clip(polygon, 1, rect.yMax, false);
                for (var j = 1; j + 1 < polygon.Count; j++)
                {
                    var area = Area(polygon[0], polygon[j], polygon[j + 1]);
                    if (area < 1e-12f) continue;
                    barArea += area;
                    output.Add(polygon[0]); output.Add(polygon[j]); output.Add(polygon[j + 1]);
                }
            }
        }
        if (originalArea <= 0f || barArea <= 0f || barArea >= originalArea * 0.9f)
            throw new InvalidOperationException($"Grille perforation did not open the surface: {barArea}/{originalArea}.");
        Debug.Log($"Camaro grille coverage: bars={barArea / originalArea:P1}, physical openings={1f - barArea / originalArea:P1}.");
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(output.Select(v => v.Position).ToList());
        mesh.SetNormals(output.Select(v => v.Normal.normalized).ToList());
        mesh.SetUVs(0, output.Select(v => v.UV).ToList());
        mesh.SetTriangles(Enumerable.Range(0, output.Count).ToArray(), 0);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static float Area(Vertex a, Vertex b, Vertex c) =>
        Vector3.Cross(b.Position - a.Position, c.Position - a.Position).magnitude * 0.5f;

    private static List<Vertex> Clip(List<Vertex> input, int axis, float boundary, bool keepGreater)
    {
        var result = new List<Vertex>();
        if (input.Count == 0) return result;
        var previous = input[input.Count - 1];
        var previousInside = keepGreater ? previous.UV[axis] >= boundary : previous.UV[axis] <= boundary;
        foreach (var current in input)
        {
            var inside = keepGreater ? current.UV[axis] >= boundary : current.UV[axis] <= boundary;
            if (inside != previousInside)
                result.Add(Vertex.Lerp(previous, current,
                    (boundary - previous.UV[axis]) / (current.UV[axis] - previous.UV[axis])));
            if (inside) result.Add(current);
            previous = current;
            previousInside = inside;
        }
        return result;
    }
}
