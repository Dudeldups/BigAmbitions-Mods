#nullable enable
using System;
using BAModAPI;
using NWH.VehiclePhysics2.Sound.SoundComponents;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DefaultExecutionOrder(200)]
internal sealed class BigfootMonsterTruckAudioController : MonoBehaviour
{
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private EngineRunningComponent? engineSound;
    private AudioSource? nativeSource;
    private AudioSource? rumbleSource;
    private AudioSource? roarSource;
    private AudioSource? crackleSource;
    private AudioSource? hornSource;
    private AudioClip? rumbleClip;
    private AudioClip? roarClip;
    private AudioClip? crackleClip;
    private AudioClip? hornClip;
    private GameObject? audioHost;
    private ModContext? context;
    private bool configured;
    private bool failed;
    private bool ownsMute;
    private bool savedMute;
    private bool voicesStarted;
    private int attempts;
    private float nextAttempt;
    private float envelope;
    private float smoothRpm;
    private float smoothThrottle;

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
                if (attempts >= 20 || Time.unscaledTime < nextAttempt)
                    return;
                attempts++;
                nextAttempt = Time.unscaledTime + 0.5f;
                if (!TryConfigure())
                {
                    if (attempts == 20)
                        Warn("native engine source unavailable after 20 attempts; standard audio retained.");
                    return;
                }
            }
            UpdatePlayback();
        }
        catch (Exception exception)
        {
            failed = true;
            Cleanup();
            Warn(
                $"custom engine audio failed; standard audio restored: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private bool TryConfigure()
    {
        physics = vehicle!.GetComponent<PhysicsVehicle>();
        engineSound = physics?.soundManager.engineRunningComponent;
        nativeSource = engineSound?.source;
        if (physics == null || nativeSource == null || nativeSource.clip == null)
            return false;

        audioHost = new GameObject("BigfootMonsterTruck_EngineAudio");
        audioHost.transform.SetParent(vehicle.transform, false);
        audioHost.transform.position = nativeSource.transform.position;

        rumbleClip = BigfootMonsterTruckEngineWave.CreateRumble();
        roarClip = BigfootMonsterTruckEngineWave.CreateRoar();
        crackleClip = BigfootMonsterTruckEngineWave.CreateCrackle();
        hornClip = BigfootMonsterTruckEngineWave.CreateHorn();
        rumbleSource = CreateSource("LowRumble", rumbleClip, 1500f, 0.05f);
        roarSource = CreateSource("SuperchargedRoar", roarClip, 5200f, 0.18f, 90f);
        crackleSource = CreateSource("ExhaustCrackle", crackleClip, 6800f, 0.24f, 520f);
        hornSource = CreateSource("DualToneTruckHorn", hornClip, 7200f, 0.06f, 85f);
        var otherSource = physics.soundManager.otherSourceGO?.GetComponent<AudioSource>();
        if (otherSource?.outputAudioMixerGroup != null)
            hornSource.outputAudioMixerGroup = otherSource.outputAudioMixerGroup;
        hornSource.maxDistance = Mathf.Max(60f, nativeSource.maxDistance * 1.4f);
        configured = true;
        return true;
    }

    private AudioSource CreateSource(
        string name,
        AudioClip clip,
        float lowPassCutoff,
        float distortion,
        float highPassCutoff = 10f)
    {
        var layer = new GameObject(name);
        layer.transform.SetParent(audioHost!.transform, false);
        var source = layer.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.clip = clip;
        source.volume = 0f;
        source.outputAudioMixerGroup = nativeSource!.outputAudioMixerGroup;
        source.spatialBlend = nativeSource.spatialBlend;
        source.minDistance = nativeSource.minDistance;
        source.maxDistance = nativeSource.maxDistance * 1.25f;
        source.rolloffMode = nativeSource.rolloffMode;
        source.SetCustomCurve(
            AudioSourceCurveType.CustomRolloff,
            nativeSource.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        source.dopplerLevel = 0f;
        source.priority = nativeSource.priority;

        var lowPass = layer.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = lowPassCutoff;
        lowPass.lowpassResonanceQ = 1.15f;
        var highPass = layer.AddComponent<AudioHighPassFilter>();
        highPass.cutoffFrequency = highPassCutoff;
        highPass.highpassResonanceQ = 1.05f;
        var distortionFilter = layer.AddComponent<AudioDistortionFilter>();
        distortionFilter.distortionLevel = distortion;
        return source;
    }

    private void UpdatePlayback()
    {
        if (physics == null || nativeSource == null || audioHost == null ||
            rumbleSource == null || roarSource == null || crackleSource == null ||
            hornSource == null)
            throw new InvalidOperationException("Configured engine audio was removed.");

        audioHost.transform.position = nativeSource.transform.position;
        var controlled = vehicle!.controlledByPlayer;
        var engine = physics.powertrain.engine;
        var running = controlled && engine.ignition && engine.IsRunning && engine.canRun;
        var paused = Time.timeScale <= 0f || AudioListener.pause;
        var master = Mathf.Clamp01(physics.soundManager.masterVolume);
        hornSource.transform.position = vehicle.transform.TransformPoint(new Vector3(0f, 1.55f, 2.1f));
        UpdateHorn(controlled && !paused && physics.input.Horn, master);

        if (controlled)
        {
            if (!ownsMute)
            {
                savedMute = nativeSource.mute;
                ownsMute = true;
            }
            nativeSource.mute = true;
        }
        else
        {
            RestoreNativeMute();
        }

        if (paused)
        {
            StopVoices();
            return;
        }

        var follow = 1f - Mathf.Exp(-Time.deltaTime / 0.11f);
        var rawRpm = engine.RPMPercent * engine.revLimiterRPM;
        smoothRpm = Mathf.Lerp(smoothRpm, rawRpm, follow);
        smoothThrottle = Mathf.Lerp(
            smoothThrottle,
            Mathf.Max(Mathf.Clamp01(engine.ThrottlePosition), Mathf.Clamp01(physics.input.Throttle)),
            follow);
        envelope = Mathf.MoveTowards(envelope, running ? 1f : 0f, Time.deltaTime * 5f);

        var normalizedRpm = Mathf.Clamp01(
            (smoothRpm - engine.idleRPM) /
            Mathf.Max(1f, engine.revLimiterRPM - engine.idleRPM));
        var revCurve = Mathf.Pow(normalizedRpm, 0.68f);
        var load = Mathf.SmoothStep(0f, 1f, smoothThrottle);

        var combustionPitch = Mathf.Lerp(0.94f, 2.35f, revCurve);
        rumbleSource.pitch = combustionPitch;
        roarSource.pitch = combustionPitch;
        crackleSource.pitch = Mathf.Lerp(0.95f, 1.55f, revCurve);
        rumbleSource.volume = envelope * master * Mathf.Lerp(0.20f, 0.28f, load);
        roarSource.volume = envelope * master * Mathf.Lerp(0.055f, 0.20f, load) *
                            Mathf.Lerp(0.78f, 1f, revCurve);
        crackleSource.volume = envelope * master * Mathf.Lerp(0.045f, 0.22f, load) * 1.05f *
                               Mathf.Lerp(0.75f, 1f, revCurve);
        rumbleSource.mute = roarSource.mute = crackleSource.mute = controlled && savedMute;

        if (envelope <= 0f)
            StopVoices();
        else if (!voicesStarted)
        {
            var startTime = AudioSettings.dspTime + 0.03d;
            rumbleSource.PlayScheduled(startTime);
            roarSource.PlayScheduled(startTime);
            crackleSource.PlayScheduled(startTime);
            voicesStarted = true;
        }
    }

    private void UpdateHorn(bool pressed, float masterVolume)
    {
        if (hornSource == null)
            return;
        var targetVolume = pressed ? masterVolume * 0.74f : 0f;
        if (pressed && !hornSource.isPlaying)
        {
            hornSource.volume = targetVolume * 0.38f;
            hornSource.Play();
        }
        hornSource.volume = Mathf.MoveTowards(
            hornSource.volume,
            targetVolume,
            Time.unscaledDeltaTime * (pressed ? 24f : 8f));
        if (!pressed && hornSource.volume <= 0f && hornSource.isPlaying)
            hornSource.Stop();
    }

    private void StopVoices()
    {
        if (rumbleSource != null)
            rumbleSource.Stop();
        if (roarSource != null)
            roarSource.Stop();
        if (crackleSource != null)
            crackleSource.Stop();
        voicesStarted = false;
    }

    private void RestoreNativeMute()
    {
        if (ownsMute && nativeSource != null)
            nativeSource.mute = savedMute;
        ownsMute = false;
    }

    private void Warn(string message) => context?.Logger.Warn(
        $"BigfootMonsterTruck audio vehicle={vehicle?.GetInstanceID()}: {message}");

    private void OnDisable()
    {
        StopVoices();
        if (hornSource != null)
        {
            hornSource.Stop();
            hornSource.volume = 0f;
        }
        RestoreNativeMute();
        envelope = smoothRpm = smoothThrottle = 0f;
    }

    private void Cleanup()
    {
        OnDisable();
        configured = false;
        if (audioHost != null)
            Destroy(audioHost);
        if (rumbleClip != null)
            Destroy(rumbleClip);
        if (roarClip != null)
            Destroy(roarClip);
        if (crackleClip != null)
            Destroy(crackleClip);
        if (hornClip != null)
            Destroy(hornClip);
        audioHost = null;
        rumbleSource = null;
        roarSource = null;
        crackleSource = null;
        hornSource = null;
        rumbleClip = null;
        roarClip = null;
        crackleClip = null;
        hornClip = null;
    }

    private void OnDestroy() => Cleanup();
}
