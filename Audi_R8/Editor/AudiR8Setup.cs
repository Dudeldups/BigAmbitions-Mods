#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class AudiR8Setup
{
    private const string ModRoot = "Assets/Mods/Audi_R8";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/2021_audi_r8.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/AudiR8DamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/AudiR8.asset";
    private const string VehiclePrefabPath = ModRoot + "/AudiR8.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/AudiR8.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/audir8.unity3d";
    private const string MacBundlePath =
        ModRoot + "/AssetBundles/Mac/audir8.unity3d";
    private const string VehicleTypeName =
        "audir8-vehicle:vehicletype_audir8";
    // Audi R8 V10 performance quattro / Audi R8 facts.
    // Body length, wheelbase and wheel/tire dimensions were cross-checked against
    // Audi's technical data; the source GLB is normalized uniformly to preserve shape.
    private const float TargetLength = 4.429f;
    private const float TargetWidth = 2.020f;
    private const float TargetHeight = 1.220f;
    private const float FrontTrack = 1.628f; // measured from the supplied wheel groups
    private const float RearTrack = 1.603f;  // measured from the supplied wheel groups
    private const float Wheelbase = 2.651f;
    private const float FrontTireRadius = 0.3275f; // 245/30 ZR20
    private const float RearTireRadius = 0.3455f;  // 305/30 ZR20
    private const float FrontTireWidth = 0.245f;
    private const float RearTireWidth = 0.305f;
    private const float WheelInset = 0f;
    private const float WheelCenterRideHeightOffset = 0f;
    private const float VehicleLinearDrag = 0f; // Quadratic road drag is owned by AudiR8RoadDynamics.
    private const float VehicleBrakeForce = 1960f;
    private const float BrakeMaxTorque = 2240f;
    // Calibrated from the latest in-game 0-50 and 50-100 km/h intervals. The
    // R8's published power and ratios remain intact; the game tire model was
    // allowing substantially more launch acceleration than the real quattro.
    private const float FrontForwardGrip = 0.40f;
    private const float RearForwardGrip = 0.415f;
    private const float FrontForwardStiffness = 1.20f;
    private const float RearForwardStiffness = 1.28f;
    private const float TireFrictionCircleStrength = 1.02f;
    private const float AntiRollBarForce = 11000f;
    private const float FrontSuspensionTravel = 0.075f;
    private const float RearSuspensionTravel = 0.080f;
    private const float DeformationStrength = 0.12f;
    private const float DeformationRadius = 0.30f;
    private const float DeformationRandomness = 0.005f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.12f, -0.12f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f, FrontTireRadius, Wheelbase * 0.5f) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f, FrontTireRadius, Wheelbase * 0.5f) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f, RearTireRadius, -Wheelbase * 0.5f) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f, RearTireRadius, -Wheelbase * 0.5f) },
        };

    // Audi publishes combined transmission ratios for this R8. NWH applies one
    // final-drive multiplier, so the listed ratios are decomposed against 3.75.
    private static readonly float[] Gears =
    {
        -3.147f,
        0f,
        3.725f,
        2.477f,
        1.799f,
        1.355f,
        1.067f,
        0.846f,
        0.625f,
    };

    private static AnimationCurve CreatePowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.08f, 0.04f),
            new Keyframe(0.15f, 0.14f),
            new Keyframe(0.25f, 0.23f),
            new Keyframe(0.35f, 0.34f),
            new Keyframe(0.45f, 0.48f),
            new Keyframe(0.55f, 0.60f),
            new Keyframe(0.65f, 0.74f),
            new Keyframe(0.747f, 0.87f),
            new Keyframe(0.86f, 0.985f),
            new Keyframe(0.90f, 1f),
            new Keyframe(0.95f, 0.99f),
            new Keyframe(1f, 0.90f));

    [MenuItem("Big Ambitions Mods/Setup Audi R8")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "Audi R8 setup complete: fitted the supplied Audi R8 model, " +
            "configured quattro/V10 performance, four independent wheel visuals, " +
            "R8 lamp geometry, repaintable body panels and fixed black trim.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    public static void RepairPrefabAndBuild()
    {
        GenerateAndBuild();
    }

    // Retune existing assets without regenerating the vehicle from a donor.
    public static void ApplyTelemetryCalibrationAndBuild()
    {
        var type = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        var typeData = new SerializedObject(type);
        SetNumber(typeData, "brakeForce", VehicleBrakeForce);
        typeData.ApplyModifiedPropertiesWithoutUndo();
        var prefab = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);
        try
        {
            prefab.GetComponent<Rigidbody>().drag = VehicleLinearDrag;
            AudiR8WashSetup.Author(prefab);
            AudiR8CrashSoundSetup.Author(prefab);
            AudiR8StripeChecks.Author(prefab);
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue;
                var data = new SerializedObject(component);
                if (component.GetType().FullName == "NWH.VehiclePhysics2.VehicleController")
                    SetRelativeNumber(data, "brakes.maxTorque", BrakeMaxTorque);
                if (component.GetType().FullName == "NWH.WheelController3D.WheelController")
                    SetRelativeNumber(data, "forwardFriction.grip",
                        prefab.transform.InverseTransformPoint(component.transform.position).z > 0f
                            ? FrontForwardGrip : RearForwardGrip);
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            AudiR8LightSetup.Author(prefab);
            AudiR8HeadlightSetup.Author(prefab);
            VerifyDamageCoverage(prefab);
            VerifyImpactAndAudioMath();
            PrefabUtility.SaveAsPrefabAsset(prefab, VehiclePrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        AssetDatabase.SaveAssets();
        AudiR8WashSetup.VerifyNativeWashLifecycle();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    private static void VerifyDamageCoverage(GameObject prefab)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var allowed = typeof(AudiR8Runtime).GetMethod("IsDeformableExterior", flags);
        var restricted = typeof(AudiR8VisualDamageController).GetMethod("RestrictToExteriorEnds", flags);
        var endVertex = typeof(AudiR8VisualDamageController).GetMethod("IsExteriorEnd", flags);
        var rearInterior = 0; var rearGlass = 0; var owned = 0;
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !(bool)allowed.Invoke(null, new object[] { filter })) continue;
            var mask = (bool)restricted.Invoke(null, new object[] { filter });
            var indices = new HashSet<int>(filter.sharedMesh.triangles);
            var vertices = filter.sharedMesh.vertices;
            var rear = 0; var eligible = 0;
            foreach (var index in indices)
            {
                var point = prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(vertices[index]));
                if (mask && !(bool)endVertex.Invoke(null, new object[] { point })) continue;
                eligible++;
                if (point.z < -1.7f) rear++;
            }
            if (filter.name.IndexOf("Kit1_Interior", StringComparison.OrdinalIgnoreCase) >= 0) rearInterior += rear;
            if (filter.name.IndexOf("Kit1_Window", StringComparison.OrdinalIgnoreCase) >= 0) rearGlass += rear;
            owned += eligible;
        }
        if (rearInterior == 0 || rearGlass == 0)
            throw new InvalidOperationException($"R8 rear damage coverage missing: exterior interior-atlas vertices={rearInterior}, rear lenses={rearGlass}.");
        Debug.Log($"AudiR8 damage coverage verified: ownedVertices={owned}, rearInteriorAtlas={rearInterior}, rearLampGlass={rearGlass}; cabin protected by end-face masks.");
    }

    private static void VerifyImpactAndAudioMath()
    {
        var assembly = typeof(AudiR8Runtime).Assembly;
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var impact = assembly.GetType("AudiR8ImpactDamageController", true);
        var normal = impact.GetMethod("NormalClosingSpeed", flags);
        var score = impact.GetMethod("ConditionDamage", flags);
        var tangent = (float)normal.Invoke(null, new object[] { new Vector3(80f, 0f, 0f), Vector3.up });
        var frontal = (float)normal.Invoke(null, new object[] { new Vector3(0f, 0f, 20f), Vector3.forward });
        var separating = (float)normal.Invoke(null, new object[] { new Vector3(0f, 0f, -20f), Vector3.forward });
        var lowScore = (float)score.Invoke(null, new object[] { 2f, 1595f, 0f, .02f, 1f, 5f });
        var realScore = (float)score.Invoke(null, new object[] { frontal, 1595f, 1595f, .02f, 1f, 5f });
        var targetHz = assembly.GetType("AudiR8AudioModel", true).GetMethod("TargetHz", flags);
        var idle = (float)targetHz.Invoke(null, new object[] { 0f });
        var high = (float)targetHz.Invoke(null, new object[] { 1f });
        var region = assembly.GetType("AudiR8VisualDamageController", true).GetMethod("RegionScale", flags);
        var frontScale = (float)region.Invoke(null, new object[] { true, true });
        var rearScale = (float)region.Invoke(null, new object[] { true, false });
        var fixedStepScore = (float)score.Invoke(null, new object[] { frontal, 1595f, 1595f, .01f, 1f, 5f });
        var reverse = assembly.GetType("AudiR8RoadDynamics", true).GetMethod("ReversePropulsionScale", flags);
        var wall40 = (float)score.Invoke(null, new object[] { 40f / 3.6f, 1595f, 0f, .02f, 1f, 5f });
        var wall200 = (float)score.Invoke(null, new object[] { 200f / 3.6f, 1595f, 0f, .02f, 1f, 5f });
        if (Math.Abs(frontScale - .80f) > .0001f || Math.Abs(rearScale - .90f) > .0001f ||
            Math.Abs(fixedStepScore - realScore) > .0001f || Math.Abs(realScore - .12375f) > .0001f ||
            (float)reverse.Invoke(null, new object[] { 0f }) != 1f ||
            (float)reverse.Invoke(null, new object[] { 37f / 3.6f }) != 1f ||
            (float)reverse.Invoke(null, new object[] { 40f / 3.6f }) != 0f ||
            wall200 <= .20f || Math.Abs(wall200 / wall40 - 5f) > .001f)
            throw new InvalidOperationException("Audi R8 regional damage/reverse limiter verification failed.");
        if (tangent != 0f || separating != 0f || lowScore != 0f || realScore <= 0f || idle != 50f || Math.Abs(high - 220f) > .01f)
            throw new InvalidOperationException("Audi R8 impact/audio calibration math verification failed.");
        Debug.Log($"AudiR8 impact/audio math verified: tangentDamage=0, separatingDamage=0, subThresholdDamage=0, wall40/200 damage={wall40:0.000}/{wall200:0.000} (no impact cap); front/rear deformation=.80/.90, condition damage halved and timestep independent, reverse torque cut at 40km/h; existing sportscar audio idle/high=50/220 Hz.");
    }

    public static void VerifyBuiltBundle()
    {
        VerifyPlatformBundles();
        foreach (var assetPath in new[] { VehicleAssetPath, VehiclePrefabPath, ManifestPath })
        {
            foreach (var dependency in AssetDatabase.GetDependencies(assetPath, true))
            {
                if (dependency.StartsWith(
                        "Assets/Mods/AudiRS6R/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Audi R8 asset '{assetPath}' retains donor dependency '{dependency}'.");
                }
            }
        }

        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");

        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException("Bundle is missing the R8 VehicleType or prefab.");
            AudiR8LightSetup.VerifyBundledPrefab(prefab, bundle);
            AudiR8WashSetup.VerifyBindings(prefab);
            AudiR8CrashSoundSetup.VerifyBindings(prefab, bundle);
            AudiR8StripeChecks.VerifyBundledPrefab(prefab);

            var serialized = new SerializedObject(vehicleType);
            var speed = ReadNumber(serialized.FindProperty("maxSpeed"));
            var power = ReadNumber(serialized.FindProperty("enginePower"));
            if (Math.Abs(speed - 331f) > 0.1f || Math.Abs(power - 456f) > 0.1f)
                throw new InvalidOperationException(
                    $"R8 performance data is incorrect: speed={speed}, power={power}.");

            var visual = FindTransform(prefab.transform, "AudiR8Visual") ??
                         throw new InvalidOperationException("Audi R8 visual root is missing.");
            if (!TryGetRendererBounds(visual, out var bounds) ||
                Math.Abs(bounds.size.z - TargetLength) > 0.035f ||
                bounds.size.y < 1.10f)
            {
                throw new InvalidOperationException(
                    $"Fitted R8 bounds are invalid: {bounds.size}.");
            }

            var wheelCount = 0;
            var caliperCount = 0;
            var legacyDonorComponentCount = 0;
            var forbiddenNodeCount = 0;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.StartsWith("AudiR8Wheel", StringComparison.Ordinal))
                    wheelCount++;
                if (transform.name.StartsWith("AudiR8FixedCaliper", StringComparison.Ordinal))
                    caliperCount++;
                if (ContainsDonorIdentity(transform.name))
                    forbiddenNodeCount++;
            }
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component != null &&
                    component.GetType().Name.StartsWith("AudiRS6R", StringComparison.Ordinal))
                    legacyDonorComponentCount++;
            }

            if (wheelCount != 4 || caliperCount != 4 ||
                legacyDonorComponentCount != 0 || forbiddenNodeCount != 0)
            {
                throw new InvalidOperationException(
                    $"R8 prefab check failed: wheels={wheelCount}/4, calipers={caliperCount}/4, " +
                    $"donorComponents={legacyDonorComponentCount}, forbiddenNodes={forbiddenNodeCount}.");
            }

            foreach (var corner in new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" })
            {
                var controller = FindTransform(prefab.transform, corner + "_WheelController");
                if (controller == null ||
                    FindTransform(prefab.transform, "AudiR8Wheel" + corner) == null ||
                    FindTransform(prefab.transform, "AudiR8FixedCaliper" + corner) == null)
                    throw new InvalidOperationException($"R8 {corner} wheel or caliper binding is missing.");
                var shouldBeFront = corner.StartsWith("Front", StringComparison.Ordinal);
                var shouldBeLeft = corner.EndsWith("Left", StringComparison.Ordinal);
                if (controller.localPosition.y < 0.22f || controller.localPosition.y > 0.48f ||
                    (controller.localPosition.z > 0f) != shouldBeFront ||
                    (controller.localPosition.x < 0f) != shouldBeLeft)
                {
                    throw new InvalidOperationException(
                        $"R8 {corner} wheel is misoriented or off the ground: {controller.localPosition}.");
                }
            }

            var bodyColliderHolder = FindTransform(prefab.transform, "BodyCollider") ??
                                     throw new InvalidOperationException("Audi R8 body collider holder is missing.");
            var bodyBoxes = bodyColliderHolder.GetComponents<BoxCollider>();
            if (bodyBoxes.Length != 1 || bodyBoxes.Any(collider => !collider.enabled) ||
                Math.Abs(bodyBoxes[0].size.x - 1.80f) > 0.01f ||
                Math.Abs(bodyBoxes[0].size.y - 1.10f) > 0.01f ||
                Math.Abs(bodyBoxes[0].size.z - 4.36f) > 0.01f)
            {
                throw new InvalidOperationException(
                    $"R8 body collision shape is not the stable single-box shell: " +
                    $"boxes={bodyBoxes.Length}, enabled={bodyBoxes.Count(collider => collider.enabled)}.");
            }

            Debug.Log(
                $"AudiR8 bundle verified: speed={speed:0} km/h, power={power:0} kW, " +
                $"bodyBounds={bounds.size}, bodyBoxes={bodyBoxes.Length}, " +
                $"rollingWheels={wheelCount}, fixedCalipers={caliperCount}, " +
                "external donor dependencies=0.");
        }
        finally
        {
            bundle.Unload(false);
        }
    }

    private static void VerifyPlatformBundles()
    {
        var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        var requiredPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;
        if (manifest == null || (manifest.TargetPlatforms & requiredPlatforms) != requiredPlatforms)
            throw new InvalidOperationException("Audi R8 manifest must build Windows and Mac bundles.");

        foreach (var platform in new[] { "Windows", "Mac" })
        {
            var path = platform == "Windows" ? WindowsBundlePath : MacBundlePath;
            var manifestPath = path + ".manifest";
            if (!System.IO.File.Exists(path) || !System.IO.File.Exists(manifestPath))
                throw new InvalidOperationException("Audi R8 platform bundle is missing: " + platform);
            var text = System.IO.File.ReadAllText(manifestPath);
            if (!BuildPipeline.GetCRCForAssetBundle(path, out var crc) || !text.Contains("CRC: " + crc))
                throw new InvalidOperationException("Audi R8 " + platform + " bundle CRC differs from its manifest.");
            Debug.Log("Audi R8 " + platform + " bundle manifest verified; CRC=" + crc + ".");
        }
    }
    private static UnityEngine.Object CreateVehicleType()
    {
        var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ReferenceAssetPath);
        if (source == null)
            throw new InvalidOperationException("Editor-time VehicleType donor asset was not found.");

        var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (target == null)
        {
            if (!AssetDatabase.CopyAsset(ReferenceAssetPath, VehicleAssetPath))
                throw new InvalidOperationException("Could not create the AudiR8 VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated AudiR8 VehicleType asset did not load.");

        target.name = "AudiR8";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 233950f);
        SetNumber(serialized, "maxFuel", 73f);
        SetNumber(serialized, "maxCargoCapacity", 2f);
        SetNumber(serialized, "maxSpeed", 331f);
        SetNumber(serialized, "enginePower", 456f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", 24f);
        SetNumber(serialized, "damageIntensity", 0.38f);
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

    private static void ConfigureVehicleTypePerformance()
    {
        var vehicleType = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (vehicleType == null)
            throw new InvalidOperationException("AudiR8 VehicleType asset was not found.");

        var serialized = new SerializedObject(vehicleType);
        SetNumber(serialized, "maxSpeed", 331f);
        SetNumber(serialized, "enginePower", 456f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(vehicleType);
    }

    private static void CreateVehiclePrefab(UnityEngine.Object vehicleType)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("AudiRS6R donor prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("The supplied Audi R8 GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "AudiR8";
        try
        {
            if (PrefabUtility.IsPartOfPrefabInstance(root))
            {
                PrefabUtility.UnpackPrefabInstance(
                    root,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
            }
            StripAudiGeometry(root);
            RemoveDonorModelHierarchy(root);
            RemoveAudiSpecificBehaviours(root);
            ConfigureRootPhysics(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);
            AudiR8CrashSoundSetup.Author(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the Audi R8 GLB.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "AudiR8Visual";
            NormalizeImportedHierarchy(modelInstance);
            RemoveModelLights(modelInstance);
            AssignPersistentMaterials(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureWheelControllers(root);
            ConfigureExitMarkers(root, modelInstance);
            AttachWheelVisuals(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            var missingScriptsRemoved = RemoveMissingScripts(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);
            AudiR8StripeChecks.Author(root);
            AudiR8LightSetup.Author(root);
            AudiR8HeadlightSetup.Author(root);

            var materialSlotCount = ValidatePersistentRenderMaterials(root);
            Debug.Log(
                $"Audi R8 prefab prepared: persistent HDRP material slots={materialSlotCount}, " +
                $"missing scripts removed={missingScriptsRemoved}.");
            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Audi R8 vehicle prefab.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void RemoveDonorModelHierarchy(GameObject root)
    {
        var candidates = new List<Transform>();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == root.transform ||
                !(string.Equals(transform.name, "RootNode", StringComparison.Ordinal) ||
                  string.Equals(transform.name, "FINAL_MODEL_2.fbx", StringComparison.Ordinal)))
                continue;

            var nestedUnderCandidate = false;
            for (var parent = transform.parent; parent != null && parent != root.transform; parent = parent.parent)
            {
                if (string.Equals(parent.name, "RootNode", StringComparison.Ordinal) ||
                    string.Equals(parent.name, "FINAL_MODEL_2.fbx", StringComparison.Ordinal))
                {
                    nestedUnderCandidate = true;
                    break;
                }
            }
            if (!nestedUnderCandidate)
                candidates.Add(transform);
        }

        foreach (var transform in candidates)
            UnityEngine.Object.DestroyImmediate(transform.gameObject);

        var obstacleIndex = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!ContainsDonorIdentity(transform.name))
                continue;

            transform.name = obstacleIndex++ == 0
                ? "AudiR8NavigationObstacle"
                : $"AudiR8NavigationObstacle_{obstacleIndex:D2}";
        }
    }

    private static bool ContainsDonorIdentity(string name) =>
        name.IndexOf("AudiRS6R", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("RS6", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.IndexOf("Sportline", StringComparison.OrdinalIgnoreCase) >= 0;

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
        foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
            UnityEngine.Object.DestroyImmediate(collider);
        foreach (var audioSource in root.GetComponentsInChildren<AudioSource>(true))
            audioSource.clip = null;
        ClearDonorAudioClipReferences(root);
        foreach (var light in root.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(light.gameObject);
    }

    private static void ClearDonorAudioClipReferences(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;

            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            var enterChildren = true;
            var changed = false;
            while (property.Next(enterChildren))
            {
                enterChildren = true;
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    !(property.objectReferenceValue is AudioClip))
                {
                    continue;
                }

                property.objectReferenceValue = null;
                changed = true;
            }

            if (changed)
                serialized.ApplyModifiedPropertiesWithoutUndo();
        }
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
        body.mass = 1595f;
        body.drag = VehicleLinearDrag;
        body.angularDrag = 1.45f;
        body.centerOfMass = StableCenterOfMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.solverIterations = Mathf.Max(body.solverIterations, 12);
        body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, 4);
        body.maxDepenetrationVelocity = 2f;

        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var configuredCenter = serialized.FindProperty("centerOfMass");
            var useDefaultCenter = serialized.FindProperty("useDefaultCenterOfMass");
            if (configuredCenter?.propertyType != SerializedPropertyType.Vector3 ||
                useDefaultCenter?.propertyType != SerializedPropertyType.Boolean)
            {
                continue;
            }

            useDefaultCenter.boolValue = false;
            configuredCenter.vector3Value = StableCenterOfMass;
            var combinedCenter = serialized.FindProperty("combinedCenterOfMass");
            if (combinedCenter?.propertyType == SerializedPropertyType.Vector3)
                combinedCenter.vector3Value = StableCenterOfMass;
            SetNumber(serialized, "baseMass", 1595f);
            SetNumber(serialized, "combinedMass", 1595f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
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
                SetRelativeNumber(
                    serialized,
                    "spring.maxLength",
                    isFront ? FrontSuspensionTravel : RearSuspensionTravel);
                SetRelativeNumber(serialized, "spring.maxForce", 19000f);
                SetRelativeNumber(serialized, "wheel.radius", isFront ? FrontTireRadius : RearTireRadius);
                SetRelativeNumber(serialized, "wheel.width", isFront ? FrontTireWidth : RearTireWidth);
                SetRelativeNumber(
                    serialized,
                    "forwardFriction.grip",
                    isFront ? FrontForwardGrip : RearForwardGrip);
                SetRelativeNumber(
                    serialized,
                    "forwardFriction.stiffness",
                    isFront ? FrontForwardStiffness : RearForwardStiffness);
                SetRelativeNumber(serialized, "frictionCircleStrength", TireFrictionCircleStrength);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static void ConfigureBodyColliders(GameObject root)
    {
        var holder = FindTransform(root.transform, "BodyCollider") ??
                     throw new InvalidOperationException("Reference BodyCollider is missing.");
        var colliders = holder.GetComponents<BoxCollider>();
        if (colliders.Length < 1)
            throw new InvalidOperationException("Reference vehicle requires a body collider.");

        // A single fitted shell avoids the simultaneous impulses caused by the
        // donor's overlapping bumper, lower-body, and cabin boxes. Keep the
        // shell above the road plane while covering the R8's full body height.
        colliders[0].center = new Vector3(0f, 0.67f, -0.05f);
        colliders[0].size = new Vector3(1.80f, 1.10f, 4.36f);
        colliders[0].isTrigger = false;
        colliders[0].enabled = true;
        for (var index = 2; index < colliders.Length; index++)
            UnityEngine.Object.DestroyImmediate(colliders[index]);
        if (colliders.Length > 1)
            UnityEngine.Object.DestroyImmediate(colliders[1]);
    }

    private static void RepairStaticWheelVisuals(GameObject root)
    {
        foreach (var pair in WheelControllerPositions)
        {
            var corner = pair.Key.Replace("_WheelController", string.Empty);
            var controller = FindTransform(root.transform, pair.Key) ??
                             throw new InvalidOperationException($"Wheel controller '{pair.Key}' is missing.");
            var mount = FindTransform(root.transform, "AudiR8Wheel" + corner) ??
                        throw new InvalidOperationException($"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "AudiR8FixedCaliper" + corner) ??
                          throw new InvalidOperationException($"Fixed caliper for '{corner}' is missing.");

            controller.localPosition = pair.Value;
            mount.SetParent(root.transform, true);
            mount.localPosition = pair.Value;
            caliper.localPosition = pair.Value;
            AssignWheelVisual(controller, mount.gameObject);
        }
    }

    private static void RepairTrueTireWheelVisuals(GameObject root)
    {
        var tires = new List<Transform>();
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!HasAncestorNameFragment(renderer.transform, "Scene_-_Root.002") ||
                HasAncestorNameFragment(renderer.transform, "AudiR8Wheel"))
            {
                continue;
            }
            tires.Add(renderer.transform);
        }
        if (tires.Count == 0)
        {
            var attachedTireCount = 0;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (HasAncestorNameFragment(renderer.transform, "AudiR8Wheel") &&
                    HasAncestorNameFragment(renderer.transform, "Geometry_AudiR8Tire_"))
                {
                    attachedTireCount++;
                }
            }

            if (attachedTireCount != 4)
                throw new InvalidOperationException(
                    $"Expected four attached AudiR8 tires, found {attachedTireCount}.");
            return;
        }
        if (tires.Count != 4)
            throw new InvalidOperationException(
                $"Expected four body-static AudiR8 tires, found {tires.Count}.");

        foreach (var pair in WheelControllerPositions)
        {
            var corner = pair.Key.Replace("_WheelController", string.Empty);
            var controller = FindTransform(root.transform, pair.Key) ??
                             throw new InvalidOperationException(
                                 $"Wheel controller '{pair.Key}' is missing.");
            var mount = FindTransform(root.transform, "AudiR8Wheel" + corner) ??
                        throw new InvalidOperationException(
                            $"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "AudiR8FixedCaliper" + corner) ??
                          throw new InvalidOperationException(
                              $"Fixed caliper for '{corner}' is missing.");
            var tire = FindClosestPart(mount, tires) ??
                       throw new InvalidOperationException(
                           $"Rolling visual for '{corner}' has no matching true tire.");
            tires.Remove(tire);

            var oldScale = mount.localScale;
            mount.localScale = Vector3.one;
            if (!TryGetRendererBounds(tire, out var tireBounds) ||
                tireBounds.size.x <= 0.001f || tireBounds.size.y <= 0.001f ||
                tireBounds.size.z <= 0.001f)
            {
                throw new InvalidOperationException($"True tire for '{corner}' has invalid bounds.");
            }

            var isFront = pair.Key.StartsWith("Front", StringComparison.Ordinal);
            var isLeft = pair.Key.IndexOf("Left", StringComparison.Ordinal) >= 0;
            var radius = isFront ? FrontTireRadius : RearTireRadius;
            var width = isFront ? FrontTireWidth : RearTireWidth;
            mount.position = tireBounds.center;
            tire.SetParent(mount, true);
            tire.name = "Geometry_AudiR8Tire_" +
                        (isFront ? "F" : "R") + (isLeft ? "L" : "R");
            var fittedScale = new Vector3(
                width / tireBounds.size.x,
                (radius * 2f) / tireBounds.size.y,
                (radius * 2f) / tireBounds.size.z);
            mount.localScale = fittedScale;
            mount.localPosition = pair.Value;
            controller.localPosition = pair.Value;

            // The caliper geometry was detached after the old rim-based fit.
            // Compensate it by the same scale ratio so it remains correctly
            // sized beside the newly fitted tire/rim/rotor assembly.
            caliper.localScale = new Vector3(
                fittedScale.x / oldScale.x,
                fittedScale.y / oldScale.y,
                fittedScale.z / oldScale.z);
            caliper.localPosition = pair.Value;
            AssignWheelVisual(controller, mount.gameObject);
        }
    }

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        // The source GLB combines its cabin by material and has no named
        // steering-wheel pivot. Use a stable left-hand-drive cockpit anchor in
        // the fitted +Z-forward R8 coordinates for the seated-player overlay.
        const float driverSide = -1.45f;
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.1f, 0f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.1f, 0f));

        if (model.GetComponentInChildren<MeshFilter>(true) == null)
            throw new InvalidOperationException("The fitted Audi R8 has no mesh for cockpit anchoring.");

        const string anchorName = "AudiR8SteeringWheelAnchor";
        var anchor = FindTransform(root.transform, anchorName);
        if (anchor == null)
        {
            var anchorObject = new GameObject(anchorName);
            anchorObject.transform.SetParent(root.transform, false);
            anchor = anchorObject.transform;
        }
        else if (anchor.parent != root.transform)
        {
            anchor.SetParent(root.transform, false);
        }

        // Estimated from the left-hand-drive cabin geometry; the GLB does not
        // supply a separate steering wheel transform or interior sub-object.
        anchor.localPosition = new Vector3(-0.29f, 0.76f, 0.56f);
        anchor.localRotation = Quaternion.identity;
        anchor.localScale = Vector3.one;
        Debug.Log(
            $"AudiR8: left-hand-drive driver exit x={driverSide:F2}; " +
            $"cockpit anchor={anchor.localPosition}.");
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
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1250f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 600f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 550f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.20f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 456f);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreatePowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 900f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 8700f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.38f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.autoStartOnThrottle", true);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.75f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 7f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.065f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 5000f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 8250f);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);
                SetRelativeBool(serialized, "powertrain.transmission.isSequential", true);
                SetRelativeBool(serialized, "powertrain.transmission.allowDownshiftGearSkipping", false);
                SetRelativeNumber(serialized, "brakes.maxTorque", BrakeMaxTorque);

                var wheelGroups = FindRelativeProperty(serialized, "powertrain.wheelGroups");
                if (wheelGroups == null || !wheelGroups.isArray || wheelGroups.arraySize != 2)
                    throw new InvalidOperationException("Reference wheel groups are missing.");
                for (var index = 0; index < wheelGroups.arraySize; index++)
                {
                    var antiRoll = wheelGroups.GetArrayElementAtIndex(index)
                        .FindPropertyRelative("antiRollBarForce");
                    if (antiRoll?.propertyType != SerializedPropertyType.Float)
                        throw new InvalidOperationException("Wheel group anti-roll setting is missing.");
                    antiRoll.floatValue = AntiRollBarForce;
                }

                var differentials = FindRelativeProperty(serialized, "powertrain.differentials");
                if (differentials == null || !differentials.isArray)
                    throw new InvalidOperationException("Reference differentials are missing.");
                var rearDriveConfigured = false;
                for (var index = 0; index < differentials.arraySize; index++)
                {
                    var differential = differentials.GetArrayElementAtIndex(index);
                    var name = differential.FindPropertyRelative("name");
                    if (name?.propertyType != SerializedPropertyType.String ||
                        !string.Equals(name.stringValue, "Center Differential", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    var bias = differential.FindPropertyRelative("biasAB");
                    if (bias?.propertyType != SerializedPropertyType.Float)
                        throw new InvalidOperationException("Center differential torque bias is missing.");
                    // Audi quattro's nominal torque split is rear-biased 40:60.
                    bias.floatValue = 0.60f;
                    rearDriveConfigured = true;
                }
                if (!rearDriveConfigured)
                    throw new InvalidOperationException("Center differential could not be configured for quattro AWD.");

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = Gears.Length;
                for (var index = 0; index < Gears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = Gears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 331f);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!found)
            throw new InvalidOperationException("NWH vehicle controller was not found on the reference prefab.");
    }

    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        model.transform.localScale = Vector3.one;

        // Importers can deliver the source GLB in either Unity Y-up or Blender
        // Z-up coordinates. Pick a rotation from the measured chassis length,
        // labeled front/rear wheels, and the expected tire-center height so an
        // equal-length 90-degree candidate cannot leave the vehicle inverted.
        var candidates = new[]
        {
            Quaternion.identity,
            Quaternion.Euler(90f, 0f, 0f),
            Quaternion.Euler(-90f, 0f, 0f),
        };
        var bestRotation = Quaternion.identity;
        var bestScore = float.PositiveInfinity;
        var bounds = default(Bounds);
        foreach (var candidate in candidates)
        {
            model.transform.localRotation = candidate;
            if (!TryGetWheelGroupBounds(model, "w:3DWheel Front L", out var frontBounds) ||
                !TryGetWheelGroupBounds(model, "w:3DWheel Rear L", out var rearBounds))
                throw new InvalidOperationException("R8 front/rear wheel groups could not be identified.");

            var oriented = frontBounds.center.z < rearBounds.center.z
                ? Quaternion.Euler(0f, 180f, 0f) * candidate
                : candidate;
            model.transform.localRotation = oriented;
            if (!TryGetModelBodyBounds(model.transform, out var candidateBounds) ||
                candidateBounds.size.z <= 0.001f ||
                !TryGetRendererBounds(model.transform, out var completeBounds) ||
                !TryGetWheelGroupBounds(model, "w:3DWheel Front L", out frontBounds) ||
                !TryGetWheelGroupBounds(model, "w:3DWheel Front R", out var frontRightBounds) ||
                !TryGetWheelGroupBounds(model, "w:3DWheel Rear L", out rearBounds) ||
                !TryGetWheelGroupBounds(model, "w:3DWheel Rear R", out var rearRightBounds))
                continue;

            var scale = TargetLength / candidateBounds.size.z;
            var meanWheelCenterY = (frontBounds.center.y + frontRightBounds.center.y +
                                    rearBounds.center.y + rearRightBounds.center.y) * 0.25f;
            var fittedWheelHeight = (meanWheelCenterY - completeBounds.min.y) * scale;
            var expectedWheelHeight = (FrontTireRadius + RearTireRadius) * 0.5f;
            var score = Mathf.Abs(fittedWheelHeight - expectedWheelHeight) +
                        0.025f * Mathf.Abs(candidateBounds.size.x * scale - TargetWidth) +
                        0.015f * Mathf.Abs(candidateBounds.size.y * scale - TargetHeight);
            if (score >= bestScore)
                continue;
            bestRotation = oriented;
            bestScore = score;
            bounds = candidateBounds;
        }
        if (bounds.size.z <= 0.001f)
            throw new InvalidOperationException("Audi R8 body length could not be measured.");

        model.transform.localRotation = bestRotation;

        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Oriented Audi R8 body bounds could not be measured.");
        var uniformScale = TargetLength / bounds.size.z;
        model.transform.localScale = Vector3.one * uniformScale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled Audi R8 body bounds could not be measured.");

        if (!TryGetRendererBounds(model.transform, out var fittedBounds))
            throw new InvalidOperationException("Complete Audi R8 bounds could not be measured.");
        model.transform.position += new Vector3(
            -fittedBounds.center.x,
            -fittedBounds.min.y,
            -fittedBounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Audi R8 body bounds could not be measured.");

        UpdateWheelControllerPositions(model);
        var measuredWheelbase = Vector3.Distance(
            WheelControllerPositions["FrontLeft_WheelController"],
            WheelControllerPositions["RearLeft_WheelController"]);
        Debug.Log(
            $"Audi R8: GLB axis fitted from wheel groups; uniformScale={uniformScale:0.0000}, " +
            $"bodyBounds={bounds.size}, wheelbase={measuredWheelbase:0.000}m.");
    }

    private static void UpdateWheelControllerPositions(GameObject model)
    {
        var wheelRoot = model.transform.parent ??
                        throw new InvalidOperationException("Audi R8 visual is not under a vehicle root.");
        WheelControllerPositions.Clear();
        var prefixes = new[]
        {
            "w:3DWheel Front L", "w:3DWheel Front R",
            "w:3DWheel Rear L", "w:3DWheel Rear R",
        };

        foreach (var prefix in prefixes)
        {
            if (!TryGetWheelGroupBounds(model, prefix, out var bounds))
                throw new InvalidOperationException($"R8 wheel group '{prefix}' has no bounds.");
            var center = wheelRoot.InverseTransformPoint(bounds.center);
            var controllerName = GetWheelCornerName(center) + "_WheelController";
            if (WheelControllerPositions.ContainsKey(controllerName))
                throw new InvalidOperationException($"R8 wheel groups overlap the '{controllerName}' binding.");
            WheelControllerPositions[controllerName] = center;
        }
        if (WheelControllerPositions.Count != 4)
            throw new InvalidOperationException("Audi R8 did not resolve to four unique wheel-controller positions.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var wheelPrefixes = new[]
        {
            "w:3DWheel Front L", "w:3DWheel Front R",
            "w:3DWheel Rear L", "w:3DWheel Rear R",
        };
        var caliperPrefixes = new[]
        {
            "w:CalliperBody Front L", "w:CalliperBody Front R",
            "w:CalliperBody Rear L", "w:CalliperBody Rear R",
        };

        for (var index = 0; index < wheelPrefixes.Length; index++)
        {
            var wheelRenderers = CollectRenderersByPrefix(model, wheelPrefixes[index]);
            if (wheelRenderers.Count == 0)
                throw new InvalidOperationException($"R8 wheel geometry '{wheelPrefixes[index]}' is missing.");

            var wheelBounds = GetCombinedBounds(wheelRenderers);
            var center = root.transform.InverseTransformPoint(wheelBounds.center);
            var corner = GetWheelCornerName(center);
            var controllerName = corner + "_WheelController";
            var controller = FindTransform(root.transform, controllerName) ??
                            throw new InvalidOperationException(
                                $"R8 wheel controller '{controllerName}' is missing.");
            WheelControllerPositions[controllerName] = center;
            controller.localPosition = center;

            var front = corner.StartsWith("Front", StringComparison.Ordinal);
            var tireRadius = front ? FrontTireRadius : RearTireRadius;
            var tireWidth = front ? FrontTireWidth : RearTireWidth;
            var wheelMount = new GameObject("AudiR8Wheel" + corner);
            wheelMount.transform.SetParent(root.transform, false);
            wheelMount.transform.SetPositionAndRotation(wheelBounds.center, root.transform.rotation);
            var wheelMaterials = CombineRendererMeshes(
                wheelRenderers,
                wheelMount.transform,
                wheelMount,
                "AudiR8Wheel" + corner);
            if (wheelMaterials.Count == 0)
                throw new InvalidOperationException($"R8 {corner} wheel mesh merge produced no materials.");
            wheelMount.transform.localScale = new Vector3(
                tireWidth / wheelBounds.size.x,
                tireRadius * 2f / wheelBounds.size.y,
                tireRadius * 2f / wheelBounds.size.z);
            wheelMount.transform.localPosition = center;
            AssignWheelVisual(controller, wheelMount);

            var caliperRenderers = CollectRenderersByPrefix(model, caliperPrefixes[index]);
            if (caliperRenderers.Count == 0)
                throw new InvalidOperationException($"R8 {corner} brake caliper geometry is missing.");
            var caliperPivot = new GameObject("AudiR8FixedCaliper" + corner);
            caliperPivot.transform.SetParent(root.transform, false);
            caliperPivot.transform.SetPositionAndRotation(wheelBounds.center, root.transform.rotation);
            var caliperMaterials = CombineRendererMeshes(
                caliperRenderers,
                caliperPivot.transform,
                caliperPivot,
                "AudiR8Caliper" + corner);
            if (caliperMaterials.Count == 0)
                throw new InvalidOperationException($"R8 {corner} caliper mesh merge produced no materials.");
            caliperPivot.transform.localPosition = center;

            Debug.Log(
                $"Audi R8: fitted {corner} from GLB; " +
                $"sourceBounds={wheelBounds.size}, targetTire={tireWidth:0.000}x" +
                $"{tireRadius * 2f:0.000}m, wheelMaterials={wheelMaterials.Count}, " +
                $"caliperMaterials={caliperMaterials.Count}.");
        }
    }

    private static List<MeshRenderer> CollectRenderersByPrefix(GameObject model, string prefix)
    {
        var renderers = new List<MeshRenderer>();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                renderers.Add(renderer);
        }
        return renderers;
    }

    private static Bounds GetCombinedBounds(IList<MeshRenderer> renderers)
    {
        if (renderers.Count == 0)
            throw new InvalidOperationException("Cannot measure an empty R8 mesh group.");
        var bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Count; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static string GetWheelCornerName(Vector3 vehicleLocalCenter)
    {
        var axle = vehicleLocalCenter.z >= 0f ? "Front" : "Rear";
        var side = vehicleLocalCenter.x < 0f ? "Left" : "Right";
        return axle + side;
    }

    private static bool TryGetWheelGroupBounds(GameObject model, string prefix, out Bounds bounds)
    {
        var renderers = CollectRenderersByPrefix(model, prefix);
        if (renderers.Count == 0)
        {
            bounds = default;
            return false;
        }
        bounds = GetCombinedBounds(renderers);
        return true;
    }

    private static List<Material> CombineRendererMeshes(
        IList<MeshRenderer> sourceRenderers,
        Transform pivot,
        GameObject target,
        string meshPrefix)
    {
        var materialOrder = new List<Material>();
        var partsByMaterial = new Dictionary<Material, List<CombineInstance>>();
        foreach (var renderer in sourceRenderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter?.sharedMesh == null)
                continue;
            var materials = renderer.sharedMaterials;
            for (var submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
            {
                if (submesh >= materials.Length || materials[submesh] == null)
                    continue;
                var material = materials[submesh];
                if (!partsByMaterial.TryGetValue(material, out var parts))
                {
                    parts = new List<CombineInstance>();
                    partsByMaterial.Add(material, parts);
                    materialOrder.Add(material);
                }
                parts.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    subMeshIndex = submesh,
                    transform = pivot.worldToLocalMatrix * filter.transform.localToWorldMatrix,
                });
            }
        }

        EnsureAssetFolder(MeshFolder);
        var targetMaterials = new List<Material>();
        for (var index = 0; index < materialOrder.Count; index++)
        {
            var material = materialOrder[index];
            var combined = new Mesh
            {
                name = meshPrefix + "_" + index.ToString("D2"),
                indexFormat = IndexFormat.UInt32,
            };
            combined.CombineMeshes(partsByMaterial[material].ToArray(), true, true, false);
            combined.RecalculateBounds();
            var path = MeshFolder + "/" +
                       SanitizeAssetName(meshPrefix + "_" + index.ToString("D2") + "_" + material.name) +
                       ".asset";
            var persistent = CreateOrUpdateMeshAsset(combined, path);
            var geometry = new GameObject("Geometry_" + meshPrefix + "_" + index.ToString("D2"));
            geometry.transform.SetParent(target.transform, false);
            geometry.AddComponent<MeshFilter>().sharedMesh = persistent;
            var renderer = geometry.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { material };
            targetMaterials.Add(material);
        }

        foreach (var source in sourceRenderers)
        {
            source.enabled = false;
            source.sharedMaterials = Array.Empty<Material>();
            var filter = source.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = null;
        }
        return targetMaterials;
    }

    private static Mesh CreateOrUpdateMeshAsset(Mesh mesh, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        existing.name = mesh.name;
        EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        return existing;
    }

    private static Transform? FindClosestPart(Transform source, List<Transform> parts)
    {
        if (!TryGetRendererBounds(source, out var sourceBounds))
            return null;
        Transform? closest = null;
        var closestDistance = float.PositiveInfinity;
        foreach (var part in parts)
        {
            if (!TryGetRendererBounds(part, out var partBounds))
                continue;
            var distance = Vector3.Distance(sourceBounds.center, partBounds.center);
            if (distance >= closestDistance)
                continue;
            closest = part;
            closestDistance = distance;
        }
        return closest;
    }

    private static Transform? FindClosestBrake(Transform wheel, List<Transform> brakes)
    {
        if (!TryGetRendererBounds(wheel, out var wheelBounds))
            return null;
        Transform? closest = null;
        var closestDistance = float.PositiveInfinity;
        foreach (var brake in brakes)
        {
            if (!TryGetRendererBounds(brake, out var brakeBounds))
                continue;
            var distance = Vector3.Distance(wheelBounds.center, brakeBounds.center);
            if (distance >= closestDistance)
                continue;
            closest = brake;
            closestDistance = distance;
        }
        return closest;
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
                if (material != null &&
                    (IsBodyPaintMaterial(material) || IsCaliperMaterial(material) ||
                     IsInteriorAccentPaintMaterial(material)))
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

    private static MeshFilter CreateDeformableBody(GameObject root, GameObject modelInstance)
    {
        var sources = new List<MeshRenderer>();
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.name.StartsWith("r:Paint_Geo", StringComparison.Ordinal))
                sources.Add(renderer);
        }
        if (sources.Count < 2)
            throw new InvalidOperationException(
                $"R8 body paint/wrap surfaces were not found; expected green paint and fixed black wrap, found {sources.Count}.");

        var instances = new List<CombineInstance>();
        var materials = new List<Material>();
        foreach (var renderer in sources)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter?.sharedMesh == null)
                continue;
            var sourceMaterials = renderer.sharedMaterials;
            for (var submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
            {
                if (submesh >= sourceMaterials.Length || sourceMaterials[submesh] == null)
                    continue;
                instances.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    subMeshIndex = submesh,
                    transform = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix,
                });
                materials.Add(sourceMaterials[submesh]);
            }
        }
        if (instances.Count < 2)
            throw new InvalidOperationException("R8 green paint and black wrap meshes could not be combined.");

        var combined = new Mesh
        {
            name = "AudiR8DamageBody",
            indexFormat = IndexFormat.UInt32,
        };
        combined.CombineMeshes(instances.ToArray(), false, true, false);
        combined.RecalculateBounds();
        EnsureAssetFolder(MeshFolder);
        var persistentMesh = CreateOrUpdateMeshAsset(combined, DamageBodyMeshPath);

        var damageBody = new GameObject("AudiR8DamageBody");
        damageBody.transform.SetParent(root.transform, false);
        var damageFilter = damageBody.AddComponent<MeshFilter>();
        damageFilter.sharedMesh = persistentMesh;
        var damageRenderer = damageBody.AddComponent<MeshRenderer>();
        damageRenderer.sharedMaterials = materials.ToArray();
        damageRenderer.shadowCastingMode = sources[0].shadowCastingMode;
        damageRenderer.receiveShadows = sources[0].receiveShadows;
        damageRenderer.lightProbeUsage = sources[0].lightProbeUsage;
        damageRenderer.reflectionProbeUsage = sources[0].reflectionProbeUsage;

        foreach (var renderer in sources)
        {
            renderer.enabled = false;
            renderer.sharedMaterials = Array.Empty<Material>();
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = null;
        }
        return damageFilter;
    }

    private static void ConfigureVehicleDeformation(GameObject root, MeshFilter bodyFilter)
    {
        var configured = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null ||
                !string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var serialized = new SerializedObject(component);
            var meshFilters = serialized.FindProperty("meshFilters");
            if (meshFilters == null || !meshFilters.isArray)
                throw new InvalidOperationException("Vehicle deformation mesh list is missing.");
            meshFilters.arraySize = 1;
            meshFilters.GetArrayElementAtIndex(0).objectReferenceValue = bodyFilter;
            var originals = serialized.FindProperty("originalMeshes");
            if (originals != null && originals.isArray)
                originals.ClearArray();
            SetNumber(serialized, "deformationStrength", DeformationStrength);
            SetNumber(serialized, "deformationRadius", DeformationRadius);
            SetNumber(serialized, "deformationRandomness", DeformationRandomness);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            configured = true;
        }

        if (!configured)
            throw new InvalidOperationException("Vehicle deformation controller is missing.");
    }

    private static bool IsBodyPaintMaterial(Material material) =>
        material.name.IndexOf("2020Paint_Material1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsCabinGlassRenderer(Transform transform) =>
        HasAncestorNameFragment(transform, "windshield") ||
        HasAncestorNameFragment(transform, "doorglass") ||
        HasAncestorNameFragment(transform, "quarterglass") ||
        HasAncestorNameFragment(transform, "backlight_tint");

    private static bool IsInteriorAccentPaintMaterial(Material material) =>
        material.name.IndexOf("stitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("seat_leather_2", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("B60000", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRimMaterial(Material material) =>
        material.name.IndexOf("wpolySurface442SG1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!AudiR8Materials.IsAudiR8Renderer(renderer.transform))
                continue;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material != null && IsRimMaterial(material) &&
                    material.name.IndexOf("_Right", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    leftMaterial = material;
                    break;
                }
            }
            if (leftMaterial != null)
                break;
        }

        if (leftMaterial == null)
            throw new InvalidOperationException("The shared AudiR8 rim material is missing.");

        ApplyRimFinish(leftMaterial, AudiR8Materials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!AudiR8Materials.IsAudiR8Renderer(renderer.transform))
                continue;
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                if (materials[index] == null || !IsRimMaterial(materials[index]))
                    continue;
                materials[index] = leftMaterial;
                renderer.SetPropertyBlock(null, index);
                configuredSlots++;
                changed = true;
            }
            if (changed)
                renderer.sharedMaterials = materials;
        }

        if (configuredSlots != 4)
            throw new InvalidOperationException(
                $"Expected four AudiR8 rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", AudiR8Materials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", AudiR8Materials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("wpolySurface443SG1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool PowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreatePowerCurve();
        if (curve == null || curve.length != expected.length)
            return false;
        for (var index = 0; index < expected.length; index++)
        {
            if (Math.Abs(curve[index].time - expected[index].time) > 0.002f ||
                Math.Abs(curve[index].value - expected[index].value) > 0.002f)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsRimInnerPaintMaterial(Material material) => false;

    private static bool IsSeatPaintMaterial(Material material) =>
        material.name.IndexOf("seat_leather_2", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("B60000", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsBaseTextureReadable(Material material)
    {
        Texture? texture = null;
        if (material.HasProperty("_BaseColorMap"))
            texture = material.GetTexture("_BaseColorMap");
        if (texture == null && material.HasProperty("_MainTex"))
            texture = material.GetTexture("_MainTex");
        return texture is Texture2D texture2D && texture2D.isReadable;
    }

    private static bool IsDarkBodyPaintMaterial(Material material) => false;

    private static bool IsInteriorPrimaryPaintMaterial(Material material) =>
        material.name.IndexOf("Interior_D", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorSecondaryPaintMaterial(Material material) =>
        material.name.IndexOf("_red", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorDarkPaintMaterial(Material material) => false;

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
                    var transparent = AudiR8Materials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"AudiR8{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

                    if (!AudiR8Materials.PrepareImportedMaterial(persistent))
                        throw new InvalidOperationException(
                            $"Material '{persistent.name}' could not be converted to HDRP/Lit.");
                    EditorUtility.SetDirty(persistent);
                    replacements.Add(source, persistent);
                }

                materials[index] = persistent;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private static void NormalizeImportedHierarchy(GameObject model)
    {
        foreach (var transform in model.GetComponentsInChildren<Transform>(true))
        {
            transform.name = transform.name
                .Replace("GreenHell", string.Empty)
                .Replace("Green Hell", string.Empty)
                .Replace("green_hell", string.Empty);
        }
    }

    private static int RemoveMissingScripts(GameObject root)
    {
        var removed = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
        return removed;
    }

    private static int ValidatePersistentRenderMaterials(GameObject root)
    {
        var rendererCount = 0;
        var slotCount = 0;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!renderer.enabled || renderer.forceRenderingOff ||
                !AudiR8Materials.IsAudiR8Renderer(renderer.transform) ||
                renderer.GetComponent<MeshFilter>()?.sharedMesh == null)
            {
                continue;
            }

            rendererCount++;
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0)
                throw new InvalidOperationException(
                    $"Visible Audi R8 renderer '{renderer.name}' has no persistent materials.");
            foreach (var material in materials)
            {
                if (material == null)
                    throw new InvalidOperationException(
                        $"Visible Audi R8 renderer '{renderer.name}' has a null material slot.");
                if (material.shader == null ||
                    !material.shader.name.StartsWith("HDRP/", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Audi R8 material '{material.name}' uses unsupported shader " +
                        $"'{material.shader?.name ?? "<null>"}'.");
                }
                slotCount++;
            }
        }

        if (rendererCount == 0 || slotCount == 0)
            throw new InvalidOperationException(
                "No visible Audi R8 renderers with persistent HDRP materials were found.");
        return slotCount;
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

        manifest.ModId = "Audi_R8";
        manifest.DisplayName = "Audi R8";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "audir8.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Audi R8 manifest references could not be assigned.");
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

    private static bool TryGetModelBodyBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var current = renderer.transform;
            var isWheelOrCaliper = false;
            while (current != null && current != root)
            {
                if (current.name.StartsWith("w:", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current.name, "Wheels", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current.name, "Calipers", StringComparison.OrdinalIgnoreCase))
                {
                    isWheelOrCaliper = true;
                    break;
                }
                current = current.parent;
            }
            if (isWheelOrCaliper)
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

    private static bool HasMaterialMarker(Renderer renderer, string marker)
    {
        foreach (var material in renderer.sharedMaterials)
            if (material != null && material.name.IndexOf(
                    marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static bool HasAncestorNameFragment(Transform transform, string fragment)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static bool TryGetAudiR8RendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!AudiR8Materials.IsAudiR8Renderer(renderer.transform))
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

    private static Transform? FindTransformWithNameFragment(Transform root, string fragment)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
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
        value = value
            .Replace("GreenHell", string.Empty)
            .Replace("Green Hell", string.Empty)
            .Replace("green_hell", string.Empty);
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '-' && chars[index] != '_')
                chars[index] = '_';
        }

        return new string(chars);
    }
}
