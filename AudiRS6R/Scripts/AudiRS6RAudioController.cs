#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class AudiRS6RAudioController : MonoBehaviour
{
    private static readonly string[] EngineNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private readonly AudiRS6RPopGate popGate = new AudiRS6RPopGate();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private AudioSource? idleSource;
    private AudioSource? popSource;
    private AudioSource? hornSource;
    private AudioClip[]? popClips;
    private float originalDistortion;
    private bool savedMute, ownsMute, configured, failed, paused, wasControlled, voicesStarted;
    private int attempts;
    private float nextAttempt, smoothRpm, smoothThrottle, envelope, driveBlend, loadBlend;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        AudiRS6ROptions.Changed -= ResetExhaustPops;
        AudiRS6ROptions.Changed += ResetExhaustPops;
    }

    private void LateUpdate()
    {
        if (vehicle == null || failed) return;
        try
        {
            if (!configured)
            {
                if (attempts >= 20 || Time.unscaledTime < nextAttempt) return;
                attempts++;
                nextAttempt = Time.unscaledTime + .5f;
                if (!TryConfigure())
                {
                    if (attempts == 20)
                        Warn("native engine audio unavailable after 20 attempts; custom audio was not initialized.");
                    return;
                }
            }
            UpdatePlayback();
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
        engineSound = physics?.soundManager.engineRunningComponent;
        native = engineSound?.source;
        if (native == null || native.clip == null || native.outputAudioMixerGroup == null || context == null)
            return false;
        originalDistortion = engineSound!.maxDistortion;
        body = vehicle.GetComponent<Rigidbody>();
        if (body == null) Warn("vehicle Rigidbody unavailable; speed-dependent downshift pops will remain silent.");
        audioHost = new GameObject("AudiRS6R_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = native.transform.position;
        // Borrow the original Car clip without processing or taking ownership.
        idleSource = CreateSource(audioHost, native.clip, true);
        layers = new AudioSource[6];
        for (var i = 0; i < EngineNames.Length; i++)
        {
            var clip = LoadClip(EngineNames[i]);
            layers[i] = CreateSource(audioHost, clip, true);
            var loaded = LoadClip(EngineNames[i]+"Load");
            layers[i + 3] = CreateSource(audioHost, loaded, true);
        }
        popClips = new[] { LoadClip("ExhaustPop1"), LoadClip("ExhaustPop2"), LoadClip("ExhaustPop3") };
        var exhaustHost = new GameObject("ExhaustPops");
        exhaustHost.transform.SetParent(audioHost.transform, false);
        popSource = CreateSource(exhaustHost, popClips[0], false);
        var hornHost = new GameObject("AudiRS6R_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var otherSource = physics!.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        if (otherSource == null || otherSource.outputAudioMixerGroup == null) otherSource = native;
        hornSource = CreateSource(hornHost, LoadClip("Horn"), true, otherSource);
        engineSound.maxDistortion = 0f;
        configured = true;
        return true;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = AudiRS6RWave.Load(Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
        ownedClips.Add(clip);
        return clip;
    }

    private AudioSource CreateSource(GameObject host, AudioClip clip, bool loop, AudioSource? template = null)
    {
        template ??= native!;
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.clip = clip;
        source.volume = 0f;
        source.outputAudioMixerGroup = template.outputAudioMixerGroup;
        source.spatialBlend = template.spatialBlend;
        source.minDistance = template.minDistance;
        source.maxDistance = template.maxDistance;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        source.rolloffMode = template.rolloffMode;
        source.dopplerLevel = 0f;
        source.priority = template.priority;
        return source;
    }

    private void UpdatePlayback()
    {
        if (physics == null || native == null || layers == null || audioHost == null || popSource == null ||
            hornSource == null || idleSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        audioHost.transform.position = native.transform.position;
        var exhaust = physics.soundManager.exhaustSourceGO;
        popSource.transform.position = exhaust != null ? exhaust.transform.position :
            vehicle!.transform.TransformPoint(new Vector3(0f, .4f, -2f));
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
            // Cancel any scheduled start too; resume schedules all held loops
            // together again instead of leaving a pre-start source paused.
            if (shouldPause) StopLayers();
            // Transients are discarded on pause; never replay a stale pop on resume.
            popSource.Stop();
            popGate.Reset();
            paused = shouldPause;
        }
        if (controlled)
        {
            if (!ownsMute) { savedMute = native.mute; ownsMute = true; }
            native.mute = true;
        }
        else RestoreMute();
        if (!running)
        {
            popSource.Stop();
            popGate.Reset();
        }

        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        var gear = physics.powertrain.transmission.Gear;
        var driverThrottle = Mathf.Clamp01(physics.input.Throttle);
        UpdateHorn(controlled && !paused && physics.input.Horn, Mathf.Clamp01(physics.soundManager.masterVolume));
        if (!paused)
        {
            var follow = 1f - Mathf.Exp(-Time.deltaTime / .1f);
            smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
            smoothThrottle = Mathf.Lerp(smoothThrottle, Mathf.Clamp01(engine.ThrottlePosition), follow);
            var normalized = AudiRS6RAudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            driveBlend = Mathf.MoveTowards(driveBlend,
                AudiRS6RAudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM), Time.deltaTime * 4f);
            idleSource.pitch = AudiRS6RAudioModel.IdlePitch;
            idleSource.volume = envelope * master * AudiRS6RAudioModel.IdleVolume(driveBlend);
            idleSource.mute = controlled && savedMute;
            var gain = envelope * master * (.24f + .18f * smoothThrottle) * Mathf.Sqrt(driveBlend);
            loadBlend = AudiRS6RAudioModel.LoadBlend(smoothThrottle);
            for (var i = 0; i < EngineNames.Length; i++)
            {
                layers[i].pitch = AudiRS6RAudioModel.Pitch(normalized,i);
                layers[i + 3].pitch = layers[i].pitch;
                var bandGain = gain * AudiRS6RAudioModel.Weight(normalized, i);
                // Matched-RMS variants change tone with load; linear interpolation
                // avoids doubling the shared harmonic content at half throttle.
                layers[i].volume = bandGain * (1f - loadBlend);
                layers[i + 3].volume = bandGain * loadBlend;
                layers[i].mute = layers[i + 3].mute = controlled && savedMute;
            }
            if (envelope <= 0f) StopLayers();
            else if (!voicesStarted)
            {
                // Start all layers on the same DSP boundary. An inaudible layer
                // keeps advancing so bringing it into the blend never restarts it.
                var start = AudioSettings.dspTime + .03d;
                idleSource.PlayScheduled(start);
                foreach (var source in layers) source.PlayScheduled(start);
                voicesStarted = true;
            }
            var pop = popGate.Sample(running && !savedMute && AudiRS6ROptions.ExhaustPopsEnabled,
                Time.time, rawRpm, driverThrottle, gear, body == null ? 0f : body.velocity.magnitude*3.6f);
            if (pop != AudiRS6RPopEvent.None) PlayPop(master);
        }
    }

    private void PlayPop(float master)
    {
        var clip = popClips![UnityEngine.Random.Range(0, popClips.Length)];
        popSource!.clip = clip;
        popSource.pitch = UnityEngine.Random.Range(AudiRS6RAudioModel.PopPitchMin, AudiRS6RAudioModel.PopPitchMax);
        popSource.volume = master * AudiRS6RAudioModel.PopVolume * popGate.Intensity * UnityEngine.Random.Range(.8f, 1.1f);
        popSource.mute = savedMute;
        popSource.PlayOneShot(clip);
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null) return;
        var target = pressed ? master * AudiRS6RAudioModel.HornVolume : 0f;
        hornSource.volume = Mathf.MoveTowards(hornSource.volume, target, Time.unscaledDeltaTime * 5f);
        if (pressed && !hornSource.isPlaying)
        {
            hornSource.Play();
        }
        else if (!pressed && hornSource.volume <= 0f && hornSource.isPlaying)
        {
            hornSource.Stop();
        }
    }

    private void ResetExhaustPops()
    {
        if (popSource != null) popSource.Stop();
        popGate.Reset();
    }

    private void Warn(string message) => context?.Logger.Warn($"AudiRS6R audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RestoreMute()
    {
        if (ownsMute && native != null) native.mute = savedMute;
        ownsMute = false;
    }

    private void StopLayers()
    {
        if (idleSource != null) idleSource.Stop();
        if (layers != null) foreach (var source in layers) if (source != null) source.Stop();
        voicesStarted = false;
    }

    private void OnDisable()
    {
        StopLayers();
        if (popSource != null) popSource.Stop();
        if (hornSource != null)
        {
            hornSource.Stop();
            hornSource.volume = 0f;
        }
        popGate.Reset();
        RestoreMute();
        if (configured && engineSound != null) engineSound.maxDistortion = originalDistortion;
        envelope = smoothRpm = smoothThrottle = driveBlend = loadBlend = 0f;
        paused = wasControlled = false;
    }

    private void OnEnable()
    {
        if (configured && engineSound != null) engineSound.maxDistortion = 0f;
    }

    private void Cleanup()
    {
        OnDisable();
        configured = false;
        if (audioHost != null) Destroy(audioHost);
        audioHost = null;
        layers = null;
        idleSource = null;
        popSource = null;
        hornSource = null;
        popClips = null;
        foreach (var clip in ownedClips) if (clip != null) Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy()
    {
        AudiRS6ROptions.Changed -= ResetExhaustPops;
        Cleanup();
    }
}
