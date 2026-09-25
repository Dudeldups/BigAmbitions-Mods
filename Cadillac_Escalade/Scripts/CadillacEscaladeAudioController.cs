#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class CadillacEscaladeAudioController : MonoBehaviour
{
    private const int MaxConfigurationAttempts = 60;
    private static readonly string[] EngineNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private readonly List<AudioClip> ownedClips = new List<AudioClip>(8);
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private AudioSource? idleSource;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private float originalDistortion;
    private bool savedMute;
    private bool ownsMute;
    private bool configured;
    private bool failed;
    private bool paused;
    private bool wasControlled;
    private bool voicesStarted;
    private int attempts;
    private float nextAttempt;
    private float smoothRpm;
    private float smoothThrottle;
    private float envelope;
    private float driveBlend;
    private float loadBlend;

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
                if (attempts >= MaxConfigurationAttempts || Time.unscaledTime < nextAttempt)
                    return;

                attempts++;
                nextAttempt = Time.unscaledTime + .5f;
                if (!TryConfigure())
                {
                    if (attempts == MaxConfigurationAttempts)
                        Warn($"vehicle audio state unavailable after {MaxConfigurationAttempts} attempts; Cadillac engine layers were not initialized.");
                    return;
                }
            }

            UpdatePlayback();
        }
        catch (NullReferenceException ex)
        {
            // NWH finishes some sound, input, and powertrain references after
            // the vehicle component has already started updating. Treat that
            // lifecycle gap as retriable instead of permanently falling back
            // to the native engine recording on the first frame.
            Cleanup();
            nextAttempt = Time.unscaledTime + .5f;
            if (attempts >= MaxConfigurationAttempts)
            {
                failed = true;
                Warn($"layered audio remained unavailable after {MaxConfigurationAttempts} attempts; native Car sound restored: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            failed = true;
            Cleanup();
            Warn($"layered audio failed; native Car sound restored: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool TryConfigure()
    {
        physics = vehicle!.GetComponent<PhysicsVehicle>();
        if (physics == null || context == null || physics.soundManager == null ||
            physics.powertrain == null || physics.powertrain.engine == null || physics.input == null)
            return false;

        engineSound = physics.soundManager.engineRunningComponent;
        native = engineSound?.source;
        if (engineSound == null || native == null || native.clip == null ||
            native.outputAudioMixerGroup == null)
            return false;

        audioHost = new GameObject("CadillacEscalade_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = native.transform.position;

        // Keep a restrained copy of the donor recording only as a smooth idle
        // bed. The six Cadillac V8 clips own the sound once RPM rises.
        idleSource = CreateSource(audioHost, native.clip, native);
        layers = new AudioSource[EngineNames.Length * 2];
        for (var i = 0; i < EngineNames.Length; i++)
        {
            layers[i] = CreateSource(audioHost, LoadClip(EngineNames[i]), native);
            layers[i + EngineNames.Length] =
                CreateSource(audioHost, LoadClip(EngineNames[i] + "Load"), native);
        }
        ConfigureEngineFilters(audioHost);

        var hornHost = new GameObject("CadillacEscalade_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var hornTemplate = physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        if (hornTemplate == null || hornTemplate.outputAudioMixerGroup == null)
            hornTemplate = native;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), hornTemplate);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), hornTemplate);

        originalDistortion = engineSound.maxDistortion;
        engineSound.maxDistortion = 0f;
        configured = true;
        CadillacEscaladeDiagnostics.Info(context,
            $"CadillacEscalade audio configured vehicle={vehicle.GetInstanceID()}, " +
            $"engineLayers=6, engineGain={CadillacEscaladeAudioModel.EngineBaseVolume:0.00}.." +
            $"{CadillacEscaladeAudioModel.EngineBaseVolume + CadillacEscaladeAudioModel.EngineThrottleVolume:0.00}, " +
            $"idleGain={CadillacEscaladeAudioModel.IdleBaseVolume:0.00}, " +
            $"hornVoices=low/high@{CadillacEscaladeAudioModel.HornLowVolume:0.00}/" +
            $"{CadillacEscaladeAudioModel.HornHighVolume:0.00}, " +
            "loadTexture=embedded-rpm-locked-v8-burble.");
        return true;
    }

    private static void ConfigureEngineFilters(GameObject host)
    {
        // Remove the synthesized clips' subsonic pressure pulse and soften
        // their upper harmonics so the large V8 does not become a toy-car tone.
        var highPass = host.AddComponent<AudioHighPassFilter>();
        highPass.cutoffFrequency = 28f;
        highPass.highpassResonanceQ = 1f;
        var lowPass = host.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = 2500f;
        lowPass.lowpassResonanceQ = 1f;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = CadillacEscaladeWave.Load(
            Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
        ownedClips.Add(clip);
        return clip;
    }

    private static AudioSource CreateSource(GameObject host, AudioClip clip, AudioSource template)
    {
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.clip = clip;
        source.volume = 0f;
        source.outputAudioMixerGroup = template.outputAudioMixerGroup;
        source.spatialBlend = template.spatialBlend;
        source.minDistance = template.minDistance;
        source.maxDistance = template.maxDistance;
        var rolloffCurve = template.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
        if (rolloffCurve != null && rolloffCurve.length > 0)
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloffCurve);
        source.rolloffMode = template.rolloffMode;
        source.priority = template.priority;
        source.dopplerLevel = 0f;
        return source;
    }

    private void UpdatePlayback()
    {
        if (physics == null || native == null || layers == null || audioHost == null ||
            idleSource == null || hornSource == null || hornSupportSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");

        audioHost.transform.position = native.transform.position;
        var controlled = vehicle!.controlledByPlayer;
        var engine = physics.powertrain.engine;
        var running = controlled && engine.ignition && engine.IsRunning && engine.canRun;
        if (controlled && !wasControlled)
        {
            smoothRpm = engine.RPMPercent * engine.revLimiterRPM;
            smoothThrottle = Mathf.Clamp01(engine.ThrottlePosition);
        }
        wasControlled = controlled;

        var shouldPause = Time.timeScale <= 0f || AudioListener.pause;
        if (shouldPause != paused)
        {
            if (shouldPause)
                StopLayers();
            paused = shouldPause;
        }

        if (controlled)
        {
            if (!ownsMute)
            {
                savedMute = native.mute;
                ownsMute = true;
            }
            native.mute = true;
            EnsureOwnedSourcesEnabled();
        }
        else
        {
            RestoreMute();
        }

        var master = Mathf.Clamp01(physics.soundManager.masterVolume);
        UpdateHorn(controlled && !paused && physics.input.Horn, master);
        if (paused)
            return;

        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        var follow = 1f - Mathf.Exp(-Time.deltaTime / .12f);
        smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
        smoothThrottle = Mathf.Lerp(
            smoothThrottle,
            Mathf.Clamp01(engine.ThrottlePosition),
            follow);
        var normalized = CadillacEscaladeAudioModel.Normalize(
            smoothRpm,
            engine.idleRPM,
            engine.revLimiterRPM);
        envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
        driveBlend = Mathf.MoveTowards(
            driveBlend,
            CadillacEscaladeAudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM),
            Time.deltaTime * 3f);
        idleSource.pitch = CadillacEscaladeAudioModel.IdlePitch;
        idleSource.volume = envelope * master * CadillacEscaladeAudioModel.IdleVolume(driveBlend);
        idleSource.mute = controlled && savedMute;

        var gain = envelope * master *
                   CadillacEscaladeAudioModel.EngineVolume(smoothThrottle) * driveBlend;
        loadBlend = CadillacEscaladeAudioModel.LoadBlend(smoothThrottle);
        for (var i = 0; i < EngineNames.Length; i++)
        {
            var pitch = CadillacEscaladeAudioModel.Pitch(normalized, i);
            layers[i].pitch = pitch;
            layers[i + EngineNames.Length].pitch = pitch;
            var bandGain = gain * CadillacEscaladeAudioModel.Weight(normalized, i);
            layers[i].volume = bandGain * (1f - loadBlend);
            layers[i + EngineNames.Length].volume = bandGain * loadBlend;
            layers[i].mute = layers[i + EngineNames.Length].mute = controlled && savedMute;
        }

        if (envelope <= 0f)
        {
            StopLayers();
        }
        else if (!voicesStarted)
        {
            var start = AudioSettings.dspTime + .03d;
            idleSource.PlayScheduled(start);
            foreach (var source in layers)
                source.PlayScheduled(start);
            voicesStarted = true;
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null)
            return;

        UpdateHornVoice(
            hornSource,
            pressed,
            pressed ? master * CadillacEscaladeAudioModel.HornLowVolume : 0f);
        UpdateHornVoice(
            hornSupportSource,
            pressed,
            pressed ? master * CadillacEscaladeAudioModel.HornHighVolume : 0f);
    }

    private void EnsureOwnedSourcesEnabled()
    {
        if (idleSource != null)
            idleSource.enabled = true;
        if (layers != null)
            foreach (var source in layers)
                if (source != null)
                    source.enabled = true;
        if (hornSource != null)
            hornSource.enabled = true;
        if (hornSupportSource != null)
            hornSupportSource.enabled = true;
    }

    private static void UpdateHornVoice(AudioSource source, bool pressed, float target)
    {
        source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * 5f);
        if (pressed && !source.isPlaying)
            source.Play();
        else if (!pressed && source.volume <= 0f && source.isPlaying)
            source.Stop();
    }

    private void StopHorn()
    {
        if (hornSource != null)
        {
            hornSource.Stop();
            hornSource.volume = 0f;
        }
        if (hornSupportSource != null)
        {
            hornSupportSource.Stop();
            hornSupportSource.volume = 0f;
        }
    }

    private void Warn(string message) =>
        context?.Logger.Warn($"CadillacEscalade audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RestoreMute()
    {
        if (ownsMute && native != null)
            native.mute = savedMute;
        ownsMute = false;
    }

    private void StopLayers()
    {
        if (idleSource != null)
            idleSource.Stop();
        if (layers != null)
            foreach (var source in layers)
                if (source != null)
                    source.Stop();
        voicesStarted = false;
    }

    private void OnDisable()
    {
        StopLayers();
        StopHorn();
        RestoreMute();
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
        hornSource = null;
        hornSupportSource = null;
        native = null;
        foreach (var clip in ownedClips)
            if (clip != null)
                Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}
