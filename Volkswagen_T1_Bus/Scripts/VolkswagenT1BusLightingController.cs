#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class VolkswagenT1BusLightingController : MonoBehaviour
{
    private const float BlinkerHalfPeriod = 0.42f;
    private const float LampSurfaceOffsetMeters = 0.006f;
    private static readonly BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? TrafficMain = typeof(VehicleLightsComponent).GetField("_main", InstanceMembers);
    private static readonly FieldInfo? TrafficBrake = typeof(VehicleLightsComponent).GetField("_brake", InstanceMembers);
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
    private Light? templateBeam;
    private Light? leftBeam;
    private Light? rightBeam;
    private MeshRenderer? headlampOverlay;
    private MeshRenderer? rearTailLeftOverlay;
    private MeshRenderer? rearTailRightOverlay;
    private Material? rearRunningMaterial;
    private Material? rearBrakeMaterial;
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
            TrafficLeftBlinker == null || TrafficRightBlinker == null || TrafficBlinkerPhase == null)
        {
            LogWarning("NPC light state source is unavailable.");
            return;
        }
        InitializeVisuals(gameObject);
    }

    private void InitializeVisuals(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        var headlamp = FindRendererByHierarchy(renderers, "BHeadlights");
        var tail = FindRendererByHierarchy(renderers, "1RearDrivingLights");
        var brake = FindRendererByHierarchy(renderers, "1BrakeLights");
        headlampOverlay = PrepareSourceOverlay(headlamp, "Headlights", new Color(.86f, .93f, 1f, 1f), 5.8f);
        // One source surface per side serves as tail, brake, and signal lamp.
        // Swapping only its material avoids coincident meshes fighting when
        // braking or flashing changes the lamp intensity.
        var rearLamp = brake ?? tail;
        PrepareSideSourceOverlays(
            rearLamp,
            "RearLamp",
            new Color(1f, .008f, .002f, 1f),
            2.6f,
            out rearTailLeftOverlay,
            out rearTailRightOverlay);
        rearRunningMaterial = rearTailLeftOverlay?.sharedMaterial ??
                              rearTailRightOverlay?.sharedMaterial;
        if (rearRunningMaterial != null)
            rearBrakeMaterial = CreateUnlitMaterial(
                "VolkswagenT1Bus_RearBrake Material",
                new Color(1f, .008f, .002f, 1f),
                4.6f);
        if (tail != null && tail != rearLamp)
            tail.enabled = false;
        if (brake != null && brake != rearLamp)
            brake.enabled = false;

        LogInfo($"original lamp sources head={headlamp?.name ?? "<missing>"} " +
                $"rearRunning={tail?.name ?? "<missing>"} " +
                $"rearBrake={brake?.name ?? "<missing>"}; one shared original red rear-lamp mesh " +
                "per side; frontSignals=omitted (no source lamp); reverseLens=omitted.");
        if (trafficLights == null)
            ConfigureHeadlightBeams();
        initialized = true;
        ApplyState();
    }

    private void PrepareSideSourceOverlays(
        MeshRenderer? source,
        string suffix,
        Color color,
        float intensity,
        out MeshRenderer? left,
        out MeshRenderer? right)
    {
        left = null;
        right = null;
        var sourceFilter = source != null ? source.GetComponent<MeshFilter>() : null;
        var sourceMesh = sourceFilter != null ? sourceFilter.sharedMesh : null;
        if (source == null || sourceFilter == null || sourceMesh == null || vehicleRoot == null)
        {
            LogWarning($"original rear lamp source '{suffix}' is missing.");
            return;
        }

        var vertexPositions = sourceMesh.vertices;
        var localToVehicle = vehicleRoot.worldToLocalMatrix * sourceFilter.transform.localToWorldMatrix;
        var leftTriangles = new List<int>[sourceMesh.subMeshCount];
        var rightTriangles = new List<int>[sourceMesh.subMeshCount];
        for (var submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
        {
            leftTriangles[submesh] = new List<int>();
            rightTriangles[submesh] = new List<int>();
            if (sourceMesh.GetTopology(submesh) != MeshTopology.Triangles)
            {
                LogWarning($"original rear lamp source '{suffix}' has non-triangle geometry.");
                return;
            }

            var indices = sourceMesh.GetTriangles(submesh);
            for (var index = 0; index + 2 < indices.Length; index += 3)
            {
                var a = indices[index];
                var b = indices[index + 1];
                var c = indices[index + 2];
                var center = (vertexPositions[a] + vertexPositions[b] + vertexPositions[c]) / 3f;
                var vehicleX = localToVehicle.MultiplyPoint3x4(center).x;
                var destination = vehicleX < 0f ? leftTriangles[submesh] : rightTriangles[submesh];
                destination.Add(a);
                destination.Add(b);
                destination.Add(c);
            }
        }

        var leftCount = CountTriangles(leftTriangles);
        var rightCount = CountTriangles(rightTriangles);
        if (leftCount == 0 || rightCount == 0)
        {
            LogWarning($"original rear lamp source '{suffix}' could not be separated by side " +
                       $"(leftTriangles={leftCount}, rightTriangles={rightCount}).");
            return;
        }

        var material = CreateUnlitMaterial("VolkswagenT1Bus_" + suffix + " Material", color, intensity);
        var materials = new Material[sourceMesh.subMeshCount];
        for (var index = 0; index < materials.Length; index++)
            materials[index] = material;

        var leftMesh = CreateSideMesh(sourceMesh, leftTriangles,
            "VolkswagenT1Bus_" + suffix + "Left Mesh");
        var rightMesh = CreateSideMesh(sourceMesh, rightTriangles,
            "VolkswagenT1Bus_" + suffix + "Right Mesh");
        left = CreateSideOverlay(source, leftMesh, materials, suffix + "Left");
        right = CreateSideOverlay(source, rightMesh, materials, suffix + "Right");
        source.enabled = false;
        LogInfo($"original rear lamp source '{suffix}' split into left={leftCount} and " +
                $"right={rightCount} source triangles; no lamp geometry was added.");
    }

    private Mesh CreateSideMesh(Mesh source, List<int>[] triangles, string name)
    {
        var mesh = Instantiate(source);
        mesh.name = name;
        for (var submesh = 0; submesh < triangles.Length; submesh++)
            mesh.SetTriangles(triangles[submesh], submesh, false);
        OffsetSurfaceAlongNormals(mesh);
        mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        return mesh;
    }

    private MeshRenderer CreateSideOverlay(
        MeshRenderer source,
        Mesh mesh,
        Material[] materials,
        string suffix)
    {
        var overlay = new GameObject("VolkswagenT1Bus_" + suffix);
        overlay.layer = source.gameObject.layer;
        overlay.transform.SetParent(source.transform, false);
        overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = overlay.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        generatedObjects.Add(overlay);
        return renderer;
    }

    private static int CountTriangles(List<int>[] submeshTriangles)
    {
        var count = 0;
        foreach (var triangles in submeshTriangles)
            count += triangles.Count / 3;
        return count;
    }

    private MeshRenderer? PrepareSourceOverlay(
        MeshRenderer? source,
        string suffix,
        Color color,
        float intensity)
    {
        if (source == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
        {
            LogWarning($"original lamp source '{suffix}' is missing.");
            return null;
        }

        var sourceFilter = source.GetComponent<MeshFilter>();
        var offsetMesh = Instantiate(sourceFilter.sharedMesh);
        offsetMesh.name = "VolkswagenT1Bus_Headlights_ZFightOffset";
        OffsetSurfaceAlongNormals(offsetMesh);
        offsetMesh.RecalculateBounds();
        generatedMeshes.Add(offsetMesh);
        sourceFilter.sharedMesh = offsetMesh;
        source.sharedMaterial = CreateUnlitMaterial(
            "VolkswagenT1Bus_" + suffix + " Material", color, intensity);
        source.shadowCastingMode = ShadowCastingMode.Off;
        source.receiveShadows = false;
        source.enabled = false;
        return source;
    }

    private static void OffsetSurfaceAlongNormals(Mesh mesh)
    {
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        if (normals == null || normals.Length != vertices.Length)
        {
            mesh.RecalculateNormals();
            normals = mesh.normals;
        }

        for (var index = 0; index < vertices.Length; index++)
        {
            var normal = normals[index];
            if (normal.sqrMagnitude > 0.0001f)
                vertices[index] += normal.normalized * LampSurfaceOffsetMeters;
        }

        mesh.vertices = vertices;
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
        clone.name = "VolkswagenT1Bus_" + suffix;
        clone.transform.localPosition = new Vector3(localX, 0.88f, 1.94f);
        clone.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);
        var light = clone.GetComponent<Light>();
        if (light == null)
        {
            Destroy(clone);
            return null;
        }
        light.enabled = false;
        light.cookie = null;
        light.range = 35f;
        light.spotAngle = 52f;
        light.innerSpotAngle = 28f;
        light.colorTemperature = 5600f;
        light.useColorTemperature = true;
        generatedObjects.Add(clone);
        return light;
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
        var blinking = leftBlinker || rightBlinker;
        if (!npc && blinking && !wasBlinking)
            blinkerPhaseStartedAt = Time.unscaledTime;
        var flash = npc
            ? blinking && TrafficBlinkerPhase?.GetValue(trafficLights) is int phase && phase != 0
            : blinking && Mathf.Repeat(Time.unscaledTime - blinkerPhaseStartedAt,
                BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        wasBlinking = blinking;

        // Each rear signal reuses only the matching side's triangles from the
        // original red stop-lamp mesh. The opposite brake lamp stays steady.
        var rearLeftBrakeOn = leftBlinker ? flash : braking;
        var rearRightBrakeOn = rightBlinker ? flash : braking;
        SetEnabled(headlampOverlay, lightsOn);
        SetRearLamp(rearTailLeftOverlay, lightsOn || rearLeftBrakeOn, rearLeftBrakeOn);
        SetRearLamp(rearTailRightOverlay, lightsOn || rearRightBrakeOn, rearRightBrakeOn);
        SetEnabled(leftBeam, controlled && lightsOn);
        SetEnabled(rightBeam, controlled && lightsOn);
        if (templateBeam != null)
            templateBeam.enabled = false;

        var state = (controlled ? 1 : 0) | (lightsOn ? 2 : 0) | (braking ? 4 : 0) |
                    (leftBlinker ? 8 : 0) | (rightBlinker ? 16 : 0);
        if (state == lastState)
            return;
        lastState = state;
        LogInfo($"state playerControlled={controlled} lights={lightsOn} braking={braking} " +
                $"leftBlinker={leftBlinker} rightBlinker={rightBlinker} " +
                $"rearLeftBrake={rearLeftBrakeOn} rearRightBrake={rearRightBrakeOn} " +
                "frontSignals=false.");
    }

    private bool ReadTrafficBool(FieldInfo? field) =>
        trafficLights != null && field?.GetValue(trafficLights) is bool value && value;

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

    private void SetRearLamp(MeshRenderer? renderer, bool enabled, bool braking)
    {
        if (renderer == null)
            return;
        if (!enabled)
        {
            SetEnabled(renderer, false);
            return;
        }

        var material = braking ? rearBrakeMaterial : rearRunningMaterial;
        if (material != null && renderer.sharedMaterial != material)
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
                materials[index] = material;
            renderer.sharedMaterials = materials;
        }
        SetEnabled(renderer, true);
    }

    private static void SetColor(Material material, string name, Color value)
    {
        if (material.HasProperty(name))
            material.SetColor(name, value);
    }

    private static void SetFloat(Material material, string name, float value)
    {
        if (material.HasProperty(name))
            material.SetFloat(name, value);
    }

    private void LogInfo(string message)
    {
        if (trafficLights != null && !VolkswagenT1BusDiagnostics.NpcLightingDebugEnabled)
            return;
        VolkswagenT1BusDiagnostics.LightingInfo(
            context,
            $"VolkswagenT1Bus lighting vehicle='{gameObject.name}' " +
            $"instance={GetInstanceID()}: {message}");
    }

    private void LogWarning(string message) =>
        context?.Logger.Warn($"VolkswagenT1Bus lighting vehicle='{gameObject.name}' " +
                             $"instance={GetInstanceID()}: {message}");

    private void OnDestroy()
    {
        foreach (var generatedObject in generatedObjects)
            if (generatedObject != null)
                Destroy(generatedObject);
        foreach (var generatedMaterial in generatedMaterials)
            if (generatedMaterial != null)
                Destroy(generatedMaterial);
        foreach (var generatedMesh in generatedMeshes)
            if (generatedMesh != null)
                Destroy(generatedMesh);
    }
}
