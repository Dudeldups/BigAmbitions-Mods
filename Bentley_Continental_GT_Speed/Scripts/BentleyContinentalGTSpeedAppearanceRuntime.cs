#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Restores Bentley paint and glass on each spawned vehicle instance. Game
/// spawn paths can replace material state after the prefab is loaded, so this
/// runs once from the vehicle lifecycle callback and owns only its own clones.
/// </summary>
[DisallowMultipleComponent]
internal sealed class BentleyContinentalGTSpeedAppearanceRuntime : MonoBehaviour
{
    private const string VehicleTintProperty = "Color_3d0f0cdbe6b74be28a1a5be5bab71dea";
    private const string VehicleFresnelColorProperty = "Color_f78fac473bac467092fb27521e9f71ea";
    private const string VehicleFresnelPowerProperty = "Vector1_481fa2a8a5e94165a039319bfd512b76";
    private const string VehicleMetAOSmoothTexture = "Texture2D_196dcf5fb385498aa0919d56d7cd684d";
    private const string VehicleDiffuseMaskTexture = "Texture2D_3d0babbacbb04731a784ae3943688e65";
    private const string VehicleNormalTexture = "Texture2D_9b32f246716c41009146a43593b55e3f";
    private const uint DecalRenderingLayerMask = 0x0000FF00u;
    private const int ExpectedCaliperPaintSlots = 8;
    private readonly List<(Renderer Renderer, Material[] Original, Material[] Applied)> originalBindings = new();
    private readonly List<(Renderer Renderer, uint OriginalMask)> originalRenderingLayerMasks = new();
    private readonly List<Material> ownedMaterials = new();
    private readonly List<Texture2D> ownedTextures = new();
    private Texture2D? neutralMetAOSmoothTexture;
    private Texture2D? neutralDiffuseMaskTexture;
    private Texture2D? flatNormalTexture;
    private ModContext? context;
    private bool configured;
    private bool incompleteWarningEmitted;

    internal bool Configure(GameObject vehicleRoot, ModContext? modContext, string source)
    {
        if (configured && IsCurrentConfigurationIntact(vehicleRoot))
            return true;
        if (configured)
            Cleanup();

        context = modContext;
        var changedRenderers = 0;
        var paintMaterials = 0;
        var caliperPaintMaterials = 0;
        var glassMaterials = 0;
        var incompleteGraphTextureBindings = new List<string>();
        foreach (var renderer in vehicleRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;
            var original = renderer.sharedMaterials;
            if (original == null || original.Length == 0)
                continue;

            Material[]? replacement = null;
            var containsBodyPaint = false;
            for (var index = 0; index < original.Length; index++)
            {
                var material = original[index];
                if (material == null)
                    continue;
                var isPaint = IsBodyPaint(material);
                var isCaliperPaint = IsCaliperPaint(material) && IsLiveExtractedCaliperRenderer(renderer);
                var isGlass = IsCabinGlass(material);
                if (!isPaint && !isCaliperPaint && !isGlass)
                    continue;

                replacement ??= (Material[])original.Clone();
                var instance = new Material(material) { name = material.name };
                if (isPaint)
                {
                    containsBodyPaint = true;
                    ConfigurePaint(instance);
                    if (!HasTexture(instance, VehicleMetAOSmoothTexture) ||
                        !HasTexture(instance, VehicleDiffuseMaskTexture) ||
                        !HasTexture(instance, VehicleNormalTexture))
                        incompleteGraphTextureBindings.Add($"{renderer.name}[{index}]/{material.name}");
                    paintMaterials++;
                }
                if (isCaliperPaint)
                {
                    // The native CarFeatures color path updates the exact
                    // CaliperAZonePaint slot through bodyMeshes. Give every
                    // vehicle its own copy before native preview/commit/cancel
                    // changes the material color.
                    ConfigurePaint(instance);
                    if (!HasTexture(instance, VehicleMetAOSmoothTexture) ||
                        !HasTexture(instance, VehicleDiffuseMaskTexture) ||
                        !HasTexture(instance, VehicleNormalTexture))
                        incompleteGraphTextureBindings.Add($"{renderer.name}[{index}]/{material.name}");
                    caliperPaintMaterials++;
                }
                if (isGlass)
                {
                    ConfigureGlass(instance);
                    glassMaterials++;
                }
                replacement[index] = instance;
                ownedMaterials.Add(instance);
            }

            if (replacement == null)
                continue;
            renderer.sharedMaterials = replacement;
            originalBindings.Add((renderer, original, replacement));
            if (containsBodyPaint && (renderer.renderingLayerMask & DecalRenderingLayerMask) != 0)
            {
                originalRenderingLayerMasks.Add((renderer, renderer.renderingLayerMask));
                renderer.renderingLayerMask &= ~DecalRenderingLayerMask;
            }
            changedRenderers++;
        }

        if (paintMaterials == 0 || caliperPaintMaterials != ExpectedCaliperPaintSlots ||
            glassMaterials == 0 || incompleteGraphTextureBindings.Count > 0)
        {
            // Roll back a partial pass so a later vehicle lifecycle event can
            // retry cleanly if the missing renderer was attached afterward.
            Cleanup();
            if (!incompleteWarningEmitted)
            {
                incompleteWarningEmitted = true;
                context?.Logger.Warn(
                    $"BentleyContinentalGTSpeed appearance instance={vehicleRoot.GetInstanceID()} " +
                    $"source='{source}' found paintMaterials={paintMaterials}, " +
                    $"caliperPaintMaterials={caliperPaintMaterials}/{ExpectedCaliperPaintSlots}, " +
                    $"glassMaterials={glassMaterials}; " +
                    $"unboundSHVehicleMaps={string.Join(",", incompleteGraphTextureBindings)}; " +
                    "runtime material repair is incomplete and will retry on the next vehicle lifecycle event.");
            }
            return false;
        }

        configured = true;
        incompleteWarningEmitted = false;
        if (BentleyContinentalGTSpeedDiagnostics.DebugEnabled &&
            BentleyContinentalGTSpeedDiagnostics.AppearanceDebugEnabled)
        {
            context?.Logger.Info(
                $"BentleyContinentalGTSpeed appearance instance={vehicleRoot.GetInstanceID()} " +
                $"source='{source}' configured renderers={changedRenderers}, " +
                $"paintMaterials={paintMaterials}, caliperPaintMaterials={caliperPaintMaterials}, " +
                $"glassMaterials={glassMaterials}.");
            LogPaintMaterialState(vehicleRoot);
        }
        return true;
    }

    private void LogPaintMaterialState(GameObject vehicleRoot)
    {
        var tintId = Shader.PropertyToID(VehicleTintProperty);
        var propertyBlock = new MaterialPropertyBlock();
        foreach (var renderer in vehicleRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            var materials = renderer.sharedMaterials;
            propertyBlock.Clear();
            renderer.GetPropertyBlock(propertyBlock);
            var hasRendererTintOverride = propertyBlock.HasProperty(tintId);
            var rendererTintOverride = hasRendererTintOverride
                ? propertyBlock.GetColor(tintId).ToString()
                : "none";
            for (var slot = 0; slot < materials.Length; slot++)
            {
                var material = materials[slot];
                if (material == null || (!IsBodyPaint(material) && !IsCaliperPaint(material))) continue;

                propertyBlock.Clear();
                renderer.GetPropertyBlock(propertyBlock, slot);
                var hasTintOverride = propertyBlock.HasProperty(tintId);
                var tintOverride = hasTintOverride
                    ? propertyBlock.GetColor(tintId).ToString()
                    : "none";
                var metallic = material.HasProperty("_Metallic")
                    ? material.GetFloat("_Metallic").ToString("0.###") : "n/a";
                var smoothness = material.HasProperty("_Smoothness")
                    ? material.GetFloat("_Smoothness").ToString("0.###") : "n/a";
                var metallicRemapMax = material.HasProperty("_MetallicRemapMax")
                    ? material.GetFloat("_MetallicRemapMax").ToString("0.###") : "n/a";
                var fresnel = material.HasProperty(VehicleFresnelColorProperty)
                    ? material.GetColor(VehicleFresnelColorProperty).ToString() : "n/a";
                var materialTint = material.HasProperty(VehicleTintProperty)
                    ? material.GetColor(VehicleTintProperty).ToString() : "n/a";
                var metAOSmoothMap = material.HasProperty(VehicleMetAOSmoothTexture)
                    ? material.GetTexture(VehicleMetAOSmoothTexture)?.name ?? "<null>" : "<missing-property>";
                var diffuseMaskMap = material.HasProperty(VehicleDiffuseMaskTexture)
                    ? material.GetTexture(VehicleDiffuseMaskTexture)?.name ?? "<null>" : "<missing-property>";
                var normalMap = material.HasProperty(VehicleNormalTexture)
                    ? material.GetTexture(VehicleNormalTexture)?.name ?? "<null>" : "<missing-property>";
                context?.Logger.Info(
                    $"BentleyContinentalGTSpeed appearance paint-slot renderer='{GetPath(renderer.transform)}' " +
                    $"slot={slot} material='{material.name}' shader='{material.shader?.name ?? "<missing>"}' " +
                    $"metallic={metallic} metallicRemapMax={metallicRemapMax} smoothness={smoothness} " +
                    $"materialTint={materialTint} fresnel={fresnel} " +
                    $"metAOSmoothMap='{metAOSmoothMap}' diffuseMaskMap='{diffuseMaskMap}' normalMap='{normalMap}' " +
                    $"rendererTintOverride={hasRendererTintOverride}:{rendererTintOverride} " +
                    $"slotTintOverride={hasTintOverride}:{tintOverride}.");
            }
        }
    }

    private static string GetPath(Transform transform)
    {
        var path = transform.name;
        for (var parent = transform.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        return path;
    }

    internal void Cleanup()
    {
        foreach (var binding in originalBindings)
        {
            if (binding.Renderer == null)
                continue;
            var current = binding.Renderer.sharedMaterials;
            var restored = (Material[])current.Clone();
            var changed = false;
            for (var appliedIndex = 0; appliedIndex < binding.Applied.Length; appliedIndex++)
            {
                if (appliedIndex >= binding.Original.Length)
                    continue;
                for (var currentIndex = 0; currentIndex < restored.Length; currentIndex++)
                {
                    if (restored[currentIndex] != binding.Applied[appliedIndex])
                        continue;
                    restored[currentIndex] = binding.Original[appliedIndex];
                    changed = true;
                }
            }
            if (changed)
                binding.Renderer.sharedMaterials = restored;
        }
        foreach (var binding in originalRenderingLayerMasks)
            if (binding.Renderer != null)
                binding.Renderer.renderingLayerMask = binding.OriginalMask;
        originalRenderingLayerMasks.Clear();
        originalBindings.Clear();
        foreach (var material in ownedMaterials)
            if (material != null)
                Destroy(material);
        ownedMaterials.Clear();
        foreach (var texture in ownedTextures)
            if (texture != null)
                Destroy(texture);
        ownedTextures.Clear();
        neutralMetAOSmoothTexture = null;
        neutralDiffuseMaskTexture = null;
        flatNormalTexture = null;
        configured = false;
    }

    private void OnDestroy() => Cleanup();

    private bool IsCurrentConfigurationIntact(GameObject vehicleRoot)
    {
        var paintFound = false;
        var caliperPaintFound = false;
        var glassFound = false;
        var caliperPaintCount = 0;
        foreach (var renderer in vehicleRoot.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;
            var current = renderer.sharedMaterials;
            var hasTargetMaterial = false;
            var hasPaintMaterial = false;
            foreach (var material in current)
            {
                if (material == null)
                    continue;
                var isPaint = IsBodyPaint(material);
                var isCaliperPaint = IsCaliperPaint(material) && IsLiveExtractedCaliperRenderer(renderer);
                var isGlass = IsCabinGlass(material);
                if (!isPaint && !isCaliperPaint && !isGlass)
                    continue;
                hasTargetMaterial = true;
                paintFound |= isPaint;
                caliperPaintFound |= isCaliperPaint;
                if (isCaliperPaint) caliperPaintCount++;
                hasPaintMaterial |= isPaint;
                glassFound |= isGlass;
                if ((isPaint || isCaliperPaint) && !IsPaintConfigurationIntact(material))
                    return false;
                if (isGlass && !IsGlassConfigurationIntact(material))
                    return false;
            }
            if (!hasTargetMaterial)
                continue;
            if (hasPaintMaterial && (renderer.renderingLayerMask & DecalRenderingLayerMask) != 0)
                return false;

            var bindingFound = false;
            foreach (var binding in originalBindings)
            {
                if (binding.Renderer != renderer)
                    continue;
                bindingFound = SameMaterials(current, binding.Applied);
                break;
            }
            if (!bindingFound)
                return false;
        }
        return paintFound && caliperPaintFound && caliperPaintCount == ExpectedCaliperPaintSlots && glassFound;
    }

    private static bool IsPaintConfigurationIntact(Material material) =>
        HasFloatValue(material, "_SurfaceType", 0f) &&
        HasFloatValue(material, "_AlphaCutoffEnable", 0f) &&
        HasFloatValue(material, "_Metallic", 0f) &&
        HasFloatValue(material, "_MetallicRemapMin", 0f) &&
        HasFloatValue(material, "_MetallicRemapMax", 1f) &&
        HasFloatValue(material, "_Smoothness", 0.5f) &&
        HasTexture(material, VehicleMetAOSmoothTexture) &&
        HasTexture(material, VehicleDiffuseMaskTexture) &&
        HasTexture(material, VehicleNormalTexture) &&
        HasFloatValue(material, "_SmoothnessRemapMin", 0f) &&
        HasFloatValue(material, "_SmoothnessRemapMax", 1f) &&
        HasFloatValue(material, "_SupportDecals", 0f) &&
        HasFloatValue(material, "_ReceivesSSR", 0f) &&
        HasFloatValue(material, "_ReceivesSSRTransparent", 0f) &&
        HasFloatValue(material, "_EnableBlendModePreserveSpecularLighting", 0f) &&
        HasFloatValue(material, "_ZWrite", 1f) &&
        HasFloatValue(material, "_CullMode", 0f) &&
        HasFloatValue(material, "_CullModeForward", 0f) &&
        HasFloatValue(material, "_OpaqueCullMode", 0f) &&
        HasFloatValue(material, "_DoubleSidedEnable", 1f) &&
        HasColorValue(material, VehicleFresnelColorProperty, Color.black) &&
        HasFloatValue(material, VehicleFresnelPowerProperty, 2.04f) &&
        material.IsKeywordEnabled("_DISABLE_DECALS") &&
        material.IsKeywordEnabled("_DISABLE_SSR") &&
        material.IsKeywordEnabled("_DOUBLESIDED_ON") &&
        !material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") &&
        !material.IsKeywordEnabled("_ALPHATEST_ON") &&
        material.renderQueue == (int)RenderQueue.Geometry + 225;

    private static bool IsGlassConfigurationIntact(Material material)
    {
        var expectedTint = new Color(0.62f, 0.70f, 0.78f, 0.02f);
        return HasColorValue(material, "_BaseColor", expectedTint) &&
               HasColorValue(material, "_Color", expectedTint) &&
               HasFloatValue(material, "_SurfaceType", 1f) &&
               HasFloatValue(material, "_BlendMode", 0f) &&
               HasFloatValue(material, "_SrcBlend", 1f) &&
               HasFloatValue(material, "_DstBlend", 10f) &&
               HasFloatValue(material, "_AlphaSrcBlend", 1f) &&
               HasFloatValue(material, "_AlphaDstBlend", 10f) &&
               HasFloatValue(material, "_ZWrite", 0f) &&
               HasFloatValue(material, "_TransparentZWrite", 0f) &&
               HasFloatValue(material, "_AlphaCutoffEnable", 0f) &&
               HasFloatValue(material, "_Metallic", 0f) &&
               HasFloatValue(material, "_Smoothness", 0.62f) &&
               HasColorValue(material, "_SpecularColor", new Color(0.55f, 0.55f, 0.55f, 1f)) &&
               HasFloatValue(material, "_SupportDecals", 0f) &&
               HasFloatValue(material, "_ReceivesSSR", 0f) &&
               HasFloatValue(material, "_ReceivesSSRTransparent", 0f) &&
               HasFloatValue(material, "_EnableBlendModePreserveSpecularLighting", 0f) &&
               HasFloatValue(material, "_TransparentCullMode", 0f) &&
               HasFloatValue(material, "_Cull", 0f) &&
               HasFloatValue(material, "_CullMode", 0f) &&
               HasFloatValue(material, "_CullModeForward", 0f) &&
               HasFloatValue(material, "_DoubleSidedEnable", 1f) &&
               material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") &&
               material.IsKeywordEnabled("_DISABLE_DECALS") &&
               material.IsKeywordEnabled("_DISABLE_SSR") &&
               material.IsKeywordEnabled("_DISABLE_SSR_TRANSPARENT") &&
               material.IsKeywordEnabled("_DOUBLESIDED_ON") &&
               !material.IsKeywordEnabled("_ALPHATEST_ON") &&
               material.renderQueue == (int)RenderQueue.Transparent;
    }

    private static bool SameMaterials(Material[] left, Material[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
            return false;
        for (var index = 0; index < left.Length; index++)
            if (left[index] != right[index])
                return false;
        return true;
    }

    private static bool IsBodyPaint(Material material)
    {
        var name = NormalizeMaterialName(material.name);
        return name.Equals("Paint", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("OEMPaint", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Bentley_Paint_Prepared", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Bentley_OEMPaint_Prepared", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("BentleyMaterial_026", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("BentleyMaterial_027", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCabinGlass(Material material)
    {
        var name = NormalizeMaterialName(material.name);
        return name.Equals("GlassMtl", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Bentley_GlassMtl_Prepared", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCaliperPaint(Material material)
    {
        var name = NormalizeMaterialName(material.name);
        return name.Equals("CaliperAZonePaint", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Bentley_CaliperAZonePaint_Prepared", StringComparison.OrdinalIgnoreCase);
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

    private static string NormalizeMaterialName(string name)
    {
        var normalized = name.Trim();
        const string cloneSuffix = "(Clone)";
        const string instanceSuffix = "(Instance)";
        while (normalized.EndsWith(cloneSuffix, StringComparison.Ordinal) ||
               normalized.EndsWith(instanceSuffix, StringComparison.Ordinal))
        {
            var suffix = normalized.EndsWith(cloneSuffix, StringComparison.Ordinal)
                ? cloneSuffix : instanceSuffix;
            normalized = normalized.Substring(0, normalized.Length - suffix.Length).TrimEnd();
        }
        return normalized;
    }

    private void ConfigurePaint(Material material)
    {
        // SH_Vehicle reads metallic, smoothness and normal data from its custom
        // graph texture properties. Normalize legacy scalar fields too, then
        // bind neutral graph maps so a null/default map cannot contribute metal.
        if (material.HasProperty(VehicleTintProperty) && material.GetColor(VehicleTintProperty).a < 0.5f)
            material.SetColor(VehicleTintProperty, new Color(1f, 1f, 1f, 0f));
        SetFloatIfPresent(material, "_Metallic", 0f);
        SetFloatIfPresent(material, "_MetallicRemapMin", 0f);
        SetFloatIfPresent(material, "_MetallicRemapMax", 1f);
        SetFloatIfPresent(material, "_Smoothness", 0.5f);
        SetFloatIfPresent(material, "_SmoothnessRemapMin", 0f);
        SetFloatIfPresent(material, "_SmoothnessRemapMax", 1f);
        SetFloatIfPresent(material, "_SupportDecals", 0f);
        SetFloatIfPresent(material, "_ReceivesSSR", 0f);
        SetFloatIfPresent(material, "_ReceivesSSRTransparent", 0f);
        SetFloatIfPresent(material, "_EnableBlendModePreserveSpecularLighting", 0f);
        SetFloatIfPresent(material, "_SurfaceType", 0f);
        SetFloatIfPresent(material, "_AlphaCutoffEnable", 0f);
        SetFloatIfPresent(material, "_ZWrite", 1f);
        SetFloatIfPresent(material, "_CullMode", 0f);
        SetFloatIfPresent(material, "_CullModeForward", 0f);
        SetFloatIfPresent(material, "_OpaqueCullMode", 0f);
        SetFloatIfPresent(material, "_DoubleSidedEnable", 1f);
        SetColorIfPresent(material, VehicleFresnelColorProperty, Color.black);
        SetFloatIfPresent(material, VehicleFresnelPowerProperty, 2.04f);
        SetNeutralTexture(material, VehicleMetAOSmoothTexture,
            ref neutralMetAOSmoothTexture, "BentleyRuntime_MetAOSmooth_Neutral", new Color(0f, 1f, 0.6f, 1f));
        SetNeutralTexture(material, VehicleDiffuseMaskTexture,
            ref neutralDiffuseMaskTexture, "BentleyRuntime_DiffuseMask_Neutral", Color.white);
        SetNeutralTexture(material, VehicleNormalTexture,
            ref flatNormalTexture, "BentleyRuntime_Normal_Flat", new Color(0.5f, 0.5f, 1f, 1f));
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DOUBLESIDED_ON");
        material.renderQueue = (int)RenderQueue.Geometry + 225;
        material.SetOverrideTag("RenderType", "Opaque");
    }

    private static void ConfigureGlass(Material material)
    {
        var tint = new Color(0.62f, 0.70f, 0.78f, 0.02f);
        SetColorIfPresent(material, "_BaseColor", tint);
        SetColorIfPresent(material, "_Color", tint);
        SetFloatIfPresent(material, "_SurfaceType", 1f);
        SetFloatIfPresent(material, "_BlendMode", 0f);
        SetFloatIfPresent(material, "_SrcBlend", 1f);
        SetFloatIfPresent(material, "_DstBlend", 10f);
        SetFloatIfPresent(material, "_AlphaSrcBlend", 1f);
        SetFloatIfPresent(material, "_AlphaDstBlend", 10f);
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
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        material.EnableKeyword("_DOUBLESIDED_ON");
        material.DisableKeyword("_ALPHATEST_ON");
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
        if (material.HasProperty(property))
            material.SetColor(property, value);
    }

    private static bool HasFloatValue(Material material, string property, float expected)
    {
        if (!material.HasProperty(property))
            return true;
        return Mathf.Abs(material.GetFloat(property) - expected) <= 0.0001f;
    }

    private static bool HasColorValue(Material material, string property, Color expected)
    {
        if (!material.HasProperty(property))
            return true;
        var color = material.GetColor(property);
        return Mathf.Abs(color.r - expected.r) <= 0.0001f &&
               Mathf.Abs(color.g - expected.g) <= 0.0001f &&
               Mathf.Abs(color.b - expected.b) <= 0.0001f &&
               Mathf.Abs(color.a - expected.a) <= 0.0001f;
    }

    private static bool HasTexture(Material material, string property) =>
        material.HasProperty(property) && material.GetTexture(property) != null;

    private void SetNeutralTexture(
        Material material, string property, ref Texture2D? cached, string textureName, Color color)
    {
        if (!material.HasProperty(property))
            return;

        if (cached == null)
        {
            cached = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
            {
                name = textureName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                anisoLevel = 0,
                hideFlags = HideFlags.HideAndDontSave,
            };
            cached.SetPixel(0, 0, color);
            cached.Apply(false, false);
            ownedTextures.Add(cached);
        }
        material.SetTexture(property, cached);
    }

    private static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
    }
}
