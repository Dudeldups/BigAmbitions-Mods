#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

internal sealed class BugattiChironPaintController : MonoBehaviour
{
    private const string BodyMaterialMarker = "BugattiOpaque_04_Body";
    private const string RimMaterialMarker = "BugattiOpaque_01_Rims";
    private const string DarkBodyMaterialMarker = "BugattiOpaque_06_Darker_Parts";
    private const string InteriorSecondaryMaterialMarker = "BugattiOpaque_08_Interior_2";
    private const string InteriorDarkMaterialMarker = "BugattiOpaque_12_Interior_1_Darker";
    private const string InteriorPrimaryMaterialMarker = "BugattiOpaque_13_Interior_1";
    private const string CaliperMaterialMarker = "BugattiOpaque_02_Caliper";
    private const string RimInnerMaterialMarker = "BugattiOpaque_03_Brake_rotor";
    private const string SeatMaterialMarker = "BugattiOpaque_19_seats";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    private static readonly int BaseColorMap = Shader.PropertyToID("_BaseColorMap");
    private static readonly int MainTexture = Shader.PropertyToID("_MainTex");
    private static readonly int BaseColorTexture = Shader.PropertyToID("baseColorTexture");
    private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
    private static readonly int Metallic = Shader.PropertyToID("_Metallic");

    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private VehicleController? vehicle;
    private string? explicitVehicleColorName;
    private VehicleColor? explicitVehicleColor;
    private ModContext? context;
    private VehicleColor? appliedVehicleColor;
    private Color32 appliedTint;
    private bool hasAppliedTint;
    private Texture2D? rimSourceTexture;
    private Texture2D? rimInnerSourceTexture;
    private Texture2D? seatSourceTexture;
    private Texture2D? rimPaintTexture;
    private Texture2D? rimInnerPaintTexture;
    private Texture2D? seatPaintTexture;
    private bool exteriorOnly;
    private bool textureFailureLogged;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        explicitVehicleColorName = null;
        explicitVehicleColor = null;
        exteriorOnly = false;
        context = modContext;
        FindPaintSlots();
        ApplyCurrentColor();
    }

    internal void RefreshCurrentColor() => ApplyCurrentColor();

    internal void InitializeForPrivateDriver(
        string? vehicleColorName,
        VehicleColor? vehicleColor)
    {
        vehicle = null;
        context = null;
        explicitVehicleColorName = vehicleColorName;
        explicitVehicleColor = vehicleColor;
        exteriorOnly = false;
        appliedVehicleColor = null;
        hasAppliedTint = false;
        FindPaintSlots();
        ApplyCurrentColor();
    }

    internal void InitializeForAmbientTraffic(VehicleColor vehicleColor)
    {
        vehicle = null;
        context = null;
        explicitVehicleColorName = null;
        explicitVehicleColor = vehicleColor;
        exteriorOnly = true;
        appliedVehicleColor = null;
        hasAppliedTint = false;
        FindPaintSlots();
        ApplyCurrentColor();
    }

    internal bool HasAppliedColor => hasAppliedTint;

    private void FindPaintSlots()
    {
        slots.Clear();
        var bodySlots = 0;
        var darkBodySlots = 0;
        var rimSlots = 0;
        var interiorSlots = 0;
        var caliperSlots = 0;
        var rimInnerSlots = 0;
        var seatSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;
                var category = GetCategory(material);
                if (category == PaintCategory.None)
                    continue;
                slots.Add(new PaintSlot(renderer, material, index, category));
                if (category == PaintCategory.MainBody) bodySlots++;
                if (category == PaintCategory.DarkBody) darkBodySlots++;
                if (category == PaintCategory.Rim) rimSlots++;
                if (category == PaintCategory.Caliper) caliperSlots++;
                if (category == PaintCategory.RimInner) rimInnerSlots++;
                if (category == PaintCategory.Seat) seatSlots++;
                if (category == PaintCategory.InteriorPrimary ||
                    category == PaintCategory.InteriorSecondary ||
                    category == PaintCategory.InteriorDark)
                    interiorSlots++;
                if (category == PaintCategory.Rim && rimSourceTexture == null)
                    rimSourceTexture = FindBaseTexture(material);
                if (category == PaintCategory.RimInner && rimInnerSourceTexture == null)
                    rimInnerSourceTexture = FindBaseTexture(material);
                if (category == PaintCategory.Seat && seatSourceTexture == null)
                    seatSourceTexture = FindBaseTexture(material);
            }
        }

        if (bodySlots == 0 || darkBodySlots == 0 || rimSlots == 0 ||
            rimInnerSlots == 0 || caliperSlots == 0 || seatSlots == 0 || interiorSlots == 0)
            context?.Logger.Warn(
                $"BugattiChiron paint vehicle={vehicle?.GetInstanceID()}: " +
                $"paint mapping incomplete bodySlots={bodySlots}, darkBodySlots={darkBodySlots}, " +
                $"rimSlots={rimSlots}, rimInnerSlots={rimInnerSlots}, " +
                $"caliperSlots={caliperSlots}, seatSlots={seatSlots}, " +
                $"interiorSlots={interiorSlots}.");
    }

    private void ApplyCurrentColor()
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
            return;
        var tint = (Color32)selected.tint;
        if (hasAppliedTint && ReferenceEquals(selected, appliedVehicleColor) && tint.Equals(appliedTint))
            return;

        var selectedColor = (Color)tint;
        selectedColor.a = 1f;
        var useDarkPaintCompensation = IsDarkSaturated(selectedColor);
        var bodyColor = CreateBodyColor(selectedColor);
        if (exteriorOnly)
            DestroyPaintTextures();
        else
            RebuildPaintTextures(tint, selectedColor, bodyColor);
        foreach (var slot in slots)
        {
            if (exteriorOnly &&
                slot.Category != PaintCategory.MainBody &&
                slot.Category != PaintCategory.DarkBody)
            {
                // Pooled traffic cars only need the visible two-tone body paint.
                // Clearing the remaining overrides also removes any detailed
                // chauffeur paint left on a reused pool object without rebuilding
                // rim or seat textures on the main thread.
                properties.Clear();
                slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
                continue;
            }

            var color = ColorForCategory(
                slot.Category,
                selectedColor,
                bodyColor,
                useDarkPaintCompensation);
            if (slot.Category == PaintCategory.Seat && seatPaintTexture == null)
                color = bodyColor;
            if (slot.Category == PaintCategory.Rim && rimPaintTexture == null)
                color = selectedColor;
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor)) properties.SetColor(BaseColor, color);
            if (slot.Material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, color);
            if (slot.Material.HasProperty(BaseColorFactor))
                properties.SetColor(BaseColorFactor, color);
            if (slot.Category == PaintCategory.MainBody || slot.Category == PaintCategory.DarkBody)
            {
                // The imported HDRP/Lit paint lacks the game's colored Fresnel layer.
                // Keep dark hues visible by reducing the mirror-like neutral reflection
                // and allowing a modest metallic contribution to tint that reflection.
                if (slot.Material.HasProperty(Smoothness))
                    properties.SetFloat(
                        Smoothness,
                        useDarkPaintCompensation
                            ? 0.65f
                            : slot.Material.GetFloat(Smoothness));
                if (slot.Material.HasProperty(Metallic))
                    properties.SetFloat(
                        Metallic,
                        useDarkPaintCompensation
                            ? slot.Category == PaintCategory.DarkBody
                                ? Mathf.Max(0.55f, slot.Material.GetFloat(Metallic))
                                : 0.35f
                            : slot.Material.GetFloat(Metallic));
            }
            if (slot.Category == PaintCategory.Rim)
            {
                // One source material contains both blue-painted recesses and
                // polished faces. The generated texture preserves that split;
                // white material tint keeps the chrome areas neutral.
                if (slot.Material.HasProperty(Smoothness))
                    properties.SetFloat(Smoothness, 0.86f);
                if (slot.Material.HasProperty(Metallic))
                    properties.SetFloat(Metallic, 1f);
            }
            var texture = slot.Category == PaintCategory.Rim
                ? rimPaintTexture
                : slot.Category == PaintCategory.RimInner
                    ? rimInnerPaintTexture
                    : slot.Category == PaintCategory.Seat
                        ? seatPaintTexture
                        : null;
            if (texture != null)
            {
                if (slot.Material.HasProperty(BaseColorMap)) properties.SetTexture(BaseColorMap, texture);
                if (slot.Material.HasProperty(MainTexture)) properties.SetTexture(MainTexture, texture);
                if (slot.Material.HasProperty(BaseColorTexture))
                    properties.SetTexture(BaseColorTexture, texture);
            }
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        appliedVehicleColor = selected;
        appliedTint = tint;
        hasAppliedTint = true;
    }

    private static PaintCategory GetCategory(Material material)
    {
        var name = material.name;
        if (name.IndexOf(BodyMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.MainBody;
        if (name.IndexOf(DarkBodyMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.DarkBody;
        if (name.IndexOf(RimMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.Rim;
        if (name.IndexOf(CaliperMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.Caliper;
        if (name.IndexOf(RimInnerMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.RimInner;
        if (name.IndexOf(SeatMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.Seat;
        if (name.IndexOf(InteriorDarkMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.InteriorDark;
        if (name.IndexOf(InteriorPrimaryMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.InteriorPrimary;
        if (name.IndexOf(InteriorSecondaryMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            return PaintCategory.InteriorSecondary;
        return PaintCategory.None;
    }

    private static Color ColorForCategory(
        PaintCategory category,
        Color selectedColor,
        Color bodyColor,
        bool useDarkPaintCompensation)
    {
        switch (category)
        {
            case PaintCategory.MainBody:
                return bodyColor;
            case PaintCategory.DarkBody:
                return Scale(bodyColor, useDarkPaintCompensation ? 0.55f : 0.35f);
            case PaintCategory.Rim:
                return Color.white;
            case PaintCategory.Caliper:
                return bodyColor;
            case PaintCategory.RimInner:
                return Scale(bodyColor, 0.35f);
            case PaintCategory.Seat:
                return Color.white;
            case PaintCategory.InteriorPrimary:
                return Color.Lerp(bodyColor, Color.white, 0.12f);
            case PaintCategory.InteriorSecondary:
                return Scale(bodyColor, 0.55f);
            case PaintCategory.InteriorDark:
                return Scale(bodyColor, 0.28f);
            default:
                return bodyColor;
        }
    }

    private static Color CreateBodyColor(Color selectedColor)
    {
        var linear = selectedColor.linear;
        linear.a = 1f;

        // The imported HDRP material has no vanilla vehicle-paint Fresnel pass.
        // Dark saturated colors need their palette value as the base color or
        // neutral environment reflections overwhelm their hue. Bright and neutral
        // colors retain the proven linear response.
        if (!IsDarkSaturated(selectedColor))
            return linear;
        return selectedColor;
    }

    private static bool IsDarkSaturated(Color color)
    {
        var brightest = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        var darkest = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        var saturation = brightest > 0.001f ? (brightest - darkest) / brightest : 0f;
        return brightest < 0.45f && saturation > 0.20f;
    }

    private static Color Scale(Color color, float factor) =>
        new Color(color.r * factor, color.g * factor, color.b * factor, 1f);

    private void RebuildPaintTextures(Color32 tint, Color selectedColor, Color bodyColor)
    {
        DestroyPaintTextures();
        try
        {
            if (rimSourceTexture != null)
                rimPaintTexture = CreateRimPaintTexture(rimSourceTexture, selectedColor);
            if (rimInnerSourceTexture != null)
                rimInnerPaintTexture = CreateNeutralPaintTexture(
                    rimInnerSourceTexture,
                    "BugattiChiron_RimInnerPaint");
            if (seatSourceTexture != null)
                seatPaintTexture = CreateSeatPaintTexture(seatSourceTexture, bodyColor);
            textureFailureLogged = false;
        }
        catch (Exception exception)
        {
            DestroyPaintTextures();
            if (!textureFailureLogged)
            {
                textureFailureLogged = true;
                context?.Logger.Warn(
                    $"BugattiChiron paint vehicle={vehicle?.GetInstanceID()}: textured paint mapping " +
                    $"failed for rgba={tint}; using solid tint fallback: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private static Texture2D CreateRimPaintTexture(Texture2D source, Color selectedColor)
    {
        var pixels = source.GetPixels32();
        var darkPaint = Color.Lerp(Color.black, selectedColor, 0.46f);
        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];
            var brightest = Math.Max(pixel.r, Math.Max(pixel.g, pixel.b));
            var darkest = Math.Min(pixel.r, Math.Min(pixel.g, pixel.b));
            var saturation = brightest > 0
                ? (brightest - darkest) / (float)brightest
                : 0f;
            var authoredBluePaint = saturation >= 0.18f &&
                                    pixel.b > pixel.r * 1.08f &&
                                    pixel.b > pixel.g * 1.04f;
            if (!authoredBluePaint)
                continue;

            var sourceShade = Mathf.InverseLerp(30f, 180f, brightest);
            var shade = Mathf.Lerp(0.58f, 1.08f, sourceShade);
            pixels[index] = (Color32)new Color(
                darkPaint.r * shade,
                darkPaint.g * shade,
                darkPaint.b * shade,
                pixel.a / 255f);
        }

        return CreateRuntimeTexture(source, pixels, "BugattiChiron_RimPaint");
    }

    private static Texture2D CreateNeutralPaintTexture(Texture2D source, string name)
    {
        var pixels = source.GetPixels32();
        for (var index = 0; index < pixels.Length; index++)
        {
            var pixel = pixels[index];
            var value = (byte)Math.Max(pixel.r, Math.Max(pixel.g, pixel.b));
            pixels[index] = new Color32(value, value, value, pixel.a);
        }
        return CreateRuntimeTexture(source, pixels, name);
    }

    private static Texture2D CreateSeatPaintTexture(Texture2D source, Color mainColor)
    {
        var pixels = source.GetPixels32();
        const float brightSeatLinear = 0.90f;
        for (var index = 0; index < pixels.Length; index++)
        {
            var sourcePixel = pixels[index];
            var brightest = Math.Max(sourcePixel.r, Math.Max(sourcePixel.g, sourcePixel.b)) / 255f;
            var shade = Mathf.Clamp01(Mathf.GammaToLinearSpace(brightest) / brightSeatLinear);
            var output = new Color(
                mainColor.r * shade,
                mainColor.g * shade,
                mainColor.b * shade,
                sourcePixel.a / 255f).gamma;
            output.a = sourcePixel.a / 255f;
            pixels[index] = (Color32)output;
        }

        return CreateRuntimeTexture(source, pixels, "BugattiChiron_SeatPaint");
    }

    private static Texture2D CreateRuntimeTexture(Texture2D source, Color32[] pixels, string name)
    {
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, false)
        {
            name = name,
            hideFlags = HideFlags.DontSave,
            filterMode = source.filterMode,
            wrapModeU = source.wrapModeU,
            wrapModeV = source.wrapModeV,
            anisoLevel = source.anisoLevel,
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    private static Texture2D? FindBaseTexture(Material material)
    {
        if (material.HasProperty(BaseColorMap) && material.GetTexture(BaseColorMap) is Texture2D hdrp)
            return hdrp;
        if (material.HasProperty(MainTexture) && material.GetTexture(MainTexture) is Texture2D legacy)
            return legacy;
        return material.HasProperty(BaseColorTexture)
            ? material.GetTexture(BaseColorTexture) as Texture2D
            : null;
    }

    private void DestroyPaintTextures()
    {
        if (rimPaintTexture != null)
            Destroy(rimPaintTexture);
        if (rimInnerPaintTexture != null)
            Destroy(rimInnerPaintTexture);
        if (seatPaintTexture != null)
            Destroy(seatPaintTexture);
        rimPaintTexture = null;
        rimInnerPaintTexture = null;
        seatPaintTexture = null;
    }

    private void OnDestroy() => DestroyPaintTextures();

    private VehicleColor? ResolveVehicleColor()
    {
        if (!string.IsNullOrEmpty(explicitVehicleColorName) &&
            VehicleHelper.TryGetVehicleColor(explicitVehicleColorName, out var explicitColor))
        {
            return explicitColor;
        }

        if (explicitVehicleColor != null)
            return explicitVehicleColor;

        var live = vehicle?.CarFeatures?.VehicleColor;
        if (live != null)
            return live;
        var colorName = vehicle?.vehicleInstance?.vehicleColorName;
        return !string.IsNullOrEmpty(colorName) && VehicleHelper.TryGetVehicleColor(colorName, out var saved)
            ? saved
            : null;
    }

    private readonly struct PaintSlot
    {
        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
        internal readonly PaintCategory Category;

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
    }

    private enum PaintCategory
    {
        None,
        MainBody,
        DarkBody,
        Rim,
        RimInner,
        Caliper,
        Seat,
        InteriorPrimary,
        InteriorSecondary,
        InteriorDark,
    }
}
