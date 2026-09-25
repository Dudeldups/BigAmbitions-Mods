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
        private static IEnumerable<Renderer> EnumerateSimpleWorldPropRenderers()
        {
            Renderer[] renderers;
            try
            {
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(false);
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"FindObjectsOfType<Renderer> simple world prop scan failed: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                    continue;

                if (GetSimpleWorldPropColor(renderer) == null)
                    continue;

                yield return renderer;
            }
        }

        private static int TintSimpleWorldPropRenderer(Renderer renderer)
        {
            var color = GetSimpleWorldPropColor(renderer);
            if (color == null)
                return 0;

            var propText = GetRendererParentAndSharedMaterialNameText(renderer);

            Material[] materials;
            try
            {
                materials = renderer.materials;
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"renderer.materials failed for simple world prop: renderer={GetPath(renderer.transform, 6)}, error={ex.GetType().Name}: {ex.Message}");
                return 0;
            }

            var patched = 0;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null || ShouldSkipSimpleWorldPropSlot(renderer, material))
                    continue;

                if (!ShouldTintSimpleWorldPropSlot(renderer, material, propText))
                    continue;

                var changedMaterial = TryTintMaterial(material, color.Value);
                var changedBlock = TryTintRendererPropertyBlock(renderer, index, material, color.Value);
                if (changedMaterial || changedBlock)
                    patched++;
            }

            return patched;
        }

        private static Color? GetSimpleWorldPropColor(Renderer renderer)
        {
            var text = GetRendererParentAndSharedMaterialNameText(renderer);

            if (IsStatueRenderer(renderer, text))
                return FixedDarkPink;

            if (IsUmbrellaRenderer(renderer, text))
                return FixedDarkPink;

            if (IsCrosswalkRenderer(renderer, text))
                return FixedBrightPink;

            if (ContainsAnyToken(text, new[] { "hydrant", "firehydrant", "fire_hydrant", "fire hydrant" }))
                return FixedBrightPink;

            if (IsBikeRentalStandRenderer(renderer, text))
                return FixedDarkPink;

            if (IsTrashCanText(text))
                return IsClosedTrashBinText(text) ? FixedTrashLightPink : FixedTrashPink;

            if (LooksLikeClosedBlueTrashBin(renderer, text))
                return FixedTrashLightPink;

            if (IsMailboxText(text))
                return FixedDarkPink;

            return null;
        }

        private static bool IsStatueRenderer(Renderer renderer, string localText)
        {
            var path = GetPath(renderer.transform, 12);
            var combined = path + " " + localText;

            if (!ContainsAnyToken(combined, new[]
            {
                "statue", "sculpture", "monument"
            }))
            {
                return false;
            }

            if (ContainsAnyToken(combined, new[]
                {
                    "ground", "floor", "buildingfloor", "building floor", "bank", "accessor", "accessory",
                    "road", "sidewalk", "decal", "crack", "streetlamp", "street lamp", "lamp"
                }))
            {
                return false;
            }

            var layerName = LayerMask.LayerToName(renderer.gameObject.layer);
            return string.Equals(layerName, "StreetProps", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(layerName, "Default", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUmbrellaRenderer(Renderer renderer, string localText)
        {
            var path = GetPath(renderer.transform, 12);
            var combined = path + " " + localText;

            if (!ContainsAnyToken(combined, new[] { "umbrella", "parasol", "canopy" }))
                return false;

            if (ContainsAnyToken(combined, new[] { "prefab_tablegroup", "regularchair", "roundtable", "table/" }))
                return false;

            return ContainsAnyToken(combined, new[] { "terrace umbrella", "terraceumbrella", "sm_terraceumbrella", "umbrella/" });
        }

        private static bool IsHandTruckRenderer(Renderer renderer, string localText)
        {
            return false;
        }

        private static bool IsDeliverySpotRenderer(Renderer renderer, string localText)
        {
            return false;
        }

        private static bool IsMarqueeRenderer(Renderer renderer, string localText)
        {
            return false;
        }

        private static bool IsCrosswalkRenderer(Renderer renderer, string localText)
        {
            var path = GetPath(renderer.transform, 14);
            var combined = path + " " + localText;

            if (!ContainsAnyToken(combined, new[]
                {
                    "sm_roadmoduleintersection_pier", "sm_roadmodule tintersection", "intersection"
                }))
            {
                return false;
            }

            if (ContainsAnyToken(combined, new[]
                {
                    "trashdecoration", "leaf_", "roadcracks", "road cracks", "decal", "plane."
                }))
            {
                return false;
            }

            if (ContainsAnyToken(combined, new[]
                {
                    "streetlamp", "street lamp", "sign", "parking"
                }))
            {
                return false;
            }

            var layerName = LayerMask.LayerToName(renderer.gameObject.layer);
            return string.Equals(layerName, "Default", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(layerName, "StreetProps", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBikeRentalStandRenderer(Renderer renderer, string localText)
        {
            // Confirmed by runtime diagnostics:
            // Bike Stand/Prefab_BikeRentalStand (...)/SM_BikeRentalStand_LOD1
            // materials=M_Aluminium_01 | M_Metal Frame.
            var path = GetPath(renderer.transform, 12);
            var combined = path + " " + localText;

            if (!ContainsAnyToken(combined, new[] { "prefab_bikerentalstand", "sm_bikerentalstand", "sm_bikerentalstand_lod1" }))
                return false;

            if (!ContainsAnyToken(combined, new[] { "m_metal frame", "m_aluminium_01", "bike stand" }))
                return false;

            // Do not touch scooter rental/electric scooter helper renderers.
            // Important: do not exclude plain "prefab_bike" or "m_bike" here, because those are substrings of BikeRentalStand names.
            if (ContainsAnyToken(combined, new[]
                {
                    "scooterrentalstand", "sm_scooterrentalstand", "scooterspawner", "sm_electricscooter", "electric scooter", "m_unlitblack", "shadow caster"
                }))
            {
                return false;
            }

            if (ContainsAnyToken(combined, new[] { "human", "pedestrian", "npc", "customer", "employee" }))
                return false;

            return true;
        }

        private static bool IsTrashCanText(string text)
        {
            if (ContainsAnyToken(text, new[]
                {
                    "trashcan", "trash_can", "trash can",
                    "trashbin", "trash_bin", "trash bin",
                    "garbagecan", "garbage_can", "garbage can",
                    "garbagebin", "garbage_bin", "garbage bin",
                    "wastecan", "waste_can", "waste can",
                    "wastebin", "waste_bin", "waste bin",
                    "recyclebin", "recycle_bin", "recycle bin",
                    "recyclingbin", "recycling_bin", "recycling bin",
                    "dumpster", "dustbin", "dust_bin", "dust bin",
                    "wheeliebin", "wheelie_bin", "wheelie bin",
                    "wheelybin", "wheely_bin", "wheely bin",
                    "wheelbin", "wheel_bin", "wheel bin",
                    "bluebin", "blue_bin", "blue bin",
                    "trashcontainer", "trash_container", "trash container",
                    "garbagecontainer", "garbage_container", "garbage container",
                    "wastecontainer", "waste_container", "waste container",
                    "refusecontainer", "refuse_container", "refuse container"
                }))
            {
                return true;
            }

            // Fallback for closed street bins that are named generically but have blue/recycle/trash hints on material or parent.
            return ContainsAnyToken(text, new[] { "bin", "container" }) &&
                   ContainsAnyToken(text, new[] { "trash", "garbage", "waste", "recycle", "recycling", "refuse", "sanitation", "wheelie", "wheely", "blue" });
        }

        private static bool IsClosedTrashBinText(string text)
        {
            return ContainsAnyToken(text, new[]
                {
                    "sortingbin", "sorting_bin", "sorting bin",
                    "wheeliebin", "wheelie_bin", "wheelie bin",
                    "wheelybin", "wheely_bin", "wheely bin",
                    "bluebin", "blue_bin", "blue bin",
                    "trashcontainer", "trash_container", "trash container",
                    "garbagecontainer", "garbage_container", "garbage container",
                    "wastecontainer", "waste_container", "waste container",
                    "refusecontainer", "refuse_container", "refuse container"
                });
        }

        private static bool LooksLikeClosedBlueTrashBin(Renderer renderer, string text)
        {
            if (ContainsAnyToken(text, new[]
                {
                    "vehicle", "car", "taxi", "truck", "traffic", "pedestrian", "human", "npc", "customer", "employee",
                    "shirt", "pants", "hair", "skin", "building", "facade", "window", "shop", "store", "sign", "billboard",
                    "streetlight", "trafficlight", "lamp", "mailbox", "postbox",
                    "carnival", "ferriswheel", "ferris wheel", "cabin", "ticketbooth", "ticket booth", "booth"
                }))
            {
                return false;
            }

            var size = renderer.bounds.size;
            var maxDimension = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (maxDimension > 4.0f)
                return false;

            var blueInfo = GetBlueMaterialInfo(renderer);
            if (blueInfo == null)
                return false;

            if (ContainsAnyToken(text, new[]
                {
                    "bin", "container", "wheelie", "wheely", "trash", "garbage", "waste", "recycle", "recycling", "refuse", "dustbin", "barrel"
                }))
            {
                LogBlueBinDiagnosticSample(renderer, blueInfo, "accepted");
                return true;
            }

            LogBlueBinDiagnosticSample(renderer, blueInfo, "blue-small-skipped-no-bin-token");
            return false;
        }

        private static string? GetBlueMaterialInfo(Renderer renderer)
        {
            Material[] materials;
            try
            {
                materials = renderer.sharedMaterials;
            }
            catch
            {
                return null;
            }

            for (var materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                var material = materials[materialIndex];
                if (material == null)
                    continue;

                var materialName = material.name ?? string.Empty;
                if (ContainsAnyToken(materialName, new[] { "blue", "bin", "trash", "garbage", "waste", "recycle", "refuse" }))
                    return $"material={materialName}";

                for (var propertyIndex = 0; propertyIndex < CandidateColorPropertyIds.Length; propertyIndex++)
                {
                    var propertyId = CandidateColorPropertyIds[propertyIndex];
                    if (!material.HasProperty(propertyId))
                        continue;

                    try
                    {
                        var color = material.GetColor(propertyId);
                        if (IsLikelyBinBlue(color))
                            return $"material={materialName}, color={color}";
                    }
                    catch
                    {
                        // Ignore unsupported reads.
                    }
                }
            }

            return null;
        }

        private static bool IsLikelyBinBlue(Color color)
        {
            return color.b >= 0.35f && color.b > color.r * 1.25f && color.b > color.g * 1.05f;
        }

        private static void LogBlueBinDiagnosticSample(Renderer renderer, string info, string reason)
        {
            if (blueBinDiagnosticSamples >= MaxBlueBinDiagnosticSamples)
                return;

            blueBinDiagnosticSamples++;
            var size = renderer.bounds.size;
            PinkFileLogger.Info(
                $"BLUE_BIN_DIAG {blueBinDiagnosticSamples}/{MaxBlueBinDiagnosticSamples}: reason={reason}, " +
                $"path={GetPath(renderer.transform, 8)}, renderer={renderer.name}, parent={(renderer.transform.parent == null ? "<null>" : renderer.transform.parent.name)}, " +
                $"size={size}, {info}");
        }

        private static bool IsMailboxText(string text)
        {
            return ContainsAnyToken(text, new[]
                {
                    "mailbox", "mail_box", "mail box",
                    "postbox", "post_box", "post box",
                    "postalbox", "postal_box", "postal box",
                    "maildrop", "mail_drop", "mail drop"
                });
        }

        private static string GetRendererAndParentNameText(Renderer renderer)
        {
            var text = renderer.name ?? string.Empty;

            var parent = renderer.transform.parent;
            if (parent != null)
                text += " " + parent.name;

            return text;
        }

        private static string GetRendererParentAndSharedMaterialNameText(Renderer renderer)
        {
            var text = GetRendererAndParentNameText(renderer);

            try
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null)
                        text += " " + material.name;
                }
            }
            catch
            {
                // Ignore material-name diagnostics for world-prop detection.
            }

            return text;
        }

        private static bool ShouldSkipSimpleWorldPropSlot(Renderer renderer, Material material)
        {
            var rendererName = renderer.name ?? string.Empty;
            var parentName = renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty;
            var materialName = material.name ?? string.Empty;
            var text = rendererName + " " + parentName + " " + materialName;
            var propText = GetRendererParentAndSharedMaterialNameText(renderer);

            if (IsTrashCanText(propText) && ContainsAnyToken(text, new[]
                {
                    "lid", "cover", "top", "cap", "handle", "hinge", "wheel", "tire", "tyre",
                    "rubber", "glass", "window", "label", "sticker", "decal"
                }))
            {
                return true;
            }

            if (IsMailboxText(propText) && ContainsAnyToken(text, new[]
                {
                    "handle", "hinge", "slot", "flag", "label", "sticker", "decal",
                    "glass", "window"
                }))
            {
                return true;
            }

            // Generic safety: do not tint obvious glass/light/wheel/decal slots on world props.
            if (ContainsAnyToken(text, new[]
                {
                    "glass", "window", "wheel", "tire", "tyre", "rim", "plate", "license",
                    "bulb", "emission", "emissive", "glow", "lens",
                    "red", "green", "yellow", "signal", "walk", "dontwalk", "don't walk"
                }))
            {
                return true;
            }

            return false;
        }

        private static bool ShouldTintSimpleWorldPropSlot(Renderer renderer, Material material, string propText)
        {
            if (IsUmbrellaRenderer(renderer, propText))
                return !ContainsAnyToken((material.name ?? string.Empty) + " " + renderer.name, new[] { "pole", "post", "frame", "metal", "stand", "base" });

            if (IsCrosswalkRenderer(renderer, propText))
                return !ContainsAnyToken((material.name ?? string.Empty) + " " + renderer.name, new[] { "shadow", "caster", "light", "bulb", "emission", "emissive", "particle", "fx", "vfx", "dust", "smoke", "asphalt" });

            return true;
        }
    }
}
