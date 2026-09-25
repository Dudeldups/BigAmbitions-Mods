#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class ChevroletCamaro1967LightingController : MonoBehaviour
{
    private const float BlinkerHalfPeriod = 0.42f;
    private static readonly BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? TrafficMain =
        typeof(VehicleLightsComponent).GetField("_main", InstanceMembers);
    private static readonly FieldInfo? TrafficBrake =
        typeof(VehicleLightsComponent).GetField("_brake", InstanceMembers);
    private static readonly FieldInfo? TrafficReverse =
        typeof(VehicleLightsComponent).GetField("_reverse", InstanceMembers);
    private static readonly FieldInfo? TrafficLeftBlinker =
        typeof(VehicleLightsComponent).GetField("_leftBlinker", InstanceMembers);
    private static readonly FieldInfo? TrafficRightBlinker =
        typeof(VehicleLightsComponent).GetField("_rightBlinker", InstanceMembers);
    private static readonly FieldInfo? TrafficBlinkerPhase =
        typeof(VehicleLightsComponent).GetField("_lastBlinkerState", InstanceMembers);

    private readonly List<GameObject> generatedObjects = new();
    private readonly List<Material> generatedMaterials = new();
    private readonly List<Mesh> generatedMeshes = new();

    private VehicleController? vehicle;
    private VehicleLightsComponent? trafficLights;
    private Transform? vehicleRoot;
    private ModContext? context;
    private object? brakes;
    private object? blinkers;
    private object? transmission;
    private Light? templateBeam;
    private Light? leftBeam;
    private Light? rightBeam;

    private MeshRenderer? headlampLeftOverlay;
    private MeshRenderer? headlampRightOverlay;
    private MeshRenderer? frontLeftBlinkerOverlay;
    private MeshRenderer? frontRightBlinkerOverlay;
    private MeshRenderer? reverseOverlay;
    private MeshRenderer? rearInnerDimOverlay;
    private MeshRenderer? rearInnerBrightOverlay;
    private MeshRenderer? rearOuterLeftDimOverlay;
    private MeshRenderer? rearOuterLeftBrightOverlay;
    private MeshRenderer? rearOuterRightDimOverlay;
    private MeshRenderer? rearOuterRightBrightOverlay;

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
        vehicleRoot = controller.transform;
        trafficLights = null;
        context = modContext;
        LocateStateSources();
        InitializeVisuals(controller.gameObject);
    }

    internal void InitializeForAmbientTraffic(ModContext? modContext)
    {
        if (initialized)
            return;

        vehicle = null;
        vehicleRoot = transform;
        context = modContext;
        trafficLights = GetComponentInChildren<VehicleLightsComponent>(true);
        if (trafficLights == null ||
            TrafficMain == null ||
            TrafficBrake == null ||
            TrafficReverse == null ||
            TrafficLeftBlinker == null ||
            TrafficRightBlinker == null ||
            TrafficBlinkerPhase == null)
        {
            context?.Logger.Warn(
                "ChevroletCamaro1967: NPC light state source is unavailable.");
            return;
        }

        InitializeVisuals(gameObject);
    }

    private void InitializeVisuals(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        var headlampLeft = FindRendererByHierarchy(renderers, "HeadlampFL");
        var headlampRight = FindRendererByHierarchy(renderers, "HeadlampFR");
        var indicatorFrontLeft = FindRendererByHierarchy(renderers, "IndicatorFL");
        var indicatorFrontRight = FindRendererByHierarchy(renderers, "IndicatorFR");
        var reverse = FindRendererByHierarchy(renderers, "ReverseLight");
        var rearInner = FindRendererByHierarchy(renderers, "RearDrivingLight_BrakeLightInner");
        var indicatorRearLeft = FindRendererByHierarchy(renderers, "IndicatorRL");
        var indicatorRearRight = FindRendererByHierarchy(renderers, "IndicatorRR");

        var warmWhite = new Color(1.0f, 0.88f, 0.66f, 1f);
        var reverseWhite = new Color(0.96f, 0.96f, 0.90f, 1f);
        var redTail = new Color(0.72f, 0.010f, 0.004f, 1f);
        var redBrake = new Color(1.0f, 0.015f, 0.003f, 1f);
        var amber = new Color(1.0f, 0.47f, 0.015f, 1f);

        headlampLeftOverlay = CreateOverlay(
            headlampLeft, "HeadlampLeft", warmWhite, 9.0f, 1.001f);
        headlampRightOverlay = CreateOverlay(
            headlampRight, "HeadlampRight", warmWhite, 9.0f, 1.001f);
        frontLeftBlinkerOverlay = CreateOverlay(
            indicatorFrontLeft, "IndicatorFrontLeft", amber, 5.2f, 1.001f);
        frontRightBlinkerOverlay = CreateOverlay(
            indicatorFrontRight, "IndicatorFrontRight", amber, 5.2f, 1.001f);
        reverseOverlay = CreateOverlay(
            reverse, "Reverse", reverseWhite, 4.6f, 1.001f);

        // RearDrivingLight_BrakeLightInner is the authored remainder after the
        // outer IndicatorRL/RR polygons were removed by the Blender exporter.
        rearInnerDimOverlay = CreateOverlay(
            rearInner, "RearInnerRunning", redTail, 2.8f, 1.001f);
        rearInnerBrightOverlay = CreateOverlay(
            rearInner, "RearInnerBrake", redBrake, 7.5f, 1.001f);
        rearOuterLeftDimOverlay = CreateOverlay(
            indicatorRearLeft, "RearOuterLeftRunning", redTail, 2.8f, 1.001f);
        rearOuterLeftBrightOverlay = CreateOverlay(
            indicatorRearLeft, "RearOuterLeftBrakeIndicator", redBrake, 7.8f, 1.001f);
        rearOuterRightDimOverlay = CreateOverlay(
            indicatorRearRight, "RearOuterRightRunning", redTail, 2.8f, 1.001f);
        rearOuterRightBrightOverlay = CreateOverlay(
            indicatorRearRight, "RearOuterRightBrakeIndicator", redBrake, 7.8f, 1.001f);

        ChevroletCamaro1967Diagnostics.Info(
            context,
            $"ChevroletCamaro1967 authored lights: " +
            $"HeadlampFL={headlampLeft != null}, HeadlampFR={headlampRight != null}, " +
            $"IndicatorFL={indicatorFrontLeft != null}, IndicatorFR={indicatorFrontRight != null}, " +
            $"ReverseLight={reverse != null}, RearInner={rearInner != null}, " +
            $"IndicatorRL={indicatorRearLeft != null}, IndicatorRR={indicatorRearRight != null}.");

        // Ambient traffic only needs the visible lamp overlays. Real Light
        // components are reserved for the player vehicle's road illumination.
        var beamCount = trafficLights == null ? ConfigureHeadlightBeams() : 0;
        initialized = true;

        ChevroletCamaro1967Diagnostics.Info(
            context,
            $"ChevroletCamaro1967 lighting: npc={trafficLights != null}, " +
            $"authored overlays={CountLampOverlays()}/11, " +
            $"blinkers={CountBlinkerOverlays()}/4, beams={beamCount}/2.");
        var incomplete = CountLampOverlays() != 11 || CountBlinkerOverlays() != 4 ||
                         (trafficLights == null && beamCount != 2);
        if (incomplete)
            context?.Logger.Warn(
                "ChevroletCamaro1967: lighting setup is incomplete; inspect Camaro mesh mapping.");

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
            context?.Logger.Warn(
                $"ChevroletCamaro1967: lighting update failed: {exception.GetType().Name}: {exception.Message}");
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
            return 0;

        templateBeam.enabled = false;
        leftBeam = CloneBeam(-0.63f, "LeftHeadlightBeam");
        rightBeam = CloneBeam(0.63f, "RightHeadlightBeam");
        return (leftBeam != null ? 1 : 0) + (rightBeam != null ? 1 : 0);
    }

    private Light? CloneBeam(float localX, string suffix)
    {
        if (templateBeam == null || vehicle == null || vehicleRoot == null)
            return null;

        var clone = Instantiate(templateBeam.gameObject, vehicleRoot, false);
        clone.name = "ChevroletCamaro1967_" + suffix;
        clone.transform.localPosition = new Vector3(localX, 0.64f, 2.20f);
        clone.transform.localRotation = Quaternion.Euler(5f, 0f, 0f);
        var light = clone.GetComponent<Light>();
        if (light == null)
        {
            Destroy(clone);
            return null;
        }

        light.enabled = false;
        light.cookie = null;
        light.range = 50f;
        light.spotAngle = 62f;
        light.innerSpotAngle = 38f;
        light.colorTemperature = 3400f;
        light.useColorTemperature = true;
        generatedObjects.Add(clone);
        return light;
    }

    private MeshRenderer? CreateOverlay(
        MeshRenderer? source,
        string suffix,
        Color color,
        float intensity,
        float scale)
    {
        if (source == null)
            return null;
        var sourceMesh = source.GetComponent<MeshFilter>()?.sharedMesh;
        if (sourceMesh == null)
            return null;
        return CreateOverlayObject(source, sourceMesh, suffix, color, intensity, scale);
    }

    private MeshRenderer? CreateFilledOverlay(
        MeshRenderer? source,
        string suffix,
        Color color,
        float intensity,
        bool front,
        float widthScale,
        float heightScale)
    {
        if (source == null || vehicle == null ||
            !TryGetVehicleLocalBounds(source, out var bounds))
        {
            return null;
        }

        return CreateVehicleLocalQuad(
            bounds,
            suffix,
            color,
            intensity,
            front,
            widthScale,
            heightScale);
    }

    private MeshRenderer? CreateFilteredFilledOverlay(
        MeshRenderer? source,
        Func<Vector3, bool> includeTriangleCenter,
        string suffix,
        Color color,
        float intensity,
        bool front,
        float widthScale,
        float heightScale)
    {
        if (source == null || vehicle == null)
            return null;

        var sourceMesh = source.GetComponent<MeshFilter>()?.sharedMesh;
        if (sourceMesh == null)
            return null;

        var vertices = sourceMesh.vertices;
        var found = false;
        var bounds = default(Bounds);
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var sourceTriangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                var a = sourceTriangles[index];
                var b = sourceTriangles[index + 1];
                var c = sourceTriangles[index + 2];
                var localA = vehicle.transform.InverseTransformPoint(
                    source.transform.TransformPoint(vertices[a]));
                var localB = vehicle.transform.InverseTransformPoint(
                    source.transform.TransformPoint(vertices[b]));
                var localC = vehicle.transform.InverseTransformPoint(
                    source.transform.TransformPoint(vertices[c]));
                var center = (localA + localB + localC) / 3f;
                if (!includeTriangleCenter(center))
                    continue;

                if (!found)
                {
                    bounds = new Bounds(localA, Vector3.zero);
                    found = true;
                }
                bounds.Encapsulate(localA);
                bounds.Encapsulate(localB);
                bounds.Encapsulate(localC);
            }
        }

        if (!found)
        {
            context?.Logger.Warn(
                $"ChevroletCamaro1967: filled light overlay '{suffix}' selected no source triangles.");
            return null;
        }

        return CreateVehicleLocalQuad(
            bounds,
            suffix,
            color,
            intensity,
            front,
            widthScale,
            heightScale);
    }

    private MeshRenderer CreateVehicleLocalQuad(
        Bounds bounds,
        string suffix,
        Color color,
        float intensity,
        bool front,
        float widthScale,
        float heightScale)
    {
        if (vehicle == null)
            throw new InvalidOperationException("Camaro vehicle is unavailable for lamp overlay creation.");

        var width = Mathf.Max(0.04f, bounds.size.x * widthScale);
        var height = Mathf.Max(0.03f, bounds.size.y * heightScale);
        var center = bounds.center;
        center.z = (front ? bounds.max.z : bounds.min.z) + (front ? 0.004f : -0.004f);

        var overlay = new GameObject("ChevroletCamaro1967_" + suffix);
        overlay.transform.SetParent(vehicle.transform, false);
        overlay.transform.localPosition = center;
        overlay.transform.localRotation = Quaternion.identity;
        overlay.transform.localScale = Vector3.one;

        var mesh = new Mesh { name = "ChevroletCamaro1967_" + suffix + "_FillMesh" };
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, -halfHeight, 0f),
            new Vector3( halfWidth, -halfHeight, 0f),
            new Vector3( halfWidth,  halfHeight, 0f),
            new Vector3(-halfWidth,  halfHeight, 0f),
        };
        // Double-sided so the thin luminous insert remains visible through the
        // authored lens regardless of source winding.
        mesh.triangles = new[]
        {
            0, 1, 2, 0, 2, 3,
            2, 1, 0, 3, 2, 0,
        };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var filter = overlay.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = overlay.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.sharedMaterial = CreateEmissionMaterial(
            "ChevroletCamaro1967_" + suffix + "_Emission",
            color,
            intensity);
        renderer.enabled = false;

        generatedObjects.Add(overlay);
        generatedMeshes.Add(mesh);
        return renderer;
    }

    private bool TryGetVehicleLocalBounds(Renderer renderer, out Bounds bounds)
    {
        bounds = default;
        if (vehicle == null)
            return false;

        var world = renderer.bounds;
        var found = false;
        for (var x = 0; x < 2; x++)
        for (var y = 0; y < 2; y++)
        for (var z = 0; z < 2; z++)
        {
            var corner = new Vector3(
                x == 0 ? world.min.x : world.max.x,
                y == 0 ? world.min.y : world.max.y,
                z == 0 ? world.min.z : world.max.z);
            var local = vehicle.transform.InverseTransformPoint(corner);
            if (!found)
            {
                bounds = new Bounds(local, Vector3.zero);
                found = true;
            }
            else
            {
                bounds.Encapsulate(local);
            }
        }

        return found;
    }

    private Material CreateEmissionMaterial(string name, Color color, float intensity)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Color") ??
                     throw new InvalidOperationException("No unlit shader is available for Camaro lamps.");
        var material = new Material(shader) { name = name };
        var hdr = color * intensity;
        if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", hdr);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", hdr);
        if (material.HasProperty("_Color")) material.SetColor("_Color", hdr);
        if (material.HasProperty("_EmissiveColor")) material.SetColor("_EmissiveColor", hdr);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", hdr);
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
        material.EnableKeyword("_EMISSION");
        generatedMaterials.Add(material);
        return material;
    }

    private MeshRenderer? CreateFilteredOverlay(
        MeshRenderer? source,
        Func<Vector3, bool> includeTriangleCenter,
        string suffix,
        Color color,
        float intensity,
        float scale)
    {
        if (source == null || vehicle == null)
            return null;
        var sourceMesh = source.GetComponent<MeshFilter>()?.sharedMesh;
        if (sourceMesh == null)
            return null;

        var vertices = sourceMesh.vertices;
        var triangles = new List<int>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var sourceTriangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                var a = sourceTriangles[index];
                var b = sourceTriangles[index + 1];
                var c = sourceTriangles[index + 2];
                var center = vehicle.transform.InverseTransformPoint(
                    source.transform.TransformPoint((vertices[a] + vertices[b] + vertices[c]) / 3f));
                if (!includeTriangleCenter(center))
                    continue;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }

        if (triangles.Count == 0)
        {
            context?.Logger.Warn($"ChevroletCamaro1967: light overlay '{suffix}' selected no source triangles.");
            return null;
        }

        var mesh = new Mesh
        {
            name = sourceMesh.name + "_" + suffix,
            indexFormat = sourceMesh.indexFormat,
            vertices = vertices,
            normals = sourceMesh.normals,
            tangents = sourceMesh.tangents,
            colors32 = sourceMesh.colors32,
            uv = sourceMesh.uv,
            uv2 = sourceMesh.uv2,
        };
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        return CreateOverlayObject(source, mesh, suffix, color, intensity, scale);
    }

    private MeshRenderer CreateOverlayObject(
        MeshRenderer source,
        Mesh mesh,
        string suffix,
        Color color,
        float intensity,
        float scale)
    {
        var overlay = new GameObject("ChevroletCamaro1967_" + suffix);
        overlay.transform.SetParent(source.transform.parent, false);
        overlay.transform.localPosition = source.transform.localPosition;
        overlay.transform.localRotation = source.transform.localRotation;
        overlay.transform.localScale = source.transform.localScale * scale;

        var filter = overlay.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = overlay.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Color");
        var material = new Material(shader)
        {
            name = "ChevroletCamaro1967_" + suffix + "_Emission"
        };
        var hdrColor = color * intensity;
        hdrColor.a = 1f;
        // HDRP/Unlit renders the visible lamp face from the unlit/base color.
        // Feed the HDR value there too instead of relying only on emission
        // properties which can leave the mesh looking dim.
        if (material.HasProperty("_UnlitColor"))
            material.SetColor("_UnlitColor", hdrColor);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", hdrColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", hdrColor);
        if (material.HasProperty("_EmissiveColor"))
            material.SetColor("_EmissiveColor", hdrColor);
        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", hdrColor);
        material.EnableKeyword("_EMISSION");
        renderer.sharedMaterial = material;
        renderer.enabled = false;

        generatedObjects.Add(overlay);
        generatedMaterials.Add(material);
        return renderer;
    }

    private void ApplyState()
    {
        var npc = trafficLights != null;
        var controlled = vehicle != null && vehicle.controlledByPlayer;
        if (!npc && vehicle == null)
            return;

        var lightsOn = npc
            ? ReadTrafficBool(TrafficMain)
            : controlled && GetBoolProperty(vehicle, "ShouldLightsBeOn");
        var braking = npc
            ? ReadTrafficBool(TrafficBrake)
            : controlled &&
              (GetBoolProperty(brakes, "IsBraking") || GetBoolMethod(brakes, "IsBraking"));
        var leftBlinker = npc
            ? ReadTrafficBool(TrafficLeftBlinker)
            : controlled && GetBoolField(blinkers, "_isLeftBlinkerOn");
        var rightBlinker = npc
            ? ReadTrafficBool(TrafficRightBlinker)
            : controlled && GetBoolField(blinkers, "_isRightBlinkerOn");
        var reversing = npc
            ? ReadTrafficBool(TrafficReverse)
            : controlled && GetIntMember(transmission, "Gear") < 0;

        var blinking = leftBlinker || rightBlinker;
        if (!npc && blinking && !wasBlinking)
            blinkerPhaseStartedAt = Time.unscaledTime;
        var flash = npc
            ? blinking &&
              TrafficBlinkerPhase?.GetValue(trafficLights) is int phase &&
              phase != 0
            : blinking &&
              Mathf.Repeat(
                  Time.unscaledTime - blinkerPhaseStartedAt,
                  BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        wasBlinking = blinking;

        SetEnabled(headlampLeftOverlay, lightsOn);
        SetEnabled(headlampRightOverlay, lightsOn);
        SetEnabled(frontLeftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(frontRightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(reverseOverlay, reversing);

        SetEnabled(rearInnerDimOverlay, lightsOn && !braking);
        SetEnabled(rearInnerBrightOverlay, braking);

        // Outer red lamps are running + brake + indicators.
        var leftOuterBright = leftBlinker ? flash : braking;
        var leftOuterDim = leftBlinker ? (!flash && lightsOn) : (lightsOn && !braking);
        var rightOuterBright = rightBlinker ? flash : braking;
        var rightOuterDim = rightBlinker ? (!flash && lightsOn) : (lightsOn && !braking);
        SetEnabled(rearOuterLeftDimOverlay, leftOuterDim);
        SetEnabled(rearOuterLeftBrightOverlay, leftOuterBright);
        SetEnabled(rearOuterRightDimOverlay, rightOuterDim);
        SetEnabled(rearOuterRightBrightOverlay, rightOuterBright);

        SetEnabled(leftBeam, controlled && lightsOn);
        SetEnabled(rightBeam, controlled && lightsOn);
        if (templateBeam != null)
            templateBeam.enabled = false;

        var state = (npc ? 64 : 0) | (controlled ? 1 : 0) |
                    (lightsOn ? 2 : 0) | (braking ? 4 : 0) |
                    (leftBlinker ? 8 : 0) | (rightBlinker ? 16 : 0) |
                    (reversing ? 32 : 0);
        if (state == lastState)
            return;
        lastState = state;
        ChevroletCamaro1967Diagnostics.Info(
            context,
            $"ChevroletCamaro1967 lights: npc={npc}, controlled={controlled}, " +
            $"head={lightsOn}, brake={braking}, reverse={reversing}, " +
            $"left={leftBlinker}, right={rightBlinker}, authoredMeshes=true.");
    }

    private bool ReadTrafficBool(FieldInfo? field) =>
        trafficLights != null &&
        field?.GetValue(trafficLights) is bool value &&
        value;

    private int CountLampOverlays() =>
        (headlampLeftOverlay != null ? 1 : 0) +
        (headlampRightOverlay != null ? 1 : 0) +
        (frontLeftBlinkerOverlay != null ? 1 : 0) +
        (frontRightBlinkerOverlay != null ? 1 : 0) +
        (reverseOverlay != null ? 1 : 0) +
        (rearInnerDimOverlay != null ? 1 : 0) +
        (rearInnerBrightOverlay != null ? 1 : 0) +
        (rearOuterLeftDimOverlay != null ? 1 : 0) +
        (rearOuterLeftBrightOverlay != null ? 1 : 0) +
        (rearOuterRightDimOverlay != null ? 1 : 0) +
        (rearOuterRightBrightOverlay != null ? 1 : 0);

    private int CountBlinkerOverlays() =>
        (frontLeftBlinkerOverlay != null ? 1 : 0) +
        (frontRightBlinkerOverlay != null ? 1 : 0) +
        (rearOuterLeftBrightOverlay != null ? 1 : 0) +
        (rearOuterRightBrightOverlay != null ? 1 : 0);

    private static MeshRenderer? FindRendererByHierarchy(
        IEnumerable<MeshRenderer> renderers,
        string marker)
    {
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;
            for (var current = renderer.transform; current != null; current = current.parent)
                if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return renderer;
        }
        return null;
    }

    private static object? GetMember(object? instance, string name)
    {
        if (instance == null)
            return null;
        var type = instance.GetType();
        return type.GetField(name, InstanceMembers)?.GetValue(instance) ??
               type.GetProperty(name, InstanceMembers)?.GetValue(instance);
    }

    private static bool GetBoolField(object? instance, string name)
    {
        if (instance == null)
            return false;
        var field = instance.GetType().GetField(name, InstanceMembers);
        return field?.FieldType == typeof(bool) && (bool)(field.GetValue(instance) ?? false);
    }

    private static bool GetBoolProperty(object? instance, string name)
    {
        if (instance == null)
            return false;
        var property = instance.GetType().GetProperty(name, InstanceMembers);
        return property?.PropertyType == typeof(bool) && (bool)(property.GetValue(instance) ?? false);
    }

    private static bool GetBoolMethod(object? instance, string name)
    {
        if (instance == null)
            return false;
        var method = instance.GetType().GetMethod(name, InstanceMembers, null, Type.EmptyTypes, null);
        return method?.ReturnType == typeof(bool) && (bool)(method.Invoke(instance, null) ?? false);
    }

    private static int GetIntMember(object? instance, string name)
    {
        var value = GetMember(instance, name);
        if (value == null)
            return 0;
        try { return Convert.ToInt32(value); }
        catch { return 0; }
    }

    private static void SetEnabled(Behaviour? behaviour, bool enabled)
    {
        if (behaviour != null)
            behaviour.enabled = enabled;
    }

    private static void SetEnabled(Renderer? renderer, bool enabled)
    {
        if (renderer != null)
            renderer.enabled = enabled;
    }

    private void OnDestroy()
    {
        foreach (var obj in generatedObjects)
            if (obj != null) Destroy(obj);
        foreach (var material in generatedMaterials)
            if (material != null) Destroy(material);
        foreach (var mesh in generatedMeshes)
            if (mesh != null) Destroy(mesh);
        generatedObjects.Clear();
        generatedMaterials.Clear();
        generatedMeshes.Clear();
    }
}
