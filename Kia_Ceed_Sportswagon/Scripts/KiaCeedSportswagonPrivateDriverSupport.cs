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

internal static class KiaCeedSportswagonPrivateDriverSupport
{
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/kiaceedsportswagon.prefab";
    private const string AiCarTypeName = "KiaCeedSportswagonPrivateDriver";
    private const int PrivateDriverPoolSize = 2;
    // Match the confirmed road-contact wheel rig to the Kia wheel arches.
    private static readonly Vector3 NpcBodyOffset = new Vector3(0f, 0.055f, -0.06f);

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
    private static readonly FieldInfo? VehicleCollidersField = typeof(VehicleComponent).GetField(
        "allColliders", BindingFlags.Instance | BindingFlags.NonPublic);
    private static ModContext? context;
    private static GameObject? lightOverlayPrefab;

    internal static void SetContext(ModContext modContext) => context = modContext;
    internal static void SetLightOverlayPrefab(GameObject prefab) => lightOverlayPrefab = prefab;
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
        lightOverlayPrefab = null;
        previousCachedPrefab = null;
        previousCacheEntryCaptured = false;
        context = null;
    }

    internal static void ReportAppearanceResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"KiaCeedSportswagon: chauffeur appearance color='{colorName ?? "<none>"}', " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            KiaCeedSportswagonDiagnostics.Info(context, message);
        else
            context?.Logger.Warn(message);
    }

    internal static void ReportDeparturePaintResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"KiaCeedSportswagon: chauffeur departure paint color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            KiaCeedSportswagonDiagnostics.Info(context, message);
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

        clone.name = "KiaCeedSportswagonPrivateDriver";
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(clone);

        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        var requiredVisuals = new[]
        {
            "KiaCeedSportswagonVisual",
            "KiaCeedSportswagonDamageBody",
            "KiaCeedSportswagonWheelFrontLeft",
            "KiaCeedSportswagonWheelFrontRight",
            "KiaCeedSportswagonWheelRearLeft",
            "KiaCeedSportswagonWheelRearRight",
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
            if (!visualName.StartsWith("KiaCeedSportswagonWheel", StringComparison.Ordinal))
                visual.transform.localPosition += NpcBodyOffset;
            SetLayerRecursively(visual.transform, clone.layer);
        }

        if (lightOverlayPrefab != null)
            KiaCeedSportswagonLightOverlayBinder.Attach(clone, lightOverlayPrefab, context);
        else
            Debug.LogWarning("KiaCeedSportswagon private-driver prefab has no Blender light overlay asset.");

        KiaCeedSportswagonMaterials.FixSolidMaterials(clone);
        // The NPC prefab copies visual roots, so retain a dedicated seat anchor.
        var playerSeat = FindTransform(playerPrefab.transform, "KiaCeedSportswagonDriverSeatAnchor");
        if (playerSeat != null)
        {
            var aiSeat = new GameObject("NpcDriverSeatAnchor");
            aiSeat.layer = clone.layer;
            aiSeat.transform.SetParent(clone.transform, false);
            aiSeat.transform.localPosition =
                playerPrefab.transform.InverseTransformPoint(playerSeat.position) + NpcBodyOffset;
            aiSeat.transform.localRotation =
                Quaternion.Inverse(playerPrefab.transform.rotation) * playerSeat.rotation;
        }
        else
            Debug.LogWarning("KiaCeedSportswagon driver seat anchor is missing from its player prefab.");

        clone.AddComponent<KiaCeedSportswagonAmbientTrafficAppearance>();
        clone.AddComponent<KiaCeedSportswagonAmbientTrafficLighting>();
        clone.AddComponent<KiaCeedSportswagonAmbientTrafficDriver>();
        if (!FitAiBodyColliders(clone, playerPrefab))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        if (!KiaCeedSportswagonTrafficWheels.Configure(clone, context))
        {
            Debug.LogWarning(
                "KiaCeedSportswagon NPC prefab could not bind all four wheels to their tire meshes; " +
                "traffic prefab was not registered.");
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        var appearance = clone.AddComponent<KiaCeedSportswagonPrivateDriverAppearance>();
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
            Debug.LogWarning("KiaCeedSportswagon NPC body collider source is missing.");
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

        var holder = new GameObject("KiaCeedSportswagon NpcBodyCollider");
        holder.layer = clone.layer;
        holder.transform.SetParent(clone.transform, false);
        holder.transform.localPosition = source.localPosition + NpcBodyOffset;
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
            Debug.LogWarning("KiaCeedSportswagon NPC body colliders have invalid bounds.");
            UnityEngine.Object.Destroy(holder);
            return false;
        }

        var trafficVehicle = clone.GetComponent<VehicleComponent>();
        if (trafficVehicle == null || VehicleCollidersField == null)
        {
            Debug.LogWarning("KiaCeedSportswagon NPC native collision bindings are unavailable.");
            return false;
        }

        // Gley caches these references in the authored template. Replacing its
        // body colliders must also replace that cache, including GetCollider's
        // primary collider, while preserving active traffic triggers and wheels.
        var ownedColliders = new List<Collider>(holder.GetComponents<BoxCollider>());
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
            if (collider.enabled && !ownedColliders.Contains(collider))
                ownedColliders.Add(collider);
        VehicleCollidersField.SetValue(trafficVehicle, ownedColliders.ToArray());

        FitAiNavigationObstacles(clone, left, right, rear, front);
        KiaCeedSportswagonDiagnostics.TrafficInfo($"KiaCeedSportswagon NPC body boxes={holder.GetComponents<BoxCollider>().Length} " +
            $"nativeColliders={ownedColliders.Count} " +
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
                Debug.LogWarning($"KiaCeedSportswagon NPC obstacle could not be fitted name='{obstacle.name}'.");
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
                Debug.LogWarning($"KiaCeedSportswagon NPC obstacle has invalid fitted length name='{obstacle.name}'.");
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
            KiaCeedSportswagonDiagnostics.TrafficInfo($"KiaCeedSportswagon NPC obstacle name='{obstacle.name}' " +
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


internal sealed class KiaCeedSportswagonAmbientTrafficLighting : MonoBehaviour
{
    private void Start()
    {
        var lighting = GetComponent<KiaCeedSportswagonLightingController>() ??
                       gameObject.AddComponent<KiaCeedSportswagonLightingController>();
        lighting.InitializeForAmbientTraffic(KiaCeedSportswagonPrivateDriverSupport.Context);
    }
}

internal sealed class KiaCeedSportswagonAmbientTrafficDriver : MonoBehaviour
{
    private void Start()
    {
        var driver = GetComponent<KiaCeedSportswagonDriverController>() ??
                     gameObject.AddComponent<KiaCeedSportswagonDriverController>();
        driver.InitializeForAmbientTraffic(KiaCeedSportswagonPrivateDriverSupport.Context);
    }
}

[DefaultExecutionOrder(1001)]
internal sealed class KiaCeedSportswagonAmbientTrafficAppearance : MonoBehaviour
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
        if (!KiaCeedSportswagonDiagnostics.TrafficEnabled || stateLogs >= 12)
            return;
        var moving = (transform.position - lastPosition).sqrMagnitude > 0.0004f;
        lastPosition = transform.position;
        var obstacleEnabled = obstacle != null && obstacle.enabled;
        if (lastMoving == moving && lastObstacleEnabled == obstacleEnabled)
            return;
        lastMoving = moving;
        lastObstacleEnabled = obstacleEnabled;
        stateLogs++;
        KiaCeedSportswagonDiagnostics.TrafficInfo(
            $"KiaCeedSportswagon NPC id={GetInstanceID()} moving={moving} " +
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
                var paint = GetComponent<KiaCeedSportswagonPaintController>();
                if (paint == null)
                    paint = gameObject.AddComponent<KiaCeedSportswagonPaintController>();
                paint.InitializeForPrivateDriver(color.name, color);
                applied = paint.HasAppliedColor;
            }
            if (colorLogs++ < 8)
                KiaCeedSportswagonDiagnostics.TrafficInfo($"KiaCeedSportswagon ambient NPC id={GetInstanceID()} " +
                    $"color='{(color != null ? color.name : "<none>")}' applied={applied}.");
            if (color != null && !applied && colorLogs <= 8)
                Debug.LogWarning($"KiaCeedSportswagon ambient NPC id={GetInstanceID()} failed to apply '{color.name}'.");
        }
        initializationCoroutine = null;
    }
}

[DefaultExecutionOrder(1000)]
internal sealed class KiaCeedSportswagonPrivateDriverAppearance : MonoBehaviour
{
    private const int InitializationFrameLimit = 4;

    private static readonly PropertyInfo? CurrentVehicleProperty =
        typeof(Player.HUD.SmartphoneUI.SmartphonePrivateDriverUI).GetProperty(
            "CurrentVehicle",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private readonly List<CaliperBinding> caliperBindings = new List<CaliperBinding>(4);
    private bool caliperVisualsBound;
    private Coroutine? initializationCoroutine;
    private Coroutine? departureCheckCoroutine;
    private PrivateDriverVehicle? privateDriver;
    private VehicleComponent? trafficVehicle;
    private bool trafficEventsSubscribed;

    internal void BindCaliperVisuals()
    {
        if (caliperVisualsBound)
            return;

        caliperBindings.Clear();
        BindCaliper("KiaCeedSportswagonFixedCaliperFrontLeft", "KiaCeedSportswagonWheelFrontLeft");
        BindCaliper("KiaCeedSportswagonFixedCaliperFrontRight", "KiaCeedSportswagonWheelFrontRight");
        BindCaliper("KiaCeedSportswagonFixedCaliperRearLeft", "KiaCeedSportswagonWheelRearLeft");
        BindCaliper("KiaCeedSportswagonFixedCaliperRearRight", "KiaCeedSportswagonWheelRearRight");
        caliperVisualsBound = caliperBindings.Count == 4;
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
        for (var frame = 0; frame < InitializationFrameLimit; frame++)
        {
            var candidate = GetComponent<PrivateDriverVehicle>();
            if (candidate != null && candidate.vehicleInstance != null)
            {
                privateDriver = candidate;
                trafficVehicle = GetComponent<VehicleComponent>();
                SubscribeTrafficEvents();
                break;
            }

            yield return null;
        }

        if (privateDriver?.vehicleInstance != null)
        {
            var colorName = privateDriver.vehicleInstance.vehicleColorName;
            var paintApplied = RestoreSavedVehicleColor(false);
            for (var pass = 1; pass < InitializationFrameLimit; pass++)
            {
                yield return new WaitForEndOfFrame();
                paintApplied = RestoreSavedVehicleColor(
                    pass == InitializationFrameLimit - 1);
            }

            KiaCeedSportswagonPrivateDriverSupport.ReportAppearanceResult(
                colorName,
                paintApplied);
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
                KiaCeedSportswagonPrivateDriverSupport.ReportDeparturePaintResult(
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
        if (KiaCeedSportswagonPrivateDriverSupport.TryResolveDriverColor(
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

        var paint = GetComponent<KiaCeedSportswagonPaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<KiaCeedSportswagonPaintController>();
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

internal static class KiaCeedSportswagonTrafficWheels
{
    private const string HubPrefix = "KiaCeedSportswagonNpcHub_";

    internal static bool Configure(GameObject root, ModContext? modContext)
    {
        var vehicle = root.GetComponent<VehicleComponent>();
        if (vehicle == null || vehicle.allWheels == null || vehicle.allWheels.Length != 4)
            return Fail(modContext, "native four-wheel array unavailable");

        var plans = new List<WheelPlan>(4);
        var corners = new HashSet<string>(StringComparer.Ordinal);
        var alreadyBound = 0;
        foreach (var wheel in vehicle.allWheels)
        {
            if (wheel?.wheelGraphics == null || wheel.wheelTransform == null ||
                (wheel.wheelPosition != Wheel.WheelPosition.Front &&
                 wheel.wheelPosition != Wheel.WheelPosition.Back))
            {
                return Fail(modContext, "native wheel transforms/axle unavailable");
            }

            var front = wheel.wheelPosition == Wheel.WheelPosition.Front;
            var left = root.transform.InverseTransformPoint(wheel.wheelGraphics.position).x < 0f;
            var corner = (front ? "Front" : "Rear") + (left ? "Left" : "Right");
            if (!corners.Add(corner))
                return Fail(modContext, "duplicate native corner " + corner);
            if (wheel.wheelGraphics.name == HubPrefix + corner)
            {
                alreadyBound++;
                continue;
            }

            var visual = Find(root.transform, "KiaCeedSportswagonWheel" + corner);
            if (visual == null || !TryBounds(root.transform, visual, out var bounds))
                return Fail(modContext, "wheel geometry unavailable: " + corner);
            var radius = bounds.extents.y;
            if (radius < 0.25f || radius > 0.45f)
                return Fail(modContext, $"invalid measured radius {radius:0.00000}: {corner}");
            plans.Add(new WheelPlan(wheel, visual, corner, bounds.center, radius));
        }

        if (alreadyBound == 4)
            return true;
        if (alreadyBound != 0 || plans.Count != 4)
            return Fail(modContext, "partially bound native wheel rig");

        var frontZ = 0f;
        var rearZ = 0f;
        foreach (var plan in plans)
        {
            var hub = new GameObject(HubPrefix + plan.Corner).transform;
            hub.SetParent(root.transform, false);
            hub.localPosition = plan.Center;
            // Gley owns a centered hub and moves that hub from its native wheel
            // transform. Parenting the authored tire with worldPositionStays
            // preserves the Kia mesh orientation, scale and true axle center.
            plan.Visual.SetParent(hub, true);
            plan.Wheel.wheelGraphics = hub;

            // Match the tire center along the axles while preserving the native
            // vertical suspension origin. The donor/Gley Y value controls ride height.
            var origin = root.transform.InverseTransformPoint(plan.Wheel.wheelTransform.position);
            origin.x = plan.Center.x;
            origin.z = plan.Center.z;
            plan.Wheel.wheelTransform.position = root.transform.TransformPoint(origin);
            plan.Wheel.wheelRadius = plan.Radius;
            plan.Wheel.wheelCircumference = 2f * Mathf.PI * plan.Radius;
            plan.Wheel.raycastLength = plan.Radius + plan.Wheel.maxSuspension;

            if (plan.Wheel.wheelPosition == Wheel.WheelPosition.Front)
                frontZ += plan.Center.z;
            else
                rearZ += plan.Center.z;
            KiaCeedSportswagonDiagnostics.TrafficInfo(
                $"KiaCeedSportswagon NPC wheel corner={plan.Corner} center={plan.Center} " +
                $"radius={plan.Radius:0.00000} origin={origin} graphics='{hub.name}'.");
        }

        vehicle.wheelDistance = Mathf.Abs((frontZ - rearZ) * 0.5f);
        return true;
    }

    private static bool Fail(ModContext? modContext, string reason)
    {
        var message = "KiaCeedSportswagon: NPC wheel preparation failed: " + reason;
        if (modContext != null)
            modContext.Logger.Warn(message);
        else
            Debug.LogWarning(message);
        return false;
    }

    private static Transform? Find(Transform root, string name)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(node.name, name, StringComparison.Ordinal))
                return node;
        return null;
    }

    private static bool TryBounds(Transform root, Transform visual, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;

            var local = filter.sharedMesh.bounds;
            for (var index = 0; index < 8; index++)
            {
                var point = local.center + Vector3.Scale(
                    local.extents,
                    new Vector3(
                        (index & 1) == 0 ? -1f : 1f,
                        (index & 2) == 0 ? -1f : 1f,
                        (index & 4) == 0 ? -1f : 1f));
                point = root.InverseTransformPoint(filter.transform.TransformPoint(point));
                if (found)
                    bounds.Encapsulate(point);
                else
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
            }
        }

        return found;
    }

    private readonly struct WheelPlan
    {
        internal WheelPlan(Wheel wheel, Transform visual, string corner, Vector3 center, float radius)
        {
            Wheel = wheel;
            Visual = visual;
            Corner = corner;
            Center = center;
            Radius = radius;
        }

        internal readonly Wheel Wheel;
        internal readonly Transform Visual;
        internal readonly string Corner;
        internal readonly Vector3 Center;
        internal readonly float Radius;
    }
}
