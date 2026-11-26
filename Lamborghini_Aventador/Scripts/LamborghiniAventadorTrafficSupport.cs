#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.SaveSystem;
using Buildings.BuildingTypes.Special.PrivateDriverService;
using GleyTrafficSystem;
using Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(LamborghiniAventadorTrafficLifecycle))]

/// <summary>Owns the Aventador's native Gley traffic type and chauffeur eligibility.</summary>
[ModEntryOnInitializationLoad]
public sealed class LamborghiniAventadorTrafficLifecycle : IModBigAmbitions
{
    private ModContext? context;
    private GameObject? playerPrefab;
    private bool active;
    private LamborghiniAventadorTrafficInitializationPatch? initializationPatch;

    // The primary vehicle entrypoint already declares this bundle. Reuse its
    // loaded bundle instead of asking the loader to load the same bundle twice.
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        context = modContext;
        active = true;
        var bundle = AssetService.GetBundle(modContext.ModId, LamborghiniAventadorMod.BundleKey);
        playerPrefab = bundle?.LoadAsset<GameObject>(LamborghiniAventadorMod.VehiclePrefabPath);
        if (playerPrefab == null)
        {
            modContext.Logger.Warn("LamborghiniAventador: traffic support could not load the native player prefab.");
            return Task.CompletedTask;
        }

        LamborghiniAventadorTrafficSupport.SetContext(modContext, playerPrefab);
        initializationPatch = LamborghiniAventadorTrafficInitializationPatch.Install(modContext);
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        LamborghiniAventadorTrafficSupport.RegisterLateCallback();

        var scene = SceneManager.GetActiveScene();
        if (string.Equals(scene.name, "MainScene", StringComparison.Ordinal))
            PreparePool("already-in-main-scene");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        active = false;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        initializationPatch?.Shutdown();
        initializationPatch = null;
        LamborghiniAventadorTrafficSupport.RemoveVehicle(LamborghiniAventadorMod.VehicleTypeName);
        playerPrefab = null;
        context = null;
        return Task.CompletedTask;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (active && string.Equals(scene.name, "MainScene", StringComparison.Ordinal))
            PreparePool("main-scene-loaded");
    }

    private void PreparePool(string source)
    {
        if (context != null && playerPrefab != null)
            LamborghiniAventadorTrafficSupport.PrepareTrafficPool(playerPrefab, source);
    }
}

internal static class LamborghiniAventadorTrafficSupport
{
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/lamborghiniaventador.prefab";
    private const string AiCarTypeName = "LamborghiniAventadorPrivateDriver";
    private const int PrivateDriverPoolSize = 2;
    private const string BodyName = "AventadorBody";
    private const string ColliderName = "BodyCollider";
    private const string SeatAnchorName = "DriverSeatAnchor";
    private static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" };
    private static readonly string[] CaliperNames = { "Caliper_FL", "Caliper_FR", "Caliper_BL", "Caliper_BR" };
    private static readonly string[] Corners = { "FL", "FR", "BL", "BR" };
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo? PrefabCacheField = typeof(PrefabHelper).GetField(
        "PrefabCache", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo? VehicleCollidersField = typeof(VehicleComponent).GetField(
        "allColliders", PrivateInstance);
    private static readonly FieldInfo? TrafficVehiclesField = typeof(TrafficManager).GetField(
        "trafficVehicles", PrivateInstance);
    private static readonly FieldInfo? IdleVehiclesField = typeof(TrafficVehicles).GetField(
        "idleVehicles", PrivateInstance);
    private static readonly List<PrivateDriverContract> ModifiedContracts = new(2);
    private static readonly List<OwnedPoolEntry> OwnedPoolEntries = new(2);
    private static GameObject? customAiPrefab;
    private static UnityEngine.Object? previousCachedPrefab;
    private static bool previousCacheCaptured;
    private static VehiclePool? modifiedVehiclePool;
    private static CarType? customCarType;
    private static bool ownsPoolEntry;
    private static bool lateCallbackRegistered;
    private static bool active;
    private static ModContext? context;
    private static GameObject? playerPrefab;
    private static VehiclePool? reportedMissingLivePool;
    private static string? lastFailure;
#if UNITY_EDITOR
    internal static Func<GameObject?>? AiTemplateFactoryForTests;
#endif

    private readonly struct OwnedPoolEntry
    {
        internal readonly VehiclePool Pool;
        internal readonly CarType CarType;

        internal OwnedPoolEntry(VehiclePool pool, CarType carType)
        {
            Pool = pool;
            CarType = carType;
        }
    }

    // This is a feature-specific diagnostic gate; both it and the mod-wide
    // LamborghiniAventadorDiagnostics.DebugEnabled default to false.
    internal static bool TrafficDebugEnabled { get; set; } = false;

    /// <summary>Context is exposed only while this mod's traffic lifecycle is active.</summary>
    internal static ModContext? CurrentContext => active ? context : null;
    internal static GameObject? PlayerPrefab => active ? playerPrefab : null;

    internal static void SetContext(ModContext modContext, GameObject targetPlayerPrefab)
    {
        context = modContext;
        playerPrefab = targetPlayerPrefab;
        active = true;

        var marker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "Lamborghini_Aventador", "Config", "traffic-diagnostics.enabled");
        TrafficDebugEnabled = File.Exists(marker);
        if (TrafficDebugEnabled)
        {
            LamborghiniAventadorDiagnostics.DebugEnabled = true;
            context.Logger.Info("LamborghiniAventador traffic diagnostics enabled.");
        }
    }

    internal static void RegisterLateCallback()
    {
        if (lateCallbackRegistered)
            return;
        GlobalEvents.RegisterOnGameLoadedLateCallback(HandleGameLoadedLate);
        lateCallbackRegistered = true;
    }

    internal static bool PrepareTrafficPool(GameObject playerPrefab, string source)
    {
        var trafficComponent = TrafficComponent.Instance;
        var pool = trafficComponent?.vehiclePool;
        if (trafficComponent == null || pool == null)
            return Fail("native traffic component or vehicle pool unavailable", source);
        return PrepareTrafficPool(playerPrefab, pool, source);
    }

    internal static bool PrepareTrafficPool(GameObject playerPrefab, VehiclePool pool, string source)
    {
        if (playerPrefab == null || pool == null)
            return Fail("player prefab or native vehicle pool unavailable", source);
        if (modifiedVehiclePool != null && !ReferenceEquals(modifiedVehiclePool, pool))
        {
            // A save transition can replace TrafficComponent and its pool while
            // this mod instance remains loaded. Do not treat the old pool's
            // entry as eligibility for the newly loaded save.
            modifiedVehiclePool = null;
            customCarType = null;
            ownsPoolEntry = false;
            reportedMissingLivePool = null;
        }
        if (!EnsureAiPrefab(playerPrefab) || customAiPrefab == null)
            return false;

        var existing = pool.trafficCars ?? Array.Empty<CarType>();
        foreach (var carType in existing)
        {
            if (carType == null || !string.Equals(carType.name, AiCarTypeName, StringComparison.Ordinal))
                continue;

            if (!ReferenceEquals(carType, customCarType) && !ReferenceEquals(carType.vehiclePrefab, customAiPrefab))
                return Fail($"unique traffic identity collision for '{AiCarTypeName}'", source);

            ConfigureCarType(carType);
            modifiedVehiclePool = pool;
            customCarType = carType;
            ownsPoolEntry = true;
            TrackOwnedPoolEntry(pool, carType);
            lastFailure = null;
            Trace(source, "existing-owned-entry-ready");
            return true;
        }

        if (TrafficManager.IsInitialized)
        {
            if (!ReferenceEquals(reportedMissingLivePool, pool))
            {
                reportedMissingLivePool = pool;
                context?.Logger.Warn($"LamborghiniAventador: cannot add '{AiCarTypeName}' after native traffic initialization; reload the save to prepare its pool.");
            }
            return false;
        }

        customCarType = new CarType();
        ConfigureCarType(customCarType);
        var expanded = new CarType[existing.Length + 1];
        Array.Copy(existing, expanded, existing.Length);
        expanded[existing.Length] = customCarType;
        pool.trafficCars = expanded;
        modifiedVehiclePool = pool;
        ownsPoolEntry = true;
        TrackOwnedPoolEntry(pool, customCarType);
        lastFailure = null;
        Trace(source, "appended-before-native-initialization");
        return true;
    }

    private static void TrackOwnedPoolEntry(VehiclePool pool, CarType carType)
    {
        foreach (var ownedEntry in OwnedPoolEntries)
            if (ReferenceEquals(ownedEntry.Pool, pool) && ReferenceEquals(ownedEntry.CarType, carType))
                return;

        OwnedPoolEntries.Add(new OwnedPoolEntry(pool, carType));
    }

    internal static void RemoveVehicle(string vehicleTypeName)
    {
        active = false;
        foreach (var contract in ModifiedContracts)
            RemoveAll(contract.usableVehicleTypes, vehicleTypeName);
        ModifiedContracts.Clear();

        foreach (var ownedEntry in OwnedPoolEntries)
        {
            var pool = ownedEntry.Pool;
            if (pool == null) continue;
            var current = pool.trafficCars ?? Array.Empty<CarType>();
            var retained = new List<CarType>(current.Length);
            foreach (var carType in current)
                if (!ReferenceEquals(carType, ownedEntry.CarType)) retained.Add(carType);
            if (retained.Count != current.Length)
                pool.trafficCars = retained.ToArray();
        }
        OwnedPoolEntries.Clear();
        modifiedVehiclePool = null;
        customCarType = null;
        ownsPoolEntry = false;
        reportedMissingLivePool = null;

        var cache = GetPrefabCache();
        if (cache != null && customAiPrefab != null && cache.Contains(AiPrefabCacheKey) &&
            ReferenceEquals(cache[AiPrefabCacheKey], customAiPrefab))
        {
            if (previousCachedPrefab != null) cache[AiPrefabCacheKey] = previousCachedPrefab;
            else cache.Remove(AiPrefabCacheKey);
        }

        if (customAiPrefab != null)
            UnityEngine.Object.Destroy(customAiPrefab);
        customAiPrefab = null;
        previousCachedPrefab = null;
        previousCacheCaptured = false;
        context = null;
        playerPrefab = null;
        lastFailure = null;
    }

    private static void HandleGameLoadedLate()
    {
        var livePool = TrafficComponent.Instance?.vehiclePool;
        // TrafficManager builds its vehicle list during Initialize. Keep this
        // post-load callback for save-backed contracts after the init prefix has
        // registered and verified this pool's owned entry.
        if (!active || customCarType == null || !ownsPoolEntry ||
            !ReferenceEquals(livePool, modifiedVehiclePool) ||
            !ContainsOwnedCarType(livePool, customCarType))
            return;
        if (EnsureVehicleAvailable(LamborghiniAventadorMod.VehicleTypeName))
            Trace("game-loaded-late", "advanced-and-premium-contracts-ready");
        else
            context?.Logger.Warn("LamborghiniAventador: traffic is prepared, but Private Driver contract eligibility is unavailable for this save load.");
    }

    private static bool ContainsOwnedCarType(VehiclePool? pool, CarType target)
    {
        if (pool?.trafficCars == null)
            return false;
        foreach (var carType in pool.trafficCars)
            if (ReferenceEquals(carType, target)) return true;
        return false;
    }

    private static bool EnsureVehicleAvailable(string vehicleTypeName)
    {
        var contracts = PrivateDriverHelpers.GetContracts();
        if (contracts == null || !contracts.TryGetValue(AdvancedContractKey, out var advanced) ||
            !contracts.TryGetValue(PremiumContractKey, out var premium) || advanced == null || premium == null)
            return false;

        EnsureContractContains(advanced, vehicleTypeName);
        EnsureContractContains(premium, vehicleTypeName);
        return Contains(advanced.usableVehicleTypes, vehicleTypeName) &&
               Contains(premium.usableVehicleTypes, vehicleTypeName);
    }

    private static void EnsureContractContains(PrivateDriverContract contract, string vehicleTypeName)
    {
        contract.usableVehicleTypes ??= new List<string>();
        if (Contains(contract.usableVehicleTypes, vehicleTypeName))
            return;
        contract.usableVehicleTypes.Add(vehicleTypeName);
        if (!ModifiedContracts.Contains(contract)) ModifiedContracts.Add(contract);
    }

    private static bool EnsureAiPrefab(GameObject playerPrefab)
    {
        if (customAiPrefab == null)
        {
            customAiPrefab = CreateAiPrefab(playerPrefab);
            if (customAiPrefab == null)
                return false;
        }

        var cache = GetPrefabCache();
        if (cache == null)
            return Fail("native prefab cache unavailable", "ai-prefab");
        if (!previousCacheCaptured)
        {
            previousCachedPrefab = cache.Contains(AiPrefabCacheKey)
                ? cache[AiPrefabCacheKey] as UnityEngine.Object
                : null;
            previousCacheCaptured = true;
        }
        cache[AiPrefabCacheKey] = customAiPrefab;
        return true;
    }

    private static GameObject? CreateAiPrefab(GameObject playerPrefab)
    {
        var body = FindTransform(playerPrefab.transform, BodyName);
        var bodyCollider = FindTransform(playerPrefab.transform, ColliderName);
        var seatAnchor = FindTransform(playerPrefab.transform, SeatAnchorName);
        var steeringWheelAnchor = FindTransform(playerPrefab.transform, "Steering_wheel");
        if (body == null || bodyCollider == null || seatAnchor == null || steeringWheelAnchor == null)
            return FailPrefab($"required prefab anchors missing body={body != null} collider={bodyCollider != null} seat={seatAnchor != null} steeringWheel={steeringWheelAnchor != null}; expected '{BodyName}', '{ColliderName}', '{SeatAnchorName}', and 'Steering_wheel'");

        var wheelVisuals = new Transform[4];
        var calipers = new Transform[4];
        for (var index = 0; index < 4; index++)
        {
            wheelVisuals[index] = FindTransform(playerPrefab.transform, WheelNames[index])!;
            calipers[index] = FindTransform(playerPrefab.transform, CaliperNames[index])!;
            if (wheelVisuals[index] == null || calipers[index] == null)
                return FailPrefab($"required wheel or caliper anchor missing corner={Corners[index]} wheel={wheelVisuals[index] != null} caliper={calipers[index] != null}");
        }

        var template = LoadAiTemplate();
        if (template == null)
            return FailPrefab("native AnselmoAF90 traffic template unavailable");

        var wasActive = template.activeSelf;
        GameObject clone;
        try
        {
            template.SetActive(false);
            clone = UnityEngine.Object.Instantiate(template);
        }
        finally
        {
            template.SetActive(wasActive);
        }

        clone.name = AiCarTypeName;
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        if (Application.isPlaying)
            UnityEngine.Object.DontDestroyOnLoad(clone);
        LamborghiniAventadorAmbientPaint.Initialize(clone, CurrentContext);
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        var visuals = new List<Transform>(9);
        visuals.Add(CloneVisual(body, clone.transform, clone.layer).transform);
        var wheelClones = new Transform[4];
        for (var index = 0; index < 4; index++)
        {
            wheelClones[index] = CloneVisual(wheelVisuals[index], clone.transform, clone.layer).transform;
            var caliperClone = CloneVisual(calipers[index], clone.transform, clone.layer).transform;
            visuals.Add(wheelClones[index]);
            visuals.Add(caliperClone);
        }

        var trafficVehicle = clone.GetComponent<VehicleComponent>();
        if (trafficVehicle == null || trafficVehicle.allWheels == null || trafficVehicle.allWheels.Length != 4)
        {
            UnityEngine.Object.Destroy(clone);
            return FailPrefab("native Gley vehicle component does not expose exactly four wheels");
        }

        if (!BindNativeWheels(clone.transform, trafficVehicle, wheelClones))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }

        if (!CopyBodyColliders(playerPrefab.transform, bodyCollider, clone))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }

        CopyAnchor(playerPrefab.transform, clone.transform, seatAnchor, "NpcDriverSeatAnchor");
        CopyAnchor(playerPrefab.transform, clone.transform, steeringWheelAnchor, "Steering_wheel");

        var gley = clone.GetComponent<VehicleComponent>();
        SetMember(gley, "prefab", clone);
        clone.AddComponent<LamborghiniAventadorTrafficCloneInitializer>();
        Trace("create-ai-prefab", $"visualRoots={visuals.Count} nativeWheels={trafficVehicle.allWheels.Length} bodyColliderBoxes={clone.GetComponentsInChildren<BoxCollider>(true).Length}.");
        return clone;
    }

    private static Transform CopyAnchor(Transform sourceRoot, Transform destinationRoot,
        Transform source, string destinationName)
    {
        var anchor = new GameObject(destinationName).transform;
        anchor.SetParent(destinationRoot, false);
        anchor.localPosition = sourceRoot.InverseTransformPoint(source.position);
        anchor.localRotation = Quaternion.Inverse(sourceRoot.rotation) * source.rotation;
        anchor.localScale = source.lossyScale;
        return anchor;
    }

    private static bool BindNativeWheels(
        Transform root,
        VehicleComponent vehicle,
        Transform[] wheelVisuals)
    {
        var plans = new List<WheelPlan>(4);
        var used = new HashSet<int>();
        foreach (var wheel in vehicle.allWheels)
        {
            if (wheel == null || wheel.wheelGraphics == null || wheel.wheelTransform == null ||
                (wheel.wheelPosition != Wheel.WheelPosition.Front && wheel.wheelPosition != Wheel.WheelPosition.Back))
                return Fail("native Gley wheel transforms or axle unavailable", "wheel-binding");

            var center = root.InverseTransformPoint(wheel.wheelGraphics.position);
            var front = wheel.wheelPosition == Wheel.WheelPosition.Front;
            var bestIndex = -1;
            var bestDistance = float.PositiveInfinity;
            for (var index = 0; index < 4; index++)
            {
                if (used.Contains(index) || (index < 2) != front)
                    continue;
                var visualCenter = CalculateBounds(root, wheelVisuals[index], out var bounds)
                    ? bounds.center : Vector3.zero;
                if (!CalculateBounds(root, wheelVisuals[index], out bounds))
                    continue;
                var distance = Mathf.Abs(center.x - visualCenter.x) + Mathf.Abs(center.z - visualCenter.z);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }
            if (bestIndex < 0 || !CalculateBounds(root, wheelVisuals[bestIndex], out var wheelBounds))
                return Fail("could not map a target wheel visual to a native Gley axle", "wheel-binding");
            var radius = wheelBounds.extents.y;
            if (radius < 0.22f || radius > 0.55f)
                return Fail($"measured tire radius {radius:0.000} is outside the plausible range", "wheel-binding");
            used.Add(bestIndex);
            plans.Add(new WheelPlan(wheel, wheelVisuals[bestIndex], bestIndex, wheelBounds.center, radius));
        }
        if (used.Count != 4 || plans.Count != 4)
            return Fail("native Gley wheel mapping did not bind four distinct corners", "wheel-binding");

        var frontAxles = new List<float>(2);
        var rearAxles = new List<float>(2);
        foreach (var plan in plans)
        {
            var hub = new GameObject("AventadorTrafficHub_" + Corners[plan.Index]).transform;
            hub.SetParent(root, false);
            hub.localPosition = plan.Center;
            plan.Visual.SetParent(hub, true);
            plan.Wheel.wheelGraphics = hub;
            var rayOrigin = root.InverseTransformPoint(plan.Wheel.wheelTransform.position);
            rayOrigin.x = plan.Center.x;
            rayOrigin.z = plan.Center.z;
            plan.Wheel.wheelTransform.position = root.TransformPoint(rayOrigin);
            plan.Wheel.wheelRadius = plan.Radius;
            plan.Wheel.wheelCircumference = 2f * Mathf.PI * plan.Radius;
            plan.Wheel.raycastLength = plan.Radius + plan.Wheel.maxSuspension;
            if (plan.Wheel.wheelPosition == Wheel.WheelPosition.Front) frontAxles.Add(plan.Center.z);
            else rearAxles.Add(plan.Center.z);
        }
        if (frontAxles.Count != 2 || rearAxles.Count != 2)
            return Fail("native axle classification is incomplete", "wheel-binding");
        vehicle.wheelDistance = Mathf.Abs((frontAxles[0] + frontAxles[1] - rearAxles[0] - rearAxles[1]) * 0.5f);
        return true;
    }

    private static bool CopyBodyColliders(Transform playerRoot, Transform source, GameObject clone)
    {
        var sourceColliders = source.GetComponents<BoxCollider>();
        if (sourceColliders.Length == 0)
            return Fail("BodyCollider contains no BoxCollider components", "body-collider");

        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger || collider is WheelCollider ||
                IsTrafficWheel(collider.transform, clone.transform))
                continue;
            collider.enabled = false;
        }

        var holder = new GameObject("AventadorNpcBodyCollider");
        holder.layer = clone.layer;
        holder.transform.SetParent(clone.transform, false);
        holder.transform.localPosition = playerRoot.InverseTransformPoint(source.position);
        holder.transform.localRotation = Quaternion.Inverse(playerRoot.rotation) * source.rotation;
        holder.transform.localScale = source.lossyScale;
        var copied = 0;
        foreach (var original in sourceColliders)
        {
            if (!original.enabled || original.isTrigger)
                continue;
            var box = holder.AddComponent<BoxCollider>();
            box.center = original.center;
            box.size = original.size;
            box.sharedMaterial = original.sharedMaterial;
            copied++;
        }
        if (copied == 0)
            return Fail("BodyCollider has no enabled non-trigger boxes", "body-collider");

        var trafficVehicle = clone.GetComponent<VehicleComponent>();
        if (trafficVehicle == null || VehicleCollidersField == null)
            return Fail("native Gley collider cache is unavailable", "body-collider");
        // Gley stores collider references during template initialization. Point
        // that cache at the new body boxes while preserving active wheel and
        // traffic trigger colliders from the cloned template.
        var ownedColliders = new List<Collider>(holder.GetComponents<Collider>());
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
            if (collider.enabled && !ownedColliders.Contains(collider)) ownedColliders.Add(collider);
        VehicleCollidersField.SetValue(trafficVehicle, ownedColliders.ToArray());
        return true;
    }

    private static bool IsTrafficWheel(Transform candidate, Transform root)
    {
        for (var parent = candidate; parent != null && parent != root; parent = parent.parent)
            if (parent.name.StartsWith("Wheel", StringComparison.Ordinal)) return true;
        return false;
    }

    private static GameObject CloneVisual(Transform source, Transform parent, int layer)
    {
        var visual = UnityEngine.Object.Instantiate(source.gameObject, parent, true);
        visual.name = source.name;
        foreach (var transform in visual.GetComponentsInChildren<Transform>(true))
            transform.gameObject.layer = layer;
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = true;
        return visual;
    }

    private static bool CalculateBounds(Transform root, Transform visual, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            var world = renderer.bounds;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? world.min.x : world.max.x,
                    (corner & 2) == 0 ? world.min.y : world.max.y,
                    (corner & 4) == 0 ? world.min.z : world.max.z);
                point = root.InverseTransformPoint(point);
                if (found) bounds.Encapsulate(point);
                else { bounds = new Bounds(point, Vector3.zero); found = true; }
            }
        }
        return found;
    }

    private static GameObject? LoadAiTemplate()
    {
#if UNITY_EDITOR
        if (AiTemplateFactoryForTests != null)
            return AiTemplateFactoryForTests();
#endif
        var cache = GetPrefabCache();
        if (cache != null && cache.Contains(AiTemplateCacheKey) && cache[AiTemplateCacheKey] is GameObject cached)
            return cached;

        foreach (var method in typeof(AddressableResolver).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name != "LoadAssetAsync" || !method.IsGenericMethodDefinition)
                continue;
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                continue;
            var handle = method.MakeGenericMethod(typeof(GameObject)).Invoke(null, new object[] { AiTemplateCacheKey });
            var wait = handle?.GetType().GetMethod("WaitForCompletion", BindingFlags.Instance | BindingFlags.Public);
            return wait?.Invoke(handle, null) as GameObject;
        }
        return null;
    }

    private static IDictionary? GetPrefabCache() => PrefabCacheField?.GetValue(null) as IDictionary;

    private static void ConfigureCarType(CarType carType)
    {
        carType.name = AiCarTypeName;
        carType.vehiclePrefab = customAiPrefab;
        carType.nrOfVehicles = PrivateDriverPoolSize;
        carType.canBeRandomlyParked = false;
        carType.hasParkedVersion = false;
        carType.canBeAiDriven = true;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        return null;
    }

    private static void SetMember(object? target, string name, object value)
    {
        if (target == null) return;
        var type = target.GetType();
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType.IsInstanceOfType(value)) { field.SetValue(target, value); return; }
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value)) property.SetValue(target, value);
    }

    private static bool Contains(IEnumerable<string>? values, string target)
    {
        if (values == null) return false;
        foreach (var value in values) if (string.Equals(value, target, StringComparison.Ordinal)) return true;
        return false;
    }

    private static void RemoveAll(List<string>? values, string target)
    {
        if (values == null) return;
        for (var index = values.Count - 1; index >= 0; index--)
            if (string.Equals(values[index], target, StringComparison.Ordinal)) values.RemoveAt(index);
    }

    private static GameObject? FailPrefab(string reason)
    {
        Fail(reason, "create-ai-prefab");
        return null;
    }

    private static bool Fail(string reason, string source)
    {
        if (!string.Equals(lastFailure, reason, StringComparison.Ordinal))
        {
            lastFailure = reason;
            context?.Logger.Warn($"LamborghiniAventador: traffic/private-driver preparation failed source='{source}': {reason}.");
        }
        return false;
    }

    private static void Trace(string source, string result)
    {
        if (LamborghiniAventadorDiagnostics.DebugEnabled && TrafficDebugEnabled)
            context?.Logger.Info($"LamborghiniAventador traffic source='{source}' result='{result}' pool={modifiedVehiclePool?.GetHashCode()} type='{AiCarTypeName}' prefab={customAiPrefab?.GetInstanceID()} poolSize={PrivateDriverPoolSize}.");
    }

    private readonly struct WheelPlan
    {
        internal WheelPlan(Wheel wheel, Transform visual, int index, Vector3 center, float radius)
        { Wheel = wheel; Visual = visual; Index = index; Center = center; Radius = radius; }
        internal readonly Wheel Wheel;
        internal readonly Transform Visual;
        internal readonly int Index;
        internal readonly Vector3 Center;
        internal readonly float Radius;
    }
}

[AddComponentMenu("")]
internal sealed class LamborghiniAventadorTrafficCalipers : MonoBehaviour
{
    private Transform? vehicleRoot;
    private readonly Transform?[] calipers = new Transform?[4];
    private readonly Transform?[] wheelHubs = new Transform?[4];

    internal bool Bind(Transform root, VehicleComponent vehicle, Transform[] sourceCalipers)
    {
        Array.Clear(calipers, 0, calipers.Length);
        Array.Clear(wheelHubs, 0, wheelHubs.Length);
        vehicleRoot = root;
        if (root == null || vehicle == null || vehicle.allWheels == null ||
            vehicle.allWheels.Length != 4 || sourceCalipers == null || sourceCalipers.Length != 4)
            return false;

        var used = new HashSet<Transform>();
        for (var index = 0; index < 4; index++)
        {
            var front = index < 2;
            var left = (index & 1) == 0;
            foreach (var wheel in vehicle.allWheels)
            {
                if (wheel?.wheelGraphics == null || used.Contains(wheel.wheelGraphics) ||
                    (wheel.wheelPosition == Wheel.WheelPosition.Front) != front ||
                    (root.InverseTransformPoint(wheel.wheelGraphics.position).x < 0f) != left)
                    continue;
                wheelHubs[index] = wheel.wheelGraphics;
                calipers[index] = sourceCalipers[index];
                used.Add(wheel.wheelGraphics);
                break;
            }
        }
        for (var index = 0; index < 4; index++)
            if (wheelHubs[index] == null || calipers[index] == null) return false;
        return true;
    }

    private void LateUpdate()
    {
        if (vehicleRoot == null) return;
        for (var index = 0; index < 4; index++)
        {
            var caliper = calipers[index];
            var hub = wheelHubs[index];
            if (caliper == null || hub == null) continue;
            var axle = Vector3.ProjectOnPlane(hub.right, vehicleRoot.up).normalized;
            if (axle.sqrMagnitude < 0.5f) continue;
            var steer = Vector3.SignedAngle(vehicleRoot.right, axle, vehicleRoot.up);
            if (Mathf.Abs(steer) > 60f) continue;
            caliper.SetPositionAndRotation(hub.position, vehicleRoot.rotation * Quaternion.Euler(0f, steer, 0f));
        }
    }
}
