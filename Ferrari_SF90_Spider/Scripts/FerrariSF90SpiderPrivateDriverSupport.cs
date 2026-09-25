#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using Buildings.BuildingTypes.Special.PrivateDriverService;
using Data.VehicleColors;
using GleyTrafficSystem;
using Helpers;
using UnityEngine;
using UnityEngine.AI;

internal static class FerrariSF90SpiderPrivateDriverSupport
{
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/ferrarisf90spider.prefab";
    private const string AiCarTypeName = "FerrariSF90SpiderPrivateDriver";
    private const int PrivateDriverPoolSize = 2;

    private static readonly List<PrivateDriverContract> ModifiedContracts =
        new List<PrivateDriverContract>(2);
    private static readonly Dictionary<string, VehicleColor> CapturedVehicleColors =
        new Dictionary<string, VehicleColor>(StringComparer.Ordinal);
    private static GameObject? customAiPrefab;
    internal static GameObject? TrafficPrefab => customAiPrefab;
    private static UnityEngine.Object? previousCachedPrefab;
    private static bool previousCacheEntryCaptured;
    private static VehiclePool? modifiedVehiclePool;
    private static CarType? customCarType;
    private static bool ownsVehiclePoolEntry;
    private static ModContext? context;

    internal static void SetContext(ModContext modContext) => context = modContext;
    internal static ModContext? Context => context;

    internal static bool PrepareTrafficPool(GameObject playerPrefab)
    {
        if (playerPrefab == null || !EnsureAiPrefab(playerPrefab) || customAiPrefab == null)
            return false;

        var trafficComponent = TrafficComponent.Instance;
        var pool = trafficComponent?.vehiclePool;
        if (pool == null)
            return false;

        var existing = pool.trafficCars ?? Array.Empty<CarType>();
        foreach (var carType in existing)
        {
            if (carType == null ||
                (!ReferenceEquals(carType.vehiclePrefab, customAiPrefab) &&
                 !string.Equals(carType.name, AiCarTypeName, StringComparison.Ordinal)))
                continue;

            var wasOwned = ownsVehiclePoolEntry && ReferenceEquals(carType, customCarType);
            modifiedVehiclePool = pool;
            customCarType = carType;
            ownsVehiclePoolEntry = wasOwned;
            if (TrafficManager.IsInitialized)
                return IsConfiguredCarType(carType);

            ConfigureCarType(carType);
            return true;
        }

        if (TrafficManager.IsInitialized)
            return false;

        customCarType = new CarType
        {
            name = AiCarTypeName,
            vehiclePrefab = customAiPrefab,
            nrOfVehicles = PrivateDriverPoolSize,
            canBeRandomlyParked = false,
            hasParkedVersion = false,
            canBeAiDriven = true,
        };
        var expanded = new CarType[existing.Length + 1];
        Array.Copy(existing, expanded, existing.Length);
        expanded[existing.Length] = customCarType;
        pool.trafficCars = expanded;
        modifiedVehiclePool = pool;
        ownsVehiclePoolEntry = true;
        FerrariSF90SpiderDiagnostics.Info(context,
            $"FerrariSF90Spider private-driver traffic type registered name='{AiCarTypeName}' " +
            $"poolSize={PrivateDriverPoolSize}.");
        return true;
    }

    internal static bool EnsureVehicleAvailable(string vehicleTypeName)
    {
        if (string.IsNullOrWhiteSpace(vehicleTypeName))
            return false;

        var contracts = PrivateDriverHelpers.GetContracts();
        if (contracts == null ||
            !contracts.TryGetValue(AdvancedContractKey, out var advanced) ||
            !contracts.TryGetValue(PremiumContractKey, out var premium) ||
            advanced == null || premium == null)
            return false;

        EnsureContractContains(advanced, vehicleTypeName);
        EnsureContractContains(premium, vehicleTypeName);
        return Contains(advanced.usableVehicleTypes, vehicleTypeName) &&
               Contains(premium.usableVehicleTypes, vehicleTypeName);
    }

    internal static void RemoveVehicle(string vehicleTypeName)
    {
        if (!string.IsNullOrWhiteSpace(vehicleTypeName))
            foreach (var contract in ModifiedContracts)
                RemoveAll(contract.usableVehicleTypes, vehicleTypeName);
        ModifiedContracts.Clear();

        var keepAiResources = false;
        if (modifiedVehiclePool != null && customCarType != null)
        {
            var existing = modifiedVehiclePool.trafficCars ?? Array.Empty<CarType>();
            var entryStillPresent = false;
            foreach (var carType in existing)
            {
                if (ReferenceEquals(carType, customCarType))
                {
                    entryStillPresent = true;
                    break;
                }
            }

            if (entryStillPresent && ownsVehiclePoolEntry && !TrafficManager.IsInitialized)
            {
                var remaining = new List<CarType>(Math.Max(0, existing.Length - 1));
                foreach (var carType in existing)
                {
                    if (!ReferenceEquals(carType, customCarType))
                        remaining.Add(carType);
                }
                modifiedVehiclePool.trafficCars = remaining.ToArray();
            }
            else if (entryStillPresent &&
                     ReferenceEquals(customCarType.vehiclePrefab, customAiPrefab))
            {
                // Native traffic arrays are fixed after initialization. Keep the
                // Ferrari prefab and captured colors alive instead of invalidating
                // an entry which the traffic manager may still reference.
                keepAiResources = true;
            }
        }

        if (!keepAiResources)
        {
            foreach (var color in CapturedVehicleColors.Values)
                if (color != null)
                    UnityEngine.Object.Destroy(color);
            CapturedVehicleColors.Clear();

            modifiedVehiclePool = null;
            customCarType = null;
            ownsVehiclePoolEntry = false;

            var cache = GetPrefabCache();
            if (cache != null && customAiPrefab != null &&
                cache.Contains(AiPrefabCacheKey) &&
                ReferenceEquals(cache[AiPrefabCacheKey], customAiPrefab))
            {
                if (previousCachedPrefab != null)
                    cache[AiPrefabCacheKey] = previousCachedPrefab;
                else
                    cache.Remove(AiPrefabCacheKey);
            }

            if (customAiPrefab != null)
                UnityEngine.Object.Destroy(customAiPrefab);
            customAiPrefab = null;
            previousCachedPrefab = null;
            previousCacheEntryCaptured = false;
        }

        context = null;
    }

    internal static bool TryResolveDriverColor(
        string? colorName,
        VehicleColor? liveColor,
        out VehicleColor? resolvedColor)
    {
        resolvedColor = null;
        if (string.IsNullOrEmpty(colorName))
            return false;
        var resolvedName = colorName!;

        if (liveColor != null &&
            string.Equals(liveColor.name, resolvedName, StringComparison.Ordinal))
        {
            resolvedColor = CaptureVehicleColor(resolvedName, liveColor);
            return true;
        }

        if (VehicleHelper.TryGetVehicleColor(resolvedName, out var registeredColor) &&
            registeredColor != null)
        {
            resolvedColor = CaptureVehicleColor(resolvedName, registeredColor);
            return true;
        }

        return CapturedVehicleColors.TryGetValue(resolvedName, out resolvedColor) &&
               resolvedColor != null;
    }

    internal static void ReportAppearanceResult(string? colorName, bool paintApplied)
    {
        var message =
            $"FerrariSF90Spider private-driver appearance color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            FerrariSF90SpiderDiagnostics.Info(context, message);
        else
            context?.Logger.Warn(message);
    }

    internal static void ReportDeparturePaintResult(string? colorName, bool paintApplied)
    {
        var message =
            $"FerrariSF90Spider private-driver departure color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            FerrariSF90SpiderDiagnostics.Info(context, message);
        else
            context?.Logger.Warn(message);
    }

    private static VehicleColor CaptureVehicleColor(string colorName, VehicleColor source)
    {
        if (!CapturedVehicleColors.TryGetValue(colorName, out var captured) || captured == null)
        {
            captured = ScriptableObject.CreateInstance<VehicleColor>();
            captured.name = colorName;
            captured.hideFlags = HideFlags.DontSave;
            CapturedVehicleColors[colorName] = captured;
        }

        captured.randomWeight = source.randomWeight;
        captured.tint = source.tint;
        captured.fresnelColor = source.fresnelColor;
        captured.fresnelPower = source.fresnelPower;
        return captured;
    }

    private static void EnsureContractContains(
        PrivateDriverContract contract,
        string vehicleTypeName)
    {
        contract.usableVehicleTypes ??= new List<string>();
        if (Contains(contract.usableVehicleTypes, vehicleTypeName))
            return;
        contract.usableVehicleTypes.Add(vehicleTypeName);
        if (!ModifiedContracts.Contains(contract))
            ModifiedContracts.Add(contract);
    }

    private static void ConfigureCarType(CarType carType)
    {
        carType.name = AiCarTypeName;
        carType.vehiclePrefab = customAiPrefab;
        carType.nrOfVehicles = PrivateDriverPoolSize;
        carType.canBeRandomlyParked = false;
        carType.hasParkedVersion = false;
        carType.canBeAiDriven = true;
    }

    private static bool IsConfiguredCarType(CarType carType) =>
        string.Equals(carType.name, AiCarTypeName, StringComparison.Ordinal) &&
        ReferenceEquals(carType.vehiclePrefab, customAiPrefab) &&
        carType.nrOfVehicles == PrivateDriverPoolSize &&
        !carType.canBeRandomlyParked &&
        !carType.hasParkedVersion &&
        carType.canBeAiDriven;

    private static bool EnsureAiPrefab(GameObject playerPrefab)
    {
        var cache = GetPrefabCache();
        if (cache == null)
            return false;
        if (customAiPrefab == null)
            customAiPrefab = CreateAiPrefab(playerPrefab);
        if (customAiPrefab == null)
            return false;

        if (!previousCacheEntryCaptured)
        {
            previousCachedPrefab = cache.Contains(AiPrefabCacheKey)
                ? cache[AiPrefabCacheKey] as UnityEngine.Object
                : null;
            previousCacheEntryCaptured = true;
        }
        cache[AiPrefabCacheKey] = customAiPrefab;
        return true;
    }

    private static GameObject? CreateAiPrefab(GameObject playerPrefab)
    {
        var cache = GetPrefabCache();
        var template = cache != null && cache.Contains(AiTemplateCacheKey)
            ? cache[AiTemplateCacheKey] as GameObject
            : LoadAiTemplate();
        if (template == null)
            return null;

        var templateWasActive = template.activeSelf;
        GameObject clone;
        try
        {
            template.SetActive(false);
            clone = UnityEngine.Object.Instantiate(template);
        }
        finally
        {
            template.SetActive(templateWasActive);
        }

        clone.name = "FerrariSF90SpiderPrivateDriver";
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(clone);
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        var requiredVisuals = new[]
        {
            "FerrariSF90SpiderVisual",
            "FerrariSF90SpiderWheelFrontLeft",
            "FerrariSF90SpiderWheelFrontRight",
            "FerrariSF90SpiderWheelRearLeft",
            "FerrariSF90SpiderWheelRearRight",
            "FerrariSF90SpiderFixedCaliperFrontLeft",
            "FerrariSF90SpiderFixedCaliperFrontRight",
            "FerrariSF90SpiderFixedCaliperRearLeft",
            "FerrariSF90SpiderFixedCaliperRearRight",
        };
        foreach (var visualName in requiredVisuals)
        {
            var source = FindTransform(playerPrefab.transform, visualName);
            if (source == null)
            {
                context?.Logger.Warn(
                    $"FerrariSF90Spider private-driver prefab source missing visual='{visualName}'.");
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            var visual = UnityEngine.Object.Instantiate(source.gameObject, clone.transform, false);
            visual.name = visualName;
            SetLayerRecursively(visual.transform, clone.layer);
        }

        // The imported prefab contains a separate bonnet-camera duplicate of
        // the body. It must never be part of the private-driver appearance;
        // otherwise the AI vehicle can show the same missing/stacked panels as
        // an early player instance.
        foreach (var transform in clone.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.IndexOf("BONNETCAM", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            transform.gameObject.SetActive(false);
            foreach (var renderer in transform.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
        }

        RepairPrivateDriverBodyShell(clone);

        FerrariSF90SpiderMaterials.FixSolidMaterials(clone);
        // The NPC prefab copies visual roots, so retain a dedicated seat anchor.
        Transform? playerSeat = null;
        foreach (var child in playerPrefab.GetComponentsInChildren<Transform>(true))
            if (child.name.IndexOf("STEERING_WHEEL", StringComparison.OrdinalIgnoreCase) >= 0)
                playerSeat = child;
        if (playerSeat != null)
        {
            var aiSeat = new GameObject("NpcDriverSeatAnchor");
            aiSeat.layer = clone.layer;
            aiSeat.transform.SetParent(clone.transform, false);
            aiSeat.transform.localPosition =
                playerPrefab.transform.InverseTransformPoint(playerSeat.position);
            aiSeat.transform.localRotation =
                Quaternion.Inverse(playerPrefab.transform.rotation) * playerSeat.rotation;
        }
        else
            Debug.LogWarning("FerrariSF90Spider NPC driver seat source is missing.");

        clone.AddComponent<FerrariSF90SpiderAmbientTrafficAppearance>();
        clone.AddComponent<FerrariSF90SpiderAmbientTrafficLighting>();
        clone.AddComponent<FerrariSF90SpiderAmbientTrafficDriver>();
        if (!FitAiBodyColliders(clone, playerPrefab))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        var appearance = clone.AddComponent<FerrariSF90SpiderPrivateDriverAppearance>();
        appearance.BindWheelVisuals();

        foreach (var component in clone.GetComponents<MonoBehaviour>())
        {
            if (component == null || !string.Equals(
                    component.GetType().FullName,
                    "GleyTrafficSystem.VehicleComponent",
                    StringComparison.Ordinal))
                continue;
            SetMember(component, "prefab", clone);
            break;
        }

        FerrariSF90SpiderDiagnostics.Info(context,
            "FerrariSF90Spider private-driver prefab prepared with in-place body, " +
            "four wheels, and four fixed calipers.");
        return clone;
    }

    private static void RepairPrivateDriverBodyShell(GameObject clone)
    {
        MeshFilter? damageFilter = null;
        MeshRenderer? damageRenderer = null;
        MeshFilter? sourceFilter = null;
        MeshRenderer? sourceRenderer = null;
        foreach (var filter in clone.GetComponentsInChildren<MeshFilter>(true))
        {
            if (string.Equals(filter.name, "FerrariSF90SpiderDamageBody", StringComparison.Ordinal))
            {
                damageFilter = filter;
                damageRenderer = filter.GetComponent<MeshRenderer>();
            }

            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null && IsSf90BodyPaintFilter(filter))
            {
                sourceFilter = filter;
                sourceRenderer = renderer;
            }
        }

        if (sourceFilter == null || sourceRenderer == null || sourceFilter.sharedMesh == null)
        {
            context?.Logger.Warn(
                "FerrariSF90Spider private-driver body shell repair skipped: " +
                "SF90 body paint source is missing.");
            return;
        }
        sourceRenderer.enabled = sourceRenderer.sharedMaterials.Length > 0;
        if (damageRenderer != null)
            damageRenderer.enabled = false;
    }

    private static bool IsSf90BodyPaintFilter(MeshFilter filter)
    {
        if (filter.name.IndexOf("body:Paint_Geo_lodA", StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null)
            return false;
        foreach (var material in renderer.sharedMaterials)
            if (material != null &&
                material.name.IndexOf("2021Paint_Material", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static bool FitAiBodyColliders(GameObject clone, GameObject playerPrefab)
    {
        var source = FindTransform(playerPrefab.transform, "BodyCollider");
        var sourceBoxes = source?.GetComponents<BoxCollider>();
        if (source == null || sourceBoxes == null || sourceBoxes.Length == 0)
        {
            Debug.LogWarning("FerrariSF90Spider NPC body collider source is missing.");
            return false;
        }

        var disabled = 0;
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger || collider is WheelCollider ||
                IsTrafficWheel(collider.transform, clone.transform))
                continue;
            collider.enabled = false;
            disabled++;
        }

        var holder = new GameObject("FerrariSF90Spider NpcBodyCollider");
        holder.layer = clone.layer;
        holder.transform.SetParent(clone.transform, false);
        holder.transform.localPosition = source.localPosition;
        holder.transform.localRotation = source.localRotation;
        holder.transform.localScale = source.localScale;

        var rear = float.PositiveInfinity;
        var front = float.NegativeInfinity;
        var left = float.PositiveInfinity;
        var right = float.NegativeInfinity;

        foreach (var original in sourceBoxes)
        {
            if (!original.enabled || original.isTrigger)
                continue;

            var box = holder.AddComponent<BoxCollider>();
            box.center = original.center;
            box.size = original.size;
            box.sharedMaterial = original.sharedMaterial;

            rear = Mathf.Min(rear, clone.transform.InverseTransformPoint(
                holder.transform.TransformPoint(box.center - Vector3.forward * box.size.z * 0.5f)).z);
            front = Mathf.Max(front, clone.transform.InverseTransformPoint(
                holder.transform.TransformPoint(box.center + Vector3.forward * box.size.z * 0.5f)).z);
            left = Mathf.Min(left, clone.transform.InverseTransformPoint(
                holder.transform.TransformPoint(box.center - Vector3.right * box.size.x * 0.5f)).x);
            right = Mathf.Max(right, clone.transform.InverseTransformPoint(
                holder.transform.TransformPoint(box.center + Vector3.right * box.size.x * 0.5f)).x);
        }

        if (front <= rear || right <= left)
        {
            Debug.LogWarning("FerrariSF90Spider NPC body colliders have invalid bounds.");
            UnityEngine.Object.Destroy(holder);
            return false;
        }

        FitAiNavigationObstacles(clone, left, right, rear, front);
        FerrariSF90SpiderDiagnostics.TrafficInfo(
            $"FerrariSF90Spider NPC body boxes={holder.GetComponents<BoxCollider>().Length} " +
            $"disabledTemplate={disabled} rootRear={rear:0.000} rootFront={front:0.000}.");
        return true;
    }

    private static bool IsTrafficWheel(Transform candidate, Transform root)
    {
        for (var current = candidate; current != null && current != root; current = current.parent)
        {
            var name = current.name;
            if (name.IndexOf("Wheel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name == "FL" || name == "FR" || name == "BL" || name == "BR")
                return true;
        }
        return false;
    }

    private static void FitAiNavigationObstacles(
        GameObject clone, float bodyLeft, float bodyRight, float bodyRear, float bodyFront)
    {
        const float pedestrianClearance = 0.40f;
        var root = clone.transform;
        foreach (var obstacle in clone.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle.shape != NavMeshObstacleShape.Box ||
                Vector3.Dot(root.forward, obstacle.transform.forward) < 0.99f)
            {
                Debug.LogWarning(
                    $"FerrariSF90Spider NPC obstacle could not be fitted name='{obstacle.name}'.");
                continue;
            }

            var oldRear = root.InverseTransformPoint(obstacle.transform.TransformPoint(
                obstacle.center - Vector3.forward * obstacle.size.z * 0.5f)).z;
            var oldFront = root.InverseTransformPoint(obstacle.transform.TransformPoint(
                obstacle.center + Vector3.forward * obstacle.size.z * 0.5f)).z;
            var targetLeft = bodyLeft + pedestrianClearance;
            var targetRight = bodyRight - pedestrianClearance;
            var targetRear = bodyRear + pedestrianClearance;
            var targetFront = bodyFront - pedestrianClearance;

            var localLeft = obstacle.transform.InverseTransformPoint(
                root.TransformPoint(new Vector3(targetLeft, 0f, 0f))).x;
            var localRight = obstacle.transform.InverseTransformPoint(
                root.TransformPoint(new Vector3(targetRight, 0f, 0f))).x;
            var localRear = obstacle.transform.InverseTransformPoint(
                root.TransformPoint(new Vector3(0f, 0f, targetRear))).z;
            var localFront = obstacle.transform.InverseTransformPoint(
                root.TransformPoint(new Vector3(0f, 0f, targetFront))).z;

            if (localFront <= localRear + 0.05f || localRight <= localLeft + 0.05f)
            {
                Debug.LogWarning(
                    $"FerrariSF90Spider NPC obstacle has invalid fitted bounds name='{obstacle.name}'.");
                continue;
            }

            var center = obstacle.center;
            center.x = (localLeft + localRight) * 0.5f;
            center.z = (localRear + localFront) * 0.5f;
            var size = obstacle.size;
            size.x = localRight - localLeft;
            size.z = localFront - localRear;
            obstacle.center = center;
            obstacle.size = size;

            FerrariSF90SpiderDiagnostics.TrafficInfo(
                $"FerrariSF90Spider NPC obstacle name='{obstacle.name}' " +
                $"oldRear={oldRear:0.000} oldFront={oldFront:0.000} " +
                $"rootLeft={targetLeft:0.000} rootRight={targetRight:0.000} " +
                $"rootRear={targetRear:0.000} rootFront={targetFront:0.000}.");
        }
    }

    private static GameObject? LoadAiTemplate()
    {
        foreach (var method in typeof(BigAmbitions.SaveSystem.AddressableResolver).GetMethods(
                     BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, "LoadAssetAsync", StringComparison.Ordinal) ||
                !method.IsGenericMethodDefinition)
                continue;
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                continue;
            var handle = method.MakeGenericMethod(typeof(GameObject)).Invoke(
                null, new object[] { AiTemplateCacheKey });
            if (handle == null)
                return null;
            return handle.GetType().GetMethod(
                       "WaitForCompletion", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(handle, null) as GameObject;
        }
        return null;
    }

    private static IDictionary? GetPrefabCache()
    {
        return typeof(PrefabHelper).GetField(
            "PrefabCache",
            BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as IDictionary;
    }

    private static void SetMember(object target, string name, object value)
    {
        var type = target.GetType();
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType.IsInstanceOfType(value))
        {
            field.SetValue(target, value);
            return;
        }
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
            property.SetValue(target, value);
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        return null;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
    }

    private static bool Contains(IEnumerable<string>? values, string target)
    {
        if (values == null)
            return false;
        foreach (var value in values)
            if (string.Equals(value, target, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void RemoveAll(List<string>? values, string target)
    {
        if (values == null)
            return;
        for (var index = values.Count - 1; index >= 0; index--)
            if (string.Equals(values[index], target, StringComparison.Ordinal))
                values.RemoveAt(index);
    }
}

internal sealed class FerrariSF90SpiderAmbientTrafficLighting : MonoBehaviour
{
    private void Start()
    {
        var lighting = GetComponent<FerrariSF90SpiderLightingController>() ??
                       gameObject.AddComponent<FerrariSF90SpiderLightingController>();
        lighting.InitializeForAmbientTraffic(FerrariSF90SpiderPrivateDriverSupport.Context);
    }
}

internal sealed class FerrariSF90SpiderAmbientTrafficDriver : MonoBehaviour
{
    private void Start()
    {
        var driver = GetComponent<FerrariSF90SpiderDriverController>() ??
                     gameObject.AddComponent<FerrariSF90SpiderDriverController>();
        driver.InitializeForAmbientTraffic(FerrariSF90SpiderPrivateDriverSupport.Context);
    }
}

[DefaultExecutionOrder(1001)]
internal sealed class FerrariSF90SpiderAmbientTrafficAppearance : MonoBehaviour
{
    private const int NativeColorAssignmentFrameLimit = 4;
    private Coroutine? initializationCoroutine;
    private NavMeshObstacle? obstacle;
    private Vector3 lastPosition;
    private bool? lastMoving;
    private bool? lastObstacleEnabled;
    private int stateLogs;
    private int colorLogs;

    private void OnEnable()
    {
        obstacle = GetComponentInChildren<NavMeshObstacle>(true);
        lastPosition = transform.position;
        lastMoving = null;
        lastObstacleEnabled = null;
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(ApplyNativeTrafficColor());
    }

    private void LateUpdate()
    {
        if (!FerrariSF90SpiderDiagnostics.TrafficEnabled || stateLogs >= 12)
            return;

        var moving = (transform.position - lastPosition).sqrMagnitude > 0.0004f;
        lastPosition = transform.position;
        var obstacleEnabled = obstacle != null && obstacle.enabled;
        if (lastMoving == moving && lastObstacleEnabled == obstacleEnabled)
            return;

        lastMoving = moving;
        lastObstacleEnabled = obstacleEnabled;
        stateLogs++;
        FerrariSF90SpiderDiagnostics.TrafficInfo(
            $"FerrariSF90Spider NPC id={GetInstanceID()} moving={moving} " +
            $"standingObstacle={obstacleEnabled} transition={stateLogs}.");
    }

    private void OnDisable()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
    }

    private IEnumerator ApplyNativeTrafficColor()
    {
        for (var frame = 0; frame < NativeColorAssignmentFrameLimit; frame++)
        {
            if (GetComponent<PrivateDriverVehicle>() != null)
            {
                initializationCoroutine = null;
                yield break;
            }
            yield return null;
        }

        if (GetComponent<PrivateDriverVehicle>() == null)
        {
            var color = GetComponent<CarFeatures>()?.VehicleColor;
            var applied = false;
            if (color != null)
            {
                var paint = GetComponent<FerrariSF90SpiderPaintController>();
                if (paint == null)
                    paint = gameObject.AddComponent<FerrariSF90SpiderPaintController>();
                paint.InitializeForPrivateDriver(color.name, color);
                applied = paint.HasAppliedColor;
            }

            if (colorLogs++ < 8)
                FerrariSF90SpiderDiagnostics.TrafficInfo(
                    $"FerrariSF90Spider ambient NPC id={GetInstanceID()} " +
                    $"color='{(color != null ? color.name : "<none>")}' applied={applied}.");
            if (color != null && !applied && colorLogs <= 8)
                Debug.LogWarning(
                    $"FerrariSF90Spider ambient NPC id={GetInstanceID()} failed to apply '{color.name}'.");
        }

        initializationCoroutine = null;
    }
}

[DefaultExecutionOrder(1000)]
internal sealed class FerrariSF90SpiderPrivateDriverAppearance : MonoBehaviour
{
    private const int InitializationFrameLimit = 4;
    private static readonly PropertyInfo? CurrentVehicleProperty =
        typeof(Player.HUD.SmartphoneUI.SmartphonePrivateDriverUI).GetProperty(
            "CurrentVehicle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly string[,] WheelNames =
    {
        { "FerrariSF90SpiderWheelFrontLeft", "FL" },
        { "FerrariSF90SpiderWheelFrontRight", "FR" },
        { "FerrariSF90SpiderWheelRearLeft", "BL" },
        { "FerrariSF90SpiderWheelRearRight", "BR" },
    };

    private readonly List<WheelBinding> wheelBindings = new List<WheelBinding>(4);
    private readonly List<CaliperBinding> caliperBindings = new List<CaliperBinding>(4);
    private bool wheelVisualsBound;
    private Coroutine? initializationCoroutine;
    private Coroutine? departureCheckCoroutine;
    private PrivateDriverVehicle? privateDriver;
    private VehicleComponent? trafficVehicle;
    private bool trafficEventsSubscribed;

    internal void BindWheelVisuals()
    {
        if (wheelVisualsBound)
            return;
        wheelBindings.Clear();
        caliperBindings.Clear();
        for (var index = 0; index < WheelNames.GetLength(0); index++)
        {
            var visual = FindTransform(transform, WheelNames[index, 0]);
            var source = FindTransform(transform, WheelNames[index, 1]);
            if (visual == null || source == null)
                continue;
            wheelBindings.Add(new WheelBinding(transform, visual, source));
        }
        BindCaliper("FerrariSF90SpiderFixedCaliperFrontLeft", "FerrariSF90SpiderWheelFrontLeft");
        BindCaliper("FerrariSF90SpiderFixedCaliperFrontRight", "FerrariSF90SpiderWheelFrontRight");
        BindCaliper("FerrariSF90SpiderFixedCaliperRearLeft", "FerrariSF90SpiderWheelRearLeft");
        BindCaliper("FerrariSF90SpiderFixedCaliperRearRight", "FerrariSF90SpiderWheelRearRight");
        wheelVisualsBound = wheelBindings.Count == WheelNames.GetLength(0);
    }

    private void BindCaliper(string caliperName, string wheelName)
    {
        var caliper = FindTransform(transform, caliperName);
        var wheel = FindTransform(transform, wheelName);
        if (caliper != null && wheel != null)
            caliperBindings.Add(new CaliperBinding(caliper, wheel));
    }

    private void Awake() => BindWheelVisuals();

    private void OnEnable()
    {
        BindWheelVisuals();
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(InitializePrivateDriverState());
    }

    private IEnumerator InitializePrivateDriverState()
    {
        for (var frame = 0; frame < InitializationFrameLimit; frame++)
        {
            var candidate = GetComponent<PrivateDriverVehicle>();
            if (candidate != null && candidate.vehicleInstance != null)
            {
                privateDriver = candidate;
                trafficVehicle = GetComponent<VehicleComponent>();
                SubscribeTrafficEvents();
                var paintApplied = RestoreSavedVehicleColor(false);
                for (var pass = 1; pass < InitializationFrameLimit; pass++)
                {
                    yield return new WaitForEndOfFrame();
                    paintApplied = RestoreSavedVehicleColor(
                        pass == InitializationFrameLimit - 1);
                }
                FerrariSF90SpiderPrivateDriverSupport.ReportAppearanceResult(
                    candidate.vehicleInstance.vehicleColorName, paintApplied);
                initializationCoroutine = null;
                yield break;
            }
            yield return null;
        }
        initializationCoroutine = null;
    }

    private void SubscribeTrafficEvents()
    {
        if (trafficEventsSubscribed)
            return;
        AIEvents.onChangeDrivingState += HandleDrivingStateChanged;
        trafficEventsSubscribed = true;
    }

    private void HandleDrivingStateChanged(
        int vehicleIndex,
        SpecialDriveActionTypes action,
        float actionValue)
    {
        if (privateDriver == null || trafficVehicle == null ||
            vehicleIndex != trafficVehicle.GetIndex())
            return;
        if (departureCheckCoroutine != null)
            StopCoroutine(departureCheckCoroutine);
        departureCheckCoroutine = StartCoroutine(EnsureDepartureAfterDismissal());
    }

    private IEnumerator EnsureDepartureAfterDismissal()
    {
        yield return null;
        var privateDriverUi = UI.UIs.Instance?.smartphoneUI?.privateDriverUI;
        if (privateDriverUi == null || CurrentVehicleProperty == null)
        {
            departureCheckCoroutine = null;
            yield break;
        }

        var current = CurrentVehicleProperty.GetValue(privateDriverUi) as PrivateDriverVehicle;
        var dismissed = privateDriver != null && current != privateDriver;
        if (dismissed && trafficVehicle != null && trafficVehicle.gameObject.activeInHierarchy)
        {
            trafficVehicle.presetPath = null;
            var trafficManager = TrafficManager.Instance;
            if (trafficManager != null)
            {
                trafficManager.SetVehicleAction(
                    trafficVehicle, SpecialDriveActionTypes.Forward, true);
                trafficManager.VehicleUpdateWaypoint(trafficVehicle);
            }
            if (privateDriver?.vehicleInstance != null)
            {
                var colorName = privateDriver.vehicleInstance.vehicleColorName;
                FerrariSF90SpiderPrivateDriverSupport.ReportDeparturePaintResult(
                    colorName, RestoreSavedVehicleColor(true));
            }
        }
        departureCheckCoroutine = null;
    }

    private bool RestoreSavedVehicleColor(bool applySpecializedPaint)
    {
        if (privateDriver?.vehicleInstance == null)
            return false;
        var colorName = privateDriver.vehicleInstance.vehicleColorName;
        var features = GetComponent<CarFeatures>();
        VehicleColor? savedColor = null;
        var restoredBaseColor =
            FerrariSF90SpiderPrivateDriverSupport.TryResolveDriverColor(
                colorName, features?.VehicleColor, out savedColor) &&
            savedColor != null;
        if (restoredBaseColor)
            features?.SetColor(savedColor);
        if (!applySpecializedPaint)
            return restoredBaseColor;

        var paint = GetComponent<FerrariSF90SpiderPaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<FerrariSF90SpiderPaintController>();
        paint.InitializeForPrivateDriver(colorName, savedColor);
        return paint.HasAppliedColor;
    }

    private void LateUpdate()
    {
        foreach (var binding in wheelBindings)
            binding.Apply(transform);
        foreach (var binding in caliperBindings)
            binding.Apply(transform);
    }

    private void OnDisable()
    {
        var overlayManager =
            InstanceBehavior<Player.HUD.ItemInfoOverlays.OverlayManager>.Instance;
        if (overlayManager != null && privateDriver != null &&
            overlayManager.IsShowingOverlayOverItem(privateDriver))
            overlayManager.HideSimpleOverlayAndClearCta();
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        if (departureCheckCoroutine != null)
            StopCoroutine(departureCheckCoroutine);
        initializationCoroutine = null;
        departureCheckCoroutine = null;
        if (trafficEventsSubscribed)
        {
            AIEvents.onChangeDrivingState -= HandleDrivingStateChanged;
            trafficEventsSubscribed = false;
        }
        privateDriver = null;
        trafficVehicle = null;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        return null;
    }

    private sealed class WheelBinding
    {
        private readonly Transform visual;
        private readonly Transform source;
        private readonly Vector3 initialVisualCenter;
        private readonly Vector3 initialSourcePosition;
        private readonly Vector3 visualCenter;
        private readonly Quaternion rotationOffset;

        internal WheelBinding(Transform root, Transform visual, Transform source)
        {
            this.visual = visual;
            this.source = source;
            var center = GetRendererCenter(visual);
            initialVisualCenter = root.InverseTransformPoint(center);
            initialSourcePosition = root.InverseTransformPoint(source.position);
            visualCenter = visual.InverseTransformPoint(center);
            rotationOffset = Quaternion.Inverse(source.rotation) * visual.rotation;
        }

        internal void Apply(Transform root)
        {
            var sourcePosition = root.InverseTransformPoint(source.position);
            var targetCenter = root.TransformPoint(
                initialVisualCenter + sourcePosition - initialSourcePosition);
            visual.rotation = source.rotation * rotationOffset;
            visual.position += targetCenter - visual.TransformPoint(visualCenter);
        }

        private static Vector3 GetRendererCenter(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return root.position;
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            return bounds.center;
        }
    }

    private sealed class CaliperBinding
    {
        private readonly Transform caliper;
        private readonly Transform wheel;

        internal CaliperBinding(Transform caliper, Transform wheel)
        {
            this.caliper = caliper;
            this.wheel = wheel;
        }

        internal void Apply(Transform root)
        {
            var axle = Vector3.ProjectOnPlane(wheel.right, root.up).normalized;
            if (axle.sqrMagnitude < 0.5f)
                return;
            var steeringAngle = Vector3.SignedAngle(root.right, axle, root.up);
            if (Mathf.Abs(steeringAngle) > 60f)
                return;
            caliper.SetPositionAndRotation(
                wheel.position,
                root.rotation * Quaternion.Euler(0f, steeringAngle, 0f));
        }
    }
}
