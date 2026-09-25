#nullable enable
using System;
using System.Collections.Generic;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class BugattiChironSetup
{
    private const string ModRoot = "Assets/Mods/BugattiChiron";
    private const string AudiAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string AudiPrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/free_bugatti_chiron.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string VehicleAssetPath = ModRoot + "/BugattiChiron.asset";
    private const string VehiclePrefabPath = ModRoot + "/BugattiChiron.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/BugattiChiron.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/bugattichiron.unity3d";
    private const string MacBundlePath =
        ModRoot + "/AssetBundles/Mac/bugattichiron.unity3d";
    private const string VehicleTypeName =
        "bugattichiron-vehicle:vehicletype_bugattichiron";
    private const float TargetLength = 4.544f;
    private const float VehicleLinearDrag = 0f;
    private const float ForcedInductionPowerMultiplier = 1f;
    private const float SimulationEnginePowerKw = 850f;
    private const float PhysicalBrakeTorque = 4200f;
    private const float DamageDecelerationThreshold = 500f;
    private const float DamageIntensity = 0.6f;
    private const float DeformationRadius = 0.48f;
    private const float DeformationStrength = 0.32f;

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-0.7945f, 0.51f, 1.3155f) },
            { "FrontRight_WheelController", new Vector3(0.7945f, 0.51f, 1.3155f) },
            { "RearLeft_WheelController", new Vector3(-0.7505f, 0.51f, -1.3955f) },
            { "RearRight_WheelController", new Vector3(0.7505f, 0.51f, -1.3955f) },
        };

    private static readonly float[] ChironGears =
    {
        -2.96f,
        0f,
        4.10f,
        2.60f,
        1.80f,
        1.35f,
        1.00f,
        0.78f,
        0.62f,
    };

    [MenuItem("Big Ambitions Mods/Setup Bugatti Chiron")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "BugattiChiron setup complete: generated an original-spec driveable Chiron " +
            "with four wheel visuals, seven-speed DSG, permanent AWD base, static Chiron " +
            "lamp geometry and decal-safe opaque materials.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    public static void VerifyBuiltBundle()
    {
        VerifyBuiltBundle(WindowsBundlePath);
        VerifyBuiltBundle(MacBundlePath);
    }

    private static void VerifyBuiltBundle(string bundlePath)
    {
        var bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{bundlePath}'.");

        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException("Bundle is missing its VehicleType or prefab.");

            var vehicleSerialized = new SerializedObject(vehicleType);
            var price = ReadNumber(vehicleSerialized.FindProperty("price"));
            var maxFuel = ReadNumber(vehicleSerialized.FindProperty("maxFuel"));
            var maxSpeed = ReadNumber(vehicleSerialized.FindProperty("maxSpeed"));
            var enginePower = ReadNumber(vehicleSerialized.FindProperty("enginePower"));
            var luxury = vehicleSerialized.FindProperty("isLuxuryCar")?.boolValue ?? true;

            var visual = FindTransform(prefab.transform, "BugattiVisual") ??
                         throw new InvalidOperationException("Bugatti visual root is missing.");
            if (!TryGetBugattiRendererBounds(prefab.transform, out var bounds))
                throw new InvalidOperationException("Bugatti visual has no renderer bounds.");
            var leftDoor = FindTransform(visual, "Door-left");
            var rightDoor = FindTransform(visual, "Door-right");
            var bodySidesOriented =
                leftDoor != null && rightDoor != null &&
                TryGetRendererBounds(leftDoor, out var leftDoorBounds) &&
                TryGetRendererBounds(rightDoor, out var rightDoorBounds) &&
                Math.Abs(leftDoorBounds.center.x - rightDoorBounds.center.x) > 0.5f &&
                Math.Abs(leftDoorBounds.center.x - rightDoorBounds.center.x) >
                Math.Abs(leftDoorBounds.center.y - rightDoorBounds.center.y) * 2f;
            var windshield = FindTransform(visual, "Windshields");
            var exhaust = FindTransform(visual, "Plastic-parts_exhaust_0");
            var bodyUpright =
                windshield != null && exhaust != null &&
                TryGetRendererBounds(windshield, out var windshieldBounds) &&
                TryGetRendererBounds(exhaust, out var exhaustBounds) &&
                windshieldBounds.center.y > exhaustBounds.center.y + 0.25f;

            var wheelVisuals = 0;
            var wheelGeometryOriented = true;
            var continuousTailLight = false;
            var thirdBrakeLight = false;
            var frontBlinkerMeshes = 0;
            var sideBlinkerMeshes = 0;
            var templateLights = prefab.GetComponentsInChildren<Light>(true);
            var headlightTemplateValid = templateLights.Length == 1 &&
                                         templateLights[0].name == "Spotlights" &&
                                         !templateLights[0].enabled;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.StartsWith("BugattiWheel", StringComparison.Ordinal))
                {
                    wheelVisuals++;
                    if (!TryGetRendererBounds(transform, out var wheelBounds) ||
                        wheelBounds.size.x > 0.40f ||
                        wheelBounds.size.y < 0.62f || wheelBounds.size.y > 0.76f ||
                        wheelBounds.size.z < 0.62f || wheelBounds.size.z > 0.76f)
                    {
                        wheelGeometryOriented = false;
                    }
                }
                if (string.Equals(
                        transform.name,
                        "Tail-light_Tail-light-LIGHT_0",
                        StringComparison.Ordinal))
                {
                    continuousTailLight = true;
                }
                if (string.Equals(transform.name, "Tail-light_Brake-lights_0", StringComparison.Ordinal))
                    thirdBrakeLight = true;
                if (transform.name.StartsWith("Headlight_Turning_lights_", StringComparison.Ordinal))
                    frontBlinkerMeshes++;
                if (transform.name.StartsWith("Door-left_Turning_lights_", StringComparison.Ordinal) ||
                    transform.name.StartsWith("Door-right_Turning_lights_", StringComparison.Ordinal))
                    sideBlinkerMeshes++;
            }

            var transmissionVerified = false;
            var launchResponseVerified = false;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null ||
                    !string.Equals(
                        component.GetType().FullName,
                        "NWH.VehiclePhysics2.VehicleController",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var serialized = new SerializedObject(component);
                var powertrain = serialized.FindProperty("powertrain");
                var transmission = powertrain?.FindPropertyRelative("transmission");
                var gearCount = transmission?.FindPropertyRelative("forwardGearCount")?.intValue ?? 0;
                var gears = transmission?.FindPropertyRelative("gears");
                transmissionVerified = gearCount == 7 && gears != null && gears.arraySize == 9;
                var clutch = powertrain?.FindPropertyRelative("clutch");
                var engine = powertrain?.FindPropertyRelative("engine");
                var forcedInduction = engine?.FindPropertyRelative("forcedInduction");
                launchResponseVerified =
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRPM")) - 1200f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("throttleEngagementOffsetRPM")) - 500f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRange")) - 500f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("creepTorque"))) < 0.01f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("inertia")) - 0.12f) < 0.001f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("idleRPM")) - 900f) < 0.01f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("startDuration")) - 0.5f) < 0.001f &&
                    Math.Abs(ReadNumber(
                        transmission?.FindPropertyRelative("_downshiftRPM")) - 3200f) < 0.01f &&
                    Math.Abs(ReadNumber(
                        forcedInduction?.FindPropertyRelative("powerGainMultiplier")) -
                        ForcedInductionPowerMultiplier) < 0.001f &&
                    !(engine?.FindPropertyRelative("stallingEnabled")?.boolValue ?? true);
            }

            var rigidbody = prefab.GetComponent<Rigidbody>();
            var accelerationDragVerified = rigidbody != null &&
                                           Math.Abs(rigidbody.drag - VehicleLinearDrag) < 0.0001f;
            var visualDamageHandlerVerified = false;
            var legacyDeformationDisabled = false;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;
                if (string.Equals(
                        component.GetType().Name,
                        "VehicleDeformationController",
                        StringComparison.Ordinal))
                {
                    var legacy = new SerializedObject(component);
                    legacyDeformationDisabled = !component.enabled &&
                                                (legacy.FindProperty("meshFilters")?.arraySize ?? -1) == 0;
                    continue;
                }
                if (!string.Equals(
                        component.GetType().FullName,
                        "NWH.VehiclePhysics2.Damage.DamageHandler",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var damage = new SerializedObject(component);
                visualDamageHandlerVerified =
                    !(damage.FindProperty("meshDeform")?.boolValue ?? true) &&
                    Math.Abs(ReadNumber(damage.FindProperty("decelerationThreshold")) -
                             DamageDecelerationThreshold) < 0.01f &&
                    Math.Abs(ReadNumber(damage.FindProperty("deformationRadius")) -
                             DeformationRadius) < 0.001f &&
                    Math.Abs(ReadNumber(damage.FindProperty("deformationStrength")) -
                             DeformationStrength) < 0.001f;
            }

            var opaqueMaterials = new HashSet<Material>();
            var decalSafeMaterials = 0;
            var transparentMaterials = 0;
            var transparentMaterialsDoubleSided = true;
            var cabinGlassTintValid = true;
            var opaqueRendererMasksSafe = true;
            var paintRenderers = new HashSet<Renderer>();
            var bodyPaintSlots = 0;
            var darkBodyPaintSlots = 0;
            var rimPaintSlots = 0;
            var interiorPrimaryPaintSlots = 0;
            var interiorSecondaryPaintSlots = 0;
            var interiorDarkPaintSlots = 0;
            var caliperSlots = 0;
            var rimInnerSlots = 0;
            var seatSlots = 0;
            var paintTexturesReadable = true;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!BugattiChironMaterials.IsBugattiRenderer(renderer.transform))
                    continue;

                var hasOpaque = false;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                        continue;
                    if (IsBodyPaintMaterial(material))
                    {
                        paintRenderers.Add(renderer);
                        bodyPaintSlots++;
                    }
                    if (IsDarkBodyPaintMaterial(material)) darkBodyPaintSlots++;
                    if (IsRimPaintMaterial(material))
                    {
                        rimPaintSlots++;
                        paintTexturesReadable &= IsBaseTextureReadable(material);
                    }
                    if (IsInteriorPrimaryPaintMaterial(material)) interiorPrimaryPaintSlots++;
                    if (IsInteriorSecondaryPaintMaterial(material)) interiorSecondaryPaintSlots++;
                    if (IsInteriorDarkPaintMaterial(material)) interiorDarkPaintSlots++;
                    if (IsCaliperMaterial(material)) caliperSlots++;
                    if (IsRimInnerPaintMaterial(material))
                    {
                        rimInnerSlots++;
                        paintTexturesReadable &= IsBaseTextureReadable(material);
                    }
                    if (IsSeatPaintMaterial(material))
                    {
                        seatSlots++;
                        paintTexturesReadable &= IsBaseTextureReadable(material);
                    }
                    if (BugattiChironMaterials.IsTransparentMaterial(material))
                    {
                        transparentMaterials++;
                        transparentMaterialsDoubleSided &=
                            (!material.HasProperty("_Cull") || material.GetFloat("_Cull") < 0.5f) &&
                            (!material.HasProperty("_DoubleSidedEnable") ||
                             material.GetFloat("_DoubleSidedEnable") > 0.5f) &&
                            material.IsKeywordEnabled("_DOUBLESIDED_ON");
                        if (BugattiChironMaterials.IsCabinGlassMaterial(material))
                        {
                            var tint = material.HasProperty("_BaseColor")
                                ? material.GetColor("_BaseColor")
                                : material.HasProperty("baseColorFactor")
                                    ? material.GetColor("baseColorFactor")
                                    : Color.black;
                            cabinGlassTintValid &= tint.r >= 0.08f &&
                                                   tint.a >= 0.26f &&
                                                   tint.a <= 0.30f;
                        }
                        continue;
                    }

                    hasOpaque = true;
                    if (!opaqueMaterials.Add(material))
                        continue;
                    if (string.Equals(material.shader.name, "HDRP/Lit", StringComparison.Ordinal) &&
                        (!material.HasProperty("_SupportDecals") ||
                         material.GetFloat("_SupportDecals") < 0.5f) &&
                        material.IsKeywordEnabled("_DISABLE_DECALS") &&
                        (!material.HasProperty("_ZWrite") || material.GetFloat("_ZWrite") > 0.5f) &&
                        material.renderQueue == (int)RenderQueue.Geometry)
                    {
                        decalSafeMaterials++;
                    }
                }

                if (hasOpaque && (renderer.renderingLayerMask & 0x0000FF00u) != 0)
                    opaqueRendererMasksSafe = false;
            }

            var paintReferencesValid = false;
            MonoBehaviour? carFeatures = null;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component != null &&
                    string.Equals(component.GetType().Name, "CarFeatures", StringComparison.Ordinal))
                {
                    carFeatures = component;
                    break;
                }
            }
            if (carFeatures != null)
            {
                var bodyMeshes = new SerializedObject(carFeatures).FindProperty("bodyMeshes");
                if (bodyMeshes != null && bodyMeshes.isArray && bodyMeshes.arraySize == paintRenderers.Count)
                {
                    paintReferencesValid = true;
                    for (var index = 0; index < bodyMeshes.arraySize; index++)
                    {
                        if (!(bodyMeshes.GetArrayElementAtIndex(index).objectReferenceValue is Renderer renderer) ||
                            !paintRenderers.Contains(renderer))
                        {
                            paintReferencesValid = false;
                            break;
                        }
                    }
                }
            }

            if (Math.Abs(price - 2400000f) > 0.5f ||
                Math.Abs(maxFuel - 100f) > 0.5f ||
                Math.Abs(maxSpeed - 420f) > 0.5f ||
                Math.Abs(enginePower - 1103f) > 0.5f ||
                !luxury ||
                bounds.size.z < 4.45f || bounds.size.z > 4.65f ||
                bounds.size.x < 1.90f || bounds.size.x > 2.15f ||
                bounds.size.y < 1.05f || bounds.size.y > 1.40f ||
                !bodySidesOriented ||
                !bodyUpright ||
                wheelVisuals != 4 ||
                !wheelGeometryOriented ||
                !continuousTailLight ||
                !thirdBrakeLight ||
                frontBlinkerMeshes != 2 ||
                sideBlinkerMeshes != 2 ||
                !headlightTemplateValid ||
                !transmissionVerified ||
                !launchResponseVerified ||
                !accelerationDragVerified ||
                !visualDamageHandlerVerified ||
                !legacyDeformationDisabled ||
                opaqueMaterials.Count == 0 ||
                decalSafeMaterials != opaqueMaterials.Count ||
                !opaqueRendererMasksSafe ||
                transparentMaterials == 0 ||
                !transparentMaterialsDoubleSided ||
                !cabinGlassTintValid ||
                bodyPaintSlots == 0 ||
                darkBodyPaintSlots == 0 ||
                rimPaintSlots != 4 ||
                interiorPrimaryPaintSlots == 0 ||
                interiorSecondaryPaintSlots == 0 ||
                interiorDarkPaintSlots == 0 ||
                caliperSlots != 4 ||
                rimInnerSlots != 4 ||
                seatSlots != 1 ||
                !paintTexturesReadable ||
                !paintReferencesValid)
            {
                throw new InvalidOperationException(
                    $"Bundle verification failed: price={price}, fuel={maxFuel}, " +
                    $"speed={maxSpeed}, power={enginePower}, luxury={luxury}, " +
                    $"bounds={bounds.size}, bodySidesOriented={bodySidesOriented}, " +
                    $"bodyUpright={bodyUpright}, " +
                    $"wheels={wheelVisuals}, " +
                    $"wheelGeometryOriented={wheelGeometryOriented}, " +
                    $"continuousTailLight={continuousTailLight}, thirdBrakeLight={thirdBrakeLight}, " +
                    $"frontBlinkers={frontBlinkerMeshes}, sideBlinkers={sideBlinkerMeshes}, " +
                    $"headlightTemplate={headlightTemplateValid}, " +
                    $"sevenSpeed={transmissionVerified}, launchResponse={launchResponseVerified}, " +
                    $"accelerationDrag={accelerationDragVerified}, " +
                    $"visualDamage={visualDamageHandlerVerified}, " +
                    $"legacyDeformationDisabled={legacyDeformationDisabled}, " +
                    $"opaque={opaqueMaterials.Count}, " +
                    $"decalSafe={decalSafeMaterials}, transparent={transparentMaterials}, " +
                    $"transparentDoubleSided={transparentMaterialsDoubleSided}, " +
                    $"cabinGlassTint={cabinGlassTintValid}, " +
                    $"bodyPaintSlots={bodyPaintSlots}, darkBodyPaintSlots={darkBodyPaintSlots}, " +
                    $"rimPaintSlots={rimPaintSlots}, " +
                    $"interiorPaintSlots={interiorPrimaryPaintSlots}/" +
                    $"{interiorSecondaryPaintSlots}/{interiorDarkPaintSlots}, " +
                    $"caliperSlots={caliperSlots}, rimInnerSlots={rimInnerSlots}, " +
                    $"seatSlots={seatSlots}, " +
                    $"paintTexturesReadable={paintTexturesReadable}, " +
                    $"paintReferences={paintReferencesValid}, " +
                    $"rendererMasksSafe={opaqueRendererMasksSafe}.");
            }

            Debug.Log(
                $"BugattiChiron bundle verified: price={price}, speed={maxSpeed}, " +
                $"power={enginePower}, bounds={bounds.size}, wheels=4, sevenSpeed=true, " +
                $"launchResponse=true, accelerationDrag=true, visualDamage=true, " +
                $"continuousTailLight=true, thirdBrakeLight=true, blinkers=4, " +
                $"headlightTemplate=true, transparentDoubleSided=true, cabinGlassTint=true, " +
                $"bodyPaintSlots={bodyPaintSlots}, darkBodyPaintSlots={darkBodyPaintSlots}, " +
                $"rimPaintSlots={rimPaintSlots}, rimInnerSlots={rimInnerSlots}, " +
                $"interiorPaint=true, calipersPainted=true, " +
                $"seatsPainted=true, paintTexturesReadable=true, " +
                $"decalSafeMaterials={decalSafeMaterials}.");
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

        var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (target == null)
        {
            if (!AssetDatabase.CopyAsset(AudiAssetPath, VehicleAssetPath))
                throw new InvalidOperationException("Could not create the Bugatti VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated Bugatti VehicleType asset did not load.");

        target.name = "BugattiChiron";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 2400000f);
        SetNumber(serialized, "maxFuel", 100f);
        SetNumber(serialized, "maxCargoCapacity", 12f);
        SetNumber(serialized, "maxSpeed", 420f);
        SetNumber(serialized, "enginePower", 1103f);
        SetNumber(serialized, "brakeForce", 30000f);
        SetNumber(serialized, "turnRadius", 30f);
        SetNumber(serialized, "damageIntensity", 0.45f);
        SetBool(serialized, "isATruck", false);
        SetBool(serialized, "isHandVehicle", false);
        SetBool(serialized, "fitsHandTruck", false);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", true);
        SetBool(serialized, "hasRadio", true);
        SetBool(serialized, "isLuxuryCar", true);
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
            throw new InvalidOperationException("Bugatti GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "BugattiChiron";
        try
        {
            StripAudiGeometry(root);
            RemoveAudiSpecificBehaviours(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureExitMarkers(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the Bugatti model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "BugattiVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            AssignPersistentMaterials(modelInstance);
            AttachWheelVisuals(root, modelInstance);
            ConfigureVisualDamage(root);
            var fix = BugattiChironMaterials.FixSolidMaterials(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);

            Debug.Log(
                $"BugattiChiron: prepared decal-safe materials renderers={fix.RendererCount}, " +
                $"decalMasksCleared={fix.DecalMasksCleared}, " +
                $"opaqueFixed={fix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={fix.TransparentMaterialsFixed}, " +
                $"hdrpValidated={fix.MaterialsValidated}.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Bugatti vehicle prefab.");
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

    private static void RemoveAudiSpecificBehaviours(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null &&
                component.GetType().Name.StartsWith("AudiRS6R", StringComparison.Ordinal))
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }
    }

    private static void RemoveModelLights(GameObject model)
    {
        foreach (var light in model.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(light.gameObject);
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        body.mass = 1995f;
        body.drag = VehicleLinearDrag;
        body.angularDrag = 1.35f;
        body.centerOfMass = new Vector3(0f, 0.26f, 0f);
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        foreach (var pair in WheelControllerPositions)
        {
            var transform = FindTransform(root.transform, pair.Key) ??
                            throw new InvalidOperationException($"Wheel controller '{pair.Key}' is missing.");
            transform.localPosition = pair.Value;
            var isFront = pair.Key.StartsWith("Front", StringComparison.Ordinal);
            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                var serialized = new SerializedObject(component);
                SetRelativeNumber(serialized, "spring.maxLength", 0.18f);
                SetRelativeNumber(serialized, "spring.maxForce", 22000f);
                SetRelativeNumber(serialized, "wheel.radius", isFront ? 0.34f : 0.355f);
                SetRelativeNumber(serialized, "wheel.width", isFront ? 0.285f : 0.355f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static void ConfigureBodyColliders(GameObject root)
    {
        var holder = FindTransform(root.transform, "BodyCollider") ??
                     throw new InvalidOperationException("Reference BodyCollider is missing.");
        var colliders = holder.GetComponents<BoxCollider>();
        if (colliders.Length < 2)
            throw new InvalidOperationException("Reference vehicle requires two body colliders.");

        colliders[0].center = new Vector3(0f, 0.43f, 0f);
        colliders[0].size = new Vector3(1.98f, 0.48f, 4.45f);
        colliders[1].center = new Vector3(0f, 0.86f, -0.08f);
        colliders[1].size = new Vector3(1.70f, 0.72f, 2.75f);
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        SetLocalPosition(root, "Driverside", new Vector3(-1.5f, 0.1f, 0.1f));
        SetLocalPosition(root, "Passengerside", new Vector3(1.5f, 0.1f, 0.1f));
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

    private static void ConfigurePowertrain(GameObject root)
    {
        var found = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            if (string.Equals(
                    component.GetType().FullName,
                    "NWH.VehiclePhysics2.VehicleController",
                    StringComparison.Ordinal))
            {
                found = true;
                SetRelativeNumber(serialized, "brakes.maxTorque", PhysicalBrakeTorque);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1200f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 500f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 500f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.12f);
                SetRelativeNumber(
                    serialized,
                    "powertrain.engine.maxPower",
                    SimulationEnginePowerKw);
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 900f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 6700f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.5f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", true);
                SetRelativeNumber(
                    serialized,
                    "powertrain.engine.forcedInduction.powerGainMultiplier",
                    ForcedInductionPowerMultiplier);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0.08f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.2f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 7f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.08f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 3200f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 6500f);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = ChironGears.Length;
                for (var index = 0; index < ChironGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = ChironGears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 420f);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!found)
            throw new InvalidOperationException("NWH vehicle controller was not found on the reference prefab.");
    }

    private static void ConfigureVisualDamage(GameObject root)
    {
        var damageHandlerFound = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;

            if (string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
            {
                component.enabled = false;
                var legacy = new SerializedObject(component);
                var meshFilters = legacy.FindProperty("meshFilters");
                var originalMeshes = legacy.FindProperty("originalMeshes");
                if (meshFilters != null)
                    meshFilters.arraySize = 0;
                if (originalMeshes != null)
                    originalMeshes.arraySize = 0;
                legacy.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(component);
                continue;
            }

            if (!string.Equals(
                    component.GetType().FullName,
                    "NWH.VehiclePhysics2.Damage.DamageHandler",
                    StringComparison.Ordinal))
            {
                continue;
            }

            damageHandlerFound = true;
            var serialized = new SerializedObject(component);
            SetRelativeBool(serialized, "meshDeform", false);
            SetRelativeNumber(serialized, "collisionTimeout", 0.8f);
            SetRelativeNumber(serialized, "damageIntensity", DamageIntensity);
            SetRelativeNumber(serialized, "decelerationThreshold", DamageDecelerationThreshold);
            SetRelativeNumber(serialized, "deformationRadius", DeformationRadius);
            SetRelativeNumber(serialized, "deformationRandomness", 0.01f);
            SetRelativeNumber(serialized, "deformationStrength", DeformationStrength);
            SetRelativeNumber(serialized, "deformationVerticesPerFrame", 8000f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!damageHandlerFound)
            throw new InvalidOperationException("NWH damage handler was not found on the reference prefab.");
    }

    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        // The imported GLB is longitudinal on Y with its authored roof toward -Z.
        // The additional -90-degree roll puts the roof on Unity Y and side windows on X.
        model.transform.localRotation =
            Quaternion.AngleAxis(-90f, Vector3.forward) *
            Quaternion.AngleAxis(120f, new Vector3(1f, 1f, 1f).normalized);
        model.transform.localScale = Vector3.one;

        if (!TryGetRendererBounds(model.transform, out var bounds))
            throw new InvalidOperationException("Bugatti model contains no renderers.");

        var headlights = FindTransform(model.transform, "Headlight");
        var tailLights = FindTransform(model.transform, "Tail-light");
        if (headlights != null && tailLights != null &&
            TryGetRendererBounds(headlights, out var headBounds) &&
            TryGetRendererBounds(tailLights, out var tailBounds) &&
            headBounds.center.z < tailBounds.center.z)
        {
            model.transform.Rotate(0f, 180f, 0f, Space.Self);
        }

        if (!TryGetRendererBounds(model.transform, out bounds) || bounds.size.z <= 0.001f)
            throw new InvalidOperationException("Bugatti model length could not be measured.");

        var scale = new Vector3(
            2.038f / bounds.size.x,
            TargetLength / bounds.size.z,
            1.212f / bounds.size.y);
        model.transform.localScale = scale;
        if (!TryGetRendererBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled Bugatti bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        if (!TryGetRendererBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Bugatti bounds could not be measured.");

        Debug.Log(
            $"BugattiChiron: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var mapping = new Dictionary<string, string>
        {
            { "Wheel-FL", "FrontLeft_WheelController" },
            { "Wheel-FR", "FrontRight_WheelController" },
            { "Wheel-BL", "RearLeft_WheelController" },
            { "Wheel-BR", "RearRight_WheelController" },
        };

        foreach (var pair in mapping)
        {
            var wheel = FindTransform(model.transform, pair.Key) ??
                        throw new InvalidOperationException($"Model wheel '{pair.Key}' is missing.");
            var controller = FindTransform(root.transform, pair.Value) ??
                             throw new InvalidOperationException($"Wheel controller '{pair.Value}' is missing.");
            var isFront = pair.Value.StartsWith("Front", StringComparison.Ordinal);
            var radius = isFront ? 0.34f : 0.355f;
            var width = isFront ? 0.285f : 0.355f;
            var mount = new GameObject(
                "BugattiWheel" +
                pair.Value.Replace("_WheelController", "").Replace("_", string.Empty));
            mount.transform.SetParent(root.transform, false);
            mount.transform.localPosition =
                new Vector3(
                    controller.localPosition.x,
                    radius,
                    controller.localPosition.z);

            wheel.SetParent(mount.transform, false);
            wheel.name = "Geometry";
            wheel.localPosition = Vector3.zero;
            // The source rim face is on the opposite side of its local Y axle.
            // +90 maps the authored outside faces to the outside on both sides.
            wheel.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheel.localScale = Vector3.one;
            if (!TryGetRendererBounds(mount.transform, out var wheelBounds) ||
                wheelBounds.size.x <= 0.001f ||
                wheelBounds.size.y <= 0.001f ||
                wheelBounds.size.z <= 0.001f)
            {
                throw new InvalidOperationException($"Model wheel '{pair.Key}' has invalid bounds.");
            }

            mount.transform.localScale = new Vector3(
                width / wheelBounds.size.x,
                (radius * 2f) / wheelBounds.size.y,
                (radius * 2f) / wheelBounds.size.z);
            if (TryGetRendererBounds(mount.transform, out wheelBounds))
                wheel.position += mount.transform.position - wheelBounds.center;

            AssignWheelVisual(controller, mount);
        }
    }

    private static void AssignWheelVisual(Transform controller, GameObject visualObject)
    {
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

        throw new InvalidOperationException($"Wheel controller '{controller.name}' has no visual property.");
    }

    private static void ConfigureRendererReferences(GameObject root)
    {
        var renderers = new List<Renderer>();
        var paintRenderers = new List<Renderer>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || renderer.sharedMaterials.Length == 0)
                continue;
            renderers.Add(renderer);
            foreach (var material in renderer.sharedMaterials)
                if (material != null && IsBodyPaintMaterial(material))
                {
                    paintRenderers.Add(renderer);
                    break;
                }
        }

        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            AssignRendererArray(serialized.FindProperty("bodyMeshes"), paintRenderers);
            AssignRendererArray(serialized.FindProperty("renderers"), renderers);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static bool IsBodyPaintMaterial(Material material) =>
        material.name.IndexOf("BugattiOpaque_04_Body", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRimPaintMaterial(Material material) =>
        material.name.IndexOf("BugattiOpaque_01_Rims", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("BugattiOpaque_02_Caliper", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRimInnerPaintMaterial(Material material) =>
        material.name.IndexOf("BugattiOpaque_03_Brake_rotor", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsSeatPaintMaterial(Material material) =>
        material.name.IndexOf("BugattiOpaque_19_seats", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsBaseTextureReadable(Material material)
    {
        Texture? texture = null;
        if (material.HasProperty("_BaseColorMap"))
            texture = material.GetTexture("_BaseColorMap");
        if (texture == null && material.HasProperty("_MainTex"))
            texture = material.GetTexture("_MainTex");
        return texture is Texture2D texture2D && texture2D.isReadable;
    }

    private static bool IsDarkBodyPaintMaterial(Material material) =>
        material.name.IndexOf("BugattiOpaque_06_Darker_Parts", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorPrimaryPaintMaterial(Material material) =>
        material.name.IndexOf("BugattiOpaque_13_Interior_1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorSecondaryPaintMaterial(Material material) =>
        material.name.IndexOf("BugattiOpaque_08_Interior_2", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorDarkPaintMaterial(Material material) =>
        material.name.IndexOf("BugattiOpaque_12_Interior_1_Darker", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void AssignPersistentMaterials(GameObject model)
    {
        EnsureAssetFolder(MaterialFolder);
        var replacements = new Dictionary<Material, Material>();
        var opaqueMaterialIndex = 0;
        var transparentMaterialIndex = 0;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                    continue;

                if (!replacements.TryGetValue(source, out var persistent))
                {
                    var transparent = BugattiChironMaterials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"Bugatti{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
                    var path = $"{MaterialFolder}/{assetName}.mat";
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
                }

                materials[index] = persistent;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private static void MarkMaterialsDirty(GameObject model)
    {
        var materials = new HashSet<Material>();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && materials.Add(material))
                {
                    EditorUtility.SetDirty(material);
                }
            }
        }
    }

    private static void CreateManifest()
    {
        var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        if (manifest == null)
        {
            manifest = ScriptableObject.CreateInstance<BAModManifest>();
            AssetDatabase.CreateAsset(manifest, ManifestPath);
        }

        manifest.ModId = "BugattiChiron";
        manifest.DisplayName = "Bugatti Chiron";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "bugattichiron.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Bugatti manifest references could not be assigned.");
        EditorUtility.SetDirty(manifest);
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

    private static bool TryGetBugattiRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!BugattiChironMaterials.IsBugattiRenderer(renderer.transform))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        }

        return null;
    }

    private static void SetLocalPosition(GameObject root, string name, Vector3 value)
    {
        var transform = FindTransform(root.transform, name) ??
                        throw new InvalidOperationException($"Reference transform '{name}' is missing.");
        transform.localPosition = value;
    }

    private static void AssignRendererArray(
        SerializedProperty? property,
        List<Renderer> renderers)
    {
        if (property == null || !property.isArray ||
            property.propertyType == SerializedPropertyType.String)
        {
            return;
        }

        property.arraySize = renderers.Count;
        for (var index = 0; index < renderers.Count; index++)
        {
            var element = property.GetArrayElementAtIndex(index);
            if (element.propertyType == SerializedPropertyType.ObjectReference)
                element.objectReferenceValue = renderers[index];
        }
    }

    private static SerializedProperty? FindRelativeProperty(
        SerializedObject serialized,
        string path)
    {
        var parts = path.Split('.');
        var property = serialized.FindProperty(parts[0]);
        for (var index = 1; index < parts.Length && property != null; index++)
            property = property.FindPropertyRelative(parts[index]);
        return property;
    }

    private static void SetRelativeNumber(
        SerializedObject serialized,
        string path,
        float value)
    {
        var property = FindRelativeProperty(serialized, path);
        if (property == null)
            return;
        if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else if (property.propertyType == SerializedPropertyType.Float)
            property.floatValue = value;
        else if (property.propertyType == SerializedPropertyType.Enum)
            property.enumValueIndex = Mathf.RoundToInt(value);
    }

    private static void SetRelativeBool(
        SerializedObject serialized,
        string path,
        bool value)
    {
        var property = FindRelativeProperty(serialized, path);
        if (property?.propertyType == SerializedPropertyType.Boolean)
            property.boolValue = value;
    }

    private static void SetString(SerializedObject serialized, string name, string value)
    {
        var property = serialized.FindProperty(name);
        if (property?.propertyType == SerializedPropertyType.String)
            property.stringValue = value;
    }

    private static void SetBool(SerializedObject serialized, string name, bool value)
    {
        var property = serialized.FindProperty(name);
        if (property?.propertyType == SerializedPropertyType.Boolean)
            property.boolValue = value;
    }

    private static void SetNumber(SerializedObject serialized, string name, float value)
    {
        var property = serialized.FindProperty(name);
        if (property == null)
            return;
        if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else if (property.propertyType == SerializedPropertyType.Float)
            property.floatValue = value;
    }

    private static float ReadNumber(SerializedProperty? property)
    {
        if (property == null)
            return 0f;
        if (property.propertyType == SerializedPropertyType.Integer)
            return property.intValue;
        return property.propertyType == SerializedPropertyType.Float
            ? property.floatValue
            : 0f;
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
        {
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '-' && chars[index] != '_')
                chars[index] = '_';
        }

        return new string(chars);
    }
}
