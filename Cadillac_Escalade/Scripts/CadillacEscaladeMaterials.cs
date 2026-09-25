#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct CadillacEscaladeMaterialFixResult
{
    public CadillacEscaladeMaterialFixResult(
        int rendererCount,
        int decalMasksCleared,
        int opaqueMaterialsFixed,
        int transparentMaterialsFixed,
        int materialsValidated,
        int rimSlotsNormalized,
        int cabinGlassRenderers,
        int cabinGlassRenderersReenabled)
    {
        RendererCount = rendererCount;
        DecalMasksCleared = decalMasksCleared;
        OpaqueMaterialsFixed = opaqueMaterialsFixed;
        TransparentMaterialsFixed = transparentMaterialsFixed;
        MaterialsValidated = materialsValidated;
        RimSlotsNormalized = rimSlotsNormalized;
        CabinGlassRenderers = cabinGlassRenderers;
        CabinGlassRenderersReenabled = cabinGlassRenderersReenabled;
    }

    public int RendererCount { get; }
    public int DecalMasksCleared { get; }
    public int OpaqueMaterialsFixed { get; }
    public int TransparentMaterialsFixed { get; }
    public int MaterialsValidated { get; }
    public int RimSlotsNormalized { get; }
    public int CabinGlassRenderers { get; }
    public int CabinGlassRenderersReenabled { get; }
}

public static class CadillacEscaladeMaterials
{
    public const float RimMetallic = 0.88f;
    public const float RimSmoothness = 0.86f;
    public static readonly Color RimBaseColor = new Color(0.72f, 0.72f, 0.74f, 1f);

    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string RimMaterialMarker = "CadillacRimChrome";
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static CadillacEscaladeMaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var canonicalRimMaterial = FindCanonicalRimMaterial(vehicle);
        var materials = new HashSet<Material>();
        var rendererCount = 0;
        var decalMasksCleared = 0;
        var opaqueMaterialsFixed = 0;
        var transparentMaterialsFixed = 0;
        var materialsValidated = 0;
        var rimSlotsNormalized = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsCadillacRenderer(renderer.transform))
                continue;

            rendererCount++;
            var hasCabinGlass = Array.Exists(
                renderer.sharedMaterials,
                material => material != null && IsCabinGlassMaterial(material));
            if (hasCabinGlass)
            {
                cabinGlassRenderers++;
                if (!renderer.enabled)
                {
                    renderer.enabled = true;
                    cabinGlassRenderersReenabled++;
                }
            }
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !materials.Add(material))
                    continue;

                if (IsTransparentMaterial(material))
                {
                    FixTransparentHdrpMaterial(material);
                    transparentMaterialsFixed++;
                    continue;
                }

                RebindToHdrpLit(material);
                ApplyFactoryPalette(material);
                if (FixSolidHdrpMaterial(material))
                    materialsValidated++;
                opaqueMaterialsFixed++;
            }

            rimSlotsNormalized += NormalizeRimRenderer(renderer, canonicalRimMaterial);

            if (!HasOpaqueMaterial(renderer))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                continue;
            }

            var previousMask = renderer.renderingLayerMask;
            renderer.renderingLayerMask &= ~HdrpDecalLayerMask;
            if (previousMask != renderer.renderingLayerMask)
                decalMasksCleared++;
        }

        return new CadillacEscaladeMaterialFixResult(
            rendererCount,
            decalMasksCleared,
            opaqueMaterialsFixed,
            transparentMaterialsFixed,
            materialsValidated,
            rimSlotsNormalized,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
    }

    private static Material? FindCanonicalRimMaterial(GameObject vehicle)
    {
        Material? fallback = null;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsCadillacRenderer(renderer.transform))
                continue;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null ||
                    material.name.IndexOf(RimMaterialMarker, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                fallback ??= material;
                if (material.name.EndsWith("_Right", StringComparison.OrdinalIgnoreCase))
                    return material;
            }
        }

        return fallback;
    }

    private static int NormalizeRimRenderer(Renderer renderer, Material? canonicalMaterial)
    {
        if (canonicalMaterial == null)
            return 0;

        SetColor(canonicalMaterial, "_BaseColor", RimBaseColor);
        SetColor(canonicalMaterial, "_Color", RimBaseColor);
        SetColor(canonicalMaterial, "baseColorFactor", RimBaseColor);
        SetFloat(canonicalMaterial, "_Metallic", RimMetallic);
        SetFloat(canonicalMaterial, "_Smoothness", RimSmoothness);

        var normalized = 0;
        var materials = renderer.sharedMaterials;
        var changed = false;
        for (var index = 0; index < materials.Length; index++)
        {
            var material = materials[index];
            if (material == null ||
                material.name.IndexOf(RimMaterialMarker, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            // All four rims deliberately share one material object and have no
            // renderer-local overrides. This makes their finish byte-for-byte
            // identical; only the scene's natural directional lighting differs.
            materials[index] = canonicalMaterial;
            renderer.SetPropertyBlock(null, index);
            normalized++;
            changed = true;
        }

        if (changed)
            renderer.sharedMaterials = materials;

        return normalized;
    }

    public static bool IsTransparentMaterial(Material material)
    {
        var name = material.name;
        if (name.IndexOf("CadillacCabinGlass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("CadillacClearLampLens", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("CadillacRearLampLens", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("CadillacAmberLampLens", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Translucent_Glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f;
    }

    public static bool IsCadillacRenderer(Transform transform)
    {
        if (transform.name.StartsWith("CadillacDamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("CadillacWheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("CadillacFixedCaliper", StringComparison.Ordinal))
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("CadillacDamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "CadillacVisual", StringComparison.Ordinal) ||
                current.name.StartsWith("CadillacWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("CadillacFixedCaliper", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasOpaqueMaterial(Renderer renderer)
    {
        foreach (var material in renderer.sharedMaterials)
        {
            if (material != null && !IsTransparentMaterial(material))
                return true;
        }

        return false;
    }

    private static bool IsHdrpMaterial(Material material)
    {
        var shaderName = material.shader?.name ?? string.Empty;
        if (shaderName.StartsWith("HDRP/", StringComparison.Ordinal))
            return true;

        var shaderGraphTarget = material.GetTag("ShaderGraphTargetId", false, string.Empty);
        return shaderGraphTarget.StartsWith("HD", StringComparison.Ordinal) ||
               material.HasProperty("_SupportDecals");
    }

    private static void RebindToHdrpLit(Material material)
    {
        var hdrpLit = Shader.Find("HDRP/Lit") ??
                      Shader.Find("High Definition Render Pipeline/Lit");
        if (hdrpLit == null || material.shader == hdrpLit)
            return;

        var color = GetColor(material, "baseColorFactor", "_BaseColor", Color.white);
        color.a = 1f;
        var baseTextureProperty = FirstTextureProperty(
            material,
            "baseColorTexture",
            "_BaseColorMap",
            "_MainTex");
        var baseTexture = baseTextureProperty == null
            ? null
            : material.GetTexture(baseTextureProperty);
        var baseScale = baseTextureProperty == null
            ? Vector2.one
            : material.GetTextureScale(baseTextureProperty);
        var baseOffset = baseTextureProperty == null
            ? Vector2.zero
            : material.GetTextureOffset(baseTextureProperty);
        var normalProperty = FirstTextureProperty(material, "normalTexture", "_NormalMap");
        var normalTexture = normalProperty == null ? null : material.GetTexture(normalProperty);
        var normalScale = GetFloat(material, "normalTexture_scale", "normalScale", "_NormalScale", 1f);
        var metallic = GetFloat(material, "metallicFactor", "_Metallic", null, 0f);
        var roughness = GetFloat(material, "roughnessFactor", null, null, 1f);

        material.shader = hdrpLit;
        SetColor(material, "_BaseColor", color);
        SetTexture(material, "_BaseColorMap", baseTexture, baseScale, baseOffset);
        SetTexture(material, "_NormalMap", normalTexture, Vector2.one, Vector2.zero);
        SetFloat(material, "_NormalScale", normalScale);
        SetFloat(material, "_Metallic", metallic);
        SetFloat(material, "_Smoothness", 1f - Mathf.Clamp01(roughness));
    }

    private static bool FixSolidHdrpMaterial(Material material)
    {
        SetOpaqueColor(material, "_BaseColor");
        SetOpaqueColor(material, "baseColorFactor");
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_AlphaCutoffEnable", 0f);
        SetFloat(material, "_SupportDecals", 0f);
        SetFloat(material, "_ReceivesSSR", 0f);
        SetFloat(material, "_ReceivesSSRTransparent", 0f);
        SetFloat(material, "_RefractionModel", 0f);
        material.renderQueue = (int)RenderQueue.Geometry;
        material.SetOverrideTag("RenderType", "Opaque");

        var validated = TryValidateHdrpMaterial(material);
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        return validated;
    }

    private static void ApplyFactoryPalette(Material material)
    {
        var name = material.name;
        if (!Contains(name, "CadillacBodyPaint") &&
            !Contains(name, "CadillacRimChrome"))
            return;

        var rim = Contains(name, "CadillacRimChrome");
        var color = rim ? RimBaseColor : Color.white;
        var metallic = rim ? RimMetallic : 0.18f;
        var smoothness = rim ? RimSmoothness : 0.82f;

        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        SetColor(material, "baseColorFactor", color);
        SetFloat(material, "_Metallic", metallic);
        SetFloat(material, "metallicFactor", metallic);
        SetFloat(material, "_Smoothness", smoothness);
        SetFloat(material, "roughnessFactor", 1f - smoothness);
    }

    private static bool Contains(string value, string marker) =>
        value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void FixTransparentHdrpMaterial(Material material)
    {
        var name = material.name;
        var cabinGlass = IsCabinGlassMaterial(material);
        // Imported transparent Lit materials collapse toward black in the game.
        // Use an unlit transparent surface for both panes and passive lamp covers;
        // the dedicated lighting overlays provide the active light signatures.
        RebindToHdrpUnlit(material);
        var tint = GetTransparentTint(name, cabinGlass);
        SetColor(material, "_UnlitColor", tint);
        SetColor(material, "_BaseColor", tint);
        SetColor(material, "_Color", tint);
        SetColor(material, "baseColorFactor", tint);
        SetFloat(material, "transmissionFactor", 0f);
        SetFloat(material, "_SurfaceType", 1f);
        SetFloat(material, "_BlendMode", 0f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloat(material, "_AlphaSrcBlend", (float)BlendMode.One);
        SetFloat(material, "_AlphaDstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloat(material, "_ZWrite", 0f);
        SetFloat(material, "_TransparentZWrite", 0f);
        SetFloat(material, "_ZTestDepthEqualForOpaque", (float)CompareFunction.LessEqual);
        SetFloat(material, "_ZTestTransparent", (float)CompareFunction.LessEqual);
        SetFloat(material, "_AlphaCutoffEnable", 0f);
        SetFloat(material, "_SupportDecals", 0f);
        SetFloat(material, "_ReceivesSSR", 0f);
        SetFloat(material, "_ReceivesSSRTransparent", 0f);
        SetFloat(material, "_TransmissionEnable", 0f);
        SetFloat(material, "_TransmissionMask", 0f);
        SetFloat(material, "_RefractionModel", 0f);
        SetFloat(material, "_EnableBlendModePreserveSpecularLighting", cabinGlass ? 1f : 0f);
        ConfigureLampRestingEmission(material, name);
        if (cabinGlass)
        {
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "metallicFactor", 0f);
            SetFloat(material, "_Smoothness", 0.95f);
            SetFloat(material, "roughnessFactor", 0f);
        }
        SetFloat(material, "_TransparentDepthPrepassEnable", 0f);
        SetFloat(material, "_TransparentDepthPostpassEnable", 0f);
        SetFloat(material, "_TransparentBackfaceEnable", 0f);
        var cullMode = (float)CullMode.Off;
        SetFloat(material, "_Cull", cullMode);
        SetFloat(material, "_CullMode", cullMode);
        SetFloat(material, "_CullModeForward", cullMode);
        SetFloat(material, "_TransparentCullMode", cullMode);
        SetFloat(material, "_DoubleSidedEnable", 1f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_DOUBLESIDED_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("TransparentDepthPrepass", false);
        material.SetShaderPassEnabled("TransparentDepthPostpass", false);
        material.SetShaderPassEnabled("TransparentBackface", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("ShadowCaster", false);
    }

    private static Color GetTransparentTint(string name, bool cabinGlass)
    {
        if (cabinGlass)
            return new Color(0.08f, 0.09f, 0.10f, 0.12f);
        if (Contains(name, "CadillacRearLampLens"))
            return new Color(0.10f, 0.008f, 0.006f, 0.38f);
        if (Contains(name, "CadillacRearLamp"))
            return new Color(0.28f, 0.012f, 0.008f, 0.36f);
        if (Contains(name, "CadillacAmberLampLens"))
            return new Color(0.35f, 0.18f, 0.01f, 0.20f);
        if (Contains(name, "CadillacFrontLamp"))
            return new Color(0.88f, 0.93f, 1f, 0.08f);
        if (Contains(name, "CadillacClearLampLens"))
            return new Color(0.88f, 0.93f, 1f, 0.06f);
        return new Color(0.18f, 0.20f, 0.22f, 0.10f);
    }

    private static void ConfigureLampRestingEmission(Material material, string name)
    {
        Color emission;
        if (Contains(name, "CadillacFrontLamp"))
            emission = new Color(0.18f, 0.22f, 0.28f, 1f);
        else if (Contains(name, "CadillacRearLamp") &&
                 !Contains(name, "CadillacRearLampLens"))
            emission = new Color(0.20f, 0.004f, 0.001f, 1f);
        else
            return;

        // The source light-signature atlas contains deliberately black RGB in
        // its unlit regions. A restrained emissive floor preserves that alpha
        // detail while keeping the physical lamp elements readable when the
        // vehicle is parked and its active light overlays are disabled.
        SetColor(material, "_EmissiveColor", emission);
        SetColor(material, "_EmissionColor", emission);
        SetFloat(material, "_UseEmissiveIntensity", 0f);
        SetFloat(material, "_EmissiveExposureWeight", 1f);
        material.EnableKeyword("_EMISSION");
    }

    private static void RebindToHdrpUnlit(Material material)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit");
        if (shader != null && material.shader != shader)
            material.shader = shader;
    }

    internal static void RestoreCabinGlassMaterial(Material material)
    {
        if (IsCabinGlassMaterial(material))
            FixTransparentHdrpMaterial(material);
    }

    internal static void RestoreRearCabinGlassMaterial(Material material)
    {
        if (!IsCabinGlassMaterial(material))
            return;
        FixTransparentHdrpMaterial(material);
        var tint = new Color(0.045f, 0.05f, 0.055f, 0.25f);
        SetColor(material, "_UnlitColor", tint);
        SetColor(material, "_BaseColor", tint);
        SetColor(material, "_Color", tint);
        SetColor(material, "baseColorFactor", tint);
    }

    public static bool IsCabinGlassMaterial(Material material)
    {
        var name = material.name;
        return name.IndexOf("CadillacCabinGlass", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Translucent_Glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Windshield", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? FirstTextureProperty(Material material, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (material.HasProperty(property) && material.GetTexture(property) != null)
                return property;
        }

        return null;
    }

    private static Color GetColor(
        Material material,
        string firstProperty,
        string secondProperty,
        Color fallback)
    {
        if (material.HasProperty(firstProperty))
            return material.GetColor(firstProperty);
        return material.HasProperty(secondProperty)
            ? material.GetColor(secondProperty)
            : fallback;
    }

    private static float GetFloat(
        Material material,
        string firstProperty,
        string? secondProperty,
        string? thirdProperty,
        float fallback)
    {
        if (material.HasProperty(firstProperty))
            return material.GetFloat(firstProperty);
        if (secondProperty != null && material.HasProperty(secondProperty))
            return material.GetFloat(secondProperty);
        return thirdProperty != null && material.HasProperty(thirdProperty)
            ? material.GetFloat(thirdProperty)
            : fallback;
    }

    private static void SetOpaqueColor(Material material, string property)
    {
        if (!material.HasProperty(property))
            return;
        var color = material.GetColor(property);
        color.a = 1f;
        material.SetColor(property, color);
    }

    private static void SetColor(Material material, string property, Color value)
    {
        if (material.HasProperty(property))
            material.SetColor(property, value);
    }

    private static void SetTexture(
        Material material,
        string property,
        Texture? texture,
        Vector2 scale,
        Vector2 offset)
    {
        if (!material.HasProperty(property))
            return;
        material.SetTexture(property, texture);
        material.SetTextureScale(property, scale);
        material.SetTextureOffset(property, offset);
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static bool TryValidateHdrpMaterial(Material material)
    {
        try
        {
            var method = ResolveValidationMethod(
                HdMaterialTypeName,
                "ValidateMaterial",
                ref validateMaterialMethod,
                ref validateMaterialMethodResolved);
            if (method != null)
            {
                var result = method.Invoke(null, new object[] { material });
                if (!(result is bool validated) || validated)
                    return true;
            }

            method = ResolveValidationMethod(
                ShaderGraphApiTypeName,
                "ValidateLightingMaterial",
                ref validateShaderGraphMaterialMethod,
                ref validateShaderGraphMaterialMethodResolved);
            if (method == null)
                return false;
            method.Invoke(null, new object[] { material });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static MethodInfo? ResolveValidationMethod(
        string typeName,
        string methodName,
        ref MethodInfo? cachedMethod,
        ref bool resolved)
    {
        if (resolved)
            return cachedMethod;

        resolved = true;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName, false);
            if (type == null)
                continue;
            cachedMethod = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Material) },
                null);
            if (cachedMethod != null)
                break;
        }

        return cachedMethod;
    }
}
