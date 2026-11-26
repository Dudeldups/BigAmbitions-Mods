#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

[AddComponentMenu("")]
internal sealed class LamborghiniAventadorGlassController : MonoBehaviour
{
    private const string WindowMaterialMarker = "Window_Material";
    private const string RedGlassMarker = "RED_GLASS";
    private const string OrangeGlassMarker = "ORANGE_GLASS";
    private const string BodyPaintMarker = "RED_PAINT";
    private const string WheelRimMarker = "AventadorWheel_Rim";
    private const string WheelRubberMarker = "AventadorWheel_Rubber";
    private const string HdMaterialTypeName = "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName = "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";
    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    internal enum MaterialRole { Opaque, BodyPaint, CabinGlass, RedLampLens, OrangeLampLens, ClearLampLens, WheelRim, WheelRubber }

    private readonly List<Renderer> cabinGlass = new List<Renderer>();
    private readonly Dictionary<Material, Material> runtimeMaterials =
        new Dictionary<Material, Material>();
    private readonly Dictionary<Renderer, Material[]> originalMaterials =
        new Dictionary<Renderer, Material[]>();
    private ModContext? context;
    private Coroutine? restoreCoroutine;
    private bool initialized;
    private bool shaderWarningLogged;

    internal void Initialize(ModContext? modContext)
    {
        context = modContext;
        if (!shaderWarningLogged && FindHdrpLitShader() == null)
        {
            context?.Logger.Warn("LamborghiniAventador appearance setup: HDRP/Lit shader is unavailable; imported materials may not render correctly.");
            shaderWarningLogged = true;
        }
        if (initialized)
            return;

        cabinGlass.Clear();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            var materials = renderer.sharedMaterials;
            var sourceMaterials = (Material[])materials.Clone();
            var containsCabinGlass = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                    continue;

                var role = GetMaterialRole(source);
                containsCabinGlass |= role == MaterialRole.CabinGlass;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeAppearance";
                    NormalizeRuntimeMaterial(runtimeMaterial, role);
                    runtimeMaterials.Add(source, runtimeMaterial);
                }
                materials[index] = runtimeMaterial;
            }

            if (materials.Length == 0) continue;
            originalMaterials[renderer] = sourceMaterials;
            renderer.sharedMaterials = materials;
            if (containsCabinGlass) cabinGlass.Add(renderer);
        }

        initialized = true;
        if (cabinGlass.Count == 0)
            context?.Logger.Warn("LamborghiniAventador appearance setup found no renderer slots marked as cabin windows.");
        else
            GlassInfo($"configured vehicle={gameObject.GetInstanceID()} cabinRenderers={cabinGlass.Count} " +
                      $"appearanceRenderers={originalMaterials.Count} runtimeMaterials={runtimeMaterials.Count}.");
        ApplyTransparentMaterials();
        LogNormalizedAppearanceMaterials();
    }

    internal void RestoreAfterVehicleEntered()
    {
        if (!initialized) return;
        if (restoreCoroutine != null) StopCoroutine(restoreCoroutine);
        restoreCoroutine = StartCoroutine(RestoreAfterEntryLifecycle());
    }

    private IEnumerator RestoreAfterEntryLifecycle()
    {
        // Entry callbacks can be followed by native renderer changes. Restore
        // after that known lifecycle transition rather than polling each frame.
        yield return null;
        yield return new WaitForEndOfFrame();
        ApplyTransparentMaterials();
        restoreCoroutine = null;
    }

    private void ApplyTransparentMaterials()
    {
        foreach (var renderer in originalMaterials.Keys)
        {
            if (renderer == null) continue;
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null) continue;
                var role = GetMaterialRole(material);
                if (runtimeMaterials.TryGetValue(material, out var replacement))
                {
                    materials[index] = replacement;
                    material = replacement;
                }
                if (runtimeMaterials.ContainsValue(material))
                    NormalizeRuntimeMaterial(material, role);
            }
            renderer.sharedMaterials = materials;
        }
    }

    internal static void NormalizeRuntimeMaterial(Material material, MaterialRole role)
    {
        var color = GetSourceColor(material);
        var baseTexture = GetTexture(material, "baseColorTexture", "_BaseColorMap", "_MainTex");
        var textureScale = GetTextureScale(material, "baseColorTexture", "_BaseColorMap", "_MainTex");
        var textureOffset = GetTextureOffset(material, "baseColorTexture", "_BaseColorMap", "_MainTex");
        var normalTexture = GetTexture(material, "normalTexture", "_NormalMap");
        var normalTextureScale = GetTextureScale(material, "normalTexture", "_NormalMap");
        var normalTextureOffset = GetTextureOffset(material, "normalTexture", "_NormalMap");
        var normalScale = GetSourceFloat(material, "normalTexture_scale", "_NormalScale", 1f);
        var metallic = GetSourceFloat(material, "metallicFactor", "_Metallic", 0f);
        var roughness = GetSourceFloat(material, "roughnessFactor", "_Roughness", 0.5f);
        if (!material.HasProperty("roughnessFactor") && !material.HasProperty("_Roughness") &&
            material.HasProperty("_Smoothness"))
            roughness = 1f - material.GetFloat("_Smoothness");
        var shader = FindHdrpLitShader();
        if (shader != null && material.shader != shader)
            material.shader = shader;

        if (role == MaterialRole.CabinGlass)
        {
            // The authored pane is black and nearly opaque. A clean neutral tint
            // keeps the cabin visible and avoids multiplying by its dark texture.
            // Low alpha and lower smoothness prevent the HDRP environment from
            // turning the pane into a white mirror.
            color = new Color(0.025f, 0.03f, 0.035f, 0.02f);
            baseTexture = null;
            metallic = 0f;
            roughness = 0.70f;
        }
        else if (role == MaterialRole.RedLampLens)
        {
            color = new Color(0.46f, 0.018f, 0.012f, 0.22f);
            baseTexture = null;
            metallic = 0f;
            roughness = 0.50f;
        }
        else if (role == MaterialRole.OrangeLampLens)
        {
            color = new Color(0.88f, 0.23f, 0.018f, 0.18f);
            baseTexture = null;
            metallic = 0f;
            roughness = 0.50f;
        }
        else if (role == MaterialRole.ClearLampLens)
        {
            // The GLB's LightA_Material is authored as an opaque silver mask,
            // but it is the headlight/rear lamp cover geometry. Keep only a
            // faint neutral tint so the lamp assemblies remain visible through
            // the cover instead of rendering as opaque white plastic.
            color = new Color(0.42f, 0.46f, 0.50f, 0.04f);
            baseTexture = null;
            metallic = 0f;
            roughness = 0.58f;
        }
        else if (role == MaterialRole.BodyPaint)
        {
            // The imported GLB paint slot carries a blue base texture and
            // near-mirror PBR values. The selected game paint is a solid opaque
            // body finish; keep modest clear-coat-like gloss without letting
            // the source texture or bright environment reflections show through.
            color.a = 1f;
            baseTexture = null;
            metallic = 0.12f;
            roughness = 0.38f;
        }
        else if (role == MaterialRole.WheelRim)
        {
            // The source GLB assigns both the tire and forged wheel one black,
            // 60%-metal material. Keep the rim a dark graphite finish while
            // letting the separately owned rubber retain a near-black diffuse.
            color = new Color(0.12f, 0.12f, 0.12f, 1f);
            metallic = 0.65f;
            roughness = 0.55f;
        }
        else if (role == MaterialRole.WheelRubber)
        {
            color = new Color(0.008f, 0.008f, 0.008f, 1f);
            metallic = 0f;
            roughness = 0.85f;
        }
        else
        {
            // Body paint must remain opaque even if imported base color has
            // a partially transparent alpha channel.
            color.a = 1f;
        }

        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        SetColor(material, "baseColorFactor", color);
        SetTexture(material, "_BaseColorMap", baseTexture, textureScale, textureOffset);
        SetTexture(material, "_MainTex", baseTexture, textureScale, textureOffset);
        SetTexture(material, "_NormalMap", normalTexture, normalTextureScale, normalTextureOffset);
        SetFloat(material, "_NormalScale", normalScale);
        SetFloat(material, "_Metallic", metallic);
        SetFloat(material, "_Smoothness", 1f - Mathf.Clamp01(roughness));
        var isTransparent = role == MaterialRole.CabinGlass ||
                            role == MaterialRole.RedLampLens ||
                            role == MaterialRole.OrangeLampLens ||
                            role == MaterialRole.ClearLampLens;
        if (isTransparent)
            ConfigureTransparentMaterial(material);
        else
            ConfigureOpaqueMaterial(material);

        // HDRP derives additional shader keywords from the material fields. Set
        // the role first, validate that final role, then reassert the exact state
        // to keep the importer's old blend/depth settings from leaking through.
        TryValidateHdrpMaterial(material);
        if (isTransparent)
            ConfigureTransparentMaterial(material);
        else
            ConfigureOpaqueMaterial(material);
    }

    private static void ConfigureTransparentMaterial(Material material)
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

    private static void ConfigureOpaqueMaterial(Material material)
    {
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_BlendMode", 0f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        SetFloat(material, "_AlphaSrcBlend", (float)BlendMode.One);
        SetFloat(material, "_AlphaDstBlend", (float)BlendMode.Zero);
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_AlphaCutoffEnable", 0f);
        SetFloat(material, "_SupportDecals", 0f);
        SetFloat(material, "_ReceivesSSR", 0f);
        SetFloat(material, "_ReceivesSSRTransparent", 0f);
        SetFloat(material, "_EnableBlendModePreserveSpecularLighting", 0f);
        SetFloat(material, "_RefractionModel", 0f);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
        material.SetShaderPassEnabled("TransparentDepthPrepass", false);
        material.SetShaderPassEnabled("TransparentDepthPostpass", false);
    }

    private static MaterialRole GetMaterialRole(Material material)
    {
        if (ContainsIgnoreCase(material.name, WheelRimMarker)) return MaterialRole.WheelRim;
        if (ContainsIgnoreCase(material.name, WheelRubberMarker)) return MaterialRole.WheelRubber;
        if (ContainsIgnoreCase(material.name, BodyPaintMarker)) return MaterialRole.BodyPaint;
        if (ContainsIgnoreCase(material.name, RedGlassMarker)) return MaterialRole.RedLampLens;
        if (ContainsIgnoreCase(material.name, OrangeGlassMarker)) return MaterialRole.OrangeLampLens;
        if (ContainsIgnoreCase(material.name, "LightA_Material")) return MaterialRole.ClearLampLens;
        if (ContainsIgnoreCase(material.name, WindowMaterialMarker)) return MaterialRole.CabinGlass;
        return MaterialRole.Opaque;
    }

    private static Shader? FindHdrpLitShader() =>
        Shader.Find("HDRP/Lit") ?? Shader.Find("High Definition Render Pipeline/Lit");

    private static Color GetSourceColor(Material material)
    {
        if (material.HasProperty("baseColorFactor")) return material.GetColor("baseColorFactor");
        if (material.HasProperty("_BaseColor")) return material.GetColor("_BaseColor");
        if (material.HasProperty("_Color")) return material.GetColor("_Color");
        return Color.white;
    }

    private static float GetSourceFloat(Material material, string sourceProperty, string targetProperty, float fallback)
    {
        if (material.HasProperty(sourceProperty)) return material.GetFloat(sourceProperty);
        if (material.HasProperty(targetProperty)) return material.GetFloat(targetProperty);
        return fallback;
    }

    private static Texture? GetTexture(Material material, params string[] names)
    {
        foreach (var name in names)
            if (material.HasProperty(name) && material.GetTexture(name) != null)
                return material.GetTexture(name);
        return null;
    }

    private static Vector2 GetTextureScale(Material material, params string[] names)
    {
        foreach (var name in names)
            if (material.HasProperty(name)) return material.GetTextureScale(name);
        return Vector2.one;
    }

    private static Vector2 GetTextureOffset(Material material, params string[] names)
    {
        foreach (var name in names)
            if (material.HasProperty(name)) return material.GetTextureOffset(name);
        return Vector2.zero;
    }

    private static void SetTexture(Material material, string property, Texture? texture, Vector2 scale, Vector2 offset)
    {
        if (!material.HasProperty(property)) return;
        material.SetTexture(property, texture);
        material.SetTextureScale(property, scale);
        material.SetTextureOffset(property, offset);
    }

    private static void SetColor(Material material, string property, Color value)
    {
        if (material.HasProperty(property)) material.SetColor(property, value);
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
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

    private static bool ContainsIgnoreCase(string value, string marker) =>
        value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;

    private void LogNormalizedAppearanceMaterials()
    {
        foreach (var pair in runtimeMaterials)
        {
            var role = GetMaterialRole(pair.Key);
            if (role == MaterialRole.Opaque) continue;

            var material = pair.Value;
            var color = material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor")
                : material.HasProperty("_Color")
                    ? material.GetColor("_Color")
                    : Color.white;
            var surface = material.HasProperty("_SurfaceType")
                ? material.GetFloat("_SurfaceType") : float.NaN;
            var zWrite = material.HasProperty("_ZWrite")
                ? material.GetFloat("_ZWrite") : float.NaN;
            var baseMap = material.HasProperty("_BaseColorMap")
                ? material.GetTexture("_BaseColorMap") : null;
            var normalMap = material.HasProperty("_NormalMap")
                ? material.GetTexture("_NormalMap") : null;
            var normalScale = material.HasProperty("_NormalScale")
                ? material.GetFloat("_NormalScale") : float.NaN;
            GlassInfo(
                $"material role={role} source='{pair.Key.name}' shader='{material.shader?.name ?? "<none>"}' " +
                $"alpha={color.a:0.00} surface={surface:0.##} zWrite={zWrite:0.##} " +
                $"queue={material.renderQueue} baseMap={(baseMap != null ? baseMap.name : "<none>")} " +
                $"normalMap={(normalMap != null ? normalMap.name : "<none>")} normalScale={normalScale:0.##}");
        }
    }

    private void GlassInfo(string message)
    {
        if (LamborghiniAventadorDiagnostics.DebugEnabled &&
            LamborghiniAventadorAppearanceDiagnostics.GlassDebugEnabled)
            context?.Logger.Info("LamborghiniAventador glass " + message);
    }

    private void OnDestroy()
    {
        if (restoreCoroutine != null) StopCoroutine(restoreCoroutine);
        restoreCoroutine = null;
        foreach (var pair in originalMaterials)
        {
            var renderer = pair.Key;
            if (renderer == null) continue;
            var current = renderer.sharedMaterials;
            var originals = pair.Value;
            var changed = false;
            var slotCount = Math.Min(current.Length, originals.Length);
            for (var index = 0; index < slotCount; index++)
            {
                if (current[index] == null || !runtimeMaterials.ContainsValue(current[index]))
                    continue;
                current[index] = originals[index];
                changed = true;
            }
            if (changed) renderer.sharedMaterials = current;
        }
        originalMaterials.Clear();
        foreach (var material in runtimeMaterials.Values)
            if (material != null) Destroy(material);
        runtimeMaterials.Clear();
    }
}

internal static class LamborghiniAventadorAppearanceDiagnostics
{
    internal static bool GlassDebugEnabled { get; set; } = false;
}
