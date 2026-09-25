#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class VolkswagenAmarokLightingController : MonoBehaviour
{
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
    private MeshRenderer? daylightOverlayRight;
    private MeshRenderer? headlampOverlay;
    private MeshRenderer? rearTailLensCover;
    private MeshRenderer? rearBrakeLensCover;
    private MeshRenderer? rearTailOverlay;
    private MeshRenderer? rearBrakeOverlay;
    private MeshRenderer? thirdBrakeOverlay;
    private MeshRenderer? reverseOverlay;
    private MeshRenderer? leftBlinkerOverlay;
    private MeshRenderer? rightBlinkerOverlay;
    private MeshRenderer? rearLeftBlinkerOverlay;
    private MeshRenderer? rearRightBlinkerOverlay;
    private MeshRenderer? sideLeftBlinkerOverlay;
    private MeshRenderer? sideRightBlinkerOverlay;
    private bool initialized;
    private bool updateFailureReported;
    private bool wasBlinking;
    private float blinkerPhaseStartedAt;
    private int lastState = -1;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (initialized && vehicle == controller) return;
        vehicle=controller; vehicleRoot=controller.transform; context=modContext; LocateStateSources();
        InitializeVisuals(controller.gameObject);
    }

    internal void InitializeForAmbientTraffic(ModContext? modContext)
    {
        if (initialized) return;
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
        var r=root.GetComponentsInChildren<MeshRenderer>(true);
        var head=FindRendererByHierarchy(r,"BHeadlights");
        var fl=FindRendererByHierarchy(r,"BDRL_Indicator_FL");
        var fr=FindRendererByHierarchy(r,"BDRL_Indicator_FR");
        var tail=FindRendererByHierarchy(r,"1RearDrivingLights");
        var brake=FindRendererByHierarchy(r,"1BrakeLights");
        var third=FindRendererByHierarchy(r,"ThirdBrakeLight");
        var reverse=FindRendererByHierarchy(r,"ReverseLights");
        var rl=FindRendererByHierarchy(r,"1IndicatorRL");
        var rr=FindRendererByHierarchy(r,"1IndicatorRR");
        var sideL=FindRendererByHierarchy(r,"SideIndicatorFL");
        var sideR=FindRendererByHierarchy(r,"SideIndicatorFR");
        var white=new Color(.86f,.93f,1f,1f); var red=new Color(1f,.008f,.002f,1f); var amber=new Color(1f,.48f,.015f,1f);
        headlampOverlay=PrepareSourceOverlay(head,"Headlights",white,5.8f);
        daylightOverlay=PrepareSourceOverlay(fl,"DRLLeft",white,3.8f); daylightOverlayRight=PrepareSourceOverlay(fr,"DRLRight",white,3.8f);
        leftBlinkerOverlay=CreateOverlay(fl,"IndicatorLeft",amber,5.2f,1.009f); rightBlinkerOverlay=CreateOverlay(fr,"IndicatorRight",amber,5.2f,1.009f);
        sideLeftBlinkerOverlay=PrepareSourceOverlay(sideL,"SideIndicatorLeft",amber,5.0f); sideRightBlinkerOverlay=PrepareSourceOverlay(sideR,"SideIndicatorRight",amber,5.0f);
        rearTailLensCover=CreateLensCover(tail,"RearTailLensCover",new Color(.34f,.012f,.008f,1f));
        rearBrakeLensCover=CreateLensCover(brake,"RearBrakeLensCover",new Color(.30f,.010f,.007f,1f));
        rearTailOverlay=PrepareSourceOverlay(tail,"RearRunning",red,2.6f); rearBrakeOverlay=PrepareSourceOverlay(brake,"RearBrake",red,4.6f);
        thirdBrakeOverlay=PrepareSourceOverlay(third,"ThirdBrake",red,4.6f); reverseOverlay=PrepareSourceOverlay(reverse,"Reverse",white,4.4f);
        rearLeftBlinkerOverlay=PrepareSourceOverlay(rl,"RearIndicatorLeft",amber,5.2f); rearRightBlinkerOverlay=PrepareSourceOverlay(rr,"RearIndicatorRight",amber,5.2f);
        LogInfo($"overlay sources head={head?.name ?? "<missing>"} fl={fl?.name ?? "<missing>"} fr={fr?.name ?? "<missing>"} " +
                $"tail={tail?.name ?? "<missing>"} brake={brake?.name ?? "<missing>"} third={third?.name ?? "<missing>"} " +
                $"reverse={reverse?.name ?? "<missing>"} rl={rl?.name ?? "<missing>"} rr={rr?.name ?? "<missing>"} " +
                $"sideL={sideL?.name ?? "<missing>"} sideR={sideR?.name ?? "<missing>"}.");
        if (trafficLights == null) ConfigureHeadlightBeams();
        initialized=true; ApplyState();
    }

    private MeshRenderer? PrepareSourceOverlay(
        MeshRenderer? source,
        string suffix,
        Color color,
        float intensity)
    {
        if (source == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"Amarok authored light source '{suffix}' is missing.");
            return null;
        }

        source.sharedMaterial = CreateUnlitMaterial(
            "VolkswagenAmarok_" + suffix + " Material", color, intensity);
        source.shadowCastingMode = ShadowCastingMode.Off;
        source.receiveShadows = false;
        source.enabled = false;
        return source;
    }

    private MeshRenderer? CreateLensCover(
        MeshRenderer? source,
        string suffix,
        Color color)
    {
        var sourceMesh = source?.GetComponent<MeshFilter>()?.sharedMesh;
        if (source == null || sourceMesh == null)
            return null;

        var host = new GameObject("VolkswagenAmarok_" + suffix);
        host.transform.SetParent(source.transform, false);
        host.transform.localScale = Vector3.one * 1.0015f;
        host.layer = source.gameObject.layer;
        host.AddComponent<MeshFilter>().sharedMesh = sourceMesh;

        var shader = Shader.Find("HDRP/Lit") ??
                     Shader.Find("High Definition Render Pipeline/Lit") ??
                     throw new InvalidOperationException("HDRP/Lit is unavailable for Amarok rear-lens cover.");
        var material = new Material(shader) { name = host.name + " Material" };
        SetColor(material, "_BaseColor", color);
        SetColor(material, "_Color", color);
        SetFloat(material, "_Metallic", 0f);
        SetFloat(material, "_Smoothness", .72f);
        SetFloat(material, "_SurfaceType", 0f);
        SetFloat(material, "_ZWrite", 1f);

        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.enabled = true;
        generatedObjects.Add(host);
        generatedMaterials.Add(material);
        return renderer;
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
        clone.name = "VolkswagenAmarok_" + suffix;
        clone.transform.localPosition = new Vector3(localX, 0.63f, 2.05f);
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
                var center = vehicleRoot.InverseTransformPoint(source.transform.TransformPoint(
                    (vertices[a] + vertices[b] + vertices[c]) / 3f));
                if (!includeTriangleCenter(center))
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
        return CreateOverlayFromTriangles(source, sourceMesh, triangles, suffix, color, intensity, scale);
    }

    private MeshRenderer? CreateConnectedOverlay(
        MeshRenderer? source,
        Func<ConnectedComponent, Bounds, bool> includeComponent,
        string suffix,
        Color color,
        float intensity,
        float scale)
    {
        if (source == null || vehicleRoot == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
            return null;

        var sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
        var vertices = sourceMesh.vertices;
        var parent = new int[vertices.Length];
        for (var index = 0; index < parent.Length; index++)
            parent[index] = index;
        var allTriangles = new List<int>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var sourceTriangles = sourceMesh.GetTriangles(subMesh);
            allTriangles.AddRange(sourceTriangles);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                Union(parent, sourceTriangles[index], sourceTriangles[index + 1]);
                Union(parent, sourceTriangles[index + 1], sourceTriangles[index + 2]);
            }
        }

        var total = new Bounds();
        var totalInitialized = false;
        var positions = new Vector3[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            positions[index] = vehicleRoot.InverseTransformPoint(
                source.transform.TransformPoint(vertices[index]));
            if (!totalInitialized)
            {
                total = new Bounds(positions[index], Vector3.zero);
                totalInitialized = true;
            }
            else
            {
                total.Encapsulate(positions[index]);
            }
        }

        var components = new Dictionary<int, ConnectedComponent>();
        for (var index = 0; index + 2 < allTriangles.Count; index += 3)
        {
            var first = allTriangles[index];
            var root = Find(parent, first);
            if (!components.TryGetValue(root, out var component))
            {
                component = new ConnectedComponent();
                components.Add(root, component);
            }
            component.AddTriangle(
                first,
                allTriangles[index + 1],
                allTriangles[index + 2],
                positions);
        }

        var triangles = new List<int>();
        foreach (var component in components.Values)
            if (includeComponent(component, total))
                triangles.AddRange(component.Triangles);
        if (triangles.Count == 0)
            return null;

        return CreateOverlayFromTriangles(source, sourceMesh, triangles, suffix, color, intensity, scale);
    }

    private MeshRenderer CreateOverlayFromTriangles(
        MeshRenderer source,
        Mesh sourceMesh,
        List<int> triangles,
        string suffix,
        Color color,
        float intensity,
        float scale)
    {
        var vertices = sourceMesh.vertices;
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

    private static bool IsRearIndicatorComponent(
        ConnectedComponent component,
        Bounds total,
        bool left)
    {
        var center = component.Bounds.center;
        if ((left && center.x >= 0f) || (!left && center.x <= 0f))
            return false;
        // brakelight_1 contains several layered components. The visible amber
        // L is one of the broad, full-height/depth layers; the thin shallow
        // components are internal lens sheets and the narrow outer component
        // is only the reflector.
        return component.Bounds.size.x >= total.size.x * 0.14f &&
               component.Bounds.size.y >= total.size.y * 0.65f &&
               component.Bounds.size.z >= total.size.z * 0.55f;
    }

    private static bool IsRearBrakeSegment(ConnectedComponent component, Bounds total)
    {
        var maximumLateral = Mathf.Max(Mathf.Abs(total.min.x), Mathf.Abs(total.max.x));
        var lateral = Mathf.Abs(component.Bounds.center.x);
        return lateral < maximumLateral * 0.90f &&
               component.Bounds.size.x >= total.size.x * 0.025f &&
               component.Bounds.size.x <= total.size.x * 0.06f &&
               component.Bounds.size.y <= total.size.y * 0.22f &&
               component.Bounds.size.z <= total.size.z * 0.25f;
    }

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }
        return index;
    }

    private static void Union(int[] parent, int first, int second)
    {
        var firstRoot = Find(parent, first);
        var secondRoot = Find(parent, second);
        if (firstRoot != secondRoot)
            parent[secondRoot] = firstRoot;
    }

    private float GetLateralSplit(MeshRenderer? source, float outerFraction, float fallback)
    {
        if (source == null || vehicleRoot == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
            return fallback;
        var vertices = source.GetComponent<MeshFilter>().sharedMesh.vertices;
        if (vertices.Length == 0)
            return fallback;
        var minimum = float.PositiveInfinity;
        var maximum = 0f;
        foreach (var vertex in vertices)
        {
            var lateral = Mathf.Abs(vehicleRoot.InverseTransformPoint(
                source.transform.TransformPoint(vertex)).x);
            minimum = Mathf.Min(minimum, lateral);
            maximum = Mathf.Max(maximum, lateral);
        }
        return maximum > minimum
            ? Mathf.Lerp(minimum, maximum, Mathf.Clamp01(1f - outerFraction))
            : fallback;
    }

    private float GetCentralGapHalfWidth(MeshRenderer? source, float gapFraction, float fallback)
    {
        if (source == null || vehicleRoot == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
            return fallback;
        var vertices = source.GetComponent<MeshFilter>().sharedMesh.vertices;
        if (vertices.Length == 0)
            return fallback;
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        foreach (var vertex in vertices)
        {
            var lateral = vehicleRoot.InverseTransformPoint(
                source.transform.TransformPoint(vertex)).x;
            minimum = Mathf.Min(minimum, lateral);
            maximum = Mathf.Max(maximum, lateral);
        }
        return maximum > minimum
            ? (maximum - minimum) * Mathf.Clamp01(gapFraction) * 0.5f
            : fallback;
    }

    private void OffsetOverlayTowardVehicleEnd(MeshRenderer? overlay, bool front)
    {
        if (overlay == null || vehicleRoot == null)
            return;
        overlay.transform.position += (front ? vehicleRoot.forward : -vehicleRoot.forward) *
                                      0.018f;
    }

    private MeshRenderer CreateOverlayObject(MeshRenderer source, Mesh mesh, string suffix,
        Color color, float intensity, float scale)
    {
        var host = new GameObject("VolkswagenAmarok_" + suffix);
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

        SetEnabled(daylightOverlay, (controlled || npc) && !leftBlinker);
        SetEnabled(daylightOverlayRight, (controlled || npc) && !rightBlinker);
        SetEnabled(headlampOverlay, lightsOn);
        SetEnabled(rearTailLensCover, false);
        SetEnabled(rearBrakeLensCover, false);
        SetEnabled(rearTailOverlay, lightsOn && !braking);
        SetEnabled(rearBrakeOverlay, braking);
        SetEnabled(thirdBrakeOverlay, braking);
        SetEnabled(reverseOverlay, reversing);
        SetEnabled(leftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(rearLeftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rearRightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(sideLeftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(sideRightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(leftBeam, controlled && lightsOn);
        SetEnabled(rightBeam, controlled && lightsOn);
        if (templateBeam != null)
            templateBeam.enabled = false;

        var state = (controlled ? 1 : 0) | (lightsOn ? 2 : 0) | (braking ? 4 : 0) |
                    (leftBlinker ? 8 : 0) | (rightBlinker ? 16 : 0) | (reversing ? 32 : 0);
        if (state == lastState)
            return;
        lastState = state;
        LogInfo($"state playerControlled={controlled} lights={lightsOn} braking={braking} " +
                $"reverse={reversing} leftBlinker={leftBlinker} rightBlinker={rightBlinker}.");
    }

    private bool ReadTrafficBool(FieldInfo? field) =>
        trafficLights != null && field?.GetValue(trafficLights) is bool value && value;

    private int CountLampOverlays() =>
        (daylightOverlay != null ? 1 : 0) + (daylightOverlayRight != null ? 1 : 0) +
        (headlampOverlay != null ? 1 : 0) +
        (rearTailOverlay != null ? 1 : 0) +
        (rearBrakeOverlay != null ? 1 : 0) +
        (thirdBrakeOverlay != null ? 1 : 0) +
        (reverseOverlay != null ? 1 : 0);

    private int CountBlinkerOverlays() =>
        (leftBlinkerOverlay != null ? 1 : 0) + (rightBlinkerOverlay != null ? 1 : 0) +
        (sideLeftBlinkerOverlay != null ? 1 : 0) + (sideRightBlinkerOverlay != null ? 1 : 0) +
        (rearLeftBlinkerOverlay != null ? 1 : 0) + (rearRightBlinkerOverlay != null ? 1 : 0);

    private static MeshRenderer? FindRenderer(
        IEnumerable<MeshRenderer> renderers,
        string hierarchyMarker,
        string materialMarker)
    {
        foreach (var renderer in renderers)
        {
            if (renderer == null || !HasAncestor(renderer.transform, hierarchyMarker))
                continue;
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.name.IndexOf(
                        materialMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return renderer;
        }
        return null;
    }

    private static MeshRenderer? FindRendererByHierarchy(
        IEnumerable<MeshRenderer> renderers,
        string hierarchyMarker)
    {
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;
            if (HasAncestor(renderer.transform, hierarchyMarker) ||
                renderer.name.IndexOf(hierarchyMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                return renderer;
            var meshName = renderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? string.Empty;
            if (meshName.IndexOf(hierarchyMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                return renderer;
        }
        return null;
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
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
        if (trafficLights != null && !VolkswagenAmarokDiagnostics.NpcLightingDebugEnabled)
            return;
        VolkswagenAmarokDiagnostics.Info(
            context,
            $"VolkswagenAmarok lighting vehicle='{gameObject.name}' " +
            $"instance={GetInstanceID()}: {message}");
    }

    private void LogWarning(string message) =>
        context?.Logger.Warn($"VolkswagenAmarok lighting vehicle='{gameObject.name}' " +
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

    private sealed class ConnectedComponent
    {
        internal readonly List<int> Triangles = new List<int>();
        internal Bounds Bounds;
        private bool boundsInitialized;

        internal void AddTriangle(int first, int second, int third, Vector3[] positions)
        {
            Triangles.Add(first);
            Triangles.Add(second);
            Triangles.Add(third);
            Encapsulate(positions[first]);
            Encapsulate(positions[second]);
            Encapsulate(positions[third]);
        }

        private void Encapsulate(Vector3 position)
        {
            if (!boundsInitialized)
            {
                Bounds = new Bounds(position, Vector3.zero);
                boundsInitialized = true;
            }
            else
            {
                Bounds.Encapsulate(position);
            }
        }
    }
}

