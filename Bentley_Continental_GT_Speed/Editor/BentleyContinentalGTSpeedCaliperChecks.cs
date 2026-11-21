#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Data.VehicleColors;
using UnityEngine;

/// <summary>Checks exact caliper paint ownership and per-vehicle material isolation.</summary>
public static class BentleyContinentalGTSpeedCaliperChecks
{
    private const string VisualRootName = "BentleyVisual";
    private const string CaliperPaintMaterialName = "Bentley_CaliperAZonePaint_Prepared";
    private const string FixedCaliperMaterialName = "Bentley_CaliperAZone_Prepared";
    private const string CaliperBadgeMaterialName = "Bentley_CaliperBadgeA_Prepared";
    private const int ExpectedCaliperRendererCount = 8;
    private const string VehicleTintProperty = "Color_3d0f0cdbe6b74be28a1a5be5bab71dea";
    private static readonly int VehicleTintPropertyId = Shader.PropertyToID(VehicleTintProperty);

    public static void VerifyPrefabBindings(GameObject prefab)
    {
        if (prefab == null) throw new ArgumentNullException(nameof(prefab));
        var features = prefab.GetComponent<CarFeatures>();
        var bodyBindings = features != null ? features.bodyMeshes : null;
        if (bodyBindings == null || bodyBindings.Length == 0)
            throw new InvalidOperationException("Bentley caliper check found no native CarFeatures.bodyMeshes bindings.");

        var allRenderers = prefab.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer != null).ToArray();
        var paintedCaliperRenderers = allRenderers
            .Where(renderer => HasExactCaliperPaintSlot(renderer) && IsLiveExtractedCaliperRenderer(renderer))
            .Distinct().ToArray();
        if (paintedCaliperRenderers.Length != ExpectedCaliperRendererCount)
            throw new InvalidOperationException(
                $"Bentley expected {ExpectedCaliperRendererCount} exact CaliperAZonePaint renderers, " +
                $"found {paintedCaliperRenderers.Length}.");

        var invalidCaliperSlots = paintedCaliperRenderers
            .Where(renderer => renderer.sharedMaterials.Length != 1 ||
                               !bodyBindings.Contains(renderer))
            .Select(renderer => renderer.name).ToArray();
        if (invalidCaliperSlots.Length > 0)
            throw new InvalidOperationException(
                "Bentley exact caliper paint renderers must each have one material slot and be in native bodyMeshes: " +
                string.Join(", ", invalidCaliperSlots));

        var staleCaliperBindings = bodyBindings.Where(renderer => renderer != null &&
            HasExactCaliperPaintSlot(renderer) && !IsLiveExtractedCaliperRenderer(renderer)).ToArray();
        if (staleCaliperBindings.Length > 0)
            throw new InvalidOperationException(
                "Bentley native bodyMeshes includes hidden/source caliper renderers: " +
                string.Join(", ", staleCaliperBindings.Select(renderer => renderer.name)));

        var fixedOnlyBindings = bodyBindings.Where(renderer => renderer != null &&
            !HasBodyPaintSlot(renderer) && !HasExactCaliperPaintSlot(renderer)).ToArray();
        if (fixedOnlyBindings.Length > 0)
            throw new InvalidOperationException(
                "Bentley native bodyMeshes includes renderers without body paint or exact caliper paint: " +
                string.Join(", ", fixedOnlyBindings.Select(renderer => renderer.name)));

        var forbiddenFixedCaliperBindings = bodyBindings.Where(renderer => renderer != null &&
            !HasBodyPaintSlot(renderer) && renderer.sharedMaterials.Any(IsFixedCaliperOrBadge)).ToArray();
        if (forbiddenFixedCaliperBindings.Length > 0)
            throw new InvalidOperationException(
                "Bentley native bodyMeshes includes fixed caliper/badge renderers: " +
                string.Join(", ", forbiddenFixedCaliperBindings.Select(renderer => renderer.name)));

        var visual = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => string.Equals(transform.name, VisualRootName, StringComparison.Ordinal));
        if (visual == null)
            throw new InvalidOperationException("Bentley caliper check cannot find BentleyVisual.");

        var corners = new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };
        var uprightRoots = corners.Select(corner => prefab.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(transform => string.Equals(
                    transform.name, corner + "_UprightVisual", StringComparison.Ordinal)))
            .ToArray();
        var cornerCounts = uprightRoots.Select(root => root == null
                ? 0
                : paintedCaliperRenderers.Count(renderer => renderer.transform.IsChildOf(root)))
            .ToArray();
        if (cornerCounts.Length != 4 || cornerCounts.Any(count => count != 2) ||
            cornerCounts.Sum() != ExpectedCaliperRendererCount)
            throw new InvalidOperationException(
                "Bentley exact caliper paint bindings do not cover all four wheel assemblies: " +
                string.Join("/", cornerCounts));

        Debug.Log(
            $"Bentley caliper paint verified: exactSlots={paintedCaliperRenderers.Length}, " +
            $"fourAssemblyRendererCounts={string.Join("/", cornerCounts)}, " +
            $"rendererOutsideBentleyVisual={paintedCaliperRenderers.Any(renderer => !renderer.transform.IsChildOf(visual))}; " +
            "fixed caliper and badge slots excluded.");
    }

    public static void VerifyRuntimeInstanceIsolation(GameObject prefab)
    {
        if (prefab == null) throw new ArgumentNullException(nameof(prefab));
        GameObject? first = null;
        GameObject? second = null;
        VehicleColor? firstColor = null;
        VehicleColor? secondColor = null;
        VehicleColor? previewColor = null;
        try
        {
            first = UnityEngine.Object.Instantiate(prefab);
            second = UnityEngine.Object.Instantiate(prefab);
            // Runtime prefabs/traffic templates are configured while their owner is inactive.
            first.SetActive(false);
            second.SetActive(false);
            first.hideFlags = HideFlags.HideAndDontSave;
            second.hideFlags = HideFlags.HideAndDontSave;
            first.name = "BentleyCaliperIsolation_First";
            second.name = "BentleyCaliperIsolation_Second";

            var appearanceType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("BentleyContinentalGTSpeedAppearanceRuntime", false))
                .FirstOrDefault(type => type != null) ??
                throw new InvalidOperationException("Bentley runtime appearance type is unavailable to Editor checks.");
            var configure = appearanceType.GetMethod(
                "Configure", BindingFlags.Instance | BindingFlags.NonPublic) ??
                throw new InvalidOperationException("Bentley runtime appearance Configure method is unavailable.");
            var firstAppearance = first.GetComponent(appearanceType) ?? first.AddComponent(appearanceType);
            var secondAppearance = second.GetComponent(appearanceType) ?? second.AddComponent(appearanceType);
            if (!(bool)configure.Invoke(firstAppearance,
                    new object?[] { first, null, "editor-caliper-isolation-check" })! ||
                !(bool)configure.Invoke(secondAppearance,
                    new object?[] { second, null, "editor-caliper-isolation-check" })!)
                throw new InvalidOperationException(
                    "Bentley runtime appearance could not configure both isolated caliper check instances.");

            var firstSlots = GetCaliperSlots(first);
            var secondSlots = GetCaliperSlots(second);
            var sourceSlots = GetCaliperSlots(prefab);
            if (firstSlots.Count != ExpectedCaliperRendererCount ||
                secondSlots.Count != ExpectedCaliperRendererCount ||
                sourceSlots.Count != ExpectedCaliperRendererCount)
                throw new InvalidOperationException(
                    "Bentley caliper isolation check did not find eight exact painted slots on each instance.");

            foreach (var key in sourceSlots.Keys)
            {
                if (!firstSlots.TryGetValue(key, out var firstMaterial) ||
                    !secondSlots.TryGetValue(key, out var secondMaterial) ||
                    firstMaterial == null || secondMaterial == null ||
                    firstMaterial == secondMaterial || firstMaterial == sourceSlots[key] ||
                    secondMaterial == sourceSlots[key])
                    throw new InvalidOperationException(
                        $"Bentley caliper slot '{key}' shares a material across vehicle instances or the prefab.");
            }

            var firstFeatures = first.GetComponent<CarFeatures>() ??
                                throw new InvalidOperationException("First Bentley check instance has no CarFeatures.");
            var secondFeatures = second.GetComponent<CarFeatures>() ??
                                 throw new InvalidOperationException("Second Bentley check instance has no CarFeatures.");
            var sourceTints = CaptureCaliperTints(prefab);
            firstColor = CreateVehicleColor("BentleyCaliperIsolation_First", new Color32(32, 184, 232, 255));
            secondColor = CreateVehicleColor("BentleyCaliperIsolation_Second", new Color32(226, 74, 49, 255));
            previewColor = CreateVehicleColor("BentleyCaliperIsolation_Preview", new Color32(117, 209, 64, 255));

            // Exercise the native color path: distinct saved colors on two
            // instances, a preview change, then cancel by restoring instance 1.
            firstFeatures.SetColor(firstColor);
            secondFeatures.SetColor(secondColor);
            VerifyNativeTint(first, firstColor.tint, "initial per-instance color");
            VerifyNativeTint(second, secondColor.tint, "second per-instance color");
            firstFeatures.SetColor(previewColor);
            VerifyNativeTint(first, previewColor.tint, "preview color");
            VerifyNativeTint(second, secondColor.tint, "preview isolation");
            firstFeatures.SetColor(firstColor);
            VerifyNativeTint(first, firstColor.tint, "cancel restore");
            VerifyNativeTint(second, secondColor.tint, "cancel isolation");
            VerifyCaliperTintsUnchanged(prefab, sourceTints);

            if (firstSlots.Values.Any(material => material == null) ||
                secondSlots.Values.Any(material => material == null) ||
                sourceSlots.Values.Any(material => material == null))
                throw new InvalidOperationException(
                    "Bentley native repaint left a null caliper material on an instance.");

            Debug.Log(
                $"Bentley caliper runtime isolation verified: {ExpectedCaliperRendererCount} exact painted slots " +
                $"use SH_Vehicle tint '{VehicleTintProperty}' and native CarFeatures.SetColor preview/cancel; " +
                "each vehicle and the saved prefab retain independent paint state.");
        }
        finally
        {
            if (firstColor != null) UnityEngine.Object.DestroyImmediate(firstColor);
            if (secondColor != null) UnityEngine.Object.DestroyImmediate(secondColor);
            if (previewColor != null) UnityEngine.Object.DestroyImmediate(previewColor);
            if (first != null) UnityEngine.Object.DestroyImmediate(first);
            if (second != null) UnityEngine.Object.DestroyImmediate(second);
        }
    }

    private static Dictionary<string, Material> GetCaliperSlots(GameObject root)
    {
        var slots = new Dictionary<string, Material>(StringComparer.Ordinal);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !IsLiveExtractedCaliperRenderer(renderer)) continue;
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null || !IsExactCaliperPaint(material)) continue;
                slots[GetRelativePath(renderer.transform, root.transform) + "[" + index + "]"] = material;
            }
        }
        return slots;
    }

    private static string GetRelativePath(Transform transform, Transform root)
    {
        var parts = new Stack<string>();
        for (var current = transform; current != null && current != root; current = current.parent)
            parts.Push(current.name);
        return string.Join("/", parts);
    }

    private static bool HasExactCaliperPaintSlot(Renderer renderer) =>
        renderer.sharedMaterials.Any(material => material != null && IsExactCaliperPaint(material));

    private static bool IsLiveExtractedCaliperRenderer(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeSelf ||
            renderer.sharedMaterials.Length != 1)
            return false;

        for (var node = renderer.transform.parent; node != null; node = node.parent)
        {
            if (!node.gameObject.activeSelf) return false;
            if (node.name == "FrontLeft_UprightVisual" || node.name == "FrontRight_UprightVisual" ||
                node.name == "RearLeft_UprightVisual" || node.name == "RearRight_UprightVisual")
                return true;
        }
        return false;
    }

    private static bool HasBodyPaintSlot(Renderer renderer) => renderer.sharedMaterials.Any(material =>
        material != null && (material.name.StartsWith("Bentley_Paint_Prepared", StringComparison.Ordinal) ||
                             material.name.StartsWith("Bentley_OEMPaint_Prepared", StringComparison.Ordinal)));

    private static bool IsExactCaliperPaint(Material material) =>
        string.Equals(material.name, CaliperPaintMaterialName, StringComparison.Ordinal) ||
        string.Equals(material.name, "CaliperAZonePaint", StringComparison.Ordinal) ||
        string.Equals(material.name, "BentleyMaterial_003", StringComparison.Ordinal);

    private static bool IsFixedCaliperOrBadge(Material material) =>
        string.Equals(material.name, FixedCaliperMaterialName, StringComparison.Ordinal) ||
        string.Equals(material.name, CaliperBadgeMaterialName, StringComparison.Ordinal);

    private static VehicleColor CreateVehicleColor(string name, Color32 tint)
    {
        var color = ScriptableObject.CreateInstance<VehicleColor>();
        color.name = name;
        color.tint = tint;
        color.fresnelColor = new Color32(215, 224, 238, 255);
        color.fresnelPower = 2.04f;
        return color;
    }

    private static Dictionary<string, (bool HasTint, Color Tint)> CaptureCaliperTints(GameObject root)
    {
        var result = new Dictionary<string, (bool HasTint, Color Tint)>(StringComparer.Ordinal);
        var properties = new MaterialPropertyBlock();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !IsLiveExtractedCaliperRenderer(renderer) ||
                !HasExactCaliperPaintSlot(renderer)) continue;
            properties.Clear();
            renderer.GetPropertyBlock(properties);
            result[GetRelativePath(renderer.transform, root.transform)] =
                (properties.HasProperty(VehicleTintPropertyId), properties.GetColor(VehicleTintPropertyId));
        }
        return result;
    }

    private static void VerifyCaliperTintsUnchanged(
        GameObject root, Dictionary<string, (bool HasTint, Color Tint)> expected)
    {
        var actual = CaptureCaliperTints(root);
        if (actual.Count != expected.Count)
            throw new InvalidOperationException("Bentley repaint changed the saved prefab caliper binding count.");
        foreach (var entry in expected)
        {
            if (!actual.TryGetValue(entry.Key, out var tint) || tint.HasTint != entry.Value.HasTint ||
                (tint.HasTint && !ColorsEqual(tint.Tint, entry.Value.Tint)))
                throw new InvalidOperationException(
                    $"Bentley repaint changed saved prefab caliper property-block state '{entry.Key}'.");
        }
    }

    private static void VerifyNativeTint(GameObject root, Color32 expectedTint, string operation)
    {
        var expected = (Color)expectedTint;
        var properties = new MaterialPropertyBlock();
        var count = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !IsLiveExtractedCaliperRenderer(renderer) ||
                !HasExactCaliperPaintSlot(renderer)) continue;
            if (renderer.sharedMaterials.Length != 1 ||
                renderer.sharedMaterials[0]?.shader == null ||
                !renderer.sharedMaterials[0].HasProperty(VehicleTintProperty))
                throw new InvalidOperationException(
                    $"Bentley caliper '{renderer.name}' does not use the native vehicle tint shader during {operation}.");
            properties.Clear();
            renderer.GetPropertyBlock(properties);
            if (!properties.HasProperty(VehicleTintPropertyId) ||
                !ColorsEqual(properties.GetColor(VehicleTintPropertyId), expected))
                throw new InvalidOperationException(
                    $"Bentley native CarFeatures.SetColor did not apply the expected tint to caliper '{renderer.name}' during {operation}.");
            count++;
        }
        if (count != ExpectedCaliperRendererCount)
            throw new InvalidOperationException(
                $"Bentley native repaint covered {count}/{ExpectedCaliperRendererCount} exact caliper renderers during {operation}.");
    }

    private static bool ColorsEqual(Color left, Color right) =>
        Mathf.Abs(left.r - right.r) < 0.0001f &&
        Mathf.Abs(left.g - right.g) < 0.0001f &&
        Mathf.Abs(left.b - right.b) < 0.0001f &&
        Mathf.Abs(left.a - right.a) < 0.0001f;
}
