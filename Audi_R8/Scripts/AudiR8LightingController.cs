#nullable enable
using System;
using System.Collections.Generic;
using System.Collections;
using BAModAPI;
using BigAmbitions.InputSystem;
using NWH.VehiclePhysics2.Effects;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

internal sealed class AudiR8LightingController : MonoBehaviour
{
    private const float BlinkerHalfPeriod = 0.42f;
    private static readonly System.Reflection.BindingFlags InstanceMembers =
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
        System.Reflection.BindingFlags.NonPublic;

    private readonly List<GameObject> generatedObjects = new List<GameObject>();
    private readonly List<Material> generatedMaterials = new List<Material>();
    private readonly List<Mesh> generatedMeshes = new List<Mesh>();
    private VehicleController? vehicle;
    private Transform? vehicleRoot;
    private ModContext? context;
    private AudiR8LightBindings.Lamp[] lampBindings = Array.Empty<AudiR8LightBindings.Lamp>();
    private object? brakes;
    private object? blinkers;
    private object? transmission;
    private Light? leftBeam;
    private Light? rightBeam;
    private MeshRenderer? permanentDaylightOverlay;
    private MeshRenderer? daylightOverlay;
    private MeshRenderer? daylightOverlayRight;
    private MeshRenderer? headlampOverlay;
    private MeshRenderer? rearTailOverlay;
    private MeshRenderer? rearBrakeOverlay;
    private MeshRenderer? reverseOverlay;
    private MeshRenderer? reverseOverlayRight;
    private MeshRenderer? leftBlinkerOverlay;
    private MeshRenderer? rightBlinkerOverlay;
    private MeshRenderer? rearLeftBlinkerOverlay;
    private MeshRenderer? rearRightBlinkerOverlay;
    private MeshRenderer? mirrorLeftBlinkerOverlay, mirrorRightBlinkerOverlay;
    private bool initialized;
    private bool updateFailureReported;
    private bool wasBlinking;
    private float blinkerPhaseStartedAt;
    private int lastState = -1;
    private bool controlled, lightsOn, braking, reversing, leftBlinker, rightBlinker;
    private InputAction? leftAction, rightAction;
    private readonly List<(VehicleLight owner, LightSource source)> stateBindings = new();

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (initialized && vehicle == controller)
            return;
        vehicle = controller;
        vehicleRoot = controller.transform;
        context = modContext;
        LocateStateSources();
        InitializeVisuals(controller.gameObject);
        SubscribeState();
    }

    private void InitializeVisuals(GameObject root)
    {
        lampBindings = AudiR8LightAssets.Resolve(root, context);
        var source = lampBindings.Length != 0 ? lampBindings[0].source : null;
        if (source == null)
        {
            LogWarning($"authored lamp bindings unavailable, resolvedGroups={lampBindings.Length}; runtime overlays were not created.");
            return;
        }

        var filter = source.GetComponent<MeshFilter>();
        var openLampMesh = AudiR8LampFaces.OpenLensCovers(filter.sharedMesh, out var covers);
        filter.sharedMesh = openLampMesh;
        generatedMeshes.Add(openLampMesh);
        if (covers != 150) LogWarning($"expected 150 opaque cover faces, removed {covers}; check model topology.");

        // Only Blender-authored complete faces illuminate. All imported subset
        // hierarchy transforms are ignored: each mesh is parented to its exact
        // production source renderer with identity local transforms.
        var white = new Color(.80f, .90f, 1f, 1f);
        var red = new Color(1f, .001f, 0f, 1f);
        var amber = new Color(1f, .48f, .015f, 1f);
        permanentDaylightOverlay = CreateAuthoredOverlay("DRL", "PermanentDaytimeRunningLights", white, 2.4f);
        daylightOverlay = CreateAuthoredOverlay("DRL_Indicator_left", "DaytimeRunningLightsLeft", white, 2.4f);
        daylightOverlayRight = CreateAuthoredOverlay("DRL_Indicator_right", "DaytimeRunningLightsRight", white, 2.4f);
        headlampOverlay = CreateAuthoredOverlay("Headlamps", "HeadlampProjectors", new Color(.90f, .95f, 1f), 3.2f);
        rearTailOverlay = CreateAuthoredOverlay("Rear_Driving_Lights", "RearTailLights", red, 1.1f);
        rearBrakeOverlay = CreateAuthoredOverlay("Brake_Lights", "RearBrakeLights", red, 2.6f);
        // These authored faces also belong to the rear indicators. Split only
        // by vehicle side so amber has priority during its flash, while the
        // other side's reverse lamp and both non-flashing phases stay white.
        reverseOverlay = CreateAuthoredOverlay("Reverse_Lights", "ReverseLightsLeft", new Color(.92f, .96f, 1f), 2.8f, -1);
        reverseOverlayRight = CreateAuthoredOverlay("Reverse_Lights", "ReverseLightsRight", new Color(.92f, .96f, 1f), 2.8f, 1);
        leftBlinkerOverlay = CreateAuthoredOverlay("DRL_Indicator_left", "FrontLeftIndicator", amber, 2.4f);
        rightBlinkerOverlay = CreateAuthoredOverlay("DRL_Indicator_right", "FrontRightIndicator", amber, 2.4f);
        rearLeftBlinkerOverlay = CreateAuthoredOverlay("Indicator_RL", "RearLeftIndicator", amber, 2.4f);
        rearRightBlinkerOverlay = CreateAuthoredOverlay("Indicator_RR", "RearRightIndicator", amber, 2.4f);
        mirrorLeftBlinkerOverlay = CreateAuthoredOverlay("Indicator_Mirror_left", "MirrorLeftIndicator", amber, 2.4f);
        mirrorRightBlinkerOverlay = CreateAuthoredOverlay("Indicator_Mirror_right", "MirrorRightIndicator", amber, 2.4f);

        var beamCount = ConfigureHeadlightBeams();
        initialized = true;
        LogInfo($"initialized authoredGroups=11 overlays={CountLampOverlays() + CountBlinkerOverlays()}/14 beams={beamCount}/2.");
        if (CountLampOverlays() != 8 || beamCount != 2 || CountBlinkerOverlays() != 6)
            LogWarning("one or more authored R8 lamp groups or road beams are missing.");
        if (blinkers == null)
            LogWarning("VehicleBlinker state source is missing; indicator input cannot be read.");
        ApplyState();
    }

    private void Update()
    {
        // This tick animates an already requested indicator phase. Vehicle,
        // brake, gear, light and input changes arrive through native callbacks.
        if (!initialized || (!leftBlinker && !rightBlinker))
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
    }

    private int ConfigureHeadlightBeams()
    {
        if (vehicleRoot == null)
            return 0;
        leftBeam = ResolveHeadlightBeam("LeftHeadlightBeam");
        rightBeam = ResolveHeadlightBeam("RightHeadlightBeam");
        return (leftBeam != null ? 1 : 0) + (rightBeam != null ? 1 : 0);
    }

    private Light? ResolveHeadlightBeam(string suffix)
    {
        var host = vehicleRoot?.Find("AudiR8_" + suffix);
        var light = host != null ? host.GetComponent<Light>() : null;
        var hasHdrpData = false;
        if (host != null)
            foreach (var component in host.GetComponents<Component>())
                if (component != null && component.GetType().FullName ==
                    "UnityEngine.Rendering.HighDefinition.HDAdditionalLightData")
                    hasHdrpData = component is Behaviour data && data.enabled;
        if (light == null || !hasHdrpData)
        {
            LogWarning($"road beam '{suffix}' is missing its bundled Light/HDRP data.");
            return null;
        }
        light.enabled = false;
        LogInfo($"road beam='{suffix}' HDRP=true candela={light.intensity} range={light.range} " +
                $"angle={light.spotAngle} cullingMask={light.cullingMask} layers={light.renderingLayerMask} " +
                $"localPosition={host!.localPosition} localAim={host.localEulerAngles}.");
        return light;
    }

    private MeshRenderer? CreateAuthoredOverlay(string group, string suffix, Color color, float intensity, int side = 0)
    {
        AudiR8LightBindings.Lamp? match = null;
        foreach (var lamp in lampBindings)
            if (lamp.group == group)
            {
                if (match != null)
                {
                    LogWarning($"duplicate authored group '{group}'.");
                    return null;
                }
                match = lamp;
            }
        if (match?.source == null || match.mesh == null)
        {
            LogWarning($"authored group '{group}' is missing its source renderer or mesh.");
            return null;
        }
        var triangles = new List<int>();
        var indices = match.mesh.triangles;
        var vertices = match.mesh.vertices;
        for (var i = 0; i < indices.Length; i += 3)
        {
            if (side != 0 && vehicleRoot != null)
            {
                var center = (vertices[indices[i]] + vertices[indices[i + 1]] + vertices[indices[i + 2]]) / 3f;
                var x = vehicleRoot.InverseTransformPoint(match.source.transform.TransformPoint(center)).x;
                if ((side < 0 && x >= 0f) || (side > 0 && x <= 0f)) continue;
            }
            triangles.Add(indices[i]); triangles.Add(indices[i + 1]); triangles.Add(indices[i + 2]);
        }
        if (triangles.Count == 0)
        {
            LogWarning($"authored group '{group}' contains no faces for side={side}.");
            return null;
        }
        var renderer = CreateOverlayFromTriangles(match.source, match.mesh,
            triangles, suffix, color, intensity, 1f);
        var bounds = renderer.bounds;
        LogInfo($"authored group='{group}' source='{match.source.name}' " +
                $"vertices={renderer.GetComponent<MeshFilter>().sharedMesh.vertexCount} " +
                $"worldCenter={bounds.center} worldSize={bounds.size} identityParent=true.");
        return renderer;
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
        // A lamp overlay needs only its selected vertices. Cloning the full lamp
        // mesh for each tiny strip multiplied crash processing and upload costs.
        var sourceVertices = sourceMesh.vertices;
        var remap = new Dictionary<int, int>();
        var selected = new List<int>();
        var compactTriangles = new int[triangles.Count];
        for (var i = 0; i < triangles.Count; i++)
        {
            var original = triangles[i];
            if (!remap.TryGetValue(original, out var compact))
            { compact = selected.Count; remap.Add(original, compact); selected.Add(original); }
            compactTriangles[i] = compact;
        }
        var vertices = CopySelected(sourceVertices, selected);
        var normals = CopySelected(sourceMesh.normals, selected);
        var mesh = new Mesh
        {
            name = sourceMesh.name + "_" + suffix,
            indexFormat = sourceMesh.indexFormat,
            vertices = OffsetFaces(vertices, normals),
            normals = normals,
            tangents = CopySelected(sourceMesh.tangents, selected),
            colors32 = CopySelected(sourceMesh.colors32, selected),
            uv = CopySelected(sourceMesh.uv, selected),
            uv2 = CopySelected(sourceMesh.uv2, selected)
        };
        mesh.SetTriangles(compactTriangles, 0, true);
        mesh.RecalculateBounds();
        generatedMeshes.Add(mesh);
        return CreateOverlayObject(source, mesh, suffix, color, intensity, scale);
    }

    private static T[] CopySelected<T>(T[] source, List<int> indices)
    {
        if (source.Length == 0) return Array.Empty<T>();
        var result = new T[indices.Count];
        for (var i = 0; i < result.Length; i++) result[i] = source[indices[i]];
        return result;
    }

    private static Vector3[] OffsetFaces(Vector3[] vertices, Vector3[] normals)
    {
        // Offset selected faces only 0.7 mm; never expand a lamp around the
        // vehicle origin. Scaling previously separated lenses from their housings.
        var result = (Vector3[])vertices.Clone();
        if (normals.Length == vertices.Length)
            for (var i = 0; i < result.Length; i++) result[i] += normals[i] * .0007f;
        return result;
    }

    private MeshRenderer CreateOverlayObject(MeshRenderer source, Mesh mesh, string suffix,
        Color color, float intensity, float scale)
    {
        var host = new GameObject("AudiR8_" + suffix);
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
        var blinking = leftBlinker || rightBlinker;
        if (blinking && !wasBlinking)
            blinkerPhaseStartedAt = Time.unscaledTime;
        var flash = blinking && Mathf.Repeat(Time.unscaledTime - blinkerPhaseStartedAt,
                BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        wasBlinking = blinking;

        SetEnabled(permanentDaylightOverlay, controlled);
        SetEnabled(daylightOverlay, controlled && !leftBlinker);
        SetEnabled(daylightOverlayRight, controlled && !rightBlinker);
        SetEnabled(headlampOverlay, lightsOn);
        SetEnabled(rearTailOverlay, lightsOn);
        SetEnabled(rearBrakeOverlay, braking);
        SetEnabled(reverseOverlay, reversing && !(leftBlinker && flash));
        SetEnabled(reverseOverlayRight, reversing && !(rightBlinker && flash));
        SetEnabled(leftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(rearLeftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(rearRightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(mirrorLeftBlinkerOverlay, leftBlinker && flash);
        SetEnabled(mirrorRightBlinkerOverlay, rightBlinker && flash);
        SetEnabled(leftBeam, controlled && lightsOn);
        SetEnabled(rightBeam, controlled && lightsOn);
        var state = (controlled ? 1 : 0) | (lightsOn ? 2 : 0) | (braking ? 4 : 0) |
                    (leftBlinker ? 8 : 0) | (rightBlinker ? 16 : 0) | (reversing ? 32 : 0);
        if (state == lastState)
            return;
        lastState = state;
        LogInfo($"state playerControlled={controlled} lights={lightsOn} braking={braking} " +
                $"reverse={reversing} leftBlinker={leftBlinker} rightBlinker={rightBlinker} " +
                $"roadBeams={leftBeam?.enabled}/{rightBeam?.enabled} candela={leftBeam?.intensity}/{rightBeam?.intensity}.");
    }

    private void SubscribeState()
    {
        if (!initialized || vehicle == null || stateBindings.Count != 0) return;
        var physics = vehicle.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        var lights = physics?.effectsManager.lightsManager;
        if (lights == null) { LogWarning("native light-state callbacks unavailable."); return; }
        Bind(lights.lowBeamLights, value => { lightsOn = controlled && value; ApplyState(); });
        Bind(lights.brakeLights, value => { braking = controlled && value; ApplyState(); });
        Bind(lights.reverseLights, value => { reversing = controlled && value; ApplyState(); });
        GlobalEvents.onEnterVehicle += OnEntered;
        GlobalEvents.onExitVehicle += OnExited;
        GlobalEvents.onVehicleVariablesChanged += OnVariablesChanged;
        var actions = typeof(InputActionHelper).GetField("PlayerInputActionMap",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic)?.GetValue(null)
            as Dictionary<PlayerAction, InputAction>;
        if (actions != null)
        {
            actions.TryGetValue(PlayerAction.VehicleLeftBlinker, out leftAction);
            actions.TryGetValue(PlayerAction.VehicleRightBlinker, out rightAction);
            if (leftAction != null) leftAction.performed += OnIndicatorInput;
            if (rightAction != null) rightAction.performed += OnIndicatorInput;
        }
        else LogWarning("indicator input callbacks unavailable.");
        OnVariablesChanged();
    }

    private void Bind(VehicleLight owner, Action<bool> apply)
    {
        var source = new LightSource();
        source.onLightTurnedOn.AddListener(() => apply(true));
        source.onLightTurnedOff.AddListener(() => apply(false));
        owner.lightSources.Add(source);
        stateBindings.Add((owner, source));
        if (owner.On) source.TurnOn();
    }

    private void OnEntered(VehicleController entered)
    {
        if (entered != vehicle) return;
        OnVariablesChanged();
    }

    private void OnExited(VehicleController exited)
    {
        if (exited != vehicle) return;
        controlled = lightsOn = braking = reversing = leftBlinker = rightBlinker = false;
        ApplyState();
    }

    private void OnVariablesChanged()
    {
        if (!isActiveAndEnabled) return;
        controlled = vehicle != null && vehicle.controlledByPlayer;
        lightsOn = controlled && GetBoolProperty(vehicle, "ShouldLightsBeOn");
        braking = controlled && GetBoolProperty(brakes, "IsBraking");
        reversing = controlled && GetIntMember(transmission, "Gear") < 0;
        ReadIndicators();
        ApplyState();
    }

    private void OnIndicatorInput(InputAction.CallbackContext _)
    {
        if (controlled) StartCoroutine(AfterNativeIndicatorInput());
    }

    private IEnumerator AfterNativeIndicatorInput()
    {
        // One deferred callback lets CarController consume the same action.
        // There is no recurring input or state-discovery coroutine.
        yield return null;
        ReadIndicators();
        ApplyState();
    }

    private void ReadIndicators()
    {
        leftBlinker = controlled && GetBoolField(blinkers, "_isLeftBlinkerOn");
        rightBlinker = controlled && GetBoolField(blinkers, "_isRightBlinkerOn");
    }

    private void OnEnable() { if (initialized) SubscribeState(); }

    private void OnDisable()
    {
        GlobalEvents.onEnterVehicle -= OnEntered;
        GlobalEvents.onExitVehicle -= OnExited;
        GlobalEvents.onVehicleVariablesChanged -= OnVariablesChanged;
        if (leftAction != null) leftAction.performed -= OnIndicatorInput;
        if (rightAction != null) rightAction.performed -= OnIndicatorInput;
        foreach (var binding in stateBindings)
        {
            binding.owner.lightSources.Remove(binding.source);
            binding.source.onLightTurnedOn.RemoveAllListeners();
            binding.source.onLightTurnedOff.RemoveAllListeners();
        }
        stateBindings.Clear();
        controlled = lightsOn = braking = reversing = leftBlinker = rightBlinker = false;
        ApplyState();
    }

    private int CountLampOverlays() =>
        (permanentDaylightOverlay != null ? 1 : 0) +
        (daylightOverlay != null ? 1 : 0) + (daylightOverlayRight != null ? 1 : 0) +
        (headlampOverlay != null ? 1 : 0) + (rearTailOverlay != null ? 1 : 0) +
        (rearBrakeOverlay != null ? 1 : 0) + (reverseOverlay != null ? 1 : 0) +
        (reverseOverlayRight != null ? 1 : 0);

    private int CountBlinkerOverlays() =>
        (leftBlinkerOverlay != null ? 1 : 0) + (rightBlinkerOverlay != null ? 1 : 0) +
        (rearLeftBlinkerOverlay != null ? 1 : 0) + (rearRightBlinkerOverlay != null ? 1 : 0) +
        (mirrorLeftBlinkerOverlay != null ? 1 : 0) + (mirrorRightBlinkerOverlay != null ? 1 : 0);

    private static object? GetMember(object? target, string name)
    {
        if (target == null)
            return null;
        return target.GetType().GetField(name, InstanceMembers)?.GetValue(target) ??
               target.GetType().GetProperty(name, InstanceMembers)?.GetValue(target);
    }

    private static bool GetBoolProperty(object? target, string name) =>
        GetMember(target, name) is bool value && value;

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
        if (!AudiR8Diagnostics.LightingDebugEnabled)
            return;
        AudiR8Diagnostics.Info(
            context,
            $"AudiR8 lighting vehicle='{gameObject.name}' " +
            $"instance={GetInstanceID()}: {message}");
    }

    private void LogWarning(string message) =>
        context?.Logger.Warn($"AudiR8 lighting vehicle='{gameObject.name}' " +
                             $"instance={GetInstanceID()}: {message}");

    private void OnDestroy()
    {
        OnDisable();
        foreach (var generatedObject in generatedObjects)
            if (generatedObject != null) Destroy(generatedObject);
        foreach (var generatedMaterial in generatedMaterials)
            if (generatedMaterial != null) Destroy(generatedMaterial);
        foreach (var generatedMesh in generatedMeshes)
            if (generatedMesh != null) Destroy(generatedMesh);
    }

}


