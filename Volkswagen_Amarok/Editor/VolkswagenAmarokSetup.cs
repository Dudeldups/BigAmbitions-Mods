#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class VolkswagenAmarokSetup
{
    private const string ModRoot = "Assets/Mods/Volkswagen_Amarok";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/2017_volkswagen_amarok_v6.glb";
    private const string LightOverlayModelPath = ModRoot + "/Models/AmarokLightOverlays.glb";
    private const string MaterialManifestPath = ModRoot + "/Config/AmarokMaterialManifest.json";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/AmarokDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/VolkswagenAmarok.asset";
    private const string VehiclePrefabPath = ModRoot + "/VolkswagenAmarok.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/VolkswagenAmarok.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/volkswagenamarok.unity3d";
    private const string VehicleTypeName =
        "volkswagenamarok-vehicle:vehicletype_volkswagenamarok";
    private const float TargetLength = 5.254f;
    private const float TargetWidth = 1.954f;
    // Width including mirrors; used only for the imported visual.
    private const float VisualTargetWidth = 2.228f;
    // Lower the body relative to the already-correct wheel centers.
    private const float BodyVisualBottomY = -0.070f;
    private const float TargetHeight = 1.834f;
    private const float FrontTrack = 1.654f;
    private const float RearTrack = 1.658f;
    private const float Wheelbase = 3.097f;
    private const float FrontTireRadius = 0.382f;
    private const float RearTireRadius = 0.382f;
    private const float FrontTireWidth = 0.255f;
    private const float RearTireWidth = 0.255f;
    private const float WheelInset = -0.010f;
    private const float WheelCenterRideHeightOffset = 0.060f;
    private const float VehicleLinearDrag = 0.020f;
    private const float VehicleBrakeForce = 2350f;
    private const float BrakeMaxTorque = 2350f;
    private const float FrontForwardGrip = 1.08f;
    private const float RearForwardGrip = 1.05f;
    private const float FrontForwardStiffness = 1.12f;
    private const float RearForwardStiffness = 1.10f;
    private const float TireFrictionCircleStrength = 1.08f;
    private const float AntiRollBarForce = 3800f;
    private const float FrontSuspensionTravel = 0.160f;
    private const float RearSuspensionTravel = 0.180f;
    private const float DeformationStrength = 0.18f;
    private const float DeformationRadius = 0.30f;
    private const float DeformationRandomness = 0.005f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.34f, -0.05f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f + WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, 1.733f) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f - WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, 1.733f) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f + WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -1.361f) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f - WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -1.361f) },
        };
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.46f, 2.08f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.86f, 0.72f, 1.14f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.46f, -2.08f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.86f, 0.72f, 1.14f);

    private static readonly float[] GT3RSGears =
    {
        -3.317f, 0f, 4.714f, 3.143f, 2.106f, 1.667f, 1.285f, 1.000f, 0.839f, 0.667f,
    };

    private static AnimationCurve CreateGT3RSPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.16f, 0.18f),
            new Keyframe(0.31f, 0.47f), new Keyframe(0.44f, 0.67f),
            new Keyframe(0.61f, 0.94f), new Keyframe(0.67f, 0.99f),
            new Keyframe(0.78f, 1.00f), new Keyframe(0.89f, 1.00f),
            new Keyframe(1.00f, 0.99f));
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "VolkswagenAmarok setup complete: generated the 2017 Amarok V6 with four " +
            "independent wheel visuals, 8-speed automatic, 4MOTION AWD, V6 TDI tuning, " +
            "functional lamp geometry, body repaint support and damage geometry.");
    }

    public static void ApplyInGameFeedbackToExistingPrefabAndBuildStandaloneWindowsAssetBundle()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var feedbackVehicleType = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath) ??
                                  throw new InvalidOperationException("Existing Amarok VehicleType asset is missing.");
        var feedbackVehicleSerialized = new SerializedObject(feedbackVehicleType);
        SetNumber(feedbackVehicleSerialized, "damageIntensity", 0.31f);
        SetNumber(feedbackVehicleSerialized, "brakeForce", VehicleBrakeForce);
        SetNumber(feedbackVehicleSerialized, "maxCargoCapacity", 24f);
        SetBool(feedbackVehicleSerialized, "autoParkSupported", false);
        feedbackVehicleSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(feedbackVehicleType);

        var root = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);
        try
        {
            var visual = FindTransform(root.transform, "AmarokVisual") ??
                         FindTransformWithNameFragment(root.transform, "AmarokVisual") ??
                         throw new InvalidOperationException("Existing Amarok visual root is missing.");

            if (!TryGetModelBodyBounds(visual, out var bounds))
                throw new InvalidOperationException("Existing Amarok visual has no measurable body bounds.");

            // The current prefab was normalized to 1.954 m using bounds that include
            // the mirrors. Correct X only; keep the already-correct Y/Z proportions.
            var xScaleCorrection = VisualTargetWidth / Mathf.Max(0.001f, bounds.size.x);
            var visualScale = visual.localScale;
            visualScale.x *= xScaleCorrection;
            visual.localScale = visualScale;

            if (!TryGetModelBodyBounds(visual, out bounds))
                throw new InvalidOperationException("Corrected Amarok visual bounds could not be measured.");

            // Recenter the widened visual around the vehicle root before setting the
            // requested body height. This is idempotent when run repeatedly.
            var centerLocal = root.transform.InverseTransformPoint(bounds.center);
            visual.position += root.transform.TransformVector(
                new Vector3(-centerLocal.x, 0f, -centerLocal.z));

            if (!TryGetModelBodyBounds(visual, out bounds))
                throw new InvalidOperationException("Recentered Amarok visual bounds could not be measured.");
            var bottomLocalY = root.transform.InverseTransformPoint(
                new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)).y;
            visual.position += root.transform.TransformVector(
                new Vector3(0f, BodyVisualBottomY - bottomLocalY, 0f));

            // Blender lamp meshes were authored against the model transform. Keep
            // them exactly aligned with the corrected visual root.
            // Amarok second-feedback wheel + authored-light alignment.
            // Restore the real track width on both the physics controllers and the
            // root-level parked/NPC wheel visuals. This also keeps parked traffic
            // from using the stale inward-shifted wheel transforms.
            var frontLeftWheel = new Vector3(-FrontTrack * 0.5f - 0.010f, 0.306f, 1.733f);
            var frontRightWheel = new Vector3(FrontTrack * 0.5f + 0.010f, 0.306f, 1.733f);
            var rearLeftWheel = new Vector3(-RearTrack * 0.5f - 0.010f, 0.306f, -1.361f);
            var rearRightWheel = new Vector3(RearTrack * 0.5f + 0.010f, 0.306f, -1.361f);
            SetLocalPosition(root, "FrontLeft_WheelController", frontLeftWheel);
            SetLocalPosition(root, "FrontRight_WheelController", frontRightWheel);
            SetLocalPosition(root, "RearLeft_WheelController", rearLeftWheel);
            SetLocalPosition(root, "RearRight_WheelController", rearRightWheel);

            var frontLeftVisual = FindTransform(root.transform, "AmarokWheelFrontLeft");
            var frontRightVisual = FindTransform(root.transform, "AmarokWheelFrontRight");
            var rearLeftVisual = FindTransform(root.transform, "AmarokWheelRearLeft");
            var rearRightVisual = FindTransform(root.transform, "AmarokWheelRearRight");
            if (frontLeftVisual != null) frontLeftVisual.localPosition = frontLeftWheel;
            if (frontRightVisual != null) frontRightVisual.localPosition = frontRightWheel;
            if (rearLeftVisual != null) rearLeftVisual.localPosition = rearLeftWheel;
            if (rearRightVisual != null) rearRightVisual.localPosition = rearRightWheel;

            var frontLeftCaliper = FindTransform(root.transform, "AmarokFixedCaliperFrontLeft");
            var frontRightCaliper = FindTransform(root.transform, "AmarokFixedCaliperFrontRight");
            var rearLeftCaliper = FindTransform(root.transform, "AmarokFixedCaliperRearLeft");
            var rearRightCaliper = FindTransform(root.transform, "AmarokFixedCaliperRearRight");
            if (frontLeftCaliper != null) frontLeftCaliper.localPosition = frontLeftWheel;
            if (frontRightCaliper != null) frontRightCaliper.localPosition = frontRightWheel;
            if (rearLeftCaliper != null) rearLeftCaliper.localPosition = rearLeftWheel;
            if (rearRightCaliper != null) rearRightCaliper.localPosition = rearRightWheel;

            // Refresh the unpacked overlay hierarchy from the current GLB on
            // every existing-prefab build. This is required for newly authored
            // Blender light/trim vertex groups to reach the saved vehicle prefab.
            // Persist the calibrated linear drag in the prefab itself.
            var feedbackRigidbody = root.GetComponent<Rigidbody>();
            if (feedbackRigidbody != null)
                feedbackRigidbody.drag = VehicleLinearDrag;

            var staleLightSources = FindTransform(root.transform, "AmarokLightSources");
            if (staleLightSources != null)
                UnityEngine.Object.DestroyImmediate(staleLightSources.gameObject);
            AttachLightOverlaySources(root, visual.gameObject);

            var lightSources = FindTransform(root.transform, "AmarokLightSources");
            if (lightSources != null)
            {
                lightSources.SetParent(visual, false);
                lightSources.localPosition = Vector3.zero;
                lightSources.localScale = Vector3.one;
                // Confirmed in-game correction: the exported lamp set points
                // upward in AmarokVisual local space. Rotate the complete set
                // exactly 90 degrees toward vehicle forward.
                lightSources.localRotation = Quaternion.Euler(90f, 0f, 0f);
                Debug.Log(
                    $"VolkswagenAmarok authored-light fixed forward rotation=" +
                    $"{lightSources.localEulerAngles}.");
            }

            // The visible outer shell is the root-level deformable body baked from
            // the source mesh. Re-bake it after changing visual scale/position so it
            // cannot remain at the old squeezed width/height. Preserve its persisted
            // body-paint material assignment while recreating the mesh.
            ConfigureOriginalBluePaintSurface(root, visual);

            var oldDamageBody = FindTransform(root.transform, "AmarokDamageBody");
            if (oldDamageBody != null)
                UnityEngine.Object.DestroyImmediate(oldDamageBody.gameObject);

            // CreateDeformableBody now consumes the source-blue-stripped model.
            // Keep the current source materials instead of restoring stale damage
            // body material references from the previous prefab build.
            var damageBody = CreateDeformableBody(root, visual.gameObject);

            ConfigureVehicleDeformation(root, damageBody);
            ConfigurePickupDamageHandler(root);
            ConfigurePowertrain(root);
            ConfigureRendererReferences(root);
            MarkMaterialsDirty(root);

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save in-game feedback changes to the Amarok prefab.");

            if (!TryGetModelBodyBounds(visual, out bounds))
                throw new InvalidOperationException("Final Amarok visual bounds could not be measured.");
            Debug.Log(
                $"VolkswagenAmarok feedback prefab patch complete: visualBounds={bounds.size}, " +
                $"visualBottom={root.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)).y:F3}, " +
                $"downshiftRPM=1900.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        BuildStandaloneWindowsAssetBundle();
    }

    public static void RegenerateAndBuildStandaloneWindowsAssetBundle()
    {
        ApplyInGameFeedbackToExistingPrefabAndBuildStandaloneWindowsAssetBundle();
    }

    [MenuItem("Big Ambitions Mods/Build Volkswagen Amarok AssetBundle")]
    public static void BuildStandaloneWindowsAssetBundle()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        const string bundleFileName = "volkswagenamarok.unity3d";
        const string bundleBaseName = "volkswagenamarok";
        const string bundleVariant = "unity3d";

        var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
        var temporaryOutputDirectory = System.IO.Path.Combine(
            projectRoot, "Temp", "VolkswagenAmarokAssetBundle", "Windows");
        if (System.IO.Directory.Exists(temporaryOutputDirectory))
            System.IO.Directory.Delete(temporaryOutputDirectory, true);
        System.IO.Directory.CreateDirectory(temporaryOutputDirectory);

        var build = new AssetBundleBuild
        {
            // Match the SDK ModPackager convention: "volkswagenamarok.unity3d"
            // is a base bundle name plus the "unity3d" variant, not a literal
            // base name containing a dot.
            assetBundleName = bundleBaseName,
            assetBundleVariant = bundleVariant,
            assetNames = new[]
            {
                VehicleAssetPath,
                VehiclePrefabPath,
            },
        };

        Debug.Log(
            $"VolkswagenAmarok: building Windows AssetBundle in temporary folder " +
            $"'{temporaryOutputDirectory}' from '{VehicleAssetPath}' and '{VehiclePrefabPath}'.");

        var manifest = BuildPipeline.BuildAssetBundles(
            temporaryOutputDirectory,
            new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);
        if (manifest == null)
            throw new InvalidOperationException(
                "Volkswagen Amarok AssetBundle build failed. See the Unity Console entries immediately before this exception for the BuildPipeline error.");

        var producedBundlePath = System.IO.Path.Combine(temporaryOutputDirectory, bundleFileName);
        if (!System.IO.File.Exists(producedBundlePath))
        {
            var producedFiles = System.IO.Directory.Exists(temporaryOutputDirectory)
                ? string.Join(", ", System.IO.Directory.GetFiles(temporaryOutputDirectory))
                : "<temporary output directory missing>";
            throw new InvalidOperationException(
                $"Volkswagen Amarok AssetBundle manifest was created, but '{bundleFileName}' was not. " +
                $"Produced files: {producedFiles}");
        }

        var finalOutputDirectory = System.IO.Path.Combine(
            Application.dataPath, "Mods", "Volkswagen_Amarok", "AssetBundles", "Windows");
        System.IO.Directory.CreateDirectory(finalOutputDirectory);
        var finalBundlePath = System.IO.Path.Combine(finalOutputDirectory, bundleFileName);
        System.IO.File.Copy(producedBundlePath, finalBundlePath, true);

        var producedManifestPath = producedBundlePath + ".manifest";
        if (System.IO.File.Exists(producedManifestPath))
            System.IO.File.Copy(producedManifestPath, finalBundlePath + ".manifest", true);

        var bundle = AssetBundle.LoadFromFile(finalBundlePath);
        if (bundle == null)
            throw new InvalidOperationException(
                $"Could not load freshly built Amarok bundle '{finalBundlePath}'.");
        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException(
                    "Fresh Amarok bundle is missing VolkswagenAmarok.asset or VolkswagenAmarok.prefab.");
        }
        finally
        {
            bundle.Unload(false);
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            $"VolkswagenAmarok: built Windows AssetBundle " +
            $"'Assets/Mods/Volkswagen_Amarok/AssetBundles/Windows/{bundleFileName}'.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    public static void RepairPrefabAndBuild()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ConfigureVehicleTypePerformance();
        var root = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);
        try
        {
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureServiceCompatibility(root);
            ConfigurePowertrain(root);
            RepairTrueTireWheelVisuals(root);
            RepairStaticWheelVisuals(root);
            ConfigureRendererReferences(root);
            PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    public static void VerifyBuiltBundle()
    {
        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");

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
            var brakeForce = ReadNumber(vehicleSerialized.FindProperty("brakeForce"));
            var luxury = vehicleSerialized.FindProperty("isLuxuryCar")?.boolValue ?? true;

            var visual = FindTransform(prefab.transform, "AmarokVisual") ??
                         throw new InvalidOperationException("Amarok visual root is missing.");
            if (!TryGetAmarokRendererBounds(prefab.transform, out var bounds))
                throw new InvalidOperationException("Amarok visual has no renderer bounds.");
            var bodySidesOriented =
                bounds.size.x > bounds.size.y * 1.4f &&
                bounds.size.z > bounds.size.x * 2f;
            var frontMarker = FindTransformWithNameFragment(visual, "bump_front_ok");
            var rearMarker = FindTransformWithNameFragment(visual, "bump_rear_ok");
            var frontFacesVehicleForward =
                frontMarker != null && rearMarker != null &&
                TryGetRendererBounds(frontMarker, out var frontMarkerBounds) &&
                TryGetRendererBounds(rearMarker, out var rearMarkerBounds) &&
                frontMarkerBounds.center.z > rearMarkerBounds.center.z + 2f;
            var windshieldHeight = bounds.max.y;
            var exhaustHeight = bounds.min.y;
            var bodyUpright = bounds.size.y > 1f && bounds.size.y < bounds.size.x;

            var wheelVisuals = 0;
            var wheelGeometryOriented = true;
            var wheelSideMappingCorrect = true;
            var fittedWheelCenters = new Dictionary<string, Vector3>();
            var fixedCalipers = 0;
            var calipersDetachedFromWheels = true;
            var fittedCaliperCenters = new Dictionary<string, Vector3>();
            var deformationBodyValid = false;
            var deformationTuningValid = false;
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
                if (transform.name.StartsWith("AmarokWheel", StringComparison.Ordinal))
                {
                    wheelVisuals++;
                    var isFrontWheel = transform.name.IndexOf("Front", StringComparison.Ordinal) >= 0;
                    var expectedWidth = isFrontWheel ? FrontTireWidth : RearTireWidth;
                    var expectedDiameter = (isFrontWheel ? FrontTireRadius : RearTireRadius) * 2f;
                    if (!TryGetRendererBounds(transform, out var tireBounds) ||
                        Math.Abs(tireBounds.size.x - expectedWidth) > 0.012f ||
                        Math.Abs(tireBounds.size.y - expectedDiameter) > 0.012f ||
                        Math.Abs(tireBounds.size.z - expectedDiameter) > 0.012f ||
                        Vector3.Distance(tireBounds.center, transform.position) > 0.012f)
                    {
                        wheelGeometryOriented = false;
                    }

                    if (FindTransformWithNameFragment(transform, "_caliper") != null)
                        calipersDetachedFromWheels = false;
                    fittedWheelCenters[transform.name] = transform.position;

                    var expectedGeometry = transform.name switch
                    {
                        "AmarokWheelFrontLeft" => "Geometry_AmarokWheel_FL",
                        "AmarokWheelFrontRight" => "Geometry_AmarokWheel_FR",
                        "AmarokWheelRearLeft" => "Geometry_AmarokWheel_RL",
                        "AmarokWheelRearRight" => "Geometry_AmarokWheel_RR",
                        _ => string.Empty,
                    };
                    if (string.IsNullOrEmpty(expectedGeometry) ||
                        FindTransform(transform, expectedGeometry) == null)
                    {
                        wheelSideMappingCorrect = false;
                    }
                }
                if (transform.name.StartsWith("AmarokFixedCaliper", StringComparison.Ordinal))
                {
                    fixedCalipers++;
                    if (FindTransformWithNameFragment(transform, "_caliper") == null)
                        calipersDetachedFromWheels = false;
                    fittedCaliperCenters[transform.name] = transform.position;
                }
                if (transform.name.IndexOf("1RearDrivingLights", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continuousTailLight = true;
                }
                if (transform.name.IndexOf("ThirdBrakeLight", StringComparison.OrdinalIgnoreCase) >= 0)
                    thirdBrakeLight = true;
                if (transform.name.IndexOf("BDRL_Indicator_FL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    transform.name.IndexOf("BDRL_Indicator_FR", StringComparison.OrdinalIgnoreCase) >= 0)
                    frontBlinkerMeshes++;
            }

            var frontLeftCenter = Vector3.zero;
            var frontRightCenter = Vector3.zero;
            var rearLeftCenter = Vector3.zero;
            var rearRightCenter = Vector3.zero;
            var wheelPlacementVerified =
                fittedWheelCenters.TryGetValue("AmarokWheelFrontLeft", out frontLeftCenter) &&
                fittedWheelCenters.TryGetValue("AmarokWheelFrontRight", out frontRightCenter) &&
                fittedWheelCenters.TryGetValue("AmarokWheelRearLeft", out rearLeftCenter) &&
                fittedWheelCenters.TryGetValue("AmarokWheelRearRight", out rearRightCenter);
            var frontTrack = wheelPlacementVerified
                ? Math.Abs(frontRightCenter.x - frontLeftCenter.x)
                : float.NaN;
            var rearTrack = wheelPlacementVerified
                ? Math.Abs(rearRightCenter.x - rearLeftCenter.x)
                : float.NaN;
            var wheelbase = wheelPlacementVerified
                ? Math.Abs(
                    (frontLeftCenter.z + frontRightCenter.z) * 0.5f -
                    (rearLeftCenter.z + rearRightCenter.z) * 0.5f)
                : float.NaN;
            wheelPlacementVerified &=
                Math.Abs(frontTrack - (FrontTrack - WheelInset * 2f)) <= 0.01f &&
                Math.Abs(rearTrack - (RearTrack - WheelInset * 2f)) <= 0.01f &&
                Math.Abs(wheelbase - Wheelbase) <= 0.01f &&
                Math.Abs(frontLeftCenter.z - frontRightCenter.z) < 0.012f &&
                Math.Abs(rearLeftCenter.z - rearRightCenter.z) < 0.012f;
            var caliperPivotsVerified = wheelPlacementVerified &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "AmarokFixedCaliperFrontLeft",
                    frontLeftCenter) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "AmarokFixedCaliperFrontRight",
                    frontRightCenter) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "AmarokFixedCaliperRearLeft",
                    rearLeftCenter) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "AmarokFixedCaliperRearRight",
                    rearRightCenter);

            var transmissionVerified = false;
            var launchResponseVerified = false;
            var brakeSystemVerified = false;
            var antiRollVerified = false;
            var massCenterVerified = false;
            var rigidbody = prefab.GetComponent<Rigidbody>();
            var linearDragVerified = rigidbody != null &&
                                     Math.Abs(rigidbody.drag - VehicleLinearDrag) < 0.001f;
            var tireFrictionCount = 0;
            var forwardGripCount = 0;
            var suspensionTravelCount = 0;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;
                var componentSerialized = new SerializedObject(component);
                var configuredCenter = componentSerialized.FindProperty("centerOfMass");
                var useDefaultCenter = componentSerialized.FindProperty("useDefaultCenterOfMass");
                if (configuredCenter?.propertyType == SerializedPropertyType.Vector3 &&
                    useDefaultCenter?.propertyType == SerializedPropertyType.Boolean)
                {
                    massCenterVerified = !useDefaultCenter.boolValue &&
                                         Vector3.Distance(
                                             configuredCenter.vector3Value,
                                             StableCenterOfMass) < 0.005f;
                }
                var friction = componentSerialized.FindProperty("frictionCircleStrength");
                if (friction != null &&
                    component.transform.name.EndsWith("_WheelController", StringComparison.Ordinal) &&
                    Math.Abs(ReadNumber(friction) - TireFrictionCircleStrength) < 0.005f)
                {
                    tireFrictionCount++;
                }
                var forwardFriction = componentSerialized.FindProperty("forwardFriction");
                var forwardGrip = forwardFriction?.FindPropertyRelative("grip");
                var forwardStiffness = forwardFriction?.FindPropertyRelative("stiffness");
                var expectedForwardGrip = component.transform.name.StartsWith(
                    "Front", StringComparison.Ordinal)
                    ? FrontForwardGrip
                    : RearForwardGrip;
                var expectedForwardStiffness = component.transform.name.StartsWith(
                    "Front", StringComparison.Ordinal)
                    ? FrontForwardStiffness
                    : RearForwardStiffness;
                if (forwardGrip != null && forwardStiffness != null &&
                    component.transform.name.EndsWith("_WheelController", StringComparison.Ordinal) &&
                    Math.Abs(ReadNumber(forwardGrip) - expectedForwardGrip) < 0.005f &&
                    Math.Abs(ReadNumber(forwardStiffness) - expectedForwardStiffness) < 0.005f)
                {
                    forwardGripCount++;
                }
                var springTravel = componentSerialized.FindProperty("spring")
                    ?.FindPropertyRelative("maxLength");
                var isWheelController = component.transform.name.EndsWith(
                    "_WheelController", StringComparison.Ordinal);
                var expectedTravel = component.transform.name.StartsWith(
                    "Front", StringComparison.Ordinal)
                    ? FrontSuspensionTravel
                    : RearSuspensionTravel;
                if (springTravel != null && isWheelController &&
                    Math.Abs(ReadNumber(springTravel) - expectedTravel) < 0.005f)
                {
                    suspensionTravelCount++;
                }
                if (component == null ||
                    !string.Equals(
                        component.GetType().FullName,
                        "NWH.VehiclePhysics2.VehicleController",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var serialized = new SerializedObject(component);
                brakeSystemVerified = Math.Abs(ReadNumber(
                    serialized.FindProperty("brakes")?.FindPropertyRelative("maxTorque")) -
                    BrakeMaxTorque) < 0.5f;
                var powertrain = serialized.FindProperty("powertrain");
                var wheelGroups = powertrain?.FindPropertyRelative("wheelGroups");
                antiRollVerified = wheelGroups != null && wheelGroups.isArray && wheelGroups.arraySize == 2;
                if (antiRollVerified)
                {
                    for (var index = 0; index < wheelGroups!.arraySize; index++)
                    {
                        antiRollVerified &= Math.Abs(ReadNumber(
                            wheelGroups.GetArrayElementAtIndex(index)
                                .FindPropertyRelative("antiRollBarForce")) - AntiRollBarForce) < 0.5f;
                    }
                }
                var transmission = powertrain?.FindPropertyRelative("transmission");
                var gearCount = transmission?.FindPropertyRelative("forwardGearCount")?.intValue ?? 0;
                var gears = transmission?.FindPropertyRelative("gears");
                transmissionVerified = gearCount == 8 && gears != null && gears.arraySize == 10;
                var clutch = powertrain?.FindPropertyRelative("clutch");
                var engine = powertrain?.FindPropertyRelative("engine");
                var powerCurve = engine?.FindPropertyRelative("powerCurve")?.animationCurveValue;
                launchResponseVerified =
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRPM")) - 1000f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("throttleEngagementOffsetRPM")) - 250f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRange")) - 300f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("creepTorque"))) < 0.01f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("inertia")) - 0.16f) < 0.001f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("startDuration")) - 0.38f) < 0.001f &&
                    GT3RSPowerCurveMatches(powerCurve) &&
                    !(engine?.FindPropertyRelative("stallingEnabled")?.boolValue ?? true);
            }

            var opaqueMaterials = new HashSet<Material>();
            var decalSafeMaterials = 0;
            var transparentMaterials = 0;
            var transparentMaterialsDoubleSided = true;
            var cabinGlassTintValid = true;
            var opaqueRendererMasksSafe = true;
            var paintRenderers = new HashSet<Renderer>();
            var bodyPaintSlots = 0;
            var interiorAccentPaintSlots = 0;
            var darkBodyPaintSlots = 0;
            var rimSlots = 0;
            var rimMaterials = new HashSet<Material>();
            var rimFinishValid = true;
            var interiorPrimaryPaintSlots = 0;
            var interiorSecondaryPaintSlots = 0;
            var interiorDarkPaintSlots = 0;
            var caliperSlots = 0;
            var rimInnerSlots = 0;
            var seatSlots = 0;
            var paintTexturesReadable = true;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!VolkswagenAmarokMaterials.IsAmarokRenderer(renderer.transform))
                    continue;

                var hasOpaque = false;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                        continue;
                    if (IsBodyPaintMaterial(material) ||
                        IsCaliperMaterial(material) ||
                        IsInteriorAccentPaintMaterial(material))
                    {
                        if (!paintRenderers.Contains(renderer))
                            paintRenderers.Add(renderer);
                        bodyPaintSlots++;
                    }
                    if (IsInteriorAccentPaintMaterial(material)) interiorAccentPaintSlots++;
                    if (IsDarkBodyPaintMaterial(material)) darkBodyPaintSlots++;
                    if (IsRimMaterial(material))
                    {
                        rimSlots++;
                        rimMaterials.Add(material);
                        var expectedRimColor = VolkswagenAmarokMaterials.RimBaseColor;
                        var rimColor = material.HasProperty("_BaseColor")
                            ? material.GetColor("_BaseColor")
                            : Color.clear;
                        rimFinishValid &=
                            material.HasProperty("_Metallic") &&
                            Math.Abs(material.GetFloat("_Metallic") - VolkswagenAmarokMaterials.RimMetallic) < 0.01f &&
                            material.HasProperty("_Smoothness") &&
                            Math.Abs(material.GetFloat("_Smoothness") - VolkswagenAmarokMaterials.RimSmoothness) < 0.01f &&
                            Math.Abs(rimColor.r - expectedRimColor.r) < 0.01f &&
                            Math.Abs(rimColor.g - expectedRimColor.g) < 0.01f &&
                            Math.Abs(rimColor.b - expectedRimColor.b) < 0.01f;
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
                    if (VolkswagenAmarokMaterials.IsTransparentMaterial(material))
                    {
                        transparentMaterials++;
                        transparentMaterialsDoubleSided &=
                            (!material.HasProperty("_Cull") || material.GetFloat("_Cull") < 0.5f) &&
                            (!material.HasProperty("_DoubleSidedEnable") ||
                             material.GetFloat("_DoubleSidedEnable") > 0.5f) &&
                            material.IsKeywordEnabled("_DOUBLESIDED_ON");
                        if (IsCabinGlassRenderer(renderer.transform) &&
                            string.Equals(material.shader.name, "HDRP/Lit", StringComparison.Ordinal))
                        {
                            var tint = material.HasProperty("_BaseColor")
                                ? material.GetColor("_BaseColor")
                                : material.HasProperty("baseColorFactor")
                                    ? material.GetColor("baseColorFactor")
                                    : Color.black;
                            cabinGlassTintValid &= tint.r >= 0.04f &&
                                                   tint.a >= 0.24f &&
                                                   tint.a <= 0.34f &&
                                                   (!material.HasProperty("_Smoothness") ||
                                                    material.GetFloat("_Smoothness") >= 0.90f) &&
                                                   (!material.HasProperty("_Metallic") ||
                                                   material.GetFloat("_Metallic") <= 0.01f);
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

            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null ||
                    !string.Equals(
                        component.GetType().Name,
                        "VehicleDeformationController",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var deformation = new SerializedObject(component);
                var meshFilters = deformation.FindProperty("meshFilters");
                if (meshFilters != null && meshFilters.isArray && meshFilters.arraySize == 1 &&
                    meshFilters.GetArrayElementAtIndex(0).objectReferenceValue is MeshFilter bodyFilter)
                {
                    deformationBodyValid =
                        string.Equals(
                            bodyFilter.name,
                            "AmarokDamageBody",
                            StringComparison.Ordinal) &&
                        bodyFilter.transform.parent == prefab.transform &&
                        bodyFilter.transform.localPosition.sqrMagnitude < 0.000001f &&
                        Quaternion.Angle(bodyFilter.transform.localRotation, Quaternion.identity) < 0.01f &&
                        Vector3.Distance(bodyFilter.transform.localScale, Vector3.one) < 0.0001f &&
                        bodyFilter.sharedMesh != null &&
                        bodyFilter.sharedMesh.isReadable;
                }

                deformationTuningValid =
                    Math.Abs(ReadNumber(deformation.FindProperty("deformationStrength")) -
                             DeformationStrength) < 0.001f &&
                    Math.Abs(ReadNumber(deformation.FindProperty("deformationRadius")) -
                             DeformationRadius) < 0.001f &&
                    Math.Abs(ReadNumber(deformation.FindProperty("deformationRandomness")) -
                             DeformationRandomness) < 0.001f;
                break;
            }

            if (Math.Abs(price - 49900f) > 0.5f ||
                Math.Abs(maxFuel - 80f) > 0.5f ||
                Math.Abs(maxSpeed - 193f) > 0.5f ||
                Math.Abs(enginePower - 165f) > 0.5f ||
                Math.Abs(brakeForce - VehicleBrakeForce) > 0.5f ||
                luxury ||
                Math.Abs(bounds.size.z - TargetLength) > 0.02f ||
                Math.Abs(bounds.size.x - TargetWidth) > 0.04f ||
                Math.Abs(bounds.size.y -
                         TargetHeight) > 0.02f ||
                !bodySidesOriented ||
                !bodyUpright ||
                !frontFacesVehicleForward ||
                wheelVisuals != 4 ||
                !wheelGeometryOriented ||
                !wheelSideMappingCorrect ||
                !wheelPlacementVerified ||
                
                
                !deformationBodyValid ||
                !deformationTuningValid ||
                !continuousTailLight ||
                !thirdBrakeLight ||
                frontBlinkerMeshes < 2 ||
                !headlightTemplateValid ||
                !transmissionVerified ||
                !launchResponseVerified ||
                !brakeSystemVerified ||
                !massCenterVerified ||
                !linearDragVerified ||
                !antiRollVerified ||
                tireFrictionCount != 4 ||
                forwardGripCount != 4 ||
                suspensionTravelCount != 4 ||
                opaqueMaterials.Count == 0 ||
                decalSafeMaterials != opaqueMaterials.Count ||
                !opaqueRendererMasksSafe ||
                transparentMaterials == 0 ||
                !transparentMaterialsDoubleSided ||
                !cabinGlassTintValid ||
                bodyPaintSlots == 0 ||
                
                !paintReferencesValid)
            {
                throw new InvalidOperationException(
                    $"Bundle verification failed: price={price}, fuel={maxFuel}, " +
                    $"speed={maxSpeed}, power={enginePower}, brakeForce={brakeForce}, luxury={luxury}, " +
                    $"bounds={bounds.size}, bodySidesOriented={bodySidesOriented}, " +
                    $"bodyUpright={bodyUpright}, frontForward={frontFacesVehicleForward}, " +
                    $"windshieldY={windshieldHeight:F3}, exhaustY={exhaustHeight:F3}, " +
                    $"wheels={wheelVisuals}, " +
                    $"wheelGeometryOriented={wheelGeometryOriented}, wheelSides={wheelSideMappingCorrect}, " +
                    $"wheelPlacement={wheelPlacementVerified}, wheelbase={wheelbase:F3}, " +
                    $"frontTrack={frontTrack:F3}, rearTrack={rearTrack:F3}, " +
                    $"fixedCalipers={fixedCalipers}, calipersDetached={calipersDetachedFromWheels}, " +
                    $"caliperPivots={caliperPivotsVerified}, " +
                    $"deformationBody={deformationBodyValid}, " +
                    $"deformationTuning={deformationTuningValid}, " +
                    $"continuousTailLight={continuousTailLight}, thirdBrakeLight={thirdBrakeLight}, " +
                    $"frontBlinkers={frontBlinkerMeshes}, sideBlinkers={sideBlinkerMeshes}, " +
                    $"headlightTemplate={headlightTemplateValid}, " +
                    $"eightSpeed={transmissionVerified}, launchResponse={launchResponseVerified}, " +
                    $"brakeSystem={brakeSystemVerified}, " +
                    $"massCenter={massCenterVerified}, linearDrag={linearDragVerified}, " +
                    $"antiRoll={antiRollVerified}, " +
                    $"tireFrictionCount={tireFrictionCount}, " +
                    $"forwardGripCount={forwardGripCount}, " +
                    $"suspensionTravelCount={suspensionTravelCount}, " +
                    $"opaque={opaqueMaterials.Count}, " +
                    $"decalSafe={decalSafeMaterials}, transparent={transparentMaterials}, " +
                    $"transparentDoubleSided={transparentMaterialsDoubleSided}, " +
                    $"cabinGlassTint={cabinGlassTintValid}, " +
                    $"bodyPaintSlots={bodyPaintSlots}, interiorAccentSlots={interiorAccentPaintSlots}, " +
                    $"caliperSlots={caliperSlots}, rimSlots={rimSlots}, " +
                    $"rimMaterials={rimMaterials.Count}, rimFinish={rimFinishValid}, " +
                    $"paintReferences={paintReferencesValid}, " +
                    $"rendererMasksSafe={opaqueRendererMasksSafe}.");
            }

            Debug.Log(
                $"VolkswagenAmarok bundle verified: price={price}, speed={maxSpeed}, " +
                $"power={enginePower}, brakeForce={brakeForce}, bounds={bounds.size}, " +
                $"wheels=4, sevenSpeed=true, rwd=true, " +
                $"fixedCalipers=4, steeringCaliperPivots=true, tireBoundsCentered=true, wheelbase={wheelbase:F3}, " +
                $"frontTrack={frontTrack:F3}, rearTrack={rearTrack:F3}, " +
                $"stableCenterOfMass=true, tireFriction={TireFrictionCircleStrength:F2}, " +
                $"suspensionTravel={FrontSuspensionTravel:F2}/{RearSuspensionTravel:F2}, " +
                $"damageBody=outer-shell-only, deformation={DeformationStrength:F2}/{DeformationRadius:F2}, " +
                $"launchResponse=true, brakeTorque={BrakeMaxTorque:F0}, " +
                $"continuousTailLight=true, thirdBrakeLight=true, blinkers=4, " +
                $"headlightTemplate=true, transparentDoubleSided=true, cabinGlassTint=true, " +
                $"bodyPaintSlots={bodyPaintSlots}, interiorAccentSlots={interiorAccentPaintSlots}, " +
                $"calipersPainted=true, rimsFactoryColor=true, rimFinish=balanced-matte-graphite, " +
                $"decalSafeMaterials={decalSafeMaterials}.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static UnityEngine.Object CreateVehicleType()
    {
        var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ReferenceAssetPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R VehicleType reference asset was not found.");

        var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (target == null)
        {
            if (!AssetDatabase.CopyAsset(ReferenceAssetPath, VehicleAssetPath))
                throw new InvalidOperationException("Could not create the Amarok VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated Amarok VehicleType asset did not load.");

        target.name = "VolkswagenAmarok";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 49900f);
        SetNumber(serialized, "maxFuel", 80f);
        SetNumber(serialized, "maxCargoCapacity", 24f); // No invented cargo capacity. // No invented pickup cargo capacity.
        SetNumber(serialized, "maxSpeed", 193f);
        SetNumber(serialized, "enginePower", 165f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", 35f);
        SetNumber(serialized, "damageIntensity", 0.28f);
        SetBool(serialized, "isATruck", true);
        SetBool(serialized, "isHandVehicle", false);
        SetBool(serialized, "fitsHandTruck", false);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", false);
        SetBool(serialized, "hasRadio", true);
        SetBool(serialized, "isLuxuryCar", false);
        SetBool(serialized, "enclosed", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        return target;
    }

    private static void ConfigureVehicleTypePerformance()
    {
        var vehicleType = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (vehicleType == null)
            throw new InvalidOperationException("Amarok VehicleType asset was not found.");

        var serialized = new SerializedObject(vehicleType);
        SetNumber(serialized, "maxSpeed", 193f);
        SetNumber(serialized, "enginePower", 165f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(vehicleType);
    }

    private static void CreateVehiclePrefab(UnityEngine.Object vehicleType)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R reference prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("Amarok 992 GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "VolkswagenAmarok";
        try
        {
            StripAudiGeometry(root);
            RemoveAudiDonorHierarchy(root);
            RemoveAudiSpecificBehaviours(root);
            RemoveMissingScripts(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureServiceCompatibility(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigureNavigationReferences(root);
            ConfigurePowertrain(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the Amarok model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "AmarokVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            modelInstance.transform.localPosition += new Vector3(0f, 0.136f, 0f); // Amarok body-to-wheel fitment
            AttachLightOverlaySources(root, modelInstance);
            ConfigureExitMarkers(root, modelInstance);
            AssignAmarokMaterialsFromManifest(modelInstance);
            AttachWheelVisuals(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            ConfigurePickupDamageHandler(root);
            var fix = PreparePrefabMaterialsWithoutRuntimeClones(root);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);

            Debug.Log(
                $"VolkswagenAmarok: prepared decal-safe materials renderers={fix.RendererCount}, " +
                $"decalMasksCleared={fix.DecalMasksCleared}, " +
                $"opaqueFixed={fix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={fix.TransparentMaterialsFixed}, " +
                $"cabinGlass={fix.CabinGlassRenderers}/" +
                $"reenabled={fix.CabinGlassRenderersReenabled}, " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, " +
                $"hdrpValidated={fix.MaterialsValidated}.");

            EnsureNoAudiDonorHierarchy(root);
            EnsureNoMissingScripts(root);
            EnsureNoErrorShaders(root);
            var componentLayoutBeforeSave = CaptureComponentLayout(root);

            var preSaveNullMaterialSlots = 0;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material == null) preSaveNullMaterialSlots++;
            if (preSaveNullMaterialSlots != 0)
                throw new InvalidOperationException(
                    $"Amarok prefab has {preSaveNullMaterialSlots} null material slot(s) immediately before SaveAsPrefabAsset().");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Amarok vehicle prefab.");
            EnsureNoAudiDonorHierarchy(result);
            EnsureNoMissingScripts(result, componentLayoutBeforeSave);
            EnsureNoErrorShaders(result);
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

    private static void RemoveAudiDonorHierarchy(GameObject root)
    {
        Transform? host = null;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null || transform == root.transform)
                continue;
            var name = transform.name;
            if (string.Equals(name, "2020_abt_sportline_audi_rs6-r", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("abt_sportline_audi_rs6", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                host = transform;
                break;
            }
        }

        if (host == null)
            throw new InvalidOperationException("Audi donor navigation host was not found on the reference prefab.");

        // This donor object is not just geometry: it carries the vanilla/generic
        // NavMeshObstacle and VehicleNavMeshObstacleToggler used by CarController.
        // Preserve those components, remove the Audi identity, and keep its already
        // stripped renderer/filter harmless.
        host.name = "VehicleNavObstacle";
        foreach (var renderer in host.GetComponents<Renderer>())
        {
            renderer.enabled = false;
            renderer.sharedMaterials = Array.Empty<Material>();
        }
        foreach (var filter in host.GetComponents<MeshFilter>())
            filter.sharedMesh = null;

        var obstacle = host.GetComponent<UnityEngine.AI.NavMeshObstacle>() ??
                       throw new InvalidOperationException("Reference NavMeshObstacle is missing from the donor navigation host.");
        obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
        obstacle.center = new Vector3(0f, TargetHeight * 0.45f, 0f);
        obstacle.size = new Vector3(TargetWidth * 0.96f, TargetHeight * 0.90f, TargetLength * 0.94f);
        obstacle.carving = true;
        obstacle.carveOnlyStationary = true;
        obstacle.carvingTimeToStationary = 0.5f;
        obstacle.carvingMoveThreshold = 0.1f;
    }

    private static void RemoveMissingScripts(GameObject root)
    {
        var removed = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null)
                continue;
            var gameObject = transform.gameObject;
            var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            if (count <= 0)
                continue;
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
            removed += count;
        }
        Debug.Log($"VolkswagenAmarok: removed {removed} missing donor script component(s).");
    }

    private static void EnsureNoMissingScripts(
        GameObject root,
        Dictionary<string, string[]>? expectedLayout = null)
    {
        var details = new List<string>();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null)
                continue;
            var gameObject = transform.gameObject;
            var missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            if (missingCount <= 0)
                continue;

            var path = GetRelativeTransformPath(root.transform, transform);
            var components = gameObject.GetComponents<Component>();
            for (var index = 0; index < components.Length; index++)
            {
                if (components[index] != null)
                    continue;
                var expected = "<unknown-before-save>";
                if (expectedLayout != null &&
                    expectedLayout.TryGetValue(path, out var types) &&
                    index < types.Length)
                {
                    expected = types[index];
                }
                var previous = index > 0 && components[index - 1] != null
                    ? components[index - 1].GetType().FullName
                    : "<none>";
                var next = index + 1 < components.Length && components[index + 1] != null
                    ? components[index + 1].GetType().FullName
                    : "<none>";
                details.Add(
                    $"path='{path}' slot={index} expectedBeforeSave='{expected}' " +
                    $"previous='{previous}' next='{next}'");
            }
        }

        if (details.Count == 0)
            return;

        foreach (var detail in details)
            Debug.LogError("VolkswagenAmarok missing-script diagnostic: " + detail);
        throw new InvalidOperationException(
            $"Generated Amarok prefab still contains {details.Count} missing script component(s): " +
            string.Join(" | ", details));
    }

    private static Dictionary<string, string[]> CaptureComponentLayout(GameObject root)
    {
        var result = new Dictionary<string, string[]>();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null)
                continue;
            var path = GetRelativeTransformPath(root.transform, transform);
            var components = transform.gameObject.GetComponents<Component>();
            var types = new string[components.Length];
            for (var index = 0; index < components.Length; index++)
                types[index] = components[index]?.GetType().FullName ?? "<missing-before-save>";
            result[path] = types;
        }
        return result;
    }

    private static string GetRelativeTransformPath(Transform root, Transform target)
    {
        if (target == root)
            return root.name;
        var names = new List<string>();
        for (var current = target; current != null && current != root; current = current.parent)
            names.Add(current.name);
        names.Reverse();
        return root.name + "/" + string.Join("/", names);
    }

    private static void EnsureNoAudiDonorHierarchy(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null)
                continue;
            var name = transform.name;
            if (string.Equals(name, "2020_abt_sportline_audi_rs6-r", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("abt_sportline_audi_rs6", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(
                    $"Generated Amarok prefab still contains Audi donor hierarchy '{name}'.");
            }
        }
    }

    private static void ConvertAmarokOpaqueMaterialsToHdrp(GameObject model)
    {
        var normalizedOpaque = 0;
        var normalizedTransparent = 0;
        var materials = new HashSet<Material>();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !materials.Add(material))
                    continue;

                var transparent = VolkswagenAmarokMaterials.IsTransparentMaterial(material);
                VolkswagenAmarokMaterials.NormalizeImportedMaterial(material);
                EditorUtility.SetDirty(material);
                if (transparent) normalizedTransparent++; else normalizedOpaque++;
            }
        }

        Debug.Log(
            $"VolkswagenAmarok: normalized persistent HDRP materials " +
            $"opaque={normalizedOpaque}, transparent={normalizedTransparent}.");
    }

    private static string? FirstMaterialTextureProperty(Material material, params string[] names)
    {
        foreach (var name in names)
            if (material.HasProperty(name) && material.GetTexture(name) != null)
                return name;
        return null;
    }

    private static Color ReadMaterialColor(
        Material material,
        string first,
        string second,
        string third,
        Color fallback)
    {
        if (material.HasProperty(first)) return material.GetColor(first);
        if (material.HasProperty(second)) return material.GetColor(second);
        if (material.HasProperty(third)) return material.GetColor(third);
        return fallback;
    }

    private static float ReadMaterialFloat(
        Material material,
        string first,
        string? second,
        float fallback)
    {
        if (material.HasProperty(first)) return material.GetFloat(first);
        if (second != null && material.HasProperty(second)) return material.GetFloat(second);
        return fallback;
    }

    private static void EnsureNoErrorShaders(GameObject root)
    {
        var broken = new List<string>();
        var seen = new HashSet<Material>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !seen.Add(material))
                    continue;
                var shader = material.shader;
                if (shader == null ||
                    string.Equals(shader.name, "Hidden/InternalErrorShader", StringComparison.Ordinal))
                {
                    broken.Add($"{renderer.name}/{material.name}");
                }
            }
        }

        if (broken.Count > 0)
            throw new InvalidOperationException(
                "Generated Amarok prefab still has error-shader materials: " +
                string.Join(", ", broken.GetRange(0, Math.Min(8, broken.Count))));
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
        body.mass = 2078f;
        body.drag = VehicleLinearDrag;
        body.angularDrag = 1.65f;
        body.centerOfMass = StableCenterOfMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

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
            SetNumber(serialized, "baseMass", 2078f);
            SetNumber(serialized, "combinedMass", 2078f);
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
                SetRelativeNumber(serialized, "spring.maxForce", 24000f);
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
        if (colliders.Length < 2)
            throw new InvalidOperationException("Reference vehicle requires two body colliders.");

        colliders[0].center = new Vector3(0f, 0.32f, 0f);
        colliders[0].size = new Vector3(1.82f, 0.42f, 4.40f);
        colliders[1].center = new Vector3(0f, 0.78f, -0.08f);
        colliders[1].size = new Vector3(1.62f, 0.72f, 2.70f);
        var frontContactCollider = colliders.Length > 2
            ? colliders[2]
            : holder.gameObject.AddComponent<BoxCollider>();
        frontContactCollider.center = FrontContactColliderCenter;
        frontContactCollider.size = FrontContactColliderSize;
        frontContactCollider.isTrigger = false;
        frontContactCollider.enabled = true;
        var rearContactCollider = colliders.Length > 3
            ? colliders[3]
            : holder.gameObject.AddComponent<BoxCollider>();
        rearContactCollider.center = RearContactColliderCenter;
        rearContactCollider.size = RearContactColliderSize;
        rearContactCollider.isTrigger = false;
        rearContactCollider.enabled = true;
    }

    private static void RepairStaticWheelVisuals(GameObject root)
    {
        foreach (var pair in WheelControllerPositions)
        {
            var corner = pair.Key.Replace("_WheelController", string.Empty);
            var controller = FindTransform(root.transform, pair.Key) ??
                             throw new InvalidOperationException($"Wheel controller '{pair.Key}' is missing.");
            var mount = FindTransform(root.transform, "AmarokWheel" + corner) ??
                        throw new InvalidOperationException($"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "AmarokFixedCaliper" + corner) ??
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
                HasAncestorNameFragment(renderer.transform, "AmarokWheel"))
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
                if (HasAncestorNameFragment(renderer.transform, "AmarokWheel") &&
                    HasAncestorNameFragment(renderer.transform, "Geometry_AmarokTire_"))
                {
                    attachedTireCount++;
                }
            }

            if (attachedTireCount != 4)
                throw new InvalidOperationException(
                    $"Expected four attached Amarok tires, found {attachedTireCount}.");
            return;
        }
        if (tires.Count != 4)
            throw new InvalidOperationException(
                $"Expected four body-static Amarok tires, found {tires.Count}.");

        foreach (var pair in WheelControllerPositions)
        {
            var corner = pair.Key.Replace("_WheelController", string.Empty);
            var controller = FindTransform(root.transform, pair.Key) ??
                             throw new InvalidOperationException(
                                 $"Wheel controller '{pair.Key}' is missing.");
            var mount = FindTransform(root.transform, "AmarokWheel" + corner) ??
                        throw new InvalidOperationException(
                            $"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "AmarokFixedCaliper" + corner) ??
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
            tire.name = "Geometry_AmarokTire_" +
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

    private static void AttachLightOverlaySources(GameObject root, GameObject modelInstance)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(LightOverlayModelPath);
        if (source == null)
            throw new InvalidOperationException(
                "Models/AmarokLightOverlays.glb is missing. Export it from the supplied Blender vertex groups first.");

        var overlay = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject ??
                      throw new InvalidOperationException("Could not instantiate Amarok light overlays.");
        PrefabUtility.UnpackPrefabInstance(
            overlay,
            PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction);

        overlay.name = "AmarokLightSources";
        overlay.transform.localPosition = modelInstance.transform.localPosition;
        overlay.transform.localRotation = modelInstance.transform.localRotation;
        overlay.transform.localScale = modelInstance.transform.localScale;

        // These meshes are source geometry only. VolkswagenAmarokLightingController
        // creates the visible emissive overlays from them at runtime.
        foreach (var renderer in overlay.GetComponentsInChildren<MeshRenderer>(true))
            renderer.enabled = false;
    }

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        var steeringWheel = FindTransformWithNameFragment(model.transform, "steering_ok") ??
                            throw new InvalidOperationException("Amarok steering wheel node 'steering_ok' is missing.");
        if (!TryGetRendererBounds(steeringWheel, out var steeringBounds))
            throw new InvalidOperationException("Amarok steering wheel renderer bounds are missing.");
        var steeringPosition = root.transform.InverseTransformPoint(steeringBounds.center);
        var driverSide = steeringPosition.x < 0f ? -1.55f : 1.55f;
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.12f, -0.10f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.12f, -0.10f));
        Debug.Log(
            $"VolkswagenAmarok: steering wheel x={steeringPosition.x:F3}; " +
            $"driver exit x={driverSide:F2}.");
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

    private static void ConfigureNavigationReferences(GameObject root)
    {
        var host = FindTransform(root.transform, "VehicleNavObstacle") ??
                   throw new InvalidOperationException("VehicleNavObstacle host is missing.");
        var obstacle = host.GetComponent<UnityEngine.AI.NavMeshObstacle>() ??
                       throw new InvalidOperationException("VehicleNavObstacle has no NavMeshObstacle component.");

        MonoBehaviour? toggler = null;
        foreach (var component in host.GetComponents<MonoBehaviour>())
        {
            if (component != null &&
                component.GetType().Name.IndexOf("NavMeshObstacleToggler", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                toggler = component;
                break;
            }
        }
        if (toggler == null)
            throw new InvalidOperationException("VehicleNavObstacleToggler component is missing from the donor navigation host.");

        var assigned = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || !string.Equals(component.GetType().Name, "CarController", StringComparison.Ordinal))
                continue;
            var serialized = new SerializedObject(component);
            var obstacleProperty = serialized.FindProperty("navMeshObstacle");
            var togglerProperty = serialized.FindProperty("obstacleToggler");
            if (obstacleProperty?.propertyType != SerializedPropertyType.ObjectReference ||
                togglerProperty?.propertyType != SerializedPropertyType.ObjectReference)
                throw new InvalidOperationException("CarController navigation reference fields are missing.");
            obstacleProperty.objectReferenceValue = obstacle;
            togglerProperty.objectReferenceValue = toggler;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            assigned = true;
            break;
        }
        if (!assigned)
            throw new InvalidOperationException("CarController could not be found for navigation-reference assignment.");
    }

    private static void ConfigureServiceCompatibility(GameObject root)
    {
        var bodyHolder = FindTransform(root.transform, "BodyCollider") ??
                         throw new InvalidOperationException(
                             "Amarok service compatibility: BodyCollider is missing.");
        BoxCollider? serviceCollider = null;
        foreach (var box in bodyHolder.GetComponents<BoxCollider>())
        {
            if (box != null && box.enabled && !box.isTrigger)
            {
                serviceCollider = box;
                break;
            }
        }
        if (serviceCollider == null)
            throw new InvalidOperationException(
                "Amarok service compatibility: no enabled body BoxCollider is available.");

        var refuelingPosition = FindTransform(root.transform, "RefuelingPosition");
        if (refuelingPosition == null)
        {
            var host = new GameObject("RefuelingPosition");
            host.transform.SetParent(root.transform, false);
            // Fuel flap area on the Amarok rear quarter. This transform is a
            // service anchor only; station eligibility is still vanilla.
            host.transform.localPosition = new Vector3(-0.92f, 0.82f, -1.55f);
            refuelingPosition = host.transform;
        }
        refuelingPosition.gameObject.SetActive(true);
        refuelingPosition.gameObject.layer = 12;

        var colliderBindings = 0;
        var refuelBindings = 0;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;

            var serialized = new SerializedObject(component);
            var vehicleCollider = serialized.FindProperty("vehicleCollider");
            if (vehicleCollider?.propertyType == SerializedPropertyType.ObjectReference)
            {
                vehicleCollider.objectReferenceValue = serviceCollider;
                colliderBindings++;
            }

            var refuel = serialized.FindProperty("refuelingPosition") ??
                         serialized.FindProperty("refuelingTransform") ??
                         serialized.FindProperty("fuelingPosition");
            if (refuel?.propertyType == SerializedPropertyType.ObjectReference)
            {
                refuel.objectReferenceValue = refuelingPosition;
                refuelBindings++;
            }

            if (string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
            {
                // Runtime owns Amarok visual deformation. Keep the legacy arrays
                // empty, matching BMW, so CarController.Repair().Reset() cannot
                // index an intentionally absent original-mesh entry.
                var meshFilters = serialized.FindProperty("meshFilters");
                if (meshFilters != null && meshFilters.isArray)
                    meshFilters.ClearArray();
                var originals = serialized.FindProperty("originalMeshes");
                if (originals != null && originals.isArray)
                    originals.ClearArray();
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log(
            $"VolkswagenAmarok service compatibility: colliderBindings={colliderBindings}, " +
            $"refuelBindings={refuelBindings}, refuelLocal={refuelingPosition.localPosition}.");
    }

    private static void ConfigurePowertrain(GameObject root)
    {
        var found = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            if (string.Equals(component.GetType().FullName,
                    "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
            {
                found = true;
                SetNumber(serialized, "wheelbase", Wheelbase);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1000f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 250f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 300f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.18f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 165f);
                var curve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (curve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                curve.animationCurveValue = CreateGT3RSPowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 725f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 4500f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.38f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", true);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0.45f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.70f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.28f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 1900f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 4100f);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);
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
                var awd = false;
                for (var index = 0; index < differentials.arraySize; index++)
                {
                    var differential = differentials.GetArrayElementAtIndex(index);
                    var name = differential.FindPropertyRelative("name");
                    if (name?.propertyType != SerializedPropertyType.String ||
                        !string.Equals(name.stringValue, "Center Differential", StringComparison.Ordinal))
                        continue;
                    var bias = differential.FindPropertyRelative("biasAB");
                    if (bias?.propertyType != SerializedPropertyType.Float)
                        throw new InvalidOperationException("Center differential torque bias is missing.");
                    bias.floatValue = 0.60f;
                    awd = true;
                }
                if (!awd)
                    throw new InvalidOperationException("Center differential could not be configured for 4MOTION AWD.");

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = GT3RSGears.Length;
                for (var index = 0; index < GT3RSGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = GT3RSGears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 193f);
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

        // Sketchfab GLBs vary in wrapper rotations. Select the axis arrangement
        // whose renderer bounds are longest on vehicle Z and shortest on Y.
        var candidates = new[]
        {
            Quaternion.identity,
            Quaternion.Euler(90f, 0f, 0f),
            Quaternion.Euler(-90f, 0f, 0f),
        };
        var bestIndex = -1;
        var bestScore = float.NegativeInfinity;
        var bounds = default(Bounds);
        for (var index = 0; index < candidates.Length; index++)
        {
            model.transform.localRotation = candidates[index];
            if (!TryGetModelBodyBounds(model.transform, out var candidateBounds))
                continue;
            var score = candidateBounds.size.z * 4f - candidateBounds.size.x -
                        candidateBounds.size.y * 2f;
            if (score <= bestScore)
                continue;
            bestIndex = index;
            bestScore = score;
            bounds = candidateBounds;
        }
        if (bestIndex < 0 || bounds.size.z <= 0.001f)
            throw new InvalidOperationException("Amarok model length could not be measured.");

        model.transform.localRotation = candidates[bestIndex];
        var front = FindTransformWithNameFragment(model.transform, "bump_front_ok");
        var rear = FindTransformWithNameFragment(model.transform, "bump_rear_ok");
        if (front != null && rear != null &&
            TryGetRendererBounds(front, out var frontBounds) &&
            TryGetRendererBounds(rear, out var rearBounds) &&
            frontBounds.center.z < rearBounds.center.z)
        {
            model.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
        }

        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Oriented Amarok body bounds could not be measured.");
        var scale = bestIndex == 0
            ? new Vector3(
                VisualTargetWidth / bounds.size.x,
                TargetHeight / bounds.size.y,
                TargetLength / bounds.size.z)
            : new Vector3(
                VisualTargetWidth / bounds.size.x,
                TargetLength / bounds.size.z,
                TargetHeight / bounds.size.y);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled Amarok bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        model.transform.position += new Vector3(0f, BodyVisualBottomY, 0f);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Amarok bounds could not be measured.");

        Debug.Log(
            $"VolkswagenAmarok: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var names = new[] { "vw_amorak_2018:wheel", "wheel", "wheel1", "wheel2" };
        var wheels = new List<Transform>();
        foreach (var name in names)
        {
            var wheel = FindTransform(model.transform, name);
            if (wheel != null) wheels.Add(wheel);
        }
        if (wheels.Count != 4)
            throw new InvalidOperationException($"Expected four Amarok wheel roots; found {wheels.Count}.");
        var centers = new Dictionary<Transform, Vector3>();
        var averageZ = 0f;
        foreach (var wheel in wheels)
        {
            var tyre = FindTransformWithNameFragment(wheel, "_tyre_0") ??
                       throw new InvalidOperationException($"Wheel '{wheel.name}' has no tyre mesh.");
            if (!TryGetRendererBounds(tyre, out var bounds))
                throw new InvalidOperationException($"Wheel '{wheel.name}' has no tyre bounds.");
            var center = root.transform.InverseTransformPoint(bounds.center);
            centers[wheel] = center;
            averageZ += center.z;
        }
        averageZ /= 4f;
        foreach (var wheel in wheels)
        {
            var center = centers[wheel];
            var front = center.z >= averageZ;
            var left = center.x < 0f;
            var corner = (front ? "Front" : "Rear") + (left ? "Left" : "Right");
            var controllerName = corner + "_WheelController";
            var controller = FindTransform(root.transform, controllerName) ??
                             throw new InvalidOperationException($"Missing {controllerName}.");
            var tyre = FindTransformWithNameFragment(wheel, "_tyre_0")!;
            TryGetRendererBounds(tyre, out var sourceBounds);
            var target = WheelControllerPositions[controllerName];
            controller.localPosition = target;
            var mount = new GameObject("AmarokWheel" + corner);
            mount.transform.SetParent(root.transform, false);
            mount.transform.position = sourceBounds.center;
            wheel.SetParent(mount.transform, true);
            mount.transform.localScale = new Vector3(
                FrontTireWidth / Mathf.Max(.001f, sourceBounds.size.x),
                FrontTireRadius * 2f / Mathf.Max(.001f, sourceBounds.size.y),
                FrontTireRadius * 2f / Mathf.Max(.001f, sourceBounds.size.z));
            mount.transform.localPosition = target;
            wheel.name = "Geometry_AmarokWheel_" + (front ? "F" : "R") + (left ? "L" : "R");
            var fixedPivot = new GameObject("AmarokFixedCaliper" + corner);
            fixedPivot.transform.SetParent(root.transform, false);
            fixedPivot.transform.localPosition = target;
            new GameObject("_caliper_placeholder").transform.SetParent(fixedPivot.transform, false);
            AssignWheelVisual(controller, mount);
        }
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

    private static void ConfigureOriginalBluePaintSurface(
        GameObject root,
        Transform visual)
    {
        // Remove generated replacement geometry persisted by a previous build.
        // Fresh VehiclePaint_Blue / VehicleOriginal objects are supplied by the
        // newly imported AmarokLightOverlays.glb on every existing-prefab build.
        foreach (var current in root.GetComponentsInChildren<Transform>(true))
        {
            if (!current.name.StartsWith(
                    "VolkswagenAmarok_VehiclePaint_Blue",
                    StringComparison.OrdinalIgnoreCase) &&
                !current.name.StartsWith(
                    "VolkswagenAmarok_VehicleOriginal_",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            UnityEngine.Object.DestroyImmediate(current.gameObject);
        }

        var paintRenderers = new List<MeshRenderer>();
        var remainderRenderers = new List<MeshRenderer>();
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer.name.IndexOf(
                    "VehiclePaint_Blue_",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                paintRenderers.Add(renderer);
                continue;
            }

            if (renderer.name.IndexOf(
                    "VehicleOriginal_",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                remainderRenderers.Add(renderer);
            }
        }

        if (paintRenderers.Count == 0)
            throw new InvalidOperationException(
                "VehiclePaint_Blue panels are missing from AmarokLightOverlays.glb.");
        if (remainderRenderers.Count == 0)
            throw new InvalidOperationException(
                "VehicleOriginal remainder geometry is missing from AmarokLightOverlays.glb.");

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
            AssetDatabase.CreateFolder(ModRoot, "Materials");

        var paintPath = MaterialFolder + "/VolkswagenAmarok_BA_VehiclePaint.mat";
        var paintMaterial = AssetDatabase.LoadAssetAtPath<Material>(paintPath);
        if (paintMaterial == null)
        {
            var shader = Shader.Find("HDRP/Lit") ??
                         Shader.Find("High Definition Render Pipeline/Lit") ??
                         throw new InvalidOperationException(
                             "HDRP/Lit is unavailable for Amarok body paint.");
            paintMaterial = new Material(shader)
            {
                name = "VolkswagenAmarok_BA_VehiclePaint",
            };
            AssetDatabase.CreateAsset(paintMaterial, paintPath);
        }

        var white = Color.white;
        if (paintMaterial.HasProperty("_BaseColor"))
            paintMaterial.SetColor("_BaseColor", white);
        if (paintMaterial.HasProperty("_Color"))
            paintMaterial.SetColor("_Color", white);
        if (paintMaterial.HasProperty("baseColorFactor"))
            paintMaterial.SetColor("baseColorFactor", white);
        if (paintMaterial.HasProperty("_BaseColorMap"))
            paintMaterial.SetTexture("_BaseColorMap", null);
        if (paintMaterial.HasProperty("baseColorTexture"))
            paintMaterial.SetTexture("baseColorTexture", null);
        if (paintMaterial.HasProperty("_MainTex"))
            paintMaterial.SetTexture("_MainTex", null);
        if (paintMaterial.HasProperty("_Metallic"))
            paintMaterial.SetFloat("_Metallic", 0.08f);
        if (paintMaterial.HasProperty("_Smoothness"))
            paintMaterial.SetFloat("_Smoothness", 0.86f);
        if (paintMaterial.HasProperty("_CoatMask"))
            paintMaterial.SetFloat("_CoatMask", 0.18f);
        if (paintMaterial.HasProperty("_SurfaceType"))
            paintMaterial.SetFloat("_SurfaceType", 0f);
        if (paintMaterial.HasProperty("_ZWrite"))
            paintMaterial.SetFloat("_ZWrite", 1f);

        var replacedSources = 0;
        var adoptedRemainders = 0;
        foreach (var paintRenderer in paintRenderers)
        {
            const string paintMarker = "VehiclePaint_Blue_";
            var markerAt = paintRenderer.name.IndexOf(
                paintMarker,
                StringComparison.OrdinalIgnoreCase);
            if (markerAt < 0)
                continue;

            var sourceKey = paintRenderer.name.Substring(
                markerAt + paintMarker.Length);

            MeshRenderer? sourceRenderer = null;
            foreach (var candidate in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (candidate == null)
                    continue;
                if (candidate.name.IndexOf(
                        "VehiclePaint_Blue",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    candidate.name.IndexOf(
                        "VehicleOriginal_",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (!string.Equals(
                        SanitizeAmarokSourceName(candidate.name),
                        sourceKey,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                sourceRenderer = candidate;
                break;
            }

            if (sourceRenderer == null)
                throw new InvalidOperationException(
                    $"Could not map Amarok paint panel '{paintRenderer.name}' " +
                    $"back to source renderer key '{sourceKey}'.");

            var sourceMaterials = sourceRenderer.sharedMaterials;
            if (sourceMaterials.Length == 0)
            {
                // The existing prefab's main body renderer can legitimately have
                // its material slots cleared by CreateDeformableBody(). Resolve
                // the pristine source material array from the imported Amarok GLB
                // instead of depending on that already-processed prefab renderer.
                sourceMaterials = ResolveAmarokSourceMaterials(sourceKey);
                Debug.Log(
                    $"VolkswagenAmarok complementary paint source '{sourceKey}' " +
                    $"restored materials from ModelPath count={sourceMaterials.Length}.");
            }
            var matchedRemainders = 0;
            foreach (var remainderRenderer in remainderRenderers)
            {
                const string remainderMarker = "VehicleOriginal_";
                var remainderAt = remainderRenderer.name.IndexOf(
                    remainderMarker,
                    StringComparison.OrdinalIgnoreCase);
                if (remainderAt < 0)
                    continue;

                var remainderTail = remainderRenderer.name.Substring(
                    remainderAt + remainderMarker.Length);
                var materialMarkerAt = remainderTail.LastIndexOf(
                    "_M",
                    StringComparison.OrdinalIgnoreCase);
                if (materialMarkerAt <= 0)
                    continue;

                var remainderSourceKey = remainderTail.Substring(
                    0,
                    materialMarkerAt);
                if (!string.Equals(
                        remainderSourceKey,
                        sourceKey,
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!int.TryParse(
                        remainderTail.Substring(materialMarkerAt + 2),
                        out var materialIndex))
                    throw new InvalidOperationException(
                        $"Could not parse original material slot from " +
                        $"'{remainderRenderer.name}'.");

                if (materialIndex < 0 || materialIndex >= sourceMaterials.Length)
                    throw new InvalidOperationException(
                        $"Original material slot {materialIndex} is out of range " +
                        $"for source renderer '{sourceRenderer.name}' " +
                        $"materials={sourceMaterials.Length}.");

                remainderRenderer.transform.SetParent(visual, true);
                remainderRenderer.gameObject.name =
                    "VolkswagenAmarok_" + remainderRenderer.gameObject.name;
                remainderRenderer.sharedMaterial = sourceMaterials[materialIndex];
                remainderRenderer.enabled = true;
                matchedRemainders++;
                adoptedRemainders++;
            }

            // A fully-blue source object legitimately has zero remainder
            // renderers. Partial objects get one or more VehicleOriginal_* pieces
            // from Blender; in both cases the old unsplit source is disabled.
            if (matchedRemainders == 0)
            {
                Debug.Log(
                    $"VolkswagenAmarok paint source '{sourceKey}' is fully paint " +
                    "and has no original remainder.");
            }

            // The Blender export is complementary: VehiclePaint_Blue contains all
            // texture-detected blue faces and VehicleOriginal_* contains every
            // remaining face. Disable the old unsplit source renderer instead of
            // trying to edit/triangulate its Unity mesh.
            sourceRenderer.enabled = false;

            paintRenderer.transform.SetParent(visual, true);
            paintRenderer.gameObject.name =
                "VolkswagenAmarok_" + paintRenderer.gameObject.name;
            paintRenderer.sharedMaterial = paintMaterial;
            paintRenderer.enabled = true;
            replacedSources++;
        }

        if (replacedSources != paintRenderers.Count)
            throw new InvalidOperationException(
                $"Amarok complementary paint replacement incomplete: " +
                $"sources={replacedSources}/{paintRenderers.Count}.");

        EditorUtility.SetDirty(paintMaterial);
        Debug.Log(
            $"VolkswagenAmarok complementary paint replacement ready " +
            $"paintPanels={paintRenderers.Count}, remainderRenderers={adoptedRemainders}, " +
            $"disabledOriginalRenderers={replacedSources}.");
    }

    private static Material[] ResolveAmarokSourceMaterials(string sourceKey)
    {
        var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (sourceModel == null)
            throw new InvalidOperationException(
                $"Could not load Amarok source model at '{ModelPath}'.");

        foreach (var renderer in sourceModel.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (renderer == null)
                continue;
            if (!string.Equals(
                    SanitizeAmarokSourceName(renderer.name),
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var materials = renderer.sharedMaterials;
            if (materials.Length == 0)
                throw new InvalidOperationException(
                    $"Amarok source model renderer '{renderer.name}' has no materials.");
            return materials;
        }

        throw new InvalidOperationException(
            $"Could not resolve Amarok source materials for key '{sourceKey}' from ModelPath.");
    }

    private static string SanitizeAmarokSourceName(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (char.IsLetterOrDigit(chars[index]) || chars[index] == '_')
                continue;
            chars[index] = '_';
        }
        return new string(chars).Trim('_');
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
                    ((IsBodyPaintMaterial(material) && !IsFactoryBlackExteriorPart(renderer)) ||
                     IsCaliperMaterial(material) || IsInteriorAccentPaintMaterial(material)))
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
        var bodyNode = FindTransform(modelInstance.transform, "vw_amorak_2018:body_phong5_0") ??
                       FindTransformWithNameFragment(modelInstance.transform, "body_phong5_0") ??
                       throw new InvalidOperationException("Amarok outer body node 'body_phong5_0' is missing.");
        var sourceRenderer = bodyNode.GetComponent<MeshRenderer>();
        var sourceFilter = bodyNode.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The Amarok outer body mesh was not found.");

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        bakedMesh.name = "AmarokDamageBody";
        var sourceToRoot = root.transform.worldToLocalMatrix * sourceFilter.transform.localToWorldMatrix;

        var vertices = bakedMesh.vertices;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = sourceToRoot.MultiplyPoint3x4(vertices[index]);
        bakedMesh.vertices = vertices;

        var normals = bakedMesh.normals;
        if (normals.Length == vertices.Length)
        {
            var normalMatrix = sourceToRoot.inverse.transpose;
            for (var index = 0; index < normals.Length; index++)
                normals[index] = normalMatrix.MultiplyVector(normals[index]).normalized;
            bakedMesh.normals = normals;
        }

        var tangents = bakedMesh.tangents;
        if (tangents.Length == vertices.Length)
        {
            for (var index = 0; index < tangents.Length; index++)
            {
                var tangent = tangents[index];
                var direction = sourceToRoot.MultiplyVector(
                    new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                tangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
            }
            bakedMesh.tangents = tangents;
        }
        bakedMesh.RecalculateBounds();
        bakedMesh.UploadMeshData(false);

        var persistentMesh = AssetDatabase.LoadAssetAtPath<Mesh>(DamageBodyMeshPath);
        if (persistentMesh == null)
        {
            AssetDatabase.CreateAsset(bakedMesh, DamageBodyMeshPath);
            persistentMesh = bakedMesh;
        }
        else
        {
            EditorUtility.CopySerialized(bakedMesh, persistentMesh);
            UnityEngine.Object.DestroyImmediate(bakedMesh);
            EditorUtility.SetDirty(persistentMesh);
        }

        var damageBody = new GameObject("AmarokDamageBody")
        {
            layer = sourceRenderer.gameObject.layer,
        };
        damageBody.transform.SetParent(root.transform, false);
        var damageFilter = damageBody.AddComponent<MeshFilter>();
        damageFilter.sharedMesh = persistentMesh;
        var damageRenderer = damageBody.AddComponent<MeshRenderer>();
        damageRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        damageRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        damageRenderer.receiveShadows = sourceRenderer.receiveShadows;
        damageRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        damageRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        damageRenderer.motionVectorGenerationMode = sourceRenderer.motionVectorGenerationMode;
        damageRenderer.allowOcclusionWhenDynamic = sourceRenderer.allowOcclusionWhenDynamic;
        damageRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;

        sourceRenderer.enabled = false;
        sourceRenderer.sharedMaterials = Array.Empty<Material>();
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

    private static void ConfigurePickupDamageHandler(GameObject root)
    {
        var found = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null ||
                !string.Equals(component.GetType().Name, "DamageHandler", StringComparison.Ordinal))
                continue;

            var serialized = new SerializedObject(component);
            SetNumber(serialized, "damageIntensity", 0.76f);
            SetNumber(serialized, "decelerationThreshold", 650f);
            SetNumber(serialized, "deformationRadius", DeformationRadius);
            SetNumber(serialized, "deformationRandomness", DeformationRandomness);
            SetNumber(serialized, "deformationStrength", DeformationStrength);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            found = true;
        }
        if (!found)
            throw new InvalidOperationException("Reference DamageHandler is missing.");
    }

    private static bool IsFactoryBlackExteriorPart(Renderer renderer)
    {
        for (var current = renderer.transform; current != null; current = current.parent)
        {
            var name = current.name;
            if (name.IndexOf("mudflap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("mud_flap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("splash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("runningboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("running_board", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("sidestep", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("side_step", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("footboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("foot_board", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("step_pad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("tread", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        var visual = FindTransform(renderer.transform.root, "AmarokVisual");
        if (visual == null || !TryGetMeshBoundsInSpace(renderer, visual, out var localBounds))
            return false;

        var center = localBounds.center;
        var size = localBounds.size;

        // Never classify the complete baked body shell as a black accessory.
        if (size.x > 1.60f && size.z > 3.0f)
            return false;

        // Separate thin guards behind the wheels.
        var splashGuard =
            Mathf.Abs(center.x) > 0.62f &&
            Mathf.Abs(center.z) > 0.72f &&
            center.y < 0.72f &&
            size.x < 0.80f &&
            size.y < 1.05f &&
            size.z < 0.85f;

        // Rubber tread pads sitting on the long side tubes. The metal tube uses
        // another material slot and is intentionally not recolored here.
        var sideTread =
            Mathf.Abs(center.x) > 0.58f &&
            Mathf.Abs(center.z) < 1.80f &&
            center.y < 0.68f &&
            size.x < 0.85f &&
            size.y < 0.45f &&
            size.z < 2.90f;

        // Black tread surface above the rear chrome bumper.
        var rearTread =
            center.z < -1.72f &&
            center.y < 0.78f &&
            size.x < 2.35f &&
            size.y < 0.50f &&
            size.z < 1.05f;

        return splashGuard || sideTread || rearTread;
    }

    private static bool TryGetMeshBoundsInSpace(
        Renderer renderer,
        Transform space,
        out Bounds bounds)
    {
        bounds = default;
        var filter = renderer.GetComponent<MeshFilter>();
        var mesh = filter?.sharedMesh;
        if (mesh == null)
            return false;

        var source = mesh.bounds;
        var extents = source.extents;
        var initialized = false;
        for (var x = -1; x <= 1; x += 2)
        for (var y = -1; y <= 1; y += 2)
        for (var z = -1; z <= 1; z += 2)
        {
            var local = source.center + Vector3.Scale(
                extents, new Vector3(x, y, z));
            var point = space.InverseTransformPoint(
                renderer.transform.TransformPoint(local));
            if (!initialized)
            {
                bounds = new Bounds(point, Vector3.zero);
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(point);
            }
        }
        return initialized;
    }

    private static bool IsBodyPaintMaterial(Material material) =>
        material.name.IndexOf("_BA_VehiclePaint", StringComparison.OrdinalIgnoreCase) >= 0;

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
        false;

    private static VolkswagenAmarokMaterialFixResult PreparePrefabMaterialsWithoutRuntimeClones(GameObject root)
    {
        // Do NOT call VolkswagenAmarokMaterials.FixSolidMaterials() while authoring
        // the prefab. That method is a runtime per-instance material-cloning path.
        // Its Instantiate(Material) results are not persistent assets, so assigning
        // them to renderers before SaveAsPrefabAsset() serializes the model's material
        // references as null. Keep the manifest-generated material assets on the
        // prefab and only ensure the runtime controller component is present.
        if (root.GetComponent<VolkswagenAmarokMaterialController>() == null)
            root.AddComponent<VolkswagenAmarokMaterialController>();

        var rendererCount = 0;
        var opaqueMaterials = 0;
        var transparentMaterials = 0;
        var uniqueMaterials = new HashSet<Material>();
        var nullSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            rendererCount++;
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                {
                    nullSlots++;
                    continue;
                }
                if (!uniqueMaterials.Add(material))
                    continue;
                if (VolkswagenAmarokMaterials.IsTransparentMaterial(material))
                    transparentMaterials++;
                else
                    opaqueMaterials++;
            }
        }

        if (nullSlots != 0)
            throw new InvalidOperationException(
                $"Amarok prefab contains {nullSlots} null material slot(s) before save; " +
                "runtime material cloning must not be used during prefab authoring.");

        Debug.Log(
            $"VolkswagenAmarok: preserving persistent prefab materials " +
            $"unique={uniqueMaterials.Count}, opaque={opaqueMaterials}, " +
            $"transparent={transparentMaterials}, nullSlots={nullSlots}; " +
            "runtime cloning deferred until vehicle initialization.");

        return new VolkswagenAmarokMaterialFixResult(
            rendererCount,
            0,
            opaqueMaterials,
            transparentMaterials,
            0,
            0,
            0,
            0);
    }

    private static int ConfigureRimFinish(GameObject root) => 0;

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", VolkswagenAmarokMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", VolkswagenAmarokMaterials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        false;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool GT3RSPowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateGT3RSPowerCurve();
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

    [Serializable]
    private sealed class AmarokMaterialManifest
    {
        public AmarokMaterialSpec[] materials = Array.Empty<AmarokMaterialSpec>();
        public AmarokMeshMaterialBinding[] meshes = Array.Empty<AmarokMeshMaterialBinding>();
    }

    [Serializable]
    private sealed class AmarokMaterialSpec
    {
        public int index;
        public string name = string.Empty;
        public string alphaMode = "OPAQUE";
        public bool doubleSided;
        public float[] baseColorFactor = Array.Empty<float>();
        public float metallicFactor = 1f;
        public float roughnessFactor = 1f;
        public string baseColorTexturePath = string.Empty;
    }

    [Serializable]
    private sealed class AmarokMeshMaterialBinding
    {
        public string rendererName = string.Empty;
        public string materialName = string.Empty;
    }

    private static void AssignAmarokMaterialsFromManifest(GameObject model)
    {
        var manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(MaterialManifestPath) ??
                            throw new InvalidOperationException(
                                "Amarok material manifest is missing. Run tools/repair_volkswagen_amarok_material_assignments.py first.");
        var manifest = JsonUtility.FromJson<AmarokMaterialManifest>(manifestAsset.text) ??
                       throw new InvalidOperationException("Amarok material manifest could not be parsed.");
        if (manifest.materials.Length != 22 || manifest.meshes.Length != 83)
            throw new InvalidOperationException(
                $"Amarok material manifest is incomplete materials={manifest.materials.Length}/22 meshes={manifest.meshes.Length}/83.");

        EnsureAssetFolder(MaterialFolder);
        var generated = new Dictionary<string, Material>(StringComparer.Ordinal);
        foreach (var spec in manifest.materials)
        {
            if (string.IsNullOrEmpty(spec.name))
                throw new InvalidOperationException("Amarok material manifest contains an unnamed material.");

            var assetName = $"AmarokSource_{spec.index:D2}_{SanitizeAssetName(spec.name)}";
            var assetPath = $"{MaterialFolder}/{assetName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("High Definition Render Pipeline/Lit") ??
                             throw new InvalidOperationException("HDRP/Lit shader is unavailable in the SDK project.");
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else
            {
                material.name = assetName;
            }

            // The runtime helper performs the SDK's known-good HDRP state setup.
            // Restore the actual glTF material data afterwards so the conversion
            // does not replace the source color/texture with generic defaults.
            VolkswagenAmarokMaterials.NormalizeImportedMaterial(material);

            var factor = spec.baseColorFactor != null && spec.baseColorFactor.Length >= 4
                ? new Color(spec.baseColorFactor[0], spec.baseColorFactor[1], spec.baseColorFactor[2], spec.baseColorFactor[3])
                : Color.white;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", factor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", factor);
            if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", factor);

            Texture2D? baseTexture = null;
            if (!string.IsNullOrEmpty(spec.baseColorTexturePath))
            {
                baseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.baseColorTexturePath) ??
                              throw new InvalidOperationException(
                                  $"Amarok texture '{spec.baseColorTexturePath}' for material '{spec.name}' is missing.");
            }
            if (material.HasProperty("_BaseColorMap")) material.SetTexture("_BaseColorMap", baseTexture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", baseTexture);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", spec.metallicFactor);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(spec.roughnessFactor));

            if (spec.doubleSided)
            {
                if (material.HasProperty("_DoubleSidedEnable")) material.SetFloat("_DoubleSidedEnable", 1f);
                if (material.HasProperty("_Cull")) material.SetFloat("_Cull", (float)CullMode.Off);
                if (material.HasProperty("_CullMode")) material.SetFloat("_CullMode", (float)CullMode.Off);
                if (material.HasProperty("_CullModeForward")) material.SetFloat("_CullModeForward", (float)CullMode.Off);
                if (material.HasProperty("_TransparentCullMode")) material.SetFloat("_TransparentCullMode", (float)CullMode.Off);
                material.EnableKeyword("_DOUBLESIDED_ON");
            }

            EditorUtility.SetDirty(material);
            generated[spec.name] = material;
        }

        var bindingByRenderer = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var binding in manifest.meshes)
        {
            if (string.IsNullOrEmpty(binding.rendererName) || string.IsNullOrEmpty(binding.materialName))
                throw new InvalidOperationException("Amarok material manifest contains an incomplete renderer binding.");
            bindingByRenderer[binding.rendererName] = binding.materialName;
        }

        var assigned = 0;
        var unmapped = new List<string>();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter?.sharedMesh == null)
                continue;
            if (!bindingByRenderer.TryGetValue(renderer.name, out var materialName) ||
                !generated.TryGetValue(materialName, out var material))
            {
                unmapped.Add(renderer.name);
                continue;
            }
            renderer.sharedMaterials = new[] { material };
            assigned++;
        }

        if (unmapped.Count != 0 || assigned != manifest.meshes.Length)
            throw new InvalidOperationException(
                $"Amarok material assignment incomplete assigned={assigned}/{manifest.meshes.Length}; " +
                $"unmapped={string.Join(", ", unmapped.GetRange(0, Math.Min(8, unmapped.Count)))}");

        var nullSlots = 0;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material == null) nullSlots++;
        if (nullSlots != 0)
            throw new InvalidOperationException(
                $"Amarok model still contains {nullSlots} null material slot(s) immediately after manifest assignment.");

        Debug.Log(
            $"VolkswagenAmarok: assigned source materials from GLB manifest " +
            $"renderers={assigned}/83 materials={generated.Count}/22 nullSlots={nullSlots}.");
    }

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
                    var transparent = VolkswagenAmarokMaterials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"Amarok{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

        manifest.ModId = "VolkswagenAmarok";
        manifest.DisplayName = "Volkswagen Amarok";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "volkswagenamarok.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Amarok manifest references could not be assigned.");
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
        var found = false; bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var current = renderer.transform; var wheel = false;
            while (current != null && current != root)
            {
                var n=current.name;
                if (n == "AmarokLightSources" || n == "vw_amorak_2018:wheel" || n == "wheel" || n == "wheel1" || n == "wheel2" || n.StartsWith("AmarokWheel", StringComparison.Ordinal)) { wheel=true; break; }
                current=current.parent;
            }
            if (wheel) continue;
            if (!found) { bounds=renderer.bounds; found=true; } else bounds.Encapsulate(renderer.bounds);
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

    private static bool TryGetAmarokRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!VolkswagenAmarokMaterials.IsAmarokRenderer(renderer.transform))
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
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '-' && chars[index] != '_')
                chars[index] = '_';
        }

        return new string(chars);
    }
}

