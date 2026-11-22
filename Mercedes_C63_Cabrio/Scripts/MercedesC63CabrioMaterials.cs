#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct MercedesC63CabrioMaterialFixResult
{
    public MercedesC63CabrioMaterialFixResult(
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

internal enum MercedesC63CabrioTransparentRole
{
    Authored,
    CabinGlass,
    HeadlampLens,
    OtherClear,
}

public static class MercedesC63CabrioMaterials
{
    public const float RimMetallic = 0.65f;
    public const float RimSmoothness = 0.45f;
    public static readonly Color RimBaseColor = new Color(0.03f, 0.03f, 0.03f, 1f);

    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string RimMaterialMarker = "M_Rim_Main_Max1";
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static MercedesC63CabrioMaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var controller = vehicle.GetComponent<MercedesC63CabrioMaterialController>();
        if (controller == null)
            controller = vehicle.AddComponent<MercedesC63CabrioMaterialController>();
        return controller.Initialize(null);
    }

    private static Material? FindCanonicalRimMaterial(GameObject vehicle)
    {
        Material? fallback = null;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsC63Renderer(renderer.transform))
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
        if (name.IndexOf("C63CarPaint", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("C63Rim", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("C63Caliper", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("phong5SG1", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("phong14SG1", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("phong15SG1", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("phong20SG1", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        return material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f;
    }

    public static bool IsC63Renderer(Transform transform)
    {
        if (transform.name.StartsWith("C63DamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("C63Wheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("C63FixedCaliper", StringComparison.Ordinal))
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("C63DamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "C63Visual", StringComparison.Ordinal) ||
                current.name.StartsWith("C63Wheel", StringComparison.Ordinal) ||
                current.name.StartsWith("C63FixedCaliper", StringComparison.Ordinal))
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

    // Editor setup uses this on saved assets without attaching runtime components.
    public static void PreparePersistentMaterial(Renderer renderer, Material material)
    {
        var role = GetTransparentRole(renderer, material);
        if (role != MercedesC63CabrioTransparentRole.Authored)
            PrepareTransparentMaterial(material, role);
        else
        {
            RebindToHdrpLit(material);
            FixSolidHdrpMaterial(material);
        }
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

    internal static bool FixSolidHdrpMaterial(Material material)
    {
        var alphaMasked = material.HasProperty("_AlphaCutoffEnable") &&
                          material.GetFloat("_AlphaCutoffEnable") > .5f;
        SetOpaqueColor(material, "_BaseColor");
        SetOpaqueColor(material, "baseColorFactor");
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_AlphaCutoffEnable", alphaMasked ? 1f : 0f);
        SetFloat(material, "_SupportDecals", 0f);
        SetFloat(material, "_ReceivesSSR", 0f);
        SetFloat(material, "_ReceivesSSRTransparent", 0f);
        SetFloat(material, "_RefractionModel", 0f);
        material.renderQueue = (int)RenderQueue.Geometry;
        material.SetOverrideTag("RenderType", alphaMasked ? "TransparentCutout" : "Opaque");
        if (alphaMasked) material.renderQueue = (int)RenderQueue.AlphaTest;

        var validated = TryValidateHdrpMaterial(material);
        // The supplied cabin and inner body panels are authored double sided.
        // Keep that state in both saved assets and instance variants.
        if (material.HasProperty("_DoubleSidedEnable") && material.GetFloat("_DoubleSidedEnable") > .5f)
        {
            material.EnableKeyword("_DOUBLESIDED_ON");
            SetFloat(material, "_CullMode", 0f);
            SetFloat(material, "_CullModeForward", 0f);
            SetFloat(material, "_OpaqueCullMode", 0f);
        }
        if (material.name.IndexOf("cabriophong1SG1", StringComparison.Ordinal) >= 0)
            PrepareBadgeMaterial(material);
        if (alphaMasked) material.EnableKeyword("_ALPHATEST_ON");
        else material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        // Imported BLEND materials can retain disabled opaque passes when
        // converted to solids. Alpha-tested GBuffer rendering requires DepthOnly.
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetShaderPassEnabled("DepthOnly", true);
        material.SetShaderPassEnabled("ShadowCaster", true);
        material.SetShaderPassEnabled("GBuffer", true);
        material.SetShaderPassEnabled("Forward", true);
        material.SetShaderPassEnabled("ForwardOnly", true);
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        return validated;
    }

    internal static void PrepareBadgeMaterial(Material material)
    {
        // The atlas contains silver stars/text, not a mirror. Preserve a
        // diffuse response when the game's reflection probe is dark.
        SetFloat(material, "_Metallic", .25f);
        SetFloat(material, "_Smoothness", .45f);
        SetFloat(material, "_AlphaCutoff", .15f);
        SetFloat(material, "_AlphaCutoffPrepass", .15f);
        SetFloat(material, "_AlphaCutoffPostpass", .15f);
        SetFloat(material, "_AlphaCutoffShadow", .15f);
        SetFloat(material, "_ZTestDepthEqualForOpaque", (float)CompareFunction.LessEqual);
    }

    internal static void PrepareTransparentMaterial(
        Material material,
        MercedesC63CabrioTransparentRole role = MercedesC63CabrioTransparentRole.OtherClear)
    {
        var cabinGlass = role == MercedesC63CabrioTransparentRole.CabinGlass;
        // Imported glTF transparency is not reliable in the game's HDRP build.
        // Exterior clear-surface variants use deterministic per-instance Lit
        // states. Authored gauges, symbols, and screens never enter this path.
        RebindToHdrpLit(material);
        var tint = cabinGlass
            ? new Color(0.09f, 0.12f, 0.15f, 0.14f)
            : role == MercedesC63CabrioTransparentRole.HeadlampLens
                ? new Color(0.78f, 0.84f, 0.90f, 0.035f)
                : new Color(0.82f, 0.86f, 0.90f, 0.05f);
        // Retain the authored red rear-lens tint while clearing the neutral
        // front/internal covers. Both use transparent HDRP surface state.
        if (role == MercedesC63CabrioTransparentRole.OtherClear &&
            material.name.IndexOf("phong15SG1", StringComparison.OrdinalIgnoreCase) >= 0)
            tint = new Color(.5f, 0f, 0f, .25f);
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
        SetFloat(material, "_RefractionModel", 0f);
        if (cabinGlass || role == MercedesC63CabrioTransparentRole.HeadlampLens ||
            role == MercedesC63CabrioTransparentRole.OtherClear)
        {
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "metallicFactor", 0f);
            SetFloat(material, "_Smoothness", 0.95f);
            SetFloat(material, "roughnessFactor", 0f);
        }
        SetFloat(material, "_TransparentDepthPrepassEnable", 0f);
        SetFloat(material, "_TransparentDepthPostpassEnable", 0f);
        SetFloat(material, "_TransparentBackfaceEnable", 0f);
        SetFloat(material, "_Cull", (float)CullMode.Back);
        SetFloat(material, "_CullMode", (float)CullMode.Back);
        SetFloat(material, "_CullModeForward", (float)CullMode.Back);
        SetFloat(material, "_TransparentCullMode", (float)CullMode.Back);
        SetFloat(material, "_DoubleSidedEnable", 0f);
        // Rebuild HDRP's material state after changing surface type. This also
        // enables the forward transparent pass when the source was opaque.
        TryValidateHdrpMaterial(material);
        material.SetShaderPassEnabled("Forward", true);
        material.SetShaderPassEnabled("ForwardOnly", true);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_DOUBLESIDED_ON");
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
        PrepareTransparentMaterial(material, MercedesC63CabrioTransparentRole.CabinGlass);
    }

    internal static MercedesC63CabrioTransparentRole GetTransparentRole(
        Renderer renderer,
        Material material)
    {
        if (HasAncestor(renderer.transform, "M_LGlass_OuterClear_SetRT_Max") ||
            material.name.IndexOf("phong20SG1", StringComparison.OrdinalIgnoreCase) >= 0)
            return MercedesC63CabrioTransparentRole.HeadlampLens;
        if (HasAncestor(renderer.transform, "M_LGlass_InnerClear_Low"))
            return MercedesC63CabrioTransparentRole.OtherClear;
        if (!IsTransparentMaterial(material))
            return MercedesC63CabrioTransparentRole.Authored;
        if (HasAncestor(renderer.transform, "M_Glass_WindowFront_Max") ||
            HasAncestor(renderer.transform, "M_Glass_WindowSide_Max") ||
            HasAncestor(renderer.transform, "M_Glass_WindowRear_Max"))
            return MercedesC63CabrioTransparentRole.CabinGlass;
        var name = material.name;
        return name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
            ? MercedesC63CabrioTransparentRole.OtherClear
            : MercedesC63CabrioTransparentRole.Authored;
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
public sealed class MercedesC63CabrioMaterialController : MonoBehaviour
{
    private const string RimMaterialMarker = "M_Rim_Main_Max1";
    private readonly List<Material> ownedMaterials = new List<Material>();
    private MercedesC63CabrioMaterialFixResult result;
    private bool initialized;

    internal MercedesC63CabrioMaterialFixResult Initialize(ModContext? context)
    {
        if (initialized)
            return result;

        initialized = true;
        var clones = new Dictionary<Material, Dictionary<MercedesC63CabrioTransparentRole, Material>>();
        var rendererCount = 0;
        var transparentMaterials = 0;
        var authoredTransparentMaterials = 0;
        var badgeMaterials = 0;
        var opaqueMaterials = 0;
        var validatedMaterials = 0;
        var rimSlots = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!MercedesC63CabrioMaterials.IsC63Renderer(renderer.transform))
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

                var role = MercedesC63CabrioMaterials.GetTransparentRole(renderer, source);
                if (!clones.TryGetValue(source, out var roleVariants))
                {
                    roleVariants = new Dictionary<MercedesC63CabrioTransparentRole, Material>();
                    clones.Add(source, roleVariants);
                }
                if (!roleVariants.TryGetValue(role, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_C63Instance_" + role;
                    roleVariants.Add(role, runtimeMaterial);
                    ownedMaterials.Add(runtimeMaterial);

                    if (role != MercedesC63CabrioTransparentRole.Authored)
                    {
                        MercedesC63CabrioMaterials.PrepareTransparentMaterial(runtimeMaterial, role);
                        transparentMaterials++;
                    }
                    else if (MercedesC63CabrioMaterials.IsTransparentMaterial(runtimeMaterial))
                    {
                        authoredTransparentMaterials++;
                    }
                    else
                    {
                        if (MercedesC63CabrioMaterials.FixSolidHdrpMaterial(runtimeMaterial)) validatedMaterials++;
                        opaqueMaterials++;
                    }
                    if (role == MercedesC63CabrioTransparentRole.Authored &&
                        runtimeMaterial.name.IndexOf("cabriophong1SG1", StringComparison.Ordinal) >= 0)
                    {
                        MercedesC63CabrioMaterials.PrepareBadgeMaterial(runtimeMaterial);
                        badgeMaterials++;
                    }
                    if (runtimeMaterial.name.IndexOf(
                            RimMaterialMarker,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetRimFinish(runtimeMaterial);
                    }
                }

                if (role == MercedesC63CabrioTransparentRole.CabinGlass)
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

        result = new MercedesC63CabrioMaterialFixResult(
            rendererCount,
            0,
            opaqueMaterials,
            transparentMaterials,
            validatedMaterials,
            rimSlots,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
        MercedesC63CabrioDiagnostics.Info(
            context,
            $"MercedesC63Cabrio materials vehicle={GetInstanceID()}: cloned " +
            $"{ownedMaterials.Count} materials for {rendererCount} renderers, " +
            $"clearSurfaceVariants={transparentMaterials}, " +
            $"authoredTransparentRetained={authoredTransparentMaterials}, " +
            $"cabinGlass={cabinGlassRenderers}, " +
            $"rimSlots={rimSlots}, badgeVariants={badgeMaterials}, opaqueVariants={opaqueMaterials}, " +
            $"validated={validatedMaterials}; opaque depth/shadow coverage restored.");
        return result;
    }

    private static void SetRimFinish(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", MercedesC63CabrioMaterials.RimBaseColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", MercedesC63CabrioMaterials.RimBaseColor);
        if (material.HasProperty("baseColorFactor"))
            material.SetColor("baseColorFactor", MercedesC63CabrioMaterials.RimBaseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", MercedesC63CabrioMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", MercedesC63CabrioMaterials.RimSmoothness);
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
public sealed class MercedesC63CabrioPaintController : MonoBehaviour
{
    private const string BodyMaterialMarker = "C63CarPaint";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly List<Material> ownedPanelMaterials = new List<Material>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private VehicleController? vehicle;
    private string? explicitVehicleColorName;
    private VehicleColor? explicitVehicleColor;
    private ModContext? context;
    private string appliedColorName = string.Empty;
    private Color32 appliedTint;
    private bool hasAppliedTint;
    private bool initialized;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        if (!initialized)
        {
            initialized = true;
            FindPaintSlots();
        }
        ApplyCurrentColor("initialize");
    }

    internal void InitializeForPrivateDriver(
        string? vehicleColorName,
        VehicleColor? vehicleColor)
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
            FindPaintSlots();
        }
        ApplyCurrentColor("private-driver");
    }

    internal bool HasAppliedColor => hasAppliedTint;

    internal void ApplyCurrentColor(string source)
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
        {
            context?.Logger.Warn(
                $"MercedesC63Cabrio paint vehicle={vehicle?.GetInstanceID()}: no vehicle color " +
                $"was available during '{source}'.");
            return;
        }

        var colorName = ((UnityEngine.Object)selected).name;
        var tint = selected.tint;
        if (hasAppliedTint &&
            string.Equals(colorName, appliedColorName, StringComparison.Ordinal) &&
            tint.Equals(appliedTint))
        {
            return;
        }

        var color = (Color)tint;
        color.a = 1f;
        foreach (var slot in slots)
        {
            var slotColor = slot.Category == PaintCategory.InteriorAccent
                ? Color.Lerp(color, Color.white, 0.12f)
                : color;
            slotColor.a = 1f;
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor))
                properties.SetColor(BaseColor, slotColor);
            if (slot.Material.HasProperty(ColorProperty))
                properties.SetColor(ColorProperty, slotColor);
            if (slot.Material.HasProperty(BaseColorFactor))
                properties.SetColor(BaseColorFactor, slotColor);
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        appliedColorName = colorName;
        appliedTint = tint;
        hasAppliedTint = true;
        MercedesC63CabrioDiagnostics.PaintInfo(
            context,
            $"MercedesC63Cabrio paint vehicle={vehicle?.GetInstanceID()}: applied " +
            $"color='{colorName}' rgba={tint} to {slots.Count} body/interior slots " +
            $"source='{source}'.");
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        var bodySlots = 0;
        var interiorAccentSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!MercedesC63CabrioMaterials.IsC63Renderer(renderer.transform))
                continue;
            var materials = renderer.sharedMaterials;
            var materialsChanged = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null && material.name.IndexOf(
                        BodyMaterialMarker,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    slots.Add(new PaintSlot(renderer, material, index, PaintCategory.Body));
                    bodySlots++;
                }
                else if (material != null && IsExteriorPaintCover(renderer, material))
                {
                    // The supplied model places a textured cover over the roof
                    // paint shell. The hood center and fender vents are factory
                    // black trim and are intentionally excluded here.
                    var panelMaterial = Instantiate(material);
                    panelMaterial.name = material.name + "_PaintSurface";
                    PrepareExteriorPaintSurface(panelMaterial);
                    materials[index] = panelMaterial;
                    ownedPanelMaterials.Add(panelMaterial);
                    slots.Add(new PaintSlot(
                        renderer,
                        panelMaterial,
                        index,
                        PaintCategory.Body));
                    bodySlots++;
                    materialsChanged = true;
                }
                else if (material != null && IsInteriorAccent(renderer, material))
                {
                    slots.Add(new PaintSlot(
                        renderer,
                        material,
                        index,
                        PaintCategory.InteriorAccent));
                    interiorAccentSlots++;
                }
            }
            if (materialsChanged)
                renderer.sharedMaterials = materials;
        }

        MercedesC63CabrioDiagnostics.PaintInfo(
            context,
            $"MercedesC63Cabrio paint vehicle={vehicle?.GetInstanceID()}: mapped " +
            $"bodySlots={bodySlots}, interiorAccentSlots={interiorAccentSlots}; " +
            "glass, lamps, unmapped carbon, rims, brakes, and black trim excluded.");
        if (bodySlots == 0)
            context?.Logger.Warn(
                $"MercedesC63Cabrio paint mapping incomplete bodySlots={bodySlots}, " +
                $"interiorAccentSlots={interiorAccentSlots}.");
    }

    private static bool IsInteriorAccent(Renderer renderer, Material material)
    {
        var materialName = material.name;
        return materialName.IndexOf("stitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
               materialName.IndexOf("seat_leather_2", StringComparison.OrdinalIgnoreCase) >= 0 ||
               materialName.IndexOf("B60000", StringComparison.OrdinalIgnoreCase) >= 0 ||
               ((HasAncestor(renderer.transform, "seat_") ||
                 HasAncestor(renderer.transform, "seats_R")) &&
                materialName.IndexOf("_red", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsExteriorPaintCover(Renderer renderer, Material material)
    {
        return false;
    }

    private static void PrepareExteriorPaintSurface(Material material)
    {
        if (material.HasProperty("_BaseColorMap"))
            material.SetTexture("_BaseColorMap", Texture2D.whiteTexture);
        if (material.HasProperty("baseColorTexture"))
            material.SetTexture("baseColorTexture", Texture2D.whiteTexture);
        if (material.HasProperty("_NormalMap"))
            material.SetTexture("_NormalMap", null);
        if (material.HasProperty("_MaskMap"))
            material.SetTexture("_MaskMap", null);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0.12f);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.86f);
        if (material.HasProperty("_CoatMask"))
            material.SetFloat("_CoatMask", 0.20f);
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
        var live = vehicle?.CarFeatures?.VehicleColor;
        if (live != null)
            return live;
        if (explicitVehicleColor != null)
            return explicitVehicleColor;
        var colorName = vehicle?.vehicleInstance?.vehicleColorName ??
                        explicitVehicleColorName;
        return !string.IsNullOrEmpty(colorName) &&
               VehicleHelper.TryGetVehicleColor(colorName, out var saved)
            ? saved
            : null;
    }

    private void OnDestroy()
    {
        foreach (var material in ownedPanelMaterials)
            if (material != null) Destroy(material);
        ownedPanelMaterials.Clear();
    }

    private readonly struct PaintSlot
    {
        internal PaintSlot(
            Renderer renderer,
            Material material,
            int materialIndex,
            PaintCategory category)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
            Category = category;
        }

        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
        internal readonly PaintCategory Category;
    }

    private enum PaintCategory
    {
        Body,
        InteriorAccent,
    }
}

