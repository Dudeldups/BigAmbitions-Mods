#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using Helpers;
using Services;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

/// <summary>
/// Owns the Bentley's event subscriptions and dealer catalog lifecycle.
/// All setup triggers are explicit game or scene transitions; there is no polling.
/// </summary>
internal sealed class BentleyContinentalGTSpeedRuntime : MonoBehaviour
{
    private static BentleyContinentalGTSpeedRuntime? active;
    private const float MinimumHealthyEngineRpm = 300f;
    private const int EngineStartAttemptCount = 2;
    private const float ExitMarkerSafetyMargin = 0.25f;
    private const float ExitPathProbeDistance = 1.5f;
    private const float ExitNavMeshSampleTolerance = 0.5f;
    private const float NavMeshObstacleGroundOverlap = 0.02f;
    private const float WarehouseExitEntranceSearchRadius = 12f;
    private const float WarehouseExitGuardDuration = 8f;
    private const float WarehouseExitGuardClearDistance = 4f;
    private static readonly MethodInfo? NativeCarStartEngine = typeof(CarController).GetMethod(
        "StartEngine", BindingFlags.Instance | BindingFlags.NonPublic);

    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private GameObject? playerVehiclePrefab;
    private bool dealerStockReady;
    private bool dealerFailureLogged;
    private bool privateDriverPoolReady;
    private bool privateDriverContractReady;
    private bool privateDriverRegistrationAllowed;
    private bool privateDriverPoolFailureLogged;
    private bool privateDriverContractFailureLogged;
    private readonly HashSet<int> configuredVehicleIds = new HashSet<int>();
    private readonly HashSet<int> nativeRendererBindingsReadyIds = new HashSet<int>();
    private readonly List<BentleyContinentalGTSpeedAudioController> audioControllers =
        new List<BentleyContinentalGTSpeedAudioController>();
    private readonly List<BentleyContinentalGTSpeedAppearanceRuntime> appearanceControllers =
        new List<BentleyContinentalGTSpeedAppearanceRuntime>();
    private readonly List<BentleyContinentalGTSpeedDamageController> damageControllers =
        new List<BentleyContinentalGTSpeedDamageController>();
    private readonly List<BentleyContinentalGTSpeedDriverController> driverControllers =
        new List<BentleyContinentalGTSpeedDriverController>();
    private readonly List<BentleyContinentalGTSpeedLightingController> lightingControllers = new();
    private readonly List<BentleyContinentalGTSpeedRoadDynamics> roadDynamicsControllers =
        new List<BentleyContinentalGTSpeedRoadDynamics>();
    private readonly HashSet<int> drivetrainReadinessHandledIds = new HashSet<int>();
    private readonly Collider[] exitOverlapBuffer = new Collider[32];
    private Coroutine? drivetrainReadinessCoroutine;
    private Coroutine? exitObservationCoroutine;
    private Coroutine? warehouseExitGuardCoroutine;
    private readonly List<Collider> warehouseExitGuardColliders = new List<Collider>();
    private int drivetrainReadinessVehicleId;

    internal static BentleyContinentalGTSpeedRuntime Initialize(
        ModContext context,
        string vehicleTypeName,
        GameObject playerVehiclePrefab)
    {
        if (active == null)
        {
            var runtimeObject = new GameObject(nameof(BentleyContinentalGTSpeedRuntime));
            DontDestroyOnLoad(runtimeObject);
            active = runtimeObject.AddComponent<BentleyContinentalGTSpeedRuntime>();
        }

        active.StopDrivetrainReadiness();
        active.StopExitObservation();
        active.StopWarehouseExitGuard("runtime-reinitialized");
        active.context = context;
        active.vehicleTypeName = vehicleTypeName;
        active.playerVehiclePrefab = playerVehiclePrefab;
        active.dealerStockReady = false;
        active.dealerFailureLogged = false;
        active.privateDriverPoolReady = false;
        active.privateDriverContractReady = false;
        active.privateDriverRegistrationAllowed = false;
        active.privateDriverPoolFailureLogged = false;
        active.privateDriverContractFailureLogged = false;
        active.configuredVehicleIds.Clear();
        active.nativeRendererBindingsReadyIds.Clear();
        active.drivetrainReadinessHandledIds.Clear();
        active.audioControllers.Clear();
        active.appearanceControllers.Clear();
        active.damageControllers.Clear();
        active.driverControllers.Clear();
        BentleyContinentalGTSpeedPrivateDriverSupport.SetContext(context);
        active.Subscribe();
        GlobalEvents.RegisterOnGameLoadedLateCallback(active.HandleGameLoadedLate);
        active.TryPreparePrivateDriverPool("mod-load");
        active.EnsureDealerStock("mod-load");
        active.ConfigureExistingVehicles("mod-load");
        return active;
    }

    internal void Shutdown()
    {
        StopDrivetrainReadiness();
        StopExitObservation();
        StopWarehouseExitGuard("runtime-shutdown");
        Unsubscribe();
        foreach (var audio in audioControllers)
        {
            if (audio == null)
                continue;
            audio.Shutdown();
            Destroy(audio);
        }
        audioControllers.Clear();
        foreach (var appearance in appearanceControllers)
        {
            if (appearance == null)
                continue;
            appearance.Cleanup();
            Destroy(appearance);
        }
        appearanceControllers.Clear();
        foreach (var damage in damageControllers)
        {
            if (damage != null)
            {
                damage.Cleanup();
                Destroy(damage);
            }
        }
        damageControllers.Clear();
        foreach (var driver in driverControllers)
        {
            if (driver == null)
                continue;
            driver.Shutdown();
            Destroy(driver);
        }
        driverControllers.Clear();
        foreach (var lighting in lightingControllers)
            if (lighting != null) { lighting.Shutdown(); Destroy(lighting); }
        lightingControllers.Clear();
        foreach (var roadDynamics in roadDynamicsControllers)
        {
            if (roadDynamics == null)
                continue;
            roadDynamics.Shutdown();
            Destroy(roadDynamics);
        }
        roadDynamicsControllers.Clear();
        configuredVehicleIds.Clear();
        nativeRendererBindingsReadyIds.Clear();
        drivetrainReadinessHandledIds.Clear();
        dealerStockReady = false;
        privateDriverPoolReady = false;
        privateDriverContractReady = false;
        privateDriverRegistrationAllowed = false;
        playerVehiclePrefab = null;
        vehicleTypeName = string.Empty;
        context = null;
        if (ReferenceEquals(active, this))
            active = null;
        Destroy(gameObject);
    }

    private void OnEnable()
    {
        Subscribe();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        Unsubscribe();
        StopDrivetrainReadiness();
        StopExitObservation();
        StopWarehouseExitGuard("runtime-disabled");
    }

    private void Subscribe()
    {
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onEnterBuilding += HandleBuildingEntered;
        GlobalEvents.onExitBuilding -= HandleBuildingExited;
        GlobalEvents.onExitBuilding += HandleBuildingExited;
        GlobalEvents.onBuildingRegistrationChange -= HandleBuildingRegistrationChanged;
        GlobalEvents.onBuildingRegistrationChange += HandleBuildingRegistrationChanged;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onGameUnloaded += HandleGameUnloaded;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onExitVehicle += HandleVehicleExited;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GlobalEvents.onVehicleVariablesChanged += HandleVehicleVariablesChanged;
        GameEvent.onGameEventTriggered -= HandleGameEvent;
        GameEvent.onGameEventTriggered += HandleGameEvent;
    }

    private void Unsubscribe()
    {
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onEnterBuilding -= HandleBuildingEntered;
        GlobalEvents.onExitBuilding -= HandleBuildingExited;
        GlobalEvents.onBuildingRegistrationChange -= HandleBuildingRegistrationChanged;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        GameEvent.onGameEventTriggered -= HandleGameEvent;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Subscribe();
        TryPreparePrivateDriverPool("scene-loaded:" + scene.name);
        EnsureDealerStock($"scene-loaded:{scene.name}");
        ConfigureExistingVehicles("scene-loaded:" + scene.name);
    }

    private void HandleGameLoadedLate()
    {
        Subscribe();
        privateDriverRegistrationAllowed = true;
        EnsurePrivateDriverSupport("game-loaded-late");
        ConfigureExistingVehicles("game-loaded-late");
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (isOpen)
        {
            EnsureDealerStock("full-menu");
            if (privateDriverRegistrationAllowed)
                EnsurePrivateDriverSupport("full-menu");
        }
        ConfigureExistingVehicles("full-menu");
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        ConfigureTargetVehicle(vehicle, "vehicle-entered");
        if (vehicle != null)
            vehicle.GetComponent<BentleyContinentalGTSpeedDriverController>()?.SetOccupied(true);
        ScheduleDrivetrainReadiness(vehicle, "vehicle-entered", force: true);
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTargetVehicle(vehicle))
            return;

        var instanceId = vehicle!.GetInstanceID();
        vehicle.GetComponent<BentleyContinentalGTSpeedDriverController>()?.SetOccupied(false);
        drivetrainReadinessHandledIds.Remove(instanceId);
        if (drivetrainReadinessVehicleId == instanceId)
            StopDrivetrainReadiness();
        StopExitObservation();
        var normalizedObstacles = ConfigureNavMeshObstacles(
            vehicle.gameObject, out var bodyBounds, out var navMeshObstacleBounds);
        if (normalizedObstacles > 0)
        {
            ConfigureExitMarkers(vehicle.gameObject, bodyBounds, "native-exit-event");
            BentleyContinentalGTSpeedDiagnostics.ExitSafetyInfo(context,
                $"vehicle={instanceId} native-exit preparation obstacleCount={normalizedObstacles}, " +
                $"bodyColliderRootBounds={FormatBounds(bodyBounds)}, " +
                $"groundedObstacleRootBounds={FormatBounds(navMeshObstacleBounds)}.");
        }
        else
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed exit-safety vehicle={instanceId}: " +
                "could not normalize NavMesh obstacle before native exit.");
        LogVehicleNavigationGeometry(vehicle, "native-exit-event-before-native-carving");
        if (BentleyContinentalGTSpeedDiagnostics.DebugEnabled &&
            BentleyContinentalGTSpeedDiagnostics.ExitSafetyDebugEnabled)
        {
            LogExitMovementState(vehicle, "native-exit-event-before-native-warp");
            exitObservationCoroutine = StartCoroutine(ObserveNativeExitAndCarving(vehicle));
        }
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
            BentleyContinentalGTSpeedDiagnostics.WarehouseExitInfo(
                context,
                $"guard skipped: current vehicle is not a player-controlled Bentley " +
                $"(vehicle={vehicle?.GetInstanceID() ?? 0}, controlled={vehicle?.controlledByPlayer ?? false}).");
            return;
        }

        var entrance = FindClosestDriveInEntrance(vehicle.transform.position, out var distance);
        if (entrance == null || distance > WarehouseExitEntranceSearchRadius)
        {
            BentleyContinentalGTSpeedDiagnostics.WarehouseExitInfo(
                context,
                $"guard skipped: no nearby drive-in entrance; vehicle={vehicle.GetInstanceID()}, " +
                $"distance={distance:0.00}m, searchRadius={WarehouseExitEntranceSearchRadius:0.00}m.");
            return;
        }

        StopWarehouseExitGuard("restarted-for-new-exit");
        foreach (var enterTrigger in entrance.GetComponentsInChildren<DriveInEntranceEnterTrigger>(true))
        foreach (var collider in enterTrigger.GetComponents<Collider>())
        {
            if (collider == null || !collider.enabled || !collider.isTrigger)
                continue;

            warehouseExitGuardColliders.Add(collider);
            collider.enabled = false;
        }

        if (warehouseExitGuardColliders.Count == 0)
        {
            BentleyContinentalGTSpeedDiagnostics.WarehouseExitInfo(
                context,
                $"guard skipped: entrance='{entrance.name}' has no enabled entry-trigger colliders; " +
                $"vehicle={vehicle.GetInstanceID()}.");
            return;
        }

        var outward = Vector3.ProjectOnPlane(
            vehicle.transform.position - entrance.transform.position,
            Vector3.up);
        if (outward.sqrMagnitude < 0.0001f)
            outward = Vector3.ProjectOnPlane(entrance.transform.forward, Vector3.up);
        if (outward.sqrMagnitude < 0.0001f)
        {
            BentleyContinentalGTSpeedDiagnostics.WarehouseExitInfo(
                context,
                $"guard aborted: outward direction is zero; vehicle={vehicle.GetInstanceID()}, " +
                $"entrance='{entrance.name}'.");
            StopWarehouseExitGuard("zero-outward-direction");
            return;
        }

        outward.Normalize();
        var startingProjection = Vector3.Dot(vehicle.transform.position, outward);
        Physics.SyncTransforms();
        BentleyContinentalGTSpeedDiagnostics.WarehouseExitInfo(
            context,
            $"guard started: vehicle={vehicle.GetInstanceID()}, entrance='{entrance.name}', " +
            $"entranceDistance={distance:0.00}m, triggerCount={warehouseExitGuardColliders.Count}, " +
            $"outward={outward}, startProjection={startingProjection:0.000}, " +
            $"clearDistance={WarehouseExitGuardClearDistance:0.00}m, " +
            $"timeout={WarehouseExitGuardDuration:0.0}s.");
        warehouseExitGuardCoroutine = StartCoroutine(GuardWarehouseExit(
            vehicle,
            outward,
            startingProjection));
    }

    private IEnumerator GuardWarehouseExit(
        VehicleController vehicle,
        Vector3 outward,
        float startingProjection)
    {
        var expiresAt = Time.unscaledTime + WarehouseExitGuardDuration;
        var reason = "timeout";
        while (vehicle != null && vehicle.controlledByPlayer && Time.unscaledTime < expiresAt)
        {
            if (Vector3.Dot(vehicle.transform.position, outward) >=
                startingProjection + WarehouseExitGuardClearDistance)
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

        BentleyContinentalGTSpeedDiagnostics.WarehouseExitInfo(
            context,
            $"guard ending: reason={reason}, vehicle={vehicle?.GetInstanceID() ?? 0}, " +
            $"projection={(vehicle == null ? float.NaN : Vector3.Dot(vehicle.transform.position, outward)):0.000}, " +
            $"startProjection={startingProjection:0.000}.");
        RestoreWarehouseExitTriggers(reason);
    }

    private void StopWarehouseExitGuard(string reason)
    {
        if (warehouseExitGuardCoroutine != null)
            StopCoroutine(warehouseExitGuardCoroutine);
        RestoreWarehouseExitTriggers(reason);
    }

    private void RestoreWarehouseExitTriggers(string reason)
    {
        if (warehouseExitGuardColliders.Count > 0)
        {
            BentleyContinentalGTSpeedDiagnostics.WarehouseExitInfo(
                context,
                $"restoring triggerCount={warehouseExitGuardColliders.Count}, reason={reason}.");
        }

        foreach (var collider in warehouseExitGuardColliders)
        {
            if (collider != null)
                collider.enabled = true;
        }

        warehouseExitGuardColliders.Clear();
        warehouseExitGuardCoroutine = null;
        Physics.SyncTransforms();
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

    private void HandleVehicleVariablesChanged()
    {
        var selected = GameManager.Instance?.selectedVehicle;
        if (IsTargetVehicle(selected))
            ScheduleDrivetrainReadiness(selected, "vehicle-variables-changed", force: false);
        ConfigureExistingVehicles("vehicle-variables-changed");
    }

    private void HandleGameEvent(string eventName)
    {
        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (IsTargetVehicle(selectedVehicle))
        {
            ConfigureTargetVehicle(selectedVehicle, "game-event:" + eventName);
            ScheduleDrivetrainReadiness(selectedVehicle, "game-event:" + eventName, force: false);
        }
    }

    private void ConfigureExistingVehicles(string source)
    {
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
            return;

        foreach (var vehicle in vehicles)
            ConfigureTargetVehicle(vehicle, source);
    }

    private bool IsTargetVehicle(VehicleController? vehicle) =>
        vehicle?.vehicleInstance != null &&
        string.Equals(
            vehicle.vehicleInstance.vehicleTypeName,
            vehicleTypeName,
            StringComparison.Ordinal);

    private void ConfigureTargetVehicle(VehicleController? vehicle, string source)
    {
        if (!IsTargetVehicle(vehicle))
            return;

        var target = vehicle!;
        var instanceId = target.GetInstanceID();
        var lighting = BentleyContinentalGTSpeedLightingController.Configure(target, context);
        if (lighting != null && !lightingControllers.Contains(lighting)) lightingControllers.Add(lighting);
        var roadDynamics = BentleyContinentalGTSpeedRoadDynamics.Configure(target.gameObject, context);
        if (roadDynamics != null && !roadDynamicsControllers.Contains(roadDynamics))
            roadDynamicsControllers.Add(roadDynamics);
        var normalizedObstacles = ConfigureNavMeshObstacles(
            target.gameObject, out var bodyBounds, out var navMeshObstacleBounds);
        if (normalizedObstacles == 0)
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed exit-safety vehicle={instanceId}: " +
                "NavMesh obstacle bounds could not be normalized from BodyCollider.");
        else
        {
            ConfigureExitMarkers(target.gameObject, bodyBounds, "vehicle-configured:" + source);
            BentleyContinentalGTSpeedDiagnostics.ExitSafetyInfo(context,
                $"vehicle={instanceId} obstacle-normalized count={normalizedObstacles}, " +
                $"bodyColliderRootBounds={FormatBounds(bodyBounds)}, " +
                $"groundedObstacleRootBounds={FormatBounds(navMeshObstacleBounds)}.");
            LogVehicleNavigationGeometry(target, "vehicle-configured:" + source);
        }
        var driver = target.GetComponent<BentleyContinentalGTSpeedDriverController>();
        if (driver == null)
            driver = target.gameObject.AddComponent<BentleyContinentalGTSpeedDriverController>();
        driver.Initialize(target, context);
        if (!driverControllers.Contains(driver))
            driverControllers.Add(driver);
        driver.SetOccupied(target.controlledByPlayer);
        if (!configuredVehicleIds.Add(instanceId))
        {
            // Paint or glass renderers can be attached after initial vehicle
            // setup. Retry only the idempotent appearance pass on later native
            // lifecycle events; do not duplicate audio, wash, or damage setup.
            var appearanceReady = target.GetComponent<BentleyContinentalGTSpeedAppearanceRuntime>()?
                .Configure(target.gameObject, context, "vehicle-instance:" + source) ?? false;
            var nativeBindingsReady = nativeRendererBindingsReadyIds.Contains(instanceId);
            if (!nativeBindingsReady)
            {
                nativeBindingsReady = BentleyContinentalGTSpeedPrivateDriverSupport
                    .ConfigurePlayerPrefab(target.gameObject, "vehicle-instance-retry:" + source);
                if (nativeBindingsReady)
                {
                    nativeRendererBindingsReadyIds.Add(instanceId);
                    BentleyContinentalGTSpeedDiagnostics.Info(context,
                        $"BentleyContinentalGTSpeed: native renderer bindings recovered instance={instanceId}, " +
                        $"source='{source}', appearance={appearanceReady}.");
                }
            }
            BentleyContinentalGTSpeedPrivateDriverSupport.RestorePlayerSavedColor(
                target, "vehicle-instance:" + source);
            return;
        }

        try
        {
            var nativeBindingsReady = BentleyContinentalGTSpeedPrivateDriverSupport
                .ConfigurePlayerPrefab(target.gameObject, "vehicle-instance:" + source);
            if (nativeBindingsReady)
                nativeRendererBindingsReadyIds.Add(instanceId);
            if (!nativeBindingsReady)
                context?.Logger.Warn(
                    $"BentleyContinentalGTSpeed: vehicle instance={instanceId} has incomplete native renderer bindings " +
                    $"source='{source}'; wash, audio, and damage setup will continue.");
            var washReady = BentleyContinentalGTSpeedWashBindings.Configure(
                target, context, "vehicle-instance:" + source);

            var appearance = target.GetComponent<BentleyContinentalGTSpeedAppearanceRuntime>();
            if (appearance == null)
                appearance = target.gameObject.AddComponent<BentleyContinentalGTSpeedAppearanceRuntime>();
            var appearanceReady = appearance.Configure(
                target.gameObject, context, "vehicle-instance:" + source);
            if (nativeBindingsReady)
                BentleyContinentalGTSpeedPrivateDriverSupport.RestorePlayerSavedColor(
                    target, "vehicle-instance-after-appearance:" + source);
            appearanceControllers.Add(appearance);

            var audio = target.GetComponent<BentleyContinentalGTSpeedAudioController>();
            if (audio == null)
                audio = target.gameObject.AddComponent<BentleyContinentalGTSpeedAudioController>();
            audio.Initialize(target, context);
            audioControllers.Add(audio);

            var damage = target.GetComponent<BentleyContinentalGTSpeedDamageController>();
            if (damage == null)
                damage = target.gameObject.AddComponent<BentleyContinentalGTSpeedDamageController>();
            var damageReady = damage.Initialize(target, context);
            damageControllers.Add(damage);

            BentleyContinentalGTSpeedDiagnostics.Info(
                context,
                $"BentleyContinentalGTSpeed: configured instance={instanceId}, source='{source}', " +
                $"nativeRenderers={nativeBindingsReady}, washBindings={washReady}, appearance={appearanceReady}, " +
                $"audio=true, nativeDamage={damageReady}.");
        }
        catch (Exception exception)
        {
            configuredVehicleIds.Remove(instanceId);
            nativeRendererBindingsReadyIds.Remove(instanceId);
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed: instance setup failed instance={instanceId}, " +
                $"source='{source}': {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void ScheduleDrivetrainReadiness(
        VehicleController? vehicle, string source, bool force)
    {
        if (!IsTargetVehicle(vehicle) || vehicle is not CarController)
            return;

        var selected = ReferenceEquals(GameManager.Instance?.selectedVehicle, vehicle);
        if (!vehicle!.controlledByPlayer && !selected)
            return;

        var instanceId = vehicle.GetInstanceID();
        if (force)
            drivetrainReadinessHandledIds.Remove(instanceId);
        if (!force && drivetrainReadinessHandledIds.Contains(instanceId))
            return;
        if (drivetrainReadinessCoroutine != null && drivetrainReadinessVehicleId == instanceId)
        {
            BentleyContinentalGTSpeedDiagnostics.DrivetrainInfo(context,
                $"entry readiness already pending vehicle={instanceId}, source='{source}'.");
            return;
        }

        StopDrivetrainReadiness();
        drivetrainReadinessVehicleId = instanceId;
        drivetrainReadinessHandledIds.Add(instanceId);
        BentleyContinentalGTSpeedDiagnostics.DrivetrainInfo(context,
            $"entry readiness scheduled vehicle={instanceId}, source='{source}', " +
            DescribeDrivetrainState(vehicle));
        drivetrainReadinessCoroutine = StartCoroutine(EnsureDrivetrainReadyAfterEntry(vehicle, source));
    }

    private IEnumerator EnsureDrivetrainReadyAfterEntry(VehicleController vehicle, string source)
    {
        // Native dealer/entry code changes the engine and NWH state
        // asynchronously. Recover only after that transition had time to run.
        yield return new WaitForSecondsRealtime(0.25f);
        if (vehicle == null || !vehicle.controlledByPlayer || !IsTargetVehicle(vehicle))
        {
            BentleyContinentalGTSpeedDiagnostics.DrivetrainInfo(context,
                $"entry readiness cancelled vehicle={vehicle?.GetInstanceID()}, source='{source}', " +
                $"controlled={vehicle?.controlledByPlayer}.");
            if (drivetrainReadinessVehicleId != 0)
                drivetrainReadinessHandledIds.Remove(drivetrainReadinessVehicleId);
            FinishDrivetrainReadiness();
            yield break;
        }

        var physics = vehicle.GetComponent<PhysicsVehicle>() ??
                      vehicle.GetComponentInChildren<PhysicsVehicle>(true);
        if (physics == null)
        {
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed drivetrain vehicle={vehicle.GetInstanceID()}: " +
                "post-entry readiness could not find the NWH controller.");
            FinishDrivetrainReadiness();
            yield break;
        }

        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        var reenabledComponents = ReactivatePlayerPowertrain(physics);
        var recovered = false;
        for (var attempt = 1; attempt <= EngineStartAttemptCount; attempt++)
        {
            if (vehicle == null || !vehicle.controlledByPlayer || !IsTargetVehicle(vehicle))
                break;
            if (vehicle is CarController washingCar && washingCar.isWashing)
            {
                // A later vehicle-variable event marks the end of the native
                // wash transition. Leave this instance eligible for that retry.
                drivetrainReadinessHandledIds.Remove(vehicle.GetInstanceID());
                BentleyContinentalGTSpeedDiagnostics.DrivetrainInfo(context,
                    $"entry readiness deferred during native wash vehicle={vehicle.GetInstanceID()}.");
                break;
            }

            var body = physics.vehicleRigidbody;
            var needsNativeActivation = !physics.enabled || !physics.IsInitialized ||
                                        (body != null && body.isKinematic);
            if (needsNativeActivation)
            {
                if (physics.stateSettings == null || NativeCarStartEngine == null)
                {
                    context?.Logger.Warn(
                        $"BentleyContinentalGTSpeed drivetrain vehicle={vehicle.GetInstanceID()}: " +
                        $"native activation unavailable; stateSettings={physics.stateSettings != null}, " +
                        $"CarController.StartEngine={NativeCarStartEngine != null}.");
                    break;
                }

                if (!RestoreNativeDriveActivation(vehicle, physics))
                    break;
                // Let Unity run the normal NWH enable/start and native wheel
                // unfreeze callbacks before checking engine health.
                yield return null;
                yield return new WaitForFixedUpdate();
                if (vehicle == null || !vehicle.controlledByPlayer)
                    break;
            }

            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            BentleyContinentalGTSpeedDiagnostics.DrivetrainInfo(context,
                $"entry readiness attempt={attempt}, vehicle={vehicle.GetInstanceID()}, " +
                $"reactivatedComponents={reenabledComponents}, " +
                $"running={engine.IsRunning}, ignition={engine.ignition}, canRun={engine.canRun}, " +
                $"rpm={rpm:0}, gear={transmission.Gear}, physicsEnabled={physics.enabled}, " +
                $"initialized={physics.IsInitialized}, kinematic={body?.isKinematic}.");
            if (engine.IsRunning && engine.ignition && engine.canRun && rpm >= MinimumHealthyEngineRpm)
            {
                if (transmission.Gear <= 0)
                    transmission.ShiftInto(1, true);

                // Native player entry can report a successful shift before
                // the transmission engages. Match the R8 recovery path and
                // allow several physics steps before accepting Drive.
                for (var settle = 0; settle < 4; settle++)
                    yield return new WaitForFixedUpdate();

                rpm = engine.RPMPercent * engine.revLimiterRPM;
                var driveReady = physics.IsInitialized && physics.powertrain.IsActive &&
                                 engine.IsActive && transmission.IsActive &&
                                 engine.IsRunning && engine.ignition && engine.canRun &&
                                 rpm >= MinimumHealthyEngineRpm && transmission.Gear > 0 &&
                                 Mathf.Abs(transmission.currentGearRatio) > 0.001f;
                if (driveReady)
                {
                    recovered = true;
                    BentleyContinentalGTSpeedDiagnostics.DrivetrainInfo(context,
                        $"post-entry drivetrain ready vehicle={vehicle.GetInstanceID()}, " +
                        $"rpm={rpm:0}, gear={transmission.Gear}, " +
                        $"ratio={transmission.currentGearRatio:0.000}.");
                    break;
                }

                BentleyContinentalGTSpeedDiagnostics.DrivetrainInfo(context,
                    $"post-entry engine healthy but drive not engaged vehicle={vehicle.GetInstanceID()}, " +
                    $"powertrainActive={physics.powertrain.IsActive}, engineActive={engine.IsActive}, " +
                    $"transmissionActive={transmission.IsActive}, gear={transmission.Gear}, " +
                    $"ratio={transmission.currentGearRatio:0.000}; retrying shift.");
                reenabledComponents += ReactivatePlayerPowertrain(physics);
                if (attempt >= EngineStartAttemptCount)
                    break;
                continue;
            }

            if (attempt >= EngineStartAttemptCount)
                break;

            engine.StopEngine();
            transmission.ShiftInto(0, true);
            transmission.currentGearRatio = 0f;
            yield return new WaitForSecondsRealtime(0.15f);
            if (vehicle == null || !vehicle.controlledByPlayer)
                break;

            body = physics.vehicleRigidbody;
            if (!physics.enabled || !physics.IsInitialized || (body != null && body.isKinematic))
            {
                if (physics.stateSettings == null || NativeCarStartEngine == null ||
                    !RestoreNativeDriveActivation(vehicle, physics))
                    break;
            }
            else
            {
                engine.StartEngine();
            }
            yield return new WaitForSecondsRealtime(0.75f);
            if (vehicle != null && vehicle.controlledByPlayer && transmission.Gear == 0)
                transmission.ShiftInto(1, true);
            yield return new WaitForSecondsRealtime(0.15f);
        }

        if (!recovered && vehicle != null && vehicle.controlledByPlayer)
        {
            var rpm = engine.RPMPercent * engine.revLimiterRPM;
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed drivetrain vehicle={vehicle.GetInstanceID()}: " +
                $"post-entry readiness remained unhealthy; {DescribeDrivetrainState(vehicle)}, " +
                $"rpm={rpm:0}, gear={transmission.Gear}.");
        }
        FinishDrivetrainReadiness();
    }

    private bool RestoreNativeDriveActivation(VehicleController vehicle, PhysicsVehicle physics)
    {
        if (!vehicle.controlledByPlayer || vehicle is not CarController car ||
            physics.stateSettings == null || NativeCarStartEngine == null)
            return false;
        try
        {
            // Native CarController.StartEngine restores the Rigidbody and NWH
            // component through the game's own lifecycle path.
            NativeCarStartEngine.Invoke(car, null);
            BentleyContinentalGTSpeedDiagnostics.DrivetrainInfo(context,
                $"native CarController.StartEngine invoked vehicle={vehicle.GetInstanceID()}, " +
                DescribeDrivetrainState(vehicle));
            return true;
        }
        catch (Exception exception)
        {
            var failure = exception.InnerException ?? exception;
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed drivetrain vehicle={vehicle.GetInstanceID()}: " +
                $"native activation threw {failure.GetType().Name}: {failure.Message}.");
            return false;
        }
    }

    private static string DescribeDrivetrainState(VehicleController vehicle)
    {
        var physics = vehicle.GetComponent<PhysicsVehicle>() ??
                      vehicle.GetComponentInChildren<PhysicsVehicle>(true);
        var body = physics?.vehicleRigidbody;
        var engine = physics?.powertrain?.engine;
        var transmission = physics?.powertrain?.transmission;
        return $"controlled={vehicle.controlledByPlayer}, " +
               $"selected={ReferenceEquals(GameManager.Instance?.selectedVehicle, vehicle)}, " +
               $"physicsEnabled={physics?.enabled}, initialized={physics?.IsInitialized}, " +
               $"powertrainActive={physics?.powertrain?.IsActive}, " +
               $"stateSettings={physics?.stateSettings != null}, kinematic={body?.isKinematic}, " +
               $"running={engine?.IsRunning}, ignition={engine?.ignition}, canRun={engine?.canRun}, " +
               $"rpm={(engine == null ? "n/a" : (engine.RPMPercent * engine.revLimiterRPM).ToString("0"))}, " +
               $"gear={transmission?.Gear}, ratio={transmission?.currentGearRatio:0.000}.";
    }

    private static int ReactivatePlayerPowertrain(PhysicsVehicle physics)
    {
        var powertrain = physics.powertrain;
        var reenabled = 0;
        if (!powertrain.IsActive && powertrain.VC_Enable(true))
            reenabled++;

        foreach (var component in powertrain.Components)
            if (component != null && !component.IsActive && component.VC_Enable(true))
                reenabled++;

        return reenabled;
    }

    private void FinishDrivetrainReadiness()
    {
        drivetrainReadinessCoroutine = null;
        drivetrainReadinessVehicleId = 0;
    }

    private void StopDrivetrainReadiness()
    {
        if (drivetrainReadinessCoroutine != null)
            StopCoroutine(drivetrainReadinessCoroutine);
        FinishDrivetrainReadiness();
    }

    private IEnumerator ObserveNativeExitAndCarving(VehicleController vehicle)
    {
        // CarController performs its own native NavMeshAgent.Warp after one
        // frame. These observations never move or otherwise change the player.
        yield return null;
        yield return null;
        if (vehicle == null)
            yield break;

        LogExitMovementState(vehicle, "after-native-exit-warp");
        var stationaryDelay = GetStationaryCarveDelay(vehicle);
        if (stationaryDelay > 0f)
        {
            // This gated, one-shot observation measures the final native
            // carving window; it is diagnostic only and performs no recovery.
            yield return new WaitForSecondsRealtime(stationaryDelay + 0.1f);
            yield return null;
            if (vehicle != null)
                LogExitMovementState(vehicle, "after-obstacle-stationary-carve-window");
        }
        exitObservationCoroutine = null;
    }

    private float GetStationaryCarveDelay(VehicleController vehicle)
    {
        var longestDelay = 0f;
        foreach (var obstacle in vehicle.GetComponentsInChildren<NavMeshObstacle>(true))
            if (obstacle != null && obstacle.enabled && obstacle.carving && obstacle.carveOnlyStationary)
                longestDelay = Mathf.Max(longestDelay, obstacle.carvingTimeToStationary);
        return longestDelay;
    }

    private static int ConfigureNavMeshObstacles(
        GameObject root, out Bounds bodyBounds, out Bounds groundedObstacleBounds)
    {
        if (!TryGetBodyColliderBounds(root.transform, out bodyBounds))
        {
            groundedObstacleBounds = default;
            return 0;
        }

        // The native Bentley boxes describe the shell and sit 0.29 m above the
        // wheel/root plane. If copied directly, the NavMesh obstacle floats
        // above the pedestrian surface. Keep the shell's horizontal footprint
        // but extend only the navigation obstacle down through the local road
        // plane so stationary carving removes the passage beneath the car.
        groundedObstacleBounds = bodyBounds;
        var groundedMin = groundedObstacleBounds.min;
        groundedMin.y = Mathf.Min(groundedMin.y, -NavMeshObstacleGroundOverlap);
        groundedObstacleBounds.SetMinMax(groundedMin, groundedObstacleBounds.max);

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
                continue;

            var rootTransform = root.transform;
            obstacle.center = obstacleTransform.InverseTransformPoint(
                rootTransform.TransformPoint(groundedObstacleBounds.center));
            obstacle.size = new Vector3(
                ProjectBodySizeOntoAxis(groundedObstacleBounds.size, rootTransform, obstacleTransform.right) /
                Mathf.Abs(scale.x),
                ProjectBodySizeOntoAxis(groundedObstacleBounds.size, rootTransform, obstacleTransform.up) /
                Mathf.Abs(scale.y),
                ProjectBodySizeOntoAxis(groundedObstacleBounds.size, rootTransform, obstacleTransform.forward) /
                Mathf.Abs(scale.z));
            normalized++;
        }

        return normalized;
    }

    private void ConfigureExitMarkers(GameObject root, Bounds bodyBounds, string source)
    {
        var player = PlayerHelper.PlayerController;
        var character = player != null ? player.Character : null;
        var agent = character != null ? character.navmeshAgent : null;
        if (agent == null || agent.radius <= 0f)
        {
            BentleyContinentalGTSpeedDiagnostics.ExitSafetyInfo(context,
                $"source='{source}' vehicle={root.GetInstanceID()} exit markers unchanged: " +
                "the native PlayerController NavMeshAgent or its radius is unavailable.");
            return;
        }

        var requiredLateralDistance = bodyBounds.extents.x + agent.radius + ExitMarkerSafetyMargin;
        foreach (var marker in root.GetComponentsInChildren<Transform>(true))
        {
            var side = string.Equals(marker.name, "Driverside", StringComparison.Ordinal) ? -1f :
                string.Equals(marker.name, "Passengerside", StringComparison.Ordinal) ? 1f : 0f;
            if (side == 0f)
                continue;

            var oldPosition = marker.localPosition;
            var currentDistance = Mathf.Abs(oldPosition.x - bodyBounds.center.x);
            var lateralDistance = Mathf.Max(currentDistance, requiredLateralDistance);
            marker.localPosition = new Vector3(
                bodyBounds.center.x + side * lateralDistance,
                oldPosition.y,
                oldPosition.z);

            if (!BentleyContinentalGTSpeedDiagnostics.DebugEnabled ||
                !BentleyContinentalGTSpeedDiagnostics.ExitSafetyDebugEnabled)
                continue;

            var navMeshPathComplete = TryCalculateExitPath(
                marker.position, marker, agent, out var routeDetails);
            var capsuleClear = TryGetExitCapsuleClearance(
                marker.position, player!.transform, character!.capsuleCollider, out var blockers);
            BentleyContinentalGTSpeedDiagnostics.ExitSafetyInfo(context,
                $"source='{source}' vehicle={root.GetInstanceID()} marker={marker.name}, " +
                $"oldLocal={oldPosition:F3}, nativeTargetLocal={marker.localPosition:F3}, " +
                $"bodyHalfWidth={bodyBounds.extents.x:F3}, agentRadius={agent.radius:F3}, " +
                $"requiredClearance={requiredLateralDistance:F3}, capsuleClear={capsuleClear}, " +
                $"capsuleBlockers=[{blockers}], completeOutwardPath={navMeshPathComplete}, " +
                $"route={routeDetails}.");
        }
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
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy ||
                    collider.isTrigger)
                    continue;

                var halfSize = collider.size * 0.5f;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = collider.center + Vector3.Scale(
                        halfSize, new Vector3(x, y, z));
                    var rootCorner = root.InverseTransformPoint(
                        collider.transform.TransformPoint(corner));
                    if (!found)
                    {
                        bounds = new Bounds(rootCorner, Vector3.zero);
                        found = true;
                    }
                    else
                        bounds.Encapsulate(rootCorner);
                }
            }
        }

        return found;
    }

    private static float ProjectBodySizeOntoAxis(
        Vector3 bodySize, Transform root, Vector3 worldAxis)
    {
        worldAxis.Normalize();
        return Mathf.Abs(Vector3.Dot(worldAxis, root.right)) * bodySize.x +
               Mathf.Abs(Vector3.Dot(worldAxis, root.up)) * bodySize.y +
               Mathf.Abs(Vector3.Dot(worldAxis, root.forward)) * bodySize.z;
    }

    private static bool TryCalculateExitPath(
        Vector3 startPosition, Transform exitMarker, NavMeshAgent agent, out string details)
    {
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask,
        };
        if (!NavMesh.SamplePosition(startPosition, out var startHit,
                ExitNavMeshSampleTolerance, filter))
        {
            details = $"start sample missing near {startPosition:F3}";
            return false;
        }

        var side = Mathf.Sign(exitMarker.localPosition.x);
        if (Mathf.Approximately(side, 0f))
            side = -1f;
        var outward = Vector3.ProjectOnPlane(
            exitMarker.parent.TransformDirection(Vector3.right * side), Vector3.up).normalized;
        var outwardTarget = startHit.position + outward * ExitPathProbeDistance;
        if (!NavMesh.SamplePosition(outwardTarget, out var endHit,
                ExitNavMeshSampleTolerance, filter))
        {
            details = $"outward sample missing near {outwardTarget:F3}";
            return false;
        }

        var path = new NavMeshPath();
        var calculated = NavMesh.CalculatePath(startHit.position, endHit.position, filter, path);
        details = $"start={startHit.position:F3}, outward={endHit.position:F3}, " +
                  $"calculated={calculated}, status={path.status}, corners={path.corners.Length}";
        return calculated && path.status == NavMeshPathStatus.PathComplete;
    }

    private bool TryGetExitCapsuleClearance(
        Vector3 playerRootPosition, Transform playerRoot, CapsuleCollider? capsule,
        out string blockers)
    {
        blockers = "capsule-missing";
        if (capsule == null)
            return false;
        if (!TryGetCapsuleShape(playerRootPosition, playerRoot, capsule,
                out var pointA, out var pointB, out var radius))
        {
            blockers = "capsule-shape-invalid";
            return false;
        }

        var count = Physics.OverlapCapsuleNonAlloc(
            pointA, pointB, radius, exitOverlapBuffer,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        var names = new List<string>(Mathf.Min(count, exitOverlapBuffer.Length));
        for (var index = 0; index < count; index++)
        {
            var collider = exitOverlapBuffer[index];
            exitOverlapBuffer[index] = null!;
            if (collider == null || collider.transform == playerRoot ||
                collider.transform.IsChildOf(playerRoot))
                continue;
            if (collider.bounds.max.y <= playerRootPosition.y + 0.03f)
                continue;
            names.Add(collider.name + ":" + collider.GetType().Name);
        }
        blockers = names.Count == 0 ? "none" : string.Join(",", names);
        return names.Count == 0 && count < exitOverlapBuffer.Length;
    }

    private static bool TryGetCapsuleShape(
        Vector3 playerRootPosition, Transform playerRoot, CapsuleCollider capsule,
        out Vector3 pointA, out Vector3 pointB, out float radius)
    {
        pointA = pointB = default;
        radius = 0f;
        var capsuleTransform = capsule.transform;
        var scale = capsuleTransform.lossyScale;
        var axisScale = capsule.direction == 0 ? Mathf.Abs(scale.x) :
            capsule.direction == 2 ? Mathf.Abs(scale.z) : Mathf.Abs(scale.y);
        var radiusScale = capsule.direction == 0
            ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))
            : capsule.direction == 2
                ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y))
                : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        radius = capsule.radius * radiusScale;
        var height = Mathf.Max(capsule.height * axisScale, radius * 2f);
        var capsuleOffset = capsuleTransform.position - playerRoot.position +
                            capsuleTransform.rotation * Vector3.Scale(capsule.center, scale);
        var center = playerRootPosition + capsuleOffset;
        var axis = capsule.direction == 0 ? capsuleTransform.right :
            capsule.direction == 2 ? capsuleTransform.forward : capsuleTransform.up;
        var extent = Mathf.Max(0f, height * 0.5f - radius);
        pointA = center - axis * extent;
        pointB = center + axis * extent;
        return radius > 0f && axis.sqrMagnitude > 0.9f;
    }

    private void LogExitMovementState(VehicleController vehicle, string stage)
    {
        if (!BentleyContinentalGTSpeedDiagnostics.DebugEnabled ||
            !BentleyContinentalGTSpeedDiagnostics.ExitSafetyDebugEnabled)
            return;

        var player = PlayerHelper.PlayerController;
        var character = player != null ? player.Character : null;
        var agent = character != null ? character.navmeshAgent : null;
        if (player == null || character == null || agent == null)
        {
            BentleyContinentalGTSpeedDiagnostics.ExitSafetyInfo(context,
                $"stage={stage} vehicle={vehicle.GetInstanceID()} player/controller/agent missing.");
            return;
        }

        var marker = vehicle.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => string.Equals(
                transform.name, "Driverside", StringComparison.Ordinal));
        var pathDetails = "driver marker missing";
        var pathComplete = marker != null && TryCalculateExitPath(
            agent.nextPosition, marker, agent, out pathDetails);
        var capsuleClear = TryGetExitCapsuleClearance(
            player.transform.position, player.transform, character.capsuleCollider,
            out var blockers);
        var rigidbody = character.characterRigidbody;
        BentleyContinentalGTSpeedDiagnostics.ExitSafetyInfo(context,
            $"stage={stage} vehicle={vehicle.GetInstanceID()} controlled={vehicle.controlledByPlayer}, " +
            $"vehiclePosition={vehicle.transform.position:F3}, playerRoot={player.transform.position:F3}, " +
            $"characterRoot={character.transform.position:F3}, agentTransform={agent.transform.position:F3}, " +
            $"agentNextPosition={agent.nextPosition:F3}, agentEnabled={agent.isActiveAndEnabled}, " +
            $"agentOnNavMesh={(agent.isActiveAndEnabled && agent.isOnNavMesh)}, " +
            $"agentRadius={agent.radius:F3}, agentHeight={agent.height:F3}, " +
            $"agentBaseOffset={agent.baseOffset:F3}, agentType={agent.agentTypeID}, " +
            $"areaMask={agent.areaMask}, updatePosition={agent.updatePosition}, " +
            $"characterCapsule={(character.capsuleCollider != null ? character.capsuleCollider.bounds.ToString() : "missing")}, " +
            $"capsuleClear={capsuleClear}, capsuleBlockers=[{blockers}], " +
            $"rigidbodyPosition={(rigidbody != null ? rigidbody.position.ToString("F3") : "missing")}, " +
            $"rigidbodyKinematic={(rigidbody != null && rigidbody.isKinematic)}, " +
            $"rigidbodyVelocity={(rigidbody != null ? rigidbody.velocity.ToString("F3") : "missing")}, " +
            $"completeOutwardPath={pathComplete}, route={pathDetails}.");
        LogVehicleNavigationGeometry(vehicle, stage);
    }

    private void LogVehicleNavigationGeometry(VehicleController vehicle, string stage)
    {
        if (!BentleyContinentalGTSpeedDiagnostics.DebugEnabled ||
            !BentleyContinentalGTSpeedDiagnostics.ExitSafetyDebugEnabled)
            return;

        var colliderDetails = new List<string>();
        foreach (var collider in vehicle.GetComponentsInChildren<Collider>(true))
        {
            if (collider == null)
                continue;
            colliderDetails.Add(
                $"{collider.name}:{collider.GetType().Name}(enabled={collider.enabled},trigger={collider.isTrigger}," +
                $"center={collider.bounds.center:F3},size={collider.bounds.size:F3})");
        }

        var obstacleDetails = new List<string>();
        foreach (var obstacle in vehicle.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle == null)
                continue;
            var obstacleBounds = GetObstacleRootLocalBounds(vehicle.transform, obstacle);
            obstacleDetails.Add(
                $"{obstacle.name}(enabled={obstacle.enabled},carving={obstacle.carving}," +
                $"stationaryOnly={obstacle.carveOnlyStationary}," +
                $"carvingTimeToStationary={obstacle.carvingTimeToStationary:F3}," +
                $"center={obstacle.center:F3},size={obstacle.size:F3}," +
                $"localScale={obstacle.transform.localScale:F3},lossyScale={obstacle.transform.lossyScale:F3}," +
                $"rootBounds={FormatBounds(obstacleBounds)})");
        }

        var togglerDetails = vehicle.GetComponentsInChildren<Component>(true)
            .Where(component => component != null &&
                                string.Equals(component.GetType().Name,
                                    "VehicleNavMeshObstacleToggler", StringComparison.Ordinal))
            .Select(component =>
                $"{component.GetType().Name}(enabled={((Behaviour)component).isActiveAndEnabled})");
        BentleyContinentalGTSpeedDiagnostics.ExitSafetyInfo(context,
            $"stage={stage} vehicle={vehicle.GetInstanceID()} bodyColliders=[" +
            $"{string.Join(";", colliderDetails)}], obstacles=[{string.Join(";", obstacleDetails)}], " +
            $"obstacleTogglers=[{string.Join(";", togglerDetails)}].");
    }

    private static Bounds GetObstacleRootLocalBounds(Transform root, NavMeshObstacle obstacle)
    {
        var bounds = new Bounds();
        var found = false;
        var half = obstacle.size * 0.5f;
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var localPoint = obstacle.center + Vector3.Scale(
                half, new Vector3(x, y, z));
            var rootPoint = root.InverseTransformPoint(
                obstacle.transform.TransformPoint(localPoint));
            if (!found)
            {
                bounds = new Bounds(rootPoint, Vector3.zero);
                found = true;
            }
            else
                bounds.Encapsulate(rootPoint);
        }
        return bounds;
    }

    private static string FormatBounds(Bounds bounds) =>
        $"min={bounds.min:F3},max={bounds.max:F3},size={bounds.size:F3}";

    private void StopExitObservation()
    {
        if (exitObservationCoroutine != null)
            StopCoroutine(exitObservationCoroutine);
        exitObservationCoroutine = null;
    }

    private void HandleBuildingEntered(Address address) =>
        RefreshDealerStockForAddress(address, "dealer-entered");

    private void HandleBuildingRegistrationChanged(Address address) =>
        RefreshDealerStockForAddress(address, "dealer-registration-changed");

    private void HandleGameUnloaded()
    {
        StopDrivetrainReadiness();
        StopExitObservation();
        StopWarehouseExitGuard("game-unloaded");
        foreach (var audio in audioControllers)
        {
            if (audio == null)
                continue;
            audio.Shutdown();
            Destroy(audio);
        }
        audioControllers.Clear();
        foreach (var appearance in appearanceControllers)
        {
            if (appearance != null)
            {
                appearance.Cleanup();
                Destroy(appearance);
            }
        }
        appearanceControllers.Clear();
        foreach (var damage in damageControllers)
        {
            if (damage != null)
            {
                damage.Cleanup();
                Destroy(damage);
            }
        }
        damageControllers.Clear();
        foreach (var driver in driverControllers)
        {
            if (driver == null)
                continue;
            driver.Shutdown();
            Destroy(driver);
        }
        driverControllers.Clear();
        configuredVehicleIds.Clear();
        nativeRendererBindingsReadyIds.Clear();
        drivetrainReadinessHandledIds.Clear();
        dealerStockReady = false;
        dealerFailureLogged = false;
        privateDriverPoolReady = false;
        privateDriverContractReady = false;
        privateDriverRegistrationAllowed = false;
        privateDriverPoolFailureLogged = false;
        privateDriverContractFailureLogged = false;
    }

    private void RefreshDealerStockForAddress(Address address, string source)
    {
        if (address == null)
            return;

        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (BentleyContinentalGTSpeedLuxuryDealerStock.IsTargetDealer(registration?.BusinessName))
            EnsureDealerStock(source);
    }

    private bool EnsureDealerStock(string source)
    {
        if (dealerStockReady)
            return true;
        if (string.IsNullOrWhiteSpace(vehicleTypeName) || BusinessLayoutSetHelper.loadingLayouts)
            return false;

        try
        {
            dealerStockReady = BentleyContinentalGTSpeedLuxuryDealerStock.EnsureVehicleAvailable(vehicleTypeName);
            if (dealerStockReady)
            {
                dealerFailureLogged = false;
                BentleyContinentalGTSpeedDiagnostics.Info(
                    context,
                    $"BentleyContinentalGTSpeed: luxury dealer stock ready source='{source}'.");
                return true;
            }
        }
        catch (Exception exception)
        {
            if (!dealerFailureLogged)
            {
                dealerFailureLogged = true;
                context?.Logger.Warn(
                    $"BentleyContinentalGTSpeed: dealer catalog setup failed source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
            return false;
        }

        if (!dealerFailureLogged)
        {
            dealerFailureLogged = true;
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed: luxury dealer stock unavailable source='{source}'; " +
                "will retry at the next dealer or menu lifecycle event.");
        }
        return false;
    }

    private bool EnsurePrivateDriverSupport(string source)
    {
        if (!privateDriverRegistrationAllowed)
            return false;

        if (!privateDriverContractReady)
        {
            try
            {
                privateDriverContractReady =
                    BentleyContinentalGTSpeedPrivateDriverSupport.EnsureVehicleAvailable(vehicleTypeName);
            }
            catch (Exception exception)
            {
                if (!privateDriverContractFailureLogged)
                {
                    privateDriverContractFailureLogged = true;
                    context?.Logger.Warn(
                        $"BentleyContinentalGTSpeed: private-driver contract setup failed source='{source}': " +
                        $"{exception.GetType().Name}: {exception.Message}");
                }
            }
        }

        if (!privateDriverPoolReady)
            TryPreparePrivateDriverPool(source);

        return privateDriverContractReady && privateDriverPoolReady;
    }

    private bool TryPreparePrivateDriverPool(string source)
    {
        if (privateDriverPoolReady)
            return true;
        if (playerVehiclePrefab == null)
            return false;

        try
        {
            privateDriverPoolReady =
                BentleyContinentalGTSpeedPrivateDriverSupport.PrepareTrafficPool(playerVehiclePrefab);
            if (privateDriverPoolReady)
            {
                privateDriverPoolFailureLogged = false;
                BentleyContinentalGTSpeedDiagnostics.Info(
                    context,
                    $"BentleyContinentalGTSpeed: private-driver traffic pool prepared source='{source}'.");
            }
            else if (!privateDriverPoolFailureLogged)
            {
                privateDriverPoolFailureLogged = true;
                context?.Logger.Warn(
                    $"BentleyContinentalGTSpeed: private-driver traffic pool unavailable source='{source}'; " +
                    "will retry on the next scene or menu lifecycle event.");
            }
        }
        catch (Exception exception)
        {
            if (!privateDriverPoolFailureLogged)
            {
                privateDriverPoolFailureLogged = true;
                context?.Logger.Warn(
                    $"BentleyContinentalGTSpeed: private-driver traffic pool setup failed source='{source}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        return privateDriverPoolReady;
    }
}

/// <summary>Preserves existing luxury dealer stock while adding this vehicle.</summary>
internal static class BentleyContinentalGTSpeedLuxuryDealerStock
{
    private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
    private const string TargetBuildingSize = "ba:buildingsize_m";
    private const int TargetBuildingVersion = 1;
    private const string TargetLayoutName = "MurrayHillCarDealershipLuxury";

    private static readonly string[] DealerContactIds =
    {
        "The Hamptons Axis",
        "Manhattan Luxury Cars"
    };

    internal static bool IsTargetDealer(string? contactId)
    {
        if (string.IsNullOrEmpty(contactId))
            return false;

        foreach (var dealerContactId in DealerContactIds)
        {
            if (string.Equals(dealerContactId, contactId, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    internal static bool EnsureVehicleAvailable(string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return false;
        if (AllDealersContainVehicle(vehicleName))
            return true;
        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        var layoutVehicles = GetLuxuryDealerLayoutVehicles();
        if (layoutVehicles.Count == 0)
            return false;

        var allDealersReady = true;
        foreach (var contactId in DealerContactIds)
            allDealersReady &= EnsureDealerStock(contactId, layoutVehicles, vehicleName);
        return allDealersReady;
    }

    internal static void RemoveVehicle(string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return;

        foreach (var contactId in DealerContactIds)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(contactId, out List<string> stock) ||
                stock == null)
            {
                continue;
            }

            var remaining = new List<string>();
            foreach (var existing in stock)
            {
                if (!string.Equals(existing, vehicleName, StringComparison.Ordinal))
                    AddUnique(remaining, existing);
            }

            if (remaining.Count == stock.Count)
                continue;
            if (remaining.Count == 0)
                ContractItemsForSaleService.RemoveContact(contactId);
            else
                ContractItemsForSaleService.SetVehiclesForContact(contactId, remaining);
        }
    }

    private static bool EnsureDealerStock(string contactId, List<string> layoutVehicles, string vehicleName)
    {
        var merged = new List<string>();
        var hasExplicitStock = ContractItemsForSaleService.TryGetVehiclesForContact(
            contactId,
            out List<string> existing);
        if (hasExplicitStock && existing != null)
            AddUniqueRange(merged, existing);
        AddUniqueRange(merged, layoutVehicles);
        AddUnique(merged, vehicleName);

        if (hasExplicitStock && existing != null && SameVehicleList(existing, merged))
            return true;
        ContractItemsForSaleService.SetVehiclesForContact(contactId, merged);
        return true;
    }

    private static List<string> GetLuxuryDealerLayoutVehicles()
    {
        var vehicles = new List<string>();
        try
        {
            var layout = TryGetLuxuryDealerLayoutSet();
            if (layout?.Items == null)
                return vehicles;

            foreach (var item in layout.Items)
            {
                var settings = item?.playerItemPurchaserSettings;
                if (settings == null || !settings.enabled || string.IsNullOrEmpty(settings.itemName))
                    continue;

                var definition = ItemsGetter.GetByName(settings.itemName);
                if (definition != null && !string.IsNullOrEmpty(definition.vehicleType))
                    AddUnique(vehicles, definition.vehicleType);
            }
        }
        catch
        {
            // Save transitions can temporarily make the requested layout unavailable.
            // The runtime retries from scene, dealer, and menu lifecycle callbacks.
        }
        return vehicles;
    }

    private static BusinessLayoutSet? TryGetLuxuryDealerLayoutSet()
    {
        if (BusinessLayoutSetHelper.loadingLayouts)
            return null;
        return BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
            TargetBusinessTypeName,
            new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
            TargetLayoutName.ToLowerInvariant(),
            false);
    }

    private static bool AllDealersContainVehicle(string vehicleName)
    {
        foreach (var contactId in DealerContactIds)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(contactId, out List<string> stock) ||
                stock == null || !ContainsVehicle(stock, vehicleName))
            {
                return false;
            }
        }
        return true;
    }

    private static void AddUniqueRange(List<string> target, IEnumerable<string> source)
    {
        foreach (var value in source)
            AddUnique(target, value);
    }

    private static void AddUnique(List<string> target, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        foreach (var existing in target)
        {
            if (string.Equals(existing, value, StringComparison.Ordinal))
                return;
        }
        target.Add(value);
    }

    private static bool ContainsVehicle(IEnumerable<string> stock, string vehicleName)
    {
        foreach (var existing in stock)
        {
            if (string.Equals(existing, vehicleName, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool SameVehicleList(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}
