#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

internal readonly struct AudiRS6RMaterialFixResult
{
    internal AudiRS6RMaterialFixResult(
        int rendererCount,
        int wheelRendererCount,
        int wheelMaterialCloneCount,
        int solidRendererCount,
        int decalMasksCleared,
        int hdrpMaterialsFixed,
        int transparentMaterialsProtected,
        int hdrpMaterialsValidated,
        string shaderNames)
    {
        RendererCount = rendererCount;
        WheelRendererCount = wheelRendererCount;
        WheelMaterialCloneCount = wheelMaterialCloneCount;
        SolidRendererCount = solidRendererCount;
        DecalMasksCleared = decalMasksCleared;
        HdrpMaterialsFixed = hdrpMaterialsFixed;
        TransparentMaterialsProtected = transparentMaterialsProtected;
        HdrpMaterialsValidated = hdrpMaterialsValidated;
        ShaderNames = shaderNames;
    }

    internal int RendererCount { get; }
    internal int WheelRendererCount { get; }
    internal int WheelMaterialCloneCount { get; }
    internal int SolidRendererCount { get; }
    internal int DecalMasksCleared { get; }
    internal int HdrpMaterialsFixed { get; }
    internal int TransparentMaterialsProtected { get; }
    internal int HdrpMaterialsValidated { get; }
    internal string ShaderNames { get; }
}

internal static class AudiRS6RMaterials
{
    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string FrontLampRendererName = "B:Light_Geo_lodA_B:Light_Geo_lodASG1_0";
    private const string HdMaterialTypeName = "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName = "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    internal static AudiRS6RMaterialFixResult FixSolidVehicleMaterials(
        GameObject vehicleRoot,
        ICollection<Material> ownedMaterials)
    {
        var visualRoot = FindImportedVisualRoot(vehicleRoot);
        var bodyRenderers = visualRoot != null
            ? visualRoot.GetComponentsInChildren<Renderer>(true)
            : Array.Empty<Renderer>();
        var bodyRendererSet = new HashSet<Renderer>(bodyRenderers);
        var wheelRenderers = new List<Renderer>();
        foreach (var renderer in vehicleRoot.GetComponentsInChildren<Renderer>(true))
            if (renderer != null && IsWheelVisual(renderer.transform)) wheelRenderers.Add(renderer);
        var wheelRendererSet = new HashSet<Renderer>(wheelRenderers);
        var isolatedWheelMaterials = new Dictionary<Material, Material>();
        var isolatedBodyMaterials = new Dictionary<Material, Material>();

        var rendererSet = new HashSet<Renderer>();
        var renderers = new List<Renderer>(bodyRenderers.Length + wheelRenderers.Count);
        foreach (var renderer in bodyRenderers)
            if (renderer != null && rendererSet.Add(renderer)) renderers.Add(renderer);
        foreach (var renderer in wheelRenderers)
            if (renderer != null && rendererSet.Add(renderer)) renderers.Add(renderer);
        var materials = new HashSet<Material>();
        var shaderNames = new HashSet<string>(StringComparer.Ordinal);
        var solidRendererCount = 0;
        var decalMasksCleared = 0;
        var hdrpMaterialsFixed = 0;
        var transparentMaterialsProtected = 0;
        var hdrpMaterialsValidated = 0;
        var wheelMaterialCloneCount = 0;

        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer is ParticleSystemRenderer || renderer is TrailRenderer ||
                renderer is LineRenderer)
            {
                continue;
            }

            var previousMask = renderer.renderingLayerMask;
            renderer.renderingLayerMask &= ~HdrpDecalLayerMask;
            if (renderer.renderingLayerMask != previousMask)
                decalMasksCleared++;

            // Road-wheel visuals can share imported materials with cabin meshes. Isolate and correct
            // every wheel material before touching the remaining imported body hierarchy.
            if (wheelRendererSet.Contains(renderer))
            {
                wheelMaterialCloneCount += CloneWheelMaterialsWithoutDecals(
                    renderer,
                    isolatedWheelMaterials,
                    ownedMaterials);
                continue;
            }

            if (!bodyRendererSet.Contains(renderer))
                continue;

            var hasSolidMaterial = false;
            var sourceMaterials = renderer.sharedMaterials;
            var runtimeMaterials = new Material[sourceMaterials.Length];
            for (var materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++)
            {
                var sourceMaterial = sourceMaterials[materialIndex];
                if (sourceMaterial == null)
                    continue;

                if (!isolatedBodyMaterials.TryGetValue(sourceMaterial, out var material))
                {
                    material = new Material(sourceMaterial)
                    {
                        name = sourceMaterial.name + " Audi Instance",
                        hideFlags = HideFlags.DontSave,
                    };
                    isolatedBodyMaterials.Add(sourceMaterial, material);
                    ownedMaterials.Add(material);
                }
                runtimeMaterials[materialIndex] = material;

                if (IsTransparentOrCutout(material))
                {
                    if (!materials.Add(material))
                        continue;

                    var transparentShaderName = material.shader != null ? material.shader.name : "<null>";
                    if (IsHdrpMaterial(material, transparentShaderName))
                    {
                        if (ProtectTransparentMaterialFromDecals(material))
                            hdrpMaterialsValidated++;
                        transparentMaterialsProtected++;
                    }

                    shaderNames.Add(transparentShaderName);
                    continue;
                }

                hasSolidMaterial = true;
                if (!materials.Add(material))
                    continue;

                var originalShaderName = material.shader != null ? material.shader.name : "<null>";
                if (!IsHdrpMaterial(material, originalShaderName))
                {
                    shaderNames.Add(originalShaderName);
                    continue;
                }

                RebindToHdrpLit(material);
                if (FixSolidHdrpMaterial(material))
                    hdrpMaterialsValidated++;

                hdrpMaterialsFixed++;
                var finalShaderName = material.shader != null ? material.shader.name : "<null>";
                shaderNames.Add(string.Equals(originalShaderName, finalShaderName, StringComparison.Ordinal)
                    ? finalShaderName
                    : originalShaderName + "->" + finalShaderName);
            }

            renderer.sharedMaterials = runtimeMaterials;

            if (hasSolidMaterial)
                solidRendererCount++;
        }

        var orderedShaderNames = new List<string>(shaderNames);
        orderedShaderNames.Sort(StringComparer.Ordinal);
        return new AudiRS6RMaterialFixResult(
            renderers.Count,
            wheelRenderers.Count,
            wheelMaterialCloneCount,
            solidRendererCount,
            decalMasksCleared,
            hdrpMaterialsFixed,
            transparentMaterialsProtected,
            hdrpMaterialsValidated,
            string.Join("|", orderedShaderNames));
    }

    internal static void PreparePaintMaterial(Material material)
    {
        RebindToHdrpLit(material);
        FixSolidHdrpMaterial(material);
        SetFloat(material, "_Metallic", 0.18f);
        SetFloat(material, "_Smoothness", 0.72f);
        SetFloat(material, "_CoatMask", 0.22f);
        SetFloat(material, "_CoatSmoothness", 0.88f);
        SetFloat(material, "_EmissiveIntensity", 0f);
        SetColor(material, "_EmissiveColor", Color.black);
        SetColor(material, "_EmissionColor", Color.black);
        material.DisableKeyword("_EMISSION");
    }

    private static GameObject? FindImportedVisualRoot(GameObject vehicleRoot)
    {
        foreach (var renderer in vehicleRoot.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer == null ||
                !string.Equals(renderer.name, FrontLampRendererName, StringComparison.Ordinal))
            {
                continue;
            }

            // The imported renderer is nested as Object_3/B:Light_Geo_lodA/<renderer>.
            // Scope material mutation to Object_3 so shared stock vehicle/effect materials are untouched.
            return renderer.transform.parent?.parent?.gameObject;
        }

        return null;
    }

    private static bool IsWheelVisual(Transform? transform)
    {
        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.IndexOf("steeringwheel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                current.name.IndexOf("steering wheel", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
        }

        for (var current = transform; current != null; current = current.parent)
        {
            var name = current.name;
            if (name.StartsWith("3DWheel ", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("WheelMesh", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("_WheelController", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Wheels", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static int CloneWheelMaterialsWithoutDecals(
        Renderer renderer,
        IDictionary<Material, Material> isolatedWheelMaterials,
        ICollection<Material> ownedWheelMaterials)
    {
        var sourceMaterials = renderer.sharedMaterials;
        if (sourceMaterials.Length == 0)
            return 0;

        var clonedMaterials = new Material[sourceMaterials.Length];
        var cloneCount = 0;
        for (var index = 0; index < sourceMaterials.Length; index++)
        {
            var source = sourceMaterials[index];
            if (source == null)
                continue;

            if (!isolatedWheelMaterials.TryGetValue(source, out var clone))
            {
                clone = new Material(source)
                {
                    name = source.name + " Audi Wheel No Decals",
                    hideFlags = HideFlags.DontSave,
                };
                RebindToHdrpLit(clone);
                FixSolidHdrpMaterial(clone);
                isolatedWheelMaterials.Add(source, clone);
                ownedWheelMaterials.Add(clone);
                cloneCount++;
            }

            clonedMaterials[index] = clone;
        }

        renderer.sharedMaterials = clonedMaterials;
        return cloneCount;
    }

    private static bool IsTransparentOrCutout(Material material)
    {
        if (material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
            material.IsKeywordEnabled("_ALPHATEST_ON"))
        {
            return true;
        }

        if (material.HasProperty("_SurfaceType") && material.GetFloat("_SurfaceType") > 0.5f)
            return true;
        if (material.HasProperty("_AlphaCutoffEnable") && material.GetFloat("_AlphaCutoffEnable") > 0.5f)
            return true;
        if (HasTransparentColor(material, "baseColorFactor") ||
            HasTransparentColor(material, "_BaseColor") ||
            HasTransparentColor(material, "_Color"))
        {
            return true;
        }

        var renderType = material.GetTag("RenderType", false, string.Empty);
        return material.renderQueue >= (int)RenderQueue.Transparent ||
               renderType.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0 ||
               renderType.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasTransparentColor(Material material, string property)
    {
        return material.HasProperty(property) && material.GetColor(property).a < 0.999f;
    }

    private static bool IsHdrpMaterial(Material material, string shaderName)
    {
        if (shaderName.StartsWith("HDRP/", StringComparison.Ordinal) ||
            shaderName.StartsWith("High Definition Render Pipeline/", StringComparison.Ordinal))
        {
            return true;
        }

        var shaderGraphTarget = material.GetTag("ShaderGraphTargetId", false, string.Empty);
        return shaderGraphTarget.StartsWith("HD", StringComparison.Ordinal) ||
               material.HasProperty("_SupportDecals");
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

        // Preserve the critical state even if HDRP validation changes in a future game build.
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        return validated;
    }

    private static bool ProtectTransparentMaterialFromDecals(Material material)
    {
        SetFloat(material, "_SupportDecals", 0f);
        var validated = TryValidateHdrpMaterial(material);

        // Keep the glass/cutout rendering state intact; only suppress decal projection.
        SetFloat(material, "_SupportDecals", 0f);
        material.EnableKeyword("_DISABLE_DECALS");
        return validated;
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
            material, "baseColorTexture", "_BaseColorMap", "_MainTex");
        var baseTexture = baseTextureProperty != null ? material.GetTexture(baseTextureProperty) : null;
        var baseScale = baseTextureProperty != null ? material.GetTextureScale(baseTextureProperty) : Vector2.one;
        var baseOffset = baseTextureProperty != null ? material.GetTextureOffset(baseTextureProperty) : Vector2.zero;

        var normalTextureProperty = FirstTextureProperty(material, "normalTexture", "_NormalMap");
        var normalTexture = normalTextureProperty != null ? material.GetTexture(normalTextureProperty) : null;
        var maskTexture = material.HasProperty("_MaskMap") ? material.GetTexture("_MaskMap") : null;
        var normalScale = GetFloat(material, "normalTexture_scale", "normalScale", "_NormalScale", 1f);
        var metallic = GetFloat(material, "metallicFactor", "_Metallic", null, 0f);
        var roughness = GetFloat(material, "roughnessFactor", null, null, 1f);

        material.shader = hdrpLit;
        SetColor(material, "_BaseColor", baseColor);
        SetTexture(material, "_BaseColorMap", baseTexture, baseScale, baseOffset);
        SetTexture(material, "_NormalMap", normalTexture, Vector2.one, Vector2.zero);
        SetTexture(material, "_MaskMap", maskTexture, Vector2.one, Vector2.zero);
        SetFloat(material, "_NormalScale", normalScale);
        SetFloat(material, "_Metallic", metallic);
        SetFloat(material, "_Smoothness", 1f - Mathf.Clamp01(roughness));
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
        if (material.HasProperty(secondProperty))
            return material.GetColor(secondProperty);
        return fallback;
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
        if (thirdProperty != null && material.HasProperty(thirdProperty))
            return material.GetFloat(thirdProperty);
        return fallback;
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

    private static void SetOpaqueColor(Material material, string property)
    {
        if (!material.HasProperty(property))
            return;

        var color = material.GetColor(property);
        color.a = 1f;
        material.SetColor(property, color);
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
            var method = ResolveValidateMaterialMethod();
            if (method != null)
            {
                var result = method.Invoke(null, new object[] { material });
                if (!(result is bool validated) || validated)
                    return true;
            }

            method = ResolveValidateShaderGraphMaterialMethod();
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

    private static MethodInfo? ResolveValidateMaterialMethod()
    {
        if (validateMaterialMethodResolved)
            return validateMaterialMethod;

        validateMaterialMethodResolved = true;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(HdMaterialTypeName, false);
            if (type == null)
                continue;

            validateMaterialMethod = type.GetMethod(
                "ValidateMaterial",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Material) },
                null);
            if (validateMaterialMethod != null)
                break;
        }

        return validateMaterialMethod;
    }

    private static MethodInfo? ResolveValidateShaderGraphMaterialMethod()
    {
        if (validateShaderGraphMaterialMethodResolved)
            return validateShaderGraphMaterialMethod;

        validateShaderGraphMaterialMethodResolved = true;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(ShaderGraphApiTypeName, false);
            if (type == null)
                continue;

            validateShaderGraphMaterialMethod = type.GetMethod(
                "ValidateLightingMaterial",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(Material) },
                null);
            if (validateShaderGraphMaterialMethod != null)
                break;
        }

        return validateShaderGraphMaterialMethod;
    }
}

internal sealed class AudiRS6RMaterialController : MonoBehaviour
{
    private const string PaintRendererName = "Paint";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");

    private bool applied;
    private readonly List<Material> ownedMaterials = new List<Material>();
    private readonly MaterialPropertyBlock paintProperties = new MaterialPropertyBlock();
    private VehicleController? vehicle;
    private ModContext? context;
    private Renderer? paintRenderer;
    private Material? sourcePaintMaterial;
    private Material? ownedPaintMaterial;
    private string? explicitVehicleColorName;
    private VehicleColor? explicitVehicleColor;
    private VehicleColor? appliedVehicleColor;
    private Color32 appliedTint;
    private bool hasAppliedTint;

    internal void Initialize(VehicleController vehicle, ModContext? context)
    {
        if (applied)
        {
            RefreshPaint();
            return;
        }

        this.vehicle = vehicle;
        this.context = context;
        var result = AudiRS6RMaterials.FixSolidVehicleMaterials(vehicle.gameObject, ownedMaterials);
        ConfigurePaintRenderer();
        RefreshPaint();
        applied = true;
        if (result.WheelRendererCount == 0 || result.WheelMaterialCloneCount == 0 ||
            result.TransparentMaterialsProtected == 0)
        {
            context?.Logger.Warn(
                $"AudiRS6R materials vehicle='{vehicle.name}' instance={vehicle.GetInstanceID()}: " +
                "opaque or transparent HDRP material protection was incomplete; ground decals may still project onto the vehicle.");
        }
    }

    internal void InitializeForPrivateDriver(string? vehicleColorName, VehicleColor? vehicleColor)
    {
        vehicle = null;
        context = null;
        explicitVehicleColorName = vehicleColorName;
        explicitVehicleColor = vehicleColor;
        hasAppliedTint = false;
        if (!applied)
        {
            AudiRS6RMaterials.FixSolidVehicleMaterials(gameObject, ownedMaterials);
            ConfigurePaintRenderer();
            applied = true;
        }
        RefreshPaint();
    }

    internal void InitializeForAmbientTraffic(VehicleColor vehicleColor)
    {
        InitializeForPrivateDriver(vehicleColor.name, vehicleColor);
    }

    internal bool HasAppliedColor => hasAppliedTint;

    internal void RefreshPaint()
    {
        if (paintRenderer == null || ownedPaintMaterial == null)
            return;

        var selected = ResolveVehicleColor();
        if (selected == null)
            return;
        var tint = (Color32)selected.tint;
        if (hasAppliedTint && ReferenceEquals(selected, appliedVehicleColor) && tint.Equals(appliedTint))
            return;

        var selectedColor = (Color)tint;
        selectedColor.a = 1f;
        paintProperties.Clear();
        paintRenderer.GetPropertyBlock(paintProperties, 0);
        if (ownedPaintMaterial.HasProperty(BaseColor))
            paintProperties.SetColor(BaseColor, selectedColor);
        if (ownedPaintMaterial.HasProperty(ColorProperty))
            paintProperties.SetColor(ColorProperty, selectedColor);
        if (ownedPaintMaterial.HasProperty(BaseColorFactor))
            paintProperties.SetColor(BaseColorFactor, selectedColor);
        paintRenderer.SetPropertyBlock(paintProperties, 0);

        appliedVehicleColor = selected;
        appliedTint = tint;
        hasAppliedTint = true;
    }

    private void ConfigurePaintRenderer()
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !string.Equals(renderer.name, PaintRendererName, StringComparison.Ordinal))
                continue;

            var materials = renderer.sharedMaterials;
            if (materials.Length == 0 || materials[0] == null)
                break;

            paintRenderer = renderer;
            sourcePaintMaterial = materials[0];
            ownedPaintMaterial = new Material(sourcePaintMaterial)
            {
                name = sourcePaintMaterial.name + " Audi HDRP Paint",
                hideFlags = HideFlags.DontSave,
            };
            AudiRS6RMaterials.PreparePaintMaterial(ownedPaintMaterial);
            materials[0] = ownedPaintMaterial;
            renderer.sharedMaterials = materials;
            context?.Logger.Info(
                $"AudiRS6R paint vehicle={vehicle?.GetInstanceID()}: renderer='{renderer.name}', " +
                $"shader='{ownedPaintMaterial.shader?.name ?? "missing"}', " +
                $"perInstance=true, emissive=false.");
            return;
        }

        context?.Logger.Warn(
            $"AudiRS6R paint vehicle={vehicle?.GetInstanceID()}: exact Paint renderer/material was not found.");
    }

    private VehicleColor? ResolveVehicleColor()
    {
        var live = vehicle?.CarFeatures?.VehicleColor;
        if (live != null)
            return live;
        if (explicitVehicleColor != null)
            return explicitVehicleColor;
        var colorName = vehicle?.vehicleInstance?.vehicleColorName ?? explicitVehicleColorName;
        return !string.IsNullOrEmpty(colorName) && VehicleHelper.TryGetVehicleColor(colorName, out var saved)
            ? saved
            : null;
    }

    private void OnDestroy()
    {
        if (paintRenderer != null && sourcePaintMaterial != null)
        {
            var materials = paintRenderer.sharedMaterials;
            if (materials.Length > 0 && materials[0] == ownedPaintMaterial)
            {
                materials[0] = sourcePaintMaterial;
                paintRenderer.sharedMaterials = materials;
            }
        }
        if (ownedPaintMaterial != null)
            Destroy(ownedPaintMaterial);
        foreach (var material in ownedMaterials)
            if (material != null) Destroy(material);
        ownedMaterials.Clear();
    }
}
