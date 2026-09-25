#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BusinessLayoutSets;
using Helpers;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

public sealed class ChevroletCamaro1967Runtime : MonoBehaviour
{
    // Reuse the game's native player-car sleep configuration so dealer cars
    // and developer-tool spawns expose the same sleep action as vanilla cars.
    private const string NativeCarSleepDonorPrefabPath =
        "Vehicles/PlayerVehicles/HonzaMimic";
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const string VehicleRepainterColorRestoredEvent =
        "vehicle-repainter:color-restored";
    private const float VehicleMass = 1483f;
    private const float RatedEnginePowerKw = 220f;
    private const float PhysicsEnginePowerKw = 140f;
    private const float EngineLossPercent = 0.32f;
    private const float EngineIdleRpm = 750f;
    private const float EngineLimitRpm = 5600f;
    private const float SpeedLimitKph = 190f;
    private const float FinalDriveRatio = 4.11f;
    private const float EngineInertia = 0.28f;
    private const float EngineStartDuration = 0.55f;
    // The dual-clutch transmission does not need a manual-car launch flare.
    // Keep its engagement just above idle so N -> first applies torque without
    // the observed 2,000 RPM pause.
    private const float ClutchEngagementRpm = 1125f;
    private const float ClutchThrottleOffsetRpm = 550f;
    private const float ClutchEngagementRange = 700f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 0.78f;
    private const float AntiRollBarForce = 6800f;
    private const float FrontSuspensionTravel = 0.105f;
    private const float RearSuspensionTravel = 0.115f;
    private const float FrontTireRadius = 0.320f;
    private const float RearTireRadius = 0.320f;
    private const float FrontTireWidth = 0.236f;
    private const float RearTireWidth = 0.236f;
    private const float WheelCenterRideHeightOffset = -0.135f;
    private const float VehicleLinearDrag = 0.045f;
    private const float BrakeMaxTorque = 1060f;
    private const float FrontForwardGrip = 0.72f;
    private const float RearForwardGrip = 0.30f;
    private const float FrontForwardStiffness = 0.96f;
    private const float RearForwardStiffness = 0.82f;
    private const float DeformationStrength = 0.22f;
    private const float DeformationRadius = 0.28f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 1f;
    private const float DamageDecelerationThreshold = 500f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private const float WarehouseExitGuardDuration = 8f;
    private const float WarehouseExitGuardClearDistance = 4f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.15f, -0.06f);
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.43f, 1.86f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.72f, 0.46f, 0.92f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.43f, -1.86f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.72f, 0.46f, 0.92f);
    private static readonly Vector3 DriverExitPosition =
        new Vector3(-2.10f, 0.20f, 0.15f);
    private static readonly Vector3 PassengerExitPosition =
        new Vector3(2.10f, 0.20f, 0.15f);

    private static readonly float[] CamaroM22Gears =
    {
        -2.26f, 0f, 2.20f, 1.64f, 1.28f, 1.00f,
    };

    private static AnimationCurve CreateCamaroPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.13f, 0.09f),
            new Keyframe(0.27f, 0.29f),
            new Keyframe(0.45f, 0.58f),
            new Keyframe(0.57f, 0.785f),
            new Keyframe(0.71f, 0.90f),
            new Keyframe(0.86f, 1.00f),
            new Keyframe(0.96f, 0.93f),
            new Keyframe(1.00f, 0.87f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private int enteredVehicleActivationInstanceId;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private ChevroletCamaro1967WarehouseEntryController? warehouseExitGuardEntryController;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private int cachedPlayerVehicleCount = -1;
    private bool dealerRegistrationReady;
    private bool dealerReadyLogged;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;
    private GameObject? playerVehiclePrefab;
    private UnityEngine.Object? nativeCarSleepConfig;
    private bool nativeCarSleepConfigUnavailableLogged;

    public static ChevroletCamaro1967Runtime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<ChevroletCamaro1967Runtime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(ChevroletCamaro1967Runtime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<ChevroletCamaro1967Runtime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        ChevroletCamaro1967PrivateDriverSupport.SetContext(context);
        var trafficFrequency =
            runtime.GetComponent<ChevroletCamaro1967TrafficFrequency>() ??
            runtime.gameObject.AddComponent<ChevroletCamaro1967TrafficFrequency>();
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
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
        StopWarehouseExitGuard();
        configuredVehicleIds.Clear();
        dealerRegistrationReady = false;
        dealerReadyLogged = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        nativeCarSleepConfig = null;
        nativeCarSleepConfigUnavailableLogged = false;
        ChevroletCamaro1967PrivateDriverSupport.RemoveVehicle(vehicleTypeName);
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
    }

    private void Update()
    {
        // Dealer purchases do not raise onEnterVehicle. Keep the hot path to
        // one count comparison and enumerate only after the collection changes.
        var vehicles = VehicleHelper.AllPlayerVehicles;
        var vehicleCount = vehicles?.Count ?? 0;
        if (vehicleCount == cachedPlayerVehicleCount)
            return;

        ConfigureExistingVehicles(out _);
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

    private void HandleGameEvent(string eventName)
    {
        if (string.Equals(
                eventName,
                VehicleRepainterColorRestoredEvent,
                StringComparison.Ordinal))
        {
            // The Repainter restores all parked custom colors in one pass. Rebind
            // each loaded player-owned Camaro from its own persisted VehicleInstance
            // rather than relying on whichever vehicle is currently selected.
            RestoreSavedPaintForExistingVehicles("vehicle-repainter:color-restored");
            return;
        }

        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        ChevroletCamaro1967Diagnostics.DealerEntryInfo(
            context,
            $"ChevroletCamaro1967 dealer-entry: game-event='{eventName}', " +
            DescribeVehicleState(selectedVehicle));

        // Dealer purchases do not reliably invoke onEnterVehicle. This event
        // is a bounded lifecycle handoff from the dealer display vehicle to
        // the owned, player-controlled instance, so configure it here rather
        // than scanning vehicles every frame.
        TryConfigureVehicle(selectedVehicle);
        selectedVehicle!
            .GetComponent<ChevroletCamaro1967PaintController>()
            ?.ApplyCurrentColor("game-event");
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SubscribeEvents();
        RestoreSavedPaintForExistingVehicles($"scene-loaded:{scene.name}");
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeEvents();
        if (ChevroletCamaro1967LoadRecovery.CompleteInterruptedLoad(context))
            StartCoroutine(ReportLoadedInputState());
        privateDriverRegistrationAllowed = true;
        RestoreSavedPaintForExistingVehicles("game-loaded-late");
        ScheduleInitialization("game-loaded-late");
    }

    private IEnumerator ReportLoadedInputState()
    {
        // The native loading screen fades out for 0.8 seconds after this event.
        yield return new WaitForSecondsRealtime(2f);
        ChevroletCamaro1967LoadRecovery.ReportInputState(context);
    }

    private void HandleGameUnloaded()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
        StopWarehouseExitGuard();
        configuredVehicleIds.Clear();
        cachedPlayerVehicleCount = -1;
        dealerRegistrationReady = false;
        dealerReadyLogged = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        nativeCarSleepConfig = null;
        nativeCarSleepConfigUnavailableLogged = false;
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        ChevroletCamaro1967Diagnostics.DealerEntryInfo(
            context,
            $"ChevroletCamaro1967 dealer-entry: onEnterVehicle, {DescribeVehicleState(vehicle)}");
        TryConfigureVehicle(vehicle);
        vehicle.GetComponent<ChevroletCamaro1967GlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle == null || !IsTargetVehicle(vehicle))
            return;
        vehicle.GetComponent<ChevroletCamaro1967PaintController>()
            ?.RestoreAfterVehicleEntered();
        ScheduleEnteredVehicleActivation(vehicle);
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;
        ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
            context,
            $"ChevroletCamaro1967 warehouse-exit: onExitVehicle, {DescribeVehicleState(vehicle)}");
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = StartCoroutine(RecoverPlayerNavMeshAfterExit(vehicle));
    }

    private IEnumerator RecoverPlayerNavMeshAfterExit(VehicleController exitedVehicle)
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        var root = PlayerHelper.PlayerController?.transform;
        if (root == null)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }

        var agents = root.GetComponentsInChildren<NavMeshAgent>(true);
        var needsRecovery = false;
        foreach (var agent in agents)
            needsRecovery |= agent != null && agent.enabled && !agent.isOnNavMesh;
        var initialExitClear = IsPlayerExitClear(root, root.position);
        needsRecovery |= !initialExitClear;
        ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
            context,
            $"ChevroletCamaro1967 player-exit: deferred validation playerPosition={root.position}, " +
            $"agentCount={agents.Length}, anyAgentOffNavMesh={agents.Any(agent => agent != null && agent.enabled && !agent.isOnNavMesh)}, " +
            $"initialClear={initialExitClear}, needsRecovery={needsRecovery}.");
        if (!needsRecovery)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }
        if (!TryFindClearExitPosition(root, exitedVehicle, out var target))
        {
            ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
                context,
                "ChevroletCamaro1967 player-exit: recovery failed; no NavMesh/capsule-clear candidate found.");
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
                if (controller != null) controller.enabled = false;
            foreach (var agent in agents)
                if (agent != null) agent.enabled = false;
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
                if (characterControllers[index] != null)
                    characterControllers[index].enabled = controllerStates[index];
            Physics.SyncTransforms();
        }
        ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
            context,
            $"ChevroletCamaro1967 player-exit: recovery moved player to {target}.");
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
            vehicleTransform.position - vehicleTransform.right * 2.05f,
            vehicleTransform.position + vehicleTransform.right * 2.05f,
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

    private bool IsTargetVehicle(VehicleController? vehicle) =>
        vehicle?.vehicleInstance != null &&
        string.Equals(
            vehicle.vehicleInstance.vehicleTypeName,
            vehicleTypeName,
            StringComparison.Ordinal);

    private static string DescribeVehicleState(VehicleController? vehicle)
    {
        if (vehicle == null)
            return "vehicle=<null>";

        var physics = vehicle.GetComponent<PhysicsVehicle>() ??
                      vehicle.GetComponentInChildren<PhysicsVehicle>(true);
        var rigidbody = vehicle.GetComponent<Rigidbody>() ??
                        vehicle.GetComponentInChildren<Rigidbody>(true) ??
                        vehicle.GetComponentInParent<Rigidbody>();
        var engine = physics?.powertrain?.engine;
        var transmission = physics?.powertrain?.transmission;
        return $"instance={vehicle.GetInstanceID()}, controlled={vehicle.controlledByPlayer}, " +
               $"selected={ReferenceEquals(InstanceBehavior<GameManager>.Instance?.selectedVehicle, vehicle)}, " +
               $"position={vehicle.transform.position}, physics={(physics == null ? "missing" : physics.enabled.ToString())}, " +
               $"kinematic={rigidbody?.isKinematic}, constraints={rigidbody?.constraints}, " +
               $"engineRunning={engine?.IsRunning}, ignition={engine?.ignition}, canRun={engine?.canRun}, " +
               $"rpm={(engine == null ? "n/a" : (engine.RPMPercent * engine.revLimiterRPM).ToString("0"))}, " +
               $"gear={transmission?.Gear}, ratio={transmission?.currentGearRatio:0.000}.";
    }

    private void ScheduleEnteredVehicleActivation(VehicleController vehicle)
    {
        if (!vehicle.controlledByPlayer &&
            !ReferenceEquals(InstanceBehavior<GameManager>.Instance?.selectedVehicle, vehicle))
        {
            ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                context,
                $"ChevroletCamaro1967 dealer-entry: activation skipped because the vehicle is " +
                $"neither controlled nor selected, {DescribeVehicleState(vehicle)}");
            return;
        }

        var instanceId = vehicle.GetInstanceID();
        if (enteredVehicleActivationCoroutine != null &&
            enteredVehicleActivationInstanceId == instanceId)
        {
            ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                context,
                $"ChevroletCamaro1967 dealer-entry: activation already pending instance={instanceId}.");
            return;
        }

        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);

        enteredVehicleActivationInstanceId = instanceId;
        ChevroletCamaro1967Diagnostics.DealerEntryInfo(
            context,
            $"ChevroletCamaro1967 dealer-entry: activation scheduled, {DescribeVehicleState(vehicle)}");
        enteredVehicleActivationCoroutine = StartCoroutine(ActivateEnteredVehicle(vehicle));
    }

    private IEnumerator ActivateEnteredVehicle(VehicleController vehicle)
    {
        // Match the working BMW/Cadillac dealer-entry pattern: native entry
        // owns the vehicle physics and wheel state. Only restart the
        // powertrain when it stayed dormant after that native transition.
        yield return new WaitForSecondsRealtime(.25f);
        var physics = vehicle.GetComponent<PhysicsVehicle>() ??
                      vehicle.GetComponentInChildren<PhysicsVehicle>(true);
        if (physics == null)
        {
            ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                context,
                $"ChevroletCamaro1967 dealer-entry: activation failed; NWH controller missing, " +
                DescribeVehicleState(vehicle));
            enteredVehicleActivationCoroutine = null;
            enteredVehicleActivationInstanceId = 0;
            yield break;
        }

        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        for (var attempt = 0; attempt < EngineStartAttemptCount; attempt++)
        {
            if (vehicle == null || !vehicle.controlledByPlayer)
            {
                ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                    context,
                    $"ChevroletCamaro1967 dealer-entry: activation cancelled attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }

            // Paint assignment from the dealer is asynchronous too; retain a
            // bounded entry refresh without turning it into runtime polling.
            vehicle.GetComponent<ChevroletCamaro1967PaintController>()
                ?.ApplyCurrentColor("vehicle-entered");
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                context,
                $"ChevroletCamaro1967 dealer-entry: activation attempt={attempt + 1}, " +
                $"running={engine.IsRunning}, ignition={engine.ignition}, canRun={engine.canRun}, " +
                $"rpm={rpm:0}, gear={transmission.Gear}, " +
                $"ratio={transmission.currentGearRatio:0.000}, physicsEnabled={physics.enabled}.");
            if (engine.IsRunning && engine.ignition && engine.canRun &&
                rpm >= MinimumHealthyEngineRpm)
            {
                // Do not overwrite an intentional reverse selection. The
                // native entry leaves an unselected gearbox at exactly zero.
                if (transmission.Gear == 0)
                {
                    transmission.ShiftInto(1, true);
                    ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                        context,
                        "ChevroletCamaro1967 dealer-entry: native engine healthy; shifted neutral to first.");
                    yield return new WaitForFixedUpdate();
                    ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                        context,
                        $"ChevroletCamaro1967 dealer-entry: first-gear settle, gear={transmission.Gear}, " +
                        $"ratio={transmission.currentGearRatio:0.000}.");
                }
                else
                {
                    ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                        context,
                        $"ChevroletCamaro1967 dealer-entry: native engine healthy; preserved gear={transmission.Gear}.");
                }
                break;
            }

            ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                context,
                $"ChevroletCamaro1967 dealer-entry: dormant powertrain; restart attempt={attempt + 1}.");
            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
            {
                ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                    context,
                    $"ChevroletCamaro1967 dealer-entry: restart aborted after stop attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }
            engine.StartEngine();
            yield return new WaitForSecondsRealtime(.75f);
            if (vehicle != null && vehicle.controlledByPlayer &&
                transmission.Gear == 0)
            {
                transmission.ShiftInto(1, true);
                ChevroletCamaro1967Diagnostics.DealerEntryInfo(
                    context,
                    $"ChevroletCamaro1967 dealer-entry: restart attempt={attempt + 1} shifted neutral to first.");
            }
            yield return new WaitForSecondsRealtime(.15f);
        }

        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        ChevroletCamaro1967Diagnostics.DealerEntryInfo(
            context,
            $"ChevroletCamaro1967 dealer-entry: activation completed, {DescribeVehicleState(vehicle)}");
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!ChevroletCamaro1967CityCarsDealerStock.IsTargetDealer(registration?.BusinessName) ||
            dealerRegistrationReady)
        {
            return;
        }

        if (BusinessLayoutSetHelper.loadingLayouts)
        {
            ScheduleInitialization("dealer-entered");
            return;
        }

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
            ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
                context,
                $"ChevroletCamaro1967 warehouse-exit: guard skipped; current vehicle is not " +
                $"the controlled Camaro ({DescribeVehicleState(vehicle)}).");
            return;
        }

        var entrance = FindClosestDriveInEntrance(vehicle.transform.position);
        if (entrance == null)
        {
            ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
                context,
                $"ChevroletCamaro1967 warehouse-exit: guard skipped; no DriveInEntrance found, " +
                DescribeVehicleState(vehicle));
            return;
        }

        var nativeMeshCollider = vehicle.GetComponentInChildren<MeshCollider>(true);
        ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
            context,
            $"ChevroletCamaro1967 warehouse-exit: native exit completed, {DescribeVehicleState(vehicle)} " +
            $"entrance='{entrance.name}' entrancePosition={entrance.transform.position}, " +
            $"nativeMesh='{nativeMeshCollider?.name ?? "missing"}', " +
            $"nativeMeshLength={nativeMeshCollider?.sharedMesh?.bounds.size.z:0.000}.");

        StopWarehouseExitGuard();
        warehouseExitGuardEntryController =
            vehicle.GetComponent<ChevroletCamaro1967WarehouseEntryController>();
        warehouseExitGuardEntryController?.SuppressEntrance(entrance, "warehouse-exit-guard");
        foreach (var enterTrigger in entrance.GetComponentsInChildren<DriveInEntranceEnterTrigger>(true))
        foreach (var collider in enterTrigger.GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled || !collider.isTrigger)
                continue;

            warehouseExitGuardColliders.Add(collider);
            ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
                context,
                $"ChevroletCamaro1967 warehouse-exit: disabling entry trigger " +
                $"'{collider.name}' bounds={collider.bounds}.");
            collider.enabled = false;
        }

        if (warehouseExitGuardColliders.Count == 0)
        {
            warehouseExitGuardEntryController?.ClearSuppressedEntrance(
                entrance,
                "no-native-entry-trigger");
            warehouseExitGuardEntryController = null;
            ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
                context,
                "ChevroletCamaro1967 warehouse-exit: guard skipped; matching entrance had no enabled trigger colliders.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(
            vehicle.transform.position - entrance.transform.position,
            Vector3.up);
        if (outward.sqrMagnitude < .0001f)
            outward = Vector3.ProjectOnPlane(entrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < .0001f)
        {
            ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
                context,
                "ChevroletCamaro1967 warehouse-exit: guard aborted; outward direction was zero.");
            StopWarehouseExitGuard();
            return;
        }

        outward.Normalize();
        var startingDistance = Vector3.Dot(vehicle.transform.position, outward);
        Physics.SyncTransforms();
        ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
            context,
            $"ChevroletCamaro1967 warehouse-exit: guard started triggerCount={warehouseExitGuardColliders.Count}, " +
            $"outward={outward}, startProjection={startingDistance:0.000}, " +
            $"clearDistance={WarehouseExitGuardClearDistance:0.00}, " +
            $"timeout={WarehouseExitGuardDuration:0.0}s.");
        warehouseExitGuardCoroutine = StartCoroutine(GuardWarehouseExit(
            vehicle,
            outward,
            startingDistance));
    }

    private IEnumerator GuardWarehouseExit(
        VehicleController vehicle,
        Vector3 outward,
        float startingDistance)
    {
        var expiresAt = Time.unscaledTime + WarehouseExitGuardDuration;
        var reason = "timeout";
        while (vehicle != null && vehicle.controlledByPlayer &&
               Time.unscaledTime < expiresAt)
        {
            if (Vector3.Dot(vehicle.transform.position, outward) >=
                startingDistance + WarehouseExitGuardClearDistance)
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
        ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
            context,
            $"ChevroletCamaro1967 warehouse-exit: guard ending reason={reason}, " +
            $"projection={(vehicle == null ? float.NaN : Vector3.Dot(vehicle.transform.position, outward)):0.000}, " +
            $"startProjection={startingDistance:0.000}.");
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
            ChevroletCamaro1967Diagnostics.WarehouseExitInfo(
                context,
                $"ChevroletCamaro1967 warehouse-exit: restoring triggerCount={warehouseExitGuardColliders.Count}, " +
                $"reason={reason}.");
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

    private static DriveInEntrance? FindClosestDriveInEntrance(Vector3 vehiclePosition)
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

        return nearest;
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (!isOpen)
            return;

        if (!dealerRegistrationReady && BusinessLayoutSetHelper.loadingLayouts)
        {
            ScheduleInitialization("full-menu");
        }
        else if (!dealerRegistrationReady)
        {
            EnsureDealerStock("full-menu");
        }

        if (privateDriverRegistrationAllowed &&
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
        var dealerReady = dealerRegistrationReady;
        var previousMatchedCount = -1;
        var stablePasses = 0;
        var maximumMatchedCount = 0;

        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            if (!privateDriverPoolReady && playerVehiclePrefab != null)
                privateDriverPoolReady = TryPreparePrivateDriverPool(source);

            while (!dealerReady && BusinessLayoutSetHelper.loadingLayouts)
            {
                ConfigureExistingVehicles(out var waitingMatchedCount);
                maximumMatchedCount = Math.Max(maximumMatchedCount, waitingMatchedCount);
                yield return new WaitForSecondsRealtime(InitializationRetryDelay);
            }

            if (!dealerReady)
                dealerReady = EnsureDealerStock(source);
            if (privateDriverRegistrationAllowed && !privateDriverReady)
                EnsurePrivateDriverSupport(source);
            ConfigureExistingVehicles(out var matchedCount);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);

            var servicesReady = dealerReady &&
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
                $"ChevroletCamaro1967: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
        if (privateDriverRegistrationAllowed && !privateDriverReady)
        {
            context?.Logger.Warn(
                $"ChevroletCamaro1967: private-driver support not ready source='{source}'.");
        }
        if (!privateDriverPoolReady)
        {
            context?.Logger.Warn(
                $"ChevroletCamaro1967: private-driver traffic pool not ready source='{source}'.");
        }
    }

    private bool EnsureDealerStock(string source)
    {
        if (dealerRegistrationReady)
            return true;
        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        try
        {
            var ready = ChevroletCamaro1967CityCarsDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerRegistrationReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                ChevroletCamaro1967Diagnostics.Info(
                    context,
                    $"ChevroletCamaro1967: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"ChevroletCamaro1967: dealer stock update failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
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
                privateDriverReady = ChevroletCamaro1967PrivateDriverSupport.EnsureVehicleAvailable(
                    vehicleTypeName);
            }
            if (privateDriverReady)
            {
                ChevroletCamaro1967Diagnostics.Info(
                    context,
                    $"ChevroletCamaro1967: private-driver support registered source='{source}'.");
            }
            return privateDriverReady && privateDriverPoolReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"ChevroletCamaro1967: private-driver registration failed source='{source}': " +
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
            return ChevroletCamaro1967PrivateDriverSupport.PrepareTrafficPool(
                playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                context?.Logger.Warn(
                    $"ChevroletCamaro1967: private-driver pool preparation failed source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
                privateDriverPreparationExceptionLogged = true;
            }

            return false;
        }
    }

    private void RestoreSavedPaintForExistingVehicles(string source)
    {
        var restoredControllers = new HashSet<int>();

        // The normal player-vehicle registry is the cheapest source and remains
        // the first choice. During save reload it can temporarily omit parked
        // vehicles, though, so it must not be the only discovery path.
        var playerVehicles = VehicleHelper.AllPlayerVehicles;
        if (playerVehicles != null)
        {
            foreach (var vehicle in playerVehicles)
                RestoreSavedPaintForVehicle(vehicle, source, restoredControllers);
        }

        var savedVehicleIds = GetSavedTargetVehicleIds();
        if (savedVehicleIds.Count == 0)
            return;

        // Parked vehicles can already exist in the loaded scene before
        // VehicleHelper.AllPlayerVehicles has registered them. Scan loaded scene
        // controllers only at lifecycle restore points, then require an exact
        // saved VehicleInstance.id match so traffic/dealer/private-driver copies
        // can never inherit a player's persisted color.
        foreach (var vehicle in FindObjectsOfType<VehicleController>(true))
        {
            var vehicleInstance = vehicle?.vehicleInstance;
            var vehicleId = vehicleInstance?.id;
            if (vehicleId == null ||
                vehicleId.Length == 0 ||
                !savedVehicleIds.Contains(vehicleId))
            {
                continue;
            }

            RestoreSavedPaintForVehicle(vehicle, source, restoredControllers);
        }
    }

    private void RestoreSavedPaintForVehicle(
        VehicleController? vehicle,
        string source,
        HashSet<int> restoredControllers)
    {
        if (vehicle == null ||
            vehicle.vehicleInstance == null ||
            !IsTargetVehicle(vehicle) ||
            !restoredControllers.Add(vehicle.GetInstanceID()))
        {
            return;
        }

        TryConfigureVehicle(vehicle);
        vehicle.GetComponent<ChevroletCamaro1967PaintController>()
            ?.RestoreAfterSaveLoad(source);
    }

    private HashSet<string> GetSavedTargetVehicleIds()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var savedVehicles = SaveGameManager.Current?.VehicleInstances;
        if (savedVehicles == null)
            return result;

        foreach (var savedVehicle in savedVehicles)
        {
            if (savedVehicle == null ||
                !string.Equals(
                    savedVehicle.vehicleTypeName,
                    vehicleTypeName,
                    StringComparison.Ordinal) ||
                string.IsNullOrEmpty(savedVehicle.id))
            {
                continue;
            }

            result.Add(savedVehicle.id);
        }

        return result;
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
                currentConfig != null &&
                IsCarSleepConfig(currentConfig))
            {
                return true;
            }

            var carConfig = ResolveNativeCarSleepConfig();
            if (carConfig == null)
            {
                if (!nativeCarSleepConfigUnavailableLogged)
                {
                    nativeCarSleepConfigUnavailableLogged = true;
                    context?.Logger.Warn(
                        "ChevroletCamaro1967: native car sleep configuration was unavailable; " +
                        "sleeping in this vehicle remains disabled.");
                }
                return false;
            }

            configField.SetValue(environment, carConfig);
            environmentField.SetValue(vehicle, environment);
            ChevroletCamaro1967Diagnostics.Info(
                context,
                $"ChevroletCamaro1967: configured native car sleep environment " +
                $"vehicle={vehicle.GetInstanceID()} donor=HonzaMimic.");
            return true;
        }
        catch (Exception exception)
        {
            if (!nativeCarSleepConfigUnavailableLogged)
            {
                nativeCarSleepConfigUnavailableLogged = true;
                context?.Logger.Warn(
                    "ChevroletCamaro1967: could not configure native car sleep environment: " +
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
        cachedPlayerVehicleCount = vehicles?.Count ?? 0;
        if (vehicles == null)
            return;

        foreach (var vehicle in vehicles)
        {
            if (vehicle?.vehicleInstance == null ||
                !string.Equals(
                    vehicle.vehicleInstance.vehicleTypeName,
                    vehicleTypeName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            matchedCount++;
            TryConfigureVehicle(vehicle);
        }
    }

    private void TryConfigureVehicle(VehicleController? vehicle)
    {
        if (vehicle == null || !IsTargetVehicle(vehicle))
            return;

        // SleepEnvironment can be absent even when all ordinary Camaro runtime
        // setup is already complete. Refresh it before the configured-ID guard,
        // and also support unsaved developer-tool vehicle spawns.
        ConfigureSleepEnvironment(vehicle);
        if (vehicle.vehicleInstance == null)
            return;

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
                rigidbody.drag = VehicleLinearDrag;
                rigidbody.angularDrag = 1.45f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.solverIterations = Mathf.Max(rigidbody.solverIterations, 12);
                rigidbody.solverVelocityIterations =
                    Mathf.Max(rigidbody.solverVelocityIterations, 4);

                var highwaySeamGuard =
                    vehicle.GetComponent<ChevroletCamaro1967HighwaySeamGuard>();
                if (highwaySeamGuard == null)
                {
                    highwaySeamGuard = vehicle.gameObject
                        .AddComponent<ChevroletCamaro1967HighwaySeamGuard>();
                }
                highwaySeamGuard.Initialize(rigidbody);
            }

            ConfigureMassProperties(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            var contactMaterialOwner =
                vehicle.GetComponent<ChevroletCamaro1967ContactMaterialOwner>();
            if (contactMaterialOwner == null)
            {
                contactMaterialOwner = vehicle.gameObject
                    .AddComponent<ChevroletCamaro1967ContactMaterialOwner>();
            }
            ConfigureBodyColliders(
                vehicle.gameObject,
                contactMaterialOwner.GetOrCreateMaterial());
            ConfigureExitMarkers(vehicle.gameObject);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(vehicle.gameObject);
            var warehouseBounds =
                vehicle.GetComponent<ChevroletCamaro1967WarehouseBoundsController>();
            if (warehouseBounds == null)
            {
                warehouseBounds = vehicle.gameObject
                    .AddComponent<ChevroletCamaro1967WarehouseBoundsController>();
            }
            warehouseBounds.Initialize();
            var warehouseEntry = vehicle.GetComponent<ChevroletCamaro1967WarehouseEntryController>();
            if (warehouseEntry == null)
            {
                warehouseEntry = vehicle.gameObject
                    .AddComponent<ChevroletCamaro1967WarehouseEntryController>();
            }
            warehouseEntry.Initialize(vehicle, context);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var caliperController = vehicle.GetComponent<ChevroletCamaro1967CaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<ChevroletCamaro1967CaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialController = vehicle.GetComponent<ChevroletCamaro1967MaterialController>();
            if (materialController == null)
                materialController = vehicle.gameObject.AddComponent<ChevroletCamaro1967MaterialController>();
            var materialResult = materialController.Initialize(context);
            var glassController = vehicle.GetComponent<ChevroletCamaro1967GlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<ChevroletCamaro1967GlassController>();
            glassController.Initialize(context);
            var paintController = vehicle.GetComponent<ChevroletCamaro1967PaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<ChevroletCamaro1967PaintController>();
            paintController.Initialize(vehicle, context);
            var lightingController = vehicle.GetComponent<ChevroletCamaro1967LightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<ChevroletCamaro1967LightingController>();
            lightingController.Initialize(vehicle, context);
            var smokeController = vehicle.GetComponent<ChevroletCamaro1967DamageSmokeController>();
            if (smokeController == null)
                smokeController = vehicle.gameObject.AddComponent<ChevroletCamaro1967DamageSmokeController>();
            smokeController.Initialize(context);
            // Lighting overlays are spawned per vehicle. Build the deformation
            // allowlist only after they exist so illuminated lamp surfaces move
            // with the surrounding lamp housings after an impact.
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var driverController = vehicle.GetComponent<ChevroletCamaro1967DriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<ChevroletCamaro1967DriverController>();
            driverController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<ChevroletCamaro1967AudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<ChevroletCamaro1967AudioController>();
            audioController.Initialize(vehicle, context);
            var shiftGuard = vehicle.GetComponent<ChevroletCamaro1967FirstGearShiftGuard>();
            if (shiftGuard == null)
                shiftGuard = vehicle.gameObject.AddComponent<ChevroletCamaro1967FirstGearShiftGuard>();
            shiftGuard.Initialize(vehicle);
            ChevroletCamaro1967Diagnostics.Info(
                context,
                $"ChevroletCamaro1967: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=M22-4-speed/4.11, rwd=true, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstaclesNormalized={normalizedNavMeshObstacles}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=small-block-v8-profile, ratedPower={RatedEnginePowerKw:0}kW-gross, " +
                $"physicsPower={PhysicsEnginePowerKw:0}kW, engineLoss={EngineLossPercent:0.00}, " +
                $"steeringCalipers=4, " +
                $"materialRenderers={materialResult.RendererCount}, " +
                $"decalMasksCleared={materialResult.DecalMasksCleared}, " +
                $"opaqueFixed={materialResult.OpaqueMaterialsFixed}, " +
                $"transparentFixed={materialResult.TransparentMaterialsFixed}, " +
                $"cabinGlass={materialResult.CabinGlassRenderers}/" +
                $"reenabled={materialResult.CabinGlassRenderersReenabled}, " +
                $"rimSlotsNormalized={materialResult.RimSlotsNormalized}, " +
                $"hdrpValidated={materialResult.MaterialsValidated}.");
            // A dealer purchase may create an already-entered vehicle without
            // raising onEnterVehicle. Configure that one entry once; regular
            // vehicle-variable events must not repeatedly touch the drivetrain.
            if (vehicle.controlledByPlayer &&
                ReferenceEquals(InstanceBehavior<GameManager>.Instance?.selectedVehicle, vehicle))
            {
                ScheduleEnteredVehicleActivation(vehicle);
            }
        }
        catch (Exception exception)
        {
            configuredVehicleIds.Remove(instanceId);
            context?.Logger.Warn(
                $"ChevroletCamaro1967: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
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

            var wheelPosition = transform.localPosition;
            wheelPosition.y = (isFront ? FrontTireRadius : RearTireRadius) +
                              WheelCenterRideHeightOffset;
            transform.localPosition = wheelPosition;

            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                var spring = GetMember(component, "spring");
                SetFloat(
                    spring,
                    "maxLength",
                    isFront ? FrontSuspensionTravel : RearSuspensionTravel);
                SetFloat(spring, "maxForce", 16500f);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", isFront ? FrontTireRadius : RearTireRadius);
                SetFloat(wheel, "width", isFront ? FrontTireWidth : RearTireWidth);
                var forwardFriction = GetMember(component, "forwardFriction");
                if (forwardFriction != null)
                {
                    SetFloat(
                        forwardFriction,
                        "grip",
                        isFront ? FrontForwardGrip : RearForwardGrip);
                    SetFloat(
                        forwardFriction,
                        "stiffness",
                        isFront ? FrontForwardStiffness : RearForwardStiffness);
                    SetValue(
                        component,
                        "forwardFriction",
                        forwardFriction.GetType(),
                        forwardFriction);
                }
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

    private static void ConfigureBodyColliders(GameObject root, PhysicMaterial contactMaterial)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BodyCollider", StringComparison.Ordinal))
                continue;

            var colliders = transform.GetComponents<BoxCollider>();
            if (colliders.Length > 0)
            {
                // This is the game-facing vehicle collider used by the
                // warehouse drive-in trigger. Keep it low enough for stable
                // road contact but tall enough to cross that trigger before
                // the closed garage-door collider.
                colliders[0].center = new Vector3(0f, 0.40f, 0f);
                colliders[0].size = new Vector3(1.82f, 0.52f, 4.40f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 0.78f, -0.08f);
                colliders[1].size = new Vector3(1.62f, 0.72f, 2.70f);
            }
            var frontContactCollider = colliders.Length > 2
                ? colliders[2]
                : transform.gameObject.AddComponent<BoxCollider>();
            frontContactCollider.center = FrontContactColliderCenter;
            frontContactCollider.size = FrontContactColliderSize;
            frontContactCollider.isTrigger = false;
            frontContactCollider.enabled = true;
            var rearContactCollider = colliders.Length > 3
                ? colliders[3]
                : transform.gameObject.AddComponent<BoxCollider>();
            rearContactCollider.center = RearContactColliderCenter;
            rearContactCollider.size = RearContactColliderSize;
            rearContactCollider.isTrigger = false;
            rearContactCollider.enabled = true;

            foreach (var collider in transform.GetComponents<BoxCollider>())
                collider.sharedMaterial = contactMaterial;
        }
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.name, "Driverside", StringComparison.Ordinal))
                transform.localPosition = DriverExitPosition;
            else if (string.Equals(transform.name, "Passengerside", StringComparison.Ordinal))
                transform.localPosition = PassengerExitPosition;
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
            if (Mathf.Abs(scale.x) < .0001f ||
                Mathf.Abs(scale.y) < .0001f ||
                Mathf.Abs(scale.z) < .0001f)
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

                var halfSize = collider.size * .5f;
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
                $"ChevroletCamaro1967 damage vehicle={vehicle.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        var filters = new List<MeshFilter>();
        foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null || !IsDeformableExterior(filter))
                continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null)
                filters.Add(filter);
        }

        if (filters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"ChevroletCamaro1967 damage vehicle={vehicle.GetInstanceID()}: " +
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
        // Imported panels do not share one root-local coordinate system, so the
        // model-aware controller deforms the selected outer shell in world space.
        damageHandler.meshDeform = false;

        var visualDamage = vehicle.GetComponent<ChevroletCamaro1967VisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<ChevroletCamaro1967VisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        ChevroletCamaro1967Diagnostics.DamageInfo(
            context,
            $"ChevroletCamaro1967 damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        if (filter == null || filter.sharedMesh == null)
            return false;

        var transform = filter.transform;
        var generatedLamp =
            filter.name.StartsWith("ChevroletCamaro1967_", StringComparison.Ordinal);
        var camaroGeometry =
            HasAncestor(transform, "CamaroVisual") ||
            filter.name.StartsWith("CamaroDamageBody", StringComparison.Ordinal) ||
            HasAncestor(transform, "CamaroDamageBody");

        if (!generatedLamp && !camaroGeometry)
            return false;

        // Keep mechanical / cabin pieces rigid. Exterior lamps, bumpers, badges
        // and trim deliberately stay in the damage set so they follow dents.
        if (HasAncestor(transform, "CamaroWheel") ||
            HasAncestor(transform, "CamaroFixedCaliper") ||
            HasAncestor(transform, "LOD_A_TYRE") ||
            HasAncestor(transform, "LOD_A_WHEEL") ||
            HasAncestor(transform, "LOD_A_ROTOR") ||
            HasAncestor(transform, "BRAKE_CALIPER") ||
            HasAncestor(transform, "INT_INTERIOR") ||
            HasAncestor(transform, "INT_STEERING") ||
            HasAncestor(transform, "LOD_A_GLASS_") ||
            HasAncestor(transform, "in_glasss"))
        {
            return false;
        }

        if (HasAncestor(transform, "LOD_A_ENGINE") ||
            HasAncestor(transform, "ENGINE") ||
            HasMaterial(filter, "tire", "wheel", "interior", "seat", "steering", "engine"))
        {
            return false;
        }

        if (generatedLamp)
            return true;

        // Collision-distance gating below prevents front/rear cross-deformation,
        // so all remaining exterior CamaroVisual pieces can follow the panel hit.
        return filter.name.StartsWith("CamaroDamageBody", StringComparison.Ordinal) ||
               HasAncestor(transform, "CamaroVisual");
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static bool HasMaterial(MeshFilter filter, params string[] markers)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;
        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;
            foreach (var marker in markers)
                if (material.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
        }
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
            var clutch = GetMember(powertrain, "clutch");
            SetFloat(clutch, "engagementRPM", ClutchEngagementRpm);
            SetFloat(clutch, "throttleEngagementOffsetRPM", ClutchThrottleOffsetRpm);
            SetFloat(clutch, "engagementRange", ClutchEngagementRange);
            SetFloat(clutch, "creepTorque", ClutchCreepTorque);
            SetFloat(clutch, "creepSpeedLimit", 1f);
            var engine = GetMember(powertrain, "engine");
            SetFloat(engine, "inertia", EngineInertia);
            SetFloat(engine, "maxPower", PhysicsEnginePowerKw);
            SetFloat(engine, "engineLossPercent", EngineLossPercent);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateCamaroPowerCurve());
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
            SetFloat(transmission, "shiftDuration", 0.30f);
            // Use the four M22 ratios while Big Ambitions handles shifting automatically.
            SetFloat(transmission, "_downshiftRPM", 2800f);
            SetFloat(transmission, "_upshiftRPM", 4750f);
            SetInt(transmission, "forwardGearCount", 4);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", CamaroM22Gears);
            var brakes = GetMember(component, "brakes");
            SetFloat(brakes, "maxTorque", BrakeMaxTorque);

            if (GetMember(powertrain, "wheelGroups") is IList wheelGroups)
            {
                foreach (var wheelGroup in wheelGroups)
                    SetFloat(wheelGroup, "antiRollBarForce", AntiRollBarForce);
            }

            var rearDriveConfigured = false;
            if (GetMember(powertrain, "differentials") is IList differentials)
            {
                foreach (var differential in differentials)
                {
                    if (!string.Equals(
                            GetMember(differential, "name") as string,
                            "Center Differential",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }
                    // The reference center differential's B output is the rear differential.
                    SetFloat(differential, "biasAB", 1f);
                    rearDriveConfigured = true;
                }
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

            return GetInt(transmission, "forwardGearCount") == 4 && rearDriveConfigured;
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

#if false // Retired: wheel geometry is authored statically in the prefab.
[AddComponentMenu("")]
[DefaultExecutionOrder(900)]
public sealed class ChevroletCamaro1967WheelGeometryController : MonoBehaviour
{
    private const float WheelAssemblyInsetMeters = 0.000f;
    private static readonly string[,] CornerNames =
    {
        { "FrontLeft_WheelController", "CamaroWheelFrontLeft", "CamaroFixedCaliperFrontLeft" },
        { "FrontRight_WheelController", "CamaroWheelFrontRight", "CamaroFixedCaliperFrontRight" },
        { "RearLeft_WheelController", "CamaroWheelRearLeft", "CamaroFixedCaliperRearLeft" },
        { "RearRight_WheelController", "CamaroWheelRearRight", "CamaroFixedCaliperRearRight" },
    };
    private readonly List<WheelVisualBinding> bindings = new List<WheelVisualBinding>(4);
    private bool initialized;

    internal int Initialize(ModContext? context, float chassisAndTireDrop)
    {
        if (initialized)
            return CornerNames.GetLength(0);

        initialized = true;
        bindings.Clear();
        var visual = FindTransform("CamaroVisual");
        if (visual != null)
            visual.position -= transform.up * chassisAndTireDrop;
        var damageBody = FindTransform("CamaroDamageBody");
        if (damageBody != null)
            damageBody.position -= transform.up * chassisAndTireDrop;

        var correctedCorners = 0;
        for (var index = 0; index < CornerNames.GetLength(0); index++)
        {
            try
            {
                var controller = FindTransform(CornerNames[index, 0]);
                var wheel = FindTransform(CornerNames[index, 1]);
                var caliper = FindTransform(CornerNames[index, 2]);
                if (controller == null || wheel == null || caliper == null)
                    throw new InvalidOperationException(
                        $"wheel assembly is incomplete for corner={index}");

                RecenterRollingGeometry(wheel, caliper);

                // Wheel.Initialize() reparents the visual under its controller.
                // Configuration can run on either side of that lifecycle point,
                // so never move the visual twice when it already follows the
                // controller hierarchy.
                var wheelFollowsController = wheel.IsChildOf(controller);
                MoveInward(controller);
                if (!wheelFollowsController)
                    MoveInward(wheel);
                MoveInward(caliper);
                MoveDown(controller, chassisAndTireDrop);
                if (!wheelFollowsController)
                    MoveDown(wheel, chassisAndTireDrop);
                MoveDown(caliper, chassisAndTireDrop);
                var wheelController = BindRollingVisual(controller, wheel);
                bindings.Add(new WheelVisualBinding(wheelController, wheel));
                correctedCorners++;
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"ChevroletCamaro1967 wheel geometry vehicle={GetInstanceID()} corner={index} " +
                    $"could not be inset: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (correctedCorners == CornerNames.GetLength(0))
        {
            ChevroletCamaro1967Diagnostics.Info(
                context,
                $"ChevroletCamaro1967 wheel geometry vehicle={GetInstanceID()}: moved " +
                $"{correctedCorners} complete wheel/controller/caliper assemblies inward by " +
                $"{WheelAssemblyInsetMeters:F3}m and lowered the complete chassis/wheel datum by " +
                $"{chassisAndTireDrop:F3}m; rolling assemblies bound to steering controllers.");
        }
        else
        {
            context?.Logger.Warn(
                $"ChevroletCamaro1967 wheel geometry vehicle={GetInstanceID()}: corrected " +
                $"{correctedCorners}/{CornerNames.GetLength(0)} wheel assemblies.");
        }
        return correctedCorners;
    }

    private Transform? FindTransform(string name)
    {
        foreach (var candidate in GetComponentsInChildren<Transform>(true))
            if (string.Equals(candidate.name, name, StringComparison.Ordinal))
                return candidate;
        return null;
    }

    private void MoveInward(Transform target)
    {
        var side = Mathf.Sign(transform.InverseTransformPoint(target.position).x);
        if (Mathf.Approximately(side, 0f))
            throw new InvalidOperationException($"'{target.name}' has no lateral side.");
        var worldOffset = transform.right * (-side * WheelAssemblyInsetMeters);
        target.position += worldOffset;
    }

    private void MoveDown(Transform target, float distance) =>
        target.position -= transform.up * distance;

    private static void RecenterRollingGeometry(Transform wheel, Transform caliper)
    {
        MeshFilter? wheelFilter = null;
        foreach (var filter in wheel.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<Renderer>();
            if (filter.sharedMesh == null || renderer == null)
                continue;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && material.name.IndexOf(
                        "material_11",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    wheelFilter = filter;
                    break;
                }
            }
            if (wheelFilter != null)
                break;
        }

        if (wheelFilter?.sharedMesh == null)
            return;
        var mesh = wheelFilter.sharedMesh;
        var vertices = mesh.vertices;
        if (vertices.Length == 0)
            return;

        var parent = new int[vertices.Length];
        for (var index = 0; index < parent.Length; index++)
            parent[index] = index;
        for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
        {
            var triangles = mesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                Union(parent, triangles[index], triangles[index + 1]);
                Union(parent, triangles[index + 1], triangles[index + 2]);
            }
        }

        var componentMinimum = new Dictionary<int, Vector3>();
        var componentMaximum = new Dictionary<int, Vector3>();
        for (var index = 0; index < vertices.Length; index++)
        {
            var local = wheel.InverseTransformPoint(
                wheelFilter.transform.TransformPoint(vertices[index]));
            var root = Find(parent, index);
            if (!componentMinimum.TryGetValue(root, out var minimum))
            {
                componentMinimum[root] = local;
                componentMaximum[root] = local;
            }
            else
            {
                componentMinimum[root] = Vector3.Min(minimum, local);
                componentMaximum[root] = Vector3.Max(componentMaximum[root], local);
            }
        }

        var bestRadialDiameter = 0f;
        var bestRadialCenter = Vector2.zero;
        foreach (var pair in componentMinimum)
        {
            var maximum = componentMaximum[pair.Key];
            var size = maximum - pair.Value;
            var radialDiameter = Mathf.Min(size.y, size.z);
            if (radialDiameter <= bestRadialDiameter)
                continue;
            bestRadialDiameter = radialDiameter;
            bestRadialCenter = new Vector2(
                (pair.Value.y + maximum.y) * 0.5f,
                (pair.Value.z + maximum.z) * 0.5f);
        }

        if (bestRadialDiameter <= 0.001f)
            return;
        var localCorrection = new Vector3(
            0f,
            -bestRadialCenter.x,
            -bestRadialCenter.y);
        var worldCorrection = wheel.TransformVector(localCorrection);
        for (var index = 0; index < wheel.childCount; index++)
            wheel.GetChild(index).localPosition += localCorrection;
        for (var index = 0; index < caliper.childCount; index++)
            caliper.GetChild(index).position += worldCorrection;
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

    private static NWH.WheelController3D.WheelController BindRollingVisual(
        Transform controller,
        Transform wheel)
    {
        var wheelController =
            controller.GetComponent<NWH.WheelController3D.WheelController>();
        if (wheelController == null)
            throw new InvalidOperationException(
                $"'{controller.name}' has no NWH wheel controller.");
        wheelController.wheel.visual = wheel.gameObject;
        wheelController.wheel.visualTransform = wheel;
        if (wheel.parent != controller)
            wheel.SetParent(controller, true);
        return wheelController;
    }

    private static bool IsFinite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static bool IsFinite(Quaternion value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
        !float.IsNaN(value.w) && !float.IsInfinity(value.w);

    private sealed class WheelVisualBinding
    {
        internal WheelVisualBinding(
            NWH.WheelController3D.WheelController controller,
            Transform visual)
        {
            Controller = controller;
            Visual = visual;
        }

        internal NWH.WheelController3D.WheelController Controller { get; }
        internal Transform Visual { get; }
    }
}

public sealed class ChevroletCamaro1967CollisionSeparationController : MonoBehaviour
{
    private const float MinimumPenetration = 0.025f;
    private const float SeparationPadding = 0.015f;
    private const float MaximumCorrectionPerPass = 0.18f;
    private const int MaximumPasses = 3;

    private readonly List<Collider> bodyColliders = new List<Collider>(2);
    private VehicleController? vehicle;
    private Rigidbody? body;
    private Coroutine? separationCoroutine;

    internal void Initialize(VehicleController controller)
    {
        if (vehicle == controller && bodyColliders.Count > 0)
            return;

        vehicle = controller;
        body = controller.GetComponent<Rigidbody>();
        bodyColliders.Clear();
        foreach (var transform in controller.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BodyCollider", StringComparison.Ordinal))
                continue;
            foreach (var collider in transform.GetComponents<Collider>())
                if (collider != null && collider.enabled && !collider.isTrigger)
                    bodyColliders.Add(collider);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (vehicle == null || body == null || collision?.collider == null)
            return;
        var otherVehicle = collision.collider.GetComponentInParent<VehicleController>();
        if (otherVehicle == null || otherVehicle == vehicle)
            return;

        if (separationCoroutine != null)
            StopCoroutine(separationCoroutine);
        separationCoroutine = StartCoroutine(ResolveVehiclePenetration(otherVehicle));
    }

    private IEnumerator ResolveVehiclePenetration(VehicleController otherVehicle)
    {
        yield return new WaitForFixedUpdate();
        var otherColliders = otherVehicle != null
            ? otherVehicle.GetComponentsInChildren<Collider>(true)
            : Array.Empty<Collider>();
        for (var pass = 0; pass < MaximumPasses; pass++)
        {
            if (body == null || otherVehicle == null)
                break;

            var bestDirection = Vector3.zero;
            var bestDistance = 0f;
            foreach (var ownCollider in bodyColliders)
            {
                if (ownCollider == null || !ownCollider.enabled)
                    continue;
                foreach (var otherCollider in otherColliders)
                {
                    if (otherCollider == null || !otherCollider.enabled ||
                        otherCollider.isTrigger ||
                        otherCollider.transform.IsChildOf(transform))
                    {
                        continue;
                    }

                    if (!Physics.ComputePenetration(
                            ownCollider,
                            ownCollider.transform.position,
                            ownCollider.transform.rotation,
                            otherCollider,
                            otherCollider.transform.position,
                            otherCollider.transform.rotation,
                            out var direction,
                            out var distance) ||
                        distance <= bestDistance)
                    {
                        continue;
                    }

                    bestDirection = direction;
                    bestDistance = distance;
                }
            }

            if (bestDistance <= MinimumPenetration || bestDirection.sqrMagnitude < 0.5f)
                break;

            var correction = Mathf.Min(
                bestDistance + SeparationPadding,
                MaximumCorrectionPerPass);
            body.position += bestDirection.normalized * correction;
            var inwardSpeed = Vector3.Dot(body.velocity, -bestDirection.normalized);
            if (inwardSpeed > 0f)
                body.velocity += bestDirection.normalized * inwardSpeed;
            body.WakeUp();
            yield return new WaitForFixedUpdate();
        }
        separationCoroutine = null;
    }

    private void OnDisable()
    {
        if (separationCoroutine != null)
            StopCoroutine(separationCoroutine);
        separationCoroutine = null;
    }
}
#endif

[AddComponentMenu("")]
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
internal sealed class ChevroletCamaro1967HighwaySeamGuard : MonoBehaviour
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
internal sealed class ChevroletCamaro1967ContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        // Match the proven vehicle body contact: low friction lets the rigid
        // bodies separate naturally after a crash instead of locking together.
        contactMaterial = new PhysicMaterial("1967 Chevrolet Camaro body contact")
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
public sealed class ChevroletCamaro1967GlassController : MonoBehaviour
{
    private readonly List<Renderer> cabinGlass = new List<Renderer>();
    private readonly Dictionary<Material, Material> runtimeMaterials =
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
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var containsCabinGlass = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null ||
                    ChevroletCamaro1967Materials.GetTransparentRole(renderer, source) !=
                    ChevroletCamaro1967TransparentRole.CabinGlass)
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    ChevroletCamaro1967Materials.RestoreCabinGlassMaterial(runtimeMaterial);
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
                    ChevroletCamaro1967Materials.GetTransparentRole(renderer, material) ==
                    ChevroletCamaro1967TransparentRole.CabinGlass)
                {
                    renderer.SetPropertyBlock(null, index);
                    ChevroletCamaro1967Materials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            ChevroletCamaro1967Diagnostics.Info(
                context,
                $"ChevroletCamaro1967 glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Unlit-transparent, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            ChevroletCamaro1967Diagnostics.Info(
                context,
                $"ChevroletCamaro1967 glass vehicle={GetInstanceID()}: repaired after " +
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
    }
}

[AddComponentMenu("")]
public sealed class ChevroletCamaro1967VisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.70f;
    private const float MaximumDentDepth = 0.38f;
    private const float DepthPerExcessMps = 0.0135f;
    private const float FrontDentLateralRadius = 0.98f;
    private const float FrontDentVerticalRadius = 0.80f;
    private const float FrontDentLongitudinalRadius = 1.10f;
    private const float MaximumFrontDentDepth = 0.50f;
    private const float FrontDepthPerExcessMps = 0.0180f;
    private const float RearDentLateralRadius = 1.04f;
    private const float RearDentVerticalRadius = 0.84f;
    private const float RearDentLongitudinalRadius = 1.18f;
    private const float MaximumRearDentDepth = 0.58f;
    private const float RearDepthPerExcessMps = 0.0200f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.5f;
    private const int MaximumDiagnosticLogs = 6;

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
    private int diagnosticLogs;
    private bool initialized;
    private bool failureReported;
    private Coroutine? repairRecoveryCoroutine;

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
        damageMeshes.Clear();
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
        if ((previousDamage > 0.001f && currentDamage <= 0.001f) ||
            (previousSavedDamage > 0.001f && currentSavedDamage <= 0.001f))
        {
            foreach (var pair in originalVertices)
            {
                if (pair.Key == null || !damageMeshes.TryGetValue(pair.Key, out var mesh) ||
                    mesh == null)
                    continue;
                // CarController.Repair() invokes the disabled legacy deformation
                // component, which swaps its serialized source mesh back onto the
                // filter. Restore this vehicle-owned mesh before resetting it so
                // later impacts never mutate the shared prefab asset.
                pair.Key.sharedMesh = mesh;
                mesh.vertices = pair.Value;
                mesh.RecalculateBounds();
            }
            if (repairRecoveryCoroutine != null)
                StopCoroutine(repairRecoveryCoroutine);
            repairRecoveryCoroutine = StartCoroutine(RestoreDrivingStateAfterRepair());
            ChevroletCamaro1967Diagnostics.DamageInfo(
                context,
                $"ChevroletCamaro1967 damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
        }
        previousDamage = currentDamage;
        previousSavedDamage = currentSavedDamage;
    }

    private IEnumerator RestoreDrivingStateAfterRepair()
    {
        yield return null;
        for (var pass = 0; pass < 3; pass++)
        {
            yield return new WaitForFixedUpdate();
            if (vehicle == null || !vehicle.controlledByPlayer)
                continue;

            vehicle.SetFreeze(false);
            var physics = vehicle.GetComponent<NWH.VehiclePhysics2.VehicleController>();
            if (physics != null)
            {
                physics.enabled = true;
                if (!physics.powertrain.engine.IsRunning)
                    physics.powertrain.engine.StartEngine();
                if (physics.powertrain.transmission.Gear == 0)
                    physics.powertrain.transmission.ShiftInto(1, true);
            }
            foreach (var wheelController in
                     vehicle.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
                wheelController.enabled = true;
            var rigidbody = vehicle.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.isKinematic = false;
                rigidbody.WakeUp();
            }
        }
        repairRecoveryCoroutine = null;
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
                if (filter == null || filter.sharedMesh == null ||
                    !IsFilterNearCollision(filter, contacts))
                {
                    continue;
                }
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                var meshChanged = false;
                var secondaryExteriorAssembly = IsSecondaryExteriorAssembly(filter);
                var attachedDetail =
                    IsAttachedExteriorDetail(filter, vertices.Length) &&
                    IsFilterCenterNearCollision(filter, contacts);
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
                        ? Mathf.Pow(
                            strongestInfluence,
                            secondaryExteriorAssembly ? 1.05f : 1.35f)
                        : Mathf.Pow(
                            strongestInfluence,
                            secondaryExteriorAssembly ? 1.55f : 2.00f);
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

                if (!meshChanged &&
                    secondaryExteriorAssembly &&
                    TryGetSecondaryAssemblyDisplacement(
                        filter,
                        contacts,
                        dentDepth,
                        frontDentDepth,
                        rearDentDepth,
                        out var assemblyDisplacement,
                        out var assemblyFront,
                        out var assemblyRear))
                {
                    for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        var worldVertex = filter.transform.TransformPoint(vertices[vertexIndex]);
                        vertices[vertexIndex] = filter.transform.InverseTransformPoint(
                            worldVertex + assemblyDisplacement);
                    }

                    changedVertices += vertices.Length;
                    changedVerticesInMesh = vertices.Length;
                    meshChanged = true;
                    frontImpact |= assemblyFront;
                    rearImpact |= assemblyRear;
                }

                if (!meshChanged)
                    continue;
                if (attachedDetail && appliedWorldDisplacements != null &&
                    changedVerticesInMesh > 0)
                {
                    // Lamps, badges, vents, fasteners, and similar separate
                    // pieces must remain attached to the panel. Translate the
                    // whole small mesh by the sampled regional deformation
                    // instead of leaving unaffected vertices hovering behind.
                    var averageDisplacement =
                        totalWorldDisplacement / changedVerticesInMesh;
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
                changedMeshes++;
            }

            if (diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                ChevroletCamaro1967Diagnostics.DamageInfo(
                    context,
                    $"ChevroletCamaro1967 damage vehicle={vehicle?.GetInstanceID()}: inward dent " +
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
                $"ChevroletCamaro1967 damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {exception.GetType().Name}: {exception.Message}");
        }
    }

    private bool IsFilterNearCollision(
        MeshFilter filter,
        ContactPoint[] contacts)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;

        // Guard against the previous cross-end damage: a front impact must never
        // translate a rear lamp/trim mesh and vice versa. Renderer bounds are in
        // world space, so this also works for generated lamp overlays.
        foreach (var contact in contacts)
        {
            var localContact = transform.InverseTransformPoint(contact.point);
            var closest = renderer.bounds.ClosestPoint(contact.point);
            var maxDistance = Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset
                ? 1.50f
                : 1.00f;
            if (Vector3.Distance(closest, contact.point) <= maxDistance)
                return true;
        }

        return false;
    }

    private bool IsFilterCenterNearCollision(
        MeshFilter filter,
        ContactPoint[] contacts)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;
        foreach (var contact in contacts)
            if (Vector3.Distance(renderer.bounds.center, contact.point) <= 1.50f)
                return true;
        return false;
    }

    private static bool DamageHasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool IsPrimaryPaintedPanel(MeshFilter filter)
    {
        var transform = filter.transform;
        return filter.name.StartsWith("CamaroDamageBody", StringComparison.Ordinal) ||
               DamageHasAncestor(transform, "CamaroDamageBody") ||
               DamageHasAncestor(transform, "LOD_A_BODY_mm_ext") ||
               DamageHasAncestor(transform, "LOD_A_HOOD_mm_ext") ||
               DamageHasAncestor(transform, "LOD_A_BOOT_mm_ext") ||
               DamageHasAncestor(transform, "LOD_A_DOOR_LEFT_mm_ext") ||
               DamageHasAncestor(transform, "LOD_A_DOOR_RIGHT_mm_ext");
    }

    private static bool IsSecondaryExteriorAssembly(MeshFilter filter)
    {
        if (filter == null || filter.sharedMesh == null)
            return false;
        return !IsPrimaryPaintedPanel(filter);
    }

    private bool TryGetSecondaryAssemblyDisplacement(
        MeshFilter filter,
        ContactPoint[] contacts,
        float sideDepth,
        float frontDepth,
        float rearDepth,
        out Vector3 displacement,
        out bool frontImpact,
        out bool rearImpact)
    {
        displacement = Vector3.zero;
        frontImpact = false;
        rearImpact = false;

        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;

        var localRendererCenter = transform.InverseTransformPoint(renderer.bounds.center);
        var strongest = 0f;
        var strongestDirection = Vector3.zero;
        var strongestDepth = 0f;
        var strongestFront = false;
        var strongestRear = false;
        var vehicleCenter = body != null ? body.worldCenterOfMass : transform.position;

        foreach (var contact in contacts)
        {
            var localContact = transform.InverseTransformPoint(contact.point);
            var endContact =
                Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
            var isFront = endContact && localContact.z >= 0f;
            var isRear = endContact && localContact.z < 0f;

            // Never drag rear hardware forward during a front collision, or vice versa.
            if (isFront && localRendererCenter.z < -0.10f)
                continue;
            if (isRear && localRendererCenter.z > 0.10f)
                continue;

            var closest = renderer.bounds.ClosestPoint(contact.point);
            var distance = Vector3.Distance(closest, contact.point);
            var maxDistance = endContact ? 1.15f : 0.70f;
            var influence = 1f - distance / maxDistance;
            if (influence <= strongest)
                continue;

            Vector3 direction;
            float depth;
            if (endContact)
            {
                direction = isFront ? -transform.forward : transform.forward;
                depth = isFront ? frontDepth : rearDepth;
            }
            else
            {
                var towardCenter = (vehicleCenter - contact.point).normalized;
                var contactNormal = contact.normal.normalized;
                direction = Vector3.Dot(contactNormal, towardCenter) >= 0f
                    ? contactNormal
                    : -contactNormal;
                depth = sideDepth;
            }

            strongest = influence;
            strongestDirection = direction;
            strongestDepth = depth;
            strongestFront = isFront;
            strongestRear = isRear;
        }

        if (strongest <= 0f || strongestDirection.sqrMagnitude < 0.5f)
            return false;

        // Assemblies behind the painted shell should follow the dent but remain
        // mechanically recognizable instead of collapsing vertex-by-vertex.
        var amount = strongestDepth * Mathf.Pow(strongest, 1.15f) * 1.00f;
        displacement = strongestDirection.normalized * amount;
        frontImpact = strongestFront;
        rearImpact = strongestRear;
        return displacement.sqrMagnitude > 0.0000001f;
    }

    private static bool IsAttachedExteriorDetail(MeshFilter filter, int vertexCount)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;

        var size = renderer.bounds.size;
        var longestSide = Mathf.Max(size.x, Mathf.Max(size.y, size.z));

        // Lamps, badges, fasteners and compact housings should follow their panel
        // as one piece. Wide chrome strips / bumpers / grilles must bend vertexwise.
        if (longestSide <= 0.45f)
            return true;

        return vertexCount <= 120 && longestSide <= 0.75f;
    }

    private void OnDestroy()
    {
        if (repairRecoveryCoroutine != null)
            StopCoroutine(repairRecoveryCoroutine);
        repairRecoveryCoroutine = null;
        foreach (var mesh in runtimeMeshes)
            if (mesh != null) Destroy(mesh);
        runtimeMeshes.Clear();
        damageMeshes.Clear();
    }
}



[AddComponentMenu("")]
public sealed class ChevroletCamaro1967DamageSmokeController : MonoBehaviour
{
    private Material? runtimeMaterial;
    private Texture2D? runtimeTexture;
    private bool initialized;

    internal void Initialize(ModContext? context)
    {
        if (initialized)
            return;
        initialized = true;

        var configured = 0;
        foreach (var particleSystem in GetComponentsInChildren<ParticleSystem>(true))
        {
            if (particleSystem == null ||
                particleSystem.name.IndexOf("SmokeBrokenCar", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
                continue;

            EnsureMaterial();
            if (runtimeMaterial == null)
                continue;

            renderer.sharedMaterial = runtimeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            configured++;
        }

        ChevroletCamaro1967Diagnostics.Info(
            context,
            $"ChevroletCamaro1967 damage smoke: configuredRenderers={configured}, " +
            "material=runtime-soft-HDRP-transparent.");
    }

    private void EnsureMaterial()
    {
        if (runtimeMaterial != null)
            return;

        runtimeTexture = CreateSoftSmokeTexture();
        var shader = Shader.Find("HDRP/Unlit") ??
                     Shader.Find("High Definition Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Transparent") ??
                     Shader.Find("Unlit/Color");
        if (shader == null)
            return;

        runtimeMaterial = new Material(shader)
        {
            name = "ChevroletCamaro1967_RuntimeDamageSmoke",
            hideFlags = HideFlags.DontSave,
        };

        var tint = new Color(0.72f, 0.74f, 0.76f, 0.52f);
        SetColor("_UnlitColor", tint);
        SetColor("_BaseColor", tint);
        SetColor("_Color", tint);
        SetTexture("_UnlitColorMap", runtimeTexture);
        SetTexture("_BaseColorMap", runtimeTexture);
        SetTexture("_MainTex", runtimeTexture);
        SetFloat("_SurfaceType", 1f);
        SetFloat("_BlendMode", 0f);
        SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        SetFloat("_AlphaSrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        SetFloat("_AlphaDstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        SetFloat("_ZWrite", 0f);
        SetFloat("_TransparentZWrite", 0f);
        SetFloat("_AlphaCutoffEnable", 0f);
        SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        SetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Off);
        SetFloat("_DoubleSidedEnable", 1f);
        runtimeMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        runtimeMaterial.EnableKeyword("_DOUBLESIDED_ON");
        runtimeMaterial.DisableKeyword("_ALPHATEST_ON");
        runtimeMaterial.SetOverrideTag("RenderType", "Transparent");
        runtimeMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        runtimeMaterial.SetShaderPassEnabled("DepthOnly", false);
        runtimeMaterial.SetShaderPassEnabled("ShadowCaster", false);
    }

    private static Texture2D CreateSoftSmokeTexture()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false)
        {
            name = "ChevroletCamaro1967_RuntimeSmokeSoftDisc",
            hideFlags = HideFlags.DontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var pixels = new Color32[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var nx = ((x + 0.5f) / size) * 2f - 1f;
                var ny = ((y + 0.5f) / size) * 2f - 1f;
                var radius = Mathf.Sqrt(nx * nx + ny * ny);
                var alpha = Mathf.Clamp01(1f - radius);
                alpha = alpha * alpha * (3f - 2f * alpha);
                // A subtle deterministic cloudy breakup prevents a perfect disc.
                var breakup = 0.88f + 0.12f *
                    Mathf.Sin(nx * 11.3f + ny * 7.7f) *
                    Mathf.Sin(nx * 5.1f - ny * 13.9f);
                var a = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * breakup * 255f), 0, 255);
                pixels[y * size + x] = new Color32(210, 214, 218, a);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private void SetColor(string property, Color value)
    {
        if (runtimeMaterial != null && runtimeMaterial.HasProperty(property))
            runtimeMaterial.SetColor(property, value);
    }

    private void SetTexture(string property, Texture value)
    {
        if (runtimeMaterial != null && runtimeMaterial.HasProperty(property))
            runtimeMaterial.SetTexture(property, value);
    }

    private void SetFloat(string property, float value)
    {
        if (runtimeMaterial != null && runtimeMaterial.HasProperty(property))
            runtimeMaterial.SetFloat(property, value);
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
        if (runtimeTexture != null)
            Destroy(runtimeTexture);
        runtimeMaterial = null;
        runtimeTexture = null;
    }
}


[AddComponentMenu("")]
public sealed class ChevroletCamaro1967FirstGearShiftGuard : MonoBehaviour
{
    private const float NormalUpshiftRpm = 4750f;
    private const float NormalDownshiftRpm = 2800f;
    private const float FirstGearNativeUpshiftRpm = 5450f;
    private const float FirstToSecondShiftRpm = 4700f;
    private const float FirstToSecondMinimumSpeedKph = 55f;
    private const float SecondGearHoldDownshiftRpm = 1800f;
    private const float SecondGearHoldSeconds = 0.90f;

    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private FieldInfo? upshiftRpmField;
    private FieldInfo? downshiftRpmField;
    private float secondGearHoldUntil;
    private float nextForcedShiftAllowed;

    internal void Initialize(VehicleController controller)
    {
        vehicle = controller;
        physics = controller.GetComponent<PhysicsVehicle>();
        body = controller.GetComponent<Rigidbody>();
        ResolveShiftFields();
    }

    private void ResolveShiftFields()
    {
        var transmission = physics?.powertrain.transmission;
        if (transmission == null)
            return;
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = transmission.GetType();
        upshiftRpmField = type.GetField("_upshiftRPM", flags);
        downshiftRpmField = type.GetField("_downshiftRPM", flags);
    }

    private void Update()
    {
        if (vehicle == null || physics == null || !vehicle.controlledByPlayer)
            return;

        var transmission = physics.powertrain.transmission;
        var engine = physics.powertrain.engine;
        var currentRpm = engine.RPMPercent * engine.revLimiterRPM;
        var speedKph = body != null ? body.velocity.magnitude * 3.6f : 0f;
        var now = Time.unscaledTime;

        // First gear previously had two independent shift paths: NWH's 4750-rpm
        // automatic upshift and this guard's later forced 5350-rpm upshift.
        // Wheelspin can therefore make NWH start a shift far too early for the
        // actual road speed. Keep NWH out of 1->2 and perform that shift once
        // both engine rpm and real vehicle speed are plausible for the M22/4.11.
        if (transmission.Gear == 1)
        {
            SetShiftRpm(upshiftRpmField, transmission, FirstGearNativeUpshiftRpm);
            SetShiftRpm(downshiftRpmField, transmission, NormalDownshiftRpm);

            if (now >= nextForcedShiftAllowed &&
                currentRpm >= FirstToSecondShiftRpm &&
                speedKph >= FirstToSecondMinimumSpeedKph &&
                engine.ThrottlePosition >= 0.30f)
            {
                transmission.ShiftInto(2, true);
                secondGearHoldUntil = now + SecondGearHoldSeconds;
                nextForcedShiftAllowed = now + 0.25f;
                SetShiftRpm(upshiftRpmField, transmission, NormalUpshiftRpm);
                SetShiftRpm(downshiftRpmField, transmission, SecondGearHoldDownshiftRpm);
            }
            return;
        }

        SetShiftRpm(upshiftRpmField, transmission, NormalUpshiftRpm);

        if (now < secondGearHoldUntil)
        {
            // A large launch-slip rpm drop used to make the automatic immediately
            // request first gear again. Temporarily lower only the 2->1 threshold.
            SetShiftRpm(downshiftRpmField, transmission, SecondGearHoldDownshiftRpm);
            if (transmission.Gear < 2 &&
                speedKph >= 45f &&
                engine.ThrottlePosition >= 0.15f &&
                now >= nextForcedShiftAllowed)
            {
                transmission.ShiftInto(2, true);
                nextForcedShiftAllowed = now + 0.20f;
            }
            return;
        }

        SetShiftRpm(downshiftRpmField, transmission, NormalDownshiftRpm);
    }

    private static void SetShiftRpm(FieldInfo? field, object transmission, float value)
    {
        if (field?.FieldType == typeof(float))
            field.SetValue(transmission, value);
    }

    private void OnDisable()
    {
        var transmission = physics?.powertrain.transmission;
        if (transmission == null)
            return;
        SetShiftRpm(upshiftRpmField, transmission, NormalUpshiftRpm);
        SetShiftRpm(downshiftRpmField, transmission, NormalDownshiftRpm);
    }
}
