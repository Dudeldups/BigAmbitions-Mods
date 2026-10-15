#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct AudiR8MaterialFixResult
{
    public AudiR8MaterialFixResult(
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

internal enum AudiR8TransparentRole
{
    Authored,
    CabinGlass,
    HeadlampLens,
    RearLampLens,
    OtherClear,
}

public static class AudiR8Materials
{
    public const float RimMetallic = 0.65f;
    public const float RimSmoothness = 0.45f;
    public static readonly Color RimBaseColor = new Color(0.03f, 0.03f, 0.03f, 1f);

    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string RimMaterialMarker = "wpolySurface442SG1";
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static AudiR8MaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var controller = vehicle.GetComponent<AudiR8MaterialController>();
        if (controller == null)
            controller = vehicle.AddComponent<AudiR8MaterialController>();
        return controller.Initialize(null);
    }

    private static Material? FindCanonicalRimMaterial(GameObject vehicle)
    {
        Material? fallback = null;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsAudiR8Renderer(renderer.transform))
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
        if (name.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f;
    }

    public static bool PrepareImportedMaterial(Material material)
    {
        if (material == null)
            return false;

        // The supplied GLB has no MASK materials. GLTFast's Shader Graph
        // nevertheless imports its OPAQUE materials with _ALPHATEST_ON and an
        // AlphaTest queue, so those values cannot identify authored cutouts.
        var transparent = IsTransparentMaterial(material);
        var doubleSided = IsDoubleSidedMaterial(material);

        // GLTFast's Shader Graph is not guaranteed to exist in the game's
        // player build. Convert the persistent source asset before bundling.
        RebindToHdrpLit(material);
        var hdrpLit = Shader.Find("HDRP/Lit") ??
                      Shader.Find("High Definition Render Pipeline/Lit");
        if (hdrpLit == null || material.shader != hdrpLit)
            return false;

        TryValidateHdrpMaterial(material);
        if (transparent)
            ConfigureTransparentSurface(material, doubleSided);
        else
            FixSolidHdrpMaterial(material);
        return true;
    }
    public static bool IsAudiR8Renderer(Transform transform)
    {
        if (transform.name.StartsWith("AudiR8DamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("AudiR8Wheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("AudiR8FixedCaliper", StringComparison.Ordinal))
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("AudiR8DamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "AudiR8Visual", StringComparison.Ordinal) ||
                current.name.StartsWith("AudiR8Wheel", StringComparison.Ordinal) ||
                current.name.StartsWith("AudiR8FixedCaliper", StringComparison.Ordinal))
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
        SetFloat(material, "_AlphaCutoff", 0f);
        SetFloat(material, "_BlendMode", 0f);
        SetFloat(material, "_SupportDecals", 0f);
        SetFloat(material, "_ReceivesSSR", 0f);
        SetFloat(material, "_ReceivesSSRTransparent", 0f);
        SetFloat(material, "_RefractionModel", 0f);
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        SetFloat(material, "_AlphaSrcBlend", (float)BlendMode.One);
        SetFloat(material, "_AlphaDstBlend", (float)BlendMode.Zero);

        var validated = TryValidateHdrpMaterial(material);
        SetOpaqueColor(material, "_BaseColor");
        SetOpaqueColor(material, "baseColorFactor");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        return validated;
    }

    internal static bool PrepareOpaqueRuntimeMaterial(Material material)
    {
        if (material == null)
            return false;

        RebindToHdrpLit(material);
        var hdrpLit = Shader.Find("HDRP/Lit") ??
                      Shader.Find("High Definition Render Pipeline/Lit");
        if (hdrpLit == null || material.shader != hdrpLit)
            return false;

        FixSolidHdrpMaterial(material);
        return true;
    }

    internal static bool PrepareAuthoredTransparentRuntimeMaterial(Material material)
    {
        if (material == null)
            return false;

        var doubleSided = IsDoubleSidedMaterial(material);
        RebindToHdrpLit(material);
        var hdrpLit = Shader.Find("HDRP/Lit") ??
                      Shader.Find("High Definition Render Pipeline/Lit");
        if (hdrpLit == null || material.shader != hdrpLit)
            return false;

        TryValidateHdrpMaterial(material);
        ConfigureTransparentSurface(material, doubleSided);
        return true;
    }

    private static bool IsDoubleSidedMaterial(Material material) =>
        material.IsKeywordEnabled("_DOUBLESIDED_ON") ||
        GetFloat(material, "_DoubleSidedEnable", null, null, 0f) > 0.5f;

    private static void ConfigureTransparentSurface(Material material, bool doubleSided)
    {
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
        SetFloat(material, "_TransparentDepthPrepassEnable", 0f);
        SetFloat(material, "_TransparentDepthPostpassEnable", 0f);
        SetFloat(material, "_TransparentBackfaceEnable", 0f);
        var cullMode = doubleSided ? CullMode.Off : CullMode.Back;
        SetFloat(material, "_Cull", (float)cullMode);
        SetFloat(material, "_CullMode", (float)cullMode);
        SetFloat(material, "_CullModeForward", (float)cullMode);
        SetFloat(material, "_TransparentCullMode", (float)cullMode);
        SetFloat(material, "_DoubleSidedEnable", doubleSided ? 1f : 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (doubleSided)
            material.EnableKeyword("_DOUBLESIDED_ON");
        else
            material.DisableKeyword("_DOUBLESIDED_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("TransparentDepthPrepass", false);
        material.SetShaderPassEnabled("TransparentDepthPostpass", false);
        material.SetShaderPassEnabled("TransparentBackface", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("ShadowCaster", false);
    }

    internal static bool HasOpaqueAlphaClipState(Material material) =>
        material.IsKeywordEnabled("_ALPHATEST_ON") ||
        GetFloat(material, "_AlphaCutoffEnable", null, null, 0f) > 0.5f ||
        string.Equals(
            material.GetTag("RenderType", false, string.Empty),
            "TransparentCutout",
            StringComparison.Ordinal) ||
        (material.renderQueue >= (int)RenderQueue.AlphaTest &&
         material.renderQueue < (int)RenderQueue.Transparent);

    internal static string DescribeSurfaceState(Material material)
    {
        var color = GetColor(material, "_BaseColor", "_Color", Color.white);
        var surface = GetFloat(material, "_SurfaceType", null, null, 0f);
        var alphaClip = GetFloat(material, "_AlphaCutoffEnable", null, null, 0f);
        var doubleSided = IsDoubleSidedMaterial(material);
        var renderType = material.GetTag("RenderType", false, string.Empty);
        return $"shader={material.shader?.name ?? "missing"}, surface={surface:0}, " +
               $"alpha={color.a:0.00}, alphaClip={alphaClip:0}, " +
               $"alphaTest={material.IsKeywordEnabled("_ALPHATEST_ON")}, " +
               $"doubleSided={doubleSided}, queue={material.renderQueue}, " +
               $"renderType={renderType}.";
    }

    internal static void PrepareTransparentMaterial(
        Material material,
        AudiR8TransparentRole role = AudiR8TransparentRole.OtherClear)
    {
        var cabinGlass = role == AudiR8TransparentRole.CabinGlass;
        var doubleSided = cabinGlass || IsDoubleSidedMaterial(material);
        // Imported glTF transparency is not reliable in the game's HDRP build.
        // Exterior clear-surface variants use deterministic per-instance Lit
        // states. Cabin glass keeps the source GLB's double-sided setting.
        RebindToHdrpLit(material);
        var tint = cabinGlass
            ? new Color(0.055f, 0.075f, 0.09f, 0.055f)
            : role == AudiR8TransparentRole.HeadlampLens
                ? new Color(0.78f, 0.84f, 0.90f, 0.035f)
                : role == AudiR8TransparentRole.RearLampLens
                    ? GetRearLampLensTint(material)
                : new Color(0.82f, 0.86f, 0.90f, 0.05f);
        SetColor(material, "_BaseColor", tint);
        SetColor(material, "_Color", tint);
        SetColor(material, "baseColorFactor", tint);
        SetFloat(material, "transmissionFactor", 0f);
        if (cabinGlass)
        {
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "metallicFactor", 0f);
            SetFloat(material, "_Smoothness", 0.85f);
            SetFloat(material, "roughnessFactor", 0f);
        }
        TryValidateHdrpMaterial(material);
        ConfigureTransparentSurface(material, doubleSided);
    }

    private static Color GetRearLampLensTint(Material material)
    {
        // Keep the authored red lens color, but the imported GLB alpha is too
        // low to read as a lens in game. Tail/brake illumination is provided by
        // separate overlays, so this opacity only restores the visible cover.
        var tint = GetColor(material, "_BaseColor", "_Color", new Color(0.735f, 0f, 0f, 1f));
        tint.a = 0.58f;
        return tint;
    }

    internal static void RestoreCabinGlassMaterial(Material material)
    {
        PrepareTransparentMaterial(material, AudiR8TransparentRole.CabinGlass);
    }

    internal static AudiR8TransparentRole GetTransparentRole(
        Renderer renderer,
        Material material)
    {
        if (!IsTransparentMaterial(material))
            return AudiR8TransparentRole.Authored;

        var rendererName = renderer.name;
        if (material.name.IndexOf("red_glass", StringComparison.OrdinalIgnoreCase) >= 0)
            return AudiR8TransparentRole.RearLampLens;
        if (rendererName.IndexOf("headlightglass", StringComparison.OrdinalIgnoreCase) >= 0)
            return AudiR8TransparentRole.HeadlampLens;
        if (rendererName.IndexOf("windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("doorglass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("quarterglass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("backlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("blackGlass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return AudiR8TransparentRole.CabinGlass;
        }

        var name = material.name;
        return name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
            ? AudiR8TransparentRole.OtherClear
            : AudiR8TransparentRole.Authored;
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
public sealed class AudiR8MaterialController : MonoBehaviour
{
    private const string RimMaterialMarker = "wpolySurface442SG1";
    private readonly List<Material> ownedMaterials = new List<Material>();
    private AudiR8MaterialFixResult result;
    private bool initialized;

    internal AudiR8MaterialFixResult Initialize(ModContext? context)
    {
        if (initialized)
            return result;

        initialized = true;
        var clones = new Dictionary<Material, Dictionary<AudiR8TransparentRole, Material>>();
        var rendererCount = 0;
        var opaqueMaterialsFixed = 0;
        var opaqueAlphaClipRemaining = 0;
        var transparentMaterials = 0;
        var authoredTransparentMaterials = 0;
        var rimSlots = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!AudiR8Materials.IsAudiR8Renderer(renderer.transform))
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

                var role = AudiR8Materials.GetTransparentRole(renderer, source);
                if (!clones.TryGetValue(source, out var roleVariants))
                {
                    roleVariants = new Dictionary<AudiR8TransparentRole, Material>();
                    clones.Add(source, roleVariants);
                }
                if (!roleVariants.TryGetValue(role, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_AudiR8Instance_" + role;
                    roleVariants.Add(role, runtimeMaterial);
                    ownedMaterials.Add(runtimeMaterial);

                    if (role != AudiR8TransparentRole.Authored)
                    {
                        AudiR8Materials.PrepareTransparentMaterial(runtimeMaterial, role);
                        transparentMaterials++;
                    }
                    else if (AudiR8Materials.IsTransparentMaterial(runtimeMaterial))
                    {
                        if (AudiR8Materials.PrepareAuthoredTransparentRuntimeMaterial(runtimeMaterial))
                            authoredTransparentMaterials++;
                    }
                    else if (AudiR8Materials.PrepareOpaqueRuntimeMaterial(runtimeMaterial))
                    {
                        opaqueMaterialsFixed++;
                        if (AudiR8Materials.HasOpaqueAlphaClipState(runtimeMaterial))
                            opaqueAlphaClipRemaining++;
                    }
                    if (runtimeMaterial.name.IndexOf(
                            RimMaterialMarker,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetRimFinish(runtimeMaterial);
                    }
                }

                if (role == AudiR8TransparentRole.CabinGlass)
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

        result = new AudiR8MaterialFixResult(
            rendererCount,
            0,
            opaqueMaterialsFixed,
            transparentMaterials,
            0,
            rimSlots,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
        AudiR8Diagnostics.Info(
            context,
            $"AudiR8 materials vehicle={GetInstanceID()}: cloned " +
            $"{ownedMaterials.Count} materials for {rendererCount} renderers, " +
            $"clearSurfaceVariants={transparentMaterials}, " +
            $"authoredTransparentNormalized={authoredTransparentMaterials}, " +
            $"opaqueNormalized={opaqueMaterialsFixed}, " +
            $"cabinGlass={cabinGlassRenderers}, " +
            $"rimSlots={rimSlots}; material state is instance-owned.");
        AudiR8Diagnostics.AppearanceInfo(
            context,
            $"AudiR8 appearance vehicle={GetInstanceID()}: " +
            $"opaqueMaterials={opaqueMaterialsFixed}, " +
            $"opaqueAlphaClipRemaining={opaqueAlphaClipRemaining}, " +
            $"transparentVariants={transparentMaterials}, " +
            $"authoredTransparent={authoredTransparentMaterials}.");
        return result;
    }

    private static void SetRimFinish(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", AudiR8Materials.RimBaseColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", AudiR8Materials.RimBaseColor);
        if (material.HasProperty("baseColorFactor"))
            material.SetColor("baseColorFactor", AudiR8Materials.RimBaseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", AudiR8Materials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", AudiR8Materials.RimSmoothness);
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
public sealed class AudiR8PaintController : MonoBehaviour
{
    // Optional notification through the game's event bus. No mod assembly or
    // palette implementation is referenced; vanilla colors work independently.
    internal const string SupplementalColorRestoredEvent = "vehicle-repainter:color-restored";
    private const string BodyMaterialMarker = "2020Paint_Material1";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private MaterialPropertyBlock properties = null!;
    private VehicleController? vehicle;
    private ModContext? context;
    private string appliedColorName = string.Empty;
    private Color32 appliedTint;
    private bool hasAppliedTint;
    private bool appliedStripesEnabled;
    private bool initialized;
    private bool awaitingSavedColor;
    private Material? bodyPaintMaterial;

    internal static Color RacingStripeColor(Color paint, bool enabled)
    {
        paint.a = 1f;
        if (!enabled) return paint;
        // Same paint luminance/threshold as the Camaro contrast stripe.
        return paint.r * .2126f + paint.g * .7152f + paint.b * .0722f >= .46f
            ? Color.black : Color.white;
    }

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        properties ??= new MaterialPropertyBlock();
        if (!initialized)
        {
            initialized = true;
            awaitingSavedColor = true;
            FindPaintSlots();
        }
        SubscribeAppearanceEvents();
        ApplyCurrentColor("initialize", preferSavedColor: true);
    }

    internal bool HasAppliedColor => hasAppliedTint;

    internal void ApplyCurrentColor(string source, bool force = false, bool preferSavedColor = false)
    {
        var selected = ResolveVehicleColor(preferSavedColor);
        if (selected == null)
        {
            context?.Logger.Warn(
                $"AudiR8 paint vehicle={vehicle?.GetInstanceID()}: no vehicle color " +
                $"was available during '{source}'.");
            return;
        }

        var colorName = ((UnityEngine.Object)selected).name;
        var tint = selected.tint;
        var stripesEnabled = AudiR8RacingStripes.GetRacingStripes(SaveGameManager.Current, vehicle?.vehicleInstance?.id);
        if (!force && hasAppliedTint &&
            string.Equals(colorName, appliedColorName, StringComparison.Ordinal) &&
            tint.Equals(appliedTint) && stripesEnabled == appliedStripesEnabled)
        {
            return;
        }

        var color = (Color)tint;
        color.a = 1f;
        foreach (var slot in slots)
        {
            if (slot.Renderer == null) continue;
            var stripe = slot.Category == PaintCategory.RacingStripe;
            var material = slot.Material;
            if (stripe)
            {
                // Keep this submesh as part of the deformable shell. Hiding the
                // renderer would hide the whole body; hiding faces could leave
                // holes because the authored paint/stripe regions share a shell.
                material = stripesEnabled ? slot.Material : bodyPaintMaterial ?? slot.Material;
                var currentMaterials = slot.Renderer.sharedMaterials;
                if (currentMaterials[slot.MaterialIndex] != material)
                {
                    currentMaterials[slot.MaterialIndex] = material;
                    slot.Renderer.sharedMaterials = currentMaterials;
                }
            }
            var slotColor = slot.Category == PaintCategory.InteriorAccent
                ? Color.Lerp(color, Color.white, 0.12f)
                : stripe ? RacingStripeColor(color, stripesEnabled) : color;
            slotColor.a = 1f;
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (material.HasProperty(BaseColor))
                properties.SetColor(BaseColor, slotColor);
            if (material.HasProperty(ColorProperty))
                properties.SetColor(ColorProperty, slotColor);
            if (material.HasProperty(BaseColorFactor))
                properties.SetColor(BaseColorFactor, slotColor);
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        appliedColorName = colorName;
        appliedTint = tint;
        hasAppliedTint = true;
        appliedStripesEnabled = stripesEnabled;
        AudiR8Diagnostics.StripesInfo(context,
            $"AudiR8 stripes vehicle='{vehicle?.vehicleInstance?.id}': enabled={stripesEnabled}, " +
            $"paint='{colorName}', stripeColor={RacingStripeColor(color, stripesEnabled)}, source='{source}'.");
        AudiR8Diagnostics.PaintInfo(
            context,
            $"AudiR8 paint vehicle={vehicle?.GetInstanceID()}: applied " +
            $"color='{colorName}' rgba={tint} to {slots.Count} body/interior slots " +
            $"source='{source}', saved='{vehicle?.vehicleInstance?.vehicleColorName}', " +
            $"live='{vehicle?.CarFeatures?.VehicleColor?.name}', preferSaved={preferSavedColor}, " +
            $"awaitingSaved={awaitingSavedColor}, force={force}.");
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        bodyPaintMaterial = null;
        var bindings = GetComponent<AudiR8AppearanceBindings>();
        var bodySlots = 0;
        var interiorAccentSlots = 0;
        var stripeSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer != bindings?.BodyRenderer && !AudiR8Materials.IsAudiR8Renderer(renderer.transform))
                continue;
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null && bindings != null && renderer == bindings.BodyRenderer &&
                    index == bindings.StripeMaterialIndex)
                {
                    slots.Add(new PaintSlot(renderer, material, index, PaintCategory.RacingStripe));
                    stripeSlots++;
                }
                else if (material != null && ((bindings != null && renderer == bindings.BodyRenderer &&
                    index == bindings.PaintMaterialIndex) || material.name.IndexOf(
                        BodyMaterialMarker,
                        StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    slots.Add(new PaintSlot(renderer, material, index, PaintCategory.Body));
                    bodyPaintMaterial ??= material;
                    bodySlots++;
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
        }

        AudiR8Diagnostics.PaintInfo(
            context,
            $"AudiR8 paint vehicle={vehicle?.GetInstanceID()}: mapped " +
            $"bodySlots={bodySlots}, interiorAccentSlots={interiorAccentSlots}; " +
            "glass, lamps, unmapped carbon, rims, brakes, and black trim excluded.");
        if (bodySlots == 0)
            context?.Logger.Warn(
                $"AudiR8 paint mapping incomplete bodySlots={bodySlots}, " +
                $"interiorAccentSlots={interiorAccentSlots}.");
        if (stripeSlots != 1)
            context?.Logger.Warn($"AudiR8 racing stripe mapping expected one body slot, found {stripeSlots}; " +
                $"binding={bindings != null}, renderer='{bindings?.BodyRenderer?.name}', slot={bindings?.StripeMaterialIndex}.");
        AudiR8Diagnostics.StripesInfo(context, $"AudiR8 stripe mapping vehicle='{vehicle?.vehicleInstance?.id}': " +
            $"renderer='{bindings?.BodyRenderer?.name}', slot={bindings?.StripeMaterialIndex}, mapped={stripeSlots}.");
    }

    private void SubscribeAppearanceEvents()
    {
        AudiR8RacingStripes.Changed -= OnStripesChanged;
        AudiR8RacingStripes.Changed += OnStripesChanged;
        GameEvent.onGameEventTriggered -= OnGameEvent;
        GameEvent.onGameEventTriggered += OnGameEvent;
    }
    private void OnGameEvent(string eventName)
    {
        if (this == null || vehicle == null) { UnsubscribeAppearanceEvents(); return; }
        if (eventName == SupplementalColorRestoredEvent)
            ApplyCurrentColor("supplemental-color-restored", force: true, preferSavedColor: true);
    }
    private void OnStripesChanged(string vehicleId)
    {
        if (vehicleId == vehicle?.vehicleInstance?.id)
            ApplyCurrentColor("stripe-setting-changed", force: true);
    }
    private void OnEnable()
    {
        if (!initialized) return;
        awaitingSavedColor = true;
        SubscribeAppearanceEvents();
        ApplyCurrentColor("paint-controller-enabled", force: true, preferSavedColor: true);
    }
    private void UnsubscribeAppearanceEvents()
    {
        AudiR8RacingStripes.Changed -= OnStripesChanged;
        GameEvent.onGameEventTriggered -= OnGameEvent;
    }
    private void OnDisable() => UnsubscribeAppearanceEvents();
    private void OnDestroy() => UnsubscribeAppearanceEvents();

    private static bool IsInteriorAccent(Renderer renderer, Material material)
    {
        var materialName = material.name;
        if (!HasAncestor(renderer.transform, "Interior"))
            return false;
        return materialName.IndexOf("stitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
               materialName.IndexOf("Coloured", StringComparison.OrdinalIgnoreCase) >= 0 ||
               materialName.IndexOf("Textured", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private VehicleColor? ResolveVehicleColor(bool preferSavedColor)
    {
        preferSavedColor |= awaitingSavedColor;
        var live = vehicle?.CarFeatures?.VehicleColor;
        // Normal repaint previews use the live color. Cold load/re-enable must
        // prefer the saved choice over a provisional native Black default.
        if (!preferSavedColor && live != null)
            return live;
        var colorName = vehicle?.vehicleInstance?.vehicleColorName;
        if (!string.IsNullOrEmpty(colorName))
        {
            if (live != null && string.Equals(live.name, colorName, StringComparison.Ordinal))
            {
                awaitingSavedColor = false;
                return live; // Also covers optional palettes kept outside the dealer registry.
            }
            if (InstanceBehavior<GlobalReferences>.Instance?.vehicleColors != null &&
                VehicleHelper.TryGetVehicleColor(colorName, out var saved))
                return saved;
        }
        return live;
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
        RacingStripe,
    }
}


