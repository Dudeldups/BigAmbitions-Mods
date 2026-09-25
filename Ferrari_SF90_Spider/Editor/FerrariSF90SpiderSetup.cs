#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class FerrariSF90SpiderSetupV15
{
    private const string ModRoot = "Assets/Mods/Ferrari_SF90_Spider";
    private const string EmbeddedReferenceBundlePath =
        ModRoot + "/tools~/reference/cadillacescalade.unity3d";
    private static readonly (string Label, string PrefabPath, string BundlePath)[] ReferenceVehicles =
    {
        (
            "Cadillac Escalade",
            "Assets/Mods/Cadillac_Escalade/CadillacEscalade.prefab",
            "Assets/Mods/Cadillac_Escalade/AssetBundles/Windows/cadillacescalade.unity3d"),
        (
            "Koenigsegg Jesko",
            "Assets/Mods/Koenigsegg_Jesko/KoenigseggJesko.prefab",
            "Assets/Mods/Koenigsegg_Jesko/AssetBundles/Windows/koenigseggjesko.unity3d"),
        (
            "Audi RS6R",
            "Assets/Mods/AudiRS6R/AudiRS6R.prefab",
            "Assets/Mods/AudiRS6R/AssetBundles/Windows/audirs6r.unity3d"),
        (
            "SDK Example Vehicle",
            "Assets/Mods/Example-Vehicle/TurboHonza.prefab",
            string.Empty),
    };
    private const string ModelPath = ModRoot + "/Models/2021_ferrari_sf90_spider.glb";
    private const string LightOverlayModelPath = ModRoot + "/Models/FerrariSF90LightOverlays.glb";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string MeshFolder = ModRoot + "/Models/GeneratedMeshes";
    private const string VehicleAssetPath = ModRoot + "/FerrariSF90Spider.asset";
    private const string VehiclePrefabPath = ModRoot + "/FerrariSF90Spider.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/FerrariSF90Spider.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/ferrarisf90spider.unity3d";
    private const string VehicleTypeName =
        "ferrarisf90spider-vehicle:vehicletype_ferrarisf90spider";

    // Ferrari-published dimensions. The supplied model includes mirrors, while
    // Ferrari's 1.973 m width is body width without mirrors. Preserve the model's
    // authored proportions by fitting the visible mirror span separately.
    private const float TargetLength = 4.704f;
    private const float TargetVisualWidthIncludingMirrors = 2.22f;
    private const float TargetHeight = 1.191f;
    private const float Wheelbase = 2.649f; // real-world specification; retained for documentation/physics reference
    // Final visual calibration measured directly against the supplied SF90 model in Unity.
    // The imported/model-scaled wheel arches do not line up perfectly with the published
    // 2.649 m wheelbase, so the prefab uses these visually verified local-Z positions.
    private const float CalibratedFrontWheelZ = 1.166f;
    private const float CalibratedRearWheelZ = -1.547f;
    private const float FrontCaliperGeometryOffsetZ = 0.0200f;
    private const float RearCaliperGeometryOffsetZ = -0.0289f;
    private const float AuthoredAxleCenterOffsetZ = -0.183f; // diagnostic fallback for raw GLB locator analysis
    private const float FrontTrack = 1.679f;
    private const float RearTrack = 1.652f;
    private const float FrontWheelRadius = 0.34325f; // 255/35 ZR20
    private const float RearWheelRadius = 0.34850f;  // 315/30 ZR20
    private const float FrontWheelWidth = 0.255f;
    private const float RearWheelWidth = 0.315f;
    private const float BodyGroundClearance = 0.105f;
    // Visual/prefab calibration from the first in-game test: keep the wheels on the ground
    // and lower only the Ferrari body/driver/collider package to reduce the wheel-arch gap.
    private const float VisualRideHeightOffsetY = -0.040f;

    private const float VehicleMass = 1670f;
    private const float PeriodSpiderMsrp = 558000f;
    private const float FuelCapacityLitres = 68f;
    private const float RatedSystemPowerKw = 735f;
    // NWH solver calibration is deliberately separate from the displayed real
    // system output. Current road-test target is Ferrari's 2.5 s / 7.0 s envelope;
    // launch traction is calibrated separately below so high-speed power remains realistic.
    private const float RoadCalibrationPowerKw = 510f;
    private const float BrakeTorque = 2450f;
    private const float BrakeActuationTime = 0.08f;
    private const float FinalDriveRatio = 4.51f;
    private const float TireFrictionCircleStrength = 0.96f;
    private const float FrontLongitudinalGrip = 0.49f;
    private const float RearLongitudinalGrip = 0.65f;
    private const float FrontLateralGrip = 0.98f;
    private const float RearLateralGrip = 0.96f;
    private const float AntiRollBarForce = 7200f;
    private const float FrontSuspensionTravel = 0.075f;
    private const float RearSuspensionTravel = 0.075f;
    private const float ExitMarkerOffset = 1.42f;

    private static readonly Vector3 StableCenterOfMass = new Vector3(0f, 0.09f, -0.13f);
    private static readonly Vector3 LowerColliderCenter = new Vector3(0f, 0.36f + VisualRideHeightOffsetY, 0f);
    private static readonly Vector3 LowerColliderSize = new Vector3(1.93f, 0.44f, 4.66f);
    private static readonly Vector3 UpperColliderCenter = new Vector3(0f, 0.73f + VisualRideHeightOffsetY, -0.16f);
    private static readonly Vector3 UpperColliderSize = new Vector3(1.68f, 0.58f, 2.46f);
    private static readonly Vector3 FrontContactColliderCenter = new Vector3(0f, 0.52f + VisualRideHeightOffsetY, 1.82f);
    private static readonly Vector3 FrontContactColliderSize = new Vector3(1.86f, 0.38f, 0.92f);
    private static readonly Vector3 SteeringReferencePosition = new Vector3(-0.37f, 0.80f + VisualRideHeightOffsetY, 0.39f);

    private const string WheelMaterialMarker = "Wheel1A_3D_3DWheel1C_Material";
    private const string CaliperMaterialMarker = "CallipersCalliperA_Zone_Material";
    private const string PaintMaterialMarker = "2021Paint_Material";
    private const string LightMaterialMarker = "LightA_Material";

    private static readonly Dictionary<string, Vector3> WheelControllerPositions =
        new Dictionary<string, Vector3>
        {
            { "FrontLeft_WheelController", new Vector3(-FrontTrack * 0.5f, FrontWheelRadius, CalibratedFrontWheelZ) },
            { "FrontRight_WheelController", new Vector3(FrontTrack * 0.5f, FrontWheelRadius, CalibratedFrontWheelZ) },
            { "RearLeft_WheelController", new Vector3(-RearTrack * 0.5f, RearWheelRadius, CalibratedRearWheelZ) },
            { "RearRight_WheelController", new Vector3(RearTrack * 0.5f, RearWheelRadius, CalibratedRearWheelZ) },
        };

    // Exact Ferrari ratios are not published in the supplied source material.
    // These are simulation starting ratios for the real eight-speed DCT layout.
    private static readonly float[] SF90SpiderGears =
    {
        -3.45f,
        0f,
        3.45f,
        2.26f,
        1.65f,
        1.29f,
        1.03f,
        0.84f,
        0.67f,
        0.48f,
    };

    private static readonly (string SourceName, string RuntimeName)[] LightOverlayNames =
    {
        // The SF90 uses the same front strip for white DRL and amber indicator.
        // The Blender extractor duplicates each labeled strip into two overlay objects
        // so the runtime can switch the same physical surface between white and amber.
        ("VehicleLightRef_DRL_FL", "FerrariSF90Spider_Light_DRL_FL"),
        ("VehicleLightRef_DRL_FR", "FerrariSF90Spider_Light_DRL_FR"),
        ("VehicleLightRef_FrontIndicator_FL", "FerrariSF90Spider_Light_FrontIndicator_FL"),
        ("VehicleLightRef_FrontIndicator_FR", "FerrariSF90Spider_Light_FrontIndicator_FR"),
        ("VehicleLightRef_MirrorIndicatorLeft", "FerrariSF90Spider_Light_MirrorIndicatorLeft"),
        ("VehicleLightRef_MirrorIndicatorRight", "FerrariSF90Spider_Light_MirrorIndicatorRight"),
        ("VehicleLightRef_Headlamps", "FerrariSF90Spider_Light_Headlamps"),
        // The same rear lamp geometry is duplicated into dim tail and bright brake overlays.
        ("VehicleLightRef_TailLights", "FerrariSF90Spider_Light_TailLights"),
        ("VehicleLightRef_BrakeLights", "FerrariSF90Spider_Light_BrakeLights"),
        ("VehicleLightRef_ThirdBrakeLight", "FerrariSF90Spider_Light_ThirdBrakeLight"),
        ("VehicleLightRef_ReverseLights", "FerrariSF90Spider_Light_ReverseLights"),
        ("VehicleLightRef_RearIndicator_RL", "FerrariSF90Spider_Light_RearIndicator_RL"),
        ("VehicleLightRef_RearIndicator_RR", "FerrariSF90Spider_Light_RearIndicator_RR"),
    };

    [MenuItem("Big Ambitions Mods/Ferrari SF90 Spider/Setup V15 (glass + RPM audio + driver + ride height)")]
    public static void Generate()
    {
        EnsureAssetFolder(MaterialFolder);
        EnsureAssetFolder(MeshFolder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("Ferrari SF90 Spider Setup V15 ACTIVE - GLASS + RPM AUDIO + DRIVER + RIDE HEIGHT");
        var reference = ResolveReferenceVehicle();
        Debug.Log($"FerrariSF90Spider setup: using '{reference.Label}' as the vehicle reference.");
        RemoveLegacyVehicleTypeAsset();
        CreateVehiclePrefab(reference);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ValidateProjectAssets(false);
        Debug.Log(
            "FerrariSF90Spider V15 setup complete: model normalized, four wheel assemblies and " +
            "four fixed calipers generated from the supplied GLB, eight-speed DCT baseline, " +
            "AWD reference drivetrain, transparent SF90 glass/lens materials, gear-aware RPM audio, driver lean, lowered body stance, and the current light-overlay import validated.");
    }

    [MenuItem("Big Ambitions Mods/Ferrari SF90 Spider/Setup + Build V15 (glass + RPM audio + ride height)")]
    public static void GenerateAndBuild()
    {
        Generate();
        BuildFerrariAssetBundle();
        VerifyBuiltBundle();
    }

    [MenuItem("Big Ambitions Mods/Ferrari SF90 Spider/Validate V15")]
    public static void ValidateProject()
    {
        ValidateProjectAssets(true);
    }

    [MenuItem("Big Ambitions Mods/Ferrari SF90 Spider/Verify Bundle V15")]
    public static void VerifyBuiltBundle()
    {
        var bundle = AssetBundle.LoadFromFile(AssetPathToAbsolutePath(WindowsBundlePath));
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{WindowsBundlePath}'.");
        try
        {
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("SF90 bundle is missing the vehicle prefab.");
            ValidatePrefab(prefab, true);
            Debug.Log("FerrariSF90Spider V15 bundle verified successfully (VehicleType is created at runtime).");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static (string Label, string PrefabPath, string BundlePath) ResolveReferenceVehicle()
    {
        // V6 ships an editor-only Cadillac reference bundle under tools~ so setup is
        // independent of which other mod source folders happen to be present/importable.
        var embeddedAbsolute = AssetPathToAbsolutePath(EmbeddedReferenceBundlePath);
        if (File.Exists(embeddedAbsolute))
        {
            Debug.Log(
                $"FerrariSF90Spider V15: using embedded Cadillac reference bundle at '{embeddedAbsolute}'.");
            return (
                "Embedded Cadillac Escalade",
                "Assets/Mods/Cadillac_Escalade/CadillacEscalade.prefab",
                EmbeddedReferenceBundlePath);
        }

        // Fallback for an incomplete patch copy: discover source prefabs/bundles dynamically.
        var diagnostics = new List<string>();
        foreach (var candidate in ReferenceVehicles)
        {
            var resolvedPrefabPath = ResolveExistingPrefabPath(candidate.PrefabPath);
            var resolvedBundlePath = ResolveExistingBundlePath(candidate.BundlePath);
            var prefab = !string.IsNullOrEmpty(resolvedPrefabPath)
                ? AssetDatabase.LoadAssetAtPath<GameObject>(resolvedPrefabPath)
                : null;
            var bundleExists = !string.IsNullOrEmpty(resolvedBundlePath) &&
                               File.Exists(AssetPathToAbsolutePath(resolvedBundlePath));

            diagnostics.Add(
                $"{candidate.Label}: prefabPath='{resolvedPrefabPath}', " +
                $"prefabAsset={(prefab != null ? "ok" : "missing")}, " +
                $"bundlePath='{resolvedBundlePath}', bundle={(bundleExists ? "ok" : "missing")}");

            if (prefab != null || bundleExists)
                return (candidate.Label, resolvedPrefabPath, resolvedBundlePath);
        }

        throw new InvalidOperationException(
            "Ferrari SF90 Spider V15 could not find its embedded editor reference bundle and no " +
            "fallback vehicle prefab/bundle is available. Checked: " + string.Join("; ", diagnostics) +
            $". Expected embedded file: '{embeddedAbsolute}'.");
    }

    private static string ResolveExistingPrefabPath(string preferredPath)
    {
        if (!string.IsNullOrEmpty(preferredPath) &&
            AssetDatabase.LoadAssetAtPath<GameObject>(preferredPath) != null)
            return preferredPath;

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(preferredPath);
        if (string.IsNullOrEmpty(fileNameWithoutExtension))
            return string.Empty;

        foreach (var guid in AssetDatabase.FindAssets(fileNameWithoutExtension + " t:Prefab", new[] { "Assets" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(Path.GetFileNameWithoutExtension(path), fileNameWithoutExtension,
                    StringComparison.OrdinalIgnoreCase))
                return path;
        }

        return string.Empty;
    }

    private static string ResolveExistingBundlePath(string preferredPath)
    {
        if (!string.IsNullOrEmpty(preferredPath) &&
            File.Exists(AssetPathToAbsolutePath(preferredPath)))
            return preferredPath;

        var fileName = Path.GetFileName(preferredPath);
        if (string.IsNullOrEmpty(fileName))
            return string.Empty;

        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
            return string.Empty;

        var assetsRoot = Path.Combine(projectRoot, "Assets");
        if (!Directory.Exists(assetsRoot))
            return string.Empty;

        try
        {
            foreach (var absolute in Directory.EnumerateFiles(assetsRoot, fileName, SearchOption.AllDirectories))
            {
                var normalizedProjectRoot = projectRoot.Replace('\\', '/').TrimEnd('/');
                var normalizedAbsolute = absolute.Replace('\\', '/');
                if (!normalizedAbsolute.StartsWith(normalizedProjectRoot + "/", StringComparison.OrdinalIgnoreCase))
                    continue;
                return normalizedAbsolute.Substring(normalizedProjectRoot.Length + 1);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"FerrariSF90Spider V15: bundle discovery failed: {ex.Message}");
        }

        return string.Empty;
    }

    private static void RemoveLegacyVehicleTypeAsset()
    {
        // V9 shipped a YAML VehicleType asset. The current SDK imports BigAmbitions.dll as
        // an explicitly referenced plugin, and Unity's editor asset pipeline can leave these
        // external-DLL ScriptableObjects unresolved even though runtime assemblies compile.
        // V10 therefore creates VehicleType in FerrariSF90SpiderMod at game runtime and keeps
        // the AssetBundle purely prefab/material/mesh based. Remove the obsolete asset so it
        // cannot poison bundle builds.
        if (AssetDatabase.DeleteAsset(VehicleAssetPath))
        {
            Debug.Log("FerrariSF90Spider V15: removed legacy editor VehicleType asset; runtime registration will create it in memory.");
            return;
        }

        var absolutePath = AssetPathToAbsolutePath(VehicleAssetPath);
        var changed = false;
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
            changed = true;
        }
        if (File.Exists(absolutePath + ".meta"))
        {
            File.Delete(absolutePath + ".meta");
            changed = true;
        }
        if (changed)
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("FerrariSF90Spider V15: removed legacy VehicleType files from disk.");
        }
    }

    private static GameObject InstantiateReferenceVehicle(
        (string Label, string PrefabPath, string BundlePath) reference)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(reference.PrefabPath);
        if (source != null)
        {
            Debug.Log(
                $"FerrariSF90Spider setup: loading {reference.Label} prefab from AssetDatabase.");
            return UnityEngine.Object.Instantiate(source);
        }

        if (string.IsNullOrEmpty(reference.BundlePath))
            throw new InvalidOperationException(
                $"Reference prefab '{reference.PrefabPath}' is not importable and no bundle fallback exists.");

        var absoluteBundlePath = AssetPathToAbsolutePath(reference.BundlePath);
        var bundle = AssetBundle.LoadFromFile(absoluteBundlePath);
        if (bundle == null)
            throw new InvalidOperationException(
                $"Could not load reference bundle '{reference.BundlePath}'.");

        try
        {
            GameObject? bundledPrefab = null;
            foreach (var assetName in bundle.GetAllAssetNames())
            {
                if (!assetName.EndsWith(
                        "/" + Path.GetFileName(reference.PrefabPath),
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                bundledPrefab = bundle.LoadAsset<GameObject>(assetName);
                if (bundledPrefab != null)
                    break;
            }

            if (bundledPrefab == null)
            {
                bundledPrefab = bundle.LoadAsset<GameObject>(reference.PrefabPath);
            }

            if (bundledPrefab == null)
                throw new InvalidOperationException(
                    $"Bundle '{reference.BundlePath}' does not contain reference prefab " +
                    $"'{reference.PrefabPath}'. Assets: {string.Join(", ", bundle.GetAllAssetNames())}");

            Debug.Log(
                $"FerrariSF90Spider setup: AssetDatabase prefab unavailable; " +
                $"loading {reference.Label} from its Windows AssetBundle fallback.");
            return UnityEngine.Object.Instantiate(bundledPrefab);
        }
        finally
        {
            // Keep loaded referenced objects alive until the generated Ferrari prefab has been saved.
            bundle.Unload(false);
        }
    }

    private static string AssetPathToAbsolutePath(string assetPath)
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
            throw new InvalidOperationException("Could not resolve the Unity project root.");

        var relative = assetPath.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(projectRoot, relative));
    }

    private static void CreateVehiclePrefab(
        (string Label, string PrefabPath, string BundlePath) reference)
    {
        var root = InstantiateReferenceVehicle(reference);
        RemoveMissingScriptsRecursively(root);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            throw new InvalidOperationException($"SF90 GLB did not import at '{ModelPath}'.");

        root.name = "FerrariSF90Spider";
        try
        {
            var removedReferenceObjects = RemoveReferenceOwnedChildren(root);
            Debug.Log($"FerrariSF90Spider V15: removed {removedReferenceObjects} reference-owned Cadillac/Audi/Koenigsegg hierarchy object(s).");
            StripReferenceGeometry(root);
            RemoveReferenceBehaviours(root);
            SanitizeInheritedReferenceHelpers(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root);
            ConfigurePowertrain(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the SF90 model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "FerrariSF90SpiderVisual";
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            ConfigureExitAndDriverMarkers(root);
            AssignPersistentMaterials(modelInstance);
            var overlays = CreateLightOverlayReferences(modelInstance);
            AttachWheelVisualsAndCalipers(root, modelInstance);

            var materialFix = FixSolidMaterialsViaRuntime(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);
            ConfigureRuntimeBootstrap(root);

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the SF90 vehicle prefab.");

            Debug.Log(
                $"FerrariSF90Spider prefab prepared: renderers={materialFix.RendererCount}, " +
                $"opaqueFixed={materialFix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={materialFix.TransparentMaterialsFixed}, " +
                $"cabinGlass={materialFix.CabinGlassRenderers}, lightOverlays={overlays}/13.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void RemoveMissingScriptsRecursively(GameObject root)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            var removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
            if (removed > 0)
                Debug.Log($"FerrariSF90Spider V15: removed {removed} missing reference script(s) from '{transform.name}'.");
        }
    }

    private static int RemoveReferenceOwnedChildren(GameObject root)
    {
        var candidates = new List<Transform>();
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (ReferenceEquals(transform, root.transform) ||
                !IsReferenceOwnedTransformName(transform.name))
                continue;
            candidates.Add(transform);
        }

        // Delete deepest objects first so a named parent and its named children can
        // both be present without leaving stale Unity-object handles behind.
        candidates.Sort((left, right) => GetTransformDepth(right).CompareTo(GetTransformDepth(left)));
        var removed = 0;
        foreach (var transform in candidates)
        {
            if (transform == null)
                continue;
            UnityEngine.Object.DestroyImmediate(transform.gameObject);
            removed++;
        }
        return removed;
    }

    private static bool IsReferenceOwnedTransformName(string name)
    {
        return name.StartsWith("Cadillac", StringComparison.Ordinal) ||
               name.StartsWith("AudiRS6R", StringComparison.Ordinal) ||
               name.StartsWith("Koenigsegg", StringComparison.Ordinal) ||
               name.StartsWith("LamborghiniRevuelto", StringComparison.Ordinal);
    }

    private static int GetTransformDepth(Transform transform)
    {
        var depth = 0;
        for (var current = transform.parent; current != null; current = current.parent)
            depth++;
        return depth;
    }

    private static void StripReferenceGeometry(GameObject root)
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

    private static void RemoveReferenceBehaviours(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var name = component.GetType().Name;
            if (name.StartsWith("AudiRS6R", StringComparison.Ordinal) ||
                name.StartsWith("CadillacEscalade", StringComparison.Ordinal) ||
                name.StartsWith("KoenigseggJesko", StringComparison.Ordinal) ||
                name.StartsWith("LamborghiniRevuelto", StringComparison.Ordinal))
                UnityEngine.Object.DestroyImmediate(component);
        }
    }

    private static void SanitizeInheritedReferenceHelpers(GameObject root)
    {
        // The Cadillac reference prefab itself descends from the Audi RS6-R setup and
        // intentionally retained its functional NavMeshObstacle / obstacle toggler on a
        // GameObject named "2020_abt_sportline_audi_rs6-r". Those components are generic
        // Big Ambitions vehicle plumbing, so keep them, but remove the misleading Audi
        // identity from the generated Ferrari prefab.
        var renamed = 0;
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null ||
                !string.Equals(component.GetType().Name, "NavMeshObstacle", StringComparison.Ordinal))
                continue;

            var helperObject = component.gameObject;
            if (helperObject == null || usedNames.Contains(helperObject.name))
                continue;

            var targetName = renamed == 0
                ? "FerrariSF90SpiderNavMeshObstacle"
                : $"FerrariSF90SpiderNavMeshObstacle{renamed + 1}";
            helperObject.name = targetName;
            usedNames.Add(targetName);
            renamed++;
        }

        // Defensive cleanup for any surviving helper/object names inherited from the
        // Cadillac/Audi source. Do not delete generic BA components merely because their
        // source object had a branded name; rename the object in place so all serialized
        // component references remain valid.
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (ReferenceEquals(transform, root.transform) ||
                !ContainsReferenceBrandToken(transform.name))
                continue;

            // A branded object that survived the explicit hierarchy cleanup is a generic
            // helper rather than visual/reference-owned geometry. Preserve its components
            // and references, but give it a Ferrari-local neutral name.
            var hasNavObstacle = false;
            foreach (var component in transform.GetComponents<Component>())
            {
                if (component != null &&
                    string.Equals(component.GetType().Name, "NavMeshObstacle", StringComparison.Ordinal))
                {
                    hasNavObstacle = true;
                    break;
                }
            }

            if (hasNavObstacle)
                transform.name = "FerrariSF90SpiderNavMeshObstacle";
        }

        if (renamed > 0)
            Debug.Log($"FerrariSF90Spider V15: sanitized {renamed} inherited NavMeshObstacle helper name(s); CarController references remain intact.");
    }

    private static bool ContainsReferenceBrandToken(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        return name.IndexOf("audi", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("rs6", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("cadillac", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("escalade", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("koenigsegg", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("jesko", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("lamborghini", StringComparison.OrdinalIgnoreCase) >= 0 ||
               name.IndexOf("revuelto", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void RemoveModelLights(GameObject model)
    {
        foreach (var light in model.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(light.gameObject);
    }

    private static void ConfigureRuntimeBootstrap(GameObject root)
    {
        var paintControllerType = RequireRuntimeType("FerrariSF90SpiderPaintController");
        if (root.GetComponent(paintControllerType) == null)
            root.AddComponent(paintControllerType);
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ?? root.AddComponent<Rigidbody>();
        body.mass = VehicleMass;
        body.drag = 0f;
        body.angularDrag = 1.45f;
        body.centerOfMass = StableCenterOfMass;
    }

    private static void ConfigureWheelControllers(GameObject root)
    {
        foreach (var pair in WheelControllerPositions)
        {
            var transform = FindTransform(root.transform, pair.Key) ??
                            throw new InvalidOperationException($"Wheel controller '{pair.Key}' is missing.");
            transform.localPosition = pair.Value;
            var front = pair.Key.StartsWith("Front", StringComparison.Ordinal);
            foreach (var component in transform.GetComponents<MonoBehaviour>())
            {
                if (component == null)
                    continue;
                var serialized = new SerializedObject(component);
                SetRelativeNumber(serialized, "spring.maxLength", front ? FrontSuspensionTravel : RearSuspensionTravel);
                SetRelativeNumber(serialized, "spring.maxForce", 20500f);
                SetRelativeNumber(serialized, "wheel.radius", front ? FrontWheelRadius : RearWheelRadius);
                SetRelativeNumber(serialized, "wheel.width", front ? FrontWheelWidth : RearWheelWidth);
                SetRelativeNumber(
                    serialized,
                    "forwardFriction.grip",
                    front ? FrontLongitudinalGrip : RearLongitudinalGrip);
                SetRelativeNumber(serialized, "sideFriction.grip", front ? FrontLateralGrip : RearLateralGrip);
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
            throw new InvalidOperationException("Reference vehicle requires two BodyCollider boxes.");
        colliders[0].center = LowerColliderCenter;
        colliders[0].size = LowerColliderSize;
        colliders[1].center = UpperColliderCenter;
        colliders[1].size = UpperColliderSize;
        var frontContact = colliders.Length > 2 ? colliders[2] : holder.gameObject.AddComponent<BoxCollider>();
        frontContact.center = FrontContactColliderCenter;
        frontContact.size = FrontContactColliderSize;
        frontContact.isTrigger = false;
        frontContact.enabled = true;
    }

    private static void ConfigureExitAndDriverMarkers(GameObject root)
    {
        SetLocalPosition(root, "Driverside", new Vector3(-ExitMarkerOffset, 0.10f, 0f));
        SetLocalPosition(root, "Passengerside", new Vector3(ExitMarkerOffset, 0.10f, 0f));

        var steering = FindTransform(root.transform, "STEERING_WHEEL");
        if (steering == null)
        {
            var marker = new GameObject("STEERING_WHEEL");
            marker.transform.SetParent(root.transform, false);
            steering = marker.transform;
        }
        steering.localPosition = SteeringReferencePosition;
        steering.localRotation = Quaternion.identity;
        steering.localScale = Vector3.one;

        // Keep the inherited road-light donor aligned with the body after the
        // visual ride-height correction. The active overlay meshes are children
        // of FerrariSF90SpiderVisual and already follow the body automatically.
        var spotlights = FindTransform(root.transform, "Spotlights");
        if (spotlights != null)
            spotlights.localPosition += new Vector3(0f, VisualRideHeightOffsetY, 0f);

        // Keep the inherited vanilla steering reference coherent as well.
        var inherited = FindTransform(root.transform, "Animate_SteeringWheel_033");
        if (inherited != null)
            inherited.localPosition = SteeringReferencePosition;
    }

    private static void ConfigureVehicleReferences(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var vehicleTypeProperty = serialized.FindProperty("vehicleType");
            if (vehicleTypeProperty?.propertyType == SerializedPropertyType.ObjectReference)
                vehicleTypeProperty.objectReferenceValue = null;
            var instance = serialized.FindProperty("vehicleInstance");
            var typeName = instance?.FindPropertyRelative("vehicleTypeName");
            if (typeName?.propertyType == SerializedPropertyType.String)
                typeName.stringValue = VehicleTypeName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static AnimationCurve CreatePowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.11f, 0.10f),
            new Keyframe(0.34f, 0.46f),
            new Keyframe(0.56f, 0.68f),
            new Keyframe(0.78f, 0.88f),
            new Keyframe(0.94f, 1f),
            new Keyframe(1f, 0.97f));

    private static void ConfigurePowertrain(GameObject root)
    {
        var found = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            if (string.Equals(component.GetType().FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
            {
                found = true;
                SetRelativeNumber(serialized, "brakes.maxTorque", BrakeTorque);
                SetRelativeNumber(serialized, "brakes.actuationTime", BrakeActuationTime);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRPM", 1400f);
                SetRelativeNumber(serialized, "powertrain.clutch.throttleEngagementOffsetRPM", 700f);
                SetRelativeNumber(serialized, "powertrain.clutch.engagementRange", 650f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepTorque", 0f);
                SetRelativeNumber(serialized, "powertrain.clutch.creepSpeedLimit", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.12f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", RoadCalibrationPowerKw);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType == SerializedPropertyType.AnimationCurve)
                    powerCurve.animationCurveValue = CreatePowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 900f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 8000f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.42f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", FinalDriveRatio);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 8f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.065f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 4300f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 7500f);
                SetRelativeNumber(serialized, "powertrain.transmission.transmissionType", 1f);
                var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
                if (gears == null || !gears.isArray)
                    throw new InvalidOperationException("Reference transmission gear array is missing.");
                gears.arraySize = SF90SpiderGears.Length;
                for (var index = 0; index < SF90SpiderGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = SF90SpiderGears[index];

                var wheelGroups = FindRelativeProperty(serialized, "powertrain.wheelGroups");
                if (wheelGroups != null && wheelGroups.isArray)
                    for (var index = 0; index < wheelGroups.arraySize; index++)
                    {
                        var antiRoll = wheelGroups.GetArrayElementAtIndex(index).FindPropertyRelative("antiRollBarForce");
                        if (antiRoll?.propertyType == SerializedPropertyType.Float)
                            antiRoll.floatValue = AntiRollBarForce;
                    }
            }
            else if (string.Equals(component.GetType().Name, "SpeedLimiterModuleWrapper", StringComparison.Ordinal))
            {
                SetRelativeBool(serialized, "module.active", true);
                SetRelativeNumber(serialized, "module.speedLimit", 340f);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        if (!found)
            throw new InvalidOperationException("NWH VehicleController was not found on the reference prefab.");
    }

    private static void NormalizeModel(GameObject model)
    {
        model.transform.localPosition = Vector3.zero;
        model.transform.localScale = Vector3.one;
        if (!TryGetModelBodyBounds(model.transform, out var bounds))
            throw new InvalidOperationException("SF90 model body contains no usable renderers.");
        if (bounds.size.x <= 0.001f || bounds.size.y <= 0.001f || bounds.size.z <= 0.001f)
            throw new InvalidOperationException("SF90 imported body bounds are invalid.");

        var scale = new Vector3(
            TargetVisualWidthIncludingMirrors / bounds.size.x,
            (TargetHeight - BodyGroundClearance) / bounds.size.y,
            TargetLength / bounds.size.z);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled SF90 body bounds could not be measured.");
        model.transform.position += new Vector3(
            -bounds.center.x,
            BodyGroundClearance - bounds.min.y + VisualRideHeightOffsetY,
            -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final SF90 body bounds could not be measured.");

        Debug.Log($"FerrariSF90Spider model normalization: scale={scale}, bodyBounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisualsAndCalipers(GameObject root, GameObject model)
    {
        var wheelRenderers = new List<MeshRenderer>();
        var caliperRenderers = new List<MeshRenderer>();
        foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (HasMaterialMarker(renderer.sharedMaterials, WheelMaterialMarker))
                wheelRenderers.Add(renderer);
            else if (HasMaterialMarker(renderer.sharedMaterials, CaliperMaterialMarker))
                caliperRenderers.Add(renderer);
        }
        if (wheelRenderers.Count == 0)
            throw new InvalidOperationException("No SF90 wheel renderers were found in the supplied GLB.");
        if (caliperRenderers.Count == 0)
            throw new InvalidOperationException("No SF90 caliper renderers were found in the supplied GLB.");

        var sourceFrontLeft = FindTransform(model.transform, "3DWheel Front L");
        var sourceFrontRight = FindTransform(model.transform, "3DWheel Front R");
        var sourceRearLeft = FindTransform(model.transform, "3DWheel Rear L");
        var sourceRearRight = FindTransform(model.transform, "3DWheel Rear R");
        // Raw GLB wheel locators are used only to classify the source geometry.
        // Final wheel positions come from the visually verified SF90 prefab calibration.
        var sourceLeftSign = sourceFrontLeft != null
            ? Mathf.Sign(root.transform.InverseTransformPoint(sourceFrontLeft.position).x)
            : -1f;
        if (Mathf.Abs(sourceLeftSign) < 0.5f)
            sourceLeftSign = -1f;
        var frontZ = sourceFrontLeft != null
            ? root.transform.InverseTransformPoint(sourceFrontLeft.position).z
            : 1f;
        var rearZ = sourceRearLeft != null
            ? root.transform.InverseTransformPoint(sourceRearLeft.position).z
            : -1f;
        var axleMid = (frontZ + rearZ) * 0.5f;
        var frontPositive = frontZ >= rearZ;

        var wheelsByCorner = NewCornerMap();
        var calipersByCorner = NewCornerMap();
        foreach (var renderer in wheelRenderers)
            wheelsByCorner[ClassifyCorner(root.transform, renderer.bounds.center, sourceLeftSign, axleMid, frontPositive)].Add(renderer);
        foreach (var renderer in caliperRenderers)
            calipersByCorner[ClassifyCorner(root.transform, renderer.bounds.center, sourceLeftSign, axleMid, frontPositive)].Add(renderer);

        var toDestroy = new HashSet<GameObject>();
        foreach (var corner in new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" })
        {
            if (wheelsByCorner[corner].Count == 0)
                throw new InvalidOperationException($"SF90 source wheel cluster '{corner}' is empty.");
            if (calipersByCorner[corner].Count == 0)
                throw new InvalidOperationException($"SF90 source caliper cluster '{corner}' is empty.");

            var front = corner.StartsWith("Front", StringComparison.Ordinal);
            var controllerName = corner.Replace("Left", "Left_WheelController").Replace("Right", "Right_WheelController");
            var controller = FindTransform(root.transform, controllerName) ??
                             throw new InvalidOperationException($"Wheel controller '{controllerName}' is missing.");
            var targetPosition = WheelControllerPositions[controllerName];
            controller.localPosition = targetPosition;

            Debug.Log(
                $"FerrariSF90Spider V15 {corner}: wheelZ={targetPosition.z:F6}, " +
                $"caliperLocalZ={(front ? FrontCaliperGeometryOffsetZ : RearCaliperGeometryOffsetZ):F4}.");

            var sourceBounds = GetLocalBounds(root.transform, wheelsByCorner[corner]);
            var targetRadius = front ? FrontWheelRadius : RearWheelRadius;
            var targetWidth = front ? FrontWheelWidth : RearWheelWidth;
            var fit = new Vector3(
                targetWidth / Mathf.Max(0.0001f, sourceBounds.size.x),
                (targetRadius * 2f) / Mathf.Max(0.0001f, sourceBounds.size.y),
                (targetRadius * 2f) / Mathf.Max(0.0001f, sourceBounds.size.z));

            var wheelMount = new GameObject("FerrariSF90SpiderWheel" + corner);
            wheelMount.transform.SetParent(root.transform, false);
            wheelMount.transform.localPosition = targetPosition;
            var wheelMesh = BakeCombinedMesh(
                root.transform,
                wheelsByCorner[corner],
                sourceBounds.center,
                fit,
                $"{MeshFolder}/FerrariSF90SpiderWheel{corner}.asset",
                "FerrariSF90SpiderWheel" + corner);
            wheelMount.AddComponent<MeshFilter>().sharedMesh = wheelMesh;
            var wheelRenderer = wheelMount.AddComponent<MeshRenderer>();
            CopyRendererSettings(wheelsByCorner[corner][0], wheelRenderer);
            wheelRenderer.sharedMaterial = FirstMaterial(wheelsByCorner[corner]);
            AssignWheelVisual(controller, wheelMount);

            var fixedCaliper = new GameObject("FerrariSF90SpiderFixedCaliper" + corner);
            fixedCaliper.transform.SetParent(root.transform, false);
            fixedCaliper.transform.localPosition = targetPosition;
            var caliperGeometry = new GameObject("FerrariSF90SpiderCaliper" + corner);
            caliperGeometry.transform.SetParent(fixedCaliper.transform, false);
            caliperGeometry.transform.localPosition = new Vector3(
                0f,
                0f,
                front ? FrontCaliperGeometryOffsetZ : RearCaliperGeometryOffsetZ);
            var caliperMesh = BakeCombinedMesh(
                root.transform,
                calipersByCorner[corner],
                sourceBounds.center,
                fit,
                $"{MeshFolder}/FerrariSF90SpiderCaliper{corner}.asset",
                "FerrariSF90SpiderCaliper" + corner);
            caliperGeometry.AddComponent<MeshFilter>().sharedMesh = caliperMesh;
            var caliperRenderer = caliperGeometry.AddComponent<MeshRenderer>();
            CopyRendererSettings(calipersByCorner[corner][0], caliperRenderer);
            caliperRenderer.sharedMaterial = FirstMaterial(calipersByCorner[corner]);

            foreach (var renderer in wheelsByCorner[corner]) toDestroy.Add(renderer.gameObject);
            foreach (var renderer in calipersByCorner[corner]) toDestroy.Add(renderer.gameObject);

            Debug.Log(
                $"FerrariSF90Spider {corner}: sourceParts={wheelsByCorner[corner].Count}, " +
                $"caliperParts={calipersByCorner[corner].Count}, sourceBounds={sourceBounds.size}, " +
                $"fit={fit}, target={targetPosition}.");
        }

        foreach (var gameObject in toDestroy)
            if (gameObject != null)
                UnityEngine.Object.DestroyImmediate(gameObject);
    }

    private static float ResolveAuthoredAxleCenterZ(
        Transform vehicleRoot,
        Transform? sourceFrontLeft,
        Transform? sourceFrontRight,
        Transform? sourceRearLeft,
        Transform? sourceRearRight)
    {
        var frontSamples = new List<float>(2);
        var rearSamples = new List<float>(2);
        if (sourceFrontLeft != null) frontSamples.Add(vehicleRoot.InverseTransformPoint(sourceFrontLeft.position).z);
        if (sourceFrontRight != null) frontSamples.Add(vehicleRoot.InverseTransformPoint(sourceFrontRight.position).z);
        if (sourceRearLeft != null) rearSamples.Add(vehicleRoot.InverseTransformPoint(sourceRearLeft.position).z);
        if (sourceRearRight != null) rearSamples.Add(vehicleRoot.InverseTransformPoint(sourceRearRight.position).z);

        if (frontSamples.Count == 0 || rearSamples.Count == 0)
        {
            Debug.LogWarning(
                $"FerrariSF90Spider V15: authored wheel locators are incomplete; using axle-center fallback {AuthoredAxleCenterOffsetZ:F3}m.");
            return AuthoredAxleCenterOffsetZ;
        }

        var frontZ = 0f;
        foreach (var sample in frontSamples) frontZ += sample;
        frontZ /= frontSamples.Count;
        var rearZ = 0f;
        foreach (var sample in rearSamples) rearZ += sample;
        rearZ /= rearSamples.Count;
        var axleCenter = (frontZ + rearZ) * 0.5f;

        // A malformed/importer-flipped locator should not throw the whole vehicle
        // several metres off-center. The supplied SF90 resolves to about -0.183 m.
        if (float.IsNaN(axleCenter) || float.IsInfinity(axleCenter) || Mathf.Abs(axleCenter) > 0.75f)
        {
            Debug.LogWarning(
                $"FerrariSF90Spider V15: authored axle center {axleCenter:F3}m is implausible; using fallback {AuthoredAxleCenterOffsetZ:F3}m.");
            return AuthoredAxleCenterOffsetZ;
        }

        Debug.Log(
            $"FerrariSF90Spider V15 raw locator diagnostic: authoredFront={frontZ:F3}m, authoredRear={rearZ:F3}m, " +
            $"authoredCenter={axleCenter:F3}m, targetFront={axleCenter + Wheelbase * 0.5f:F3}m, " +
            $"targetRear={axleCenter - Wheelbase * 0.5f:F3}m.");
        return axleCenter;
    }

    private static Dictionary<string, List<MeshRenderer>> NewCornerMap() =>
        new Dictionary<string, List<MeshRenderer>>
        {
            { "FrontLeft", new List<MeshRenderer>() },
            { "FrontRight", new List<MeshRenderer>() },
            { "RearLeft", new List<MeshRenderer>() },
            { "RearRight", new List<MeshRenderer>() },
        };

    private static string ClassifyCorner(
        Transform vehicleRoot,
        Vector3 worldCenter,
        float sourceLeftSign,
        float axleMid,
        bool frontPositive)
    {
        var local = vehicleRoot.InverseTransformPoint(worldCenter);
        var isLeft = Mathf.Sign(local.x == 0f ? sourceLeftSign : local.x) == Mathf.Sign(sourceLeftSign);
        var isFront = frontPositive ? local.z >= axleMid : local.z <= axleMid;
        return (isFront ? "Front" : "Rear") + (isLeft ? "Left" : "Right");
    }

    private static Bounds GetLocalBounds(Transform targetRoot, IReadOnlyList<MeshRenderer> renderers)
    {
        var found = false;
        var bounds = default(Bounds);
        foreach (var renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter?.sharedMesh == null)
                continue;
            var matrix = targetRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var vertices = filter.sharedMesh.vertices;
            for (var index = 0; index < vertices.Length; index++)
            {
                var point = matrix.MultiplyPoint3x4(vertices[index]);
                if (!found)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else
                    bounds.Encapsulate(point);
            }
        }
        if (!found)
            throw new InvalidOperationException("Could not calculate source mesh bounds.");
        return bounds;
    }

    private static Mesh BakeCombinedMesh(
        Transform vehicleRoot,
        IReadOnlyList<MeshRenderer> renderers,
        Vector3 authoredWheelCenter,
        Vector3 fit,
        string assetPath,
        string meshName)
    {
        var combines = new List<CombineInstance>();
        foreach (var renderer in renderers)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter?.sharedMesh == null)
                continue;
            var sourceToRoot = vehicleRoot.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var matrix = Matrix4x4.Scale(fit) * Matrix4x4.Translate(-authoredWheelCenter) * sourceToRoot;
            for (var subMesh = 0; subMesh < filter.sharedMesh.subMeshCount; subMesh++)
            {
                combines.Add(new CombineInstance
                {
                    mesh = filter.sharedMesh,
                    subMeshIndex = subMesh,
                    transform = matrix,
                });
            }
        }
        if (combines.Count == 0)
            throw new InvalidOperationException($"No readable meshes were available for '{meshName}'.");

        var combined = new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
        combined.CombineMeshes(combines.ToArray(), true, true, false);
        combined.RecalculateBounds();
        if (combined.normals == null || combined.normals.Length != combined.vertexCount)
            combined.RecalculateNormals();
        combined.UploadMeshData(false);

        EnsureAssetFolder(MeshFolder);
        var persistent = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (persistent == null)
        {
            AssetDatabase.CreateAsset(combined, assetPath);
            persistent = combined;
        }
        else
        {
            EditorUtility.CopySerialized(combined, persistent);
            UnityEngine.Object.DestroyImmediate(combined);
            EditorUtility.SetDirty(persistent);
        }
        return persistent;
    }

    private static Material FirstMaterial(IReadOnlyList<MeshRenderer> renderers)
    {
        foreach (var renderer in renderers)
            foreach (var material in renderer.sharedMaterials)
                if (material != null)
                    return material;
        throw new InvalidOperationException("Generated geometry has no source material.");
    }

    private static void CopyRendererSettings(Renderer source, Renderer target)
    {
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;
        target.motionVectorGenerationMode = source.motionVectorGenerationMode;
        target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        target.renderingLayerMask = source.renderingLayerMask;
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
        throw new InvalidOperationException($"Wheel controller '{controller.name}' has no visual reference.");
    }

    private static int CreateLightOverlayReferences(GameObject model)
    {
        var overlayModel = AssetDatabase.LoadAssetAtPath<GameObject>(LightOverlayModelPath);
        if (overlayModel == null)
        {
            Debug.LogWarning(
                "FerrariSF90Spider: FerrariSF90LightOverlays.glb not found. Vehicle setup will " +
                "remain drivable and headlight beams still work, but DRL/indicator/brake/reverse " +
                "surface overlays must be exported after labeling the lamp mesh in Blender.");
            return 0;
        }

        var lightAnchor = FindTransformWithNameFragment(model.transform, "Light_Geo_lodA_") ??
                          FindTransform(model.transform, "Light_Geo_lodA");
        if (lightAnchor == null)
            throw new InvalidOperationException("SF90 Light_Geo_lodA anchor was not found.");

        var created = 0;
        var candidateNames = CollectOverlayMeshCandidateNames(overlayModel);
        Debug.Log(
            "FerrariSF90Spider V15: imported light-overlay mesh candidates: " +
            (candidateNames.Count == 0 ? "<none>" : string.Join(", ", candidateNames)));

        foreach (var pair in LightOverlayNames)
        {
            var mesh = ResolveOverlayMesh(overlayModel, pair.SourceName);
            if (mesh == null)
            {
                Debug.LogWarning(
                    $"FerrariSF90Spider: overlay mesh '{pair.SourceName}' is missing after checking " +
                    "both the imported hierarchy and all model sub-assets.");
                continue;
            }

            var existing = FindTransform(model.transform, pair.RuntimeName);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            var overlay = new GameObject(pair.RuntimeName);
            overlay.transform.SetParent(lightAnchor, false);
            overlay.transform.localPosition = Vector3.zero;
            overlay.transform.localRotation = Quaternion.identity;
            overlay.transform.localScale = Vector3.one;
            overlay.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = overlay.AddComponent<MeshRenderer>();
            renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            created++;
        }
        return created;
    }

    private static Mesh? ResolveOverlayMesh(GameObject overlayModel, string expectedName)
    {
        // First try the normal imported hierarchy. Depending on Blender/Unity importer
        // versions, the node name or the Mesh sub-asset name can receive suffixes, so
        // compare both names with a tolerant matcher rather than requiring exact equality.
        foreach (var filter in overlayModel.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (mesh == null)
                continue;
            if (OverlayNameMatches(filter.transform.name, expectedName) ||
                OverlayNameMatches(mesh.name, expectedName))
                return mesh;
        }

        // Model importers also expose meshes as sub-assets even when their GameObject node
        // names are rewritten or flattened. This is the most reliable fallback for GLB.
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(LightOverlayModelPath))
        {
            if (asset is Mesh mesh && OverlayNameMatches(mesh.name, expectedName))
                return mesh;
        }

        return null;
    }

    private static bool OverlayNameMatches(string? candidate, string expectedName)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        // Blender can emit .001-style suffixes and Unity can append import suffixes. The
        // exported VehicleLightRef_* token itself remains unique, so substring matching is
        // safe for this dedicated overlay GLB.
        return candidate.IndexOf(expectedName, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static List<string> CollectOverlayMeshCandidateNames(GameObject overlayModel)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var filter in overlayModel.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;
            var entry = $"node='{filter.transform.name}' mesh='{filter.sharedMesh.name}'";
            if (seen.Add(entry))
                names.Add(entry);
        }

        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(LightOverlayModelPath))
        {
            if (!(asset is Mesh mesh))
                continue;
            var entry = $"subasset='{mesh.name}'";
            if (seen.Add(entry))
                names.Add(entry);
        }

        return names;
    }

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
                var assetName = GetPersistentMaterialName(source);
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
                        EditorUtility.SetDirty(persistent);
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

    private static string GetPersistentMaterialName(Material source)
    {
        var name = source.name;
        if (ContainsIgnoreCase(name, PaintMaterialMarker))
            return "FerrariSF90SpiderBodyPaint_2021Paint_Material";
        if (ContainsIgnoreCase(name, CaliperMaterialMarker))
            return "FerrariSF90Spider_" + CaliperMaterialMarker;
        if (ContainsIgnoreCase(name, WheelMaterialMarker))
            return "FerrariSF90Spider_" + WheelMaterialMarker;
        if (ContainsIgnoreCase(name, "Window_Material"))
            return "FerrariSF90SpiderWindow_Material";
        if (ContainsIgnoreCase(name, "RED_GLASS"))
            return "FerrariSF90Spider_RED_GLASS";
        if (ContainsIgnoreCase(name, LightMaterialMarker))
            return "FerrariSF90SpiderLightA_Material";
        return "FerrariSF90Spider_" + SanitizeAssetName(name);
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
                if (material != null && ContainsIgnoreCase(material.name, PaintMaterialMarker))
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


    private static void BuildFerrariAssetBundle()
    {
        DiscoveredMod mod = null;
        foreach (var candidate in ModDiscovery.DiscoverAll())
        {
            if (string.Equals(candidate.ModFolderAssetPath, ModRoot, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.Manifest.ModId, "FerrariSF90Spider", StringComparison.OrdinalIgnoreCase))
            {
                mod = candidate;
                break;
            }
        }

        if (mod == null)
            throw new InvalidOperationException(
                "FerrariSF90Spider V15: Mod Builder could not discover the Ferrari mod. " +
                "Make sure ModManifest.asset exists and has finished importing.");

        var bundleName = mod.Manifest.AssetBundleName;
        if (string.IsNullOrWhiteSpace(bundleName))
            throw new InvalidOperationException("FerrariSF90Spider V15: ModManifest.AssetBundleName is empty.");

        if ((mod.Manifest.TargetPlatforms & ModTargetPlatforms.Windows) == 0)
            throw new InvalidOperationException(
                "FerrariSF90Spider V15: Windows is not enabled in ModManifest.TargetPlatforms.");

        var assignedCount = AssignFerrariBundleableAssets(mod, bundleName);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log($"FerrariSF90Spider V15: updated {assignedCount} AssetBundle assignment(s) for '{bundleName}'.");

        var assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
        if (assetPaths == null || assetPaths.Length == 0)
            throw new InvalidOperationException(
                $"FerrariSF90Spider V15: no assets are assigned to AssetBundle '{bundleName}'.");

        var (baseName, variant) = SplitFerrariBundleName(bundleName);
        var build = new AssetBundleBuild
        {
            assetBundleName = baseName,
            assetBundleVariant = variant,
            assetNames = assetPaths,
        };

        var outputDir = Path.Combine(mod.ModFolderAbsolutePath, "AssetBundles", "Windows");
        Directory.CreateDirectory(outputDir);

        var manifest = BuildPipeline.BuildAssetBundles(
            outputDir,
            new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
            throw new InvalidOperationException(
                "FerrariSF90Spider V15: Unity returned null while building the Windows AssetBundle.");

        var producedPath = Path.Combine(outputDir, bundleName);
        if (!File.Exists(producedPath))
            throw new FileNotFoundException(
                "FerrariSF90Spider V15: expected Windows AssetBundle was not created.", producedPath);

        var length = new FileInfo(producedPath).Length;
        if (length < 256)
            throw new InvalidOperationException(
                $"FerrariSF90Spider V15: built AssetBundle is unexpectedly small ({length} bytes): {producedPath}");

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log(
            $"FerrariSF90Spider V15: built Windows AssetBundle '{producedPath}' " +
            $"with {assetPaths.Length} assigned asset(s), size={length:N0} bytes.");
    }

    private static int AssignFerrariBundleableAssets(DiscoveredMod mod, string fullBundleName)
    {
        var (baseName, variant) = SplitFerrariBundleName(fullBundleName);
        var changedCount = 0;
        var bundleableAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assetPath in FindFerrariBundleableModAssets(mod))
            bundleableAssets.Add(assetPath);

        foreach (var assetPath in FindAllFerrariModFileAssets(mod))
        {
            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
                continue;

            if (!bundleableAssets.Contains(assetPath))
            {
                if (string.Equals(importer.assetBundleName, baseName, StringComparison.Ordinal) &&
                    string.Equals(importer.assetBundleVariant, variant, StringComparison.Ordinal))
                {
                    importer.SetAssetBundleNameAndVariant(string.Empty, string.Empty);
                    importer.SaveAndReimport();
                    changedCount++;
                }

                continue;
            }

            if (string.Equals(importer.assetBundleName, baseName, StringComparison.Ordinal) &&
                string.Equals(importer.assetBundleVariant, variant, StringComparison.Ordinal))
                continue;

            importer.SetAssetBundleNameAndVariant(baseName, variant);
            importer.SaveAndReimport();
            changedCount++;
        }

        return changedCount;
    }

    private static IEnumerable<string> FindAllFerrariModFileAssets(DiscoveredMod mod)
    {
        foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { mod.ModFolderAssetPath }))
        {
            var assetPath = NormaliseFerrariAssetPath(AssetDatabase.GUIDToAssetPath(guid));
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                continue;

            if (assetPath.StartsWith(mod.ModFolderAssetPath + "/AssetBundles/", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return assetPath;
        }
    }

    private static IEnumerable<string> FindFerrariBundleableModAssets(DiscoveredMod mod)
    {
        var localesPrefix = mod.Manifest.LocalesFolder != null
            ? NormaliseFerrariAssetPath(AssetDatabase.GetAssetPath(mod.Manifest.LocalesFolder)) + "/"
            : string.Empty;
        var enumsPath = mod.Manifest.EnumsFile != null
            ? NormaliseFerrariAssetPath(AssetDatabase.GetAssetPath(mod.Manifest.EnumsFile))
            : string.Empty;

        foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { mod.ModFolderAssetPath }))
        {
            var assetPath = NormaliseFerrariAssetPath(AssetDatabase.GUIDToAssetPath(guid));
            if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                continue;

            if (string.Equals(assetPath, mod.ManifestAssetPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assetPath, mod.AsmdefAssetPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(assetPath, enumsPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (assetPath.StartsWith(mod.ModFolderAssetPath + "/AssetBundles/", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrEmpty(localesPrefix) &&
                assetPath.StartsWith(localesPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var extension = Path.GetExtension(assetPath);
            if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.Equals(Path.GetFileName(assetPath), "thumbnail.png", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return assetPath;
        }
    }

    private static (string name, string variant) SplitFerrariBundleName(string fullBundleName)
    {
        var idx = fullBundleName.LastIndexOf('.');
        if (idx <= 0 || idx >= fullBundleName.Length - 1)
            return (fullBundleName, string.Empty);
        return (fullBundleName.Substring(0, idx), fullBundleName.Substring(idx + 1));
    }

    private static string NormaliseFerrariAssetPath(string path)
    {
        return string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/').TrimEnd('/');
    }

    private static void CreateManifest()
    {
        var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        if (manifest == null)
        {
            manifest = ScriptableObject.CreateInstance<BAModManifest>();
            AssetDatabase.CreateAsset(manifest, ManifestPath);
        }
        manifest.ModId = "FerrariSF90Spider";
        manifest.DisplayName = "Ferrari SF90 Spider";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "ferrarisf90spider.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows;
        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("SF90 manifest references could not be assigned.");
        EditorUtility.SetDirty(manifest);
    }

    private static void ValidateProjectAssets(bool logSuccess)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabPath) ??
                     throw new InvalidOperationException("FerrariSF90Spider.prefab is missing. Run setup first.");
        ValidatePrefab(prefab, false);
        ValidateAudioFiles();
        if (logSuccess)
            Debug.Log("FerrariSF90Spider project assets validated successfully.");
    }

    private static void ValidatePrefab(GameObject prefab, bool bundleValidation)
    {
        var issues = new List<string>();
        var body = prefab.GetComponent<Rigidbody>();
        if (body == null || Mathf.Abs(body.mass - VehicleMass) > 0.5f)
            issues.Add("mass");
        if (body != null && Vector3.Distance(body.centerOfMass, StableCenterOfMass) > 0.002f)
            issues.Add("centerOfMass");

        foreach (var pair in WheelControllerPositions)
        {
            var controller = FindTransform(prefab.transform, pair.Key);
            if (controller == null || Vector3.Distance(controller.localPosition, pair.Value) > 0.005f)
                issues.Add(pair.Key);
        }
        foreach (var corner in new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" })
        {
            if (FindTransform(prefab.transform, "FerrariSF90SpiderWheel" + corner) == null)
                issues.Add("wheel:" + corner);
            if (FindTransform(prefab.transform, "FerrariSF90SpiderFixedCaliper" + corner) == null)
                issues.Add("caliper:" + corner);
        }
        if (FindTransform(prefab.transform, "STEERING_WHEEL") == null)
            issues.Add("STEERING_WHEEL marker");
        if (FindTransform(prefab.transform, "Spotlights") == null)
            issues.Add("Spotlights beam donor");
        if (!HasRuntimeComponent(prefab, "FerrariSF90SpiderPaintController"))
            issues.Add("paint bootstrap");

        var referenceLeftovers = new List<string>();
        foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
        {
            if (ReferenceEquals(transform, prefab.transform) ||
                (!IsReferenceOwnedTransformName(transform.name) && !ContainsReferenceBrandToken(transform.name)))
                continue;
            if (referenceLeftovers.Count < 8) referenceLeftovers.Add(transform.name);
        }
        if (referenceLeftovers.Count > 0)
            issues.Add("reference leftovers=" + string.Join("|", referenceLeftovers));

        var paintSlots = 0;
        var glassSlots = 0;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                if (ContainsIgnoreCase(material.name, PaintMaterialMarker)) paintSlots++;
                if (IsCabinGlassMaterialViaRuntime(material)) glassSlots++;
            }
        if (paintSlots == 0) issues.Add("body paint material");
        if (glassSlots == 0) issues.Add("cabin glass material");

        var transmissionVerified = false;
        foreach (var component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null || !string.Equals(component.GetType().FullName, "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
                continue;
            var serialized = new SerializedObject(component);
            var count = FindRelativeProperty(serialized, "powertrain.transmission.forwardGearCount");
            var gears = FindRelativeProperty(serialized, "powertrain.transmission.gears");
            transmissionVerified = Mathf.Abs(ReadNumber(count) - 8f) < 0.1f &&
                                   gears != null && gears.isArray && gears.arraySize == SF90SpiderGears.Length;
        }
        if (!transmissionVerified) issues.Add("8-speed transmission");

        if (issues.Count > 0)
            throw new InvalidOperationException("SF90 prefab validation failed: " + string.Join(", ", issues));

        var overlayCount = 0;
        foreach (var pair in LightOverlayNames)
            if (FindTransform(prefab.transform, pair.RuntimeName)?.GetComponent<MeshFilter>()?.sharedMesh != null)
                overlayCount++;
        if (overlayCount < LightOverlayNames.Length)
            Debug.LogWarning(
                $"FerrariSF90Spider validation: functional lamp overlays={overlayCount}/{LightOverlayNames.Length}. " +
                "This is expected until FerrariSF90LightOverlays.glb is created from labeled lamp geometry.");

        if (!bundleValidation && TryGetFerrariRendererBounds(prefab.transform, out var bounds))
            Debug.Log($"FerrariSF90Spider visual bounds={bounds.size}, lightOverlays={overlayCount}/13.");
    }

    private static void ValidateAudioFiles()
    {
        foreach (var name in new[]
                 {
                     "EngineCore.wav", "HornLow.wav", "HornHigh.wav"
                 })
        {
            var path = ModRoot + "/Config/Audio/" + name;
            if (!System.IO.File.Exists(path))
                throw new InvalidOperationException($"Required runtime audio file '{path}' is missing.");
        }
    }

    private static bool TryGetFerrariRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled || !IsFerrariRendererViaRuntime(renderer.transform))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private static bool TryGetModelBodyBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (HasMaterialMarker(renderer.sharedMaterials, WheelMaterialMarker) ||
                HasMaterialMarker(renderer.sharedMaterials, CaliperMaterialMarker))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else bounds.Encapsulate(renderer.bounds);
        }
        return found;
    }

    private static bool HasMaterialMarker(IReadOnlyList<Material> materials, string marker)
    {
        for (var index = 0; index < materials.Count; index++)
            if (materials[index] != null && ContainsIgnoreCase(materials[index].name, marker))
                return true;
        return false;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        return null;
    }

    private static Transform? FindTransformWithNameFragment(Transform root, string fragment)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (ContainsIgnoreCase(transform.name, fragment))
                return transform;
        return null;
    }

    private static void SetLocalPosition(GameObject root, string name, Vector3 value)
    {
        var transform = FindTransform(root.transform, name) ??
                        throw new InvalidOperationException($"Required transform '{name}' is missing.");
        transform.localPosition = value;
    }

    private static void AssignRendererArray(SerializedProperty? property, IReadOnlyList<Renderer> values)
    {
        if (property == null || !property.isArray || property.propertyType == SerializedPropertyType.String)
            return;
        property.arraySize = values.Count;
        for (var index = 0; index < values.Count; index++)
        {
            var element = property.GetArrayElementAtIndex(index);
            if (element.propertyType == SerializedPropertyType.ObjectReference)
                element.objectReferenceValue = values[index];
        }
    }

    private static SerializedProperty? FindRelativeProperty(SerializedObject serialized, string path)
    {
        var parts = path.Split('.');
        SerializedProperty? property = serialized.FindProperty(parts[0]);
        for (var index = 1; index < parts.Length && property != null; index++)
            property = property.FindPropertyRelative(parts[index]);
        return property;
    }

    private static void SetRelativeNumber(SerializedObject serialized, string path, float value)
    {
        var property = FindRelativeProperty(serialized, path);
        if (property == null) return;
        WriteNumber(property, value);
    }

    private static void SetRelativeBool(SerializedObject serialized, string path, bool value)
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
        if (property != null) WriteNumber(property, value);
    }

    private static void WriteNumber(SerializedProperty property, float value)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Float:
                property.floatValue = value;
                break;
            case SerializedPropertyType.Integer:
                property.intValue = Mathf.RoundToInt(value);
                break;
        }
    }

    private static float ReadNumber(SerializedProperty? property)
    {
        if (property == null) return float.NaN;
        if (property.propertyType == SerializedPropertyType.Float) return property.floatValue;
        if (property.propertyType == SerializedPropertyType.Integer) return property.intValue;
        return float.NaN;
    }

    private static bool ContainsIgnoreCase(string value, string marker) =>
        value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string SanitizeAssetName(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '_' && chars[index] != '-')
                chars[index] = '_';
        return new string(chars).Trim('_');
    }

    private static void MarkMaterialsDirty(GameObject root)
    {
        var materials = new HashSet<Material>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && materials.Add(material))
                    EditorUtility.SetDirty(material);
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        var slash = folder.LastIndexOf('/');
        if (slash <= 0) throw new InvalidOperationException($"Invalid asset folder '{folder}'.");
        var parent = folder.Substring(0, slash);
        var leaf = folder.Substring(slash + 1);
        EnsureAssetFolder(parent);
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(parent, leaf);
    }

    private readonly struct MaterialFixSummary
    {
        internal MaterialFixSummary(
            int rendererCount,
            int opaqueMaterialsFixed,
            int transparentMaterialsFixed,
            int cabinGlassRenderers)
        {
            RendererCount = rendererCount;
            OpaqueMaterialsFixed = opaqueMaterialsFixed;
            TransparentMaterialsFixed = transparentMaterialsFixed;
            CabinGlassRenderers = cabinGlassRenderers;
        }

        internal int RendererCount { get; }
        internal int OpaqueMaterialsFixed { get; }
        internal int TransparentMaterialsFixed { get; }
        internal int CabinGlassRenderers { get; }
    }

    // The current SDK compiles mod Editor assemblies without a compile-time
    // reference to the corresponding runtime asmdef. Keep the editor setup
    // independent and resolve the Ferrari runtime helpers only when the setup
    // command actually runs.
    private static Type? FindRuntimeType(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var exact = assembly.GetType(typeName, false);
            if (exact != null)
                return exact;

            IEnumerable<Type?> types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            foreach (var type in types)
            {
                if (type == null)
                    continue;

                if (string.Equals(type.Name, typeName, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                    (type.FullName?.EndsWith("." + typeName, StringComparison.Ordinal) ?? false))
                    return type;
            }
        }

        return null;
    }

    private static Type RequireRuntimeType(string typeName)
    {
        return FindRuntimeType(typeName) ??
               throw new InvalidOperationException(
                   $"Ferrari SF90 runtime type '{typeName}' is not loaded. " +
                   "Let Unity finish compiling FerrariSF90Spider first, then run the setup command again.");
    }

    private static bool HasRuntimeComponent(GameObject root, string typeName)
    {
        var type = FindRuntimeType(typeName);
        return type != null && root.GetComponent(type) != null;
    }

    private static MaterialFixSummary FixSolidMaterialsViaRuntime(GameObject root)
    {
        var type = RequireRuntimeType("FerrariSF90SpiderMaterials");
        var method = type.GetMethod(
            "FixSolidMaterials",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null,
            new[] { typeof(GameObject) },
            null);

        if (method == null)
            throw new MissingMethodException(type.FullName, "FixSolidMaterials(GameObject)");

        var result = method.Invoke(null, new object[] { root });
        if (result == null)
            return default;

        return new MaterialFixSummary(
            ReadRuntimeInt(result, "RendererCount"),
            ReadRuntimeInt(result, "OpaqueMaterialsFixed"),
            ReadRuntimeInt(result, "TransparentMaterialsFixed"),
            ReadRuntimeInt(result, "CabinGlassRenderers"));
    }

    private static bool IsCabinGlassMaterialViaRuntime(Material material)
    {
        var type = RequireRuntimeType("FerrariSF90SpiderMaterials");
        var method = type.GetMethod(
            "IsCabinGlassMaterial",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null,
            new[] { typeof(Material) },
            null);

        if (method == null)
            throw new MissingMethodException(type.FullName, "IsCabinGlassMaterial(Material)");

        return method.Invoke(null, new object[] { material }) is bool value && value;
    }

    private static bool IsFerrariRendererViaRuntime(Transform transform)
    {
        var type = RequireRuntimeType("FerrariSF90SpiderMaterials");
        var method = type.GetMethod(
            "IsFerrariRenderer",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null,
            new[] { typeof(Transform) },
            null);

        if (method == null)
            throw new MissingMethodException(type.FullName, "IsFerrariRenderer(Transform)");

        return method.Invoke(null, new object[] { transform }) is bool value && value;
    }

    private static int ReadRuntimeInt(object result, string propertyName)
    {
        var property = result.GetType().GetProperty(
            propertyName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (property?.GetValue(result) is int value)
            return value;
        return 0;
    }

}
