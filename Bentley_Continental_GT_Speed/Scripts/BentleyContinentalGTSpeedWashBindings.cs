#nullable enable
using System.Reflection;
using BAModAPI;
using UnityEngine;

/// <summary>
/// Repairs the native wash bindings on the Bentley prefab and each spawned
/// vehicle. The game coroutine retains ownership of wash progress and cleanup.
/// </summary>
internal static class BentleyContinentalGTSpeedWashBindings
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string EffectName = "BentleyWashEffects";
    private const string AudioSourceName = "BentleyWashAudioSource";
    private const float NativeLoopLength = 3.21956253f;
    private const float NativeEndLength = 1.80956244f;

    private static readonly FieldInfo? WashEffectField = FindField(typeof(CarController), "washVfx");
    private static readonly FieldInfo? WashAudioField = FindField(typeof(CarController), "washAudioSource");
    private static readonly FieldInfo? WashLoopField = FindField(typeof(CarController), "washSoundLoop");
    private static readonly FieldInfo? WashEndField = FindField(typeof(CarController), "washSoundEnd");

    internal static bool Configure(VehicleController? vehicle, ModContext? context, string source)
    {
        if (vehicle is not CarController car)
            return false;

        try
        {
            var effect = WashEffectField?.GetValue(car) as GameObject;
            if (effect == null && WashEffectField != null)
            {
                effect = car.transform.Find(EffectName)?.gameObject;
                if (effect == null)
                {
                    effect = CreateSafeEffect(car);
                    context?.Logger.Warn(
                        $"BentleyContinentalGTSpeed wash vehicle={car.GetInstanceID()}: authored effect was missing; " +
                        "created an inactive fallback so the native wash path can finish safely.");
                }
                effect.SetActive(false);
                WashEffectField.SetValue(car, effect);
            }

            var audio = WashAudioField?.GetValue(car) as AudioSource;
            if (audio == null)
            {
                var audioObject = car.transform.Find(AudioSourceName)?.gameObject;
                if (audioObject == null)
                {
                    audioObject = new GameObject(AudioSourceName) { hideFlags = HideFlags.DontSave };
                    audioObject.transform.SetParent(car.transform, false);
                }
                audio = audioObject.GetComponent<AudioSource>() ?? audioObject.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.pitch = 1f;
                audio.loop = false;
                audio.volume = 1f;
                audio.spatialBlend = 1f;
                audio.dopplerLevel = 1f;
                audio.reverbZoneMix = 1f;
                WashAudioField?.SetValue(car, audio);
                context?.Logger.Warn(
                    $"BentleyContinentalGTSpeed wash vehicle={car.GetInstanceID()}: native wash source was missing; " +
                    "created one dedicated source instead of reusing another vehicle AudioSource.");
            }
            if (audio != null)
            {
                audio.playOnAwake = false;
                if (audio.outputAudioMixerGroup == null)
                    audio.outputAudioMixerGroup = GlobalReferences.Instance?.playerVehicleMixerGroup;
            }

            // Native CarController owns clip switching and playback. Preserve
            // its authored vanilla references and never add a competing source.
            var loop = WashLoopField?.GetValue(car) as AudioClip;
            var end = WashEndField?.GetValue(car) as AudioClip;
            var validLoop = IsNativeClip(loop, "BentleyWashLoop", NativeLoopLength);
            var validEnd = IsNativeClip(end, "BentleyWashEnd", NativeEndLength);

            var complete = audio != null && validLoop && validEnd &&
                           (WashEffectField == null || effect != null) &&
                           WashAudioField != null && WashLoopField != null && WashEndField != null;
            if (!complete)
            {
                context?.Logger.Warn(
                    $"BentleyContinentalGTSpeed wash vehicle={car.GetInstanceID()}: native bindings incomplete " +
                    $"source={audio != null}, loop={DescribeClip(loop, validLoop)}, end={DescribeClip(end, validEnd)}, " +
                    $"effect={effect != null}, fields={WashAudioField != null}/{WashLoopField != null}/{WashEndField != null}; " +
                    $"source='{source}'.");
                return false;
            }

            BentleyContinentalGTSpeedDiagnostics.WashInfo(
                context,
                $"vehicle={car.GetInstanceID()} source='{source}' audio={audio!.name} " +
                $"loop='{loop!.name}' end='{end!.name}' effect={(effect != null ? effect.name : "<field unavailable>")} " +
                $"isWashing={car.isWashing} playing={audio.isPlaying} currentClip='{audio.clip?.name ?? "<none>"}' " +
                $"pitch={audio.pitch:0.00} volume={audio.volume:0.00} spatialBlend={audio.spatialBlend:0.00} " +
                $"reverbMix={audio.reverbZoneMix:0.00} mixer='{audio.outputAudioMixerGroup?.name ?? "<none>"}' " +
                $"playOnAwake={audio.playOnAwake} sourceLoop={audio.loop}; native coroutine owns playback.");
            return true;
        }
        catch (System.Exception exception)
        {
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed wash setup failed vehicle={car.GetInstanceID()} source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static bool IsNativeClip(AudioClip? clip, string expectedName, float expectedLength) =>
        clip != null && clip.name == expectedName && clip.frequency > 0 && clip.samples > 0 &&
        Mathf.Abs(clip.length - expectedLength) <= 0.01f;

    private static string DescribeClip(AudioClip? clip, bool valid) => clip == null
        ? "<missing>"
        : $"'{clip.name}' {clip.frequency}Hz/{clip.samples} samples/{clip.length:0.000}s validNative={valid}";

    private static GameObject CreateSafeEffect(CarController car)
    {
        var effect = new GameObject(EffectName) { layer = car.gameObject.layer };
        effect.transform.SetParent(car.transform, false);
        effect.transform.localPosition = new Vector3(0f, 0.65f, 0f);
        var particles = effect.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = 0.5f;
        main.startSpeed = 0.35f;
        main.startSize = 0.075f;
        main.gravityModifier = 0.35f;
        main.maxParticles = 96;
        var emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 120f;
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1.9f, 0.65f, 4.3f);
        effect.SetActive(false);
        return effect;
    }

    private static FieldInfo? FindField(System.Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, Members | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }
}
