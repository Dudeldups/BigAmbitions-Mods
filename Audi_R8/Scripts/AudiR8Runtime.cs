#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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

public sealed class AudiR8Runtime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1595f;
    private const float EnginePowerKw = 456f;
    private const float VehicleBrakeForce = 1960f;
    private const float BrakeMaxTorque = 2240f;
    private const float EngineIdleRpm = 900f;
    private const float MinimumHealthyEngineRpm = EngineIdleRpm * 0.5f;
    private const int EngineStartAttemptCount = 3;
    private const float EngineLimitRpm = 8700f;
    private const float SpeedLimitKph = 331f;
    private const float FinalDriveRatio = 3.75f;
    private const float EngineInertia = 0.20f;
    private const bool EngineStallingEnabled = false;
    private const bool EngineAutoStartOnThrottle = true;
    private const float EngineStartDuration = 0.38f;
    private const float ClutchEngagementRpm = 1250f;
    private const float ClutchThrottleOffsetRpm = 600f;
    private const float ClutchEngagementRange = 550f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 1.02f;
    private const float AntiRollBarForce = 11000f;
    private const float FrontSuspensionTravel = 0.075f;
    private const float RearSuspensionTravel = 0.080f;
    private const float FrontTireRadius = 0.3275f;
    private const float RearTireRadius = 0.3455f;
    private const float FrontTireWidth = 0.245f;
    private const float RearTireWidth = 0.305f;
    private const float VehicleLinearDrag = 0f; // Quadratic road drag is owned by AudiR8RoadDynamics.
    private const float FrontForwardGrip = 0.40f;
    private const float RearForwardGrip = 0.415f;
    private const float FrontForwardStiffness = 1.20f;
    private const float RearForwardStiffness = 1.28f;
    private const float DeformationStrength = 0.14f;
    private const float DeformationRadius = 0.30f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 1f;
    private const float DamageDecelerationThreshold = 500f;
    private const float BodyVisualDropMeters = 0.015f;
    private static readonly int DrivableSurfaceLayerMask = LayerMask.GetMask(
        "Ground", "Sidewalk", "Crosswalk", "Roads", "GroundUnplacable", "ParkingArea");
    private const float WarehouseExitGuardDuration = 8f;
    private const float WarehouseExitGuardClearDistance = 4f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.12f, -0.12f);
    private static readonly Vector3 DriverExitPosition =
        new Vector3(-1.55f, 0.18f, 0.10f);
    private static readonly Vector3 PassengerExitPosition =
        new Vector3(1.55f, 0.18f, 0.10f);

    private static readonly float[] Gears =
    {
        -3.147f,
        0f,
        3.725f,
        2.477f,
        1.799f,
        1.355f,
        1.067f,
        0.846f,
        0.625f,
    };

    private static AnimationCurve CreatePowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.08f, 0.04f),
            new Keyframe(0.15f, 0.14f),
            new Keyframe(0.25f, 0.23f),
            new Keyframe(0.35f, 0.34f),
            new Keyframe(0.45f, 0.48f),
            new Keyframe(0.55f, 0.60f),
            new Keyframe(0.65f, 0.74f),
            new Keyframe(0.747f, 0.87f),
            new Keyframe(0.86f, 0.985f),
            new Keyframe(0.90f, 1f),
            new Keyframe(0.95f, 0.99f),
            new Keyframe(1f, 0.90f));

    private static UnityEngine.Object? fallbackCarSleepConfig;

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private Coroutine? drivetrainDiagnosticsCoroutine;
    private int enteredVehicleActivationInstanceId;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private AudiR8WarehouseEntryController? warehouseExitGuardEntryController;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private int cachedPlayerVehicleCount = -1;
    private bool dealerRegistrationReady;
    private bool dealerReadyLogged;

    public static AudiR8Runtime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject _)
    {
        var runtime = FindObjectOfType<AudiR8Runtime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(AudiR8Runtime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<AudiR8Runtime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
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
        StopDrivetrainDiagnostics();
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
        StopWarehouseExitGuard();
        configuredVehicleIds.Clear();
        dealerRegistrationReady = false;
        dealerReadyLogged = false;
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
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
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
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
    }

    private void HandleGameEvent(string eventName)
    {
        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        AudiR8Diagnostics.DealerEntryInfo(
            context,
            $"AudiR8 dealer-entry: game-event='{eventName}', " +
            DescribeVehicleState(selectedVehicle));

        // Dealer purchases do not reliably invoke onEnterVehicle. This event
        // is a bounded lifecycle handoff from the dealer display vehicle to
        // the owned, player-controlled instance, so configure it here rather
        // than scanning vehicles every frame.
        TryConfigureVehicle(selectedVehicle);
        selectedVehicle!
            .GetComponent<AudiR8PaintController>()
            ?.ApplyCurrentColor("game-event");
    }

    private void HandleVehicleVariablesChanged() => ConfigureExistingVehicles(out _);

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SubscribeEvents();
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeEvents();
        ScheduleInitialization("game-loaded-late");
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
        StopDrivetrainDiagnostics();
        if (exitedPlayerRecoveryCoroutine != null)
            StopCoroutine(exitedPlayerRecoveryCoroutine);
        exitedPlayerRecoveryCoroutine = null;
        StopWarehouseExitGuard();
        configuredVehicleIds.Clear();
        cachedPlayerVehicleCount = -1;
        dealerRegistrationReady = false;
        dealerReadyLogged = false;
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        var targetVehicle = IsTargetVehicle(vehicle);
        AudiR8Diagnostics.DealerEntryInfo(
            context,
            $"AudiR8 dealer-entry: onEnterVehicle target={targetVehicle}, " +
            DescribeVehicleState(vehicle));
        TryConfigureVehicle(vehicle);
        vehicle.GetComponent<AudiR8GlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle == null || !targetVehicle)
            return;
        vehicle.GetComponent<AudiR8PaintController>()
            ?.ApplyCurrentColor("vehicle-entered");
        ScheduleEnteredVehicleActivation(vehicle);
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;
        AudiR8Diagnostics.WarehouseExitInfo(
            context,
            $"AudiR8 warehouse-exit: onExitVehicle, {DescribeVehicleState(vehicle)}");
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        StopDrivetrainDiagnostics();
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
        AudiR8Diagnostics.WarehouseExitInfo(
            context,
            $"AudiR8 player-exit: deferred validation playerPosition={root.position}, " +
            $"agentCount={agents.Length}, anyAgentOffNavMesh={agents.Any(agent => agent != null && agent.enabled && !agent.isOnNavMesh)}, " +
            $"initialClear={initialExitClear}, needsRecovery={needsRecovery}.");
        if (!needsRecovery)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }
        if (!TryFindClearExitPosition(root, exitedVehicle, out var target))
        {
            AudiR8Diagnostics.WarehouseExitInfo(
                context,
                "AudiR8 player-exit: recovery failed; no NavMesh/capsule-clear candidate found.");
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
        AudiR8Diagnostics.WarehouseExitInfo(
            context,
            $"AudiR8 player-exit: recovery moved player to {target}.");
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
               $"instanceType={vehicle.vehicleInstance?.vehicleTypeName ?? "<null>"}, " +
               $"assetType={vehicle.vehicleType?.vehicleTypeName ?? "<null>"}, " +
               $"selected={ReferenceEquals(InstanceBehavior<GameManager>.Instance?.selectedVehicle, vehicle)}, " +
               $"position={vehicle.transform.position}, physics={(physics == null ? "missing" : physics.enabled.ToString())}, " +
               $"nwhInitialized={physics?.IsInitialized}, powertrainActive={physics?.powertrain?.IsActive}, " +
               $"engineActive={engine?.IsActive}, transmissionActive={transmission?.IsActive}, " +
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
            AudiR8Diagnostics.DealerEntryInfo(
                context,
                $"AudiR8 dealer-entry: activation skipped because the vehicle is " +
                $"neither controlled nor selected, {DescribeVehicleState(vehicle)}");
            return;
        }

        var instanceId = vehicle.GetInstanceID();
        if (enteredVehicleActivationCoroutine != null &&
            enteredVehicleActivationInstanceId == instanceId)
        {
            AudiR8Diagnostics.DealerEntryInfo(
                context,
                $"AudiR8 dealer-entry: activation already pending instance={instanceId}.");
            return;
        }

        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);

        enteredVehicleActivationInstanceId = instanceId;
        AudiR8Diagnostics.DealerEntryInfo(
            context,
            $"AudiR8 dealer-entry: activation scheduled, {DescribeVehicleState(vehicle)}");
        enteredVehicleActivationCoroutine = StartCoroutine(ActivateEnteredVehicle(vehicle));
    }

    private IEnumerator ActivateEnteredVehicle(VehicleController vehicle)
    {
        // Native entry owns the vehicle physics and wheel state. Preserve a
        // running engine and bring the transmission into first without
        // resetting through neutral during the activation handshake.
        yield return new WaitForSecondsRealtime(.25f);
        if (vehicle == null || !vehicle.controlledByPlayer)
        {
            enteredVehicleActivationCoroutine = null;
            enteredVehicleActivationInstanceId = 0;
            yield break;
        }

        var physics = vehicle.GetComponent<PhysicsVehicle>() ??
                      vehicle.GetComponentInChildren<PhysicsVehicle>(true);
        if (physics == null)
        {
            AudiR8Diagnostics.DealerEntryInfo(
                context,
                $"AudiR8 dealer-entry: activation failed; NWH controller missing, " +
                DescribeVehicleState(vehicle));
            enteredVehicleActivationCoroutine = null;
            enteredVehicleActivationInstanceId = 0;
            yield break;
        }

        physics.enabled = true;
        for (var pass = 0; pass < 20 && vehicle != null &&
             vehicle.controlledByPlayer && !physics.IsInitialized; pass++)
        {
            yield return new WaitForFixedUpdate();
        }
        if (!physics.IsInitialized)
        {
            context?.Logger.Warn(
                $"AudiR8 drivetrain vehicle={vehicle?.GetInstanceID()}: " +
                "NWH VehicleController did not finish initialization before player entry.");
            enteredVehicleActivationCoroutine = null;
            enteredVehicleActivationInstanceId = 0;
            yield break;
        }
        if (vehicle == null || !vehicle.controlledByPlayer)
        {
            enteredVehicleActivationCoroutine = null;
            enteredVehicleActivationInstanceId = 0;
            yield break;
        }

        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        vehicle.GetComponent<AudiR8PaintController>()
            ?.ApplyCurrentColor("vehicle-entered");
        var reenabledComponents = ReactivatePlayerPowertrain(physics);
        var driveReady = false;
        for (var attempt = 0; attempt <= EngineStartAttemptCount; attempt++)
        {
            if (vehicle == null || !vehicle.controlledByPlayer)
            {
                AudiR8Diagnostics.DealerEntryInfo(
                    context,
                    $"AudiR8 dealer-entry: activation cancelled attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }

            vehicle.GetComponent<AudiR8PaintController>()
                ?.ApplyCurrentColor("vehicle-entered");
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            var engineHealthy = engine.IsRunning && engine.ignition && engine.canRun &&
                                rpm >= MinimumHealthyEngineRpm;
            AudiR8Diagnostics.DealerEntryInfo(
                context,
                $"AudiR8 dealer-entry: activation attempt={attempt + 1}, " +
                $"running={engine.IsRunning}, ignition={engine.ignition}, canRun={engine.canRun}, " +
                $"rpm={rpm:0}, gear={transmission.Gear}, " +
                $"ratio={transmission.currentGearRatio:0.000}, physicsEnabled={physics.enabled}.");
            if (engineHealthy)
            {
                // Native entry leaves the new player vehicle in neutral. Select
                // Drive only after the R8 engine has actually reached idle.
                if (transmission.Gear <= 0)
                {
                    transmission.ShiftInto(1, true);
                    AudiR8Diagnostics.DealerEntryInfo(
                        context,
                        "AudiR8 dealer-entry: healthy engine; shifted neutral to first.");
                }

                for (var settle = 0; settle < 4; settle++)
                    yield return new WaitForFixedUpdate();
                driveReady = transmission.Gear > 0 &&
                             Mathf.Abs(transmission.currentGearRatio) > 0.001f;
                if (!driveReady)
                {
                    AudiR8Diagnostics.DealerEntryInfo(
                        context,
                        $"AudiR8 dealer-entry: healthy engine but Drive did not engage; " +
                        $"gear={transmission.Gear}, ratio={transmission.currentGearRatio:0.000}.");
                    continue;
                }
                break;
            }

            if (attempt == EngineStartAttemptCount)
                break;

            AudiR8Diagnostics.DealerEntryInfo(
                context,
                $"AudiR8 dealer-entry: dormant powertrain; restart attempt={attempt + 1}, " +
                $"reactivatedComponents={reenabledComponents}.");
            // NWH can leave a first-entry start half-latched (IsRunning=true,
            // RPM=0). Always stop it, then give the starter a clean neutral state.
            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
            {
                AudiR8Diagnostics.DealerEntryInfo(
                    context,
                    $"AudiR8 dealer-entry: restart aborted after stop attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }

            engine.StartEngine();
            yield return new WaitForSecondsRealtime(.75f);
        }

        if (vehicle == null || !vehicle.controlledByPlayer)
        {
            enteredVehicleActivationCoroutine = null;
            enteredVehicleActivationInstanceId = 0;
            yield break;
        }

        var finalRpm = engine.RPMPercent * engine.revLimiterRPM;
        driveReady = driveReady &&
                     physics.IsInitialized &&
                     physics.powertrain.IsActive &&
                     engine.IsActive &&
                     transmission.IsActive &&
                     engine.IsRunning &&
                     engine.ignition &&
                     engine.canRun &&
                     finalRpm >= MinimumHealthyEngineRpm &&
                     transmission.Gear > 0 &&
                     Mathf.Abs(transmission.currentGearRatio) > 0.001f;
        if (driveReady)
        {
            context?.Logger.Info(
                $"AudiR8 drivetrain vehicle={vehicle!.GetInstanceID()}: ready " +
                $"rpm={finalRpm:0}, gear={transmission.Gear}, " +
                $"ratio={transmission.currentGearRatio:0.000}; " +
                DescribeDriveState(vehicle, physics));
        }
        else
        {
            context?.Logger.Warn(
                $"AudiR8 drivetrain vehicle={vehicle?.GetInstanceID()}: not ready after " +
                "player-entry activation; " +
                $"nwhInitialized={physics.IsInitialized}, " +
                $"powertrainActive={physics.powertrain.IsActive}, engineActive={engine.IsActive}, " +
                $"transmissionActive={transmission.IsActive}, running={engine.IsRunning}, " +
                $"ignition={engine.ignition}, canRun={engine.canRun}, rpm={finalRpm:0}, " +
                $"gear={transmission.Gear}, ratio={transmission.currentGearRatio:0.000}, " +
                $"inactiveComponents={DescribeInactivePowertrainComponents(physics)}; " +
                DescribeDriveState(vehicle, physics));
        }

        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        AudiR8Diagnostics.DealerEntryInfo(
            context,
            $"AudiR8 dealer-entry: activation completed, {DescribeVehicleState(vehicle)}");
        StartDrivetrainDiagnostics(vehicle, physics);
    }

    private void StartDrivetrainDiagnostics(VehicleController? vehicle, PhysicsVehicle? physics)
    {
        StopDrivetrainDiagnostics();
        if (vehicle != null && physics != null && vehicle.controlledByPlayer &&
            AudiR8Diagnostics.DebugEnabled && AudiR8Diagnostics.DrivetrainDebugEnabled)
            drivetrainDiagnosticsCoroutine = StartCoroutine(MonitorPlayerDriveState(vehicle, physics));
    }

    private void StopDrivetrainDiagnostics()
    {
        if (drivetrainDiagnosticsCoroutine != null)
            StopCoroutine(drivetrainDiagnosticsCoroutine);
        drivetrainDiagnosticsCoroutine = null;
    }

    private IEnumerator MonitorPlayerDriveState(VehicleController vehicle, PhysicsVehicle physics)
    {
        var startedAt = Time.unscaledTime;
        var nextLog = startedAt;
        while (vehicle != null && vehicle.controlledByPlayer && physics != null)
        {
            yield return new WaitForSecondsRealtime(1f);
            if (vehicle == null || !vehicle.controlledByPlayer || physics == null)
                break;
            if (!AudiR8Diagnostics.DebugEnabled || !AudiR8Diagnostics.DrivetrainDebugEnabled ||
                Time.unscaledTime < nextLog)
                continue;

            var input = physics.input;
            var hasInput = input != null &&
                           (input.Throttle > 0.05f || input.Brakes > 0.05f ||
                            input.Handbrake > 0.05f || Mathf.Abs(input.Steering) > 0.05f ||
                            Mathf.Abs(input.Vertical) > 0.05f);
            if (!hasInput && Time.unscaledTime - startedAt > 10f)
                continue;

            AudiR8Diagnostics.DrivetrainInfo(
                context,
                $"AudiR8 drivetrain-test vehicle={vehicle.GetInstanceID()}: " +
                DescribeDriveState(vehicle, physics));
            nextLog = Time.unscaledTime + 1f;
        }

        drivetrainDiagnosticsCoroutine = null;
    }

    private static string DescribeDriveState(VehicleController? vehicle, PhysicsVehicle physics)
    {
        var input = physics.input;
        var engine = physics.powertrain.engine;
        var clutch = physics.powertrain.clutch;
        var transmission = physics.powertrain.transmission;
        var rigidbody = vehicle == null ? null : vehicle.GetComponent<Rigidbody>();
        var velocity = vehicle == null || rigidbody == null ? Vector3.zero :
            vehicle.transform.InverseTransformDirection(rigidbody.velocity);
        var wheels = new List<string>();
        foreach (var wheel in physics.powertrain.wheels)
        {
            if (wheel == null)
                continue;
            wheels.Add($"{wheel.name}:torque={wheel.inputTorque:0.0}," +
                       $"surface={wheel.surfaceMapIndex}");
        }
        var wheelContact = new List<string>();
        if (vehicle != null)
        {
            foreach (var controller in
                     vehicle.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
            {
                if (controller == null)
                    continue;
                var groundDetection = GetMember(controller, "groundDetection");
                var grounded = GetMember(controller, "isGrounded") ??
                               GetMember(groundDetection, "isGrounded");
                var hitInfo = GetMember(controller, "wheelHit") ??
                              GetMember(groundDetection, "wheelHit");
                var hitCollider = GetMember(hitInfo, "collider") as Collider;
                wheelContact.Add(
                    $"{controller.name}:grounded={grounded?.ToString() ?? "unknown"}," +
                    $"mask={controller.layerMask.value},hit={hitCollider?.name ?? "none"}," +
                    $"position={controller.transform.position}");
            }
        }
        var engineRpm = engine.RPMPercent * engine.revLimiterRPM;
        var speedKph = rigidbody == null ? 0f : rigidbody.velocity.magnitude * 3.6f;
        var stalled = GetMember(engine, "IsStalled");
        var stallingEnabled = GetMember(engine, "stallingEnabled");
        var autoStartOnThrottle = GetMember(engine, "autoStartOnThrottle");

        return $"inputAuto={input?.autoSetInput}, throttle={input?.Throttle:0.00}, " +
               $"vertical={input?.Vertical:0.00}, brake={input?.Brakes:0.00}, " +
               $"handbrake={input?.Handbrake:0.00}, steer={input?.Steering:0.00}, " +
               $"engineRPM={engineRpm:0}, engineRunning={engine.IsRunning}, " +
               $"ignition={engine.ignition}, canRun={engine.canRun}, " +
               $"stalled={stalled?.ToString() ?? "unknown"}, " +
               $"stallingEnabled={stallingEnabled?.ToString() ?? "unknown"}, " +
               $"autoStartOnThrottle={autoStartOnThrottle?.ToString() ?? "unknown"}, " +
               $"speedKph={speedKph:0.0}, " +
               $"flyingStart={engine.flyingStartEnabled}, " +
               $"engineThrottle={engine.ThrottlePosition:0.00}, " +
               $"engineOut={engine.outputTorque:0.0}, clutchOut={clutch.outputTorque:0.0}, " +
               $"transmissionOut={transmission.outputTorque:0.0}, " +
               $"wheelInputs=[{string.Join(";", wheels)}], " +
               $"wheelContact=[{string.Join(";", wheelContact)}], " +
               $"localVelocity={velocity}.";
    }

    private static int ReactivatePlayerPowertrain(PhysicsVehicle physics)
    {
        var powertrain = physics.powertrain;
        var reenabled = 0;
        if (!powertrain.IsActive && powertrain.VC_Enable(true))
            reenabled++;

        foreach (var component in powertrain.Components)
        {
            if (component != null && !component.IsActive && component.VC_Enable(true))
                reenabled++;
        }

        return reenabled;
    }

    private static string DescribeInactivePowertrainComponents(PhysicsVehicle physics)
    {
        var inactive = new List<string>();
        foreach (var component in physics.powertrain.Components)
        {
            if (component != null && !component.IsActive)
                inactive.Add(component.GetType().Name);
        }

        return inactive.Count == 0 ? "none" : string.Join(",", inactive);
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!AudiR8LuxuryDealerStock.IsTargetDealer(registration?.BusinessName) ||
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
            AudiR8Diagnostics.WarehouseExitInfo(
                context,
                $"AudiR8 warehouse-exit: guard skipped; current vehicle is not " +
                $"a controlled AudiR8 ({DescribeVehicleState(vehicle)}).");
            return;
        }

        var entrance = FindClosestDriveInEntrance(vehicle.transform.position);
        if (entrance == null)
        {
            AudiR8Diagnostics.WarehouseExitInfo(
                context,
                $"AudiR8 warehouse-exit: guard skipped; no DriveInEntrance found, " +
                DescribeVehicleState(vehicle));
            return;
        }

        var nativeMeshCollider = vehicle.GetComponentInChildren<MeshCollider>(true);
        AudiR8Diagnostics.WarehouseExitInfo(
            context,
            $"AudiR8 warehouse-exit: native exit completed, {DescribeVehicleState(vehicle)} " +
            $"entrance='{entrance.name}' entrancePosition={entrance.transform.position}, " +
            $"nativeMesh='{nativeMeshCollider?.name ?? "missing"}', " +
            $"nativeMeshLength={nativeMeshCollider?.sharedMesh?.bounds.size.z:0.000}.");

        StopWarehouseExitGuard();
        warehouseExitGuardEntryController =
            vehicle.GetComponent<AudiR8WarehouseEntryController>();
        warehouseExitGuardEntryController?.SuppressEntrance(entrance, "warehouse-exit-guard");
        foreach (var enterTrigger in entrance.GetComponentsInChildren<DriveInEntranceEnterTrigger>(true))
        foreach (var collider in enterTrigger.GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled || !collider.isTrigger)
                continue;

            warehouseExitGuardColliders.Add(collider);
            AudiR8Diagnostics.WarehouseExitInfo(
                context,
                $"AudiR8 warehouse-exit: disabling entry trigger " +
                $"'{collider.name}' bounds={collider.bounds}.");
            collider.enabled = false;
        }

        if (warehouseExitGuardColliders.Count == 0)
        {
            warehouseExitGuardEntryController?.ClearSuppressedEntrance(
                entrance,
                "no-native-entry-trigger");
            warehouseExitGuardEntryController = null;
            AudiR8Diagnostics.WarehouseExitInfo(
                context,
                "AudiR8 warehouse-exit: guard skipped; matching entrance had no enabled trigger colliders.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(
            vehicle.transform.position - entrance.transform.position,
            Vector3.up);
        if (outward.sqrMagnitude < .0001f)
            outward = Vector3.ProjectOnPlane(entrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < .0001f)
        {
            AudiR8Diagnostics.WarehouseExitInfo(
                context,
                "AudiR8 warehouse-exit: guard aborted; outward direction was zero.");
            StopWarehouseExitGuard();
            return;
        }

        outward.Normalize();
        var startingDistance = Vector3.Dot(vehicle.transform.position, outward);
        Physics.SyncTransforms();
        AudiR8Diagnostics.WarehouseExitInfo(
            context,
            $"AudiR8 warehouse-exit: guard started triggerCount={warehouseExitGuardColliders.Count}, " +
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
        AudiR8Diagnostics.WarehouseExitInfo(
            context,
            $"AudiR8 warehouse-exit: guard ending reason={reason}, " +
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
            AudiR8Diagnostics.WarehouseExitInfo(
                context,
                $"AudiR8 warehouse-exit: restoring triggerCount={warehouseExitGuardColliders.Count}, " +
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
            ScheduleInitialization("full-menu");
        else if (!dealerRegistrationReady)
            EnsureDealerStock("full-menu");
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
            while (!dealerReady && BusinessLayoutSetHelper.loadingLayouts)
            {
                ConfigureExistingVehicles(out var waitingMatchedCount);
                maximumMatchedCount = Math.Max(maximumMatchedCount, waitingMatchedCount);
                yield return new WaitForSecondsRealtime(InitializationRetryDelay);
            }

            if (!dealerReady)
                dealerReady = EnsureDealerStock(source);
            ConfigureExistingVehicles(out var matchedCount);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);

            if (dealerReady && matchedCount == previousMatchedCount)
                stablePasses++;
            else
                stablePasses = 0;
            previousMatchedCount = matchedCount;

            if (dealerReady && stablePasses >= RequiredStablePasses)
                break;
            if (attempt < InitializationRetryCount)
                yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        initializationCoroutine = null;
        if (!dealerReady)
            context?.Logger.Warn(
                $"AudiR8: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
    }

    private bool EnsureDealerStock(string source)
    {
        if (dealerRegistrationReady)
            return true;
        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        try
        {
            var ready = AudiR8LuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerRegistrationReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                AudiR8Diagnostics.Info(
                    context,
                    $"AudiR8: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"AudiR8: dealer stock update failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
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
        if (vehicle?.vehicleInstance == null ||
            !string.Equals(
                vehicle.vehicleInstance.vehicleTypeName,
                vehicleTypeName,
                StringComparison.Ordinal))
        {
            return;
        }

        var instanceId = vehicle.GetInstanceID();
        if (!configuredVehicleIds.Add(instanceId))
        {
            vehicle.GetComponent<AudiR8PaintController>()
                ?.ApplyCurrentColor("vehicle-variables-changed");
            return;
        }

        try
        {
            var vehicleTypeBrakeConfigured =
                SetFloat(GetMember(vehicle, "vehicleType"), "brakeForce", VehicleBrakeForce);
            if (!vehicleTypeBrakeConfigured)
                context?.Logger.Warn(
                    $"AudiR8 brakes vehicle={instanceId}: could not apply " +
                    $"vehicle brake force {VehicleBrakeForce:0} at runtime.");

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
                rigidbody.maxDepenetrationVelocity = 2f;

                var highwaySeamGuard =
                    vehicle.GetComponent<AudiR8HighwaySeamGuard>();
                if (highwaySeamGuard == null)
                {
                    highwaySeamGuard = vehicle.gameObject
                        .AddComponent<AudiR8HighwaySeamGuard>();
                }
                highwaySeamGuard.Initialize(rigidbody);
            }

            ConfigureMassProperties(vehicle.gameObject);
            var configuredWheelControllers = ConfigureWheelControllers(vehicle.gameObject);
            var contactMaterialOwner =
                vehicle.GetComponent<AudiR8ContactMaterialOwner>();
            if (contactMaterialOwner == null)
            {
                contactMaterialOwner = vehicle.gameObject
                    .AddComponent<AudiR8ContactMaterialOwner>();
            }
            ConfigureBodyColliders(
                vehicle.gameObject,
                contactMaterialOwner.GetOrCreateMaterial(),
                context);
            ConfigureExitMarkers(vehicle.gameObject);
            ConfigureBodyVisualOffset(vehicle.gameObject);
            var sleepEnvironmentConfigured = ConfigureSleepEnvironment(vehicle);
            AudiR8WashBindings.Configure(vehicle, context);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(vehicle.gameObject);
            var warehouseBounds =
                vehicle.GetComponent<AudiR8WarehouseBoundsController>();
            if (warehouseBounds == null)
            {
                warehouseBounds = vehicle.gameObject
                    .AddComponent<AudiR8WarehouseBoundsController>();
            }
            warehouseBounds.Initialize();
            var warehouseEntry = vehicle.GetComponent<AudiR8WarehouseEntryController>();
            if (warehouseEntry == null)
            {
                warehouseEntry = vehicle.gameObject
                    .AddComponent<AudiR8WarehouseEntryController>();
            }
            warehouseEntry.Initialize(vehicle, context);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject, context);
            var roadDynamics = vehicle.GetComponent<AudiR8RoadDynamics>();
            if (roadDynamics == null)
                roadDynamics = vehicle.gameObject.AddComponent<AudiR8RoadDynamics>();
            var roadPhysics = vehicle.GetComponent<PhysicsVehicle>();
            if (roadPhysics != null) roadDynamics.Initialize(roadPhysics, context);
            if (!powertrainConfigured)
            {
                context?.Logger.Warn(
                    $"AudiR8 drivetrain configuration incomplete instance={instanceId}: " +
                    $"inertia={EngineInertia:0.000}, stallingEnabled={EngineStallingEnabled}, " +
                    $"autoStartOnThrottle={EngineAutoStartOnThrottle}; " +
                    "check NWH engine, brake and quattro differential fields.");
            }
            var caliperController = vehicle.GetComponent<AudiR8CaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<AudiR8CaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialController = vehicle.GetComponent<AudiR8MaterialController>();
            if (materialController == null)
                materialController = vehicle.gameObject.AddComponent<AudiR8MaterialController>();
            var materialResult = materialController.Initialize(context);
            var glassController = vehicle.GetComponent<AudiR8GlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<AudiR8GlassController>();
            glassController.Initialize(context);
            var paintController = vehicle.GetComponent<AudiR8PaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<AudiR8PaintController>();
            paintController.Initialize(vehicle, context);
            var lightingController = vehicle.GetComponent<AudiR8LightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<AudiR8LightingController>();
            lightingController.Initialize(vehicle, context);
            // Lighting overlays are spawned per vehicle. Build the deformation
            // allowlist only after they exist so illuminated lamp surfaces move
            // with the surrounding lamp housings after an impact.
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var driverController = vehicle.GetComponent<AudiR8DriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<AudiR8DriverController>();
            driverController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<AudiR8AudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<AudiR8AudioController>();
            audioController.Initialize(vehicle, context);
            AudiR8Diagnostics.Info(
                context,
                $"AudiR8: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=7-speed-S-tronic, quattroBias=40:60, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"wheelContactControllers={configuredWheelControllers}/4 " +
                $"surfaceMask={DrivableSurfaceLayerMask}, " +
                $"sleepEnvironmentConfigured={sleepEnvironmentConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstaclesNormalized={normalizedNavMeshObstacles}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"stallingEnabled={EngineStallingEnabled}, " +
                $"autoStartOnThrottle={EngineAutoStartOnThrottle}, " +
                $"powerCurve=5.2L-naturally-aspirated-V10-profile, steeringCalipers=4, " +
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
                $"AudiR8: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static int ConfigureWheelControllers(GameObject root)
    {
        var configured = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            var isFront = transform.name.StartsWith("Front", StringComparison.Ordinal);
            var isRear = transform.name.StartsWith("Rear", StringComparison.Ordinal);
            if ((!isFront && !isRear) ||
                !transform.name.EndsWith("_WheelController", StringComparison.Ordinal))
                continue;

            var wheelController = transform.GetComponent<NWH.WheelController3D.WheelController>();
            if (wheelController != null && DrivableSurfaceLayerMask != 0)
            {
                wheelController.layerMask = DrivableSurfaceLayerMask;
                configured++;
            }

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

    private static void ConfigureBodyColliders(
        GameObject root,
        PhysicMaterial contactMaterial,
        ModContext? diagnosticContext)
    {
        var body = root.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.solverIterations = Mathf.Max(body.solverIterations, 12);
            body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, 4);
        }

        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BodyCollider", StringComparison.Ordinal))
                continue;

            var colliders = transform.GetComponents<BoxCollider>();
            if (colliders.Length > 0)
            {
                // Keep one full shell for traffic impacts. Overlapping bumper,
                // lower-body, and cabin boxes produced several solver impulses
                // on the same hit and made the light mid-engine car hop.
                colliders[0].center = new Vector3(0f, 0.67f, -0.05f);
                colliders[0].size = new Vector3(1.80f, 1.10f, 4.36f);
                colliders[0].isTrigger = false;
                colliders[0].enabled = true;
            }
            for (var index = 1; index < colliders.Length; index++)
                colliders[index].enabled = false;

            if (colliders.Length > 0)
                colliders[0].sharedMaterial = contactMaterial;

            AudiR8Diagnostics.CollisionInfo(
                diagnosticContext,
                $"AudiR8 impact setup vehicle={root.GetInstanceID()}: " +
                $"activeBodyBoxes={(colliders.Length > 0 ? 1 : 0)} " +
                $"disabledOverlappingBoxes={Math.Max(0, colliders.Length - 1)} " +
                $"solver={body?.solverIterations}/{body?.solverVelocityIterations} " +
                $"maxDepenetrationVelocity={body?.maxDepenetrationVelocity:0.0}mps " +
                "bounce=0.");
        }
    }

    private static void ConfigureBodyVisualOffset(GameObject root)
    {
        var offset = root.GetComponent<AudiR8BodyVisualOffset>() ??
                     root.AddComponent<AudiR8BodyVisualOffset>();
        offset.Initialize(BodyVisualDropMeters);
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
        var existingVisualDamage = vehicle.GetComponent<AudiR8VisualDamageController>();
        foreach (var component in vehicle.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || !string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
                continue;
            component.enabled = false;
            ClearCollection(component, "_deformationQueue");
            // Unity delivers OnCollisionEnter even to disabled behaviours. Its
            // mesh list must be empty as well, otherwise it keeps queuing work.
            if (existingVisualDamage == null && GetMember(component, "meshFilters") is MeshFilter[] legacyFilters &&
                GetMember(component, "originalMeshes") is Mesh[] originals)
                for (var index = 0; index < Mathf.Min(legacyFilters.Length, originals.Length); index++)
                    if (legacyFilters[index] != null && originals[index] != null)
                        legacyFilters[index].sharedMesh = originals[index];
            SetValue(component, "meshFilters", typeof(MeshFilter[]), Array.Empty<MeshFilter>());
            SetValue(component, "originalMeshes", typeof(Mesh[]), Array.Empty<Mesh>());
        }

        var damageHandler =
            vehicle.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true);
        if (damageHandler == null)
        {
            context?.Logger.Warn(
                $"AudiR8 damage vehicle={vehicle.GetInstanceID()}: " +
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
                $"AudiR8 damage vehicle={vehicle.GetInstanceID()}: " +
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

        // The native handler tests total relative speed, including road-parallel
        // travel. Own scoring from normal closing speed and real contact evidence.
        damageHandler.visualOnly = true;
        var impactDamage = vehicle.GetComponent<AudiR8ImpactDamageController>() ??
                           vehicle.gameObject.AddComponent<AudiR8ImpactDamageController>();
        impactDamage.Initialize(vehicle, damageHandler, context, DamageDecelerationThreshold / 100f);

        var visualDamage = vehicle.GetComponent<AudiR8VisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<AudiR8VisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        AudiR8Diagnostics.DamageInfo(
            context,
            $"AudiR8 damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps " +
            $"meshNames=[{string.Join(",", filters.Select(filter => filter.name))}]; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        if (filter.name.StartsWith("AudiR8DamageBody", StringComparison.Ordinal))
            return true;

        if (!HasAncestor(filter.transform, "AudiR8Visual"))
            return false;

        // Mixed Interior/Window meshes also contain exterior bumper/lamp faces.
        // The damage owner masks those by vehicle-local position; wheel/cabin
        // protection must not discard an entire mixed mesh here.
        if (HasAncestor(filter.transform, "AudiR8Wheel") ||
            HasAncestor(filter.transform, "AudiR8FixedCaliper") ||
            HasAncestor(filter.transform, "w:3DWheel") ||
            HasAncestor(filter.transform, "w:Calliper") ||
            HasAncestor(filter.transform, "SeatBelt") ||
            HasMaterial(filter, "SeatBelt",
                "wpolySurface442SG1", "wpolySurface443SG1", "wCalliperParking"))
            return false;

        return true;
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

    private static bool ConfigurePowertrain(GameObject root, ModContext? diagnosticContext)
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
            var inertiaConfigured = SetFloat(engine, "inertia", EngineInertia);
            var stallingConfigured = SetBool(engine, "stallingEnabled", EngineStallingEnabled);
            var throttleRestartConfigured =
                SetBool(engine, "autoStartOnThrottle", EngineAutoStartOnThrottle);
            SetFloat(engine, "maxPower", EnginePowerKw);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreatePowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", false);
            SetFloat(forcedInduction, "powerGainMultiplier", 1f);
            SetFloat(forcedInduction, "spoolUpTime", 0f);

            var transmission = GetMember(powertrain, "transmission");
            var brakesConfigured = SetNestedFloat(component, "brakes", "maxTorque", BrakeMaxTorque);
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.075f);
            // Audi's 1st/2nd ratio step is the largest: at 8,250 RPM it leaves
            // about 5,500 RPM after an upshift, so keep the downshift point below it.
            SetFloat(transmission, "_downshiftRPM", 5000f);
            SetFloat(transmission, "_upshiftRPM", 8250f);
            SetInt(transmission, "forwardGearCount", 7);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetBool(transmission, "isSequential", true);
            SetBool(transmission, "allowDownshiftGearSkipping", false);
            SetFloatArray(transmission, "gears", Gears);
            var forwardGearCount = GetInt(transmission, "forwardGearCount");
            var minimumPostShiftRpm = Enumerable.Range(1, Gears.Length - 3)
                .Min(gear => 8250f * Gears[gear + 2] / Gears[gear + 1]);
            var firstToSecondPostShiftRpm = 8250f * Gears[3] / Gears[2];
            var gearRatios = string.Join("|", Gears.Select(
                ratio => ratio.ToString("0.000", CultureInfo.InvariantCulture)));
            AudiR8Diagnostics.DrivetrainInfo(
                diagnosticContext,
                $"AudiR8 transmission vehicle={component.GetInstanceID()}: " +
                $"type=automatic-sequential gears={forwardGearCount} " +
                $"upshift=8250rpm downshift=5000rpm " +
                $"1to2PostShift={firstToSecondPostShiftRpm:0}rpm " +
                $"minimumPostShift={minimumPostShiftRpm:0}rpm " +
                $"brakeMaxTorque={BrakeMaxTorque:0} configured={brakesConfigured} " +
                $"ratios={gearRatios}");

            if (GetMember(powertrain, "wheelGroups") is IList wheelGroups)
            {
                foreach (var wheelGroup in wheelGroups)
                    SetFloat(wheelGroup, "antiRollBarForce", AntiRollBarForce);
            }

            var centerDifferentialConfigured = false;
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
                    // Audi quattro uses a rear-biased 40:60 front/rear split.
                    SetFloat(differential, "biasAB", 0.60f);
                    centerDifferentialConfigured = true;
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

            return GetInt(transmission, "forwardGearCount") == 7 &&
                   centerDifferentialConfigured &&
                   brakesConfigured &&
                   inertiaConfigured &&
                   stallingConfigured &&
                   throttleRestartConfigured;
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

    private static bool SetFloat(object? target, string name, float value) =>
        SetValue(target, name, typeof(float), value);

    private static bool SetNestedFloat(object? target, string parentName, string fieldName, float value)
    {
        var parent = GetMember(target, parentName);
        if (parent == null || !SetFloat(parent, fieldName, value))
            return false;
        return !parent.GetType().IsValueType ||
               SetValue(target, parentName, parent.GetType(), parent);
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

    private static bool SetBool(object? target, string name, bool value) =>
        SetValue(target, name, typeof(bool), value);

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

    private bool ConfigureSleepEnvironment(VehicleController target)
    {
        try
        {
            var environmentField = FindField(typeof(VehicleController), "sleepEnvironment");
            var environment = environmentField?.GetValue(target);
            if (environmentField == null || environment == null)
                throw new InvalidOperationException("Vehicle sleep-environment field is unavailable.");

            var configField = FindField(environment.GetType(), "config");
            if (configField == null)
                throw new InvalidOperationException("Vehicle sleep configuration field is unavailable.");

            if (configField.GetValue(environment) is UnityEngine.Object currentConfig &&
                currentConfig != null && IsCarSleepConfig(currentConfig))
                return true;

            UnityEngine.Object? carConfig = FindLoadedVehicleCarSleepConfig(
                target,
                environmentField,
                configField);
            if (carConfig == null)
            {
                foreach (var candidate in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
                {
                    if (candidate == null ||
                        candidate.GetType().FullName != "PlayerActivity.SleepEnvironmentConfig" ||
                        !IsCarSleepConfig(candidate))
                        continue;

                    carConfig = candidate;
                    break;
                }
            }

            if (carConfig == null)
            {
                fallbackCarSleepConfig ??= CreateFallbackCarSleepConfig(configField.FieldType);
                carConfig = fallbackCarSleepConfig;
            }

            if (carConfig == null)
                throw new InvalidOperationException("The native Car sleep configuration could not be resolved.");

            configField.SetValue(environment, carConfig);
            environmentField.SetValue(target, environment);
            AudiR8Diagnostics.Info(
                context,
                $"AudiR8 sleep vehicle={target.GetInstanceID()}: assigned native Car sleep configuration.");
            return true;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"AudiR8 sleep vehicle={target.GetInstanceID()}: " +
                $"could not assign native Car sleep configuration: {exception.GetBaseException().Message}");
            return false;
        }
    }

    private static UnityEngine.Object? FindLoadedVehicleCarSleepConfig(
        VehicleController target,
        FieldInfo environmentField,
        FieldInfo configField)
    {
        foreach (var otherVehicle in Resources.FindObjectsOfTypeAll<VehicleController>())
        {
            if (otherVehicle == null || otherVehicle == target)
                continue;

            var otherEnvironment = environmentField.GetValue(otherVehicle);
            if (otherEnvironment == null)
                continue;

            var candidate = configField.GetValue(otherEnvironment) as UnityEngine.Object;
            if (candidate != null && IsCarSleepConfig(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsCarSleepConfig(UnityEngine.Object candidate)
    {
        var typeValue = FindField(candidate.GetType(), "sleepEnvironmentType")?.GetValue(candidate);
        return typeValue != null && Convert.ToInt32(typeValue) == 1;
    }

    private static UnityEngine.Object? CreateFallbackCarSleepConfig(Type configType)
    {
        if (!typeof(ScriptableObject).IsAssignableFrom(configType))
            return null;

        var config = ScriptableObject.CreateInstance(configType);
        config.name = "AudiR8 Runtime Car Sleep Config";
        config.hideFlags = HideFlags.HideAndDontSave;
        SetEnumField(config, "sleepEnvironmentType", 1);
        SetEnumField(config, "energyRegen", 3);

        var balanceConfigField = FindField(configType, "balanceConfig");
        if (balanceConfigField == null ||
            !typeof(ScriptableObject).IsAssignableFrom(balanceConfigField.FieldType))
        {
            Destroy(config);
            return null;
        }

        var balance = ScriptableObject.CreateInstance(balanceConfigField.FieldType);
        balance.name = "AudiR8 Runtime Car Sleep Balance";
        balance.hideFlags = HideFlags.HideAndDontSave;
        SetStringField(balance, "displayName", "Car");
        SetEnumField(balance, "source", 0);
        SetIntField(balance, "defaultDurationMinutes", 480);
        SetIntField(balance, "minDurationMinutes", 60);
        SetIntField(balance, "maxDurationMinutes", 1440);
        balanceConfigField.SetValue(config, balance);

        var luxuryBalanceField = FindField(configType, "luxuryOverrideBalanceConfig");
        luxuryBalanceField?.SetValue(config, balance);
        return config;
    }

    private static void SetEnumField(object target, string fieldName, int value)
    {
        var field = FindField(target.GetType(), fieldName);
        if (field?.FieldType.IsEnum == true)
            field.SetValue(target, Enum.ToObject(field.FieldType, value));
    }

    private static void SetIntField(object target, string fieldName, int value)
    {
        var field = FindField(target.GetType(), fieldName);
        if (field?.FieldType == typeof(int))
            field.SetValue(target, value);
    }

    private static void SetStringField(object target, string fieldName, string value)
    {
        var field = FindField(target.GetType(), fieldName);
        if (field?.FieldType == typeof(string))
            field.SetValue(target, value);
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
public sealed class AudiR8WheelGeometryController : MonoBehaviour
{
    private const float WheelAssemblyInsetMeters = 0.085f;
    private static readonly string[,] CornerNames =
    {
        { "FrontLeft_WheelController", "AudiR8WheelFrontLeft", "AudiR8FixedCaliperFrontLeft" },
        { "FrontRight_WheelController", "AudiR8WheelFrontRight", "AudiR8FixedCaliperFrontRight" },
        { "RearLeft_WheelController", "AudiR8WheelRearLeft", "AudiR8FixedCaliperRearLeft" },
        { "RearRight_WheelController", "AudiR8WheelRearRight", "AudiR8FixedCaliperRearRight" },
    };
    private readonly List<WheelVisualBinding> bindings = new List<WheelVisualBinding>(4);
    private bool initialized;

    internal int Initialize(ModContext? context, float chassisAndTireDrop)
    {
        if (initialized)
            return CornerNames.GetLength(0);

        initialized = true;
        bindings.Clear();
        var visual = FindTransform("AudiR8Visual");
        if (visual != null)
            visual.position -= transform.up * chassisAndTireDrop;
        var damageBody = FindTransform("AudiR8DamageBody");
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
                    $"AudiR8 wheel geometry vehicle={GetInstanceID()} corner={index} " +
                    $"could not be inset: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (correctedCorners == CornerNames.GetLength(0))
        {
            AudiR8Diagnostics.Info(
                context,
                $"AudiR8 wheel geometry vehicle={GetInstanceID()}: moved " +
                $"{correctedCorners} complete wheel/controller/caliper assemblies inward by " +
                $"{WheelAssemblyInsetMeters:F3}m and lowered the complete chassis/wheel datum by " +
                $"{chassisAndTireDrop:F3}m; rolling assemblies bound to steering controllers.");
        }
        else
        {
            context?.Logger.Warn(
                $"AudiR8 wheel geometry vehicle={GetInstanceID()}: corrected " +
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
                        "wpolySurface442SG1",
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

public sealed class AudiR8CollisionSeparationController : MonoBehaviour
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
internal sealed class AudiR8HighwaySeamGuard : MonoBehaviour
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
internal sealed class AudiR8ContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        // Low friction lets rigid vehicle bodies separate naturally after a
        // crash instead of locking together.
        contactMaterial = new PhysicMaterial("Audi R8 body contact")
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
public sealed class AudiR8GlassController : MonoBehaviour
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
                    AudiR8Materials.GetTransparentRole(renderer, source) !=
                    AudiR8TransparentRole.CabinGlass)
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    AudiR8Materials.RestoreCabinGlassMaterial(runtimeMaterial);
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
                    AudiR8Materials.GetTransparentRole(renderer, material) ==
                    AudiR8TransparentRole.CabinGlass)
                {
                    renderer.SetPropertyBlock(null, index);
                    AudiR8Materials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            var materialState = "unavailable";
            foreach (var renderer in cabinGlass)
            {
                if (renderer == null)
                    continue;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null ||
                        AudiR8Materials.GetTransparentRole(renderer, material) !=
                        AudiR8TransparentRole.CabinGlass)
                        continue;
                    materialState = AudiR8Materials.DescribeSurfaceState(material);
                    break;
                }
                if (!string.Equals(materialState, "unavailable", StringComparison.Ordinal))
                    break;
            }
            AudiR8Diagnostics.Info(
                context,
                $"AudiR8 glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
            AudiR8Diagnostics.AppearanceInfo(
                context,
                $"AudiR8 appearance vehicle={GetInstanceID()}: cabin glass {materialState}");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            AudiR8Diagnostics.Info(
                context,
                $"AudiR8 glass vehicle={GetInstanceID()}: repaired after " +
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
internal sealed class AudiR8BodyVisualOffset : MonoBehaviour
{
    private bool initialized;

    internal void Initialize(float dropMeters)
    {
        if (initialized || dropMeters <= 0f)
            return;

        var visual = FindTarget("AudiR8Visual");
        if (visual == null)
            return;

        visual.localPosition -= Vector3.up * dropMeters;
        var damageBody = FindTarget("AudiR8DamageBody");
        if (damageBody != null)
            damageBody.localPosition -= Vector3.up * dropMeters;
        var steeringWheelAnchor = FindTarget("AudiR8SteeringWheelAnchor");
        if (steeringWheelAnchor != null)
            steeringWheelAnchor.localPosition -= Vector3.up * dropMeters;
        initialized = true;
    }

    private Transform? FindTarget(string targetName)
    {
        foreach (var child in GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, targetName, StringComparison.Ordinal))
                return child;
        return null;
    }
}


