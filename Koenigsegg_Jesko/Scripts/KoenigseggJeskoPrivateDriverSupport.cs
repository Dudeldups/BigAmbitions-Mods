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

internal static class KoenigseggJeskoPrivateDriverSupport
{
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/koenigseggjesko.prefab";
    private const string AiCarTypeName = "KoenigseggJeskoPrivateDriver";
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

    internal static void ReportCollisionDiagnostic(string message) =>
        KoenigseggJeskoDiagnostics.CollisionInfo(context, message);

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
        KoenigseggJeskoDiagnostics.Info(context,
            $"KoenigseggJesko private-driver traffic type registered name='{AiCarTypeName}' " +
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

        foreach (var color in CapturedVehicleColors.Values)
            if (color != null)
                UnityEngine.Object.Destroy(color);
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
            $"KoenigseggJesko private-driver appearance color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            KoenigseggJeskoDiagnostics.Info(context, message);
        else
            context?.Logger.Warn(message);
    }

    internal static void ReportDeparturePaintResult(string? colorName, bool paintApplied)
    {
        var message =
            $"KoenigseggJesko private-driver departure color='{colorName ?? "<none>"}' " +
            $"paintApplied={paintApplied}.";
        if (paintApplied)
            KoenigseggJeskoDiagnostics.Info(context, message);
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

        clone.name = "KoenigseggJeskoPrivateDriver";
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(clone);
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        // The Anselmo traffic template's body collider is centered for its own
        // body. Keeping it makes the Jesko solid ahead of its visible nose and
        // leaves the rear penetrable. Use the Jesko's own body volumes instead.
        var playerBody = FindTransform(playerPrefab.transform, "BodyCollider");
        var playerBodyBoxes = playerBody?.GetComponents<BoxCollider>();
        if (playerBody == null || playerBodyBoxes == null || playerBodyBoxes.Length < 3)
        {
            context?.Logger.Warn("KoenigseggJesko private-driver prefab missing fitted body colliders.");
            UnityEngine.Object.Destroy(clone);
            return null;
        }

        var disabledTemplateColliders = 0;
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
        {
            if (!collider.enabled || collider.isTrigger || collider is WheelCollider)
                continue;
            var box = collider as BoxCollider;
            KoenigseggJeskoDiagnostics.CollisionInfo(context,
                $"KoenigseggJesko NPC template collider disabled name='{collider.name}' " +
                $"type={collider.GetType().Name} " +
                $"localPosition={clone.transform.InverseTransformPoint(collider.transform.position)} " +
                $"boxCenter={(box != null ? box.center.ToString() : "n/a")} " +
                $"boxSize={(box != null ? box.size.ToString() : "n/a")}.");
            collider.enabled = false;
            disabledTemplateColliders++;
        }

        var npcBody = UnityEngine.Object.Instantiate(playerBody.gameObject, clone.transform, false);
        npcBody.name = "KoenigseggJeskoNpcBodyCollider";
        SetLayerRecursively(npcBody.transform, clone.layer);
        var npcBodyBoxes = npcBody.GetComponents<BoxCollider>();
        npcBodyBoxes[0].center = new Vector3(0f, 0.38f, -0.02f);
        npcBodyBoxes[0].size = new Vector3(1.96f, 0.46f, 4.82f);
        npcBodyBoxes[1].center = new Vector3(0f, 0.78f, -0.18f);
        npcBodyBoxes[1].size = new Vector3(1.72f, 0.62f, 2.62f);
        var bodyRear = float.PositiveInfinity;
        var bodyFront = float.NegativeInfinity;
        foreach (var box in npcBodyBoxes)
        {
            var rear = clone.transform.InverseTransformPoint(
                box.transform.TransformPoint(box.center - Vector3.forward * box.size.z * 0.5f)).z;
            var front = clone.transform.InverseTransformPoint(
                box.transform.TransformPoint(box.center + Vector3.forward * box.size.z * 0.5f)).z;
            bodyRear = Mathf.Min(bodyRear, rear);
            bodyFront = Mathf.Max(bodyFront, front);
        }
        FitAiNavigationObstacles(clone, bodyRear, bodyFront);
        KoenigseggJeskoDiagnostics.CollisionInfo(context,
            $"KoenigseggJesko NPC body fitted disabledTemplateColliders={disabledTemplateColliders} " +
            $"localPosition={npcBody.transform.localPosition} boxes={npcBodyBoxes.Length} " +
            $"frontLocalZ={bodyFront:0.00} rearLocalZ={bodyRear:0.00}.");

        var requiredVisuals = new[]
        {
            "KoenigseggVisual",
            "KoenigseggWheelFrontLeft",
            "KoenigseggWheelFrontRight",
            "KoenigseggWheelRearLeft",
            "KoenigseggWheelRearRight",
            "KoenigseggFixedCaliperFrontLeft",
            "KoenigseggFixedCaliperFrontRight",
            "KoenigseggFixedCaliperRearLeft",
            "KoenigseggFixedCaliperRearRight",
        };
        foreach (var visualName in requiredVisuals)
        {
            var source = FindTransform(playerPrefab.transform, visualName);
            if (source == null)
            {
                context?.Logger.Warn(
                    $"KoenigseggJesko private-driver prefab source missing visual='{visualName}'.");
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

        KoenigseggJeskoMaterials.FixSolidMaterials(clone);
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
            Debug.LogWarning("KoenigseggJesko NPC driver seat source is missing.");

        clone.AddComponent<KoenigseggJeskoAmbientTrafficAppearance>();
        clone.AddComponent<KoenigseggJeskoAmbientTrafficLighting>();
        clone.AddComponent<KoenigseggJeskoAmbientTrafficDriver>();
        var appearance = clone.AddComponent<KoenigseggJeskoPrivateDriverAppearance>();
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

        KoenigseggJeskoDiagnostics.Info(context,
            "KoenigseggJesko private-driver prefab prepared with in-place body, " +
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
            if (string.Equals(filter.name, "KoenigseggDamageBody", StringComparison.Ordinal))
            {
                damageFilter = filter;
                damageRenderer = filter.GetComponent<MeshRenderer>();
            }

            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer != null &&
                filter.name.IndexOf("BODY_mm_ext", StringComparison.OrdinalIgnoreCase) >= 0 &&
                filter.name.IndexOf("BONNETCAM", StringComparison.OrdinalIgnoreCase) < 0)
            {
                sourceFilter = filter;
                sourceRenderer = renderer;
            }
        }

        if (sourceFilter == null || sourceRenderer == null || sourceFilter.sharedMesh == null)
        {
            context?.Logger.Warn(
                "KoenigseggJesko private-driver body shell repair skipped: " +
                "normal BODY_mm_ext source is missing.");
            return;
        }
        sourceRenderer.enabled = sourceRenderer.sharedMaterials.Length > 0;
        if (damageRenderer != null)
            damageRenderer.enabled = false;
    }

    private static void FitAiNavigationObstacles(GameObject clone, float bodyRear, float bodyFront)
    {
        // Pedestrians stop at the navigation obstacle plus their own clearance.
        // Fit both ends because the Anselmo template is longitudinally offset
        // from the Jesko, even though the new physical body boxes are centered.
        const float pedestrianClearance = 0.40f;
        var root = clone.transform;
        foreach (var obstacle in clone.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle.shape != NavMeshObstacleShape.Box ||
                Vector3.Dot(root.forward, obstacle.transform.forward) < 0.99f)
            {
                context?.Logger.Warn(
                    $"KoenigseggJesko NPC navigation obstacle not fitted " +
                    $"name='{obstacle.name}' shape={obstacle.shape}.");
                continue;
            }

            var oldRearLocal = obstacle.center.z - obstacle.size.z * 0.5f;
            var oldFrontLocal = obstacle.center.z + obstacle.size.z * 0.5f;
            var oldRear = root.InverseTransformPoint(obstacle.transform.TransformPoint(
                new Vector3(obstacle.center.x, obstacle.center.y, oldRearLocal))).z;
            var oldFront = root.InverseTransformPoint(obstacle.transform.TransformPoint(
                new Vector3(obstacle.center.x, obstacle.center.y, oldFrontLocal))).z;
            var targetRear = bodyRear + pedestrianClearance;
            var targetFront = bodyFront - pedestrianClearance;
            var newRearLocal = obstacle.transform.InverseTransformPoint(root.TransformPoint(
                new Vector3(0f, 0f, targetRear))).z;
            var newFrontLocal = obstacle.transform.InverseTransformPoint(root.TransformPoint(
                new Vector3(0f, 0f, targetFront))).z;
            if (newFrontLocal <= newRearLocal + 0.05f)
            {
                context?.Logger.Warn(
                    $"KoenigseggJesko NPC navigation obstacle fit invalid " +
                    $"name='{obstacle.name}' rear={targetRear:0.000} front={targetFront:0.000}.");
                continue;
            }

            var center = obstacle.center;
            center.z = (newRearLocal + newFrontLocal) * 0.5f;
            var size = obstacle.size;
            size.z = newFrontLocal - newRearLocal;
            obstacle.center = center;
            obstacle.size = size;
            KoenigseggJeskoDiagnostics.CollisionInfo(context,
                $"KoenigseggJesko NPC navigation obstacle fitted name='{obstacle.name}' " +
                $"oldRear={oldRear:0.000} oldFront={oldFront:0.000} " +
                $"newRear={targetRear:0.000} newFront={targetFront:0.000} " +
                $"bodyRear={bodyRear:0.000} bodyFront={bodyFront:0.000}.");
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

internal sealed class KoenigseggJeskoAmbientTrafficLighting : MonoBehaviour
{
    private void Start()
    {
        var lighting = GetComponent<KoenigseggJeskoLightingController>() ??
                       gameObject.AddComponent<KoenigseggJeskoLightingController>();
        lighting.InitializeForAmbientTraffic(KoenigseggJeskoPrivateDriverSupport.Context);
    }
}

internal sealed class KoenigseggJeskoAmbientTrafficDriver : MonoBehaviour
{
    private void Start()
    {
        var driver = GetComponent<KoenigseggJeskoDriverController>() ??
                     gameObject.AddComponent<KoenigseggJeskoDriverController>();
        driver.InitializeForAmbientTraffic(KoenigseggJeskoPrivateDriverSupport.Context);
    }
}

[DefaultExecutionOrder(1001)]
internal sealed class KoenigseggJeskoAmbientTrafficAppearance : MonoBehaviour
{
    private const int NativeColorAssignmentFrameLimit = 4;
    private static int activeCount;
    private static int performanceTransitionsLogged;
    private Coroutine? initializationCoroutine;
    private int colorLogs;
    private bool counted;

    internal static int ActiveCount => activeCount;

    private void OnEnable()
    {
        if (!counted)
        {
            counted = true;
            activeCount++;
            LogPerformanceTransition("enabled");
        }
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = StartCoroutine(ApplyNativeTrafficColor());
    }

    private void OnDisable()
    {
        if (counted)
        {
            counted = false;
            activeCount = Math.Max(0, activeCount - 1);
            LogPerformanceTransition("disabled");
        }
        if (initializationCoroutine != null)
            StopCoroutine(initializationCoroutine);
        initializationCoroutine = null;
    }

    private void LogPerformanceTransition(string state)
    {
        if (performanceTransitionsLogged >= 40 ||
            !KoenigseggJeskoDiagnostics.DebugEnabled ||
            !KoenigseggJeskoDiagnostics.PerformanceDebugEnabled)
            return;
        performanceTransitionsLogged++;
        KoenigseggJeskoDiagnostics.PerformanceInfo(
            KoenigseggJeskoPrivateDriverSupport.Context,
            $"Jesko performance npc vehicle={GetInstanceID()} state={state} " +
            $"active={activeCount} time={Time.realtimeSinceStartup:0.000}s.");
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
            var liveColor = GetComponent<CarFeatures>()?.VehicleColor;
            var paintApplied = false;
            if (liveColor != null)
            {
                var paint = GetComponent<KoenigseggJeskoPaintController>();
                if (paint == null)
                    paint = gameObject.AddComponent<KoenigseggJeskoPaintController>();
                paint.InitializeForAmbientTraffic(liveColor);
                paintApplied = paint.HasAppliedColor;
            }

            if (colorLogs++ < 8)
                KoenigseggJeskoDiagnostics.NpcColorInfo(
                    $"KoenigseggJesko ambient NPC id={GetInstanceID()} " +
                    $"trafficColor='{(liveColor != null ? liveColor.name : "<none>")}' " +
                    $"paintApplied={paintApplied}.");
            if (liveColor != null && !paintApplied)
                KoenigseggJeskoDiagnostics.Warn(
                    $"KoenigseggJesko ambient NPC id={GetInstanceID()} " +
                    $"failed to apply traffic color='{liveColor.name}'.");
        }
        initializationCoroutine = null;
    }
}

[DefaultExecutionOrder(1000)]
internal sealed class KoenigseggJeskoPrivateDriverAppearance : MonoBehaviour
{
    private const int InitializationFrameLimit = 4;
    private static readonly PropertyInfo? CurrentVehicleProperty =
        typeof(Player.HUD.SmartphoneUI.SmartphonePrivateDriverUI).GetProperty(
            "CurrentVehicle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly string[,] WheelNames =
    {
        { "KoenigseggWheelFrontLeft", "FL" },
        { "KoenigseggWheelFrontRight", "FR" },
        { "KoenigseggWheelRearLeft", "BL" },
        { "KoenigseggWheelRearRight", "BR" },
    };

    private readonly List<WheelBinding> wheelBindings = new List<WheelBinding>(4);
    private readonly List<CaliperBinding> caliperBindings = new List<CaliperBinding>(4);
    private bool wheelVisualsBound;
    private Coroutine? initializationCoroutine;
    private Coroutine? departureCheckCoroutine;
    private PrivateDriverVehicle? privateDriver;
    private VehicleComponent? trafficVehicle;
    private bool trafficEventsSubscribed;
    private MeshCollider? templateBodyCollider;
    private BoxCollider? npcBodyCollider;
    private NavMeshObstacle? npcObstacle;
    private Transform? npcVisual;
    private Vector3 previousPosition;
    private bool previousMoving;
    private bool previousTemplateEnabled;
    private bool previousObstacleEnabled;
    private Vector3 previousTemplateLocalPosition;
    private Vector3 previousVisualLocalPosition;
    private int collisionStateLogs;
    private int playerContactLogs;

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
        BindCaliper("KoenigseggFixedCaliperFrontLeft", "KoenigseggWheelFrontLeft");
        BindCaliper("KoenigseggFixedCaliperFrontRight", "KoenigseggWheelFrontRight");
        BindCaliper("KoenigseggFixedCaliperRearLeft", "KoenigseggWheelRearLeft");
        BindCaliper("KoenigseggFixedCaliperRearRight", "KoenigseggWheelRearRight");
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
        templateBodyCollider = FindTransform(transform, "BodyCollider")?.GetComponent<MeshCollider>();
        npcBodyCollider = FindTransform(transform, "KoenigseggJeskoNpcBodyCollider")?.GetComponent<BoxCollider>();
        npcObstacle = GetComponentInChildren<NavMeshObstacle>(true);
        npcVisual = FindTransform(transform, "KoenigseggVisual");
        previousPosition = transform.position;
        previousMoving = false;
        previousTemplateEnabled = templateBodyCollider != null && templateBodyCollider.enabled;
        previousObstacleEnabled = npcObstacle != null && npcObstacle.enabled;
        previousTemplateLocalPosition = templateBodyCollider != null
            ? templateBodyCollider.transform.localPosition : Vector3.zero;
        previousVisualLocalPosition = npcVisual != null ? npcVisual.localPosition : Vector3.zero;
        LogCollisionState("spawn", 0f);
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
                KoenigseggJeskoPrivateDriverSupport.ReportAppearanceResult(
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
                KoenigseggJeskoPrivateDriverSupport.ReportDeparturePaintResult(
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
            KoenigseggJeskoPrivateDriverSupport.TryResolveDriverColor(
                colorName, features?.VehicleColor, out savedColor) &&
            savedColor != null;
        if (restoredBaseColor)
            features?.SetColor(savedColor);
        if (!applySpecializedPaint)
            return restoredBaseColor;

        var paint = GetComponent<KoenigseggJeskoPaintController>();
        if (paint == null)
            paint = gameObject.AddComponent<KoenigseggJeskoPaintController>();
        paint.InitializeForPrivateDriver(colorName, savedColor);
        return paint.HasAppliedColor;
    }

    private void LateUpdate()
    {
        foreach (var binding in wheelBindings)
            binding.Apply(transform);
        foreach (var binding in caliperBindings)
            binding.Apply(transform);
        ObserveCollisionState();
    }


    private void ObserveCollisionState()
    {
        if (!KoenigseggJeskoDiagnostics.DebugEnabled ||
            !KoenigseggJeskoDiagnostics.CollisionDebugEnabled || collisionStateLogs >= 12)
            return;
        var speed = Time.deltaTime > 0f
            ? Vector3.Distance(transform.position, previousPosition) / Time.deltaTime
            : 0f;
        previousPosition = transform.position;
        var moving = speed > 0.5f || (previousMoving && speed >= 0.05f);
        var templateEnabled = templateBodyCollider != null && templateBodyCollider.enabled;
        var obstacleEnabled = npcObstacle != null && npcObstacle.enabled;
        var templatePosition = templateBodyCollider != null
            ? templateBodyCollider.transform.localPosition : Vector3.zero;
        var visualPosition = npcVisual != null ? npcVisual.localPosition : Vector3.zero;
        if (moving == previousMoving && templateEnabled == previousTemplateEnabled &&
            obstacleEnabled == previousObstacleEnabled &&
            Vector3.Distance(templatePosition, previousTemplateLocalPosition) < 0.05f &&
            Vector3.Distance(visualPosition, previousVisualLocalPosition) < 0.05f)
            return;
        previousMoving = moving;
        previousTemplateEnabled = templateEnabled;
        previousObstacleEnabled = obstacleEnabled;
        previousTemplateLocalPosition = templatePosition;
        previousVisualLocalPosition = visualPosition;
        LogCollisionState(moving ? "moving" : "stopped", speed);
    }

    private void LogCollisionState(string reason, float speed)
    {
        if (!KoenigseggJeskoDiagnostics.DebugEnabled ||
            !KoenigseggJeskoDiagnostics.CollisionDebugEnabled || collisionStateLogs++ >= 12)
            return;
        var obstacleRear = npcObstacle != null
            ? transform.InverseTransformPoint(npcObstacle.transform.TransformPoint(
                npcObstacle.center - Vector3.forward * npcObstacle.size.z * 0.5f)).z
            : float.NaN;
        var obstacleFront = npcObstacle != null
            ? transform.InverseTransformPoint(npcObstacle.transform.TransformPoint(
                npcObstacle.center + Vector3.forward * npcObstacle.size.z * 0.5f)).z
            : float.NaN;
        KoenigseggJeskoPrivateDriverSupport.ReportCollisionDiagnostic(
            $"KoenigseggJesko NPC collision state instance={GetInstanceID()} " +
            $"reason={reason} speed={speed:0.00}mps " +
            $"templateEnabled={templateBodyCollider?.enabled} " +
            $"templateLocalPosition={templateBodyCollider?.transform.localPosition} " +
            $"npcBodyEnabled={npcBodyCollider?.enabled} " +
            $"npcBodyLocalPosition={npcBodyCollider?.transform.localPosition} " +
            $"visualLocalPosition={npcVisual?.localPosition} " +
            $"obstacleEnabled={npcObstacle?.enabled} " +
            $"obstacleRear={obstacleRear:0.00} obstacleFront={obstacleFront:0.00}.");
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!KoenigseggJeskoDiagnostics.DebugEnabled ||
            !KoenigseggJeskoDiagnostics.CollisionDebugEnabled ||
            collision.contactCount == 0 || collision.collider.name != "Player" ||
            playerContactLogs++ >= 8)
            return;
        var contact = collision.GetContact(0);
        KoenigseggJeskoPrivateDriverSupport.ReportCollisionDiagnostic(
            $"KoenigseggJesko NPC player contact instance={GetInstanceID()} " +
            $"self='{contact.thisCollider?.name}' selfId={contact.thisCollider?.GetInstanceID()} " +
            $"localPoint={transform.InverseTransformPoint(contact.point)} " +
            $"templateEnabled={templateBodyCollider?.enabled} speed={collision.relativeVelocity.magnitude:0.00}mps.");
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
