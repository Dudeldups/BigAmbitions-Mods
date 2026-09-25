#nullable enable
using System;
using System.Collections.Generic;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class BMWM4G82Setup
{
    private const string ModRoot = "Assets/Mods/BMW_M4_G82";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/bmw_m4.glb";
    private const string LightingModelPath = ModRoot + "/Models/bmw_m4_lights.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/BMWDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/BMWM4G82.asset";
    private const string VehiclePrefabPath = ModRoot + "/BMWM4G82.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/BMWM4G82.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/bmw_m4_g82.unity3d";
    private const string VehicleTypeName =
        "bmwm4g82-vehicle:vehicletype_bmwm4g82";
    private const long ColouredAtlasTextureLocalId = 6107228799987672122L;
    private const float TargetLength = 4.794f;
    private const float TargetWidth = 1.887f;
    private const float TargetHeight = 1.394f;
    private const float VisualFrontAxleZ = 1.535f;
    private const float VisualRearAxleZ = -1.022f;
    private const float VisualWheelbase = VisualFrontAxleZ - VisualRearAxleZ;
    private const float FrontTrack = 1.617f;
    private const float RearTrack = 1.605f;
    private const float FrontTireWidth = 0.275f;
    private const float RearTireWidth = 0.285f;
    private const float FrontTireRadius = 0.3376f;
    private const float RearTireRadius = 0.3395f;
    private const float VisualBodyOffsetY = -0.035f;
    private const float VehicleLinearDrag = 0.015f;
    private const float CaliperOutboardOffset = 0.060f;
    private const float CaliperRearwardOffset = -0.020f;
    private const float ForwardTireGrip = 0.50f;
    private const float TireFrictionCircleStrength = 0.82f;
    private const float BrakeMaxTorque = 32000f;
    private const float BrakeActuationTime = 0.010f;
    private const float MaximumDepenetrationVelocity = 4.5f;
    private const float AntiRollBarForce = 8400f;
    private const float SteeringDegreesPerSecond = 95f;
    private const float MaximumSteerAngle = 32f;
    private const float FrontSuspensionTravel = 0.07f;
    private const float RearSuspensionTravel = 0.07f;
    private const float SuspensionBumpRate = 15000f;
    private const float SuspensionReboundRate = 17000f;
    private const float SuspensionExtensionSpeed = 6f;
    private const float MaximumDamagedWheelWobbleAngle = 1.5f;
    private const float DeformationStrength = 0.17f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private const float DamageIntensity = 0.62f;
    private const float DamageDecelerationThreshold = 500f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.06f, -0.08f);
    private static readonly Vector3 SteeringAnchorPosition = new Vector3(-0.38f, 0.92f, 0.55f);
    private static readonly Vector3 DriverExitPosition = new Vector3(-2.05f, 0.20f, 0.15f);
    private static readonly Vector3 PassengerExitPosition = new Vector3(2.05f, 0.20f, 0.15f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-0.8085f, FrontTireRadius, VisualFrontAxleZ) },
            { "FrontRight_WheelController", new Vector3(0.8085f, FrontTireRadius, VisualFrontAxleZ) },
            { "RearLeft_WheelController", new Vector3(-0.8025f, RearTireRadius, VisualRearAxleZ) },
            { "RearRight_WheelController", new Vector3(0.8025f, RearTireRadius, VisualRearAxleZ) },
        };

    private static readonly float[] M4Gears =
    {
        -3.478f,
        0f,
        5.000f,
        3.200f,
        2.143f,
        1.720f,
        1.313f,
        1.000f,
        0.823f,
        0.640f,
    };

    private static AnimationCurve CreateM4PowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.12f, 0.07f),
            new Keyframe(0.25f, 0.25f),
            new Keyframe(0.38f, 0.48f),
            new Keyframe(0.55f, 0.70f),
            new Keyframe(0.68f, 0.88f),
            new Keyframe(0.76f, 0.98f),
            new Keyframe(0.87f, 1f),
            new Keyframe(1f, 0.90f));

    [MenuItem("Big Ambitions Mods/Setup BMW M4 G82")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "BMWM4G82 setup complete: generated a fitted G82 M4 Competition with four " +
            "independent wheel visuals, eight-speed M Steptronic, M xDrive, inline-six audio, functional lamp " +
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
                throw new InvalidOperationException("Bundle is missing its BMW VehicleType or prefab.");

            var vehicleSerialized = new SerializedObject(vehicleType);
            var bundledVehicleTypeName =
                vehicleSerialized.FindProperty("vehicleTypeName")?.stringValue;
            if (!string.Equals(bundledVehicleTypeName, VehicleTypeName, StringComparison.Ordinal) ||
                !string.Equals(prefab.name, "BMWM4G82", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"BMW purchase identity mismatch vehicleType='{bundledVehicleTypeName}', " +
                    $"prefab='{prefab.name}'.");
            }

            var price = ReadNumber(vehicleSerialized.FindProperty("price"));
            var maxFuel = ReadNumber(vehicleSerialized.FindProperty("maxFuel"));
            var maxCargo = ReadNumber(vehicleSerialized.FindProperty("maxCargoCapacity"));
            var maxSpeed = ReadNumber(vehicleSerialized.FindProperty("maxSpeed"));
            var enginePower = ReadNumber(vehicleSerialized.FindProperty("enginePower"));
            var luxury = vehicleSerialized.FindProperty("isLuxuryCar")?.boolValue ?? false;
            if (Math.Abs(price - 79795f) > 0.1f || Math.Abs(maxFuel - 59f) > 0.1f ||
                Math.Abs(maxCargo - 12f) > 0.1f || Math.Abs(maxSpeed - 290f) > 0.1f ||
                Math.Abs(enginePower - 375f) > 0.1f || !luxury)
            {
                throw new InvalidOperationException(
                    $"BMW VehicleType mismatch price={price} fuel={maxFuel} cargo={maxCargo} " +
                    $"speed={maxSpeed} power={enginePower} luxury={luxury}.");
            }

            var requiredUniqueNames = new[]
            {
                "BMWVisual", "BodyCollider", "BMWDamageBody", "Steering_wheel",
                "Driverside", "Passengerside", "BMW_DRL_Source", "BMW_Lamp_Source",
                "BMW_RearLamp_Source", "FrontLeft_WheelController", "FrontRight_WheelController",
                "RearLeft_WheelController", "RearRight_WheelController", "BMWWheelFrontLeft",
                "BMWWheelFrontRight", "BMWWheelRearLeft", "BMWWheelRearRight",
                "BMWFixedCaliperFrontLeft", "BMWFixedCaliperFrontRight",
                "BMWFixedCaliperRearLeft", "BMWFixedCaliperRearRight",
                "BMW_Interior_Source", "BMW_EngineDetails_Source",
                "BMW_DashboardDisplay_Source",
                "BMWLightRef_FrontHeadlights", "BMWLightRef_MirrorTurnSignalLeft",
                "BMWLightRef_MirrorTurnSignalRight", "BMWLightRef_RearBrakeLights",
                "BMWLightRef_RearIndicatorLeft", "BMWLightRef_RearIndicatorRight",
                "BMWLightRef_RearReverseLights", "BMWLightRef_RearRunningLights",
                "BMWLightRef_FrontOuterDrlIndicatorLeft",
                "BMWLightRef_FrontOuterDrlIndicatorRight", "BMWLightRef_FrontInnerDrl",
            };
            foreach (var requiredName in requiredUniqueNames)
            {
                var count = CountTransformsNamed(prefab.transform, requiredName);
                if (count != 1)
                    throw new InvalidOperationException(
                        $"BMW prefab requires exactly one '{requiredName}' transform, found {count}.");
            }

            var missingScripts = 0;
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
                if (component == null) missingScripts++;
            var missingMaterials = 0;
            var missingMeshes = 0;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled)
                    continue;
                foreach (var material in renderer.sharedMaterials)
                    if (material == null) missingMaterials++;
                if (renderer is MeshRenderer &&
                    renderer.GetComponent<MeshFilter>()?.sharedMesh == null)
                    missingMeshes++;
            }
            if (missingScripts != 0 || missingMaterials != 0 || missingMeshes != 0)
                throw new InvalidOperationException(
                    $"BMW prefab has missing content scripts={missingScripts}, " +
                    $"materials={missingMaterials}, meshes={missingMeshes}.");

            var body = prefab.GetComponent<Rigidbody>() ??
                       throw new InvalidOperationException("BMW prefab root Rigidbody is missing.");
            if (Math.Abs(body.mass - 1775f) > 0.1f ||
                Vector3.Distance(body.centerOfMass, StableCenterOfMass) > 0.001f)
                throw new InvalidOperationException(
                    $"BMW root physics mismatch mass={body.mass}, center={body.centerOfMass}.");
            var steeringAnchor = FindTransform(prefab.transform, "Steering_wheel")!;
            var driverExit = FindTransform(prefab.transform, "Driverside")!;
            var passengerExit = FindTransform(prefab.transform, "Passengerside")!;
            if (Vector3.Distance(steeringAnchor.localPosition, SteeringAnchorPosition) > 0.001f ||
                Vector3.Distance(driverExit.localPosition, DriverExitPosition) > 0.001f ||
                Vector3.Distance(passengerExit.localPosition, PassengerExitPosition) > 0.001f)
                throw new InvalidOperationException(
                    $"BMW LHD cabin anchors mismatch steering={steeringAnchor.localPosition}, " +
                    $"exits={driverExit.localPosition}/{passengerExit.localPosition}.");

            var visual = FindTransform(prefab.transform, "BMWVisual") ??
                         throw new InvalidOperationException("BMW visual root is missing.");
            if (Math.Abs(visual.localPosition.y - VisualBodyOffsetY) > 0.001f)
                throw new InvalidOperationException(
                    $"BMW visual body offset mismatch y={visual.localPosition.y:F3}.");
            var authoredUp = visual.TransformDirection(Vector3.forward).normalized;
            if (Vector3.Dot(authoredUp, prefab.transform.up) < 0.99f)
                throw new InvalidOperationException(
                    $"BMW body is not upright: authoredUp={authoredUp}, chassisUp={prefab.transform.up}.");
            if (!TryGetModelBodyBounds(visual, out var bounds) ||
                Math.Abs(bounds.size.x - TargetWidth) > 0.06f ||
                Math.Abs(bounds.size.y - TargetHeight) > 0.06f ||
                Math.Abs(bounds.size.z - TargetLength) > 0.06f)
            {
                throw new InvalidOperationException($"BMW body bounds are invalid: {bounds.size}.");
            }
            if (prefab.GetComponents<BMWM4G82SpawnConfigurator>().Length != 1)
                throw new InvalidOperationException(
                    "BMW prefab requires exactly one spawn-time configurator.");

            var centers = new Dictionary<string, Vector3>();
            var wheelCount = 0;
            var caliperCount = 0;
            var caliperTriangles = 0;
            var correctlyShapedCalipers = 0;
            var originalCaliperRenderers = 0;
            var centerCapCount = 0;
            var neutralCenterCaps = 0;
            var rollingParts = 0;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.StartsWith("BMWWheel", StringComparison.Ordinal))
                {
                    wheelCount++;
                    centers[transform.name] = transform.localPosition;
                    var wheelRenderers = transform.GetComponentsInChildren<MeshRenderer>(true);
                    rollingParts += wheelRenderers.Length;
                    foreach (var renderer in wheelRenderers)
                    {
                        if (!renderer.name.Contains("Material_001"))
                            continue;
                        centerCapCount++;
                        if (Array.TrueForAll(renderer.sharedMaterials, material =>
                                material != null && material.HasProperty("_BaseColor") &&
                                Vector4.Distance(material.GetColor("_BaseColor"), Color.black) < 0.05f))
                            neutralCenterCaps++;
                    }
                }
                else if (transform.name.StartsWith("BMWFixedCaliper", StringComparison.Ordinal))
                {
                    caliperCount++;
                    foreach (var renderer in transform.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null)
                            continue;
                        if (renderer.name.StartsWith(
                                "BMW_OriginalCaliper_",
                                StringComparison.Ordinal))
                            originalCaliperRenderers++;
                        for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                            caliperTriangles += mesh.GetTriangles(subMesh).Length / 3;
                        var caliperSize = mesh.bounds.size;
                        if (caliperSize.y >= 0.22f && caliperSize.x >= 0.07f &&
                            caliperSize.x <= 0.25f && caliperSize.z <= 0.18f &&
                            mesh.vertexCount >= 500)
                            correctlyShapedCalipers++;
                    }
                }
            }
            if (wheelCount != 4 || caliperCount != 4 || centerCapCount != 4 ||
                neutralCenterCaps != 4 || rollingParts < 20 ||
                originalCaliperRenderers != 4 || correctlyShapedCalipers != 4 ||
                caliperTriangles < 4000)
                throw new InvalidOperationException(
                    $"BMW wheel assembly incomplete wheels={wheelCount} calipers={caliperCount} " +
                    $"originalCaliperRenderers={originalCaliperRenderers}/4 " +
                    $"caliperTriangles={caliperTriangles} shaped={correctlyShapedCalipers}/4 " +
                    $"centerCaps={centerCapCount}/" +
                    $"neutral={neutralCenterCaps} rollingParts={rollingParts}.");

            if (!centers.TryGetValue("BMWWheelFrontLeft", out var frontLeft) ||
                !centers.TryGetValue("BMWWheelFrontRight", out var frontRight) ||
                !centers.TryGetValue("BMWWheelRearLeft", out var rearLeft) ||
                !centers.TryGetValue("BMWWheelRearRight", out var rearRight))
                throw new InvalidOperationException("BMW wheel centers are missing.");
            var frontTrack = Math.Abs(frontRight.x - frontLeft.x);
            var rearTrack = Math.Abs(rearRight.x - rearLeft.x);
            var wheelbase = Math.Abs((frontLeft.z + frontRight.z - rearLeft.z - rearRight.z) * 0.5f);
            if (Math.Abs(frontTrack - FrontTrack) > 0.01f ||
                Math.Abs(rearTrack - RearTrack) > 0.01f ||
                Math.Abs(wheelbase - VisualWheelbase) > 0.01f ||
                Math.Abs(frontLeft.z - VisualFrontAxleZ) > 0.01f ||
                Math.Abs(rearLeft.z - VisualRearAxleZ) > 0.01f)
                throw new InvalidOperationException(
                    $"BMW wheel geometry mismatch wheelbase={wheelbase:F3} tracks={frontTrack:F3}/{rearTrack:F3}.");

            var validatedSprings = 0;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (!transform.name.EndsWith("_WheelController", StringComparison.Ordinal))
                    continue;
                foreach (var component in transform.GetComponents<MonoBehaviour>())
                {
                    var serializedWheel = new SerializedObject(component);
                    var spring = serializedWheel.FindProperty("spring.maxLength");
                    if (spring == null)
                        continue;
                    var expectedTravel = transform.name.StartsWith("Front", StringComparison.Ordinal)
                        ? FrontSuspensionTravel
                        : RearSuspensionTravel;
                    if (Math.Abs(ReadNumber(spring) - expectedTravel) > 0.001f)
                        throw new InvalidOperationException(
                            $"BMW suspension travel mismatch at '{transform.name}': {ReadNumber(spring):F3}.");
                    var bump = ReadNumber(serializedWheel.FindProperty("damper.bumpRate"));
                    var rebound = ReadNumber(serializedWheel.FindProperty("damper.reboundRate"));
                    var extension = ReadNumber(
                        serializedWheel.FindProperty("suspensionExtensionSpeedCoeff"));
                    var damageWobble = ReadNumber(
                        serializedWheel.FindProperty("damageMaxWobbleAngle"));
                    if (Math.Abs(bump - SuspensionBumpRate) > 1f ||
                        Math.Abs(rebound - SuspensionReboundRate) > 1f ||
                        Math.Abs(extension - SuspensionExtensionSpeed) > 0.01f ||
                        Math.Abs(damageWobble - MaximumDamagedWheelWobbleAngle) > 0.01f)
                        throw new InvalidOperationException(
                            $"BMW suspension damping mismatch at '{transform.name}': " +
                            $"bump={bump:F0} rebound={rebound:F0} extension={extension:F1} " +
                            $"damageWobble={damageWobble:F1}.");
                    validatedSprings++;
                }
            }
            if (validatedSprings != 4)
                throw new InvalidOperationException(
                    $"BMW requires four validated suspension springs, found {validatedSprings}.");

            var bodyCollider = FindTransform(prefab.transform, "BodyCollider") ??
                               throw new InvalidOperationException("BMW body collider holder is missing.");
            var bodyColliders = bodyCollider.GetComponents<BoxCollider>();
            if (bodyColliders.Length != 2 ||
                Vector3.Distance(bodyColliders[0].center, new Vector3(0f, 0.40f, 0f)) > 0.01f ||
                Vector3.Distance(bodyColliders[0].size, new Vector3(1.82f, 0.44f, 4.62f)) > 0.01f ||
                Vector3.Distance(bodyColliders[1].center, new Vector3(0f, 0.84f, -0.20f)) > 0.01f ||
                Vector3.Distance(bodyColliders[1].size, new Vector3(1.50f, 0.62f, 2.55f)) > 0.01f)
                throw new InvalidOperationException("BMW body colliders are incomplete.");
            if (FindTransform(prefab.transform, "BMWDamageBody") == null ||
                FindTransform(prefab.transform, "BMW_DRL_Source") == null ||
                FindTransform(prefab.transform, "BMW_Lamp_Source") == null ||
                FindTransform(prefab.transform, "BMW_RearLamp_Source") == null ||
                FindTransform(prefab.transform, "BMW_RoofPaint_Source") == null ||
                FindTransform(prefab.transform, "Steering_wheel") == null)
                throw new InvalidOperationException("BMW damage, lighting, or driver anchors are incomplete.");

            var materialResult = BMWM4G82Materials.FixSolidMaterials(prefab);
            var roofRenderer = FindTransform(prefab.transform, "BMW_RoofPaint_Source")
                ?.GetComponent<MeshRenderer>();
            if (roofRenderer == null || !roofRenderer.enabled ||
                Array.Exists(roofRenderer.sharedMaterials, material =>
                    material == null || BMWM4G82Materials.IsTransparentMaterial(material) ||
                    !IsBodyPaintMaterial(material)))
                throw new InvalidOperationException("BMW roof is not assigned to opaque body paint.");
            if (materialResult.CabinGlassRenderers != 1 || materialResult.TransparentMaterialsFixed < 1)
                throw new InvalidOperationException(
                    $"BMW glass validation failed renderers={materialResult.CabinGlassRenderers} transparent={materialResult.TransparentMaterialsFixed}.");
            foreach (var rendererName in new[] { "BMW_Interior_Source", "BMW_EngineDetails_Source" })
            {
                var renderer = FindTransform(prefab.transform, rendererName)?.GetComponent<MeshRenderer>();
                if (renderer == null || !renderer.enabled ||
                    Array.Exists(renderer.sharedMaterials, material =>
                        material == null || BMWM4G82Materials.IsTransparentMaterial(material)))
                    throw new InvalidOperationException(
                        $"BMW opaque detail renderer '{rendererName}' is missing or transparent.");
            }
            var colouredAtlasSlots = 0;
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.name.IndexOf(
                            "Coloured", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        material.HasProperty("_BaseColorMap") &&
                        material.GetTexture("_BaseColorMap") != null)
                        colouredAtlasSlots++;
            if (colouredAtlasSlots != 6)
                throw new InvalidOperationException(
                    $"BMW factory-color atlas validation found {colouredAtlasSlots}/6 renderer slots.");
            if (Math.Abs(prefab.transform.localScale.x - 1f) > 0.001f ||
                Math.Abs(prefab.transform.localScale.y - 1f) > 0.001f ||
                Math.Abs(prefab.transform.localScale.z - 1f) > 0.001f)
                throw new InvalidOperationException($"BMW prefab root scale is not one: {prefab.transform.localScale}.");

            Debug.Log(
                $"BMWM4G82 bundle verified: price={price:F0}, fuel={maxFuel:F0}L, cargo={maxCargo:F0}, " +
                $"speed={maxSpeed:F0}km/h, power={enginePower:F0}kW, bounds={bounds.size}, " +
                $"bodyUpright={Vector3.Dot(authoredUp, prefab.transform.up):F2}, spawnConfig=true, " +
                $"wheelbase={wheelbase:F3}, tracks={frontTrack:F3}/{rearTrack:F3}, " +
                $"wheels={wheelCount}, fixedCalipers={caliperCount}, " +
                $"caliperTriangles={caliperTriangles}, centerCaps={centerCapCount}, " +
                $"rollingParts={rollingParts}, " +
                $"glass={materialResult.CabinGlassRenderers}, materials={materialResult.RendererCount}.");
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
                throw new InvalidOperationException("Could not create the BMW VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated BMW VehicleType asset did not load.");

        target.name = "BMWM4G82";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 79795f);
        SetNumber(serialized, "maxFuel", 59f);
        SetNumber(serialized, "maxCargoCapacity", 12f);
        SetNumber(serialized, "maxSpeed", 290f);
        SetNumber(serialized, "enginePower", 375f);
        SetNumber(serialized, "brakeForce", BrakeMaxTorque);
        SetNumber(serialized, "turnRadius", 25f);
        SetNumber(serialized, "damageIntensity", DamageIntensity);
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
        var lightingModel = AssetDatabase.LoadAssetAtPath<GameObject>(LightingModelPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R reference prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("BMW GLB did not import as a prefab.");
        if (lightingModel == null)
            throw new InvalidOperationException("BMW labeled lighting GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "BMWM4G82";
        try
        {
            StripAudiGeometry(root);
            RemoveAudiSpecificBehaviours(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);
            if (root.GetComponent<BMWM4G82SpawnConfigurator>() == null)
                root.AddComponent<BMWM4G82SpawnConfigurator>();

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the BMW model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "BMWVisual";
            RemoveModelLights(modelInstance);
            PreserveOriginalCaliperSources(modelInstance);
            NameKeyRenderers(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureRoofPaint(modelInstance);
            RemoveAuxiliaryPaintGeometry(modelInstance);
            CreateSteeringAnchor(root);
            ConfigureExitMarkers(root);
            AssignPersistentMaterials(modelInstance);
            AttachWheelVisuals(root, modelInstance);
            modelInstance.transform.localPosition += Vector3.up * VisualBodyOffsetY;
            AttachExactLightingSources(modelInstance, lightingModel);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            var fix = BMWM4G82Materials.FixSolidMaterials(root);
            if (RestoreColouredAtlasMaterials(root) != 1)
                throw new InvalidOperationException(
                    "BMW requires exactly one restored factory-color atlas material.");
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);

            Debug.Log(
                $"BMWM4G82: prepared decal-safe materials renderers={fix.RendererCount}, " +
                $"decalMasksCleared={fix.DecalMasksCleared}, " +
                $"opaqueFixed={fix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={fix.TransparentMaterialsFixed}, " +
                $"bodyPaintNormalized={fix.BodyPaintMaterialsNormalized}, " +
                $"cabinGlass={fix.CabinGlassRenderers}/" +
                $"reenabled={fix.CabinGlassRenderersReenabled}, " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, " +
                $"hdrpValidated={fix.MaterialsValidated}.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the BMW vehicle prefab.");
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

    private static void PreserveOriginalCaliperSources(GameObject model)
    {
        var sourceHolder = new GameObject("BMW_OriginalCaliper_Sources");
        sourceHolder.transform.SetParent(model.transform, false);
        var sourceIndex = 0;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            var material = renderer.sharedMaterial;
            if (!IsOriginalCaliperMaterial(material))
                continue;

            // The previous setup removed this mesh before normalization but
            // left its empty renderer transform in the bounds pass. Retain a
            // temporary empty marker at that exact transform so revealing the
            // authored caliper does not alter the already-tested body scale,
            // ride height or wheelbase.
            var normalizationMarker = new GameObject(
                $"BMW_CaliperNormalizationMarker_{sourceIndex}");
            normalizationMarker.transform.SetParent(renderer.transform.parent, false);
            normalizationMarker.transform.localPosition = renderer.transform.localPosition;
            normalizationMarker.transform.localRotation = renderer.transform.localRotation;
            normalizationMarker.transform.localScale = renderer.transform.localScale;
            normalizationMarker.AddComponent<MeshRenderer>().enabled = false;

            // Material.002 is the authored brake-caliper assembly. It used to
            // be mistaken for Sketchfab studio geometry and discarded before
            // wheel extraction. Keep the source mesh intact but hidden so it
            // can be split into fixed, non-rolling corner geometry later.
            renderer.name = $"BMW_OriginalCaliper_Source_{sourceIndex++}";
            renderer.enabled = false;
            renderer.transform.SetParent(sourceHolder.transform, true);
        }

        if (sourceIndex != 2)
            throw new InvalidOperationException(
                $"BMW requires two authored Material.002 caliper sources, found {sourceIndex}.");
    }

    private static void AttachExactLightingSources(GameObject model, GameObject lightingModel)
    {
        var parent = model.transform.parent;
        if (parent == null)
            throw new InvalidOperationException("BMW visual root has no vehicle parent.");
        var lightingInstance = PrefabUtility.InstantiatePrefab(
            lightingModel, parent) as GameObject;
        if (lightingInstance == null)
            throw new InvalidOperationException("Could not instantiate the BMW labeled lighting model.");

        PrefabUtility.UnpackPrefabInstance(
            lightingInstance,
            PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction);
        lightingInstance.name = "BMWExactLightingImport";

        var lampAnchor = FindTransform(model.transform, "BMW_Lamp_Source") ??
                         throw new InvalidOperationException("BMW production lamp anchor is missing.");
        var drlAnchor = FindTransform(model.transform, "BMW_DRL_Source") ??
                        throw new InvalidOperationException("BMW production DRL anchor is missing.");

        var requiredNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "BMWLightRef_FrontHeadlights",
            "BMWLightRef_MirrorTurnSignalLeft",
            "BMWLightRef_MirrorTurnSignalRight",
            "BMWLightRef_RearBrakeLights",
            "BMWLightRef_RearIndicatorLeft",
            "BMWLightRef_RearIndicatorRight",
            "BMWLightRef_RearReverseLights",
            "BMWLightRef_RearRunningLights",
            "BMWLightRef_FrontOuterDrlIndicatorLeft",
            "BMWLightRef_FrontOuterDrlIndicatorRight",
            "BMWLightRef_FrontInnerDrl",
        };
        var foundNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var importedRenderer in lightingInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!requiredNames.Contains(importedRenderer.name))
                continue;
            if (!foundNames.Add(importedRenderer.name))
                throw new InvalidOperationException(
                    $"BMW labeled lighting model contains duplicate renderer '{importedRenderer.name}'.");
            var mesh = importedRenderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
                throw new InvalidOperationException(
                    $"BMW labeled lighting renderer '{importedRenderer.name}' has no mesh.");

            // The extracted mesh vertices remain in the same Object_67 or
            // Object_71 local coordinates as the production model. Parenting
            // an identity source under that exact renderer transform avoids
            // importing and normalizing the subset GLB hierarchy a second
            // time, which previously moved the DRLs above the roof and the
            // rear sources away from the vehicle.
            var anchor = importedRenderer.name.StartsWith(
                "BMWLightRef_FrontOuterDrl", StringComparison.Ordinal) ||
                         string.Equals(importedRenderer.name, "BMWLightRef_FrontInnerDrl",
                             StringComparison.Ordinal)
                ? drlAnchor
                : lampAnchor;
            var source = new GameObject(importedRenderer.name);
            source.transform.SetParent(anchor, false);
            source.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = source.AddComponent<MeshRenderer>();

            renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterials = Array.Empty<Material>();

            if (!TryGetRendererBounds(renderer.transform, out var bounds))
                throw new InvalidOperationException(
                    $"BMW labeled lighting renderer '{renderer.name}' has no bounds.");
            ValidateExactLightingBounds(renderer.name, bounds);
            Debug.Log(
                $"BMWM4G82: validated labeled light '{renderer.name}' " +
                $"center={bounds.center} size={bounds.size}.");
        }

        UnityEngine.Object.DestroyImmediate(lightingInstance);

        if (foundNames.Count != requiredNames.Count)
        {
            requiredNames.ExceptWith(foundNames);
            throw new InvalidOperationException(
                "BMW labeled lighting model is missing: " + string.Join(", ", requiredNames));
        }

        Debug.Log(
            $"BMWM4G82: attached exact labeled lighting meshes={foundNames.Count}/" +
            $"{requiredNames.Count} from '{LightingModelPath}'.");
    }

    private static void ValidateExactLightingBounds(string name, Bounds bounds)
    {
        var isFront = name.StartsWith("BMWLightRef_Front", StringComparison.Ordinal);
        var isRear = name.StartsWith("BMWLightRef_Rear", StringComparison.Ordinal);
        var isMirror = name.StartsWith("BMWLightRef_Mirror", StringComparison.Ordinal);
        var insideVehicle = Math.Abs(bounds.center.x) < TargetWidth * 0.7f &&
                            bounds.min.y > -0.15f && bounds.max.y < TargetHeight + 0.35f &&
                            bounds.min.z > -TargetLength * 0.65f &&
                            bounds.max.z < TargetLength * 0.65f;
        var longitudinallyCorrect =
            (isFront && bounds.center.z > TargetLength * 0.28f) ||
            (isRear && bounds.center.z < -TargetLength * 0.28f) ||
            (isMirror && Math.Abs(bounds.center.z) < TargetLength * 0.32f);
        if (!insideVehicle || !longitudinallyCorrect)
            throw new InvalidOperationException(
                $"BMW labeled lighting renderer '{name}' is outside its expected vehicle region: " +
                $"center={bounds.center} min={bounds.min} max={bounds.max}.");
    }

    private static void NameKeyRenderers(GameObject model)
    {
        var glassIndex = 0;
        var badgeIndex = 0;
        MeshRenderer? bodySource = null;
        var bodySourceVertexCount = -1;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            var materialName = renderer.sharedMaterial?.name ?? string.Empty;
            if (materialName.IndexOf("PaintTNR", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var vertexCount = renderer.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount ?? 0;
                if (vertexCount > bodySourceVertexCount)
                {
                    bodySource = renderer;
                    bodySourceVertexCount = vertexCount;
                }
            }
            else if (materialName.IndexOf("LightA", StringComparison.OrdinalIgnoreCase) >= 0)
                renderer.name = "BMW_Lamp_Source";
            else if (string.Equals(materialName, "emit", StringComparison.OrdinalIgnoreCase))
                renderer.name = "BMW_DRL_Source";
            else if (string.Equals(materialName, "red_glass", StringComparison.OrdinalIgnoreCase))
                renderer.name = "BMW_RearLamp_Source";
            else if (materialName.IndexOf("glasswindshiled", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                renderer.name = string.Equals(renderer.name, "Object_83", StringComparison.Ordinal)
                    ? "BMW_CabinGlass_Object_83"
                    : $"BMW_CabinGlass_{glassIndex}";
                glassIndex++;
            }
            else if (materialName.IndexOf("InteriorA", StringComparison.OrdinalIgnoreCase) >= 0)
                renderer.name = "BMW_Interior_Source";
            else if (materialName.IndexOf("EngineA", StringComparison.OrdinalIgnoreCase) >= 0)
                renderer.name = "BMW_EngineDetails_Source";
            else if (materialName.IndexOf("BadgeA", StringComparison.OrdinalIgnoreCase) >= 0)
                renderer.name = $"BMW_Badge_Source_{badgeIndex++}";
            else if (string.Equals(materialName, "phong2", StringComparison.OrdinalIgnoreCase))
                renderer.name = "BMW_DashboardDisplay_Source";
        }

        if (bodySource != null)
            bodySource.name = "BMW_Body_Source";

        if (FindTransform(model.transform, "BMW_Body_Source") == null ||
            FindTransform(model.transform, "BMW_Lamp_Source") == null ||
            FindTransform(model.transform, "BMW_DRL_Source") == null ||
            FindTransform(model.transform, "BMW_RearLamp_Source") == null)
        {
            throw new InvalidOperationException(
                "BMW model material classification could not locate body and lamp renderers.");
        }
    }

    private static void ConfigureRoofPaint(GameObject model)
    {
        var body = FindTransform(model.transform, "BMW_Body_Source")
            ?.GetComponent<MeshRenderer>() ??
            throw new InvalidOperationException("BMW body paint source is missing.");
        var bodyMaterial = body.sharedMaterial ??
                           throw new InvalidOperationException("BMW body paint material is missing.");
        MeshRenderer? roof = null;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!Array.Exists(renderer.sharedMaterials, material =>
                    material != null && BMWM4G82Materials.IsCabinGlassMaterial(material)))
                continue;
            var bounds = renderer.bounds;
            if (bounds.center.y > 1.2f && bounds.size.y < 0.25f && bounds.size.z < 1.6f)
            {
                if (roof != null)
                    throw new InvalidOperationException("BMW roof classification found multiple candidates.");
                roof = renderer;
            }
        }
        if (roof == null)
            throw new InvalidOperationException("BMW roof paint geometry could not be isolated from the glass.");

        roof.name = "BMW_RoofPaint_Source";
        roof.sharedMaterial = bodyMaterial;
        Debug.Log($"BMWM4G82: classified roof as opaque body paint bounds={roof.bounds.size}.");
    }

    private static void RemoveAuxiliaryPaintGeometry(GameObject model)
    {
        var removed = 0;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (string.Equals(renderer.name, "BMW_Body_Source", StringComparison.Ordinal) ||
                string.Equals(renderer.name, "BMW_RoofPaint_Source", StringComparison.Ordinal))
                continue;
            if (!Array.Exists(renderer.sharedMaterials, material =>
                    material != null && material.name.IndexOf(
                        "PaintTNR", StringComparison.OrdinalIgnoreCase) >= 0))
                continue;

            renderer.enabled = false;
            renderer.sharedMaterials = Array.Empty<Material>();
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = null;
            removed++;
        }
        if (removed == 0)
            throw new InvalidOperationException("BMW auxiliary PaintTNR geometry was not found.");
        Debug.Log($"BMWM4G82: removed auxiliary PaintTNR geometry count={removed}.");
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        body.mass = 1775f;
        body.drag = VehicleLinearDrag;
        body.angularDrag = 1.90f;
        body.centerOfMass = StableCenterOfMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.maxDepenetrationVelocity = MaximumDepenetrationVelocity;
        body.solverIterations = Math.Max(body.solverIterations, 12);
        body.solverVelocityIterations = Math.Max(body.solverVelocityIterations, 4);

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
            SetNumber(serialized, "baseMass", 1775f);
            SetNumber(serialized, "combinedMass", 1775f);
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
                SetRelativeNumber(serialized, "spring.maxForce", isFront ? 19500f : 18500f);
                SetRelativeNumber(serialized, "damper.bumpRate", SuspensionBumpRate);
                SetRelativeNumber(serialized, "damper.reboundRate", SuspensionReboundRate);
                SetRelativeNumber(serialized, "suspensionExtensionSpeedCoeff", SuspensionExtensionSpeed);
                SetRelativeNumber(serialized, "damageMaxWobbleAngle", MaximumDamagedWheelWobbleAngle);
                SetRelativeNumber(serialized, "wheel.radius", isFront ? FrontTireRadius : RearTireRadius);
                SetRelativeNumber(serialized, "wheel.width", isFront ? FrontTireWidth : RearTireWidth);
                SetRelativeNumber(serialized, "forwardFriction.grip", ForwardTireGrip);
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

        colliders[0].center = new Vector3(0f, 0.40f, 0f);
        colliders[0].size = new Vector3(1.82f, 0.44f, 4.62f);
        colliders[1].center = new Vector3(0f, 0.84f, -0.20f);
        colliders[1].size = new Vector3(1.50f, 0.62f, 2.55f);
    }

    private static void CreateSteeringAnchor(GameObject root)
    {
        var steeringAnchor = new GameObject("Steering_wheel");
        steeringAnchor.transform.SetParent(root.transform, false);
        steeringAnchor.transform.localPosition = SteeringAnchorPosition;
        Debug.Log(
            $"BMWM4G82: fitted left-hand-drive steering anchor at " +
            $"{SteeringAnchorPosition} from the G82 cabin proportions.");
    }

    private static void ConfigureExitMarkers(GameObject root)
    {
        var steeringWheel = FindTransform(root.transform, "Steering_wheel") ??
                            throw new InvalidOperationException("Fitted steering-wheel anchor is missing.");
        var steeringPosition = steeringWheel.localPosition;
        SetLocalPosition(root, "Driverside", DriverExitPosition);
        SetLocalPosition(root, "Passengerside", PassengerExitPosition);
        Debug.Log(
            $"BMWM4G82: steering wheel x={steeringPosition.x:F3}; " +
            $"driver exit={DriverExitPosition}.");
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
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1150f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 450f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 500f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.14f);
                // NWH applies maxPower at the driven wheels. Retain the
                // 375 kW VehicleType specification, but account for drivetrain
                // loss here so the simulated car matches BMW's measured times.
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 350f);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateM4PowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 800f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 7200f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.55f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", true);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0.28f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.154f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.11f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 4800f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 7000f);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);
                SetRelativeNumber(serialized, "brakes.maxTorque", BrakeMaxTorque);
                SetRelativeNumber(serialized, "brakes.actuationTime", BrakeActuationTime);
                SetRelativeNumber(
                    serialized,
                    "steering.degreesPerSecondLimit",
                    SteeringDegreesPerSecond);
                SetRelativeNumber(
                    serialized,
                    "steering.maximumSteerAngle",
                    MaximumSteerAngle);
                var steeringSmoothing = FindRelativeProperty(
                    serialized,
                    "steering.speedSensitiveSmoothingCurve");
                if (steeringSmoothing?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException(
                        "Reference speed-sensitive steering smoothing curve is missing.");
                steeringSmoothing.animationCurveValue = new AnimationCurve(
                    new Keyframe(0f, 0.10f),
                    new Keyframe(0.30f, 0.17f),
                    new Keyframe(1f, 0.22f));

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
                gears.arraySize = M4Gears.Length;
                for (var index = 0; index < M4Gears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = M4Gears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 290f);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!found)
            throw new InvalidOperationException("NWH vehicle controller was not found on the reference prefab.");
    }

    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        // The supplied Sketchfab GLB uses Y for length and Z for height. A negative
        // quarter-turn maps authored +Z to chassis +Y; the positive turn mirrors
        // the body vertically while leaving the separately generated wheels upright.
        model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        model.transform.localScale = Vector3.one;

        if (!TryGetModelBodyBounds(model.transform, out var bounds))
            throw new InvalidOperationException("BMW model contains no renderers.");

        var headlights = FindTransform(model.transform, "BMW_DRL_Source");
        var tailLights = FindTransform(model.transform, "BMW_RearLamp_Source");
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
            throw new InvalidOperationException("BMW model length could not be measured.");

        var scale = new Vector3(
            TargetWidth / bounds.size.x,
            TargetLength / bounds.size.z,
            TargetHeight / bounds.size.y);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled BMW bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final BMW bounds could not be measured.");

        Debug.Log(
            $"BMWM4G82: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var sources = new List<MeshRenderer>();
        var originalCaliperSources = new List<MeshRenderer>();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (IsOriginalCaliperMaterial(renderer.sharedMaterial))
            {
                originalCaliperSources.Add(renderer);
                Debug.Log(
                    $"BMWM4G82 original caliper source renderer='{renderer.name}' " +
                    $"material='{renderer.sharedMaterial.name}'.");
            }
            else if (IsWheelSourceMaterial(renderer.sharedMaterial))
            {
                sources.Add(renderer);
                Debug.Log($"BMWM4G82 wheel source renderer='{renderer.name}' material='{renderer.sharedMaterial.name}'.");
            }
        }
        if (sources.Count < 5)
            throw new InvalidOperationException($"Expected the BMW wheel source renderers, found {sources.Count}.");
        if (originalCaliperSources.Count != 2)
            throw new InvalidOperationException(
                $"Expected two BMW authored caliper source renderers, found {originalCaliperSources.Count}.");

        if (!TryGetWheelRegionBounds(root.transform, sources, true, true, out var frontLeftBounds) ||
            !TryGetWheelRegionBounds(root.transform, sources, false, true, out var rearLeftBounds))
        {
            throw new InvalidOperationException("BMW tire regions could not be measured.");
        }

        var corners = new[]
        {
            new WheelCorner("FrontLeft", "FrontLeft_WheelController", true, true,
                new Vector3(-FrontTrack * 0.5f, FrontTireRadius, frontLeftBounds.center.z),
                FrontTireWidth, FrontTireRadius),
            new WheelCorner("FrontRight", "FrontRight_WheelController", true, false,
                new Vector3(FrontTrack * 0.5f, FrontTireRadius, frontLeftBounds.center.z),
                FrontTireWidth, FrontTireRadius),
            new WheelCorner("RearLeft", "RearLeft_WheelController", false, true,
                new Vector3(-RearTrack * 0.5f, RearTireRadius, rearLeftBounds.center.z),
                RearTireWidth, RearTireRadius),
            new WheelCorner("RearRight", "RearRight_WheelController", false, false,
                new Vector3(RearTrack * 0.5f, RearTireRadius, rearLeftBounds.center.z),
                RearTireWidth, RearTireRadius),
        };

        foreach (var corner in corners)
            CreateWheelCorner(root, sources, originalCaliperSources, corner);

        foreach (var renderer in sources)
        {
            renderer.enabled = false;
            renderer.sharedMaterials = Array.Empty<Material>();
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = null;
        }
        foreach (var renderer in originalCaliperSources)
        {
            renderer.enabled = false;
            renderer.sharedMaterials = Array.Empty<Material>();
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = null;
        }
    }

    private readonly struct WheelCorner
    {
        public WheelCorner(string suffix, string controllerName, bool front, bool left,
            Vector3 position, float width, float radius)
        {
            Suffix = suffix;
            ControllerName = controllerName;
            Front = front;
            Left = left;
            Position = position;
            Width = width;
            Radius = radius;
        }

        public string Suffix { get; }
        public string ControllerName { get; }
        public bool Front { get; }
        public bool Left { get; }
        public Vector3 Position { get; }
        public float Width { get; }
        public float Radius { get; }
    }

    private enum WheelVisualPart
    {
        Rolling,
        CenterCap,
    }

    private static void CreateWheelCorner(
        GameObject root,
        List<MeshRenderer> sources,
        List<MeshRenderer> originalCaliperSources,
        WheelCorner corner)
    {
        var controller = FindTransform(root.transform, corner.ControllerName) ??
                         throw new InvalidOperationException($"Wheel controller '{corner.ControllerName}' is missing.");
        controller.localPosition = corner.Position;

        var mount = new GameObject("BMWWheel" + corner.Suffix);
        mount.transform.SetParent(root.transform, false);
        mount.transform.localPosition = corner.Position;
        var geometry = new GameObject("Geometry_" + corner.Suffix);
        geometry.transform.SetParent(root.transform, false);

        MeshRenderer? tireRenderer = null;
        var rollingParts = 0;
        foreach (var source in sources)
        {
            if (!IsRollingWheelMaterial(source.sharedMaterial))
                continue;
            var created = CreateFilteredWheelPart(
                root, geometry, source, corner, WheelVisualPart.Rolling);
            if (created == null)
                continue;
            rollingParts++;
            if (IsTireMaterial(source.sharedMaterial))
                tireRenderer = created;
        }
        if (tireRenderer == null || rollingParts < 4)
            throw new InvalidOperationException(
                $"BMW {corner.Suffix} rolling assembly is incomplete ({rollingParts} parts).");

        foreach (var source in sources)
        {
            if (!IsCaliperMaterial(source.sharedMaterial))
                continue;
            if (CreateFilteredWheelPart(
                    root, geometry, source, corner, WheelVisualPart.CenterCap) == null)
                continue;
            rollingParts++;
            break;
        }
        if (rollingParts < 5)
            throw new InvalidOperationException(
                $"BMW {corner.Suffix} center cap is missing ({rollingParts} rolling parts).");

        geometry.transform.SetParent(mount.transform, true);
        if (!TryGetRendererBounds(tireRenderer.transform, out var tireBounds))
            throw new InvalidOperationException($"BMW {corner.Suffix} tire bounds are missing.");
        mount.transform.localScale = new Vector3(
            corner.Width / tireBounds.size.x,
            (corner.Radius * 2f) / tireBounds.size.y,
            (corner.Radius * 2f) / tireBounds.size.z);
        if (!TryGetRendererBounds(tireRenderer.transform, out tireBounds))
            throw new InvalidOperationException($"BMW {corner.Suffix} fitted tire bounds are missing.");
        var alignmentDelta = mount.transform.position - tireBounds.center;
        geometry.transform.position += alignmentDelta;

        var fixedMount = new GameObject("BMWFixedCaliper" + corner.Suffix);
        fixedMount.transform.SetParent(root.transform, false);
        fixedMount.transform.localPosition = corner.Position;
        var caliperGeometry = new GameObject("BMWCaliperGeometry_" + corner.Suffix);
        caliperGeometry.transform.SetParent(root.transform, false);
        CreateOriginalCaliperGeometry(
            root.transform,
            caliperGeometry,
            originalCaliperSources,
            corner);
        // The authored caliper mesh sits behind the fitted wheel face. Move
        // each original assembly outboard and 2 cm rearward so the authored
        // caliper body clears the fitted brake disc on every corner.
        caliperGeometry.transform.localPosition =
            Vector3.right * (corner.Left ? -CaliperOutboardOffset : CaliperOutboardOffset) +
            Vector3.forward * CaliperRearwardOffset;
        caliperGeometry.transform.SetParent(fixedMount.transform, true);

        AssignWheelVisual(controller, mount);
        Debug.Log(
            $"BMWM4G82: fitted {corner.Suffix} center={corner.Position} " +
            $"tire={corner.Width:F3}x{corner.Radius * 2f:F3}m rollingParts={rollingParts}.");
    }

    private static MeshRenderer? CreateFilteredWheelPart(GameObject root, GameObject holder,
        MeshRenderer source, WheelCorner corner, WheelVisualPart partKind)
    {
        var filter = source.GetComponent<MeshFilter>();
        if (filter?.sharedMesh == null)
            return null;
        var wheelFaceThreshold = Mathf.Abs(corner.Position.x) - 0.005f;
        var mesh = CreateFilteredRootSpaceMesh(root.transform, source, vertex =>
        {
            if ((corner.Left ? vertex.x >= 0f : vertex.x < 0f) ||
                (corner.Front ? vertex.z < 0f : vertex.z >= 0f))
                return false;
            if (partKind == WheelVisualPart.CenterCap)
                return Mathf.Abs(vertex.x) >= wheelFaceThreshold;
            return true;
        });
        if (mesh == null)
            return null;

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");
        var materialMarker = SanitizeAssetName(source.sharedMaterial?.name ?? source.name);
        var path = $"{MeshFolder}/BMW_{corner.Suffix}_{materialMarker}.asset";
        var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (persistent == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            persistent = mesh;
        }
        else
        {
            EditorUtility.CopySerialized(mesh, persistent);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(persistent);
        }

        var part = new GameObject("BMW_WheelPart_" + corner.Suffix + "_" + materialMarker);
        part.transform.SetParent(holder.transform, false);
        part.AddComponent<MeshFilter>().sharedMesh = persistent;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;
        renderer.lightProbeUsage = source.lightProbeUsage;
        renderer.reflectionProbeUsage = source.reflectionProbeUsage;
        renderer.renderingLayerMask = source.renderingLayerMask;
        return renderer;
    }

    private static void CreateOriginalCaliperGeometry(
        Transform root,
        GameObject holder,
        IReadOnlyList<MeshRenderer> sources,
        WheelCorner corner)
    {
        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        Mesh? bestMesh = null;
        Material? bestMaterial = null;
        var bestTriangleCount = 0;
        foreach (var source in sources)
        {
            var candidate = CreateCompactOriginalCaliperMesh(root, source, corner, out var triangles);
            if (candidate == null)
                continue;
            if (triangles > bestTriangleCount)
            {
                if (bestMesh != null)
                    UnityEngine.Object.DestroyImmediate(bestMesh);
                bestMesh = candidate;
                bestMaterial = source.sharedMaterial;
                bestTriangleCount = triangles;
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        if (bestMesh == null || bestMaterial == null || bestTriangleCount < 1000)
            throw new InvalidOperationException(
                $"BMW {corner.Suffix} authored caliper extraction failed triangles={bestTriangleCount}.");

        var meshPath = $"{MeshFolder}/BMW_{corner.Suffix}_OriginalCaliper.asset";
        var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (persistent == null)
        {
            AssetDatabase.CreateAsset(bestMesh, meshPath);
            persistent = bestMesh;
        }
        else
        {
            EditorUtility.CopySerialized(bestMesh, persistent);
            UnityEngine.Object.DestroyImmediate(bestMesh);
            EditorUtility.SetDirty(persistent);
        }

        var part = new GameObject("BMW_OriginalCaliper_" + corner.Suffix);
        part.transform.SetParent(holder.transform, false);
        part.AddComponent<MeshFilter>().sharedMesh = persistent;
        var renderer = part.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = bestMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        Debug.Log(
            $"BMWM4G82: extracted authored {corner.Suffix} caliper " +
            $"triangles={bestTriangleCount} bounds={persistent.bounds.size} " +
            "mounted at the authored brake-disc position.");
    }

    private static Mesh? CreateCompactOriginalCaliperMesh(
        Transform root,
        MeshRenderer source,
        WheelCorner corner,
        out int triangleCount)
    {
        triangleCount = 0;
        var sourceMesh = source.GetComponent<MeshFilter>()?.sharedMesh;
        if (sourceMesh == null)
            return null;

        var sourceVertices = sourceMesh.vertices;
        var sourceToRoot = root.worldToLocalMatrix * source.transform.localToWorldMatrix;
        var rootVertices = new Vector3[sourceVertices.Length];
        for (var index = 0; index < rootVertices.Length; index++)
            rootVertices[index] = sourceToRoot.MultiplyPoint3x4(sourceVertices[index]);

        var selectedTriangles = new List<int>();
        var selectedVertices = new HashSet<int>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var triangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var a = triangles[index];
                var b = triangles[index + 1];
                var c = triangles[index + 2];
                var center = (rootVertices[a] + rootVertices[b] + rootVertices[c]) / 3f;
                var delta = center - corner.Position;
                if (Mathf.Abs(delta.x) > 0.30f || Mathf.Abs(delta.y) > 0.48f ||
                    Mathf.Abs(delta.z) > 0.48f)
                    continue;
                selectedTriangles.Add(a);
                selectedTriangles.Add(b);
                selectedTriangles.Add(c);
                selectedVertices.Add(a);
                selectedVertices.Add(b);
                selectedVertices.Add(c);
            }
        }
        triangleCount = selectedTriangles.Count / 3;
        if (triangleCount == 0)
            return null;

        var orderedSourceIndices = new List<int>(selectedVertices);
        orderedSourceIndices.Sort();
        var remap = new Dictionary<int, int>(orderedSourceIndices.Count);
        var vertices = new Vector3[orderedSourceIndices.Count];
        for (var index = 0; index < orderedSourceIndices.Count; index++)
        {
            var sourceIndex = orderedSourceIndices[index];
            remap[sourceIndex] = index;
            vertices[index] = rootVertices[sourceIndex];
        }
        for (var index = 0; index < selectedTriangles.Count; index++)
            selectedTriangles[index] = remap[selectedTriangles[index]];

        var mesh = new Mesh
        {
            name = "BMW_" + corner.Suffix + "_OriginalCaliper",
            indexFormat = vertices.Length > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16,
            vertices = vertices,
        };
        var sourceNormals = sourceMesh.normals;
        if (sourceNormals.Length == sourceVertices.Length)
        {
            var normalMatrix = sourceToRoot.inverse.transpose;
            var normals = new Vector3[orderedSourceIndices.Count];
            for (var index = 0; index < orderedSourceIndices.Count; index++)
                normals[index] = normalMatrix.MultiplyVector(
                    sourceNormals[orderedSourceIndices[index]]).normalized;
            mesh.normals = normals;
        }
        var sourceTangents = sourceMesh.tangents;
        if (sourceTangents.Length == sourceVertices.Length)
        {
            var tangents = new Vector4[orderedSourceIndices.Count];
            for (var index = 0; index < orderedSourceIndices.Count; index++)
            {
                var sourceTangent = sourceTangents[orderedSourceIndices[index]];
                var direction = sourceToRoot.MultiplyVector(new Vector3(
                    sourceTangent.x,
                    sourceTangent.y,
                    sourceTangent.z)).normalized;
                tangents[index] = new Vector4(
                    direction.x,
                    direction.y,
                    direction.z,
                    sourceTangent.w);
            }
            mesh.tangents = tangents;
        }
        var sourceColors = sourceMesh.colors32;
        if (sourceColors.Length == sourceVertices.Length)
        {
            var colors = new Color32[orderedSourceIndices.Count];
            for (var index = 0; index < orderedSourceIndices.Count; index++)
                colors[index] = sourceColors[orderedSourceIndices[index]];
            mesh.colors32 = colors;
        }
        CopyUvChannel(sourceMesh.uv, sourceVertices.Length, orderedSourceIndices, values => mesh.uv = values);
        CopyUvChannel(sourceMesh.uv2, sourceVertices.Length, orderedSourceIndices, values => mesh.uv2 = values);
        mesh.SetTriangles(selectedTriangles, 0, true);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void CopyUvChannel(
        Vector2[] source,
        int sourceVertexCount,
        IReadOnlyList<int> sourceIndices,
        Action<Vector2[]> assign)
    {
        if (source.Length != sourceVertexCount)
            return;
        var values = new Vector2[sourceIndices.Count];
        for (var index = 0; index < sourceIndices.Count; index++)
            values[index] = source[sourceIndices[index]];
        assign(values);
    }

    private static Mesh? CreateFilteredRootSpaceMesh(Transform root, MeshRenderer source,
        Func<Vector3, bool> includeVertex)
    {
        var sourceMesh = source.GetComponent<MeshFilter>()?.sharedMesh;
        if (sourceMesh == null)
            return null;
        var sourceVertices = sourceMesh.vertices;
        var vertices = new Vector3[sourceVertices.Length];
        var sourceToRoot = root.worldToLocalMatrix * source.transform.localToWorldMatrix;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = sourceToRoot.MultiplyPoint3x4(sourceVertices[index]);

        var selectedBySubMesh = new List<int>[sourceMesh.subMeshCount];
        var selectedVertices = new HashSet<int>();
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var selected = new List<int>();
            var triangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var a = triangles[index];
                var b = triangles[index + 1];
                var c = triangles[index + 2];
                if (!includeVertex((vertices[a] + vertices[b] + vertices[c]) / 3f))
                    continue;
                selected.Add(a);
                selected.Add(b);
                selected.Add(c);
                selectedVertices.Add(a);
                selectedVertices.Add(b);
                selectedVertices.Add(c);
            }
            selectedBySubMesh[subMesh] = selected;
        }
        if (selectedVertices.Count == 0)
            return null;

        var mesh = UnityEngine.Object.Instantiate(sourceMesh);
        mesh.name = sourceMesh.name + "_BMWCorner";
        mesh.vertices = vertices;
        var normals = mesh.normals;
        if (normals.Length == vertices.Length)
        {
            var normalMatrix = sourceToRoot.inverse.transpose;
            for (var index = 0; index < normals.Length; index++)
                normals[index] = normalMatrix.MultiplyVector(normals[index]).normalized;
            mesh.normals = normals;
        }
        var tangents = mesh.tangents;
        if (tangents.Length == vertices.Length)
        {
            for (var index = 0; index < tangents.Length; index++)
            {
                var tangent = tangents[index];
                var direction = sourceToRoot.MultiplyVector(
                    new Vector3(tangent.x, tangent.y, tangent.z)).normalized;
                tangents[index] = new Vector4(direction.x, direction.y, direction.z, tangent.w);
            }
            mesh.tangents = tangents;
        }
        mesh.subMeshCount = sourceMesh.subMeshCount;
        for (var subMesh = 0; subMesh < selectedBySubMesh.Length; subMesh++)
            mesh.SetTriangles(selectedBySubMesh[subMesh], subMesh, false);
        using (var enumerator = selectedVertices.GetEnumerator())
        {
            enumerator.MoveNext();
            var bounds = new Bounds(vertices[enumerator.Current], Vector3.zero);
            while (enumerator.MoveNext())
                bounds.Encapsulate(vertices[enumerator.Current]);
            mesh.bounds = bounds;
        }
        mesh.UploadMeshData(false);
        return mesh;
    }

    private static bool TryGetWheelRegionBounds(Transform root, List<MeshRenderer> sources,
        bool front, bool left, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var source in sources)
        {
            if (!IsTireMaterial(source.sharedMaterial))
                continue;
            var mesh = source.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null)
                continue;
            foreach (var vertex in mesh.vertices)
            {
                var point = root.InverseTransformPoint(source.transform.TransformPoint(vertex));
                if ((left ? point.x >= 0f : point.x < 0f) ||
                    (front ? point.z < 0f : point.z >= 0f))
                    continue;
                if (!found)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(point);
                }
            }
        }
        return found;
    }

    private static bool IsWheelSourceMaterial(Material? material)
    {
        var name = material?.name ?? string.Empty;
        return name.IndexOf("sidetyre", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("disk.001", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("disk_001", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("metalblack", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.EndsWith("_main", StringComparison.OrdinalIgnoreCase) ||
               name.IndexOf("Material.001", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Material_001", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsOriginalCaliperMaterial(Material? material)
    {
        var name = material?.name ?? string.Empty;
        return name.IndexOf("Material.002", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("Material_002", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsRollingWheelMaterial(Material? material) =>
        IsWheelSourceMaterial(material) && !IsCaliperMaterial(material!);

    private static bool IsTireMaterial(Material? material) =>
        material?.name.IndexOf("sidetyre", StringComparison.OrdinalIgnoreCase) >= 0;

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
            if (string.Equals(renderer.name, "BMW_Body_Source", StringComparison.Ordinal))
            {
                sourceRenderer = renderer;
                break;
            }
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The BMW outer body mesh was not found.");

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        bakedMesh.name = "BMWDamageBody";
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

        var damageBody = new GameObject("BMWDamageBody")
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
        var damageHandlerConfigured = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;

            if (string.Equals(
                    component.GetType().Name,
                    "DamageHandler",
                    StringComparison.Ordinal))
            {
                var damageSerialized = new SerializedObject(component);
                SetNumber(damageSerialized, "damageIntensity", DamageIntensity);
                SetNumber(
                    damageSerialized,
                    "decelerationThreshold",
                    DamageDecelerationThreshold);
                damageSerialized.ApplyModifiedPropertiesWithoutUndo();
                damageHandlerConfigured = true;
                continue;
            }

            if (!string.Equals(
                    component.GetType().Name,
                    "VehicleDeformationController",
                    StringComparison.Ordinal))
                continue;

            var serialized = new SerializedObject(component);
            var meshFilters = serialized.FindProperty("meshFilters");
            if (meshFilters == null || !meshFilters.isArray)
                throw new InvalidOperationException("Vehicle deformation mesh list is missing.");
            // Runtime uses the model-aware damage controller. Keep the legacy
            // arrays empty so CarController.Repair() can call Reset() safely
            // without indexing an intentionally absent original-mesh entry.
            meshFilters.arraySize = 0;
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
        if (!damageHandlerConfigured)
            throw new InvalidOperationException("Vehicle damage handler is missing.");
    }

    private static bool IsBodyPaintMaterial(Material material) =>
        material.name.IndexOf("PaintTNR", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorAccentPaintMaterial(Material material) =>
        material.name.IndexOf("InteriorA", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRimMaterial(Material material) =>
        material.name.EndsWith("_main", StringComparison.OrdinalIgnoreCase);

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!BMWM4G82Materials.IsBMWRenderer(renderer.transform))
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
            throw new InvalidOperationException("The shared BMW rim material is missing.");

        ApplyRimFinish(leftMaterial, BMWM4G82Materials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!BMWM4G82Materials.IsBMWRenderer(renderer.transform))
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
                $"Expected four BMW rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", BMWM4G82Materials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", BMWM4G82Materials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("Material.001", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("Material_001", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool M4PowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateM4PowerCurve();
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
        material.name.IndexOf("Carbon1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorPrimaryPaintMaterial(Material material) =>
        material.name.IndexOf("InteriorA", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorSecondaryPaintMaterial(Material material) =>
        material.name.IndexOf("phong2", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorDarkPaintMaterial(Material material) =>
        material.name.IndexOf("InteriorA", StringComparison.OrdinalIgnoreCase) >= 0;

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
                    var transparent = BMWM4G82Materials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var sourceName = SanitizeAssetName(source.name);
                    var assetName = $"BMW{kind}_{materialIndex:D2}_{sourceName}";
                    var existingPath = FindExistingMaterialPath(kind, sourceName);
                    var path = existingPath ?? $"{MaterialFolder}/{assetName}.mat";
                    persistent = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (persistent == null)
                    {
                        persistent = new Material(source) { name = assetName };
                        AssetDatabase.CreateAsset(persistent, path);
                    }
                    else
                    {
                        // This atlas carries the factory multi-color trim and
                        // roundel-adjacent detail. Some importer refreshes
                        // expose it without its texture bindings, so retain the
                        // already curated persistent material instead of
                        // replacing it with an incomplete transient material.
                        var preserveCuratedAtlas = sourceName.IndexOf(
                            "Coloured", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!preserveCuratedAtlas)
                        {
                            persistent.CopyPropertiesFromMaterial(source);
                            persistent.shader = source.shader;
                        }
                        else
                        {
                            RestoreColouredAtlasMaterial(persistent);
                        }
                        persistent.name = existingPath == null
                            ? assetName
                            : System.IO.Path.GetFileNameWithoutExtension(path);
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

    private static void RestoreColouredAtlasMaterial(Material material)
    {
        Texture2D? atlas = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (!(asset is Texture2D texture) ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    texture, out _, out long localId) ||
                localId != ColouredAtlasTextureLocalId)
                continue;
            atlas = texture;
            break;
        }
        if (atlas == null)
            throw new InvalidOperationException("BMW factory-color texture atlas is missing.");

        foreach (var property in new[] { "baseColorTexture", "_BaseColorMap", "_MainTex" })
            if (material.HasProperty(property)) material.SetTexture(property, atlas);
        foreach (var property in new[] { "baseColorFactor", "_BaseColor", "_Color" })
            if (material.HasProperty(property)) material.SetColor(property, Color.white);
        if (material.HasProperty("metallicFactor")) material.SetFloat("metallicFactor", 0.5f);
        if (material.HasProperty("roughnessFactor")) material.SetFloat("roughnessFactor", 0f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.5f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f);
        EditorUtility.SetDirty(material);
    }

    private static int RestoreColouredAtlasMaterials(GameObject root)
    {
        var materials = new HashSet<Material>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.name.IndexOf(
                        "Coloured", StringComparison.OrdinalIgnoreCase) >= 0)
                    materials.Add(material);
        foreach (var material in materials)
            RestoreColouredAtlasMaterial(material);
        return materials.Count;
    }

    private static string? FindExistingMaterialPath(string kind, string sourceName)
    {
        var prefix = $"BMW{kind}_";
        var suffix = "_" + sourceName + ".mat";
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var fileName = System.IO.Path.GetFileName(path);
            if (fileName.StartsWith(prefix, StringComparison.Ordinal) &&
                fileName.EndsWith(suffix, StringComparison.Ordinal))
                return path;
        }
        return null;
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

        manifest.ModId = "BMWM4G82";
        manifest.DisplayName = "BMW M4 G82";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "bmw_m4_g82.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("BMW manifest references could not be assigned.");
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
            if (renderer is MeshRenderer meshRenderer &&
                IsOriginalCaliperMaterial(meshRenderer.sharedMaterial))
                continue;
            var current = renderer.transform;
            var belongsToSourceWheel = false;
            while (current != null && current != root)
            {
                if (current.name.StartsWith("Circle.001", StringComparison.Ordinal) ||
                    current.name.StartsWith("Circle.004", StringComparison.Ordinal) ||
                    current.name.StartsWith(
                        "BMW_OriginalCaliper_Source",
                        StringComparison.Ordinal))
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

    private static bool TryGetBMWRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!BMWM4G82Materials.IsBMWRenderer(renderer.transform))
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

    private static int CountTransformsNamed(Transform root, string name)
    {
        var count = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal)) count++;
        return count;
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
