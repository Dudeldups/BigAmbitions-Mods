#nullable enable
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

public static class AudiR8CrashSoundSetup
{
    private const string AudioRoot = "Assets/Mods/Audi_R8/Config/Audio/";
    private const int ClipCount = 7;

    public static void Author(GameObject root)
    {
        var physics = root.GetComponent<PhysicsVehicle>() ??
                      throw new InvalidOperationException("Audi R8 crash audio needs its native physics controller.");
        var crash = physics.soundManager.crashComponent;
        var previousClips = crash.clips.Count(clip => clip != null);
        crash.clips.Clear();
        for (var i = 1; i <= ClipCount; i++)
        {
            var path = AudioRoot + $"Crash{i:D2}.wav";
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path) ??
                       throw new InvalidOperationException($"Audi R8 crash clip is missing: {path}");
            crash.clips.Add(clip);
        }
        // Preserve the stock collision event, random clip selection, pitch,
        // cooldown, spatial source and game mixer/master-volume behavior.
        EditorUtility.SetDirty(physics);
        Debug.Log($"AudiR8 crash audio authored: previously resolved clips={previousClips}, owned clips={crash.clips.Count}.");
        VerifyBindings(root);
    }

    public static void VerifyBindings(GameObject root, AssetBundle? bundle = null)
    {
        var physics = root.GetComponent<PhysicsVehicle>() ??
                      throw new InvalidOperationException("Audi R8 native crash audio controller is missing.");
        var crash = physics.soundManager.crashComponent;
        if (crash.clips.Count != ClipCount || crash.baseVolume <= 0f ||
            crash.velocityMagnitudeEffect <= 0f || Math.Abs(crash.minDelay - 80f) > .01f)
            throw new InvalidOperationException("Audi R8 native crash audio settings are incomplete.");
        var bundledAssets = bundle?.GetAllAssetNames();
        for (var i = 0; i < ClipCount; i++)
        {
            var clip = crash.clips[i];
            var path = AudioRoot + $"Crash{i + 1:D2}.wav";
            if (clip == null || clip.samples <= 0 || clip.length <= 0f ||
                (bundle == null && AssetDatabase.GetAssetPath(clip) != path) ||
                (bundledAssets != null && (!bundledAssets.Contains(path.ToLowerInvariant()) ||
                    bundle!.LoadAsset<AudioClip>(path) != clip)))
                throw new InvalidOperationException($"Audi R8 crash clip is absent or external: {path}");
        }
        Debug.Log($"AudiR8 crash audio verified: {ClipCount} nonempty owned clips, native collision playback, volume={crash.baseVolume:0.00}, cooldown={crash.minDelay:0}ms, bundled={bundle != null}.");
    }
}
