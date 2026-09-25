#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct BMWM4G82MaterialFixResult
{
    public BMWM4G82MaterialFixResult(
        int rendererCount,
        int decalMasksCleared,
        int opaqueMaterialsFixed,
        int transparentMaterialsFixed,
        int materialsValidated,
        int bodyPaintMaterialsNormalized,
        int rimSlotsNormalized,
        int cabinGlassRenderers,
        int cabinGlassRenderersReenabled)
    {
        RendererCount = rendererCount;
        DecalMasksCleared = decalMasksCleared;
        OpaqueMaterialsFixed = opaqueMaterialsFixed;
        TransparentMaterialsFixed = transparentMaterialsFixed;
        MaterialsValidated = materialsValidated;
        BodyPaintMaterialsNormalized = bodyPaintMaterialsNormalized;
        RimSlotsNormalized = rimSlotsNormalized;
        CabinGlassRenderers = cabinGlassRenderers;
        CabinGlassRenderersReenabled = cabinGlassRenderersReenabled;
    }

    public int RendererCount { get; }
    public int DecalMasksCleared { get; }
    public int OpaqueMaterialsFixed { get; }
    public int TransparentMaterialsFixed { get; }
    public int MaterialsValidated { get; }
    public int BodyPaintMaterialsNormalized { get; }
    public int RimSlotsNormalized { get; }
    public int CabinGlassRenderers { get; }
    public int CabinGlassRenderersReenabled { get; }
}

public static class BMWM4G82Materials
{
    public const float RimMetallic = 0.08f;
    public const float RimSmoothness = 0.32f;
    public const float BodyPaintMetallic = 0.30f;
    public const float BodyPaintSmoothness = 0.92f;
    public static readonly Color RimBaseColor = new Color(0.23f, 0.23f, 0.23f, 1f);
    public static readonly Color CaliperBaseColor = new Color(0.78f, 0.80f, 0.82f, 1f);

    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string RimMaterialMarker = "_main";
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactorProperty = Shader.PropertyToID("baseColorFactor");
    private static readonly int MetallicProperty = Shader.PropertyToID("_Metallic");
    private static readonly int SmoothnessProperty = Shader.PropertyToID("_Smoothness");
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static BMWM4G82MaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var canonicalRimMaterial = FindCanonicalRimMaterial(vehicle);
        var materials = new HashSet<Material>();
        var rendererCount = 0;
        var decalMasksCleared = 0;
        var opaqueMaterialsFixed = 0;
        var transparentMaterialsFixed = 0;
        var materialsValidated = 0;
        var bodyPaintMaterialsNormalized = 0;
        var rimSlotsNormalized = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsBMWRenderer(renderer.transform))
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

                if (IsTexturedDecalMaterial(material))
                {
                    FixTexturedCutoutMaterial(material);
                    opaqueMaterialsFixed++;
                    continue;
                }

                if (IsTransparentMaterial(material))
                {
                    FixTransparentHdrpMaterial(material);
                    transparentMaterialsFixed++;
                    continue;
                }

                RebindToHdrpLit(material);
                if (FixSolidHdrpMaterial(material))
                    materialsValidated++;
                if (IsBodyPaintMaterial(material))
                {
                    ConfigureBodyPaintFinish(material);
                    bodyPaintMaterialsNormalized++;
                }
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

        return new BMWM4G82MaterialFixResult(
            rendererCount,
            decalMasksCleared,
            opaqueMaterialsFixed,
            transparentMaterialsFixed,
            materialsValidated,
            bodyPaintMaterialsNormalized,
            rimSlotsNormalized,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
    }

    private static Material? FindCanonicalRimMaterial(GameObject vehicle)
    {
        Material? fallback = null;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsBMWRenderer(renderer.transform))
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

    public static int ApplyCaliperFinish(GameObject vehicle)
    {
        var configured = 0;
        var properties = new MaterialPropertyBlock();
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsCaliperRenderer(renderer.transform))
                continue;
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;
                properties.Clear();
                renderer.GetPropertyBlock(properties, index);
                if (material.HasProperty(BaseColorProperty))
                    properties.SetColor(BaseColorProperty, CaliperBaseColor);
                if (material.HasProperty(ColorProperty))
                    properties.SetColor(ColorProperty, CaliperBaseColor);
                if (material.HasProperty(BaseColorFactorProperty))
                    properties.SetColor(BaseColorFactorProperty, CaliperBaseColor);
                if (material.HasProperty(MetallicProperty))
                    properties.SetFloat(MetallicProperty, 0.35f);
                if (material.HasProperty(SmoothnessProperty))
                    properties.SetFloat(SmoothnessProperty, 0.46f);
                renderer.SetPropertyBlock(properties, index);
                configured++;
            }
        }
        return configured;
    }

    internal static bool IsCaliperRenderer(Transform transform)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.StartsWith("BMWFixedCaliper", StringComparison.Ordinal))
                return true;
        return false;
    }

    public static bool IsTransparentMaterial(Material material)
    {
        var name = material.name;
        // The source GLB marks the large engine-bay and cabin atlases as
        // BLEND even though nearly all their pixels are opaque. Treating that
        // import flag as glass made the steering wheel, seats, door cards and
        // console fade almost completely in the game's HDRP renderer.
        if (name.IndexOf("EngineA", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("InteriorA", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }
        if (name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f;
    }

    public static bool IsBMWRenderer(Transform transform)
    {
        if (transform.name.StartsWith("BMWDamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("BMWWheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("BMWFixedCaliper", StringComparison.Ordinal))
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("BMWDamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "BMWVisual", StringComparison.Ordinal) ||
                current.name.StartsWith("BMWWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("BMWFixedCaliper", StringComparison.Ordinal))
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
        SetFloat(material, "_TransmissionEnable", 0f);
        SetFloat(material, "_TransmissionMask", 0f);
        SetFloat(material, "transmissionFactor", 0f);
        return validated;
    }

    private static bool IsBodyPaintMaterial(Material material) =>
        material.name.IndexOf("PaintTNR", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void ConfigureBodyPaintFinish(Material material)
    {
        // Imported PaintTNR is almost black and fully metallic. If a native
        // transition clears the per-instance property block, that authored
        // fallback makes every selected color appear black. Keep the shared
        // tint neutral and let BMWM4G82PaintController provide the saved color.
        SetColor(material, "_BaseColor", Color.white);
        SetColor(material, "_Color", Color.white);
        SetColor(material, "baseColorFactor", Color.white);
        SetFloat(material, "_Metallic", BodyPaintMetallic);
        SetFloat(material, "metallicFactor", BodyPaintMetallic);
        SetFloat(material, "_Smoothness", BodyPaintSmoothness);
        SetFloat(material, "roughnessFactor", 1f - BodyPaintSmoothness);
    }

    private static void FixTransparentHdrpMaterial(Material material)
    {
        var name = material.name;
        var cabinGlass = IsCabinGlassMaterial(material);
        var texturedDecal = IsTexturedDecalMaterial(material);
        if (cabinGlass || texturedDecal)
        {
            // The imported glTF glass shader can retain a valid-looking
            // transparent state while producing no visible pixels in the
            // game's HDRP build. Cabin glass uses the stock game Lit shader so
            // its blend state is deterministic on every vehicle instance.
            RebindToHdrpLit(material);
        }
        var tint = texturedDecal
            ? Color.white
            : cabinGlass
            ? new Color(0.10f, 0.14f, 0.18f, 0.28f)
            : name.IndexOf("Headlight", StringComparison.OrdinalIgnoreCase) >= 0
                ? new Color(0.72f, 0.80f, 0.88f, 0.08f)
                : name.IndexOf("Taillight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                  name.IndexOf("Tail_light", StringComparison.OrdinalIgnoreCase) >= 0
                    ? new Color(0.72f, 0.025f, 0.008f, 0.16f)
                    : new Color(0.82f, 0.86f, 0.90f, 0.08f);
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
            SetFloat(material, "_Metallic", 0.15f);
            SetFloat(material, "metallicFactor", 0.15f);
            SetFloat(material, "_Smoothness", 0.95f);
            SetFloat(material, "roughnessFactor", 0f);
        }
        SetFloat(material, "_TransparentDepthPrepassEnable", 0f);
        SetFloat(material, "_TransparentDepthPostpassEnable", 0f);
        SetFloat(material, "_TransparentBackfaceEnable", 0f);
        SetFloat(material, "_Cull", 0f);
        SetFloat(material, "_CullMode", 0f);
        SetFloat(material, "_CullModeForward", 0f);
        SetFloat(material, "_TransparentCullMode", 0f);
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

    private static void FixTexturedCutoutMaterial(Material material)
    {
        RebindToHdrpLit(material);
        var tint = Color.white;
        SetColor(material, "_BaseColor", tint);
        SetColor(material, "_Color", tint);
        SetColor(material, "baseColorFactor", tint);
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_AlphaCutoffEnable", 1f);
        SetFloat(material, "_AlphaCutoff", 0.04f);
        SetFloat(material, "_Cutoff", 0.04f);
        SetFloat(material, "_SupportDecals", 0f);
        SetFloat(material, "_ReceivesSSR", 0f);
        SetFloat(material, "_ReceivesSSRTransparent", 0f);
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        material.SetOverrideTag("RenderType", "TransparentCutout");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        TryValidateHdrpMaterial(material);
        material.EnableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetShaderPassEnabled("DepthOnly", true);
        material.SetShaderPassEnabled("ShadowCaster", true);
    }

    private static bool IsTexturedDecalMaterial(Material material)
    {
        var name = material.name;
        return name.IndexOf("BadgeA", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("ManufacturerPlateA", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static void RestoreCabinGlassMaterial(Material material)
    {
        if (IsCabinGlassMaterial(material))
            FixTransparentHdrpMaterial(material);
    }

    public static bool IsCabinGlassMaterial(Material material)
    {
        var name = material.name;
        return name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("glasswindshiled", StringComparison.OrdinalIgnoreCase) >= 0;
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
internal sealed class BMWM4G82RuntimeMaterialOwner : MonoBehaviour
{
    private readonly List<Material> ownedMaterials = new List<Material>();
    private bool initialized;

    internal int Initialize()
    {
        if (initialized)
            return ownedMaterials.Count;

        var sharedClones = new Dictionary<Material, Material>();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!BMWM4G82Materials.IsBMWRenderer(renderer.transform))
                continue;

            var materials = renderer.sharedMaterials;
            var changed = false;
            var caliperRenderer = BMWM4G82Materials.IsCaliperRenderer(renderer.transform);
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                    continue;

                Material clone;
                if (caliperRenderer || !sharedClones.TryGetValue(source, out clone))
                {
                    clone = new Material(source)
                    {
                        name = source.name + (caliperRenderer
                            ? " BMWM4G82 Caliper Runtime"
                            : " BMWM4G82 Runtime")
                    };
                    ownedMaterials.Add(clone);
                    if (!caliperRenderer)
                        sharedClones[source] = clone;
                }

                materials[index] = clone;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }

        initialized = true;
        return ownedMaterials.Count;
    }

    private void OnDestroy()
    {
        foreach (var material in ownedMaterials)
            if (material != null) Destroy(material);
        ownedMaterials.Clear();
    }
}
