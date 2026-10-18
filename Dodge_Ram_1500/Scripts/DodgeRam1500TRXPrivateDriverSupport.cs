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

internal static class DodgeRam1500TRXPrivateDriverSupport
{
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/dodgeram1500trx.prefab";
    private const string AiCarTypeName = "DodgeRam1500TRXPrivateDriver";
    private const int PrivateDriverPoolSize = 2;

    private static readonly List<PrivateDriverContract> ModifiedContracts =
        new List<PrivateDriverContract>(2);
    private static readonly Dictionary<string, VehicleColor> CapturedVehicleColors =
        new Dictionary<string, VehicleColor>(StringComparer.Ordinal);
    private static GameObject? customAiPrefab;
    internal static GameObject? TrafficPrefab => customAiPrefab;
    internal static string LastPoolFailure { get; private set; } = "not-attempted";
    private static UnityEngine.Object? previousCachedPrefab;
    private static bool previousCacheEntryCaptured;
    private static VehiclePool? modifiedVehiclePool;
    private static CarType? customCarType;
    private static ModContext? context;

    internal static void SetContext(ModContext modContext) => context = modContext;
    internal static ModContext? Context => context;

    internal static bool PrepareTrafficPool(GameObject playerPrefab)
    {
        LastPoolFailure = "AI-template/cache-or-owned-geometry-unavailable";
        if (playerPrefab == null || !EnsureAiPrefab(playerPrefab) || customAiPrefab == null)
            return false;

        var trafficComponent = TrafficComponent.Instance;
        if (trafficComponent == null) { LastPoolFailure = "TrafficComponent-not-awake"; return false; }
        var pool = trafficComponent.vehiclePool;
        if (pool == null) { LastPoolFailure = "native-pool-unavailable"; return false; }

        return RegisterOwnedPool(pool, TrafficManager.IsInitialized);
    }

    private static bool RegisterOwnedPool(VehiclePool pool, bool trafficInitialized)
    {
        if (customAiPrefab == null) return false;
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
            LastPoolFailure = "none";
            return true;
        }

        if (trafficInitialized) { LastPoolFailure = "native-traffic-already-initialized; reload-needed"; return false; }

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
        LastPoolFailure = "none";
        DodgeRam1500TRXDiagnostics.Info(context, $"DodgeRam1500TRX traffic registration: appended owned type before native traffic initialization; entries={expanded.Length}, pool={PrivateDriverPoolSize}.");
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
        {
            if (capturedColor != null)
                UnityEngine.Object.Destroy(capturedColor);
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
        bool paintApplied)
    {
        var message =
            $"DodgeRam1500TRX: chauffeur appearance color='{colorName ?? "<none>"}', " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            DodgeRam1500TRXDiagnostics.Info(context, message);
        else
            context?.Logger.Warn(message);
    }

    internal static void ReportDeparturePaintResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"DodgeRam1500TRX: chauffeur departure paint color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            DodgeRam1500TRXDiagnostics.Info(context, message);
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
            ? cache[AiTemplateCacheKey] as GameObject : null;
        // A native cache key can survive with an unloaded/null object. Resolve
        // that exact stock template again at this lifecycle point.
        if (template == null) template = LoadAiTemplate();
        if (template == null)
        {
            LastPoolFailure = "native-AI-template-unavailable:" + AiTemplateCacheKey;
            return null;
        }

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

        clone.name = "DodgeRam1500TRXPrivateDriver";
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(clone);

        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        var requiredVisuals = new[]
        {
            "DodgeRam1500TRXVisual",
            "DodgeRam1500TRXDamageBody",
            "DodgeRam1500TRXWheelFrontLeft",
            "DodgeRam1500TRXWheelFrontRight",
            "DodgeRam1500TRXWheelRearLeft",
            "DodgeRam1500TRXWheelRearRight",
            "DodgeRam1500TRXFixedCaliperFrontLeft",
            "DodgeRam1500TRXFixedCaliperFrontRight",
            "DodgeRam1500TRXFixedCaliperRearLeft",
            "DodgeRam1500TRXFixedCaliperRearRight",
        };
        foreach (var visualName in requiredVisuals)
        {
            var source = FindTransform(playerPrefab.transform, visualName);
            if (source == null)
            {
                LastPoolFailure = "missing-owned-visual:" + visualName;
                context?.Logger.Warn("DodgeRam1500TRX NPC prefab rejected: " + LastPoolFailure);
                UnityEngine.Object.Destroy(clone);
                return null;
            }

            var visual = UnityEngine.Object.Instantiate(source.gameObject, clone.transform, false);
            visual.name = visualName;
            SetLayerRecursively(visual.transform, clone.layer);
        }

        DodgeRam1500TRXMaterials.FixSolidMaterials(clone);
        // The NPC prefab copies visual roots, so retain a dedicated seat anchor.
        Transform? playerSeat = null;
        foreach (var child in playerPrefab.GetComponentsInChildren<Transform>(true))
            if (child.name.IndexOf("DodgeRam1500TRXSteeringWheel", StringComparison.OrdinalIgnoreCase) >= 0)
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
            Debug.LogWarning("DodgeRam1500TRX NPC driver seat source is missing.");

        clone.AddComponent<DodgeRam1500TRXAmbientTrafficAppearance>();
        clone.AddComponent<DodgeRam1500TRXAmbientTrafficLighting>();
        clone.AddComponent<DodgeRam1500TRXAmbientTrafficDriver>();
        if (!FitAiBodyColliders(clone, playerPrefab))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        if (!DodgeRam1500TRXTrafficWheels.Configure(clone, context))
        {
            LastPoolFailure = "native-wheel-hub-binding-failed";
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        var appearance = clone.AddComponent<DodgeRam1500TRXPrivateDriverAppearance>();
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
        var sourceBoxes = source?.GetComponentsInChildren<BoxCollider>(true);
        if (source == null || sourceBoxes == null || sourceBoxes.Length == 0)
        {
            Debug.LogWarning("DodgeRam1500TRX NPC body collider source is missing.");
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

        var holder = new GameObject("DodgeRam1500TRX NpcBodyCollider");
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
            Debug.LogWarning("DodgeRam1500TRX NPC body colliders have invalid bounds.");
            UnityEngine.Object.Destroy(holder);
            return false;
        }

        FitAiNavigationObstacles(clone, left, right, rear, front);
        DodgeRam1500TRXDiagnostics.TrafficInfo($"DodgeRam1500TRX NPC body boxes={holder.GetComponents<BoxCollider>().Length} " +
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
                Debug.LogWarning($"DodgeRam1500TRX NPC obstacle could not be fitted name='{obstacle.name}'.");
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
                Debug.LogWarning($"DodgeRam1500TRX NPC obstacle has invalid fitted length name='{obstacle.name}'.");
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
            DodgeRam1500TRXDiagnostics.TrafficInfo($"DodgeRam1500TRX NPC obstacle name='{obstacle.name}' " +
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


internal sealed class DodgeRam1500TRXAmbientTrafficLighting : MonoBehaviour
{
    private void Start()
    {
        var lighting = GetComponent<DodgeRam1500TRXLightingController>() ??
                       gameObject.AddComponent<DodgeRam1500TRXLightingController>();
        lighting.InitializeForAmbientTraffic(DodgeRam1500TRXPrivateDriverSupport.Context);
    }
}

internal sealed class DodgeRam1500TRXAmbientTrafficDriver : MonoBehaviour
{
    private void OnEnable()
    {
        var driver = GetComponent<DodgeRam1500TRXDriverController>() ??
                     gameObject.AddComponent<DodgeRam1500TRXDriverController>();
        driver.InitializeForAmbientTraffic(DodgeRam1500TRXPrivateDriverSupport.Context);
    }
}

[DefaultExecutionOrder(1001)]
internal sealed class DodgeRam1500TRXAmbientTrafficAppearance : MonoBehaviour
{
    private const int NativeColorAssignmentFrameLimit = 4;
    private Coroutine? initializationCoroutine;
    private int colorLogs;

    private void OnEnable()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(ApplyNativeTrafficColor());
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
                var paint = GetComponent<DodgeRam1500TRXPaintController>();
                if (paint == null)
                    paint = gameObject.AddComponent<DodgeRam1500TRXPaintController>();
                paint.InitializeForPrivateDriver(color.name, color);
                applied = paint.HasAppliedColor;
            }
            if (colorLogs++ < 8)
                DodgeRam1500TRXDiagnostics.TrafficInfo($"DodgeRam1500TRX ambient NPC id={GetInstanceID()} " +
                    $"color='{(color != null ? color.name : "<none>")}' applied={applied}.");
            if (color != null && !applied && colorLogs <= 8)
                Debug.LogWarning($"DodgeRam1500TRX ambient NPC id={GetInstanceID()} failed to apply '{color.name}'.");
        }
        initializationCoroutine = null;
    }
}

[DefaultExecutionOrder(1000)]
internal sealed class DodgeRam1500TRXPrivateDriverAppearance : MonoBehaviour
{
    private const int InitializationFrameLimit = 4;

    private static readonly PropertyInfo? CurrentVehicleProperty =
        typeof(Player.HUD.SmartphoneUI.SmartphonePrivateDriverUI).GetProperty(
            "CurrentVehicle",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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

        if (!DodgeRam1500TRXTrafficWheels.Configure(gameObject, DodgeRam1500TRXPrivateDriverSupport.Context)) return;
        caliperBindings.Clear();
        BindCaliper("DodgeRam1500TRXFixedCaliperFrontLeft", "DodgeRam1500TRXWheelFrontLeft");
        BindCaliper("DodgeRam1500TRXFixedCaliperFrontRight", "DodgeRam1500TRXWheelFrontRight");
        BindCaliper("DodgeRam1500TRXFixedCaliperRearLeft", "DodgeRam1500TRXWheelRearLeft");
        BindCaliper("DodgeRam1500TRXFixedCaliperRearRight", "DodgeRam1500TRXWheelRearRight");
        wheelVisualsBound = caliperBindings.Count == 4;
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

                DodgeRam1500TRXPrivateDriverSupport.ReportAppearanceResult(
                    candidate.vehicleInstance.vehicleColorName,
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
                DodgeRam1500TRXPrivateDriverSupport.ReportDeparturePaintResult(
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
        if (DodgeRam1500TRXPrivateDriverSupport.TryResolveDriverColor(
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

        var paint = GetComponent<DodgeRam1500TRXPaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<DodgeRam1500TRXPaintController>();
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
