#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct DodgeRam1500TRXMaterialFixResult
{
    public DodgeRam1500TRXMaterialFixResult(
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

internal enum DodgeRam1500TRXTransparentRole
{
    Authored,
    CabinGlass,
    HeadlampLens,
    OtherClear,
}

public static class DodgeRam1500TRXMaterials
{
    public const string RearCabinGlassMarker = "DodgeRam1500TRXRearCabinGlass";
    public const float RimMetallic = 0.65f;
    public const float RimSmoothness = 0.45f;
    public static readonly Color RimBaseColor = new Color(0.03f, 0.03f, 0.03f, 1f);

    internal const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string RimMaterialMarker = "DodgeRam1500TRXRim";
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static DodgeRam1500TRXMaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var controller = vehicle.GetComponent<DodgeRam1500TRXMaterialController>();
        if (controller == null)
            controller = vehicle.AddComponent<DodgeRam1500TRXMaterialController>();
        return controller.Initialize(null);
    }

    private static Material? FindCanonicalRimMaterial(GameObject vehicle)
    {
        Material? fallback = null;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsDodgeRam1500TRXRenderer(renderer.transform))
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
            name.IndexOf("RAM_1500TRX_2021Window_Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f;
    }

    public static bool IsDodgeRam1500TRXRenderer(Transform transform)
    {
        if (transform.name.StartsWith("DodgeRam1500TRXDamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("DodgeRam1500TRXWheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("DodgeRam1500TRXFixedCaliper", StringComparison.Ordinal))
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("DodgeRam1500TRXDamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "DodgeRam1500TRXVisual", StringComparison.Ordinal) ||
                current.name.StartsWith("DodgeRam1500TRXWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("DodgeRam1500TRXFixedCaliper", StringComparison.Ordinal))
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

    internal static void RebindToHdrpLit(Material material)
    {
        var hdrpLit = Shader.Find("HDRP/Lit") ??
                      Shader.Find("High Definition Render Pipeline/Lit");
        if (hdrpLit == null) throw new InvalidOperationException("DodgeRam1500TRX: runtime HDRP/Lit shader unavailable.");
        if (material.shader == hdrpLit) return;
        // Authored HDRP values are already normalized; rebinding the game shader
        // must retain maps, cutout/normal state, smoothness and fixed trim colors.
        if (material.shader != null && material.shader.name == "HDRP/Lit")
        {
            material.shader = hdrpLit;
            return;
        }

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

    public static bool PrepareOpaqueMaterial(Material material)
    {
        if (material.name.StartsWith("DodgeRam1500TRXLamp", StringComparison.Ordinal))
        {
            // Keep radiance in the visible unlit channel across the game's shader rebind.
            var radiance = material.GetColor("_UnlitColor");
            if (radiance.maxColorComponent <= 0f) radiance = material.GetColor("_EmissiveColor");
            material.shader = Shader.Find("HDRP/Unlit") ?? throw new InvalidOperationException("DodgeRam1500TRX: runtime HDRP/Unlit unavailable.");
            if (radiance.maxColorComponent <= 0f) throw new InvalidOperationException("DodgeRam1500TRX: lamp has no visible radiance: " + material.name);
            material.SetColor("_UnlitColor", radiance);
            material.SetColor("_EmissiveColor", Color.black);
            material.SetFloat("_SurfaceType", 0f);
            material.SetFloat("_CullMode", 0f);
            material.SetShaderPassEnabled("ForwardOnly", true);
            material.renderQueue = (int)RenderQueue.Geometry + 450;
            return true;
        }
        var cutout = material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > .5f;
        RebindToHdrpLit(material);
        var validated = FixSolidHdrpMaterial(material);
        if (cutout)
        {
            material.SetFloat("_AlphaCutoffEnable", 1f);
            material.EnableKeyword("_ALPHATEST_ON");
            validated = TryValidateHdrpMaterial(material) || validated;
        }
        return validated;
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
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        if (material.HasProperty("_NormalMap") && material.GetTexture("_NormalMap") != null) material.EnableKeyword("_NORMALMAP");
        else material.DisableKeyword("_NORMALMAP");
        material.EnableKeyword("_NORMALMAP_TANGENT_SPACE");
        if (material.HasProperty("_MaskMap") && material.GetTexture("_MaskMap") != null) material.EnableKeyword("_MASKMAP");
        else material.DisableKeyword("_MASKMAP");
        if (material.HasProperty("_DoubleSidedEnable") && material.GetFloat("_DoubleSidedEnable") > .5f) material.EnableKeyword("_DOUBLESIDED_ON");
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        return validated;
    }

    public static void PrepareClearMaterial(Material material, bool cabinGlass) =>
        PrepareTransparentMaterial(material, cabinGlass ? DodgeRam1500TRXTransparentRole.CabinGlass : DodgeRam1500TRXTransparentRole.OtherClear);

    internal static void PrepareTransparentMaterial(
        Material material,
        DodgeRam1500TRXTransparentRole role = DodgeRam1500TRXTransparentRole.OtherClear)
    {
        var cabinGlass = role == DodgeRam1500TRXTransparentRole.CabinGlass;
        // Imported glTF transparency is not reliable in the game's HDRP build.
        // Exterior clear-surface variants use deterministic per-instance Lit
        // states. Authored gauges, symbols, and screens never enter this path.
        RebindToHdrpLit(material);
        var tint = cabinGlass
            ? (material.name.StartsWith(RearCabinGlassMarker, StringComparison.Ordinal)
                ? new Color(0.035f, 0.045f, 0.055f, 0.30f)
                : new Color(0.09f, 0.12f, 0.15f, 0.14f))
            : role == DodgeRam1500TRXTransparentRole.HeadlampLens
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
        // These three clear materials have no maps in the source GLB.
        foreach (var property in new[] { "_BaseColorMap", "_NormalMap", "_MaskMap" })
            if (material.HasProperty(property)) material.SetTexture(property, null);
        material.DisableKeyword("_NORMALMAP"); material.DisableKeyword("_MASKMAP");
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
        TryValidateHdrpMaterial(material);
        material.SetShaderPassEnabled("TransparentDepthPrepass", false);
        material.SetShaderPassEnabled("TransparentDepthPostpass", false);
        material.SetShaderPassEnabled("TransparentBackface", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        material.SetShaderPassEnabled("ShadowCaster", false);
    }

    public static void PrepareRearCabinGlassMaterial(Material material)
    {
        if (!material.name.StartsWith(RearCabinGlassMarker, StringComparison.Ordinal))
            throw new ArgumentException("Rear cabin glass must have its exact owned marker.", nameof(material));
        PrepareTransparentMaterial(material, DodgeRam1500TRXTransparentRole.CabinGlass);
    }

    internal static void RestoreCabinGlassMaterial(Material material)
    {
        PrepareTransparentMaterial(material, DodgeRam1500TRXTransparentRole.CabinGlass);
    }

    internal static DodgeRam1500TRXTransparentRole GetTransparentRole(
        Renderer renderer,
        Material material)
    {
        if (!IsTransparentMaterial(material))
            return DodgeRam1500TRXTransparentRole.Authored;

        var rendererName = renderer.name;
        if (rendererName.IndexOf("ORANGE_GLASS", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("RED_GLASS", StringComparison.OrdinalIgnoreCase) >= 0)
            return DodgeRam1500TRXTransparentRole.OtherClear;

        var cabinGlass = rendererName.IndexOf("WindowInside_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         rendererName.IndexOf("Window_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         material.name.IndexOf("RAM_1500TRX_2021Window_Material", StringComparison.OrdinalIgnoreCase) >= 0;
        for (var current = renderer.transform.parent; current != null && !cabinGlass; current = current.parent)
        {
            cabinGlass = current.name.IndexOf("WindowInside_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         current.name.IndexOf("Window_Geo", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        if (cabinGlass)
            return DodgeRam1500TRXTransparentRole.CabinGlass;

        var name = material.name;
        return name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0
            ? DodgeRam1500TRXTransparentRole.OtherClear
            : DodgeRam1500TRXTransparentRole.Authored;
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
public sealed class DodgeRam1500TRXMaterialController : MonoBehaviour
{
    private const string RimMaterialMarker = "DodgeRam1500TRXRim";
    private readonly List<Material> ownedMaterials = new List<Material>();
    private DodgeRam1500TRXMaterialFixResult result;
    private bool initialized;

    private void Awake() => Initialize(null);

    internal DodgeRam1500TRXMaterialFixResult Initialize(ModContext? context)
    {
        if (initialized)
            return result;

        initialized = true;
        var clones = new Dictionary<Material, Dictionary<DodgeRam1500TRXTransparentRole, Material>>();
        var rendererCount = 0;
        var decalMasksCleared = 0;
        var transparentMaterials = 0;
        var opaqueMaterials = 0;
        var validatedMaterials = 0;
        var authoredTransparentMaterials = 0;
        var rimSlots = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!DodgeRam1500TRXMaterials.IsDodgeRam1500TRXRenderer(renderer.transform))
                continue;

            rendererCount++;
            var layers = renderer.renderingLayerMask & ~DodgeRam1500TRXMaterials.HdrpDecalLayerMask;
            if (layers != renderer.renderingLayerMask) { renderer.renderingLayerMask = layers; decalMasksCleared++; }
            var materials = renderer.sharedMaterials;
            var changed = false;
            var hasCabinGlass = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                    continue;

                var role = DodgeRam1500TRXMaterials.GetTransparentRole(renderer, source);
                if (!clones.TryGetValue(source, out var roleVariants))
                {
                    roleVariants = new Dictionary<DodgeRam1500TRXTransparentRole, Material>();
                    clones.Add(source, roleVariants);
                }
                if (!roleVariants.TryGetValue(role, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_DodgeRam1500TRXInstance_" + role;
                    runtimeMaterial.hideFlags = HideFlags.DontSave;
                    roleVariants.Add(role, runtimeMaterial);
                    ownedMaterials.Add(runtimeMaterial);

                    if (role != DodgeRam1500TRXTransparentRole.Authored)
                    {
                        DodgeRam1500TRXMaterials.PrepareTransparentMaterial(runtimeMaterial, role);
                        transparentMaterials++;
                    }
                    else if (DodgeRam1500TRXMaterials.IsTransparentMaterial(runtimeMaterial))
                    {
                        DodgeRam1500TRXMaterials.RebindToHdrpLit(runtimeMaterial);
                        authoredTransparentMaterials++;
                    }
                    else
                    {
                        if (DodgeRam1500TRXMaterials.PrepareOpaqueMaterial(runtimeMaterial)) validatedMaterials++;
                        opaqueMaterials++;
                    }
                    if (runtimeMaterial.name.IndexOf(
                            RimMaterialMarker,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetRimFinish(runtimeMaterial);
                    }
                }

                if (role == DodgeRam1500TRXTransparentRole.CabinGlass)
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

        result = new DodgeRam1500TRXMaterialFixResult(
            rendererCount,
            decalMasksCleared,
            opaqueMaterials,
            transparentMaterials,
            validatedMaterials,
            rimSlots,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
        DodgeRam1500TRXDiagnostics.Info(
            context,
            $"DodgeRam1500TRX materials vehicle={GetInstanceID()}: cloned " +
            $"{ownedMaterials.Count} materials for {rendererCount} renderers, " +
            $"clearSurfaceVariants={transparentMaterials}, " +
            $"authoredTransparentRetained={authoredTransparentMaterials}, " +
            $"cabinGlass={cabinGlassRenderers}, " +
            $"rimSlots={rimSlots}, opaqueRuntimeShaders={opaqueMaterials}, validated={validatedMaterials}.");
        return result;
    }

    private static void SetRimFinish(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", DodgeRam1500TRXMaterials.RimBaseColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", DodgeRam1500TRXMaterials.RimBaseColor);
        if (material.HasProperty("baseColorFactor"))
            material.SetColor("baseColorFactor", DodgeRam1500TRXMaterials.RimBaseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", DodgeRam1500TRXMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", DodgeRam1500TRXMaterials.RimSmoothness);
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
public sealed class DodgeRam1500TRXPaintController : MonoBehaviour
{
    // Optional native event only; no dependency on a palette/repainter mod.
    internal const string SupplementalColorRestoredEvent = "vehicle-repainter:color-restored";
    private const string BodyMaterialMarker = "RAM_1500TRX_2021Paint_Material";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private MaterialPropertyBlock? properties;
    private VehicleController? vehicle;
    private string? explicitVehicleColorName;
    private VehicleColor? explicitVehicleColor;
    private ModContext? context;
    private string appliedColorName = string.Empty;
    private Color32 appliedTint;
    private bool hasAppliedTint;
    private bool initialized;
    private bool awaitingSavedColor;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        if (!initialized)
        {
            initialized = true;
            awaitingSavedColor = true;
            FindPaintSlots();
        }
        SubscribeAppearanceEvents();
        ApplyCurrentColor("initialize", preferSavedColor: true);
    }

    internal void InitializeForPrivateDriver(
        string? vehicleColorName,
        VehicleColor? vehicleColor)
    {
        UnsubscribeAppearanceEvents();
        awaitingSavedColor = false;
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

    internal void ApplyCurrentColor(string source, bool force = false, bool preferSavedColor = false)
    {
        var selected = ResolveVehicleColor(preferSavedColor);
        if (selected == null)
        {
            context?.Logger.Warn(
                $"DodgeRam1500TRX paint vehicle={vehicle?.GetInstanceID()}: no vehicle color " +
                $"was available during '{source}'.");
            return;
        }

        var colorName = ((UnityEngine.Object)selected).name;
        var tint = selected.tint;
        if (!force && hasAppliedTint &&
            string.Equals(colorName, appliedColorName, StringComparison.Ordinal) &&
            tint.Equals(appliedTint))
        {
            return;
        }

        // Unity native objects must be created during the apply lifecycle,
        // not from MonoBehaviour field initialization during deserialization.
        var block = properties ??= new MaterialPropertyBlock();
        var color = (Color)tint;
        color.a = 1f;
        foreach (var slot in slots)
        {
            color.a = 1f;
            block.Clear();
            slot.Renderer.GetPropertyBlock(block, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor))
                block.SetColor(BaseColor, color);
            if (slot.Material.HasProperty(ColorProperty))
                block.SetColor(ColorProperty, color);
            if (slot.Material.HasProperty(BaseColorFactor))
                block.SetColor(BaseColorFactor, color);
            slot.Renderer.SetPropertyBlock(block, slot.MaterialIndex);
        }

        appliedColorName = colorName;
        appliedTint = tint;
        hasAppliedTint = true;
        DodgeRam1500TRXDiagnostics.PaintInfo(
            context,
            $"DodgeRam1500TRX paint vehicle={vehicle?.GetInstanceID()}: applied " +
            $"color='{colorName}' rgba={tint} to {slots.Count} body/interior slots " +
            $"source='{source}', saved='{vehicle?.vehicleInstance?.vehicleColorName}', " +
            $"live='{vehicle?.CarFeatures?.VehicleColor?.name}', preferSaved={preferSavedColor}, " +
            $"awaitingSaved={awaitingSavedColor}, force={force}.");
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        var bodySlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!DodgeRam1500TRXMaterials.IsDodgeRam1500TRXRenderer(renderer.transform))
                continue;
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null && material.name.IndexOf(
                        BodyMaterialMarker,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    slots.Add(new PaintSlot(renderer, material, index));
                    bodySlots++;
                }
            }
        }

        DodgeRam1500TRXDiagnostics.PaintInfo(
            context,
            $"DodgeRam1500TRX paint vehicle={vehicle?.GetInstanceID()}: mapped " +
            $"bodySlots={bodySlots}; glass, lamps, rims, brakes, and black trim excluded.");
        if (bodySlots == 0)
            context?.Logger.Warn(
                $"DodgeRam1500TRX paint mapping incomplete bodySlots={bodySlots}.");
    }

    private void SubscribeAppearanceEvents()
    {
        if (vehicle == null) return;
        GameEvent.onGameEventTriggered -= OnGameEvent;
        GameEvent.onGameEventTriggered += OnGameEvent;
    }

    private void UnsubscribeAppearanceEvents() => GameEvent.onGameEventTriggered -= OnGameEvent;

    private void OnGameEvent(string eventName)
    {
        if (this == null || vehicle == null) { UnsubscribeAppearanceEvents(); return; }
        if (eventName == SupplementalColorRestoredEvent)
            ApplyCurrentColor("supplemental-color-restored", force: true, preferSavedColor: true);
    }

    private void OnEnable()
    {
        if (!initialized) return;
        awaitingSavedColor = vehicle != null;
        SubscribeAppearanceEvents();
        ApplyCurrentColor("paint-controller-enabled", force: true, preferSavedColor: vehicle != null);
    }

    private void OnDisable() => UnsubscribeAppearanceEvents();
    private void OnDestroy() => UnsubscribeAppearanceEvents();

    private VehicleColor? ResolveVehicleColor(bool preferSavedColor)
    {
        // Traffic/chauffeur instances keep their explicit per-instance color.
        if (vehicle == null && explicitVehicleColor != null) return explicitVehicleColor;
        preferSavedColor |= awaitingSavedColor;
        var live = vehicle?.CarFeatures?.VehicleColor;
        if (!preferSavedColor && live != null) return live;
        var colorName = vehicle?.vehicleInstance?.vehicleColorName ?? explicitVehicleColorName;
        if (!string.IsNullOrEmpty(colorName))
        {
            if (live != null && string.Equals(live.name, colorName, StringComparison.Ordinal))
            {
                awaitingSavedColor = false;
                return live; // Optional palettes may be outside the dealer registry.
            }
            if (InstanceBehavior<GlobalReferences>.Instance?.vehicleColors != null &&
                VehicleHelper.TryGetVehicleColor(colorName, out var saved)) return saved;
        }
        return live ?? explicitVehicleColor;
    }

    private readonly struct PaintSlot
    {
        internal PaintSlot(
            Renderer renderer,
            Material material,
            int materialIndex)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
        }

        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
    }
}

