#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

[AddComponentMenu("")]
public sealed class VolkswagenAmarokMaterialController : MonoBehaviour
{
    private const string RimMaterialMarker = "__AMAROK_SEPARATE_RIM_FINISH_DISABLED__";
    private readonly List<Material> ownedMaterials = new List<Material>();
    private VolkswagenAmarokMaterialFixResult result;
    private bool initialized;

    internal VolkswagenAmarokMaterialFixResult Initialize(ModContext? context)
    {
        if (initialized)
            return result;

        initialized = true;
        var clones = new Dictionary<Material, Dictionary<VolkswagenAmarokTransparentRole, Material>>();
        var rendererCount = 0;
        var transparentMaterials = 0;
        var opaqueMaterials = 0;
        var authoredTransparentMaterials = 0;
        var rimSlots = 0;
        var cabinGlassRenderers = 0;
        var cabinGlassRenderersReenabled = 0;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!VolkswagenAmarokMaterials.IsAmarokRenderer(renderer.transform))
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

                var role = VolkswagenAmarokMaterials.GetTransparentRole(renderer, source);
                if (!clones.TryGetValue(source, out var roleVariants))
                {
                    roleVariants = new Dictionary<VolkswagenAmarokTransparentRole, Material>();
                    clones.Add(source, roleVariants);
                }
                if (!roleVariants.TryGetValue(role, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_AmarokInstance_" + role;
                    roleVariants.Add(role, runtimeMaterial);
                    ownedMaterials.Add(runtimeMaterial);

                    if (VolkswagenAmarokMaterials.IsTransparentMaterial(runtimeMaterial))
                    {
                        var transparentRole = role == VolkswagenAmarokTransparentRole.Authored
                            ? VolkswagenAmarokTransparentRole.OtherClear
                            : role;
                        VolkswagenAmarokMaterials.PrepareTransparentMaterial(runtimeMaterial, transparentRole);
                        transparentMaterials++;
                    }
                    else
                    {
                        VolkswagenAmarokMaterials.NormalizeImportedMaterial(runtimeMaterial);
                        opaqueMaterials++;
                    }
                    if (runtimeMaterial.name.IndexOf(
                            RimMaterialMarker,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SetRimFinish(runtimeMaterial);
                    }
                }

                if (role == VolkswagenAmarokTransparentRole.CabinGlass)
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

        result = new VolkswagenAmarokMaterialFixResult(
            rendererCount,
            0,
            opaqueMaterials,
            transparentMaterials,
            0,
            rimSlots,
            cabinGlassRenderers,
            cabinGlassRenderersReenabled);
        VolkswagenAmarokDiagnostics.Info(
            context,
            $"VolkswagenAmarok materials vehicle={GetInstanceID()}: cloned " +
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
            material.SetColor("_BaseColor", VolkswagenAmarokMaterials.RimBaseColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", VolkswagenAmarokMaterials.RimBaseColor);
        if (material.HasProperty("baseColorFactor"))
            material.SetColor("baseColorFactor", VolkswagenAmarokMaterials.RimBaseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", VolkswagenAmarokMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", VolkswagenAmarokMaterials.RimSmoothness);
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
