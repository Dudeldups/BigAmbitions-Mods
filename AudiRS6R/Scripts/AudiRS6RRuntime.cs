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

public sealed class AudiRS6RRuntime : MonoBehaviour
{
    private const int InitializationRetryCount = 20;
    private const int RequiredStableInitializationPasses = 8;
    private const float InitializationRetryDelay = 0.25f;
    private const float AntiRollBarForce = 6500f;
    private const float BrakeActuationTime = 0.06f;
    private const float BrakeMaxTorque = 2050f;
    private const float CenterOfMassHeight = 0.25f;
    private const float DamageIntensity = 0.5f;
    private const float DamageDecelerationThreshold = 200f;
    private const float DeformationRadius = 0.62f;
    private const float DeformationStrength = 0.42f;
    private const float DriverExitLocalX = -1.5f;
    private const float ExitLocalY = 0.1f;
    private const float ExitLocalZ = 0.117f;
    private const float FuelConsumptionMultiplier = 20f;
    private const float FuelIdleConsumption = 0.045f;
    private const float EngineInertia = 0.9f;
    private const float SimulationEnginePowerKw = 390f;
    private const float EngineLossPercent = 0.57f;
    private const float ClutchSlipTorque = 780f;
    private const float CenterDifferentialRearBias = 0.60f;
    private const float HandbrakeCoefficient = 2f;
    private const float LowerBodyColliderCenterY = 0.6f;
    private const float LowerBodyColliderHeight = 0.62f;
    private const float PassengerExitLocalX = 1.5f;
    private const float RearBrakeCoefficient = 0.55f;
    private const float SuspensionMaxLength = 0.25f;
    private const float VehicleLinearDrag = 0f;
    private const float VehicleMass = 2050f;
    private const float VisualBodyLocalHeight = 0.08f;
    private const float WarehouseExitEntranceSearchRadius = 12f;
    private const float WarehouseExitTriggerClearance = 0.25f;

    private static readonly string[] VehicleLightGroupFieldNames =
    {
        "brakeLights",
        "extraLights",
        "highBeamLights",
        "leftBlinkers",
        "lowBeamLights",
        "reverseLights",
        "rightBlinkers",
        "tailLights"
    };

    private Coroutine? initializationCoroutine;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private GameObject? playerVehiclePrefab;
    private int cachedPlayerVehicleCount = -1;
    private bool dealerReady;
    private bool dealerReadyLogged;
    private bool privateDriverPoolReady;
    private bool privateDriverReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPreparationExceptionLogged;

    public static AudiRS6RRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        var runtime = FindObjectOfType<AudiRS6RRuntime>();
        if (runtime == null)
        {
            var runtimeObject = new GameObject(nameof(AudiRS6RRuntime));
            DontDestroyOnLoad(runtimeObject);
            runtime = runtimeObject.AddComponent<AudiRS6RRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.playerVehiclePrefab = playerVehiclePrefab;
        AudiRS6RPrivateDriverSupport.SetContext(context);
        var trafficFrequency = runtime.GetComponent<AudiRS6RTrafficFrequency>() ??
                               runtime.gameObject.AddComponent<AudiRS6RTrafficFrequency>();
        trafficFrequency.Initialize(context);
        runtime.SubscribeGlobalEvents();
        GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
        runtime.ScheduleInitialization("mod-load");
        return runtime;
    }

    public void Shutdown()
    {
        if (initializationCoroutine != null)
        {
            StopCoroutine(initializationCoroutine);
            initializationCoroutine = null;
        }

        cachedPlayerVehicleCount = -1;
        dealerReady = false;
        dealerReadyLogged = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
        AudiRS6RPrivateDriverSupport.RemoveVehicle(vehicleTypeName);
        playerVehiclePrefab = null;
        RemoveVehicleRuntimeComponents();
        Destroy(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SubscribeGlobalEvents();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnsubscribeGlobalEvents();
    }

    private void Update()
    {
        // Dealer purchases do not necessarily raise onEnterVehicle. Keep the
        // hot path to a count comparison and enumerate only when it changes.
        var vehicles = VehicleHelper.AllPlayerVehicles;
        var vehicleCount = vehicles?.Count ?? 0;
        if (vehicleCount == cachedPlayerVehicleCount)
            return;

        EnsureVehiclesConfigured(out _, out _);
    }

    private void SubscribeGlobalEvents()
    {
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GameEvent.onGameEventTriggered += HandleGameEvent;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onEnterBuilding += HandleBuildingEntered;
        GlobalEvents.onExitBuilding -= HandleBuildingExited;
        GlobalEvents.onExitBuilding += HandleBuildingExited;
        GlobalEvents.onBuildingRegistrationChange -= HandleBuildingRegistrationChanged;
        GlobalEvents.onBuildingRegistrationChange += HandleBuildingRegistrationChanged;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onGameUnloaded += HandleGameUnloaded;
    }

    private void UnsubscribeGlobalEvents()
    {
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onExitBuilding -= HandleBuildingExited;
        GlobalEvents.onBuildingRegistrationChange -= HandleBuildingRegistrationChanged;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // GlobalEvents delegates can be reset during a game-state transition.
        SubscribeGlobalEvents();
        ScheduleInitialization($"scene-loaded:{scene.name}");
    }

    private void HandleGameLoadedLate()
    {
        SubscribeGlobalEvents();
        privateDriverRegistrationAllowed = true;
        ScheduleInitialization("game-loaded-late");
    }

    private void HandleGameUnloaded()
    {
        if (initializationCoroutine != null)
        {
            StopCoroutine(initializationCoroutine);
            initializationCoroutine = null;
        }
        cachedPlayerVehicleCount = -1;
        dealerReady = false;
        dealerReadyLogged = false;
        privateDriverPoolReady = false;
        privateDriverReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPreparationExceptionLogged = false;
    }

    private void HandleVehicleVariablesChanged() => EnsureVehiclesConfigured(out _, out _);

    private void HandleVehicleEntered(VehicleController vehicleController)
    {
        TryConfigureVehicle(vehicleController);
        vehicleController?.GetComponent<AudiRS6RMaterialController>()?.RefreshPaint();
    }

    private void HandleGameEvent(string _)
    {
        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (selectedVehicle?.vehicleInstance == null ||
            !string.Equals(
                selectedVehicle.vehicleInstance.vehicleTypeName,
                vehicleTypeName,
                StringComparison.Ordinal))
        {
            return;
        }

        selectedVehicle.GetComponent<AudiRS6RMaterialController>()?.RefreshPaint();
    }

    private void HandleBuildingEntered(Address address)
    {
        RefreshDealerStockForAddress(address, "dealer-entered");
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
                $"AudiRS6R: warehouse exit guard could not resolve the nearby drive-in " +
                $"entrance vehicle={vehicle.GetInstanceID()} distance={distance:0.00}m.");
            return;
        }

        if (!TryGetWarehouseEnterTriggerBounds(nearestEntrance, out var triggerBounds) ||
            !TryGetWorldBodyColliderBounds(vehicle.transform, out var bodyBounds))
        {
            context?.Logger.Warn(
                $"AudiRS6R: warehouse exit correction could not resolve fitted bounds " +
                $"vehicle={vehicle.GetInstanceID()} entrance='{nearestEntrance.name}'.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(nearestEntrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < 0.0001f)
            return;
        outward.Normalize();

        // Native warehouse exit placement uses the source MeshCollider bounds,
        // which do not describe the Audi's fitted physical body. Clear only the
        // remaining trigger overlap before the next physics step, regardless of
        // whether the stored vehicle faces forward or backward.
        var triggerMaximum = Vector3.Dot(triggerBounds.center, outward) +
                             ProjectBoundsExtent(triggerBounds.extents, outward);
        var bodyMinimum = Vector3.Dot(bodyBounds.center, outward) -
                          ProjectBoundsExtent(bodyBounds.extents, outward);
        var correctionDistance = triggerMaximum + WarehouseExitTriggerClearance - bodyMinimum;
        if (correctionDistance <= 0f)
            return;

        vehicle.transform.position += outward * correctionDistance;
        Physics.SyncTransforms();
        AudiRS6RDiagnostics.WarehouseExitInfo(
            context,
            $"AudiRS6R: corrected warehouse exit trigger overlap " +
            $"vehicle={vehicle.GetInstanceID()} entrance='{nearestEntrance.name}' " +
            $"entranceDistance={distance:0.00}m correction={correctionDistance:0.00}m.");
    }

    private bool IsTargetVehicle(VehicleController? vehicle) =>
        vehicle?.vehicleInstance != null &&
        string.Equals(
            vehicle.vehicleInstance.vehicleTypeName,
            vehicleTypeName,
            StringComparison.Ordinal);

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

    private void HandleBuildingRegistrationChanged(Address address)
    {
        RefreshDealerStockForAddress(address, "dealer-registration-changed");
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
        if (privateDriverRegistrationAllowed && (!privateDriverReady || !privateDriverPoolReady))
            EnsurePrivateDriverSupport("full-menu");
    }

    private void RefreshDealerStockForAddress(Address address, string source)
    {
        if (address == null)
            return;

        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (!AudiRS6RLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
            return;

        var ready = EnsureDealerStock(source);
        if (!ready)
            context?.Logger.Warn($"AudiRS6R: luxury dealer catalog was not ready source='{source}'.");
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
        var attempts = 0;
        var maximumMatchedCount = 0;
        var configuredCount = 0;

        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            attempts = attempt;
            if (!privateDriverPoolReady && playerVehiclePrefab != null)
                privateDriverPoolReady = TryPreparePrivateDriverPool(source);
            if (!dealerReady && !BusinessLayoutSetHelper.loadingLayouts)
                EnsureDealerStock(source);
            if (privateDriverRegistrationAllowed && !privateDriverReady)
                EnsurePrivateDriverSupport(source);
            EnsureVehiclesConfigured(out var matchedCount, out var configuredThisPass);
            maximumMatchedCount = Math.Max(maximumMatchedCount, matchedCount);
            configuredCount += configuredThisPass;

            var servicesReady = dealerReady &&
                                privateDriverPoolReady &&
                                (!privateDriverRegistrationAllowed || privateDriverReady);
            if (servicesReady && matchedCount == previousMatchedCount)
                stablePasses++;
            else
                stablePasses = 0;

            previousMatchedCount = matchedCount;
            if (servicesReady && stablePasses >= RequiredStableInitializationPasses)
                break;

            if (attempt < InitializationRetryCount)
                yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        initializationCoroutine = null;
        if (!dealerReady)
        {
            context?.Logger.Warn(
                $"AudiRS6R: lifecycle initialization did not find dealer stock source='{source}', " +
                $"attempts={attempts}, matchedVehicles={maximumMatchedCount}, configuredVehicles={configuredCount}.");
        }
        if (!privateDriverPoolReady)
            context?.Logger.Warn($"AudiRS6R: private-driver traffic pool was not ready source='{source}'.");
        if (privateDriverRegistrationAllowed && !privateDriverReady)
            context?.Logger.Warn($"AudiRS6R: private-driver contract registration was not ready source='{source}'.");
    }

    private bool EnsureDealerStock(string source)
    {
        if (dealerReady)
            return true;
        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        try
        {
            dealerReady = AudiRS6RLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName, context);
            if (dealerReady && !dealerReadyLogged)
            {
                dealerReadyLogged = true;
                context?.Logger.Info($"AudiRS6R: dealer registration ready source='{source}'.");
            }
            return dealerReady;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"AudiRS6R: dealer registration failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private bool EnsurePrivateDriverSupport(string source)
    {
        if (!privateDriverRegistrationAllowed || playerVehiclePrefab == null)
            return false;

        if (!privateDriverPoolReady)
            privateDriverPoolReady = TryPreparePrivateDriverPool(source);
        if (!privateDriverReady)
            privateDriverReady = AudiRS6RPrivateDriverSupport.EnsureVehicleAvailable(vehicleTypeName);
        if (privateDriverReady && privateDriverPoolReady)
            context?.Logger.Info($"AudiRS6R: private-driver support ready source='{source}'.");
        return privateDriverReady && privateDriverPoolReady;
    }

    private bool TryPreparePrivateDriverPool(string source)
    {
        if (playerVehiclePrefab == null)
            return false;

        try
        {
            return AudiRS6RPrivateDriverSupport.PrepareTrafficPool(playerVehiclePrefab);
        }
        catch (Exception exception)
        {
            if (!privateDriverPreparationExceptionLogged)
            {
                privateDriverPreparationExceptionLogged = true;
                context?.Logger.Warn(
                    $"AudiRS6R: private-driver pool preparation failed source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
            return false;
        }
    }

    private void EnsureVehiclesConfigured(out int matchedCount, out int configuredCount)
    {
        matchedCount = 0;
        configuredCount = 0;
        if (string.IsNullOrWhiteSpace(vehicleTypeName))
            return;

        var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
        cachedPlayerVehicleCount = allPlayerVehicles?.Count ?? 0;
        if (allPlayerVehicles == null)
            return;

        foreach (var vehicleController in allPlayerVehicles)
        {
            if (vehicleController?.vehicleInstance == null ||
                !string.Equals(vehicleController.vehicleInstance.vehicleTypeName, vehicleTypeName, StringComparison.Ordinal))
            {
                continue;
            }

            matchedCount++;
            if (TryConfigureVehicle(vehicleController))
                configuredCount++;
        }
    }

    private bool TryConfigureVehicle(VehicleController? vehicleController)
    {
        if (vehicleController?.vehicleInstance == null ||
            !string.Equals(vehicleController.vehicleInstance.vehicleTypeName, vehicleTypeName, StringComparison.Ordinal))
        {
            return false;
        }

        var sleepConfigured = ConfigureSleepEnvironment(vehicleController);
        var roadDamageGuard = vehicleController.GetComponent<AudiRS6RRoadDamageGuard>();
        var materialController = vehicleController.GetComponent<AudiRS6RMaterialController>();
        var lightingController = vehicleController.GetComponent<AudiRS6RLightingController>();
        var addedRoadDamageGuard = roadDamageGuard == null;
        var addedMaterialController = materialController == null;
        var addedLightingController = lightingController == null;

        if (addedRoadDamageGuard)
        {
            ConfigureVehiclePhysics(vehicleController);
            roadDamageGuard = vehicleController.gameObject.AddComponent<AudiRS6RRoadDamageGuard>();
        }

        if (addedLightingController)
            lightingController = vehicleController.gameObject.AddComponent<AudiRS6RLightingController>();

        if (addedMaterialController)
            materialController = vehicleController.gameObject.AddComponent<AudiRS6RMaterialController>();

        // Correct imported opaque materials before the lighting controller clones any lamp surfaces.
        materialController!.Initialize(vehicleController, context);
        lightingController!.Initialize(vehicleController, context);
        roadDamageGuard!.Initialize(vehicleController);

        var driverController = vehicleController.GetComponent<AudiRS6RDriverController>();
        var addedDriverController = driverController == null;
        if (addedDriverController)
            driverController = vehicleController.gameObject.AddComponent<AudiRS6RDriverController>();
        driverController!.Initialize(vehicleController, context);

        var audioController = vehicleController.GetComponent<AudiRS6RAudioController>();
        var addedAudioController = audioController == null;
        if (addedAudioController)
            audioController = vehicleController.gameObject.AddComponent<AudiRS6RAudioController>();
        audioController!.Initialize(vehicleController, context);

        var performanceTelemetry = vehicleController.GetComponent<AudiRS6RPerformanceTelemetry>();
        var addedPerformanceTelemetry = performanceTelemetry == null;
        if (addedPerformanceTelemetry)
            performanceTelemetry = vehicleController.gameObject.AddComponent<AudiRS6RPerformanceTelemetry>();
        performanceTelemetry!.Initialize(vehicleController, context);

        return sleepConfigured > 0 || addedRoadDamageGuard || addedMaterialController || addedLightingController ||
               addedDriverController || addedAudioController || addedPerformanceTelemetry;
    }

    private void ConfigureVehiclePhysics(VehicleController vehicleController)
    {
        var rigidbody = vehicleController.GetComponent<Rigidbody>() ?? vehicleController.GetComponentInParent<Rigidbody>();
        ConfigureCenterOfMassModules(vehicleController);
        if (rigidbody != null)
        {
            rigidbody.centerOfMass = new Vector3(0f, CenterOfMassHeight, 0f);
            rigidbody.mass = VehicleMass;
            rigidbody.drag = VehicleLinearDrag;
        }

        ConfigureAccelerationDynamics(vehicleController);
        ConfigureVisualBodyHeight(vehicleController);
        var exitMarkerCount = ConfigureExitMarkers(vehicleController);
        ConfigureBodyColliders(vehicleController, out var bodyColliderCount, out var adjustedColliderCount);
        var navMeshObstacleCount = ConfigureNavMeshObstacles(vehicleController.gameObject);
        ConfigureSuspension(vehicleController, out _, out _);
        ConfigureBrakes(vehicleController, out _, out _);
        var deformableMeshCount = ConfigureVisualDamage(vehicleController);
        ConfigureFuelConsumption(vehicleController);
        ConfigureLights(vehicleController, out _, out _, out _);
        context?.Logger.Info(
            $"AudiRS6R vehicle={vehicleController.GetInstanceID()}: configured exitMarkers={exitMarkerCount}, " +
            $"bodyColliders={bodyColliderCount}, adjustedBodyColliders={adjustedColliderCount}, " +
            $"navMeshObstacles={navMeshObstacleCount}, deformableExteriorMeshes={deformableMeshCount}, " +
            $"power={SimulationEnginePowerKw:0}kW simulation/544kW rated, engineLoss={EngineLossPercent:0.00}, rearTorqueBias={CenterDifferentialRearBias:0.00}, " +
            $"brakeTorque={BrakeMaxTorque:0}Nm.");
    }

    private static void ConfigureAccelerationDynamics(VehicleController vehicleController)
    {
        var targetSpeedLimit = vehicleController.vehicleType?.maxSpeed ?? 0;

        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;

            var componentType = component.GetType();
            if (string.Equals(componentType.FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
            {
                var powertrain = GetMemberValue(component, "powertrain");
                var clutch = GetMemberValue(powertrain, "clutch");
                if (clutch != null)
                    TrySetFloatMember(clutch, "slipTorque", ClutchSlipTorque);
                var engine = GetMemberValue(powertrain, "engine");
                if (engine != null)
                {
                    TrySetFloatMember(engine, "maxPower", SimulationEnginePowerKw);
                    TrySetFloatMember(engine, "inertia", EngineInertia);
                    TrySetFloatMember(engine, "engineLossPercent", EngineLossPercent);
                    TrySetMemberValue(engine, "powerCurve", CreateRS6RPowerCurve());
                }

                if (GetMemberValue(powertrain, "differentials") is IList differentials)
                {
                    foreach (var differential in differentials)
                    {
                        if (differential == null ||
                            !string.Equals(
                                GetMemberValue(differential, "name") as string,
                                "Center Differential",
                                StringComparison.Ordinal))
                        {
                            continue;
                        }

                        TrySetFloatMember(differential, "biasAB", CenterDifferentialRearBias);
                    }
                }
            }
            else if (string.Equals(componentType.Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                var module = GetMemberValue(component, "module") ?? InvokeNoArgumentMethod(component, "GetModule");
                if (module != null && targetSpeedLimit > 0)
                    TrySetFloatMember(module, "speedLimit", targetSpeedLimit);
            }
        }
    }

    private static int ConfigureCenterOfMassModules(VehicleController vehicleController)
    {
        var configuredCount = 0;
        var targetCenterOfMass = new Vector3(0f, CenterOfMassHeight, 0f);
        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;

            var configured = TrySetVector3Field(component, "centerOfMass", targetCenterOfMass);
            configured |= TrySetVector3Field(component, "combinedCenterOfMass", targetCenterOfMass);
            if (configured)
                configuredCount++;
        }

        return configuredCount;
    }

    private static int ConfigureVisualBodyHeight(VehicleController vehicleController)
    {
        var configuredCount = 0;
        foreach (var child in vehicleController.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || child.parent == null ||
                !string.Equals(child.parent.name, "CarHolder", StringComparison.Ordinal) ||
                (!string.Equals(child.name, "Body", StringComparison.Ordinal) &&
                 !string.Equals(child.name, "Paint", StringComparison.Ordinal)))
            {
                continue;
            }

            var position = child.localPosition;
            position.y = VisualBodyLocalHeight;
            child.localPosition = position;
            configuredCount++;
        }

        return configuredCount;
    }

    private static int ConfigureExitMarkers(VehicleController vehicleController)
    {
        var configuredCount = 0;
        foreach (var child in vehicleController.GetComponentsInChildren<Transform>(true))
        {
            if (child == null)
                continue;

            float localX;
            if (string.Equals(child.name, "Driverside", StringComparison.Ordinal))
                localX = DriverExitLocalX;
            else if (string.Equals(child.name, "Passengerside", StringComparison.Ordinal))
                localX = PassengerExitLocalX;
            else
                continue;

            child.localPosition = new Vector3(localX, ExitLocalY, ExitLocalZ);
            configuredCount++;
        }

        return configuredCount;
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

            vehicle.GetComponent<AudiRS6RMaterialController>()?.RefreshPaint();
        }
    }

    private static AnimationCurve CreateRS6RPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.13f, 0.10f),
            new Keyframe(0.29f, 0.355f),
            new Keyframe(0.36f, 0.443f),
            new Keyframe(0.50f, 0.62f),
            new Keyframe(0.64f, 0.80f),
            new Keyframe(0.79f, 0.95f),
            new Keyframe(0.86f, 1f),
            new Keyframe(1f, 0.84f));

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

    private static void ConfigureBodyColliders(
        VehicleController vehicleController,
        out int bodyColliderCount,
        out int adjustedCount)
    {
        bodyColliderCount = 0;
        adjustedCount = 0;

        foreach (var boxCollider in vehicleController.GetComponentsInChildren<BoxCollider>(true))
        {
            if (boxCollider == null ||
                !string.Equals(boxCollider.name, "BodyCollider", StringComparison.Ordinal))
            {
                continue;
            }

            bodyColliderCount++;
            if (boxCollider.center.y >= 0.8f)
                continue;

            var center = boxCollider.center;
            var size = boxCollider.size;
            center.y = LowerBodyColliderCenterY;
            size.y = LowerBodyColliderHeight;
            boxCollider.center = center;
            boxCollider.size = size;
            adjustedCount++;
        }
    }

    private int ConfigureVisualDamage(VehicleController vehicleController)
    {
        foreach (var component in vehicleController.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || !string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
            {
                continue;
            }
            component.enabled = false;
            ClearCollection(component, "_deformationQueue");
        }

        var damageHandler =
            vehicleController.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true);
        if (damageHandler == null)
        {
            context?.Logger.Warn(
                $"AudiRS6R damage vehicle={vehicleController.GetInstanceID()}: " +
                "NWH damage handler is missing; visual damage remains disabled.");
            return 0;
        }

        var deformableFilters = new List<MeshFilter>();
        foreach (var filter in vehicleController.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter == null || filter.sharedMesh == null || !IsDeformableExterior(filter.name))
                continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.enabled)
                deformableFilters.Add(filter);
        }

        if (deformableFilters.Count == 0)
        {
            damageHandler.meshDeform = false;
            context?.Logger.Warn(
                $"AudiRS6R damage vehicle={vehicleController.GetInstanceID()}: " +
                "deformable exterior meshes are missing; visual damage remains disabled.");
            return 0;
        }

        context?.Logger.Info(
            $"AudiRS6R damage vehicle={vehicleController.GetInstanceID()}: " +
            $"deformationTargets=[{string.Join(",", deformableFilters.ConvertAll(filter => filter.name))}].");

        ClearCollection(damageHandler, "_collisionEvents");
        damageHandler.collisionTimeout = 0.8f;
        damageHandler.damageIntensity = DamageIntensity;
        damageHandler.decelerationThreshold = DamageDecelerationThreshold;
        damageHandler.deformationRadius = DeformationRadius;
        damageHandler.deformationRandomness = 0.01f;
        damageHandler.deformationStrength = DeformationStrength;
        damageHandler.deformationVerticesPerFrame = 8000;
        // Imported Audi panels use several local spaces. The stock deformation
        // path can miss the lower fascia, so the model-aware controller below
        // dents explicit exterior meshes in world space.
        damageHandler.meshDeform = false;

        var visualDamage = vehicleController.GetComponent<AudiRS6RVisualDamageController>();
        if (visualDamage == null)
            visualDamage = vehicleController.gameObject.AddComponent<AudiRS6RVisualDamageController>();
        visualDamage.Initialize(
            vehicleController,
            damageHandler,
            context,
            deformableFilters,
            DamageDecelerationThreshold / 100f);
        return deformableFilters.Count;
    }

    private static bool IsDeformableExterior(string name)
    {
        return string.Equals(name, "Paint", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Base_Geo_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Kit2_Paint_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Kit2_Coloured_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Kit2_Carbon1_Geo_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Kit2_Carbon2M_Geo_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Grille", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Kit2_Grille", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Kit2_Interior_Geo_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Kit2_Textured_Geo_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Light_Geo_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Kit2_Badge_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:ManufacturerPlate_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:WindowInside_Geo_", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("B:Window_Geo_lodA_B:", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "B:Window_Geo_lodA_red_glass_0", StringComparison.OrdinalIgnoreCase);
    }

    private static void ClearCollection(object target, string fieldName)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var collection = field?.GetValue(target);
        collection?.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public)
            ?.Invoke(collection, null);
    }

    private int ConfigureSleepEnvironment(VehicleController vehicleController)
    {
        try
        {
            var environmentField = typeof(VehicleController).GetField(
                "sleepEnvironment",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var environment = environmentField?.GetValue(vehicleController);
            if (environmentField == null || environment == null)
                return 0;

            var configField = environment.GetType().BaseType?.GetField(
                "config",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (configField == null)
                return 0;

            if (configField.GetValue(environment) is UnityEngine.Object currentConfig && currentConfig != null)
                return 0;

            UnityEngine.Object? carConfig = FindLoadedVehicleCarSleepConfig(
                vehicleController,
                environmentField,
                configField);
            foreach (var candidate in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (carConfig != null)
                    break;
                if (candidate == null || candidate.GetType().FullName != "PlayerActivity.SleepEnvironmentConfig")
                    continue;
                if (!IsCarSleepConfig(candidate))
                    continue;

                carConfig = candidate;
            }

            if (carConfig == null)
            {
                carConfig = CreateFallbackCarSleepConfig(configField.FieldType);
                if (carConfig == null)
                {
                    context?.Logger.Warn("AudiRS6R: current Car sleep configuration was unavailable and fallback creation failed.");
                    return 0;
                }
            }

            configField.SetValue(environment, carConfig);
            environmentField.SetValue(vehicleController, environment);
            return 1;
        }
        catch (Exception ex)
        {
            context?.Logger.Warn($"AudiRS6R: could not assign the current Car sleep configuration: {ex.Message}");
            return 0;
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
        var typeField = FindField(candidate.GetType(), "sleepEnvironmentType");
        var typeValue = typeField?.GetValue(candidate);
        return typeValue != null && Convert.ToInt32(typeValue) == 1;
    }

    private static UnityEngine.Object? CreateFallbackCarSleepConfig(Type configType)
    {
        if (!typeof(ScriptableObject).IsAssignableFrom(configType))
            return null;

        var config = ScriptableObject.CreateInstance(configType);
        config.name = "AudiRS6R Runtime Car Sleep Config";
        config.hideFlags = HideFlags.HideAndDontSave;

        SetEnumField(config, "sleepEnvironmentType", 1);
        SetEnumField(config, "energyRegen", 3);

        var balanceConfigField = FindField(configType, "balanceConfig");
        if (balanceConfigField == null || !typeof(ScriptableObject).IsAssignableFrom(balanceConfigField.FieldType))
        {
            Destroy(config);
            return null;
        }

        var balance = ScriptableObject.CreateInstance(balanceConfigField.FieldType);
        balance.name = "AudiRS6R Runtime Car Sleep Balance";
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

    private static FieldInfo? FindField(Type type, string fieldName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }

        return null;
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

    private void ConfigureSuspension(
        VehicleController vehicleController,
        out int suspensionCount,
        out int adjustedCount)
    {
        suspensionCount = 0;
        adjustedCount = 0;

        foreach (var child in vehicleController.GetComponentsInChildren<Transform>(true))
        {
            if (child == null || !IsWheelController(child.name))
                continue;

            foreach (var component in child.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                    continue;

                try
                {
                    var springField = component.GetType().GetField(
                        "spring",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var spring = springField?.GetValue(component);
                    if (springField == null || spring == null)
                        continue;

                    var maxLengthField = spring.GetType().GetField(
                        "maxLength",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (maxLengthField == null || maxLengthField.FieldType != typeof(float))
                        continue;

                    suspensionCount++;
                    var currentMaxLength = (float)(maxLengthField.GetValue(spring) ?? 0f);
                    if (Mathf.Approximately(currentMaxLength, SuspensionMaxLength))
                        continue;

                    maxLengthField.SetValue(spring, SuspensionMaxLength);
                    springField.SetValue(component, spring);
                    adjustedCount++;
                }
                catch (Exception ex)
                {
                    context?.Logger.Warn(
                        $"AudiRS6R: could not configure suspension '{child.name}': {ex.Message}");
                }
            }
        }
    }

    private void ConfigureBrakes(
        VehicleController vehicleController,
        out int brakeModuleCount,
        out int axleGroupCount)
    {
        brakeModuleCount = 0;
        axleGroupCount = 0;

        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;

            try
            {
                var componentType = component.GetType();
                var brakesField = componentType.GetField(
                    "brakes",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var brakes = brakesField?.GetValue(component);
                if (brakesField != null && brakes != null)
                {
                    var configured = TrySetFloatField(brakes, "maxTorque", BrakeMaxTorque);
                    configured |= TrySetFloatField(brakes, "actuationTime", BrakeActuationTime);
                    if (configured)
                    {
                        brakesField.SetValue(component, brakes);
                        brakeModuleCount++;
                    }
                }

                var wheelGroupsField = componentType.GetField(
                    "wheelGroups",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (!(wheelGroupsField?.GetValue(component) is IList wheelGroups))
                    continue;

                for (var index = 0; index < wheelGroups.Count; index++)
                {
                    var wheelGroup = wheelGroups[index];
                    if (wheelGroup == null)
                        continue;

                    var groupName = GetStringField(wheelGroup, "name");
                    var isRearAxle = groupName.IndexOf("Rear", StringComparison.OrdinalIgnoreCase) >= 0 || index == 1;
                    var configured = TrySetFloatField(wheelGroup, "antiRollBarForce", AntiRollBarForce);
                    if (isRearAxle)
                    {
                        configured |= TrySetFloatField(wheelGroup, "brakeCoefficient", RearBrakeCoefficient);
                        configured |= TrySetFloatField(wheelGroup, "handbrakeCoefficient", HandbrakeCoefficient);
                    }

                    if (!configured)
                        continue;

                    if (wheelGroup.GetType().IsValueType)
                        wheelGroups[index] = wheelGroup;
                    axleGroupCount++;
                }

                wheelGroupsField.SetValue(component, wheelGroups);
            }
            catch (Exception ex)
            {
                context?.Logger.Warn($"AudiRS6R: could not configure brakes: {ex.Message}");
            }
        }
    }

    private void ConfigureLights(
        VehicleController vehicleController,
        out int lightManagerCount,
        out int validLightSourceCount,
        out int invalidLightSourceCount)
    {
        lightManagerCount = 0;
        validLightSourceCount = 0;
        invalidLightSourceCount = 0;

        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;

            try
            {
                var effectsManagerField = component.GetType().GetField(
                    "effectsManager",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var effectsManager = effectsManagerField?.GetValue(component);
                if (effectsManagerField == null || effectsManager == null)
                    continue;

                var lightsManagerField = effectsManager.GetType().GetField(
                    "lightsManager",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var lightsManager = lightsManagerField?.GetValue(effectsManager);
                if (lightsManagerField == null || lightsManager == null)
                    continue;

                lightManagerCount++;
                foreach (var groupFieldName in VehicleLightGroupFieldNames)
                {
                    var groupField = lightsManager.GetType().GetField(
                        groupFieldName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var group = groupField?.GetValue(lightsManager);
                    if (groupField == null || group == null)
                        continue;

                    var sourcesField = group.GetType().GetField(
                        "lightSources",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (!(sourcesField?.GetValue(group) is IList sources))
                        continue;

                    for (var index = sources.Count - 1; index >= 0; index--)
                    {
                        var source = sources[index];
                        if (source == null)
                        {
                            sources.RemoveAt(index);
                            invalidLightSourceCount++;
                            continue;
                        }

                        var light = GetObjectField(source, "light");
                        var meshRenderer = GetObjectField(source, "meshRenderer");
                        var lightSourceType = GetIntField(source, "type", -1);
                        var isValid = lightSourceType == 0 ? light != null : meshRenderer != null;
                        if (!isValid)
                        {
                            sources.RemoveAt(index);
                            invalidLightSourceCount++;
                        }
                        else
                        {
                            validLightSourceCount++;
                        }
                    }

                    sourcesField.SetValue(group, sources);
                    if (group.GetType().IsValueType)
                        groupField.SetValue(lightsManager, group);
                }

                if (lightsManager.GetType().IsValueType)
                    lightsManagerField.SetValue(effectsManager, lightsManager);
                if (effectsManager.GetType().IsValueType)
                    effectsManagerField.SetValue(component, effectsManager);
            }
            catch (Exception ex)
            {
                context?.Logger.Warn($"AudiRS6R: could not sanitize vehicle lights: {ex.Message}");
            }
        }
    }

    private void ConfigureFuelConsumption(VehicleController vehicleController)
    {
        var fuelModuleFound = false;
        foreach (var component in vehicleController.GetComponents<MonoBehaviour>())
        {
            if (component == null ||
                !string.Equals(
                    component.GetType().FullName,
                    "NWH.VehiclePhysics2.Modules.Fuel.FuelModuleWrapper",
                    StringComparison.Ordinal))
            {
                continue;
            }

            fuelModuleFound = true;
            try
            {
                var moduleField = component.GetType().GetField(
                    "module",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var module = moduleField?.GetValue(component);
                if (moduleField == null || module == null)
                    continue;

                var multiplierConfigured =
                    TrySetFloatField(module, "consumptionMultiplier", FuelConsumptionMultiplier);
                var idleConfigured = TrySetFloatField(module, "idleConsumption", FuelIdleConsumption);
                if (multiplierConfigured || idleConfigured)
                    moduleField.SetValue(component, module);
            }
            catch (Exception ex)
            {
                context?.Logger.Warn($"AudiRS6R: could not configure fuel consumption: {ex.Message}");
            }
        }

        if (!fuelModuleFound)
            context?.Logger.Warn("AudiRS6R: fuel module was not found on the configured vehicle.");
    }

    private static bool TrySetFloatField(object target, string fieldName, float value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(float))
            return false;

        field.SetValue(target, value);
        return true;
    }

    private static object? GetMemberValue(object? target, string memberName)
    {
        if (target == null)
            return null;

        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return field.GetValue(target);

            var property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(target, null);
        }

        return null;
    }

    private static bool TrySetFloatMember(object target, string memberName, float value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null && field.FieldType == typeof(float))
            {
                field.SetValue(target, value);
                return true;
            }

            var property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null && property.CanWrite && property.PropertyType == typeof(float))
            {
                property.SetValue(target, value, null);
                return true;
            }
        }

        return false;
    }

    private static bool TrySetMemberValue(object target, string memberName, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null && field.FieldType.IsInstanceOfType(value))
            {
                field.SetValue(target, value);
                return true;
            }

            var property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null && property.CanWrite && property.PropertyType.IsInstanceOfType(value))
            {
                property.SetValue(target, value, null);
                return true;
            }
        }

        return false;
    }

    private static object? InvokeNoArgumentMethod(object target, string methodName)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);
        return method?.Invoke(target, null);
    }

    private static bool TrySetVector3Field(object target, string fieldName, Vector3 value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null || field.FieldType != typeof(Vector3))
            return false;

        field.SetValue(target, value);
        return true;
    }

    private static string GetStringField(object target, string fieldName)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(target) as string ?? string.Empty;
    }

    private static UnityEngine.Object? GetObjectField(object target, string fieldName)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(target) as UnityEngine.Object;
    }

    private static int GetIntField(object target, string fieldName, int fallback)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var value = field?.GetValue(target);
        return value != null ? Convert.ToInt32(value) : fallback;
    }

    private static bool IsWheelController(string objectName)
    {
        return string.Equals(objectName, "FrontLeft_WheelController", StringComparison.Ordinal) ||
               string.Equals(objectName, "FrontRight_WheelController", StringComparison.Ordinal) ||
               string.Equals(objectName, "RearLeft_WheelController", StringComparison.Ordinal) ||
               string.Equals(objectName, "RearRight_WheelController", StringComparison.Ordinal);
    }

    private void RemoveVehicleRuntimeComponents()
    {
        foreach (var audioController in FindObjectsOfType<AudiRS6RAudioController>(true))
            if (audioController != null) Destroy(audioController);

        foreach (var driverController in FindObjectsOfType<AudiRS6RDriverController>(true))
            if (driverController != null) Destroy(driverController);

        foreach (var materialController in FindObjectsOfType<AudiRS6RMaterialController>(true))
            if (materialController != null) Destroy(materialController);

        foreach (var telemetry in FindObjectsOfType<AudiRS6RPerformanceTelemetry>(true))
            if (telemetry != null) Destroy(telemetry);

        foreach (var damageController in FindObjectsOfType<AudiRS6RVisualDamageController>(true))
            if (damageController != null) Destroy(damageController);

        var roadDamageGuards = FindObjectsOfType<AudiRS6RRoadDamageGuard>();
        foreach (var roadDamageGuard in roadDamageGuards)
        {
            if (roadDamageGuard != null)
                Destroy(roadDamageGuard);
        }

        var lightingControllers = FindObjectsOfType<AudiRS6RLightingController>();
        foreach (var lightingController in lightingControllers)
        {
            if (lightingController != null)
                Destroy(lightingController);
        }
    }

}

[AddComponentMenu("")]
public sealed class AudiRS6RVisualDamageController : MonoBehaviour
{
    private const float DentRadius = 0.54f;
    private const float MinimumSideDentDepth = 0.016f;
    private const float MaximumSideDentDepth = 0.18f;
    private const float DepthPerExcessMps = 0.008f;
    private const float EndDentLateralRadius = 0.95f;
    private const float EndDentVerticalRadius = 0.85f;
    private const float EndDentLongitudinalRadius = 1.12f;
    private const float FrontDentCenterLowering = 0.08f;
    private const float RearDentCenterLowering = 0.18f;
    private const float MinimumFrontEndDentDepth = 0.030f;
    private const float MinimumRearEndDentDepth = 0.022f;
    private const float MaximumFrontEndDentDepth = 0.285f;
    private const float MaximumRearEndDentDepth = 0.23f;
    private const float FrontEndDepthPerExcessMps = 0.011f;
    private const float RearEndDepthPerExcessMps = 0.009f;
    private const float EndContactMinimumLongitudinalOffset = 1.35f;
    private const float CollisionCooldown = 0.5f;

    private readonly List<MeshFilter> deformableFilters = new();
    private readonly Dictionary<MeshFilter, Mesh> originalMeshes = new();
    private readonly Dictionary<MeshFilter, Vector3[]> originalVertices = new();
    private readonly List<Mesh> runtimeMeshes = new();
    private VehicleController? vehicle;
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private AudiRS6RLightingController? lightingController;
    private AudiRS6RRoadDamageGuard? roadDamageGuard;
    private ModContext? context;
    private Rigidbody? body;
    private float impactThresholdMps;
    private float nextCollisionTime;
    private float previousDamage;
    private Coroutine? numericalDamageCheck;
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
        lightingController = controller.GetComponent<AudiRS6RLightingController>();
        context = modContext;
        body = controller.GetComponent<Rigidbody>();
        impactThresholdMps = thresholdMps;
        previousDamage = handler.Damage;
        deformableFilters.Clear();
        originalMeshes.Clear();
        originalVertices.Clear();
        runtimeMeshes.Clear();
        foreach (var filter in filters)
        {
            if (filter == null || filter.sharedMesh == null)
                continue;

            var sourceMesh = filter.sharedMesh;
            var runtimeMesh = Instantiate(sourceMesh);
            runtimeMesh.name = sourceMesh.name + "_AudiRS6R_RuntimeDamage";
            filter.sharedMesh = runtimeMesh;
            deformableFilters.Add(filter);
            originalMeshes[filter] = sourceMesh;
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
            var repairedMeshes = 0;
            foreach (var pair in originalVertices)
            {
                if (pair.Key == null || pair.Key.sharedMesh == null)
                    continue;
                var mesh = pair.Key.sharedMesh;
                mesh.vertices = pair.Value;
                mesh.RecalculateBounds();
                SynchronizeLighting(pair.Key, mesh);
                repairedMeshes++;
            }
            context?.Logger.Info(
                $"AudiRS6R damage vehicle={vehicle?.GetInstanceID()}: repaired visual meshes={repairedMeshes}.");
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

        if (roadDamageGuard == null && vehicle != null)
            roadDamageGuard = vehicle.GetComponent<AudiRS6RRoadDamageGuard>();
        if (roadDamageGuard != null && roadDamageGuard.IsRoadSurfaceCollision(collision))
            return;

        try
        {
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            nextCollisionTime = Time.unscaledTime + CollisionCooldown;
            var damageBeforeImpact = CurrentNumericalDamage();
            var expectedDamage = CalculateNativeEquivalentDamage(collision);
            var contacts = collision.contacts;
            if (contacts.Length == 0)
                return;

            var contactSamples = new ContactSample[contacts.Length];
            for (var contactIndex = 0; contactIndex < contacts.Length; contactIndex++)
            {
                var contact = contacts[contactIndex];
                var localPoint = transform.InverseTransformPoint(contact.point);
                var isEndContact =
                    Mathf.Abs(localPoint.z) >= EndContactMinimumLongitudinalOffset &&
                    Mathf.Abs(localPoint.z) > Mathf.Abs(localPoint.x);
                contactSamples[contactIndex] = new ContactSample(
                    localPoint,
                    transform.InverseTransformDirection(contact.normal).normalized,
                    isEndContact,
                    isEndContact && localPoint.z >= 0f);
            }

            // Collision contacts for one rigid body impact occupy the same
            // vehicle region. Classify once so front-only/rear-only meshes are
            // skipped before their vertex buffers are read.
            var primaryContact = contactSamples[0];

            var excessSpeed = collision.relativeVelocity.magnitude - impactThresholdMps;
            var sideDentDepth = Mathf.Clamp(
                excessSpeed * DepthPerExcessMps,
                MinimumSideDentDepth,
                MaximumSideDentDepth);
            var localCenter = transform.InverseTransformPoint(
                body != null ? body.worldCenterOfMass : transform.position);
            var changedMeshes = 0;
            var changedMeshNames = new List<string>();
            var maximumDisplacement = 0f;
            var region = "side";

            foreach (var filter in deformableFilters)
            {
                if (filter == null || filter.sharedMesh == null ||
                    !CanDeformAtContact(
                        filter.name,
                        primaryContact.IsEndContact,
                        primaryContact.IsFrontEndContact))
                {
                    continue;
                }
                var mesh = filter.sharedMesh;
                var vertices = mesh.vertices;
                originalVertices.TryGetValue(filter, out var sourceVertices);
                var filterToVehicle = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var vehicleToFilter = filterToVehicle.inverse;
                var meshChanged = false;
                for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
                {
                    var vehicleLocalVertex = filterToVehicle.MultiplyPoint3x4(vertices[vertexIndex]);
                    var strongestInfluence = 0f;
                    var inwardDirection = Vector3.zero;
                    var selectedDepth = sideDentDepth;
                    var selectedEndImpact = false;
                    var selectedFrontEndImpact = false;
                    foreach (var contact in contactSamples)
                    {
                        if (!CanDeformAtContact(
                                filter.name,
                                contact.IsEndContact,
                                contact.IsFrontEndContact))
                        {
                            continue;
                        }
                        float influence;
                        Vector3 candidateDirection;
                        if (contact.IsEndContact)
                        {
                            // The Audi's visible bumper skins sit below the main
                            // collision contact. Lowering this ellipsoid keeps the
                            // fascia inside the dent instead of only folding the
                            // painted panel above it.
                            var centerLowering = contact.IsFrontEndContact
                                ? FrontDentCenterLowering
                                : RearDentCenterLowering;
                            var influenceCenter = contact.LocalPoint - Vector3.up * centerLowering;
                            var localDelta = vehicleLocalVertex - influenceCenter;
                            var normalizedDistance = Mathf.Sqrt(
                                localDelta.x * localDelta.x /
                                (EndDentLateralRadius * EndDentLateralRadius) +
                                localDelta.y * localDelta.y /
                                (EndDentVerticalRadius * EndDentVerticalRadius) +
                                localDelta.z * localDelta.z /
                                (EndDentLongitudinalRadius * EndDentLongitudinalRadius));
                            influence = 1f - normalizedDistance;
                            candidateDirection = contact.LocalPoint.z >= 0f
                                ? Vector3.back
                                : Vector3.forward;
                        }
                        else
                        {
                            influence = 1f -
                                        Vector3.Distance(vehicleLocalVertex, contact.LocalPoint) /
                                        DentRadius;
                            var towardCenter = (localCenter - contact.LocalPoint).normalized;
                            candidateDirection = Vector3.Dot(contact.LocalNormal, towardCenter) >= 0f
                                ? contact.LocalNormal
                                : -contact.LocalNormal;
                        }

                        if (influence <= strongestInfluence)
                            continue;
                        strongestInfluence = influence;
                        inwardDirection = candidateDirection;
                        selectedFrontEndImpact = contact.IsFrontEndContact;
                        selectedDepth = contact.IsEndContact
                            ? Mathf.Clamp(
                                excessSpeed * (selectedFrontEndImpact
                                    ? FrontEndDepthPerExcessMps
                                    : RearEndDepthPerExcessMps),
                                selectedFrontEndImpact
                                    ? MinimumFrontEndDentDepth
                                    : MinimumRearEndDentDepth,
                                selectedFrontEndImpact
                                    ? MaximumFrontEndDentDepth
                                    : MaximumRearEndDentDepth)
                            : sideDentDepth;
                        selectedEndImpact = contact.IsEndContact;
                    }

                    if (strongestInfluence <= 0f || inwardDirection.sqrMagnitude < 0.5f)
                        continue;
                    var falloff = selectedEndImpact
                        ? Mathf.Pow(strongestInfluence, 1.35f)
                        : strongestInfluence * strongestInfluence;
                    vehicleLocalVertex += inwardDirection * (selectedDepth * falloff);
                    var cumulativeCap = selectedEndImpact
                        ? selectedFrontEndImpact
                            ? MaximumFrontEndDentDepth
                            : MaximumRearEndDentDepth
                        : MaximumSideDentDepth;
                    if (sourceVertices != null && vertexIndex < sourceVertices.Length)
                    {
                        var originalVehicleLocalVertex =
                            filterToVehicle.MultiplyPoint3x4(sourceVertices[vertexIndex]);
                        var cumulativeOffset = vehicleLocalVertex - originalVehicleLocalVertex;
                        maximumDisplacement = Mathf.Max(maximumDisplacement, cumulativeOffset.magnitude);
                        if (cumulativeOffset.sqrMagnitude > cumulativeCap * cumulativeCap)
                        {
                            vehicleLocalVertex = originalVehicleLocalVertex +
                                                 cumulativeOffset.normalized * cumulativeCap;
                        }
                    }
                    vertices[vertexIndex] = vehicleToFilter.MultiplyPoint3x4(vehicleLocalVertex);
                    meshChanged = true;
                    if (selectedEndImpact)
                        region = selectedFrontEndImpact ? "front" : "rear";
                }

                if (!meshChanged)
                    continue;
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                SynchronizeLighting(filter, mesh);
                changedMeshes++;
                changedMeshNames.Add(filter.name);
            }

            if (changedMeshes > 0)
            {
                var elapsedMilliseconds =
                    (System.Diagnostics.Stopwatch.GetTimestamp() - startedAt) * 1000d /
                    System.Diagnostics.Stopwatch.Frequency;
                context?.Logger.Info(
                    $"AudiRS6R damage vehicle={vehicle?.GetInstanceID()}: region={region}, " +
                    $"impact={collision.relativeVelocity.magnitude:0.0}mps, meshes={changedMeshes}, " +
                    $"maxDisplacement={maximumDisplacement:0.000}m, processing={elapsedMilliseconds:0.0}ms, " +
                    $"targets=[{string.Join(",", changedMeshNames)}].");
            }

            if (numericalDamageCheck != null)
                StopCoroutine(numericalDamageCheck);
            numericalDamageCheck = StartCoroutine(
                EnsureNumericalDamageAfterCollision(damageBeforeImpact, expectedDamage, region));
        }
        catch (Exception exception)
        {
            if (failureReported)
                return;
            failureReported = true;
            context?.Logger.Warn(
                $"AudiRS6R damage vehicle={vehicle?.GetInstanceID()}: inward deformation failed " +
                $"with {exception.GetType().Name}: {exception.Message}");
        }
    }

    private float CurrentNumericalDamage()
    {
        var physicsDamage = damageHandler?.Damage ?? 0f;
        var savedDamage = vehicle?.vehicleInstance?.damage ?? 0f;
        return Mathf.Max(physicsDamage, savedDamage);
    }

    private float CalculateNativeEquivalentDamage(Collision collision)
    {
        if (body == null || damageHandler == null)
            return 0f;

        var fixedDeltaTime = Mathf.Max(Time.fixedDeltaTime, 0.001f);
        var mass = Mathf.Max(body.mass, 1f);
        var intensity = Mathf.Clamp(damageHandler.damageIntensity, 0f, 0.99f);
        return collision.impulse.magnitude /
               (fixedDeltaTime * mass * 10f) *
               intensity *
               0.005f;
    }

    private IEnumerator EnsureNumericalDamageAfterCollision(
        float damageBeforeImpact,
        float expectedDamage,
        string region)
    {
        // Let the native NWH and game save-damage handlers finish first. The
        // fallback only runs when a valid non-road impact produced no increase.
        yield return null;
        numericalDamageCheck = null;

        var damageAfterNativeHandlers = CurrentNumericalDamage();
        if (damageAfterNativeHandlers > damageBeforeImpact + 0.0001f ||
            expectedDamage <= 0.0001f ||
            vehicle?.vehicleInstance == null)
        {
            yield break;
        }

        var targetDamage = Mathf.Clamp01(damageBeforeImpact + expectedDamage);
        if (vehicle is CarController carController)
            carController.SetDamage(targetDamage);
        else
        {
            vehicle.vehicleInstance.damage = targetDamage;
            damageHandler?.SetDamage(targetDamage);
        }

        SaveGameManager.MarkChange();
        GlobalEvents.onVehicleVariablesChanged?.Invoke();
        context?.Logger.Info(
            $"AudiRS6R damage vehicle={vehicle.GetInstanceID()}: restored missing numerical " +
            $"damage region={region}, before={damageBeforeImpact:0.000}, " +
            $"after={targetDamage:0.000}, nativeAfter={damageAfterNativeHandlers:0.000}.");
    }

    private static bool CanDeformAtContact(
        string meshName,
        bool isEndContact,
        bool isFrontEndContact)
    {
        if (meshName.StartsWith("B:Kit2_Interior_Geo_", StringComparison.OrdinalIgnoreCase) ||
            meshName.StartsWith("B:WindowInside_Geo_", StringComparison.OrdinalIgnoreCase))
        {
            return isEndContact && !isFrontEndContact;
        }

        if (meshName.StartsWith("B:Window_Geo_lodA_B:", StringComparison.OrdinalIgnoreCase))
            return isEndContact;

        if (meshName.StartsWith("B:Kit2_Textured_Geo_", StringComparison.OrdinalIgnoreCase))
        {
            return isFrontEndContact;
        }

        return true;
    }

    private readonly struct ContactSample
    {
        internal ContactSample(
            Vector3 localPoint,
            Vector3 localNormal,
            bool isEndContact,
            bool isFrontEndContact)
        {
            LocalPoint = localPoint;
            LocalNormal = localNormal;
            IsEndContact = isEndContact;
            IsFrontEndContact = isFrontEndContact;
        }

        internal Vector3 LocalPoint { get; }
        internal Vector3 LocalNormal { get; }
        internal bool IsEndContact { get; }
        internal bool IsFrontEndContact { get; }
    }

    private void SynchronizeLighting(MeshFilter filter, Mesh mesh)
    {
        if (lightingController == null && vehicle != null)
            lightingController = vehicle.GetComponent<AudiRS6RLightingController>();
        lightingController?.SynchronizeDeformedSource(filter, mesh);
    }

    private void OnDestroy()
    {
        if (numericalDamageCheck != null)
            StopCoroutine(numericalDamageCheck);
        foreach (var pair in originalMeshes)
            if (pair.Key != null && pair.Value != null)
                pair.Key.sharedMesh = pair.Value;
        foreach (var mesh in runtimeMeshes)
            if (mesh != null) Destroy(mesh);
        runtimeMeshes.Clear();
    }
}
