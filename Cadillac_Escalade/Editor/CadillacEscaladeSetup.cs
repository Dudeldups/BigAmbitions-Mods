#nullable enable
using System;
using System.Collections.Generic;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class CadillacEscaladeSetup
{
    private const string ModRoot = "Assets/Mods/Cadillac_Escalade";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/cadillac_escalade.glb";
    private const string LightOverlayModelPath = ModRoot + "/Models/CadillacLightOverlays.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string FrontDamageBodyMeshPath =
        MeshFolder + "/CadillacDamageBodyFront.asset";
    private const string RearDamageBodyMeshPath =
        MeshFolder + "/CadillacDamageBodyRear.asset";
    private const string VehicleAssetPath = ModRoot + "/CadillacEscalade.asset";
    private const string VehiclePrefabPath = ModRoot + "/CadillacEscalade.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/CadillacEscalade.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/cadillacescalade.unity3d";
    private const string VehicleTypeName =
        "cadillacescalade-vehicle:vehicletype_cadillacescalade";
    private const float TargetLength = 5.382f;
    // The supplied 2021 GLB includes both extended mirrors in its body bounds.
    private const float TargetVisualWidthIncludingMirrors = 2.45f;
    private const float TargetHeight = 1.948f;
    private const float Wheelbase = 3.064f;
    private const float BodyGroundClearance = 0.18f;
    private const float BodyVisualLowering = 0.10f;
    private const float WheelRadius = 0.408f;
    private const float WheelWidth = 0.285f;
    private const float VehicleMass = 2738f;
    private const float PeriodMsrp = 86195f;
    private const float FuelCapacityLitres = 91f;
    private const float RatedEnginePowerKw = 313f;
    private const float EngineRoadCalibrationPowerKw = 232f;
    private const float BrakeMaxTorque = 4000f;
    private const float BrakeActuationTime = 0.10f;
    private const float TireFrictionCircleStrength = 0.80f;
    private const float FrontLateralGrip = 0.54f;
    private const float RearLateralGrip = 0.58f;
    private const float AntiRollBarForce = 10500f;
    private const float FrontSuspensionTravel = 0.15f;
    private const float RearSuspensionTravel = 0.15f;
    private const float DeformationStrength = 0.13f;
    private const float DeformationRadius = 0.45f;
    private const float DeformationRandomness = 0.012f;
    private const float ExitMarkerOffset = 1.75f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.22f, -0.10f);
    private static readonly Vector3 LowerColliderCenter = new Vector3(0f, 0.30f, -0.05f);
    private static readonly Vector3 LowerColliderSize = new Vector3(1.94f, 0.50f, 5.12f);
    private static readonly Vector3 UpperColliderCenter = new Vector3(0f, 0.78f, -0.22f);
    private static readonly Vector3 UpperColliderSize = new Vector3(1.72f, 0.74f, 3.42f);

    private const string FrontDrlAnchor =
        "E:common_etches_light_etchings_common_001_common_etches_light_etchings_common_001_alpha_badges_002_E:alpha_badges_002_0";
    private const string FrontIndicatorAnchor =
        "E:common_headlights_etched_headlights_etched_common_001_common_headlights_etched_headlights_etched_common_001_front_lamps_etched_0_E:alpha_badges_002_0";
    private const string HeadlampAnchor =
        "E:common_high_beams_high_beam_common_001_common_high_beams_high_beam_common_001_lights_001_E:alpha_badges_002_0";
    private const string ReverseLightAnchor =
        "E:swb_bright_chrome_bright_chrome_swb_swb_bright_chrome_bright_chrome_swb_Chrome_Bright_003_SILVER_CHROME_0";
    private const string ThirdBrakeLightAnchor =
        "E:swb_chml_red_chml_red_swb_swb_chml_red_chml_red_swb_Glass_Red_Tint_E:Glass_Red_Tint_002_0";
    private const string RearLightAnchor =
        "E:swb_rear_etchings_rear_etchings_swb_swb_rear_etchings_rear_etchings_swb_light_etchings_E:alpha_badges_002_0";

    private static readonly string[] LightOverlayReferenceNames =
    {
        "CadillacEscalade_Light_DRL_FL",
        "CadillacEscalade_Light_DRL_FR",
        "CadillacEscalade_Light_BumperIndicator_FL",
        "CadillacEscalade_Light_BumperIndicator_FR",
        "CadillacEscalade_Light_FrontIndicator_FL",
        "CadillacEscalade_Light_FrontIndicator_FR",
        "CadillacEscalade_Light_Headlamps",
        "CadillacEscalade_Light_TailLights",
        "CadillacEscalade_Light_BrakeLights",
        "CadillacEscalade_Light_ThirdBrakeLight",
        "CadillacEscalade_Light_ReverseLights",
        "CadillacEscalade_Light_RearIndicator_RL",
        "CadillacEscalade_Light_RearIndicator_RR",
    };

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-0.865f, WheelRadius, 1.500f) },
            { "FrontRight_WheelController", new Vector3(0.865f, WheelRadius, 1.500f) },
            { "RearLeft_WheelController", new Vector3(-0.850f, WheelRadius, -1.446f) },
            { "RearRight_WheelController", new Vector3(0.850f, WheelRadius, -1.446f) },
        };

    private static readonly float[] EscaladeGears =
    {
        -4.87f,
        0f,
        4.70f,
        2.99f,
        2.15f,
        1.80f,
        1.52f,
        1.28f,
        1.00f,
        0.85f,
        0.69f,
        0.64f,
    };

    private static AnimationCurve CreateEscaladePowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.10f, 0.12f),
            new Keyframe(0.30f, 0.40f),
            new Keyframe(0.55f, 0.72f),
            new Keyframe(0.72f, 0.88f),
            new Keyframe(0.88f, 0.98f),
            new Keyframe(0.93f, 1f),
            new Keyframe(1f, 0.90f));

    [MenuItem("Big Ambitions Mods/Setup Cadillac Escalade")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "CadillacEscalade setup complete: generated a fitted Escalade with four " +
            "independent wheel visuals, 10-speed automatic, 40:60 AWD, V8 audio, functional lamp " +
            "geometry, colorable body/calipers, and corrected glass materials.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    public static void VerifyBuiltBundle()
    {
        VerifyCadillacBundle();
    }

    private static void VerifyCadillacBundle()
    {
        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");

        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException(
                    "Cadillac bundle is missing its VehicleType or prefab.");

            var issues = new List<string>();
            var serialized = new SerializedObject(vehicleType);
            var price = ReadNumber(serialized.FindProperty("price"));
            var fuel = ReadNumber(serialized.FindProperty("maxFuel"));
            var cargo = ReadNumber(serialized.FindProperty("maxCargoCapacity"));
            var speed = ReadNumber(serialized.FindProperty("maxSpeed"));
            var power = ReadNumber(serialized.FindProperty("enginePower"));
            var fitsHandTruck = serialized.FindProperty("fitsHandTruck");
            var fitsFlatbed = serialized.FindProperty("fitsFlatbed");
            if (Math.Abs(price - PeriodMsrp) > 0.5f) issues.Add($"price={price}");
            if (Math.Abs(fuel - FuelCapacityLitres) > 0.5f) issues.Add($"fuel={fuel}");
            if (Math.Abs(cargo - 24f) > 0.5f) issues.Add($"cargo={cargo}");
            if (Math.Abs(speed - 193f) > 0.5f) issues.Add($"speed={speed}");
            if (Math.Abs(power - RatedEnginePowerKw) > 0.5f) issues.Add($"power={power}");
            if (fitsHandTruck?.propertyType != SerializedPropertyType.Boolean ||
                !fitsHandTruck.boolValue)
            {
                issues.Add("fitsHandTruck=false");
            }
            if (fitsFlatbed?.propertyType != SerializedPropertyType.Boolean ||
                !fitsFlatbed.boolValue)
            {
                issues.Add("fitsFlatbed=false");
            }

            if (!TryGetCadillacRendererBounds(prefab.transform, out var bounds))
                issues.Add("no Cadillac renderer bounds");
            else
            {
                if (bounds.size.z < 5.32f || bounds.size.z > 5.44f)
                    issues.Add($"length={bounds.size.z:F3}");
                if (bounds.size.x < 2.40f || bounds.size.x > 2.50f)
                    issues.Add($"mirrorSpan={bounds.size.x:F3}");
                if (bounds.size.y < 1.80f || bounds.size.y > 1.92f)
                    issues.Add($"height={bounds.size.y:F3}");
            }

            var wheelMounts = 0;
            var rotors = 0;
            var fixedCalipers = 0;
            var positiveWheelTransforms = true;
            var straightTireMeshes = 0;
            var glassRenderers = 0;
            var bodyPaintSlots = 0;
            var caliperSlots = 0;
            var centeredPaintBodyMeshes = 0;
            var lightOverlayReferences = 0;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(transform.name, "CadillacWheelFrontLeft", StringComparison.Ordinal) ||
                    string.Equals(transform.name, "CadillacWheelFrontRight", StringComparison.Ordinal) ||
                    string.Equals(transform.name, "CadillacWheelRearLeft", StringComparison.Ordinal) ||
                    string.Equals(transform.name, "CadillacWheelRearRight", StringComparison.Ordinal))
                {
                    wheelMounts++;
                    positiveWheelTransforms &=
                        Vector3.Distance(transform.localScale, Vector3.one) < 0.0001f;
                }
                if (transform.name.StartsWith("CadillacBrakeRotor", StringComparison.Ordinal))
                    rotors++;
                if (transform.name.StartsWith("CadillacFixedCaliper", StringComparison.Ordinal))
                    fixedCalipers++;
                var wheelRenderer = transform.GetComponent<MeshRenderer>();
                if (transform.name.StartsWith("CadillacWheel", StringComparison.Ordinal) &&
                    wheelRenderer != null &&
                    HasMaterialMarker(wheelRenderer.sharedMaterials, "CadillacTire"))
                {
                    var tireMesh = transform.GetComponent<MeshFilter>()?.sharedMesh;
                    if (tireMesh != null)
                    {
                        var size = tireMesh.bounds.size;
                        if (Math.Abs(size.x - WheelWidth) < 0.005f &&
                            Math.Abs(size.y - WheelRadius * 2f) < 0.005f &&
                            Math.Abs(size.z - WheelRadius * 2f) < 0.005f)
                        {
                            straightTireMeshes++;
                        }
                    }
                }
                if (transform.name.StartsWith("CadillacDamageBody", StringComparison.Ordinal))
                {
                    var bodyMesh = transform.GetComponent<MeshFilter>()?.sharedMesh;
                    if (bodyMesh != null)
                        centeredPaintBodyMeshes++;
                }
                if (transform.name.StartsWith("CadillacEscalade_Light_", StringComparison.Ordinal))
                {
                    var mesh = transform.GetComponent<MeshFilter>()?.sharedMesh;
                    var renderer = transform.GetComponent<MeshRenderer>();
                    if (mesh != null && renderer != null && !renderer.enabled &&
                        Vector3.Distance(transform.localPosition, Vector3.zero) < 0.0001f &&
                        Quaternion.Angle(transform.localRotation, Quaternion.identity) < 0.01f &&
                        Vector3.Distance(transform.localScale, Vector3.one) < 0.0001f)
                    {
                        lightOverlayReferences++;
                    }
                }
            }
            if (centeredPaintBodyMeshes != 2)
                issues.Add($"centeredPaintBodyMeshes={centeredPaintBodyMeshes}");
            if (lightOverlayReferences != LightOverlayReferenceNames.Length)
                issues.Add($"lightOverlayReferences={lightOverlayReferences}");
            foreach (var overlayName in LightOverlayReferenceNames)
                if (FindTransform(prefab.transform, overlayName) == null)
                    issues.Add($"missingLightOverlay={overlayName}");

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var hasGlass = false;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                        continue;
                    if (CadillacEscaladeMaterials.IsCabinGlassMaterial(material))
                        hasGlass = true;
                    if (IsBodyPaintMaterial(material))
                        bodyPaintSlots++;
                    if (IsCaliperMaterial(material))
                        caliperSlots++;
                }
                if (hasGlass)
                    glassRenderers++;
            }

            var frontLeft = FindTransform(prefab.transform, "FrontLeft_WheelController");
            var frontRight = FindTransform(prefab.transform, "FrontRight_WheelController");
            var rearLeft = FindTransform(prefab.transform, "RearLeft_WheelController");
            var rearRight = FindTransform(prefab.transform, "RearRight_WheelController");
            if (frontLeft == null || frontRight == null || rearLeft == null || rearRight == null)
            {
                issues.Add("wheel controllers missing");
            }
            else
            {
                var wheelbase = Math.Abs(frontLeft.localPosition.z - rearLeft.localPosition.z);
                var frontTrack = Math.Abs(frontRight.localPosition.x - frontLeft.localPosition.x);
                var rearTrack = Math.Abs(rearRight.localPosition.x - rearLeft.localPosition.x);
                var axleMidpoint =
                    (frontLeft.localPosition.z + rearLeft.localPosition.z) * 0.5f;
                if (Math.Abs(wheelbase - Wheelbase) > 0.01f)
                    issues.Add($"wheelbase={wheelbase:F3}");
                if (axleMidpoint < 0.16f || axleMidpoint > 0.19f)
                    issues.Add($"axleMidpoint={axleMidpoint:F3}");
                if (frontTrack < 1.74f || frontTrack > 1.82f)
                    issues.Add($"frontTrack={frontTrack:F3}");
                if (rearTrack < 1.74f || rearTrack > 1.82f)
                    issues.Add($"rearTrack={rearTrack:F3}");
            }

            var body = prefab.GetComponent<Rigidbody>();
            if (body == null || Math.Abs(body.mass - VehicleMass) > 0.5f ||
                Vector3.Distance(body.centerOfMass, StableCenterOfMass) > 0.001f)
            {
                issues.Add("mass/center-of-mass");
            }

            var bodyCollider = FindTransform(prefab.transform, "BodyCollider");
            var bodyColliders = bodyCollider?.GetComponents<BoxCollider>() ?? Array.Empty<BoxCollider>();
            if (bodyColliders.Length < 2 ||
                Vector3.Distance(bodyColliders[0].center, LowerColliderCenter) > 0.001f ||
                Vector3.Distance(bodyColliders[0].size, LowerColliderSize) > 0.001f ||
                Vector3.Distance(bodyColliders[1].center, UpperColliderCenter) > 0.001f ||
                Vector3.Distance(bodyColliders[1].size, UpperColliderSize) > 0.001f)
            {
                issues.Add("body-collider-profile");
            }

            var powertrainVerified = false;
            var speedLimiterVerified = false;
            var verifiedLateralGripCorners = 0;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;

                if (component.transform.name.EndsWith("_WheelController", StringComparison.Ordinal))
                {
                    var wheel = new SerializedObject(component);
                    var lateralGrip = FindRelativeProperty(wheel, "sideFriction.grip");
                    var expectedGrip = component.transform.name.StartsWith(
                        "Front",
                        StringComparison.Ordinal)
                        ? FrontLateralGrip
                        : RearLateralGrip;
                    if (lateralGrip != null &&
                        Math.Abs(ReadNumber(lateralGrip) - expectedGrip) < 0.001f)
                    {
                        verifiedLateralGripCorners++;
                    }
                }

                if (string.Equals(
                        component.GetType().Name,
                        "SpeedLimiterModuleWrapper",
                        StringComparison.Ordinal))
                {
                    var limiter = FindRelativeProperty(new SerializedObject(component), "module");
                    var active = limiter?.FindPropertyRelative("active");
                    speedLimiterVerified =
                        active?.propertyType == SerializedPropertyType.Boolean &&
                        active.boolValue &&
                        Math.Abs(ReadNumber(limiter?.FindPropertyRelative("speedLimit")) - 193f) < 0.5f;
                    continue;
                }

                if (!string.Equals(
                        component.GetType().FullName,
                        "NWH.VehiclePhysics2.VehicleController",
                        StringComparison.Ordinal))
                    continue;

                var vehicle = new SerializedObject(component);
                var transmission = FindRelativeProperty(
                    vehicle,
                    "powertrain.transmission");
                var gears = transmission?.FindPropertyRelative("gears");
                powertrainVerified =
                    Math.Abs(ReadNumber(
                        transmission?.FindPropertyRelative("finalGearRatio")) - 3.23f) < 0.01f &&
                    Math.Abs(ReadNumber(
                        transmission?.FindPropertyRelative("forwardGearCount")) - 10f) < 0.01f &&
                    Math.Abs(ReadNumber(
                        transmission?.FindPropertyRelative("_downshiftRPM")) - 3500f) < 0.5f &&
                    gears != null &&
                    gears.isArray &&
                    gears.arraySize == EscaladeGears.Length;
                var clutch = FindRelativeProperty(vehicle, "powertrain.clutch");
                powertrainVerified &=
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("creepTorque"))) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("creepSpeedLimit")) - 1f) < 0.01f;
                var engine = FindRelativeProperty(vehicle, "powertrain.engine");
                powertrainVerified &=
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("maxPower")) -
                             EngineRoadCalibrationPowerKw) < 0.5f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("revLimiterRPM")) - 6000f) < 0.5f;
                var brakes = vehicle.FindProperty("brakes");
                powertrainVerified &=
                    Math.Abs(ReadNumber(brakes?.FindPropertyRelative("maxTorque")) -
                             BrakeMaxTorque) < 0.5f &&
                    Math.Abs(ReadNumber(brakes?.FindPropertyRelative("actuationTime")) -
                             BrakeActuationTime) < 0.001f;
            }
            if (!powertrainVerified)
                issues.Add("2021 powertrain/brakes");
            if (!speedLimiterVerified)
                issues.Add("193kph speed limiter");
            if (verifiedLateralGripCorners != 4)
                issues.Add($"lateralGripCorners={verifiedLateralGripCorners}");

            if (wheelMounts != 4) issues.Add($"wheelMounts={wheelMounts}");
            if (rotors != 4) issues.Add($"rotors={rotors}");
            if (fixedCalipers != 4) issues.Add($"fixedCalipers={fixedCalipers}");
            if (!positiveWheelTransforms) issues.Add("wheel mount scale");
            if (straightTireMeshes != 4) issues.Add($"straightTireMeshes={straightTireMeshes}");
            if (glassRenderers < 2) issues.Add($"glassRenderers={glassRenderers}");
            if (bodyPaintSlots != 2) issues.Add($"bodyPaintSlots={bodyPaintSlots}");
            if (caliperSlots != 4) issues.Add($"caliperSlots={caliperSlots}");
            if (prefab.GetComponent<CadillacEscaladePaintController>() == null)
                issues.Add("spawn-time-paint-bootstrap");
            if (FindTransform(prefab.transform, "Animate_SteeringWheel_033") == null)
                issues.Add("steering/driver reference");
            var driverExit = FindTransform(prefab.transform, "Driverside");
            var passengerExit = FindTransform(prefab.transform, "Passengerside");
            if (driverExit == null || passengerExit == null ||
                Vector3.Distance(driverExit.localPosition, new Vector3(-ExitMarkerOffset, 0.10f, 0.30f)) > 0.001f ||
                Vector3.Distance(passengerExit.localPosition, new Vector3(ExitMarkerOffset, 0.10f, 0.30f)) > 0.001f)
            {
                issues.Add("exit-marker-clearance");
            }
            if (FindTransform(prefab.transform, "Spotlights") == null)
                issues.Add("headlight beam template");
            if (FindTransform(prefab.transform, "CadillacDamageBodyFront") == null ||
                FindTransform(prefab.transform, "CadillacDamageBodyRear") == null)
                issues.Add("deformable bodies");

            if (issues.Count > 0)
                throw new InvalidOperationException(
                    "Cadillac bundle verification failed: " + string.Join(", ", issues));

            Debug.Log(
                $"CadillacEscalade bundle verified: price={price:F0}, fuel={fuel:F0}L, " +
                $"cargo={cargo:F0}, speed={speed:F0}kph, power={power:F0}kW, " +
                $"bounds={bounds.size}, wheels={wheelMounts}, rotors={rotors}, " +
                $"straightTires={straightTireMeshes}, fixedCalipers={fixedCalipers}, " +
                $"glassRenderers={glassRenderers}, " +
                $"bodyPaintSlots={bodyPaintSlots}, caliperSlots={caliperSlots}, " +
                $"lightOverlays={lightOverlayReferences}, " +
                "transmission=10-speed-automatic@3500rpm-downshift, drivetrain=40:60-AWD.");
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
                throw new InvalidOperationException("Could not create the Cadillac VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated Cadillac VehicleType asset did not load.");

        target.name = "CadillacEscalade";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", PeriodMsrp);
        SetNumber(serialized, "maxFuel", FuelCapacityLitres);
        SetNumber(serialized, "maxCargoCapacity", 24f);
        SetNumber(serialized, "maxSpeed", 193f);
        SetNumber(serialized, "enginePower", RatedEnginePowerKw);
        SetNumber(serialized, "brakeForce", BrakeMaxTorque);
        SetNumber(serialized, "turnRadius", 35f);
        SetNumber(serialized, "damageIntensity", 0.75f);
        SetBool(serialized, "isATruck", false);
        SetBool(serialized, "isHandVehicle", false);
        SetBool(serialized, "fitsHandTruck", true);
        SetBool(serialized, "fitsFlatbed", true);
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
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R reference prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("Cadillac GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "CadillacEscalade";
        try
        {
            StripAudiGeometry(root);
            RemoveAudiSpecificBehaviours(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the Cadillac model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "CadillacVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureExitMarkers(root, modelInstance);
            AssignPersistentMaterials(modelInstance);
            var lightOverlaysConfigured = CreateLightOverlayReferences(root, modelInstance);
            AttachWheelVisuals(root, modelInstance);
            var damageBodies = CreateDeformableBodies(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBodies);
            var fix = CadillacEscaladeMaterials.FixSolidMaterials(root);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);
            ConfigureRuntimeBootstrap(root);

            Debug.Log(
                $"CadillacEscalade: prepared decal-safe materials renderers={fix.RendererCount}, " +
                $"decalMasksCleared={fix.DecalMasksCleared}, " +
                $"opaqueFixed={fix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={fix.TransparentMaterialsFixed}, " +
                $"cabinGlass={fix.CabinGlassRenderers}/" +
                $"reenabled={fix.CabinGlassRenderersReenabled}, " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, " +
                $"lightOverlaysConfigured={lightOverlaysConfigured}, " +
                $"hdrpValidated={fix.MaterialsValidated}.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Cadillac vehicle prefab.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ConfigureRuntimeBootstrap(GameObject root)
    {
        if (root.GetComponent<CadillacEscaladePaintController>() == null)
            root.AddComponent<CadillacEscaladePaintController>();
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

    private static int CreateLightOverlayReferences(GameObject vehicleRoot, GameObject model)
    {
        var overlayModel = AssetDatabase.LoadAssetAtPath<GameObject>(LightOverlayModelPath);
        if (overlayModel == null)
            throw new InvalidOperationException(
                $"Cadillac light overlay GLB did not import at '{LightOverlayModelPath}'.");

        var frontDrlAnchor = FindTransform(model.transform, FrontDrlAnchor) ??
                             throw new InvalidOperationException(
                                 $"Cadillac light anchor '{FrontDrlAnchor}' is missing.");
        var frontIndicatorAnchor = FindTransform(model.transform, FrontIndicatorAnchor) ??
                                   throw new InvalidOperationException(
                                       $"Cadillac light anchor '{FrontIndicatorAnchor}' is missing.");
        var headlampAnchor = FindTransform(model.transform, HeadlampAnchor) ??
                             throw new InvalidOperationException(
                                 $"Cadillac light anchor '{HeadlampAnchor}' is missing.");
        var reverseAnchor = FindTransform(model.transform, ReverseLightAnchor) ??
                            throw new InvalidOperationException(
                                $"Cadillac light anchor '{ReverseLightAnchor}' is missing.");
        var thirdBrakeAnchor = FindTransform(model.transform, ThirdBrakeLightAnchor) ??
                               throw new InvalidOperationException(
                                   $"Cadillac light anchor '{ThirdBrakeLightAnchor}' is missing.");
        var rearAnchor = FindTransform(model.transform, RearLightAnchor) ??
                         throw new InvalidOperationException(
                             $"Cadillac light anchor '{RearLightAnchor}' is missing.");

        var drlLeft = FindLightOverlayMesh(overlayModel, "VehicleLightRef_DRL_Indicator_FL");
        var drlRight = FindLightOverlayMesh(overlayModel, "VehicleLightRef_DRL_Indicator_FR");
        var frontIndicatorLeft = FindLightOverlayMesh(overlayModel, "VehicleLightRef_Indicator_FL");
        var frontIndicatorRight = FindLightOverlayMesh(overlayModel, "VehicleLightRef_Indicator_FR");
        var headlamps = FindLightOverlayMesh(overlayModel, "VehicleLightRef_Front_lights");
        var reverse = FindLightOverlayMesh(overlayModel, "VehicleLightRef_Reverse_light");
        var thirdBrake = FindLightOverlayMesh(overlayModel, "VehicleLightRef_3rd_brake_light");
        var brakeLights = FindLightOverlayMesh(overlayModel, "VehicleLightRef_Brake_lights");
        var rearIndicatorLeft = FindLightOverlayMesh(overlayModel, "VehicleLightRef_Indicator_RL");
        var rearIndicatorRight = FindLightOverlayMesh(overlayModel, "VehicleLightRef_Indicator_RR");

        var references = new List<Transform>
        {
            CreateLightOverlayReference(frontDrlAnchor, drlLeft, LightOverlayReferenceNames[0]),
            CreateLightOverlayReference(frontDrlAnchor, drlRight, LightOverlayReferenceNames[1]),
            CreateLightOverlayReference(frontDrlAnchor, drlLeft, LightOverlayReferenceNames[2]),
            CreateLightOverlayReference(frontDrlAnchor, drlRight, LightOverlayReferenceNames[3]),
            CreateLightOverlayReference(frontIndicatorAnchor, frontIndicatorLeft, LightOverlayReferenceNames[4]),
            CreateLightOverlayReference(frontIndicatorAnchor, frontIndicatorRight, LightOverlayReferenceNames[5]),
            CreateLightOverlayReference(headlampAnchor, headlamps, LightOverlayReferenceNames[6]),
            CreateLightOverlayReference(rearAnchor, brakeLights, LightOverlayReferenceNames[7]),
            CreateLightOverlayReference(rearAnchor, brakeLights, LightOverlayReferenceNames[8]),
            CreateLightOverlayReference(thirdBrakeAnchor, thirdBrake, LightOverlayReferenceNames[9]),
            CreateLightOverlayReference(reverseAnchor, reverse, LightOverlayReferenceNames[10]),
            CreateLightOverlayReference(rearAnchor, rearIndicatorLeft, LightOverlayReferenceNames[11]),
            CreateLightOverlayReference(rearAnchor, rearIndicatorRight, LightOverlayReferenceNames[12]),
        };

        for (var index = 0; index < references.Count; index++)
        {
            var expectedFront = index <= 6;
            var center = vehicleRoot.transform.InverseTransformPoint(
                references[index].TransformPoint(
                    references[index].GetComponent<MeshFilter>().sharedMesh.bounds.center));
            if ((expectedFront && center.z < 1.5f) || (!expectedFront && center.z > -1.5f))
            {
                throw new InvalidOperationException(
                    $"Cadillac light overlay '{references[index].name}' is outside its expected " +
                    $"{(expectedFront ? "front" : "rear")} region at {center}.");
            }
        }

        return references.Count;
    }

    private static Mesh FindLightOverlayMesh(GameObject overlayModel, string name)
    {
        foreach (var filter in overlayModel.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh != null &&
                (string.Equals(filter.name, name, StringComparison.Ordinal) ||
                 string.Equals(filter.sharedMesh.name, name, StringComparison.Ordinal)))
            {
                return filter.sharedMesh;
            }
        }

        throw new InvalidOperationException(
            $"Cadillac light overlay mesh '{name}' is missing from '{LightOverlayModelPath}'.");
    }

    private static Transform CreateLightOverlayReference(Transform anchor, Mesh mesh, string name)
    {
        var existing = anchor.Find(name);
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing.gameObject);

        var host = new GameObject(name);
        host.transform.SetParent(anchor, false);
        host.transform.localPosition = Vector3.zero;
        host.transform.localRotation = Quaternion.identity;
        host.transform.localScale = Vector3.one;
        host.layer = anchor.gameObject.layer;
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        var sourceRenderer = anchor.GetComponent<MeshRenderer>();
        if (sourceRenderer != null)
            renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;
        return host.transform;
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        body.mass = VehicleMass;
        body.drag = 0f;
        body.angularDrag = 1.8f;
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
            SetNumber(serialized, "baseMass", VehicleMass);
            SetNumber(serialized, "combinedMass", VehicleMass);
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
                SetRelativeNumber(serialized, "spring.maxForce", 26000f);
                SetRelativeNumber(serialized, "wheel.radius", WheelRadius);
                SetRelativeNumber(serialized, "wheel.width", WheelWidth);
                SetRelativeNumber(
                    serialized,
                    "sideFriction.grip",
                    isFront ? FrontLateralGrip : RearLateralGrip);
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

        colliders[0].center = LowerColliderCenter;
        colliders[0].size = LowerColliderSize;
        colliders[1].center = UpperColliderCenter;
        colliders[1].size = UpperColliderSize;
    }

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        const float driverSide = -ExitMarkerOffset;
        SetLocalPosition(root, "Animate_SteeringWheel_033", new Vector3(-0.52f, 1.15f, 0.68f));
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.10f, 0.30f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.10f, 0.30f));
        Debug.Log(
            $"CadillacEscalade: left-hand-drive steering reference=(-0.52,1.15,0.68); " +
            $"driver/passenger exits={driverSide:F2}/{-driverSide:F2}.");
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
                SetRelativeNumber(serialized, "brakes.maxTorque", BrakeMaxTorque);
                SetRelativeNumber(serialized, "brakes.actuationTime", BrakeActuationTime);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1000f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 350f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 700f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.45f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", EngineRoadCalibrationPowerKw);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateEscaladePowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 600f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 6000f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.80f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.23f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 10f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.25f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 3500f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 5750f);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);

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

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = EscaladeGears.Length;
                for (var index = 0; index < EscaladeGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = EscaladeGears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeBool(serialized, "module.active", true);
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
        // glTFast resolves the Sketchfab root matrix to a Y-up model whose nose
        // points along vehicle +Z. Preserve the replacement model's authored
        // rotation and normalize only its body group; its wheels are fitted
        // independently after the body is centered.
        model.transform.localScale = Vector3.one;

        if (!TryGetModelBodyBounds(model.transform, out var bounds))
            throw new InvalidOperationException("Cadillac replacement model body group contains no renderers.");
        if (bounds.size.x <= 0.001f || bounds.size.y <= 0.001f || bounds.size.z <= 0.001f)
            throw new InvalidOperationException("Cadillac replacement model body bounds are invalid.");

        var scale = new Vector3(
            TargetVisualWidthIncludingMirrors / bounds.size.x,
            TargetLength / bounds.size.z,
            (TargetHeight - BodyGroundClearance) / bounds.size.y);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled Cadillac bounds could not be measured.");

        model.transform.position += new Vector3(
            -bounds.center.x,
            BodyGroundClearance - BodyVisualLowering - bounds.min.y,
            -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Cadillac bounds could not be measured.");

        Debug.Log(
            $"CadillacEscalade: normalized replacement 2021 GLB scale={scale}, " +
            $"bodyBounds={bounds.size}, center={bounds.center}, " +
            $"visualLowering={BodyVisualLowering:F2}m.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        if (!TryGetModelBodyBounds(model.transform, out var bodyBounds))
            throw new InvalidOperationException("Cadillac body bounds are unavailable for wheel fitting.");

        var wheelParts = new Dictionary<string, List<MeshRenderer>>
        {
            { "FrontLeft_WheelController", new List<MeshRenderer>() },
            { "FrontRight_WheelController", new List<MeshRenderer>() },
            { "RearLeft_WheelController", new List<MeshRenderer>() },
            { "RearRight_WheelController", new List<MeshRenderer>() },
        };
        var tireRenderers = new Dictionary<string, MeshRenderer>();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsUnderNamedAncestor(renderer.transform, model.transform, "group1") ||
                !IsReplacementWheelMaterial(renderer.sharedMaterials))
                continue;

            var center = root.transform.InverseTransformPoint(renderer.bounds.center);
            var key = center.z >= bodyBounds.center.z ? "Front" : "Rear";
            key += center.x < bodyBounds.center.x ? "Left_WheelController" : "Right_WheelController";
            wheelParts[key].Add(renderer);
            if (HasMaterialMarker(renderer.sharedMaterials, "CadillacTire"))
                tireRenderers[key] = renderer;
        }

        var authoredCenters = new Dictionary<string, Vector3>();
        foreach (var pair in wheelParts)
        {
            if (pair.Value.Count < 4 || !tireRenderers.TryGetValue(pair.Key, out var tireRenderer))
                throw new InvalidOperationException(
                    $"Replacement wheel '{pair.Key}' is incomplete: parts={pair.Value.Count}, tire={tireRenderers.ContainsKey(pair.Key)}.");
            var tireBounds = tireRenderer.bounds;
            if (tireBounds.size.x <= 0.001f || tireBounds.size.y <= 0.001f || tireBounds.size.z <= 0.001f)
                throw new InvalidOperationException($"Replacement wheel '{pair.Key}' has invalid tire bounds.");
            authoredCenters[pair.Key] = root.transform.InverseTransformPoint(tireBounds.center);
        }
        var frontHalfTrack =
            (Math.Abs(authoredCenters["FrontLeft_WheelController"].x) +
             Math.Abs(authoredCenters["FrontRight_WheelController"].x)) * 0.5f;
        var rearHalfTrack =
            (Math.Abs(authoredCenters["RearLeft_WheelController"].x) +
             Math.Abs(authoredCenters["RearRight_WheelController"].x)) * 0.5f;
        var frontAxle =
            (authoredCenters["FrontLeft_WheelController"].z +
             authoredCenters["FrontRight_WheelController"].z) * 0.5f;
        var rearAxle =
            (authoredCenters["RearLeft_WheelController"].z +
             authoredCenters["RearRight_WheelController"].z) * 0.5f;

        foreach (var pair in wheelParts)
        {
            var tireBounds = tireRenderers[pair.Key].bounds;
            var authoredCenter = authoredCenters[pair.Key];
            var controller = FindTransform(root.transform, pair.Key) ??
                             throw new InvalidOperationException($"Wheel controller '{pair.Key}' is missing.");
            var front = pair.Key.StartsWith("Front", StringComparison.Ordinal);
            var left = pair.Key.IndexOf("Left", StringComparison.Ordinal) >= 0;
            var controllerPosition = new Vector3(
                (left ? -1f : 1f) * (front ? frontHalfTrack : rearHalfTrack),
                WheelRadius,
                front ? frontAxle : rearAxle);
            controller.localPosition = controllerPosition;
            var suffix = pair.Key.Replace("_WheelController", string.Empty).Replace("_", string.Empty);
            var mount = new GameObject("CadillacWheel" + suffix);
            mount.transform.SetParent(root.transform, false);
            mount.transform.localPosition = controllerPosition;
            var fit = new Vector3(
                WheelWidth / tireBounds.size.x,
                (WheelRadius * 2f) / tireBounds.size.y,
                (WheelRadius * 2f) / tireBounds.size.z);
            pair.Value.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
            for (var index = 0; index < pair.Value.Count; index++)
                BakeWheelPart(root, mount, pair.Value[index], authoredCenter, fit, suffix, index);

            CreateBrakeHardware(root, mount, controller, suffix);
            Debug.Log(
                $"CadillacEscalade: fitted replacement wheel {pair.Key} parts={pair.Value.Count}, " +
                $"authoredCenter={authoredCenter}, controller={controllerPosition}, fit={fit}, " +
                $"tire={WheelWidth:F3}x{WheelRadius * 2f:F3}m.");

            AssignWheelVisual(controller, mount);
        }
    }

    private static void BakeWheelPart(
        GameObject root,
        GameObject mount,
        MeshRenderer renderer,
        Vector3 authoredCenter,
        Vector3 fit,
        string wheelSuffix,
        int partIndex)
    {
        var filter = renderer.GetComponent<MeshFilter>();
        if (filter?.sharedMesh == null)
            throw new InvalidOperationException(
                $"Wheel source '{renderer.name}' has no readable mesh.");

        EnsureAssetFolder(MeshFolder);
        var assetSuffix = $"{wheelSuffix}Part{partIndex:D2}";
        var meshPath = $"{MeshFolder}/CadillacWheel{assetSuffix}.asset";
        var baked = UnityEngine.Object.Instantiate(filter.sharedMesh);
        baked.name = "CadillacWheel" + assetSuffix;
        var sourceToRoot = root.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        var sourceToMount = Matrix4x4.Scale(fit) *
                            Matrix4x4.Translate(-authoredCenter) *
                            sourceToRoot;
        var vertices = baked.vertices;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = sourceToMount.MultiplyPoint3x4(vertices[index]);
        baked.vertices = vertices;

        var normals = baked.normals;
        if (normals.Length == vertices.Length)
        {
            var normalMatrix = sourceToMount.inverse.transpose;
            for (var index = 0; index < normals.Length; index++)
                normals[index] = normalMatrix.MultiplyVector(normals[index]).normalized;
            baked.normals = normals;
        }

        var tangents = baked.tangents;
        var mirrored = sourceToMount.determinant < 0f;
        if (tangents.Length == vertices.Length)
        {
            for (var index = 0; index < tangents.Length; index++)
            {
                var tangent = tangents[index];
                var direction = sourceToMount.MultiplyVector(
                    new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                tangents[index] = new Vector4(
                    direction.x,
                    direction.y,
                    direction.z,
                    mirrored ? -tangent.w : tangent.w);
            }
            baked.tangents = tangents;
        }

        if (mirrored)
        {
            for (var subMesh = 0; subMesh < baked.subMeshCount; subMesh++)
            {
                var triangles = baked.GetTriangles(subMesh);
                for (var index = 0; index + 2 < triangles.Length; index += 3)
                    (triangles[index + 1], triangles[index + 2]) =
                        (triangles[index + 2], triangles[index + 1]);
                baked.SetTriangles(triangles, subMesh, false);
            }
        }
        baked.RecalculateBounds();
        baked.UploadMeshData(false);

        var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (persistent == null)
        {
            AssetDatabase.CreateAsset(baked, meshPath);
            persistent = baked;
        }
        else
        {
            EditorUtility.CopySerialized(baked, persistent);
            UnityEngine.Object.DestroyImmediate(baked);
            EditorUtility.SetDirty(persistent);
        }

        var geometry = new GameObject("CadillacWheel" + assetSuffix);
        geometry.transform.SetParent(mount.transform, false);
        geometry.AddComponent<MeshFilter>().sharedMesh = persistent;
        var targetRenderer = geometry.AddComponent<MeshRenderer>();
        targetRenderer.sharedMaterials = renderer.sharedMaterials;
        targetRenderer.shadowCastingMode = renderer.shadowCastingMode;
        targetRenderer.receiveShadows = renderer.receiveShadows;
        targetRenderer.lightProbeUsage = renderer.lightProbeUsage;
        targetRenderer.reflectionProbeUsage = renderer.reflectionProbeUsage;
        targetRenderer.renderingLayerMask = renderer.renderingLayerMask;
        UnityEngine.Object.DestroyImmediate(renderer.gameObject);
    }

    private static void CreateBrakeHardware(
        GameObject root,
        GameObject wheelMount,
        Transform controller,
        string suffix)
    {
        var rotorMaterial = GetOrCreateBrakeMaterial(
            "CadillacBrakeRotor",
            new Color(0.32f, 0.34f, 0.36f, 1f),
            0.82f,
            0.48f);
        var caliperMaterial = GetOrCreateBrakeMaterial(
            "CadillacCaliper",
            new Color(0.55f, 0.03f, 0.025f, 1f),
            0.48f,
            0.62f);

        var rotor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rotor.name = "CadillacBrakeRotor" + suffix;
        UnityEngine.Object.DestroyImmediate(rotor.GetComponent<Collider>());
        rotor.transform.SetParent(wheelMount.transform, false);
        rotor.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        rotor.transform.localScale = new Vector3(0.32f, 0.012f, 0.32f);
        rotor.GetComponent<MeshRenderer>().sharedMaterial = rotorMaterial;

        var fixedCaliper = new GameObject("CadillacFixedCaliper" + suffix);
        fixedCaliper.transform.SetParent(root.transform, false);
        fixedCaliper.transform.localPosition = controller.localPosition;
        var caliper = GameObject.CreatePrimitive(PrimitiveType.Cube);
        caliper.name = "CadillacCaliper" + suffix;
        UnityEngine.Object.DestroyImmediate(caliper.GetComponent<Collider>());
        caliper.transform.SetParent(fixedCaliper.transform, false);
        var side = controller.localPosition.x < 0f ? -1f : 1f;
        caliper.transform.localPosition = new Vector3(side * 0.04f, 0.10f, -0.13f);
        caliper.transform.localScale = new Vector3(0.055f, 0.20f, 0.085f);
        caliper.GetComponent<MeshRenderer>().sharedMaterial = caliperMaterial;
    }

    private static Material GetOrCreateBrakeMaterial(
        string name,
        Color color,
        float metallic,
        float smoothness)
    {
        EnsureAssetFolder(MaterialFolder);
        var path = $"{MaterialFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("HDRP/Lit") ??
                         Shader.Find("High Definition Render Pipeline/Lit") ??
                         throw new InvalidOperationException("HDRP Lit shader is unavailable.");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
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

    private static List<MeshFilter> CreateDeformableBodies(GameObject root, GameObject modelInstance)
    {
        var sourceRenderers = new List<MeshRenderer>();
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (HasMaterialMarker(renderer.sharedMaterials, "CadillacBodyPaint"))
                sourceRenderers.Add(renderer);
        }
        if (sourceRenderers.Count != 2)
            throw new InvalidOperationException(
                $"Expected two replacement Cadillac paint-body renderers, found {sourceRenderers.Count}.");
        sourceRenderers.Sort((left, right) => right.bounds.center.z.CompareTo(left.bounds.center.z));

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");
        var results = new List<MeshFilter>();
        for (var bodyIndex = 0; bodyIndex < sourceRenderers.Count; bodyIndex++)
        {
            var sourceRenderer = sourceRenderers[bodyIndex];
            var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
            if (sourceFilter?.sharedMesh == null)
                throw new InvalidOperationException($"Cadillac paint body '{sourceRenderer.name}' has no mesh.");
            var label = bodyIndex == 0 ? "Front" : "Rear";
            var assetPath = bodyIndex == 0 ? FrontDamageBodyMeshPath : RearDamageBodyMeshPath;
            var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
            bakedMesh.name = "CadillacDamageBody" + label;
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

            var persistentMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (persistentMesh == null)
            {
                AssetDatabase.CreateAsset(bakedMesh, assetPath);
                persistentMesh = bakedMesh;
            }
            else
            {
                EditorUtility.CopySerialized(bakedMesh, persistentMesh);
                UnityEngine.Object.DestroyImmediate(bakedMesh);
                EditorUtility.SetDirty(persistentMesh);
            }

            var damageBody = new GameObject("CadillacDamageBody" + label)
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
            results.Add(damageFilter);
        }
        return results;
    }

    private static void ConfigureVehicleDeformation(GameObject root, IReadOnlyList<MeshFilter> bodyFilters)
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
            meshFilters.arraySize = bodyFilters.Count;
            for (var index = 0; index < bodyFilters.Count; index++)
                meshFilters.GetArrayElementAtIndex(index).objectReferenceValue = bodyFilters[index];
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
        material.name.IndexOf(
            "CadillacBodyPaint",
            StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorAccentPaintMaterial(Material material) =>
        false;

    private static bool IsRimMaterial(Material material) =>
        material.name.IndexOf(
            "CadillacRimChrome",
            StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!CadillacEscaladeMaterials.IsCadillacRenderer(renderer.transform))
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
            throw new InvalidOperationException("The shared Cadillac rim material is missing.");

        ApplyRimFinish(leftMaterial, CadillacEscaladeMaterials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!CadillacEscaladeMaterials.IsCadillacRenderer(renderer.transform))
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

        if (configuredSlots != 8)
            throw new InvalidOperationException(
                $"Expected eight replacement Cadillac rim-chrome slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", CadillacEscaladeMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", CadillacEscaladeMaterials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("CadillacCaliper", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool EscaladePowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateEscaladePowerCurve();
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

    private static bool IsRimInnerPaintMaterial(Material material) =>
        false;

    private static bool IsSeatPaintMaterial(Material material) =>
        false;

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
        false;

    private static bool IsInteriorPrimaryPaintMaterial(Material material) =>
        false;

    private static bool IsInteriorSecondaryPaintMaterial(Material material) =>
        false;

    private static bool IsInteriorDarkPaintMaterial(Material material) =>
        false;

    private static void AssignPersistentMaterials(GameObject model)
    {
        EnsureAssetFolder(MaterialFolder);
        var replacements = new Dictionary<string, Material>(StringComparer.Ordinal);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                    continue;

                var assetName = GetPersistentMaterialName(renderer.transform, source);
                if (!replacements.TryGetValue(assetName, out var persistent))
                {
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

                    replacements.Add(assetName, persistent);
                }

                materials[index] = persistent;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private static string GetPersistentMaterialName(Transform renderer, Material source)
    {
        var sourceName = source.name;
        if (ContainsIgnoreCase(sourceName, "EPaint_Body_swb1"))
            return "CadillacBodyPaint";
        if (ContainsIgnoreCase(sourceName, "EPlastic_Clear_004"))
            return TransformPathContains(renderer, "window_glass") ||
                   TransformPathContains(renderer, "glass_windows")
                ? "CadillacCabinGlass"
                : "CadillacClearLampLens";
        if (ContainsIgnoreCase(sourceName, "EGlass_Red_Tint_002"))
            return "CadillacRearLampLens";
        if (ContainsIgnoreCase(sourceName, "EGlass_Amber_004"))
            return "CadillacAmberLampLens";
        if (ContainsIgnoreCase(sourceName, "Wheelscombined_texture_maps_005"))
            return "CadillacTire";
        if (ContainsIgnoreCase(sourceName, "Wheelspolish_cast_aluminum_inner1"))
            return "CadillacRimInner";
        if (ContainsIgnoreCase(sourceName, "WheelsPaint_Black_Gloss_008"))
            return "CadillacRimBlack";
        if (ContainsIgnoreCase(sourceName, "SILVER_CHROME"))
            return TransformPathContains(renderer, "group1")
                ? "CadillacChrome"
                : "CadillacRimChrome";
        if (ContainsIgnoreCase(sourceName, "Ealpha_badges_002"))
        {
            if (TransformPathContains(renderer, "tail_lamp") ||
                TransformPathContains(renderer, "rear_etchings") ||
                TransformPathContains(renderer, "chml"))
                return "CadillacRearLamp";
            if (TransformPathContains(renderer, "running_headlight") ||
                TransformPathContains(renderer, "running_facia") ||
                TransformPathContains(renderer, "high_beams") ||
                TransformPathContains(renderer, "headlights_etched") ||
                TransformPathContains(renderer, "etches_light"))
                return "CadillacFrontLamp";
            return "CadillacAlphaBadges";
        }
        return "Cadillac_" + SanitizeAssetName(sourceName);
    }

    private static bool TransformPathContains(Transform transform, string marker)
    {
        for (var current = transform; current != null; current = current.parent)
            if (ContainsIgnoreCase(current.name, marker))
                return true;
        return false;
    }

    private static bool ContainsIgnoreCase(string value, string marker) =>
        value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;

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

        manifest.ModId = "CadillacEscalade";
        manifest.DisplayName = "Cadillac Escalade";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "cadillacescalade.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Cadillac manifest references could not be assigned.");
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
            if (!IsUnderNamedAncestor(renderer.transform, root, "group1"))
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

    private static bool IsUnderNamedAncestor(Transform transform, Transform root, string name)
    {
        for (var current = transform; current != null && current != root; current = current.parent)
            if (string.Equals(current.name, name, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static bool IsReplacementWheelMaterial(IReadOnlyList<Material> materials) =>
        HasMaterialMarker(materials, "CadillacTire") ||
        HasMaterialMarker(materials, "CadillacRim");

    private static bool HasMaterialMarker(IReadOnlyList<Material> materials, string marker)
    {
        for (var index = 0; index < materials.Count; index++)
            if (materials[index] != null &&
                materials[index].name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static bool TryGetCadillacRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!CadillacEscaladeMaterials.IsCadillacRenderer(renderer.transform))
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
