#nullable enable
using System;
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[DisallowMultipleComponent]
public sealed class BattleBusAudioController : MonoBehaviour
{
    [SerializeField] private AudioClip[] engineClips = Array.Empty<AudioClip>();
    [SerializeField] private AudioClip? burnerClip = null;
    [SerializeField] private AudioClip? hornClip = null;
    [SerializeField] private AudioClip? longHornClip = null;
    private bool hornHeld;
    private bool longHornTriggered;
    private float hornPressedAt;
    private const float LongHornHoldSeconds = 0.7f;
    [SerializeField] private AudioClip[] radioClips = Array.Empty<AudioClip>();
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private BattleBusFlightController? flight;
    private ModContext? context;
    private AudioSource? native;
    private AudioSource[] engines = Array.Empty<AudioSource>();
    private AudioSource? burner;
    private AudioSource? horn;
    private AudioSource? radio;
    private GameObject? host;
    private System.Random random = new System.Random(230927);
    private bool configured;
    private bool wasControlled;
    private bool ownsMute;
    private bool savedMute;
    private float savedHornVolume;
    private bool warned;
    private float retryUntil;
    private float nextRadio;
    private float rpm;
    private int lastState = -1;
    private int lastRpmBand = -1;

    internal void Initialize(ModContext? value) => context = value;

    private void Awake()
    {
        vehicle = GetComponent<VehicleController>();
        physics = GetComponent<PhysicsVehicle>();
        flight = GetComponent<BattleBusFlightController>();
        random = new System.Random(GetInstanceID());
    }

    private void Update()
    {
        if (vehicle == null || physics == null || flight == null) return;
        var controlled = vehicle.controlledByPlayer;
        if (controlled && !wasControlled)
        {
            retryUntil = Time.unscaledTime + 10f;
            warned = false;
            nextRadio = Time.time;
        }
        wasControlled = controlled;
        if (!configured)
        {
            if (!controlled || Time.unscaledTime > retryUntil) return;
            if (!TryConfigure())
            {
                if (!warned && Time.unscaledTime > retryUntil - .25f)
                { context?.Logger.Warn("Battle Bus audio: native mixer/source or serialized clips unavailable after entry."); warned = true; }
                return;
            }
        }
        var paused = Time.timeScale <= 0f || AudioListener.pause;
        if (controlled && !ownsMute) { savedMute = native!.mute; savedHornVolume = physics.soundManager.hornComponent.baseVolume; ownsMute = true; }
        if (ownsMute) { native!.mute = true; physics.soundManager.hornComponent.baseVolume = 0f; }
        if (!controlled) RestoreMute();
        // Leave headroom when horn, burner, engine and radio overlap.
        var master = .8f * Mathf.Clamp01(physics.soundManager.masterVolume);
        var active = controlled && !paused && !savedMute && flight.IsBurnerOn;
        var engine = physics.powertrain.engine;
        var driving = active && flight.IsDriving && engine.ignition && engine.IsRunning && engine.canRun;
        var normalized = Mathf.InverseLerp(engine.idleRPM, engine.revLimiterRPM, engine.RPMPercent * engine.revLimiterRPM);
        rpm = Mathf.MoveTowards(rpm, normalized, Time.deltaTime * 2f);
        var load = Mathf.Clamp01(Mathf.Abs(physics.input.Vertical));
        // Preserve the approved low range, then make the upper half audible.
        var knee = Mathf.InverseLerp(engine.idleRPM, engine.revLimiterRPM, 3500f);
        var kneePitch = Mathf.Lerp(1f, 1.28f, knee);
        engines[0].pitch = rpm <= knee
            ? Mathf.Lerp(1f, kneePitch, Mathf.InverseLerp(0f, knee, rpm))
            : Mathf.Lerp(kneePitch, 1.65f, Mathf.InverseLerp(knee, 1f, rpm));
        if (BattleBusDiagnostics.DebugEnabled && BattleBusDiagnostics.AudioDebugEnabled)
        {
            var band = driving ? Mathf.FloorToInt(engine.RPMPercent * engine.revLimiterRPM / 1000f) : -1;
            if (band != lastRpmBand)
            {
                lastRpmBand = band;
                BattleBusDiagnostics.Info(context, $"Battle Bus engine RPM band={band}k pitch={engines[0].pitch:F2} limiter={engine.revLimiterRPM:F0}.");
            }
        }
        FadeLoop(engines[0], driving ? master * .60f * (.70f + load * .30f) : 0f, paused);
        var boost = flight.IsBurnerBoosted;
        burner!.pitch = boost ? 1.06f : .96f;
        FadeLoop(burner, active ? master * (boost ? .42f : .12f) : 0f, paused);
        var honking = active && physics.input.Horn;
        if (!active)
        {
            horn!.Stop();
            hornHeld = false;
            longHornTriggered = false;
        }
        else
        {
            horn!.volume = Mathf.Clamp01(master * 1.2f);
            if (honking && !hornHeld)
            {
                hornHeld = true;
                hornPressedAt = Time.unscaledTime;
                longHornTriggered = false;
            }

            var holdSeconds = hornHeld ? Time.unscaledTime - hornPressedAt : 0f;
            if (hornHeld && honking && !longHornTriggered && holdSeconds >= LongHornHoldSeconds)
            {
                PlayHornClip(longHornClip, "long", holdSeconds);
                longHornTriggered = true;
            }
            else if (hornHeld && !honking)
            {
                if (!longHornTriggered)
                {
                    var variant = holdSeconds >= LongHornHoldSeconds ? "long" : "short";
                    PlayHornClip(variant == "long" ? longHornClip : hornClip, variant, holdSeconds);
                }
                hornHeld = false;
                longHornTriggered = false;
            }
        }
        if (!active) radio!.Stop();
        else
        {
            radio!.volume = master * .55f;
            if (Time.time >= nextRadio && !radio.isPlaying)
            {
                radio.clip = radioClips[random.Next(radioClips.Length)];
                radio.Play();
                if (BattleBusDiagnostics.AudioDebugEnabled)
                    BattleBusDiagnostics.Info(context, $"Battle Bus CB played clip={radio.clip.name}, gain={radio.volume:F2}, spatial={radio.spatialBlend:F2}.");
                nextRadio = Time.time + 10f + (float)random.NextDouble() * 10f;
            }
        }
        var state = (driving ? 1 : 0) | (active ? 2 : 0) | (boost ? 4 : 0) | (honking ? 8 : 0);
        if (state != lastState)
        {
            lastState = state;
            if (BattleBusDiagnostics.AudioDebugEnabled)
                BattleBusDiagnostics.Info(context, $"Battle Bus audio vehicle={GetInstanceID()} state={state} master={master:F2} mixer={native!.outputAudioMixerGroup.name}.");
        }
    }

    private void PlayHornClip(AudioClip? clip, string variant, float holdSeconds)
    {
        if (clip == null || horn == null) return;
        horn.clip = clip;
        horn.loop = false;
        horn.Play();
        if (BattleBusDiagnostics.AudioDebugEnabled)
            BattleBusDiagnostics.Info(context,
                $"Battle Bus horn played variant={variant}, hold={holdSeconds:F2}s, clip={clip.name}, gain={horn.volume:F2}.");
    }

    private bool TryConfigure()
    {
        native = physics!.soundManager.engineRunningComponent.source;
        if (native == null || native.outputAudioMixerGroup == null || engineClips.Length != 1 ||
            burnerClip == null || hornClip == null || longHornClip == null || radioClips.Length != 1) return false;
        host = new GameObject("BattleBusAudio");
        host.transform.SetParent(transform, false);
        engines = new AudioSource[1];
        for (var i = 0; i < engines.Length; i++) engines[i] = CreateVoice(engineClips[i], "Diesel" + i, new Vector3(0f,.8f,2.8f), true);
        burner = CreateVoice(burnerClip, "Burner", new Vector3(0f,2.9f,-.7f), true);
        horn = CreateVoice(hornClip, "TwoToneHorn", new Vector3(0f,1.2f,3f), true);
        horn.spatialBlend = .65f;
        horn.rolloffMode = AudioRolloffMode.Logarithmic;
        horn.minDistance = 12f;
        horn.maxDistance = 90f;
        horn.priority = 80;
        radio = CreateVoice(radioClips[0], "CabRadio", new Vector3(-.3f,1.2f,2f), false);
        // Cabin radio must remain audible from the elevated gameplay camera.
        radio.spatialBlend = .2f;
        radio.priority = 96;
        if (physics.soundManager.otherMixerGroup != null)
        {
            radio.outputAudioMixerGroup = physics.soundManager.otherMixerGroup;
            horn.outputAudioMixerGroup = physics.soundManager.otherMixerGroup;
        }
        configured = true;
        if (BattleBusDiagnostics.AudioDebugEnabled)
            BattleBusDiagnostics.Info(context, $"Battle Bus audio initialized vehicle={GetInstanceID()}, clips=5, mixer={native.outputAudioMixerGroup.name}.");
        return true;
    }

    private AudioSource CreateVoice(AudioClip clip, string name, Vector3 position, bool loop)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(host!.transform, false);
        obj.transform.localPosition = position;
        var source = obj.AddComponent<AudioSource>();
        source.clip = clip; source.loop = loop; source.playOnAwake = false; source.volume = 0f;
        source.outputAudioMixerGroup = native!.outputAudioMixerGroup;
        source.spatialBlend = native.spatialBlend;
        source.minDistance = native.minDistance; source.maxDistance = native.maxDistance;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, native.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        source.rolloffMode = native.rolloffMode; source.dopplerLevel = 0f; source.priority = native.priority;
        return source;
    }

    private static void FadeLoop(AudioSource source, float target, bool paused)
    {
        source.volume = paused ? 0f : Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * 2f);
        if (source.volume > .001f && !source.isPlaying) source.Play();
        else if (source.volume <= .001f && source.isPlaying) source.Stop();
    }
    private void LateUpdate()
    {
        if (!ownsMute || physics == null) return;
        // Native audio updates can restore volume after this controller Update.
        if (native != null) native.mute = true;
        physics.soundManager.hornComponent.baseVolume = 0f;
    }
    private void RestoreMute()
    {
        if (ownsMute)
        {
            if (native != null) native.mute = savedMute;
            if (physics != null) physics.soundManager.hornComponent.baseVolume = savedHornVolume;
        }
        ownsMute = false;
    }
    private void OnDisable()
    {
        foreach (var source in engines) { source.Stop(); source.volume = 0f; }
        if (burner != null) { burner.Stop(); burner.volume = 0f; }
        if (horn != null) { horn.Stop(); horn.volume = 0f; }
        if (radio != null) radio.Stop();
        RestoreMute(); wasControlled = false;
    }
    private void OnDestroy() { RestoreMute(); if (host != null) Destroy(host); }
}
