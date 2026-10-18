#nullable enable
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;

internal sealed class DodgeRam1500TRXLightingController : MonoBehaviour
{
    private const float BlinkerHalfPeriod = .42f;
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Dictionary<string, MeshRenderer> lamps = new Dictionary<string, MeshRenderer>();
    private readonly List<GameObject> beams = new List<GameObject>();
    private VehicleController? vehicle;
    private ModContext? context;
    private Light? templateBeam, leftBeam, rightBeam;
    private Func<bool> lightsOn = () => false, braking = () => false, left = () => false, right = () => false;
    private Func<int> gear = () => 0, trafficPhase = () => 0;
    private Func<bool> trafficReverse = () => false;
    private bool npc, initialized, failed, wasBlinking;
    private float phaseStart;
    private int lastState = -1;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (initialized) return;
        vehicle = controller; context = modContext;
        var physics = controller.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        var blinker = controller.GetComponentInChildren<Vehicles.Components.VehicleBlinker>(true);
        lightsOn = Read<bool>(controller, "ShouldLightsBeOn");
        braking = Read<bool>(physics?.brakes, "IsBraking");
        gear = Read<int>(physics?.powertrain.transmission, "Gear");
        left = Read<bool>(blinker, "_isLeftBlinkerOn");
        right = Read<bool>(blinker, "_isRightBlinkerOn");
        Bind();
        foreach (var light in GetComponentsInChildren<Light>(true))
            if (light.name == "Spotlights") { templateBeam = light; break; }
        if (templateBeam == null) throw new InvalidOperationException("TRX headlight beam template is missing.");
        templateBeam.enabled = false;
        leftBeam = MakeBeam(-.72f, "Left"); rightBeam = MakeBeam(.72f, "Right");
        initialized = true;
        ApplyState();
    }

    internal void InitializeForAmbientTraffic(ModContext? modContext)
    {
        if (initialized) return;
        context = modContext; npc = true;
        var source = GetComponentInChildren<VehicleLightsComponent>(true);
        lightsOn = Read<bool>(source, "_main"); braking = Read<bool>(source, "_brake");
        left = Read<bool>(source, "_leftBlinker"); right = Read<bool>(source, "_rightBlinker");
        trafficReverse = Read<bool>(source, "_reverse"); trafficPhase = Read<int>(source, "_lastBlinkerState");
        Bind(); initialized = true; ApplyState();
    }

    private void Bind()
    {
        const string prefix = "DodgeRam1500TRX_Lamp_";
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.name.StartsWith(prefix, StringComparison.Ordinal))
                lamps.Add(renderer.name.Substring(prefix.Length), renderer);
        if (lamps.Count != 24) throw new InvalidOperationException($"TRX requires 24 authored lamp surfaces, found {lamps.Count}.");
        foreach (var pair in lamps)
        {
            var material = pair.Value.sharedMaterial;
            if (material == null || !material.HasProperty("_UnlitColor") || material.GetColor("_UnlitColor").maxColorComponent <= 0f)
                throw new InvalidOperationException("TRX lamp surface has no visible radiance: " + pair.Key);
        }
        DodgeRam1500TRXDiagnostics.Info(context, $"DodgeRam1500TRX lighting instance={GetInstanceID()}: bound 24 Blender-authored lamp surfaces (selection revision=blender-overlays-v1) with visible unlit radiance; npc={npc}.");
    }

    private Light MakeBeam(float x, string side)
    {
        var host = Instantiate(templateBeam!.gameObject, transform, false);
        host.name = "DodgeRam1500TRX_" + side + "HeadlightBeam";
        host.transform.localPosition = DodgeRam1500TRXBodyFit.RakePoint(new Vector3(x, 1.00f, 2.72f));
        host.transform.localRotation = DodgeRam1500TRXBodyFit.RakeRotation * Quaternion.Euler(3f, 0f, 0f);
        var light = host.GetComponent<Light>();
        light.enabled = false; light.cookie = null; light.range = 50f;
        light.spotAngle = 62f; light.innerSpotAngle = 38f;
        light.colorTemperature = 5600f; light.useColorTemperature = true;
        beams.Add(host); return light;
    }

    private void Update()
    {
        if (!initialized || failed) return;
        try { ApplyState(); }
        catch (Exception e)
        {
            failed = true; DisableLights();
            context?.Logger.Warn($"DodgeRam1500TRX lights instance={GetInstanceID()}: {e.GetType().Name}: {e.Message}");
        }
    }

    private void ApplyState()
    {
        var powered = npc || (vehicle != null && vehicle.controlledByPlayer);
        var running = powered && lightsOn(); var stop = powered && braking();
        var l = powered && left(); var r = powered && right();
        var reverse = powered && (npc ? trafficReverse() : gear() < 0);
        var blinking = l || r;
        if (blinking && !wasBlinking) phaseStart = Time.unscaledTime;
        wasBlinking = blinking;
        var flash = blinking && (npc ? trafficPhase() != 0 : Mathf.Repeat(Time.unscaledTime - phaseStart, BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod);
        // The labeled upper DRL remains white. Only the shared outer strip
        // hands off to its amber indicator overlay on the signaling side.
        Set("DrlLeft", powered); Set("DrlRight", powered);
        Set("DrlIndicatorLeft", powered && !l); Set("DrlIndicatorRight", powered && !r);
        Set("HeadLeft", running); Set("HeadRight", running);
        Set("FogLeft", running); Set("FogRight", running);
        // Rear driving lamps use the existing two LED chambers whenever the
        // vehicle is powered. Keep them visible during day/track driving too;
        // automatic headlamp/environment state does not suppress these emitters.
        Set("TailLeft", powered && !stop); Set("TailRight", powered && !stop);
        // Blender defines separate amber turn surfaces. Brakes and tails remain
        // independent of turn phase; only tail/brake share the same physical faces.
        Set("BrakeLeft", stop); Set("BrakeRight", stop);
        Set("ThirdBrake", stop);
        Set("ReverseLeft", reverse); Set("ReverseRight", reverse);
        Set("SignalLeft", l && flash); Set("SignalRight", r && flash);
        Set("RearSignalLeft", l && flash); Set("RearSignalRight", r && flash);
        Set("MirrorLeft", l && flash); Set("MirrorRight", r && flash);
        Set("FrontMarkers", powered); Set("RearMarkers", running); Set("Plate", running);
        if (leftBeam != null) leftBeam.enabled = running;
        if (rightBeam != null) rightBeam.enabled = running;
        if (templateBeam != null) templateBeam.enabled = false;
        var state = (powered ? 1 : 0) | (running ? 2 : 0) | (stop ? 4 : 0) | (l ? 8 : 0) | (r ? 16 : 0) | (reverse ? 32 : 0);
        if (state == lastState) return;
        lastState = state;
        if (npc ? DodgeRam1500TRXDiagnostics.NpcLightingDebugEnabled : DodgeRam1500TRXDiagnostics.LightingDebugEnabled)
            DodgeRam1500TRXDiagnostics.Info(context, $"DodgeRam1500TRX lighting instance={GetInstanceID()}: powered={powered}, running={running}, brake={stop}, reverse={reverse}, left={l}, right={r}.");
    }

    private void Set(string name, bool enabled)
    {
        var renderer = lamps[name];
        if (renderer != null && renderer.enabled != enabled) renderer.enabled = enabled;
    }

    // Reflection is resolved only once. Compiled getters avoid discovery and
    // boxed field/property values in continuous lamp animation.
    private static Func<T> Read<T>(object? target, string name)
    {
        if (target == null) throw new InvalidOperationException("TRX light state source missing: " + name);
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var instance = Expression.Constant(target, type);
            Expression? body = null;
            var field = type.GetField(name, Members | BindingFlags.DeclaredOnly);
            var property = type.GetProperty(name, Members | BindingFlags.DeclaredOnly);
            var method = type.GetMethod(name, Members | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            if (field != null) body = Expression.Field(instance, field);
            else if (property != null) body = Expression.Property(instance, property);
            else if (method != null) body = Expression.Call(instance, method);
            if (body != null) return Expression.Lambda<Func<T>>(Expression.Convert(body, typeof(T))).Compile();
        }
        throw new InvalidOperationException("TRX light state member missing: " + name);
    }

    private void DisableLights()
    {
        foreach (var lamp in lamps.Values) if (lamp != null) lamp.enabled = false;
        if (leftBeam != null) leftBeam.enabled = false;
        if (rightBeam != null) rightBeam.enabled = false;
    }
    private void OnDisable() => DisableLights();
    private void OnDestroy()
    {
        DisableLights();
        foreach (var beam in beams) if (beam != null) Destroy(beam);
    }
}
