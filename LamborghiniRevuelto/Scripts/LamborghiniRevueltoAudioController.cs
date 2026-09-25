#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class LamborghiniRevueltoAudioController : MonoBehaviour
{
    private static readonly string[] EngineNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private AudioSource? idleSource;
    private AudioSource? crackleSource;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private AudioClip? crackleClip;
    private float originalDistortion;
    private bool savedMute, ownsMute, configured, failed, paused, wasControlled, voicesStarted;
    private int attempts;
    private float nextAttempt, smoothRpm, smoothThrottle, envelope, driveBlend, loadBlend;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
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
        audioHost = new GameObject("LamborghiniRevuelto_EngineLayers");
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
        crackleClip = LamborghiniRevueltoCrackleWave.Create();
        var exhaustHost = new GameObject("LamborghiniRevuelto_ExhaustCrackle");
        exhaustHost.transform.SetParent(audioHost.transform, false);
        crackleSource = CreateSource(exhaustHost, crackleClip, true);
        ConfigureCrackleFilters(exhaustHost);
        var hornHost = new GameObject("LamborghiniRevuelto_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var otherSource = physics!.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        if (otherSource == null || otherSource.outputAudioMixerGroup == null) otherSource = native;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, otherSource);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, otherSource);
        engineSound.maxDistortion = 0f;
        configured = true;
        return true;
    }

    private static void ConfigureCrackleFilters(GameObject host)
    {
        var lowPass = host.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = 4800f;
        lowPass.lowpassResonanceQ = 1.05f;
        var highPass = host.AddComponent<AudioHighPassFilter>();
        highPass.cutoffFrequency = 420f;
        highPass.highpassResonanceQ = 1.02f;
        var distortion = host.AddComponent<AudioDistortionFilter>();
        distortion.distortionLevel = .015f;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = LamborghiniRevueltoWave.Load(Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
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
        if (physics == null || native == null || layers == null || audioHost == null || crackleSource == null ||
            hornSource == null || hornSupportSource == null || idleSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        audioHost.transform.position = native.transform.position;
        var exhaust = physics.soundManager.exhaustSourceGO;
        crackleSource.transform.position = exhaust != null ? exhaust.transform.position :
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
            paused = shouldPause;
        }
        if (controlled)
        {
            if (!ownsMute) { savedMute = native.mute; ownsMute = true; }
            native.mute = true;
        }
        else RestoreMute();

        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        UpdateHorn(controlled && !paused && physics.input.Horn, Mathf.Clamp01(physics.soundManager.masterVolume));
        if (!paused)
        {
            var follow = 1f - Mathf.Exp(-Time.deltaTime / .1f);
            smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
            smoothThrottle = Mathf.Lerp(smoothThrottle, Mathf.Clamp01(engine.ThrottlePosition), follow);
            var normalized = LamborghiniRevueltoAudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            driveBlend = Mathf.MoveTowards(driveBlend,
                LamborghiniRevueltoAudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM), Time.deltaTime * 4f);
            idleSource.pitch = LamborghiniRevueltoAudioModel.IdlePitch;
            idleSource.volume = envelope * master * LamborghiniRevueltoAudioModel.IdleVolume(driveBlend);
            idleSource.mute = controlled && savedMute;
            var gain = envelope * master * LamborghiniRevueltoAudioModel.EngineVolume(smoothThrottle) *
                       Mathf.Sqrt(driveBlend);
            loadBlend = LamborghiniRevueltoAudioModel.LoadBlend(smoothThrottle);
            for (var i = 0; i < EngineNames.Length; i++)
            {
                layers[i].pitch = LamborghiniRevueltoAudioModel.Pitch(normalized,i);
                layers[i + 3].pitch = layers[i].pitch;
                var bandGain = gain * LamborghiniRevueltoAudioModel.Weight(normalized, i);
                // Matched-RMS variants change tone with load; linear interpolation
                // avoids doubling the shared harmonic content at half throttle.
                layers[i].volume = bandGain * (1f - loadBlend);
                layers[i + 3].volume = bandGain * loadBlend;
                layers[i].mute = layers[i + 3].mute = controlled && savedMute;
            }
            var crackleLoad = Mathf.SmoothStep(0f, 1f, smoothThrottle);
            crackleSource.pitch = Mathf.Lerp(.90f, 1.22f, normalized);
            crackleSource.volume = envelope * master * Mathf.Lerp(
                LamborghiniRevueltoAudioModel.CrackleIdleVolume,
                LamborghiniRevueltoAudioModel.CrackleLoadVolume,
                crackleLoad);
            crackleSource.mute = controlled && savedMute;
            if (envelope <= 0f) StopLayers();
            else if (!voicesStarted)
            {
                // Start all layers on the same DSP boundary. An inaudible layer
                // keeps advancing so bringing it into the blend never restarts it.
                var start = AudioSettings.dspTime + .03d;
                idleSource.PlayScheduled(start);
                foreach (var source in layers) source.PlayScheduled(start);
                crackleSource.PlayScheduled(start);
                voicesStarted = true;
            }
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null) return;
        var lowTarget = pressed ? master * LamborghiniRevueltoAudioModel.HornLowVolume : 0f;
        var highTarget = pressed ? master * LamborghiniRevueltoAudioModel.HornHighVolume : 0f;
        UpdateHornVoice(hornSource, pressed, lowTarget);
        UpdateHornVoice(hornSupportSource, pressed, highTarget);
    }

    private static void UpdateHornVoice(AudioSource source, bool pressed, float target)
    {
        source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * 5f);
        if (pressed && !source.isPlaying)
            source.Play();
        else if (!pressed && source.volume <= 0f && source.isPlaying)
            source.Stop();
    }

    private void Warn(string message) => context?.Logger.Warn($"LamborghiniRevuelto audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RestoreMute()
    {
        if (ownsMute && native != null) native.mute = savedMute;
        ownsMute = false;
    }

    private void StopLayers()
    {
        if (idleSource != null) idleSource.Stop();
        if (layers != null) foreach (var source in layers) if (source != null) source.Stop();
        if (crackleSource != null) crackleSource.Stop();
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
        if (hornSupportSource != null)
        {
            hornSupportSource.Stop();
            hornSupportSource.volume = 0f;
        }
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
        crackleSource = null;
        hornSource = null;
        hornSupportSource = null;
        if (crackleClip != null) Destroy(crackleClip);
        crackleClip = null;
        foreach (var clip in ownedClips) if (clip != null) Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}
