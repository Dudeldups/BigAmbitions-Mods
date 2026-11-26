#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class LamborghiniAventadorAudioController : MonoBehaviour
{
    private static readonly string[] EngineNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private const float DirectFallbackMixGain = 0.5f;
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private AudioSource? sourceTemplate;
    private AudioSource[]? layers;
    private AudioSource? idleSource;
    private AudioSource? hornLowSource;
    private AudioSource? hornHighSource;
    private GameObject? audioHost;
    private GameObject? hornHost;
    private Coroutine? initialization;
    private float originalDistortion;
    private float smoothRpm, smoothThrottle, envelope, driveBlend, loadBlend;
    private bool configured, failed, paused, wasControlled, voicesStarted, usesDirectFallbackMix;
    private bool savedMute, ownsMute;
    private bool? lastRunningState;
    private bool? lastVoicePlaybackState;
    private bool? lastDriveLayerState;

    /// <summary>
    /// Bind once from the vehicle spawn/setup lifecycle after adding this
    /// component. Defer one frame so NWH's own component lifecycle can finish.
    /// </summary>
    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (controller == null) throw new ArgumentNullException(nameof(controller));
        if (vehicle != null && vehicle != controller)
            throw new InvalidOperationException("Aventador audio controller cannot be rebound to another vehicle.");
        vehicle = controller;
        context = modContext;
        if (!configured && initialization == null && isActiveAndEnabled)
        {
            // Spawn can precede the NWH audio manager's initialization. A later
            // vehicle lifecycle callback is a second authoritative attempt.
            failed = false;
            initialization = StartCoroutine(ConfigureAfterNativeStart());
        }
    }

    private System.Collections.IEnumerator ConfigureAfterNativeStart()
    {
        yield return null;
        try
        {
            if (TryConfigure())
                AudioInfo("configured after one deferred lifecycle attempt.");
            else
            {
                failed = true;
                Warn("vehicle physics/audio manager unavailable on the deferred lifecycle attempt; keeping native engine audio.");
            }
        }
        catch (Exception ex)
        {
            failed = true;
            initialization = null;
            Cleanup();
            Warn($"audio initialization failed: {ex.GetType().Name}: {ex.Message}; native engine audio restored.");
        }
        initialization = null;
    }

    private bool TryConfigure()
    {
        if (vehicle == null || context == null) return false;
        physics = vehicle.GetComponent<PhysicsVehicle>();
        if (physics == null || physics.soundManager == null) return false;
        engineSound = physics.soundManager.engineRunningComponent;
        native = engineSound?.source;
        var auxiliary = physics.soundManager.otherSourceGO != null
            ? physics.soundManager.otherSourceGO.GetComponent<AudioSource>()
            : null;
        sourceTemplate = native ?? auxiliary;
        usesDirectFallbackMix = sourceTemplate == null || sourceTemplate.outputAudioMixerGroup == null;
        originalDistortion = engineSound?.maxDistortion ?? 0f;
        var enginePosition = sourceTemplate != null
            ? sourceTemplate.transform.position
            : vehicle.transform.position;
        audioHost = new GameObject("LamborghiniAventador_EngineAudio");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = enginePosition;

        var coastClips = new AudioClip[EngineNames.Length];
        var loadClips = new AudioClip[EngineNames.Length];
        layers = new AudioSource[6];
        for (var i = 0; i < EngineNames.Length; i++)
        {
            coastClips[i] = LamborghiniAventadorAudioWave.LoadEngineClip(Path.Combine(
                context.ModRootPath, "Config", "Audio", EngineNames[i] + ".wav"));
            loadClips[i] = LamborghiniAventadorAudioWave.LoadEngineClip(Path.Combine(
                context.ModRootPath, "Config", "Audio", EngineNames[i] + "Load.wav"));
            layers[i] = CreateSource(audioHost, coastClips[i], sourceTemplate,
                minimumDistance: LamborghiniAventadorAudioModel.EngineMinDistance);
            layers[i + 3] = CreateSource(audioHost, loadClips[i], sourceTemplate,
                minimumDistance: LamborghiniAventadorAudioModel.EngineMinDistance);
        }
        // Use the game's own idle clip when available. If a manual/dealer spawn
        // omitted that source, EngineLow is the same Revuelto recording already
        // used by the layered voices.
        var idleClip = native != null && native.clip != null ? native.clip : coastClips[0];
        idleSource = CreateSource(audioHost, idleClip, sourceTemplate, ownsClip: false,
            minimumDistance: LamborghiniAventadorAudioModel.EngineMinDistance);
        hornHost = new GameObject("LamborghiniAventador_Horn");
        hornHost.transform.SetParent(vehicle.transform, false);
        hornHost.transform.localPosition = new Vector3(0f, .45f, 2.05f);
        hornLowSource = CreateSource(hornHost,
            LamborghiniAventadorAudioWave.CreateHorn("Aventador horn low", 294f), sourceTemplate);
        hornHighSource = CreateSource(hornHost,
            LamborghiniAventadorAudioWave.CreateHorn("Aventador horn high", 370f), sourceTemplate);

        if (engineSound != null) engineSound.maxDistortion = 0f;
        configured = true;
        var clipSummaryParts = new string[EngineNames.Length * 2];
        for (var i = 0; i < EngineNames.Length; i++)
        {
            clipSummaryParts[i] = $"{coastClips[i].name}={coastClips[i].length:0.00}s/{coastClips[i].frequency}Hz";
            clipSummaryParts[i + EngineNames.Length] =
                $"{loadClips[i].name}={loadClips[i].length:0.00}s/{loadClips[i].frequency}Hz";
        }
        var clipSummary = string.Join(", ", clipSummaryParts);
        AudioInfo($"configured vehicle={vehicle.GetInstanceID()}, engineLayers=6, " +
                  $"nativeClip='{native?.clip?.name ?? "<none>"}', source=" +
                  $"{(native != null ? "native" : sourceTemplate != null ? "auxiliary" : "direct-fallback")}, " +
                  $"mixer={(usesDirectFallbackMix ? "direct" : "game")}, " +
                  $"engine-source=spatial:{idleSource.spatialBlend:0.00}," +
                  $"rolloff:{idleSource.rolloffMode},distance:{idleSource.minDistance:0.0}..{idleSource.maxDistance:0.0}," +
                  $"group='{idleSource.outputAudioMixerGroup?.name ?? "<none>"}', " +
                  $"engineMasterGain={LamborghiniAventadorAudioModel.EngineMasterGain:0.00}, " +
                  $"idleGain={LamborghiniAventadorAudioModel.IdleBaseVolume:0.00}, " +
                  $"driveGain={LamborghiniAventadorAudioModel.EngineBaseVolume:0.00}+" +
                  $"{LamborghiniAventadorAudioModel.EngineThrottleVolume:0.00}*throttle, " +
                  $"Revuelto WAVs=[{clipSummary}] + load variants verified while loading.");
        return true;
    }

    private AudioSource CreateSource(GameObject host, AudioClip clip, AudioSource? template,
        bool ownsClip = true, float? minimumDistance = null)
    {
        if (ownsClip) ownedClips.Add(clip);
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.clip = clip;
        source.volume = 0f;
        // NWH uses the native source's mute flag to suppress its own engine
        // loop when driving. These replacement voices share the native mixer,
        // master volume and AudioListener pause state, but must not inherit that
        // source-local suppression or the controlled engine becomes silent.
        source.mute = false;
        source.outputAudioMixerGroup = template?.outputAudioMixerGroup;
        if (template != null)
        {
            source.spatialBlend = template.spatialBlend;
            source.minDistance = minimumDistance ?? template.minDistance;
            source.maxDistance = template.maxDistance;
            source.rolloffMode = template.rolloffMode;
            source.priority = template.priority;
        }
        else
        {
            source.spatialBlend = 1f;
            source.minDistance = minimumDistance ?? 4f;
            source.maxDistance = 50f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.priority = 128;
        }
        if (template != null && template.rolloffMode == AudioRolloffMode.Custom)
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        source.dopplerLevel = 0f;
        return source;
    }

    private void LateUpdate()
    {
        if (!configured || failed) return;
        try { UpdatePlayback(); }
        catch (Exception ex)
        {
            failed = true;
            Cleanup();
            Warn($"layered playback failed: {ex.GetType().Name}: {ex.Message}; native engine audio restored.");
        }
    }

    private void UpdatePlayback()
    {
        if (vehicle == null || physics == null || layers == null || idleSource == null ||
            hornLowSource == null || hornHighSource == null)
            throw new InvalidOperationException("Configured vehicle audio references were removed.");

        audioHost!.transform.position = native != null ? native.transform.position : vehicle.transform.position;
        var controlled = vehicle.controlledByPlayer;
        var engine = physics.powertrain.engine;
        var running = controlled && engine.ignition && engine.IsRunning && engine.canRun;
        if (lastRunningState != running)
        {
            AudioInfo($"playback state vehicle={vehicle.GetInstanceID()} controlled={controlled} " +
                      $"ignition={engine.ignition} running={running} canRun={engine.canRun} " +
                      $"rpm={engine.RPMPercent:0.00} throttle={engine.ThrottlePosition:0.00}.");
            lastRunningState = running;
        }
        if (controlled && !wasControlled)
        {
            smoothRpm = engine.RPMPercent * engine.revLimiterRPM;
            smoothThrottle = Mathf.Clamp01(engine.ThrottlePosition);
        }
        wasControlled = controlled;

        var shouldPause = Time.timeScale <= 0f || AudioListener.pause;
        if (shouldPause != paused)
        {
            if (shouldPause) StopEngineVoices();
            paused = shouldPause;
        }

        if (controlled && native != null)
        {
            if (!ownsMute) { savedMute = native.mute; ownsMute = true; }
            native.mute = true;
        }
        else RestoreMute();

        var master = Mathf.Clamp01(physics.soundManager.masterVolume);
        var mixGain = usesDirectFallbackMix ? DirectFallbackMixGain : 1f;
        UpdateHorn(controlled && !paused && physics.input.Horn, master * mixGain);
        if (paused) return;

        var follow = 1f - Mathf.Exp(-Time.deltaTime / .10f);
        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
        smoothThrottle = Mathf.Lerp(smoothThrottle, Mathf.Clamp01(engine.ThrottlePosition), follow);
        var normalized = LamborghiniAventadorAudioModel.Normalize(
            smoothRpm, engine.idleRPM, engine.revLimiterRPM);
        envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
        driveBlend = Mathf.MoveTowards(driveBlend,
            LamborghiniAventadorAudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM),
            Time.deltaTime * 4f);
        idleSource.pitch = 1f;
        idleSource.volume = Mathf.Clamp01(
            envelope * master * mixGain * LamborghiniAventadorAudioModel.EngineMasterGain *
            LamborghiniAventadorAudioModel.IdleVolume(driveBlend));
        idleSource.mute = false;

        var gain = envelope * master * mixGain * LamborghiniAventadorAudioModel.EngineMasterGain *
                   LamborghiniAventadorAudioModel.EngineVolume(smoothThrottle) * Mathf.Sqrt(driveBlend);
        loadBlend = LamborghiniAventadorAudioModel.LoadBlend(smoothThrottle);
        var maximumLayerGain = 0f;
        for (var i = 0; i < EngineNames.Length; i++)
        {
            var pitch = LamborghiniAventadorAudioModel.Pitch(normalized, i);
            layers[i].pitch = layers[i + 3].pitch = pitch;
            var band = gain * LamborghiniAventadorAudioModel.Weight(normalized, i);
            layers[i].volume = Mathf.Clamp01(band * (1f - loadBlend));
            layers[i + 3].volume = Mathf.Clamp01(band * loadBlend);
            if (layers[i].volume > maximumLayerGain) maximumLayerGain = layers[i].volume;
            if (layers[i + 3].volume > maximumLayerGain) maximumLayerGain = layers[i + 3].volume;
            layers[i].mute = layers[i + 3].mute = false;
        }

        var driveLayersActive = driveBlend >= .25f && maximumLayerGain >= .05f;
        if (lastDriveLayerState != driveLayersActive)
        {
            AudioInfo($"drive layers vehicle={vehicle.GetInstanceID()} active={driveLayersActive} " +
                      $"maxLayerGain={maximumLayerGain:0.000} master={master:0.00} directMix={mixGain:0.00} " +
                      $"rpm={rawRpm:0} throttle={smoothThrottle:0.00} driveBlend={driveBlend:0.00} " +
                      $"sourceDistance={idleSource.minDistance:0.0}..{idleSource.maxDistance:0.0} " +
                      $"group='{idleSource.outputAudioMixerGroup?.name ?? "<none>"}'.");
            lastDriveLayerState = driveLayersActive;
        }

        if (envelope <= 0f) StopEngineVoices();
        else if (!voicesStarted)
        {
            var start = AudioSettings.dspTime + .03d;
            idleSource.PlayScheduled(start);
            foreach (var source in layers) source.PlayScheduled(start);
            voicesStarted = true;
        }

        var voicePlaybackActive = idleSource.isPlaying;
        if (lastVoicePlaybackState != voicePlaybackActive)
        {
            AudioInfo($"voice playback vehicle={vehicle.GetInstanceID()} active={voicePlaybackActive} " +
                      $"idleGain={idleSource.volume:0.000} maxLayerGain={maximumLayerGain:0.000} " +
                      $"master={master:0.00} directMix={mixGain:0.00} rpm={rawRpm:0} " +
                      $"throttle={smoothThrottle:0.00} driveBlend={driveBlend:0.00} " +
                      $"sourceDistance={idleSource.minDistance:0.0}..{idleSource.maxDistance:0.0} " +
                      $"group='{idleSource.outputAudioMixerGroup?.name ?? "<none>"}'.");
            lastVoicePlaybackState = voicePlaybackActive;
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        UpdateHornVoice(hornLowSource!, pressed, pressed ? master * LamborghiniAventadorAudioModel.HornLowVolume : 0f);
        UpdateHornVoice(hornHighSource!, pressed, pressed ? master * LamborghiniAventadorAudioModel.HornHighVolume : 0f);
    }

    private static void UpdateHornVoice(AudioSource source, bool pressed, float target)
    {
        source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * 5f);
        if (pressed && !source.isPlaying) source.Play();
        else if (!pressed && source.volume <= 0f && source.isPlaying) source.Stop();
    }

    private void AudioInfo(string message)
    {
        if (LamborghiniAventadorDiagnostics.DebugEnabled &&
            LamborghiniAventadorAudioModel.AudioDebugEnabled)
            LamborghiniAventadorDiagnostics.Info(context, "LamborghiniAventador audio " + message);
    }

    private void Warn(string message) =>
        context?.Logger.Warn($"LamborghiniAventador audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RestoreMute()
    {
        if (ownsMute && native != null) native.mute = savedMute;
        ownsMute = false;
    }

    private void StopEngineVoices()
    {
        idleSource?.Stop();
        if (layers != null) foreach (var source in layers) source?.Stop();
        voicesStarted = false;
    }

    private void OnDisable()
    {
        if (initialization != null)
        {
            StopCoroutine(initialization);
            initialization = null;
        }
        StopEngineVoices();
        hornLowSource?.Stop();
        hornHighSource?.Stop();
        if (hornLowSource != null) hornLowSource.volume = 0f;
        if (hornHighSource != null) hornHighSource.volume = 0f;
        RestoreMute();
        if (configured && engineSound != null) engineSound.maxDistortion = originalDistortion;
        paused = wasControlled = false;
        lastRunningState = null;
        lastVoicePlaybackState = null;
        lastDriveLayerState = null;
        envelope = smoothRpm = smoothThrottle = driveBlend = loadBlend = 0f;
    }

    private void OnEnable()
    {
        if (vehicle != null && !configured && !failed && initialization == null)
            initialization = StartCoroutine(ConfigureAfterNativeStart());
        if (configured && engineSound != null) engineSound.maxDistortion = 0f;
    }

    private void Cleanup()
    {
        OnDisable();
        configured = false;
        if (audioHost != null) Destroy(audioHost);
        if (hornHost != null) Destroy(hornHost);
        audioHost = null;
        hornHost = null;
        layers = null;
        idleSource = hornLowSource = hornHighSource = null;
        foreach (var clip in ownedClips) if (clip != null) Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy() => Cleanup();
}
