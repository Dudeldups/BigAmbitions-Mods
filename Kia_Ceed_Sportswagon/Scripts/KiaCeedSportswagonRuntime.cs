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

public sealed class KiaCeedSportswagonRuntime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1450f;
    private const float EnginePowerKw = 117.5f;
    // Developer Tools recorded three clean 0-100 km/h runs at 8.421-8.425 s.
    // Kia's 7DCT Sportswagon figure is 8.8 s; scaling the simulated output from
    // 0.73 to 0.70 predicts about 8.8 s without changing rated catalog power.
    private const float EngineSimulationPowerScale = 0.70f;
    private const float EngineIdleRpm = 750f;
    private const float EngineLimitRpm = 6500f;
    private const float SpeedLimitKph = 210f;
    private const float FinalDriveRatio = 1f;
    private const float EngineInertia = 0.13f;
    private const float EngineStartDuration = 0.38f;
    // The dual-clutch transmission does not need a manual-car launch flare.
    // Keep its engagement just above idle so N -> first applies torque without
    // the observed 2,000 RPM pause.
    private const float ClutchEngagementRpm = 1050f;
    private const float ClutchThrottleOffsetRpm = 450f;
    private const float ClutchEngagementRange = 450f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 1.00f;
    private const float AntiRollBarForce = 5200f;
    private const float SuspensionReboundRate = 19000f;
    private const float FrontSuspensionTravel = 0.110f;
    private const float RearSuspensionTravel = 0.125f;
    private const float FrontTireRadius = 0.31715f;
    private const float RearTireRadius = 0.31715f;
    private const float FrontTireWidth = 0.225f;
    private const float RearTireWidth = 0.225f;
    private const float FrontTrack = 1.559f;
    private const float RearTrack = 1.567f;
    private const float Wheelbase = 2.650f;
    // Player-only fit adjustment. NPC bindings retain the authored tire centers
    // at +1.410 m front and -1.240 m rear in their separate traffic setup.
    private const float FrontWheelForwardOffset = 0.105f;
    private const float RearWheelForwardOffset = 0.1025f;
    private const float TargetBodyLength = 4.605f;
    private const float FrontBodyRise = 0.020f;
    private const float RearBodyDrop = 0.0025f;
    private const float WheelCenterRideHeightOffset = 0f;
    // Highway resistance is applied as aerodynamic force by the Kia-only
    // component below; Unity's linear Rigidbody drag is not speed-squared air
    // resistance and made the measured 0-200 run far too quick.
    private const float VehicleLinearDrag = 0f;
    // Telemetry normalized to 100-0 km/h gave about 29.3 m against ADAC's
    // 31.9 m Ceed Sportswagon result. A ~15% force increase targets ~25.5 m,
    // about 25% shorter than the road-car result requested by the user.
    private const float VehicleBrakeForce = 2165f;
    private const float BrakeMaxTorque = 2165f;
    private const float VehicleAngularDrag = 1.9f;
    private const float FrontForwardGrip = 1.00f;
    private const float RearForwardGrip = 1.04f;
    private const float FrontForwardStiffness = 1.08f;
    private const float RearForwardStiffness = 1.12f;
    private const float DeformationStrength = 0.19f;
    private const float DeformationRadius = 0.27f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 0.60f;
    private const float DamageDecelerationThreshold = 500f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private const float WarehouseExitGuardDuration = 8f;
    private const float WarehouseExitGuardClearDistance = 4f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.14f, 0.16f);
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.48f, 1.91f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.78f, 0.56f, 0.98f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.48f, -1.96f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.78f, 0.56f, 1.06f);
    private static readonly Vector3 DriverExitPosition =
        new Vector3(-2.10f, 0.20f, 0.15f);
    private static readonly Vector3 PassengerExitPosition =
        new Vector3(2.10f, 0.20f, 0.15f);

    private static readonly float[] CeedDctGears =
    {
        // D7UF1 final-drive groups: 1/2/4/5 use 4.294; 3/6/7/R use 3.174.
        -5.304f * 3.174f,
        0f,
        3.929f * 4.294f,
        2.318f * 4.294f,
        2.043f * 3.174f,
        1.070f * 4.294f,
        0.822f * 4.294f,
        0.884f * 3.174f,
        0.721f * 3.174f,
    };

    private static AnimationCurve CreateCeedPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.12f, 0.10f),
            new Keyframe(0.23f, 0.34f),
            new Keyframe(0.38f, 0.57f),
            new Keyframe(0.54f, 0.79f),
            new Keyframe(0.69f, 0.88f),
            new Keyframe(0.85f, 1f),
            new Keyframe(0.93f, 0.94f),
            new Keyframe(1f, 0.80f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private int enteredVehicleActivationInstanceId;
    private Coroutine? exitedPlayerRecoveryCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private KiaCeedSportswagonWarehouseEntryController? warehouseExitGuardEntryController;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private bool dealerRegistrationReady;
    private bool dealerReadyLogged;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;
    private GameObject? playerVehiclePrefab;
    private GameObject? lightOverlayPrefab;

    public static KiaCeedSportswagonRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab,
        GameObject lightOverlayPrefab)
    {
        var runtime = FindObjectOfType<KiaCeedSportswagonRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(KiaCeedSportswagonRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<KiaCeedSportswagonRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        runtime.lightOverlayPrefab = lightOverlayPrefab;
        KiaCeedSportswagonSleepBindings.Configure(
            playerVehiclePrefab.GetComponent<VehicleController>(), context);
        if (playerVehiclePrefab.GetComponent<KiaCeedSportswagonCreationNotifier>() == null)
            playerVehiclePrefab.AddComponent<KiaCeedSportswagonCreationNotifier>();
        KiaCeedSportswagonPrivateDriverSupport.SetContext(context);
        KiaCeedSportswagonPrivateDriverSupport.SetLightOverlayPrefab(lightOverlayPrefab);
        var trafficFrequency = runtime.GetComponent<KiaCeedSportswagonTrafficFrequency>() ??
                               runtime.gameObject.AddComponent<KiaCeedSportswagonTrafficFrequency>();
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
        KiaCeedSportswagonPrivateDriverSupport.RemoveVehicle(vehicleTypeName);
        playerVehiclePrefab = null;
        lightOverlayPrefab = null;
        KiaCeedSportswagonVisualDamageController.ClearAnchorLayoutCache();
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
        KiaCeedSportswagonCreationNotifier.Created -= HandleVehicleCreated;
        KiaCeedSportswagonCreationNotifier.Created += HandleVehicleCreated;
        KiaCeedSportswagonCreationNotifier.AppearanceReady -= HandleAppearanceReady;
        KiaCeedSportswagonCreationNotifier.AppearanceReady += HandleAppearanceReady;
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
        KiaCeedSportswagonCreationNotifier.Created -= HandleVehicleCreated;
        KiaCeedSportswagonCreationNotifier.AppearanceReady -= HandleAppearanceReady;
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
        if (eventName == KiaCeedSportswagonPaintController.SupplementalColorRestoredEvent)
        {
            ConfigureExistingAppearance("supplemental-color-restored");
            return;
        }
        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        KiaCeedSportswagonDiagnostics.DealerEntryInfo(
            context,
            $"KiaCeedSportswagon dealer-entry: game-event='{eventName}', " +
            DescribeVehicleState(selectedVehicle));

        // Dealer purchases do not reliably invoke onEnterVehicle. This event
        // is a bounded lifecycle handoff from the dealer display vehicle to
        // the owned, player-controlled instance, so configure it here rather
        // than scanning vehicles every frame.
        TryConfigureVehicle(selectedVehicle);
        var paintController = selectedVehicle!
            .GetComponent<KiaCeedSportswagonPaintController>();
        paintController?.ApplyCurrentColor("game-event");
        if (string.Equals(eventName, "vehicle-repainter:finish-preview", StringComparison.Ordinal) ||
            string.Equals(eventName, "vehicle-repainter:color-preview", StringComparison.Ordinal))
        {
            paintController?.LogFinishPreview(eventName);
        }
    }

    private void HandleVehicleVariablesChanged() => ConfigureExistingVehicles(out _);

    private void HandleVehicleCreated(VehicleController vehicle)
    {
        TryConfigureVehicle(vehicle);
        KiaCeedSportswagonDiagnostics.Info(context,
            $"KiaCeedSportswagon: creation callback vehicle={vehicle.GetInstanceID()}.");
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SubscribeEvents();
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeEvents();
        ConfigureExistingAppearance("game-loaded-late");
        if (KiaCeedSportswagonLoadRecovery.CompleteInterruptedLoad(context))
            StartCoroutine(ReportLoadedInputState());
        privateDriverRegistrationAllowed = true;
        ScheduleInitialization("game-loaded-late");
    }

    private IEnumerator ReportLoadedInputState()
    {
        // The native loading screen fades out for 0.8 seconds after this event.
        yield return new WaitForSecondsRealtime(2f);
        KiaCeedSportswagonLoadRecovery.ReportInputState(context);
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
        KiaCeedSportswagonDiagnostics.DealerEntryInfo(
            context,
            $"KiaCeedSportswagon dealer-entry: onEnterVehicle, {DescribeVehicleState(vehicle)}");
        TryConfigureVehicle(vehicle);
        vehicle.GetComponent<KiaCeedSportswagonGlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle == null || !IsTargetVehicle(vehicle))
            return;
        vehicle.GetComponent<KiaCeedSportswagonPaintController>()
            ?.ApplyCurrentColor("vehicle-entered");
        ScheduleEnteredVehicleActivation(vehicle);
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;
        KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
            context,
            $"KiaCeedSportswagon warehouse-exit: onExitVehicle, {DescribeVehicleState(vehicle)}");
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
        KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
            context,
            $"KiaCeedSportswagon player-exit: deferred validation playerPosition={root.position}, " +
            $"agentCount={agents.Length}, anyAgentOffNavMesh={agents.Any(agent => agent != null && agent.enabled && !agent.isOnNavMesh)}, " +
            $"initialClear={initialExitClear}, needsRecovery={needsRecovery}.");
        if (!needsRecovery)
        {
            exitedPlayerRecoveryCoroutine = null;
            yield break;
        }
        if (!TryFindClearExitPosition(root, exitedVehicle, out var target))
        {
            KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
                context,
                "KiaCeedSportswagon player-exit: recovery failed; no NavMesh/capsule-clear candidate found.");
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
        KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
            context,
            $"KiaCeedSportswagon player-exit: recovery moved player to {target}.");
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
            KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                context,
                $"KiaCeedSportswagon dealer-entry: activation skipped because the vehicle is " +
                $"neither controlled nor selected, {DescribeVehicleState(vehicle)}");
            return;
        }

        var instanceId = vehicle.GetInstanceID();
        if (enteredVehicleActivationCoroutine != null &&
            enteredVehicleActivationInstanceId == instanceId)
        {
            KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                context,
                $"KiaCeedSportswagon dealer-entry: activation already pending instance={instanceId}.");
            return;
        }

        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);

        enteredVehicleActivationInstanceId = instanceId;
        KiaCeedSportswagonDiagnostics.DealerEntryInfo(
            context,
            $"KiaCeedSportswagon dealer-entry: activation scheduled, {DescribeVehicleState(vehicle)}");
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
            KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                context,
                $"KiaCeedSportswagon dealer-entry: activation failed; NWH controller missing, " +
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
                KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                    context,
                    $"KiaCeedSportswagon dealer-entry: activation cancelled attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }

            // Paint assignment from the dealer is asynchronous too; retain a
            // bounded entry refresh without turning it into runtime polling.
            vehicle.GetComponent<KiaCeedSportswagonPaintController>()
                ?.ApplyCurrentColor("vehicle-entered");
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                context,
                $"KiaCeedSportswagon dealer-entry: activation attempt={attempt + 1}, " +
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
                    KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                        context,
                        "KiaCeedSportswagon dealer-entry: native engine healthy; shifted neutral to first.");
                    yield return new WaitForFixedUpdate();
                    KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                        context,
                        $"KiaCeedSportswagon dealer-entry: first-gear settle, gear={transmission.Gear}, " +
                        $"ratio={transmission.currentGearRatio:0.000}.");
                }
                else
                {
                    KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                        context,
                        $"KiaCeedSportswagon dealer-entry: native engine healthy; preserved gear={transmission.Gear}.");
                }
                break;
            }

            KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                context,
                $"KiaCeedSportswagon dealer-entry: dormant powertrain; restart attempt={attempt + 1}.");
            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
            {
                KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                    context,
                    $"KiaCeedSportswagon dealer-entry: restart aborted after stop attempt={attempt + 1}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
                break;
            }
            engine.StartEngine();
            yield return new WaitForSecondsRealtime(.75f);
            if (vehicle != null && vehicle.controlledByPlayer &&
                transmission.Gear == 0)
            {
                transmission.ShiftInto(1, true);
                KiaCeedSportswagonDiagnostics.DealerEntryInfo(
                    context,
                    $"KiaCeedSportswagon dealer-entry: restart attempt={attempt + 1} shifted neutral to first.");
            }
            yield return new WaitForSecondsRealtime(.15f);
        }

        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        KiaCeedSportswagonDiagnostics.DealerEntryInfo(
            context,
            $"KiaCeedSportswagon dealer-entry: activation completed, {DescribeVehicleState(vehicle)}");
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!KiaCeedSportswagonDealerStock.IsTargetDealer(registration?.BusinessName) ||
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
            KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
                context,
                $"KiaCeedSportswagon warehouse-exit: guard skipped; current vehicle is not " +
                $"a controlled KiaCeedSportswagon ({DescribeVehicleState(vehicle)}).");
            return;
        }

        var entrance = FindClosestDriveInEntrance(vehicle.transform.position);
        if (entrance == null)
        {
            KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
                context,
                $"KiaCeedSportswagon warehouse-exit: guard skipped; no DriveInEntrance found, " +
                DescribeVehicleState(vehicle));
            return;
        }

        var nativeMeshCollider = vehicle.GetComponentInChildren<MeshCollider>(true);
        KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
            context,
            $"KiaCeedSportswagon warehouse-exit: native exit completed, {DescribeVehicleState(vehicle)} " +
            $"entrance='{entrance.name}' entrancePosition={entrance.transform.position}, " +
            $"nativeMesh='{nativeMeshCollider?.name ?? "missing"}', " +
            $"nativeMeshLength={nativeMeshCollider?.sharedMesh?.bounds.size.z:0.000}.");

        StopWarehouseExitGuard();
        warehouseExitGuardEntryController =
            vehicle.GetComponent<KiaCeedSportswagonWarehouseEntryController>();
        warehouseExitGuardEntryController?.SuppressEntrance(entrance, "warehouse-exit-guard");
        foreach (var enterTrigger in entrance.GetComponentsInChildren<DriveInEntranceEnterTrigger>(true))
        foreach (var collider in enterTrigger.GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled || !collider.isTrigger)
                continue;

            warehouseExitGuardColliders.Add(collider);
            KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
                context,
                $"KiaCeedSportswagon warehouse-exit: disabling entry trigger " +
                $"'{collider.name}' bounds={collider.bounds}.");
            collider.enabled = false;
        }

        if (warehouseExitGuardColliders.Count == 0)
        {
            warehouseExitGuardEntryController?.ClearSuppressedEntrance(
                entrance,
                "no-native-entry-trigger");
            warehouseExitGuardEntryController = null;
            KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
                context,
                "KiaCeedSportswagon warehouse-exit: guard skipped; matching entrance had no enabled trigger colliders.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(
            vehicle.transform.position - entrance.transform.position,
            Vector3.up);
        if (outward.sqrMagnitude < .0001f)
            outward = Vector3.ProjectOnPlane(entrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < .0001f)
        {
            KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
                context,
                "KiaCeedSportswagon warehouse-exit: guard aborted; outward direction was zero.");
            StopWarehouseExitGuard();
            return;
        }

        outward.Normalize();
        var startingDistance = Vector3.Dot(vehicle.transform.position, outward);
        Physics.SyncTransforms();
        KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
            context,
            $"KiaCeedSportswagon warehouse-exit: guard started triggerCount={warehouseExitGuardColliders.Count}, " +
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
        KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
            context,
            $"KiaCeedSportswagon warehouse-exit: guard ending reason={reason}, " +
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
            KiaCeedSportswagonDiagnostics.WarehouseExitInfo(
                context,
                $"KiaCeedSportswagon warehouse-exit: restoring triggerCount={warehouseExitGuardColliders.Count}, " +
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
        ConfigureExistingAppearance(source);
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
                $"KiaCeedSportswagon: City Cars stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
        if (privateDriverRegistrationAllowed && !privateDriverReady)
        {
            context?.Logger.Warn(
                $"KiaCeedSportswagon: private-driver support not ready source='{source}'.");
        }
        if (!privateDriverPoolReady)
        {
            context?.Logger.Warn(
                $"KiaCeedSportswagon: private-driver traffic pool not ready source='{source}'.");
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
            var ready = KiaCeedSportswagonDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            dealerRegistrationReady = ready;
            if (ready && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                KiaCeedSportswagonDiagnostics.Info(
                    context,
                    $"KiaCeedSportswagon: available at City Cars " +
                    $"source='{source}'.");
            }
            return ready;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"KiaCeedSportswagon: dealer stock update failed source='{source}': " +
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
                privateDriverReady = KiaCeedSportswagonPrivateDriverSupport.EnsureVehicleAvailable(
                    vehicleTypeName);
            }
            if (privateDriverReady)
            {
                KiaCeedSportswagonDiagnostics.Info(
                    context,
                    $"KiaCeedSportswagon: private-driver support registered source='{source}'.");
            }
            return privateDriverReady && privateDriverPoolReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"KiaCeedSportswagon: private-driver registration failed source='{source}': " +
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
            return KiaCeedSportswagonPrivateDriverSupport.PrepareTrafficPool(
                playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                context?.Logger.Warn(
                    $"KiaCeedSportswagon: private-driver pool preparation failed source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
                privateDriverPreparationExceptionLogged = true;
            }

            return false;
        }
    }

    private void HandleAppearanceReady(VehicleController vehicle) =>
        ConfigureVehicleAppearance(vehicle, "prefab-spawn-ready");

    private void ConfigureExistingAppearance(string source)
    {
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null) return;
        foreach (var vehicle in vehicles)
            ConfigureVehicleAppearance(vehicle, source);
    }

    private void ConfigureVehicleAppearance(VehicleController? vehicle, string source)
    {
        if (!IsTargetVehicle(vehicle)) return;
        try
        {
            var materials = vehicle!.GetComponent<KiaCeedSportswagonMaterialController>() ??
                vehicle.gameObject.AddComponent<KiaCeedSportswagonMaterialController>();
            materials.Initialize(context);
            var paint = vehicle.GetComponent<KiaCeedSportswagonPaintController>() ??
                vehicle.gameObject.AddComponent<KiaCeedSportswagonPaintController>();
            paint.Initialize(vehicle, context);
            paint.ApplyCurrentColor(source, force: true, preferSavedColor: true);
            KiaCeedSportswagonDiagnostics.PaintInfo(context,
                $"KiaCeedSportswagon reload appearance vehicle={vehicle.GetInstanceID()} source='{source}' " +
                $"saved='{vehicle.vehicleInstance?.vehicleColorName}' live='{vehicle.CarFeatures?.VehicleColor?.name}' " +
                $"applied={paint.HasAppliedColor}.");
        }
        catch (Exception exception)
        {
            context?.Logger.Warn($"KiaCeedSportswagon appearance vehicle={vehicle?.GetInstanceID()} source='{source}': {exception.GetBaseException().Message}");
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
        if (vehicle != null && (IsTargetVehicle(vehicle) ||
            string.Equals(vehicle.vehicleType?.vehicleTypeName, vehicleTypeName, StringComparison.Ordinal)))
            KiaCeedSportswagonSleepBindings.Configure(vehicle, context);
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
            vehicle.GetComponent<KiaCeedSportswagonPaintController>()
                ?.ApplyCurrentColor("vehicle-variables-changed");
            return;
        }

        try
        {
            var vehicleType = GetMember(vehicle, "vehicleType");
            var vehicleBrakeForceConfigured = SetValue(
                vehicleType, "brakeForce", typeof(float), VehicleBrakeForce);
            var rigidbody = vehicle.GetComponent<Rigidbody>() ?? vehicle.GetComponentInParent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.mass = VehicleMass;
                rigidbody.centerOfMass = StableCenterOfMass;
                rigidbody.drag = VehicleLinearDrag;
                rigidbody.angularDrag = VehicleAngularDrag;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.solverIterations = Mathf.Max(rigidbody.solverIterations, 12);
                rigidbody.solverVelocityIterations =
                    Mathf.Max(rigidbody.solverVelocityIterations, 4);

                var aerodynamicDrag =
                    rigidbody.GetComponent<KiaCeedSportswagonAerodynamicDrag>();
                if (aerodynamicDrag == null)
                {
                    aerodynamicDrag = rigidbody.gameObject
                        .AddComponent<KiaCeedSportswagonAerodynamicDrag>();
                }
                aerodynamicDrag.Initialize(rigidbody, context);

                var highwaySeamGuard =
                    vehicle.GetComponent<KiaCeedSportswagonHighwaySeamGuard>();
                if (highwaySeamGuard == null)
                {
                    highwaySeamGuard = vehicle.gameObject
                        .AddComponent<KiaCeedSportswagonHighwaySeamGuard>();
                }
                highwaySeamGuard.Initialize(rigidbody, context);
            }

            ConfigureMassProperties(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            ConfigureFrontBodyPitch(vehicle.gameObject);
            var contactMaterialOwner =
                vehicle.GetComponent<KiaCeedSportswagonContactMaterialOwner>();
            if (contactMaterialOwner == null)
            {
                contactMaterialOwner = vehicle.gameObject
                    .AddComponent<KiaCeedSportswagonContactMaterialOwner>();
            }
            ConfigureBodyColliders(
                vehicle.gameObject,
                contactMaterialOwner.GetOrCreateMaterial());
            ConfigureExitMarkers(vehicle.gameObject);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(vehicle.gameObject);
            var warehouseBounds =
                vehicle.GetComponent<KiaCeedSportswagonWarehouseBoundsController>();
            if (warehouseBounds == null)
            {
                warehouseBounds = vehicle.gameObject
                    .AddComponent<KiaCeedSportswagonWarehouseBoundsController>();
            }
            warehouseBounds.Initialize();
            var warehouseEntry = vehicle.GetComponent<KiaCeedSportswagonWarehouseEntryController>();
            if (warehouseEntry == null)
            {
                warehouseEntry = vehicle.gameObject
                    .AddComponent<KiaCeedSportswagonWarehouseEntryController>();
            }
            warehouseEntry.Initialize(vehicle, context);
            var powertrainConfigured = ConfigurePowertrain(vehicle.gameObject);
            var caliperController = vehicle.GetComponent<KiaCeedSportswagonCaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<KiaCeedSportswagonCaliperController>();
            caliperController.Initialize(vehicle, context);
            var materialController = vehicle.GetComponent<KiaCeedSportswagonMaterialController>();
            if (materialController == null)
                materialController = vehicle.gameObject.AddComponent<KiaCeedSportswagonMaterialController>();
            var materialResult = materialController.Initialize(context);
            var glassController = vehicle.GetComponent<KiaCeedSportswagonGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<KiaCeedSportswagonGlassController>();
            glassController.Initialize(context);
            var paintController = vehicle.GetComponent<KiaCeedSportswagonPaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<KiaCeedSportswagonPaintController>();
            paintController.Initialize(vehicle, context);
            var lightOverlayCount = lightOverlayPrefab == null
                ? 0
                : KiaCeedSportswagonLightOverlayBinder.Attach(
                    vehicle.gameObject, lightOverlayPrefab, context);
            var lightingController = vehicle.GetComponent<KiaCeedSportswagonLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<KiaCeedSportswagonLightingController>();
            lightingController.Initialize(vehicle, context);
            // Lighting overlays are spawned per vehicle. Build the deformation
            // allowlist only after they exist so illuminated lamp surfaces move
            // with the surrounding lamp housings after an impact.
            KiaCeedSportswagonWashBindings.Configure(vehicle, context);
            var deformableBodyMeshes = ConfigureVisualDamage(vehicle);
            var driverController = vehicle.GetComponent<KiaCeedSportswagonDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<KiaCeedSportswagonDriverController>();
            driverController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<KiaCeedSportswagonAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<KiaCeedSportswagonAudioController>();
            audioController.Initialize(vehicle, context);
            KiaCeedSportswagonDiagnostics.Info(
                context,
                $"KiaCeedSportswagon: configured vehicle instance={instanceId}, " +
                $"mass={VehicleMass:0}kg, engine=1.5L-turbo-I4 rated={EnginePowerKw:0.0}kW " +
                $"NWH-output={EnginePowerKw * EngineSimulationPowerScale:0.0}kW " +
                $"calibration={EngineSimulationPowerScale:0.00}, transmission=7-speed-DCT, fwd=true, " +
                $"brakeTorque={BrakeMaxTorque:0}, " +
                $"vehicleBrakeForceConfigured={vehicleBrakeForceConfigured}, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstaclesNormalized={normalizedNavMeshObstacles}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=spec-derived-1.5T-profile, animatedFrontCalipers=2, " +
                "rearBrakes=combined-chassis-assembly, " +
                $"materialRenderers={materialResult.RendererCount}, " +
                $"decalMasksCleared={materialResult.DecalMasksCleared}, " +
                $"opaqueFixed={materialResult.OpaqueMaterialsFixed}, " +
                $"transparentFixed={materialResult.TransparentMaterialsFixed}, " +
                $"lightOverlays={lightOverlayCount}/12, " +
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
                $"KiaCeedSportswagon: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        var transforms = root.GetComponentsInChildren<Transform>(true);
        Transform? rearLeft = null;
        Transform? rearRight = null;
        Transform? rearBrakeAssembly = null;
        Transform? frontLeft = null;
        Transform? frontRight = null;
        foreach (var transform in transforms)
        {
            if (string.Equals(transform.name, "RearLeft_WheelController", StringComparison.Ordinal))
                rearLeft = transform;
            else if (string.Equals(transform.name, "RearRight_WheelController", StringComparison.Ordinal))
                rearRight = transform;
            else if (string.Equals(transform.name, "KiaCeedSportswagonRearBrakeAssembly", StringComparison.Ordinal))
                rearBrakeAssembly = transform;
            else if (transform.name == "FrontLeft_WheelController")
                frontLeft = transform;
            else if (transform.name == "FrontRight_WheelController")
                frontRight = transform;
        }

        var rearCorrection = 0f;
        var frontCorrection = frontLeft != null && frontRight != null
            ? Wheelbase * 0.5f + FrontWheelForwardOffset -
              (frontLeft.localPosition.z + frontRight.localPosition.z) * 0.5f
            : 0f;
        if (rearLeft != null && rearRight != null)
        {
            var targetRearZ = -Wheelbase * 0.5f + RearWheelForwardOffset;
            rearCorrection = targetRearZ - (rearLeft.localPosition.z + rearRight.localPosition.z) * 0.5f;
        }

        foreach (var transform in transforms)
        {
            if (TryGetKiaWheelPosition(transform.name, out var targetPosition))
                transform.localPosition = targetPosition;
            if (!transform.name.EndsWith("_WheelController", StringComparison.Ordinal))
                continue;

            var isFront = transform.name.StartsWith("Front", StringComparison.Ordinal);
            var isRear = transform.name.StartsWith("Rear", StringComparison.Ordinal);
            if (!isFront && !isRear)
                continue;

            ConfigureWheelPhysics(transform, isFront);
        }

        // The imported combined rear brake meshes are fixed to the chassis;
        // keep their axle center in step with the rear wheel controllers.
        if (rearBrakeAssembly != null && Mathf.Abs(rearCorrection) > 0.0001f)
            rearBrakeAssembly.position += root.transform.forward * rearCorrection;
        foreach (var transform in transforms)
            if (transform.name == "KiaCeedSportswagonFixedCaliperFrontLeft" ||
                transform.name == "KiaCeedSportswagonFixedCaliperFrontRight")
                transform.position += root.transform.forward * frontCorrection;
    }

    private static void ConfigureWheelPhysics(Transform transform, bool isFront)
    {
        foreach (var component in transform.GetComponents<MonoBehaviour>())
        {
            var spring = GetMember(component, "spring");
            SetFloat(spring, "maxLength", isFront ? FrontSuspensionTravel : RearSuspensionTravel);
            SetFloat(spring, "maxForce", 19000f);

            var damper = GetMember(component, "damper");
            SetFloat(damper, "reboundRate", SuspensionReboundRate);

            var wheel = GetMember(component, "wheel");
            SetFloat(wheel, "radius", isFront ? FrontTireRadius : RearTireRadius);
            SetFloat(wheel, "width", isFront ? FrontTireWidth : RearTireWidth);
            var forwardFriction = GetMember(component, "forwardFriction");
            if (forwardFriction != null)
            {
                SetFloat(forwardFriction, "grip", isFront ? FrontForwardGrip : RearForwardGrip);
                SetFloat(
                    forwardFriction,
                    "stiffness",
                    isFront ? FrontForwardStiffness : RearForwardStiffness);
                SetValue(component, "forwardFriction", forwardFriction.GetType(), forwardFriction);
            }
            SetFloat(component, "frictionCircleStrength", TireFrictionCircleStrength);
        }
    }

    private static bool TryGetKiaWheelPosition(string transformName, out Vector3 position)
    {
        position = Vector3.zero;
        string corner;
        if (transformName.EndsWith("_WheelController", StringComparison.Ordinal))
            corner = transformName.Substring(0, transformName.Length - "_WheelController".Length);
        else if (transformName.StartsWith("KiaCeedSportswagonWheel", StringComparison.Ordinal))
            corner = transformName.Substring("KiaCeedSportswagonWheel".Length);
        else if (transformName.StartsWith("KiaCeedSportswagonFixedCaliper", StringComparison.Ordinal))
            corner = transformName.Substring("KiaCeedSportswagonFixedCaliper".Length);
        else if (string.Equals(transformName, "FL", StringComparison.Ordinal))
            corner = "FrontLeft";
        else if (string.Equals(transformName, "FR", StringComparison.Ordinal))
            corner = "FrontRight";
        else if (string.Equals(transformName, "BL", StringComparison.Ordinal))
            corner = "RearLeft";
        else if (string.Equals(transformName, "BR", StringComparison.Ordinal))
            corner = "RearRight";
        else
            return false;

        var isFront = corner.StartsWith("Front", StringComparison.Ordinal);
        var isRear = corner.StartsWith("Rear", StringComparison.Ordinal);
        var isLeft = corner.EndsWith("Left", StringComparison.Ordinal);
        var isRight = corner.EndsWith("Right", StringComparison.Ordinal);
        if ((!isFront && !isRear) || (!isLeft && !isRight))
            return false;

        var track = isFront ? FrontTrack : RearTrack;
        var radius = isFront ? FrontTireRadius : RearTireRadius;
        position = new Vector3(
            (isLeft ? -1f : 1f) * track * 0.5f,
            radius + WheelCenterRideHeightOffset,
            (isFront ? 1f : -1f) * Wheelbase * 0.5f +
            (isFront ? FrontWheelForwardOffset : RearWheelForwardOffset));
        return true;
    }

    private static void ConfigureFrontBodyPitch(GameObject root)
    {
        var transforms = root.GetComponentsInChildren<Transform>(true);
        Transform? visual = null;
        Transform? damageBody = null;
        foreach (var transform in transforms)
        {
            if (string.Equals(transform.name, "KiaCeedSportswagonVisual", StringComparison.Ordinal))
                visual = transform;
            else if (string.Equals(transform.name, "KiaCeedSportswagonDamageBody", StringComparison.Ordinal))
                damageBody = transform;
        }

        if (visual == null)
            throw new InvalidOperationException("Kia visual root is missing for the front body pitch correction.");

        var pivot = root.transform.TransformPoint(new Vector3(0f, 0f, -TargetBodyLength * 0.5f));
        // Keep the existing front height while lowering only the rear by 2.5 mm.
        var pitch = -Mathf.Atan((FrontBodyRise + RearBodyDrop) / TargetBodyLength) * Mathf.Rad2Deg;
        visual.RotateAround(pivot, root.transform.right, pitch);
        visual.position -= root.transform.up * RearBodyDrop;
        if (damageBody != null)
        {
            damageBody.RotateAround(pivot, root.transform.right, pitch);
            damageBody.position -= root.transform.up * RearBodyDrop;
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
            if (component is VehicleDeformationController legacy)
            {
                // Start may never run after disabling this component. Native
                // Repair still calls Reset, which indexes both arrays together.
                // Kia owns restoration of every per-instance damage mesh.
                legacy.meshFilters = Array.Empty<MeshFilter>();
                legacy.originalMeshes = Array.Empty<Mesh>();
            }
        }

        var damageHandler =
            vehicle.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true);
        if (damageHandler == null)
        {
            context?.Logger.Warn(
                $"KiaCeedSportswagon damage vehicle={vehicle.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        var filters = new List<MeshFilter>();
        foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null ||
                (!IsDeformableExterior(filter) && !IsVisiblePaintFragment(filter)))
                continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            // Lamp overlays start disabled and are enabled by the lighting
            // controller only when illuminated. They must still own a damage mesh.
            var lightOverlay = filter.name.StartsWith(
                "KiaCeedSportswagonLightOverlay_", StringComparison.Ordinal);
            if (renderer != null &&
                (lightOverlay || (renderer.enabled && !renderer.forceRenderingOff)))
                filters.Add(filter);
        }

        if (filters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"KiaCeedSportswagon damage vehicle={vehicle.GetInstanceID()}: " +
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

        var visualDamage = vehicle.GetComponent<KiaCeedSportswagonVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<KiaCeedSportswagonVisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        var explicitDamageTargets = filters.Count(IsExplicitDamageTarget);
        var attachmentTargets = filters.Count(IsDamageAttachmentDetail);
        var lampTargets = filters.Count(IsExteriorLampSurface);
        var requestedTargets = filters.Count(IsUserSpecifiedDamageTarget);
        var targetNames = string.Join(",", filters.Select(filter => filter.name).Take(24));
        var requestedTargetNames = string.Join(",", filters
            .Where(IsUserSpecifiedDamageTarget)
            .Select(filter => filter.name)
            .Take(24));
        var missingRequestedTargets = string.Join(",", UserSpecifiedDamageTargetMarkers
            .Where(marker => !filters.Any(filter => MatchesUserSpecifiedTarget(filter, marker))));
        var attachmentNames = string.Join(",", filters
            .Where(IsDamageAttachmentDetail)
            .Select(filter => filter.name)
            .Take(24));
        var paintFragmentStates = string.Join(",", vehicle
            .GetComponentsInChildren<MeshFilter>(true)
            .Where(filter => filter != null &&
                HasAncestor(filter.transform,
                    "Ext_nl_sportswagon_glt_2019_Door_FL_carpaint.001_CarPaint_0"))
            .Select(filter =>
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                return $"{filter.name}:vertices={filter.sharedMesh?.vertexCount ?? 0}:" +
                       $"renderer={renderer?.enabled}:forceOff={renderer?.forceRenderingOff}:" +
                       $"selected={filters.Contains(filter)}";
            }));
        if (attachmentTargets == 0)
        {
            context?.Logger.Warn(
                $"KiaCeedSportswagon damage vehicle={vehicle.GetInstanceID()}: " +
                "no separate grille, badge, emblem or lamp meshes were selected.");
        }
        if (missingRequestedTargets.Length > 0)
        {
            context?.Logger.Warn(
                $"KiaCeedSportswagon damage vehicle={vehicle.GetInstanceID()}: " +
                $"requested source meshes were not found in the damage filter: " +
                $"[{missingRequestedTargets}].");
        }
        KiaCeedSportswagonDiagnostics.DamageInfo(
            context,
            $"KiaCeedSportswagon damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} explicitDetails={explicitDamageTargets} " +
            $"endAttachments={attachmentTargets} requestedTargets={requestedTargets} " +
            $"lampMeshes={lampTargets} intensity={DamageIntensity:0.00} " +
            $"threshold={DamageDecelerationThreshold / 100f:0.0}mps targets=[{targetNames}] " +
            $"requested=[{requestedTargetNames}] " +
            $"requestedMissing=[{missingRequestedTargets}] " +
            $"endTargets=[{attachmentNames}] paintFragments=[{paintFragmentStates}]; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        if (filter.name.StartsWith("KiaCeedSportswagonDamageBody", StringComparison.Ordinal) ||
            filter.name.StartsWith("KiaCeedSportswagonLightOverlay_", StringComparison.Ordinal))
            return true;

        var transform = filter.transform;
        var lampSurface = IsExteriorLampSurface(filter);
        var explicitDamageTarget = IsExplicitDamageTarget(filter);
        var userSpecifiedTarget = IsUserSpecifiedDamageTarget(filter);
        var explicitlyNamedInteriorTarget = UserSpecifiedDamageTargetMarkers.Any(
            marker => marker.StartsWith("Int_", StringComparison.Ordinal) &&
                      MatchesUserSpecifiedTarget(filter, marker));
        if (HasAncestor(transform, "Int_") && !explicitlyNamedInteriorTarget)
            return false;
        if (IsRollingOrBrakeAssembly(transform) ||
            (HasAncestor(transform, "Int_") && !lampSurface &&
             !explicitDamageTarget && !userSpecifiedTarget))
            return false;
        if (HasAncestor(transform, "sunroof") && !userSpecifiedTarget)
            return false;

        if (!lampSurface && !userSpecifiedTarget &&
            (HasMaterial(filter, "glass", "window") || HasAncestor(transform, "windshield") ||
             HasAncestor(transform, "doorglass") ||
             HasAncestor(transform, "quarterglass") || HasAncestor(transform, "backlight")))
            return false;

        // Ext_* nodes include bumpers, painted panels, trim and lamp housings.
        // Do not reject a whole mixed-material renderer just because it also
        // contains grille or chrome sub-materials; that was excluding complete
        // Kia bumper meshes. Cabin glass, interior and rolling assemblies are
        // filtered separately above.
        return userSpecifiedTarget || lampSurface || explicitDamageTarget || HasAncestor(transform, "Ext_") ||
               HasMaterial(filter, "CarPaint", "plastic", "rubbertrim", "mirror",
                   "grille", "chrome", "aluminiumbrushed");
    }

    private static bool IsExplicitDamageTarget(MeshFilter filter)
    {
        var transform = filter.transform;
        return HasAncestor(transform, "bumper") || HasAncestor(transform, "grille") ||
               HasAncestor(transform, "grill") || HasAncestor(transform, "plate") ||
               HasAncestor(transform, "license") || HasAncestor(transform, "kennzeichen") ||
               HasAncestor(transform, "badge") || HasAncestor(transform, "emblem") ||
               HasAncestor(transform, "logo") ||
               HasAncestor(transform, "tailgate") || HasAncestor(transform, "trunk") ||
               HasAncestor(transform, "boot") ||
               HasMaterial(filter, "plate", "license", "kennzeichen", "badge", "emblem") ||
               IsUserSpecifiedDamageTarget(filter);
    }

    private static bool IsVisiblePaintFragment(MeshFilter filter) =>
        HasAncestor(filter.transform,
            "Ext_nl_sportswagon_glt_2019_Door_FL_carpaint.001_CarPaint_0") &&
        HasMaterial(filter, "CarPaint");

    private static readonly string[] UserSpecifiedDamageTargetMarkers =
    {
        "Int_Ceed_SW_Centre_Console_selfillumshiny__interior.001_blackpaint.001",
        "Int_Sportswagon_base_aluminiumbrushed.003_aluminiumbrushed.001",
        "plate1_plate_0",
        "Ext_mirror_electric_plasticdiffuse__linepattern.001_plastic_lineP_0",
        "Ext_GTL_MY22_rearbumper_plasticdiffuse__rearpattern.001_grille_S_0",
        "Int_Sportswagon_base_chrome.002_chrome.001_0",
        "Ext_Sportswagon_base_upperrearlights_chromered__squarepatte",
        "Ext_nl_sportswagon_glt_2019_Door_FL_carpaint.001_CarPaint_0",
        "Ext_sunroof_glasstintedlight.002_glass_clear_0",
        "Ext_sunroof_glasstintedlight.002_red_glass_0",
        "Ext_Sportswagon_base_Door_Trunk_plasticdiffuse__orange_flip_orange_0",
        "Int_Ceed_SW_Centre_Console_buttons_GOLD.004_plasticdiffuse.001_0",
        "KiaCeedSportswagonLightOverlay_RearTailLights",
        "KiaCeedSportswagonLightOverlay_RearBrakeLights",
        "Ext_overhead_lighting_plasticshiny.002_plasticshiny.001_0",
        // Rear interior surfaces follow nearby painted-shell vertex deltas on rear hits.
        "Int_Sportswagon_base_leather.001_leather.001_0",
        "Int_fabrics__carpet.001_fabrics__carpet.001_0",
        "Int_boot_static_GTL_left_plasticdiffuse__darkbump1.002_darkbump1.001_0",
        "Int_boot_static_w_speakers_selfillumrough__speakers.001_speakers_0",
    };

    private static readonly string[] UserSpecifiedAttachmentMarkers =
    {
        "Int_Ceed_SW_Centre_Console_selfillumshiny__interior.001_blackpaint.001",
        "Int_Sportswagon_base_aluminiumbrushed.003_aluminiumbrushed.001",
        "plate1_plate_0",
        "Ext_mirror_electric_plasticdiffuse__linepattern.001_plastic_lineP_0",
        "Ext_GTL_MY22_rearbumper_plasticdiffuse__rearpattern.001_grille_S_0",
        "Int_Sportswagon_base_chrome.002_chrome.001_0",
        "Ext_Sportswagon_base_upperrearlights_chromered__squarepatte",
        "Ext_sunroof_glasstintedlight.002_glass_clear_0",
        "Ext_sunroof_glasstintedlight.002_red_glass_0",
        "Ext_Sportswagon_base_Door_Trunk_plasticdiffuse__orange_flip_orange_0",
        "KiaCeedSportswagonLightOverlay_RearTailLights",
        "KiaCeedSportswagonLightOverlay_RearBrakeLights",
    };

    internal static bool IsUserSpecifiedDamageTarget(MeshFilter filter)
    {
        return GetUserSpecifiedDamageTargetMarker(filter) != null;
    }

    internal static string? GetUserSpecifiedDamageTargetMarker(MeshFilter filter)
    {
        foreach (var marker in UserSpecifiedDamageTargetMarkers)
            if (MatchesUserSpecifiedTarget(filter, marker))
                return marker;
        return null;
    }

    internal static bool IsUserSpecifiedAttachmentTarget(MeshFilter filter)
    {
        foreach (var marker in UserSpecifiedAttachmentMarkers)
            if (MatchesUserSpecifiedTarget(filter, marker))
                return true;
        return false;
    }

    internal static bool MatchesUserSpecifiedTarget(MeshFilter filter, string marker) =>
        HasAncestor(filter.transform, marker) ||
        HasMaterial(filter, marker);

    private static bool IsDamageAttachmentDetail(MeshFilter filter) =>
        IsExteriorLampSurface(filter) ||
        HasAncestor(filter.transform, "grille") ||
        HasAncestor(filter.transform, "grill") ||
        HasAncestor(filter.transform, "plate") ||
        HasAncestor(filter.transform, "license") ||
        HasAncestor(filter.transform, "kennzeichen") ||
        HasAncestor(filter.transform, "badge") ||
        HasAncestor(filter.transform, "emblem") ||
        HasAncestor(filter.transform, "logo") ||
        HasMaterial(filter,
            "grille_pattern", "grille_S", "chrome_R", "chrome.001", "chrome_line",
            "chrome_pink", "chrome_dark", "plate", "license", "kennzeichen", "badge", "emblem") ||
        IsUserSpecifiedAttachmentTarget(filter);

    private static bool IsRollingOrBrakeAssembly(Transform transform)
    {
        for (var current = transform; current != null; current = current.parent)
        {
            var name = current.name;
            if (name.IndexOf("KiaCeedSportswagonWheel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Wheel_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("tyre", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("tire", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("rim", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("discbrake", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("caliper", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("FixedCaliper", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool IsExteriorLampSurface(MeshFilter filter) =>
        HasAncestor(filter.transform, "headlight") ||
        HasAncestor(filter.transform, "foglight") ||
        HasAncestor(filter.transform, "taillight") ||
        HasAncestor(filter.transform, "upperrearlight") ||
        HasAncestor(filter.transform, "LightsMain") ||
        HasAncestor(filter.transform, "Int_Sportswagon_base_chrome.002") ||
        HasMaterial(filter, "light_glass", "red_glass", "chrome_lights");

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
            var brakes = GetMember(component, "brakes");
            var brakeTorqueConfigured = SetValue(
                brakes, "maxTorque", typeof(float), BrakeMaxTorque);
            var clutch = GetMember(powertrain, "clutch");
            SetFloat(clutch, "engagementRPM", ClutchEngagementRpm);
            SetFloat(clutch, "throttleEngagementOffsetRPM", ClutchThrottleOffsetRpm);
            SetFloat(clutch, "engagementRange", ClutchEngagementRange);
            SetFloat(clutch, "creepTorque", ClutchCreepTorque);
            SetFloat(clutch, "creepSpeedLimit", 1f);
            var engine = GetMember(powertrain, "engine");
            SetFloat(engine, "inertia", EngineInertia);
            SetFloat(engine, "maxPower", EnginePowerKw * EngineSimulationPowerScale);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateCeedPowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", true);
            SetFloat(forcedInduction, "powerGainMultiplier", 1f);
            SetFloat(forcedInduction, "spoolUpTime", 0.28f);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.18f);
            SetFloat(transmission, "_downshiftRPM", 3400f);
            SetFloat(transmission, "_upshiftRPM", 6200f);
            SetInt(transmission, "forwardGearCount", 7);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", CeedDctGears);

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
                    // NWH routes output A to the front axle; zero leaves the
                    // center differential entirely on the FWD output.
                    SetFloat(differential, "biasAB", 0f);
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

            return GetInt(transmission, "forwardGearCount") == 7 &&
                   rearDriveConfigured && brakeTorqueConfigured;
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
internal sealed class KiaCeedSportswagonHighwaySeamGuard : MonoBehaviour
{
    // The last road test contacted the known Hamptons highway seam at
    // 111-114 km/h; a 40 m/s trigger (144 km/h) missed those impacts.
    private const float MinimumSpeedMps = 25f;
    private const float MaximumSampleAgeSeconds = 0.1f;
    private const float MinimumUpwardContactNormal = 0.9f;
    private const float CorrectionLogCooldown = 2f;
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
    private float nextCorrectionLogTime;
    private ModContext? context;

    internal void Initialize(Rigidbody vehicleBody, ModContext? modContext)
    {
        body = vehicleBody;
        context = modContext;
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
        if (Time.unscaledTime >= nextCorrectionLogTime)
        {
            nextCorrectionLogTime = Time.unscaledTime + CorrectionLogCooldown;
            KiaCeedSportswagonDiagnostics.HighwaySeamInfo(
                context,
                $"KiaCeedSportswagon highway seam guard vehicle={body.GetInstanceID()} " +
                $"surface='{other.name}' speed={velocityBeforeStep.magnitude * 3.6f:0.0}kph " +
                $"verticalVelocity={velocityBeforeStep.y:0.00}->{correctedVelocity.y:0.00}mps.");
        }
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
internal sealed class KiaCeedSportswagonContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        // Match the proven Revuelto body contact: low friction lets the rigid
        // bodies separate naturally after a crash instead of locking together.
        contactMaterial = new PhysicMaterial("KiaCeedSportswagon body contact")
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
public sealed class KiaCeedSportswagonGlassController : MonoBehaviour
{
    private readonly List<Renderer> visibleGlassRenderers = new List<Renderer>();
    private readonly Dictionary<Material, Dictionary<KiaCeedSportswagonTransparentRole, Material>> runtimeMaterials =
        new Dictionary<Material, Dictionary<KiaCeedSportswagonTransparentRole, Material>>();
    private readonly List<GameObject> rearPrivacyObjects = new List<GameObject>();
    private readonly List<Mesh> rearPrivacyMeshes = new List<Mesh>();
    private readonly List<Material> rearPrivacyMaterials = new List<Material>();
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

        visibleGlassRenderers.Clear();
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var containsVisibleGlass = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                {
                    continue;
                }

                var role = KiaCeedSportswagonMaterials.GetTransparentRole(renderer, source);
                if (role != KiaCeedSportswagonTransparentRole.CabinGlass &&
                    role != KiaCeedSportswagonTransparentRole.RearPrivacyGlass)
                    continue;

                containsVisibleGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var roleMaterials))
                {
                    roleMaterials = new Dictionary<KiaCeedSportswagonTransparentRole, Material>();
                    runtimeMaterials.Add(source, roleMaterials);
                }
                if (!roleMaterials.TryGetValue(role, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_Runtime" + role;
                    KiaCeedSportswagonMaterials.PrepareTransparentMaterial(runtimeMaterial, role);
                    roleMaterials.Add(role, runtimeMaterial);
                }
                materials[index] = runtimeMaterial;
            }
            if (!containsVisibleGlass)
                continue;
            renderer.sharedMaterials = materials;
            visibleGlassRenderers.Add(renderer);
        }
        ConfigureRearPrivacyTintOverlays();
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
        foreach (var renderer in visibleGlassRenderers)
        {
            if (renderer == null)
                continue;
            if (!renderer.enabled || renderer.forceRenderingOff)
                restored++;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            // Do not clear the renderer-wide MPB here: on renderers shared
            // with body paint it may carry the Repainter's active finish.
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null)
                {
                    var role = KiaCeedSportswagonMaterials.GetTransparentRole(renderer, material);
                    if (role != KiaCeedSportswagonTransparentRole.CabinGlass &&
                        role != KiaCeedSportswagonTransparentRole.RearPrivacyGlass)
                        continue;
                    renderer.SetPropertyBlock(null, index);
                    propertyBlocksCleared++;
                    KiaCeedSportswagonMaterials.PrepareTransparentMaterial(material, role);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            KiaCeedSportswagonDiagnostics.Info(
                context,
                $"KiaCeedSportswagon glass vehicle={GetInstanceID()}: configured " +
                $"renderers={visibleGlassRenderers.Count}, " +
                $"materialRoles={runtimeMaterials.Count}, rearPrivacyOverlays={rearPrivacyObjects.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            KiaCeedSportswagonDiagnostics.Info(
                context,
                $"KiaCeedSportswagon glass vehicle={GetInstanceID()}: repaired after " +
                $"'{source}' renderers={restored}, propertyBlocks={propertyBlocksCleared}.");
        }
    }

    private void OnDestroy()
    {
        if (restoreCoroutine != null)
            StopCoroutine(restoreCoroutine);
        restoreCoroutine = null;
        foreach (var roleMaterials in runtimeMaterials.Values)
        {
            foreach (var material in roleMaterials.Values)
                if (material != null)
                    Destroy(material);
        }
        runtimeMaterials.Clear();
        foreach (var gameObject in rearPrivacyObjects)
            if (gameObject != null)
                Destroy(gameObject);
        rearPrivacyObjects.Clear();
        foreach (var mesh in rearPrivacyMeshes)
            if (mesh != null)
                Destroy(mesh);
        rearPrivacyMeshes.Clear();
        foreach (var material in rearPrivacyMaterials)
            if (material != null)
                Destroy(material);
        rearPrivacyMaterials.Clear();
    }

    private void ConfigureRearPrivacyTintOverlays()
    {
        var configuredTriangles = 0;
        foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var sourceMesh = filter != null ? filter.sharedMesh : null;
            var sourceMaterials = renderer.sharedMaterials;
            if (sourceMesh == null || sourceMaterials.Length == 0 || IsExcludedLampOrRoof(renderer.name))
                continue;

            var vertices = sourceMesh.vertices;
            var selected = new List<int>();
            Material? tintSource = null;
            var subMeshCount = Mathf.Min(sourceMesh.subMeshCount, sourceMaterials.Length);
            for (var subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                var material = sourceMaterials[subMesh];
                if (material == null || !KiaCeedSportswagonMaterials.IsTransparentMaterial(material))
                    continue;
                var role = KiaCeedSportswagonMaterials.GetTransparentRole(renderer, material);
                if (role == KiaCeedSportswagonTransparentRole.PanoramicRoofGlass ||
                    role == KiaCeedSportswagonTransparentRole.HeadlampLens ||
                    role == KiaCeedSportswagonTransparentRole.Authored)
                    continue;

                var indices = sourceMesh.GetTriangles(subMesh);
                for (var index = 0; index + 2 < indices.Length; index += 3)
                {
                    var a = indices[index];
                    var b = indices[index + 1];
                    var c = indices[index + 2];
                    var center = transform.InverseTransformPoint(renderer.transform.TransformPoint(
                        (vertices[a] + vertices[b] + vertices[c]) / 3f));
                    if (!IsRearPrivacyWindowTriangle(center))
                        continue;
                    selected.Add(a);
                    selected.Add(b);
                    selected.Add(c);
                    tintSource ??= material;
                }
            }

            if (selected.Count == 0 || tintSource == null)
                continue;
            var mesh = new Mesh
            {
                name = "KiaCeedSportswagon_RearPrivacy_" + renderer.name,
                indexFormat = sourceMesh.indexFormat,
                vertices = vertices,
                normals = sourceMesh.normals,
                tangents = sourceMesh.tangents,
                uv = sourceMesh.uv,
                uv2 = sourceMesh.uv2,
            };
            mesh.SetTriangles(selected, 0, true);
            mesh.RecalculateBounds();
            rearPrivacyMeshes.Add(mesh);

            var overlayObject = new GameObject("KiaCeedSportswagon_RearPrivacy_" + renderer.name);
            overlayObject.transform.SetParent(renderer.transform, false);
            overlayObject.transform.localScale = Vector3.one * 1.0002f;
            overlayObject.layer = renderer.gameObject.layer;
            overlayObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
            overlayRenderer.renderingLayerMask = renderer.renderingLayerMask;
            overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            overlayRenderer.receiveShadows = false;
            overlayRenderer.sharedMaterial = Instantiate(tintSource);
            overlayRenderer.sharedMaterial.name = overlayObject.name + "_SmokedGlass";
            KiaCeedSportswagonMaterials.PrepareTransparentMaterial(
                overlayRenderer.sharedMaterial,
                KiaCeedSportswagonTransparentRole.RearPrivacyGlass);
            overlayObject.SetActive(true);
            rearPrivacyObjects.Add(overlayObject);
            rearPrivacyMaterials.Add(overlayRenderer.sharedMaterial);
            configuredTriangles += selected.Count / 3;
        }

        if (rearPrivacyObjects.Count == 0)
            context?.Logger.Warn(
                $"KiaCeedSportswagon glass vehicle={GetInstanceID()}: no transparent rear-window triangles " +
                "matched the rear doors, quarter glass, or tailgate regions.");
        else
            KiaCeedSportswagonDiagnostics.Info(
                context,
                $"KiaCeedSportswagon glass vehicle={GetInstanceID()}: darkened rear privacy glass " +
                $"overlays={rearPrivacyObjects.Count}, triangles={configuredTriangles}; " +
                "front side glass and windshield retained their current tint.");
    }

    private static bool IsExcludedLampOrRoof(string rendererName) =>
        rendererName.IndexOf("sunroof", StringComparison.OrdinalIgnoreCase) >= 0 ||
        rendererName.IndexOf("headlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
        rendererName.IndexOf("taillight", StringComparison.OrdinalIgnoreCase) >= 0 ||
        rendererName.IndexOf("upperrearlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
        rendererName.IndexOf("light_glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
        rendererName.IndexOf("squarepattern", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRearPrivacyWindowTriangle(Vector3 center)
    {
        var sideWindow = Mathf.Abs(center.x) >= 0.38f && Mathf.Abs(center.x) <= 0.98f &&
                         center.y >= 0.82f && center.y <= 1.48f &&
                         center.z >= -1.86f && center.z <= -0.30f;
        var tailgateWindow = Mathf.Abs(center.x) <= 0.92f &&
                             center.y >= 0.76f && center.y <= 1.48f && center.z <= -1.78f;
        return sideWindow || tailgateWindow;
    }
}

[AddComponentMenu("")]
public sealed class KiaCeedSportswagonVisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.64f;
    private const float MaximumDentDepth = 0.24f;
    private const float DepthPerExcessMps = 0.011f;
    private const float FrontDentLateralRadius = 1.22f;
    private const float FrontDentVerticalRadius = 1.14f;
    private const float FrontDentLongitudinalRadius = 1.42f;
    private const float MaximumFrontDentDepth = 0.17f;
    private const float FrontDepthPerExcessMps = 0.006f;
    private const float RearDentLateralRadius = 1.28f;
    private const float RearDentVerticalRadius = 1.22f;
    private const float RearHatchAnchorRearLimit = -1.60f;
    private const float FrontPanelAnchorMinimumZ = 1.60f;
    private const float FrontPanelAnchorMaximumDistance = 0.75f;
    private const float RearDentLongitudinalRadius = 1.62f;
    private const float MaximumRearDentDepth = 0.30f;
    private const float RearDepthPerExcessMps = 0.009f;
    private const float EndImpactDentStrength = 0.90f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    // Keep visual collision cadence aligned with native numerical damage.
    private const float CollisionCooldown = 0.8f;
    private const double SlowProcessingWarningMilliseconds = 25d;
    private const int MaximumProcessedContacts = 8;
    private const int MaximumDiagnosticLogs = 32;
    private const int MaximumCollisionGateDiagnosticLogs = 32;

    private readonly struct DamageContactData
    {
        internal DamageContactData(
            Vector3 worldPoint,
            Vector3 localPoint,
            Vector3 inwardDirection,
            bool isEnd,
            bool isFront,
            float maximumRadiusSquared,
            float inverseLateralRadiusSquared,
            float inverseLongitudinalRadiusSquared,
            float depth)
        {
            WorldPoint = worldPoint;
            LocalPoint = localPoint;
            InwardDirection = inwardDirection;
            IsEnd = isEnd;
            IsFront = isFront;
            MaximumRadiusSquared = maximumRadiusSquared;
            InverseLateralRadiusSquared = inverseLateralRadiusSquared;
            InverseLongitudinalRadiusSquared = inverseLongitudinalRadiusSquared;
            Depth = depth;
        }

        internal Vector3 WorldPoint { get; }
        internal Vector3 LocalPoint { get; }
        internal Vector3 InwardDirection { get; }
        internal bool IsEnd { get; }
        internal bool IsFront { get; }
        internal float MaximumRadiusSquared { get; }
        internal float InverseLateralRadiusSquared { get; }
        internal float InverseLongitudinalRadiusSquared { get; }
        internal float Depth { get; }
    }

    private readonly struct PanelShellVertex
    {
        internal PanelShellVertex(MeshFilter filter, int vertexIndex, Vector3 vehiclePosition)
        {
            Filter = filter;
            VertexIndex = vertexIndex;
            VehiclePosition = vehiclePosition;
        }

        internal MeshFilter Filter { get; }
        internal int VertexIndex { get; }
        internal Vector3 VehiclePosition { get; }
    }

    private readonly struct PanelVertexAnchor
    {
        internal PanelVertexAnchor(int interiorVertexIndex, MeshFilter shellFilter, int shellVertexIndex)
        {
            InteriorVertexIndex = interiorVertexIndex;
            ShellFilter = shellFilter;
            ShellVertexIndex = shellVertexIndex;
        }

        internal int InteriorVertexIndex { get; }
        internal MeshFilter ShellFilter { get; }
        internal int ShellVertexIndex { get; }
    }

    private readonly struct CachedPanelVertexAnchor
    {
        internal CachedPanelVertexAnchor(int targetIndex, int sourceFilterIndex, int sourceVertexIndex)
        {
            TargetIndex = targetIndex;
            SourceFilterIndex = sourceFilterIndex;
            SourceVertexIndex = sourceVertexIndex;
        }

        internal int TargetIndex { get; }
        internal int SourceFilterIndex { get; }
        internal int SourceVertexIndex { get; }
    }

    private sealed class PanelAnchorLayout
    {
        internal int[] MeshIds = Array.Empty<int>();
        internal int[] VertexCounts = Array.Empty<int>();
        internal string[] Names = Array.Empty<string>();
        internal Matrix4x4[] Matrices = Array.Empty<Matrix4x4>();
        internal int[] OverlayParents = Array.Empty<int>();
        internal CachedPanelVertexAnchor[]?[] Panels = Array.Empty<CachedPanelVertexAnchor[]?>();
        internal CachedPanelVertexAnchor[]?[] Overlays = Array.Empty<CachedPanelVertexAnchor[]?>();
    }

    // Retain one immutable layout, with no meshes, transforms or live vehicles.
    // Each instance restores its own MeshFilter references and mutable vertices.
    private static PanelAnchorLayout? cachedAnchorLayout;

    internal static void ClearAnchorLayoutCache() => cachedAnchorLayout = null;

    // Balanced nearest-vertex search keeps whole-car attachment binding bounded
    // even when the imported shell has dense material groups at either end.
    private sealed class PanelShellTree
    {
        private readonly PanelShellVertex[] points;
        internal int Count => points.Length;

        internal PanelShellTree(PanelShellVertex[] points)
        {
            this.points = points;
            Build(0, points.Length, 0);
        }

        private void Build(int start, int end, int depth)
        {
            if (end - start <= 1) return;
            var middle = (start + end) / 2;
            SelectMedian(start, end, middle, depth % 3);
            Build(start, middle, depth + 1);
            Build(middle + 1, end, depth + 1);
        }

        private void SelectMedian(int start, int end, int middle, int axis)
        {
            // Partition once per level instead of sorting every subtree. Three
            // partitions also keep planar/split geometry from degenerating.
            while (end - start > 1)
            {
                var pivot = points[(start + end) / 2].VehiclePosition[axis];
                var lower = start;
                var index = start;
                var upper = end;
                while (index < upper)
                {
                    var value = points[index].VehiclePosition[axis];
                    if (value < pivot)
                    {
                        Swap(index, lower);
                        index++;
                        lower++;
                    }
                    else if (value > pivot)
                    {
                        upper--;
                        Swap(index, upper);
                    }
                    else
                        index++;
                }
                if (middle < lower)
                    end = lower;
                else if (middle >= upper)
                    start = upper;
                else
                    return;
            }
        }

        private void Swap(int first, int second)
        {
            var value = points[first];
            points[first] = points[second];
            points[second] = value;
        }

        internal bool TryFindNearest(Vector3 target, float maximumDistance,
            out PanelShellVertex nearest, out float distance)
        {
            nearest = default;
            var bestSquared = maximumDistance * maximumDistance;
            var found = false;
            Search(0, points.Length, 0, target, ref bestSquared, ref nearest, ref found);
            distance = Mathf.Sqrt(bestSquared);
            return found;
        }

        private void Search(int start, int end, int depth, Vector3 target,
            ref float bestSquared, ref PanelShellVertex nearest, ref bool found)
        {
            if (start >= end || bestSquared == 0f) return;
            var middle = (start + end) / 2;
            var point = points[middle];
            var squared = (point.VehiclePosition - target).sqrMagnitude;
            if (squared < bestSquared)
            {
                bestSquared = squared;
                nearest = point;
                found = true;
                if (bestSquared == 0f) return;
            }
            var axis = depth % 3;
            var difference = target[axis] - point.VehiclePosition[axis];
            if (difference < 0f)
            {
                Search(start, middle, depth + 1, target, ref bestSquared, ref nearest, ref found);
                if (difference * difference < bestSquared)
                    Search(middle + 1, end, depth + 1, target, ref bestSquared, ref nearest, ref found);
            }
            else
            {
                Search(middle + 1, end, depth + 1, target, ref bestSquared, ref nearest, ref found);
                if (difference * difference < bestSquared)
                    Search(start, middle, depth + 1, target, ref bestSquared, ref nearest, ref found);
            }
        }
    }

    private readonly List<MeshFilter> deformableFilters = new List<MeshFilter>();
    private readonly List<int> sourceMeshIds = new List<int>();
    private bool anchorLayoutCacheHit;
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
    private readonly Dictionary<MeshFilter, int[]> frontEndVertexIndices =
        new Dictionary<MeshFilter, int[]>();
    private readonly Dictionary<MeshFilter, int[]> rearEndVertexIndices =
        new Dictionary<MeshFilter, int[]>();
    private readonly Dictionary<MeshFilter, PanelVertexAnchor[]> panelVertexAnchors =
        new Dictionary<MeshFilter, PanelVertexAnchor[]>();
    private readonly Dictionary<MeshFilter, PanelVertexAnchor[]> lampOverlayVertexAnchors =
        new Dictionary<MeshFilter, PanelVertexAnchor[]>();
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private readonly ContactPoint[] contactScratch =
        new ContactPoint[MaximumProcessedContacts];
    private readonly DamageContactData[] impactContactScratch =
        new DamageContactData[MaximumProcessedContacts];
    private readonly List<string> changedMeshNames = new List<string>(12);
    private readonly List<string> changedEndAttachmentNames = new List<string>(12);
    private readonly List<string> changedRequestedTargetResults = new List<string>(24);
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
    private bool slowProcessingWarningReported;
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

        var initializationStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        vehicle = controller;
        damageHandler = handler;
        context = modContext;
        body = controller.GetComponent<Rigidbody>();
        impactThresholdMps = thresholdMps;
        previousDamage = handler.Damage;
        previousSavedDamage = controller.vehicleInstance?.damage ?? 0f;
        nextCollisionTime = 0f;
        deformableFilters.Clear();
        sourceMeshIds.Clear();
        anchorLayoutCacheHit = false;
        originalVertices.Clear();
        currentVertices.Clear();
        originalBounds.Clear();
        filterToVehicleMatrices.Clear();
        vehicleToFilterMatrices.Clear();
        damageMeshes.Clear();
        frontEndVertexIndices.Clear();
        rearEndVertexIndices.Clear();
        panelVertexAnchors.Clear();
        lampOverlayVertexAnchors.Clear();
        runtimeMeshes.Clear();
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            sourceMeshIds.Add(filter.sharedMesh.GetInstanceID());
            var runtimeMesh = Instantiate(filter.sharedMesh);
            runtimeMesh.name = filter.sharedMesh.name + "_RuntimeDamage";
            runtimeMesh.MarkDynamic();
            filter.sharedMesh = runtimeMesh;
            var vertices = runtimeMesh.vertices;
            var baseline = new Vector3[vertices.Length];
            Array.Copy(vertices, baseline, vertices.Length);
            var filterToVehicle = CalculateFilterToVehicle(filter.transform, controller.transform);
            deformableFilters.Add(filter);
            originalVertices[filter] = baseline;
            currentVertices[filter] = vertices;
            originalBounds[filter] = runtimeMesh.bounds;
            filterToVehicleMatrices[filter] = filterToVehicle;
            vehicleToFilterMatrices[filter] = filterToVehicle.inverse;
            damageMeshes[filter] = runtimeMesh;
            runtimeMeshes.Add(runtimeMesh);
            if (vertices.Length >= 30000)
            {
                var frontIndices = new List<int>();
                var rearIndices = new List<int>();
                for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    var longitudinalPosition = filterToVehicle.MultiplyPoint3x4(
                        vertices[vertexIndex]).z;
                    // End impacts cannot influence vertices past the opposite
                    // half of a merged GLB material group. Keep a margin for
                    // the largest configured longitudinal damage radius.
                    if (longitudinalPosition >= -1f)
                        frontIndices.Add(vertexIndex);
                    if (longitudinalPosition <= 1f)
                        rearIndices.Add(vertexIndex);
                }
                frontEndVertexIndices[filter] = frontIndices.ToArray();
                rearEndVertexIndices[filter] = rearIndices.ToArray();
            }
        }
        BuildPanelVertexAnchors();
        initialized = true;
        var initializationMilliseconds =
            (System.Diagnostics.Stopwatch.GetTimestamp() - initializationStartedAt) *
            1000d / System.Diagnostics.Stopwatch.Frequency;
        KiaCeedSportswagonDiagnostics.DamageInfo(context,
            $"KiaCeedSportswagon damage vehicle={vehicle?.GetInstanceID()}: initialized " +
            $"meshes={deformableFilters.Count} anchorLayoutCacheHit={anchorLayoutCacheHit} " +
            $"cloneAndBinding={initializationMilliseconds:0.0}ms.");
    }

    private static Matrix4x4 CalculateFilterToVehicle(Transform filter, Transform root)
    {
        var matrix = Matrix4x4.identity;
        for (var current = filter; current != root; current = current.parent)
        {
            if (current == null)
                return root.worldToLocalMatrix * filter.localToWorldMatrix;
            matrix = Matrix4x4.TRS(current.localPosition, current.localRotation, current.localScale) * matrix;
        }
        return matrix;
    }

    private void BuildPanelVertexAnchors()
    {
        if (TryRestoreAnchorLayout())
            return;
        const float cellSize = 0.35f;
        var shellTree = BuildAnchorTree(deformableFilters.Where(IsPaintedBodyTailgateTarget));
        if (shellTree.Count == 0)
        {
            context?.Logger.Warn("KiaCeedSportswagon: painted damage shell unavailable for detail anchors.");
            return;
        }

        foreach (var filter in deformableFilters)
        {
            if (IsPaintedBodyTailgateTarget(filter) || IsLightOverlay(filter))
                continue;
            // Imported Int_* material groups also contain exterior emblems and
            // lamps. Selection already excludes cabin meshes; use the explicit
            // lining list, rather than the misleading importer name, here.
            var anchors = MapPanelVertices(
                filter, shellTree, cellSize, IsRearInteriorShellFollower(filter), true);
            if (anchors.Length > 0)
                panelVertexAnchors[filter] = anchors;
        }

        // Blender overlays are subsets of their parent's production lamp mesh.
        // Bind to that exact mesh, after its body anchors have been applied, so
        // the emissive surface retains its original spacing from the lamp cover.
        foreach (var filter in deformableFilters.Where(IsLightOverlay))
        {
            var lamp = filter.transform.parent?.GetComponent<MeshFilter>();
            if (lamp == null || !originalVertices.ContainsKey(lamp))
            {
                context?.Logger.Warn($"KiaCeedSportswagon: overlay '{filter.name}' has no owned lamp damage mesh.");
                continue;
            }
            const float lampCellSize = 0.025f;
            var lampTree = BuildAnchorTree(new[] { lamp });
            var anchors = MapPanelVertices(filter, lampTree, lampCellSize, false);
            lampOverlayVertexAnchors[filter] = anchors;
            if (anchors.Length != originalVertices[filter].Length)
                context?.Logger.Warn($"KiaCeedSportswagon: overlay '{filter.name}' mapped " +
                    $"{anchors.Length}/{originalVertices[filter].Length} vertices to its production lamp.");
        }
        CacheAnchorLayout();
    }

    private bool TryRestoreAnchorLayout()
    {
        var layout = cachedAnchorLayout;
        if (layout == null || layout.MeshIds.Length != deformableFilters.Count)
            return false;
        for (var index = 0; index < deformableFilters.Count; index++)
        {
            var filter = deformableFilters[index];
            if (layout.MeshIds[index] != sourceMeshIds[index] ||
                layout.VertexCounts[index] != originalVertices[filter].Length ||
                layout.Names[index] != filter.name ||
                layout.OverlayParents[index] != GetOverlayParentIndex(filter))
                return false;
            var matrix = filterToVehicleMatrices[filter];
            for (var element = 0; element < 16; element++)
                if (Mathf.Abs(layout.Matrices[index][element] - matrix[element]) > 0.00001f)
                    return false;
        }

        RestoreBindings(layout.Panels, panelVertexAnchors);
        RestoreBindings(layout.Overlays, lampOverlayVertexAnchors);
        anchorLayoutCacheHit = true;
        return true;
    }

    private int GetOverlayParentIndex(MeshFilter filter)
    {
        if (!IsLightOverlay(filter))
            return -1;
        var parent = filter.transform.parent?.GetComponent<MeshFilter>();
        return parent != null ? deformableFilters.IndexOf(parent) : -1;
    }

    private void RestoreBindings(CachedPanelVertexAnchor[]?[] cached,
        Dictionary<MeshFilter, PanelVertexAnchor[]> destination)
    {
        for (var index = 0; index < cached.Length; index++)
        {
            var source = cached[index];
            if (source == null)
                continue;
            var anchors = new PanelVertexAnchor[source.Length];
            for (var vertex = 0; vertex < source.Length; vertex++)
            {
                var binding = source[vertex];
                anchors[vertex] = new PanelVertexAnchor(binding.TargetIndex,
                    deformableFilters[binding.SourceFilterIndex], binding.SourceVertexIndex);
            }
            destination[deformableFilters[index]] = anchors;
        }
    }

    private void CacheAnchorLayout()
    {
        cachedAnchorLayout = new PanelAnchorLayout
        {
            MeshIds = sourceMeshIds.ToArray(),
            VertexCounts = deformableFilters.Select(filter => originalVertices[filter].Length).ToArray(),
            Names = deformableFilters.Select(filter => filter.name).ToArray(),
            Matrices = deformableFilters.Select(filter => filterToVehicleMatrices[filter]).ToArray(),
            OverlayParents = deformableFilters.Select(GetOverlayParentIndex).ToArray(),
            Panels = CacheBindings(panelVertexAnchors),
            Overlays = CacheBindings(lampOverlayVertexAnchors),
        };
    }

    private CachedPanelVertexAnchor[]?[] CacheBindings(Dictionary<MeshFilter, PanelVertexAnchor[]> bindings)
    {
        var indices = new Dictionary<MeshFilter, int>();
        for (var index = 0; index < deformableFilters.Count; index++)
            indices[deformableFilters[index]] = index;
        var result = new CachedPanelVertexAnchor[]?[deformableFilters.Count];
        foreach (var pair in bindings)
        {
            var anchors = new CachedPanelVertexAnchor[pair.Value.Length];
            for (var vertex = 0; vertex < anchors.Length; vertex++)
            {
                var binding = pair.Value[vertex];
                anchors[vertex] = new CachedPanelVertexAnchor(binding.InteriorVertexIndex,
                    indices[binding.ShellFilter], binding.ShellVertexIndex);
            }
            result[indices[pair.Key]] = anchors;
        }
        return result;
    }

    private PanelShellTree BuildAnchorTree(IEnumerable<MeshFilter> sources)
    {
        var points = new List<PanelShellVertex>();
        var uniquePositions = new HashSet<Vector3>();
        foreach (var filter in sources)
        {
            if (!originalVertices.TryGetValue(filter, out var vertices) ||
                !filterToVehicleMatrices.TryGetValue(filter, out var toVehicle))
                continue;
            for (var index = 0; index < vertices.Length; index++)
            {
                var point = toVehicle.MultiplyPoint3x4(vertices[index]);
                if (uniquePositions.Add(point))
                    points.Add(new PanelShellVertex(filter, index, point));
            }
        }
        return new PanelShellTree(points.ToArray());
    }

    private PanelVertexAnchor[] MapPanelVertices(
        MeshFilter filter,
        PanelShellTree tree,
        float maximumDistance,
        bool rearInteriorOnly,
        bool bindFrontExterior = false)
    {
        var vertices = originalVertices[filter];
        var toVehicle = filterToVehicleMatrices[filter];
        var anchors = new List<PanelVertexAnchor>();
        var eligible = 0;
        var maximumGap = 0f;
        var frontEligible = 0;
        var frontMapped = 0;
        var queryResults = new Dictionary<Vector3, PanelShellVertex?>();
        var mixedFrontExterior = IsMixedFrontExteriorShellFollower(filter);
        for (var index = 0; index < vertices.Length; index++)
        {
            var point = toVehicle.MultiplyPoint3x4(vertices[index]);
            var frontExterior = bindFrontExterior && point.z >= FrontPanelAnchorMinimumZ &&
                                (!rearInteriorOnly || mixedFrontExterior);
            if (rearInteriorOnly && point.z > RearHatchAnchorRearLimit && !frontExterior)
                continue;
            eligible++;
            if (frontExterior)
                frontEligible++;
            if (!queryResults.TryGetValue(point, out var match))
            {
                // The front grille/lamp recesses are farther from painted skin
                // than the rear lining. Keep the confirmed rear radius intact.
                var radius = frontExterior ? FrontPanelAnchorMaximumDistance : maximumDistance;
                match = tree.TryFindNearest(point, radius, out var nearest, out _)
                    ? nearest : (PanelShellVertex?)null;
                queryResults.Add(point, match);
            }
            if (!match.HasValue)
                continue;
            anchors.Add(new PanelVertexAnchor(index, match.Value.Filter, match.Value.VertexIndex));
            maximumGap = Mathf.Max(maximumGap, Vector3.Distance(point, match.Value.VehiclePosition));
            if (frontExterior)
                frontMapped++;
        }
        KiaCeedSportswagonDiagnostics.DamageInfo(context,
            $"KiaCeedSportswagon damage vehicle={vehicle?.GetInstanceID()}: panel='{filter.name}' " +
            $"anchors={anchors.Count}/{eligible} front={frontMapped}/{frontEligible} " +
            $"uniqueQueries={queryResults.Count} maxGap={maximumGap * 100f:0.0}cm.");
        if (frontMapped != frontEligible)
            context?.Logger.Warn($"KiaCeedSportswagon damage vehicle={vehicle?.GetInstanceID()}: " +
                $"front panel '{filter.name}' has {frontEligible - frontMapped} unbound vertices " +
                $"within the {FrontPanelAnchorMaximumDistance:0.00}m front-shell search.");
        return anchors.ToArray();
    }

    private void ApplyPanelAnchors(Dictionary<MeshFilter, PanelVertexAnchor[]> bindings)
    {
        foreach (var pair in bindings)
        {
            var filter = pair.Key;
            if (filter == null || !currentVertices.TryGetValue(filter, out var vertices))
                continue;
            var baseline = originalVertices[filter];
            var toVehicle = filterToVehicleMatrices[filter];
            var fromVehicle = vehicleToFilterMatrices[filter];
            var changed = 0;
            foreach (var anchor in pair.Value)
            {
                var sourceDelta = currentVertices[anchor.ShellFilter][anchor.ShellVertexIndex] -
                                  originalVertices[anchor.ShellFilter][anchor.ShellVertexIndex];
                var displacement = filterToVehicleMatrices[anchor.ShellFilter].MultiplyVector(sourceDelta);
                var target = fromVehicle.MultiplyPoint3x4(
                    toVehicle.MultiplyPoint3x4(baseline[anchor.InteriorVertexIndex]) + displacement);
                if ((target - vertices[anchor.InteriorVertexIndex]).sqrMagnitude < 0.0000000001f)
                    continue;
                vertices[anchor.InteriorVertexIndex] = target;
                changed++;
            }
            if (changed == 0)
                continue;
            damageMeshes[filter].vertices = vertices;
            damageMeshes[filter].RecalculateBounds();
            KiaCeedSportswagonDiagnostics.DamageInfo(context,
                $"KiaCeedSportswagon damage vehicle={vehicle?.GetInstanceID()}: " +
                $"panel-follow='{filter.name}' vertices={changed}/{vertices.Length}.");
        }
    }

    private static bool IsLightOverlay(MeshFilter filter) =>
        filter.name.StartsWith("KiaCeedSportswagonLightOverlay_", StringComparison.Ordinal);

    private void OnEnable()
    {
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
        GameEvent.onGameEventTriggered -= HandleRepairEvent;
        GameEvent.onGameEventTriggered += HandleRepairEvent;
    }

    private void OnDisable()
    {
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GameEvent.onGameEventTriggered -= HandleRepairEvent;
        if (repairRecoveryCoroutine != null)
            StopCoroutine(repairRecoveryCoroutine);
        repairRecoveryCoroutine = null;
    }

    private void HandleRepairEvent(string eventName)
    {
        if (eventName == "ba:gameevent_vehiclerepaired" && initialized &&
            GameManager.Instance?.selectedVehicle == vehicle)
            RestoreAfterRepair();
    }

    private void HandleVehicleVariablesChanged()
    {
        if (!initialized || damageHandler == null)
            return;

        var currentDamage = damageHandler.Damage;
        var currentSavedDamage = vehicle?.vehicleInstance?.damage ?? 0f;
        var repairDetected =
            (previousDamage > 0.001f && currentDamage <= 0.001f) ||
            (previousSavedDamage > 0.001f && currentSavedDamage <= 0.001f);
        if (repairDetected)
            RestoreAfterRepair();
        previousDamage = damageHandler.Damage;
        previousSavedDamage = vehicle?.vehicleInstance?.damage ?? 0f;
    }

    private void RestoreAfterRepair()
    {
        if (!initialized || damageHandler == null)
            return;
        var currentDamage = damageHandler.Damage;
        var staleDamageBeforeRepair = currentDamage;
        if (currentDamage > 0.001f)
            damageHandler.Repair();
        currentDamage = damageHandler.Damage;
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
            if (currentVertices.TryGetValue(pair.Key, out var current))
                Array.Copy(pair.Value, current, pair.Value.Length);
            if (originalBounds.TryGetValue(pair.Key, out var bounds))
                mesh.bounds = bounds;
        }
        if (repairRecoveryCoroutine != null)
            StopCoroutine(repairRecoveryCoroutine);
        if (isActiveAndEnabled)
            repairRecoveryCoroutine = StartCoroutine(RestoreDrivingStateAfterRepair());
        KiaCeedSportswagonDiagnostics.DamageInfo(
            context,
            $"KiaCeedSportswagon damage vehicle={vehicle?.GetInstanceID()}: " +
            $"workshop repair synchronized NWH damage from {staleDamageBeforeRepair * 100f:0.0}% " +
            $"to {currentDamage * 100f:0.0}% and restored visual body.");
        if (currentDamage > 0.001f)
        {
            context?.Logger.Warn(
                $"KiaCeedSportswagon damage vehicle={vehicle?.GetInstanceID()}: " +
                $"NWH damage remained at {currentDamage * 100f:0.0}% after workshop repair.");
        }
        previousDamage = currentDamage;
        previousSavedDamage = vehicle?.vehicleInstance?.damage ?? 0f;
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

        var impactDeltaVMps = EstimateImpactDeltaVelocity(collision);
        if (impactDeltaVMps < impactThresholdMps)
        {
            LogRejectedCollision(collision, "below-impulse-threshold", 0f, impactDeltaVMps);
            return;
        }

        if (!NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision))
        {
            LogRejectedCollision(collision, "NWH-collision-invalid", 0f, impactDeltaVMps);
            return;
        }

        var timeUntilReady = nextCollisionTime - Time.unscaledTime;
        if (timeUntilReady > 0f)
        {
            LogRejectedCollision(collision, "cooldown", timeUntilReady, impactDeltaVMps);
            return;
        }

        var contactCount = collision.contactCount;
        if (contactCount == 0)
        {
            LogRejectedCollision(collision, "valid-collision-without-contacts", 0f, impactDeltaVMps);
            return;
        }

        var processedContactCount = Mathf.Min(contactCount, MaximumProcessedContacts);
        for (var index = 0; index < processedContactCount; index++)
        {
            var sourceIndex = contactCount <= MaximumProcessedContacts
                ? index
                : Mathf.RoundToInt((contactCount - 1f) * index / (processedContactCount - 1f));
            contactScratch[index] = collision.GetContact(sourceIndex);
        }
        DeformContacts(contactScratch, processedContactCount, impactDeltaVMps, collision);
    }

    // Shared by the live collision path and the shipped-bundle contact verifier.
    // Numerical damage remains owned exclusively by the native handler.
    private void DeformContacts(ContactPoint[] contacts, int processedContactCount,
        float impactDeltaVMps, Collision? collision = null)
    {
        if (!initialized || processedContactCount <= 0 || processedContactCount > contacts.Length)
            return;
        var contactCount = collision?.contactCount ?? processedContactCount;
        try
        {
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            if (collision != null && KiaCeedSportswagonDiagnostics.DamageDiagnosticsEnabled &&
                diagnosticLogs < MaximumDiagnosticLogs)
            {
                StartCoroutine(TraceNativeDamageAfterCollision(
                    collision.collider.name, collision.collider.tag,
                    collision.relativeVelocity.magnitude));
            }
            nextCollisionTime = Time.unscaledTime + CollisionCooldown;

            var processedContacts = contactScratch;
            var impactContacts = impactContactScratch;
            changedMeshNames.Clear();
            changedEndAttachmentNames.Clear();
            changedRequestedTargetResults.Clear();
            var root = transform;
            var center = body != null ? body.worldCenterOfMass : root.position;
            var excessSpeed = impactDeltaVMps - impactThresholdMps;
            var dentDepth = Mathf.Clamp(excessSpeed * DepthPerExcessMps, 0.025f, MaximumDentDepth);
            var frontDentDepth = Mathf.Clamp(
                excessSpeed * FrontDepthPerExcessMps,
                0.04f,
                MaximumFrontDentDepth) * EndImpactDentStrength;
            var rearDentDepth = Mathf.Clamp(
                excessSpeed * RearDepthPerExcessMps,
                0.04f,
                MaximumRearDentDepth) * EndImpactDentStrength;
            for (var processedIndex = 0;
                 processedIndex < processedContactCount;
                 processedIndex++)
            {
                var contact = contacts[processedIndex];
                var localContact = root.InverseTransformPoint(contact.point);
                var isEndContact =
                    Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                    Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                var isFrontContact = isEndContact && localContact.z >= 0f;
                Vector3 localDirection;
                float maximumRadius;
                float inverseLateralRadiusSquared = 0f;
                float inverseLongitudinalRadiusSquared = 0f;
                float selectedDepth;
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
                    inverseLateralRadiusSquared = 1f / (lateralRadius * lateralRadius);
                    inverseLongitudinalRadiusSquared =
                        1f / (longitudinalRadius * longitudinalRadius);
                    maximumRadius = Mathf.Max(
                        lateralRadius,
                        Mathf.Max(verticalRadius, longitudinalRadius));
                    localDirection = isFrontContact ? Vector3.back : Vector3.forward;
                    selectedDepth = isFrontContact ? frontDentDepth : rearDentDepth;
                }
                else
                {
                    var towardCenter = (center - contact.point).normalized;
                    var contactNormal = contact.normal.normalized;
                    var worldDirection = Vector3.Dot(contactNormal, towardCenter) >= 0f
                        ? contactNormal
                        : -contactNormal;
                    localDirection = root.InverseTransformDirection(worldDirection).normalized;
                    maximumRadius = DentRadius;
                    selectedDepth = dentDepth;
                }

                processedContacts[processedIndex] = contact;
                impactContacts[processedIndex] = new DamageContactData(
                    contact.point,
                    localContact,
                    localDirection,
                    isEndContact,
                    isFrontContact,
                    maximumRadius * maximumRadius,
                    inverseLateralRadiusSquared,
                    inverseLongitudinalRadiusSquared,
                    selectedDepth);
            }

            var primaryLocalContact = impactContacts[0].LocalPoint;
            var frontEndOnly = true;
            var rearEndOnly = true;
            for (var impactIndex = 0; impactIndex < processedContactCount; impactIndex++)
            {
                frontEndOnly &= impactContacts[impactIndex].IsEnd &&
                                impactContacts[impactIndex].IsFront;
                rearEndOnly &= impactContacts[impactIndex].IsEnd &&
                               !impactContacts[impactIndex].IsFront;
            }
            var changedMeshes = 0;
            var changedVertices = 0;
            var skippedMeshes = 0;
            var frontImpact = false;
            var rearImpact = false;
            foreach (var filter in deformableFilters)
            {
                if (filter == null || filter.sharedMesh == null ||
                    !currentVertices.TryGetValue(filter, out var vertices) ||
                    !originalVertices.TryGetValue(filter, out var baseline) ||
                    !damageMeshes.TryGetValue(filter, out var mesh))
                    continue;
                var requestedMarker = KiaCeedSportswagonRuntime
                    .GetUserSpecifiedDamageTargetMarker(filter);
                if (IsLightOverlay(filter) ||
                    (panelVertexAnchors.TryGetValue(filter, out var bindings) &&
                     bindings.Length == vertices.Length) ||
                    IsRearInteriorShellFollower(filter))
                    continue;
                var tailgatePanel = IsDamageTailgatePanelTarget(filter);
                // Painted panels own the deformation field; nearby details
                // inherit their actual vertex deltas after this pass.
                var attachedDetail = !tailgatePanel &&
                                     IsAttachedExteriorDetail(filter, vertices.Length);
                var endTarget = !tailgatePanel &&
                                IsEndAttachmentTarget(filter);
                var renderer = filter.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var canReachFilter = false;
                    var bounds = renderer.bounds;
                    for (var impactIndex = 0;
                         impactIndex < processedContactCount;
                         impactIndex++)
                    {
                        var impactContact = impactContacts[impactIndex];
                        if (CanBoundsReachImpact(bounds, impactContact, endTarget))
                        {
                            canReachFilter = true;
                            break;
                        }
                    }
                    if (!canReachFilter)
                    {
                        skippedMeshes++;
                        AddRequestedTargetResult(
                            requestedMarker,
                            filter,
                            "outside-impact-bounds");
                        continue;
                    }
                }

                if (!filterToVehicleMatrices.TryGetValue(filter, out var filterToVehicle) ||
                    !vehicleToFilterMatrices.TryGetValue(filter, out var vehicleToFilter))
                {
                    filterToVehicle = root.worldToLocalMatrix *
                                      filter.transform.localToWorldMatrix;
                    vehicleToFilter = filterToVehicle.inverse;
                    filterToVehicleMatrices[filter] = filterToVehicle;
                    vehicleToFilterMatrices[filter] = vehicleToFilter;
                }

                var meshChanged = false;
                var meshMode = "surface-deformation";
                var changedMeshVertices = 0;
                var meshMaximumShift = 0f;
                if (attachedDetail && endTarget &&
                    TryGetEndAttachmentDisplacement(
                        filter,
                        processedContacts,
                        processedContactCount,
                        frontDentDepth,
                        rearDentDepth,
                        out var rigidDisplacement,
                        out var rigidIsFront))
                {
                    var localDisplacement = root.InverseTransformVector(rigidDisplacement);
                    for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        var vehicleVertex =
                            filterToVehicle.MultiplyPoint3x4(vertices[vertexIndex]);
                        var localVertex = vehicleToFilter.MultiplyPoint3x4(
                            vehicleVertex + localDisplacement);
                        if (vertexIndex < baseline.Length)
                        {
                            var cumulativeLimit = rigidIsFront
                                ? MaximumFrontDentDepth
                                : MaximumRearDentDepth;
                            cumulativeLimit *= EndImpactDentStrength;
                            localVertex = ClampVehicleSpaceDent(
                                localVertex, baseline[vertexIndex], filterToVehicle,
                                vehicleToFilter, cumulativeLimit);
                        }
                        meshMaximumShift = Mathf.Max(
                            meshMaximumShift,
                            filterToVehicle.MultiplyVector(
                                localVertex - vertices[vertexIndex]).magnitude);
                        vertices[vertexIndex] = localVertex;
                    }
                    changedVertices += vertices.Length;
                    changedMeshVertices = vertices.Length;
                    meshChanged = true;
                    meshMode = "panel-follow";
                    frontImpact |= rigidIsFront;
                    rearImpact |= !rigidIsFront;
                }
                else
                {
                    int[]? candidateIndices = null;
                    if (frontEndOnly)
                        frontEndVertexIndices.TryGetValue(filter, out candidateIndices);
                    else if (rearEndOnly)
                        rearEndVertexIndices.TryGetValue(filter, out candidateIndices);
                    var candidateCount = candidateIndices?.Length ?? vertices.Length;
                    for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
                    {
                        var vertexIndex = candidateIndices == null
                            ? candidateIndex : candidateIndices[candidateIndex];
                        var vehicleVertex =
                            filterToVehicle.MultiplyPoint3x4(vertices[vertexIndex]);
                        var strongestInfluence = 0f;
                        var inwardDirection = Vector3.zero;
                        var selectedDepth = dentDepth;
                        var selectedEndImpact = false;
                        var selectedFrontImpact = false;
                        for (var impactIndex = 0;
                             impactIndex < processedContactCount;
                             impactIndex++)
                        {
                            var impactContact = impactContacts[impactIndex];
                            var localDelta = vehicleVertex - impactContact.LocalPoint;
                            float influence;
                            if (impactContact.IsEnd)
                            {
                                float normalizedDistanceSquared;
                                if (endTarget)
                                {
                                    // GLB material groups often combine distinct
                                    // grille, bumper, lamp or tailgate pieces. Keep
                                    // their damage localized to this car end while
                                    // allowing a whole low/high detail to follow
                                    // the impact panel.
                                    var lateralRadius = (impactContact.IsFront
                                        ? FrontDentLateralRadius
                                        : RearDentLateralRadius) * 1.45f;
                                    var longitudinalRadius = (impactContact.IsFront
                                        ? FrontDentLongitudinalRadius
                                        : RearDentLongitudinalRadius) * 1.45f;
                                    normalizedDistanceSquared =
                                        localDelta.x * localDelta.x /
                                        (lateralRadius * lateralRadius) +
                                        localDelta.z * localDelta.z /
                                        (longitudinalRadius * longitudinalRadius);
                                }
                                else
                                {
                                    var verticalRadius = impactContact.IsFront
                                        ? FrontDentVerticalRadius
                                        : RearDentVerticalRadius;
                                    normalizedDistanceSquared =
                                        localDelta.x * localDelta.x *
                                        impactContact.InverseLateralRadiusSquared +
                                        localDelta.y * localDelta.y /
                                        (verticalRadius * verticalRadius) +
                                        localDelta.z * localDelta.z *
                                        impactContact.InverseLongitudinalRadiusSquared;
                                }
                                if (normalizedDistanceSquared >= 1f)
                                    continue;
                                influence = 1f - Mathf.Sqrt(normalizedDistanceSquared);
                            }
                            else
                            {
                                var distanceSquared = localDelta.sqrMagnitude;
                                if (distanceSquared >= DentRadius * DentRadius)
                                    continue;
                                influence = 1f - Mathf.Sqrt(distanceSquared) / DentRadius;
                            }

                            if (influence <= strongestInfluence)
                                continue;
                            strongestInfluence = influence;
                            inwardDirection = impactContact.InwardDirection;
                            selectedDepth = impactContact.Depth;
                            selectedEndImpact = impactContact.IsEnd;
                            selectedFrontImpact = impactContact.IsFront;
                        }

                        if (strongestInfluence <= 0f || inwardDirection.sqrMagnitude < 0.5f)
                            continue;
                        var falloff = selectedEndImpact
                            ? Mathf.Pow(strongestInfluence, 1.35f)
                            : strongestInfluence * strongestInfluence;
                        var deformedVehicleVertex = vehicleVertex +
                            inwardDirection * (selectedDepth * falloff);
                        var localVertex =
                            vehicleToFilter.MultiplyPoint3x4(deformedVehicleVertex);
                        if (vertexIndex < baseline.Length)
                        {
                            var cumulativeLimit = selectedEndImpact
                                ? selectedFrontImpact
                                    ? MaximumFrontDentDepth * EndImpactDentStrength
                                    : MaximumRearDentDepth * EndImpactDentStrength
                                : MaximumDentDepth;
                            localVertex = ClampVehicleSpaceDent(
                                localVertex, baseline[vertexIndex], filterToVehicle,
                                vehicleToFilter, cumulativeLimit);
                        }
                        meshMaximumShift = Mathf.Max(
                            meshMaximumShift,
                            filterToVehicle.MultiplyVector(
                                localVertex - vertices[vertexIndex]).magnitude);
                        vertices[vertexIndex] = localVertex;
                        changedVertices++;
                        changedMeshVertices++;
                        meshChanged = true;
                        frontImpact |= selectedEndImpact && selectedFrontImpact;
                        rearImpact |= selectedEndImpact && !selectedFrontImpact;
                    }
                }

                if (!meshChanged && attachedDetail && endTarget &&
                    TryGetEndAttachmentDisplacement(
                        filter,
                        processedContacts,
                        processedContactCount,
                        frontDentDepth,
                        rearDentDepth,
                        out var attachmentDisplacement,
                        out var attachmentIsFront))
                {
                    var localDisplacement = root.InverseTransformVector(attachmentDisplacement);
                    for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                    {
                        var vehicleVertex =
                            filterToVehicle.MultiplyPoint3x4(vertices[vertexIndex]);
                        var localVertex = vehicleToFilter.MultiplyPoint3x4(
                            vehicleVertex + localDisplacement);
                        if (vertexIndex < baseline.Length)
                        {
                            var cumulativeLimit = attachmentIsFront
                                ? MaximumFrontDentDepth
                                : MaximumRearDentDepth;
                            cumulativeLimit *= EndImpactDentStrength;
                            localVertex = ClampVehicleSpaceDent(
                                localVertex, baseline[vertexIndex], filterToVehicle,
                                vehicleToFilter, cumulativeLimit);
                        }
                        meshMaximumShift = Mathf.Max(
                            meshMaximumShift,
                            filterToVehicle.MultiplyVector(
                                localVertex - vertices[vertexIndex]).magnitude);
                        vertices[vertexIndex] = localVertex;
                    }
                    changedVertices += vertices.Length;
                    changedMeshVertices = vertices.Length;
                    meshChanged = true;
                    meshMode = "panel-follow-fallback";
                    frontImpact |= attachmentIsFront;
                    rearImpact |= !attachmentIsFront;
                }

                if (!meshChanged)
                {
                    AddRequestedTargetResult(
                        requestedMarker,
                        filter,
                        "no-vertices-in-impact-falloff");
                    continue;
                }
                mesh.vertices = vertices;
                if (originalBounds.TryGetValue(filter, out var originalMeshBounds))
                    mesh.bounds = originalMeshBounds;
                changedMeshes++;
                if (changedMeshNames.Count < 12)
                {
                    changedMeshNames.Add(
                        $"{filter.name}:{meshMode}:{changedMeshVertices}/{vertices.Length}v:" +
                        $"maxShift={meshMaximumShift * 100f:0.0}cm");
                }
                if (endTarget && changedEndAttachmentNames.Count < 12)
                    changedEndAttachmentNames.Add(filter.name);
                AddRequestedTargetResult(
                    requestedMarker,
                    filter,
                    $"{meshMode}:{changedMeshVertices}/{vertices.Length}v:" +
                    $"maxShift={meshMaximumShift * 100f:0.0}cm");
            }

            // Apply body followers first, then lamp-owned overlays. Both use
            // the master's cumulative delta without an independent falloff/cap.
            ApplyPanelAnchors(panelVertexAnchors);
            ApplyPanelAnchors(lampOverlayVertexAnchors);

            var processingMilliseconds =
                (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt) *
                1000d / System.Diagnostics.Stopwatch.Frequency;
            var postImpactVelocity = body != null ? body.velocity : Vector3.zero;
            var postImpactAngularVelocity = body != null ? body.angularVelocity : Vector3.zero;
            if (!slowProcessingWarningReported &&
                processingMilliseconds >= SlowProcessingWarningMilliseconds)
            {
                slowProcessingWarningReported = true;
                context?.Logger.Warn(
                    $"KiaCeedSportswagon damage vehicle={vehicle?.GetInstanceID()}: " +
                    $"collision deformation took {processingMilliseconds:0.0}ms " +
                    $"(contacts={processedContactCount}/{contactCount}, " +
                    $"meshes={changedMeshes}, skippedMeshes={skippedMeshes}, " +
                    $"vertices={changedVertices}); further slow-impact warnings suppressed.");
            }

            if (diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                KiaCeedSportswagonDiagnostics.DamageInfo(
                    context,
                    $"KiaCeedSportswagon damage vehicle={vehicle?.GetInstanceID()}: inward dent " +
                    $"contact='{collision?.collider?.name ?? "verification"}' " +
                    $"relativeSpeed={(collision?.relativeVelocity.magnitude ?? 0f) * 3.6f:0.0}kph " +
                    $"estimatedDeltaV={impactDeltaVMps:0.00}mps " +
                    $"localContact=({primaryLocalContact.x:0.00}," +
                    $"{primaryLocalContact.y:0.00},{primaryLocalContact.z:0.00}) " +
                    $"region={(frontImpact ? "front" : rearImpact ? "rear" : "side")} " +
                    $"depth={(frontImpact ? frontDentDepth : rearImpact ? rearDentDepth : dentDepth):0.000}m " +
                    $"contacts={processedContactCount}/{contactCount} meshes={changedMeshes} " +
                    $"skippedMeshes={skippedMeshes} vertices={changedVertices} " +
                    $"processing={processingMilliseconds:0.0}ms " +
                    $"impulse={(collision?.impulse.magnitude ?? 0f):0}N*s " +
                    $"postVelocity=({postImpactVelocity.x:0.0},{postImpactVelocity.y:0.0}," +
                    $"{postImpactVelocity.z:0.0})m/s " +
                    $"postAngular=({postImpactAngularVelocity.x:0.00}," +
                    $"{postImpactAngularVelocity.y:0.00},{postImpactAngularVelocity.z:0.00})rad/s " +
                    $"changed=[{string.Join(",", changedMeshNames)}] " +
                    $"endParts=[{string.Join(",", changedEndAttachmentNames)}] " +
                    $"requestedParts=[{string.Join(";", changedRequestedTargetResults)}] " +
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
                $"KiaCeedSportswagon damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LogRejectedCollision(
        Collision collision,
        string reason,
        float cooldownRemaining,
        float impactDeltaVMps)
    {
        if (!KiaCeedSportswagonDiagnostics.DamageDiagnosticsEnabled ||
            collisionGateDiagnosticLogs >= MaximumCollisionGateDiagnosticLogs)
        {
            return;
        }

        collisionGateDiagnosticLogs++;
        KiaCeedSportswagonDiagnostics.DamageInfo(
            context,
            $"KiaCeedSportswagon damage vehicle={vehicle?.GetInstanceID()}: " +
            $"collision skipped reason={reason} " +
            $"collider='{collision.collider?.name ?? "unknown"}' " +
            $"relativeSpeed={collision.relativeVelocity.magnitude * 3.6f:0.0}kph " +
            $"estimatedDeltaV={impactDeltaVMps:0.00}mps " +
            $"deltaVThreshold={impactThresholdMps:0.00}mps " +
            $"cooldownRemaining={cooldownRemaining:0.000}s " +
            $"contacts={collision.contactCount} " +
            $"nwhDamage={(damageHandler?.Damage ?? 0f) * 100f:0.0}% " +
            $"vehicleDamage={(vehicle?.vehicleInstance?.damage ?? 0f) * 100f:0.0}%.");
    }

    private float EstimateImpactDeltaVelocity(Collision collision)
    {
        var mass = body != null ? body.mass : 0f;
        return mass > 0f ? collision.impulse.magnitude / mass : 0f;
    }

    private IEnumerator TraceNativeDamageAfterCollision(string colliderName, string colliderTag, float speed)
    {
        // Read after the native CarController synchronization; this path never
        // writes damage or overrides native rejection/settings decisions.
        yield return null;
        if (damageHandler == null || vehicle == null)
            yield break;
        var members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var nativeType = damageHandler.GetType();
        var visualOnly = nativeType.GetField("visualOnly", members)?.GetValue(damageHandler);
        var lastCollisionTime = nativeType.GetField("lastCollisionTime", members)?.GetValue(damageHandler);
        KiaCeedSportswagonDiagnostics.DamageInfo(context,
            $"KiaCeedSportswagon native damage vehicle={vehicle.GetInstanceID()}: soleOwner=NWH " +
            $"collider='{colliderName}' tag='{colliderTag}' relativeSpeed={speed:0.00}mps " +
            $"enabled={damageHandler.enabled} visualOnly={visualOnly} lastCollisionTime={lastCollisionTime} " +
            $"physics={damageHandler.Damage:0.000} saved={vehicle.vehicleInstance?.damage:0.000}.");
    }

    private void AddRequestedTargetResult(
        string? requestedMarker,
        MeshFilter filter,
        string result)
    {
        if (requestedMarker == null || changedRequestedTargetResults.Count >= 24)
            return;

        changedRequestedTargetResults.Add(
            $"{requestedMarker}=>{filter.name}:{result}");
    }

    private static Vector3 ClampVehicleSpaceDent(
        Vector3 candidate,
        Vector3 baseline,
        Matrix4x4 filterToVehicle,
        Matrix4x4 vehicleToFilter,
        float maximumMeters)
    {
        var baselineVehicle = filterToVehicle.MultiplyPoint3x4(baseline);
        var candidateVehicle = filterToVehicle.MultiplyPoint3x4(candidate);
        return vehicleToFilter.MultiplyPoint3x4(
            baselineVehicle + Vector3.ClampMagnitude(
                candidateVehicle - baselineVehicle, maximumMeters));
    }

    private bool CanBoundsReachImpact(
        Bounds bounds,
        DamageContactData impactContact,
        bool endTarget)
    {
        if (bounds.SqrDistance(impactContact.WorldPoint) <=
            impactContact.MaximumRadiusSquared)
        {
            return true;
        }

        if (!endTarget || !impactContact.IsEnd)
            return false;

        // The general surface falloff is three-dimensional. Low bumper/grille
        // parts and high lamp covers must still pass the end-panel prefilter.
        var nearestPoint = bounds.ClosestPoint(impactContact.WorldPoint);
        var localDelta = transform.InverseTransformVector(
            nearestPoint - impactContact.WorldPoint);
        var lateralRadius = (impactContact.IsFront
            ? FrontDentLateralRadius
            : RearDentLateralRadius) * 1.45f;
        var longitudinalRadius = (impactContact.IsFront
            ? FrontDentLongitudinalRadius
            : RearDentLongitudinalRadius) * 1.45f;
        var normalizedDistanceSquared =
            localDelta.x * localDelta.x / (lateralRadius * lateralRadius) +
            localDelta.z * localDelta.z / (longitudinalRadius * longitudinalRadius);
        return normalizedDistanceSquared <= 1f;
    }

    private static bool IsAttachedExteriorDetail(MeshFilter filter, int vertexCount)
    {
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;
        var size = renderer.bounds.size;
        var longestSide = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        // Only move a mesh as one rigid attachment when its bounds describe a
        // local part. Some GLB material groups contain matching pieces at both
        // ends of the car; translating those full-car bounds would detach them.
        return longestSide <= 0.32f ||
               (longestSide <= 2.15f &&
                (IsDamageAttachmentDetail(filter) || vertexCount <= 400));
    }

    private static bool IsMixedFrontExteriorShellFollower(MeshFilter filter) =>
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Int_Ceed_SW_Centre_Console_selfillumshiny__interior.001_blackpaint.001_0");

    private static bool IsRearInteriorShellFollower(MeshFilter filter) =>
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Int_Sportswagon_base_leather.001_leather.001_0") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Int_fabrics__carpet.001_fabrics__carpet.001_0") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Int_boot_static_GTL_left_plasticdiffuse__darkbump1.002_darkbump1.001_0") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Int_boot_static_w_speakers_selfillumrough__speakers.001_speakers_0") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Int_Ceed_SW_Centre_Console_selfillumshiny__interior.001_blackpaint.001_0");

    private bool IsEndAttachmentTarget(MeshFilter filter) =>
        filter.name.StartsWith("KiaCeedSportswagonLightOverlay_", StringComparison.Ordinal) ||
        IsDamageAttachmentDetail(filter) ||
        IsDamageEndPanelTarget(filter);

    private static bool IsDamageAttachmentDetail(MeshFilter filter) =>
        IsDamageLampSurface(filter) ||
        HasDamageNameMarker(filter.transform, "grille") ||
        HasDamageNameMarker(filter.transform, "grill") ||
        HasDamageNameMarker(filter.transform, "plate") ||
        HasDamageNameMarker(filter.transform, "license") ||
        HasDamageNameMarker(filter.transform, "kennzeichen") ||
        HasDamageNameMarker(filter.transform, "badge") ||
        HasDamageNameMarker(filter.transform, "emblem") ||
        HasDamageNameMarker(filter.transform, "logo") ||
        HasDamageMaterialMarker(filter,
            "grille_pattern", "grille_S", "chrome_R", "chrome.001", "chrome_line",
            "chrome_pink", "chrome_dark", "plate", "license", "kennzeichen", "badge", "emblem") ||
        KiaCeedSportswagonRuntime.IsUserSpecifiedAttachmentTarget(filter);

    private static bool IsDamageEndPanelTarget(MeshFilter filter) =>
        HasDamageNameMarker(filter.transform, "bumper") ||
        HasDamageNameMarker(filter.transform, "tailgate") ||
        HasDamageNameMarker(filter.transform, "trunk") ||
        HasDamageNameMarker(filter.transform, "boot") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Int_Ceed_SW_Centre_Console_selfillumshiny__interior.001_blackpaint.001") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Int_Sportswagon_base_aluminiumbrushed.003_aluminiumbrushed.001") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Ext_nl_sportswagon_glt_2019_Door_FL_carpaint.001_CarPaint_0") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Ext_Sportswagon_base_Door_Trunk_plasticdiffuse__orange_flip_orange_0") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Int_boot_static_GTL_left_plasticdiffuse__darkbump1.002_darkbump1.001_0") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Int_Ceed_SW_Centre_Console_buttons_GOLD.004_plasticdiffuse.001_0") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Ext_sunroof_glasstintedlight.002_red_glass_0") ||
        KiaCeedSportswagonRuntime.MatchesUserSpecifiedTarget(
            filter,
            "Ext_overhead_lighting_plasticshiny.002_plasticshiny.001_0");

    private static bool IsDamageTailgatePanelTarget(MeshFilter filter) =>
        IsPaintedBodyTailgateTarget(filter) ||
        HasDamageNameMarker(filter.transform, "tailgate") ||
        HasDamageNameMarker(filter.transform, "trunk");

    private static bool IsPaintedBodyTailgateTarget(MeshFilter filter) =>
        filter.name.StartsWith("KiaCeedSportswagonDamageBody", StringComparison.Ordinal) ||
        HasDamageMaterialMarker(filter, "CarPaint");

    private static bool IsDamageLampSurface(MeshFilter filter) =>
        HasDamageNameMarker(filter.transform, "headlight") ||
        HasDamageNameMarker(filter.transform, "taillight") ||
        HasDamageNameMarker(filter.transform, "upperrearlight") ||
        HasDamageNameMarker(filter.transform, "LightsMain") ||
        HasDamageMaterialMarker(filter, "light_glass", "red_glass", "chrome_lights");

    private static bool HasDamageNameMarker(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static bool HasDamageMaterialMarker(MeshFilter filter, params string[] markers)
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

    private bool TryGetEndAttachmentDisplacement(
        MeshFilter filter,
        ContactPoint[] contacts,
        int contactCount,
        float frontDentDepth,
        float rearDentDepth,
        out Vector3 displacement,
        out bool isFront)
    {
        displacement = Vector3.zero;
        isFront = false;
        var renderer = filter.GetComponent<Renderer>();
        if (renderer == null)
            return false;

        var attachmentBounds = renderer.bounds;
        var strongestInfluence = 0f;
        var selectedDepth = 0f;
        Vector3 direction = Vector3.zero;
        for (var contactIndex = 0; contactIndex < contactCount; contactIndex++)
        {
            var contact = contacts[contactIndex];
            var localContact = transform.InverseTransformPoint(contact.point);
            var endContact = Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                             Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
            if (!endContact)
                continue;

            var front = localContact.z >= 0f;
            // Sample the nearest point on the mesh bounds, so low bumper
            // pieces, high lamp covers and thin emblems are still tied to an
            // impact when their geometric centers are far above or below it.
            var attachmentPoint = attachmentBounds.ClosestPoint(contact.point);
            var delta = transform.InverseTransformVector(attachmentPoint - contact.point);
            var lateralRadius = (front ? FrontDentLateralRadius : RearDentLateralRadius) * 1.45f;
            var longitudinalRadius =
                (front ? FrontDentLongitudinalRadius : RearDentLongitudinalRadius) * 1.45f;
            // Height is intentionally omitted here. The shell uses a vertical
            // falloff, while a separate lamp or grille must follow its end
            // panel even when the contact is low on the bumper.
            var normalizedDistance = Mathf.Sqrt(
                delta.x * delta.x / (lateralRadius * lateralRadius) +
                delta.z * delta.z / (longitudinalRadius * longitudinalRadius));
            var influence = 1f - normalizedDistance;
            if (influence <= strongestInfluence)
                continue;

            strongestInfluence = influence;
            selectedDepth = front ? frontDentDepth : rearDentDepth;
            direction = front ? -transform.forward : transform.forward;
            isFront = front;
        }

        if (strongestInfluence <= 0f)
            return false;
        // Interior trim and lamp overlays must follow the same inward panel
        // movement as the outer shell. A separate, smaller attachment cap left
        // the boot trim protruding and could tuck the rear lamps behind the body.
        var attachmentWeight = Mathf.Lerp(0.80f, 1.0f, strongestInfluence);
        displacement = direction * (selectedDepth * attachmentWeight);
        return displacement.sqrMagnitude > 0.000001f;
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
public sealed class KiaCeedSportswagonAerodynamicDrag : MonoBehaviour
{
    private const float AirDensityKgPerCubicMeter = 1.225f;
    // Supplemental drag on top of the game's own wheel and drivetrain losses.
    // The 15:19 diagnostic run reached 100 km/h in 8.88 s but only 193.8 km/h
    // after 44 s of full throttle. Reduce the supplemental load above 100 km/h
    // toward a plausible roughly 50 s 0-200 without changing launch torque.
    private const float EffectiveDragAreaSquareMeters = 0.16f;

    private Rigidbody? body;
    private bool initialized;

    internal void Initialize(Rigidbody target, ModContext? context)
    {
        if (initialized && body == target)
            return;

        body = target;
        initialized = target != null;
        KiaCeedSportswagonDiagnostics.Info(
            context,
            $"KiaCeedSportswagon dynamics vehicle={target?.GetInstanceID()}: " +
            $"enabled quadratic highway drag CdA={EffectiveDragAreaSquareMeters:0.00}m² " +
            $"airDensity={AirDensityKgPerCubicMeter:0.000}kg/m³; Unity linear drag=0.");
    }

    private void FixedUpdate()
    {
        if (!initialized || body == null || body.isKinematic)
            return;

        var airVelocity = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
        var speedSquared = airVelocity.sqrMagnitude;
        if (speedSquared < 1f)
            return;

        var dragForce = 0.5f * AirDensityKgPerCubicMeter *
                        EffectiveDragAreaSquareMeters * speedSquared;
        body.AddForce(-airVelocity.normalized * dragForce, ForceMode.Force);
    }
}

