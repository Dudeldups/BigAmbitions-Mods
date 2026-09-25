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

public sealed class BMWM4G82Runtime : MonoBehaviour
{
    private static BMWM4G82Runtime? activeRuntime;
    private const string VehicleRepainterColorRestoredEvent =
        "vehicle-repainter:color-restored";
    private const string VehicleRepainterColorPreviewEvent =
        "vehicle-repainter:color-preview";
    private const string VehicleRepainterColorResetEvent =
        "vehicle-repainter:color-reset";
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1775f;
    private const float VehicleLinearDrag = 0.015f;
    private const float EnginePowerKw = 350f;
    private const float EngineIdleRpm = 800f;
    private const float EngineLimitRpm = 7200f;
    private const float SpeedLimitKph = 290f;
    private const float FinalDriveRatio = 3.154f;
    private const float EngineInertia = 0.14f;
    private const float EngineStartDuration = 0.55f;
    private const float ClutchEngagementRpm = 1150f;
    private const float ClutchThrottleOffsetRpm = 450f;
    private const float ClutchEngagementRange = 500f;
    private const float ClutchCreepTorque = 0f;
    private const float ForwardTireGrip = 0.50f;
    private const float TireFrictionCircleStrength = 0.82f;
    private const float BrakeMaxTorque = 32000f;
    private const float BrakeActuationTime = 0.010f;
    private const float MaximumDepenetrationVelocity = 4.5f;
    private const float AntiRollBarForce = 8400f;
    private const float SteeringDegreesPerSecond = 95f;
    private const float MaximumSteerAngle = 32f;
    private const float FrontSuspensionTravel = 0.07f;
    private const float RearSuspensionTravel = 0.07f;
    private const float SuspensionBumpRate = 15000f;
    private const float SuspensionReboundRate = 17000f;
    private const float SuspensionExtensionSpeed = 6f;
    private const float MaximumDamagedWheelWobbleAngle = 1.5f;
    private const float DeformationStrength = 0.17f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 0.62f;
    private const float DamageDecelerationThreshold = 500f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const float WarehouseExitEntranceSearchRadius = 12f;
    private const float WarehouseExitTriggerClearance = 0.25f;
    private const int EngineStartAttemptCount = 3;
    private static readonly Vector3 DriverExitPosition = new Vector3(-2.05f, 0.20f, 0.15f);
    private static readonly Vector3 PassengerExitPosition = new Vector3(2.05f, 0.20f, 0.15f);
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.06f, -0.08f);

    private static readonly float[] M4Gears =
    {
        -3.478f,
        0f,
        5.000f,
        3.200f,
        2.143f,
        1.720f,
        1.313f,
        1.000f,
        0.823f,
        0.640f,
    };

    private static readonly HashSet<string> ExplicitDeformableExteriorNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Object_63",
            "Object_83",
            // NameKeyRenderers maps source Object_83 (the windshield/cabin
            // glass shell) to this stable production-prefab name.
            "BMW_CabinGlass_Object_83",
            "Object_134",
            "Object_138",
            "Object_154",
            "Object_171",
            "Object_191",
            "Object_203",
            "Object_215",
        };

    private static AnimationCurve CreateM4PowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.12f, 0.07f),
            new Keyframe(0.25f, 0.25f),
            new Keyframe(0.38f, 0.48f),
            new Keyframe(0.55f, 0.70f),
            new Keyframe(0.68f, 0.88f),
            new Keyframe(0.76f, 0.98f),
            new Keyframe(0.87f, 1f),
            new Keyframe(1f, 0.90f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? powertrainReadinessCoroutine;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private bool dealerReady;
    private bool dealerReadyLogged;
    private bool privateDriverPoolReady;
    private bool privateDriverContractReady;
    private bool privateDriverRegistrationAllowed;
    private GameObject? playerVehiclePrefab;

    public static BMWM4G82Runtime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<BMWM4G82Runtime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(BMWM4G82Runtime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<BMWM4G82Runtime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        runtime.dealerReady = false;
        runtime.dealerReadyLogged = false;
        runtime.privateDriverPoolReady = false;
        runtime.privateDriverContractReady = false;
        runtime.privateDriverRegistrationAllowed = false;
        activeRuntime = runtime;
        var trafficFrequency = runtime.GetComponent<BMWM4G82TrafficFrequency>() ??
                               runtime.gameObject.AddComponent<BMWM4G82TrafficFrequency>();
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
        configuredVehicleIds.Clear();
        dealerReady = false;
        dealerReadyLogged = false;
        privateDriverPoolReady = false;
        privateDriverContractReady = false;
        privateDriverRegistrationAllowed = false;
        BMWM4G82PrivateDriverSupport.RemoveVehicle(vehicleTypeName);
        playerVehiclePrefab = null;
        if (ReferenceEquals(activeRuntime, this))
            activeRuntime = null;
        Destroy(gameObject);
    }

    internal static bool ConfigureSpawnedVehicle(VehicleController? vehicle)
    {
        return activeRuntime != null &&
               activeRuntime.TryConfigureVehicle(vehicle, "prefab-start");
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
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onExitBuilding -= HandleBuildingExited;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
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
        if (BMWM4G82LoadRecovery.CompleteInterruptedLoad(context))
            StartCoroutine(ReportLoadedInputState());
        privateDriverRegistrationAllowed = true;
        ScheduleInitialization("game-loaded-late");
    }

    private IEnumerator ReportLoadedInputState()
    {
        // The native loading screen fades out for 0.8 seconds after this event.
        yield return new WaitForSecondsRealtime(2f);
        BMWM4G82LoadRecovery.ReportInputState(context);
    }

    private void HandleGameUnloaded()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        if (powertrainReadinessCoroutine != null)
            StopCoroutine(powertrainReadinessCoroutine);
        initializationCoroutine = null;
        powertrainReadinessCoroutine = null;
        configuredVehicleIds.Clear();
        dealerReady = false;
        dealerReadyLogged = false;
        privateDriverPoolReady = false;
        privateDriverContractReady = false;
        privateDriverRegistrationAllowed = false;
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
            .GetComponent<BMWM4G82PaintController>()
            ?.ApplyCurrentColor("vehicle-repainter", true);
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

            vehicle!.GetComponent<BMWM4G82PaintController>()
                ?.ApplyCurrentColor("vehicle-repainter-restored", true);
        }
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        if (IsTargetVehicle(vehicle) &&
            !configuredVehicleIds.Contains(vehicle.GetInstanceID()))
        {
            context?.Logger.Warn(
                $"BMWM4G82: vehicle entered before spawn configuration " +
                $"instance={vehicle.GetInstanceID()}; applying fallback.");
        }
        TryConfigureVehicle(vehicle, "vehicle-entered-fallback");
        vehicle?.GetComponent<BMWM4G82GlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle != null && IsTargetVehicle(vehicle))
        {
            if (powertrainReadinessCoroutine != null)
                StopCoroutine(powertrainReadinessCoroutine);
            powertrainReadinessCoroutine = StartCoroutine(EnsureDrivetrainReadyAfterEntry(vehicle));
        }
    }

    private IEnumerator EnsureDrivetrainReadyAfterEntry(VehicleController vehicle)
    {
        // Native entry starts the engine asynchronously. A running flag can be
        // true while the engine is still at zero RPM, which leaves throttle
        // input connected but produces no wheel torque until a later re-entry.
        yield return new WaitForSecondsRealtime(0.25f);
        var physicsVehicle = vehicle == null
            ? null
            : vehicle.GetComponent<NWH.VehiclePhysics2.VehicleController>() ??
              vehicle.GetComponentInChildren<NWH.VehiclePhysics2.VehicleController>(true);
        if (physicsVehicle == null)
        {
            context?.Logger.Warn(
                $"BMWM4G82: post-entry drivetrain unavailable vehicle={vehicle?.GetInstanceID()}.");
            powertrainReadinessCoroutine = null;
            yield break;
        }

        var engine = physicsVehicle.powertrain.engine;
        var transmission = physicsVehicle.powertrain.transmission;
        for (var attempt = 1; attempt <= EngineStartAttemptCount; attempt++)
        {
            if (vehicle == null || !vehicle.controlledByPlayer || !IsTargetVehicle(vehicle))
                break;
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            if (engine.IsRunning && engine.ignition && engine.canRun &&
                rpm >= MinimumHealthyEngineRpm)
            {
                if (transmission.Gear <= 0)
                    transmission.ShiftInto(1, true);
                context?.Logger.Info(
                    $"BMWM4G82: post-entry drivetrain ready vehicle={vehicle.GetInstanceID()} " +
                    $"attempt={attempt} running={engine.IsRunning} rpm={rpm:0} " +
                    $"gear={transmission.Gear}.");
                powertrainReadinessCoroutine = null;
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

        var finalRpm = engine.RPMPercent * engine.revLimiterRPM;
        context?.Logger.Warn(
            $"BMWM4G82: post-entry drivetrain remained unavailable " +
            $"vehicle={vehicle?.GetInstanceID()} controlled={vehicle?.controlledByPlayer} " +
            $"running={engine.IsRunning} rpm={finalRpm:0} gear={transmission.Gear}.");
        powertrainReadinessCoroutine = null;
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (dealerReady ||
            !BMWM4G82LuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
            return;

        if (BusinessLayoutSetHelper.loadingLayouts)
            ScheduleInitialization("dealer-entered");
        else
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
            return;

        var nearestEntrance = FindClosestDriveInEntrance(
            vehicle.transform.position,
            out var distance);
        if (nearestEntrance == null || distance > WarehouseExitEntranceSearchRadius)
        {
            context?.Logger.Warn(
                $"BMWM4G82: warehouse exit guard could not resolve the nearby drive-in " +
                $"entrance vehicle={vehicle.GetInstanceID()} distance={distance:0.00}m.");
            return;
        }

        if (!TryGetWarehouseEnterTriggerBounds(nearestEntrance, out var triggerBounds) ||
            !TryGetWorldBodyColliderBounds(vehicle.transform, out var bodyBounds))
        {
            context?.Logger.Warn(
                $"BMWM4G82: warehouse exit correction could not resolve fitted bounds " +
                $"vehicle={vehicle.GetInstanceID()} entrance='{nearestEntrance.name}'.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(nearestEntrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < 0.0001f)
            return;
        outward.Normalize();

        // Native warehouse exit offsets the spawn by the source MeshCollider's
        // local bounds. Those bounds do not describe the fitted BMW body, so its
        // physical collider can still overlap the child EnterTrigger and start
        // another entry immediately. Move only far enough toward the garage door
        // for the fitted body to clear that trigger before the next physics step.
        var triggerMaximum = Vector3.Dot(triggerBounds.center, outward) +
                             ProjectBoundsExtent(triggerBounds.extents, outward);
        var bodyMinimum = Vector3.Dot(bodyBounds.center, outward) -
                          ProjectBoundsExtent(bodyBounds.extents, outward);
        var correctionDistance = triggerMaximum + WarehouseExitTriggerClearance - bodyMinimum;
        if (correctionDistance <= 0f)
            return;

        vehicle.transform.position += outward * correctionDistance;
        Physics.SyncTransforms();
        context?.Logger.Info(
            $"BMWM4G82: corrected warehouse exit trigger overlap " +
            $"vehicle={vehicle.GetInstanceID()} entrance='{nearestEntrance.name}' " +
            $"entranceDistance={distance:0.00}m correction={correctionDistance:0.00}m.");
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

    private static bool TryGetWarehouseEnterTriggerBounds(
        DriveInEntrance entrance,
        out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var trigger in entrance.GetComponentsInChildren<DriveInEntranceEnterTrigger>(true))
        foreach (var collider in trigger.GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled || !collider.isTrigger)
                continue;
            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return found;
    }

    private static bool TryGetWorldBodyColliderBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(child.name, "BodyCollider", StringComparison.Ordinal))
                continue;

            foreach (var collider in child.GetComponents<BoxCollider>())
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                    continue;
                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
        }

        return found;
    }

    private static float ProjectBoundsExtent(Vector3 extents, Vector3 worldAxis) =>
        Mathf.Abs(worldAxis.x) * extents.x +
        Mathf.Abs(worldAxis.y) * extents.y +
        Mathf.Abs(worldAxis.z) * extents.z;

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (!isOpen)
        {
            ConfigureExistingVehicles(out _);
            return;
        }
        if (!dealerReady && BusinessLayoutSetHelper.loadingLayouts)
            ScheduleInitialization("full-menu");
        else if (!dealerReady)
            EnsureDealerStock("full-menu");
        if (privateDriverRegistrationAllowed &&
            (!privateDriverContractReady || !privateDriverPoolReady))
            EnsurePrivateDriverSupport("full-menu");
    }

    private void HandleVehicleVariablesChanged() => ConfigureExistingVehicles(out _);

    private void ScheduleInitialization(string source)
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(InitializeForLifecycle(source));
    }

    private IEnumerator InitializeForLifecycle(string source)
    {
        if (!privateDriverPoolReady && playerVehiclePrefab != null)
            privateDriverPoolReady = TryPreparePrivateDriverPool();

        while (!dealerReady && BusinessLayoutSetHelper.loadingLayouts)
        {
            ConfigureExistingVehicles(out _);
            yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        var previousMatchedCount = -1;
        var stablePasses = 0;
        var maximumMatchedCount = 0;

        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            if (!privateDriverPoolReady && playerVehiclePrefab != null)
                privateDriverPoolReady = TryPreparePrivateDriverPool();
            if (!dealerReady)
                dealerReady = EnsureDealerStock(source);
            if (privateDriverRegistrationAllowed && !privateDriverContractReady)
                EnsurePrivateDriverSupport(source);
            ConfigureExistingVehicles(out var matchedCount);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);

            var servicesReady = dealerReady &&
                                privateDriverPoolReady &&
                                (!privateDriverRegistrationAllowed ||
                                 privateDriverContractReady);
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
                $"BMWM4G82: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
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
            dealerReady = BMWM4G82LuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            if (dealerReady && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                context?.Logger.Info(
                    $"BMWM4G82: available at The Hamptons Axis and Manhattan Luxury Cars " +
                    $"source='{source}'.");
            }
            return dealerReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"BMWM4G82: dealer stock update failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool EnsurePrivateDriverSupport(string source)
    {
        if (privateDriverContractReady && privateDriverPoolReady)
            return true;
        if (!privateDriverRegistrationAllowed)
            return false;

        if (!privateDriverContractReady)
            privateDriverContractReady = TryRegisterPrivateDriverContracts();
        if (!privateDriverPoolReady && playerVehiclePrefab != null)
            privateDriverPoolReady = TryPreparePrivateDriverPool();

        return privateDriverContractReady && privateDriverPoolReady;
    }

    private bool TryRegisterPrivateDriverContracts()
    {
        try
        {
            return BMWM4G82PrivateDriverSupport.EnsureVehicleAvailable(
                vehicleTypeName);
        }
        catch
        {
            return false;
        }
    }

    private bool TryPreparePrivateDriverPool()
    {
        if (playerVehiclePrefab == null)
            return false;

        try
        {
            return BMWM4G82PrivateDriverSupport.PrepareTrafficPool(
                playerVehiclePrefab);
        }
        catch
        {
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
            TryConfigureVehicle(vehicle, "existing-vehicle-scan");
        }
    }

    private bool TryConfigureVehicle(VehicleController? vehicle, string source)
    {
        if (!IsTargetVehicle(vehicle))
            return false;

        var targetVehicle = vehicle!;
        var instanceId = targetVehicle.GetInstanceID();
        if (!configuredVehicleIds.Add(instanceId))
        {
            targetVehicle.GetComponent<BMWM4G82PaintController>()
                ?.ApplyCurrentColor(source);
            return true;
        }

        try
        {
            var rigidbody = targetVehicle.GetComponent<Rigidbody>() ??
                            targetVehicle.GetComponentInParent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.mass = VehicleMass;
                rigidbody.centerOfMass = StableCenterOfMass;
                rigidbody.drag = VehicleLinearDrag;
                rigidbody.angularDrag = 1.90f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.maxDepenetrationVelocity = MaximumDepenetrationVelocity;
                rigidbody.solverIterations = Math.Max(rigidbody.solverIterations, 12);
                rigidbody.solverVelocityIterations = Math.Max(
                    rigidbody.solverVelocityIterations,
                    4);
            }

            ConfigureMassProperties(targetVehicle.gameObject);
            ConfigureWheelControllers(targetVehicle.gameObject);
            ConfigureBodyColliders(targetVehicle.gameObject);
            var normalizedNavMeshObstacles = ConfigureNavMeshObstacles(targetVehicle.gameObject);
            ConfigureExitMarkers(targetVehicle.gameObject);
            var settlingController = targetVehicle.GetComponent<BMWM4G82SettlingController>();
            if (settlingController == null)
                settlingController = targetVehicle.gameObject.AddComponent<BMWM4G82SettlingController>();
            settlingController.Initialize(targetVehicle);
            var powertrainConfigured = ConfigurePowertrain(targetVehicle.gameObject);
            var caliperController = targetVehicle.GetComponent<BMWM4G82CaliperController>();
            if (caliperController == null)
                caliperController = targetVehicle.gameObject.AddComponent<BMWM4G82CaliperController>();
            caliperController.Initialize(targetVehicle, context);
            var materialOwner = targetVehicle.GetComponent<BMWM4G82RuntimeMaterialOwner>();
            if (materialOwner == null)
                materialOwner = targetVehicle.gameObject.AddComponent<BMWM4G82RuntimeMaterialOwner>();
            var runtimeMaterialCount = materialOwner.Initialize();
            var materialResult = BMWM4G82Materials.FixSolidMaterials(targetVehicle.gameObject);
            var caliperMaterialCount = BMWM4G82Materials.ApplyCaliperFinish(targetVehicle.gameObject);
            var paintController = targetVehicle.GetComponent<BMWM4G82PaintController>();
            if (paintController == null)
                paintController = targetVehicle.gameObject.AddComponent<BMWM4G82PaintController>();
            paintController.Initialize(targetVehicle, context);
            var glassController = targetVehicle.GetComponent<BMWM4G82GlassController>();
            if (glassController == null)
                glassController = targetVehicle.gameObject.AddComponent<BMWM4G82GlassController>();
            glassController.Initialize(context);
            var lightingController = targetVehicle.GetComponent<BMWM4G82LightingController>();
            if (lightingController == null)
                lightingController = targetVehicle.gameObject.AddComponent<BMWM4G82LightingController>();
            lightingController.Initialize(targetVehicle, context);
            // Lighting overlays must exist before the damage controller captures
            // its per-instance meshes, otherwise lit surfaces stay behind when
            // the surrounding lamp and body geometry dents.
            var deformableBodyMeshes = ConfigureVisualDamage(targetVehicle);
            var driverController = targetVehicle.GetComponent<BMWM4G82DriverController>();
            if (driverController == null)
                driverController = targetVehicle.gameObject.AddComponent<BMWM4G82DriverController>();
            driverController.Initialize(targetVehicle, context);
            var audioController = targetVehicle.GetComponent<BMWM4G82AudioController>();
            if (audioController == null)
                audioController = targetVehicle.gameObject.AddComponent<BMWM4G82AudioController>();
            audioController.Initialize(targetVehicle, context);
            var accelerationTelemetry =
                targetVehicle.GetComponent<BMWM4G82AccelerationTelemetry>();
            if (accelerationTelemetry == null)
            {
                accelerationTelemetry = targetVehicle.gameObject
                    .AddComponent<BMWM4G82AccelerationTelemetry>();
            }
            accelerationTelemetry.Initialize(targetVehicle, context);

            context?.Logger.Info(
                $"BMWM4G82: configured vehicle instance={instanceId}, source='{source}', " +
                $"mass={VehicleMass:0}kg, transmission=8-speed-M-Steptronic, awd=true, " +
                $"powertrainConfigured={powertrainConfigured}, " +
                $"centerOfMass={StableCenterOfMass}, antiRoll={AntiRollBarForce:0}, " +
                $"tireFriction={TireFrictionCircleStrength:0.00}, " +
                $"suspensionTravel={FrontSuspensionTravel:0.00}/{RearSuspensionTravel:0.00}, " +
                $"navMeshObstaclesNormalized={normalizedNavMeshObstacles}, " +
                $"deformableBodyMeshes={deformableBodyMeshes}, " +
                $"damageThreshold={DamageDecelerationThreshold / 100f:0.0}mps, " +
                $"launchClutch={ClutchEngagementRpm:0}+{ClutchThrottleOffsetRpm:0}rpm/" +
                $"{ClutchEngagementRange:0}rpm, engineInertia={EngineInertia:0.000}, " +
                $"powerCurve=S58-twin-turbo, steeringCalipers=4, " +
                "wheelGeometry=authored-positive-transform-splits, " +
                $"runtimeMaterials={runtimeMaterialCount}, caliperMaterials={caliperMaterialCount}, " +
                $"materialRenderers={materialResult.RendererCount}, " +
                $"decalMasksCleared={materialResult.DecalMasksCleared}, " +
                $"opaqueFixed={materialResult.OpaqueMaterialsFixed}, " +
                $"transparentFixed={materialResult.TransparentMaterialsFixed}, " +
                $"bodyPaintNormalized={materialResult.BodyPaintMaterialsNormalized}, " +
                $"cabinGlass={materialResult.CabinGlassRenderers}/" +
                $"reenabled={materialResult.CabinGlassRenderersReenabled}, " +
                $"rimSlotsNormalized={materialResult.RimSlotsNormalized}, " +
                $"hdrpValidated={materialResult.MaterialsValidated}.");
            return true;
        }
        catch (Exception exception)
        {
            configuredVehicleIds.Remove(instanceId);
            context?.Logger.Warn(
                $"BMWM4G82: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool IsTargetVehicle(VehicleController? vehicle)
    {
        return vehicle?.vehicleInstance != null &&
               string.Equals(
                   vehicle.vehicleInstance.vehicleTypeName,
                   vehicleTypeName,
                   StringComparison.Ordinal);
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
                SetFloat(spring, "maxForce", isFront ? 19500f : 18500f);
                SetMember(component, "spring", spring);

                var damper = GetMember(component, "damper");
                SetFloat(damper, "bumpRate", SuspensionBumpRate);
                SetFloat(damper, "reboundRate", SuspensionReboundRate);
                SetMember(component, "damper", damper);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", isFront ? 0.3376f : 0.3395f);
                SetFloat(wheel, "width", isFront ? 0.275f : 0.285f);
                SetMember(component, "wheel", wheel);
                var forwardFriction = GetMember(component, "forwardFriction");
                SetFloat(forwardFriction, "grip", ForwardTireGrip);
                SetMember(component, "forwardFriction", forwardFriction);
                SetFloat(component, "frictionCircleStrength", TireFrictionCircleStrength);
                SetFloat(component, "suspensionExtensionSpeedCoeff", SuspensionExtensionSpeed);
                SetFloat(component, "damageMaxWobbleAngle", MaximumDamagedWheelWobbleAngle);
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
                colliders[0].center = new Vector3(0f, 0.40f, 0f);
                colliders[0].size = new Vector3(1.82f, 0.44f, 4.62f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 0.84f, -0.20f);
                colliders[1].size = new Vector3(1.50f, 0.62f, 2.55f);
            }
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
                $"BMWM4G82 damage vehicle={vehicle.GetInstanceID()}: " +
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
            // Stateful lamp overlays are normally disabled when the vehicle is
            // first configured. They still need their own per-instance damage
            // meshes so switching the lights on after a crash cannot reveal an
            // undeformed surface floating in front of the damaged lamp.
            if (renderer != null &&
                (renderer.enabled ||
                 filter.name.StartsWith("BMWM4G82_", StringComparison.Ordinal) ||
                 ExplicitDeformableExteriorNames.Contains(filter.name)))
                filters.Add(filter);
        }

        if (filters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"BMWM4G82 damage vehicle={vehicle.GetInstanceID()}: " +
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

        var visualDamage = vehicle.GetComponent<BMWM4G82VisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<BMWM4G82VisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);

        context?.Logger.Info(
            $"BMWM4G82 damage vehicle={vehicle.GetInstanceID()}: enabled inward deformation " +
            $"bodyMeshes={filters.Count} threshold={DamageDecelerationThreshold / 100f:0.0}mps " +
            $"filters=[{string.Join(", ", filters.ConvertAll(filter => filter.name))}]; " +
            "legacy deformation disabled.");
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        if (filter.name.StartsWith("BMWDamageBody", StringComparison.Ordinal))
            return true;
        if (filter.name.StartsWith("BMWM4G82_", StringComparison.Ordinal))
            return true;
        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null || !BMWM4G82Materials.IsBMWRenderer(renderer.transform))
            return false;
        foreach (var current in filter.GetComponentsInParent<Transform>(true))
            if (current.name.StartsWith("BMWWheel", StringComparison.Ordinal) ||
                current.name.StartsWith("BMWFixedCaliper", StringComparison.Ordinal))
                return false;
        if (ExplicitDeformableExteriorNames.Contains(filter.name))
            return true;
        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;
            var name = material.name;
            if (name.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("SeatBelt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("EngineA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                BMWM4G82Materials.IsCabinGlassMaterial(material))
                return false;
        }

        // The imported lower fascias, intake inserts and lamp assemblies span
        // several generic source materials. Treat every non-cabin BMW mesh at
        // the front/rear ends as exterior so those attached details follow the
        // surrounding impact instead of remaining rigid or floating.
        var vehicle = filter.GetComponentInParent<VehicleController>();
        if (vehicle != null)
        {
            var localCenter = vehicle.transform.InverseTransformPoint(renderer.bounds.center);
            if (Mathf.Abs(localCenter.z) >= 1.35f)
                return true;
        }

        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;
            var name = material.name;
            if (name.IndexOf("PaintTNR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Coloured", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Carbon1", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Grille", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Badge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("LightA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("red_glass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("wmit_red", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("emit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("ManufacturerPlate", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("Base_Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0)
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
            SetFloat(brakes, "maxTorque", BrakeMaxTorque);
            SetFloat(brakes, "actuationTime", BrakeActuationTime);
            SetMember(component, "brakes", brakes);
            var clutch = GetMember(powertrain, "clutch");
            SetFloat(clutch, "engagementRPM", ClutchEngagementRpm);
            SetFloat(clutch, "throttleEngagementOffsetRPM", ClutchThrottleOffsetRpm);
            SetFloat(clutch, "engagementRange", ClutchEngagementRange);
            SetFloat(clutch, "creepTorque", ClutchCreepTorque);
            SetFloat(clutch, "creepSpeedLimit", 1f);
            var engine = GetMember(powertrain, "engine");
            SetFloat(engine, "inertia", EngineInertia);
            SetFloat(engine, "maxPower", EnginePowerKw);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateM4PowerCurve());
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
            SetFloat(transmission, "shiftDuration", 0.11f);
            SetFloat(transmission, "_downshiftRPM", 4800f);
            SetFloat(transmission, "_upshiftRPM", 7000f);
            SetInt(transmission, "forwardGearCount", 8);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", M4Gears);

            var steering = GetMember(component, "steering");
            SetFloat(steering, "degreesPerSecondLimit", SteeringDegreesPerSecond);
            SetFloat(steering, "maximumSteerAngle", MaximumSteerAngle);
            SetValue(
                steering,
                "speedSensitiveSmoothingCurve",
                typeof(AnimationCurve),
                new AnimationCurve(
                    new Keyframe(0f, 0.10f),
                    new Keyframe(0.30f, 0.17f),
                    new Keyframe(1f, 0.22f)));
            SetMember(component, "steering", steering);

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

    private static void SetMember(object? target, string name, object? value)
    {
        if (target == null || value == null)
            return;
        var field = FindField(target.GetType(), name);
        if (field != null && field.FieldType.IsInstanceOfType(value))
        {
            field.SetValue(target, value);
            return;
        }
        var property = FindProperty(target.GetType(), name);
        if (property != null && property.CanWrite && property.PropertyType.IsInstanceOfType(value))
            property.SetValue(target, value, null);
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
public sealed class BMWM4G82GlassController : MonoBehaviour
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
                    !BMWM4G82Materials.IsCabinGlassMaterial(source))
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    BMWM4G82Materials.RestoreCabinGlassMaterial(runtimeMaterial);
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
                    BMWM4G82Materials.IsCabinGlassMaterial(material))
                {
                    renderer.SetPropertyBlock(null, index);
                    BMWM4G82Materials.RestoreCabinGlassMaterial(material);
                }
            }
        }
        if (string.Equals(source, "initialize", StringComparison.Ordinal))
        {
            context?.Logger.Info(
                $"BMWM4G82 glass vehicle={GetInstanceID()}: configured " +
                $"renderers={cabinGlass.Count}, runtimeMaterials={runtimeMaterials.Count}, " +
                "shader=HDRP/Lit, deferredPolling=false.");
        }
        else if (restored > 0 || propertyBlocksCleared > 0)
        {
            context?.Logger.Info(
                $"BMWM4G82 glass vehicle={GetInstanceID()}: repaired after " +
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
public sealed class BMWM4G82VisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.58f;
    private const float MaximumDentDepth = 0.13f;
    private const float DepthPerExcessMps = 0.0042f;
    private const float FrontDentLateralRadius = 1.08f;
    private const float FrontDentVerticalRadius = 0.94f;
    private const float FrontDentLongitudinalRadius = 1.22f;
    private const float RecessedFrontTrimLateralRadius = 1.30f;
    private const float RecessedFrontTrimVerticalRadius = 1.15f;
    private const float RecessedFrontTrimLongitudinalRadius = 3.00f;
    private const float RecessedFrontTrimFalloffExponent = 0.85f;
    private const float MaximumFrontDentDepth = 0.13f;
    private const float FrontDepthPerExcessMps = 0.0038f;
    private const float RearDentLateralRadius = 1.08f;
    private const float RearDentVerticalRadius = 0.94f;
    private const float RearDentLongitudinalRadius = 1.28f;
    private const float MaximumRearDentDepth = 0.14f;
    private const float RearDepthPerExcessMps = 0.0037f;
    private const float FrontEndFalloffExponent = 1.65f;
    private const float RearEndFalloffExponent = 1.55f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.75f;
    private const int MaximumDiagnosticLogs = 6;

    private static readonly HashSet<string> RecessedFrontTrimNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            // Blender isolation confirms these are the large kidney-grille
            // assembly and its forward grille detail. Their vertices sit well
            // behind the bumper contact plane and need the extended influence.
            "Object_16",
            "Object_20",
        };

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
    private int diagnosticLogs;
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
        if (previousDamage > 0.001f && currentDamage <= 0.001f)
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
            context?.Logger.Info(
                $"BMWM4G82 damage vehicle={vehicle?.GetInstanceID()}: visual body repaired.");
        }
        previousDamage = currentDamage;
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
            var dentDepth = Mathf.Clamp(excessSpeed * DepthPerExcessMps, 0.006f, MaximumDentDepth);
            var frontDentDepth = Mathf.Clamp(
                excessSpeed * FrontDepthPerExcessMps,
                0.010f,
                MaximumFrontDentDepth);
            var rearDentDepth = Mathf.Clamp(
                excessSpeed * RearDepthPerExcessMps,
                0.010f,
                MaximumRearDentDepth);
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
                var recessedFrontTrim = RecessedFrontTrimNames.Contains(filter.name);
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
                                ? recessedFrontTrim
                                    ? RecessedFrontTrimLateralRadius
                                    : FrontDentLateralRadius
                                : RearDentLateralRadius;
                            var verticalRadius = isFrontContact
                                ? recessedFrontTrim
                                    ? RecessedFrontTrimVerticalRadius
                                    : FrontDentVerticalRadius
                                : RearDentVerticalRadius;
                            var longitudinalRadius = isFrontContact
                                ? recessedFrontTrim
                                    ? RecessedFrontTrimLongitudinalRadius
                                    : FrontDentLongitudinalRadius
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
                            selectedFrontImpact && recessedFrontTrim
                                ? RecessedFrontTrimFalloffExponent
                                : selectedFrontImpact
                                ? FrontEndFalloffExponent
                                : RearEndFalloffExponent)
                        : strongestInfluence * strongestInfluence;
                    worldVertex += inwardDirection * (selectedDepth * falloff);
                    var localVertex = filter.transform.InverseTransformPoint(worldVertex);
                    if (originalVertices.TryGetValue(filter, out var baseline) &&
                        vertexIndex < baseline.Length)
                    {
                        var cumulativeLimit = selectedEndImpact
                            ? selectedFrontImpact ? MaximumFrontDentDepth : MaximumRearDentDepth
                            : MaximumDentDepth;
                        localVertex = baseline[vertexIndex] + Vector3.ClampMagnitude(
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
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                changedMeshes++;
                changedMeshNames.Add(filter.name);
            }

            if (diagnosticLogs++ < MaximumDiagnosticLogs)
            {
                context?.Logger.Info(
                    $"BMWM4G82 damage vehicle={vehicle?.GetInstanceID()}: inward dent " +
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
                $"BMWM4G82 damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void OnDestroy()
    {
        foreach (var mesh in runtimeMeshes)
            if (mesh != null) Destroy(mesh);
        runtimeMeshes.Clear();
    }
}
