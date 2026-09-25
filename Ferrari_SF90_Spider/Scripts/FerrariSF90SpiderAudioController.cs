#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class FerrariSF90SpiderAudioController : MonoBehaviour
{
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private readonly Dictionary<AudioSource, NativeSourceState> suppressedNativeSources =
        new Dictionary<AudioSource, NativeSourceState>();

    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private GameObject? audioHost;
    private AudioSource? engineSource;
    private AudioLowPassFilter? engineLowPass;
    private AudioHighPassFilter? engineHighPass;
    private AudioDistortionFilter? engineDistortion;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private float originalDistortion;
    private bool configured, failed, paused, wasControlled, voiceStarted;
    private int attempts;
    private float nextAttempt, smoothRpm, smoothThrottle, envelope;

    private System.Reflection.FieldInfo? engineBaseVolumeField;
    private System.Reflection.PropertyInfo? engineBaseVolumeProperty;
    private bool hasOriginalEngineBaseVolume;
    private float originalEngineBaseVolume;

    private sealed class NativeSourceState
    {
        internal readonly bool Mute;
        internal readonly float Volume;
        internal NativeSourceState(AudioSource source)
        {
            Mute = source.mute;
            Volume = source.volume;
        }
    }

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
                        Warn("native NWH audio routing never became ready; custom engine audio was not initialized.");
                    return;
                }
            }
            UpdatePlayback();
        }
        catch (Exception ex)
        {
            failed = true;
            Cleanup();
            Warn($"audio failed; native sources restored: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool TryConfigure()
    {
        physics = vehicle!.GetComponent<PhysicsVehicle>();
        engineSound = physics?.soundManager.engineRunningComponent;
        native = engineSound?.source;
        if (physics == null || context == null || engineSound == null || native == null ||
            native.outputAudioMixerGroup == null)
            return false;

        audioHost = new GameObject("FerrariSF90Spider_EngineV24");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = native.transform.position;

        // One tonal engine voice only. No low/mid/high bands, no turbo loop and
        // no procedural crackle source are mixed underneath it in V24.
        engineSource = CreateSource(audioHost, LoadClip("EngineCore"), true, native);
        engineLowPass = audioHost.AddComponent<AudioLowPassFilter>();
        engineLowPass.cutoffFrequency = 3000f;
        engineLowPass.lowpassResonanceQ = 1f;
        engineHighPass = audioHost.AddComponent<AudioHighPassFilter>();
        engineHighPass.cutoffFrequency = 28f;
        engineHighPass.highpassResonanceQ = 1f;
        engineDistortion = audioHost.AddComponent<AudioDistortionFilter>();
        engineDistortion.distortionLevel = .004f;

        var hornTemplate = physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        if (hornTemplate == null || hornTemplate.outputAudioMixerGroup == null)
            hornTemplate = native;
        var hornHost = new GameObject("FerrariSF90Spider_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, hornTemplate);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, hornTemplate);

        originalDistortion = engineSound.maxDistortion;
        engineSound.maxDistortion = 0f;
        UpdateNativeEngineSuppression(true);
        configured = true;
        FerrariSF90SpiderDiagnostics.Info(
            context,
            $"FerrariSF90Spider audio V24 configured vehicle={vehicle.GetInstanceID()}, " +
            "engine=single-additive-flat-plane-v8, audibleEngineSources=1, " +
            "nativeEngineExhaustSuppressed=true, turboLoop=false, crackleLoop=false, " +
            $"engineGain={FerrariSF90SpiderAudioModel.EngineBaseVolume:0.00}.." +
            $"{FerrariSF90SpiderAudioModel.EngineBaseVolume + FerrariSF90SpiderAudioModel.EngineThrottleVolume:0.00}, " +
            $"hornVoices=low/high@{FerrariSF90SpiderAudioModel.HornLowVolume:0.00}/" +
            $"{FerrariSF90SpiderAudioModel.HornHighVolume:0.00}, routing=native-NWH-mixer.");
        return true;
    }

    private AudioClip LoadClip(string name)
    {
        var clip = FerrariSF90SpiderWave.Load(
            Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav"));
        ownedClips.Add(clip);
        return clip;
    }

    private static AudioSource CreateSource(GameObject host, AudioClip clip, bool loop, AudioSource template)
    {
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.clip = clip;
        source.volume = 0f;
        source.outputAudioMixerGroup = template.outputAudioMixerGroup;
        source.spatialBlend = template.spatialBlend;
        source.minDistance = template.minDistance;
        source.maxDistance = template.maxDistance;
        var curve = template.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
        if (curve != null && curve.length > 0)
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
        source.rolloffMode = template.rolloffMode;
        source.priority = template.priority;
        source.dopplerLevel = 0f;
        return source;
    }

    private void UpdatePlayback()
    {
        if (physics == null || native == null || audioHost == null || engineSource == null ||
            engineLowPass == null || engineDistortion == null || hornSource == null ||
            hornSupportSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");

        audioHost.transform.position = native.transform.position;

        var controlled = vehicle!.controlledByPlayer;
        var engine = physics.powertrain.engine;
        var running = controlled && engine.ignition && engine.IsRunning && engine.canRun;
        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        var rawThrottle = Mathf.Clamp01(engine.ThrottlePosition);

        if (controlled && !wasControlled)
        {
            smoothRpm = rawRpm;
            smoothThrottle = rawThrottle;
        }
        wasControlled = controlled;

        var shouldPause = Time.timeScale <= 0f || AudioListener.pause;
        if (shouldPause != paused)
        {
            if (shouldPause)
                StopEngineVoice();
            paused = shouldPause;
        }

        UpdateNativeEngineSuppression(controlled);
        var master = Mathf.Clamp01(physics.soundManager.masterVolume);
        UpdateHorn(controlled && !paused && physics.input.Horn, master);
        if (paused)
            return;

        // Keep the already-validated shift response: falling RPM follows very
        // quickly so an upshift produces an immediate audible pitch drop.
        var rpmTau = rawRpm < smoothRpm ? .020f : .055f;
        var rpmFollow = 1f - Mathf.Exp(-Time.deltaTime / rpmTau);
        var throttleFollow = 1f - Mathf.Exp(-Time.deltaTime / .080f);
        smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, rpmFollow);
        smoothThrottle = Mathf.Lerp(smoothThrottle, rawThrottle, throttleFollow);

        var normalized = FerrariSF90SpiderAudioModel.Normalize(
            smoothRpm, engine.idleRPM, engine.revLimiterRPM);
        envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 7f);

        engineSource.pitch = FerrariSF90SpiderAudioModel.EnginePitch(normalized);
        engineSource.volume = envelope * master *
                              FerrariSF90SpiderAudioModel.EngineVolume(smoothThrottle, normalized);
        engineLowPass.cutoffFrequency = FerrariSF90SpiderAudioModel.EngineLowPass(
            normalized, smoothThrottle);
        engineDistortion.distortionLevel = FerrariSF90SpiderAudioModel.EngineDistortion(smoothThrottle);
        engineSource.mute = false;

        if (envelope <= 0f)
        {
            StopEngineVoice();
        }
        else if (!voiceStarted)
        {
            engineSource.PlayScheduled(AudioSettings.dspTime + .03d);
            voiceStarted = true;
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null)
            return;
        UpdateHornVoice(hornSource, pressed,
            pressed ? master * FerrariSF90SpiderAudioModel.HornLowVolume : 0f);
        UpdateHornVoice(hornSupportSource, pressed,
            pressed ? master * FerrariSF90SpiderAudioModel.HornHighVolume : 0f);
    }

    private static void UpdateHornVoice(AudioSource source, bool pressed, float target)
    {
        source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * 5f);
        if (pressed && !source.isPlaying)
            source.Play();
        else if (!pressed && source.volume <= 0f && source.isPlaying)
            source.Stop();
    }

    private void UpdateNativeEngineSuppression(bool suppress)
    {
        if (!suppress)
        {
            RestoreNativeEngineAudio();
            return;
        }
        if (physics == null)
            return;

        var liveComponent = physics.soundManager.engineRunningComponent;
        if (liveComponent != null)
        {
            engineSound = liveComponent;
            ZeroNativeEngineBaseVolume(liveComponent);
            if (liveComponent.source != null)
            {
                native = liveComponent.source;
                SuppressNativeSource(liveComponent.source);
            }
        }

        // Suppress every native continuous engine/exhaust bed. Our horn lives
        // below audioHost and is explicitly excluded by SuppressNativeSource().
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
        if (source == null || (audioHost != null && source.transform.IsChildOf(audioHost.transform)))
            return;
        if (!suppressedNativeSources.ContainsKey(source))
            suppressedNativeSources[source] = new NativeSourceState(source);
        source.volume = 0f;
        source.mute = true;
    }

    private void ZeroNativeEngineBaseVolume(object component)
    {
        var type = component.GetType();
        engineBaseVolumeField ??= FindField(type, "baseVolume");
        if (engineBaseVolumeField != null && engineBaseVolumeField.FieldType == typeof(float))
        {
            if (!hasOriginalEngineBaseVolume)
            {
                originalEngineBaseVolume = (float)(engineBaseVolumeField.GetValue(component) ?? 0f);
                hasOriginalEngineBaseVolume = true;
            }
            engineBaseVolumeField.SetValue(component, 0f);
            return;
        }
        engineBaseVolumeProperty ??= type.GetProperty(
            "baseVolume",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic);
        if (engineBaseVolumeProperty?.CanRead == true && engineBaseVolumeProperty.CanWrite &&
            engineBaseVolumeProperty.PropertyType == typeof(float))
        {
            if (!hasOriginalEngineBaseVolume)
            {
                originalEngineBaseVolume = (float)(engineBaseVolumeProperty.GetValue(component) ?? 0f);
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
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
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

    private void StopEngineVoice()
    {
        if (engineSource != null)
            engineSource.Stop();
        voiceStarted = false;
    }

    private void Warn(string message) =>
        context?.Logger.Warn($"FerrariSF90Spider audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void OnDisable()
    {
        StopEngineVoice();
        if (hornSource != null) { hornSource.Stop(); hornSource.volume = 0f; }
        if (hornSupportSource != null) { hornSupportSource.Stop(); hornSupportSource.volume = 0f; }
        RestoreNativeEngineAudio();
        if (configured && engineSound != null)
            engineSound.maxDistortion = originalDistortion;
        envelope = smoothRpm = smoothThrottle = 0f;
        paused = wasControlled = false;
    }

    private void OnEnable()
    {
        if (configured && engineSound != null)
            engineSound.maxDistortion = 0f;
    }

    private void Cleanup()
    {
        OnDisable();
        configured = false;
        if (audioHost != null)
            Destroy(audioHost);
        audioHost = null;
        engineSource = null;
        hornSource = null;
        hornSupportSource = null;
        foreach (var clip in ownedClips)
            if (clip != null)
                Destroy(clip);
        ownedClips.Clear();
    }

    private void OnDestroy() => Cleanup();
}
