#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;
using UnityEngine.Rendering;

namespace MootorVehicle
{
    internal readonly struct CowMaterialFixResult
    {
        internal CowMaterialFixResult(
            int rendererCount,
            int decalMasksCleared,
            int hdrpMaterialsFixed,
            int hdrpMaterialsValidated,
            string shaderNames)
        {
            RendererCount = rendererCount;
            DecalMasksCleared = decalMasksCleared;
            HdrpMaterialsFixed = hdrpMaterialsFixed;
            HdrpMaterialsValidated = hdrpMaterialsValidated;
            ShaderNames = shaderNames;
        }

        internal int RendererCount { get; }
        internal int DecalMasksCleared { get; }
        internal int HdrpMaterialsFixed { get; }
        internal int HdrpMaterialsValidated { get; }
        internal string ShaderNames { get; }
    }

    internal static class MootorVehicleMaterials
    {
        private const uint HdrpDecalLayerMask = 0x0000FF00u;
        private const string HdMaterialTypeName = "UnityEngine.Rendering.HighDefinition.HDMaterial";
        private const string ShaderGraphApiTypeName = "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";
        private static MethodInfo? validateMaterialMethod;
        private static bool validateMaterialMethodResolved;
        private static MethodInfo? validateShaderGraphMaterialMethod;
        private static bool validateShaderGraphMaterialMethodResolved;

        internal static CowMaterialFixResult FixSolidCowMaterials(GameObject cowVisual)
        {
            var renderers = cowVisual.GetComponentsInChildren<Renderer>(true);
            var materials = new HashSet<Material>();
            var shaderNames = new HashSet<string>(StringComparer.Ordinal);
            var decalMasksCleared = 0;
            var hdrpMaterialsFixed = 0;
            var hdrpMaterialsValidated = 0;

            foreach (var renderer in renderers)
            {
                var previousMask = renderer.renderingLayerMask;
                renderer.renderingLayerMask &= ~HdrpDecalLayerMask;
                if (renderer.renderingLayerMask != previousMask)
                    decalMasksCleared++;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !materials.Add(material))
                        continue;

                    var originalShaderName = material.shader != null ? material.shader.name : "<null>";
                    if (!IsHdrpMaterial(material, originalShaderName))
                    {
                        shaderNames.Add(originalShaderName);
                        continue;
                    }

                    RebindToHdrpLit(material);

                    if (FixSolidHdrpMaterial(material))
                        hdrpMaterialsValidated++;

                    hdrpMaterialsFixed++;
                    var finalShaderName = material.shader != null ? material.shader.name : "<null>";
                    shaderNames.Add(string.Equals(originalShaderName, finalShaderName, StringComparison.Ordinal)
                        ? finalShaderName
                        : originalShaderName + "->" + finalShaderName);
                }
            }

            var orderedShaderNames = new List<string>(shaderNames);
            orderedShaderNames.Sort(StringComparer.Ordinal);
            return new CowMaterialFixResult(
                renderers.Length,
                decalMasksCleared,
                hdrpMaterialsFixed,
                hdrpMaterialsValidated,
                string.Join("|", orderedShaderNames));
        }

        private static bool IsHdrpMaterial(Material material, string shaderName)
        {
            if (shaderName.StartsWith("HDRP/", StringComparison.Ordinal))
                return true;

            var shaderGraphTarget = material.GetTag("ShaderGraphTargetId", false, string.Empty);
            return shaderGraphTarget.StartsWith("HD", StringComparison.Ordinal) ||
                   material.HasProperty("_SupportDecals");
        }

        private static bool FixSolidHdrpMaterial(Material material)
        {
            SetOpaqueColor(material, "_BaseColor");
            SetOpaqueColor(material, "baseColorFactor");

            SetFloat(material, "_SurfaceType", 0f);
            SetFloat(material, "_AlphaCutoffEnable", 0f);
            SetFloat(material, "_SupportDecals", 0f);
            SetFloat(material, "_ReceivesSSR", 0f);
            SetFloat(material, "_ReceivesSSRTransparent", 0f);
            SetFloat(material, "_RefractionModel", 0f);
            material.renderQueue = (int)RenderQueue.Geometry;
            material.SetOverrideTag("RenderType", "Opaque");

            var validated = TryValidateHdrpMaterial(material);

            // Keep the critical keywords explicit if HDRP validation is unavailable in a future build.
            material.EnableKeyword("_DISABLE_DECALS");
            material.EnableKeyword("_DISABLE_SSR");
            material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
            SetFloat(material, "_ZWrite", 1f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.One);
            SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
            return validated;
        }

        private static void RebindToHdrpLit(Material material)
        {
            var hdrpLit = Shader.Find("HDRP/Lit") ??
                          Shader.Find("High Definition Render Pipeline/Lit");
            if (hdrpLit == null || material.shader == hdrpLit)
                return;

            var baseColor = GetColor(material, "baseColorFactor", "_BaseColor", Color.white);
            baseColor.a = 1f;
            var baseTextureProperty = FirstTextureProperty(material, "baseColorTexture", "_BaseColorMap", "_MainTex");
            var baseTexture = baseTextureProperty != null ? material.GetTexture(baseTextureProperty) : null;
            var baseScale = baseTextureProperty != null ? material.GetTextureScale(baseTextureProperty) : Vector2.one;
            var baseOffset = baseTextureProperty != null ? material.GetTextureOffset(baseTextureProperty) : Vector2.zero;

            var normalTextureProperty = FirstTextureProperty(material, "normalTexture", "_NormalMap");
            var normalTexture = normalTextureProperty != null ? material.GetTexture(normalTextureProperty) : null;
            var normalScale = GetFloat(material, "normalTexture_scale", "normalScale", "_NormalScale", 1f);
            var metallic = GetFloat(material, "metallicFactor", "_Metallic", null, 0f);
            var roughness = GetFloat(material, "roughnessFactor", null, null, 1f);

            material.shader = hdrpLit;
            SetColor(material, "_BaseColor", baseColor);
            SetTexture(material, "_BaseColorMap", baseTexture, baseScale, baseOffset);
            SetTexture(material, "_NormalMap", normalTexture, Vector2.one, Vector2.zero);
            SetFloat(material, "_NormalScale", normalScale);
            SetFloat(material, "_Metallic", metallic);
            SetFloat(material, "_Smoothness", 1f - Mathf.Clamp01(roughness));
        }

        private static string? FirstTextureProperty(Material material, params string[] properties)
        {
            foreach (var property in properties)
                if (material.HasProperty(property) && material.GetTexture(property) != null)
                    return property;
            return null;
        }

        private static Color GetColor(
            Material material,
            string firstProperty,
            string secondProperty,
            Color fallback)
        {
            if (material.HasProperty(firstProperty))
                return material.GetColor(firstProperty);
            if (material.HasProperty(secondProperty))
                return material.GetColor(secondProperty);
            return fallback;
        }

        private static float GetFloat(
            Material material,
            string firstProperty,
            string? secondProperty,
            string? thirdProperty,
            float fallback)
        {
            if (material.HasProperty(firstProperty))
                return material.GetFloat(firstProperty);
            if (secondProperty != null && material.HasProperty(secondProperty))
                return material.GetFloat(secondProperty);
            if (thirdProperty != null && material.HasProperty(thirdProperty))
                return material.GetFloat(thirdProperty);
            return fallback;
        }

        private static void SetColor(Material material, string property, Color value)
        {
            if (material.HasProperty(property))
                material.SetColor(property, value);
        }

        private static void SetTexture(
            Material material,
            string property,
            Texture? texture,
            Vector2 scale,
            Vector2 offset)
        {
            if (!material.HasProperty(property))
                return;

            material.SetTexture(property, texture);
            material.SetTextureScale(property, scale);
            material.SetTextureOffset(property, offset);
        }

        private static void SetOpaqueColor(Material material, string property)
        {
            if (!material.HasProperty(property))
                return;

            var color = material.GetColor(property);
            color.a = 1f;
            material.SetColor(property, color);
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }

        private static bool TryValidateHdrpMaterial(Material material)
        {
            try
            {
                var method = ResolveValidateMaterialMethod();
                if (method != null)
                {
                    var result = method.Invoke(null, new object[] { material });
                    if (result is not bool validated || validated)
                        return true;
                }

                // glTFast's HDRP shader graph does not expose the target tag expected by
                // HDMaterial.ValidateMaterial. Invoke HDRP's shader-graph validator directly
                // so its decal keywords, passes, and stencil state are synchronized.
                method = ResolveValidateShaderGraphMaterialMethod();
                if (method == null)
                    return false;

                method.Invoke(null, new object[] { material });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo? ResolveValidateMaterialMethod()
        {
            if (validateMaterialMethodResolved)
                return validateMaterialMethod;

            validateMaterialMethodResolved = true;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(HdMaterialTypeName, false);
                if (type == null)
                    continue;

                validateMaterialMethod = type.GetMethod(
                    "ValidateMaterial",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(Material) },
                    null);
                if (validateMaterialMethod != null)
                    break;
            }

            return validateMaterialMethod;
        }

        private static MethodInfo? ResolveValidateShaderGraphMaterialMethod()
        {
            if (validateShaderGraphMaterialMethodResolved)
                return validateShaderGraphMaterialMethod;

            validateShaderGraphMaterialMethodResolved = true;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(ShaderGraphApiTypeName, false);
                if (type == null)
                    continue;

                validateShaderGraphMaterialMethod = type.GetMethod(
                    "ValidateLightingMaterial",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(Material) },
                    null);
                if (validateShaderGraphMaterialMethod != null)
                    break;
            }

            return validateShaderGraphMaterialMethod;
        }
    }

    internal sealed class MootorVehicleMaterialController : MonoBehaviour
    {
        private const string CowVisualName = "MootorVehicle_CowVisual";
        private bool applied;
        private bool missingVisualLogged;

        internal void Initialize(VehicleController vehicle, ModContext? context)
        {
            if (applied)
                return;

            Transform? cowVisual = null;
            foreach (var child in vehicle.GetComponentsInChildren<Transform>(true))
                if (string.Equals(child.name, CowVisualName, StringComparison.Ordinal))
                {
                    cowVisual = child;
                    break;
                }

            if (cowVisual == null)
            {
                if (!missingVisualLogged)
                {
                    missingVisualLogged = true;
                    context?.Logger.Warn(
                        $"Moo-tor Vehicle materials vehicle={vehicle.GetInstanceID()}: cow visual was not found.");
                }

                return;
            }

            // Asset loading can remap shaders for the active render pipeline. Reapply the complete
            // opaque HDRP state here even when the material already reports the expected shader.
            var result = MootorVehicleMaterials.FixSolidCowMaterials(cowVisual.gameObject);
            applied = true;
            MootorVehicleDiagnostics.Info(
                context,
                $"Moo-tor Vehicle materials vehicle={vehicle.GetInstanceID()}: " +
                $"renderers={result.RendererCount} decalMasksCleared={result.DecalMasksCleared} " +
                $"hdrpFixed={result.HdrpMaterialsFixed} " +
                $"hdrpValidated={result.HdrpMaterialsValidated} shaders='{result.ShaderNames}'.");
            if (result.HdrpMaterialsValidated < result.HdrpMaterialsFixed)
                context?.Logger.Warn(
                    $"Moo-tor Vehicle materials vehicle={vehicle.GetInstanceID()}: " +
                    $"HDRP validation was unavailable for " +
                    $"{result.HdrpMaterialsFixed - result.HdrpMaterialsValidated} material(s).");
        }
    }
}
