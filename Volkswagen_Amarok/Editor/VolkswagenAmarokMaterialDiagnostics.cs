#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class VolkswagenAmarokMaterialDiagnostics
{
    private const string PrefabPath = "Assets/Mods/Volkswagen_Amarok/VolkswagenAmarok.prefab";

    [MenuItem("Big Ambitions Mods/Diagnose Volkswagen Amarok Materials")]
    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException($"Amarok prefab was not found at '{PrefabPath}'.");

        var pipeline = GraphicsSettings.currentRenderPipeline;
        var renderers = prefab.GetComponentsInChildren<Renderer>(true);
        var unique = new Dictionary<int, Entry>();
        var nullSlots = 0;
        var unsupported = 0;
        var errorShaders = 0;

        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            for (var slot = 0; slot < materials.Length; slot++)
            {
                var material = materials[slot];
                if (material == null)
                {
                    nullSlots++;
                    Debug.LogError(
                        $"[Amarok Material] NULL material renderer='{GetPath(renderer.transform)}' slot={slot}");
                    continue;
                }

                var shader = material.shader;
                var shaderName = shader != null ? shader.name : "<null>";
                var supported = shader != null && shader.isSupported;
                var errorShader = shader == null ||
                                  string.Equals(shaderName, "Hidden/InternalErrorShader", StringComparison.Ordinal);
                if (!supported) unsupported++;
                if (errorShader) errorShaders++;

                if (!unique.TryGetValue(material.GetInstanceID(), out var entry))
                {
                    entry = new Entry
                    {
                        Material = material,
                        ShaderName = shaderName,
                        ShaderSupported = supported,
                        ErrorShader = errorShader,
                        AssetPath = AssetDatabase.GetAssetPath(material),
                        ShaderAssetPath = shader != null ? AssetDatabase.GetAssetPath(shader) : string.Empty,
                        SurfaceType = ReadFloat(material, "_SurfaceType"),
                        RenderQueue = material.renderQueue,
                        BaseTexture = ReadTextureName(material, "_BaseColorMap", "baseColorTexture", "_MainTex"),
                        NormalTexture = ReadTextureName(material, "_NormalMap", "normalTexture", "_BumpMap"),
                    };
                    unique.Add(material.GetInstanceID(), entry);
                }
                entry.Uses.Add($"{GetPath(renderer.transform)}[{slot}]");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("===== VOLKSWAGEN AMAROK MATERIAL DIAGNOSTICS =====");
        sb.AppendLine($"Render pipeline: {(pipeline != null ? pipeline.GetType().FullName : "<built-in/null>")}");
        sb.AppendLine($"Renderers: {renderers.Length}");
        sb.AppendLine($"Unique materials: {unique.Count}");
        sb.AppendLine($"Null material slots: {nullSlots}");
        sb.AppendLine($"Unsupported material uses: {unsupported}");
        sb.AppendLine($"Error-shader material uses: {errorShaders}");
        sb.AppendLine();

        var index = 0;
        foreach (var entry in unique.Values)
        {
            index++;
            sb.AppendLine(
                $"[{index:D2}] material='{entry.Material.name}' " +
                $"shader='{entry.ShaderName}' supported={entry.ShaderSupported} errorShader={entry.ErrorShader} " +
                $"queue={entry.RenderQueue} surfaceType={entry.SurfaceType} " +
                $"baseTex='{entry.BaseTexture}' normalTex='{entry.NormalTexture}'");
            sb.AppendLine($"     materialAsset='{entry.AssetPath}'");
            sb.AppendLine($"     shaderAsset='{entry.ShaderAssetPath}'");
            sb.AppendLine($"     uses={string.Join(" | ", entry.Uses)}");
        }

        Debug.Log(sb.ToString());
        Debug.Log(
            $"VolkswagenAmarok material diagnostic complete: unique={unique.Count}, " +
            $"nullSlots={nullSlots}, unsupportedUses={unsupported}, errorShaderUses={errorShaders}.");
    }

    private static string GetPath(Transform transform)
    {
        var names = new List<string>();
        for (var current = transform; current != null; current = current.parent)
            names.Add(current.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static string ReadFloat(Material material, string property)
    {
        return material.HasProperty(property)
            ? material.GetFloat(property).ToString("0.###")
            : "<n/a>";
    }

    private static string ReadTextureName(Material material, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (!material.HasProperty(property))
                continue;
            var texture = material.GetTexture(property);
            if (texture != null)
                return texture.name;
        }
        return "<none>";
    }

    private sealed class Entry
    {
        internal Material Material = null!;
        internal string ShaderName = string.Empty;
        internal bool ShaderSupported;
        internal bool ErrorShader;
        internal string AssetPath = string.Empty;
        internal string ShaderAssetPath = string.Empty;
        internal string SurfaceType = string.Empty;
        internal int RenderQueue;
        internal string BaseTexture = string.Empty;
        internal string NormalTexture = string.Empty;
        internal readonly List<string> Uses = new List<string>();
    }
}
