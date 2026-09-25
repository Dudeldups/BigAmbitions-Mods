#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class BMWM4G82AudioController : MonoBehaviour
{
    private static readonly string[] EngineNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private readonly BMWM4G82ShiftPopGate shiftPopGate = new BMWM4G82ShiftPopGate();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private AudioSource? idleSource;
    private AudioSource? shiftPopSource;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private AudioClip[]? shiftPopClips;
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
                        Warn(
                            "native engine audio unavailable after 20 attempts; " +
                            $"physics={physics != null}, engineComponent={engineSound != null}, " +
                            $"source={native != null}, clip={native?.clip != null}, " +
                            $"mixer={native?.outputAudioMixerGroup != null}, context={context != null}; " +
                            "custom audio was not initialized.");
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
        // Some valid inherited player-vehicle sources route directly to the
        // AudioListener and therefore have no explicit mixer group. A missing
        // group must not block the BMW layers from initializing.
        if (native == null || native.clip == null || context == null)
            return false;
        originalDistortion = engineSound!.maxDistortion;
        body = vehicle.GetComponent<Rigidbody>();
        audioHost = new GameObject("BMWM4G82_EngineLayers");
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
        shiftPopClips = new[]
        {
            LoadClip("ExhaustPop1"),
            LoadClip("ExhaustPop2"),
            LoadClip("ExhaustPop3"),
        };
        var shiftPopHost = new GameObject("BMWM4G82_ShiftPops");
        shiftPopHost.transform.SetParent(audioHost.transform, false);
        shiftPopSource = CreateSource(shiftPopHost, shiftPopClips[0], false);
        var hornHost = new GameObject("BMWM4G82_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var otherSource = physics!.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        if (otherSource == null || otherSource.outputAudioMixerGroup == null) otherSource = native;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, otherSource);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, otherSource);
        engineSound.maxDistortion = 0f;
        configured = true;
        context.Logger.Info(
            $"BMWM4G82 audio configured vehicle={vehicle.GetInstanceID()}, " +
            $"engineLayers=7, engineGain={BMWM4G82AudioModel.EngineBaseVolume:0.00}.." +
            $"{BMWM4G82AudioModel.EngineBaseVolume + BMWM4G82AudioModel.EngineThrottleVolume:0.00}, " +
            $"hornVoices=low/high@{BMWM4G82AudioModel.HornLowVolume:0.00}/" +
            $"{BMWM4G82AudioModel.HornHighVolume:0.00}, " +
            $"sourceDistance={native.minDistance:0.0}..{native.maxDistance:0.0}, " +
            "exhaust=packaged-shift-and-overrun-pops.");
        return true;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = BMWM4G82Wave.Load(Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
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
        if (physics == null || native == null || layers == null || audioHost == null ||
            shiftPopSource == null || shiftPopClips == null ||
            hornSource == null || hornSupportSource == null || idleSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        audioHost.transform.position = native.transform.position;
        var exhaust = physics.soundManager.exhaustSourceGO;
        shiftPopSource.transform.position = exhaust != null ? exhaust.transform.position :
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
            shiftPopSource.Stop();
            shiftPopGate.Reset();
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
            shiftPopSource.Stop();
            shiftPopGate.Reset();
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
            var normalized = BMWM4G82AudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            driveBlend = Mathf.MoveTowards(driveBlend,
                BMWM4G82AudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM), Time.deltaTime * 4f);
            idleSource.pitch = BMWM4G82AudioModel.IdlePitch;
            idleSource.volume = envelope * master * BMWM4G82AudioModel.IdleVolume(driveBlend);
            idleSource.mute = controlled && savedMute;
            var gain = envelope * master * BMWM4G82AudioModel.EngineVolume(smoothThrottle) *
                       Mathf.Sqrt(driveBlend);
            loadBlend = BMWM4G82AudioModel.LoadBlend(smoothThrottle);
            for (var i = 0; i < EngineNames.Length; i++)
            {
                layers[i].pitch = BMWM4G82AudioModel.Pitch(normalized,i);
                layers[i + 3].pitch = layers[i].pitch;
                var bandGain = gain * BMWM4G82AudioModel.Weight(normalized, i);
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
            var popEvent = shiftPopGate.Sample(
                running && !savedMute,
                Time.time,
                rawRpm,
                driverThrottle,
                gear,
                body == null ? 0f : body.velocity.magnitude * 3.6f);
            if (popEvent != BMWM4G82ShiftPopEvent.None)
                PlayShiftPop(popEvent, master);
        }
    }

    private void PlayShiftPop(BMWM4G82ShiftPopEvent popEvent, float master)
    {
        if (shiftPopSource == null || shiftPopClips == null || shiftPopClips.Length == 0)
            return;
        var clip = shiftPopClips[UnityEngine.Random.Range(0, shiftPopClips.Length)];
        shiftPopSource.pitch = popEvent == BMWM4G82ShiftPopEvent.Downshift
            ? UnityEngine.Random.Range(.88f, .95f)
            : popEvent == BMWM4G82ShiftPopEvent.ThrottleLift
                ? UnityEngine.Random.Range(.92f, 1.00f)
                : UnityEngine.Random.Range(.98f, 1.045f);
        shiftPopSource.volume = master * BMWM4G82AudioModel.ShiftPopVolume *
                                shiftPopGate.Intensity * UnityEngine.Random.Range(.94f, 1.08f);
        shiftPopSource.mute = savedMute;
        shiftPopSource.PlayOneShot(clip);
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null) return;
        var lowTarget = pressed ? master * BMWM4G82AudioModel.HornLowVolume : 0f;
        var highTarget = pressed ? master * BMWM4G82AudioModel.HornHighVolume : 0f;
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

    private void Warn(string message) => context?.Logger.Warn($"BMWM4G82 audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RestoreMute()
    {
        if (ownsMute && native != null) native.mute = savedMute;
        ownsMute = false;
    }

    private void StopLayers()
    {
        if (idleSource != null) idleSource.Stop();
        if (layers != null) foreach (var source in layers) if (source != null) source.Stop();
        if (shiftPopSource != null) shiftPopSource.Stop();
        shiftPopGate.Reset();
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
        shiftPopSource = null;
        hornSource = null;
        hornSupportSource = null;
        shiftPopClips = null;
        body = null;
        foreach (var clip in ownedClips) if (clip != null) Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}
