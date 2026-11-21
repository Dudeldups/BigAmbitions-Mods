#nullable enable
using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// Suppresses only the two authored EngineA mesh renderers that sit above the
/// engine bay and become visible through a damaged hood. Their source objects,
/// transforms, and any rig bindings remain intact.
/// </summary>
internal static class BentleyContinentalGTSpeedEngineGeometrySetup
{
    private const string EngineMaterialName = "Bentley_EngineA_Prepared";

    private static readonly string[] EngineRendererNames =
    {
        "Object_49",
        "Object_179",
    };

    internal static void Apply(GameObject visualRoot)
    {
        if (visualRoot == null)
            throw new ArgumentNullException(nameof(visualRoot));

        var renderers = FindAuthoredEngineRenderers(visualRoot);
        var vehicle = visualRoot.GetComponentInParent<VehicleController>(true);
        var vehicleRoot = vehicle != null ? vehicle.transform : visualRoot.transform;
        foreach (var renderer in renderers)
        {
            var center = vehicleRoot.InverseTransformPoint(renderer.bounds.center);
            if (center.z < 1.3f || center.y < .25f || center.y > 1.2f)
                throw new InvalidOperationException(
                    $"Bentley engine geometry '{renderer.name}' moved outside its verified under-hood bounds: " +
                    $"center={center}.");

            renderer.enabled = false;
            Debug.Log(
                $"Bentley under-hood detail suppressed renderer='{renderer.name}', " +
                $"material='{EngineMaterialName}', visualCenter={center}.");
        }

        VerifyDisabled(visualRoot);
    }

    internal static void VerifyDisabled(GameObject visualRoot)
    {
        if (visualRoot == null)
            throw new ArgumentNullException(nameof(visualRoot));

        var renderers = FindAuthoredEngineRenderers(visualRoot);
        var enabledNames = renderers.Where(renderer => renderer.enabled).Select(renderer => renderer.name).ToArray();
        if (enabledNames.Length > 0)
            throw new InvalidOperationException(
                "Bentley static engine detail must stay hidden so a deformed hood cannot expose it: " +
                string.Join(", ", enabledNames) + ".");
    }

    private static Renderer[] FindAuthoredEngineRenderers(GameObject visualRoot)
    {
        var all = visualRoot.GetComponentsInChildren<Renderer>(true);
        var found = EngineRendererNames
            .Select(name => all.Where(renderer => renderer != null &&
                                                  string.Equals(renderer.name, name, StringComparison.Ordinal))
                .ToArray())
            .ToArray();
        for (var index = 0; index < EngineRendererNames.Length; index++)
        {
            if (found[index].Length != 1)
                throw new InvalidOperationException(
                    $"Bentley engine detail renderer '{EngineRendererNames[index]}' must be unique; " +
                    $"found={found[index].Length}.");
        }

        var renderers = found.Select(matches => matches[0]).ToArray();
        foreach (var renderer in renderers)
        {
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                throw new InvalidOperationException(
                    $"Bentley engine detail '{renderer.name}' has an unexpected renderer type " +
                    renderer.GetType().Name + ".");

            var materials = renderer.sharedMaterials;
            if (materials.Length != 1 || materials[0] == null ||
                !string.Equals(materials[0].name, EngineMaterialName, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Bentley engine detail '{renderer.name}' material ownership changed; expected only " +
                    $"'{EngineMaterialName}', actual=[{string.Join(", ", materials.Where(material => material != null).Select(material => material.name))}].");

            var hasMesh = false;
            if (renderer is SkinnedMeshRenderer skinned)
                hasMesh = skinned.sharedMesh != null;
            else
            {
                var filter = renderer.GetComponent<MeshFilter>();
                hasMesh = filter != null && filter.sharedMesh != null;
            }
            if (!hasMesh)
                throw new InvalidOperationException("Bentley engine detail renderer lost its mesh: " + renderer.name + ".");
        }

        return renderers;
    }
}
