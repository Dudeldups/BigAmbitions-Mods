#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BAModAPI;
using BusinessLayoutSets;
using Helpers;
using UI.PurchaseVehicle;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class BigfootMonsterTruckRuntime : MonoBehaviour
{
    private const string VehicleRepainterColorRestoredEvent =
        "vehicle-repainter:color-restored";
    private const string DeveloperToolsColorRestoredEvent =
        "developer-tools:vehicle-recolor-restored";
    private const int InitializationRetryCount = 24;
    private const float InitializationRetryDelay = 0.25f;
    private const float VehicleMass = 6500f;
    private const float WheelRadius = 0.78f;
    private const float WheelWidth = 1.05f;
    private const float SuspensionLength = 0.65f;
    private const float SuspensionForce = 28000f;
    private const float FrontAxleZ = 1.60f;
    private const float RearAxleZ = -1.60f;
    private const float HalfTrack = 1.35f;
    private const float AxleHeight = 1.05f;
    private const float CenterOfMassHeight = 0.72f;
    private const float DriverSeatHeight = 2.02f;
    private const float BrakeTorque = 32000f;
    private const float AntiRollForce = 4200f;
    private const float TargetTopSpeed = 140f;
    private const float EngineRevLimiter = 6500f;
    private const float TransmissionFinalDrive = 13.6f;
    private static readonly float[] TransmissionGears =
    {
        -3.6f,
        0f,
        4.2f,
        2.5f,
        1.55f,
        1f,
    };

    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private Coroutine? initializationCoroutine;
    private int observedPlayerVehicleCount = -1;
    private VehicleController? previewVehicle;
    private BigfootMonsterTruckPaintController? previewPaintController;

    public static BigfootMonsterTruckRuntime Initialize(ModContext context, string vehicleTypeName)
    {
        var runtime = FindObjectOfType<BigfootMonsterTruckRuntime>();
        if (runtime == null)
        {
            var host = new GameObject(nameof(BigfootMonsterTruckRuntime));
            DontDestroyOnLoad(host);
            runtime = host.AddComponent<BigfootMonsterTruckRuntime>();
        }

        runtime.context = context;
        runtime.vehicleTypeName = vehicleTypeName ?? string.Empty;
        runtime.SubscribeEvents();
        GlobalEvents.RegisterOnGameLoadedLateCallback(runtime.HandleGameLoadedLate);
        runtime.ScheduleInitialization("mod-load");
        if (BigfootMonsterTruckDebug.Enabled)
        {
            context.Logger.Info(
                "BigfootMonsterTruck: runtime initialized with player-vehicle change detection; " +
                "recurring global scans disabled.");
        }
        return runtime;
    }

    public void Shutdown()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        foreach (var marker in FindObjectsOfType<BigfootMonsterTruckConfigured>(true))
            if (marker != null)
                Destroy(marker);
        foreach (var driver in FindObjectsOfType<BigfootMonsterTruckDriverController>(true))
            if (driver != null)
                Destroy(driver);
        foreach (var guard in FindObjectsOfType<BigfootMonsterTruckCollisionGuard>(true))
            if (guard != null)
                Destroy(guard);
        foreach (var audioController in FindObjectsOfType<BigfootMonsterTruckAudioController>(true))
            if (audioController != null)
                Destroy(audioController);
        foreach (var parkingController in FindObjectsOfType<BigfootMonsterTruckParkingController>(true))
            if (parkingController != null)
                Destroy(parkingController);
        foreach (var lightingController in FindObjectsOfType<BigfootMonsterTruckLightingController>(true))
            if (lightingController != null)
                Destroy(lightingController);
        foreach (var paintController in FindObjectsOfType<BigfootMonsterTruckPaintController>(true))
            if (paintController != null)
                Destroy(paintController);
        previewVehicle = null;
        previewPaintController = null;
        Destroy(gameObject);
    }

    private void Update()
    {
        var currentCount = VehicleHelper.AllPlayerVehicles?.Count ?? 0;
        if (currentCount != observedPlayerVehicleCount)
        {
            var configured = ConfigureExistingVehicles();
            if (configured > 0 && BigfootMonsterTruckDebug.VehicleDiscoveryEnabled)
            {
                context?.Logger.Info(
                    $"BigfootMonsterTruck: player vehicle list changed count={currentCount}, " +
                    $"configured={configured}.");
            }
        }

        RefreshPaintPreview();
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
        GlobalEvents.onNewHour -= HandleNewHour;
        GlobalEvents.onNewHour += HandleNewHour;
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
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onNewHour -= HandleNewHour;
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
        ScheduleInitialization("game-loaded-late");
    }

    private void HandleGameUnloaded()
    {
        if (initializationCoroutine != null)
        {
            StopCoroutine(initializationCoroutine);
            initializationCoroutine = null;
        }
        previewVehicle = null;
        previewPaintController = null;
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        TryConfigureVehicle(vehicle, "vehicle-entered");
        vehicle?.GetComponent<BigfootMonsterTruckParkingController>()?.HandleVehicleEntered();
    }

    private void HandleVehicleExited(VehicleController vehicle) =>
        vehicle?.GetComponent<BigfootMonsterTruckParkingController>()?.HandleVehicleExited();

    private static void HandleNewHour()
    {
        foreach (var parkingController in FindObjectsOfType<BigfootMonsterTruckParkingController>(true))
            if (parkingController != null)
                parkingController.AddSecondSpaceHourlyFee();
    }

    private void HandleBuildingEntered(Address address)
    {
        if (address == null)
            return;
        var registration = BuildingHelper.GetBuildingRegistration(address);
        if (string.Equals(
                registration?.BusinessName,
                BigfootTruckDealerStock.DealerContactId,
                StringComparison.Ordinal))
        {
            BigfootTruckDealerStock.EnsureVehicleAvailable(
                vehicleTypeName,
                context,
                "truck-dealer-entered");
        }
    }

    private void HandleFullMenuToggle(bool isOpen)
    {
        if (isOpen)
            BigfootTruckDealerStock.EnsureVehicleAvailable(vehicleTypeName, context, "menu-opened");
    }

    private void HandleVehicleVariablesChanged()
    {
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
            return;

        foreach (var vehicle in vehicles)
            if (IsTargetVehicle(vehicle))
                vehicle!.GetComponent<BigfootMonsterTruckPaintController>()
                    ?.RefreshSavedColor("vehicle-variables-changed");
    }

    private void RefreshPaintPreview()
    {
        if (!PurchaseVehicleUI.IsPanelOpen)
        {
            previewPaintController?.RefreshSavedColor("repaint-preview-closed");
            previewVehicle = null;
            previewPaintController = null;
            return;
        }

        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!ReferenceEquals(selectedVehicle, previewVehicle))
        {
            previewVehicle = selectedVehicle;
            previewPaintController = IsTargetVehicle(selectedVehicle)
                ? selectedVehicle!.GetComponent<BigfootMonsterTruckPaintController>()
                : null;
        }

        previewPaintController?.RefreshPreviewColor();
    }

    private void HandleGameEvent(string eventName)
    {
        if (string.Equals(eventName, VehicleRepainterColorRestoredEvent, StringComparison.Ordinal) ||
            string.Equals(eventName, DeveloperToolsColorRestoredEvent, StringComparison.Ordinal))
        {
            RefreshExistingVehiclePaint($"paint-restored:{eventName}");
            return;
        }

        if (!PurchaseVehicleUI.IsPanelOpen)
            return;

        var selectedVehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (!IsTargetVehicle(selectedVehicle))
            return;

        selectedVehicle!
            .GetComponent<BigfootMonsterTruckPaintController>()
            ?.RefreshPreviewColor();
    }

    private void RefreshExistingVehiclePaint(string source)
    {
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
            return;

        var restored = 0;
        foreach (var vehicle in vehicles)
        {
            if (!IsTargetVehicle(vehicle))
                continue;

            if (vehicle!.GetComponent<BigfootMonsterTruckPaintController>()
                    ?.RefreshSavedColor(source, true) == true)
            {
                restored++;
            }
        }

        if (restored > 0)
        {
            context?.Logger.Info(
                $"BigfootMonsterTruck paint: restored {restored} truck(s) after '{source}'.");
        }
    }

    private void ScheduleInitialization(string source)
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(InitializeForLifecycle(source));
    }

    private IEnumerator InitializeForLifecycle(string source)
    {
        while (BusinessLayoutSetHelper.loadingLayouts)
        {
            ConfigureExistingVehicles();
            yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        var dealerReady = false;
        for (var attempt = 1; attempt <= InitializationRetryCount; attempt++)
        {
            dealerReady = BigfootTruckDealerStock.EnsureVehicleAvailable(
                vehicleTypeName,
                context,
                source);
            ConfigureExistingVehicles();
            if (dealerReady)
                break;
            yield return new WaitForSecondsRealtime(InitializationRetryDelay);
        }

        initializationCoroutine = null;
        if (!dealerReady)
        {
            context?.Logger.Warn(
                $"BigfootMonsterTruck: truck dealer data was not ready source='{source}'.");
        }
    }

    private int ConfigureExistingVehicles()
    {
        var configured = 0;
        var vehicles = VehicleHelper.AllPlayerVehicles;
        if (vehicles == null)
        {
            observedPlayerVehicleCount = 0;
            return configured;
        }
        observedPlayerVehicleCount = vehicles.Count;
        foreach (var vehicle in vehicles)
            if (TryConfigureVehicle(vehicle, "existing-vehicle"))
                configured++;
        return configured;
    }

    private bool TryConfigureVehicle(VehicleController? vehicle, string source)
    {
        if (!IsTargetVehicle(vehicle))
            return false;
        var targetVehicle = vehicle!;
        var paint = targetVehicle.GetComponent<BigfootMonsterTruckPaintController>();
        if (paint == null)
        {
            paint = targetVehicle.gameObject.AddComponent<BigfootMonsterTruckPaintController>();
            paint.Initialize(targetVehicle, context);
        }
        else
        {
            paint.RefreshSavedColor(source);
        }
        if (targetVehicle.GetComponent<BigfootMonsterTruckConfigured>() != null)
            return false;

        var rigidbody = targetVehicle.GetComponent<Rigidbody>() ?? targetVehicle.GetComponentInParent<Rigidbody>();
        if (rigidbody != null)
        {
            rigidbody.mass = VehicleMass;
            rigidbody.centerOfMass = new Vector3(0f, CenterOfMassHeight, 0f);
            rigidbody.drag = 0.02f;
            rigidbody.angularDrag = 0.12f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }

        var contactMaterial = new PhysicMaterial("Bigfoot low-friction contact")
        {
            dynamicFriction = 0.05f,
            staticFriction = 0.05f,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounciness = 0f,
            bounceCombine = PhysicMaterialCombine.Minimum,
        };
        var wheelCount = ConfigureWheels(targetVehicle);
        ConfigureBodyColliders(targetVehicle, contactMaterial);
        ConfigureWheelContactColliders(targetVehicle, contactMaterial);
        ConfigureVehicleModules(targetVehicle);
        ConfigureExitMarkers(targetVehicle);
        ConfigureDriverSeat(targetVehicle);
        var materialFix = BigfootMonsterTruckMaterials.FixSolidMaterials(targetVehicle.gameObject);
        if (materialFix.MaterialsValidated < materialFix.OpaqueMaterialsFixed)
            context?.Logger.Warn(
                $"BigfootMonsterTruck materials vehicle={targetVehicle.GetInstanceID()}: " +
                "HDRP validation was unavailable for one or more opaque materials.");

        var driver = targetVehicle.gameObject.AddComponent<BigfootMonsterTruckDriverController>();
        driver.Initialize(targetVehicle, context);
        var collisionGuard = targetVehicle.gameObject.AddComponent<BigfootMonsterTruckCollisionGuard>();
        collisionGuard.Initialize(targetVehicle, context);
        var audioController = targetVehicle.gameObject.AddComponent<BigfootMonsterTruckAudioController>();
        audioController.Initialize(targetVehicle, context);
        var parkingController = targetVehicle.gameObject.AddComponent<BigfootMonsterTruckParkingController>();
        parkingController.Initialize(targetVehicle, context);
        var lightingController = targetVehicle.gameObject.AddComponent<BigfootMonsterTruckLightingController>();
        lightingController.Initialize(targetVehicle, context);
        var marker = targetVehicle.gameObject.AddComponent<BigfootMonsterTruckConfigured>();
        marker.Initialize(contactMaterial);
        if (wheelCount != 4)
            context?.Logger.Warn(
                $"BigfootMonsterTruck: expected four wheel controllers but configured {wheelCount}.");
        return true;
    }

    private bool IsTargetVehicle(VehicleController? vehicle) =>
        vehicle?.vehicleInstance != null &&
        string.Equals(vehicle.vehicleInstance.vehicleTypeName, vehicleTypeName, StringComparison.Ordinal);

    private int ConfigureWheels(VehicleController vehicle)
    {
        var count = 0;
        foreach (var transform in vehicle.GetComponentsInChildren<Transform>(true))
        {
            if (!TryGetWheelPosition(transform.name, out var position))
                continue;
            transform.localPosition = position;
            count++;

            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                var spring = GetMember(component, "spring");
                SetFloat(spring, "maxLength", SuspensionLength);
                SetFloat(spring, "maxForce", SuspensionForce);
                SetMember(component, "spring", spring);

                var damper = GetMember(component, "damper");
                SetFloat(damper, "bumpRate", 22000f);
                SetFloat(damper, "reboundRate", 26000f);
                SetMember(component, "damper", damper);

                var wheel = GetMember(component, "wheel");
                SetFloat(wheel, "radius", WheelRadius);
                SetFloat(wheel, "width", WheelWidth);
                SetFloat(wheel, "mass", 120f);
                SetMember(component, "wheel", wheel);

                var sideFriction = GetMember(component, "sideFriction");
                SetFloat(sideFriction, "grip", 1.0f);
                SetFloat(sideFriction, "stiffness", 1.12f);
                SetMember(component, "sideFriction", sideFriction);

                var forwardFriction = GetMember(component, "forwardFriction");
                SetFloat(forwardFriction, "grip", 1.2f);
                SetFloat(forwardFriction, "stiffness", 1.15f);
                SetMember(component, "forwardFriction", forwardFriction);

                SetFloat(component, "loadRating", 30000f);
                SetFloat(component, "rollingResistanceTorque", 90f);
                SetFloat(component, "forceApplicationPointDistance", 0.5f);
                // Wheel raycasts against traffic vehicles made their rigidbodies latch
                // together and drag the truck. Body colliders handle vehicle impacts.
                SetFloat(component, "otherBodyForceScale", 1f);
            }
        }

        return count;
    }

    private static bool TryGetWheelPosition(string name, out Vector3 position)
    {
        if (!name.EndsWith("_WheelController", StringComparison.Ordinal))
        {
            position = default;
            return false;
        }

        if (name.IndexOf("FrontLeft", StringComparison.OrdinalIgnoreCase) >= 0)
            position = new Vector3(-HalfTrack, AxleHeight, FrontAxleZ);
        else if (name.IndexOf("FrontRight", StringComparison.OrdinalIgnoreCase) >= 0)
            position = new Vector3(HalfTrack, AxleHeight, FrontAxleZ);
        else if (name.IndexOf("RearLeft", StringComparison.OrdinalIgnoreCase) >= 0)
            position = new Vector3(-HalfTrack, AxleHeight, RearAxleZ);
        else if (name.IndexOf("RearRight", StringComparison.OrdinalIgnoreCase) >= 0)
            position = new Vector3(HalfTrack, AxleHeight, RearAxleZ);
        else
        {
            position = default;
            return false;
        }
        return true;
    }

    private static int ConfigureBodyColliders(VehicleController vehicle, PhysicMaterial contactMaterial)
    {
        var count = 0;
        foreach (var transform in vehicle.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BodyCollider", StringComparison.Ordinal))
                continue;
            var colliders = transform.GetComponents<BoxCollider>();
            for (var index = 0; index < colliders.Length; index++)
            {
                var collider = colliders[index];
                if (index == 0)
                {
                    collider.center = new Vector3(0f, 1.2f, -0.05f);
                    collider.size = new Vector3(2.5f, 0.55f, 5.3f);
                }
                else
                {
                    collider.center = new Vector3(0f, 2.05f, 0.1f);
                    collider.size = new Vector3(2.2f, 1.2f, 3.7f);
                }
                collider.sharedMaterial = contactMaterial;
                count++;
            }
        }
        return count;
    }

    private static int ConfigureWheelContactColliders(
        VehicleController vehicle,
        PhysicMaterial contactMaterial)
    {
        var holder = vehicle.transform.Find("BigfootWheelContactColliders");
        if (holder == null)
        {
            var holderObject = new GameObject("BigfootWheelContactColliders")
            {
                layer = 12,
                tag = "Wheel",
            };
            holder = holderObject.transform;
            holder.SetParent(vehicle.transform, false);
        }

        foreach (var box in holder.GetComponents<BoxCollider>())
        {
            box.enabled = false;
            Destroy(box);
        }

        var colliders = holder.GetComponents<SphereCollider>();
        while (colliders.Length < 4)
        {
            holder.gameObject.AddComponent<SphereCollider>();
            colliders = holder.GetComponents<SphereCollider>();
        }

        var centers = new[]
        {
            new Vector3(-HalfTrack, 0.58f, FrontAxleZ),
            new Vector3(HalfTrack, 0.58f, FrontAxleZ),
            new Vector3(-HalfTrack, 0.58f, RearAxleZ),
            new Vector3(HalfTrack, 0.58f, RearAxleZ),
        };
        for (var index = 0; index < colliders.Length; index++)
        {
            colliders[index].enabled = index < centers.Length;
            if (index >= centers.Length)
                continue;
            colliders[index].isTrigger = false;
            colliders[index].center = centers[index];
            colliders[index].radius = 0.58f;
            colliders[index].sharedMaterial = contactMaterial;
        }
        return centers.Length;
    }

    private static void ConfigureExitMarkers(VehicleController vehicle)
    {
        foreach (var transform in vehicle.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.name, "Driverside", StringComparison.Ordinal))
                transform.localPosition = new Vector3(-2.05f, 0.1f, 0.1f);
            else if (string.Equals(transform.name, "Passengerside", StringComparison.Ordinal))
                transform.localPosition = new Vector3(2.05f, 0.1f, 0.1f);
        }
    }

    private static void ConfigureDriverSeat(VehicleController vehicle)
    {
        foreach (var transform in vehicle.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "BigfootDriverSeat", StringComparison.Ordinal))
                continue;
            transform.localPosition = new Vector3(0f, DriverSeatHeight, 0.18f);
            return;
        }
    }

    private void ConfigureVehicleModules(VehicleController vehicle)
    {
        foreach (var component in vehicle.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            try
            {
                var type = component.GetType();
                if (string.Equals(type.FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
                {
                    var powertrain = GetMember(component, "powertrain");
                    var engine = GetMember(powertrain, "engine");
                    SetFloat(engine, "maxPower", vehicle.vehicleType?.enginePower ?? 1200f);
                    SetFloat(engine, "revLimiterRPM", EngineRevLimiter);
                    SetMember(powertrain, "engine", engine);
                    var transmission = GetMember(powertrain, "transmission");
                    SetFloat(transmission, "finalGearRatio", TransmissionFinalDrive);
                    SetFloat(transmission, "_upshiftRPM", 5900f);
                    SetFloat(transmission, "_downshiftRPM", 2700f);
                    SetInt(transmission, "forwardGearCount", 4);
                    SetInt(transmission, "reverseGearCount", 1);
                    if (GetMember(transmission, "gears") is IList gears)
                    {
                        gears.Clear();
                        foreach (var ratio in TransmissionGears)
                            gears.Add(ratio);
                        SetMember(transmission, "gears", gears);
                    }
                    SetMember(powertrain, "transmission", transmission);
                    var differentials = GetMember(powertrain, "differentials") as IList;
                    if (differentials != null)
                    {
                        for (var index = 0; index < differentials.Count; index++)
                        {
                            var differential = differentials[index];
                            SetFloat(differential, "slipTorque", 1000f);
                            if (differential != null)
                                differentials[index] = differential;
                        }
                        SetMember(powertrain, "differentials", differentials);
                    }
                    SetMember(component, "powertrain", powertrain);

                    var steering = GetMember(component, "steering");
                    SetFloat(steering, "maximumSteerAngle", 28f);
                    SetMember(component, "steering", steering);
                }
                else if (string.Equals(type.Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
                {
                    var speedLimiter = GetMember(component, "module");
                    SetFloat(speedLimiter, "speedLimit", TargetTopSpeed);
                    SetMember(component, "module", speedLimiter);
                }

                var brakes = GetMember(component, "brakes");
                SetFloat(brakes, "maxTorque", BrakeTorque);
                SetFloat(brakes, "actuationTime", 0.1f);
                SetMember(component, "brakes", brakes);
                SetFloat(component, "baseMass", VehicleMass);

                var wheelGroups = GetMember(component, "wheelGroups") as IList;
                if (wheelGroups != null)
                {
                    for (var index = 0; index < wheelGroups.Count; index++)
                    {
                        var group = wheelGroups[index];
                        SetFloat(group, "antiRollBarForce", AntiRollForce);
                        SetFloat(group, "brakeCoefficient", index == 0 ? 1f : 0.85f);
                        if (index > 0)
                            SetFloat(group, "handbrakeCoefficient", 1.8f);
                        if (group != null && group.GetType().IsValueType)
                            wheelGroups[index] = group;
                    }
                    SetMember(component, "wheelGroups", wheelGroups);
                }
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"BigfootMonsterTruck: module tuning failed for '{component.GetType().Name}': " +
                    exception.Message);
            }
        }
    }

    private static object? GetMember(object? target, string name)
    {
        if (target == null)
            return null;
        var type = target.GetType();
        return FindField(type, name)?.GetValue(target) ??
               type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                   ?.GetValue(target);
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
        var property = target.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
            property.SetValue(target, value);
    }

    private static bool SetFloat(object? target, string name, float value)
    {
        if (target == null)
            return false;
        var field = FindField(target.GetType(), name);
        if (field?.FieldType == typeof(float))
        {
            field.SetValue(target, value);
            return true;
        }
        var property = target.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType == typeof(float))
        {
            property.SetValue(target, value);
            return true;
        }
        return false;
    }

    private static bool SetInt(object? target, string name, int value)
    {
        if (target == null)
            return false;
        var field = FindField(target.GetType(), name);
        if (field?.FieldType == typeof(int))
        {
            field.SetValue(target, value);
            return true;
        }
        var property = target.GetType().GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType == typeof(int))
        {
            property.SetValue(target, value);
            return true;
        }
        return false;
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
}

internal static class BigfootMonsterTruckDebug
{
    internal static readonly bool Enabled = false;
    internal static readonly bool VehicleDiscovery = false;
    internal static bool VehicleDiscoveryEnabled => Enabled && VehicleDiscovery;
}

internal sealed class BigfootMonsterTruckConfigured : MonoBehaviour
{
    private PhysicMaterial? ownedContactMaterial;

    public void Initialize(PhysicMaterial contactMaterial) => ownedContactMaterial = contactMaterial;

    private void OnDestroy()
    {
        if (ownedContactMaterial != null)
            Destroy(ownedContactMaterial);
        ownedContactMaterial = null;
    }
}
