#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BAModAPI;
using BusinessLayoutSets;
using Helpers;
using UI.PurchaseVehicle;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

public sealed class LamborghiniRevueltoRuntime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStablePasses = 5;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 1772f;
    private const float EnginePowerKw = 747f;
    private const float EngineIdleRpm = 1000f;
    private const float EngineLimitRpm = 9500f;
    private const float SpeedLimitKph = 355f;
    private const float FinalDriveRatio = 3.15f;
    private const float DownshiftRpm = 6500f;
    private const float EngineInertia = 0.09f;
    private const float EngineStartDuration = 0.42f;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 3;
    private const float ClutchEngagementRpm = 1400f;
    private const float ClutchThrottleOffsetRpm = 700f;
    private const float ClutchEngagementRange = 650f;
    private const float ClutchCreepTorque = 0f;
    private const float TireFrictionCircleStrength = 0.92f;
    private const float AntiRollBarForce = 7800f;
    private const float FrontSuspensionTravel = 0.08f;
    private const float RearSuspensionTravel = 0.06f;
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.67f, 1.68f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.94f, 0.46f, 1.10f);
    private const float DeformationStrength = 0.20f;
    private const float DeformationRadius = 0.22f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 1f;
    private const float DamageDecelerationThreshold = 500f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.10f, -0.08f);

    private static readonly float[] RevueltoGears =
    {
        -3.13f,
        0f,
        3.08f,
        2.19f,
        1.63f,
        1.29f,
        1.03f,
        0.84f,
        0.69f,
        0.58f,
    };

    private static AnimationCurve CreateRevueltoPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.23f, 0.17f),
            new Keyframe(0.55f, 0.36f),
            new Keyframe(0.78f, 0.58f),
            new Keyframe(0.90f, 1f),
            new Keyframe(1f, 0.88f));

    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private Coroutine? initializationCoroutine;
    private Coroutine? enteredVehicleActivationCoroutine;
    private int enteredVehicleActivationInstanceId;
    private ModContext? context;
    private bool dealerReady;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;
    private string vehicleTypeName = string.Empty;
    private int observedPlayerVehicleCount = -1;
    private GameObject? playerVehiclePrefab;
    private VehicleController? previewVehicle;
    private LamborghiniRevueltoPaintController? previewPaintController;
    private VehicleController? observedSelectedVehicle;
    private bool observedSelectedReady;
    private bool observedSelectedControlled;

    public static LamborghiniRevueltoRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<LamborghiniRevueltoRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(LamborghiniRevueltoRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<LamborghiniRevueltoRuntime>();
        }

        runtime.context = context;
        var dealerEntryMarker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "LamborghiniRevuelto", "Config", "dealer-entry-diagnostics.enabled");
        if (File.Exists(dealerEntryMarker))
        {
            LamborghiniRevueltoDiagnostics.DebugEnabled = true;
            LamborghiniRevueltoDiagnostics.DealerEntryDebugEnabled = true;
            context.Logger.Info("LamborghiniRevuelto dealer-entry diagnostics enabled.");
        }
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        LamborghiniRevueltoPrivateDriverSupport.SetContext(context);
        var trafficFrequency = runtime.GetComponent<LamborghiniRevueltoTrafficFrequency>() ??
                               runtime.gameObject.AddComponent<LamborghiniRevueltoTrafficFrequency>();
        trafficFrequency.Initialize(context);
        runtime.SubscribeEvents();
        GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
        runtime.ScheduleInitialization("mod-load");
        return runtime;
    }

    public void Shutdown()
    {
        StopEnteredVehicleActivation();
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
        configuredVehicleIds.Clear();
        dealerReady = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        previewVehicle = null;
        previewPaintController = null;
        LamborghiniRevueltoPrivateDriverSupport.RemoveVehicle(vehicleTypeName);
        playerVehiclePrefab = null;
        Destroy(gameObject);
    }

    private void Update()
    {
        var currentCount = VehicleHelper.AllPlayerVehicles?.Count ?? 0;
        if (currentCount != observedPlayerVehicleCount)
            ConfigureExistingVehicles(out _);

        WatchSelectedVehicle();
        RefreshPaintPreview();
    }

    private void WatchSelectedVehicle()
    {
        var selected = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!ReferenceEquals(selected, observedSelectedVehicle))
        {
            observedSelectedVehicle = selected;
            observedSelectedReady = false;
            observedSelectedControlled = false;
        }

        var controlled = selected != null && selected.controlledByPlayer;
        if (selected != null && IsTargetVehicle(selected) &&
            (!observedSelectedReady || (controlled && !observedSelectedControlled)))
        {
            observedSelectedReady = true;
            TryConfigureVehicle(selected);
            ScheduleEnteredVehicleActivation(selected);
        }
        observedSelectedControlled = controlled;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SubscribeEvents();
    }

    private void OnDisable()
    {
        StopEnteredVehicleActivation();
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
        if (LamborghiniRevueltoLoadRecovery.CompleteInterruptedLoad(context))
            StartCoroutine(ReportLoadedInputState());
        privateDriverRegistrationAllowed = true;
        ScheduleInitialization("game-loaded-late");
    }

    private IEnumerator ReportLoadedInputState()
    {
        // The native loading screen fades out for 0.8 seconds after this event.
        yield return new WaitForSecondsRealtime(2f);
        LamborghiniRevueltoLoadRecovery.ReportInputState(context);
    }

    private void HandleGameUnloaded()
    {
        StopEnteredVehicleActivation();
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
        configuredVehicleIds.Clear();
        dealerReady = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        observedPlayerVehicleCount = -1;
        previewVehicle = null;
        previewPaintController = null;
        observedSelectedVehicle = null;
        observedSelectedReady = false;
        observedSelectedControlled = false;
    }

    private void RefreshPaintPreview()
    {
        if (!PurchaseVehicleUI.IsPanelOpen)
        {
            // ResetColor restores CarFeatures as the repaint panel closes. Give
            // the HDRP adapter one final event-bound refresh so cancelling a
            // preview cannot leave its per-material color overrides behind.
            previewPaintController?.RefreshColor();
            previewVehicle = null;
            previewPaintController = null;
            return;
        }

        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!ReferenceEquals(selectedVehicle, previewVehicle))
        {
            previewVehicle = selectedVehicle;
            previewPaintController = IsTargetVehicle(selectedVehicle)
                ? selectedVehicle!.GetComponent<LamborghiniRevueltoPaintController>()
                : null;
        }

        previewPaintController?.RefreshColor();
    }

    private void HandleGameEvent(string _)
    {
        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        selectedVehicle!
            .GetComponent<LamborghiniRevueltoPaintController>()
            ?.RefreshColor();
    }

    private bool IsTargetVehicle(VehicleController? vehicle) =>
        vehicle?.vehicleInstance != null &&
        string.Equals(
            vehicle.vehicleInstance.vehicleTypeName,
            vehicleTypeName,
            StringComparison.Ordinal);

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        TryConfigureVehicle(vehicle);
        vehicle?.GetComponent<LamborghiniRevueltoGlassController>()
            ?.RestoreAfterVehicleEntered();
        if (vehicle != null && IsTargetVehicle(vehicle))
            ScheduleEnteredVehicleActivation(vehicle);
    }

    private void ScheduleEnteredVehicleActivation(VehicleController vehicle)
    {
        var instanceId = vehicle.GetInstanceID();
        if (!configuredVehicleIds.Contains(instanceId) ||
            (!vehicle.controlledByPlayer &&
             !ReferenceEquals(InstanceBehavior<GameManager>.Instance?.selectedVehicle, vehicle)))
            return;
        if (enteredVehicleActivationCoroutine != null &&
            enteredVehicleActivationInstanceId == instanceId)
            return;

        StopEnteredVehicleActivation();
        enteredVehicleActivationInstanceId = instanceId;
        LamborghiniRevueltoDiagnostics.DealerEntryInfo(context,
            $"LamborghiniRevuelto dealer-entry scheduled vehicle={instanceId} " +
            $"controlled={vehicle.controlledByPlayer} selected=" +
            $"{ReferenceEquals(InstanceBehavior<GameManager>.Instance?.selectedVehicle, vehicle)}.");
        enteredVehicleActivationCoroutine = StartCoroutine(ActivateEnteredVehicle(vehicle));
    }

    private IEnumerator ActivateEnteredVehicle(VehicleController vehicle)
    {
        // Let native entry finish the dealer display-to-player transition.
        // Only recover a dormant powertrain; native entry owns body constraints.
        yield return new WaitForSecondsRealtime(0.25f);
        for (var wait = 0; wait < 30 && vehicle != null &&
             !vehicle.controlledByPlayer; wait++)
            yield return new WaitForSecondsRealtime(0.1f);

        if (vehicle == null || !vehicle.controlledByPlayer || !IsTargetVehicle(vehicle))
        {
            LamborghiniRevueltoDiagnostics.DealerEntryInfo(context,
                $"LamborghiniRevuelto dealer-entry cancelled vehicle={vehicle?.GetInstanceID()} " +
                $"controlled={vehicle?.controlledByPlayer}.");
            enteredVehicleActivationCoroutine = null;
            enteredVehicleActivationInstanceId = 0;
            yield break;
        }

        var physics = vehicle.GetComponent<PhysicsVehicle>() ??
                      vehicle.GetComponentInChildren<PhysicsVehicle>(true);
        if (physics == null)
        {
            context?.Logger.Warn(
                $"LamborghiniRevuelto dealer-entry vehicle={vehicle.GetInstanceID()}: " +
                "NWH physics controller unavailable.");
            enteredVehicleActivationCoroutine = null;
            enteredVehicleActivationInstanceId = 0;
            yield break;
        }

        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        for (var attempt = 1; attempt <= EngineStartAttemptCount; attempt++)
        {
            if (vehicle == null || !vehicle.controlledByPlayer)
                break;
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            LamborghiniRevueltoDiagnostics.DealerEntryInfo(context,
                $"LamborghiniRevuelto dealer-entry vehicle={vehicle.GetInstanceID()} " +
                $"attempt={attempt} physics={physics.enabled} running={engine.IsRunning} " +
                $"ignition={engine.ignition} canRun={engine.canRun} rpm={rpm:0} " +
                $"gear={transmission.Gear} ratio={transmission.currentGearRatio:0.000}.");
            if (physics.enabled && engine.IsRunning && engine.ignition && engine.canRun &&
                rpm >= MinimumHealthyEngineRpm)
            {
                if (transmission.Gear == 0)
                {
                    transmission.ShiftInto(1, true);
                    yield return new WaitForFixedUpdate();
                }
                if (transmission.Gear != 0)
                {
                    LamborghiniRevueltoDiagnostics.DealerEntryInfo(context,
                        $"LamborghiniRevuelto dealer-entry ready vehicle={vehicle.GetInstanceID()} " +
                        $"attempt={attempt} gear={transmission.Gear}.");
                    enteredVehicleActivationCoroutine = null;
                    enteredVehicleActivationInstanceId = 0;
                    yield break;
                }
            }

            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(0.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
                break;
            engine.StartEngine();
            yield return new WaitForSecondsRealtime(0.75f);
            if (vehicle != null && vehicle.controlledByPlayer && transmission.Gear == 0)
                transmission.ShiftInto(1, true);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
        if (vehicle != null && vehicle.controlledByPlayer)
            context?.Logger.Warn(
                $"LamborghiniRevuelto dealer-entry vehicle={vehicle.GetInstanceID()}: " +
                $"drivetrain remained unavailable physics={physics.enabled} " +
                $"running={engine.IsRunning} rpm={engine.RPMPercent * engine.revLimiterRPM:0} " +
                $"gear={transmission.Gear}.");
    }

    private void StopEnteredVehicleActivation()
    {
        if (enteredVehicleActivationCoroutine != null)
            StopCoroutine(enteredVehicleActivationCoroutine);
        enteredVehicleActivationCoroutine = null;
        enteredVehicleActivationInstanceId = 0;
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!dealerReady &&
            !BusinessLayoutSetHelper.loadingLayouts &&
            LamborghiniRevueltoLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
        {
            EnsureDealerStock("dealer-entered");
        }
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (!isOpen)
            return;

        if (!dealerReady && !BusinessLayoutSetHelper.loadingLayouts)
            EnsureDealerStock("full-menu");
        if (privateDriverRegistrationAllowed &&
            (!privateDriverReady || !privateDriverPoolReady))
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
                $"LamborghiniRevuelto: luxury dealer stock not ready source='{source}', " +
                $"matchedVehicles={maximumMatchedCount}.");
        }
        if (privateDriverRegistrationAllowed && !privateDriverReady)
        {
            context?.Logger.Warn(
                $"LamborghiniRevuelto: private-driver support not ready source='{source}'.");
        }
        if (!privateDriverPoolReady)
        {
            context?.Logger.Warn(
                $"LamborghiniRevuelto: private-driver traffic pool not ready source='{source}'.");
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
            dealerReady = LamborghiniRevueltoLuxuryDealerStock.EnsureVehicleAvailable(
                vehicleTypeName);
            return dealerReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"LamborghiniRevuelto: dealer stock update failed source='{source}': " +
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
                privateDriverReady = LamborghiniRevueltoPrivateDriverSupport.EnsureVehicleAvailable(
                    vehicleTypeName);
                if (privateDriverReady)
                {
                    context?.Logger.Info(
                        $"LamborghiniRevuelto: private-driver support registered source='{source}'.");
                }
            }
            return privateDriverReady && privateDriverPoolReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"LamborghiniRevuelto: private-driver registration failed source='{source}': " +
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
            return LamborghiniRevueltoPrivateDriverSupport.PrepareTrafficPool(
                playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                context?.Logger.Warn(
                    $"LamborghiniRevuelto: private-driver pool preparation failed source='{source}': " +
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
        {
            observedPlayerVehicleCount = 0;
            return;
        }

        observedPlayerVehicleCount = vehicles.Count;

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
            vehicle.GetComponent<LamborghiniRevueltoPaintController>()?.RefreshColor();
            return;
        }

        try
        {
            var rigidbody = vehicle.GetComponent<Rigidbody>() ?? vehicle.GetComponentInParent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.mass = VehicleMass;
                rigidbody.centerOfMass = StableCenterOfMass;
                rigidbody.drag = 0f;
                rigidbody.angularDrag = 1.45f;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rigidbody.solverIterations = Mathf.Max(rigidbody.solverIterations, 12);
                rigidbody.solverVelocityIterations =
                    Mathf.Max(rigidbody.solverVelocityIterations, 4);

                var aerodynamics = vehicle.GetComponent<LamborghiniRevueltoAerodynamics>();
                if (aerodynamics == null)
                    aerodynamics = vehicle.gameObject.AddComponent<LamborghiniRevueltoAerodynamics>();
                aerodynamics.Initialize(rigidbody);

                var highwaySeamGuard =
                    vehicle.GetComponent<LamborghiniRevueltoHighwaySeamGuard>();
                if (highwaySeamGuard == null)
                {
                    highwaySeamGuard = vehicle.gameObject
                        .AddComponent<LamborghiniRevueltoHighwaySeamGuard>();
                }
                highwaySeamGuard.Initialize(rigidbody);
            }

            ConfigureMassProperties(vehicle.gameObject);
            ConfigureWheelControllers(vehicle.gameObject);
            var rimGeometryController =
                vehicle.GetComponent<LamborghiniRevueltoRimGeometryController>();
            if (rimGeometryController == null)
            {
                rimGeometryController = vehicle.gameObject
                    .AddComponent<LamborghiniRevueltoRimGeometryController>();
            }
            rimGeometryController.Initialize(context);
            var contactMaterialOwner =
                vehicle.GetComponent<LamborghiniRevueltoContactMaterialOwner>();
            if (contactMaterialOwner == null)
            {
                contactMaterialOwner = vehicle.gameObject
                    .AddComponent<LamborghiniRevueltoContactMaterialOwner>();
            }
            ConfigureBodyColliders(
                vehicle.gameObject,
                contactMaterialOwner.GetOrCreateMaterial());
            ConfigureNavMeshObstacles(vehicle.gameObject);
            ConfigureVisualDamage(vehicle);
            ConfigurePowertrain(vehicle.gameObject);
            var caliperController = vehicle.GetComponent<LamborghiniRevueltoCaliperController>();
            if (caliperController == null)
                caliperController = vehicle.gameObject.AddComponent<LamborghiniRevueltoCaliperController>();
            caliperController.Initialize(vehicle, context);
            LamborghiniRevueltoMaterials.FixSolidMaterials(vehicle.gameObject);
            var glassController = vehicle.GetComponent<LamborghiniRevueltoGlassController>();
            if (glassController == null)
                glassController = vehicle.gameObject.AddComponent<LamborghiniRevueltoGlassController>();
            glassController.Initialize(context);
            var lightingController = vehicle.GetComponent<LamborghiniRevueltoLightingController>();
            if (lightingController == null)
                lightingController = vehicle.gameObject.AddComponent<LamborghiniRevueltoLightingController>();
            lightingController.Initialize(vehicle, context);
            var driverController = vehicle.GetComponent<LamborghiniRevueltoDriverController>();
            if (driverController == null)
                driverController = vehicle.gameObject.AddComponent<LamborghiniRevueltoDriverController>();
            driverController.Initialize(vehicle, context);
            var paintController = vehicle.GetComponent<LamborghiniRevueltoPaintController>();
            if (paintController == null)
                paintController = vehicle.gameObject.AddComponent<LamborghiniRevueltoPaintController>();
            paintController.Initialize(vehicle, context);
            var audioController = vehicle.GetComponent<LamborghiniRevueltoAudioController>();
            if (audioController == null)
                audioController = vehicle.gameObject.AddComponent<LamborghiniRevueltoAudioController>();
            audioController.Initialize(vehicle, context);
            if (ReferenceEquals(InstanceBehavior<GameManager>.Instance?.selectedVehicle, vehicle))
                ScheduleEnteredVehicleActivation(vehicle);
        }
        catch (Exception exception)
        {
            configuredVehicleIds.Remove(instanceId);
            context?.Logger.Warn(
                $"LamborghiniRevuelto: vehicle configuration failed instance={instanceId}: " +
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
                SetFloat(spring, "maxForce", 20500f);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", isFront ? 0.348f : 0.370f);
                SetFloat(wheel, "width", isFront ? 0.265f : 0.345f);
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
                colliders[0].center = new Vector3(0f, 0.38f, -0.02f);
                colliders[0].size = new Vector3(1.96f, 0.46f, 4.82f);
            }
            if (colliders.Length > 1)
            {
                colliders[1].center = new Vector3(0f, 0.78f, -0.18f);
                colliders[1].size = new Vector3(1.72f, 0.62f, 2.62f);
            }

            // The Revuelto's wedge-shaped nose is taller than the lower body box,
            // while the cabin box ends behind the front axle. Without this fitted
            // bridge, the nose can pass beneath a van's body collider and become
            // trapped before the physics solver sees the upper body.
            var frontContactCollider = colliders.Length > 2
                ? colliders[2]
                : transform.gameObject.AddComponent<BoxCollider>();
            frontContactCollider.center = FrontContactColliderCenter;
            frontContactCollider.size = FrontContactColliderSize;
            frontContactCollider.isTrigger = false;
            frontContactCollider.enabled = true;

            foreach (var collider in transform.GetComponents<BoxCollider>())
                collider.sharedMaterial = contactMaterial;
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
                $"LamborghiniRevuelto damage vehicle={vehicle.GetInstanceID()}: " +
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
            if (renderer != null && renderer.enabled)
                filters.Add(filter);
        }

        if (filters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"LamborghiniRevuelto damage vehicle={vehicle.GetInstanceID()}: " +
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

        var visualDamage = vehicle.GetComponent<LamborghiniRevueltoVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicle.gameObject.AddComponent<LamborghiniRevueltoVisualDamageController>();
        visualDamage.Initialize(
            vehicle,
            damageHandler,
            context,
            filters,
            DamageDecelerationThreshold / 100f);
        return filters.Count;
    }

    private static bool IsDeformableExterior(MeshFilter filter)
    {
        var name = filter.name;
        if (name.IndexOf("_Interior_", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return false;
        }

        return name.StartsWith("LamborghiniDamageBody", StringComparison.Ordinal) ||
               string.Equals(name, "Hood.075_Body_0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Hood.075_Plastic_0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Logo_Logo_0", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Front_part_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Front_vents", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Headlight_carbon", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("Headlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.StartsWith("Daylight_Part_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Daylight_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Mid_part_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Mid_parts_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Rear_part_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Rear_plastic", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Rear_vent", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Rear_engine_carbon", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Exhaust_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Tail_light_Plastic", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("Tail_light_", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("Taillight", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.StartsWith("Brake_light_", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("Turning_light", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.StartsWith("Vents_", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Mirrors_Body_0", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Mirrors_Carbon_0", StringComparison.OrdinalIgnoreCase);
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
            SetFloat(engine, "maxPower", EnginePowerKw);
            SetValue(engine, "powerCurve", typeof(AnimationCurve), CreateRevueltoPowerCurve());
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
            SetFloat(transmission, "shiftDuration", 0.065f);
            SetFloat(transmission, "_downshiftRPM", DownshiftRpm);
            SetFloat(transmission, "_upshiftRPM", 9250f);
            SetInt(transmission, "forwardGearCount", 8);
            SetInt(transmission, "reverseGearCount", 1);
            SetInt(transmission, "transmissionType", 1);
            SetFloatArray(transmission, "gears", RevueltoGears);

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
internal sealed class LamborghiniRevueltoAerodynamics : MonoBehaviour
{
    // Approximate road-load drag with a speed-squared force above the launch
    // range. This preserves the 0-100 response while curbing the overly strong
    // pull above 200 km/h seen in the diagnostic runs.
    private const float DragForceCoefficient = 0.56f;
    private const float MinimumDragSpeedMps = 25f;

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

        var dragForce = DragForceCoefficient * (speedSquared - minimumSpeedSquared);
        body.AddForce(-planarVelocity.normalized * dragForce, ForceMode.Force);
    }
}

[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
internal sealed class LamborghiniRevueltoHighwaySeamGuard : MonoBehaviour
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
internal sealed class LamborghiniRevueltoContactMaterialOwner : MonoBehaviour
{
    private PhysicMaterial? contactMaterial;

    internal PhysicMaterial GetOrCreateMaterial()
    {
        if (contactMaterial != null)
            return contactMaterial;

        contactMaterial = new PhysicMaterial("Lamborghini Revuelto body contact")
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
public sealed class LamborghiniRevueltoRimGeometryController : MonoBehaviour
{
    private readonly List<Mesh> runtimeMeshes = new List<Mesh>();
    private bool initialized;

    internal int Initialize(ModContext? context)
    {
        if (initialized)
            return runtimeMeshes.Count;

        initialized = true;
        var mirrored = 0;
        mirrored += MirrorRightMesh("Wheel_FR_Rim_0", "Wheel_FL_Rim_0");
        mirrored += MirrorRightMesh("Wheel_BR_Rim_0", "Wheel_BL_Rim_0");
        mirrored += MirrorRightMesh("Wheel_FR_Caliper_0", "Wheel_FL_Caliper_0");
        mirrored += MirrorRightMesh("Wheel_BR_Caliper_0", "Wheel_BL_Caliper_0");
        mirrored += MirrorRightMesh("Wheel_FR_Tire_0", "Wheel_FL_Tire_0");
        mirrored += MirrorRightMesh("Wheel_BR_Tire_0", "Wheel_BL_Tire_0");
        mirrored += MirrorRightMesh("Wheel_FR_Brake_rotor_0", "Wheel_FL_Brake_rotor_0");
        mirrored += MirrorRightMesh("Wheel_BR_Brake_rotor_0", "Wheel_BL_Brake_rotor_0");
        mirrored += MirrorRightMesh("Wheel_FR_Logo_0", "Wheel_FL_Logo_0");
        mirrored += MirrorRightMesh("Wheel_BR_Logo_0", "Wheel_BL_Logo_0");
        if (mirrored != 10)
        {
            context?.Logger.Warn(
                $"LamborghiniRevuelto wheel finish vehicle={GetInstanceID()}: mirrored " +
                $"{mirrored}/10 left-side wheel meshes; a mesh pair is missing.");
        }
        return mirrored;
    }

    private int MirrorRightMesh(string rightName, string leftName)
    {
        MeshFilter? right = null;
        MeshFilter? left = null;
        foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
        {
            if (string.Equals(filter.name, rightName, StringComparison.Ordinal))
                right = filter;
            else if (string.Equals(filter.name, leftName, StringComparison.Ordinal))
                left = filter;
        }
        if (right?.sharedMesh == null || left == null)
            return 0;

        var mirroredMesh = Instantiate(right.sharedMesh);
        mirroredMesh.name = leftName + "_MirroredFromRight";
        var rightToRoot = transform.worldToLocalMatrix * right.transform.localToWorldMatrix;
        var rootToLeft = left.transform.worldToLocalMatrix * transform.localToWorldMatrix;
        var rightToMirroredLeft =
            rootToLeft * Matrix4x4.Scale(new Vector3(-1f, 1f, 1f)) * rightToRoot;
        var vertices = mirroredMesh.vertices;
        var normals = mirroredMesh.normals;
        var tangents = mirroredMesh.tangents;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = rightToMirroredLeft.MultiplyPoint3x4(vertices[index]);
        mirroredMesh.vertices = vertices;

        // Preserve the preferred right-side authored smoothing exactly. A
        // recalculation produces subtly different highlights even when geometry
        // and materials match, which made the left wheels look washed out.
        var normalTransform = rightToMirroredLeft.inverse.transpose;
        for (var index = 0; index < normals.Length; index++)
            normals[index] = normalTransform.MultiplyVector(normals[index]).normalized;
        mirroredMesh.normals = normals;
        var handedness = rightToMirroredLeft.determinant < 0f ? -1f : 1f;
        for (var index = 0; index < tangents.Length; index++)
        {
            var direction = rightToMirroredLeft.MultiplyVector(
                new Vector3(tangents[index].x, tangents[index].y, tangents[index].z)).normalized;
            tangents[index] = new Vector4(
                direction.x,
                direction.y,
                direction.z,
                tangents[index].w * handedness);
        }
        mirroredMesh.tangents = tangents;

        // Mirroring reverses handedness. Restore outward-facing triangle winding
        // before deriving normals so both sides respond identically to lighting.
        for (var subMesh = 0; subMesh < mirroredMesh.subMeshCount; subMesh++)
        {
            var triangles = mirroredMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var second = triangles[index + 1];
                triangles[index + 1] = triangles[index + 2];
                triangles[index + 2] = second;
            }
            mirroredMesh.SetTriangles(triangles, subMesh, false);
        }
        mirroredMesh.RecalculateBounds();
        left.sharedMesh = mirroredMesh;
        runtimeMeshes.Add(mirroredMesh);
        return 1;
    }

    private void OnDestroy()
    {
        foreach (var mesh in runtimeMeshes)
        {
            if (mesh != null)
                Destroy(mesh);
        }
        runtimeMeshes.Clear();
    }
}

[AddComponentMenu("")]
public sealed class LamborghiniRevueltoGlassController : MonoBehaviour
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
            EnsureVisible();
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
                    !LamborghiniRevueltoMaterials.IsCabinGlassMaterial(source))
                {
                    continue;
                }

                containsCabinGlass = true;
                if (!runtimeMaterials.TryGetValue(source, out var runtimeMaterial))
                {
                    runtimeMaterial = Instantiate(source);
                    runtimeMaterial.name = source.name + "_RuntimeCabinGlass";
                    LamborghiniRevueltoMaterials.RestoreCabinGlassMaterial(runtimeMaterial);
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
        // Vehicle entry can alter renderer state after the entry callback. Two
        // deferred event passes restore glass once setup has settled, without a
        // permanent per-frame poll.
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
            {
                renderer.SetPropertyBlock(null);
            }
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material != null &&
                    LamborghiniRevueltoMaterials.IsCabinGlassMaterial(material))
                {
                    renderer.SetPropertyBlock(null, index);
                    LamborghiniRevueltoMaterials.RestoreCabinGlassMaterial(material);
                }
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
public sealed class LamborghiniRevueltoVisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.64f;
    private const float MaximumDentDepth = 0.34f;
    private const float DepthPerExcessMps = 0.011f;
    private const float FrontDentLateralRadius = 0.82f;
    private const float FrontDentVerticalRadius = 0.68f;
    private const float FrontDentLongitudinalRadius = 0.95f;
    private const float MaximumFrontDentDepth = 0.36f;
    private const float FrontDepthPerExcessMps = 0.012f;
    private const float RearDentLateralRadius = 0.96f;
    private const float RearDentVerticalRadius = 0.82f;
    private const float RearDentLongitudinalRadius = 1.18f;
    private const float MaximumRearDentDepth = 0.58f;
    private const float RearDepthPerExcessMps = 0.017f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.5f;

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
                    }

                    if (strongestInfluence <= 0f || inwardDirection.sqrMagnitude < 0.5f)
                        continue;
                    var falloff = selectedEndImpact
                        ? Mathf.Pow(strongestInfluence, 1.35f)
                        : strongestInfluence * strongestInfluence;
                    worldVertex += inwardDirection * (selectedDepth * falloff);
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
        catch (Exception exception)
        {
            if (failureReported)
                return;
            failureReported = true;
            context?.Logger.Warn(
                $"LamborghiniRevuelto damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
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
