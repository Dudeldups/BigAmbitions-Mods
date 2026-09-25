#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BigfootMonsterTruckSetup
{
    private const string WindowsBundlePath =
        "Assets/Mods/BigfootMonsterTruck/AssetBundles/Windows/bigfootmonstertruck.unity3d";
    private const string AudiAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string AudiPrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath =
        "Assets/Mods/BigfootMonsterTruck/Models/bigfoot-driveable.glb";
    private const string WindshieldMaterialPath =
        "Assets/Mods/BigfootMonsterTruck/Models/BigfootWindshield.mat";
    private const string OpaqueMaterialFolder =
        "Assets/Mods/BigfootMonsterTruck/Models/GeneratedMaterials";
    private const string TargetAssetPath =
        "Assets/Mods/BigfootMonsterTruck/BigfootMonsterTruck.asset";
    private const string TargetPrefabPath =
        "Assets/Mods/BigfootMonsterTruck/BigfootMonsterTruck.prefab";
    private const string VehicleTypeName =
        "bigfootmonstertruck-vehicle:vehicletype_bigfootmonstertruck";
    private const float AxleHeight = 1.05f;
    private const float WheelPrefabVerticalOffset = -0.23f;

    [MenuItem("Big Ambitions Mods/Setup Bigfoot Monster Truck")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "BigfootMonsterTruck setup complete: generated driveable prefab, vehicle type, " +
            "four animated wheel visuals, centered seat anchor, colliders and transparent windshield.");
    }

    public static void VerifyBuiltBundle()
    {
        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");
        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(TargetAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(TargetPrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException("Bundle is missing its VehicleType or vehicle prefab.");
            var serializedVehicleType = new SerializedObject(vehicleType);
            var bundledPrice = serializedVehicleType.FindProperty("price")?.floatValue ?? 0f;
            var bundledMaxSpeed = serializedVehicleType.FindProperty("maxSpeed")?.intValue ?? 0;
            var bundledCargoCapacity =
                serializedVehicleType.FindProperty("maxCargoCapacity")?.intValue ?? 0;

            var wheelControllers = 0;
            var wheelVisuals = 0;
            var wheelVisualVerticalRadiusTotal = 0f;
            var alignedWheelVisuals = 0;
            var animatedWheelVisuals = 0;
            var authoredWheelAxes = 0;
            var outwardWheelFaces = 0;
            var axleAlignedWheelVisuals = 0;
            var physicalWheelColliders = 0;
            var climbContactColliders = 0;
            var climbChassisRestored = false;
            var hasSeat = false;
            var raisedSeat = false;
            var loadingAtDriverDoor = false;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.EndsWith("_WheelController", StringComparison.Ordinal))
                    wheelControllers++;
                if (transform.name.StartsWith("Wheel", StringComparison.Ordinal) &&
                    transform.name.EndsWith("Visual", StringComparison.Ordinal))
                {
                    wheelVisuals++;
                    if (TryGetRendererBounds(transform, out var bounds) &&
                        Vector3.Distance(bounds.center, transform.position) < 0.02f)
                    {
                        alignedWheelVisuals++;
                        wheelVisualVerticalRadiusTotal += bounds.extents.y;
                    }
                    if (transform.localScale.x < 0f)
                        outwardWheelFaces++;
                }
                if (transform.name.EndsWith("_WheelController", StringComparison.Ordinal) &&
                    TryGetAssignedWheelVisual(transform, out var assignedVisual) &&
                    assignedVisual.name.StartsWith("Wheel", StringComparison.Ordinal) &&
                    assignedVisual.name.EndsWith("Visual", StringComparison.Ordinal))
                {
                    animatedWheelVisuals++;
                    if (Quaternion.Angle(
                            assignedVisual.transform.localRotation,
                            Quaternion.identity) > 1f)
                        authoredWheelAxes++;
                    if (Vector3.Distance(
                            assignedVisual.transform.position,
                            transform.position + prefab.transform.up * WheelPrefabVerticalOffset) < 0.01f)
                        axleAlignedWheelVisuals++;
                }
                if (string.Equals(transform.name, "BigfootWheelContactColliders", StringComparison.Ordinal))
                {
                    var contacts = transform.GetComponents<SphereCollider>();
                    physicalWheelColliders = contacts.Length;
                    foreach (var contact in contacts)
                        if (Mathf.Abs(contact.radius - 0.58f) < 0.01f &&
                            Mathf.Abs(contact.center.y - 0.58f) < 0.01f &&
                            Mathf.Abs(Mathf.Abs(contact.center.z) - 1.60f) < 0.01f)
                            climbContactColliders++;
                }
                if (string.Equals(transform.name, "BodyCollider", StringComparison.Ordinal))
                {
                    var bodyColliders = transform.GetComponents<BoxCollider>();
                    climbChassisRestored = bodyColliders.Length >= 2 &&
                                           Mathf.Abs(bodyColliders[0].size.z - 5.3f) < 0.01f;
                }
                if (string.Equals(transform.name, "BigfootDriverSeat", StringComparison.Ordinal))
                {
                    hasSeat = true;
                    raisedSeat = transform.localPosition.y >= 2.01f &&
                                 transform.localPosition.y <= 2.03f;
                }
                if (string.Equals(transform.name, "LoadingPosition", StringComparison.Ordinal))
                    loadingAtDriverDoor =
                        Vector3.Distance(transform.localPosition, new Vector3(-2.05f, 0.1f, 0.1f)) < 0.01f;
            }

            var visibleRenderers = 0;
            var hasTransparentGlass = false;
            var hasWindshieldDecalAtlas = false;
            var opaqueMaterials = new HashSet<Material>();
            var decalSafeOpaqueMaterials = 0;
            var opaqueRendererMasksSafe = true;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.enabled && renderer.sharedMaterials.Length > 0)
                    visibleRenderers++;
                var isBigfootRenderer = IsBigfootVisualRenderer(renderer.transform);
                var hasBigfootOpaqueMaterial = false;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (isBigfootRenderer && material != null &&
                        !BigfootMonsterTruckMaterials.IsTransparentMaterial(material) &&
                        opaqueMaterials.Add(material))
                    {
                        hasBigfootOpaqueMaterial = true;
                        if (string.Equals(material.shader.name, "HDRP/Lit", StringComparison.Ordinal) &&
                            (!material.HasProperty("_SupportDecals") ||
                             material.GetFloat("_SupportDecals") < 0.5f) &&
                            material.IsKeywordEnabled("_DISABLE_DECALS") &&
                            (!material.HasProperty("_ZWrite") || material.GetFloat("_ZWrite") > 0.5f) &&
                            material.renderQueue == (int)RenderQueue.Geometry)
                            decalSafeOpaqueMaterials++;
                    }
                    else if (isBigfootRenderer && material != null &&
                             !BigfootMonsterTruckMaterials.IsTransparentMaterial(material))
                        hasBigfootOpaqueMaterial = true;
                    if (material != null &&
                        material.name.IndexOf("Windshield_Glass", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        material.renderQueue >= 3000 &&
                        material.color.r >= 0.7f &&
                        material.color.a >= 0.15f && material.color.a <= 0.3f &&
                        string.Equals(material.shader.name, "Bigfoot/TransparentWindshield",
                            StringComparison.Ordinal))
                    {
                        hasTransparentGlass = true;
                        hasWindshieldDecalAtlas = material.mainTexture != null;
                    }
                }
                if (hasBigfootOpaqueMaterial && (renderer.renderingLayerMask & 0x0000FF00u) != 0)
                    opaqueRendererMasksSafe = false;
            }

            if (wheelControllers != 4 || wheelVisuals != 4 || alignedWheelVisuals != 4 ||
                animatedWheelVisuals != 4 || authoredWheelAxes != 4 ||
                outwardWheelFaces != 4 || axleAlignedWheelVisuals != 4 ||
                physicalWheelColliders != 4 ||
                climbContactColliders != 4 || !climbChassisRestored ||
                !hasSeat || !raisedSeat ||
                !loadingAtDriverDoor || visibleRenderers == 0 || !hasTransparentGlass ||
                !hasWindshieldDecalAtlas ||
                opaqueMaterials.Count == 0 ||
                decalSafeOpaqueMaterials != opaqueMaterials.Count || !opaqueRendererMasksSafe ||
                Mathf.Abs(bundledPrice - 345000f) > 0.5f || bundledMaxSpeed != 140 ||
                bundledCargoCapacity != 4)
            {
                throw new InvalidOperationException(
                    $"Bundle verification failed: controllers={wheelControllers}, " +
                    $"wheelVisuals={wheelVisuals}, alignedWheelVisuals={alignedWheelVisuals}, " +
                    $"animatedWheelVisuals={animatedWheelVisuals}, " +
                    $"authoredWheelAxes={authoredWheelAxes}, " +
                    $"outwardWheelFaces={outwardWheelFaces}, " +
                    $"axleAlignedWheelVisuals={axleAlignedWheelVisuals}, " +
                    $"physicalWheelColliders={physicalWheelColliders}, " +
                    $"climbContacts={climbContactColliders}, " +
                    $"climbChassis={climbChassisRestored}, " +
                    $"seat={hasSeat}, raisedSeat={raisedSeat}, visibleRenderers={visibleRenderers}, " +
                    $"loadingAtDriverDoor={loadingAtDriverDoor}, " +
                    $"transparentGlass={hasTransparentGlass}, " +
                    $"windshieldDecalAtlas={hasWindshieldDecalAtlas}, price={bundledPrice}, " +
                    $"maxSpeed={bundledMaxSpeed}, cargoCapacity={bundledCargoCapacity}, " +
                    $"opaqueMaterials={opaqueMaterials.Count}, " +
                    $"decalSafe={decalSafeOpaqueMaterials}, " +
                    $"rendererMasksSafe={opaqueRendererMasksSafe}.");
            }

            Debug.Log(
                $"BigfootMonsterTruck bundle verified: controllers={wheelControllers}, " +
                $"wheelVisuals={wheelVisuals}, alignedWheelVisuals={alignedWheelVisuals}, " +
                $"averageWheelVisualRadius={wheelVisualVerticalRadiusTotal / wheelVisuals:F3}, " +
                $"animatedWheelVisuals={animatedWheelVisuals}, " +
                $"authoredWheelAxes={authoredWheelAxes}, " +
                $"outwardWheelFaces={outwardWheelFaces}, " +
                $"axleAlignedWheelVisuals={axleAlignedWheelVisuals}, " +
                $"physicalWheelColliders={physicalWheelColliders}, " +
                $"climbContacts={climbContactColliders}, " +
                $"visibleRenderers={visibleRenderers}, decalSafe={decalSafeOpaqueMaterials}, " +
                $"raisedCenterSeat=true, loadingAtDriverDoor=true, " +
                $"transparentGlass=true, windshieldDecalAtlas=true, cargoCapacity=4.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static UnityEngine.Object CreateVehicleType()
    {
        var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AudiAssetPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R VehicleType reference asset was not found.");

        var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetAssetPath);
        if (target == null)
        {
            if (!AssetDatabase.CopyAsset(AudiAssetPath, TargetAssetPath))
                throw new InvalidOperationException("Could not create the Bigfoot VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TargetAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }
        if (target == null)
            throw new InvalidOperationException("Generated Bigfoot VehicleType asset did not load.");

        target.name = "BigfootMonsterTruck";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 345000f);
        SetNumber(serialized, "maxFuel", 110f);
        SetNumber(serialized, "maxCargoCapacity", 4f);
        SetNumber(serialized, "maxSpeed", 140f);
        SetNumber(serialized, "enginePower", 1200f);
        SetNumber(serialized, "brakeForce", 32000f);
        SetNumber(serialized, "turnRadius", 30f);
        SetNumber(serialized, "damageIntensity", 0.38f);
        SetBool(serialized, "isATruck", false);
        SetBool(serialized, "fitsHandTruck", true);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", false);
        SetBool(serialized, "hasRadio", true);
        SetBool(serialized, "enclosed", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        return target;
    }

    private static void CreateVehiclePrefab(UnityEngine.Object vehicleType)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(AudiPrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R reference prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("Processed Bigfoot GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "BigfootMonsterTruck";
        try
        {
            StripAudiGeometry(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureWheelContactColliders(root);
            ConfigureExitMarkers(root);
            ConfigureVehicleReferences(root, vehicleType);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the processed Bigfoot model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "BigfootVisual";
            ConfigureWindshieldMaterial(modelInstance, CreateWindshieldMaterial());
            AssignPersistentOpaqueMaterials(modelInstance);
            var materialFix = BigfootMonsterTruckMaterials.FixSolidMaterials(modelInstance);
            MarkOpaqueMaterialsDirty(modelInstance);
            Debug.Log(
                $"BigfootMonsterTruck: prepared decal-safe opaque materials " +
                $"renderers={materialFix.RendererCount}, " +
                $"decalMasksCleared={materialFix.DecalMasksCleared}, " +
                $"opaqueFixed={materialFix.OpaqueMaterialsFixed}, " +
                $"hdrpValidated={materialFix.MaterialsValidated}.");
            AttachWheelVisuals(root, modelInstance);
            CreateSeatAnchor(root);
            ConfigureRendererReferences(root, modelInstance);

            var result = PrefabUtility.SaveAsPrefabAsset(root, TargetPrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Bigfoot vehicle prefab.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void StripAudiGeometry(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.enabled = false;
            renderer.sharedMaterials = Array.Empty<Material>();
        }
        foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderer.enabled = false;
            renderer.sharedMesh = null;
            renderer.sharedMaterials = Array.Empty<Material>();
        }
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            filter.sharedMesh = null;
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>();
        if (body == null)
            throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        body.mass = 6500f;
        body.drag = 0.02f;
        body.angularDrag = 0.12f;
        body.centerOfMass = new Vector3(0f, 0.72f, 0f);
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        SetLocalPosition(root, "FrontLeft_WheelController", new Vector3(-1.35f, AxleHeight, 1.60f));
        SetLocalPosition(root, "FrontRight_WheelController", new Vector3(1.35f, AxleHeight, 1.60f));
        SetLocalPosition(root, "RearLeft_WheelController", new Vector3(-1.35f, AxleHeight, -1.60f));
        SetLocalPosition(root, "RearRight_WheelController", new Vector3(1.35f, AxleHeight, -1.60f));
    }

    private static void ConfigureBodyColliders(GameObject root)
    {
        var holder = FindTransform(root.transform, "BodyCollider") ??
                     throw new InvalidOperationException("Reference BodyCollider is missing.");
        var colliders = holder.GetComponents<BoxCollider>();
        if (colliders.Length < 2)
            throw new InvalidOperationException("Reference vehicle needs two body colliders.");
        colliders[0].center = new Vector3(0f, 1.2f, -0.05f);
        colliders[0].size = new Vector3(2.5f, 0.55f, 5.3f);
        colliders[1].center = new Vector3(0f, 2.05f, 0.1f);
        colliders[1].size = new Vector3(2.2f, 1.2f, 3.7f);
    }

    private static void ConfigureWheelContactColliders(GameObject root)
    {
        var holder = new GameObject("BigfootWheelContactColliders")
        {
            layer = 12,
            tag = "Wheel",
        };
        holder.transform.SetParent(root.transform, false);
        var centers = new[]
        {
            new Vector3(-1.35f, 0.58f, 1.60f),
            new Vector3(1.35f, 0.58f, 1.60f),
            new Vector3(-1.35f, 0.58f, -1.60f),
            new Vector3(1.35f, 0.58f, -1.60f),
        };
        foreach (var center in centers)
        {
            var collider = holder.AddComponent<SphereCollider>();
            collider.center = center;
            collider.radius = 0.58f;
        }
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        SetLocalPosition(root, "Driverside", new Vector3(-2.05f, 0.1f, 0.1f));
        SetLocalPosition(root, "Passengerside", new Vector3(2.05f, 0.1f, 0.1f));
        SetLocalPosition(root, "LoadingPosition", new Vector3(-2.05f, 0.1f, 0.1f));
    }

    private static void ConfigureVehicleReferences(GameObject root, UnityEngine.Object vehicleType)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var vehicleTypeProperty = serialized.FindProperty("vehicleType");
            if (vehicleTypeProperty?.propertyType == SerializedPropertyType.ObjectReference)
                vehicleTypeProperty.objectReferenceValue = vehicleType;
            var instance = serialized.FindProperty("vehicleInstance");
            var typeName = instance?.FindPropertyRelative("vehicleTypeName");
            if (typeName?.propertyType == SerializedPropertyType.String)
                typeName.stringValue = VehicleTypeName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var mapping = new Dictionary<string, string>
        {
            { "WheelFrontLeftVisual", "FrontLeft_WheelController" },
            { "WheelFrontRightVisual", "FrontRight_WheelController" },
            { "WheelRearLeftVisual", "RearLeft_WheelController" },
            { "WheelRearRightVisual", "RearRight_WheelController" },
        };

        foreach (var pair in mapping)
        {
            var wheel = FindTransform(model.transform, pair.Key) ??
                        throw new InvalidOperationException($"Model wheel '{pair.Key}' is missing.");
            var target = FindWheelVisualTarget(root, pair.Value) ??
                         throw new InvalidOperationException(
                             $"Wheel visual reference for '{pair.Value}' is missing.");
            var wheelRotation = wheel.rotation;
            var wheelScale = wheel.lossyScale;
            var controller = FindTransform(root.transform, pair.Value) ??
                             throw new InvalidOperationException(
                                 $"Wheel controller '{pair.Value}' is missing.");
            wheel.SetParent(target.parent, false);
            wheel.SetPositionAndRotation(
                controller.position + root.transform.up * WheelPrefabVerticalOffset,
                wheelRotation * Quaternion.AngleAxis(180f, Vector3.up));
            wheel.localScale = new Vector3(-Mathf.Abs(wheelScale.x), wheelScale.y, wheelScale.z);
            AssignWheelVisual(root, pair.Value, wheel.gameObject);
        }
    }

    private static void AssignWheelVisual(GameObject root, string controllerName, GameObject visualObject)
    {
        var controller = FindTransform(root.transform, controllerName) ??
                         throw new InvalidOperationException($"Wheel controller '{controllerName}' is missing.");
        foreach (var component in controller.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var wheel = serialized.FindProperty("wheel");
            var visual = wheel?.FindPropertyRelative("visual");
            if (visual?.propertyType != SerializedPropertyType.ObjectReference)
                continue;
            visual.objectReferenceValue = visualObject;
            var visualTransform = wheel?.FindPropertyRelative("visualTransform");
            if (visualTransform?.propertyType == SerializedPropertyType.ObjectReference)
                visualTransform.objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return;
        }
        throw new InvalidOperationException($"Wheel controller '{controllerName}' has no visual property.");
    }

    private static bool TryGetAssignedWheelVisual(Transform controller, out GameObject visualObject)
    {
        foreach (var component in controller.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var visual = serialized.FindProperty("wheel")?.FindPropertyRelative("visual");
            if (visual?.objectReferenceValue is GameObject gameObject)
            {
                visualObject = gameObject;
                return true;
            }
        }
        visualObject = null!;
        return false;
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return true;
    }

    private static Transform? FindWheelVisualTarget(GameObject root, string controllerName)
    {
        var controller = FindTransform(root.transform, controllerName);
        if (controller == null)
            return null;
        foreach (var component in controller.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var visual = serialized.FindProperty("wheel")?.FindPropertyRelative("visual");
            if (visual?.objectReferenceValue is Transform transform)
                return transform;
        }
        return controller;
    }

    private static void CreateSeatAnchor(GameObject root)
    {
        var anchor = new GameObject("BigfootDriverSeat");
        anchor.transform.SetParent(root.transform, false);
        anchor.transform.localPosition = new Vector3(0f, 2.02f, 0.18f);
        anchor.transform.localRotation = Quaternion.identity;
    }

    private static void ConfigureRendererReferences(GameObject root, GameObject model)
    {
        var renderers = new List<Renderer>(root.GetComponentsInChildren<Renderer>(true));

        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            AssignRendererArray(serialized.FindProperty("bodyMeshes"), renderers);
            AssignRendererArray(serialized.FindProperty("renderers"), renderers);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static Material CreateWindshieldMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(WindshieldMaterialPath);
        if (material == null)
        {
            var shader = Shader.Find("Bigfoot/TransparentWindshield") ??
                         throw new InvalidOperationException("The Bigfoot windshield shader is unavailable.");
            material = new Material(shader) { name = "Bigfoot_Windshield_Glass" };
            AssetDatabase.CreateAsset(material, WindshieldMaterialPath);
        }

        material.shader = Shader.Find("Bigfoot/TransparentWindshield") ??
                          throw new InvalidOperationException("The Bigfoot windshield shader is unavailable.");
        material.name = "Bigfoot_Windshield_Glass";
        material.color = new Color(0.78f, 0.84f, 0.88f, 0.2f);
        material.SetFloat("_Mode", 3f);
        material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_Cull", (int)CullMode.Off);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void AssignPersistentOpaqueMaterials(GameObject model)
    {
        EnsureAssetFolder(OpaqueMaterialFolder);
        var replacements = new Dictionary<Material, Material>();
        var materialIndex = 0;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null || BigfootMonsterTruckMaterials.IsTransparentMaterial(source))
                    continue;
                if (!replacements.TryGetValue(source, out var persistent))
                {
                    var assetName = $"BigfootOpaque_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
                    var path = $"{OpaqueMaterialFolder}/{assetName}.mat";
                    persistent = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (persistent == null)
                    {
                        persistent = new Material(source) { name = assetName };
                        AssetDatabase.CreateAsset(persistent, path);
                    }
                    else
                    {
                        persistent.CopyPropertiesFromMaterial(source);
                        persistent.shader = source.shader;
                        persistent.name = assetName;
                    }
                    replacements.Add(source, persistent);
                    materialIndex++;
                }
                materials[index] = persistent;
                changed = true;
            }
            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private static void MarkOpaqueMaterialsDirty(GameObject model)
    {
        var materials = new HashSet<Material>();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null &&
                    !BigfootMonsterTruckMaterials.IsTransparentMaterial(material) &&
                    materials.Add(material))
                    EditorUtility.SetDirty(material);
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;
        var separator = folder.LastIndexOf('/');
        if (separator <= 0)
            throw new InvalidOperationException($"Invalid asset folder '{folder}'.");
        var parent = folder.Substring(0, separator);
        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, folder.Substring(separator + 1));
    }

    private static string SanitizeAssetName(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '-' && chars[index] != '_')
                chars[index] = '_';
        return new string(chars);
    }

    private static bool IsBigfootVisualRenderer(Transform transform)
    {
        if (transform.name.StartsWith("Wheel", StringComparison.Ordinal) &&
            transform.name.EndsWith("Visual", StringComparison.Ordinal))
            return true;
        for (var current = transform; current != null; current = current.parent)
            if (string.Equals(current.name, "BigfootVisual", StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void ConfigureWindshieldMaterial(GameObject model, Material windshieldMaterial)
    {
        Texture? decalAtlas = null;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null ||
                    !string.Equals(material.name, "Material_3", StringComparison.OrdinalIgnoreCase))
                    continue;
                decalAtlas = material.mainTexture;
                if (decalAtlas != null)
                    break;
            }
            if (decalAtlas != null)
                break;
        }

        if (decalAtlas == null)
            throw new InvalidOperationException("The body decal atlas required by the windshield was not found.");
        windshieldMaterial.SetTexture("_MainTex", decalAtlas);

        var replacements = 0;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                if (materials[index] == null ||
                    materials[index].name.IndexOf("Windshield_Glass", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                materials[index] = windshieldMaterial;
                changed = true;
                replacements++;
            }
            if (changed)
                renderer.sharedMaterials = materials;
        }
        if (replacements == 0)
            throw new InvalidOperationException("No windshield material slots were found on the model.");
    }

    private static void AssignRendererArray(SerializedProperty? property, List<Renderer> renderers)
    {
        if (property == null || !property.isArray || property.propertyType == SerializedPropertyType.String)
            return;
        property.arraySize = renderers.Count;
        for (var index = 0; index < renderers.Count; index++)
        {
            var element = property.GetArrayElementAtIndex(index);
            if (element.propertyType == SerializedPropertyType.ObjectReference)
                element.objectReferenceValue = renderers[index];
        }
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        return null;
    }

    private static void SetLocalPosition(GameObject root, string name, Vector3 position)
    {
        var transform = FindTransform(root.transform, name) ??
                        throw new InvalidOperationException($"Reference transform '{name}' is missing.");
        transform.localPosition = position;
    }

    private static void SetString(SerializedObject target, string name, string value)
    {
        var property = target.FindProperty(name);
        if (property != null)
            property.stringValue = value;
    }

    private static void SetBool(SerializedObject target, string name, bool value)
    {
        var property = target.FindProperty(name);
        if (property != null)
        property.boolValue = value;
    }

    private static void SetNumber(SerializedObject target, string name, float value)
    {
        var property = target.FindProperty(name);
        if (property == null)
            return;
        if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else if (property.propertyType == SerializedPropertyType.Float)
            property.floatValue = value;
    }
}
