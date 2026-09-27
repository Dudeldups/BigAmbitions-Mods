#nullable enable
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class BattleBusSetup
{
    private static void ConfigureBusAudio(GameObject root)
    {
        AudioClip Clip(string name)
        {
            var path = "Assets/Mods/Battle_Bus/Audio/" + name + ".wav";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            SetBundle(path);
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path) ?? throw new InvalidOperationException("Missing bus audio clip " + name);
        }
        var engine = new[] { Clip("DieselIdle") };
        var controller = root.AddComponent<BattleBusAudioController>();
        var serialized = new SerializedObject(controller);
        SetObjectArray(serialized, "engineClips", engine.Cast<UnityEngine.Object>().ToArray());
        serialized.FindProperty("burnerClip").objectReferenceValue = Clip("Burner");
        serialized.FindProperty("hornClip").objectReferenceValue = Clip("Horn");
        serialized.FindProperty("longHornClip").objectReferenceValue = Clip("HornLong");
        SetObjectArray(serialized, "radioClips", new[] { Clip("RadioSquelch1") }.Cast<UnityEngine.Object>().ToArray());
        serialized.ApplyModifiedPropertiesWithoutUndo();
        // Give the native engine source a local clip so its mixer/lifecycle is
        // initialized. The runtime controller owns the audible layered mix.
        var physics = FindComponentBySerializedProperty(root, "soundManager.engineRunningComponent.clips") ?? throw new InvalidOperationException("Bus native audio component missing.");
        var native = new SerializedObject(physics);
        SetObjectArray(native, "soundManager.engineRunningComponent.clips", new UnityEngine.Object[] { engine[0] });
        native.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log("Battle Bus audio bound: diesel idle/low/high, burner, two-tone horn and three radio squelches.");
    }
}
