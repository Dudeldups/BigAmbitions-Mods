#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class DodgeChallenger2018Setup
{
    private const string ModRoot = "Assets/Mods/Dodge_Challenger_2018";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/2018_dodge_challenger_srt_demon_hpe1200.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/DodgeDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/DodgeChallenger2018.asset";
    private const string VehiclePrefabPath = ModRoot + "/DodgeChallenger2018.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/DodgeChallenger2018.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/dodgechallenger2018.unity3d";
    private const string VehicleTypeName =
        "dodgechallenger2018-vehicle:vehicletype_dodgechallenger2018";
    private const float TargetLength = 5.0156f;
    private const float TargetWidth = 2.1787f; // including mirrors; uploaded GLB matches this envelope
    private const float TargetHeight = 1.459f;
    private const float FrontTrack = 1.6714f;
    private const float RearTrack = 1.6678f;
    private const float Wheelbase = 2.9504f;
    private const float FrontTireRadius = 0.3546f;
    private const float RearTireRadius = 0.3546f;
    private const float FrontTireWidth = 0.315f;
    private const float RearTireWidth = 0.315f;
    private const float WheelInset = 0f;
    private const float WheelCenterRideHeightOffset = 0f;
    private const float VisualBodyOffsetY = -0.020f;
    private const float VehicleLinearDrag = 0.038f;
    private const float VehicleBrakeForce = 2000f;
    private const float BrakeMaxTorque = 2000f;
    private const float FrontForwardGrip = 0.90f;
    private const float RearForwardGrip = 0.95f;
    private const float FrontForwardStiffness = 1.27f;
    private const float RearForwardStiffness = 0.70f;
    private const float TireFrictionCircleStrength = 1.00f;
    private const float AntiRollBarForce = 9500f;
    private const float FrontSuspensionTravel = 0.090f;
    private const float RearSuspensionTravel = 0.095f;
    private const float DeformationStrength = 0.12f;
    private const float DeformationRadius = 0.28f;
    private const float DeformationRandomness = 0.005f;
    private static readonly string[] WheelReferenceMeshNames =
    {
        "Dodge_HPE1200Demon_2018_Modified_CSB:Wheel_01_LF_Dodge_HPE1200Demon_2018_Modified_CSB:Wheel2Mtl1_0",
        "Dodge_HPE1200Demon_2018_Modified_CSB:Wheel_01_LR_Dodge_HPE1200Demon_2018_Modified_CSB:Wheel2Mtl1_0",
        "Dodge_HPE1200Demon_2018_Modified_CSB:Wheel_01_RF_Dodge_HPE1200Demon_2018_Modified_CSB:Wheel2Mtl1_0",
        "Dodge_HPE1200Demon_2018_Modified_CSB:Wheel_01_RR_Dodge_HPE1200Demon_2018_Modified_CSB:Wheel2Mtl1_0",
    };
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.22f, -0.12f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController",  new Vector3(-0.8206f, FrontTireRadius,  1.5532f) },
            { "FrontRight_WheelController", new Vector3( 0.8206f, FrontTireRadius,  1.5532f) },
            { "RearLeft_WheelController",   new Vector3(-0.8224f, RearTireRadius, -1.3928f) },
            { "RearRight_WheelController",  new Vector3( 0.8224f, RearTireRadius, -1.3928f) },
        };
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.52f, 1.93f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.92f, 0.56f, 1.12f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.53f, -2.02f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.92f, 0.58f, 1.14f);

    private static readonly float[] DemonGears =
    {
        -3.32f,
        0f,
        4.71f,
        3.14f,
        2.10f,
        1.67f,
        1.29f,
        1.00f,
        0.84f,
        0.67f,
    };

    private static AnimationCurve CreateDemonPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.12f, 0.05f),
            new Keyframe(0.22f, 0.16f),
            new Keyframe(0.32f, 0.30f),
            new Keyframe(0.46f, 0.54f),
            new Keyframe(0.62f, 0.72f),
            new Keyframe(0.78f, 0.90f),
            new Keyframe(0.86f, 1.00f),
            new Keyframe(1f, 0.93f));

    [MenuItem("Big Ambitions Mods/Setup 2018 Dodge Challenger")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "DodgeChallenger2018 setup complete: generated a fitted Challenger SRT Demon HPE1200 with four " +
            "independent wheel visuals, eight-speed 8HP90, RWD, supercharged-V8 audio, functional lamp " +
            "geometry, colorable body/calipers, and corrected glass materials.");
    }

    public static void GenerateAndBuild()
    {
        try
        {
            Debug.Log("DodgeChallenger2018 BUILD_STAGE: Generate");
            Generate();
            Debug.Log("DodgeChallenger2018 BUILD_STAGE: BuildForMod");
            ModAssetBundleCli.BuildForMod();
            Debug.Log("DodgeChallenger2018 BUILD_STAGE: VerifyBuiltBundle");
            VerifyBuiltBundle();
            Debug.Log("DodgeChallenger2018 BUILD_STAGE: Complete");
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "DodgeChallenger2018 BUILD_FAILURE: " +
                exception.GetType().FullName + ": " + exception.Message);
            Debug.LogException(exception);
            Console.Error.WriteLine(exception.ToString());
            throw;
        }
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

            var serialized = new SerializedObject(vehicleType);
            var price = ReadNumber(serialized.FindProperty("price"));
            var fuel = ReadNumber(serialized.FindProperty("maxFuel"));
            var speed = ReadNumber(serialized.FindProperty("maxSpeed"));
            var power = ReadNumber(serialized.FindProperty("enginePower"));
            var visual = FindTransform(prefab.transform, "DodgeVisual");
            if (visual == null || !TryGetRendererBounds(prefab.transform, out var bounds))
                throw new InvalidOperationException("Dodge visual/bounds are missing.");

            var wheelCount = 0;
            var caliperCount = 0;
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("DodgeWheel", StringComparison.Ordinal)) wheelCount++;
                if (t.name.StartsWith("DodgeFixedCaliper", StringComparison.Ordinal)) caliperCount++;
            }

            if (Math.Abs(price - 169945f) > .5f || Math.Abs(fuel - 70f) > .5f ||
                Math.Abs(speed - 350f) > .5f || Math.Abs(power - 895f) > .5f ||
                Math.Abs(bounds.size.z - TargetLength) > .10f ||
                Math.Abs(bounds.size.y - TargetHeight) > .10f ||
                Math.Abs(bounds.size.x - TargetWidth) > .12f ||
                wheelCount != 4 || caliperCount != 4)
            {
                throw new InvalidOperationException(
                    $"Dodge bundle verification failed price={price} fuel={fuel} speed={speed} power={power} " +
                    $"bounds={bounds.size} wheels={wheelCount} calipers={caliperCount}.");
            }

            Debug.Log($"DodgeChallenger2018 bundle verified: price={price}, speed={speed}, power={power}, bounds={bounds.size}.");
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
                throw new InvalidOperationException("Could not create the Dodge VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated Dodge VehicleType asset did not load.");

        target.name = "DodgeChallenger2018";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 169945f);
        SetNumber(serialized, "maxFuel", 70f);
        SetNumber(serialized, "maxCargoCapacity", 4f);
        SetNumber(serialized, "maxSpeed", 350f);
        SetNumber(serialized, "enginePower", 895f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", 29f);
        SetNumber(serialized, "damageIntensity", 0.34f);
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
            throw new InvalidOperationException("Dodge VehicleType asset was not found.");

        var serialized = new SerializedObject(vehicleType);
        SetNumber(serialized, "maxSpeed", 350f);
        SetNumber(serialized, "enginePower", 895f);
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
            throw new InvalidOperationException("Challenger GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "DodgeChallenger2018";
        try
        {
            StripAudiGeometry(root);
            RemoveAudiSpecificBehaviours(root);
            RemoveMissingScriptsRecursively(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the Dodge model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "DodgeVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureDodgeDriverReference(root);
            ConfigureExitMarkers(root, modelInstance);
            AssignPersistentMaterials(modelInstance);
            GeneratePersistentLampOverlays(root, modelInstance);
            // Extract and center the wheel assemblies while the imported GLB
            // hierarchy is still intact. Crash splitting can otherwise replace
            // candidate wheel renderers before their axle geometry is measured.
            AttachWheelVisuals(root, modelInstance);
            SplitCrashDetailMeshes(modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);
            RemoveDodgeRuntimeBehaviours(root);
            RemoveMissingScriptsRecursively(root);

            Debug.Log(
                $"DodgeChallenger2018: prepared persistent HDRP materials and prefab; " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, visualBodyOffsetY={VisualBodyOffsetY:F3}.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Dodge vehicle prefab.");
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

    private static void RemoveDodgeRuntimeBehaviours(GameObject root)
    {
        var removed = 0;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            if (!component.GetType().Name.StartsWith("DodgeChallenger2018", StringComparison.Ordinal))
                continue;
            UnityEngine.Object.DestroyImmediate(component);
            removed++;
        }
        if (removed > 0)
            Debug.Log($"DodgeChallenger2018: removed {removed} runtime-only component(s) before prefab save.");
    }

    private static void RemoveMissingScriptsRecursively(GameObject root)
    {
        var removed = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null)
                continue;
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
        }
        if (removed > 0)
            Debug.Log($"DodgeChallenger2018: removed {removed} missing donor script component(s).");
    }

    private static void RemoveModelLights(GameObject model)
    {
        foreach (var light in model.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(light.gameObject);
    }

    private static void ConfigureDodgeDriverReference(GameObject root)
    {
        var existing = FindTransform(root.transform, "DodgeSteeringReference");
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing.gameObject);

        var reference = new GameObject("DodgeSteeringReference");
        reference.transform.SetParent(root.transform, false);
        // Derived from a connected-component scan of the supplied Interior mesh.
        // The glTF handedness conversion places the LHD steering assembly on -X.
        // Z includes the +0.162 m centering shift applied by NormalizeModel().
        reference.transform.localPosition = new Vector3(-0.372f, 0.931f, 0.533f);
        reference.transform.localRotation = Quaternion.identity;
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        body.mass = 1941f;
        body.drag = VehicleLinearDrag;
        body.angularDrag = 1.80f;
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
            SetNumber(serialized, "baseMass", 1941f);
            SetNumber(serialized, "combinedMass", 1941f);
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

        colliders[0].center = new Vector3(0f, 0.34f, -0.04f);
        colliders[0].size = new Vector3(1.94f, 0.50f, 4.82f);
        colliders[1].center = new Vector3(0f, 0.82f, -0.18f);
        colliders[1].size = new Vector3(1.70f, 0.72f, 2.78f);

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

    private static void RepairStaticWheelVisuals(GameObject root)
    {
        foreach (var pair in WheelControllerPositions)
        {
            var corner = pair.Key.Replace("_WheelController", string.Empty);
            var controller = FindTransform(root.transform, pair.Key) ??
                             throw new InvalidOperationException($"Wheel controller '{pair.Key}' is missing.");
            var mount = FindTransform(root.transform, "DodgeWheel" + corner) ??
                        throw new InvalidOperationException($"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "DodgeFixedCaliper" + corner) ??
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
        // Current generated prefabs bake the true tire shell together with each
        // wheel around the correct axle center. Never apply non-uniform scale to
        // a rotating NWH visual here; that legacy repair path caused wobble.
        foreach (var pair in WheelControllerPositions)
        {
            var corner = pair.Key.Replace("_WheelController", string.Empty);
            var mount = FindTransform(root.transform, "DodgeWheel" + corner) ??
                        throw new InvalidOperationException(
                            $"Rolling visual for '{corner}' is missing.");
            mount.localScale = Vector3.one;
            mount.localPosition = pair.Value;
        }
    }

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        var steeringReference = FindTransform(root.transform, "DodgeSteeringReference") ??
                                throw new InvalidOperationException("Measured Dodge steering reference is missing.");
        // LHD Challenger: game vehicle convention uses negative X for driver side.
        var driverSide = -1.55f;
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.10f, 0.20f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.10f, 0.20f));
        Debug.Log(
            $"DodgeChallenger2018: steering reference={steeringReference.localPosition}; " +
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
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 900f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 300f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 900f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.clutch.slipTorque", 1650f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.20f);
                SetRelativeBool(serialized, "powertrain.engine.autoStartOnThrottle", true);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", 755f);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateDemonPowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 750f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 6500f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.38f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", true);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0.03f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.09f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.065f);
                SetRelativeNumber(serialized, "powertrain.transmission.postShiftBan", 0.35f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 3300f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 5200f);
                SetRelativeBool(serialized, "powertrain.transmission.variableShiftPoint", false);
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
                gears.arraySize = DemonGears.Length;
                for (var index = 0; index < DemonGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = DemonGears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 350f);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!found)
            throw new InvalidOperationException("NWH vehicle controller was not found on the reference prefab.");
    }

    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;

        // Unity/glTFast imports this particular GLB with:
        //   X = vehicle width
        //   Y = vehicle length
        //   Z = vehicle height
        // The source wheel naming also places LF/RF on +source X while the
        // Big Ambitions vehicle convention uses negative X for the left side.
        //
        // -90 degrees around X makes source Z the upright Unity Y axis.
        // The previous extra 180-degree Y turn made the visible nose face the
        // physics rear: forward input looked like reverse and the visible rear
        // wheels were attached to the steering axle. Keep only the axis conversion.
        model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        model.transform.localScale = Vector3.one * 10f;

        if (!TryGetModelBodyBounds(model.transform, out var bounds))
            throw new InvalidOperationException("Dodge model bounds could not be measured.");

        // Center X/Z around the vehicle root. Keep the wheel/controller ride height
        // unchanged, but lower the visible body by 20 mm per the in-game fitment test.
        model.transform.position += new Vector3(
            -bounds.center.x,
            -bounds.min.y + VisualBodyOffsetY,
            -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Dodge bounds could not be measured.");

        if (Math.Abs(bounds.size.z - TargetLength) > 0.08f ||
            Math.Abs(bounds.size.y - TargetHeight) > 0.08f ||
            Math.Abs(bounds.size.x - TargetWidth) > 0.10f)
        {
            throw new InvalidOperationException(
                $"Supplied Challenger GLB does not match researched dimensions after orientation + 10x scale: {bounds.size}.");
        }

        Debug.Log(
            $"DodgeChallenger2018: normalized supplied GLB rotation=(-90X), " +
            $"scale=10, bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var brakes = new List<Transform>();
        foreach (var candidate in model.GetComponentsInChildren<Transform>(true))
        {
            if (candidate.name.IndexOf(":Caliper", StringComparison.OrdinalIgnoreCase) >= 0 &&
                candidate.parent != null &&
                candidate.parent.name.IndexOf(":Caliper", StringComparison.OrdinalIgnoreCase) >= 0 &&
                !brakes.Contains(candidate.parent))
            {
                brakes.Add(candidate.parent);
            }
        }

        if (brakes.Count != 4)
            throw new InvalidOperationException(
                $"Expected four Dodge caliper assemblies; found calipers={brakes.Count}.");

        foreach (var expectedReferenceName in WheelReferenceMeshNames)
        {
            var reference = FindExactWheelReference(model, expectedReferenceName) ??
                            throw new InvalidOperationException(
                                $"Exact Dodge wheel mesh '{expectedReferenceName}' is missing.");

            var controllerName = ResolveWheelControllerName(expectedReferenceName);
            var controller = FindTransform(root.transform, controllerName) ??
                             throw new InvalidOperationException(
                                 $"Wheel controller '{controllerName}' is missing.");
            var targetCenter = WheelControllerPositions[controllerName];
            controller.localPosition = targetCenter;

            var corner = controllerName.Replace("_WheelController", string.Empty);
            var isFront = corner.StartsWith("Front", StringComparison.Ordinal);
            var isLeft = corner.EndsWith("Left", StringComparison.Ordinal);
            var targetWidth = isFront ? FrontTireWidth : RearTireWidth;
            var targetRadius = isFront ? FrontTireRadius : RearTireRadius;

            var brake = FindClosestBrake(reference.transform, brakes) ??
                        throw new InvalidOperationException(
                            $"Exact wheel '{expectedReferenceName}' has no nearby caliper assembly.");
            brakes.Remove(brake);

            var mount = new GameObject("DodgeWheel" + corner);
            mount.transform.SetParent(root.transform, false);
            mount.transform.localPosition = targetCenter;
            mount.transform.localRotation = Quaternion.identity;
            mount.transform.localScale = Vector3.one;

            BakeExactWheelVisual(
                root,
                reference,
                mount.transform,
                targetWidth,
                targetRadius,
                corner);

            // The caliper must follow steering/suspension, but never wheel spin.
            var fixedCaliper = new GameObject("DodgeFixedCaliper" + corner);
            fixedCaliper.transform.SetParent(root.transform, false);
            fixedCaliper.transform.localPosition = targetCenter;
            fixedCaliper.transform.localRotation = Quaternion.identity;
            fixedCaliper.transform.localScale = Vector3.one;
            brake.SetParent(fixedCaliper.transform, true);
            brake.name = "Geometry_DodgeCaliper_" +
                         (isFront ? "F" : "R") + (isLeft ? "L" : "R");
            CreateCaliperBranding(fixedCaliper.transform, brake, isLeft, corner);

            AssignWheelVisual(controller, mount);

            // Remove the authored source hierarchy after the exact wheel mesh has
            // been baked. This prevents a second, body-static wheel from surviving
            // beside the NWH visual.
            var sourceGroup = FindSourceWheelGroup(reference.transform, model.transform);
            UnityEngine.Object.DestroyImmediate(sourceGroup.gameObject);

            Debug.Log(
                $"DodgeChallenger2018: exact wheel {corner} source='{expectedReferenceName}', " +
                $"target={targetCenter}, width={targetWidth:F3}m, diameter={targetRadius * 2f:F3}m.");
        }

        if (brakes.Count != 0)
            throw new InvalidOperationException(
                $"Dodge wheel fit left unmatched calipers={brakes.Count}.");
    }

    private static MeshFilter? FindExactWheelReference(
        GameObject model,
        string expectedName)
    {
        MeshFilter? found = null;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponent<MeshRenderer>() == null)
                continue;

            if (!string.Equals(filter.name, expectedName, StringComparison.Ordinal) &&
                !string.Equals(filter.sharedMesh.name, expectedName, StringComparison.Ordinal))
            {
                continue;
            }

            if (found != null)
            {
                throw new InvalidOperationException(
                    $"Exact wheel reference '{expectedName}' is present more than once.");
            }

            found = filter;
        }

        return found;
    }

    private static string ResolveWheelControllerName(string referenceName)
    {
        if (referenceName.IndexOf(":Wheel_01_LF_", StringComparison.OrdinalIgnoreCase) >= 0)
            return "FrontLeft_WheelController";
        if (referenceName.IndexOf(":Wheel_01_RF_", StringComparison.OrdinalIgnoreCase) >= 0)
            return "FrontRight_WheelController";
        if (referenceName.IndexOf(":Wheel_01_LR_", StringComparison.OrdinalIgnoreCase) >= 0)
            return "RearLeft_WheelController";
        if (referenceName.IndexOf(":Wheel_01_RR_", StringComparison.OrdinalIgnoreCase) >= 0)
            return "RearRight_WheelController";

        throw new InvalidOperationException(
            $"Could not map exact wheel reference '{referenceName}' to a vehicle corner.");
    }

    private static Transform FindSourceWheelGroup(
        Transform reference,
        Transform modelRoot)
    {
        var current = reference;
        while (current.parent != null &&
               current.parent != modelRoot &&
               current.parent.name.IndexOf(":Wheel_01_", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            current = current.parent;
        }

        return current;
    }

    private static void BakeExactWheelVisual(
        GameObject root,
        MeshFilter reference,
        Transform mount,
        float targetWidth,
        float targetRadius,
        string corner)
    {
        var sourceRenderer = reference.GetComponent<MeshRenderer>() ??
                             throw new InvalidOperationException(
                                 $"Exact Dodge wheel '{reference.name}' has no MeshRenderer.");
        var sourceMesh = reference.sharedMesh ??
                         throw new InvalidOperationException(
                             $"Exact Dodge wheel '{reference.name}' has no mesh.");

        var sourceToRoot =
            root.transform.worldToLocalMatrix * reference.transform.localToWorldMatrix;

        var localSize = sourceMesh.bounds.size;
        Vector3 localAxle;
        if (localSize.x <= localSize.y && localSize.x <= localSize.z)
            localAxle = Vector3.right;
        else if (localSize.y <= localSize.x && localSize.y <= localSize.z)
            localAxle = Vector3.up;
        else
            localAxle = Vector3.forward;

        var fallbackAxle = sourceToRoot.MultiplyVector(localAxle).normalized;
        var rootPoints = new List<Vector3>(sourceMesh.vertexCount);
        foreach (var vertex in sourceMesh.vertices)
            rootPoints.Add(sourceToRoot.MultiplyPoint3x4(vertex));

        // Do not assume the mesh-local X/Y/Z axes describe the actual wheel
        // plane. Front-wheel source geometry can carry baked steering/camber in
        // its vertices even when the Transform itself looks axis-aligned. Fit the
        // smallest-variance principal axis of the exact wheel/disc geometry.
        var authoredAxle = FitWheelAxle(rootPoints, fallbackAxle);
        if (authoredAxle.sqrMagnitude < 0.5f)
            throw new InvalidOperationException(
                $"Exact Dodge wheel '{reference.name}' has an invalid fitted axle.");
        if (Vector3.Dot(authoredAxle, Vector3.right) < 0f)
            authoredAxle = -authoredAxle;

        var axleCorrection =
            Quaternion.FromToRotation(authoredAxle, Vector3.right);
        var correctionAngle =
            Quaternion.Angle(Quaternion.identity, axleCorrection);

        // Rotate the exact Blender mesh into NWH axle space first, then fit
        // the real radial circle center from its outer geometry.
        var correctedPoints = new List<Vector3>(rootPoints.Count);
        var roughBounds = default(Bounds);
        var hasRoughBounds = false;
        foreach (var rootPoint in rootPoints)
        {
            var correctedRoot = axleCorrection * rootPoint;
            correctedPoints.Add(correctedRoot);
            if (!hasRoughBounds)
            {
                roughBounds = new Bounds(correctedRoot, Vector3.zero);
                hasRoughBounds = true;
            }
            else
            {
                roughBounds.Encapsulate(correctedRoot);
            }
        }

        if (!hasRoughBounds)
            throw new InvalidOperationException(
                $"Exact Dodge wheel '{reference.name}' has no vertices.");

        var fittedRadialCenter = FitWheelRadialCenter(correctedPoints, roughBounds);
        var correctedCenter = new Vector3(
            roughBounds.center.x,
            fittedRadialCenter.x,
            fittedRadialCenter.y);
        var authoredCenter =
            Quaternion.Inverse(axleCorrection) * correctedCenter;

        var correctedBounds = default(Bounds);
        var hasBounds = false;
        foreach (var correctedRoot in correctedPoints)
        {
            var corrected = correctedRoot - correctedCenter;
            if (!hasBounds)
            {
                correctedBounds = new Bounds(corrected, Vector3.zero);
                hasBounds = true;
            }
            else
            {
                correctedBounds.Encapsulate(corrected);
            }
        }

        if (!hasBounds ||
            correctedBounds.size.x <= 0.001f ||
            correctedBounds.size.y <= 0.001f ||
            correctedBounds.size.z <= 0.001f)
        {
            throw new InvalidOperationException(
                $"Exact Dodge wheel '{reference.name}' has invalid corrected bounds.");
        }

        // Never stretch Y and Z independently. A rotating wheel must stay
        // perfectly circular in its radial plane or it visibly 'breathes'/wobbles.
        var sourceDiameter =
            Mathf.Max(correctedBounds.size.y, correctedBounds.size.z);
        var radialScale = (targetRadius * 2f) / sourceDiameter;
        var widthScale = targetWidth / correctedBounds.size.x;
        var fit = new Vector3(widthScale, radialScale, radialScale);

        var sourceToMount =
            Matrix4x4.Scale(fit) *
            Matrix4x4.Rotate(axleCorrection) *
            Matrix4x4.Translate(-authoredCenter) *
            sourceToRoot;

        var baked = UnityEngine.Object.Instantiate(sourceMesh);
        baked.name = $"DodgeWheel{corner}";

        var vertices = baked.vertices;
        for (var index = 0; index < vertices.Length; index++)
            vertices[index] = sourceToMount.MultiplyPoint3x4(vertices[index]);
        baked.vertices = vertices;

        var normals = baked.normals;
        if (normals.Length == vertices.Length)
        {
            var normalMatrix = sourceToMount.inverse.transpose;
            for (var index = 0; index < normals.Length; index++)
                normals[index] =
                    normalMatrix.MultiplyVector(normals[index]).normalized;
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
                {
                    var temporary = triangles[index + 1];
                    triangles[index + 1] = triangles[index + 2];
                    triangles[index + 2] = temporary;
                }
                baked.SetTriangles(triangles, subMesh, false);
            }
        }

        baked.RecalculateBounds();

        // Do NOT use the complete mesh AABB center as the spin center. The exact
        // Blender object also contains asymmetric spoke/brake-disc geometry, so
        // its bounding box can legitimately sit about a millimetre off-axis even
        // when the outer wheel ring is concentric. Fit the outer radial circle
        // again on the final baked vertices and remove only that true radial
        // center error.
        var bakedPoints = new List<Vector3>(vertices.Length);
        var bakedRoughBounds = default(Bounds);
        var hasBakedBounds = false;
        foreach (var vertex in baked.vertices)
        {
            bakedPoints.Add(vertex);
            if (!hasBakedBounds)
            {
                bakedRoughBounds = new Bounds(vertex, Vector3.zero);
                hasBakedBounds = true;
            }
            else
            {
                bakedRoughBounds.Encapsulate(vertex);
            }
        }

        var finalRadialCenter =
            FitWheelRadialCenter(bakedPoints, bakedRoughBounds);
        if (finalRadialCenter.sqrMagnitude > 0f)
        {
            vertices = baked.vertices;
            for (var index = 0; index < vertices.Length; index++)
            {
                var vertex = vertices[index];
                vertex.y -= finalRadialCenter.x;
                vertex.z -= finalRadialCenter.y;
                vertices[index] = vertex;
            }
            baked.vertices = vertices;
            baked.RecalculateBounds();
        }

        // Validate the same geometric feature that defines wheel spin: the outer
        // circle center, not the full mesh AABB center.
        bakedPoints.Clear();
        bakedRoughBounds = default;
        hasBakedBounds = false;
        foreach (var vertex in baked.vertices)
        {
            bakedPoints.Add(vertex);
            if (!hasBakedBounds)
            {
                bakedRoughBounds = new Bounds(vertex, Vector3.zero);
                hasBakedBounds = true;
            }
            else
            {
                bakedRoughBounds.Encapsulate(vertex);
            }
        }

        var residualRadialCenter =
            FitWheelRadialCenter(bakedPoints, bakedRoughBounds);
        var radialOffset = residualRadialCenter.magnitude;
        if (radialOffset > 0.00025f)
        {
            UnityEngine.Object.DestroyImmediate(baked);
            throw new InvalidOperationException(
                $"Exact Dodge wheel '{reference.name}' outer circle remains off-axis by " +
                $"{radialOffset * 1000f:F3} mm after recentering.");
        }

        Debug.Log(
            $"DodgeChallenger2018: exact {corner} radial recenter " +
            $"initial={finalRadialCenter * 1000f}mm, residual={residualRadialCenter * 1000f}mm, " +
            $"aabbCenter={baked.bounds.center}.");

        baked.UploadMeshData(false);

        EnsureAssetFolder(MeshFolder);
        var meshPath = $"{MeshFolder}/DodgeWheel{corner}.asset";
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

        var geometry = new GameObject($"Geometry_DodgeWheel_{corner}")
        {
            layer = sourceRenderer.gameObject.layer
        };
        geometry.transform.SetParent(mount, false);
        geometry.transform.localPosition = Vector3.zero;
        geometry.transform.localRotation = Quaternion.identity;
        geometry.transform.localScale = Vector3.one;
        geometry.AddComponent<MeshFilter>().sharedMesh = persistent;

        var renderer = geometry.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = sourceRenderer.sharedMaterials;
        renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        renderer.receiveShadows = sourceRenderer.receiveShadows;
        renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;

        Debug.Log(
            $"DodgeChallenger2018: baked exact {corner} wheel " +
            $"source='{reference.name}', localBounds={localSize}, corrected={correctedBounds.size}, " +
            $"center={authoredCenter}, axle={authoredAxle}, axleCorrection={correctionAngle:F3}deg, " +
            $"fit={fit}, bakedCenter={persistent.bounds.center}, bakedSize={persistent.bounds.size}.");
    }

    private static Vector3 FitWheelAxle(
        List<Vector3> points,
        Vector3 fallback)
    {
        if (points.Count < 16)
            return fallback.sqrMagnitude > 0.5f
                ? fallback.normalized
                : Vector3.right;

        var mean = Vector3.zero;
        foreach (var point in points)
            mean += point;
        mean /= points.Count;

        double xx = 0d, xy = 0d, xz = 0d;
        double yy = 0d, yz = 0d, zz = 0d;
        foreach (var point in points)
        {
            var dx = point.x - mean.x;
            var dy = point.y - mean.y;
            var dz = point.z - mean.z;
            xx += dx * dx;
            xy += dx * dy;
            xz += dx * dz;
            yy += dy * dy;
            yz += dy * dz;
            zz += dz * dz;
        }

        var inverseCount = 1d / points.Count;
        xx *= inverseCount;
        xy *= inverseCount;
        xz *= inverseCount;
        yy *= inverseCount;
        yz *= inverseCount;
        zz *= inverseCount;

        // Inverse power iteration converges on the eigenvector belonging to the
        // smallest covariance eigenvalue: the thin axle direction of a wheel.
        var trace = xx + yy + zz;
        var regularization = Math.Max(1e-12d, trace * 1e-9d);
        var matrix = new double[,]
        {
            { xx + regularization, xy, xz },
            { xy, yy + regularization, yz },
            { xz, yz, zz + regularization },
        };

        var axis = fallback.sqrMagnitude > 0.5f
            ? fallback.normalized
            : Vector3.right;

        for (var iteration = 0; iteration < 10; iteration++)
        {
            var rhs = new[]
            {
                (double)axis.x,
                (double)axis.y,
                (double)axis.z,
            };
            if (!TrySolve3x3(matrix, rhs, out var solution))
                break;

            var next = new Vector3(
                (float)solution[0],
                (float)solution[1],
                (float)solution[2]);
            if (next.sqrMagnitude < 1e-10f)
                break;

            next.Normalize();
            if (Vector3.Dot(next, axis) < 0f)
                next = -next;
            axis = next;
        }

        if (axis.sqrMagnitude < 0.5f)
            return fallback.sqrMagnitude > 0.5f
                ? fallback.normalized
                : Vector3.right;

        return axis.normalized;
    }

    private static Vector2 FitWheelRadialCenter(
        List<Vector3> points,
        Bounds roughBounds)
    {
        var roughCenter = new Vector2(
            roughBounds.center.y,
            roughBounds.center.z);

        var maxRadius = 0f;
        foreach (var point in points)
        {
            var radius = Vector2.Distance(
                new Vector2(point.y, point.z),
                roughCenter);
            if (radius > maxRadius)
                maxRadius = radius;
        }

        if (maxRadius <= 0.001f)
            return roughCenter;

        // Only the outer circular ring defines the spin center. Spokes, hub and
        // brake disc would bias a centroid toward their asymmetric topology.
        var threshold = maxRadius * 0.90f;
        double syy = 0d, syz = 0d, sy = 0d;
        double szz = 0d, sz = 0d;
        double by = 0d, bz = 0d, bc = 0d;
        var count = 0;

        foreach (var point in points)
        {
            var y = point.y - roughCenter.x;
            var z = point.z - roughCenter.y;
            var radius = Math.Sqrt(y * y + z * z);
            if (radius < threshold)
                continue;

            var q = y * y + z * z;
            syy += y * y;
            syz += y * z;
            sy += y;
            szz += z * z;
            sz += z;
            by += y * q;
            bz += z * q;
            bc += q;
            count++;
        }

        if (count < 12)
            return roughCenter;

        var matrix = new double[,]
        {
            { syy, syz, sy },
            { syz, szz, sz },
            { sy,  sz,  count },
        };
        var vector = new[] { by, bz, bc };

        if (!TrySolve3x3(matrix, vector, out var solution))
            return roughCenter;

        var correction = new Vector2(
            (float)(solution[0] * 0.5d),
            (float)(solution[1] * 0.5d));

        // A real wheel center can only differ slightly from its radial AABB
        // center. Reject an unstable algebraic fit instead of creating nonsense.
        if (float.IsNaN(correction.x) ||
            float.IsInfinity(correction.x) ||
            float.IsNaN(correction.y) ||
            float.IsInfinity(correction.y) ||
            correction.magnitude > 0.05f)
        {
            return roughCenter;
        }

        return roughCenter + correction;
    }

    private static bool TrySolve3x3(
        double[,] matrix,
        double[] vector,
        out double[] solution)
    {
        solution = new double[3];
        var augmented = new double[3, 4];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
                augmented[row, column] = matrix[row, column];
            augmented[row, 3] = vector[row];
        }

        for (var pivot = 0; pivot < 3; pivot++)
        {
            var bestRow = pivot;
            var bestValue = Math.Abs(augmented[pivot, pivot]);
            for (var row = pivot + 1; row < 3; row++)
            {
                var value = Math.Abs(augmented[row, pivot]);
                if (value <= bestValue)
                    continue;
                bestValue = value;
                bestRow = row;
            }

            if (bestValue < 1e-12d)
                return false;

            if (bestRow != pivot)
            {
                for (var column = pivot; column < 4; column++)
                {
                    var temporary = augmented[pivot, column];
                    augmented[pivot, column] = augmented[bestRow, column];
                    augmented[bestRow, column] = temporary;
                }
            }

            var divisor = augmented[pivot, pivot];
            for (var column = pivot; column < 4; column++)
                augmented[pivot, column] /= divisor;

            for (var row = 0; row < 3; row++)
            {
                if (row == pivot)
                    continue;

                var factor = augmented[row, pivot];
                for (var column = pivot; column < 4; column++)
                    augmented[row, column] -= factor * augmented[pivot, column];
            }
        }

        for (var row = 0; row < 3; row++)
            solution[row] = augmented[row, 3];
        return true;
    }

    private static void CreateCaliperBranding(
        Transform fixedCaliper,
        Transform caliperGeometry,
        bool isLeft,
        string corner)
    {
        if (!TryGetRendererBounds(caliperGeometry, out var worldBounds))
            return;

        var localCenter = fixedCaliper.InverseTransformPoint(worldBounds.center);
        var outward = isLeft ? -1f : 1f;

        var labelObject = new GameObject("DodgeBremboLabel" + corner);
        labelObject.transform.SetParent(fixedCaliper, false);
        labelObject.transform.localPosition =
            localCenter + new Vector3(outward * 0.105f, 0.015f, 0f);
        labelObject.transform.localRotation =
            Quaternion.Euler(0f, isLeft ? -90f : 90f, 0f);
        labelObject.transform.localScale = Vector3.one;

        var label = labelObject.AddComponent<TextMesh>();
        label.text = "brembo";
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 48;
        label.characterSize = 0.024f;
        label.color = Color.white;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            label.font = font;
            var renderer = label.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = font.material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        Debug.Log($"DodgeChallenger2018: added Brembo branding to {corner} caliper.");
    }

    private static void GeneratePersistentLampOverlays(GameObject root, GameObject model)
    {
        MeshRenderer? lightSource = null;
        MeshRenderer? rearLensSource = null;
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (lightSource == null &&
                (renderer.name.IndexOf(":Light_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 HasMaterialMarker(renderer, "LightMtl1")))
            {
                lightSource = renderer;
            }

            if (rearLensSource == null &&
                (renderer.name.IndexOf(":GlassRed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 HasMaterialMarker(renderer, "GlassRedMtl1")))
            {
                rearLensSource = renderer;
            }
        }

        var lightFilter = lightSource?.GetComponent<MeshFilter>();
        var rearLensFilter = rearLensSource?.GetComponent<MeshFilter>();
        if (lightSource == null || lightFilter?.sharedMesh == null)
            throw new InvalidOperationException("Dodge light source mesh was not available during prefab generation.");
        if (rearLensSource == null || rearLensFilter?.sharedMesh == null)
            throw new InvalidOperationException("Dodge rear red lens mesh was not available during prefab generation.");

        // Front: both round headlamp units per side plus the outer indicator pieces.
        CreatePersistentLampOverlay(root, lightSource, lightFilter.sharedMesh,
            "FrontWhiteLeft",
            p => p.z > 1.68f && p.x < -0.24f && p.x > -0.84f);
        CreatePersistentLampOverlay(root, lightSource, lightFilter.sharedMesh,
            "FrontWhiteRight",
            p => p.z > 1.68f && p.x > 0.24f && p.x < 0.84f);
        CreatePersistentLampOverlay(root, lightSource, lightFilter.sharedMesh,
            "FrontIndicatorLeft",
            p => p.z > 1.60f && p.x <= -0.84f);
        CreatePersistentLampOverlay(root, lightSource, lightFilter.sharedMesh,
            "FrontIndicatorRight",
            p => p.z > 1.60f && p.x >= 0.84f);

        // Rear: the previous LightMtl1-only selection contains mostly the vertical
        // internal LED segments, which is why the screenshot showed only partial
        // illumination. The actual complete red taillamp face lives in GlassRed.
        // Use that lens for tail/brake illumination, then place reverse/turn
        // elements from LightMtl1 on top so the inner functions remain visible.
        CreatePersistentLampOverlay(root, rearLensSource, rearLensFilter.sharedMesh,
            "RearTail",
            p => IsMainRearLampFace(p));
        CreatePersistentLampOverlay(root, rearLensSource, rearLensFilter.sharedMesh,
            "RearBrake",
            p => IsMainRearLampFace(p));

        // The authored lamp shell is open toward the cabin. Add two opaque backing
        // plates behind the visible red lens so no camera angle can see through the
        // brake/tail lamp into the vehicle interior.
        CreateRearLampHousing(root, rearLensSource, rearLensFilter.sharedMesh, true);
        CreateRearLampHousing(root, rearLensSource, rearLensFilter.sharedMesh, false);

        CreatePersistentLampOverlay(root, lightSource, lightFilter.sharedMesh,
            "ThirdBrake",
            p => p.z < -0.45f && p.y > 1.12f && Mathf.Abs(p.x) < 0.30f);

        CreatePersistentLampOverlay(root, lightSource, lightFilter.sharedMesh,
            "Reverse",
            p => p.z < -2.22f && p.y > 0.70f && p.y < 1.00f &&
                 Mathf.Abs(p.x) > 0.18f && Mathf.Abs(p.x) < 0.48f);
        CreatePersistentLampOverlay(root, lightSource, lightFilter.sharedMesh,
            "RearIndicatorLeft",
            p => p.z < -2.22f && p.y > 0.70f && p.y < 1.00f &&
                 p.x < -0.46f && p.x > -0.84f);
        CreatePersistentLampOverlay(root, lightSource, lightFilter.sharedMesh,
            "RearIndicatorRight",
            p => p.z < -2.22f && p.y > 0.70f && p.y < 1.00f &&
                 p.x > 0.46f && p.x < 0.84f);
    }

    private static void CreateRearLampHousing(
        GameObject root,
        MeshRenderer source,
        Mesh sourceMesh,
        bool left)
    {
        var found = false;
        var bounds = default(Bounds);
        foreach (var vertex in sourceMesh.vertices)
        {
            var point = root.transform.InverseTransformPoint(
                source.transform.TransformPoint(vertex));
            if (!IsMainRearLampFace(point))
                continue;
            if (left ? point.x >= 0f : point.x <= 0f)
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

        if (!found)
            throw new InvalidOperationException(
                $"Dodge rear lamp housing {(left ? "left" : "right")} selected no source vertices.");

        // Slightly inside the car (+Z from the rear surface). The plate is a
        // solid rectangle, intentionally filling the open holes in the imported
        // lens assembly that exposed the cabin.
        var z = bounds.max.z + 0.018f;
        var insetX = Mathf.Min(0.015f, bounds.size.x * 0.03f);
        var insetY = Mathf.Min(0.015f, bounds.size.y * 0.04f);
        var minX = bounds.min.x + insetX;
        var maxX = bounds.max.x - insetX;
        var minY = bounds.min.y + insetY;
        var maxY = bounds.max.y - insetY;

        var mesh = new Mesh
        {
            name = left ? "DodgeLamp_RearHousingLeft" : "DodgeLamp_RearHousingRight",
            vertices = new[]
            {
                new Vector3(minX, minY, z),
                new Vector3(maxX, minY, z),
                new Vector3(minX, maxY, z),
                new Vector3(maxX, maxY, z),
            },
            uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            }
        };
        mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        EnsureAssetFolder(MeshFolder);
        var assetPath = $"{MeshFolder}/{mesh.name}.asset";
        var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (persistent == null)
        {
            AssetDatabase.CreateAsset(mesh, assetPath);
            persistent = mesh;
        }
        else
        {
            EditorUtility.CopySerialized(mesh, persistent);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(persistent);
        }

        var existing = FindTransform(root.transform, persistent.name);
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing.gameObject);

        var host = new GameObject(persistent.name);
        host.transform.SetParent(root.transform, false);
        host.layer = source.gameObject.layer;
        host.AddComponent<MeshFilter>().sharedMesh = persistent;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = source.sharedMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = true;

        Debug.Log(
            $"DodgeChallenger2018: generated opaque rear housing '{persistent.name}' " +
            $"bounds={bounds.size} z={z:F3}.");
    }

    private static bool IsMainRearLampFace(Vector3 p)
    {
        // The imported GlassRed mesh also contains the two low bumper reflectors
        // and the narrow side reflectors. Only the upper inboard lamp assemblies
        // should emit tail/brake light.
        return p.z < -2.05f &&
               p.y > 0.60f && p.y < 1.08f &&
               Mathf.Abs(p.x) < 0.83f;
    }

    private static void SplitCrashDetailMeshes(GameObject model)
    {
        var candidates = new List<MeshRenderer>();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            var name = renderer.name;
            if (name.IndexOf(":Badge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf(":GlassRed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                (name.IndexOf(":Glass_", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 name.IndexOf(":Window", StringComparison.OrdinalIgnoreCase) < 0) ||
                name.IndexOf(":Textured", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                candidates.Add(renderer);
            }
        }

        var created = 0;
        foreach (var renderer in candidates)
            created += SplitRendererIntoConnectedComponents(renderer);

        Debug.Log(
            $"DodgeChallenger2018: split crash-following trim into {created} " +
            "connected component mesh object(s).");
    }

    private readonly struct CrashTriangle
    {
        internal CrashTriangle(int subMesh, int a, int b, int c)
        {
            SubMesh = subMesh;
            A = a;
            B = b;
            C = c;
        }

        internal readonly int SubMesh;
        internal readonly int A;
        internal readonly int B;
        internal readonly int C;
    }

    private static int FindDisjointRoot(int[] parents, int value)
    {
        var root = value;
        while (parents[root] != root)
            root = parents[root];

        while (parents[value] != value)
        {
            var next = parents[value];
            parents[value] = root;
            value = next;
        }

        return root;
    }

    private static void UnionDisjoint(int[] parents, byte[] ranks, int left, int right)
    {
        var a = FindDisjointRoot(parents, left);
        var b = FindDisjointRoot(parents, right);
        if (a == b)
            return;

        if (ranks[a] < ranks[b])
            parents[a] = b;
        else if (ranks[a] > ranks[b])
            parents[b] = a;
        else
        {
            parents[b] = a;
            ranks[a]++;
        }
    }

    private static int SplitRendererIntoConnectedComponents(MeshRenderer sourceRenderer)
    {
        var sourceFilter = sourceRenderer.GetComponent<MeshFilter>();
        var sourceMesh = sourceFilter != null ? sourceFilter.sharedMesh : null;
        if (sourceFilter == null || sourceMesh == null || sourceMesh.vertexCount == 0)
            return 0;

        var parent = sourceRenderer.transform;
        var vertexCount = sourceMesh.vertexCount;
        var disjoint = new int[vertexCount];
        var rank = new byte[vertexCount];
        for (var i = 0; i < disjoint.Length; i++)
            disjoint[i] = i;

        var trianglesBySubmesh = new int[sourceMesh.subMeshCount][];
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var triangles = sourceMesh.GetTriangles(subMesh);
            trianglesBySubmesh[subMesh] = triangles;
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                UnionDisjoint(disjoint, rank, triangles[i], triangles[i + 1]);
                UnionDisjoint(disjoint, rank, triangles[i], triangles[i + 2]);
            }
        }

        var groups = new Dictionary<int, List<CrashTriangle>>();
        for (var subMesh = 0; subMesh < trianglesBySubmesh.Length; subMesh++)
        {
            var triangles = trianglesBySubmesh[subMesh];
            for (var i = 0; i + 2 < triangles.Length; i += 3)
            {
                var a = triangles[i];
                var root = FindDisjointRoot(disjoint, a);
                List<CrashTriangle> group;
                if (!groups.TryGetValue(root, out group))
                {
                    group = new List<CrashTriangle>();
                    groups.Add(root, group);
                }

                group.Add(new CrashTriangle(
                    subMesh,
                    a,
                    triangles[i + 1],
                    triangles[i + 2]));
            }
        }

        if (groups.Count <= 1)
            return 0;

        var sourceVertices = sourceMesh.vertices;
        var sourceNormals = sourceMesh.normals;
        var sourceTangents = sourceMesh.tangents;
        var sourceColors = sourceMesh.colors32;
        var sourceUv = sourceMesh.uv;
        var sourceUv2 = sourceMesh.uv2;
        var sourceUv3 = sourceMesh.uv3;
        var sourceUv4 = sourceMesh.uv4;
        var componentIndex = 0;

        foreach (var group in groups.Values)
        {
            var oldIndices = new HashSet<int>();
            foreach (var triangle in group)
            {
                oldIndices.Add(triangle.A);
                oldIndices.Add(triangle.B);
                oldIndices.Add(triangle.C);
            }

            var ordered = oldIndices.ToArray();
            Array.Sort(ordered);
            var remap = new Dictionary<int, int>(ordered.Length);
            for (var i = 0; i < ordered.Length; i++)
                remap[ordered[i]] = i;

            var mesh = new Mesh();
            mesh.name =
                $"DodgeCrashDetail_{SanitizeAssetName(sourceRenderer.name)}_{componentIndex:D2}";
            mesh.indexFormat = ordered.Length > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            var vertices = new Vector3[ordered.Length];
            for (var i = 0; i < ordered.Length; i++)
                vertices[i] = sourceVertices[ordered[i]];
            mesh.vertices = vertices;

            if (sourceNormals != null && sourceNormals.Length == sourceVertices.Length)
            {
                var normals = new Vector3[ordered.Length];
                for (var i = 0; i < ordered.Length; i++)
                    normals[i] = sourceNormals[ordered[i]];
                mesh.normals = normals;
            }

            if (sourceTangents != null && sourceTangents.Length == sourceVertices.Length)
            {
                var tangents = new Vector4[ordered.Length];
                for (var i = 0; i < ordered.Length; i++)
                    tangents[i] = sourceTangents[ordered[i]];
                mesh.tangents = tangents;
            }

            if (sourceColors != null && sourceColors.Length == sourceVertices.Length)
            {
                var colors = new Color32[ordered.Length];
                for (var i = 0; i < ordered.Length; i++)
                    colors[i] = sourceColors[ordered[i]];
                mesh.colors32 = colors;
            }

            CopyUvSubset(mesh, 0, sourceUv, ordered);
            CopyUvSubset(mesh, 1, sourceUv2, ordered);
            CopyUvSubset(mesh, 2, sourceUv3, ordered);
            CopyUvSubset(mesh, 3, sourceUv4, ordered);

            mesh.subMeshCount = sourceMesh.subMeshCount;
            for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
            {
                var triangles = new List<int>();
                foreach (var triangle in group)
                {
                    if (triangle.SubMesh != subMesh)
                        continue;
                    triangles.Add(remap[triangle.A]);
                    triangles.Add(remap[triangle.B]);
                    triangles.Add(remap[triangle.C]);
                }
                mesh.SetTriangles(triangles, subMesh, false);
            }

            mesh.RecalculateBounds();
            if (mesh.normals == null || mesh.normals.Length == 0)
                mesh.RecalculateNormals();

            EnsureAssetFolder(MeshFolder);
            var assetPath = $"{MeshFolder}/{mesh.name}.asset";
            var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (persistent == null)
            {
                AssetDatabase.CreateAsset(mesh, assetPath);
                persistent = mesh;
            }
            else
            {
                EditorUtility.CopySerialized(mesh, persistent);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(persistent);
            }

            var child = new GameObject(
                $"DodgeCrashDetail_{SanitizeAssetName(sourceRenderer.name)}_{componentIndex:D2}");
            child.layer = sourceRenderer.gameObject.layer;
            child.transform.SetParent(parent, false);
            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            child.AddComponent<MeshFilter>().sharedMesh = persistent;

            var childRenderer = child.AddComponent<MeshRenderer>();
            childRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            childRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            childRenderer.receiveShadows = sourceRenderer.receiveShadows;
            childRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
            childRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
            childRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            componentIndex++;
        }

        sourceRenderer.enabled = false;
        sourceRenderer.sharedMaterials = Array.Empty<Material>();
        sourceFilter.sharedMesh = null;
        return componentIndex;
    }

    private static void CopyUvSubset(
        Mesh mesh,
        int channel,
        Vector2[] source,
        int[] ordered)
    {
        if (source == null || source.Length == 0)
            return;
        var values = new List<Vector2>(ordered.Length);
        foreach (var index in ordered)
            values.Add(index < source.Length ? source[index] : Vector2.zero);
        mesh.SetUVs(channel, values);
    }

    private static void CreatePersistentLampOverlay(
        GameObject root,
        MeshRenderer source,
        Mesh sourceMesh,
        string suffix,
        Func<Vector3, bool> includeTriangleCenter)
    {
        var triangles = new List<int>();
        var vertices = sourceMesh.vertices;
        for (var subMesh = 0; subMesh < sourceMesh.subMeshCount; subMesh++)
        {
            var sourceTriangles = sourceMesh.GetTriangles(subMesh);
            for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
            {
                var a = sourceTriangles[index];
                var b = sourceTriangles[index + 1];
                var d = sourceTriangles[index + 2];
                var localCenter = (vertices[a] + vertices[b] + vertices[d]) / 3f;
                var rootCenter = root.transform.InverseTransformPoint(
                    source.transform.TransformPoint(localCenter));
                if (!includeTriangleCenter(rootCenter))
                    continue;
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(d);
            }
        }

        if (triangles.Count == 0)
            throw new InvalidOperationException($"Dodge lamp overlay '{suffix}' selected no triangles.");

        var generated = new Mesh
        {
            name = "DodgeLamp_" + suffix,
            indexFormat = sourceMesh.indexFormat,
            vertices = sourceMesh.vertices,
            normals = sourceMesh.normals,
            tangents = sourceMesh.tangents,
            colors32 = sourceMesh.colors32,
            uv = sourceMesh.uv,
            uv2 = sourceMesh.uv2
        };
        generated.SetTriangles(triangles, 0, true);
        generated.RecalculateBounds();
        generated.UploadMeshData(false);

        EnsureAssetFolder(MeshFolder);
        var assetPath = $"{MeshFolder}/DodgeLamp_{suffix}.asset";
        var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (persistent == null)
        {
            AssetDatabase.CreateAsset(generated, assetPath);
            persistent = generated;
        }
        else
        {
            EditorUtility.CopySerialized(generated, persistent);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(persistent);
        }

        var existing = FindTransform(root.transform, "DodgeLamp_" + suffix);
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing.gameObject);

        var host = new GameObject("DodgeLamp_" + suffix);
        host.transform.SetParent(source.transform, false);
        // Slightly lift the emissive copy off the passive lens geometry to avoid
        // coplanar Z-fighting without visibly changing the lamp shape.
        host.transform.localScale = Vector3.one * 1.003f;
        host.layer = source.gameObject.layer;
        host.AddComponent<MeshFilter>().sharedMesh = persistent;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = source.sharedMaterial;
        renderer.renderingLayerMask = source.renderingLayerMask;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.enabled = false;

        Debug.Log(
            $"DodgeChallenger2018: generated lamp overlay '{suffix}' " +
            $"triangles={triangles.Count / 3}, bounds={persistent.bounds.size}.");
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
            if (HasMaterialMarker(renderer, "paint1Mtl1"))
            {
                sourceRenderer = renderer;
                break;
            }
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The Dodge paint shell was not found.");

        EnsureAssetFolder(MeshFolder);
        var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        bakedMesh.name = "DodgeDamageBody";
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

        var damageBody = new GameObject("DodgeDamageBody") { layer = sourceRenderer.gameObject.layer };
        damageBody.transform.SetParent(root.transform, false);
        var damageFilter = damageBody.AddComponent<MeshFilter>();
        damageFilter.sharedMesh = persistentMesh;
        var damageRenderer = damageBody.AddComponent<MeshRenderer>();
        damageRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        damageRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        damageRenderer.receiveShadows = sourceRenderer.receiveShadows;
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
        material.name.IndexOf("paint1Mtl1", StringComparison.OrdinalIgnoreCase) >= 0;

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
        material.name.IndexOf("Wheel2Mtl1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!DodgeChallenger2018Materials.IsDodgeRenderer(renderer.transform))
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
            throw new InvalidOperationException("The shared Dodge rim material is missing.");

        ApplyRimFinish(leftMaterial, DodgeChallenger2018Materials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!DodgeChallenger2018Materials.IsDodgeRenderer(renderer.transform))
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
                $"Expected four Dodge rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", DodgeChallenger2018Materials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", DodgeChallenger2018Materials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("calip", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool DemonPowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateDemonPowerCurve();
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
                    var transparent = DodgeChallenger2018Materials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"Dodge{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

                    var sourceBaseMap = source.HasProperty("baseColorTexture")
                        ? source.GetTexture("baseColorTexture")
                        : source.HasProperty("_BaseColorMap")
                            ? source.GetTexture("_BaseColorMap")
                            : source.HasProperty("_MainTex")
                                ? source.GetTexture("_MainTex")
                                : null;
                    var sourceNormalMap = source.HasProperty("normalTexture")
                        ? source.GetTexture("normalTexture")
                        : source.HasProperty("_NormalMap")
                            ? source.GetTexture("_NormalMap")
                            : null;
                    var sourceColor = source.HasProperty("baseColorFactor")
                        ? source.GetColor("baseColorFactor")
                        : source.HasProperty("_BaseColor")
                            ? source.GetColor("_BaseColor")
                            : Color.white;

                    DodgeChallenger2018Materials.NormalizeImportedMaterial(persistent);

                    // Preserve authored PBR inputs only for opaque materials.
                    // Transparent panes/lenses deliberately use deterministic
                    // HDRP/Unlit tints; restoring the source glTF base texture
                    // here made the cabin glass opaque black again.
                    var preserveAuthoredPbr =
                        !transparent &&
                        source.name.IndexOf("TexturedMtl1", StringComparison.OrdinalIgnoreCase) < 0;
                    if (preserveAuthoredPbr)
                    {
                        if (sourceBaseMap != null && persistent.HasProperty("_BaseColorMap"))
                            persistent.SetTexture("_BaseColorMap", sourceBaseMap);
                        if (sourceNormalMap != null && persistent.HasProperty("_NormalMap"))
                            persistent.SetTexture("_NormalMap", sourceNormalMap);
                        if (persistent.HasProperty("_BaseColor"))
                        {
                            sourceColor.a = 1f;
                            persistent.SetColor("_BaseColor", sourceColor);
                        }
                    }

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

        manifest.ModId = "Dodge_Challenger_2018";
        manifest.DisplayName = "2018 Dodge Challenger";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "dodgechallenger2018.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = (ModTargetPlatforms)3;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Dodge manifest references could not be assigned.");
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
            if (renderer == null) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
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

    private static bool TryGetDodgeRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!DodgeChallenger2018Materials.IsDodgeRenderer(renderer.transform))
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

