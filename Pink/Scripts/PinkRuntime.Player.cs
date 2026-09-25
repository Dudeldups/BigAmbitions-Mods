#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pink
{
    internal static partial class PinkRuntime
    {
        private static readonly Dictionary<int, PlayerTextureSnapshot> PatchedPlayerTextures = new Dictionary<int, PlayerTextureSnapshot>();
        private static Texture2D? PlayerPinkTexture;

        private static int TintPlayerUpperBodyIfAvailable()
        {
            var patched = 0;
            var seenRoots = new HashSet<int>();

            foreach (var root in EnumeratePlayerRoots())
            {
                if (root == null || !seenRoots.Add(root.GetInstanceID()))
                    continue;

                patched += TintPlayerUpperBodyRoot(root);
            }

            PinkFileLogger.Info($"PLAYER_UPPER_BODY_TINT patchedSlots={patched}");
            return patched;
        }

        private static IEnumerable<GameObject> EnumeratePlayerRoots()
        {
            var fromHelper = TryResolvePlayerRootFromPlayerHelper();
            if (fromHelper != null)
                yield return fromHelper;

            foreach (var tagged in FindByTagSafe("Player"))
            {
                if (tagged != null)
                    yield return tagged;
            }

            var fallbackNames = new[]
            {
                "GameManager/PlayerController",
                "GameManager/Player",
                "PlayerController",
                "Player",
                "Player(Clone)",
                "MainPlayer"
            };

            for (var index = 0; index < fallbackNames.Length; index++)
            {
                GameObject? go = null;
                try
                {
                    go = GameObject.Find(fallbackNames[index]);
                }
                catch
                {
                    // Ignore missing/invalid scene lookups.
                }

                if (go != null)
                    yield return go;
            }
        }

        private static GameObject? TryResolvePlayerRootFromPlayerHelper()
        {
            var helperType = FindRuntimeTypeBySimpleName("PlayerHelper");
            if (helperType == null)
                return null;

            var memberNames = new[]
            {
                "PlayerController",
                "playerController",
                "CurrentPlayerController",
                "currentPlayerController",
                "Player",
                "player"
            };

            for (var index = 0; index < memberNames.Length; index++)
            {
                var value = TryGetStaticMemberValue(helperType, memberNames[index]);
                var root = TryConvertToGameObject(value);
                if (root != null)
                    return root;
            }

            return null;
        }

        private static Type? FindRuntimeTypeBySimpleName(string simpleName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                var assembly = assemblies[assemblyIndex];
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = Array.FindAll(ex.Types, type => type != null)!;
                }
                catch
                {
                    continue;
                }

                for (var typeIndex = 0; typeIndex < types.Length; typeIndex++)
                {
                    var type = types[typeIndex];
                    if (type == null)
                        continue;

                    if (string.Equals(type.Name, simpleName, StringComparison.Ordinal) ||
                        string.Equals(type.FullName, simpleName, StringComparison.Ordinal) ||
                        (type.FullName != null && type.FullName.EndsWith("." + simpleName, StringComparison.Ordinal)))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static object? TryGetStaticMemberValue(Type type, string memberName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            try
            {
                var property = type.GetProperty(memberName, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(null, null);
            }
            catch
            {
                // Try field next.
            }

            try
            {
                var field = type.GetField(memberName, flags);
                if (field != null)
                    return field.GetValue(null);
            }
            catch
            {
                // Ignore.
            }

            return null;
        }

        private static GameObject? TryConvertToGameObject(object? value)
        {
            if (value == null)
                return null;

            if (value is GameObject gameObject)
                return gameObject;

            if (value is Component component)
                return component.gameObject;

            if (value is Transform transform)
                return transform.gameObject;

            return null;
        }

        private static int TintPlayerUpperBodyRoot(GameObject root)
        {
            var patched = 0;
            var renderers = root.GetComponentsInChildren<Renderer>(false);

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                LogPlayerRendererDiagnostic(root, renderer, index);

                if (!LooksLikePlayerUpperBodyRenderer(root, renderer))
                    continue;

                patched += ForceTintPlayerUpperBodyRenderer(renderer);
            }

            PinkFileLogger.Info($"PLAYER_UPPER_BODY_ROOT path={GetPath(root.transform, 8)} renderers={renderers.Length} patchedSlots={patched}");
            return patched;
        }

        private static int ForceTintPlayerUpperBodyRenderer(Renderer renderer)
        {
            var patched = 0;
            Material[] materials;

            try
            {
                materials = renderer.materials;
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"PLAYER_UPPER_BODY renderer.materials failed: renderer={GetPath(renderer.transform, 8)}, error={ex.GetType().Name}: {ex.Message}");
                return 0;
            }

            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;

                if (!ShouldTintPlayerUpperBodyMaterial(renderer, material))
                    continue;

                ForceTintMaterialAndPropertyBlock(renderer, index, material, FixedBrightPink);
                var textureOverrides = ForcePlayerUpperBodyTexture(material);
                patched++;

                PinkFileLogger.Info(
                    $"PLAYER_UPPER_BODY_SLOT renderer={GetPath(renderer.transform, 8)}, slot={index}, material={material.name}, textureOverrides={textureOverrides}");
            }

            return patched;
        }

        private static bool ShouldTintPlayerUpperBodyMaterial(Renderer renderer, Material material)
        {
            var rendererPath = GetPath(renderer.transform, 10);
            var materialName = material.name ?? string.Empty;
            var shaderName = material.shader != null ? material.shader.name : string.Empty;
            var combined = rendererPath + " " + materialName + " " + shaderName;

            if (!ContainsAnyToken(rendererPath, new[] { "/torso/top", "torso/top", "/model/female/torso/top", "/model/male/torso/top" }))
                return false;

            if (ContainsAnyToken(materialName, NpcHardDenyTokens) || ContainsAnyToken(shaderName, new[] { "skin", "hair", "face" }))
                return false;

            if (ContainsAnyToken(materialName, new[] { "m_top", "shirt", "hoodie", "sweater", "jacket", "polo", "suit" }))
                return true;

            return shaderName.IndexOf("CharacterClothes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   shaderName.IndexOf("CharacterClothing", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ForceTintMaterialAndPropertyBlock(Renderer renderer, int materialIndex, Material material, Color pinkColor)
        {
            var supportedColorPropertyIds = GetSupportedColorPropertyIds(material);
            var changedProperties = new List<MaterialColorProperty>();

            for (var index = 0; index < supportedColorPropertyIds.Count; index++)
            {
                var propertyId = supportedColorPropertyIds[index];
                if (!material.HasProperty(propertyId))
                    continue;

                changedProperties.Add(new MaterialColorProperty(propertyId, material.GetColor(propertyId)));
            }

            var materialId = material.GetInstanceID();
            if (changedProperties.Count > 0 && !PatchedMaterials.ContainsKey(materialId))
                PatchedMaterials[materialId] = new MaterialColorSnapshot(material, changedProperties.ToArray());

            for (var index = 0; index < supportedColorPropertyIds.Count; index++)
            {
                var propertyId = supportedColorPropertyIds[index];
                if (material.HasProperty(propertyId))
                    material.SetColor(propertyId, pinkColor);
            }

            if (supportedColorPropertyIds.Count == 0)
                return;

            var key = new RendererSlotKey(renderer.GetInstanceID(), materialIndex);
            if (!PatchedRendererSlots.ContainsKey(key))
            {
                var originalBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(originalBlock, materialIndex);
                PatchedRendererSlots[key] = new RendererPropertyBlockSnapshot(renderer, materialIndex, originalBlock);
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block, materialIndex);
            for (var index = 0; index < supportedColorPropertyIds.Count; index++)
                block.SetColor(supportedColorPropertyIds[index], pinkColor);

            renderer.SetPropertyBlock(block, materialIndex);
        }

        private static int ForcePlayerUpperBodyTexture(Material material)
        {
            var texture = GetSolidColorTexture(ref PlayerPinkTexture, "PinkCity_PlayerTop_PinkTexture", FixedBrightPink);
            return ForceMaterialTexture(material, texture, PatchedPlayerTextures, "PLAYER_TEXTURE");
        }

        private static int ForceMaterialTexture(
            Material material,
            Texture2D texture,
            Dictionary<int, PlayerTextureSnapshot> snapshotMap,
            string logPrefix)
        {
            var shader = material.shader;
            if (shader == null)
                return 0;

            var changed = new List<MaterialTextureProperty>();
            try
            {
                var count = shader.GetPropertyCount();
                for (var index = 0; index < count; index++)
                {
                    if (shader.GetPropertyType(index) != ShaderPropertyType.Texture)
                        continue;

                    var propertyName = shader.GetPropertyName(index);
                    if (!ShouldOverridePlayerTextureProperty(shader, index, propertyName))
                        continue;

                    var propertyId = Shader.PropertyToID(propertyName);
                    if (!material.HasProperty(propertyId))
                        continue;

                    changed.Add(new MaterialTextureProperty(propertyId, material.GetTexture(propertyId)));
                }
            }
            catch (Exception ex)
            {
                PinkFileLogger.Warn($"{logPrefix} property discovery failed: material={material.name}, error={ex.GetType().Name}: {ex.Message}");
            }

            if (changed.Count == 0)
                return 0;

            var materialId = material.GetInstanceID();
            if (!snapshotMap.ContainsKey(materialId))
                snapshotMap[materialId] = new PlayerTextureSnapshot(material, changed.ToArray());

            for (var index = 0; index < changed.Count; index++)
                material.SetTexture(changed[index].PropertyId, texture);

            return changed.Count;
        }

        private static bool ShouldOverridePlayerTextureProperty(Shader shader, int propertyIndex, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return false;

            if (ContainsAnyToken(propertyName, new[] { "normal", "bump", "mask", "metal", "rough", "smooth", "occlusion", "ao", "spec", "emission", "height", "detail", "noise" }))
                return false;

            try
            {
                var dimension = shader.GetPropertyTextureDimension(propertyIndex);
                if (dimension != TextureDimension.Tex2D)
                    return false;
            }
            catch
            {
                // If dimension lookup fails, keep going by name.
            }

            return ContainsAnyToken(propertyName, new[] { "base", "main", "albedo", "diffuse", "color", "map", "tex" });
        }

        private static Texture2D GetSolidColorTexture(ref Texture2D? cache, string name, Color color)
        {
            if (cache != null)
                return cache;

            cache = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            cache.SetPixel(0, 0, color);
            cache.Apply(false, false);
            return cache;
        }

        private static bool LooksLikePlayerUpperBodyRenderer(GameObject root, Renderer renderer)
        {
            var path = GetPath(renderer.transform, 10);

            if (ContainsAnyToken(path, new[] { "/torso/top", "torso/top", "/model/female/torso/top", "/model/male/torso/top" }))
                return true;

            var rendererName = renderer.name ?? string.Empty;
            var parentName = renderer.transform.parent != null ? renderer.transform.parent.name : string.Empty;
            var materialNames = string.Empty;
            var shaderNames = string.Empty;

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                    continue;

                materialNames += " " + (material.name ?? string.Empty);
                shaderNames += " " + (material.shader != null ? material.shader.name : string.Empty);
            }

            var combined = path + " " + rendererName + " " + parentName + " " + materialNames + " " + shaderNames;

            if (ContainsAnyToken(combined, NpcHardDenyTokens))
                return false;

            if (ContainsAnyToken(combined, NpcAllowTokens))
                return true;

            if (ContainsAnyToken(materialNames, NpcFallbackStrictMaterialTokens))
                return true;

            if (shaderNames.IndexOf("CharacterClothes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderNames.IndexOf("CharacterClothing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderNames.IndexOf("Cloth", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            try
            {
                var relativeCenterY = renderer.bounds.center.y - root.transform.position.y;
                var sizeY = renderer.bounds.size.y;
                if (relativeCenterY >= 0.65f && relativeCenterY <= 1.65f && sizeY <= 1.45f)
                    return true;
            }
            catch
            {
                // Bounds can fail for some renderers; ignore.
            }

            return false;
        }

        private static void LogPlayerRendererDiagnostic(GameObject root, Renderer renderer, int index)
        {
            if (!PinkFileLogger.Enabled)
                return;

            var materialNames = string.Empty;
            var shaderNames = string.Empty;

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                    continue;

                materialNames += (materialNames.Length == 0 ? string.Empty : "|") + (material.name ?? "<unnamed>");
                shaderNames += (shaderNames.Length == 0 ? string.Empty : "|") + (material.shader != null ? material.shader.name : "<null>");
            }

            var relativeCenterY = 0f;
            var sizeY = 0f;
            try
            {
                relativeCenterY = renderer.bounds.center.y - root.transform.position.y;
                sizeY = renderer.bounds.size.y;
            }
            catch
            {
                // Keep defaults.
            }

            PinkFileLogger.Info(
                $"PLAYER_RENDERER_DIAG {index}: path={GetPath(renderer.transform, 10)}, renderer={renderer.name}, " +
                $"materials={materialNames}, shaders={shaderNames}, relativeCenterY={relativeCenterY:0.00}, sizeY={sizeY:0.00}");
        }

        private static int RestorePlayerTextureOverrides()
        {
            var restored = 0;
            restored += RestoreTextureSnapshotMap(PatchedPlayerTextures);
            return restored;
        }

        private static int RestoreTextureSnapshotMap(Dictionary<int, PlayerTextureSnapshot> snapshotMap)
        {
            var restored = 0;
            foreach (var snapshot in snapshotMap.Values)
            {
                if (snapshot.Material == null)
                    continue;

                foreach (var property in snapshot.Properties)
                {
                    if (snapshot.Material.HasProperty(property.PropertyId))
                    {
                        snapshot.Material.SetTexture(property.PropertyId, property.OriginalTexture);
                        restored++;
                    }
                }
            }

            snapshotMap.Clear();
            return restored;
        }

        private static void ResetPlayerTextureState()
        {
            PatchedPlayerTextures.Clear();
            PlayerPinkTexture = null;
        }

        private readonly struct PlayerTextureSnapshot
        {
            internal PlayerTextureSnapshot(Material material, MaterialTextureProperty[] properties)
            {
                Material = material;
                Properties = properties;
            }

            internal Material Material { get; }
            internal MaterialTextureProperty[] Properties { get; }
        }

        private readonly struct MaterialTextureProperty
        {
            internal MaterialTextureProperty(int propertyId, Texture? originalTexture)
            {
                PropertyId = propertyId;
                OriginalTexture = originalTexture;
            }

            internal int PropertyId { get; }
            internal Texture? OriginalTexture { get; }
        }
    }
}
