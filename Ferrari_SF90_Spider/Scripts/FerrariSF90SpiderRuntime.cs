#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BusinessLayoutSets;
using Helpers;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

public sealed class FerrariSF90SpiderRuntime : MonoBehaviour
{
    private const string VehicleRepainterColorRestoredEvent =
        "vehicle-repainter:color-restored";
    private const string VehicleRepainterColorPreviewEvent =
        "vehicle-repainter:color-preview";
    private const string VehicleRepainterColorResetEvent =
        "vehicle-repainter:color-reset";
    private const string DeveloperToolsVehicleRecolorEvent =
        "developer-tools:vehicle-recolor";
    private const string DeveloperToolsVehicleRecolorRestoredEvent =
        "developer-tools:vehicle-recolor-restored";
    // Reuse the game's native player-car sleep configuration. This keeps the
    // standard player-car sleep action working for dealer cars and unsaved dev spawns.
    private const string NativeCarSleepDonorPrefabPath =
        "Vehicles/PlayerVehicles/HonzaMimic";
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1670f;
    // Ferrari quotes 1,000 cv / 735 kW combined system output. NWH applies its
    // maxPower setting more directly than real crank/system output, so this is a
    // solver calibration starting point. Tune it against the included 0-100 and
    // 0-200 telemetry targets rather than replacing the public VehicleType rating.
    private const float EnginePowerKw = 510f;
    private const float BrakeTorque = 2450f;
    private const float EngineIdleRpm = 900f;
    private const float EngineLimitRpm = 8000f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private const float WarehouseExitEntranceSearchRadius = 12f;
    private const float WarehouseExitGuardDuration = 8f;
    private const float WarehouseExitGuardClearDistance = 4f;
    private const float SpeedLimitKph = 340f;
    private const float FinalDriveRatio = 4.51f;
    private const float EngineInertia = 0.12f;
    private const float EngineStartDuration = 0.42f;
    private const float ClutchEngagementRpm = 1400f;
    private const float ClutchThrottleOffsetRpm = 700f;
    private const float ClutchEngagementRange = 650f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 0.96f;
    // Repeated road tests with 490 kW averaged roughly 2.34 s to 100 km/h while
    // clean 0-200 runs landed at 7.02-7.04 s. Preserve the factory-like high-speed
    // pace with a small power increase, while trimming mostly rear longitudinal
    // grip so the AWD launch moves toward Ferrari's 2.5 s target without coupling
    // the correction entirely to engine output.
    private const float FrontLongitudinalGrip = 0.49f;
    private const float RearLongitudinalGrip = 0.65f;
    private const float AntiRollBarForce = 7200f;
    private const float FrontSuspensionTravel = 0.075f;
    private const float RearSuspensionTravel = 0.075f;
    private const float VisualRideHeightOffsetY = -0.040f;
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.52f + VisualRideHeightOffsetY, 1.82f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.86f, 0.38f, 0.92f);
    private const float DeformationStrength = 0.20f;
    private const float DeformationRadius = 0.22f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 0.50f;
    private const float DamageDecelerationThreshold = 500f;
    // Keep visual crumpling responsive at the previous ~12.6 km/h threshold
    // while mechanical condition damage uses the tougher 18 km/h threshold above.
    private const float VisualDamageImpactThresholdMps = 3.5f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.09f, -0.13f);
    private const float FrontTrack = 1.679f;
    private const float RearTrack = 1.652f;
    // Final visually verified positions from the SF90 prefab. Do not derive
    // these from the nominal wheelbase: the imported model origin is offset.
    private const float CalibratedFrontWheelZ = 1.166f;
    private const float CalibratedRearWheelZ = -1.547f;
    private static readonly Dictionary<string, Vector3> WheelPlacementOverrides =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f, 0.34325f, CalibratedFrontWheelZ) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f, 0.34325f, CalibratedFrontWheelZ) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f, 0.34850f, CalibratedRearWheelZ) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f, 0.34850f, CalibratedRearWheelZ) },
            { "FerrariSF90SpiderWheelFrontLeft", new Vector3(-FrontTrack * 0.5f, 0.34325f, CalibratedFrontWheelZ) },
            { "FerrariSF90SpiderWheelFrontRight", new Vector3(FrontTrack * 0.5f, 0.34325f, CalibratedFrontWheelZ) },
            { "FerrariSF90SpiderWheelRearLeft", new Vector3(-RearTrack * 0.5f, 0.34850f, CalibratedRearWheelZ) },
            { "FerrariSF90SpiderWheelRearRight", new Vector3(RearTrack * 0.5f, 0.34850f, CalibratedRearWheelZ) },
            { "FerrariSF90SpiderFixedCaliperFrontLeft", new Vector3(-FrontTrack * 0.5f, 0.34325f, CalibratedFrontWheelZ) },
            { "FerrariSF90SpiderFixedCaliperFrontRight", new Vector3(FrontTrack * 0.5f, 0.34325f, CalibratedFrontWheelZ) },
            { "FerrariSF90SpiderFixedCaliperRearLeft", new Vector3(-RearTrack * 0.5f, 0.34850f, CalibratedRearWheelZ) },
            { "FerrariSF90SpiderFixedCaliperRearRight", new Vector3(RearTrack * 0.5f, 0.34850f, CalibratedRearWheelZ) },
        };

    private static readonly float[] SF90SpiderGears =
    {
        -3.45f, // gameplay reverse fallback; the road car reverses on the front e-motors
        0f,
        3.45f,
        2.26f,
        1.65f,
        1.29f,
        1.03f,
        0.84f,
        0.67f,
        0.48f,
    };

    private static AnimationCurve CreateSF90SpiderPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.11f, 0.10f),
            new Keyframe(0.34f, 0.46f),
            new Keyframe(0.56f, 0.68f),
            new Keyframe(0.78f, 0.88f),
            new Keyframe(0.94f, 1f),
            new Keyframe(1f, 0.97f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private readonly HashSet<int> configurationReadinessWarnings = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private int enteredVehicleActivationInstanceId;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private FerrariSF90SpiderWarehouseEntryController? warehouseExitGuardEntryController;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private GameObject? playerVehiclePrefab;
    private int cachedPlayerVehicleCount = -1;
    private bool dealerReadyLogged;
    private bool dealerReady;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;
    private UnityEngine.Object? nativeCarSleepConfig;
    private bool nativeCarSleepConfigUnavailableLogged;

    public static FerrariSF90SpiderRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<FerrariSF90SpiderRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(FerrariSF90SpiderRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<FerrariSF90SpiderRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        FerrariSF90SpiderPrivateDriverSupport.SetContext(context);
        var trafficFrequency = runtime.GetComponent<FerrariSF90SpiderTrafficFrequency>() ??
                               runtime.gameObject.AddComponent<FerrariSF90SpiderTrafficFrequency>();
        trafficFrequency.Initialize(context);
        runtime.SubscribeEvents();
        GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
        runtime.ScheduleInitialization("mod-load");
        return runtime;
    }

    public void Shutdown()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
        configuredVehicleIds.Clear();
        configurationReadinessWarnings.Clear();
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        exitedPlayerRecoveryCoroutine = null;
        StopWarehouseExitGuard();
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        nativeCarSleepConfig = null;
        nativeCarSleepConfigUnavailableLogged = false;
        FerrariSF90SpiderPrivateDriverSupport.RemoveVehicle(vehicleTypeName);
        playerVehiclePrefab = null;
        Destroy(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SubscribeEvents();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnsubscribeEvents();
        StopWarehouseExitGuard();
    }

    private void Update()
    {
        // Dealer purchases do not raise onEnterVehicle. Keep the hot path to
        // one count comparison and enumerate only after the collection changes.
        var vehicles = VehicleHelper.AllPlayerVehicles;
        var vehicleCount = vehicles?.Count ?? 0;
        if (vehicleCount == cachedPlayerVehicleCount)
            return;
        cachedPlayerVehicleCount = vehicleCount;
        ScheduleInitialization("player-vehicle-count-changed");
    }

    private void SubscribeEvents()
    {
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GameEvent.onGameEventTriggered += HandleGameEvent;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onExitVehicle += HandleVehicleExited;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onEnterBuilding += HandleBuildingEntered;
        GlobalEvents.onExitBuilding -= HandleBuildingExited;
        GlobalEvents.onExitBuilding += HandleBuildingExited;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onGameUnloaded += HandleGameUnloaded;
    }

    private void UnsubscribeEvents()
    {
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onExitBuilding -= HandleBuildingExited;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SubscribeEvents();
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeEvents();
        privateDriverRegistrationAllowed = true;
        ScheduleInitialization("game-loaded-late");
    }

    private void HandleGameUnloaded()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
        configuredVehicleIds.Clear();
        configurationReadinessWarnings.Clear();
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        exitedPlayerRecoveryCoroutine = null;
        StopWarehouseExitGuard();
        cachedPlayerVehicleCount = -1;
        dealerReadyLogged = false;
        dealerReady = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
    }

    private void HandleGameEvent(string eventName)
    {
        var developerToolsEvent =
            string.Equals(eventName, DeveloperToolsVehicleRecolorEvent,
                StringComparison.Ordinal) ||
            string.Equals(eventName, DeveloperToolsVehicleRecolorRestoredEvent,
                StringComparison.Ordinal);
        if (!string.Equals(eventName, VehicleRepainterColorRestoredEvent,
                StringComparison.Ordinal) &&
            !string.Equals(eventName, VehicleRepainterColorPreviewEvent,
                StringComparison.Ordinal) &&
            !string.Equals(eventName, VehicleRepainterColorResetEvent,
                StringComparison.Ordinal) &&
            !string.Equals(eventName, DeveloperToolsVehicleRecolorEvent,
                StringComparison.Ordinal) &&
            !string.Equals(eventName, DeveloperToolsVehicleRecolorRestoredEvent,
                StringComparison.Ordinal))
        {
            return;
        }

        if (developerToolsEvent)
        {
            RefreshDeveloperToolPaint(eventName);
            return;
        }

        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        TryConfigureVehicle(selectedVehicle);
        selectedVehicle!
            .GetComponent<FerrariSF90SpiderPaintController>()
            ?.RefreshCurrentColor(eventName);
        FerrariSF90SpiderDiagnostics.PaintInfo(
            context,
            $"FerrariSF90Spider repaint event='{eventName}' " +
            $"vehicle={selectedVehicle.GetInstanceID()} handled=true.");
    }

    private void RefreshDeveloperToolPaint(string eventName)
    {
        var vehicles = VehicleHelper.AllPlayerVehicles;
        var targetCount = 0;
        var changedCount = 0;
        if (vehicles != null)
        {
            foreach (var vehicle in vehicles)
            {
                if (!IsTargetVehicle(vehicle))
                    continue;

                targetCount++;
                TryConfigureVehicle(vehicle);
                if (vehicle!
                    .GetComponent<FerrariSF90SpiderPaintController>()
                    ?.RefreshCurrentColor(eventName, settleWhenUnchanged: false) == true)
                {
                    changedCount++;
                }
            }
        }

        FerrariSF90SpiderDiagnostics.PaintInfo(
            context,
            $"FerrariSF90Spider developer recolor event='{eventName}' " +
            $"targets={targetCount}, changed={changedCount}.");
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        if (IsTargetVehicle(vehicle) && !TryConfigureVehicle(vehicle))
            ScheduleInitialization("vehicle-entered-fallback");
        vehicle?.GetComponent<FerrariSF90SpiderGlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle == null || !IsTargetVehicle(vehicle))
            return;
        vehicle.GetComponent<FerrariSF90SpiderPaintController>()
            ?.RestoreAfterVehicleEntered();
        ScheduleEnteredVehicleActivation(vehicle);
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = StartCoroutine(RecoverPlayerNavMeshAfterExit(vehicle));
    }

    private IEnumerator ActivateEnteredVehicle(VehicleController vehicle)
    {
        // Native entry owns the dealer display-to-player physics transition.
        // Only recover a dormant powertrain after that transition has settled;
        // forcing SetFreeze(false) here caused the low car to spring out of the
        // ground and oscillate on its rear suspension.
        yield return new WaitForSecondsRealtime(0.25f);
        var physics = vehicle == null
            ? null
            : vehicle.GetComponent<PhysicsVehicle>() ??
              vehicle.GetComponentInChildren<PhysicsVehicle>(true);
        if (physics == null)
        {
            context?.Logger.Warn(
                $"FerrariSF90Spider: post-entry drivetrain unavailable vehicle={vehicle?.GetInstanceID()}.");
            enteredVehicleActivationCoroutine = null;
            enteredVehicleActivationInstanceId = 0;
            yield break;
        }

        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        for (var attempt = 1; attempt <= EngineStartAttemptCount; attempt++)
        {
            if (vehicle == null || !vehicle.controlledByPlayer || !IsTargetVehicle(vehicle))
                break;
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            if (engine.IsRunning && engine.ignition && engine.canRun &&
                rpm >= MinimumHealthyEngineRpm)
            {
                if (transmission.Gear == 0)
                {
                    transmission.ShiftInto(1, true);
                    yield return new WaitForFixedUpdate();
                }
                FerrariSF90SpiderDiagnostics.Info(context,
                    $"FerrariSF90Spider: post-entry drivetrain ready vehicle={vehicle.GetInstanceID()} " +
                    $"attempt={attempt} running={engine.IsRunning} rpm={rpm:0} gear={transmission.Gear}.");
                enteredVehicleActivationCoroutine = null;
                enteredVehicleActivationInstanceId = 0;
                yield break;
            }

            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(0.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
                break;
            engine.StartEngine();
            yield return new WaitForSecondsRealtime(0.75f);
            if (vehicle != null && vehicle.controlledByPlayer)
                transmission.ShiftInto(1, true);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        var finalRpm = engine.RPMPercent * engine.revLimiterRPM;
        context?.Logger.Warn(
            $"FerrariSF90Spider: post-entry drivetrain remained unavailable " +
            $"vehicle={vehicle?.GetInstanceID()} controlled={vehicle?.controlledByPlayer} " +
            $"running={engine.IsRunning} rpm={finalRpm:0} gear={transmission.Gear}.");
    }

    private void ScheduleEnteredVehicleActivation(VehicleController vehicle)
    {
        if (!vehicle.controlledByPlayer &&
            !ReferenceEquals(InstanceBehavior<GameManager>.Instance?.selectedVehicle, vehicle))
        {
            FerrariSF90SpiderDiagnostics.Info(context,
                $"FerrariSF90Spider: post-entry activation skipped vehicle={vehicle.GetInstanceID()} " +
                "because it is neither controlled nor selected.");
            return;
        }

        var instanceId = vehicle.GetInstanceID();
        if (enteredVehicleActivationCoroutine != null &&
            enteredVehicleActivationInstanceId == instanceId)
            return;
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);

        enteredVehicleActivationInstanceId = instanceId;
        FerrariSF90SpiderDiagnostics.Info(context,
            $"FerrariSF90Spider: post-entry activation scheduled vehicle={instanceId} " +
            $"controlled={vehicle.controlledByPlayer} selected=" +
            $"{ReferenceEquals(InstanceBehavior<GameManager>.Instance?.selectedVehicle, vehicle)}.");
        enteredVehicleActivationCoroutine = StartCoroutine(ActivateEnteredVehicle(vehicle));
    }

    private IEnumerator RecoverPlayerNavMeshAfterExit(VehicleController exitedVehicle)
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        var playerRoot = PlayerHelper.PlayerController?.transform;
        if (playerRoot == null)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        var agents = playerRoot.GetComponentsInChildren<NavMeshAgent>(true);
        var needsRecovery = false;
        foreach (var agent in agents)
            needsRecovery |= agent != null && agent.enabled && !agent.isOnNavMesh;
        needsRecovery |= !IsPlayerExitClear(playerRoot, playerRoot.position);
        if (!needsRecovery || !TryFindClearExitPosition(playerRoot, exitedVehicle, out var target))
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        var characterControllers = playerRoot.GetComponentsInChildren<CharacterController>(true);
        var controllerStates = Array.ConvertAll(
            characterControllers,
            controller => controller != null && controller.enabled);
        var agentStates = Array.ConvertAll(agents, agent => agent != null && agent.enabled);
        try
        {
            foreach (var controller in characterControllers)
                if (controller != null) controller.enabled = false;
            foreach (var agent in agents)
                if (agent != null) agent.enabled = false;
            playerRoot.position = target;
            Physics.SyncTransforms();
        }
        finally
        {
            for (var index = 0; index < agents.Length; index++)
            {
                var agent = agents[index];
                if (agent == null)
                    continue;
                agent.enabled = agentStates[index];
                if (agent.enabled && agent.isOnNavMesh)
                {
                    agent.Warp(target);
                    agent.ResetPath();
                }
            }
            for (var index = 0; index < characterControllers.Length; index++)
                if (characterControllers[index] != null)
                    characterControllers[index].enabled = controllerStates[index];
            Physics.SyncTransforms();
        }

        FerrariSF90SpiderDiagnostics.Info(context,
            $"FerrariSF90Spider: recovered player after vehicle exit vehicle={exitedVehicle.GetInstanceID()} " +
            $"position={target}.");
        exitedPlayerRecoveryCoroutine = null;
    }

    private static bool TryFindClearExitPosition(
        Transform playerRoot,
        VehicleController exitedVehicle,
        out Vector3 target)
    {
        var vehicleTransform = exitedVehicle.transform;
        var driverMarker = FindChildTransform(vehicleTransform, "Driverside");
        var passengerMarker = FindChildTransform(vehicleTransform, "Passengerside");
        var candidates = new List<Vector3>
        {
            driverMarker != null ? driverMarker.position : vehicleTransform.position - vehicleTransform.right * 2.05f,
            passengerMarker != null ? passengerMarker.position : vehicleTransform.position + vehicleTransform.right * 2.05f,
            vehicleTransform.position - vehicleTransform.forward * 2.35f,
            vehicleTransform.position + vehicleTransform.forward * 2.35f,
            vehicleTransform.position - vehicleTransform.right * 2.05f - vehicleTransform.forward * 1.35f,
            vehicleTransform.position + vehicleTransform.right * 2.05f - vehicleTransform.forward * 1.35f,
            vehicleTransform.position - vehicleTransform.right * 2.45f + vehicleTransform.forward * 1.15f,
            vehicleTransform.position + vehicleTransform.right * 2.45f + vehicleTransform.forward * 1.15f,
        };

        foreach (var candidate in candidates)
        {
            if (!NavMesh.SamplePosition(candidate, out var hit, 1.25f, NavMesh.AllAreas))
                continue;
            var sampled = hit.position + Vector3.up * 0.05f;
            if (!IsPlayerExitClear(playerRoot, sampled))
                continue;
            target = sampled;
            return true;
        }

        target = default;
        return false;
    }

    private static bool IsPlayerExitClear(Transform playerRoot, Vector3 position)
    {
        var overlaps = Physics.OverlapCapsule(
            position + Vector3.up * 0.42f,
            position + Vector3.up * 1.55f,
            0.30f,
            ~0,
            QueryTriggerInteraction.Ignore);
        foreach (var overlap in overlaps)
        {
            if (overlap == null || overlap.transform.IsChildOf(playerRoot))
                continue;
            return false;
        }
        return true;
    }

    private static Transform? FindChildTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        return null;
    }

    private bool IsTargetVehicle(VehicleController? vehicle) =>
        vehicle != null &&
        (string.Equals(
             vehicle.vehicleInstance?.vehicleTypeName,
             vehicleTypeName,
             StringComparison.Ordinal) ||
         string.Equals(
             vehicle.vehicleType?.vehicleTypeName,
             vehicleTypeName,
             StringComparison.Ordinal));

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!dealerReady &&
            !BusinessLayoutSetHelper.loadingLayouts &&
            FerrariSF90SpiderLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
            EnsureDealerStock("dealer-entered");
    }

    private void HandleBuildingExited(Address address)
    {
        if (address == null ||
            !string.Equals(
                BuildingHelper.GetBuilding(address)?.BuildingType,
                "ba:buildingtype_warehouse",
                StringComparison.Ordinal))
        {
            return;
        }

        var vehicle = VehicleHelper.GetCurrentVehicleBase();
        if (vehicle == null || !vehicle.controlledByPlayer || !IsTargetVehicle(vehicle))
        {
            FerrariSF90SpiderDiagnostics.WarehouseInfo(
                context,
                "FerrariSF90Spider warehouse-exit: guard skipped because the current vehicle " +
                "is not a player-controlled SF90Spider.");
            return;
        }

        var entrance = FindClosestDriveInEntrance(vehicle.transform.position, out var distance);
        if (entrance == null || distance > WarehouseExitEntranceSearchRadius)
        {
            FerrariSF90SpiderDiagnostics.WarehouseInfo(
                context,
                $"FerrariSF90Spider warehouse-exit: no nearby drive-in entrance; " +
                $"vehicle={vehicle.GetInstanceID()}, distance={distance:0.00}m.");
            return;
        }

        StopWarehouseExitGuard();
        warehouseExitGuardEntryController =
            vehicle.GetComponent<FerrariSF90SpiderWarehouseEntryController>();
        warehouseExitGuardEntryController?.SuppressEntrance(
            entrance,
            "warehouse-exit-guard");
        foreach (var enterTrigger in entrance.GetComponentsInChildren<DriveInEntranceEnterTrigger>(true))
        foreach (var collider in enterTrigger.GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled || !collider.isTrigger)
                continue;

            warehouseExitGuardColliders.Add(collider);
            collider.enabled = false;
        }

        if (warehouseExitGuardColliders.Count == 0)
        {
            warehouseExitGuardEntryController?.ClearSuppressedEntrance(
                entrance,
                "no-native-entry-trigger");
            warehouseExitGuardEntryController = null;
            FerrariSF90SpiderDiagnostics.WarehouseInfo(
                context,
                $"FerrariSF90Spider warehouse-exit: entrance='{entrance.name}' had no " +
                "enabled child entry-trigger colliders.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(
            vehicle.transform.position - entrance.transform.position,
            Vector3.up);
        if (outward.sqrMagnitude < 0.0001f)
            outward = Vector3.ProjectOnPlane(entrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < 0.0001f)
        {
            FerrariSF90SpiderDiagnostics.WarehouseInfo(
                context,
                "FerrariSF90Spider warehouse-exit: guard aborted because outward direction was zero.");
            StopWarehouseExitGuard();
            return;
        }

        outward.Normalize();
        var startingProjection = Vector3.Dot(vehicle.transform.position, outward);
        Physics.SyncTransforms();
        FerrariSF90SpiderDiagnostics.WarehouseInfo(
            context,
            $"FerrariSF90Spider warehouse-exit: guard started vehicle={vehicle.GetInstanceID()}, " +
            $"entrance='{entrance.name}', entranceDistance={distance:0.00}m, " +
            $"triggerCount={warehouseExitGuardColliders.Count}, outward={outward}, " +
            $"clearDistance={WarehouseExitGuardClearDistance:0.00}m, " +
            $"timeout={WarehouseExitGuardDuration:0.0}s.");
        warehouseExitGuardCoroutine = StartCoroutine(GuardWarehouseExit(
            vehicle,
            outward,
            startingProjection));
    }

    private IEnumerator GuardWarehouseExit(
        VehicleController vehicle,
        Vector3 outward,
        float startingProjection)
    {
        var expiresAt = Time.unscaledTime + WarehouseExitGuardDuration;
        var reason = "timeout";
        while (vehicle != null && vehicle.controlledByPlayer &&
               Time.unscaledTime < expiresAt)
        {
            if (Vector3.Dot(vehicle.transform.position, outward) >=
                startingProjection + WarehouseExitGuardClearDistance)
            {
                reason = "moved-away";
                break;
            }

            yield return new WaitForFixedUpdate();
        }

        if (vehicle == null)
            reason = "vehicle-destroyed";
        else if (!vehicle.controlledByPlayer)
            reason = "player-left-vehicle";

        FerrariSF90SpiderDiagnostics.WarehouseInfo(
            context,
            $"FerrariSF90Spider warehouse-exit: guard ending reason={reason}.");
        RestoreWarehouseExitTriggers(reason);
    }

    private void StopWarehouseExitGuard()
    {
        if (warehouseExitGuardCoroutine != null)
            StopCoroutine(warehouseExitGuardCoroutine);
        RestoreWarehouseExitTriggers("cancelled-or-reset");
    }

    private void RestoreWarehouseExitTriggers(string reason)
    {
        if (warehouseExitGuardColliders.Count > 0)
        {
            FerrariSF90SpiderDiagnostics.WarehouseInfo(
                context,
                $"FerrariSF90Spider warehouse-exit: restoring " +
                $"triggerCount={warehouseExitGuardColliders.Count}, reason={reason}.");
        }

        foreach (var collider in warehouseExitGuardColliders)
        {
            if (collider != null)
                collider.enabled = true;
        }

        warehouseExitGuardColliders.Clear();
        warehouseExitGuardEntryController?.ClearSuppressedEntrance(null, reason);
        warehouseExitGuardEntryController = null;
        warehouseExitGuardCoroutine = null;
        Physics.SyncTransforms();
    }

    private static DriveInEntrance? FindClosestDriveInEntrance(
        Vector3 vehiclePosition,
        out float distance)
    {
        DriveInEntrance? nearest = null;
        var nearestDistanceSquared = float.PositiveInfinity;
        foreach (var entrance in FindObjectsOfType<DriveInEntrance>(true))
        {
            if (entrance == null)
                continue;

            var distanceSquared = (entrance.transform.position - vehiclePosition).sqrMagnitude;
            if (distanceSquared >= nearestDistanceSquared)
                continue;

            nearest = entrance;
            nearestDistanceSquared = distanceSquared;
        }

        distance = nearest == null
            ? float.PositiveInfinity
            : Mathf.Sqrt(nearestDistanceSquared);
        return nearest;
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (isOpen && !dealerReady && !BusinessLayoutSetHelper.loadingLayouts)
            EnsureDealerStock("full-menu");
        if (isOpen && privateDriverRegistrationAllowed &&
            (!privateDriverReady || !privateDriverPoolReady))
            EnsurePrivateDriverSupport("full-menu");
    }

    private void ScheduleInitialization(string source)
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(InitializeForLifecycle(source));
    }

    private IEnumerator InitializeForLifecycle(string source)
    {
        var previousMatchedCount = -1;
        var stablePasses = 0;
        var maximumMatchedCount = 0;

        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            if (!privateDriverPoolReady && playerVehiclePrefab != null)
                privateDriverPoolReady = TryPreparePrivateDriverPool(source);
            if (!dealerReady)
                dealerReady = EnsureDealerStock(source);
            if (privateDriverRegistrationAllowed && !privateDriverReady)
                EnsurePrivateDriverSupport(source);
            ConfigureExistingVehicles(out var matchedCount);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);

            var servicesReady = dealerReady && privateDriverPoolReady &&
                                (!privateDriverRegistrationAllowed || privateDriverReady);
            if (servicesReady && matchedCount == previousMatchedCount)
                stablePasses++;
            else
                stablePasses = 0;
            previousMatchedCount = matchedCount;

            if (servicesReady && stablePasses >= RequiredStablePasses)
                break;
            if (attempt < InitializationRetryCount)
                yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        initializationCoroutine = null;
        if (!dealerReady)
        {
            context?.Logger.Warn(
                $"FerrariSF90Spider: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
        if (privateDriverRegistrationAllowed && !privateDriverReady)
            context?.Logger.Warn(
                $"FerrariSF90Spider: private-driver contracts not ready source='{source}'.");
        if (!privateDriverPoolReady)
            context?.Logger.Warn(
                $"FerrariSF90Spider: private-driver traffic pool not ready source='{source}'.");
    }

    private bool EnsurePrivateDriverSupport(string source)
    {
        if (privateDriverReady && privateDriverPoolReady)
            return true;
        if (!privateDriverRegistrationAllowed || playerVehiclePrefab == null)
            return false;

        try
        {
            if (!privateDriverPoolReady)
                privateDriverPoolReady = TryPreparePrivateDriverPool(source);
            if (!privateDriverReady)
            {
                privateDriverReady =
                    FerrariSF90SpiderPrivateDriverSupport.EnsureVehicleAvailable(
                        vehicleTypeName);
            }
            if (privateDriverReady)
                FerrariSF90SpiderDiagnostics.Info(context,
                    $"FerrariSF90Spider: private-driver support registered source='{source}'.");
            return privateDriverReady && privateDriverPoolReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"FerrariSF90Spider: private-driver registration failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool TryPreparePrivateDriverPool(string source)
    {
        if (playerVehiclePrefab == null)
            return false;
        try
        {
            return FerrariSF90SpiderPrivateDriverSupport.PrepareTrafficPool(
                playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                privateDriverPreparationExceptionLogged = true;
                context?.Logger.Warn(
                    $"FerrariSF90Spider: private-driver pool preparation failed source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
            return false;
        }
    }

    private bool EnsureDealerStock(string source)
    {
        if (dealerReady)
            return true;
        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;
        try
        {
            var ready = FerrariSF90SpiderLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                FerrariSF90SpiderDiagnostics.Info(context,
                    $"FerrariSF90Spider: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"FerrariSF90Spider: dealer stock update failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool ConfigureSleepEnvironment(VehicleController vehicle)
    {
        try
        {
            var environmentField = FindField(typeof(VehicleController), "sleepEnvironment");
            var environment = environmentField?.GetValue(vehicle);
            if (environmentField == null || environment == null)
                return false;

            var configField = FindField(environment.GetType(), "config");
            if (configField == null)
                return false;
            if (configField.GetValue(environment) is UnityEngine.Object currentConfig &&
                currentConfig != null)
                return IsCarSleepConfig(currentConfig);

            var carConfig = ResolveNativeCarSleepConfig();
            if (carConfig == null)
            {
                if (!nativeCarSleepConfigUnavailableLogged)
                {
                    nativeCarSleepConfigUnavailableLogged = true;
                    context?.Logger.Warn(
                        "FerrariSF90Spider: native car sleep configuration was unavailable; " +
                        "sleeping in this vehicle will remain disabled.");
                }
                return false;
            }

            configField.SetValue(environment, carConfig);
            environmentField.SetValue(vehicle, environment);
            FerrariSF90SpiderDiagnostics.Info(context,
                $"FerrariSF90Spider: configured native car sleep environment " +
                $"vehicle={vehicle.GetInstanceID()} donor=HonzaMimic.");
            return true;
        }
        catch (Exception exception)
        {
            if (!nativeCarSleepConfigUnavailableLogged)
            {
                nativeCarSleepConfigUnavailableLogged = true;
                context?.Logger.Warn(
                    "FerrariSF90Spider: could not configure the native car sleep environment: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
            return false;
        }
    }

    private UnityEngine.Object? ResolveNativeCarSleepConfig()
    {
        if (nativeCarSleepConfig != null)
            return nativeCarSleepConfig;

        var donor = PrefabHelper.LoadPrefabAssetByName(NativeCarSleepDonorPrefabPath);
        var donorVehicle = donor?.GetComponent<VehicleController>() ??
                           donor?.GetComponentInChildren<VehicleController>(true);
        if (donorVehicle == null)
            return null;

        var environmentField = FindField(typeof(VehicleController), "sleepEnvironment");
        var donorEnvironment = environmentField?.GetValue(donorVehicle);
        var configField = donorEnvironment == null
            ? null
            : FindField(donorEnvironment.GetType(), "config");
        var candidate = configField?.GetValue(donorEnvironment) as UnityEngine.Object;
        if (candidate == null || !IsCarSleepConfig(candidate))
            return null;

        nativeCarSleepConfig = candidate;
        return nativeCarSleepConfig;
    }

    private static bool IsCarSleepConfig(UnityEngine.Object candidate)
    {
        var typeField = FindField(candidate.GetType(), "sleepEnvironmentType");
        var typeValue = typeField?.GetValue(candidate);
        return typeValue != null && Convert.ToInt32(typeValue) == 1;
    }

    private void ConfigureExistingVehicles(out int matchedCount)
    {
        matchedCount = 0;
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
            return;

        foreach (var vehicle in vehicles)
        {
            if (!IsTargetVehicle(vehicle))
                continue;

            matchedCount++;
            TryConfigureVehicle(vehicle);
        }
    }

    private static bool HasVehicleVisualsReady(GameObject root)
    {
        var wheelControllers = 0;
        var hasNormalBody = false;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            if (IsSf90BodyPaintFilter(filter))
                hasNormalBody = true;
        }
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.EndsWith("_WheelController", StringComparison.Ordinal))
                wheelControllers++;
        }
        return wheelControllers >= 4 && hasNormalBody;
    }

    internal static bool IsSf90BodyPaintFilter(MeshFilter filter)
    {
        if (filter.name.IndexOf("body:Paint_Geo_lodA", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null)
            return false;
        foreach (var material in renderer.sharedMaterials)
            if (material != null &&
                material.name.IndexOf("2021Paint_Material", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private bool TryConfigureVehicle(VehicleController? vehicle)
    {
        if (!IsTargetVehicle(vehicle) || vehicle == null)
            return false;

        // Configure this before requiring a saved instance: developer-tool
        // spawns can be unsaved but must still expose the native Car sleep action.
        var sleepConfigured = ConfigureSleepEnvironment(vehicle);
        if (vehicle.vehicleInstance == null)
            return sleepConfigured;

        var instanceId = vehicle.GetInstanceID();
        if (configuredVehicleIds.Contains(instanceId))
            return true;

        try
        {
            if (!HasVehicleVisualsReady(vehicle.gameObject))
            {
                if (configurationReadinessWarnings.Add(instanceId))
                    context?.Logger.Warn(
                        $"FerrariSF90Spider: dealer vehicle instance={instanceId} is still settling; " +
                        "deferring model-dependent setup until wheels and the normal shell exist.");
                return false;
            }

            var rigidbody = vehicle.GetComponent<Rigidbody>() ?? vehicle.GetComponentInParent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.mass = VehicleMass;
                rigidbody.centerOfMass = StableCenterOfMass;
                rigidbody.drag = 0f;
                rigidbody.angularDrag = 1.45f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.solverIterations = Mathf.Max(rigidbody.solverIterations, 12);
                rigidbody.solverVelocityIterations =
                    Mathf.Max(rigidbody.solverVelocityIterations, 4);

                var highwaySeamGuard =
                    vehicle.GetComponent<FerrariSF90SpiderHighwaySeamGuard>();
                if (highwaySeamGuard == null)
                {
                    highwaySeamGuard = vehicle.gameObject
                        .AddComponent<FerrariSF90SpiderHighwaySeamGuard>();
                }
                highwaySeamGuard.Initialize(rigidbody);
            }

            ConfigureMassProperties(vehicle.gameObject);
            var wheelPlacements = ConfigureWheelPlacements(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            var contactMaterialOwner =
                vehicle.GetComponent<FerrariSF90SpiderContactMaterialOwner>();
            if (contactMaterialOwner == null)
            {
                contactMaterialOwner = vehicle.gameObject
                    .AddComponent<FerrariSF90SpiderContactMaterialOwner>();
            }
            ConfigureBodyColliders(
                vehicle.gameObject,
                contactMaterialOwner.GetOrCreateMaterial());
            var settlingController = vehicle.GetComponent<FerrariSF90SpiderSettlingController>();
            if (settlingController == null)
                settlingController = vehicle.gameObject.AddComponent<FerrariSF90SpiderSettlingController>();
            settlingController.Initialize(vehicle, context);
            var disabledFallbackChassis = DisableFallbackChassis(vehicle.gameObject);
            var disabledBonnetCamera = DisableBonnetCameraGeometry(vehicle.gameObject);
            ConfigureExitMarkers(vehicle.gameObject);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(vehicle.gameObject);
            var warehouseBounds =
                vehicle.GetComponent<FerrariSF90SpiderWarehouseBoundsController>();
            if (warehouseBounds == null)
            {
                warehouseBounds = vehicle.gameObject
                    .AddComponent<FerrariSF90SpiderWarehouseBoundsController>();
            }
            warehouseBounds.Initialize();
            var warehouseEntry =
                vehicle.GetComponent<FerrariSF90SpiderWarehouseEntryController>();
            if (warehouseEntry == null)
            {
                warehouseEntry = vehicle.gameObject
                    .AddComponent<FerrariSF90SpiderWarehouseEntryController>();
            }
            warehouseEntry.Initialize(vehicle, context);
            var repairedBodyShell = UseAuthoredBodyShell(vehicle.gameObject);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var caliperController = vehicle.GetComponent<FerrariSF90SpiderCaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<FerrariSF90SpiderCaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialResult = FerrariSF90SpiderMaterials.FixSolidMaterials(vehicle.gameObject);
            var glassController = vehicle.GetComponent<FerrariSF90SpiderGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<FerrariSF90SpiderGlassController>();
            glassController.Initialize(context);
            var lightingController = vehicle.GetComponent<FerrariSF90SpiderLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<FerrariSF90SpiderLightingController>();
            lightingController.Initialize(vehicle, context);
            // Capture the lighting overlays as well as the painted shell so
            // bumper inserts, lamp details, grilles, and aero cannot remain
            // rigid or float in front of a deformed end section.
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var driverController = vehicle.GetComponent<FerrariSF90SpiderDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<FerrariSF90SpiderDriverController>();
            driverController.Initialize(vehicle, context);
            var paintController = vehicle.GetComponent<FerrariSF90SpiderPaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<FerrariSF90SpiderPaintController>();
            paintController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<FerrariSF90SpiderAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<FerrariSF90SpiderAudioController>();
            audioController.Initialize(vehicle, context);
            var accelerationTelemetry =
                vehicle.GetComponent<FerrariSF90SpiderAccelerationTelemetry>();
            if (accelerationTelemetry == null)
            {
                accelerationTelemetry = vehicle.gameObject
                    .AddComponent<FerrariSF90SpiderAccelerationTelemetry>();
            }
            accelerationTelemetry.Initialize(vehicle, context);

            if (wheelPlacements < 12 || !repairedBodyShell || deformableBodyMeshes == 0 ||
                !powertrainConfigured || !sleepConfigured)
            {
                context?.Logger.Warn(
                    $"FerrariSF90Spider: vehicle instance={instanceId} setup incomplete; " +
                    $"sleep={sleepConfigured}, wheels={wheelPlacements}/12, " +
                    $"bodyShell={repairedBodyShell}, deformable={deformableBodyMeshes}, " +
                    $"powertrain={powertrainConfigured}. Retrying on the lifecycle pass.");
                return false;
            }

            configuredVehicleIds.Add(instanceId);

            // Dealer purchases can create an already-entered car without
            // raising onEnterVehicle. Schedule the same bounded powertrain
            // handoff once when that freshly configured instance is selected.
            if (vehicle.controlledByPlayer &&
                ReferenceEquals(InstanceBehavior<GameManager>.Instance?.selectedVehicle, vehicle))
            {
                ScheduleEnteredVehicleActivation(vehicle);
            }

            FerrariSF90SpiderDiagnostics.Info(context,
                $"FerrariSF90Spider: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=8-speed-DCT, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"sleepEnvironmentConfigured={sleepConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"wheelPlacements={wheelPlacements}/12, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstacles={normalizedNavMeshObstacles}, " +
                $"disabledFallbackChassis={disabledFallbackChassis}, " +
                $"disabledBonnetCamera={disabledBonnetCamera}, " +
                $"authoredBodyShell={repairedBodyShell}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=sf90-baseline, brakeTorque={BrakeTorque:0}, steeringCalipers=4, " +
                $"materialRenderers={materialResult.RendererCount}, " +
                $"decalMasksCleared={materialResult.DecalMasksCleared}, " +
                $"opaqueFixed={materialResult.OpaqueMaterialsFixed}, " +
                $"transparentFixed={materialResult.TransparentMaterialsFixed}, " +
                $"cabinGlass={materialResult.CabinGlassRenderers}/" +
                $"reenabled={materialResult.CabinGlassRenderersReenabled}, " +
                $"rimSlotsNormalized={materialResult.RimSlotsNormalized}, " +
                $"hdrpValidated={materialResult.MaterialsValidated}.");
            configurationReadinessWarnings.Remove(instanceId);
            return true;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"FerrariSF90Spider: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            var isFront = transform.name.StartsWith("Front", StringComparison.Ordinal);
            var isRear = transform.name.StartsWith("Rear", StringComparison.Ordinal);
            if ((!isFront && !isRear) ||
                !transform.name.EndsWith("_WheelController", StringComparison.Ordinal))
                continue;

            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                var spring = GetMember(component, "spring");
                SetFloat(
                    spring,
                    "maxLength",
                    isFront ? FrontSuspensionTravel : RearSuspensionTravel);
                SetFloat(spring, "maxForce", 20500f);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", isFront ? 0.3433f : 0.3485f);
                SetFloat(wheel, "width", isFront ? 0.255f : 0.315f);
                var forwardFriction = GetMember(component, "forwardFriction");
                SetFloat(
                    forwardFriction,
                    "grip",
                    isFront ? FrontLongitudinalGrip : RearLongitudinalGrip);
                SetFloat(component, "frictionCircleStrength", TireFrictionCircleStrength);
            }
        }
    }

    private static int ConfigureWheelPlacements(GameObject root)
    {
        var configured = 0;
        foreach (var pair in WheelPlacementOverrides)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(transform.name, pair.Key, StringComparison.Ordinal))
                    continue;

                transform.localPosition = pair.Value;
                configured++;
                break;
            }
        }

        return configured;
    }

    private static void ConfigureMassProperties(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || GetMember(component, "centerOfMass") is not Vector3)
                continue;
            SetBool(component, "useDefaultCenterOfMass", false);
            SetVector3(component, "centerOfMass", StableCenterOfMass);
            SetVector3(component, "combinedCenterOfMass", StableCenterOfMass);
            SetFloat(component, "baseMass", VehicleMass);
            SetFloat(component, "combinedMass", VehicleMass);
        }
    }

    private static void ConfigureBodyColliders(GameObject root, PhysicMaterial contactMaterial)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BodyCollider", StringComparison.Ordinal))
                continue;

            var colliders = transform.GetComponents<BoxCollider>();
            if (colliders.Length > 0)
            {
                colliders[0].center = new Vector3(0f, 0.36f + VisualRideHeightOffsetY, 0f);
                colliders[0].size = new Vector3(1.93f, 0.44f, 4.66f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 0.73f + VisualRideHeightOffsetY, -0.16f);
                colliders[1].size = new Vector3(1.68f, 0.58f, 2.46f);
            }

            // The SF90Spider's low wedge nose can otherwise pass below a taller
            // vehicle's body collider and trap the two vehicles together.
            // This fitted bridge closes that vertical gap without extending
            // the collision footprint beyond the visible front bodywork.
            var frontContactCollider = colliders.Length > 2
                ? colliders[2]
                : transform.gameObject.AddComponent<BoxCollider>();
            frontContactCollider.center = FrontContactColliderCenter;
            frontContactCollider.size = FrontContactColliderSize;
            frontContactCollider.isTrigger = false;
            frontContactCollider.enabled = true;

            foreach (var collider in transform.GetComponents<BoxCollider>())
                collider.sharedMaterial = contactMaterial;
        }
    }

    private static int DisableFallbackChassis(GameObject root)
    {
        var disabled = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.IndexOf("LOD_B_CHASSIS", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            transform.gameObject.SetActive(false);
            foreach (var renderer in transform.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            disabled++;
        }
        return disabled;
    }

    private static int DisableBonnetCameraGeometry(GameObject root)
    {
        var disabled = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.IndexOf("BONNETCAM", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            transform.gameObject.SetActive(false);
            foreach (var renderer in transform.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            disabled++;
        }

        return disabled;
    }

    private bool UseAuthoredBodyShell(GameObject root)
    {
        MeshFilter? damageFilter = null;
        MeshRenderer? damageRenderer = null;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!string.Equals(filter.name, "FerrariSF90SpiderDamageBody", StringComparison.Ordinal))
                continue;
            damageFilter = filter;
            damageRenderer = filter.GetComponent<MeshRenderer>();
            break;
        }

        MeshRenderer? sourceRenderer = null;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || !IsSf90BodyPaintFilter(filter) ||
                (damageFilter != null && renderer.transform.IsChildOf(damageFilter.transform)))
                continue;
            sourceRenderer = renderer;
            break;
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
        {
            context?.Logger.Warn(
                $"FerrariSF90Spider body vehicle={root.GetInstanceID()}: " +
                "SF90 body paint shell was not found.");
            return false;
        }

        // Keep deformation on the authored body in its original transform.
        // A second root-local shell cannot remain aligned with this imported
        // hierarchy and was the source of the upside-down duplicate chassis.
        sourceRenderer.enabled = sourceRenderer.sharedMaterials.Length > 0;
        if (damageRenderer != null)
            damageRenderer.enabled = false;
        FerrariSF90SpiderDiagnostics.Info(context,
            $"FerrariSF90Spider body vehicle={root.GetInstanceID()}: using authored in-place shell " +
            $"visible={sourceRenderer.enabled}, mesh='{sourceFilter.sharedMesh.name}', " +
            $"materials={sourceRenderer.sharedMaterials.Length}; duplicate damage shell disabled.");
        return sourceRenderer.enabled;
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        const float driverSide = -1.45f;
        const float passengerSide = 1.45f;
        var configured = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.name, "Driverside", StringComparison.Ordinal))
            {
                transform.localPosition = new Vector3(driverSide, 0.1f, 0f);
                configured++;
            }
            else if (string.Equals(transform.name, "Passengerside", StringComparison.Ordinal))
            {
                transform.localPosition = new Vector3(passengerSide, 0.1f, 0f);
                configured++;
            }
        }

        if (configured != 2)
            Debug.LogWarning($"FerrariSF90Spider: expected two exit markers, configured={configured}.");
    }

    private static int ConfigureNavMeshObstacles(GameObject root)
    {
        if (!TryGetBodyColliderBounds(root.transform, out var bodyBounds))
            return 0;

        var normalized = 0;
        foreach (var obstacle in root.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle == null || obstacle.shape != NavMeshObstacleShape.Box)
                continue;

            var obstacleTransform = obstacle.transform;
            var scale = obstacleTransform.lossyScale;
            if (Mathf.Abs(scale.x) < 0.0001f ||
                Mathf.Abs(scale.y) < 0.0001f ||
                Mathf.Abs(scale.z) < 0.0001f)
            {
                continue;
            }

            var rootTransform = root.transform;
            obstacle.center = obstacleTransform.InverseTransformPoint(
                rootTransform.TransformPoint(bodyBounds.center));
            obstacle.size = new Vector3(
                ProjectBodySizeOntoAxis(bodyBounds.size, rootTransform, obstacleTransform.right) /
                Mathf.Abs(scale.x),
                ProjectBodySizeOntoAxis(bodyBounds.size, rootTransform, obstacleTransform.up) /
                Mathf.Abs(scale.y),
                ProjectBodySizeOntoAxis(bodyBounds.size, rootTransform, obstacleTransform.forward) /
                Mathf.Abs(scale.z));
            normalized++;
        }

        return normalized;
    }

    private static bool TryGetBodyColliderBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(child.name, "BodyCollider", StringComparison.Ordinal))
                continue;

            foreach (var collider in child.GetComponents<BoxCollider>())
            {
                if (collider == null || collider.isTrigger)
                    continue;

                var halfSize = collider.size * 0.5f;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = collider.center + Vector3.Scale(
                        halfSize,
                        new Vector3(x, y, z));
                    var rootCorner = root.InverseTransformPoint(
                        collider.transform.TransformPoint(corner));
                    if (!found)
                    {
                        bounds = new Bounds(rootCorner, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(rootCorner);
                    }
                }
            }
        }

        return found;
    }

    private static float ProjectBodySizeOntoAxis(
        Vector3 bodySize,
        Transform root,
        Vector3 worldAxis)
    {
        worldAxis.Normalize();
        return Mathf.Abs(Vector3.Dot(worldAxis, root.right)) * bodySize.x +
               Mathf.Abs(Vector3.Dot(worldAxis, root.up)) * bodySize.y +
               Mathf.Abs(Vector3.Dot(worldAxis, root.forward)) * bodySize.z;
    }

    private int ConfigureVisualDamage(VehicleController vehicle)
    {
        foreach (var component in vehicle.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || !string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
                continue;
            component.enabled = false;
            ClearCollection(component, "_deformationQueue");
        }

        var damageHandler =
            vehicle.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true);
        if (damageHandler == null)
        {
            context?.Logger.Warn(
                $"FerrariSF90Spider damage vehicle={vehicle.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        var filters = new List<MeshFilter>();
        foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null ||
                !IsDeformableExterior(filter))
                continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null &&
                (renderer.enabled ||
                 filter.name.StartsWith("FerrariSF90Spider_", StringComparison.Ordinal)))
                filters.Add(filter);
        }

        if (filters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"FerrariSF90Spider damage vehicle={vehicle.GetInstanceID()}: " +
                "deformable outer body mesh is missing; visual damage remains disabled.");
            return 0;
        }

        ClearCollection(damageHandler, "_collisionEvents");
        damageHandler.collisionTimeout = 0.8f;
        damageHandler.damageIntensity = DamageIntensity;
        damageHandler.decelerationThreshold = DamageDecelerationThreshold;
        damageHandler.deformationRadius = DeformationRadius;
        damageHandler.deformationRandomness = DeformationRandomness;
        damageHandler.deformationStrength = DeformationStrength;
        damageHandler.deformationVerticesPerFrame = 8000;
        // The stock deformation controller assumes every mesh shares root-local
        // coordinates and can silently miss imported panels. The model-aware
        // controller below works in world space and only touches the outer shell.
        damageHandler.meshDeform = false;

        var impactDamage = vehicle.GetComponent<FerrariSF90SpiderImpactDamageController>();
        if (impactDamage == null)
            impactDamage = vehicle.gameObject.AddComponent<FerrariSF90SpiderImpactDamageController>();
        impactDamage.Initialize(vehicle, damageHandler, context);

        var visualDamage = vehicle.GetComponent<FerrariSF90SpiderVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<FerrariSF90SpiderVisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            VisualDamageImpactThresholdMps);

        FerrariSF90SpiderDiagnostics.Info(context,
            $"FerrariSF90Spider damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} visualThreshold={VisualDamageImpactThresholdMps:0.0}mps " +
            $"conditionThreshold={DamageDecelerationThreshold / 100f:0.0}mps " +
            $"filters=[{string.Join(", ", filters.ConvertAll(filter => filter.name))}]; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        var name = filter.name;
        if (name.StartsWith("FerrariSF90SpiderDamageBody", StringComparison.Ordinal))
            return false;
        if (name.StartsWith("FerrariSF90Spider_", StringComparison.Ordinal))
            return true;
        if (name.IndexOf("_INT_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("TYRE_mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("WHEEL_mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("ROTOR_mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("BRAKE_CALIPER", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null || !FerrariSF90SpiderMaterials.IsFerrariRenderer(renderer.transform))
            return false;
        foreach (var parent in filter.GetComponentsInParent<Transform>(true))
        {
            if (parent.name.StartsWith("FerrariSF90SpiderWheel", StringComparison.Ordinal) ||
                parent.name.StartsWith("FerrariSF90SpiderFixedCaliper", StringComparison.Ordinal))
                return false;
        }

        // The authored paint shell is the actual visible SF90 body. V17 only
        // admitted end-zone attachments here, so damage could increase while
        // the main yellow/painted body stayed visually rigid. Always own the
        // paint shell explicitly before applying the narrower attachment rules.
        if (IsSf90BodyPaintFilter(filter))
            return true;

        var explicitImpactAttachment =
            name.IndexOf("Light_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("FerrariSF90Spider_Light_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Badge_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("ManufacturerPlate_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Grille", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Base_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Carbon", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("Coloured_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("FRONTBUMPER", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("WING_REAR", StringComparison.OrdinalIgnoreCase) >= 0;
        if (explicitImpactAttachment)
            return true;

        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;
            var materialName = material.name;
            if (materialName.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                materialName.IndexOf("Wheel1A_3D_3DWheel1C_Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                materialName.IndexOf("CallipersCalliperA_Zone_Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                materialName.IndexOf("EngineA_Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                FerrariSF90SpiderMaterials.IsCabinGlassMaterial(material))
                return false;
        }

        // Imported end assemblies use generic carbon/plastic materials. Their
        // world bounds are the reliable ownership signal for the front and
        // rear deformation zones, including the bumper pieces behind the paint.
        var vehicle = filter.GetComponentInParent<VehicleController>();
        if (vehicle != null)
        {
            var localCenter = vehicle.transform.InverseTransformPoint(renderer.bounds.center);
            if (Mathf.Abs(localCenter.z) >= 1.25f)
                return true;
        }

        return name.IndexOf("Vehicle_Exterior", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Light_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Tail", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Brake", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Bumper", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Rear", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Carbon", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Coloured_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Plastic", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void ClearCollection(object target, string fieldName)
    {
        var collection = FindField(target.GetType(), fieldName)?.GetValue(target);
        collection?.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(collection, null);
    }

    private static bool ConfigurePowertrain(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null ||
                !string.Equals(
                    component.GetType().FullName,
                    "NWH.VehiclePhysics2.VehicleController",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var powertrain = GetMember(component, "powertrain");
            var clutch = GetMember(powertrain, "clutch");
            SetFloat(clutch, "engagementRPM", ClutchEngagementRpm);
            SetFloat(clutch, "throttleEngagementOffsetRPM", ClutchThrottleOffsetRpm);
            SetFloat(clutch, "engagementRange", ClutchEngagementRange);
            SetFloat(clutch, "creepTorque", ClutchCreepTorque);
            SetFloat(clutch, "creepSpeedLimit", 1f);
            var engine = GetMember(powertrain, "engine");
            SetFloat(engine, "inertia", EngineInertia);
            SetFloat(engine, "maxPower", EnginePowerKw);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateSF90SpiderPowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", false);
            SetFloat(forcedInduction, "powerGainMultiplier", 1f);
            SetFloat(forcedInduction, "spoolUpTime", 0f);

            var brakes = GetMember(component, "brakes");
            SetFloat(brakes, "maxTorque", BrakeTorque);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.065f);
            SetFloat(transmission, "_downshiftRPM", 4300f);
            SetFloat(transmission, "_upshiftRPM", 7500f);
            SetInt(transmission, "forwardGearCount", 8);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", SF90SpiderGears);

            if (GetMember(powertrain, "wheelGroups") is IList wheelGroups)
            {
                foreach (var wheelGroup in wheelGroups)
                    SetFloat(wheelGroup, "antiRollBarForce", AntiRollBarForce);
            }

            foreach (var other in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (other != null &&
                    string.Equals(other.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
                {
                    var module = GetMember(other, "module");
                    SetFloat(module, "speedLimit", SpeedLimitKph);
                }
            }

            return GetInt(transmission, "forwardGearCount") == 8;
        }

        return false;
    }

    private static object? GetMember(object? target, string name)
    {
        if (target == null)
            return null;

        var field = FindField(target.GetType(), name);
        if (field != null)
            return field.GetValue(target);

        var property = FindProperty(target.GetType(), name);
        return property?.GetValue(target, null);
    }

    private static void SetFloat(object? target, string name, float value)
    {
        SetValue(target, name, typeof(float), value);
    }

    private static void SetInt(object? target, string name, int value)
    {
        if (!SetValue(target, name, typeof(int), value))
        {
            var field = target == null ? null : FindField(target.GetType(), name);
            if (field?.FieldType.IsEnum == true)
                field.SetValue(target, Enum.ToObject(field.FieldType, value));
        }
    }

    private static void SetBool(object? target, string name, bool value)
    {
        SetValue(target, name, typeof(bool), value);
    }

    private static void SetVector3(object? target, string name, Vector3 value)
    {
        SetValue(target, name, typeof(Vector3), value);
    }

    private static int GetInt(object? target, string name)
    {
        var value = GetMember(target, name);
        return value == null ? 0 : Convert.ToInt32(value);
    }

    private static bool SetValue(object? target, string name, Type expectedType, object value)
    {
        if (target == null)
            return false;

        var field = FindField(target.GetType(), name);
        if (field != null && field.FieldType == expectedType)
        {
            field.SetValue(target, value);
            return true;
        }

        var property = FindProperty(target.GetType(), name);
        if (property != null && property.CanWrite && property.PropertyType == expectedType)
        {
            property.SetValue(target, value, null);
            return true;
        }

        return false;
    }

    private static void SetFloatArray(object? target, string name, float[] values)
    {
        if (target == null)
            return;

        var field = FindField(target.GetType(), name);
        if (field == null)
            return;

        if (field.FieldType == typeof(float[]))
        {
            field.SetValue(target, (float[])values.Clone());
            return;
        }

        if (!(field.GetValue(target) is IList list))
            return;
        list.Clear();
        foreach (var value in values)
            list.Add(value);
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }

        return null;
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var property = current.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (property != null)
                return property;
        }

        return null;
    }
}

[AddComponentMenu("")]
internal sealed class FerrariSF90SpiderContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        contactMaterial = new PhysicMaterial("Ferrari SF90 Spider body contact")
        {
            dynamicFriction = 0.05f,
            staticFriction = 0.05f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum,
        };
        return contactMaterial;
    }

    private void OnDestroy()
    {
        if (contactMaterial != null)
            Destroy(contactMaterial);
        contactMaterial = null;
    }
}

[AddComponentMenu("")]
public sealed class FerrariSF90SpiderRimGeometryController : MonoBehaviour
{
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private bool initialized;

    internal int Initialize(ModContext? context)
    {
        if (initialized)
            return runtimeMeshes.Count;

        initialized = true;
        var mirrored = 0;
        mirrored += MirrorRightMesh("Wheel_FR_Rim_0", "Wheel_FL_Rim_0");
        mirrored += MirrorRightMesh("Wheel_BR_Rim_0", "Wheel_BL_Rim_0");
        mirrored += MirrorRightMesh("Wheel_FR_Caliper_0", "Wheel_FL_Caliper_0");
        mirrored += MirrorRightMesh("Wheel_BR_Caliper_0", "Wheel_BL_Caliper_0");
        mirrored += MirrorRightMesh("Wheel_FR_Tire_0", "Wheel_FL_Tire_0");
        mirrored += MirrorRightMesh("Wheel_BR_Tire_0", "Wheel_BL_Tire_0");
        mirrored += MirrorRightMesh("Wheel_FR_Brake_rotor_0", "Wheel_FL_Brake_rotor_0");
        mirrored += MirrorRightMesh("Wheel_BR_Brake_rotor_0", "Wheel_BL_Brake_rotor_0");
        mirrored += MirrorRightMesh("Wheel_FR_Logo_0", "Wheel_FL_Logo_0");
        mirrored += MirrorRightMesh("Wheel_BR_Logo_0", "Wheel_BL_Logo_0");
        if (mirrored != 10)
        {
            context?.Logger.Warn(
                $"FerrariSF90Spider wheel finish vehicle={GetInstanceID()}: mirrored " +
                $"{mirrored}/10 left-side wheel meshes; a mesh pair is missing.");
        }
        else
        {
            FerrariSF90SpiderDiagnostics.Info(context,
                $"FerrariSF90Spider wheel finish vehicle={GetInstanceID()}: complete left " +
                "wheel assemblies rebuilt as exact mirrors of the preferred right-side geometry.");
        }
        return mirrored;
    }

    private int MirrorRightMesh(string rightName, string leftName)
    {
        MeshFilter? right = null;
        MeshFilter? left = null;
        foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (string.Equals(filter.name, rightName, StringComparison.Ordinal))
                right = filter;
            else if (string.Equals(filter.name, leftName, StringComparison.Ordinal))
                left = filter;
        }
        if (right?.sharedMesh == null || left == null)
            return 0;

        var mirroredMesh = Instantiate(right.sharedMesh);
        mirroredMesh.name = leftName + "_MirroredFromRight";
        var rightToRoot = transform.worldToLocalMatrix * right.transform.localToWorldMatrix;
        var rootToLeft = left.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        var rightToMirroredLeft =
            rootToLeft * Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) * rightToRoot;
        var vertices = mirroredMesh.vertices;
        var normals = mirroredMesh.normals;
        var tangents = mirroredMesh.tangents;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = rightToMirroredLeft.MultiplyPoint3x4(vertices[index]);
        mirroredMesh.vertices = vertices;

        // Preserve the preferred right-side authored smoothing exactly. A
        // recalculation produces subtly different highlights even when geometry
        // and materials match, which made the left wheels look washed out.
        var normalTransform = rightToMirroredLeft.inverse.transpose;
        for (var index = 0; index < normals.Length; index++)
            normals[index] = normalTransform.MultiplyVector(normals[index]).normalized;
        mirroredMesh.normals = normals;
        var handedness = rightToMirroredLeft.determinant < 0f ? -1f : 1f;
        for (var index = 0; index < tangents.Length; index++)
        {
            var direction = rightToMirroredLeft.MultiplyVector(
                new Vector3(tangents[index].x, tangents[index].y, tangents[index].z)).normalized;
            tangents[index] = new Vector4(
                direction.x,
                direction.y,
                direction.z,
                tangents[index].w * handedness);
        }
        mirroredMesh.tangents = tangents;

        // Mirroring reverses handedness. Restore outward-facing triangle winding
        // before deriving normals so both sides respond identically to lighting.
        for (var subMesh = 0; subMesh < mirroredMesh.subMeshCount; subMesh++)
        {
            var triangles = mirroredMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var second = triangles[index + 1];
                triangles[index + 1] = triangles[index + 2];
                triangles[index + 2] = second;
            }
            mirroredMesh.SetTriangles(triangles, subMesh, false);
        }
        mirroredMesh.RecalculateBounds();
        left.sharedMesh = mirroredMesh;
        runtimeMeshes.Add(mirroredMesh);
        return 1;
    }

    private void OnDestroy()
    {
        foreach (var mesh in runtimeMeshes)
        {
            if (mesh != null)
                Destroy(mesh);
        }
        runtimeMeshes.Clear();
    }
}

[AddComponentMenu("")]
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
internal sealed class FerrariSF90SpiderHighwaySeamGuard : MonoBehaviour
{
    private const float MinimumSpeedMps = 40f;
    private const float MaximumSampleAgeSeconds = 0.1f;
    private const float MinimumUpwardContactNormal = 0.9f;
    private static readonly string[] KnownHighwaySurfaceNames =
    {
        "HamptonsAvenue_Highway",
        "HighwayAvenue_Highway",
        "X_IntersectionAASAAS_Highway",
    };

    private Rigidbody? body;
    private Vector3 velocityBeforeStep;
    private Vector3 angularVelocityBeforeStep;
    private float velocitySampleTime;

    internal void Initialize(Rigidbody vehicleBody)
    {
        body = vehicleBody;
    }

    private void FixedUpdate()
    {
        if (body == null || body.isKinematic)
            return;

        var planarVelocity = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
        if (planarVelocity.sqrMagnitude < MinimumSpeedMps * MinimumSpeedMps)
            return;

        velocityBeforeStep = body.velocity;
        angularVelocityBeforeStep = body.angularVelocity;
        velocitySampleTime = Time.unscaledTime;
    }

    private void OnCollisionEnter(Collision collision)
    {
        CorrectKnownHighwaySeam(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        CorrectKnownHighwaySeam(collision);
    }

    private void CorrectKnownHighwaySeam(Collision collision)
    {
        if (collision == null || body == null)
            return;

        var other = collision.collider;
        if (other == null ||
            Time.unscaledTime - velocitySampleTime > MaximumSampleAgeSeconds ||
            !IsKnownHighwaySurface(other.name) || !HasUpwardContact(collision))
        {
            return;
        }

        var correctedVelocity = body.velocity;
        if (correctedVelocity.y <= velocityBeforeStep.y)
            return;

        correctedVelocity.y = velocityBeforeStep.y;
        body.velocity = correctedVelocity;
        body.angularVelocity = angularVelocityBeforeStep;
    }

    private static bool HasUpwardContact(Collision collision)
    {
        for (var index = 0; index < collision.contactCount; index++)
        {
            if (collision.GetContact(index).normal.y >= MinimumUpwardContactNormal)
                return true;
        }
        return false;
    }

    private static bool IsKnownHighwaySurface(string objectName)
    {
        foreach (var surfaceName in KnownHighwaySurfaceNames)
        {
            if (objectName.IndexOf(surfaceName, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }
}

[AddComponentMenu("")]
[DefaultExecutionOrder(1100)]
internal sealed class FerrariSF90SpiderSettlingController : MonoBehaviour
{
    private const float SettleDuration = 1.35f;
    private const float MaximumSettlingSpeed = 1.5f;
    private VehicleController? vehicle;
    private Rigidbody? body;
    private ModContext? context;
    private bool wasControlled;
    private bool settling;
    private float settleUntil;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        body = controller.GetComponent<Rigidbody>() ?? controller.GetComponentInParent<Rigidbody>();
        context = modContext;
        wasControlled = controller.controlledByPlayer;
        BeginSettling(wasControlled ? "configured-controlled" : "dealer-display");
    }

    private void FixedUpdate()
    {
        if (vehicle == null || body == null)
            return;

        var controlled = vehicle.controlledByPlayer;
        if (controlled != wasControlled)
            BeginSettling(controlled ? "player-entry" : "player-exit");
        wasControlled = controlled;

        if (!settling)
            return;
        if (Time.unscaledTime >= settleUntil)
        {
            EndSettling("duration-complete");
            return;
        }
        if (body.isKinematic)
            return;

        var up = vehicle.transform.up;
        var velocity = body.velocity;
        var horizontalVelocity = Vector3.ProjectOnPlane(velocity, up);
        if (horizontalVelocity.sqrMagnitude > MaximumSettlingSpeed * MaximumSettlingSpeed)
        {
            EndSettling("vehicle-moving");
            return;
        }

        var verticalVelocity = Vector3.Project(velocity, up);
        var verticalSpeed = Vector3.Dot(velocity, up);
        if (Mathf.Abs(verticalSpeed) < 0.025f)
            verticalVelocity = Vector3.zero;
        else
            verticalVelocity *= verticalSpeed > 0f ? 0.10f : 0.35f;
        body.velocity = horizontalVelocity + verticalVelocity;

        // Preserve steering/yaw while suppressing only the pitch and roll that
        // make a freshly released dealer car hop on its short suspension.
        var yawVelocity = Vector3.Project(body.angularVelocity, up);
        body.angularVelocity = yawVelocity + (body.angularVelocity - yawVelocity) * 0.18f;
    }

    private void BeginSettling(string reason)
    {
        settling = true;
        settleUntil = Time.unscaledTime + SettleDuration;
        FerrariSF90SpiderDiagnostics.Info(context,
            $"FerrariSF90Spider settling vehicle={vehicle?.GetInstanceID()}: begin " +
            $"reason={reason} controlled={vehicle?.controlledByPlayer}.");
    }

    private void EndSettling(string reason)
    {
        if (!settling)
            return;
        settling = false;
        FerrariSF90SpiderDiagnostics.Info(context,
            $"FerrariSF90Spider settling vehicle={vehicle?.GetInstanceID()}: end reason={reason}.");
    }
}

public sealed class FerrariSF90SpiderGlassController : MonoBehaviour
{
    private readonly List<Renderer> cabinGlass = new List<Renderer>();
    private readonly Dictionary<Material, Material> runtimeMaterials =
        new Dictionary<Material, Material>();
    private readonly Dictionary<Material, Material> runtimeHeadlampMaterials =
        new Dictionary<Material, Material>();
    private ModContext? context;
    private Coroutine? restoreCoroutine;
    private bool initialized;

    internal void Initialize(ModContext? modContext)
    {
        context = modContext;
        if (initialized)
        {
            EnsureVisible("reinitialize");
            return;
        }

        cabinGlass.Clear();
        var disabledInteriorDuplicates = 0;
        var headlampLenses = 0;
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            // The GLB includes a second set of black interior window shells.
            // Rendering both panes produces the opaque, angle-dependent black
            // glass seen in-game even with the exterior material configured
            // correctly. Keep the authored exterior panes only.
            if (IsInteriorDuplicateGlassRenderer(renderer))
            {
                renderer.enabled = false;
                renderer.forceRenderingOff = true;
                disabledInteriorDuplicates++;
                continue;
            }
            if (IsHeadlampLensRenderer(renderer))
            {
                ConfigureHeadlampLens(renderer);
                headlampLenses++;
            }
            if (!IsExteriorCabinGlassRenderer(renderer))
                continue;
            var materials = renderer.sharedMaterials;
            var containsCabinGlass = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null ||
                    !FerrariSF90SpiderMaterials.IsCabinGlassMaterial(source))
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    FerrariSF90SpiderMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
                    runtimeMaterials.Add(source, runtimeMaterial);
                }
                materials[index] = runtimeMaterial;
            }
            if (!containsCabinGlass)
                continue;
            renderer.sharedMaterials = materials;
            cabinGlass.Add(renderer);
        }
        initialized = true;
        EnsureVisible("initialize");
        FerrariSF90SpiderDiagnostics.Info(context,
            $"FerrariSF90Spider glass vehicle={GetInstanceID()}: disabled interior duplicate panes=" +
            $"{disabledInteriorDuplicates}, clear headlamp lenses={headlampLenses}; " +
            "exterior panes remain independently transparent.");
    }

    internal void RestoreAfterVehicleEntered()
    {
        if (!initialized)
            return;
        if (restoreCoroutine != null)
            StopCoroutine(restoreCoroutine);
        restoreCoroutine = StartCoroutine(RestoreAfterEntryLifecycle());
    }

    private IEnumerator RestoreAfterEntryLifecycle()
    {
        // Vehicle entry can alter renderer state after the entry callback. Two
        // deferred event passes restore glass once setup has settled, without a
        // permanent per-frame poll.
        yield return null;
        yield return new WaitForEndOfFrame();
        EnsureVisible("vehicle-entered");
        restoreCoroutine = null;
    }

    private void EnsureVisible(string source)
    {
        var restored = 0;
        var propertyBlocksCleared = 0;
        foreach (var renderer in cabinGlass)
        {
            if (renderer == null)
                continue;
            if (!renderer.enabled || renderer.forceRenderingOff)
                restored++;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (renderer.HasPropertyBlock())
            {
                renderer.SetPropertyBlock(null);
                propertyBlocksCleared++;
            }
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null &&
                    FerrariSF90SpiderMaterials.IsCabinGlassMaterial(material))
                {
                    renderer.SetPropertyBlock(null, index);
                    FerrariSF90SpiderMaterials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            FerrariSF90SpiderDiagnostics.Info(context,
                $"FerrariSF90Spider glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            FerrariSF90SpiderDiagnostics.Info(context,
                $"FerrariSF90Spider glass vehicle={GetInstanceID()}: repaired after " +
                $"'{source}' renderers={restored}, propertyBlocks={propertyBlocksCleared}.");
        }
    }

    private static bool IsInteriorDuplicateGlassRenderer(Renderer renderer) =>
        renderer.name.IndexOf("WindowInside_Geo", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsExteriorCabinGlassRenderer(Renderer renderer)
    {
        if (renderer.name.IndexOf("Window_Geo", StringComparison.OrdinalIgnoreCase) < 0 ||
            renderer.name.IndexOf("WindowInside_Geo", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        foreach (var material in renderer.sharedMaterials)
            if (material != null && FerrariSF90SpiderMaterials.IsCabinGlassMaterial(material))
                return true;
        return false;
    }

    private static bool IsHeadlampLensRenderer(Renderer renderer)
    {
        if (renderer.name.IndexOf("Light_Geo", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        foreach (var material in renderer.sharedMaterials)
            if (material != null &&
                material.name.IndexOf("LightA_Material", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private void ConfigureHeadlampLens(Renderer renderer)
    {
        var materials = renderer.sharedMaterials;
        for (var index = 0; index < materials.Length; index++)
        {
            var source = materials[index];
            if (source == null)
                continue;
            if (!runtimeHeadlampMaterials.TryGetValue(source, out var runtimeMaterial))
            {
                runtimeMaterial = Instantiate(source);
                runtimeMaterial.name = source.name + "_RuntimeHeadlampLens";
                FerrariSF90SpiderMaterials.RestoreHeadlampLensMaterial(runtimeMaterial);
                runtimeHeadlampMaterials.Add(source, runtimeMaterial);
            }
            materials[index] = runtimeMaterial;
            renderer.SetPropertyBlock(null, index);
        }
        renderer.sharedMaterials = materials;
        renderer.enabled = true;
        renderer.forceRenderingOff = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private void OnDestroy()
    {
        if (restoreCoroutine != null)
            StopCoroutine(restoreCoroutine);
        restoreCoroutine = null;
        foreach (var material in runtimeMaterials.Values)
        {
            if (material != null)
                Destroy(material);
        }
        runtimeMaterials.Clear();
        foreach (var material in runtimeHeadlampMaterials.Values)
        {
            if (material != null)
                Destroy(material);
        }
        runtimeHeadlampMaterials.Clear();
    }
}

[AddComponentMenu("")]
public sealed class FerrariSF90SpiderVisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.64f;
    private const float MaximumDentDepth = 0.34f;
    private const float DepthPerExcessMps = 0.011f;
    // Final visual-damage calibration: keep the same dent footprint, but reduce
    // displacement depth so front/side crashes do not crumple the SF90 too
    // aggressively. Rear impacts stay slightly stronger by design.
    private const float SideDentDepthScale = 0.697f;
    private const float FrontDentDepthScale = 0.779f;
    private const float RearDentDepthScale = 0.782f;
    private const float FrontDentLateralRadius = 0.82f;
    private const float FrontDentVerticalRadius = 0.68f;
    private const float FrontDentLongitudinalRadius = 0.95f;
    private const float FrontLipDentLateralRadius = 1.18f;
    private const float FrontLipDentVerticalRadius = 0.92f;
    private const float FrontLipDentLongitudinalRadius = 1.30f;
    private const float MaximumFrontDentDepth = 0.36f;
    private const float FrontDepthPerExcessMps = 0.012f;
    private const float RearDentLateralRadius = 0.96f;
    private const float RearDentVerticalRadius = 1.18f;
    private const float RearDentLongitudinalRadius = 1.18f;
    private const float MaximumRearDentDepth = 0.52f;
    private const float RearDepthPerExcessMps = 0.0153f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float ShellFollowerLongitudinalScale = 0.08f;
    private const float ShellFollowerLateralScale = 1.28f;
    private const float ShellFollowerVerticalScale = 1.22f;
    private const float ShellFollowerDepthScale = 1.03f;
    private const float HeadlampFollowerDepthScale = 1.16f;
    // Do not let the compressed shell-follower Z distance make a front impact
    // pull rear lamps (or vice versa). Only nearby end-zone hardware follows.
    private const float ShellFollowerMaximumLongitudinalSetback = 1.30f;
    private const float CollisionCooldown = 0.5f;
    private const int MaximumDiagnosticLogs = 10;

    private readonly List<MeshFilter> deformableFilters = new List<MeshFilter>();
    private readonly Dictionary<MeshFilter, Vector3[]> originalVertices =
        new Dictionary<MeshFilter, Vector3[]>();
    private readonly Dictionary<MeshFilter, Mesh> damageMeshes =
        new Dictionary<MeshFilter, Mesh>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private ModContext? context;
    private Rigidbody? body;
    private float impactThresholdMps;
    private float nextCollisionTime;
    private float previousDamage;
    private float previousSavedDamage;
    private float repairClearSince = -1f;
    private int diagnosticLogs;
    private Coroutine? repairRecoveryCoroutine;
    private bool initialized;
    private bool failureReported;

    internal void Initialize(
        VehicleController controller,
        NWH.VehiclePhysics2.Damage.DamageHandler handler,
        ModContext? modContext,
        IReadOnlyList<MeshFilter> filters,
        float thresholdMps)
    {
        if (initialized && vehicle == controller)
            return;

        vehicle = controller;
        damageHandler = handler;
        context = modContext;
        body = controller.GetComponent<Rigidbody>();
        impactThresholdMps = thresholdMps;
        previousDamage = handler.Damage;
        previousSavedDamage = controller.vehicleInstance?.damage ?? 0f;
        repairClearSince = -1f;
        deformableFilters.Clear();
        originalVertices.Clear();
        damageMeshes.Clear();
        runtimeMeshes.Clear();
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            var runtimeMesh = Instantiate(filter.sharedMesh);
            runtimeMesh.name = filter.sharedMesh.name + "_RuntimeDamage";
            runtimeMesh.MarkDynamic();
            filter.sharedMesh = runtimeMesh;
            deformableFilters.Add(filter);
            originalVertices[filter] = runtimeMesh.vertices;
            damageMeshes[filter] = runtimeMesh;
            runtimeMeshes.Add(runtimeMesh);
        }
        initialized = true;
    }

    private void Update()
    {
        if (!initialized || damageHandler == null)
            return;

        var currentDamage = damageHandler.Damage;
        var currentSavedDamage = vehicle?.vehicleInstance?.damage ?? 0f;
        var savedRepairTransition =
            previousSavedDamage > 0.001f && currentSavedDamage <= 0.001f;
        var nativeRepairTransition =
            previousDamage > 0.001f && currentDamage <= 0.001f &&
            currentSavedDamage <= 0.001f;

        // The vanilla repair UI writes the saved vehicle damage first on some
        // paths while NWH can still retain disabled powertrain state and stale
        // component damage for another frame. Treat the saved transition as the
        // authoritative repair signal, then explicitly repair NWH and recover
        // the drivetrain in the coroutine below.
        if (savedRepairTransition || nativeRepairTransition)
        {
            BeginRepairRecovery(savedRepairTransition ? "saved-damage-cleared" : "native-damage-cleared");
        }
        else if (currentDamage <= 0.001f && currentSavedDamage <= 0.001f &&
                 (previousDamage > 0.001f || previousSavedDamage > 0.001f))
        {
            if (repairClearSince < 0f)
                repairClearSince = Time.unscaledTime;
            else if (Time.unscaledTime - repairClearSince >= 0.35f)
                BeginRepairRecovery("sustained-clear-fallback");
        }
        else if (currentDamage > 0.001f || currentSavedDamage > 0.001f)
        {
            repairClearSince = -1f;
        }

        previousDamage = currentDamage;
        previousSavedDamage = currentSavedDamage;
    }

    private void BeginRepairRecovery(string reason)
    {
        RestoreVisualMeshesAfterRepair();
        if (repairRecoveryCoroutine != null)
            StopCoroutine(repairRecoveryCoroutine);
        repairRecoveryCoroutine = StartCoroutine(RestoreDrivingStateAfterRepair());
        repairClearSince = -1f;
        FerrariSF90SpiderDiagnostics.Info(
            context,
            $"FerrariSF90Spider repair V24 vehicle={vehicle?.GetInstanceID()}: " +
            $"detected reason={reason}; visual meshes restored and drivetrain recovery scheduled.");
    }

    private void RestoreVisualMeshesAfterRepair()
    {
        foreach (var pair in originalVertices)
        {
            if (pair.Key == null || !damageMeshes.TryGetValue(pair.Key, out var mesh) ||
                mesh == null)
                continue;
            // CarController.Repair() / the disabled legacy deformation path can
            // swap a serialized source mesh back onto the filter. Rebind the
            // vehicle-owned runtime mesh before resetting its vertices.
            pair.Key.sharedMesh = mesh;
            mesh.vertices = pair.Value;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || collision == null || Time.unscaledTime < nextCollisionTime ||
            collision.collider.GetComponentInParent<DriveInEntrance>() != null ||
            collision.relativeVelocity.magnitude < impactThresholdMps ||
            !NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision))
            return;

        try
        {
            nextCollisionTime = Time.unscaledTime + CollisionCooldown;
            var contacts = collision.contacts;
            if (contacts.Length == 0)
                return;

            var excessSpeed = collision.relativeVelocity.magnitude - impactThresholdMps;
            var dentDepth =
                Mathf.Clamp(excessSpeed * DepthPerExcessMps, 0.025f, MaximumDentDepth) *
                SideDentDepthScale;
            var frontDentDepth =
                Mathf.Clamp(
                    excessSpeed * FrontDepthPerExcessMps,
                    0.04f,
                    MaximumFrontDentDepth) *
                FrontDentDepthScale;
            var rearDentDepth =
                Mathf.Clamp(
                    excessSpeed * RearDepthPerExcessMps,
                    0.04f,
                    MaximumRearDentDepth) *
                RearDentDepthScale;
            var center = body != null ? body.worldCenterOfMass : transform.position;
            var primaryLocalContact = transform.InverseTransformPoint(contacts[0].point);
            var changedMeshes = 0;
            var changedVertices = 0;
            var changedMeshNames = new List<string>();
            var frontImpact = false;
            var rearImpact = false;

            foreach (var filter in deformableFilters)
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                var meshChanged = false;
                var frontLowerLip = IsFrontLowerLip(filter);
                var attachedDetail = IsAttachedExteriorDetail(filter, vertices.Length);
                var followOuterShell = ShouldFollowOuterShell(filter);
                var appliedWorldDisplacements = attachedDetail
                    ? new Vector3[vertices.Length]
                    : null;
                var totalWorldDisplacement = Vector3.zero;
                var changedVerticesInMesh = 0;
                for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    var worldVertex = filter.transform.TransformPoint(vertices[vertexIndex]);
                    var strongestInfluence = 0f;
                    var inwardDirection = Vector3.zero;
                    var selectedDepth = dentDepth;
                    var selectedEndImpact = false;
                    var selectedFrontImpact = false;
                    foreach (var contact in contacts)
                    {
                        var localContact = transform.InverseTransformPoint(contact.point);
                        var isEndContact =
                            Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                            Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                        var isFrontContact = isEndContact && localContact.z >= 0f;
                        float influence;
                        Vector3 candidateDirection;
                        if (isEndContact)
                        {
                            var localDelta = transform.InverseTransformVector(worldVertex - contact.point);
                            var lateralRadius = isFrontContact
                                ? frontLowerLip ? FrontLipDentLateralRadius : FrontDentLateralRadius
                                : RearDentLateralRadius;
                            var verticalRadius = isFrontContact
                                ? frontLowerLip ? FrontLipDentVerticalRadius : FrontDentVerticalRadius
                                : RearDentVerticalRadius;
                            var longitudinalRadius = isFrontContact
                                ? frontLowerLip ? FrontLipDentLongitudinalRadius : FrontDentLongitudinalRadius
                                : RearDentLongitudinalRadius;
                            if (followOuterShell)
                            {
                                // Keep shell followers tied to the same end of the car. V23's
                                // compressed Z distance could otherwise make a front crash move
                                // rear lamps and other remote trim.
                                if (Mathf.Abs(localDelta.z) > ShellFollowerMaximumLongitudinalSetback)
                                    continue;
                                // Lamp/grille/base/carbon/coloured exterior pieces can sit
                                // behind the paint shell. Compress only the remaining local
                                // setback and widen the field so the visible hardware follows.
                                localDelta.z *= ShellFollowerLongitudinalScale;
                                lateralRadius *= ShellFollowerLateralScale;
                                verticalRadius *= ShellFollowerVerticalScale;
                            }
                            var normalizedDistance = Mathf.Sqrt(
                                localDelta.x * localDelta.x /
                                (lateralRadius * lateralRadius) +
                                localDelta.y * localDelta.y /
                                (verticalRadius * verticalRadius) +
                                localDelta.z * localDelta.z /
                                (longitudinalRadius * longitudinalRadius));
                            influence = 1f - normalizedDistance;
                            candidateDirection = localContact.z >= 0f
                                ? -transform.forward
                                : transform.forward;
                        }
                        else
                        {
                            influence = 1f - Vector3.Distance(worldVertex, contact.point) / DentRadius;
                            var towardCenter = (center - contact.point).normalized;
                            var contactNormal = contact.normal.normalized;
                            candidateDirection = Vector3.Dot(contactNormal, towardCenter) >= 0f
                                ? contactNormal
                                : -contactNormal;
                        }

                        if (influence <= strongestInfluence)
                            continue;
                        strongestInfluence = influence;
                        inwardDirection = candidateDirection;
                        var followerDepthScale = followOuterShell
                            ? GetShellFollowerDepthScale(filter, isFrontContact)
                            : 1f;
                        selectedDepth = (isEndContact
                            ? isFrontContact ? frontDentDepth : rearDentDepth
                            : dentDepth) * followerDepthScale;
                        selectedEndImpact = isEndContact;
                        selectedFrontImpact = isFrontContact;
                    }

                    if (strongestInfluence <= 0f || inwardDirection.sqrMagnitude < 0.5f)
                        continue;
                    var falloff = selectedEndImpact
                        ? Mathf.Pow(strongestInfluence, 1.35f)
                        : strongestInfluence * strongestInfluence;
                    var worldDisplacement = inwardDirection * (selectedDepth * falloff);
                    worldVertex += worldDisplacement;
                    vertices[vertexIndex] = filter.transform.InverseTransformPoint(worldVertex);
                    if (appliedWorldDisplacements != null)
                        appliedWorldDisplacements[vertexIndex] = worldDisplacement;
                    totalWorldDisplacement += worldDisplacement;
                    changedVerticesInMesh++;
                    changedVertices++;
                    meshChanged = true;
                    frontImpact |= selectedEndImpact && selectedFrontImpact;
                    rearImpact |= selectedEndImpact && !selectedFrontImpact;
                }

                if (!meshChanged && attachedDetail)
                {
                    // A badge, grille, lamp insert or other small rigid detail can sit
                    // just behind the painted shell and have no vertex inside the dent
                    // radius even though the panel in front of it moved. Sample the
                    // deformation field at the detail's bounds center and translate the
                    // whole part so it follows that panel instead of floating in place.
                    var detailRenderer = filter.GetComponent<Renderer>();
                    var samplePoint = detailRenderer != null
                        ? detailRenderer.bounds.center
                        : filter.transform.position;
                    var strongestInfluence = 0f;
                    var inwardDirection = Vector3.zero;
                    var selectedDepth = dentDepth;
                    var selectedEndImpact = false;
                    var selectedFrontImpact = false;
                    foreach (var contact in contacts)
                    {
                        var localContact = transform.InverseTransformPoint(contact.point);
                        var isEndContact =
                            Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                            Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                        var isFrontContact = isEndContact && localContact.z >= 0f;
                        float influence;
                        Vector3 candidateDirection;
                        if (isEndContact)
                        {
                            var localDelta = transform.InverseTransformVector(samplePoint - contact.point);
                            var lateralRadius = (isFrontContact
                                ? frontLowerLip ? FrontLipDentLateralRadius : FrontDentLateralRadius
                                : RearDentLateralRadius) * 1.16f;
                            var verticalRadius = (isFrontContact
                                ? frontLowerLip ? FrontLipDentVerticalRadius : FrontDentVerticalRadius
                                : RearDentVerticalRadius) * 1.16f;
                            var longitudinalRadius = (isFrontContact
                                ? frontLowerLip ? FrontLipDentLongitudinalRadius : FrontDentLongitudinalRadius
                                : RearDentLongitudinalRadius) * 1.22f;
                            if (followOuterShell)
                            {
                                if (Mathf.Abs(localDelta.z) > ShellFollowerMaximumLongitudinalSetback)
                                    continue;
                                localDelta.z *= ShellFollowerLongitudinalScale;
                            }
                            var normalizedDistance = Mathf.Sqrt(
                                localDelta.x * localDelta.x / (lateralRadius * lateralRadius) +
                                localDelta.y * localDelta.y / (verticalRadius * verticalRadius) +
                                localDelta.z * localDelta.z / (longitudinalRadius * longitudinalRadius));
                            influence = 1f - normalizedDistance;
                            candidateDirection = localContact.z >= 0f
                                ? -transform.forward
                                : transform.forward;
                        }
                        else
                        {
                            influence = 1f - Vector3.Distance(samplePoint, contact.point) / (DentRadius * 1.15f);
                            var towardCenter = (center - contact.point).normalized;
                            var contactNormal = contact.normal.normalized;
                            candidateDirection = Vector3.Dot(contactNormal, towardCenter) >= 0f
                                ? contactNormal
                                : -contactNormal;
                        }

                        if (influence <= strongestInfluence)
                            continue;
                        strongestInfluence = influence;
                        inwardDirection = candidateDirection;
                        var followerDepthScale = followOuterShell
                            ? GetShellFollowerDepthScale(filter, isFrontContact)
                            : 1f;
                        selectedDepth = (isEndContact
                            ? isFrontContact ? frontDentDepth : rearDentDepth
                            : dentDepth) * followerDepthScale;
                        selectedEndImpact = isEndContact;
                        selectedFrontImpact = isFrontContact;
                    }

                    if (strongestInfluence > 0f && inwardDirection.sqrMagnitude >= .5f)
                    {
                        var falloff = selectedEndImpact
                            ? Mathf.Pow(strongestInfluence, 1.35f)
                            : strongestInfluence * strongestInfluence;
                        var detailDisplacement = inwardDirection * (selectedDepth * falloff);
                        for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                        {
                            var worldVertex = filter.transform.TransformPoint(vertices[vertexIndex]);
                            vertices[vertexIndex] = filter.transform.InverseTransformPoint(
                                worldVertex + detailDisplacement);
                        }
                        changedVertices += vertices.Length;
                        meshChanged = true;
                        appliedWorldDisplacements = null;
                        frontImpact |= selectedEndImpact && selectedFrontImpact;
                        rearImpact |= selectedEndImpact && !selectedFrontImpact;
                    }
                }

                if (!meshChanged)
                    continue;
                if (attachedDetail && appliedWorldDisplacements != null &&
                    changedVerticesInMesh > 0)
                {
                    // Small lamps, badges, aero inserts and trim should follow
                    // the panel rather than deforming only a few vertices and
                    // floating in front of the dent. Translate the whole detail
                    // by the sampled local damage displacement.
                    var averageDisplacement = totalWorldDisplacement / changedVerticesInMesh;
                    for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        var undeformedWorld = filter.transform.TransformPoint(vertices[vertexIndex]) -
                                              appliedWorldDisplacements[vertexIndex];
                        vertices[vertexIndex] = filter.transform.InverseTransformPoint(
                            undeformedWorld + averageDisplacement);
                    }
                    changedVertices += vertices.Length - changedVerticesInMesh;
                }
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                changedMeshes++;
                changedMeshNames.Add(filter.name);
            }

            if (diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                // Retain bounded impact diagnostics for opt-in debugging without
                // writing routine deformation events to release Player.log output.
                FerrariSF90SpiderDiagnostics.Info(
                    context,
                    $"FerrariSF90Spider damage V24 vehicle={vehicle?.GetInstanceID()}: inward dent " +
                    $"contact='{collision.collider?.name ?? "unknown"}' " +
                    $"relativeSpeed={collision.relativeVelocity.magnitude * 3.6f:0.0}kph " +
                    $"localContact=({primaryLocalContact.x:0.00}," +
                    $"{primaryLocalContact.y:0.00},{primaryLocalContact.z:0.00}) " +
                    $"region={(frontImpact ? "front" : rearImpact ? "rear" : "side")} " +
                    $"depth={(frontImpact ? frontDentDepth : rearImpact ? rearDentDepth : dentDepth):0.000}m " +
                    $"meshes={changedMeshes} vertices={changedVertices} " +
                    $"changed=[{string.Join(", ", changedMeshNames)}] " +
                    $"nwhDamage={(damageHandler?.Damage ?? 0f) * 100f:0.0}% " +
                    $"vehicleDamage={(vehicle?.vehicleInstance?.damage ?? 0f) * 100f:0.0}%.");
            }
        }
        catch (Exception exception)
        {
            if (failureReported)
                return;
            failureReported = true;
            context?.Logger.Warn(
                $"FerrariSF90Spider damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {exception.GetType().Name}: {exception.Message}");
        }
    }

    private IEnumerator RestoreDrivingStateAfterRepair()
    {
        yield return null;
        if (vehicle == null || damageHandler == null)
        {
            repairRecoveryCoroutine = null;
            yield break;
        }

        // Cadillac / Porsche / Jesko all need an explicit post-repair wake-up on
        // current NWH builds. Repair the native component state first, then make
        // the player vehicle dynamic again before restarting its powertrain.
        damageHandler.Repair();
        vehicle.SetFreeze(false);

        var physics = vehicle.GetComponent<PhysicsVehicle>() ??
                      vehicle.GetComponentInChildren<PhysicsVehicle>(true);
        var rigidbody = vehicle.GetComponent<Rigidbody>() ??
                        vehicle.GetComponentInParent<Rigidbody>();
        foreach (var wheelController in
                 vehicle.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
            wheelController.enabled = true;
        if (rigidbody != null)
        {
            rigidbody.isKinematic = false;
            rigidbody.WakeUp();
        }
        if (physics == null)
        {
            context?.Logger.Warn(
                $"FerrariSF90Spider repair V24 vehicle={vehicle.GetInstanceID()}: " +
                "NWH vehicle controller missing during drivetrain recovery.");
            repairRecoveryCoroutine = null;
            yield break;
        }

        physics.enabled = true;
        if (!vehicle.controlledByPlayer)
        {
            // A repair can complete while the player is outside. Native entry
            // recovery will start the engine later; the vehicle must nevertheless
            // leave repair in a movable/non-frozen state now.
            FerrariSF90SpiderDiagnostics.Info(
                context,
                $"FerrariSF90Spider repair V24 vehicle={vehicle.GetInstanceID()}: " +
                "physics/wheels restored while vehicle is not player-controlled.");
            repairRecoveryCoroutine = null;
            yield break;
        }

        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        engine.StopEngine();
        transmission.ShiftInto(0, true);
        transmission.currentGearRatio = 0f;
        yield return new WaitForSecondsRealtime(.15f);
        if (vehicle == null || !vehicle.controlledByPlayer)
        {
            repairRecoveryCoroutine = null;
            yield break;
        }

        engine.StartEngine();
        for (var pass = 1; pass <= 5; pass++)
        {
            yield return new WaitForSecondsRealtime(.35f);
            if (vehicle == null || !vehicle.controlledByPlayer)
                break;

            vehicle.SetFreeze(false);
            physics.enabled = true;
            foreach (var wheelController in
                     vehicle.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
                wheelController.enabled = true;
            if (rigidbody != null)
            {
                rigidbody.isKinematic = false;
                rigidbody.WakeUp();
            }

            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            if (!engine.IsRunning || !engine.ignition || !engine.canRun || rpm < 300f)
            {
                engine.StartEngine();
                continue;
            }

            if (transmission.Gear == 0)
                transmission.ShiftInto(1, true);
            FerrariSF90SpiderDiagnostics.Info(
                context,
                $"FerrariSF90Spider repair V24 vehicle={vehicle.GetInstanceID()}: " +
                $"drivetrain recovered pass={pass}, rpm={rpm:0}, gear={transmission.Gear}, " +
                $"physics={physics.enabled}, kinematic={rigidbody?.isKinematic}.");
            repairRecoveryCoroutine = null;
            yield break;
        }

        context?.Logger.Warn(
            $"FerrariSF90Spider repair V24 vehicle={vehicle?.GetInstanceID()}: " +
            $"engine remained unavailable after repair; running={engine.IsRunning}, " +
            $"ignition={engine.ignition}, canRun={engine.canRun}, " +
            $"rpm={engine.RPMPercent * engine.revLimiterRPM:0}, gear={transmission.Gear}.");
        repairRecoveryCoroutine = null;
    }

    private static float GetShellFollowerDepthScale(MeshFilter filter, bool frontImpact)
    {
        if (!frontImpact)
            return ShellFollowerDepthScale;

        // The transparent LightA mesh is the physical headlamp cover/lens. It sits
        // slightly behind the painted nose in the source GLB, so make it follow
        // the front shell a little more strongly instead of protruding after a dent.
        var name = filter.name;
        if (name.IndexOf("Light_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("FerrariSF90Spider_Light_DRL_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("FerrariSF90Spider_Light_FrontIndicator_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("FerrariSF90Spider_Light_Headlamps", StringComparison.OrdinalIgnoreCase) >= 0)
            return HeadlampFollowerDepthScale;

        return ShellFollowerDepthScale;
    }

    private static bool ShouldFollowOuterShell(MeshFilter filter)
    {
        if (FerrariSF90SpiderRuntime.IsSf90BodyPaintFilter(filter))
            return false;
        var name = filter.name;
        return name.IndexOf("Base_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Coloured_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Carbon", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Grille", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Light_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Badge_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("ManufacturerPlate_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("FerrariSF90Spider_Light_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsAttachedExteriorDetail(MeshFilter filter, int vertexCount)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;
        // Only truly small rigid pieces are translated as a whole. Combined
        // meshes such as Grille1/Light_Geo span both ends of the vehicle and
        // must instead follow the dent field per vertex.
        if (FerrariSF90SpiderRuntime.IsSf90BodyPaintFilter(filter))
            return false;
        var name = filter.name;
        if (name.IndexOf("Badge_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("ManufacturerPlate_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("FerrariSF90Spider_Light_", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        var size = renderer.bounds.size;
        var longestSide = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        return vertexCount <= 420 || longestSide <= .34f;
    }

    private static bool IsFrontLowerLip(MeshFilter filter)
    {
        var renderer = filter.GetComponent<MeshRenderer>();
        var vehicle = filter.GetComponentInParent<VehicleController>();
        if (renderer == null || vehicle == null)
            return false;
        var hasCarbon = false;
        foreach (var material in renderer.sharedMaterials)
            if (material != null &&
                material.name.IndexOf("Carbon2_Material", StringComparison.OrdinalIgnoreCase) >= 0)
                hasCarbon = true;
        if (!hasCarbon)
            return false;
        var localCenter = vehicle.transform.InverseTransformPoint(renderer.bounds.center);
        return localCenter.z > 1.45f && localCenter.y < 0.55f;
    }

    private void OnDestroy()
    {
        if (repairRecoveryCoroutine != null)
            StopCoroutine(repairRecoveryCoroutine);
        repairRecoveryCoroutine = null;
        foreach (var mesh in runtimeMeshes)
            if (mesh != null) Destroy(mesh);
        runtimeMeshes.Clear();
    }
}
