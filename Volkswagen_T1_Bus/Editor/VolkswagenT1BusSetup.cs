#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class VolkswagenT1BusSetup
{
    private const string ModRoot = "Assets/Mods/Volkswagen_T1_Bus";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/vw_t1_style_bus_free.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/T1BusDamageBody.asset";
    private const string RearGlassMeshPath =
        MeshFolder + "/T1BusRearGlass.asset";
    private const string VehicleAssetPath = ModRoot + "/VolkswagenT1Bus.asset";
    private const string VehiclePrefabPath = ModRoot + "/VolkswagenT1Bus.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/VolkswagenT1Bus.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/volkswagen_t1_bus.unity3d";
    private const string MacBundlePath =
        ModRoot + "/AssetBundles/Mac/volkswagen_t1_bus.unity3d";
    private const string VehicleTypeName =
        "volkswagent1bus-vehicle:vehicletype_volkswagent1bus";
    private const float TargetLength = 4.28f;
    private const float TargetWidth = 1.75f;
    private const float VisualTargetWidth = 1.85f;
    private const float BodyVisualBottomY = -0.01f;
    private const float TargetHeight = 1.93f;
    private const float FrontTrack = 1.37f;
    private const float RearTrack = 1.36f;
    private const float Wheelbase = 2.39f;
    private const float FrontTireRadius = 0.35f;
    private const float RearTireRadius = 0.35f;
    private const float FrontTireWidth = 0.18f;
    private const float RearTireWidth = 0.18f;
    private const float WheelInset = 0f;
    private const float WheelCenterRideHeightOffset = 0.015f;
    private const float VehicleLinearDrag = 0f;
    private const float VehicleBrakeForce = 875f;
    private const float BrakeMaxTorque = 875f;
    private const float FactoryEnginePowerKw = 32.3f;
    private const float EngineSimulationPowerKw = 35.0f;
    private const float EngineLossPercent = 0f;
    private const float FrontForwardGrip = 0.92f;
    private const float RearForwardGrip = 0.90f;
    private const float FrontForwardStiffness = 0.95f;
    private const float RearForwardStiffness = 0.94f;
    private const float TireFrictionCircleStrength = 0.90f;
    private const float AntiRollBarForce = 1800f;
    private const float FrontSuspensionTravel = 0.120f;
    private const float RearSuspensionTravel = 0.140f;
    private const float DeformationStrength = 0.14f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.27f, -0.26f);
    private const float FrontSidewaysGrip = 0.85f;
    private const float RearSidewaysGrip = 0.90f;
    private const float SidewaysStiffness = 0.95f;

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f, FrontTireRadius + WheelCenterRideHeightOffset, 1.140f) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f, FrontTireRadius + WheelCenterRideHeightOffset, 1.140f) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f, RearTireRadius + WheelCenterRideHeightOffset, -1.25f) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f, RearTireRadius + WheelCenterRideHeightOffset, -1.25f) },
        };
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.41f, 1.78f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.64f, 0.68f, 0.82f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.41f, -1.78f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.64f, 0.68f, 0.82f);

    private static readonly float[] T1BusGears =
    {
        -3.80f, 0f, 3.80f, 2.06f, 1.26f, 0.82f,
    };

    private static AnimationCurve CreateT1BusPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.20f, 0.18f),
            new Keyframe(0.33f, 0.40f), new Keyframe(0.465f, 0.66f),
            new Keyframe(0.60f, 0.79f), new Keyframe(0.78f, 0.90f),
            new Keyframe(0.93f, 1.00f),
            new Keyframe(1.00f, 0.94f));
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "VolkswagenT1Bus setup complete: 1967 Type 2 T1 1500, rear-engine/rear-drive " +
            "four-speed powertrain, fitted source wheels, factory two-tone paint, " +
            "independent lamp states, repaint support and deformation geometry.");
    }

    public static void RegenerateAndBuildStandaloneWindowsAssetBundle()
    {
        Generate();
        BuildStandaloneWindowsAssetBundle();
    }

    public static void RegenerateAndBuildAssetBundles()
    {
        Generate();
        BuildStandaloneAssetBundles();
    }

    [MenuItem("Big Ambitions Mods/Build Volkswagen T1 Bus AssetBundle")]
    public static void BuildStandaloneWindowsAssetBundle()
    {
        BuildStandaloneAssetBundle(BuildTarget.StandaloneWindows64, "Windows");
    }

    public static void BuildStandaloneMacAssetBundle()
    {
        BuildStandaloneAssetBundle(BuildTarget.StandaloneOSX, "Mac");
    }

    public static void BuildStandaloneAssetBundles()
    {
        BuildStandaloneWindowsAssetBundle();
        BuildStandaloneMacAssetBundle();
    }

    private static void BuildStandaloneAssetBundle(BuildTarget target, string platformFolder)
    {
        VolkswagenT1BusWashSetup.PrepareAssetsForBundle();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        const string bundleFileName = "volkswagen_t1_bus.unity3d";
        const string bundleBaseName = "volkswagen_t1_bus";
        const string bundleVariant = "unity3d";

        var projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
        var temporaryOutputDirectory = System.IO.Path.Combine(
            projectRoot, "Temp", "VolkswagenT1BusAssetBundle", platformFolder);
        if (System.IO.Directory.Exists(temporaryOutputDirectory))
            System.IO.Directory.Delete(temporaryOutputDirectory, true);
        System.IO.Directory.CreateDirectory(temporaryOutputDirectory);

        var build = new AssetBundleBuild
        {
            // Match the SDK ModPackager convention: "volkswagen_t1_bus.unity3d"
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
            $"VolkswagenT1Bus: building {platformFolder} AssetBundle in temporary folder " +
            $"'{temporaryOutputDirectory}' from '{VehicleAssetPath}' and '{VehiclePrefabPath}'.");

        var manifest = BuildPipeline.BuildAssetBundles(
            temporaryOutputDirectory,
            new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression,
            target);
        if (manifest == null)
            throw new InvalidOperationException(
                "Volkswagen T1 Bus AssetBundle build failed. See the Unity Console entries immediately before this exception for the BuildPipeline error.");

        var producedBundlePath = System.IO.Path.Combine(temporaryOutputDirectory, bundleFileName);
        if (!System.IO.File.Exists(producedBundlePath))
        {
            var producedFiles = System.IO.Directory.Exists(temporaryOutputDirectory)
                ? string.Join(", ", System.IO.Directory.GetFiles(temporaryOutputDirectory))
                : "<temporary output directory missing>";
            throw new InvalidOperationException(
                $"Volkswagen T1 Bus AssetBundle manifest was created, but '{bundleFileName}' was not. " +
                $"Produced files: {producedFiles}");
        }

        var finalOutputDirectory = System.IO.Path.Combine(
            Application.dataPath, "Mods", "Volkswagen_T1_Bus", "AssetBundles", platformFolder);
        System.IO.Directory.CreateDirectory(finalOutputDirectory);
        var finalBundlePath = System.IO.Path.Combine(finalOutputDirectory, bundleFileName);
        foreach (var producedPath in System.IO.Directory.GetFiles(temporaryOutputDirectory))
        {
            var destinationPath = System.IO.Path.Combine(
                finalOutputDirectory,
                System.IO.Path.GetFileName(producedPath));
            System.IO.File.Copy(producedPath, destinationPath, true);
        }

        var bundle = AssetBundle.LoadFromFile(finalBundlePath);
        if (bundle == null)
            throw new InvalidOperationException(
                $"Could not load freshly built T1Bus bundle '{finalBundlePath}'.");
        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException(
                    "Fresh T1Bus bundle is missing VolkswagenT1Bus.asset or VolkswagenT1Bus.prefab.");
        }
        finally
        {
            bundle.Unload(false);
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            $"VolkswagenT1Bus: built {platformFolder} AssetBundle " +
            $"'Assets/Mods/Volkswagen_T1_Bus/AssetBundles/{platformFolder}/{bundleFileName}'.");
        VerifyBuiltBundleAtPath(finalBundlePath);
    }

    private static bool IsSupportedRuntimeAudioWav(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 44 || new string(reader.ReadChars(4)) != "RIFF")
                return false;
            var end = 8L + reader.ReadUInt32();
            if (end > stream.Length || new string(reader.ReadChars(4)) != "WAVE")
                return false;

            var format = 0;
            var channels = 0;
            var rate = 0;
            var bits = 0;
            var alignment = 0;
            uint dataBytes = 0;
            while (stream.Position + 8 <= end)
            {
                var id = new string(reader.ReadChars(4));
                var size = reader.ReadUInt32();
                var next = stream.Position + size;
                if (next > end)
                    return false;
                if (id == "fmt ")
                {
                    if (size < 16)
                        return false;
                    format = reader.ReadUInt16();
                    channels = reader.ReadUInt16();
                    rate = reader.ReadInt32();
                    reader.ReadUInt32();
                    alignment = reader.ReadUInt16();
                    bits = reader.ReadUInt16();
                }
                else if (id == "data")
                {
                    dataBytes = size;
                }
                stream.Position = next + (size & 1);
            }

            return format == 1 && channels == 1 && (rate == 44100 || rate == 48000) &&
                   bits == 16 && alignment == 2 && dataBytes > 0 && dataBytes % 2 == 0 &&
                   dataBytes / 2 <= rate * 10;
        }
        catch
        {
            return false;
        }
    }

    public static void GenerateAndBuild()
    {
        Generate();
        BuildStandaloneAssetBundles();
    }

    public static void RepairPrefabAndBuild()
    {
        GenerateAndBuild();
    }
    public static void VerifyBuiltBundle()
    {
        VerifyBuiltBundleAtPath(WindowsBundlePath);
    }

    public static void VerifyBuiltMacBundle()
    {
        VerifyBuiltBundleAtPath(MacBundlePath);
    }

    private static void VerifyBuiltBundleAtPath(string bundlePath)
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
            VolkswagenT1BusWashSetup.VerifyBindings(prefab);

            var type = new SerializedObject(vehicleType);
            RequireBuild(ReadString(type.FindProperty("vehicleTypeName")) == VehicleTypeName,
                "VehicleType localization id does not match this mod.");
            var prefabLookupId = VehicleTypeName.Split('_').ElementAtOrDefault(1) ?? string.Empty;
            var prefabAssetName = Path.GetFileNameWithoutExtension(VehiclePrefabPath);
            RequireBuild(!string.IsNullOrEmpty(prefabLookupId) &&
                         string.Equals(prefabAssetName, prefabLookupId, StringComparison.OrdinalIgnoreCase),
                $"VehicleType id resolves to player prefab '{prefabLookupId}', but bundled asset is '{prefabAssetName}.prefab'.");
            RequireBuild(Math.Abs(ReadNumber(type.FindProperty("maxFuel")) - 40f) < 0.01f,
                "Fuel capacity is not the sourced 40 L.");
            RequireBuild(Math.Abs(ReadNumber(type.FindProperty("maxSpeed")) - 105f) < 0.01f,
                "Speed limit is not 105 km/h.");
            RequireBuild(Math.Abs(ReadNumber(type.FindProperty("enginePower")) - FactoryEnginePowerKw) < 0.1f,
                "Factory-rated engine output metadata is incorrect.");
            RequireBuild(Math.Abs(ReadNumber(type.FindProperty("maxCargoCapacity")) - 20f) < 0.01f,
                "Gameplay cargo capacity is not the requested 20 storage slots.");
            var truckFlag = type.FindProperty("isATruck");
            var luxuryFlag = type.FindProperty("isLuxuryCar");
            var radioFlag = type.FindProperty("hasRadio");
            var enclosedFlag = type.FindProperty("enclosed");
            RequireBuild(IsFalseOrMissing(truckFlag) &&
                         IsFalseOrMissing(luxuryFlag) &&
                         IsFalseOrMissing(radioFlag) &&
                         enclosedFlag?.propertyType == SerializedPropertyType.Boolean &&
                         enclosedFlag.boolValue,
                "Vehicle classification or cabin settings are incorrect " +
                $"(isATruck={DescribeBool(truckFlag)}, isLuxuryCar={DescribeBool(luxuryFlag)}, " +
                $"hasRadio={DescribeBool(radioFlag)}, enclosed={DescribeBool(enclosedFlag)}).");

            var visual = FindTransform(prefab.transform, "T1BusVisual");
            if (visual == null || !TryGetT1BusRendererBounds(prefab.transform, out var visualBounds))
                throw new InvalidOperationException("T1 model visual root or renderer bounds are missing.");
            RequireBuild(Math.Abs(visualBounds.size.z - TargetLength) < 0.12f &&
                         Math.Abs(visualBounds.size.y - TargetHeight) < 0.16f &&
                         visualBounds.size.x > 1.60f && visualBounds.size.x < 2.00f,
                $"Imported T1 dimensions look wrong: {visualBounds.size.x:F2} x {visualBounds.size.y:F2} x {visualBounds.size.z:F2} m.");

            var cabinGlassRenderers = visual.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            RequireBuild(cabinGlassRenderers.Length == 3 && cabinGlassRenderers.All(renderer =>
                             renderer.enabled && renderer.gameObject.activeSelf &&
                             renderer.sharedMaterials.Any(material => material != null &&
                                 VolkswagenT1BusMaterials.IsTransparentMaterial(material) &&
                                 material.renderQueue >= (int)RenderQueue.Transparent)),
                $"Expected three active, transparent source window renderers; found {cabinGlassRenderers.Length}.");
            RequireBuild(prefab.GetComponent<VolkswagenT1BusMaterialController>() != null,
                "Runtime material controller that restores transparent cabin glass is missing.");

            var rearGlass = FindTransform(prefab.transform, "T1BusRearGlass");
            var rearGlassRenderer = rearGlass?.GetComponent<MeshRenderer>();
            var rearGlassMesh = rearGlass?.GetComponent<MeshFilter>()?.sharedMesh;
            RequireBuild(rearGlassRenderer != null && rearGlassRenderer.enabled &&
                         rearGlassMesh != null && rearGlassMesh.vertexCount >= 8 &&
                         rearGlassRenderer.sharedMaterials.Any(material => material != null &&
                             VolkswagenT1BusMaterials.IsTransparentMaterial(material) &&
                             material.renderQueue >= (int)RenderQueue.Transparent),
                "T1Bus rear-window pane or transparent material is missing.");

            var frontLamp = FindTransformWithNameFragment(visual, "headlights");
            var rearLamp = FindTransformWithNameFragment(visual, "tail light");
            var frontWheelController = FindTransform(prefab.transform, "FrontLeft_WheelController");
            if (frontLamp == null || rearLamp == null || frontWheelController == null ||
                !TryGetRendererBounds(frontLamp, out var frontLampBounds) ||
                !TryGetRendererBounds(rearLamp, out var rearLampBounds))
                throw new InvalidOperationException("T1 front/rear orientation landmarks are missing.");
            RequireBuild(frontLampBounds.center.z > rearLampBounds.center.z &&
                         frontLampBounds.center.y > frontWheelController.position.y,
                $"T1 model orientation is incorrect: front lamp=({frontLampBounds.center.x:F2}, {frontLampBounds.center.y:F2}, {frontLampBounds.center.z:F2}), " +
                $"rear lamp=({rearLampBounds.center.x:F2}, {rearLampBounds.center.y:F2}, {rearLampBounds.center.z:F2}), " +
                $"front wheel Y={frontWheelController.position.y:F2}.");

            var damageBody = FindTransform(prefab.transform, "T1BusDamageBody");
            if (damageBody?.GetComponent<MeshFilter>()?.sharedMesh == null ||
                damageBody.GetComponent<MeshRenderer>() == null)
                throw new InvalidOperationException("Deformable T1 body mesh or renderer is missing.");
            var damageMaterials = damageBody.GetComponent<MeshRenderer>().sharedMaterials;
            RequireBuild(damageMaterials.Any(material => material != null &&
                             material.name.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0) &&
                         damageMaterials.Any(material => material != null &&
                             (material.name.IndexOf("headlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              material.name.IndexOf("black metal", StringComparison.OrdinalIgnoreCase) >= 0)),
                "Deformable exterior mesh omits the T1 chrome and front lamp/body details.");
            var lightSources = FindTransform(prefab.transform, "T1BusLightSources");
            if (lightSources == null)
                throw new InvalidOperationException("Independent lamp source hierarchy is missing.");
            foreach (var lampName in new[] { "BHeadlights", "1RearDrivingLights", "1BrakeLights" })
                RequireBuild(FindTransform(lightSources, lampName) != null,
                    $"Required T1 lamp source '{lampName}' is missing.");
            RequireBuild(lightSources.childCount == 3,
                $"T1 lamp source hierarchy contains {lightSources.childCount - 3} extra non-original lamp object(s).");
            var lights = prefab.GetComponentsInChildren<Light>(true);
            RequireBuild(Array.Exists(lights, light => light.name == "Spotlights" && !light.enabled),
                "Road-light template is missing or active before player input.");
            RequireBuild(FindTransform(prefab.transform, "Driverside") != null &&
                         FindTransform(prefab.transform, "Passengerside") != null &&
                         FindTransform(prefab.transform, "RefuelingPosition") != null,
                "Driver/passenger exit or refueling anchor is missing.");
            RequireBuild(FindTransform(prefab.transform, "VehicleNavObstacle")
                             ?.GetComponent<UnityEngine.AI.NavMeshObstacle>() != null,
                "Vehicle navigation-obstacle safeguard is missing.");

            var bodyCollider = FindTransform(prefab.transform, "BodyCollider");
            var bodyColliders = bodyCollider != null ? bodyCollider.GetComponents<BoxCollider>() : Array.Empty<BoxCollider>();
            RequireBuild(bodyColliders.Length >= 4,
                "Main body and front/rear contact colliders are incomplete.");
            var body = prefab.GetComponent<Rigidbody>();
            if (body == null || Math.Abs(body.mass - 1150f) > 0.1f ||
                Vector3.Distance(body.centerOfMass, StableCenterOfMass) > 0.01f ||
                body.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic)
                throw new InvalidOperationException("Mass, center of mass or collision detection is not configured for the T1.");

            foreach (var pair in WheelControllerPositions)
            {
                var controller = FindTransform(prefab.transform, pair.Key);
                var corner = pair.Key.Replace("_WheelController", string.Empty);
                var mount = FindTransform(prefab.transform, "T1BusWheel" + corner);
                RequireBuild(controller != null && Vector3.Distance(controller.localPosition, pair.Value) < 0.015f,
                    $"Wheel physics position for {corner} is missing or incorrect.");
                if (mount == null)
                    throw new InvalidOperationException($"Rolling wheel visual for {corner} is missing.");
                var hasFittedTire = false;
                foreach (var child in mount.GetComponentsInChildren<Transform>(true))
                    hasFittedTire |= child.name.StartsWith("Geometry_T1BusTire_", StringComparison.Ordinal);
                RequireBuild(hasFittedTire, $"Fitted tire mesh for {corner} is missing.");
            }

            var serializedPhysicsFound = false;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || !string.Equals(component.GetType().FullName,
                        "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
                    continue;
                serializedPhysicsFound = true;
                var physics = new SerializedObject(component);
                var powertrain = FindRelativeProperty(physics, "powertrain");
                var engine = powertrain?.FindPropertyRelative("engine");
                var transmission = powertrain?.FindPropertyRelative("transmission");
                var gears = transmission?.FindPropertyRelative("gears");
                var forcedInduction = engine?.FindPropertyRelative("forcedInduction")
                    ?.FindPropertyRelative("useForcedInduction");
                RequireBuild(Math.Abs(ReadNumber(physics.FindProperty("wheelbase")) - Wheelbase) < 0.01f,
                    "Physics wheelbase does not match the adjusted 2.39 m axle positions.");
                RequireBuild(Math.Abs(ReadNumber(engine?.FindPropertyRelative("maxPower")) - EngineSimulationPowerKw) < 0.1f &&
                             Math.Abs(ReadNumber(engine?.FindPropertyRelative("engineLossPercent")) - EngineLossPercent) < 0.01f &&
                             Math.Abs(ReadNumber(engine?.FindPropertyRelative("idleRPM")) - 850f) < 0.1f &&
                             Math.Abs(ReadNumber(engine?.FindPropertyRelative("revLimiterRPM")) - 4300f) < 0.1f &&
                             forcedInduction?.boolValue == false,
                    "Engine output, RPM range or naturally aspirated setting is incorrect.");
                RequireBuild(transmission?.FindPropertyRelative("forwardGearCount")?.intValue == 4 &&
                             transmission?.FindPropertyRelative("reverseGearCount")?.intValue == 1 &&
                             gears != null && gears.isArray && gears.arraySize == T1BusGears.Length &&
                             Math.Abs(ReadNumber(transmission?.FindPropertyRelative("finalGearRatio")) - 6.0764f) < 0.01f,
                    "Manual four-speed transmission or simulation final drive is incomplete.");
                for (var index = 0; index < T1BusGears.Length; index++)
                    RequireBuild(Math.Abs(ReadNumber(gears!.GetArrayElementAtIndex(index)) - T1BusGears[index]) < 0.01f,
                        $"Simulation gear value at slot {index} does not match the documented setup.");

                var rearDriveConfigured = false;
                var differentials = FindRelativeProperty(physics, "powertrain.differentials");
                if (differentials != null && differentials.isArray)
                    for (var index = 0; index < differentials.arraySize; index++)
                    {
                        var differential = differentials.GetArrayElementAtIndex(index);
                        if (ReadString(differential.FindPropertyRelative("name")) == "Center Differential")
                            rearDriveConfigured = Math.Abs(ReadNumber(differential.FindPropertyRelative("biasAB")) - 1f) < 0.001f;
                    }
                RequireBuild(rearDriveConfigured,
                    "Powertrain differential is not set to deliver drive to the rear axle.");
                RequireBuild(Math.Abs(ReadNumber(FindRelativeProperty(physics, "brakes.maxTorque")) - BrakeMaxTorque) < 0.5f,
                    "Brake torque is not set to the reduced T1 drum-brake value.");
            }
            RequireBuild(serializedPhysicsFound, "NWH VehicleController is missing.");

            var hasBodyPaint = false;
            var hasCreamPaint = false;
            var hasGlass = false;
            var nullMaterialSlots = 0;
            var errorShaders = new List<string>();
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        nullMaterialSlots++;
                        continue;
                    }
                    hasBodyPaint |= material.name.IndexOf("BA_VehiclePaint_body_1", StringComparison.OrdinalIgnoreCase) >= 0;
                    hasCreamPaint |= material.name.IndexOf("FactoryCream_body_2", StringComparison.OrdinalIgnoreCase) >= 0;
                    hasGlass |= material.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 && material.renderQueue >= 2500;
                    if ((material.shader?.name ?? string.Empty).IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0)
                        errorShaders.Add(material.name);
                }
            RequireBuild(nullMaterialSlots == 0 && errorShaders.Count == 0,
                $"Prefab contains null material slots ({nullMaterialSlots}) or error shaders ({errorShaders.Count}).");
            RequireBuild(hasBodyPaint && hasCreamPaint && hasGlass,
                "Red repaintable body, fixed cream body or transparent cabin glass material is missing.");
            RequireBuild(prefab.GetComponent<VolkswagenT1BusMaterialController>() != null,
                "Runtime material/paint lifecycle controller is missing.");

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            foreach (var clipName in new[]
                     {
                         "VW_T1_Worn_Old_Engine_740RPM_Source",
                         "EngineT1Worn740RPM_2x",
                         "VW_T1_Worn_ConstantDrive_Mixer_Friendly",
                         "HornLow",
                         "HornHigh",
                     })
            {
                var path = Path.Combine(projectRoot, ModRoot, "Config", "Audio", clipName + ".wav");
                RequireBuild(File.Exists(path) && new FileInfo(path).Length > 44,
                    $"Generated T1 audio asset '{clipName}.wav' is missing or empty.");
                if (clipName != "VW_T1_Worn_Old_Engine_740RPM_Source")
                    RequireBuild(IsSupportedRuntimeAudioWav(path),
                        $"Runtime T1 audio asset '{clipName}.wav' is not supported mono PCM.");
            }

            Debug.Log(
                "VolkswagenT1Bus bundle verification passed: researched T1 dimensions and engine, " +
                "rear drive, four-speed gearbox, fitted wheels, paint/glass, independent lamp states, " +
                "deformation body, collision/service safeguards, 20 storage slots, separate idle/drive loops, and horn assets.");
        }
        finally
        {
            bundle.Unload(false);
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
                throw new InvalidOperationException("Could not create the T1Bus VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated T1Bus VehicleType asset did not load.");

        target.name = "VolkswagenT1Bus";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 29900f); // Gameplay balance estimate; not a historical price.
        SetNumber(serialized, "maxFuel", 40f);
        SetNumber(serialized, "maxCargoCapacity", 20f); // User-requested storage capacity.
        SetNumber(serialized, "maxSpeed", 105f);
        SetNumber(serialized, "enginePower", FactoryEnginePowerKw);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", 35f);
        SetNumber(serialized, "damageIntensity", 0.32f);
        SetBool(serialized, "isATruck", false);
        SetBool(serialized, "isHandVehicle", false);
        SetBool(serialized, "fitsHandTruck", false);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", false);
        SetBool(serialized, "hasRadio", false);
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
            throw new InvalidOperationException("T1Bus VehicleType asset was not found.");

        var serialized = new SerializedObject(vehicleType);
        SetNumber(serialized, "maxSpeed", 105f);
        SetNumber(serialized, "enginePower", FactoryEnginePowerKw);
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
            throw new InvalidOperationException("The supplied Volkswagen T1 GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "VolkswagenT1Bus";
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
                throw new InvalidOperationException("Could not instantiate the T1Bus model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "T1BusVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            modelInstance.transform.localPosition += new Vector3(0f, 0.126f, 0f); // lower body 1 cm relative to raised wheels
            AttachLightOverlaySources(root, modelInstance);
            ConfigureExitMarkers(root, modelInstance);
            AssignT1BusSourceMaterials(modelInstance);
            CreateRearGlass(root);
            AttachWheelVisuals(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            ConfigurePickupDamageHandler(root);
            var fix = PreparePrefabMaterialsWithoutRuntimeClones(root);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);
            VolkswagenT1BusWashSetup.Author(root);

            Debug.Log(
                $"VolkswagenT1Bus: prepared decal-safe materials renderers={fix.RendererCount}, " +
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
                    $"T1Bus prefab has {preSaveNullMaterialSlots} null material slot(s) immediately before SaveAsPrefabAsset().");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the T1Bus vehicle prefab.");
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
        Debug.Log($"VolkswagenT1Bus: removed {removed} missing donor script component(s).");
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
            Debug.LogError("VolkswagenT1Bus missing-script diagnostic: " + detail);
        throw new InvalidOperationException(
            $"Generated T1Bus prefab still contains {details.Count} missing script component(s): " +
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
                    $"Generated T1Bus prefab still contains Audi donor hierarchy '{name}'.");
            }
        }
    }

    private static void ConvertT1BusOpaqueMaterialsToHdrp(GameObject model)
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

                var transparent = VolkswagenT1BusMaterials.IsTransparentMaterial(material);
                VolkswagenT1BusMaterials.NormalizeImportedMaterial(material);
                EditorUtility.SetDirty(material);
                if (transparent) normalizedTransparent++; else normalizedOpaque++;
            }
        }

        Debug.Log(
            $"VolkswagenT1Bus: normalized persistent HDRP materials " +
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
                "Generated T1Bus prefab still has error-shader materials: " +
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
        body.mass = 1150f;
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
            SetNumber(serialized, "baseMass", 1150f);
            SetNumber(serialized, "combinedMass", 1150f);
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
                SetRelativeNumber(
                    serialized,
                    "sideFriction.grip",
                    isFront ? FrontSidewaysGrip : RearSidewaysGrip);
                SetRelativeNumber(
                    serialized,
                    "sideFriction.stiffness",
                    SidewaysStiffness);
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

        colliders[0].center = new Vector3(0f, 0.37f, 0f);
        colliders[0].size = new Vector3(1.68f, 0.52f, 4.05f);
        colliders[1].center = new Vector3(0f, 1.01f, -0.05f);
        colliders[1].size = new Vector3(1.60f, 1.10f, 3.15f);
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
            var mount = FindTransform(root.transform, "T1BusWheel" + corner) ??
                        throw new InvalidOperationException($"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "T1BusFixedCaliper" + corner) ??
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
                HasAncestorNameFragment(renderer.transform, "T1BusWheel"))
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
                if (HasAncestorNameFragment(renderer.transform, "T1BusWheel") &&
                    HasAncestorNameFragment(renderer.transform, "Geometry_T1BusTire_"))
                {
                    attachedTireCount++;
                }
            }

            if (attachedTireCount != 4)
                throw new InvalidOperationException(
                    $"Expected four attached T1Bus tires, found {attachedTireCount}.");
            return;
        }
        if (tires.Count != 4)
            throw new InvalidOperationException(
                $"Expected four body-static T1Bus tires, found {tires.Count}.");

        foreach (var pair in WheelControllerPositions)
        {
            var corner = pair.Key.Replace("_WheelController", string.Empty);
            var controller = FindTransform(root.transform, pair.Key) ??
                             throw new InvalidOperationException(
                                 $"Wheel controller '{pair.Key}' is missing.");
            var mount = FindTransform(root.transform, "T1BusWheel" + corner) ??
                        throw new InvalidOperationException(
                            $"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "T1BusFixedCaliper" + corner) ??
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
            tire.name = "Geometry_T1BusTire_" +
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
        var headlamp = FindRendererByNameFragment(modelInstance.transform, "headlights") ??
                       throw new InvalidOperationException("The T1 GLB has no headlight lens mesh.");
        var tailLamp = FindRendererByNameFragment(modelInstance.transform, "tail light") ??
                       throw new InvalidOperationException("The T1 GLB has no rear lamp mesh.");
        var lightRoot = new GameObject("T1BusLightSources");
        lightRoot.transform.SetParent(root.transform, false);
        CopyLightSource(headlamp, lightRoot.transform, "BHeadlights");
        CopyLightSource(tailLamp, lightRoot.transform, "1RearDrivingLights");
        CopyLightSource(tailLamp, lightRoot.transform, "1BrakeLights");

    }

    private static MeshRenderer? FindRendererByNameFragment(Transform root, string fragment)
    {
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var meshName = renderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? string.Empty;
            if (renderer.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0 ||
                meshName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return renderer;
        }
        return null;
    }

    private static void CopyLightSource(MeshRenderer source, Transform parent, string name)
    {
        var sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException($"T1 lamp source '{source.name}' has no mesh.");
        var host = new GameObject(name);
        host.transform.SetParent(parent, false);
        host.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        host.transform.localScale = Vector3.Scale(
            source.transform.lossyScale,
            new Vector3(1f / Mathf.Max(.001f, parent.lossyScale.x),
                        1f / Mathf.Max(.001f, parent.lossyScale.y),
                        1f / Mathf.Max(.001f, parent.lossyScale.z)));
        host.layer = source.gameObject.layer;
        host.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
    }

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        var steeringWheel = FindTransformWithNameFragment(model.transform, "steering wheel") ??
                            throw new InvalidOperationException("T1Bus steering wheel node 'steering wheel' is missing.");
        if (!TryGetRendererBounds(steeringWheel, out var steeringBounds))
            throw new InvalidOperationException("T1Bus steering wheel renderer bounds are missing.");
        var steeringPosition = root.transform.InverseTransformPoint(steeringBounds.center);
        var driverSide = steeringPosition.x < 0f ? -0.90f : 0.90f;
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.11f, -0.08f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.11f, -0.08f));
        Debug.Log(
            $"VolkswagenT1Bus: steering wheel x={steeringPosition.x:F3}; " +
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
                             "T1Bus service compatibility: BodyCollider is missing.");
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
                "T1Bus service compatibility: no enabled body BoxCollider is available.");

        var refuelingPosition = FindTransform(root.transform, "RefuelingPosition");
        if (refuelingPosition == null)
        {
            var host = new GameObject("RefuelingPosition");
            host.transform.SetParent(root.transform, false);
            // Fuel flap area on the T1Bus rear quarter. This transform is a
            // service anchor only; station eligibility is still vanilla.
            host.transform.localPosition = new Vector3(0.82f, 0.82f, -1.58f);
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
                // Runtime owns T1Bus visual deformation. Keep the legacy arrays
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
            $"VolkswagenT1Bus service compatibility: colliderBindings={colliderBindings}, " +
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
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 950f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 125f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 220f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.32f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", EngineSimulationPowerKw);
                SetRelativeNumber(serialized, "powertrain.engine.engineLossPercent", EngineLossPercent);
                var curve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (curve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                curve.animationCurveValue = CreateT1BusPowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 850f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 4300f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.50f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 6.0764f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 4f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.42f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 2100f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 3800f);
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
                var rearDrive = false;
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
                    // The reference center differential's B output feeds the rear
                    // differential; biasAB=1 selects the T1 rear-drive axle.
                    bias.floatValue = 1f;
                    rearDrive = true;
                }
                if (!rearDrive)
                    throw new InvalidOperationException("Reference center differential was not available for the T1 rear-drive setup.");

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = T1BusGears.Length;
                for (var index = 0; index < T1BusGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = T1BusGears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 105f);
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

        // Sketchfab GLBs vary in wrapper rotations. Select the upright axis
        // arrangement whose body bounds are longest on vehicle Z and shortest on Y.
        var candidates = new[]
        {
            Quaternion.identity,
            Quaternion.Euler(90f, 0f, 0f),
            Quaternion.Euler(-90f, 0f, 0f),
        };
        var bestIndex = -1;
        var bestScore = float.NegativeInfinity;
        var bounds = default(Bounds);
        var frontWheelNames = new[] { "Cylinder", "wheel fr" };
        var rearWheelNames = new[] { "wheel rl", "wheel rr" };
        for (var index = 0; index < candidates.Length; index++)
        {
            model.transform.localRotation = candidates[index];
            if (!TryGetModelBodyBounds(model.transform, out var candidateBounds))
                continue;
            if (!TryGetSourceAxleCenter(model.transform, frontWheelNames, out var frontAxleCenter) ||
                !TryGetSourceAxleCenter(model.transform, rearWheelNames, out var rearAxleCenter))
                continue;

            var wheelCenterY = (frontAxleCenter.y + rearAxleCenter.y) * 0.5f;
            if (wheelCenterY >= candidateBounds.center.y)
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
            throw new InvalidOperationException(
                "T1Bus model axes could not be normalized upright from its front/rear wheel positions.");

        model.transform.localRotation = candidates[bestIndex];
        if (!TryGetSourceAxleCenter(model.transform, frontWheelNames, out var frontAxle) ||
            !TryGetSourceAxleCenter(model.transform, rearWheelNames, out var rearAxle))
            throw new InvalidOperationException("T1Bus front/rear wheel positions could not be measured.");
        if (frontAxle.z < rearAxle.z)
        {
            model.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
        }

        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Oriented T1Bus body bounds could not be measured.");
        if (!TryGetSourceAxleCenter(model.transform, frontWheelNames, out frontAxle) ||
            !TryGetSourceAxleCenter(model.transform, rearWheelNames, out rearAxle) ||
            frontAxle.z <= rearAxle.z ||
            (frontAxle.y + rearAxle.y) * 0.5f >= bounds.center.y)
            throw new InvalidOperationException(
                "T1Bus orientation correction did not put the front axle forward and wheels below the body.");
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
            throw new InvalidOperationException("Scaled T1Bus bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        model.transform.position += new Vector3(0f, BodyVisualBottomY, 0f);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final T1Bus bounds could not be measured.");

        Debug.Log(
            $"VolkswagenT1Bus: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}, " +
            $"frontAxleZ={frontAxle.z:F3}, rearAxleZ={rearAxle.z:F3}, " +
            $"wheelCenterY={(frontAxle.y + rearAxle.y) * 0.5f:F3}.");
    }

    private static bool TryGetSourceAxleCenter(
        Transform modelRoot,
        string[] wheelNames,
        out Vector3 axleCenter)
    {
        axleCenter = Vector3.zero;
        if (wheelNames.Length == 0)
            return false;

        foreach (var wheelName in wheelNames)
        {
            var wheel = FindTransform(modelRoot, wheelName);
            var tyre = wheel != null ? FindTransformWithNameFragment(wheel, "_rubber_0") : null;
            if (tyre == null || !TryGetRendererBounds(tyre, out var tyreBounds))
                return false;
            axleCenter += tyreBounds.center;
        }

        axleCenter /= wheelNames.Length;
        return true;
    }

    private static void CreateRearGlass(GameObject root)
    {
        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        // These points follow the open rear-window border in body_body 2_0 of the
        // supplied GLB. Its glass mesh includes the front and side panes, but no
        // pane over this factory-shaped rear aperture.
        var aperture = new[]
        {
            new Vector3(0f, 182f, -246f),
            new Vector3(37.6f, 182f, -246f),
            new Vector3(54.1f, 182f, -246f),
            new Vector3(59.4f, 182f, -244f),
            new Vector3(65.7f, 182f, -241.7f),
            new Vector3(65.7f, 186.2f, -241.7f),
            new Vector3(65.7f, 232f, -230.3f),
            new Vector3(61.6f, 236.2f, -230.3f),
            new Vector3(57f, 238.4f, -232.7f),
            new Vector3(0f, 238.4f, -234.4f),
            new Vector3(-57f, 238.4f, -232.7f),
            new Vector3(-61.6f, 236.2f, -230.3f),
            new Vector3(-65.7f, 232f, -230.3f),
            new Vector3(-65.7f, 186.2f, -241.7f),
            new Vector3(-65.7f, 182f, -241.7f),
            new Vector3(-59.4f, 182f, -244f),
            new Vector3(-54.1f, 182f, -246f),
            new Vector3(-37.6f, 182f, -246f),
        };
        const float sourceLeft = -120.98f;
        const float sourceRight = 120.91f;
        const float sourceBottom = 43.25f;
        const float sourceTop = 268.17f;
        const float sourceRear = -270.04f;
        const float sourceFront = 289.55f;
        const float sourceCenterX = (sourceLeft + sourceRight) * 0.5f;
        const float sourceCenterZ = (sourceRear + sourceFront) * 0.5f;
        const float glassInset = 0.006f;

        var points = new Vector3[aperture.Length + 1];
        var uv = new Vector2[points.Length];
        var bounds = default(Bounds);
        for (var i = 0; i < aperture.Length; i++)
        {
            var source = aperture[i];
            points[i] = new Vector3(
                (source.x - sourceCenterX) * VisualTargetWidth / (sourceRight - sourceLeft),
                (source.y - sourceBottom) * TargetHeight / (sourceTop - sourceBottom) +
                BodyVisualBottomY + 0.126f,
                (source.z - sourceCenterZ) * TargetLength / (sourceFront - sourceRear) + glassInset);
            if (i == 0)
                bounds = new Bounds(points[i], Vector3.zero);
            else
                bounds.Encapsulate(points[i]);
        }

        var center = Vector3.zero;
        for (var i = 0; i < aperture.Length; i++) center += points[i];
        points[aperture.Length] = center / aperture.Length;
        bounds.Encapsulate(points[aperture.Length]);
        for (var i = 0; i < points.Length; i++)
            uv[i] = new Vector2(
                Mathf.InverseLerp(bounds.min.x, bounds.max.x, points[i].x),
                Mathf.InverseLerp(bounds.min.y, bounds.max.y, points[i].y));

        var triangles = new int[aperture.Length * 3];
        for (var i = 0; i < aperture.Length; i++)
        {
            var triangle = i * 3;
            triangles[triangle] = i;
            triangles[triangle + 1] = (i + 1) % aperture.Length;
            triangles[triangle + 2] = aperture.Length;
        }

        var generated = new Mesh
        {
            name = "T1BusRearGlass",
            indexFormat = IndexFormat.UInt16,
            vertices = points,
            triangles = triangles,
            uv = uv,
        };
        generated.RecalculateNormals();
        generated.RecalculateBounds();
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(RearGlassMeshPath);
        if (saved == null)
        {
            AssetDatabase.CreateAsset(generated, RearGlassMeshPath);
            saved = generated;
        }
        else
        {
            EditorUtility.CopySerialized(generated, saved);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(saved);
        }

        var glassMaterial = root.GetComponentsInChildren<MeshRenderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials)
            .FirstOrDefault(material => material != null &&
                material.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0);
        if (glassMaterial == null)
            throw new InvalidOperationException("The imported T1 cabin-glass material is missing.");

        var rearGlass = new GameObject("T1BusRearGlass");
        rearGlass.transform.SetParent(root.transform, false);
        var filter = rearGlass.AddComponent<MeshFilter>();
        filter.sharedMesh = saved;
        var renderer = rearGlass.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = glassMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        Debug.Log(
            $"VolkswagenT1Bus: fitted source-shaped rear window vertices={saved.vertexCount}, " +
            $"width={bounds.size.x:0.00}m height={bounds.size.y:0.00}m.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var names = new[] { "Cylinder", "wheel fr", "wheel rl", "wheel rr" };
        var wheels = new List<Transform>();
        foreach (var name in names)
        {
            var wheel = FindTransform(model.transform, name);
            if (wheel != null) wheels.Add(wheel);
        }
        if (wheels.Count != 4)
            throw new InvalidOperationException($"Expected four T1Bus wheel roots; found {wheels.Count}.");
        var centers = new Dictionary<Transform, Vector3>();
        var averageZ = 0f;
        foreach (var wheel in wheels)
        {
            var tyre = FindTransformWithNameFragment(wheel, "_rubber_0") ??
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
            var tyre = FindTransformWithNameFragment(wheel, "_rubber_0")!;
            TryGetRendererBounds(tyre, out var sourceBounds);
            var target = WheelControllerPositions[controllerName];
            controller.localPosition = target;
            var mount = new GameObject("T1BusWheel" + corner);
            mount.transform.SetParent(root.transform, false);
            mount.transform.position = sourceBounds.center;
            wheel.SetParent(mount.transform, true);
            var tireRadius = front ? FrontTireRadius : RearTireRadius;
            var tireWidth = front ? FrontTireWidth : RearTireWidth;
            var geometrySuffix = (front ? "F" : "R") + (left ? "L" : "R");
            mount.transform.localScale = new Vector3(
                tireWidth / Mathf.Max(.001f, sourceBounds.size.x),
                tireRadius * 2f / Mathf.Max(.001f, sourceBounds.size.y),
                tireRadius * 2f / Mathf.Max(.001f, sourceBounds.size.z));
            mount.transform.localPosition = target;
            wheel.name = "Geometry_T1BusWheel_" + geometrySuffix;
            tyre.name = "Geometry_T1BusTire_" + geometrySuffix;
            var fixedPivot = new GameObject("T1BusFixedCaliper" + corner);
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
        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");
        var combines = new List<CombineInstance>();
        var materials = new List<Material>();
        var sourceRenderers = new List<MeshRenderer>();
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!renderer.enabled || IsWheelOrInteriorRenderer(renderer.transform)) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter?.sharedMesh == null) continue;
            var sourceMaterials = renderer.sharedMaterials;
            if (!sourceMaterials.Any(material => IsDeformableExteriorMaterial(material)))
                continue;

            var localToRoot = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var includedSubmesh = false;
            for (var subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
            {
                var materialIndex = Mathf.Min(subMesh, sourceMaterials.Length - 1);
                if (materialIndex < 0 ||
                    !IsDeformableExteriorMaterial(sourceMaterials[materialIndex]))
                    continue;
                combines.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    subMeshIndex = subMesh,
                    transform = localToRoot,
                });
                materials.Add(sourceMaterials[materialIndex]);
                includedSubmesh = true;
            }
            if (includedSubmesh)
                sourceRenderers.Add(renderer);
        }
        if (combines.Count == 0)
            throw new InvalidOperationException("No opaque T1 exterior panels were found for the damage mesh.");

        var bakedMesh = new Mesh
        {
            name = "T1BusDamageBody",
            indexFormat = combines.Sum(item => item.mesh.vertexCount) > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16,
        };
        bakedMesh.CombineMeshes(combines.ToArray(), false, true);
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

        var damageBody = new GameObject("T1BusDamageBody");
        damageBody.transform.SetParent(root.transform, false);
        var damageFilter = damageBody.AddComponent<MeshFilter>();
        damageFilter.sharedMesh = persistentMesh;
        var damageRenderer = damageBody.AddComponent<MeshRenderer>();
        damageRenderer.sharedMaterials = materials.ToArray();
        damageRenderer.shadowCastingMode = ShadowCastingMode.On;
        damageRenderer.receiveShadows = true;
        foreach (var sourceRenderer in sourceRenderers)
        {
            sourceRenderer.enabled = false;
            sourceRenderer.sharedMaterials = Array.Empty<Material>();
        }
        Debug.Log($"VolkswagenT1Bus: baked deformable shell panels={sourceRenderers.Count}, submeshes={materials.Count}, vertices={persistentMesh.vertexCount}.");
        return damageFilter;
    }

    private static bool IsDeformableExteriorMaterial(Material? material) =>
        material != null && !VolkswagenT1BusMaterials.IsTransparentMaterial(material) &&
        material.name.IndexOf("seat", StringComparison.OrdinalIgnoreCase) < 0 &&
        material.name.IndexOf("carpet", StringComparison.OrdinalIgnoreCase) < 0 &&
        material.name.IndexOf("fabric", StringComparison.OrdinalIgnoreCase) < 0;

    private static bool IsWheelOrInteriorRenderer(Transform transform)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.StartsWith("T1BusWheel", StringComparison.Ordinal) ||
                current.name.Equals("Cylinder", StringComparison.OrdinalIgnoreCase) ||
                current.name.Equals("wheel rl", StringComparison.OrdinalIgnoreCase) ||
                current.name.Equals("wheel fr", StringComparison.OrdinalIgnoreCase) ||
                current.name.Equals("wheel rr", StringComparison.OrdinalIgnoreCase) ||
                current.name.Equals("steering wheel", StringComparison.OrdinalIgnoreCase) ||
                current.name.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
                current.name.IndexOf("cabin", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
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

        var visual = FindTransform(renderer.transform.root, "T1BusVisual");
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

    private static VolkswagenT1BusMaterialFixResult PreparePrefabMaterialsWithoutRuntimeClones(GameObject root)
    {
        // Do NOT call VolkswagenT1BusMaterials.FixSolidMaterials() while authoring
        // the prefab. That method is a runtime per-instance material-cloning path.
        // Its Instantiate(Material) results are not persistent assets, so assigning
        // them to renderers before SaveAsPrefabAsset() serializes the model's material
        // references as null. Keep the manifest-generated material assets on the
        // prefab and only ensure the runtime controller component is present.
        if (root.GetComponent<VolkswagenT1BusMaterialController>() == null)
            root.AddComponent<VolkswagenT1BusMaterialController>();

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
                if (VolkswagenT1BusMaterials.IsTransparentMaterial(material))
                    transparentMaterials++;
                else
                    opaqueMaterials++;
            }
        }

        if (nullSlots != 0)
            throw new InvalidOperationException(
                $"T1Bus prefab contains {nullSlots} null material slot(s) before save; " +
                "runtime material cloning must not be used during prefab authoring.");

        Debug.Log(
            $"VolkswagenT1Bus: preserving persistent prefab materials " +
            $"unique={uniqueMaterials.Count}, opaque={opaqueMaterials}, " +
            $"transparent={transparentMaterials}, nullSlots={nullSlots}; " +
            "runtime cloning deferred until vehicle initialization.");

        return new VolkswagenT1BusMaterialFixResult(
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
            material.SetFloat("_Metallic", VolkswagenT1BusMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", VolkswagenT1BusMaterials.RimSmoothness);
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

    private static bool T1BusPowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateT1BusPowerCurve();
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

    private static void AssignT1BusSourceMaterials(GameObject model)
    {
        EnsureAssetFolder(MaterialFolder);
        var replacements = new Dictionary<Material, Material>();
        var rendererCount = 0;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter?.sharedMesh == null) continue;
            rendererCount++;
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                    throw new InvalidOperationException($"T1 GLB renderer '{renderer.name}' has a null material.");
                if (!replacements.TryGetValue(source, out var persistent))
                {
                    var sourceName = source.name;
                    var lower = sourceName.ToLowerInvariant();
                    var assetName = lower.Contains("body_1")
                        ? "VolkswagenT1Bus_BA_VehiclePaint_body_1"
                        : lower.Contains("body_2")
                            ? "VolkswagenT1Bus_FactoryCream_body_2"
                            : "VolkswagenT1Bus_" + SanitizeAssetName(sourceName);
                    var assetPath = MaterialFolder + "/" + assetName + ".mat";
                    persistent = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                    if (persistent == null)
                    {
                        persistent = new Material(source) { name = assetName };
                        AssetDatabase.CreateAsset(persistent, assetPath);
                    }
                    else
                    {
                        persistent.CopyPropertiesFromMaterial(source);
                        persistent.shader = source.shader;
                        persistent.name = assetName;
                    }
                    VolkswagenT1BusMaterials.NormalizeImportedMaterial(persistent);
                    persistent.name = assetName;
                    if (lower.Contains("body_1"))
                    {
                        if (persistent.HasProperty("_Metallic")) persistent.SetFloat("_Metallic", 0.06f);
                        if (persistent.HasProperty("_Smoothness")) persistent.SetFloat("_Smoothness", 0.78f);
                    }
                    else if (lower.Contains("body_2"))
                    {
                        if (persistent.HasProperty("_Metallic")) persistent.SetFloat("_Metallic", 0.02f);
                        if (persistent.HasProperty("_Smoothness")) persistent.SetFloat("_Smoothness", 0.58f);
                    }
                    else if (lower.Contains("chrome"))
                    {
                        if (persistent.HasProperty("_Metallic")) persistent.SetFloat("_Metallic", 0.86f);
                        if (persistent.HasProperty("_Smoothness")) persistent.SetFloat("_Smoothness", 0.90f);
                    }
                    else if (lower.Contains("white_wall"))
                    {
                        var wall = new Color(.78f, .75f, .69f, 1f);
                        if (persistent.HasProperty("_BaseColor")) persistent.SetColor("_BaseColor", wall);
                        if (persistent.HasProperty("_Color")) persistent.SetColor("_Color", wall);
                        if (persistent.HasProperty("_Metallic")) persistent.SetFloat("_Metallic", 0f);
                        if (persistent.HasProperty("_Smoothness")) persistent.SetFloat("_Smoothness", .24f);
                    }
                    else if (lower.Contains("headlights") || lower.Contains("tail_light"))
                    {
                        if (persistent.HasProperty("_EmissiveColor")) persistent.SetColor("_EmissiveColor", Color.black);
                        if (persistent.HasProperty("_EmissionColor")) persistent.SetColor("_EmissionColor", Color.black);
                        persistent.DisableKeyword("_EMISSION");
                    }
                    EditorUtility.SetDirty(persistent);
                    replacements.Add(source, persistent);
                }
                materials[index] = persistent;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
        var nullSlots = 0;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material == null) nullSlots++;
        if (nullSlots != 0)
            throw new InvalidOperationException($"T1 GLB material conversion left {nullSlots} null slots.");
        Debug.Log($"VolkswagenT1Bus: converted and retained GLB materials on {rendererCount} mesh renderers; red body paint remains repaintable and cream roof remains factory trim.");
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

        manifest.ModId = "Volkswagen_T1_Bus";
        manifest.DisplayName = "Volkswagen T1 Bus";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "volkswagen_t1_bus.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("T1Bus manifest references could not be assigned.");
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
            var isWheel = false;
            for (var current = renderer.transform; current != null && current != root; current = current.parent)
            {
                var name = current.name;
                if (name.StartsWith("T1BusWheel", StringComparison.Ordinal) ||
                    name.Equals("Cylinder", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("wheel fr", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("wheel rl", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("wheel rr", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("T1BusLightSources", StringComparison.Ordinal))
                {
                    isWheel = true;
                    break;
                }
            }
            if (isWheel)
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

    private static bool HasAncestorNameFragment(Transform transform, string fragment)
    {
        for (var current = transform; current != null; current = current.parent)
            if (current.name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static bool TryGetT1BusRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!VolkswagenT1BusMaterials.IsT1BusRenderer(renderer.transform))
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

    private static string ReadString(SerializedProperty? property) =>
        property?.propertyType == SerializedPropertyType.String ? property.stringValue : string.Empty;

    private static bool IsFalseOrMissing(SerializedProperty? property) =>
        property == null ||
        (property.propertyType == SerializedPropertyType.Boolean && !property.boolValue);

    private static string DescribeBool(SerializedProperty? property) =>
        property == null
            ? "<absent>"
            : property.propertyType == SerializedPropertyType.Boolean
                ? property.boolValue.ToString()
                : "<" + property.propertyType + ">";

    private static void RequireBuild(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("T1Bus bundle verification failed: " + message);
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

