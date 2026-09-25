#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class BugattiChironLightingController : MonoBehaviour
{
    private const string HeadlampName = "Headlight_Headlights-Lights_0";
    private const string RearStripName = "Tail-light_Tail-light-LIGHT_0";
    private const string ThirdBrakeLightName = "Tail-light_Brake-lights_0";
    private const string FrontLeftBlinkerName = "Headlight_Turning_lights_left_0";
    private const string FrontRightBlinkerName = "Headlight_Turning_lights_right_0";
    private const string SideLeftBlinkerName = "Door-left_Turning_lights_left_0";
    private const string SideRightBlinkerName = "Door-right_Turning_lights_right_0";
    private const float BlinkerHalfPeriod = 0.42f;
    private static readonly BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? TrafficMain = typeof(VehicleLightsComponent).GetField("_main", InstanceMembers);
    private static readonly FieldInfo? TrafficBrake = typeof(VehicleLightsComponent).GetField("_brake", InstanceMembers);
    private static readonly FieldInfo? TrafficReverse = typeof(VehicleLightsComponent).GetField("_reverse", InstanceMembers);
    private static readonly FieldInfo? TrafficLeftBlinker = typeof(VehicleLightsComponent).GetField("_leftBlinker", InstanceMembers);
    private static readonly FieldInfo? TrafficRightBlinker = typeof(VehicleLightsComponent).GetField("_rightBlinker", InstanceMembers);
    private static readonly FieldInfo? TrafficBlinkerPhase = typeof(VehicleLightsComponent).GetField("_lastBlinkerState", InstanceMembers);

    private readonly List<GameObject> generatedObjects = new();
    private readonly List<Material> generatedMaterials = new();
    private readonly List<Mesh> generatedMeshes = new();
    private readonly List<OverlayMeshBinding> deformableOverlayBindings = new();
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
    private MeshRenderer? rearTailOverlay;
    private MeshRenderer? rearBrakeOverlay;
    private MeshRenderer? thirdBrakeOverlay;
    private MeshRenderer? frontLeftBlinkerOverlay;
    private MeshRenderer? frontRightBlinkerOverlay;
    private MeshRenderer? sideLeftBlinkerOverlay;
    private MeshRenderer? sideRightBlinkerOverlay;
    private MeshRenderer? rearLeftBlinkerOverlay;
    private MeshRenderer? rearRightBlinkerOverlay;
    private bool initialized;
    private bool updateFailureReported;
    private bool wasBlinking;
    private float blinkerPhaseStartedAt;
    private int lastNpcLightState = -1;

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
        var headlamp = FindRenderer(renderers, HeadlampName);
        var rearStrip = FindRenderer(renderers, RearStripName);
        var thirdBrake = FindRenderer(renderers, ThirdBrakeLightName);
        var frontLeftBlinker = FindRenderer(renderers, FrontLeftBlinkerName);
        var frontRightBlinker = FindRenderer(renderers, FrontRightBlinkerName);
        var sideLeftBlinker = FindRenderer(renderers, SideLeftBlinkerName);
        var sideRightBlinker = FindRenderer(renderers, SideRightBlinkerName);

        headlampOverlay = CreateOverlay(
            headlamp, "BugattiChiron_HeadlampRectangles", new Color(0.78f, 0.87f, 1f, 1f), 4.5f);
        rearTailOverlay = CreateOverlay(
            rearStrip, "BugattiChiron_RearTailStrip", new Color(0.78f, 0.006f, 0.002f, 1f), 2.7f);
        rearBrakeOverlay = CreateOverlay(
            rearStrip, "BugattiChiron_RearBrakeStrip", new Color(1f, 0.008f, 0.001f, 1f), 4.2f, 1.003f);
        thirdBrakeOverlay = CreateOverlay(
            thirdBrake, "BugattiChiron_ThirdBrakeLight", new Color(1f, 0.008f, 0.001f, 1f), 4.2f);
        var amber = new Color(1f, 0.20f, 0.002f, 1f);
        frontLeftBlinkerOverlay = CreateOverlay(
            frontLeftBlinker, "BugattiChiron_FrontLeftBlinker", amber, 5.2f, 1.004f);
        frontRightBlinkerOverlay = CreateOverlay(
            frontRightBlinker, "BugattiChiron_FrontRightBlinker", amber, 5.2f, 1.004f);
        sideLeftBlinkerOverlay = CreateOverlay(
            sideLeftBlinker, "BugattiChiron_SideLeftBlinker", amber, 4.2f, 1.004f);
        sideRightBlinkerOverlay = CreateOverlay(
            sideRightBlinker, "BugattiChiron_SideRightBlinker", amber, 4.2f, 1.004f);
        rearLeftBlinkerOverlay = CreateFilteredOverlay(
            rearStrip, position => position.x <= -0.38f,
            "BugattiChiron_RearLeftBlinker", amber, 6.0f, 1.006f);
        rearRightBlinkerOverlay = CreateFilteredOverlay(
            rearStrip, position => position.x >= 0.38f,
            "BugattiChiron_RearRightBlinker", amber, 6.0f, 1.006f);
        var beamCount = trafficLights == null ? ConfigureHeadlightBeams() : 1;

        initialized = true;
        if (headlampOverlay == null || rearTailOverlay == null || rearBrakeOverlay == null ||
            thirdBrakeOverlay == null || (trafficLights == null && beamCount != 2) || CountBlinkerOverlays() != 6)
        {
            LogWarning("lighting setup is incomplete; inspect the named renderer and beam diagnostics.");
        }
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
            SynchronizeDeformableOverlays();
            ApplyState();
        }
        catch (Exception ex)
        {
            if (updateFailureReported)
                return;
            updateFailureReported = true;
            LogWarning($"update failed: {ex.GetType().Name}: {ex.Message}");
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
                brakes = component.GetType().GetField("brakes", InstanceMembers)?.GetValue(component);
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
            LogWarning("the inherited 'Spotlights' road-light template is missing.");
            return 0;
        }
        templateBeam.enabled = false;
        leftBeam = CloneBeam(-0.68f, "BugattiChiron_LeftHeadlightBeam");
        rightBeam = CloneBeam(0.68f, "BugattiChiron_RightHeadlightBeam");
        return (leftBeam != null ? 1 : 0) + (rightBeam != null ? 1 : 0);
    }

    private Light? CloneBeam(float localX, string objectName)
    {
        if (templateBeam == null || vehicleRoot == null)
            return null;
        try
        {
            var clone = Instantiate(templateBeam.gameObject, vehicleRoot, false);
            clone.name = objectName;
            clone.transform.localPosition = new Vector3(localX, 0.58f, 2.18f);
            clone.transform.localRotation = Quaternion.Euler(7f, 0f, 0f);
            var light = clone.GetComponent<Light>();
            if (light == null)
            {
                Destroy(clone);
                return null;
            }
            light.enabled = false;
            light.cookie = null;
            light.range = 45f;
            light.spotAngle = 68f;
            light.innerSpotAngle = 42f;
            light.colorTemperature = 5000f;
            light.useColorTemperature = true;
            generatedObjects.Add(clone);
            return light;
        }
        catch (Exception ex)
        {
            LogWarning($"could not create road beam '{objectName}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private MeshRenderer? CreateOverlay(
        MeshRenderer? source,
        string objectName,
        Color color,
        float intensity = 3.5f,
        float scale = 1.0015f)
    {
        if (source == null)
        {
            LogWarning($"overlay '{objectName}' has no source renderer.");
            return null;
        }
        var filter = source.GetComponent<MeshFilter>();
        if (filter?.sharedMesh == null)
        {
            LogWarning($"overlay '{objectName}' source '{source.name}' has no mesh.");
            return null;
        }

        var overlayObject = new GameObject(objectName);
        overlayObject.transform.SetParent(source.transform, false);
        overlayObject.transform.localScale = Vector3.one * scale;
        overlayObject.layer = source.gameObject.layer;
        var overlayFilter = overlayObject.AddComponent<MeshFilter>();
        overlayFilter.sharedMesh = filter.sharedMesh;
        var overlay = overlayObject.AddComponent<MeshRenderer>();
        overlay.sharedMaterial = CreateUnlitMaterial(objectName + " Material", color, intensity);
        overlay.renderingLayerMask = source.renderingLayerMask;
        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.enabled = false;
        deformableOverlayBindings.Add(new OverlayMeshBinding(filter, overlayFilter));
        generatedObjects.Add(overlayObject);
        return overlay;
    }

    private void SynchronizeDeformableOverlays()
    {
        foreach (var binding in deformableOverlayBindings)
        {
            if (binding.Source == null || binding.Overlay == null ||
                binding.Source.sharedMesh == binding.Overlay.sharedMesh)
            {
                continue;
            }

            // Collision deformation replaces MeshFilter.sharedMesh with its
            // writable runtime clone. Point the emissive overlay at that same
            // mesh so lamp geometry cannot remain at the undamaged position.
            binding.Overlay.sharedMesh = binding.Source.sharedMesh;
        }
    }

    private MeshRenderer? CreateFilteredOverlay(
        MeshRenderer? source,
        Func<Vector3, bool> includeTriangleCenter,
        string objectName,
        Color color,
        float intensity,
        float scale)
    {
        if (source == null || vehicleRoot == null)
        {
            LogWarning($"filtered overlay '{objectName}' has no source renderer or vehicle.");
            return null;
        }
        var filter = source.GetComponent<MeshFilter>();
        if (filter?.sharedMesh == null)
        {
            LogWarning($"filtered overlay '{objectName}' source '{source.name}' has no mesh.");
            return null;
        }

        var sourceMesh = filter.sharedMesh;
        var vertices = sourceMesh.vertices;
        var vehiclePositions = new Vector3[vertices.Length];
        for (var index = 0; index < vertices.Length; index++)
        {
            vehiclePositions[index] = vehicleRoot.InverseTransformPoint(
                source.transform.TransformPoint(vertices[index]));
        }

        var triangles = new List<int>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var sourceTriangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                var a = sourceTriangles[index];
                var b = sourceTriangles[index + 1];
                var c = sourceTriangles[index + 2];
                var center = (vehiclePositions[a] + vehiclePositions[b] + vehiclePositions[c]) / 3f;
                if (!includeTriangleCenter(center))
                    continue;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }
        if (triangles.Count == 0)
        {
            LogWarning($"filtered overlay '{objectName}' selected no triangles on '{source.name}'.");
            return null;
        }

        var mesh = new Mesh
        {
            name = sourceMesh.name + "_" + objectName,
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

        var overlayObject = new GameObject(objectName);
        overlayObject.transform.SetParent(source.transform, false);
        overlayObject.transform.localScale = Vector3.one * scale;
        overlayObject.layer = source.gameObject.layer;
        overlayObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var overlay = overlayObject.AddComponent<MeshRenderer>();
        overlay.sharedMaterial = CreateUnlitMaterial(objectName + " Material", color, intensity);
        overlay.renderingLayerMask = source.renderingLayerMask;
        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.enabled = false;
        generatedMeshes.Add(mesh);
        generatedObjects.Add(overlayObject);
        return overlay;
    }

    private Material CreateUnlitMaterial(string materialName, Color color, float intensity)
    {
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Color");
        if (shader == null)
            throw new InvalidOperationException("No compatible unlit shader is available.");

        var material = new Material(shader) { name = materialName };
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
        SetFloat(material, "_CullMode", 0f);
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
        var isBlinking = leftBlinker || rightBlinker;
        if (!npc && isBlinking && !wasBlinking)
            blinkerPhaseStartedAt = Time.unscaledTime;
        var blinkerFlash = npc ? isBlinking &&
            TrafficBlinkerPhase?.GetValue(trafficLights) is int phase && phase != 0 :
            isBlinking && Mathf.Repeat(Time.unscaledTime - blinkerPhaseStartedAt,
                BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        wasBlinking = isBlinking;

        SetEnabled(headlampOverlay, lightsOn);
        SetEnabled(rearTailOverlay, lightsOn && !braking);
        SetEnabled(rearBrakeOverlay, braking);
        SetEnabled(thirdBrakeOverlay, braking);
        SetEnabled(frontLeftBlinkerOverlay, leftBlinker && blinkerFlash);
        SetEnabled(frontRightBlinkerOverlay, rightBlinker && blinkerFlash);
        SetEnabled(sideLeftBlinkerOverlay, leftBlinker && blinkerFlash);
        SetEnabled(sideRightBlinkerOverlay, rightBlinker && blinkerFlash);
        SetEnabled(rearLeftBlinkerOverlay, leftBlinker && blinkerFlash);
        SetEnabled(rearRightBlinkerOverlay, rightBlinker && blinkerFlash);
        SetEnabled(leftBeam, controlled && lightsOn);
        SetEnabled(rightBeam, controlled && lightsOn);
        if (templateBeam != null)
            templateBeam.enabled = false;
        if (npc && BugattiChironDiagnostics.DebugEnabled &&
            BugattiChironDiagnostics.NpcLightingDebugEnabled)
        {
            var state = (lightsOn ? 1 : 0) | (braking ? 2 : 0) |
                (leftBlinker ? 4 : 0) | (rightBlinker ? 8 : 0);
            if (state != lastNpcLightState)
            {
                lastNpcLightState = state;
                context?.Logger.Info($"BugattiChiron NPC lighting instance={GetInstanceID()} " +
                    $"lights={lightsOn} brake={braking} left={leftBlinker} right={rightBlinker}.");
            }
        }
    }

    private bool ReadTrafficBool(FieldInfo? field) =>
        trafficLights != null && field?.GetValue(trafficLights) is bool value && value;

    private int CountLampOverlays() =>
        (headlampOverlay != null ? 1 : 0) + (rearTailOverlay != null ? 1 : 0) +
        (rearBrakeOverlay != null ? 1 : 0) + (thirdBrakeOverlay != null ? 1 : 0);

    private int CountBlinkerOverlays() =>
        (frontLeftBlinkerOverlay != null ? 1 : 0) + (frontRightBlinkerOverlay != null ? 1 : 0) +
        (sideLeftBlinkerOverlay != null ? 1 : 0) + (sideRightBlinkerOverlay != null ? 1 : 0) +
        (rearLeftBlinkerOverlay != null ? 1 : 0) + (rearRightBlinkerOverlay != null ? 1 : 0);

    private static MeshRenderer? FindRenderer(IEnumerable<MeshRenderer> renderers, string name)
    {
        foreach (var renderer in renderers)
            if (renderer != null && string.Equals(renderer.name, name, StringComparison.Ordinal))
                return renderer;
        return null;
    }

    private static bool GetBoolProperty(object? target, string name) =>
        target != null && target.GetType().GetProperty(name, InstanceMembers)?.GetValue(target) is bool value && value;

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

    private static void SetColor(Material material, string name, Color value)
    {
        if (material.HasProperty(name)) material.SetColor(name, value);
    }

    private static void SetFloat(Material material, string name, float value)
    {
        if (material.HasProperty(name)) material.SetFloat(name, value);
    }

    private void LogWarning(string message) =>
        context?.Logger.Warn($"BugattiChiron lighting vehicle='{gameObject.name}' " +
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

    private sealed class OverlayMeshBinding
    {
        public OverlayMeshBinding(MeshFilter source, MeshFilter overlay)
        {
            Source = source;
            Overlay = overlay;
        }

        public MeshFilter Source { get; }
        public MeshFilter Overlay { get; }
    }
}
