#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Vehicles.VehicleTypes;
using NWH.VehiclePhysics2.Modules.Aerodynamics;

/// <summary>
/// Rebuilds the Aventador SVJ source prefab from the supplied GLB and native Audi
/// controller template. This is deterministic and safe to rerun: it replaces only
/// the two Aventador-owned output assets and keeps their existing GUIDs.
/// </summary>
public static class LamborghiniAventadorSetup
{
    private const string Root = "Assets/Mods/Lamborghini_Aventador";
    private const string ModelPath = Root + "/Models/lamborghini_aventador_svj.glb";
    private const string VehiclePath = Root + "/LamborghiniAventador.asset";
    private const string PrefabPath = Root + "/LamborghiniAventador.prefab";
    private const string ManifestPath = Root + "/ModManifest.asset";
    private const string AssemblyPath = Root + "/LamborghiniAventador.asmdef";
    private const string DependenciesPath = Root + "/Dependencies";
    private const string GleyLampMaterialPath = Root + "/Shaders/LamborghiniAventadorGleyLamp.mat";
    private const string InteriorAccentAtlasPath = Root + "/Generated/AventadorInteriorAccentAtlas.png";
    private const string InteriorAccentMaterialPath = Root + "/Generated/AventadorInteriorAccent.mat";
    private const string WarehousePlacementMeshPath = Root + "/Generated/AventadorWarehousePlacementBounds.asset";
    private const string NativeSpawnMeshPath = Root + "/Generated/AventadorNativeSpawnBounds.asset";
    internal const float WarehouseClearancePerEnd = 1.30f;
    private const string WheelMeshesPath = Root + "/Generated/LamborghiniAventadorWheelMeshes.asset";
    private const string GleyLampShaderName = "LamborghiniAventador/GleyTrafficLamp";
    private const string LocalesPath = Root + "/Locales";
    private const string WindowsBundlePath = Root + "/AssetBundles/Windows/lamborghiniaventador.unity3d";
    private const string MacBundlePath = Root + "/AssetBundles/Mac/lamborghiniaventador.unity3d";
    private const string DonorVehiclePath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string DonorPrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string VehicleTypeName = "lamborghiniaventador-vehicle:vehicletype_lamborghiniaventador";
    private const float Length = 4.943f;
    private const float WidthIncludingMirrors = 2.273f;
    private const float Height = 1.136f;
    private const float Wheelbase = 2.700f;
    private const float FrontTrack = 1.720f;
    private const float RearTrack = 1.680f;
    private const float FrontRadius = 0.3305f;
    private const float RearRadius = 0.35545f;
    private const float FrontWidth = 0.255f;
    private const float RearWidth = 0.355f;
    private const float WheelRearwardOffset = 0.10f;
    internal const float BodyRaising = 0.020f;
    internal const float BodyLowering = 0.04f - BodyRaising;
    internal const float WheelCenterLowering = 0.010f;
    private const float CaliperGeometryLowering = BodyRaising + WheelCenterLowering;
    private const float ExitSideOffset = 1.85f;
    private const float OperatingMass = 1770f;
    private const float FrontSpringTravel = 0.075f;
    private const float RearSpringTravel = 0.065f;
    private const float SpringMaximumForce = 21500f;
    private static readonly Vector3 VehicleCenterOfMass = new Vector3(0f, 0.10f, -0.08f);
    private const float EnginePower = 566f;
    private const float EnginePeakTorqueNm = 720f;
    private const float EnginePeakTorqueRPM = 6750f;
    private const float EnginePeakPowerRPM = 8500f;
    private const float TorqueToPowerConversion = 9549.2965855f;
    private const float EngineIdleRPM = 1100f;
    private const float EngineRevLimiterRPM = 8700f;
    private const float ClutchEngagementRPM = 1300f;
    private const float ClutchThrottleEngagementOffsetRPM = 250f;
    private const float ClutchEngagementRangeRPM = 500f;
    // At full output the engine-loss compensation and AWD modifier expose 770.4 Nm to the clutch.
    // 1.10x gives 792 Nm capacity, a 2.8% margin above that compensated torque.
    internal const float ClutchTorqueSafetyFactor = 1.10f;
    internal const float AutomaticUpshiftRpm = 8000f;
    // The first-to-second ratio step lands at 4,989 rpm: keep the native
    // ground-speed-based downshift threshold below that landing speed.
    internal const float AutomaticDownshiftRpm = 4200f;
    private const float AerodynamicDragCoefficient = 0.36f;
    // The latest CSV records 30.2 m from 103.6 km/h to rest; interpolation
    // estimates about 28.5 m from 100 km/h, close to Lamborghini's 30 m target.
    // Instantaneous g is unreliable during catch-up FixedUpdate steps. These
    // values remain provisional and mass-scaled from the in-repo Revuelto and R8.
    private const float VehicleBrakeForce = 2600f;
    private const float BrakeMaxTorque = 2600f;
    private static readonly float[] Gears = { -2.929f, 0f, 3.909f, 2.438f, 1.810f, 1.458f, 1.185f, 0.967f, 0.844f };

    private sealed class WheelSource
    {
        internal string Label = "";
        internal string MountName = "";
        internal string ControllerName = "";
        internal bool Front;
        internal bool Left;
        internal Transform Group = null!;
        internal Bounds Bounds;
        internal Vector3 Target;
    }

    private enum WheelSurface { Rim, Rubber }

    private const float TireAtlasOuterRadiusFraction = 0.80f;

    [MenuItem("Big Ambitions Mods/Setup Lamborghini Aventador SVJ")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var donor = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPrefabPath);
        var donorType = AssetDatabase.LoadAssetAtPath<VehicleType>(DonorVehiclePath);
        if (model == null || donor == null || donorType == null)
            throw new InvalidOperationException("Aventador setup needs the imported GLB and Audi RS6R native template assets.");

        EnsureGleyLampMaterial();

        var vehicleType = CreateVehicleType(donorType);
        var root = PrefabUtility.InstantiatePrefab(donor) as GameObject;
        if (root == null) throw new InvalidOperationException("Could not instantiate the native Audi vehicle prefab.");
        root.name = "LamborghiniAventador";
        try
        {
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            StripDonorGeometry(root);
            RemoveDonorBehaviours(root);
            ConfigureBodyCollider(root);
            ConfigureRootPhysics(root);
            ConfigureNativeReferences(root, vehicleType);

            var body = new GameObject("AventadorBody");
            body.transform.SetParent(root.transform, false);
            var modelObject = PrefabUtility.InstantiatePrefab(model, body.transform) as GameObject;
            if (modelObject == null) throw new InvalidOperationException("Could not instantiate the imported Aventador GLB.");
            PrefabUtility.UnpackPrefabInstance(modelObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            modelObject.name = "ImportedAventadorSVJ";

            NormalizeModel(modelObject.transform);
            ConfigureInteriorAccentMaterial(modelObject);
            body.transform.localPosition = Vector3.down * BodyLowering;
            LamborghiniAventadorBlenderLights.Configure(root);
            var wheels = MeasureWheelSources(modelObject.transform);
            MoveWheelVisuals(root.transform, wheels);
            MoveCalipers(root.transform, modelObject.transform, wheels);
            CombineWheelMeshes(wheels);
            ConfigureWheelControllers(root.transform, wheels);
            ConfigurePowertrain(root);
            ConfigureAnchors(root.transform, wheels);
            ConfigureBoxColliders(root.transform);
            ConfigureWarehousePlacement(root);
            ConfigureNavMeshObstacles(root);
            LamborghiniAventadorWashSetup.Author(root);
            EnsureSpawnAppearance(root);

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (saved == null) throw new InvalidOperationException("Could not save the generated Aventador prefab.");
            EditorUtility.SetDirty(vehicleType);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            CreateManifest();
            AssetDatabase.SaveAssets();
            LamborghiniAventadorAssetChecks.Verify();
            Debug.Log("LamborghiniAventador setup complete. Source-scaled body and four measured wheel assemblies saved; inspect bounds, wheel contact, caliper ownership, and driver anchors before tuning or bundling.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static void GenerateAndBuild()
    {
        Generate();
        LamborghiniAventadorAssetChecks.VerifyAutoParkingGeometry();
        LamborghiniAventadorAssetChecks.VerifyPaintRefreshIdempotence();
        LamborghiniAventadorCaliperRegression.VerifySteeringAndSpinIndependence();
        LamborghiniAventadorTeleportSuspensionRegression.VerifyTeleportSuspensionReset();
        LamborghiniAventadorSuspensionGeometryRegression.VerifySuspensionGeometry();
        LamborghiniAventadorDamageRegression.VerifyDamageOwnershipAndRegions();
        LamborghiniAventadorNativeSpringFixture.VerifyNativeSpringRestFixture();
        LamborghiniAventadorShiftHysteresisRegression.VerifyShiftHysteresis();
        LamborghiniAventadorTrafficInitializationRegression.VerifyPreAllocationRegistration();
        ModAssetBundleCli.BuildForMod();
        VerifyPlatformBundles();
        LamborghiniAventadorAssetChecks.CompareExistingWheelBundleToSource();
    }

    private static void ConfigureInteriorAccentMaterial(GameObject model)
    {
        // This byte-identical copy of the source InteriorA color atlas is
        // independently readable for tinting, while other GLB textures stay GPU-only.
        var importer = AssetImporter.GetAtPath(InteriorAccentAtlasPath) as TextureImporter ??
            throw new InvalidOperationException("Aventador interior accent atlas was not imported.");
        if (!importer.isReadable || importer.textureCompression != TextureImporterCompression.Uncompressed ||
            importer.wrapMode != TextureWrapMode.Repeat || importer.alphaIsTransparency)
        {
            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(InteriorAccentAtlasPath);
        var renderer = model.GetComponentsInChildren<MeshRenderer>(true).Single(candidate =>
            candidate.name == "Object_9" && candidate.transform.parent?.parent?.name == "Interior_Geo_lodA_4");
        var source = renderer.sharedMaterial;
        if (atlas == null || atlas.width != 512 || atlas.height != 512 ||
            source == null || !source.HasProperty("baseColorTexture"))
            throw new InvalidOperationException("Aventador interior accent atlas or GLB InteriorA material is invalid.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(InteriorAccentMaterialPath);
        var create = material == null;
        if (material == null)
            material = new Material(source);
        else
            material.CopyPropertiesFromMaterial(source);
        material.name = "AventadorInteriorAccent";
        material.SetTexture("baseColorTexture", atlas);
        if (create)
            AssetDatabase.CreateAsset(material, InteriorAccentMaterialPath);
        EditorUtility.SetDirty(material);
        renderer.sharedMaterial = material;
    }

    private static void EnsureGleyLampMaterial()
    {
        var shader = Shader.Find(GleyLampShaderName);
        if (shader == null)
            throw new InvalidOperationException("Aventador Gley lamp shader failed to import: " + GleyLampShaderName);

        var material = AssetDatabase.LoadAssetAtPath<Material>(GleyLampMaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "LamborghiniAventadorGleyLamp" };
            AssetDatabase.CreateAsset(material, GleyLampMaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }

        var importer = AssetImporter.GetAtPath(GleyLampMaterialPath);
        if (importer == null)
            throw new InvalidOperationException("Aventador Gley lamp material importer is missing.");
        importer.SetAssetBundleNameAndVariant("lamborghiniaventador", "unity3d");
        AssetDatabase.SaveAssets();
    }

    private static void CreateManifest()
    {
        var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        if (manifest == null)
        {
            manifest = ScriptableObject.CreateInstance<BAModManifest>();
            AssetDatabase.CreateAsset(manifest, ManifestPath);
        }

        manifest.ModId = "Lamborghini_Aventador";
        manifest.DisplayName = "Lamborghini Aventador SVJ";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "lamborghiniaventador.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(DependenciesPath);
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;
        if (manifest.ModAssembly == null || manifest.LocalesFolder == null ||
            manifest.DependenciesFolder == null)
            throw new InvalidOperationException("Lamborghini Aventador manifest assembly, locale, or dependency-folder reference could not be assigned.");
        EditorUtility.SetDirty(manifest);
    }

    private static void VerifyPlatformBundles()
    {
        var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        var requiredPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;
        if (manifest == null || (manifest.TargetPlatforms & requiredPlatforms) != requiredPlatforms)
            throw new InvalidOperationException("Lamborghini Aventador manifest must target Windows and Mac.");

        foreach (var pair in new[] { (Name: "Windows", Path: WindowsBundlePath), (Name: "Mac", Path: MacBundlePath) })
        {
            var manifestPath = pair.Path + ".manifest";
            if (!File.Exists(pair.Path) || !File.Exists(manifestPath))
                throw new InvalidOperationException("Lamborghini Aventador platform bundle or manifest is missing: " + pair.Name);
            var text = File.ReadAllText(manifestPath);
            if (!BuildPipeline.GetCRCForAssetBundle(pair.Path, out var crc) || !text.Contains("CRC: " + crc))
                throw new InvalidOperationException("Lamborghini Aventador " + pair.Name + " bundle CRC differs from its manifest.");
            Debug.Log("Lamborghini Aventador " + pair.Name + " bundle manifest verified; CRC=" + crc + ".");
        }
    }

    private static VehicleType CreateVehicleType(VehicleType donor)
    {
        var target = AssetDatabase.LoadAssetAtPath<VehicleType>(VehiclePath);
        if (target == null)
        {
            if (!AssetDatabase.CopyAsset(DonorVehiclePath, VehiclePath))
                throw new InvalidOperationException("Could not create the target-owned VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<VehicleType>(VehiclePath);
        }
        if (target == null) throw new InvalidOperationException("Generated VehicleType failed to load.");
        if (!ReferenceEquals(target, donor)) EditorUtility.CopySerialized(donor, target);
        target.name = "LamborghiniAventador";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 608358f);
        SetNumber(serialized, "maxFuel", 85f);
        SetNumber(serialized, "maxCargoCapacity", 2f);
        SetNumber(serialized, "maxSpeed", 355f);
        SetNumber(serialized, "enginePower", EnginePower);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        SetNumber(serialized, "turnRadius", 27f);
        SetNumber(serialized, "damageIntensity", 0.42f);
        SetBool(serialized, "fitsHandTruck", false);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", true);
        SetBool(serialized, "isLuxuryCar", true);
        SetBool(serialized, "enclosed", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        return target;
    }

    private static void StripDonorGeometry(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
            renderer.sharedMaterials = Array.Empty<Material>();
        }
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) filter.sharedMesh = null;
        foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.sharedMesh = null;
        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
    }

    private static void RemoveDonorBehaviours(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component != null && component.GetType().Name.StartsWith("AudiRS6R", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(component);
        }
    }

    private static void NormalizeModel(Transform model)
    {
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one;
        var wheelTransforms = model.GetComponentsInChildren<Transform>(true);
        var frontLeft = FindWheelSource(wheelTransforms, "3DWheel Front L");
        var frontRight = FindWheelSource(wheelTransforms, "3DWheel Front R");
        var rearLeft = FindWheelSource(wheelTransforms, "3DWheel Rear L");
        var rearRight = FindWheelSource(wheelTransforms, "3DWheel Rear R");
        if (frontLeft == null || frontRight == null || rearLeft == null || rearRight == null)
            throw new InvalidOperationException("Could not orient the Aventador model without all four named source wheels.");
        var uprightCandidates = new[]
        {
            Quaternion.identity,
            Quaternion.Euler(90f, 0f, 0f), Quaternion.Euler(-90f, 0f, 0f),
            Quaternion.Euler(0f, 0f, 90f), Quaternion.Euler(0f, 0f, -90f),
            Quaternion.Euler(0f, 90f, 0f), Quaternion.Euler(0f, -90f, 0f),
        };
        var yawFlip = Quaternion.Euler(0f, 180f, 0f);
        var candidates = uprightCandidates.SelectMany(rotation => new[] { rotation, yawFlip * rotation }).ToArray();
        var bestRotation = Quaternion.identity;
        var bestBounds = default(Bounds);
        var bestScore = float.NegativeInfinity;
        var bestWheelCenterY = float.NaN;
        var bestWheelCenterFraction = float.NaN;
        foreach (var candidate in candidates)
        {
            model.localRotation = candidate;
            if (!TryGetBounds(model, out var bounds)) continue;
            if (bounds.size.y <= 0.0001f) continue;
            if (!TryGetBounds(frontLeft, out var frontLeftBounds) || !TryGetBounds(frontRight, out var frontRightBounds) ||
                !TryGetBounds(rearLeft, out var rearLeftBounds) || !TryGetBounds(rearRight, out var rearRightBounds))
                continue;
            var frontZ = (frontLeftBounds.center.z + frontRightBounds.center.z) * 0.5f;
            var rearZ = (rearLeftBounds.center.z + rearRightBounds.center.z) * 0.5f;
            if (frontZ <= rearZ) continue;
            var wheelCentersY = new[]
            {
                frontLeftBounds.center.y,
                frontRightBounds.center.y,
                rearLeftBounds.center.y,
                rearRightBounds.center.y,
            };
            var wheelCenterY = wheelCentersY.Average();
            var wheelCenterFraction = (wheelCenterY - bounds.min.y) / bounds.size.y;
            var lowerWheelLimit = bounds.min.y + bounds.size.y * 0.42f;
            if (wheelCenterY >= bounds.center.y || wheelCentersY.Any(y => y >= lowerWheelLimit))
                continue;
            var horizontal = Mathf.Max(bounds.size.x, bounds.size.z);
            var longitudinal = bounds.size.z;
            var upright = bounds.size.y;
            var score = 1000f + longitudinal - Mathf.Abs(horizontal - longitudinal) * 0.01f -
                        Mathf.Abs(upright - 1.1f) * 0.05f - Mathf.Abs(wheelCenterFraction - 0.30f);
            if (bounds.size.z < bounds.size.x * 1.2f || bounds.size.y > bounds.size.z * 0.5f || score <= bestScore) continue;
            bestRotation = candidate;
            bestBounds = bounds;
            bestScore = score;
            bestWheelCenterY = wheelCenterY;
            bestWheelCenterFraction = wheelCenterFraction;
        }
        if (bestScore == float.NegativeInfinity)
            throw new InvalidOperationException("Could not orient imported Aventador with longitudinal Z, vertical Y, front wheels forward, and all four wheels below the body center.");

        // The source GLB has a 0.01 axis scale in its FINAL_MODEL.fbx node. Fit a
        // uniform authoring scale to the documented length; never compensate in Update.
        model.localRotation = bestRotation;
        var scale = Length / bestBounds.size.z;
        model.localScale = Vector3.one * scale;
        if (!TryGetBounds(model, out var scaled)) throw new InvalidOperationException("Aventador bounds disappeared after scale correction.");
        model.position += new Vector3(-scaled.center.x, -scaled.min.y, -scaled.center.z);
        if (!TryGetBounds(model, out var finalBounds)) throw new InvalidOperationException("Could not measure normalized Aventador body.");
        if (Mathf.Abs(finalBounds.size.x - WidthIncludingMirrors) > 0.22f || Mathf.Abs(finalBounds.size.y - Height) > 0.22f || Mathf.Abs(finalBounds.size.z - Length) > 0.03f)
            throw new InvalidOperationException($"Aventador imported bounds disagree with source dimensions after uniform scale: {finalBounds.size}; inspect axis orientation before saving.");
        Debug.Log($"LamborghiniAventador imported GLB normalized with scale={scale:F4}, rotation={bestRotation.eulerAngles}, bounds={finalBounds.size}, center={finalBounds.center}, meanSourceWheelCenterY={bestWheelCenterY:F4}, wheelCenterFraction={bestWheelCenterFraction:F3}, namedFrontZ={frontLeft.position.z:F2}, namedRearZ={rearLeft.position.z:F2}.");
    }

    private static void CombineWheelMeshes(List<WheelSource> wheels)
    {
        if (wheels.Count != 4)
            throw new InvalidOperationException($"Aventador wheel combine requires four measured wheel groups, found {wheels.Count}.");

        var parent = Root + "/Generated";
        if (!AssetDatabase.IsValidFolder(parent))
            AssetDatabase.CreateFolder(Root, "Generated");

        var importer = AssetImporter.GetAtPath(WheelMeshesPath);
        if (importer == null && AssetDatabase.LoadMainAssetAtPath(WheelMeshesPath) == null)
        {
            var placeholder = new Mesh { name = "AventadorWheelBatch_Wheel_FL_00" };
            AssetDatabase.CreateAsset(placeholder, WheelMeshesPath);
            importer = AssetImporter.GetAtPath(WheelMeshesPath);
        }
        if (importer == null)
            throw new InvalidOperationException("Aventador combined wheel mesh asset importer is missing.");
        importer.SetAssetBundleNameAndVariant("lamborghiniaventador", "unity3d");

        var savedMeshes = AssetDatabase.LoadAllAssetsAtPath(WheelMeshesPath)
            .OfType<Mesh>().ToDictionary(mesh => mesh.name, StringComparer.Ordinal);
        var mainMesh = AssetDatabase.LoadMainAssetAtPath(WheelMeshesPath) as Mesh;
        var mainMeshClaimed = false;
        var generatedNames = new HashSet<string>(StringComparer.Ordinal);
        var generatedMaterialNames = new HashSet<string>(StringComparer.Ordinal);
        var wheelMaterials = new Dictionary<(Material Source, WheelSurface Surface), Material>();
        var totalSources = 0;
        var totalBatches = 0;
        var totalRimSources = 0;
        var totalRubberSources = 0;

        foreach (var wheel in wheels.OrderBy(item => item.MountName, StringComparer.Ordinal))
        {
            var wheelRimSources = 0;
            var wheelRubberSources = 0;
            var sourceChildren = new List<GameObject>();
            foreach (Transform child in wheel.Group)
                sourceChildren.Add(child.gameObject);

            var wheelCenter = wheel.Group.InverseTransformPoint(wheel.Bounds.center);
            var sourceFilters = wheel.Group.GetComponentsInChildren<MeshFilter>(true)
                .Where(filter => filter != null && filter.sharedMesh != null).ToArray();
            var sourcePoints = sourceFilters
                .SelectMany(filter => filter.sharedMesh!.vertices.Select(vertex =>
                {
                    var local = wheel.Group.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                    return local;
                })).ToArray();
            var sourceMin = sourcePoints.Aggregate(new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), Vector3.Min);
            var sourceMax = sourcePoints.Aggregate(new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity), Vector3.Max);
            var sourceSize = sourceMax - sourceMin;
            var axleAxis = sourceSize.x <= sourceSize.y && sourceSize.x <= sourceSize.z ? 0 :
                sourceSize.y <= sourceSize.z ? 1 : 2;
            var sourceOuterRadius = sourcePoints.Select(point => RadialDistance(point - wheelCenter, axleAxis))
                .DefaultIfEmpty(0f).Max();
            if (sourceOuterRadius <= 0.0001f)
                throw new InvalidOperationException($"Aventador wheel '{wheel.MountName}' has no measurable radial geometry.");

            var groups = new List<(Material Material, WheelSurface Surface, MeshRenderer Settings, List<(MeshFilter Filter, MeshRenderer Renderer)> Sources, List<CombineInstance> Parts)>();
            foreach (var renderer in wheel.Group.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                var materials = renderer.sharedMaterials;
                if (filter == null || mesh == null || materials.Length != 1 || materials[0] == null ||
                    mesh.subMeshCount != 1 || renderer.HasPropertyBlock() || renderer.forceRenderingOff ||
                    renderer.lightmapIndex >= 0 || renderer.realtimeLightmapIndex >= 0 ||
                    renderer.probeAnchor != null || renderer.lightProbeProxyVolumeOverride != null ||
                    renderer.additionalVertexStreams != null)
                    throw new InvalidOperationException($"Aventador wheel renderer '{renderer.name}' has an unsupported mesh, material slot, or per-renderer state; refusing a lossy wheel combine.");

                var sourceMaterial = materials[0];
                var surface = ClassifyWheelSurface(wheel, filter, sourceOuterRadius, wheelCenter, axleAxis);
                var material = GetWheelSurfaceMaterial(sourceMaterial, surface, wheelMaterials, generatedMaterialNames);
                if (surface == WheelSurface.Rubber)
                {
                    totalRubberSources++;
                    wheelRubberSources++;
                }
                else
                {
                    totalRimSources++;
                    wheelRimSources++;
                }
                var group = groups.FirstOrDefault(candidate => candidate.Material == material && candidate.Surface == surface &&
                    SameWheelRenderState(candidate.Settings, renderer));
                if (group.Sources == null)
                {
                    group = (material, surface, renderer,
                        new List<(MeshFilter Filter, MeshRenderer Renderer)>(),
                        new List<CombineInstance>());
                    groups.Add(group);
                }

                group.Parts.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = 0,
                    transform = wheel.Group.worldToLocalMatrix * filter.transform.localToWorldMatrix,
                });
                group.Sources.Add((filter, renderer));
            }

            if (groups.Count == 0)
                throw new InvalidOperationException($"Aventador wheel group '{wheel.MountName}' has no active wheel mesh renderers.");

            var sourceMeshCount = groups.Sum(group => group.Sources.Count);
            var referencedMeshCount = wheel.Group.GetComponentsInChildren<MeshFilter>(true)
                .Count(filter => filter != null && filter.sharedMesh != null);
            if (referencedMeshCount != sourceMeshCount)
                throw new InvalidOperationException($"Aventador wheel group '{wheel.MountName}' has {referencedMeshCount} referenced mesh filters but only {sourceMeshCount} supported renderers; refusing to remove unrendered mesh data.");
            foreach (var component in wheel.Group.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.transform == wheel.Group ||
                    component is Transform || component is MeshRenderer || component is MeshFilter)
                    continue;
                throw new InvalidOperationException($"Aventador wheel group '{wheel.MountName}' contains unsupported component '{component.GetType().Name}' on '{component.name}'.");
            }

            var orderedGroups = groups.OrderBy(group => group.Surface).ThenBy(group => group.Material.name, StringComparer.Ordinal).ToArray();
            for (var index = 0; index < orderedGroups.Length; index++)
            {
                var group = orderedGroups[index];
                var meshName = $"AventadorWheelBatch_{wheel.MountName}_{index:D2}";
                generatedNames.Add(meshName);
                var combined = new Mesh { name = meshName };
                if (group.Parts.Sum(part => part.mesh.vertexCount) > 65535)
                    combined.indexFormat = IndexFormat.UInt32;
                combined.CombineMeshes(group.Parts.ToArray(), true, true, false);
                combined.RecalculateBounds();

                if (mainMesh != null && !mainMeshClaimed && index == 0 && !savedMeshes.ContainsKey(meshName))
                {
                    EditorUtility.CopySerialized(combined, mainMesh);
                    mainMesh.name = meshName;
                    combined = mainMesh;
                    mainMeshClaimed = true;
                    savedMeshes[meshName] = mainMesh;
                }
                else if (savedMeshes.TryGetValue(meshName, out var existing))
                {
                    var generatedMesh = combined;
                    EditorUtility.CopySerialized(generatedMesh, existing);
                    existing.name = meshName;
                    combined = existing;
                    if (existing == mainMesh)
                        mainMeshClaimed = true;
                    UnityEngine.Object.DestroyImmediate(generatedMesh);
                }
                else if (AssetDatabase.LoadMainAssetAtPath(WheelMeshesPath) == null)
                {
                    AssetDatabase.CreateAsset(combined, WheelMeshesPath);
                    mainMesh = combined;
                    importer = AssetImporter.GetAtPath(WheelMeshesPath);
                    if (importer == null)
                        throw new InvalidOperationException("Aventador combined wheel mesh importer could not be created.");
                    importer.SetAssetBundleNameAndVariant("lamborghiniaventador", "unity3d");
                    savedMeshes[meshName] = combined;
                }
                else
                {
                    AssetDatabase.AddObjectToAsset(combined, WheelMeshesPath);
                    savedMeshes[meshName] = combined;
                }

                var batchObject = new GameObject(meshName);
                batchObject.transform.SetParent(wheel.Group, false);
                batchObject.AddComponent<MeshFilter>().sharedMesh = combined;
                var batchRenderer = batchObject.AddComponent<MeshRenderer>();
                batchRenderer.sharedMaterial = group.Material;
                CopyWheelRenderState(group.Settings, batchRenderer);

                totalSources += group.Sources.Count;
                totalBatches++;
                EditorUtility.SetDirty(combined);
            }

            foreach (var sourceChild in sourceChildren)
                UnityEngine.Object.DestroyImmediate(sourceChild);

            Debug.Log($"Aventador wheel material split {wheel.MountName}: sourceFragments={wheelRimSources + wheelRubberSources}, rimFragments={wheelRimSources}, rubberFragments={wheelRubberSources}, tireAtlas='P Zero sidewall/Pirelli strip/wrapped tread', axleAxis={axleAxis}, sourceOuterRadius={sourceOuterRadius:F6}, measured rim max=0.74R, tire min=0.85R.");
        }

        foreach (var stale in AssetDatabase.LoadAllAssetsAtPath(WheelMeshesPath)
                     .OfType<Mesh>().Where(mesh => !generatedNames.Contains(mesh.name)).ToArray())
        {
            if (AssetDatabase.IsMainAsset(stale))
            {
                if (generatedNames.Contains("AventadorWheelBatch_Wheel_FL_00"))
                {
                    var replacement = savedMeshes["AventadorWheelBatch_Wheel_FL_00"];
                    if (replacement == stale)
                        continue;
                }
                throw new InvalidOperationException("Aventador generated wheel mesh asset contains an unexpected main mesh that cannot be safely removed.");
            }
            AssetDatabase.RemoveObjectFromAsset(stale);
            UnityEngine.Object.DestroyImmediate(stale, true);
        }

        foreach (var stale in AssetDatabase.LoadAllAssetsAtPath(WheelMeshesPath)
                     .OfType<Material>().Where(material => material.name.StartsWith("AventadorWheel_", StringComparison.Ordinal) &&
                                                            !generatedMaterialNames.Contains(material.name)).ToArray())
        {
            AssetDatabase.RemoveObjectFromAsset(stale);
            UnityEngine.Object.DestroyImmediate(stale, true);
        }

        EditorUtility.SetDirty(importer);
        AssetDatabase.SaveAssets();
        Debug.Log($"LamborghiniAventador wheel renderer combine: sourceRenderers={totalSources}, rimSourceMeshes={totalRimSources}, rubberSourceMeshes={totalRubberSources}, replacementRenderers={totalBatches}, wheelMounts={wheels.Count}; surface split uses tire-atlas UV signatures and the measured source radial gap (outer tire starts at 0.85R; rim ends at 0.74R)." );
    }

    private static WheelSurface ClassifyWheelSurface(WheelSource wheel, MeshFilter filter, float sourceOuterRadius, Vector3 wheelCenter, int axleAxis)
    {
        var mesh = filter.sharedMesh;
        var vertices = mesh.vertices;
        var uvs = mesh.uv;
        if (vertices.Length == 0 || uvs.Length != vertices.Length)
            throw new InvalidOperationException($"Aventador wheel fragment '{filter.name}' has no complete UV0 chart; refusing an unproven rim/rubber assignment.");

        var outerBandCount = 0;
        var outerChartCount = 0;
        var uvMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var uvMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (var index = 0; index < vertices.Length; index++)
        {
            var local = wheel.Group.InverseTransformPoint(filter.transform.TransformPoint(vertices[index]));
            var radius = RadialDistance(local - wheelCenter, axleAxis);
            var uv = uvs[index];
            uvMin = Vector2.Min(uvMin, uv);
            uvMax = Vector2.Max(uvMax, uv);
            if (radius >= sourceOuterRadius * TireAtlasOuterRadiusFraction)
            {
                outerBandCount++;
                if (IsTireAtlasUv(uv)) outerChartCount++;
            }
        }

        // The GLB's tire-only normal atlas has three stable UV signatures: the
        // large P Zero sidewall chart, the Pirelli sidewall strip, and its
        // wrapped tread strip. In the authored wheel geometry these charts
        // coincide with the radial gap between tire (>=0.85R) and rim (<=0.74R).
        // Require both signals so an inner spoke that happens to cross the UV
        // atlas cannot be assigned the rubber finish.
        var outerFraction = outerBandCount / (float)vertices.Length;
        var chartFraction = outerBandCount == 0 ? 0f : outerChartCount / (float)outerBandCount;
        var uvSpan = uvMax - uvMin;
        var wrappedV = uvs.Select(uv => uv.y - Mathf.Floor(uv.y)).ToArray();
        var wrappedMinV = wrappedV.Min();
        var wrappedMaxV = wrappedV.Max();
        var sidewallChart = uvSpan.x >= 0.60f && uvSpan.y >= 0.60f && uvMin.x <= 0.16f && uvMax.x >= 0.84f &&
                            uvMin.y >= -0.01f && uvMax.y <= 1.01f;
        var pirelliChart = uvMin.x >= 0.76f && uvMax.x <= 1.01f &&
                           ((uvMin.y >= 0.10f && uvMax.y <= 0.38f) || (wrappedMinV >= 0.62f && wrappedMaxV <= 0.90f));
        var wrappedTreadChart = uvMin.x >= 0.68f && uvMax.x <= 1.01f &&
                                ((uvMin.y >= -1.01f && uvMax.y <= -0.82f) || (wrappedMinV >= 0.85f && wrappedMaxV <= 1.00f));
        var hasTireChart = sidewallChart || pirelliChart || wrappedTreadChart;
        if (outerFraction >= 0.80f && chartFraction >= 0.80f && hasTireChart)
            return WheelSurface.Rubber;

        return WheelSurface.Rim;
    }

    private static float RadialDistance(Vector3 delta, int axleAxis) => axleAxis == 0
        ? Mathf.Sqrt(delta.y * delta.y + delta.z * delta.z)
        : axleAxis == 1
            ? Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z)
            : Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y);

    private static bool IsTireAtlasUv(Vector2 uv)
    {
        var wrappedV = uv.y - Mathf.Floor(uv.y);
        var pirelli = uv.x >= 0.76f && uv.x <= 1.01f && wrappedV >= 0.10f && wrappedV <= 0.38f;
        var tread = uv.x >= 0.68f && uv.x <= 1.01f && (wrappedV <= 0.15f || wrappedV >= 0.85f);
        var sidewall = uv.x >= 0.13f && uv.x <= 0.87f && wrappedV >= 0.13f && wrappedV <= 0.87f;
        return pirelli || tread || sidewall;
    }

    private static Material GetWheelSurfaceMaterial(
        Material source,
        WheelSurface surface,
        IDictionary<(Material Source, WheelSurface Surface), Material> cache,
        ISet<string> generatedMaterialNames)
    {
        var key = (source, surface);
        if (cache.TryGetValue(key, out var existing)) return existing;

        var name = surface == WheelSurface.Rubber ? "AventadorWheel_Rubber" : "AventadorWheel_Rim";
        var material = AssetDatabase.LoadAllAssetsAtPath(WheelMeshesPath).OfType<Material>()
            .FirstOrDefault(candidate => candidate.name == name);
        if (material == null)
        {
            material = new Material(source) { name = name };
            AssetDatabase.AddObjectToAsset(material, WheelMeshesPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, material);
            material.name = name;
        }
        EditorUtility.SetDirty(material);
        generatedMaterialNames.Add(name);
        cache.Add(key, material);
        return material;
    }

    private static bool SameWheelRenderState(MeshRenderer first, MeshRenderer other) =>
        first.shadowCastingMode == other.shadowCastingMode &&
        first.receiveShadows == other.receiveShadows &&
        first.lightProbeUsage == other.lightProbeUsage &&
        first.reflectionProbeUsage == other.reflectionProbeUsage &&
        first.motionVectorGenerationMode == other.motionVectorGenerationMode &&
        first.renderingLayerMask == other.renderingLayerMask &&
        first.sortingLayerID == other.sortingLayerID &&
        first.sortingOrder == other.sortingOrder &&
        first.rendererPriority == other.rendererPriority &&
        first.allowOcclusionWhenDynamic == other.allowOcclusionWhenDynamic;

    private static void CopyWheelRenderState(MeshRenderer source, MeshRenderer target)
    {
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;
        target.motionVectorGenerationMode = source.motionVectorGenerationMode;
        target.renderingLayerMask = source.renderingLayerMask;
        target.sortingLayerID = source.sortingLayerID;
        target.sortingOrder = source.sortingOrder;
        target.rendererPriority = source.rendererPriority;
        target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
    }

    private static Transform? FindWheelSource(IEnumerable<Transform> transforms, string prefix) =>
        transforms.FirstOrDefault(transform => transform.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static List<WheelSource> MeasureWheelSources(Transform model)
    {
        var specs = new[]
        {
            // The GLB faces -Z before the authoring yaw flip. That proper 180°
            // rotation also reverses X, so source L geometry occupies the game
            // right side and source R geometry must become the game-left wheel.
            ("3DWheel Front L", "Wheel_FR", "FrontRight_WheelController", true, false),
            ("3DWheel Front R", "Wheel_FL", "FrontLeft_WheelController", true, true),
            ("3DWheel Rear L", "Wheel_BR", "RearRight_WheelController", false, false),
            ("3DWheel Rear R", "Wheel_BL", "RearLeft_WheelController", false, true),
        };
        var all = model.GetComponentsInChildren<Transform>(true);
        var results = new List<WheelSource>(4);
        foreach (var spec in specs)
        {
            var group = all.FirstOrDefault(t => t.name.StartsWith(spec.Item1, StringComparison.OrdinalIgnoreCase));
            if (group == null || !TryGetBounds(group, out var bounds))
                throw new InvalidOperationException($"Required imported wheel group '{spec.Item1}' was not found or has no geometry.");
            results.Add(new WheelSource
            {
                Label = spec.Item1,
                MountName = spec.Item2,
                ControllerName = spec.Item3,
                Front = spec.Item4,
                Left = spec.Item5,
                Group = group,
                Bounds = bounds,
                Target = new Vector3(
                    (spec.Item5 ? -1f : 1f) * (spec.Item4 ? FrontTrack : RearTrack) * 0.5f,
                    (spec.Item4 ? FrontRadius : RearRadius) - WheelCenterLowering,
                    (spec.Item4 ? Wheelbase * 0.5f : -Wheelbase * 0.5f) - WheelRearwardOffset),
            });
            Debug.Log($"Aventador source wheel {spec.Item2}: center={bounds.center}, dimensions={bounds.size}.");
        }
        return results;
    }

    private static void MoveWheelVisuals(Transform root, List<WheelSource> wheels)
    {
        foreach (var wheel in wheels)
        {
            var mount = new GameObject(wheel.MountName);
            mount.transform.SetParent(root, false);
            mount.transform.localPosition = wheel.Target;
            wheel.Group.SetParent(mount.transform, true);
            wheel.Group.name = "Geometry_" + wheel.MountName;
            if (!TryGetBounds(wheel.Group, out var source)) throw new InvalidOperationException("Wheel geometry lost its renderers during attachment.");
            var targetWidth = wheel.Front ? FrontWidth : RearWidth;
            var radius = wheel.Front ? FrontRadius : RearRadius;
            mount.transform.localScale = new Vector3(targetWidth / source.size.x, radius * 2f / source.size.y, radius * 2f / source.size.z);
            if (!TryGetBounds(wheel.Group, out var fitted)) throw new InvalidOperationException("Wheel geometry lost its renderers during fitting.");
            wheel.Group.position += root.TransformPoint(wheel.Target) - fitted.center;
            if (!TryGetBounds(wheel.Group, out fitted)) throw new InvalidOperationException("Wheel geometry lost its renderers before face correction.");

            // glTFast's handedness conversion makes the GLB's inner dish face
            // point outward in Unity. Rotate every imported wheel around its
            // own center so the spoke/rim face becomes the visible outer face.
            var preservedCenter = fitted.center;
            wheel.Group.RotateAround(preservedCenter, root.up, 180f);
            if (!TryGetBounds(wheel.Group, out var corrected) ||
                Vector3.Distance(preservedCenter, corrected.center) > 0.001f)
                throw new InvalidOperationException($"Wheel face correction moved {wheel.MountName} away from its measured center.");
            if (!TryGetBounds(wheel.Group, out corrected))
                throw new InvalidOperationException($"Could not remeasure wheel geometry for {wheel.MountName}.");
            var expectedVisualCenter = root.TransformPoint(wheel.Target);
            if (Vector3.Distance(corrected.center, expectedVisualCenter) > 0.001f)
                throw new InvalidOperationException(
                    $"Wheel visual adjustment moved {wheel.MountName} to {corrected.center}, expected {expectedVisualCenter}.");
            wheel.Bounds = corrected;
            Debug.Log($"Aventador wheel center authoring {wheel.MountName}: centerBefore={preservedCenter}, " +
                      $"centerAfter={corrected.center}, pivot/controller target lowered={WheelCenterLowering:0.000}m; " +
                      $"tire radius remains unchanged.");
        }
    }

    private static void MoveCalipers(Transform root, Transform model, List<WheelSource> wheels)
    {
        var renderers = model.GetComponentsInChildren<Renderer>(true)
            .Where(r => r.sharedMaterials.Any(m => m != null && m.name.IndexOf("Calliper", StringComparison.OrdinalIgnoreCase) >= 0))
            .ToArray();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("Aventador GLB contains no renderer with a Calliper material; stable Caliper_* roots were still authored empty.");
        }
        var mounts = new Dictionary<WheelSource, Transform>();
        var geometryHolders = new Dictionary<WheelSource, Transform>();
        foreach (var wheel in wheels)
        {
            var mount = new GameObject("Caliper_" + wheel.MountName.Substring("Wheel_".Length));
            mount.transform.SetParent(root, false);
            mount.transform.localPosition = wheel.Target;
            mounts.Add(wheel, mount.transform);
            var geometry = new GameObject("Geometry_" + mount.name);
            geometry.transform.SetParent(mount.transform, false);
            geometryHolders.Add(wheel, geometry.transform);
        }
        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            var nearest = wheels.OrderBy(w => Vector3.SqrMagnitude(renderer.bounds.center - root.TransformPoint(w.Target))).First();
            renderer.transform.SetParent(geometryHolders[nearest], true);
        }
        foreach (var holder in geometryHolders.Values)
            holder.localPosition += Vector3.down * CaliperGeometryLowering;
    }

    private static void ConfigureWheelControllers(Transform root, List<WheelSource> wheels)
    {
        foreach (var wheel in wheels)
        {
            var controller = Find(root, wheel.ControllerName) ?? throw new InvalidOperationException($"Native wheel controller '{wheel.ControllerName}' is missing.");
            // A WheelController is the upper suspension anchor, not the rolling
            // visual's center. Native ground teleports place the vehicle root on
            // the road; co-locating these two points fully compresses all springs.
            // Author the loaded extension above the unchanged tire-center pivot.
            controller.localPosition = wheel.Target + Vector3.up * CalculateSuspensionRestLength(wheel.Front);
            var isFront = wheel.Front;
            var radius = isFront ? FrontRadius : RearRadius;
            var width = isFront ? FrontWidth : RearWidth;
            foreach (var component in controller.GetComponents<MonoBehaviour>())
            {
                if (component == null || component.GetType().Name != "WheelController") continue;
                var so = new SerializedObject(component);
                SetRelativeNumber(so, "wheel.radius", radius);
                SetRelativeNumber(so, "wheel.width", width);
                SetRelativeNumber(so, "spring.maxLength", isFront ? FrontSpringTravel : RearSpringTravel);
                SetRelativeNumber(so, "spring.maxForce", SpringMaximumForce);
                var springCurve = FindRelative(so, "spring.forceCurve");
                if (springCurve == null || springCurve.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Native suspension force curve is unavailable.");
                springCurve.animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                // The latest CSV shows 0-100 in 1.76 s but only about 0.02 max
                // longitudinal slip, so tire grip is not limiting launch. Keep
                // this modest setup and manage high-load output in the native
                // powertrain modifier while preserving factory engine ratings.
                SetRelativeNumber(so, "forwardFriction.grip", isFront ? 0.80f : 1.00f);
                SetRelativeNumber(so, "frictionCircleStrength", 0.98f);
                var wheelVisual = root.Find(wheel.MountName);
                var wheelProp = so.FindProperty("wheel");
                var visual = wheelProp?.FindPropertyRelative("visual");
                if (wheelVisual == null || visual?.propertyType != SerializedPropertyType.ObjectReference)
                    throw new InvalidOperationException($"Native visual binding unavailable for '{wheel.ControllerName}'.");
                visual.objectReferenceValue = wheelVisual.gameObject;
                var visualTransform = wheelProp?.FindPropertyRelative("visualTransform");
                if (visualTransform?.propertyType == SerializedPropertyType.ObjectReference) visualTransform.objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    internal static float CalculateSuspensionRestLength(bool front)
    {
        var frontZ = Wheelbase * 0.5f - WheelRearwardOffset;
        var rearZ = -Wheelbase * 0.5f - WheelRearwardOffset;
        var frontLoadFraction = (VehicleCenterOfMass.z - rearZ) / (frontZ - rearZ);
        var axleLoadFraction = front ? frontLoadFraction : 1f - frontLoadFraction;
        var cornerLoad = OperatingMass * Physics.gravity.magnitude * axleLoadFraction * 0.5f;
        var travel = front ? FrontSpringTravel : RearSpringTravel;
        if (cornerLoad <= 0f || cornerLoad >= SpringMaximumForce)
            throw new InvalidOperationException("Aventador static corner load is outside its suspension capacity.");
        return travel * (1f - cornerLoad / SpringMaximumForce);
    }

    private static void ConfigurePowertrain(GameObject root)
    {
        var enginePowerCurve = CreateEnginePowerCurve();
        var nativeEngineLoop = AssetDatabase.LoadAssetAtPath<AudioClip>(
            Root + "/Config/Audio/EngineLow.wav");
        if (nativeEngineLoop == null || nativeEngineLoop.samples < 44100 || nativeEngineLoop.length < 1f)
            throw new InvalidOperationException("Revuelto EngineLow WAV is missing or could not be imported for the NWH idle source.");
        var found = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || component.GetType().FullName != "NWH.VehiclePhysics2.VehicleController") continue;
            found = true;
            var so = new SerializedObject(component);
            // Start coupling earlier at launch to limit the sustained engine
            // flare seen below 20 km/h while retaining the NWH automatic clutch.
            SetRelativeNumber(so, "powertrain.clutch.engagementRPM", ClutchEngagementRPM);
            SetRelativeNumber(so, "powertrain.clutch.throttleEngagementOffsetRPM", ClutchThrottleEngagementOffsetRPM);
            SetRelativeNumber(so, "powertrain.clutch.engagementRange", ClutchEngagementRangeRPM);
            SetRelativeNumber(so, "powertrain.clutch.creepTorque", 0f);
            SetRelativeNumber(so, "powertrain.clutch.slipTorque",
                EstimatePeakTorqueNm(enginePowerCurve, EnginePower) * ClutchTorqueSafetyFactor);
            SetRelativeNumber(so, "powertrain.engine.inertia", 0.16f);
            SetRelativeNumber(so, "powertrain.engine.maxPower", EnginePower);
            SetRelativeNumber(so, "powertrain.engine.idleRPM", EngineIdleRPM);
            SetRelativeNumber(so, "powertrain.engine.revLimiterRPM", EngineRevLimiterRPM);
            SetRelativeNumber(so, "powertrain.engine.startDuration", 0.45f);
            SetRelativeBool(so, "powertrain.engine.stallingEnabled", true);
            SetRelativeBool(so, "powertrain.engine.autoStartOnThrottle", true);
            SetRelativeBool(so, "powertrain.engine.forcedInduction.useForcedInduction", false);
            var engineClips = FindRelative(so, "soundManager.engineRunningComponent.clips");
            if (engineClips == null || !engineClips.isArray)
                throw new InvalidOperationException("NWH engine running clip array is missing; cannot remove donor audio reference.");
            engineClips.arraySize = 1;
            engineClips.GetArrayElementAtIndex(0).objectReferenceValue = nativeEngineLoop;
            foreach (var path in new[]
                     {
                         "soundManager.engineStartStopComponent.clips",
                         "soundManager.engineFanComponent.clips",
                         "soundManager.exhaustPopComponent.clips",
                         "soundManager.gearChangeComponent.clips",
                         "soundManager.transmissionWhineComponent.clips",
                         "soundManager.turboFlutterComponent.clips",
                         "soundManager.turboWhistleComponent.clips",
                         "soundManager.wheelSkidComponent.clips",
                         "soundManager.wheelTireNoiseComponent.clips",
                         "soundManager.suspensionBumpComponent.clips",
                         "soundManager.blinkerComponent.clips",
                         "soundManager.airBrakeComponent.clips",
                         "soundManager.crashComponent.clips",
                         "soundManager.hornComponent.clips",
                         "soundManager.reverseBeepComponent.clips"
                     })
            {
                var clips = FindRelative(so, path);
                if (clips == null || !clips.isArray) continue;
                clips.arraySize = 0;
            }
            SetRelativeObject(so, "soundManager.mixer", null);
            var curve = FindRelative(so, "powertrain.engine.powerCurve");
            if (curve?.propertyType == SerializedPropertyType.AnimationCurve)
                curve.animationCurveValue = enginePowerCurve;
            SetRelativeNumber(so, "powertrain.transmission.finalGearRatio", 2.867f);
            SetRelativeNumber(so, "powertrain.transmission.forwardGearCount", 7f);
            SetRelativeNumber(so, "powertrain.transmission.reverseGearCount", 1f);
            SetRelativeNumber(so, "powertrain.transmission.shiftDuration", 0.095f);
            SetRelativeNumber(so, "powertrain.transmission._downshiftRPM", AutomaticDownshiftRpm);
            SetRelativeNumber(so, "powertrain.transmission._upshiftRPM", AutomaticUpshiftRpm);
            SetRelativeNumber(so, "powertrain.transmission.transmissionType", 1f);
            var gearProperty = FindRelative(so, "powertrain.transmission.gears");
            if (gearProperty != null && gearProperty.isArray)
            {
                gearProperty.arraySize = Gears.Length;
                for (var i = 0; i < Gears.Length; i++) gearProperty.GetArrayElementAtIndex(i).floatValue = Gears[i];
            }
            SetRelativeNumber(so, "brakes.maxTorque", BrakeMaxTorque);
            SetRelativeNumber(so, "steering.maximumSteerAngle", 33f);
            SetRelativeNumber(so, "powertrain.engine.engineLossPercent", 0.22f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        if (!found) throw new InvalidOperationException("NWH VehicleController missing from donor prefab.");
        ConfigureAerodynamics(root);
    }

    private static float EstimatePeakTorqueNm(AnimationCurve powerCurve, float maxPowerKw)
    {
        var peakTorqueNm = 0f;
        for (var rpm = EngineIdleRPM; rpm <= EngineRevLimiterRPM; rpm += 1f)
        {
            var angularVelocity = rpm * (Mathf.PI * 2f / 60f);
            var torqueNm = powerCurve.Evaluate(rpm / EngineRevLimiterRPM) * maxPowerKw * 1000f / angularVelocity;
            peakTorqueNm = Mathf.Max(peakTorqueNm, torqueNm);
        }
        return peakTorqueNm;
    }

    private static AnimationCurve CreateEnginePowerCurve()
    {
        // Lamborghini publishes the peak values, not a complete dyno trace.
        // This aspirated V12 curve interpolates a rising low/mid-range torque
        // shape while matching 720 Nm at 6,750 rpm and 566 kW at 8,500 rpm.
        // The linear power segments prevent Hermite tangents from overshooting
        // the factory torque ceiling between the authored points.
        var points = new (float Rpm, float TorqueNm)[]
        {
            (0f, 0f),
            (1100f, 320f),
            (1500f, 400f),
            (2000f, 470f),
            (2500f, 520f),
            (3000f, 560f),
            (3500f, 600f),
            (4000f, 635f),
            (4500f, 665f),
            (5000f, 688f),
            (5500f, 704f),
            (6000f, 715f),
            (6500f, 719f),
            (EnginePeakTorqueRPM, EnginePeakTorqueNm),
            (7000f, 718f),
            (7500f, 700f),
            (8000f, 675f),
            (EnginePeakPowerRPM, EnginePower * TorqueToPowerConversion / EnginePeakPowerRPM),
            (EngineRevLimiterRPM, 610f),
        };

        var keys = new Keyframe[points.Length];
        var normalizedRpm = new float[points.Length];
        var normalizedPower = new float[points.Length];
        for (var index = 0; index < points.Length; index++)
        {
            normalizedRpm[index] = points[index].Rpm / EngineRevLimiterRPM;
            var powerKw = points[index].TorqueNm * points[index].Rpm / TorqueToPowerConversion;
            normalizedPower[index] = powerKw / EnginePower;
        }

        for (var index = 0; index < points.Length; index++)
        {
            var previous = Mathf.Max(0, index - 1);
            var next = Mathf.Min(points.Length - 1, index + 1);
            var incoming = index == 0
                ? (normalizedPower[1] - normalizedPower[0]) / (normalizedRpm[1] - normalizedRpm[0])
                : (normalizedPower[index] - normalizedPower[previous]) / (normalizedRpm[index] - normalizedRpm[previous]);
            var outgoing = index == points.Length - 1
                ? incoming
                : (normalizedPower[next] - normalizedPower[index]) / (normalizedRpm[next] - normalizedRpm[index]);
            keys[index] = new Keyframe(normalizedRpm[index], normalizedPower[index], incoming, outgoing);
        }

        return new AnimationCurve(keys);
    }

    private static void ConfigureAerodynamics(GameObject root)
    {
        var wrapper = root.GetComponent<AerodynamicsModuleWrapper>();
        if (wrapper == null) wrapper = root.AddComponent<AerodynamicsModuleWrapper>();
        var module = wrapper.module ?? (wrapper.module = new NWH.VehiclePhysics2.Modules.Aerodynamics.AerodynamicsModule());
        module.dimensions = new Vector3(WidthIncludingMirrors, Height, Length);
        module.frontalCd = AerodynamicDragCoefficient;
        module.sideCd = 1.05f;
        module.simulateDrag = true;
        module.simulateDownforce = false;
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ?? throw new InvalidOperationException("Native donor Rigidbody is missing.");
        body.mass = OperatingMass;
        // CarController.Reset clears Rigidbody drag when the vehicle is spawned;
        // the NWH AerodynamicsModule below owns speed-squared road drag instead.
        body.drag = 0f;
        body.angularDrag = 1.1f;
        body.centerOfMass = VehicleCenterOfMass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var so = new SerializedObject(component);
            var useDefault = so.FindProperty("useDefaultCenterOfMass");
            var center = so.FindProperty("centerOfMass");
            if (useDefault?.propertyType == SerializedPropertyType.Boolean && center?.propertyType == SerializedPropertyType.Vector3)
            {
                useDefault.boolValue = false;
                center.vector3Value = body.centerOfMass;
                SetNumber(so, "baseMass", OperatingMass);
                SetNumber(so, "combinedMass", OperatingMass);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static void ConfigureBodyCollider(GameObject root)
    {
        var holder = Find(root.transform, "BodyCollider") ?? throw new InvalidOperationException("Native BodyCollider holder is missing.");
        foreach (var old in holder.GetComponents<BoxCollider>()) UnityEngine.Object.DestroyImmediate(old);
        var lower = holder.gameObject.AddComponent<BoxCollider>();
        lower.center = new Vector3(0f, 0.38f, 0f);
        lower.size = new Vector3(1.84f, 0.42f, 4.55f);
        var cabin = holder.gameObject.AddComponent<BoxCollider>();
        cabin.center = new Vector3(0f, 0.75f, -0.10f);
        cabin.size = new Vector3(1.62f, 0.48f, 2.25f);
    }

    private static void ConfigureBoxColliders(Transform root)
    {
        foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = true;
        foreach (var collider in root.GetComponentsInChildren<BoxCollider>(true))
        {
            if (collider.gameObject.name != "BodyCollider") continue;
            collider.isTrigger = false;
        }
    }

    private static void ConfigureWarehousePlacement(GameObject root)
    {
        var nativeBody = root.transform.Find("CarHolder/Colliders/BodyCollider");
        if (nativeBody == null || !TryGetBodyColliderBounds(root.transform, out var bounds))
            throw new InvalidOperationException("Aventador warehouse placement requires the fitted native BodyCollider.");

        // BuildingManager uses the first MeshCollider's mesh length for door
        // clearance. Spawn conflict checking resolves the separate child path.
        // Both meshes are query data. The solid boxes continue to drive physics.
        var warehouseSize = bounds.size + Vector3.forward * (2f * WarehouseClearancePerEnd);
        BindPlacementMesh(root, WarehousePlacementMeshPath, warehouseSize);
        BindPlacementMesh(nativeBody.gameObject, NativeSpawnMeshPath, bounds.size);
    }

    private static void BindPlacementMesh(GameObject target, string path, Vector3 size)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var create = mesh == null;
        if (mesh == null)
            mesh = new Mesh { name = Path.GetFileNameWithoutExtension(path) };

        var half = size * .5f;
        mesh.vertices = new[]
        {
            new Vector3(-half.x, -half.y, -half.z), new Vector3(half.x, -half.y, -half.z),
            new Vector3(half.x, half.y, -half.z), new Vector3(-half.x, half.y, -half.z),
            new Vector3(-half.x, -half.y, half.z), new Vector3(half.x, -half.y, half.z),
            new Vector3(half.x, half.y, half.z), new Vector3(-half.x, half.y, half.z)
        };
        mesh.triangles = new[]
        {
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7
        };
        mesh.RecalculateBounds();
        if (create) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);

        var collider = target.GetComponent<MeshCollider>();
        if (collider == null) collider = target.AddComponent<MeshCollider>();
        collider.enabled = false;
        collider.convex = true;
        collider.isTrigger = false;
        collider.sharedMesh = mesh;
    }

    private static void ConfigureNavMeshObstacles(GameObject root)
    {
        if (!TryGetBodyColliderBounds(root.transform, out var bodyBounds))
            throw new InvalidOperationException(
                "Aventador NavMesh obstacle setup could not find the solid BodyCollider bounds.");

        foreach (var obstacle in root.GetComponentsInChildren<NavMeshObstacle>(true))
        {
            if (obstacle == null || obstacle.shape != NavMeshObstacleShape.Box)
                continue;

            var obstacleTransform = obstacle.transform;
            var scale = obstacleTransform.lossyScale;
            if (Mathf.Abs(scale.x) < .0001f ||
                Mathf.Abs(scale.y) < .0001f ||
                Mathf.Abs(scale.z) < .0001f)
                continue;

            obstacle.center = obstacleTransform.InverseTransformPoint(
                root.transform.TransformPoint(bodyBounds.center));
            obstacle.size = new Vector3(
                ProjectBodySizeOntoAxis(bodyBounds.size, root.transform, obstacleTransform.right) /
                Mathf.Abs(scale.x),
                ProjectBodySizeOntoAxis(bodyBounds.size, root.transform, obstacleTransform.up) /
                Mathf.Abs(scale.y),
                ProjectBodySizeOntoAxis(bodyBounds.size, root.transform, obstacleTransform.forward) /
                Mathf.Abs(scale.z));
        }
    }

    private static bool TryGetBodyColliderBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(child.name, "BodyCollider", StringComparison.Ordinal))
                continue;

            foreach (var collider in child.GetComponents<BoxCollider>())
            {
                if (collider == null || collider.isTrigger)
                    continue;

                var halfSize = collider.size * .5f;
                for (var x = -1; x <= 1; x += 2)
                for (var y = -1; y <= 1; y += 2)
                for (var z = -1; z <= 1; z += 2)
                {
                    var corner = collider.center + Vector3.Scale(
                        halfSize, new Vector3(x, y, z));
                    var rootCorner = root.InverseTransformPoint(
                        collider.transform.TransformPoint(corner));
                    if (!found)
                    {
                        bounds = new Bounds(rootCorner, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(rootCorner);
                    }
                }
            }
        }

        return found;
    }

    private static float ProjectBodySizeOntoAxis(
        Vector3 bodySize, Transform root, Vector3 worldAxis)
    {
        worldAxis.Normalize();
        return Mathf.Abs(Vector3.Dot(worldAxis, root.right)) * bodySize.x +
               Mathf.Abs(Vector3.Dot(worldAxis, root.up)) * bodySize.y +
               Mathf.Abs(Vector3.Dot(worldAxis, root.forward)) * bodySize.z;
    }

    private static void ConfigureNativeReferences(GameObject root, VehicleType vehicleType)
    {
        var native = root.GetComponent<VehicleController>() ??
                     throw new InvalidOperationException("Native VehicleController is missing while binding the body collider.");
        var body = Find(root.transform, "BodyCollider") ??
                   throw new InvalidOperationException("Native BodyCollider holder is missing while binding the vehicle collider.");
        var primaryCollider = body.GetComponents<BoxCollider>().FirstOrDefault(collider =>
            collider.enabled && !collider.isTrigger && Mathf.Abs(collider.size.z - 4.55f) < 0.01f) ??
            throw new InvalidOperationException("Aventador primary solid chassis BoxCollider was not created.");
        var nativeProperties = new SerializedObject(native);
        var vehicleCollider = nativeProperties.FindProperty("vehicleCollider");
        if (vehicleCollider?.propertyType != SerializedPropertyType.ObjectReference)
            throw new InvalidOperationException("Native VehicleController.vehicleCollider reference is unavailable.");
        vehicleCollider.objectReferenceValue = primaryCollider;
        nativeProperties.ApplyModifiedPropertiesWithoutUndo();

        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null) continue;
            var so = new SerializedObject(component);
            var type = so.FindProperty("vehicleType");
            if (type?.propertyType == SerializedPropertyType.ObjectReference) type.objectReferenceValue = vehicleType;
            var instance = so.FindProperty("vehicleInstance");
            var name = instance?.FindPropertyRelative("vehicleTypeName");
            if (name?.propertyType == SerializedPropertyType.String) name.stringValue = VehicleTypeName;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void ConfigureAnchors(Transform root, List<WheelSource> wheels)
    {
        // The body is left-hand drive; wheel/controller geometry confirms that
        // the driver occupies negative X. Keep the wheel marker static: it is
        // used to place the seated avatar's hands, not as a rotating visual.
        var driverSeat = EnsureAnchor(root, "DriverSeatAnchor", new Vector3(-0.38f, 0.37f, 0.25f));
        EnsureAnchor(root, "Steering_wheel", new Vector3(-0.38f, 0.95f, 0.62f));
        ConfigureSteeringReference(root);
        EnsureAnchor(root, "NpcDriverSeatAnchor", driverSeat.localPosition);
        EnsureAnchor(root, "CameraAnchor", new Vector3(0f, 1.15f, -0.15f));
        EnsureAnchor(root, "ExitDriverAnchor", new Vector3(-ExitSideOffset, 0.10f, 0f));
        EnsureAnchor(root, "ExitPassengerAnchor", new Vector3(ExitSideOffset, 0.10f, 0f));
        var driver = Find(root, "Driverside");
        var passenger = Find(root, "Passengerside");
        if (driver != null) driver.localPosition = new Vector3(-ExitSideOffset, 0.10f, 0f);
        if (passenger != null) passenger.localPosition = new Vector3(ExitSideOffset, 0.10f, 0f);
    }

    private static void ConfigureSteeringReference(Transform root)
    {
        var controller = root.GetComponentInChildren<NWH.VehiclePhysics2.VehicleController>(true);
        if (controller == null)
            throw new InvalidOperationException("Native NWH controller is missing while configuring steering geometry.");

        var serialized = new SerializedObject(controller);
        SetRelativeObject(serialized, "steering.steeringWheel", null);
        SetRelativeNumber(serialized, "wheelbase", Wheelbase);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        var steeringReference = FindRelative(serialized, "steering.steeringWheel");
        var wheelbase = serialized.FindProperty("wheelbase");
        if (steeringReference?.objectReferenceValue != null || wheelbase == null ||
            wheelbase.propertyType != SerializedPropertyType.Float || Mathf.Abs(wheelbase.floatValue - Wheelbase) > 0.001f)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var members = controller.GetType().GetMembers(flags)
                .Where(member => member.Name.IndexOf("steer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 member.Name.IndexOf("wheelbase", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(member => member.MemberType + ":" + member.Name + ":" +
                    (member is System.Reflection.FieldInfo field ? field.FieldType.FullName :
                     member is System.Reflection.PropertyInfo property ? property.PropertyType.FullName : ""));
            throw new InvalidOperationException(
                $"Native NWH steering setup did not serialize: the static steering wheel must remain unbound, " +
                $"steering type={steeringReference?.propertyType}, " +
                $"reference value={steeringReference?.objectReferenceValue}; " +
                $"wheelbase type={wheelbase?.propertyType}, value={wheelbase?.floatValue}, expected={Wheelbase:F3}; " +
                $"members=[{string.Join(", ", members)}].");
        }
    }

    private static void EnsureSpawnAppearance(GameObject root)
    {
        if (root.GetComponent<LamborghiniAventadorSpawnAppearance>() == null)
            root.AddComponent<LamborghiniAventadorSpawnAppearance>();
    }

    private static Transform EnsureAnchor(Transform root, string name, Vector3 position)
    {
        var anchor = Find(root, name);
        if (anchor == null)
        {
            var go = new GameObject(name);
            anchor = go.transform;
            anchor.SetParent(root, false);
        }
        anchor.localPosition = position;
        anchor.localRotation = Quaternion.identity;
        return anchor;
    }

    private static bool TryGetBounds(Transform root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r != null && r.enabled).ToArray();
        if (renderers.Length == 0) { bounds = default; return false; }
        bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    private static Transform? Find(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => string.Equals(t.name, name, StringComparison.Ordinal));

    private static SerializedProperty? FindRelative(SerializedObject so, string path)
    {
        var parts = path.Split('.');
        var prop = so.FindProperty(parts[0]);
        for (var i = 1; i < parts.Length && prop != null; i++) prop = prop.FindPropertyRelative(parts[i]);
        return prop;
    }

    private static void SetRelativeNumber(SerializedObject so, string path, float value)
    {
        var prop = FindRelative(so, path);
        if (prop?.propertyType == SerializedPropertyType.Float) prop.floatValue = value;
        else if (prop?.propertyType == SerializedPropertyType.Integer || prop?.propertyType == SerializedPropertyType.Enum) prop.intValue = Mathf.RoundToInt(value);
    }

    private static void SetRelativeBool(SerializedObject so, string path, bool value)
    {
        var prop = FindRelative(so, path);
        if (prop?.propertyType == SerializedPropertyType.Boolean) prop.boolValue = value;
    }

    private static void SetRelativeObject(SerializedObject so, string path, UnityEngine.Object? value)
    {
        var property = FindRelative(so, path);
        if (property?.propertyType == SerializedPropertyType.ObjectReference)
            property.objectReferenceValue = value;
    }

    private static void SetString(SerializedObject so, string name, string value)
    {
        var prop = so.FindProperty(name);
        if (prop?.propertyType == SerializedPropertyType.String) prop.stringValue = value;
    }

    private static void SetBool(SerializedObject so, string name, bool value)
    {
        var prop = so.FindProperty(name);
        if (prop?.propertyType == SerializedPropertyType.Boolean) prop.boolValue = value;
    }

    private static void SetNumber(SerializedObject so, string name, float value)
    {
        var prop = so.FindProperty(name);
        if (prop?.propertyType == SerializedPropertyType.Float) prop.floatValue = value;
        else if (prop?.propertyType == SerializedPropertyType.Integer) prop.intValue = Mathf.RoundToInt(value);
    }
}
