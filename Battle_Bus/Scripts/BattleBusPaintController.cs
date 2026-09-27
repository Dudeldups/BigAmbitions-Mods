#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class BattleBusPaintController : MonoBehaviour
{
    private const string PaintMaterialMarker = "BattleBusPaint_";
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");
    private static readonly int BaseColorFactor = Shader.PropertyToID("baseColorFactor");
    // The source blue atlas color is neutralized to gray for repainting. This
    // brighter blue compensates for that atlas value and reproduces the source.
    private static readonly Color DefaultBusBlue = new Color32(119, 194, 255, 255);

    private readonly List<PaintSlot> slots = new List<PaintSlot>();
    private MaterialPropertyBlock properties = null!;
    private VehicleController? vehicle;
    private ModContext? context;
    private VehicleColor? appliedColor;
    private Color32 appliedTint;
    private bool hasAppliedTint;
    private Texture2D? capTexture;
    private Color32[]? capOriginal;
    private Color32[]? capWorking;
    private bool initialized;
    private bool warnedNoSlots;

    private void Awake()
    {
        properties = new MaterialPropertyBlock();
        vehicle = GetComponent<VehicleController>();
        FindPaintSlots();
    }

    private void Start() => ApplyCurrentColor("vehicle-spawn", true);

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        if (!initialized || slots.Count == 0)
            FindPaintSlots();
    }

    internal bool ApplyCurrentColor(string source, bool force = false)
    {
        if (slots.Count == 0)
        {
            if (!warnedNoSlots)
            {
                warnedNoSlots = true;
                context?.Logger.Warn(
                    $"Battle Bus paint vehicle={vehicle?.GetInstanceID()}: no generated paint material slots were found.");
            }
            return false;
        }

        var selected = ResolveVehicleColor(source);
        var tint = selected != null ? (Color32)selected.tint : (Color32)DefaultBusBlue;
        if (!force && hasAppliedTint && ReferenceEquals(selected, appliedColor) && tint.Equals(appliedTint))
            return true;

        var color = (Color)tint;
        color.a = 1f;
        var updatedSlots = 0;
        foreach (var slot in slots)
        {
            properties.Clear();
            slot.Renderer.GetPropertyBlock(properties, slot.MaterialIndex);
            if(slot.Renderer.name=="BattleBusPaintBalloonCap" || slot.Renderer.name=="Object_14_OriginalColorBalloon")
            {
                var atlas=slot.Material.GetTexture("_BaseColorMap") as Texture2D;
                if(atlas!=null)
                {
                    if(capTexture==null)
                    {
                        capOriginal=atlas.GetPixels32();capWorking=new Color32[capOriginal.Length];
                        capTexture=new Texture2D(atlas.width,atlas.height,TextureFormat.RGBA32,true,false){name="BattleBusInstanceCap",filterMode=FilterMode.Bilinear};
                    }
                    for(var pixel=0;pixel<capOriginal!.Length;pixel++)
                    {
                        var c=capOriginal[pixel];
                        if(c.b>28 && c.b>c.r*1.10f && c.b>c.g*1.02f)
                        {var shade=Mathf.Max(c.r,Mathf.Max(c.g,c.b))/255f;c=(Color32)new Color(shade*color.r,shade*color.g,shade*color.b,1f);}
                        capWorking![pixel]=c;
                    }
                    capTexture.SetPixels32(capWorking!);capTexture.Apply();
                    properties.SetTexture("_BaseColorMap",capTexture);properties.SetColor(BaseColor,Color.white);
                    slot.Renderer.SetPropertyBlock(properties,slot.MaterialIndex);updatedSlots++;continue;
                }
            }
            var supportsColor = false;
            if (slot.Material.HasProperty(BaseColor))
            {
                properties.SetColor(BaseColor, color);
                supportsColor = true;
            }
            if (slot.Material.HasProperty(ColorProperty))
            {
                properties.SetColor(ColorProperty, color);
                supportsColor = true;
            }
            if (slot.Material.HasProperty(BaseColorFactor))
            {
                properties.SetColor(BaseColorFactor, color);
                supportsColor = true;
            }
            if (supportsColor)
            {
                slot.Renderer.SetPropertyBlock(properties, slot.MaterialIndex);
                updatedSlots++;
            }
            else
                context?.Logger.Warn(
                    $"Battle Bus paint vehicle={vehicle?.GetInstanceID()}: renderer='{slot.Renderer.name}' " +
                    $"material='{slot.Material.name}' has no supported base color property.");
        }

        if (updatedSlots == 0)
            return false;

        appliedColor = selected;
        appliedTint = tint;
        hasAppliedTint = true;
        var colorName = selected != null ? selected.name : "default-bus-blue";
        BattleBusDiagnostics.PaintInfo(context,
            $"Battle Bus paint vehicle={vehicle?.GetInstanceID()}: applied color='{colorName}' " +
            $"rgba={tint}, materialSlots={updatedSlots}, source='{source}'.");
        return true;
    }

    private void FindPaintSlots()
    {
        slots.Clear();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null || material.name.IndexOf(
                        PaintMaterialMarker, StringComparison.OrdinalIgnoreCase) < 0 && renderer.name != "Object_14_OriginalColorBalloon")
                    continue;
                slots.Add(new PaintSlot(renderer, material, index));
            }
        }

        initialized = true;
        if (slots.Count == 0 && !warnedNoSlots)
        {
            warnedNoSlots = true;
            context?.Logger.Warn(
                $"Battle Bus paint vehicle={vehicle?.GetInstanceID()}: no generated paint material slots were found.");
        }
    }

    private VehicleColor? ResolveVehicleColor(string source)
    {
        var live = vehicle?.CarFeatures?.VehicleColor;
        if (live != null && string.Equals(source, "vehicle-repainter", StringComparison.Ordinal))
            return live;

        var colorName = vehicle?.vehicleInstance?.vehicleColorName;
        if (!string.IsNullOrWhiteSpace(colorName))
        {
            if (VehicleHelper.TryGetVehicleColor(colorName, out var saved))
                return saved;
            if (live != null)
                return live;
        }
        return null;
    }

    private void OnDestroy(){if(capTexture!=null)Destroy(capTexture);}

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
}
