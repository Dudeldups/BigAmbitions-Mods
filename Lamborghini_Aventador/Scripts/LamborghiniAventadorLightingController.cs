#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.InputSystem;
using GleyTrafficSystem;
using NWH.VehiclePhysics2.Effects;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Aventador SVJ lighting for the supplied Outlaw Games GLB. The lamp map is
/// uses the Blender selections bound to their original production renderers.
/// </summary>
internal sealed class LamborghiniAventadorLightingController : MonoBehaviour
{
    internal static bool LightingDebugEnabled { get; set; } = false;

    private const float BlinkerHalfPeriod = 0.42f;
    private const float CalibratedLampMapBodyY = -0.04f;
    private float lampMapHeightOffset;
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly HashSet<string> ReportedWarnings = new HashSet<string>();
    private static readonly FieldInfo? TrafficMeshRenderer = typeof(VehicleLightsComponent).GetField("meshRenderer", Members);

    private readonly List<GameObject> ownedObjects = new List<GameObject>();
    private readonly List<Mesh> ownedMeshes = new List<Mesh>();
    private readonly List<Material> ownedMaterials = new List<Material>();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.VehicleController? physicsVehicle;
    private VehicleLightsComponent? trafficLights;
    private Material? trafficLampTemplate;
    private MeshRenderer? trafficLampRenderer;
    private Transform? vehicleRoot;
    private ModContext? context;
    private object? brakes;
    private object? blinkers;
    private object? transmission;
    private object? nwhController;
    private object? nwhLights;
    private InputAction? leftAction;
    private InputAction? rightAction;
    private Coroutine? indicatorInputRoutine;
    private readonly List<(VehicleLight owner, LightSource source)> stateBindings = new();
    private MeshRenderer? drl;
    private MeshRenderer? lowBeamEmitter;
    private MeshRenderer? highBeamEmitter;
    private MeshRenderer? tail;
    private MeshRenderer? brake;
    private MeshRenderer? centerBrake;
    private MeshRenderer? reverse;
    private MeshRenderer? frontLeftIndicator;
    private MeshRenderer? frontRightIndicator;
    private MeshRenderer? sideLeftIndicator;
    private MeshRenderer? sideRightIndicator;
    private MeshRenderer? rearLeftIndicator;
    private MeshRenderer? rearRightIndicator;
    private Light? leftBeam;
    private Light? rightBeam;
    private bool initialized;
    private bool stateSubscribed;
    private bool controlled;
    private bool lightsOn;
    private bool braking;
    private bool reversing;
    private bool highBeam;
    private bool leftIndicator;
    private bool rightIndicator;
    private bool ambientTraffic;
    private bool wasBlinking;
    private bool? lastBeamHighState;
    private float blinkerStartedAt;
    private bool updateWarningLogged;

    /// <summary>Initialize a player-owned vehicle after its imported model is attached.</summary>
    public void Initialize(VehicleController controller, ModContext modContext)
    {
        if (initialized && vehicle == controller)
            return;
        UnsubscribeState();
        vehicle = controller;
        physicsVehicle = controller.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        vehicleRoot = controller.transform;
        context = modContext;
        trafficLights = null;
        LocateStateSources(controller.gameObject);
        BuildModelLampMap(controller.gameObject);
        SubscribeState();
    }

    /// <summary>Initialize a cloned Gley traffic vehicle from its clone initializer.</summary>
    internal void InitializeForAmbientTraffic(ModContext modContext)
    {
        if (initialized)
            return;
        ambientTraffic = true;
        context = modContext;
        vehicleRoot = transform;
        vehicle = GetComponentInChildren<VehicleController>(true);
        physicsVehicle = vehicle != null ? vehicle.GetComponent<NWH.VehiclePhysics2.VehicleController>() : null;
        trafficLights = GetComponentInChildren<VehicleLightsComponent>(true);
        if (vehicle != null)
            LocateStateSources(gameObject);
        var bundle = AssetService.GetBundle(modContext.ModId, LamborghiniAventadorMod.BundleKey);
        trafficLampTemplate = bundle?.LoadAsset<Material>(LamborghiniAventadorMod.GleyLampMaterialPath);
        if (trafficLights == null)
            LogWarning("native Gley VehicleLightsComponent is unavailable; ambient custom lamps cannot be bound.");
        if (trafficLampTemplate == null)
            LogWarning("Gley lamp shader material is unavailable in the Aventador bundle; ambient custom lamps cannot be bound.");
        BuildModelLampMap(gameObject);
        ConfigureTrafficLampRenderer();
    }

    private void LocateStateSources(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            if (brakes == null) brakes = GetMember(component, "brakes");
            if (component.GetType().FullName == "Vehicles.Components.VehicleBlinker") blinkers = component;
            if (component.GetType().FullName == "NWH.VehiclePhysics2.VehicleController")
            {
                physicsVehicle = component as NWH.VehiclePhysics2.VehicleController;
                nwhController = component;
                var powertrain = GetMember(component, "powertrain");
                transmission = GetMember(powertrain, "transmission");
                nwhLights = GetMember(component, "lights");
            }
        }
    }

    private void BuildModelLampMap(GameObject root)
    {
        var body = root.transform.Find("AventadorBody");
        lampMapHeightOffset = body != null ? body.localPosition.y - CalibratedLampMapBodyY : 0f;
        if (body == null)
        {
            LogWarning("the Aventador body for the authored Blender lights is missing.");
            return;
        }

        var prefix = LamborghiniAventadorLampGeometry.SourcePrefix;
        var holders = body.GetComponentsInChildren<MeshFilter>(true)
            .Where(filter => filter.name.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(filter => filter.name, StringComparer.Ordinal);
        MeshRenderer? Lamp(string group, string name, Color color, float intensity)
        {
            holders.TryGetValue(prefix + group, out var holder);
            var anchor = holder != null ? holder.transform.parent?.GetComponent<MeshRenderer>() : null;
            var mesh = holder != null ? holder.sharedMesh : null;
            if (anchor != null && mesh != null)
                return CreateOverlay(anchor, mesh, "Aventador_" + name, color, intensity);

            LogWarning($"authored Blender lamp '{group}' could not bind " +
                $"(holder={holder != null}, anchor={anchor != null}, mesh={mesh != null}).");
            return null;
        }

        var amber = new Color(1f, .16f, .004f);
        drl = Lamp("DRL", "DRL", new Color(.78f, .91f, 1f), 4.6f);
        lowBeamEmitter = Lamp("Headlights", "LowBeamEmitter", new Color(.88f, .94f, 1f), 5.7f);
        highBeamEmitter = Lamp("Headlights", "HighBeamEmitter", new Color(.94f, .97f, 1f), 7.1f);
        tail = Lamp("Rear_Driving_Brake_Lights", "TailSignature", new Color(.72f, .006f, .002f), 2.4f);
        brake = Lamp("Rear_Driving_Brake_Lights", "BrakeSignature", new Color(1f, .008f, .001f), 4.8f);
        centerBrake = Lamp("High_Mounted_Brake_Light", "CenterBrakeLamp", new Color(1f, .008f, .001f), 4.8f);
        reverse = Lamp("Reverse_Lights", "ReverseEmitters", new Color(.90f, .96f, 1f), 4.2f);
        frontLeftIndicator = Lamp("Indicator_FL", "FrontLeftIndicator", amber, 5.0f);
        frontRightIndicator = Lamp("Indicator_FR", "FrontRightIndicator", amber, 5.0f);
        sideLeftIndicator = Lamp("Indicator_Side_L", "SideLeftIndicator", amber, 5.0f);
        sideRightIndicator = Lamp("Indicator_Side_R", "SideRightIndicator", amber, 5.0f);
        rearLeftIndicator = Lamp("Indicator_RL", "RearLeftIndicator", amber, 5.2f);
        rearRightIndicator = Lamp("Indicator_RR", "RearRightIndicator", amber, 5.2f);

        ConfigureHeadlightBeams();
        initialized = true;
        if (LamborghiniAventadorDiagnostics.DebugEnabled && LightingDebugEnabled)
            context?.Logger.Info($"LamborghiniAventador Blender light map vehicle={GetInstanceID()} traffic={ambientTraffic} " +
                $"holders={holders.Count} outputs={ownedObjects.Count - CountBeams()} beams={CountBeams()} " +
                $"DRLShader='{drl?.sharedMaterial?.shader?.name}' DRLShaderSupported={drl?.sharedMaterial?.shader?.isSupported}.");
        ApplyState();
    }

    private void ConfigureHeadlightBeams()
    {
        // Emit from the actual two SVJ projector bowls; no inherited donor beam
        // location is reused. Local +Z is the vehicle's forward axis in the GLB.
        leftBeam = CreateSpotBeam(-.71f, "Aventador_LeftHeadlampBeam");
        rightBeam = CreateSpotBeam(.71f, "Aventador_RightHeadlampBeam");
    }

    private Light CreateSpotBeam(float localX, string name)
    {
        var host = new GameObject(name);
        host.transform.SetParent(vehicleRoot, false);
        host.transform.localPosition = new Vector3(localX, .47f + lampMapHeightOffset, 1.86f);
        host.transform.localRotation = Quaternion.Euler(2f, 0f, 0f);
        host.layer = gameObject.layer;
        var light = host.AddComponent<Light>();
        light.type = LightType.Spot;
        light.color = new Color(.88f, .94f, 1f);
        light.intensity = 16500f;
        light.range = 52f;
        light.spotAngle = 50f;
        light.innerSpotAngle = 32f;
        light.shadows = LightShadows.None;
        light.enabled = false;
        ownedObjects.Add(host);
        return light;
    }

    private MeshRenderer CreateOverlay(MeshRenderer source, Mesh mesh, string name, Color color, float intensity)
    {
        var overlayObject = new GameObject(name);
        overlayObject.transform.SetParent(source.transform, false);
        overlayObject.layer = source.gameObject.layer;
        overlayObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var overlay = overlayObject.AddComponent<MeshRenderer>();
        overlay.sharedMaterial = CreateLampMaterial(name, color, intensity);
        overlay.renderingLayerMask = source.renderingLayerMask;
        overlay.shadowCastingMode = ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.enabled = false;
        ownedObjects.Add(overlayObject);
        return overlay;
    }

    private Material CreateLampMaterial(string name, Color color, float intensity)
    {
        if (trafficLights == null || trafficLampTemplate == null)
            return CreateEmissionMaterial(name + "Material", color, intensity);

        var material = new Material(trafficLampTemplate) { name = name + "Material" };
        material.SetFloat("_LampMode", GetTrafficLampMode(name));
        material.SetColor("_LampColor", color);
        material.SetFloat("_LampIntensity", intensity);
        ownedMaterials.Add(material);
        return material;
    }

    private static float GetTrafficLampMode(string name) => name switch
    {
        "Aventador_DRL" => 0f,
        "Aventador_LowBeamEmitter" => 1f,
        "Aventador_HighBeamEmitter" => 1f,
        "Aventador_TailSignature" => 2f,
        "Aventador_BrakeSignature" => 3f,
        "Aventador_CenterBrakeLamp" => 3f,
        "Aventador_ReverseEmitters" => 4f,
        "Aventador_FrontLeftIndicator" or "Aventador_SideLeftIndicator" => 5f,
        "Aventador_RearLeftIndicator" => 5f,
        "Aventador_FrontRightIndicator" or "Aventador_SideRightIndicator" => 6f,
        "Aventador_RearRightIndicator" => 6f,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown Aventador traffic lamp overlay.")
    };

    private void ConfigureTrafficLampRenderer()
    {
        if (trafficLights == null || trafficLampTemplate == null)
        {
            DisableIndividualLampOverlays();
            return;
        }
        if (vehicleRoot == null || TrafficMeshRenderer == null ||
            TrafficMeshRenderer.FieldType != typeof(MeshRenderer))
        {
            DisableIndividualLampOverlays();
            LogWarning("Gley meshRenderer field is unavailable; ambient custom lamps cannot use Gley's property block.");
            return;
        }

        var sources = ownedObjects
            .Where(obj => obj != null && obj.GetComponent<MeshFilter>() != null && obj.GetComponent<MeshRenderer>() != null)
            .Select(obj => (Filter: obj.GetComponent<MeshFilter>()!, Renderer: obj.GetComponent<MeshRenderer>()!))
            .ToArray();
        if (sources.Length == 0 || sources.Any(source => source.Renderer.sharedMaterial == null))
        {
            DisableIndividualLampOverlays();
            LogWarning("Gley lamp overlay meshes or materials are missing; ambient custom lamps cannot be bound.");
            return;
        }

        var overlayRoot = new GameObject("Aventador_GleyLampOverlay");
        overlayRoot.transform.SetParent(vehicleRoot, false);
        overlayRoot.layer = gameObject.layer;
        var combine = new CombineInstance[sources.Length];
        var materials = new Material[sources.Length];
        for (var index = 0; index < sources.Length; index++)
        {
            combine[index] = new CombineInstance
            {
                mesh = sources[index].Filter.sharedMesh,
                subMeshIndex = 0,
                transform = overlayRoot.transform.worldToLocalMatrix * sources[index].Filter.transform.localToWorldMatrix
            };
            materials[index] = sources[index].Renderer.sharedMaterial;
        }

        var mesh = new Mesh
        {
            name = "Aventador_GleyLampOverlayMesh",
            indexFormat = sources.Sum(source => source.Filter.sharedMesh.vertexCount) > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };
        mesh.CombineMeshes(combine, false, true, false);
        mesh.RecalculateBounds();
        ownedMeshes.Add(mesh);
        var filter = overlayRoot.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        trafficLampRenderer = overlayRoot.AddComponent<MeshRenderer>();
        trafficLampRenderer.sharedMaterials = materials;
        trafficLampRenderer.shadowCastingMode = ShadowCastingMode.Off;
        trafficLampRenderer.receiveShadows = false;
        trafficLampRenderer.enabled = false;
        ownedObjects.Add(overlayRoot);

        TrafficMeshRenderer.SetValue(trafficLights, trafficLampRenderer);
        if (!ReferenceEquals(TrafficMeshRenderer.GetValue(trafficLights), trafficLampRenderer))
        {
            Destroy(overlayRoot);
            ownedObjects.Remove(overlayRoot);
            ownedMeshes.Remove(mesh);
            Destroy(mesh);
            LogWarning("Gley did not retain the Aventador lamp renderer; ambient custom lamps cannot be bound.");
            return;
        }

        foreach (var source in sources)
            source.Renderer.enabled = false;
        trafficLampRenderer.enabled = true;

        if (LamborghiniAventadorDiagnostics.DebugEnabled && LightingDebugEnabled)
            context?.Logger.Info($"LamborghiniAventador traffic lamp renderer bound vehicle={GetInstanceID()} overlays={sources.Length} materialBlock=Gley.");
    }

    private void DisableIndividualLampOverlays()
    {
        foreach (var obj in ownedObjects)
        {
            if (obj == null) continue;
            var renderer = obj.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
        }
    }

    private Material CreateEmissionMaterial(string name, Color color, float intensity)
    {
        var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Color") ?? throw new InvalidOperationException("Aventador lighting requires an unlit HDRP-compatible shader.");
        var material = new Material(shader) { name = name, renderQueue = 2450 };
        var hdr = color * intensity;
        hdr.a = 1f;
        SetColor(material, "_UnlitColor", hdr); SetColor(material, "_BaseColor", hdr);
        SetColor(material, "_Color", hdr); SetColor(material, "_EmissiveColor", hdr);
        SetColor(material, "_EmissionColor", hdr);
        SetFloat(material, "_SurfaceType", 0f); SetFloat(material, "_ZWrite", 1f); SetFloat(material, "_Cull", 0f);
        material.EnableKeyword("_EMISSION");
        ownedMaterials.Add(material);
        return material;
    }

    private void Update()
    {
        if (!initialized || ambientTraffic) return;
        try
        {
            // Player lamp state changes arrive through NWH and Big Ambitions
            // callbacks. This tick only animates an already requested indicator.
            if (leftIndicator || rightIndicator)
                ApplyState();
        }
        catch (Exception exception)
        {
            if (updateWarningLogged) return;
            updateWarningLogged = true;
            LogWarning($"state update failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void ApplyState()
    {
        if (ambientTraffic) return;
        var blinking = leftIndicator || rightIndicator;
        if (blinking && !wasBlinking) blinkerStartedAt = Time.unscaledTime;
        var flash = blinking && Mathf.Repeat(Time.unscaledTime - blinkerStartedAt, BlinkerHalfPeriod * 2f) < BlinkerHalfPeriod;
        wasBlinking = blinking;

        // Keep the front signature on while the player controls the car, even
        // with low beams. Low/high projectors and rear tail lamps use
        // authoritative headlight state.
        SetEnabled(drl, controlled || lightsOn);
        SetEnabled(lowBeamEmitter, lightsOn && !highBeam);
        SetEnabled(highBeamEmitter, lightsOn && highBeam);
        SetEnabled(tail, lightsOn && !braking);
        SetEnabled(brake, braking);
        SetEnabled(centerBrake, braking);
        SetEnabled(reverse, reversing);
        SetEnabled(frontLeftIndicator, leftIndicator && flash); SetEnabled(rearLeftIndicator, leftIndicator && flash);
        SetEnabled(frontRightIndicator, rightIndicator && flash); SetEnabled(rearRightIndicator, rightIndicator && flash);
        SetEnabled(sideLeftIndicator, leftIndicator && flash);
        SetEnabled(sideRightIndicator, rightIndicator && flash);
        var beamsOn = controlled && lightsOn;
        SetEnabled(leftBeam, beamsOn);
        SetEnabled(rightBeam, beamsOn);
        if (beamsOn && lastBeamHighState != highBeam)
        {
            ConfigureBeamState(leftBeam, highBeam);
            ConfigureBeamState(rightBeam, highBeam);
            lastBeamHighState = highBeam;
        }
    }

    private static void ConfigureBeamState(Light? beam, bool high)
    {
        if (beam == null) return;
        beam.range = high ? 72f : 52f;
        beam.spotAngle = high ? 62f : 50f;
        beam.innerSpotAngle = high ? 42f : 32f;
        beam.intensity = high ? 26000f : 16500f;
    }

    private void SubscribeState()
    {
        if (!initialized || vehicle == null || trafficLights != null || stateSubscribed)
            return;

        stateSubscribed = true;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onExitVehicle += HandleVehicleExited;
        GlobalEvents.outdoorLightsStatusChanged -= HandleOutdoorLightsChanged;
        GlobalEvents.outdoorLightsStatusChanged += HandleOutdoorLightsChanged;
        GlobalEvents.onBindingsChanged -= HandleBindingsChanged;
        GlobalEvents.onBindingsChanged += HandleBindingsChanged;

        if (physicsVehicle != null)
        {
            var lightManager = physicsVehicle.effectsManager != null ? physicsVehicle.effectsManager.lightsManager : null;
            if (lightManager != null)
            {
                Bind(lightManager.lowBeamLights, value =>
                {
                    if (!controlled) return;
                    lightsOn = value;
                    ApplyState();
                });
                Bind(lightManager.brakeLights, value =>
                {
                    if (!controlled) return;
                    braking = value;
                    ApplyState();
                });
                Bind(lightManager.reverseLights, value =>
                {
                    if (!controlled) return;
                    reversing = value;
                    ApplyState();
                });
                Bind(GetMember(lightManager, "highBeamLights") as VehicleLight, value =>
                {
                    if (!controlled) return;
                    highBeam = value;
                    ApplyState();
                });
            }

            physicsVehicle.brakes.onBrakesActivate.AddListener(HandleBrakesActivated);
            physicsVehicle.brakes.onBrakesDeactivate.AddListener(HandleBrakesDeactivated);
            physicsVehicle.powertrain.transmission.onShift.AddListener(HandleGearChanged);
        }
        else
        {
            LogWarning("NWH controller missing; native brake, reverse, and lamp state callbacks are unavailable.");
        }

        RefreshIndicatorInputBindings();
        SynchronizePlayerState();
    }

    private void UnsubscribeState()
    {
        if (stateSubscribed)
        {
            GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
            GlobalEvents.onExitVehicle -= HandleVehicleExited;
            GlobalEvents.outdoorLightsStatusChanged -= HandleOutdoorLightsChanged;
            GlobalEvents.onBindingsChanged -= HandleBindingsChanged;
            if (physicsVehicle != null)
            {
                physicsVehicle.brakes.onBrakesActivate.RemoveListener(HandleBrakesActivated);
                physicsVehicle.brakes.onBrakesDeactivate.RemoveListener(HandleBrakesDeactivated);
                physicsVehicle.powertrain.transmission.onShift.RemoveListener(HandleGearChanged);
            }
            foreach (var binding in stateBindings)
            {
                binding.owner.lightSources.Remove(binding.source);
                binding.source.onLightTurnedOn.RemoveAllListeners();
                binding.source.onLightTurnedOff.RemoveAllListeners();
            }
            stateBindings.Clear();
            stateSubscribed = false;
        }

        UnbindIndicatorInput();
        if (indicatorInputRoutine != null)
        {
            StopCoroutine(indicatorInputRoutine);
            indicatorInputRoutine = null;
        }
    }

    private void Bind(VehicleLight? owner, Action<bool> apply)
    {
        if (owner == null) return;
        var source = new LightSource();
        source.onLightTurnedOn.AddListener(() => apply(true));
        source.onLightTurnedOff.AddListener(() => apply(false));
        owner.lightSources.Add(source);
        stateBindings.Add((owner, source));
        if (owner.On) source.TurnOn();
    }

    private void RefreshIndicatorInputBindings()
    {
        UnbindIndicatorInput();
        var actions = typeof(InputActionHelper).GetField("PlayerInputActionMap",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            as Dictionary<PlayerAction, InputAction>;
        if (actions == null)
        {
            LogWarning("player indicator input callbacks are unavailable; native signals cannot be mirrored.");
            return;
        }

        actions.TryGetValue(PlayerAction.VehicleLeftBlinker, out leftAction);
        actions.TryGetValue(PlayerAction.VehicleRightBlinker, out rightAction);
        if (leftAction != null) leftAction.performed += HandleIndicatorInput;
        if (rightAction != null) rightAction.performed += HandleIndicatorInput;
        if (leftAction == null || rightAction == null)
            LogWarning("one or more player indicator input actions are missing.");
    }

    private void UnbindIndicatorInput()
    {
        if (leftAction != null) leftAction.performed -= HandleIndicatorInput;
        if (rightAction != null) rightAction.performed -= HandleIndicatorInput;
        leftAction = null;
        rightAction = null;
    }

    private void HandleBindingsChanged() => RefreshIndicatorInputBindings();

    private void HandleIndicatorInput(InputAction.CallbackContext _)
    {
        if (!controlled || !isActiveAndEnabled) return;
        if (indicatorInputRoutine != null) StopCoroutine(indicatorInputRoutine);
        indicatorInputRoutine = StartCoroutine(SynchronizeIndicatorsAfterNativeInput());
    }

    private IEnumerator SynchronizeIndicatorsAfterNativeInput()
    {
        // CarController consumes this same input in Update and flips the native
        // VehicleBlinker flags. This single deferred read follows that callback.
        yield return null;
        indicatorInputRoutine = null;
        ReadIndicators();
        ApplyState();
    }

    private void HandleVehicleEntered(VehicleController entered)
    {
        if (entered != vehicle) return;
        SynchronizePlayerState();
    }

    private void HandleVehicleExited(VehicleController exited)
    {
        if (exited != vehicle) return;
        controlled = lightsOn = braking = reversing = highBeam = leftIndicator = rightIndicator = false;
        ApplyState();
    }

    private void HandleOutdoorLightsChanged(bool _)
    {
        if (!controlled) return;
        lightsOn = ReadBoolMember(vehicle, "ShouldLightsBeOn");
        ApplyState();
    }

    private void HandleBrakesActivated()
    {
        if (!controlled) return;
        braking = true;
        ApplyState();
    }

    private void HandleBrakesDeactivated()
    {
        if (!controlled) return;
        braking = false;
        ApplyState();
    }

    private void HandleGearChanged()
    {
        if (!controlled || physicsVehicle == null) return;
        reversing = physicsVehicle.powertrain.transmission.Gear < 0;
        ApplyState();
    }

    private void SynchronizePlayerState()
    {
        controlled = vehicle != null && vehicle.controlledByPlayer;
        if (!controlled)
        {
            lightsOn = braking = reversing = highBeam = leftIndicator = rightIndicator = false;
        }
        else
        {
            lightsOn = ReadBoolMember(vehicle, "ShouldLightsBeOn");
            braking = physicsVehicle != null && physicsVehicle.brakes.IsBraking;
            reversing = physicsVehicle != null && physicsVehicle.powertrain.transmission.Gear < 0;
            ReadIndicators();
        }
        ApplyState();
    }

    private void ReadIndicators()
    {
        leftIndicator = controlled && ReadBoolField(blinkers, "_isLeftBlinkerOn");
        rightIndicator = controlled && ReadBoolField(blinkers, "_isRightBlinkerOn");
    }

    private static object? GetMember(object? target, string name) => target == null ? null :
        target.GetType().GetField(name, Members)?.GetValue(target) ?? target.GetType().GetProperty(name, Members)?.GetValue(target);
    private static bool ReadBoolMember(object? target, string name) => GetMember(target, name) is bool value && value;
    private static bool ReadBoolField(object? target, string name) =>
        target?.GetType().GetField(name, Members)?.GetValue(target) is bool value && value;
    private static void SetEnabled(Renderer? renderer, bool enabled)
    { if (renderer != null && renderer.enabled != enabled) renderer.enabled = enabled; }
    private static void SetEnabled(Light? light, bool enabled)
    { if (light != null && light.enabled != enabled) light.enabled = enabled; }
    private static void SetColor(Material material, string property, Color value)
    { if (material.HasProperty(property)) material.SetColor(property, value); }
    private static void SetFloat(Material material, string property, float value)
    { if (material.HasProperty(property)) material.SetFloat(property, value); }
    private int CountBeams() => (leftBeam != null ? 1 : 0) + (rightBeam != null ? 1 : 0);
    private void LogWarning(string message)
    {
        if (!ReportedWarnings.Add(message)) return;
        context?.Logger.Warn($"LamborghiniAventador lighting vehicle={GetInstanceID()}: {message}");
    }

    private void OnEnable()
    {
        if (initialized && ambientTraffic && trafficLampRenderer != null)
            trafficLampRenderer.enabled = true;
        else if (initialized && trafficLights == null)
            SubscribeState();
    }

    private void OnDisable()
    {
        UnsubscribeState();
        controlled = lightsOn = braking = reversing = highBeam = leftIndicator = rightIndicator = false;
        foreach (var obj in ownedObjects)
        {
            if (obj == null) continue;
            var renderer = obj.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.enabled = false;
            var light = obj.GetComponent<Light>();
            if (light != null) light.enabled = false;
        }
    }

    private void OnDestroy()
    {
        UnsubscribeState();
        foreach (var obj in ownedObjects) if (obj != null) Destroy(obj);
        foreach (var material in ownedMaterials) if (material != null) Destroy(material);
        foreach (var mesh in ownedMeshes) if (mesh != null) Destroy(mesh);
    }
}
