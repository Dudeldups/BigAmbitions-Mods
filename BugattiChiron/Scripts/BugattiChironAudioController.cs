#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class BugattiChironAudioController : MonoBehaviour
{
    private const float DirectFallbackMixGain = 0.16f;
    private static readonly string[] EngineNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private readonly List<AudioClip> ownedClips = new();
    private readonly Dictionary<AudioSource, bool> mutedDefaultVoices = new();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private AudioSource? sourceTemplate;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private AudioSource? idleSource;
    private AudioSource? turboSource;
    private AudioSource? hornSource;
    private AudioClip? turboClip;
    private float originalDistortion;
    private bool savedMute;
    private bool ownsMute;
    private bool configured;
    private bool failed;
    private bool paused;
    private bool wasControlled;
    private bool voicesStarted;
    private bool usesDirectFallbackMix;
    private bool reportedDefaultVoiceSearch;
    private int attempts;
    private float nextAttempt;
    private float smoothRpm;
    private float smoothThrottle;
    private float envelope;
    private float driveBlend;
    private float loadBlend;
    private float nextDefaultVoiceScan;
    private float defaultVoiceScanUntil;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
    }

    private void LateUpdate()
    {
        if (vehicle == null || failed)
            return;
        try
        {
            if (!configured)
            {
                if (attempts >= 20 || Time.unscaledTime < nextAttempt)
                    return;
                attempts++;
                nextAttempt = Time.unscaledTime + 0.5f;
                if (!TryConfigure())
                {
                    if (attempts == 20)
                        Warn(
                            "custom audio prerequisites unavailable after 20 attempts; " +
                            $"physics={physics != null} engineSound={engineSound != null} " +
                            $"source={native != null} clip={native?.clip != null} " +
                            $"mixer={native?.outputAudioMixerGroup != null} " +
                            $"context={context != null}; custom audio was not initialized.");
                    return;
                }
            }
            UpdatePlayback();
        }
        catch (Exception exception)
        {
            failed = true;
            Cleanup();
            Warn(
                $"layered audio failed; native Car sound restored: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private bool TryConfigure()
    {
        physics = vehicle!.GetComponent<PhysicsVehicle>();
        engineSound = physics?.soundManager.engineRunningComponent;
        native = engineSound?.source;
        if (physics == null || context == null)
            return false;

        var otherSource = physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        sourceTemplate = native ?? otherSource;
        usesDirectFallbackMix = sourceTemplate == null ||
                                sourceTemplate.outputAudioMixerGroup == null;
        originalDistortion = engineSound?.maxDistortion ?? 0f;
        audioHost = new GameObject("BugattiChiron_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = EnginePosition();

        // Retain a quiet copy of the game's idle bed for low-speed mechanical
        // texture, then hand the audible engine character to the W16 layers.
        idleSource = CreateSource(
            audioHost, native?.clip ?? LoadClip("EngineLow"), true, sourceTemplate);
        layers = new AudioSource[6];
        for (var index = 0; index < EngineNames.Length; index++)
        {
            layers[index] = CreateSource(audioHost, LoadClip(EngineNames[index]), true, sourceTemplate);
            layers[index + 3] =
                CreateSource(audioHost, LoadClip(EngineNames[index] + "Load"), true, sourceTemplate);
        }

        turboClip = BugattiChironTurboWave.Create();
        var turboHost = new GameObject("BugattiChiron_QuadTurbo");
        turboHost.transform.SetParent(audioHost.transform, false);
        turboSource = CreateSource(turboHost, turboClip, true, sourceTemplate);
        ConfigureTurboFilters(turboHost);

        var hornHost = new GameObject("BugattiChiron_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var hornTemplate = otherSource ?? sourceTemplate;
        hornSource = CreateSource(hornHost, LoadClip("Horn"), true, hornTemplate);

        if (engineSound != null)
            engineSound.maxDistortion = 0f;
        configured = true;
        if (BugattiChironDiagnostics.DebugEnabled &&
            BugattiChironDiagnostics.AudioDebugEnabled)
        {
            context.Logger.Info(
                $"BugattiChiron audio vehicle={vehicle.GetInstanceID()}: custom layers ready " +
                $"source={(native != null ? "native" : sourceTemplate != null ? "auxiliary" : "direct")} " +
                $"mixer='{sourceTemplate?.outputAudioMixerGroup?.name ?? "<default output>"}' " +
                $"directMixGain={(usesDirectFallbackMix ? DirectFallbackMixGain : 1f):0.00}.");
        }
        return true;
    }

    private static void ConfigureTurboFilters(GameObject host)
    {
        var lowPass = host.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = 3200f;
        lowPass.lowpassResonanceQ = 1.05f;
        var highPass = host.AddComponent<AudioHighPassFilter>();
        highPass.cutoffFrequency = 350f;
        highPass.highpassResonanceQ = 1.08f;
        var distortion = host.AddComponent<AudioDistortionFilter>();
        distortion.distortionLevel = 0.006f;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = BugattiChironWave.Load(
            Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
        ownedClips.Add(clip);
        return clip;
    }

    private AudioSource CreateSource(
        GameObject host,
        AudioClip clip,
        bool loop,
        AudioSource? template)
    {
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.clip = clip;
        source.volume = 0f;
        if (template != null)
        {
            source.outputAudioMixerGroup = template.outputAudioMixerGroup;
            source.spatialBlend = template.spatialBlend;
            source.minDistance = template.minDistance;
            source.maxDistance = template.maxDistance;
            source.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            source.rolloffMode = template.rolloffMode;
            source.priority = template.priority;
        }
        else
        {
            source.spatialBlend = 1f;
            source.minDistance = 4f;
            source.maxDistance = 50f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.priority = 128;
        }
        source.dopplerLevel = 0f;
        return source;
    }

    private Vector3 EnginePosition() => native != null
        ? native.transform.position
        : vehicle!.transform.TransformPoint(new Vector3(0f, 0.45f, -0.8f));

    private void UpdatePlayback()
    {
        if (physics == null || layers == null || audioHost == null ||
            turboSource == null || hornSource == null || idleSource == null)
        {
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        }

        audioHost.transform.position = EnginePosition();
        var exhaust = physics.soundManager.exhaustSourceGO;
        turboSource.transform.position = exhaust != null
            ? exhaust.transform.position
            : vehicle!.transform.TransformPoint(new Vector3(0f, 0.4f, -2f));

        var controlled = vehicle!.controlledByPlayer;
        var engine = physics.powertrain.engine;
        var running = controlled && engine.ignition && engine.IsRunning && engine.canRun;
        if (controlled && !wasControlled)
        {
            smoothRpm = engine.RPMPercent * engine.revLimiterRPM;
            smoothThrottle = Mathf.Clamp01(engine.ThrottlePosition);
            defaultVoiceScanUntil = Time.unscaledTime + 10f;
            nextDefaultVoiceScan = 0f;
            reportedDefaultVoiceSearch = false;
        }
        if (controlled != wasControlled &&
            BugattiChironDiagnostics.DebugEnabled &&
            BugattiChironDiagnostics.AudioDebugEnabled)
        {
            context?.Logger.Info(
                $"BugattiChiron audio vehicle={vehicle.GetInstanceID()}: " +
                $"controlled={controlled} ignition={engine.ignition} " +
                $"running={engine.IsRunning} canRun={engine.canRun} " +
                $"source={(native != null ? "native" : sourceTemplate != null ? "auxiliary" : "direct")}.");
        }
        wasControlled = controlled;

        var shouldPause = Time.timeScale <= 0f || AudioListener.pause;
        if (shouldPause != paused)
        {
            if (shouldPause)
                StopLayers();
            paused = shouldPause;
        }

        if (controlled && native != null)
        {
            if (!ownsMute)
            {
                savedMute = native.mute;
                ownsMute = true;
            }
            native.mute = true;
        }
        else
        {
            RestoreMute();
        }
        MuteDefaultEngineVoices(controlled);

        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        var mixGain = usesDirectFallbackMix ? DirectFallbackMixGain : 1f;
        UpdateHorn(
            controlled && !paused && physics.input.Horn,
            Mathf.Clamp01(physics.soundManager.masterVolume) * mixGain);
        if (paused)
            return;

        var follow = 1f - Mathf.Exp(-Time.deltaTime / 0.06f);
        smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
        smoothThrottle = Mathf.Lerp(
            smoothThrottle,
            Mathf.Clamp01(engine.ThrottlePosition),
            follow);
        var normalized = BugattiChironAudioModel.Normalize(
            smoothRpm,
            engine.idleRPM,
            engine.revLimiterRPM);
        envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
        var master = Mathf.Clamp01(physics.soundManager.masterVolume);
        driveBlend = Mathf.MoveTowards(
            driveBlend,
            BugattiChironAudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM),
            Time.deltaTime * 6f);

        idleSource.pitch = native?.clip != null
            ? BugattiChironAudioModel.IdlePitch
            : BugattiChironAudioModel.Pitch(normalized, 0);
        idleSource.volume =
            envelope * master * mixGain * BugattiChironAudioModel.IdleVolume(driveBlend);
        idleSource.mute = controlled && savedMute;

        var gain = envelope * master * mixGain *
                   BugattiChironAudioModel.EngineVolume(smoothThrottle) *
                   Mathf.Sqrt(driveBlend);
        loadBlend = BugattiChironAudioModel.LoadBlend(smoothThrottle);
        for (var index = 0; index < EngineNames.Length; index++)
        {
            layers[index].pitch = BugattiChironAudioModel.Pitch(normalized, index);
            layers[index + 3].pitch = layers[index].pitch;
            var bandGain = gain * BugattiChironAudioModel.Weight(normalized, index);
            layers[index].volume = bandGain * (1f - loadBlend);
            layers[index + 3].volume = bandGain * loadBlend;
            layers[index].mute = layers[index + 3].mute = controlled && savedMute;
        }

        turboSource.pitch = Mathf.Lerp(0.65f, 0.95f, normalized);
        turboSource.volume = envelope * master * mixGain *
                             BugattiChironAudioModel.TurboVolume(normalized, smoothThrottle);
        turboSource.mute = controlled && savedMute;

        if (envelope <= 0f)
        {
            StopLayers();
        }
        else if (!voicesStarted)
        {
            var start = AudioSettings.dspTime + 0.03d;
            idleSource.PlayScheduled(start);
            foreach (var source in layers)
                source.PlayScheduled(start);
            turboSource.PlayScheduled(start);
            voicesStarted = true;
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null)
            return;
        var target = pressed ? master * BugattiChironAudioModel.HornVolume : 0f;
        hornSource.volume = Mathf.MoveTowards(
            hornSource.volume,
            target,
            Time.unscaledDeltaTime * 5f);
        if (pressed && !hornSource.isPlaying)
            hornSource.Play();
        else if (!pressed && hornSource.volume <= 0f && hornSource.isPlaying)
            hornSource.Stop();
    }

    private void Warn(string message) =>
        context?.Logger.Warn(
            $"BugattiChiron audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RestoreMute()
    {
        if (ownsMute && native != null)
            native.mute = savedMute;
        ownsMute = false;
    }

    private void MuteDefaultEngineVoices(bool controlled)
    {
        if (!controlled)
        {
            RestoreDefaultEngineVoices();
            return;
        }

        if (Time.unscaledTime <= defaultVoiceScanUntil &&
            Time.unscaledTime >= nextDefaultVoiceScan)
        {
            nextDefaultVoiceScan = Time.unscaledTime + 0.5f;
            foreach (var source in vehicle!.GetComponentsInChildren<AudioSource>(true))
            {
                if (source == null || source == native ||
                    (audioHost != null && source.transform.IsChildOf(audioHost.transform)) ||
                    source.clip == null ||
                    !string.Equals(source.clip.name, "Car", StringComparison.OrdinalIgnoreCase) ||
                    mutedDefaultVoices.ContainsKey(source))
                    continue;

                mutedDefaultVoices.Add(source, source.mute);
                if (BugattiChironDiagnostics.DebugEnabled &&
                    BugattiChironDiagnostics.AudioDebugEnabled)
                {
                    context?.Logger.Info(
                        $"BugattiChiron audio vehicle={vehicle.GetInstanceID()}: " +
                        $"muted stock Car voice object='{source.gameObject.name}' " +
                        $"mixer='{source.outputAudioMixerGroup?.name ?? "<default output>"}'.");
                }
            }
        }

        if (!reportedDefaultVoiceSearch && Time.unscaledTime > defaultVoiceScanUntil)
        {
            reportedDefaultVoiceSearch = true;
            if (BugattiChironDiagnostics.DebugEnabled &&
                BugattiChironDiagnostics.AudioDebugEnabled)
            {
                var candidates = new List<string>();
                if (mutedDefaultVoices.Count == 0)
                {
                    foreach (var source in vehicle!.GetComponentsInChildren<AudioSource>(true))
                    {
                        if (audioHost != null && source.transform.IsChildOf(audioHost.transform))
                            continue;
                        candidates.Add($"{source.gameObject.name}:{source.clip?.name ?? "<no clip>"}");
                        if (candidates.Count == 8)
                            break;
                    }
                }
                context?.Logger.Info(
                    $"BugattiChiron audio vehicle={vehicle!.GetInstanceID()}: " +
                    $"stock Car voice scan complete found={mutedDefaultVoices.Count} " +
                    $"otherSources=[{string.Join(", ", candidates)}].");
            }
        }

        foreach (var voice in mutedDefaultVoices)
            if (voice.Key != null)
                voice.Key.mute = true;
    }

    private void RestoreDefaultEngineVoices()
    {
        foreach (var voice in mutedDefaultVoices)
            if (voice.Key != null)
                voice.Key.mute = voice.Value;
        mutedDefaultVoices.Clear();
    }

    private void StopLayers()
    {
        if (idleSource != null)
            idleSource.Stop();
        if (layers != null)
        {
            foreach (var source in layers)
            {
                if (source != null)
                    source.Stop();
            }
        }
        if (turboSource != null)
            turboSource.Stop();
        voicesStarted = false;
    }

    private void OnDisable()
    {
        StopLayers();
        if (hornSource != null)
        {
            hornSource.Stop();
            hornSource.volume = 0f;
        }
        RestoreMute();
        RestoreDefaultEngineVoices();
        if (configured && engineSound != null)
            engineSound.maxDistortion = originalDistortion;
        envelope = 0f;
        smoothRpm = 0f;
        smoothThrottle = 0f;
        driveBlend = 0f;
        loadBlend = 0f;
        paused = false;
        wasControlled = false;
    }

    private void OnEnable()
    {
        if (configured && engineSound != null)
            engineSound.maxDistortion = 0f;
    }

    private void Cleanup()
    {
        OnDisable();
        configured = false;
        if (audioHost != null)
            Destroy(audioHost);
        audioHost = null;
        layers = null;
        idleSource = null;
        turboSource = null;
        hornSource = null;
        if (turboClip != null)
            Destroy(turboClip);
        turboClip = null;
        foreach (var clip in ownedClips)
        {
            if (clip != null)
                Destroy(clip);
        }
        ownedClips.Clear();
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}
