#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BAModAPI;
using Buildings.BuildingTypes.Special.PrivateDriverService;
using Data.VehicleColors;
using GleyTrafficSystem;
using Helpers;
using UnityEngine;

/// <summary>
/// Bentley-only private-driver contract, traffic-pool, prefab-cache and saved
/// paint support. Call contract registration after save deserialization and
/// prepare the pool before TrafficManager initialization.
/// </summary>
internal static class BentleyContinentalGTSpeedPrivateDriverSupport
{
    private const string VehicleRepainterColorPreviewEvent = "vehicle-repainter:color-preview";
    private const string AdvancedContractKey = "ba:private_driver_type_advanced";
    private const string PremiumContractKey = "ba:private_driver_type_premium";
    private const string AiTemplateCacheKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string AiPrefabCacheKey = "Prefabs/Vehicles/bentleycontinentalgtspeed.prefab";
    private const string AiCarTypeName = "BentleyContinentalGTSpeedPrivateDriver";
    private const int PrivateDriverPoolSize = 2;
    private const float TrafficBodyLiftMeters = 0.10f;
    internal static ModContext? Context => context;

    // Require both the mod-wide and this feature-specific flag for traces.
    internal static bool DebugEnabled { get; set; } = false;

    private static readonly List<PrivateDriverContract> ModifiedContracts = new List<PrivateDriverContract>(2);
    private static readonly Dictionary<string, VehicleColor> CapturedVehicleColors =
        new Dictionary<string, VehicleColor>(StringComparer.Ordinal);
    private static GameObject? customAiPrefab;
    private static UnityEngine.Object? previousCachedPrefab;
    private static bool previousCacheEntryCaptured;
    private static VehiclePool? modifiedVehiclePool;
    private static CarType? customCarType;
    private static ModContext? context;
    private static bool missingSeatAnchorLogged;

    internal static GameObject? TrafficPrefab => customAiPrefab;
    internal static void SetContext(ModContext modContext) => context = modContext;

    /// <summary>
    /// Binds the generated Bentley model to the native runtime's paint and
    /// vehicle-renderer arrays and validates its body collider. Chauffeur seat
    /// setup is optional and must not prevent player-vehicle registration.
    /// Call on the loaded player prefab before registering or spawning it.
    /// </summary>
    internal static bool ConfigurePlayerPrefab(GameObject? playerPrefab, string source = "runtime")
    {
        if (playerPrefab == null)
            return Fail($"player prefab unavailable source='{source}'");
        var visual = FindTransform(playerPrefab.transform, "BentleyVisual");
        var bodyCollider = FindTransform(playerPrefab.transform, "BodyCollider");
        var vehicleController = playerPrefab.GetComponent<VehicleController>();
        var carFeatures = playerPrefab.GetComponent<CarFeatures>();
        if (visual == null || bodyCollider == null || vehicleController == null || carFeatures == null ||
            bodyCollider.GetComponents<BoxCollider>().Length == 0)
        {
            return Fail($"player prefab references incomplete source='{source}' " +
                        "(BentleyVisual, VehicleController, CarFeatures, or BodyCollider missing)");
        }

        if (!BindRollingWheelVisuals(playerPrefab.transform, out var wheelBindings))
            return Fail($"native rolling wheel visuals could not be bound source='{source}'");

        // Wheel visuals and authored caliper paint pieces are siblings of
        // BentleyVisual after wheel extraction. Bind from this vehicle instance
        // once so native paint includes all eight exact CaliperAZonePaint slots.
        if (!TryGetRendererBindings(playerPrefab.transform, out var allRenderers, out var paintRenderers))
            return Fail($"Bentley renderer discovery failed source='{source}'; " +
                        DescribeRendererCandidates(playerPrefab.transform));
        var vehicleRenderers = GetRenderableRenderers(playerPrefab.transform);
        if (vehicleRenderers.Length > 0)
            allRenderers = vehicleRenderers;
        SetMember(vehicleController, "CarFeatures", carFeatures);
        if (!TryAssignRendererArray(carFeatures, "bodyMeshes", paintRenderers) ||
            !TryAssignRendererArray(vehicleController, "renderers", allRenderers))
        {
            return Fail($"native paint/vehicle renderer arrays could not be bound source='{source}'");
        }
        ApplySavedPlayerColor(vehicleController, carFeatures, source);
        var hasSeatAnchor = EnsureDriverSeatAnchor(playerPrefab, out _);
        if (!hasSeatAnchor && !missingSeatAnchorLogged)
        {
            missingSeatAnchorLogged = true;
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed: private-driver service is unavailable because the supplied model has no " +
                $"derivable driver-seat anchor; player-vehicle setup continues source='{source}'.");
        }

        Trace($"player prefab references ready source='{source}', renderers={allRenderers.Length}, " +
              $"paintRenderers={paintRenderers.Length}, bodyBoxes={bodyCollider.GetComponents<BoxCollider>().Length}, " +
              $"rollingWheelBindings={wheelBindings}, driverSeatAnchor={hasSeatAnchor}.");
        return true;
    }

    internal static bool PrepareTrafficPool(GameObject? playerPrefab)
    {
        if (!ConfigurePlayerPrefab(playerPrefab, "traffic-pool") ||
            !EnsureAiPrefab(playerPrefab!) || customAiPrefab == null)
            return false;
        var pool = TrafficComponent.Instance?.vehiclePool;
        if (pool == null)
            return false;

        var existing = pool.trafficCars ?? Array.Empty<CarType>();
        foreach (var item in existing)
        {
            if (item == null || !ReferenceEquals(item, customCarType))
                continue;
            ApplyCarType(item);
            modifiedVehiclePool = pool;
            return true;
        }
        foreach (var item in existing)
            if (item != null && string.Equals(item.name, AiCarTypeName, StringComparison.Ordinal))
                return Fail("traffic pool contains an unowned Bentley type entry");
        if (TrafficManager.IsInitialized)
            return Fail("pool preparation was requested after TrafficManager initialization");

        customCarType = new CarType();
        ApplyCarType(customCarType);
        var expanded = new CarType[existing.Length + 1];
        Array.Copy(existing, expanded, existing.Length);
        expanded[existing.Length] = customCarType;
        pool.trafficCars = expanded;
        modifiedVehiclePool = pool;
        Trace($"traffic pool entry appended; existing={existing.Length}, total={expanded.Length}.");
        return true;
    }

    internal static bool EnsureVehicleAvailable(string vehicleTypeName)
    {
        if (!string.Equals(vehicleTypeName, BentleyContinentalGTSpeedMod.VehicleTypeName, StringComparison.Ordinal))
            return Fail("contract registration rejected an unexpected vehicle type");
        var contracts = PrivateDriverHelpers.GetContracts();
        if (contracts == null ||
            !contracts.TryGetValue(AdvancedContractKey, out var advanced) ||
            !contracts.TryGetValue(PremiumContractKey, out var premium) ||
            advanced == null || premium == null)
            return false;

        EnsureContractContains(advanced, vehicleTypeName);
        EnsureContractContains(premium, vehicleTypeName);
        var ready = Contains(advanced.usableVehicleTypes, vehicleTypeName) &&
                    Contains(premium.usableVehicleTypes, vehicleTypeName);
        Trace($"contract registration result={ready}; vehicleType='{vehicleTypeName}'.");
        return ready;
    }

    internal static void RemoveVehicle(string vehicleTypeName)
    {
        if (string.Equals(vehicleTypeName, BentleyContinentalGTSpeedMod.VehicleTypeName, StringComparison.Ordinal))
            foreach (var contract in ModifiedContracts)
                RemoveAll(contract.usableVehicleTypes, vehicleTypeName);
        ModifiedContracts.Clear();

        if (modifiedVehiclePool != null && customCarType != null)
        {
            var current = modifiedVehiclePool.trafficCars ?? Array.Empty<CarType>();
            var remaining = new List<CarType>(current.Length);
            foreach (var item in current)
                if (!ReferenceEquals(item, customCarType)) remaining.Add(item);
            modifiedVehiclePool.trafficCars = remaining.ToArray();
        }
        modifiedVehiclePool = null;
        customCarType = null;

        var cache = GetPrefabCache();
        if (cache != null && customAiPrefab != null && cache.Contains(AiPrefabCacheKey) &&
            ReferenceEquals(cache[AiPrefabCacheKey], customAiPrefab))
        {
            if (previousCachedPrefab != null) cache[AiPrefabCacheKey] = previousCachedPrefab;
            else cache.Remove(AiPrefabCacheKey);
        }
        if (customAiPrefab != null) UnityEngine.Object.Destroy(customAiPrefab);
        customAiPrefab = null;
        previousCachedPrefab = null;
        previousCacheEntryCaptured = false;
        foreach (var color in CapturedVehicleColors.Values)
            if (color != null) UnityEngine.Object.Destroy(color);
        CapturedVehicleColors.Clear();
        context = null;
    }

    internal static bool TryResolveDriverColor(
        string? colorName, VehicleColor? liveColor, out VehicleColor? resolvedColor)
    {
        resolvedColor = null;
        if (string.IsNullOrWhiteSpace(colorName)) return false;
        var resolvedName = colorName!;
        if (liveColor != null && string.Equals(liveColor.name, resolvedName, StringComparison.Ordinal))
        {
            resolvedColor = CaptureVehicleColor(resolvedName, liveColor);
            return true;
        }
        if (VehicleHelper.TryGetVehicleColor(resolvedName, out var registered) && registered != null)
        {
            resolvedColor = CaptureVehicleColor(resolvedName, registered);
            return true;
        }
        return CapturedVehicleColors.TryGetValue(resolvedName, out resolvedColor) && resolvedColor != null;
    }

    internal static void ReportAppearanceResult(string? colorName, bool applied) =>
        ReportPaintResult("appearance", colorName, applied);

    internal static void ReportDeparturePaintResult(string? colorName, bool applied) =>
        ReportPaintResult("departure", colorName, applied);

    private static void ReportPaintResult(string operation, string? colorName, bool applied)
    {
        var message = $"BentleyContinentalGTSpeed: private-driver paint {operation} " +
                      $"color='{colorName ?? "<none>"}', applied={applied}.";
        if (applied) Trace(message);
        else context?.Logger.Warn(message);
    }

    private static bool EnsureAiPrefab(GameObject playerPrefab)
    {
        var cache = GetPrefabCache();
        if (cache == null) return Fail("native prefab cache is unavailable");
        if (customAiPrefab == null)
            customAiPrefab = CreateAiPrefab(playerPrefab);
        if (customAiPrefab == null) return false;
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
        var visualSource = FindTransform(playerPrefab.transform, "BentleyVisual");
        var seatSource = FindSeatAnchor(playerPrefab.transform);
        var steeringSource = FindTransform(playerPrefab.transform, BentleyContinentalGTSpeedDriverController.SteeringWheelMarker);
        if (visualSource == null || seatSource == null || steeringSource == null ||
            playerPrefab.GetComponent<CarFeatures>() == null)
        {
            Fail("player prefab lacks BentleyVisual, driver seat/steering anchors or CarFeatures");
            return null;
        }

        // Load the authored prefab. A cached traffic instance can already have shifted
        // its suspension rays during Gley initialization.
        var template = LoadTrafficAsset(AiTemplateCacheKey);
        if (template == null)
        {
            Fail("native Anselmo traffic template is unavailable");
            return null;
        }

        var staging = new GameObject("BentleyTrafficPrefabStaging");
        staging.SetActive(false);
        GameObject clone;
        try
        {
            clone = UnityEngine.Object.Instantiate(template, staging.transform, false);
            clone.SetActive(false);
            clone.transform.SetParent(null, false);
        }
        finally
        {
            UnityEngine.Object.Destroy(staging);
        }
        clone.name = AiCarTypeName;
        clone.hideFlags = HideFlags.DontSave;
        clone.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(clone);
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;

        var visual = UnityEngine.Object.Instantiate(visualSource.gameObject, clone.transform, false);
        visual.name = "BentleyVisual";
        SetLayerRecursively(visual.transform, clone.layer);
        if (!CloneExternalWheelVisuals(playerPrefab.transform, clone.transform))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        var lampSource = playerPrefab.GetComponentInChildren<BentleyContinentalGTSpeedLampRig>(true);
        var trafficLights = clone.GetComponent<VehicleLightsComponent>();
        if (lampSource == null || trafficLights == null)
        {
            UnityEngine.Object.Destroy(clone);
            Fail("authored Bentley lamp rig or native Gley light controller missing");
            return null;
        }
        var lampClone = UnityEngine.Object.Instantiate(lampSource.gameObject, clone.transform, false)
            .GetComponent<BentleyContinentalGTSpeedLampRig>();
        lampClone.name = "BentleyLampEmitters";
        lampClone.emitters.sharedMaterial = lampClone.npcMaterial;
        lampClone.emitters.enabled = true;
        lampClone.playerEmitters = Array.Empty<SkinnedMeshRenderer>();
        foreach (var emitter in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (emitter.name.StartsWith("BentleyLampEmitters_", StringComparison.Ordinal))
                emitter.enabled = false;
        trafficLights.meshRenderer = lampClone.emitters;
        trafficLights.frontLightBrightness = 1f;
        trafficLights.backLightBrightness = 1f;
        trafficLights.brakeLightBrightness = 3f;
        trafficLights.reverseLightBrightness = 1f;
        var npcBeam = typeof(VehicleLightsComponent).GetField("spotlight",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(trafficLights) as Light;
        if (npcBeam != null)
        {
            npcBeam.transform.position = clone.transform.TransformPoint(lampClone.npcBeamPosition);
            npcBeam.transform.rotation = clone.transform.rotation * Quaternion.Euler(12, 0, 0);
            npcBeam.range = 55f; npcBeam.spotAngle = 70f;
        }
        // Gley writes its own native shader-property block and blink phase.
        // The dedicated emitter shader uses that contract without a polling loop.
        var seat = new GameObject("NpcDriverSeatAnchor");
        seat.layer = clone.layer;
        seat.transform.SetParent(clone.transform, false);
        seat.transform.localPosition = playerPrefab.transform.InverseTransformPoint(seatSource.position);
        seat.transform.localRotation = Quaternion.Inverse(playerPrefab.transform.rotation) * seatSource.rotation;

        var nativeVehicle = clone.GetComponent<VehicleComponent>();
        if (nativeVehicle == null || nativeVehicle.allWheels == null || nativeVehicle.allWheels.Length != 4 ||
            nativeVehicle.allWheels.Any(wheel => wheel?.wheelGraphics == null || wheel.wheelTransform == null))
        {
            Fail("native Gley four-wheel chassis is unavailable");
            UnityEngine.Object.Destroy(clone);
            return null;
        }

        // Align the model with the midpoint of the donor axles. Leave Gley's
        // ray origins, graphics transforms, collider hierarchy and inertia intact.
        var stockMidpoint = nativeVehicle.allWheels.Average(
            wheel => clone.transform.InverseTransformPoint(wheel.wheelTransform.position).z);
        var bentleyMidpoint = new[] { "3DWheel Front L", "3DWheel Rear L" }.Average(
            name => clone.transform.Find(name).localPosition.z);
        var visualOffset = new Vector3(0f, 0f, stockMidpoint - bentleyMidpoint);
        var bodyOffset = visualOffset + Vector3.up * TrafficBodyLiftMeters;
        visual.transform.localPosition += bodyOffset;
        lampClone.transform.localPosition += bodyOffset;
        if (npcBeam != null) npcBeam.transform.position += clone.transform.TransformVector(bodyOffset);
        seat.transform.localPosition += bodyOffset;
        var steering = new GameObject(BentleyContinentalGTSpeedDriverController.SteeringWheelMarker).transform;
        steering.SetParent(clone.transform, false);
        steering.localPosition = playerPrefab.transform.InverseTransformPoint(steeringSource.position) + bodyOffset;
        steering.localRotation = Quaternion.Inverse(playerPrefab.transform.rotation) * steeringSource.rotation;
        clone.AddComponent<BentleyContinentalGTSpeedDriverController>().ConfigureForTraffic();

        var wheelPose = clone.AddComponent<BentleyContinentalGTSpeedTrafficWheelPose>();
        if (!wheelPose.Bind(nativeVehicle, visualOffset) || !ConfigureVehicleRenderers(clone, clone.transform))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        Trace($"Bentley NPC visual offset Z={visualOffset.z:F3} m, body lift={TrafficBodyLiftMeters:F3} m, native wheelbase={nativeVehicle.wheelDistance:F3} m.");
        var appearance = clone.AddComponent<BentleyContinentalGTSpeedAppearanceRuntime>();
        if (!appearance.Configure(clone, context, "private-driver-prefab"))
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        foreach (var component in clone.GetComponents<MonoBehaviour>())
        {
            if (component != null && string.Equals(component.GetType().FullName,
                    "GleyTrafficSystem.VehicleComponent", StringComparison.Ordinal))
            {
                SetMember(component, "prefab", clone);
                break;
            }
        }
        var privateDriverAppearance = clone.AddComponent<BentleyContinentalGTSpeedPrivateDriverAppearance>();
        if (!privateDriverAppearance.BindExpectedReferences())
        {
            UnityEngine.Object.Destroy(clone);
            return null;
        }
        Trace("private-driver AI prefab created with target visuals and Gley-owned wheel pose.");
        return clone;
    }

    private static bool CloneExternalWheelVisuals(Transform playerRoot, Transform cloneRoot)
    {
        // ConfigurePlayerPrefab reparents the native rolling roots and their
        // upright caliper roots beside BentleyVisual. Clone those roots too:
        // copying BentleyVisual alone silently drops the extracted meshes.
        var sourceNames = new[]
        {
            "3DWheel Front L", "3DWheel Front R", "3DWheel Rear L", "3DWheel Rear R",
            "FrontLeft_UprightVisual", "FrontRight_UprightVisual",
            "RearLeft_UprightVisual", "RearRight_UprightVisual",
        };
        foreach (var sourceName in sourceNames)
        {
            var source = FindTransform(playerRoot, sourceName);
            if (source == null || source.parent != playerRoot)
                return Fail($"external wheel visual '{sourceName}' is missing or not a vehicle-root sibling");

            var copied = UnityEngine.Object.Instantiate(source.gameObject, cloneRoot, false);
            copied.name = source.gameObject.name;
            copied.transform.localPosition = playerRoot.InverseTransformPoint(source.position);
            copied.transform.localRotation = Quaternion.Inverse(playerRoot.rotation) * source.rotation;
            copied.transform.localScale = source.localScale;
            SetLayerRecursively(copied.transform, cloneRoot.gameObject.layer);
            if (!HasVisibleWheelGeometry(copied.transform))
                return Fail($"cloned wheel visual '{sourceName}' has no enabled mesh triangles and materials");
        }

        return true;
    }

    private static bool HasVisibleWheelGeometry(Transform root) =>
        HasVisibleWheelGeometry(new[] { root });

    private static bool HasVisibleWheelGeometry(IEnumerable<Transform> roots)
    {
        foreach (var root in roots)
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (renderer.enabled && mesh != null && mesh.triangles.Length >= 3 &&
                renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0 &&
                renderer.sharedMaterials.Any(material => material != null))
                return true;
        }

        return false;
    }

    private static Transform? FindSeatAnchor(Transform root) =>
        FindTransform(root, "BentleyDriverSeatAnchor") ??
        FindTransform(root, "NpcDriverSeatAnchor") ??
        FindTransform(root, "DriverSeatBase_107") ??
        FindTransformContaining(root, "steer_3");

    private static bool EnsureDriverSeatAnchor(GameObject prefab, out Transform anchor)
    {
        anchor = FindTransform(prefab.transform, "BentleyDriverSeatAnchor")!;
        if (anchor != null)
            return true;
        var seat = FindTransform(prefab.transform, "DriverSeatBase_107");
        if (seat == null || !TryGetLocalRendererBounds(prefab.transform, seat, out var bounds))
            return false;

        // Seat root transforms are at the imported mesh origin. Derive the
        // anchor from the actual seat geometry instead of using that origin.
        var seatPosition = bounds.center;
        seatPosition.y = bounds.min.y + Mathf.Clamp(bounds.size.y * 0.55f, 0.18f, 0.42f);
        var anchorObject = new GameObject("BentleyDriverSeatAnchor");
        anchorObject.layer = prefab.layer;
        anchorObject.transform.SetParent(prefab.transform, false);
        anchorObject.transform.localPosition = seatPosition;
        anchorObject.transform.localRotation = Quaternion.identity;
        anchor = anchorObject.transform;
        return true;
    }

    private static bool TryGetLocalRendererBounds(Transform root, Transform subtree, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var renderer in subtree.GetComponentsInChildren<Renderer>(true))
        {
            Bounds localBounds;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
                localBounds = skinned.sharedMesh.bounds;
            else if (renderer.GetComponent<MeshFilter>()?.sharedMesh is Mesh mesh)
                localBounds = mesh.bounds;
            else
                continue;
            for (var index = 0; index < 8; index++)
            {
                var sign = new Vector3(
                    (index & 1) == 0 ? -1f : 1f,
                    (index & 2) == 0 ? -1f : 1f,
                    (index & 4) == 0 ? -1f : 1f);
                var point = root.InverseTransformPoint(
                    renderer.transform.TransformPoint(localBounds.center + Vector3.Scale(localBounds.extents, sign)));
                if (found) bounds.Encapsulate(point);
                else { bounds = new Bounds(point, Vector3.zero); found = true; }
            }
        }
        return found && bounds.size.sqrMagnitude > 0.0001f;
    }

    // Native traffic uses Gley VehicleComponent + CarFeatures. The Anselmo AI
    // template has no player VehicleController; its absence must not reject it.
    private static bool ConfigureVehicleRenderers(GameObject root, Transform visualRoot)
    {
        var vehicleController = root.GetComponent<VehicleController>();
        var carFeatures = root.GetComponent<CarFeatures>();
        if (carFeatures == null)
            return Fail("traffic template CarFeatures component unavailable");
        if (!TryGetRendererBindings(visualRoot, out var allRenderers, out var paintRenderers))
            return Fail("traffic template renderer bindings unavailable " +
                        $"(renderers={GetRenderableRenderers(visualRoot).Length}, " +
                        $"paintRenderers=0; {DescribeRendererCandidates(visualRoot)})");
        if (vehicleController != null)
            SetMember(vehicleController, "CarFeatures", carFeatures);
        if (!TryAssignRendererArray(carFeatures, "bodyMeshes", paintRenderers))
            return Fail($"traffic template CarFeatures.bodyMeshes rejected {paintRenderers.Length} paint renderers");
        if (vehicleController != null && !TryAssignRendererArray(vehicleController, "renderers", allRenderers))
            return Fail($"traffic template VehicleController.renderers rejected {allRenderers.Length} renderers");
        Trace($"traffic template renderers configured; total={allRenderers.Length}, paint={paintRenderers.Length}, " +
              $"playerVehicleControllerPresent={vehicleController != null}.");
        return true;
    }

    private static bool TryGetRendererBindings(
        Transform visualRoot, out Renderer[] allRenderers, out Renderer[] paintRenderers)
    {
        var all = GetRenderableRenderers(visualRoot).ToList();
        var paint = new List<Renderer>();
        foreach (var renderer in all)
        {
            if (IsPaintableBentleyRenderer(renderer.transform, visualRoot))
                paint.Add(renderer);
        }
        allRenderers = all.ToArray();
        paintRenderers = paint.ToArray();
        return allRenderers.Length > 0 && paintRenderers.Length > 0;
    }

    private static Renderer[] GetRenderableRenderers(Transform root)
    {
        var renderers = new List<Renderer>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.sharedMaterials == null || renderer.sharedMaterials.Length == 0)
                continue;
            renderers.Add(renderer);
        }
        return renderers.ToArray();
    }

    private static bool IsPaintableBentleyRenderer(Transform candidate, Transform root)
    {
        for (var node = candidate; node != null && node != root; node = node.parent)
        {
            var name = node.name;
            if (name.IndexOf("Paint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("ColourZone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("ColorZone", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        // Imported GLB meshes often use generic Object_* transform names and
        // keep Paint/OEMPaint as material slots instead of named hierarchy
        // nodes. Match the same explicit body-paint materials used by the
        // appearance and damage setup, including Unity's clone suffix.
        var renderer = candidate.GetComponent<Renderer>();
        if (renderer == null || renderer.sharedMaterials == null)
            return false;

        foreach (var material in renderer.sharedMaterials)
        {
            if (IsBentleyBodyPaintMaterial(material))
                return true;
        }
        // Native CarFeatures stores renderers, not material slots. The Bentley
        // GLB gives each painted caliper piece its own one-slot renderer, so
        // include only that exact slot and never its adjacent badge or fixed
        // CaliperAZone material.
        return renderer.sharedMaterials.Length == 1 && renderer.enabled && renderer.gameObject.activeSelf &&
               IsUnderBentleyUpright(renderer.transform) &&
               IsBentleyCaliperPaintMaterial(renderer.sharedMaterials[0]);
    }

    private static bool IsUnderBentleyUpright(Transform transform)
    {
        for (var node = transform.parent; node != null; node = node.parent)
        {
            if (!node.gameObject.activeSelf) return false;
            if (node.name == "FrontLeft_UprightVisual" || node.name == "FrontRight_UprightVisual" ||
                node.name == "RearLeft_UprightVisual" || node.name == "RearRight_UprightVisual")
                return true;
        }
        return false;
    }

    private static bool IsBentleyCaliperPaintMaterial(Material? material)
    {
        if (material == null)
            return false;

        var name = material.name.Trim();
        const string cloneSuffix = "(Clone)";
        const string instanceSuffix = "(Instance)";
        while (name.EndsWith(cloneSuffix, StringComparison.Ordinal) ||
               name.EndsWith(instanceSuffix, StringComparison.Ordinal))
        {
            var suffix = name.EndsWith(cloneSuffix, StringComparison.Ordinal)
                ? cloneSuffix
                : instanceSuffix;
            name = name.Substring(0, name.Length - suffix.Length).TrimEnd();
        }

        return string.Equals(name, "CaliperAZonePaint", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Bentley_CaliperAZonePaint_Prepared", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "BentleyMaterial_003", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBentleyBodyPaintMaterial(Material? material)
    {
        if (material == null)
            return false;

        var name = material.name.Trim();
        const string cloneSuffix = "(Clone)";
        const string instanceSuffix = "(Instance)";
        while (name.EndsWith(cloneSuffix, StringComparison.Ordinal) ||
               name.EndsWith(instanceSuffix, StringComparison.Ordinal))
        {
            var suffix = name.EndsWith(cloneSuffix, StringComparison.Ordinal)
                ? cloneSuffix
                : instanceSuffix;
            name = name.Substring(0, name.Length - suffix.Length).TrimEnd();
        }

        return string.Equals(name, "Paint", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "OEMPaint", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Bentley_Paint_Prepared", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "Bentley_OEMPaint_Prepared", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "BentleyMaterial_026", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "BentleyMaterial_027", StringComparison.OrdinalIgnoreCase);
    }

    private static bool BindRollingWheelVisuals(Transform vehicleRoot, out int boundCount)
    {
        boundCount = 0;
        var bindings = new[]
        {
            (Controller: "FrontLeft_WheelController", Visual: "3DWheel Front L"),
            (Controller: "FrontRight_WheelController", Visual: "3DWheel Front R"),
            (Controller: "RearLeft_WheelController", Visual: "3DWheel Rear L"),
            (Controller: "RearRight_WheelController", Visual: "3DWheel Rear R"),
        };

        foreach (var binding in bindings)
        {
            var controller = FindTransform(vehicleRoot, binding.Controller);
            var visual = FindTransform(vehicleRoot, binding.Visual);
            var wheelController = controller?.GetComponents<Component>()
                .FirstOrDefault(component => component != null &&
                    string.Equals(component.GetType().Name, "WheelController", StringComparison.Ordinal));
            if (controller == null || visual == null || wheelController == null)
                return false;

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var wheelField = wheelController.GetType().GetField("wheel", flags);
            if (wheelField == null)
                return false;
            var wheel = wheelField.GetValue(wheelController);
            if (wheel == null)
                return false;
            var visualField = wheel.GetType().GetField("visual", flags);
            var visualTransformField = wheel.GetType().GetField("visualTransform", flags);
            if (visualField == null || visualTransformField == null ||
                !visualField.FieldType.IsInstanceOfType(visual.gameObject))
                return false;

            visual.position = controller.position;
            if (visual.parent != vehicleRoot)
                visual.SetParent(vehicleRoot, true);
            visualField.SetValue(wheel, visual.gameObject);
            // Keep the same binding as the working Porsche/Bugatti prefabs:
            // the native wheel visual is set, while visualTransform stays null.
            visualTransformField.SetValue(wheel, null);
            wheelField.SetValue(wheelController, wheel);
            if (!ReferenceEquals(visualField.GetValue(wheel), visual.gameObject) ||
                visualTransformField.GetValue(wheel) != null ||
                visual.parent != vehicleRoot ||
                Vector3.Distance(visual.position, controller.position) > 0.002f)
                return false;
            boundCount++;
        }

        return boundCount == bindings.Length;
    }

    private static void ApplySavedPlayerColor(
        VehicleController vehicleController, CarFeatures carFeatures, string source)
    {
        // The repainter has already applied the selected color through
        // CarFeatures.SetColor before emitting its preview event. Keep this
        // guard here so both prefab binding and later lifecycle restores honor it.
        if (!string.IsNullOrEmpty(source) &&
            source.IndexOf(VehicleRepainterColorPreviewEvent, StringComparison.Ordinal) >= 0)
        {
            if (BentleyContinentalGTSpeedDiagnostics.DebugEnabled &&
                BentleyContinentalGTSpeedDiagnostics.AppearanceDebugEnabled)
            {
                context?.Logger.Info(
                    $"BentleyContinentalGTSpeed appearance: preview color preserved " +
                    $"instance={vehicleController.GetInstanceID()} source='{source}'.");
            }
            return;
        }

        var colorName = vehicleController.vehicleInstance?.vehicleColorName;
        if (string.IsNullOrWhiteSpace(colorName))
            return;

        if (!TryResolveDriverColor(colorName, carFeatures.VehicleColor, out var savedColor) || savedColor == null)
        {
            context?.Logger.Warn(
                $"BentleyContinentalGTSpeed: saved paint color='{colorName}' could not be resolved " +
                $"after native renderer binding source='{source}'.");
            return;
        }

        carFeatures.SetColor(savedColor);
        Trace($"player paint restored color='{colorName}' source='{source}', " +
              $"bodyRenderers={carFeatures.bodyMeshes?.Length ?? 0}.");
    }

    internal static void RestorePlayerSavedColor(VehicleController vehicleController, string source)
    {
        var features = vehicleController != null ? vehicleController.GetComponent<CarFeatures>() : null;
        if (vehicleController != null && features != null)
            ApplySavedPlayerColor(vehicleController, features, source);
    }

    private static string DescribeRendererCandidates(Transform root)
    {
        var details = new List<string>();
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;
            var materials = new List<string>();
            foreach (var material in renderer.sharedMaterials)
                if (material != null)
                    materials.Add(material.name);
            details.Add(renderer.name + "[" + string.Join(",", materials) + "]");
            if (details.Count == 8)
                break;
        }

        return $"vehicleRenderers={renderers.Length}, samples=" +
               (details.Count > 0 ? string.Join("; ", details) : "<none>");
    }

    private static bool TryAssignRendererArray(object target, string memberName, Renderer[] renderers)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
                return TryAssignArray(field.FieldType, renderers, value => field.SetValue(target, value));
            var property = type.GetProperty(memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property?.CanWrite == true)
                return TryAssignArray(property.PropertyType, renderers, value => property.SetValue(target, value));
        }
        return false;
    }

    private static bool TryAssignArray(Type memberType, Renderer[] renderers, Action<Array> assign)
    {
        if (!memberType.IsArray)
            return false;
        var elementType = memberType.GetElementType();
        if (elementType == null || !typeof(Renderer).IsAssignableFrom(elementType))
            return false;
        var array = Array.CreateInstance(elementType, renderers.Length);
        for (var index = 0; index < renderers.Length; index++)
        {
            if (!elementType.IsInstanceOfType(renderers[index]))
                return false;
            array.SetValue(renderers[index], index);
        }
        assign(array);
        return true;
    }

    internal static GameObject? LoadTrafficAsset(string key)
    {
        foreach (var method in typeof(BigAmbitions.SaveSystem.AddressableResolver).GetMethods(
                     BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, "LoadAssetAsync", StringComparison.Ordinal) ||
                !method.IsGenericMethodDefinition) continue;
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string)) continue;
            var handle = method.MakeGenericMethod(typeof(GameObject))
                .Invoke(null, new object[] { key });
            var wait = handle?.GetType().GetMethod("WaitForCompletion", BindingFlags.Instance | BindingFlags.Public);
            return wait?.Invoke(handle, null) as GameObject;
        }
        return null;
    }

    private static IDictionary? GetPrefabCache()
    {
        var field = typeof(PrefabHelper).GetField("PrefabCache", BindingFlags.Static | BindingFlags.NonPublic);
        return field?.GetValue(null) as IDictionary;
    }

    private static VehicleColor CaptureVehicleColor(string name, VehicleColor source)
    {
        if (!CapturedVehicleColors.TryGetValue(name, out var captured) || captured == null)
        {
            captured = ScriptableObject.CreateInstance<VehicleColor>();
            captured.name = name;
            captured.hideFlags = HideFlags.DontSave;
            CapturedVehicleColors[name] = captured;
        }
        captured.randomWeight = source.randomWeight;
        captured.tint = source.tint;
        captured.fresnelColor = source.fresnelColor;
        captured.fresnelPower = source.fresnelPower;
        return captured;
    }

    private static void EnsureContractContains(PrivateDriverContract contract, string typeName)
    {
        contract.usableVehicleTypes ??= new List<string>();
        if (Contains(contract.usableVehicleTypes, typeName)) return;
        contract.usableVehicleTypes.Add(typeName);
        if (!ModifiedContracts.Contains(contract)) ModifiedContracts.Add(contract);
    }

    private static void ApplyCarType(CarType carType)
    {
        carType.name = AiCarTypeName;
        carType.vehiclePrefab = customAiPrefab;
        carType.nrOfVehicles = PrivateDriverPoolSize;
        carType.canBeRandomlyParked = false;
        carType.hasParkedVersion = false;
        carType.canBeAiDriven = true;
    }

    private static void SetMember(object target, string name, object value)
    {
        var type = target.GetType();
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null && field.FieldType.IsInstanceOfType(value)) { field.SetValue(target, value); return; }
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
            property.SetValue(target, value);
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        return null;
    }

    private static Transform? FindTransformContaining(Transform root, string fragment)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (child.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return child;
        return null;
    }

    private static void SetLayerRecursively(Transform root, int layer)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
    }

    private static bool Contains(IEnumerable<string>? values, string target)
    {
        if (values == null) return false;
        foreach (var value in values)
            if (string.Equals(value, target, StringComparison.Ordinal)) return true;
        return false;
    }

    private static void RemoveAll(List<string>? values, string target)
    {
        if (values == null) return;
        for (var index = values.Count - 1; index >= 0; index--)
            if (string.Equals(values[index], target, StringComparison.Ordinal)) values.RemoveAt(index);
    }

    private static bool Fail(string reason)
    {
        var message = "BentleyContinentalGTSpeed: private-driver setup unavailable: " + reason + ".";
        if (context != null) context.Logger.Warn(message);
        else Debug.LogWarning(message);
        return false;
    }

    private static void Trace(string message)
    {
        if (BentleyContinentalGTSpeedDiagnostics.DebugEnabled && DebugEnabled)
            BentleyContinentalGTSpeedDiagnostics.Info(context, message);
    }

}

internal sealed class BentleyContinentalGTSpeedTrafficWheelPose : MonoBehaviour
{
    [Serializable]
    private struct WheelBinding
    {
        public Transform Native;
        public Transform Rolling;
        public Transform Upright;
        public Vector3 PositionOffset;
        public Quaternion RotationOffset;
    }

    [SerializeField] private WheelBinding[] wheels = Array.Empty<WheelBinding>();

    internal bool Bind(VehicleComponent vehicle, Vector3 visualOffset)
    {
        var bindings = new List<WheelBinding>(4);
        foreach (var wheel in vehicle.allWheels)
        {
            var source = wheel.wheelGraphics;
            var front = wheel.wheelPosition == Wheel.WheelPosition.Front;
            var left = transform.InverseTransformPoint(source.position).x < 0f;
            var rolling = transform.Find("3DWheel " + (front ? "Front " : "Rear ") + (left ? "L" : "R"));
            var upright = transform.Find((front ? "Front" : "Rear") + (left ? "Left" : "Right") + "_UprightVisual");
            if (rolling == null || upright == null)
            {
                Debug.LogWarning("Bentley traffic wheel visuals are incomplete.");
                return false;
            }

            rolling.localPosition += visualOffset;
            upright.localPosition += visualOffset;
            bindings.Add(new WheelBinding
            {
                Native = source,
                Rolling = rolling,
                Upright = upright,
                PositionOffset = rolling.localPosition - transform.InverseTransformPoint(source.position),
                RotationOffset = Quaternion.Inverse(source.rotation) * rolling.rotation,
            });
        }

        wheels = bindings.ToArray();
        return true;
    }

    private void LateUpdate()
    {
        foreach (var wheel in wheels)
        {
            var center = wheel.Native.position + transform.TransformVector(wheel.PositionOffset);
            var rotation = wheel.Native.rotation * wheel.RotationOffset;
            wheel.Rolling.SetPositionAndRotation(center, rotation);
            var axle = rotation * Vector3.right;
            wheel.Upright.SetPositionAndRotation(center,
                Quaternion.LookRotation(Vector3.Cross(axle, transform.up), transform.up));
        }
    }
}

/// <summary>Restores saved chauffeur paint and never writes native wheel pose.</summary>
internal sealed class BentleyContinentalGTSpeedPrivateDriverAppearance : MonoBehaviour
{
    private Coroutine? initializeCoroutine;
    private Coroutine? transitionCoroutine;
    private PrivateDriverVehicle? privateDriver;
    private VehicleComponent? trafficVehicle;
    private bool subscribed;
    private bool referencesReady;

    internal bool BindExpectedReferences()
    {
        referencesReady = GetComponent<VehicleComponent>() != null &&
                          GetComponent<CarFeatures>() != null &&
                          FindTransform(transform, "NpcDriverSeatAnchor") != null &&
                          FindTransform(transform, "BentleyVisual") != null;
        if (!referencesReady)
            Debug.LogWarning("BentleyContinentalGTSpeed: chauffeur appearance references are incomplete.");
        return referencesReady;
    }

    private void OnEnable()
    {
        if (!BindExpectedReferences()) return;
        if (initializeCoroutine != null) StopCoroutine(initializeCoroutine);
        initializeCoroutine = StartCoroutine(InitializeSavedAppearance());
    }

    private IEnumerator InitializeSavedAppearance()
    {
        // Native spawn attaches the saved instance over its lifecycle boundary;
        // make a short bounded attempt rather than polling indefinitely.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            yield return null;
            privateDriver = GetComponent<PrivateDriverVehicle>();
            trafficVehicle = GetComponent<VehicleComponent>();
            if (privateDriver?.vehicleInstance != null && trafficVehicle != null)
                break;
        }
        if (privateDriver?.vehicleInstance == null || trafficVehicle == null)
        { initializeCoroutine = null; yield break; }
        SubscribeTrafficEvents();
        var applied = RestoreSavedColor();
        yield return new WaitForEndOfFrame();
        applied = RestoreSavedColor() || applied;
        BentleyContinentalGTSpeedPrivateDriverSupport.ReportAppearanceResult(
            privateDriver.vehicleInstance.vehicleColorName, applied);
        initializeCoroutine = null;
    }

    private void SubscribeTrafficEvents()
    {
        if (subscribed) return;
        AIEvents.onChangeDrivingState += HandleDrivingStateChanged;
        subscribed = true;
    }

    private void HandleDrivingStateChanged(int vehicleIndex, SpecialDriveActionTypes action, float actionValue)
    {
        if (privateDriver == null || trafficVehicle == null || vehicleIndex != trafficVehicle.GetIndex()) return;
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        transitionCoroutine = StartCoroutine(RefreshPaintAfterTransition());
    }

    private IEnumerator RefreshPaintAfterTransition()
    {
        yield return null;
        var ui = UI.UIs.Instance?.smartphoneUI?.privateDriverUI;
        var currentProperty = typeof(Player.HUD.SmartphoneUI.SmartphonePrivateDriverUI).GetProperty(
            "CurrentVehicle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (ui != null && currentProperty != null)
        {
            var current = currentProperty.GetValue(ui) as PrivateDriverVehicle;
            if (privateDriver != null && current != privateDriver && trafficVehicle != null &&
                trafficVehicle.gameObject.activeInHierarchy)
            {
                BentleyContinentalGTSpeedPrivateDriverSupport.ReportDeparturePaintResult(
                    privateDriver.vehicleInstance?.vehicleColorName, RestoreSavedColor());
            }
        }
        transitionCoroutine = null;
    }

    private bool RestoreSavedColor()
    {
        if (privateDriver?.vehicleInstance == null) return false;
        var features = GetComponent<CarFeatures>();
        var colorName = privateDriver.vehicleInstance.vehicleColorName;
        if (features == null ||
            !BentleyContinentalGTSpeedPrivateDriverSupport.TryResolveDriverColor(
                colorName, features.VehicleColor, out var color) || color == null)
            return false;
        features.SetColor(color);
        return true;
    }

    private void OnDisable()
    {
        if (initializeCoroutine != null) StopCoroutine(initializeCoroutine);
        if (transitionCoroutine != null) StopCoroutine(transitionCoroutine);
        initializeCoroutine = null;
        transitionCoroutine = null;
        if (subscribed)
        {
            AIEvents.onChangeDrivingState -= HandleDrivingStateChanged;
            subscribed = false;
        }
        privateDriver = null;
        trafficVehicle = null;
    }

    private void OnDestroy() => OnDisable();

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(node.name, name, StringComparison.Ordinal)) return node;
        return null;
    }
}
