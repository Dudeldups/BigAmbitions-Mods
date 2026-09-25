#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct DodgeChallenger2018MaterialFixResult
{
    public DodgeChallenger2018MaterialFixResult(
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

internal enum DodgeChallenger2018TransparentRole
{
    Authored,
    CabinGlass,
    HeadlampLens,
    RearLampLens,
    OtherClear,
}

public static class DodgeChallenger2018Materials
{
    public const float RimMetallic = 0.65f;
    public const float RimSmoothness = 0.45f;
    public static readonly Color RimBaseColor = new Color(0.03f, 0.03f, 0.03f, 1f);

    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string RimMaterialMarker = "Wheel2Mtl1";
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static DodgeChallenger2018MaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var controller = vehicle.GetComponent<DodgeChallenger2018MaterialController>();
        if (controller == null)
            controller = vehicle.AddComponent<DodgeChallenger2018MaterialController>();
        return controller.Initialize(null);
    }

    private static Material? FindCanonicalRimMaterial(GameObject vehicle)
    {
        Material? fallback = null;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsDodgeRenderer(renderer.transform))
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
        if (name.IndexOf("glasssurr", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        if (name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f;
    }

    public static bool NormalizeImportedMaterial(Material material)
    {
        var name = material.name;
        if (name.IndexOf("GlassRed", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            PrepareTransparentMaterial(material, DodgeChallenger2018TransparentRole.RearLampLens);
            return true;
        }
        if (name.IndexOf("GlassMtl", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            PrepareTransparentMaterial(material, DodgeChallenger2018TransparentRole.CabinGlass);
            return true;
        }
        if (name.IndexOf("TexturedMtl1", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            RebindToHdrpLit(material);
            SetTexture(material, "_BaseColorMap", null, Vector2.one, Vector2.zero);
            SetTexture(material, "_MainTex", null, Vector2.one, Vector2.zero);
            var splitter = new Color(0.018f, 0.020f, 0.022f, 1f);
            SetColor(material, "_BaseColor", splitter);
            SetColor(material, "_Color", splitter);
            SetColor(material, "baseColorFactor", splitter);
            SetFloat(material, "_Metallic", 0.05f);
            SetFloat(material, "_Smoothness", 0.28f);
            return FixSolidHdrpMaterial(material);
        }

        // glTF shader graphs are editor/import-time dependencies and render
        // magenta in the shipped game. Rebind every remaining authored material
        // to HDRP/Lit while preserving its base texture, normal, metallic and roughness.
        RebindToHdrpLit(material);
        return FixSolidHdrpMaterial(material);
    }

    public static bool IsDodgeRenderer(Transform transform)
    {
        if (transform.name.StartsWith("DodgeDamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("DodgeWheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("DodgeFixedCaliper", StringComparison.Ordinal))
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("DodgeDamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "DodgeVisual", StringComparison.Ordinal) ||
                current.name.StartsWith("DodgeWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("DodgeFixedCaliper", StringComparison.Ordinal))
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
        DodgeChallenger2018TransparentRole role = DodgeChallenger2018TransparentRole.OtherClear)
    {
        var cabinGlass = role == DodgeChallenger2018TransparentRole.CabinGlass;

        if (role == DodgeChallenger2018TransparentRole.RearLampLens)
        {
            // The authored red lens has no opaque lamp housing behind it, so a
            // transparent material exposes the cabin through the taillight.
            // Keep the passive lens as smooth dark-red plastic; the dedicated
            // emissive tail/brake/reverse/indicator overlays render on top.
            RebindToHdrpLit(material);
            var rearLens = new Color(0.16f, 0.006f, 0.004f, 1f);
            SetTexture(material, "_BaseColorMap", null, Vector2.one, Vector2.zero);
            SetTexture(material, "_MainTex", null, Vector2.one, Vector2.zero);
            SetColor(material, "_BaseColor", rearLens);
            SetColor(material, "_Color", rearLens);
            SetColor(material, "baseColorFactor", rearLens);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", 0.82f);
            // Fully reset the material's blend state. Merely changing
            // _SurfaceType on an imported transparent glTF material leaves HDRP
            // blend factors/keywords behind and the lens still renders see-through.
            FixSolidHdrpMaterial(material);
            SetFloat(material, "_SrcBlend", (float)BlendMode.One);
            SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
            SetFloat(material, "_AlphaSrcBlend", (float)BlendMode.One);
            SetFloat(material, "_AlphaDstBlend", (float)BlendMode.Zero);
            SetFloat(material, "_TransparentZWrite", 1f);
            SetFloat(material, "_Cull", (float)CullMode.Off);
            SetFloat(material, "_CullMode", (float)CullMode.Off);
            SetFloat(material, "_CullModeForward", (float)CullMode.Off);
            SetFloat(material, "_DoubleSidedEnable", 1f);
            material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_DOUBLESIDED_ON");
            material.SetOverrideTag("RenderType", "Opaque");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.SetShaderPassEnabled("DepthOnly", true);
            material.SetShaderPassEnabled("ShadowCaster", false);
            return;
        }

        // Imported glTF transparency is not reliable in the game's HDRP build.
        // Exterior clear-surface variants use deterministic per-instance Lit
        // states. Authored gauges, symbols, and screens never enter this path.
        // Imported transparent Lit surfaces can collapse to opaque black in the
        // player build. Use the proven HDRP/Unlit transparent path for panes/lenses.
        RebindToHdrpUnlit(material);
        var tint = role == DodgeChallenger2018TransparentRole.RearLampLens
            ? new Color(0.36f, 0.010f, 0.008f, 0.32f)
            : cabinGlass
                ? new Color(0.08f, 0.10f, 0.12f, 0.16f)
                : role == DodgeChallenger2018TransparentRole.HeadlampLens
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
            // The source pane texture is nearly black and, when multiplied by the
            // transparent tint, made the windows look opaque. Cabin glass uses a
            // clean neutral tint instead; the separate glass-surround mesh remains.
            SetTexture(material, "_BaseColorMap", null, Vector2.one, Vector2.zero);
            SetTexture(material, "_UnlitColorMap", null, Vector2.one, Vector2.zero);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "metallicFactor", 0f);
            SetFloat(material, "_Smoothness", 0.95f);
            SetFloat(material, "roughnessFactor", 0f);
        }
        SetFloat(material, "_TransparentDepthPrepassEnable", 0f);
        SetFloat(material, "_TransparentDepthPostpassEnable", 0f);
        SetFloat(material, "_TransparentBackfaceEnable", 0f);
        SetFloat(material, "_Cull", (float)CullMode.Off);
        SetFloat(material, "_CullMode", (float)CullMode.Off);
        SetFloat(material, "_CullModeForward", (float)CullMode.Off);
        SetFloat(material, "_TransparentCullMode", (float)CullMode.Off);
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

    internal static void RestoreCabinGlassMaterial(Material material)
    {
        PrepareTransparentMaterial(material, DodgeChallenger2018TransparentRole.CabinGlass);
    }

    internal static DodgeChallenger2018TransparentRole GetTransparentRole(
        Renderer renderer,
        Material material)
    {
        var rendererName = renderer.name;
        var name = material.name;
        var underGlassRed = false;
        var underWindow = false;
        var underClearGlass = false;
        var underLight = false;
        var underGlassSurround = false;
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            var currentName = current.name;
            underGlassRed |= currentName.IndexOf(":GlassRed", StringComparison.OrdinalIgnoreCase) >= 0;
            underWindow |= currentName.IndexOf(":Window", StringComparison.OrdinalIgnoreCase) >= 0;
            underClearGlass |= currentName.IndexOf(":Glass_", StringComparison.OrdinalIgnoreCase) >= 0;
            underLight |= currentName.IndexOf(":Light_", StringComparison.OrdinalIgnoreCase) >= 0;
            underGlassSurround |= currentName.IndexOf("glasssurr", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (underGlassSurround)
            return DodgeChallenger2018TransparentRole.Authored;

        if (underGlassRed ||
            name.IndexOf("GlassRed", StringComparison.OrdinalIgnoreCase) >= 0)
            return DodgeChallenger2018TransparentRole.RearLampLens;

        if (underWindow)
            return DodgeChallenger2018TransparentRole.CabinGlass;

        if (underClearGlass || underLight)
            return DodgeChallenger2018TransparentRole.HeadlampLens;

        if (!IsTransparentMaterial(material))
            return DodgeChallenger2018TransparentRole.Authored;

        // Alpha on imported badges/interior shader graphs is not evidence that
        // they are exterior glass.
        return DodgeChallenger2018TransparentRole.Authored;
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
public sealed class DodgeChallenger2018MaterialController : MonoBehaviour
{
    private const string RimMaterialMarker = "Wheel2Mtl1";
    private readonly List<Material> ownedMaterials = new List<Material>();
    private DodgeChallenger2018MaterialFixResult result;
    private bool initialized;

    internal DodgeChallenger2018MaterialFixResult Initialize(ModContext? context)
    {
        if (initialized)
            return result;

        initialized = true;
        var clones = new Dictionary<Material, Dictionary<DodgeChallenger2018TransparentRole, Material>>();
        var rendererCount = 0;
        var transparentMaterials = 0;
        var authoredTransparentMaterials = 0;
        var rimSlots = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!DodgeChallenger2018Materials.IsDodgeRenderer(renderer.transform))
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

                var role = DodgeChallenger2018Materials.GetTransparentRole(renderer, source);
                if (!clones.TryGetValue(source, out var roleVariants))
                {
                    roleVariants = new Dictionary<DodgeChallenger2018TransparentRole, Material>();
                    clones.Add(source, roleVariants);
                }
                if (!roleVariants.TryGetValue(role, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_DodgeInstance_" + role;
                    roleVariants.Add(role, runtimeMaterial);
                    ownedMaterials.Add(runtimeMaterial);

                    if (role != DodgeChallenger2018TransparentRole.Authored)
                    {
                        DodgeChallenger2018Materials.PrepareTransparentMaterial(runtimeMaterial, role);
                        transparentMaterials++;
                    }
                    else
                    {
                        DodgeChallenger2018Materials.NormalizeImportedMaterial(runtimeMaterial);
                    }
                    if (runtimeMaterial.name.IndexOf(
                            RimMaterialMarker,
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        HasAncestor(renderer.transform, "DodgeWheel"))
                    {
                        SetRimFinish(runtimeMaterial);
                    }
                }

                if (role == DodgeChallenger2018TransparentRole.CabinGlass)
                    hasCabinGlass = true;
                if (runtimeMaterial.name.IndexOf(
                        RimMaterialMarker,
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    HasAncestor(renderer.transform, "DodgeWheel"))
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

        result = new DodgeChallenger2018MaterialFixResult(
            rendererCount,
            0,
            0,
            transparentMaterials,
            0,
            rimSlots,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
        DodgeChallenger2018Diagnostics.Info(
            context,
            $"DodgeChallenger2018 materials vehicle={GetInstanceID()}: cloned " +
            $"{ownedMaterials.Count} materials for {rendererCount} renderers, " +
            $"clearSurfaceVariants={transparentMaterials}, " +
            $"authoredTransparentRetained={authoredTransparentMaterials}, " +
            $"cabinGlass={cabinGlassRenderers}, " +
            $"rimSlots={rimSlots}; opaque authored materials retained unchanged.");
        return result;
    }

    private static void SetRimFinish(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", DodgeChallenger2018Materials.RimBaseColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", DodgeChallenger2018Materials.RimBaseColor);
        if (material.HasProperty("baseColorFactor"))
            material.SetColor("baseColorFactor", DodgeChallenger2018Materials.RimBaseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", DodgeChallenger2018Materials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", DodgeChallenger2018Materials.RimSmoothness);
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
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
public sealed class DodgeChallenger2018PaintController : MonoBehaviour
{
    private static readonly Dictionary<string, VehicleColor> ResolvedPrivateColors =
        new Dictionary<string, VehicleColor>(StringComparer.Ordinal);
    private const string BodyMaterialMarker = "paint1Mtl1";
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

    internal void ApplyCurrentColor(string source, bool preferLiveColor = false)
    {
        var selected = ResolveVehicleColor(preferLiveColor);
        if (selected == null)
        {
            context?.Logger.Warn(
                $"DodgeChallenger2018 paint vehicle={vehicle?.GetInstanceID()}: no vehicle color " +
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
        DodgeChallenger2018Diagnostics.PaintInfo(
            context,
            $"DodgeChallenger2018 paint vehicle={vehicle?.GetInstanceID()}: applied " +
            $"color='{colorName}' rgba={tint} to {slots.Count} body/interior slots " +
            $"source='{source}'.");
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        var bodySlots = 0;
        var caliperSlots = 0;
        var interiorAccentSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!DodgeChallenger2018Materials.IsDodgeRenderer(renderer.transform))
                continue;
            var materials = renderer.sharedMaterials;
            var materialsChanged = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                var isDamageBody = renderer.name.StartsWith(
                    "DodgeDamageBody",
                    StringComparison.Ordinal);

                if (material != null &&
                    HasAncestor(renderer.transform, "DodgeFixedCaliper") &&
                    renderer.name.IndexOf("DodgeBremboLabel", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    // Calipers follow the selected vehicle color. The authored
                    // caliper texture contains a white placeholder strip where
                    // the Brembo branding should be, so use a clean paintable
                    // surface and add the branding separately in the prefab setup.
                    var caliperMaterial = Instantiate(material);
                    caliperMaterial.name = material.name + "_BodyColorCaliper";
                    PrepareCaliperPaintSurface(caliperMaterial);
                    materials[index] = caliperMaterial;
                    ownedPanelMaterials.Add(caliperMaterial);
                    slots.Add(new PaintSlot(
                        renderer,
                        caliperMaterial,
                        index,
                        PaintCategory.Caliper));
                    caliperSlots++;
                    materialsChanged = true;
                }
                else if (material != null &&
                    (isDamageBody ||
                     material.name.IndexOf(
                         BodyMaterialMarker,
                         StringComparison.OrdinalIgnoreCase) >= 0))
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

        DodgeChallenger2018Diagnostics.PaintInfo(
            context,
            $"DodgeChallenger2018 paint vehicle={vehicle?.GetInstanceID()}: mapped " +
            $"bodySlots={bodySlots}, caliperSlots={caliperSlots}, " +
            $"interiorAccentSlots={interiorAccentSlots}; " +
            "glass, lamps, unmapped carbon, rims, and black trim excluded.");
        if (bodySlots == 0)
            context?.Logger.Warn(
                $"DodgeChallenger2018 paint mapping incomplete bodySlots={bodySlots}, " +
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
        var materialName = material.name;
        // The supplied Challenger model exposes its exterior paint directly through
        // paint1Mtl1; there is no Dodge-style textured roof paint cover.
        return false;
    }

    private static void PrepareCaliperPaintSurface(Material material)
    {
        if (material.HasProperty("_BaseColorMap"))
            material.SetTexture("_BaseColorMap", Texture2D.whiteTexture);
        if (material.HasProperty("baseColorTexture"))
            material.SetTexture("baseColorTexture", Texture2D.whiteTexture);
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", Texture2D.whiteTexture);
        if (material.HasProperty("_MaskMap"))
            material.SetTexture("_MaskMap", null);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
        if (material.HasProperty("baseColorFactor"))
            material.SetColor("baseColorFactor", Color.white);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0.18f);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.72f);
        if (material.HasProperty("_CoatMask"))
            material.SetFloat("_CoatMask", 0.15f);
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

    private VehicleColor? ResolveVehicleColor(bool preferLiveColor)
    {
        var live = vehicle?.CarFeatures?.VehicleColor;

        // Repainter preview deliberately changes only CarFeatures.VehicleColor;
        // vehicleInstance.vehicleColorName must remain untouched until purchase.
        // Honor that live value only for the explicit preview path. All normal
        // initialization/save-load paths still prefer the persisted color name.
        if (preferLiveColor && live != null)
            return live;

        var colorName = vehicle?.vehicleInstance?.vehicleColorName ??
                        explicitVehicleColorName;

        if (colorName is string persistedName &&
            persistedName.Length > 0 &&
            TryResolveVehicleColorByName(persistedName, out var persisted))
        {
            return persisted;
        }

        if (live != null)
            return live;

        return explicitVehicleColor;
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

        if (ResolvedPrivateColors.TryGetValue(colorName, out var cached) &&
            cached != null)
        {
            vehicleColor = cached;
            return true;
        }

        // Vehicle Repainter / DeveloperTools can own private VehicleColor
        // ScriptableObjects that are intentionally not registered in
        // VehicleHelper. This fallback can be expensive in a large modded save,
        // so scan only on a cache miss and retain the resolved object by name.
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

            ResolvedPrivateColors[colorName] = candidate;
            vehicleColor = candidate;
            return true;
        }

        vehicleColor = null!;
        return false;
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
        Caliper,
        InteriorAccent,
    }
}

