#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class KiaCeedSportswagonSetup
{
    private const string ModRoot = "Assets/Mods/Kia_Ceed_Sportswagon";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/2022_kia_ceed_sportswagon_gt-plusline.glb";
    private const string LightOverlayModelPath =
        ModRoot + "/Models/GeneratedLights/KiaCeedSWLightOverlays.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string DamageBodyMeshPath =
        MeshFolder + "/KiaCeedSportswagonDamageBody.asset";
    private const string VehicleAssetPath = ModRoot + "/KiaCeedSportswagon.asset";
    private const string VehiclePrefabPath = ModRoot + "/KiaCeedSportswagon.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/KiaCeedSportswagon.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string VehicleBundleBaseName = "kiaceedsportswagon";
    private const string LightOverlayBundleBaseName = "kiaceedsportswagonlights";
    private const string BundleVariant = "unity3d";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/kiaceedsportswagon.unity3d";
    private const string WindowsLightOverlayBundlePath =
        ModRoot + "/AssetBundles/Windows/kiaceedsportswagonlights.unity3d";
    private const string MacBundlePath =
        ModRoot + "/AssetBundles/Mac/kiaceedsportswagon.unity3d";
    private const string MacLightOverlayBundlePath =
        ModRoot + "/AssetBundles/Mac/kiaceedsportswagonlights.unity3d";
    private const string VehicleTypeName =
        "kiaceedsportswagon-vehicle:vehicletype_kiaceedsportswagon";
    private const float TargetLength = 4.605f;
    private const float TargetWidth = 2.055f; // overall visual width incl. mirrors
    private const float TargetHeight = 1.460f;
    private const string DriverSeatAnchorName = "KiaCeedSportswagonDriverSeatAnchor";
    private const float FrontTrack = 1.559f;
    private const float RearTrack = 1.567f;
    private const float Wheelbase = 2.650f;
    // Match the imported Kia tire centers at +1.410 m front and -1.240 m rear.
    private const float FrontWheelForwardOffset = 0.085f;
    private const float RearWheelForwardOffset = 0.085f;
    private const float BodyVisualRaise = 0.010f;
    private const float FrontTireRadius = 0.31715f;
    private const float RearTireRadius = 0.31715f;
    private const float FrontTireWidth = 0.225f;
    private const float RearTireWidth = 0.225f;
    private const float WheelInset = 0.000f;
    private const float WheelCenterRideHeightOffset = 0f;
    // The runtime adds speed-squared aerodynamic drag; keep Unity's linear
    // Rigidbody drag at zero to avoid applying a second, nonphysical model.
    private const float VehicleLinearDrag = 0f;
    // Developer Tools telemetry measured about 29.3 m normalized to 100-0 km/h;
    // the requested 25% stronger-than-road-car target is about 25.5 m.
    private const float EnginePowerKw = 117.5f;
    private const float EngineSimulationPowerScale = 0.70f;
    private const float VehicleBrakeForce = 2165f;
    private const float BrakeMaxTorque = 2165f;
    private const float FrontForwardGrip = 1.00f;
    private const float RearForwardGrip = 1.04f;
    private const float FrontForwardStiffness = 1.08f;
    private const float RearForwardStiffness = 1.12f;
    private const float TireFrictionCircleStrength = 1.00f;
    private const float AntiRollBarForce = 5200f;
    private const float SuspensionReboundRate = 19000f;
    private const float FrontSuspensionTravel = 0.110f;
    private const float RearSuspensionTravel = 0.125f;
    private const float DeformationStrength = 0.19f;
    private const float DeformationRadius = 0.27f;
    private const float DeformationRandomness = 0.005f;
    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.14f, 0.16f);

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f + WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, Wheelbase * 0.5f + FrontWheelForwardOffset) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f - WheelInset, FrontTireRadius + WheelCenterRideHeightOffset, Wheelbase * 0.5f + FrontWheelForwardOffset) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f + WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f + RearWheelForwardOffset) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f - WheelInset, RearTireRadius + WheelCenterRideHeightOffset, -Wheelbase * 0.5f + RearWheelForwardOffset) },
        };
    private static readonly Vector3 FrontContactColliderCenter =
        new Vector3(0f, 0.48f, 1.91f);
    private static readonly Vector3 FrontContactColliderSize =
        new Vector3(1.78f, 0.56f, 0.98f);
    private static readonly Vector3 RearContactColliderCenter =
        new Vector3(0f, 0.48f, -1.96f);
    private static readonly Vector3 RearContactColliderSize =
        new Vector3(1.78f, 0.56f, 1.06f);

    private static readonly float[] CeedDctGears =
    {
        // D7UF1 final-drive groups: 1/2/4/5 use 4.294; 3/6/7/R use 3.174.
        -5.304f * 3.174f, 0f,
        3.929f * 4.294f, 2.318f * 4.294f,
        2.043f * 3.174f, 1.070f * 4.294f,
        0.822f * 4.294f, 0.884f * 3.174f, 0.721f * 3.174f,
    };

    private static AnimationCurve CreateCeedPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.12f, 0.10f),
            new Keyframe(0.23f, 0.34f),
            new Keyframe(0.38f, 0.57f),
            new Keyframe(0.54f, 0.79f),
            new Keyframe(0.69f, 0.88f),
            new Keyframe(0.85f, 1f),
            new Keyframe(0.93f, 0.94f),
            new Keyframe(1f, 0.80f));

    [MenuItem("Big Ambitions Mods/Setup Kia Ceed Sportswagon")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EnsurePrefabScriptsResolvable(ReferencePrefabPath);
        if (File.Exists(Path.Combine(Application.dataPath,
                VehiclePrefabPath.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar))))
            EnsurePrefabScriptsResolvable(VehiclePrefabPath);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            "KiaCeedSportswagon setup complete: generated a fitted Ceed Sportswagon with four " +
            "independent wheel visuals, seven-speed DCT, front-wheel drive, 1.5-litre turbo-four audio, functional lamp " +
            "geometry, colorable body/calipers, and corrected glass materials.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        BuildVehicleAndLightOverlayBundles();
        VerifyBuiltBundle();
    }

    public static void BuildStandaloneAssetBundles()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        BuildVehicleAndLightOverlayBundles();
        VerifyMacBundleFiles();
    }

    public static void RepairPrefabAndBuild()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        // This repair path only rebuilds the independent Blender-light bundle.
        // The main vehicle bundle contains serialized NWH components and must
        // not be rebuilt in an editor project where those scripts are missing.
        BuildLightOverlayBundleOnly();
        VerifyLightOverlayBundle();
    }

    private static void BuildLightOverlayBundleOnly()
    {
        var importer = AssetImporter.GetAtPath(LightOverlayModelPath);
        if (importer == null)
            throw new InvalidOperationException(
                $"Blender light overlay model was not imported at '{LightOverlayModelPath}'.");
        importer.SetAssetBundleNameAndVariant(LightOverlayBundleBaseName, BundleVariant);
        importer.SaveAndReimport();

        var projectTemp = Path.Combine(Path.GetTempPath(),
            "KiaCeedSportswagonLightOverlay_" + Guid.NewGuid().ToString("N"));
        try
        {
            var build = new AssetBundleBuild
            {
                assetBundleName = LightOverlayBundleBaseName,
                assetBundleVariant = BundleVariant,
                assetNames = new[] { LightOverlayModelPath },
            };
            BuildOverlayBundleForTarget(
                projectTemp, build, BuildTarget.StandaloneWindows64, "Windows", WindowsLightOverlayBundlePath);
            BuildOverlayBundleForTarget(
                projectTemp, build, BuildTarget.StandaloneOSX, "Mac", MacLightOverlayBundlePath);

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "KiaCeedSportswagon: built independent Windows and Mac light overlay bundles.");
        }
        finally
        {
            if (Directory.Exists(projectTemp))
                Directory.Delete(projectTemp, true);
        }
    }

    private static void BuildOverlayBundleForTarget(
        string projectTemp,
        AssetBundleBuild build,
        BuildTarget target,
        string platformFolder,
        string finalBundlePath)
    {
        var targetOutput = Path.Combine(projectTemp, platformFolder);
        Directory.CreateDirectory(targetOutput);
        var manifest = BuildPipeline.BuildAssetBundles(
            targetOutput,
            new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression,
            target);
        if (manifest == null)
            throw new InvalidOperationException($"Kia {platformFolder} light overlay AssetBundle build failed.");

        var producedBundle = Path.Combine(targetOutput, Path.GetFileName(finalBundlePath));
        if (!File.Exists(producedBundle))
            throw new FileNotFoundException(
                $"Kia {platformFolder} light overlay AssetBundle was not produced.", producedBundle);

        var outputDirectory = Path.GetDirectoryName(finalBundlePath) ??
                              throw new InvalidOperationException("Kia AssetBundle output directory is invalid.");
        Directory.CreateDirectory(outputDirectory);
        File.Copy(producedBundle, finalBundlePath, true);
        var producedManifest = producedBundle + ".manifest";
        if (File.Exists(producedManifest))
            File.Copy(producedManifest, finalBundlePath + ".manifest", true);
    }

    private static void VerifyLightOverlayBundle()
    {
        var bundle = AssetBundle.LoadFromFile(WindowsLightOverlayBundlePath);
        if (bundle == null)
            throw new InvalidOperationException(
                $"Could not load light overlay bundle '{WindowsLightOverlayBundlePath}'.");

        try
        {
            var overlayAsset = bundle.LoadAsset<GameObject>(LightOverlayModelPath);
            var vehiclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabPath);
            var visual = vehiclePrefab != null
                ? FindTransform(vehiclePrefab.transform, "KiaCeedSportswagonVisual")
                : null;
            if (overlayAsset == null || visual == null)
                throw new InvalidOperationException(
                    "Light overlay bundle or Kia visual prefab asset is missing.");
            VerifyLightOverlays(visual, overlayAsset);

            var overlayCount = 0;
            foreach (var filter in overlayAsset.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0 &&
                    filter.sharedMesh.triangles.Length > 0)
                    overlayCount++;
            if (overlayCount != 12)
                throw new InvalidOperationException(
                    $"Kia light overlay bundle contains {overlayCount}/12 non-empty overlays.");

            Debug.Log($"KiaCeedSportswagon: verified all {overlayCount} bundled lamp overlays.");
        }
        finally
        {
            bundle.Unload(false);
        }
    }

    private static void BuildVehicleAndLightOverlayBundles()
    {
        // ReferencePrefabPath is only needed by Generate(), which validates it before
        // copying. Rebuilding existing Kia assets must not be blocked by an unrelated
        // reference vehicle that is not included in either AssetBundle.
        EnsurePrefabScriptsResolvable(VehiclePrefabPath);

        var vehicleAssets = new List<string>();
        var lightAssets = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { ModRoot }))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath) ||
                assetPath.StartsWith(ModRoot + "/AssetBundles/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assetPath, ManifestPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assetPath, AssemblyPath, StringComparison.OrdinalIgnoreCase) ||
                assetPath.StartsWith(LocalesPath + "/", StringComparison.OrdinalIgnoreCase))
                continue;

            var extension = Path.GetExtension(assetPath);
            if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".meta", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(assetPath), "thumbnail.png", StringComparison.OrdinalIgnoreCase))
                continue;

            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
                continue;
            if (string.Equals(assetPath, LightOverlayModelPath, StringComparison.OrdinalIgnoreCase))
            {
                lightAssets.Add(assetPath);
                SetAssetBundle(importer, LightOverlayBundleBaseName);
            }
            else
            {
                vehicleAssets.Add(assetPath);
                SetAssetBundle(importer, VehicleBundleBaseName);
            }
        }

        if (!vehicleAssets.Contains(VehicleAssetPath) || !vehicleAssets.Contains(VehiclePrefabPath) ||
            lightAssets.Count != 1)
            throw new InvalidOperationException(
                $"Kia bundle inputs are incomplete: main={vehicleAssets.Count}, lights={lightAssets.Count}.");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var outputDirectory = Path.GetDirectoryName(WindowsBundlePath) ??
                              throw new InvalidOperationException("Kia AssetBundle output directory is invalid.");
        Directory.CreateDirectory(outputDirectory);
        var builds = new[]
        {
            new AssetBundleBuild
            {
                assetBundleName = VehicleBundleBaseName,
                assetBundleVariant = BundleVariant,
                assetNames = vehicleAssets.ToArray(),
            },
            new AssetBundleBuild
            {
                assetBundleName = LightOverlayBundleBaseName,
                assetBundleVariant = BundleVariant,
                assetNames = lightAssets.ToArray(),
            },
        };
        var manifest = BuildPipeline.BuildAssetBundles(
            outputDirectory,
            builds,
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);
        if (manifest == null || !File.Exists(WindowsBundlePath) || !File.Exists(WindowsLightOverlayBundlePath))
            throw new InvalidOperationException(
                "Kia vehicle and light overlay AssetBundles could not both be built.");

        BuildBundlesForPlatform(builds, BuildTarget.StandaloneOSX, "Mac");

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            $"KiaCeedSportswagon: built {vehicleAssets.Count} vehicle assets and " +
            $"{lightAssets.Count} independent light overlay asset(s).");
    }

    private static void BuildBundlesForPlatform(
        AssetBundleBuild[] builds, BuildTarget target, string platformFolder)
    {
        var outputDirectory = ModRoot + "/AssetBundles/" + platformFolder;
        Directory.CreateDirectory(outputDirectory);
        var manifest = BuildPipeline.BuildAssetBundles(
            outputDirectory,
            builds,
            BuildAssetBundleOptions.ChunkBasedCompression,
            target);
        var vehiclePath = outputDirectory + "/" + VehicleBundleBaseName + "." + BundleVariant;
        var lightsPath = outputDirectory + "/" + LightOverlayBundleBaseName + "." + BundleVariant;
        if (manifest == null || !File.Exists(vehiclePath) || !File.Exists(lightsPath))
            throw new InvalidOperationException(
                $"Kia vehicle and light overlay AssetBundles could not both be built for {platformFolder}.");

        foreach (var path in new[] { vehiclePath, lightsPath })
        {
            var bundleManifestPath = path + ".manifest";
            if (!File.Exists(bundleManifestPath) ||
                !BuildPipeline.GetCRCForAssetBundle(path, out var crc) ||
                !File.ReadAllText(bundleManifestPath).Contains("CRC: " + crc))
                throw new InvalidOperationException(
                    $"Kia {platformFolder} bundle manifest or CRC verification failed for '{path}'.");
        }

        Debug.Log($"KiaCeedSportswagon: built and CRC-verified both {platformFolder} AssetBundles.");
    }

    private static void SetAssetBundle(AssetImporter importer, string bundleBaseName)
    {
        if (string.Equals(importer.assetBundleName, bundleBaseName, StringComparison.Ordinal) &&
            string.Equals(importer.assetBundleVariant, BundleVariant, StringComparison.Ordinal))
            return;
        importer.SetAssetBundleNameAndVariant(bundleBaseName, BundleVariant);
        importer.SaveAndReimport();
    }

    private static void EnsurePrefabScriptsResolvable(string prefabPath)
    {
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        if (prefab == null)
            throw new InvalidOperationException($"Could not load prefab '{prefabPath}'.");
        try
        {
            var missingScripts = CountMissingScripts(prefab);
            if (missingScripts > 0)
                throw new InvalidOperationException(
                    $"Refusing to modify or rebuild '{prefabPath}': {missingScripts} MonoBehaviour script(s) are unresolved in this Unity project.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
    }

    private static int CountMissingScripts(GameObject root)
    {
        var count = 0;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
        return count;
    }

    public static void VerifyBuiltBundle()
    {
        var bundle = AssetBundle.LoadFromFile(WindowsBundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");
        var lightOverlayBundle = AssetBundle.LoadFromFile(WindowsLightOverlayBundlePath);
        if (lightOverlayBundle == null)
        {
            bundle.Unload(false);
            throw new InvalidOperationException(
                $"Could not load bundle '{WindowsLightOverlayBundlePath}'.");
        }

        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException("Bundle is missing its VehicleType or prefab.");
            var missingScripts = CountMissingScripts(prefab);
            if (missingScripts > 0)
                throw new InvalidOperationException(
                    $"Vehicle bundle has {missingScripts} missing MonoBehaviour script(s); refusing to verify it.");

            var vehicle = new SerializedObject(vehicleType);
            if (Math.Abs(ReadNumber(vehicle.FindProperty("price")) - 33590f) > 0.01f ||
                Math.Abs(ReadNumber(vehicle.FindProperty("maxFuel")) - 50f) > 0.01f ||
                Math.Abs(ReadNumber(vehicle.FindProperty("maxSpeed")) - 210f) > 0.01f ||
                Math.Abs(ReadNumber(vehicle.FindProperty("enginePower")) - 117.5f) > 0.01f ||
                (vehicle.FindProperty("isLuxuryCar")?.boolValue ?? true))
                throw new InvalidOperationException("Kia Ceed vehicle data does not match the researched GT-Line configuration.");

            var visual = FindTransform(prefab.transform, "KiaCeedSportswagonVisual") ??
                         throw new InvalidOperationException("Kia visual root is missing.");
            var lightOverlays = lightOverlayBundle.LoadAsset<GameObject>(LightOverlayModelPath);
            if (lightOverlays == null)
                throw new InvalidOperationException("Bundle is missing the Blender light overlay model.");
            VerifyLightOverlays(visual, lightOverlays);
            if (!TryGetModelBodyBounds(visual, out var bodyBounds))
                throw new InvalidOperationException("Kia body bounds are missing from the generated prefab.");
            var wheelCenterHeight = 0f;
            foreach (var corner in new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" })
            {
                var wheelController = FindTransform(prefab.transform, corner + "_WheelController") ??
                                      throw new InvalidOperationException($"Wheel controller for {corner} is missing.");
                wheelCenterHeight += wheelController.position.y * 0.25f;
            }
            if (bodyBounds.center.y <= wheelCenterHeight + 0.12f)
                throw new InvalidOperationException(
                    $"Kia body is not above its wheels: bodyCenterY={bodyBounds.center.y:0.000}, " +
                    $"wheelCenterY={wheelCenterHeight:0.000}.");
            var boundsProbe = UnityEngine.Object.Instantiate(prefab);
            Bounds bounds;
            try
            {
                if (!TryGetKiaCeedSportswagonRendererBounds(boundsProbe.transform, out bounds) ||
                    Math.Abs(bounds.size.x - TargetWidth) > 0.035f ||
                    Math.Abs(bounds.size.y - TargetHeight) > 0.035f ||
                    Math.Abs(bounds.size.z - TargetLength) > 0.035f)
                {
                    foreach (var renderer in boundsProbe.GetComponentsInChildren<Renderer>(true))
                    {
                        if (!KiaCeedSportswagonMaterials.IsKiaCeedSportswagonRenderer(renderer.transform))
                            continue;
                        var path = new Stack<string>();
                        for (var current = renderer.transform; current != null; current = current.parent)
                            path.Push(current.name);
                        var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        var materialNames = string.Join(",", Array.ConvertAll(
                            renderer.sharedMaterials,
                            material => material != null ? material.name : "<null>"));
                        Debug.LogWarning(
                            $"KiaCeedSportswagon bounds diagnostic: path={string.Join("/", path)}, " +
                            $"worldBounds={renderer.bounds}, localScale={renderer.transform.localScale}, " +
                            $"lossyScale={renderer.transform.lossyScale}, " +
                            $"mesh={(mesh != null ? mesh.name : renderer.GetType().Name)}, " +
                            $"materials={materialNames}.");
                    }
                    throw new InvalidOperationException($"Normalized Kia body bounds are incorrect: {bounds.size}.");
                }
                Debug.Log($"KiaCeedSportswagon: verified instantiated bundle bounds={bounds.size}.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(boundsProbe);
            }

            var mounts = new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };
            var tireVisualCount = 0;
            var fixedCaliperCount = 0;
            foreach (var corner in mounts)
            {
                var controller = FindTransform(prefab.transform, corner + "_WheelController") ??
                                 throw new InvalidOperationException($"NWH wheel controller for {corner} is missing.");
                var mount = FindTransform(prefab.transform, "KiaCeedSportswagonWheel" + corner) ??
                            throw new InvalidOperationException($"Moving Kia wheel visual for {corner} is missing.");
                if (!mount.IsChildOf(visual) && mount.parent != prefab.transform)
                    throw new InvalidOperationException($"Wheel visual {corner} is not attached to the vehicle.");
                var tireRenderers = 0;
                foreach (var renderer in mount.GetComponentsInChildren<MeshRenderer>(true))
                    if (HasMaterialMarker(renderer, "tyre")) tireRenderers++;
                var expectedWheelPosition = WheelControllerPositions[corner + "_WheelController"];
                if (tireRenderers != 1)
                {
                    throw new InvalidOperationException(
                        $"Wheel/tire setup is incomplete for {corner}: tireRenderers={tireRenderers}.");
                }
                if (Vector3.Distance(controller.localPosition, expectedWheelPosition) > 0.005f)
                    Debug.LogWarning(
                        $"KiaCeedSportswagon: {corner} controller is runtime-positioned at spawn; " +
                        $"prefab={controller.localPosition}, runtime={expectedWheelPosition}.");
                var geometryName = "Geometry_KiaCeedSportswagonWheel_" +
                                   (corner.StartsWith("Front", StringComparison.Ordinal) ? "F" : "R") +
                                   (corner.EndsWith("Left", StringComparison.Ordinal) ? "L" : "R");
                var geometry = FindTransform(prefab.transform, geometryName) ??
                               throw new InvalidOperationException($"Authored Kia wheel geometry '{geometryName}' is missing.");
                var outward = corner.EndsWith("Left", StringComparison.Ordinal)
                    ? -prefab.transform.right
                    : prefab.transform.right;
                if (Vector3.Dot(geometry.right, outward) < 0.98f ||
                    Vector3.Dot(geometry.up, prefab.transform.up) < 0.98f)
                {
                    throw new InvalidOperationException(
                        $"Wheel face is not outward and upright for {corner}: " +
                        $"axle={geometry.right}, up={geometry.up}.");
                }
                tireVisualCount += tireRenderers;
                if (corner.StartsWith("Front", StringComparison.Ordinal))
                {
                    var caliper = FindTransform(prefab.transform, "KiaCeedSportswagonFixedCaliper" + corner);
                    if (caliper == null || Vector3.Distance(caliper.localPosition, controller.localPosition) > 0.005f)
                        throw new InvalidOperationException($"Fixed caliper placement is invalid for {corner}.");
                    fixedCaliperCount++;
                }
            }
            var rearBrakes = FindTransform(visual, "KiaCeedSportswagonRearBrakeAssembly");
            var rearBrakeRenderers = rearBrakes?.GetComponentsInChildren<MeshRenderer>(true) ??
                                     Array.Empty<MeshRenderer>();
            var rearCaliperFound = false;
            var rearDiskFound = false;
            var rearRotorFound = false;
            foreach (var renderer in rearBrakeRenderers)
            {
                rearCaliperFound |= HasMaterialMarker(renderer, "chrome_R");
                rearDiskFound |= HasMaterialMarker(renderer, "disk");
                rearRotorFound |= HasMaterialMarker(renderer, "metal_BR");
            }
            if (tireVisualCount != 4 || fixedCaliperCount != 2 ||
                rearBrakeRenderers.Length < 3 || !rearCaliperFound || !rearDiskFound || !rearRotorFound)
                throw new InvalidOperationException(
                    "Expected four fitted tires, two front calipers and the supplied full-width rear brake meshes.");

            if (FindTransform(prefab.transform, "Geometry_KiaCeedSportswagonWheel_FL") == null ||
                FindTransform(prefab.transform, "Geometry_KiaCeedSportswagonWheel_FR") == null ||
                FindTransform(prefab.transform, "Geometry_KiaCeedSportswagonWheel_RL") == null ||
                FindTransform(prefab.transform, "Geometry_KiaCeedSportswagonWheel_RR") == null)
                throw new InvalidOperationException("Authored Kia FL/FR/RL/RR wheel mapping was lost.");
            if (FindTransform(prefab.transform, DriverSeatAnchorName) == null)
                throw new InvalidOperationException("Kia driver seat anchor is missing.");

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var bodyPaintSlots = 0;
            var missingMaterials = new List<string>();
            foreach (var renderer in renderers)
            {
                if (KiaCeedSportswagonMaterials.IsKiaCeedSportswagonRenderer(renderer.transform))
                {
                    var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                    if (mesh != null && mesh.subMeshCount > 0 &&
                        (renderer.enabled || string.Equals(
                            renderer.name, "KiaCeedSportswagonDamageBody", StringComparison.Ordinal)))
                    {
                        var assignedMaterials = renderer.sharedMaterials;
                        if (assignedMaterials.Length < mesh.subMeshCount)
                        {
                            missingMaterials.Add(renderer.name);
                        }
                        else
                        {
                            for (var index = 0; index < mesh.subMeshCount; index++)
                                if (assignedMaterials[index] == null)
                                    missingMaterials.Add(renderer.name);
                        }
                    }
                }
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && IsBodyPaintMaterial(material)) bodyPaintSlots++;
            }
            if (missingMaterials.Count > 0)
                throw new InvalidOperationException(
                    $"Bundle has {missingMaterials.Count} Kia mesh renderer(s) with missing materials: " +
                    string.Join(", ", missingMaterials.GetRange(0, Math.Min(8, missingMaterials.Count))) + ".");
            if (bodyPaintSlots < 1)
                throw new InvalidOperationException("No colorable CarPaint body material was found.");

            var deformationFound = false;
            var deformationBound = false;
            var nwhFound = false;
            var fwdVerified = false;
            var transmissionVerified = false;
            var engineVerified = false;
            foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) continue;
                if (string.Equals(component.GetType().Name, "VehicleDeformationController", StringComparison.Ordinal))
                {
                    deformationFound = true;
                    var serialized = new SerializedObject(component);
                    var filters = serialized.FindProperty("meshFilters");
                    if (filters != null && filters.isArray)
                        for (var index = 0; index < filters.arraySize; index++)
                            deformationBound |= filters.GetArrayElementAtIndex(index).objectReferenceValue is MeshFilter filter &&
                                               filter.name.StartsWith("KiaCeedSportswagonDamageBody", StringComparison.Ordinal);
                }
                if (!string.Equals(component.GetType().FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
                    continue;

                nwhFound = true;
                var serializedNwh = new SerializedObject(component);
                var powertrain = serializedNwh.FindProperty("powertrain");
                var engine = powertrain?.FindPropertyRelative("engine");
                var engineCurve = engine?.FindPropertyRelative("powerCurve")?.animationCurveValue;
                engineVerified = Math.Abs(ReadNumber(engine?.FindPropertyRelative("maxPower")) - 117.5f) < 0.1f &&
                                 Math.Abs(ReadNumber(engine?.FindPropertyRelative("idleRPM")) - 750f) < 0.1f &&
                                 Math.Abs(ReadNumber(engine?.FindPropertyRelative("revLimiterRPM")) - 6500f) < 0.1f &&
                                 CeedPowerCurveMatches(engineCurve);
                var transmission = powertrain?.FindPropertyRelative("transmission");
                var gears = transmission?.FindPropertyRelative("gears");
                var gearRatiosMatch = gears != null && gears.isArray &&
                                      gears.arraySize == CeedDctGears.Length;
                if (gearRatiosMatch)
                {
                    for (var index = 0; index < CeedDctGears.Length; index++)
                    {
                        if (Math.Abs(gears!.GetArrayElementAtIndex(index).floatValue -
                                     CeedDctGears[index]) > 0.002f)
                        {
                            gearRatiosMatch = false;
                            break;
                        }
                    }
                }
                transmissionVerified = transmission?.FindPropertyRelative("forwardGearCount")?.intValue == 7 &&
                                       transmission?.FindPropertyRelative("reverseGearCount")?.intValue == 1 &&
                                       gearRatiosMatch;
                var differentials = powertrain?.FindPropertyRelative("differentials");
                if (differentials != null && differentials.isArray)
                {
                    for (var index = 0; index < differentials.arraySize; index++)
                    {
                        var differential = differentials.GetArrayElementAtIndex(index);
                        if (string.Equals(differential.FindPropertyRelative("name")?.stringValue,
                                "Center Differential", StringComparison.Ordinal))
                            fwdVerified = Math.Abs(ReadNumber(differential.FindPropertyRelative("biasAB"))) < 0.001f;
                    }
                }
            }

            var damageMesh = FindTransform(prefab.transform, "KiaCeedSportswagonDamageBody");
            var bodyCollider = FindTransform(prefab.transform, "BodyCollider");
            if (damageMesh?.GetComponent<MeshFilter>()?.sharedMesh == null)
                throw new InvalidOperationException("The Kia exterior damage mesh is missing.");
            if (deformationFound && !deformationBound)
                throw new InvalidOperationException("Exterior impact deformation is not bound to the Kia body mesh.");
            if (!deformationFound)
                Debug.LogWarning(
                    "KiaCeedSportswagon: NWH deformation scripts are not imported in this Unity project; " +
                    "their existing serialized prefab references were preserved but could not be inspected.");
            if (bodyCollider == null || bodyCollider.GetComponents<Collider>().Length < 4)
                throw new InvalidOperationException("Body and bumper safety colliders are incomplete.");
            var rearParkingCollider = FindTransform(prefab.transform, "RearParallelParkingCollider")
                ?.GetComponent<BoxCollider>();
            var parkingHelperCollider = FindTransform(prefab.transform, "VehicleParkingHelper")
                ?.GetComponent<SphereCollider>();
            var totalColliders = prefab.GetComponentsInChildren<Collider>(true).Length;
            if (rearParkingCollider == null || !rearParkingCollider.isTrigger ||
                parkingHelperCollider == null || totalColliders < 6)
                throw new InvalidOperationException(
                    $"Vehicle/parking collider safeguards are incomplete: total={totalColliders}, " +
                    $"rearParkingTrigger={(rearParkingCollider != null && rearParkingCollider.isTrigger)}, " +
                    $"parkingHelper={parkingHelperCollider != null}.");
            if (nwhFound && (!engineVerified || !transmissionVerified || !fwdVerified))
                throw new InvalidOperationException("NWH engine, 7DCT or front-wheel-drive setup is incomplete.");
            if (!nwhFound)
                Debug.LogWarning(
                    "KiaCeedSportswagon: NWH drivetrain scripts are not imported in this Unity project; " +
                    "their existing serialized prefab configuration was preserved but could not be inspected.");

            var templateBeam = FindTransform(prefab.transform, "Spotlights")?.GetComponent<Light>();
            if (templateBeam == null || templateBeam.enabled)
                throw new InvalidOperationException("Disabled road-light template is missing.");
            Debug.Log(
                $"KiaCeedSportswagon bundle verification passed: dimensions={bounds.size}, " +
                $"engine={ReadNumber(vehicle.FindProperty("enginePower")):0.0}kW, " +
                $"speed={ReadNumber(vehicle.FindProperty("maxSpeed")):0}km/h, " +
                $"FWD/7DCT, wheels={tireVisualCount}, frontCalipers={fixedCaliperCount}, " +
                $"rearBrakeMeshes={rearBrakeRenderers.Length}, " +
                $"paintSlots={bodyPaintSlots}, damageMesh=present, bodyColliders={bodyCollider.GetComponents<Collider>().Length}.");
        }
        finally
        {
            bundle.Unload(false);
            lightOverlayBundle.Unload(false);
        }

        VerifyMacBundleFiles();
    }

    private static void VerifyMacBundleFiles()
    {
        foreach (var path in new[] { MacBundlePath, MacLightOverlayBundlePath })
        {
            if (!File.Exists(path) || !File.Exists(path + ".manifest"))
                throw new InvalidOperationException($"Kia Mac bundle or manifest is missing: '{path}'.");
            if (!BuildPipeline.GetCRCForAssetBundle(path, out var crc) ||
                !File.ReadAllText(path + ".manifest").Contains("CRC: " + crc))
                throw new InvalidOperationException($"Kia Mac bundle CRC does not match its manifest: '{path}'.");
        }

        var platformManifestPath = ModRoot + "/AssetBundles/Mac/Mac.manifest";
        if (!File.Exists(platformManifestPath))
            throw new InvalidOperationException("Kia Mac platform manifest is missing.");
        var platformManifest = File.ReadAllText(platformManifestPath);
        if (!platformManifest.Contains("Name: " + VehicleBundleBaseName + "." + BundleVariant) ||
            !platformManifest.Contains("Name: " + LightOverlayBundleBaseName + "." + BundleVariant))
            throw new InvalidOperationException("Kia Mac platform manifest does not list both runtime bundles.");

        Debug.Log("KiaCeedSportswagon: verified Mac vehicle and light overlay bundles, CRCs, and manifest entries.");
    }

    private static UnityEngine.Object CreateVehicleType()
    {
        var vehicleTypeClass = FindVehicleTypeClass();
        var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (target == null)
        {
            if (!AssetDatabase.CopyAsset(ReferenceAssetPath, VehicleAssetPath))
                throw new InvalidOperationException(
                    "Could not create the Kia VehicleType from the Audi reference asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }

        if (target != null && target.GetType() != vehicleTypeClass)
            throw new InvalidOperationException(
                $"Refusing to replace '{VehicleAssetPath}': its serialized VehicleType script is unresolved " +
                $"(loaded {target.GetType().FullName}, expected {vehicleTypeClass.FullName}).");

        if (target == null)
            throw new InvalidOperationException("Generated KiaCeedSportswagon VehicleType asset did not load.");

        target.name = "KiaCeedSportswagon";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 33590f);
        SetNumber(serialized, "maxFuel", 50f);
        SetNumber(serialized, "maxCargoCapacity", 16f);
        SetNumber(serialized, "maxSpeed", 210f);
        SetNumber(serialized, "enginePower", 117.5f);
        SetNumber(serialized, "brakeForce", VehicleBrakeForce);
        // VehicleType.turnRadius is a game steering-tune value, not meters of
        // published turning-circle diameter. The Ceed's compact FWD setup uses
        // the normal passenger-car range.
        SetNumber(serialized, "turnRadius", 25f);
        SetNumber(serialized, "damageIntensity", 0.32f);
        SetBool(serialized, "isATruck", false);
        SetBool(serialized, "isHandVehicle", false);
        SetBool(serialized, "fitsHandTruck", false);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", true);
        SetBool(serialized, "taxDeductible", false);
        SetBool(serialized, "hasRadio", true);
        SetBool(serialized, "isLuxuryCar", false);
        SetNumber(serialized, "destinationsThatCanDeliver", 2f);
        SetBool(serialized, "countsForPersonalGoals", true);
        SetBool(serialized, "spawnInPlayerObject", false);
        SetBool(serialized, "usePedestrianCam", false);
        SetNumber(serialized, "autoDestroyAfterMinutes", -1f);
        SetBool(serialized, "enclosed", true);
        SetBool(serialized, "canGetDirty", true);
        SetNumber(serialized, "dirtinessTimer", 600f);
        SetNumber(serialized, "cleanByRainTimer", 360f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        return target;
    }

    private static Type FindVehicleTypeClass()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("Vehicles.VehicleTypes.VehicleType", false);
            if (type != null && typeof(ScriptableObject).IsAssignableFrom(type))
                return type;
        }

        throw new InvalidOperationException(
            "Vehicles.VehicleTypes.VehicleType could not be resolved from the loaded game assemblies.");
    }

    private static void CreateVehiclePrefab(UnityEngine.Object vehicleType)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePrefabPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R reference prefab was not found.");
        if (model == null)
            throw new InvalidOperationException("KiaCeedSportswagon 2022 GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "KiaCeedSportswagon";
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
                throw new InvalidOperationException("Could not instantiate the KiaCeedSportswagon model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "KiaCeedSportswagonVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureExitMarkers(root, modelInstance);
            AssignPersistentMaterials(modelInstance);
            AttachWheelVisuals(root, modelInstance);
            ConfigureLightOverlays(modelInstance.transform);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);

            Debug.Log(
                "KiaCeedSportswagon: kept persistent authored materials on the prefab; " +
                "per-instance glass materials are prepared by the runtime vehicle lifecycle.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the KiaCeedSportswagon vehicle prefab.");
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

    private static Transform FindModelVisualRoot(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, "KiaCeedSportswagonVisual", StringComparison.Ordinal))
                return transform;
        throw new InvalidOperationException("Kia visual root is missing from the vehicle prefab.");
    }

    private static void VerifyLightOverlays(Transform modelRoot, GameObject overlayAsset)
    {
        var definitions = new[]
        {
            new[] { "Int_Sportswagon_base_chrome.002_chrome.001_0", "KiaCeedSWLight_Headlamps", "Headlamps" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_FrontDRL_Left", "FrontDRL_Left" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_FrontDRL_Right", "FrontDRL_Right" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_FrontIndicator_Left", "FrontIndicator_Left" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_FrontIndicator_Right", "FrontIndicator_Right" },
            new[] { "Ext_GTL_taillights_chromered__linepattern.001_chrome_line_0", "KiaCeedSWLight_MirrorIndicator_Left", "MirrorIndicator_Left" },
            new[] { "Ext_GTL_taillights_chromered__linepattern.001_chrome_line_0", "KiaCeedSWLight_MirrorIndicator_Right", "MirrorIndicator_Right" },
            new[] { "Ext_GTL_taillights_chromered__linepattern.001_chrome_line_0", "KiaCeedSWLight_RearIndicator_Left", "RearIndicator_Left" },
            new[] { "Ext_GTL_taillights_chromered__linepattern.001_chrome_line_0", "KiaCeedSWLight_RearIndicator_Right", "RearIndicator_Right" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_RearTailLights", "RearTailLights" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_RearBrakeLights", "RearBrakeLights" },
            new[] { "Ext_Sportswagon_base_upperrearlights_chromered__squarepatte_squarepattern.001_0", "KiaCeedSWLight_ReverseLights", "ReverseLights" },
        };

        var sourceFilters = overlayAsset.GetComponentsInChildren<MeshFilter>(true);
        foreach (var definition in definitions)
        {
            var anchor = FindUniqueTransform(modelRoot, definition[0]);
            var source = FindUniqueMeshFilter(sourceFilters, definition[1]);
            var productionMesh = anchor.GetComponent<MeshFilter>()?.sharedMesh;
            if (productionMesh == null || !ContainsBounds(productionMesh.bounds, source.sharedMesh.bounds, 0.002f) ||
                source.sharedMesh.vertexCount == 0 || source.sharedMesh.triangles.Length == 0)
                throw new InvalidOperationException(
                    $"Blender lamp overlay '{definition[2]}' is empty or no longer aligns with '{definition[0]}'.");
        }

        Debug.Log(
            $"KiaCeedSportswagon: verified {definitions.Length} Blender light overlays for runtime attachment.");
    }

    private static void ConfigureLightOverlays(Transform modelRoot)
    {
        var overlayAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LightOverlayModelPath);
        if (overlayAsset == null)
            throw new InvalidOperationException(
                $"Blender light overlay model was not imported at '{LightOverlayModelPath}'.");

        var importer = AssetImporter.GetAtPath(LightOverlayModelPath);
        if (importer != null &&
            (!string.Equals(importer.assetBundleName, LightOverlayBundleBaseName, StringComparison.Ordinal) ||
             !string.Equals(importer.assetBundleVariant, BundleVariant, StringComparison.Ordinal)))
        {
            importer.assetBundleName = LightOverlayBundleBaseName;
            importer.assetBundleVariant = BundleVariant;
            importer.SaveAndReimport();
            overlayAsset = AssetDatabase.LoadAssetAtPath<GameObject>(LightOverlayModelPath);
        }

        var sourceFilters = overlayAsset.GetComponentsInChildren<MeshFilter>(true);
        var definitions = new[]
        {
            new[] { "Int_Sportswagon_base_chrome.002_chrome.001_0", "KiaCeedSWLight_Headlamps", "Headlamps" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_FrontDRL_Left", "FrontDRL_Left" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_FrontDRL_Right", "FrontDRL_Right" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_FrontIndicator_Left", "FrontIndicator_Left" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_FrontIndicator_Right", "FrontIndicator_Right" },
            new[] { "Ext_GTL_taillights_chromered__linepattern.001_chrome_line_0", "KiaCeedSWLight_MirrorIndicator_Left", "MirrorIndicator_Left" },
            new[] { "Ext_GTL_taillights_chromered__linepattern.001_chrome_line_0", "KiaCeedSWLight_MirrorIndicator_Right", "MirrorIndicator_Right" },
            new[] { "Ext_GTL_taillights_chromered__linepattern.001_chrome_line_0", "KiaCeedSWLight_RearIndicator_Left", "RearIndicator_Left" },
            new[] { "Ext_GTL_taillights_chromered__linepattern.001_chrome_line_0", "KiaCeedSWLight_RearIndicator_Right", "RearIndicator_Right" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_RearTailLights", "RearTailLights" },
            new[] { "LightsMain__boot.001_light_glass_0", "KiaCeedSWLight_RearBrakeLights", "RearBrakeLights" },
            new[] { "Ext_Sportswagon_base_upperrearlights_chromered__squarepatte_squarepattern.001_0", "KiaCeedSWLight_ReverseLights", "ReverseLights" },
        };

        foreach (var definition in definitions)
        {
            var anchor = FindUniqueTransform(modelRoot, definition[0]);
            var source = FindUniqueMeshFilter(sourceFilters, definition[1]);
            var productionMesh = anchor.GetComponent<MeshFilter>()?.sharedMesh ??
                                 throw new InvalidOperationException(
                                     $"Production lamp anchor '{definition[0]}' has no mesh.");
            if (!ContainsBounds(productionMesh.bounds, source.sharedMesh.bounds, 0.002f))
                throw new InvalidOperationException(
                    $"Blender overlay '{definition[2]}' no longer aligns with production mesh '{definition[0]}'.");
            var hostName = "KiaCeedSportswagonLightOverlay_" + definition[2];
            var existing = anchor.Find(hostName);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);

            var host = new GameObject(hostName);
            host.layer = anchor.gameObject.layer;
            host.transform.SetParent(anchor, false);
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;
            // The source mesh belongs to the separate light bundle. Keep no cross-bundle
            // mesh reference serialized into the vehicle prefab; the runtime binder fills it.
            host.AddComponent<MeshFilter>();
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = Array.Empty<Material>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            Debug.Log(
                $"KiaCeedSportswagon: attached labeled lamp overlay '{definition[2]}' " +
                $"to '{definition[0]}' with {source.sharedMesh.vertexCount} vertices and " +
                $"{source.sharedMesh.triangles.Length / 3} triangles; " +
                $"overlayBounds={source.sharedMesh.bounds} productionBounds={productionMesh.bounds}.");
        }
    }

    private static bool ContainsBounds(Bounds outer, Bounds inner, float tolerance)
    {
        var outerMin = outer.min - Vector3.one * tolerance;
        var outerMax = outer.max + Vector3.one * tolerance;
        var innerMin = inner.min;
        var innerMax = inner.max;
        return innerMin.x >= outerMin.x && innerMin.y >= outerMin.y && innerMin.z >= outerMin.z &&
               innerMax.x <= outerMax.x && innerMax.y <= outerMax.y && innerMax.z <= outerMax.z;
    }

    private static Transform FindUniqueTransform(Transform root, string name)
    {
        Transform? match = null;
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (!string.Equals(transform.name, name, StringComparison.Ordinal))
                continue;
            if (match != null)
                throw new InvalidOperationException($"More than one Kia overlay anchor is named '{name}'.");
            match = transform;
        }

        return match ?? throw new InvalidOperationException($"Kia overlay anchor '{name}' is missing.");
    }

    private static MeshFilter FindUniqueMeshFilter(MeshFilter[] filters, string name)
    {
        MeshFilter? match = null;
        foreach (var filter in filters)
        {
            if (filter == null || !string.Equals(filter.gameObject.name, name, StringComparison.Ordinal))
                continue;
            if (match != null)
                throw new InvalidOperationException($"More than one Blender overlay is named '{name}'.");
            match = filter;
        }

        if (match == null || match.sharedMesh == null)
            throw new InvalidOperationException($"Blender overlay mesh '{name}' is missing.");
        return match;
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        body.mass = 1450f;
        body.drag = VehicleLinearDrag;
        body.angularDrag = 1.9f;
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
                if (component == null)
                {
                    Debug.LogWarning(
                        $"KiaCeedSportswagon setup skipped a missing script on wheel controller '{pair.Key}'.");
                    continue;
                }

                var serialized = new SerializedObject(component);
                SetRelativeNumber(
                    serialized,
                    "spring.maxLength",
                    isFront ? FrontSuspensionTravel : RearSuspensionTravel);
                SetRelativeNumber(serialized, "spring.maxForce", 19000f);
                SetRelativeNumber(serialized, "damper.reboundRate", SuspensionReboundRate);
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
            var mount = FindTransform(root.transform, "KiaCeedSportswagonWheel" + corner) ??
                        throw new InvalidOperationException($"Rolling visual for '{corner}' is missing.");

            controller.localPosition = pair.Value;
            mount.SetParent(root.transform, true);
            mount.localPosition = pair.Value;
            OrientWheelVisual(mount, corner.EndsWith("Left", StringComparison.Ordinal));
            if (corner.StartsWith("Front", StringComparison.Ordinal))
            {
                var caliper = FindTransform(root.transform, "KiaCeedSportswagonFixedCaliper" + corner) ??
                              throw new InvalidOperationException($"Fixed caliper for '{corner}' is missing.");
                caliper.localPosition = pair.Value;
            }
            else if (FindTransform(root.transform, "KiaCeedSportswagonRearBrakeAssembly") == null)
            {
                throw new InvalidOperationException("Combined GLB rear brake assembly is missing.");
            }
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
            var mount = FindTransform(root.transform, "KiaCeedSportswagonWheel" + corner) ??
                        throw new InvalidOperationException($"Rolling visual for '{corner}' is missing.");
            var tireCount = 0;
            foreach (var renderer in mount.GetComponentsInChildren<MeshRenderer>(true))
                if (HasMaterialMarker(renderer, "tyre")) tireCount++;
            if (tireCount != 1)
                throw new InvalidOperationException($"Expected one authored tire under '{corner}', found {tireCount}.");
            mount.SetParent(root.transform, true);
            mount.localPosition = pair.Value;
            controller.localPosition = pair.Value;
            OrientWheelVisual(mount, corner.EndsWith("Left", StringComparison.Ordinal));
            AssignWheelVisual(controller, mount.gameObject);
        }
    }

    private static void ConfigureExitMarkers(GameObject root, GameObject model)
    {
        var steeringWheel = FindTransformWithNameFragment(model.transform, "steeringwheel") ??
                            throw new InvalidOperationException("Model steering wheel is missing.");
        var steeringWorldPosition = steeringWheel.position;
        if (TryGetRendererBounds(steeringWheel, out var steeringBounds))
            steeringWorldPosition = steeringBounds.center;
        var steeringPosition = root.transform.InverseTransformPoint(steeringWorldPosition);
        var driverSide = steeringPosition.x < 0f ? -1.45f : 1.45f;
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.1f, 0f));
        SetLocalPosition(root, "Passengerside", new Vector3(-driverSide, 0.1f, 0f));
        var seatAnchor = FindTransform(root.transform, DriverSeatAnchorName);
        if (seatAnchor == null)
        {
            seatAnchor = new GameObject(DriverSeatAnchorName).transform;
            seatAnchor.SetParent(root.transform, false);
        }
        seatAnchor.localPosition = steeringPosition;
        seatAnchor.localRotation = Quaternion.identity;
        Debug.Log(
            $"KiaCeedSportswagon: steering wheel x={steeringPosition.x:F3}; " +
            $"driver exit x={driverSide:F2}, seatAnchor={seatAnchor.localPosition}.");
    }

    private static void OrientWheelVisual(Transform mount, bool isLeft)
    {
        Transform? geometry = mount.name.StartsWith(
            "Geometry_KiaCeedSportswagonWheel_", StringComparison.Ordinal) ? mount : null;
        for (var index = 0; geometry == null && index < mount.childCount; index++)
        {
            var child = mount.GetChild(index);
            if (!child.name.StartsWith("Geometry_KiaCeedSportswagonWheel_", StringComparison.Ordinal))
                continue;
            geometry = child;
            break;
        }
        if (geometry == null)
            throw new InvalidOperationException($"Kia wheel geometry under '{mount.name}' is missing.");

        // Normalize the imported mesh pivot to Y-up and face each rim outward.
        geometry.localRotation = isLeft ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
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
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1050f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 450f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 450f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.13f);
                SetRelativeNumber(
                    serialized,
                    "powertrain.engine.maxPower",
                    EnginePowerKw * EngineSimulationPowerScale);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateCeedPowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 750f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 6500f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.38f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", true);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0.28f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 7f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.18f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 3400f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 6200f);
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
                    // The reference center differential's A output is the front axle.
                    bias.floatValue = 0f;
                    rearDriveConfigured = true;
                }
                if (!rearDriveConfigured)
                    throw new InvalidOperationException("Center differential could not be configured for FWD.");

                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = CeedDctGears.Length;
                for (var index = 0; index < CeedDctGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = CeedDctGears[index];
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeNumber(serialized, "module.speedLimit", 210f);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (!found)
            throw new InvalidOperationException("NWH vehicle controller was not found on the reference prefab.");
    }

    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        // Derive vehicle axes from all four authored tire centers. Bounding-box
        // dimensions alone selected an upside-down orientation for this GLB.
        var wheelCenters = new Vector3[4];
        var wheelNames = new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
        for (var index = 0; index < wheelNames.Length; index++)
        {
            if (!TryGetWheelBounds(model.transform, wheelNames[index], out var wheelBounds))
                throw new InvalidOperationException($"KiaCeedSportswagon tire '{wheelNames[index]}' could not be measured.");
            wheelCenters[index] = wheelBounds.center;
        }

        var frontCenter = (wheelCenters[0] + wheelCenters[1]) * 0.5f;
        var rearCenter = (wheelCenters[2] + wheelCenters[3]) * 0.5f;
        var leftCenter = (wheelCenters[0] + wheelCenters[2]) * 0.5f;
        var rightCenter = (wheelCenters[1] + wheelCenters[3]) * 0.5f;
        var forward = (frontCenter - rearCenter).normalized;
        var right = Vector3.ProjectOnPlane(rightCenter - leftCenter, forward).normalized;
        var up = Vector3.Cross(forward, right).normalized;
        if (forward.sqrMagnitude < 0.9f || right.sqrMagnitude < 0.9f || up.sqrMagnitude < 0.9f)
            throw new InvalidOperationException("KiaCeedSportswagon wheel nodes do not define usable vehicle axes.");

        model.transform.localRotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
        if (!TryGetModelBodyBounds(model.transform, out var bounds) ||
            !TryGetRendererBounds(model.transform, out var fullBounds))
            throw new InvalidOperationException("Oriented KiaCeedSportswagon bounds could not be measured.");

        for (var index = 0; index < wheelNames.Length; index++)
        {
            if (!TryGetWheelBounds(model.transform, wheelNames[index], out var orientedWheelBounds))
                throw new InvalidOperationException($"Oriented Kia tire '{wheelNames[index]}' could not be measured.");
            wheelCenters[index] = orientedWheelBounds.center;
        }

        var orientedFront = (wheelCenters[0].z + wheelCenters[1].z) * 0.5f;
        var orientedRear = (wheelCenters[2].z + wheelCenters[3].z) * 0.5f;
        var orientedLeft = (wheelCenters[0].x + wheelCenters[2].x) * 0.5f;
        var orientedRight = (wheelCenters[1].x + wheelCenters[3].x) * 0.5f;
        var averageWheelHeight = (wheelCenters[0].y + wheelCenters[1].y +
                                  wheelCenters[2].y + wheelCenters[3].y) * 0.25f;
        if (orientedFront <= orientedRear || orientedRight <= orientedLeft ||
            averageWheelHeight >= fullBounds.center.y - fullBounds.size.y * 0.12f)
        {
            throw new InvalidOperationException(
                "KiaCeedSportswagon GLB axes failed the upright/front/side check: " +
                $"frontDelta={orientedFront - orientedRear:0.000}, " +
                $"rightDelta={orientedRight - orientedLeft:0.000}, " +
                $"wheelHeight={averageWheelHeight:0.000}, boundsCenterY={fullBounds.center.y:0.000}.");
        }

        var worldScale = new Vector3(
            TargetWidth / bounds.size.x,
            TargetHeight / bounds.size.y,
            TargetLength / bounds.size.z);
        var worldRotation = model.transform.rotation;
        var scale = new Vector3(
            worldScale[DominantWorldAxis(worldRotation * Vector3.right)],
            worldScale[DominantWorldAxis(worldRotation * Vector3.up)],
            worldScale[DominantWorldAxis(worldRotation * Vector3.forward)]);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled KiaCeedSportswagon bounds could not be measured.");
        if (Math.Abs(bounds.size.x - TargetWidth) > 0.035f ||
            Math.Abs(bounds.size.y - TargetHeight) > 0.035f ||
            Math.Abs(bounds.size.z - TargetLength) > 0.035f)
        {
            throw new InvalidOperationException($"Axis-corrected Kia model scale produced unexpected bounds: {bounds.size}.");
        }

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        model.transform.localPosition += Vector3.up * BodyVisualRaise;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final KiaCeedSportswagon bounds could not be measured.");

        Debug.Log(
            $"KiaCeedSportswagon: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}, " +
            $"wheelHeight={averageWheelHeight:0.000}, upright=true.");
    }

    private static int DominantWorldAxis(Vector3 direction)
    {
        var absolute = new Vector3(Math.Abs(direction.x), Math.Abs(direction.y), Math.Abs(direction.z));
        if (absolute.x >= absolute.y && absolute.x >= absolute.z)
            return 0;
        return absolute.y >= absolute.z ? 1 : 2;
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var wheelNames = new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
        if (wheelNames.Length != 4)
            throw new InvalidOperationException("Kia Ceed wheel map is incomplete.");

        if (FindTransform(model.transform, "Wheel_RL") == null)
            throw new InvalidOperationException("GLB wheel node 'Wheel_RL' is missing.");
        var rearRight = FindTransform(model.transform, "Wheel_RR") ??
                        throw new InvalidOperationException("GLB wheel node 'Wheel_RR' is missing.");
        var rearBrakeAssembly = ExtractRearBrakeAssembly(rearRight, model);
        rearBrakeAssembly.transform.position += root.transform.forward * RearWheelForwardOffset -
                                                root.transform.up * BodyVisualRaise;

        foreach (var wheelName in wheelNames)
        {
            var isFront = wheelName.Contains("_F");
            var isLeft = wheelName.EndsWith("L", StringComparison.Ordinal);
            var corner = (isFront ? "Front" : "Rear") + (isLeft ? "Left" : "Right");
            var wheel = FindTransform(model.transform, wheelName) ??
                        throw new InvalidOperationException($"GLB wheel node '{wheelName}' is missing.");
            var tireRenderer = FindRendererWithMaterial(wheel, "tyre") ??
                               throw new InvalidOperationException($"GLB tire under '{wheelName}' is missing.");
            var caliperRenderer = isFront ? FindRendererWithMaterial(wheel, "chrome_R") : null;
            if (isFront && caliperRenderer == null)
                throw new InvalidOperationException($"GLB caliper under '{wheelName}' is missing.");
            if (!TryGetRendererBounds(tireRenderer.transform, out var tireBounds) ||
                tireBounds.size.x <= 0.001f || tireBounds.size.y <= 0.001f ||
                tireBounds.size.z <= 0.001f)
                throw new InvalidOperationException($"Tire under '{wheelName}' has invalid bounds.");

            var controllerName = corner + "_WheelController";
            var controller = FindTransform(root.transform, controllerName) ??
                             throw new InvalidOperationException($"Wheel controller '{controllerName}' is missing.");
            var targetCenter = WheelControllerPositions[controllerName];
            var radius = isFront ? FrontTireRadius : RearTireRadius;
            var width = isFront ? FrontTireWidth : RearTireWidth;
            controller.localPosition = targetCenter;

            var mount = new GameObject("KiaCeedSportswagonWheel" + corner);
            mount.transform.SetParent(root.transform, false);
            mount.transform.position = tireBounds.center;
            wheel.SetParent(mount.transform, true);
            mount.transform.localScale = new Vector3(
                width / tireBounds.size.x,
                (radius * 2f) / tireBounds.size.y,
                (radius * 2f) / tireBounds.size.z);
            mount.transform.localPosition = targetCenter;
            wheel.name = "Geometry_KiaCeedSportswagonWheel_" + wheelName.Substring("Wheel_".Length);
            OrientWheelVisual(wheel, isLeft);

            if (caliperRenderer != null)
            {
                var fixedCaliper = new GameObject("KiaCeedSportswagonFixedCaliper" + corner);
                fixedCaliper.transform.SetParent(root.transform, false);
                fixedCaliper.transform.localPosition = targetCenter;
                caliperRenderer.transform.SetParent(fixedCaliper.transform, true);
                fixedCaliper.transform.localScale = mount.transform.localScale;
                caliperRenderer.transform.name = "Geometry_KiaCeedSportswagonCaliper_" +
                                                 wheelName.Substring("Wheel_".Length);
            }

            AssignWheelVisual(controller, mount);
            Debug.Log(
                $"KiaCeedSportswagon: fitted {corner} wheel node='{wheelName}' " +
                $"sourceTire={tireBounds.size}, target={targetCenter}.");
        }
        Debug.Log(
            $"KiaCeedSportswagon: kept {rearBrakeAssembly.GetComponentsInChildren<MeshRenderer>(true).Length} " +
            "full-width rear brake renderers fixed to the chassis because the supplied GLB stores both rear sides in each mesh.");
    }

    private static GameObject ExtractRearBrakeAssembly(Transform rearRight, GameObject model)
    {
        var brakeRoots = new HashSet<Transform>();
        foreach (var renderer in rearRight.GetComponentsInChildren<MeshRenderer>(true))
        {
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null || mesh.name.IndexOf("discbrake_rear", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var brakeRoot = renderer.transform;
            while (brakeRoot.parent != rearRight)
            {
                brakeRoot = brakeRoot.parent ??
                            throw new InvalidOperationException("GLB rear brake hierarchy is detached from Wheel_RR.");
            }
            brakeRoots.Add(brakeRoot);
        }

        if (brakeRoots.Count < 3)
            throw new InvalidOperationException(
                $"Expected the GLB's combined rear caliper and rotor meshes; found {brakeRoots.Count}.");

        var assembly = new GameObject("KiaCeedSportswagonRearBrakeAssembly");
        assembly.transform.SetParent(model.transform, false);
        foreach (var brakeRoot in brakeRoots)
            brakeRoot.SetParent(assembly.transform, true);
        return assembly;
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
        MeshRenderer? sourceRenderer = null;
        var largestBodyVolume = 0f;
        foreach (var renderer in modelInstance.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!HasMaterialMarker(renderer, "CarPaint") ||
                HasAncestorNameFragment(renderer.transform, "Wheel_"))
                continue;
            var size = renderer.bounds.size;
            var volume = size.x * size.y * size.z;
            if (volume <= largestBodyVolume) continue;
            sourceRenderer = renderer;
            largestBodyVolume = volume;
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The Kia outer body mesh was not found.");

        if (!AssetDatabase.IsValidFolder(MeshFolder))
            AssetDatabase.CreateFolder(ModRoot + "/Models", "GeneratedMeshes");

        var bakedMesh = UnityEngine.Object.Instantiate(sourceFilter.sharedMesh);
        bakedMesh.name = "KiaCeedSportswagonDamageBody";
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

        var damageBody = new GameObject("KiaCeedSportswagonDamageBody")
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
        material.name.IndexOf("costuras", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("gtlinered", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CeedPowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateCeedPowerCurve();
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

    private static void AssignPersistentMaterials(GameObject model)
    {
        EnsureAssetFolder(MaterialFolder);
        var replacements = new Dictionary<Material, Material>();
        var persistentBySourceName = LoadPersistentMaterialNameMap();
        var sourceNameCandidates = new List<string>(persistentBySourceName.Keys);
        sourceNameCandidates.Sort((left, right) => right.Length.CompareTo(left.Length));
        var opaqueMaterialIndex = 0;
        var transparentMaterialIndex = 0;
        var fallbackAssignments = 0;
        var missingMeshes = new List<string>();
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            var slotCount = mesh != null ? Math.Max(materials.Length, mesh.subMeshCount) : materials.Length;
            if (slotCount == 0)
                continue;
            if (materials.Length < slotCount)
            {
                var resized = new Material[slotCount];
                Array.Copy(materials, resized, materials.Length);
                materials = resized;
            }
            var changed = false;
            Material? inferredMaterial = null;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null)
                {
                    if (inferredMaterial == null)
                        inferredMaterial = FindPersistentMaterialForRenderer(
                            renderer, persistentBySourceName, sourceNameCandidates);
                    if (inferredMaterial != null)
                    {
                        materials[index] = inferredMaterial;
                        changed = true;
                        fallbackAssignments++;
                    }
                    else if (mesh != null && index < mesh.subMeshCount)
                    {
                        missingMeshes.Add(renderer.name + " (" + mesh.name + ")");
                    }
                    continue;
                }

                if (!replacements.TryGetValue(source, out var persistent))
                {
                    var sourceKey = SanitizeAssetName(source.name);
                    if (!persistentBySourceName.TryGetValue(sourceKey, out persistent))
                    {
                        var transparent = KiaCeedSportswagonMaterials.IsTransparentMaterial(source);
                        var kind = transparent ? "Transparent" : "Opaque";
                        var materialIndex = transparent
                            ? transparentMaterialIndex++
                            : opaqueMaterialIndex++;
                        var assetName = $"KiaCeedSportswagon{kind}_{materialIndex:D2}_{sourceKey}";
                        var path = $"{MaterialFolder}/{assetName}.mat";
                        persistent = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (persistent == null)
                        {
                            persistent = new Material(source) { name = assetName };
                            AssetDatabase.CreateAsset(persistent, path);
                        }
                        persistentBySourceName[sourceKey] = persistent;
                        sourceNameCandidates.Add(sourceKey);
                        sourceNameCandidates.Sort((left, right) => right.Length.CompareTo(left.Length));
                    }
                    else
                    {
                        persistent.CopyPropertiesFromMaterial(source);
                        persistent.shader = source.shader;
                    }

                    replacements.Add(source, persistent);
                }

                materials[index] = persistent;
                changed = true;
            }

            if (changed || renderer.sharedMaterials.Length != materials.Length)
                renderer.sharedMaterials = materials;
        }

        if (missingMeshes.Count > 0)
            throw new InvalidOperationException(
                $"Could not map GLB material names onto {missingMeshes.Count} mesh renderer(s): " +
                string.Join(", ", missingMeshes.GetRange(0, Math.Min(8, missingMeshes.Count))) + ".");

        Debug.Log(
            $"KiaCeedSportswagon: assigned persistent GLB materials fallbackSlots={fallbackAssignments}, " +
            $"sourceMaterialCopies={replacements.Count}.");
    }

    private static Dictionary<string, Material> LoadPersistentMaterialNameMap()
    {
        var result = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var fileName = path.Substring(path.LastIndexOf('/') + 1);
            const string opaquePrefix = "KiaCeedSportswagonOpaque_";
            const string transparentPrefix = "KiaCeedSportswagonTransparent_";
            var prefix = fileName.StartsWith(opaquePrefix, StringComparison.Ordinal)
                ? opaquePrefix
                : fileName.StartsWith(transparentPrefix, StringComparison.Ordinal)
                    ? transparentPrefix
                    : string.Empty;
            if (prefix.Length == 0 || !fileName.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                continue;

            var indexEnd = fileName.IndexOf('_', prefix.Length);
            if (indexEnd < 0 || indexEnd + 1 >= fileName.Length - 4)
                continue;
            var sourceName = fileName.Substring(indexEnd + 1, fileName.Length - indexEnd - 5);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                continue;
            if (result.TryGetValue(sourceName, out var existing) && existing != material)
                throw new InvalidOperationException(
                    $"Multiple persistent Kia materials map to source name '{sourceName}'.");
            result[sourceName] = material;
        }
        return result;
    }

    private static Material? FindPersistentMaterialForRenderer(
        Renderer renderer,
        Dictionary<string, Material> persistentBySourceName,
        List<string> sourceNameCandidates)
    {
        var meshName = renderer.GetComponent<MeshFilter>()?.sharedMesh?.name;
        var rendererNames = meshName == null
            ? new[] { renderer.name }
            : new[] { meshName, renderer.name };
        foreach (var rendererName in rendererNames)
        {
            var sanitizedName = SanitizeAssetName(rendererName);
            foreach (var sourceName in sourceNameCandidates)
            {
                if (!sanitizedName.EndsWith("_" + sourceName + "_0", StringComparison.OrdinalIgnoreCase) &&
                    !sanitizedName.EndsWith("_" + sourceName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (persistentBySourceName.TryGetValue(sourceName, out var material))
                    return material;
            }
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

        manifest.ModId = "KiaCeedSportswagon";
        manifest.DisplayName = "Kia Ceed Sportswagon";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "kiaceedsportswagon.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("KiaCeedSportswagon manifest references could not be assigned.");
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
            if (HasAncestorNameFragment(renderer.transform, "Wheel_"))
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

    private static bool TryGetWheelBounds(Transform root, string wheelName, out Bounds bounds)
    {
        bounds = default;
        var wheel = FindTransform(root, wheelName);
        if (wheel == null)
            return false;

        var tire = FindRendererWithMaterial(wheel, "tyre");
        return tire != null && TryGetRendererBounds(tire.transform, out bounds);
    }

    private static MeshRenderer? FindRendererWithMaterial(Transform root, string marker)
    {
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            if (HasMaterialMarker(renderer, marker))
                return renderer;
        return null;
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

    private static bool TryGetKiaCeedSportswagonRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!KiaCeedSportswagonMaterials.IsKiaCeedSportswagonRenderer(renderer.transform))
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

