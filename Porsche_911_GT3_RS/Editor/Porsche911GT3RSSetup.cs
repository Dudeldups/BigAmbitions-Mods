#nullable enable
using System;
using System.Collections.Generic;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class Porsche911GT3RSSetup
{
    private const string ModRoot = "Assets/Mods/Porsche_911_GT3_RS";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/2023_porsche_911_gt3_rs_992.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/PorscheDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/Porsche911GT3RS.asset";
    private const string VehiclePrefabPath = ModRoot + "/Porsche911GT3RS.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/Porsche911GT3RS.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/porsche911gt3rs.unity3d";
    private const string VehicleTypeName =
        "porsche911gt3rs-vehicle:vehicletype_porsche911gt3rs";
    private const float TargetLength = 4.572f;
    private const float TargetWidth = 1.900f;
    private const float TargetHeight = 1.322f;
    private const float FrontTrack = 1.630f;
    private const float RearTrack = 1.582f;
    private const float Wheelbase = 2.457f;
    private const float FrontTireRadius = 0.35025f;
    private const float RearTireRadius = 0.36720f;
    private const float FrontTireWidth = 0.275f;
    private const float RearTireWidth = 0.335f;
    private const float WheelInset = 0.085f;
    private const float WheelCenterRideHeightOffset = 0.060f;
    private const float VehicleLinearDrag = 0.035f;
    private const float VehicleBrakeForce = 2200f;
    private const float BrakeMaxTorque = 2200f;
    private const float FrontForwardGrip = 0.90f;
    private const float RearForwardGrip = 0.95f;
    private const float FrontForwardStiffness = 1.27f;
    private const float RearForwardStiffness = 1.35f;
    private const float TireFrictionCircleStrength = 1.02f;
    private const float AntiRollBarForce = 9000f;
    private const float FrontSuspensionTravel = 0.075f;
    private const float RearSuspensionTravel = 0.080f;
    private const float DeformationStrength = 0.17f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.08f, -0.28f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f + WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, Wheelbase * 0.5f) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f - WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, Wheelbase * 0.5f) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f + WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f - WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f) },
        };
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.61f, 1.58f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.78f, 0.50f, 1.08f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.60f, -1.78f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.78f, 0.50f, 1.02f);

    private static readonly float[] GT3RSGears =
    {
        -3.42f,
        0f,
        3.75f,
        2.38f,
        1.72f,
        1.34f,
        1.11f,
        0.96f,
        0.84f,
    };

    private static AnimationCurve CreateGT3RSPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.10f, 0.023f),
            new Keyframe(0.23f, 0.078f),
            new Keyframe(0.45f, 0.215f),
            new Keyframe(0.67f, 0.48f),
            new Keyframe(0.82f, 0.50f),
            new Keyframe(0.90f, 0.64f),
            new Keyframe(0.94f, 1f),
            new Keyframe(1f, 0.94f));

    [MenuItem("Big Ambitions Mods/Setup Porsche 911 GT3 RS")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "Porsche911GT3RS setup complete: generated a fitted GT3 RS with four " +
            "independent wheel visuals, seven-speed PDK, RWD, flat-six audio, functional lamp " +
            "geometry, colorable body/calipers, and corrected glass materials.");
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

            var visual = FindTransform(prefab.transform, "PorscheVisual") ??
                         throw new InvalidOperationException("Porsche visual root is missing.");
            if (!TryGetPorscheRendererBounds(prefab.transform, out var bounds))
                throw new InvalidOperationException("Porsche visual has no renderer bounds.");
            var bodySidesOriented =
                bounds.size.x > bounds.size.y * 1.4f &&
                bounds.size.z > bounds.size.x * 2f;
            var frontMarker = FindTransformWithNameFragment(visual, "gt3rs_bumper_F");
            var rearMarker = FindTransformWithNameFragment(visual, "gt3rs_bumper_R");
            var frontFacesVehicleForward =
                frontMarker != null && rearMarker != null &&
                TryGetRendererBounds(frontMarker, out var frontMarkerBounds) &&
                TryGetRendererBounds(rearMarker, out var rearMarkerBounds) &&
                frontMarkerBounds.center.z > rearMarkerBounds.center.z + 2f;
            var windshield = FindTransformWithNameFragment(visual, "windshield");
            var exhaust = FindTransformWithNameFragment(visual, "exhaust");
            var windshieldHeight = float.NaN;
            var exhaustHeight = float.NaN;
            if (windshield != null && TryGetRendererBounds(windshield, out var windshieldBounds))
                windshieldHeight = windshieldBounds.center.y;
            if (exhaust != null && TryGetRendererBounds(exhaust, out var exhaustBounds))
                exhaustHeight = exhaustBounds.center.y;
            var bodyUpright =
                !float.IsNaN(windshieldHeight) && !float.IsNaN(exhaustHeight) &&
                windshieldHeight > exhaustHeight;

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
                if (transform.name.StartsWith("PorscheWheel", StringComparison.Ordinal))
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
                        "PorscheWheelFrontLeft" => "Geometry_PorscheWheel_FL",
                        "PorscheWheelFrontRight" => "Geometry_PorscheWheel_FR",
                        "PorscheWheelRearLeft" => "Geometry_PorscheWheel_RL",
                        "PorscheWheelRearRight" => "Geometry_PorscheWheel_RR",
                        _ => string.Empty,
                    };
                    if (string.IsNullOrEmpty(expectedGeometry) ||
                        FindTransform(transform, expectedGeometry) == null)
                    {
                        wheelSideMappingCorrect = false;
                    }
                }
                if (transform.name.StartsWith("PorscheFixedCaliper", StringComparison.Ordinal))
                {
                    fixedCalipers++;
                    if (FindTransformWithNameFragment(transform, "_caliper") == null)
                        calipersDetachedFromWheels = false;
                    fittedCaliperCenters[transform.name] = transform.position;
                }
                if (transform.name.IndexOf("fascia_mid", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continuousTailLight = true;
                }
                if (transform.name.IndexOf("tailgate", StringComparison.OrdinalIgnoreCase) >= 0)
                    thirdBrakeLight = true;
                if (transform.name.IndexOf("headlight_L_led", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    transform.name.IndexOf("headlight_R_led", StringComparison.OrdinalIgnoreCase) >= 0)
                    frontBlinkerMeshes++;
            }

            var frontLeftCenter = Vector3.zero;
            var frontRightCenter = Vector3.zero;
            var rearLeftCenter = Vector3.zero;
            var rearRightCenter = Vector3.zero;
            var wheelPlacementVerified =
                fittedWheelCenters.TryGetValue("PorscheWheelFrontLeft", out frontLeftCenter) &&
                fittedWheelCenters.TryGetValue("PorscheWheelFrontRight", out frontRightCenter) &&
                fittedWheelCenters.TryGetValue("PorscheWheelRearLeft", out rearLeftCenter) &&
                fittedWheelCenters.TryGetValue("PorscheWheelRearRight", out rearRightCenter);
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
                    "PorscheFixedCaliperFrontLeft",
                    frontLeftCenter) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "PorscheFixedCaliperFrontRight",
                    frontRightCenter) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "PorscheFixedCaliperRearLeft",
                    rearLeftCenter) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "PorscheFixedCaliperRearRight",
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
                transmissionVerified = gearCount == 7 && gears != null && gears.arraySize == 9;
                var clutch = powertrain?.FindPropertyRelative("clutch");
                var engine = powertrain?.FindPropertyRelative("engine");
                var powerCurve = engine?.FindPropertyRelative("powerCurve")?.animationCurveValue;
                launchResponseVerified =
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRPM")) - 1250f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("throttleEngagementOffsetRPM")) - 600f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRange")) - 550f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("creepTorque"))) < 0.01f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("inertia")) - 0.075f) < 0.001f &&
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
                if (!Porsche911GT3RSMaterials.IsPorscheRenderer(renderer.transform))
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
                        var expectedRimColor = Porsche911GT3RSMaterials.RimBaseColor;
                        var rimColor = material.HasProperty("_BaseColor")
                            ? material.GetColor("_BaseColor")
                            : Color.clear;
                        rimFinishValid &=
                            material.HasProperty("_Metallic") &&
                            Math.Abs(material.GetFloat("_Metallic") - Porsche911GT3RSMaterials.RimMetallic) < 0.01f &&
                            material.HasProperty("_Smoothness") &&
                            Math.Abs(material.GetFloat("_Smoothness") - Porsche911GT3RSMaterials.RimSmoothness) < 0.01f &&
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
                    if (Porsche911GT3RSMaterials.IsTransparentMaterial(material))
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
                            "PorscheDamageBody",
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

            if (Math.Abs(price - 223800f) > 0.5f ||
                Math.Abs(maxFuel - 64f) > 0.5f ||
                Math.Abs(maxSpeed - 296f) > 0.5f ||
                Math.Abs(enginePower - 386f) > 0.5f ||
                Math.Abs(brakeForce - VehicleBrakeForce) > 0.5f ||
                !luxury ||
                Math.Abs(bounds.size.z - TargetLength) > 0.02f ||
                Math.Abs(bounds.size.x - TargetWidth) > 0.04f ||
                Math.Abs(bounds.size.y -
                         (TargetHeight - WheelCenterRideHeightOffset)) > 0.02f ||
                !bodySidesOriented ||
                !bodyUpright ||
                !frontFacesVehicleForward ||
                wheelVisuals != 4 ||
                !wheelGeometryOriented ||
                !wheelSideMappingCorrect ||
                !wheelPlacementVerified ||
                fixedCalipers != 4 ||
                !calipersDetachedFromWheels ||
                !caliperPivotsVerified ||
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
                interiorAccentPaintSlots == 0 ||
                caliperSlots != 4 ||
                rimSlots != 4 ||
                rimMaterials.Count != 1 ||
                !rimFinishValid ||
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
                    $"sevenSpeed={transmissionVerified}, launchResponse={launchResponseVerified}, " +
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
                $"Porsche911GT3RS bundle verified: price={price}, speed={maxSpeed}, " +
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
                throw new InvalidOperationException("Could not create the Porsche VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated Porsche VehicleType asset did not load.");

        target.name = "Porsche911GT3RS";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 223800f);
        SetNumber(serialized, "maxFuel", 64f);
        SetNumber(serialized, "maxCargoCapacity", 2f);
        SetNumber(serialized, "maxSpeed", 296f);
        SetNumber(serialized, "enginePower", 386f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", 26f);
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
            throw new InvalidOperationException("Porsche VehicleType asset was not found.");

        var serialized = new SerializedObject(vehicleType);
        SetNumber(serialized, "maxSpeed", 296f);
        SetNumber(serialized, "enginePower", 386f);
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
            throw new InvalidOperationException("Porsche 992 GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "Porsche911GT3RS";
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
                throw new InvalidOperationException("Could not instantiate the Porsche model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "PorscheVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureExitMarkers(root, modelInstance);
            AssignPersistentMaterials(modelInstance);
            AttachWheelVisuals(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            var fix = Porsche911GT3RSMaterials.FixSolidMaterials(root);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);

            Debug.Log(
                $"Porsche911GT3RS: prepared decal-safe materials renderers={fix.RendererCount}, " +
                $"decalMasksCleared={fix.DecalMasksCleared}, " +
                $"opaqueFixed={fix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={fix.TransparentMaterialsFixed}, " +
                $"cabinGlass={fix.CabinGlassRenderers}/" +
                $"reenabled={fix.CabinGlassRenderersReenabled}, " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, " +
                $"hdrpValidated={fix.MaterialsValidated}.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Porsche vehicle prefab.");
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
        body.mass = 1450f;
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
            SetNumber(serialized, "baseMass", 1450f);
            SetNumber(serialized, "combinedMass", 1450f);
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
            var mount = FindTransform(root.transform, "PorscheWheel" + corner) ??
                        throw new InvalidOperationException($"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "PorscheFixedCaliper" + corner) ??
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
                HasAncestorNameFragment(renderer.transform, "PorscheWheel"))
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
                if (HasAncestorNameFragment(renderer.transform, "PorscheWheel") &&
                    HasAncestorNameFragment(renderer.transform, "Geometry_PorscheTire_"))
                {
                    attachedTireCount++;
                }
            }

            if (attachedTireCount != 4)
                throw new InvalidOperationException(
                    $"Expected four attached Porsche tires, found {attachedTireCount}.");
            return;
        }
        if (tires.Count != 4)
            throw new InvalidOperationException(
                $"Expected four body-static Porsche tires, found {tires.Count}.");

        foreach (var pair in WheelControllerPositions)
        {
            var corner = pair.Key.Replace("_WheelController", string.Empty);
            var controller = FindTransform(root.transform, pair.Key) ??
                             throw new InvalidOperationException(
                                 $"Wheel controller '{pair.Key}' is missing.");
            var mount = FindTransform(root.transform, "PorscheWheel" + corner) ??
                        throw new InvalidOperationException(
                            $"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "PorscheFixedCaliper" + corner) ??
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
            tire.name = "Geometry_PorscheTire_" +
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
        var steeringWheel = FindTransformWithNameFragment(model.transform, "steer_3") ??
                            throw new InvalidOperationException("Model steering wheel is missing.");
        var steeringPosition = root.transform.InverseTransformPoint(steeringWheel.position);
        var driverSide = steeringPosition.x < 0f ? -1.45f : 1.45f;
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.1f, 0f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.1f, 0f));
        Debug.Log(
            $"Porsche911GT3RS: steering wheel x={steeringPosition.x:F3}; " +
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
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.075f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 386f);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateGT3RSPowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 900f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 9000f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.38f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 4.27f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 7f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.065f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 5500f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 8850f);
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
                    // The reference center differential's B output is the rear differential.
                    bias.floatValue = 1f;
                    rearDriveConfigured = true;
                }
                if (!rearDriveConfigured)
                    throw new InvalidOperationException("Center differential could not be configured for RWD.");

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = GT3RSGears.Length;
                for (var index = 0; index < GT3RSGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = GT3RSGears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 296f);
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
            throw new InvalidOperationException("Porsche model length could not be measured.");

        model.transform.localRotation = candidates[bestIndex];
        var front = FindTransformWithNameFragment(model.transform, "gt3rs_bumper_F");
        var rear = FindTransformWithNameFragment(model.transform, "gt3rs_bumper_R");
        if (front != null && rear != null &&
            TryGetRendererBounds(front, out var frontBounds) &&
            TryGetRendererBounds(rear, out var rearBounds) &&
            frontBounds.center.z < rearBounds.center.z)
        {
            model.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
        }

        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Oriented Porsche body bounds could not be measured.");
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
            throw new InvalidOperationException("Scaled Porsche bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Porsche bounds could not be measured.");

        Debug.Log(
            $"Porsche911GT3RS: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var wheels = new List<Transform>();
        var tires = new List<Transform>();
        var brakes = new List<Transform>();
        foreach (var candidate in model.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.childCount == 2 && candidate.name.StartsWith(
                    "TwiXeR_992_gt3rs_style_1_chrome_wheels_20x9",
                    StringComparison.OrdinalIgnoreCase))
                wheels.Add(candidate);
            else if (candidate.childCount == 4 && candidate.name.StartsWith(
                         "amdb11_brakedisc_", StringComparison.OrdinalIgnoreCase))
                brakes.Add(candidate);
        }

        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (HasAncestorNameFragment(renderer.transform, "Scene_-_Root.002"))
                tires.Add(renderer.transform);
        }

        if (wheels.Count != 4 || tires.Count != 4 || brakes.Count != 4)
            throw new InvalidOperationException(
                $"Expected four Porsche rolling assemblies; found rims={wheels.Count}, " +
                $"tires={tires.Count}, brakes={brakes.Count}.");

        var wheelCenters = new Dictionary<Transform, Vector3>();
        var wheelTires = new Dictionary<Transform, Transform>();
        var unmatchedTires = new List<Transform>(tires);
        var averageZ = 0f;
        foreach (var wheel in wheels)
        {
            var tire = FindClosestPart(wheel, unmatchedTires) ??
                       throw new InvalidOperationException($"Rim '{wheel.name}' has no tire mesh.");
            unmatchedTires.Remove(tire);
            if (!TryGetRendererBounds(tire, out var tireBounds))
                throw new InvalidOperationException($"Wheel '{wheel.name}' has no renderer bounds.");
            var center = root.transform.InverseTransformPoint(tireBounds.center);
            wheelCenters.Add(wheel, center);
            wheelTires.Add(wheel, tire);
            averageZ += center.z;
        }
        averageZ /= wheels.Count;

        foreach (var wheel in wheels)
        {
            var authoredCenter = wheelCenters[wheel];
            var isFront = authoredCenter.z >= averageZ;
            var isLeft = authoredCenter.x < 0f;
            var corner = (isFront ? "Front" : "Rear") + (isLeft ? "Left" : "Right");
            var controllerName = corner + "_WheelController";
            var controller = FindTransform(root.transform, controllerName) ??
                             throw new InvalidOperationException($"Wheel controller '{controllerName}' is missing.");
            var radius = isFront ? FrontTireRadius : RearTireRadius;
            var width = isFront ? FrontTireWidth : RearTireWidth;
            var targetCenter = WheelControllerPositions[controllerName];
            controller.localPosition = targetCenter;

            var tire = wheelTires[wheel];
            if (!TryGetRendererBounds(tire, out var sourceBounds) ||
                sourceBounds.size.x <= 0.001f || sourceBounds.size.y <= 0.001f ||
                sourceBounds.size.z <= 0.001f)
                throw new InvalidOperationException($"Tire for '{wheel.name}' has invalid bounds.");
            var brake = FindClosestBrake(wheel, brakes) ??
                        throw new InvalidOperationException($"Wheel '{wheel.name}' has no brake assembly.");
            brakes.Remove(brake);

            var mount = new GameObject("PorscheWheel" + corner);
            mount.transform.SetParent(root.transform, false);
            // The GLB stores the visible tire as a separate Scene_-_Root.002
            // mesh beside the chrome rim hierarchy. Both must share the same
            // NWH visual mount or the tire remains body-static while steering.
            mount.transform.position = sourceBounds.center;
            wheel.SetParent(mount.transform, true);
            tire.SetParent(mount.transform, true);
            brake.SetParent(mount.transform, true);
            wheel.name = "Geometry_PorscheWheel_" +
                         (isFront ? "F" : "R") + (isLeft ? "L" : "R");
            tire.name = "Geometry_PorscheTire_" +
                        (isFront ? "F" : "R") + (isLeft ? "L" : "R");
            mount.transform.localScale = new Vector3(
                width / sourceBounds.size.x,
                (radius * 2f) / sourceBounds.size.y,
                (radius * 2f) / sourceBounds.size.z);
            mount.transform.localPosition = targetCenter;

            var caliper = FindTransformWithNameFragment(brake, "_caliper") ??
                          throw new InvalidOperationException(
                              $"Brake assembly '{brake.name}' has no caliper renderer.");
            var fixedCaliper = new GameObject("PorscheFixedCaliper" + corner);
            fixedCaliper.transform.SetParent(root.transform, false);
            fixedCaliper.transform.localPosition = targetCenter;
            caliper.SetParent(fixedCaliper.transform, true);

            AssignWheelVisual(controller, mount);
            Debug.Log(
                $"Porsche911GT3RS: fitted {corner} authored={authoredCenter} target={targetCenter}, " +
                $"sourceTire={sourceBounds.size}, tire={width:F3}x{radius * 2f:F3}m, " +
                $"brake='{brake.name}'.");
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
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (HasMaterialMarker(renderer, "carPaint") &&
                HasAncestorNameFragment(renderer.transform, "body_gt3rs"))
            {
                sourceRenderer = renderer;
                break;
            }
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The Porsche outer body mesh was not found.");

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        bakedMesh.name = "PorscheDamageBody";
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

        var damageBody = new GameObject("PorscheDamageBody")
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

    private static bool IsBodyPaintMaterial(Material material) =>
        material.name.IndexOf("carPaint", StringComparison.OrdinalIgnoreCase) >= 0;

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
        material.name.IndexOf("wheels_chrome_1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!Porsche911GT3RSMaterials.IsPorscheRenderer(renderer.transform))
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
            throw new InvalidOperationException("The shared Porsche rim material is missing.");

        ApplyRimFinish(leftMaterial, Porsche911GT3RSMaterials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!Porsche911GT3RSMaterials.IsPorscheRenderer(renderer.transform))
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
                $"Expected four Porsche rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", Porsche911GT3RSMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", Porsche911GT3RSMaterials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("amdb11_caliper", StringComparison.OrdinalIgnoreCase) >= 0;

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
                    var transparent = Porsche911GT3RSMaterials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"Porsche{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

        manifest.ModId = "Porsche911GT3RS";
        manifest.DisplayName = "Porsche 911 GT3 RS";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "porsche911gt3rs.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Porsche manifest references could not be assigned.");
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
            var belongsToSourceWheel = false;
            while (current != null && current != root)
            {
                if (current.name.IndexOf(
                        "gt3rs_style_1_chrome_wheels_20x9",
                        StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.StartsWith("amdb11_brakedisc_", StringComparison.OrdinalIgnoreCase))
                {
                    belongsToSourceWheel = true;
                    break;
                }
                current = current.parent;
            }

            if (belongsToSourceWheel)
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

    private static bool TryGetPorscheRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!Porsche911GT3RSMaterials.IsPorscheRenderer(renderer.transform))
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

