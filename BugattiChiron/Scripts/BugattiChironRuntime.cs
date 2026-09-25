#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using BusinessLayoutSets;
using Helpers;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;

public sealed class BugattiChironRuntime : MonoBehaviour
{
    private const string VehicleRepainterColorRestoredEvent = "vehicle-repainter:color-restored";
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1995f;
    // NWH's simplified drivetrain has lower losses than the real car. Keeping the
    // catalog specification at 1,103 kW while using this calibrated simulation
    // value brings the measured 0-300 km/h time closer to the Chiron's 13.1 s.
    private const float SimulationEnginePowerKw = 850f;
    // Calibrated against Bugatti's published 100-0 and 200-0 km/h distances.
    private const float PhysicalBrakeTorque = 3000f;
    private const float EngineIdleRpm = 900f;
    private const float EngineLimitRpm = 6700f;
    private const float SpeedLimitKph = 420f;
    private const float FinalDriveRatio = 3.2f;
    // Match the aggressive 60%-of-redline kickdown used by the game's
    // performance-car transmission while retaining the Chiron's 6,700 RPM limit.
    private const float DownshiftRpm = 4000f;
    private const float EngineInertia = 0.12f;
    private const float EngineStartDuration = 0.5f;
    private const float ClutchEngagementRpm = 1200f;
    private const float ClutchThrottleOffsetRpm = 500f;
    private const float ClutchEngagementRange = 500f;
    private const float ClutchCreepTorque = 0f;
    private const float VehicleLinearDrag = 0f;
    private const float ForcedInductionPowerMultiplier = 1f;
    private const float DamageDecelerationThreshold = 500f;
    private const float DamageIntensity = 0.6f;
    private const float DeformationRadius = 0.48f;
    private const float DeformationStrength = 0.32f;
    private const float DriverExitLocalX = -1.5f;
    private const float PassengerExitLocalX = 1.5f;
    private const float ExitLocalY = 0.1f;
    private const float ExitLocalZ = 0.1f;

    private static readonly float[] ChironGears =
    {
        -2.96f,
        0f,
        4.10f,
        2.60f,
        1.80f,
        1.35f,
        1.00f,
        0.78f,
        0.62f,
    };

    private static AnimationCurve CreateChironPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.12f, 0.065f),
            new Keyframe(0.20f, 0.145f),
            new Keyframe(0.30f, 0.28f),
            new Keyframe(0.55f, 0.515f),
            new Keyframe(0.78f, 0.73f),
            new Keyframe(0.90f, 0.85f),
            new Keyframe(1f, 0.99f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private BugattiChironTrafficRarity? trafficRarity;
    private ModContext? context;
    private bool dealerReady;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;
    private int cachedPlayerVehicleCount = -1;
    private string vehicleTypeName = string.Empty;
    private GameObject? playerVehiclePrefab;
    private VehicleController? wheelDiagnosticVehicle;
    private Transform?[]? wheelDiagnosticControllers;
    private Transform?[]? wheelDiagnosticVisuals;
    private float nextWheelDiagnosticTime;

    private static readonly string[] WheelDiagnosticCorners =
    {
        "FrontLeft", "FrontRight", "RearLeft", "RearRight",
    };

    public static BugattiChironRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<BugattiChironRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(BugattiChironRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<BugattiChironRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        BugattiChironPrivateDriverSupport.SetContext(context);
        runtime.trafficRarity = runtime.GetComponent<BugattiChironTrafficRarity>() ??
                                runtime.gameObject.AddComponent<BugattiChironTrafficRarity>();
        runtime.trafficRarity.Initialize(context);
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
        dealerReady = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        cachedPlayerVehicleCount = -1;
        BugattiChironPrivateDriverSupport.RemoveVehicle(vehicleTypeName);
        trafficRarity = null;
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
        var vehicles = VehicleHelper.AllPlayerVehicles;
        var currentCount = vehicles?.Count ?? 0;
        if (currentCount != cachedPlayerVehicleCount)
            ConfigureExistingVehicles(out _);
    }

    private void LateUpdate()
    {
        if (!BugattiChironDiagnostics.DebugEnabled ||
            !BugattiChironDiagnostics.WheelDebugEnabled)
            return;

        var selected = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selected))
        {
            wheelDiagnosticVehicle = null;
            return;
        }

        if (wheelDiagnosticVehicle != selected)
        {
            wheelDiagnosticVehicle = selected;
            wheelDiagnosticControllers = new Transform?[WheelDiagnosticCorners.Length];
            wheelDiagnosticVisuals = new Transform?[WheelDiagnosticCorners.Length];
            var transforms = selected!.GetComponentsInChildren<Transform>(true);
            for (var corner = 0; corner < WheelDiagnosticCorners.Length; corner++)
            {
                var controllerName = WheelDiagnosticCorners[corner] + "_WheelController";
                var visualName = "BugattiWheel" + WheelDiagnosticCorners[corner];
                foreach (var candidate in transforms)
                {
                    if (candidate.name == controllerName)
                        wheelDiagnosticControllers[corner] = candidate;
                    else if (candidate.name == visualName)
                        wheelDiagnosticVisuals[corner] = candidate;
                }
            }
            nextWheelDiagnosticTime = 0f;
        }

        if (Time.unscaledTime < nextWheelDiagnosticTime)
            return;
        nextWheelDiagnosticTime = Time.unscaledTime + 3f;

        var chassis = selected!.transform;
        var rigidbody = selected.GetComponent<Rigidbody>() ?? selected.GetComponentInParent<Rigidbody>();
        var speed = rigidbody != null ? rigidbody.velocity.magnitude * 3.6f : 0f;
        for (var corner = 0; corner < WheelDiagnosticCorners.Length; corner++)
        {
            var controller = wheelDiagnosticControllers![corner];
            var visual = wheelDiagnosticVisuals![corner];
            if (controller == null || visual == null)
            {
                context?.Logger.Info(
                    $"BugattiChiron wheel diagnostic instance={selected.GetInstanceID()} " +
                    $"corner={WheelDiagnosticCorners[corner]} controllerFound={controller != null} " +
                    $"visualFound={visual != null}");
                continue;
            }

            var geometry = visual.Find("Geometry");
            var axle = chassis.InverseTransformDirection(visual.right);
            var controllerOffset = chassis.InverseTransformPoint(visual.position) -
                                   chassis.InverseTransformPoint(controller.position);
            context?.Logger.Info(
                $"BugattiChiron wheel diagnostic instance={selected.GetInstanceID()} " +
                $"corner={WheelDiagnosticCorners[corner]} speedKph={speed:0.0} " +
                $"controllerLocalPos={controller.localPosition:F3} " +
                $"controllerLocalEuler={controller.localEulerAngles:F1} " +
                $"visualParent={visual.parent?.name ?? "<none>"} " +
                $"visualLocalPos={visual.localPosition:F3} " +
                $"visualLocalEuler={visual.localEulerAngles:F1} " +
                $"visualAxleInChassis={axle:F3} " +
                $"visualOffsetFromController={controllerOffset:F3} " +
                $"geometryLocalEuler={(geometry != null ? geometry.localEulerAngles.ToString("F1") : "<missing>")}");
        }
    }

    private void SubscribeEvents()
    {
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GameEvent.onGameEventTriggered += HandleGameEvent;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onEnterBuilding += HandleBuildingEntered;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onGameUnloaded += HandleGameUnloaded;
    }

    private void UnsubscribeEvents()
    {
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
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
        if (BugattiChironLoadRecovery.CompleteInterruptedLoad(context))
            StartCoroutine(ReportLoadedInputState());
        privateDriverRegistrationAllowed = true;
        trafficRarity?.ScheduleRebalance();
        ScheduleInitialization("game-loaded-late");
    }

    private IEnumerator ReportLoadedInputState()
    {
        // The native loading screen fades out for 0.8 seconds after this event.
        yield return new WaitForSecondsRealtime(2f);
        BugattiChironLoadRecovery.ReportInputState(context);
    }

    private void HandleGameUnloaded()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
        configuredVehicleIds.Clear();
        dealerReady = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        cachedPlayerVehicleCount = -1;
    }

    private void HandleGameEvent(string eventName)
    {
        if (string.Equals(eventName, VehicleRepainterColorRestoredEvent, StringComparison.Ordinal))
        {
            RefreshExistingVehiclePaint();
            return;
        }

        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        TryConfigureVehicle(selectedVehicle);
        selectedVehicle!
            .GetComponent<BugattiChironPaintController>()
            ?.RefreshCurrentColor();
    }

    private bool IsTargetVehicle(VehicleController? vehicle) =>
        vehicle?.vehicleInstance != null &&
        string.Equals(
            vehicle.vehicleInstance.vehicleTypeName,
            vehicleTypeName,
            StringComparison.Ordinal);

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        var isTarget = IsTargetVehicle(vehicle);
        TryConfigureVehicle(vehicle);
        vehicle?.GetComponent<BugattiChironPaintController>()?.RefreshCurrentColor();
        vehicle?.GetComponent<BugattiChironGlassController>()?.RestoreAfterVehicleEntered();
        if (isTarget)
        {
            var engine = vehicle!
                .GetComponent<NWH.VehiclePhysics2.VehicleController>()
                ?.powertrain?.engine;
            if (engine != null && !engine.IsRunning && engine.canRun)
            {
                SetBool(engine, "flyingStartEnabled", true);
                engine.StartEngine();
            }
        }
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!dealerReady &&
            !BusinessLayoutSetHelper.loadingLayouts &&
            BugattiChironLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
        {
            EnsureDealerStock("dealer-entered");
        }
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (!isOpen)
        {
            RefreshExistingVehiclePaint();
            return;
        }

        if (!dealerReady && !BusinessLayoutSetHelper.loadingLayouts)
            EnsureDealerStock("full-menu");
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
                EnsureDealerStock(source);
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
                $"BugattiChiron: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
        if (privateDriverRegistrationAllowed && !privateDriverReady)
        {
            context?.Logger.Warn(
                $"BugattiChiron: private-driver support not ready source='{source}'.");
        }
        if (!privateDriverPoolReady)
        {
            context?.Logger.Warn(
                $"BugattiChiron: private-driver traffic pool not ready source='{source}'.");
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
            dealerReady = BugattiChironLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            return dealerReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"BugattiChiron: dealer stock update failed source='{source}': " +
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
                privateDriverReady = BugattiChironPrivateDriverSupport.EnsureVehicleAvailable(
                    vehicleTypeName);
            }
            return privateDriverReady && privateDriverPoolReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"BugattiChiron: private-driver registration failed source='{source}': " +
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
            return BugattiChironPrivateDriverSupport.PrepareTrafficPool(playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                context?.Logger.Warn(
                    $"BugattiChiron: private-driver pool preparation failed source='{source}': " +
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

    private void RefreshExistingVehiclePaint()
    {
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

            var paintController = vehicle.GetComponent<BugattiChironPaintController>();
            if (paintController == null)
                continue;

            paintController.RefreshCurrentColor();
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
            return;

        try
        {
            var rigidbody = vehicle.GetComponent<Rigidbody>() ?? vehicle.GetComponentInParent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.mass = VehicleMass;
                rigidbody.centerOfMass = new Vector3(0f, 0.26f, 0f);
                rigidbody.drag = VehicleLinearDrag;
                rigidbody.angularDrag = 1.35f;

                var aerodynamics = vehicle.GetComponent<BugattiChironAerodynamics>();
                if (aerodynamics == null)
                    aerodynamics = vehicle.gameObject.AddComponent<BugattiChironAerodynamics>();
                aerodynamics.Initialize(rigidbody);

                var highwaySeamGuard =
                    vehicle.GetComponent<BugattiChironHighwaySeamGuard>();
                if (highwaySeamGuard == null)
                {
                    highwaySeamGuard = vehicle.gameObject
                        .AddComponent<BugattiChironHighwaySeamGuard>();
                }
                highwaySeamGuard.Initialize(rigidbody);
            }

            ConfigureWheelControllers(vehicle.gameObject);
            ConfigureExitMarkers(vehicle.gameObject);
            ConfigureBodyColliders(vehicle.gameObject);
            ConfigureNavMeshObstacles(vehicle.gameObject);
            ConfigurePowertrain(vehicle.gameObject);
            BugattiChironMaterials.FixSolidMaterials(vehicle.gameObject);
            var glassController = vehicle.GetComponent<BugattiChironGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<BugattiChironGlassController>();
            glassController.Initialize(context);
            ConfigureVisualDamage(vehicle);
            var lightingController = vehicle.GetComponent<BugattiChironLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<BugattiChironLightingController>();
            lightingController.Initialize(vehicle, context);
            var driverController = vehicle.GetComponent<BugattiChironDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<BugattiChironDriverController>();
            driverController.Initialize(vehicle, context);
            var paintController = vehicle.GetComponent<BugattiChironPaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<BugattiChironPaintController>();
            paintController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<BugattiChironAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<BugattiChironAudioController>();
            audioController.Initialize(vehicle, context);
            try
            {
                var caliperController = vehicle.GetComponent<BugattiChironCaliperController>();
                if (caliperController == null)
                    caliperController = vehicle.gameObject.AddComponent<BugattiChironCaliperController>();
                caliperController.Initialize(vehicle, context);
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"BugattiChiron: steering caliper configuration failed instance={instanceId}; " +
                    $"remaining vehicle systems stay active: {exception.GetType().Name}: " +
                    exception.Message);
            }
            var bridgeSeamGuard = vehicle.GetComponent<BugattiChironBridgeSeamGuard>();
            if (bridgeSeamGuard == null)
                bridgeSeamGuard = vehicle.gameObject.AddComponent<BugattiChironBridgeSeamGuard>();
            bridgeSeamGuard.Initialize(vehicle, context);
            var pinRecovery = vehicle.GetComponent<BugattiChironAiVehiclePinRecovery>();
            if (pinRecovery == null)
                pinRecovery = vehicle.gameObject.AddComponent<BugattiChironAiVehiclePinRecovery>();
            pinRecovery.Initialize(vehicle, context);
        }
        catch (Exception exception)
        {
            configuredVehicleIds.Remove(instanceId);
            context?.Logger.Warn(
                $"BugattiChiron: vehicle configuration failed instance={instanceId}: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        var positions = new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-0.7945f, 0.51f, 1.3155f) },
            { "FrontRight_WheelController", new Vector3(0.7945f, 0.51f, 1.3155f) },
            { "RearLeft_WheelController", new Vector3(-0.7505f, 0.51f, -1.3955f) },
            { "RearRight_WheelController", new Vector3(0.7505f, 0.51f, -1.3955f) },
        };

        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!positions.TryGetValue(transform.name, out var position))
                continue;

            transform.localPosition = position;
            var isFront = transform.name.StartsWith("Front", StringComparison.Ordinal);
            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                var spring = GetMember(component, "spring");
                SetFloat(spring, "maxLength", 0.18f);
                SetFloat(spring, "maxForce", 22000f);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", isFront ? 0.34f : 0.355f);
                SetFloat(wheel, "width", isFront ? 0.285f : 0.355f);
            }
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
                colliders[0].center = new Vector3(0f, 0.43f, 0f);
                colliders[0].size = new Vector3(1.98f, 0.48f, 4.45f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 0.86f, -0.08f);
                colliders[1].size = new Vector3(1.70f, 0.72f, 2.75f);
            }
        }
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            float localX;
            if (string.Equals(transform.name, "Driverside", StringComparison.Ordinal))
                localX = DriverExitLocalX;
            else if (string.Equals(transform.name, "Passengerside", StringComparison.Ordinal))
                localX = PassengerExitLocalX;
            else
                continue;

            transform.localPosition = new Vector3(localX, ExitLocalY, ExitLocalZ);
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
            SetFloat(brakes, "maxTorque", PhysicalBrakeTorque);
            var clutch = GetMember(powertrain, "clutch");
            SetFloat(clutch, "engagementRPM", ClutchEngagementRpm);
            SetFloat(clutch, "throttleEngagementOffsetRPM", ClutchThrottleOffsetRpm);
            SetFloat(clutch, "engagementRange", ClutchEngagementRange);
            SetFloat(clutch, "creepTorque", ClutchCreepTorque);
            SetFloat(clutch, "creepSpeedLimit", 1f);
            var engine = GetMember(powertrain, "engine");
            SetFloat(engine, "inertia", EngineInertia);
            SetFloat(engine, "maxPower", SimulationEnginePowerKw);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateChironPowerCurve());
            SetFloat(engine, "idleRPM", EngineIdleRpm);
            SetFloat(engine, "revLimiterRPM", EngineLimitRpm);
            SetFloat(engine, "startDuration", EngineStartDuration);
            SetBool(engine, "stallingEnabled", false);
            SetBool(engine, "flyingStartEnabled", true);
            var forcedInduction = GetMember(engine, "forcedInduction");
            SetBool(forcedInduction, "useForcedInduction", true);
            SetFloat(forcedInduction, "powerGainMultiplier", ForcedInductionPowerMultiplier);
            SetFloat(forcedInduction, "spoolUpTime", 0.08f);

            var transmission = GetMember(powertrain, "transmission");
            SetFloat(transmission, "finalGearRatio", FinalDriveRatio);
            SetFloat(transmission, "shiftDuration", 0.065f);
            SetFloat(transmission, "_downshiftRPM", DownshiftRpm);
            SetFloat(transmission, "_upshiftRPM", 6100f);
            SetInt(transmission, "forwardGearCount", 7);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", ChironGears);

            foreach (var other in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (other != null &&
                    string.Equals(other.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
                {
                    var module = GetMember(other, "module");
                    SetFloat(module, "speedLimit", SpeedLimitKph);
                }
            }

            return GetInt(transmission, "forwardGearCount") == 7;
        }

        return false;
    }

    private int ConfigureVisualDamage(VehicleController vehicle)
    {
        var legacyDeformation = vehicle.GetComponentInChildren<VehicleDeformationController>(true);
        if (legacyDeformation != null)
        {
            legacyDeformation.enabled = false;
            ClearCollection(legacyDeformation, "_deformationQueue");
        }

        var damageHandler =
            vehicle.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true);
        if (damageHandler == null)
        {
            context?.Logger.Warn(
                $"BugattiChiron damage vehicle={vehicle.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        var deformableFilters = new List<MeshFilter>();
        foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null ||
                !BugattiChironMaterials.IsBugattiRenderer(filter.transform))
            {
                continue;
            }

            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled || !IsDeformableExterior(filter, renderer))
                continue;
            deformableFilters.Add(filter);
        }

        if (deformableFilters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"BugattiChiron damage vehicle={vehicle.GetInstanceID()}: " +
                "could not find visible exterior meshes; " +
                "visual damage remains disabled.");
            return 0;
        }

        ClearCollection(damageHandler, "_collisionEvents");

        damageHandler.collisionTimeout = 0.8f;
        damageHandler.damageIntensity = DamageIntensity;
        damageHandler.decelerationThreshold = DamageDecelerationThreshold;
        damageHandler.deformationRadius = DeformationRadius;
        damageHandler.deformationRandomness = 0.01f;
        damageHandler.deformationStrength = DeformationStrength;
        damageHandler.deformationVerticesPerFrame = 8000;
        // The bundled NWH mesh step adds the contact normal. On this model the
        // reported normal points out of the body, inflating panels instead of denting them.
        damageHandler.meshDeform = false;

        var visualDamage = vehicle.GetComponent<BugattiChironVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<BugattiChironVisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            deformableFilters,
            DamageDecelerationThreshold / 100f);

        return deformableFilters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter, Renderer renderer)
    {
        var filterName = filter.name;
        var bodyPanel =
            filterName.StartsWith("Body_", StringComparison.OrdinalIgnoreCase) ||
            filterName.StartsWith("Door-left_", StringComparison.OrdinalIgnoreCase) ||
            filterName.StartsWith("Door-right_", StringComparison.OrdinalIgnoreCase);
        var exteriorPlastic =
            string.Equals(filterName, "Plastic-parts_Plastic_0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(filterName, "Plastic-parts_Carbon_0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(filterName, "Plastic-parts_Headlight-1_0", StringComparison.OrdinalIgnoreCase);
        var frontDetails =
            filterName.StartsWith("Headlight_", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(filterName, "Engine_Carbon_0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                filterName,
                "Plastic-parts_Vents-texture_0",
                StringComparison.OrdinalIgnoreCase);
        var rearExhaust = string.Equals(
            filterName,
            "Plastic-parts_exhaust_0",
            StringComparison.OrdinalIgnoreCase);
        var frontGrille =
            filterName.StartsWith("B:Grille", StringComparison.OrdinalIgnoreCase) ||
            filterName.StartsWith("B:Kit2_Grille", StringComparison.OrdinalIgnoreCase);
        // The FBX groups the visible horseshoe grille, surround, and badge under
        // Engine even though they are exterior nose pieces. Keep the actual engine
        // and its carbon geometry rigid by admitting only these named submeshes.
        var frontCenterAssembly =
            string.Equals(filterName, "Engine_Plastic_0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(filterName, "Engine_Silver_0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(filterName, "Engine_Logo_0", StringComparison.OrdinalIgnoreCase);
        if (!bodyPanel && !frontCenterAssembly && !exteriorPlastic &&
            !frontDetails && !frontGrille && !rearExhaust)
        {
            return false;
        }

        // Small lamp and exhaust pieces otherwise remain rigid over a dented
        // bumper and visually float at their original positions.
        if (frontDetails ||
            rearExhaust ||
            string.Equals(filterName, "Plastic-parts_Headlight-1_0", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null)
                continue;
            var name = material.name;
            if (name.IndexOf("BugattiOpaque_04_Body", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("BugattiOpaque_06_Darker_Parts", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("BugattiOpaque_07_Carbon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("BugattiOpaque_05_Silver", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("BugattiOpaque_09_Plastic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (frontCenterAssembly &&
                 name.IndexOf("BugattiOpaque_10_Logo", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }
        }
        return false;
    }

    private static void ClearCollection(object target, string fieldName)
    {
        var collection = FindField(target.GetType(), fieldName)?.GetValue(target);
        collection?.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(collection, null);
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

internal sealed class BugattiChironTrafficRarity : MonoBehaviour
{
    private const int TargetTrafficSpawnsPerBugatti = 200;
    private static readonly BindingFlags PrivateInstance =
        BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo? TrafficVehiclesField =
        typeof(TrafficManager).GetField("trafficVehicles", PrivateInstance);
    private static readonly FieldInfo? IdleVehiclesField =
        typeof(TrafficVehicles).GetField("idleVehicles", PrivateInstance);

    private readonly List<VehicleComponent> ownedIdleVehicles = new List<VehicleComponent>(2);
    private readonly Dictionary<string, int> trafficSpawnsByPrefab =
        new Dictionary<string, int>(StringComparer.Ordinal);
    private ModContext? context;
    private Coroutine? rebalanceCoroutine;
    private bool missingFieldReported;
    private bool firstBalanceLogged;
    private int bugattiSpawns;
    private int totalTrafficSpawns;

    internal void Initialize(ModContext modContext)
    {
        context = modContext;
        ScheduleRebalance();
    }

    private void OnEnable()
    {
        DensityEvents.onVehicleAdded += HandleVehicleAdded;
        AIEvents.onVehicleChangedState += HandleVehicleChangedState;
        ScheduleRebalance();
    }

    private void OnDisable()
    {
        LogTrafficSummary("disabled");
        DensityEvents.onVehicleAdded -= HandleVehicleAdded;
        AIEvents.onVehicleChangedState -= HandleVehicleChangedState;
        if (rebalanceCoroutine != null)
            StopCoroutine(rebalanceCoroutine);
        rebalanceCoroutine = null;
    }

    internal void ScheduleRebalance()
    {
        if (rebalanceCoroutine == null && isActiveAndEnabled)
            rebalanceCoroutine = StartCoroutine(RebalanceAfterTrafficUpdate());
    }

    private IEnumerator RebalanceAfterTrafficUpdate()
    {
        // VehicleChangedState is emitted before a returned car enters the idle list.
        yield return null;
        for (var frame = 0; frame < 120 && !TrafficManager.IsInitialized; frame++)
            yield return null;

        if (TrafficManager.IsInitialized)
            RebalanceIdleVehicles();
        rebalanceCoroutine = null;
    }

    private void HandleVehicleAdded(int vehicleIndex)
    {
        if (TryGetIdleVehicles(out _, out var trafficVehicles))
        {
            var all = trafficVehicles.GetVehicleList();
            if (vehicleIndex >= 0 && vehicleIndex < all.Count)
            {
                totalTrafficSpawns++;
                var prefab = all[vehicleIndex]?.prefab;
                if (prefab != null)
                {
                    var prefabName = prefab.name;
                    trafficSpawnsByPrefab.TryGetValue(prefabName, out var count);
                    trafficSpawnsByPrefab[prefabName] = count + 1;
                }
                if (prefab != null &&
                    prefab == BugattiChironPrivateDriverSupport.TrafficPrefab)
                    bugattiSpawns++;
                if (BugattiChironDiagnostics.DebugEnabled &&
                    BugattiChironDiagnostics.TrafficRarityDebugEnabled && prefab != null &&
                    prefab == BugattiChironPrivateDriverSupport.TrafficPrefab)
                    context?.Logger.Info(
                        $"BugattiChiron traffic rarity: spawned='{prefab.name}' " +
                        $"bugatti={bugattiSpawns} total={totalTrafficSpawns} " +
                        $"target=1/{TargetTrafficSpawnsPerBugatti}.");
                if (totalTrafficSpawns % 50 == 0)
                    LogTrafficSummary("periodic");
            }
        }
        // Initial traffic is spawned inside TrafficManager.Initialize, before
        // IsInitialized becomes true. Reorder after each add in that first wave.
        RebalanceIdleVehicles();
        ScheduleRebalance();
    }

    private void LogTrafficSummary(string reason)
    {
        if (totalTrafficSpawns == 0 || !BugattiChironDiagnostics.DebugEnabled ||
            !BugattiChironDiagnostics.TrafficRarityDebugEnabled)
            return;
        var percent = 100d * bugattiSpawns / totalTrafficSpawns;
        context?.Logger.Info(
            $"BugattiChiron traffic rarity: summary reason={reason} " +
            $"bugatti={bugattiSpawns} total={totalTrafficSpawns} share={percent:0.0}% " +
            $"target={100d / TargetTrafficSpawnsPerBugatti:0.0}%.");

        // Read the current pool so an installed mod with zero observed spawns
        // remains visible in the sample. All of our vehicle mods use this
        // prefab suffix; unrelated traffic stays in the comparison count.
        var moddedPrefabs = new HashSet<string>(StringComparer.Ordinal);
        var trafficCars = TrafficComponent.Instance?.vehiclePool?.trafficCars;
        if (trafficCars != null)
            foreach (var car in trafficCars)
            {
                var name = car?.vehiclePrefab?.name;
                if (name != null && name.EndsWith("PrivateDriver", StringComparison.Ordinal))
                    moddedPrefabs.Add(name);
            }
        foreach (var name in trafficSpawnsByPrefab.Keys)
            if (name.EndsWith("PrivateDriver", StringComparison.Ordinal))
                moddedPrefabs.Add(name);

        var names = new List<string>(moddedPrefabs);
        names.Sort(StringComparer.Ordinal);
        var counts = new List<string>(names.Count);
        var moddedSpawns = 0;
        foreach (var name in names)
        {
            trafficSpawnsByPrefab.TryGetValue(name, out var count);
            moddedSpawns += count;
            counts.Add($"{name.Substring(0, name.Length - "PrivateDriver".Length)}={count}");
        }
        context?.Logger.Info(
            $"BugattiChiron traffic mix: reason={reason} sampleSpawns={totalTrafficSpawns} " +
            $"modded={moddedSpawns} moddedShare={100d * moddedSpawns / totalTrafficSpawns:0.0}% " +
            $"stockOrOther={totalTrafficSpawns - moddedSpawns} " +
            $"moddedByType=[{string.Join(", ", counts)}].");
    }

    private void HandleVehicleChangedState(
        int vehicleIndex, Collider vehicleCollider, SpecialDriveActionTypes action)
    {
        // DensityManager emits this state immediately before returning a car.
        if ((int)action == 10000)
            ScheduleRebalance();
    }

    private bool TryGetIdleVehicles(
        out List<VehicleComponent>? idle, out TrafficVehicles trafficVehicles)
    {
        idle = null;
        trafficVehicles = null!;
        var manager = TrafficManager.Instance;
        if (manager == null)
            return false;

        if (TrafficVehiclesField?.GetValue(manager) is TrafficVehicles vehicles &&
            IdleVehiclesField?.GetValue(vehicles) is List<VehicleComponent> list)
        {
            trafficVehicles = vehicles;
            idle = list;
            return true;
        }

        if (!missingFieldReported)
        {
            missingFieldReported = true;
            context?.Logger.Warn(
                "BugattiChiron traffic rarity: traffic idle list is unavailable; " +
                "NPC frequency remains unchanged.");
        }
        return false;
    }

    private void RebalanceIdleVehicles()
    {
        if (!TryGetIdleVehicles(out var idle, out _) || idle == null)
            return;
        var bugattiPrefab = BugattiChironPrivateDriverSupport.TrafficPrefab;
        if (bugattiPrefab == null)
            return;

        ownedIdleVehicles.Clear();
        for (var index = idle.Count - 1; index >= 0; index--)
        {
            var vehicle = idle[index];
            if (vehicle == null || vehicle.prefab != bugattiPrefab)
                continue;
            ownedIdleVehicles.Insert(0, vehicle);
            idle.RemoveAt(index);
        }
        if (ownedIdleVehicles.Count == 0)
            return;

        // The game takes the first idle car in a randomly selected vehicle
        // group. Prioritize one Bugatti when its share of all traffic spawns
        // falls below the fixed target; otherwise let other idle cars go first.
        // No other mod or specific vehicle type is required for this decision.
        var due = (long)bugattiSpawns * TargetTrafficSpawnsPerBugatti <= totalTrafficSpawns;
        if (due)
        {
            idle.Insert(0, ownedIdleVehicles[0]);
            for (var index = 1; index < ownedIdleVehicles.Count; index++)
                idle.Add(ownedIdleVehicles[index]);
        }
        else
        {
            idle.AddRange(ownedIdleVehicles);
        }

        if (!firstBalanceLogged && BugattiChironDiagnostics.DebugEnabled &&
            BugattiChironDiagnostics.TrafficRarityDebugEnabled)
        {
            firstBalanceLogged = true;
            context?.Logger.Info(
                $"BugattiChiron traffic rarity: moved {ownedIdleVehicles.Count} idle car(s) " +
                $"due={due} bugattiSpawns={bugattiSpawns} " +
                $"totalSpawns={totalTrafficSpawns} idleCount={idle.Count}.");
        }
    }
}

[AddComponentMenu("")]
internal sealed class BugattiChironGlassController : MonoBehaviour
{
    private readonly List<Renderer> cabinGlass = new List<Renderer>();
    private readonly Dictionary<Material, Material> runtimeMaterials =
        new Dictionary<Material, Material>();
    private Coroutine? restoreCoroutine;
    private bool initialized;

    internal void Initialize(ModContext? _)
    {
        if (initialized)
        {
            EnsureVisible();
            return;
        }

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var containsCabinGlass = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null || !BugattiChironMaterials.IsCabinGlassMaterial(source))
                    continue;

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    BugattiChironMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
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
        EnsureVisible();
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
        yield return null;
        yield return new WaitForEndOfFrame();
        EnsureVisible();
        restoreCoroutine = null;
    }

    private void EnsureVisible()
    {
        foreach (var renderer in cabinGlass)
        {
            if (renderer == null)
                continue;
            renderer.enabled = true;
            renderer.forceRenderingOff = false;
            if (renderer.HasPropertyBlock())
                renderer.SetPropertyBlock(null);

            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null || !BugattiChironMaterials.IsCabinGlassMaterial(material))
                    continue;
                renderer.SetPropertyBlock(null, index);
                BugattiChironMaterials.RestoreCabinGlassMaterial(material);
            }
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
public sealed class BugattiChironVisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.55f;
    private const float MaximumDentDepth = 0.3f;
    private const float DepthPerExcessMps = 0.009f;
    private const float EndDentLateralRadius = 0.9f;
    private const float EndDentVerticalRadius = 0.9f;
    private const float EndDentLongitudinalRadius = 1.15f;
    private const float MaximumFrontEndDentDepth = 0.30f;
    private const float MaximumRearEndDentDepth = 0.45f;
    private const float MaximumSideCumulativeDentDepth = 0.20f;
    private const float EndDepthPerExcessMps = 0.012f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.5f;

    private readonly List<MeshFilter> deformableFilters = new();
    private readonly Dictionary<MeshFilter, Mesh> originalMeshes = new();
    private readonly Dictionary<MeshFilter, Vector3[]> originalVertices = new();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private ModContext? context;
    private Rigidbody? body;
    private float impactThresholdMps;
    private float nextCollisionTime;
    private float previousDamage;
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
        originalMeshes.Clear();
        originalVertices.Clear();
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;
            deformableFilters.Add(filter);
            originalMeshes[filter] = filter.sharedMesh;
            originalVertices[filter] = filter.sharedMesh.vertices;
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
            foreach (var pair in originalMeshes)
            {
                if (pair.Key != null && pair.Value != null)
                    pair.Key.sharedMesh = pair.Value;
            }
        }
        previousDamage = currentDamage;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!initialized || collision == null || Time.unscaledTime < nextCollisionTime ||
            collision.relativeVelocity.magnitude < impactThresholdMps ||
            !NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision))
        {
            return;
        }

        try
        {
            nextCollisionTime = Time.unscaledTime + CollisionCooldown;
            var contacts = collision.contacts;
            if (contacts.Length == 0)
                return;

            var excessSpeed = collision.relativeVelocity.magnitude - impactThresholdMps;
            var dentDepth = Mathf.Clamp(excessSpeed * DepthPerExcessMps, 0.02f, MaximumDentDepth);
            var center = body != null ? body.worldCenterOfMass : transform.position;

            foreach (var filter in deformableFilters)
            {
                if (filter == null || filter.sharedMesh == null)
                    continue;
                var mesh = filter.mesh;
                var vertices = mesh.vertices;
                originalVertices.TryGetValue(filter, out var sourceVertices);
                var meshChanged = false;
                for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    var worldVertex = filter.transform.TransformPoint(vertices[vertexIndex]);
                    var strongestInfluence = 0f;
                    var inwardDirection = Vector3.zero;
                    var selectedDepth = dentDepth;
                    var selectedEndImpact = false;
                    var selectedFrontEndImpact = false;
                    foreach (var contact in contacts)
                    {
                        var localContact = transform.InverseTransformPoint(contact.point);
                        var isEndContact =
                            Mathf.Abs(localContact.z) >= EndContactMinimumLongitudinalOffset &&
                            Mathf.Abs(localContact.z) > Mathf.Abs(localContact.x);
                        float influence;
                        Vector3 candidateDirection;
                        if (isEndContact)
                        {
                            var localDelta = transform.InverseTransformVector(worldVertex - contact.point);
                            var normalizedDistance = Mathf.Sqrt(
                                localDelta.x * localDelta.x /
                                (EndDentLateralRadius * EndDentLateralRadius) +
                                localDelta.y * localDelta.y /
                                (EndDentVerticalRadius * EndDentVerticalRadius) +
                                localDelta.z * localDelta.z /
                                (EndDentLongitudinalRadius * EndDentLongitudinalRadius));
                            influence = 1f - normalizedDistance;
                            candidateDirection = localContact.z >= 0f
                                ? -transform.forward
                                : transform.forward;
                        }
                        else
                        {
                            var distance = Vector3.Distance(worldVertex, contact.point);
                            influence = 1f - distance / DentRadius;
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
                        selectedFrontEndImpact = isEndContact && localContact.z >= 0f;
                        selectedDepth = isEndContact
                            ? Mathf.Clamp(
                                excessSpeed * EndDepthPerExcessMps,
                                0.035f,
                                selectedFrontEndImpact
                                    ? MaximumFrontEndDentDepth
                                    : MaximumRearEndDentDepth)
                            : dentDepth;
                        selectedEndImpact = isEndContact;
                    }

                    if (strongestInfluence <= 0f || inwardDirection.sqrMagnitude < 0.5f)
                        continue;
                    var falloff = selectedEndImpact
                        ? Mathf.Pow(strongestInfluence, 1.35f)
                        : strongestInfluence * strongestInfluence;
                    worldVertex += inwardDirection * (selectedDepth * falloff);
                    var cumulativeCap = selectedEndImpact
                        ? (selectedFrontEndImpact
                            ? MaximumFrontEndDentDepth
                            : MaximumRearEndDentDepth)
                        : MaximumSideCumulativeDentDepth;
                    if (sourceVertices != null && vertexIndex < sourceVertices.Length)
                    {
                        var originalWorldVertex =
                            filter.transform.TransformPoint(sourceVertices[vertexIndex]);
                        var cumulativeOffset = worldVertex - originalWorldVertex;
                        if (cumulativeOffset.sqrMagnitude > cumulativeCap * cumulativeCap)
                        {
                            worldVertex = originalWorldVertex +
                                          cumulativeOffset.normalized * cumulativeCap;
                        }
                    }
                    vertices[vertexIndex] = filter.transform.InverseTransformPoint(worldVertex);
                    meshChanged = true;
                }

                if (!meshChanged)
                    continue;
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
            }
        }
        catch (Exception ex)
        {
            if (failureReported)
                return;
            failureReported = true;
            context?.Logger.Warn(
                $"BugattiChiron damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {ex.GetType().Name}: {ex.Message}");
        }
    }
}

[AddComponentMenu("")]
internal sealed class BugattiChironAiVehiclePinRecovery : MonoBehaviour
{
    private const float MaximumStuckSpeedMps = 0.90f;
    private const float RequiredThrottle = 0.35f;
    private const float RequiredContactSeconds = 0.35f;
    private const float RequiredThrottleSeconds = 0.45f;
    private const float RecoveryCollisionIgnoreSeconds = 1.15f;
    private const float RecoveryEscapeSpeedMps = 2.5f;
    private const float RecoveryLiftSpeedMps = 0.25f;
    private const float RecoveryCooldownSeconds = 3f;

    private readonly List<Collider> ownBodyColliders = new();
    private readonly List<Collider> ignoredOtherColliders = new();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.VehicleController? physicsVehicle;
    private Rigidbody? body;
    private Transform? contactedVehicleRoot;
    private ModContext? context;
    private float contactStartedAt;
    private float throttleStartedAt;
    private float recoveryEndsAt;
    private float nextRecoveryAt;
    private bool recoveryActive;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        physicsVehicle = controller.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        body = controller.GetComponent<Rigidbody>();
        ownBodyColliders.Clear();
        foreach (var child in controller.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(child.name, "BodyCollider", StringComparison.Ordinal))
                continue;
            ownBodyColliders.AddRange(child.GetComponents<Collider>());
        }
    }

    private void FixedUpdate()
    {
        if (recoveryActive)
        {
            if (Time.unscaledTime >= recoveryEndsAt)
                RestoreCollisions();
            return;
        }

        if (vehicle == null || physicsVehicle == null || body == null ||
            !vehicle.controlledByPlayer || contactedVehicleRoot == null ||
            Time.unscaledTime < nextRecoveryAt)
        {
            throttleStartedAt = 0f;
            return;
        }

        var throttle = Mathf.Abs(physicsVehicle.input.Throttle);
        if (throttle < RequiredThrottle || body.velocity.magnitude > MaximumStuckSpeedMps)
        {
            throttleStartedAt = 0f;
            return;
        }

        if (throttleStartedAt <= 0f)
            throttleStartedAt = Time.unscaledTime;
        if (Time.unscaledTime - contactStartedAt < RequiredContactSeconds ||
            Time.unscaledTime - throttleStartedAt < RequiredThrottleSeconds)
        {
            return;
        }

        BeginRecovery();
    }

    private void OnCollisionEnter(Collision collision)
    {
        TrackAiVehicleContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        TrackAiVehicleContact(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (!recoveryActive && CollisionBelongsToContact(collision))
            ClearContact();
    }

    private void OnDisable()
    {
        RestoreCollisions();
    }

    private void TrackAiVehicleContact(Collision collision)
    {
        var otherCollider = collision?.collider;
        if (recoveryActive || collision == null || otherCollider == null ||
            collision.rigidbody == body)
        {
            return;
        }

        var otherVehicle = otherCollider.GetComponentInParent<
                               NWH.VehiclePhysics2.VehicleController>() ??
                           collision.rigidbody?.GetComponentInParent<
                               NWH.VehiclePhysics2.VehicleController>();
        var otherLayer = LayerMask.LayerToName(otherCollider.gameObject.layer);
        var vehicleNamedCollider = otherCollider.name.IndexOf(
            "vehicletype_",
            StringComparison.OrdinalIgnoreCase) >= 0;
        if (otherVehicle == null && !vehicleNamedCollider &&
            !string.Equals(otherLayer, "AiVehicles", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var contactRoot = otherVehicle != null
            ? otherVehicle.transform
            : collision.rigidbody != null
                ? collision.rigidbody.transform
                : otherCollider.transform;
        if (contactedVehicleRoot == contactRoot)
            return;
        contactedVehicleRoot = contactRoot;
        contactStartedAt = Time.unscaledTime;
        throttleStartedAt = 0f;
    }

    private void BeginRecovery()
    {
        if (vehicle == null || physicsVehicle == null || body == null ||
            contactedVehicleRoot == null)
            return;

        ignoredOtherColliders.Clear();
        var otherColliders = contactedVehicleRoot.GetComponentsInChildren<Collider>(true);
        var ignoredPairs = 0;
        foreach (var other in otherColliders)
        {
            if (other == null)
                continue;
            ignoredOtherColliders.Add(other);
            foreach (var own in ownBodyColliders)
            {
                if (own == null || own == other)
                    continue;
                Physics.IgnoreCollision(own, other, true);
                ignoredPairs++;
            }
        }

        var gear = physicsVehicle.powertrain.transmission.Gear;
        var escapeDirection = gear < 0 ? -vehicle.transform.forward : vehicle.transform.forward;
        var longitudinalSpeed = Vector3.Dot(body.velocity, escapeDirection);
        if (longitudinalSpeed < RecoveryEscapeSpeedMps)
            body.velocity += escapeDirection * (RecoveryEscapeSpeedMps - longitudinalSpeed);
        body.velocity += Vector3.up * RecoveryLiftSpeedMps;
        body.WakeUp();

        recoveryActive = true;
        recoveryEndsAt = Time.unscaledTime + RecoveryCollisionIgnoreSeconds;
        nextRecoveryAt = recoveryEndsAt + RecoveryCooldownSeconds;
        context?.Logger.Warn(
            $"BugattiChiron pin recovery vehicle={vehicle.GetInstanceID()}: released vehicle overlap " +
            $"other='{contactedVehicleRoot.name}' gear={gear} ignoredPairs={ignoredPairs} " +
            $"escapeSpeed={RecoveryEscapeSpeedMps:0.0}mps ignoreFor=" +
            $"{RecoveryCollisionIgnoreSeconds:0.00}s.");
    }

    private void RestoreCollisions()
    {
        if (!recoveryActive)
            return;
        foreach (var other in ignoredOtherColliders)
        {
            if (other == null)
                continue;
            foreach (var own in ownBodyColliders)
            {
                if (own != null && own != other)
                    Physics.IgnoreCollision(own, other, false);
            }
        }

        recoveryActive = false;
        ignoredOtherColliders.Clear();
        ClearContact();
    }

    private void ClearContact()
    {
        contactedVehicleRoot = null;
        contactStartedAt = 0f;
        throttleStartedAt = 0f;
    }

    private bool CollisionBelongsToContact(Collision? collision)
    {
        if (collision?.collider == null || contactedVehicleRoot == null)
            return false;
        var colliderTransform = collision.collider.transform;
        return colliderTransform == contactedVehicleRoot ||
               colliderTransform.IsChildOf(contactedVehicleRoot);
    }
}

[DisallowMultipleComponent]
internal sealed class BugattiChironAerodynamics : MonoBehaviour
{
    // F = coefficient * velocity^2. This keeps launch response strong while
    // reproducing the rapidly increasing load a Chiron sees above 200 km/h.
    private const float DragForceCoefficient = 0.50f;
    private const float MinimumDragSpeedMps = 5f;

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
        if (speedSquared < MinimumDragSpeedMps * MinimumDragSpeedMps)
            return;

        var dragForce = DragForceCoefficient * speedSquared;
        body.AddForce(-planarVelocity.normalized * dragForce, ForceMode.Force);
    }
}

[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
internal sealed class BugattiChironHighwaySeamGuard : MonoBehaviour
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

[DefaultExecutionOrder(-100)]
internal sealed class BugattiChironBridgeSeamGuard : MonoBehaviour
{
    private const float MinimumVelocityRestoreMps = 25f;
    private const float HighSpeedVelocityMemorySeconds = 0.75f;
    private static readonly string[] KnownBridgeSeamNames =
    {
        "BridgeMiddleRoad",
        "BridgeConnectionGroundPlane",
        "BridgeJointCollider",
    };

    private Rigidbody? body;
    private Collider[] bodyColliders = Array.Empty<Collider>();
    private Vector3 velocityBeforeStep;
    private Vector3 angularVelocityBeforeStep;
    private float velocitySampleTime;

    internal void Initialize(VehicleController controller, ModContext? _)
    {
        body = controller.GetComponent<Rigidbody>();
        var colliderHolder = FindChild(controller.transform, "BodyCollider");
        bodyColliders = colliderHolder != null
            ? colliderHolder.GetComponents<Collider>()
            : Array.Empty<Collider>();

        foreach (var other in UnityEngine.Object.FindObjectsOfType<Collider>(true))
        {
            if (other == null || !IsKnownBridgeSeam(other.name))
                continue;
            IgnoreBodyCollision(other);
        }
    }

    private void FixedUpdate()
    {
        if (body == null)
            return;
        if (body.velocity.magnitude >= MinimumVelocityRestoreMps)
        {
            velocityBeforeStep = body.velocity;
            angularVelocityBeforeStep = body.angularVelocity;
            velocitySampleTime = Time.unscaledTime;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleKnownBridgeContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        HandleKnownBridgeContact(collision);
    }

    private void HandleKnownBridgeContact(Collision collision)
    {
        var other = collision?.collider;
        if (body == null || other == null || !IsKnownBridgeSeam(other.name))
            return;

        IgnoreBodyCollision(other);
        var speedBefore = velocityBeforeStep.magnitude;
        var speedAfter = body.velocity.magnitude;
        var restored = speedBefore >= MinimumVelocityRestoreMps &&
                       Time.unscaledTime - velocitySampleTime <= HighSpeedVelocityMemorySeconds &&
                       speedAfter < speedBefore * 0.98f;
        if (restored)
        {
            body.velocity = velocityBeforeStep;
            body.angularVelocity = angularVelocityBeforeStep;
        }

    }

    private int IgnoreBodyCollision(Collider other)
    {
        var ignored = 0;
        foreach (var own in bodyColliders)
        {
            if (own == null || own == other)
                continue;
            Physics.IgnoreCollision(own, other, true);
            ignored++;
        }
        return ignored;
    }

    private static bool IsKnownBridgeSeam(string objectName)
    {
        foreach (var seamName in KnownBridgeSeamNames)
        {
            if (objectName.IndexOf(seamName, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static Transform? FindChild(Transform root, string objectName)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, objectName, StringComparison.Ordinal))
                return child;
        }
        return null;
    }
}
