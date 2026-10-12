#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

[AddComponentMenu("")]
public sealed class VolkswagenT1BusMaterialController : MonoBehaviour
{
    private const string RimMaterialMarker = "__T1BUS_SEPARATE_RIM_FINISH_DISABLED__";
    private readonly List<Material> ownedMaterials = new List<Material>();
    private VolkswagenT1BusMaterialFixResult result;
    private bool initialized;

    internal VolkswagenT1BusMaterialFixResult Initialize(ModContext? context)
    {
        if (initialized)
            return result;

        initialized = true;
        var clones = new Dictionary<Material, Dictionary<VolkswagenT1BusTransparentRole, Material>>();
        var rendererCount = 0;
        var transparentMaterials = 0;
        var opaqueMaterials = 0;
        var authoredTransparentMaterials = 0;
        var rimSlots = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!VolkswagenT1BusMaterials.IsT1BusRenderer(renderer.transform))
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

                var role = VolkswagenT1BusMaterials.GetTransparentRole(renderer, source);
                if (!clones.TryGetValue(source, out var roleVariants))
                {
                    roleVariants = new Dictionary<VolkswagenT1BusTransparentRole, Material>();
                    clones.Add(source, roleVariants);
                }
                if (!roleVariants.TryGetValue(role, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_T1BusInstance_" + role;
                    roleVariants.Add(role, runtimeMaterial);
                    ownedMaterials.Add(runtimeMaterial);

                    if (VolkswagenT1BusMaterials.IsTransparentMaterial(runtimeMaterial))
                    {
                        var transparentRole = role == VolkswagenT1BusTransparentRole.Authored
                            ? VolkswagenT1BusTransparentRole.OtherClear
                            : role;
                        VolkswagenT1BusMaterials.PrepareTransparentMaterial(runtimeMaterial, transparentRole);
                        transparentMaterials++;
                    }
                    else
                    {
                        VolkswagenT1BusMaterials.NormalizeImportedMaterial(runtimeMaterial);
                        opaqueMaterials++;
                    }
                    if (runtimeMaterial.name.IndexOf(
                            RimMaterialMarker,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetRimFinish(runtimeMaterial);
                    }
                }

                if (role == VolkswagenT1BusTransparentRole.CabinGlass)
                    hasCabinGlass = true;
                if (runtimeMaterial.name.IndexOf(
                        RimMaterialMarker,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    rimSlots++;
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

        result = new VolkswagenT1BusMaterialFixResult(
            rendererCount,
            0,
            opaqueMaterials,
            transparentMaterials,
            0,
            rimSlots,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
        VolkswagenT1BusDiagnostics.Info(
            context,
            $"VolkswagenT1Bus materials vehicle={GetInstanceID()}: cloned " +
            $"{ownedMaterials.Count} materials for {rendererCount} renderers, " +
            $"clearSurfaceVariants={transparentMaterials}, " +
            $"authoredTransparentRetained={authoredTransparentMaterials}, " +
            $"cabinGlass={cabinGlassRenderers}, " +
            $"rimSlots={rimSlots}; opaque authored materials retained unchanged.");
        return result;
    }

    private static void SetRimFinish(Material material)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", VolkswagenT1BusMaterials.RimBaseColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", VolkswagenT1BusMaterials.RimBaseColor);
        if (material.HasProperty("baseColorFactor"))
            material.SetColor("baseColorFactor", VolkswagenT1BusMaterials.RimBaseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", VolkswagenT1BusMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", VolkswagenT1BusMaterials.RimSmoothness);
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
