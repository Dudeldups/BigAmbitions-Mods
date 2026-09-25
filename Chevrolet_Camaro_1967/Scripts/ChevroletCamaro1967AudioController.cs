#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class ChevroletCamaro1967AudioController : MonoBehaviour
{
    // Manual spawns can miss the game's mixer-routed engine source. Keep the
    // unmixed fallback quiet enough to match the dealer vehicle's mixer gain.
    private const float DirectFallbackMixGain = .28f;
    private const float DirectFallbackIdleBoost = 1.45f;
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
    private AudioSource? idleRumbleSource;
    private AudioSource? idleBurbleSource;
    private AudioSource? crackleSource;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private AudioClip? crackleClip;
    private float originalDistortion;
    private bool configured, failed, paused, wasControlled, voicesStarted,
        usesDirectFallbackMix;
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
        if (physics == null || context == null)
            return false;
        var otherSource = physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        // Manual spawns can omit the native engine source while still having an
        // auxiliary vehicle source with the correct mixer and spatial profile.
        sourceTemplate = native ?? otherSource;
        usesDirectFallbackMix = sourceTemplate == null ||
                                sourceTemplate.outputAudioMixerGroup == null;
        originalDistortion = engineSound?.maxDistortion ?? 0f;
        audioHost = new GameObject("ChevroletCamaro1967_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = EnginePosition();
        // Preserve the existing native-idle/EngineIdle primary selection. The
        // rejected extra voice is replaced, not the last usable primary sound.
        var customIdleClip = LoadClip("EngineIdle");
        idleSource = CreateSource(
            audioHost,
            native?.clip ?? customIdleClip,
            true,
            sourceTemplate);
        if (native?.clip != null)
        {
            // Preserve the accepted 82/20 idle mix.
            idleRumbleSource = CreateSource(
                audioHost,
                customIdleClip,
                true,
                sourceTemplate);
        }

        // Smooth overlapping pressure events, not the old hard-reset Burble2.
        // Also present on manual spawns without a native engine AudioSource.
        idleBurbleSource = CreateSource(
            audioHost,
            LoadClip("EngineIdleBurbleSmooth"),
            true,
            sourceTemplate);

        // Keep drive filtering isolated from the idle source. The first good
        // Camaro idle used the native idle clip without the later 900-Hz host
        // low-pass that changed its character.
        var driveHost = new GameObject("ChevroletCamaro1967_DriveLayers");
        driveHost.transform.SetParent(audioHost.transform, false);
        layers = new AudioSource[6];
        for (var i = 0; i < EngineNames.Length; i++)
        {
            var clip = LoadClip(EngineNames[i]);
            layers[i] = CreateSource(driveHost, clip, true, sourceTemplate);
            var loaded = LoadClip(EngineNames[i]+"Load");
            layers[i + 3] = CreateSource(driveHost, loaded, true, sourceTemplate);
        }
        ConfigureDriveFilters(driveHost);
        crackleClip = ChevroletCamaro1967CrackleWave.Create();
        var exhaustHost = new GameObject("ChevroletCamaro1967_ExhaustCrackle");
        exhaustHost.transform.SetParent(audioHost.transform, false);
        crackleSource = CreateSource(exhaustHost, crackleClip, true, sourceTemplate);
        // This is the subtle filtered pulse bed from the first test whose idle
        // had the desired classic burble. It is now strictly an idle-only layer.
        ConfigureCrackleFilters(exhaustHost);
        var hornHost = new GameObject("ChevroletCamaro1967_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var hornTemplate = otherSource ?? sourceTemplate;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, hornTemplate);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, hornTemplate);
        if (engineSound != null) engineSound.maxDistortion = 0f;
        configured = true;
        ChevroletCamaro1967Diagnostics.Info(
            context,
            $"ChevroletCamaro1967 audio configured vehicle={vehicle.GetInstanceID()}, " +
            $"engineLayers=7, engineGain={ChevroletCamaro1967AudioModel.EngineBaseVolume:0.00}.." +
            $"{ChevroletCamaro1967AudioModel.EngineBaseVolume + ChevroletCamaro1967AudioModel.EngineThrottleVolume:0.00}, " +
            $"hornVoices=low/high@{ChevroletCamaro1967AudioModel.HornLowVolume:0.00}/" +
            $"{ChevroletCamaro1967AudioModel.HornHighVolume:0.00}, " +
            $"source={(native != null ? "native" : sourceTemplate != null ? "auxiliary" : "direct-fallback")}, " +
            $"sourceDistance={idleSource.minDistance:0.0}..{idleSource.maxDistance:0.0}, " +
            $"directMixGain={(usesDirectFallbackMix ? DirectFallbackMixGain : 1f):0.00}, " +
            "idleBurble=accepted-82-20-mix+smooth-bank-pulses, driveFilter=isolated, " +
            "driveCrackle=false, nativeEngineExhaustSuppressed=true.");
        // One unconditional diagnostic per configured vehicle: previous logs
        // hid the actual idle clip/fallback choice behind optional debug flags.
        Debug.Log(
            $"[Mod:Chevrolet_Camaro_1967] Camaro idle revision=burble-emphasis-v2, " +
            $"vehicle={vehicle.GetInstanceID()}, main='{idleSource.clip?.name}', " +
            $"support='{idleRumbleSource?.clip?.name ?? "none"}', " +
            $"burble='{idleBurbleSource.clip?.name}', " +
            $"nativeIdle={idleRumbleSource != null}, supportGain=0.05, secondaryGain=0.31, " +
            "customLoopsSameClock=true, idleAssetsRequired=true.");
        return true;
    }

    private static void ConfigureDriveFilters(GameObject host)
    {
        var highPass = host.AddComponent<AudioHighPassFilter>();
        highPass.cutoffFrequency = 24f;
        highPass.highpassResonanceQ = 1f;
        var lowPass = host.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = 1800f;
        lowPass.lowpassResonanceQ = 1f;
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
        var path = Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav");
        if (name.StartsWith("EngineIdle", StringComparison.Ordinal) && !File.Exists(path))
        {
            // The generic Wave fallback is a tonal drive oscillator, not an
            // idle recording. Do not silently substitute it for either burble.
            throw new FileNotFoundException(
                "Required Camaro idle WAV is missing. Run the Camaro isolated build " +
                "to regenerate and install Config/Audio; generic tonal fallback is disabled for idle.",
                path);
        }
        var clip = ChevroletCamaro1967Wave.Load(path);
        ownedClips.Add(clip);
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
            : vehicle!.transform.TransformPoint(new Vector3(0f, .55f, 1.15f));
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
            SuppressNativeEngineAudio();
        else
            RestoreNativeEngineAudio();

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
            var normalized = ChevroletCamaro1967AudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            driveBlend = Mathf.MoveTowards(driveBlend,
                ChevroletCamaro1967AudioModel.DrivingBlend(rawRpm, engine.idleRPM, engine.revLimiterRPM), Time.deltaTime * 4f);
            var idleMixGain = mixGain *
                              (usesDirectFallbackMix ? DirectFallbackIdleBoost : 1f);
            var idleVolume = envelope * master * idleMixGain *
                             ChevroletCamaro1967AudioModel.IdleVolume(driveBlend);
            idleSource.pitch = ChevroletCamaro1967AudioModel.IdlePitch;
            // Keep the accepted overall idle level, but move the mix away from
            // the rapid 50-Hz support texture and toward the slower audible
            // exhaust-bank accents. The three weights still sum to 1.18, exactly
            // as before (0.82 + 0.20 + 0.16), so this is a character rebalance,
            // not another idle-volume increase.
            idleSource.volume = idleVolume * (idleRumbleSource != null ? 0.82f : 1f);
            idleSource.mute = false;
            if (idleRumbleSource != null)
            {
                idleRumbleSource.pitch = 0.96f;
                idleRumbleSource.volume = idleVolume * 0.05f;
                idleRumbleSource.mute = false;
            }
            if (idleBurbleSource != null)
            {
                idleBurbleSource.pitch = idleRumbleSource != null
                    ? idleRumbleSource.pitch
                    : idleSource.pitch;
                idleBurbleSource.volume = idleVolume * 0.31f;
                idleBurbleSource.mute = false;
            }
            var gain = envelope * master * mixGain * ChevroletCamaro1967AudioModel.EngineVolume(smoothThrottle) *
                       Mathf.Sqrt(driveBlend);
            loadBlend = ChevroletCamaro1967AudioModel.LoadBlend(smoothThrottle);
            for (var i = 0; i < EngineNames.Length; i++)
            {
                layers[i].pitch = ChevroletCamaro1967AudioModel.Pitch(normalized,i);
                layers[i + 3].pitch = layers[i].pitch;
                var bandGain = gain * ChevroletCamaro1967AudioModel.Weight(normalized, i);
                // Matched-RMS variants change tone with load; linear interpolation
                // avoids doubling the shared harmonic content at half throttle.
                layers[i].volume = bandGain * (1f - loadBlend);
                layers[i + 3].volume = bandGain * loadBlend;
                layers[i].mute = layers[i + 3].mute = false;
            }
            crackleSource.pitch = .82f;
            crackleSource.volume = envelope * master * mixGain *
                                   ChevroletCamaro1967AudioModel.CrackleIdleVolume *
                                   Mathf.Sqrt(1f - Mathf.Clamp01(driveBlend));
            crackleSource.mute = false;
            if (envelope <= 0f) StopLayers();
            else if (!voicesStarted)
            {
                // Start all layers on the same DSP boundary. An inaudible layer
                // keeps advancing so bringing it into the blend never restarts it.
                var start = AudioSettings.dspTime + .03d;
                idleSource.PlayScheduled(start);
                if (idleRumbleSource != null) idleRumbleSource.PlayScheduled(start);
                if (idleBurbleSource != null) idleBurbleSource.PlayScheduled(start);
                foreach (var source in layers) source.PlayScheduled(start);
                crackleSource.PlayScheduled(start);
                voicesStarted = true;
            }
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null) return;
        var lowTarget = pressed ? master * ChevroletCamaro1967AudioModel.HornLowVolume : 0f;
        var highTarget = pressed ? master * ChevroletCamaro1967AudioModel.HornHighVolume : 0f;
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

    private void Warn(string message) => context?.Logger.Warn($"ChevroletCamaro1967 audio vehicle={vehicle?.GetInstanceID()}: {message}");

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

        // Keep all donor engine/exhaust voices silent. The Camaro now owns both
        // its idle and drive tone, so no generic reference-car sound can bleed in.
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
        {
            return;
        }

        if (!suppressedNativeSources.ContainsKey(source))
            suppressedNativeSources[source] = new NativeSourceState(source);

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

    private void StopLayers()
    {
        if (idleSource != null) idleSource.Stop();
        if (idleRumbleSource != null) idleRumbleSource.Stop();
        if (idleBurbleSource != null) idleBurbleSource.Stop();
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
        RestoreNativeEngineAudio();
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
        idleRumbleSource = null;
        idleBurbleSource = null;
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
