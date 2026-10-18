#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class DodgeRam1500TRXAudioController : MonoBehaviour
{
    // Manual spawns can miss the game's mixer-routed engine source. Keep the
    // unmixed fallback quiet enough to match the dealer vehicle's mixer gain.
    private const float DirectFallbackMixGain = .16f;
    private static readonly string[] EngineNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private AudioSource? sourceTemplate;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private AudioSource? idleSource;
    private AudioSource? crackleSource;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private AudioClip? crackleClip;
    private float originalDistortion;
    private bool savedMute, ownsMute, configured, failed, paused, wasControlled, voicesStarted,
        usesDirectFallbackMix, nativeEnableSubscribed;
    private bool warnedCrashAudio;
    private int crashDiagnosticCount;
    private int reportedPlayback;
    private double scheduledStart;
    private Coroutine? initialization;
    private float smoothRpm, smoothThrottle, envelope, driveBlend, loadBlend;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        if (context != null && File.Exists(Path.Combine(context.ModRootPath, "Config", "audio-diagnostics.enabled")))
        {
            DodgeRam1500TRXDiagnostics.DebugEnabled = true;
            DodgeRam1500TRXDiagnostics.AudioDebugEnabled = true;
        }
        if (nativeEnableSubscribed && physics != null)
        {
            physics.onEnable.RemoveListener(ScheduleInitialization);
            physics.onCollision.RemoveListener(OnCrashCollision);
        }
        physics = controller.GetComponent<PhysicsVehicle>();
        nativeEnableSubscribed = physics != null;
        if (physics != null)
        {
            physics.onEnable.AddListener(ScheduleInitialization);
            physics.onCollision.AddListener(OnCrashCollision);
        }
        ScheduleInitialization();
    }

    private void ScheduleInitialization()
    {
        if (failed || vehicle == null || !isActiveAndEnabled) return;
        if (initialization != null) StopCoroutine(initialization);
        initialization = StartCoroutine(InitializeAfterNativeStart());
    }

    private IEnumerator InitializeAfterNativeStart()
    {
        // Native NWH sources can appear only when entering a parked truck.
        // Its onEnable event triggers this one handoff again, without polling.
        yield return null;
        yield return null;
        initialization = null;
        try
        {
            if (configured) RefreshNativeRouting();
            else if (!TryConfigure()) Warn("native audio was unavailable after spawn initialization.");
        }
        catch (Exception ex)
        {
            failed = true; Cleanup();
            Warn($"audio initialization failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LateUpdate()
    {
        if (vehicle == null || failed || !configured) return;
        try
        {
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
        if (physics == null || context == null)
            return false;
        var otherSource = FindRoutedSource(physics.soundManager.otherSourceGO);
        // A present but unrouted engine source must not hide a valid auxiliary.
        sourceTemplate = FindNativeTemplate(native, otherSource);
        usesDirectFallbackMix = sourceTemplate == null ||
                                sourceTemplate.outputAudioMixerGroup == null;
        originalDistortion = engineSound?.maxDistortion ?? 0f;
        audioHost = new GameObject("DodgeRam1500TRX_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = EnginePosition();
        // Always use the owned V8 idle: the native Car clip is a different engine.
        // Keep its mixer/spatial routing while replacing the audible waveform.
        idleSource = CreateSource(
            audioHost,
            LoadClip("EngineLow", DodgeRam1500TRXAudioModel.EngineSampleGain),
            true,
            sourceTemplate, engineVoice: true);
        var engineLowPass = audioHost.AddComponent<AudioLowPassFilter>();
        engineLowPass.cutoffFrequency = 2300f;
        engineLowPass.lowpassResonanceQ = 1f;
        layers = new AudioSource[6];
        for (var i = 0; i < EngineNames.Length; i++)
        {
            var clip = LoadClip(EngineNames[i], DodgeRam1500TRXAudioModel.EngineSampleGain);
            layers[i] = CreateSource(audioHost, clip, true, sourceTemplate, engineVoice: true);
            var loaded = LoadClip(EngineNames[i]+"Load", DodgeRam1500TRXAudioModel.EngineSampleGain);
            layers[i + 3] = CreateSource(audioHost, loaded, true, sourceTemplate, engineVoice: true);
        }
        crackleClip = DodgeRam1500TRXCrackleWave.Create();
        var exhaustHost = new GameObject("DodgeRam1500TRX_ExhaustCrackle");
        exhaustHost.transform.SetParent(audioHost.transform, false);
        crackleSource = CreateSource(exhaustHost, crackleClip, true, sourceTemplate);
        ConfigureCrackleFilters(exhaustHost);
        var hornHost = new GameObject("DodgeRam1500TRX_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var hornTemplate = otherSource ?? sourceTemplate;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, hornTemplate);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, hornTemplate);
        if (engineSound != null) engineSound.maxDistortion = 0f;
        configured = true;
        DodgeRam1500TRXDiagnostics.AudioInfo(
            context,
            $"DodgeRam1500TRX audio revision=audible-engine-spatial-v3 configured vehicle={vehicle.GetInstanceID()}, " +
            $"engineSampleGain={DodgeRam1500TRXAudioModel.EngineSampleGain:0.00}, " +
            $"idleGain={DodgeRam1500TRXAudioModel.IdleBaseVolume:0.00}, " +
            $"engineLayers=7, engineGain={DodgeRam1500TRXAudioModel.EngineBaseVolume:0.00}.." +
            $"{DodgeRam1500TRXAudioModel.EngineBaseVolume + DodgeRam1500TRXAudioModel.EngineThrottleVolume:0.00}, " +
            $"hornVoices=low/high@{DodgeRam1500TRXAudioModel.HornLowVolume:0.00}/" +
            $"{DodgeRam1500TRXAudioModel.HornHighVolume:0.00}, " +
            $"source={(native != null ? "native" : sourceTemplate != null ? "auxiliary" : "direct-fallback")}, " +
            $"sourceDistance={idleSource.minDistance:0.0}..{idleSource.maxDistance:0.0}, " +
            $"directMixGain={(usesDirectFallbackMix ? DirectFallbackMixGain : 1f):0.00}, " +
            "exhaust=continuous-subtle-crackle.");
        return true;
    }

    // Observe native collision playback for support. NWH owns clip selection,
    // spatial/mixer settings and cooldown; never add a second playback path.
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
        if (DodgeRam1500TRXDiagnostics.DebugEnabled && DodgeRam1500TRXDiagnostics.CrashAudioDebugEnabled && crashDiagnosticCount++ < 12)
            DodgeRam1500TRXDiagnostics.CrashAudioInfo(context,
                $"DodgeRam1500TRX native crash event vehicle={vehicle.GetInstanceID()} other='{collision.collider?.name}' " +
                $"relative={collision.relativeVelocity.magnitude:0.00}mps clips={clipCount} active={crash.IsActive} " +
                $"source={crash.source != null} enabled={crash.source?.enabled} mute={crash.source?.mute} " +
                $"master={physics.soundManager.masterVolume:0.00} volume={crash.baseVolume:0.00}.");
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

    private AudioClip LoadClip(string name, float sampleGain = 1f)
    {
        var clip = DodgeRam1500TRXWave.Load(Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"), sampleGain);
        ownedClips.Add(clip);
        if (DodgeRam1500TRXDiagnostics.DebugEnabled && DodgeRam1500TRXDiagnostics.AudioDebugEnabled)
        {
            var samples = new float[clip.samples];
            if (!clip.GetData(samples, 0)) throw new InvalidDataException("Cannot inspect loaded engine clip.");
            var peak = 0f; double energy = 0d;
            foreach (var sample in samples) { peak = Mathf.Max(peak, Mathf.Abs(sample)); energy += sample * sample; }
            DodgeRam1500TRXDiagnostics.AudioInfo(context,
                $"DodgeRam1500TRX audio clip vehicle={vehicle?.GetInstanceID()} name={name} " +
                $"samples={clip.samples} rate={clip.frequency} gain={sampleGain:0.00} peak={peak:0.000} rms={Math.Sqrt(energy / samples.Length):0.000}.");
        }
        return clip;
    }

    private AudioSource CreateSource(GameObject host, AudioClip clip, bool loop, AudioSource? template, bool engineVoice = false)
    {
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.clip = clip;
        source.volume = 0f;
        if (engineVoice) ApplyEngineSourceProfile(source, template);
        else ApplySourceProfile(source, template);
        return source;
    }

    private static void ApplySourceProfile(AudioSource source, AudioSource? template)
    {
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
            // Dealer and private-driver spawns can lack the game's native engine
            // voice. Keep the custom layers audible and spatial in that case.
            source.outputAudioMixerGroup = null;
            source.spatialBlend = 1f;
            source.minDistance = 4f;
            source.maxDistance = 50f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.priority = 128;
        }
        source.dopplerLevel = 0f;
    }

    private static void ApplyEngineSourceProfile(AudioSource source, AudioSource? template)
    {
        ApplySourceProfile(source, template);
        // Keep native routing/master control, with a deliberate engine distance
        // reference instead of the one-metre native source default.
        source.minDistance = Mathf.Max(source.minDistance, DodgeRam1500TRXAudioModel.EngineMinimumDistance);
        source.maxDistance = Mathf.Max(source.maxDistance, source.minDistance + 1f);
        source.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    private static AudioSource? FindRoutedSource(GameObject? host)
    {
        if (host == null) return null;
        foreach (var source in host.GetComponentsInChildren<AudioSource>(true))
            if (source.outputAudioMixerGroup != null) return source;
        return null;
    }

    private AudioSource? FindNativeTemplate(AudioSource? engine, AudioSource? other) =>
        engine != null && engine.outputAudioMixerGroup != null ? engine :
        FindRoutedSource(physics?.soundManager.engineSourceGO) ?? other ?? engine;

    private void RefreshNativeRouting()
    {
        if (physics == null || idleSource == null || layers == null || crackleSource == null ||
            hornSource == null || hornSupportSource == null) return;
        var nextEngine = physics.soundManager.engineRunningComponent;
        var nextNative = nextEngine?.source;
        var other = FindRoutedSource(physics.soundManager.otherSourceGO);
        var template = FindNativeTemplate(nextNative, other);
        RestoreMute();
        native = nextNative;
        engineSound = nextEngine;
        sourceTemplate = template;
        usesDirectFallbackMix = template == null || template.outputAudioMixerGroup == null;
        ApplyEngineSourceProfile(idleSource, template);
        foreach (var layer in layers) ApplyEngineSourceProfile(layer, template);
        ApplySourceProfile(crackleSource, template);
        ApplySourceProfile(hornSource, other ?? template);
        ApplySourceProfile(hornSupportSource, other ?? template);
        if (engineSound != null) engineSound.maxDistortion = 0f;
        reportedPlayback = 0;
        DodgeRam1500TRXDiagnostics.AudioInfo(context,
            $"DodgeRam1500TRX audio routing-ready vehicle={vehicle?.GetInstanceID()} " +
            $"native={native != null} mixer='{idleSource.outputAudioMixerGroup?.name ?? "<none>"}' " +
            $"directMixGain={(usesDirectFallbackMix ? DirectFallbackMixGain : 1f):0.00} " +
            $"distance={idleSource.minDistance:0.0}..{idleSource.maxDistance:0.0} clips={ownedClips.Count}.");
        if (vehicle != null && vehicle.controlledByPlayer && usesDirectFallbackMix)
            Warn("native audio routing is still unavailable after vehicle activation; direct fallback remains attenuated.");
    }

    private Vector3 EnginePosition()
    {
        return native != null
            ? native.transform.position
            : vehicle!.transform.TransformPoint(new Vector3(0f, .75f, 1.8f));
    }

    private void UpdatePlayback()
    {
        if (physics == null || layers == null || audioHost == null || crackleSource == null ||
            hornSource == null || hornSupportSource == null || idleSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        audioHost.transform.position = EnginePosition();
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
        if (!controlled) reportedPlayback = 0;
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
            controlled && !paused && physics.input.Horn,
            Mathf.Clamp01(physics.soundManager.masterVolume) * mixGain);
        if (!paused)
        {
            var follow = 1f - Mathf.Exp(-Time.deltaTime / .1f);
            smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
            smoothThrottle = Mathf.Lerp(smoothThrottle, Mathf.Clamp01(engine.ThrottlePosition), follow);
            var normalized = DodgeRam1500TRXAudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            driveBlend = Mathf.MoveTowards(driveBlend,
                DodgeRam1500TRXAudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM), Time.deltaTime * 4f);
            idleSource.pitch = DodgeRam1500TRXAudioModel.IdlePitch;
            idleSource.volume = envelope * master * mixGain *
                                DodgeRam1500TRXAudioModel.IdleVolume(driveBlend);
            idleSource.mute = controlled && savedMute;
            var gain = envelope * master * mixGain * DodgeRam1500TRXAudioModel.EngineVolume(smoothThrottle) *
                       Mathf.Sqrt(driveBlend);
            loadBlend = DodgeRam1500TRXAudioModel.LoadBlend(smoothThrottle);
            for (var i = 0; i < EngineNames.Length; i++)
            {
                layers[i].pitch = DodgeRam1500TRXAudioModel.Pitch(normalized,i);
                layers[i + 3].pitch = layers[i].pitch;
                var bandGain = gain * DodgeRam1500TRXAudioModel.Weight(normalized, i);
                // Matched-RMS variants change tone with load; linear interpolation
                // avoids doubling the shared harmonic content at half throttle.
                layers[i].volume = bandGain * (1f - loadBlend);
                layers[i + 3].volume = bandGain * loadBlend;
                layers[i].mute = layers[i + 3].mute = controlled && savedMute;
            }
            var crackleLoad = Mathf.SmoothStep(0f, 1f, smoothThrottle);
            crackleSource.pitch = Mathf.Lerp(.82f, 1.08f, normalized);
            crackleSource.volume = envelope * master * mixGain * Mathf.Lerp(
                DodgeRam1500TRXAudioModel.CrackleIdleVolume,
                DodgeRam1500TRXAudioModel.CrackleLoadVolume,
                crackleLoad);
            crackleSource.mute = controlled && savedMute;
            if (envelope <= 0f) StopLayers();
            else if (!voicesStarted)
            {
                // Start all layers on the same DSP boundary. An inaudible layer
                // keeps advancing so bringing it into the blend never restarts it.
                var start = AudioSettings.dspTime + .03d;
                scheduledStart = start;
                idleSource.PlayScheduled(start);
                foreach (var source in layers) source.PlayScheduled(start);
                crackleSource.PlayScheduled(start);
                voicesStarted = true;
            }
            if (DodgeRam1500TRXDiagnostics.DebugEnabled && DodgeRam1500TRXDiagnostics.AudioDebugEnabled &&
                running && voicesStarted && AudioSettings.dspTime > scheduledStart + .1d)
            {
                var stage = driveBlend < .1f ? 1 : driveBlend > .9f ? 2 : 0;
                if (stage != 0 && (reportedPlayback & stage) == 0)
                {
                    reportedPlayback |= stage;
                    var mixer = idleSource.outputAudioMixerGroup?.audioMixer;
                    var engineDb = float.NaN; var attenuationDb = float.NaN;
                    if (mixer != null)
                    {
                        mixer.GetFloat("engine", out engineDb);
                        mixer.GetFloat("attenuation", out attenuationDb);
                    }
                    DodgeRam1500TRXDiagnostics.AudioInfo(context,
                        $"DodgeRam1500TRX audio playback vehicle={vehicle.GetInstanceID()} state={(stage == 1 ? "idle" : "drive")} " +
                        $"rpm={rawRpm:0} throttle={smoothThrottle:0.00} master={master:0.00} listener={AudioListener.volume:0.00} " +
                        $"playing={idleSource.isPlaying}/{layers[0].isPlaying} mute={idleSource.mute} " +
                        $"idleVolume={idleSource.volume:0.000} engineGain={gain:0.000} " +
                        $"mixer='{idleSource.outputAudioMixerGroup?.name ?? "<none>"}' fallback={usesDirectFallbackMix} " +
                        $"engineDb={engineDb:0.0} attenuationDb={attenuationDb:0.0} " +
                        $"minDistance={idleSource.minDistance:0.0} rolloff={idleSource.rolloffMode}.");
                }
            }
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null) return;
        var lowTarget = pressed ? master * DodgeRam1500TRXAudioModel.HornLowVolume : 0f;
        var highTarget = pressed ? master * DodgeRam1500TRXAudioModel.HornHighVolume : 0f;
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

    private void Warn(string message) => context?.Logger.Warn($"DodgeRam1500TRX audio vehicle={vehicle?.GetInstanceID()}: {message}");

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
        if (initialization != null) StopCoroutine(initialization);
        initialization = null;
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
        ScheduleInitialization();
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
        if (nativeEnableSubscribed && physics != null)
        {
            physics.onEnable.RemoveListener(ScheduleInitialization);
            physics.onCollision.RemoveListener(OnCrashCollision);
        }
        nativeEnableSubscribed = false;
        Cleanup();
    }
}

