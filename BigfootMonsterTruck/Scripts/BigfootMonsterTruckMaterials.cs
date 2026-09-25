#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct BigfootMonsterTruckMaterialFixResult
{
    public BigfootMonsterTruckMaterialFixResult(
        int rendererCount,
        int decalMasksCleared,
        int opaqueMaterialsFixed,
        int materialsValidated)
    {
        RendererCount = rendererCount;
        DecalMasksCleared = decalMasksCleared;
        OpaqueMaterialsFixed = opaqueMaterialsFixed;
        MaterialsValidated = materialsValidated;
    }

    public int RendererCount { get; }
    public int DecalMasksCleared { get; }
    public int OpaqueMaterialsFixed { get; }
    public int MaterialsValidated { get; }
}

public static class BigfootMonsterTruckMaterials
{
    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";
    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static BigfootMonsterTruckMaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var renderers = vehicle.GetComponentsInChildren<Renderer>(true);
        var materials = new HashSet<Material>();
        var decalMasksCleared = 0;
        var opaqueMaterialsFixed = 0;
        var materialsValidated = 0;
        var rendererCount = 0;

        foreach (var renderer in renderers)
        {
            if (!IsBigfootVisualRenderer(renderer.transform))
                continue;
            rendererCount++;
            if (!HasOpaqueMaterial(renderer))
                continue;
            var previousMask = renderer.renderingLayerMask;
            renderer.renderingLayerMask &= ~HdrpDecalLayerMask;
            if (renderer.renderingLayerMask != previousMask)
                decalMasksCleared++;

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || IsTransparentMaterial(material) || !materials.Add(material))
                    continue;
                if (!IsHdrpMaterial(material))
                    continue;
                RebindToHdrpLit(material);
                if (FixSolidHdrpMaterial(material))
                    materialsValidated++;
                opaqueMaterialsFixed++;
            }
        }

        return new BigfootMonsterTruckMaterialFixResult(
            rendererCount,
            decalMasksCleared,
            opaqueMaterialsFixed,
            materialsValidated);
    }

    public static bool IsTransparentMaterial(Material material)
    {
        if (material.name.IndexOf("Windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            string.Equals(material.shader?.name, "Bigfoot/TransparentWindshield", StringComparison.Ordinal))
            return true;
        return material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f;
    }

    private static bool HasOpaqueMaterial(Renderer renderer)
    {
        foreach (var material in renderer.sharedMaterials)
            if (material != null && !IsTransparentMaterial(material))
                return true;
        return false;
    }

    private static bool IsBigfootVisualRenderer(Transform transform)
    {
        if (transform.name.StartsWith("Wheel", StringComparison.Ordinal) &&
            transform.name.EndsWith("Visual", StringComparison.Ordinal))
            return true;
        for (var current = transform; current != null; current = current.parent)
            if (string.Equals(current.name, "BigfootVisual", StringComparison.Ordinal))
                return true;
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

        var baseColor = GetColor(material, "baseColorFactor", "_BaseColor", Color.white);
        baseColor.a = 1f;
        var baseTextureProperty = FirstTextureProperty(
            material,
            "baseColorTexture",
            "_BaseColorMap",
            "_MainTex");
        var baseTexture = baseTextureProperty != null
            ? material.GetTexture(baseTextureProperty)
            : null;
        var baseScale = baseTextureProperty != null
            ? material.GetTextureScale(baseTextureProperty)
            : Vector2.one;
        var baseOffset = baseTextureProperty != null
            ? material.GetTextureOffset(baseTextureProperty)
            : Vector2.zero;
        var normalTextureProperty = FirstTextureProperty(material, "normalTexture", "_NormalMap");
        var normalTexture = normalTextureProperty != null
            ? material.GetTexture(normalTextureProperty)
            : null;
        var normalScale = GetFloat(
            material,
            "normalTexture_scale",
            "normalScale",
            "_NormalScale",
            1f);
        var metallic = GetFloat(material, "metallicFactor", "_Metallic", null, 0f);
        var roughness = GetFloat(material, "roughnessFactor", null, null, 1f);

        material.shader = hdrpLit;
        SetColor(material, "_BaseColor", baseColor);
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

    private static string? FirstTextureProperty(Material material, params string[] properties)
    {
        foreach (var property in properties)
            if (material.HasProperty(property) && material.GetTexture(property) != null)
                return property;
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
                if (result is not bool validated || validated)
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
