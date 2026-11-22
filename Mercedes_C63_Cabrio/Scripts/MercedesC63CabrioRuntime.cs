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

public sealed class MercedesC63CabrioRuntime : MonoBehaviour
{
    private const float VehicleMass = 1909f;
    private const float EnginePowerKw = 350f;
    private const float EngineIdleRpm = 900f;
    private const float EngineLimitRpm = 7000f;
    private const float SpeedLimitKph = 250f;
    private const float FinalDriveRatio = 3.06f;
    private const float EngineInertia = MercedesC63CabrioTuning.EngineInertia;
    private const float EngineStartDuration = 0.38f;
    // AMG's 7-speed MCT uses a wet launch clutch rather than a dual-clutch PDK.
    private const float ClutchEngagementRpm = MercedesC63CabrioTuning.ClutchEngagementRpm;
    private const float ClutchThrottleOffsetRpm = MercedesC63CabrioTuning.ClutchThrottleOffsetRpm;
    private const float ClutchEngagementRange = MercedesC63CabrioTuning.ClutchEngagementRange;
    private const float ClutchCreepTorque = 0f;
    private const float TurboSpoolUpTime = 0.35f;
    private const float TireFrictionCircleStrength = 1.02f;
    private const float AntiRollBarForce = 8200f;
    private const float FrontSuspensionTravel = 0.090f;
    private const float RearSuspensionTravel = 0.095f;
    private const float FrontTireRadius = 0.3306f;
    private const float RearTireRadius = 0.32835f;
    private const float FrontTireWidth = 0.255f;
    private const float RearTireWidth = 0.285f;
    private const float VehicleLinearDrag = 0.045f;
    private const float FrontForwardGrip = MercedesC63CabrioTuning.FrontGrip;
    private const float RearForwardGrip = MercedesC63CabrioTuning.RearGrip;
    private const float FrontForwardStiffness = MercedesC63CabrioTuning.TireStiffness;
    private const float RearForwardStiffness = MercedesC63CabrioTuning.TireStiffness;
    private const float DeformationStrength = 0.17f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 1f;
    // Native NWH scales relative speed by 100. Match the Audi/Porsche 2 m/s
    // percentage-damage threshold without changing the accepted cosmetic dents.
    private const float DamageDecelerationThreshold = 200f;
    private const float VisualImpactThresholdMps = 5f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private const float WarehouseExitGuardDuration = 8f;
    private const float WarehouseExitGuardClearDistance = 4f;
    private static readonly Vector3 StableCenterOfMass = MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.10f, 0.14f));
    private static readonly Vector3 FrontContactColliderCenter =
        MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.61f, 1.58f));
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.78f, 0.50f, 1.08f);
    private static readonly Vector3 RearContactColliderCenter =
        MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.60f, -1.78f));
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.78f, 0.50f, 1.02f);
    private static readonly Vector3 DriverExitPosition =
        new Vector3(-2.10f, 0.20f, 0.15f);
    private static readonly Vector3 PassengerExitPosition =
        new Vector3(2.10f, 0.20f, 0.15f);

    private static readonly float[] C63Gears =
    {
        -3.42f, 0f, 4.38f, 2.86f, 1.92f, 1.37f, 1.00f, 0.82f, 0.73f,
    };

    private static AnimationCurve CreateC63PowerCurve()
    {
        // NWH consumes normalized POWER, not torque. P[kW] = T[Nm] * rpm / 9549.3.
        // The manufacturer's 650 Nm plateau spans 1750-4500 rpm; peak power is
        // 350 kW at 5500-6250 rpm. Linear tangents avoid torque overshoot.
        var keys = new[] {
            new Keyframe(0f, 0f), new Keyframe(900f / 7000f, 0.10f),
            new Keyframe(1750f / 7000f, 650f * 1750f / 9549.3f / 350f),
            new Keyframe(4500f / 7000f, 650f * 4500f / 9549.3f / 350f),
            new Keyframe(5500f / 7000f, 1f), new Keyframe(6250f / 7000f, 1f),
            new Keyframe(1f, 0.93f) };
        for (var i = 0; i < keys.Length; i++)
        {
            if (i > 0) keys[i].inTangent = (keys[i].value - keys[i-1].value) / (keys[i].time - keys[i-1].time);
            if (i + 1 < keys.Length) keys[i].outTangent = (keys[i+1].value - keys[i].value) / (keys[i+1].time - keys[i].time);
        }
        return new AnimationCurve(keys);
    }

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private int variableEventTraceCount;
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private int enteredVehicleActivationInstanceId;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private MercedesC63CabrioWarehouseEntryController? warehouseExitGuardEntryController;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private bool dealerRegistrationReady;
    private bool dealerReadyLogged;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;
    private GameObject? playerVehiclePrefab;

    public static MercedesC63CabrioRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<MercedesC63CabrioRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(MercedesC63CabrioRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<MercedesC63CabrioRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        MercedesC63CabrioPrivateDriverSupport.SetContext(context);
        var trafficFrequency = runtime.GetComponent<MercedesC63CabrioTrafficFrequency>() ??
                               runtime.gameObject.AddComponent<MercedesC63CabrioTrafficFrequency>();
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
        MercedesC63CabrioPrivateDriverSupport.RemoveVehicle(vehicleTypeName);
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

        if (MercedesC63CabrioDiagnostics.DebugEnabled && MercedesC63CabrioDiagnostics.DealerEntryDebugEnabled)
            MercedesC63CabrioDiagnostics.DealerEntryInfo(
            context,
            $"MercedesC63Cabrio dealer-entry: game-event='{eventName}', " +
            DescribeVehicleState(selectedVehicle));

        // Dealer purchases do not reliably invoke onEnterVehicle. This event
        // is a bounded lifecycle handoff from the dealer display vehicle to
        // the owned, player-controlled instance, so configure it here rather
        // than scanning vehicles every frame.
        TryConfigureVehicle(selectedVehicle);
        selectedVehicle?.GetComponent<MercedesC63CabrioVisualDamageController>()?.CheckForCompletedRepair();
        selectedVehicle!
            .GetComponent<MercedesC63CabrioPaintController>()
            ?.ApplyCurrentColor("game-event");
    }

    private void HandleVehicleVariablesChanged()
    {
        // Fuel, mileage and damage notifications arrive during driving. They
        // must not reload integration services or search the entire scene.
        // The native owned-vehicle collection also covers dealer purchases.
        var trace = MercedesC63CabrioDiagnostics.PerformanceEnabled && variableEventTraceCount < 12;
        var started = trace ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
        ConfigureExistingVehicles(out var matched);
        if (trace)
        {
            variableEventTraceCount++;
            var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000d /
                          System.Diagnostics.Stopwatch.Frequency;
            MercedesC63CabrioDiagnostics.PerformanceInfo(context,
                $"MercedesC63Cabrio driving variables: matched={matched}, elapsedMs={elapsed:0.000}, " +
                $"sample={variableEventTraceCount}/12, sceneSearch=false, registrationRetry=false.");
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SubscribeEvents();
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeEvents();
        if (MercedesC63CabrioLoadRecovery.CompleteInterruptedLoad(context))
            StartCoroutine(ReportLoadedInputState());
        privateDriverRegistrationAllowed = true;
        ScheduleInitialization("game-loaded-late");
    }

    private IEnumerator ReportLoadedInputState()
    {
        // The native loading screen fades out for 0.8 seconds after this event.
        yield return new WaitForSecondsRealtime(2f);
        MercedesC63CabrioLoadRecovery.ReportInputState(context);
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
        dealerRegistrationReady = false;
        dealerReadyLogged = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        if (vehicle == null || !IsTargetVehicle(vehicle)) return;
        MercedesC63CabrioDiagnostics.DealerEntryInfo(
            context,
            $"MercedesC63Cabrio dealer-entry: onEnterVehicle, {DescribeVehicleState(vehicle)}");
        TryConfigureVehicle(vehicle);
        vehicle.GetComponent<MercedesC63CabrioGlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle == null || !IsTargetVehicle(vehicle))
            return;
        vehicle.GetComponent<MercedesC63CabrioPaintController>()
            ?.ApplyCurrentColor("vehicle-entered");
        ScheduleEnteredVehicleActivation(vehicle);
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;
        MercedesC63CabrioDiagnostics.WarehouseExitInfo(
            context,
            $"MercedesC63Cabrio warehouse-exit: onExitVehicle, {DescribeVehicleState(vehicle)}");
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
        MercedesC63CabrioDiagnostics.WarehouseExitInfo(
            context,
            $"MercedesC63Cabrio player-exit: deferred validation playerPosition={root.position}, " +
            $"agentCount={agents.Length}, anyAgentOffNavMesh={agents.Any(agent => agent != null && agent.enabled && !agent.isOnNavMesh)}, " +
            $"initialClear={initialExitClear}, needsRecovery={needsRecovery}.");
        if (!needsRecovery)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }
        if (!TryFindClearExitPosition(root, exitedVehicle, out var target))
        {
            MercedesC63CabrioDiagnostics.WarehouseExitInfo(
                context,
                "MercedesC63Cabrio player-exit: recovery failed; no NavMesh/capsule-clear candidate found.");
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
        MercedesC63CabrioDiagnostics.WarehouseExitInfo(
            context,
            $"MercedesC63Cabrio player-exit: recovery moved player to {target}.");
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
            MercedesC63CabrioDiagnostics.DealerEntryInfo(
                context,
                $"MercedesC63Cabrio dealer-entry: activation skipped because the vehicle is " +
                $"neither controlled nor selected, {DescribeVehicleState(vehicle)}");
            return;
        }

        var instanceId = vehicle.GetInstanceID();
        if (enteredVehicleActivationCoroutine != null &&
            enteredVehicleActivationInstanceId == instanceId)
        {
            MercedesC63CabrioDiagnostics.DealerEntryInfo(
                context,
                $"MercedesC63Cabrio dealer-entry: activation already pending instance={instanceId}.");
            return;
        }

        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);

        enteredVehicleActivationInstanceId = instanceId;
        MercedesC63CabrioDiagnostics.DealerEntryInfo(
            context,
            $"MercedesC63Cabrio dealer-entry: activation scheduled, {DescribeVehicleState(vehicle)}");
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
            MercedesC63CabrioDiagnostics.DealerEntryInfo(
                context,
                $"MercedesC63Cabrio dealer-entry: activation failed; NWH controller missing, " +
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
                MercedesC63CabrioDiagnostics.DealerEntryInfo(
                    context,
                    $"MercedesC63Cabrio dealer-entry: activation cancelled attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }

            // Paint assignment from the dealer is asynchronous too; retain a
            // bounded entry refresh without turning it into runtime polling.
            vehicle.GetComponent<MercedesC63CabrioDriverController>()?.RefreshOccupancy();
            vehicle.GetComponent<MercedesC63CabrioPaintController>()
                ?.ApplyCurrentColor("vehicle-entered");
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            MercedesC63CabrioDiagnostics.DealerEntryInfo(
                context,
                $"MercedesC63Cabrio dealer-entry: activation attempt={attempt + 1}, " +
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
                    MercedesC63CabrioDiagnostics.DealerEntryInfo(
                        context,
                        "MercedesC63Cabrio dealer-entry: native engine healthy; shifted neutral to first.");
                    yield return new WaitForFixedUpdate();
                    MercedesC63CabrioDiagnostics.DealerEntryInfo(
                        context,
                        $"MercedesC63Cabrio dealer-entry: first-gear settle, gear={transmission.Gear}, " +
                        $"ratio={transmission.currentGearRatio:0.000}.");
                }
                else
                {
                    MercedesC63CabrioDiagnostics.DealerEntryInfo(
                        context,
                        $"MercedesC63Cabrio dealer-entry: native engine healthy; preserved gear={transmission.Gear}.");
                }
                break;
            }

            MercedesC63CabrioDiagnostics.DealerEntryInfo(
                context,
                $"MercedesC63Cabrio dealer-entry: dormant powertrain; restart attempt={attempt + 1}.");
            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
            {
                MercedesC63CabrioDiagnostics.DealerEntryInfo(
                    context,
                    $"MercedesC63Cabrio dealer-entry: restart aborted after stop attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }
            engine.StartEngine();
            yield return new WaitForSecondsRealtime(.75f);
            if (vehicle != null && vehicle.controlledByPlayer &&
                transmission.Gear == 0)
            {
                transmission.ShiftInto(1, true);
                MercedesC63CabrioDiagnostics.DealerEntryInfo(
                    context,
                    $"MercedesC63Cabrio dealer-entry: restart attempt={attempt + 1} shifted neutral to first.");
            }
            yield return new WaitForSecondsRealtime(.15f);
        }

        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        MercedesC63CabrioDiagnostics.DealerEntryInfo(
            context,
            $"MercedesC63Cabrio dealer-entry: activation completed, {DescribeVehicleState(vehicle)}");
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!MercedesC63CabrioLuxuryDealerStock.IsTargetDealer(registration?.BusinessName) ||
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
            MercedesC63CabrioDiagnostics.WarehouseExitInfo(
                context,
                $"MercedesC63Cabrio warehouse-exit: guard skipped; current vehicle is not " +
                $"a controlled Mercedes-AMG C63 Cabriolet ({DescribeVehicleState(vehicle)}).");
            return;
        }

        var entrance = FindClosestDriveInEntrance(vehicle.transform.position);
        if (entrance == null)
        {
            MercedesC63CabrioDiagnostics.WarehouseExitInfo(
                context,
                $"MercedesC63Cabrio warehouse-exit: guard skipped; no DriveInEntrance found, " +
                DescribeVehicleState(vehicle));
            return;
        }

        var nativeMeshCollider = vehicle.GetComponentInChildren<MeshCollider>(true);
        MercedesC63CabrioDiagnostics.WarehouseExitInfo(
            context,
            $"MercedesC63Cabrio warehouse-exit: native exit completed, {DescribeVehicleState(vehicle)} " +
            $"entrance='{entrance.name}' entrancePosition={entrance.transform.position}, " +
            $"nativeMesh='{nativeMeshCollider?.name ?? "missing"}', " +
            $"nativeMeshLength={nativeMeshCollider?.sharedMesh?.bounds.size.z:0.000}.");

        StopWarehouseExitGuard();
        warehouseExitGuardEntryController =
            vehicle.GetComponent<MercedesC63CabrioWarehouseEntryController>();
        warehouseExitGuardEntryController?.SuppressEntrance(entrance, "warehouse-exit-guard");
        foreach (var enterTrigger in entrance.GetComponentsInChildren<DriveInEntranceEnterTrigger>(true))
        foreach (var collider in enterTrigger.GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled || !collider.isTrigger)
                continue;

            warehouseExitGuardColliders.Add(collider);
            MercedesC63CabrioDiagnostics.WarehouseExitInfo(
                context,
                $"MercedesC63Cabrio warehouse-exit: disabling entry trigger " +
                $"'{collider.name}' bounds={collider.bounds}.");
            collider.enabled = false;
        }

        if (warehouseExitGuardColliders.Count == 0)
        {
            warehouseExitGuardEntryController?.ClearSuppressedEntrance(
                entrance,
                "no-native-entry-trigger");
            warehouseExitGuardEntryController = null;
            MercedesC63CabrioDiagnostics.WarehouseExitInfo(
                context,
                "MercedesC63Cabrio warehouse-exit: guard skipped; matching entrance had no enabled trigger colliders.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(
            vehicle.transform.position - entrance.transform.position,
            Vector3.up);
        if (outward.sqrMagnitude < .0001f)
            outward = Vector3.ProjectOnPlane(entrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < .0001f)
        {
            MercedesC63CabrioDiagnostics.WarehouseExitInfo(
                context,
                "MercedesC63Cabrio warehouse-exit: guard aborted; outward direction was zero.");
            StopWarehouseExitGuard();
            return;
        }

        outward.Normalize();
        var startingDistance = Vector3.Dot(vehicle.transform.position, outward);
        Physics.SyncTransforms();
        MercedesC63CabrioDiagnostics.WarehouseExitInfo(
            context,
            $"MercedesC63Cabrio warehouse-exit: guard started triggerCount={warehouseExitGuardColliders.Count}, " +
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
        MercedesC63CabrioDiagnostics.WarehouseExitInfo(
            context,
            $"MercedesC63Cabrio warehouse-exit: guard ending reason={reason}, " +
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
            MercedesC63CabrioDiagnostics.WarehouseExitInfo(
                context,
                $"MercedesC63Cabrio warehouse-exit: restoring triggerCount={warehouseExitGuardColliders.Count}, " +
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

        TryFinishRegistrations("full-menu");
        ConfigureExistingVehicles(out _);
    }

    private void ScheduleInitialization(string source)
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(InitializeForLifecycle(source));
    }

    private IEnumerator InitializeForLifecycle(string source)
    {
        // Lifecycle callbacks can arrive while the game is still completing a
        // scene transition. Defer one frame, then perform a single event-driven
        // registration/configuration pass.
        yield return null;
        initializationCoroutine = null;

        if (!privateDriverPoolReady && playerVehiclePrefab != null)
            privateDriverPoolReady = TryPreparePrivateDriverPool(source);
        TryFinishRegistrations(source);
        var dealerReady = dealerRegistrationReady;
        if (privateDriverRegistrationAllowed)
            EnsurePrivateDriverSupport(source);
        ConfigureExistingVehicles(out var matchedCount);

        if (!dealerReady && !BusinessLayoutSetHelper.loadingLayouts)
            context?.Logger.Warn(
                $"MercedesC63Cabrio: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={matchedCount}.");
        if (privateDriverRegistrationAllowed && !privateDriverReady)
            context?.Logger.Warn(
                $"MercedesC63Cabrio: private-driver support not ready source='{source}'.");
        if (privateDriverRegistrationAllowed && !privateDriverPoolReady)
            context?.Logger.Warn(
                $"MercedesC63Cabrio: private-driver traffic pool not ready source='{source}'.");
    }
    private void TryFinishRegistrations(string source)
    {
        if (initializationCoroutine != null)
            return;
        if (!privateDriverPoolReady && playerVehiclePrefab != null)
            privateDriverPoolReady = TryPreparePrivateDriverPool(source);
        if (!dealerRegistrationReady && !BusinessLayoutSetHelper.loadingLayouts)
            EnsureDealerStock(source);
        if (privateDriverRegistrationAllowed && (!privateDriverReady || !privateDriverPoolReady))
            EnsurePrivateDriverSupport(source);
    }
    private bool EnsureDealerStock(string source)
    {
        if (dealerRegistrationReady)
            return true;
        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        try
        {
            var ready = MercedesC63CabrioLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerRegistrationReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                MercedesC63CabrioDiagnostics.Info(
                    context,
                    $"MercedesC63Cabrio: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"MercedesC63Cabrio: dealer stock update failed source='{source}': " +
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
                privateDriverReady = MercedesC63CabrioPrivateDriverSupport.EnsureVehicleAvailable(
                    vehicleTypeName);
            }
            if (privateDriverReady)
            {
                MercedesC63CabrioDiagnostics.Info(
                    context,
                    $"MercedesC63Cabrio: private-driver support registered source='{source}'.");
            }
            return privateDriverReady && privateDriverPoolReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"MercedesC63Cabrio: private-driver registration failed source='{source}': " +
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
            return MercedesC63CabrioPrivateDriverSupport.PrepareTrafficPool(
                playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                context?.Logger.Warn(
                    $"MercedesC63Cabrio: private-driver pool preparation failed source='{source}': " +
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
            vehicle.GetComponent<MercedesC63CabrioPaintController>()
                ?.ApplyCurrentColor("vehicle-variables-changed");
            vehicle.GetComponent<MercedesC63CabrioVisualDamageController>()
                ?.CheckForCompletedRepair();
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
                    vehicle.GetComponent<MercedesC63CabrioHighwaySeamGuard>();
                if (highwaySeamGuard == null)
                {
                    highwaySeamGuard = vehicle.gameObject
                        .AddComponent<MercedesC63CabrioHighwaySeamGuard>();
                }
                highwaySeamGuard.Initialize(rigidbody);
            }

            ConfigureMassProperties(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            var contactMaterialOwner =
                vehicle.GetComponent<MercedesC63CabrioContactMaterialOwner>();
            if (contactMaterialOwner == null)
            {
                contactMaterialOwner = vehicle.gameObject
                    .AddComponent<MercedesC63CabrioContactMaterialOwner>();
            }
            ConfigureBodyColliders(
                vehicle.gameObject,
                contactMaterialOwner.GetOrCreateMaterial());
            ConfigureExitMarkers(vehicle.gameObject);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(vehicle.gameObject);
            var warehouseBounds =
                vehicle.GetComponent<MercedesC63CabrioWarehouseBoundsController>();
            if (warehouseBounds == null)
            {
                warehouseBounds = vehicle.gameObject
                    .AddComponent<MercedesC63CabrioWarehouseBoundsController>();
            }
            warehouseBounds.Initialize();
            var warehouseEntry = vehicle.GetComponent<MercedesC63CabrioWarehouseEntryController>();
            if (warehouseEntry == null)
            {
                warehouseEntry = vehicle.gameObject
                    .AddComponent<MercedesC63CabrioWarehouseEntryController>();
            }
            warehouseEntry.Initialize(vehicle, context);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var traction = vehicle.GetComponent<MercedesC63CabrioTractionController>() ??
                vehicle.gameObject.AddComponent<MercedesC63CabrioTractionController>();
            traction.Initialize(vehicle.GetComponent<PhysicsVehicle>(), context);
            var caliperController = vehicle.GetComponent<MercedesC63CabrioCaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<MercedesC63CabrioCaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialController = vehicle.GetComponent<MercedesC63CabrioMaterialController>();
            if (materialController == null)
                materialController = vehicle.gameObject.AddComponent<MercedesC63CabrioMaterialController>();
            var materialResult = materialController.Initialize(context);
            var glassController = vehicle.GetComponent<MercedesC63CabrioGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<MercedesC63CabrioGlassController>();
            glassController.Initialize(context);
            var paintController = vehicle.GetComponent<MercedesC63CabrioPaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<MercedesC63CabrioPaintController>();
            paintController.Initialize(vehicle, context);
            var lightingController = vehicle.GetComponent<MercedesC63CabrioLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<MercedesC63CabrioLightingController>();
            lightingController.Initialize(vehicle, context);
            // Lighting overlays are spawned per vehicle. Build the deformation
            // allowlist only after they exist so illuminated lamp surfaces move
            // with the surrounding lamp housings after an impact.
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var driverController = vehicle.GetComponent<MercedesC63CabrioDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<MercedesC63CabrioDriverController>();
            driverController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<MercedesC63CabrioAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<MercedesC63CabrioAudioController>();
            audioController.Initialize(vehicle, context);
            MercedesC63CabrioDiagnostics.Info(
                context,
                $"MercedesC63Cabrio: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, transmission=7-speed-AMG-MCT, rwd=true, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"shiftRpm={MercedesC63CabrioTuning.DownshiftRpm:0}/{MercedesC63CabrioTuning.UpshiftRpm:0}, " +
                $"brakeTorque={MercedesC63CabrioTuning.BrakeTorque:0.0}, nativeAbsTcs=true, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstaclesNormalized={normalizedNavMeshObstacles}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=4.0L-V8-650Nm-peak-350kW, twinTurbo=true, steeringCalipers=4, " +
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
                $"MercedesC63Cabrio: vehicle configuration failed instance={instanceId}: " +
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
                SetFloat(component, "camber", 0f);
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
                colliders[0].center = MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.40f, 0f));
                colliders[0].size = new Vector3(1.82f, 0.52f, 4.40f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.78f, -0.08f));
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
                $"MercedesC63Cabrio damage vehicle={vehicle.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        ConfigureNativeDamage(damageHandler);
        var impactDamage = vehicle.GetComponent<MercedesC63CabrioImpactDamageController>();
        if (impactDamage == null)
            impactDamage = vehicle.gameObject.AddComponent<MercedesC63CabrioImpactDamageController>();
        impactDamage.Initialize(vehicle,damageHandler,context);

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
                $"MercedesC63Cabrio damage vehicle={vehicle.GetInstanceID()}: " +
                "deformable outer body mesh is missing; visual damage remains disabled.");
            return 0;
        }

        ClearCollection(damageHandler, "_collisionEvents");
        damageHandler.deformationRadius = DeformationRadius;
        damageHandler.deformationRandomness = DeformationRandomness;
        damageHandler.deformationStrength = DeformationStrength;
        damageHandler.deformationVerticesPerFrame = 8000;
        // Imported panels do not share one root-local coordinate system, so the
        // model-aware controller deforms the selected outer shell in world space.
        damageHandler.meshDeform = false;

        var visualDamage = vehicle.GetComponent<MercedesC63CabrioVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<MercedesC63CabrioVisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            VisualImpactThresholdMps);

        MercedesC63CabrioDiagnostics.DamageInfo(
            context,
            $"MercedesC63Cabrio damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} percentageThreshold={damageHandler.decelerationThreshold / 100f:0.0}mps, " +
            $"collisionCooldown={damageHandler.collisionTimeout:0.00}s, visualThreshold={VisualImpactThresholdMps:0.0}mps; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    internal static void ConfigureNativeDamage(NWH.VehiclePhysics2.Damage.DamageHandler handler)
    {
        // Keep percentage damage on the game's native impulse -> Damage -> saved
        // vehicle/UI path. This must also apply to an existing loaded Mercedes.
        handler.enabled = true;
        handler.visualOnly = false;
        handler.collisionTimeout = 0.2f;
        handler.damageIntensity = DamageIntensity;
        handler.decelerationThreshold = DamageDecelerationThreshold;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        if (filter == null || filter.sharedMesh == null)
            return false;
        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null) return false;
        // Lamp overlays are children of their source meshes and can be off
        // when damage is registered. Classify them by the lamp assembly.
        if (filter.name.StartsWith("MercedesC63Cabrio_", StringComparison.Ordinal) &&
            filter.transform.parent != null &&
            (HasAncestor(filter.transform.parent, "taillights") ||
             HasAncestor(filter.transform.parent, "M_LBucket_SetRT_Max")))
            return true;
        if (!renderer.enabled) return false;
        if (filter.name == "C63DamageBody") return true;
        // Explicit Mercedes exterior nodes; never deform cabin, wheels or calipers.
        foreach (var marker in new[] { "M_Badge_Max", "M_Opaque_ChromeSmooth_Max",
            "M_Opaque_PlasticRoughBlack_Max", "M_LicensePlate_Max", "M_Opaque_AluminumSmooth_Max",
            "M_CarPaintNormal_Max", "M_CarPaint_Trim_CarbonA_Max", "M_Grille", "M_LBucket",
            "M_LGlass", "taillights", "M_Chassis_Max", "M_Engine_Max" })
            if (HasAncestor(filter.transform, marker)) return true;
        return false;
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
            if (component == null || !string.Equals(component.GetType().FullName,
                    "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal)) continue;
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
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateC63PowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", true);
            SetFloat(forcedInduction, "powerGainMultiplier", 1f);
            SetFloat(forcedInduction, "spoolUpTime", TurboSpoolUpTime);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", MercedesC63CabrioTuning.ShiftDuration);
            SetFloat(transmission, "_downshiftRPM", MercedesC63CabrioTuning.DownshiftRpm);
            SetFloat(transmission, "_upshiftRPM", MercedesC63CabrioTuning.UpshiftRpm);
            SetFloat(transmission, "postShiftBan", MercedesC63CabrioTuning.PostShiftBan);
            SetFloat(transmission, "variableShiftIntensity", MercedesC63CabrioTuning.VariableShiftIntensity);
            SetBool(transmission, "variableShiftPoint", true);
            SetFloat(GetMember(component, "brakes"), "maxTorque", MercedesC63CabrioTuning.BrakeTorque);
            SetInt(transmission, "forwardGearCount", 7);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", C63Gears);

            if (GetMember(powertrain, "wheelGroups") is IList wheelGroups)
                foreach (var wheelGroup in wheelGroups)
                    SetFloat(wheelGroup, "antiRollBarForce", AntiRollBarForce);

            var rearDriveConfigured = false;
            if (GetMember(powertrain, "differentials") is IList differentials)
            {
                foreach (var differential in differentials)
                {
                    if (differential is NWH.VehiclePhysics2.Powertrain.DifferentialComponent rear && rear.name == "Rear Differential")
                    {
                        rear.DifferentialType = NWH.VehiclePhysics2.Powertrain.DifferentialComponent.Type.LimitedSlip;
                        rear.stiffness = 0.5f; rear.powerRamp = 0.4f; rear.coastRamp = 0.2f;
                    }
                    if (!string.Equals(GetMember(differential, "name") as string,
                            "Center Differential", StringComparison.Ordinal)) continue;
                    // The unpacked rig routes differential output B through the rear axle.
                    SetFloat(differential, "biasAB", 1f);
                    rearDriveConfigured = true;
                }
            }
            foreach (var limiter in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (limiter != null && string.Equals(limiter.GetType().Name,
                        "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
                    SetFloat(GetMember(limiter, "module"), "speedLimit", SpeedLimitKph);
            return GetInt(transmission, "forwardGearCount") == 7 && rearDriveConfigured;
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
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
internal sealed class MercedesC63CabrioHighwaySeamGuard : MonoBehaviour
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
internal sealed class MercedesC63CabrioContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        // Match the proven Revuelto body contact: low friction lets the rigid
        // bodies separate naturally after a crash instead of locking together.
        contactMaterial = new PhysicMaterial("Mercedes-AMG C63 Cabriolet body contact")
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
public sealed class MercedesC63CabrioGlassController : MonoBehaviour
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
                    MercedesC63CabrioMaterials.GetTransparentRole(renderer, source) !=
                    MercedesC63CabrioTransparentRole.CabinGlass)
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    MercedesC63CabrioMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
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
                    MercedesC63CabrioMaterials.GetTransparentRole(renderer, material) ==
                    MercedesC63CabrioTransparentRole.CabinGlass)
                {
                    renderer.SetPropertyBlock(null, index);
                    MercedesC63CabrioMaterials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            MercedesC63CabrioDiagnostics.Info(
                context,
                $"MercedesC63Cabrio glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            MercedesC63CabrioDiagnostics.Info(
                context,
                $"MercedesC63Cabrio glass vehicle={GetInstanceID()}: repaired after " +
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
public sealed class MercedesC63CabrioVisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.64f;
    private const float MaximumDentDepth = 0.34f;
    private const float DepthPerExcessMps = 0.011f;
    private const float FrontDentLongitudinalRadius = 0.95f;
    private const float MaximumFrontDentDepth = 0.14f;
    private const float FrontDepthPerExcessMps = 0.004f;
    private const float RearDentLongitudinalRadius = 1.02f;
    private const float MaximumRearDentDepth = 0.16f;
    private const float RearDepthPerExcessMps = 0.0045f;
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
    private int percentageDiagnosticLogs;
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
        var repairMeshes = new List<Mesh>();
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            var pristine = Instantiate(filter.sharedMesh);
            pristine.name = filter.sharedMesh.name + "_C63RepairSource";
            repairMeshes.Add(pristine); runtimeMeshes.Add(pristine);
            var runtimeMesh = Instantiate(filter.sharedMesh);
            runtimeMesh.name = filter.sharedMesh.name + "_RuntimeDamage";
            filter.sharedMesh = runtimeMesh;
            deformableFilters.Add(filter);
            originalVertices[filter] = runtimeMesh.vertices;
            damageMeshes[filter] = runtimeMesh;
            runtimeMeshes.Add(runtimeMesh);
        }
        // Native Repair calls Reset even when no UI/global event is emitted.
        // Bind all exterior parts to this instance's immutable repair sources.
        var legacy = controller.GetComponent<VehicleDeformationController>();
        if (legacy == null) throw new InvalidOperationException("Native C63 repair reset component missing.");
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        typeof(VehicleDeformationController).GetField("meshFilters", fields)!.SetValue(legacy, deformableFilters.ToArray());
        typeof(VehicleDeformationController).GetField("originalMeshes", fields)!.SetValue(legacy, repairMeshes.ToArray());
        initialized = true;
    }

    private Mesh GetWritableMesh(MeshFilter filter)
    {
        var owned = damageMeshes[filter];
        if (filter.sharedMesh != owned)
        {
            // Resume after native Reset without ever deforming the pristine source.
            owned.vertices = originalVertices[filter]; owned.RecalculateBounds();
            filter.sharedMesh = owned;
        }
        return owned;
    }

    internal void CheckForCompletedRepair()
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
            MercedesC63CabrioDiagnostics.DamageInfo(
                context,
                $"MercedesC63Cabrio damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
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
        if (initialized && collision != null && damageHandler != null &&
            MercedesC63CabrioDiagnostics.DebugEnabled && MercedesC63CabrioDiagnostics.DamageDebugEnabled &&
            percentageDiagnosticLogs < 12 && NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision))
        {
            percentageDiagnosticLogs++;
            // Copy callback values now: Unity can reuse the Collision object.
            StartCoroutine(ReportNativeDamage(collision.collider.name,
                collision.relativeVelocity.magnitude, collision.impulse.magnitude));
        }
        previousDamage = Mathf.Max(previousDamage, damageHandler?.Damage ?? 0f);
        previousSavedDamage = Mathf.Max(previousSavedDamage, vehicle?.vehicleInstance?.damage ?? 0f);
        if (!initialized || collision == null || Time.unscaledTime < nextCollisionTime ||
            collision.relativeVelocity.magnitude < impactThresholdMps ||
            !NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision)) return;
        try
        {
            var contacts = collision.contacts;
            if (contacts.Length == 0) return;
            nextCollisionTime = Time.unscaledTime + CollisionCooldown;
            var points = new Vector3[contacts.Length];
            var normals = new Vector3[contacts.Length];
            for (var i = 0; i < contacts.Length; i++) { points[i] = contacts[i].point; normals[i] = contacts[i].normal; }
            var changed = ApplyImpact(points, normals, collision.relativeVelocity.magnitude);
            if (diagnosticLogs++ < MaximumDiagnosticLogs)
                MercedesC63CabrioDiagnostics.DamageInfo(context,
                    $"MercedesC63Cabrio damage vehicle={vehicle?.GetInstanceID()}, contact={collision.collider?.name}, " +
                    $"speed={collision.relativeVelocity.magnitude:0.00}m/s, meshes={changed}, " +
                    $"owned={deformableFilters.Count}, endCap={MaximumEndCumulativeDent:0.00}m, sideCap={MaximumCumulativeDent:0.00}m.");
        }
        catch (Exception exception)
        {
            if (failureReported) return;
            failureReported = true;
            context?.Logger.Warn($"MercedesC63Cabrio damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {exception.GetType().Name}: {exception.Message}");
        }
    }

    private IEnumerator ReportNativeDamage(string target, float speed, float impulse)
    {
        // CarController synchronizes native damage to the save after one frame.
        yield return null;
        yield return null;
        if (damageHandler == null || vehicle == null) yield break;
        MercedesC63CabrioDiagnostics.DamageInfo(context,
            $"MercedesC63Cabrio percentage damage vehicle={vehicle.GetInstanceID()}, target={target}, " +
            $"relativeSpeed={speed:0.00}m/s, impulse={impulse:0.0}Ns, " +
            $"threshold={damageHandler.decelerationThreshold / 100f:0.0}m/s, " +
            $"enabled={damageHandler.enabled}, visualOnly={damageHandler.visualOnly}, " +
            $"native={damageHandler.Damage:P2}, saved={vehicle.vehicleInstance?.damage:P2}.");
    }

    private const float MaximumCumulativeDent = .48f;
    private const float MaximumEndCumulativeDent = .20f;

    // One world-space field for body, trim, lamps, grille and end-region supports.
    // Evaluate immutable rest positions, so mesh partitions cannot drift apart on repeat impacts.
    internal int ApplyImpact(Vector3[] points, Vector3[] normals, float relativeSpeed)
    {
        if (!initialized || points.Length == 0 || points.Length != normals.Length || relativeSpeed < impactThresholdMps) return 0;
        var changedMeshes = 0;
        var excessSpeed = relativeSpeed - impactThresholdMps;
        foreach (var filter in deformableFilters)
        {
            if (filter == null) continue;
            var mesh = GetWritableMesh(filter);
            var vertices = mesh.vertices;
            var originals = originalVertices[filter];
            var changed = false;
            var support = filter.name.Contains("M_Chassis_Max") || filter.name.Contains("M_Engine_Max");
            for (var v = 0; v < vertices.Length; v++)
            {
                var restWorld = filter.transform.TransformPoint(originals[v]);
                var restBody = transform.InverseTransformPoint(restWorld);
                // Internal supports are included only in the crush zones, never the cabin/floor center.
                if (support && Mathf.Abs(restBody.z) < EndContactMinimumLongitudinalOffset) continue;
                var displacement = Vector3.zero;
                var selectedEnd = false;
                for (var pointIndex = 0; pointIndex < points.Length; pointIndex++)
                {
                    var localContact = transform.InverseTransformPoint(points[pointIndex]);
                    var delta = transform.InverseTransformVector(restWorld - points[pointIndex]);
                    var end = Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                              Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                    float influence, depth;
                    Vector3 direction;
                    if (end)
                    {
                        var front = localContact.z >= 0f;
                        influence = EndInfluence(delta, front);
                        depth = Mathf.Clamp(excessSpeed * (front ? FrontDepthPerExcessMps : RearDepthPerExcessMps),
                            .015f, front ? MaximumFrontDentDepth : MaximumRearDentDepth);
                        direction = front ? -transform.forward : transform.forward;
                    }
                    else
                    {
                        influence = Mathf.Pow(Mathf.Clamp01(1f - delta.magnitude / DentRadius), 2f);
                        depth = Mathf.Clamp(excessSpeed * DepthPerExcessMps, .025f, MaximumDentDepth);
                        var towardCenter = ((body != null ? body.worldCenterOfMass : transform.position) - points[pointIndex]).normalized;
                        direction = normals[pointIndex].normalized;
                        if (Vector3.Dot(direction, towardCenter) < 0f) direction = -direction;
                    }
                    var candidate = direction * (depth * influence);
                    if (candidate.sqrMagnitude > displacement.sqrMagnitude) { displacement = candidate; selectedEnd = end; }
                }
                if (displacement.sqrMagnitude < .00000001f) continue;
                var currentWorld = filter.transform.TransformPoint(vertices[v]);
                var existingDent = currentWorld - restWorld;
                // Preserve a prior stronger side dent; a later end impact must not repair it.
                var cap = Mathf.Max(existingDent.magnitude, selectedEnd ? MaximumEndCumulativeDent : MaximumCumulativeDent);
                var cumulative = Vector3.ClampMagnitude(existingDent + displacement, cap);
                vertices[v] = filter.transform.InverseTransformPoint(restWorld + cumulative);
                changed = true;
            }
            if (!changed) continue;
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            changedMeshes++;
        }
        return changedMeshes;
    }

    internal static float EndInfluence(Vector3 delta, bool front)
    {
        // A low bumper contact must also push the attached upper lamp/trunk/fascia.
        // Use a longitudinal crush plane with a feathered footprint rather than
        // an ellipsoid that fades almost to zero at the upper/lower panel edges.
        var x = Mathf.Abs(delta.x) / 1.20f;
        var y = Mathf.Abs(delta.y) / 1.15f;
        var z = Mathf.Abs(delta.z) / (front ? FrontDentLongitudinalRadius : RearDentLongitudinalRadius);
        if (x >= 1f || y >= 1f || z >= 1f) return 0f;
        var footprint = (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.80f, 1f, x))) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.85f, 1f, y)));
        return footprint * Mathf.Pow(1f - z, 1.35f);
    }

    private void OnDestroy()
    {
        if (repairRecoveryCoroutine != null)
            StopCoroutine(repairRecoveryCoroutine);
        repairRecoveryCoroutine = null;
        foreach (var mesh in runtimeMeshes)
            if (mesh != null) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
        runtimeMeshes.Clear();
        damageMeshes.Clear();
    }
}

