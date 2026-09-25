#nullable enable
using System;
using System.Collections.Generic;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class LamborghiniRevueltoSetup
{
    private const string ModRoot = "Assets/Mods/LamborghiniRevuelto";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/free_lamborghini_revuelto.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/LamborghiniDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/LamborghiniRevuelto.asset";
    private const string VehiclePrefabPath = ModRoot + "/LamborghiniRevuelto.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/LamborghiniRevuelto.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/lamborghinirevuelto.unity3d";
    private const string VehicleTypeName =
        "lamborghinirevuelto-vehicle:vehicletype_lamborghinirevuelto";
    private const float TargetLength = 4.947f;
    private const float TargetWidth = 2.033f;
    private const float TargetHeight = 1.160f;
    private const float TireFrictionCircleStrength = 0.92f;
    private const float AntiRollBarForce = 7800f;
    private const float FrontSuspensionTravel = 0.08f;
    private const float RearSuspensionTravel = 0.06f;
    private const float FrontWheelOutset = 0.03f;
    private const float RearWheelOutset = 0f;
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.67f, 1.68f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.94f, 0.46f, 1.10f);
    private const float DeformationStrength = 0.20f;
    private const float DeformationRadius = 0.22f;
    private const float DeformationRandomness = 0.005f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.10f, -0.08f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-0.8515f, 0.348f, 1.3202f) },
            { "FrontRight_WheelController", new Vector3(0.8515f, 0.348f, 1.3202f) },
            { "RearLeft_WheelController", new Vector3(-0.8056f, 0.370f, -1.5811f) },
            { "RearRight_WheelController", new Vector3(0.8056f, 0.370f, -1.5811f) },
        };

    private static readonly float[] RevueltoGears =
    {
        -3.13f,
        0f,
        3.08f,
        2.19f,
        1.63f,
        1.29f,
        1.03f,
        0.84f,
        0.69f,
        0.58f,
    };

    private static AnimationCurve CreateRevueltoPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.23f, 0.17f),
            new Keyframe(0.55f, 0.36f),
            new Keyframe(0.78f, 0.58f),
            new Keyframe(0.90f, 1f),
            new Keyframe(1f, 0.88f));

    [MenuItem("Big Ambitions Mods/Setup Lamborghini Revuelto")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "LamborghiniRevuelto setup complete: generated a fitted Revuelto with four " +
            "independent wheel visuals, eight-speed DCT, AWD, V12 audio, functional lamp " +
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
            var luxury = vehicleSerialized.FindProperty("isLuxuryCar")?.boolValue ?? true;

            var visual = FindTransform(prefab.transform, "LamborghiniVisual") ??
                         throw new InvalidOperationException("Lamborghini visual root is missing.");
            if (!TryGetLamborghiniRendererBounds(prefab.transform, out var bounds))
                throw new InvalidOperationException("Lamborghini visual has no renderer bounds.");
            var bodySidesOriented =
                bounds.size.x > bounds.size.y * 1.4f &&
                bounds.size.z > bounds.size.x * 2f;
            var frontMarker = FindTransform(visual, "Daylight");
            var rearMarker = FindTransform(visual, "Tail_light");
            var frontFacesVehicleForward =
                frontMarker != null && rearMarker != null &&
                TryGetRendererBounds(frontMarker, out var frontMarkerBounds) &&
                TryGetRendererBounds(rearMarker, out var rearMarkerBounds) &&
                frontMarkerBounds.center.z > rearMarkerBounds.center.z + 2f;
            var windshield = FindTransform(visual, "Windshield");
            var exhaust = FindTransform(visual, "Exhaust_1_Exhaust_0");
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
                if (transform.name.StartsWith("LamborghiniWheel", StringComparison.Ordinal))
                {
                    wheelVisuals++;
                    var tire = FindTransformWithNameFragment(transform, "_Tire_");
                    var isFrontWheel = transform.name.IndexOf("Front", StringComparison.Ordinal) >= 0;
                    var expectedWidth = isFrontWheel ? 0.265f : 0.345f;
                    var expectedDiameter = isFrontWheel ? 0.696f : 0.740f;
                    if (tire == null ||
                        !TryGetRendererBounds(tire, out var tireBounds) ||
                        Math.Abs(tireBounds.size.x - expectedWidth) > 0.012f ||
                        Math.Abs(tireBounds.size.y - expectedDiameter) > 0.012f ||
                        Math.Abs(tireBounds.size.z - expectedDiameter) > 0.012f ||
                        Vector3.Distance(tireBounds.center, transform.position) > 0.012f)
                    {
                        wheelGeometryOriented = false;
                    }

                    if (FindTransformWithNameFragment(transform, "_Caliper_") != null)
                        calipersDetachedFromWheels = false;
                    fittedWheelCenters[transform.name] = transform.position;

                    var expectedGeometry = transform.name switch
                    {
                        "LamborghiniWheelFrontLeft" => "Geometry_Wheel_FL",
                        "LamborghiniWheelFrontRight" => "Geometry_Wheel_FR",
                        "LamborghiniWheelRearLeft" => "Geometry_Wheel_BL",
                        "LamborghiniWheelRearRight" => "Geometry_Wheel_BR",
                        _ => string.Empty,
                    };
                    if (string.IsNullOrEmpty(expectedGeometry) ||
                        FindTransform(transform, expectedGeometry) == null)
                    {
                        wheelSideMappingCorrect = false;
                    }
                }
                if (transform.name.StartsWith("LamborghiniFixedCaliper", StringComparison.Ordinal))
                {
                    fixedCalipers++;
                    if (FindTransformWithNameFragment(transform, "_Caliper_") == null)
                        calipersDetachedFromWheels = false;
                    fittedCaliperCenters[transform.name] = transform.position;
                }
                if (string.Equals(
                        transform.name,
                        "Tail_light_Tail_light_0",
                        StringComparison.Ordinal))
                {
                    continuousTailLight = true;
                }
                if (string.Equals(transform.name, "Brake_light_Tail_light_brake_0", StringComparison.Ordinal))
                    thirdBrakeLight = true;
                if (string.Equals(transform.name, "Hood.075_Turning_light_Left_0", StringComparison.Ordinal) ||
                    string.Equals(transform.name, "Hood.075_Turning_light_right_0", StringComparison.Ordinal))
                    frontBlinkerMeshes++;
                if (transform.name.StartsWith("Door-left_Turning_lights_", StringComparison.Ordinal) ||
                    transform.name.StartsWith("Door-right_Turning_lights_", StringComparison.Ordinal))
                    sideBlinkerMeshes++;
            }

            var frontLeftCenter = Vector3.zero;
            var frontRightCenter = Vector3.zero;
            var rearLeftCenter = Vector3.zero;
            var rearRightCenter = Vector3.zero;
            var wheelPlacementVerified =
                fittedWheelCenters.TryGetValue("LamborghiniWheelFrontLeft", out frontLeftCenter) &&
                fittedWheelCenters.TryGetValue("LamborghiniWheelFrontRight", out frontRightCenter) &&
                fittedWheelCenters.TryGetValue("LamborghiniWheelRearLeft", out rearLeftCenter) &&
                fittedWheelCenters.TryGetValue("LamborghiniWheelRearRight", out rearRightCenter);
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
                frontTrack >= 1.68f && frontTrack <= 1.72f &&
                rearTrack >= 1.59f && rearTrack <= 1.63f &&
                wheelbase >= 2.88f && wheelbase <= 2.92f &&
                Math.Abs(frontLeftCenter.z - frontRightCenter.z) < 0.012f &&
                Math.Abs(rearLeftCenter.z - rearRightCenter.z) < 0.012f;
            var caliperPivotsVerified = wheelPlacementVerified &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "LamborghiniFixedCaliperFrontLeft",
                    frontLeftCenter) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "LamborghiniFixedCaliperFrontRight",
                    frontRightCenter) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "LamborghiniFixedCaliperRearLeft",
                    rearLeftCenter) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "LamborghiniFixedCaliperRearRight",
                    rearRightCenter);

            var transmissionVerified = false;
            var launchResponseVerified = false;
            var antiRollVerified = false;
            var massCenterVerified = false;
            var tireFrictionCount = 0;
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
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRPM")) - 1400f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("throttleEngagementOffsetRPM")) - 700f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRange")) - 650f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("creepTorque"))) < 0.01f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("inertia")) - 0.09f) < 0.001f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("startDuration")) - 0.42f) < 0.001f &&
                    RevueltoPowerCurveMatches(powerCurve) &&
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
                if (!LamborghiniRevueltoMaterials.IsLamborghiniRenderer(renderer.transform))
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
                    if (IsInteriorAccentPaintMaterial(material)) interiorAccentPaintSlots++;
                    if (IsDarkBodyPaintMaterial(material)) darkBodyPaintSlots++;
                    if (IsRimMaterial(material))
                    {
                        rimSlots++;
                        rimMaterials.Add(material);
                        var expectedRimColor = LamborghiniRevueltoMaterials.RimBaseColor;
                        var rimColor = material.HasProperty("_BaseColor")
                            ? material.GetColor("_BaseColor")
                            : Color.clear;
                        rimFinishValid &=
                            material.HasProperty("_Metallic") &&
                            Math.Abs(material.GetFloat("_Metallic") - LamborghiniRevueltoMaterials.RimMetallic) < 0.01f &&
                            material.HasProperty("_Smoothness") &&
                            Math.Abs(material.GetFloat("_Smoothness") - LamborghiniRevueltoMaterials.RimSmoothness) < 0.01f &&
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
                    if (LamborghiniRevueltoMaterials.IsTransparentMaterial(material))
                    {
                        transparentMaterials++;
                        transparentMaterialsDoubleSided &=
                            (!material.HasProperty("_Cull") || material.GetFloat("_Cull") < 0.5f) &&
                            (!material.HasProperty("_DoubleSidedEnable") ||
                             material.GetFloat("_DoubleSidedEnable") > 0.5f) &&
                            material.IsKeywordEnabled("_DOUBLESIDED_ON");
                        if (LamborghiniRevueltoMaterials.IsCabinGlassMaterial(material))
                        {
                            var tint = material.HasProperty("_BaseColor")
                                ? material.GetColor("_BaseColor")
                                : material.HasProperty("baseColorFactor")
                                    ? material.GetColor("baseColorFactor")
                                    : Color.black;
                            cabinGlassTintValid &= tint.r >= 0.04f &&
                                                   tint.a >= 0.40f &&
                                                   tint.a <= 0.50f &&
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
                            "LamborghiniDamageBody",
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

            if (Math.Abs(price - 608358f) > 0.5f ||
                Math.Abs(maxFuel - 85f) > 0.5f ||
                Math.Abs(maxSpeed - 355f) > 0.5f ||
                Math.Abs(enginePower - 747f) > 0.5f ||
                !luxury ||
                bounds.size.z < 4.90f || bounds.size.z > 5.00f ||
                bounds.size.x < 1.98f || bounds.size.x > 2.08f ||
                bounds.size.y < 1.10f || bounds.size.y > 1.22f ||
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
                frontBlinkerMeshes != 2 ||
                !headlightTemplateValid ||
                !transmissionVerified ||
                !launchResponseVerified ||
                !massCenterVerified ||
                !antiRollVerified ||
                tireFrictionCount != 4 ||
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
                    $"speed={maxSpeed}, power={enginePower}, luxury={luxury}, " +
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
                    $"massCenter={massCenterVerified}, antiRoll={antiRollVerified}, " +
                    $"tireFrictionCount={tireFrictionCount}, " +
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
                $"LamborghiniRevuelto bundle verified: price={price}, speed={maxSpeed}, " +
                $"power={enginePower}, bounds={bounds.size}, wheels=4, eightSpeed=true, " +
                $"fixedCalipers=4, steeringCaliperPivots=true, tireBoundsCentered=true, wheelbase={wheelbase:F3}, " +
                $"frontTrack={frontTrack:F3}, rearTrack={rearTrack:F3}, " +
                $"stableCenterOfMass=true, tireFriction={TireFrictionCircleStrength:F2}, " +
                $"suspensionTravel={FrontSuspensionTravel:F2}/{RearSuspensionTravel:F2}, " +
                $"damageBody=outer-shell-only, deformation={DeformationStrength:F2}/{DeformationRadius:F2}, " +
                $"launchResponse=true, " +
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
                throw new InvalidOperationException("Could not create the Lamborghini VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated Lamborghini VehicleType asset did not load.");

        target.name = "LamborghiniRevuelto";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 608358f);
        SetNumber(serialized, "maxFuel", 85f);
        SetNumber(serialized, "maxCargoCapacity", 4f);
        SetNumber(serialized, "maxSpeed", 355f);
        SetNumber(serialized, "enginePower", 747f);
        SetNumber(serialized, "brakeForce", 26000f);
        SetNumber(serialized, "turnRadius", 27f);
        SetNumber(serialized, "damageIntensity", 0.42f);
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
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R reference prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("Lamborghini GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "LamborghiniRevuelto";
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
                throw new InvalidOperationException("Could not instantiate the Lamborghini model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "LamborghiniVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureExitMarkers(root, modelInstance);
            AssignPersistentMaterials(modelInstance);
            AttachWheelVisuals(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            var fix = LamborghiniRevueltoMaterials.FixSolidMaterials(root);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);

            Debug.Log(
                $"LamborghiniRevuelto: prepared decal-safe materials renderers={fix.RendererCount}, " +
                $"decalMasksCleared={fix.DecalMasksCleared}, " +
                $"opaqueFixed={fix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={fix.TransparentMaterialsFixed}, " +
                $"cabinGlass={fix.CabinGlassRenderers}/" +
                $"reenabled={fix.CabinGlassRenderersReenabled}, " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, " +
                $"hdrpValidated={fix.MaterialsValidated}.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Lamborghini vehicle prefab.");
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
        body.mass = 1772f;
        body.drag = 0f;
        body.angularDrag = 1.45f;
        body.centerOfMass = StableCenterOfMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.solverIterations = Mathf.Max(body.solverIterations, 12);
        body.solverVelocityIterations = Mathf.Max(body.solverVelocityIterations, 4);

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
            SetNumber(serialized, "baseMass", 1772f);
            SetNumber(serialized, "combinedMass", 1772f);
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
                SetRelativeNumber(serialized, "spring.maxForce", 20500f);
                SetRelativeNumber(serialized, "wheel.radius", isFront ? 0.348f : 0.370f);
                SetRelativeNumber(serialized, "wheel.width", isFront ? 0.265f : 0.345f);
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

        colliders[0].center = new Vector3(0f, 0.38f, -0.02f);
        colliders[0].size = new Vector3(1.96f, 0.46f, 4.82f);
        colliders[1].center = new Vector3(0f, 0.78f, -0.18f);
        colliders[1].size = new Vector3(1.72f, 0.62f, 2.62f);
        var frontContactCollider = colliders.Length > 2
            ? colliders[2]
            : holder.gameObject.AddComponent<BoxCollider>();
        frontContactCollider.center = FrontContactColliderCenter;
        frontContactCollider.size = FrontContactColliderSize;
        frontContactCollider.isTrigger = false;
        frontContactCollider.enabled = true;
    }

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        var steeringWheel = FindTransform(model.transform, "Steering_wheel") ??
                            throw new InvalidOperationException("Model steering wheel is missing.");
        var steeringPosition = root.transform.InverseTransformPoint(steeringWheel.position);
        var driverSide = steeringPosition.x < 0f ? -1.45f : 1.45f;
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.1f, 0f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.1f, 0f));
        Debug.Log(
            $"LamborghiniRevuelto: steering wheel x={steeringPosition.x:F3}; " +
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
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1400f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 700f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 650f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.09f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 747f);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateRevueltoPowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 1000f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 9500f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.42f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.15f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.065f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 6500f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 9250f);
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
                gears.arraySize = RevueltoGears.Length;
                for (var index = 0; index < RevueltoGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = RevueltoGears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 355f);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!found)
            throw new InvalidOperationException("NWH vehicle controller was not found on the reference prefab.");
    }

    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        // glTFast imports this Blender model with its authored length on Unity Y
        // and height on Z. Rotate it once so Y is up.
        model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        model.transform.localScale = Vector3.one;

        if (!TryGetModelBodyBounds(model.transform, out var bounds))
            throw new InvalidOperationException("Lamborghini model contains no renderers.");

        var headlights = FindTransform(model.transform, "Daylight");
        var tailLights = FindTransform(model.transform, "Tail_light");
        if (headlights != null && tailLights != null &&
            TryGetRendererBounds(headlights, out var headBounds) &&
            TryGetRendererBounds(tailLights, out var tailBounds) &&
            headBounds.center.z < tailBounds.center.z)
        {
            // Yaw around the chassis/parent up axis. Rotating in self space here
            // would turn around the GLB's authored longitudinal axis instead.
            model.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
        }

        if (!TryGetModelBodyBounds(model.transform, out bounds) || bounds.size.z <= 0.001f)
            throw new InvalidOperationException("Lamborghini model length could not be measured.");

        var scale = new Vector3(
            TargetWidth / bounds.size.x,
            TargetLength / bounds.size.z,
            TargetHeight / bounds.size.y);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled Lamborghini bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Lamborghini bounds could not be measured.");

        Debug.Log(
            $"LamborghiniRevuelto: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var mapping = new Dictionary<string, string>
        {
            { "Wheel_FL", "FrontLeft_WheelController" },
            { "Wheel_FR", "FrontRight_WheelController" },
            { "Wheel_BL", "RearLeft_WheelController" },
            { "Wheel_BR", "RearRight_WheelController" },
        };

        foreach (var pair in mapping)
        {
            var wheel = FindTransform(model.transform, pair.Key) ??
                        throw new InvalidOperationException($"Model wheel '{pair.Key}' is missing.");
            var controller = FindTransform(root.transform, pair.Value) ??
                             throw new InvalidOperationException($"Wheel controller '{pair.Value}' is missing.");
            var isFront = pair.Value.StartsWith("Front", StringComparison.Ordinal);
            var radius = isFront ? 0.348f : 0.370f;
            var width = isFront ? 0.265f : 0.345f;
            var tire = FindTransformWithNameFragment(wheel, "_Tire_") ??
                       throw new InvalidOperationException(
                           $"Model wheel '{pair.Key}' has no dedicated tire renderer.");
            if (!TryGetRendererBounds(tire, out var tireBounds) ||
                tireBounds.size.x <= 0.001f ||
                tireBounds.size.y <= 0.001f ||
                tireBounds.size.z <= 0.001f)
            {
                throw new InvalidOperationException($"Model wheel '{pair.Key}' has invalid tire bounds.");
            }

            // Use the supplied model's wheel center for X/Z so physics and
            // visuals share the actual wheel-arch locations. Only Y is replaced
            // with the physical radius to put the contact patch on the ground.
            var authoredCenter = root.transform.InverseTransformPoint(tireBounds.center);
            var side = authoredCenter.x < 0f ? -1f : 1f;
            var outset = isFront ? FrontWheelOutset : RearWheelOutset;
            controller.localPosition = new Vector3(
                authoredCenter.x + side * outset,
                radius,
                authoredCenter.z);
            var mount = new GameObject(
                "LamborghiniWheel" +
                pair.Value.Replace("_WheelController", "").Replace("_", string.Empty));
            mount.transform.SetParent(root.transform, false);
            mount.transform.localPosition =
                new Vector3(
                    controller.localPosition.x,
                    radius,
                    controller.localPosition.z);

            // Preserve the GLB's side-specific hierarchy and rotation. Resetting
            // both sides to one rotation turns the authored inner rim faces out.
            wheel.SetParent(mount.transform, true);
            wheel.name = "Geometry_" + pair.Key;

            // Fit and center from the tire alone. The authored brake caliper is
            // deliberately off-axis, so including it in the aggregate bounds
            // shifts and distorts the complete rotating assembly.
            mount.transform.localScale = new Vector3(
                width / tireBounds.size.x,
                (radius * 2f) / tireBounds.size.y,
                (radius * 2f) / tireBounds.size.z);
            if (!TryGetRendererBounds(tire, out tireBounds))
                throw new InvalidOperationException($"Model wheel '{pair.Key}' tire could not be fitted.");
            wheel.position += mount.transform.position - tireBounds.center;
            Debug.Log(
                $"LamborghiniRevuelto: fitted {pair.Key} to authored arch " +
                $"center=({authoredCenter.x:F3},{radius:F3},{authoredCenter.z:F3}), " +
                $"tire={width:F3}x{radius * 2f:F3}m.");

            // The rotor remains part of the rolling visual, while the caliper
            // keeps its fitted world pose under a chassis-owned mount and can no
            // longer inherit wheel spin from NWH's visual transform.
            var caliper = FindTransformWithNameFragment(wheel, "_Caliper_") ??
                          throw new InvalidOperationException(
                              $"Model wheel '{pair.Key}' has no brake caliper renderer.");
            var fixedCaliper = new GameObject(
                "LamborghiniFixedCaliper" +
                pair.Value.Replace("_WheelController", "").Replace("_", string.Empty));
            fixedCaliper.transform.SetParent(root.transform, false);
            fixedCaliper.transform.position = mount.transform.position;
            fixedCaliper.transform.rotation = root.transform.rotation;
            caliper.SetParent(fixedCaliper.transform, true);

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

    private static MeshFilter CreateDeformableBody(GameObject root, GameObject modelInstance)
    {
        MeshRenderer? sourceRenderer = null;
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (string.Equals(renderer.name, "Hood.075_Body_0", StringComparison.Ordinal))
            {
                sourceRenderer = renderer;
                break;
            }
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The Lamborghini outer body mesh was not found.");

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        bakedMesh.name = "LamborghiniDamageBody";
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

        var damageBody = new GameObject("LamborghiniDamageBody")
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
        material.name.IndexOf("_Body", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorAccentPaintMaterial(Material material) =>
        material.name.IndexOf("_Interior_color", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRimMaterial(Material material) =>
        material.name.IndexOf("LamborghiniOpaque_19_material", StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!LamborghiniRevueltoMaterials.IsLamborghiniRenderer(renderer.transform))
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
            throw new InvalidOperationException("The shared Lamborghini rim material is missing.");

        ApplyRimFinish(leftMaterial, LamborghiniRevueltoMaterials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!LamborghiniRevueltoMaterials.IsLamborghiniRenderer(renderer.transform))
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
                $"Expected four Lamborghini rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", LamborghiniRevueltoMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", LamborghiniRevueltoMaterials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("_Caliper", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool RevueltoPowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateRevueltoPowerCurve();
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
        material.name.IndexOf("LamborghiniOpaque_03_Brake_rotor", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsSeatPaintMaterial(Material material) =>
        material.name.IndexOf("LamborghiniOpaque_19_seats", StringComparison.OrdinalIgnoreCase) >= 0;

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
        material.name.IndexOf("LamborghiniOpaque_06_Darker_Parts", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorPrimaryPaintMaterial(Material material) =>
        material.name.IndexOf("LamborghiniOpaque_13_Interior_1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorSecondaryPaintMaterial(Material material) =>
        material.name.IndexOf("LamborghiniOpaque_08_Interior_2", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorDarkPaintMaterial(Material material) =>
        material.name.IndexOf("LamborghiniOpaque_12_Interior_1_Darker", StringComparison.OrdinalIgnoreCase) >= 0;

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
                    var transparent = LamborghiniRevueltoMaterials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"Lamborghini{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

        manifest.ModId = "LamborghiniRevuelto";
        manifest.DisplayName = "Lamborghini Revuelto";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "lamborghinirevuelto.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Lamborghini manifest references could not be assigned.");
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
                if (current.name == "Wheel_FL" || current.name == "Wheel_FR" ||
                    current.name == "Wheel_BL" || current.name == "Wheel_BR")
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

    private static bool TryGetLamborghiniRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!LamborghiniRevueltoMaterials.IsLamborghiniRenderer(renderer.transform))
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
