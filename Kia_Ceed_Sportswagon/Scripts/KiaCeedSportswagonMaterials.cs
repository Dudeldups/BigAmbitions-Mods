#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

public readonly struct KiaCeedSportswagonMaterialFixResult
{
    public KiaCeedSportswagonMaterialFixResult(
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

internal enum KiaCeedSportswagonTransparentRole
{
    Authored,
    CabinGlass,
    RearPrivacyGlass,
    PanoramicRoofGlass,
    HeadlampLens,
    OtherClear,
}

public static class KiaCeedSportswagonMaterials
{
    private const uint HdrpDecalLayerMask = 0x0000FF00u;
    private const string HdMaterialTypeName =
        "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName =
        "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";

    private static MethodInfo? validateMaterialMethod;
    private static bool validateMaterialMethodResolved;
    private static MethodInfo? validateShaderGraphMaterialMethod;
    private static bool validateShaderGraphMaterialMethodResolved;

    public static KiaCeedSportswagonMaterialFixResult FixSolidMaterials(GameObject vehicle)
    {
        var controller = vehicle.GetComponent<KiaCeedSportswagonMaterialController>();
        if (controller == null)
            controller = vehicle.AddComponent<KiaCeedSportswagonMaterialController>();
        return controller.Initialize(null);
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

    public static bool IsKiaCeedSportswagonRenderer(Transform transform)
    {
        if (transform.name.StartsWith("KiaCeedSportswagonDamageBody", StringComparison.Ordinal) ||
            transform.name.StartsWith("KiaCeedSportswagonWheel", StringComparison.Ordinal) ||
            transform.name.StartsWith("KiaCeedSportswagonFixedCaliper", StringComparison.Ordinal))
            return true;

        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.StartsWith("KiaCeedSportswagonDamageBody", StringComparison.Ordinal) ||
                string.Equals(current.name, "KiaCeedSportswagonVisual", StringComparison.Ordinal) ||
                current.name.StartsWith("KiaCeedSportswagonWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("KiaCeedSportswagonFixedCaliper", StringComparison.Ordinal))
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

    internal static bool IsBodyPaintMaterial(Material material) =>
        material.name.IndexOf("carPaint", StringComparison.OrdinalIgnoreCase) >= 0;

    internal static bool PrepareBodyPaintMaterial(Material material)
    {
        // Follow Camaro's no-base-texture paint fallback: remove any baked
        // albedo, start from neutral white, then apply the vehicle color to the
        // per-vehicle material. The Kia GLB's carPaint material has no albedo
        // texture today, but clearing the glTF aliases keeps imported dark tint
        // from multiplying the selected paint if the source changes later.
        RebindToHdrpLit(material);
        if (!material.HasProperty("_BaseColor") ||
            !material.HasProperty("_Metallic") ||
            !material.HasProperty("_Smoothness"))
        {
            return false;
        }

        NeutralizeBodyPaintTexture(material);
        SetOpaqueColor(material, "_BaseColor");
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_AlphaCutoffEnable", 0f);
        SetFloat(material, "_SupportDecals", 0f);
        SetFloat(material, "_Metallic", 0.08f);
        SetFloat(material, "_Smoothness", 0.86f);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_DISABLE_DECALS");
        material.SetOverrideTag("RenderType", "Opaque");
        material.renderQueue = (int)RenderQueue.Geometry;
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
        TryValidateHdrpMaterial(material);
        return true;
    }

    private static void NeutralizeBodyPaintTexture(Material material)
    {
        SetTextureToNull(material, "_BaseColorMap");
        SetTextureToNull(material, "_MainTex");
        SetTextureToNull(material, "baseColorTexture");
        SetColor(material, "_BaseColor", Color.white);
        SetColor(material, "_Color", Color.white);
        SetColor(material, "baseColorFactor", Color.white);
    }

    private static void SetTextureToNull(Material material, string property)
    {
        if (material.HasProperty(property))
            material.SetTexture(property, null);
    }

    internal static void PrepareTransparentMaterial(
        Material material,
        KiaCeedSportswagonTransparentRole role = KiaCeedSportswagonTransparentRole.OtherClear)
    {
        var cabinGlass = role == KiaCeedSportswagonTransparentRole.CabinGlass;
        var rearPrivacyGlass = role == KiaCeedSportswagonTransparentRole.RearPrivacyGlass;
        var smokedGlass = cabinGlass || rearPrivacyGlass ||
                          role == KiaCeedSportswagonTransparentRole.PanoramicRoofGlass;
        // Imported glTF transparency is not reliable in the game's HDRP build.
        // Exterior clear-surface variants use deterministic per-instance Lit
        // states. Authored gauges, symbols, and screens never enter this path.
        RebindToHdrpLit(material);
        var tint = role == KiaCeedSportswagonTransparentRole.PanoramicRoofGlass
            ? new Color(0.035f, 0.05f, 0.07f, 0.34f)
            : cabinGlass
                ? new Color(0.09f, 0.12f, 0.15f, 0.14f)
            : rearPrivacyGlass
                ? new Color(0.02f, 0.028f, 0.04f, 0.63f)
            : role == KiaCeedSportswagonTransparentRole.HeadlampLens
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
        if (smokedGlass)
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
        PrepareTransparentMaterial(material, KiaCeedSportswagonTransparentRole.CabinGlass);
    }

    internal static KiaCeedSportswagonTransparentRole GetTransparentRole(
        Renderer renderer,
        Material material)
    {
        if (!IsTransparentMaterial(material))
            return KiaCeedSportswagonTransparentRole.Authored;

        var rendererName = renderer.name;
        if (rendererName.IndexOf("headlightglass", StringComparison.OrdinalIgnoreCase) >= 0)
            return KiaCeedSportswagonTransparentRole.HeadlampLens;
        if (rendererName.IndexOf("sunroof", StringComparison.OrdinalIgnoreCase) >= 0 &&
            material.name.IndexOf("glass_clear", StringComparison.OrdinalIgnoreCase) >= 0)
            return KiaCeedSportswagonTransparentRole.PanoramicRoofGlass;
        if (rendererName.IndexOf("quarterglass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("backlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("rearwindow", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("rear_window", StringComparison.OrdinalIgnoreCase) >= 0 ||
            IsRearDoorGlassName(rendererName))
        {
            return KiaCeedSportswagonTransparentRole.RearPrivacyGlass;
        }
        if (rendererName.IndexOf("windshield", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("doorglass", StringComparison.OrdinalIgnoreCase) >= 0 ||
            rendererName.IndexOf("blackGlass", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return KiaCeedSportswagonTransparentRole.CabinGlass;
        }

        var name = material.name;
        return name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
            ? KiaCeedSportswagonTransparentRole.OtherClear
            : KiaCeedSportswagonTransparentRole.Authored;
    }

    private static bool IsRearDoorGlassName(string rendererName)
    {
        if (rendererName.IndexOf("doorglass", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return rendererName.IndexOf("_RL", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("_RR", StringComparison.OrdinalIgnoreCase) >= 0 ||
               rendererName.IndexOf("rear", StringComparison.OrdinalIgnoreCase) >= 0;
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
public sealed class KiaCeedSportswagonMaterialController : MonoBehaviour
{
    private readonly List<Material> ownedMaterials = new List<Material>();
    private KiaCeedSportswagonMaterialFixResult result;
    private bool initialized;

    internal KiaCeedSportswagonMaterialFixResult Initialize(ModContext? context)
    {
        if (initialized)
            return result;

        initialized = true;
        var clones = new Dictionary<Material, Dictionary<KiaCeedSportswagonTransparentRole, Material>>();
        var paintVariants = new Dictionary<Material, Material>();
        var rendererCount = 0;
        var transparentMaterials = 0;
        var opaquePaintSlots = 0;
        var authoredTransparentMaterials = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!KiaCeedSportswagonMaterials.IsKiaCeedSportswagonRenderer(renderer.transform))
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

                var role = KiaCeedSportswagonMaterials.GetTransparentRole(renderer, source);
                hasCabinGlass |= role == KiaCeedSportswagonTransparentRole.CabinGlass;
                if (role == KiaCeedSportswagonTransparentRole.Authored)
                {
                    if (KiaCeedSportswagonMaterials.IsTransparentMaterial(source))
                    {
                        authoredTransparentMaterials++;
                        continue;
                    }

                    if (!KiaCeedSportswagonMaterials.IsBodyPaintMaterial(source))
                        continue;

                    if (!paintVariants.TryGetValue(source, out var paintMaterial))
                    {
                        paintMaterial = Instantiate(source);
                        paintMaterial.name = source.name +
                                             "_KiaCeedSportswagonPaintInstance";
                        if (!KiaCeedSportswagonMaterials.PrepareBodyPaintMaterial(
                                paintMaterial))
                        {
                            context?.Logger.Warn(
                                $"KiaCeedSportswagon paint vehicle={GetInstanceID()}: " +
                                $"could not prepare body material '{source.name}' as HDRP/Lit.");
                        }
                        paintVariants.Add(source, paintMaterial);
                        ownedMaterials.Add(paintMaterial);
                    }
                    materials[index] = paintMaterial;
                    changed = true;
                    opaquePaintSlots++;
                    continue;
                }

                if (!clones.TryGetValue(source, out var roleVariants))
                {
                    roleVariants = new Dictionary<KiaCeedSportswagonTransparentRole, Material>();
                    clones.Add(source, roleVariants);
                }
                if (!roleVariants.TryGetValue(role, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_KiaCeedSportswagonInstance_" + role;
                    roleVariants.Add(role, runtimeMaterial);
                    ownedMaterials.Add(runtimeMaterial);

                    if (role != KiaCeedSportswagonTransparentRole.Authored)
                    {
                        KiaCeedSportswagonMaterials.PrepareTransparentMaterial(runtimeMaterial, role);
                        transparentMaterials++;
                    }
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

        result = new KiaCeedSportswagonMaterialFixResult(
            rendererCount,
            0,
            opaquePaintSlots,
            transparentMaterials,
            0,
            0,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
        KiaCeedSportswagonDiagnostics.Info(
            context,
            $"KiaCeedSportswagon materials vehicle={GetInstanceID()}: instanced " +
            $"{ownedMaterials.Count} material variants for {rendererCount} renderers, " +
            $"bodyPaintSlots={opaquePaintSlots}, " +
            $"clearSurfaceVariants={transparentMaterials}, " +
            $"authoredTransparentShared={authoredTransparentMaterials}, " +
            $"cabinGlass={cabinGlassRenderers}, " +
            "body paint uses per-vehicle HDRP/Lit (.08 metallic, .86 smoothness); " +
            "factory GT-Line wheel finish retained.");
        return result;
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
public sealed class KiaCeedSportswagonPaintController : MonoBehaviour
{
    internal const string SupplementalColorRestoredEvent = "vehicle-repainter:color-restored";
    private const string BodyMaterialMarker = "carPaint";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly List<Material> ownedPaintMaterials = new List<Material>();
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
                $"KiaCeedSportswagon paint vehicle={vehicle?.GetInstanceID()}: no vehicle color " +
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

        var block = properties ??= new MaterialPropertyBlock();
        var color = (Color)tint;
        color.a = 1f;
        var bodyMaterialsTinted = 0;
        foreach (var slot in slots)
        {
            var slotColor = slot.Category == PaintCategory.InteriorAccent
                ? Color.Lerp(color, Color.white, 0.12f)
                : color;
            slotColor.a = 1f;

            if (slot.Category == PaintCategory.Body)
            {
                // Traffic clones can inherit references from one shared AI
                // prefab, so the prefab-time material clone alone does not
                // isolate paint between live NPC vehicles.
                SetTint(slot.Material, slotColor);
                bodyMaterialsTinted++;
            }

            // Color-only overrides replace stale native tints while preserving
            // independent repainter smoothness/finish overrides on this slot.
            block.Clear();
            slot.Renderer.GetPropertyBlock(block, slot.MaterialIndex);
            SetTint(block, slot.Material, slotColor);
            slot.Renderer.SetPropertyBlock(block, slot.MaterialIndex);
        }

        appliedColorName = colorName;
        appliedTint = tint;
        hasAppliedTint = true;
        KiaCeedSportswagonDiagnostics.PaintInfo(
            context,
            $"KiaCeedSportswagon paint vehicle={vehicle?.GetInstanceID()}: applied " +
            $"color='{colorName}' rgba={tint} to {slots.Count} body/interior slots " +
            $"bodyPerVehicleMaterialsTinted={bodyMaterialsTinted}; " +
            $"source='{source}', saved='{vehicle?.vehicleInstance?.vehicleColorName}', " +
            $"live='{vehicle?.CarFeatures?.VehicleColor?.name}', preferSaved={preferSavedColor}, " +
            $"awaitingSaved={awaitingSavedColor}, force={force}.");
    }

    private static void SetTint(Material material, Color color)
    {
        if (material.HasProperty(BaseColor))
            material.SetColor(BaseColor, color);
        if (material.HasProperty(ColorProperty))
            material.SetColor(ColorProperty, color);
        if (material.HasProperty(BaseColorFactor))
            material.SetColor(BaseColorFactor, color);
    }

    private static void SetTint(
        MaterialPropertyBlock propertyBlock,
        Material material,
        Color color)
    {
        if (material.HasProperty(BaseColor))
            propertyBlock.SetColor(BaseColor, color);
        if (material.HasProperty(ColorProperty))
            propertyBlock.SetColor(ColorProperty, color);
        if (material.HasProperty(BaseColorFactor))
            propertyBlock.SetColor(BaseColorFactor, color);
    }

    internal void LogFinishPreview(string source)
    {
        if (!KiaCeedSportswagonDiagnostics.PaintDiagnosticsEnabled)
            return;

        var bodySlots = 0;
        var tintOverrides = 0;
        var smoothnessOverrides = 0;
        var rendererSmoothnessOverrides = 0;
        var values = new List<string>();
        var propertyBlock = new MaterialPropertyBlock();
        var rendererPropertyBlock = new MaterialPropertyBlock();
        foreach (var slot in slots)
        {
            if (slot.Category != PaintCategory.Body || slot.Renderer == null || slot.Material == null)
                continue;

            bodySlots++;
            propertyBlock.Clear();
            slot.Renderer.GetPropertyBlock(propertyBlock, slot.MaterialIndex);
            rendererPropertyBlock.Clear();
            slot.Renderer.GetPropertyBlock(rendererPropertyBlock);
            if (propertyBlock.HasColor(BaseColor) || propertyBlock.HasColor(ColorProperty) ||
                propertyBlock.HasColor(BaseColorFactor))
            {
                tintOverrides++;
            }

            if (!slot.Material.HasProperty(Smoothness))
                continue;
            var hasSlotSmoothness = propertyBlock.HasFloat(Smoothness);
            var hasRendererSmoothness = rendererPropertyBlock.HasFloat(Smoothness);
            if (hasSlotSmoothness)
                smoothnessOverrides++;
            if (hasRendererSmoothness)
                rendererSmoothnessOverrides++;
            if (values.Count < 12)
            {
                var value = hasSlotSmoothness
                    ? propertyBlock.GetFloat(Smoothness)
                    : hasRendererSmoothness
                        ? rendererPropertyBlock.GetFloat(Smoothness)
                        : slot.Material.GetFloat(Smoothness);
                values.Add($"{slot.Material.name}:{value:0.000}" +
                           (hasSlotSmoothness
                               ? "(slot)"
                               : hasRendererSmoothness ? "(renderer)" : "(material)"));
            }
        }

        KiaCeedSportswagonDiagnostics.PaintInfo(
            context,
            $"KiaCeedSportswagon paint vehicle={vehicle?.GetInstanceID()}: " +
            $"finish preview source='{source}' bodySlots={bodySlots} " +
            $"tintOverrides={tintOverrides} smoothnessOverrides={smoothnessOverrides} " +
            $"rendererSmoothnessOverrides={rendererSmoothnessOverrides} " +
            $"smoothness=[{string.Join(",", values)}].");
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        var paintVariants = new Dictionary<Material, Material>();
        var bodySlots = 0;
        var interiorAccentSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!KiaCeedSportswagonMaterials.IsKiaCeedSportswagonRenderer(renderer.transform))
                continue;
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null && material.name.IndexOf(
                        BodyMaterialMarker,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (!paintVariants.TryGetValue(material, out var instanceMaterial))
                    {
                        instanceMaterial = Instantiate(material);
                        instanceMaterial.name = material.name +
                                                "_KiaCeedSportswagonPaintVehicle";
                        paintVariants.Add(material, instanceMaterial);
                        ownedPaintMaterials.Add(instanceMaterial);
                    }

                    materials[index] = instanceMaterial;
                    changed = true;
                    slots.Add(new PaintSlot(
                        renderer,
                        instanceMaterial,
                        index,
                        PaintCategory.Body));
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

            if (changed)
                renderer.sharedMaterials = materials;
        }

        KiaCeedSportswagonDiagnostics.PaintInfo(
            context,
            $"KiaCeedSportswagon paint vehicle={vehicle?.GetInstanceID()}: mapped " +
            $"bodySlots={bodySlots}, interiorAccentSlots={interiorAccentSlots}; " +
            $"instancePaintMaterials={ownedPaintMaterials.Count}, " +
            "bodyColor=per-vehicle-material, finishValues=preserved, " +
            "bodyMetallic=0.08, baseRoughness=0.22; " +
            "glass, lamps, unmapped carbon, rims, brakes, and black trim excluded.");
        if (bodySlots == 0)
            context?.Logger.Warn(
                $"KiaCeedSportswagon paint mapping incomplete bodySlots={bodySlots}, " +
                $"interiorAccentSlots={interiorAccentSlots}.");
    }

    private void OnDestroy()
    {
        UnsubscribeAppearanceEvents();
        foreach (var material in ownedPaintMaterials)
            if (material != null)
                Destroy(material);
        ownedPaintMaterials.Clear();
    }

    private static bool IsInteriorAccent(Renderer renderer, Material material)
    {
        var materialName = material.name;
        return materialName.IndexOf("stitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
               materialName.IndexOf("seat_leather_2", StringComparison.OrdinalIgnoreCase) >= 0 ||
               materialName.IndexOf("B60000", StringComparison.OrdinalIgnoreCase) >= 0 ||
               materialName.IndexOf("gtlinered", StringComparison.OrdinalIgnoreCase) >= 0 ||
               ((HasAncestor(renderer.transform, "seat_") ||
                 HasAncestor(renderer.transform, "seats_R")) &&
                materialName.IndexOf("_red", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
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

    private VehicleColor? ResolveVehicleColor(bool preferSavedColor)
    {
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
                return live;
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

