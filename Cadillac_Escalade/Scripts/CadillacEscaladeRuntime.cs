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

public sealed class CadillacEscaladeRuntime : MonoBehaviour
{
    private static readonly FieldInfo? LastAutoParkPositionField =
        typeof(VehicleParkingHelper).GetField("_lastAutoParkPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? HudParkingStateField =
        typeof(UI.ItemPanel.VehicleInfoPanel).GetField("currentParkingState",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private const float AutoParkingRefreshDuration = 3f;
    private const float AutoParkingRefreshInterval = 0.05f;
    private const string VehicleRepainterColorRestoredEvent =
        "vehicle-repainter:color-restored";
    private const string VehicleRepainterColorPreviewEvent =
        "vehicle-repainter:color-preview";
    private const string VehicleRepainterColorResetEvent =
        "vehicle-repainter:color-reset";
    // Match the established Alfa approach: reuse the game's native player-car
    // sleep configuration instead of synthesizing one or scanning all loaded
    // vehicles. This is valid for dealer and developer-tool spawned Cadillacs.
    private const string NativeCarSleepDonorPrefabPath =
        "Vehicles/PlayerVehicles/HonzaMimic";
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 2738f;
    // NWH applies maxPower much closer to wheel power than SAE crank power.
    // Keep the rated 313 kW on VehicleType, but use the measured road-test
    // calibration here so the 2.7-ton SUV reaches 60 mph in about 6.1 seconds.
    private const float EnginePowerKw = 232f;
    private const float BrakeMaxTorque = 4000f;
    private const float BrakeActuationTime = 0.10f;
    private const float EngineIdleRpm = 600f;
    private const float EngineLimitRpm = 6000f;
    private const float SpeedLimitKph = 193f;
    private const float FinalDriveRatio = 3.23f;
    private const float DownshiftRpm = 3500f;
    private const float EngineInertia = 0.45f;
    private const float EngineStartDuration = 0.80f;
    private const float ClutchEngagementRpm = 1000f;
    private const float ClutchThrottleOffsetRpm = 350f;
    private const float ClutchEngagementRange = 700f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 0.80f;
    private const float FrontLateralGrip = 0.54f;
    private const float RearLateralGrip = 0.58f;
    private const float AntiRollBarForce = 10500f;
    private const float FrontSuspensionTravel = 0.15f;
    private const float RearSuspensionTravel = 0.15f;
    private const float DeformationStrength = 0.13f;
    private const float DeformationRadius = 0.45f;
    private const float DeformationRandomness = 0.012f;
    private const float DamageIntensity = 0.75f;
    private const float DamageDecelerationThreshold = 300f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private const float WarehouseExitEntranceSearchRadius = 12f;
    private const float WarehouseExitGuardDuration = 8f;
    private const float WarehouseExitGuardClearDistance = 4f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.22f, -0.10f);
    private static readonly Vector3 LowerColliderCenter = new Vector3(0f, 0.30f, -0.05f);
    private static readonly Vector3 LowerColliderSize = new Vector3(1.94f, 0.50f, 5.12f);
    private static readonly Vector3 UpperColliderCenter = new Vector3(0f, 0.78f, -0.35f);
    private static readonly Vector3 UpperColliderSize = new Vector3(1.72f, 0.74f, 4.10f);

    private static readonly float[] EscaladeGears =
    {
        -4.87f,
        0f,
        4.70f,
        2.99f,
        2.15f,
        1.80f,
        1.52f,
        1.28f,
        1.00f,
        0.85f,
        0.69f,
        0.64f,
    };

    private static AnimationCurve CreateEscaladePowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.10f, 0.12f),
            new Keyframe(0.30f, 0.40f),
            new Keyframe(0.55f, 0.72f),
            new Keyframe(0.72f, 0.88f),
            new Keyframe(0.88f, 0.98f),
            new Keyframe(0.93f, 1f),
            new Keyframe(1f, 0.90f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? powertrainReadinessCoroutine;
    private VehicleController? autoParkingVehicle;
    private VehicleParkingHelper? autoParkingHelper;
    private Vector3 autoParkingRefreshSpot = Vector3.positiveInfinity;
    private int autoParkingRefreshAttempts;
    private float autoParkingRefreshUntil;
    private float nextAutoParkingRefresh;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private GameObject? playerVehiclePrefab;
    private bool dealerReady;
    private bool dealerReadyLogged;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;
    private UnityEngine.Object? nativeCarSleepConfig;
    private bool nativeCarSleepConfigUnavailableLogged;
    private int cachedPlayerVehicleCount = -1;
    private int cachedTargetVehicleCount;

    public static CadillacEscaladeRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<CadillacEscaladeRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(CadillacEscaladeRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<CadillacEscaladeRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        runtime.ResetPlayerVehicleSnapshot();
        var trafficFrequency = runtime.GetComponent<CadillacEscaladeTrafficFrequency>() ??
                               runtime.gameObject.AddComponent<CadillacEscaladeTrafficFrequency>();
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
        if (powertrainReadinessCoroutine != null)
            StopCoroutine(powertrainReadinessCoroutine);
        initializationCoroutine = null;
        powertrainReadinessCoroutine = null;
        StopPlayerExitRecovery();
        StopWarehouseExitGuard();
        CadillacEscaladePrivateDriverSupport.RemoveVehicle(vehicleTypeName);
        playerVehiclePrefab = null;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        nativeCarSleepConfig = null;
        nativeCarSleepConfigUnavailableLogged = false;
        configuredVehicleIds.Clear();
        ResetPlayerVehicleSnapshot();
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
        StopPlayerExitRecovery();
        StopWarehouseExitGuard();
    }

    private void Update()
    {
        // Dealer purchases do not raise onEnterVehicle. Keep the hot path to an
        // allocation-free integer comparison and enumerate only after the
        // small player-vehicle collection changes.
        var playerVehicleCount = VehicleHelper.AllPlayerVehicles?.Count ?? 0;
        if (playerVehicleCount != cachedPlayerVehicleCount)
            ConfigurePlayerVehiclesIfChanged(out _);
    }

    private void LateUpdate()
    {
        RefreshAutoParkingZoneIfNeeded();
    }

    private void RefreshAutoParkingZoneIfNeeded()
    {
        var vehicle = GameManager.Instance?.selectedVehicle;
        if (!IsTargetVehicle(vehicle) || vehicle is not CarController car)
        {
            autoParkingVehicle = null;
            autoParkingHelper = null;
            return;
        }

        if (autoParkingVehicle != vehicle)
        {
            autoParkingVehicle = vehicle;
            autoParkingHelper = vehicle.GetComponentInChildren<VehicleParkingHelper>(true);
            autoParkingRefreshSpot = Vector3.positiveInfinity;
            autoParkingRefreshAttempts = 0;
            autoParkingRefreshUntil = 0f;
        }
        if (autoParkingHelper == null ||
            LastAutoParkPositionField?.GetValue(autoParkingHelper) is not Vector3 spot ||
            Vector3.Distance(vehicle.transform.position, spot) > 0.75f)
        {
            autoParkingRefreshSpot = Vector3.positiveInfinity;
            autoParkingRefreshAttempts = 0;
            return;
        }
        if (spot != autoParkingRefreshSpot)
        {
            autoParkingRefreshSpot = spot;
            autoParkingRefreshAttempts = 0;
            autoParkingRefreshUntil = Time.unscaledTime + AutoParkingRefreshDuration;
            nextAutoParkingRefresh = 0f;
        }
        if (Time.unscaledTime > autoParkingRefreshUntil ||
            Time.unscaledTime < nextAutoParkingRefresh)
            return;
        nextAutoParkingRefresh = Time.unscaledTime + AutoParkingRefreshInterval;

        var panel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
        if (panel == null || !panel.isActiveAndEnabled)
            return;
        var hudBefore = HudParkingStateField?.GetValue(panel)?.ToString() ?? "<unknown>";
        if (hudBefore == ParkingState.Legal.ToString())
            return;

        // The game's stopped/moving transition can overwrite the first legal
        // result after teleport. Recheck for a short time while the car settles.
        autoParkingRefreshAttempts++;
        Physics.SyncTransforms();
        car.UpdateParkingZone();
        var hudAfter = HudParkingStateField?.GetValue(panel)?.ToString() ?? "<unknown>";
        var wheels = GetParkingWheelState(car, out var neighbourhood, out var wheelChecks);
        if (wheels && hudAfter != ParkingState.Legal.ToString())
        {
            panel.SetParkingZone(ParkingState.Legal, neighbourhood);
            hudAfter = HudParkingStateField?.GetValue(panel)?.ToString() ?? "<unknown>";
        }
        if (CadillacEscaladeDiagnostics.DebugEnabled &&
            CadillacEscaladeDiagnostics.AutoParkingDebugEnabled &&
            (autoParkingRefreshAttempts == 1 || autoParkingRefreshAttempts % 10 == 0))
            context?.Logger.Info(
                $"CadillacEscalade auto-parking refresh vehicle={vehicle.GetInstanceID()} " +
                $"attempt={autoParkingRefreshAttempts} speed={car.CurrentSpeed:0.00} " +
                $"hudBefore={hudBefore} hudAfter={hudAfter} legalWheels={wheels} " +
                $"root={vehicle.transform.position} wheels=[{wheelChecks}].");
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
        ResetPlayerVehicleSnapshot();
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeEvents();
        ResetPlayerVehicleSnapshot();
        privateDriverRegistrationAllowed = true;
        ScheduleInitialization("game-loaded-late");
    }

    private void HandleGameUnloaded()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        if (powertrainReadinessCoroutine != null)
            StopCoroutine(powertrainReadinessCoroutine);
        initializationCoroutine = null;
        powertrainReadinessCoroutine = null;
        StopPlayerExitRecovery();
        StopWarehouseExitGuard();
        configuredVehicleIds.Clear();
        ResetPlayerVehicleSnapshot();
        dealerReady = false;
        dealerReadyLogged = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        nativeCarSleepConfig = null;
        nativeCarSleepConfigUnavailableLogged = false;
        autoParkingVehicle = null;
        autoParkingHelper = null;
        autoParkingRefreshAttempts = 0;
        autoParkingRefreshUntil = 0f;
    }

    private void HandleGameEvent(string eventName)
    {
        if (string.Equals(eventName, VehicleRepainterColorRestoredEvent,
                StringComparison.Ordinal))
        {
            RefreshExistingVehiclePaint();
            return;
        }

        if (!string.Equals(eventName, VehicleRepainterColorPreviewEvent,
                StringComparison.Ordinal) &&
            !string.Equals(eventName, VehicleRepainterColorResetEvent,
                StringComparison.Ordinal))
        {
            return;
        }

        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        selectedVehicle!
            .GetComponent<CadillacEscaladePaintController>()
            ?.RefreshCurrentColor();
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        TryConfigureVehicle(vehicle);
        if (!IsTargetVehicle(vehicle))
            return;
        vehicle.GetComponent<CadillacEscaladePaintController>()?.RestoreAfterVehicleEntered();
        vehicle.GetComponent<CadillacEscaladeGlassController>()?.RestoreAfterVehicleEntered();
        if (powertrainReadinessCoroutine != null)
            StopCoroutine(powertrainReadinessCoroutine);
        powertrainReadinessCoroutine = StartCoroutine(EnsurePowertrainReadyAfterEntry(vehicle));
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;

        StartCoroutine(ReconcileParkingAfterExit(vehicle));
        StopPlayerExitRecovery();
        exitedPlayerRecoveryCoroutine = StartCoroutine(RecoverPlayerNavMeshAfterExit(vehicle));
    }

    private IEnumerator ReconcileParkingAfterExit(VehicleController vehicle)
    {
        // Native exit and parking-state updates finish later in the frame.
        yield return new WaitForEndOfFrame();
        if (vehicle == null)
            yield break;

        Physics.SyncTransforms();
        var helper = vehicle.GetComponentInChildren<VehicleParkingHelper>(true);
        var lastSpot = helper != null &&
            LastAutoParkPositionField?.GetValue(helper) is Vector3 position
            ? position : Vector3.positiveInfinity;
        var distance = Vector3.Distance(vehicle.transform.position, lastSpot);
        var autoParked = distance <= 0.75f;
        var diagnostic = CadillacEscaladeDiagnostics.DebugEnabled &&
                         CadillacEscaladeDiagnostics.AutoParkingDebugEnabled;
        if (!autoParked && !diagnostic)
            yield break;

        var panel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
        var hudBefore = panel == null
            ? "<unavailable>" : HudParkingStateField?.GetValue(panel)?.ToString() ?? "<unknown>";
        var savedBefore = vehicle.vehicleInstance?.parkingState.ToString() ?? "<none>";
        var neighbourhood = string.Empty;
        var wheelChecks = "not-a-car";
        var allWheelsLegal = vehicle is CarController car &&
                             GetParkingWheelState(car, out neighbourhood,
                                 out wheelChecks);
        var corrected = false;
        if (autoParked && allWheelsLegal && vehicle.vehicleInstance != null)
        {
            if (vehicle.vehicleInstance.parkingState != ParkingState.Legal)
            {
                vehicle.vehicleInstance.parkingState = ParkingState.Legal;
                vehicle.vehicleInstance.parkingNeighbourhood = neighbourhood;
                corrected = true;
            }
        }
        if (autoParked && !allWheelsLegal)
            context?.Logger.Warn(
                $"CadillacEscalade auto-parking: vehicle={vehicle.GetInstanceID()} " +
                $"is not fully inside a legal parking zone after exit; " +
                $"wheels=[{wheelChecks}].");
        if (diagnostic)
        {
            var hudAfter = panel == null
                ? "<unavailable>" : HudParkingStateField?.GetValue(panel)?.ToString() ?? "<unknown>";
            context?.Logger.Info(
                $"CadillacEscalade auto-parking exit vehicle={vehicle.GetInstanceID()} " +
                $"savedBefore={savedBefore} hudBefore={hudBefore} " +
                $"distanceToAutoSpot={distance:0.00}m legalWheels={allWheelsLegal} " +
                $"selected={GameManager.Instance?.selectedVehicle == vehicle} corrected={corrected} " +
                $"savedAfter={vehicle.vehicleInstance?.parkingState.ToString() ?? "<none>"} " +
                $"hudAfter={hudAfter} " +
                $"root={vehicle.transform.position} wheels=[{wheelChecks}].");
        }
    }

    private static bool GetParkingWheelState(CarController car,
        out string neighbourhood, out string wheelChecks)
    {
        neighbourhood = string.Empty;
        var checks = new List<string>();
        var wheels = car.vehicleController?.powertrain?.wheels;
        var allWheelsLegal = wheels != null && wheels.Count == 4;
        if (wheels != null)
            for (var index = 0; index < wheels.Count; index++)
            {
                var wheel = wheels[index]?.wheelUAPI;
                if (wheel == null)
                {
                    allWheelsLegal = false;
                    checks.Add($"{index}:missing-wheel");
                    continue;
                }
                var transform = wheel.transform;
                var hits = Physics.OverlapBox(
                    transform.position - transform.up * 0.1f,
                    new Vector3(0.1f, wheel.Radius * 2f, 0.1f),
                    transform.rotation, LayerHelper.parkingAreaLayerMask);
                var lane = hits.Length > 0
                    ? hits[0].GetComponentInParent<ParkingLaneGenerator>() : null;
                if (lane == null || lane.isHandicapParking ||
                    (neighbourhood.Length > 0 && neighbourhood != lane.neighbourhood))
                    allWheelsLegal = false;
                if (lane != null)
                    neighbourhood = lane.neighbourhood;
                checks.Add($"{index}:hits={hits.Length},lane={lane?.name ?? "<none>"}," +
                           $"handicap={lane?.isHandicapParking.ToString() ?? "<none>"}");
            }
        wheelChecks = string.Join("; ", checks);
        return allWheelsLegal;
    }

    private void StopPlayerExitRecovery()
    {
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
    }

    private IEnumerator RecoverPlayerNavMeshAfterExit(VehicleController exitedVehicle)
    {
        // Native exit first completes its animation and transfers input. Check
        // once afterwards instead of changing every normal, successful exit.
        yield return null;
        yield return new WaitForEndOfFrame();

        var root = PlayerHelper.PlayerController?.transform;
        if (root == null)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        Physics.SyncTransforms();
        var agents = root.GetComponentsInChildren<NavMeshAgent>(true);
        var anyAgentOffNavMesh = false;
        foreach (var agent in agents)
            anyAgentOffNavMesh |= agent != null && agent.enabled && !agent.isOnNavMesh;

        var initialExitClear = IsPlayerExitClear(root, root.position);
        var needsRecovery = anyAgentOffNavMesh || !initialExitClear;
        CadillacEscaladeDiagnostics.PlayerExitInfo(
            context,
            $"CadillacEscalade player-exit: deferred validation vehicle={exitedVehicle.GetInstanceID()}, " +
            $"playerPosition={root.position}, agentCount={agents.Length}, " +
            $"anyAgentOffNavMesh={anyAgentOffNavMesh}, initialClear={initialExitClear}, " +
            $"needsRecovery={needsRecovery}.");
        if (!needsRecovery)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        if (!TryFindClearExitPosition(root, exitedVehicle, out var target))
        {
            CadillacEscaladeDiagnostics.PlayerExitInfo(
                context,
                $"CadillacEscalade player-exit: recovery failed vehicle={exitedVehicle.GetInstanceID()}; " +
                "no NavMesh/capsule-clear candidate was found.");
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        var characterControllers = root.GetComponentsInChildren<CharacterController>(true);
        var controllerStates = Array.ConvertAll(
            characterControllers,
            controller => controller != null && controller.enabled);
        var agentStates = Array.ConvertAll(agents, agent => agent != null && agent.enabled);
        try
        {
            foreach (var controller in characterControllers)
            {
                if (controller != null)
                    controller.enabled = false;
            }
            foreach (var agent in agents)
            {
                if (agent != null)
                    agent.enabled = false;
            }

            root.position = target;
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
            {
                if (characterControllers[index] != null)
                    characterControllers[index].enabled = controllerStates[index];
            }

            Physics.SyncTransforms();
        }

        CadillacEscaladeDiagnostics.PlayerExitInfo(
            context,
            $"CadillacEscalade player-exit: recovery moved player to {target}.");
        exitedPlayerRecoveryCoroutine = null;
    }

    private static bool TryFindClearExitPosition(
        Transform playerRoot,
        VehicleController exitedVehicle,
        out Vector3 target)
    {
        var vehicleTransform = exitedVehicle.transform;
        var candidates = new[]
        {
            playerRoot.position,
            vehicleTransform.position - vehicleTransform.right * 2.25f,
            vehicleTransform.position + vehicleTransform.right * 2.25f,
            vehicleTransform.position - vehicleTransform.forward * 3.00f,
            vehicleTransform.position + vehicleTransform.forward * 3.00f,
            vehicleTransform.position - vehicleTransform.right * 2.25f - vehicleTransform.forward * 1.75f,
            vehicleTransform.position + vehicleTransform.right * 2.25f - vehicleTransform.forward * 1.75f,
            vehicleTransform.position - vehicleTransform.right * 2.55f + vehicleTransform.forward * 1.30f,
            vehicleTransform.position + vehicleTransform.right * 2.55f + vehicleTransform.forward * 1.30f,
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

    private IEnumerator EnsurePowertrainReadyAfterEntry(VehicleController vehicle)
    {
        // Native entry starts the engine asynchronously. Give that lifecycle a
        // short grace period, then perform a bounded clean restart only when it
        // remained dormant (the reported failure had zero RPM in first gear).
        yield return new WaitForSecondsRealtime(0.25f);
        var physics = vehicle.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        if (physics == null)
        {
            context?.Logger.Warn(
                $"CadillacEscalade drivetrain vehicle={vehicle.GetInstanceID()}: " +
                "entry readiness could not find the NWH controller.");
            powertrainReadinessCoroutine = null;
            yield break;
        }

        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        for (var attempt = 1; attempt <= EngineStartAttemptCount; attempt++)
        {
            if (!vehicle.controlledByPlayer)
                break;
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            if (engine.IsRunning && engine.ignition && engine.canRun &&
                rpm >= MinimumHealthyEngineRpm)
            {
                if (transmission.Gear <= 0)
                    transmission.ShiftInto(1, true);
                CadillacEscaladeDiagnostics.Info(context,
                    $"CadillacEscalade drivetrain vehicle={vehicle.GetInstanceID()}: " +
                    $"entry ready attempt={attempt}, running={engine.IsRunning}, " +
                    $"rpm={rpm:0}, gear={transmission.Gear}.");
                powertrainReadinessCoroutine = null;
                yield break;
            }

            if (attempt == 1)
            {
                context?.Logger.Warn(
                    $"CadillacEscalade drivetrain vehicle={vehicle.GetInstanceID()}: " +
                    $"dormant after entry; beginning bounded restart, running={engine.IsRunning}, " +
                    $"ignition={engine.ignition}, canRun={engine.canRun}, rpm={rpm:0}, " +
                    $"gear={transmission.Gear}.");
            }
            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(0.15f);
            if (!vehicle.controlledByPlayer)
                break;
            engine.StartEngine();
            yield return new WaitForSecondsRealtime(0.90f);
            if (vehicle.controlledByPlayer)
                transmission.ShiftInto(1, true);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        if (!vehicle.controlledByPlayer)
        {
            powertrainReadinessCoroutine = null;
            yield break;
        }

        var finalRpm = engine.RPMPercent * engine.revLimiterRPM;
        context?.Logger.Warn(
            $"CadillacEscalade drivetrain vehicle={vehicle.GetInstanceID()}: " +
            $"entry readiness ended without a healthy engine, controlled={vehicle.controlledByPlayer}, " +
            $"running={engine.IsRunning}, ignition={engine.ignition}, canRun={engine.canRun}, " +
            $"rpm={finalRpm:0}, gear={transmission.Gear}.");
        powertrainReadinessCoroutine = null;
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (CadillacEscaladeLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
            ScheduleInitialization("dealer-entered");
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
            CadillacEscaladeDiagnostics.WarehouseExitInfo(
                context,
                "CadillacEscalade warehouse-exit: guard skipped because the current vehicle " +
                "is not a player-controlled Escalade.");
            return;
        }

        var entrance = FindClosestDriveInEntrance(vehicle.transform.position, out var distance);
        if (entrance == null || distance > WarehouseExitEntranceSearchRadius)
        {
            CadillacEscaladeDiagnostics.WarehouseExitInfo(
                context,
                $"CadillacEscalade warehouse-exit: guard skipped because no nearby drive-in " +
                $"entrance was found; vehicle={vehicle.GetInstanceID()}, distance={distance:0.00}m.");
            return;
        }

        StopWarehouseExitGuard();
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
            CadillacEscaladeDiagnostics.WarehouseExitInfo(
                context,
                $"CadillacEscalade warehouse-exit: guard skipped because entrance='{entrance.name}' " +
                "had no enabled child entry-trigger colliders.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(
            vehicle.transform.position - entrance.transform.position,
            Vector3.up);
        if (outward.sqrMagnitude < 0.0001f)
            outward = Vector3.ProjectOnPlane(entrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < 0.0001f)
        {
            CadillacEscaladeDiagnostics.WarehouseExitInfo(
                context,
                "CadillacEscalade warehouse-exit: guard aborted because the outward direction was zero.");
            RestoreWarehouseExitTriggers("zero-outward-direction");
            return;
        }

        outward.Normalize();
        var startingProjection = Vector3.Dot(vehicle.transform.position, outward);
        var placementCollider = vehicle.GetComponent<MeshCollider>();
        Physics.SyncTransforms();
        CadillacEscaladeDiagnostics.WarehouseExitInfo(
            context,
            $"CadillacEscalade warehouse-exit: guard started vehicle={vehicle.GetInstanceID()}, " +
            $"entrance='{entrance.name}', entranceDistance={distance:0.00}m, " +
            $"triggerCount={warehouseExitGuardColliders.Count}, outward={outward}, " +
            $"startProjection={startingProjection:0.000}, " +
            $"nativeMesh='{placementCollider?.name ?? "missing"}', " +
            $"nativeMeshLength={placementCollider?.sharedMesh?.bounds.size.z:0.000}, " +
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

        CadillacEscaladeDiagnostics.WarehouseExitInfo(
            context,
            $"CadillacEscalade warehouse-exit: guard ending reason={reason}, " +
            $"projection={(vehicle == null ? float.NaN : Vector3.Dot(vehicle.transform.position, outward)):0.000}, " +
            $"startProjection={startingProjection:0.000}.");
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
            CadillacEscaladeDiagnostics.WarehouseExitInfo(
                context,
                $"CadillacEscalade warehouse-exit: restoring triggerCount={warehouseExitGuardColliders.Count}, " +
                $"reason={reason}.");
        }

        foreach (var collider in warehouseExitGuardColliders)
        {
            if (collider != null)
                collider.enabled = true;
        }

        warehouseExitGuardColliders.Clear();
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
        if (isOpen)
            ScheduleInitialization("full-menu");
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

        if (!privateDriverPoolReady)
            privateDriverPoolReady = TryPreparePrivateDriverPool(source);

        while (BusinessLayoutSetHelper.loadingLayouts)
        {
            ConfigurePlayerVehiclesIfChanged(out var matchedCount);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);
            yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            if (!privateDriverPoolReady)
                privateDriverPoolReady = TryPreparePrivateDriverPool(source);
            if (!dealerReady)
                dealerReady = EnsureDealerStock(source);
            if (privateDriverRegistrationAllowed && !privateDriverReady)
                privateDriverReady = EnsurePrivateDriverSupport(source);
            ConfigurePlayerVehiclesIfChanged(out var matchedCount);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);

            var servicesReady =
                dealerReady &&
                privateDriverPoolReady &&
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
                $"CadillacEscalade: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
        if (!privateDriverPoolReady)
        {
            context?.Logger.Warn(
                $"CadillacEscalade: private-driver traffic pool not ready source='{source}'.");
        }
        if (privateDriverRegistrationAllowed && !privateDriverReady)
        {
            context?.Logger.Warn(
                $"CadillacEscalade: private-driver contracts not ready source='{source}'.");
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
            var ready = CadillacEscaladeLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                CadillacEscaladeDiagnostics.Info(context,
                    $"CadillacEscalade: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"CadillacEscalade: dealer stock update failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool EnsurePrivateDriverSupport(string source)
    {
        if (privateDriverReady)
            return true;
        if (playerVehiclePrefab == null)
            return false;

        if (!privateDriverPoolReady)
            privateDriverPoolReady = TryPreparePrivateDriverPool(source);
        if (!privateDriverPoolReady)
            return false;

        try
        {
            privateDriverReady =
                CadillacEscaladePrivateDriverSupport.EnsureVehicleAvailable(vehicleTypeName);
            if (privateDriverReady)
            {
                CadillacEscaladeDiagnostics.Info(context,
                    $"CadillacEscalade: available to advanced and premium private drivers " +
                    $"source='{source}'.");
            }
            return privateDriverReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"CadillacEscalade: private-driver contract update failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool TryPreparePrivateDriverPool(string source)
    {
        if (privateDriverPoolReady)
            return true;
        if (playerVehiclePrefab == null)
            return false;

        try
        {
            var ready =
                CadillacEscaladePrivateDriverSupport.PrepareTrafficPool(playerVehiclePrefab);
            if (ready)
            {
                privateDriverPreparationExceptionLogged = false;
                CadillacEscaladeDiagnostics.Info(context,
                    $"CadillacEscalade: private-driver traffic pool ready source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                privateDriverPreparationExceptionLogged = true;
                context?.Logger.Warn(
                    $"CadillacEscalade: private-driver traffic preparation failed " +
                    $"source='{source}': {exception.GetType().Name}: {exception.Message}");
            }
            return false;
        }
    }

    private void ResetPlayerVehicleSnapshot()
    {
        cachedPlayerVehicleCount = -1;
        cachedTargetVehicleCount = 0;
    }

    private void ConfigurePlayerVehiclesIfChanged(out int matchedCount)
    {
        var vehicles = VehicleHelper.AllPlayerVehicles;
        var playerVehicleCount = vehicles?.Count ?? 0;
        if (playerVehicleCount == cachedPlayerVehicleCount)
        {
            matchedCount = cachedTargetVehicleCount;
            return;
        }

        cachedPlayerVehicleCount = playerVehicleCount;
        matchedCount = 0;
        if (vehicles == null)
        {
            cachedTargetVehicleCount = 0;
            return;
        }

        foreach (var vehicle in vehicles)
        {
            if (!IsTargetVehicle(vehicle))
                continue;

            matchedCount++;
            TryConfigureVehicle(vehicle);
        }
        cachedTargetVehicleCount = matchedCount;
    }

    private void RefreshExistingVehiclePaint()
    {
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
            return;

        foreach (var vehicle in vehicles)
        {
            if (!IsTargetVehicle(vehicle))
                continue;

            vehicle
                .GetComponent<CadillacEscaladePaintController>()
                ?.RefreshCurrentColor();
        }
    }

    private void TryConfigureVehicle(VehicleController? vehicle)
    {
        if (!IsTargetVehicle(vehicle) || vehicle == null)
            return;

        // This must run before checking vehicleInstance: developer-tool spawns
        // can have no saved instance yet, but still need to support sleeping.
        ConfigureSleepEnvironment(vehicle);

        if (vehicle.vehicleInstance == null)
        {
            ConfigurePresentationOnly(vehicle);
            return;
        }

        var instanceId = vehicle.GetInstanceID();
        if (!configuredVehicleIds.Add(instanceId))
            return;

        try
        {
            var rigidbody = vehicle.GetComponent<Rigidbody>() ?? vehicle.GetComponentInParent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.mass = VehicleMass;
                rigidbody.centerOfMass = StableCenterOfMass;
                rigidbody.drag = 0f;
                rigidbody.angularDrag = 1.45f;

                var roadLoad = vehicle.GetComponent<CadillacEscaladeRoadLoad>();
                if (roadLoad == null)
                    roadLoad = vehicle.gameObject.AddComponent<CadillacEscaladeRoadLoad>();
                roadLoad.Initialize(rigidbody);
            }

            ConfigureMassProperties(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            const int bakedPositiveWheelMeshes = 8;
            ConfigureBodyColliders(vehicle.gameObject);
            ConfigureExitMarkers(vehicle.gameObject);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(vehicle.gameObject);
            var warehouseBounds = vehicle.GetComponent<CadillacEscaladeWarehouseBoundsController>();
            if (warehouseBounds == null)
            {
                warehouseBounds = vehicle.gameObject
                    .AddComponent<CadillacEscaladeWarehouseBoundsController>();
            }
            warehouseBounds.Initialize();
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var caliperController = vehicle.GetComponent<CadillacEscaladeCaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<CadillacEscaladeCaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialResult = CadillacEscaladeMaterials.FixSolidMaterials(vehicle.gameObject);
            var lightingController = vehicle.GetComponent<CadillacEscaladeLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<CadillacEscaladeLightingController>();
            lightingController.Initialize(vehicle, context);
            // Lighting assigns per-instance emissive materials to the labeled
            // overlay meshes. Capture them afterward so crash deformation keeps
            // every illuminated face attached to its source lamp geometry.
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var driverController = vehicle.GetComponent<CadillacEscaladeDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<CadillacEscaladeDriverController>();
            driverController.Initialize(vehicle, context);
            var paintController = vehicle.GetComponent<CadillacEscaladePaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<CadillacEscaladePaintController>();
            paintController.Initialize(vehicle, context);
            var glassController = vehicle.GetComponent<CadillacEscaladeGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<CadillacEscaladeGlassController>();
            glassController.Initialize(context);
            var audioController = vehicle.GetComponent<CadillacEscaladeAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<CadillacEscaladeAudioController>();
            audioController.Initialize(vehicle, context);
            var accelerationTelemetry =
                vehicle.GetComponent<CadillacEscaladeAccelerationTelemetry>();
            if (accelerationTelemetry == null)
            {
                accelerationTelemetry = vehicle.gameObject
                    .AddComponent<CadillacEscaladeAccelerationTelemetry>();
            }
            accelerationTelemetry.Initialize(vehicle, context);

            CadillacEscaladeDiagnostics.Info(context,
                $"CadillacEscalade: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=10-speed-automatic, awd=40:60, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstaclesNormalized={normalizedNavMeshObstacles}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"downshift={DownshiftRpm:0}rpm, " +
                $"powerCurve=2021-L87-road-calibration, brakes={BrakeMaxTorque:0}Nm, " +
                $"steeringCalipers=4, " +
                $"bakedPositiveWheelMeshes={bakedPositiveWheelMeshes}, " +
                $"materialRenderers={materialResult.RendererCount}, " +
                $"decalMasksCleared={materialResult.DecalMasksCleared}, " +
                $"opaqueFixed={materialResult.OpaqueMaterialsFixed}, " +
                $"transparentFixed={materialResult.TransparentMaterialsFixed}, " +
                $"cabinGlass={materialResult.CabinGlassRenderers}/" +
                $"reenabled={materialResult.CabinGlassRenderersReenabled}, " +
                $"rimSlotsNormalized={materialResult.RimSlotsNormalized}, " +
                $"hdrpValidated={materialResult.MaterialsValidated}.");
        }
        catch (Exception exception)
        {
            configuredVehicleIds.Remove(instanceId);
            context?.Logger.Warn(
                $"CadillacEscalade: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
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
            {
                return IsCarSleepConfig(currentConfig);
            }

            var carSleepConfig = ResolveNativeCarSleepConfig();
            if (carSleepConfig == null)
            {
                if (!nativeCarSleepConfigUnavailableLogged)
                {
                    nativeCarSleepConfigUnavailableLogged = true;
                    context?.Logger.Warn(
                        "CadillacEscalade: native car sleep configuration was unavailable; " +
                        "sleeping in this vehicle will remain disabled.");
                }

                return false;
            }

            configField.SetValue(environment, carSleepConfig);
            environmentField.SetValue(vehicle, environment);
            CadillacEscaladeDiagnostics.Info(context,
                $"CadillacEscalade: configured native car sleep environment " +
                $"vehicle={vehicle.GetInstanceID()} donor=HonzaMimic.");
            return true;
        }
        catch (Exception exception)
        {
            if (!nativeCarSleepConfigUnavailableLogged)
            {
                nativeCarSleepConfigUnavailableLogged = true;
                context?.Logger.Warn(
                    "CadillacEscalade: could not configure the native car sleep environment: " +
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
        var environmentType = GetMember(candidate, "sleepEnvironmentType");
        return environmentType != null && Convert.ToInt32(environmentType) == 1;
    }

    private void ConfigurePresentationOnly(VehicleController vehicle)
    {
        CadillacEscaladeMaterials.FixSolidMaterials(vehicle.gameObject);
        var paintController = vehicle.GetComponent<CadillacEscaladePaintController>();
        if (paintController == null)
        {
            paintController = vehicle.gameObject.AddComponent<CadillacEscaladePaintController>();
            paintController.Initialize(vehicle, context);
        }
        var glassController = vehicle.GetComponent<CadillacEscaladeGlassController>();
        if (glassController == null)
        {
            glassController = vehicle.gameObject.AddComponent<CadillacEscaladeGlassController>();
            glassController.Initialize(context);
        }
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        SetLocalPosition(root, "Animate_SteeringWheel_033", new Vector3(-0.52f, 1.15f, 0.68f));
        SetLocalPosition(root, "Driverside", new Vector3(-1.75f, 0.10f, 0.30f));
        SetLocalPosition(root, "Passengerside", new Vector3(1.75f, 0.10f, 0.30f));
    }

    private static void SetLocalPosition(GameObject root, string childName, Vector3 position)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, childName, StringComparison.Ordinal))
                continue;
            transform.localPosition = position;
            return;
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
                SetFloat(spring, "maxForce", 26000f);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", 0.408f);
                SetFloat(wheel, "width", 0.285f);
                var sideFriction = GetMember(component, "sideFriction");
                SetFloat(
                    sideFriction,
                    "grip",
                    isFront ? FrontLateralGrip : RearLateralGrip);
                SetFloat(component, "frictionCircleStrength", TireFrictionCircleStrength);
            }
        }
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

    private static void ConfigureBodyColliders(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BodyCollider", StringComparison.Ordinal))
                continue;

            var colliders = transform.GetComponents<BoxCollider>();
            if (colliders.Length > 0)
            {
                colliders[0].center = LowerColliderCenter;
                colliders[0].size = LowerColliderSize;
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = UpperColliderCenter;
                colliders[1].size = UpperColliderSize;
            }
        }
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
                $"CadillacEscalade damage vehicle={vehicle.GetInstanceID()}: " +
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
                (renderer.enabled || filter.name.StartsWith("CadillacEscalade_", StringComparison.Ordinal)))
                filters.Add(filter);
        }

        if (filters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"CadillacEscalade damage vehicle={vehicle.GetInstanceID()}: " +
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

        var visualDamage = vehicle.GetComponent<CadillacEscaladeVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<CadillacEscaladeVisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        CadillacEscaladeDiagnostics.Info(context,
            $"CadillacEscalade damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps " +
            $"filters=[{string.Join(", ", filters.ConvertAll(filter => filter.name))}]; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        var name = filter.name;
        if (name.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("glass_windows", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("_int_", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("screen", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.StartsWith("CadillacWheel", StringComparison.Ordinal) ||
            name.StartsWith("CadillacBrake", StringComparison.Ordinal) ||
            name.StartsWith("CadillacFixedCaliper", StringComparison.Ordinal) ||
            name.StartsWith("polySurface", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.StartsWith("CadillacDamageBody", StringComparison.Ordinal) ||
               name.StartsWith("CadillacEscalade_", StringComparison.Ordinal) ||
               ContainsAny(name,
                   "combined_mesh", "blackout", "black_smooth", "smooth_plastics",
                   "painted_black", "panited_gloss", "misc_primer", "bright_chrome",
                   "galvano", "stainless_steel", "steel_cast", "clear_plastic",
                   "LED_Light_Pipe", "rubber",
                   "tail_lamp", "rear_etchings", "rear_turn_signals", "chml",
                   "reflectorGlass", "running_headlight", "running_facia_lamps",
                   "high_beams", "headlights_etched", "headlight_metals",
                   "etches_light", "front_emblem", "rear_emblem", "chrome_badges",
                   "Grille", "Kit2_Coloured", "Light_Geo", "ManufacturerPlate");
    }

    private static bool ContainsAny(string value, params string[] markers)
    {
        foreach (var marker in markers)
            if (value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
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
            var brakes = GetMember(component, "brakes");
            SetFloat(brakes, "maxTorque", BrakeMaxTorque);
            SetFloat(brakes, "actuationTime", BrakeActuationTime);
            var clutch = GetMember(powertrain, "clutch");
            SetFloat(clutch, "engagementRPM", ClutchEngagementRpm);
            SetFloat(clutch, "throttleEngagementOffsetRPM", ClutchThrottleOffsetRpm);
            SetFloat(clutch, "engagementRange", ClutchEngagementRange);
            SetFloat(clutch, "creepTorque", ClutchCreepTorque);
            SetFloat(clutch, "creepSpeedLimit", 1f);
            var engine = GetMember(powertrain, "engine");
            SetFloat(engine, "inertia", EngineInertia);
            SetFloat(engine, "maxPower", EnginePowerKw);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateEscaladePowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", false);
            SetFloat(forcedInduction, "powerGainMultiplier", 1f);
            SetFloat(forcedInduction, "spoolUpTime", 0f);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.25f);
            SetFloat(transmission, "_downshiftRPM", DownshiftRpm);
            SetFloat(transmission, "_upshiftRPM", 5750f);
            SetInt(transmission, "forwardGearCount", 10);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", EscaladeGears);

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
                    SetBool(module, "active", true);
                    SetFloat(module, "speedLimit", SpeedLimitKph);
                }
            }

            return GetInt(transmission, "forwardGearCount") == 10;
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

[DisallowMultipleComponent]
internal sealed class CadillacEscaladeRoadLoad : MonoBehaviour
{
    // 0.5 * air density * Cd (0.34) * estimated frontal area (3.35 m2).
    // NWH already supplies low-speed tire losses, so aerodynamic resistance is
    // introduced above urban speeds without double-counting rolling resistance.
    private const float AerodynamicForceCoefficient = 0.70f;
    private const float MinimumDragSpeedMps = 20f;

    private Rigidbody? body;

    internal void Initialize(Rigidbody vehicleBody)
    {
        body = vehicleBody;
    }

    private void FixedUpdate()
    {
        if (body == null || body.isKinematic)
            return;

        var planarVelocity = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
        var speedSquared = planarVelocity.sqrMagnitude;
        var minimumSpeedSquared = MinimumDragSpeedMps * MinimumDragSpeedMps;
        if (speedSquared <= minimumSpeedSquared)
            return;

        var dragForce = AerodynamicForceCoefficient *
                        (speedSquared - minimumSpeedSquared);
        body.AddForce(-planarVelocity.normalized * dragForce, ForceMode.Force);
    }
}

[AddComponentMenu("")]
public sealed class CadillacEscaladeGlassController : MonoBehaviour
{
    private readonly List<Renderer> cabinGlass = new List<Renderer>();
    private readonly HashSet<Renderer> rearCabinGlass = new HashSet<Renderer>();
    private readonly Dictionary<Material, Material> runtimeMaterials =
        new Dictionary<Material, Material>();
    private readonly Dictionary<Material, Material> rearRuntimeMaterials =
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
        rearCabinGlass.Clear();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var rearGlass = IsRearCabinGlass(renderer.transform);
            var materials = renderer.sharedMaterials;
            var containsCabinGlass = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null ||
                    !CadillacEscaladeMaterials.IsCabinGlassMaterial(source))
                {
                    continue;
                }

                containsCabinGlass = true;
                var materialMap = rearGlass ? rearRuntimeMaterials : runtimeMaterials;
                if (!materialMap.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name +
                                           (rearGlass ? "_RuntimeRearCabinGlass" : "_RuntimeCabinGlass");
                    if (rearGlass)
                        CadillacEscaladeMaterials.RestoreRearCabinGlassMaterial(runtimeMaterial);
                    else
                        CadillacEscaladeMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
                    materialMap.Add(source, runtimeMaterial);
                }
                materials[index] = runtimeMaterial;
            }
            if (!containsCabinGlass)
                continue;
            renderer.sharedMaterials = materials;
            cabinGlass.Add(renderer);
            if (rearGlass)
                rearCabinGlass.Add(renderer);
        }
        initialized = true;
        EnsureVisible("initialize");
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
                    CadillacEscaladeMaterials.IsCabinGlassMaterial(material))
                {
                    renderer.SetPropertyBlock(null, index);
                    if (rearCabinGlass.Contains(renderer))
                        CadillacEscaladeMaterials.RestoreRearCabinGlassMaterial(material);
                    else
                        CadillacEscaladeMaterials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            CadillacEscaladeDiagnostics.Info(context,
                $"CadillacEscalade glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, rearRenderers={rearCabinGlass.Count}, " +
                $"runtimeMaterials={runtimeMaterials.Count + rearRuntimeMaterials.Count}, " +
                "shader=HDRP/Unlit, frontTint=(0.08,0.09,0.10,0.12), " +
                "rearTint=(0.045,0.05,0.055,0.25), deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            CadillacEscaladeDiagnostics.Info(context,
                $"CadillacEscalade glass vehicle={GetInstanceID()}: repaired after " +
                $"'{source}' renderers={restored}, propertyBlocks={propertyBlocksCleared}.");
        }
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
        foreach (var material in rearRuntimeMaterials.Values)
        {
            if (material != null)
                Destroy(material);
        }
        rearRuntimeMaterials.Clear();
        rearCabinGlass.Clear();
    }

    private static bool IsRearCabinGlass(Transform transform)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf("glass_windows", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
}

[AddComponentMenu("")]
public sealed class CadillacEscaladeVisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.70f;
    private const float MaximumDentDepth = 0.24f;
    private const float DepthPerExcessMps = 0.011f;
    private const float FrontDentLateralRadius = 0.82f;
    private const float FrontDentVerticalRadius = 0.68f;
    private const float FrontDentLongitudinalRadius = 0.95f;
    private const float MaximumFrontDentDepth = 0.28f;
    private const float FrontDepthPerExcessMps = 0.012f;
    private const float RearDentLateralRadius = 0.90f;
    private const float RearDentVerticalRadius = 0.72f;
    private const float RearDentLongitudinalRadius = 1.02f;
    private const float MaximumRearDentDepth = 0.40f;
    private const float RearDepthPerExcessMps = 0.012f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.5f;
    private const int MaximumDiagnosticLogs = 6;

    private readonly List<MeshFilter> deformableFilters = new List<MeshFilter>();
    private readonly Dictionary<MeshFilter, Vector3[]> originalVertices =
        new Dictionary<MeshFilter, Vector3[]>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private ModContext? context;
    private Rigidbody? body;
    private float impactThresholdMps;
    private float nextCollisionTime;
    private float previousDamage;
    private float previousSavedDamage;
    private int diagnosticLogs;
    private Coroutine? repairPowertrainCoroutine;
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
        deformableFilters.Clear();
        originalVertices.Clear();
        runtimeMeshes.Clear();
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            var runtimeMesh = Instantiate(filter.sharedMesh);
            runtimeMesh.name = filter.sharedMesh.name + "_RuntimeDamage";
            filter.sharedMesh = runtimeMesh;
            deformableFilters.Add(filter);
            originalVertices[filter] = runtimeMesh.vertices;
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
        var repaired = (previousDamage > 0.001f && currentDamage <= 0.001f) ||
                       (previousSavedDamage > 0.001f && currentSavedDamage <= 0.001f);
        if (repaired)
        {
            foreach (var pair in originalVertices)
            {
                if (pair.Key == null || pair.Key.sharedMesh == null)
                    continue;
                var mesh = pair.Key.sharedMesh;
                mesh.vertices = pair.Value;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
            }
            CadillacEscaladeDiagnostics.Info(context,
                $"CadillacEscalade damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
            if (repairPowertrainCoroutine != null)
                StopCoroutine(repairPowertrainCoroutine);
            repairPowertrainCoroutine = StartCoroutine(RestorePowertrainAfterRepair());
        }
        previousDamage = currentDamage;
        previousSavedDamage = currentSavedDamage;
    }

    private IEnumerator RestorePowertrainAfterRepair()
    {
        // Vanilla repair restores NWH component health but does not restart an
        // engine that was stopped when the vehicle reached total damage.
        yield return null;
        var physics = vehicle?.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        if (physics == null || damageHandler == null)
        {
            repairPowertrainCoroutine = null;
            yield break;
        }

        damageHandler.Repair();
        if (vehicle == null || !vehicle.controlledByPlayer)
        {
            repairPowertrainCoroutine = null;
            yield break;
        }

        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        engine.StopEngine();
        transmission.ShiftInto(0, true);
        transmission.currentGearRatio = 0f;
        yield return new WaitForSecondsRealtime(0.15f);

        engine.StartEngine();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            yield return new WaitForSecondsRealtime(0.45f);
            if (vehicle == null || !vehicle.controlledByPlayer)
                break;
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            if (engine.IsRunning && engine.ignition && engine.canRun && rpm >= 300f)
            {
                transmission.ShiftInto(1, true);
                CadillacEscaladeDiagnostics.Info(context,
                    $"CadillacEscalade drivetrain vehicle={vehicle.GetInstanceID()}: " +
                    $"restarted after repair attempt={attempt}, rpm={rpm:0}.");
                repairPowertrainCoroutine = null;
                yield break;
            }
            engine.StartEngine();
        }

        context?.Logger.Warn(
            $"CadillacEscalade drivetrain vehicle={vehicle?.GetInstanceID()}: " +
            "engine did not restart after repair.");
        repairPowertrainCoroutine = null;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || collision == null || Time.unscaledTime < nextCollisionTime ||
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
            var dentDepth = Mathf.Clamp(excessSpeed * DepthPerExcessMps, 0.025f, MaximumDentDepth);
            var frontDentDepth = Mathf.Clamp(
                excessSpeed * FrontDepthPerExcessMps,
                0.04f,
                MaximumFrontDentDepth);
            var rearDentDepth = Mathf.Clamp(
                excessSpeed * RearDepthPerExcessMps,
                0.04f,
                MaximumRearDentDepth);
            var center = body != null ? body.worldCenterOfMass : transform.position;
            var primaryLocalContact = transform.InverseTransformPoint(contacts[0].point);
            var changedMeshes = 0;
            var changedVertices = 0;
            var frontImpact = false;
            var rearImpact = false;

            foreach (var filter in deformableFilters)
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                var meshChanged = false;
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
                                ? FrontDentLateralRadius
                                : RearDentLateralRadius;
                            var verticalRadius = isFrontContact
                                ? FrontDentVerticalRadius
                                : RearDentVerticalRadius;
                            var longitudinalRadius = isFrontContact
                                ? FrontDentLongitudinalRadius
                                : RearDentLongitudinalRadius;
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
                        selectedDepth = isEndContact
                            ? isFrontContact ? frontDentDepth : rearDentDepth
                            : dentDepth;
                        selectedEndImpact = isEndContact;
                        selectedFrontImpact = isFrontContact;
                    }

                    if (strongestInfluence <= 0f || inwardDirection.sqrMagnitude < 0.5f)
                        continue;
                    var falloff = selectedEndImpact
                        ? Mathf.Pow(strongestInfluence, 1.35f)
                        : strongestInfluence * strongestInfluence;
                    worldVertex += inwardDirection * (selectedDepth * falloff);
                    vertices[vertexIndex] = filter.transform.InverseTransformPoint(worldVertex);
                    changedVertices++;
                    meshChanged = true;
                    frontImpact |= selectedEndImpact && selectedFrontImpact;
                    rearImpact |= selectedEndImpact && !selectedFrontImpact;
                }

                if (!meshChanged)
                    continue;
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                changedMeshes++;
            }

            if (diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                CadillacEscaladeDiagnostics.Info(context,
                    $"CadillacEscalade damage vehicle={vehicle?.GetInstanceID()}: inward dent " +
                    $"contact='{collision.collider?.name ?? "unknown"}' " +
                    $"relativeSpeed={collision.relativeVelocity.magnitude * 3.6f:0.0}kph " +
                    $"localContact=({primaryLocalContact.x:0.00}," +
                    $"{primaryLocalContact.y:0.00},{primaryLocalContact.z:0.00}) " +
                    $"region={(frontImpact ? "front" : rearImpact ? "rear" : "side")} " +
                    $"depth={(frontImpact ? frontDentDepth : rearImpact ? rearDentDepth : dentDepth):0.000}m " +
                    $"meshes={changedMeshes} vertices={changedVertices} " +
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
                $"CadillacEscalade damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void OnDestroy()
    {
        if (repairPowertrainCoroutine != null)
            StopCoroutine(repairPowertrainCoroutine);
        repairPowertrainCoroutine = null;
        foreach (var mesh in runtimeMeshes)
            if (mesh != null) Destroy(mesh);
        runtimeMeshes.Clear();
    }
}
