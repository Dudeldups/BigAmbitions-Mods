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
        private static IEnumerable<GameObject> EnumerateVehicleRoots()
        {
            var seen = new HashSet<int>();

            foreach (var tag in VehicleTags)
            {
                foreach (var root in FindByTagSafe(tag))
                {
                    var safeRoot = ResolveSafeRoot(root, RootKind.Vehicle, "tag:" + tag);
                    if (safeRoot == null || !seen.Add(safeRoot.GetInstanceID()))
                        continue;

                    yield return safeRoot;
                }
            }

            foreach (var root in EnumerateRootsFromBehaviourTypes(RootKind.Vehicle))
            {
                if (root == null || !seen.Add(root.GetInstanceID()))
                    continue;

                yield return root;
            }
        }

        private static IEnumerable<GameObject> EnumerateNpcRoots()
        {
            var seen = new HashSet<int>();

            // Behaviour roots first: this is more likely to catch normal walking pedestrians.
            foreach (var root in EnumerateRootsFromBehaviourTypes(RootKind.Npc))
            {
                if (root == null || !seen.Add(root.GetInstanceID()))
                    continue;

                yield return root;
            }

            // Tag fallback second. This often finds street vendors/carnival helper humans, so it is intentionally lower priority.
            foreach (var tag in NpcTags)
            {
                foreach (var root in FindByTagSafe(tag))
                {
                    var safeRoot = ResolveSafeRoot(root, RootKind.Npc, "tag:" + tag);
                    if (safeRoot == null || !seen.Add(safeRoot.GetInstanceID()))
                        continue;

                    yield return safeRoot;
                }
            }
        }

        private static IEnumerable<GameObject> EnumerateRootsFromBehaviourTypes(RootKind kind)
        {
            MonoBehaviour[] behaviours;
            try
            {
                behaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>(true);
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"FindObjectsOfType<MonoBehaviour> failed for {kind}: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }

            for (var index = 0; index < behaviours.Length; index++)
            {
                var behaviour = behaviours[index];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                var typeName = type.Name;
                var fullName = type.FullName ?? string.Empty;

                var matches = kind == RootKind.Vehicle
                    ? IsVehicleBehaviourType(typeName, fullName)
                    : IsNpcBehaviourType(typeName, fullName);

                if (!matches)
                    continue;

                CountBehaviourHit(typeName);

                var root = ResolveSafeRoot(behaviour.gameObject, kind, "behaviour:" + typeName);
                if (root == null)
                    continue;

                yield return root;
            }
        }

        private static IEnumerable<Renderer> EnumerateVehicleFallbackRenderers()
        {
            Renderer[] renderers;
            try
            {
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(false);
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"FindObjectsOfType<Renderer> fallback failed: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (!LooksLikeVehicleRenderer(renderer))
                    continue;

                yield return renderer;
            }
        }

        private static bool LooksLikeVehicleRenderer(Renderer renderer)
        {
            var rendererName = renderer.name ?? string.Empty;
            var parentName = renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty;
            var materialNames = string.Empty;

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                    continue;

                materialNames += " " + material.name;
            }

            var combined = rendererName + " " + parentName + " " + materialNames;

            if (LooksLikeTaxiRenderer(renderer, combined))
                return true;

            if (ContainsAnyToken(combined, VehicleDenyTokens))
                return false;

            if (!ContainsAnyToken(combined, VehicleFallbackAllowTokens))
                return false;

            // Avoid obvious UI/preview/editor-ish renderers; actual parked/traffic vehicles can still live under Roads/Street paths.
            var rootName = renderer.transform.root != null ? renderer.transform.root.name : string.Empty;
            if (ContainsAnyToken(rootName, new[] { "canvas", "ui", "preview", "thumbnail" }))
                return false;

            return true;
        }

        private static bool IsVehicleBehaviourType(string typeName, string fullName)
        {
            if (typeName.EndsWith("VehicleController", StringComparison.OrdinalIgnoreCase))
                return true;

            if (typeName.EndsWith("CarController", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(typeName, "TrafficCar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "AITrafficCar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "AiTrafficCar", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "ParkedVehicle", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static bool IsNpcBehaviourType(string typeName, string fullName)
        {
            if (string.Equals(typeName, "BaseHuman", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "Pedestrian", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "CarnivalPedestrian", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "WaterPedestrian", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "CasualStationaryAi", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(typeName, "CasualAi", StringComparison.OrdinalIgnoreCase))
                return true;

            // Runtime customers often have concrete names like HairdresserCustomer or NightclubCustomer.
            // UI/helper classes like CustomerCapacityShelf should not match.
            if (typeName.EndsWith("Customer", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static IEnumerable<Renderer> EnumerateNpcFallbackRenderers()
        {
            Renderer[] renderers;
            try
            {
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(false);
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"FindObjectsOfType<Renderer> NPC fallback failed: {ex.GetType().Name}: {ex.Message}");
                yield break;
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (!LooksLikeNpcClothingRenderer(renderer))
                    continue;

                yield return renderer;
            }
        }

        private static bool LooksLikeNpcClothingRenderer(Renderer renderer)
        {
            var path = GetPath(renderer.transform, 10);
            if (ContainsAnyToken(path, NpcSkipPathTokens) || ContainsAnyToken(path, NpcFallbackPathDenyTokens))
                return false;

            var rootName = renderer.transform.root != null ? renderer.transform.root.name : string.Empty;
            if (ContainsAnyToken(rootName, new[] { "canvas", "ui", "preview", "thumbnail" }))
                return false;

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                    continue;

                var materialName = material.name ?? string.Empty;
                var shaderName = material.shader != null ? material.shader.name : string.Empty;
                var slotText = materialName + " " + shaderName;

                if (ContainsAnyToken(slotText, NpcHardDenyTokens))
                    continue;

                // The earlier fallback also accepted generic tokens like top/cloth, which matched
                // rooftops, facade cloth materials, and building props. The fallback now requires
                // actual character clothing shaders or known character clothing material prefixes.
                if (shaderName.IndexOf("CharacterClothes", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                if (ContainsAnyToken(materialName, NpcFallbackStrictMaterialTokens))
                    return true;

                if (ContainsAnyToken(materialName, new[]
                    {
                        "m_hgshirt", "m_shirt", "m_openedshirtlines", "m_highneckshirt", "m_highneckshirt_female",
                        "m_sweater", "m_hoodie", "m_jacket", "m_polo"
                    }))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<GameObject> FindByTagSafe(string tag)
        {
            try
            {
                return GameObject.FindGameObjectsWithTag(tag);
            }
            catch (UnityException)
            {
                PinkFileLogger.Verbose($"Tag not present in this scene/build: {tag}");
                return Array.Empty<GameObject>();
            }
        }

        private static GameObject? ResolveSafeRoot(GameObject source, RootKind kind, string sourceReason)
        {
            if (source == null)
                return null;

            var current = source.transform;
            var best = current;

            var sourceName = current.name ?? string.Empty;
            if (kind == RootKind.Vehicle && LooksLikeVehicleRoot(sourceName))
                best = current;
            else if (kind == RootKind.Npc && LooksLikeNpcRoot(sourceName))
                best = current;

            while (current.parent != null)
            {
                var parent = current.parent;
                var parentName = parent.name ?? string.Empty;

                if (ContainsAnyToken(parentName, HardRejectRootNameTokens))
                    break;

                if (kind == RootKind.Vehicle && LooksLikeVehicleRoot(parentName))
                    best = parent;
                else if (kind == RootKind.Npc && LooksLikeNpcRoot(parentName) && ShouldPromoteNpcRoot(best, parent))
                    best = parent;

                current = parent;
            }

            var rootObject = best.gameObject;
            if (IsRejectedRoot(rootObject, kind, sourceReason))
                return null;

            return rootObject;
        }

        private static bool IsRejectedRoot(GameObject root, RootKind kind, string sourceReason)
        {
            var rootName = root.name ?? string.Empty;
            if (ContainsAnyToken(rootName, HardRejectRootNameTokens))
            {
                LogRejectedRoot(root, kind, sourceReason, "hardRejectRootName");
                return true;
            }

            if (kind == RootKind.Npc)
            {
                var path = GetPath(root.transform, 10);
                if (ContainsAnyToken(path, NpcSkipPathTokens))
                {
                    LogRejectedRoot(root, kind, sourceReason, "npcVendorOrCarnivalPath");
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeVehicleRoot(string value)
        {
            return ContainsAnyToken(value, new[] { "vehicle", "car", "truck", "van", "taxi", "bus", "traffic", "flatbed" });
        }

        private static bool LooksLikeNpcRoot(string value)
        {
            return ContainsAnyToken(value, new[] { "npc", "pedestrian", "customer", "employee", "citizen", "person", "human", "character" });
        }

        private static bool ShouldPromoteNpcRoot(Transform currentBest, Transform candidateParent)
        {
            var currentName = currentBest.name ?? string.Empty;
            var candidateName = candidateParent.name ?? string.Empty;

            if (!LooksLikeNpcRoot(currentName))
                return true;

            if (IsGenericNpcContainerName(candidateName) && !IsGenericNpcContainerName(currentName))
                return false;

            return true;
        }

        private static bool IsGenericNpcContainerName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            return string.Equals(trimmed, "Pedestrian", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Pedestrians", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "NPC", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "NPCs", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Customer", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Customers", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Employee", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Employees", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Citizen", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Citizens", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Human", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Humans", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Character", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(trimmed, "Characters", StringComparison.OrdinalIgnoreCase);
        }

        private static void LogRejectedRoot(GameObject root, RootKind kind, string sourceReason, string rejectReason)
        {
            var id = root.GetInstanceID();
            if (!LoggedRejectedRoots.Add(id))
                return;

            PinkFileLogger.Verbose($"Rejected {kind} root: reason={rejectReason}, source={sourceReason}, path={GetPath(root.transform, 8)}");
        }


        private static void CountBehaviourHit(string typeName)
        {
            if (!BehaviourTypeHitCounts.TryGetValue(typeName, out var count))
                count = 0;

            BehaviourTypeHitCounts[typeName] = count + 1;
        }

        private static void LogBehaviourTypeSummary()
        {
            if (BehaviourTypeHitCounts.Count == 0)
            {
                PinkFileLogger.Info("Behaviour type summary: no vehicle/NPC-like MonoBehaviour type names matched.");
                return;
            }

            var parts = new List<string>();
            foreach (var pair in BehaviourTypeHitCounts)
                parts.Add(pair.Key + "=" + pair.Value);

            PinkFileLogger.Info("Behaviour type summary: " + string.Join(", ", parts.ToArray()));
        }
    }
}
