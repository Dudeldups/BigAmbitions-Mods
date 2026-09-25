#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class CadillacEscaladeLightingController : MonoBehaviour
{
    private const string DrlLeftName = "CadillacEscalade_Light_DRL_FL";
    private const string DrlRightName = "CadillacEscalade_Light_DRL_FR";
    private const string BumperIndicatorLeftName =
        "CadillacEscalade_Light_BumperIndicator_FL";
    private const string BumperIndicatorRightName =
        "CadillacEscalade_Light_BumperIndicator_FR";
    private const string FrontIndicatorLeftName =
        "CadillacEscalade_Light_FrontIndicator_FL";
    private const string FrontIndicatorRightName =
        "CadillacEscalade_Light_FrontIndicator_FR";
    private const string HeadlampsName = "CadillacEscalade_Light_Headlamps";
    private const string TailLightsName = "CadillacEscalade_Light_TailLights";
    private const string BrakeLightsName = "CadillacEscalade_Light_BrakeLights";
    private const string ThirdBrakeLightName = "CadillacEscalade_Light_ThirdBrakeLight";
    private const string ReverseLightsName = "CadillacEscalade_Light_ReverseLights";
    private const string RearIndicatorLeftName =
        "CadillacEscalade_Light_RearIndicator_RL";
    private const string RearIndicatorRightName =
        "CadillacEscalade_Light_RearIndicator_RR";
    private const float BlinkerHalfPeriod = 0.42f;
    private static readonly BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? TrafficMain = typeof(VehicleLightsComponent).GetField("_main", InstanceMembers);
    private static readonly FieldInfo? TrafficBrake = typeof(VehicleLightsComponent).GetField("_brake", InstanceMembers);
    private static readonly FieldInfo? TrafficReverse = typeof(VehicleLightsComponent).GetField("_reverse", InstanceMembers);
    private static readonly FieldInfo? TrafficLeftBlinker = typeof(VehicleLightsComponent).GetField("_leftBlinker", InstanceMembers);
    private static readonly FieldInfo? TrafficRightBlinker = typeof(VehicleLightsComponent).GetField("_rightBlinker", InstanceMembers);
    private static readonly FieldInfo? TrafficBlinkerPhase = typeof(VehicleLightsComponent).GetField("_lastBlinkerState", InstanceMembers);

    private readonly List<GameObject> generatedObjects = new List<GameObject>();
    private readonly List<Material> generatedMaterials = new List<Material>();
    private VehicleController? vehicle;
    private VehicleLightsComponent? trafficLights;
    private ModContext? context;
    private object? brakes;
    private object? blinkers;
    private object? transmission;
    private Light? templateBeam;
    private Light? leftBeam;
    private Light? rightBeam;
    private MeshRenderer? leftDrlOverlay;
    private MeshRenderer? rightDrlOverlay;
    private MeshRenderer? headlampOverlay;
    private MeshRenderer? rearTailOverlay;
    private MeshRenderer? rearBrakeOverlay;
    private MeshRenderer? thirdBrakeOverlay;
    private MeshRenderer? reverseOverlay;
    private MeshRenderer? leftBlinkerOverlay;
    private MeshRenderer? rightBlinkerOverlay;
    private MeshRenderer? leftBumperBlinkerOverlay;
    private MeshRenderer? rightBumperBlinkerOverlay;
    private MeshRenderer? rearLeftBlinkerOverlay;
    private MeshRenderer? rearRightBlinkerOverlay;
    private bool initialized;
    private bool updateFailureReported;
    private bool wasBlinking;
    private float blinkerPhaseStartedAt;
    private int lastState = -1;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (initialized && vehicle == controller)
            return;
        vehicle = controller;
        context = modContext;
        LocateStateSources();
        InitializeVisuals(controller.gameObject);
    }

    internal void InitializeForAmbientTraffic(ModContext? modContext)
    {
        if (initialized)
            return;
        context = modContext;
        trafficLights = GetComponentInChildren<VehicleLightsComponent>(true);
        if (trafficLights == null || TrafficMain == null || TrafficBrake == null ||
            TrafficReverse == null || TrafficLeftBlinker == null ||
            TrafficRightBlinker == null || TrafficBlinkerPhase == null)
        {
            LogWarning("NPC light state source is unavailable.");
            return;
        }
        InitializeVisuals(gameObject);
    }

    private void InitializeVisuals(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        var white = new Color(0.86f, 0.93f, 1f, 1f);
        var red = new Color(1f, 0.006f, 0.001f, 1f);
        var amber = new Color(1f, 0.42f, 0.005f, 1f);
        leftDrlOverlay = PrepareOverlay(renderers, DrlLeftName, white, 4.8f);
        rightDrlOverlay = PrepareOverlay(renderers, DrlRightName, white, 4.8f);
        leftBumperBlinkerOverlay =
            PrepareOverlay(renderers, BumperIndicatorLeftName, amber, 6.2f);
        rightBumperBlinkerOverlay =
            PrepareOverlay(renderers, BumperIndicatorRightName, amber, 6.2f);
        leftBlinkerOverlay = PrepareOverlay(renderers, FrontIndicatorLeftName, amber, 6.2f);
        rightBlinkerOverlay = PrepareOverlay(renderers, FrontIndicatorRightName, amber, 6.2f);
        headlampOverlay = PrepareOverlay(renderers, HeadlampsName, white, 6.2f);
        rearTailOverlay = PrepareOverlay(renderers, TailLightsName,
            new Color(0.72f, 0.002f, 0.001f, 1f), 2.2f);
        rearBrakeOverlay = PrepareOverlay(renderers, BrakeLightsName, red, 4.8f);
        thirdBrakeOverlay = PrepareOverlay(renderers, ThirdBrakeLightName, red, 5.2f);
        reverseOverlay = PrepareOverlay(renderers, ReverseLightsName, white, 4.8f);
        rearLeftBlinkerOverlay =
            PrepareOverlay(renderers, RearIndicatorLeftName, amber, 6.5f);
        rearRightBlinkerOverlay =
            PrepareOverlay(renderers, RearIndicatorRightName, amber, 6.5f);
        var beamCount = trafficLights == null ? ConfigureHeadlightBeams() : 1;

        initialized = true;
        LogInfo($"initialized labeled lamp overlays={CountLampOverlays()}/7 " +
                $"indicator overlays={CountBlinkerOverlays()}/6 beams={beamCount}/2.");
        if (CountLampOverlays() != 7 || (trafficLights == null && beamCount != 2) || CountBlinkerOverlays() != 6)
            LogWarning("labeled lighting setup is incomplete; inspect overlay asset binding.");
        if (trafficLights == null && blinkers == null)
            LogWarning("VehicleBlinker state source is missing; indicator input cannot be read.");
        ApplyState();
    }

    private void Update()
    {
        if (!initialized)
            return;
        try
        {
            ApplyState();
        }
        catch (Exception exception)
        {
            if (updateFailureReported)
                return;
            updateFailureReported = true;
            LogWarning($"update failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LocateStateSources()
    {
        if (vehicle == null)
            return;
        foreach (var component in vehicle.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            if (brakes == null)
                brakes = GetMember(component, "brakes");
            if (component.GetType().FullName == "Vehicles.Components.VehicleBlinker")
                blinkers = component;
            if (component.GetType().FullName == "NWH.VehiclePhysics2.VehicleController")
                transmission = GetMember(GetMember(component, "powertrain"), "transmission");
        }
        foreach (var light in vehicle.GetComponentsInChildren<Light>(true))
        {
            if (light != null && string.Equals(light.name, "Spotlights", StringComparison.Ordinal))
            {
                templateBeam = light;
                break;
            }
        }
    }

    private int ConfigureHeadlightBeams()
    {
        if (templateBeam == null)
        {
            LogWarning("the inherited Spotlights road-light template is missing.");
            return 0;
        }
        templateBeam.enabled = false;
        leftBeam = CloneBeam(-0.68f, "LeftHeadlightBeam");
        rightBeam = CloneBeam(0.68f, "RightHeadlightBeam");
        return (leftBeam != null ? 1 : 0) + (rightBeam != null ? 1 : 0);
    }

    private Light? CloneBeam(float localX, string suffix)
    {
        if (templateBeam == null || vehicle == null)
            return null;
        var clone = Instantiate(templateBeam.gameObject, vehicle.transform, false);
        clone.name = "CadillacEscalade_" + suffix;
        clone.transform.localPosition = new Vector3(localX, 0.86f, 2.43f);
        clone.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);
        var light = clone.GetComponent<Light>();
        if (light == null)
        {
            Destroy(clone);
            return null;
        }
        light.enabled = false;
        light.cookie = null;
        light.range = 55f;
        light.spotAngle = 58f;
        light.innerSpotAngle = 34f;
        light.colorTemperature = 5000f;
        light.useColorTemperature = true;
        generatedObjects.Add(clone);
        return light;
    }

    private MeshRenderer? PrepareOverlay(
        IEnumerable<MeshRenderer> renderers,
        string name,
        Color color,
        float intensity)
    {
        var renderer = FindRenderer(renderers, name);
        if (renderer?.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"labeled overlay '{name}' is missing or has no mesh.");
            return null;
        }

        renderer.sharedMaterial = CreateUnlitMaterial(name + " Material", color, intensity);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        return renderer;
    }

    private Material CreateUnlitMaterial(string name, Color color, float intensity)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Color") ??
                     throw new InvalidOperationException("No compatible unlit shader is available.");
        var material = new Material(shader) { name = name };
        var hdrColor = color * intensity;
        hdrColor.a = 1f;
        SetColor(material, "_UnlitColor", hdrColor);
        SetColor(material, "_BaseColor", hdrColor);
        SetColor(material, "_Color", hdrColor);
        SetColor(material, "baseColorFactor", hdrColor);
        SetColor(material, "_EmissiveColor", hdrColor);
        SetColor(material, "_EmissionColor", hdrColor);
        SetFloat(material, "_SurfaceType", 1f);
        SetFloat(material, "_BlendMode", 0f);
        SetFloat(material, "_SrcBlend", 1f);
        SetFloat(material, "_DstBlend", 10f);
        SetFloat(material, "_ZWrite", 0f);
        SetFloat(material, "_ZTestMode", 4f);
        SetFloat(material, "_ZTestGBuffer", 4f);
        SetFloat(material, "_Cull", 0f);
        SetFloat(material, "_CullMode", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_BLENDMODE_ALPHA");
        material.EnableKeyword("_EMISSION");
        material.renderQueue = 3000;
        generatedMaterials.Add(material);
        return material;
    }

    private void ApplyState()
    {
        var npc = trafficLights != null;
        var controlled = vehicle != null && vehicle.controlledByPlayer;
        var lightsOn = npc ? ReadTrafficBool(TrafficMain) : controlled && GetBoolProperty(vehicle, "ShouldLightsBeOn");
        var braking = npc ? ReadTrafficBool(TrafficBrake) : controlled &&
            (GetBoolProperty(brakes, "IsBraking") || GetBoolMethod(brakes, "IsBraking"));
        var leftBlinker = npc ? ReadTrafficBool(TrafficLeftBlinker) :
            controlled && GetBoolField(blinkers, "_isLeftBlinkerOn");
        var rightBlinker = npc ? ReadTrafficBool(TrafficRightBlinker) :
            controlled && GetBoolField(blinkers, "_isRightBlinkerOn");
        var reversing = npc ? ReadTrafficBool(TrafficReverse) :
            controlled && GetIntMember(transmission, "Gear") < 0;
        var blinking = leftBlinker || rightBlinker;
        if (!npc && blinking && !wasBlinking)
            blinkerPhaseStartedAt = Time.unscaledTime;
        var flash = npc ? blinking && TrafficBlinkerPhase?.GetValue(trafficLights) is int phase && phase != 0 :
            blinking && Mathf.Repeat(Time.unscaledTime - blinkerPhaseStartedAt,
                BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        wasBlinking = blinking;

        // The bumper signatures share physical geometry with the indicators.
        // Suppress the corresponding white DRL for the entire time its signal
        // is selected, including the dark half of the amber blink cycle.
        SetEnabled(leftDrlOverlay, (controlled || npc) && !leftBlinker);
        SetEnabled(rightDrlOverlay, (controlled || npc) && !rightBlinker);
        SetEnabled(headlampOverlay, lightsOn);
        SetEnabled(rearTailOverlay, lightsOn && !braking);
        SetEnabled(rearBrakeOverlay, braking);
        SetEnabled(thirdBrakeOverlay, braking);
        SetEnabled(reverseOverlay, reversing);
        SetEnabled(leftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(leftBumperBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rightBumperBlinkerOverlay, rightBlinker && flash);
        SetEnabled(rearLeftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rearRightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(leftBeam, controlled && lightsOn);
        SetEnabled(rightBeam, controlled && lightsOn);
        if (templateBeam != null)
            templateBeam.enabled = false;

        var state = (controlled ? 1 : 0) | (lightsOn ? 2 : 0) | (braking ? 4 : 0) |
                    (leftBlinker ? 8 : 0) | (rightBlinker ? 16 : 0) | (reversing ? 32 : 0);
        if (state == lastState)
            return;
        lastState = state;
        LogInfo($"state npc={npc} playerControlled={controlled} lights={lightsOn} braking={braking} " +
                $"reverse={reversing} leftBlinker={leftBlinker} rightBlinker={rightBlinker}.");
    }

    private bool ReadTrafficBool(FieldInfo? field) =>
        trafficLights != null && field?.GetValue(trafficLights) is bool value && value;

    private int CountLampOverlays() =>
        (leftDrlOverlay != null ? 1 : 0) + (rightDrlOverlay != null ? 1 : 0) +
        (headlampOverlay != null ? 1 : 0) + (rearTailOverlay != null ? 1 : 0) +
        (rearBrakeOverlay != null ? 1 : 0) + (thirdBrakeOverlay != null ? 1 : 0) +
        (reverseOverlay != null ? 1 : 0);

    private int CountBlinkerOverlays() =>
        (leftBlinkerOverlay != null ? 1 : 0) + (rightBlinkerOverlay != null ? 1 : 0) +
        (leftBumperBlinkerOverlay != null ? 1 : 0) +
        (rightBumperBlinkerOverlay != null ? 1 : 0) +
        (rearLeftBlinkerOverlay != null ? 1 : 0) + (rearRightBlinkerOverlay != null ? 1 : 0);

    private static MeshRenderer? FindRenderer(IEnumerable<MeshRenderer> renderers, string name)
    {
        foreach (var renderer in renderers)
            if (renderer != null &&
                string.Equals(renderer.name, name, StringComparison.Ordinal))
                return renderer;
        return null;
    }

    private static object? GetMember(object? target, string name)
    {
        if (target == null)
            return null;
        return target.GetType().GetField(name, InstanceMembers)?.GetValue(target) ??
               target.GetType().GetProperty(name, InstanceMembers)?.GetValue(target);
    }

    private static bool GetBoolProperty(object? target, string name) =>
        GetMember(target, name) is bool value && value;

    private static bool GetBoolMethod(object? target, string name) =>
        target != null && target.GetType().GetMethod(name, InstanceMembers, null, Type.EmptyTypes, null)
            ?.Invoke(target, null) is bool value && value;

    private static bool GetBoolField(object? target, string name) =>
        target != null && target.GetType().GetField(name, InstanceMembers)?.GetValue(target) is bool value && value;

    private static int GetIntMember(object? target, string name)
    {
        var value = GetMember(target, name);
        return value == null ? 0 : Convert.ToInt32(value);
    }

    private static void SetEnabled(Renderer? renderer, bool enabled)
    {
        if (renderer != null && renderer.enabled != enabled)
            renderer.enabled = enabled;
    }

    private static void SetEnabled(Light? light, bool enabled)
    {
        if (light != null && light.enabled != enabled)
            light.enabled = enabled;
    }

    private static void SetColor(Material material, string name, Color value)
    {
        if (material.HasProperty(name)) material.SetColor(name, value);
    }

    private static void SetFloat(Material material, string name, float value)
    {
        if (material.HasProperty(name)) material.SetFloat(name, value);
    }

    private void LogInfo(string message)
    {
        if (trafficLights != null && !CadillacEscaladeDiagnostics.NpcLightingDebugEnabled)
            return;
        CadillacEscaladeDiagnostics.Info(
            context,
            $"CadillacEscalade lighting vehicle='{gameObject.name}' " +
            $"instance={GetInstanceID()}: {message}");
    }

    private void LogWarning(string message) =>
        context?.Logger.Warn($"CadillacEscalade lighting vehicle='{gameObject.name}' " +
                             $"instance={GetInstanceID()}: {message}");

    private void OnDestroy()
    {
        foreach (var generatedObject in generatedObjects)
            if (generatedObject != null) Destroy(generatedObject);
        foreach (var generatedMaterial in generatedMaterials)
            if (generatedMaterial != null) Destroy(generatedMaterial);
    }
}
