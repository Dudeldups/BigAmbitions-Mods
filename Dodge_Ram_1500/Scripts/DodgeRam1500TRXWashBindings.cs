#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using BAModAPI;
using Helpers;
using UnityEngine;

// Native code owns washing, cleaning, progress, navigation blockers and audio.
// Repair missing dependencies at the instance lifecycle, without polling.
public static class DodgeRam1500TRXWashBindings
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo? Effect = typeof(CarController).GetField("washVfx", Members);
    private static readonly FieldInfo? Audio = typeof(CarController).GetField("washAudioSource", Members);
    private static readonly FieldInfo? Loop = typeof(CarController).GetField("washSoundLoop", Members);
    private static readonly FieldInfo? End = typeof(CarController).GetField("washSoundEnd", Members);
    private static readonly List<DodgeRam1500TRXWashPhysicsGuard> owners = new();
    private static CarController? nativeWashTemplate;

    internal static void Configure(VehicleController vehicle, ModContext? context)
    {
        if (vehicle is not CarController car) return;
        if (context != null && File.Exists(Path.Combine(context.ModRootPath, "Config", "wash-diagnostics.enabled")))
        {
            DodgeRam1500TRXDiagnostics.DebugEnabled = true;
            DodgeRam1500TRXDiagnostics.WashDebugEnabled = true;
        }
        if (Effect == null || Audio == null || Loop == null || End == null)
        {
            context?.Logger.Warn($"DodgeRam1500TRX wash vehicle={vehicle.GetInstanceID()}: native fields unavailable.");
            return;
        }
        var guard = car.GetComponent<DodgeRam1500TRXWashPhysicsGuard>();
        if (guard != null && guard.IsConfigured)
        {
            if (Effect.GetValue(car) == null) Effect.SetValue(car, guard.EventEffect);
            if (Audio.GetValue(car) == null) Audio.SetValue(car, guard.WashAudio);
            ResolveClips(car, context);
            return;
        }
        guard ??= car.gameObject.AddComponent<DodgeRam1500TRXWashPhysicsGuard>();
        var originalEffect = Effect.GetValue(car) as GameObject;
        var effects = originalEffect;
        var ownsEffect = false;
        if (effects == null)
        {
            effects = car.transform.Find("DodgeRam1500TRXWashEffects")?.gameObject;
            if (effects == null)
            {
                effects = new GameObject("DodgeRam1500TRXWashEffects") { hideFlags = HideFlags.DontSave };
                effects.transform.SetParent(car.transform, false);
                effects.SetActive(false);
                ownsEffect = true;
            }
            Effect.SetValue(car, effects);
        }
        ResolveClips(car, context);
        var loop = Loop.GetValue(car) as AudioClip;
        var end = End.GetValue(car) as AudioClip;
        var originalAudio = Audio.GetValue(car) as AudioSource;
        var host = new GameObject("DodgeRam1500TRX_WashAudio") { hideFlags = HideFlags.DontSave };
        host.transform.SetParent(car.transform, false);
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.volume = originalAudio != null ? originalAudio.volume : .7f;
        source.spatialBlend = .85f;
        source.minDistance = 4f;
        source.maxDistance = 30f;
        source.dopplerLevel = 0f;
        source.outputAudioMixerGroup = InstanceBehavior<GlobalReferences>.Instance?.playerVehicleMixerGroup ?? originalAudio?.outputAudioMixerGroup;
        Audio.SetValue(car, source);
        if (loop == null || end == null)
            context?.Logger.Warn($"DodgeRam1500TRX wash vehicle={car.GetInstanceID()}: actual native wash clips unavailable; loop={loop != null}, end={end != null}.");
        guard.Initialize(car, effects, source, originalEffect, originalAudio, ownsEffect, context);
        owners.RemoveAll(owner => owner == null);
        if (!owners.Contains(guard)) owners.Add(guard);
        DodgeRam1500TRXDiagnostics.WashInfo(context,
            $"DodgeRam1500TRX wash vehicle={car.GetInstanceID()}: dedicated audio bound; loop={loop?.name}, end={end?.name}; suspension freeze/resume events installed.");
    }

    private static void ResolveClips(CarController car, ModContext? context)
    {
        var loop = Loop!.GetValue(car) as AudioClip;
        var end = End!.GetValue(car) as AudioClip;
        if (loop != null && end != null) return;
        // SDK stubs may exist in the Editor yet resolve to null in game. Use
        // the actual vanilla game's generic wash clips when either is absent.
        try
        {
            if (nativeWashTemplate == null)
                nativeWashTemplate = PrefabHelper.LoadPrefabAssetByName("Vehicles/PlayerVehicles/HonzaMimic")?.GetComponent<CarController>();
            if (nativeWashTemplate == null) return;
            if (loop == null) Loop.SetValue(car, Loop.GetValue(nativeWashTemplate));
            if (end == null) End.SetValue(car, End.GetValue(nativeWashTemplate));
        }
        catch (Exception exception)
        {
            // An optional sound-asset failure must not abort configuration of
            // the vehicle's controls, colliders and other independent systems.
            context?.Logger.Warn($"DodgeRam1500TRX wash vehicle={car.GetInstanceID()}: native audio resolution failed: {exception.GetBaseException().Message}");
        }
    }

    internal static void Shutdown()
    {
        foreach (var owner in owners) if (owner != null) owner.Shutdown();
        owners.Clear();
        nativeWashTemplate = null;
    }
}
