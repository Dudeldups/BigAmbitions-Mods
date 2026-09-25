#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

[AddComponentMenu("")]
internal sealed class BMWM4G82PaintController : MonoBehaviour
{
    private const string MainPaintMarker = "PaintTNR";
    private const string InteriorMaterialMarker = "InteriorA";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");

    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly List<PaintSlot> caliperSlots = new List<PaintSlot>();
    private readonly List<InteriorSlot> interiorSlots = new List<InteriorSlot>();
    private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
    private Texture2D? interiorSourceTexture;
    private Color32[]? interiorSourcePixels;
    private Texture2D? paintedInteriorTexture;
    private VehicleController? vehicle;
    private ModContext? context;
    private string? explicitVehicleColorName;
    private VehicleColor? explicitVehicleColor;
    private VehicleColor? appliedColor;
    private Color32 appliedTint;
    private bool hasAppliedTint;
    private bool initialized;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (!initialized || vehicle != controller)
        {
            vehicle = controller;
            context = modContext;
            explicitVehicleColorName = null;
            explicitVehicleColor = null;
            FindPaintSlots();
            initialized = true;
        }

        ApplyCurrentColor("vehicle-configured", true);
    }

    internal bool HasAppliedColor => hasAppliedTint && slots.Count > 0;

    internal void InitializeForPrivateDriver(
        string? colorName,
        VehicleColor? resolvedColor)
    {
        vehicle = null;
        context = null;
        explicitVehicleColorName = colorName;
        explicitVehicleColor = resolvedColor;
        appliedColor = null;
        hasAppliedTint = false;
        FindPaintSlots();
        initialized = true;
        ApplyCurrentColor("private-driver", true);
    }

    internal bool ApplyCurrentColor(string source, bool force = false)
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
            return false;

        var tint = (Color32)selected.tint;
        if (!force && hasAppliedTint && ReferenceEquals(selected, appliedColor) && tint.Equals(appliedTint))
            return true;

        var color = (Color)tint;
        color.a = 1f;
        foreach (var slot in slots)
        {
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor)) properties.SetColor(BaseColor, color);
            if (slot.Material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, color);
            if (slot.Material.HasProperty(BaseColorFactor)) properties.SetColor(BaseColorFactor, color);
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        // The original Sketchfab calipers use their own material rather than
        // PaintTNR, so they must explicitly follow the configured vehicle
        // color. Preserve the metallic/smoothness finish already installed by
        // BMWM4G82Materials while replacing only its color properties.
        foreach (var slot in caliperSlots)
        {
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor)) properties.SetColor(BaseColor, color);
            if (slot.Material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, color);
            if (slot.Material.HasProperty(BaseColorFactor)) properties.SetColor(BaseColorFactor, color);
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        ApplyInteriorAccent(color);

        appliedColor = selected;
        appliedTint = tint;
        hasAppliedTint = true;
        context?.Logger.Info(
            $"BMWM4G82 paint vehicle={vehicle?.GetInstanceID()}: applied " +
            $"color='{((UnityEngine.Object)selected).name}' rgba={tint} " +
            $"bodySlots={slots.Count} caliperSlots={caliperSlots.Count} source='{source}'.");
        return slots.Count > 0;
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        caliperSlots.Clear();
        interiorSlots.Clear();
        interiorSourceTexture = null;
        interiorSourcePixels = null;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;
                if (BMWM4G82Materials.IsCaliperRenderer(renderer.transform))
                    caliperSlots.Add(new PaintSlot(renderer, material, index));
                else if (IsPaintMaterial(material.name))
                    slots.Add(new PaintSlot(renderer, material, index));
                else if (material.name.IndexOf(
                             InteriorMaterialMarker,
                             StringComparison.OrdinalIgnoreCase) >= 0)
                    PrepareInteriorAccent(renderer, material, index);
            }
        }

        if (slots.Count == 0)
            context?.Logger.Warn(
                $"BMWM4G82 paint vehicle={vehicle?.GetInstanceID()}: no body-paint slots were found.");
    }

    private void PrepareInteriorAccent(Renderer renderer, Material material, int materialIndex)
    {
        var texture = GetBaseTexture(material) as Texture2D;
        if (texture == null)
            return;
        if (interiorSourceTexture != null && interiorSourceTexture != texture)
            return;
        interiorSlots.Add(new InteriorSlot(renderer, material, materialIndex));
        if (interiorSourcePixels != null)
            return;
        try
        {
            interiorSourcePixels = ReadPixels(texture);
            interiorSourceTexture = texture;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"BMWM4G82 paint vehicle={vehicle?.GetInstanceID()}: interior accent " +
                $"texture could not be prepared: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void ApplyInteriorAccent(Color bodyColor)
    {
        if (interiorSlots.Count == 0 || interiorSourceTexture == null || interiorSourcePixels == null)
            return;

        var pixels = (Color32[])interiorSourcePixels.Clone();
        var accent = new Color(
            Mathf.Max(0.035f, bodyColor.r * 0.72f),
            Mathf.Max(0.035f, bodyColor.g * 0.72f),
            Mathf.Max(0.035f, bodyColor.b * 0.72f),
            1f);
        for (var index = 0; index < pixels.Length; index++)
        {
            var source = pixels[index];
            var red = source.r / 255f;
            var green = source.g / 255f;
            var blue = source.b / 255f;
            // The bottom strip of the authored cabin atlas is the bright
            // yellow upholstery/piping layer. Preserve the dashboard symbols,
            // M stripes and neutral upholstery while replacing only that hue.
            var yellow = Mathf.Min(red, green) - blue;
            var saturation = Mathf.Max(red, green) - Mathf.Min(red, green);
            var mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.16f, 0.38f, yellow)) *
                       (1f - Mathf.SmoothStep(0f, 1f,
                           Mathf.InverseLerp(0.20f, 0.42f, saturation)));
            if (mask <= 0f || red < 0.35f || green < 0.42f)
                continue;

            var shade = Mathf.Lerp(0.58f, 1.08f, Mathf.Max(red, green));
            var replacement = new Color(
                accent.r * shade,
                accent.g * shade,
                accent.b * shade,
                1f);
            var original = new Color(red, green, blue, 1f);
            var result = Color.Lerp(original, replacement, mask);
            pixels[index] = new Color32(
                (byte)Mathf.RoundToInt(Mathf.Clamp01(result.r) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(result.g) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(result.b) * 255f),
                source.a);
        }

        var replacementTexture = new Texture2D(
            interiorSourceTexture.width,
            interiorSourceTexture.height,
            TextureFormat.RGBA32,
            true,
            false)
        {
            name = $"BMW M4 interior accent {bodyColor.r:F2},{bodyColor.g:F2},{bodyColor.b:F2}",
            filterMode = interiorSourceTexture.filterMode,
            wrapMode = interiorSourceTexture.wrapMode,
            anisoLevel = interiorSourceTexture.anisoLevel,
        };
        replacementTexture.SetPixels32(pixels);
        replacementTexture.Apply(true, false);
        foreach (var slot in interiorSlots)
        {
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            SetBaseTexture(properties, slot.Material, replacementTexture);
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }
        if (paintedInteriorTexture != null)
            Destroy(paintedInteriorTexture);
        paintedInteriorTexture = replacementTexture;
    }

    private static Color32[] ReadPixels(Texture2D source)
    {
        if (source.isReadable)
            return source.GetPixels32();

        var previous = RenderTexture.active;
        var temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        Texture2D? readable = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readable = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                false,
                false);
            readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            readable.Apply(false, false);
            return readable.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            if (readable != null)
                Destroy(readable);
        }
    }

    private static Texture? GetBaseTexture(Material material)
    {
        foreach (var property in new[] { "_BaseColorMap", "_MainTex", "baseColorTexture" })
            if (material.HasProperty(property) && material.GetTexture(property) != null)
                return material.GetTexture(property);
        return null;
    }

    private static void SetBaseTexture(
        MaterialPropertyBlock block,
        Material material,
        Texture texture)
    {
        foreach (var property in new[] { "_BaseColorMap", "_MainTex", "baseColorTexture" })
            if (material.HasProperty(property))
                block.SetTexture(property, texture);
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
        return !string.IsNullOrEmpty(colorName) && VehicleHelper.TryGetVehicleColor(colorName, out var saved)
            ? saved
            : null;
    }

    private static bool IsPaintMaterial(string materialName) =>
        materialName.IndexOf(MainPaintMarker, StringComparison.OrdinalIgnoreCase) >= 0;

    private void OnDestroy()
    {
        if (interiorSourceTexture != null)
            foreach (var slot in interiorSlots)
            {
                properties.Clear();
                slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
                SetBaseTexture(properties, slot.Material, interiorSourceTexture);
                slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
            }
        if (paintedInteriorTexture != null)
            Destroy(paintedInteriorTexture);
        paintedInteriorTexture = null;
    }

    private readonly struct PaintSlot
    {
        internal PaintSlot(Renderer renderer, Material material, int materialIndex)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
        }

        internal readonly Renderer Renderer;
        internal readonly Material Material;
        internal readonly int MaterialIndex;
    }

    private readonly struct InteriorSlot
    {
        internal InteriorSlot(Renderer renderer, Material material, int materialIndex)
        {
            Renderer = renderer;
            Material = material;
            MaterialIndex = materialIndex;
        }

        internal Renderer Renderer { get; }
        internal Material Material { get; }
        internal int MaterialIndex { get; }
    }
}
