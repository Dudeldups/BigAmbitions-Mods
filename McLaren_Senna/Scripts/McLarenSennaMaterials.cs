#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct McLarenSennaMaterialFixResult
{
    public McLarenSennaMaterialFixResult(
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

internal enum McLarenSennaTransparentRole
{
    Authored,
    CabinGlass,
    HeadlampLens,
    OtherClear,
}

public static class McLarenSennaMaterials
{
    public const float RimMetallic = 0.65f;
    public const float RimSmoothness = 0.45f;
    public static readonly Color RimBaseColor = new Color(0.03f, 0.03f, 0.03f, 1f);

    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string RimMaterialMarker = "Wheel1A";
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static McLarenSennaMaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var controller = vehicle.GetComponent<McLarenSennaMaterialController>();
        if (controller == null)
            controller = vehicle.AddComponent<McLarenSennaMaterialController>();
        return controller.Initialize(null);
    }

    private static Material? FindCanonicalRimMaterial(GameObject vehicle)
    {
        Material? fallback = null;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsMcLarenRenderer(renderer.transform))
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

    public static bool IsMcLarenRenderer(Transform transform)
    {
        if (transform.name.StartsWith("McLarenDamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("McLarenWheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("McLarenFixedCaliper", StringComparison.Ordinal))
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("McLarenDamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "McLarenVisual", StringComparison.Ordinal) ||
                current.name.StartsWith("McLarenWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("McLarenFixedCaliper", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
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
        var emission = GetColor(material, "emissiveFactor", "_EmissiveColor", Color.black);
        var emissionTextureProperty = FirstTextureProperty(material, "emissiveTexture", "_EmissiveColorMap");
        var emissionTexture = emissionTextureProperty == null ? null : material.GetTexture(emissionTextureProperty);
        var metallic = GetFloat(material, "metallicFactor", "_Metallic", null, 0f);
        var roughness = GetFloat(material, "roughnessFactor", null, null, 1f);

        material.shader = hdrpLit;
        SetColor(material, "_BaseColor", color);
        SetTexture(material, "_BaseColorMap", baseTexture, baseScale, baseOffset);
        SetTexture(material, "_NormalMap", normalTexture, Vector2.one, Vector2.zero);
        SetFloat(material, "_NormalScale", normalScale);
        SetFloat(material, "_Metallic", metallic);
        SetFloat(material, "_Smoothness", 1f - Mathf.Clamp01(roughness));
        SetColor(material, "_EmissiveColor", emission);
        SetColor(material, "_EmissiveColorLDR", emission);
        SetFloat(material, "_EmissiveIntensity", 1f);
        SetTexture(material, "_EmissiveColorMap", emissionTexture, baseScale, baseOffset);
        if (normalTexture != null) material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
        if (emissionTexture != null) material.EnableKeyword("_EMISSIVE_COLOR_MAP");
    }

    // glTFast's built-in shaders do not have HDRP rendering passes. Convert
    // authored solids/cutouts while retaining texture, PBR and two-sided state.
    public static bool PrepareAuthoredMaterial(Material material)
    {
        if (material.shader != null && material.shader.name == "HDRP/Unlit") return false;
        var imported = material.shader != null && material.shader.name.StartsWith("glTF/", StringComparison.Ordinal);
        var mode = imported && material.HasProperty("_Mode") ? material.GetFloat("_Mode") :
            material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > .5f ? 2f :
            material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > .5f ? 1f : 0f;
        var cutoff = imported && material.HasProperty("alphaCutoff") ? material.GetFloat("alphaCutoff") :
            material.HasProperty("_AlphaCutoff") ? material.GetFloat("_AlphaCutoff") : .5f;
        var twoSided = imported ? material.GetFloat("_CullMode") == 0f : material.HasProperty("_DoubleSidedEnable") && material.GetFloat("_DoubleSidedEnable") > .5f;
        // BadgeA is an alpha atlas for lettering on a small carrier plane.
        // Use cutout depth/pass state explicitly: transparent Lit sorting and
        // inherited opaque blend factors can expose the atlas's white padding.
        if (material.name.IndexOf("BadgeA", StringComparison.Ordinal) >= 0)
        {
            mode = 1f;
            cutoff = .15f;
        }
        RebindToHdrpLit(material);
        if (material.shader == null || material.shader.name != "HDRP/Lit")
            throw new InvalidOperationException("McLaren HDRP/Lit shader is unavailable for '" + material.name + "'.");
        SetFloat(material, "_DoubleSidedEnable", twoSided ? 1f : 0f);
        SetFloat(material, "_CullMode", (float)(twoSided ? CullMode.Off : CullMode.Back));
        SetFloat(material, "_CullModeForward", (float)(twoSided ? CullMode.Off : CullMode.Back));
        if (twoSided) material.EnableKeyword("_DOUBLESIDED_ON");
        else material.DisableKeyword("_DOUBLESIDED_ON");
        if (mode >= 2f)
        {
            SetFloat(material, "_SurfaceType", 1f);
            SetFloat(material, "_BlendMode", 0f);
            SetFloat(material, "_TransparentZWrite", 0f);
            SetFloat(material, "_ZWrite", 0f);
            SetFloat(material, "_AlphaCutoffEnable", 0f);
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            return TryValidateHdrpMaterial(material);
        }
        var validated = FixSolidHdrpMaterial(material);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (mode == 1f)
        {
            SetFloat(material, "_AlphaCutoffEnable", 1f);
            SetFloat(material, "_AlphaCutoff", cutoff);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.SetOverrideTag("RenderType", "TransparentCutout");
            validated |= TryValidateHdrpMaterial(material);
        }
        material.SetShaderPassEnabled("Forward", true);
        material.SetShaderPassEnabled("GBuffer", true);
        material.SetShaderPassEnabled("DepthOnly", true);
        material.SetShaderPassEnabled("ShadowCaster", true);
        return validated;
    }

    public static bool PrepareGameMaterial(Renderer renderer, Material material)
    {
        var role = GetTransparentRole(renderer, material);
        if (role == McLarenSennaTransparentRole.Authored) return PrepareAuthoredMaterial(material);
        PrepareTransparentMaterial(material, role);
        return TryValidateHdrpMaterial(material);
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
        McLarenSennaTransparentRole role = McLarenSennaTransparentRole.OtherClear)
    {
        var cabinGlass = role == McLarenSennaTransparentRole.CabinGlass;
        // Imported glTF transparency is not reliable in the game's HDRP build.
        // Exterior clear-surface variants use deterministic per-instance Lit
        // states. Authored gauges, symbols, and screens never enter this path.
        RebindToHdrpLit(material);
        var tint = cabinGlass
            ? new Color(0.09f, 0.12f, 0.15f, 0.14f)
            : role == McLarenSennaTransparentRole.HeadlampLens
                ? new Color(0.78f, 0.84f, 0.90f, 0.035f)
                : new Color(0.82f, 0.86f, 0.90f, 0.05f);
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
        PrepareTransparentMaterial(material, McLarenSennaTransparentRole.CabinGlass);
    }

    internal static McLarenSennaTransparentRole GetTransparentRole(
        Renderer renderer,
        Material material)
    {
        var rendererName = renderer.name;
        var materialName = material.name;
        if (rendererName.StartsWith("McLarenLampCovers", StringComparison.Ordinal))
            return McLarenSennaTransparentRole.HeadlampLens;
        if (materialName.IndexOf("red_glass", StringComparison.OrdinalIgnoreCase) >= 0)
            return McLarenSennaTransparentRole.Authored;
        if ((HasAncestor(renderer.transform, "Kit2_Window_Geo") ||
            HasAncestor(renderer.transform, "Kit2_WindowInside_Geo") ||
            materialName.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0) ||
            rendererName.IndexOf("windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("doorglass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("quarterglass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("backlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("blackGlass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return McLarenSennaTransparentRole.CabinGlass;
        }

        if (!IsTransparentMaterial(material))
            return McLarenSennaTransparentRole.Authored;
        if (rendererName.IndexOf("headlightglass", StringComparison.OrdinalIgnoreCase) >= 0)
            return McLarenSennaTransparentRole.HeadlampLens;

        return materialName.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
            ? McLarenSennaTransparentRole.OtherClear
            : McLarenSennaTransparentRole.Authored;
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
public sealed class McLarenSennaMaterialController : MonoBehaviour
{
    private const string RimMaterialMarker = "Wheel1A";
    private readonly List<Material> ownedMaterials = new List<Material>();
    private McLarenSennaMaterialFixResult result;
    private bool initialized;

    internal McLarenSennaMaterialFixResult Initialize(ModContext? context)
    {
        if (initialized)
            return result;

        initialized = true;
        var clones = new Dictionary<Material, Dictionary<McLarenSennaTransparentRole, Material>>();
        var rendererCount = 0;
        var transparentMaterials = 0;
        var authoredTransparentMaterials = 0;
        var opaqueMaterials = 0;
        var validatedMaterials = 0;
        var rimSlots = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!McLarenSennaMaterials.IsMcLarenRenderer(renderer.transform))
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

                var role = McLarenSennaMaterials.GetTransparentRole(renderer, source);
                if (!clones.TryGetValue(source, out var roleVariants))
                {
                    roleVariants = new Dictionary<McLarenSennaTransparentRole, Material>();
                    clones.Add(source, roleVariants);
                }
                if (!roleVariants.TryGetValue(role, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_McLarenInstance_" + role;
                    roleVariants.Add(role, runtimeMaterial);
                    ownedMaterials.Add(runtimeMaterial);

                    if (role != McLarenSennaTransparentRole.Authored)
                    {
                        McLarenSennaMaterials.PrepareTransparentMaterial(runtimeMaterial, role);
                        transparentMaterials++;
                    }
                    else
                    {
                        if (McLarenSennaMaterials.PrepareAuthoredMaterial(runtimeMaterial)) validatedMaterials++;
                        if (McLarenSennaMaterials.IsTransparentMaterial(runtimeMaterial)) authoredTransparentMaterials++;
                        else opaqueMaterials++;
                    }
                    if (runtimeMaterial.name.IndexOf(
                            RimMaterialMarker,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetRimFinish(runtimeMaterial);
                    }
                }

                if (role == McLarenSennaTransparentRole.CabinGlass)
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

        result = new McLarenSennaMaterialFixResult(
            rendererCount,
            0,
            opaqueMaterials,
            transparentMaterials,
            validatedMaterials,
            rimSlots,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
        McLarenSennaDiagnostics.Info(
            context,
            $"McLarenSenna materials vehicle={GetInstanceID()}: cloned " +
            $"{ownedMaterials.Count} materials for {rendererCount} renderers, " +
            $"clearSurfaceVariants={transparentMaterials}, " +
            $"authoredTransparentRetained={authoredTransparentMaterials}, " +
            $"cabinGlass={cabinGlassRenderers}, " +
            $"rimSlots={rimSlots}, opaqueHdrp={opaqueMaterials}, validated={validatedMaterials}.");
        return result;
    }

    private static void SetRimFinish(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", McLarenSennaMaterials.RimBaseColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", McLarenSennaMaterials.RimBaseColor);
        if (material.HasProperty("baseColorFactor"))
            material.SetColor("baseColorFactor", McLarenSennaMaterials.RimBaseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", McLarenSennaMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", McLarenSennaMaterials.RimSmoothness);
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
public sealed class McLarenSennaPaintController : MonoBehaviour
{
    private const string BodyMaterialMarker = "Paint";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly List<Material> ownedPanelMaterials = new List<Material>();
    private MaterialPropertyBlock? properties;
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
                $"McLarenSenna paint vehicle={vehicle?.GetInstanceID()}: no vehicle color " +
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
        properties ??= new MaterialPropertyBlock();
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
        McLarenSennaDiagnostics.PaintInfo(
            context,
            $"McLarenSenna paint vehicle={vehicle?.GetInstanceID()}: applied " +
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
            if (!McLarenSennaMaterials.IsMcLarenRenderer(renderer.transform))
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

        McLarenSennaDiagnostics.PaintInfo(
            context,
            $"McLarenSenna paint vehicle={vehicle?.GetInstanceID()}: mapped " +
            $"bodySlots={bodySlots}, interiorAccentSlots={interiorAccentSlots}; " +
            "glass, lamps, unmapped carbon, rims, brakes, and black trim excluded.");
        if (bodySlots == 0)
            context?.Logger.Warn(
                "McLarenSenna paint mapping incomplete: the supplied GLB has no mapped exterior paint slots.");
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
        var savedName = vehicle?.vehicleInstance?.vehicleColorName;
        // Awake can retain a preview/default CarFeatures color before the
        // native spawn assigns the purchased instance. Its saved choice wins.
        if (!string.IsNullOrEmpty(savedName))
        {
            if (live != null && string.Equals(live.name, savedName, StringComparison.Ordinal))
                return live;
            if (VehicleHelper.TryGetVehicleColor(savedName, out var ordered))
                return ordered;
        }
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


