#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModTemplate.Editor;
using BAModAPI.Services;
using BigAmbitions.SaveSystem;
using Helpers;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class MercedesC63CabrioSetup
{
    private const string ModRoot = "Assets/Mods/Mercedes_C63_Cabrio";
    private const string ReferenceAssetPath = "Assets/Mods/Example-Vehicle/TurboHonza.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/2018_mercedes_amg_c63_cabriolet.glb";
    private const string AudioMixerPath = ModRoot + "/Audio/VehicleAudioMixer.mixer";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/C63DamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/MercedesC63Cabrio.asset";
    private const string VehiclePrefabPath = ModRoot + "/Mercedes.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/MercedesC63Cabrio.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/mercedes_c63_cabrio.unity3d";
    private const string VehicleTypeName =
        "mercedes-c63-cabrio:vehicletype_mercedes_c63_cabrio";
    private const float TargetLength = 4.7498f;
    private const float TargetWidth = 1.8771f;
    private const float TargetHeight = 1.4021f;
    private const float FrontTrack = 1.63576f;
    private const float RearTrack = 1.59258f;
    private const float Wheelbase = 2.83972f;
    private const float FrontTireRadius = 0.3306f;
    private const float RearTireRadius = 0.32835f;
    private const float FrontTireWidth = 0.255f;
    private const float RearTireWidth = 0.285f;
    private const float WheelInset = 0.09f; // prior 40 mm + requested 50 mm per corner
    private const float BodyGroundClearance = 0.14f;
    private const float TargetBodyHeight = TargetHeight - BodyGroundClearance;
    private const float WheelCenterRideHeightOffset = 0.035f + MercedesC63CabrioTuning.GroundCorrection;
    private const float VehicleLinearDrag = 0.045f;
    private const float VehicleBrakeForce = MercedesC63CabrioTuning.BrakeTorque;
    private const float BrakeMaxTorque = MercedesC63CabrioTuning.BrakeTorque;
    private const float FrontForwardGrip = MercedesC63CabrioTuning.FrontGrip;
    private const float RearForwardGrip = MercedesC63CabrioTuning.RearGrip;
    private const float FrontForwardStiffness = MercedesC63CabrioTuning.TireStiffness;
    private const float RearForwardStiffness = MercedesC63CabrioTuning.TireStiffness;
    private const float TireFrictionCircleStrength = 1.02f;
    private const float AntiRollBarForce = 8200f;
    private const float FrontSuspensionTravel = 0.090f;
    private const float RearSuspensionTravel = 0.095f;
    private const float DeformationStrength = 0.17f;
    private const float DeformationRadius = 0.24f;
    private const float DeformationRandomness = 0.005f;
    private static readonly Vector3 StableCenterOfMass = MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.10f, 0.14f));

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f + WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, Wheelbase * 0.5f + MercedesC63CabrioTuning.FrontWheelForwardOffset) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f - WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, Wheelbase * 0.5f + MercedesC63CabrioTuning.FrontWheelForwardOffset) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f + WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f + MercedesC63CabrioTuning.RearWheelForwardOffset) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f - WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f + MercedesC63CabrioTuning.RearWheelForwardOffset) },
        };
    private static readonly Vector3 FrontContactColliderCenter =
        MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.61f, 1.58f));
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.78f, 0.50f, 1.08f);
    private static readonly Vector3 RearContactColliderCenter =
        MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.60f, -1.78f));
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.78f, 0.50f, 1.02f);

    private static readonly float[] C63Gears =
    {
        -3.42f,
        0f,
        4.38f,
        2.86f,
        1.92f,
        1.37f,
        1.00f,
        0.82f,
        0.73f,
    };

    private static AnimationCurve CreateC63PowerCurve()
    {
        // NWH consumes normalized POWER, not torque. P[kW] = T[Nm] * rpm / 9549.3.
        // The manufacturer's 650 Nm plateau spans 1750-4500 rpm; peak power is
        // 350 kW at 5500-6250 rpm. Linear tangents avoid torque overshoot.
        var keys = new[] {
            new Keyframe(0f, 0f), new Keyframe(900f / 7000f, 0.10f),
            new Keyframe(1750f / 7000f, 650f * 1750f / 9549.3f / 350f),
            new Keyframe(4500f / 7000f, 650f * 4500f / 9549.3f / 350f),
            new Keyframe(5500f / 7000f, 1f), new Keyframe(6250f / 7000f, 1f),
            new Keyframe(1f, 0.93f) };
        for (var i = 0; i < keys.Length; i++)
        {
            if (i > 0) keys[i].inTangent = (keys[i].value - keys[i-1].value) / (keys[i].time - keys[i-1].time);
            if (i + 1 < keys.Length) keys[i].outTangent = (keys[i+1].value - keys[i].value) / (keys[i+1].time - keys[i].time);
        }
        return new AnimationCurve(keys);
    }

    [MenuItem("Big Ambitions Mods/Setup Mercedes-AMG C63 Cabriolet")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        MercedesC63CabrioSourceMaterials.Prepare();
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        PruneUnusedGeneratedAssets();
        CreateManifest();
        MercedesC63CabrioReview.Validate(AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabPath));
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AuditNoForeignReferences(VehicleAssetPath);
        AuditNoForeignReferences(VehiclePrefabPath);
        Debug.Log("MercedesC63Cabrio setup complete: normalized the supplied C63 Cabriolet GLB, " +
                  "fitted 255/40R18 and 285/35R18 wheels, configured the seven-speed AMG MCT " +
                  "RWD powertrain, and audited all prefab and VehicleType asset references.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        BuildAndVerify();
    }

    public static void BuildAndVerify()
    {
        ModAssetBundleCli.BuildForMod();
        File.Copy(WindowsBundlePath, ModRoot + "/AssetBundles/mercedes_c63_cabrio.unity3d", true);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        VerifyBuiltBundle();
    }

    public static void RepairPrefabAndBuild()
    {
        GenerateAndBuild();
    }

    public static void VerifyBuiltBundle()
    {
        AuditNoForeignReferences(VehicleAssetPath);
        AuditNoForeignReferences(VehiclePrefabPath);
        const string verificationModId = "MercedesC63CabrioEditorVerification";
        const string bundleKey = "AssetBundles/mercedes_c63_cabrio.unity3d";
        const string lazyBundleKey = "AssetBundles/unloaded-verification-bundle";
        // Register a valid, unloaded manifest bundle first. The native global
        // fallback used to invalidate its registry enumerator when loading it.
        AssetService.RegisterBundle(verificationModId, lazyBundleKey,
            Path.GetFullPath(ModRoot + "/AssetBundles/Windows/Windows"));
        AssetService.RegisterBundle(verificationModId, bundleKey,
            Path.GetFullPath(ModRoot + "/" + bundleKey));
        var bundle = AssetService.GetBundle(verificationModId, bundleKey);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");

        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException("Bundle is missing the Mercedes VehicleType or prefab.");

            MercedesC63CabrioReview.VerifyNativePrefabRegistration(prefab,
                (Vehicles.VehicleTypes.VehicleType)vehicleType);
            if (AssetService.IsBundleLoaded(verificationModId, lazyBundleKey))
                throw new InvalidOperationException("Mercedes registration loaded an unrelated pending bundle.");
            var nativeType = (Vehicles.VehicleTypes.VehicleType)vehicleType;
            var nativePrefabName = "Vehicles/PlayerVehicles/" + nativeType.vehicleTypeName.GetIdWithoutType();
            if (!string.Equals(nativeType.vehicleTypeName.GetIdWithoutType(),
                    Path.GetFileNameWithoutExtension(VehiclePrefabPath), StringComparison.OrdinalIgnoreCase) ||
                PrefabHelper.LoadPrefabAssetByName(nativePrefabName) != prefab)
                throw new InvalidOperationException($"Native Mercedes spawn prefab cannot be resolved: {nativePrefabName}.");
            var nativeSpawn = PrefabHelper.CreatePrefab(nativePrefabName);
            try
            {
                if (nativeSpawn.GetComponent<CarController>() == null ||
                    nativeSpawn.transform.Find("CarHolder/Colliders/BodyCollider")?.GetComponent<BoxCollider>() == null)
                    throw new InvalidOperationException("Native Mercedes spawn is missing its car controller or body collider.");
                Debug.Log($"MercedesC63Cabrio native prefab lookup and creation verified: type={nativeType.vehicleTypeName}, path={nativePrefabName}.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(nativeSpawn);
            }

            ValidateRenderableMaterials(prefab, false);
            MercedesC63CabrioReview.Validate(prefab);
            if (!File.ReadAllText(LocalesPath + "/en.json").Contains(VehicleTypeName))
                throw new InvalidOperationException("Mercedes vehicle name locale is missing.");
            var vehicle = new SerializedObject(vehicleType);
            var price = ReadNumber(vehicle.FindProperty("price"));
            var fuel = ReadNumber(vehicle.FindProperty("maxFuel"));
            var speed = ReadNumber(vehicle.FindProperty("maxSpeed"));
            var power = ReadNumber(vehicle.FindProperty("enginePower"));
            var vehicleName = vehicle.FindProperty("vehicleTypeName")?.stringValue;
            if (Math.Abs(price - 73500f) > 0.5f || Math.Abs(fuel - 65.9f) > 0.1f ||
                Math.Abs(speed - 250f) > 0.5f || Math.Abs(power - 350f) > 0.5f ||
                !string.Equals(vehicleName, VehicleTypeName, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Mercedes data mismatch: type={vehicleName}, price={price}, fuel={fuel}, " +
                    $"speed={speed}, power={power}.");

            var visual = FindTransform(prefab.transform, "C63Visual") ??
                         throw new InvalidOperationException("C63 visual root is missing.");
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                    throw new InvalidOperationException($"Bundle prefab has a missing script at {transform.name}.");
            if (!TryGetRendererBounds(visual, out var visualBounds) ||
                Math.Abs(visualBounds.size.x - TargetWidth) > 0.06f ||
                Math.Abs(visualBounds.max.y - TargetHeight) > 0.06f ||
                Math.Abs(visualBounds.size.z - TargetLength) > 0.08f)
                throw new InvalidOperationException($"C63 visual chassis dimensions are incorrect: {visualBounds.size}.");
            var body = FindTransform(prefab.transform, "C63DamageBody") ??
                       throw new InvalidOperationException("Deformable C63 body mesh is missing.");
            var bodyFilter = body.GetComponent<MeshFilter>();
            var bodyRenderer = body.GetComponent<MeshRenderer>();
            if (bodyFilter == null || bodyFilter.sharedMesh == null ||
                bodyFilter.sharedMesh.vertexCount < 1000 || bodyRenderer == null || !bodyRenderer.enabled)
                throw new InvalidOperationException("C63 damage mesh is absent or not renderable.");
            var bodySize = bodyRenderer.bounds.size;
            if (bodySize.x < TargetWidth - 0.30f || bodySize.x > TargetWidth + 0.08f ||
                bodySize.y < 1.10f || bodySize.y > TargetHeight + 0.04f ||
                Math.Abs(bodySize.z - TargetLength) > 0.08f)
                throw new InvalidOperationException($"C63 deformable paint mesh bounds are implausible: {bodySize}.");

            var wheelCount = 0;
            var caliperCount = 0;
            foreach (var pair in WheelControllerPositions)
            {
                var corner = pair.Key.Replace("_WheelController", string.Empty);
                var wheel = FindTransform(prefab.transform, "C63Wheel" + corner) ??
                            throw new InvalidOperationException($"{corner} rolling assembly is missing.");
                var caliper = FindTransform(prefab.transform, "C63FixedCaliper" + corner) ??
                              throw new InvalidOperationException($"{corner} fixed caliper is missing.");
                var controller = FindTransform(prefab.transform, pair.Key) ??
                                 throw new InvalidOperationException($"{corner} NWH wheel controller is missing.");
                var restingCenter = pair.Value - Vector3.up * MercedesC63CabrioTuning.InitialSpringLength;
                if (Vector3.Distance(controller.localPosition, pair.Value) > 0.005f ||
                    Vector3.Distance(wheel.localPosition, restingCenter) > 0.005f ||
                    Vector3.Distance(caliper.localPosition, restingCenter) > 0.005f)
                    throw new InvalidOperationException($"{corner} wheel, controller, or caliper is misaligned.");
                if (!TryGetRendererBounds(wheel, out var wheelBounds))
                    throw new InvalidOperationException($"{corner} wheel has no renderer bounds.");
                if (Vector3.Distance(wheelBounds.center, prefab.transform.TransformPoint(restingCenter)) > 0.005f)
                    throw new InvalidOperationException($"{corner} wheel mesh is off its physics pivot: {wheelBounds.center}.");
                var front = pair.Key.StartsWith("Front", StringComparison.Ordinal);
                var expectedWidth = front ? FrontTireWidth : RearTireWidth;
                var expectedDiameter = 2f * (front ? FrontTireRadius : RearTireRadius);
                if (Math.Abs(wheelBounds.size.x - expectedWidth) > 0.025f ||
                    Math.Abs(wheelBounds.size.y - expectedDiameter) > 0.025f ||
                    Math.Abs(wheelBounds.size.z - expectedDiameter) > 0.025f)
                    throw new InvalidOperationException(
                        $"{corner} tire dimensions mismatch: {wheelBounds.size}; " +
                        $"expected {expectedWidth:0.000}x{expectedDiameter:0.000}m.");
                wheelCount++;
                caliperCount++;
            }
            if (wheelCount != 4 || caliperCount != 4)
                throw new InvalidOperationException("Expected four fitted wheels and four fixed calipers.");

            if (FindTransformWithNameFragment(visual, "M_LBucket_Max") == null ||
                FindTransformWithNameFragment(visual, "taillights") == null)
                throw new InvalidOperationException("C63 front or rear lamp geometry is missing.");
            var roadLights = prefab.GetComponentsInChildren<Light>(true);
            if (roadLights.Length != 1 || roadLights[0].name != "Spotlights" || roadLights[0].enabled)
                throw new InvalidOperationException("Expected the disabled road-light template on the C63 prefab.");

            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                if (System.Text.RegularExpressions.Regex.IsMatch(transform.name, "audi(?!o)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                    transform.name.IndexOf("rs6r", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    transform.name.IndexOf("porsche", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    transform.name.IndexOf("gt3rs", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException(
                        $"Donor-named transform remains in the Mercedes prefab: '{transform.name}'.");

            Debug.Log($"MercedesC63Cabrio bundle verified: price=${price:0}, fuel={fuel:0.0}L, " +
                      $"speed={speed:0}km/h, power={power:0}kW, mass=1909kg, " +
                      $"bounds={bodySize}, wheels={wheelCount}, calipers={caliperCount}, " +
                      "RWD/7G AMG MCT, donor asset-reference audit=clean.");
        }
        finally
        {
            MercedesC63CabrioReview.ReleaseNativePrefabRegistration();
            AssetService.UnloadBundle(verificationModId.ToLowerInvariant() + "/" + lazyBundleKey, true);
            AssetService.UnloadBundle(verificationModId.ToLowerInvariant() + "/" + bundleKey, true);
        }
    }

    private static void AuditNoForeignReferences(string assetPath)
    {
        if (!assetPath.StartsWith(ModRoot + "/", StringComparison.Ordinal))
            throw new InvalidOperationException($"Reference audit received a non-target asset: {assetPath}.");
        foreach (var dependency in AssetDatabase.GetDependencies(assetPath, true))
        {
            if (dependency.StartsWith("Assets/Mods/", StringComparison.OrdinalIgnoreCase) &&
                !dependency.StartsWith(ModRoot + "/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Foreign mod asset reference remains in '{assetPath}': '{dependency}'.");
        }

        var main = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (main == null) throw new InvalidOperationException($"Could not load audit asset '{assetPath}'.");
        var inspected = new List<UnityEngine.Object>();
        if (main is GameObject gameObject)
        {
            foreach (var transform in gameObject.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                    throw new InvalidOperationException(
                        $"Missing MonoBehaviour remains at {assetPath}:{transform.name}.");
            foreach (var component in gameObject.GetComponentsInChildren<Component>(true))
                if (component != null) inspected.Add(component);
        }
        else
        {
            inspected.Add(main);
        }

        foreach (var current in inspected)
        {
            var serialized = new SerializedObject(current);
            var property = serialized.GetIterator();
            var enterChildren = true;
            while (property.Next(enterChildren))
            {
                enterChildren = true;
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    property.objectReferenceValue == null) continue;
                var referencedPath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                if (referencedPath.StartsWith("Assets/Mods/", StringComparison.OrdinalIgnoreCase) &&
                    !referencedPath.StartsWith(ModRoot + "/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Foreign serialized reference remains in '{assetPath}.{property.propertyPath}': " +
                        $"'{referencedPath}'.");
            }
        }
    }
    private static UnityEngine.Object CreateVehicleType()
    {
        var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ReferenceAssetPath);
        if (source == null)
            throw new InvalidOperationException("Example VehicleType template was not found.");

        var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (target == null)
        {
            if (!AssetDatabase.CopyAsset(ReferenceAssetPath, VehicleAssetPath))
                throw new InvalidOperationException("Could not create the Mercedes VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }
        if (target == null)
            throw new InvalidOperationException("Generated Mercedes VehicleType asset did not load.");

        target.name = "MercedesC63Cabrio";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 73500f);
        SetNumber(serialized, "maxFuel", 65.9f);
        SetNumber(serialized, "maxCargoCapacity", 2f);
        SetNumber(serialized, "maxSpeed", 250f);
        SetNumber(serialized, "enginePower", 350f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", 27f);
        SetNumber(serialized, "damageIntensity", 0.50f);
        SetBool(serialized, "isATruck", false);
        SetBool(serialized, "isHandVehicle", false);
        SetBool(serialized, "fitsHandTruck", false);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", true);
        SetBool(serialized, "hasRadio", true);
        SetBool(serialized, "isLuxuryCar", true);
        SetBool(serialized, "enclosed", false);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        ClearForeignAssetReferences(target);
        return target;
    }

    private static void ConfigureVehicleTypePerformance()
    {
        var vehicleType = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (vehicleType == null)
            throw new InvalidOperationException("Mercedes VehicleType asset was not found.");
        var serialized = new SerializedObject(vehicleType);
        SetNumber(serialized, "maxSpeed", 250f);
        SetNumber(serialized, "enginePower", 350f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(vehicleType);
        ClearForeignAssetReferences(vehicleType);
    }

    private static void CreateVehiclePrefab(UnityEngine.Object vehicleType)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R structural donor prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("Mercedes C63 GLB did not import as a prefab.");

        var root = PrefabUtility.InstantiatePrefab(source) as GameObject ??
                   throw new InvalidOperationException("Could not instantiate the reference vehicle rig.");
        root.name = "MercedesC63Cabrio";
        try
        {
            PrefabUtility.UnpackPrefabInstance(
                root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            RemoveAudiSpecificBehaviours(root);
            StripAudiGeometry(root);
            RenameDonorTransforms(root.transform);
            ClearForeignAssetReferences(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigureAudioMixerReferences(root);
            ConfigureNativeActivities(root);
            ConfigurePowertrain(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject ??
                                UnityEngine.Object.Instantiate(model, root.transform);
            if (PrefabUtility.IsPartOfPrefabInstance(modelInstance))
                PrefabUtility.UnpackPrefabInstance(
                    modelInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            modelInstance.name = "C63Visual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            modelInstance.transform.localPosition = MercedesC63CabrioTuning.BodyPoint(modelInstance.transform.localPosition);
            modelInstance.transform.localRotation = MercedesC63CabrioTuning.BodyPitch * modelInstance.transform.localRotation;
            ConfigureExitMarkers(root, modelInstance);
            AssignPersistentMaterials(modelInstance);
            SeparateBadgeFaces(root, modelInstance);
            AttachWheelVisuals(root, modelInstance);
            ConfigureSteeringWheel(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            // Persistent assets only: runtime material controllers clone materials per instance.
            // Those transient clones cannot be serialized into a prefab.
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);
            ClearForeignAssetReferences(root);
            RemoveMissingMonoBehaviours(root);

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Mercedes vehicle prefab.");
            ValidateRenderableMaterials(result, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(VehiclePrefabPath, ImportAssetOptions.ForceSynchronousImport);
            AuditNoForeignReferences(VehiclePrefabPath);
            Debug.Log(
                $"MercedesC63Cabrio prefab prepared: mass=1909kg, body={TargetLength:F3}x" +
                $"{TargetWidth:F3}m, wheelbase={Wheelbase:F3}m, rim slots={rimMaterialsConfigured}, " +
                "persistent materials validated, foreign references=0.");
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
        foreach (var collider in root.GetComponentsInChildren<MeshCollider>(true))
        {
            collider.sharedMesh = null;
            UnityEngine.Object.DestroyImmediate(collider);
        }
        foreach (var light in root.GetComponentsInChildren<Light>(true))
        {
            if (string.Equals(light.gameObject.name, "Spotlights", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(light.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(light);
        }
        var spotlightTemplate = new GameObject("Spotlights");
        spotlightTemplate.transform.SetParent(root.transform, false);
        spotlightTemplate.transform.localPosition = MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.63f, 1.85f));
        var roadLight = spotlightTemplate.AddComponent<Light>();
        roadLight.type = LightType.Spot;
        roadLight.range = 50f;
        roadLight.spotAngle = 62f;
        roadLight.innerSpotAngle = 38f;
        roadLight.colorTemperature = 5600f;
        roadLight.useColorTemperature = true;
        roadLight.shadows = LightShadows.None;
        var hdLight = spotlightTemplate.AddComponent<HDAdditionalLightData>();
        hdLight.SetIntensity(1200f, LightUnit.Lumen);
        roadLight.enabled = false;
    }

    private static void RemoveAudiSpecificBehaviours(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var script = MonoScript.FromMonoBehaviour(component);
            var path = script == null ? string.Empty : AssetDatabase.GetAssetPath(script);
            if (path.StartsWith("Assets/Mods/", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith(ModRoot + "/", StringComparison.OrdinalIgnoreCase))
                UnityEngine.Object.DestroyImmediate(component);
        }
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
    }

    private static void RemoveMissingMonoBehaviours(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            var gameObject = transform.gameObject;
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) > 0)
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
            var remaining = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            if (remaining > 0)
                throw new InvalidOperationException(
                    $"Unresolved missing MonoBehaviour(s) remain on {transform.name}: {remaining}.");
        }
    }

    private static void RemoveMissingMonoBehavioursFromSavedPrefab(string assetPath)
    {
        var root = PrefabUtility.LoadPrefabContents(assetPath);
        try
        {
            var changed = false;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var gameObject = transform.gameObject;
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) == 0)
                    continue;
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
                var remaining = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
                if (remaining > 0)
                    throw new InvalidOperationException(
                        $"Saved Mercedes prefab still has {remaining} missing MonoBehaviour(s) on {transform.name}.");
                changed = true;
            }
            if (changed && PrefabUtility.SaveAsPrefabAsset(root, assetPath) == null)
                throw new InvalidOperationException("Could not persist missing-script cleanup on Mercedes prefab.");
            if (changed)
                Debug.Log("MercedesC63Cabrio removed an unresolved prefab component after save.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ClearForeignAssetReferences(UnityEngine.Object target)
    {
        var objects = new List<UnityEngine.Object>();
        if (target is GameObject gameObject)
        {
            foreach (var component in gameObject.GetComponentsInChildren<Component>(true))
                if (component != null) objects.Add(component);
        }
        else
        {
            objects.Add(target);
        }

        foreach (var current in objects)
        {
            var serialized = new SerializedObject(current);
            var property = serialized.GetIterator();
            var enterChildren = true;
            var changed = false;
            while (property.Next(enterChildren))
            {
                enterChildren = true;
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    property.objectReferenceValue == null) continue;
                var path = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                if (!path.StartsWith("Assets/Mods/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith(ModRoot + "/", StringComparison.OrdinalIgnoreCase)) continue;
                property.objectReferenceValue = null;
                changed = true;
            }
            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(current);
            }
        }
    }
    private static void RenameDonorTransforms(Transform root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            var name = transform.name;
            var renamed = System.Text.RegularExpressions.Regex.Replace(
                name,
                "audi(?!o)|rs6[-_]?r",
                "C63",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!string.Equals(name, renamed, StringComparison.Ordinal))
                transform.name = renamed;
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
        body.mass = 1909f;
        body.drag = VehicleLinearDrag;
        body.angularDrag = 1.45f;
        body.centerOfMass = StableCenterOfMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var damage = root.GetComponent<NWH.VehiclePhysics2.Damage.DamageHandler>() ??
                     throw new InvalidOperationException("Mercedes native damage handler missing.");
        typeof(MercedesC63CabrioRuntime).GetMethod("ConfigureNativeDamage",
            BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] {damage});

        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var serialized = new SerializedObject(component);
            var configuredCenter = serialized.FindProperty("centerOfMass");
            var useDefaultCenter = serialized.FindProperty("useDefaultCenterOfMass");
            if (configuredCenter?.propertyType != SerializedPropertyType.Vector3 ||
                useDefaultCenter?.propertyType != SerializedPropertyType.Boolean) continue;
            useDefaultCenter.boolValue = false;
            configuredCenter.vector3Value = StableCenterOfMass;
            var combinedCenter = serialized.FindProperty("combinedCenterOfMass");
            if (combinedCenter?.propertyType == SerializedPropertyType.Vector3)
                combinedCenter.vector3Value = StableCenterOfMass;
            SetNumber(serialized, "baseMass", 1909f);
            SetNumber(serialized, "combinedMass", 1909f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
    private static void ConfigureWheelControllers(GameObject root)
    {
        // Arcade's curve peaks at almost 100% slip. A road-tire curve lets the
        // traction controller hold a sensible slip/RPM instead of redline launches.
        const string path = ModRoot + "/Config/C63RoadTireFriction.asset";
        var tire = AssetDatabase.LoadAssetAtPath<NWH.Common.Vehicles.FrictionPreset>(path);
        if (tire == null)
        {
            tire = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<NWH.Common.Vehicles.FrictionPreset>(
                "Assets/_BaDependencies/Vehicle Dependencies/AsphaltTireFrictionPreset.asset"));
            tire.name = "C63RoadTireFriction"; AssetDatabase.CreateAsset(tire, path);
        }
        tire.BCDE = new Vector4(9f, 2.15f, 1.30f, .871f);
        tire.UpdateFrictionCurve(); EditorUtility.SetDirty(tire);
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
                SetRelativeNumber(serialized, "spring.length", MercedesC63CabrioTuning.InitialSpringLength);
                SetRelativeNumber(serialized, "spring.prevLength", MercedesC63CabrioTuning.InitialSpringLength);
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
                var preset = serialized.FindProperty("activeFrictionPreset");
                if (preset != null) preset.objectReferenceValue = tire;
                SetRelativeNumber(serialized, "camber", 0f);
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

        colliders[0].center = MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.40f, 0f));
        colliders[0].size = new Vector3(1.82f, 0.52f, 4.40f);
        colliders[1].center = MercedesC63CabrioTuning.BodyPoint(new Vector3(0f, 0.78f, -0.08f));
        colliders[1].size = new Vector3(1.62f, 0.72f, 2.70f);
        var frontContactCollider = colliders.Length > 2 ? colliders[2] : holder.gameObject.AddComponent<BoxCollider>();
        frontContactCollider.center = FrontContactColliderCenter;
        frontContactCollider.size = FrontContactColliderSize;
        frontContactCollider.isTrigger = false;
        frontContactCollider.enabled = true;
        var rearContactCollider = colliders.Length > 3 ? colliders[3] : holder.gameObject.AddComponent<BoxCollider>();
        rearContactCollider.center = RearContactColliderCenter;
        rearContactCollider.size = RearContactColliderSize;
        rearContactCollider.isTrigger = false;
        rearContactCollider.enabled = true;
    }
    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        // The measured Mercedes steering wheel is on negative X.
        SetLocalPosition(root, "Driverside", new Vector3(-2.10f, 0.20f, 0.15f));
        SetLocalPosition(root, "Passengerside", new Vector3(2.10f, 0.20f, 0.15f));
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

    private static void ConfigureNativeActivities(GameObject root)
    {
        EnsureAssetFolder(MaterialFolder);
        const string configPath = ModRoot + "/Config/C63SleepEnvironment.asset";
        const string balancePath = ModRoot + "/Config/C63SleepBalance.asset";
        var balance = AssetDatabase.LoadAssetAtPath<PlayerActivity.PlayerActivityBalanceConfig>(balancePath);
        if (balance == null)
        {
            balance = ScriptableObject.CreateInstance<PlayerActivity.PlayerActivityBalanceConfig>();
            AssetDatabase.CreateAsset(balance, balancePath);
        }
        var values = new SerializedObject(balance);
        SetString(values, "displayName", VehicleTypeName);
        SetNumber(values, "defaultDurationMinutes", 480); SetNumber(values, "minDurationMinutes", 60);
        SetNumber(values, "maxDurationMinutes", 1440);
        values.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(balance);
        var config = AssetDatabase.LoadAssetAtPath<PlayerActivity.SleepEnvironmentConfig>(configPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<PlayerActivity.SleepEnvironmentConfig>();
            AssetDatabase.CreateAsset(config, configPath);
        }
        values = new SerializedObject(config);
        values.FindProperty("sleepEnvironmentType").intValue = 1;
        values.FindProperty("energyRegen").intValue = 3;
        values.FindProperty("balanceConfig").objectReferenceValue = balance;
        values.FindProperty("luxuryOverrideBalanceConfig").objectReferenceValue = balance;
        values.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(config);

        var washAudio = new GameObject("C63WashAudio"); washAudio.transform.SetParent(root.transform, false);
        var audio = washAudio.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.spatialBlend = 1f;
        var wash = new GameObject("C63WashEffect"); wash.transform.SetParent(root.transform, false);
        wash.transform.localPosition = new Vector3(0, 1.6f, 0); wash.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var particles = wash.AddComponent<ParticleSystem>();
        var main = particles.main; main.playOnAwake = true; main.loop = true; main.startLifetime = 0.8f;
        main.startSpeed = 1.3f; main.startSize = 0.018f; main.startColor = new Color(0.65f, 0.82f, 0.95f, 0.5f);
        var emission = particles.emission; emission.rateOverTime = 160;
        var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(1.7f, 4.2f, 0.1f);
        var materialPath = MaterialFolder + "/C63WashWater.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null) { material = new Material(Shader.Find("HDRP/Unlit")); AssetDatabase.CreateAsset(material, materialPath); }
        material.SetColor("_UnlitColor", new Color(0.65f, 0.82f, 0.95f, 0.5f));
        particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); wash.SetActive(false);
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var serialized = new SerializedObject(component);
            var sleep = serialized.FindProperty("sleepEnvironment.config");
            if (sleep != null) sleep.objectReferenceValue = config;
            if (serialized.FindProperty("washVfx") != null)
            {
                serialized.FindProperty("washVfx").objectReferenceValue = wash;
                serialized.FindProperty("washAudioSource").objectReferenceValue = audio;
                serialized.FindProperty("washSoundLoop").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(ModRoot + "/Config/Audio/WashLoop.wav");
                serialized.FindProperty("washSoundEnd").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(ModRoot + "/Config/Audio/WashEnd.wav");
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void ConfigureAudioMixerReferences(GameObject root)
    {
        var groups = new Dictionary<string, AudioMixerGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(AudioMixerPath))
            if (asset is AudioMixerGroup group) groups[group.name] = group;
        if (!groups.TryGetValue("Engine", out var engine) ||
            !groups.TryGetValue("Other", out var other) ||
            !groups.TryGetValue("Turbo", out var turbo) ||
            !groups.TryGetValue("Crash", out var crash))
            throw new InvalidOperationException("Mercedes vehicle audio mixer is missing an expected group.");

        foreach (var source in root.GetComponentsInChildren<AudioSource>(true))
        {
            var name = source.name;
            source.outputAudioMixerGroup =
                name.IndexOf("engine", StringComparison.OrdinalIgnoreCase) >= 0 ? engine :
                name.IndexOf("turbo", StringComparison.OrdinalIgnoreCase) >= 0 ? turbo :
                name.IndexOf("crash", StringComparison.OrdinalIgnoreCase) >= 0 ? crash : other;
        }
    }

    private static void ConfigurePowertrain(GameObject root)
    {
        // Native modules are discovered once by NWH's ModuleManager on creation.
        var tcs = root.GetComponent<NWH.VehiclePhysics2.Modules.TCS.TCSModuleWrapper>() ??
            root.AddComponent<NWH.VehiclePhysics2.Modules.TCS.TCSModuleWrapper>();
        tcs.module.slipThreshold = MercedesC63CabrioTuning.TractionSlipThreshold;
        tcs.module.lowerSpeedThreshold = 0f;
        var abs = root.GetComponent<NWH.VehiclePhysics2.Modules.ABS.ABSModuleWrapper>() ??
            root.AddComponent<NWH.VehiclePhysics2.Modules.ABS.ABSModuleWrapper>();
        abs.module.slipThreshold = MercedesC63CabrioTuning.AbsSlipThreshold;
        abs.module.lowerSpeedThreshold = 0f;
        var found = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var serialized = new SerializedObject(component);
            if (string.Equals(component.GetType().FullName,
                    "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
            {
                found = true;
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", MercedesC63CabrioTuning.ClutchEngagementRpm);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", MercedesC63CabrioTuning.ClutchThrottleOffsetRpm);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", MercedesC63CabrioTuning.ClutchEngagementRange);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", MercedesC63CabrioTuning.EngineInertia);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 350f);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateC63PowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 900f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 7000f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.38f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", true);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0.35f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.06f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 7f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", MercedesC63CabrioTuning.ShiftDuration);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", MercedesC63CabrioTuning.DownshiftRpm);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", MercedesC63CabrioTuning.UpshiftRpm);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.postShiftBan", MercedesC63CabrioTuning.PostShiftBan);
                SetRelativeNumber(serialized, "powertrain.transmission.variableShiftIntensity", MercedesC63CabrioTuning.VariableShiftIntensity);
                SetRelativeBool(serialized, "powertrain.transmission.variableShiftPoint", true);
                SetRelativeNumber(serialized, "brakes.maxTorque", BrakeMaxTorque);

                var wheelGroups = FindRelativeProperty(serialized, "powertrain.wheelGroups");
                if (wheelGroups == null || !wheelGroups.isArray || wheelGroups.arraySize != 2)
                    throw new InvalidOperationException("Reference front/rear wheel groups are missing.");
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
                    if (name?.stringValue == "Rear Differential")
                    {
                        differential.FindPropertyRelative("_differentialType").intValue = 2;
                        differential.FindPropertyRelative("stiffness").floatValue = 0.5f;
                        differential.FindPropertyRelative("powerRamp").floatValue = 0.4f;
                        differential.FindPropertyRelative("coastRamp").floatValue = 0.2f;
                    }
                    if (name?.propertyType != SerializedPropertyType.String ||
                        !string.Equals(name.stringValue, "Center Differential", StringComparison.Ordinal))
                        continue;
                    var rearBias = differential.FindPropertyRelative("biasAB");
                    if (rearBias?.propertyType != SerializedPropertyType.Float)
                        throw new InvalidOperationException("Center differential rear torque bias is missing.");
                    rearBias.floatValue = 1f;
                    rearDriveConfigured = true;
                }
                if (!rearDriveConfigured)
                    throw new InvalidOperationException("Center differential could not be configured for RWD.");

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("AMG MCT transmission gear array is missing.");
                gears.arraySize = C63Gears.Length;
                for (var index = 0; index < C63Gears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = C63Gears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 250f);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        if (!found)
            throw new InvalidOperationException("NWH vehicle controller was not found on the structural rig.");
    }
    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        // Preserve the GLB importer root rotation: it carries the authored axis conversion.
        if (!TryGetModelBodyBounds(model.transform, out var bounds) || bounds.size.z <= 0.001f)
            throw new InvalidOperationException("Mercedes C63 GLB has no measurable body geometry.");

        // The provided GLB is upright and faces +Z. Preserve its authored geometry
        // and apply a uniform scale from the manufacturer's published overall length.
        var scale = TargetLength / bounds.size.z;
        model.transform.localScale *= scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled Mercedes C63 body bounds could not be measured.");

        // Preserve the length-derived uniform scale, then correct the supplied
        // mesh width deviation. Height is measured from the tire contact plane,
        // not from the underside of the body: keep 14 cm of ground clearance.
        // The GLB root rotates the imported basis, so map world-axis factors
        // back onto the prefab root local scale axes.
        var worldFactors = new Vector3(TargetWidth / bounds.size.x, TargetBodyHeight / bounds.size.y, 1f);
        var localFactors = new Vector3(
            AxisAlignedScaleFactor(model.transform.right, worldFactors),
            AxisAlignedScaleFactor(model.transform.up, worldFactors),
            AxisAlignedScaleFactor(model.transform.forward, worldFactors));
        model.transform.localScale = Vector3.Scale(model.transform.localScale, localFactors);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Dimension-corrected Mercedes C63 body bounds could not be measured.");
        model.transform.position += new Vector3(-bounds.center.x, BodyGroundClearance - bounds.min.y, -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Mercedes C63 bounds could not be measured.");
        if (Math.Abs(bounds.size.x - TargetWidth) > 0.06f ||
            Math.Abs(bounds.max.y - TargetHeight) > 0.06f ||
            Math.Abs(bounds.size.z - TargetLength) > 0.08f)
            throw new InvalidOperationException(
                $"Supplied C63 body dimensions do not match the published chassis values: {bounds.size}.");

        var frontWheel = FindTransform(model.transform, "3DWheel Front L") ??
                         throw new InvalidOperationException("C63 front-left wheel node is missing.");
        var rearWheel = FindTransform(model.transform, "3DWheel Rear L") ??
                        throw new InvalidOperationException("C63 rear-left wheel node is missing.");
        if (frontWheel.position.z <= rearWheel.position.z)
            throw new InvalidOperationException("C63 GLB is not oriented with its front toward +Z.");
        Debug.Log($"MercedesC63Cabrio model normalized scale={scale:0.000}, bounds={bounds.size}.");
    }
    private static float AxisAlignedScaleFactor(Vector3 localAxisInWorld, Vector3 worldFactors)
    {
        localAxisInWorld = new Vector3(
            Mathf.Abs(localAxisInWorld.x),
            Mathf.Abs(localAxisInWorld.y),
            Mathf.Abs(localAxisInWorld.z));
        return Vector3.Dot(localAxisInWorld.normalized, worldFactors);
    }

    private static void SeparateBadgeFaces(GameObject root, GameObject model)
    {
        var badge = model.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name.Contains("M_Badge_Max"));
        var mesh = UnityEngine.Object.Instantiate(badge.sharedMesh); mesh.name = "C63BadgeFaces";
        var vertices = mesh.vertices; var normals = mesh.normals;
        var toVehicle = root.transform.worldToLocalMatrix * badge.transform.localToWorldMatrix;
        var normalMatrix = toVehicle.inverse.transpose; var inverse = toVehicle.inverse;
        for (var i = 0; i < vertices.Length; i++)
            vertices[i] += inverse.MultiplyVector(normalMatrix.MultiplyVector(normals[i]).normalized * .0006f);
        mesh.vertices = vertices; mesh.RecalculateBounds(); badge.sharedMesh = SaveMesh(mesh);
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var sourceNames = new[]
        {
            ("FrontLeft", "3DWheel Front L", "Calliper Front L_02"),
            ("FrontRight", "3DWheel Front R", "Calliper Front R_03"),
            ("RearLeft", "3DWheel Rear L", "Calliper Rear L_04"),
            ("RearRight", "3DWheel Rear R", "Calliper Rear R_05"),
        };
        var sourceNodes = new Dictionary<string, Transform>();
        foreach (var entry in sourceNames)
        {
            sourceNodes[entry.Item2] = FindTransform(model.transform, entry.Item2) ?? throw new InvalidOperationException("Mercedes wheel source missing.");
            sourceNodes[entry.Item3] = FindTransform(model.transform, entry.Item3) ?? throw new InvalidOperationException("Mercedes caliper source missing.");
        }
        var sourceBoundsByName = new Dictionary<string, Bounds>();
        foreach (var entry in sourceNames)
        { TryGetRendererBounds(sourceNodes[entry.Item2], out var b); sourceBoundsByName[entry.Item2] = b; }
        foreach (var entry in sourceNames)
        {
            var corner = entry.Item1;
            var controllerName = corner + "_WheelController";
            var controller = FindTransform(root.transform, controllerName) ??
                             throw new InvalidOperationException($"Wheel controller '{controllerName}' is missing.");
            var targetCenter = WheelControllerPositions[controllerName];
            var isFront = corner.StartsWith("Front", StringComparison.Ordinal);
            var sourceEntry = entry;
            if (Mathf.Sign(sourceBoundsByName[entry.Item2].center.x) != Mathf.Sign(targetCenter.x))
                sourceEntry = Array.Find(sourceNames, e =>
                    e.Item1.StartsWith(isFront ? "Front" : "Rear", StringComparison.Ordinal) && e.Item1 != corner);
            var wheelNode = sourceNodes[sourceEntry.Item2];
            var caliperNode = sourceNodes[sourceEntry.Item3];
            var sourceBounds = sourceBoundsByName[sourceEntry.Item2];
            if (sourceBounds.size.x <= .001f || sourceBounds.size.y <= .001f || sourceBounds.size.z <= .001f)
                throw new InvalidOperationException($"Source C63 wheel '{sourceEntry.Item2}' has invalid bounds.");
            var radius = isFront ? FrontTireRadius : RearTireRadius;
            var width = isFront ? FrontTireWidth : RearTireWidth;
            var diameter = radius * 2f;

            controller.localPosition = targetCenter;
            controller.localRotation = Quaternion.identity;
            controller.localScale = Vector3.one;
            var mount = new GameObject("C63Wheel" + corner);
            mount.transform.SetParent(root.transform, false);
            var restingCenter = targetCenter - Vector3.up * MercedesC63CabrioTuning.InitialSpringLength;
            mount.transform.localPosition = restingCenter;
            mount.transform.localRotation = Quaternion.identity;
            mount.transform.localScale = Vector3.one;
            // Work in the wheel's own axle frame, BEFORE the authored 1-1.5 degree
            // camber and body normalization. Baking camber into a +X rolling mesh
            // makes its rim plane precess. Static suspension owns any future camber.
            var filters = wheelNode.GetComponentsInChildren<MeshFilter>(true);
            var axleBounds = new Bounds(); var rimBounds = new Bounds();
            var foundAxle = false; var foundRim = false;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                var local = wheelNode.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    var point = local.MultiplyPoint3x4(vertex);
                    if (!foundAxle) { axleBounds = new Bounds(point, Vector3.zero); foundAxle = true; }
                    else axleBounds.Encapsulate(point);
                    if (filter.name.Contains("M_Rim_Secondary"))
                    {
                        if (!foundRim) { rimBounds = new Bounds(point, Vector3.zero); foundRim = true; }
                        else rimBounds.Encapsulate(point);
                    }
                }
            }
            if (!foundAxle || !foundRim) throw new InvalidOperationException("C63 tire/rim axle measurement missing.");
            var radialRimOffset = new Vector3(0f,
                axleBounds.center.y - rimBounds.center.y, axleBounds.center.z - rimBounds.center.z);
            // glTF's handedness conversion may flip the local X direction. Infer
            // the outside from the measured rim lip, never from the source L/R label.
            var outward = rimBounds.center.x - axleBounds.center.x;
            if (Mathf.Abs(outward) < .02f) throw new InvalidOperationException("C63 rim outside is ambiguous.");
            var facing = Quaternion.Euler(0f, targetCenter.x * outward < 0f ? 180f : 0f, 0f);
            var fit = Matrix4x4.Rotate(facing) * Matrix4x4.Scale(new Vector3(
                width / axleBounds.size.x, diameter / axleBounds.size.y, diameter / axleBounds.size.z)) *
                Matrix4x4.Translate(-axleBounds.center);
            var meshIndex = 0;
            foreach (var filter in filters)
            {
                if (filter.sharedMesh == null) continue;
                var rimCorrection = filter.name.Contains("M_Rim_")
                    ? Matrix4x4.Translate(radialRimOffset) : Matrix4x4.identity;
                var matrix = fit * rimCorrection * wheelNode.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var baked = UnityEngine.Object.Instantiate(filter.sharedMesh);
                baked.name = "C63Wheel" + corner + "_" + meshIndex++;
                var vertices = baked.vertices;
                var normals = baked.normals;
                var tangents = baked.tangents;
                var normalMatrix = matrix.inverse.transpose;
                for (var i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                    if (normals.Length == vertices.Length)
                        normals[i] = normalMatrix.MultiplyVector(normals[i]).normalized;
                    if (tangents.Length == vertices.Length)
                    {
                        var tangent = matrix.MultiplyVector(tangents[i]).normalized;
                        tangents[i] = new Vector4(tangent.x, tangent.y, tangent.z,
                            tangents[i].w * Mathf.Sign(matrix.determinant));
                    }
                }
                if (matrix.determinant < 0f)
                    for (var submesh = 0; submesh < baked.subMeshCount; submesh++)
                    {
                        var indices = baked.GetTriangles(submesh);
                        for (var i = 0; i < indices.Length; i += 3)
                        { var swap = indices[i]; indices[i] = indices[i + 1]; indices[i + 1] = swap; }
                        baked.SetTriangles(indices, submesh);
                    }
                baked.vertices = vertices;
                if (normals.Length == vertices.Length) baked.normals = normals;
                if (tangents.Length == vertices.Length) baked.tangents = tangents;
                baked.RecalculateBounds();
                filter.sharedMesh = SaveMesh(baked);
                filter.transform.SetParent(mount.transform, false);
                filter.transform.localPosition = Vector3.zero;
                filter.transform.localRotation = Quaternion.identity;
                filter.transform.localScale = Vector3.one;
            }
            // Mesh nodes now belong exclusively to this rolling assembly.
            UnityEngine.Object.DestroyImmediate(wheelNode.gameObject);

            var fixedCaliper = new GameObject("C63FixedCaliper" + corner);
            fixedCaliper.transform.SetParent(root.transform, false);
            fixedCaliper.transform.localPosition = restingCenter;
            caliperNode.SetParent(fixedCaliper.transform, true);
            caliperNode.name = "Geometry_C63Caliper_" + (isFront ? "F" : "R") +
                               (corner.EndsWith("Left", StringComparison.Ordinal) ? "L" : "R");
            // Preserve the caliper offset relative to its original wheel center.
            caliperNode.position = root.transform.TransformPoint(restingCenter) + (caliperNode.position - sourceBounds.center);
            AssignWheelVisual(controller, mount);
            Debug.Log($"MercedesC63Cabrio fitted {corner}: tire={width:0.000}x{diameter:0.000}m, " +
                      $"wheelbase={Wheelbase:0.000}m, track={(isFront ? FrontTrack : RearTrack):0.000}m.");
        }
    }
    private static void ConfigureSteeringWheel(GameObject root, GameObject model)
    {
        EnsureAssetFolder(MeshFolder);
        MeshFilter? source = null;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            if (HasAncestorNameFragment(filter.transform, "M_Interior_SetRT_Max")) source = filter;
        if (source == null || source.sharedMesh == null) throw new InvalidOperationException("Mercedes interior mesh missing.");
        var original = source.sharedMesh;
        var matrix = root.transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
        var selected = MercedesC63CabrioMeshParts.Select(original, matrix, actual =>
        {
            var b = MercedesC63CabrioMeshParts.ToLegacyBodyBounds(actual);
            return             b.min.x > -0.515f && b.max.x < -0.15f && b.min.y > 0.67f && b.max.y < 1.06f &&
            b.min.z > 0.245f && b.max.z < 0.445f;
        });
        if (selected.Count < 3000) throw new InvalidOperationException("Mercedes steering geometry selection incomplete.");
        var selectedVertices = new HashSet<int>(selected);
        var remaining = new List<int>(); var triangles = original.triangles;
        for (var i = 0; i < triangles.Length; i += 3)
            if (!selectedVertices.Contains(triangles[i]))
            { remaining.Add(triangles[i]); remaining.Add(triangles[i+1]); remaining.Add(triangles[i+2]); }
        var pivot = new GameObject("C63SteeringWheel").transform;
        pivot.SetParent(model.transform, false);
        pivot.position = root.transform.TransformPoint(MercedesC63CabrioMeshParts.FromLegacyBodyPoint(new Vector3(-0.3336f, 0.8638f, 0.347f)));
        pivot.rotation = root.transform.rotation * MercedesC63CabrioTuning.BodyPitch * Quaternion.LookRotation(new Vector3(0.05f, -0.39f, 0.92f));
        var geometry = new GameObject("C63SteeringGeometry");
        geometry.layer = source.gameObject.layer;
        geometry.transform.SetParent(source.transform, false);
        geometry.transform.SetParent(pivot, true);
        geometry.AddComponent<MeshFilter>().sharedMesh = SaveMesh(
            MercedesC63CabrioMeshParts.Extract(original, selected, "C63SteeringWheel"));
        var renderer = geometry.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = source.GetComponent<MeshRenderer>().sharedMaterials;
        source.sharedMesh = SaveMesh(MercedesC63CabrioMeshParts.Extract(original, remaining, "C63InteriorWithoutSteering"));
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var serialized = new SerializedObject(component);
            var wheel = serialized.FindProperty("steering.steeringWheel");
            if (wheel != null)
            {
                wheel.objectReferenceValue = pivot;
                // Keep the visible wheel fixed, as requested; hands use this seat anchor.
                SetRelativeNumber(serialized, "steering.steeringWheelTurnRatio", 0f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        Debug.Log($"Mercedes steering extracted: {selected.Count / 3} triangles, pivot={pivot.position}.");
    }

    private static Mesh SaveMesh(Mesh mesh)
    {
        var path = MeshFolder + "/" + mesh.name + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(saved); return saved;
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
                    (IsBodyPaintMaterial(material) || IsInteriorAccentPaintMaterial(material)))
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
        var sources = new List<(MeshFilter filter, MeshRenderer renderer)>();
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!HasAncestorNameFragment(renderer.transform, "M_CarPaint_Max")) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            if (renderer.enabled && filter?.sharedMesh != null)
                sources.Add((filter, renderer));
        }
        if (sources.Count == 0)
            throw new InvalidOperationException("The C63 outer body meshes under M_CarPaint_Max were not found.");

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");
        var combines = new List<CombineInstance>();
        var materials = new List<Material>();
        foreach (var source in sources)
        {
            var mesh = source.filter.sharedMesh;
            var matrix = root.transform.worldToLocalMatrix * source.filter.transform.localToWorldMatrix;
            var materialSlots = source.renderer.sharedMaterials;
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (submesh >= materialSlots.Length || materialSlots[submesh] == null) continue;
                combines.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = submesh,
                    transform = matrix,
                });
                materials.Add(materialSlots[submesh]);
            }
        }
        if (combines.Count == 0)
            throw new InvalidOperationException("C63 body mesh submeshes have no persistent materials.");

        var totalVertices = 0;
        foreach (var source in sources) totalVertices += source.filter.sharedMesh.vertexCount;
        var bakedMesh = new Mesh { name = "C63DamageBody" };
        if (totalVertices > 65535) bakedMesh.indexFormat = IndexFormat.UInt32;
        bakedMesh.CombineMeshes(combines.ToArray(), false, true, false);
        bakedMesh.RecalculateBounds();
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

        var damageBody = new GameObject("C63DamageBody");
        damageBody.transform.SetParent(root.transform, false);
        var damageFilter = damageBody.AddComponent<MeshFilter>();
        damageFilter.sharedMesh = persistentMesh;
        var damageRenderer = damageBody.AddComponent<MeshRenderer>();
        damageRenderer.sharedMaterials = materials.ToArray();
        damageRenderer.shadowCastingMode = sources[0].renderer.shadowCastingMode;
        damageRenderer.receiveShadows = sources[0].renderer.receiveShadows;
        damageRenderer.lightProbeUsage = sources[0].renderer.lightProbeUsage;
        damageRenderer.reflectionProbeUsage = sources[0].renderer.reflectionProbeUsage;
        damageRenderer.motionVectorGenerationMode = sources[0].renderer.motionVectorGenerationMode;
        damageRenderer.allowOcclusionWhenDynamic = sources[0].renderer.allowOcclusionWhenDynamic;
        damageRenderer.renderingLayerMask = sources[0].renderer.renderingLayerMask;
        foreach (var source in sources)
        {
            source.renderer.enabled = false;
            source.renderer.sharedMaterials = Array.Empty<Material>();
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
        material.name.IndexOf("C63CarPaint", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsCabinGlassRenderer(Transform transform) =>
        HasAncestorNameFragment(transform, "M_Glass_WindowFront_Max") ||
        HasAncestorNameFragment(transform, "M_Glass_WindowSide_Max") ||
        HasAncestorNameFragment(transform, "M_Glass_WindowRear_Max");

    private static bool IsInteriorAccentPaintMaterial(Material material) =>
        material.name.IndexOf("stitch", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("seat_leather_2", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("B60000", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRimMaterial(Material material) =>
        material.name.IndexOf("M_Rim_Main_Max1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!MercedesC63CabrioMaterials.IsC63Renderer(renderer.transform))
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
            throw new InvalidOperationException("The shared Mercedes C63 rim material is missing.");

        ApplyRimFinish(leftMaterial, MercedesC63CabrioMaterials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!MercedesC63CabrioMaterials.IsC63Renderer(renderer.transform))
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
                $"Expected one Mercedes C63 main-rim material slot per wheel, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", MercedesC63CabrioMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", MercedesC63CabrioMaterials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("CallipersCallip", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool C63PowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateC63PowerCurve();
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
        var replacements = new Dictionary<(Material, string), Material>();
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

                var transparent = MercedesC63CabrioMaterials.IsTransparentMaterial(source);
                var kind = transparent ? "Transparent" : "Opaque";
                var semantic = HasAncestorNameFragment(renderer.transform, "M_CarPaint_Max")
                    ? "C63CarPaint"
                    : IsRimMaterial(source) ? "C63Rim"
                    : IsCaliperMaterial(source) ? "C63Caliper"
                    : "MercedesC63" + kind;
                // Imported materials can be shared by paint and chrome. Split by role.
                if (!replacements.TryGetValue((source, semantic), out var persistent))
                {
                    var materialIndex = transparent ? transparentMaterialIndex++ : opaqueMaterialIndex++;
                    var assetName =
                        $"{semantic}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

                    MercedesC63CabrioSourceMaterials.Apply(persistent, source.name);
                    MercedesC63CabrioMaterials.PreparePersistentMaterial(renderer, persistent);
                    EditorUtility.SetDirty(persistent);
                    replacements.Add((source, semantic), persistent);
                }

                materials[index] = persistent;
                changed = true;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }
    }

    private static void ValidateRenderableMaterials(GameObject root, bool requirePersistent)
    {
        var slots = 0;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
            var materials = renderer.sharedMaterials;
            if (materials.Length < filter.sharedMesh.subMeshCount)
                throw new InvalidOperationException($"Missing material slots on {filter.name}.");
            foreach (var material in materials)
            {
                if (material == null || material.shader == null ||
                    material.shader.name.Contains("InternalErrorShader"))
                    throw new InvalidOperationException($"Invalid material on {filter.name}.");
                if (requirePersistent && !EditorUtility.IsPersistent(material))
                    throw new InvalidOperationException($"Transient material on {filter.name}: {material.name}.");
                slots++;
            }
        }
        if (slots < 50) throw new InvalidOperationException($"Incomplete Mercedes materials: {slots} slots.");
        Debug.Log($"Mercedes material audit: {slots} valid slots, persistent={requirePersistent}.");
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

    private static void PruneUnusedGeneratedAssets()
    {
        var used = new HashSet<string>(AssetDatabase.GetDependencies(VehiclePrefabPath, true), StringComparer.Ordinal);
        foreach (var folder in new[] { MaterialFolder, MeshFolder, ModRoot + "/Models/Textures" })
            foreach (var guid in AssetDatabase.FindAssets("", new[] { folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith(folder + "/", StringComparison.Ordinal) || used.Contains(path) ||
                    AssetDatabase.IsValidFolder(path)) continue;
                if (!AssetDatabase.DeleteAsset(path)) throw new InvalidOperationException($"Could not remove unused generated asset: {path}.");
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

        manifest.ModId = "MercedesC63Cabrio";
        manifest.DisplayName = "Mercedes-AMG C63 Cabriolet";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "mercedes_c63_cabrio.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Mercedes C63 manifest references could not be assigned.");
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
            if (HasAncestorNameFragment(renderer.transform, "3DWheel ") ||
                HasAncestorNameFragment(renderer.transform, "Calliper Front ") ||
                HasAncestorNameFragment(renderer.transform, "Calliper Rear "))
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

    private static bool TryGetC63RendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!MercedesC63CabrioMaterials.IsC63Renderer(renderer.transform))
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

