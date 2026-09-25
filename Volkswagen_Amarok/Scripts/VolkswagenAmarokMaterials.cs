#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct VolkswagenAmarokMaterialFixResult
{
    public VolkswagenAmarokMaterialFixResult(
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

internal enum VolkswagenAmarokTransparentRole
{
    Authored,
    CabinGlass,
    HeadlampLens,
    RearLampLens,
    OtherClear,
}

public static class VolkswagenAmarokMaterials
{
    public const float RimMetallic = 0.65f;
    public const float RimSmoothness = 0.45f;
    public static readonly Color RimBaseColor = new Color(0.03f, 0.03f, 0.03f, 1f);

    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string RimMaterialMarker = "__AMAROK_SEPARATE_RIM_FINISH_DISABLED__";
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static VolkswagenAmarokMaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var controller = vehicle.GetComponent<VolkswagenAmarokMaterialController>();
        if (controller == null)
            controller = vehicle.AddComponent<VolkswagenAmarokMaterialController>();
        return controller.Initialize(null);
    }

    private static Material? FindCanonicalRimMaterial(GameObject vehicle)
    {
        Material? fallback = null;
        foreach (var renderer in vehicle.GetComponentsInChildren<Renderer>(true))
        {
            if (!IsAmarokRenderer(renderer.transform))
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
        return name.IndexOf("phong15", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("EXT_GLASS", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool NormalizeImportedMaterial(Material material)
    {
        if (IsTransparentMaterial(material))
        {
            PrepareTransparentMaterial(material, VolkswagenAmarokTransparentRole.OtherClear);
            return true;
        }

        RebindToHdrpLit(material);
        return FixSolidHdrpMaterial(material);
    }



    public static bool IsAmarokRenderer(Transform transform)
    {
        if (transform.name.StartsWith("AmarokDamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("AmarokWheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("AmarokFixedCaliper", StringComparison.Ordinal))
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("AmarokDamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal) ||
                current.name.StartsWith("AmarokWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("AmarokFixedCaliper", StringComparison.Ordinal))
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

    private static void NeutralizeAmarokBodyPaintTexture(Material material)
    {
        if (material.name.IndexOf(
                "_BA_VehiclePaint",
                StringComparison.OrdinalIgnoreCase) < 0)
            return;

        if (material.HasProperty("_BaseColorMap"))
            material.SetTexture("_BaseColorMap", null);
        if (material.HasProperty("baseColorTexture"))
            material.SetTexture("baseColorTexture", null);
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", null);
    }

    internal static bool FixSolidHdrpMaterial(Material material)
    {
        NeutralizeAmarokBodyPaintTexture(material);
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
        VolkswagenAmarokTransparentRole role = VolkswagenAmarokTransparentRole.OtherClear)
    {
        var sourceTransparentTint = GetColor(
            material, "baseColorFactor", "_BaseColor", Color.white);
        var rearLampLens = role == VolkswagenAmarokTransparentRole.RearLampLens;
        var cabinGlass = role == VolkswagenAmarokTransparentRole.CabinGlass;
        // Imported glTF transparency is not reliable in the game's HDRP build.
        // Exterior clear-surface variants use deterministic per-instance Lit
        // states. Authored gauges, symbols, and screens never enter this path.
        RebindToHdrpLit(material);
        var tint = rearLampLens
            ? new Color(0.48f, 0.018f, 0.012f, 0.78f)
            : cabinGlass
                ? new Color(0.09f, 0.12f, 0.15f, 0.14f)
                : role == VolkswagenAmarokTransparentRole.HeadlampLens
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
        if (rearLampLens)
        {
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", 0.90f);
            SetFloat(material, "roughnessFactor", 0.10f);
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
        PrepareTransparentMaterial(material, VolkswagenAmarokTransparentRole.CabinGlass);
    }

    internal static VolkswagenAmarokTransparentRole GetTransparentRole(
        Renderer renderer,
        Material material)
    {
        if (string.Equals(
                renderer.name,
                "vw_amorak_2018:cam_EXT_GLASS_0",
                StringComparison.OrdinalIgnoreCase))
            return VolkswagenAmarokTransparentRole.RearLampLens;

        if (!IsTransparentMaterial(material))
            return VolkswagenAmarokTransparentRole.Authored;

        Transform? visual = null;
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            if (!string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal))
                continue;
            visual = current;
            break;
        }

        if (visual != null)
        {
            var center = visual.InverseTransformPoint(renderer.bounds.center);
            // Rear light clusters sit at the two outer corners behind the rear
            // axle. The rear cabin window is central and much farther forward.
            if (center.z < -1.70f && Mathf.Abs(center.x) > 0.48f && center.y > 0.35f)
                return VolkswagenAmarokTransparentRole.RearLampLens;
        }

        var rendererName = renderer.name;
        if (rendererName.IndexOf("headlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("far", StringComparison.OrdinalIgnoreCase) >= 0)
            return VolkswagenAmarokTransparentRole.HeadlampLens;

        if (rendererName.IndexOf("windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return VolkswagenAmarokTransparentRole.CabinGlass;
        }

        return VolkswagenAmarokTransparentRole.OtherClear;
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
public sealed class VolkswagenAmarokPaintController : MonoBehaviour
{
    private const string BodyMaterialMarker = "phong5";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly List<Material> ownedPanelMaterials = new List<Material>();
    private readonly List<Mesh> ownedFactoryBlackMeshes = new List<Mesh>();
    private readonly List<Material> ownedFactoryBlackMaterials = new List<Material>();
    private bool explicitFactoryBlackGeometryPrepared;
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
                $"VolkswagenAmarok paint vehicle={vehicle?.GetInstanceID()}: no vehicle color " +
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

        ApplyFactoryBlackExteriorParts();

        appliedColorName = colorName;
        appliedTint = tint;
        hasAppliedTint = true;
        VolkswagenAmarokDiagnostics.PaintInfo(
            context,
            $"VolkswagenAmarok paint vehicle={vehicle?.GetInstanceID()}: applied " +
            $"color='{colorName}' rgba={tint} to {slots.Count} body/interior slots " +
            $"source='{source}'.");
    }

    private static bool IsAmarokBodyPaintMaterial(Renderer renderer, Material material)
    {
        return renderer.name.IndexOf(
                   "VehiclePaint_Blue",
                   StringComparison.OrdinalIgnoreCase) >= 0 ||
               material.name.IndexOf(
                   "_BA_VehiclePaint",
                   StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsRawAmarokBodyPaintMaterial(Material material) =>
        material.name.IndexOf("phong5", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("dorr_R", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsFactoryBlackExteriorPart(Renderer renderer)
    {
        if (string.Equals(
                renderer.name,
                "vw_amorak_2018:bump_rear_ok_phong5_0",
                StringComparison.OrdinalIgnoreCase) ||
            renderer.name.StartsWith(
                "VolkswagenAmarok_FactoryBlack_",
                StringComparison.OrdinalIgnoreCase) ||
            renderer.name.IndexOf("extra1", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        for (var current = renderer.transform; current != null; current = current.parent)
        {
            var name = current.name;
            if (name.IndexOf("mudflap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("mud_flap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("splash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("runningboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("running_board", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("sidestep", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("side_step", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("footboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("foot_board", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("step_pad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("tread", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        Transform? visual = null;
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            if (!string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal))
                continue;
            visual = current;
            break;
        }
        if (visual == null || !TryGetMeshBoundsInSpace(renderer, visual, out var localBounds))
            return false;

        var center = localBounds.center;
        var size = localBounds.size;

        // Never classify the complete baked body shell as a black accessory.
        if (size.x > 1.60f && size.z > 3.0f)
            return false;

        // Separate thin guards behind the wheels.
        var splashGuard =
            Mathf.Abs(center.x) > 0.62f &&
            Mathf.Abs(center.z) > 0.72f &&
            center.y < 0.72f &&
            size.x < 0.80f &&
            size.y < 1.05f &&
            size.z < 0.85f;

        // Rubber tread pads sitting on the long side tubes. The metal tube uses
        // another material slot and is intentionally not recolored here.
        var sideTread =
            Mathf.Abs(center.x) > 0.58f &&
            Mathf.Abs(center.z) < 1.80f &&
            center.y < 0.68f &&
            size.x < 0.85f &&
            size.y < 0.45f &&
            size.z < 2.90f;

        // Black tread surface above the rear chrome bumper.
        var rearTread =
            center.z < -1.72f &&
            center.y < 0.78f &&
            size.x < 2.35f &&
            size.y < 0.50f &&
            size.z < 1.05f;

        return splashGuard || sideTread || rearTread;
    }

    private static bool TryGetMeshBoundsInSpace(
        Renderer renderer,
        Transform space,
        out Bounds bounds)
    {
        bounds = default;
        var filter = renderer.GetComponent<MeshFilter>();
        var mesh = filter?.sharedMesh;
        if (mesh == null)
            return false;

        var source = mesh.bounds;
        var extents = source.extents;
        var initialized = false;
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var local = source.center + Vector3.Scale(
                extents, new Vector3(x, y, z));
            var point = space.InverseTransformPoint(
                renderer.transform.TransformPoint(local));
            if (!initialized)
            {
                bounds = new Bounds(point, Vector3.zero);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(point);
            }
        }
        return initialized;
    }

    private void PrepareExplicitFactoryBlackGeometry()
    {
        if (explicitFactoryBlackGeometryPrepared)
            return;
        explicitFactoryBlackGeometryPrepared = true;

        Transform? visual = null;
        foreach (var current in GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(current.name, "AmarokVisual", StringComparison.Ordinal))
                continue;
            visual = current;
            break;
        }
        if (visual == null)
        {
            context?.Logger.Warn(
                "VolkswagenAmarok paint: AmarokVisual is missing for black-part splitting.");
            return;
        }

        var authoredSideSteps = PrepareAuthoredFactoryBlackOverlay(
            "FactoryBlack_SideSteps");
        var authoredMudguards = PrepareAuthoredFactoryBlackOverlay(
            "FactoryBlack_Mudguards");

        var sideStepTriangles = 0;
        var mudGuardTriangles = 0;
        var candidates = GetComponentsInChildren<MeshRenderer>(true);
        foreach (var renderer in candidates)
        {
            if (renderer == null ||
                renderer.name.StartsWith(
                    "VolkswagenAmarok_FactoryBlack_",
                    StringComparison.Ordinal))
                continue;

            var isAventura = HasNameFragmentInHierarchy(renderer, "aventuramodular");
            var isRearBumper = HasNameFragmentInHierarchy(renderer, "bump_rear_ok");
            var hasBodyPaint = false;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && IsRawAmarokBodyPaintMaterial(material))
                {
                    hasBodyPaint = true;
                    break;
                }
            }

            if (isAventura && !authoredSideSteps)
            {
                sideStepTriangles += SplitFactoryBlackTriangles(
                    renderer,
                    visual,
                    "SideSteps",
                    (center, normal) =>
                        Mathf.Abs(center.x) > 0.52f &&
                        center.y > -0.20f &&
                        center.y < 0.82f &&
                        center.z > -1.55f &&
                        center.z < 1.55f);
                continue;
            }

            if (!hasBodyPaint || isRearBumper || authoredMudguards)
                continue;

            mudGuardTriangles += SplitFactoryBlackTriangles(
                renderer,
                visual,
                "Mudguards",
                (center, normal) =>
                {
                    var frontBehindWheel = center.z > 1.02f && center.z < 1.58f;
                    var rearBehindWheel = center.z > -2.02f && center.z < -1.42f;
                    return (frontBehindWheel || rearBehindWheel) &&
                           Mathf.Abs(center.x) > 0.70f &&
                           center.y > -0.20f &&
                           center.y < 0.72f &&
                           Mathf.Abs(normal.z) > 0.28f;
                });
        }

        VolkswagenAmarokDiagnostics.PaintInfo(
            context,
            $"VolkswagenAmarok paint black trim: authoredSideSteps={authoredSideSteps}, " +
            $"authoredMudguards={authoredMudguards}, " +
            $"fallbackSideStepTriangles={sideStepTriangles}, " +
            $"fallbackMudGuardTriangles={mudGuardTriangles}.");
    }

    private bool PrepareAuthoredFactoryBlackOverlay(string marker)
    {
        MeshRenderer? found = null;
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer == null ||
                renderer.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            found = renderer;
            break;
        }
        if (found == null)
            return false;

        var shader = Shader.Find("HDRP/Lit") ??
                     Shader.Find("High Definition Render Pipeline/Lit");
        if (shader == null)
        {
            context?.Logger.Warn(
                $"VolkswagenAmarok paint: HDRP/Lit missing for authored black trim '{marker}'.");
            return false;
        }

        var material = new Material(shader)
        {
            name = "VolkswagenAmarok_" + marker + "_Material",
        };
        var black = new Color(0.018f, 0.018f, 0.018f, 1f);
        if (material.HasProperty(BaseColor))
            material.SetColor(BaseColor, black);
        if (material.HasProperty(ColorProperty))
            material.SetColor(ColorProperty, black);
        if (material.HasProperty(BaseColorFactor))
            material.SetColor(BaseColorFactor, black);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.30f);
        if (material.HasProperty("_SurfaceType"))
            material.SetFloat("_SurfaceType", 0f);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 1f);

        found.sharedMaterial = material;
        found.shadowCastingMode = ShadowCastingMode.On;
        found.receiveShadows = true;
        found.enabled = true;
        // Geometry is already displaced outward per face by the Blender exporter.
        // Do not scale around the object's pivot; that was the source of front/back
        // inconsistencies on the mudguards and partial coverage on the side steps.
        ownedFactoryBlackMaterials.Add(material);

        VolkswagenAmarokDiagnostics.PaintInfo(
            context,
            $"VolkswagenAmarok paint: authored factory-black overlay '{marker}' enabled.");
        return true;
    }

    private static bool HasNameFragmentInHierarchy(
        Renderer renderer,
        string fragment)
    {
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            if (current.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private int SplitFactoryBlackTriangles(
        MeshRenderer sourceRenderer,
        Transform visual,
        string suffix,
        Func<Vector3, Vector3, bool> shouldExtract)
    {
        var filter = sourceRenderer.GetComponent<MeshFilter>();
        var sourceMesh = filter?.sharedMesh;
        if (filter == null || sourceMesh == null || sourceMesh.vertexCount == 0)
            return 0;

        var vertices = sourceMesh.vertices;
        var keepBySubMesh = new List<int>[sourceMesh.subMeshCount];
        var blackBySubMesh = new List<int>[sourceMesh.subMeshCount];
        var keptTriangles = 0;
        var blackTriangles = 0;

        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            keepBySubMesh[subMesh] = new List<int>();
            blackBySubMesh[subMesh] = new List<int>();
            var triangles = sourceMesh.GetTriangles(subMesh);

            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var ia = triangles[index];
                var ib = triangles[index + 1];
                var ic = triangles[index + 2];
                var aWorld = sourceRenderer.transform.TransformPoint(vertices[ia]);
                var bWorld = sourceRenderer.transform.TransformPoint(vertices[ib]);
                var cWorld = sourceRenderer.transform.TransformPoint(vertices[ic]);
                var a = visual.InverseTransformPoint(aWorld);
                var b = visual.InverseTransformPoint(bWorld);
                var c = visual.InverseTransformPoint(cWorld);
                var center = (a + b + c) / 3f;
                var normal = Vector3.Cross(b - a, c - a).normalized;

                var extracted = shouldExtract(center, normal);
                var target = extracted
                    ? blackBySubMesh[subMesh]
                    : keepBySubMesh[subMesh];
                target.Add(ia);
                target.Add(ib);
                target.Add(ic);

                if (extracted)
                    blackTriangles++;
                else
                    keptTriangles++;
            }
        }

        if (blackTriangles == 0 || keptTriangles == 0)
        {
            VolkswagenAmarokDiagnostics.PaintInfo(
                context,
                $"VolkswagenAmarok paint triangle candidate source='{sourceRenderer.name}' " +
                $"part='{suffix}' extracted={blackTriangles} kept={keptTriangles}.");
            return 0;
        }

        var remainder = Instantiate(sourceMesh);
        remainder.name = sourceMesh.name + "_VehicleColorRemainder";
        var blackMesh = Instantiate(sourceMesh);
        blackMesh.name = sourceMesh.name + "_FactoryBlack_" + suffix;
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            remainder.SetTriangles(keepBySubMesh[subMesh], subMesh, true);
            blackMesh.SetTriangles(blackBySubMesh[subMesh], subMesh, true);
        }
        remainder.RecalculateBounds();
        blackMesh.RecalculateBounds();

        filter.sharedMesh = remainder;
        ownedFactoryBlackMeshes.Add(remainder);
        ownedFactoryBlackMeshes.Add(blackMesh);

        var host = new GameObject(
            "VolkswagenAmarok_FactoryBlack_" + suffix + "_" + sourceRenderer.name);
        host.layer = sourceRenderer.gameObject.layer;
        host.transform.SetParent(sourceRenderer.transform, false);
        host.AddComponent<MeshFilter>().sharedMesh = blackMesh;

        var blackRenderer = host.AddComponent<MeshRenderer>();
        blackRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        blackRenderer.receiveShadows = sourceRenderer.receiveShadows;
        blackRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        blackRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        blackRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;

        var black = new Color(0.018f, 0.018f, 0.018f, 1f);
        var sourceMaterials = sourceRenderer.sharedMaterials;
        var blackMaterials = new Material[sourceMaterials.Length];
        for (var index = 0; index < sourceMaterials.Length; index++)
        {
            var source = sourceMaterials[index];
            if (source == null)
                continue;

            var material = Instantiate(source);
            material.name = source.name + "_FactoryBlack_" + suffix;
            if (material.HasProperty(BaseColor))
                material.SetColor(BaseColor, black);
            if (material.HasProperty(ColorProperty))
                material.SetColor(ColorProperty, black);
            if (material.HasProperty(BaseColorFactor))
                material.SetColor(BaseColorFactor, black);
            if (material.HasProperty("_BaseColorMap"))
                material.SetTexture("_BaseColorMap", null);
            if (material.HasProperty("baseColorTexture"))
                material.SetTexture("baseColorTexture", null);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", null);

            blackMaterials[index] = material;
            ownedFactoryBlackMaterials.Add(material);
        }
        blackRenderer.sharedMaterials = blackMaterials;

        VolkswagenAmarokDiagnostics.PaintInfo(
            context,
            $"VolkswagenAmarok paint extracted source='{sourceRenderer.name}' " +
            $"part='{suffix}' triangles={blackTriangles}.");
        return blackTriangles;
    }

    private void ApplyFactoryBlackExteriorParts()
    {
        var black = new Color(0.018f, 0.018f, 0.018f, 1f);
        var applied = new List<string>();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!IsFactoryBlackExteriorPart(renderer))
                continue;
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null || !IsRawAmarokBodyPaintMaterial(material))
                    continue;
                properties.Clear();
                renderer.GetPropertyBlock(properties, index);
                if (material.HasProperty(BaseColor)) properties.SetColor(BaseColor, black);
                if (material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, black);
                if (material.HasProperty(BaseColorFactor)) properties.SetColor(BaseColorFactor, black);
                renderer.SetPropertyBlock(properties, index);
                if (!applied.Contains(renderer.name))
                    applied.Add(renderer.name);
            }
        }
        VolkswagenAmarokDiagnostics.PaintInfo(
            context,
            $"VolkswagenAmarok paint factoryBlack=[{string.Join(", ", applied)}].");
    }

    private void FindPaintSlots()
    {
        PrepareExplicitFactoryBlackGeometry();
        slots.Clear();
        var bodySlots = 0;
        var interiorAccentSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!VolkswagenAmarokMaterials.IsAmarokRenderer(renderer.transform))
                continue;
            var materials = renderer.sharedMaterials;
            var materialsChanged = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null && IsAmarokBodyPaintMaterial(renderer, material))
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

        VolkswagenAmarokDiagnostics.PaintInfo(
            context,
            $"VolkswagenAmarok paint vehicle={vehicle?.GetInstanceID()}: mapped " +
            $"bodySlots={bodySlots}, interiorAccentSlots={interiorAccentSlots}; " +
            "glass, lamps, unmapped carbon, rims, brakes, and black trim excluded.");
        if (bodySlots == 0)
            context?.Logger.Warn(
                $"VolkswagenAmarok paint mapping incomplete bodySlots={bodySlots}, " +
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
        if (false &&
            materialName.IndexOf(
                "carbon_roof",
                StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }
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
        foreach (var material in ownedFactoryBlackMaterials)
            if (material != null) Destroy(material);
        ownedFactoryBlackMaterials.Clear();
        foreach (var mesh in ownedFactoryBlackMeshes)
            if (mesh != null) Destroy(mesh);
        ownedFactoryBlackMeshes.Clear();
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

