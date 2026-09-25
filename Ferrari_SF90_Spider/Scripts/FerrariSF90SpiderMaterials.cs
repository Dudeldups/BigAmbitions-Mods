#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct FerrariSF90SpiderMaterialFixResult
{
    public FerrariSF90SpiderMaterialFixResult(
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

public static class FerrariSF90SpiderMaterials
{
    public const float RimMetallic = 0.08f;
    public const float RimSmoothness = 0.32f;
    public static readonly Color RimBaseColor = new Color(0.23f, 0.23f, 0.23f, 1f);

    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string RimMaterialMarker = "Wheel1A_3D_3DWheel1C_Material";
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static FerrariSF90SpiderMaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        // Runtime material repair changes shader state and finish properties. Keep
        // those changes on instance-owned materials so one SF90Spider cannot repaint or
        // reconfigure another spawned SF90Spider (or the imported bundle asset).
        if (Application.isPlaying)
        {
            var instanceController = vehicle.GetComponent<FerrariSF90SpiderMaterialController>();
            if (instanceController == null)
                instanceController = vehicle.AddComponent<FerrariSF90SpiderMaterialController>();
            instanceController.Initialize();
        }

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
            if (!IsFerrariRenderer(renderer.transform))
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

        return new FerrariSF90SpiderMaterialFixResult(
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
            if (!IsFerrariRenderer(renderer.transform))
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

        // The supplied SF90 GLB packs tire, rim and brake-disc shading into one
        // authored wheel material. Do not tint or force metallic/smoothness here;
        // doing so would also make the tire rubber metallic. We only normalize the
        // material object reference across all four generated wheel renderers.

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
            name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("LightA_Material", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f;
    }

    public static bool IsFerrariRenderer(Transform transform)
    {
        if (transform.name.StartsWith("FerrariSF90SpiderDamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("FerrariSF90SpiderWheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("FerrariSF90SpiderFixedCaliper", StringComparison.Ordinal) ||
            transform.name.IndexOf("Ferrari_SF90Spider", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("FerrariSF90SpiderDamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "FerrariSF90SpiderVisual", StringComparison.Ordinal) ||
                current.name.IndexOf("Ferrari_SF90Spider", StringComparison.OrdinalIgnoreCase) >= 0 ||
                current.name.StartsWith("FerrariSF90SpiderWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("FerrariSF90SpiderFixedCaliper", StringComparison.Ordinal))
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

    private static void FixTransparentHdrpMaterial(Material material)
    {
        var name = material.name;
        var cabinGlass = IsCabinGlassMaterial(material);
        var headlampLens = IsHeadlampLensMaterial(material);

        // The imported glTF transmission materials look correct in Blender but
        // collapse toward opaque black under the game's HDRP lighting. Cadillac's
        // proven vehicle path avoids that by rendering passive glass/lenses as
        // simple unlit transparent surfaces; the dedicated SF90 light overlays
        // provide the active DRL/indicator/brake/reverse illumination.
        RebindToHdrpUnlit(material);
        var tint = GetTransparentTint(name, cabinGlass, headlampLens);
        SetColor(material, "_UnlitColor", tint);
        SetColor(material, "_BaseColor", tint);
        SetColor(material, "_Color", tint);
        SetColor(material, "baseColorFactor", tint);

        // The source Window material carries a black base factor + transmission
        // model and the LightA atlas can also darken the physical lens. For the
        // passive glass shell we want the transparent tint itself, not the glTF
        // black/transmission shading.
        if (cabinGlass || headlampLens)
        {
            SetTexture(material, "_UnlitColorMap", null, Vector2.one, Vector2.zero);
            SetTexture(material, "_BaseColorMap", null, Vector2.one, Vector2.zero);
            SetTexture(material, "_MainTex", null, Vector2.one, Vector2.zero);
            SetTexture(material, "baseColorTexture", null, Vector2.one, Vector2.zero);
        }

        SetFloat(material, "transmissionFactor", 0f);
        SetFloat(material, "_TransmissionEnable", 0f);
        SetFloat(material, "_TransmissionMask", 0f);
        SetFloat(material, "_RefractionModel", 0f);
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

    private static Color GetTransparentTint(string name, bool cabinGlass, bool headlampLens)
    {
        if (cabinGlass)
            return new Color(0.08f, 0.10f, 0.12f, 0.14f);
        if (headlampLens)
            return new Color(0.88f, 0.93f, 1f, 0.055f);
        if (name.IndexOf("RED_GLASS", StringComparison.OrdinalIgnoreCase) >= 0)
            return new Color(0.42f, 0.012f, 0.006f, 0.22f);
        return new Color(0.18f, 0.20f, 0.22f, 0.10f);
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

    internal static void RestoreHeadlampLensMaterial(Material material)
    {
        if (IsHeadlampLensMaterial(material))
            FixTransparentHdrpMaterial(material);
    }

    public static bool IsCabinGlassMaterial(Material material)
    {
        var name = material.name;
        return name.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Glass_Blask", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsHeadlampLensMaterial(Material material)
    {
        return material.name.IndexOf("LightA_Material", StringComparison.OrdinalIgnoreCase) >= 0;
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
public sealed class FerrariSF90SpiderMaterialController : MonoBehaviour
{
    private readonly List<Material> ownedMaterials = new List<Material>();
    private bool initialized;

    internal void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        var clones = new Dictionary<Material, Material>();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!FerrariSF90SpiderMaterials.IsFerrariRenderer(renderer.transform))
                continue;

            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                    continue;

                if (!clones.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_FerrariInstance";
                    clones.Add(source, runtimeMaterial);
                    ownedMaterials.Add(runtimeMaterial);
                }

                materials[index] = runtimeMaterial;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
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
