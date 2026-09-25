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
        private static Color GetPinkColorFor(Renderer renderer, Material material, int materialIndex, RootKind kind, bool strictVehicleFallback)
        {
            var key = GetStablePinkKey(renderer, material, kind, strictVehicleFallback);
            var index = PositiveStableHash(key) % PinkPalette.Length;
            return PinkPalette[index];
        }

        private static string GetStablePinkKey(Renderer renderer, Material material, RootKind kind, bool strictVehicleFallback)
        {
            var materialName = NormalizeName(material.name);

            if (kind == RootKind.Vehicle)
            {
                if (strictVehicleFallback)
                {
                    // Parked/static vehicles appear to switch renderer/LOD while the player drives past.
                    // Use a quantized world-position key so all LOD variants at the same parked location
                    // receive the same pink tone. Do not include renderer/material IDs here.
                    return "vehicle-fallback-position|" + GetQuantizedBoundsPositionKey(renderer);
                }

                return "vehicle-root|" + FindStableVehicleOwnerName(renderer.transform);
            }

            return "npc|" + FindStableNpcOwnerName(renderer.transform) + "|" + materialName;
        }

        private static string GetQuantizedBoundsPositionKey(Renderer renderer)
        {
            var center = renderer.bounds.center;
            const float bucketSize = 5f;

            var x = Mathf.RoundToInt(center.x / bucketSize);
            var y = Mathf.RoundToInt(center.y / bucketSize);
            var z = Mathf.RoundToInt(center.z / bucketSize);

            return x + ":" + y + ":" + z;
        }

        private static string FindStableVehicleOwnerName(Transform transform)
        {
            var current = transform;
            while (current != null)
            {
                var name = current.name ?? string.Empty;
                if (ContainsAnyToken(name, new[] { "vehicle", "truck", "flatbed", "taxi", "saloon", "sedan", "suv", "pickup", "delivery", "van" }))
                    return NormalizeName(name);

                current = current.parent;
            }

            if (transform.root != null)
                return NormalizeName(transform.root.name);

            return NormalizeName(transform.name);
        }

        private static string FindStableNpcOwnerName(Transform transform)
        {
            var current = transform;
            Transform? best = transform.root;

            while (current != null)
            {
                var name = current.name ?? string.Empty;
                if (LooksLikeNpcRoot(name) && !IsGenericNpcContainerName(name))
                    best = current;

                current = current.parent;
            }

            if (best != null)
                return NormalizeName(GetPath(best, 4));

            return NormalizeName(transform.root != null ? transform.root.name : transform.name);
        }

        private static string NormalizeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return value
                .Replace("(Instance)", string.Empty)
                .Replace("(Clone)", string.Empty)
                .Trim()
                .ToLowerInvariant();
        }

        private static int PositiveStableHash(string value)
        {
            unchecked
            {
                var hash = (int)2166136261;
                for (var index = 0; index < value.Length; index++)
                    hash = (hash ^ value[index]) * 16777619;

                return hash & 0x7fffffff;
            }
        }

        private static bool TryTintMaterialBlend(Material material, Color pinkColor, float strength)
        {
            var changedProperties = new List<MaterialColorProperty>();
            for (var index = 0; index < CandidateColorPropertyIds.Length; index++)
            {
                var propertyId = CandidateColorPropertyIds[index];
                if (!material.HasProperty(propertyId))
                    continue;

                changedProperties.Add(new MaterialColorProperty(propertyId, material.GetColor(propertyId)));
            }

            if (changedProperties.Count == 0)
                return false;

            var materialId = material.GetInstanceID();
            var isNewPatch = !PatchedMaterials.ContainsKey(materialId);
            if (isNewPatch)
                PatchedMaterials[materialId] = new MaterialColorSnapshot(material, changedProperties.ToArray());

            foreach (var property in changedProperties)
                material.SetColor(property.PropertyId, Color.Lerp(property.OriginalColor, pinkColor, strength));

            return isNewPatch;
        }

        private static bool TryTintRendererPropertyBlockBlend(Renderer renderer, int materialIndex, Material material, Color pinkColor, float strength)
        {
            var supportedPropertyIds = GetSupportedColorPropertyIds(material);
            if (supportedPropertyIds.Count == 0)
                return false;

            var key = new RendererSlotKey(renderer.GetInstanceID(), materialIndex);
            if (PatchedRendererSlots.ContainsKey(key))
                return false;

            var originalBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(originalBlock, materialIndex);
            PatchedRendererSlots[key] = new RendererPropertyBlockSnapshot(renderer, materialIndex, originalBlock);

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, materialIndex);
            for (var index = 0; index < supportedPropertyIds.Count; index++)
            {
                var propertyId = supportedPropertyIds[index];
                var baseColor = material.HasProperty(propertyId) ? material.GetColor(propertyId) : Color.white;
                block.SetColor(propertyId, Color.Lerp(baseColor, pinkColor, strength));
            }

            renderer.SetPropertyBlock(block, materialIndex);
            return true;
        }

        private static bool TryTintMaterial(Material material, Color pinkColor)
        {
            var changedProperties = new List<MaterialColorProperty>();
            for (var index = 0; index < CandidateColorPropertyIds.Length; index++)
            {
                var propertyId = CandidateColorPropertyIds[index];
                if (!material.HasProperty(propertyId))
                    continue;

                changedProperties.Add(new MaterialColorProperty(propertyId, material.GetColor(propertyId)));
            }

            if (changedProperties.Count == 0)
                return false;

            var materialId = material.GetInstanceID();
            var isNewPatch = !PatchedMaterials.ContainsKey(materialId);
            if (isNewPatch)
                PatchedMaterials[materialId] = new MaterialColorSnapshot(material, changedProperties.ToArray());

            foreach (var property in changedProperties)
                material.SetColor(property.PropertyId, pinkColor);

            return isNewPatch;
        }

        private static bool TryTintRendererPropertyBlock(Renderer renderer, int materialIndex, Material material, Color pinkColor)
        {
            var supportedPropertyIds = GetSupportedColorPropertyIds(material);
            if (supportedPropertyIds.Count == 0)
                return false;

            var key = new RendererSlotKey(renderer.GetInstanceID(), materialIndex);
            if (PatchedRendererSlots.ContainsKey(key))
                return false;

            var originalBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(originalBlock, materialIndex);
            PatchedRendererSlots[key] = new RendererPropertyBlockSnapshot(renderer, materialIndex, originalBlock);

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, materialIndex);
            for (var index = 0; index < supportedPropertyIds.Count; index++)
                block.SetColor(supportedPropertyIds[index], pinkColor);

            renderer.SetPropertyBlock(block, materialIndex);
            return true;
        }

        private static List<int> GetSupportedColorPropertyIds(Material material)
        {
            var supported = new List<int>();

            var shader = material.shader;
            if (shader != null)
            {
                var shaderId = shader.GetInstanceID();
                if (!ShaderColorPropertyIdCache.TryGetValue(shaderId, out var shaderPropertyIds))
                {
                    shaderPropertyIds = DiscoverShaderColorPropertyIds(shader);
                    ShaderColorPropertyIdCache[shaderId] = shaderPropertyIds;
                }

                for (var index = 0; index < shaderPropertyIds.Length; index++)
                {
                    var propertyId = shaderPropertyIds[index];
                    if (material.HasProperty(propertyId) && !supported.Contains(propertyId))
                        supported.Add(propertyId);
                }
            }

            // Fallback for shaders where runtime property enumeration is incomplete or unavailable.
            for (var index = 0; index < CandidateColorPropertyIds.Length; index++)
            {
                var propertyId = CandidateColorPropertyIds[index];
                if (material.HasProperty(propertyId) && !supported.Contains(propertyId))
                    supported.Add(propertyId);
            }

            return supported;
        }

        private static int[] DiscoverShaderColorPropertyIds(Shader shader)
        {
            var ids = new List<int>();

            try
            {
                var count = shader.GetPropertyCount();
                for (var index = 0; index < count; index++)
                {
                    if (shader.GetPropertyType(index) != ShaderPropertyType.Color)
                        continue;

                    var propertyName = shader.GetPropertyName(index);
                    if (ContainsAnyToken(propertyName, ShaderColorPropertyDenyTokens))
                        continue;

                    var propertyId = Shader.PropertyToID(propertyName);
                    if (!ids.Contains(propertyId))
                        ids.Add(propertyId);
                }
            }
            catch (Exception ex)
            {
                PinkFileLogger.Verbose($"Shader property discovery failed for shader={shader.name}: {ex.GetType().Name}: {ex.Message}");
            }

            if (ids.Count > 0)
                PinkFileLogger.Verbose($"Shader color properties discovered: shader={shader.name}, count={ids.Count}");

            return ids.ToArray();
        }

        private static bool ContainsAnyToken(string? value, string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            for (var index = 0; index < tokens.Length; index++)
            {
                if (value.IndexOf(tokens[index], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static string GetPath(Transform? transform, int maxDepth)
        {
            if (transform == null)
                return "<null>";

            var names = new List<string>();
            var current = transform;
            var depth = 0;
            while (current != null && depth < maxDepth)
            {
                names.Add(current.name);
                current = current.parent;
                depth++;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private readonly struct MaterialColorSnapshot
        {
            internal MaterialColorSnapshot(Material material, MaterialColorProperty[] properties)
            {
                Material = material;
                Properties = properties;
            }

            internal Material Material { get; }
            internal MaterialColorProperty[] Properties { get; }
        }

        private readonly struct MaterialColorProperty
        {
            internal MaterialColorProperty(int propertyId, Color originalColor)
            {
                PropertyId = propertyId;
                OriginalColor = originalColor;
            }

            internal int PropertyId { get; }
            internal Color OriginalColor { get; }
        }

        private readonly struct RendererSlotKey : IEquatable<RendererSlotKey>
        {
            internal RendererSlotKey(int rendererId, int materialIndex)
            {
                RendererId = rendererId;
                MaterialIndex = materialIndex;
            }

            private int RendererId { get; }
            private int MaterialIndex { get; }

            public bool Equals(RendererSlotKey other)
            {
                return RendererId == other.RendererId && MaterialIndex == other.MaterialIndex;
            }

            public override bool Equals(object? obj)
            {
                return obj is RendererSlotKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (RendererId * 397) ^ MaterialIndex;
                }
            }
        }

        private readonly struct RendererPropertyBlockSnapshot
        {
            internal RendererPropertyBlockSnapshot(Renderer renderer, int materialIndex, MaterialPropertyBlock originalBlock)
            {
                Renderer = renderer;
                MaterialIndex = materialIndex;
                OriginalBlock = originalBlock;
            }

            internal Renderer Renderer { get; }
            internal int MaterialIndex { get; }
            internal MaterialPropertyBlock OriginalBlock { get; }
        }
    }
}
