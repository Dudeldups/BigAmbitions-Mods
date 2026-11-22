using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using Newtonsoft.Json;
using UnityEngine;

// Extract embedded GLB images directly: ShaderGraph import in a graphics-less
// batch can otherwise yield materials with zero serialized texture properties.
public static class MercedesC63CabrioSourceMaterials
{
    private const string Root = "Assets/Mods/Mercedes_C63_Cabrio/Models";
    [Serializable] private class Document { public MaterialData[] materials; public TextureData[] textures; public ImageData[] images; public View[] bufferViews; }
    [Serializable] private class MaterialData { public string name; public Pbr pbrMetallicRoughness; public Map normalTexture; public string alphaMode; public float alphaCutoff = .5f; public bool doubleSided; }
    [Serializable] private class Pbr { public Map baseColorTexture; public Map metallicRoughnessTexture; public float[] baseColorFactor; public float metallicFactor = 1; public float roughnessFactor = 1; }
    [Serializable] private class Map { public int index; public float scale = 1; public Extensions extensions; }
    [Serializable] private class Extensions { public TextureTransform KHR_texture_transform; }
    [Serializable] private class TextureTransform { public float[] scale; public float[] offset; }
    [Serializable] private class TextureData { public int source; }
    [Serializable] private class ImageData { public int bufferView; public string mimeType; }
    [Serializable] private class View { public int byteOffset; public int byteLength; }
    private static Document document;
    private static byte[] bytes;
    private static int binaryOffset;

    public static void Prepare()
    {
        bytes = File.ReadAllBytes(Root + "/2018_mercedes_amg_c63_cabriolet.glb");
        var jsonLength = BitConverter.ToInt32(bytes, 12);
        document = JsonConvert.DeserializeObject<Document>(Encoding.UTF8.GetString(bytes, 20, jsonLength));
        // Missing glTF maps must stay null; Unity JsonUtility synthesizes nested objects at index zero.
        binaryOffset = 20 + jsonLength + 8;
        if (!AssetDatabase.IsValidFolder(Root + "/Textures")) AssetDatabase.CreateFolder(Root, "Textures");
        foreach (var material in document.materials)
        {
            if (material.pbrMetallicRoughness?.baseColorTexture != null) Texture(material.pbrMetallicRoughness.baseColorTexture.index, false);
            if (material.normalTexture != null) Texture(material.normalTexture.index, true);
        }
    }

    public static void Apply(Material material, string sourceName)
    {
        string Normalize(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray());
        var data = document.materials.FirstOrDefault(m => Normalize(m.name) == Normalize(sourceName));
        if (data == null) throw new InvalidOperationException($"No GLB material data for {sourceName}.");
        material.shader = Shader.Find("HDRP/Lit");
        material.SetTexture("_BaseColorMap", null);
        material.SetTexture("_NormalMap", null);
        material.SetTexture("_MaskMap", null);
        var pbr = data.pbrMetallicRoughness;
        var factor = pbr?.baseColorFactor;
        material.SetColor("_BaseColor", factor != null && factor.Length == 4 ? new Color(factor[0], factor[1], factor[2], factor[3]) : Color.white);
        // Source BLEND atlases contain real cutouts, including cabin controls,
        // chassis and engine detail. Use opaque alpha-tested HDRP surfaces so
        // their transparent white atlas padding does not become visible panels.
        // Solid leather/body coverage remains in the separate authored meshes.
        var sourceKey = Normalize(data.name);
        var maskedTexture = pbr?.baseColorTexture != null &&
            (data.alphaMode == "MASK" || data.alphaMode == "BLEND" || sourceKey == "cabriophong1SG1" ||
             sourceKey == "nfsMRimBadgeMax1" || sourceKey == "cabriophong12SG1" ||
             sourceKey == "cabriophong17SG1");
        material.SetFloat("_DoubleSidedEnable", data.doubleSided ? 1f : 0f);
        material.SetFloat("_DoubleSidedNormalMode", 1f); // mirror authored normals on the back face
        material.SetFloat("_CullMode", data.doubleSided ? 0f : 2f);
        material.SetFloat("_CullModeForward", data.doubleSided ? 0f : 2f);
        material.SetFloat("_OpaqueCullMode", data.doubleSided ? 0f : 2f);
        material.SetFloat("_TransparentCullMode", data.doubleSided ? 0f : 2f);
        material.SetFloat("_AlphaCutoffEnable", maskedTexture ? 1f : 0f);
        material.SetFloat("_AlphaCutoff", data.alphaMode == "MASK" ? data.alphaCutoff : .15f);
        material.SetFloat("_Metallic", pbr?.metallicFactor ?? 1f);
        material.SetFloat("_Smoothness", 1f - (pbr?.roughnessFactor ?? 1f));
        if (pbr?.baseColorTexture != null)
        {
            material.SetTexture("_BaseColorMap", Texture(pbr.baseColorTexture.index, false));
            ApplyTextureTransform(material, "_BaseColorMap", pbr.baseColorTexture);
        }
        if (pbr?.metallicRoughnessTexture != null)
            material.SetTexture("_MaskMap", Mask(pbr.metallicRoughnessTexture.index, pbr.metallicFactor, pbr.roughnessFactor));
        if (data.normalTexture != null)
        {
            material.SetTexture("_NormalMap", Texture(data.normalTexture.index, true));
            material.SetFloat("_NormalScale", data.normalTexture.scale);
            ApplyTextureTransform(material, "_NormalMap", data.normalTexture);
        }
        if (pbr?.baseColorTexture != null && material.GetTexture("_BaseColorMap") == null)
            throw new InvalidOperationException($"GLB base texture lost for {sourceName}.");
    }

    private static void ApplyTextureTransform(Material material, string property, Map map)
    {
        var transform = map.extensions?.KHR_texture_transform;
        material.SetTextureScale(property, transform?.scale?.Length == 2
            ? new Vector2(transform.scale[0], transform.scale[1]) : Vector2.one);
        material.SetTextureOffset(property, transform?.offset?.Length == 2
            ? new Vector2(transform.offset[0], transform.offset[1]) : Vector2.zero);
    }

    private static byte[] ImageBytes(int index)
    {
        var image = document.images[document.textures[index].source];
        var view = document.bufferViews[image.bufferView]; var data = new byte[view.byteLength];
        Buffer.BlockCopy(bytes, binaryOffset + view.byteOffset, data, 0, view.byteLength);
        return data;
    }

    private static Texture2D Mask(int index, float metallic, float roughness)
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!source.LoadImage(ImageBytes(index))) throw new InvalidOperationException("Could not decode GLB metallic/roughness image.");
        var pixels = source.GetPixels();
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = new Color(pixels[i].b * metallic, 1f, 1f, 1f - pixels[i].g * roughness);
        var mask = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, true);
        mask.name = "C63_HDRP_Mask_" + index; mask.SetPixels(pixels); mask.Apply();
        UnityEngine.Object.DestroyImmediate(source);
        var path = Root + "/Textures/" + mask.name + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (saved == null) { AssetDatabase.CreateAsset(mask, path); return mask; }
        EditorUtility.CopySerialized(mask, saved); UnityEngine.Object.DestroyImmediate(mask);
        EditorUtility.SetDirty(saved); return saved;
    }

    private static Texture2D Texture(int index, bool normal)
    {
        var image = document.images[document.textures[index].source];
        var path = Root + "/Textures/C63_" + document.textures[index].source.ToString("D2") + (normal ? "_Normal" : "_Color") + (image.mimeType == "image/jpeg" ? ".jpg" : ".png");
        var data = ImageBytes(index);
        if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(data))
        {
            File.WriteAllBytes(path, data); AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        var desiredType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != desiredType || importer.sRGBTexture == normal || importer.maxTextureSize < 2048 || (!normal && !importer.alphaIsTransparency))
        {
            importer.textureType = desiredType; importer.sRGBTexture = !normal;
            importer.maxTextureSize = 2048; importer.mipmapEnabled = true; importer.alphaIsTransparency = !normal; importer.SaveAndReimport();
        }
        if (!normal && index == 8 && (!importer.mipMapsPreserveCoverage ||
            Math.Abs(importer.alphaTestReferenceValue - .15f) > .001f ||
            importer.textureCompression != TextureImporterCompression.Uncompressed))
        {
            importer.mipMapsPreserveCoverage = true; importer.alphaTestReferenceValue = .15f;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
