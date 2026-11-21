#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Prepares the Bentley's imported appearance on an authoring instance.
/// Call before the setup pipeline persists generated materials so its existing
/// persistence pass can save the isolated copies with the rest of the model.
/// </summary>
public static class BentleyContinentalGTSpeedAppearanceSetup
{
    private const string VisualRootName = "BentleyVisual";
    private const string BodyPaintMarker = "Paint";
    private const string OemPaintMarker = "OEMPaint";
    private const string CaliperPaintMarker = "CaliperAZonePaint";
    private const string CabinGlassMarker = "GlassMtl";
    private const string RedLensMarker = "GlassRed";
    private const string AmberLensMarker = "GlassAmber";
    private const string VehiclePaintShaderPath = "Assets/_BaDependencies/Shaders/Vehicles/SH_Vehicle.shadergraph";
    private const string VehicleTintProperty = "Color_3d0f0cdbe6b74be28a1a5be5bab71dea";
    private const string VehicleFresnelColorProperty = "Color_f78fac473bac467092fb27521e9f71ea";
    private const string VehicleFresnelPowerProperty = "Vector1_481fa2a8a5e94165a039319bfd512b76";
    private const string VehicleMetAOSmoothTexture = "Texture2D_196dcf5fb385498aa0919d56d7cd684d";
    private const string VehicleDiffuseMaskTexture = "Texture2D_3d0babbacbb04731a784ae3943688e65";
    private const string VehicleNormalTexture = "Texture2D_9b32f246716c41009146a43593b55e3f";
    private const string NeutralMetAOSmoothPath = "Assets/Mods/Bentley_Continental_GT_Speed/Models/GeneratedMaterials/Textures/BentleyPaint_MetAOSmooth.asset";
    private const string NeutralDiffuseMaskPath = "Assets/Mods/Bentley_Continental_GT_Speed/Models/GeneratedMaterials/Textures/BentleyPaint_DiffuseMask.asset";
    private const string NeutralNormalPath = "Assets/Mods/Bentley_Continental_GT_Speed/Models/GeneratedMaterials/Textures/BentleyPaint_Normal.asset";
    private const string BadgeBMarker = "BadgeB";
    private const string BadgeModelPath = "Assets/Mods/Bentley_Continental_GT_Speed/Models/2025_bentley_continental_gt_speed.glb";
    private const string GeneratedTextureFolder = "Assets/Mods/Bentley_Continental_GT_Speed/Models/GeneratedMaterials/Textures";
    private const string BadgeBAlphaTexturePath = "Assets/Mods/Bentley_Continental_GT_Speed/Models/GeneratedMaterials/Textures/BentleyBadgeB_Alpha.asset";
    private const byte BadgeBackgroundLuminanceCutoff = 96;
    private const uint DecalRenderingLayerMask = 0x0000FF00u;

    /// <summary>
    /// Configures target-owned body/caliper paint and authored glass/lens materials.
    /// Source GLB materials are never edited; isolated material copies are
    /// assigned to this model root for the caller's normal asset-persistence pass.
    /// </summary>
    public static void Apply(GameObject root)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));

        var visualRoot = string.Equals(root.name, VisualRootName, StringComparison.Ordinal)
            ? root.transform
            : root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => string.Equals(
                    transform.name, VisualRootName, StringComparison.Ordinal));
        if (visualRoot == null)
            throw new InvalidOperationException(
                $"Bentley appearance setup could not find the '{VisualRootName}' model root below '{root.name}'.");

        var hdrpLit = ResolveHdrpLitShader();
        if (hdrpLit == null)
            throw new InvalidOperationException(
                "Bentley appearance setup cannot continue because the HDRP/Lit shader is unavailable.");
        var vehiclePaint = AssetDatabase.LoadAssetAtPath<Shader>(VehiclePaintShaderPath);
        if (vehiclePaint == null)
            throw new InvalidOperationException(
                $"Bentley appearance setup cannot load the game's vehicle paint shader at '{VehiclePaintShaderPath}'.");
        var paintTextures = EnsurePaintTextures();
        var appearanceRenderers = GetAppearanceRenderers(root, visualRoot);

        var prepared = new Dictionary<Material, Material>();
        var bodySlots = 0;
        var caliperPaintSlots = 0;
        var glassSlots = 0;
        var lensSlots = 0;
        var fixedSlots = 0;

        foreach (var renderer in appearanceRenderers)
        {
            var materials = renderer.sharedMaterials;
            var changed = false;
            var containsBodyPaint = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null) continue;

                var materialName = NormalizeMaterialName(source.name);
                var marker = GetSourceMarker(materialName);
                var role = GetRole(marker);
                var alreadyPrepared = IsPreparedMaterial(materialName);
                if (role == MaterialRole.CaliperPaint && root.transform != visualRoot &&
                    !IsLiveExtractedCaliperRenderer(renderer))
                    continue;
                containsBodyPaint |= role == MaterialRole.BodyPaint;

                if (!prepared.TryGetValue(source, out var material))
                {
                    var metallic = GetFloat(source, "metallicFactor", "_Metallic", 0f);
                    var roughness = GetFloat(source, "roughnessFactor", null, 0.5f);
                    var alphaCutoff = GetFloat(source, "alphaCutoff", "_AlphaCutoff", 0.5f);
                    material = alreadyPrepared ? source : new Material(source);
                    material.name = $"Bentley_{marker}_Prepared";
                    material.shader = UsesVehiclePaintShader(role) ? vehiclePaint : hdrpLit;
                    CopyBaseColorTexture(source, material);
                    CopyNormalTexture(source, material);
                    CopyBaseColor(source, material, role);
                    if (UsesVehiclePaintShader(role))
                        ConfigureVehiclePaint(material, paintTextures);
                    else if (role == MaterialRole.CabinGlass || role == MaterialRole.ColoredLens)
                        ConfigureTransparent(material, role == MaterialRole.CabinGlass
                        ? new Color(0.62f, 0.70f, 0.78f, 0.02f)
                            : null);
                    else
                        ConfigureOpaque(material, metallic, roughness,
                            marker == "Grille1A" || marker == "Grille2A" || marker == BadgeBMarker,
                            marker == BadgeBMarker ? 0.5f : alphaCutoff);

                    if (marker == BadgeBMarker)
                        ConfigureBadgeTransparency(material);

                    prepared.Add(source, material);
                }

                materials[index] = material;
                changed = true;
                if (role == MaterialRole.BodyPaint) bodySlots++;
                else if (role == MaterialRole.CaliperPaint) caliperPaintSlots++;
                else if (role == MaterialRole.CabinGlass) glassSlots++;
                else if (role == MaterialRole.ColoredLens) lensSlots++;
                else fixedSlots++;
            }

            if (changed) renderer.sharedMaterials = materials;
            if (containsBodyPaint)
                renderer.renderingLayerMask &= ~DecalRenderingLayerMask;
        }

        if (bodySlots == 0)
            throw new InvalidOperationException(
                "Bentley appearance setup found no exact Paint/OEMPaint body material slots.");

        var invalidMaterials = appearanceRenderers
            .Where(renderer => renderer != null && renderer.enabled &&
                               renderer.gameObject.activeInHierarchy)
            .SelectMany(renderer => renderer.sharedMaterials
                .Where(material => material != null &&
                    !(root.transform != visualRoot &&
                      GetRole(GetSourceMarker(NormalizeMaterialName(material.name))) == MaterialRole.CaliperPaint &&
                      !IsLiveExtractedCaliperRenderer(renderer)) &&
                    material.shader != (UsesVehiclePaintShader(
                        GetRole(GetSourceMarker(NormalizeMaterialName(material.name))))
                        ? vehiclePaint
                        : hdrpLit))
                .Select(material => $"{renderer.name}/{material.name} ({material.shader?.name ?? "<missing>"})"))
            .Take(8)
            .ToArray();
        if (invalidMaterials.Length > 0)
            throw new InvalidOperationException(
                "Bentley appearance setup left visible materials on the wrong role-specific shader: " +
                string.Join(", ", invalidMaterials));

        foreach (var material in prepared.Values)
        {
            if (material.HasProperty(VehicleTintProperty))
                VerifyPaintMaterial(material, "prepared appearance material");
            EditorUtility.SetDirty(material);
        }
        Debug.Log(
            $"Bentley appearance prepared: body paint slots={bodySlots}, transparent glass slots={glassSlots}, " +
            $"caliper paint slots={caliperPaintSlots}, colored lens slots={lensSlots}, " +
            $"fixed trim/interior/wheel slots={fixedSlots}; " +
            "imported source materials remain untouched. " +
            "Lamp-function assignment is not inferred from these materials; no mirror-specific source material exists.");
    }

    private static Renderer[] GetAppearanceRenderers(GameObject root, Transform visualRoot)
    {
        var renderers = visualRoot.GetComponentsInChildren<Renderer>(true).ToList();
        if (root.transform == visualRoot)
            return renderers.ToArray();

        // Wheel geometry extraction moves caliper pieces to upright roots that
        // are siblings of BentleyVisual. Include only the exact authored caliper
        // paint material outside the visual root so donor and chassis renderers
        // elsewhere on the vehicle root remain untouched during repair setup.
        renderers.AddRange(root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer != null && IsLiveExtractedCaliperRenderer(renderer) &&
                               !renderer.transform.IsChildOf(visualRoot) &&
                               renderer.sharedMaterials.Any(IsCaliperPaintMaterial)));
        return renderers.Distinct().ToArray();
    }

    private static bool IsLiveExtractedCaliperRenderer(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeSelf ||
            renderer.sharedMaterials.Length != 1)
            return false;

        for (var node = renderer.transform.parent; node != null; node = node.parent)
        {
            if (!node.gameObject.activeSelf) return false;
            if (node.name == "FrontLeft_UprightVisual" || node.name == "FrontRight_UprightVisual" ||
                node.name == "RearLeft_UprightVisual" || node.name == "RearRight_UprightVisual")
                return true;
        }
        return false;
    }

    private static bool IsCaliperPaintMaterial(Material? material)
    {
        if (material == null)
            return false;

        var name = NormalizeMaterialName(material.name);
        return string.Equals(name, CaliperPaintMarker, StringComparison.Ordinal) ||
               string.Equals(name, "Bentley_CaliperAZonePaint_Prepared", StringComparison.Ordinal) ||
               string.Equals(name, "BentleyMaterial_003", StringComparison.Ordinal);
    }

    /// <summary>
    /// Validates that the exact BadgeB atlas has a persistent exterior-background mask
    /// and that only its material uses opaque alpha clipping.
    /// </summary>
    public static void VerifyBadgeTransparency(Material material, string owner)
    {
        if (material == null)
            throw new InvalidOperationException($"Bentley badge validation found a null material on {owner}.");

        var marker = GetSourceMarker(NormalizeMaterialName(material.name));
        if (!string.Equals(marker, BadgeBMarker, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Bentley badge validation expected the exact {BadgeBMarker} material on {owner}, " +
                $"found '{material.name}'.");

        if (!material.HasProperty("_BaseColorMap") ||
            material.GetTexture("_BaseColorMap") is not Texture2D badgeTexture)
            throw new InvalidOperationException(
                $"Bentley badge validation found no HDRP base-color atlas on {owner}/{material.name}.");

        var texturePath = AssetDatabase.GetAssetPath(badgeTexture);
        if (!texturePath.StartsWith(GeneratedTextureFolder + "/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Bentley badge validation found a non-persistent or foreign alpha atlas on " +
                $"{owner}/{material.name}: '{texturePath}'.");

        if (!material.HasProperty("_AlphaCutoffEnable") ||
            !Mathf.Approximately(material.GetFloat("_AlphaCutoffEnable"), 1f) ||
            !material.HasProperty("_AlphaCutoff") ||
            !Mathf.Approximately(material.GetFloat("_AlphaCutoff"), 0.5f) ||
            !material.IsKeywordEnabled("_ALPHATEST_ON") ||
            material.renderQueue != (int)RenderQueue.AlphaTest ||
            !string.Equals(material.GetTag("RenderType", false), "TransparentCutout", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Bentley badge validation found non-cutout rendering state on {owner}/{material.name}: " +
                $"alphaCutoffEnabled={material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0.5f}, " +
                $"alphaCutoff={(material.HasProperty("_AlphaCutoff") ? material.GetFloat("_AlphaCutoff").ToString("0.###") : "missing")}, " +
                $"keyword={material.IsKeywordEnabled("_ALPHATEST_ON")}, queue={material.renderQueue}, " +
                $"RenderType='{material.GetTag("RenderType", false)}'.");

        if (!HasBadgeAlphaMask(badgeTexture, out var transparentPixels, out var preservedDarkDetailPixels))
            throw new InvalidOperationException(
                $"Bentley badge validation found an invalid {BadgeBMarker} atlas on {owner}/{material.name}: " +
                $"transparentPixels={transparentPixels}, preservedDarkDetailPixels={preservedDarkDetailPixels}.");
        Debug.Log($"Bentley BadgeB alpha verified: transparent background pixels={transparentPixels}, " +
                  $"preserved dark artwork pixels={preservedDarkDetailPixels}, texture='{texturePath}'.");
    }

    private static void ConfigureBadgeTransparency(Material material)
    {
        if (!material.HasProperty("_BaseColorMap") ||
            material.GetTexture("_BaseColorMap") is not Texture2D currentTexture)
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} has no base-color atlas for its transparency mask.");

        var baseScale = material.GetTextureScale("_BaseColorMap");
        var baseOffset = material.GetTextureOffset("_BaseColorMap");
        var mainScale = material.HasProperty("_MainTex") ? material.GetTextureScale("_MainTex") : Vector2.one;
        var mainOffset = material.HasProperty("_MainTex") ? material.GetTextureOffset("_MainTex") : Vector2.zero;
        var gltfScale = material.HasProperty("baseColorTexture")
            ? material.GetTextureScale("baseColorTexture") : Vector2.one;
        var gltfOffset = material.HasProperty("baseColorTexture")
            ? material.GetTextureOffset("baseColorTexture") : Vector2.zero;

        var persistedMask = AssetDatabase.LoadAssetAtPath<Texture2D>(BadgeBAlphaTexturePath);
        var generatedMask = CreateBadgeAlphaMask(currentTexture);
        if (persistedMask == null)
        {
            AssetDatabase.CreateAsset(generatedMask, BadgeBAlphaTexturePath);
            persistedMask = generatedMask;
        }
        else
        {
            EditorUtility.CopySerialized(generatedMask, persistedMask);
            EditorUtility.SetDirty(persistedMask);
            UnityEngine.Object.DestroyImmediate(generatedMask);
        }

        var importer = AssetImporter.GetAtPath(BadgeBAlphaTexturePath);
        if (importer == null)
            throw new InvalidOperationException(
                $"Bentley badge setup could not persist its exact alpha atlas at '{BadgeBAlphaTexturePath}'.");
        importer.assetBundleName = "bentleycontinentalgtspeed";
        importer.assetBundleVariant = "unity3d";
        EditorUtility.SetDirty(importer);

        material.SetTexture("_BaseColorMap", persistedMask);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", persistedMask);
        if (material.HasProperty("baseColorTexture")) material.SetTexture("baseColorTexture", persistedMask);
        material.SetTextureScale("_BaseColorMap", baseScale);
        material.SetTextureOffset("_BaseColorMap", baseOffset);
        if (material.HasProperty("_MainTex"))
        {
            material.SetTextureScale("_MainTex", mainScale);
            material.SetTextureOffset("_MainTex", mainOffset);
        }
        if (material.HasProperty("baseColorTexture"))
        {
            material.SetTextureScale("baseColorTexture", gltfScale);
            material.SetTextureOffset("baseColorTexture", gltfOffset);
        }

        if (HasBadgeAlphaMask(persistedMask, out var transparentPixels, out var preservedDarkDetailPixels))
        {
            Debug.Log(
                $"Bentley {BadgeBMarker} alpha atlas generated from the exact GLB image referenced by its material: " +
                $"texture='{BadgeBAlphaTexturePath}', dimensions={persistedMask.width}x{persistedMask.height}, " +
                $"exteriorPixels={transparentPixels}, preservedDarkDetailPixels={preservedDarkDetailPixels}, " +
                $"connected-background-cutoff={BadgeBackgroundLuminanceCutoff}.");
        }
    }

    private static Texture2D CreateBadgeAlphaMask(Texture2D source)
    {
        var pngBytes = ReadBadgeBaseColorPngFromGlb();
        var decodedSource = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        if (!ImageConversion.LoadImage(decodedSource, pngBytes, false))
        {
            UnityEngine.Object.DestroyImmediate(decodedSource);
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} image extracted from '{BadgeModelPath}' is not a supported PNG.");
        }
        if (decodedSource.width != source.width || decodedSource.height != source.height)
        {
            var decodedDimensions = $"{decodedSource.width}x{decodedSource.height}";
            UnityEngine.Object.DestroyImmediate(decodedSource);
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} GLB image dimensions {decodedDimensions} do not match imported " +
                $"material texture '{source.name}' ({source.width}x{source.height}).");
        }

        Color32[] pixels;
        try
        {
            pixels = decodedSource.GetPixels32(0);
        }
        catch (Exception exception)
        {
            UnityEngine.Object.DestroyImmediate(decodedSource);
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} image extracted from '{BadgeModelPath}' is unexpectedly unreadable.", exception);
        }

        var width = decodedSource.width;
        var height = decodedSource.height;
        if (pixels.Length != width * height)
        {
            UnityEngine.Object.DestroyImmediate(decodedSource);
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} atlas '{source.name}' returned an unexpected pixel count.");
        }

        var exteriorBackground = new bool[pixels.Length];
        var queue = new int[pixels.Length];
        var queueEnd = 0;
        var queueStart = 0;

        void EnqueueExteriorPixel(int index)
        {
            if (exteriorBackground[index]) return;
            var pixel = pixels[index];
            if (Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) > BadgeBackgroundLuminanceCutoff)
                return;
            exteriorBackground[index] = true;
            queue[queueEnd++] = index;
        }

        // The source PNG is RGB with a black atlas background. Flood only dark
        // pixels connected to its outer edge so enclosed black logo lettering,
        // the B counters, and the dark wing details remain opaque.
        for (var x = 0; x < width; x++)
        {
            EnqueueExteriorPixel(x);
            EnqueueExteriorPixel((height - 1) * width + x);
        }
        for (var y = 0; y < height; y++)
        {
            EnqueueExteriorPixel(y * width);
            EnqueueExteriorPixel(y * width + width - 1);
        }

        while (queueStart < queueEnd)
        {
            var index = queue[queueStart++];
            var x = index % width;
            var y = index / width;
            for (var offsetY = -1; offsetY <= 1; offsetY++)
            for (var offsetX = -1; offsetX <= 1; offsetX++)
            {
                if (offsetX == 0 && offsetY == 0) continue;
                var neighborX = x + offsetX;
                var neighborY = y + offsetY;
                if (neighborX < 0 || neighborX >= width || neighborY < 0 || neighborY >= height)
                    continue;
                EnqueueExteriorPixel(neighborY * width + neighborX);
            }
        }

        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];
            pixel.a = exteriorBackground[index] ? (byte)0 : (byte)255;
            pixels[index] = pixel;
        }

        var masked = new Texture2D(width, height, TextureFormat.RGBA32,
            source.mipmapCount > 1, false)
        {
            name = "BentleyBadgeB_Alpha",
            filterMode = source.filterMode,
            wrapModeU = source.wrapModeU,
            wrapModeV = source.wrapModeV,
            anisoLevel = source.anisoLevel,
            mipMapBias = source.mipMapBias,
        };
        masked.SetPixels32(pixels, 0);
        masked.Apply(true, false);
        UnityEngine.Object.DestroyImmediate(decodedSource);
        return masked;
    }

    private static byte[] ReadBadgeBaseColorPngFromGlb()
    {
        var absoluteModelPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BadgeModelPath));
        if (!File.Exists(absoluteModelPath))
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} alpha setup cannot read its source GLB '{BadgeModelPath}'.");

        var glbBytes = File.ReadAllBytes(absoluteModelPath);
        if (glbBytes.Length < 20 || ReadUInt32LittleEndian(glbBytes, 0) != 0x46546C67u ||
            ReadUInt32LittleEndian(glbBytes, 4) != 2u ||
            ReadUInt32LittleEndian(glbBytes, 8) != glbBytes.Length)
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} alpha setup found an invalid glTF 2.0 container at '{BadgeModelPath}'.");

        var chunkOffset = 12;
        GlbMaterialDocument? document = null;
        var binaryChunkOffset = -1;
        var binaryChunkLength = 0;
        while (chunkOffset <= glbBytes.Length - 8)
        {
            var chunkLength = checked((int)ReadUInt32LittleEndian(glbBytes, chunkOffset));
            var chunkType = ReadUInt32LittleEndian(glbBytes, chunkOffset + 4);
            var payloadOffset = chunkOffset + 8;
            if (chunkLength < 0 || payloadOffset > glbBytes.Length - chunkLength)
                throw new InvalidOperationException(
                    $"Bentley {BadgeBMarker} alpha setup found a truncated GLB chunk at offset {chunkOffset}.");

            if (chunkType == 0x4E4F534Au)
            {
                var json = Encoding.UTF8.GetString(glbBytes, payloadOffset, chunkLength).TrimEnd('\0', ' ', '\t', '\r', '\n');
                document = JsonUtility.FromJson<GlbMaterialDocument>(json);
            }
            else if (chunkType == 0x004E4942u)
            {
                binaryChunkOffset = payloadOffset;
                binaryChunkLength = chunkLength;
            }

            chunkOffset = payloadOffset + chunkLength;
        }

        if (document == null || binaryChunkOffset < 0 || document.materials == null ||
            document.textures == null || document.images == null || document.bufferViews == null)
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} alpha setup could not resolve JSON and BIN data in '{BadgeModelPath}'.");

        var matches = document.materials
            .Where(material => material != null &&
                               string.Equals(material.name, BadgeBMarker, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || matches[0].pbrMetallicRoughness?.baseColorTexture == null)
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} alpha setup expected exactly one GLB material with a base-color texture; " +
                $"found {matches.Length}.");

        var textureIndex = matches[0].pbrMetallicRoughness.baseColorTexture.index;
        if (textureIndex < 0 || textureIndex >= document.textures.Length)
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} material references invalid GLB texture index {textureIndex}.");
        var imageIndex = document.textures[textureIndex].source;
        if (imageIndex < 0 || imageIndex >= document.images.Length)
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} texture {textureIndex} references invalid GLB image index {imageIndex}.");
        var image = document.images[imageIndex];
        if (image.bufferView < 0 || image.bufferView >= document.bufferViews.Length ||
            !string.Equals(image.mimeType, "image/png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} material's GLB image {imageIndex} is not a PNG buffer view.");

        var view = document.bufferViews[image.bufferView];
        var byteOffset = view.byteOffset;
        if (byteOffset < 0 || view.byteLength <= 0 || byteOffset > binaryChunkLength - view.byteLength)
            throw new InvalidOperationException(
                $"Bentley {BadgeBMarker} image buffer view {image.bufferView} is outside the GLB BIN chunk.");

        var pngBytes = new byte[view.byteLength];
        Buffer.BlockCopy(glbBytes, binaryChunkOffset + byteOffset, pngBytes, 0, view.byteLength);
        return pngBytes;
    }

    private static uint ReadUInt32LittleEndian(byte[] bytes, int offset) =>
        (uint)bytes[offset] |
        (uint)bytes[offset + 1] << 8 |
        (uint)bytes[offset + 2] << 16 |
        (uint)bytes[offset + 3] << 24;

    private static bool HasBadgeAlphaMask(
        Texture2D texture, out int transparentPixels, out int preservedDarkDetailPixels)
    {
        transparentPixels = 0;
        preservedDarkDetailPixels = 0;
        Color32[] pixels;
        try
        {
            pixels = texture.GetPixels32(0);
        }
        catch (Exception)
        {
            return false;
        }

        if (pixels.Length != texture.width * texture.height || pixels.Length == 0)
            return false;

        foreach (var pixel in pixels)
        {
            if (pixel.a == 0)
                transparentPixels++;
            if (pixel.a == 255 && Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) <= BadgeBackgroundLuminanceCutoff)
                preservedDarkDetailPixels++;
        }

        var minimumTransparentPixels = pixels.Length / 4;
        return pixels[0].a == 0 && pixels[texture.width - 1].a == 0 &&
               pixels[(texture.height - 1) * texture.width].a == 0 &&
               pixels[pixels.Length - 1].a == 0 &&
               transparentPixels >= minimumTransparentPixels && preservedDarkDetailPixels > 1000;
    }

    private static MaterialRole GetRole(string materialName)
    {
        if (string.Equals(materialName, BodyPaintMarker, StringComparison.Ordinal))
            return MaterialRole.BodyPaint;
        if (string.Equals(materialName, OemPaintMarker, StringComparison.Ordinal))
            return MaterialRole.BodyPaint;
        if (string.Equals(materialName, CaliperPaintMarker, StringComparison.Ordinal))
            return MaterialRole.CaliperPaint;
        if (string.Equals(materialName, CabinGlassMarker, StringComparison.Ordinal))
            return MaterialRole.CabinGlass;
        if (string.Equals(materialName, RedLensMarker, StringComparison.Ordinal) ||
            string.Equals(materialName, AmberLensMarker, StringComparison.Ordinal))
            return MaterialRole.ColoredLens;
        // LightA has no emissive factor or function-specific split in the GLB.
        // Keep it in the ordinary authored-material path instead of making it glow.
        return MaterialRole.FixedOpaque;
    }

    private static bool UsesVehiclePaintShader(MaterialRole role) =>
        role == MaterialRole.BodyPaint || role == MaterialRole.CaliperPaint;

    private static bool IsPreparedMaterial(string materialName) =>
        materialName.StartsWith("Bentley_", StringComparison.Ordinal) &&
        materialName.EndsWith("_Prepared", StringComparison.Ordinal);

    private static string GetSourceMarker(string materialName)
    {
        if (!IsPreparedMaterial(materialName))
            return materialName;
        return materialName.Substring("Bentley_".Length,
            materialName.Length - "Bentley_".Length - "_Prepared".Length);
    }

    private static string NormalizeMaterialName(string name)
    {
        var normalized = name.Trim();
        const string cloneSuffix = "(Clone)";
        while (normalized.EndsWith(cloneSuffix, StringComparison.Ordinal))
            normalized = normalized.Substring(0, normalized.Length - cloneSuffix.Length).TrimEnd();
        return normalized;
    }

    private static Shader? ResolveHdrpLitShader()
    {
        var shader = Shader.Find("HDRP/Lit") ??
                     Shader.Find("High Definition Render Pipeline/Lit");
        if (shader != null)
            return shader;

        // Shader.Find can miss package shaders during batch authoring even when
        // the active HDRP package has imported the source asset.
        return AssetDatabase.LoadAssetAtPath<Shader>(
            "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/Lit.shader");
    }

    private static void CopyBaseColorTexture(Material source, Material target)
    {
        var sourceProperty = source.HasProperty("baseColorTexture")
            ? "baseColorTexture"
            : source.HasProperty("_BaseColorMap") ? "_BaseColorMap" : string.Empty;
        var texture = sourceProperty.Length > 0 ? source.GetTexture(sourceProperty) : null;
        if (texture == null || !target.HasProperty("_BaseColorMap")) return;

        target.SetTexture("_BaseColorMap", texture);
        target.SetTextureScale("_BaseColorMap", source.GetTextureScale(sourceProperty));
        target.SetTextureOffset("_BaseColorMap", source.GetTextureOffset(sourceProperty));
    }

    private static void CopyNormalTexture(Material source, Material target)
    {
        var sourceProperty = source.HasProperty("normalTexture")
            ? "normalTexture"
            : source.HasProperty("_NormalMap") ? "_NormalMap" : string.Empty;
        if (sourceProperty.Length == 0 || !target.HasProperty("_NormalMap")) return;
        var texture = source.GetTexture(sourceProperty);
        if (texture == null) return;

        target.SetTexture("_NormalMap", texture);
        target.SetTextureScale("_NormalMap", source.GetTextureScale(sourceProperty));
        target.SetTextureOffset("_NormalMap", source.GetTextureOffset(sourceProperty));
        SetFloatIfPresent(target, "_NormalScale", GetFloat(source, "normalTexture_scale", "_NormalScale", 1f));
    }

    private static void CopyBaseColor(Material source, Material target, MaterialRole role)
    {
        var color = Color.white;
        if (source.HasProperty("baseColorFactor"))
            color = source.GetColor("baseColorFactor");
        else if (source.HasProperty("_BaseColor"))
            color = source.GetColor("_BaseColor");
        else if (source.HasProperty("_Color"))
            color = source.GetColor("_Color");

        if (role == MaterialRole.BodyPaint || role == MaterialRole.CaliperPaint)
            color = Color.white;
        else if (role == MaterialRole.CabinGlass)
            color = new Color(0.78f, 0.84f, 0.89f, Mathf.Clamp01(color.a));

        SetColorIfPresent(target, "_BaseColor", color);
        SetColorIfPresent(target, "_Color", color);
    }

    private static void ConfigureVehiclePaint(Material material, PaintTextures paintTextures)
    {
        if (!material.HasProperty(VehicleTintProperty))
            throw new InvalidOperationException(
                $"Bentley vehicle paint shader '{material.shader.name}' lacks CarFeatures tint property '{VehicleTintProperty}'.");

        SetFloatIfPresent(material, "_SurfaceType", 0f);
        SetFloatIfPresent(material, "_AlphaCutoffEnable", 0f);
        SetFloatIfPresent(material, "_SupportDecals", 0f);
        // Preserve reference scalar values for any shader variants that expose
        // them. SH_Vehicle's actual metallic/smoothness outputs are texture
        // graph inputs, so the explicit neutral graph maps below own the PBR
        // result; the legacy glTF/HDRP scalar fields alone do not.
        SetFloatIfPresent(material, "_Metallic", 0f);
        SetFloatIfPresent(material, "_MetallicRemapMin", 0f);
        SetFloatIfPresent(material, "_MetallicRemapMax", 1f);
        SetFloatIfPresent(material, "_Smoothness", 0.5f);
        SetFloatIfPresent(material, "_SmoothnessRemapMin", 0f);
        SetFloatIfPresent(material, "_SmoothnessRemapMax", 1f);
        SetFloatIfPresent(material, "_ReceivesSSR", 0f);
        SetFloatIfPresent(material, "_ReceivesSSRTransparent", 0f);
        SetFloatIfPresent(material, "_EnableBlendModePreserveSpecularLighting", 0f);
        SetFloatIfPresent(material, "_ZWrite", 1f);
        // Some authored body panels have reversed or open mesh faces. Keep the
        // car-paint tint visible from either side of those panels.
        SetFloatIfPresent(material, "_CullMode", 0f);
        SetFloatIfPresent(material, "_CullModeForward", 0f);
        SetFloatIfPresent(material, "_OpaqueCullMode", 0f);
        SetFloatIfPresent(material, "_DoubleSidedEnable", 1f);
        material.EnableKeyword("_DOUBLESIDED_ON");
        material.EnableKeyword("_DISABLE_SSR");
        SetColorIfPresent(material, VehicleTintProperty, new Color(1f, 1f, 1f, 0f));
        SetColorIfPresent(material, VehicleFresnelColorProperty, Color.black);
        SetFloatIfPresent(material, VehicleFresnelPowerProperty, 2.04f);
        SetTextureIfPresent(material, VehicleMetAOSmoothTexture, paintTextures.MetAOSmooth);
        SetTextureIfPresent(material, VehicleDiffuseMaskTexture, paintTextures.DiffuseMask);
        SetTextureIfPresent(material, VehicleNormalTexture, paintTextures.Normal);
        material.EnableKeyword("_DISABLE_DECALS");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.Geometry + 225;
        material.SetOverrideTag("RenderType", "Opaque");
    }

    private static PaintTextures EnsurePaintTextures()
    {
        var metAOSmooth = EnsureTextureAsset(
            NeutralMetAOSmoothPath, "BentleyPaint_MetAOSmooth", new Color(0f, 1f, 0.6f, 1f));
        var diffuseMask = EnsureTextureAsset(
            NeutralDiffuseMaskPath, "BentleyPaint_DiffuseMask", Color.white);
        var normal = EnsureTextureAsset(
            NeutralNormalPath, "BentleyPaint_Normal", new Color(0.5f, 0.5f, 1f, 1f));
        return new PaintTextures(metAOSmooth, diffuseMask, normal);
    }

    private static Texture2D EnsureTextureAsset(string path, string name, Color color)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null)
        {
            texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 0,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, false);
            AssetDatabase.CreateAsset(texture, path);
        }

        var importer = AssetImporter.GetAtPath(path);
        if (importer == null)
            throw new InvalidOperationException(
                $"Bentley appearance setup could not resolve generated paint texture importer '{path}'.");
        importer.assetBundleName = "bentleycontinentalgtspeed";
        importer.assetBundleVariant = "unity3d";
        EditorUtility.SetDirty(importer);
        return texture;
    }

    private static void SetTextureIfPresent(Material material, string property, Texture texture)
    {
        if (material.HasProperty(property)) material.SetTexture(property, texture);
    }

    /// <summary>Validates a serialized SH_Vehicle paint material and its graph-owned texture inputs.</summary>
    public static void VerifyPaintMaterial(Material material, string owner)
    {
        if (material == null)
            throw new InvalidOperationException($"Bentley paint validation found a null material on {owner}.");
        var vehiclePaint = AssetDatabase.LoadAssetAtPath<Shader>(VehiclePaintShaderPath);
        if (vehiclePaint == null || material.shader != vehiclePaint)
            throw new InvalidOperationException(
                $"Bentley paint validation expected SH_Vehicle on {owner}/{material.name}; " +
                $"actual shader='{material.shader?.name ?? "<missing>"}'.");

        var missingGraphTextures = new[]
            {
                VehicleMetAOSmoothTexture, VehicleDiffuseMaskTexture, VehicleNormalTexture,
            }
            .Where(property => !material.HasProperty(property) || material.GetTexture(property) == null)
            .ToArray();
        if (missingGraphTextures.Length > 0)
            throw new InvalidOperationException(
                $"Bentley paint validation found unbound SH_Vehicle graph inputs on {owner}/{material.name}: " +
                string.Join(", ", missingGraphTextures));
        var metAOSmooth = (Texture2D)material.GetTexture(VehicleMetAOSmoothTexture);
        if (!metAOSmooth.isReadable)
            throw new InvalidOperationException(
                $"Bentley paint validation cannot inspect the SH_Vehicle metal map on {owner}/{material.name}.");
        var neutralChannels = metAOSmooth.GetPixel(0, 0);
        if (neutralChannels.r > 0.0001f || neutralChannels.g < 0.999f)
            throw new InvalidOperationException(
                $"Bentley paint validation found a metallic or occlusion override in the SH_Vehicle map on " +
                $"{owner}/{material.name}: pixel={neutralChannels}.");
        if (!material.HasProperty(VehicleTintProperty) ||
            !MaterialColorEquals(material, VehicleTintProperty, new Color(1f, 1f, 1f, 0f)) ||
            !MaterialFloatEqualsIfPresent(material, "_Metallic", 0f) ||
            !MaterialFloatEqualsIfPresent(material, "_MetallicRemapMin", 0f) ||
            !MaterialFloatEqualsIfPresent(material, "_MetallicRemapMax", 1f) ||
            !MaterialFloatEqualsIfPresent(material, "_Smoothness", 0.5f))
            throw new InvalidOperationException(
                $"Bentley paint validation found mismatched tint or reference PBR values on {owner}/{material.name}.");
    }

    private static bool MaterialFloatEquals(Material material, string property, float expected) =>
        material.HasProperty(property) && Mathf.Abs(material.GetFloat(property) - expected) <= 0.0001f;

    private static bool MaterialFloatEqualsIfPresent(Material material, string property, float expected) =>
        !material.HasProperty(property) || MaterialFloatEquals(material, property, expected);

    private static bool MaterialColorEquals(Material material, string property, Color expected)
    {
        if (!material.HasProperty(property)) return false;
        var actual = material.GetColor(property);
        return Mathf.Abs(actual.r - expected.r) <= 0.0001f &&
               Mathf.Abs(actual.g - expected.g) <= 0.0001f &&
               Mathf.Abs(actual.b - expected.b) <= 0.0001f &&
               Mathf.Abs(actual.a - expected.a) <= 0.0001f;
    }

    [Serializable]
    private sealed class GlbMaterialDocument
    {
        public GlbMaterial[] materials = Array.Empty<GlbMaterial>();
        public GlbTexture[] textures = Array.Empty<GlbTexture>();
        public GlbImage[] images = Array.Empty<GlbImage>();
        public GlbBufferView[] bufferViews = Array.Empty<GlbBufferView>();
    }

    [Serializable]
    private sealed class GlbMaterial
    {
        public string name = string.Empty;
        public GlbPbrMetallicRoughness? pbrMetallicRoughness;
    }

    [Serializable]
    private sealed class GlbPbrMetallicRoughness
    {
        public GlbTextureInfo? baseColorTexture;
    }

    [Serializable]
    private sealed class GlbTextureInfo
    {
        public int index;
    }

    [Serializable]
    private sealed class GlbTexture
    {
        public int source;
    }

    [Serializable]
    private sealed class GlbImage
    {
        public int bufferView = -1;
        public string mimeType = string.Empty;
    }

    [Serializable]
    private sealed class GlbBufferView
    {
        public int byteOffset;
        public int byteLength;
    }

    private readonly struct PaintTextures
    {
        internal PaintTextures(Texture2D metAOSmooth, Texture2D diffuseMask, Texture2D normal)
        {
            MetAOSmooth = metAOSmooth;
            DiffuseMask = diffuseMask;
            Normal = normal;
        }

        internal readonly Texture2D MetAOSmooth;
        internal readonly Texture2D DiffuseMask;
        internal readonly Texture2D Normal;
    }

    private static void ConfigureOpaque(
        Material material,
        float metallic,
        float roughness,
        bool alphaClipped,
        float alphaCutoff)
    {
        SetFloatIfPresent(material, "_SurfaceType", 0f);
        SetFloatIfPresent(material, "_Metallic", metallic);
        SetFloatIfPresent(material, "_Smoothness", 1f - Mathf.Clamp01(roughness));
        SetFloatIfPresent(material, "_SmoothnessRemapMin", 0f);
        SetFloatIfPresent(material, "_SmoothnessRemapMax", 1f);
        SetFloatIfPresent(material, "_ZWrite", 1f);
        SetFloatIfPresent(material, "_SupportDecals", 0f);
        SetFloatIfPresent(material, "_ReceivesSSR", 0f);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (alphaClipped)
        {
            SetFloatIfPresent(material, "_AlphaCutoffEnable", 1f);
            SetFloatIfPresent(material, "_AlphaCutoff", alphaCutoff);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.SetOverrideTag("RenderType", "TransparentCutout");
        }
        else
        {
            SetFloatIfPresent(material, "_AlphaCutoffEnable", 0f);
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.SetOverrideTag("RenderType", "Opaque");
        }
    }

    private static void ConfigureTransparent(Material material, Color? tint)
    {
        if (tint.HasValue)
        {
            SetColorIfPresent(material, "_BaseColor", tint.Value);
            SetColorIfPresent(material, "_Color", tint.Value);
        }

        SetFloatIfPresent(material, "_SurfaceType", 1f);
        SetFloatIfPresent(material, "_BlendMode", 0f);
        SetFloatIfPresent(material, "_SrcBlend", (float)BlendMode.One);
        SetFloatIfPresent(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, "_AlphaSrcBlend", (float)BlendMode.One);
        SetFloatIfPresent(material, "_AlphaDstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloatIfPresent(material, "_ZWrite", 0f);
        SetFloatIfPresent(material, "_TransparentZWrite", 0f);
        SetFloatIfPresent(material, "_AlphaCutoffEnable", 0f);
        SetFloatIfPresent(material, "_Metallic", 0f);
        SetFloatIfPresent(material, "_Smoothness", 0.62f);
        SetColorIfPresent(material, "_SpecularColor", new Color(0.55f, 0.55f, 0.55f, 1f));
        SetFloatIfPresent(material, "_SupportDecals", 0f);
        SetFloatIfPresent(material, "_ReceivesSSR", 0f);
        SetFloatIfPresent(material, "_ReceivesSSRTransparent", 0f);
        SetFloatIfPresent(material, "_EnableBlendModePreserveSpecularLighting", 0f);
        SetFloatIfPresent(material, "_TransparentCullMode", 0f);
        SetFloatIfPresent(material, "_Cull", 0f);
        SetFloatIfPresent(material, "_CullMode", 0f);
        SetFloatIfPresent(material, "_CullModeForward", 0f);
        SetFloatIfPresent(material, "_DoubleSidedEnable", 1f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_DOUBLESIDED_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.SetShaderPassEnabled("TransparentDepthPrepass", false);
        material.SetShaderPassEnabled("TransparentDepthPostpass", false);
        material.SetShaderPassEnabled("TransparentBackface", false);
    }

    private static void SetColorIfPresent(Material material, string property, Color value)
    {
        if (material.HasProperty(property)) material.SetColor(property, value);
    }

    private static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
    }

    private static float GetFloat(Material material, string firstProperty, string? secondProperty, float fallback)
    {
        if (material.HasProperty(firstProperty)) return material.GetFloat(firstProperty);
        if (secondProperty != null && material.HasProperty(secondProperty)) return material.GetFloat(secondProperty);
        return fallback;
    }

    private enum MaterialRole
    {
        FixedOpaque,
        BodyPaint,
        CaliperPaint,
        CabinGlass,
        ColoredLens,
    }
}
