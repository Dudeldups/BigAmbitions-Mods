#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class McLarenSennaSetup
{
    private const string ModRoot = "Assets/Mods/McLaren_Senna";
    private const string TemplatePrefabPath = ModRoot + "/McLarenSennaTemplate.prefab";
    
    private const string ModelPath = ModRoot + "/Models/2019_mclaren_senna.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/McLarenDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/McLarenSenna.asset";
    private const string VehiclePrefabPath = ModRoot + "/McLarenSenna.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/McLarenSenna.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/mclarensenna.unity3d";
    private const string VehicleTypeName =
        "mclarensenna-vehicle:vehicletype_mclarensenna";
    private const float TargetLength = 4.744f;
    private const float TargetWidth = 2.153f;
    private const float TargetHeight = 1.195f;
    private const float FrontTrack = 1.645f;
    private const float RearTrack = 1.610f;
    private const float Wheelbase = 2.670f;
    private const float FrontTireRadius = 0.32705f;
    private const float RearTireRadius = 0.34850f;
    private const float FrontTireWidth = 0.245f;
    private const float RearTireWidth = 0.315f;
    private const float WheelInset = 0f;
    private const float WheelCenterRideHeightOffset = 0.060f;
    private const float VehicleLinearDrag = 0.035f;
    private const float VehicleBrakeForce = McLarenSennaPowertrainCalibration.LowSpeedBrakeTorque;
    private const float BrakeMaxTorque = McLarenSennaPowertrainCalibration.BrakeTorque;
    private const float FrontForwardGrip = 0.98f;
    private const float RearForwardGrip = 1.08f;
    private const float FrontForwardStiffness = 1.22f;
    private const float RearForwardStiffness = 1.32f;
    private const float TireFrictionCircleStrength = 1.02f;
    private const float AntiRollBarForce = 10000f;
    private const float FrontSuspensionTravel = 0.065f;
    private const float RearSuspensionTravel = 0.070f;
    private const float DeformationStrength = 0.17f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.03f, -0.18f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f + WheelInset, FrontTireRadius + WheelCenterRideHeightOffset - .010f, Wheelbase * 0.5f - 0.18f) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f - WheelInset, FrontTireRadius + WheelCenterRideHeightOffset - .010f, Wheelbase * 0.5f - 0.18f) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f + WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f - 0.175f) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f - WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f - 0.175f) },
        };
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.49f, 1.61f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.78f, 0.42f, 1.02f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.48f, -1.70f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.78f, 0.42f, 1.02f);

    private static readonly float[] SennaGears =
    {
        -2.8f,
        0f,
        3.982f,
        2.612f,
        1.905f,
        1.479f,
        1.160f,
        0.906f,
        0.686f,
    };

    private static AnimationCurve CreateSennaPowerCurve() =>
        McLarenSennaPowertrainCalibration.CreatePowerCurve();

    [MenuItem("Big Ambitions Mods/Setup McLaren Senna")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "McLarenSenna setup complete: fitted the supplied Senna model with four " +
            "independent wheel visuals, seven-speed SSG, rear-wheel drive, turbo V8 audio, " +
            "functional lamp geometry, colorable body/calipers, and corrected glass materials.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    public static void RepairAppearanceAndBuild()
    {
        var root = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);
        try
        {
            McLarenSennaAppearanceSetup.Prepare(root);
            McLarenSennaWashSetup.Author(root);
            EnsureSpawnAppearance(root);
            ConfigureRendererReferences(root);
            MarkMaterialsDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ModAssetBundleCli.BuildForMod();
        McLarenSennaGeometrySetup.VerifyStoredAndBuilt();
    }

    public static void RepairPrefabAndBuild()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var platformManifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        if (platformManifest == null) throw new InvalidOperationException("McLaren mod manifest is missing.");
        platformManifest.TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;
        EditorUtility.SetDirty(platformManifest);
        ConfigureVehicleTypePerformance();
        var root = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);
        try
        {
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigurePowertrain(root);
            
            RepairStaticWheelVisuals(root);
            McLarenSennaGeometrySetup.RepairDriverAnchors(root);
            McLarenSennaGeometrySetup.RepairHeadlightBeams(root);
            McLarenSennaAppearanceSetup.Prepare(root);
            McLarenSennaWashSetup.Author(root);
            EnsureSpawnAppearance(root);
            ConfigureRendererReferences(root);
            MarkMaterialsDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ModAssetBundleCli.BuildForMod();
        McLarenSennaGeometrySetup.VerifyStoredAndBuilt();
    }

    public static void VerifyFeedbackConfiguration(GameObject prefab)
    {
        if (prefab.GetComponents<McLarenSennaSpawnAppearance>().Length != 1)
            throw new InvalidOperationException("Senna parked spawn appearance initializer is missing or duplicated.");
        foreach (var pair in WheelControllerPositions)
        {
            var controller = FindTransform(prefab.transform, pair.Key);
            var corner = pair.Key.Replace("_WheelController", "");
            foreach (var node in new[] { controller, FindTransform(prefab.transform, "McLarenWheel" + corner), FindTransform(prefab.transform, "McLarenFixedCaliper" + corner) })
                if (node == null || Vector3.Distance(prefab.transform.InverseTransformPoint(node.position), pair.Value) > .0001f)
                    throw new InvalidOperationException("Senna wheel/contact/caliper position mismatch: " + pair.Key);
        }
        var physics = prefab.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        var configured = physics.powertrain.engine;
        if (!configured.stallingEnabled || !configured.autoStartOnThrottle || configured.inertia < .19f ||
            configured.powerCurve.Evaluate(configured.idleRPM / configured.revLimiterRPM) > .04f ||
            physics.powertrain.clutch.slipTorque < 1200f)
            throw new InvalidOperationException("Senna low-RPM launch/restart configuration is invalid.");
        foreach (var material in prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct())
            if (material != null && material.name.Contains("BadgeA") &&
                (material.GetFloat("_SurfaceType") != 0f || material.GetFloat("_ZWrite") != 1f ||
                 material.GetFloat("_AlphaCutoffEnable") != 1f || !material.IsKeywordEnabled("_ALPHATEST_ON")))
                throw new InvalidOperationException("Senna badge alpha/depth state invalid: " + material.name);
        var seat = FindTransform(prefab.transform, "McLarenDriverSeat");
        var steering = FindTransform(prefab.transform, "McLarenSteeringAnchor");
        if (Mathf.Abs((steering.localPosition.z - seat.localPosition.z) - .39f) > .0001f)
            throw new InvalidOperationException("Senna driver seat rearward offset is invalid.");
        McLarenSennaWheelGeometry.Verify(prefab);
        McLarenSennaGeometrySetup.VerifyHeadlightBeams(prefab);
        if (Mathf.Abs(physics.steering.maximumSteerAngle - McLarenSennaPowertrainCalibration.SteeringAngle) > .001f)
            throw new InvalidOperationException("Senna low-speed steering calibration differs from VehicleType.");
        McLarenSennaWashSetup.VerifyBindings(prefab);
        VerifyNativeProfiles(physics);
        VerifyPerformanceAndAudio(physics);
        VerifyNativeStallRecovery(configured);
        Debug.Log("Senna feedback configuration verified: wheel/contact/caliper positions, seat, badge cutout and native stall recovery.");
    }

    private static void VerifyPerformanceAndAudio(NWH.VehiclePhysics2.VehicleController physics)
    {
        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        if (Mathf.Abs(transmission.UpshiftRPM - McLarenSennaPowertrainCalibration.UpshiftRpm) > .1f ||
            Mathf.Abs(transmission.DownshiftRPM - McLarenSennaPowertrainCalibration.DownshiftRpm) > .1f ||
            Mathf.Abs(transmission.postShiftBan - McLarenSennaPowertrainCalibration.PostShiftBan) > .001f ||
            Mathf.Abs(physics.brakes.maxTorque - McLarenSennaPowertrainCalibration.BrakeTorque) > .1f)
            throw new InvalidOperationException("Senna shift hysteresis/brake calibration mismatch.");
        for (var i = 2; i < SennaGears.Length - 1; i++)
            if (transmission.UpshiftRPM * SennaGears[i+1] / SennaGears[i] <= transmission.DownshiftRPM + 300f)
                throw new InvalidOperationException("Senna upshift falls into the downshift range.");
        for (var rpm = 1100f; rpm <= 8500f; rpm += 5f)
        {
            var power = engine.powerCurve.Evaluate(rpm / 8500f) * engine.maxPower;
            var torque = power * 30000f / (Mathf.PI * rpm);
            if (power > 588.1f || torque > 800.1f) throw new InvalidOperationException("Senna power curve exceeds manufacturer power/torque.");
        }
        if (Mathf.Abs(engine.powerCurve.Evaluate(7250f / 8500f) * engine.maxPower - 588f) > .1f)
            throw new InvalidOperationException("Senna 7,250 RPM peak power is missing.");
        var host = new GameObject("SennaDrivelineFixture");
        host.SetActive(false);
        var fixture = host.AddComponent<NWH.VehiclePhysics2.VehicleController>();
        var binding = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        try
        {
            var calibration = host.AddComponent<McLarenSennaPowertrainCalibration>();
            var type = typeof(McLarenSennaPowertrainCalibration);
            type.GetMethod("Initialize", binding).Invoke(calibration, new object[] { fixture });
            type.GetMethod("Initialize", binding).Invoke(calibration, new object[] { fixture });
            if (fixture.powertrain.engine.powerModifiers.Count != 1 || fixture.brakes.brakeTorqueModifiers.Count != 1 ||
                Mathf.Abs(1.25f * fixture.powertrain.engine.powerModifiers[0]() - .25f - McLarenSennaPowertrainCalibration.DrivelineEfficiency * McLarenSennaPowertrainCalibration.GameForceCalibration) > .0001f)
                throw new InvalidOperationException("Driveline modifier duplicated or compensated native losses incorrectly.");
            type.GetMethod("OnDisable", binding).Invoke(calibration, null);
            if (fixture.powertrain.engine.powerModifiers.Count != 0 || fixture.brakes.brakeTorqueModifiers.Count != 0) throw new InvalidOperationException("Driveline modifier leaked on disable.");
            type.GetMethod("OnEnable", binding).Invoke(calibration, null);
            type.GetMethod("OnDestroy", binding).Invoke(calibration, null);
            UnityEngine.Object.DestroyImmediate(calibration);
            if (fixture.powertrain.engine.powerModifiers.Count != 0 || fixture.brakes.brakeTorqueModifiers.Count != 0) throw new InvalidOperationException("Driveline modifier leaked on unload.");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
        if (Mathf.Abs(McLarenSennaPowertrainCalibration.BrakeMultiplier(100f) * McLarenSennaPowertrainCalibration.BrakeTorque - McLarenSennaPowertrainCalibration.LowSpeedBrakeTorque) > .1f ||
            Mathf.Abs(McLarenSennaPowertrainCalibration.BrakeMultiplier(200f) * McLarenSennaPowertrainCalibration.BrakeTorque - McLarenSennaPowertrainCalibration.BrakeTorque) > .1f ||
            McLarenSennaPowertrainCalibration.BrakeMultiplier(300f) > 1f)
            throw new InvalidOperationException("Senna speed-dependent braking is outside calibrated limits.");
        var previousLaunch = 0f;
        for (var speed = 0f; speed <= 335f; speed += 1f)
        {
            var launch = McLarenSennaPowertrainCalibration.LaunchTorqueMultiplier(speed, 1f, 1);
            if (launch < .65f || launch > 1f || launch < previousLaunch ||
                McLarenSennaPowertrainCalibration.LaunchTorqueMultiplier(speed, .5f, 1) != 1f ||
                McLarenSennaPowertrainCalibration.LaunchTorqueMultiplier(speed, 1f, -1) != 1f ||
                (speed >= 120f && launch != 1f)) throw new InvalidOperationException("Senna launch torque envelope is unsafe or discontinuous.");
            previousLaunch = launch;
        }
        var previousHighSpeed = 1f;
        for (var speed = 0f; speed <= 335f; speed += 1f)
        {
            var highSpeed = McLarenSennaPowertrainCalibration.HighSpeedTorqueMultiplier(speed, 1);
            if (highSpeed < McLarenSennaPowertrainCalibration.HighSpeedTorqueFloor || highSpeed > previousHighSpeed ||
                (speed <= 200f && highSpeed != 1f) ||
                (speed >= 300f && Mathf.Abs(highSpeed - McLarenSennaPowertrainCalibration.HighSpeedTorqueFloor) > .00001f) ||
                McLarenSennaPowertrainCalibration.HighSpeedTorqueMultiplier(speed, -1) != 1f ||
                McLarenSennaPowertrainCalibration.HighSpeedTorqueMultiplier(speed, 0) != 1f)
                throw new InvalidOperationException("Senna high-speed torque envelope is outside its calibrated bounds.");
            previousHighSpeed = highSpeed;
        }
        var audio = typeof(McLarenSennaRuntime).Assembly.GetType("McLarenSennaAudioModel", true);
        var target = audio.GetMethod("TargetHz", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var volume = audio.GetMethod("EngineVolume", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        foreach (var throttle in new[] { 0f, .25f, .5f, .75f, 1f })
            if (Mathf.Abs((float)volume.Invoke(null, new object[] { throttle }) - (.44f + .46f*throttle)*.88f) > .00001f)
                throw new InvalidOperationException("Senna driving audio reduction is inconsistent.");
        var previous = 0f;
        for (var rpm = 1100f; rpm <= 8500f; rpm += 25f)
        {
            var normalized = (rpm - 1100f) / 7400f;
            var hz = (float)target.Invoke(null, new object[] { normalized });
            if (hz < previous || hz > 220.001f ||
                (rpm <= 6000f && Mathf.Abs(hz - (float)(50d * Math.Pow(6.4d, normalized))) > .001f))
                throw new InvalidOperationException("Senna upper audio compression changes the validated lower range or is discontinuous.");
            previous = hz;
        }
        Debug.Log("Senna performance/audio verified: 588 kW @ 7250, <=800 Nm, safe seven-gear hysteresis, 2140..3380 Nm native brake modifier, unchanged <=6000 RPM tone, upper carrier <=220 Hz.");
    }

    private static void VerifyNativeStallRecovery(NWH.VehiclePhysics2.Powertrain.EngineComponent configured)
    {
        // Exercise the installed NWH solver's actual IsRunning/IsStalled and
        // autoStartOnThrottle transitions, without instantiating a game Car or
        // changing player, fuel, damage, save data or Unity play mode.
        var host = new GameObject("McLarenNativeEngineVerification");
        host.SetActive(false);
        try
        {
            var physics = host.AddComponent<NWH.VehiclePhysics2.VehicleController>();
            var engine = physics.powertrain.engine;
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(configured), engine);
            engine.VC_SetVehicleController(physics);
            engine.outputNameHash = 0; // Isolate lifecycle from wheel/load integration.
            engine.flyingStartEnabled = true; // Synchronous native starter for this editor fixture.
            engine.ignition = true;
            engine.canRun = true;
            physics.input.VC_SetVehicleController(physics);
            physics.input.autoSetInput = false;
            physics.input.swapInputInReverse = false;
            physics.input.Throttle = 0f;
            physics.input.VC_Update();
            var starts = 0;
            engine.onStart.AddListener(() => starts++);
            engine.outputAngularVelocity = engine.idleRPM * Mathf.PI / 30f;
            engine.IntegrateDownwards(.02f);
            physics.input.Throttle = 1f;
            physics.input.VC_Update();
            // Reproduce the previous failure: with stalls disabled, NWH keeps
            // reporting a running engine at zero RPM and does not auto-start.
            engine.stallingEnabled = false;
            engine.outputAngularVelocity = 0f;
            for (var tick = 0; tick < 3; tick++) engine.IntegrateDownwards(.02f);
            if (!engine.IsRunning || engine.OutputRPM != 0f || starts != 0)
                throw new InvalidOperationException("Zero-RPM baseline did not reproduce.");
            engine.stallingEnabled = configured.stallingEnabled;
            engine.outputAngularVelocity = engine.idleRPM * Mathf.PI / 30f;
            engine.IntegrateDownwards(.02f);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                engine.outputAngularVelocity = 0f;
                engine.IntegrateDownwards(.02f);
                if (engine.IsRunning || !engine.IsStalled)
                    throw new InvalidOperationException("NWH did not detect zero-RPM stall.");
                engine.IntegrateDownwards(.02f);
                if (!engine.IsRunning || engine.IsStalled || engine.OutputRPM < engine.idleRPM * .99f)
                    throw new InvalidOperationException("NWH automatic stall restart failed.");
            }
            if (starts != 3) throw new InvalidOperationException("Unexpected native restart count: " + starts);
            engine.canRun = false;
            engine.ignition = false;
            engine.outputAngularVelocity = 0f;
            engine.IntegrateDownwards(.02f);
            if (starts != 3 || engine.IsRunning) throw new InvalidOperationException("Restart bypassed native canRun safety.");
            Debug.Log("Senna native NWH engine recovery verified: three zero-RPM restarts, canRun safety retained.");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    public static void VerifyPlatformBundleManifests()
    {
        var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        var requiredPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;
        if (manifest == null || (manifest.TargetPlatforms & requiredPlatforms) != requiredPlatforms)
            throw new InvalidOperationException("McLaren manifest must build Windows and Mac bundles.");
        foreach (var platform in new[] { "Windows", "Mac" })
        {
            var path = ModRoot + "/AssetBundles/" + platform + "/mclarensenna.unity3d";
            if (!System.IO.File.Exists(path) || !System.IO.File.Exists(path + ".manifest"))
                throw new InvalidOperationException("McLaren platform bundle is missing: " + platform);
            var text = System.IO.File.ReadAllText(path + ".manifest");
            if (!text.Contains("guid: 769c37c828f64c4e9a2fdb90d1b3edfd"))
                throw new InvalidOperationException("McLaren " + platform + " bundle lacks parked spawn appearance initialization.");
            // Same five NWH native script references restored by the SDK's
            // 12 October Ferrari/Koenigsegg macOS correction, plus two managed types.
            foreach (var id in new[] { -333503815, 417210127, -221227779, 45295712, -1100654092 })
                if (!text.Contains("fileID: " + id + ", guid: 4384b44565f83c96ced3259b77f79fb6"))
                    throw new InvalidOperationException("McLaren " + platform + " bundle lacks native NWH script " + id);
            foreach (var type in new[] { "NWH.VehiclePhysics2.StateDefinition", "NWH.VehiclePhysics2.GroundDetection.SurfaceMap" })
                if (!text.Contains("ClassName: " + type))
                    throw new InvalidOperationException("McLaren " + platform + " bundle lacks " + type);
            if (!BuildPipeline.GetCRCForAssetBundle(path, out var crc) || !text.Contains("CRC: " + crc))
                throw new InvalidOperationException("McLaren " + platform + " bundle CRC differs from its manifest.");
            Debug.Log("Senna " + platform + " bundle manifest verified: five native NWH scripts, StateDefinition, SurfaceMap and CRC=" + crc + ".");
        }
    }

    private static void VerifyNativeProfiles(NWH.VehiclePhysics2.VehicleController physics)
    {
        var serialized = new SerializedObject(physics);
        var ground = serialized.FindProperty("groundDetection.groundDetectionPreset")?.objectReferenceValue;
        var states = serialized.FindProperty("stateSettings")?.objectReferenceValue;
        if (ground == null || states == null) throw new InvalidOperationException("Senna native ground/state profiles are missing from source or bundle.");
        var groundData = new SerializedObject(ground);
        if (groundData.FindProperty("fallbackSurfacePreset")?.objectReferenceValue == null)
            throw new InvalidOperationException("Senna native tire surface fallback is missing.");
        var lods = new SerializedObject(states).FindProperty("LODs");
        if (lods == null || lods.arraySize != 3)
            throw new InvalidOperationException("Senna native state LOD profiles are incomplete.");
        for (var i = 0; i < lods.arraySize; i++)
            if (lods.GetArrayElementAtIndex(i).objectReferenceValue == null)
                throw new InvalidOperationException("Senna native state LOD reference is missing: " + i);
        Debug.Log("Senna native dependency profiles verified: ground/tire surface fallback and three state LODs.");
    }

    public static void VerifyBuiltBundle()
    {
        VerifyNoForeignVehicleDependencies(VehiclePrefabPath);
        VerifyNoForeignVehicleDependencies(VehicleAssetPath);
        VerifyNoMissingScripts(VehiclePrefabPath);
        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");

        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException("Bundle is missing its VehicleType or prefab.");

            McLarenSennaGeometrySetup.Verify(prefab);
            VerifyFeedbackConfiguration(prefab);
            var serialized = new SerializedObject(vehicleType);
            var price = ReadNumber(serialized.FindProperty("price"));
            var maxSpeed = ReadNumber(serialized.FindProperty("maxSpeed"));
            var enginePower = ReadNumber(serialized.FindProperty("enginePower"));
            var brakeForce = ReadNumber(serialized.FindProperty("brakeForce"));
            var wheelControllers = 0;
            var missingComponents = 0;
            var clutchEngagementRpm = 0f;
            var engineIdleRpm = 0f;
            var hasPowertrainController = false;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                foreach (var component in transform.GetComponents<Component>())
                {
                    if (component == null)
                    {
                        missingComponents++;
                        continue;
                    }
                    if (!string.Equals(component.GetType().FullName,
                            "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
                        continue;

                    var powertrain = new SerializedObject(component);
                    clutchEngagementRpm = ReadNumber(FindRelativeProperty(
                        powertrain, "powertrain.clutch.engagementRPM"));
                    engineIdleRpm = ReadNumber(FindRelativeProperty(
                        powertrain, "powertrain.engine.idleRPM"));
                    hasPowertrainController = true;
                }
            }

            foreach (var pair in WheelControllerPositions)
            {
                var controller = FindTransform(prefab.transform, pair.Key);
                if (controller == null)
                    throw new InvalidOperationException($"Wheel controller '{pair.Key}' is missing.");
                var wheelController = controller.GetComponent("WheelController");
                if (wheelController == null)
                    throw new InvalidOperationException($"NWH wheel controller on '{pair.Key}' is missing.");
                wheelControllers++;
            }

            if (FindTransform(prefab.transform, "McLarenVisual") == null ||
                FindTransform(prefab.transform, "BodyCollider") == null ||
                prefab.GetComponent<Rigidbody>() == null ||
                !HasComponentNamed(prefab, "VehicleDeformationController") ||
                wheelControllers != 4 ||
                Math.Abs(price - 958966f) > 0.5f ||
                Math.Abs(maxSpeed - 335f) > 0.5f ||
                Math.Abs(enginePower - 588f) > 0.5f ||
                Math.Abs(brakeForce - VehicleBrakeForce) > 0.5f ||
                missingComponents > 0 ||
                !hasPowertrainController ||
                clutchEngagementRpm <= engineIdleRpm)
            {
                throw new InvalidOperationException(
                    $"Senna verification failed: price={price}, maxSpeed={maxSpeed}, " +
                    $"power={enginePower}kW, brakeForce={brakeForce}, " +
                    $"wheelControllers={wheelControllers}, missingComponents={missingComponents}, " +
                    $"clutchRPM={clutchEngagementRpm}, idleRPM={engineIdleRpm}.");
            }

            Debug.Log(
                $"McLarenSenna bundle verified: price={price}, topSpeed={maxSpeed}km/h, " +
                $"power={enginePower}kW, brakeForce={brakeForce}, wheelControllers=4, " +
                $"clutch={clutchEngagementRpm:0}rpm/idle={engineIdleRpm:0}rpm, " +
                "RWD/7-speed setup and target-owned dependency graph present.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }
    private static UnityEngine.Object CreateVehicleType()
    {
        var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (target == null)
            throw new InvalidOperationException("McLaren Senna VehicleType template is missing.");

        target.name = "McLarenSenna";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 958966f);
        SetNumber(serialized, "maxFuel", 72f);
        SetNumber(serialized, "maxCargoCapacity", 2f);
        SetNumber(serialized, "maxSpeed", 335f);
        SetNumber(serialized, "enginePower", 588f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", McLarenSennaPowertrainCalibration.SteeringAngle);
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

    private static bool HasComponentNamed(GameObject root, string typeName)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (component != null && string.Equals(
                    component.GetType().Name, typeName, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void ConfigureVehicleTypePerformance()
    {
        var vehicleType = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (vehicleType == null)
            throw new InvalidOperationException("McLaren Senna VehicleType asset is missing.");

        var serialized = new SerializedObject(vehicleType);
        SetNumber(serialized, "maxSpeed", 335f);
        SetNumber(serialized, "enginePower", 588f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", McLarenSennaPowertrainCalibration.SteeringAngle);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(vehicleType);
    }
    private static void EnsureSpawnAppearance(GameObject root)
    {
        if (root.GetComponent<McLarenSennaSpawnAppearance>() == null)
            root.AddComponent<McLarenSennaSpawnAppearance>();
    }

    private static void CreateVehiclePrefab(UnityEngine.Object vehicleType)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("McLaren-owned vehicle template prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("McLaren Senna GLB did not import as a prefab.");

        var root = PrefabUtility.InstantiatePrefab(source) as GameObject ??
                   throw new InvalidOperationException("Could not instantiate the McLaren template.");
        PrefabUtility.UnpackPrefabInstance(
            root,
            PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction);
        root.name = "McLarenSenna";
        try
        {
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the McLaren Senna model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "McLarenVisual";
            RemoveModelLights(modelInstance);
            RemoveModelCameras(modelInstance);
            RemoveAuxiliaryModelGeometry(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureExitMarkers(root, modelInstance);
            AttachWheelVisuals(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            McLarenSennaAppearanceSetup.Prepare(root);
            var materialFix = McLarenSennaMaterials.FixSolidMaterials(root);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            McLarenSennaGeometrySetup.Author(root);
            AssignPersistentMaterials(root);
            McLarenSennaGeometrySetup.Persist(root);
            MarkMaterialsDirty(root);
            McLarenSennaWashSetup.Author(root);
            EnsureSpawnAppearance(root);
            ConfigureRendererReferences(root);
            var missingScriptsRemoved = RemoveMissingScriptsRecursively(root);
            if (missingScriptsRemoved > 0)
                Debug.LogWarning(
                    $"McLarenSenna: removed {missingScriptsRemoved} missing-script component(s) " +
                    "from the generated target prefab.");

            Debug.Log(
                $"McLarenSenna: prepared target-owned prefab; renderers={materialFix.RendererCount}, " +
                $"transparentFixed={materialFix.TransparentMaterialsFixed}, " +
                $"cabinGlass={materialFix.CabinGlassRenderers}/" +
                $"reenabled={materialFix.CabinGlassRenderersReenabled}, " +
                $"rimSlots={rimMaterialsConfigured}, hdrpValidated={materialFix.MaterialsValidated}.");

            if (PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath) == null)
                throw new InvalidOperationException("Could not save the McLaren Senna vehicle prefab.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        VerifyNoForeignVehicleDependencies(VehiclePrefabPath);
        VerifyNoMissingScripts(VehiclePrefabPath);
    }

    private static int RemoveMissingScriptsRecursively(GameObject root)
    {
        var removed = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform != null)
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(
                    transform.gameObject);
        }
        return removed;
    }

    private static void VerifyNoMissingScripts(string prefabPath)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
            throw new InvalidOperationException(
                $"Could not reload generated prefab for missing-script verification: {prefabPath}.");

        try
        {
            var removed = RemoveMissingScriptsRecursively(root);
            if (removed > 0)
            {
                if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null)
                    throw new InvalidOperationException(
                        $"Could not save missing-script cleanup for '{prefabPath}'.");
                Debug.LogWarning(
                    $"McLarenSenna: removed {removed} missing-script component(s) " +
                    "during serialized prefab cleanup.");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
            throw new InvalidOperationException(
                $"Could not reload generated prefab after cleanup: {prefabPath}.");
        try
        {
            var remaining = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (transform != null)
                    remaining += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transform.gameObject);
            if (remaining > 0)
                throw new InvalidOperationException(
                    $"Generated McLaren prefab still contains {remaining} missing-script component(s).");
            Debug.Log("McLarenSenna prefab verified: no missing script components remain.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void RemoveModelLights(GameObject model)
    {
        foreach (var light in model.GetComponentsInChildren<Light>(true))
            if (light != null)
                UnityEngine.Object.DestroyImmediate(light.gameObject);
    }

    private static void RemoveModelCameras(GameObject model)
    {
        foreach (var camera in model.GetComponentsInChildren<Camera>(true))
            if (camera != null)
                UnityEngine.Object.DestroyImmediate(camera.gameObject);
    }

    private static void RemoveAuxiliaryModelGeometry(GameObject model)
    {
        var removed = 0;
        foreach (var transform in model.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, "Icosphere", StringComparison.OrdinalIgnoreCase))
                continue;
            UnityEngine.Object.DestroyImmediate(transform.gameObject);
            removed++;
        }

        Debug.Log($"McLarenSenna model import: removed auxiliary GLB meshes={removed}.");
    }
    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        var damage = root.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true) ??
                     throw new InvalidOperationException("Senna native damage handler is missing.");
        damage.damageIntensity = .45f; damage.decelerationThreshold = 500f;
        damage.collisionTimeout = .8f; damage.meshDeform = false;
        body.mass = 1309f;
        body.drag = VehicleLinearDrag;
        body.angularDrag = 1.45f;
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
            SetNumber(serialized, "baseMass", 1309f);
            SetNumber(serialized, "combinedMass", 1309f);
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
        if (colliders.Length < 2)
            throw new InvalidOperationException("Reference vehicle requires two body colliders.");

        colliders[0].center = new Vector3(0f, 0.32f, 0f);
        colliders[0].size = new Vector3(2.00f, 0.44f, 4.58f);
        colliders[1].center = new Vector3(0f, 0.76f, -0.08f);
        colliders[1].size = new Vector3(1.78f, 0.65f, 2.78f);
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
            var mount = FindTransform(root.transform, "McLarenWheel" + corner) ??
                        throw new InvalidOperationException($"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "McLarenFixedCaliper" + corner) ??
                          throw new InvalidOperationException($"Fixed caliper for '{corner}' is missing.");

            var translation = root.transform.TransformPoint(pair.Value) - mount.position;
            var caliperChildren = Enumerable.Range(0, caliper.childCount).Select(caliper.GetChild).ToArray();
            var childPositions = caliperChildren.Select(child => child.position + translation).ToArray();
            controller.localPosition = pair.Value;
            mount.SetParent(root.transform, true);
            mount.localPosition = pair.Value;
            // The original caliper pivot included a fitting translation. Center
            // its pivot while preserving that geometry offset and moving the
            // complete brake assembly by the same delta as the tire/contact.
            caliper.localPosition = pair.Value;
            for (var index = 0; index < caliperChildren.Length; index++)
                caliperChildren[index].position = childPositions[index];
            AssignWheelVisual(controller, mount.gameObject);
        }
        McLarenSennaWheelGeometry.Recalibrate(root);
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
            if (string.Equals(component.GetType().FullName,
                    "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
            {
                found = true;
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1600f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 300f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 400f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.20f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 588f);
                SetRelativeNumber(serialized, "powertrain.engine.engineLossPercent", 0.25f);
                SetRelativeBool(serialized, "powertrain.engine.autoStartOnThrottle", true);
                SetRelativeNumber(serialized, "powertrain.clutch.inertia", 0.02f);
                SetRelativeNumber(serialized, "powertrain.clutch.slipTorque", 1200f);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateSennaPowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 1100f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 8500f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.42f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", true);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.308f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 7f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.065f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", McLarenSennaPowertrainCalibration.DownshiftRpm);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", McLarenSennaPowertrainCalibration.UpshiftRpm);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.postShiftBan", McLarenSennaPowertrainCalibration.PostShiftBan);
                SetRelativeNumber(serialized, "brakes.maxTorque", BrakeMaxTorque);
                SetRelativeNumber(serialized, "steering.maximumSteerAngle", McLarenSennaPowertrainCalibration.SteeringAngle);

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
                        continue;
                    var bias = differential.FindPropertyRelative("biasAB");
                    if (bias?.propertyType != SerializedPropertyType.Float)
                        throw new InvalidOperationException("Center differential torque bias is missing.");
                    bias.floatValue = 1f;
                    rearDriveConfigured = true;
                }
                if (!rearDriveConfigured)
                    throw new InvalidOperationException("Center differential could not be configured for RWD.");

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = SennaGears.Length;
                for (var index = 0; index < SennaGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = SennaGears[index];
            }
            else if (string.Equals(component.GetType().Name,
                         "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 335f);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!found)
            throw new InvalidOperationException("NWH vehicle controller was not found on the McLaren template.");
    }
    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        // The supplied GLB has a 0.01 Sketchfab wrapper; resetting the imported
        // root scale restores its authored metre-scale dimensions.
        model.transform.localScale = Vector3.one;
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
            var wheelReference = FindTransformWithNameFragment(model.transform, "3DWheel Front L");
            var cabinReference = FindTransformWithNameFragment(model.transform, "Kit2_Interior_Geo");
            if (wheelReference == null || cabinReference == null ||
                !TryGetRendererBounds(wheelReference, out var wheelBounds) ||
                !TryGetRendererBounds(cabinReference, out var cabinBounds) ||
                cabinBounds.center.y <= wheelBounds.center.y)
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
            throw new InvalidOperationException("McLaren Senna model length could not be measured.");

        model.transform.localRotation = candidates[bestIndex];
        var front = FindTransformWithNameFragment(model.transform, "3DWheel Front L");
        var rear = FindTransformWithNameFragment(model.transform, "3DWheel Rear L");
        if (front == null || rear == null ||
            !TryGetRendererBounds(front, out var frontBounds) ||
            !TryGetRendererBounds(rear, out var rearBounds))
        {
            throw new InvalidOperationException("The four authored Senna wheel groups were not found.");
        }
        if (frontBounds.center.z < rearBounds.center.z)
            model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;

        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Oriented McLaren Senna bounds could not be measured.");
        // Keep world-axis normalization separate from the imported root rotation.
        // Otherwise an X rotation swaps the height/length scale axes.
        var importedChildren = new List<Transform>();
        foreach (Transform child in model.transform) importedChildren.Add(child);
        var orientation = new GameObject("McLarenModelOrientation");
        orientation.transform.SetParent(model.transform, false);
        var selectedRotation = model.transform.localRotation;
        model.transform.localRotation = Quaternion.identity;
        orientation.transform.localRotation = selectedRotation;
        foreach (var child in importedChildren) child.SetParent(orientation.transform, false);
        var scale = new Vector3(
            TargetWidth / bounds.size.x,
            TargetHeight / bounds.size.y,
            TargetLength / bounds.size.z);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled McLaren Senna bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final McLaren Senna bounds could not be measured.");
        Debug.Log(
            $"McLarenSenna: normalized supplied GLB scale={scale}, bounds={bounds.size}, " +
            $"center={bounds.center}; front wheel group z={frontBounds.center.z:F3}, " +
            $"rear wheel group z={rearBounds.center.z:F3}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var wheels = new List<Transform>();
        var calipers = new List<Transform>();
        var badges = new List<Transform>();
        foreach (var candidate in model.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name.StartsWith("3DWheel Front ", StringComparison.OrdinalIgnoreCase) ||
                candidate.name.StartsWith("3DWheel Rear ", StringComparison.OrdinalIgnoreCase))
            {
                wheels.Add(candidate);
            }
            else if (candidate.name.StartsWith("CalliperExotic", StringComparison.OrdinalIgnoreCase))
            {
                calipers.Add(candidate);
            }
            else if (candidate.name.StartsWith("CalliperBadge ", StringComparison.OrdinalIgnoreCase))
            {
                badges.Add(candidate);
            }
        }
        if (wheels.Count != 4 || calipers.Count != 4 || badges.Count != 4)
            throw new InvalidOperationException(
                $"Expected Senna wheel/caliper groups 4/4/4, found {wheels.Count}/{calipers.Count}/{badges.Count}.");

        foreach (var wheel in wheels)
        {
            if (!TryGetRendererBounds(wheel, out var sourceBounds) ||
                sourceBounds.size.x <= 0.001f || sourceBounds.size.y <= 0.001f ||
                sourceBounds.size.z <= 0.001f)
                throw new InvalidOperationException($"Wheel group '{wheel.name}' has invalid bounds.");

            var caliper = FindClosestPart(wheel, calipers) ??
                          throw new InvalidOperationException($"No Senna caliper group matches '{wheel.name}'.");
            var badge = FindClosestPart(wheel, badges) ??
                        throw new InvalidOperationException($"No Senna caliper badge matches '{wheel.name}'.");

            var isFront = wheel.name.IndexOf("Front", StringComparison.OrdinalIgnoreCase) >= 0;
            var isLeft = wheel.name.EndsWith(" L", StringComparison.OrdinalIgnoreCase);
            var corner = (isFront ? "Front" : "Rear") + (isLeft ? "Left" : "Right");
            var controllerName = corner + "_WheelController";
            var controller = FindTransform(root.transform, controllerName) ??
                             throw new InvalidOperationException($"Wheel controller '{controllerName}' is missing.");
            var targetCenter = WheelControllerPositions[controllerName];
            var targetCenterWorld = root.transform.TransformPoint(targetCenter);
            var fitTranslation = targetCenterWorld - sourceBounds.center;
            var radius = isFront ? FrontTireRadius : RearTireRadius;
            var width = isFront ? FrontTireWidth : RearTireWidth;
            var mount = new GameObject("McLarenWheel" + corner);
            mount.transform.SetParent(root.transform, false);
            mount.transform.localPosition = targetCenter;
            wheel.SetParent(mount.transform, true);
            wheel.name = "Geometry_McLarenWheel_" + (isFront ? "F" : "R") + (isLeft ? "L" : "R");
            mount.transform.localScale = new Vector3(
                width / sourceBounds.size.x,
                (radius * 2f) / sourceBounds.size.y,
                (radius * 2f) / sourceBounds.size.z);
            if (!TryGetRendererBounds(wheel, out var fittedBounds))
                throw new InvalidOperationException($"Wheel group '{wheel.name}' lost its renderers during fitting.");
            wheel.position += targetCenterWorld - fittedBounds.center;
            controller.localPosition = targetCenter;
            AssignWheelVisual(controller, mount);

            calipers.Remove(caliper);
            badges.Remove(badge);
            var fixedCaliper = new GameObject("McLarenFixedCaliper" + corner);
            fixedCaliper.transform.SetParent(root.transform, false);
            fixedCaliper.transform.localPosition = targetCenter;
            caliper.SetParent(fixedCaliper.transform, true);
            badge.SetParent(fixedCaliper.transform, true);
            caliper.position += fitTranslation;
            badge.position += fitTranslation;
            caliper.name = "SennaCalliperGeometry_" + corner;
            badge.name = "SennaCalliperBadge_" + corner;

            Debug.Log(
                $"McLarenSenna wheel fit: corner={corner}, authored={sourceBounds.center}, " +
                $"target={targetCenter}, sourceSize={sourceBounds.size}, " +
                $"tire={width:F3}x{radius * 2f:F3}m, fixedCaliper=true.");
        }
        McLarenSennaWheelGeometry.Recalibrate(root);
    }

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        SetLocalPosition(root, "Driverside", new Vector3(-1.38f, 0.1f, 0f));
        SetLocalPosition(root, "Passengerside", new Vector3(1.38f, 0.1f, 0f));
        Debug.Log("McLarenSenna: configured left-hand-drive exit markers with side-clearance recovery.");
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
        MeshRenderer? sourceRenderer = null;
        var largestArea = 0f;
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!HasMaterialMarker(renderer, "Paint") ||
                renderer.GetComponent<MeshFilter>()?.sharedMesh == null)
                continue;
            var bounds = renderer.bounds;
            var area = bounds.size.x * bounds.size.z;
            if (area <= largestArea)
                continue;
            largestArea = area;
            sourceRenderer = renderer;
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The Senna painted outer body mesh was not found.");

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");
        var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        bakedMesh.name = "McLarenDamageBody";
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
                var direction = sourceToRoot.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
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

        var damageBody = new GameObject("McLarenDamageBody") { layer = sourceRenderer.gameObject.layer };
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
        UnityEngine.Object.DestroyImmediate(sourceRenderer);
        UnityEngine.Object.DestroyImmediate(sourceFilter);
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
        material.name.IndexOf("Paint", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsCabinGlassRenderer(Transform transform) =>
        HasAncestorNameFragment(transform, "Kit2_Window_Geo") ||
        HasAncestorNameFragment(transform, "Kit2_WindowInside_Geo") ||
        HasAncestorNameFragment(transform, "windshield") ||
        HasAncestorNameFragment(transform, "doorglass") ||
        HasAncestorNameFragment(transform, "quarterglass") ||
        HasAncestorNameFragment(transform, "backlight_tint");

    private static bool IsInteriorAccentPaintMaterial(Material material) =>
        material.name.IndexOf("InteriorAccent", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRimMaterial(Material material) =>
        material.name.IndexOf("Wheel1A", StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!McLarenSennaMaterials.IsMcLarenRenderer(renderer.transform))
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
            throw new InvalidOperationException("The shared McLaren rim material is missing.");

        ApplyRimFinish(leftMaterial, McLarenSennaMaterials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!McLarenSennaMaterials.IsMcLarenRenderer(renderer.transform))
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

        if (configuredSlots == 0)
            throw new InvalidOperationException(
                $"Expected four McLaren rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", McLarenSennaMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", McLarenSennaMaterials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("Calliper", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool SennaPowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateSennaPowerCurve();
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
        material.name.IndexOf("InteriorAccent", StringComparison.OrdinalIgnoreCase) >= 0;

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
                    var transparent = McLarenSennaMaterials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"McLaren{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

        manifest.ModId = "McLarenSenna";
        manifest.DisplayName = "McLaren Senna";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "mclarensenna.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("McLaren manifest references could not be assigned.");
        EditorUtility.SetDirty(manifest);
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        // Renderer.bounds may retain the previous orientation in an editor setup
        // pass. Transform the mesh bounds explicitly after every rotation/scale.
        bounds = default;
        var found = false;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var local = filter.sharedMesh.bounds;
            for (var corner = 0; corner < 8; corner++)
            {
                var p = filter.transform.TransformPoint(local.center + Vector3.Scale(local.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                if (!found) { bounds = new Bounds(p, Vector3.zero); found = true; }
                else bounds.Encapsulate(p);
            }
        }
        return found;
    }

    private static bool TryGetModelBodyBounds(Transform root, out Bounds bounds) =>
        TryGetRendererBounds(root, out bounds);

    private static void VerifyNoForeignVehicleDependencies(string assetPath)
    {
        var forbiddenRoots = new[]
        {
            "Assets/Mods/AudiRS6R/",
            "Assets/Mods/Porsche_911_GT3_RS/",
            "Assets/Mods/BMW_M4_G82/",
        };
        foreach (var dependency in AssetDatabase.GetDependencies(assetPath, true))
        {
            if (string.IsNullOrEmpty(dependency))
                continue;
            foreach (var forbiddenRoot in forbiddenRoots)
            {
                if (!dependency.StartsWith(forbiddenRoot, StringComparison.OrdinalIgnoreCase))
                    continue;
                throw new InvalidOperationException(
                    $"Foreign vehicle dependency remains in '{assetPath}': '{dependency}'.");
            }
        }

        if (!assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            return;

        var root = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (ContainsForeignVehicleIdentity(transform.name))
                    throw new InvalidOperationException(
                        $"Foreign vehicle object name remains in '{assetPath}': '{transform.name}'.");
                foreach (var component in transform.GetComponents<Component>())
                {
                    if (component == null)
                        continue;
                    var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();
                    var enterChildren = true;
                    while (property.Next(enterChildren))
                    {
                        enterChildren = false;
                        if (property.propertyType == SerializedPropertyType.String &&
                            ContainsForeignVehicleIdentity(property.stringValue))
                        {
                            throw new InvalidOperationException(
                                $"Foreign vehicle string remains in '{assetPath}': " +
                                $"'{property.propertyPath}={property.stringValue}'.");
                        }
                    }
                }
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool ContainsForeignVehicleIdentity(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        var candidate = value!;
        return candidate.IndexOf("AudiRS6R", StringComparison.OrdinalIgnoreCase) >= 0 ||
               candidate.IndexOf("Porsche911GT3RS", StringComparison.OrdinalIgnoreCase) >= 0 ||
               candidate.IndexOf("BMW_M4_G82", StringComparison.OrdinalIgnoreCase) >= 0 ||
               candidate.IndexOf("Porsche_911_GT3_RS", StringComparison.OrdinalIgnoreCase) >= 0;
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

    private static bool TryGetMcLarenRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!McLarenSennaMaterials.IsMcLarenRenderer(renderer.transform))
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







