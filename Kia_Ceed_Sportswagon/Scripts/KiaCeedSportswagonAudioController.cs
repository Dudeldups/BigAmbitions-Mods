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
internal sealed class KiaCeedSportswagonAudioController : MonoBehaviour
{
    // Manual spawns can miss the game's mixer-routed engine source. Keep the
    // direct fallback audible when no vehicle SFX mixer route is available.
    private const float DirectFallbackMixGain = .75f;
    private const float EngineMinimumDistance = 4f;
    private static readonly string[] EngineNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private readonly Dictionary<AudioSource, NativeSourceState> suppressedNativeSources =
        new Dictionary<AudioSource, NativeSourceState>();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private AudioSource? sourceTemplate;
    private GameObject? audioHost;
    private AudioSource[]? layers;
    private AudioSource? idleSource;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private float originalDistortion;
    private bool configured, failed, paused, wasControlled, voicesStarted,
        usesDirectFallbackMix, nativeEnableSubscribed;
    private Coroutine? initialization;
    private int reportedPlayback;
    private double scheduledStart;
    private float smoothRpm, smoothThrottle, envelope, driveBlend, loadBlend;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        if (nativeEnableSubscribed && physics != null)
            physics.onEnable.RemoveListener(ScheduleInitialization);
        physics = controller.GetComponent<PhysicsVehicle>();
        nativeEnableSubscribed = physics != null;
        if (physics != null) physics.onEnable.AddListener(ScheduleInitialization);
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
        // NWH creates parked vehicles' audio voices when their physics activates.
        // Wait for that event's startup to finish, then bind once (no polling).
        yield return null;
        yield return null;
        initialization = null;
        try
        {
            if (configured) RefreshNativeRouting();
            else if (!TryConfigure()) Warn("native audio unavailable after vehicle activation.");
        }
        catch (Exception ex)
        {
            failed = true;
            Cleanup();
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
        var otherSource = FindRoutedSource(physics.soundManager.otherSourceGO) ??
                          physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        sourceTemplate = FindNativeTemplate(native, otherSource);
        usesDirectFallbackMix = sourceTemplate == null ||
                                sourceTemplate.outputAudioMixerGroup == null;
        originalDistortion = engineSound?.maxDistortion ?? 0f;
        audioHost = new GameObject("KiaCeedSportswagon_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = EnginePosition();
        // The donor vehicle's native engine clip belongs to a different engine;
        // use the deterministic Ceed I4 idle bed with its original mix routing.
        idleSource = CreateSource(audioHost, LoadClip("EngineLow", KiaCeedSportswagonAudioModel.EngineSampleGain), true, sourceTemplate, engineVoice: true);
        layers = new AudioSource[6];
        for (var i = 0; i < EngineNames.Length; i++)
        {
            var clip = LoadClip(EngineNames[i], KiaCeedSportswagonAudioModel.EngineSampleGain);
            layers[i] = CreateSource(audioHost, clip, true, sourceTemplate, engineVoice: true);
            var loaded = LoadClip(EngineNames[i]+"Load", KiaCeedSportswagonAudioModel.EngineSampleGain);
            layers[i + 3] = CreateSource(audioHost, loaded, true, sourceTemplate, engineVoice: true);
        }
        var hornHost = new GameObject("KiaCeedSportswagon_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var hornTemplate = otherSource ?? sourceTemplate;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, hornTemplate);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, hornTemplate);
        if (engineSound != null) engineSound.maxDistortion = 0f;
        configured = true;
        if (vehicle.controlledByPlayer) SuppressNativeEngineAudio();
        KiaCeedSportswagonDiagnostics.Info(
            context,
            $"KiaCeedSportswagon audio revision=sample-level-v4 configured vehicle={vehicle.GetInstanceID()}, " +
            $"sampleGain={KiaCeedSportswagonAudioModel.EngineSampleGain:0.00}, " +
            $"engineLayers=7, engineGain={KiaCeedSportswagonAudioModel.EngineBaseVolume:0.00}.." +
            $"{KiaCeedSportswagonAudioModel.EngineBaseVolume + KiaCeedSportswagonAudioModel.EngineThrottleVolume:0.00}, " +
            $"hornVoices=low/high@{KiaCeedSportswagonAudioModel.HornLowVolume:0.00}/" +
            $"{KiaCeedSportswagonAudioModel.HornHighVolume:0.00}, " +
            $"source={(native != null ? "native" : sourceTemplate != null ? "auxiliary" : "direct-fallback")}, " +
            $"sourceDistance={idleSource.minDistance:0.0}..{idleSource.maxDistance:0.0}, " +
            $"directMixGain={(usesDirectFallbackMix ? DirectFallbackMixGain : 1f):0.00}, " +
            "exhaust=stock-turbo-profile; no artificial overrun crackle.");
        return true;
    }

    private AudioClip LoadClip(string name, float sampleGain = 1f)
    {
        var clip = KiaCeedSportswagonWave.Load(Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"), sampleGain);
        ownedClips.Add(clip);
        return clip;
    }

    private AudioSource CreateSource(GameObject host, AudioClip clip, bool loop, AudioSource? template, bool engineVoice = false)
    {
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.clip = clip;
        source.volume = 0f;
        ApplySourceProfile(source, template, engineVoice);
        return source;
    }

    private static void ApplySourceProfile(AudioSource source, AudioSource? template, bool engineVoice = false)
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
        if (engineVoice)
        {
            // Keep the same engine distance reference for fresh and loaded cars.
            // Native voices otherwise bring a one-metre/custom attenuation profile.
            source.minDistance = EngineMinimumDistance;
            source.maxDistance = Mathf.Max(50f, source.maxDistance);
            source.rolloffMode = AudioRolloffMode.Logarithmic;
        }
        source.dopplerLevel = 0f;
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
        FindRoutedSource(physics?.soundManager.engineSourceGO) ??
        (other != null && other.outputAudioMixerGroup != null ? other : engine ?? other);

    private void RefreshNativeRouting()
    {
        if (physics == null || idleSource == null || layers == null ||
            hornSource == null || hornSupportSource == null) return;
        reportedPlayback = 0;
        RestoreNativeEngineAudio();
        var nextEngine = physics.soundManager.engineRunningComponent;
        if (engineSound != nextEngine)
        {
            if (engineSound != null) engineSound.maxDistortion = originalDistortion;
            originalDistortion = nextEngine?.maxDistortion ?? 0f;
        }
        engineSound = nextEngine;
        native = engineSound?.source;
        var other = FindRoutedSource(physics.soundManager.otherSourceGO) ??
                    physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        sourceTemplate = FindNativeTemplate(native, other);
        usesDirectFallbackMix = sourceTemplate == null || sourceTemplate.outputAudioMixerGroup == null;
        ApplySourceProfile(idleSource, sourceTemplate, engineVoice: true);
        foreach (var layer in layers) ApplySourceProfile(layer, sourceTemplate, engineVoice: true);
        ApplySourceProfile(hornSource, other ?? sourceTemplate);
        ApplySourceProfile(hornSupportSource, other ?? sourceTemplate);
        if (engineSound != null) engineSound.maxDistortion = 0f;
        if (vehicle != null && vehicle.controlledByPlayer) SuppressNativeEngineAudio();
        KiaCeedSportswagonDiagnostics.NativeAudioInfo(context,
            $"KiaCeedSportswagon audio activation vehicle={vehicle?.GetInstanceID()} " +
            $"mixer='{idleSource.outputAudioMixerGroup?.name ?? "<none>"}' fallback={usesDirectFallbackMix} " +
            $"distance={idleSource.minDistance:0.0}..{idleSource.maxDistance:0.0} " +
            $"master={physics.soundManager.masterVolume:0.00} clips={ownedClips.Count}.");
    }

    private Vector3 EnginePosition()
    {
        return native != null
            ? native.transform.position
            : vehicle!.transform.TransformPoint(new Vector3(0f, .45f, .8f));
    }

    private void UpdatePlayback()
    {
        if (physics == null || layers == null || audioHost == null ||
            hornSource == null || hornSupportSource == null || idleSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        audioHost.transform.position = EnginePosition();
        var controlled = vehicle!.controlledByPlayer;
        var engine = physics.powertrain.engine;
        var running = controlled && engine.ignition && engine.IsRunning && engine.canRun;
        if (controlled && !wasControlled)
        {
            smoothRpm = engine.RPMPercent * engine.revLimiterRPM;
            smoothThrottle = Mathf.Clamp01(engine.ThrottlePosition);
        }
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
            if (!wasControlled) SuppressNativeEngineAudio();
            if (native != null)
                SuppressNativeSource(native);
            KeepNativeAudioSuppressed();
        }
        else
        {
            RestoreNativeEngineAudio();
        }

        wasControlled = controlled;

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
            var normalized = KiaCeedSportswagonAudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            driveBlend = Mathf.MoveTowards(driveBlend,
                KiaCeedSportswagonAudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM), Time.deltaTime * 4f);
            idleSource.pitch = KiaCeedSportswagonAudioModel.IdlePitch;
            idleSource.volume = envelope * master * mixGain *
                                KiaCeedSportswagonAudioModel.IdleVolume(driveBlend);
            // Donor mute state is owned solely by suppressedNativeSources;
            // replacement voices retain their independent playback state.
            var gain = envelope * master * mixGain * KiaCeedSportswagonAudioModel.EngineVolume(smoothThrottle) *
                       Mathf.Sqrt(driveBlend);
            loadBlend = KiaCeedSportswagonAudioModel.LoadBlend(smoothThrottle);
            for (var i = 0; i < EngineNames.Length; i++)
            {
                layers[i].pitch = KiaCeedSportswagonAudioModel.Pitch(normalized,i);
                layers[i + 3].pitch = layers[i].pitch;
                var bandGain = gain * KiaCeedSportswagonAudioModel.Weight(normalized, i);
                // Matched-RMS variants change tone with load; linear interpolation
                // avoids doubling the shared harmonic content at half throttle.
                layers[i].volume = bandGain * (1f - loadBlend);
                layers[i + 3].volume = bandGain * loadBlend;
            }
            if (envelope <= 0f) StopLayers();
            else if (!voicesStarted)
            {
                // Start all layers on the same DSP boundary. An inaudible layer
                // keeps advancing so bringing it into the blend never restarts it.
                var start = AudioSettings.dspTime + .03d;
                scheduledStart = start;
                idleSource.PlayScheduled(start);
                foreach (var source in layers) source.PlayScheduled(start);
                voicesStarted = true;
            }
            if (KiaCeedSportswagonDiagnostics.DebugEnabled && KiaCeedSportswagonDiagnostics.NativeAudioDebugEnabled &&
                running && voicesStarted && AudioSettings.dspTime > scheduledStart + .1d)
            {
                // At most one idle and one driving snapshot per activation.
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
                    KiaCeedSportswagonDiagnostics.NativeAudioInfo(context,
                        $"KiaCeedSportswagon audio playback vehicle={vehicle.GetInstanceID()} " +
                        $"state={(stage == 1 ? "idle" : "drive")} rpm={rawRpm:0} throttle={smoothThrottle:0.00} " +
                        $"master={master:0.00} listener={AudioListener.volume:0.00} " +
                        $"playing={idleSource.isPlaying}/{layers[0].isPlaying} mute={idleSource.mute} " +
                        $"idleVolume={idleSource.volume:0.000} driveGain={gain:0.000} " +
                        $"sampleGain={KiaCeedSportswagonAudioModel.EngineSampleGain:0.00} " +
                        $"mixer='{idleSource.outputAudioMixerGroup?.name ?? "<none>"}' fallback={usesDirectFallbackMix} " +
                        $"engineDb={engineDb:0.0} attenuationDb={attenuationDb:0.0} " +
                        $"minDistance={idleSource.minDistance:0.0} position={audioHost.transform.position}.");
                }
            }
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null) return;
        var lowTarget = pressed ? master * KiaCeedSportswagonAudioModel.HornLowVolume : 0f;
        var highTarget = pressed ? master * KiaCeedSportswagonAudioModel.HornHighVolume : 0f;
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

    private void Warn(string message) => context?.Logger.Warn($"KiaCeedSportswagon audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private readonly struct NativeSourceState
    {
        internal NativeSourceState(AudioSource source)
        {
            Mute = source.mute;
            Volume = source.volume;
        }

        internal readonly bool Mute;
        internal readonly float Volume;
    }

    private void SuppressNativeEngineAudio()
    {
        if (physics == null)
            return;

        var liveEngine = physics.soundManager.engineRunningComponent;
        if (liveEngine?.source != null)
        {
            engineSound = liveEngine;
            native = liveEngine.source;
            SuppressNativeSource(liveEngine.source);
        }

        SuppressSourcesUnder(physics.soundManager.engineSourceGO);
        SuppressSourcesUnder(physics.soundManager.exhaustSourceGO);
    }

    private void SuppressSourcesUnder(GameObject? host)
    {
        if (host == null)
            return;

        foreach (var source in host.GetComponentsInChildren<AudioSource>(true))
            SuppressNativeSource(source);
    }

    private void SuppressNativeSource(AudioSource source)
    {
        if (source == null ||
            (audioHost != null && source.transform.IsChildOf(audioHost.transform)))
            return;

        if (!suppressedNativeSources.ContainsKey(source))
        {
            suppressedNativeSources[source] = new NativeSourceState(source);
            KiaCeedSportswagonDiagnostics.NativeAudioInfo(
                context,
                $"KiaCeedSportswagon audio suppressed donor source='{source.name}' " +
                $"vehicle={vehicle?.GetInstanceID()}.");
        }

        source.volume = 0f;
        source.mute = true;
    }

    private void RestoreNativeEngineAudio()
    {
        foreach (var pair in suppressedNativeSources)
        {
            if (pair.Key == null)
                continue;
            pair.Key.mute = pair.Value.Mute;
            pair.Key.volume = pair.Value.Volume;
        }

        suppressedNativeSources.Clear();
    }

    private void KeepNativeAudioSuppressed()
    {
        foreach (var source in suppressedNativeSources.Keys)
        {
            if (source == null)
                continue;
            source.volume = 0f;
            source.mute = true;
        }
    }

    private void StopLayers()
    {
        if (idleSource != null) idleSource.Stop();
        if (layers != null) foreach (var source in layers) if (source != null) source.Stop();
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
        RestoreNativeEngineAudio();
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
        hornSource = null;
        hornSupportSource = null;
        foreach (var clip in ownedClips) if (clip != null) Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy()
    {
        if (nativeEnableSubscribed && physics != null)
            physics.onEnable.RemoveListener(ScheduleInitialization);
        nativeEnableSubscribed = false;
        Cleanup();
    }
}

