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

internal static class DodgeChallenger2018PrivateDriverSupport
{
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/dodgechallenger2018.prefab";
    private const string AiCarTypeName = "DodgeChallenger2018PrivateDriver";
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
            $"DodgeChallenger2018: chauffeur appearance color='{colorName ?? "<none>"}', " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            DodgeChallenger2018Diagnostics.Info(context, message);
        else
            context?.Logger.Warn(message);
    }

    internal static void ReportDeparturePaintResult(
        string? colorName,
        bool paintApplied)
    {
        var message =
            $"DodgeChallenger2018: chauffeur departure paint color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            DodgeChallenger2018Diagnostics.Info(context, message);
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

        clone.name = "DodgeChallenger2018PrivateDriver";
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(clone);

        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        var requiredVisuals = new[]
        {
            "DodgeVisual",
            "DodgeDamageBody",
            "DodgeWheelFrontLeft",
            "DodgeWheelFrontRight",
            "DodgeWheelRearLeft",
            "DodgeWheelRearRight",
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

        // Challenger lamp overlays are generated as root-level prefab children,
        // not inside DodgeVisual. Copy them explicitly so the ambient lighting
        // controller can drive the same meshes as the player vehicle.
        for (var childIndex = 0; childIndex < playerPrefab.transform.childCount; childIndex++)
        {
            var source = playerPrefab.transform.GetChild(childIndex);
            if (!source.name.StartsWith("DodgeLamp_", StringComparison.Ordinal))
                continue;
            var lamp = UnityEngine.Object.Instantiate(source.gameObject, clone.transform, false);
            lamp.name = source.name;
            SetLayerRecursively(lamp.transform, clone.layer);
        }

        // Preserve the already tested Challenger steering/seat reference for the
        // random ambient NPC driver. The driver controller applies its existing
        // SeatOffset relative to this anchor.
        var playerSeat = FindTransform(playerPrefab.transform, "DodgeSteeringReference");
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
        {
            Debug.LogWarning("DodgeChallenger2018 NPC driver seat source is missing.");
        }

        DodgeChallenger2018Materials.FixSolidMaterials(clone);
        clone.AddComponent<DodgeChallenger2018AmbientTrafficAppearance>();
        clone.AddComponent<DodgeChallenger2018AmbientTrafficLighting>();
        clone.AddComponent<DodgeChallenger2018AmbientTrafficDriver>();
        if (!FitAiBodyColliders(clone, playerPrefab))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        var appearance = clone.AddComponent<DodgeChallenger2018PrivateDriverAppearance>();
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
            Debug.LogWarning("DodgeChallenger2018 NPC body collider source is missing.");
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

        var holder = new GameObject("DodgeChallenger2018 NpcBodyCollider");
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
            Debug.LogWarning("DodgeChallenger2018 NPC body colliders have invalid bounds.");
            UnityEngine.Object.Destroy(holder);
            return false;
        }

        FitAiNavigationObstacles(clone, left, right, rear, front);
        DodgeChallenger2018Diagnostics.TrafficInfo($"DodgeChallenger2018 NPC body boxes={holder.GetComponents<BoxCollider>().Length} " +
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
                Debug.LogWarning($"DodgeChallenger2018 NPC obstacle could not be fitted name='{obstacle.name}'.");
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
                Debug.LogWarning($"DodgeChallenger2018 NPC obstacle has invalid fitted length name='{obstacle.name}'.");
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
            DodgeChallenger2018Diagnostics.TrafficInfo($"DodgeChallenger2018 NPC obstacle name='{obstacle.name}' " +
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


[DefaultExecutionOrder(1001)]
internal sealed class DodgeChallenger2018AmbientTrafficAppearance : MonoBehaviour
{
    private const int EarlyPaintFrame = 4;
    private const int FallbackLockFrame = 30;
    private Coroutine? initializationCoroutine;
    private Coroutine? lifecycleRestoreCoroutine;
    private VehicleComponent? trafficVehicle;
    private VehicleColor? lockedColor;
    private string lockedColorName = string.Empty;
    private NavMeshObstacle? obstacle;
    private Vector3 lastPosition;
    private bool? lastMoving;
    private bool? lastObstacleEnabled;
    private bool trafficEventsSubscribed;
    private int stateLogs;
    private int colorLogs;

    private void OnEnable()
    {
        obstacle = GetComponentInChildren<NavMeshObstacle>(true);
        trafficVehicle = GetComponent<VehicleComponent>();
        lockedColor = null;
        lockedColorName = string.Empty;
        lastPosition = transform.position;
        lastMoving = null;
        lastObstacleEnabled = null;
        SubscribeTrafficEvents();
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(SettleNativeTrafficColor());
    }

    private void SubscribeTrafficEvents()
    {
        if (trafficEventsSubscribed)
            return;
        DensityEvents.onVehicleAdded += HandleVehicleAdded;
        AIEvents.onVehicleChangedState += HandleVehicleChangedState;
        trafficEventsSubscribed = true;
    }

    private void UnsubscribeTrafficEvents()
    {
        if (!trafficEventsSubscribed)
            return;
        DensityEvents.onVehicleAdded -= HandleVehicleAdded;
        AIEvents.onVehicleChangedState -= HandleVehicleChangedState;
        trafficEventsSubscribed = false;
    }

    private bool IsOwnTrafficVehicle(int vehicleIndex) =>
        trafficVehicle != null &&
        vehicleIndex >= 0 &&
        trafficVehicle.GetIndex() == vehicleIndex;

    private void HandleVehicleAdded(int vehicleIndex)
    {
        if (!IsOwnTrafficVehicle(vehicleIndex) || GetComponent<PrivateDriverVehicle>() != null)
            return;
        ScheduleLifecycleRestore(true, "vehicle-added");
    }

    private void HandleVehicleChangedState(
        int vehicleIndex,
        Collider vehicleCollider,
        SpecialDriveActionTypes action)
    {
        if (!IsOwnTrafficVehicle(vehicleIndex) || GetComponent<PrivateDriverVehicle>() != null)
            return;
        ScheduleLifecycleRestore(false, "state-changed");
    }

    private void ScheduleLifecycleRestore(bool captureLatest, string source)
    {
        if (lifecycleRestoreCoroutine != null)
            StopCoroutine(lifecycleRestoreCoroutine);
        lifecycleRestoreCoroutine = StartCoroutine(
            RestoreAfterTrafficMutation(captureLatest, source));
    }

    private IEnumerator RestoreAfterTrafficMutation(bool captureLatest, string source)
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        if (GetComponent<PrivateDriverVehicle>() == null)
        {
            if (captureLatest || lockedColor == null)
                ApplyCurrentNativeColor(true, source);
            else
                ReapplyLockedColor(source);
        }

        // Some Gley state transitions update CarFeatures/material state one frame
        // after the public event. Reassert once more after that deferred pass.
        yield return new WaitForEndOfFrame();
        if (GetComponent<PrivateDriverVehicle>() == null && lockedColor != null)
            ReapplyLockedColor(source + "-deferred");

        lifecycleRestoreCoroutine = null;
    }

    private IEnumerator SettleNativeTrafficColor()
    {
        for (var frame = 0; frame <= FallbackLockFrame; frame++)
        {
            if (GetComponent<PrivateDriverVehicle>() != null)
            {
                initializationCoroutine = null;
                yield break;
            }

            if (frame == EarlyPaintFrame)
            {
                if (lockedColor != null)
                    ReapplyLockedColor("early-settle-locked");
                else
                    ApplyCurrentNativeColor(false, "early-settle");
            }
            else if (frame == FallbackLockFrame && lockedColor == null)
            {
                ApplyCurrentNativeColor(true, "fallback-lock");
            }

            yield return null;
        }

        initializationCoroutine = null;
    }

    private bool ApplyCurrentNativeColor(bool lockColor, string source)
    {
        var color = GetComponent<CarFeatures>()?.VehicleColor;
        if (color == null)
            return false;

        if (lockColor)
        {
            lockedColor = color;
            lockedColorName = ((UnityEngine.Object)color).name;
        }

        return ApplyColor(color, source);
    }

    private bool ReapplyLockedColor(string source)
    {
        if (lockedColor == null)
            return ApplyCurrentNativeColor(true, source + "-capture");
        return ApplyColor(lockedColor, source);
    }

    private bool ApplyColor(VehicleColor color, string source)
    {
        var features = GetComponent<CarFeatures>();
        features?.SetColor(color);

        var paint = GetComponent<DodgeChallenger2018PaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<DodgeChallenger2018PaintController>();

        var colorName = ((UnityEngine.Object)color).name;
        // InitializeForPrivateDriver intentionally invalidates the paint
        // controller's applied-color cache before applying the property blocks.
        // That is required when Gley has reset renderer state without changing
        // CarFeatures.VehicleColor.
        paint.InitializeForPrivateDriver(colorName, color);
        var applied = paint.HasAppliedColor;

        if (colorLogs++ < 12)
            DodgeChallenger2018Diagnostics.TrafficInfo(
                $"DodgeChallenger2018 ambient NPC id={GetInstanceID()} " +
                $"color='{colorName}' locked='{lockedColorName}' source='{source}' applied={applied}.");
        if (!applied && colorLogs <= 12)
            Debug.LogWarning(
                $"DodgeChallenger2018 ambient NPC id={GetInstanceID()} failed to apply " +
                $"'{colorName}' after '{source}'.");

        return applied;
    }

    private void LateUpdate()
    {
        if (!DodgeChallenger2018Diagnostics.TrafficEnabled || stateLogs >= 12)
            return;
        var moving = (transform.position - lastPosition).sqrMagnitude > 0.0004f;
        lastPosition = transform.position;
        var obstacleEnabled = obstacle != null && obstacle.enabled;
        if (lastMoving == moving && lastObstacleEnabled == obstacleEnabled)
            return;
        lastMoving = moving;
        lastObstacleEnabled = obstacleEnabled;
        stateLogs++;
        DodgeChallenger2018Diagnostics.TrafficInfo(
            $"DodgeChallenger2018 NPC id={GetInstanceID()} moving={moving} " +
            $"standingObstacle={obstacleEnabled} transition={stateLogs}.");
    }

    private void OnDisable()
    {
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        if (lifecycleRestoreCoroutine != null)
            StopCoroutine(lifecycleRestoreCoroutine);
        initializationCoroutine = null;
        lifecycleRestoreCoroutine = null;
        UnsubscribeTrafficEvents();
        trafficVehicle = null;
        lockedColor = null;
        lockedColorName = string.Empty;
    }
}

[DefaultExecutionOrder(1000)]
internal sealed class DodgeChallenger2018PrivateDriverAppearance : MonoBehaviour
{
    private const int InitializationFrameLimit = 4;

    private static readonly PropertyInfo? CurrentVehicleProperty =
        typeof(Player.HUD.SmartphoneUI.SmartphonePrivateDriverUI).GetProperty(
            "CurrentVehicle",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly string[,] WheelNames =
    {
        { "DodgeWheelFrontLeft", "FL" },
        { "DodgeWheelFrontRight", "FR" },
        { "DodgeWheelRearLeft", "BL" },
        { "DodgeWheelRearRight", "BR" },
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

        BindCaliper("DodgeFixedCaliperFrontLeft", "DodgeWheelFrontLeft");
        BindCaliper("DodgeFixedCaliperFrontRight", "DodgeWheelFrontRight");
        BindCaliper("DodgeFixedCaliperRearLeft", "DodgeWheelRearLeft");
        BindCaliper("DodgeFixedCaliperRearRight", "DodgeWheelRearRight");
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

                DodgeChallenger2018PrivateDriverSupport.ReportAppearanceResult(
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
                DodgeChallenger2018PrivateDriverSupport.ReportDeparturePaintResult(
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
        if (DodgeChallenger2018PrivateDriverSupport.TryResolveDriverColor(
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

        var paint = GetComponent<DodgeChallenger2018PaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<DodgeChallenger2018PaintController>();
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


internal sealed class DodgeChallenger2018AmbientTrafficLighting : MonoBehaviour
{
    private void Start()
    {
        var lighting = GetComponent<DodgeChallenger2018LightingController>() ??
                       gameObject.AddComponent<DodgeChallenger2018LightingController>();
        lighting.InitializeForAmbientTraffic(DodgeChallenger2018PrivateDriverSupport.Context);
    }
}

internal sealed class DodgeChallenger2018AmbientTrafficDriver : MonoBehaviour
{
    private void Start()
    {
        var driver = GetComponent<DodgeChallenger2018DriverController>() ??
                     gameObject.AddComponent<DodgeChallenger2018DriverController>();
        driver.InitializeForAmbientTraffic(DodgeChallenger2018PrivateDriverSupport.Context);
    }
}
