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

public sealed class DodgeChallenger2018Runtime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const string VehicleRepainterColorPreviewEvent =
        "vehicle-repainter:color-preview";
    private const string VehicleRepainterColorRestoredEvent =
        "vehicle-repainter:color-restored";
    private const string NativeCarSleepDonorPrefabPath =
        "Vehicles/PlayerVehicles/HonzaMimic";
    private const float VehicleMass = 1941f;
    // Hennessey rates the package at 1,200 crank hp (~895 kW), while the
    // documented Dynojet result is 1,013 rwhp (~755 kW). NWH has no drivetrain
    // loss model here, so feed it the measured wheel power while VehicleType
    // continues to advertise the real 1,200 hp crank rating.
    private const float EnginePowerKw = 755f;
    private const float EngineIdleRpm = 750f;
    private const float EngineLimitRpm = 6500f;
    private const float SpeedLimitKph = 350f;
    private const float FinalDriveRatio = 3.09f;
    private const float EngineInertia = 0.20f;
    private const float EngineStartDuration = 0.38f;
    // The TorqueFlite automatic should engage cleanly just above idle.
    // Keep engagement low so N -> first applies torque without
    // an artificial launch flare.
    private const float ClutchEngagementRpm = 900f;
    private const float ClutchThrottleOffsetRpm = 300f;
    private const float ClutchEngagementRange = 900f;
    private const float ClutchCreepTorque = 0f;
    private const float ClutchSlipTorque = 1650f;
    private const float TireFrictionCircleStrength = 1.00f;
    private const float AntiRollBarForce = 9500f;
    private const float FrontSuspensionTravel = 0.090f;
    private const float RearSuspensionTravel = 0.095f;
    private const float FrontTireRadius = 0.3546f;
    private const float RearTireRadius = 0.3546f;
    private const float FrontTireWidth = 0.315f;
    private const float RearTireWidth = 0.315f;
    private const float VehicleLinearDrag = 0.038f;
    private const float FrontForwardGrip = 0.90f;
    private const float RearForwardGrip = 0.95f;
    private const float FrontForwardStiffness = 1.27f;
    private const float RearForwardStiffness = 0.70f;
    private const float DeformationStrength = 0.12f;
    private const float DeformationRadius = 0.28f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 0.75f;
    private const float DamageDecelerationThreshold = 500f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private const float WarehouseExitGuardDuration = 8f;
    private const float WarehouseExitGuardClearDistance = 4f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.22f, -0.12f);
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.52f, 1.93f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.92f, 0.56f, 1.12f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.53f, -2.02f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.92f, 0.58f, 1.14f);
    private static readonly Vector3 DriverExitPosition =
        new Vector3(-2.10f, 0.20f, 0.15f);
    private static readonly Vector3 PassengerExitPosition =
        new Vector3(2.10f, 0.20f, 0.15f);

    private static readonly float[] DemonGears =
    {
        -3.32f, 0f, 4.71f, 3.14f, 2.10f, 1.67f, 1.29f, 1.00f, 0.84f, 0.67f,
    };

    private static AnimationCurve CreateDemonPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.12f, 0.05f),
            new Keyframe(0.22f, 0.16f),
            new Keyframe(0.32f, 0.30f),
            new Keyframe(0.46f, 0.54f),
            new Keyframe(0.62f, 0.72f),
            new Keyframe(0.78f, 0.90f),
            new Keyframe(0.86f, 1.00f),
            new Keyframe(1f, 0.93f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private int enteredVehicleActivationInstanceId;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private DodgeChallenger2018WarehouseEntryController? warehouseExitGuardEntryController;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private int cachedPlayerVehicleCount = -1;
    private bool dealerRegistrationReady;
    private bool dealerReadyLogged;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;
    private UnityEngine.Object? nativeCarSleepConfig;
    private bool nativeCarSleepConfigUnavailableLogged;
    private GameObject? playerVehiclePrefab;

    public static DodgeChallenger2018Runtime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<DodgeChallenger2018Runtime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(DodgeChallenger2018Runtime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<DodgeChallenger2018Runtime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        DodgeChallenger2018PrivateDriverSupport.SetContext(context);
        var trafficFrequency = runtime.GetComponent<DodgeChallenger2018TrafficFrequency>() ??
                               runtime.gameObject.AddComponent<DodgeChallenger2018TrafficFrequency>();
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
        DodgeChallenger2018PrivateDriverSupport.RemoveVehicle(vehicleTypeName);
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
            // Repainter restores parked custom VehicleColor objects in one pass.
            // Rebind each loaded player-owned Challenger from its own persisted
            // VehicleInstance instead of relying on selection/entry state.
            RestoreSavedPaintForExistingVehicles("vehicle-repainter:color-restored");
            return;
        }

        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        DodgeChallenger2018Diagnostics.DealerEntryInfo(
            context,
            $"DodgeChallenger2018 dealer-entry: game-event='{eventName}', " +
            DescribeVehicleState(selectedVehicle));

        // Dealer purchases do not reliably invoke onEnterVehicle. This event
        // is a bounded lifecycle handoff from the dealer display vehicle to
        // the owned, player-controlled instance, so configure it here rather
        // than scanning vehicles every frame.
        TryConfigureVehicle(selectedVehicle);
        selectedVehicle!
            .GetComponent<DodgeChallenger2018PaintController>()
            ?.ApplyCurrentColor(
                eventName.Length == 0 ? "game-event" : eventName,
                preferLiveColor: string.Equals(
                    eventName,
                    VehicleRepainterColorPreviewEvent,
                    StringComparison.Ordinal));
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
        if (DodgeChallenger2018LoadRecovery.CompleteInterruptedLoad(context))
            StartCoroutine(ReportLoadedInputState());
        privateDriverRegistrationAllowed = true;
        RestoreSavedPaintForExistingVehicles("game-loaded-late");
        ScheduleInitialization("game-loaded-late");
    }

    private IEnumerator ReportLoadedInputState()
    {
        // The native loading screen fades out for 0.8 seconds after this event.
        yield return new WaitForSecondsRealtime(2f);
        DodgeChallenger2018LoadRecovery.ReportInputState(context);
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
        DodgeChallenger2018Diagnostics.DealerEntryInfo(
            context,
            $"DodgeChallenger2018 dealer-entry: onEnterVehicle, {DescribeVehicleState(vehicle)}");
        TryConfigureVehicle(vehicle);
        vehicle.GetComponent<DodgeChallenger2018GlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle == null || !IsTargetVehicle(vehicle))
            return;
        vehicle.GetComponent<DodgeChallenger2018PaintController>()
            ?.ApplyCurrentColor("vehicle-entered");
        ScheduleEnteredVehicleActivation(vehicle);
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;
        DodgeChallenger2018Diagnostics.WarehouseExitInfo(
            context,
            $"DodgeChallenger2018 warehouse-exit: onExitVehicle, {DescribeVehicleState(vehicle)}");
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
        DodgeChallenger2018Diagnostics.WarehouseExitInfo(
            context,
            $"DodgeChallenger2018 player-exit: deferred validation playerPosition={root.position}, " +
            $"agentCount={agents.Length}, anyAgentOffNavMesh={agents.Any(agent => agent != null && agent.enabled && !agent.isOnNavMesh)}, " +
            $"initialClear={initialExitClear}, needsRecovery={needsRecovery}.");
        if (!needsRecovery)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }
        if (!TryFindClearExitPosition(root, exitedVehicle, out var target))
        {
            DodgeChallenger2018Diagnostics.WarehouseExitInfo(
                context,
                "DodgeChallenger2018 player-exit: recovery failed; no NavMesh/capsule-clear candidate found.");
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
        DodgeChallenger2018Diagnostics.WarehouseExitInfo(
            context,
            $"DodgeChallenger2018 player-exit: recovery moved player to {target}.");
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
        vehicle != null &&
        (string.Equals(
             vehicle.vehicleInstance?.vehicleTypeName,
             vehicleTypeName,
             StringComparison.Ordinal) ||
         string.Equals(
             vehicle.vehicleType?.vehicleTypeName,
             vehicleTypeName,
             StringComparison.Ordinal));

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
            DodgeChallenger2018Diagnostics.DealerEntryInfo(
                context,
                $"DodgeChallenger2018 dealer-entry: activation skipped because the vehicle is " +
                $"neither controlled nor selected, {DescribeVehicleState(vehicle)}");
            return;
        }

        var instanceId = vehicle.GetInstanceID();
        if (enteredVehicleActivationCoroutine != null &&
            enteredVehicleActivationInstanceId == instanceId)
        {
            DodgeChallenger2018Diagnostics.DealerEntryInfo(
                context,
                $"DodgeChallenger2018 dealer-entry: activation already pending instance={instanceId}.");
            return;
        }

        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);

        enteredVehicleActivationInstanceId = instanceId;
        DodgeChallenger2018Diagnostics.DealerEntryInfo(
            context,
            $"DodgeChallenger2018 dealer-entry: activation scheduled, {DescribeVehicleState(vehicle)}");
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
            DodgeChallenger2018Diagnostics.DealerEntryInfo(
                context,
                $"DodgeChallenger2018 dealer-entry: activation failed; NWH controller missing, " +
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
                DodgeChallenger2018Diagnostics.DealerEntryInfo(
                    context,
                    $"DodgeChallenger2018 dealer-entry: activation cancelled attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }

            // Paint assignment from the dealer is asynchronous too; retain a
            // bounded entry refresh without turning it into runtime polling.
            vehicle.GetComponent<DodgeChallenger2018PaintController>()
                ?.ApplyCurrentColor("vehicle-entered");
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            DodgeChallenger2018Diagnostics.DealerEntryInfo(
                context,
                $"DodgeChallenger2018 dealer-entry: activation attempt={attempt + 1}, " +
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
                    DodgeChallenger2018Diagnostics.DealerEntryInfo(
                        context,
                        "DodgeChallenger2018 dealer-entry: native engine healthy; shifted neutral to first.");
                    yield return new WaitForFixedUpdate();
                    DodgeChallenger2018Diagnostics.DealerEntryInfo(
                        context,
                        $"DodgeChallenger2018 dealer-entry: first-gear settle, gear={transmission.Gear}, " +
                        $"ratio={transmission.currentGearRatio:0.000}.");
                }
                else
                {
                    DodgeChallenger2018Diagnostics.DealerEntryInfo(
                        context,
                        $"DodgeChallenger2018 dealer-entry: native engine healthy; preserved gear={transmission.Gear}.");
                }
                break;
            }

            DodgeChallenger2018Diagnostics.DealerEntryInfo(
                context,
                $"DodgeChallenger2018 dealer-entry: dormant powertrain; restart attempt={attempt + 1}.");
            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
            {
                DodgeChallenger2018Diagnostics.DealerEntryInfo(
                    context,
                    $"DodgeChallenger2018 dealer-entry: restart aborted after stop attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }
            engine.StartEngine();
            yield return new WaitForSecondsRealtime(.75f);
            if (vehicle != null && vehicle.controlledByPlayer &&
                transmission.Gear == 0)
            {
                transmission.ShiftInto(1, true);
                DodgeChallenger2018Diagnostics.DealerEntryInfo(
                    context,
                    $"DodgeChallenger2018 dealer-entry: restart attempt={attempt + 1} shifted neutral to first.");
            }
            yield return new WaitForSecondsRealtime(.15f);
        }

        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        DodgeChallenger2018Diagnostics.DealerEntryInfo(
            context,
            $"DodgeChallenger2018 dealer-entry: activation completed, {DescribeVehicleState(vehicle)}");
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!DodgeChallenger2018LuxuryDealerStock.IsTargetDealer(registration?.BusinessName) ||
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
            DodgeChallenger2018Diagnostics.WarehouseExitInfo(
                context,
                $"DodgeChallenger2018 warehouse-exit: guard skipped; current vehicle is not " +
                $"a controlled Dodge ({DescribeVehicleState(vehicle)}).");
            return;
        }

        var entrance = FindClosestDriveInEntrance(vehicle.transform.position);
        if (entrance == null)
        {
            DodgeChallenger2018Diagnostics.WarehouseExitInfo(
                context,
                $"DodgeChallenger2018 warehouse-exit: guard skipped; no DriveInEntrance found, " +
                DescribeVehicleState(vehicle));
            return;
        }

        var nativeMeshCollider = vehicle.GetComponentInChildren<MeshCollider>(true);
        DodgeChallenger2018Diagnostics.WarehouseExitInfo(
            context,
            $"DodgeChallenger2018 warehouse-exit: native exit completed, {DescribeVehicleState(vehicle)} " +
            $"entrance='{entrance.name}' entrancePosition={entrance.transform.position}, " +
            $"nativeMesh='{nativeMeshCollider?.name ?? "missing"}', " +
            $"nativeMeshLength={nativeMeshCollider?.sharedMesh?.bounds.size.z:0.000}.");

        StopWarehouseExitGuard();
        warehouseExitGuardEntryController =
            vehicle.GetComponent<DodgeChallenger2018WarehouseEntryController>();
        warehouseExitGuardEntryController?.SuppressEntrance(entrance, "warehouse-exit-guard");
        foreach (var enterTrigger in entrance.GetComponentsInChildren<DriveInEntranceEnterTrigger>(true))
        foreach (var collider in enterTrigger.GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled || !collider.isTrigger)
                continue;

            warehouseExitGuardColliders.Add(collider);
            DodgeChallenger2018Diagnostics.WarehouseExitInfo(
                context,
                $"DodgeChallenger2018 warehouse-exit: disabling entry trigger " +
                $"'{collider.name}' bounds={collider.bounds}.");
            collider.enabled = false;
        }

        if (warehouseExitGuardColliders.Count == 0)
        {
            warehouseExitGuardEntryController?.ClearSuppressedEntrance(
                entrance,
                "no-native-entry-trigger");
            warehouseExitGuardEntryController = null;
            DodgeChallenger2018Diagnostics.WarehouseExitInfo(
                context,
                "DodgeChallenger2018 warehouse-exit: guard skipped; matching entrance had no enabled trigger colliders.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(
            vehicle.transform.position - entrance.transform.position,
            Vector3.up);
        if (outward.sqrMagnitude < .0001f)
            outward = Vector3.ProjectOnPlane(entrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < .0001f)
        {
            DodgeChallenger2018Diagnostics.WarehouseExitInfo(
                context,
                "DodgeChallenger2018 warehouse-exit: guard aborted; outward direction was zero.");
            StopWarehouseExitGuard();
            return;
        }

        outward.Normalize();
        var startingDistance = Vector3.Dot(vehicle.transform.position, outward);
        Physics.SyncTransforms();
        DodgeChallenger2018Diagnostics.WarehouseExitInfo(
            context,
            $"DodgeChallenger2018 warehouse-exit: guard started triggerCount={warehouseExitGuardColliders.Count}, " +
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
        DodgeChallenger2018Diagnostics.WarehouseExitInfo(
            context,
            $"DodgeChallenger2018 warehouse-exit: guard ending reason={reason}, " +
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
            DodgeChallenger2018Diagnostics.WarehouseExitInfo(
                context,
                $"DodgeChallenger2018 warehouse-exit: restoring triggerCount={warehouseExitGuardColliders.Count}, " +
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
                $"DodgeChallenger2018: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
        if (privateDriverRegistrationAllowed && !privateDriverReady)
        {
            context?.Logger.Warn(
                $"DodgeChallenger2018: private-driver support not ready source='{source}'.");
        }
        if (!privateDriverPoolReady)
        {
            context?.Logger.Warn(
                $"DodgeChallenger2018: private-driver traffic pool not ready source='{source}'.");
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
            var ready = DodgeChallenger2018LuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerRegistrationReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                DodgeChallenger2018Diagnostics.Info(
                    context,
                    $"DodgeChallenger2018: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"DodgeChallenger2018: dealer stock update failed source='{source}': " +
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
                privateDriverReady = DodgeChallenger2018PrivateDriverSupport.EnsureVehicleAvailable(
                    vehicleTypeName);
            }
            if (privateDriverReady)
            {
                DodgeChallenger2018Diagnostics.Info(
                    context,
                    $"DodgeChallenger2018: private-driver support registered source='{source}'.");
            }
            return privateDriverReady && privateDriverPoolReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"DodgeChallenger2018: private-driver registration failed source='{source}': " +
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
            return DodgeChallenger2018PrivateDriverSupport.PrepareTrafficPool(
                playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                context?.Logger.Warn(
                    $"DodgeChallenger2018: private-driver pool preparation failed source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
                privateDriverPreparationExceptionLogged = true;
            }

            return false;
        }
    }

    private void RestoreSavedPaintForExistingVehicles(string source)
    {
        var restoredControllers = new HashSet<int>();

        var playerVehicles = VehicleHelper.AllPlayerVehicles;
        if (playerVehicles != null)
        {
            foreach (var vehicle in playerVehicles)
                RestoreSavedPaintForVehicle(vehicle, source, restoredControllers);
        }

        var savedVehicleIds = GetSavedTargetVehicleIds();
        if (savedVehicleIds.Count == 0)
            return;

        // During save reload a parked owned vehicle may already exist in the
        // scene before AllPlayerVehicles has registered it. This scan is limited
        // to lifecycle restore points and requires an exact saved instance id,
        // so traffic/dealer/private-driver copies are excluded.
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
        vehicle.GetComponent<DodgeChallenger2018PaintController>()
            ?.ApplyCurrentColor(source);
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
                currentConfig != null)
                return IsCarSleepConfig(currentConfig);

            var carConfig = ResolveNativeCarSleepConfig();
            if (carConfig == null)
            {
                if (!nativeCarSleepConfigUnavailableLogged)
                {
                    nativeCarSleepConfigUnavailableLogged = true;
                    context?.Logger.Warn(
                        "DodgeChallenger2018: native car sleep configuration was unavailable; " +
                        "sleeping in this vehicle will remain disabled.");
                }
                return false;
            }

            configField.SetValue(environment, carConfig);
            environmentField.SetValue(vehicle, environment);
            DodgeChallenger2018Diagnostics.Info(
                context,
                $"DodgeChallenger2018: configured native car sleep environment " +
                $"vehicle={vehicle.GetInstanceID()} donor=HonzaMimic.");
            return true;
        }
        catch (Exception exception)
        {
            if (!nativeCarSleepConfigUnavailableLogged)
            {
                nativeCarSleepConfigUnavailableLogged = true;
                context?.Logger.Warn(
                    "DodgeChallenger2018: could not configure the native car sleep environment: " +
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
        if (!IsTargetVehicle(vehicle) || vehicle == null)
            return;

        // Match the established Ferrari/Porsche pattern: developer-tool spawns
        // may not have a saved VehicleInstance yet, but they should still expose
        // the native Car sleep action.
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
                rigidbody.angularDrag = 1.80f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.solverIterations = Mathf.Max(rigidbody.solverIterations, 12);
                rigidbody.solverVelocityIterations =
                    Mathf.Max(rigidbody.solverVelocityIterations, 4);

                var highwaySeamGuard =
                    vehicle.GetComponent<DodgeChallenger2018HighwaySeamGuard>();
                if (highwaySeamGuard == null)
                {
                    highwaySeamGuard = vehicle.gameObject
                        .AddComponent<DodgeChallenger2018HighwaySeamGuard>();
                }
                highwaySeamGuard.Initialize(rigidbody);
            }

            ConfigureMassProperties(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            var contactMaterialOwner =
                vehicle.GetComponent<DodgeChallenger2018ContactMaterialOwner>();
            if (contactMaterialOwner == null)
            {
                contactMaterialOwner = vehicle.gameObject
                    .AddComponent<DodgeChallenger2018ContactMaterialOwner>();
            }
            ConfigureBodyColliders(
                vehicle.gameObject,
                contactMaterialOwner.GetOrCreateMaterial());
            ConfigureExitMarkers(vehicle.gameObject);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(vehicle.gameObject);
            var warehouseBounds =
                vehicle.GetComponent<DodgeChallenger2018WarehouseBoundsController>();
            if (warehouseBounds == null)
            {
                warehouseBounds = vehicle.gameObject
                    .AddComponent<DodgeChallenger2018WarehouseBoundsController>();
            }
            warehouseBounds.Initialize();
            var warehouseEntry = vehicle.GetComponent<DodgeChallenger2018WarehouseEntryController>();
            if (warehouseEntry == null)
            {
                warehouseEntry = vehicle.gameObject
                    .AddComponent<DodgeChallenger2018WarehouseEntryController>();
            }
            warehouseEntry.Initialize(vehicle, context);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var drivetrainGuard = vehicle.GetComponent<DodgeChallenger2018DrivetrainGuard>();
            if (drivetrainGuard == null)
                drivetrainGuard = vehicle.gameObject.AddComponent<DodgeChallenger2018DrivetrainGuard>();
            drivetrainGuard.Initialize(vehicle, context);
            var caliperController = vehicle.GetComponent<DodgeChallenger2018CaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<DodgeChallenger2018CaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialController = vehicle.GetComponent<DodgeChallenger2018MaterialController>();
            if (materialController == null)
                materialController = vehicle.gameObject.AddComponent<DodgeChallenger2018MaterialController>();
            var materialResult = materialController.Initialize(context);
            var glassController = vehicle.GetComponent<DodgeChallenger2018GlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<DodgeChallenger2018GlassController>();
            glassController.Initialize(context);
            var paintController = vehicle.GetComponent<DodgeChallenger2018PaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<DodgeChallenger2018PaintController>();
            paintController.Initialize(vehicle, context);
            var lightingController = vehicle.GetComponent<DodgeChallenger2018LightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<DodgeChallenger2018LightingController>();
            lightingController.Initialize(vehicle, context);
            // Lighting overlays are spawned per vehicle. Build the deformation
            // allowlist only after they exist so illuminated lamp surfaces move
            // with the surrounding lamp housings after an impact.
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var driverController = vehicle.GetComponent<DodgeChallenger2018DriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<DodgeChallenger2018DriverController>();
            driverController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<DodgeChallenger2018AudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<DodgeChallenger2018AudioController>();
            audioController.Initialize(vehicle, context);
            DodgeChallenger2018Diagnostics.Info(
                context,
                $"DodgeChallenger2018: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=8-speed-8HP90, rwd=true, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstaclesNormalized={normalizedNavMeshObstacles}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=supercharged-hemi-simulation-profile, steeringCalipers=4, " +
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
                $"DodgeChallenger2018: vehicle configuration failed instance={instanceId}: " +
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

            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                var spring = GetMember(component, "spring");
                SetFloat(
                    spring,
                    "maxLength",
                    isFront ? FrontSuspensionTravel : RearSuspensionTravel);
                SetFloat(spring, "maxForce", 19000f);

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
                $"DodgeChallenger2018 damage vehicle={vehicle.GetInstanceID()}: " +
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
                $"DodgeChallenger2018 damage vehicle={vehicle.GetInstanceID()}: " +
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

        var visualDamage = vehicle.GetComponent<DodgeChallenger2018VisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<DodgeChallenger2018VisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        DodgeChallenger2018Diagnostics.DamageInfo(
            context,
            $"DodgeChallenger2018 damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        if (filter == null || filter.sharedMesh == null)
            return false;
        if (!HasAncestor(filter.transform, "DodgeVisual") &&
            !filter.name.StartsWith("DodgeDamageBody", StringComparison.Ordinal) &&
            !filter.name.StartsWith("DodgeLamp_", StringComparison.Ordinal))
            return false;

        if (HasAncestor(filter.transform, "DodgeWheel") ||
            HasAncestor(filter.transform, "DodgeFixedCaliper") ||
            HasAncestor(filter.transform, ":Window") ||
            HasAncestor(filter.transform, ":Interior") ||
            HasAncestor(filter.transform, ":Engine"))
            return false;

        // Crash-following detail geometry. These meshes visibly sit on the
        // bumper/body and must not remain floating in their original position.
        if (filter.name.StartsWith("DodgeLamp_", StringComparison.Ordinal) ||
            filter.name.StartsWith("DodgeCrashDetail_", StringComparison.Ordinal) ||
            HasAncestor(filter.transform, ":Light") ||
            HasAncestor(filter.transform, ":Glass") ||
            HasAncestor(filter.transform, ":Badge") ||
            HasAncestor(filter.transform, ":Textured") ||
            HasAncestor(filter.transform, ":Grille"))
            return true;

        if (HasMaterial(filter, "interior", "engine", "Wheel", "calip"))
            return false;

        return filter.name.StartsWith("DodgeDamageBody", StringComparison.Ordinal) ||
               HasAncestor(filter.transform, ":Paint") ||
               HasAncestor(filter.transform, ":Coloured") ||
               HasAncestor(filter.transform, ":Base") ||
               HasMaterial(filter, "paint1Mtl1", "ColouredMtl1", "Base",
                   "BadgeBMtl1", "TexturedMtl1", "LightMtl1", "GlassRedMtl1");
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
            SetFloat(clutch, "slipTorque", ClutchSlipTorque);
            var engine = GetMember(powertrain, "engine");
            SetFloat(engine, "inertia", EngineInertia);
            SetBool(engine, "autoStartOnThrottle", true);
            SetFloat(engine, "maxPower", EnginePowerKw);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateDemonPowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", true);
            SetFloat(forcedInduction, "powerGainMultiplier", 1f);
            SetFloat(forcedInduction, "spoolUpTime", 0.03f);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.065f);
            SetFloat(transmission, "postShiftBan", 0.35f);
            // TorqueFlite 8HP90 calibration for the supercharged HEMI.
            SetFloat(transmission, "_downshiftRPM", 3300f);
            SetFloat(transmission, "_upshiftRPM", 5200f);
            SetBool(transmission, "variableShiftPoint", false);
            SetInt(transmission, "forwardGearCount", 8);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", DemonGears);

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

            return GetInt(transmission, "forwardGearCount") == 8 && rearDriveConfigured;
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
public sealed class DodgeChallenger2018WheelGeometryController : MonoBehaviour
{
    private const float WheelAssemblyInsetMeters = 0f;
    private static readonly string[,] CornerNames =
    {
        { "FrontLeft_WheelController", "DodgeWheelFrontLeft", "DodgeFixedCaliperFrontLeft" },
        { "FrontRight_WheelController", "DodgeWheelFrontRight", "DodgeFixedCaliperFrontRight" },
        { "RearLeft_WheelController", "DodgeWheelRearLeft", "DodgeFixedCaliperRearLeft" },
        { "RearRight_WheelController", "DodgeWheelRearRight", "DodgeFixedCaliperRearRight" },
    };
    private readonly List<WheelVisualBinding> bindings = new List<WheelVisualBinding>(4);
    private bool initialized;

    internal int Initialize(ModContext? context, float chassisAndTireDrop)
    {
        if (initialized)
            return CornerNames.GetLength(0);

        initialized = true;
        bindings.Clear();
        var visual = FindTransform("DodgeVisual");
        if (visual != null)
            visual.position -= transform.up * chassisAndTireDrop;
        var damageBody = FindTransform("DodgeDamageBody");
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
                    $"DodgeChallenger2018 wheel geometry vehicle={GetInstanceID()} corner={index} " +
                    $"could not be inset: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (correctedCorners == CornerNames.GetLength(0))
        {
            DodgeChallenger2018Diagnostics.Info(
                context,
                $"DodgeChallenger2018 wheel geometry vehicle={GetInstanceID()}: moved " +
                $"{correctedCorners} complete wheel/controller/caliper assemblies inward by " +
                $"{WheelAssemblyInsetMeters:F3}m and lowered the complete chassis/wheel datum by " +
                $"{chassisAndTireDrop:F3}m; rolling assemblies bound to steering controllers.");
        }
        else
        {
            context?.Logger.Warn(
                $"DodgeChallenger2018 wheel geometry vehicle={GetInstanceID()}: corrected " +
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
                        "Wheel2Mtl1",
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

public sealed class DodgeChallenger2018CollisionSeparationController : MonoBehaviour
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
internal sealed class DodgeChallenger2018HighwaySeamGuard : MonoBehaviour
{
    private const float MinimumSpeedMps = 25f;
    private const float MaximumSampleAgeSeconds = 0.16f;
    private const float MinimumUpwardContactNormal = 0.78f;
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
            !IsKnownHighwaySurface(other) || !HasUpwardContact(collision))
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

    private static bool IsKnownHighwaySurface(Collider collider)
    {
        for (var current = collider.transform; current != null; current = current.parent)
        {
            var objectName = current.name;
            foreach (var surfaceName in KnownHighwaySurfaceNames)
            {
                if (objectName.IndexOf(surfaceName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            // Highway pieces are not guaranteed to keep one of the three exact
            // prefab names after scene batching. Restrict the generic fallback to
            // hierarchy names containing "Highway"; ordinary curbs, walls and
            // vehicle collisions remain untouched.
            if (objectName.IndexOf("Highway", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }
}

[AddComponentMenu("")]
internal sealed class DodgeChallenger2018ContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        // Match the proven Revuelto body contact: low friction lets the rigid
        // bodies separate naturally after a crash instead of locking together.
        contactMaterial = new PhysicMaterial("2018 Dodge Challenger body contact")
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
public sealed class DodgeChallenger2018GlassController : MonoBehaviour
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
                    DodgeChallenger2018Materials.GetTransparentRole(renderer, source) !=
                    DodgeChallenger2018TransparentRole.CabinGlass)
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    DodgeChallenger2018Materials.RestoreCabinGlassMaterial(runtimeMaterial);
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
                    DodgeChallenger2018Materials.GetTransparentRole(renderer, material) ==
                    DodgeChallenger2018TransparentRole.CabinGlass)
                {
                    renderer.SetPropertyBlock(null, index);
                    DodgeChallenger2018Materials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            DodgeChallenger2018Diagnostics.Info(
                context,
                $"DodgeChallenger2018 glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Unlit-transparent, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            DodgeChallenger2018Diagnostics.Info(
                context,
                $"DodgeChallenger2018 glass vehicle={GetInstanceID()}: repaired after " +
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
public sealed class DodgeChallenger2018VisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.64f;
    private const float MaximumDentDepth = 0.30f;
    private const float DepthPerExcessMps = 0.010f;
    private const float FrontDentLateralRadius = 0.82f;
    private const float FrontDentVerticalRadius = 0.68f;
    private const float FrontDentLongitudinalRadius = 0.95f;
    private const float MaximumFrontDentDepth = 0.28f;
    private const float FrontDepthPerExcessMps = 0.0095f;
    private const float RearDentLateralRadius = 0.88f;
    private const float RearDentVerticalRadius = 0.72f;
    private const float RearDentLongitudinalRadius = 1.02f;
    private const float MaximumRearDentDepth = 0.42f;
    private const float RearDepthPerExcessMps = 0.013f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.5f;
    private const int MaximumDiagnosticLogs = 6;

    private readonly List<MeshFilter> deformableFilters = new List<MeshFilter>();
    private readonly Dictionary<MeshFilter, Vector3[]> originalVertices =
        new Dictionary<MeshFilter, Vector3[]>();
    private readonly Dictionary<MeshFilter, Vector3[]> currentVertices =
        new Dictionary<MeshFilter, Vector3[]>();
    private readonly Dictionary<MeshFilter, Bounds> originalBounds =
        new Dictionary<MeshFilter, Bounds>();
    private readonly Dictionary<MeshFilter, Matrix4x4> filterToVehicleMatrices =
        new Dictionary<MeshFilter, Matrix4x4>();
    private readonly Dictionary<MeshFilter, Matrix4x4> vehicleToFilterMatrices =
        new Dictionary<MeshFilter, Matrix4x4>();
    private readonly Dictionary<MeshFilter, bool> attachedDetails =
        new Dictionary<MeshFilter, bool>();
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
        currentVertices.Clear();
        originalBounds.Clear();
        filterToVehicleMatrices.Clear();
        vehicleToFilterMatrices.Clear();
        attachedDetails.Clear();
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

            var vertices = runtimeMesh.vertices;
            var baseline = new Vector3[vertices.Length];
            Array.Copy(vertices, baseline, vertices.Length);
            var filterToVehicle =
                controller.transform.worldToLocalMatrix *
                filter.transform.localToWorldMatrix;

            deformableFilters.Add(filter);
            originalVertices[filter] = baseline;
            currentVertices[filter] = vertices;
            originalBounds[filter] = runtimeMesh.bounds;
            filterToVehicleMatrices[filter] = filterToVehicle;
            vehicleToFilterMatrices[filter] = filterToVehicle.inverse;
            attachedDetails[filter] =
                IsAttachedExteriorDetail(filter, vertices.Length);
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
                if (pair.Key == null ||
                    !damageMeshes.TryGetValue(pair.Key, out var mesh) ||
                    mesh == null)
                    continue;

                pair.Key.sharedMesh = mesh;
                if (!currentVertices.TryGetValue(pair.Key, out var working) ||
                    working.Length != pair.Value.Length)
                {
                    working = new Vector3[pair.Value.Length];
                    currentVertices[pair.Key] = working;
                }

                Array.Copy(pair.Value, working, pair.Value.Length);
                mesh.vertices = working;
                if (originalBounds.TryGetValue(pair.Key, out var bounds))
                    mesh.bounds = bounds;
            }

            if (repairRecoveryCoroutine != null)
                StopCoroutine(repairRecoveryCoroutine);
            repairRecoveryCoroutine =
                StartCoroutine(RestoreDrivingStateAfterRepair());
            DodgeChallenger2018Diagnostics.DamageInfo(
                context,
                $"DodgeChallenger2018 damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
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
        if (!initialized || collision == null ||
            Time.unscaledTime < nextCollisionTime ||
            collision.relativeVelocity.magnitude < impactThresholdMps ||
            !NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision))
            return;

        try
        {
            var diagnosticsEnabled =
                DodgeChallenger2018Diagnostics.DebugEnabled &&
                DodgeChallenger2018Diagnostics.DamageDebugEnabled &&
                diagnosticLogs < MaximumDiagnosticLogs;
            var startedAt = diagnosticsEnabled
                ? System.Diagnostics.Stopwatch.GetTimestamp()
                : 0L;

            nextCollisionTime = Time.unscaledTime + CollisionCooldown;
            var contactCount = collision.contactCount;
            if (contactCount <= 0)
                return;

            var relativeSpeed = collision.relativeVelocity.magnitude;
            var excessSpeed = relativeSpeed - impactThresholdMps;
            var dentDepth = Mathf.Clamp(
                excessSpeed * DepthPerExcessMps,
                0.025f,
                MaximumDentDepth);
            var frontDentDepth = Mathf.Clamp(
                excessSpeed * FrontDepthPerExcessMps,
                0.04f,
                MaximumFrontDentDepth);
            var rearDentDepth = Mathf.Clamp(
                excessSpeed * RearDepthPerExcessMps,
                0.04f,
                MaximumRearDentDepth);

            // Same hot-path pattern as the Amarok: contacts are converted once
            // to vehicle-local space, then reused for all meshes and vertices.
            var worldContactPoints = new Vector3[contactCount];
            var localContactPoints = new Vector3[contactCount];
            var localInwardDirections = new Vector3[contactCount];
            var contactIsEnd = new bool[contactCount];
            var contactIsFront = new bool[contactCount];
            var contactRadiusSquared = new float[contactCount];
            var invLateralRadiusSquared = new float[contactCount];
            var invVerticalRadiusSquared = new float[contactCount];
            var invLongitudinalRadiusSquared = new float[contactCount];

            var vehicleTransform = transform;
            var center = body != null
                ? body.worldCenterOfMass
                : vehicleTransform.position;

            for (var contactIndex = 0;
                 contactIndex < contactCount;
                 contactIndex++)
            {
                var contact = collision.GetContact(contactIndex);
                var localContact =
                    vehicleTransform.InverseTransformPoint(contact.point);
                var isEndContact =
                    Mathf.Abs(localContact.z) >=
                    EndContactMinimumLongitudinalOffset &&
                    Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                var isFrontContact =
                    isEndContact && localContact.z >= 0f;

                worldContactPoints[contactIndex] = contact.point;
                localContactPoints[contactIndex] = localContact;
                contactIsEnd[contactIndex] = isEndContact;
                contactIsFront[contactIndex] = isFrontContact;

                if (isEndContact)
                {
                    var lateralRadius = isFrontContact
                        ? FrontDentLateralRadius
                        : RearDentLateralRadius;
                    var verticalRadius = isFrontContact
                        ? FrontDentVerticalRadius
                        : RearDentVerticalRadius;
                    var longitudinalRadius = isFrontContact
                        ? FrontDentLongitudinalRadius
                        : RearDentLongitudinalRadius;

                    invLateralRadiusSquared[contactIndex] =
                        1f / (lateralRadius * lateralRadius);
                    invVerticalRadiusSquared[contactIndex] =
                        1f / (verticalRadius * verticalRadius);
                    invLongitudinalRadiusSquared[contactIndex] =
                        1f / (longitudinalRadius * longitudinalRadius);

                    var maximumRadius = Mathf.Max(
                        lateralRadius,
                        Mathf.Max(verticalRadius, longitudinalRadius));
                    contactRadiusSquared[contactIndex] =
                        maximumRadius * maximumRadius;
                    localInwardDirections[contactIndex] =
                        isFrontContact ? Vector3.back : Vector3.forward;
                }
                else
                {
                    var towardCenter =
                        (center - contact.point).normalized;
                    var contactNormal = contact.normal.normalized;
                    var worldDirection =
                        Vector3.Dot(contactNormal, towardCenter) >= 0f
                            ? contactNormal
                            : -contactNormal;
                    localInwardDirections[contactIndex] =
                        vehicleTransform.InverseTransformDirection(
                            worldDirection).normalized;
                    contactRadiusSquared[contactIndex] =
                        DentRadius * DentRadius;
                }
            }

            var primaryLocalContact = localContactPoints[0];
            var changedMeshes = 0;
            var changedVertices = 0;
            var skippedMeshes = 0;
            var frontImpact = false;
            var rearImpact = false;

            foreach (var filter in deformableFilters)
            {
                if (filter == null || filter.sharedMesh == null ||
                    !currentVertices.TryGetValue(filter, out var vertices) ||
                    !originalVertices.TryGetValue(filter, out var baseline))
                    continue;

                // Reject whole meshes before touching their vertex arrays. This
                // is the largest Amarok win on large authored vehicle meshes.
                var renderer = filter.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var canReachFilter = false;
                    var bounds = renderer.bounds;
                    for (var contactIndex = 0;
                         contactIndex < contactCount;
                         contactIndex++)
                    {
                        if (bounds.SqrDistance(
                                worldContactPoints[contactIndex]) <=
                            contactRadiusSquared[contactIndex])
                        {
                            canReachFilter = true;
                            break;
                        }
                    }

                    if (!canReachFilter)
                    {
                        skippedMeshes++;
                        continue;
                    }
                }

                if (!filterToVehicleMatrices.TryGetValue(
                        filter, out var filterToVehicle) ||
                    !vehicleToFilterMatrices.TryGetValue(
                        filter, out var vehicleToFilter))
                {
                    filterToVehicle =
                        vehicleTransform.worldToLocalMatrix *
                        filter.transform.localToWorldMatrix;
                    vehicleToFilter = filterToVehicle.inverse;
                    filterToVehicleMatrices[filter] = filterToVehicle;
                    vehicleToFilterMatrices[filter] = vehicleToFilter;
                }

                var mesh = filter.sharedMesh;
                var attachedDetail =
                    attachedDetails.TryGetValue(filter, out var detail) &&
                    detail;
                var meshChanged = false;
                var changedVerticesInMesh = 0;
                var totalVehicleDisplacement = Vector3.zero;

                for (var vertexIndex = 0;
                     vertexIndex < vertices.Length;
                     vertexIndex++)
                {
                    var vehicleVertex =
                        filterToVehicle.MultiplyPoint3x4(
                            vertices[vertexIndex]);
                    var strongestInfluence = 0f;
                    var inwardDirection = Vector3.zero;
                    var selectedDepth = dentDepth;
                    var selectedEndImpact = false;
                    var selectedFrontImpact = false;

                    for (var contactIndex = 0;
                         contactIndex < contactCount;
                         contactIndex++)
                    {
                        var localDelta =
                            vehicleVertex -
                            localContactPoints[contactIndex];
                        float influence;

                        if (contactIsEnd[contactIndex])
                        {
                            var normalizedDistanceSquared =
                                localDelta.x * localDelta.x *
                                invLateralRadiusSquared[contactIndex] +
                                localDelta.y * localDelta.y *
                                invVerticalRadiusSquared[contactIndex] +
                                localDelta.z * localDelta.z *
                                invLongitudinalRadiusSquared[contactIndex];
                            if (normalizedDistanceSquared >= 1f)
                                continue;

                            influence =
                                1f - Mathf.Sqrt(
                                    normalizedDistanceSquared);
                        }
                        else
                        {
                            var distanceSquared =
                                localDelta.sqrMagnitude;
                            if (distanceSquared >=
                                contactRadiusSquared[contactIndex])
                                continue;

                            influence =
                                1f -
                                Mathf.Sqrt(distanceSquared) / DentRadius;
                        }

                        if (influence <= strongestInfluence)
                            continue;

                        strongestInfluence = influence;
                        inwardDirection =
                            localInwardDirections[contactIndex];
                        selectedEndImpact =
                            contactIsEnd[contactIndex];
                        selectedFrontImpact =
                            contactIsFront[contactIndex];
                        selectedDepth = selectedEndImpact
                            ? selectedFrontImpact
                                ? frontDentDepth
                                : rearDentDepth
                            : dentDepth;
                    }

                    if (strongestInfluence <= 0f ||
                        inwardDirection.sqrMagnitude < 0.5f)
                        continue;

                    var falloff = selectedEndImpact
                        ? Mathf.Pow(strongestInfluence, 1.35f)
                        : strongestInfluence * strongestInfluence;
                    var vehicleDisplacement =
                        inwardDirection * (selectedDepth * falloff);

                    if (attachedDetail)
                    {
                        // Small lamps/badges still follow the surrounding dent,
                        // but without allocating a displacement array per impact.
                        totalVehicleDisplacement += vehicleDisplacement;
                        changedVerticesInMesh++;
                        meshChanged = true;
                    }
                    else
                    {
                        vehicleVertex += vehicleDisplacement;
                        var localVertex =
                            vehicleToFilter.MultiplyPoint3x4(
                                vehicleVertex);

                        if (vertexIndex < baseline.Length)
                        {
                            var cumulativeLimit = selectedEndImpact
                                ? selectedFrontImpact
                                    ? MaximumFrontDentDepth
                                    : MaximumRearDentDepth
                                : MaximumDentDepth;
                            localVertex =
                                baseline[vertexIndex] +
                                Vector3.ClampMagnitude(
                                    localVertex -
                                    baseline[vertexIndex],
                                    cumulativeLimit);
                        }

                        vertices[vertexIndex] = localVertex;
                        changedVertices++;
                        meshChanged = true;
                    }

                    frontImpact |=
                        selectedEndImpact && selectedFrontImpact;
                    rearImpact |=
                        selectedEndImpact && !selectedFrontImpact;
                }

                if (!meshChanged)
                    continue;

                if (attachedDetail && changedVerticesInMesh > 0)
                {
                    var averageVehicleDisplacement =
                        totalVehicleDisplacement /
                        changedVerticesInMesh;
                    var localDisplacement =
                        vehicleToFilter.MultiplyVector(
                            averageVehicleDisplacement);
                    for (var vertexIndex = 0;
                         vertexIndex < vertices.Length;
                         vertexIndex++)
                    {
                        vertices[vertexIndex] += localDisplacement;
                    }

                    changedVertices += vertices.Length;
                }

                // Deformation is inward, so the spawn-time bounds remain a safe
                // conservative culling volume. Avoid a full bounds rebuild on hits.
                mesh.vertices = vertices;
                if (attachedDetail)
                {
                    // The whole detail moved, so translate its cached bounds too.
                    // This avoids a full bounds rebuild while keeping later crash
                    // culling accurate for lamps/badges that followed the dent.
                    var translatedBounds = mesh.bounds;
                    var averageVehicleDisplacement =
                        totalVehicleDisplacement /
                        Mathf.Max(1, changedVerticesInMesh);
                    translatedBounds.center +=
                        vehicleToFilter.MultiplyVector(
                            averageVehicleDisplacement);
                    mesh.bounds = translatedBounds;
                }
                else if (originalBounds.TryGetValue(
                             filter, out var originalMeshBounds))
                {
                    mesh.bounds = originalMeshBounds;
                }
                changedMeshes++;
            }

            if (diagnosticsEnabled)
            {
                diagnosticLogs++;
                var elapsedMilliseconds =
                    (System.Diagnostics.Stopwatch.GetTimestamp() -
                     startedAt) *
                    1000d /
                    System.Diagnostics.Stopwatch.Frequency;
                DodgeChallenger2018Diagnostics.DamageInfo(
                    context,
                    $"DodgeChallenger2018 damage vehicle={vehicle?.GetInstanceID()}: " +
                    $"inward dent contact='{collision.collider?.name ?? "unknown"}' " +
                    $"relativeSpeed={relativeSpeed * 3.6f:0.0}kph " +
                    $"localContact=({primaryLocalContact.x:0.00}," +
                    $"{primaryLocalContact.y:0.00}," +
                    $"{primaryLocalContact.z:0.00}) " +
                    $"region={(frontImpact ? "front" : rearImpact ? "rear" : "side")} " +
                    $"depth={(frontImpact ? frontDentDepth : rearImpact ? rearDentDepth : dentDepth):0.000}m " +
                    $"meshes={changedMeshes} skipped={skippedMeshes} " +
                    $"vertices={changedVertices} " +
                    $"processing={elapsedMilliseconds:0.0}ms " +
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
                $"DodgeChallenger2018 damage vehicle={vehicle?.GetInstanceID()}: " +
                $"inward deformation failed with " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static bool HasAncestor(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
        {
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool IsAttachedExteriorDetail(MeshFilter filter, int vertexCount)
    {
        if (filter.name.StartsWith("DodgeLamp_", StringComparison.Ordinal) ||
            filter.name.StartsWith("DodgeCrashDetail_", StringComparison.Ordinal) ||
            HasAncestor(filter.transform, ":Badge") ||
            HasAncestor(filter.transform, ":GlassRed") ||
            (HasAncestor(filter.transform, ":Glass") &&
             !HasAncestor(filter.transform, ":Window")) ||
            HasAncestor(filter.transform, ":Textured"))
        {
            return true;
        }

        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;
        var size = renderer.bounds.size;
        var longestSide = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        return vertexCount <= 280 || longestSide <= 0.24f;
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

