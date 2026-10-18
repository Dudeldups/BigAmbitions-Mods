#nullable enable
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

public static class DodgeRam1500TRXCrashSoundSetup
{
    private const string AudioRoot = "Assets/Mods/Dodge_Ram_1500/Config/Audio/";
    private const int ClipCount = 7;

    private const string PrefabPath = "Assets/Mods/Dodge_Ram_1500/DodgeRam1500TRX.prefab";

    // Update the existing prefab only; preserve accepted tuning and file IDs.
    public static void AuthorAndBuild()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        const string previousBundle = "Assets/Mods/Dodge_Ram_1500/AssetBundles/Windows/dodgeram1500trx.unity3d";
        var bundle = AssetBundle.LoadFromFile(previousBundle);
        if (bundle != null)
        {
            try
            {
                var previous = bundle.LoadAsset<GameObject>(PrefabPath)?.GetComponent<PhysicsVehicle>();
                Debug.Log($"DodgeRam1500TRX previous bundled crash audio: resolved clips={previous?.soundManager.crashComponent.clips.Count(clip => clip != null)}.");
            }
            finally { bundle.Unload(true); }
        }
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Author(root);
            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                throw new InvalidOperationException("Could not save Dodge Ram crash audio bindings.");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        DodgeRam1500TRXSetup.BuildExistingAndVerify();
    }

    public static void Author(GameObject root)
    {
        var physics = root.GetComponent<PhysicsVehicle>() ??
                      throw new InvalidOperationException("Dodge Ram crash audio needs its native physics controller.");
        var crash = physics.soundManager.crashComponent;
        var previousClips = crash.clips.Count(clip => clip != null);
        crash.clips.Clear();
        for (var i = 1; i <= ClipCount; i++)
        {
            var path = AudioRoot + $"Crash{i:D2}.wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path) ??
                       throw new InvalidOperationException($"Dodge Ram crash clip is missing: {path}");
            crash.clips.Add(clip);
        }
        // Preserve the stock collision event, random clip selection, pitch,
        // cooldown, spatial source and game mixer/master-volume behavior.
        EditorUtility.SetDirty(physics);
        Debug.Log($"DodgeRam1500TRX crash audio authored: previously resolved clips={previousClips}, owned clips={crash.clips.Count}.");
        VerifyBindings(root);
    }

    public static void VerifyBindings(GameObject root, AssetBundle? bundle = null)
    {
        var physics = root.GetComponent<PhysicsVehicle>() ??
                      throw new InvalidOperationException("Dodge Ram native crash audio controller is missing.");
        var crash = physics.soundManager.crashComponent;
        if (crash.clips.Count != ClipCount || crash.baseVolume <= 0f ||
            crash.velocityMagnitudeEffect <= 0f || Math.Abs(crash.minDelay - 80f) > .01f)
            throw new InvalidOperationException("Dodge Ram native crash audio settings are incomplete.");
        var bundledAssets = bundle?.GetAllAssetNames();
        for (var i = 0; i < ClipCount; i++)
        {
            var clip = crash.clips[i];
            var path = AudioRoot + $"Crash{i + 1:D2}.wav";
            if (clip == null || clip.samples <= 0 || clip.length <= 0f ||
                (bundle == null && AssetDatabase.GetAssetPath(clip) != path) ||
                (bundledAssets != null && (!bundledAssets.Contains(path.ToLowerInvariant()) ||
                    bundle!.LoadAsset<AudioClip>(path) != clip)))
                throw new InvalidOperationException($"Dodge Ram crash clip is absent or external: {path}");
        }
        Debug.Log($"DodgeRam1500TRX crash audio verified: {ClipCount} nonempty owned clips, native collision playback, volume={crash.baseVolume:0.00}, cooldown={crash.minDelay:0}ms, bundled={bundle != null}.");
    }
}
