#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class DodgeChallenger2018AudioController : MonoBehaviour
{
    // Body and bark are phase-synchronized but intentionally use separate final
    // masters: the smooth engine body stays controlled while the supercharged
    // combustion/exhaust bark is allowed to dominate under load.
    private const float BodyPlaybackMaster = .95f;
    private const float BarkPlaybackMaster = 1.30f;
    private const float IdlePlaybackMaster = 1.40f;
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
    private AudioSource? idleSource;
    private AudioSource? driveBodySource;
    private AudioSource? driveBarkSource;
    private AudioSource? crackleSource;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private AudioClip? crackleClip;
    private System.Reflection.FieldInfo? engineBaseVolumeField;
    private System.Reflection.PropertyInfo? engineBaseVolumeProperty;
    private float originalEngineBaseVolume;
    private bool hasOriginalEngineBaseVolume;
    private float originalDistortion;
    private bool configured, failed, paused, wasControlled, voicesStarted,
        mixerWaitLogged;
    private int attempts;
    private float nextAttempt, smoothRpm, smoothThrottle, envelope, driveBlend;
    private double scheduledStartDsp;

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
                if (attempts >= 80 || Time.unscaledTime < nextAttempt)
                    return;

                attempts++;
                nextAttempt = Time.unscaledTime + .35f;
                if (!TryConfigure())
                {
                    if (attempts == 80)
                        Warn("native NWH audio mixer never became ready; custom engine audio was not initialized.");
                    return;
                }
            }

            UpdatePlayback();
        }
        catch (Exception ex)
        {
            failed = true;
            Cleanup();
            Warn($"HEMI audio failed; native Car sound restored: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool TryConfigure()
    {
        physics = vehicle!.GetComponent<PhysicsVehicle>();
        engineSound = physics?.soundManager.engineRunningComponent;
        native = engineSound?.source;
        if (physics == null || context == null)
            return false;

        var otherSource = FindMixerRoutedSource(physics.soundManager.otherSourceGO);
        sourceTemplate =
            native != null && native.outputAudioMixerGroup != null
                ? native
                : otherSource ??
                  FindMixerRoutedSource(physics.soundManager.engineSourceGO) ??
                  FindMixerRoutedSource(physics.soundManager.exhaustSourceGO);

        if (sourceTemplate == null || sourceTemplate.outputAudioMixerGroup == null)
        {
            if (!mixerWaitLogged)
            {
                mixerWaitLogged = true;
                DodgeChallenger2018Diagnostics.Info(
                    context,
                    $"DodgeChallenger2018 audio waiting for native NWH mixer " +
                    $"vehicle={vehicle.GetInstanceID()} native={(native != null)}.");
            }
            return false;
        }

        mixerWaitLogged = false;
        originalDistortion = engineSound?.maxDistortion ?? 0f;

        audioHost = new GameObject("DodgeChallenger2018_EngineLayers");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = EnginePosition();

        idleSource = CreateSource(
            audioHost,
            LoadClip("EngineIdle"),
            true,
            sourceTemplate);
        driveBodySource = CreateSource(
            audioHost,
            LoadClip("EngineDriveBody"),
            true,
            sourceTemplate);
        driveBarkSource = CreateSource(
            audioHost,
            LoadClip("EngineDriveBark"),
            true,
            sourceTemplate);
        ValidateEngineClips(idleSource.clip, driveBodySource.clip, driveBarkSource.clip);

        crackleClip = DodgeChallenger2018CrackleWave.Create();
        var exhaustHost = new GameObject("DodgeChallenger2018_ExhaustCrackle");
        exhaustHost.transform.SetParent(audioHost.transform, false);
        crackleSource = CreateSource(exhaustHost, crackleClip, true, sourceTemplate);
        ConfigureCrackleFilters(exhaustHost);
        ConfigureEngineFilters(audioHost);

        var hornHost = new GameObject("DodgeChallenger2018_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var hornTemplate = otherSource ?? sourceTemplate;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, hornTemplate);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, hornTemplate);

        if (engineSound != null)
            engineSound.maxDistortion = 0f;

        configured = true;
        // Equal sample grids, identical pitch and one DSP boundary maintain the
        // same crank phase for idle/body/bark, even across inaudible passages.
        StartLayers();

        DodgeChallenger2018Diagnostics.Info(
            context,
            $"DodgeChallenger2018 audio configured vehicle={vehicle.GetInstanceID()}, " +
            $"engineLayers=3-hemi-common-clock, bodyGain={DodgeChallenger2018AudioModel.EngineBaseVolume:0.00}.." +
            $"{DodgeChallenger2018AudioModel.EngineBaseVolume + DodgeChallenger2018AudioModel.EngineThrottleVolume:0.00}, " +
            $"barkGain={DodgeChallenger2018AudioModel.BarkVolume(0f):0.00}.." +
            $"{DodgeChallenger2018AudioModel.BarkVolume(1f):0.00}, " +
            $"bodyMaster={BodyPlaybackMaster:0.00}, barkMaster={BarkPlaybackMaster:0.00}, " +
            $"idleMaster={IdlePlaybackMaster:0.00}, " +
            $"hornVoices=low/high@{DodgeChallenger2018AudioModel.HornLowVolume:0.00}/" +
            $"{DodgeChallenger2018AudioModel.HornHighVolume:0.00}, " +
            $"source={(ReferenceEquals(sourceTemplate, native) ? "native" : "auxiliary-mixer")}, " +
            $"sourceDistance={idleSource.minDistance:0.0}..{idleSource.maxDistance:0.0}, " +
            $"mixer='{sourceTemplate.outputAudioMixerGroup?.name ?? "<none>"}', " +
            $"wav={idleSource.clip.frequency}Hz/{idleSource.clip.samples}samples/" +
            $"{idleSource.clip.channels}ch, " +
            "audioRevision=hemi-clean-common-clock-v1, idle=soft-pulses, " +
            "drive=matchedBody+Bark, nativeEngineExhaustSuppressed=true.");
        return true;
    }

    private static void ValidateEngineClips(AudioClip idle, AudioClip body, AudioClip bark)
    {
        var expectedSamples = Mathf.RoundToInt(
            idle.frequency * 120f * DodgeChallenger2018AudioModel.EngineCycles /
            DodgeChallenger2018AudioModel.PlaybackReferenceRpm);
        if (idle.channels != 1 || body.channels != 1 || bark.channels != 1 ||
            idle.frequency != body.frequency || idle.frequency != bark.frequency ||
            idle.samples != expectedSamples || body.samples != expectedSamples ||
            bark.samples != expectedSamples)
        {
            throw new InvalidOperationException(
                "HEMI engine WAVs have different/old phase grids. " +
                "Regenerate Config/Audio with tools/generate_dodge_challenger_2018_audio.py " +
                "and reinstall the mod together with its DLL.");
        }
    }

    private static void ConfigureEngineFilters(GameObject host)
    {
        var highPass = host.AddComponent<AudioHighPassFilter>();
        highPass.cutoffFrequency = 22f;
        highPass.highpassResonanceQ = 1f;

        var lowPass = host.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = 900f;
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

    private static AudioSource? FindMixerRoutedSource(GameObject? host)
    {
        if (host == null)
            return null;

        foreach (var source in host.GetComponentsInChildren<AudioSource>(true))
        {
            if (source != null && source.outputAudioMixerGroup != null)
                return source;
        }
        return null;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = DodgeChallenger2018Wave.Load(
            Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
        ownedClips.Add(clip);
        return clip;
    }

    private AudioSource CreateSource(
        GameObject host,
        AudioClip clip,
        bool loop,
        AudioSource? template)
    {
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.clip = clip;
        source.volume = 0f;

        if (template == null || template.outputAudioMixerGroup == null)
            throw new InvalidOperationException(
                "A mixer-routed NWH AudioSource is required for Challenger audio.");

        source.outputAudioMixerGroup = template.outputAudioMixerGroup;
        source.spatialBlend = template.spatialBlend;
        source.minDistance = template.minDistance;
        source.maxDistance = template.maxDistance;
        source.SetCustomCurve(
            AudioSourceCurveType.CustomRolloff,
            template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        source.rolloffMode = template.rolloffMode;
        source.priority = template.priority;
        source.dopplerLevel = 0f;
        return source;
    }

    private Vector3 EnginePosition()
    {
        return native != null
            ? native.transform.position
            : vehicle!.transform.TransformPoint(new Vector3(0f, .62f, 1.25f));
    }

    private void UpdatePlayback()
    {
        if (physics == null ||
            driveBodySource == null ||
            driveBarkSource == null ||
            audioHost == null ||
            crackleSource == null ||
            hornSource == null ||
            hornSupportSource == null ||
            idleSource == null)
        {
            throw new InvalidOperationException(
                "Configured audio source or vehicle was removed.");
        }

        audioHost.transform.position = EnginePosition();
        var exhaust = physics.soundManager.exhaustSourceGO;
        crackleSource.transform.position = exhaust != null
            ? exhaust.transform.position
            : vehicle!.transform.TransformPoint(new Vector3(0f, .4f, -2f));

        var controlled = vehicle!.controlledByPlayer;
        var engine = physics.powertrain.engine;
        var running =
            controlled && engine.ignition && engine.IsRunning && engine.canRun;

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
            SuppressNativeEngineAudio();
        else
            RestoreNativeEngineAudio();

        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        const float mixGain = 1f;

        UpdateHorn(
            controlled && !paused && physics.input.Horn,
            Mathf.Clamp01(physics.soundManager.masterVolume) * mixGain);

        if (paused)
            return;

        if (!voicesStarted)
            StartLayers();

        var follow = 1f - Mathf.Exp(-Time.deltaTime / .085f);
        smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
        smoothThrottle = Mathf.Lerp(
            smoothThrottle,
            Mathf.Clamp01(engine.ThrottlePosition),
            follow);
        ApplyEnginePitch();

        // A scheduled source is not audible yet. In particular on pause/resume,
        // do not advance its attack before the actual DSP start has arrived.
        if (AudioSettings.dspTime < scheduledStartDsp)
        {
            envelope = 0f;
            idleSource.volume = driveBodySource.volume = driveBarkSource.volume = 0f;
            return;
        }

        var envelopeSpeed = running ? 2.4f : 7.0f;
        envelope = Mathf.MoveTowards(
            envelope,
            running ? 1f : 0f,
            Time.deltaTime * envelopeSpeed);

        var master = Mathf.Clamp01(physics.soundManager.masterVolume);
        driveBlend = Mathf.MoveTowards(
            driveBlend,
            DodgeChallenger2018AudioModel.DrivingBlend(
                smoothRpm,
                engine.idleRPM,
                engine.revLimiterRPM),
            Time.deltaTime * 3.5f);

        const float idleMixGain = 1f;
        idleSource.volume =
            envelope *
            master *
            idleMixGain *
            IdlePlaybackMaster *
            DodgeChallenger2018AudioModel.IdleVolume(driveBlend);
        idleSource.mute = false;

        var bodyGain =
            envelope *
            master *
            mixGain *
            BodyPlaybackMaster *
            DodgeChallenger2018AudioModel.EngineVolume(smoothThrottle) *
            driveBlend;
        var barkGain =
            envelope *
            master *
            mixGain *
            BarkPlaybackMaster *
            DodgeChallenger2018AudioModel.BarkVolume(smoothThrottle) *
            driveBlend;

        driveBodySource.volume = bodyGain;
        driveBarkSource.volume = barkGain;
        driveBodySource.mute = false;
        driveBarkSource.mute = false;

        // No independent continuous crackle/noise bed.
        crackleSource.pitch = 1f;
        crackleSource.volume = 0f;
        crackleSource.mute = true;
    }

    private void ApplyEnginePitch()
    {
        if (physics == null || idleSource == null ||
            driveBodySource == null || driveBarkSource == null)
            return;
        var engine = physics.powertrain.engine;
        var commonPitch = DodgeChallenger2018AudioModel.EnginePitch(
            smoothRpm, engine.idleRPM, engine.revLimiterRPM);
        idleSource.pitch = commonPitch;
        driveBodySource.pitch = commonPitch;
        driveBarkSource.pitch = commonPitch;
    }

    private void StartLayers()
    {
        if (voicesStarted ||
            idleSource == null ||
            driveBodySource == null ||
            driveBarkSource == null)
        {
            return;
        }

        scheduledStartDsp = AudioSettings.dspTime + .04d;
        envelope = 0f;
        idleSource.volume = driveBodySource.volume = driveBarkSource.volume = 0f;
        ApplyEnginePitch();
        idleSource.timeSamples = driveBodySource.timeSamples = driveBarkSource.timeSamples = 0;
        idleSource.PlayScheduled(scheduledStartDsp);
        driveBodySource.PlayScheduled(scheduledStartDsp);
        driveBarkSource.PlayScheduled(scheduledStartDsp);
        voicesStarted = true;
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null)
            return;

        var lowTarget = pressed
            ? master * DodgeChallenger2018AudioModel.HornLowVolume
            : 0f;
        var highTarget = pressed
            ? master * DodgeChallenger2018AudioModel.HornHighVolume
            : 0f;

        UpdateHornVoice(hornSource, pressed, lowTarget);
        UpdateHornVoice(hornSupportSource, pressed, highTarget);
    }

    private static void UpdateHornVoice(
        AudioSource source,
        bool pressed,
        float target)
    {
        source.volume = Mathf.MoveTowards(
            source.volume,
            target,
            Time.unscaledDeltaTime * 5f);

        if (pressed && !source.isPlaying)
            source.Play();
        else if (!pressed && source.volume <= 0f && source.isPlaying)
            source.Stop();
    }

    private void Warn(string message) =>
        context?.Logger.Warn(
            $"DodgeChallenger2018 audio vehicle={vehicle?.GetInstanceID()}: {message}");

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
        if (liveEngine != null)
        {
            engineSound = liveEngine;
            ZeroNativeEngineBaseVolume(liveEngine);
            if (liveEngine.source != null)
            {
                native = liveEngine.source;
                SuppressNativeSource(liveEngine.source);
            }
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
            (audioHost != null &&
             source.transform.IsChildOf(audioHost.transform)))
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

        if (hasOriginalEngineBaseVolume && engineSound != null)
        {
            if (engineBaseVolumeField != null)
                engineBaseVolumeField.SetValue(engineSound, originalEngineBaseVolume);
            else if (engineBaseVolumeProperty?.CanWrite == true)
                engineBaseVolumeProperty.SetValue(engineSound, originalEngineBaseVolume);
        }
        hasOriginalEngineBaseVolume = false;
    }

    private void ZeroNativeEngineBaseVolume(object component)
    {
        var type = component.GetType();
        engineBaseVolumeField ??= FindField(type, "baseVolume");
        if (engineBaseVolumeField != null &&
            engineBaseVolumeField.FieldType == typeof(float))
        {
            if (!hasOriginalEngineBaseVolume)
            {
                originalEngineBaseVolume =
                    (float)(engineBaseVolumeField.GetValue(component) ?? 0f);
                hasOriginalEngineBaseVolume = true;
            }
            engineBaseVolumeField.SetValue(component, 0f);
            return;
        }

        engineBaseVolumeProperty ??= type.GetProperty(
            "baseVolume",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        if (engineBaseVolumeProperty?.CanRead == true &&
            engineBaseVolumeProperty.CanWrite &&
            engineBaseVolumeProperty.PropertyType == typeof(float))
        {
            if (!hasOriginalEngineBaseVolume)
            {
                originalEngineBaseVolume =
                    (float)(engineBaseVolumeProperty.GetValue(component) ?? 0f);
                hasOriginalEngineBaseVolume = true;
            }
            engineBaseVolumeProperty.SetValue(component, 0f);
        }
    }

    private static System.Reflection.FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
    }

    private void StopLayers()
    {
        if (idleSource != null)
            idleSource.Stop();
        if (driveBodySource != null)
            driveBodySource.Stop();
        if (driveBarkSource != null)
            driveBarkSource.Stop();
        if (crackleSource != null)
            crackleSource.Stop();
        voicesStarted = false;
        scheduledStartDsp = 0d;
        envelope = 0f;
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
        if (configured && engineSound != null)
            engineSound.maxDistortion = originalDistortion;

        envelope = smoothRpm = smoothThrottle = driveBlend = 0f;
        paused = wasControlled = false;
    }

    private void OnEnable()
    {
        if (configured && engineSound != null)
        {
            engineSound.maxDistortion = 0f;
            StartLayers();
        }
    }

    private void Cleanup()
    {
        OnDisable();
        configured = false;

        if (audioHost != null)
            Destroy(audioHost);

        audioHost = null;
        idleSource = null;
        driveBodySource = null;
        driveBarkSource = null;
        crackleSource = null;
        hornSource = null;
        hornSupportSource = null;

        if (crackleClip != null)
            Destroy(crackleClip);
        crackleClip = null;

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
