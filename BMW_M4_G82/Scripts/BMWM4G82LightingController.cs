#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class BMWM4G82LightingController : MonoBehaviour
{
    private const string DaylightName = "BMW_DRL_Source";
    private const string LampName = "BMW_Lamp_Source";
    private const string RearStripName = "BMW_RearLamp_Source";
    private const string FrontHeadlightsName = "BMWLightRef_FrontHeadlights";
    private const string FrontInnerDrlName = "BMWLightRef_FrontInnerDrl";
    private const string FrontOuterDrlIndicatorLeftName =
        "BMWLightRef_FrontOuterDrlIndicatorLeft";
    private const string FrontOuterDrlIndicatorRightName =
        "BMWLightRef_FrontOuterDrlIndicatorRight";
    private const string MirrorTurnSignalLeftName = "BMWLightRef_MirrorTurnSignalLeft";
    private const string MirrorTurnSignalRightName = "BMWLightRef_MirrorTurnSignalRight";
    private const string RearRunningLightsName = "BMWLightRef_RearRunningLights";
    private const string RearBrakeLightsName = "BMWLightRef_RearBrakeLights";
    private const string RearReverseLightsName = "BMWLightRef_RearReverseLights";
    private const string RearIndicatorLeftName = "BMWLightRef_RearIndicatorLeft";
    private const string RearIndicatorRightName = "BMWLightRef_RearIndicatorRight";
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
    private MeshRenderer? innerDaylightOverlay;
    private MeshRenderer? leftDaylightOverlay;
    private MeshRenderer? rightDaylightOverlay;
    private MeshRenderer? headlampOverlay;
    private MeshRenderer? rearTailOverlay;
    private MeshRenderer? rearBrakeOverlay;
    private MeshRenderer? reverseOverlay;
    private MeshRenderer? leftBlinkerOverlay;
    private MeshRenderer? rightBlinkerOverlay;
    private MeshRenderer? leftMirrorBlinkerOverlay;
    private MeshRenderer? rightMirrorBlinkerOverlay;
    private MeshRenderer? rearLeftBlinkerOverlay;
    private MeshRenderer? rearRightBlinkerOverlay;
    private MeshRenderer? licensePlateOverlay;
    private bool initialized;
    private bool updateFailureReported;
    private bool wasBlinking;
    private float blinkerPhaseStartedAt;

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
        var lamp = FindRenderer(renderers, LampName);
        var rearStrip = FindRenderer(renderers, RearStripName);
        var frontHeadlights = FindRenderer(renderers, FrontHeadlightsName);
        var frontInnerDrl = FindRenderer(renderers, FrontInnerDrlName);
        var frontOuterDrlLeft = FindRenderer(renderers, FrontOuterDrlIndicatorLeftName);
        var frontOuterDrlRight = FindRenderer(renderers, FrontOuterDrlIndicatorRightName);
        var mirrorTurnSignalLeft = FindRenderer(renderers, MirrorTurnSignalLeftName);
        var mirrorTurnSignalRight = FindRenderer(renderers, MirrorTurnSignalRightName);
        var rearRunningLights = FindRenderer(renderers, RearRunningLightsName);
        var rearBrakeLights = FindRenderer(renderers, RearBrakeLightsName);
        var rearReverseLights = FindRenderer(renderers, RearReverseLightsName);
        var rearIndicatorLeft = FindRenderer(renderers, RearIndicatorLeftName);
        var rearIndicatorRight = FindRenderer(renderers, RearIndicatorRightName);

        var white = new Color(0.90f, 0.95f, 1f, 1f);
        innerDaylightOverlay = CreateOverlay(frontInnerDrl,
            "InnerDaytimeRunningLights", white, 4.8f, 1.001f);
        leftDaylightOverlay = CreateOverlay(frontOuterDrlLeft,
            "LeftOuterDaytimeRunningLight", white, 4.8f, 1.001f);
        rightDaylightOverlay = CreateOverlay(frontOuterDrlRight,
            "RightOuterDaytimeRunningLight", white, 4.8f, 1.001f);
        headlampOverlay = CreateOverlay(frontHeadlights,
            "HeadlampProjectors", white, 8.0f, 1.001f, 0.040f);
        rearTailOverlay = CreateOverlay(rearRunningLights,
            "RearRunningLights", new Color(0.45f, 0f, 0f, 1f), 2.0f, 1.002f);
        rearBrakeOverlay = CreateOverlay(rearBrakeLights,
            "RearBrakeLights", new Color(0.62f, 0f, 0f, 1f), 3.6f, 1.002f);
        reverseOverlay = CreateOverlay(rearReverseLights,
            "ReverseLights", white, 4.6f, 1.002f);
        licensePlateOverlay = CreateComponentOverlay(lamp,
            IsRearLicensePlateLight,
            "LicensePlateLight", new Color(1f, 0.88f, 0.68f, 1f),
            3.2f, 1.003f, -0.012f);
        var amber = new Color(1f, 0.42f, 0.005f, 1f);
        leftBlinkerOverlay = CreateOverlay(frontOuterDrlLeft,
            "LeftFrontIndicator", amber, 5.4f, 1.002f);
        rightBlinkerOverlay = CreateOverlay(frontOuterDrlRight,
            "RightFrontIndicator", amber, 5.4f, 1.002f);
        leftMirrorBlinkerOverlay = CreateOverlay(mirrorTurnSignalLeft,
            "LeftMirrorIndicator", amber, 4.2f, 1.002f);
        rightMirrorBlinkerOverlay = CreateOverlay(mirrorTurnSignalRight,
            "RightMirrorIndicator", amber, 4.2f, 1.002f);
        var rearAmber = new Color(1f, 0.58f, 0.005f, 1f);
        rearLeftBlinkerOverlay = CreateOverlay(rearIndicatorLeft,
            "RearLeftIndicator", rearAmber, 4.8f, 1.002f);
        rearRightBlinkerOverlay = CreateOverlay(rearIndicatorRight,
            "RearRightIndicator", rearAmber, 4.8f, 1.002f);
        var beamCount = trafficLights == null ? ConfigureHeadlightBeams() : 1;

        initialized = true;
        LogInfo($"initialized exactLabeledSources={CountExactSources(renderers)}/11 " +
                $"legacySources='{daylight?.name}/{lamp?.name}/{rearStrip?.name}' " +
                $"rearLens='{rearStrip?.name}' beams={beamCount}/2 " +
                $"lampOverlays={CountLampOverlays()}/8 blinkerOverlays={CountBlinkerOverlays()}/6.");
        if (CountExactSources(renderers) != 11 || CountLampOverlays() != 8 ||
            (trafficLights == null && beamCount != 2) || CountBlinkerOverlays() != 6)
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
        leftBeam = CloneBeam(-0.62f, "LeftHeadlightBeam");
        rightBeam = CloneBeam(0.62f, "RightHeadlightBeam");
        return (leftBeam != null ? 1 : 0) + (rightBeam != null ? 1 : 0);
    }

    private Light? CloneBeam(float localX, string suffix)
    {
        if (templateBeam == null || vehicle == null)
            return null;
        var clone = Instantiate(templateBeam.gameObject, vehicle.transform, false);
        clone.name = "BMWM4G82_" + suffix;
        clone.transform.localPosition = new Vector3(localX, 0.62f, 2.12f);
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
        float intensity, float scale = 1.002f, float longitudinalOffset = 0f)
    {
        if (source == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"overlay '{suffix}' source is missing.");
            return null;
        }
        return CreateOverlayObject(source, source.GetComponent<MeshFilter>().sharedMesh,
            suffix, color, intensity, scale, longitudinalOffset);
    }

    private MeshRenderer? CreateComponentOverlay(MeshRenderer? source,
        Func<LampComponent, bool> includeComponent, string suffix, Color color,
        float intensity, float scale, float longitudinalOffset = 0f)
    {
        if (source == null || vehicleRoot == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"component overlay '{suffix}' source is missing.");
            return null;
        }
        var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
        var vertices = sourceMesh.vertices;
        var rootVertices = new Vector3[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
            rootVertices[index] = vehicleRoot.InverseTransformPoint(
                source.transform.TransformPoint(vertices[index]));

        var allTriangles = new List<MeshTriangle>();
        var trianglesByVertex = new Dictionary<int, List<int>>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var sourceTriangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                var a = sourceTriangles[index];
                var b = sourceTriangles[index + 1];
                var c = sourceTriangles[index + 2];
                var triangleIndex = allTriangles.Count;
                allTriangles.Add(new MeshTriangle(a, b, c));
                AddTriangleForVertex(trianglesByVertex, a, triangleIndex);
                AddTriangleForVertex(trianglesByVertex, b, triangleIndex);
                AddTriangleForVertex(trianglesByVertex, c, triangleIndex);
            }
        }

        var selectedTriangles = new List<int>();
        var visited = new bool[allTriangles.Count];
        var queue = new Queue<int>();
        var componentTriangles = new List<int>();
        var componentCount = 0;
        var selectedComponentCount = 0;
        for (var seed = 0; seed < allTriangles.Count; seed++)
        {
            if (visited[seed])
                continue;

            componentCount++;
            componentTriangles.Clear();
            queue.Enqueue(seed);
            visited[seed] = true;
            var hasBounds = false;
            var bounds = default(Bounds);
            while (queue.Count > 0)
            {
                var triangleIndex = queue.Dequeue();
                componentTriangles.Add(triangleIndex);
                var triangle = allTriangles[triangleIndex];
                Encapsulate(ref bounds, ref hasBounds, rootVertices[triangle.A]);
                Encapsulate(ref bounds, ref hasBounds, rootVertices[triangle.B]);
                Encapsulate(ref bounds, ref hasBounds, rootVertices[triangle.C]);
                EnqueueNeighbors(trianglesByVertex, triangle.A, visited, queue);
                EnqueueNeighbors(trianglesByVertex, triangle.B, visited, queue);
                EnqueueNeighbors(trianglesByVertex, triangle.C, visited, queue);
            }

            var component = new LampComponent(bounds, componentTriangles.Count);
            if (!includeComponent(component))
                continue;

            selectedComponentCount++;
            foreach (var triangleIndex in componentTriangles)
            {
                var triangle = allTriangles[triangleIndex];
                selectedTriangles.Add(triangle.A);
                selectedTriangles.Add(triangle.B);
                selectedTriangles.Add(triangle.C);
            }
        }

        if (selectedTriangles.Count == 0)
        {
            LogWarning($"component overlay '{suffix}' selected no geometry from " +
                       $"{componentCount} components.");
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
        mesh.SetTriangles(selectedTriangles, 0, true);
        mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        LogInfo($"overlay '{suffix}' selected components={selectedComponentCount}/" +
                $"{componentCount} triangles={selectedTriangles.Count / 3}.");
        return CreateOverlayObject(
            source,
            mesh,
            suffix,
            color,
            intensity,
            scale,
            longitudinalOffset);
    }

    private static bool IsRear(LampComponent component) => component.Bounds.center.z < -1.60f;

    private static bool IsRearLicensePlateLight(LampComponent component) =>
        IsRear(component) && component.TriangleCount >= 16 &&
        component.TriangleCount <= 20 && Mathf.Abs(component.Bounds.center.x) >= 0.08f &&
        Mathf.Abs(component.Bounds.center.x) <= 0.16f && component.Bounds.size.x >= 0.05f &&
        component.Bounds.size.x <= 0.09f && component.Bounds.size.y <= 0.01f &&
        component.Bounds.size.z <= 0.05f;

    private static void AddTriangleForVertex(
        IDictionary<int, List<int>> trianglesByVertex,
        int vertex,
        int triangle)
    {
        if (!trianglesByVertex.TryGetValue(vertex, out var triangles))
        {
            triangles = new List<int>();
            trianglesByVertex.Add(vertex, triangles);
        }
        triangles.Add(triangle);
    }

    private static void EnqueueNeighbors(
        IReadOnlyDictionary<int, List<int>> trianglesByVertex,
        int vertex,
        bool[] visited,
        Queue<int> queue)
    {
        if (!trianglesByVertex.TryGetValue(vertex, out var neighbors))
            return;
        foreach (var neighbor in neighbors)
        {
            if (visited[neighbor])
                continue;
            visited[neighbor] = true;
            queue.Enqueue(neighbor);
        }
    }

    private static void Encapsulate(ref Bounds bounds, ref bool hasBounds, Vector3 point)
    {
        if (!hasBounds)
        {
            bounds = new Bounds(point, Vector3.zero);
            hasBounds = true;
            return;
        }
        bounds.Encapsulate(point);
    }

    private MeshRenderer CreateOverlayObject(MeshRenderer source, Mesh mesh, string suffix,
        Color color, float intensity, float scale, float longitudinalOffset)
    {
        var host = new GameObject("BMWM4G82_" + suffix);
        host.transform.SetParent(source.transform, false);
        host.transform.localScale = Vector3.one * scale;
        if (vehicleRoot != null && Mathf.Abs(longitudinalOffset) > 0.0001f)
            host.transform.position += vehicleRoot.forward * longitudinalOffset;
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

        // The labeled outer DRL mesh is shared with its indicator. While a
        // side's blinker is selected, its white DRL remains off for the entire
        // blink cycle and only the amber overlay flashes. The inner DRLs stay
        // white whenever the player controls the vehicle, including daytime.
        SetEnabled(innerDaylightOverlay, controlled || npc);
        SetEnabled(leftDaylightOverlay, (controlled || npc) && !leftBlinker);
        SetEnabled(rightDaylightOverlay, (controlled || npc) && !rightBlinker);
        SetEnabled(headlampOverlay, lightsOn);
        SetEnabled(rearTailOverlay, lightsOn);
        SetEnabled(rearBrakeOverlay, braking);
        SetEnabled(reverseOverlay, reversing);
        SetEnabled(licensePlateOverlay, lightsOn);
        SetEnabled(leftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(leftMirrorBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rightMirrorBlinkerOverlay, rightBlinker && flash);
        SetEnabled(rearLeftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rearRightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(leftBeam, controlled && lightsOn);
        SetEnabled(rightBeam, controlled && lightsOn);
        if (templateBeam != null)
            templateBeam.enabled = false;

    }

    private bool ReadTrafficBool(FieldInfo? field) =>
        trafficLights != null && field?.GetValue(trafficLights) is bool value && value;

    private int CountLampOverlays() =>
        (innerDaylightOverlay != null ? 1 : 0) +
        (leftDaylightOverlay != null ? 1 : 0) + (rightDaylightOverlay != null ? 1 : 0) +
        (headlampOverlay != null ? 1 : 0) +
        (rearTailOverlay != null ? 1 : 0) + (rearBrakeOverlay != null ? 1 : 0) +
        (reverseOverlay != null ? 1 : 0) + (licensePlateOverlay != null ? 1 : 0);

    private int CountBlinkerOverlays() =>
        (leftBlinkerOverlay != null ? 1 : 0) + (rightBlinkerOverlay != null ? 1 : 0) +
        (leftMirrorBlinkerOverlay != null ? 1 : 0) +
        (rightMirrorBlinkerOverlay != null ? 1 : 0) +
        (rearLeftBlinkerOverlay != null ? 1 : 0) + (rearRightBlinkerOverlay != null ? 1 : 0);

    private static int CountExactSources(IEnumerable<MeshRenderer> renderers)
    {
        var count = 0;
        foreach (var renderer in renderers)
        {
            if (renderer != null && renderer.name.StartsWith("BMWLightRef_", StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    private static MeshRenderer? FindRenderer(IEnumerable<MeshRenderer> renderers, string name)
    {
        foreach (var renderer in renderers)
            if (renderer != null && string.Equals(renderer.name, name, StringComparison.Ordinal))
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
        if (!BMWM4G82Diagnostics.DebugEnabled ||
            (trafficLights != null && !BMWM4G82Diagnostics.NpcLightingDebugEnabled))
            return;
        context?.Logger.Info($"BMWM4G82 lighting vehicle='{gameObject.name}' " +
                             $"instance={GetInstanceID()}: {message}");
    }

    private void LogWarning(string message) =>
        context?.Logger.Warn($"BMWM4G82 lighting vehicle='{gameObject.name}' " +
                             $"instance={GetInstanceID()}: {message}");

    private readonly struct MeshTriangle
    {
        internal MeshTriangle(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }

        internal readonly int A;
        internal readonly int B;
        internal readonly int C;
    }

    private readonly struct LampComponent
    {
        internal LampComponent(Bounds bounds, int triangleCount)
        {
            Bounds = bounds;
            TriangleCount = triangleCount;
        }

        internal readonly Bounds Bounds;
        internal readonly int TriangleCount;
    }

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
