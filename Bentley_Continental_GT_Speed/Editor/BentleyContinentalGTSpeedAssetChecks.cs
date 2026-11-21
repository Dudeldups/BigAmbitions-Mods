#nullable enable
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vehicles.VehicleTypes;

public static class BentleyContinentalGTSpeedAssetChecks
{
    private const string Root = "Assets/Mods/Bentley_Continental_GT_Speed/";
    private const string PrefabPath = Root + "BentleyContinentalGTSpeed.prefab";
    private const string TypePath = Root + "BentleyContinentalGTSpeed.asset";
    private const string MeshFolder = Root + "Models/GeneratedMeshes";
    private const string MaterialFolder = Root + "Models/GeneratedMaterials";
    private const string TextureFolder = MaterialFolder + "/Textures";
    private const string VehiclePaintShaderPath = "Assets/_BaDependencies/Shaders/Vehicles/SH_Vehicle.shadergraph";
    private const uint DecalRenderingLayerMask = 0x0000FF00u;

    [MenuItem("Big Ambitions Mods/Scaffold/Verify BentleyContinentalGTSpeed Assets")]
    public static void Verify()
    {
        var type = AssetDatabase.LoadAssetAtPath<VehicleType>(TypePath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (type == null || prefab == null)
            throw new InvalidOperationException("BentleyContinentalGTSpeed: missing VehicleType asset or prefab.");
        if (!string.Equals(type.vehicleTypeName, "bentleycontinentalgtspeed-vehicle:vehicletype_bentleycontinentalgtspeed", StringComparison.Ordinal))
            throw new InvalidOperationException("BentleyContinentalGTSpeed: vehicle type identity mismatch.");

        var native = prefab.GetComponent<VehicleController>();
        var nwh = prefab.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        if (native == null || nwh == null || nwh.stateSettings == null)
            throw new InvalidOperationException("BentleyContinentalGTSpeed: missing native controllers or NWH StateSettings.");
        VerifySteeringAndTractionControl(type, nwh, prefab);

        var wheelCount = prefab.GetComponentsInChildren<Component>(true)
            .Count(component => component != null && component.GetType().Name == "WheelController");
        if (wheelCount != 4)
            throw new InvalidOperationException("BentleyContinentalGTSpeed: expected four wheel controllers, got " + wheelCount);

        VerifyWheelAndShellFit(prefab);

        var driverSteeringAnchor = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "BentleyPlayerSteeringWheelAnchor");
        var driverSeatAnchor = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "BentleyDriverSeatAnchor");
        if (driverSteeringAnchor == null || driverSeatAnchor == null)
            throw new InvalidOperationException("BentleyContinentalGTSpeed: player/traffic driver anchors are missing.");

        var donorVisual = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "2020_abt_sportline_McLaren_rs6-r");
        if (donorVisual == null || !donorVisual.gameObject.activeSelf)
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: scaled donor NavMesh helper is missing or inactive.");
        var donorObstacle = donorVisual.GetComponent<UnityEngine.AI.NavMeshObstacle>();
        var nativeSerialized = new SerializedObject(native);
        var nativeObstacle = nativeSerialized.FindProperty("navMeshObstacle")?.objectReferenceValue as
                             UnityEngine.AI.NavMeshObstacle;
        if (donorObstacle == null || !donorObstacle.enabled || nativeObstacle != donorObstacle)
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: VehicleController is not wired to its active NavMesh obstacle.");
        VerifyExitNavigationGeometry(prefab, donorObstacle);
        if (donorVisual.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer != null && renderer.enabled))
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: a donor renderer is enabled beside the Bentley model.");

        foreach (var wheelController in prefab.GetComponentsInChildren<Component>(true)
                     .Where(component => component != null && component.GetType().Name == "WheelController"))
        {
            var serializedWheel = new SerializedObject(wheelController);
            var visual = serializedWheel.FindProperty("wheel.visual")?.objectReferenceValue as GameObject;
            var nonRotatingVisual = serializedWheel.FindProperty("wheel.nonRotatingVisual")?.objectReferenceValue as GameObject;
            var visualTransform = serializedWheel.FindProperty("wheel.visualTransform")?.objectReferenceValue as Transform;
            if (visual == null ||
                !visual.transform.IsChildOf(prefab.transform) ||
                visualTransform != null ||
                visual.transform.parent != prefab.transform)
                throw new InvalidOperationException(
                    "BentleyContinentalGTSpeed: native rolling/steering wheel visual binding is invalid on " +
                    wheelController.name);
            VerifyWheelVisual(prefab.transform, visual, wheelController.name + ".wheel.visual");
            VerifyWheelVisual(prefab.transform, nonRotatingVisual, wheelController.name + ".wheel.nonRotatingVisual");
        }

        VerifyVisibleRenderers(prefab);
        var paintBindings = prefab.GetComponent<CarFeatures>()?.bodyMeshes;
        if (paintBindings == null || paintBindings.Length == 0 ||
            paintBindings.Any(renderer => renderer == null ||
                !renderer.sharedMaterials.Any(material => material != null &&
                    (material.name.StartsWith("Bentley_Paint_Prepared", StringComparison.Ordinal) ||
                     material.name.StartsWith("Bentley_OEMPaint_Prepared", StringComparison.Ordinal) ||
                     string.Equals(material.name, "Bentley_CaliperAZonePaint_Prepared", StringComparison.Ordinal)))))
            throw new InvalidOperationException("Bentley native bodyMeshes contains missing or donor paint renderers.");
        BentleyContinentalGTSpeedCaliperChecks.VerifyPrefabBindings(prefab);
        BentleyContinentalGTSpeedCaliperChecks.VerifyRuntimeInstanceIsolation(prefab);
        BentleyContinentalGTSpeedTrafficChecks.VerifyRendererBindingWithoutPlayerController(prefab);
        BentleyContinentalGTSpeedWashSetup.VerifyBindings(prefab);
        BentleyContinentalGTSpeedConditionChecks.Verify(prefab, type);
        BentleyContinentalGTSpeedDamageChecks.Verify(prefab);
        BentleyContinentalGTSpeedLightingSetup.Verify(prefab);
        BentleyContinentalGTSpeedLightStateChecks.VerifyPlayer(prefab);
        var badgeMaterials = prefab.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null && material.name == "Bentley_BadgeB_Prepared")
            .Distinct().ToArray();
        if (badgeMaterials.Length == 0)
            throw new InvalidOperationException("Bentley badge material is missing from the saved prefab.");
        foreach (var badgeMaterial in badgeMaterials)
            BentleyContinentalGTSpeedAppearanceSetup.VerifyBadgeTransparency(badgeMaterial, PrefabPath);
        VerifyPersistedAssets("t:Mesh", MeshFolder, "mesh");
        VerifyPersistedAssets("t:Material", MaterialFolder, "material");
        VerifyPersistedAssets("t:Texture2D", TextureFolder, "texture");

        foreach (var dependency in AssetDatabase.GetDependencies(PrefabPath, true)
                     .Concat(AssetDatabase.GetDependencies(TypePath, true)))
        {
            if (dependency.StartsWith("Assets/Mods/", StringComparison.OrdinalIgnoreCase) &&
                !dependency.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("BentleyContinentalGTSpeed: foreign vehicle/mod asset dependency: " + dependency);
        }
        Debug.Log("BentleyContinentalGTSpeed: native asset/ownership checks pass, NOT a gameplay validation.");
    }

    private static void VerifySteeringAndTractionControl(
        VehicleType vehicleType,
        NWH.VehiclePhysics2.VehicleController nwh,
        GameObject prefab)
    {
        const float expectedSteerAngle = 35.80f;
        const float expectedSlipThreshold = 0.10f;
        var serializedNwh = new SerializedObject(nwh);
        var steerAngle = serializedNwh.FindProperty("steering.maximumSteerAngle");
        if (steerAngle?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(steerAngle.floatValue - expectedSteerAngle) > 0.001f ||
            Mathf.Abs(vehicleType.turnRadius - Mathf.RoundToInt(expectedSteerAngle)) > 0.001f)
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: steering lock must be 35.80 degrees and the integer VehicleType proxy 36.");

        var brakeTorque = serializedNwh.FindProperty("brakes.maxTorque");
        var downshiftRpm = serializedNwh.FindProperty("powertrain.transmission._downshiftRPM");
        if (brakeTorque?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(brakeTorque.floatValue - 4700f) > 0.001f ||
            downshiftRpm?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(downshiftRpm.floatValue - 3800f) > 0.001f)
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: NWH brake torque/downshift threshold must be 4700 Nm/3800 RPM.");

        foreach (var wheel in prefab.GetComponentsInChildren<Component>(true)
                     .Where(component => component != null && component.GetType().Name == "WheelController"))
        {
            var serializedWheel = new SerializedObject(wheel);
            var forwardGrip = serializedWheel.FindProperty("forwardFriction.grip");
            if (forwardGrip?.propertyType != SerializedPropertyType.Float ||
                Mathf.Abs(forwardGrip.floatValue - 0.50f) > 0.001f)
                throw new InvalidOperationException(
                    "BentleyContinentalGTSpeed: all NWH wheels must use 0.50 longitudinal grip.");
        }

        var tcs = prefab.GetComponent<NWH.VehiclePhysics2.Modules.TCS.TCSModuleWrapper>();
        if (tcs == null)
            throw new InvalidOperationException("BentleyContinentalGTSpeed: native NWH TCS wrapper is missing.");
        var serializedTcs = new SerializedObject(tcs);
        var slipThreshold = serializedTcs.FindProperty("module.slipThreshold");
        if (slipThreshold?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(slipThreshold.floatValue - expectedSlipThreshold) > 0.001f)
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: native NWH TCS slip threshold is not serialized at 0.10.");
    }

    private static void VerifyWheelAndShellFit(GameObject prefab)
    {
        var transforms = prefab.GetComponentsInChildren<Transform>(true);
        var shell = transforms.FirstOrDefault(item => item.name == "BentleyVisual");
        const float expectedShellY = 0.007489137f - 0.14f;
        if (shell == null || Mathf.Abs(shell.localPosition.y - expectedShellY) > 0.001f)
            throw new InvalidOperationException("Bentley shell must be 2 cm above the previously tested fit.");
        foreach (var corner in new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" })
        {
            var wheel = transforms.FirstOrDefault(item => item.name == corner + "_WheelController");
            var upright = transforms.FirstOrDefault(item => item.name == corner + "_UprightVisual");
            var front = corner.StartsWith("Front", StringComparison.Ordinal);
            var expectedZ = front ? 1.5417409f : -1.3088720f;
            var expectedY = front ? 0.3801434f : 0.3811483f;
            if (wheel == null || Mathf.Abs(wheel.localPosition.z - expectedZ) > 0.002f ||
                Mathf.Abs(wheel.localPosition.y - expectedY) > 0.002f || upright == null ||
                Vector3.Distance(wheel.position, upright.position) > 0.002f)
                throw new InvalidOperationException("Bentley wheel/caliper authored fit mismatch: " + corner);
        }
        Debug.Log("Bentley fit verified: shell +0.02 m, front axle +0.01 m, tire heights/rear axle unchanged.");
    }

    private static void VerifyExitNavigationGeometry(GameObject prefab, UnityEngine.AI.NavMeshObstacle obstacle)
    {
        if (!TryGetBodyColliderBounds(prefab.transform, out var bodyBounds))
            throw new InvalidOperationException("BentleyContinentalGTSpeed: active BodyCollider bounds are missing.");

        var groundedBounds = bodyBounds;
        var groundedMinimum = groundedBounds.min;
        groundedMinimum.y = Mathf.Min(groundedMinimum.y, -0.02f);
        groundedBounds.SetMinMax(groundedMinimum, groundedBounds.max);
        var obstacleBounds = TransformBoxToRoot(
            prefab.transform, obstacle.transform, obstacle.center, obstacle.size);
        if (!BoundsNearlyEqual(groundedBounds, obstacleBounds, 0.005f))
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: serialized NavMeshObstacle does not match the grounded BodyCollider footprint. " +
                $"expected={groundedBounds}, actual={obstacleBounds}");

        var driverMarker = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "Driverside");
        var passengerMarker = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "Passengerside");
        var minimumMarkerOffset = bodyBounds.extents.x + 0.75f;
        if (driverMarker == null || passengerMarker == null ||
            prefab.transform.InverseTransformPoint(driverMarker.position).x > bodyBounds.center.x - minimumMarkerOffset ||
            prefab.transform.InverseTransformPoint(passengerMarker.position).x < bodyBounds.center.x + minimumMarkerOffset)
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: native exit targets need at least 0.75 m of clearance beyond the body collider.");
    }

    /// <summary>Authors the native exit obstacle and exit target spacing from the active body collider.</summary>
    public static void AuthorExitNavigationGeometry(GameObject prefab)
    {
        if (prefab == null)
            throw new ArgumentNullException(nameof(prefab));
        if (!TryGetBodyColliderBounds(prefab.transform, out var bodyBounds))
            throw new InvalidOperationException("BentleyContinentalGTSpeed: active BodyCollider bounds are missing.");

        var donorVisual = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "2020_abt_sportline_McLaren_rs6-r");
        var obstacle = donorVisual != null
            ? donorVisual.GetComponent<UnityEngine.AI.NavMeshObstacle>()
            : null;
        if (obstacle == null)
            throw new InvalidOperationException("BentleyContinentalGTSpeed: NavMesh obstacle helper is missing.");

        var groundedBounds = bodyBounds;
        var groundedMinimum = groundedBounds.min;
        groundedMinimum.y = Mathf.Min(groundedMinimum.y, -0.02f);
        groundedBounds.SetMinMax(groundedMinimum, groundedBounds.max);
        var obstacleLocalBounds = TransformBoxToObstacleLocal(
            prefab.transform, obstacle.transform, groundedBounds.center, groundedBounds.size);
        obstacle.center = obstacleLocalBounds.center;
        obstacle.size = obstacleLocalBounds.size;

        var driverMarker = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "Driverside");
        var passengerMarker = prefab.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => transform.name == "Passengerside");
        if (driverMarker == null || passengerMarker == null)
            throw new InvalidOperationException("BentleyContinentalGTSpeed: native exit markers are missing.");

        var minimumMarkerOffset = bodyBounds.extents.x + 0.75f;
        var driverRootPosition = prefab.transform.InverseTransformPoint(driverMarker.position);
        if (driverRootPosition.x > bodyBounds.center.x - minimumMarkerOffset)
        {
            driverRootPosition.x = bodyBounds.center.x - minimumMarkerOffset;
            driverMarker.position = prefab.transform.TransformPoint(driverRootPosition);
        }
        var passengerRootPosition = prefab.transform.InverseTransformPoint(passengerMarker.position);
        if (passengerRootPosition.x < bodyBounds.center.x + minimumMarkerOffset)
        {
            passengerRootPosition.x = bodyBounds.center.x + minimumMarkerOffset;
            passengerMarker.position = prefab.transform.TransformPoint(passengerRootPosition);
        }
    }

    private static bool TryGetBodyColliderBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(child.name, "BodyCollider", StringComparison.Ordinal))
                continue;
            foreach (var collider in child.GetComponentsInChildren<BoxCollider>(true))
            {
                if (collider == null || !collider.enabled || collider.isTrigger ||
                    !IsActiveWithinPrefab(collider.transform, root))
                    continue;
                var half = collider.size * 0.5f;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var local = collider.center + Vector3.Scale(half, new Vector3(x, y, z));
                    var rootPoint = root.InverseTransformPoint(collider.transform.TransformPoint(local));
                    if (!found)
                    {
                        bounds = new Bounds(rootPoint, Vector3.zero);
                        found = true;
                    }
                    else
                        bounds.Encapsulate(rootPoint);
                }
            }
        }
        return found;
    }

    private static bool IsActiveWithinPrefab(Transform current, Transform root)
    {
        while (current != null)
        {
            if (!current.gameObject.activeSelf)
                return false;
            if (current == root)
                return true;
            current = current.parent;
        }
        return false;
    }

    private static Bounds TransformBoxToRoot(Transform root, Transform boxTransform, Vector3 center, Vector3 size)
    {
        var bounds = new Bounds();
        var found = false;
        var half = size * 0.5f;
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var local = center + Vector3.Scale(half, new Vector3(x, y, z));
            var rootPoint = root.InverseTransformPoint(boxTransform.TransformPoint(local));
            if (!found)
            {
                bounds = new Bounds(rootPoint, Vector3.zero);
                found = true;
            }
            else
                bounds.Encapsulate(rootPoint);
        }
        return bounds;
    }

    private static Bounds TransformBoxToObstacleLocal(
        Transform root, Transform obstacleTransform, Vector3 center, Vector3 size)
    {
        var bounds = new Bounds();
        var found = false;
        var half = size * 0.5f;
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var rootPoint = center + Vector3.Scale(half, new Vector3(x, y, z));
            var obstaclePoint = obstacleTransform.InverseTransformPoint(root.TransformPoint(rootPoint));
            if (!found)
            {
                bounds = new Bounds(obstaclePoint, Vector3.zero);
                found = true;
            }
            else
                bounds.Encapsulate(obstaclePoint);
        }
        return bounds;
    }

    private static void VerifyWheelVisual(Transform prefabRoot, GameObject? visual, string description)
    {
        if (visual == null || !visual.transform.IsChildOf(prefabRoot))
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: " + description + " is missing or outside the prefab.");

        var hasValidRenderer = visual.GetComponentsInChildren<MeshRenderer>(true)
            .Any(renderer => renderer != null && renderer.enabled && !renderer.forceRenderingOff &&
                             IsActiveWithinPrefab(renderer.transform, prefabRoot) &&
                             renderer.GetComponent<MeshFilter>()?.sharedMesh != null);
        if (!hasValidRenderer)
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: " + description +
                " needs an active enabled MeshRenderer with a nonempty MeshFilter mesh on the renderer object.");
    }

    private static bool BoundsNearlyEqual(Bounds left, Bounds right, float tolerance) =>
        Vector3.Distance(left.min, right.min) <= tolerance &&
        Vector3.Distance(left.max, right.max) <= tolerance;

    /// <summary>Batch-mode entry point for Unity -executeMethod.</summary>
    public static void VerifyBatch()
    {
        try
        {
            Verify();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("BentleyContinentalGTSpeed asset checks failed: " + exception);
            EditorApplication.Exit(1);
        }
    }

    private static void VerifyVisibleRenderers(GameObject prefab)
    {
        var hdrpLit = Shader.Find("HDRP/Lit");
        var vehiclePaint = AssetDatabase.LoadAssetAtPath<Shader>(VehiclePaintShaderPath);
        if (hdrpLit == null || vehiclePaint == null)
            throw new InvalidOperationException("BentleyContinentalGTSpeed: expected vehicle appearance shaders are unavailable.");

        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                !IsActiveWithinPrefab(renderer.transform, prefab.transform))
                continue;

            Mesh? mesh = null;
            if (renderer is MeshRenderer)
                mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            else if (renderer is SkinnedMeshRenderer skinnedRenderer)
                mesh = skinnedRenderer.sharedMesh;
            else
                continue;

            if (mesh == null)
                throw new InvalidOperationException(
                    "BentleyContinentalGTSpeed: visible renderer has no mesh: " + renderer.name);
            VerifyOwnedAsset(mesh, "visible mesh on " + renderer.name);

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0 || materials.Any(material => material == null))
                throw new InvalidOperationException(
                    "BentleyContinentalGTSpeed: visible renderer has missing material references: " + renderer.name);

            foreach (var material in materials)
            {
                VerifyOwnedAsset(material, "visible material on " + renderer.name);
                var materialName = material.name.Replace("(Clone)", string.Empty).Trim();
                var isBodyPaint = materialName.StartsWith("Bentley_Paint_Prepared", StringComparison.Ordinal) ||
                                  materialName.StartsWith("Bentley_OEMPaint_Prepared", StringComparison.Ordinal);
                var isCaliperPaint = string.Equals(materialName, "Bentley_CaliperAZonePaint_Prepared", StringComparison.Ordinal);
                var expectedShader = materialName == "Bentley_LampEmitters_Prepared"
                    ? Shader.Find("Bentley/ModelLampEmitters")
                    : isBodyPaint || isCaliperPaint ? vehiclePaint : hdrpLit;
                if (material.shader != expectedShader)
                    throw new InvalidOperationException(
                        "BentleyContinentalGTSpeed: visible material has the wrong role-specific shader: " +
                        renderer.name + "/" + material.name + " (" +
                        (material.shader != null ? material.shader.name : "<missing>") + ")");
                if (isBodyPaint && !material.HasProperty("Color_3d0f0cdbe6b74be28a1a5be5bab71dea"))
                    throw new InvalidOperationException(
                        "BentleyContinentalGTSpeed: body paint shader lacks the native CarFeatures tint property: " +
                        renderer.name + "/" + material.name);
                if (isBodyPaint || isCaliperPaint)
                    BentleyContinentalGTSpeedAppearanceSetup.VerifyPaintMaterial(material, renderer.name);
                if (isBodyPaint &&
                    ((renderer.renderingLayerMask & DecalRenderingLayerMask) != 0 ||
                     !MaterialFloatEquals(material, "_SupportDecals", 0f) ||
                     !MaterialFloatEquals(material, "_Metallic", 0f) ||
                     !MaterialFloatEquals(material, "_Smoothness", 0.5f) ||
                     !MaterialFloatEquals(material, "_ReceivesSSR", 0f) ||
                     !MaterialFloatEquals(material, "_ReceivesSSRTransparent", 0f) ||
                     !MaterialFloatEquals(material, "_EnableBlendModePreserveSpecularLighting", 0f) ||
                     !MaterialFloatEquals(material, "Vector1_481fa2a8a5e94165a039319bfd512b76", 2.04f) ||
                     !MaterialColorEquals(material, "Color_f78fac473bac467092fb27521e9f71ea", Color.black) ||
                     !material.IsKeywordEnabled("_DISABLE_DECALS") ||
                     !material.IsKeywordEnabled("_DISABLE_SSR")))
                    throw new InvalidOperationException(
                        "BentleyContinentalGTSpeed: body paint settings or decal/reflection safeguards are incomplete: " +
                        renderer.name + "/" + material.name);
                if (materialName.StartsWith("Bentley_GlassMtl_Prepared", StringComparison.Ordinal) &&
                    (!MaterialColorEquals(material, "_BaseColor", new Color(0.62f, 0.70f, 0.78f, 0.02f)) ||
                     !MaterialColorEquals(material, "_Color", new Color(0.62f, 0.70f, 0.78f, 0.02f)) ||
                     !MaterialFloatEquals(material, "_Smoothness", 0.62f) ||
                     !MaterialColorEquals(material, "_SpecularColor", new Color(0.55f, 0.55f, 0.55f, 1f)) ||
                     !MaterialFloatEquals(material, "_EnableBlendModePreserveSpecularLighting", 0f) ||
                     !MaterialFloatEquals(material, "_ReceivesSSR", 0f) ||
                     !MaterialFloatEquals(material, "_ReceivesSSRTransparent", 0f) ||
                     !MaterialFloatEquals(material, "_Cull", 0f) ||
                     !material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") ||
                     !material.IsKeywordEnabled("_DISABLE_SSR") ||
                     !material.IsKeywordEnabled("_DISABLE_SSR_TRANSPARENT")))
                    throw new InvalidOperationException(
                        "BentleyContinentalGTSpeed: cabin glass transparency or reflection settings are incomplete: " +
                        renderer.name + "/" + material.name);
                foreach (var propertyName in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(propertyName);
                    if (texture != null)
                        VerifyOwnedAsset(texture, "texture '" + propertyName + "' on material " + material.name);
                }
            }
        }
    }

    private static void VerifyPersistedAssets(string filter, string folder, string label)
    {
        var paths = AssetDatabase.FindAssets(filter, new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToArray();
        if (paths.Length == 0)
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: no persisted " + label + " assets found under " + folder);

        foreach (var path in paths)
        {
            if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "BentleyContinentalGTSpeed: persisted " + label + " is outside the target mod: " + path);
        }
    }

    private static bool MaterialFloatEquals(Material material, string property, float expected) =>
        !material.HasProperty(property) ||
        Mathf.Abs(material.GetFloat(property) - expected) <= 0.0001f;

    private static bool MaterialColorEquals(Material material, string property, Color expected)
    {
        if (!material.HasProperty(property))
            return true;
        var actual = material.GetColor(property);
        return Mathf.Abs(actual.r - expected.r) <= 0.0001f &&
               Mathf.Abs(actual.g - expected.g) <= 0.0001f &&
               Mathf.Abs(actual.b - expected.b) <= 0.0001f &&
               Mathf.Abs(actual.a - expected.a) <= 0.0001f;
    }

    private static void VerifyOwnedAsset(UnityEngine.Object asset, string description)
    {
        var path = AssetDatabase.GetAssetPath(asset);
        if (!path.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "BentleyContinentalGTSpeed: " + description + " is not a persistent Bentley asset: " + path);
    }
}
