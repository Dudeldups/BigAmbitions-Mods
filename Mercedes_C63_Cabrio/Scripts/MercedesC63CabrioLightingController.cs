#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;
using UnityEngine.Rendering;

internal sealed class MercedesC63CabrioLightingController : MonoBehaviour
{
    private const float BlinkerHalfPeriod = 0.42f;
    private static readonly BindingFlags InstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly PropertyInfo? PlayerLights = typeof(VehicleController).GetProperty("ShouldLightsBeOn", InstanceMembers);
    private static readonly FieldInfo? PlayerLeftBlinker = typeof(Vehicles.Components.VehicleBlinker).GetField("_isLeftBlinkerOn", InstanceMembers);
    private static readonly FieldInfo? PlayerRightBlinker = typeof(Vehicles.Components.VehicleBlinker).GetField("_isRightBlinkerOn", InstanceMembers);
    private static readonly FieldInfo? TrafficMain = typeof(VehicleLightsComponent).GetField("_main", InstanceMembers);
    private static readonly FieldInfo? TrafficBrake = typeof(VehicleLightsComponent).GetField("_brake", InstanceMembers);
    private static readonly FieldInfo? TrafficReverse = typeof(VehicleLightsComponent).GetField("_reverse", InstanceMembers);
    private static readonly FieldInfo? TrafficLeftBlinker = typeof(VehicleLightsComponent).GetField("_leftBlinker", InstanceMembers);
    private static readonly FieldInfo? TrafficRightBlinker = typeof(VehicleLightsComponent).GetField("_rightBlinker", InstanceMembers);
    private static readonly FieldInfo? TrafficBlinkerPhase = typeof(VehicleLightsComponent).GetField("_lastBlinkerState", InstanceMembers);

    private readonly List<GameObject> generatedObjects = new List<GameObject>();
    private readonly List<Material> generatedMaterials = new List<Material>();
    // Immutable partitions are shared by identical authored rigs. Damage makes
    // its own writable clones, while enabled/light state belongs to each renderer.
    private static readonly Dictionary<(Mesh, Matrix4x4, string), LampMeshLease> lampMeshCache =
        new Dictionary<(Mesh, Matrix4x4, string), LampMeshLease>();
    private readonly List<LampMeshLease> lampMeshLeases = new List<LampMeshLease>(12);
    private int partitionBuilds;
    private sealed class LampMeshLease
    {
        internal readonly (Mesh, Matrix4x4, string) Key;
        internal readonly Mesh Mesh;
        internal int Owners;
        internal LampMeshLease((Mesh, Matrix4x4, string) key, Mesh mesh) { Key = key; Mesh = mesh; }
    }
    private VehicleController? vehicle;
    private VehicleLightsComponent? trafficLights;
    private Transform? vehicleRoot;
    private ModContext? context;
    private NWH.VehiclePhysics2.Brakes? brakes;
    private object? blinkers;
    private NWH.VehiclePhysics2.Powertrain.TransmissionComponent? transmission;
    private Light? templateBeam;
    private Light? leftBeam;
    private Light? rightBeam;
    private MeshRenderer? daylightOverlay;
    private MeshRenderer? daylightOverlayRight;
    private MeshRenderer? headlampOverlay;
    private MeshRenderer? headlampOverlayRight;
    private MeshRenderer? rearRunningOverlay;
    private MeshRenderer? rearBrakeOverlay;
    private MeshRenderer? thirdBrakeOverlay;
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
        var tracePerformance = MercedesC63CabrioDiagnostics.PerformanceEnabled;
        var started = tracePerformance ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        // This GLB combines front and rear lamp internals in phong14SG1.
        // SetRT contains the center brake lamp, not a front lamp.
        var emitters = FindRenderer(renderers, "taillights", "phong14SG1");
        var centerBrake = FindRendererByHierarchy(renderers, "M_LBucket_SetRT_Max");
        var white = new Color(0.85f, 0.94f, 1f, 1f);
        var red = new Color(1f, 0.008f, 0.001f, 1f);
        var amber = new Color(1f, 0.43f, 0.015f, 1f);
        // Bounds are measured in normalized Mercedes vehicle coordinates.
        Func<Bounds, bool> row = b => b.center.z > 1.85f && b.center.y > 0.61f && b.size.x < 0.05f;
        Func<Bounds, bool> projector = b => b.center.z > 2f && b.center.y < 0.62f && b.size.x < 0.09f && b.size.y > 0.06f;
        Func<Bounds, bool> outerUpper = b => b.center.z < -1.8f && Mathf.Abs(b.center.x) > 0.55f &&
            b.center.y > 0.81f && b.center.y < 0.82f && b.size.y > 0.02f;
        Func<Bounds, bool> outerLower = b => b.center.z < -1.8f && Mathf.Abs(b.center.x) > 0.55f &&
            b.center.y > 0.748f && b.center.y < 0.754f && b.size.x < 0.24f;
        Func<Bounds, bool> innerUpper = b => b.center.z < -2.1f && Mathf.Abs(b.center.x) < 0.52f &&
            Mathf.Abs(b.center.x) > 0.4f && b.center.y > 0.81f && b.size.y < 0.04f;
        // Keep each colored insert separate from its surrounding light guide.
        Func<Bounds, bool> innerLower = b => b.center.z < -2.17f && Mathf.Abs(b.center.x) > .4f &&
            Mathf.Abs(b.center.x) < .5f && b.center.y > .742f && b.center.y < .750f &&
            b.size.y > .028f && b.size.y < .032f;
        Func<Bounds, bool> runningOutline = b =>
        {
            var x = Mathf.Abs(b.center.x);
            var y = b.center.y;
            if (b.center.z >= -1.8f) return false;
            // The trunk-lid guides are connected rings around each insert.
            if (x > .4f && x < .52f && b.center.z < -2.1f)
                return b.size.y > .045f && b.size.y < .055f &&
                    ((y > .81f && y < .82f) || (y > .742f && y < .749f));
            // On the fender the guides are separate strips along each edge.
            return x > .59f && x < .68f &&
                ((y > .828f && y < .837f && b.size.y < .018f) ||
                 (y > .795f && y < .805f && b.size.y < .024f) ||
                 (y > .772f && y < .781f && b.size.y < .030f) ||
                 (y > .734f && y < .742f && b.size.y > .035f && b.size.y < .045f));
        };
        daylightOverlay = Part(emitters, b => row(b) && b.center.x < 0, "DaytimeRunningLightsLeft", white, 4.8f);
        daylightOverlayRight = Part(emitters, b => row(b) && b.center.x > 0, "DaytimeRunningLightsRight", white, 4.8f);
        headlampOverlay = Part(emitters, b => projector(b) && b.center.x < 0, "HeadlampProjectorLeft", white, 6.4f);
        headlampOverlayRight = Part(emitters, b => projector(b) && b.center.x > 0, "HeadlampProjectorRight", white, 6.4f);
        rearRunningOverlay = Part(emitters, runningOutline, "RearRunningOutline", red, 2.8f);
        rearBrakeOverlay = Part(emitters, b => outerUpper(b) || innerUpper(b), "RearBrakeSignature", red, 4.5f);
        thirdBrakeOverlay = Part(centerBrake, b => b.size.z < 0.035f, "ThirdBrakeLight", red, 4.5f);
        reverseOverlay = Part(emitters, innerLower, "ReverseLight", white, 4.8f);
        leftBlinkerOverlay = Part(emitters, b => row(b) && b.center.x < 0, "FrontLeftIndicator", amber, 5.4f);
        rightBlinkerOverlay = Part(emitters, b => row(b) && b.center.x > 0, "FrontRightIndicator", amber, 5.4f);
        rearLeftBlinkerOverlay = Part(emitters, b => outerLower(b) && b.center.x < 0, "RearLeftIndicator", amber, 5.4f);
        rearRightBlinkerOverlay = Part(emitters, b => outerLower(b) && b.center.x > 0, "RearRightIndicator", amber, 5.4f);
        var beamCount = trafficLights == null ? ConfigureHeadlightBeams() : 1;

        initialized = true;
        if (tracePerformance)
        {
            var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d /
                          System.Diagnostics.Stopwatch.Frequency;
            MercedesC63CabrioDiagnostics.PerformanceInfo(context,
                $"MercedesC63Cabrio lamps instance={GetInstanceID()}: partitionsBuilt={partitionBuilds}, " +
                $"partitionsReused={lampMeshLeases.Count - partitionBuilds}, elapsedMs={elapsed:0.000}.");
        }
        LogInfo($"initialized C63 lamps: beams={beamCount}/2, " +
                $"lampOverlays={CountLampOverlays()}/8 blinkerOverlays={CountBlinkerOverlays()}/4, " +
                $"rearBrakeSignatureVertices={rearBrakeOverlay?.GetComponent<MeshFilter>().sharedMesh.vertexCount ?? 0}.");
        if (CountLampOverlays() != 8 || (trafficLights == null && beamCount != 2) || CountBlinkerOverlays() != 4)
            LogWarning("C63 lighting setup is incomplete; inspect the GLB lamp-node diagnostics.");
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

            if (component.GetType().FullName == "Vehicles.Components.VehicleBlinker")
                blinkers = component;
            if (component is NWH.VehiclePhysics2.VehicleController physics)
            {
                brakes = physics.brakes;
                transmission = physics.powertrain.transmission;
            }
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
        clone.name = "MercedesC63Cabrio_" + suffix;
        clone.transform.localPosition = MercedesC63CabrioTuning.BodyPoint(new Vector3(localX, 0.63f, 2.05f));
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

    private MeshRenderer? Part(MeshRenderer? source, Func<Bounds, bool> include,
        string suffix, Color color, float intensity)
    {
        if (source == null || vehicleRoot == null || source.GetComponent<MeshFilter>()?.sharedMesh == null)
            throw new InvalidOperationException($"C63 lamp source missing for {suffix}.");
        var original = source.GetComponent<MeshFilter>().sharedMesh;
        // Compose local transforms so world movement cannot change the cache key.
        var matrix = Matrix4x4.identity;
        for (var current = source.transform; current != vehicleRoot; current = current.parent)
        {
            if (current == null) throw new InvalidOperationException("C63 lamp source is outside its vehicle.");
            matrix = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * matrix;
        }
        var key = (original, matrix, suffix);
        if (!lampMeshCache.TryGetValue(key, out var lease) || lease.Mesh == null)
        {
            var selectionMatrix = MercedesC63CabrioTuning.BodyStance.inverse * matrix;
            var triangles = MercedesC63CabrioMeshParts.Select(original, selectionMatrix,
                b => include(MercedesC63CabrioMeshParts.ToLegacyUnposedBodyBounds(b)));
            if (triangles.Count == 0) throw new InvalidOperationException($"C63 lamp partition is empty: {suffix}.");
            var partition = MercedesC63CabrioMeshParts.Extract(original, triangles, "C63_" + suffix);
            // Offset 0.4 mm in vehicle space, independent of world position.
            var vertices = partition.vertices; var normals = partition.normals;
            var normalMatrix = matrix.inverse.transpose;
            var inverse = matrix.inverse;
            for (var i = 0; i < vertices.Length && i < normals.Length; i++)
            {
                var normal = normalMatrix.MultiplyVector(normals[i]).normalized;
                vertices[i] += inverse.MultiplyVector(normal * .0004f);
            }
            partition.vertices = vertices; partition.RecalculateBounds();
            lease = new LampMeshLease(key, partition); lampMeshCache[key] = lease;
            partitionBuilds++;
        }
        lease.Owners++; lampMeshLeases.Add(lease);
        var mesh = lease.Mesh;
        var host = new GameObject("MercedesC63Cabrio_" + suffix);
        host.transform.SetParent(source.transform, false); host.layer = source.gameObject.layer;
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CreateUnlitMaterial(host.name + " Material", color, intensity);
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.enabled = false; generatedObjects.Add(host); return renderer;
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
            controlled && PlayerLights?.GetValue(vehicle) is bool on && on;
        var braking = npc ? ReadTrafficBool(TrafficBrake) : controlled &&
            brakes != null && brakes.IsBraking;
        var leftBlinker = npc ? ReadTrafficBool(TrafficLeftBlinker) :
            controlled && PlayerLeftBlinker?.GetValue(blinkers) is bool left && left;
        var rightBlinker = npc ? ReadTrafficBool(TrafficRightBlinker) :
            controlled && PlayerRightBlinker?.GetValue(blinkers) is bool right && right;
        var reversing = npc ? ReadTrafficBool(TrafficReverse) :
            controlled && transmission != null && transmission.Gear < 0;
        var blinking = leftBlinker || rightBlinker;
        if (!npc && blinking && !wasBlinking)
            blinkerPhaseStartedAt = Time.unscaledTime;
        var flash = npc ? blinking && TrafficBlinkerPhase?.GetValue(trafficLights) is int phase && phase != 0 :
            blinking && Mathf.Repeat(Time.unscaledTime - blinkerPhaseStartedAt,
                BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        wasBlinking = blinking;

        SetEnabled(daylightOverlay, (npc || controlled) && !leftBlinker);
        SetEnabled(daylightOverlayRight, (npc || controlled) && !rightBlinker);
        SetEnabled(headlampOverlay, lightsOn);
        SetEnabled(headlampOverlayRight, lightsOn);
        ApplyRearLampState(lightsOn, braking, reversing, leftBlinker && flash, rightBlinker && flash);
        SetEnabled(leftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rightBlinkerOverlay, rightBlinker && flash);
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

    private void ApplyRearLampState(bool running, bool braking, bool reversing, bool leftFlash, bool rightFlash)
    {
        SetEnabled(rearRunningOverlay, running);
        SetEnabled(rearBrakeOverlay, braking);
        SetEnabled(thirdBrakeOverlay, braking);
        SetEnabled(reverseOverlay, reversing);
        SetEnabled(rearLeftBlinkerOverlay, leftFlash);
        SetEnabled(rearRightBlinkerOverlay, rightFlash);
    }

    private bool ReadTrafficBool(FieldInfo? field) =>
        trafficLights != null && field?.GetValue(trafficLights) is bool value && value;

    private int CountLampOverlays() =>
        (daylightOverlay != null ? 1 : 0) + (daylightOverlayRight != null ? 1 : 0) +
        (headlampOverlay != null ? 1 : 0) + (headlampOverlayRight != null ? 1 : 0) +
        (rearBrakeOverlay != null ? 1 : 0) +
        (rearRunningOverlay != null ? 1 : 0) +
        (thirdBrakeOverlay != null ? 1 : 0) +
        (reverseOverlay != null ? 1 : 0);

    private int CountBlinkerOverlays() =>
        (leftBlinkerOverlay != null ? 1 : 0) + (rightBlinkerOverlay != null ? 1 : 0) +
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
            if (renderer != null && HasAncestor(renderer.transform, hierarchyMarker))
                return renderer;
        return null;
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
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
        if (trafficLights != null && !MercedesC63CabrioDiagnostics.NpcLightingDebugEnabled)
            return;
        MercedesC63CabrioDiagnostics.Info(
            context,
            $"MercedesC63Cabrio lighting vehicle='{gameObject.name}' " +
            $"instance={GetInstanceID()}: {message}");
    }

    private void LogWarning(string message) =>
        context?.Logger.Warn($"MercedesC63Cabrio lighting vehicle='{gameObject.name}' " +
                             $"instance={GetInstanceID()}: {message}");

    private static void Release(UnityEngine.Object value)
    { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }

    private void OnDestroy()
    {
        foreach (var generatedObject in generatedObjects)
            if (generatedObject != null) Release(generatedObject);
        foreach (var generatedMaterial in generatedMaterials)
            if (generatedMaterial != null) Release(generatedMaterial);
        foreach (var lease in lampMeshLeases)
        {
            if (--lease.Owners != 0) continue;
            if (lampMeshCache.TryGetValue(lease.Key, out var current) && ReferenceEquals(current, lease))
                lampMeshCache.Remove(lease.Key);
            if (lease.Mesh != null) Release(lease.Mesh);
        }
        lampMeshLeases.Clear();
    }

}
