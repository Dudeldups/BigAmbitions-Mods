#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq.Expressions;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;

internal sealed class McLarenSennaLightingController : MonoBehaviour
{
    private const float BlinkerHalfPeriod = .42f;
    private static readonly BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? TrafficMain = typeof(VehicleLightsComponent).GetField("_main", InstanceMembers);
    private static readonly FieldInfo? TrafficBrake = typeof(VehicleLightsComponent).GetField("_brake", InstanceMembers);
    private static readonly FieldInfo? TrafficReverse = typeof(VehicleLightsComponent).GetField("_reverse", InstanceMembers);
    private static readonly FieldInfo? TrafficLeftBlinker = typeof(VehicleLightsComponent).GetField("_leftBlinker", InstanceMembers);
    private static readonly FieldInfo? TrafficRightBlinker = typeof(VehicleLightsComponent).GetField("_rightBlinker", InstanceMembers);
    private static readonly FieldInfo? TrafficBlinkerPhase = typeof(VehicleLightsComponent).GetField("_lastBlinkerState", InstanceMembers);
    private static readonly string[] Names = { "FrontLeft", "FrontRight", "HeadLeft", "HeadRight", "RearLeft", "RearRight", "CenterBrake", "Reverse" };
    private readonly Lamp?[] lamps = new Lamp?[8];
    private readonly List<Light> inheritedBeams = new List<Light>();
    private VehicleController? vehicle;
    private VehicleLightsComponent? trafficLights;
    private ModContext? context;
    private Func<bool> headlightsState = () => false;
    private Func<bool> brakingState = () => false;
    private Func<bool> leftState = () => false;
    private Func<bool> rightState = () => false;
    private Func<int> gearState = () => 0;
    private Func<bool> reverseState = () => false;
    private Func<int> phaseState = () => 0;
    private Light? leftBeam;
    private Light? rightBeam;
    private bool initialized;
    private bool wasBlinking;
    private bool failureReported;
    private float blinkStarted;
    private int lastState = -1;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (initialized) return;
        vehicle = controller;
        context = modContext;
        object? brakes = null, blinkers = null, transmission = null;
        foreach (var component in controller.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            if (brakes == null) brakes = GetMember(component, "brakes");
            if (component.GetType().FullName == "Vehicles.Components.VehicleBlinker") blinkers = component;
            if (component.GetType().FullName == "NWH.VehiclePhysics2.VehicleController") transmission = GetMember(GetMember(component, "powertrain"), "transmission");
        }
        headlightsState = Accessor<bool>(controller, "ShouldLightsBeOn");
        brakingState = Accessor<bool>(brakes, "IsBraking");
        leftState = Accessor<bool>(blinkers, "_isLeftBlinkerOn");
        rightState = Accessor<bool>(blinkers, "_isRightBlinkerOn");
        gearState = Accessor<int>(transmission, "Gear");
        Bind();
    }

    internal void InitializeForAmbientTraffic(ModContext? modContext)
    {
        if (initialized) return;
        context = modContext;
        trafficLights = GetComponentInChildren<VehicleLightsComponent>(true);
        if (trafficLights == null || TrafficMain == null || TrafficBrake == null || TrafficReverse == null || TrafficLeftBlinker == null || TrafficRightBlinker == null || TrafficBlinkerPhase == null)
        { context?.Logger.Warn("McLarenSenna: NPC light state binding failed."); return; }
        headlightsState = Accessor<bool>(trafficLights, "_main");
        brakingState = Accessor<bool>(trafficLights, "_brake");
        leftState = Accessor<bool>(trafficLights, "_leftBlinker");
        rightState = Accessor<bool>(trafficLights, "_rightBlinker");
        reverseState = Accessor<bool>(trafficLights, "_reverse");
        phaseState = Accessor<int>(trafficLights, "_lastBlinkerState");
        Bind();
    }

    private void Bind()
    {
        var renderers = GetComponentsInChildren<MeshRenderer>(true);
        for (var i = 0; i < Names.Length; i++)
        {
            MeshRenderer? found = null;
            foreach (var renderer in renderers)
                if (renderer.name == "McLarenLamp_" + Names[i])
                {
                    if (found != null) throw new InvalidOperationException("Duplicate Senna emitter " + Names[i]);
                    found = renderer;
                }
            if (found == null || found.sharedMaterial == null || found.GetComponent<MeshFilter>()?.sharedMesh == null)
                throw new InvalidOperationException("Missing authored Senna emitter " + Names[i]);
            lamps[i] = new Lamp(found);
        }
        foreach (var beam in GetComponentsInChildren<Light>(true))
        {
            if (beam.name == "McLarenBeam_Left") leftBeam = beam;
            else if (beam.name == "McLarenBeam_Right") rightBeam = beam;
            else inheritedBeams.Add(beam);
        }
        if (leftBeam == null || rightBeam == null) throw new InvalidOperationException("McLaren headlight beam anchors are missing.");
        initialized = true;
        McLarenSennaDiagnostics.Info(context, "McLarenSenna lighting: bound 8 authored emitters and 2 road beams.");
        ApplyState();
    }

    private void Update()
    {
        if (!initialized) return;
        try { ApplyState(); }
        catch (Exception ex)
        {
            if (!failureReported) context?.Logger.Warn("McLarenSenna lighting failed: " + ex.Message);
            failureReported = true;
        }
    }

    private void ApplyState()
    {
        var npc = trafficLights != null;
        var controlled = vehicle != null && vehicle.controlledByPlayer;
        var running = npc || controlled;
        var headlights = running && headlightsState();
        var braking = running && brakingState();
        var left = running && leftState();
        var right = running && rightState();
        var reversing = npc ? reverseState() : controlled && gearState() < 0;
        var blinking = left || right;
        if (blinking && !wasBlinking) blinkStarted = Time.unscaledTime;
        wasBlinking = blinking;
        var flash = npc ? blinking && phaseState() != 0 : blinking && Mathf.Repeat(Time.unscaledTime - blinkStarted, BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        // The native controller can switch its donor beam back on; suppress only
        // inherited lights on this target vehicle while the two authored beams own illumination.
        foreach (var beam in inheritedBeams) if (beam != null && beam.enabled) beam.enabled = false;
        var state = (running ? 1 : 0) | (headlights ? 2 : 0) | (braking ? 4 : 0) | (left ? 8 : 0) | (right ? 16 : 0) | (reversing ? 32 : 0) | (flash ? 64 : 0);
        if (state == lastState) return;
        lastState = state;
        var white = new Color(.82f, .92f, 1f);
        var amber = new Color(1f, .24f, .005f);
        var red = new Color(1f, .003f, .001f);
        // The real front modules share DRL/turn-signal surfaces: white is off
        // for the entire indicator cycle, including the dark half of each flash.
        lamps[0]!.Set(left ? flash : running, left ? amber : white, left ? 3f : 2.2f);
        lamps[1]!.Set(right ? flash : running, right ? amber : white, right ? 3f : 2.2f);
        lamps[2]!.Set(headlights, white, 4f);
        lamps[3]!.Set(headlights, white, 4f);
        // Combined rear modules are independently controlled by side. One physical
        // mesh per side prevents coplanar tail/brake/indicator overlays hiding each other.
        lamps[4]!.Set(left ? flash : running || braking, left ? amber : red, left ? 3f : braking ? 4f : 1.3f);
        lamps[5]!.Set(right ? flash : running || braking, right ? amber : red, right ? 3f : braking ? 4f : 1.3f);
        lamps[6]!.Set(braking, red, 4f);
        lamps[7]!.Set(reversing, white, 3f);
        leftBeam!.enabled = headlights;
        rightBeam!.enabled = headlights;
    }

    private static object? GetMember(object? target, string name) => target == null ? null :
        target.GetType().GetField(name, InstanceMembers)?.GetValue(target) ?? target.GetType().GetProperty(name, InstanceMembers)?.GetValue(target);

    private static Func<T> Accessor<T>(object? target, string name)
    {
        if (target == null) throw new InvalidOperationException("Missing Senna light state source: " + name);
        var type = target.GetType();
        var instance = Expression.Constant(target);
        Expression? read = null;
        var field = type.GetField(name, InstanceMembers);
        var property = type.GetProperty(name, InstanceMembers);
        var method = type.GetMethod(name, InstanceMembers, null, Type.EmptyTypes, null);
        if (field != null) read = Expression.Field(instance, field);
        else if (property != null) read = Expression.Property(instance, property);
        else if (method != null) read = Expression.Call(instance, method);
        if (read == null) throw new InvalidOperationException("Missing Senna light state member: " + type.Name + "." + name);
        return Expression.Lambda<Func<T>>(Expression.Convert(read, typeof(T))).Compile();
    }

    private void OnDisable()
    {
        foreach (var lamp in lamps) lamp?.Set(false, Color.black, 0f);
        if (leftBeam != null) leftBeam.enabled = false;
        if (rightBeam != null) rightBeam.enabled = false;
        lastState = -1;
        wasBlinking = false;
    }

    private void OnDestroy() { foreach (var lamp in lamps) lamp?.Dispose(); }

    private sealed class Lamp
    {
        private readonly MeshRenderer renderer;
        private readonly Material original;
        private readonly Material material;
        internal Lamp(MeshRenderer target)
        {
            renderer = target;
            original = target.sharedMaterial;
            material = new Material(original) { name = original.name + "_Instance", hideFlags = HideFlags.DontSave };
            renderer.sharedMaterial = material;
        }
        internal void Set(bool on, Color color, float intensity)
        {
            if (renderer == null) return;
            renderer.enabled = on;
            if (!on) return;
            var hdr = color * intensity; hdr.a = 1f;
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", hdr);
            if (material.HasProperty("_Color")) material.SetColor("_Color", hdr);
            if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", hdr);
        }
        internal void Dispose()
        {
            if (renderer != null && renderer.sharedMaterial == material) renderer.sharedMaterial = original;
            UnityEngine.Object.Destroy(material);
        }
    }
}
