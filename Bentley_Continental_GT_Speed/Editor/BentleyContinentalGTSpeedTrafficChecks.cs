#nullable enable
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Regression checks for building Bentley traffic visuals on an inactive traffic paint fixture.</summary>
public static class BentleyContinentalGTSpeedTrafficChecks
{
    private const int ExpectedBodyRendererCount = 6;
    private const int ExpectedCaliperRendererCount = 8;
    private const string CaliperPaintMaterialName = "Bentley_CaliperAZonePaint_Prepared";

    public static void VerifyRendererBindingWithoutPlayerController(GameObject playerPrefab)
    {
        if (playerPrefab == null)
            throw new ArgumentNullException(nameof(playerPrefab));

        var fixture = new GameObject("BentleyTrafficRendererFixture")
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        fixture.SetActive(false);
        try
        {
            fixture.AddComponent<CarFeatures>();
            if (fixture.GetComponent<VehicleController>() != null)
                throw new InvalidOperationException(
                    "Bentley traffic renderer fixture unexpectedly contains a player VehicleController.");

            CloneVisual(playerPrefab.transform, fixture.transform, "BentleyVisual");
            foreach (var corner in new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" })
                CloneVisual(playerPrefab.transform, fixture.transform, corner + "_UprightVisual");

            var support = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("BentleyContinentalGTSpeedPrivateDriverSupport", false))
                .FirstOrDefault(type => type != null) ??
                throw new InvalidOperationException("Bentley private-driver support type is unavailable to Editor checks.");
            var configure = support.GetMethod(
                "ConfigureVehicleRenderers", BindingFlags.Static | BindingFlags.NonPublic) ??
                throw new InvalidOperationException("Bentley traffic renderer configuration method is unavailable.");
            if (!(bool)configure.Invoke(null, new object[] { fixture, fixture.transform })!)
                throw new InvalidOperationException(
                    "Bentley traffic renderer configuration rejected an inactive traffic paint fixture without VehicleController.");

            var features = fixture.GetComponent<CarFeatures>();
            var bodyMeshes = features != null ? features.bodyMeshes : null;
            if (bodyMeshes == null)
                throw new InvalidOperationException(
                    "Bentley traffic renderer fixture lost its native paint bindings.");

            var bodyRendererCount = bodyMeshes.Count(IsBodyPaintRenderer);
            var boundCalipers = bodyMeshes.Where(IsExactCaliperPaintRenderer).ToArray();
            var hiddenOriginalCalipers = bodyMeshes.Where(renderer => renderer != null &&
                HasExactCaliperPaintMaterial(renderer) && !IsLiveExtractedCaliperRenderer(renderer)).ToArray();
            if (bodyRendererCount != ExpectedBodyRendererCount ||
                boundCalipers.Length != ExpectedCaliperRendererCount ||
                boundCalipers.Distinct().Count() != ExpectedCaliperRendererCount ||
                hiddenOriginalCalipers.Length != 0)
            {
                throw new InvalidOperationException(
                    "Bentley inactive traffic renderer fixture expected six body paint and eight unique upright " +
                    $"caliper bindings with no hidden originals; found body={bodyRendererCount}, " +
                    $"calipers={boundCalipers.Length}, hiddenOriginals={hiddenOriginalCalipers.Length}, " +
                    $"allBindings={bodyMeshes.Length}.");
            }

            Debug.Log(
                "Bentley traffic renderer regression passed: inactive traffic paint fixture without VehicleController " +
                $"bound body={bodyRendererCount}, calipers={boundCalipers.Length}, hiddenOriginals=0.");
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw new InvalidOperationException(
                "Bentley traffic renderer regression failed: " + exception.InnerException.Message,
                exception.InnerException);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(fixture);
        }
    }

    private static void CloneVisual(Transform sourceRoot, Transform destinationRoot, string name)
    {
        var source = FindTransform(sourceRoot, name);
        if (source == null)
            throw new InvalidOperationException("Bentley traffic renderer fixture is missing " + name + ".");
        var clone = UnityEngine.Object.Instantiate(source.gameObject, destinationRoot, false);
        clone.name = name;
    }

    private static Transform? FindTransform(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => string.Equals(transform.name, name, StringComparison.Ordinal));

    private static bool IsBodyPaintRenderer(Renderer? renderer) => renderer != null &&
        renderer.sharedMaterials.Any(material => material != null && IsBodyPaintMaterial(material.name));

    private static bool IsExactCaliperPaintRenderer(Renderer? renderer) => renderer != null &&
        HasExactCaliperPaintMaterial(renderer) && IsLiveExtractedCaliperRenderer(renderer);

    private static bool HasExactCaliperPaintMaterial(Renderer renderer) =>
        renderer.sharedMaterials.Length == 1 &&
        string.Equals(NormalizeMaterialName(renderer.sharedMaterials[0]?.name),
            CaliperPaintMaterialName, StringComparison.OrdinalIgnoreCase);

    private static bool IsLiveExtractedCaliperRenderer(Renderer renderer)
    {
        if (!renderer.enabled || !renderer.gameObject.activeSelf)
            return false;
        for (var node = renderer.transform.parent; node != null; node = node.parent)
        {
            if (!node.gameObject.activeSelf)
                return false;
            if (node.name == "FrontLeft_UprightVisual" || node.name == "FrontRight_UprightVisual" ||
                node.name == "RearLeft_UprightVisual" || node.name == "RearRight_UprightVisual")
                return true;
        }
        return false;
    }

    private static bool IsBodyPaintMaterial(string name)
    {
        var normalized = NormalizeMaterialName(name);
        return normalized.Equals("Paint", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("OEMPaint", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Bentley_Paint_Prepared", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("Bentley_OEMPaint_Prepared", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("BentleyMaterial_026", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("BentleyMaterial_027", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMaterialName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        foreach (var suffix in new[] { "(Clone)", "(Instance)" })
            if (normalized.EndsWith(suffix, StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - suffix.Length).TrimEnd();
        return normalized;
    }
}
