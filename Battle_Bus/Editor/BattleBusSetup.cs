#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

public static partial class BattleBusSetup
{
    private const string HdrpMaterialTypeName = "UnityEngine.Rendering.HighDefinition.HDMaterial";
    private const string ShaderGraphApiTypeName = "UnityEngine.Rendering.HighDefinition.ShaderGraphAPI";
    private const string BundleName = "battlebus";
    private const string AudiAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string AudiPrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = "Assets/Mods/Battle_Bus/Models/battle_bus_fortnite.glb";
    private const string GeneratedMeshFolder = "Assets/Mods/Battle_Bus/Models/GeneratedMeshes";
    private const string GeneratedMaterialFolder = "Assets/Mods/Battle_Bus/Models/GeneratedMaterials";
    private const string VehicleAssetPath = "Assets/Mods/Battle_Bus/BattleBus.asset";
    private const string VehiclePrefabPath = "Assets/Mods/Battle_Bus/BattleBus.prefab";
    private const string FlameMaterialPath = "Assets/Mods/Battle_Bus/Models/GeneratedMeshes/BalloonFlame.mat";
    private const string FlameTexturePath = "Assets/Mods/Battle_Bus/Models/GeneratedMeshes/BalloonFlameSprite.asset";
    private const string VehicleTypeName = "battle-bus-vehicle:vehicletype_battlebus";
    private const string PaintableHullRendererName = "Object_14";
    private const string SourceStaticFlameRendererName = "Object_8";
    private const string PaintableHullMeshPrefix = "PaintableBody_";
    private const float TargetLength = 8.5f;
    private const float TargetVehicleMass = 4800f;
    private const float TargetAntiRollBarForce = 24000f;
    private const float WheelWeldTolerance = 0.001f;
    private const float BalloonSplitHeight = 4.5f;
    private const float WheelSeedCenterTolerance = 0.10f;
    private const float WheelRowTolerance = 0.15f;
    private const float FrontMaximumSteerAngle = 59f;
    private const float FrontSteeringDegreesPerSecond = 100f;
    private static MethodInfo? HdrpValidateMaterialMethod;
    private static bool HdrpValidateMaterialMethodResolved;
    private static MethodInfo? ShaderGraphValidateMaterialMethod;
    private static bool ShaderGraphValidateMaterialMethodResolved;

    [MenuItem("Big Ambitions Mods/Setup Battle Bus")]
    public static void Generate()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        RemoveUnusedGeneratedMeshes();
        RemoveUnusedGeneratedMaterials();
        SetBundle(VehicleAssetPath);
        SetBundle(VehiclePrefabPath);
        AssetDatabase.SaveAssets();
        BuildBundles();
        Debug.Log("Battle Bus setup complete: generated car VehicleType, six tire visuals, " +
                  "four wheel controllers, collision-free balloon, gas-station service bounds and flight runtime.");
    }

    public static void BuildBundles()
    {
        var targets = new[]
        {
            (BuildTarget.StandaloneWindows64, "Windows"),
            (BuildTarget.StandaloneOSX, "Mac"),
        };
        foreach (var (target, folder) in targets)
        {
            var outputPath = $"Assets/Mods/Battle_Bus/AssetBundles/{folder}";
            Directory.CreateDirectory(outputPath);
            var manifest = BuildPipeline.BuildAssetBundles(outputPath,
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                target);
            var bundlePath = Path.Combine(outputPath, BundleName + ".unity3d");
            var bundleName = BundleName + ".unity3d";
            if (manifest == null || !manifest.GetAllAssetBundles().Contains(bundleName))
                throw new InvalidOperationException($"Battle Bus {folder} asset bundle did not build correctly.");

            var dependencies = manifest.GetDirectDependencies(bundleName);
            if (dependencies.Length != 0)
                throw new InvalidOperationException(
                    $"Battle Bus {folder} bundle has external dependencies: {string.Join(", ", dependencies)}.");

            foreach (var unrelatedBundle in manifest.GetAllAssetBundles().Where(name => name != bundleName))
            {
                DeleteGeneratedBundleFile(Path.Combine(outputPath, unrelatedBundle));
                DeleteGeneratedBundleFile(Path.Combine(outputPath, unrelatedBundle + ".manifest"));
            }

            if (!File.Exists(bundlePath) || new FileInfo(bundlePath).Length < 256)
                throw new InvalidOperationException($"Battle Bus {folder} asset bundle did not build correctly.");
            Debug.Log($"Battle Bus {folder} AssetBundle built: {bundlePath} " +
                      $"({new FileInfo(bundlePath).Length:N0} bytes).");
        }
        VerifyBuiltBundle();
    }

    public static void VerifyBuiltBundle()
    {
        var bundlePath = "Assets/Mods/Battle_Bus/AssetBundles/Windows/battlebus.unity3d";
        var bundle = AssetBundle.LoadFromFile(bundlePath);
        if (bundle == null)
            throw new InvalidOperationException($"Could not load bundle '{bundlePath}'.");
        try
        {
            var vehicleType = bundle.LoadAsset<UnityEngine.Object>(VehicleAssetPath);
            var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
            if (vehicleType == null || prefab == null)
                throw new InvalidOperationException("Battle Bus bundle is missing its VehicleType or prefab.");

            var model = FindTransform(prefab.transform, "BattleBusVisual") ??
                        throw new InvalidOperationException("Battle Bus prefab is missing BattleBusVisual.");
            var vehicle = FindComponentBySerializedProperty(prefab, "renderers") ??
                          throw new InvalidOperationException("Battle Bus VehicleController renderer list is missing.");
            var serializedVehicle = new SerializedObject(vehicle);
            var vehicleRenderers = serializedVehicle.FindProperty("renderers");
            if (vehicleRenderers == null || !vehicleRenderers.isArray || vehicleRenderers.arraySize == 0)
                throw new InvalidOperationException("Battle Bus VehicleController has no visible model renderers.");
            for (var index = 0; index < vehicleRenderers.arraySize; index++)
            {
                var renderer = vehicleRenderers.GetArrayElementAtIndex(index).objectReferenceValue as Renderer;
                var isBusBodyMesh = renderer != null && renderer.transform.IsChildOf(model);
                var isBusTireMesh = renderer != null && IsBusTireRenderer(renderer.transform);
                if (renderer == null || (!isBusBodyMesh && !isBusTireMesh) ||
                    renderer.gameObject.layer != LayerMask.NameToLayer("Vehicles"))
                    throw new InvalidOperationException(
                        $"Battle Bus renderer entry {index} does not point at a Vehicles-layer bus mesh.");

                foreach (var material in renderer.sharedMaterials)
                    if (material == null || material.shader == null ||
                        string.Equals(material.shader.name, "glTF/PbrMetallicRoughness", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Battle Bus renderer '{renderer.name}' still has an unsupported imported material.");
            }

            var balloonRenderer = model.GetComponentsInChildren<MeshRenderer>(true)
                .SingleOrDefault(candidate => candidate.name.EndsWith(
                    "_OriginalColorBalloon", StringComparison.Ordinal));
            var balloonMesh = balloonRenderer != null
                ? balloonRenderer.GetComponent<MeshFilter>()?.sharedMesh
                : null;
            if (balloonRenderer == null || !balloonRenderer.enabled || balloonMesh == null ||
                balloonMesh.triangles.Length < 1500)
                throw new InvalidOperationException(
                    "Battle Bus balloon envelope is missing or has too little visible geometry.");

            var glassRenderer = model.GetComponentsInChildren<Renderer>(true)
                .SingleOrDefault(candidate => string.Equals(candidate.name, "Object_18", StringComparison.Ordinal));
            if (glassRenderer == null || !glassRenderer.enabled ||
                glassRenderer.sharedMaterials.Length == 0 ||
                glassRenderer.sharedMaterials.Any(material => material == null ||
                    !material.HasProperty("_SurfaceType") || material.GetFloat("_SurfaceType") < 0.5f))
                throw new InvalidOperationException(
                    "Battle Bus window renderer is missing or its material is not transparent HDRP/Lit.");

            var navMeshObstacleProperty = serializedVehicle.FindProperty("navMeshObstacle");
            var navMeshObstacle = navMeshObstacleProperty?.objectReferenceValue as NavMeshObstacle;
            if (navMeshObstacle == null || navMeshObstacle.size.x < 2.3f || navMeshObstacle.size.x > 2.7f ||
                navMeshObstacle.size.y > 4f || navMeshObstacle.size.z < 7.5f)
                throw new InvalidOperationException(
                    $"Battle Bus NavMeshObstacle does not match the chassis footprint: " +
                    $"{(navMeshObstacle != null ? navMeshObstacle.size.ToString() : "missing")}.");
            if (navMeshObstacle.transform.parent != prefab.transform ||
                navMeshObstacle.transform.localPosition.sqrMagnitude > 0.0001f ||
                Quaternion.Angle(navMeshObstacle.transform.localRotation, Quaternion.identity) > 0.1f ||
                (navMeshObstacle.transform.localScale - Vector3.one).sqrMagnitude > 0.0001f)
                throw new InvalidOperationException(
                    "Battle Bus NavMeshObstacle must be root-aligned so cached and physical bounds match.");

            var carFeatures = FindComponentBySerializedProperty(prefab, "bodyMeshes") ??
                              throw new InvalidOperationException("Battle Bus CarFeatures bodyMeshes is missing.");
            var serializedFeatures = new SerializedObject(carFeatures);
            var bodyMeshes = serializedFeatures.FindProperty("bodyMeshes");
            if (bodyMeshes == null || !bodyMeshes.isArray || bodyMeshes.arraySize != 6)
                throw new InvalidOperationException(
                    $"Battle Bus CarFeatures must contain hull, balloon cap, two doors and two detail renderers; found " +
                    $"{(bodyMeshes != null && bodyMeshes.isArray ? bodyMeshes.arraySize : 0)}.");
            for (var index = 0; index < bodyMeshes.arraySize; index++)
            {
                var renderer = bodyMeshes.GetArrayElementAtIndex(index).objectReferenceValue as Renderer;
                if (renderer == null || !renderer.transform.IsChildOf(model) ||
                    !(renderer.name == PaintableHullRendererName ||
                      renderer.name == "BattleBusPaintDoor" ||
                      renderer.name == "BattleBusPaintSingleSideDoor" ||
                      renderer.name == "BattleBusPaintBalloonCap" ||
                      renderer.name == "BattleBusPaintAccent_Object_10" ||
                      renderer.name == "BattleBusPaintAccent_Object_12") ||
                    renderer.GetComponent<MeshFilter>()?.sharedMesh == null)
                    throw new InvalidOperationException(
                        $"Battle Bus paint renderer entry {index} is not an isolated body detail.");
                if (!renderer.sharedMaterials.Any(material => material != null &&
                        material.name.StartsWith("BattleBusPaint_", StringComparison.Ordinal) &&
                        (material.HasProperty("_BaseColor") || material.HasProperty("_Color"))))
                    throw new InvalidOperationException(
                        $"Battle Bus paint renderer '{renderer.name}' has no native paint color property.");
            }
            var paintMaterialRenderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null && renderer.sharedMaterials.Any(material => material != null &&
                    material.name.StartsWith("BattleBusPaint_", StringComparison.Ordinal))).ToArray();
            if (paintMaterialRenderers.Length != 6 ||
                paintMaterialRenderers.Any(renderer => !Enumerable.Range(0, bodyMeshes.arraySize)
                    .Any(index => ReferenceEquals(renderer,
                        bodyMeshes.GetArrayElementAtIndex(index).objectReferenceValue))))
                throw new InvalidOperationException(
                    $"Battle Bus paint is assigned to {paintMaterialRenderers.Length} renderers outside the six paint details.");

            var physicsController = prefab.GetComponentsInChildren<Component>(true).FirstOrDefault(component =>
                component != null && component.GetType().FullName == "NWH.VehiclePhysics2.VehicleController");
            if (physicsController is not Behaviour physicsBehaviour || !physicsBehaviour.enabled)
                throw new InvalidOperationException("Battle Bus NWH VehicleController is missing or disabled.");
            var serializedPhysics = new SerializedObject(physicsController);
            var playerControllable = serializedPhysics.FindProperty("isPlayerControllable");
            var powertrainWheels = serializedPhysics.FindProperty("powertrain.wheels");
            if (playerControllable?.boolValue != true || powertrainWheels == null ||
                !powertrainWheels.isArray || powertrainWheels.arraySize != 4)
                throw new InvalidOperationException(
                    "Battle Bus drivetrain is not player-controllable or does not have four wheel controllers.");

            // NWH's controller fields are not surfaced by Unity's SerializedObject API when
            // read back from prefab or AssetBundle-loaded objects. Verify the serialized
            // component document directly; this is the source Unity includes in the bundle.
            var physicsDocument = Regex.Split(File.ReadAllText(VehiclePrefabPath), @"(?m)^--- !u!")
                .SingleOrDefault(document => document.Contains("useDefaultMass:") &&
                                             document.Contains("baseMass:") &&
                                             document.Contains("inertiaTensor:"));
            var massMatch = physicsDocument != null
                ? Regex.Match(physicsDocument, @"(?m)^\s*baseMass:\s*([-+\d.eE]+)\s*$")
                : Match.Empty;
            var inertiaMatch = physicsDocument != null
                ? Regex.Match(physicsDocument, @"(?m)^\s*inertiaTensor:\s*\{x:\s*([-+\d.eE]+)")
                : Match.Empty;
            if (physicsDocument == null ||
                !Regex.IsMatch(physicsDocument, @"(?m)^\s*useDefaultMass:\s*0\s*$") ||
                !Regex.IsMatch(physicsDocument, @"(?m)^\s*useDefaultInertia:\s*0\s*$") ||
                !massMatch.Success ||
                !float.TryParse(massMatch.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var configuredMass) ||
                Mathf.Abs(configuredMass - TargetVehicleMass) > 0.1f ||
                !inertiaMatch.Success ||
                !float.TryParse(inertiaMatch.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var inertiaX) || inertiaX <= 1000f)
                throw new InvalidOperationException(
                    "Battle Bus runtime mass or inertia still contains the small Audi donor settings: " +
                    $"source prefab document found={physicsDocument != null}, baseMass=" +
                    $"{(massMatch.Success ? massMatch.Groups[1].Value : "missing")}, " +
                    $"inertiaX={(inertiaMatch.Success ? inertiaMatch.Groups[1].Value : "missing")}.");
            for (var index = 0; index < powertrainWheels.arraySize; index++)
                if (powertrainWheels.GetArrayElementAtIndex(index).FindPropertyRelative("wheelUAPI")?.objectReferenceValue == null)
                    throw new InvalidOperationException($"Battle Bus drivetrain wheel {index} is not connected.");

            var controllers = new[]
            {
                "FrontLeft_WheelController", "FrontRight_WheelController",
                "RearLeft_WheelController", "RearRight_WheelController",
            };
            var bodyBox = FindTransform(prefab.transform, "BodyCollider")?.GetComponent<BoxCollider>();
            if (bodyBox == null || !bodyBox.enabled ||
                bodyBox.center.y - bodyBox.size.y * 0.5f < 0.11f)
                throw new InvalidOperationException("Battle Bus body collider must clear the road beneath the tires.");
            foreach (var name in controllers)
            {
                var controller = FindTransform(prefab.transform, name);
                if (controller == null)
                    throw new InvalidOperationException($"Battle Bus prefab is missing '{name}'.");

                var wheelComponent = controller.GetComponents<MonoBehaviour>().FirstOrDefault(component =>
                {
                    if (component == null)
                        return false;
                    var serialized = new SerializedObject(component);
                    return serialized.FindProperty("wheel")?.FindPropertyRelative("visual") != null;
                });
                if (wheelComponent == null)
                    throw new InvalidOperationException($"Battle Bus wheel visual binding is missing on '{name}'.");

                var serializedWheel = new SerializedObject(wheelComponent);
                var wheel = serializedWheel.FindProperty("wheel");
                var angularVelocity = wheel?.FindPropertyRelative("angularVelocity");
                if (angularVelocity?.propertyType != SerializedPropertyType.Float)
                    throw new InvalidOperationException(
                        $"Battle Bus wheel '{name}' has no serialized angular-velocity state for flight spin control.");
                var visualObject = wheel?.FindPropertyRelative("visual")?.objectReferenceValue as GameObject;
                var visualTransform = wheel?.FindPropertyRelative("visualTransform")?.objectReferenceValue as Transform;
                if (visualObject == null || visualTransform == null ||
                    visualTransform.gameObject != visualObject || !visualTransform.IsChildOf(controller))
                    throw new InvalidOperationException(
                        $"Battle Bus wheel '{name}' does not bind both visual references to its mounted visual root.");
                if (visualTransform.localPosition.sqrMagnitude > 0.0001f ||
                    Quaternion.Angle(visualTransform.localRotation, Quaternion.identity) > 0.1f ||
                    (visualTransform.localScale - Vector3.one).sqrMagnitude > 0.0001f)
                    throw new InvalidOperationException(
                        $"Battle Bus wheel '{name}' visual mount is offset from the wheel axle.");

                var tireRenderers = visualObject.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(renderer => renderer.GetComponent<MeshFilter>()?.sharedMesh != null).ToArray();
                if (tireRenderers.Length == 0)
                    throw new InvalidOperationException($"Battle Bus wheel '{name}' has no mesh-rendered tire visuals.");
                Debug.Log($"Battle Bus wheel visual verified: controller='{name}', " +
                          $"renderers={tireRenderers.Length}, visual='{visualObject.name}'.");
            }

            var tireVisuals = 0;
            var hasFlight = false;
            var hasBalloonCollider = false;
            var hasServiceBounds = false;
            var serviceColliderIsTrigger = false;
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.StartsWith("BattleBusTire_", StringComparison.Ordinal) &&
                    transform.GetComponent<MeshRenderer>() != null)
                    tireVisuals++;
                if (transform.GetComponent<BattleBusFlightController>() != null)
                    hasFlight = true;
                if (string.Equals(transform.name, "BattleBusBalloonCollision", StringComparison.Ordinal) &&
                    transform.GetComponent<Collider>() != null)
                    hasBalloonCollider = true;
                if (string.Equals(transform.name, "GasStationServiceBounds", StringComparison.Ordinal))
                {
                    hasServiceBounds = true;
                    var collider = transform.GetComponent<BoxCollider>();
                    serviceColliderIsTrigger = collider != null && collider.isTrigger;
                }
            }

            if (tireVisuals != 6 || !hasFlight || hasBalloonCollider || !hasServiceBounds ||
                !serviceColliderIsTrigger)
                throw new InvalidOperationException(
                    $"Battle Bus bundle verification failed: tireVisuals={tireVisuals}, " +
                    $"flight={hasFlight}, balloonCollider={hasBalloonCollider}, " +
                    $"serviceBounds={hasServiceBounds}, serviceTrigger={serviceColliderIsTrigger}.");

            var serializedType = new SerializedObject(vehicleType);
            var registeredName = serializedType.FindProperty("vehicleTypeName");
            if (registeredName?.stringValue != VehicleTypeName)
                throw new InvalidOperationException("Battle Bus VehicleType registration name did not survive bundling.");
            Debug.Log("Battle Bus bundle verified: VehicleType registration, custom renderers and paint meshes, " +
                      "bus-size NavMeshObstacle, four wheel controllers, six tire visuals, flight runtime, " +
                      "balloon without collision and service trigger.");

            var flight = prefab.GetComponent<BattleBusFlightController>();
            var serializedFlight = flight != null ? new SerializedObject(flight) : null;
            var flameMotion = serializedFlight?.FindProperty("flameMotion")?.objectReferenceValue as Transform;
            var flameRenderer = flameMotion != null ? flameMotion.GetComponent<ParticleSystemRenderer>() : null;
            if (flameRenderer == null || !flameRenderer.enabled ||
                flameMotion!.GetComponent<ParticleSystem>() == null ||
                !string.Equals(flameMotion!.name, "BattleBusBalloonFlame", StringComparison.Ordinal))
                throw new InvalidOperationException("Battle Bus flight visual has no particle flame.");
            var flameMaterial = flameRenderer.sharedMaterial;
            if (flameMaterial == null || flameMaterial.shader == null ||
                flameMaterial.shader.name != "HDRP/Unlit" ||
                flameMaterial.GetTexture("_UnlitColorMap") == null)
                throw new InvalidOperationException("Battle Bus flame is missing its alpha-masked particle shader or texture.");
            if (Quaternion.Angle(flameMotion.localRotation,
                    Quaternion.Euler(-90f, 0f, 0f)) > 0.1f)
                throw new InvalidOperationException("Battle Bus particle flame is not aimed up into the balloon.");
            var rearArmPivots = serializedFlight?.FindProperty("rearArmPivots");
            if (rearArmPivots == null || !rearArmPivots.isArray || rearArmPivots.arraySize != 2 ||
                Enumerable.Range(0, 2).Any(index =>
                    rearArmPivots.GetArrayElementAtIndex(index).objectReferenceValue is not Transform))
                throw new InvalidOperationException("Battle Bus rear arm pivots were not serialized.");
            var visualDamage = prefab.GetComponent<BattleBusVisualDamageController>();
            var serializedDamage = visualDamage != null ? new SerializedObject(visualDamage) : null;
            var damageMeshes = serializedDamage?.FindProperty("bodyMeshes");
            if (damageMeshes == null || !damageMeshes.isArray || damageMeshes.arraySize != 17)
                throw new InvalidOperationException("Battle Bus impact deformation does not reference seventeen exterior meshes.");
            var sourceFlame = FindTransform(model, SourceStaticFlameRendererName);
            if (sourceFlame?.GetComponent<MeshFilter>()?.sharedMesh != null ||
                sourceFlame?.GetComponent<Renderer>()?.enabled == true)
                throw new InvalidOperationException("Battle Bus bundle contains both animated and static flame geometry.");
            var serializedSupportPoints = serializedFlight?.FindProperty("supportPoints");
            var serializedSupportRadii = serializedFlight?.FindProperty("supportWheelRadii");
            if (serializedSupportPoints == null || !serializedSupportPoints.isArray ||
                serializedSupportPoints.arraySize != 4 || serializedSupportRadii == null ||
                !serializedSupportRadii.isArray || serializedSupportRadii.arraySize != 4)
                throw new InvalidOperationException(
                    "Battle Bus flight clearance must use four wheel-center probes and radii.");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static UnityEngine.Object CreateVehicleType()
    {
        var source = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AudiAssetPath);
        if (source == null)
            throw new InvalidOperationException("Audi RS6R VehicleType reference asset was not found.");

        var target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        if (target == null)
        {
            if (!AssetDatabase.CopyAsset(AudiAssetPath, VehicleAssetPath))
                throw new InvalidOperationException("Could not create Battle Bus VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
            EditorUtility.CopySerialized(source, target);

        if (target == null)
            throw new InvalidOperationException("Generated Battle Bus VehicleType did not load.");
        target.name = "BattleBus";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        // TowVehicle routes tagged heavy vehicles to truck repair bays.
        var tags = serialized.FindProperty("tags");
        tags.arraySize = 1;
        tags.GetArrayElementAtIndex(0).stringValue = "ba:vehicletag_istruck";
        SetNumber(serialized, "price", 125000f);
        SetNumber(serialized, "maxFuel", 120f);
        SetNumber(serialized, "maxCargoCapacity", 28f);
        SetNumber(serialized, "maxSpeed", 130f);
        SetNumber(serialized, "enginePower", 620f);
        SetNumber(serialized, "brakeForce", 8400f);
        SetNumber(serialized, "turnRadius", 6f);
        SetNumber(serialized, "damageIntensity", 0.42f);
        SetBool(serialized, "fitsHandTruck", true);
        SetBool(serialized, "fitsFlatbed", false);
        SetBool(serialized, "autoParkSupported", false);
        SetBool(serialized, "hasRadio", true);
        SetBool(serialized, "enclosed", true);
        SetBool(serialized, "isLuxuryCar", false);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        if (serialized.FindProperty("vehicleTypeName")?.stringValue != VehicleTypeName)
            throw new InvalidOperationException("VehicleTypeName could not be set on the generated vehicle asset.");
        EditorUtility.SetDirty(target);
        return target;
    }

    private static void CreateVehiclePrefab(UnityEngine.Object vehicleType)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(AudiPrefabPath);
        var importedModel = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (source == null || importedModel == null)
            throw new InvalidOperationException("Audi donor prefab or imported Battle Bus model is missing.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "BattleBus";
        try
        {
            RemoveDonorEngineAudioDependency(root);
            StripDonorGeometry(root);
            var model = PrefabUtility.InstantiatePrefab(importedModel, root.transform) as GameObject;
            if (model == null)
                throw new InvalidOperationException("Could not instantiate the imported Battle Bus model.");
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            model.name = "BattleBusVisual";

            NormalizeModel(model, root.transform);
            var allModelRenderers = model.GetComponentsInChildren<Renderer>(true);
            var visibleModelRenderers = allModelRenderers.Where(renderer =>
                renderer != null && renderer.enabled && renderer.sharedMaterials.Length > 0).ToArray();
            if (visibleModelRenderers.Length == 0)
                throw new InvalidOperationException("Imported Battle Bus model has no visible renderers.");
            LogModelMaterialAudit(visibleModelRenderers);

            // Correct the complete source model before extracting wheels so all
            // wheel centers and mesh vertices use the same final vehicle axes.
            CorrectBodyOrientation(model, root.transform, visibleModelRenderers);
            NormalizeImportedMaterials(visibleModelRenderers);
            ExtractBlenderSelections(model.transform, root);
            var sourceFlameAnchor = RemoveSourceStaticFlame(model.transform, root.transform);
            RemoveSourceFlameOverlay(model.transform, root.transform);
            var candidates = DiscoverWheelComponents(model.transform, root.transform,
                includeThinDetachedTread: true);
            var wheelComponents = SelectWheelComponents(candidates);
            AddFrontRightTread(wheelComponents, root.transform);
            var smallWheelParts = DiscoverWheelComponents(model.transform, root.transform,
                includeThinDetachedTread: true, includeSmallDetails: true);
            AddLooseTireTread(wheelComponents, smallWheelParts);
            var wheelGroups = CreateWheelGroups(wheelComponents);

            SplitWheelComponents(wheelComponents, wheelGroups);
            RemoveFrontProtrudingHubs(model.transform, root.transform, wheelGroups);
            RemoveRearLooseArcs(model.transform, root.transform, wheelGroups);
            // The authored model depicts the bus hanging below its balloon. Lower
            // the hull relative to the grounded wheel visuals, with a little more
            // drop at the front. Lower the hull a further 9 cm relative to the
            // grounded tires; both axles still had excessive arch clearance.
            var bodyPitchPivot = root.transform.InverseTransformPoint(model.transform.position);
            var bodyPitch = Quaternion.AngleAxis(0.65f, Vector3.right);
            sourceFlameAnchor = bodyPitchPivot + bodyPitch * (sourceFlameAnchor - bodyPitchPivot) +
                                Vector3.down * 0.22f;
            model.transform.rotation = Quaternion.AngleAxis(0.65f, root.transform.right) * model.transform.rotation;
            model.transform.position -= root.transform.up * 0.22f;
            var balloonRenderer = SplitBalloonGeometry(model.transform, root.transform);
            CompleteFrontEntryDoor(model.transform, root.transform);
            CorrectEntryGlass(model.transform, root.transform);
            var rearArms = ExtractRearArmLowerSections(model.transform, root.transform);
            ExtractSingleSideDoor(model.transform, root.transform);
            SplitPaintedBodyDetails(model.transform, root.transform);
            SeparateBalloonRig(model.transform,root.transform);
            var finalModelRenderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null && renderer.enabled && renderer.sharedMaterials.Length > 0)
                .ToArray();
            var bodyRenderers = finalModelRenderers
                .Where(renderer => renderer != balloonRenderer && HasRenderableMesh(renderer)).ToArray();
            if (bodyRenderers.Length == 0)
                throw new InvalidOperationException("Battle Bus model has no renderable meshes after extracting the tires.");
            var paintRenderers = ConfigurePaintMaterials(bodyRenderers);
            ConfigureVehicleDeformation(root, bodyRenderers);
            var vehicleRenderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(HasRenderableMesh).ToArray();
            var allBounds = GetRendererBounds(root.transform, vehicleRenderers);
            var chassisBounds = GetChassisBounds(root.transform, bodyRenderers, wheelGroups, allBounds);
            ConfigureModelLayer(model, vehicleRenderers);
            ConfigurePhysics(root, wheelGroups, chassisBounds, allBounds);
            ConfigureWheelControllers(root, wheelGroups);
            ConfigureVehicleSteering(root);
            ConfigureBodyCollider(root, chassisBounds);
        var damageHandler = FindComponentBySerializedProperty(root, "decelerationThreshold") ??
            throw new InvalidOperationException("Battle Bus donor damage handler is missing.");
        var damageSettings = new SerializedObject(damageHandler);
        SetNumber(damageSettings, "damageIntensity", 0.68f);
        SetNumber(damageSettings, "decelerationThreshold", 350f);
        damageSettings.ApplyModifiedPropertiesWithoutUndo();
            var serviceCollider = ConfigureServiceBounds(root, chassisBounds);
            ConfigureEntryAnchors(root, wheelGroups, chassisBounds);
            ConfigureVehicleReferences(root, vehicleType, serviceCollider, vehicleRenderers,
                paintRenderers, chassisBounds);
            var flameMotion = CreateFallbackFlame(root, chassisBounds, sourceFlameAnchor);
            AttachFlightController(root, wheelGroups, flameMotion, rearArms);
            if (root.GetComponent<BattleBusPaintController>() == null)
                root.AddComponent<BattleBusPaintController>();
            DisableLights(root);
            ConfigureAddedVisuals(root, model.transform);
            ConfigureBusAudio(root);
            root.AddComponent<BattleBusWorkshopRecovery>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("Could not save Battle Bus prefab.");
            SetBundle(VehiclePrefabPath);
            Debug.Log($"Battle Bus prefab generated: visualSize={allBounds.size}, " +
                      $"chassisSize={chassisBounds.size}, wheelRadius={wheelGroups.AverageRadius:F2}, " +
                      $"rearWheelComponents={wheelGroups.RearLeft.Count + wheelGroups.RearRight.Count}.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void RemoveDonorEngineAudioDependency(GameObject root)
    {
        var controller = root.GetComponentsInChildren<Component>(true).FirstOrDefault(component =>
            component != null && component.GetType().FullName == "NWH.VehiclePhysics2.VehicleController");
        if (controller == null)
            throw new InvalidOperationException("Audi donor VehicleController was not found.");

        var serialized = new SerializedObject(controller);
        var clips = serialized.FindProperty("soundManager.engineRunningComponent.clips");
        if (clips == null || !clips.isArray)
            throw new InvalidOperationException("Audi donor engine-running audio clips could not be located.");

        var removedClipCount = clips.arraySize;
        clips.arraySize = 0;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"Battle Bus donor audio detached: removed {removedClipCount} Audi engine clip reference(s).");
    }

    private static void DeleteGeneratedBundleFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
        var metaPath = path + ".meta";
        if (File.Exists(metaPath))
            File.Delete(metaPath);
    }

    private static void StripDonorGeometry(GameObject root)
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

    private static void NormalizeModel(GameObject model, Transform vehicleRoot)
    {
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
        if (!TryGetRendererBounds(vehicleRoot, model.GetComponentsInChildren<Renderer>(true), out var bounds))
            throw new InvalidOperationException("Battle Bus model bounds could not be read.");

        if (bounds.size.x > bounds.size.z)
        {
            model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            TryGetRendererBounds(vehicleRoot, model.GetComponentsInChildren<Renderer>(true), out bounds);
        }
        if (bounds.size.z <= 0.01f)
            throw new InvalidOperationException($"Battle Bus model has invalid bounds {bounds.size}.");

        var scale = TargetLength / bounds.size.z;
        model.transform.localScale = Vector3.one * scale;
        TryGetRendererBounds(vehicleRoot, model.GetComponentsInChildren<Renderer>(true), out bounds);
        model.transform.localPosition += new Vector3(-bounds.center.x, -bounds.min.y + 0.04f,
            -bounds.center.z);
    }

    private static void CorrectBodyOrientation(GameObject model, Transform vehicleRoot, Renderer[] modelRenderers)
    {
        if (!TryGetRendererBounds(vehicleRoot, modelRenderers, out var bounds))
            throw new InvalidOperationException("Battle Bus model bounds could not be read before orientation correction.");

        // The imported bus points nose-down, with the balloon axis running forward.
        // Rotate the intact source hierarchy before wheel extraction, so detached
        // wheels inherit the corrected orientation and remain aligned with the body.
        var pivot = bounds.center;
        var pivotInModel = model.transform.InverseTransformPoint(pivot);
        var correctedRotation = model.transform.localRotation * Quaternion.Euler(-90f, 0f, 0f);
        model.transform.localRotation = correctedRotation;
        if (!TryGetRendererBounds(vehicleRoot, modelRenderers, out bounds) || bounds.size.z <= 0.01f)
            throw new InvalidOperationException("Battle Bus model bounds are invalid after orientation correction.");

        var correctedScale = model.transform.localScale * (TargetLength / bounds.size.z);
        model.transform.localScale = correctedScale;
        model.transform.localPosition = pivot - correctedRotation * Vector3.Scale(pivotInModel, correctedScale);
        if (!TryGetRendererBounds(vehicleRoot, modelRenderers, out bounds))
            throw new InvalidOperationException("Battle Bus model bounds could not be read after orientation correction.");

        model.transform.localPosition += Vector3.up * (0.04f - bounds.min.y);
        if (!TryGetRendererBounds(vehicleRoot, modelRenderers, out bounds))
            throw new InvalidOperationException("Battle Bus model bounds could not be read after ground alignment.");
        Debug.Log($"Battle Bus model orientation corrected by -90 degrees on X before wheel extraction: " +
                  $"size={bounds.size}, center={bounds.center}.");
    }

    private static List<MeshComponent> DiscoverWheelComponents(Transform modelRoot, Transform vehicleRoot,
        bool includeThinDetachedTread, bool includeSmallDetails = false,
        float maximumCenterHeight = 1.25f, float maximumComponentSize = 1.25f)
    {
        var result = new List<MeshComponent>();
        foreach (var filter in modelRoot.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            var renderer = filter.GetComponent<Renderer>();
            if (mesh == null || renderer == null || mesh.vertexCount == 0)
                continue;

            var sourceVertices = mesh.vertices;
            var rootVertices = new Vector3[sourceVertices.Length];
            for (var index = 0; index < sourceVertices.Length; index++)
                rootVertices[index] = vehicleRoot.InverseTransformPoint(
                    filter.transform.TransformPoint(sourceVertices[index]));

            var union = new UnionFind(sourceVertices.Length);
            WeldDuplicateVertices(rootVertices, union);
            var trianglesPerSubmesh = new int[mesh.subMeshCount][];
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var triangles = mesh.GetTriangles(submesh);
                trianglesPerSubmesh[submesh] = triangles;
                for (var index = 0; index + 2 < triangles.Length; index += 3)
                {
                    union.Join(triangles[index], triangles[index + 1]);
                    union.Join(triangles[index + 1], triangles[index + 2]);
                }
            }

            var accumulators = new Dictionary<int, ComponentAccumulator>();
            for (var submesh = 0; submesh < trianglesPerSubmesh.Length; submesh++)
            {
                var triangles = trianglesPerSubmesh[submesh];
                for (var index = 0; index + 2 < triangles.Length; index += 3)
                {
                    var vertex = triangles[index];
                    var rootIndex = union.Find(vertex);
                    if (!accumulators.TryGetValue(rootIndex, out var accumulator))
                    {
                        accumulator = new ComponentAccumulator(mesh.subMeshCount);
                        accumulators.Add(rootIndex, accumulator);
                    }
                    accumulator.Triangles[submesh].Add(vertex);
                    accumulator.Triangles[submesh].Add(triangles[index + 1]);
                    accumulator.Triangles[submesh].Add(triangles[index + 2]);
                    accumulator.Vertices.Add(vertex);
                    accumulator.Vertices.Add(triangles[index + 1]);
                    accumulator.Vertices.Add(triangles[index + 2]);
                }
            }

            foreach (var accumulator in accumulators.Values)
            {
                if (accumulator.FaceCount < (includeSmallDetails ? 8 : 40))
                    continue;
                var first = true;
                var bounds = new Bounds();
                foreach (var vertex in accumulator.Vertices)
                {
                    var point = rootVertices[vertex];
                    if (first)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        first = false;
                    }
                    else
                        bounds.Encapsulate(point);
                }
                var size = bounds.size;
                var biggest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                var smallest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
                var radial = Mathf.Max(size.y, size.z);
                if (biggest < (includeSmallDetails ? 0.025f : 0.20f) || biggest > maximumComponentSize ||
                    (!includeSmallDetails && radial < 0.28f) ||
                    (!includeThinDetachedTread && (smallest < 0.12f || smallest / biggest < 0.18f)) ||
                    bounds.center.y < -0.15f || bounds.center.y > maximumCenterHeight)
                    continue;

                result.Add(new MeshComponent(filter, renderer, accumulator.Triangles,
                    accumulator.Vertices, bounds, accumulator.FaceCount));
            }
        }
        return result;
    }

    private static void WeldDuplicateVertices(Vector3[] positions, UnionFind union)
    {
        var byPosition = new Dictionary<QuantizedPosition, int>();
        var inverseTolerance = 1f / WheelWeldTolerance;
        for (var index = 0; index < positions.Length; index++)
        {
            var position = positions[index];
            var key = new QuantizedPosition(
                Mathf.RoundToInt(position.x * inverseTolerance),
                Mathf.RoundToInt(position.y * inverseTolerance),
                Mathf.RoundToInt(position.z * inverseTolerance));
            if (byPosition.TryGetValue(key, out var existing))
                union.Join(index, existing);
            else
                byPosition.Add(key, index);
        }
    }

    private static List<MeshComponent> SelectWheelComponents(List<MeshComponent> candidates)
    {
        var assemblyCandidates = candidates.Where(component =>
        {
            var size = component.Bounds.size;
            var biggest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            var smallest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            // The outer rear rims are only 11 cm deep and belong with the tires.
            return smallest >= 0.10f && smallest / biggest >= 0.18f;
        }).ToList();
        var tireTemplates = candidates.Where(component =>
                component.FaceCount >= 500 && component.Bounds.size.x > 0.18f &&
                component.Bounds.size.x < 0.65f && component.Bounds.size.y > 0.55f &&
                component.Bounds.size.z > 0.55f &&
                Mathf.Max(component.Bounds.size.y, component.Bounds.size.z) /
                Mathf.Min(component.Bounds.size.y, component.Bounds.size.z) < 1.20f &&
                component.Bounds.center.y < 1.5f)
            .OrderByDescending(component => component.FaceCount).ToList();
        var tireCenters = new List<MeshComponent>();
        foreach (var candidate in tireTemplates)
        {
            if (tireCenters.Any(existing =>
                    Mathf.Abs(existing.Bounds.center.x - candidate.Bounds.center.x) < WheelSeedCenterTolerance &&
                    Mathf.Abs(existing.Bounds.center.y - candidate.Bounds.center.y) < WheelSeedCenterTolerance &&
                    Mathf.Abs(existing.Bounds.center.z - candidate.Bounds.center.z) < WheelSeedCenterTolerance))
                continue;
            tireCenters.Add(candidate);
        }

        var candidatesSummary = string.Join("; ", tireCenters.Select(tire =>
            $"{tire.Filter.name}@{tire.Bounds.center} size={tire.Bounds.size} faces={tire.FaceCount}"));
        if (tireCenters.Count != 6)
            throw new InvalidOperationException(
                $"Expected the Battle Bus source's six distinct tires (two front and four rear), " +
                $"found {tireCenters.Count}. Candidates: {candidatesSummary}");

        foreach (var tire in tireCenters)
            Debug.Log($"Battle Bus source tire: mesh='{tire.Filter.name}', " +
                      $"material='{tire.Renderer.sharedMaterial?.name ?? "missing"}', " +
                      $"center={tire.Bounds.center}, size={tire.Bounds.size}, faces={tire.FaceCount}.");

        var axleRows = new List<List<MeshComponent>>();
        foreach (var tire in tireCenters.OrderBy(component => component.Bounds.center.z))
        {
            var row = axleRows.FirstOrDefault(existing =>
                Mathf.Abs(existing[0].Bounds.center.z - tire.Bounds.center.z) < WheelRowTolerance);
            if (row == null)
            {
                row = new List<MeshComponent>();
                axleRows.Add(row);
            }
            row.Add(tire);
        }

        var frontRow = axleRows.SingleOrDefault(row => row.Count == 2);
        var rearRow = axleRows.SingleOrDefault(row => row.Count == 4);
        if (axleRows.Count != 2 || frontRow == null || rearRow == null)
            throw new InvalidOperationException(
                $"Battle Bus source tires did not form one front pair and one rear dual axle: " +
                $"rows={string.Join(", ", axleRows.Select(row => row.Count))}; candidates: {candidatesSummary}");

        var centerX = tireCenters.Average(tire => tire.Bounds.center.x);
        var frontLeft = frontRow.SingleOrDefault(tire => tire.Bounds.center.x < centerX);
        var frontRight = frontRow.SingleOrDefault(tire => tire.Bounds.center.x >= centerX);
        var rearLeft = rearRow.Where(tire => tire.Bounds.center.x < centerX)
            .OrderBy(tire => tire.Bounds.center.x).ToList();
        var rearRight = rearRow.Where(tire => tire.Bounds.center.x >= centerX)
            .OrderBy(tire => tire.Bounds.center.x).ToList();
        if (frontLeft == null || frontRight == null || rearLeft.Count != 2 || rearRight.Count != 2)
            throw new InvalidOperationException(
                $"Battle Bus tires did not split symmetrically by side: " +
                $"front={frontRow.Count}, rear left/right={rearLeft.Count}/{rearRight.Count}; " +
                $"candidates: {candidatesSummary}");

        var frontAxleZ = frontRow.Average(tire => tire.Bounds.center.z);
        var rearAxleZ = rearRow.Average(tire => tire.Bounds.center.z);
        var frontDirection = Mathf.Sign(frontAxleZ - rearAxleZ);
        if (Mathf.Approximately(frontDirection, 0f))
            throw new InvalidOperationException("Battle Bus front and rear source axles overlap.");

        var frontLeftCenter = frontLeft.Bounds.center;
        var frontRightCenter = frontRight.Bounds.center;
        var sourceTireRadius = frontRow.Average(tire => tire.Bounds.size.y * 0.5f);
        var sourceGroundEstimate = frontRow.Average(tire => tire.Bounds.center.y) - sourceTireRadius - 0.12f;
        var wheelY = Mathf.Clamp(sourceGroundEstimate, sourceTireRadius + 0.02f, sourceTireRadius + 0.06f) - 0.03f;
        var frontLeftAdjustment = new Vector3(-0.03f, 0f, 0.015f);
        var frontRightAdjustment = new Vector3(-0.10f, 0f, 0.015f);
        var result = new List<MeshComponent>();
        var frontLeftCorrection = GetGroundWheelCorrection(frontLeft);
        var frontRightCorrection = GetGroundWheelCorrection(frontRight);
        AddWheelAssembly(result, FindWheelAssemblyParts(assemblyCandidates, frontLeft, tireCenters),
            frontLeftCenter, new Vector3(frontLeftCenter.x, wheelY, frontLeftCenter.z) + frontLeftAdjustment,
            frontLeftCorrection);
        AddWheelAssembly(result, FindWheelAssemblyParts(assemblyCandidates, frontRight, tireCenters),
            frontRightCenter, new Vector3(frontRightCenter.x, wheelY, frontRightCenter.z) + frontRightAdjustment,
            frontRightCorrection);
        foreach (var tire in rearLeft.Concat(rearRight))
        {
            var center = tire.Bounds.center;
            AddWheelAssembly(result, FindWheelAssemblyParts(assemblyCandidates, tire, tireCenters),
                center, new Vector3(center.x, wheelY, center.z), GetGroundWheelCorrection(tire));
        }

        // The narrow Object_12 arcs sit well above the tire crowns in the source.
        // They are suspension decoration, not tread, and orbit visibly if mounted
        // on a wheel. The 56 individual Object_10 lugs per tire are extracted below.

        Debug.Log($"Battle Bus source axles: front sourceZ={frontAxleZ:F2}, " +
                  $"front finalZ={frontAxleZ + frontLeftAdjustment.z:F2}, rear sourceZ={rearAxleZ:F2}, " +
                  $"front wheels={frontLeftCenter.x + frontLeftAdjustment.x:F2}/" +
                  $"{frontRightCenter.x + frontRightAdjustment.x:F2}, " +
                  $"rear left duals={rearLeft[0].Bounds.center.x:F2}/{rearLeft[1].Bounds.center.x:F2}, " +
                  $"rear right duals={rearRight[0].Bounds.center.x:F2}/{rearRight[1].Bounds.center.x:F2}, " +
                  $"selected parts={result.Count}, wheelY={wheelY:F2}.");
        return result;
    }

    private static Quaternion GetGroundWheelCorrection(MeshComponent tire)
    {
        var source = tire.SourceMesh ??
                     throw new InvalidOperationException("Could not measure the Battle Bus tire axle.");
        var sourceVertices = source.vertices;
        var points = new List<Vector3>(tire.Vertices.Count);
        foreach (var index in tire.Vertices)
            if (index >= 0 && index < sourceVertices.Length)
                points.Add(tire.VehicleRoot.InverseTransformPoint(
                    tire.Filter.transform.TransformPoint(sourceVertices[index])));
        if (points.Count < 12)
            throw new InvalidOperationException(
                $"Battle Bus tire '{tire.Filter.name}' has too few vertices to measure its axle direction.");

        var mean = points.Aggregate(Vector3.zero, (sum, point) => sum + point) / points.Count;
        var covariance = new float[3, 3];
        foreach (var point in points)
        {
            var delta = point - mean;
            covariance[0, 0] += delta.x * delta.x;
            covariance[0, 1] += delta.x * delta.y;
            covariance[0, 2] += delta.x * delta.z;
            covariance[1, 1] += delta.y * delta.y;
            covariance[1, 2] += delta.y * delta.z;
            covariance[2, 2] += delta.z * delta.z;
        }
        covariance[1, 0] = covariance[0, 1];
        covariance[2, 0] = covariance[0, 2];
        covariance[2, 1] = covariance[1, 2];

        var eigenvectors = new float[3, 3];
        eigenvectors[0, 0] = eigenvectors[1, 1] = eigenvectors[2, 2] = 1f;
        for (var iteration = 0; iteration < 24; iteration++)
        {
            var p = 0;
            var q = 1;
            var largest = Mathf.Abs(covariance[p, q]);
            if (Mathf.Abs(covariance[0, 2]) > largest)
            {
                p = 0;
                q = 2;
                largest = Mathf.Abs(covariance[p, q]);
            }
            if (Mathf.Abs(covariance[1, 2]) > largest)
            {
                p = 1;
                q = 2;
                largest = Mathf.Abs(covariance[p, q]);
            }
            if (largest < 0.000001f)
                break;

            var angle = 0.5f * Mathf.Atan2(2f * covariance[p, q],
                covariance[q, q] - covariance[p, p]);
            var cosine = Mathf.Cos(angle);
            var sine = Mathf.Sin(angle);
            var app = covariance[p, p];
            var aqq = covariance[q, q];
            var apq = covariance[p, q];
            covariance[p, p] = cosine * cosine * app - 2f * sine * cosine * apq +
                               sine * sine * aqq;
            covariance[q, q] = sine * sine * app + 2f * sine * cosine * apq +
                               cosine * cosine * aqq;
            covariance[p, q] = covariance[q, p] = 0f;
            for (var axis = 0; axis < 3; axis++)
            {
                if (axis != p && axis != q)
                {
                    var oldP = covariance[axis, p];
                    var oldQ = covariance[axis, q];
                    covariance[axis, p] = covariance[p, axis] = cosine * oldP - sine * oldQ;
                    covariance[axis, q] = covariance[q, axis] = sine * oldP + cosine * oldQ;
                }

                var vectorP = eigenvectors[axis, p];
                var vectorQ = eigenvectors[axis, q];
                eigenvectors[axis, p] = cosine * vectorP - sine * vectorQ;
                eigenvectors[axis, q] = sine * vectorP + cosine * vectorQ;
            }
        }

        var smallestEigenvalue = covariance[0, 0];
        var smallestAxis = 0;
        for (var axis = 1; axis < 3; axis++)
            if (covariance[axis, axis] < smallestEigenvalue)
            {
                smallestEigenvalue = covariance[axis, axis];
                smallestAxis = axis;
            }

        var axle = new Vector3(eigenvectors[0, smallestAxis],
            eigenvectors[1, smallestAxis], eigenvectors[2, smallestAxis]).normalized;
        if (Vector3.Dot(axle, Vector3.right) < 0f)
            axle = -axle;
        var correctionDegrees = Vector3.Angle(axle, Vector3.right);
        if (correctionDegrees > 25f)
            throw new InvalidOperationException(
                $"Battle Bus tire '{tire.Filter.name}' axle is {correctionDegrees:F1} degrees from the vehicle's " +
                $"ground-aligned axle; refusing to rotate a mismatched tire mesh.");

        var correction = Quaternion.FromToRotation(axle, Vector3.right);
        Debug.Log($"Battle Bus wheel ground alignment measured: mesh='{tire.Filter.name}', " +
                  $"sourceAxle={axle}, correction={correctionDegrees:F2} degrees.");
        return correction;
    }

    private static void AddFrontRightTread(List<MeshComponent> wheels, Transform vehicleRoot)
    {
        var tire = wheels.Where(component => component.FaceCount > 2000 &&
                                           component.WheelAnchor.z > 2.8f)
            .OrderByDescending(component => component.WheelAnchor.x).First();
        var sourceAxle = tire.WheelAnchor - tire.PositionOffset;
        var filter = wheels.Select(component => component.Filter)
            .First(candidate => candidate.name == "Object_12");
        var renderer = filter.GetComponent<MeshRenderer>();
        var mesh = filter.sharedMesh!;
        var atlas = GetMaterialBaseColorTexture(renderer.sharedMaterial) as Texture2D;
        if (atlas == null || !TryReadEmbeddedTexturePixels(atlas,
                out var pixels, out var width, out var height))
            throw new InvalidOperationException("Could not inspect the front right tire profile atlas.");

        var excluded = new HashSet<(int, int, int)>();
        foreach (var component in wheels.Where(component => component.Filter == filter))
            for (var submesh = 0; submesh < component.Triangles.Length; submesh++)
            {
                var triangles = component.Triangles[submesh];
                for (var index = 0; index + 2 < triangles.Count; index += 3)
                    excluded.Add((triangles[index], triangles[index + 1], triangles[index + 2]));
            }

        var sourceVertices = mesh.vertices;
        var sourceUv = mesh.uv;
        var selected = new List<int>[mesh.subMeshCount];
        var used = new HashSet<int>();
        var count = 0;
        for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            selected[submesh] = new List<int>();
            var triangles = mesh.GetTriangles(submesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var a = triangles[index];
                var b = triangles[index + 1];
                var c = triangles[index + 2];
                if (excluded.Contains((a, b, c)))
                    continue;
                var center = vehicleRoot.InverseTransformPoint(filter.transform.TransformPoint(
                    (sourceVertices[a] + sourceVertices[b] + sourceVertices[c]) / 3f));
                var radial = Vector2.Distance(new Vector2(center.y, center.z),
                    new Vector2(sourceAxle.y, sourceAxle.z));
                if (Mathf.Abs(center.x - sourceAxle.x) > 0.35f ||
                    // Tire radius is 0.36m; the separate 123-face bracket at 0.45m stays on the body.
                    radial < 0.27f || radial > 0.385f)
                    continue;
                var u = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(
                    (sourceUv[a].x + sourceUv[b].x + sourceUv[c].x) / 3f, 1f) * width),
                    0, width - 1);
                var v = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(
                    (sourceUv[a].y + sourceUv[b].y + sourceUv[c].y) / 3f, 1f) * height),
                    0, height - 1);
                var color = pixels[v * width + u];
                if (color.r > 110 || color.g > 110 || color.b > 110)
                    continue;
                selected[submesh].Add(a);
                selected[submesh].Add(b);
                selected[submesh].Add(c);
                used.Add(a);
                used.Add(b);
                used.Add(c);
                count++;
            }
        }
        if (count < 20 || count > 60)
            throw new InvalidOperationException(
                $"Expected roughly 32 detached front right tread faces, found {count}.");
        var bounds = new Bounds();
        var first = true;
        foreach (var vertex in used)
        {
            var point = vehicleRoot.InverseTransformPoint(
                filter.transform.TransformPoint(sourceVertices[vertex]));
            if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
            else bounds.Encapsulate(point);
        }
        var partIndex = wheels.Where(component => component.TireIndex == tire.TireIndex)
            .Select(component => component.PartIndex).DefaultIfEmpty(-1).Max() + 1;
        wheels.Add(new MeshComponent(filter, renderer, selected, used,
            new Bounds(bounds.center + tire.PositionOffset, bounds.size), count,
            tire.PositionOffset, tire.WheelAnchor)
        {
            TireIndex = tire.TireIndex,
            PartIndex = partIndex,
            WheelRotationCorrection = tire.WheelRotationCorrection,
        });
        Debug.Log($"Battle Bus front right detached tread moved onto tire: " +
                  $"faces={count}, bounds={bounds}.");
    }

    private static void AddLooseTireTread(List<MeshComponent> wheels, List<MeshComponent> details)
    {
        var tires = wheels.Where(component => component.FaceCount >= 1000 &&
                component.Bounds.size.y > 0.55f && component.Bounds.size.z > 0.55f)
            .GroupBy(component => component.TireIndex)
            .Select(group => group.OrderByDescending(component => component.FaceCount).First())
            .ToArray();
        if (tires.Length != 6)
            throw new InvalidOperationException($"Expected six tire anchors for loose tread, found {tires.Length}.");

        var counts = new Dictionary<int, int>();
        foreach (var detail in details.Where(component =>
                     string.Equals(component.Filter.name, "Object_10", StringComparison.Ordinal) &&
                     component.FaceCount >= 30 && component.FaceCount <= 100 &&
                     Mathf.Max(component.Bounds.size.x,
                         Mathf.Max(component.Bounds.size.y, component.Bounds.size.z)) <= 0.18f))
        {
            var center = detail.Bounds.center;
            var tire = tires.OrderBy(candidate =>
                Vector3.Distance(center, candidate.WheelAnchor - candidate.PositionOffset)).First();
            var axle = tire.WheelAnchor - tire.PositionOffset;
            var radial = Vector2.Distance(new Vector2(center.y, center.z),
                new Vector2(axle.y, axle.z));
            if (Mathf.Abs(center.x - axle.x) > 0.17f || radial < 0.23f || radial > 0.43f)
                continue;

            var partIndex = wheels.Where(component => component.TireIndex == tire.TireIndex)
                .Select(component => component.PartIndex).DefaultIfEmpty(-1).Max() + 1;
            wheels.Add(new MeshComponent(detail.Filter, detail.Renderer, detail.Triangles,
                detail.Vertices, new Bounds(center + tire.PositionOffset, detail.Bounds.size),
                detail.FaceCount, tire.PositionOffset, tire.WheelAnchor)
            {
                TireIndex = tire.TireIndex,
                PartIndex = partIndex,
                WheelRotationCorrection = tire.WheelRotationCorrection,
            });
            counts[tire.TireIndex] = counts.TryGetValue(tire.TireIndex, out var count) ? count + 1 : 1;
        }

        if (tires.Any(tire => counts.GetValueOrDefault(tire.TireIndex) !=
                (tire.Bounds.center.z < 0f ? 70 : 0)))
            throw new InvalidOperationException("Battle Bus rear tire tread extraction is incomplete: " +
                string.Join(", ", tires.Select(tire =>
                    $"tire {tire.TireIndex}={counts.GetValueOrDefault(tire.TireIndex)}")));
        Debug.Log($"Battle Bus loose tire tread moved from body to rolling visuals: " +
                  string.Join(", ", tires.Select(tire =>
                      $"tire {tire.TireIndex}={counts.GetValueOrDefault(tire.TireIndex)}")));
    }

    private static void AddRearDetachedTreadParts(List<MeshComponent> result,
        List<MeshComponent> candidates, List<MeshComponent> dualTires)
    {
        var outerTire = dualTires.OrderBy(tire => Mathf.Abs(tire.Bounds.center.x))
            .Last();
        var matchingProfiles = candidates.Where(component =>
            {
                var size = component.Bounds.size;
                var center = component.Bounds.center;
                return component.FaceCount >= 350 &&
                       size.x >= 0.025f && size.x <= 0.08f &&
                       size.y >= 0.28f && size.y <= 0.50f &&
                       size.z >= 0.70f && size.z <= 0.95f &&
                       Mathf.Abs(center.x - outerTire.Bounds.center.x) <= 0.12f &&
                       Mathf.Abs(center.y - outerTire.Bounds.center.y) >= 0.20f &&
                       Mathf.Abs(center.y - outerTire.Bounds.center.y) <= 0.40f &&
                       Mathf.Abs(center.z - outerTire.Bounds.center.z) <= 0.08f;
            })
            .OrderBy(component => Mathf.Abs(component.Bounds.center.x - outerTire.Bounds.center.x))
            .ToList();

        if (matchingProfiles.Count != 1)
        {
            Debug.LogWarning($"Battle Bus rear detached tread near wheel={outerTire.Bounds.center}: " +
                             $"expected one narrow profile component, found {matchingProfiles.Count}.");
            return;
        }

        var profile = matchingProfiles[0];
        if (result.Any(component => ReferenceEquals(component.Filter, profile.Filter) &&
                                    component.Triangles.Length == profile.Triangles.Length &&
                                    Enumerable.Range(0, profile.Triangles.Length).All(index =>
                                        ReferenceEquals(component.Triangles[index], profile.Triangles[index]))))
            return;

        var sourceWheelCenter = outerTire.Bounds.center;
        var wheel = result.OrderBy(component => Vector3.Distance(component.WheelAnchor, sourceWheelCenter))
            .First();
        var targetCenter = wheel.WheelAnchor;
        // Preserve the profile's authored position around the tire. Its bounds
        // center lies on the tread, not at the axle center.
        var offset = wheel.PositionOffset;
        var partIndex = result.Where(component => component.TireIndex == wheel.TireIndex)
            .Select(component => component.PartIndex).DefaultIfEmpty(-1).Max() + 1;
        result.Add(new MeshComponent(profile.Filter, profile.Renderer, profile.Triangles,
            profile.Vertices, new Bounds(profile.Bounds.center + offset, profile.Bounds.size), profile.FaceCount,
            offset, targetCenter)
        {
            TireIndex = wheel.TireIndex,
            PartIndex = partIndex,
            WheelRotationCorrection = wheel.WheelRotationCorrection,
        });
        Debug.Log($"Battle Bus detached rear tread attached to wheel at {targetCenter}: " +
                  $"renderer='{profile.Filter.name}', sourceCenter={profile.Bounds.center}, " +
                  $"offset={offset}, size={profile.Bounds.size}, faces={profile.FaceCount}.");
    }

    private static List<MeshComponent> FindWheelAssemblyParts(List<MeshComponent> candidates,
        MeshComponent tireTemplate, List<MeshComponent> tireCenters)
    {
        var center = tireTemplate.Bounds.center;
        return candidates.Where(component =>
                Mathf.Abs(component.Bounds.center.x - center.x) <= 0.18f &&
                Mathf.Abs(component.Bounds.center.y - center.y) <= 0.08f &&
                Mathf.Abs(component.Bounds.center.z - center.z) <= 0.08f &&
                !(center.z > 2.8f && component.FaceCount >= 500 && component.FaceCount <= 560 &&
                  component.Bounds.size.x >= 0.10f && component.Bounds.size.x <= 0.18f &&
                  component.Bounds.size.y >= 0.30f && component.Bounds.size.y <= 0.36f &&
                  component.Bounds.size.z >= 0.30f && component.Bounds.size.z <= 0.38f) &&
                !tireCenters.Any(other => !ReferenceEquals(other, tireTemplate) &&
                    Mathf.Abs(other.Bounds.center.x - component.Bounds.center.x) <= 0.18f &&
                    Mathf.Abs(other.Bounds.center.y - component.Bounds.center.y) <= 0.08f &&
                    Mathf.Abs(other.Bounds.center.z - component.Bounds.center.z) <= 0.08f &&
                    Vector3.Distance(other.Bounds.center, component.Bounds.center) <
                    Vector3.Distance(center, component.Bounds.center)))
            .OrderBy(component => component.Bounds.center.x)
            .ThenByDescending(component => component.FaceCount)
            .ToList();
    }

    private static void AddWheelAssembly(List<MeshComponent> result,
        List<MeshComponent> sourceParts, Vector3 sourceCenter, Vector3 targetCenter,
        Quaternion wheelRotationCorrection)
    {
        var offset = targetCenter - sourceCenter;
        var tireIndex = result.Select(component => component.TireIndex).DefaultIfEmpty(-1).Max() + 1;
        var partIndex = 0;
        foreach (var part in sourceParts)
        {
            var clone = new MeshComponent(part.Filter, part.Renderer, part.Triangles,
                part.Vertices, new Bounds(part.Bounds.center + offset, part.Bounds.size),
                part.FaceCount, offset, targetCenter)
            {
                TireIndex = tireIndex,
                PartIndex = partIndex++,
                WheelRotationCorrection = wheelRotationCorrection,
            };
            result.Add(clone);
        }
    }

    private static WheelGroups CreateWheelGroups(List<MeshComponent> wheels)
    {
        var tireAssemblies = wheels.GroupBy(component => component.TireIndex)
            .Select(group => new TireAssembly
            {
                Components = group.ToList(),
                Center = group.First().WheelAnchor,
                Size = group.OrderByDescending(component => component.FaceCount).First().Bounds.size,
            }).ToList();
        var rows = new List<List<TireAssembly>>();
        foreach (var tire in tireAssemblies.OrderBy(assembly => assembly.Center.z))
        {
            var row = rows.FirstOrDefault(existing =>
                Mathf.Abs(existing[0].Center.z - tire.Center.z) < WheelRowTolerance);
            if (row == null)
            {
                row = new List<TireAssembly>();
                rows.Add(row);
            }
            row.Add(tire);
        }
        var frontTires = rows.SingleOrDefault(row => row.Count == 2);
        var rearTires = rows.SingleOrDefault(row => row.Count == 4);
        if (rows.Count != 2 || frontTires == null || rearTires == null)
            throw new InvalidOperationException(
                $"Expected one pair of front tires and four rear dual tires; " +
                $"grouped rows={string.Join(", ", rows.Select(row => row.Count))}.");

        var centerX = tireAssemblies.Average(tire => tire.Center.x);
        var frontLeftTire = frontTires.SingleOrDefault(tire => tire.Center.x < centerX);
        var frontRightTire = frontTires.SingleOrDefault(tire => tire.Center.x >= centerX);
        var rearLeftTires = rearTires.Where(tire => tire.Center.x < centerX)
            .OrderBy(tire => tire.Center.x).ToList();
        var rearRightTires = rearTires.Where(tire => tire.Center.x >= centerX)
            .OrderBy(tire => tire.Center.x).ToList();
        if (frontLeftTire == null || frontRightTire == null ||
            rearLeftTires.Count != 2 || rearRightTires.Count != 2)
            throw new InvalidOperationException(
                $"Expected one front tire on each side and two rear tires on each side; " +
                $"front={frontTires.Count}, rear={rearLeftTires.Count}/{rearRightTires.Count}.");

        var groups = new WheelGroups
        {
            FrontLeft = frontLeftTire.Components,
            FrontRight = frontRightTire.Components,
            RearLeft = rearLeftTires.SelectMany(tire => tire.Components).ToList(),
            RearRight = rearRightTires.SelectMany(tire => tire.Components).ToList(),
            TireSize = AverageSizes(frontTires.Select(tire => tire.Size)),
            RearTireSize = AverageSizes(rearTires.Select(tire => tire.Size)),
        };
        groups.FrontLeftPosition = frontLeftTire.Center;
        groups.FrontRightPosition = frontRightTire.Center;
        groups.RearLeftPosition = AverageTirePosition(rearLeftTires);
        groups.RearRightPosition = AverageTirePosition(rearRightTires);
        groups.AverageRadius = groups.TireSize.y * 0.5f;
        var dualWheelCenterSpacing = Mathf.Abs(rearLeftTires[1].Center.x - rearLeftTires[0].Center.x);
        groups.RearTireSize.x += dualWheelCenterSpacing;
        groups.SupportPositions = new List<Vector3>
        {
            groups.FrontLeftPosition,
            groups.FrontRightPosition,
            groups.RearLeftPosition,
            groups.RearRightPosition,
        };
        return groups;
    }

    private static Vector3 AverageSizes(IEnumerable<Vector3> sizes)
    {
        var list = sizes.ToList();
        return list.Aggregate(Vector3.zero, (sum, size) => sum + size) / list.Count;
    }

    private static Vector3 AverageTirePosition(List<TireAssembly> tires)
    {
        return tires.Aggregate(Vector3.zero, (sum, tire) => sum + tire.Center) / tires.Count;
    }

    private static void SplitWheelComponents(List<MeshComponent> wheels, WheelGroups groups)
    {
        EnsureAssetFolder(GeneratedMeshFolder);
        var selectedByFilter = wheels.GroupBy(wheel => wheel.Filter).ToDictionary(
            group => group.Key,
            group => group.ToList());
        var meshIndex = 0;
        foreach (var pair in selectedByFilter)
        {
            var filter = pair.Key;
            var sourceMesh = pair.Value[0].SourceMesh;
            var renderer = filter.GetComponent<Renderer>();
            if (sourceMesh == null || renderer == null)
                continue;

            var removed = new HashSet<TriangleKey>[sourceMesh.subMeshCount];
            for (var submesh = 0; submesh < removed.Length; submesh++)
                removed[submesh] = new HashSet<TriangleKey>();
            foreach (var component in pair.Value)
                for (var submesh = 0; submesh < component.Triangles.Length; submesh++)
                {
                    var selected = component.Triangles[submesh];
                    for (var index = 0; index + 2 < selected.Count; index += 3)
                        removed[submesh].Add(new TriangleKey(
                            selected[index], selected[index + 1], selected[index + 2]));
                }

            var remainder = UnityEngine.Object.Instantiate(sourceMesh);
            remainder.name = $"BattleBusRemainder_{meshIndex:D2}_{Sanitize(filter.name)}";
            var originalTriangleCount = 0;
            var remainingTriangleCount = 0;
            for (var submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
            {
                var original = sourceMesh.GetTriangles(submesh);
                originalTriangleCount += original.Length / 3;
                var kept = new List<int>(original.Length);
                for (var index = 0; index + 2 < original.Length; index += 3)
                {
                    if (removed[submesh].Contains(new TriangleKey(
                            original[index], original[index + 1], original[index + 2])))
                        continue;
                    kept.Add(original[index]);
                    kept.Add(original[index + 1]);
                    kept.Add(original[index + 2]);
                    remainingTriangleCount++;
                }
                remainder.SetTriangles(kept, submesh, false);
            }
            if (originalTriangleCount == 0 || remainingTriangleCount < originalTriangleCount * 0.75f)
            {
                UnityEngine.Object.DestroyImmediate(remainder);
                throw new InvalidOperationException(
                    $"Wheel extraction would remove too much of Battle Bus renderer '{filter.name}': " +
                    $"remaining={remainingTriangleCount}/{originalTriangleCount} triangles.");
            }
            remainder.RecalculateBounds();
            var remainderPath = $"{GeneratedMeshFolder}/Remainder_{meshIndex:D2}_{Sanitize(filter.name)}.asset";
            filter.sharedMesh = SaveGeneratedMesh(remainder, remainderPath);
            var selectedTriangleCount = removed.Sum(set => set.Count);
            Debug.Log($"Battle Bus wheel extraction removed exact source triangles from renderer='{filter.name}': " +
                      $"extracted={selectedTriangleCount}, remaining={remainingTriangleCount}/{originalTriangleCount}.");

            foreach (var component in pair.Value)
                CreateTireVisual(component, groups);
            meshIndex++;
        }
    }

    private static Renderer SplitBalloonGeometry(Transform modelRoot, Transform vehicleRoot)
    {
        var renderer = modelRoot.GetComponentsInChildren<MeshRenderer>(true)
            .SingleOrDefault(candidate => string.Equals(candidate.name, "Object_14",
                StringComparison.Ordinal));
        if (renderer == null)
            throw new InvalidOperationException("Could not identify the mixed bus-body and balloon renderer Object_14.");

        var filter = renderer.GetComponent<MeshFilter>();
        var source = filter != null ? filter.sharedMesh : null;
        if (filter == null || source == null)
            throw new InvalidOperationException("The mixed bus-body and balloon renderer has no source mesh.");

        var sourceVertices = source.vertices;
        var rootVertices = new Vector3[sourceVertices.Length];
        for (var index = 0; index < sourceVertices.Length; index++)
            rootVertices[index] = vehicleRoot.InverseTransformPoint(
                filter.transform.TransformPoint(sourceVertices[index]));

        var bodyTriangles = new List<int>[source.subMeshCount];
        var balloonTriangles = new List<int>[source.subMeshCount];
        var capTriangles = new List<int>[source.subMeshCount];
        var balloonTexture = GetMaterialBaseColorTexture(renderer.sharedMaterial) as Texture2D;
        if (balloonTexture == null || !TryReadEmbeddedTexturePixels(balloonTexture,
                out var balloonPixels, out var balloonWidth, out var balloonHeight))
            throw new InvalidOperationException("Could not inspect the balloon cap paint atlas.");
        var sourceUv = source.uv;
        var allTriangles = source.triangles;
        var balloonHeights = allTriangles.Select((vertex,index)=>new { vertex,index }).Where(item=>item.index%3==0)
            .Select(item=>(rootVertices[allTriangles[item.index]].y+rootVertices[allTriangles[item.index+1]].y+rootVertices[allTriangles[item.index+2]].y)/3f)
            .Where(height=>height>=BalloonSplitHeight).OrderBy(height=>height).ToArray();
        var capSplit=balloonHeights[balloonHeights.Length/2];
        var bodyFaceCount = 0;
        var balloonFaceCount = 0;
        var capFaceCount = 0;
        for (var submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            bodyTriangles[submesh] = new List<int>();
            balloonTriangles[submesh] = new List<int>();
            capTriangles[submesh] = new List<int>();
            var triangles = source.GetTriangles(submesh);
            for (var index = 0; index + 2 < triangles.Length; index += 3)
            {
                var first = triangles[index];
                var second = triangles[index + 1];
                var third = triangles[index + 2];
                var balloonTriangle =
                    (rootVertices[first].y + rootVertices[second].y + rootVertices[third].y) / 3f >=
                    BalloonSplitHeight;
                var capTriangle = balloonTriangle &&
                    (rootVertices[first].y + rootVertices[second].y + rootVertices[third].y) / 3f >= capSplit;
                var destination = capTriangle ? capTriangles[submesh] :
                    balloonTriangle ? balloonTriangles[submesh] : bodyTriangles[submesh];
                destination.Add(first);
                destination.Add(second);
                destination.Add(third);
                if (capTriangle)
                    capFaceCount++;
                else if (balloonTriangle)
                    balloonFaceCount++;
                else
                    bodyFaceCount++;
            }
        }

        if (bodyFaceCount < 500 || balloonFaceCount < 500 || capFaceCount < 100)
            throw new InvalidOperationException(
                $"Could not split Object_14 into bus and balloon geometry: " +
                $"bodyFaces={bodyFaceCount}, balloonFaces={balloonFaceCount}, capFaces={capFaceCount}, splitY={BalloonSplitHeight:F1}.");

        EnsureAssetFolder(GeneratedMeshFolder);
        var bodyName = $"PaintableBody_{Sanitize(filter.name)}";
        var bodyPath = $"{GeneratedMeshFolder}/{bodyName}.asset";
        var bodyMesh = SaveGeneratedMesh(CreateFilteredMesh(source, bodyTriangles, bodyName), bodyPath);
        filter.sharedMesh = bodyMesh;

        var balloonName = $"OriginalColorBalloon_{Sanitize(filter.name)}";
        var balloonPath = $"{GeneratedMeshFolder}/{balloonName}.asset";
        var balloonMesh = SaveGeneratedMesh(CreateFilteredMesh(source, balloonTriangles, balloonName),
            balloonPath);

        var balloonObject = new GameObject($"{filter.name}_OriginalColorBalloon");
        balloonObject.layer = filter.gameObject.layer;
        balloonObject.transform.SetParent(filter.transform.parent, false);
        balloonObject.transform.localPosition = filter.transform.localPosition;
        balloonObject.transform.localRotation = filter.transform.localRotation;
        balloonObject.transform.localScale = filter.transform.localScale;
        var balloonFilter = balloonObject.AddComponent<MeshFilter>();
        balloonFilter.sharedMesh = balloonMesh;
        var balloonRenderer = balloonObject.AddComponent<MeshRenderer>();
        balloonRenderer.sharedMaterials = renderer.sharedMaterials;
        CopyRendererSettings(renderer, balloonRenderer);
        SetBundle(balloonPath);

        var capPath = $"{GeneratedMeshFolder}/PaintableBalloonCap_{Sanitize(filter.name)}.asset";
        var capMesh = SaveGeneratedMesh(CreateFilteredMesh(source, capTriangles,
            "PaintableBalloonCap"), capPath);
        var capObject = new GameObject("BattleBusPaintBalloonCap") { layer = filter.gameObject.layer };
        capObject.transform.SetParent(filter.transform.parent, false);
        capObject.transform.localPosition = filter.transform.localPosition;
        capObject.transform.localRotation = filter.transform.localRotation;
        capObject.transform.localScale = filter.transform.localScale;
        capObject.AddComponent<MeshFilter>().sharedMesh = capMesh;
        var capRenderer = capObject.AddComponent<MeshRenderer>();
        capRenderer.sharedMaterials = renderer.sharedMaterials;
        CopyRendererSettings(renderer, capRenderer);
        SetBundle(capPath);

        if (!TryGetRendererBounds(vehicleRoot, new[] { balloonRenderer }, out var balloonBounds))
            throw new InvalidOperationException("Separated Battle Bus balloon has no measurable bounds.");

        Debug.Log($"Battle Bus balloon geometry separated from paintable body: " +
                  $"renderer='{filter.name}', bodyFaces={bodyFaceCount}, balloonFaces={balloonFaceCount}, capFaces={capFaceCount}, " +
                  $"splitY={BalloonSplitHeight:F1}, balloonBounds={balloonBounds}, " +
                  $"originalMaterials={balloonRenderer.sharedMaterials.Length}.");
        return balloonRenderer;
    }

    private static void CompleteFrontEntryDoor(Transform modelRoot, Transform vehicleRoot)
    {
        var parts = DiscoverWheelComponents(modelRoot, vehicleRoot, true, true, 2.4f, 1.5f)
            .Where(part => part.Filter.name == PaintableHullRendererName &&
                part.Bounds.center.x > 0.7f && part.Bounds.center.z > 1.9f &&
                part.Bounds.center.z < 2.6f).ToArray();
        var frames = parts.Where(part => part.FaceCount == 640).ToArray();
        var windows = parts.Where(part => part.FaceCount == 560).ToArray();
        if (frames.Length != 1 || windows.Length != 4)
            throw new InvalidOperationException($"Entry door geometry changed: frames={frames.Length}, windows={windows.Length}.");
        var upper = windows.OrderByDescending(part => part.Bounds.center.y).Take(2)
            .OrderBy(part => part.Bounds.center.z).ToArray();
        var lower = windows.OrderBy(part => part.Bounds.center.y).Take(2)
            .OrderBy(part => part.Bounds.center.z).ToArray();
        var upperDelta = upper[0].Bounds.center - upper[1].Bounds.center;
        var lowerDelta = lower[0].Bounds.center - lower[1].Bounds.center;
        var template = frames[0];
        var filter = template.Filter;
        var source = filter.sharedMesh;
        if (source == null || source.subMeshCount != 1)
            throw new InvalidOperationException("Entry door must share the single opaque hull material.");
        // The original rear leaf has glass rectangles and rubber outlines but
        // no solid surround. Reuse the complete forward leaf, preserving its
        // window openings and aligning both pairs of existing window outlines.
        var backing = CreateFilteredMesh(source, template.Triangles, "EntryDoorBacking");
        var vertices = backing.vertices;
        for (var i = 0; i < vertices.Length; i++)
        {
            var point = vehicleRoot.InverseTransformPoint(filter.transform.TransformPoint(vertices[i]));
            var height = (point.y - lower[1].Bounds.center.y) /
                         (upper[1].Bounds.center.y - lower[1].Bounds.center.y);
            point += Vector3.LerpUnclamped(lowerDelta, upperDelta, height);
            vertices[i] = filter.transform.InverseTransformPoint(vehicleRoot.TransformPoint(point));
        }
        backing.vertices = vertices;
        backing.RecalculateNormals();
        var combined = new Mesh { name = source.name, indexFormat = IndexFormat.UInt32 };
        combined.CombineMeshes(new[] {
            new CombineInstance { mesh = source, transform = Matrix4x4.identity },
            new CombineInstance { mesh = backing, transform = Matrix4x4.identity },
        }, true, true);
        var path = AssetDatabase.GetAssetPath(source);
        filter.sharedMesh = SaveGeneratedMesh(combined, path);
        UnityEngine.Object.DestroyImmediate(backing);
        SetBundle(path);
        Debug.Log($"Battle Bus missing entry-door surround rebuilt: faces={template.FaceCount}, " +
                  $"upperOffset={upperDelta}, lowerOffset={lowerDelta}; existing glass and window openings retained.");
    }

    private static void RemoveFrontProtrudingHubs(Transform modelRoot, Transform vehicleRoot,
        WheelGroups wheels)
    {
        var front = new[] { wheels.FrontLeftPosition, wheels.FrontRightPosition };
        var candidates = DiscoverWheelComponents(modelRoot, vehicleRoot,
            includeThinDetachedTread: true, includeSmallDetails: true);
        // The two 454-face caps and their sixteen separate 40-face bolts are
        // the protruding axle ends. The 432-face inner rim discs stay on the wheels.
        var stubs = candidates.Where(component =>
                (component.Filter.name == "Object_12" && component.FaceCount == 454 ||
                 (component.Filter.name == "Object_10" || component.Filter.name == "Object_12") &&
                 component.FaceCount == 40 && component.Bounds.size.magnitude < 0.09f) &&
                front.Any(wheel => Mathf.Abs(component.Bounds.center.z - wheel.z) < 0.13f &&
                    Mathf.Abs(component.Bounds.center.x - wheel.x) < 0.34f))
            .ToArray();
        if (stubs.Length != 18 || stubs.Count(stub => stub.FaceCount == 454) != 2 ||
            stubs.Count(stub => stub.Bounds.center.x < 0f) != 9)
            throw new InvalidOperationException(
                $"Expected two protruding front axle caps and sixteen bolts, found {stubs.Length}: " +
                string.Join(", ", stubs.Select(stub =>
                    $"{stub.Filter.name}@{stub.Bounds.center}/{stub.Bounds.size}")));

        foreach (var group in stubs.GroupBy(stub => stub.Filter))
        {
            var filter = group.Key;
            var source = filter.sharedMesh ??
                         throw new InvalidOperationException("Front axle stub source mesh is missing.");
            var remainder = new List<int>[source.subMeshCount];
            for (var submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                var excluded = new HashSet<(int, int, int)>();
                foreach (var stub in group)
                {
                    var triangles = stub.Triangles[submesh];
                    for (var index = 0; index + 2 < triangles.Count; index += 3)
                        excluded.Add((triangles[index], triangles[index + 1], triangles[index + 2]));
                }
                remainder[submesh] = new List<int>();
                var sourceTriangles = source.GetTriangles(submesh);
                for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
                    if (!excluded.Contains((sourceTriangles[index], sourceTriangles[index + 1],
                            sourceTriangles[index + 2])))
                    {
                        remainder[submesh].Add(sourceTriangles[index]);
                        remainder[submesh].Add(sourceTriangles[index + 1]);
                        remainder[submesh].Add(sourceTriangles[index + 2]);
                    }
            }
            var path = $"{GeneratedMeshFolder}/WithoutFrontProtrudingHub_{filter.name}.asset";
            filter.sharedMesh = SaveGeneratedMesh(CreateFilteredMesh(source, remainder,
                $"WithoutFrontProtrudingHub_{filter.name}"), path);
            SetBundle(path);
        }
        Debug.Log("Battle Bus front axle caps and bolts removed; both rim discs and suspension supports retained: " +
                  string.Join(", ", stubs.Select(stub =>
                      $"{stub.Filter.name}@{stub.Bounds.center}, faces={stub.FaceCount}")));
    }

    private static void RemoveRearLooseArcs(Transform modelRoot, Transform vehicleRoot, WheelGroups wheels)
    {
        var rear = new[] { wheels.RearLeftPosition, wheels.RearRightPosition };
        var arcs = DiscoverWheelComponents(modelRoot, vehicleRoot, includeThinDetachedTread: true)
            .Where(component => component.Filter.name == "Object_12" &&
                component.FaceCount >= 350 && component.FaceCount <= 650 &&
                component.Bounds.size.x >= 0.025f && component.Bounds.size.x <= 0.08f &&
                component.Bounds.size.y >= 0.28f && component.Bounds.size.y <= 0.50f &&
                component.Bounds.size.z >= 0.70f && component.Bounds.size.z <= 0.95f &&
                rear.Any(position => Mathf.Abs(component.Bounds.center.x - position.x) < 0.22f &&
                    Mathf.Abs(component.Bounds.center.z - position.z) < 0.09f))
            .ToArray();
        if (arcs.Length != 2)
            throw new InvalidOperationException($"Expected two detached rear profile arcs, found {arcs.Length}.");

        var filter = arcs[0].Filter;
        var source = filter.sharedMesh ?? throw new InvalidOperationException("Rear profile source is missing.");
        var kept = new List<int>[source.subMeshCount];
        for (var submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            var removed = new HashSet<(int, int, int)>();
            foreach (var arc in arcs)
            {
                var triangles = arc.Triangles[submesh];
                for (var index = 0; index + 2 < triangles.Count; index += 3)
                    removed.Add((triangles[index], triangles[index + 1], triangles[index + 2]));
            }
            kept[submesh] = new List<int>();
            var trianglesInMesh = source.GetTriangles(submesh);
            for (var index = 0; index + 2 < trianglesInMesh.Length; index += 3)
                if (!removed.Contains((trianglesInMesh[index], trianglesInMesh[index + 1],
                        trianglesInMesh[index + 2])))
            {
                kept[submesh].Add(trianglesInMesh[index]);
                kept[submesh].Add(trianglesInMesh[index + 1]);
                kept[submesh].Add(trianglesInMesh[index + 2]);
            }
        }
        var path = $"{GeneratedMeshFolder}/WithoutRearLooseArcs_{filter.name}.asset";
        filter.sharedMesh = SaveGeneratedMesh(CreateFilteredMesh(source, kept,
            "WithoutRearLooseArcs"), path);
        SetBundle(path);
        Debug.Log($"Battle Bus removed {arcs.Length} disconnected rear profile arcs; " +
                  "the individual tire lugs remain attached to the rolling meshes.");
    }

    private static Transform[] ExtractRearArmLowerSections(Transform modelRoot, Transform vehicleRoot)
    {
        var parts = DiscoverWheelComponents(modelRoot, vehicleRoot,
                includeThinDetachedTread: true, includeSmallDetails: true,
                maximumCenterHeight: 1.9f)
            .Where(component => Mathf.Abs(component.Bounds.center.x) > 0.72f &&
                Mathf.Abs(component.Bounds.center.x) < 1.12f &&
                (component.Filter.name == "Object_10" &&
                    ((component.FaceCount >= 400 && component.FaceCount <= 445 &&
                      component.Bounds.center.y > 0.70f && component.Bounds.center.y < 1.15f &&
                      component.Bounds.center.z > -3.05f && component.Bounds.center.z < -2.70f) ||
                     (component.FaceCount >= 150 && component.FaceCount <= 160 &&
                      component.Bounds.center.y > 1.55f && component.Bounds.center.y < 1.90f &&
                      component.Bounds.center.z > -3.75f && component.Bounds.center.z < -3.40f) ||
                     (component.FaceCount >= 175 && component.FaceCount <= 185 &&
                      component.Bounds.center.y > 0.14f && component.Bounds.center.y < 0.28f &&
                      component.Bounds.center.z > -2.30f && component.Bounds.center.z < -2.05f) ||
                     (component.FaceCount >= 84 && component.FaceCount <= 90 &&
                      component.Bounds.center.y > 0.45f && component.Bounds.center.y < 0.85f &&
                      component.Bounds.center.z > -2.65f && component.Bounds.center.z < -2.30f) ||
                     ((component.FaceCount == 141 || component.FaceCount == 176) &&
                      component.Bounds.size.x < 0.03f && component.Bounds.center.y > 0.4f &&
                      component.Bounds.center.y < 0.8f && component.Bounds.center.z > -2.6f &&
                      component.Bounds.center.z < -2.1f) ||
                     (component.FaceCount == 880 && component.Bounds.center.y < 0.3f &&
                      component.Bounds.center.z > -2.3f && component.Bounds.center.z < -2.05f)) ||
                 component.Filter.name == "Object_12" &&
                    ((component.FaceCount >= 950 && component.FaceCount <= 965 &&
                      component.Bounds.center.y > 0.65f && component.Bounds.center.y < 1.15f &&
                      component.Bounds.center.z > -3.20f && component.Bounds.center.z < -2.85f) ||
                     (component.FaceCount >= 805 && component.FaceCount <= 850 &&
                      component.Bounds.center.y > 1.10f && component.Bounds.center.y < 1.60f &&
                      component.Bounds.center.z > -3.60f && component.Bounds.center.z < -3.20f) ||
                     (component.FaceCount >= 150 && component.FaceCount <= 160 &&
                      component.Bounds.center.y > 1.35f && component.Bounds.center.y < 1.75f &&
                      component.Bounds.center.z > -3.70f && component.Bounds.center.z < -3.40f) ||
                     (component.FaceCount >= 180 && component.FaceCount <= 190 &&
                      component.Bounds.center.y > 0.95f && component.Bounds.center.y < 1.30f &&
                      component.Bounds.center.z > -3.60f && component.Bounds.center.z < -3.30f) ||
                     (component.FaceCount >= 165 && component.FaceCount <= 175 &&
                      component.Bounds.center.y > 0.35f && component.Bounds.center.y < 0.75f &&
                      component.Bounds.center.z > -2.75f && component.Bounds.center.z < -2.45f) ||
                     (component.FaceCount >= 265 && component.FaceCount <= 280 &&
                      component.Bounds.center.y > 0.35f && component.Bounds.center.y < 0.75f &&
                      component.Bounds.center.z > -2.50f && component.Bounds.center.z < -2.20f) ||
                     (component.FaceCount >= 175 && component.FaceCount <= 185 &&
                      component.Bounds.center.y > 0.20f && component.Bounds.center.y < 0.70f &&
                      component.Bounds.center.z > -2.35f && component.Bounds.center.z < -2.05f) ||
                     ((component.FaceCount >= 135 && component.FaceCount <= 145 ||
                       component.FaceCount >= 55 && component.FaceCount <= 65) &&
                      component.Bounds.center.y > 0.45f && component.Bounds.center.y < 1.20f &&
                      component.Bounds.center.z > -3.05f && component.Bounds.center.z < -2.55f) ||
                     (component.FaceCount == 40 && component.Bounds.size.magnitude < 0.08f &&
                      Mathf.Abs(component.Bounds.center.x) > 0.9f &&
                      component.Bounds.center.y > 0.3f && component.Bounds.center.y < 0.75f &&
                      component.Bounds.center.z > -2.55f && component.Bounds.center.z < -2.1f) ||
                     ((component.FaceCount == 13 || component.FaceCount == 23) &&
                      component.Bounds.center.x > 1.02f && component.Bounds.center.y > 0.45f &&
                      component.Bounds.center.y < 0.7f && component.Bounds.center.z > -2.45f &&
                      component.Bounds.center.z < -2.15f))))
            .ToArray();
        if (parts.Length != 44 || parts.Count(part => part.Bounds.center.x < 0f) != 21)
            throw new InvalidOperationException("Expected 21 left and 23 right movable rear arm pieces, found " +
                string.Join(", ", parts.GroupBy(part => part.Filter.name)
                    .Select(group => $"{group.Key}={group.Count()}")) + ": " +
                string.Join("; ", parts.Select(part =>
                    $"{part.Filter.name}/{part.FaceCount}@{part.Bounds.center}")));

        var arms = new Transform[2];
        for (var side = 0; side < arms.Length; side++)
        {
            var sign = side == 0 ? -1f : 1f;
            var pivot = new GameObject(side == 0 ? "BattleBusRearArmLeft" : "BattleBusRearArmRight")
                .transform;
            pivot.SetParent(modelRoot, false);
            pivot.position = vehicleRoot.TransformPoint(new Vector3(sign < 0f ? -0.84f : 0.94f,
                1.90f, -3.58f));
            pivot.rotation = vehicleRoot.rotation;
            arms[side] = pivot;
        }

        foreach (var sourceGroup in parts.GroupBy(part => part.Filter))
        {
            var sourceFilter = sourceGroup.Key;
            var source = sourceFilter.sharedMesh ??
                         throw new InvalidOperationException("Battle Bus rear arm source mesh is missing.");
            var sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
            var remainder = new List<int>[source.subMeshCount];
            for (var submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                var excluded = new HashSet<(int, int, int)>();
                foreach (var component in sourceGroup)
                {
                    var selected = component.Triangles[submesh];
                    for (var index = 0; index + 2 < selected.Count; index += 3)
                        excluded.Add((selected[index], selected[index + 1], selected[index + 2]));
                }
                remainder[submesh] = new List<int>();
                var original = source.GetTriangles(submesh);
                for (var index = 0; index + 2 < original.Length; index += 3)
                    if (!excluded.Contains((original[index], original[index + 1], original[index + 2])))
                    {
                        remainder[submesh].Add(original[index]);
                        remainder[submesh].Add(original[index + 1]);
                        remainder[submesh].Add(original[index + 2]);
                    }
            }

            var partNumber = 0;
            foreach (var component in sourceGroup)
            {
                var side = component.Bounds.center.x < 0f ? 0 : 1;
                var pivot = arms[side];
                var name = $"RearArm_{sourceFilter.name}_{(side == 0 ? "L" : "R")}_{partNumber++:D2}";
                var mesh = CreateFilteredMesh(source, component.Triangles, name);
                var vertices = mesh.vertices;
                for (var index = 0; index < vertices.Length; index++)
                    vertices[index] = pivot.InverseTransformPoint(
                        sourceFilter.transform.TransformPoint(vertices[index]));
                mesh.vertices = vertices;
                var normals = mesh.normals;
                for (var index = 0; index < normals.Length; index++)
                    normals[index] = pivot.InverseTransformDirection(
                        sourceFilter.transform.TransformDirection(normals[index])).normalized;
                mesh.normals = normals;
                mesh.RecalculateBounds();
                var meshPath = $"{GeneratedMeshFolder}/{name}.asset";
                var savedMesh = SaveGeneratedMesh(mesh, meshPath);
                SetBundle(meshPath);
                var partObject = new GameObject(name) { layer = sourceFilter.gameObject.layer };
                partObject.transform.SetParent(pivot, false);
                partObject.AddComponent<MeshFilter>().sharedMesh = savedMesh;
                var partRenderer = partObject.AddComponent<MeshRenderer>();
                partRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
                CopyRendererSettings(sourceRenderer, partRenderer);
            }
            var remainderPath = $"{GeneratedMeshFolder}/WithoutRearArm_{sourceFilter.name}.asset";
            sourceFilter.sharedMesh = SaveGeneratedMesh(CreateFilteredMesh(source, remainder,
                $"WithoutRearArm_{sourceFilter.name}"), remainderPath);
            SetBundle(remainderPath);
        }
        Debug.Log($"Battle Bus rear arm lower and middle sections articulated: " +
                  $"left=21, right=23; roof mounts stay fixed.");
        return arms;
    }

    private static void ExtractSingleSideDoor(Transform modelRoot, Transform vehicleRoot)
    {
        var doors = DiscoverWheelComponents(modelRoot, vehicleRoot,
                includeThinDetachedTread: true, includeSmallDetails: true)
            .Where(component => component.Filter.name == "Object_10" &&
                component.FaceCount >= 470 && component.FaceCount <= 520 &&
                component.Bounds.center.x < -0.55f && component.Bounds.center.x > -0.90f &&
                component.Bounds.center.y > 0.75f && component.Bounds.center.y < 1.35f &&
                component.Bounds.center.z > -0.75f && component.Bounds.center.z < -0.15f &&
                component.Bounds.size.x < 0.05f &&
                component.Bounds.size.y > 0.9f && component.Bounds.size.y < 1.2f &&
                component.Bounds.size.z > 0.4f && component.Bounds.size.z < 0.65f)
            .ToArray();
        if (doors.Length != 1)
            throw new InvalidOperationException($"Expected one separate single side door, found {doors.Length}.");

        var door = doors[0];
        var filter = door.Filter;
        var source = filter.sharedMesh ??
                     throw new InvalidOperationException("Battle Bus single side door source mesh is missing.");
        var remainder = new List<int>[source.subMeshCount];
        for (var submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            var excluded = new HashSet<(int, int, int)>();
            var selected = door.Triangles[submesh];
            for (var index = 0; index + 2 < selected.Count; index += 3)
                excluded.Add((selected[index], selected[index + 1], selected[index + 2]));
            remainder[submesh] = new List<int>();
            var original = source.GetTriangles(submesh);
            for (var index = 0; index + 2 < original.Length; index += 3)
                if (!excluded.Contains((original[index], original[index + 1], original[index + 2])))
                {
                    remainder[submesh].Add(original[index]);
                    remainder[submesh].Add(original[index + 1]);
                    remainder[submesh].Add(original[index + 2]);
                }
        }

        var doorPath = $"{GeneratedMeshFolder}/SingleSideDoor.asset";
        var remainderPath = $"{GeneratedMeshFolder}/WithoutSingleSideDoor.asset";
        var doorMesh = SaveGeneratedMesh(CreateFilteredMesh(source, door.Triangles,
            "SingleSideDoor"), doorPath);
        filter.sharedMesh = SaveGeneratedMesh(CreateFilteredMesh(source, remainder,
            "WithoutSingleSideDoor"), remainderPath);
        SetBundle(doorPath);
        SetBundle(remainderPath);

        var doorObject = new GameObject("BattleBusPaintSingleSideDoor") { layer = filter.gameObject.layer };
        doorObject.transform.SetParent(filter.transform.parent, false);
        doorObject.transform.localPosition = filter.transform.localPosition;
        doorObject.transform.localRotation = filter.transform.localRotation;
        doorObject.transform.localScale = filter.transform.localScale;
        doorObject.AddComponent<MeshFilter>().sharedMesh = doorMesh;
        var doorRenderer = doorObject.AddComponent<MeshRenderer>();
        var sourceRenderer = filter.GetComponent<MeshRenderer>();
        doorRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        CopyRendererSettings(sourceRenderer, doorRenderer);
        Debug.Log($"Battle Bus single side door separated for opaque paint: " +
                  $"faces={door.FaceCount}, bounds={door.Bounds}.");
    }

    private static void SplitPaintedBodyDetails(Transform modelRoot, Transform vehicleRoot)
    {
        var components = DiscoverWheelComponents(modelRoot, vehicleRoot, true, true, 3.5f, 4.5f);
        var hoses = components.Where(part => part.Filter.name == "Object_10" &&
            part.FaceCount == 6500 && part.Bounds.center.z > 2f).ToArray();
        var panels = components.Where(part => part.Filter.name == "Object_10" &&
            (part.FaceCount == 824 && part.Bounds.center.z < -3.9f ||
             part.FaceCount == 72 && part.Bounds.center.x < -0.65f &&
             part.Bounds.center.z > 2f && part.Bounds.center.y < 0.8f)).ToArray();
        if (hoses.Length != 1 || panels.Length != 2)
            throw new InvalidOperationException($"Expected one complete hose and two body panels: hoses={hoses.Length}, panels={panels.Length}.");
        var filters = modelRoot.GetComponentsInChildren<MeshFilter>(true)
            .Where(filter => filter.name == "Object_10" || filter.name == "Object_12").ToArray();
        if (filters.Length != 2)
            throw new InvalidOperationException($"Expected two body accent atlas meshes, found {filters.Length}.");

        foreach (var filter in filters)
        {
            var source = filter.sharedMesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (source == null || renderer == null)
                throw new InvalidOperationException($"Paint detail source '{filter.name}' is missing.");
            var texture = GetMaterialBaseColorTexture(renderer.sharedMaterial) as Texture2D;
            if (texture == null || !TryReadEmbeddedTexturePixels(texture,
                    out var pixels, out var width, out var height))
                throw new InvalidOperationException($"Could not inspect blue paint atlas on '{filter.name}'.");

            var vertices = source.vertices;
            var uv = source.uv;
            var hoseVertices = new HashSet<int>(hoses.Where(part => part.Filter == filter)
                .SelectMany(part => part.Vertices));
            var panelVertices = new HashSet<int>(panels.Where(part => part.Filter == filter)
                .SelectMany(part => part.Vertices));
            var fixedTriangles = new List<int>[source.subMeshCount];
            var paintTriangles = new List<int>[source.subMeshCount];
            var doorTriangles = new List<int>[source.subMeshCount];
            var selectedFaces = 0;
            var selectedDoorFaces = 0;
            for (var submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                fixedTriangles[submesh] = new List<int>();
                paintTriangles[submesh] = new List<int>();
                doorTriangles[submesh] = new List<int>();
                var triangles = source.GetTriangles(submesh);
                for (var index = 0; index + 2 < triangles.Length; index += 3)
                {
                    var a = triangles[index];
                    var b = triangles[index + 1];
                    var c = triangles[index + 2];
                    var position = vehicleRoot.InverseTransformPoint(filter.transform.TransformPoint(
                        (vertices[a] + vertices[b] + vertices[c]) / 3f));
                    var bodySide = Mathf.Abs(position.x) > 0.65f && position.y > 0.35f &&
                                   position.y < 2.8f && position.z > -4.5f && position.z < 3.5f;
                    var upperRearPanel = position.z < -3.9f && position.y > 1.75f &&
                                         position.y < 2.8f && Mathf.Abs(position.x) < 1.1f;
                    var frontPanel = position.z > 2.45f && position.z < 2.85f &&
                                     position.y > 0.85f && position.y < 2.3f &&
                                     Mathf.Abs(position.x) < 0.75f;
                    var blue = uv.Length == source.vertexCount &&
                               IsBlueAtlasTriangle(pixels, width, height, uv[a], uv[b], uv[c]);
                    var faceNormal = vehicleRoot.InverseTransformDirection(
                        filter.transform.TransformDirection(Vector3.Cross(
                            vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized));
                    var paintDoor = !hoseVertices.Contains(a) && filter.name == "Object_10" &&
                                    position.x > 0.65f && position.x < 1.08f &&
                                    position.y > 0.30f && position.y < 2.25f &&
                                    position.z > 2.20f && position.z < 2.82f &&
                                    Mathf.Abs(faceNormal.x) > 0.60f;
                    // Whole connected parts avoid the sawtooth boundaries caused
                    // by classifying individual triangles on a shared texture atlas.
                    var paint = !paintDoor && !hoseVertices.Contains(a) &&
                                (((bodySide || upperRearPanel || frontPanel) && blue) ||
                                 panelVertices.Contains(a));
                    var target = paintDoor ? doorTriangles[submesh] :
                        paint ? paintTriangles[submesh] : fixedTriangles[submesh];
                    target.Add(a);
                    target.Add(b);
                    target.Add(c);
                    if (paint)
                        selectedFaces++;
                    if (paintDoor)
                        selectedDoorFaces++;
                }
            }

            if (selectedFaces < 150)
                throw new InvalidOperationException(
                    $"Only {selectedFaces} paint detail faces identified on '{filter.name}'.");
            var suffix = $"Accent_{filter.name}";
            var remainderPath = $"{GeneratedMeshFolder}/PaintRemainder_{suffix}.asset";
            var paintPath = $"{GeneratedMeshFolder}/PaintDetail_{suffix}.asset";
            filter.sharedMesh = SaveGeneratedMesh(CreateFilteredMesh(source, fixedTriangles,
                $"PaintRemainder_{suffix}"), remainderPath);
            var paintMesh = SaveGeneratedMesh(CreateFilteredMesh(source, paintTriangles,
                $"PaintDetail_{suffix}"), paintPath);
            SetBundle(remainderPath);
            SetBundle(paintPath);

            var paintObject = new GameObject($"BattleBusPaint{suffix}") { layer = filter.gameObject.layer };
            paintObject.transform.SetParent(filter.transform.parent, false);
            paintObject.transform.localPosition = filter.transform.localPosition;
            paintObject.transform.localRotation = filter.transform.localRotation;
            paintObject.transform.localScale = filter.transform.localScale;
            paintObject.AddComponent<MeshFilter>().sharedMesh = paintMesh;
            var paintRenderer = paintObject.AddComponent<MeshRenderer>();
            paintRenderer.sharedMaterials = renderer.sharedMaterials;
            CopyRendererSettings(renderer, paintRenderer);
            if (filter.name == "Object_10")
            {
                if (selectedDoorFaces < 80)
                    throw new InvalidOperationException(
                        $"Only {selectedDoorFaces} front right door faces identified on Object_10.");
                var doorPath = $"{GeneratedMeshFolder}/PaintDetail_Door.asset";
                var doorMesh = SaveGeneratedMesh(CreateFilteredMesh(source, doorTriangles,
                    "PaintDetail_Door"), doorPath);
                SetBundle(doorPath);
                var doorObject = new GameObject("BattleBusPaintDoor") { layer = filter.gameObject.layer };
                doorObject.transform.SetParent(filter.transform.parent, false);
                doorObject.transform.localPosition = filter.transform.localPosition;
                doorObject.transform.localRotation = filter.transform.localRotation;
                doorObject.transform.localScale = filter.transform.localScale;
                doorObject.AddComponent<MeshFilter>().sharedMesh = doorMesh;
                var doorRenderer = doorObject.AddComponent<MeshRenderer>();
                doorRenderer.sharedMaterials = renderer.sharedMaterials;
                CopyRendererSettings(renderer, doorRenderer);
                Debug.Log($"Battle Bus front right door paint isolated: faces={selectedDoorFaces}, " +
                          $"bounds={doorRenderer.bounds}.");
            }
            Debug.Log($"Battle Bus isolated paint detail '{suffix}': faces={selectedFaces}, " +
                      $"bounds={paintRenderer.bounds}.");
        }
    }

    private static bool IsBlueAtlasPixel(Color32[] pixels, int width, int height, Vector2 uv)
    {
        var x = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * width), 0, width - 1);
        var y = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * height), 0, height - 1);
        var color = pixels[y * width + x];
        return color.b > 35 && color.b > color.r * 1.22f &&
               color.b > color.g * 1.10f;
    }

    private static bool IsBlueAtlasTriangle(Color32[] pixels, int width, int height,
        Vector2 a, Vector2 b, Vector2 c)
    {
        // Atlas colors can change within a large triangle. Sampling only its
        // centroid left sawtooth shaped source-blue islands on both fascias.
        return IsBlueAtlasPixel(pixels, width, height, (a + b + c) / 3f) ||
               IsBlueAtlasPixel(pixels, width, height, a) ||
               IsBlueAtlasPixel(pixels, width, height, b) ||
               IsBlueAtlasPixel(pixels, width, height, c) ||
               IsBlueAtlasPixel(pixels, width, height, (a + b) * 0.5f) ||
               IsBlueAtlasPixel(pixels, width, height, (b + c) * 0.5f) ||
               IsBlueAtlasPixel(pixels, width, height, (c + a) * 0.5f);
    }

    private static Mesh CreateFilteredMesh(Mesh source, List<int>[] triangles, string meshName)
    {
        var sourceVertices = source.vertices;
        var sourceNormals = source.normals;
        var sourceTangents = source.tangents;
        var sourceUv = source.uv;
        var sourceUv2 = source.uv2;
        var sourceColors = source.colors32;
        var used = new SortedSet<int>();
        foreach (var submesh in triangles)
            foreach (var vertex in submesh)
                used.Add(vertex);

        if (used.Count == 0)
            throw new InvalidOperationException($"Cannot create empty filtered mesh '{meshName}'.");

        var remap = new Dictionary<int, int>(used.Count);
        var vertices = new List<Vector3>(used.Count);
        var normals = new List<Vector3>(used.Count);
        var tangents = new List<Vector4>(used.Count);
        var uv = new List<Vector2>(used.Count);
        var uv2 = new List<Vector2>(used.Count);
        var colors = new List<Color32>(used.Count);
        foreach (var oldIndex in used)
        {
            remap.Add(oldIndex, vertices.Count);
            vertices.Add(sourceVertices[oldIndex]);
            if (sourceNormals.Length == source.vertexCount)
                normals.Add(sourceNormals[oldIndex]);
            if (sourceTangents.Length == source.vertexCount)
                tangents.Add(sourceTangents[oldIndex]);
            if (sourceUv.Length == source.vertexCount)
                uv.Add(sourceUv[oldIndex]);
            if (sourceUv2.Length == source.vertexCount)
                uv2.Add(sourceUv2[oldIndex]);
            if (sourceColors.Length == source.vertexCount)
                colors.Add(sourceColors[oldIndex]);
        }

        var mesh = new Mesh
        {
            name = meshName,
            indexFormat = used.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        mesh.SetVertices(vertices);
        if (normals.Count == vertices.Count)
            mesh.SetNormals(normals);
        else
            mesh.RecalculateNormals();
        if (tangents.Count == vertices.Count)
            mesh.SetTangents(tangents);
        if (uv.Count == vertices.Count)
            mesh.SetUVs(0, uv);
        if (uv2.Count == vertices.Count)
            mesh.SetUVs(1, uv2);
        if (colors.Count == vertices.Count)
            mesh.SetColors(colors);

        mesh.subMeshCount = triangles.Length;
        for (var submesh = 0; submesh < triangles.Length; submesh++)
        {
            var mappedTriangles = new List<int>(triangles[submesh].Count);
            foreach (var vertex in triangles[submesh])
                mappedTriangles.Add(remap[vertex]);
            mesh.SetTriangles(mappedTriangles, submesh, false);
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void CreateTireVisual(MeshComponent component, WheelGroups groups)
    {
        var group = groups.GetGroup(component);
        var wheelPosition = groups.GetPosition(group);
        var controllerName = groups.GetControllerName(group);
        var controller = FindTransform(component.Filter.transform.root, controllerName);
        // Donor transforms are looked up on the generated vehicle root below;
        // this fallback is replaced by the named controller established before save.
        if (controller == null)
            controller = FindTransform(component.Filter.transform, controllerName);
        if (controller == null)
            throw new InvalidOperationException($"Wheel controller '{controllerName}' is missing.");

        var meshPath = $"{GeneratedMeshFolder}/Tire_{Sanitize(controllerName)}_{component.TireIndex:D2}_{component.PartIndex:D2}.asset";
        var mesh = SaveGeneratedMesh(BuildComponentMesh(component, wheelPosition), meshPath);

        var visual = FindOrCreateWheelVisual(controller) ??
                     throw new InvalidOperationException(
                         $"Wheel controller '{controllerName}' has no configurable visual mount.");
        var assemblyName = $"BattleBusTire_{component.TireIndex:D2}";
        var tire = FindTransform(visual.CamberPivot, assemblyName)?.gameObject;
        var isNewAssembly = tire == null;
        if (isNewAssembly)
        {
            tire = new GameObject(assemblyName);
            tire!.transform.SetParent(visual.CamberPivot, false);
        }
        else
        {
            var part = new GameObject($"Part_{component.PartIndex:D2}");
            part.transform.SetParent(tire!.transform, false);
            tire = part;
        }
        // The generated mesh vertices are already expressed around the axle group's
        // center. Keep the child at the pivot origin so dual tires are not offset twice.
        tire.transform.localPosition = Vector3.zero;
        tire.transform.localRotation = Quaternion.identity;
        tire.transform.localScale = Vector3.one;
        var filter = tire.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = tire.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = component.Renderer.sharedMaterials;
        CopyRendererSettings(component.Renderer, renderer);
        if (isNewAssembly)
            visual.TireCount++;
    }

    private static Mesh BuildComponentMesh(MeshComponent component, Vector3 wheelPosition)
    {
        var source = component.SourceMesh ??
                     throw new InvalidOperationException("Source tire mesh disappeared during setup.");
        var sourceVertices = source.vertices;
        var sourceNormals = source.normals;
        var sourceTangents = source.tangents;
        var sourceUv = source.uv;
        var sourceUv2 = source.uv2;
        var sourceColors = source.colors;
        var used = new SortedSet<int>();
        foreach (var submesh in component.Triangles)
            foreach (var vertex in submesh)
                used.Add(vertex);

        var map = new Dictionary<int, int>();
        var vertices = new List<Vector3>(used.Count);
        var normals = new List<Vector3>(used.Count);
        var tangents = new List<Vector4>(used.Count);
        var uv = new List<Vector2>(used.Count);
        var uv2 = new List<Vector2>(used.Count);
        var colors = new List<Color>(used.Count);
        foreach (var oldIndex in used)
        {
            map.Add(oldIndex, vertices.Count);
            var rootPosition = component.VehicleRoot.InverseTransformPoint(
                component.Filter.transform.TransformPoint(sourceVertices[oldIndex]));
            // Correct each tire about its own axle before placing it inside a
            // dual-wheel visual. Rotating the inner tire about the shared pivot
            // moves its center off-axis and makes it wobble while spinning.
            vertices.Add(component.WheelRotationCorrection *
                         (rootPosition + component.PositionOffset - component.WheelAnchor) +
                         component.WheelAnchor - wheelPosition);
            if (sourceNormals.Length == source.vertexCount)
                normals.Add(component.WheelRotationCorrection *
                            component.VehicleRoot.InverseTransformDirection(
                                component.Filter.transform.TransformDirection(sourceNormals[oldIndex])));
            if (sourceTangents.Length == source.vertexCount)
            {
                var sourceTangent = sourceTangents[oldIndex];
                var tangent = component.WheelRotationCorrection *
                              component.VehicleRoot.InverseTransformDirection(
                                  component.Filter.transform.TransformDirection(
                                      new Vector3(sourceTangent.x, sourceTangent.y, sourceTangent.z)));
                tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, sourceTangent.w));
            }
            if (sourceUv.Length == source.vertexCount)
                uv.Add(sourceUv[oldIndex]);
            if (sourceUv2.Length == source.vertexCount)
                uv2.Add(sourceUv2[oldIndex]);
            if (sourceColors.Length == source.vertexCount)
                colors.Add(sourceColors[oldIndex]);
        }

        var mesh = new Mesh
        {
            name = $"BattleBusTire_{component.TireIndex:D2}",
            indexFormat = used.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        mesh.SetVertices(vertices);
        if (normals.Count == vertices.Count)
            mesh.SetNormals(normals);
        else
            mesh.RecalculateNormals();
        if (tangents.Count == vertices.Count)
            mesh.SetTangents(tangents);
        if (uv.Count == vertices.Count)
            mesh.SetUVs(0, uv);
        if (uv2.Count == vertices.Count)
            mesh.SetUVs(1, uv2);
        if (colors.Count == vertices.Count)
            mesh.SetColors(colors);
        mesh.subMeshCount = component.Triangles.Length;
        for (var submesh = 0; submesh < component.Triangles.Length; submesh++)
        {
            var remapped = component.Triangles[submesh].Select(index => map[index]).ToList();
            mesh.SetTriangles(remapped, submesh, false);
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void ConfigurePhysics(GameObject root, WheelGroups groups, Bounds chassisBounds,
        Bounds allBounds)
    {
        var body = root.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Audi donor has no Rigidbody.");
        body.mass = TargetVehicleMass;
        body.drag = 0.03f;
        body.angularDrag = 1.15f;
        var axleCenterX = (groups.FrontLeftPosition.x + groups.FrontRightPosition.x +
                           groups.RearLeftPosition.x + groups.RearRightPosition.x) * 0.25f;
        body.centerOfMass = new Vector3(axleCenterX, 0.36f,
            chassisBounds.center.z);
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        var dynamics = FindComponentBySerializedProperty(root, "baseMass") ??
                       throw new InvalidOperationException("NWH VehicleController mass properties were not found.");
        var serialized = new SerializedObject(dynamics);
        var dimensions = allBounds.size;
        var inertia = new Vector3(
            TargetVehicleMass * (dimensions.y * dimensions.y + dimensions.z * dimensions.z) / 12f,
            TargetVehicleMass * (dimensions.x * dimensions.x + dimensions.z * dimensions.z) / 12f,
            TargetVehicleMass * (dimensions.x * dimensions.x + dimensions.y * dimensions.y) / 12f);
        SetBool(serialized, "useDefaultMass", false);
        SetNumber(serialized, "baseMass", TargetVehicleMass);
        SetNumber(serialized, "combinedMass", TargetVehicleMass);
        SetVector3(serialized, "dimensions", dimensions);
        SetBool(serialized, "useDefaultCenterOfMass", false);
        SetVector3(serialized, "centerOfMass", body.centerOfMass);
        SetVector3(serialized, "combinedCenterOfMass", body.centerOfMass);
        SetBool(serialized, "useDefaultInertia", false);
        SetVector3(serialized, "inertiaTensor", inertia);
        SetVector3(serialized, "combinedInertiaTensor", inertia);
        var wheelGroups = serialized.FindProperty("wheelGroups");
        if (wheelGroups != null && wheelGroups.isArray)
            for (var index = 0; index < wheelGroups.arraySize; index++)
                SetNumberOnProperty(wheelGroups.GetArrayElementAtIndex(index)
                    .FindPropertyRelative("antiRollBarForce"), TargetAntiRollBarForce);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        body.inertiaTensor = inertia;
        Debug.Log($"Battle Bus mass and roll stability configured: mass={TargetVehicleMass:F0}kg, " +
                  $"centerOfMass={body.centerOfMass}, dimensions={dimensions}, inertia={inertia}, " +
                  $"antiRoll={TargetAntiRollBarForce:F0}, angularDrag={body.angularDrag:F2}.");
    }

    private static void ConfigureWheelControllers(GameObject root, WheelGroups groups)
    {
        SetWheelController(root, "FrontLeft_WheelController", groups.FrontLeftPosition,
            groups.TireSize);
        SetWheelController(root, "FrontRight_WheelController", groups.FrontRightPosition,
            groups.TireSize);
        SetWheelController(root, "RearLeft_WheelController", groups.RearLeftPosition,
            groups.RearTireSize);
        SetWheelController(root, "RearRight_WheelController", groups.RearRightPosition,
            groups.RearTireSize);
    }

    private static void SetWheelController(GameObject root, string name, Vector3 position, Vector3 tireSize)
    {
        var transform = FindTransform(root.transform, name) ??
                        throw new InvalidOperationException($"Audi wheel controller '{name}' is missing.");
        transform.position = root.transform.TransformPoint(position);
        transform.rotation = root.transform.rotation;
        foreach (var component in transform.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var wheel = serialized.FindProperty("wheel");
            var visual = wheel?.FindPropertyRelative("visual");
            if (visual?.propertyType != SerializedPropertyType.ObjectReference)
                continue;
            var radius = wheel!.FindPropertyRelative("radius");
            var width = wheel.FindPropertyRelative("width");
            if (radius != null && radius.propertyType == SerializedPropertyType.Float)
                radius.floatValue = Mathf.Max(0.18f, tireSize.y * 0.5f);
            if (width != null && width.propertyType == SerializedPropertyType.Float)
                width.floatValue = Mathf.Max(0.14f, tireSize.x);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void ConfigureVehicleSteering(GameObject root)
    {
        var component = FindComponentBySerializedProperty(root, "steering.maximumSteerAngle") ??
                        throw new InvalidOperationException("NWH vehicle steering settings were not found on the Battle Bus.");
        var serialized = new SerializedObject(component);
        SetNumber(serialized, "brakes.maxTorque", 8400f);
        SetNumber(serialized, "steering.maximumSteerAngle", FrontMaximumSteerAngle);
        SetNumber(serialized, "steering.degreesPerSecondLimit", FrontSteeringDegreesPerSecond);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"Battle Bus front steering configured: maximumSteerAngle={FrontMaximumSteerAngle:F1}, " +
                  $"degreesPerSecondLimit={FrontSteeringDegreesPerSecond:F1}.");
    }

    private static void ConfigureVehicleDeformation(GameObject root, Renderer[] bodyRenderers)
    {
        var filters = bodyRenderers.Where(renderer =>
                renderer.name == PaintableHullRendererName ||
                renderer.name == "BattleBusPaintDoor" ||
                renderer.name == "BattleBusPaintSingleSideDoor" ||
                renderer.name.StartsWith("BattleBusPaintAccent_", StringComparison.Ordinal) ||
                renderer.name == "Object_10" || renderer.name == "Object_12" ||
                renderer.name == "Object_16" || renderer.name == "Object_18" ||
                renderer.name == "Object_20" || renderer.name.StartsWith("BattleBusLamp_", StringComparison.Ordinal))
            .Select(renderer => renderer.GetComponent<MeshFilter>())
            .Where(filter => filter != null && filter.sharedMesh != null)
            .Cast<MeshFilter>().ToArray();
        if (filters.Length != 17)
            throw new InvalidOperationException(
                $"Expected seventeen Battle Bus body, door, trim, lamp and bumper meshes for deformation, found {filters.Length}.");

        var controller = root.GetComponentsInChildren<MonoBehaviour>(true)
            .SingleOrDefault(component => component != null &&
                component.GetType().Name == "VehicleDeformationController") ??
            throw new InvalidOperationException("Battle Bus donor deformation controller is missing.");
        var serialized = new SerializedObject(controller);
        var meshFilters = serialized.FindProperty("meshFilters");
        if (meshFilters == null || !meshFilters.isArray)
            throw new InvalidOperationException("Battle Bus deformation mesh list is missing.");
        meshFilters.arraySize = filters.Length;
        for (var index = 0; index < filters.Length; index++)
            meshFilters.GetArrayElementAtIndex(index).objectReferenceValue = filters[index];
        var originals = serialized.FindProperty("originalMeshes");
        if (originals != null && originals.isArray)
            originals.ClearArray();
        SetNumber(serialized, "deformationStrength", 0.18f);
        SetNumber(serialized, "deformationRadius", 0.55f);
        SetNumber(serialized, "deformationRandomness", 0.008f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        controller.enabled = false;
        var visualDamage = root.GetComponent<BattleBusVisualDamageController>() ??
                           root.AddComponent<BattleBusVisualDamageController>();
        var visualDamageSerialized = new SerializedObject(visualDamage);
        SetObjectArray(visualDamageSerialized, "bodyMeshes", filters);
        visualDamageSerialized.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log("Battle Bus collision deformation bound to body, doors, trim, lamps and bumpers: " +
                  string.Join(", ", filters.Select(filter => filter.name)));
    }

    private static void ConfigureBodyCollider(GameObject root, Bounds chassisBounds)
    {
        var holder = FindTransform(root.transform, "BodyCollider") ??
                     throw new InvalidOperationException("Audi donor BodyCollider transform is missing.");
        var boxes = holder.GetComponents<BoxCollider>();
        if (boxes.Length == 0)
            throw new InvalidOperationException("Audi donor has no chassis BoxCollider.");

        // Decorative lower parts extend below the road after the visual body is
        // lowered. Keep the physical box above the tire contact patches so it
        // cannot prop up the bus or block the driven wheels and reverse gear.
        var fittedMin = chassisBounds.min;
        fittedMin.y = Mathf.Max(fittedMin.y, 0.12f);
        var fittedMax = chassisBounds.max;
        if (fittedMax.y <= fittedMin.y)
            throw new InvalidOperationException("Battle Bus chassis collider has no ground clearance.");
        var fittedBounds = new Bounds((fittedMin + fittedMax) * 0.5f, fittedMax - fittedMin);
        var center = holder.InverseTransformPoint(root.transform.TransformPoint(fittedBounds.center));
        var rootScale = root.transform.lossyScale;
        var holderScale = holder.lossyScale;
        var size = new Vector3(
            chassisBounds.size.x * rootScale.x / Mathf.Max(0.001f, holderScale.x),
            fittedBounds.size.y * rootScale.y / Mathf.Max(0.001f, holderScale.y),
            chassisBounds.size.z * rootScale.z / Mathf.Max(0.001f, holderScale.z));
        boxes[0].center = center;
        boxes[0].size = size;
        for (var index = 1; index < boxes.Length; index++)
            boxes[index].enabled = false;
        Debug.Log($"Battle Bus body collider fitted above road: bottom={fittedMin.y:F2}m, " +
                  $"top={fittedMax.y:F2}m, center={boxes[0].center}, size={boxes[0].size}.");
    }

    private static BoxCollider ConfigureServiceBounds(GameObject root, Bounds allBounds)
    {
        var go = new GameObject("GasStationServiceBounds") { layer = 2 };
        go.transform.SetParent(root.transform, false);
        var collider = go.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        // Use the chassis footprint for interaction; the decorative balloon must
        // not widen the lower vehicle selection/service volume.
        collider.center = allBounds.center;
        collider.size = allBounds.size + new Vector3(0.2f, 0.2f, 0.3f);
        return collider;
    }

    private static void ConfigureEntryAnchors(GameObject root, WheelGroups groups, Bounds chassisBounds)
    {
        var leftTrack = Mathf.Abs(groups.FrontLeftPosition.x - groups.FrontRightPosition.x) * 0.5f;
        var doorX = -(leftTrack + 0.35f);
        var frontZ = Mathf.Max(groups.FrontLeftPosition.z, groups.FrontRightPosition.z);
        SetOrCreatePosition(root, "Driverside", new Vector3(doorX, 0.1f, frontZ - 0.4f));
        SetOrCreatePosition(root, "Passengerside", new Vector3(-doorX, 0.1f, frontZ - 0.4f));
        SetOrCreatePosition(root, "LoadingPosition", new Vector3(doorX, 0.1f, frontZ - 0.4f));
        SetOrCreatePosition(root, "RefuelingPosition", new Vector3(-doorX, 0.9f,
            groups.RearRightPosition.z - 0.35f));

        var seat = FindTransform(root.transform, "BattleBusDriverSeat") ??
                   new GameObject("BattleBusDriverSeat").transform;
        seat.SetParent(root.transform, false);
        seat.localPosition = new Vector3(groups.FrontLeftPosition.x * 0.42f,
            Mathf.Max(chassisBounds.center.y + 0.15f, 1.1f), frontZ - 0.65f);
        seat.localRotation = Quaternion.identity;
    }

    private static void ConfigureVehicleReferences(GameObject root, UnityEngine.Object vehicleType,
        BoxCollider serviceCollider, Renderer[] vehicleRenderers, Renderer[] paintRenderers, Bounds allBounds)
    {
        var vehicle = FindComponentBySerializedProperty(root, "vehicleCollider") ??
                      throw new InvalidOperationException("Vehicle component with vehicleCollider is missing from Audi donor prefab.");
        var serializedController = new SerializedObject(vehicle);
        var serviceProperty = serializedController.FindProperty("vehicleCollider");
        if (serviceProperty?.propertyType != SerializedPropertyType.ObjectReference)
            throw new InvalidOperationException("Vehicle.vehicleCollider property is missing.");
        serviceProperty.objectReferenceValue = serviceCollider;
        var fuelPosition = serializedController.FindProperty("vehicleRefuelingPosition") ??
                           serializedController.FindProperty("refuelingPosition") ??
                           serializedController.FindProperty("refuelingTransform") ??
                           serializedController.FindProperty("fuelingPosition");
        var refuel = FindTransform(root.transform, "RefuelingPosition");
        if (fuelPosition?.propertyType == SerializedPropertyType.ObjectReference)
            fuelPosition.objectReferenceValue = refuel;
        var vehicleTypeProperty = serializedController.FindProperty("vehicleType");
        if (vehicleTypeProperty?.propertyType == SerializedPropertyType.ObjectReference)
            vehicleTypeProperty.objectReferenceValue = vehicleType;
        var instance = serializedController.FindProperty("vehicleInstance");
        var typeName = instance?.FindPropertyRelative("vehicleTypeName");
        if (typeName?.propertyType == SerializedPropertyType.String)
            typeName.stringValue = VehicleTypeName;

        var renderers = serializedController.FindProperty("renderers");
        if (renderers == null || !renderers.isArray)
            throw new InvalidOperationException("Vehicle renderer list is missing from the donor prefab.");
        SetObjectArray(serializedController, "renderers", vehicleRenderers.Cast<UnityEngine.Object>().ToArray());

        var obstacleProperty = serializedController.FindProperty("navMeshObstacle");
        if (obstacleProperty?.propertyType != SerializedPropertyType.ObjectReference ||
            obstacleProperty.objectReferenceValue is not NavMeshObstacle navMeshObstacle)
            throw new InvalidOperationException("Vehicle NavMeshObstacle is missing from the donor prefab.");
        ConfigureNavMeshObstacle(root, navMeshObstacle, allBounds);
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        var carFeatures = FindComponentBySerializedProperty(root, "bodyMeshes") ??
                          throw new InvalidOperationException("CarFeatures.bodyMeshes is missing from the donor prefab.");
        var serializedFeatures = new SerializedObject(carFeatures);
        var bodyMeshes = serializedFeatures.FindProperty("bodyMeshes");
        if (bodyMeshes == null || !bodyMeshes.isArray)
            throw new InvalidOperationException("CarFeatures.bodyMeshes is not a renderer array.");
        SetObjectArray(serializedFeatures, "bodyMeshes", paintRenderers.Cast<UnityEngine.Object>().ToArray());
        serializedFeatures.ApplyModifiedPropertiesWithoutUndo();
    }

    private static bool HasRenderableMesh(Renderer renderer)
    {
        if (renderer == null || renderer is not MeshRenderer || !renderer.enabled ||
            renderer.sharedMaterials.Length == 0)
            return false;
        var meshFilter = renderer.GetComponent<MeshFilter>();
        if (meshFilter == null)
            return false;
        var mesh = meshFilter.sharedMesh;
        if (mesh == null || mesh.vertexCount == 0)
            return false;
        for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
            if (mesh.GetIndexCount(submesh) > 0)
                return true;
        return false;
    }

    private static void LogModelMaterialAudit(IEnumerable<Renderer> renderers)
    {
        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null)
                    continue;
                var baseColorTexture = GetMaterialTextureName(material,
                    "_BaseColorMap", "baseColorTexture", "_MainTex");
                var baseColor = GetMaterialColor(material,
                    "_BaseColor", "baseColorFactor", "_Color");
                var baseTexture = GetMaterialBaseColorTexture(material);
                var paintProperties = $"_BaseColor:{material.HasProperty("_BaseColor")}, " +
                                      $"_Color:{material.HasProperty("_Color")}, " +
                                      $"baseColorFactor:{material.HasProperty("baseColorFactor")}";
                Debug.Log($"Battle Bus material audit: renderer='{renderer.name}', slot={index}, " +
                          $"material='{material.name}', shader='{material.shader?.name ?? "missing"}', " +
                          $"paintProperties=({paintProperties}), baseColor={baseColor}, " +
                          $"baseTexture='{baseColorTexture}', bounds={renderer.bounds}, " +
                          $"readable={baseTexture is Texture2D texture && texture.isReadable}.");
            }
        }
    }

    private static string GetMaterialTextureName(Material material, params string[] properties)
    {
        foreach (var property in properties)
            if (material.HasProperty(property) && material.GetTexture(property) != null)
                return material.GetTexture(property)!.name;
        return "none";
    }

    private static Color GetMaterialColor(Material material, params string[] properties)
    {
        foreach (var property in properties)
            if (material.HasProperty(property))
                return material.GetColor(property);
        return Color.white;
    }

    private static void NormalizeImportedMaterials(Renderer[] renderers)
    {
        EnsureAssetFolder(GeneratedMaterialFolder);
        var convertedMaterials = new Dictionary<Material, Material>();
        var convertedSlots = 0;
        foreach (var renderer in renderers)
        {
            var materials = renderer.sharedMaterials;
            var changed = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (source == null || source.shader == null ||
                    !string.Equals(source.shader.name, "glTF/PbrMetallicRoughness", StringComparison.Ordinal))
                    continue;

                if (!convertedMaterials.TryGetValue(source, out var supported))
                {
                    var materialPath = $"{GeneratedMaterialFolder}/BattleBusMaterial_{Sanitize(source.name)}.mat";
                    supported = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (supported == null)
                    {
                        supported = new Material(source) { name = source.name };
                        AssetDatabase.CreateAsset(supported, materialPath);
                    }
                    else
                    {
                        EditorUtility.CopySerialized(source, supported);
                        supported.name = source.name;
                    }

                    var texture = GetMaterialBaseColorTexture(source, out var textureProperty);
                    if (string.Equals(renderer.name, "Object_18", StringComparison.Ordinal))
                        ConfigureHdrpTransparentMaterial(supported, source, texture, textureProperty,
                            0.72f, false);
                    else if (string.Equals(renderer.name, "Object_26", StringComparison.Ordinal))
                    {
                        var sourceAlpha = GetMaterialColor(source,
                            "baseColorFactor", "_BaseColor", "_Color").a;
                        ConfigureHdrpTransparentMaterial(supported, source, texture, textureProperty,
                            Mathf.Clamp01(sourceAlpha), false);
                    }
                    else
                        ConfigureHdrpPaintMaterial(supported, source, texture, textureProperty);

                    SetBundle(materialPath);
                    EditorUtility.SetDirty(supported);
                    convertedMaterials.Add(source, supported);
                }

                materials[index] = supported;
                changed = true;
                convertedSlots++;
            }

            if (changed)
                renderer.sharedMaterials = materials;
        }

        Debug.Log($"Battle Bus material normalization: converted {convertedSlots} imported glTF slots " +
                  $"across {convertedMaterials.Count} source materials to HDRP/Lit, with explicit glass and burner handling.");
    }

    private static void ConfigureHdrpTransparentMaterial(Material material, Material source,
        Texture? baseColorTexture, string sourceTextureProperty, float alpha, bool isFlame)
    {
        var shader = Shader.Find("HDRP/Lit") ??
                     Shader.Find("High Definition Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("HDRP/Lit shader was not available for Battle Bus transparent materials.");

        var baseColor = GetMaterialColor(source, "baseColorFactor", "_BaseColor", "_Color");
        baseColor.a = alpha;
        var textureScale = !string.IsNullOrEmpty(sourceTextureProperty)
            ? source.GetTextureScale(sourceTextureProperty)
            : Vector2.one;
        var textureOffset = !string.IsNullOrEmpty(sourceTextureProperty)
            ? source.GetTextureOffset(sourceTextureProperty)
            : Vector2.zero;
        var normalTextureProperty = source.HasProperty("normalTexture") ? "normalTexture" :
            source.HasProperty("_NormalMap") ? "_NormalMap" : string.Empty;
        var normalTexture = !string.IsNullOrEmpty(normalTextureProperty)
            ? source.GetTexture(normalTextureProperty)
            : null;
        var normalScale = source.HasProperty("normalTexture_scale")
            ? source.GetFloat("normalTexture_scale")
            : source.HasProperty("_NormalScale") ? source.GetFloat("_NormalScale") : 1f;
        var metallic = source.HasProperty("metallicFactor")
            ? source.GetFloat("metallicFactor")
            : source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0f;
        var roughness = source.HasProperty("roughnessFactor")
            ? source.GetFloat("roughnessFactor")
            : 0.5f;

        material.shader = shader;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_BaseColorMap"))
        {
            material.SetTexture("_BaseColorMap", baseColorTexture);
            material.SetTextureScale("_BaseColorMap", textureScale);
            material.SetTextureOffset("_BaseColorMap", textureOffset);
        }
        if (material.HasProperty("_NormalMap"))
        {
            material.SetTexture("_NormalMap", normalTexture);
            material.SetFloat("_NormalScale", normalScale);
        }
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(roughness));
        SetMaterialFloat(material, "_SurfaceType", 1f);
        SetMaterialFloat(material, "_BlendMode", 0f);
        SetMaterialFloat(material, "_AlphaCutoffEnable", 0f);
        SetMaterialFloat(material, "_DoubleSidedEnable", 1f);
        SetMaterialFloat(material, "_CullMode", (float)CullMode.Off);
        SetMaterialFloat(material, "_SupportDecals", 0f);
        SetMaterialFloat(material, "_ReceivesSSR", 0f);
        SetMaterialFloat(material, "_ReceivesSSRTransparent", 0f);
        SetMaterialFloat(material, "_RefractionModel", 0f);
        SetMaterialFloat(material, "_ZWrite", 0f);
        SetMaterialFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
        SetMaterialFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetOverrideTag("RenderType", "Transparent");

        var validated = TryValidateHdrpMaterial(material);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_BLENDMODE_ALPHA");
        material.EnableKeyword("_DISABLE_DECALS");
        material.DisableKeyword("_ALPHATEST_ON");
        SetMaterialFloat(material, "_SurfaceType", 1f);
        SetMaterialFloat(material, "_ZWrite", 0f);
        SetMaterialFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
        SetMaterialFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (isFlame)
        {
            var emission = new Color(1f, 0.34f, 0.035f, 1f);
            if (material.HasProperty("_EmissiveColor"))
                material.SetColor("_EmissiveColor", emission);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", emission);
            SetMaterialFloat(material, "_EmissiveIntensity", 4f);
            material.EnableKeyword("_EMISSION");
        }

        if (!validated)
            Debug.LogWarning($"Battle Bus transparent material '{source.name}' was configured without HDRP " +
                             "material validation; inspect glass and burner rendering in the game build.");
    }

    private static Renderer[] ConfigurePaintMaterials(Renderer[] bodyRenderers)
    {
        EnsureAssetFolder(GeneratedMaterialFolder);
        var paintRenderers = new List<Renderer>();
        var convertedMaterials = new Dictionary<Material, Material>();
        var hullRenderers = bodyRenderers.Where(renderer =>
        {
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            return string.Equals(renderer.name, PaintableHullRendererName, StringComparison.Ordinal) &&
                   mesh != null && mesh.name.StartsWith(PaintableHullMeshPrefix, StringComparison.Ordinal);
        }).ToArray();
        if (hullRenderers.Length != 1)
            throw new InvalidOperationException(
                $"Expected one separated Battle Bus hull to paint, found {hullRenderers.Length}.");

        foreach (var renderer in bodyRenderers)
        {
            var materials = renderer.sharedMaterials;
            var rendererHasPaint = false;
            for (var index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                var isHull = ReferenceEquals(renderer, hullRenderers[0]);
                var isDoor = renderer.name == "BattleBusPaintDoor" ||
                             renderer.name == "BattleBusPaintSingleSideDoor";
                var isAccent = renderer.name.StartsWith("BattleBusPaintAccent_", StringComparison.Ordinal);
                var isCap = renderer.name == "BattleBusPaintBalloonCap";
                if (source == null || !(isHull || isDoor || isAccent || isCap) ||
                    !((isAccent || isDoor) ? IsBattleBusPaintMaterialCandidate(source) :
                        IsBattleBusHullPaintMaterial(source)))
                    continue;

                if (isCap)
                    materials[index] = CreateCapPaintMaterial(source);
                else if (isDoor)
                    materials[index] = CreateTintablePaintMaterial(source, true);
                else if (!convertedMaterials.TryGetValue(source, out var paintMaterial))
                {
                    paintMaterial = CreateTintablePaintMaterial(source);
                    convertedMaterials.Add(source, paintMaterial);
                    materials[index] = paintMaterial;
                }
                else
                    materials[index] = paintMaterial;
                rendererHasPaint = true;
            }

            if (!rendererHasPaint)
            {
                if (renderer.sharedMaterials.Any(material => material != null &&
                        IsBattleBusPaintMaterialCandidate(material)))
                    Debug.Log($"Battle Bus paint excluded non-hull renderer='{renderer.name}' " +
                              $"mesh='{renderer.GetComponent<MeshFilter>()?.sharedMesh?.name ?? "missing"}'.");
                continue;
            }
            renderer.sharedMaterials = materials;
            paintRenderers.Add(renderer);
        }

        if (paintRenderers.Count != 6)
            throw new InvalidOperationException(
                $"Battle Bus paint setup expected hull, balloon cap, two accents and two doors; found {paintRenderers.Count}.");
        Debug.Log("Battle Bus paint setup: hull, balloon cap, both doors and isolated blue details use per-vehicle paint.");
        return paintRenderers.ToArray();
    }

    private static bool IsBattleBusPaintMaterialCandidate(Material material) =>
        material.name.IndexOf("Material__25", StringComparison.OrdinalIgnoreCase) >= 0 ||
        material.name.IndexOf("Material__26", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsBattleBusHullPaintMaterial(Material material) =>
        material.name.IndexOf("Material__25", StringComparison.OrdinalIgnoreCase) >= 0;

    private static Material CreateTintablePaintMaterial(Material source, bool neutralizeWarmDoor = false)
    {
        var assetName = $"BattleBusPaint_{Sanitize(source.name)}" +
                        (neutralizeWarmDoor ? "_Door" : string.Empty);
        var materialPath = $"{GeneratedMaterialFolder}/{assetName}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(source) { name = assetName };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, material);
            material.name = assetName;
        }

        var sourceTexture = GetMaterialBaseColorTexture(source, out var sourceTextureProperty);
        var neutralTexture = sourceTexture is Texture2D sourceTexture2D
            ? CreateTintablePaintTexture(sourceTexture2D, assetName, neutralizeWarmDoor)
            : null;
        ConfigureHdrpPaintMaterial(material, source, neutralTexture ?? sourceTexture,
            sourceTextureProperty);
        SetBundle(materialPath);
        EditorUtility.SetDirty(material);
        Debug.Log($"Battle Bus paint material prepared: '{source.name}' -> '{assetName}', " +
                  $"texture='{(neutralTexture != null ? neutralTexture.name : sourceTexture?.name ?? "none")}', " +
                  $"shader='{material.shader?.name ?? "missing"}'.");
        return material;
    }

    private static Texture2D? CreateTintablePaintTexture(Texture2D source, string materialName,
        bool neutralizeWarmDoor)
    {
        if (!TryReadEmbeddedTexturePixels(source, out var pixels, out var width, out var height))
        {
            Debug.LogWarning($"Battle Bus paint texture '{source.name}' could not be decoded from the " +
                             "source GLB; the HDRP color property will still be enabled, but its blue " +
                             "base pixels could not be neutralized.");
            return null;
        }

        var neutralizedPixels = 0;
        for (var index = 0; index < pixels.Length; index++)
        {
            var color = pixels[index];
            if (color.a != 255)
            {
                color.a = 255;
                pixels[index] = color;
            }
            var isBlue = color.b >= 28 && color.b > color.r * 1.20f &&
                         color.b > color.g * 1.10f;
            var isWarmDoor = neutralizeWarmDoor && color.r > 95 &&
                             color.r > color.g * 1.25f && color.g > color.b * 1.20f;
            if (!isBlue && !isWarmDoor)
                continue;

            var maximum = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
            var average = (color.r + color.g + color.b) / 3f;
            var neutral = (byte)Mathf.Clamp(Mathf.RoundToInt(maximum * 0.82f + average * 0.18f), 0, 255);
            pixels[index] = new Color32(neutral, neutral, neutral, color.a);
            neutralizedPixels++;
        }

        if (neutralizedPixels == 0)
        {
            Debug.LogWarning($"Battle Bus paint texture '{source.name}' contains no blue pixels to neutralize.");
            return null;
        }

        var assetName = $"{materialName}_{Sanitize(source.name)}_Tintable";
        var assetPath = $"{GeneratedMaterialFolder}/{assetName}.asset";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        var generatedTexture = new Texture2D(width, height, TextureFormat.RGBA32,
            source.mipmapCount > 1, false)
        {
            name = assetName,
            wrapMode = source.wrapMode,
            filterMode = source.filterMode,
            anisoLevel = source.anisoLevel,
        };
        generatedTexture.SetPixels32(pixels);
        generatedTexture.Apply(source.mipmapCount > 1, false);
        if (texture == null)
        {
            texture = generatedTexture;
            AssetDatabase.CreateAsset(texture, assetPath);
        }
        else
        {
            EditorUtility.CopySerialized(generatedTexture, texture);
            texture.name = assetName;
            UnityEngine.Object.DestroyImmediate(generatedTexture);
            EditorUtility.SetDirty(texture);
        }
        SetBundle(assetPath);
        Debug.Log($"Battle Bus paint texture prepared: source='{source.name}', size={width}x{height}, " +
                  $"bluePixelsNeutralized={neutralizedPixels}.");
        return texture;
    }

    private static bool TryReadEmbeddedTexturePixels(Texture2D source,
        out Color32[] pixels, out int width, out int height)
    {
        pixels = Array.Empty<Color32>();
        width = 0;
        height = 0;
        if (!source.name.StartsWith("image_", StringComparison.Ordinal) ||
            !int.TryParse(source.name.Substring("image_".Length), out var imageIndex))
            return false;

        byte[] modelBytes;
        try
        {
            modelBytes = File.ReadAllBytes(ModelPath);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Battle Bus could not read the source GLB for paint texture '{source.name}': " +
                             $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }

        if (modelBytes.Length < 20)
            return false;
        var jsonLength = BitConverter.ToInt32(modelBytes, 12);
        if (jsonLength <= 0 || jsonLength > modelBytes.Length - 20)
            return false;

        GlbDocument? document;
        try
        {
            var json = Encoding.UTF8.GetString(modelBytes, 20, jsonLength).TrimEnd('\0', ' ', '\r', '\n');
            document = JsonUtility.FromJson<GlbDocument>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Battle Bus could not parse the source GLB for paint texture '{source.name}': " +
                             $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }

        if (document?.images == null || imageIndex < 0 || imageIndex >= document.images.Length ||
            document.bufferViews == null)
            return false;
        var image = document.images[imageIndex];
        if (image.bufferView < 0 || image.bufferView >= document.bufferViews.Length)
            return false;
        var view = document.bufferViews[image.bufferView];
        var binOffset = 20 + jsonLength;
        if (binOffset + 8 > modelBytes.Length)
            return false;
        var binLength = BitConverter.ToInt32(modelBytes, binOffset);
        var dataOffset = binOffset + 8 + view.byteOffset;
        if (view.byteLength <= 0 || dataOffset < 0 || dataOffset + view.byteLength > modelBytes.Length ||
            view.byteOffset + view.byteLength > binLength)
            return false;

        var encoded = new byte[view.byteLength];
        Buffer.BlockCopy(modelBytes, dataOffset, encoded, 0, encoded.Length);
        var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        try
        {
            if (!ImageConversion.LoadImage(decoded, encoded, false))
                return false;
            width = decoded.width;
            height = decoded.height;
            pixels = decoded.GetPixels32();
            return pixels.Length == width * height;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Battle Bus could not decode embedded paint texture '{source.name}': " +
                             $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(decoded);
        }
    }

    private static void ConfigureHdrpPaintMaterial(Material material, Material source,
        Texture? baseColorTexture, string sourceTextureProperty)
    {
        var shader = Shader.Find("HDRP/Lit") ??
                     Shader.Find("High Definition Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("HDRP/Lit shader was not available for Battle Bus paint.");

        var baseColor = GetMaterialColor(source, "baseColorFactor", "_BaseColor", "_Color");
        baseColor.a = 1f;
        var textureScale = !string.IsNullOrEmpty(sourceTextureProperty)
            ? source.GetTextureScale(sourceTextureProperty)
            : Vector2.one;
        var textureOffset = !string.IsNullOrEmpty(sourceTextureProperty)
            ? source.GetTextureOffset(sourceTextureProperty)
            : Vector2.zero;
        var normalTextureProperty = source.HasProperty("normalTexture") ? "normalTexture" :
            source.HasProperty("_NormalMap") ? "_NormalMap" : string.Empty;
        var normalTexture = !string.IsNullOrEmpty(normalTextureProperty)
            ? source.GetTexture(normalTextureProperty)
            : null;
        var normalScale = source.HasProperty("normalTexture_scale")
            ? source.GetFloat("normalTexture_scale")
            : source.HasProperty("_NormalScale") ? source.GetFloat("_NormalScale") : 1f;
        var metallic = source.HasProperty("metallicFactor")
            ? source.GetFloat("metallicFactor")
            : source.HasProperty("_Metallic") ? source.GetFloat("_Metallic") : 0f;
        var roughness = source.HasProperty("roughnessFactor")
            ? source.GetFloat("roughnessFactor")
            : 0.5f;

        material.shader = shader;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_BaseColorMap"))
        {
            material.SetTexture("_BaseColorMap", baseColorTexture);
            material.SetTextureScale("_BaseColorMap", textureScale);
            material.SetTextureOffset("_BaseColorMap", textureOffset);
        }
        if (material.HasProperty("_NormalMap"))
        {
            material.SetTexture("_NormalMap", normalTexture);
            material.SetFloat("_NormalScale", normalScale);
        }
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(roughness));
        if (material.HasProperty("_SurfaceType"))
            material.SetFloat("_SurfaceType", 0f);
        if (material.HasProperty("_AlphaCutoffEnable"))
            material.SetFloat("_AlphaCutoffEnable", 0f);
        // The glTF body and entry door contain outward faces with opposite
        // winding. Culling either side opens a visible hole beside the steps.
        SetMaterialFloat(material, "_DoubleSidedEnable", 1f);
        SetMaterialFloat(material, "_CullMode", (float)CullMode.Off);
        SetMaterialFloat(material, "_CullModeForward", (float)CullMode.Off);
        SetMaterialFloat(material, "_SupportDecals", 0f);
        SetMaterialFloat(material, "_ReceivesSSR", 0f);
        SetMaterialFloat(material, "_ReceivesSSRTransparent", 0f);
        SetMaterialFloat(material, "_RefractionModel", 0f);
        material.renderQueue = (int)RenderQueue.Geometry;
        material.SetOverrideTag("RenderType", "Opaque");

        var validated = TryValidateHdrpMaterial(material);
        SetMaterialFloat(material, "_DoubleSidedEnable", 1f);
        SetMaterialFloat(material, "_CullMode", (float)CullMode.Off);
        SetMaterialFloat(material, "_CullModeForward", (float)CullMode.Off);
        material.EnableKeyword("_DISABLE_DECALS");
        material.EnableKeyword("_DISABLE_SSR");
        material.EnableKeyword("_DISABLE_SSR_TRANSPARENT");
        SetMaterialFloat(material, "_ZWrite", 1f);
        SetMaterialFloat(material, "_SrcBlend", (float)BlendMode.One);
        SetMaterialFloat(material, "_DstBlend", (float)BlendMode.Zero);
        if (!validated)
            Debug.LogWarning("Battle Bus HDRP paint material passed property setup without HDRP " +
                             "material validation; check shader compatibility in the game build.");
    }

    private static bool TryValidateHdrpMaterial(Material material)
    {
        try
        {
            var method = ResolveMaterialValidationMethod(
                HdrpMaterialTypeName, "ValidateMaterial",
                ref HdrpValidateMaterialMethod, ref HdrpValidateMaterialMethodResolved);
            if (method != null)
            {
                var result = method.Invoke(null, new object[] { material });
                if (result is not bool validated || validated)
                    return true;
            }

            method = ResolveMaterialValidationMethod(
                ShaderGraphApiTypeName, "ValidateLightingMaterial",
                ref ShaderGraphValidateMaterialMethod, ref ShaderGraphValidateMaterialMethodResolved);
            if (method == null)
                return false;
            method.Invoke(null, new object[] { material });
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Battle Bus HDRP material validation failed: " +
                             $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static MethodInfo? ResolveMaterialValidationMethod(string typeName, string methodName,
        ref MethodInfo? cachedMethod, ref bool resolved)
    {
        if (resolved)
            return cachedMethod;
        resolved = true;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(typeName, false);
            if (type == null)
                continue;
            cachedMethod = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(Material) }, null);
            if (cachedMethod != null)
                break;
        }
        return cachedMethod;
    }

    private static void SetMaterialFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static Texture? GetMaterialBaseColorTexture(Material material, out string propertyName)
    {
        foreach (var property in new[] { "_BaseColorMap", "baseColorTexture", "_MainTex" })
            if (material.HasProperty(property) && material.GetTexture(property) != null)
            {
                propertyName = property;
                return material.GetTexture(property);
            }
        propertyName = string.Empty;
        return null;
    }

    private static Texture? GetMaterialBaseColorTexture(Material material) =>
        GetMaterialBaseColorTexture(material, out _);

    private static bool IsBusTireRenderer(Transform rendererTransform)
    {
        for (var current = rendererTransform; current != null; current = current.parent)
            if (current.name.StartsWith("BattleBusTire_", StringComparison.Ordinal))
                return true;
        return false;
    }

    private static void ConfigureModelLayer(GameObject model, Renderer[] vehicleRenderers)
    {
        var vehiclesLayer = LayerMask.NameToLayer("Vehicles");
        if (vehiclesLayer < 0)
            throw new InvalidOperationException("The Vehicles layer is missing from ProjectSettings/TagManager.asset.");
        foreach (var transform in model.GetComponentsInChildren<Transform>(true))
            transform.gameObject.layer = vehiclesLayer;
        foreach (var renderer in vehicleRenderers)
            renderer.gameObject.layer = vehiclesLayer;
    }

    private static void ConfigureNavMeshObstacle(GameObject root, NavMeshObstacle obstacle, Bounds bounds)
    {
        var obstacleTransform = obstacle.transform;
        obstacleTransform.SetParent(root.transform, false);
        obstacleTransform.localPosition = Vector3.zero;
        obstacleTransform.localRotation = Quaternion.identity;
        obstacleTransform.localScale = Vector3.one;

        // VehicleHelper reads NavMeshObstacle.center/size directly as the cached
        // vehicle bounds, without applying the obstacle Transform. Keep this
        // component aligned to the vehicle root so the cached bounds and the
        // physical NavMesh obstacle use the same bus-sized dimensions.
        obstacle.center = bounds.center;
        obstacle.size = bounds.size;
        Debug.Log($"Battle Bus ground footprint: center={bounds.center}, size={bounds.size}; balloon excluded.");
    }

    private static void AttachFlightController(GameObject root, WheelGroups groups, Transform? flameMotion,
        Transform[] rearArms)
    {
        var frontLeft = FindTransform(root.transform, "FrontLeft_WheelController");
        var frontRight = FindTransform(root.transform, "FrontRight_WheelController");
        var rearLeft = FindTransform(root.transform, "RearLeft_WheelController");
        var rearRight = FindTransform(root.transform, "RearRight_WheelController");
        if (frontLeft == null || frontRight == null || rearLeft == null || rearRight == null)
            throw new InvalidOperationException("Battle Bus wheel controller transforms are incomplete.");

        var wheelVisuals = new[]
        {
            FindWheelVisual(frontLeft),
            FindWheelVisual(frontRight),
            FindWheelVisual(rearLeft),
            FindWheelVisual(rearRight),
        };
        if (wheelVisuals.Any(visual => visual == null))
            throw new InvalidOperationException("Battle Bus wheel visual camber pivots are incomplete.");

        var flight = root.GetComponent<BattleBusFlightController>() ?? root.AddComponent<BattleBusFlightController>();
        var serialized = new SerializedObject(flight);
        SetObject(serialized, "flameMotion", flameMotion);
        SetObjectArray(serialized, "rearArmPivots", rearArms);
        var supportPoints = new Transform[groups.SupportPositions.Count];
        for (var index = 0; index < groups.SupportPositions.Count; index++)
        {
            var supportPoint = new GameObject($"BattleBusGroundProbe_{index:D2}").transform;
            supportPoint.SetParent(root.transform, false);
            supportPoint.localPosition = groups.SupportPositions[index];
            supportPoints[index] = supportPoint;
        }
        SetObjectArray(serialized, "supportPoints", supportPoints);
        SetFloatArray(serialized, "supportWheelRadii", new[]
        {
            groups.TireSize.y * 0.5f,
            groups.TireSize.y * 0.5f,
            groups.RearTireSize.y * 0.5f,
            groups.RearTireSize.y * 0.5f,
        });
        SetObjectArray(serialized, "wheelCamberPivots",
            wheelVisuals.Select(visual => visual!.CamberPivot).ToArray());
        SetFloatArray(serialized, "wheelCamberDirections", new[] { 1f, -1f, 1f, -1f });
        SetNumber(serialized, "flightCamberDegrees", 6f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        flight.CachePrefabConfiguration();
    }

    private static Vector3 RemoveSourceStaticFlame(Transform modelRoot, Transform vehicleRoot)
    {
        var renderer = modelRoot.GetComponentsInChildren<MeshRenderer>(true)
            .SingleOrDefault(candidate => string.Equals(candidate.name,
                SourceStaticFlameRendererName, StringComparison.Ordinal));
        if (renderer == null)
            throw new InvalidOperationException(
                $"Could not identify the source static flame renderer '{SourceStaticFlameRendererName}'.");

        var filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null ||
            !TryGetRendererBounds(vehicleRoot, new[] { renderer }, out var bounds))
            throw new InvalidOperationException("The source static flame mesh is missing or has no bounds.");

        var warmMaterial = renderer.sharedMaterials.FirstOrDefault(material => material != null &&
            material.name.IndexOf("Material__82", StringComparison.OrdinalIgnoreCase) >= 0);
        var color = warmMaterial != null
            ? GetMaterialColor(warmMaterial, "_BaseColor", "baseColorFactor", "_Color")
            : Color.black;
        if (warmMaterial == null || color.r < 0.7f || color.g < 0.05f || color.g > 0.4f ||
            color.b > 0.1f || Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) > 0.75f)
            throw new InvalidOperationException(
                $"The expected source flame renderer no longer matches its known orange burner mesh: " +
                $"material='{warmMaterial?.name ?? "missing"}', color={color}, bounds={bounds}.");

        var anchor = bounds.center;
        filter.sharedMesh = null;
        renderer.sharedMaterials = Array.Empty<Material>();
        renderer.enabled = false;
        Debug.Log($"Battle Bus duplicate static flame removed: renderer='{renderer.name}', " +
                  $"anchor={anchor}, sourceColor={color}, sourceBounds={bounds}.");
        return anchor;
    }

    private static void RemoveSourceFlameOverlay(Transform modelRoot, Transform vehicleRoot)
    {
        var renderer = modelRoot.GetComponentsInChildren<MeshRenderer>(true)
            .SingleOrDefault(candidate => string.Equals(candidate.name, "Object_26", StringComparison.Ordinal));
        if (renderer == null || !TryGetRendererBounds(vehicleRoot, new[] { renderer }, out var bounds))
            throw new InvalidOperationException("The source flame overlay Object_26 is missing.");
        var material = renderer.sharedMaterial;
        var color = material != null
            ? GetMaterialColor(material, "_BaseColor", "baseColorFactor", "_Color")
            : Color.black;
        if (material == null ||
            !(material.name.Contains("Material.001") || material.name.Contains("Material_001")) ||
            color.r < 0.8f || color.g < 0.3f || color.g > 0.8f || color.a > 0.4f ||
            bounds.center.y < 1.5f || bounds.center.y > 4.5f)
            throw new InvalidOperationException(
                $"The expected translucent source flame overlay changed: material='{material?.name}', " +
                $"color={color}, bounds={bounds}.");
        renderer.GetComponent<MeshFilter>()!.sharedMesh = null;
        renderer.sharedMaterials = Array.Empty<Material>();
        renderer.enabled = false;
        Debug.Log($"Battle Bus additional source flame overlay removed: bounds={bounds}, color={color}.");
    }

    private static Transform CreateFallbackFlame(GameObject root, Bounds chassisBounds,
        Vector3 sourceFlameAnchor)
    {
        var flame = new GameObject("BattleBusBalloonFlame").transform;
        flame.SetParent(root.transform, false);
        flame.localPosition = sourceFlameAnchor + Vector3.down * 0.22f;
        // Unity's cone emits along local +Z; point that axis up into the balloon.
        flame.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        flame.localScale = Vector3.one;
        var particles = flame.gameObject.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.loop = true;
        main.playOnAwake = true;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.32f, 0.56f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.38f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
        main.startColor = Color.white;
        main.maxParticles = 48;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var emission = particles.emission;
        emission.rateOverTime = 42f;
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 8f;
        shape.radius = 0.035f;
        shape.length = 0f;
        var color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(new Gradient
        {
            colorKeys = new[]
            {
                new GradientColorKey(new Color(1f, 0.88f, 0.34f), 0f),
                new GradientColorKey(new Color(1f, 0.38f, 0.035f), 0.55f),
                new GradientColorKey(new Color(0.60f, 0.055f, 0.01f), 1f),
            },
            alphaKeys = new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.85f, 0.14f),
                new GradientAlphaKey(0.66f, 0.60f),
                new GradientAlphaKey(0f, 1f),
            },
        });
        var size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f,
            new AnimationCurve(new Keyframe(0f, 0.35f),
                new Keyframe(0.25f, 1f), new Keyframe(1f, 0.15f)));
        var noise = particles.noise;
        noise.enabled = true;
        noise.strength = 0.09f;
        noise.frequency = 1.5f;
        noise.scrollSpeed = 1f;
        var renderer = flame.gameObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
        });
        renderer.sharedMaterial = CreateFallbackFlameMaterial();
        Debug.Log($"Battle Bus single particle burner flame generated: " +
                  $"localPosition={flame.localPosition}, sourceAnchor={sourceFlameAnchor}, " +
                  $"rate={emission.rateOverTime.constant}, material='{renderer.sharedMaterial.name}'.");
        return flame;
    }

    private static Texture2D CreateFlameSpriteTexture()
    {
        const int resolution = 128;
        var generated = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            name = "BattleBusFlameSprite",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        var pixels = new Color32[resolution * resolution];
        for (var y = 0; y < resolution; y++)
        for (var x = 0; x < resolution; x++)
        {
            var u = (x + 0.5f) / resolution * 2f - 1f;
            var v = (y + 0.5f) / resolution;
            var taper = Mathf.Lerp(0.9f, 0.18f, v);
            var bend = Mathf.Sin(v * 8f) * 0.08f * v;
            var across = Mathf.Abs((u - bend) / taper);
            var edge = Mathf.Pow(Mathf.Clamp01(1f - across), 1.4f);
            var vertical = Mathf.Pow(Mathf.Sin(v * Mathf.PI), 0.9f);
            var alpha = Mathf.Clamp01(edge * vertical * 0.9f);
            pixels[y * resolution + x] = new Color(
                1f, Mathf.Lerp(0.72f, 0.13f, v), Mathf.Lerp(0.28f, 0.015f, v), alpha);
        }
        generated.SetPixels32(pixels);
        generated.Apply(false, false);
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(FlameTexturePath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(generated, FlameTexturePath);
            existing = generated;
        }
        else
        {
            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
        }
        SetBundle(FlameTexturePath);
        return existing;
    }

    private static Material CreateFallbackFlameMaterial()
    {
        var shader = Shader.Find("HDRP/Unlit") ?? throw new InvalidOperationException("HDRP/Unlit missing.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(FlameMaterialPath);
        if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, FlameMaterialPath); }
        material.shader = shader;
        material.name = "BattleBusBalloonFlame";
        BattleBusVisualController.ConfigureFireMaterial(material, CreateFlameSpriteTexture());
        EditorUtility.SetDirty(material);
        SetBundle(FlameMaterialPath);
        return material;
    }

    private static void DisableLights(GameObject root)
    {
        foreach (var light in root.GetComponentsInChildren<Light>(true))
            light.enabled = false;
    }

    private static WheelVisual? FindOrCreateWheelVisual(Transform controller)
    {
        var existingRoot = FindTransform(controller, controller.name + "_BattleBusVisual");
        var existingCamber = FindTransform(controller, controller.name + "_CamberPivot");
        if (existingRoot == null)
        {
            var visualRootObject = new GameObject(controller.name + "_BattleBusVisual");
            visualRootObject.transform.SetParent(controller, false);
            existingRoot = visualRootObject.transform;
        }

        if (existingCamber == null)
        {
            var camberObject = new GameObject(controller.name + "_CamberPivot");
            camberObject.transform.SetParent(existingRoot, false);
            existingCamber = camberObject.transform;
        }

        existingRoot.SetParent(controller, false);
        existingRoot.localPosition = Vector3.zero;
        existingRoot.localRotation = Quaternion.identity;
        existingRoot.localScale = Vector3.one;
        existingCamber.SetParent(existingRoot, false);
        existingCamber.localPosition = Vector3.zero;
        existingCamber.localRotation = Quaternion.identity;
        existingCamber.localScale = Vector3.one;

        foreach (var component in controller.GetComponents<MonoBehaviour>())
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            var wheel = serialized.FindProperty("wheel");
            var visual = wheel?.FindPropertyRelative("visual");
            if (visual?.propertyType != SerializedPropertyType.ObjectReference)
                continue;

            visual.objectReferenceValue = existingRoot.gameObject;
            var visualTransform = wheel!.FindPropertyRelative("visualTransform");
            if (visualTransform?.propertyType == SerializedPropertyType.ObjectReference)
                visualTransform.objectReferenceValue = existingRoot;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return new WheelVisual(existingRoot, existingCamber);
        }

        throw new InvalidOperationException($"Wheel controller '{controller.name}' has no visual property.");
    }

    private static WheelVisual? FindWheelVisual(Transform controller)
    {
        var mount = FindTransform(controller, controller.name + "_BattleBusVisual");
        var camber = FindTransform(controller, controller.name + "_CamberPivot");
        return mount == null || camber == null ? null : new WheelVisual(mount, camber);
    }

    private static Bounds GetChassisBounds(Transform vehicleRoot, Renderer[] renderers,
        WheelGroups wheels, Bounds allBounds)
    {
        var selected = new List<Renderer>();
        foreach (var renderer in renderers)
        {
            if (!TryGetRendererBounds(vehicleRoot, new[] { renderer }, out var bounds))
                continue;
            if (bounds.center.y < 2.2f && bounds.size.y < 3.8f && bounds.size.x < 5.5f &&
                bounds.size.z < 10f)
                selected.Add(renderer);
        }
        if (TryGetRendererBounds(vehicleRoot, selected.ToArray(), out var modelBounds) &&
            modelBounds.size.x > 0.8f && modelBounds.size.z > 1.8f)
            return modelBounds;

        var front = (wheels.FrontLeftPosition.z + wheels.FrontRightPosition.z) * 0.5f;
        var rear = (wheels.RearLeftPosition.z + wheels.RearRightPosition.z) * 0.5f;
        var width = Mathf.Abs(wheels.FrontLeftPosition.x - wheels.FrontRightPosition.x) + 0.8f;
        var center = new Vector3(0f, Mathf.Max(1.0f, wheels.AverageWheelY + 0.8f), (front + rear) * 0.5f);
        var size = new Vector3(width, 1.6f, Mathf.Abs(front - rear) + 2.0f);
        if (allBounds.size.x > size.x)
            size.x = Mathf.Min(allBounds.size.x, 3.6f);
        return new Bounds(center, size);
    }

    private static bool TryGetRendererBounds(Transform root, Renderer[] renderers, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;
            var world = renderer.bounds;
            var min = world.min;
            var max = world.max;
            for (var corner = 0; corner < 8; corner++)
            {
                var point = new Vector3((corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                point = root.InverseTransformPoint(point);
                if (!found)
                {
                    bounds = new Bounds(point, Vector3.zero);
                    found = true;
                }
                else
                    bounds.Encapsulate(point);
            }
        }
        return found;
    }

    private static Bounds GetRendererBounds(Transform root, Renderer[] renderers)
    {
        if (!TryGetRendererBounds(root, renderers, out var bounds))
            throw new InvalidOperationException("Battle Bus renderers did not produce valid bounds.");
        return bounds;
    }

    private static Vector3 AveragePosition(List<MeshComponent> components)
    {
        var total = Vector3.zero;
        foreach (var component in components)
            total += component.WheelAnchor;
        return total / components.Count;
    }

    private static Vector3 AverageBoundsSize(List<MeshComponent> components)
    {
        var total = Vector3.zero;
        foreach (var component in components)
            total += component.Bounds.size;
        return total / components.Count;
    }

    private static void SetOrCreatePosition(GameObject root, string name, Vector3 position)
    {
        var transform = FindTransform(root.transform, name);
        if (transform == null)
        {
            transform = new GameObject(name).transform;
            transform.SetParent(root.transform, false);
        }
        transform.localPosition = position;
        transform.localRotation = Quaternion.identity;
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        return null;
    }

    private static Component? FindComponentBySerializedProperty(GameObject root, string propertyName)
    {
        foreach (var component in root.GetComponentsInChildren<Component>(true))
        {
            if (component == null)
                continue;
            var serialized = new SerializedObject(component);
            if (serialized.FindProperty(propertyName) != null)
                return component;
        }
        return null;
    }

    private static void SetObject(SerializedObject serialized, string propertyName, UnityEngine.Object? value)
    {
        var property = serialized.FindProperty(propertyName);
        if (property?.propertyType == SerializedPropertyType.ObjectReference)
            property.objectReferenceValue = value;
    }

    private static void SetObjectArray(SerializedObject serialized, string propertyName,
        UnityEngine.Object[] values)
    {
        var property = serialized.FindProperty(propertyName);
        if (property == null || !property.isArray)
            return;
        property.arraySize = values.Length;
        for (var index = 0; index < values.Length; index++)
            property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
    }

    private static void SetFloatArray(SerializedObject serialized, string propertyName, float[] values)
    {
        var property = serialized.FindProperty(propertyName);
        if (property == null || !property.isArray)
            return;
        property.arraySize = values.Length;
        for (var index = 0; index < values.Length; index++)
            property.GetArrayElementAtIndex(index).floatValue = values[index];
    }

    private static void SetString(SerializedObject serialized, string propertyName, string value)
    {
        var property = serialized.FindProperty(propertyName);
        if (property != null)
            property.stringValue = value;
    }

    private static void SetBool(SerializedObject serialized, string propertyName, bool value)
    {
        var property = serialized.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }

    private static void SetNumber(SerializedObject serialized, string propertyName, float value)
    {
        var property = serialized.FindProperty(propertyName);
        if (property == null)
            return;
        if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else if (property.propertyType == SerializedPropertyType.Float)
            property.floatValue = value;
    }

    private static void SetNumberOnProperty(SerializedProperty? property, float value)
    {
        if (property == null)
            return;
        if (property.propertyType == SerializedPropertyType.Integer)
            property.intValue = Mathf.RoundToInt(value);
        else if (property.propertyType == SerializedPropertyType.Float)
            property.floatValue = value;
    }

    private static void SetVector3(SerializedObject serialized, string propertyName, Vector3 value)
    {
        var property = serialized.FindProperty(propertyName);
        if (property?.propertyType == SerializedPropertyType.Vector3)
            property.vector3Value = value;
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

    private static Mesh SaveGeneratedMesh(Mesh mesh, string assetPath)
    {
        EnsureAssetFolder(GeneratedMeshFolder);
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, assetPath);
            existing = mesh;
        }
        else
        {
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = mesh.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        SetBundle(assetPath);
        return existing;
    }

    private static void RemoveUnusedGeneratedMeshes()
    {
        if (!AssetDatabase.IsValidFolder(GeneratedMeshFolder))
            return;

        var prefabDependencies = new HashSet<string>(
            AssetDatabase.GetDependencies(VehiclePrefabPath, true),
            StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var guid in AssetDatabase.FindAssets("", new[] { GeneratedMeshFolder }))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(assetPath) ||
                AssetDatabase.LoadAssetAtPath<Mesh>(assetPath) == null ||
                prefabDependencies.Contains(assetPath))
                continue;
            if (AssetDatabase.DeleteAsset(assetPath))
                removed++;
        }

        if (removed > 0)
            Debug.Log($"Battle Bus generated mesh cleanup removed {removed} unreferenced meshes.");
    }

    private static void RemoveUnusedGeneratedMaterials()
    {
        if (!AssetDatabase.IsValidFolder(GeneratedMaterialFolder))
            return;

        var prefabDependencies = new HashSet<string>(
            AssetDatabase.GetDependencies(VehiclePrefabPath, true),
            StringComparer.OrdinalIgnoreCase);
        // Newly written material textures can be absent from Unity's cached
        // transitive prefab dependency list until their next import. Read the
        // live material references before deleting any generated paint asset.
        foreach (var materialPath in prefabDependencies.Where(path =>
                     path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
                continue;
            foreach (var property in material.GetTexturePropertyNames())
            {
                var texture = material.GetTexture(property);
                if (texture == null)
                    continue;
                var texturePath = AssetDatabase.GetAssetPath(texture);
                if (!string.IsNullOrEmpty(texturePath))
                    prefabDependencies.Add(texturePath);
            }
        }
        var removed = 0;
        foreach (var guid in AssetDatabase.FindAssets("", new[] { GeneratedMaterialFolder }))
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetDatabase.IsValidFolder(assetPath))
                continue;
            var generatedPaint = assetPath.StartsWith(
                GeneratedMaterialFolder + "/BattleBusPaint_", StringComparison.OrdinalIgnoreCase);
            var generatedSourceMaterial = assetPath.StartsWith(
                GeneratedMaterialFolder + "/BattleBusMaterial_", StringComparison.OrdinalIgnoreCase);
            if ((!generatedPaint && !generatedSourceMaterial) || prefabDependencies.Contains(assetPath))
                continue;
            if (AssetDatabase.DeleteAsset(assetPath))
                removed++;
        }

        if (removed > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Battle Bus generated material cleanup removed {removed} unreferenced assets.");
        }
    }

    private static void SetBundle(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath);
        if (importer == null)
            throw new InvalidOperationException($"Could not set asset bundle for '{assetPath}'.");
        importer.assetBundleName = BundleName;
        importer.assetBundleVariant = "unity3d";
        EditorUtility.SetDirty(importer);
    }

    private static string Sanitize(string value)
    {
        var chars = value.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
            if (!char.IsLetterOrDigit(chars[index]) && chars[index] != '_' && chars[index] != '-')
                chars[index] = '_';
        return new string(chars);
    }

    private static void CopyRendererSettings(Renderer source, Renderer target)
    {
        target.shadowCastingMode = source.shadowCastingMode;
        target.receiveShadows = source.receiveShadows;
        target.motionVectorGenerationMode = source.motionVectorGenerationMode;
        target.allowOcclusionWhenDynamic = source.allowOcclusionWhenDynamic;
        target.lightProbeUsage = source.lightProbeUsage;
        target.reflectionProbeUsage = source.reflectionProbeUsage;
        target.renderingLayerMask = source.renderingLayerMask;
        target.sortingLayerID = source.sortingLayerID;
        target.sortingOrder = source.sortingOrder;
    }

    [Serializable]
    private sealed class GlbDocument
    {
        public GlbBufferView[] bufferViews = Array.Empty<GlbBufferView>();
        public GlbImage[] images = Array.Empty<GlbImage>();
    }

    [Serializable]
    private sealed class GlbBufferView
    {
        public int byteOffset;
        public int byteLength;
    }

    [Serializable]
    private sealed class GlbImage
    {
        public int bufferView;
    }

    private sealed class MeshComponent
    {
        internal readonly MeshFilter Filter;
        internal readonly Renderer Renderer;
        internal readonly Mesh? SourceMesh;
        internal readonly Transform VehicleRoot;
        internal readonly List<int>[] Triangles;
        internal readonly HashSet<int> Vertices;
        internal readonly Bounds Bounds;
        internal readonly int FaceCount;
        internal readonly Vector3 PositionOffset;
        internal readonly Vector3 WheelAnchor;
        internal int TireIndex;
        internal int PartIndex;
        internal Quaternion WheelRotationCorrection = Quaternion.identity;

        internal MeshComponent(MeshFilter filter, Renderer renderer, List<int>[] triangles,
            HashSet<int> vertices, Bounds bounds, int faceCount,
            Vector3 positionOffset = default, Vector3? wheelAnchor = null)
        {
            Filter = filter;
            Renderer = renderer;
            SourceMesh = filter.sharedMesh;
            VehicleRoot = filter.transform.root;
            Triangles = triangles;
            Vertices = vertices;
            Bounds = bounds;
            FaceCount = faceCount;
            PositionOffset = positionOffset;
            WheelAnchor = wheelAnchor ?? bounds.center;
        }
    }

    private sealed class ComponentAccumulator
    {
        internal readonly List<int>[] Triangles;
        internal readonly HashSet<int> Vertices = new HashSet<int>();
        internal int FaceCount => Triangles.Sum(indices => indices.Count) / 3;

        internal ComponentAccumulator(int submeshCount)
        {
            Triangles = new List<int>[submeshCount];
            for (var index = 0; index < submeshCount; index++)
                Triangles[index] = new List<int>();
        }
    }

    private sealed class WheelGroups
    {
        internal List<MeshComponent> FrontLeft = new List<MeshComponent>();
        internal List<MeshComponent> FrontRight = new List<MeshComponent>();
        internal List<MeshComponent> RearLeft = new List<MeshComponent>();
        internal List<MeshComponent> RearRight = new List<MeshComponent>();
        internal Vector3 FrontLeftPosition;
        internal Vector3 FrontRightPosition;
        internal Vector3 RearLeftPosition;
        internal Vector3 RearRightPosition;
        internal float AverageRadius;
        internal Vector3 TireSize;
        internal Vector3 RearTireSize;
        internal List<Vector3> SupportPositions = new List<Vector3>();
        internal float AverageWheelY => (FrontLeftPosition.y + FrontRightPosition.y +
                                        RearLeftPosition.y + RearRightPosition.y) * 0.25f;

        internal List<MeshComponent> GetGroup(MeshComponent component)
        {
            if (FrontLeft.Contains(component)) return FrontLeft;
            if (FrontRight.Contains(component)) return FrontRight;
            if (RearLeft.Contains(component)) return RearLeft;
            if (RearRight.Contains(component)) return RearRight;
            throw new InvalidOperationException("Tire component does not belong to a wheel group.");
        }

        internal Vector3 GetPosition(List<MeshComponent> group)
        {
            if (ReferenceEquals(group, FrontLeft)) return FrontLeftPosition;
            if (ReferenceEquals(group, FrontRight)) return FrontRightPosition;
            if (ReferenceEquals(group, RearLeft)) return RearLeftPosition;
            return RearRightPosition;
        }

        internal string GetControllerName(List<MeshComponent> group)
        {
            if (ReferenceEquals(group, FrontLeft)) return "FrontLeft_WheelController";
            if (ReferenceEquals(group, FrontRight)) return "FrontRight_WheelController";
            if (ReferenceEquals(group, RearLeft)) return "RearLeft_WheelController";
            return "RearRight_WheelController";
        }
    }

    private sealed class TireAssembly
    {
        internal List<MeshComponent> Components = new List<MeshComponent>();
        internal Vector3 Center;
        internal Vector3 Size;
    }

    private sealed class WheelVisual
    {
        internal readonly Transform Root;
        internal readonly Transform CamberPivot;
        internal int TireCount;
        internal WheelVisual(Transform root, Transform camberPivot)
        {
            Root = root;
            CamberPivot = camberPivot;
        }
    }

    private readonly struct TriangleKey : IEquatable<TriangleKey>
    {
        private readonly int first;
        private readonly int second;
        private readonly int third;

        internal TriangleKey(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            first = a;
            second = b;
            third = c;
        }

        public bool Equals(TriangleKey other) =>
            first == other.first && second == other.second && third == other.third;

        public override bool Equals(object? obj) => obj is TriangleKey other && Equals(other);

        public override int GetHashCode() => unchecked((first * 397) ^ (second * 31) ^ third);
    }

    private readonly struct QuantizedPosition : IEquatable<QuantizedPosition>
    {
        private readonly int x;
        private readonly int y;
        private readonly int z;
        internal QuantizedPosition(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
        public bool Equals(QuantizedPosition other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object? obj) => obj is QuantizedPosition other && Equals(other);
        public override int GetHashCode() => unchecked((x * 397) ^ (y * 31) ^ z);
    }

    private sealed class UnionFind
    {
        private readonly int[] parent;
        private readonly byte[] rank;
        internal UnionFind(int size)
        {
            parent = new int[size];
            rank = new byte[size];
            for (var index = 0; index < size; index++) parent[index] = index;
        }
        internal int Find(int value)
        {
            if (parent[value] != value) parent[value] = Find(parent[value]);
            return parent[value];
        }
        internal void Join(int left, int right)
        {
            var leftRoot = Find(left);
            var rightRoot = Find(right);
            if (leftRoot == rightRoot) return;
            if (rank[leftRoot] < rank[rightRoot]) parent[leftRoot] = rightRoot;
            else if (rank[leftRoot] > rank[rightRoot]) parent[rightRoot] = leftRoot;
            else { parent[rightRoot] = leftRoot; rank[leftRoot]++; }
        }
    }
}
