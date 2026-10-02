#nullable enable
using System;
using System.IO;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEngine;

public static class BMWM4G82CompatibilityBuild
{
    private const string Root = "Assets/Mods/BMW_M4_G82";
    private static readonly long[] RequiredPhysicsScripts =
    {
        -333503815, 417210127, -221227779, 45295712, -1100654092
    };

    private static bool HasStateSettings(GameObject prefab)
    {
        foreach (var component in prefab.GetComponents<MonoBehaviour>())
        {
            if (component == null || component.GetType().FullName !=
                "NWH.VehiclePhysics2.VehicleController")
                continue;
            return new SerializedObject(component).FindProperty("stateSettings")?.objectReferenceValue != null;
        }
        return false;
    }

    private static void ValidateWashAudio(GameObject prefab)
    {
        foreach (var component in prefab.GetComponents<MonoBehaviour>())
        {
            if (component == null || component.GetType().FullName != "CarController")
                continue;
            var serialized = new SerializedObject(component);
            foreach (var field in new[] { "washAudioSource", "washSoundLoop", "washSoundEnd" })
                if (serialized.FindProperty(field)?.objectReferenceValue == null)
                    throw new InvalidOperationException("BMW wash dependency is missing: " + field);
            return;
        }
        throw new InvalidOperationException("BMW native CarController is missing.");
    }
    // Rebuild the existing finished vehicle; do not regenerate its geometry.
    public static void Build()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/BMWM4G82.prefab");
        if (prefab == null)
            throw new InvalidOperationException("BMW purchase prefab is missing.");
        ValidateWashAudio(prefab);
        if (!HasStateSettings(prefab))
            throw new InvalidOperationException("BMW NWH StateSettings dependency is missing.");

        var importer = AssetImporter.GetAtPath(
            "Assets/_BaDependencies/GameDlls/ExternalPlugins.dll") as PluginImporter;
        if (importer == null || (!importer.GetCompatibleWithAnyPlatform() &&
            (!importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX) ||
             !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64))))
            throw new InvalidOperationException("ExternalPlugins must support both Windows and Mac.");

        ModAssetBundleCli.BuildForMod();
        foreach (var platform in new[] { "Windows", "Mac" })
        {
            var path = Root + "/AssetBundles/" + platform + "/bmw_m4_g82.unity3d";
            var manifest = File.ReadAllText(path + ".manifest");
            foreach (var script in RequiredPhysicsScripts)
            {
                var reference = "{fileID: " + script +
                    ", guid: 4384b44565f83c96ced3259b77f79fb6, type: 3}";
                if (!manifest.Contains(reference))
                    throw new InvalidOperationException(
                        platform + " bundle is missing required NWH script " + script + ".");
            }
            Debug.Log("BMWM4G82 compatibility: verified five NWH dependency types in " + platform + ".");
        }

        var bundle = AssetBundle.LoadFromFile(
            Root + "/AssetBundles/Windows/bmw_m4_g82.unity3d");
        if (bundle == null)
            throw new InvalidOperationException("Could not reload the built Windows bundle.");
        try
        {
            var bundledPrefab = bundle.LoadAsset<GameObject>(Root + "/BMWM4G82.prefab");
            if (bundledPrefab == null || !HasStateSettings(bundledPrefab))
                throw new InvalidOperationException("Built prefab lost its StateSettings.");
            ValidateWashAudio(bundledPrefab);
            Debug.Log("BMWM4G82 compatibility: built Windows prefab retains StateSettings and wash audio.");
        }
        finally { bundle.Unload(true); }
    }
}