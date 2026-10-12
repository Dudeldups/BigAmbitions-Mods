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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

public sealed class VolkswagenT1BusRuntime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1150f;
    // Effective output in the game powertrain. The 8% reserve over the factory
    // rating compensates for the measured high-speed shortfall in the simulation.
    private const float EngineSimulationPowerKw = 35.0f;
    private const float EngineLossPercent = 0f;
    private const float BrakeMaxTorque = 875f;
    private const float EngineIdleRpm = 850f;
    private const float EngineLimitRpm = 4300f;
    private const float SpeedLimitKph = 105f;
    // Simulate the T1's 4.375 ring-and-pinion and 1.389 wheel reducers together.
    private const float FinalDriveRatio = 6.0764f;
    private const float EngineInertia = 0.32f;
    private const float EngineStartDuration = 0.50f;
    private const float ClutchEngagementRpm = 950f;
    private const float ClutchThrottleOffsetRpm = 125f;
    private const float ClutchEngagementRange = 220f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 0.90f;
    private const float AntiRollBarForce = 1800f;
    private const float FrontSuspensionTravel = 0.120f;
    private const float RearSuspensionTravel = 0.140f;
    private const float FrontTireRadius = 0.35f;
    private const float RearTireRadius = 0.35f;
    private const float FrontTireWidth = 0.18f;
    private const float RearTireWidth = 0.18f;
    private const float VehicleLinearDrag = 0f;
    private const float FrontForwardGrip = 0.92f;
    private const float RearForwardGrip = 0.90f;
    private const float FrontForwardStiffness = 0.95f;
    private const float RearForwardStiffness = 0.94f;
    private const float DeformationStrength = 0.14f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 0.84f;
    private const float DamageDecelerationThreshold = 300f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private const float WarehouseExitGuardDuration = 8f;
    private const float WarehouseExitGuardClearDistance = 4f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.27f, -0.26f);
    private const float FrontSidewaysGrip = 0.85f;
    private const float RearSidewaysGrip = 0.90f;
    private const float SidewaysStiffness = 0.95f;
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.41f, 1.78f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.64f, 0.68f, 0.82f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.41f, -1.78f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.64f, 0.68f, 0.82f);
    private static readonly Vector3 DriverExitPosition =
        new Vector3(-0.90f, 0.19f, -0.08f);
    private static readonly Vector3 PassengerExitPosition =
        new Vector3(0.90f, 0.19f, -0.08f);

    private static readonly float[] T1BusGears =
    {
        -3.80f, 0f, 3.80f, 2.06f, 1.26f, 0.82f,
    };

    private static AnimationCurve CreateT1BusPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.20f, 0.18f),
            new Keyframe(0.33f, 0.40f), new Keyframe(0.465f, 0.66f),
            new Keyframe(0.60f, 0.79f), new Keyframe(0.78f, 0.90f),
            new Keyframe(0.93f, 1.00f),
            new Keyframe(1.00f, 0.94f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private int enteredVehicleActivationInstanceId;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private VolkswagenT1BusWarehouseEntryController? warehouseExitGuardEntryController;
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

    public static VolkswagenT1BusRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<VolkswagenT1BusRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(VolkswagenT1BusRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<VolkswagenT1BusRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        VolkswagenT1BusPrivateDriverSupport.SetContext(context);
        var trafficFrequency = runtime.GetComponent<VolkswagenT1BusTrafficFrequency>() ??
                               runtime.gameObject.AddComponent<VolkswagenT1BusTrafficFrequency>();
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
        VolkswagenT1BusPrivateDriverSupport.RemoveVehicle(vehicleTypeName);
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

        VolkswagenT1BusDiagnostics.DealerEntryInfo(
            context,
            $"VolkswagenT1Bus dealer-entry: game-event='{eventName}', " +
            DescribeVehicleState(selectedVehicle));

        // Dealer purchases do not reliably invoke onEnterVehicle. This event
        // is a bounded lifecycle handoff from the dealer display vehicle to
        // the owned, player-controlled instance, so configure it here rather
        // than scanning vehicles every frame.
        TryConfigureVehicle(selectedVehicle);
        selectedVehicle!
            .GetComponent<VolkswagenT1BusPaintController>()
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
        if (VolkswagenT1BusLoadRecovery.CompleteInterruptedLoad(context))
            StartCoroutine(ReportLoadedInputState());
        privateDriverRegistrationAllowed = true;
        ScheduleInitialization("game-loaded-late");
    }

    private IEnumerator ReportLoadedInputState()
    {
        // The native loading screen fades out for 0.8 seconds after this event.
        yield return new WaitForSecondsRealtime(2f);
        VolkswagenT1BusLoadRecovery.ReportInputState(context);
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
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        VolkswagenT1BusDiagnostics.DealerEntryInfo(
            context,
            $"VolkswagenT1Bus dealer-entry: onEnterVehicle, {DescribeVehicleState(vehicle)}");
        TryConfigureVehicle(vehicle);
        vehicle.GetComponent<VolkswagenT1BusGlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle == null || !IsTargetVehicle(vehicle))
            return;
        vehicle.GetComponent<VolkswagenT1BusPaintController>()
            ?.ApplyCurrentColor("vehicle-entered");
        ScheduleEnteredVehicleActivation(vehicle);
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;
        VolkswagenT1BusDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenT1Bus warehouse-exit: onExitVehicle, {DescribeVehicleState(vehicle)}");
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
        VolkswagenT1BusDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenT1Bus player-exit: deferred validation playerPosition={root.position}, " +
            $"agentCount={agents.Length}, anyAgentOffNavMesh={agents.Any(agent => agent != null && agent.enabled && !agent.isOnNavMesh)}, " +
            $"initialClear={initialExitClear}, needsRecovery={needsRecovery}.");
        if (!needsRecovery)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }
        if (!TryFindClearExitPosition(root, exitedVehicle, out var target))
        {
            VolkswagenT1BusDiagnostics.WarehouseExitInfo(
                context,
                "VolkswagenT1Bus player-exit: recovery failed; no NavMesh/capsule-clear candidate found.");
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
        VolkswagenT1BusDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenT1Bus player-exit: recovery moved player to {target}.");
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
            VolkswagenT1BusDiagnostics.DealerEntryInfo(
                context,
                $"VolkswagenT1Bus dealer-entry: activation skipped because the vehicle is " +
                $"neither controlled nor selected, {DescribeVehicleState(vehicle)}");
            return;
        }

        var runtimeBody = vehicle.GetComponent<Rigidbody>() ??
                          vehicle.GetComponentInParent<Rigidbody>();
        if (runtimeBody != null)
            runtimeBody.drag = VehicleLinearDrag;

        var instanceId = vehicle.GetInstanceID();
        if (enteredVehicleActivationCoroutine != null &&
            enteredVehicleActivationInstanceId == instanceId)
        {
            VolkswagenT1BusDiagnostics.DealerEntryInfo(
                context,
                $"VolkswagenT1Bus dealer-entry: activation already pending instance={instanceId}.");
            return;
        }

        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);

        enteredVehicleActivationInstanceId = instanceId;
        VolkswagenT1BusDiagnostics.DealerEntryInfo(
            context,
            $"VolkswagenT1Bus dealer-entry: activation scheduled, {DescribeVehicleState(vehicle)}");
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
            VolkswagenT1BusDiagnostics.DealerEntryInfo(
                context,
                $"VolkswagenT1Bus dealer-entry: activation failed; NWH controller missing, " +
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
                VolkswagenT1BusDiagnostics.DealerEntryInfo(
                    context,
                    $"VolkswagenT1Bus dealer-entry: activation cancelled attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }

            // Paint assignment from the dealer is asynchronous too; retain a
            // bounded entry refresh without turning it into runtime polling.
            vehicle.GetComponent<VolkswagenT1BusPaintController>()
                ?.ApplyCurrentColor("vehicle-entered");
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            VolkswagenT1BusDiagnostics.DealerEntryInfo(
                context,
                $"VolkswagenT1Bus dealer-entry: activation attempt={attempt + 1}, " +
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
                    VolkswagenT1BusDiagnostics.DealerEntryInfo(
                        context,
                        "VolkswagenT1Bus dealer-entry: native engine healthy; shifted neutral to first.");
                    yield return new WaitForFixedUpdate();
                    VolkswagenT1BusDiagnostics.DealerEntryInfo(
                        context,
                        $"VolkswagenT1Bus dealer-entry: first-gear settle, gear={transmission.Gear}, " +
                        $"ratio={transmission.currentGearRatio:0.000}.");
                }
                else
                {
                    VolkswagenT1BusDiagnostics.DealerEntryInfo(
                        context,
                        $"VolkswagenT1Bus dealer-entry: native engine healthy; preserved gear={transmission.Gear}.");
                }
                break;
            }

            VolkswagenT1BusDiagnostics.DealerEntryInfo(
                context,
                $"VolkswagenT1Bus dealer-entry: dormant powertrain; restart attempt={attempt + 1}.");
            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
            {
                VolkswagenT1BusDiagnostics.DealerEntryInfo(
                    context,
                    $"VolkswagenT1Bus dealer-entry: restart aborted after stop attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }
            engine.StartEngine();
            yield return new WaitForSecondsRealtime(.75f);
            if (vehicle != null && vehicle.controlledByPlayer &&
                transmission.Gear == 0)
            {
                transmission.ShiftInto(1, true);
                VolkswagenT1BusDiagnostics.DealerEntryInfo(
                    context,
                    $"VolkswagenT1Bus dealer-entry: restart attempt={attempt + 1} shifted neutral to first.");
            }
            yield return new WaitForSecondsRealtime(.15f);
        }

        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        VolkswagenT1BusDiagnostics.DealerEntryInfo(
            context,
            $"VolkswagenT1Bus dealer-entry: activation completed, {DescribeVehicleState(vehicle)}");
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!VolkswagenT1BusCityCarsDealerStock.IsTargetDealer(registration?.BusinessName) ||
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
            VolkswagenT1BusDiagnostics.WarehouseExitInfo(
                context,
                $"VolkswagenT1Bus warehouse-exit: guard skipped; current vehicle is not " +
                $"a controlled T1Bus ({DescribeVehicleState(vehicle)}).");
            return;
        }

        var entrance = FindClosestDriveInEntrance(vehicle.transform.position);
        if (entrance == null)
        {
            VolkswagenT1BusDiagnostics.WarehouseExitInfo(
                context,
                $"VolkswagenT1Bus warehouse-exit: guard skipped; no DriveInEntrance found, " +
                DescribeVehicleState(vehicle));
            return;
        }

        var nativeMeshCollider = vehicle.GetComponentInChildren<MeshCollider>(true);
        VolkswagenT1BusDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenT1Bus warehouse-exit: native exit completed, {DescribeVehicleState(vehicle)} " +
            $"entrance='{entrance.name}' entrancePosition={entrance.transform.position}, " +
            $"nativeMesh='{nativeMeshCollider?.name ?? "missing"}', " +
            $"nativeMeshLength={nativeMeshCollider?.sharedMesh?.bounds.size.z:0.000}.");

        StopWarehouseExitGuard();
        warehouseExitGuardEntryController =
            vehicle.GetComponent<VolkswagenT1BusWarehouseEntryController>();
        warehouseExitGuardEntryController?.SuppressEntrance(entrance, "warehouse-exit-guard");
        foreach (var enterTrigger in entrance.GetComponentsInChildren<DriveInEntranceEnterTrigger>(true))
        foreach (var collider in enterTrigger.GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled || !collider.isTrigger)
                continue;

            warehouseExitGuardColliders.Add(collider);
            VolkswagenT1BusDiagnostics.WarehouseExitInfo(
                context,
                $"VolkswagenT1Bus warehouse-exit: disabling entry trigger " +
                $"'{collider.name}' bounds={collider.bounds}.");
            collider.enabled = false;
        }

        if (warehouseExitGuardColliders.Count == 0)
        {
            warehouseExitGuardEntryController?.ClearSuppressedEntrance(
                entrance,
                "no-native-entry-trigger");
            warehouseExitGuardEntryController = null;
            VolkswagenT1BusDiagnostics.WarehouseExitInfo(
                context,
                "VolkswagenT1Bus warehouse-exit: guard skipped; matching entrance had no enabled trigger colliders.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(
            vehicle.transform.position - entrance.transform.position,
            Vector3.up);
        if (outward.sqrMagnitude < .0001f)
            outward = Vector3.ProjectOnPlane(entrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < .0001f)
        {
            VolkswagenT1BusDiagnostics.WarehouseExitInfo(
                context,
                "VolkswagenT1Bus warehouse-exit: guard aborted; outward direction was zero.");
            StopWarehouseExitGuard();
            return;
        }

        outward.Normalize();
        var startingDistance = Vector3.Dot(vehicle.transform.position, outward);
        Physics.SyncTransforms();
        VolkswagenT1BusDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenT1Bus warehouse-exit: guard started triggerCount={warehouseExitGuardColliders.Count}, " +
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
        VolkswagenT1BusDiagnostics.WarehouseExitInfo(
            context,
            $"VolkswagenT1Bus warehouse-exit: guard ending reason={reason}, " +
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
            VolkswagenT1BusDiagnostics.WarehouseExitInfo(
                context,
                $"VolkswagenT1Bus warehouse-exit: restoring triggerCount={warehouseExitGuardColliders.Count}, " +
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
                $"VolkswagenT1Bus: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
        if (privateDriverRegistrationAllowed && !privateDriverReady)
        {
            context?.Logger.Warn(
                $"VolkswagenT1Bus: private-driver support not ready source='{source}'.");
        }
        if (!privateDriverPoolReady)
        {
            context?.Logger.Warn(
                $"VolkswagenT1Bus: private-driver traffic pool not ready source='{source}'.");
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
            var ready = VolkswagenT1BusCityCarsDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerRegistrationReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                VolkswagenT1BusDiagnostics.Info(
                    context,
                    $"VolkswagenT1Bus: available at City Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"VolkswagenT1Bus: dealer stock update failed source='{source}': " +
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
                privateDriverReady = VolkswagenT1BusPrivateDriverSupport.EnsureVehicleAvailable(
                    vehicleTypeName);
            }
            if (privateDriverReady)
            {
                VolkswagenT1BusDiagnostics.Info(
                    context,
                    $"VolkswagenT1Bus: private-driver support registered source='{source}'.");
            }
            return privateDriverReady && privateDriverPoolReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"VolkswagenT1Bus: private-driver registration failed source='{source}': " +
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
            return VolkswagenT1BusPrivateDriverSupport.PrepareTrafficPool(
                playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                context?.Logger.Warn(
                    $"VolkswagenT1Bus: private-driver pool preparation failed source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
                privateDriverPreparationExceptionLogged = true;
            }

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
            vehicle.GetComponent<VolkswagenT1BusPaintController>()
                ?.ApplyCurrentColor("vehicle-variables-changed");
            return;
        }

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
                    vehicle.GetComponent<VolkswagenT1BusHighwaySeamGuard>();
                if (highwaySeamGuard == null)
                {
                    highwaySeamGuard = vehicle.gameObject
                        .AddComponent<VolkswagenT1BusHighwaySeamGuard>();
                }
                highwaySeamGuard.Initialize(rigidbody);
            }

            ConfigureMassProperties(vehicle.gameObject);
            if (rigidbody != null)
            {
                var aerodynamics = vehicle.GetComponent<VolkswagenT1BusAerodynamicsController>();
                if (aerodynamics == null)
                    aerodynamics = vehicle.gameObject
                        .AddComponent<VolkswagenT1BusAerodynamicsController>();
                aerodynamics.Initialize(rigidbody, context);
            }
            ConfigureWheelControllers(vehicle.gameObject);
            var contactMaterialOwner =
                vehicle.GetComponent<VolkswagenT1BusContactMaterialOwner>();
            if (contactMaterialOwner == null)
            {
                contactMaterialOwner = vehicle.gameObject
                    .AddComponent<VolkswagenT1BusContactMaterialOwner>();
            }
            ConfigureBodyColliders(
                vehicle.gameObject,
                contactMaterialOwner.GetOrCreateMaterial());
            ConfigureExitMarkers(vehicle.gameObject);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(vehicle.gameObject);
            var warehouseBounds =
                vehicle.GetComponent<VolkswagenT1BusWarehouseBoundsController>();
            if (warehouseBounds == null)
            {
                warehouseBounds = vehicle.gameObject
                    .AddComponent<VolkswagenT1BusWarehouseBoundsController>();
            }
            warehouseBounds.Initialize();
            var warehouseEntry = vehicle.GetComponent<VolkswagenT1BusWarehouseEntryController>();
            if (warehouseEntry == null)
            {
                warehouseEntry = vehicle.gameObject
                    .AddComponent<VolkswagenT1BusWarehouseEntryController>();
            }
            warehouseEntry.Initialize(vehicle, context);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var caliperController = vehicle.GetComponent<VolkswagenT1BusCaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<VolkswagenT1BusCaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialController = vehicle.GetComponent<VolkswagenT1BusMaterialController>();
            if (materialController == null)
                materialController = vehicle.gameObject.AddComponent<VolkswagenT1BusMaterialController>();
            var materialResult = materialController.Initialize(context);
            var glassController = vehicle.GetComponent<VolkswagenT1BusGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<VolkswagenT1BusGlassController>();
            glassController.Initialize(context);
            var paintController = vehicle.GetComponent<VolkswagenT1BusPaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<VolkswagenT1BusPaintController>();
            paintController.Initialize(vehicle, context);
            var lightingController = vehicle.GetComponent<VolkswagenT1BusLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<VolkswagenT1BusLightingController>();
            lightingController.Initialize(vehicle, context);
            // Lighting overlays are spawned per vehicle. Build the deformation
            // allowlist only after they exist so illuminated lamp surfaces move
            // with the surrounding lamp housings after an impact.
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            VolkswagenT1BusWashBindings.Configure(vehicle, context);
            var driverController = vehicle.GetComponent<VolkswagenT1BusDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<VolkswagenT1BusDriverController>();
            driverController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<VolkswagenT1BusAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<VolkswagenT1BusAudioController>();
            audioController.Initialize(vehicle, context);
            VolkswagenT1BusDiagnostics.Info(
                context,
                $"VolkswagenT1Bus: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=4-speed-manual, rearDrive=true, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"lateralGrip={FrontSidewaysGrip:0.00}/{RearSidewaysGrip:0.00}, " +
                $"lateralStiffness={SidewaysStiffness:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstaclesNormalized={normalizedNavMeshObstacles}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=1500cc-air-cooled-flat-four-profile, simulationPower={EngineSimulationPowerKw:0.0}kW, " +
                $"factoryRatedPower=32.3kW/44PS, engineLoss={EngineLossPercent:0.00}, " +
                $"forcedInduction=false, drumBrakes=true, brakeTorque={BrakeMaxTorque:0}Nm, " +
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
                $"VolkswagenT1Bus: vehicle configuration failed instance={instanceId}: " +
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
                SetFloat(spring, "maxForce", 24000f);

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
                var sideFriction = GetMember(component, "sideFriction");
                if (sideFriction != null)
                {
                    SetFloat(
                        sideFriction,
                        "grip",
                        isFront ? FrontSidewaysGrip : RearSidewaysGrip);
                    SetFloat(sideFriction, "stiffness", SidewaysStiffness);
                    SetValue(
                        component,
                        "sideFriction",
                        sideFriction.GetType(),
                        sideFriction);
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
                colliders[0].center = new Vector3(0f, 0.37f, 0f);
                colliders[0].size = new Vector3(1.68f, 0.52f, 4.05f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 1.01f, -0.05f);
                colliders[1].size = new Vector3(1.60f, 1.10f, 3.15f);
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
                $"VolkswagenT1Bus damage vehicle={vehicle.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        var filters = new List<MeshFilter>();
        foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null || !IsDeformableExterior(filter))
                continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            var statefulLampOverlay = IsStatefulLampSurface(filter);
            if (renderer != null && (renderer.enabled || statefulLampOverlay))
                filters.Add(filter);
        }

        if (filters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"VolkswagenT1Bus damage vehicle={vehicle.GetInstanceID()}: " +
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

        var visualDamage = vehicle.GetComponent<VolkswagenT1BusVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<VolkswagenT1BusVisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        VolkswagenT1BusDiagnostics.DamageInfo(
            context,
            $"VolkswagenT1Bus damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        // Authored lamp surfaces live under T1BusLightSources, which is normally
        // excluded from crash deformation. Allow every visible/stateful lamp mesh
        // before that broad exclusion so illuminated geometry cannot remain
        // floating at its undamaged position after surrounding bodywork dents.
        if (IsStatefulLampSurface(filter))
            return true;

        if (filter.name.StartsWith("T1BusDamageBody", StringComparison.Ordinal))
            return true;
        if (HasAncestor(filter.transform, "T1BusRearGlass") ||
            HasAncestor(filter.transform, "T1BusWheel") ||
            HasAncestor(filter.transform, "T1BusFixedCaliper") ||
            HasAncestor(filter.transform, "T1BusLightSources") ||
            HasAncestor(filter.transform, "interior") ||
            HasAncestor(filter.transform, "steering wheel") ||
            HasMaterial(filter, "glass", "carpet", "fabric", "leather", "seatbelt", "gauges"))
            return false;

        // Paint panels are baked into the shared damage shell. Other visible
        // opaque exterior meshes (badge, chrome, bumpers and lamp housings) keep
        // their own renderer and are deformed alongside it.
        var renderer = filter.GetComponent<MeshRenderer>();
        return HasAncestor(filter.transform, "T1BusVisual") &&
               renderer != null && renderer.enabled && renderer.sharedMaterials.Length > 0;
    }

    private static bool IsStatefulLampSurface(MeshFilter filter)
    {
        var name = filter.name;
        return name.IndexOf("BHeadlights", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("1RearDrivingLights", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("1BrakeLights", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.StartsWith("VolkswagenT1Bus_Headlights", StringComparison.Ordinal) ||
               name.StartsWith("VolkswagenT1Bus_RearLampLeft", StringComparison.Ordinal) ||
               name.StartsWith("VolkswagenT1Bus_RearLampRight", StringComparison.Ordinal);
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
            if (component == null || !string.Equals(
                    component.GetType().FullName, "NWH.VehiclePhysics2.VehicleController",
                    StringComparison.Ordinal))
                continue;

            var powertrain = GetMember(component, "powertrain");
            var clutch = GetMember(powertrain, "clutch");
            SetFloat(clutch, "engagementRPM", ClutchEngagementRpm);
            SetFloat(clutch, "throttleEngagementOffsetRPM", ClutchThrottleOffsetRpm);
            SetFloat(clutch, "engagementRange", ClutchEngagementRange);
            SetFloat(clutch, "creepTorque", ClutchCreepTorque);
            SetFloat(clutch, "creepSpeedLimit", 1f);

            var engine = GetMember(powertrain, "engine");
            SetFloat(engine, "inertia", EngineInertia);
            SetFloat(engine, "maxPower", EngineSimulationPowerKw);
            SetFloat(engine, "engineLossPercent", EngineLossPercent);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateT1BusPowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", false);
            SetFloat(forcedInduction, "boost", 0f);
            SetFloat(forcedInduction, "powerGainMultiplier", 1f);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.28f);
            SetFloat(transmission, "_downshiftRPM", 2100f);
            SetFloat(transmission, "_upshiftRPM", 3800f);
            SetInt(transmission, "forwardGearCount", 4);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", T1BusGears);

            var brakes = GetMember(component, "brakes");
            if (!SetValue(brakes, "maxTorque", typeof(float), BrakeMaxTorque))
                return false;

            if (GetMember(powertrain, "wheelGroups") is System.Collections.IList wheelGroups)
                foreach (var wheelGroup in wheelGroups)
                    SetFloat(wheelGroup, "antiRollBarForce", AntiRollBarForce);

            var rearDrive = false;
            if (GetMember(powertrain, "differentials") is System.Collections.IList differentials)
            {
                foreach (var differential in differentials)
                {
                    if (!string.Equals(GetMember(differential, "name") as string,
                            "Center Differential", StringComparison.Ordinal))
                        continue;
                    // B is the rear differential on the reference powertrain.
                    SetFloat(differential, "biasAB", 1f);
                    rearDrive = true;
                }
            }

            foreach (var other in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (other != null && string.Equals(other.GetType().Name,
                        "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
                    SetFloat(GetMember(other, "module"), "speedLimit", SpeedLimitKph);

            var configuredBrakeTorque = GetMember(brakes, "maxTorque");
            var reducedBrakeTorque = configuredBrakeTorque != null &&
                                     Math.Abs(Convert.ToSingle(configuredBrakeTorque) - BrakeMaxTorque) < 0.1f;
            return GetInt(transmission, "forwardGearCount") == 4 && rearDrive && reducedBrakeTorque;
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
public sealed class VolkswagenT1BusWheelGeometryController : MonoBehaviour
{
    private const float WheelAssemblyInsetMeters = 0.085f;
    private static readonly string[,] CornerNames =
    {
        { "FrontLeft_WheelController", "T1BusWheelFrontLeft", "T1BusFixedCaliperFrontLeft" },
        { "FrontRight_WheelController", "T1BusWheelFrontRight", "T1BusFixedCaliperFrontRight" },
        { "RearLeft_WheelController", "T1BusWheelRearLeft", "T1BusFixedCaliperRearLeft" },
        { "RearRight_WheelController", "T1BusWheelRearRight", "T1BusFixedCaliperRearRight" },
    };
    private readonly List<WheelVisualBinding> bindings = new List<WheelVisualBinding>(4);
    private bool initialized;

    internal int Initialize(ModContext? context, float chassisAndTireDrop)
    {
        if (initialized)
            return CornerNames.GetLength(0);

        initialized = true;
        bindings.Clear();
        var visual = FindTransform("T1BusVisual");
        if (visual != null)
            visual.position -= transform.up * chassisAndTireDrop;
        var damageBody = FindTransform("T1BusDamageBody");
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
                    $"VolkswagenT1Bus wheel geometry vehicle={GetInstanceID()} corner={index} " +
                    $"could not be inset: {exception.GetType().Name}: {exception.Message}");
            }
        }

        if (correctedCorners == CornerNames.GetLength(0))
        {
            VolkswagenT1BusDiagnostics.Info(
                context,
                $"VolkswagenT1Bus wheel geometry vehicle={GetInstanceID()}: moved " +
                $"{correctedCorners} complete wheel/controller/caliper assemblies inward by " +
                $"{WheelAssemblyInsetMeters:F3}m and lowered the complete chassis/wheel datum by " +
                $"{chassisAndTireDrop:F3}m; rolling assemblies bound to steering controllers.");
        }
        else
        {
            context?.Logger.Warn(
                $"VolkswagenT1Bus wheel geometry vehicle={GetInstanceID()}: corrected " +
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
                        "wheels_chrome_1",
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

public sealed class VolkswagenT1BusCollisionSeparationController : MonoBehaviour
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
internal sealed class VolkswagenT1BusHighwaySeamGuard : MonoBehaviour
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
internal sealed class VolkswagenT1BusContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        // Match the proven Revuelto body contact: low friction lets the rigid
        // bodies separate naturally after a crash instead of locking together.
        contactMaterial = new PhysicMaterial("Volkswagen T1 Bus body contact")
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
public sealed class VolkswagenT1BusGlassController : MonoBehaviour
{
    private readonly List<Renderer> cabinGlass = new List<Renderer>();
    private readonly List<GameObject> generatedPaneObjects = new List<GameObject>();
    private readonly List<Mesh> generatedPaneMeshes = new List<Mesh>();
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
            var allMaterialsAreCabinGlass = materials.Length > 0;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                {
                    allMaterialsAreCabinGlass = false;
                    continue;
                }

                var role = VolkswagenT1BusMaterials.GetTransparentRole(renderer, source);
                if (role != VolkswagenT1BusTransparentRole.CabinGlass)
                {
                    allMaterialsAreCabinGlass = false;
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    VolkswagenT1BusMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
                    runtimeMaterials.Add(source, runtimeMaterial);
                }
                materials[index] = runtimeMaterial;
            }
            if (!containsCabinGlass)
                continue;

            if (allMaterialsAreCabinGlass && renderer is MeshRenderer meshRenderer &&
                IsCombinedBodyGlass(renderer))
            {
                var separatedPanes = SplitDisconnectedPanels(meshRenderer, materials);
                if (separatedPanes.Count > 1)
                {
                    renderer.enabled = false;
                    renderer.forceRenderingOff = true;
                    cabinGlass.AddRange(separatedPanes);
                    VolkswagenT1BusDiagnostics.Info(
                        context,
                        $"VolkswagenT1Bus glass vehicle={GetInstanceID()}: separated " +
                        $"original body-glass mesh='{renderer.name}' into " +
                        $"{separatedPanes.Count} independent source panels.");
                    continue;
                }
            }

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
                    VolkswagenT1BusMaterials.GetTransparentRole(renderer, material) ==
                    VolkswagenT1BusTransparentRole.CabinGlass)
                {
                    renderer.SetPropertyBlock(null, index);
                    VolkswagenT1BusMaterials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            VolkswagenT1BusDiagnostics.Info(
                context,
                $"VolkswagenT1Bus glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            VolkswagenT1BusDiagnostics.Info(
                context,
                $"VolkswagenT1Bus glass vehicle={GetInstanceID()}: repaired after " +
                $"'{source}' renderers={restored}, propertyBlocks={propertyBlocksCleared}.");
        }
    }

    private static bool IsCombinedBodyGlass(Renderer renderer)
    {
        var meshName = renderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? string.Empty;
        return renderer.name.IndexOf("body_glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
               meshName.IndexOf("body_glass", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private List<Renderer> SplitDisconnectedPanels(
        MeshRenderer sourceRenderer,
        Material[] materials)
    {
        var result = new List<Renderer>();
        var filter = sourceRenderer.GetComponent<MeshFilter>();
        var sourceMesh = filter != null ? filter.sharedMesh : null;
        if (filter == null || sourceMesh == null || sourceMesh.subMeshCount == 0)
            return result;

        var vertexPositions = sourceMesh.vertices;
        var triangles = new List<GlassTriangle>();
        for (var submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
        {
            if (sourceMesh.GetTopology(submesh) != MeshTopology.Triangles)
                return result;
            var indices = sourceMesh.GetTriangles(submesh);
            for (var index = 0; index + 2 < indices.Length; index += 3)
                triangles.Add(new GlassTriangle(submesh, indices[index], indices[index + 1], indices[index + 2]));
        }
        if (triangles.Count < 2)
            return result;

        var parents = new int[triangles.Count];
        for (var index = 0; index < parents.Length; index++)
            parents[index] = index;
        var firstTriangleForVertex = new Dictionary<int, int>();
        for (var index = 0; index < triangles.Count; index++)
        {
            var triangle = triangles[index];
            JoinVertex(triangle.A, index, firstTriangleForVertex, parents);
            JoinVertex(triangle.B, index, firstTriangleForVertex, parents);
            JoinVertex(triangle.C, index, firstTriangleForVertex, parents);
        }

        var components = new Dictionary<int, List<int>>();
        for (var index = 0; index < triangles.Count; index++)
        {
            var root = FindComponentRoot(parents, index);
            if (!components.TryGetValue(root, out var component))
            {
                component = new List<int>();
                components.Add(root, component);
            }
            component.Add(index);
        }
        if (components.Count <= 1)
            return result;

        var componentNumber = 0;
        foreach (var component in components.Values)
        {
            var componentTriangles = new List<int>[sourceMesh.subMeshCount];
            for (var submesh = 0; submesh < componentTriangles.Length; submesh++)
                componentTriangles[submesh] = new List<int>();
            var usedVertices = new HashSet<int>();
            foreach (var triangleIndex in component)
            {
                var triangle = triangles[triangleIndex];
                componentTriangles[triangle.Submesh].Add(triangle.A);
                componentTriangles[triangle.Submesh].Add(triangle.B);
                componentTriangles[triangle.Submesh].Add(triangle.C);
                usedVertices.Add(triangle.A);
                usedVertices.Add(triangle.B);
                usedVertices.Add(triangle.C);
            }

            var panelMesh = Instantiate(sourceMesh);
            panelMesh.name = sourceMesh.name + "_CabinGlassPanel_" + componentNumber;
            for (var submesh = 0; submesh < componentTriangles.Length; submesh++)
                panelMesh.SetTriangles(componentTriangles[submesh], submesh, false);
            var firstVertex = true;
            var bounds = default(Bounds);
            foreach (var vertex in usedVertices)
            {
                if (firstVertex)
                {
                    bounds = new Bounds(vertexPositions[vertex], Vector3.zero);
                    firstVertex = false;
                }
                else
                {
                    bounds.Encapsulate(vertexPositions[vertex]);
                }
            }
            bounds.Expand(0.002f);
            panelMesh.bounds = bounds;
            generatedPaneMeshes.Add(panelMesh);

            var paneObject = new GameObject(sourceRenderer.name + "_CabinGlassPanel_" + componentNumber);
            paneObject.layer = sourceRenderer.gameObject.layer;
            paneObject.transform.SetParent(sourceRenderer.transform, false);
            paneObject.AddComponent<MeshFilter>().sharedMesh = panelMesh;
            var paneRenderer = paneObject.AddComponent<MeshRenderer>();
            paneRenderer.sharedMaterials = materials;
            paneRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
            paneRenderer.shadowCastingMode = ShadowCastingMode.Off;
            paneRenderer.receiveShadows = false;
            paneRenderer.forceRenderingOff = false;
            paneRenderer.enabled = true;
            generatedPaneObjects.Add(paneObject);
            result.Add(paneRenderer);
            componentNumber++;
        }

        return result;
    }

    private static void JoinVertex(
        int vertex,
        int triangle,
        Dictionary<int, int> firstTriangleForVertex,
        int[] parents)
    {
        if (firstTriangleForVertex.TryGetValue(vertex, out var firstTriangle))
            JoinComponents(parents, triangle, firstTriangle);
        else
            firstTriangleForVertex.Add(vertex, triangle);
    }

    private static int FindComponentRoot(int[] parents, int index)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }
        return index;
    }

    private static void JoinComponents(int[] parents, int first, int second)
    {
        var firstRoot = FindComponentRoot(parents, first);
        var secondRoot = FindComponentRoot(parents, second);
        if (firstRoot != secondRoot)
            parents[secondRoot] = firstRoot;
    }

    private readonly struct GlassTriangle
    {
        internal GlassTriangle(int submesh, int a, int b, int c)
        {
            Submesh = submesh;
            A = a;
            B = b;
            C = c;
        }

        internal readonly int Submesh;
        internal readonly int A;
        internal readonly int B;
        internal readonly int C;
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
        foreach (var paneObject in generatedPaneObjects)
            if (paneObject != null)
                Destroy(paneObject);
        generatedPaneObjects.Clear();
        foreach (var mesh in generatedPaneMeshes)
            if (mesh != null)
                Destroy(mesh);
        generatedPaneMeshes.Clear();
    }
}

[AddComponentMenu("")]
public sealed class VolkswagenT1BusVisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.64f;
    private const float MaximumDentDepth = 0.34f;
    private const float DepthPerExcessMps = 0.011f;
    private const float FrontDentLateralRadius = 0.82f;
    private const float FrontDentVerticalRadius = 0.68f;
    private const float FrontDentLongitudinalRadius = 0.95f;
    private const float MaximumFrontDentDepth = 0.24786f;
    private const float FrontDepthPerExcessMps = 0.008262f;
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
    private readonly Dictionary<MeshFilter, Mesh> damageMeshes =
        new Dictionary<MeshFilter, Mesh>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private readonly List<DeformableLightAnchor> deformableLightAnchors =
        new List<DeformableLightAnchor>();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private ModContext? context;
    private Rigidbody? body;
    private float impactThresholdMps;
    private float nextCollisionTime;
    private float previousDamage;
    private float previousSavedDamage;
    private int diagnosticLogs;
    private int collisionGateDiagnosticLogs;
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
        damageMeshes.Clear();
        runtimeMeshes.Clear();
        deformableLightAnchors.Clear();
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
            damageMeshes[filter] = runtimeMesh;
            runtimeMeshes.Add(runtimeMesh);
        }
        InitializeDeformableLightAnchors(controller);
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
            repairRecoveryCoroutine = StartCoroutine(RestoreDrivingStateAfterRepair());
            VolkswagenT1BusDiagnostics.DamageInfo(
                context,
                $"VolkswagenT1Bus damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
        }
        UpdateDeformableLightAnchors();
        previousDamage = currentDamage;
        previousSavedDamage = currentSavedDamage;
    }

    private void InitializeDeformableLightAnchors(VehicleController controller)
    {
        foreach (var light in controller.GetComponentsInChildren<Light>(true))
        {
            if (light == null ||
                (!light.name.StartsWith("VolkswagenT1Bus_LeftHeadlightBeam", StringComparison.Ordinal) &&
                 !light.name.StartsWith("VolkswagenT1Bus_RightHeadlightBeam", StringComparison.Ordinal)))
                continue;

            var lightPosition = controller.transform.InverseTransformPoint(light.transform.position);
            MeshFilter? nearestFilter = null;
            var nearestVertexIndex = -1;
            var nearestDistanceSquared = float.PositiveInfinity;
            foreach (var filter in deformableFilters)
            {
                if (!IsFrontLampMesh(filter) ||
                    !originalVertices.TryGetValue(filter, out var baseline) ||
                    !filterToVehicleMatrices.TryGetValue(filter, out var filterToVehicle))
                    continue;

                for (var vertexIndex = 0; vertexIndex < baseline.Length; vertexIndex++)
                {
                    var vertexPosition = filterToVehicle.MultiplyPoint3x4(baseline[vertexIndex]);
                    var distanceSquared = (vertexPosition - lightPosition).sqrMagnitude;
                    if (distanceSquared >= nearestDistanceSquared)
                        continue;

                    nearestDistanceSquared = distanceSquared;
                    nearestFilter = filter;
                    nearestVertexIndex = vertexIndex;
                }
            }

            if (nearestFilter == null || nearestVertexIndex < 0 || nearestDistanceSquared > 0.65f * 0.65f)
            {
                context?.Logger.Warn(
                    $"VolkswagenT1Bus damage vehicle={controller.GetInstanceID()}: " +
                    $"headlight beam '{light.name}' could not find a nearby deforming lamp vertex.");
                continue;
            }

            deformableLightAnchors.Add(new DeformableLightAnchor(
                light,
                nearestFilter,
                nearestVertexIndex,
                lightPosition));
        }

        if (deformableLightAnchors.Count > 0)
            VolkswagenT1BusDiagnostics.DamageInfo(
                context,
                $"VolkswagenT1Bus damage vehicle={controller.GetInstanceID()}: " +
                $"attached {deformableLightAnchors.Count} headlight beams to deforming lamp vertices.");
    }

    private static bool IsFrontLampMesh(MeshFilter filter)
    {
        var meshName = filter.sharedMesh != null ? filter.sharedMesh.name : string.Empty;
        if (filter.name.IndexOf("BHeadlights", StringComparison.OrdinalIgnoreCase) >= 0 ||
            meshName.IndexOf("BHeadlights", StringComparison.OrdinalIgnoreCase) >= 0 ||
            filter.name.StartsWith("VolkswagenT1Bus_Headlights", StringComparison.Ordinal))
            return true;

        for (var current = filter.transform.parent; current != null; current = current.parent)
            if (current.name.IndexOf("BHeadlights", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private void UpdateDeformableLightAnchors()
    {
        if (vehicle == null || deformableLightAnchors.Count == 0)
            return;

        foreach (var anchor in deformableLightAnchors)
        {
            if (anchor.Light == null || anchor.Filter == null ||
                !currentVertices.TryGetValue(anchor.Filter, out var current) ||
                !originalVertices.TryGetValue(anchor.Filter, out var baseline) ||
                !filterToVehicleMatrices.TryGetValue(anchor.Filter, out var filterToVehicle) ||
                anchor.VertexIndex < 0 || anchor.VertexIndex >= current.Length ||
                anchor.VertexIndex >= baseline.Length)
                continue;

            var deformation = filterToVehicle.MultiplyVector(
                current[anchor.VertexIndex] - baseline[anchor.VertexIndex]);
            anchor.Light.transform.position = vehicle.transform.TransformPoint(
                anchor.VehicleLocalPosition + deformation);
        }
    }

    private readonly struct DeformableLightAnchor
    {
        public DeformableLightAnchor(
            Light light,
            MeshFilter filter,
            int vertexIndex,
            Vector3 vehicleLocalPosition)
        {
            Light = light;
            Filter = filter;
            VertexIndex = vertexIndex;
            VehicleLocalPosition = vehicleLocalPosition;
        }

        public Light Light { get; }
        public MeshFilter Filter { get; }
        public int VertexIndex { get; }
        public Vector3 VehicleLocalPosition { get; }
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
        if (!initialized || collision == null)
            return;

        var relativeSpeed = collision.relativeVelocity.magnitude;
        var cooldownReady = Time.unscaledTime >= nextCollisionTime;
        var speedReady = relativeSpeed >= impactThresholdMps;
        var validCollision = NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision);
        if (VolkswagenT1BusDiagnostics.DebugEnabled &&
            VolkswagenT1BusDiagnostics.DamageDebugEnabled &&
            collisionGateDiagnosticLogs++ < MaximumDiagnosticLogs)
        {
            VolkswagenT1BusDiagnostics.DamageInfo(
                context,
                $"VolkswagenT1Bus damage vehicle={vehicle?.GetInstanceID()}: collision observed " +
                $"other='{collision.collider?.name ?? "unknown"}' " +
                $"relativeSpeed={relativeSpeed * 3.6f:0.0}kph " +
                $"threshold={impactThresholdMps * 3.6f:0.0}kph " +
                $"valid={validCollision} cooldownReady={cooldownReady} " +
                $"speedReady={speedReady} contacts={collision.contactCount}.");
        }
        if (!cooldownReady || !speedReady || !validCollision)
            return;

        try
        {
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            nextCollisionTime = Time.unscaledTime + CollisionCooldown;

            var contactCount = collision.contactCount;
            if (contactCount <= 0)
                return;

            var excessSpeed = relativeSpeed - impactThresholdMps;
            var dentDepth = Mathf.Clamp(
                excessSpeed * DepthPerExcessMps,
                0.025f,
                MaximumDentDepth);
            var frontDentDepth = Mathf.Clamp(
                excessSpeed * FrontDepthPerExcessMps,
                0.034f,
                MaximumFrontDentDepth);
            var rearDentDepth = Mathf.Clamp(
                excessSpeed * RearDepthPerExcessMps,
                0.04f,
                MaximumRearDentDepth);

            // Contacts are tiny in number but used against tens of thousands of
            // vertices. Convert them to vehicle-local space once instead of doing
            // Transform/InverseTransform work inside the vertex/contact loop.
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
            var center = body != null ? body.worldCenterOfMass : vehicleTransform.position;
            for (var contactIndex = 0; contactIndex < contactCount; contactIndex++)
            {
                var contact = collision.GetContact(contactIndex);
                var localContact = vehicleTransform.InverseTransformPoint(contact.point);
                var isEndContact =
                    Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                    Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                var isFrontContact = isEndContact && localContact.z >= 0f;

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
                    var towardCenter = (center - contact.point).normalized;
                    var contactNormal = contact.normal.normalized;
                    var worldDirection =
                        Vector3.Dot(contactNormal, towardCenter) >= 0f
                            ? contactNormal
                            : -contactNormal;
                    localInwardDirections[contactIndex] =
                        vehicleTransform.InverseTransformDirection(
                            worldDirection).normalized;
                    contactRadiusSquared[contactIndex] = DentRadius * DentRadius;
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

                var renderer = filter.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var canReachFilter = false;
                    var bounds = renderer.bounds;
                    for (var contactIndex = 0;
                         contactIndex < contactCount;
                         contactIndex++)
                    {
                        if (bounds.SqrDistance(worldContactPoints[contactIndex]) <=
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
                        filter,
                        out var filterToVehicle) ||
                    !vehicleToFilterMatrices.TryGetValue(
                        filter,
                        out var vehicleToFilter))
                {
                    filterToVehicle =
                        vehicleTransform.worldToLocalMatrix *
                        filter.transform.localToWorldMatrix;
                    vehicleToFilter = filterToVehicle.inverse;
                    filterToVehicleMatrices[filter] = filterToVehicle;
                    vehicleToFilterMatrices[filter] = vehicleToFilter;
                }

                var mesh = filter.sharedMesh;
                var meshChanged = false;

                for (var vertexIndex = 0;
                     vertexIndex < vertices.Length;
                     vertexIndex++)
                {
                    var vehicleVertex =
                        filterToVehicle.MultiplyPoint3x4(vertices[vertexIndex]);
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
                            vehicleVertex - localContactPoints[contactIndex];
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
                                1f - Mathf.Sqrt(normalizedDistanceSquared);
                        }
                        else
                        {
                            var distanceSquared = localDelta.sqrMagnitude;
                            if (distanceSquared >= contactRadiusSquared[contactIndex])
                                continue;
                            influence =
                                1f - Mathf.Sqrt(distanceSquared) / DentRadius;
                        }

                        if (influence <= strongestInfluence)
                            continue;

                        strongestInfluence = influence;
                        inwardDirection =
                            localInwardDirections[contactIndex];
                        selectedEndImpact = contactIsEnd[contactIndex];
                        selectedFrontImpact = contactIsFront[contactIndex];
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
                    vehicleVertex +=
                        inwardDirection * (selectedDepth * falloff);

                    var localVertex =
                        vehicleToFilter.MultiplyPoint3x4(vehicleVertex);

                    if (vertexIndex < baseline.Length)
                    {
                        var cumulativeLimit = selectedEndImpact
                            ? selectedFrontImpact
                                ? MaximumFrontDentDepth
                                : MaximumRearDentDepth
                            : MaximumDentDepth;
                        localVertex = baseline[vertexIndex] +
                                      Vector3.ClampMagnitude(
                                          localVertex - baseline[vertexIndex],
                                          cumulativeLimit);
                    }

                    vertices[vertexIndex] = localVertex;
                    changedVertices++;
                    meshChanged = true;
                    frontImpact |= selectedEndImpact && selectedFrontImpact;
                    rearImpact |= selectedEndImpact && !selectedFrontImpact;
                }

                if (!meshChanged)
                    continue;

                // Deformation is strictly inward, so the spawn-time mesh bounds
                // remain a safe conservative rendering/culling volume. Avoid the
                // expensive full-mesh RecalculateBounds() pass on every impact.
                mesh.vertices = vertices;
                if (originalBounds.TryGetValue(filter, out var originalMeshBounds))
                    mesh.bounds = originalMeshBounds;
                changedMeshes++;
            }

            if (VolkswagenT1BusDiagnostics.DebugEnabled &&
                VolkswagenT1BusDiagnostics.DamageDebugEnabled &&
                diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                var elapsedMilliseconds =
                    (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt) *
                    1000d / System.Diagnostics.Stopwatch.Frequency;
                VolkswagenT1BusDiagnostics.DamageInfo(
                    context,
                    $"VolkswagenT1Bus damage vehicle={vehicle?.GetInstanceID()}: " +
                    $"inward dent contact='{collision.collider?.name ?? "unknown"}' " +
                    $"relativeSpeed={relativeSpeed * 3.6f:0.0}kph " +
                    $"localContact=({primaryLocalContact.x:0.00}," +
                    $"{primaryLocalContact.y:0.00},{primaryLocalContact.z:0.00}) " +
                    $"region={(frontImpact ? "front" : rearImpact ? "rear" : "side")} " +
                    $"depth={(frontImpact ? frontDentDepth : rearImpact ? rearDentDepth : dentDepth):0.000}m " +
                    $"meshes={changedMeshes} skipped={skippedMeshes} " +
                    $"vertices={changedVertices} processing={elapsedMilliseconds:0.0}ms " +
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
                $"VolkswagenT1Bus damage vehicle={vehicle?.GetInstanceID()}: " +
                $"inward deformation failed with {exception.GetType().Name}: " +
                $"{exception.Message}");
        }
    }

    private static bool IsAttachedExteriorDetail(MeshFilter filter, int vertexCount)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;
        var size = renderer.bounds.size;
        var longestSide = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        return vertexCount <= 400 || longestSide <= 0.32f;
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

internal sealed class VolkswagenT1BusAerodynamicsController : MonoBehaviour
{
    private const float AirDensityKgPerCubicMeter = 1.225f;
    // The period T1 drag coefficient is reported near 0.44. Combined with an
    // estimated projected front area of about 2.95 m2, this gives CdA ~= 1.30.
    private const float DragAreaSquareMeters = 1.30f;
    private const float RollingResistanceCoefficient = 0.010f;
    private const float MinimumSpeedMetersPerSecond = 0.25f;

    private Rigidbody? body;
    private ModContext? context;
    private bool initialized;

    internal void Initialize(Rigidbody rigidbody, ModContext? modContext)
    {
        if (initialized)
            return;

        body = rigidbody;
        context = modContext;
        initialized = true;
        VolkswagenT1BusDiagnostics.Info(
            context,
            $"VolkswagenT1Bus aerodynamics rigidbody={body.GetInstanceID()}: " +
            $"dragArea={DragAreaSquareMeters:0.00}m2 rollingResistance={RollingResistanceCoefficient:0.000}.");
    }

    private void FixedUpdate()
    {
        if (body == null)
            return;

        var planarVelocity = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
        var speed = planarVelocity.magnitude;
        if (speed < MinimumSpeedMetersPerSecond)
            return;

        var aerodynamicForce =
            0.5f * AirDensityKgPerCubicMeter * DragAreaSquareMeters * speed * speed;
        var airForce = -planarVelocity / speed * aerodynamicForce;
        var forwardSpeed = Vector3.Dot(planarVelocity, body.transform.forward);
        var rollingForce = Mathf.Abs(forwardSpeed) < MinimumSpeedMetersPerSecond
            ? Vector3.zero
            : -Mathf.Sign(forwardSpeed) * body.transform.forward *
              (body.mass * Physics.gravity.magnitude * RollingResistanceCoefficient);
        body.AddForce(airForce + rollingForce, ForceMode.Force);
    }
}

