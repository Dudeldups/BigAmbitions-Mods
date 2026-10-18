#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class DodgeRam1500TRXSetup
{
    private const string ModRoot = "Assets/Mods/Dodge_Ram_1500";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/2021_ram_1500_trx.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/DodgeRam1500TRXDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/DodgeRam1500TRX.asset";
    private const string VehiclePrefabPath = ModRoot + "/DodgeRam1500TRX.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/DodgeRam1500TRX.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/dodgeram1500trx.unity3d";
    private const string MacBundlePath =
        ModRoot + "/AssetBundles/Mac/dodgeram1500trx.unity3d";
    private const string VehicleTypeName =
        "dodgeram1500trx-vehicle:vehicletype_dodgeram1500trx";
    private const float TargetLength = 5.916f;
    private const float TargetWidth = 2.510f;
    private const float TargetHeight = 2.055f;
    private const float FrontTrack = 1.8923f;
    private const float RearTrack = 1.8821f;
    private const float Wheelbase = 3.6855f;
    private const float FrontTireRadius = 0.4445f;
    private const float RearTireRadius = 0.4445f;
    private const float FrontTireWidth = 0.325f;
    private const float RearTireWidth = 0.325f;
    private const float WheelInset = 0f;
    private const float WheelCenterRideHeightOffset = -0.015f;
    private const float FrontWheelForwardOffset = 0.095f;
    private const float RearWheelForwardOffset = 0.110f;
    private const float VehicleLinearDrag = 0f;
    private const float VehicleBrakeForce = DodgeRam1500TRXPowertrainProfile.BrakeTorque;
    private const float BrakeMaxTorque = DodgeRam1500TRXPowertrainProfile.BrakeTorque;
    private const float FrontForwardGrip = 1.00f;
    private const float RearForwardGrip = 1.00f;
    private const float FrontForwardStiffness = 1.08f;
    private const float RearForwardStiffness = 1.08f;
    private const float TireFrictionCircleStrength = 1.02f;
    private const float AntiRollBarForce = 12000f;
    private const float FrontSuspensionTravel = 0.150f;
    private const float RearSuspensionTravel = 0.180f;
    private const float DeformationStrength = 0.15f;
    private const float DeformationRadius = 0.34f;
    private const float DeformationRandomness = 0.005f;
    // Truck-sized mass distribution, estimated from the chassis footprint (kg m^2).
    private static readonly Vector3 StableInertiaTensor = new Vector3(8000f, 8400f, 3200f);
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.35f, -0.05f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f + WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, Wheelbase * 0.5f + FrontWheelForwardOffset) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f - WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, Wheelbase * 0.5f + FrontWheelForwardOffset) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f + WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f + RearWheelForwardOffset) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f - WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f + RearWheelForwardOffset) },
        };
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.44f, 2.50f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(2.10f, 0.62f, 0.78f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.44f, -2.50f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(2.10f, 0.62f, 0.78f);

    private static readonly float[] TRXGears =
    {
        -3.30f,
        0f,
        4.71f,
        3.14f,
        2.11f,
        1.67f,
        1.29f,
        1.00f,
        0.84f,
        0.67f,
    };

    private static AnimationCurve CreateTRXPowerCurve() => DodgeRam1500TRXPowertrainProfile.CreatePowerCurve();

    [MenuItem("Big Ambitions Mods/Setup Dodge Ram 1500 TRX")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        ValidateThumbnail();
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "DodgeRam1500TRX setup complete: generated the 2021 TRX with four " +
            "independent 35-inch wheel assemblies, 8-speed automatic AWD, supercharged V8 audio, " +
            "state-controlled lamp geometry, repaint support, and corrected glass materials.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    // Runtime-only tuning retains existing prefab file IDs and authored geometry.
    public static void BuildExistingAndVerify()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ValidateThumbnail();
        DodgeRam1500TRXCrashSoundSetup.VerifyBindings(AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabPath));
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    public static void RepairPrefabAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    public static void VerifyBuiltBundle()
    {
        VerifyPlatformBundles();
        AssertNoExampleDependencies(VehicleAssetPath);
        AssertNoExampleDependencies(VehiclePrefabPath);
        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");
        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath) ??
                              throw new InvalidOperationException("Bundle is missing its VehicleType.");
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath) ??
                         throw new InvalidOperationException("Bundle is missing its vehicle prefab.");
            var type = new SerializedObject(vehicleType);
            var typeValid = string.Equals(type.FindProperty("vehicleTypeName")?.stringValue,
                               VehicleTypeName, StringComparison.Ordinal) &&
                Math.Abs(ReadNumber(type.FindProperty("price")) - 69995f) < 0.5f &&
                Math.Abs(ReadNumber(type.FindProperty("maxFuel")) - 125f) < 0.5f &&
                Math.Abs(ReadNumber(type.FindProperty("maxCargoCapacity")) - 24f) < 0.5f &&
                Math.Abs(ReadNumber(type.FindProperty("maxSpeed")) - 190f) < 0.5f &&
                Math.Abs(ReadNumber(type.FindProperty("enginePower")) - 523f) < 0.5f &&
                (type.FindProperty("isLuxuryCar")?.boolValue ?? false);
            if (!TryGetDodgeRam1500TRXRendererBounds(prefab.transform, out var bounds))
                throw new InvalidOperationException("TRX prefab has no renderer bounds.");
            var dimensionsValid = Math.Abs(bounds.size.z - TargetLength) < 0.04f &&
                                  Math.Abs(bounds.size.x - TargetWidth) < 0.055f &&
                                  Math.Abs(bounds.size.y - (TargetHeight - 0.08f - DodgeRam1500TRXBodyFit.AdditionalRearDrop)) < 0.04f;
            var visual = FindTransform(prefab.transform, "DodgeRam1500TRXVisual");
            var wheelCount = 0;
            var wheelsValid = true;
            var caliperCount = 0;
            var rotorCount = 0;
            var glassCount = 0;
            var paintCount = 0;
            var lightGeometry = false;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.StartsWith("DodgeRam1500TRXWheel", StringComparison.Ordinal))
                {
                    wheelCount++;
                    wheelsValid &= transform.GetComponentInChildren<MeshRenderer>(true) != null;
                }
                if (transform.name.StartsWith("DodgeRam1500TRXFixedCaliper", StringComparison.Ordinal) &&
                    transform.GetComponentInChildren<MeshRenderer>(true) != null)
                    caliperCount++;
                if (transform.name.StartsWith("DodgeRam1500TRXBrakeRotor", StringComparison.Ordinal) &&
                    transform.GetComponentInChildren<MeshRenderer>(true) != null)
                    rotorCount++;
            }
            if (visual != null)
            {
                foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.sharedMaterials.Length == 0)
                        continue;
                    if (IsCabinGlassRenderer(renderer.transform)) glassCount++;
                    if (HasAncestorNameFragment(renderer.transform, "Light_Geo")) lightGeometry = true;
                }
            }
            var vehicleBody = prefab.GetComponent<Rigidbody>();
            var massValid = vehicleBody != null && Math.Abs(vehicleBody.mass - 2880f) < 0.5f;
            var transmissionValid = false;
            var awdValid = false;
            var engineValid = false;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component.GetType().FullName != "NWH.VehiclePhysics2.VehicleController")
                    continue;
                var nwh = new SerializedObject(component);
                var powertrain = nwh.FindProperty("powertrain");
                var transmission = powertrain?.FindPropertyRelative("transmission");
                var gears = transmission?.FindPropertyRelative("gears");
                transmissionValid = transmission?.FindPropertyRelative("forwardGearCount")?.intValue == 8 &&
                                    gears != null && gears.isArray && gears.arraySize == TRXGears.Length;
                if (transmissionValid)
                    for (var index = 0; index < TRXGears.Length; index++)
                        transmissionValid &= Math.Abs(gears!.GetArrayElementAtIndex(index).floatValue -
                                                      TRXGears[index]) < 0.01f;
                var differentials = powertrain?.FindPropertyRelative("differentials");
                if (differentials != null && differentials.isArray)
                    for (var index = 0; index < differentials.arraySize; index++)
                    {
                        var differential = differentials.GetArrayElementAtIndex(index);
                        if (differential.FindPropertyRelative("name")?.stringValue == "Center Differential")
                            awdValid = Math.Abs(ReadNumber(differential.FindPropertyRelative("biasAB")) - 0.4f) < 0.005f;
                    }
                var engine = powertrain?.FindPropertyRelative("engine");
                var forced = engine?.FindPropertyRelative("forcedInduction")?.FindPropertyRelative("useForcedInduction");
                engineValid = Math.Abs(ReadNumber(engine?.FindPropertyRelative("maxPower")) - 523f) < 0.5f &&
                              Math.Abs(ReadNumber(engine?.FindPropertyRelative("idleRPM")) - 700f) < 0.5f &&
                              Math.Abs(ReadNumber(engine?.FindPropertyRelative("revLimiterRPM")) - 6200f) < 0.5f &&
                              (forced?.boolValue ?? false);
                break;
            }
            var damage = FindTransform(prefab.transform, "DodgeRam1500TRXDamageBody")?.GetComponent<MeshFilter>();
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
                if (HasMaterialMarker(renderer, "RAM_1500TRX_2021Paint_Material")) paintCount++;
            var templateBeam = false;
            foreach (var light in prefab.GetComponentsInChildren<Light>(true))
                templateBeam |= light.name == "Spotlights";
            var componentsValid = true;
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                componentsValid &= component != null;
            if (!typeValid || !dimensionsValid || wheelCount != 4 || !wheelsValid ||
                caliperCount != 4 || rotorCount != 4 || !massValid || !transmissionValid ||
                !awdValid || !engineValid || damage?.sharedMesh == null || !damage.sharedMesh.isReadable ||
                glassCount == 0 || paintCount == 0 || !lightGeometry || !templateBeam || !componentsValid)
            {
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (!DodgeRam1500TRXMaterials.IsDodgeRam1500TRXRenderer(renderer.transform))
                        continue;
                    Debug.LogError(
                        $"DodgeRam1500TRX renderer bounds: {GetHierarchyPath(renderer.transform)} " +
                        $"enabled={renderer.enabled}, min={renderer.bounds.min}, max={renderer.bounds.max}, " +
                        $"materials={string.Join(",", Array.ConvertAll(renderer.sharedMaterials, m => m == null ? "<null>" : m.name))}.");
                }
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                {
                    foreach (var component in transform.GetComponents<Component>())
                    {
                        if (component == null)
                            Debug.LogError($"DodgeRam1500TRX missing component on {GetHierarchyPath(transform)}.");
                    }
                }
                throw new InvalidOperationException(
                    $"TRX verification failed: type={typeValid}, dimensions={bounds.size}, " +
                    $"dimensionsValid={dimensionsValid}, wheels={wheelCount}/{wheelsValid}, " +
                    $"calipers={caliperCount}, rotors={rotorCount}, mass={massValid}, " +
                    $"8HP95={transmissionValid}, AWD40/60={awdValid}, engine={engineValid}, " +
                    $"damage={damage?.sharedMesh != null}, glass={glassCount}, paint={paintCount}, " +
                    $"lightGeometry={lightGeometry}, beamTemplate={templateBeam}, components={componentsValid}.");
            }
            DodgeRam1500TRXCrashSoundSetup.VerifyBindings(prefab, bundle);
            DodgeRam1500TRXReview.Validate(prefab);
            Debug.Log(
                $"DodgeRam1500TRX bundle verified: 702hp HEMI, mass=2880kg, speed=190kph, " +
                $"fuel=125L, cargo=24, dimensions={bounds.size}, 8HP95 AWD=40/60, " +
                $"wheels=4x35in, brake assemblies=4, damage=ready, paint=ready, lights=ready, glass={glassCount}.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static void VerifyPlatformBundles()
    {
        var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        var required = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;
        if (manifest == null || (manifest.TargetPlatforms & required) != required)
            throw new InvalidOperationException("TRX manifest must build Windows and Mac bundles.");
        foreach (var platform in new[] { "Windows", "Mac" })
        {
            var path = platform == "Windows" ? WindowsBundlePath : MacBundlePath;
            if (!File.Exists(path) || !File.Exists(path + ".manifest"))
                throw new InvalidOperationException("TRX platform bundle is missing: " + platform);
            if (!BuildPipeline.GetCRCForAssetBundle(path, out var crc) ||
                !File.ReadAllText(path + ".manifest").Contains("CRC: " + crc))
                throw new InvalidOperationException("TRX " + platform + " bundle CRC differs from its manifest.");
            var sidecar = File.ReadAllText(path + ".manifest");
            for (var index = 1; index <= 7; index++)
                if (sidecar.IndexOf(ModRoot + $"/Config/Audio/Crash{index:D2}.wav", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException($"TRX {platform} bundle omits owned crash clip {index}.");
            Debug.Log("TRX " + platform + " bundle manifest verified with seven crash clips; CRC=" + crc + ".");
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
                throw new InvalidOperationException("Could not create the DodgeRam1500TRX VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated DodgeRam1500TRX VehicleType asset did not load.");

        target.name = "DodgeRam1500TRX";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 69995f);
        SetNumber(serialized, "maxFuel", 125f);
        SetNumber(serialized, "maxCargoCapacity", 24f);
        SetNumber(serialized, "maxSpeed", 190f);
        SetNumber(serialized, "enginePower", 523f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", DodgeRam1500TRXSteeringProfile.MaximumSteerAngle);
        SetNumber(serialized, "damageIntensity", 0.45f);
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
            throw new InvalidOperationException("DodgeRam1500TRX VehicleType asset was not found.");

        var serialized = new SerializedObject(vehicleType);
        SetNumber(serialized, "maxSpeed", 190f);
        SetNumber(serialized, "enginePower", 523f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(vehicleType);
    }

    private static void CreateVehiclePrefab(UnityEngine.Object vehicleType)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("Vehicle template prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("2021 Ram 1500 TRX GLB did not import as a prefab.");

        var root = PrefabUtility.InstantiatePrefab(source) as GameObject;
        if (root == null)
            throw new InvalidOperationException("Could not instantiate the Audi authoring seed as a prefab instance.");
        root.name = "DodgeRam1500TRX";
        try
        {
            PrefabUtility.UnpackPrefabInstance(
                root,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                throw new InvalidOperationException("Vehicle donor prefab instance was not completely unpacked.");
            StripAudiGeometry(root);
            RenameDonorObjects(root);
            RemoveAudiSpecificBehaviours(root);
            ClearDonorObjectReferences(root);
            var removedMissingScripts = RemoveMissingScripts(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);
            DodgeRam1500TRXCrashSoundSetup.Author(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the DodgeRam1500TRX model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "DodgeRam1500TRXVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureExitMarkers(root, modelInstance);
            AssignPersistentMaterials(modelInstance);
            AttachWheelVisuals(root, modelInstance);
            DodgeRam1500TRXDetailAuthoring.Configure(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            // Requested cumulative body correction: -11 cm. Apply after source-space selections.
            modelInstance.transform.position += root.transform.TransformVector(new Vector3(0f, -.11f, 0f));
            damageBody.transform.localPosition += new Vector3(0f, -.11f, 0f);
            var frontPivot = root.transform.TransformPoint(DodgeRam1500TRXBodyFit.FrontPivot);
            modelInstance.transform.RotateAround(frontPivot, root.transform.right, DodgeRam1500TRXBodyFit.RakeDegrees);
            damageBody.transform.RotateAround(frontPivot, root.transform.right, DodgeRam1500TRXBodyFit.RakeDegrees);
            Debug.Log($"TRX rear body rake: rearDrop={DodgeRam1500TRXBodyFit.AdditionalRearDrop:F3}m, pitch={DodgeRam1500TRXBodyFit.RakeDegrees:F4}deg, front axle fixed.");
            var materialController = root.GetComponent<DodgeRam1500TRXMaterialController>() ??
                                     root.AddComponent<DodgeRam1500TRXMaterialController>();
            materialController.hideFlags = HideFlags.None;
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);
            removedMissingScripts += RemoveMissingScripts(root);

            Debug.Log(
                "DodgeRam1500TRX: persisted authored materials; deferred per-instance glass setup " +
                "to the vehicle runtime; " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, " +
                $"missingScriptsRemoved={removedMissingScripts}.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the DodgeRam1500TRX vehicle prefab.");
            AssertNoExampleDependencies(VehiclePrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        CleanSavedPrefabMissingScripts();
    }

    private static void CleanSavedPrefabMissingScripts()
    {
        var contents = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);
        try
        {
            var removed = RemoveMissingScripts(contents);
            var remaining = CountNullComponents(contents);
            if (remaining != 0)
                throw new InvalidOperationException(
                    $"Saved DodgeRam1500TRX prefab still contains {remaining} missing components.");
            if (removed > 0)
            {
                if (PrefabUtility.SaveAsPrefabAsset(contents, VehiclePrefabPath) == null)
                    throw new InvalidOperationException("Could not save the cleaned DodgeRam1500TRX prefab.");
                Debug.Log($"DodgeRam1500TRX: removed {removed} missing scripts from the serialized prefab.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }

        AssertNoExampleDependencies(VehiclePrefabPath);
    }

    private static void ValidateThumbnail()
    {
        const string thumbnailPath = ModRoot + "/thumbnail.png";
        if (!File.Exists(thumbnailPath))
            throw new InvalidOperationException(
                "DodgeRam1500TRX thumbnail is missing; render it with tools~/render_trx_thumbnail.py.");

        AssetDatabase.ImportAsset(thumbnailPath, ImportAssetOptions.ForceSynchronousImport);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(thumbnailPath), false))
                throw new InvalidOperationException("DodgeRam1500TRX thumbnail PNG could not be decoded.");
            if (texture.width < 512 || texture.height < 288)
                throw new InvalidOperationException(
                    $"DodgeRam1500TRX thumbnail is too small ({texture.width}x{texture.height}).");
            var renderedPixels = CountThumbnailGeometryPixels(texture);
            if (renderedPixels < 250)
                throw new InvalidOperationException(
                    $"DodgeRam1500TRX thumbnail is blank; non-background samples={renderedPixels}.");
            Debug.Log(
                $"DodgeRam1500TRX thumbnail verified {texture.width}x{texture.height}, " +
                $"non-background samples={renderedPixels}.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static int CountThumbnailGeometryPixels(Texture2D texture)
    {
        var background = texture.GetPixel(0, 0);
        var visibleSamples = 0;
        for (var y = 0; y < texture.height; y += 3)
        for (var x = 0; x < texture.width; x += 3)
        {
            var pixel = texture.GetPixel(x, y);
            var red = pixel.r - background.r;
            var green = pixel.g - background.g;
            var blue = pixel.b - background.b;
            if (red * red + green * green + blue * blue > 0.0025f)
                visibleSamples++;
        }

        return visibleSamples;
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

    private static void RenameDonorObjects(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.IndexOf("audi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                transform.name.IndexOf("rs6", StringComparison.OrdinalIgnoreCase) >= 0)
                transform.name = "DodgeRam1500TRXTemplateVisual";
        }
    }

    private static void RemoveAudiSpecificBehaviours(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null &&
                (component.GetType().Name.StartsWith("AudiRS6R", StringComparison.Ordinal) ||
                 IsExampleAsset(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(component)))))
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }
    }


    private static void ClearDonorObjectReferences(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            var changed = false;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    property.objectReferenceValue == null)
                    continue;
                var assetPath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                if (!IsExampleAsset(assetPath))
                    continue;
                property.objectReferenceValue = null;
                changed = true;
            }
            if (changed)
                serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static bool IsExampleAsset(string path) =>
        path.StartsWith("Assets/Mods/AudiRS6R/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("Assets/Mods/BMW_M4_G82/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("Assets/Mods/Porsche_911_GT3_RS/", StringComparison.OrdinalIgnoreCase);

    private static void AssertNoExampleDependencies(string assetPath)
    {
        foreach (var dependency in AssetDatabase.GetDependencies(assetPath, true))
        {
            if (IsExampleAsset(dependency))
                throw new InvalidOperationException(
                    $"Dodge asset '{assetPath}' still depends on donor asset '{dependency}'.");
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
                   throw new InvalidOperationException("Vehicle template has no Rigidbody.");
        body.mass = 2880f;
        body.drag = VehicleLinearDrag;
        body.angularDrag = 1.45f;
        body.centerOfMass = StableCenterOfMass;
        body.inertiaTensorRotation = Quaternion.identity;
        body.inertiaTensor = StableInertiaTensor;
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
            var useDefaultInertia = serialized.FindProperty("useDefaultInertia");
            if (useDefaultInertia != null) useDefaultInertia.boolValue = false;
            foreach (var field in new[] { "inertiaTensor", "combinedInertiaTensor" })
            {
                var tensor = serialized.FindProperty(field);
                if (tensor?.propertyType == SerializedPropertyType.Vector3) tensor.vector3Value = StableInertiaTensor;
            }
            var dimensions = serialized.FindProperty("dimensions");
            if (dimensions?.propertyType == SerializedPropertyType.Vector3) dimensions.vector3Value = new Vector3(2.08f, 1.6f, 5.48f);
            SetNumber(serialized, "baseMass", 2880f);
            SetNumber(serialized, "combinedMass", 2880f);
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
                SetRelativeNumber(serialized, "spring.maxForce", 32000f);
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
        var colliders = holder.GetComponentsInChildren<BoxCollider>(true);
        if (colliders.Length < 2)
            throw new InvalidOperationException("Reference vehicle requires two body colliders.");

        colliders[0].center = new Vector3(0f, 0.35f, 0f);
        colliders[0].size = new Vector3(2.10f, 0.52f, 5.80f);
        colliders[1].center = new Vector3(0f, 1.01f, -0.05f);
        colliders[1].size = new Vector3(1.94f, 1.20f, 2.72f);
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

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        // The TRX is left-hand drive; these markers clear the front crew-cab doors.
        SetLocalPosition(root, "Driverside", new Vector3(-1.65f, 0.17f, 0.55f));
        SetLocalPosition(root, "Passengerside", new Vector3(1.65f, 0.17f, 0.55f));
        Debug.Log("DodgeRam1500TRX: configured front crew-cab exit markers.");
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
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 850f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 250f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 250f);
                SetRelativeNumber(serialized, "powertrain.clutch.slipTorque", 1100f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 3f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.8f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 523f);
                SetRelativeNumber(serialized, "powertrain.engine.engineLossPercent", DodgeRam1500TRXPowertrainProfile.NativeEngineLoss);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateTRXPowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 700f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 6200f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.48f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", true);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0.05f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.55f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.22f);
                SetRelativeBool(serialized, "powertrain.transmission.variableShiftPoint", false);
                SetRelativeNumber(serialized, "wheelbase", Wheelbase + FrontWheelForwardOffset - RearWheelForwardOffset);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 3100f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 5800f);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);
                SetRelativeNumber(serialized, "brakes.maxTorque", BrakeMaxTorque);
                SetRelativeNumber(serialized, "steering.maximumSteerAngle", DodgeRam1500TRXSteeringProfile.MaximumSteerAngle);
                SetRelativeNumber(serialized, "steering.degreesPerSecondLimit", DodgeRam1500TRXSteeringProfile.DegreesPerSecond);
                var steeringCurve = FindRelativeProperty(serialized, "steering.speedSensitiveSteeringCurve");
                if (steeringCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("TRX native steering speed curve is missing.");
                steeringCurve.animationCurveValue = DodgeRam1500TRXSteeringProfile.CreateSpeedCurve();

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
                var allWheelDriveConfigured = false;
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
                        throw new InvalidOperationException("Center differential front/rear torque split is missing.");
                    // The TRX Auto transfer case uses a 40:60 front-to-rear torque split.
                    bias.floatValue = 0.4f;
                    allWheelDriveConfigured = true;
                }
                if (!allWheelDriveConfigured)
                    throw new InvalidOperationException("Center differential could not be configured for 40:60 AWD.");

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = TRXGears.Length;
                for (var index = 0; index < TRXGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = TRXGears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 190f);
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
            throw new InvalidOperationException("DodgeRam1500TRX model length could not be measured.");

        model.transform.localRotation = candidates[bestIndex];
        var front = FindTransformWithNameFragment(model.transform, "ORANGE_GLASS");
        var rear = FindTransformWithNameFragment(model.transform, "RED_GLASS");
        if (front != null && rear != null &&
            TryGetRendererBounds(front, out var frontBounds) &&
            TryGetRendererBounds(rear, out var rearBounds) &&
            frontBounds.center.z < rearBounds.center.z)
        {
            model.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
        }

        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Oriented DodgeRam1500TRX body bounds could not be measured.");
        // Bounds alone cannot distinguish an upright vehicle from an inverted one.
        // Use the original wheel geometry as the vertical orientation reference.
        var wheels = FindTransformWithNameFragment(model.transform, "polySurface8441");
        if (wheels == null || !TryGetRendererBounds(wheels, out var wheelBounds))
            throw new InvalidOperationException("TRX upright orientation reference is missing.");
        if (wheelBounds.center.y > bounds.center.y)
        {
            model.transform.localRotation = Quaternion.Euler(0f, 0f, 180f) * model.transform.localRotation;
            if (!TryGetModelBodyBounds(model.transform, out bounds))
                throw new InvalidOperationException("Upright TRX bounds are missing.");
        }

        var scale = bestIndex == 0
            ? new Vector3(
                TargetWidth / bounds.size.x,
                TargetHeight / bounds.size.y,
                TargetLength / bounds.size.z)
            : new Vector3(
                TargetWidth / bounds.size.x,
                TargetLength / bounds.size.z,
                TargetHeight / bounds.size.y);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled DodgeRam1500TRX bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final DodgeRam1500TRX bounds could not be measured.");

        Debug.Log(
            $"DodgeRam1500TRX: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        DodgeRam1500TRXWheelBuilder.Attach(
            root,
            model,
            WheelControllerPositions,
            FrontTireRadius,
            RearTireRadius,
            FrontTireWidth,
            MeshFolder,
            MaterialFolder);
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
                    (IsBodyPaintMaterial(material) || IsCaliperMaterial(material)))
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
        MeshRenderer? sourceRenderer = null;
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (HasMaterialMarker(renderer, "RAM_1500TRX_2021Paint_Material") &&
                HasAncestorNameFragment(renderer.transform, "body:Paint"))
            {
                sourceRenderer = renderer;
                break;
            }
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The DodgeRam1500TRX outer body mesh was not found.");

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        bakedMesh.name = "DodgeRam1500TRXDamageBody";
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
                tangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w * (sourceToRoot.determinant < 0f ? -1f : 1f));
            }
            bakedMesh.tangents = tangents;
        }
        if (sourceToRoot.determinant < 0f)
        {
            for (var submesh = 0; submesh < bakedMesh.subMeshCount; submesh++)
            {
                var triangles = bakedMesh.GetTriangles(submesh);
                for (var i = 0; i < triangles.Length; i += 3)
                    (triangles[i + 1], triangles[i + 2]) = (triangles[i + 2], triangles[i + 1]);
                bakedMesh.SetTriangles(triangles, submesh);
            }
        }
        Debug.Log($"TRX body bake determinant={sourceToRoot.determinant:F6}, mirroredWindingCorrected={sourceToRoot.determinant < 0f}.");
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

        var damageBody = new GameObject("DodgeRam1500TRXDamageBody")
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
        // Custom world-space deformation owns all exterior meshes and writes the
        // native save records. Remove the legacy collision callback entirely:
        // Unity also sends collision callbacks to disabled MonoBehaviours.
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (component != null && component.GetType().Name == "VehicleDeformationController")
                UnityEngine.Object.DestroyImmediate(component);
    }

    private static bool IsBodyPaintMaterial(Material material) =>
        material.name.IndexOf("RAM_1500TRX_2021Paint_Material", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsCabinGlassRenderer(Transform transform)
    {
        if (HasAncestorNameFragment(transform, "ORANGE_GLASS") ||
            HasAncestorNameFragment(transform, "RED_GLASS"))
            return false;
        return HasAncestorNameFragment(transform, "Window_Geo") ||
               HasAncestorNameFragment(transform, "WindowInside_Geo");
    }

    private static bool IsRimMaterial(Material material) =>
        material.name.IndexOf("DodgeRam1500TRXRim", StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!DodgeRam1500TRXMaterials.IsDodgeRam1500TRXRenderer(renderer.transform))
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
            throw new InvalidOperationException("The shared DodgeRam1500TRX rim material is missing.");

        ApplyRimFinish(leftMaterial, DodgeRam1500TRXMaterials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!DodgeRam1500TRXMaterials.IsDodgeRam1500TRXRenderer(renderer.transform))
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
                $"Expected four DodgeRam1500TRX rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", DodgeRam1500TRXMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", DodgeRam1500TRXMaterials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("DodgeRam1500TRXCaliper", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool TRXPowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateTRXPowerCurve();
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
        material.name.IndexOf("RAM_1500TRX_2021InteriorA_Material", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorSecondaryPaintMaterial(Material material) =>
        material.name.IndexOf("InteriorA_Material", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorDarkPaintMaterial(Material material) => false;

    private static void AssignPersistentMaterials(GameObject model)
    {
        EnsureAssetFolder(MaterialFolder);
        DodgeRam1500TRXSourceMaterials.Begin();
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
                    var transparent = DodgeRam1500TRXMaterials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"DodgeRam1500TRX{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

                    DodgeRam1500TRXSourceMaterials.Apply(persistent, source.name);
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

        manifest.ModId = "DodgeRam1500TRX";
        manifest.DisplayName = "Dodge Ram 1500 TRX";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "dodgeram1500trx.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("DodgeRam1500TRX manifest references could not be assigned.");
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

    private static bool TryGetDodgeRam1500TRXRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!DodgeRam1500TRXMaterials.IsDodgeRam1500TRXRenderer(renderer.transform))
                continue;
            if (!renderer.enabled || renderer.forceRenderingOff)
                continue;
            if (renderer is MeshRenderer meshRenderer &&
                (meshRenderer.GetComponent<MeshFilter>()?.sharedMesh == null ||
                 meshRenderer.GetComponent<MeshFilter>()!.sharedMesh.vertexCount == 0))
                continue;
            if (renderer is SkinnedMeshRenderer skinnedRenderer &&
                (skinnedRenderer.sharedMesh == null || skinnedRenderer.sharedMesh.vertexCount == 0))
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

    private static int RemoveMissingScripts(GameObject root)
    {
        var removed = 0;
        var remaining = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            var gameObject = transform.gameObject;
            removed += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
            remaining += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
        }

        if (remaining != 0)
            throw new InvalidOperationException("DodgeRam1500TRX still contains a missing MonoBehaviour script.");
        return removed;
    }

    private static int CountNullComponents(GameObject root)
    {
        var missing = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            foreach (var component in transform.GetComponents<Component>())
                if (component == null)
                    missing++;
        return missing;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var path = transform.name;
        for (var current = transform.parent; current != null; current = current.parent)
            path = current.name + "/" + path;
        return path;
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

