#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pink
{
    internal static partial class PinkRuntime
    {
        private const float ManualDebugRadiusMeters = 5f;
        private const int ManualDebugMaxEntries = 200;

        internal static void HandleManualDebugHotkeys()
        {
            if (!PinkFileLogger.Enabled || !Input.GetKeyDown(KeyCode.F4))
                return;

            DumpNearbyItemsAroundPlayer();
        }

        private static void DumpNearbyItemsAroundPlayer()
        {
            var playerRoot = GetPrimaryPlayerRoot();
            if (playerRoot == null)
            {
                PinkFileLogger.Warn("MANUAL_DEBUG F4 pressed, but no player root could be resolved.", alsoGameLog: true);
                return;
            }

            var playerPosition = playerRoot.transform.position;
            var entries = new List<NearbyDebugEntry>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            Renderer[] renderers;
            try
            {
                renderers = UnityEngine.Object.FindObjectsOfType<Renderer>(false);
            }
            catch (Exception ex)
            {
                PinkFileLogger.Error($"MANUAL_DEBUG renderer scan failed: {ex.GetType().Name}: {ex.Message}", alsoGameLog: true);
                return;
            }

            for (var index = 0; index < renderers.Length; index++)
            {
                var renderer = renderers[index];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                var distance = Vector3.Distance(playerPosition, renderer.bounds.center);
                if (distance > ManualDebugRadiusMeters)
                    continue;

                var root = renderer.transform.root != null ? renderer.transform.root.gameObject : renderer.gameObject;
                var path = GetPath(renderer.transform, 10);
                var dedupeKey = root.GetInstanceID() + "|" + path;
                if (!seenKeys.Add(dedupeKey))
                    continue;

                entries.Add(new NearbyDebugEntry(
                    distance,
                    root.name ?? string.Empty,
                    path,
                    renderer.gameObject.tag,
                    LayerMask.LayerToName(renderer.gameObject.layer),
                    renderer.GetType().Name,
                    BuildMaterialSummary(renderer.sharedMaterials)));
            }

            entries.Sort((left, right) => left.Distance.CompareTo(right.Distance));

            PinkFileLogger.Info(
                $"MANUAL_DEBUG START radius={ManualDebugRadiusMeters:0.0}m player={GetPath(playerRoot.transform, 8)} " +
                $"playerPos=({playerPosition.x:0.00}, {playerPosition.y:0.00}, {playerPosition.z:0.00}) matches={entries.Count}",
                alsoGameLog: true);

            var limit = Mathf.Min(entries.Count, ManualDebugMaxEntries);
            for (var index = 0; index < limit; index++)
            {
                var entry = entries[index];
                PinkFileLogger.Info(
                    $"MANUAL_DEBUG ITEM {index + 1}/{entries.Count}: distance={entry.Distance:0.00}m, root={entry.RootName}, " +
                    $"tag={entry.Tag}, layer={entry.Layer}, rendererType={entry.RendererType}, path={entry.Path}, materials={entry.MaterialSummary}");
            }

            if (entries.Count > ManualDebugMaxEntries)
            {
                PinkFileLogger.Info(
                    $"MANUAL_DEBUG truncated output to {ManualDebugMaxEntries} items out of {entries.Count} matches.");
            }

            PinkFileLogger.Info(
                $"MANUAL_DEBUG END file={(PinkFileLogger.FilePath ?? "<unknown>")}",
                alsoGameLog: true);
        }

        private static GameObject? GetPrimaryPlayerRoot()
        {
            foreach (var root in EnumeratePlayerRoots())
            {
                if (root != null)
                    return root;
            }

            return null;
        }

        private static string BuildMaterialSummary(Material[] materials)
        {
            if (materials == null || materials.Length == 0)
                return "<none>";

            var parts = new List<string>(materials.Length);
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                {
                    parts.Add(index + ":<null>");
                    continue;
                }

                var shaderName = material.shader != null ? material.shader.name : "<null-shader>";
                parts.Add(index + ":" + material.name + "@" + shaderName);
            }

            return string.Join(" | ", parts.ToArray());
        }

        private readonly struct NearbyDebugEntry
        {
            internal NearbyDebugEntry(
                float distance,
                string rootName,
                string path,
                string tag,
                string layer,
                string rendererType,
                string materialSummary)
            {
                Distance = distance;
                RootName = rootName;
                Path = path;
                Tag = string.IsNullOrWhiteSpace(tag) ? "<untagged>" : tag;
                Layer = string.IsNullOrWhiteSpace(layer) ? "<unnamed>" : layer;
                RendererType = rendererType;
                MaterialSummary = materialSummary;
            }

            internal float Distance { get; }
            internal string RootName { get; }
            internal string Path { get; }
            internal string Tag { get; }
            internal string Layer { get; }
            internal string RendererType { get; }
            internal string MaterialSummary { get; }
        }
    }
}
