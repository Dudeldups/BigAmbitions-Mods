#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

[AddComponentMenu("")]
public sealed class CadillacEscaladePaintController : MonoBehaviour
{
    private const int PaintSettlementAttempts = 20;
    private const float PaintSettlementDelay = 0.10f;
    private const string BodyMaterialMarker = "CadillacBodyPaint";
    private const string CaliperMaterialMarker = "CadillacCaliper";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private readonly List<Material> ownedMaterials = new List<Material>();
    private MaterialPropertyBlock properties = null!;
    private VehicleController? vehicle;
    private ModContext? context;
    private string? explicitVehicleColorName;
    private VehicleColor? explicitVehicleColor;
    private Coroutine? settlementCoroutine;
    private VehicleColor? appliedColor;
    private Color32 appliedTint;
    private bool hasAppliedTint;

    private void Awake()
    {
        properties = new MaterialPropertyBlock();
        var controller = GetComponent<VehicleController>();
        if (controller == null)
            return;

        CadillacEscaladeMaterials.FixSolidMaterials(controller.gameObject);
        Initialize(controller, null);

        var glassController = controller.GetComponent<CadillacEscaladeGlassController>();
        if (glassController == null)
            glassController = controller.gameObject.AddComponent<CadillacEscaladeGlassController>();
        glassController.Initialize(null);
    }

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (properties == null)
            properties = new MaterialPropertyBlock();
        vehicle = controller;
        context = modContext;
        explicitVehicleColorName = null;
        explicitVehicleColor = null;
        FindPaintSlots();
        ApplyCurrentColor();
        SchedulePaintSettlement();
    }

    internal void RestoreAfterVehicleEntered()
    {
        SchedulePaintSettlement();
    }

    internal void RefreshCurrentColor() => ApplyCurrentColor(true);

    internal void InitializeForPrivateDriver(
        string? vehicleColorName,
        VehicleColor? vehicleColor)
    {
        if (properties == null)
            properties = new MaterialPropertyBlock();
        vehicle = null;
        context = null;
        explicitVehicleColorName = vehicleColorName;
        explicitVehicleColor = vehicleColor;
        appliedColor = null;
        hasAppliedTint = false;
        FindPaintSlots();
        ApplyCurrentColor(true);
    }

    internal bool HasAppliedColor => hasAppliedTint;

    private void SchedulePaintSettlement()
    {
        if (settlementCoroutine != null)
            StopCoroutine(settlementCoroutine);
        settlementCoroutine = StartCoroutine(SettlePaintAfterLifecycle());
    }

    private IEnumerator SettlePaintAfterLifecycle()
    {
        // Dealer previews and entered vehicles can receive their selected color
        // after the controller is created. Retry only across that bounded spawn
        // transition, then stop; no permanent paint-repair polling is needed.
        yield return null;
        yield return new WaitForEndOfFrame();
        for (var attempt = 1; attempt <= PaintSettlementAttempts; attempt++)
        {
            if (ApplyCurrentColor(true))
                break;
            if (attempt < PaintSettlementAttempts)
                yield return new WaitForSecondsRealtime(PaintSettlementDelay);
        }
        settlementCoroutine = null;
    }

    private void FindPaintSlots()
    {
        ReleaseOwnedMaterials();
        slots.Clear();
        var bodySlots = 0;
        var caliperSlots = 0;
        var interiorAccentSlots = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;
                PaintCategory? category = null;
                if (material.name.IndexOf(BodyMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    category = PaintCategory.Body;
                    bodySlots++;
                }
                else if (material.name.IndexOf(CaliperMaterialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    category = PaintCategory.Caliper;
                    caliperSlots++;
                }

                if (!category.HasValue)
                    continue;

                // The game can replace or clear renderer property blocks after a
                // vehicle is spawned. Give every mutable slot an instance-owned
                // material so paint is stable without leaking to other vehicles.
                var instanceMaterial = new Material(material)
                {
                    name = material.name + "_VehiclePaint_" + GetInstanceID()
                };
                ownedMaterials.Add(instanceMaterial);
                materials[index] = instanceMaterial;
                renderer.sharedMaterials = materials;
                renderer.SetPropertyBlock(null, index);
                slots.Add(new PaintSlot(renderer, instanceMaterial, index, category.Value));
            }
        }

        CadillacEscaladeDiagnostics.Info(context,
            $"CadillacEscalade paint vehicle={vehicle?.GetInstanceID()}: " +
            $"mapped bodySlots={bodySlots}, caliperSlots={caliperSlots}, " +
            $"interiorAccentSlots={interiorAccentSlots}; " +
            "rims, tires, carbon, black trim, and glass remain factory materials.");
        if (bodySlots == 0 || caliperSlots != 4)
            context?.Logger.Warn(
                $"CadillacEscalade paint mapping incomplete bodySlots={bodySlots}, " +
                $"caliperSlots={caliperSlots}, interiorAccentSlots={interiorAccentSlots}.");
    }

    private bool ApplyCurrentColor(bool force = false)
    {
        var selected = ResolveVehicleColor();
        if (selected == null)
            return false;
        var tint = (Color32)selected.tint;
        if (!force && hasAppliedTint && ReferenceEquals(selected, appliedColor) && tint.Equals(appliedTint))
            return true;

        var selectedColor = (Color)tint;
        selectedColor.a = 1f;
        foreach (var slot in slots)
        {
            // Both replacement-model body shells and the four generated calipers
            // are color linked; chrome, tires, glass, lamps, and interior remain
            // factory materials.
            var color = slot.Category == PaintCategory.InteriorAccent
                ? Color.Lerp(selectedColor, Color.white, 0.12f)
                : selectedColor;
            color.a = 1f;
            if (slot.Material.HasProperty(BaseColor)) slot.Material.SetColor(BaseColor, color);
            if (slot.Material.HasProperty(ColorProperty)) slot.Material.SetColor(ColorProperty, color);
            if (slot.Material.HasProperty(BaseColorFactor)) slot.Material.SetColor(BaseColorFactor, color);
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if (slot.Material.HasProperty(BaseColor)) properties.SetColor(BaseColor, color);
            if (slot.Material.HasProperty(ColorProperty)) properties.SetColor(ColorProperty, color);
            if (slot.Material.HasProperty(BaseColorFactor)) properties.SetColor(BaseColorFactor, color);
            slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
        }

        appliedColor = selected;
        appliedTint = tint;
        hasAppliedTint = true;
        CadillacEscaladeDiagnostics.Info(context,
            $"CadillacEscalade paint vehicle={vehicle?.GetInstanceID()}: " +
            $"applied color='{((UnityEngine.Object)selected).name}' rgba={tint} " +
            $"to {slots.Count} instance-owned body/caliper slots.");
        return true;
    }

    private void OnDestroy()
    {
        if (settlementCoroutine != null)
            StopCoroutine(settlementCoroutine);
        settlementCoroutine = null;
        ReleaseOwnedMaterials();
    }

    private void ReleaseOwnedMaterials()
    {
        foreach (var material in ownedMaterials)
        {
            if (material != null)
                Destroy(material);
        }
        ownedMaterials.Clear();
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
