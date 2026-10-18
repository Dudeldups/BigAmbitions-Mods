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

public sealed class DodgeRam1500TRXRuntime : MonoBehaviour
{
    private const float VehicleMass = 2880f;
    private const float EnginePowerKw = 523f;
    private const float EngineIdleRpm = 700f;
    private const float EngineLimitRpm = 6200f;
    private const float SpeedLimitKph = 190f;
    private const float FinalDriveRatio = 3.55f;
    private const float EngineInertia = 0.8f;
    private const float ClutchSlipTorque = 1100f;
    private const float EngineStartDuration = 0.48f;
    // NWH's clutch model uses these low engagement values for the 8HP95's
    // torque-converter launch without a manual-car RPM flare.
    private const float ClutchEngagementRpm = 850f;
    private const float ClutchThrottleOffsetRpm = 250f;
    private const float ClutchEngagementRange = 250f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 1.02f;
    private const float AntiRollBarForce = 12000f;
    private const float FrontSuspensionTravel = 0.150f;
    private const float RearSuspensionTravel = 0.180f;
    private const float FrontTireRadius = 0.4445f;
    private const float RearTireRadius = 0.4445f;
    private const float FrontTireWidth = 0.325f;
    private const float RearTireWidth = 0.325f;
    private const float VehicleLinearDrag = 0f;
    private const float FrontForwardGrip = 1.00f;
    private const float RearForwardGrip = 1.00f;
    private const float FrontForwardStiffness = 1.08f;
    private const float RearForwardStiffness = 1.08f;
    private const float DeformationStrength = 0.15f;
    private const float DeformationRadius = 0.34f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 0.45f;
    private const float DamageDecelerationThreshold = 500f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private const float WarehouseExitGuardDuration = 8f;
    private const float WarehouseExitGuardClearDistance = 4f;
    // Truck-sized mass distribution, estimated from the chassis footprint (kg m^2).
    private static readonly Vector3 StableInertiaTensor = new Vector3(8000f, 8400f, 3200f);
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.35f, -0.05f);
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.44f, 2.50f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(2.10f, 0.62f, 0.78f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.44f, -2.50f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(2.10f, 0.62f, 0.78f);
    private static readonly Vector3 DriverExitPosition =
        new Vector3(-1.65f, 0.17f, 0.55f);
    private static readonly Vector3 PassengerExitPosition =
        new Vector3(1.65f, 0.17f, 0.55f);

    private static readonly float[] TRXGears =
    {
        -3.30f,
        0f,
        4.71f,
        3.14f,
        2.11f,
        1.67f,
        1.29f,
        1.00f,
        0.84f,
        0.67f,
    };

    private static AnimationCurve CreateTRXPowerCurve() => DodgeRam1500TRXPowertrainProfile.CreatePowerCurve();

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private readonly HashSet<DodgeRam1500TRXDrivelineLoss> drivelineLosses = new HashSet<DodgeRam1500TRXDrivelineLoss>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private int enteredVehicleActivationInstanceId;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private Coroutine? warehouseControlRecoveryCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private DodgeRam1500TRXWarehouseEntryController? warehouseExitGuardEntryController;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private bool dealerRegistrationReady;
    private bool dealerReadyLogged;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private readonly List<DodgeRam1500TRXVisualDamageController> visualDamageOwners = new();
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;
    private GameObject? playerVehiclePrefab;

    public static DodgeRam1500TRXRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<DodgeRam1500TRXRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(DodgeRam1500TRXRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<DodgeRam1500TRXRuntime>();
        }

        DodgeRam1500TRXNativeActivities.SetContext(context);
        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        DodgeRam1500TRXPrivateDriverSupport.SetContext(context);
        var trafficFrequency = runtime.GetComponent<DodgeRam1500TRXTrafficFrequency>() ??
                               runtime.gameObject.AddComponent<DodgeRam1500TRXTrafficFrequency>();
        trafficFrequency.Initialize(context);
        runtime.SubscribeEvents();
        GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
        runtime.privateDriverPoolReady = runtime.TryPreparePrivateDriverPool("mod-load-immediate");
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
        StopWarehouseControlRecovery();
        StopWarehouseExitGuard();
        DodgeRam1500TRXWashBindings.Shutdown();
        DodgeRam1500TRXNativeActivities.Shutdown();
        foreach (var owner in visualDamageOwners) if (owner != null) owner.ReleaseNumericalOwnership();
        visualDamageOwners.Clear();
        foreach (var loss in drivelineLosses)
            if (loss != null) loss.Shutdown();
        drivelineLosses.Clear();
        configuredVehicleIds.Clear();
        dealerRegistrationReady = false;
        dealerReadyLogged = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        DodgeRam1500TRXPrivateDriverSupport.RemoveVehicle(vehicleTypeName);
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

    private void SubscribeEvents()
    {
        DodgeRam1500TRXNativeActivities.PlayerInstanceReady -= HandlePlayerInstanceReady;
        DodgeRam1500TRXNativeActivities.PlayerInstanceReady += HandlePlayerInstanceReady;
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
        DodgeRam1500TRXNativeActivities.PlayerInstanceReady -= HandlePlayerInstanceReady;
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
        // Restore notifications can precede creation of a paint controller and
        // have no selected vehicle. Bootstrap all owned targets at this exact
        // transition, independently of dealer/layout/traffic readiness.
        if (eventName == DodgeRam1500TRXPaintController.SupplementalColorRestoredEvent)
        {
            ConfigureExistingAppearance("supplemental-color-restored");
            return;
        }
        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        DodgeRam1500TRXDiagnostics.DealerEntryInfo(
            context,
            $"DodgeRam1500TRX dealer-entry: game-event='{eventName}', " +
            DescribeVehicleState(selectedVehicle));

        // Dealer purchases do not reliably invoke onEnterVehicle. This event
        // is a bounded lifecycle handoff from the dealer display vehicle to
        // the owned, player-controlled instance, so configure it here rather
        // than scanning vehicles every frame.
        TryConfigureVehicle(selectedVehicle);
        selectedVehicle!
            .GetComponent<DodgeRam1500TRXPaintController>()
            ?.ApplyCurrentColor("game-event");
    }

    private void HandleVehicleVariablesChanged() => ConfigureExistingVehicles(out _);

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SubscribeEvents();
        // Scene-loaded runs after native Awake, before Start initializes traffic.
        // Do not postpone pool registration behind layout loading or one frame.
        if (!privateDriverPoolReady && playerVehiclePrefab != null)
            privateDriverPoolReady = TryPreparePrivateDriverPool($"scene-loaded-immediate:{scene.name}");
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeEvents();
        if (DodgeRam1500TRXLoadRecovery.CompleteInterruptedLoad(context))
            StartCoroutine(ReportLoadedInputState());
        privateDriverRegistrationAllowed = true;
        ScheduleInitialization("game-loaded-late");
    }

    private IEnumerator ReportLoadedInputState()
    {
        // The native loading screen fades out for 0.8 seconds after this event.
        yield return new WaitForSecondsRealtime(2f);
        DodgeRam1500TRXLoadRecovery.ReportInputState(context);
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
        StopWarehouseControlRecovery();
        StopWarehouseExitGuard();
        DodgeRam1500TRXWashBindings.Shutdown();
        DodgeRam1500TRXNativeActivities.Shutdown();
        foreach (var owner in visualDamageOwners) if (owner != null) owner.ReleaseNumericalOwnership();
        visualDamageOwners.Clear();
        foreach (var loss in drivelineLosses)
            if (loss != null) loss.Shutdown();
        drivelineLosses.Clear();
        configuredVehicleIds.Clear();
        dealerRegistrationReady = false;
        dealerReadyLogged = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        DodgeRam1500TRXDiagnostics.DealerEntryInfo(
            context,
            $"DodgeRam1500TRX dealer-entry: onEnterVehicle, {DescribeVehicleState(vehicle)}");
        TryConfigureVehicle(vehicle);
        vehicle.GetComponent<DodgeRam1500TRXGlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle == null || !IsTargetVehicle(vehicle))
            return;
        vehicle.GetComponent<DodgeRam1500TRXPaintController>()
            ?.ApplyCurrentColor("vehicle-entered");
        ScheduleEnteredVehicleActivation(vehicle);
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;
        StopWarehouseControlRecovery();
        DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
            context,
            $"DodgeRam1500TRX warehouse-exit: onExitVehicle, {DescribeVehicleState(vehicle)}");
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
        DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
            context,
            $"DodgeRam1500TRX player-exit: deferred validation playerPosition={root.position}, " +
            $"agentCount={agents.Length}, anyAgentOffNavMesh={agents.Any(agent => agent != null && agent.enabled && !agent.isOnNavMesh)}, " +
            $"initialClear={initialExitClear}, needsRecovery={needsRecovery}.");
        if (!needsRecovery)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }
        if (!TryFindClearExitPosition(root, exitedVehicle, out var target))
        {
            DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
                context,
                "DodgeRam1500TRX player-exit: recovery failed; no NavMesh/capsule-clear candidate found.");
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
        DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
            context,
            $"DodgeRam1500TRX player-exit: recovery moved player to {target}.");
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
               $"nwhInitialized={physics?.IsInitialized}, stateSettings={physics?.stateSettings != null}, powertrainActive={physics?.powertrain?.IsActive}, " +
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
            DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                context,
                $"DodgeRam1500TRX dealer-entry: activation skipped because the vehicle is " +
                $"neither controlled nor selected, {DescribeVehicleState(vehicle)}");
            return;
        }

        var instanceId = vehicle.GetInstanceID();
        if (enteredVehicleActivationCoroutine != null &&
            enteredVehicleActivationInstanceId == instanceId)
        {
            DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                context,
                $"DodgeRam1500TRX dealer-entry: activation already pending instance={instanceId}.");
            return;
        }

        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);

        enteredVehicleActivationInstanceId = instanceId;
        DodgeRam1500TRXDiagnostics.DealerEntryInfo(
            context,
            $"DodgeRam1500TRX dealer-entry: activation scheduled, {DescribeVehicleState(vehicle)}");
        enteredVehicleActivationCoroutine = StartCoroutine(ActivateEnteredVehicle(vehicle));
    }

    private static readonly MethodInfo? NativeCarStartEngine = typeof(CarController).GetMethod(
        "StartEngine", BindingFlags.Instance | BindingFlags.NonPublic);

    private bool RestoreNativeDriveActivation(VehicleController vehicle, PhysicsVehicle physics)
    {
        if (!vehicle.controlledByPlayer || vehicle is not CarController car) return false;
        if (physics.stateSettings == null || NativeCarStartEngine == null)
        {
            context?.Logger.Warn($"DodgeRam1500TRX drive activation failed vehicle={vehicle.GetInstanceID()}: " +
                $"stateSettings={physics.stateSettings != null}, nativeStart={NativeCarStartEngine != null}; input/physics cannot be restored.");
            return false;
        }
        // Native CarController.StartEngine enables NWH and clears kinematic mode.
        // NWH Start/OnEnable reactivates components and the game's wheel/unfreeze
        // callbacks. Preserve normal fuel/damage restrictions and engine tuning.
        try { NativeCarStartEngine.Invoke(car, null); }
        catch (Exception exception)
        {
            var failure = exception.InnerException ?? exception;
            context?.Logger.Warn($"DodgeRam1500TRX native drive activation failed vehicle={vehicle.GetInstanceID()}: {failure.GetType().Name}: {failure.Message}");
            return false;
        }
        DodgeRam1500TRXDiagnostics.DealerEntryInfo(context,
            "DodgeRam1500TRX drive activation used native CarController.StartEngine: " + DescribeVehicleState(vehicle));
        return true;
    }

    private IEnumerator ActivateEnteredVehicle(VehicleController vehicle)
    {
        // Wait for native entry. Its later save/dealer stabilization can leave
        // the entire physics controller asleep; recover through CarController's
        // native start path, not EngineComponent.StartEngine alone.
        yield return new WaitForSecondsRealtime(.25f);
        var physics = vehicle.GetComponent<PhysicsVehicle>() ??
                      vehicle.GetComponentInChildren<PhysicsVehicle>(true);
        if (physics == null)
        {
            DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                context,
                $"DodgeRam1500TRX dealer-entry: activation failed; NWH controller missing, " +
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
                DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                    context,
                    $"DodgeRam1500TRX dealer-entry: activation cancelled attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }

            if (vehicle is CarController washingCar && washingCar.isWashing)
            {
                DodgeRam1500TRXDiagnostics.DealerEntryInfo(context,
                    "DodgeRam1500TRX entry activation deferred to native wash completion; preserving wash suspension lock.");
                break;
            }

            if (physics.stateSettings == null)
            {
                context?.Logger.Warn($"DodgeRam1500TRX drive activation cancelled vehicle={vehicle.GetInstanceID()}: bundled native StateSettings are missing.");
                break;
            }
            var body = physics.vehicleRigidbody;
            if (!physics.enabled || !physics.IsInitialized || (body != null && body.isKinematic))
            {
                if (!RestoreNativeDriveActivation(vehicle, physics)) break;
                // Let the normal first Start initialize components and run the
                // native wheel/unfreeze callback before checking engine health.
                yield return null;
                if (vehicle == null || !vehicle.controlledByPlayer) break;
            }
            // Paint assignment from the dealer is asynchronous too; retain a
            // bounded entry refresh without turning it into runtime polling.
            vehicle.GetComponent<DodgeRam1500TRXPaintController>()
                ?.ApplyCurrentColor("vehicle-entered");
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                context,
                $"DodgeRam1500TRX dealer-entry: activation attempt={attempt + 1}, " +
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
                    DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                        context,
                        "DodgeRam1500TRX dealer-entry: native engine healthy; shifted neutral to first.");
                    yield return new WaitForFixedUpdate();
                    DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                        context,
                        $"DodgeRam1500TRX dealer-entry: first-gear settle, gear={transmission.Gear}, " +
                        $"ratio={transmission.currentGearRatio:0.000}.");
                }
                else
                {
                    DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                        context,
                        $"DodgeRam1500TRX dealer-entry: native engine healthy; preserved gear={transmission.Gear}.");
                }
                break;
            }

            DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                context,
                $"DodgeRam1500TRX dealer-entry: dormant powertrain; restart attempt={attempt + 1}.");
            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
            {
                DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                    context,
                    $"DodgeRam1500TRX dealer-entry: restart aborted after stop attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }
            if (!RestoreNativeDriveActivation(vehicle, physics)) break;
            yield return new WaitForSecondsRealtime(.75f);
            if (vehicle != null && vehicle.controlledByPlayer &&
                transmission.Gear == 0)
            {
                transmission.ShiftInto(1, true);
                DodgeRam1500TRXDiagnostics.DealerEntryInfo(
                    context,
                    $"DodgeRam1500TRX dealer-entry: restart attempt={attempt + 1} shifted neutral to first.");
            }
            yield return new WaitForSecondsRealtime(.15f);
        }

        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        DodgeRam1500TRXDiagnostics.DealerEntryInfo(
            context,
            $"DodgeRam1500TRX dealer-entry: activation completed, {DescribeVehicleState(vehicle)}");
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!DodgeRam1500TRXLuxuryDealerStock.IsTargetDealer(registration?.BusinessName) ||
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
            DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
                context,
                $"DodgeRam1500TRX warehouse-exit: guard skipped; current vehicle is not " +
                $"a controlled DodgeRam1500TRX ({DescribeVehicleState(vehicle)}).");
            return;
        }

        StopWarehouseControlRecovery();
        warehouseControlRecoveryCoroutine = StartCoroutine(RecoverWarehouseControls(vehicle));
        var entrance = FindClosestDriveInEntrance(vehicle.transform.position);
        if (entrance == null)
        {
            DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
                context,
                $"DodgeRam1500TRX warehouse-exit: guard skipped; no DriveInEntrance found, " +
                DescribeVehicleState(vehicle));
            return;
        }

        var nativeMeshCollider = vehicle.GetComponentInChildren<MeshCollider>(true);
        DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
            context,
            $"DodgeRam1500TRX warehouse-exit: native exit completed, {DescribeVehicleState(vehicle)} " +
            $"entrance='{entrance.name}' entrancePosition={entrance.transform.position}, " +
            $"nativeMesh='{nativeMeshCollider?.name ?? "missing"}', " +
            $"nativeMeshLength={nativeMeshCollider?.sharedMesh?.bounds.size.z:0.000}.");

        StopWarehouseExitGuard();
        warehouseExitGuardEntryController =
            vehicle.GetComponent<DodgeRam1500TRXWarehouseEntryController>();
        warehouseExitGuardEntryController?.SuppressEntrance(entrance, "warehouse-exit-guard");
        foreach (var enterTrigger in entrance.GetComponentsInChildren<DriveInEntranceEnterTrigger>(true))
        foreach (var collider in enterTrigger.GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled || !collider.isTrigger)
                continue;

            warehouseExitGuardColliders.Add(collider);
            DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
                context,
                $"DodgeRam1500TRX warehouse-exit: disabling entry trigger " +
                $"'{collider.name}' bounds={collider.bounds}.");
            collider.enabled = false;
        }

        if (warehouseExitGuardColliders.Count == 0)
        {
            warehouseExitGuardEntryController?.ClearSuppressedEntrance(
                entrance,
                "no-native-entry-trigger");
            warehouseExitGuardEntryController = null;
            DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
                context,
                "DodgeRam1500TRX warehouse-exit: guard skipped; matching entrance had no enabled trigger colliders.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(
            vehicle.transform.position - entrance.transform.position,
            Vector3.up);
        if (outward.sqrMagnitude < .0001f)
            outward = Vector3.ProjectOnPlane(entrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < .0001f)
        {
            DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
                context,
                "DodgeRam1500TRX warehouse-exit: guard aborted; outward direction was zero.");
            StopWarehouseExitGuard();
            return;
        }

        outward.Normalize();
        var startingDistance = Vector3.Dot(vehicle.transform.position, outward);
        Physics.SyncTransforms();
        DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
            context,
            $"DodgeRam1500TRX warehouse-exit: guard started triggerCount={warehouseExitGuardColliders.Count}, " +
            $"outward={outward}, startProjection={startingDistance:0.000}, " +
            $"clearDistance={WarehouseExitGuardClearDistance:0.00}, " +
            $"timeout={WarehouseExitGuardDuration:0.0}s.");
        warehouseExitGuardCoroutine = StartCoroutine(GuardWarehouseExit(
            vehicle,
            outward,
            startingDistance));
    }

    private static readonly FieldInfo? BuildingTransitionField = typeof(BuildingManager).GetField(
        "enteringBuilding", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private void StopWarehouseControlRecovery()
    {
        if (warehouseControlRecoveryCoroutine != null) StopCoroutine(warehouseControlRecoveryCoroutine);
        warehouseControlRecoveryCoroutine = null;
    }

    private static void ReleaseWarehouseYaw(VehicleController vehicle, Rigidbody body)
    {
        if ((body.constraints & RigidbodyConstraints.FreezeRotationY) == 0) return;
        // Use the native unfreeze path so its center-of-mass properties are also
        // restored. Only the completed, controlled Dodge transition calls here.
        vehicle.SetFreeze(false);
        body.WakeUp();
    }

    private IEnumerator RecoverWarehouseControls(VehicleController vehicle)
    {
        // BuildingManager.MoveCarToSpawnPoint temporarily sets constraints=96,
        // including FreezeRotationY. Wait for its finite building transition;
        // do not release the stabilization lock during the native fade/teleport.
        for (var attempt = 0; attempt < 25; attempt++)
        {
            yield return new WaitForSecondsRealtime(.1f);
            if (vehicle == null || !vehicle.controlledByPlayer ||
                !ReferenceEquals(GameManager.Instance?.selectedVehicle, vehicle)) break;
            var manager = InstanceBehavior<BuildingManager>.Instance;
            if (manager != null && BuildingTransitionField?.GetValue(manager) is true) continue;
            var body = vehicle.GetComponent<Rigidbody>();
            if (body == null) break;
            var before = body.constraints;
            ReleaseWarehouseYaw(vehicle, body);
            DodgeRam1500TRXDiagnostics.WarehouseExitInfo(context,
                $"DodgeRam1500TRX warehouse controls vehicle={vehicle.GetInstanceID()} attempt={attempt + 1} constraintsBefore={before} constraintsAfter={body.constraints} nativeTransitionFinished=true.");
            if ((body.constraints & RigidbodyConstraints.FreezeRotationY) != 0)
                context?.Logger.Warn($"DodgeRam1500TRX warehouse exit: yaw is still locked on vehicle={vehicle.GetInstanceID()}.");
            warehouseControlRecoveryCoroutine = null;
            yield break;
        }
        var remainingBody = vehicle != null ? vehicle.GetComponent<Rigidbody>() : null;
        if (vehicle != null && vehicle.controlledByPlayer && remainingBody != null &&
            (remainingBody.constraints & RigidbodyConstraints.FreezeRotationY) != 0)
            context?.Logger.Warn($"DodgeRam1500TRX warehouse exit: native transition did not release yaw within 2.5 seconds on vehicle={vehicle.GetInstanceID()}.");
        warehouseControlRecoveryCoroutine = null;
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
        DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
            context,
            $"DodgeRam1500TRX warehouse-exit: guard ending reason={reason}, " +
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
            DodgeRam1500TRXDiagnostics.WarehouseExitInfo(
                context,
                $"DodgeRam1500TRX warehouse-exit: restoring triggerCount={warehouseExitGuardColliders.Count}, " +
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
        ConfigureExistingVehicles(out _);
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
        // One frame gives the scene callback a stable hierarchy. If business
        // layouts are still loading, their game-loaded-late callback will
        // schedule the next attempt instead of polling the loading flag.
        yield return null;
        initializationCoroutine = null;

        if (!privateDriverPoolReady && playerVehiclePrefab != null)
            privateDriverPoolReady = TryPreparePrivateDriverPool(source);
        ConfigureExistingAppearance(source);
        if (BusinessLayoutSetHelper.loadingLayouts)
        {
            DodgeRam1500TRXDiagnostics.Info(
                context,
                $"DodgeRam1500TRX lifecycle init deferred source='{source}' until game-loaded-late.");
            yield break;
        }

        var dealerReady = dealerRegistrationReady || EnsureDealerStock(source);
        if (privateDriverRegistrationAllowed && !privateDriverReady)
            EnsurePrivateDriverSupport(source);
        ConfigureExistingVehicles(out var matchedCount);

        if (!dealerReady)
        {
            context?.Logger.Warn(
                $"DodgeRam1500TRX: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={matchedCount}.");
        }
        if (privateDriverRegistrationAllowed && !privateDriverReady)
        {
            context?.Logger.Warn(
                $"DodgeRam1500TRX: private-driver support not ready source='{source}'.");
        }
        if (!privateDriverPoolReady)
        {
            context?.Logger.Warn(
                $"DodgeRam1500TRX: private-driver traffic pool not ready source='{source}', reason={DodgeRam1500TRXPrivateDriverSupport.LastPoolFailure}.");
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
            var ready = DodgeRam1500TRXLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerRegistrationReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                DodgeRam1500TRXDiagnostics.Info(
                    context,
                    $"DodgeRam1500TRX: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"DodgeRam1500TRX: dealer stock update failed source='{source}': " +
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
                privateDriverReady = DodgeRam1500TRXPrivateDriverSupport.EnsureVehicleAvailable(
                    vehicleTypeName);
            }
            if (privateDriverReady)
            {
                DodgeRam1500TRXDiagnostics.Info(
                    context,
                    $"DodgeRam1500TRX: private-driver support registered source='{source}'.");
            }
            return privateDriverReady && privateDriverPoolReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"DodgeRam1500TRX: private-driver registration failed source='{source}': " +
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
            return DodgeRam1500TRXPrivateDriverSupport.PrepareTrafficPool(
                playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                context?.Logger.Warn(
                    $"DodgeRam1500TRX: private-driver pool preparation failed source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
                privateDriverPreparationExceptionLogged = true;
            }

            return false;
        }
    }

    private void HandlePlayerInstanceReady(VehicleController vehicle) =>
        ConfigureVehicleAppearance(vehicle, "prefab-spawn-ready");

    private void ConfigureExistingAppearance(string source)
    {
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null) return;
        foreach (var vehicle in vehicles) ConfigureVehicleAppearance(vehicle, source);
    }

    private void ConfigureVehicleAppearance(VehicleController? vehicle, string source)
    {
        if (!IsTargetVehicle(vehicle)) return;
        try
        {
            var materials = vehicle!.GetComponent<DodgeRam1500TRXMaterialController>();
            if (materials == null) materials = vehicle.gameObject.AddComponent<DodgeRam1500TRXMaterialController>();
            materials.Initialize(context);
            var paint = vehicle.GetComponent<DodgeRam1500TRXPaintController>();
            if (paint == null) paint = vehicle.gameObject.AddComponent<DodgeRam1500TRXPaintController>();
            paint.Initialize(vehicle, context);
            paint.ApplyCurrentColor(source, force: true, preferSavedColor: true);
            DodgeRam1500TRXDiagnostics.PaintInfo(context,
                $"DodgeRam1500TRX reload appearance vehicle={vehicle.GetInstanceID()} source='{source}' " +
                $"saved='{vehicle.vehicleInstance?.vehicleColorName}' live='{vehicle.CarFeatures?.VehicleColor?.name}' " +
                $"applied={paint.HasAppliedColor}.");
        }
        catch (Exception exception)
        {
            context?.Logger.Warn($"DodgeRam1500TRX appearance vehicle={vehicle?.GetInstanceID()} source='{source}': {exception.GetBaseException().Message}");
        }
    }

    private void ConfigureExistingVehicles(out int matchedCount)
    {
        matchedCount = 0;
        var vehicles = VehicleHelper.AllPlayerVehicles;
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
        if (vehicle != null && (IsTargetVehicle(vehicle) || vehicle.GetComponent<DodgeRam1500TRXNativeActivities>() != null))
            DodgeRam1500TRXNativeActivities.EnsureSleep(vehicle, context);

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
            vehicle.GetComponent<DodgeRam1500TRXVisualDamageController>()?.CheckRepair();
            vehicle.GetComponent<DodgeRam1500TRXPaintController>()
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
                rigidbody.inertiaTensorRotation = Quaternion.identity;
                rigidbody.inertiaTensor = StableInertiaTensor;
                rigidbody.drag = VehicleLinearDrag;
                rigidbody.angularDrag = 1.45f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.solverIterations = Mathf.Max(rigidbody.solverIterations, 12);
                rigidbody.solverVelocityIterations =
                    Mathf.Max(rigidbody.solverVelocityIterations, 4);

                var highwaySeamGuard =
                    vehicle.GetComponent<DodgeRam1500TRXHighwaySeamGuard>();
                if (highwaySeamGuard == null)
                {
                    highwaySeamGuard = vehicle.gameObject
                        .AddComponent<DodgeRam1500TRXHighwaySeamGuard>();
                }
                highwaySeamGuard.Initialize(rigidbody);
            }

            DodgeRam1500TRXWashBindings.Configure(vehicle, context);
            ConfigureMassProperties(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            var contactMaterialOwner =
                vehicle.GetComponent<DodgeRam1500TRXContactMaterialOwner>();
            if (contactMaterialOwner == null)
            {
                contactMaterialOwner = vehicle.gameObject
                    .AddComponent<DodgeRam1500TRXContactMaterialOwner>();
            }
            ConfigureBodyColliders(
                vehicle.gameObject,
                contactMaterialOwner.GetOrCreateMaterial());
            ConfigureExitMarkers(vehicle.gameObject);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(vehicle.gameObject);
            var warehouseBounds =
                vehicle.GetComponent<DodgeRam1500TRXWarehouseBoundsController>();
            if (warehouseBounds == null)
            {
                warehouseBounds = vehicle.gameObject
                    .AddComponent<DodgeRam1500TRXWarehouseBoundsController>();
            }
            warehouseBounds.Initialize();
            var warehouseEntry = vehicle.GetComponent<DodgeRam1500TRXWarehouseEntryController>();
            if (warehouseEntry == null)
            {
                warehouseEntry = vehicle.gameObject
                    .AddComponent<DodgeRam1500TRXWarehouseEntryController>();
            }
            warehouseEntry.Initialize(vehicle, context);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var drivelineLoss = vehicle.GetComponent<DodgeRam1500TRXDrivelineLoss>();
            if (drivelineLoss != null) drivelineLosses.Add(drivelineLoss);
            var aerodynamics = vehicle.GetComponent<DodgeRam1500TRXAerodynamicResistance>();
            if (aerodynamics == null) aerodynamics = vehicle.gameObject.AddComponent<DodgeRam1500TRXAerodynamicResistance>();
            aerodynamics.Initialize(vehicle.GetComponent<Rigidbody>());
            var collisionSeparation = vehicle.GetComponent<DodgeRam1500TRXCollisionSeparationController>();
            if (collisionSeparation == null)
                collisionSeparation = vehicle.gameObject.AddComponent<DodgeRam1500TRXCollisionSeparationController>();
            collisionSeparation.Initialize(vehicle);
            var caliperController = vehicle.GetComponent<DodgeRam1500TRXCaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<DodgeRam1500TRXCaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialController = vehicle.GetComponent<DodgeRam1500TRXMaterialController>();
            if (materialController == null)
                materialController = vehicle.gameObject.AddComponent<DodgeRam1500TRXMaterialController>();
            var materialResult = materialController.Initialize(context);
            var glassController = vehicle.GetComponent<DodgeRam1500TRXGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<DodgeRam1500TRXGlassController>();
            glassController.Initialize(context);
            var paintController = vehicle.GetComponent<DodgeRam1500TRXPaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<DodgeRam1500TRXPaintController>();
            paintController.Initialize(vehicle, context);
            var lightingController = vehicle.GetComponent<DodgeRam1500TRXLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<DodgeRam1500TRXLightingController>();
            lightingController.Initialize(vehicle, context);
            // Include the authored lamp overlays in this instance's deformation
            // allowlist only after they exist so illuminated lamp surfaces move
            // with the surrounding lamp housings after an impact.
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var driverController = vehicle.GetComponent<DodgeRam1500TRXDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<DodgeRam1500TRXDriverController>();
            driverController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<DodgeRam1500TRXAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<DodgeRam1500TRXAudioController>();
            audioController.Initialize(vehicle, context);
            DodgeRam1500TRXDiagnostics.Info(
                context,
                $"DodgeRam1500TRX: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=8-speed-automatic, downshiftRPM=3100, upshiftRPM=5800, awd=40/60, " +
                $"powertrainConfigured={powertrainConfigured}, drivePowerLoss={DodgeRam1500TRXPowertrainProfile.DrivePowerLoss:P1}, dragArea={DodgeRam1500TRXPowertrainProfile.DragArea:0.00}m2, brakeTorque={DodgeRam1500TRXPowertrainProfile.BrakeTorque:0}Nm, " +
                $"centerOfMass={StableCenterOfMass}, inertia={StableInertiaTensor}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstaclesNormalized={normalizedNavMeshObstacles}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"converterModel={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, creep={ClutchCreepTorque:0}Nm, " +
                $"engineInertia={EngineInertia:0.000}, clutchCapacityNm={ClutchSlipTorque:0}, " +
                $"powerCurve=supercharged-HEMI-V8-profile, steeringCalipers=4, steeringFullLockThrough=25kmh, steeringRate={DodgeRam1500TRXSteeringProfile.DegreesPerSecond:0}deg/s, " +
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
                $"DodgeRam1500TRX: vehicle configuration failed instance={instanceId}: " +
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
                SetFloat(spring, "maxForce", 32000f);

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
            SetBool(component, "useDefaultInertia", false);
            SetVector3(component, "inertiaTensor", StableInertiaTensor);
            SetVector3(component, "combinedInertiaTensor", StableInertiaTensor);
            SetVector3(component, "dimensions", new Vector3(2.08f, 1.6f, 5.48f));
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

            var colliders = transform.GetComponentsInChildren<BoxCollider>(true);
            if (colliders.Length > 0)
            {
                // Keep the underside above the tire contact plane to avoid
                // scraping the road and levering the chassis during an impact.
                // The upper face still reaches the warehouse drive-in trigger.
                colliders[0].center = new Vector3(0f, 0.35f, 0f);
                colliders[0].size = new Vector3(2.10f, 0.52f, 5.80f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 1.01f, -0.05f);
                colliders[1].size = new Vector3(1.94f, 1.20f, 2.72f);
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

            foreach (var collider in transform.GetComponentsInChildren<BoxCollider>(true))
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

            foreach (var collider in child.GetComponentsInChildren<BoxCollider>(true))
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
                $"DodgeRam1500TRX damage vehicle={vehicle.GetInstanceID()}: " +
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
                $"DodgeRam1500TRX damage vehicle={vehicle.GetInstanceID()}: " +
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

        var visualDamage = vehicle.GetComponent<DodgeRam1500TRXVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<DodgeRam1500TRXVisualDamageController>();
        if (!visualDamageOwners.Contains(visualDamage)) visualDamageOwners.Add(visualDamage);
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        DodgeRam1500TRXDiagnostics.DamageInfo(
            context,
            $"DodgeRam1500TRX damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null)
            return false;
        if (filter.name.StartsWith("DodgeRam1500TRXDamageBody", StringComparison.Ordinal) ||
            filter.name.StartsWith("DodgeRam1500TRX_Lamp_", StringComparison.Ordinal))
            return true;
        if (!HasAncestor(filter.transform, "DodgeRam1500TRXVisual"))
            return false;
        // Mixed source material groups are split by the authoring pass. Only
        // their exterior child meshes are owned; cabin glass and trim stay rigid.
        return filter.name.StartsWith("DodgeRam1500TRXExterior_", StringComparison.Ordinal) ||
               HasAncestor(filter.transform, "Light_Geo") ||
               HasAncestor(filter.transform, "ORANGE_GLASS") ||
               HasAncestor(filter.transform, "RED_GLASS");
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
            SetFloat(clutch, "slipTorque", ClutchSlipTorque);
            SetFloat(clutch, "creepTorque", ClutchCreepTorque);
            SetFloat(clutch, "creepSpeedLimit", 3f);
            SetFloat(GetMember(component, "brakes"), "maxTorque", DodgeRam1500TRXPowertrainProfile.BrakeTorque);
            var engine = GetMember(powertrain, "engine");
            SetFloat(engine, "inertia", EngineInertia);
            SetFloat(engine, "maxPower", EnginePowerKw);
            SetFloat(engine, "engineLossPercent", DodgeRam1500TRXPowertrainProfile.NativeEngineLoss);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateTRXPowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", true);
            SetFloat(forcedInduction, "powerGainMultiplier", 1f);
            SetFloat(forcedInduction, "spoolUpTime", 0.05f);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.22f);
            SetBool(transmission, "variableShiftPoint", false);
            SetFloat(component, "wheelbase", 3.6705f);
            var steering = GetMember(component, "steering");
            SetValue(steering, "steeringWheel", typeof(Transform), null);
            SetFloat(steering, "maximumSteerAngle", DodgeRam1500TRXSteeringProfile.MaximumSteerAngle);
            SetFloat(steering, "degreesPerSecondLimit", DodgeRam1500TRXSteeringProfile.DegreesPerSecond);
            SetValue(steering, "speedSensitiveSteeringCurve", typeof(AnimationCurve), DodgeRam1500TRXSteeringProfile.CreateSpeedCurve());
            // The 8HP95 shifts below the 6,200 RPM limiter while holding the V8's broad low-speed torque.
            SetFloat(transmission, "_downshiftRPM", 3100f);
            SetFloat(transmission, "_upshiftRPM", 5800f);
            SetInt(transmission, "forwardGearCount", 8);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", TRXGears);

            if (GetMember(powertrain, "wheelGroups") is IList wheelGroups)
            {
                foreach (var wheelGroup in wheelGroups)
                    SetFloat(wheelGroup, "antiRollBarForce", AntiRollBarForce);
            }

            var allWheelDriveConfigured = false;
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
                    // Auto mode sends 40 percent of torque to front axle A and 60 percent to rear axle B.
                    SetFloat(differential, "biasAB", 0.4f);
                    allWheelDriveConfigured = true;
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

            var loss = root.GetComponent<DodgeRam1500TRXDrivelineLoss>() ?? root.AddComponent<DodgeRam1500TRXDrivelineLoss>();
            loss.Initialize((PhysicsVehicle)component);
            return GetInt(transmission, "forwardGearCount") == 8 && allWheelDriveConfigured && loss.IsBound;
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

    private static bool SetValue(object? target, string name, Type expectedType, object? value)
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

public sealed class DodgeRam1500TRXCollisionSeparationController : MonoBehaviour
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
            foreach (var collider in transform.GetComponentsInChildren<Collider>(true))
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

[AddComponentMenu("")]
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
internal sealed class DodgeRam1500TRXHighwaySeamGuard : MonoBehaviour
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
internal sealed class DodgeRam1500TRXContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        // Match the proven Revuelto body contact: low friction lets the rigid
        // bodies separate naturally after a crash instead of locking together.
        contactMaterial = new PhysicMaterial("Dodge Ram 1500 TRX body contact")
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
public sealed class DodgeRam1500TRXGlassController : MonoBehaviour
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
                    DodgeRam1500TRXMaterials.GetTransparentRole(renderer, source) !=
                    DodgeRam1500TRXTransparentRole.CabinGlass)
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    DodgeRam1500TRXMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
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
                    DodgeRam1500TRXMaterials.GetTransparentRole(renderer, material) ==
                    DodgeRam1500TRXTransparentRole.CabinGlass)
                {
                    renderer.SetPropertyBlock(null, index);
                    DodgeRam1500TRXMaterials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            DodgeRam1500TRXDiagnostics.Info(
                context,
                $"DodgeRam1500TRX glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            DodgeRam1500TRXDiagnostics.Info(
                context,
                $"DodgeRam1500TRX glass vehicle={GetInstanceID()}: repaired after " +
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
public sealed class DodgeRam1500TRXVisualDamageController : MonoBehaviour
{
    private static readonly MethodInfo? NativeDamageNotification = typeof(CarController).GetMethod("OnVehicleCollision", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
    private bool originalHandlerEnabled;
    private bool ownsNumericalDamage;
    private const float DentRadius = 0.36f;
    private const float MaximumDentDepth = 0.34f;
    private const float MaximumAccumulatedDent = .60f;
    private const float DepthPerExcessMps = 0.011f;
    private const float FrontDentLateralRadius = 1.55f;
    private const float FrontDentVerticalRadius = 1.20f;
    private const float FrontDentLongitudinalRadius = 0.95f;
    private const float MaximumFrontDentDepth = 0.2448f;
    private const float FrontDepthPerExcessMps = 0.00816f;
    private const float RearDentLateralRadius = 1.55f;
    private const float RearDentVerticalRadius = 1.20f;
    private const float RearDentLongitudinalRadius = 1.02f;
    private const float MaximumRearDentDepth = 0.21675f;
    private const float RearDepthPerExcessMps = 0.00663f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.5f;
    private const int MaximumDiagnosticLogs = 6;

    private readonly List<MeshFilter> deformableFilters = new List<MeshFilter>();
    private readonly Dictionary<MeshFilter, Vector3[]> originalVertices =
        new Dictionary<MeshFilter, Vector3[]>();
    private readonly Dictionary<MeshFilter, Mesh> damageMeshes =
        new Dictionary<MeshFilter, Mesh>();
    private readonly Dictionary<MeshFilter, Vector3[]> currentVertices = new Dictionary<MeshFilter, Vector3[]>();
    private readonly Dictionary<MeshFilter, Matrix4x4> filterToVehicleMatrices = new Dictionary<MeshFilter, Matrix4x4>();
    private readonly Dictionary<MeshFilter, Matrix4x4> vehicleToFilterMatrices = new Dictionary<MeshFilter, Matrix4x4>();
    private readonly Dictionary<MeshFilter, Bounds> originalBounds = new Dictionary<MeshFilter, Bounds>();
    private readonly Dictionary<MeshFilter, Bounds> conservativeBounds = new Dictionary<MeshFilter, Bounds>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private readonly Dictionary<Transform, Vector3> originalBeamPositions = new Dictionary<Transform, Vector3>();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private ModContext? context;
    private Rigidbody? body;
    private float impactThresholdMps;
    private float nextCollisionTime;
    private bool hasVisualDamage;
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
        hasVisualDamage = false;
        deformableFilters.Clear();
        originalVertices.Clear();
        damageMeshes.Clear();
        currentVertices.Clear(); filterToVehicleMatrices.Clear(); vehicleToFilterMatrices.Clear();
        originalBounds.Clear(); conservativeBounds.Clear();
        runtimeMeshes.Clear();
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            var runtimeMesh = Instantiate(filter.sharedMesh);
            runtimeMesh.name = filter.sharedMesh.name + "_RuntimeDamage";
            filter.sharedMesh = runtimeMesh;
            deformableFilters.Add(filter);
            var working = runtimeMesh.vertices;
            originalVertices[filter] = (Vector3[])working.Clone();
            currentVertices[filter] = working;
            var matrix = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            filterToVehicleMatrices[filter] = matrix;
            vehicleToFilterMatrices[filter] = matrix.inverse;
            originalBounds[filter] = runtimeMesh.bounds;
            // Lamp inserts may move outside their tiny original bounds. The
            // bounded displacement envelope also safely covers sequential dents.
            var envelope = runtimeMesh.bounds;
            var inverse = matrix.inverse;
            var extent = new Vector3(
                new Vector3(inverse.m00, inverse.m01, inverse.m02).magnitude,
                new Vector3(inverse.m10, inverse.m11, inverse.m12).magnitude,
                new Vector3(inverse.m20, inverse.m21, inverse.m22).magnitude) * MaximumAccumulatedDent;
            envelope.Expand(extent * 2f);
            conservativeBounds[filter] = envelope;
            runtimeMesh.MarkDynamic();
            damageMeshes[filter] = runtimeMesh;
            runtimeMeshes.Add(runtimeMesh);
        }
        originalBeamPositions.Clear();
        foreach (var light in controller.GetComponentsInChildren<Light>(true))
            if (light.name.StartsWith("DodgeRam1500TRX_", StringComparison.Ordinal) && light.name.EndsWith("HeadlightBeam", StringComparison.Ordinal))
                originalBeamPositions.Add(light.transform, light.transform.localPosition);
        originalHandlerEnabled = handler.enabled;
        handler.enabled = false;
        ownsNumericalDamage = true;
        initialized = true;
        ReplaySavedDeformations();
    }

    internal void CheckRepair()
    {
        if (!initialized || damageHandler == null)
            return;

        var currentDamage = damageHandler.Damage;
        var currentSavedDamage = vehicle?.vehicleInstance?.damage ?? 0f;
        if (hasVisualDamage && currentDamage <= 0.001f && currentSavedDamage <= 0.001f)
        {
            RestoreMeshes();
            if (repairRecoveryCoroutine != null)
                StopCoroutine(repairRecoveryCoroutine);
            if (isActiveAndEnabled)
                repairRecoveryCoroutine = StartCoroutine(RestoreDrivingStateAfterRepair());
            DodgeRam1500TRXDiagnostics.DamageInfo(
                context,
                $"DodgeRam1500TRX damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
        }
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

    private void OnCollisionEnter(Collision collision) => HandleImpact(collision);
    private void OnCollisionStay(Collision collision) => HandleImpact(collision);

    internal static bool HasEffectiveImpulse(float impulse, float mass) =>
        !float.IsNaN(impulse) && !float.IsInfinity(impulse) && mass > 0f && impulse / mass >= .1f;

    internal static bool IsEligibleSurface(bool nativeValid, string objectName, float horizontalNormal)
        => nativeValid || (objectName.StartsWith("Tree ", StringComparison.OrdinalIgnoreCase) && horizontalNormal >= .5f);

    private void HandleImpact(Collision collision)
    {
        if (!initialized || !ownsNumericalDamage || vehicle?.vehicleInstance == null || collision == null || damageHandler == null || body == null ||
            Time.unscaledTime < nextCollisionTime || collision.relativeVelocity.magnitude < impactThresholdMps ||
            !HasEffectiveImpulse(collision.impulse.magnitude, body.mass) ||
            (SaveGameManager.Current != null && SaveGameManager.Current.gameVariables.disableVehicleDamage)) return;
        var count = collision.contactCount;
        if (count == 0 || collision.collider == null || collision.collider.isTrigger) return;
        var horizontal = 0f;
        for (var i = 0; i < count; i++) horizontal = Mathf.Max(horizontal, Vector3.ProjectOnPlane(collision.GetContact(i).normal, Vector3.up).magnitude);
        var nativeValid = NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision);
        if (!IsEligibleSurface(nativeValid, collision.collider.name, horizontal)) return;
        // Use the game's exact damage equation and part-damage handling. The
        // native automatic receiver is suspended, so only effective impacts
        // consume a cooldown; zero-impulse compound callbacks cannot hide a hit.
        var before = damageHandler.Damage;
        if (!damageHandler.Enqueue(collision, collision.relativeVelocity.magnitude * 100f)) return;
        vehicle.vehicleInstance.damage = damageHandler.Damage;
        damageHandler.lastCollision = collision;
        damageHandler.lastCollisionTime = Time.realtimeSinceStartup;
        nextCollisionTime = Time.unscaledTime + CollisionCooldown;
        DodgeRam1500TRXDiagnostics.DamageInfo(context,
            $"DodgeRam1500TRX impact vehicle={vehicle.GetInstanceID()} other={collision.collider.name} nativeValid={nativeValid} impulse={collision.impulse.magnitude:0.0} damage={before:0.000}->{damageHandler.Damage:0.000}.");
        var points = new VehicleDeformationController.VehicleDeformation.VehicleDeformationPoint[count];
        for (var i = 0; i < count; i++)
        {
            var contact = collision.GetContact(i);
            points[i] = new VehicleDeformationController.VehicleDeformation.VehicleDeformationPoint
            {
                point = transform.InverseTransformPoint(contact.point),
                normal = transform.InverseTransformDirection(contact.normal),
                deformationRandomness = 1f
            };
        }
        var record = new VehicleDeformationController.VehicleDeformation
        {
            decelerationMagnitude = collision.relativeVelocity.magnitude * 100f,
            points = points
        };
        var saved = vehicle.vehicleInstance.deformations;
        saved.Add(record);
        if (saved.Count > 20)
        {
            saved.RemoveRange(0, saved.Count - 20);
            RestoreMeshes();
            ReplaySavedDeformations();
        }
        else ApplySavedDeformation(record, collision.collider?.name ?? "collision");
        // CarController already schedules its notification for native-valid
        // contacts. Trees excluded by the broad ground-layer filter need the
        // same broken-car notification and vehicle-variable event explicitly.
        if (!nativeValid && Application.isPlaying && vehicle is CarController)
        {
            try { NativeDamageNotification?.Invoke(vehicle, null); }
            catch (Exception exception)
            {
                if (!failureReported)
                {
                    failureReported = true;
                    context?.Logger.Warn($"DodgeRam1500TRX impact notification: {exception.GetBaseException().Message}");
                }
            }
        }
    }

    private void RestoreMeshes()
    {
        foreach (var pair in originalVertices)
        {
            if (pair.Key == null || !damageMeshes.TryGetValue(pair.Key, out var mesh) || mesh == null) continue;
            Array.Copy(pair.Value, currentVertices[pair.Key], pair.Value.Length);
            pair.Key.sharedMesh = mesh; mesh.vertices = currentVertices[pair.Key]; mesh.bounds = originalBounds[pair.Key];
        }
        foreach (var anchor in originalBeamPositions)
            if (anchor.Key != null) anchor.Key.localPosition = anchor.Value;
        hasVisualDamage = false;
    }

    private void ReplaySavedDeformations()
    {
        var records = vehicle?.vehicleInstance?.deformations;
        if (records == null) return;
        foreach (var record in records)
            ApplySavedDeformation(record, "saved-impact");
        DodgeRam1500TRXDiagnostics.DamageInfo(context,
            $"DodgeRam1500TRX damage vehicle={vehicle?.GetInstanceID()}: restored {records.Count} saved impacts.");
    }

    private readonly struct ImpactContact
    {
        internal readonly Vector3 point, inward, inverseRadiusSquared;
        internal readonly float radiusSquared;
        internal readonly bool end, front;
        internal ImpactContact(Vector3 point, Vector3 normal, Vector3 center)
        {
            this.point = point;
            end = Mathf.Abs(point.z) >= EndContactMinimumLongitudinalOffset && Mathf.Abs(point.z) > Mathf.Abs(point.x);
            front = end && point.z >= 0f;
            if (end)
            {
                var lateral = front ? FrontDentLateralRadius : RearDentLateralRadius;
                var vertical = front ? FrontDentVerticalRadius : RearDentVerticalRadius;
                var longitudinal = front ? FrontDentLongitudinalRadius : RearDentLongitudinalRadius;
                inverseRadiusSquared = new Vector3(1f / (lateral * lateral), 1f / (vertical * vertical), 1f / (longitudinal * longitudinal));
                var radius = Mathf.Max(lateral, Mathf.Max(vertical, longitudinal));
                radiusSquared = radius * radius;
                inward = front ? Vector3.back : Vector3.forward;
            }
            else
            {
                inverseRadiusSquared = Vector3.zero;
                radiusSquared = DentRadius * DentRadius;
                normal.Normalize();
                inward = Vector3.Dot(normal, center - point) >= 0f ? normal : -normal;
            }
        }
    }

    private void ApplySavedDeformation(VehicleDeformationController.VehicleDeformation record, string contactName)
    {
        if (record?.points == null || record.points.Length == 0 ||
            float.IsNaN(record.decelerationMagnitude) || float.IsInfinity(record.decelerationMagnitude)) return;
        var speed = Mathf.Clamp(record.decelerationMagnitude / 100f, 0f, 200f);
        if (speed < impactThresholdMps) return;
        try
        {
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            var localCenter = body != null ? transform.InverseTransformPoint(body.worldCenterOfMass) : Vector3.zero;
            // The native saved contacts already use vehicle coordinates. Prepare
            // directions/radii once, as in the Amarok, outside every vertex loop.
            var contacts = Array.ConvertAll(record.points, point => new ImpactContact(point.point, point.normal, localCenter));
            var excessSpeed = speed - impactThresholdMps;
            var dentDepth = Mathf.Clamp(excessSpeed * DepthPerExcessMps, 0.025f, MaximumDentDepth);
            var frontDentDepth = Mathf.Clamp(
                excessSpeed * FrontDepthPerExcessMps,
                0.0272f,
                MaximumFrontDentDepth);
            var rearDentDepth = Mathf.Clamp(
                excessSpeed * RearDepthPerExcessMps,
                0.0204f,
                MaximumRearDentDepth);
            var primaryLocalContact = contacts[0].point;
            var skippedMeshes = 0;
            var changedMeshes = 0;
            var changedVertices = 0;
            var frontImpact = false;
            var rearImpact = false;

            foreach (var filter in deformableFilters)
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;
                var mesh = filter.sharedMesh;
                var vertices = currentVertices[filter];
                var matrix = filterToVehicleMatrices[filter];
                var inverse = vehicleToFilterMatrices[filter];
                // Reject distant parts before visiting their vertices. Transform
                // contacts once per mesh, avoiding Unity Transform calls per vertex.
                var reachable = false;
                var bounds = mesh.bounds;
                foreach (var contact in contacts)
                {
                    var contactInMesh = inverse.MultiplyPoint3x4(contact.point);
                    var maximumScale = Mathf.Max(inverse.GetColumn(0).magnitude,
                        Mathf.Max(inverse.GetColumn(1).magnitude, inverse.GetColumn(2).magnitude));
                    if (bounds.SqrDistance(contactInMesh) <= contact.radiusSquared * maximumScale * maximumScale)
                    { reachable = true; break; }
                }
                if (!reachable) { skippedMeshes++; continue; }
                var meshChanged = false;
                for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    var vertex = matrix.MultiplyPoint3x4(vertices[vertexIndex]);
                    var displacement = CalculateDisplacement(vertex, contacts, dentDepth, frontDentDepth, rearDentDepth, out var vertexFrontImpact, out var vertexRearImpact);
                    if (displacement.sqrMagnitude < 1e-12f) continue;
                    var baseline = matrix.MultiplyPoint3x4(originalVertices[filter][vertexIndex]);
                    vertex = baseline + Vector3.ClampMagnitude(vertex + displacement - baseline,
                        MaximumAccumulatedDent * (vertexRearImpact ? .6375f : vertexFrontImpact ? .68f : 1f));
                    vertices[vertexIndex] = inverse.MultiplyPoint3x4(vertex);
                    changedVertices++;
                    meshChanged = true;
                    frontImpact |= vertexFrontImpact;
                    rearImpact |= vertexRearImpact;
                }

                if (!meshChanged)
                    continue;
                mesh.vertices = vertices;
                // Prepared envelope covers every allowed dent, including tiny
                // lamp parts. No complete RecalculateBounds pass during a crash.
                mesh.bounds = conservativeBounds[filter];
                changedMeshes++;
            }

            foreach (var anchor in originalBeamPositions)
            {
                if (anchor.Key == null) continue;
                var originalWorld = anchor.Key.parent.TransformPoint(anchor.Value);
                var displacement = transform.TransformVector(CalculateDisplacement(transform.InverseTransformPoint(anchor.Key.position), contacts, dentDepth, frontDentDepth, rearDentDepth, out var frontAnchor, out var rearAnchor));
                anchor.Key.position = originalWorld + Vector3.ClampMagnitude(anchor.Key.position + displacement - originalWorld, MaximumAccumulatedDent * (rearAnchor ? .6375f : frontAnchor ? .68f : 1f));
            }
            hasVisualDamage |= changedMeshes > 0;
            if (DodgeRam1500TRXDiagnostics.DebugEnabled && DodgeRam1500TRXDiagnostics.DamageDebugEnabled && diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                DodgeRam1500TRXDiagnostics.DamageInfo(
                    context,
                    $"DodgeRam1500TRX damage vehicle={vehicle?.GetInstanceID()}: inward dent " +
                    $"contact='{contactName}' " +
                    $"relativeSpeed={speed * 3.6f:0.0}kph " +
                    $"localContact=({primaryLocalContact.x:0.00}," +
                    $"{primaryLocalContact.y:0.00},{primaryLocalContact.z:0.00}) " +
                    $"region={(frontImpact ? "front" : rearImpact ? "rear" : "side")} " +
                    $"depth={(frontImpact ? frontDentDepth : rearImpact ? rearDentDepth : dentDepth):0.000}m " +
                    $"meshes={changedMeshes} skipped={skippedMeshes} vertices={changedVertices} processing={(System.Diagnostics.Stopwatch.GetTimestamp() - startedAt) * 1000d / System.Diagnostics.Stopwatch.Frequency:0.0}ms " +
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
                $"DodgeRam1500TRX damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static Vector3 CalculateDisplacement(Vector3 vertex, ImpactContact[] contacts, float dentDepth, float frontDentDepth, float rearDentDepth, out bool frontImpact, out bool rearImpact)
    {
        frontImpact = rearImpact = false;
        var strongest = 0f;
        var direction = Vector3.zero;
        var depth = dentDepth;
        var selectedEnd = false;
        foreach (var contact in contacts)
        {
            var delta = vertex - contact.point;
            var distanceSquared = contact.end
                ? delta.x * delta.x * contact.inverseRadiusSquared.x +
                  delta.y * delta.y * contact.inverseRadiusSquared.y +
                  delta.z * delta.z * contact.inverseRadiusSquared.z
                : delta.sqrMagnitude / contact.radiusSquared;
            if (distanceSquared >= 1f) continue;
            var influence = 1f - Mathf.Sqrt(distanceSquared);
            if (influence <= strongest) continue;
            strongest = influence;
            direction = contact.inward;
            selectedEnd = contact.end;
            frontImpact = contact.front;
            rearImpact = contact.end && !contact.front;
            depth = contact.end ? contact.front ? frontDentDepth : rearDentDepth : dentDepth;
        }
        if (strongest <= 0f || direction.sqrMagnitude < .5f) return Vector3.zero;
        var falloff = selectedEnd ? Mathf.Pow(strongest, 1.35f) : strongest * strongest;
        return direction * (depth * falloff);
    }

    internal void ReleaseNumericalOwnership()
    {
        if (ownsNumericalDamage && damageHandler != null) damageHandler.enabled = originalHandlerEnabled;
        ownsNumericalDamage = false;
        initialized = false;
    }

    private void OnDestroy()
    {
        ReleaseNumericalOwnership();
        if (repairRecoveryCoroutine != null)
            StopCoroutine(repairRecoveryCoroutine);
        repairRecoveryCoroutine = null;
        foreach (var mesh in runtimeMeshes)
            if (mesh != null) Destroy(mesh);
        runtimeMeshes.Clear();
        damageMeshes.Clear();
    }
}


// The installed NWH steering code evaluates its speed curves at |speed in m/s| / 50.
public static class DodgeRam1500TRXSteeringProfile
{
    public const float MaximumSteerAngle = 35f;
    public const float DegreesPerSecond = 160f;
    public static AnimationCurve CreateSpeedCurve() => new AnimationCurve(
        new Keyframe(0f, 1f, 0f, 0f),
        new Keyframe(25f / 180f, 1f, 0f, 0f),
        new Keyframe(35f / 180f, .80f, -2f, -2f),
        new Keyframe(.3f, .50f, -.6f, -.6f),
        new Keyframe(1f, .25f, -.1f, 0f));
}

public static class DodgeRam1500TRXBodyFit
{
    // Pitch the body around the front axle; controller/wheel assemblies remain in their authored locations.
    public static readonly Vector3 FrontPivot = new Vector3(0f, .4295f, 1.93775f);
    public const float EffectiveWheelbase = 3.6705f;
    public const float AdditionalRearDrop = .02f;
    public static float RakeDegrees => -Mathf.Atan(AdditionalRearDrop / EffectiveWheelbase) * Mathf.Rad2Deg;
    public static Quaternion RakeRotation => Quaternion.Euler(RakeDegrees, 0f, 0f);
    public static Vector3 RakePoint(Vector3 point) => FrontPivot + RakeRotation * (point - FrontPivot);
    public static Vector3 UnrakePoint(Vector3 point) => FrontPivot + Quaternion.Inverse(RakeRotation) * (point - FrontPivot);
}

// The published peaks are 882 Nm at 4800 RPM and 523 kW at 6100 RPM.
// Intermediate torque values are simulation calibration, not a measured dyno trace.
public static class DodgeRam1500TRXPowertrainProfile
{
    // 2026-10-03 CSV: 70 mph -> 0.5 km/h in 48.86 m at 3500 Nm.
    // C/D's 189 ft real stop / 1.25 = 46.09 m. Compensate for added quadratic drag.
    public const float BrakeTorque = 4531.25f;
    // Retain the native off-throttle engine braking from the measured setup.
    public const float NativeEngineLoss = .57f;
    // Residual driveline calibration from the straight 20261003-210737 CSV.
    // This is a simulation correction, not a measured factory AWD efficiency.
    public const float DrivePowerLoss = .055f;
    public static float DrivePowerModifier(float nativeEngineLoss) =>
        1f - DrivePowerLoss / (1f + Mathf.Max(0f, nativeEngineLoss));
    // Simulation calibration against the CSV and C/D's 100-mph test;
    // this Cd*A is not a published wind-tunnel measurement of the TRX.
    public const float DragArea = 2.25f;
    public static float AerodynamicForce(float speedMps) => .5f * 1.225f * DragArea * speedMps * speedMps;
    public static AnimationCurve CreatePowerCurve()
    {
        var rpm = new[] { 0f, 700f, 1500f, 2500f, 3500f, 4800f, 5500f, 6100f, 6200f };
        var torque = new[] { 0f, 470f, 620f, 760f, 820f, 882f, 860f, 523000f / (6100f * Mathf.PI / 30f), 790f };
        var keys = new Keyframe[rpm.Length];
        for (var i = 0; i < keys.Length; i++)
            keys[i] = new Keyframe(rpm[i] / 6200f, torque[i] * rpm[i] * Mathf.PI / 30f / 523000f);
        // Linear power interpolation avoids Hermite overshoot and spurious low-RPM torque.
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i].inTangent = i == 0 ? 0f : (keys[i].value - keys[i - 1].value) / (keys[i].time - keys[i - 1].time);
            keys[i].outTangent = i == keys.Length - 1 ? 0f : (keys[i + 1].value - keys[i].value) / (keys[i + 1].time - keys[i].time);
        }
        return new AnimationCurve(keys);
    }
}


[AddComponentMenu("")]
public sealed class DodgeRam1500TRXAerodynamicResistance : MonoBehaviour
{
    private Rigidbody? body;
    internal void Initialize(Rigidbody? vehicleBody) => body = vehicleBody;
    private void FixedUpdate()
    {
        if (body == null || body.isKinematic) return;
        var velocity = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
        var speed = velocity.magnitude;
        if (speed < .1f) return;
        body.AddForce(-velocity / speed * DodgeRam1500TRXPowertrainProfile.AerodynamicForce(speed), ForceMode.Force);
    }
}


[AddComponentMenu("")]
public sealed class DodgeRam1500TRXDrivelineLoss : MonoBehaviour
{
    private PhysicsVehicle? physics;
    private NWH.VehiclePhysics2.Powertrain.EngineComponent? engine;
    private NWH.VehiclePhysics2.Powertrain.EngineComponent.PowerModifier? modifier;
    public bool IsBound => engine != null && modifier != null && engine.powerModifiers.Contains(modifier);

    internal void Initialize(PhysicsVehicle controller)
    {
        Detach();
        physics = controller;
        engine = controller.powertrain?.engine ?? throw new InvalidOperationException("TRX driveline loss requires the native engine.");
        modifier = CalculateModifier;
        enabled = true;
        Attach();
    }

    // Called by native physics when it calculates propulsion. At full throttle,
    // NWH generates P*(1+engineLoss)*modifier then subtracts P*engineLoss.
    // Compensating that gross-power factor leaves precisely P*(1-DrivePowerLoss).
    // No road-speed scaling, extra drag, or alteration of off-throttle braking.
    private float CalculateModifier() => physics == null || physics.input.InputSwappedThrottle <= 0f
        ? 1f
        : DodgeRam1500TRXPowertrainProfile.DrivePowerModifier(engine!.engineLossPercent);

    private void Attach()
    {
        if (engine != null && modifier != null && !engine.powerModifiers.Contains(modifier))
            engine.powerModifiers.Add(modifier);
    }

    private void Detach()
    {
        if (engine != null && modifier != null) engine.powerModifiers.Remove(modifier);
    }

    internal void Shutdown()
    {
        Detach();
        enabled = false;
    }

    private void OnEnable() => Attach();
    private void OnDisable() => Detach();
    private void OnDestroy() => Detach();
}
