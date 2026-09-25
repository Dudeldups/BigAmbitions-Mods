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

internal static class AudiRS6RPrivateDriverSupport
{
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/audirs6r.prefab";
    private const string AiCarTypeName = "AudiRS6RPrivateDriver";
    private const int PrivateDriverPoolSize = 2;

    private static readonly List<PrivateDriverContract> ModifiedContracts = new(2);
    private static readonly Dictionary<string, VehicleColor> CapturedVehicleColors =
        new(StringComparer.Ordinal);
    private static GameObject? customAiPrefab;
    internal static GameObject? TrafficPrefab => customAiPrefab;
    private static UnityEngine.Object? previousCachedPrefab;
    private static bool previousCacheEntryCaptured;
    private static VehiclePool? modifiedVehiclePool;
    private static CarType? customCarType;
    private static ModContext? context;

    internal static void SetContext(ModContext modContext) => context = modContext;
    internal static ModContext? Context => context;

    internal static bool PrepareTrafficPool(GameObject playerPrefab)
    {
        if (playerPrefab == null || !EnsureAiPrefab(playerPrefab) || customAiPrefab == null)
            return false;

        var trafficComponent = TrafficComponent.Instance;
        if (trafficComponent == null)
            return false;
        var pool = trafficComponent.vehiclePool;
        if (pool == null)
            return false;

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
            if (capturedColor != null)
                UnityEngine.Object.Destroy(capturedColor);
        CapturedVehicleColors.Clear();

        if (modifiedVehiclePool != null && customCarType != null)
        {
            var existing = modifiedVehiclePool.trafficCars ?? Array.Empty<CarType>();
            var remaining = new List<CarType>(existing.Length);
            foreach (var carType in existing)
                if (!ReferenceEquals(carType, customCarType))
                    remaining.Add(carType);
            modifiedVehiclePool.trafficCars = remaining.ToArray();
        }
        modifiedVehiclePool = null;
        customCarType = null;

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
        context = null;
    }

    internal static void ReportAppearanceResult(
        string? colorName,
        string? liveColorName,
        bool paintApplied)
    {
        var message =
            $"AudiRS6R: chauffeur appearance color='{colorName ?? "<none>"}' " +
            $"liveColor='{liveColorName ?? "<none>"}', paintApplied={paintApplied}.";
        if (paintApplied)
            context?.Logger.Info(message);
        else
            context?.Logger.Warn(message);
    }

    internal static void ReportDepartureCorrection(int vehicleIndex) =>
        context?.Logger.Info(
            $"AudiRS6R: chauffeur departure resumed after dismissal vehicleIndex={vehicleIndex}.");

    internal static void ReportDeparturePaintResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"AudiRS6R: chauffeur departure paint color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            context?.Logger.Info(message);
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
        if (!Contains(contract.usableVehicleTypes, vehicleTypeName))
        {
            contract.usableVehicleTypes.Add(vehicleTypeName);
            if (!ModifiedContracts.Contains(contract))
                ModifiedContracts.Add(contract);
        }
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
            : LoadTemplateFromAddressables();
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

        clone.name = "AudiRS6RPrivateDriver";
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(clone);

        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        var requiredVisuals = new[]
        {
            "Body",
            "Paint",
            "3DWheel Front L",
            "3DWheel Front R",
            "3DWheel Rear L",
            "3DWheel Rear R",
        };
        foreach (var visualName in requiredVisuals)
        {
            var source = FindTransform(playerPrefab.transform, visualName);
            if (source == null)
            {
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            var visual = UnityEngine.Object.Instantiate(source.gameObject, clone.transform, false);
            visual.name = visualName;
            SetLayerRecursively(visual.transform, clone.layer);
        }

        if (!FitAiBodyColliders(clone, playerPrefab))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        // Keep a seat marker on the AI prefab, which only copies selected visual roots.
        Transform? playerSeat = null;
        foreach (var child in playerPrefab.GetComponentsInChildren<Transform>(true))
            if (child.name == "Animate_SteeringWheel_033")
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
            Debug.LogWarning("AudiRS6R NPC driver seat source is missing.");

        clone.AddComponent<AudiRS6RAmbientTrafficAppearance>();
        clone.AddComponent<AudiRS6RAmbientTrafficLighting>();
        clone.AddComponent<AudiRS6RAmbientTrafficDriver>();
        var appearance = clone.AddComponent<AudiRS6RPrivateDriverAppearance>();
        appearance.BindWheelVisuals();

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
            Debug.LogWarning("AudiRS6R NPC body collider source is missing.");
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

        var holder = new GameObject("AudiRS6R NpcBodyCollider");
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
            Debug.LogWarning("AudiRS6R NPC body colliders have invalid bounds.");
            UnityEngine.Object.Destroy(holder);
            return false;
        }

        FitAiNavigationObstacles(clone, left, right, rear, front);
        AudiRS6RDiagnostics.TrafficInfo($"AudiRS6R NPC body boxes={holder.GetComponents<BoxCollider>().Length} " +
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
                Debug.LogWarning($"AudiRS6R NPC obstacle could not be fitted name='{obstacle.name}'.");
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
                Debug.LogWarning($"AudiRS6R NPC obstacle has invalid fitted length name='{obstacle.name}'.");
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
            AudiRS6RDiagnostics.TrafficInfo($"AudiRS6R NPC obstacle name='{obstacle.name}' " +
                $"oldRear={oldRear:0.000} oldFront={oldFront:0.000} " +
                $"rootLeft={targetLeft:0.000} rootRight={targetRight:0.000} " +
                $"rootRear={targetRear:0.000} rootFront={targetFront:0.000}.");
        }
    }

    private static GameObject? LoadTemplateFromAddressables()
    {
        // This is a fallback for unusually early initialization, before
        // PrefabHelper has populated its cache. Keep the ResourceManager handle
        // behind reflection so the mod does not acquire a compile-time package
        // dependency solely for this recovery path.
        MethodInfo? loadMethod = null;
        foreach (var candidate in typeof(BigAmbitions.SaveSystem.AddressableResolver).GetMethods(
                     BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(candidate.Name, "LoadAssetAsync", StringComparison.Ordinal) ||
                !candidate.IsGenericMethodDefinition ||
                candidate.GetGenericArguments().Length != 1 ||
                candidate.GetParameters().Length != 1)
            {
                continue;
            }

            loadMethod = candidate;
            break;
        }

        var handle = loadMethod?
            .MakeGenericMethod(typeof(GameObject))
            .Invoke(null, new object[] { AiTemplateCacheKey });
        var waitMethod = handle?.GetType().GetMethod(
            "WaitForCompletion",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            Type.EmptyTypes,
            null);
        return waitMethod?.Invoke(handle, null) as GameObject;
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

internal sealed class AudiRS6RAmbientTrafficLighting : MonoBehaviour
{
    private void Start()
    {
        var lighting = GetComponent<AudiRS6RLightingController>() ??
                       gameObject.AddComponent<AudiRS6RLightingController>();
        lighting.InitializeForAmbientTraffic(AudiRS6RPrivateDriverSupport.Context);
    }
}

[DefaultExecutionOrder(1001)]
internal sealed class AudiRS6RAmbientTrafficDriver : MonoBehaviour
{
    private void Start()
    {
        var driver = GetComponent<AudiRS6RDriverController>() ??
                     gameObject.AddComponent<AudiRS6RDriverController>();
        driver.InitializeForAmbientTraffic(AudiRS6RPrivateDriverSupport.Context);
    }
}

internal sealed class AudiRS6RAmbientTrafficAppearance : MonoBehaviour
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
        if (!AudiRS6RDiagnostics.TrafficEnabled || stateLogs >= 12)
            return;
        var moving = (transform.position - lastPosition).sqrMagnitude > 0.0004f;
        lastPosition = transform.position;
        var obstacleEnabled = obstacle != null && obstacle.enabled;
        if (lastMoving == moving && lastObstacleEnabled == obstacleEnabled)
            return;
        lastMoving = moving;
        lastObstacleEnabled = obstacleEnabled;
        stateLogs++;
        AudiRS6RDiagnostics.TrafficInfo(
            $"AudiRS6R NPC id={GetInstanceID()} moving={moving} " +
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
            // Chauffeur instances retain the separately validated saved-color path.
            if (GetComponent<PrivateDriverVehicle>() != null)
            {
                initializationCoroutine = null;
                yield break;
            }

            yield return null;
        }

        if (GetComponent<PrivateDriverVehicle>() == null)
        {
            var liveColor = GetComponent<CarFeatures>()?.VehicleColor;
            if (liveColor != null)
            {
                var paint = GetComponent<AudiRS6RMaterialController>();
                if (paint == null)
                    paint = gameObject.AddComponent<AudiRS6RMaterialController>();
                paint.InitializeForAmbientTraffic(liveColor);
                if (colorLogs++ < 8)
                    AudiRS6RDiagnostics.TrafficInfo($"AudiRS6R ambient NPC id={GetInstanceID()} " +
                        $"color='{liveColor.name}' applied={paint.HasAppliedColor}.");
                if (!paint.HasAppliedColor && colorLogs <= 8)
                    Debug.LogWarning($"AudiRS6R ambient NPC id={GetInstanceID()} failed to apply '{liveColor.name}'.");
            }
        }

        initializationCoroutine = null;
    }
}

[DefaultExecutionOrder(1000)]
internal sealed class AudiRS6RPrivateDriverAppearance : MonoBehaviour
{
    private const int InitializationFrameLimit = 4;

    private static readonly PropertyInfo? CurrentVehicleProperty =
        typeof(Player.HUD.SmartphoneUI.SmartphonePrivateDriverUI).GetProperty(
            "CurrentVehicle",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly string[,] WheelNames =
    {
        { "3DWheel Front L", "FL" },
        { "3DWheel Front R", "FR" },
        { "3DWheel Rear L", "BL" },
        { "3DWheel Rear R", "BR" },
    };

    private readonly List<WheelBinding> wheelBindings = new(4);
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
        for (var index = 0; index < WheelNames.GetLength(0); index++)
        {
            var visual = FindTransform(transform, WheelNames[index, 0]);
            var source = FindTransform(transform, WheelNames[index, 1]);
            if (visual == null || source == null)
                continue;
            wheelBindings.Add(new WheelBinding(transform, visual, source));
        }
        wheelVisualsBound = wheelBindings.Count == WheelNames.GetLength(0);
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

                // A pooled traffic vehicle can receive a random color during its
                // first few activation frames. Restore the saved vehicle color at
                // that exact lifecycle boundary, then stop after this fixed window.
                var paintApplied = RestoreSavedVehicleColor(false);
                for (var pass = 1; pass < InitializationFrameLimit; pass++)
                {
                    yield return new WaitForEndOfFrame();
                    paintApplied = RestoreSavedVehicleColor(
                        pass == InitializationFrameLimit - 1);
                }

                var liveColorName = GetComponent<CarFeatures>()?.VehicleColor?.name;
                AudiRS6RPrivateDriverSupport.ReportAppearanceResult(
                    candidate.vehicleInstance.vehicleColorName,
                    liveColorName,
                    paintApplied);
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
        {
            return;
        }

        // A stop-state event can arrive immediately before DriveAway. Restarting
        // the bounded check ensures the final event is the one evaluated after
        // SmartphonePrivateDriverUI has cleared CurrentVehicle.
        if (departureCheckCoroutine != null)
            StopCoroutine(departureCheckCoroutine);
        departureCheckCoroutine = StartCoroutine(EnsureDepartureAfterDismissal());
    }

    private IEnumerator EnsureDepartureAfterDismissal()
    {
        // DismissPrivateDriver clears CurrentVehicle only after DriveAway raises
        // its traffic-state event. Waiting one frame distinguishes that event
        // from the normal stop events used while picking up the player.
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
                AudiRS6RPrivateDriverSupport.ReportDepartureCorrection(
                    trafficVehicle.GetIndex());
            }

            if (privateDriver?.vehicleInstance != null)
            {
                var colorName = privateDriver.vehicleInstance.vehicleColorName;
                AudiRS6RPrivateDriverSupport.ReportDeparturePaintResult(
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
        VehicleColor? savedColor = null;
        if (AudiRS6RPrivateDriverSupport.TryResolveDriverColor(
                colorName,
                features?.VehicleColor,
                out savedColor) &&
            savedColor != null)
        {
            features?.SetColor(savedColor);
            restoredBaseColor = features != null;
        }

        if (!applySpecializedPaint)
            return restoredBaseColor;

        var paint = GetComponent<AudiRS6RMaterialController>();
        if (paint == null)
            paint = gameObject.AddComponent<AudiRS6RMaterialController>();
        paint.InitializeForPrivateDriver(colorName, savedColor);
        return paint.HasAppliedColor;
    }

    private void LateUpdate()
    {
        foreach (var binding in wheelBindings)
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
            if (string.Equals(child.name, name, StringComparison.Ordinal))
                return child;
        return null;
    }

    private sealed class WheelBinding
    {
        private readonly Transform visual;
        private readonly Transform source;
        private readonly Vector3 initialVisualPosition;
        private readonly Vector3 initialSourcePosition;
        private readonly Quaternion rotationOffset;

        internal WheelBinding(Transform root, Transform visual, Transform source)
        {
            this.visual = visual;
            this.source = source;
            initialVisualPosition = root.InverseTransformPoint(visual.position);
            initialSourcePosition = root.InverseTransformPoint(source.position);
            rotationOffset = Quaternion.Inverse(source.rotation) * visual.rotation;
        }

        internal void Apply(Transform root)
        {
            var sourcePosition = root.InverseTransformPoint(source.position);
            visual.position = root.TransformPoint(
                initialVisualPosition + sourcePosition - initialSourcePosition);
            visual.rotation = source.rotation * rotationOffset;
        }
    }
}
