#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using UnityEngine.Audio;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

/// <summary>
/// Plays the existing McLaren Senna V8 loop assets already reused by Audi R8.
/// This controller does not synthesize or modify engine audio.
/// </summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
internal sealed class BentleyContinentalGTSpeedAudioController : MonoBehaviour
{
    private const float DirectFallbackMixGain = 0.16f;
    private const int EngineLayerCount = 3;
    private static readonly string[] CoastClipNames = { "EngineLow", "EngineMid", "EngineHigh" };
    private static readonly string[] LoadClipNames = { "EngineLowLoad", "EngineMidLoad", "EngineHighLoad" };

    private readonly List<AudioClip> ownedClips = new();
    private readonly List<(SoundComponent Component, float BaseVolume)> suppressedSounds = new();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private AudioSource? sourceTemplate;
    private GameObject? audioHost;
    private AudioSource? idleSource;
    private AudioSource[] engineBands = Array.Empty<AudioSource>();
    private bool configured;
    private bool failed;
    private bool controlled;
    private bool engineRunning;
    private bool ownsNativeMute;
    private bool savedNativeMute;
    private bool voicesStarted;
    private bool usesDirectFallbackMix;
    private bool paused;
    private bool wasControlled;
    private bool? lastEngineRunning;
    private float smoothRpm;
    private float smoothThrottle;
    private float envelope;
    private float driveBlend;
    private float loadBlend;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (controller == null)
            throw new ArgumentNullException(nameof(controller));
        if (vehicle != null && vehicle != controller)
            Cleanup();

        vehicle = controller;
        context = modContext;
        physics = vehicle.GetComponent<PhysicsVehicle>();
        GlobalEvents.onEnterVehicle -= OnEntered;
        GlobalEvents.onEnterVehicle += OnEntered;
        GlobalEvents.onExitVehicle -= OnExited;
        GlobalEvents.onExitVehicle += OnExited;
        GlobalEvents.onVehicleVariablesChanged -= OnVariablesChanged;
        GlobalEvents.onVehicleVariablesChanged += OnVariablesChanged;
        if (physics != null)
        {
            physics.onVehicleInitialized.RemoveListener(OnPhysicsInitialized);
            physics.onVehicleInitialized.AddListener(OnPhysicsInitialized);
        }

        OnPhysicsInitialized();
        OnVariablesChanged();
    }

    internal void Shutdown()
    {
        failed = true;
        Cleanup();
    }

    private void LateUpdate()
    {
        if (vehicle == null || !configured || failed)
            return;
        try
        {
            UpdatePlayback();
        }
        catch (Exception exception)
        {
            failed = true;
            Cleanup();
            Warn("existing engine audio playback failed; native sound state restored: " +
                 $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private void OnPhysicsInitialized()
    {
        if (configured || failed || physics == null)
            return;
        try
        {
            if (!TryConfigure())
                return;
            physics.powertrain.engine.onStart.AddListener(OnEngineStarted);
            physics.powertrain.engine.onStop.AddListener(OnEngineStopped);
            OnVariablesChanged();
        }
        catch (Exception exception)
        {
            failed = true;
            Cleanup();
            Warn("existing engine audio setup failed: " +
                 $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private bool TryConfigure()
    {
        if (vehicle == null || physics == null || context == null)
            return false;

        engineSound = physics.soundManager.engineRunningComponent;
        native = engineSound?.source;
        var otherSource = physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        sourceTemplate = native ?? otherSource;
        var mixer = native?.outputAudioMixerGroup ?? physics.soundManager.engineMixerGroup ??
                    GlobalReferences.Instance?.playerVehicleMixerGroup;
        usesDirectFallbackMix = mixer == null;
        if (mixer != null)
        {
            if (physics.soundManager.engineMixerGroup == null)
                physics.soundManager.engineMixerGroup = mixer;
            if (native != null && native.outputAudioMixerGroup == null)
                native.outputAudioMixerGroup = mixer;
        }

        audioHost = new GameObject("BentleyContinentalGTSpeed_ExistingV8Audio");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.localPosition = BentleyAudioModel.EngineLocalPosition;
        idleSource = CreateSource(audioHost, LoadClip("EngineIdle"), loop: true, mixer, sourceTemplate);
        idleSource.pitch = BentleyAudioModel.IdlePitch;

        engineBands = new AudioSource[EngineLayerCount * 2];
        for (var layer = 0; layer < EngineLayerCount; layer++)
        {
            engineBands[layer] = CreateSource(
                audioHost, LoadClip(CoastClipNames[layer]), loop: true, mixer, sourceTemplate);
            engineBands[layer + EngineLayerCount] = CreateSource(
                audioHost, LoadClip(LoadClipNames[layer]), loop: true, mixer, sourceTemplate);
        }

        Suppress(physics.soundManager.engineFanComponent);
        Suppress(physics.soundManager.transmissionWhineComponent);
        Suppress(physics.soundManager.turboWhistleComponent);
        Suppress(physics.soundManager.turboFlutterComponent);
        Suppress(physics.soundManager.exhaustPopComponent);
        configured = true;
        BentleyContinentalGTSpeedAudioDiagnostics.Info(
            context,
            $"vehicle={vehicle.GetInstanceID()} reused seven existing McLaren Senna V8 loops " +
            $"through {(usesDirectFallbackMix ? "direct spatial output" : "the game engine mixer")}; " +
            $"source={(native != null ? "native" : sourceTemplate != null ? "auxiliary" : "spatial fallback")}, " +
            $"clipCount={ownedClips.Count}.");
        return true;
    }

    private AudioClip LoadClip(string name)
    {
        var path = Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav");
        var clip = BentleyPcmWave.Load(path);
        ownedClips.Add(clip);
        return clip;
    }

    private AudioSource CreateSource(
        GameObject host, AudioClip clip, bool loop, AudioMixerGroup? mixer, AudioSource? template)
    {
        var source = host.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.clip = clip;
        source.volume = 0f;
        source.outputAudioMixerGroup = mixer;
        if (template != null)
        {
            source.spatialBlend = template.spatialBlend;
            source.minDistance = template.minDistance;
            source.maxDistance = template.maxDistance;
            source.rolloffMode = template.rolloffMode;
            source.priority = template.priority;
            source.SetCustomCurve(
                AudioSourceCurveType.CustomRolloff,
                template.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        }
        else
        {
            source.spatialBlend = 1f;
            source.minDistance = 4f;
            source.maxDistance = 55f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.priority = 100;
        }
        source.dopplerLevel = 0f;
        source.ignoreListenerPause = false;
        source.ignoreListenerVolume = false;
        return source;
    }

    private void OnEntered(VehicleController target)
    {
        if (target == vehicle)
            OnVariablesChanged();
    }

    private void OnExited(VehicleController target)
    {
        if (target != vehicle)
            return;
        controlled = false;
        RestoreNativeMute();
    }

    private void OnVariablesChanged()
    {
        controlled = vehicle != null && vehicle.controlledByPlayer;
        var engine = physics?.powertrain?.engine;
        engineRunning = engine != null && (engine.IsRunning || engine.ignition) && engine.canRun;
        if (!configured && !failed)
            OnPhysicsInitialized();
    }

    private void OnEngineStarted() => engineRunning = true;

    private void OnEngineStopped() => engineRunning = false;

    private void Suppress(SoundComponent component)
    {
        suppressedSounds.Add((component, component.baseVolume));
        component.baseVolume = 0f;
        if (component.source != null)
            component.source.volume = 0f;
    }

    private void UpdatePlayback()
    {
        if (physics == null || vehicle == null || audioHost == null ||
            idleSource == null || engineBands.Length != EngineLayerCount * 2)
            throw new InvalidOperationException("A configured vehicle audio object was removed.");

        audioHost.transform.position = native != null
            ? native.transform.position
            : vehicle.transform.TransformPoint(BentleyAudioModel.EngineLocalPosition);
        var engine = physics.powertrain.engine;
        var shouldRun = controlled && engineRunning && engine.canRun;
        if (controlled && !wasControlled)
        {
            smoothRpm = engine.RPMPercent * engine.revLimiterRPM;
            smoothThrottle = Mathf.Clamp01(physics.input.Throttle);
        }
        wasControlled = controlled;

        var shouldPause = Time.timeScale <= 0f || AudioListener.pause;
        if (shouldPause != paused)
        {
            if (shouldPause)
                StopEngineVoices();
            paused = shouldPause;
        }

        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        var throttle = Mathf.Clamp01(physics.input.Throttle);
        var master = Mathf.Clamp01(physics.soundManager.masterVolume);
        var mixGain = usesDirectFallbackMix ? DirectFallbackMixGain : 1f;
        if (!paused)
        {
            var follow = 1f - Mathf.Exp(-Time.deltaTime / 0.1f);
            smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
            smoothThrottle = Mathf.Lerp(smoothThrottle, throttle, follow);
            envelope = Mathf.MoveTowards(envelope, shouldRun ? 1f : 0f, Time.deltaTime * 5.5f);
            driveBlend = Mathf.MoveTowards(
                driveBlend,
                BentleyAudioModel.DrivingBlend(smoothRpm, engine.idleRPM, engine.revLimiterRPM),
                Time.deltaTime * 4f);
            loadBlend = Mathf.MoveTowards(
                loadBlend, BentleyAudioModel.LoadBlend(smoothThrottle), Time.deltaTime * 4f);

            var normalized = BentleyAudioModel.Normalize(smoothRpm, engine.idleRPM, engine.revLimiterRPM);
            var targetHz = BentleyAudioModel.TargetHz(normalized);
            var gain = envelope * master * mixGain * BentleyAudioModel.EngineVolume(smoothThrottle) *
                       Mathf.Sqrt(driveBlend);
            idleSource.pitch = BentleyAudioModel.IdlePitch;
            idleSource.volume = envelope * master * mixGain * BentleyAudioModel.IdleVolume(driveBlend);
            idleSource.mute = false;

            for (var layer = 0; layer < EngineLayerCount; layer++)
            {
                var pitch = targetHz / BentleyAudioModel.ReferenceHz(layer);
                var bandGain = gain * BentleyAudioModel.Weight(normalized, layer);
                var coast = engineBands[layer];
                var loaded = engineBands[layer + EngineLayerCount];
                coast.pitch = loaded.pitch = pitch;
                coast.volume = bandGain * (1f - loadBlend);
                loaded.volume = bandGain * loadBlend;
                coast.mute = loaded.mute = false;
            }

            if (envelope <= 0.001f)
                StopEngineVoices();
            else if (!voicesStarted)
                StartEngineVoices();
        }

        if (controlled && shouldRun && voicesStarted && native != null)
        {
            if (!ownsNativeMute)
            {
                savedNativeMute = native.mute;
                ownsNativeMute = true;
            }
            native.mute = true;
        }
        else
        {
            RestoreNativeMute();
        }

        if (lastEngineRunning != shouldRun)
        {
            BentleyContinentalGTSpeedAudioDiagnostics.Info(
                context,
                $"vehicle={vehicle.GetInstanceID()} engineRunning={shouldRun}, controlled={controlled}, " +
                $"ignition={engine.ignition}, rpm={rawRpm:0}, voicesStarted={voicesStarted}.");
            lastEngineRunning = shouldRun;
        }
    }

    private void StartEngineVoices()
    {
        if (idleSource == null)
            return;
        var startAt = AudioSettings.dspTime + 0.03d;
        idleSource.PlayScheduled(startAt);
        foreach (var source in engineBands)
            source.PlayScheduled(startAt);
        voicesStarted = true;
    }

    private void StopEngineVoices()
    {
        if (idleSource != null)
            idleSource.Stop();
        foreach (var source in engineBands)
            if (source != null)
                source.Stop();
        voicesStarted = false;
    }

    private void RestoreNativeMute()
    {
        if (ownsNativeMute && native != null)
            native.mute = savedNativeMute;
        ownsNativeMute = false;
    }

    private void Warn(string message) =>
        context?.Logger.Warn($"BentleyContinentalGTSpeed audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void OnDisable()
    {
        StopEngineVoices();
        RestoreNativeMute();
        foreach (var sound in suppressedSounds)
            sound.Component.baseVolume = sound.BaseVolume;
        envelope = smoothRpm = smoothThrottle = driveBlend = loadBlend = 0f;
        lastEngineRunning = null;
        paused = wasControlled = false;
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
            physics.powertrain.engine.onStart.RemoveListener(OnEngineStarted);
            physics.powertrain.engine.onStop.RemoveListener(OnEngineStopped);
        }
        foreach (var sound in suppressedSounds)
            sound.Component.baseVolume = sound.BaseVolume;
        suppressedSounds.Clear();
        if (audioHost != null)
            Destroy(audioHost);
        audioHost = null;
        idleSource = null;
        engineBands = Array.Empty<AudioSource>();
        foreach (var clip in ownedClips)
            if (clip != null)
                Destroy(clip);
        ownedClips.Clear();
        configured = false;
    }

    private void OnDestroy() => Cleanup();
}

/// <summary>RPM and load mapping for the existing V8 recordings.</summary>
internal static class BentleyAudioModel
{
    internal const float IdlePitch = 0.68f;
    internal static readonly Vector3 EngineLocalPosition = new(0f, 0.48f, 1.15f);

    internal static float LoadBlend(float throttle) => Clamp01((throttle - 0.12f) / 0.72f);
    internal static float IdleVolume(float drivingBlend) => 0.50f * Mathf.Sqrt(1f - Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) => 0.3872f + 0.4048f * Clamp01(throttle);
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - 0.015f * limiter) / Math.Max(1f, 0.09f * limiter));
    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));
    internal static float ReferenceHz(int layer) => layer == 0 ? 73f : layer == 1 ? 220f : 440f;

    internal static float TargetHz(float normalized)
    {
        var position = Clamp01(normalized);
        const double knee = (6000d - 1100d) / (8500d - 1100d);
        if (position <= knee)
            return (float)(50d * Math.Pow(320d / 50d, position));
        var kneeHz = 50d * Math.Pow(320d / 50d, knee);
        var progress = (position - knee) / (1d - knee);
        return (float)(kneeHz + (220d - kneeHz) *
            (1d - Math.Exp(-2d * progress)) / (1d - Math.Exp(-2d)));
    }

    internal static float Weight(float normalized, int layer)
    {
        var position = Clamp01(normalized) * 2f;
        var lower = position < 1f ? 0 : 1;
        var blend = position - lower;
        if (layer == lower)
            return (float)Math.Cos(blend * Math.PI * 0.5d);
        if (layer == lower + 1)
            return (float)Math.Sin(blend * Math.PI * 0.5d);
        return 0f;
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}

internal static class BentleyPcmWave
{
    internal static AudioClip Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        if (stream.Length < 44 || stream.Length > 4 * 1024 * 1024 ||
            new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Invalid existing engine WAV size or RIFF header.");
        var end = 8L + reader.ReadUInt32();
        if (end > stream.Length || new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Invalid existing engine WAV container.");

        var format = 0;
        var channels = 0;
        var rate = 0;
        var bits = 0;
        var alignment = 0;
        byte[]? bytes = null;
        while (stream.Position + 8 <= end)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadUInt32();
            var next = stream.Position + size;
            if (next > end)
                throw new InvalidDataException("Truncated existing engine WAV chunk.");
            if (id == "fmt ")
            {
                if (size < 16)
                    throw new InvalidDataException("Truncated existing engine WAV format.");
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                rate = reader.ReadInt32();
                reader.ReadUInt32();
                alignment = reader.ReadUInt16();
                bits = reader.ReadUInt16();
            }
            else if (id == "data")
            {
                bytes = reader.ReadBytes(checked((int)size));
            }
            stream.Position = next + (size & 1);
        }

        if (format != 1 || channels != 1 || bits != 16 || alignment != 2 || rate != 44100 ||
            bytes == null || bytes.Length == 0 || bytes.Length % 2 != 0 || bytes.Length / 2 > rate * 5)
            throw new InvalidDataException("Expected mono 44.1 kHz 16-bit PCM, up to five seconds.");

        var samples = new float[bytes.Length / 2];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = (short)(bytes[2 * index] | bytes[2 * index + 1] << 8) / 32768f;
        var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), samples.Length, 1, rate, false);
        try
        {
            if (!clip.SetData(samples, 0))
                throw new InvalidDataException("Could not load existing engine WAV data.");
            return clip;
        }
        catch
        {
            UnityEngine.Object.Destroy(clip);
            throw;
        }
    }
}

internal static class BentleyContinentalGTSpeedAudioDiagnostics
{
    internal static bool AudioDebugEnabled { get; set; } = false;

    internal static void Info(ModContext? context, string message)
    {
        if (BentleyContinentalGTSpeedDiagnostics.DebugEnabled && AudioDebugEnabled)
            context?.Logger.Info("BentleyContinentalGTSpeed audio: " + message);
    }
}
