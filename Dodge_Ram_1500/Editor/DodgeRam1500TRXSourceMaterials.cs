#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

// The imported ShaderGraph can be GraphErrorShader2 and expose no properties.
// Reconstruct from the GLB source instead of reading that broken material.
public static class DodgeRam1500TRXSourceMaterials
{
    private const string Root = "Assets/Mods/Dodge_Ram_1500/Models/";
    [Serializable] private class Document { public Definition[] materials = Array.Empty<Definition>(); public Image[] images = Array.Empty<Image>(); public TextureReference[] textures = Array.Empty<TextureReference>(); public View[] bufferViews = Array.Empty<View>(); }
    [Serializable] private class Definition { public string name = ""; public string alphaMode = "OPAQUE"; public bool doubleSided; public Pbr pbrMetallicRoughness = new Pbr(); public Map? normalTexture; public Extensions extensions = new Extensions(); }
    [Serializable] private class Pbr { public float[] baseColorFactor = {1f,1f,1f,1f}; public float metallicFactor = 1f; public float roughnessFactor = 1f; public Map? baseColorTexture; public Map? metallicRoughnessTexture; }
    [Serializable] private class Map { public int index; public float scale = 1f; }
    [Serializable] private class Extensions { public Clearcoat KHR_materials_clearcoat = new Clearcoat(); }
    [Serializable] private class Clearcoat { public float clearcoatFactor; }
    [Serializable] private class Image { public int bufferView; public string mimeType = ""; }
    [Serializable] private class TextureReference { public int source; }
    [Serializable] private class View { public int byteOffset; public int byteLength; }
    private static Document? document;
    private static byte[] binary = Array.Empty<byte>();
    private static readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();

    private static void ReadSource()
    {
        using var reader = new BinaryReader(File.OpenRead(Root + "2021_ram_1500_trx.glb"));
        if (reader.ReadUInt32() != 0x46546c67 || reader.ReadUInt32() != 2) throw new InvalidDataException("TRX GLB header");
        reader.ReadUInt32();
        var count = reader.ReadInt32();
        if (reader.ReadUInt32() != 0x4e4f534a) throw new InvalidDataException("TRX GLB JSON chunk");
        document = JsonConvert.DeserializeObject<Document>(Encoding.UTF8.GetString(reader.ReadBytes(count))) ?? throw new InvalidDataException("TRX GLB material definitions missing");
        count = reader.ReadInt32();
        if (reader.ReadUInt32() != 0x004e4942) throw new InvalidDataException("TRX GLB binary chunk");
        binary = reader.ReadBytes(count);
    }

    public static void Begin()
    {
        ReadSource(); textures.Clear();
        if (!AssetDatabase.IsValidFolder(Root + "SourceTextures")) AssetDatabase.CreateFolder(Root.TrimEnd('/'), "SourceTextures");
        var normals = new HashSet<int>(document!.materials.Where(m => m.normalTexture != null).Select(m => m.normalTexture!.index));
        for (var i = 0; i < document.textures.Length; i++)
        {
            var image = document.images[document.textures[i].source];
            if (image.mimeType != "image/png") throw new InvalidDataException("Unexpected TRX image format");
            var view = document.bufferViews[image.bufferView];
            var path = Root + $"SourceTextures/TRXTexture{i:D2}.png";
            WriteChanged(path, binary.Skip(view.byteOffset).Take(view.byteLength).ToArray());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var normal = normals.Contains(i);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal && i != 25;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.maxTextureSize = 2048; importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            textures.Add(i, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }
        Debug.Log($"TRX restored original GLB material definitions={document.materials.Length}, textures={textures.Count}.");
    }

    public static void Apply(Material material, string sourceName)
    {
        var data = document!.materials.Single(m => m.name == sourceName);
        var shader = Shader.Find("HDRP/Lit") ?? throw new InvalidOperationException("TRX HDRP/Lit unavailable");
        // Reset imported shader properties, pass overrides and keywords together.
        var clean = new Material(shader);
        material.shader = shader; material.CopyPropertiesFromMaterial(clean);
        UnityEngine.Object.DestroyImmediate(clean);
        material.shaderKeywords = Array.Empty<string>();
        var pbr = data.pbrMetallicRoughness;
        var color = pbr.baseColorFactor;
        material.SetColor("_BaseColor", new Color(color[0], color[1], color[2], color[3]));
        material.SetTexture("_BaseColorMap", pbr.baseColorTexture == null ? null : textures[pbr.baseColorTexture.index]);
        material.SetTexture("_NormalMap", data.normalTexture == null ? null : textures[data.normalTexture.index]);
        material.SetFloat("_NormalScale", data.normalTexture?.scale ?? 1f);
        material.SetFloat("_Metallic", pbr.metallicFactor);
        material.SetFloat("_Smoothness", 1f - pbr.roughnessFactor);
        material.SetFloat("_CoatMask", data.extensions.KHR_materials_clearcoat.clearcoatFactor);
        material.SetFloat("_DoubleSidedEnable", data.doubleSided ? 1f : 0f);
        material.SetFloat("_DoubleSidedNormalMode", 1f);
        material.SetVector("_DoubleSidedConstants", new Vector4(1f, 1f, -1f, 0f));
        material.SetFloat("_CullMode", data.doubleSided ? 0f : 2f);
        material.SetFloat("_CullModeForward", data.doubleSided ? 0f : 2f);
        material.SetTexture("_MaskMap", pbr.metallicRoughnessTexture == null ? null : BuildMask(pbr));
        // Badge alpha is a cutout, not a see-through cabin or lamp lens.
        var cutout = data.alphaMode != "OPAQUE" && sourceName.Contains("Badge");
        material.SetFloat("_AlphaCutoffEnable", cutout ? 1f : 0f);
        material.SetFloat("_AlphaCutoff", .3f);
        DodgeRam1500TRXMaterials.PrepareOpaqueMaterial(material);
        if (sourceName.Contains("Window") || sourceName == "ORANGE_GLASS" || sourceName == "RED_GLASS")
            DodgeRam1500TRXMaterials.PrepareClearMaterial(material, sourceName.Contains("Window"));
        ValidateMaps(material, data);
        EditorUtility.SetDirty(material);
    }

    public static void ValidateAllSourceMaterials()
    {
        if (document == null)
        {
            ReadSource(); textures.Clear();
            for (var i = 0; i < document!.textures.Length; i++)
                textures.Add(i, AssetDatabase.LoadAssetAtPath<Texture2D>(Root + $"SourceTextures/TRXTexture{i:D2}.png") ??
                    throw new InvalidOperationException("TRX extracted texture missing: " + i));
        }
        foreach (var data in document.materials)
        {
            var matches = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Mods/Dodge_Ram_1500/Models/GeneratedMaterials" })
                .Select(guid => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(material => material.name.EndsWith("_" + data.name, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("TRX material definition ownership: " + data.name);
            ValidateMaps(matches[0], data);
        }
        foreach (var pair in textures)
        {
            var normal = document.materials.Any(data => data.normalTexture?.index == pair.Key);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(pair.Value));
            if (importer.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default) ||
                importer.sRGBTexture != (!normal && pair.Key != 25))
                throw new InvalidOperationException("TRX texture color/normal role: " + pair.Key);
        }
        Debug.Log("TRX source material matrix passed: all 26 definitions, exact map identities/absence, all 34 texture import roles.");
    }

    private static void ValidateMaps(Material material, Definition data)
    {
        var pbr = data.pbrMetallicRoughness;
        if (material.GetTexture("_BaseColorMap") != (pbr.baseColorTexture == null ? null : textures[pbr.baseColorTexture.index]) ||
            material.GetTexture("_NormalMap") != (data.normalTexture == null ? null : textures[data.normalTexture.index]) ||
            material.GetTexture("_MaskMap") != (pbr.metallicRoughnessTexture == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "SourceTextures/TRXLightMask.png")))
            throw new InvalidOperationException("TRX source map mismatch: " + data.name);
    }

    private static Texture2D BuildMask(Pbr pbr)
    {
        var image = document!.images[document.textures[pbr.metallicRoughnessTexture!.index].source];
        var view = document.bufferViews[image.bufferView];
        var raw = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        raw.LoadImage(binary.Skip(view.byteOffset).Take(view.byteLength).ToArray());
        var pixels = raw.GetPixels();
        for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color(pixels[i].b * pbr.metallicFactor, 1f, 1f, 1f - pixels[i].g * pbr.roughnessFactor);
        raw.SetPixels(pixels); raw.Apply();
        var path = Root + "SourceTextures/TRXLightMask.png";
        WriteChanged(path, raw.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(raw);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture = false; importer.maxTextureSize = 2048; importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void WriteChanged(string path, byte[] bytes)
    {
        if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
    }
}
