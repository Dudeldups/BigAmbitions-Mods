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

internal static class McLarenSennaPrivateDriverSupport
{
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/mclarensenna.prefab";
    private const string AiCarTypeName = "McLarenSennaPrivateDriver";
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
    private static VehiclePool? reportedMissingLivePool;
    private static readonly FieldInfo? VehicleCollidersField = typeof(VehicleComponent).GetField(
        "allColliders", BindingFlags.Instance | BindingFlags.NonPublic);
    private static ModContext? context;

    internal static void SetContext(ModContext modContext) => context = modContext;
    internal static ModContext? Context => context;

    internal static bool PrepareTrafficPool(GameObject playerPrefab, string source = "runtime")
    {
        var trafficComponent = TrafficComponent.Instance;
        if (trafficComponent == null)
        {
            TracePoolPreparation(source, "traffic-component-unavailable");
            return false;
        }
        var pool = trafficComponent.vehiclePool;
        if (pool == null)
        {
            TracePoolPreparation(source, "vehicle-pool-unavailable");
            return false;
        }
        if (playerPrefab == null || !EnsureAiPrefab(playerPrefab) || customAiPrefab == null)
        {
            TracePoolPreparation(source, "ai-prefab-unavailable");
            return false;
        }

        var existing = pool.trafficCars ?? Array.Empty<CarType>();
        foreach (var carType in existing)
        {
            if (carType == null ||
                (!ReferenceEquals(carType.vehiclePrefab, customAiPrefab) &&
                 !string.Equals(carType.name, AiCarTypeName, StringComparison.Ordinal)))
                continue;

            carType.name = AiCarTypeName;
            carType.vehiclePrefab = customAiPrefab;
            carType.nrOfVehicles = PrivateDriverPoolSize;
            carType.canBeRandomlyParked = false;
            carType.hasParkedVersion = false;
            carType.canBeAiDriven = true;
            modifiedVehiclePool = pool;
            customCarType = carType;
            TracePoolPreparation(source, "existing-owned-entry-ready");
            return true;
        }

        if (TrafficManager.IsInitialized)
        {
            if (!ReferenceEquals(reportedMissingLivePool, pool))
            {
                reportedMissingLivePool = pool;
                context?.Logger.Warn($"McLarenSenna: cannot add missing traffic type '{AiCarTypeName}' " +
                    $"after native pool initialization source='{source}'. A cold save reload is required.");
            }
            return false;
        }

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
        TracePoolPreparation(source, "appended-before-native-initialization");
        return true;
    }

    private static void TracePoolPreparation(string source, string result)
    {
        McLarenSennaDiagnostics.PrivateDriverInfo(context,
            $"McLarenSenna traffic pool source='{source}' result='{result}', " +
            $"pool={TrafficComponent.Instance?.vehiclePool?.GetInstanceID()}, " +
            $"type='{AiCarTypeName}', prefab={customAiPrefab?.GetInstanceID()}, size={PrivateDriverPoolSize}.");
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
        {
            return false;
        }

        EnsureContractContains(advanced, vehicleTypeName);
        EnsureContractContains(premium, vehicleTypeName);
        return Contains(advanced.usableVehicleTypes, vehicleTypeName) &&
               Contains(premium.usableVehicleTypes, vehicleTypeName);
    }

    internal static void RemoveVehicle(string vehicleTypeName)
    {
        if (!string.IsNullOrWhiteSpace(vehicleTypeName))
        {
            foreach (var contract in ModifiedContracts)
                RemoveAll(contract.usableVehicleTypes, vehicleTypeName);
        }
        ModifiedContracts.Clear();

        foreach (var capturedColor in CapturedVehicleColors.Values)
        {
            if (capturedColor != null)
                DestroyOwnedObject(capturedColor);
        }
        CapturedVehicleColors.Clear();

        if (modifiedVehiclePool != null && customCarType != null)
        {
            var existing = modifiedVehiclePool.trafficCars ?? Array.Empty<CarType>();
            var remaining = new List<CarType>(existing.Length);
            foreach (var carType in existing)
            {
                if (!ReferenceEquals(carType, customCarType))
                    remaining.Add(carType);
            }
            modifiedVehiclePool.trafficCars = remaining.ToArray();
        }
        modifiedVehiclePool = null;
        customCarType = null;
        reportedMissingLivePool = null;

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
            DestroyOwnedObject(customAiPrefab);
        customAiPrefab = null;
        previousCachedPrefab = null;
        previousCacheEntryCaptured = false;
        context = null;
    }

    private static void DestroyOwnedObject(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }

    internal static void ReportAppearanceResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"McLarenSenna: chauffeur appearance color='{colorName ?? "<none>"}', " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            McLarenSennaDiagnostics.Info(context, message);
        else
            context?.Logger.Warn(message);
    }

    internal static void ReportDeparturePaintResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"McLarenSenna: chauffeur departure paint color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            McLarenSennaDiagnostics.Info(context, message);
        else
            context?.Logger.Warn(message);
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

        clone.name = "McLarenSennaPrivateDriver";
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        if (Application.isPlaying)
            UnityEngine.Object.DontDestroyOnLoad(clone);

        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        var requiredVisuals = new[]
        {
            "McLarenVisual",
            "McLarenSteeringAnchor",
            "McLarenDriverSeat",
            "McLarenLightBeams",
            "McLarenDamageBody",
            "McLarenWheelFrontLeft",
            "McLarenWheelFrontRight",
            "McLarenWheelRearLeft",
            "McLarenWheelRearRight",
            "McLarenFixedCaliperFrontLeft",
            "McLarenFixedCaliperFrontRight",
            "McLarenFixedCaliperRearLeft",
            "McLarenFixedCaliperRearRight",
        };
        foreach (var visualName in requiredVisuals)
        {
            var source = FindTransform(playerPrefab.transform, visualName);
            if (source == null)
            {
                context?.Logger.Warn($"McLarenSenna: required chauffeur/NPC visual '{visualName}' " +
                    "is missing from the player prefab; AI preparation stopped.");
                DestroyOwnedObject(clone);
                return null;
            }

            var visual = UnityEngine.Object.Instantiate(source.gameObject, clone.transform, false);
            visual.name = visualName;
            SetLayerRecursively(visual.transform, clone.layer);
        }

        McLarenSennaMaterials.FixSolidMaterials(clone);
        clone.AddComponent<McLarenSennaAmbientTrafficAppearance>();
        clone.AddComponent<McLarenSennaAmbientTrafficLighting>();
        clone.AddComponent<McLarenSennaAmbientTrafficDriver>();
        if (!FitAiBodyColliders(clone, playerPrefab))
        {
            DestroyOwnedObject(clone);
            return null;
        }
        if (!McLarenSennaTrafficWheels.Configure(clone, context))
        {
            DestroyOwnedObject(clone);
            return null;
        }
        var appearance = clone.AddComponent<McLarenSennaPrivateDriverAppearance>();
        appearance.BindCaliperVisuals();

        foreach (var component in clone.GetComponents<MonoBehaviour>())
        {
            if (component != null && string.Equals(
                    component.GetType().FullName,
                    "GleyTrafficSystem.VehicleComponent",
                    StringComparison.Ordinal))
            {
                SetMember(component, "prefab", clone);
                break;
            }
        }

        return clone;
    }


    private static bool FitAiBodyColliders(GameObject clone, GameObject playerPrefab)
    {
        var source = FindTransform(playerPrefab.transform, "BodyCollider");
        var sourceBoxes = source?.GetComponents<BoxCollider>();
        if (source == null || sourceBoxes == null || sourceBoxes.Length == 0)
        {
            Debug.LogWarning("McLarenSenna NPC body collider source is missing.");
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

        var holder = new GameObject("McLarenSenna NpcBodyCollider");
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
        if (front <= rear)
        {
            Debug.LogWarning("McLarenSenna NPC body colliders have invalid bounds.");
            DestroyOwnedObject(holder);
            return false;
        }

        var trafficVehicle = clone.GetComponent<VehicleComponent>();
        if (trafficVehicle == null || VehicleCollidersField == null)
        {
            Debug.LogWarning("McLarenSenna NPC native collision bindings are unavailable.");
            return false;
        }
        // Gley caches these references in the authored template. Replacing its
        // body colliders must also replace that cache, including GetCollider's
        // primary collider, while preserving active traffic triggers/wheels.
        var ownedColliders = new List<Collider>(holder.GetComponents<BoxCollider>());
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
            if (collider.enabled && !ownedColliders.Contains(collider))
                ownedColliders.Add(collider);
        VehicleCollidersField.SetValue(trafficVehicle, ownedColliders.ToArray());

        FitAiNavigationObstacles(clone, left, right, rear, front);
        McLarenSennaDiagnostics.TrafficInfo($"McLarenSenna NPC body boxes={holder.GetComponents<BoxCollider>().Length} " +
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
                Debug.LogWarning($"McLarenSenna NPC obstacle could not be fitted name='{obstacle.name}'.");
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
                Debug.LogWarning($"McLarenSenna NPC obstacle has invalid fitted length name='{obstacle.name}'.");
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
            McLarenSennaDiagnostics.TrafficInfo($"McLarenSenna NPC obstacle name='{obstacle.name}' " +
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
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                continue;

            var handle = method
                .MakeGenericMethod(typeof(GameObject))
                .Invoke(null, new object[] { AiTemplateCacheKey });
            if (handle == null)
                return null;

            var waitForCompletion = handle.GetType().GetMethod(
                "WaitForCompletion",
                BindingFlags.Instance | BindingFlags.Public);
            return waitForCompletion?.Invoke(handle, null) as GameObject;
        }

        return null;
    }

    private static IDictionary? GetPrefabCache()
    {
        var field = typeof(PrefabHelper).GetField(
            "PrefabCache",
            BindingFlags.Static | BindingFlags.NonPublic);
        return field?.GetValue(null) as IDictionary;
    }

    private static void SetMember(object target, string name, object value)
    {
        var type = target.GetType();
        var field = type.GetField(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType.IsInstanceOfType(value))
        {
            field.SetValue(target, value);
            return;
        }

        var property = type.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
            property.SetValue(target, value);
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        }
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
        {
            if (string.Equals(value, target, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static void RemoveAll(List<string>? values, string target)
    {
        if (values == null)
            return;
        for (var index = values.Count - 1; index >= 0; index--)
        {
            if (string.Equals(values[index], target, StringComparison.Ordinal))
                values.RemoveAt(index);
        }
    }
}


internal sealed class McLarenSennaAmbientTrafficLighting : MonoBehaviour
{
    private void Start()
    {
        var lighting = GetComponent<McLarenSennaLightingController>() ??
                       gameObject.AddComponent<McLarenSennaLightingController>();
        lighting.InitializeForAmbientTraffic(McLarenSennaPrivateDriverSupport.Context);
    }
}

internal sealed class McLarenSennaAmbientTrafficDriver : MonoBehaviour
{
    private void Start()
    {
        var driver = GetComponent<McLarenSennaDriverController>() ??
                     gameObject.AddComponent<McLarenSennaDriverController>();
        driver.InitializeForAmbientTraffic(McLarenSennaPrivateDriverSupport.Context);
    }
}

[DefaultExecutionOrder(1001)]
internal sealed class McLarenSennaAmbientTrafficAppearance : MonoBehaviour
{
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
        if (!McLarenSennaDiagnostics.TrafficEnabled || stateLogs >= 12)
            return;
        var moving = (transform.position - lastPosition).sqrMagnitude > 0.0004f;
        lastPosition = transform.position;
        var obstacleEnabled = obstacle != null && obstacle.enabled;
        if (lastMoving == moving && lastObstacleEnabled == obstacleEnabled)
            return;
        lastMoving = moving;
        lastObstacleEnabled = obstacleEnabled;
        stateLogs++;
        McLarenSennaDiagnostics.TrafficInfo(
            $"McLarenSenna NPC id={GetInstanceID()} moving={moving} " +
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
        // The traffic pool assigns its final components during spawn. Defer one
        // frame from OnEnable, then handle this spawn once instead of polling.
        yield return null;

        if (GetComponent<PrivateDriverVehicle>() == null)
        {
            // The native template can have no initialized color, or retain a
            // prior pooled color. Select from the game's weighted palette for
            // this ambient activation; chauffeur paint follows saved data.
            var color = VehicleHelper.GetRandomVehicleColor();
            var applied = false;
            if (color != null)
            {
                applied = ApplyAmbientColor(color);
            }
            if (colorLogs++ < 8)
                McLarenSennaDiagnostics.TrafficInfo($"McLarenSenna ambient NPC id={GetInstanceID()} " +
                    $"color='{(color != null ? color.name : "<none>")}' applied={applied}.");
            if (!applied && colorLogs <= 8)
                Debug.LogWarning($"McLarenSenna ambient NPC id={GetInstanceID()} failed to apply native palette color '{color?.name ?? "<none>"}'.");
        }
        initializationCoroutine = null;
    }

    internal bool ApplyAmbientColor(VehicleColor color)
    {
        if (GetComponent<PrivateDriverVehicle>() != null) return false;
        GetComponent<CarFeatures>()?.SetColor(color);
        var materials = GetComponent<McLarenSennaMaterialController>();
        if (materials == null) materials = gameObject.AddComponent<McLarenSennaMaterialController>();
        materials.Initialize(McLarenSennaPrivateDriverSupport.Context);
        var paint = GetComponent<McLarenSennaPaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<McLarenSennaPaintController>();
        paint.InitializeForPrivateDriver(color.name, color);
        return paint.HasAppliedColor;
    }
}

[DefaultExecutionOrder(1000)]
internal sealed class McLarenSennaPrivateDriverAppearance : MonoBehaviour
{

    private static readonly PropertyInfo? CurrentVehicleProperty =
        typeof(Player.HUD.SmartphoneUI.SmartphonePrivateDriverUI).GetProperty(
            "CurrentVehicle",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private readonly List<CaliperBinding> caliperBindings = new List<CaliperBinding>(4);
    private bool calipersBound;
    private Coroutine? initializationCoroutine;
    private Coroutine? departureCheckCoroutine;
    private PrivateDriverVehicle? privateDriver;
    private VehicleComponent? trafficVehicle;
    private bool trafficEventsSubscribed;

    internal void BindCaliperVisuals()
    {
        if (calipersBound)
            return;

        caliperBindings.Clear();
        BindCaliper("McLarenFixedCaliperFrontLeft", "McLarenWheelFrontLeft");
        BindCaliper("McLarenFixedCaliperFrontRight", "McLarenWheelFrontRight");
        BindCaliper("McLarenFixedCaliperRearLeft", "McLarenWheelRearLeft");
        BindCaliper("McLarenFixedCaliperRearRight", "McLarenWheelRearRight");
        calipersBound = caliperBindings.Count == 4;
    }

    private void BindCaliper(string caliperName, string wheelName)
    {
        var caliper = FindTransform(transform, caliperName);
        var wheel = FindTransform(transform, wheelName);
        if (caliper != null && wheel != null)
            caliperBindings.Add(new CaliperBinding(caliper, wheel));
    }

    private void Awake() => BindCaliperVisuals();

    private void OnEnable()
    {
        BindCaliperVisuals();
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(InitializePrivateDriverState());
    }

    private IEnumerator InitializePrivateDriverState()
    {
        // Private-driver components and saved vehicle data are assigned as part
        // of the spawn lifecycle. Read them once after that lifecycle callback.
        yield return null;
        var candidate = GetComponent<PrivateDriverVehicle>();
        if (candidate == null || candidate.vehicleInstance == null)
        {
            initializationCoroutine = null;
            yield break;
        }

        GetComponent<McLarenSennaDriverController>()?.SetOccupied(false);
        privateDriver = candidate;
        trafficVehicle = GetComponent<VehicleComponent>();
        SubscribeTrafficEvents();

        var paintApplied = RestoreSavedVehicleColor(false);
        // Native color setup can finish at end of frame. Reapply the persistent
        // private-driver color once after that lifecycle boundary.
        yield return new WaitForEndOfFrame();
        paintApplied = RestoreSavedVehicleColor(true);
        McLarenSennaPrivateDriverSupport.ReportAppearanceResult(
            candidate.vehicleInstance.vehicleColorName,
            paintApplied);
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
        {
            return;
        }

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

        var currentPrivateDriver =
            CurrentVehicleProperty.GetValue(privateDriverUi) as PrivateDriverVehicle;
        var dismissed = privateDriver != null && currentPrivateDriver != privateDriver;
        if (dismissed && trafficVehicle != null && trafficVehicle.gameObject.activeInHierarchy)
        {
            trafficVehicle.presetPath = null;
            var trafficManager = TrafficManager.Instance;
            if (trafficManager != null)
            {
                trafficManager.SetVehicleAction(
                    trafficVehicle,
                    SpecialDriveActionTypes.Forward,
                    true);
                trafficManager.VehicleUpdateWaypoint(trafficVehicle);
            }
            if (privateDriver?.vehicleInstance != null)
            {
                var colorName = privateDriver.vehicleInstance.vehicleColorName;
                McLarenSennaPrivateDriverSupport.ReportDeparturePaintResult(
                    colorName,
                    RestoreSavedVehicleColor(true));
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
        var restoredBaseColor = false;
        if (McLarenSennaPrivateDriverSupport.TryResolveDriverColor(
                colorName,
                features?.VehicleColor,
                out var savedColor) &&
            savedColor != null)
        {
            features?.SetColor(savedColor);
            restoredBaseColor = features != null;
        }

        if (!applySpecializedPaint)
            return restoredBaseColor;

        var paint = GetComponent<McLarenSennaPaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<McLarenSennaPaintController>();
        paint.InitializeForPrivateDriver(colorName, savedColor);
        return paint.HasAppliedColor;
    }

    private void LateUpdate()
    {
        foreach (var binding in caliperBindings)
            binding.Apply(transform);
    }

    private void OnDisable()
    {
        var overlayManager =
            InstanceBehavior<Player.HUD.ItemInfoOverlays.OverlayManager>.Instance;
        if (overlayManager != null && privateDriver != null &&
            overlayManager.IsShowingOverlayOverItem(privateDriver))
        {
            overlayManager.HideSimpleOverlayAndClearCta();
        }

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
        {
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        }
        return null;
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

