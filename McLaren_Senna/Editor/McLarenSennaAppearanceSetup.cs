#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class McLarenSennaAppearanceSetup
{
    private const string MaskFolder = "Assets/Mods/McLaren_Senna/Models/GeneratedMasks";

    public static void Prepare(GameObject root)
    {
        McLarenSennaSupportGeometry.Author(root);
        SeparateLampCovers(root);
        SeparateRearBadges(root);
        var prepared = new Dictionary<Material, Material>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!McLarenSennaMaterials.IsMcLarenRenderer(renderer.transform)) continue;
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null) continue;
                if (!prepared.TryGetValue(source, out var material))
                {
                    var path = AssetDatabase.GetAssetPath(source);
                    // Change only target-owned .mat assets. Imported GLB subassets
                    // need an isolated copy that the existing persistence pass saves.
                    material = path.StartsWith("Assets/Mods/McLaren_Senna/", StringComparison.Ordinal) && path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)
                        ? source : new Material(source) { name = source.name };
                    var mask = PackMask(material);
                    McLarenSennaMaterials.PrepareGameMaterial(renderer, material);
                    if (mask != null)
                    {
                        material.SetTexture("_MaskMap", mask);
                        material.SetTextureScale("_MaskMap", Vector2.one);
                        material.SetFloat("_Metallic", 1f);
                        material.SetFloat("_Smoothness", 1f);
                        material.SetFloat("_SmoothnessRemapMin", 0f);
                        material.SetFloat("_SmoothnessRemapMax", 1f);
                        material.SetFloat("_AORemapMin", 0f);
                        material.SetFloat("_AORemapMax", 1f);
                        material.EnableKeyword("_MASKMAP");
                    }
                    prepared[source] = material;
                    EditorUtility.SetDirty(material);
                }
                materials[index] = material;
            }
            renderer.sharedMaterials = materials;
        }
        Debug.Log($"Senna HDRP appearance prepared: {prepared.Count} target materials.");
    }

    private static void SeparateLampCovers(GameObject root)
    {
        const string meshPath = "Assets/Mods/McLaren_Senna/Models/GeneratedMeshes/McLarenLampCovers.asset";
        const string materialPath = "Assets/Mods/McLaren_Senna/Models/GeneratedMaterials/McLarenLampCover.mat";
        if (root.GetComponentsInChildren<MeshFilter>(true).Any(f => f.name == "McLarenLampCovers_Clear")) return;
        var source = root.GetComponentsInChildren<MeshFilter>(true).Single(f =>
            f.name.Contains("Kit2_Window_Geo") && f.name.EndsWith("_Material1_0", StringComparison.Ordinal));
        var mesh = source.sharedMesh;
        var vertices = mesh.vertices;
        var cabin = new List<int>();
        var covers = new List<int>();
        var triangles = mesh.triangles;
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var center = root.transform.InverseTransformPoint(source.transform.TransformPoint(
                (vertices[triangles[i]] + vertices[triangles[i+1]] + vertices[triangles[i+2]]) / 3f));
            // Measured source islands: headlights z=1.51..2.17; rear lamp/trim
            // covers z<-1.80. The cabin/rear window ends at z=-1.40.
            var target = center.z > 1.45f || center.z < -1.80f ? covers : cabin;
            target.Add(triangles[i]); target.Add(triangles[i+1]); target.Add(triangles[i+2]);
        }
        if (covers.Count == 0 || cabin.Count == 0) throw new InvalidOperationException("Senna lamp-cover/cabin split is empty.");
        Mesh Subset(string name, List<int> selected)
        {
            var indices = selected.Distinct().ToArray();
            var remap = indices.Select((old, index) => new { old, index }).ToDictionary(x => x.old, x => x.index);
            var result = new Mesh { name = name };
            result.vertices = indices.Select(i => vertices[i]).ToArray();
            var normals = mesh.normals; var uv = mesh.uv; var tangents = mesh.tangents;
            if (normals.Length == vertices.Length) result.normals = indices.Select(i => normals[i]).ToArray();
            if (uv.Length == vertices.Length) result.uv = indices.Select(i => uv[i]).ToArray();
            if (tangents.Length == vertices.Length) result.tangents = indices.Select(i => tangents[i]).ToArray();
            result.triangles = selected.Select(i => remap[i]).ToArray();
            result.RecalculateBounds();
            return result;
        }
        var generated = new[] { Subset("McLarenLampCovers_Clear", covers), Subset("McLarenCabinGlass", cabin) };
        var existing = AssetDatabase.LoadAllAssetsAtPath(meshPath).OfType<Mesh>().ToArray();
        for (var i = 0; i < generated.Length; i++)
        {
            var persistent = existing.FirstOrDefault(m => m.name == generated[i].name);
            if (persistent == null)
            {
                if (i == 0 && !System.IO.File.Exists(meshPath)) AssetDatabase.CreateAsset(generated[i], meshPath);
                else AssetDatabase.AddObjectToAsset(generated[i], meshPath);
            }
            else
            {
                EditorUtility.CopySerialized(generated[i], persistent);
                UnityEngine.Object.DestroyImmediate(generated[i]);
                generated[i] = persistent;
            }
            EditorUtility.SetDirty(generated[i]);
        }
        source.sharedMesh = generated[1];
        var host = new GameObject("McLarenLampCovers_Clear") { layer = source.gameObject.layer };
        host.transform.SetParent(source.transform, false);
        host.AddComponent<MeshFilter>().sharedMesh = generated[0];
        var originalRenderer = source.GetComponent<MeshRenderer>();
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.renderingLayerMask = originalRenderer.renderingLayerMask;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(originalRenderer.sharedMaterial) { name = "McLarenLampCover" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        renderer.sharedMaterial = material;
        Debug.Log($"Senna lamp covers separated: exterior={covers.Count / 3} cabin={cabin.Count / 3} triangles; original UVs/transforms retained.");
    }

    private static void SeparateRearBadges(GameObject root)
    {
        const string path = "Assets/Mods/McLaren_Senna/Models/GeneratedMeshes/McLarenRearBadges.asset";
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.name.Contains("Kit2_Badge_Geo") || filter.sharedMesh == null) continue;
            if (AssetDatabase.GetAssetPath(filter.sharedMesh) == path) continue;
            var source = filter.sharedMesh;
            var vertices = source.vertices;
            var adjusted = 0;
            for (var i = 0; i < vertices.Length; i++)
            {
                var point = root.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
                if (point.z > -1.8f) continue;
                // Retain UVs and shape; separate rear lettering from the grille
                // by 4 mm along the vehicle's rearward direction.
                vertices[i] += filter.transform.InverseTransformVector(root.transform.TransformVector(Vector3.back * .004f));
                adjusted++;
            }
            if (adjusted == 0) throw new InvalidOperationException("Rear Senna badge faces were not found.");
            var corrected = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (corrected == null)
            {
                corrected = UnityEngine.Object.Instantiate(source);
                AssetDatabase.CreateAsset(corrected, path);
            }
            else EditorUtility.CopySerialized(source, corrected);
            corrected.name = "McLarenRearBadges";
            corrected.vertices = vertices;
            corrected.RecalculateBounds();
            EditorUtility.SetDirty(corrected);
            filter.sharedMesh = corrected;
            Debug.Log($"Senna rear badges separated from grille: {adjusted} vertices, 4 mm clearance.");
        }
    }

    private static Texture2D? PackMask(Material material)
    {
        if (material.shader == null || !material.shader.name.StartsWith("glTF/", StringComparison.Ordinal)) return null;
        var metallicRoughness = material.GetTexture("metallicRoughnessTexture") as Texture2D;
        var occlusion = material.GetTexture("occlusionTexture") as Texture2D;
        if (metallicRoughness == null && occlusion == null) return null;
        var width = Mathf.Max(metallicRoughness?.width ?? 1, occlusion?.width ?? 1);
        var height = Mathf.Max(metallicRoughness?.height ?? 1, occlusion?.height ?? 1);
        var metallic = material.GetFloat("metallicFactor");
        var roughness = material.GetFloat("roughnessFactor");
        var aoStrength = material.GetFloat("occlusionTexture_strength");
        var pixels = new Color[width * height];
        Texture2D? mrReadable = null;
        Texture2D? aoReadable = null;
        try
        {
            mrReadable = Readable(metallicRoughness);
            aoReadable = Readable(occlusion);
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var u = (x + .5f) / width; var v = (y + .5f) / height;
                    var mr = mrReadable != null ? mrReadable.GetPixelBilinear(u, v) : Color.white;
                    var ao = aoReadable != null ? aoReadable.GetPixelBilinear(u, v).r : 1f;
                    pixels[y * width + x] = new Color(mr.b * metallic, Mathf.Lerp(1f, ao, aoStrength), 1f, 1f - mr.g * roughness);
                }
        }
        finally
        {
            if (mrReadable != null && mrReadable != metallicRoughness) UnityEngine.Object.DestroyImmediate(mrReadable);
            if (aoReadable != null && aoReadable != occlusion) UnityEngine.Object.DestroyImmediate(aoReadable);
        }
        if (!AssetDatabase.IsValidFolder(MaskFolder)) AssetDatabase.CreateFolder("Assets/Mods/McLaren_Senna/Models", "GeneratedMasks");
        var safeName = material.name.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
        var path = MaskFolder + "/" + safeName + "_Mask.asset";
        var packed = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (packed == null)
        {
            packed = new Texture2D(width, height, TextureFormat.RGBA32, true, true) { name = safeName + "_Mask" };
            AssetDatabase.CreateAsset(packed, path);
        }
        else if (packed.width != width || packed.height != height) packed.Reinitialize(width, height);
        packed.SetPixels(pixels); packed.Apply(true, false);
        packed.wrapMode = metallicRoughness?.wrapMode ?? occlusion!.wrapMode;
        packed.filterMode = FilterMode.Trilinear;
        EditorUtility.SetDirty(packed);
        return packed;
    }

    private static Texture2D? Readable(Texture2D? source)
    {
        if (source == null || source.isReadable) return source;
        var previous = RenderTexture.active;
        var buffer = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        try
        {
            Graphics.Blit(source, buffer);
            RenderTexture.active = buffer;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, true);
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            copy.Apply(false, false);
            return copy;
        }
        finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(buffer); }
    }

}
