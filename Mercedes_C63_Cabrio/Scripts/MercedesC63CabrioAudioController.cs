#nullable enable
using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using UnityEngine.Audio;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class MercedesC63CabrioAudioController : MonoBehaviour
{
    // Engine routing comes from the game vehicle mixer, never from a spawn-dependent source.
    private const float DirectHornMixGain = .16f;
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
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private AudioMixerGroup? engineMixer;
    private bool fullMenuOpen;
    private float originalDistortion;
    private bool savedMute, ownsMute, configured, failed, paused, wasControlled, voicesStarted,
        usesDirectFallbackMix;
    private float smoothRpm, smoothThrottle, envelope, driveBlend, loadBlend;
    private Coroutine? initialization;
    private Coroutine? entryRebind;
    private Coroutine? outputProbe;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        ScheduleInitialization();
    }

    private void ScheduleInitialization()
    {
        if (configured || failed || vehicle == null || !isActiveAndEnabled || initialization != null) return;
        initialization = StartCoroutine(InitializeAfterNativeStart());
    }

    private IEnumerator InitializeAfterNativeStart()
    {
        // One spawn lifecycle continuation, after native Start created its mixer/sources.
        // No recurring retry or scene discovery.
        yield return null;
        yield return null;
        initialization = null;
        try
        {
            configured = TryConfigure();
            if (!configured) Warn("vehicle context/shared Engine mixer unavailable after native Start; native sound retained until the next entry event.");
        }
        catch (Exception ex)
        {
            failed = true;
            Cleanup();
            Warn($"layered audio setup failed; native Car sound remains enabled: {ex.GetType().Name}: {ex.Message}");
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
        engineSound = physics?.soundManager?.engineRunningComponent;
        native = engineSound?.source;
        if (physics == null || context == null)
            return false;
        engineMixer = ResolveEngineMixer();
        if (engineMixer == null) return false;
        var otherSource = physics.soundManager?.otherSourceGO?.GetComponent<AudioSource>();
        // Manual spawns can omit the native engine source while still having an
        // auxiliary vehicle source with the correct mixer and spatial profile.
        sourceTemplate = native ?? otherSource;
        usesDirectFallbackMix = sourceTemplate == null ||
                                sourceTemplate.outputAudioMixerGroup == null;
        originalDistortion = engineSound?.maxDistortion ?? 0f;
        audioHost = new GameObject("MercedesC63Cabrio_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = EnginePosition();
        // Reuse the finalized Audi idle + driving mix with owned local WAVs.
        idleSource = CreateSource(audioHost,LoadClip("EngineIdle"),true,sourceTemplate);
        layers = new AudioSource[6];
        for (var i = 0; i < EngineNames.Length; i++)
        {
            var clip = LoadClip(EngineNames[i]);
            layers[i] = CreateSource(audioHost, clip, true, sourceTemplate);
            var loaded = LoadClip(EngineNames[i]+"Load");
            layers[i + 3] = CreateSource(audioHost, loaded, true, sourceTemplate);
        }
        var hornHost = new GameObject("MercedesC63Cabrio_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var hornTemplate = otherSource ?? sourceTemplate;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, hornTemplate);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, hornTemplate);
        if (engineSound != null) engineSound.maxDistortion = 0f;
        ConfigureEngineProfile(idleSource,engineMixer);
        foreach (var voice in layers) ConfigureEngineProfile(voice, engineMixer);
        configured = true;
        BindPhysicsLifecycle();
        MercedesC63CabrioDiagnostics.AudioInfo(
            context,
            $"MercedesC63Cabrio audio configured vehicle={vehicle.GetInstanceID()}, " +
            $"lowClip={layers[0].clip.name}, sharedMixer={engineMixer.name}, " +
            $"engineLayers=7, engineProfile=accepted-Audi-V8-local-copy, engineGain={MercedesC63CabrioAudioModel.EngineBaseVolume:0.00}.." +
            $"{MercedesC63CabrioAudioModel.EngineBaseVolume + MercedesC63CabrioAudioModel.EngineThrottleVolume:0.00}, " +
            $"idleGain={MercedesC63CabrioAudioModel.IdleBaseVolume:0.00}, " +
            $"hornVoices=low/high@{MercedesC63CabrioAudioModel.HornLowVolume:0.00}/" +
            $"{MercedesC63CabrioAudioModel.HornHighVolume:0.00}, " +
            $"source={(native != null ? "native" : sourceTemplate != null ? "auxiliary" : "direct-fallback")}, " +
            $"sourceDistance={layers[0].minDistance:0.0}..{layers[0].maxDistance:0.0}, " +
            "extraNoiseLayer=false, proceduralCrackle=false; shared engine routing active.");
        return true;
    }

    private AudioMixerGroup? ResolveEngineMixer()
    {
        // Current native CarController.Awake uses this exact game-owned mixer.
        // Copying engine/auxiliary AudioSource routing can instead retain the mod
        // prefab mixer (or direct output) after a developer spawn or cold reload.
        var mixer = InstanceBehavior<GlobalReferences>.Instance?.vehicleAudioMixer;
        if (mixer == null) return null;
        foreach (var group in mixer.FindMatchingGroups("Engine"))
            if (group.name == "Engine") return group;
        return null;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = MercedesC63CabrioWave.Load(Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
        ownedClips.Add(clip);
        if (clip.loadState != AudioDataLoadState.Loaded || clip.samples <= 0)
            throw new InvalidDataException("Engine clip failed to load: " + name);
        MercedesC63CabrioDiagnostics.AudioInfo(context,
            $"MercedesC63Cabrio WAV loaded path={Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav")}, " +
            $"state={clip.loadState}, samples={clip.samples}, rate={clip.frequency}, channels={clip.channels}.");
        return clip;
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
            ApplySourceTemplate(source, template);
        }
        else
        {
            // Dealer and private-driver spawns can lack the game's native engine
            // voice. Keep the custom layers audible and spatial in that case.
            source.spatialBlend = 1f;
            source.minDistance = 4f;
            source.maxDistance = 50f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.priority = 128;
        }
        source.dopplerLevel = 0f;
        return source;
    }

    private static void ApplySourceTemplate(AudioSource source, AudioSource template)
    {
        source.outputAudioMixerGroup = template.outputAudioMixerGroup;
        source.spatialBlend = template.spatialBlend;
        source.minDistance = template.minDistance;
        source.maxDistance = template.maxDistance;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        source.rolloffMode = template.rolloffMode;
        source.priority = template.priority;
    }

    internal static void ConfigureEngineProfile(AudioSource source, AudioMixerGroup mixer)
    {
        source.outputAudioMixerGroup = mixer;
        source.mute = false;
        // All engine voices share the same chase-camera profile, regardless of native source.
        source.spatialBlend = 1f;
        source.minDistance = 8f;
        source.maxDistance = 65f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.dopplerLevel = 0f;
        source.priority = 80;
    }

    private Vector3 EnginePosition() => vehicle!.transform.TransformPoint(new Vector3(0f, .45f, 1.3f));

    private void UpdatePlayback()
    {
        if (physics == null || layers == null || idleSource == null || audioHost == null ||
            hornSource == null || hornSupportSource == null)
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
        wasControlled = controlled;
        var shouldPause = Time.timeScale <= 0f || AudioListener.pause || fullMenuOpen || entryRebind != null;
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
        UpdateHorn(
            controlled && !paused && physics.input.Horn,
            Mathf.Clamp01(physics.soundManager.masterVolume) * (usesDirectFallbackMix ? DirectHornMixGain : 1f));
        if (!paused)
        {
            var follow = 1f - Mathf.Exp(-Time.deltaTime / .1f);
            smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
            smoothThrottle = Mathf.Lerp(smoothThrottle, Mathf.Clamp01(engine.ThrottlePosition), follow);
            var normalized = MercedesC63CabrioAudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            driveBlend = Mathf.MoveTowards(driveBlend,
                MercedesC63CabrioAudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM), Time.deltaTime * 4f);
            idleSource.pitch = 1f;
            idleSource.volume = envelope * master * MercedesC63CabrioAudioModel.IdleVolume(driveBlend);
            idleSource.mute = false;
            var gain = envelope * master * MercedesC63CabrioAudioModel.PlaybackVolume(driveBlend, smoothThrottle);
            loadBlend = MercedesC63CabrioAudioModel.LoadBlend(smoothThrottle);
            for (var i = 0; i < EngineNames.Length; i++)
            {
                layers[i].pitch = MercedesC63CabrioAudioModel.Pitch(normalized,i);
                layers[i + 3].pitch = layers[i].pitch;
                var bandGain = gain * MercedesC63CabrioAudioModel.Weight(normalized, i);
                // Matched-RMS variants change tone with load; linear interpolation
                // avoids doubling the shared harmonic content at half throttle.
                layers[i].volume = bandGain * (1f - loadBlend);
                layers[i + 3].volume = bandGain * loadBlend;
                layers[i].mute = layers[i + 3].mute = false;
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
                if (MercedesC63CabrioDiagnostics.DebugEnabled && MercedesC63CabrioDiagnostics.AudioDebugEnabled && outputProbe == null)
                    outputProbe=StartCoroutine(ReportEngineOutput());
                MercedesC63CabrioDiagnostics.AudioInfo(context,
                    $"MercedesC63Cabrio engine playback scheduled vehicle={vehicle.GetInstanceID()}, dsp={start:0.000}, " +
                    $"low={layers[0].clip.name}/{layers[0].clip.loadState}, lowVolume={layers[0].volume:0.000}, " +
                    $"master={master:0.000}, ownedMute={layers[0].mute}, nativeMuteSaved={savedMute}, " +
                    $"mixer={layers[0].outputAudioMixerGroup?.name ?? "missing"}, distance={layers[0].minDistance}..{layers[0].maxDistance}.");
            }
        }
    }

    private IEnumerator ReportEngineOutput()
    {
        // One bounded probe after a playback start, never recurring audio discovery.
        yield return new WaitForSecondsRealtime(.35f);
        outputProbe=null;
        if (layers == null || idleSource == null || engineMixer == null || paused || !voicesStarted) yield break;
        var samples=new float[1024];
        var peak=0f; var sumSquares=0f; var playing=0;
        foreach (var source in layers)
        {
            if (source.isPlaying) playing++;
            source.GetOutputData(samples,0);
            foreach(var value in samples) { peak=Mathf.Max(peak,Mathf.Abs(value)); sumSquares+=value*value; }
        }
        if (idleSource.isPlaying) playing++;
        idleSource.GetOutputData(samples,0);
        foreach(var value in samples) { peak=Mathf.Max(peak,Mathf.Abs(value)); sumSquares+=value*value; }
        engineMixer.audioMixer.GetFloat("engine",out var engineDb);
        engineMixer.audioMixer.GetFloat("attenuation",out var attenuationDb);
        MercedesC63CabrioDiagnostics.AudioInfo(context,
            $"MercedesC63Cabrio engine output vehicle={vehicle?.GetInstanceID()}, playing={playing}/7, " +
            $"sourcePeak={peak:0.00000}, sourceRms={Mathf.Sqrt(sumSquares/(1024f*(layers.Length+1))):0.00000}, " +
            $"gameEngineDb={engineDb:0.0}, gameAttenuationDb={attenuationDb:0.0}, " +
            $"master={physics?.soundManager?.masterVolume:0.000}, idleDrive={driveBlend:0.00}, envelope={envelope:0.00}.");
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null) return;
        var lowTarget = pressed ? master * MercedesC63CabrioAudioModel.HornLowVolume : 0f;
        var highTarget = pressed ? master * MercedesC63CabrioAudioModel.HornHighVolume : 0f;
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

    private void Warn(string message) => context?.Logger.Warn($"MercedesC63Cabrio audio vehicle={vehicle?.GetInstanceID()}: {message}");

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
        GlobalEvents.onEnterVehicle -= HandleEntered;
        GlobalEvents.onFullMenuToggle -= HandleFullMenu;
        if (physics != null) physics.onEnable.RemoveListener(HandlePhysicsEnabled);
        if (entryRebind != null) StopCoroutine(entryRebind);
        entryRebind = null;
        if (outputProbe != null) StopCoroutine(outputProbe);
        outputProbe = null;
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

    private void HandleFullMenu(bool open)
    {
        fullMenuOpen = open;
        if (open) StopLayers();
    }

    private void BindPhysicsLifecycle()
    {
        if (physics == null) return;
        physics.onEnable.RemoveListener(HandlePhysicsEnabled);
        physics.onEnable.AddListener(HandlePhysicsEnabled);
    }

    private void HandlePhysicsEnabled()
    {
        if (vehicle != null && vehicle.controlledByPlayer) HandleEntered(vehicle);
    }

    private void HandleEntered(VehicleController controller)
    {
        if (controller != vehicle) return;
        if (!configured) { ScheduleInitialization(); return; }
        if (entryRebind == null && isActiveAndEnabled) entryRebind = StartCoroutine(RebindAfterEntry(controller));
    }

    private IEnumerator RebindAfterEntry(VehicleController controller)
    {
        // Entry can precede native physics/audio enable. Rebind once after that
        // transition, rather than retaining a missing/stale source and playing twice.
        yield return null;
        yield return null;
        entryRebind = null;
        try { RebindEntered(controller); }
        catch (Exception ex)
        {
            Cleanup(); failed = true;
            Warn($"entry audio rebind failed; native sound restored: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void RebindEntered(VehicleController controller)
    {
        if (controller != vehicle) return;
        if (!configured) { ScheduleInitialization(); return; }
        var currentPhysics = physics;
        var currentLayers = layers;
        if (currentPhysics == null || currentLayers == null || currentLayers.Length == 0) return;
        StopLayers(); RestoreMute();
        if (engineSound != null) engineSound.maxDistortion = originalDistortion;
        engineSound = currentPhysics.soundManager?.engineRunningComponent;
        native = engineSound?.source;
        originalDistortion = engineSound?.maxDistortion ?? 0f;
        if (engineSound != null) engineSound.maxDistortion = 0f;
        var shared = ResolveEngineMixer();
        if (shared == null)
        {
            Cleanup(); failed = true;
            Warn("shared Engine mixer missing on entry; native audio restored.");
            return;
        }
        engineMixer = shared;
        if (idleSource != null) ConfigureEngineProfile(idleSource,shared);
        foreach (var source in currentLayers) ConfigureEngineProfile(source, shared);
        MercedesC63CabrioDiagnostics.AudioInfo(context,
            $"MercedesC63Cabrio entry audio vehicle={controller.GetInstanceID()}, clip={currentLayers[0].clip.name}, " +
            $"state={currentLayers[0].clip.loadState}, sharedMixer={shared.audioMixer.name}/{shared.name}, " +
            $"master={currentPhysics.soundManager?.masterVolume:0.000}, native={native?.GetInstanceID()}, nativeMute={native?.mute}.");
    }

    private void OnEnable()
    {
        fullMenuOpen = false;
        GlobalEvents.onEnterVehicle -= HandleEntered;
        GlobalEvents.onFullMenuToggle -= HandleFullMenu;
        GlobalEvents.onEnterVehicle += HandleEntered;
        GlobalEvents.onFullMenuToggle += HandleFullMenu;
        ScheduleInitialization();
        if (configured) BindPhysicsLifecycle();
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
        engineMixer = null;
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}

