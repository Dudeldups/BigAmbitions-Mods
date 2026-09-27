#nullable enable
using System;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BattleBusModelInspector
{
    private const string ModelPath = "Assets/Mods/Battle_Bus/Models/battle_bus_fortnite.glb";

    [MenuItem("Big Ambitions Mods/Inspect Battle Bus Model")]
    public static void Inspect()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (root == null)
            throw new InvalidOperationException($"Could not import Battle Bus model '{ModelPath}'.");

        var report = new StringBuilder("Battle Bus imported model report\n");
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException("Imported Battle Bus model has no renderers.");

        var allBounds = renderers[0].bounds;
        foreach (var renderer in renderers)
            allBounds.Encapsulate(renderer.bounds);
        report.AppendLine($"root={root.name} rendererCount={renderers.Length} bounds={allBounds} size={allBounds.size}");

        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            var filter = transform.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            var renderer = transform.GetComponent<Renderer>();
            var names = renderer == null ? string.Empty : string.Join(",", Array.ConvertAll(renderer.sharedMaterials, m => m == null ? "<null>" : m.name));
            var meshBounds = mesh == null ? "none" : mesh.bounds.ToString();
            var worldBounds = renderer == null ? "none" : renderer.bounds.ToString();
            report.AppendLine($"node={GetPath(transform, root.transform)} active={transform.gameObject.activeSelf} local={transform.localPosition} scale={transform.localScale} meshVerts={(mesh == null ? 0 : mesh.vertexCount)} meshBounds={meshBounds} worldBounds={worldBounds} materials=[{names}]");
        }

        Debug.Log(report.ToString());
    }

    private static string GetPath(Transform transform, Transform root)
    {
        var path = transform.name;
        while (transform.parent != null && transform.parent != root.parent)
        {
            transform = transform.parent;
            if (transform == root)
                break;
            path = transform.name + "/" + path;
        }
        return path;
    }
}
