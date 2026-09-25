#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class KoenigseggJeskoLightingController : MonoBehaviour
{
    private const string DaylightName = "CHASSIS_mm_lights";
    private const string HeadlampName = "HEADLIGHT_LENS_LEFT_mm_lights";
    private const string SecondaryHeadlampName = "HEADLIGHT_LENS_RIGHT_mm_lights";
    private const string RearStripName = "REARBUMPER_mm_lights";
    private const string TailLeftName = "TAILLIGHT_LENS_LEFT_mm_lights";
    private const string TailRightName = "TAILLIGHT_LENS_RIGHT_mm_lights";
    private const string BrakeLeftName = "TAILLIGHT_LENS_LEFT_mm_lights";
    private const string BrakeRightName = "TAILLIGHT_LENS_RIGHT_mm_lights";
    private const string ThirdBrakeLightName = "REARBUMPER_mm_lights";
    private const string ReverseLightName = "REARBUMPER_mm_lights";
    private const string LeftBlinkerName = "HEADLIGHT_LENS_LEFT_mm_lights";
    private const string RightBlinkerName = "HEADLIGHT_LENS_RIGHT_mm_lights";
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
    private readonly List<Mesh> generatedMeshes = new List<Mesh>();
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
    private MeshRenderer? daylightOverlay;
    private MeshRenderer? headlampOverlay;
    private MeshRenderer? secondaryHeadlampOverlay;
    private MeshRenderer? rearTailLeftOverlay;
    private MeshRenderer? rearTailRightOverlay;
    private MeshRenderer? brakeLeftOverlay;
    private MeshRenderer? brakeRightOverlay;
    private MeshRenderer? centerRunningOverlay;
    private MeshRenderer? reverseOverlay;
    private MeshRenderer? leftBlinkerOverlay;
    private MeshRenderer? rightBlinkerOverlay;
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
        vehicleRoot = controller.transform;
        context = modContext;
        LocateStateSources();
        InitializeVisuals(controller.gameObject);
    }

    internal void InitializeForAmbientTraffic(ModContext? modContext)
    {
        if (initialized)
            return;
        context = modContext;
        vehicleRoot = transform;
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
        var daylight = FindRenderer(renderers, DaylightName);
        var headlamp = FindRenderer(renderers, HeadlampName);
        var secondaryHeadlamp = FindRenderer(renderers, SecondaryHeadlampName);
        var rearStrip = FindRenderer(renderers, RearStripName);
        var tailLeft = FindRenderer(renderers, TailLeftName);
        var tailRight = FindRenderer(renderers, TailRightName);
        var brakeLeft = FindRenderer(renderers, BrakeLeftName);
        var brakeRight = FindRenderer(renderers, BrakeRightName);
        var thirdBrake = FindRenderer(renderers, ThirdBrakeLightName);
        var reverseLight = FindRenderer(renderers, ReverseLightName);
        var leftBlinker = FindRenderer(renderers, LeftBlinkerName);
        var rightBlinker = FindRenderer(renderers, RightBlinkerName);

        daylightOverlay = CreateFilteredOverlay(daylight,
            center => Mathf.Abs(center.x) > 0.64f && center.z > 1.50f,
            "DaytimeRunningLights",
            new Color(0.80f, 0.90f, 1f, 1f), 5.2f, 1.002f);
        headlampOverlay = CreateDirectionalFilteredOverlay(headlamp,
            (center, normal) => Mathf.Abs(center.x) < 0.84f && normal.z > 0.20f,
            "HeadlampProjectors", new Color(0.90f, 0.95f, 1f, 1f), 4.2f, 1.001f);
        secondaryHeadlampOverlay = CreateDirectionalFilteredOverlay(secondaryHeadlamp,
            (center, normal) => Mathf.Abs(center.x) < 0.84f && normal.z > 0.20f,
            "HeadlampSecondary", new Color(0.84f, 0.92f, 1f, 1f), 3.8f, 1.001f);
        // The single center lamp is the Jesko's rear running lamp. The outer
        // assemblies do not glow as one lens: their inner connected faces are
        // brake emitters and their existing smaller faces are indicators.
        rearTailLeftOverlay = null;
        rearTailRightOverlay = null;
        brakeLeftOverlay = CreateConnectedComponentFilteredOverlay(brakeLeft,
            (_, triangleCount) => triangleCount == 29,
            "RearBrakeSignatureLeft", new Color(1f, 0.008f, 0.001f, 1f), 4.0f, 1.003f);
        brakeRightOverlay = CreateConnectedComponentFilteredOverlay(brakeRight,
            (_, triangleCount) => triangleCount == 29,
            "RearBrakeSignatureRight", new Color(1f, 0.008f, 0.001f, 1f), 4.0f, 1.003f);
        centerRunningOverlay = CreateFilteredOverlay(thirdBrake,
            center => Mathf.Abs(center.x) < 0.15f && center.y > 0.58f,
            "CenterRearRunningLight",
            new Color(0.78f, 0.006f, 0.002f, 1f), 1.7f, 1.004f);
        reverseOverlay = CreateFilteredOverlay(reverseLight,
            center => Mathf.Abs(center.x) < 0.14f && center.y > 0.36f && center.y < 0.44f,
            "ReverseLight",
            new Color(0.92f, 0.96f, 1f, 1f), 4.8f, 1.002f);
        var amber = new Color(1f, 0.42f, 0.001f, 1f);
        leftBlinkerOverlay = CreateDirectionalFilteredOverlay(leftBlinker,
            (center, normal) => Mathf.Abs(center.x) < 0.84f && normal.z > 0.20f,
            "LeftIndicator", amber, 4.5f, 1.003f);
        rightBlinkerOverlay = CreateDirectionalFilteredOverlay(rightBlinker,
            (center, normal) => Mathf.Abs(center.x) < 0.84f && normal.z > 0.20f,
            "RightIndicator", amber, 4.5f, 1.003f);
        rearLeftBlinkerOverlay = CreateConnectedComponentFilteredOverlay(tailLeft,
            (_, triangleCount) => triangleCount >= 4 && triangleCount < 13,
            "RearLeftIndicator", amber, 5.2f, 1.005f);
        rearRightBlinkerOverlay = CreateConnectedComponentFilteredOverlay(tailRight,
            (_, triangleCount) => triangleCount >= 4 && triangleCount < 13,
            "RearRightIndicator", amber, 5.2f, 1.005f);
        var beamCount = trafficLights == null ? ConfigureHeadlightBeams() : 1;

        initialized = true;
        LogInfo($"initialized front='{daylight?.name}/{headlamp?.name}/{secondaryHeadlamp?.name}' " +
                $"rear='{rearStrip?.name}' tail='{tailLeft?.name}/{tailRight?.name}' " +
                $"brake='{brakeLeft?.name}/{brakeRight?.name}' " +
                $"centerRunning='{thirdBrake?.name}' " +
                $"reverse='{reverseLight?.name}' beams={beamCount}/2 " +
                $"lampOverlays={CountLampOverlays()}/7 blinkerOverlays={CountBlinkerOverlays()}/4.");
        if (CountLampOverlays() != 7 || (trafficLights == null && beamCount != 2) || CountBlinkerOverlays() != 4)
            LogWarning("lighting setup is incomplete; inspect renderer-name diagnostics.");
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
        leftBeam = CloneBeam(-0.72f, "LeftHeadlightBeam");
        rightBeam = CloneBeam(0.72f, "RightHeadlightBeam");
        return (leftBeam != null ? 1 : 0) + (rightBeam != null ? 1 : 0);
    }

    private Light? CloneBeam(float localX, string suffix)
    {
        if (templateBeam == null || vehicleRoot == null)
            return null;
        var clone = Instantiate(templateBeam.gameObject, vehicleRoot, false);
        clone.name = "KoenigseggJesko_" + suffix;
        clone.transform.localPosition = new Vector3(localX, 0.58f, 2.22f);
        clone.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
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
        light.colorTemperature = 5600f;
        light.useColorTemperature = true;
        generatedObjects.Add(clone);
        return light;
    }

    private MeshRenderer? CreateOverlay(MeshRenderer? source, string suffix, Color color,
        float intensity, float scale = 1.002f)
    {
        if (source == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"overlay '{suffix}' source is missing.");
            return null;
        }
        return CreateOverlayObject(source, source.GetComponent<MeshFilter>().sharedMesh,
            suffix, color, intensity, scale);
    }

    private MeshRenderer? CreateFilteredOverlay(MeshRenderer? source,
        Func<Vector3, bool> includeTriangleCenter, string suffix, Color color,
        float intensity, float scale)
    {
        return CreateDirectionalFilteredOverlay(source,
            (center, normal) => includeTriangleCenter(center), suffix, color, intensity, scale);
    }

    private MeshRenderer? CreateConnectedComponentFilteredOverlay(
        MeshRenderer? source,
        Func<Bounds, int, bool> includeComponent,
        string suffix,
        Color color,
        float intensity,
        float scale)
    {
        if (source == null || vehicleRoot == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"component-filtered overlay '{suffix}' source is missing.");
            return null;
        }

        var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
        var vertices = sourceMesh.vertices;
        var parents = new int[vertices.Length];
        for (var index = 0; index < parents.Length; index++)
            parents[index] = index;

        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var sourceTriangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                Union(parents, sourceTriangles[index], sourceTriangles[index + 1]);
                Union(parents, sourceTriangles[index], sourceTriangles[index + 2]);
            }
        }

        var componentBounds = new Dictionary<int, Bounds>();
        for (var index = 0; index < vertices.Length; index++)
        {
            var root = FindRoot(parents, index);
            if (!componentBounds.TryGetValue(root, out var bounds))
                componentBounds[root] = new Bounds(vertices[index], Vector3.zero);
            else
                bounds.Encapsulate(vertices[index]);
        }

        var componentTriangleCounts = new Dictionary<int, int>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var sourceTriangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                var root = FindRoot(parents, sourceTriangles[index]);
                componentTriangleCounts[root] = componentTriangleCounts.TryGetValue(root, out var count)
                    ? count + 1
                    : 1;
            }
        }

        var selectedRoots = new HashSet<int>();
        foreach (var pair in componentBounds)
            if (includeComponent(pair.Value,
                    componentTriangleCounts.TryGetValue(pair.Key, out var count) ? count : 0))
                selectedRoots.Add(pair.Key);

        var triangles = new List<int>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var sourceTriangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                if (!selectedRoots.Contains(FindRoot(parents, sourceTriangles[index])))
                    continue;
                triangles.Add(sourceTriangles[index]);
                triangles.Add(sourceTriangles[index + 1]);
                triangles.Add(sourceTriangles[index + 2]);
            }
        }

        if (triangles.Count == 0)
        {
            LogWarning($"component-filtered overlay '{suffix}' selected no components.");
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
            uv2 = sourceMesh.uv2
        };
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        return CreateOverlayObject(source, mesh, suffix, color, intensity, scale);
    }

    private static int FindRoot(int[] parents, int value)
    {
        while (parents[value] != value)
        {
            parents[value] = parents[parents[value]];
            value = parents[value];
        }
        return value;
    }

    private static void Union(int[] parents, int left, int right)
    {
        var leftRoot = FindRoot(parents, left);
        var rightRoot = FindRoot(parents, right);
        if (leftRoot != rightRoot)
            parents[rightRoot] = leftRoot;
    }

    private MeshRenderer? CreateDirectionalFilteredOverlay(MeshRenderer? source,
        Func<Vector3, Vector3, bool> includeTriangle, string suffix, Color color,
        float intensity, float scale)
    {
        if (source == null || vehicleRoot == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"filtered overlay '{suffix}' source is missing.");
            return null;
        }
        var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
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
                var localA = vertices[a];
                var localB = vertices[b];
                var localC = vertices[c];
                var center = vehicleRoot.InverseTransformPoint(source.transform.TransformPoint(
                    (localA + localB + localC) / 3f));
                var normal = vehicleRoot.InverseTransformDirection(source.transform.TransformDirection(
                    Vector3.Cross(localB - localA, localC - localA).normalized));
                if (!includeTriangle(center, normal))
                    continue;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }
        if (triangles.Count == 0)
        {
            LogWarning($"filtered overlay '{suffix}' selected no triangles.");
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
            uv2 = sourceMesh.uv2
        };
        mesh.SetTriangles(triangles, 0, true);
        mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        return CreateOverlayObject(source, mesh, suffix, color, intensity, scale);
    }

    private MeshRenderer CreateOverlayObject(MeshRenderer source, Mesh mesh, string suffix,
        Color color, float intensity, float scale)
    {
        var host = new GameObject("KoenigseggJesko_" + suffix);
        host.transform.SetParent(source.transform, false);
        host.transform.localScale = Vector3.one * scale;
        host.layer = source.gameObject.layer;
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CreateUnlitMaterial(host.name + " Material", color, intensity);
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        generatedObjects.Add(host);
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
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_ZWrite", 1f);
        SetFloat(material, "_Cull", 0f);
        material.EnableKeyword("_EMISSION");
        material.renderQueue = 2450;
        generatedMaterials.Add(material);
        return material;
    }

    private void ApplyState()
    {
        var npc = trafficLights != null;
        var controlled = vehicle != null && vehicle.controlledByPlayer;
        var lightsOn = npc ? ReadTrafficBool(TrafficMain) :
            controlled && GetBoolProperty(vehicle, "ShouldLightsBeOn");
        // The Jesko DRL signature is independent of the headlamp/night-light state.
        var drlOn = controlled || npc;
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

        SetEnabled(daylightOverlay, drlOn);
        SetEnabled(headlampOverlay, lightsOn);
        SetEnabled(secondaryHeadlampOverlay, lightsOn);
        SetEnabled(rearTailLeftOverlay, false);
        SetEnabled(rearTailRightOverlay, false);
        SetEnabled(brakeLeftOverlay, braking);
        SetEnabled(brakeRightOverlay, braking);
        SetEnabled(centerRunningOverlay, lightsOn);
        SetEnabled(reverseOverlay, reversing);
        SetEnabled(leftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(rearLeftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rearRightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(leftBeam, controlled && lightsOn);
        SetEnabled(rightBeam, controlled && lightsOn);
        if (templateBeam != null)
            templateBeam.enabled = false;

        var state = (controlled ? 1 : 0) | (lightsOn ? 2 : 0) | (braking ? 4 : 0) |
                    (leftBlinker ? 8 : 0) | (rightBlinker ? 16 : 0) | (reversing ? 32 : 0);
        if (drlOn)
            state |= 64;
        if (state == lastState)
            return;
        lastState = state;
        LogInfo($"state playerControlled={controlled} drl={drlOn} lights={lightsOn} braking={braking} " +
                $"reverse={reversing} leftBlinker={leftBlinker} rightBlinker={rightBlinker}.");
    }

    private bool ReadTrafficBool(FieldInfo? field) =>
        trafficLights != null && field?.GetValue(trafficLights) is bool value && value;

    private int CountLampOverlays() =>
        (daylightOverlay != null ? 1 : 0) + (headlampOverlay != null ? 1 : 0) +
        (secondaryHeadlampOverlay != null ? 1 : 0) +
        (rearTailLeftOverlay != null ? 1 : 0) + (rearTailRightOverlay != null ? 1 : 0) +
        (brakeLeftOverlay != null ? 1 : 0) + (brakeRightOverlay != null ? 1 : 0) +
        (centerRunningOverlay != null ? 1 : 0) +
        (reverseOverlay != null ? 1 : 0);

    private int CountBlinkerOverlays() =>
        (leftBlinkerOverlay != null ? 1 : 0) + (rightBlinkerOverlay != null ? 1 : 0) +
        (rearLeftBlinkerOverlay != null ? 1 : 0) + (rearRightBlinkerOverlay != null ? 1 : 0);

    private static MeshRenderer? FindRenderer(IEnumerable<MeshRenderer> renderers, string name)
    {
        foreach (var renderer in renderers)
            if (renderer != null &&
                (string.Equals(renderer.name, name, StringComparison.Ordinal) ||
                 renderer.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
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
        if (trafficLights != null && !KoenigseggJeskoDiagnostics.NpcLightingDebugEnabled)
            return;
        KoenigseggJeskoDiagnostics.Info(
            context,
            $"KoenigseggJesko lighting vehicle='{gameObject.name}' " +
            $"instance={GetInstanceID()}: {message}");
    }

    private void LogWarning(string message) =>
        context?.Logger.Warn($"KoenigseggJesko lighting vehicle='{gameObject.name}' " +
                             $"instance={GetInstanceID()}: {message}");

    private void OnDestroy()
    {
        foreach (var generatedObject in generatedObjects)
            if (generatedObject != null) Destroy(generatedObject);
        foreach (var generatedMaterial in generatedMaterials)
            if (generatedMaterial != null) Destroy(generatedMaterial);
        foreach (var generatedMesh in generatedMeshes)
            if (generatedMesh != null) Destroy(generatedMesh);
    }
}

