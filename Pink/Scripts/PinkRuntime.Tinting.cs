#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pink
{
    internal static partial class PinkRuntime
    {
        private static int TintVehicleRoot(GameObject root)
        {
            var patched = 0;
            var totalRenderers = 0;
            var skippedRenderers = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(false);

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                totalRenderers++;
                if (ShouldSkipWholeVehicleRenderer(renderer))
                {
                    skippedRenderers++;
                    continue;
                }

                patched += TintRendererMaterials(renderer, RootKind.Vehicle);
            }

            PinkFileLogger.Verbose(
                $"Vehicle root processed: path={GetPath(root.transform, 6)}, renderers={totalRenderers}, skippedRenderers={skippedRenderers}, patchedSlots={patched}");

            return patched;
        }

        private static int TintNpcRoot(GameObject root)
        {
            var patched = 0;
            var totalRenderers = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(false);

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                totalRenderers++;
                patched += TintRendererMaterials(renderer, RootKind.Npc);
            }

            PinkFileLogger.Verbose(
                $"NPC root processed: path={GetPath(root.transform, 6)}, renderers={totalRenderers}, patchedSlots={patched}");

            return patched;
        }

        private static int TintRendererMaterials(Renderer renderer, RootKind kind, bool strictVehicleFallback = false)
        {
            var patched = 0;
            Material[] materials;

            try
            {
                materials = renderer.materials;
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"renderer.materials failed: kind={kind}, renderer={GetPath(renderer.transform, 6)}, error={ex.GetType().Name}: {ex.Message}");
                return 0;
            }

            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;

                if (kind == RootKind.Vehicle && !ShouldTintVehicleMaterialSlot(renderer, material, index, materials.Length, strictVehicleFallback))
                    continue;

                if (kind == RootKind.Npc && !ShouldTintNpcMaterialSlot(renderer, material, index))
                    continue;

                var pinkColor = GetPinkColorFor(renderer, material, index, kind, strictVehicleFallback);
                var changedMaterial = TryTintMaterial(material, pinkColor);
                var changedBlock = TryTintRendererPropertyBlock(renderer, index, material, pinkColor);

                if (!changedMaterial && !changedBlock)
                    continue;

                if (kind == RootKind.Npc)
                    LogNpcDiagnosticSample(renderer, material, index, changedMaterial, changedBlock);

                patched++;
                PinkFileLogger.Verbose(
                    $"Patched {kind} material slot: renderer={renderer.name}, slot={index}, material={material.name}, " +
                    $"materialColor={(changedMaterial ? "yes" : "no")}, propertyBlock={(changedBlock ? "yes" : "no")}");
            }

            return patched;
        }

        private static bool LooksLikeTaxiRenderer(Renderer renderer, string combined)
        {
            if (ContainsAnyToken(combined, new[] { "delivery", "truck", "flatbed", "freight", "vanbody", "body_van", "van_body" }))
                return false;

            if (ContainsAnyToken(combined, TaxiAllowTokens))
            {
                LogTaxiDiagnosticSample(renderer, null, combined, "renderer-token");
                return true;
            }

            if (ContainsAnyToken(combined, new[] { "cab" }) && HasLikelyYellowPaint(renderer))
            {
                LogTaxiDiagnosticSample(renderer, null, combined, "yellow-cab-renderer");
                return true;
            }


            return false;
        }

        private static bool IsLikelyTaxiSlot(Renderer renderer, Material material, string slotText)
        {
            if (ContainsAnyToken(slotText, new[] { "delivery", "truck", "flatbed", "freight", "vanbody", "body_van", "van_body" }))
                return false;

            if (ContainsAnyToken(slotText, TaxiAllowTokens))
                return true;

            if (ContainsAnyToken(slotText, new[] { "cab" }) && HasLikelyYellowPaint(material))
                return true;

            if (HasLikelyYellowPaint(material) && ContainsAnyToken(slotText, new[] { "car", "vehicle", "traffic", "saloon", "sedan" }))
                return true;

            return false;
        }

        private static bool HasLikelyYellowPaint(Renderer renderer)
        {
            Material[] materials;
            try
            {
                materials = renderer.sharedMaterials;
            }
            catch
            {
                return false;
            }

            for (var index = 0; index < materials.Length; index++)
            {
                if (HasLikelyYellowPaint(materials[index]))
                    return true;
            }

            return false;
        }

        private static bool HasLikelyYellowPaint(Material? material)
        {
            if (material == null)
                return false;

            var materialName = material.name ?? string.Empty;
            if (ContainsAnyToken(materialName, new[] { "yellow", "taxi", "taxicab", "yellowcab" }))
                return true;

            for (var index = 0; index < CandidateColorPropertyIds.Length; index++)
            {
                var propertyId = CandidateColorPropertyIds[index];
                if (!material.HasProperty(propertyId))
                    continue;

                try
                {
                    if (IsLikelyTaxiYellow(material.GetColor(propertyId)))
                        return true;
                }
                catch
                {
                    // Ignore unsupported color reads.
                }
            }

            return false;
        }

        private static bool IsLikelyTaxiYellow(Color color)
        {
            return color.r >= 0.65f && color.g >= 0.45f && color.b <= 0.35f && color.r >= color.g * 0.85f;
        }

        private static void LogTaxiDiagnosticSample(Renderer renderer, Material? material, string text, string reason)
        {
            if (taxiDiagnosticSamples >= MaxTaxiDiagnosticSamples)
                return;

            taxiDiagnosticSamples++;
            PinkFileLogger.Info(
                $"TAXI_DIAG {taxiDiagnosticSamples}/{MaxTaxiDiagnosticSamples}: reason={reason}, " +
                $"rendererPath={GetPath(renderer.transform, 8)}, renderer={renderer.name}, " +
                $"material={(material == null ? "<shared-scan>" : material.name)}, text={text}");
        }

        private static void LogTrafficLightDiagnosticSample(Renderer renderer, Material material, string text)
        {
            if (trafficLightDiagnosticSamples >= MaxTrafficLightDiagnosticSamples)
                return;

            trafficLightDiagnosticSamples++;
            PinkFileLogger.Info(
                $"TRAFFICLIGHT_DIAG {trafficLightDiagnosticSamples}/{MaxTrafficLightDiagnosticSamples}: " +
                $"rendererPath={GetPath(renderer.transform, 8)}, renderer={renderer.name}, parent={(renderer.transform.parent == null ? "<null>" : renderer.transform.parent.name)}, " +
                $"material={material.name}, text={text}");
        }

        private static bool ShouldSkipWholeVehicleRenderer(Renderer renderer)
        {
            var name = renderer.name;
            if (ContainsAnyToken(name, VehicleDenyTokens))
                return true;

            return false;
        }

        private static bool ShouldTintVehicleMaterialSlot(Renderer renderer, Material material, int materialIndex, int materialCount, bool strictVehicleFallback)
        {
            var rendererName = renderer.name ?? string.Empty;
            var parentName = renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty;
            var materialName = material.name ?? string.Empty;
            var slotText = rendererName + " " + parentName + " " + materialName;

            if (IsLikelyTaxiSlot(renderer, material, slotText))
            {
                LogTaxiDiagnosticSample(renderer, material, slotText, "tint");

                // Taxis are often named YellowCab/TaxiCab/CabBody or only expose a yellow cab material.
                // The normal cab/cabin deny exists for delivery trucks, but would also block taxi bodies.
                if (ContainsAnyToken(slotText, VehicleDenyTokensWithoutCab))
                    return false;

                return true;
            }

            if (ContainsAnyToken(slotText, VehicleDenyTokens))
                return false;

            if (strictVehicleFallback)
            {
                // For global renderer fallback, do not accept generic body/door/panel/exterior names.
                // Those are common on buildings. Require a vehicle-specific renderer/material name.
                return ContainsAnyToken(slotText, VehicleFallbackAllowTokens);
            }

            if (ContainsAnyToken(slotText, VehicleAllowTokens))
                return true;

            // If a vehicle renderer has several material slots and the slot name tells us nothing,
            // do not tint it. Multi-slot renderers often contain glass/lights/plates mixed with body material.
            if (materialCount > 1)
            {
                PinkFileLogger.Verbose(
                    $"Skipped unknown vehicle multi-material slot: renderer={rendererName}, slot={materialIndex}, material={materialName}");
                return false;
            }

            // Single-material vehicle renderers are usually body/exterior meshes. This keeps Flatbed/M_Flatbed working.
            return true;
        }

        private static bool ShouldTintNpcMaterialSlot(Renderer renderer, Material material, int materialIndex)
        {
            var rendererName = renderer.name ?? string.Empty;
            var parentName = renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty;
            var materialName = material.name ?? string.Empty;
            var shaderName = material.shader != null ? material.shader.name : string.Empty;
            var slotText = rendererName + " " + parentName + " " + materialName + " " + shaderName;

            // Important: hard deny must still win over any outfit selection.
            if (ContainsAnyToken(materialName, NpcHardDenyTokens))
                return false;

            if (ContainsAnyToken(slotText, NpcHardDenyTokens))
                return false;

            var category = ClassifyNpcAppearanceCategory(renderer, material, slotText);
            if (category == NpcAppearanceCategory.None)
                return false;

            var shouldTint = ShouldTintNpcAppearanceCategory(renderer, material, category);
            if (shouldTint)
            {
                PinkFileLogger.Verbose(
                    $"Accepted NPC slot: renderer={rendererName}, slot={materialIndex}, material={materialName}, category={category}");
                return true;
            }

            PinkFileLogger.Verbose(
                $"Skipped NPC slot after outfit roll: renderer={rendererName}, slot={materialIndex}, material={materialName}, category={category}");
            return false;
        }

        private static NpcAppearanceCategory ClassifyNpcAppearanceCategory(Renderer renderer, Material material, string slotText)
        {
            if (ContainsAnyToken(slotText, NpcHairTokens))
                return NpcAppearanceCategory.Hair;

            if (ContainsAnyToken(slotText, NpcUpperClothingTokens) || ContainsAnyToken(slotText, NpcAllowTokens))
                return NpcAppearanceCategory.UpperClothing;

            if (ContainsAnyToken(slotText, NpcLowerClothingTokens))
                return NpcAppearanceCategory.LowerClothing;

            if (ContainsAnyToken(slotText, NpcFootwearTokens))
                return NpcAppearanceCategory.Footwear;

            if (ContainsAnyToken(slotText, NpcHeadwearTokens))
                return NpcAppearanceCategory.Headwear;

            if (ContainsAnyToken(slotText, NpcAccessoryTokens))
                return NpcAppearanceCategory.Accessory;

            if (!AggressiveNpcClothingTint)
                return NpcAppearanceCategory.None;

            if (material.shader != null &&
                material.shader.name.IndexOf("CharacterClothes", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return NpcAppearanceCategory.UpperClothing;
            }

            return NpcAppearanceCategory.None;
        }

        private static bool ShouldTintNpcAppearanceCategory(Renderer renderer, Material material, NpcAppearanceCategory category)
        {
            var key = BuildNpcAppearanceRollKey(renderer, material, category);
            var roll = GetStableUnitFloat(key);

            switch (category)
            {
                case NpcAppearanceCategory.Hair:
                    return roll < 0.035f;
                case NpcAppearanceCategory.UpperClothing:
                    return roll < 0.90f;
                case NpcAppearanceCategory.LowerClothing:
                    return roll < 0.62f;
                case NpcAppearanceCategory.Footwear:
                    return roll < 0.35f;
                case NpcAppearanceCategory.Headwear:
                    return roll < 0.28f;
                case NpcAppearanceCategory.Accessory:
                    return roll < 0.24f;
                default:
                    return false;
            }
        }

        private static string BuildNpcAppearanceRollKey(Renderer renderer, Material material, NpcAppearanceCategory category)
        {
            return "npc-roll|" + FindStableNpcOwnerName(renderer.transform) + "|" + category + "|" + NormalizeName(material.name) + "|" + NormalizeName(renderer.name);
        }

        private static float GetStableUnitFloat(string key)
        {
            var hash = PositiveStableHash(key);
            return hash / (float)int.MaxValue;
        }

        private static void LogNpcDiagnosticSample(Renderer renderer, Material material, int materialIndex, bool changedMaterial, bool changedBlock)
        {
            if (!EnableNpcDiagnosticSamples || npcDiagnosticSamples >= MaxNpcDiagnosticSamples)
                return;

            npcDiagnosticSamples++;
            PinkFileLogger.Info(
                $"NPC_DIAG patchedSlot={npcDiagnosticSamples}/{MaxNpcDiagnosticSamples}, " +
                $"rendererPath={GetPath(renderer.transform, 8)}, renderer={renderer.name}, slot={materialIndex}, " +
                $"material={material.name}, shader={(material.shader != null ? material.shader.name : "<null>")}, " +
                $"properties={GetSupportedColorPropertyNamesForLog(material)}, materialColor={(changedMaterial ? "yes" : "no")}, propertyBlock={(changedBlock ? "yes" : "no")}");
        }

        private static string GetSupportedColorPropertyNamesForLog(Material material)
        {
            var names = new List<string>();

            try
            {
                var shader = material.shader;
                if (shader != null)
                {
                    var count = shader.GetPropertyCount();
                    for (var index = 0; index < count; index++)
                    {
                        if (shader.GetPropertyType(index) != ShaderPropertyType.Color)
                            continue;

                        var propertyName = shader.GetPropertyName(index);
                        if (ContainsAnyToken(propertyName, ShaderColorPropertyDenyTokens))
                            continue;

                        if (!names.Contains(propertyName))
                            names.Add(propertyName);
                    }
                }
            }
            catch
            {
                // Ignore diagnostic failures.
            }

            var fallbackNames = new[]
            {
                "_BaseColor", "_Color", "_MainColor", "_TintColor", "_VehicleColor", "_PaintColor",
                "_PrimaryColor", "_SecondaryColor", "_Color1", "_Color2", "_AlbedoColor", "_DiffuseColor"
            };

            for (var index = 0; index < fallbackNames.Length; index++)
            {
                var name = fallbackNames[index];
                if (material.HasProperty(Shader.PropertyToID(name)) && !names.Contains(name))
                    names.Add(name);
            }

            return names.Count == 0 ? "<none>" : string.Join("|", names.ToArray());
        }

        private enum NpcAppearanceCategory
        {
            None,
            Hair,
            UpperClothing,
            LowerClothing,
            Footwear,
            Headwear,
            Accessory
        }
    }
}
