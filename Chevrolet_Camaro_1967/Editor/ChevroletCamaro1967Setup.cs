#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class ChevroletCamaro1967Setup
{
    private const string ModRoot = "Assets/Mods/Chevrolet_Camaro_1967";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/1967_chevy_camaro_ss_hidden_jewel.glb";
    private const string LightOverlayModelPath = ModRoot + "/Models/CamaroLightOverlays.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/CamaroDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/ChevroletCamaro1967.asset";
    private const string VehiclePrefabPath = ModRoot + "/ChevroletCamaro1967.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/ChevroletCamaro1967.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/chevroletcamaro1967.unity3d";
    private const string VehicleTypeName =
        "chevroletcamaro1967-vehicle:vehicletype_chevroletcamaro1967";
    private const float TargetLength = 4.691f;
    private const float TargetWidth = 1.842f;
    private const float TargetHeight = 1.295f;
    private const float FrontTrack = 1.499f;
    private const float RearTrack = 1.496f;
    private const float Wheelbase = 2.743f;
    private const float FrontTireRadius = 0.320f;
    private const float RearTireRadius = 0.320f;
    private const float FrontTireWidth = 0.236f;
    private const float RearTireWidth = 0.236f;
    private const float WheelInset = 0.000f;
    private const float WheelCenterRideHeightOffset = -0.135f;
    private const float VehicleLinearDrag = 0.045f;
    private const float PhysicsEnginePowerKw = 140f;
    private const float EngineLossPercent = 0.32f;
    private const float VehicleBrakeForce = 1060f;
    private const float BrakeMaxTorque = 1060f;
    private const float FrontForwardGrip = 0.72f;
    private const float RearForwardGrip = 0.30f;
    private const float FrontForwardStiffness = 0.96f;
    private const float RearForwardStiffness = 0.82f;
    private const float TireFrictionCircleStrength = 0.78f;
    private const float AntiRollBarForce = 6800f;
    private const float FrontSuspensionTravel = 0.105f;
    private const float RearSuspensionTravel = 0.115f;
    private const float DeformationStrength = 0.22f;
    private const float DeformationRadius = 0.28f;
    private const float DeformationRandomness = 0.005f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.15f, -0.06f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f + WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, Wheelbase * 0.5f + 0.02f) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f - WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, Wheelbase * 0.5f + 0.02f) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f + WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f + 0.05f) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f - WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f + 0.05f) },
        };
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.43f, 1.86f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.72f, 0.46f, 0.92f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.43f, -1.86f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.72f, 0.46f, 0.92f);

    private static readonly float[] CamaroM22Gears =
    {
        -2.26f, 0f, 2.20f, 1.64f, 1.28f, 1.00f,
    };

    private static AnimationCurve CreateCamaroPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.13f, 0.09f),
            new Keyframe(0.27f, 0.29f),
            new Keyframe(0.45f, 0.58f),
            new Keyframe(0.57f, 0.785f),
            new Keyframe(0.71f, 0.90f),
            new Keyframe(0.86f, 1.00f),
            new Keyframe(0.96f, 0.93f),
            new Keyframe(1.00f, 0.87f));

    [MenuItem("Big Ambitions Mods/Setup 1967 Chevrolet Camaro")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "ChevroletCamaro1967 setup complete: generated the supplied 1967 Camaro RS/SS " +
            "with four independent wheel visuals, M22 four-speed RWD driveline, small-block " +
            "V8 tuning, source-derived lighting, repaint support and damage geometry.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    public static void RepairAppearanceAndBuild()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var source = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<Material>().Single(material => material.name == "badges");
        var target = AssetDatabase.LoadAssetAtPath<Material>(
            MaterialFolder + "/CamaroTransparent_00_badges.mat");
        if (target == null)
            throw new InvalidOperationException("Camaro badge material is missing.");
        // Recover the original glTF texture before converting old Unlit materials.
        target.shader = source.shader;
        target.CopyPropertiesFromMaterial(source);
        ChevroletCamaro1967Materials.NormalizeImportedMaterial(target);
        RestoreGrilleOpenings(target);
        var root = PrefabUtility.LoadPrefabContents(VehiclePrefabPath);
        try
        {
            RepairGrilleGeometry(root);
            PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        if (target.GetTexture("_BaseColorMap") == null ||
            target.GetFloat("_AlphaCutoffEnable") != 1f ||
            target.GetColor("_BaseColor") != Color.white)
            throw new InvalidOperationException("Camaro badge texture/cutout validation failed.");
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();
        Debug.Log("Camaro appearance: original badge/grille atlas restored with white tint and alpha cutout.");
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
    }

    private static void RepairGrilleGeometry(GameObject target)
    {
        ChevroletCamaro1967GrilleGeometry.Repair(target,
            AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath),
            AssetDatabase.LoadAssetAtPath<Texture2D>(MaterialFolder + "/CamaroBadgeAtlasOpenGrille.asset"),
            MeshFolder);
    }

    private static void RestoreGrilleOpenings(Material material)
    {
        // Only the bottom atlas strip belongs to the grille. Its authored holes
        // have alpha 153/255, which becomes opaque under the badge cutout shader.
        // Preserve all RGB and all emblem pixels; remove only that residual alpha.
        var source = material.GetTexture("_BaseColorMap");
        if (source == null)
            throw new InvalidOperationException("Camaro badge atlas is missing.");
        var temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        var previous = RenderTexture.active;
        var corrected = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, false)
        {
            name = "CamaroBadgeAtlasOpenGrille",
            filterMode = source.filterMode,
            wrapMode = source.wrapMode,
            anisoLevel = source.anisoLevel,
        };
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            corrected.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
        var pixels = corrected.GetPixels32();
        var holes = 0;
        var bars = 0;
        for (var y = 0; y < corrected.height; y++)
        {
            // Original 512px atlas: grille starts at row 376 from the top.
            if ((1f - (y + 0.5f) / corrected.height) * 512f < 376f)
                continue;
            for (var x = 0; x < corrected.width; x++)
            {
                var index = y * corrected.width + x;
                if (pixels[index].a <= 154)
                {
                    pixels[index].a = 0;
                    holes++;
                }
                else bars++;
            }
        }
        if (holes == 0 || bars == 0)
            throw new InvalidOperationException("Camaro grille atlas has no holes or bars.");
        corrected.SetPixels32(pixels);
        corrected.Apply(true, false);
        var path = MaterialFolder + "/CamaroBadgeAtlasOpenGrille.asset";
        var persistent = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (persistent == null)
        {
            AssetDatabase.CreateAsset(corrected, path);
            persistent = corrected;
        }
        else
        {
            EditorUtility.CopySerialized(corrected, persistent);
            UnityEngine.Object.DestroyImmediate(corrected);
        }
        material.SetTexture("_BaseColorMap", persistent);
        EditorUtility.SetDirty(persistent);
        Debug.Log($"Camaro grille atlas: opened {holes} pixels, retained {bars} bar pixels; emblems unchanged.");
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
                throw new InvalidOperationException("Bundle is missing ChevroletCamaro1967.asset or ChevroletCamaro1967.prefab.");

            var vehicleSerialized = new SerializedObject(vehicleType);
            var price = ReadNumber(vehicleSerialized.FindProperty("price"));
            var maxFuel = ReadNumber(vehicleSerialized.FindProperty("maxFuel"));
            var maxCargo = ReadNumber(vehicleSerialized.FindProperty("maxCargoCapacity"));
            var maxSpeed = ReadNumber(vehicleSerialized.FindProperty("maxSpeed"));
            var enginePower = ReadNumber(vehicleSerialized.FindProperty("enginePower"));
            var luxury = vehicleSerialized.FindProperty("isLuxuryCar")?.boolValue ?? true;
            var autoPark = vehicleSerialized.FindProperty("autoParkSupported")?.boolValue ?? true;
            var fitsHandTruck = vehicleSerialized.FindProperty("fitsHandTruck")?.boolValue ?? true;
            var fitsFlatbed = vehicleSerialized.FindProperty("fitsFlatbed")?.boolValue ?? true;
            if (Math.Abs(price - 54900f) > 1f || Math.Abs(maxFuel - 70f) > 0.1f ||
                Math.Abs(maxCargo - 5f) > 0.1f || Math.Abs(maxSpeed - 190f) > 0.1f ||
                Math.Abs(enginePower - 220f) > 0.1f || luxury || autoPark ||
                fitsHandTruck || fitsFlatbed)
                throw new InvalidOperationException(
                    $"Camaro VehicleType mismatch price={price}, fuel={maxFuel}, cargo={maxCargo}, " +
                    $"speed={maxSpeed}, power={enginePower}, luxury={luxury}, autoPark={autoPark}, " +
                    $"fitsHandTruck={fitsHandTruck}, fitsFlatbed={fitsFlatbed}.");

            var body = prefab.GetComponent<Rigidbody>() ??
                       throw new InvalidOperationException("Camaro prefab has no Rigidbody.");
            if (Math.Abs(body.mass - 1483f) > 1f ||
                Vector3.Distance(body.centerOfMass, StableCenterOfMass) > 0.01f)
                throw new InvalidOperationException($"Camaro Rigidbody mismatch mass={body.mass}, center={body.centerOfMass}.");

            var visual = FindTransform(prefab.transform, "CamaroVisual") ??
                         throw new InvalidOperationException("Camaro visual root is missing.");
            if (!TryGetModelBodyBounds(visual, out var bounds))
                throw new InvalidOperationException("Camaro visual bounds could not be measured.");
            if (Math.Abs(bounds.size.x - TargetWidth) > 0.10f ||
                Math.Abs(bounds.size.y - TargetHeight) > 0.10f ||
                Math.Abs(bounds.size.z - TargetLength) > 0.12f)
                throw new InvalidOperationException($"Camaro bounds mismatch: {bounds.size}.");

            foreach (var pair in WheelControllerPositions)
            {
                var controller = FindTransform(prefab.transform, pair.Key) ??
                                 throw new InvalidOperationException($"Missing wheel controller '{pair.Key}'.");
                if (Vector3.Distance(controller.localPosition, pair.Value) > 0.02f)
                    throw new InvalidOperationException($"Wheel controller '{pair.Key}' is misaligned.");
                var corner = pair.Key.Replace("_WheelController", string.Empty);
                if (FindTransform(prefab.transform, "CamaroWheel" + corner) == null ||
                    FindTransform(prefab.transform, "CamaroFixedCaliper" + corner) == null)
                    throw new InvalidOperationException($"Missing Camaro wheel/caliper visual for '{corner}'.");
            }

            var renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            foreach (var marker in new[] {
                         "HeadlampFL", "HeadlampFR", "IndicatorFL", "IndicatorFR",
                         "ReverseLight", "RearDrivingLight_BrakeLightInner",
                         "IndicatorRL", "IndicatorRR" })
                if (FindRendererByHierarchyMarker(renderers, marker) == null)
                    throw new InvalidOperationException($"Camaro authored lighting mesh '{marker}' is missing.");

            Debug.Log($"ChevroletCamaro1967 bundle verified: price={price}, fuel={maxFuel}, cargo={maxCargo}, " +
                      $"speed={maxSpeed}, power={enginePower}, mass={body.mass}, bounds={bounds.size}, " +
                      "M22=4-speed/4.11, RWD=true, wheels=4, authoredLighting=true.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static MeshRenderer? FindRendererByHierarchyMarker(
        IEnumerable<MeshRenderer> renderers,
        string marker)
    {
        foreach (var renderer in renderers)
            if (renderer != null && HasAncestorNameFragment(renderer.transform, marker))
                return renderer;
        return null;
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
                throw new InvalidOperationException("Could not create the Camaro VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated Camaro VehicleType asset did not load.");

        target.name = "ChevroletCamaro1967";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 54900f);
        SetNumber(serialized, "maxFuel", 70f);
        SetNumber(serialized, "maxCargoCapacity", 5f);
        SetNumber(serialized, "maxSpeed", 190f);
        SetNumber(serialized, "enginePower", 220f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", 37f);
        SetNumber(serialized, "damageIntensity", 0.30f);
        SetBool(serialized, "isATruck", false);
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
            throw new InvalidOperationException("Camaro VehicleType asset was not found.");

        var serialized = new SerializedObject(vehicleType);
        SetNumber(serialized, "maxCargoCapacity", 5f);
        SetNumber(serialized, "maxSpeed", 190f);
        SetNumber(serialized, "enginePower", 220f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetBool(serialized, "fitsHandTruck", false);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", false);
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
            throw new InvalidOperationException("1967 Camaro GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "ChevroletCamaro1967";
        try
        {
            StripAudiGeometry(root);
            RemoveAudiSpecificBehaviours(root);
            RemoveMissingScripts(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the Camaro model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "CamaroVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureExitMarkers(root, modelInstance);
            AssignPersistentMaterials(modelInstance);
            RepairGrilleGeometry(modelInstance);
            var persistentMaterialsNormalized = NormalizePersistentCamaroMaterials(root);
            AttachWheelVisuals(root, modelInstance);
            // Player wheel/physics ride height is correct. Lower only the visible
            // body/chassis another 2 cm; the wheel assemblies stay on their
            // calibrated controller datum at the vehicle root.
            modelInstance.transform.localPosition += Vector3.down * 0.03f;
            AttachLightOverlaySources(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);
            EnsureNoMissingScripts(root);

            Debug.Log(
                $"ChevroletCamaro1967: prepared persistent HDRP materials " +
                $"normalized={persistentMaterialsNormalized}, " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, " +
                $"runtime material helpers are not serialized into the prefab.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Camaro vehicle prefab.");
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

        Debug.Log($"ChevroletCamaro1967: removed {removed} missing donor script component(s).");
    }

    private static void EnsureNoMissingScripts(GameObject root)
    {
        var broken = new List<string>();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (transform == null)
                continue;
            var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            if (count > 0)
                broken.Add($"{transform.name}:{count}");
        }

        if (broken.Count > 0)
            throw new InvalidOperationException(
                "Generated Camaro prefab still contains missing script components: " +
                string.Join(", ", broken.GetRange(0, Math.Min(8, broken.Count))));
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
        body.mass = 1483f;
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
            SetNumber(serialized, "baseMass", 1483f);
            SetNumber(serialized, "combinedMass", 1483f);
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
                SetRelativeNumber(serialized, "spring.maxForce", 16500f);
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
            var mount = FindTransform(root.transform, "CamaroWheel" + corner) ??
                        throw new InvalidOperationException($"Rolling visual for '{corner}' is missing.");
            var caliper = FindTransform(root.transform, "CamaroFixedCaliper" + corner) ??
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
        foreach (var pair in WheelControllerPositions)
        {
            var corner = pair.Key.Replace("_WheelController", string.Empty);
            var controller = FindTransform(root.transform, pair.Key) ??
                             throw new InvalidOperationException($"Wheel controller '{pair.Key}' is missing.");
            var mount = FindTransform(root.transform, "CamaroWheel" + corner) ??
                        throw new InvalidOperationException($"Rolling visual for '{corner}' is missing.");
            controller.localPosition = pair.Value;
            mount.localPosition = pair.Value;
            AssignWheelVisual(controller, mount.gameObject);
        }
    }

    private static void AttachLightOverlaySources(GameObject root, GameObject modelInstance)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(LightOverlayModelPath);
        if (source == null)
            throw new InvalidOperationException(
                "Models/CamaroLightOverlays.glb is missing. Run the isolated build so the supplied Blender groups are exported first.");

        var overlay = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject ??
                      throw new InvalidOperationException("Could not instantiate Camaro light overlays.");
        PrefabUtility.UnpackPrefabInstance(
            overlay,
            PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction);

        overlay.name = "CamaroLightSources";

        // The Blender-authored overlay GLB uses Blender Z-up. Parent it below the
        // already-normalized Camaro visual and rotate the authored set 90 degrees
        // toward vehicle-forward, matching the proven Amarok authored-light path.
        overlay.transform.SetParent(modelInstance.transform, false);
        overlay.transform.localPosition = Vector3.zero;
        overlay.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        overlay.transform.localScale = Vector3.one;

        Debug.Log(
            $"ChevroletCamaro1967 authored-light transform: parent={modelInstance.name}, " +
            $"localRotation={overlay.transform.localEulerAngles}, localPosition={overlay.transform.localPosition}.");

        // Authored source geometry only. The runtime lighting controller clones
        // these exact meshes with the appropriate emissive materials.
        foreach (var renderer in overlay.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        var steeringWheel = FindTransformWithNameFragment(model.transform, "INT_STEERING_WHEEL") ??
                            throw new InvalidOperationException("Model steering wheel is missing.");
        var steeringPosition = root.transform.InverseTransformPoint(steeringWheel.position);
        var driverSide = steeringPosition.x < 0f ? -1.45f : 1.45f;
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.1f, 0f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.1f, 0f));
        Debug.Log(
            $"ChevroletCamaro1967: steering wheel x={steeringPosition.x:F3}; " +
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
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1125f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 550f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 700f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.28f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", PhysicsEnginePowerKw);
                SetRelativeNumber(serialized, "powertrain.engine.engineLossPercent", EngineLossPercent);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateCamaroPowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 750f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 5600f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.55f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 4.11f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 4f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.30f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 2800f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 4750f);
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
                gears.arraySize = CamaroM22Gears.Length;
                for (var index = 0; index < CamaroM22Gears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = CamaroM22Gears[index];
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
            throw new InvalidOperationException("Camaro model length could not be measured.");

        model.transform.localRotation = candidates[bestIndex];
        var front = FindTransformWithNameFragment(model.transform, "HEADLIGHT_LENS_LEFT");
        var rear = FindTransformWithNameFragment(model.transform, "TAILLIGHT_LENS_LEFT");
        if (front != null && rear != null &&
            TryGetRendererBounds(front, out var frontBounds) &&
            TryGetRendererBounds(rear, out var rearBounds) &&
            frontBounds.center.z < rearBounds.center.z)
        {
            model.transform.localRotation =
                Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
        }

        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Oriented Camaro body bounds could not be measured.");
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
            throw new InvalidOperationException("Scaled Camaro bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Camaro bounds could not be measured.");

        Debug.Log(
            $"ChevroletCamaro1967: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var tires = new List<Transform>();
        var wheels = new List<Transform>();
        var rotors = new List<Transform>();
        var calipers = new List<Transform>();

        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            var hierarchy = GetTransformPath(renderer.transform);
            if (hierarchy.IndexOf("LOD_A_TYRE", StringComparison.OrdinalIgnoreCase) >= 0)
                tires.Add(renderer.transform);
            else if (hierarchy.IndexOf("LOD_A_WHEEL", StringComparison.OrdinalIgnoreCase) >= 0)
                wheels.Add(renderer.transform);
            else if (hierarchy.IndexOf("LOD_A_ROTOR", StringComparison.OrdinalIgnoreCase) >= 0)
                rotors.Add(renderer.transform);
            else if (hierarchy.IndexOf("camaro_ss:LOD_A_BRAKE_CALIPER", StringComparison.OrdinalIgnoreCase) >= 0)
                calipers.Add(renderer.transform);
        }

        tires = UniqueRendererRoots(tires);
        wheels = UniqueRendererRoots(wheels);
        rotors = UniqueRendererRoots(rotors);
        calipers = UniqueRendererRoots(calipers);
        if (tires.Count != 4 || wheels.Count != 4 || rotors.Count != 4 || calipers.Count != 4)
            throw new InvalidOperationException(
                $"Expected four Camaro wheel parts; tires={tires.Count}, rims={wheels.Count}, rotors={rotors.Count}, calipers={calipers.Count}.");

        var averageZ = 0f;
        var tireCenters = new Dictionary<Transform, Vector3>();
        foreach (var tire in tires)
        {
            if (!TryGetRendererBounds(tire, out var bounds))
                throw new InvalidOperationException($"Tire '{tire.name}' has no measurable bounds.");
            var center = root.transform.InverseTransformPoint(bounds.center);
            tireCenters[tire] = center;
            averageZ += center.z;
        }
        averageZ /= tires.Count;

        var remainingWheels = new List<Transform>(wheels);
        var remainingRotors = new List<Transform>(rotors);
        var remainingCalipers = new List<Transform>(calipers);
        foreach (var tire in tires)
        {
            var authoredCenter = tireCenters[tire];
            var isFront = authoredCenter.z >= averageZ;
            var isLeft = authoredCenter.x < 0f;
            var corner = (isFront ? "Front" : "Rear") + (isLeft ? "Left" : "Right");
            var controllerName = corner + "_WheelController";
            var controller = FindTransform(root.transform, controllerName) ??
                             throw new InvalidOperationException($"Wheel controller '{controllerName}' is missing.");
            var targetCenter = WheelControllerPositions[controllerName];
            var radius = isFront ? FrontTireRadius : RearTireRadius;
            var width = isFront ? FrontTireWidth : RearTireWidth;
            if (!TryGetRendererBounds(tire, out var sourceBounds))
                throw new InvalidOperationException($"Tire '{tire.name}' has no renderer bounds.");

            var wheel = FindClosestPart(tire, remainingWheels) ??
                        throw new InvalidOperationException($"Tire '{tire.name}' has no matching rim.");
            var rotor = FindClosestPart(tire, remainingRotors) ??
                        throw new InvalidOperationException($"Tire '{tire.name}' has no matching rotor.");
            var caliper = FindClosestPart(tire, remainingCalipers) ??
                          throw new InvalidOperationException($"Tire '{tire.name}' has no matching caliper.");
            remainingWheels.Remove(wheel);
            remainingRotors.Remove(rotor);
            remainingCalipers.Remove(caliper);

            var mount = new GameObject("CamaroWheel" + corner);
            mount.transform.SetParent(root.transform, false);
            mount.transform.position = sourceBounds.center;
            tire.SetParent(mount.transform, true);
            wheel.SetParent(mount.transform, true);
            rotor.SetParent(mount.transform, true);
            tire.name = "Geometry_CamaroTire_" + (isFront ? "F" : "R") + (isLeft ? "L" : "R");
            wheel.name = "Geometry_CamaroWheel_" + (isFront ? "F" : "R") + (isLeft ? "L" : "R");
            rotor.name = "Geometry_CamaroRotor_" + (isFront ? "F" : "R") + (isLeft ? "L" : "R");
            mount.transform.localScale = new Vector3(
                width / Mathf.Max(0.001f, sourceBounds.size.x),
                (radius * 2f) / Mathf.Max(0.001f, sourceBounds.size.y),
                (radius * 2f) / Mathf.Max(0.001f, sourceBounds.size.z));
            mount.transform.localPosition = targetCenter;
            controller.localPosition = targetCenter;

            var fixedCaliper = new GameObject("CamaroFixedCaliper" + corner);
            fixedCaliper.transform.SetParent(root.transform, false);
            fixedCaliper.transform.position = caliper.position;
            caliper.SetParent(fixedCaliper.transform, true);
            fixedCaliper.transform.localPosition = targetCenter;
            fixedCaliper.transform.localScale = mount.transform.localScale;

            AssignWheelVisual(controller, mount);
        }
    }

    private static List<Transform> UniqueRendererRoots(List<Transform> candidates)
    {
        var result = new List<Transform>();
        var seen = new HashSet<Transform>();
        foreach (var candidate in candidates)
        {
            var root = candidate;
            while (root.parent != null &&
                   (root.parent.name.IndexOf("LOD_A_TYRE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    root.parent.name.IndexOf("LOD_A_WHEEL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    root.parent.name.IndexOf("LOD_A_ROTOR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    root.parent.name.IndexOf("BRAKE_CALIPER", StringComparison.OrdinalIgnoreCase) >= 0))
                root = root.parent;
            if (seen.Add(root))
                result.Add(root);
        }
        return result;
    }

    private static string GetTransformPath(Transform transform)
    {
        var parts = new List<string>();
        for (var current = transform; current != null; current = current.parent)
            parts.Add(current.name);
        parts.Reverse();
        return string.Join("/", parts);
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
            if (IsExteriorBodyRendererForPaint(renderer.transform))
                paintRenderers.Add(renderer);
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

    private static bool IsExteriorBodyRendererForPaint(Transform transform)
    {
        return HasAncestorNameFragment(transform, "LOD_A_BODY_mm_ext") ||
               HasAncestorNameFragment(transform, "LOD_A_BOOT_mm_ext") ||
               HasAncestorNameFragment(transform, "LOD_A_HOOD_mm_ext") ||
               HasAncestorNameFragment(transform, "LOD_A_DOOR_LEFT_mm_ext") ||
               HasAncestorNameFragment(transform, "LOD_A_DOOR_RIGHT_mm_ext") ||
               HasAncestorNameFragment(transform, "CamaroDamageBody");
    }

    private static MeshFilter CreateDeformableBody(GameObject root, GameObject modelInstance)
    {
        MeshRenderer? sourceRenderer = null;
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (HasAncestorNameFragment(renderer.transform, "LOD_A_BODY_mm_ext"))
            {
                sourceRenderer = renderer;
                break;
            }
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The Camaro LOD_A_BODY_mm_ext outer body mesh was not found.");

        var baked = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        baked.name = "CamaroDamageBody";
        EnsureAssetFolder(MeshFolder);
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(DamageBodyMeshPath);
        if (existing == null)
            AssetDatabase.CreateAsset(baked, DamageBodyMeshPath);
        else
        {
            EditorUtility.CopySerialized(baked, existing);
            UnityEngine.Object.DestroyImmediate(baked);
            baked = existing;
        }
        EditorUtility.SetDirty(baked);

        var damageBody = new GameObject("CamaroDamageBody");
        damageBody.transform.SetParent(root.transform, false);
        damageBody.transform.position = sourceRenderer.transform.position;
        damageBody.transform.rotation = sourceRenderer.transform.rotation;
        damageBody.transform.localScale = sourceRenderer.transform.lossyScale;
        damageBody.transform.SetParent(root.transform, true);
        var damageFilter = damageBody.AddComponent<MeshFilter>();
        damageFilter.sharedMesh = baked;
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
        string.Equals(material.name, "material", StringComparison.OrdinalIgnoreCase) ||
        material.name.IndexOf("Opaque_", StringComparison.OrdinalIgnoreCase) >= 0 &&
        material.name.EndsWith("_material", StringComparison.OrdinalIgnoreCase);

    private static bool IsCabinGlassRenderer(Transform transform) =>
        HasAncestorNameFragment(transform, "GLASS") ||
        HasAncestorNameFragment(transform, "windows") ||
        HasAncestorNameFragment(transform, "in_glasss");

    private static bool IsInteriorAccentPaintMaterial(Material material) => false;

    private static bool IsRimMaterial(Material material) =>
        material.name.IndexOf("material_11", StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!ChevroletCamaro1967Materials.IsCamaroRenderer(renderer.transform))
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
            throw new InvalidOperationException("The shared Camaro rim material is missing.");

        ApplyRimFinish(leftMaterial, ChevroletCamaro1967Materials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!ChevroletCamaro1967Materials.IsCamaroRenderer(renderer.transform))
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
                $"Expected four Camaro rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", ChevroletCamaro1967Materials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", ChevroletCamaro1967Materials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("misc", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static bool CamaroPowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateCamaroPowerCurve();
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

    private static bool IsSeatPaintMaterial(Material material) => false;

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

    private static bool IsInteriorPrimaryPaintMaterial(Material material) => false;

    private static bool IsInteriorSecondaryPaintMaterial(Material material) => false;

    private static bool IsInteriorDarkPaintMaterial(Material material) => false;

    private static int NormalizePersistentCamaroMaterials(GameObject model)
    {
        var normalized = 0;
        var materials = new HashSet<Material>();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || !materials.Add(material))
                    continue;
                ChevroletCamaro1967Materials.NormalizeImportedMaterial(material);
                EditorUtility.SetDirty(material);
                normalized++;
            }
        }

        return normalized;
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
                    var transparent = ChevroletCamaro1967Materials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"Camaro{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

                    ChevroletCamaro1967Materials.NormalizeImportedMaterial(persistent);
                    if (ChevroletCamaro1967Materials.IsBadgeMaterial(persistent))
                        RestoreGrilleOpenings(persistent);
                    EditorUtility.SetDirty(persistent);

                    if (string.Equals(source.name, "emiss", StringComparison.OrdinalIgnoreCase))
                    {
                        if (persistent.HasProperty("_EmissiveColor"))
                            persistent.SetColor("_EmissiveColor", Color.black);
                        if (persistent.HasProperty("_EmissionColor"))
                            persistent.SetColor("_EmissionColor", Color.black);
                        persistent.DisableKeyword("_EMISSION");
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

        manifest.ModId = "ChevroletCamaro1967";
        manifest.DisplayName = "1967 Chevrolet Camaro";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "chevroletcamaro1967.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = (ModTargetPlatforms)3;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Camaro manifest references could not be assigned.");
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
                if (current.name.IndexOf("LOD_A_TYRE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("LOD_A_WHEEL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("LOD_A_ROTOR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("BRAKE_CALIPER", StringComparison.OrdinalIgnoreCase) >= 0)
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

    private static bool TryGetCamaroRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!ChevroletCamaro1967Materials.IsCamaroRenderer(renderer.transform))
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

