#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct ChevroletCamaro1967MaterialFixResult
{
    public ChevroletCamaro1967MaterialFixResult(
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

internal enum ChevroletCamaro1967TransparentRole
{
    Authored,
    CabinGlass,
    HeadlampLens,
    OtherClear,
}

public static class ChevroletCamaro1967Materials
{
    public const float RimMetallic = 0.82f;
    public const float RimSmoothness = 0.68f;
    public static readonly Color RimBaseColor = new Color(0.58f, 0.58f, 0.58f, 1f);

    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string RimMaterialMarker = "material_11";
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static ChevroletCamaro1967MaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var controller = vehicle.GetComponent<ChevroletCamaro1967MaterialController>();
        if (controller == null)
            controller = vehicle.AddComponent<ChevroletCamaro1967MaterialController>();
        return controller.Initialize(null);
    }

    private static Material? FindCanonicalRimMaterial(GameObject vehicle)
    {
        Material? fallback = null;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsCamaroRenderer(renderer.transform))
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
        if (name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f;
    }

    public static bool IsBadgeMaterial(Material material)
    {
        return string.Equals(material.name, "badges", StringComparison.Ordinal) ||
               string.Equals(material.name, "CamaroTransparent_00_badges", StringComparison.Ordinal) ||
               material.name.StartsWith("CamaroTransparent_00_badges_CamaroInstance_", StringComparison.Ordinal);
    }

    public static bool NormalizeImportedMaterial(Material material)
    {
        if (IsBadgeMaterial(material))
        {
            PrepareBadgeMaterial(material);
            return true;
        }

        if (IsTransparentMaterial(material))
        {
            PrepareTransparentMaterial(material, ChevroletCamaro1967TransparentRole.OtherClear);
            return true;
        }

        RebindToHdrpLit(material);
        return FixSolidHdrpMaterial(material);
    }

    public static bool IsCamaroRenderer(Transform transform)
    {
        if (transform.name.StartsWith("CamaroDamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("CamaroWheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("CamaroFixedCaliper", StringComparison.Ordinal))
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("CamaroDamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "CamaroVisual", StringComparison.Ordinal) ||
                current.name.StartsWith("CamaroWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("CamaroFixedCaliper", StringComparison.Ordinal))
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

    private static void PrepareBadgeMaterial(Material material)
    {
        // The authored atlas contains the fender scripts, flags and grille mesh.
        // It needs lit, alpha-cutout surfaces, never the glass tint/Unlit path.
        RebindToHdrpLit(material);
        SetColor(material, "_BaseColor", Color.white);
        SetColor(material, "baseColorFactor", Color.white);
        SetFloat(material, "_Metallic", 0.705526f);
        SetFloat(material, "_Smoothness", 0.67238f);
        FixSolidHdrpMaterial(material);
        SetFloat(material, "_AlphaCutoffEnable", 1f);
        SetFloat(material, "_AlphaCutoff", 0.1f);
        SetFloat(material, "_DoubleSidedEnable", 1f);
        SetFloat(material, "_DoubleSidedNormalMode", 1f);
        SetFloat(material, "_CullMode", (float)CullMode.Off);
        SetFloat(material, "_CullModeForward", (float)CullMode.Off);
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        material.SetShaderPassEnabled("DepthOnly", true);
        material.SetShaderPassEnabled("ShadowCaster", true);
        TryValidateHdrpMaterial(material);
    }

    private static void RebindToHdrpUnlit(Material material)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit");
        if (shader != null && material.shader != shader)
            material.shader = shader;
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

    internal static void PrepareTransparentMaterial(
        Material material,
        ChevroletCamaro1967TransparentRole role = ChevroletCamaro1967TransparentRole.OtherClear)
    {
        var cabinGlass = role == ChevroletCamaro1967TransparentRole.CabinGlass;
        // Imported glTF transparency is not reliable in the game's HDRP build.
        // Exterior clear-surface variants use deterministic per-instance Lit
        // states. Authored gauges, symbols, and screens never enter this path.
        // Imported transparent Lit materials can collapse to opaque black in
        // the player build. Use the proven vehicle-glass HDRP/Unlit path.
        RebindToHdrpUnlit(material);
        var tint = cabinGlass
            ? new Color(0.08f, 0.10f, 0.12f, 0.16f)
            : role == ChevroletCamaro1967TransparentRole.HeadlampLens
                ? new Color(0.78f, 0.84f, 0.90f, 0.035f)
                : new Color(0.82f, 0.86f, 0.90f, 0.05f);
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
        SetFloat(material, "_EnableBlendModePreserveSpecularLighting", 0f);
        if (cabinGlass)
        {
            // The source Camaro windows are authored as dark tinted glass. In the
            // player HDRP build that texture behaves like an opaque black mask, so
            // keep the geometry but remove only its base-color texture.
            SetTexture(material, "_BaseColorMap", null, Vector2.one, Vector2.zero);
            SetTexture(material, "_UnlitColorMap", null, Vector2.one, Vector2.zero);
            SetTexture(material, "_MainTex", null, Vector2.one, Vector2.zero);
            SetTexture(material, "baseColorTexture", null, Vector2.one, Vector2.zero);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "metallicFactor", 0f);
            SetFloat(material, "_Smoothness", 0.95f);
            SetFloat(material, "roughnessFactor", 0f);
        }
        SetFloat(material, "_TransparentDepthPrepassEnable", 0f);
        SetFloat(material, "_TransparentDepthPostpassEnable", 0f);
        SetFloat(material, "_TransparentBackfaceEnable", cabinGlass ? 1f : 0f);
        SetFloat(material, "_Cull", cabinGlass ? (float)CullMode.Off : (float)CullMode.Back);
        SetFloat(material, "_CullMode", cabinGlass ? (float)CullMode.Off : (float)CullMode.Back);
        SetFloat(material, "_CullModeForward", cabinGlass ? (float)CullMode.Off : (float)CullMode.Back);
        SetFloat(material, "_TransparentCullMode", cabinGlass ? (float)CullMode.Off : (float)CullMode.Back);
        SetFloat(material, "_DoubleSidedEnable", cabinGlass ? 1f : 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (cabinGlass) material.EnableKeyword("_DOUBLESIDED_ON");
        else material.DisableKeyword("_DOUBLESIDED_ON");
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

    internal static void RestoreCabinGlassMaterial(Material material)
    {
        PrepareTransparentMaterial(material, ChevroletCamaro1967TransparentRole.CabinGlass);
    }

    internal static ChevroletCamaro1967TransparentRole GetTransparentRole(
        Renderer renderer,
        Material material)
    {
        if (IsBadgeMaterial(material))
            return ChevroletCamaro1967TransparentRole.Authored;

        // Geometry names are more trustworthy than imported glTF surface state:
        // the Camaro panes arrive looking opaque black in the player build.
        if (HasAncestor(renderer.transform, "LOD_A_GLASS_") ||
            HasAncestor(renderer.transform, "INT_INTERIOR_mm_windows") ||
            HasAncestor(renderer.transform, "in_glasss") ||
            renderer.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            renderer.name.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ChevroletCamaro1967TransparentRole.CabinGlass;
        }

        if (HasAncestor(renderer.transform, "HEADLIGHT_LENS"))
            return ChevroletCamaro1967TransparentRole.HeadlampLens;

        var name = material.name;
        if (name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ChevroletCamaro1967TransparentRole.CabinGlass;
        }

        return IsTransparentMaterial(material)
            ? ChevroletCamaro1967TransparentRole.OtherClear
            : ChevroletCamaro1967TransparentRole.Authored;
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
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

[AddComponentMenu("")]
public sealed class ChevroletCamaro1967MaterialController : MonoBehaviour
{
    private const string RimMaterialMarker = "material_11";
    private readonly List<Material> ownedMaterials = new List<Material>();
    private ChevroletCamaro1967MaterialFixResult result;
    private bool initialized;

    internal ChevroletCamaro1967MaterialFixResult Initialize(ModContext? context)
    {
        if (initialized)
            return result;

        initialized = true;
        var clones = new Dictionary<Material, Dictionary<ChevroletCamaro1967TransparentRole, Material>>();
        var rendererCount = 0;
        var transparentMaterials = 0;
        var authoredTransparentMaterials = 0;
        var badgeSlots = 0;
        var rimSlots = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!ChevroletCamaro1967Materials.IsCamaroRenderer(renderer.transform))
                continue;

            rendererCount++;
            var materials = renderer.sharedMaterials;
            var changed = false;
            var hasCabinGlass = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                    continue;

                var role = ChevroletCamaro1967Materials.GetTransparentRole(renderer, source);
                if (ChevroletCamaro1967Materials.IsBadgeMaterial(source))
                    badgeSlots++;
                if (!clones.TryGetValue(source, out var roleVariants))
                {
                    roleVariants = new Dictionary<ChevroletCamaro1967TransparentRole, Material>();
                    clones.Add(source, roleVariants);
                }
                if (!roleVariants.TryGetValue(role, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_CamaroInstance_" + role;
                    ChevroletCamaro1967Materials.NormalizeImportedMaterial(runtimeMaterial);
                    roleVariants.Add(role, runtimeMaterial);
                    ownedMaterials.Add(runtimeMaterial);

                    if (role != ChevroletCamaro1967TransparentRole.Authored)
                    {
                        ChevroletCamaro1967Materials.PrepareTransparentMaterial(runtimeMaterial, role);
                        transparentMaterials++;
                    }
                    else if (ChevroletCamaro1967Materials.IsTransparentMaterial(runtimeMaterial))
                    {
                        authoredTransparentMaterials++;
                    }
                    if (runtimeMaterial.name.IndexOf(
                            RimMaterialMarker,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetRimFinish(runtimeMaterial);
                    }
                }

                if (role == ChevroletCamaro1967TransparentRole.CabinGlass)
                    hasCabinGlass = true;
                if (runtimeMaterial.name.IndexOf(
                        RimMaterialMarker,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rimSlots++;
                }
                materials[index] = runtimeMaterial;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
            if (!hasCabinGlass)
                continue;
            cabinGlassRenderers++;
            if (!renderer.enabled || renderer.forceRenderingOff)
            {
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                cabinGlassRenderersReenabled++;
            }
        }

        result = new ChevroletCamaro1967MaterialFixResult(
            rendererCount,
            0,
            0,
            transparentMaterials,
            0,
            rimSlots,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
        ChevroletCamaro1967Diagnostics.Info(
            context,
            $"ChevroletCamaro1967 materials vehicle={GetInstanceID()}: cloned " +
            $"{ownedMaterials.Count} materials for {rendererCount} renderers, " +
            $"clearSurfaceVariants={transparentMaterials}, " +
            $"authoredTransparentRetained={authoredTransparentMaterials}, badgeSlots={badgeSlots}, " +
            $"cabinGlass={cabinGlassRenderers}, " +
            $"rimSlots={rimSlots}; opaque authored materials retained unchanged.");
        return result;
    }

    private static void SetRimFinish(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", ChevroletCamaro1967Materials.RimBaseColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", ChevroletCamaro1967Materials.RimBaseColor);
        if (material.HasProperty("baseColorFactor"))
            material.SetColor("baseColorFactor", ChevroletCamaro1967Materials.RimBaseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", ChevroletCamaro1967Materials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", ChevroletCamaro1967Materials.RimSmoothness);
    }

    private void OnDestroy()
    {
        foreach (var material in ownedMaterials)
        {
            if (material != null)
                Destroy(material);
        }
        ownedMaterials.Clear();
    }
}

[AddComponentMenu("")]
public sealed class ChevroletCamaro1967PaintController : MonoBehaviour
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");

    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly List<Material> ownedPanelMaterials = new List<Material>();
    private readonly List<BodyPaintTexture> bodyPaintTextures = new List<BodyPaintTexture>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();

    private VehicleController? vehicle;
    private string? explicitVehicleColorName;
    private VehicleColor? explicitVehicleColor;
    private ModContext? context;
    private string appliedColorName = string.Empty;
    private Color32 appliedTint;
    private bool hasAppliedTint;
    private bool initialized;
    private Coroutine? savedColorRecoveryCoroutine;
    private bool ambientTrafficOptimized;
    private const int AmbientTrafficTextureMaxSize = 512;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        ambientTrafficOptimized = false;
        if (!initialized)
        {
            initialized = true;
            FindPaintSlots();
        }
        ApplyCurrentColor("initialize");
        ScheduleSavedColorSettlement("initialize");
    }

    internal void RestoreAfterVehicleEntered() =>
        ScheduleSavedColorSettlement("vehicle-entered");

    internal void RestoreAfterSaveLoad(string source = "save-load") =>
        ScheduleSavedColorSettlement(source);

    internal void InitializeForPrivateDriver(
        string? vehicleColorName,
        VehicleColor? vehicleColor)
    {
        InitializeExternalPaint(
            vehicleColorName,
            vehicleColor,
            ambientTraffic: false,
            source: "private-driver");
    }

    internal void InitializeForAmbientTraffic(
        string? vehicleColorName,
        VehicleColor? vehicleColor)
    {
        InitializeExternalPaint(
            vehicleColorName,
            vehicleColor,
            ambientTraffic: true,
            source: "ambient-traffic");
    }

    private void InitializeExternalPaint(
        string? vehicleColorName,
        VehicleColor? vehicleColor,
        bool ambientTraffic,
        string source)
    {
        vehicle = null;
        context = null;
        explicitVehicleColorName = vehicleColorName;
        explicitVehicleColor = vehicleColor;
        appliedColorName = string.Empty;
        hasAppliedTint = false;

        if (!initialized)
        {
            initialized = true;
            ambientTrafficOptimized = ambientTraffic;
            FindPaintSlots(
                ambientTraffic ? AmbientTrafficTextureMaxSize : 0);
        }
        else if (ambientTrafficOptimized != ambientTraffic)
        {
            RestoreOriginalPaintMaterials();
            ambientTrafficOptimized = ambientTraffic;
            FindPaintSlots(
                ambientTraffic ? AmbientTrafficTextureMaxSize : 0);
        }

        ApplyCurrentColor(source);
    }

    internal bool HasAppliedColor => hasAppliedTint;

    internal void ApplyCurrentColor(string source)
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
        {
            context?.Logger.Warn(
                $"ChevroletCamaro1967 paint vehicle={vehicle?.GetInstanceID()}: no vehicle color " +
                $"was available during '{source}'.");
            return;
        }

        ApplyResolvedColor(selected, source, force: false);
    }

    private bool ApplyResolvedColor(
        VehicleColor selected,
        string source,
        bool force)
    {
        var colorName = ((UnityEngine.Object)selected).name;
        var tint = selected.tint;
        if (!force &&
            hasAppliedTint &&
            string.Equals(colorName, appliedColorName, StringComparison.Ordinal) &&
            tint.Equals(appliedTint))
        {
            return true;
        }

        var color = (Color)tint;
        color.a = 1f;
        var clearedRenderers = new HashSet<Renderer>();
        foreach (var slot in slots)
        {
            if (clearedRenderers.Add(slot.Renderer))
                slot.Renderer.SetPropertyBlock(null);

            if (slot.BodyTexture != null)
            {
                slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
                slot.BodyTexture.Apply(color);
                continue;
            }

            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor))
                properties.SetColor(BaseColor, color);
            if (slot.Material.HasProperty(ColorProperty))
                properties.SetColor(ColorProperty, color);
            if (slot.Material.HasProperty(BaseColorFactor))
                properties.SetColor(BaseColorFactor, color);
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        appliedColorName = colorName;
        appliedTint = tint;
        hasAppliedTint = true;
        ChevroletCamaro1967Diagnostics.PaintInfo(
            context,
            $"ChevroletCamaro1967 paint vehicle={vehicle?.GetInstanceID()}: applied " +
            $"color='{colorName}' rgba={tint} to {slots.Count} exterior body slots; " +
            $"frontDecoration={(UseDarkContrast(color) ? "black" : "white")} source='{source}' force={force}.");
        return true;
    }

    private void ScheduleSavedColorSettlement(string source)
    {
        if (vehicle == null)
            return;

        // Always try immediately first. If the saved VehicleColor registry entry
        // is already available this completes synchronously and no coroutine lives.
        if (TryRestorePersistedColor(source))
            return;

        var persistedName = vehicle.vehicleInstance?.vehicleColorName;
        if (string.IsNullOrEmpty(persistedName))
            return;

        // If the vehicle exists and has a persisted color name, but that
        // VehicleColor has not been registered yet, wait for the condition
        // instead of guessing a timeout.
        if (savedColorRecoveryCoroutine != null)
            StopCoroutine(savedColorRecoveryCoroutine);
        savedColorRecoveryCoroutine = StartCoroutine(
            WaitForPersistedColor(source));
    }

    private IEnumerator WaitForPersistedColor(string source)
    {
        while (vehicle != null && vehicle.vehicleInstance != null)
        {
            if (TryRestorePersistedColor(source))
                break;

            // No fixed "save must finish within N seconds" assumption. This
            // coroutine exists only for this loaded Camaro and stops immediately
            // once its persisted color can be resolved, or when the vehicle dies.
            yield return new WaitForSecondsRealtime(0.10f);
        }

        savedColorRecoveryCoroutine = null;
    }

    private bool TryRestorePersistedColor(string source)
    {
        if (vehicle?.vehicleInstance == null)
            return false;

        var savedName = vehicle.vehicleInstance.vehicleColorName;
        if (string.IsNullOrEmpty(savedName))
            return false;

        // Vehicle Repainter / DeveloperTools can own custom VehicleColor objects
        // that are intentionally not registered in VehicleHelper. Once another
        // lifecycle owner has restored such a color into CarFeatures, accept the
        // live object as authoritative when its persisted name matches.
        var liveColor = vehicle.CarFeatures?.VehicleColor;
        var liveName = liveColor != null
            ? ((UnityEngine.Object)liveColor).name
            : "<none>";
        if (liveColor != null &&
            string.Equals(liveName, savedName, StringComparison.Ordinal))
        {
            ApplyResolvedColor(liveColor, source + "-live-custom", force: true);
            return true;
        }

        if (!TryResolveVehicleColorByName(savedName, out var savedColor) ||
            savedColor == null)
        {
            return false;
        }

        if (vehicle.CarFeatures != null &&
            (!string.Equals(liveName, savedName, StringComparison.Ordinal) ||
             liveColor == null ||
             !liveColor.tint.Equals(savedColor.tint)))
        {
            vehicle.CarFeatures.SetColor(savedColor);
        }

        ApplyResolvedColor(savedColor, source + "-persisted", force: true);
        return true;
    }

    private void FindPaintSlots(int maxTextureSize = 0)
    {
        slots.Clear();
        ReleaseBodyTextures();
        foreach (var material in ownedPanelMaterials)
            if (material != null) Destroy(material);
        ownedPanelMaterials.Clear();

        var bodySlots = 0;
        var texturedBodySlots = 0;
        var mapped = new List<string>();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!ChevroletCamaro1967Materials.IsCamaroRenderer(renderer.transform) ||
                !IsExteriorPaintRenderer(renderer.transform))
            {
                continue;
            }

            var materials = renderer.sharedMaterials;
            var bodyIndex = GetDominantOpaqueMaterialIndex(renderer, materials);
            if (bodyIndex < 0 || bodyIndex >= materials.Length)
                continue;

            var source = materials[bodyIndex];
            if (source == null)
                continue;

            var bodyTexture = BodyPaintTexture.Create(source, maxTextureSize);
            Material runtimeMaterial;
            if (bodyTexture != null)
            {
                bodyPaintTextures.Add(bodyTexture);
                runtimeMaterial = bodyTexture.Material;
                texturedBodySlots++;
            }
            else
            {
                runtimeMaterial = Instantiate(source);
                runtimeMaterial.name = source.name + "_CamaroBodyPaint";
                ChevroletCamaro1967Materials.NormalizeImportedMaterial(runtimeMaterial);
                NeutralizeBodyPaintTexture(runtimeMaterial);
                ownedPanelMaterials.Add(runtimeMaterial);
            }

            materials[bodyIndex] = runtimeMaterial;
            renderer.sharedMaterials = materials;
            slots.Add(new PaintSlot(
                renderer,
                source,
                runtimeMaterial,
                bodyIndex,
                bodyTexture));
            bodySlots++;
            mapped.Add($"{renderer.name}[{bodyIndex}]={source.name}");
        }

        ChevroletCamaro1967Diagnostics.PaintInfo(
            context,
            $"ChevroletCamaro1967 paint vehicle={vehicle?.GetInstanceID()}: mapped " +
            $"bodySlots={bodySlots}, texturePreserved={texturedBodySlots}, " +
            $"ambientTrafficOptimized={ambientTrafficOptimized}, maxTextureSize={maxTextureSize}, " +
            $"dominantSubmeshes=[{string.Join(", ", mapped)}]; front stripe uses adaptive contrast; SS 396 emblem retains authored pixels.");

        if (bodySlots == 0)
            context?.Logger.Warn("ChevroletCamaro1967 paint mapping found no exterior body slots.");
    }

    private static int GetDominantOpaqueMaterialIndex(Renderer renderer, Material[] materials)
    {
        Mesh? mesh = null;
        if (renderer is SkinnedMeshRenderer skinned)
            mesh = skinned.sharedMesh;
        else
            mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;

        var bestIndex = -1;
        uint bestIndexCount = 0;
        for (var index = 0; index < materials.Length; index++)
        {
            var material = materials[index];
            if (material == null || ChevroletCamaro1967Materials.IsTransparentMaterial(material))
                continue;

            var indexCount = mesh != null && index < mesh.subMeshCount
                ? mesh.GetIndexCount(index)
                : 1u;
            if (bestIndex >= 0 && indexCount <= bestIndexCount)
                continue;

            bestIndex = index;
            bestIndexCount = indexCount;
        }

        return bestIndex;
    }

    private static void NeutralizeBodyPaintTexture(Material material)
    {
        if (material.HasProperty("_BaseColorMap"))
            material.SetTexture("_BaseColorMap", null);
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", null);
        if (material.HasProperty("baseColorTexture"))
            material.SetTexture("baseColorTexture", null);

        SetMaterialColor(material, Color.white);
    }

    private static bool UseDarkContrast(Color paint)
    {
        var luminance = paint.r * 0.2126f + paint.g * 0.7152f + paint.b * 0.0722f;
        return luminance >= 0.46f;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        color.a = 1f;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", color);
        if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", color);
    }

    private static bool IsExteriorPaintRenderer(Transform transform)
    {
        return HasAncestor(transform, "LOD_A_BODY_mm_ext") ||
               HasAncestor(transform, "LOD_A_BOOT_mm_ext") ||
               HasAncestor(transform, "LOD_A_HOOD_mm_ext") ||
               HasAncestor(transform, "LOD_A_DOOR_LEFT_mm_ext") ||
               HasAncestor(transform, "LOD_A_DOOR_RIGHT_mm_ext") ||
               HasAncestor(transform, "CamaroDamageBody");
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private VehicleColor? ResolveVehicleColor()
    {
        // During save-load / entry settlement, the persisted vehicleColorName is
        // authoritative. CarFeatures can transiently contain the prefab's black
        // default and would otherwise overwrite the restored texture again.
        if (vehicle != null && savedColorRecoveryCoroutine != null)
        {
            var persistedName = vehicle.vehicleInstance?.vehicleColorName;
            if (!string.IsNullOrEmpty(persistedName) &&
                VehicleHelper.TryGetVehicleColor(persistedName, out var persisted) &&
                persisted != null)
            {
                return persisted;
            }
        }

        var live = vehicle?.CarFeatures?.VehicleColor;
        if (live != null)
            return live;
        if (explicitVehicleColor != null)
            return explicitVehicleColor;
        var colorName = vehicle?.vehicleInstance?.vehicleColorName ??
                        explicitVehicleColorName;
        return !string.IsNullOrEmpty(colorName) &&
               TryResolveVehicleColorByName(colorName!, out var saved)
            ? saved
            : null;
    }

    private static bool TryResolveVehicleColorByName(
        string colorName,
        out VehicleColor vehicleColor)
    {
        if (VehicleHelper.TryGetVehicleColor(colorName, out vehicleColor) &&
            vehicleColor != null)
        {
            return true;
        }

        // Vehicle Repainter / DeveloperTools may keep additional colors as
        // private ScriptableObjects instead of registering them in VehicleHelper.
        // They are already loaded before parked vehicle visuals settle.
        foreach (var candidate in Resources.FindObjectsOfTypeAll<VehicleColor>())
        {
            if (candidate == null ||
                !string.Equals(
                    ((UnityEngine.Object)candidate).name,
                    colorName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            vehicleColor = candidate;
            return true;
        }

        vehicleColor = null!;
        return false;
    }

    private void RestoreOriginalPaintMaterials()
    {
        foreach (var slot in slots)
        {
            if (slot.Renderer == null || slot.SourceMaterial == null)
                continue;
            var materials = slot.Renderer.sharedMaterials;
            if (slot.MaterialIndex < 0 || slot.MaterialIndex >= materials.Length)
                continue;
            materials[slot.MaterialIndex] = slot.SourceMaterial;
            slot.Renderer.sharedMaterials = materials;
            slot.Renderer.SetPropertyBlock(null, slot.MaterialIndex);
        }

        slots.Clear();
        ReleaseBodyTextures();
        foreach (var material in ownedPanelMaterials)
            if (material != null) Destroy(material);
        ownedPanelMaterials.Clear();
    }

    private void ReleaseBodyTextures()
    {
        foreach (var state in bodyPaintTextures)
            state?.Dispose();
        bodyPaintTextures.Clear();
    }

    private void OnDestroy()
    {
        if (savedColorRecoveryCoroutine != null)
            StopCoroutine(savedColorRecoveryCoroutine);
        savedColorRecoveryCoroutine = null;
        ReleaseBodyTextures();
        foreach (var material in ownedPanelMaterials)
            if (material != null) Destroy(material);
        ownedPanelMaterials.Clear();
    }

    private readonly struct PaintSlot
    {
        internal PaintSlot(
            Renderer renderer,
            Material sourceMaterial,
            Material material,
            int materialIndex,
            BodyPaintTexture? bodyTexture)
        {
            Renderer = renderer;
            SourceMaterial = sourceMaterial;
            Material = material;
            MaterialIndex = materialIndex;
            BodyTexture = bodyTexture;
        }

        internal readonly Renderer Renderer;
        internal readonly Material SourceMaterial;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
        internal readonly BodyPaintTexture? BodyTexture;
    }

    private sealed class BodyPaintTexture
    {
        private readonly Texture2D texture;
        private readonly Color32[] sourcePixels;
        private readonly Color32[] outputPixels;
        private readonly bool[] decorationPixels;
        private readonly bool[] fixedEmblemPixels;
        private readonly byte[] shadeMultipliers;
        private readonly bool updateMipmaps;
        private bool? lastDarkContrast;
        private Color32 lastPaint;
        private bool hasPaint;

        private BodyPaintTexture(
            Material material,
            Texture2D texture,
            Color32[] sourcePixels,
            bool[] decorationPixels,
            bool[] fixedEmblemPixels,
            byte[] shadeMultipliers,
            bool updateMipmaps)
        {
            Material = material;
            this.texture = texture;
            this.sourcePixels = sourcePixels;
            outputPixels = new Color32[sourcePixels.Length];
            this.decorationPixels = decorationPixels;
            this.fixedEmblemPixels = fixedEmblemPixels;
            this.shadeMultipliers = shadeMultipliers;
            this.updateMipmaps = updateMipmaps;
        }

        internal Material Material { get; }

        internal static BodyPaintTexture? Create(
            Material sourceMaterial,
            int maxTextureSize)
        {
            var sourceTexture = FindBaseTexture(sourceMaterial);
            if (sourceTexture == null)
                return null;

            var sourceWidth = sourceTexture.width;
            var sourceHeight = sourceTexture.height;
            var targetWidth = sourceWidth;
            var targetHeight = sourceHeight;
            if (maxTextureSize > 0)
            {
                var longest = Math.Max(sourceWidth, sourceHeight);
                if (longest > maxTextureSize)
                {
                    var scale = maxTextureSize / (float)longest;
                    targetWidth = Math.Max(1, Mathf.RoundToInt(sourceWidth * scale));
                    targetHeight = Math.Max(1, Mathf.RoundToInt(sourceHeight * scale));
                }
            }

            var trafficOptimized = maxTextureSize > 0;
            var temporary = RenderTexture.GetTemporary(
                targetWidth,
                targetHeight,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.Default);
            var previous = RenderTexture.active;
            Texture2D readable;
            try
            {
                Graphics.Blit(sourceTexture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(
                    targetWidth,
                    targetHeight,
                    TextureFormat.RGBA32,
                    !trafficOptimized,
                    false)
                {
                    name = sourceTexture.name +
                           (trafficOptimized
                               ? "_CamaroTrafficPaint512"
                               : "_CamaroRuntimePaint"),
                    hideFlags = HideFlags.DontSave,
                    filterMode = sourceTexture.filterMode,
                    wrapMode = sourceTexture.wrapMode,
                    anisoLevel = trafficOptimized
                        ? 1
                        : sourceTexture.anisoLevel,
                };
                readable.ReadPixels(
                    new Rect(0f, 0f, targetWidth, targetHeight),
                    0,
                    0,
                    false);
                readable.Apply(!trafficOptimized, false);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }

            var runtimeMaterial = UnityEngine.Object.Instantiate(sourceMaterial);
            runtimeMaterial.name = sourceMaterial.name +
                                   (trafficOptimized
                                       ? "_CamaroTrafficPaint"
                                       : "_CamaroRuntimePaint");
            runtimeMaterial.hideFlags = HideFlags.DontSave;
            ChevroletCamaro1967Materials.NormalizeImportedMaterial(runtimeMaterial);
            SetTexture(runtimeMaterial, "_BaseColorMap", readable);
            SetTexture(runtimeMaterial, "_MainTex", readable);
            SetTexture(runtimeMaterial, "baseColorTexture", readable);
            SetMaterialColor(runtimeMaterial, Color.white);

            var pixels = readable.GetPixels32();
            var decoration = new bool[pixels.Length];
            var fixedEmblem = new bool[pixels.Length];
            var shade = new byte[pixels.Length];
            for (var index = 0; index < pixels.Length; index++)
            {
                // Source EXT atlas, measured in the original 1024-reference image:
                // SS 396 including its black surround occupies x=720..880,
                // y=200..283 from the top. ReadPixels uses a bottom-left origin.
                // Normalize coordinates so the same mask works for traffic's 512 atlas.
                var atlasX = (index % targetWidth + 0.5f) * 1024f / targetWidth;
                var atlasY = (1f - (index / targetWidth + 0.5f) / targetHeight) * 1024f;
                fixedEmblem[index] = atlasX >= 720f && atlasX < 880f &&
                                     atlasY >= 200f && atlasY < 283f;
                var source = (Color)pixels[index];
                Color.RGBToHSV(source, out _, out var saturation, out var value);
                decoration[index] =
                    source.a > 0.02f &&
                    saturation < 0.22f &&
                    value > 0.58f;
                shade[index] = (byte)Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Lerp(0.88f, 1f, value) * 255f),
                    0,
                    255);
            }

            return new BodyPaintTexture(
                runtimeMaterial,
                readable,
                pixels,
                decoration,
                fixedEmblem,
                shade,
                updateMipmaps: !trafficOptimized);
        }

        internal void Apply(Color paint)
        {
            var darkContrast = UseDarkContrast(paint);
            var paintBytes = (Color32)paint;
            if (hasPaint && lastPaint.Equals(paintBytes) && lastDarkContrast == darkContrast)
                return;

            for (var index = 0; index < sourcePixels.Length; index++)
            {
                var source = sourcePixels[index];
                if (fixedEmblemPixels[index])
                {
                    outputPixels[index] = source;
                    continue;
                }
                if (decorationPixels[index])
                {
                    var shade = darkContrast ? (byte)18 : (byte)245;
                    outputPixels[index] = new Color32(shade, shade, shade, source.a);
                    continue;
                }

                var shadeMultiplier = shadeMultipliers[index];
                outputPixels[index] = new Color32(
                    (byte)((paintBytes.r * shadeMultiplier + 127) / 255),
                    (byte)((paintBytes.g * shadeMultiplier + 127) / 255),
                    (byte)((paintBytes.b * shadeMultiplier + 127) / 255),
                    source.a);
            }

            texture.SetPixels32(outputPixels);
            texture.Apply(updateMipmaps, false);
            SetMaterialColor(Material, Color.white);
            hasPaint = true;
            lastPaint = paintBytes;
            lastDarkContrast = darkContrast;
        }

        internal void Dispose()
        {
            if (Material != null) UnityEngine.Object.Destroy(Material);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }

        private static Texture? FindBaseTexture(Material material)
        {
            if (material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") != null)
                return material.GetTexture("_BaseColorMap");
            if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null)
                return material.GetTexture("_MainTex");
            if (material.HasProperty("baseColorTexture") && material.GetTexture("baseColorTexture") != null)
                return material.GetTexture("baseColorTexture");
            return null;
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property))
                material.SetTexture(property, texture);
        }
    }
}

