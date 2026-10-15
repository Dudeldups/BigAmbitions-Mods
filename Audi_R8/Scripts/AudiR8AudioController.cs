#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using BigAmbitions.InputSystem;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using UnityEngine.InputSystem;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class AudiR8AudioController : MonoBehaviour
{
    // Manual spawns can miss the game's mixer-routed engine source. Prefer
    // the actual engine bus; retain an audible, spatial fallback if absent.
    private const float DirectFallbackMixGain = .16f;
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private AudioSource? sourceTemplate;
    private GameObject? audioHost;
    private AudioSource? engineSource;
    private AudioSource[] engineBands = Array.Empty<AudioSource>();
    private readonly List<(SoundComponent component, float volume)> suppressedSounds = new();
    private InputAction? hornAction;
    private bool controlled, engineRunning, hornPressed;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private float originalDistortion;
    private bool savedMute, ownsMute, configured, failed, paused, wasControlled, voicesStarted,
        usesDirectFallbackMix;
    private float smoothRpm, smoothThrottle, envelope, driveBlend;
    private bool warnedCrashAudio;
    private int crashDiagnosticCount;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        physics = vehicle.GetComponent<PhysicsVehicle>();
        GlobalEvents.onEnterVehicle += OnEntered;
        GlobalEvents.onExitVehicle += OnExited;
        GlobalEvents.onVehicleVariablesChanged += OnVariablesChanged;
        if (physics != null)
        {
            physics.onVehicleInitialized.AddListener(OnPhysicsInitialized);
            physics.onCollision.AddListener(OnCrashCollision);
        }
        OnPhysicsInitialized();
        OnVariablesChanged();
    }

    private void LateUpdate()
    {
        if (vehicle == null || failed) return;
        try
        {
            if (!configured) return;
            UpdatePlayback();
        }
        catch (Exception ex)
        {
            failed = true;
            Cleanup();
            Warn($"engine audio failed; native Car sound restored: {ex.GetType().Name}: {ex.Message}");
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
        // Manual spawns can omit the native engine source while still having an
        // auxiliary vehicle source with the correct mixer and spatial profile.
        sourceTemplate = native ?? otherSource;
        var mixer = native?.outputAudioMixerGroup ?? physics.soundManager.engineMixerGroup ??
                    GlobalReferences.Instance?.playerVehicleMixerGroup;
        usesDirectFallbackMix = mixer == null;
        originalDistortion = engineSound?.maxDistortion ?? 0f;
        audioHost = new GameObject("AudiR8_EngineVoice");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = EnginePosition();
        // Use the existing sportscar's separate continuous idle and six
        // coast/load clips unchanged. No recording edits or generated layers.
        engineSource = CreateSource(audioHost, LoadClip("EngineIdle"), true, sourceTemplate);
        engineSource.outputAudioMixerGroup = mixer;
        engineBands = new AudioSource[6];
        var names = new[] { "EngineLow", "EngineMid", "EngineHigh" };
        for (var i = 0; i < engineBands.Length; i++)
        {
            var name = names[i % 3] + (i >= 3 ? "Load" : "");
            engineBands[i] = CreateSource(audioHost, LoadClip(name), true, sourceTemplate);
            engineBands[i].outputAudioMixerGroup = mixer;
        }
        var hornHost = new GameObject("AudiR8_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var hornTemplate = otherSource ?? sourceTemplate;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, hornTemplate);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, hornTemplate);
        if (engineSound != null) engineSound.maxDistortion = 0f;
        Suppress(physics.soundManager.engineFanComponent);
        Suppress(physics.soundManager.transmissionWhineComponent);
        Suppress(physics.soundManager.turboWhistleComponent);
        Suppress(physics.soundManager.turboFlutterComponent);
        Suppress(physics.soundManager.exhaustPopComponent);
        var actions = typeof(InputActionHelper).GetField("PlayerInputActionMap",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic)?.GetValue(null) as Dictionary<PlayerAction, InputAction>;
        if (actions != null && actions.TryGetValue(PlayerAction.VehicleHorn, out hornAction))
        { hornAction.performed += OnHornPressed; hornAction.canceled += OnHornReleased; }
        configured = true;
        AudiR8Diagnostics.AudioInfo(
            context,
            $"AudiR8 audio configured vehicle={vehicle.GetInstanceID()}, " +
            $"idleClip=EngineIdle, engineVoices=7, engineGain={AudiR8AudioModel.EngineBaseVolume:0.00}.." +
            $"{AudiR8AudioModel.EngineBaseVolume + AudiR8AudioModel.EngineThrottleVolume:0.00}, " +
            $"hornVoices=low/high@{AudiR8AudioModel.HornLowVolume:0.00}/" +
            $"{AudiR8AudioModel.HornHighVolume:0.00}, " +
            $"source={(native != null ? "native" : sourceTemplate != null ? "auxiliary" : "direct-fallback")}, " +
            $"sourceDistance={engineSource.minDistance:0.0}..{engineSource.maxDistance:0.0}, " +
            $"directMixGain={(usesDirectFallbackMix ? DirectFallbackMixGain : 1f):0.00}, " +
            "engine=unchanged existing sportscar clips/mapping; auxiliary whine/pop voices suppressed.");
        return true;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = AudiR8Wave.Load(Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
        ownedClips.Add(clip);
        return clip;
    }

    // Observe the native event for support only; NWH owns playback, including
    // its cooldown. A second custom Play call would duplicate every crash.
    private void OnCrashCollision(Collision collision)
    {
        if (vehicle?.controlledByPlayer != true || physics == null || collision.contactCount == 0 ||
            !NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision)) return;
        var crash = physics.soundManager.crashComponent;
        var clipCount = 0;
        foreach (var clip in crash.clips) if (clip != null) clipCount++;
        if (!warnedCrashAudio && (clipCount == 0 || crash.source == null))
        {
            warnedCrashAudio = true;
            Warn($"native crash audio unavailable: clips={clipCount}, source={crash.source != null}.");
        }
        if (AudiR8Diagnostics.DebugEnabled && AudiR8Diagnostics.CrashAudioDebugEnabled && crashDiagnosticCount++ < 12)
            AudiR8Diagnostics.CrashAudioInfo(context,
                $"AudiR8 native crash event vehicle={vehicle.GetInstanceID()} other='{collision.collider?.name}' " +
                $"relative={collision.relativeVelocity.magnitude:0.00}mps clips={clipCount} active={crash.IsActive} " +
                $"source={crash.source != null} enabled={crash.source?.enabled} mute={crash.source?.mute} " +
                $"master={physics.soundManager.masterVolume:0.00} volume={crash.baseVolume:0.00}.");
    }

    private void OnPhysicsInitialized()
    {
        if (configured || failed || physics == null) return;
        try
        {
            if (!TryConfigure()) { Warn("engine audio initialization unavailable."); return; }
            physics.powertrain.engine.onStart.AddListener(OnEngineStarted);
            physics.powertrain.engine.onStop.AddListener(OnEngineStopped);
            OnVariablesChanged();
        }
        catch (Exception ex) { failed = true; Cleanup(); Warn($"audio initialization failed: {ex.Message}"); }
    }

    private void OnEntered(VehicleController target) { if (target == vehicle) OnVariablesChanged(); }
    private void OnExited(VehicleController target)
    {
        if (target != vehicle) return;
        controlled = hornPressed = false;
        RestoreMute();
    }
    private void OnVariablesChanged()
    {
        controlled = vehicle != null && vehicle.controlledByPlayer;
        var engine = physics?.powertrain.engine;
        // IsRunning settles during the next physics integration after onStart.
        // Entry/variable callbacks must not cancel the start event in that gap.
        engineRunning = engine != null && (engine.IsRunning || engine.ignition) && engine.canRun;
    }
    private void OnEngineStarted() => engineRunning = true;
    private void OnEngineStopped() => engineRunning = false;
    private void OnHornPressed(InputAction.CallbackContext _) => hornPressed = controlled;
    private void OnHornReleased(InputAction.CallbackContext _) => hornPressed = false;

    private void Suppress(SoundComponent component)
    {
        suppressedSounds.Add((component, component.baseVolume));
        component.baseVolume = 0f;
        if (component.source != null) component.source.volume = 0f;
    }

    private void SetBand(int index, float normalized, float load, float gain)
    {
        var source = engineBands[index];
        var band = index % 3;
        source.pitch = AudiR8AudioModel.Pitch(normalized, band);
        source.volume = gain * AudiR8AudioModel.Weight(normalized, band) *
                        (index >= 3 ? load : 1f - load);
        // Native sources can still be muted by their parked/startup lifecycle
        // when control changes. Mixer/master gain owns the user's sound settings.
        source.mute = false;
    }

    private AudioSource CreateSource(GameObject host, AudioClip clip, bool loop, AudioSource? template)
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
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
            source.rolloffMode = template.rolloffMode;
            source.priority = template.priority;
        }
        else
        {
            // Dealer spawns can lack the game's native engine voice. Keep the
            // custom engine voice audible and spatial in that case.
            source.spatialBlend = 1f;
            source.minDistance = 4f;
            source.maxDistance = 50f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.priority = 128;
        }
        source.dopplerLevel = 0f;
        return source;
    }

    private Vector3 EnginePosition()
    {
        return native != null
            ? native.transform.position
            : vehicle!.transform.TransformPoint(new Vector3(0f, .45f, -.8f));
    }

    private void UpdatePlayback()
    {
        if (physics == null || engineSource == null || audioHost == null ||
            hornSource == null || hornSupportSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        audioHost.transform.position = EnginePosition();
        var engine = physics.powertrain.engine;
        var running = controlled && engineRunning;
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
        if (controlled && native != null)
        {
            if (!ownsMute) { savedMute = native.mute; ownsMute = true; }
            native.mute = true;
        }
        else RestoreMute();

        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        var mixGain = usesDirectFallbackMix ? DirectFallbackMixGain : 1f;
        UpdateHorn(
            controlled && !paused && hornPressed,
            Mathf.Clamp01(physics.soundManager.masterVolume) * mixGain);
        if (!paused)
        {
            var follow = 1f - Mathf.Exp(-Time.deltaTime / .1f);
            smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
            smoothThrottle = Mathf.Lerp(smoothThrottle, Mathf.Clamp01(engine.ThrottlePosition), follow);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            driveBlend = Mathf.MoveTowards(driveBlend,
                AudiR8AudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM), Time.deltaTime * 4f);
            var normalized = AudiR8AudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            engineSource.pitch = AudiR8AudioModel.IdlePitch;
            engineSource.volume = envelope * master * mixGain * AudiR8AudioModel.IdleVolume(driveBlend);
            engineSource.mute = false;
            var gain = envelope * master * mixGain * AudiR8AudioModel.EngineVolume(smoothThrottle) * Mathf.Sqrt(driveBlend);
            var load = AudiR8AudioModel.LoadBlend(smoothThrottle);
            for (var i = 0; i < engineBands.Length; i++) SetBand(i, normalized, load, gain);
            if (envelope <= 0f) StopLayers();
            else if (!voicesStarted)
            {
                var start = AudioSettings.dspTime + .03d;
                engineSource.PlayScheduled(start);
                foreach (var band in engineBands) band.PlayScheduled(start);
                voicesStarted = true;
            }
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null) return;
        var lowTarget = pressed ? master * AudiR8AudioModel.HornLowVolume : 0f;
        var highTarget = pressed ? master * AudiR8AudioModel.HornHighVolume : 0f;
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

    private void Warn(string message) => context?.Logger.Warn($"AudiR8 audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RestoreMute()
    {
        if (ownsMute && native != null) native.mute = savedMute;
        ownsMute = false;
    }

    private void StopLayers()
    {
        if (engineSource != null) engineSource.Stop();
        foreach (var band in engineBands) if (band != null) band.Stop();
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
        foreach (var sound in suppressedSounds) sound.component.baseVolume = sound.volume;
        envelope = smoothRpm = smoothThrottle = driveBlend = 0f;
        paused = wasControlled = false;
    }

    private void OnEnable()
    {
        if (configured && engineSound != null) engineSound.maxDistortion = 0f;
        foreach (var sound in suppressedSounds) sound.component.baseVolume = 0f;
        if (vehicle != null) OnVariablesChanged();
    }

    private void Cleanup()
    {
        OnDisable();
        GlobalEvents.onEnterVehicle -= OnEntered;
        GlobalEvents.onExitVehicle -= OnExited;
        GlobalEvents.onVehicleVariablesChanged -= OnVariablesChanged;
        if (physics != null)
        {
            physics.onVehicleInitialized.RemoveListener(OnPhysicsInitialized);
            physics.onCollision.RemoveListener(OnCrashCollision);
            physics.powertrain.engine.onStart.RemoveListener(OnEngineStarted);
            physics.powertrain.engine.onStop.RemoveListener(OnEngineStopped);
        }
        if (hornAction != null)
        { hornAction.performed -= OnHornPressed; hornAction.canceled -= OnHornReleased; }
        foreach (var sound in suppressedSounds) sound.component.baseVolume = sound.volume;
        suppressedSounds.Clear();
        configured = false;
        if (audioHost != null) Destroy(audioHost);
        audioHost = null;
        engineSource = null;
        engineBands = Array.Empty<AudioSource>();
        hornSource = null;
        hornSupportSource = null;
        foreach (var clip in ownedClips) if (clip != null) Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}


