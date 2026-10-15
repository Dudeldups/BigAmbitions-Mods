#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

// Cache only authored assets. Bind instance renderers once at vehicle creation,
// independently of custom MonoBehaviour fields deserialized from an AssetBundle.
internal static class AudiR8LightAssets
{
    private const string AssetPath = "Assets/Mods/Audi_R8/Models/AudiR8LightOverlays.glb";
    private sealed class Template
    {
        internal AudiR8AuthoredLights.Definition Record;
        internal Mesh Mesh = null!;
    }
    private static Template[] templates = Array.Empty<Template>();

    internal static bool Load(AssetBundle bundle, ModContext? context)
    {
        Clear();
        try
        {
            var imported = bundle.LoadAsset<GameObject>(AssetPath);
            if (imported == null)
                throw new InvalidOperationException("Authored lamp GLB is missing from the bundle.");
            var definitions = AudiR8AuthoredLights.Groups;
            if (definitions.Length != 11)
                throw new InvalidOperationException("Compiled lamp definitions must contain eleven groups.");
            var meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
            foreach (var filter in imported.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) meshes.Add(filter.name, filter.sharedMesh);
            var groups = new HashSet<string>(StringComparer.Ordinal);
            var loaded = new Template[definitions.Length];
            for (var i = 0; i < loaded.Length; i++)
            {
                var record = definitions[i];
                if (!groups.Add(record.group) || string.IsNullOrEmpty(record.source) ||
                    !meshes.TryGetValue(record.mesh, out var mesh) ||
                    !mesh.isReadable || mesh.triangles.Length != record.faces * 3)
                    throw new InvalidOperationException($"Invalid authored lamp asset '{record.group}'.");
                loaded[i] = new Template { Record = record, Mesh = mesh };
            }
            templates = loaded;
            Log(context, $"asset catalog loaded groups={templates.Length} from the active R8 bundle.");
            return true;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn($"AudiR8 lighting: authored asset load failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    internal static AudiR8LightBindings.Lamp[] Resolve(GameObject vehicle, ModContext? context)
    {
        if (templates.Length != 11)
        {
            context?.Logger.Warn($"AudiR8 lighting vehicle={vehicle.GetInstanceID()}: authored asset catalog unavailable, groups={templates.Length}.");
            return Array.Empty<AudiR8LightBindings.Lamp>();
        }
        // Exact object names originate in the topology-validated Blender export.
        // This is one instance-local search during its lifecycle initialization.
        var renderers = vehicle.GetComponentsInChildren<MeshRenderer>(true);
        var result = new AudiR8LightBindings.Lamp[templates.Length];
        for (var i = 0; i < templates.Length; i++)
        {
            var template = templates[i];
            MeshRenderer? source = null;
            foreach (var renderer in renderers)
            {
                if (!string.Equals(renderer.name, template.Record.source, StringComparison.Ordinal)) continue;
                if (source != null)
                {
                    context?.Logger.Warn($"AudiR8 lighting vehicle={vehicle.GetInstanceID()}: duplicate production source '{template.Record.source}'.");
                    return Array.Empty<AudiR8LightBindings.Lamp>();
                }
                source = renderer;
            }
            if (source == null || source.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount != template.Record.sourceVertices)
            {
                context?.Logger.Warn($"AudiR8 lighting vehicle={vehicle.GetInstanceID()}: source binding failed group='{template.Record.group}' source='{template.Record.source}' expectedVertices={template.Record.sourceVertices} rendererFound={source != null}.");
                return Array.Empty<AudiR8LightBindings.Lamp>();
            }
            result[i] = new AudiR8LightBindings.Lamp { group = template.Record.group, source = source, mesh = template.Mesh };
        }
        var authored = vehicle.GetComponent<AudiR8LightBindings>();
        Log(context, $"vehicle={vehicle.GetInstanceID()} instance bindings resolved={result.Length} " +
            $"serializedComponent={authored != null} serializedGroups={authored?.Lamps?.Length ?? 0}.");
        return result;
    }

    internal static void Clear() => templates = Array.Empty<Template>();
    private static void Log(ModContext? context, string message)
    {
        if (AudiR8Diagnostics.LightingDebugEnabled) AudiR8Diagnostics.Info(context, "AudiR8 lighting: " + message);
    }
}
