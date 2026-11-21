#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BAModTemplate.Editor;
using GLTFast;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Vehicles.VehicleTypes;

/// <summary>
/// Repeatable target-only authoring entry point for the Bentley's Unity assets.
/// The native rig is unpacked before saving so no vehicle-mod prefab becomes a
/// serialized dependency of this mod.
/// </summary>
public static class BentleyContinentalGTSpeedSetup
{
    private const string Root = "Assets/Mods/Bentley_Continental_GT_Speed";
    private const string NativeRigPath = "Assets/Mods/McLaren_Senna/McLarenSennaTemplate.prefab";
    private const string PrefabPath = Root + "/BentleyContinentalGTSpeed.prefab";
    private const string VehicleTypePath = Root + "/BentleyContinentalGTSpeed.asset";
    private const string ManifestPath = Root + "/ModManifest.asset";
    private const string MeshFolder = Root + "/Models/GeneratedMeshes";
    private const string MaterialFolder = Root + "/Models/GeneratedMaterials";
    private const string TextureFolder = MaterialFolder + "/Textures";
    private const string AssemblyPath = Root + "/BentleyContinentalGTSpeed.asmdef";
    private const string LocalePath = Root + "/Locales";
    private const string VehicleTypeName =
        "bentleycontinentalgtspeed-vehicle:vehicletype_bentleycontinentalgtspeed";
    private const string DonorVisualName = "2020_abt_sportline_McLaren_rs6-r";
    private const string RoadBeamName = "Spotlights";
    private const float RoadBeamForwardMeters = 1.99f;
    private const string DriverSteeringAnchorName = "BentleyPlayerSteeringWheelAnchor";
    private const string DriverSeatAnchorName = "BentleyDriverSeatAnchor";
    private const float TargetLength = 4.895f;
    // Raise the previously tested shell by 2 cm; physical tire heights stay fixed.
    private const float VisualGroundingOffsetMeters = 0.14f;
    private const float FrontAxleForwardOffsetMeters = 0.01f;
    private const float WheelOutwardOffsetMeters = 0.04f;

    private static readonly (string Corner, string PivotFragment, string VisualName)[] WheelPivots =
    {
        ("FrontLeft", "Calliper Front L_", "3DWheel Front L"),
        ("FrontRight", "Calliper Front R_", "3DWheel Front R"),
        ("RearLeft", "Calliper Rear L_", "3DWheel Rear L"),
        ("RearRight", "Calliper Rear R_", "3DWheel Rear R"),
    };

    [MenuItem("Big Ambitions Mods/Setup Bentley Continental GT Speed")]
    public static async void Generate()
    {
        try
        {
            await GenerateAsync();
        }
        catch (Exception exception)
        {
            Debug.LogError("BentleyContinentalGTSpeed setup failed: " + exception);
        }
    }

    /// <summary>Batch-mode entry point; exits only after the asynchronous GLB import finishes.</summary>
    public static async void GenerateBatch()
    {
        try
        {
            await GenerateAsync();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("BentleyContinentalGTSpeed setup failed: " + exception);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>Regenerates authored assets, builds both platform bundles, then exits batch mode.</summary>
    public static async void GenerateAndBuildAssetBundlesBatch()
    {
        try
        {
            await GenerateAsync();
            BentleyContinentalGTSpeedAssetChecks.Verify();
            BuildAssetBundles();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("BentleyContinentalGTSpeed setup/bundle build failed: " + exception);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem("Big Ambitions Mods/Repair Bentley Runtime Assets and Build Bundles")]
    public static void RepairRuntimeAssets()
    {
        try
        {
            RepairCurrentPrefab();
            BuildAssetBundles();
        }
        catch (Exception exception)
        {
            Debug.LogError("BentleyContinentalGTSpeed repair failed: " + exception);
        }
    }

    public static void RepairAndBuildAssetBundlesBatch()
    {
        try
        {
            RepairCurrentPrefab();
            BuildAssetBundles();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogError("BentleyContinentalGTSpeed repair/bundle build failed: " + exception);
            EditorApplication.Exit(1);
        }
    }

    private static void RepairCurrentPrefab()
    {
        EnsureFolders();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
            throw new InvalidOperationException("The existing Bentley player prefab is missing: " + PrefabPath);
        var vehicleType = AssetDatabase.LoadAssetAtPath<VehicleType>(VehicleTypePath);
        if (vehicleType == null)
            throw new InvalidOperationException("The existing Bentley VehicleType asset is missing: " + VehicleTypePath);
        try
        {
            var visualRoot = FindTransform(root.transform, "BentleyVisual") ??
                             throw new InvalidOperationException("Existing Bentley prefab lacks BentleyVisual.");
            DisableDonorVisual(root.transform);
            SetVisualGroundingOffset(visualRoot, VisualGroundingOffsetMeters);
            BentleyContinentalGTSpeedAppearanceSetup.Apply(root);
            BentleyContinentalGTSpeedEngineGeometrySetup.Apply(visualRoot.gameObject);
            AuthorPaintRendererBindings(root, visualRoot);
            PlaceNativeWheelControllers(root, visualRoot, visualRoot.localScale.x,
                VisualGroundingOffsetMeters);
            BentleyContinentalGTSpeedWheelGeometrySetup.CorrectRollingMesh(root.transform);
            ConfigureRoadBeamPosition(root.transform);
            ConfigureDriverAnchors(root.transform);
            BentleyContinentalGTSpeedDrivingSetup.Configure(root, vehicleType);
            BentleyContinentalGTSpeedWashSetup.Author(root);
            BentleyContinentalGTSpeedAssetChecks.AuthorExitNavigationGeometry(root);
            BentleyContinentalGTSpeedLightingSetup.Author(root);
            SavePrefab(root);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        VerifyTargetAssetOwnership();
        BentleyContinentalGTSpeedAssetChecks.Verify();
        Debug.Log("BentleyContinentalGTSpeed existing prefab repaired: saved paint/glass, " +
                  "NWH-owned rolling/steering wheels, runtime driver anchors, legal AutoPark preservation, " +
                  "donor-render suppression, NavMesh obstacle safeguards, telemetry-calibrated dynamics " +
                  "and foreign-reference validation complete.");
    }

    private static void BuildAssetBundles()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var assetPaths = AssetDatabase.FindAssets(string.Empty, new[] { Root })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path =>
            {
                var importer = AssetImporter.GetAtPath(path);
                return importer != null &&
                       string.Equals(importer.assetBundleName, "bentleycontinentalgtspeed", StringComparison.Ordinal) &&
                       string.Equals(importer.assetBundleVariant, "unity3d", StringComparison.Ordinal) &&
                       !string.Equals(Path.GetExtension(path), ".glb", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (assetPaths.Length == 0 || !assetPaths.Contains(PrefabPath, StringComparer.Ordinal) ||
            !assetPaths.Contains(VehicleTypePath, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Bentley bundle build is missing the target prefab, VehicleType, or assigned bundle assets.");

        var build = new AssetBundleBuild
        {
            assetBundleName = "bentleycontinentalgtspeed",
            assetBundleVariant = "unity3d",
            assetNames = assetPaths,
        };
        var targets = new[]
        {
            (BuildTarget.StandaloneWindows64, "Windows"),
            (BuildTarget.StandaloneOSX, "Mac"),
        };
        foreach (var (target, folder) in targets)
        {
            var outputPath = Path.Combine(Root, "AssetBundles", folder);
            Directory.CreateDirectory(outputPath);
            var manifest = BuildPipeline.BuildAssetBundles(
                outputPath,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                target);
            var bundleName = "bentleycontinentalgtspeed.unity3d";
            var bundlePath = Path.Combine(outputPath, bundleName);
            if (manifest == null || !manifest.GetAllAssetBundles().Contains(bundleName) ||
                !File.Exists(bundlePath) || new FileInfo(bundlePath).Length < 256)
                throw new InvalidOperationException(
                    "Bentley " + folder + " AssetBundle did not build correctly: " + bundlePath);

            var dependencies = manifest.GetDirectDependencies(bundleName);
            if (dependencies.Length != 0)
                throw new InvalidOperationException(
                    "Bentley " + folder + " bundle has external bundle dependencies: " +
                    string.Join(", ", dependencies));
            Debug.Log(
                $"Bentley {folder} AssetBundle built: {bundlePath}, " +
                $"size={new FileInfo(bundlePath).Length:N0} bytes, assigned assets={assetPaths.Length}.");
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    private static async Task GenerateAsync()
    {
        EnsureFolders();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var modelFile = Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "Mods/Bentley_Continental_GT_Speed/Models/2025_bentley_continental_gt_speed.glb"));
        if (!File.Exists(modelFile))
            throw new FileNotFoundException("Bentley source GLB was not found.", modelFile);

        var rig = AssetDatabase.LoadAssetAtPath<GameObject>(NativeRigPath);
        if (rig == null)
            throw new InvalidOperationException("The neutral native vehicle rig is missing: " + NativeRigPath);

        var vehicleType = GetOrCreateVehicleType();
        var manifest = GetOrCreateManifest();
        var prefabRoot = InstantiateUnpackedRig(rig);
        // The GLB is imported from an editor authoring command, where creating
        // glTFast's persistent runtime defer-agent object is not allowed.
        var importer = new GltfImport(deferAgent: new UninterruptedDeferAgent());
        GameObject? visualRoot = null;
        try
        {
            var modelUri = new Uri(modelFile, UriKind.Absolute);
            if (!await importer.Load(modelUri))
                throw new InvalidOperationException("glTFast 6.14.1 could not load the Bentley GLB.");

            visualRoot = new GameObject("BentleyVisual");
            visualRoot.transform.SetParent(prefabRoot.transform, false);
            if (!await importer.InstantiateMainSceneAsync(visualRoot.transform))
                throw new InvalidOperationException("glTFast 6.14.1 could not instantiate the Bentley scene.");

            RemoveImportedCamerasAndLights(visualRoot);
            var modelBounds = GetRendererBounds(visualRoot.transform);
            if (modelBounds.size.z <= modelBounds.size.x || modelBounds.size.z <= modelBounds.size.y)
                throw new InvalidOperationException(
                    "The imported Bentley GLB is not oriented X-left/Y-up/Z-forward; measured bounds=" +
                    modelBounds.size + ".");

            var uniformScale = TargetLength / modelBounds.size.z;
            visualRoot.transform.localScale = Vector3.one * uniformScale;
            visualRoot.transform.localPosition = new Vector3(
                -modelBounds.center.x * uniformScale,
                -modelBounds.min.y * uniformScale - VisualGroundingOffsetMeters,
                -modelBounds.center.z * uniformScale);

            BentleyContinentalGTSpeedAppearanceSetup.Apply(visualRoot);
            BentleyContinentalGTSpeedEngineGeometrySetup.Apply(visualRoot);
            PersistGeometryAndMaterials(visualRoot.transform);
            DisableDonorVisual(prefabRoot.transform);
            AssignVehicleType(prefabRoot, vehicleType);
            PlaceNativeWheelControllers(prefabRoot, visualRoot.transform, uniformScale,
                VisualGroundingOffsetMeters);
            BentleyContinentalGTSpeedWheelGeometrySetup.ExtractAndBind(
                prefabRoot.transform, visualRoot.transform);
            BentleyContinentalGTSpeedWheelGeometrySetup.CorrectRollingMesh(prefabRoot.transform);
            AuthorPaintRendererBindings(prefabRoot, visualRoot.transform);
            ConfigureRoadBeamPosition(prefabRoot.transform);
            ConfigureDriverAnchors(prefabRoot.transform);
            BentleyContinentalGTSpeedDrivingSetup.Configure(prefabRoot, vehicleType);
            BentleyContinentalGTSpeedWashSetup.Author(prefabRoot);
            BentleyContinentalGTSpeedAssetChecks.AuthorExitNavigationGeometry(prefabRoot);
            BentleyContinentalGTSpeedLightingSetup.Author(prefabRoot);
            SavePrefab(prefabRoot);
        }
        finally
        {
            importer.Dispose();
            if (prefabRoot != null)
                UnityEngine.Object.DestroyImmediate(prefabRoot);
            if (visualRoot != null && visualRoot != null)
                UnityEngine.Object.DestroyImmediate(visualRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        VerifyTargetAssetOwnership();
        EditorUtility.SetDirty(vehicleType);
        EditorUtility.SetDirty(manifest);
        Debug.Log(
            "BentleyContinentalGTSpeed authoring complete: target-owned GLB meshes/materials, native " +
            "player rig, four measured wheel centers, VehicleType identity and manifest saved. " +
            "Runtime features and contract evidence remain unverified.");
    }

    private static void EnsureFolders()
    {
        EnsureFolder(Root + "/Models/GeneratedMeshes");
        EnsureFolder(Root + "/Models/GeneratedMaterials/Textures");
    }

    private static void EnsureFolder(string path)
    {
        var parts = path.Split('/');
        var current = parts[0];
        for (var index = 1; index < parts.Length; index++)
        {
            var next = current + "/" + parts[index];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[index]);
            current = next;
        }
    }

    private static GameObject InstantiateUnpackedRig(GameObject rig)
    {
        var root = PrefabUtility.InstantiatePrefab(rig) as GameObject ??
                   throw new InvalidOperationException("Could not instantiate the neutral native vehicle rig.");
        PrefabUtility.UnpackPrefabInstance(
            root,
            PrefabUnpackMode.Completely,
            InteractionMode.AutomatedAction);
        root.name = "BentleyContinentalGTSpeed";
        return root;
    }

    private static void RemoveImportedCamerasAndLights(GameObject model)
    {
        foreach (var camera in model.GetComponentsInChildren<Camera>(true))
            UnityEngine.Object.DestroyImmediate(camera);
        foreach (var light in model.GetComponentsInChildren<Light>(true))
            UnityEngine.Object.DestroyImmediate(light);
    }

    private static Bounds GetRendererBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer != null && renderer.enabled)
            .ToArray();
        if (renderers.Length == 0)
            throw new InvalidOperationException("The Bentley model has no renderable mesh geometry.");

        var bounds = renderers[0].bounds;
        for (var index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static void PersistGeometryAndMaterials(Transform model)
    {
        var meshAssets = new Dictionary<Mesh, Mesh>();
        var materialAssets = new Dictionary<Material, Material>();
        var textureAssets = new Dictionary<Texture2D, Texture2D>();
        var meshIndex = 0;
        var materialIndex = 0;
        var textureIndex = 0;

        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;
            if (!meshAssets.TryGetValue(filter.sharedMesh, out var persisted))
            {
                var meshName = "BentleyMesh_" + meshIndex++.ToString("D3");
                var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
                mesh.name = meshName;
                persisted = PersistAsset(
                    mesh,
                    MeshFolder + "/" + meshName + ".asset");
                meshAssets.Add(filter.sharedMesh, persisted);
            }
            filter.sharedMesh = persisted;
        }

        foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.sharedMesh == null)
                continue;
            if (!meshAssets.TryGetValue(renderer.sharedMesh, out var persisted))
            {
                var meshName = "BentleyMesh_" + meshIndex++.ToString("D3");
                var mesh = UnityEngine.Object.Instantiate(renderer.sharedMesh);
                mesh.name = meshName;
                persisted = PersistAsset(
                    mesh,
                    MeshFolder + "/" + meshName + ".asset");
                meshAssets.Add(renderer.sharedMesh, persisted);
            }
            renderer.sharedMesh = persisted;
        }

        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (var slot = 0; slot < materials.Length; slot++)
            {
                var source = materials[slot];
                if (source == null)
                    continue;
                if (!materialAssets.TryGetValue(source, out var persisted))
                {
                    var copy = UnityEngine.Object.Instantiate(source);
                    foreach (var propertyName in copy.GetTexturePropertyNames())
                    {
                        if (copy.GetTexture(propertyName) is not Texture2D sourceTexture)
                            continue;
                        if (!textureAssets.TryGetValue(sourceTexture, out var texture))
                        {
                            texture = PersistAsset(
                                UnityEngine.Object.Instantiate(sourceTexture),
                                TextureFolder + "/BentleyTexture_" + textureIndex++.ToString("D3") + ".asset");
                            textureAssets.Add(sourceTexture, texture);
                        }
                        copy.SetTexture(propertyName, texture);
                    }
                    persisted = PersistAsset(
                        copy,
                        MaterialFolder + "/BentleyMaterial_" + materialIndex++.ToString("D3") + ".mat");
                    materialAssets.Add(source, persisted);
                }
                materials[slot] = persisted;
            }
            renderer.sharedMaterials = materials;
        }
    }

    private static T PersistAsset<T>(T source, string path) where T : UnityEngine.Object
    {
        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(source, existing);
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(source);
            return existing;
        }

        AssetDatabase.CreateAsset(source, path);
        EditorUtility.SetDirty(source);
        return source;
    }

    private static void AssignVehicleType(GameObject root, VehicleType vehicleType)
    {
        var found = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var vehicleTypeProperty = serialized.FindProperty("vehicleType");
            if (vehicleTypeProperty?.propertyType == SerializedPropertyType.ObjectReference)
                vehicleTypeProperty.objectReferenceValue = vehicleType;
            if (string.Equals(component.GetType().FullName,
                    "NWH.VehiclePhysics2.VehicleController", StringComparison.Ordinal))
                found = true;
            var vehicleInstance = serialized.FindProperty("vehicleInstance");
            var nameProperty = vehicleInstance?.FindPropertyRelative("vehicleTypeName");
            if (nameProperty?.propertyType == SerializedPropertyType.String)
                nameProperty.stringValue = VehicleTypeName;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        if (!found)
            throw new InvalidOperationException("Native NWH VehicleController is missing from the rig.");

        var nativeVehicle = root.GetComponent<VehicleController>();
        if (nativeVehicle == null)
            throw new InvalidOperationException("Native game VehicleController is missing from the rig.");

        RemoveForeignModReferences(root);
    }

    private static void AuthorPaintRendererBindings(GameObject root, Transform visualRoot)
    {
        var features = root.GetComponent<CarFeatures>() ??
                       throw new InvalidOperationException("Bentley CarFeatures is missing.");
        // Extracted calipers live beside BentleyVisual under the native uprights.
        var renderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(renderer => renderer.sharedMaterials.Any(material => material != null &&
                (material.name.StartsWith("Bentley_Paint_Prepared", StringComparison.Ordinal) ||
                 material.name.StartsWith("Bentley_OEMPaint_Prepared", StringComparison.Ordinal) ||
                 (string.Equals(material.name, "Bentley_CaliperAZonePaint_Prepared", StringComparison.Ordinal) &&
                  IsLiveExtractedCaliperRenderer(renderer)))))
            .ToArray();
        if (renderers.Length == 0)
            throw new InvalidOperationException("Bentley paint renderer bindings are empty.");
        var serialized = new SerializedObject(features);
        var meshes = serialized.FindProperty("bodyMeshes");
        if (meshes == null || !meshes.isArray)
            throw new InvalidOperationException("Native CarFeatures bodyMeshes array is missing.");
        meshes.arraySize = renderers.Length;
        for (var index = 0; index < renderers.Length; index++)
            meshes.GetArrayElementAtIndex(index).objectReferenceValue = renderers[index];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static bool IsLiveExtractedCaliperRenderer(Renderer renderer)
    {
        if (renderer == null || !renderer.enabled || !renderer.gameObject.activeSelf ||
            renderer.sharedMaterials.Length != 1)
            return false;

        for (var node = renderer.transform.parent; node != null; node = node.parent)
        {
            if (!node.gameObject.activeSelf) return false;
            if (node.name == "FrontLeft_UprightVisual" || node.name == "FrontRight_UprightVisual" ||
                node.name == "RearLeft_UprightVisual" || node.name == "RearRight_UprightVisual")
                return true;
        }
        return false;
    }

    private static void RemoveForeignModReferences(GameObject root)
    {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null || component is Transform)
                continue;
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            var changed = false;
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    property.objectReferenceValue == null)
                    continue;
                var referencedPath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                if (!referencedPath.StartsWith("Assets/Mods/", StringComparison.OrdinalIgnoreCase) ||
                    referencedPath.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase))
                    continue;
                property.objectReferenceValue = null;
                changed = true;
            }
            if (changed)
                serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void PlaceNativeWheelControllers(
        GameObject root,
        Transform visualRoot,
        float scale,
        float visualGroundingOffsetMeters)
    {
        var nwhWheelCount = root.GetComponentsInChildren<Component>(true)
            .Count(component => component != null && component.GetType().Name == "WheelController");
        if (nwhWheelCount != 4)
            throw new InvalidOperationException("Expected four native wheel controllers, found " + nwhWheelCount + ".");

        foreach (var wheel in WheelPivots)
        {
            var candidates = visualRoot.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.name.StartsWith(wheel.PivotFragment, StringComparison.Ordinal))
                .ToArray();
            if (candidates.Length == 0)
                throw new InvalidOperationException("The Bentley model is missing measured wheel pivot '" + wheel.PivotFragment + "'.");

            // Keep the physical wheel centers at their authored ground-aligned
            // positions even though the body visual is lowered independently.
            var authoredPivot = candidates[0].position;
            if (candidates.Any(candidate => Vector3.Distance(candidate.position, authoredPivot) > 0.002f))
                throw new InvalidOperationException("Duplicate Bentley wheel pivots disagree for " + wheel.Corner + ".");
            var measured = authoredPivot + visualRoot.up * visualGroundingOffsetMeters;
            if (wheel.Corner.StartsWith("Front", StringComparison.Ordinal))
                measured += root.transform.forward * FrontAxleForwardOffsetMeters;

            var controller = FindTransform(root.transform, wheel.Corner + "_WheelController") ??
                             throw new InvalidOperationException("Native wheel controller transform is missing: " + wheel.Corner);
            var center = root.transform.InverseTransformPoint(measured);
            center.x += Mathf.Sign(center.x) * WheelOutwardOffsetMeters;
            controller.localPosition = center;

            var visual = FindTransform(root.transform, wheel.VisualName) ??
                         throw new InvalidOperationException("Bentley wheel visual is missing: " + wheel.VisualName);
            var wheelController = controller.GetComponents<Component>()
                .FirstOrDefault(component => component != null &&
                    string.Equals(component.GetType().Name, "WheelController", StringComparison.Ordinal)) ??
                throw new InvalidOperationException("Native WheelController component is missing: " + wheel.Corner);
            var serializedWheel = new SerializedObject(wheelController);
            var visualBinding = serializedWheel.FindProperty("wheel.visual");
            var visualTransformBinding = serializedWheel.FindProperty("wheel.visualTransform");
            if (visualBinding == null || visualBinding.propertyType != SerializedPropertyType.ObjectReference)
                throw new InvalidOperationException("Could not inspect native wheel visual binding: " + wheel.Corner);
            if (visualTransformBinding == null ||
                visualTransformBinding.propertyType != SerializedPropertyType.ObjectReference)
                throw new InvalidOperationException("Could not inspect native wheel visual transform binding: " + wheel.Corner);
            visual.position = controller.position;
            var upright = FindTransform(root.transform, wheel.Corner + "_UprightVisual");
            if (upright != null)
                upright.position = controller.position;
            if (visual.parent != root.transform)
                visual.SetParent(root.transform, true);
            visualBinding.objectReferenceValue = visual.gameObject;
            // Match the working Porsche/Bugatti vehicle prefabs: NWH receives
            // the wheel visual GameObject and resolves its native pivot itself.
            // The explicit visualTransform field is null on those vehicles.
            visualTransformBinding.objectReferenceValue = null;
            serializedWheel.ApplyModifiedPropertiesWithoutUndo();
            if (serializedWheel.FindProperty("wheel.visual")?.objectReferenceValue != visual.gameObject)
                throw new InvalidOperationException("Native wheel visual binding did not persist: " + wheel.Corner);
            if (serializedWheel.FindProperty("wheel.visualTransform")?.objectReferenceValue != null)
                throw new InvalidOperationException("Native wheel visual transform must match the working vehicle setup: " + wheel.Corner);
            if (Vector3.Distance(visual.position, controller.position) > 0.002f)
                throw new InvalidOperationException("Bentley wheel visual did not align with its controller: " + wheel.Corner);

            var side = wheel.Corner.EndsWith("Left", StringComparison.Ordinal) ? "left" : "right";
            var axle = wheel.Corner.StartsWith("Front", StringComparison.Ordinal) ? "front" : "rear";
            Debug.Log(
                $"BentleyContinentalGTSpeed measured {axle}-{side} wheel center=" +
                controller.localPosition + $" m after model scale {scale:F4}; visual='{visual.name}' aligned.");
        }
    }

    private static void DisableDonorVisual(Transform root)
    {
        var donor = FindTransform(root, DonorVisualName);
        if (donor == null)
            return;

        // The VehicleController and NavMeshObstacleToggler both reference the
        // donor's correctly scaled carving obstacle. Keep that helper active,
        // while disabling its template renderers so only the Bentley is shown.
        if (!donor.gameObject.activeSelf)
            donor.gameObject.SetActive(true);
        var renderers = donor.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
            if (renderer != null && renderer.enabled)
                renderer.enabled = false;

        var obstacle = donor.GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle == null || !obstacle.enabled)
        {
            throw new InvalidOperationException(
                "Bentley donor helper is missing its active NavMeshObstacle: " + DonorVisualName);
        }
        Debug.Log(
            $"BentleyContinentalGTSpeed kept the scaled NavMesh obstacle on '{DonorVisualName}' active " +
            $"and disabled {renderers.Length} donor renderer(s).");
    }

    private static void ConfigureRoadBeamPosition(Transform root)
    {
        var roadBeam = FindTransform(root, RoadBeamName) ??
                       throw new InvalidOperationException("Bentley road-light Spotlights emitter is missing.");
        var position = roadBeam.localPosition;
        position.z = RoadBeamForwardMeters;
        roadBeam.localPosition = position;
    }

    private static void ConfigureDriverAnchors(Transform root)
    {
        var wheelPivot = FindTransform(root, "Animate_SteeringWheel_27") ??
                         FindTransform(root, "SteeringWheelBase_137") ??
                         throw new InvalidOperationException("Bentley steering-wheel anchor source is missing.");
        var steeringAnchor = FindTransform(root, DriverSteeringAnchorName);
        if (steeringAnchor == null)
        {
            steeringAnchor = new GameObject(DriverSteeringAnchorName).transform;
            steeringAnchor.SetParent(root, false);
        }
        steeringAnchor.localPosition = root.InverseTransformPoint(wheelPivot.position);
        steeringAnchor.localRotation = Quaternion.Inverse(root.rotation) * wheelPivot.rotation;

        var seatAnchor = FindTransform(root, DriverSeatAnchorName);
        if (seatAnchor == null)
        {
            seatAnchor = new GameObject(DriverSeatAnchorName).transform;
            seatAnchor.SetParent(root, false);
        }
        // The player pelvis sits below and behind the wheel center; keep this
        // target in vehicle axes so it follows the model's lowered cabin.
        seatAnchor.localPosition = steeringAnchor.localPosition + new Vector3(0f, -0.30f, -0.45f);
        seatAnchor.localRotation = steeringAnchor.localRotation;
    }

    private static void SetVisualGroundingOffset(Transform visualRoot, float offsetMeters)
    {
        // The current GLB import's grounded root position is stable and stored
        // in the prefab. Assign an absolute target so repeat repair/build runs
        // do not accumulate the body offset.
        const float groundedRootY = 0.007489137f;
        var position = visualRoot.localPosition;
        position.y = groundedRootY - offsetMeters;
        visualRoot.localPosition = position;
    }

    private static Transform? FindTransform(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(transform => string.Equals(transform.name, name, StringComparison.Ordinal));

    private static void SavePrefab(GameObject root)
    {
        if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
            throw new InvalidOperationException("Could not save target-owned player prefab: " + PrefabPath);
    }

    private static VehicleType GetOrCreateVehicleType()
    {
        var vehicleType = AssetDatabase.LoadAssetAtPath<VehicleType>(VehicleTypePath);
        if (vehicleType == null)
        {
            vehicleType = ScriptableObject.CreateInstance<VehicleType>();
            AssetDatabase.CreateAsset(vehicleType, VehicleTypePath);
        }
        vehicleType.name = "BentleyContinentalGTSpeed";
        var serialized = new SerializedObject(vehicleType);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(vehicleType);
        return vehicleType;
    }

    private static BAModManifest GetOrCreateManifest()
    {
        var manifest = AssetDatabase.LoadAssetAtPath<BAModManifest>(ManifestPath);
        if (manifest == null)
        {
            manifest = ScriptableObject.CreateInstance<BAModManifest>();
            AssetDatabase.CreateAsset(manifest, ManifestPath);
        }
        manifest.ModId = "BentleyContinentalGTSpeed";
        manifest.DisplayName = "Bentley Continental GT Speed";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "bentleycontinentalgtspeed.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalePath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows | ModTargetPlatforms.Mac;
        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Bentley manifest is missing target assembly or localization folder.");
        EditorUtility.SetDirty(manifest);
        return manifest;
    }

    private static void VerifyTargetAssetOwnership()
    {
        foreach (var path in new[] { PrefabPath, VehicleTypePath })
        {
            foreach (var dependency in AssetDatabase.GetDependencies(path, true))
            {
                if (dependency.StartsWith("Assets/Mods/", StringComparison.OrdinalIgnoreCase) &&
                    !dependency.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Saved Bentley asset contains a foreign mod dependency: " + path + " -> " + dependency);
            }
        }
    }

    private static void SetString(SerializedObject serialized, string propertyName, string value)
    {
        var property = serialized.FindProperty(propertyName);
        if (property?.propertyType == SerializedPropertyType.String)
            property.stringValue = value;
    }
}
