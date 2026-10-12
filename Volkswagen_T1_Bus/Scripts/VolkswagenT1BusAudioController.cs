#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class VolkswagenT1BusAudioController : MonoBehaviour
{
    // Manual spawns can miss the game's mixer-routed engine source. Keep the
    // unmixed fallback quiet enough to match the dealer vehicle's mixer gain.
    private const float DirectFallbackMixGain = .16f;
    private const int NativeSourceBindAttempts = 40;
    private const float NativeSourceBindInterval = .25f;
    private const int NativeAuxiliarySuppressionAttempts = 20;
    private const float NativeAuxiliarySuppressionInterval = .5f;
    private static readonly string[] NativeAuxiliarySoundNames =
    {
        "transmissionWhineComponent",
        "turboFlutterComponent",
        "turboWhistleComponent",
        "engineFanComponent",
    };
    private static readonly string[] NativeAuxiliarySourceMembers =
    {
        "transmissionSourceGO",
        "turboSourceGO",
        "whistleSourceGO",
        "whineSourceGO",
        "flutterSourceGO",
    };
    private static readonly string NativeSoundComponentsNamespace =
        typeof(EngineRunningComponent).Namespace ?? "NWH.VehiclePhysics2.Sound.SoundComponents";
    private readonly List<AudioClip> ownedClips = new List<AudioClip>();
    private readonly Dictionary<AudioSource, NativeAuxiliarySourceState> nativeAuxiliarySources =
        new Dictionary<AudioSource, NativeAuxiliarySourceState>();
    private readonly Dictionary<MonoBehaviour, NativeSoundComponentState> nativeSoundComponents =
        new Dictionary<MonoBehaviour, NativeSoundComponentState>();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private EngineRunningComponent? engineSound;
    private AudioSource? native;
    private AudioSource? sourceTemplate;
    private GameObject? audioHost;
    private AudioSource? engineSource;
    private AudioSource? driveSource;
    private AudioSource? hornSource;
    private AudioSource? hornSupportSource;
    private float originalDistortion;
    private bool savedMute, configured, failed, paused, wasControlled, voicesStarted,
        usesDirectFallbackMix, distortionCaptured, nativeMuteLogged, nativeBindExhausted;
    private bool ambientTraffic;
    private int attempts, nativeBindAttempts;
    private int nativeAuxiliarySuppressionAttempts;
    private float nextAttempt, nextNativeBindAt, nextNativeAuxiliarySuppressionAt,
        smoothRpm, smoothThrottle, envelope;
    private bool nativeAuxiliarySuppressionComplete;
    private bool nativeAuxiliarySuppressionWarningReported;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
    }

    internal void InitializeForAmbientTraffic(ModContext? modContext)
    {
        ambientTraffic = true;
        context = modContext;
        vehicle = GetComponentInChildren<VehicleController>(true);
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
            if (vehicle.controlledByPlayer || ambientTraffic)
                SuppressNativeAuxiliarySounds();
            else
                RestoreNativeSound();
            TryBindNativeEngineSource();
            UpdatePlayback();
        }
        catch (Exception ex)
        {
            failed = true;
            Cleanup();
            Warn($"recorded engine audio failed; native Car sound restored: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool TryConfigure()
    {
        physics = vehicle!.GetComponent<PhysicsVehicle>();
        engineSound = physics?.soundManager.engineRunningComponent;
        native = engineSound?.source;
        if (physics == null || context == null)
            return false;
        savedMute = native != null && native.mute;
        var otherSource = physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        // Manual spawns can omit the native engine source while still having an
        // auxiliary vehicle source with the correct mixer and spatial profile.
        sourceTemplate = native ?? otherSource;
        usesDirectFallbackMix = sourceTemplate == null ||
                                sourceTemplate.outputAudioMixerGroup == null;
        originalDistortion = engineSound?.maxDistortion ?? 0f;
        distortionCaptured = engineSound != null;
        audioHost = new GameObject("VolkswagenT1Bus_RecordedEngine");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = EnginePosition();
        engineSource = CreateSource(
            audioHost,
            LoadClip("EngineT1Worn740RPM_2x"),
            true,
            sourceTemplate);
        driveSource = CreateSource(
            audioHost,
            LoadClip("VW_T1_Worn_ConstantDrive_Mixer_Friendly"),
            true,
            sourceTemplate);
        var hornHost = new GameObject("VolkswagenT1Bus_Horn");
        hornHost.transform.SetParent(audioHost.transform, false);
        var hornTemplate = otherSource ?? sourceTemplate;
        hornSource = CreateSource(hornHost, LoadClip("HornLow"), true, hornTemplate);
        hornSupportSource = CreateSource(hornHost, LoadClip("HornHigh"), true, hornTemplate);
        if (engineSound != null) engineSound.maxDistortion = 0f;
        configured = true;
        VolkswagenT1BusDiagnostics.AudioInfo(
            context,
            $"VolkswagenT1Bus audio configured vehicle={vehicle.GetInstanceID()}, " +
            $"idleSource=user-recording@740rpm, idlePitch={VolkswagenT1BusAudioModel.EnginePitch(850f):0.000}.." +
            $"{VolkswagenT1BusAudioModel.EnginePitch(VolkswagenT1BusAudioModel.DriveBlendEndRpm):0.000}, engineGain=" +
            $"{VolkswagenT1BusAudioModel.EngineBaseVolume:0.00}.." +
            $"{VolkswagenT1BusAudioModel.EngineBaseVolume + VolkswagenT1BusAudioModel.EngineThrottleVolume:0.00}, " +
            $"driveSource=user-supplied-loop@{VolkswagenT1BusAudioModel.DrivePitch(VolkswagenT1BusAudioModel.DriveLoopReferenceRpm):0.000}x/" +
            $"{VolkswagenT1BusAudioModel.DriveLoopReferenceRpm:0}rpm, highRpmPitch=" +
            $"{VolkswagenT1BusAudioModel.DrivePitch(4300f):0.000}x@4300rpm, " +
            $"idleDriveCrossfade={VolkswagenT1BusAudioModel.DriveBlendStartRpm:0}.." +
            $"{VolkswagenT1BusAudioModel.DriveBlendEndRpm:0}rpm, " +
            $"hornVoices=low/high@{VolkswagenT1BusAudioModel.HornLowVolume:0.00}/" +
            $"{VolkswagenT1BusAudioModel.HornHighVolume:0.00}, " +
            $"source={(native != null ? "native" : sourceTemplate != null ? "auxiliary" : "direct-fallback")}, " +
            $"sourceDistance={engineSource.minDistance:0.0}..{engineSource.maxDistance:0.0}, " +
            $"directMixGain={(usesDirectFallbackMix ? DirectFallbackMixGain : 1f):0.00}, " +
            "separate RPM-mapped idle and driving loops, turboWhistle=none, forcedInduction=disabled, " +
            "broadbandNoise=none, exhaust=pops-and-crackles-disabled.");
        return true;
    }

    private void SuppressNativeAuxiliarySounds()
    {
        if (physics == null || Time.unscaledTime < nextNativeAuxiliarySuppressionAt)
            return;

        // Components and sources can be created again after the sound manager's
        // first initialization. Keep scanning at a bounded interval even after
        // the known component owners have been muted once.
        nextNativeAuxiliarySuppressionAt = Time.unscaledTime + NativeAuxiliarySuppressionInterval;
        if (nativeAuxiliarySuppressionAttempts < NativeAuxiliarySuppressionAttempts)
            nativeAuxiliarySuppressionAttempts++;

        var soundManager = physics.soundManager;
        var requiredSuppressed = 0;
        for (var index = 0; index < NativeAuxiliarySoundNames.Length; index++)
        {
            var name = NativeAuxiliarySoundNames[index];
            var component = GetMember(soundManager, name);
            if (component == null)
            {
                // The fan is optional in the sound-manager versions used by
                // some manual-spawn paths. The three whine components are not.
                if (index < NativeAuxiliarySoundNames.Length - 1)
                    requiredSuppressed = -100;
                continue;
            }

            var suppressed = false;
            if (component is MonoBehaviour soundComponent)
                suppressed = SuppressNativeSoundComponent(soundComponent, name);
            else
            {
                SuppressAuxiliarySourceOwner(component, name, true);
                TrySetMember(component, "baseVolume", 0f);
                suppressed = TrySetMember(component, "enabled", false);
            }
            if (index < NativeAuxiliarySoundNames.Length - 1)
            {
                if (suppressed)
                    requiredSuppressed++;
                else
                    requiredSuppressed = -100;
            }
        }

        foreach (var component in physics.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null ||
                !string.Equals(
                    component.GetType().Namespace,
                    NativeSoundComponentsNamespace,
                    StringComparison.Ordinal))
                continue;
            SuppressNativeSoundComponent(component, component.GetType().Name);
        }
        if (native != null)
            MuteNativeAuxiliarySource(native, "native engine source", true);

        if (requiredSuppressed == NativeAuxiliarySoundNames.Length - 1)
        {
            if (!nativeAuxiliarySuppressionComplete)
                VolkswagenT1BusDiagnostics.AudioInfo(
                    context,
                    $"VolkswagenT1Bus audio vehicle={vehicle?.GetInstanceID()}: muted " +
                    $"NWH engine and auxiliary sound components ({nativeSoundComponents.Count}) " +
                    "and their sources; suppression is re-applied while custom audio is active.");
            nativeAuxiliarySuppressionComplete = true;
        }
        else if (nativeAuxiliarySuppressionAttempts >= NativeAuxiliarySuppressionAttempts &&
                 !nativeAuxiliarySuppressionWarningReported)
        {
            nativeAuxiliarySuppressionWarningReported = true;
            Warn("some named NWH sound-manager fields were unavailable; NWH components, referenced auxiliary sources, and every source in the vehicle hierarchy are still suppressed.");
        }

        SuppressAuxiliarySourceOwner(
            GetMember(soundManager, "otherSourceGO"), "soundManager.otherSourceGO", true);
        foreach (var member in NativeAuxiliarySourceMembers)
            SuppressAuxiliarySourceOwner(GetMember(soundManager, member), member, true);

        foreach (var source in vehicle!.GetComponentsInChildren<AudioSource>(true))
        {
            if (source == null ||
                (audioHost != null && source.transform.IsChildOf(audioHost.transform)))
                continue;
            // The current NWH build can expose engine and auxiliary sources under
            // different names. Mute the full vehicle-owned hierarchy so an
            // unrecognized stock loop cannot survive beside the replacement.
            MuteNativeAuxiliarySource(source, "vehicle-owned AudioSource hierarchy", true);
        }
    }

    private bool SuppressNativeSoundComponent(MonoBehaviour component, string ownerName)
    {
        if (!nativeSoundComponents.ContainsKey(component))
        {
            var baseVolume = GetMember(component, "baseVolume") is float value
                ? value
                : (float?)null;
            nativeSoundComponents.Add(
                component,
                new NativeSoundComponentState(component.enabled, baseVolume));
        }

        SuppressAuxiliarySourceOwner(component, ownerName, true);
        TrySetMember(component, "baseVolume", 0f);
        component.enabled = false;
        return !component.enabled;
    }

    private void SuppressAuxiliarySourceOwner(
        object? owner,
        string ownerName,
        bool includeNativeEngine = false)
    {
        if (owner == null)
            return;

        if (owner is AudioSource directSource)
            MuteNativeAuxiliarySource(directSource, ownerName, includeNativeEngine);

        if (owner is GameObject gameObject)
            foreach (var ownerSource in gameObject.GetComponentsInChildren<AudioSource>(true))
                MuteNativeAuxiliarySource(ownerSource, ownerName, includeNativeEngine);
        else if (owner is Component component)
            foreach (var ownerSource in component.GetComponentsInChildren<AudioSource>(true))
                MuteNativeAuxiliarySource(ownerSource, ownerName, includeNativeEngine);

        foreach (var member in new[] { "source", "audioSource", "soundSource", "sourceGO", "audioSourceGO" })
        {
            var referenced = GetMember(owner, member);
            if (referenced is AudioSource referencedSource)
                MuteNativeAuxiliarySource(
                    referencedSource, ownerName + "." + member, includeNativeEngine);
            else if (referenced is GameObject sourceObject)
                foreach (var objectSource in sourceObject.GetComponentsInChildren<AudioSource>(true))
                    MuteNativeAuxiliarySource(
                        objectSource, ownerName + "." + member, includeNativeEngine);
            else if (referenced is Component sourceComponent)
                foreach (var componentSource in sourceComponent.GetComponentsInChildren<AudioSource>(true))
                    MuteNativeAuxiliarySource(
                        componentSource, ownerName + "." + member, includeNativeEngine);
        }
    }

    private void MuteNativeAuxiliarySource(
        AudioSource source,
        string identity,
        bool includeNativeEngine = false)
    {
        if (source == null ||
            (!includeNativeEngine && (source == native || source == engineSound?.source)) ||
            (audioHost != null && source.transform.IsChildOf(audioHost.transform)))
            return;

        var firstSuppression = !nativeAuxiliarySources.ContainsKey(source);
        var wasPlaying = source.isPlaying;
        if (firstSuppression)
            nativeAuxiliarySources.Add(source, new NativeAuxiliarySourceState(
                source.volume, source.mute, source.enabled));
        if (wasPlaying)
            source.Stop();
        source.volume = 0f;
        source.mute = true;
        source.enabled = false;
        if (firstSuppression || wasPlaying)
            VolkswagenT1BusDiagnostics.AudioInfo(
                context,
                $"VolkswagenT1Bus audio vehicle={vehicle?.GetInstanceID()}: stopped native auxiliary " +
                $"source='{source.name}' clip='{source.clip?.name ?? "<none>"}' route='{identity}'.");
    }

    private readonly struct NativeAuxiliarySourceState
    {
        public NativeAuxiliarySourceState(float volume, bool mute, bool enabled)
        {
            Volume = volume;
            Mute = mute;
            Enabled = enabled;
        }

        public float Volume { get; }
        public bool Mute { get; }
        public bool Enabled { get; }
    }

    private readonly struct NativeSoundComponentState
    {
        public NativeSoundComponentState(bool enabled, float? baseVolume)
        {
            Enabled = enabled;
            BaseVolume = baseVolume;
        }

        public bool Enabled { get; }
        public float? BaseVolume { get; }
    }

    private static object? GetMember(object target, string name)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (field != null)
                return field.GetValue(target);

            var property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (property != null && property.CanRead)
                return property.GetValue(target, null);
        }

        return null;
    }

    private static bool TrySetMember(object target, string name, float value) =>
        TrySetMember(target, name, typeof(float), value);

    private static bool TrySetMember(object target, string name, bool value) =>
        TrySetMember(target, name, typeof(bool), value);

    private static bool TrySetMember(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (field != null && field.FieldType.IsInstanceOfType(value))
            {
                field.SetValue(target, value);
                return true;
            }

            var property = type.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (property != null && property.CanWrite &&
                property.PropertyType.IsInstanceOfType(value))
            {
                property.SetValue(target, value, null);
                return true;
            }
        }

        return false;
    }

    private static bool TrySetMember(object target, string name, Type type, object value)
    {
        for (var current = target.GetType(); current != null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (field != null && field.FieldType == type)
            {
                field.SetValue(target, value);
                return true;
            }

            var property = current.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (property != null && property.CanWrite && property.PropertyType == type)
            {
                property.SetValue(target, value, null);
                return true;
            }
        }

        return false;
    }

    private void TryBindNativeEngineSource()
    {
        if (native != null || Time.unscaledTime < nextNativeBindAt)
            return;

        if (nativeBindAttempts < NativeSourceBindAttempts)
            nativeBindAttempts++;
        // The engine source is assigned after manual-spawn setup on some paths.
        nextNativeBindAt = Time.unscaledTime + NativeSourceBindInterval;
        if (physics == null)
            return;

        if (engineSound == null)
        {
            engineSound = physics.soundManager.engineRunningComponent;
            if (engineSound != null && !distortionCaptured)
            {
                originalDistortion = engineSound.maxDistortion;
                distortionCaptured = true;
                engineSound.maxDistortion = 0f;
            }
        }

        var candidate = engineSound?.source;
        if (candidate == null)
        {
            if (nativeBindAttempts >= NativeSourceBindAttempts && !nativeBindExhausted)
            {
                nativeBindExhausted = true;
                VolkswagenT1BusDiagnostics.AudioInfo(
                    context,
                    $"VolkswagenT1Bus audio vehicle={vehicle?.GetInstanceID()}: native engine AudioSource " +
                    $"not assigned after {nativeBindAttempts} checks; delayed binding will continue.");
            }
            return;
        }

        native = candidate;
        savedMute = nativeAuxiliarySources.TryGetValue(native, out var savedSourceState)
            ? savedSourceState.Mute
            : native.mute;
        MuteNativeAuxiliarySource(native, "native engine source bound late", true);
        nativeBindExhausted = true;
        VolkswagenT1BusDiagnostics.AudioInfo(
            context,
            $"VolkswagenT1Bus audio vehicle={vehicle?.GetInstanceID()}: bound delayed native engine source " +
            $"name='{native.name}' instance={native.GetInstanceID()} clip='{native.clip?.name ?? "<none>"}' " +
            $"playing={native.isPlaying} muteBefore={native.mute}.");
    }

    private AudioClip LoadClip(string name)
    {
        var path = Path.Combine(context!.ModRootPath, "Config", "Audio", name + ".wav");
        var clip = VolkswagenT1BusWave.Load(path);
        ownedClips.Add(clip);
        VolkswagenT1BusDiagnostics.AudioInfo(
            context,
            $"VolkswagenT1Bus audio loaded custom clip name={clip.name} samples={clip.samples} " +
            $"frequency={clip.frequency} path={path}.");
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
            // voice. Keep the replacement source audible and spatial in that case.
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
        return vehicle!.transform.TransformPoint(new Vector3(0f, .45f, -1.05f));
    }

    private void UpdatePlayback()
    {
        if (physics == null || audioHost == null || engineSource == null || driveSource == null ||
            hornSource == null || hornSupportSource == null)
            throw new InvalidOperationException("Configured audio source or vehicle was removed.");
        audioHost.transform.position = EnginePosition();
        var controlled = vehicle!.controlledByPlayer;
        var engine = physics.powertrain.engine;
        var running = (controlled || ambientTraffic) && engine.ignition && engine.IsRunning && engine.canRun;
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
        if ((controlled || ambientTraffic) && native != null)
        {
            if (!nativeMuteLogged)
            {
                nativeMuteLogged = true;
                VolkswagenT1BusDiagnostics.AudioInfo(
                    context,
                    $"VolkswagenT1Bus audio vehicle={vehicle?.GetInstanceID()}: disabled native engine source " +
                    $"name='{native.name}' instance={native.GetInstanceID()} controlled={controlled} ambient={ambientTraffic}.");
            }
        }
        else
        {
            nativeMuteLogged = false;
        }

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
            envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 6f);
            var master = Mathf.Clamp01(physics.soundManager.masterVolume);
            var driveBlend = VolkswagenT1BusAudioModel.DriveBlend(smoothRpm);
            var engineVolume = envelope * master * mixGain *
                               VolkswagenT1BusAudioModel.EngineVolume(smoothThrottle);
            engineSource.pitch = VolkswagenT1BusAudioModel.EnginePitch(smoothRpm);
            engineSource.volume = engineVolume * (1f - driveBlend);
            engineSource.mute = controlled && savedMute;
            driveSource.pitch = VolkswagenT1BusAudioModel.DrivePitch(smoothRpm);
            driveSource.volume = engineVolume * driveBlend;
            driveSource.mute = controlled && savedMute;
            if (envelope <= 0f) StopLayers();
            else if (!voicesStarted)
            {
                var start = AudioSettings.dspTime + .03d;
                engineSource.PlayScheduled(start);
                driveSource.PlayScheduled(start);
                voicesStarted = true;
            }
        }
    }

    private void UpdateHorn(bool pressed, float master)
    {
        if (hornSource == null || hornSupportSource == null) return;
        var lowTarget = pressed ? master * VolkswagenT1BusAudioModel.HornLowVolume : 0f;
        var highTarget = pressed ? master * VolkswagenT1BusAudioModel.HornHighVolume : 0f;
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

    private void Warn(string message) => context?.Logger.Warn($"VolkswagenT1Bus audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void RestoreNativeSound()
    {
        RestoreNativeAuxiliarySources();
        RestoreNativeSoundComponents();
        nextNativeAuxiliarySuppressionAt = 0f;
        nativeAuxiliarySuppressionAttempts = 0;
        nativeAuxiliarySuppressionComplete = false;
        nativeAuxiliarySuppressionWarningReported = false;
    }

    private void RestoreNativeSoundComponents()
    {
        foreach (var pair in nativeSoundComponents)
        {
            if (pair.Key == null)
                continue;
            if (pair.Value.BaseVolume.HasValue)
                TrySetMember(pair.Key, "baseVolume", pair.Value.BaseVolume.Value);
            pair.Key.enabled = pair.Value.Enabled;
        }
        nativeSoundComponents.Clear();
    }

    private void StopLayers()
    {
        if (engineSource != null) engineSource.Stop();
        if (driveSource != null) driveSource.Stop();
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
        if (configured && distortionCaptured && engineSound != null) engineSound.maxDistortion = originalDistortion;
        RestoreNativeSound();
        envelope = smoothRpm = smoothThrottle = 0f;
        paused = wasControlled = false;
    }

    private void OnEnable()
    {
        if (configured && distortionCaptured && engineSound != null) engineSound.maxDistortion = 0f;
    }

    private void Cleanup()
    {
        OnDisable();
        RestoreNativeAuxiliarySources();
        configured = false;
        if (audioHost != null) Destroy(audioHost);
        audioHost = null;
        engineSource = null;
        driveSource = null;
        hornSource = null;
        hornSupportSource = null;
        foreach (var clip in ownedClips) if (clip != null) Destroy(clip);
        ownedClips.Clear();
    }

    private void RestoreNativeAuxiliarySources()
    {
        foreach (var pair in nativeAuxiliarySources)
        {
            if (pair.Key == null)
                continue;
            pair.Key.volume = pair.Value.Volume;
            pair.Key.mute = pair.Value.Mute;
            pair.Key.enabled = pair.Value.Enabled;
        }
        nativeAuxiliarySources.Clear();
    }

    private void OnDestroy()
    {
        Cleanup();
    }
}

