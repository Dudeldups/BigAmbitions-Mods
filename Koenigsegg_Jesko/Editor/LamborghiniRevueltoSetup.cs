#nullable enable
using System;
using System.Collections.Generic;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Rendering;

public static class KoenigseggJeskoSetup
{
    private const string ModRoot = "Assets/Mods/Koenigsegg_Jesko";
    private const string ReferenceAssetPath = "Assets/Mods/AudiRS6R/AudiRS6R.asset";
    private const string ReferencePrefabPath = "Assets/Mods/AudiRS6R/AudiRS6R.prefab";
    private const string ModelPath = ModRoot + "/Models/2020_koenigsegg_jesko.glb";
    private const string NativeEngineFallbackClipPath =
        ModRoot + "/Config/Audio/EngineLow.wav";
    private const string MaterialFolder = ModRoot + "/Models/GeneratedMaterials";
    private const string VehicleAssetPath = ModRoot + "/KoenigseggJesko.asset";
    private const string VehiclePrefabPath = ModRoot + "/KoenigseggJesko.prefab";
    private const string ManifestPath = ModRoot + "/ModManifest.asset";
    private const string AssemblyPath = ModRoot + "/KoenigseggJesko.asmdef";
    private const string LocalesPath = ModRoot + "/Locales";
    private const string WindowsBundlePath =
        ModRoot + "/AssetBundles/Windows/koenigseggjesko.unity3d";
    private const string VehicleTypeName =
        "koenigseggjesko-vehicle:vehicletype_koenigseggjesko";
    private const float TargetLength = 4.610f;
    private const float TargetWidth = 2.030f;
    private const float TargetHeight = 1.210f;
    private const float TireFrictionCircleStrength = 0.92f;
    private const float AntiRollBarForce = 7800f;
    private const float FrontSuspensionTravel = 0.08f;
    private const float RearSuspensionTravel = 0.06f;
    private const float EffectiveEnginePowerKw = 390f;
    private const float BrakeTorque = 2100f;
    private const float FrontWheelOutset = 0.03f;
    private const float RearWheelOutset = 0f;
    private const float FrontWheelForwardOffset = 0.06f;
    private const float RearWheelForwardOffset = 0.12f;
    private const float FrontWheelRearwardOffset = -0.065f;
    private const float RearWheelRearwardOffset = -0.115f;
    private const float FrontCaliperOutset = 0.03f;
    private const float FrontCaliperHeightOffset = 0.06f;
    private const float FrontCaliperLongitudinalOffset = -0.045f;
    private const float RearCaliperOutset = 0.01f;
    private const float RearCaliperHeightOffset = 0.04f;
    private const float RearCaliperLongitudinalOffset = 0.08f;
    private const float FrontWheelHeightOffset = 0.04f;
    private const float BodyVisualHeightOffset = 0.07f;
    // Retain the user-confirmed tire grounding. Body ride height is authored
    // independently through BodyVisualHeightOffset above.
    private const float WheelGroundingOffset = 0.06f;
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
            { "FrontLeft_WheelController", new Vector3(-0.806f, 0.327f, 1.354f) },
            { "FrontRight_WheelController", new Vector3(0.806f, 0.327f, 1.354f) },
            { "RearLeft_WheelController", new Vector3(-0.767f, 0.331f, -1.289f) },
            { "RearRight_WheelController", new Vector3(0.767f, 0.331f, -1.289f) },
        };

    private static readonly float[] JeskoGears =
    {
        -3.00f,
        0f,
        3.20f,
        2.15f,
        1.55f,
        1.18f,
        0.94f,
        0.78f,
        0.66f,
        0.57f,
        0.49f,
    };

    private static AnimationCurve CreateJeskoPowerCurve() =>
        new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.106f, 0.02f),
            new Keyframe(0.318f, 0.296f),
            new Keyframe(0.480f, 0.447f),
            new Keyframe(0.726f, 0.676f),
            new Keyframe(0.918f, 1f),
            new Keyframe(1f, 0.94f));

    [MenuItem("Big Ambitions Mods/Setup Koenigsegg Jesko")]
    public static void Generate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var vehicleType = CreateVehicleType();
        CreateVehiclePrefab(vehicleType);
        CreateManifest();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                "KoenigseggJesko setup complete: generated a fitted Jesko with four " +
                "independent wheel visuals, nine-speed LST, Jesko V8 audio, functional lamp " +
                "geometry, colorable body/calipers, and corrected glass materials.");
    }

    public static void GenerateAndBuild()
    {
        Generate();
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
        VerifyAccentLettering();
    }

    public static void BuildAndVerifyExisting()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ModAssetBundleCli.BuildForMod();
        VerifyBuiltBundle();
        VerifyAccentLettering();
    }

    // Batch entry point: omit -quit; the test exits Unity after physics callbacks.
    public static void VerifyImpactDamageInPlayMode()
    {
        SessionState.SetBool("JeskoImpactVerificationPending", true);
        EditorApplication.EnterPlaymode();
    }

    [InitializeOnLoadMethod]
    private static void ResumeImpactVerification()
    {
        if (!SessionState.GetBool("JeskoImpactVerificationPending", false)) return;
        EditorApplication.update += RunPendingImpactVerification;
    }

    private static void RunPendingImpactVerification()
    {
        if (!Application.isPlaying) return;
        EditorApplication.update -= RunPendingImpactVerification;
        SessionState.EraseBool("JeskoImpactVerificationPending");
        try { VerifyImpactDamage(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    public static void VerifyImpactDamage()
    {
        var type = typeof(KoenigseggJeskoMod).Assembly.GetType("KoenigseggJeskoImpactDamageController", true);
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var damage = type.GetMethod("ImpactDamage", flags)!;
        var reconcile = type.GetMethod("ReconcileDamage", flags)!;
        var closing = type.GetMethod("ClosingSpeed", flags)!;
        float Severity(float speed, float impulse, float otherMass = 1420f) =>
            (float)damage.Invoke(null, new object[] { speed, impulse, 1420f, otherMass, 0.02f, 0.8f, 3.5f });
        float Reconcile(float current, float baseline, float expected) =>
            (float)reconcile.Invoke(null, new object[] { current, baseline, expected });
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Jesko impact verification: " + name);
        }
        var substantial = Severity(32.7f, 0f);
        Check(substantial > 0.30f && substantial < 0.35f, "low-impulse severe contact lost");
        Check(Severity(0f, 0f) == 0f && Severity(2f, 500f) == 0f, "resting/low-energy contact charged");
        Check(Severity(0f, 14200f) > 0.19f, "continuing contact impulse lost");
        Check(Mathf.Abs(Reconcile(0.40f, 0.1f, 0.3f) - 0.40f) < 0.0001f, "native damage double-counted");
        Check(Mathf.Abs(Reconcile(0.102f, 0.1f, 0.3f) - 0.40f) < 0.0001f, "weak first contact masks main hit");
        Check(Reconcile(0.7f, 0.1f, 0.3f) == 0.7f, "native severe damage reduced");
        var corrected = Reconcile(0.102f, 0.1f, 0.3f);
        Check(Reconcile(corrected, 0.1f, 0.3f) == corrected, "repeat changed budget");
        Check(Reconcile(0.9f, 0.9f, 0.5f) == 1f, "condition unbounded");
        Check(Severity(20f, 0f, 0f) > Severity(20f, 0f, 1420f), "dynamic mass not respected");

        // Verify Unity's real callback velocity/normal convention, including a
        // rear-first impact, without running the game or mutating the prefab.
        var scene = UnityEngine.SceneManagement.SceneManager.CreateScene("JeskoImpactVerification",
            new UnityEngine.SceneManagement.CreateSceneParameters(UnityEngine.SceneManagement.LocalPhysicsMode.Physics3D));
        try
        {
            Check(scene.GetPhysicsScene() != Physics.defaultPhysicsScene, "preview must isolate physics");
            foreach (var direction in new[] { Vector3.forward, Vector3.back })
            {
                var moving = new GameObject("JeskoImpactProbe");
                var wall = new GameObject("JeskoImpactWall");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(moving, scene);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, scene);
                moving.AddComponent<BoxCollider>();
                var body = moving.AddComponent<Rigidbody>();
                body.mass = 1420f;
                body.useGravity = false;
                var probe = moving.AddComponent<KoenigseggJeskoImpactVerificationProbe>();
                wall.transform.position = direction * 2f;
                wall.AddComponent<BoxCollider>();
                body.velocity = direction * 20f;
                Physics.SyncTransforms();
                for (var i = 0; i < 10 && !probe.Contacted; i++) scene.GetPhysicsScene().Simulate(0.02f);
                Check(probe.Contacted, "physics probe produced no collision");
                var speed = (float)closing.Invoke(null, new object[] { probe.RelativeVelocity, probe.Normal });
                Debug.Log($"Jesko impact probe direction={direction} relative={probe.RelativeVelocity} normal={probe.Normal} closing={speed} impulse={probe.Impulse}");
                Check(speed > 15f, "contact-normal sign rejects real forward/reverse crash");
                Check((float)closing.Invoke(null, new object[] { -probe.RelativeVelocity, probe.Normal }) == 0f,
                    "separating motion charged");
                Check((float)closing.Invoke(null, new object[] { Vector3.right * 20f, probe.Normal }) < 0.001f,
                    "tangential scrape charged");
                UnityEngine.Object.DestroyImmediate(moving);
                UnityEngine.Object.DestroyImmediate(wall);
            }
        }
        finally { UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(scene); }
        Debug.Log("Jesko impact verification passed: forward/reverse callbacks, low impulse, Stay impulse, mass, threshold, native credit, cap, scrape/separation.");
    }

    public static void VerifyAccentLettering()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabPath);
        var runtimeType = typeof(KoenigseggJeskoMod).Assembly.GetType("KoenigseggJeskoPaintController", true);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
        var classify = runtimeType.GetMethod("IsAccentLetteringRenderer", flags)!;
        var textureType = runtimeType.GetNestedType("ExteriorContrastTexture", flags)!;
        var create = textureType.GetMethod("Create", flags)!;
        var apply = textureType.GetMethod("Apply", flags)!;
        var renderers = new List<Renderer>();
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && (bool)classify.Invoke(null, new object[] { renderer, material }))
                    renderers.Add(renderer);
        if (renderers.Count != 3)
            throw new InvalidOperationException($"Lettering: expected BOOT/REARBUMPER/WING_REAR, got {renderers.Count}.");

        var source = renderers[0].sharedMaterial;
        var state = create.Invoke(null, new object[] { source, false, true })!;
        var materialResult = (Material)textureType.GetProperty("Material", flags)!.GetValue(state)!;
        var texture = (Texture2D)materialResult.GetTexture("_BaseColorMap");
        try
        {
            typeof(KoenigseggJeskoMaterials).GetMethod("FixTransparentHdrpMaterial", flags)!
                .Invoke(null, new object[] { materialResult });
            if (materialResult.GetColor("_BaseColor").a < 0.999f)
                throw new InvalidOperationException("Badge material repair reduced lettering opacity to glass opacity.");
            apply.Invoke(state, new object[] { Color.black });
            var darkPaintPixels = texture.GetPixels32();
            apply.Invoke(state, new object[] { Color.white });
            var lightPaintPixels = texture.GetPixels32();
            // Independent GLB UV bounds for each authored lettering island.
            var regions = new[] {
                new Rect(0.0259f, 0.4404f, 0.4116f, 0.0625f),
                new Rect(0.0244f, 0.5151f, 0.4072f, 0.0601f),
                new Rect(0.4243f, 0.0615f, 0.0615f, 0.2735f),
            };
            var names = new[] { "cabin-side Jesko", "rear-plate Jesko", "wing 251" };
            for (var regionIndex = 0; regionIndex < regions.Length; regionIndex++)
            {
                var region = regions[regionIndex];
                var changed = 0;
                for (var i = 0; i < lightPaintPixels.Length; i++)
                {
                    var uv = new Vector2((i % texture.width + 0.5f) / texture.width,
                        1f - (i / texture.width + 0.5f) / texture.height);
                    if (!region.Contains(uv) || darkPaintPixels[i].Equals(lightPaintPixels[i]))
                        continue;
                    var light = lightPaintPixels[i];
                    var dark = darkPaintPixels[i];
                    if (light.r != 128 || light.g != 128 || light.b != 128 ||
                        dark.r < 184 || dark.r != dark.g || dark.r != dark.b || dark.a != light.a)
                        throw new InvalidOperationException($"Incorrect contrast/alpha for {names[regionIndex]}.");
                    changed++;
                }
                if (changed < 100)
                    throw new InvalidOperationException($"No readable accent glyphs for {names[regionIndex]} ({changed} pixels).");
                var renderer = renderers.Find(r => r.name.Contains(new[] { "BOOT_mm", "REARBUMPER_mm", "WING_REAR_mm" }[regionIndex]))!;
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                var matchingVertices = 0;
                var meshRegion = new Rect(region.x - 0.001f, region.y - 0.001f,
                    region.width + 0.002f, region.height + 0.002f);
                var left = false;
                var right = false;
                var uvs = mesh.uv;
                var vertices = mesh.vertices;
                for (var i = 0; i < uvs.Length; i++)
                    if (meshRegion.Contains(new Vector2(uvs[i].x, 1f - uvs[i].y)))
                    {
                        matchingVertices++;
                        left |= vertices[i].x < -0.1f;
                        right |= vertices[i].x > 0.1f;
                    }
                if (matchingVertices < (regionIndex == 1 ? 4 : 8))
                    throw new InvalidOperationException($"Missing left/right imported mesh coverage for {names[regionIndex]}: {matchingVertices} vertices.");
                if (regionIndex != 1 && (!left || !right))
                    throw new InvalidOperationException($"Lettering does not cover both sides: {names[regionIndex]}.");
                Debug.Log($"Jesko lettering verified: {names[regionIndex]}, pixels={changed}, vertices={matchingVertices}, light paint=gray, dark paint=white, alpha preserved.");
            }
            var original = (Color32[])textureType.GetField("sourcePixels", flags)!.GetValue(state)!;
            for (var i = 0; i < original.Length; i++)
                if (darkPaintPixels[i].Equals(lightPaintPixels[i]) && !original[i].Equals(lightPaintPixels[i]))
                    throw new InvalidOperationException("Lettering recolored unrelated badge pixels.");
            apply.Invoke(state, new object[] { Color.black });
            var repeated = texture.GetPixels32();
            for (var i = 0; i < repeated.Length; i++)
                if (!repeated[i].Equals(darkPaintPixels[i]))
                    throw new InvalidOperationException("Lettering repaint roundtrip changed output.");
            var args = Environment.GetCommandLineArgs();
            var previewArg = Array.IndexOf(args, "-letteringPreview");
            if (previewArg >= 0 && previewArg + 1 < args.Length)
                System.IO.File.WriteAllBytes(args[previewArg + 1], texture.EncodeToPNG());
            Debug.Log("Jesko lettering verification passed: correct renderers, all five decals, preserved non-letter pixels, repeat repaint.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(materialResult);
            UnityEngine.Object.DestroyImmediate(texture);
        }
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

            var visual = FindTransform(prefab.transform, "KoenigseggVisual") ??
                         throw new InvalidOperationException("Koenigsegg visual root is missing.");
            var visualRideHeightValid =
                Math.Abs(visual.localPosition.y - -0.04485577f) < 0.001f;
            var bodyColliderHolder = FindTransform(prefab.transform, "BodyCollider");
            var bodyColliders = bodyColliderHolder != null
                ? bodyColliderHolder.GetComponents<BoxCollider>()
                : Array.Empty<BoxCollider>();
            var frontContactColliderValid = bodyColliders.Length >= 3 &&
                Vector3.Distance(bodyColliders[2].center, FrontContactColliderCenter) < 0.001f &&
                Vector3.Distance(bodyColliders[2].size, FrontContactColliderSize) < 0.001f &&
                bodyColliders[2].enabled && !bodyColliders[2].isTrigger;
            if (!TryGetKoenigseggRendererBounds(prefab.transform, out var bounds))
                throw new InvalidOperationException("Koenigsegg visual has no renderer bounds.");
            var bodySidesOriented =
                bounds.size.x > bounds.size.y * 1.4f &&
                bounds.size.z > bounds.size.x * 2f;
            var frontMarker = FindTransformWithNameFragment(visual, "HEADLIGHT_LENS_LEFT");
            var rearMarker = FindTransformWithNameFragment(visual, "REARBUMPER_mm_lights");
            var frontFacesVehicleForward =
                frontMarker != null && rearMarker != null &&
                TryGetRendererBounds(frontMarker, out var frontMarkerBounds) &&
                TryGetRendererBounds(rearMarker, out var rearMarkerBounds) &&
                frontMarkerBounds.center.z > rearMarkerBounds.center.z + 2f;
            var windshield = FindTransformWithNameFragment(visual, "GLASS_FRONT");
            var exhaust = FindTransformWithNameFragment(visual, "EXHAUST");
            var windshieldHeight = float.NaN;
            var exhaustHeight = float.NaN;
            if (windshield != null && TryGetRendererBounds(windshield, out var windshieldBounds))
                windshieldHeight = windshieldBounds.center.y;
            if (exhaust != null && TryGetRendererBounds(exhaust, out var exhaustBounds))
                exhaustHeight = exhaustBounds.center.y;
            var bodyUpright =
                !float.IsNaN(windshieldHeight) && !float.IsNaN(exhaustHeight) &&
                windshieldHeight > exhaustHeight + 0.05f &&
                bounds.size.y > 1.0f;

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
                if (transform.name.StartsWith("KoenigseggWheel", StringComparison.Ordinal))
                {
                    wheelVisuals++;
                    var tire = FindTransformWithNameFragment(transform, "Tire_");
                    var isFrontWheel = transform.name.IndexOf("Front", StringComparison.Ordinal) >= 0;
                    var expectedWidth = isFrontWheel ? 0.265f : 0.325f;
                    var expectedDiameter = isFrontWheel ? 0.694f : 0.742f;
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

                    if (FindTransformWithNameFragment(transform, "Geometry_") == null)
                    {
                        wheelSideMappingCorrect = false;
                    }
                }
                if (transform.name.StartsWith("KoenigseggFixedCaliper", StringComparison.Ordinal))
                {
                    fixedCalipers++;
                    if (FindTransformWithNameFragment(transform, "_Caliper_") == null)
                        calipersDetachedFromWheels = false;
                    fittedCaliperCenters[transform.name] = transform.position;
                }
                if (transform.name.IndexOf("REARBUMPER_mm_lights", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continuousTailLight = true;
                }
                if (transform.name.IndexOf("REARBUMPER_mm_lights", StringComparison.OrdinalIgnoreCase) >= 0)
                    thirdBrakeLight = true;
                if (transform.name.IndexOf("HEADLIGHT_LENS_LEFT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    transform.name.IndexOf("HEADLIGHT_LENS_RIGHT", StringComparison.OrdinalIgnoreCase) >= 0)
                    frontBlinkerMeshes++;
                if (transform.name.IndexOf("HEADLIGHT_LENS_LEFT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    transform.name.IndexOf("HEADLIGHT_LENS_RIGHT", StringComparison.OrdinalIgnoreCase) >= 0)
                    sideBlinkerMeshes++;
            }

            var frontLeftCenter = Vector3.zero;
            var frontRightCenter = Vector3.zero;
            var rearLeftCenter = Vector3.zero;
            var rearRightCenter = Vector3.zero;
            var wheelPlacementVerified =
                fittedWheelCenters.TryGetValue("KoenigseggWheelFrontLeft", out frontLeftCenter) &&
                fittedWheelCenters.TryGetValue("KoenigseggWheelFrontRight", out frontRightCenter) &&
                fittedWheelCenters.TryGetValue("KoenigseggWheelRearLeft", out rearLeftCenter) &&
                fittedWheelCenters.TryGetValue("KoenigseggWheelRearRight", out rearRightCenter);
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
                frontTrack >= 1.45f && frontTrack <= 1.75f &&
                rearTrack >= 1.40f && rearTrack <= 1.70f &&
                wheelbase >= 2.60f && wheelbase <= 2.85f &&
                Math.Abs(frontLeftCenter.z - frontRightCenter.z) < 0.012f &&
                Math.Abs(rearLeftCenter.z - rearRightCenter.z) < 0.012f;
            var caliperPivotsVerified = wheelPlacementVerified &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "KoenigseggFixedCaliperFrontLeft",
                    frontLeftCenter + CaliperOffset(true, -1f)) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "KoenigseggFixedCaliperFrontRight",
                    frontRightCenter + CaliperOffset(true, 1f)) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "KoenigseggFixedCaliperRearLeft",
                    rearLeftCenter + CaliperOffset(false, -1f)) &&
                CaliperPivotMatches(
                    fittedCaliperCenters,
                    "KoenigseggFixedCaliperRearRight",
                    rearRightCenter + CaliperOffset(false, 1f));

            var transmissionVerified = false;
            var launchResponseVerified = false;
            var antiRollVerified = false;
            var massCenterVerified = false;
            var nativeEngineTemplateValid = false;
            var donorHierarchyRenamed =
                FindTransform(prefab.transform, "VehicleNavMeshObstacle") != null &&
                FindTransform(prefab.transform, "2020_abt_sportline_audi_rs6-r") == null;
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
                var nativeClips = serialized.FindProperty("soundManager")
                    ?.FindPropertyRelative("engineRunningComponent")
                    ?.FindPropertyRelative("clips");
                nativeEngineTemplateValid = nativeClips != null && nativeClips.isArray &&
                                            nativeClips.arraySize == 1 &&
                                            nativeClips.GetArrayElementAtIndex(0)
                                                .objectReferenceValue is AudioClip nativeClip &&
                                            string.Equals(
                                                nativeClip.name,
                                                "EngineLow",
                                                StringComparison.Ordinal);
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
                transmissionVerified = gearCount == 9 && gears != null && gears.arraySize == 11;
                var clutch = powertrain?.FindPropertyRelative("clutch");
                var engine = powertrain?.FindPropertyRelative("engine");
                var powerCurve = engine?.FindPropertyRelative("powerCurve")?.animationCurveValue;
                launchResponseVerified =
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRPM")) - 1400f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("throttleEngagementOffsetRPM")) - 700f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("engagementRange")) - 650f) < 0.01f &&
                    Math.Abs(ReadNumber(clutch?.FindPropertyRelative("creepTorque"))) < 0.01f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("inertia")) - 0.12f) < 0.001f &&
                    Math.Abs(ReadNumber(engine?.FindPropertyRelative("startDuration")) - 0.42f) < 0.001f &&
                    JeskoPowerCurveMatches(powerCurve) &&
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
                if (!KoenigseggJeskoMaterials.IsKoenigseggRenderer(renderer.transform))
                    continue;
                if (!renderer.enabled || renderer.sharedMaterials.Length == 0)
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
                        var expectedRimColor = KoenigseggJeskoMaterials.RimBaseColor;
                        var rimColor = material.HasProperty("_BaseColor")
                            ? material.GetColor("_BaseColor")
                            : Color.clear;
                        rimFinishValid &=
                            material.HasProperty("_Metallic") &&
                            Math.Abs(material.GetFloat("_Metallic") - KoenigseggJeskoMaterials.RimMetallic) < 0.01f &&
                            material.HasProperty("_Smoothness") &&
                            Math.Abs(material.GetFloat("_Smoothness") - KoenigseggJeskoMaterials.RimSmoothness) < 0.01f &&
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
                    if (KoenigseggJeskoMaterials.IsTransparentMaterial(material))
                    {
                        transparentMaterials++;
                        transparentMaterialsDoubleSided &=
                            (!material.HasProperty("_Cull") ||
                             material.GetFloat("_Cull") < 0.5f) &&
                            (!material.HasProperty("_DoubleSidedEnable") ||
                             material.GetFloat("_DoubleSidedEnable") > 0.5f) &&
                            material.IsKeywordEnabled("_DOUBLESIDED_ON");
                        if (KoenigseggJeskoMaterials.IsCabinGlassMaterial(material))
                        {
                            var tint = material.HasProperty("_BaseColor")
                                ? material.GetColor("_BaseColor")
                                : material.HasProperty("baseColorFactor")
                                    ? material.GetColor("baseColorFactor")
                                    : Color.black;
                            cabinGlassTintValid &= tint.r >= 0.04f &&
                                                   tint.a >= 0.10f &&
                                                    tint.a <= 0.30f &&
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
                        bodyFilter.name.IndexOf("BODY_mm_ext", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        bodyFilter.name.IndexOf("BONNETCAM", StringComparison.OrdinalIgnoreCase) < 0 &&
                        bodyFilter.GetComponent<MeshRenderer>()?.enabled == true &&
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

            if (Math.Abs(price - 3500000f) > 0.5f ||
                Math.Abs(maxFuel - 72f) > 0.5f ||
                Math.Abs(maxSpeed - 480f) > 0.5f ||
                Math.Abs(enginePower - EffectiveEnginePowerKw) > 0.5f ||
                !luxury ||
                bounds.size.z < 4.50f || bounds.size.z > 4.72f ||
                bounds.size.x < 1.98f || bounds.size.x > 2.08f ||
                bounds.size.y < 1.15f || bounds.size.y > 1.28f ||
                !visualRideHeightValid ||
                !frontContactColliderValid ||
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
                !headlightTemplateValid ||
                !transmissionVerified ||
                !launchResponseVerified ||
                !nativeEngineTemplateValid ||
                !donorHierarchyRenamed ||
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
                rimSlots != 4 ||
                rimMaterials.Count != 1 ||
                !rimFinishValid ||
                !paintReferencesValid)
            {
                throw new InvalidOperationException(
                    $"Bundle verification failed: price={price}, fuel={maxFuel}, " +
                    $"speed={maxSpeed}, power={enginePower}, luxury={luxury}, " +
                    $"bounds={bounds.size}, bodySidesOriented={bodySidesOriented}, " +
                    $"visualRideHeight={visualRideHeightValid}, " +
                    $"frontContactCollider={frontContactColliderValid}/{bodyColliders.Length}, " +
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
                    $"nineSpeed={transmissionVerified}, launchResponse={launchResponseVerified}, " +
                    $"nativeEngineTemplate={nativeEngineTemplateValid}, " +
                    $"donorHierarchyRenamed={donorHierarchyRenamed}, " +
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

            var runtimeBrakeFaces = VerifyRuntimeLampOverlays(prefab);

            Debug.Log(
                $"KoenigseggJesko bundle verified: price={price}, speed={maxSpeed}, " +
                $"power={enginePower}, bounds={bounds.size}, wheels=4, nineSpeed=true, " +
                $"visualRideHeight=+7cm, frontContactCollider=true, " +
                $"fixedCalipers=4, steeringCaliperPivots=true, tireBoundsCentered=true, wheelbase={wheelbase:F3}, " +
                $"frontTrack={frontTrack:F3}, rearTrack={rearTrack:F3}, " +
                $"stableCenterOfMass=true, tireFriction={TireFrictionCircleStrength:F2}, " +
                $"suspensionTravel={FrontSuspensionTravel:F2}/{RearSuspensionTravel:F2}, " +
                $"damageBody=in-place-authored-shell, deformation={DeformationStrength:F2}/{DeformationRadius:F2}, " +
                $"launchResponse=true, nativeEngineTemplate=Jesko, donorHierarchy=neutral, " +
                $"centerRearRunningLight=true, segmentedBrakeLights=true/{runtimeBrakeFaces}faces, blinkers=4, " +
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

    private static int VerifyRuntimeLampOverlays(GameObject prefab)
    {
        var instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "KoenigseggJesko_LightingVerification";
        try
        {
            Type? lightingType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                lightingType = assembly.GetType("KoenigseggJeskoLightingController", false);
                if (lightingType != null)
                    break;
            }
            if (lightingType == null)
                throw new InvalidOperationException(
                    "Runtime lighting verification cannot resolve the Jesko lighting controller.");
            var initialize = lightingType.GetMethod(
                "Initialize",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic) ??
                throw new InvalidOperationException(
                    "Runtime lighting verification cannot resolve Initialize().");
            var parameterType = initialize.GetParameters()[0].ParameterType;
            var vehicle = instance.GetComponentInChildren(parameterType, true);
            if (vehicle == null)
                throw new InvalidOperationException(
                    $"Runtime lighting verification has no '{parameterType.FullName}' component.");
            var lighting = instance.AddComponent(lightingType);
            initialize.Invoke(lighting, new object?[] { vehicle, null });
            var left = FindTransform(instance.transform, "KoenigseggJesko_RearBrakeSignatureLeft")
                       ?.GetComponent<MeshFilter>()?.sharedMesh;
            var right = FindTransform(instance.transform, "KoenigseggJesko_RearBrakeSignatureRight")
                        ?.GetComponent<MeshFilter>()?.sharedMesh;
            var center = FindTransform(instance.transform, "KoenigseggJesko_CenterRearRunningLight");
            var leftIndicator = FindTransform(instance.transform, "KoenigseggJesko_RearLeftIndicator");
            var rightIndicator = FindTransform(instance.transform, "KoenigseggJesko_RearRightIndicator");
            var faceCount = (left?.triangles.Length ?? 0) / 3 +
                            (right?.triangles.Length ?? 0) / 3;
            if (left == null || right == null || center == null ||
                leftIndicator == null || rightIndicator == null ||
                faceCount <= 0 || faceCount >= 160)
            {
                throw new InvalidOperationException(
                    $"Runtime rear-lamp verification failed: brakeFaces={faceCount}, " +
                    $"center={center != null}, indicators={leftIndicator != null}/{rightIndicator != null}.");
            }
            return faceCount;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
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
                throw new InvalidOperationException("Could not create the Koenigsegg VehicleType asset.");
            target = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(VehicleAssetPath);
        }
        else
        {
            EditorUtility.CopySerialized(source, target);
        }

        if (target == null)
            throw new InvalidOperationException("Generated Koenigsegg VehicleType asset did not load.");

        target.name = "KoenigseggJesko";
        var serialized = new SerializedObject(target);
        SetString(serialized, "vehicleTypeName", VehicleTypeName);
        SetNumber(serialized, "price", 3500000f);
        SetNumber(serialized, "maxFuel", 72f);
        SetNumber(serialized, "maxCargoCapacity", 2f);
        SetNumber(serialized, "maxSpeed", 480f);
        SetNumber(serialized, "enginePower", EffectiveEnginePowerKw);
        SetNumber(serialized, "brakeForce", BrakeTorque);
        SetNumber(serialized, "turnRadius", 27f);
        SetNumber(serialized, "damageIntensity", 0.336f);
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
            throw new InvalidOperationException("Koenigsegg GLB did not import as a prefab.");

        var root = UnityEngine.Object.Instantiate(source);
        root.name = "KoenigseggJesko";
        try
        {
            StripAudiGeometry(root);
            RemoveAudiSpecificBehaviours(root);
            ConfigureRootPhysics(root);
            ConfigureWheelControllers(root);
            ConfigureBodyColliders(root);
            ConfigureVehicleReferences(root, vehicleType);
            ConfigurePowertrain(root);
            ConfigureNativeAudioTemplate(root);

            var modelInstance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
            if (modelInstance == null)
                throw new InvalidOperationException("Could not instantiate the Koenigsegg model.");
            PrefabUtility.UnpackPrefabInstance(
                modelInstance,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            modelInstance.name = "KoenigseggVisual";
            DisableFallbackChassis(modelInstance);
            DisableBonnetCameraGeometry(modelInstance);
            RemoveModelLights(modelInstance);
            NormalizeModel(modelInstance);
            // Body ride-height correction is visual-only. Keep every wheel,
            // tire, rotor, caliper, and wheel-controller anchor unchanged.
            modelInstance.transform.localPosition += Vector3.up * BodyVisualHeightOffset;
            ConfigureExitMarkers(root, modelInstance);
            AssignPersistentMaterials(modelInstance);
            AttachWheelVisuals(root, modelInstance);
            var damageBody = CreateDeformableBody(root, modelInstance);
            ConfigureVehicleDeformation(root, damageBody);
            var fix = KoenigseggJeskoMaterials.FixSolidMaterials(root);
            var rimMaterialsConfigured = ConfigureRimFinish(root);
            MarkMaterialsDirty(root);
            ConfigureRendererReferences(root);

            Debug.Log(
                $"KoenigseggJesko: prepared decal-safe materials renderers={fix.RendererCount}, " +
                $"decalMasksCleared={fix.DecalMasksCleared}, " +
                $"opaqueFixed={fix.OpaqueMaterialsFixed}, " +
                $"transparentFixed={fix.TransparentMaterialsFixed}, " +
                $"cabinGlass={fix.CabinGlassRenderers}/" +
                $"reenabled={fix.CabinGlassRenderersReenabled}, " +
                $"rimMaterialsConfigured={rimMaterialsConfigured}, " +
                $"hdrpValidated={fix.MaterialsValidated}.");

            var result = PrefabUtility.SaveAsPrefabAsset(root, VehiclePrefabPath);
            if (result == null)
                throw new InvalidOperationException("Could not save the Koenigsegg vehicle prefab.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void StripAudiGeometry(GameObject root)
    {
        var donorModelRoot = FindTransform(root.transform, "2020_abt_sportline_audi_rs6-r");
        if (donorModelRoot != null)
            donorModelRoot.name = "VehicleNavMeshObstacle";

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

    private static void DisableFallbackChassis(GameObject model)
    {
        var disabled = 0;
        foreach (var transform in model.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.IndexOf("LOD_B_CHASSIS", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            transform.gameObject.SetActive(false);
            foreach (var renderer in transform.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            disabled++;
        }

        if (disabled == 0)
            Debug.LogWarning("KoenigseggJesko: no LOD_B_CHASSIS fallback was found in the imported model.");
        else
            Debug.Log($"KoenigseggJesko: disabled {disabled} fallback LOD_B chassis object(s).");
    }

    private static void DisableBonnetCameraGeometry(GameObject model)
    {
        var disabled = 0;
        foreach (var transform in model.GetComponentsInChildren<Transform>(true))
        {
            if (transform.name.IndexOf("BONNETCAM", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            transform.gameObject.SetActive(false);
            foreach (var renderer in transform.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = false;
            disabled++;
        }

        Debug.Log($"KoenigseggJesko: disabled bonnet-camera geometry groups={disabled}.");
    }

    private static void ConfigureRootPhysics(GameObject root)
    {
        var body = root.GetComponent<Rigidbody>() ??
                   throw new InvalidOperationException("Reference prefab has no Rigidbody.");
        body.mass = 1420f;
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
            SetNumber(serialized, "baseMass", 1420f);
            SetNumber(serialized, "combinedMass", 1420f);
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
                SetRelativeNumber(serialized, "wheel.radius", isFront ? 0.347f : 0.371f);
                SetRelativeNumber(serialized, "wheel.width", isFront ? 0.265f : 0.325f);
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
        colliders[0].size = new Vector3(1.96f, 0.50f, 4.48f);
        colliders[1].center = new Vector3(0f, 0.82f, -0.10f);
        colliders[1].size = new Vector3(1.74f, 0.58f, 2.35f);
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
        var steeringWheel = FindTransformWithNameFragment(model.transform, "STEERING_WHEEL") ??
                            throw new InvalidOperationException("Model steering wheel is missing.");
        var steeringPosition = root.transform.InverseTransformPoint(steeringWheel.position);
        // The supplied Jesko is left-hand drive. Keep the marker on the
        // steering-wheel side even if a stale reference prefab had opposite
        // marker positions when this setup script is rerun.
        const float driverSide = -1.45f;
        const float passengerSide = 1.45f;
        SetLocalPosition(root, "Driverside", new Vector3(driverSide, 0.1f, 0f));
        SetLocalPosition(root, "Passengerside", new Vector3(passengerSide, 0.1f, 0f));
        Debug.Log(
            $"KoenigseggJesko: steering wheel x={steeringPosition.x:F3}; " +
            $"driver exit x={driverSide:F2}; passenger exit x={passengerSide:F2}.");
    }

    private static void ConfigureNativeAudioTemplate(GameObject root)
    {
        var fallbackClip = AssetDatabase.LoadAssetAtPath<AudioClip>(NativeEngineFallbackClipPath) ??
                           throw new InvalidOperationException(
                               "Koenigsegg native engine fallback clip is missing.");
        var configured = false;
        foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (component == null ||
                !string.Equals(
                    component.GetType().FullName,
                    "NWH.VehiclePhysics2.VehicleController",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var serialized = new SerializedObject(component);
            var clips = serialized.FindProperty("soundManager")
                ?.FindPropertyRelative("engineRunningComponent")
                ?.FindPropertyRelative("clips");
            if (clips == null || !clips.isArray)
                continue;

            clips.arraySize = 1;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = fallbackClip;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            configured = true;
        }

        if (!configured)
            throw new InvalidOperationException("Vehicle engine audio template could not be configured.");
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
                SetRelativeNumber(serialized, "powertrain.engine.inertia", 0.12f);
                SetRelativeNumber(serialized, "powertrain.engine.maxPower", EffectiveEnginePowerKw);
                SetRelativeNumber(serialized, "brakes.maxTorque", BrakeTorque);
                var powerCurve = FindRelativeProperty(serialized, "powertrain.engine.powerCurve");
                if (powerCurve?.propertyType != SerializedPropertyType.AnimationCurve)
                    throw new InvalidOperationException("Reference engine power curve is missing.");
                powerCurve.animationCurveValue = CreateJeskoPowerCurve();
                SetRelativeNumber(serialized, "powertrain.engine.idleRPM", 900f);
                SetRelativeNumber(serialized, "powertrain.engine.revLimiterRPM", 8500f);
                SetRelativeNumber(serialized, "powertrain.engine.startDuration", 0.42f);
                SetRelativeBool(serialized, "powertrain.engine.stallingEnabled", false);
                SetRelativeBool(serialized, "powertrain.engine.forcedInduction.useForcedInduction", false);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.powerGainMultiplier", 1f);
                SetRelativeNumber(serialized, "powertrain.engine.forcedInduction.spoolUpTime", 0f);
                SetRelativeNumber(serialized, "powertrain.transmission.finalGearRatio", 3.25f);
                SetRelativeNumber(serialized, "powertrain.transmission.forwardGearCount", 9f);
                SetRelativeNumber(serialized, "powertrain.transmission.reverseGearCount", 1f);
                SetRelativeNumber(serialized, "powertrain.transmission.shiftDuration", 0.065f);
                SetRelativeNumber(serialized, "powertrain.transmission._downshiftRPM", 5000f);
                SetRelativeNumber(serialized, "powertrain.transmission._upshiftRPM", 7600f);
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
                gears.arraySize = JeskoGears.Length;
                for (var index = 0; index < JeskoGears.Length; index++)
                    gears.GetArrayElementAtIndex(index).floatValue = JeskoGears[index];
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
        // The imported GLB's nested wrapper exposes the authored longitudinal
        // axis on its source Y axis. The -90-degree X correction makes that
        // axis the game's +Z forward axis and keeps +Y upright. A yaw-only
        // correction leaves the chassis on its side, while combining the old
        // X correction with a yaw adds a 180-degree roll and flips the car.
        model.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        model.transform.localScale = Vector3.one;

        if (!TryGetModelBodyBounds(model.transform, out var bounds))
            throw new InvalidOperationException("Koenigsegg model contains no renderers.");

        if (!TryGetModelBodyBounds(model.transform, out bounds) || bounds.size.z <= 0.001f)
            throw new InvalidOperationException("Koenigsegg model length could not be measured.");

        var scale = new Vector3(
            TargetWidth / bounds.size.x,
            TargetLength / bounds.size.z,
            TargetHeight / bounds.size.y);
        model.transform.localScale = scale;
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Scaled Koenigsegg bounds could not be measured.");

        model.transform.position += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        if (!TryGetModelBodyBounds(model.transform, out bounds))
            throw new InvalidOperationException("Final Koenigsegg bounds could not be measured.");

        Debug.Log(
            $"KoenigseggJesko: normalized supplied GLB scale={scale}, " +
            $"bounds={bounds.size}, center={bounds.center}.");
    }

    private static void AttachWheelVisuals(GameObject root, GameObject model)
    {
        var mapping = new Dictionary<string, string>
        {
            { "LOD_A_WHEEL_mm_wheel", "FrontRight_WheelController" },
            { "jesko:LOD_A_WHEEL_mm_wheel", "FrontLeft_WheelController" },
            { "LOD_A_WHEEL_mm_wheel2", "RearRight_WheelController" },
            { "LOD_A_WHEEL_mm_wheel1", "RearLeft_WheelController" },
        };

        var tireNames = new Dictionary<string, string>
        {
            { "LOD_A_WHEEL_mm_wheel", "LOD_A_TYRE_mm_tyre" },
            { "jesko:LOD_A_WHEEL_mm_wheel", "jesko:LOD_A_TYRE_mm_tyre" },
            { "LOD_A_WHEEL_mm_wheel2", "LOD_A_TYRE_mm_tyre2" },
            { "LOD_A_WHEEL_mm_wheel1", "LOD_A_TYRE_mm_tyre1" },
        };
        var rotorNames = new Dictionary<string, string>
        {
            { "LOD_A_WHEEL_mm_wheel", "LOD_A_ROTOR_mm_rotor" },
            { "jesko:LOD_A_WHEEL_mm_wheel", "jesko:LOD_A_ROTOR_mm_rotor" },
            { "LOD_A_WHEEL_mm_wheel2", "LOD_A_ROTOR_mm_rotor2" },
            { "LOD_A_WHEEL_mm_wheel1", "LOD_A_ROTOR_mm_rotor1" },
        };
        var caliperNames = new Dictionary<string, string>
        {
            { "LOD_A_WHEEL_mm_wheel", "jesko:LOD_A_BRAKE_CALIPER_FRONT_RIGHT_mm_misc" },
            { "jesko:LOD_A_WHEEL_mm_wheel", "jesko:LOD_A_BRAKE_CALIPER_FRONT_LEFT_mm_misc" },
            { "LOD_A_WHEEL_mm_wheel2", "jesko:LOD_A_BRAKE_CALIPER_REAR_RIGHT_mm_misc" },
            { "LOD_A_WHEEL_mm_wheel1", "jesko:LOD_A_BRAKE_CALIPER_REAR_LEFT_mm_misc" },
        };

        foreach (var pair in mapping)
        {
            var wheel = FindTransform(model.transform, pair.Key) ??
                        throw new InvalidOperationException($"Model wheel '{pair.Key}' is missing.");
            var controller = FindTransform(root.transform, pair.Value) ??
                             throw new InvalidOperationException($"Wheel controller '{pair.Value}' is missing.");
            var isFront = pair.Value.StartsWith("Front", StringComparison.Ordinal);
            var radius = isFront ? 0.347f : 0.371f;
            var width = isFront ? 0.265f : 0.325f;
            var tire = FindTransform(model.transform, tireNames[pair.Key]) ??
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
            var forwardOffset = isFront ? FrontWheelForwardOffset : RearWheelForwardOffset;
            var rearwardOffset = isFront ? FrontWheelRearwardOffset : RearWheelRearwardOffset;
            controller.localPosition = new Vector3(
                authoredCenter.x + side * outset,
                radius + WheelGroundingOffset +
                    (isFront ? FrontWheelHeightOffset : 0f),
                authoredCenter.z + forwardOffset + rearwardOffset);
            var mount = new GameObject(
                "KoenigseggWheel" +
                pair.Value.Replace("_WheelController", "").Replace("_", string.Empty));
            mount.transform.SetParent(root.transform, false);
            mount.transform.localPosition =
                new Vector3(
                    controller.localPosition.x,
                    radius + WheelGroundingOffset +
                        (isFront ? FrontWheelHeightOffset : 0f),
                    controller.localPosition.z);

            // Each source corner is bound to the controller on the same signed
            // vehicle X side. Swapping sides exposes the authored inner rim face.
            wheel.SetParent(mount.transform, true);
            wheel.name = "Geometry_" + pair.Key;
            var rotor = FindTransform(model.transform, rotorNames[pair.Key]) ??
                        throw new InvalidOperationException(
                            $"Model wheel '{pair.Key}' has no dedicated brake rotor.");
            rotor.SetParent(mount.transform, true);
            rotor.name = "Rotor_" + pair.Key;
            tire.SetParent(mount.transform, true);
            tire.name = "Tire_" + pair.Key;

            // Fit and center from the tire alone. The authored brake caliper is
            // deliberately off-axis, so including it in the aggregate bounds
            // shifts and distorts the complete rotating assembly.
            mount.transform.localScale = new Vector3(
                width / tireBounds.size.x,
                (radius * 2f) / tireBounds.size.y,
                (radius * 2f) / tireBounds.size.z);
            if (!TryGetRendererBounds(tire, out tireBounds))
                throw new InvalidOperationException($"Model wheel '{pair.Key}' tire could not be fitted.");
            var wheelCenterCorrection = mount.transform.position - tireBounds.center;
            wheel.position += wheelCenterCorrection;
            rotor.position += wheelCenterCorrection;
            tire.position += wheelCenterCorrection;
            Debug.Log(
                $"KoenigseggJesko: fitted {pair.Key} to authored arch " +
                $"center=({authoredCenter.x:F3},{radius:F3},{authoredCenter.z:F3}), " +
                $"tire={width:F3}x{radius * 2f:F3}m.");

            // The rotor remains part of the rolling visual, while the caliper
            // keeps its fitted world pose under a chassis-owned mount and can no
            // longer inherit wheel spin from NWH's visual transform.
            var caliper = FindTransform(model.transform, caliperNames[pair.Key]) ??
                          throw new InvalidOperationException(
                              $"Model wheel '{pair.Key}' has no brake caliper renderer.");
            var fixedCaliper = new GameObject(
                "KoenigseggFixedCaliper" +
                pair.Value.Replace("_WheelController", "").Replace("_", string.Empty));
            fixedCaliper.transform.SetParent(root.transform, false);
            fixedCaliper.transform.localPosition =
                mount.transform.localPosition + CaliperOffset(isFront, side);
            fixedCaliper.transform.rotation = root.transform.rotation;
            caliper.SetParent(fixedCaliper.transform, true);
            caliper.name = "Jesko_Caliper_" + pair.Key;

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
            if (renderer.name.IndexOf("BODY_mm_ext", StringComparison.OrdinalIgnoreCase) >= 0 &&
                renderer.name.IndexOf("BONNETCAM", StringComparison.OrdinalIgnoreCase) < 0)
            {
                sourceRenderer = renderer;
                break;
            }
        }

        var sourceFilter = sourceRenderer?.GetComponent<MeshFilter>();
        if (sourceRenderer == null || sourceFilter?.sharedMesh == null)
            throw new InvalidOperationException("The Koenigsegg outer body mesh was not found.");

        // Preserve the body renderer in its authored hierarchy. The custom
        // runtime deformation controller works in world space and clones this
        // mesh per instance, so a second root-local baked shell is unnecessary
        // and cannot drift or invert relative to the remaining body panels.
        sourceRenderer.enabled = true;
        return sourceFilter;
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
        material.name.IndexOf("Exterior_mm_ext1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorAccentPaintMaterial(Material material) =>
        material.name.IndexOf("Tela_Int", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRimMaterial(Material material) =>
        material.name.IndexOf("Exterior_mm_wheel1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static int ConfigureRimFinish(GameObject root)
    {
        Material? leftMaterial = null;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!KoenigseggJeskoMaterials.IsKoenigseggRenderer(renderer.transform))
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
            throw new InvalidOperationException("The shared Koenigsegg rim material is missing.");

        ApplyRimFinish(leftMaterial, KoenigseggJeskoMaterials.RimBaseColor);

        var configuredSlots = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!KoenigseggJeskoMaterials.IsKoenigseggRenderer(renderer.transform))
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
                $"Expected four Koenigsegg rim slots, found {configuredSlots}.");
        return configuredSlots;
    }

    private static void ApplyRimFinish(Material material, Color baseColor)
    {
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        if (material.HasProperty("baseColorFactor")) material.SetColor("baseColorFactor", baseColor);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", KoenigseggJeskoMaterials.RimMetallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", KoenigseggJeskoMaterials.RimSmoothness);
        EditorUtility.SetDirty(material);
    }

    private static bool IsCaliperMaterial(Material material) =>
        material.name.IndexOf("Exterior_mm_misc1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool CaliperPivotMatches(
        IReadOnlyDictionary<string, Vector3> centers,
        string name,
        Vector3 wheelCenter) =>
        centers.TryGetValue(name, out var center) &&
        Vector3.Distance(center, wheelCenter) < 0.005f;

    private static Vector3 CaliperOffset(bool isFront, float side) =>
        isFront
            ? new Vector3(
                side * FrontCaliperOutset,
                FrontCaliperHeightOffset,
                FrontCaliperLongitudinalOffset)
            : new Vector3(
                side * RearCaliperOutset,
                RearCaliperHeightOffset,
                RearCaliperLongitudinalOffset);

    private static bool JeskoPowerCurveMatches(AnimationCurve? curve)
    {
        var expected = CreateJeskoPowerCurve();
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
        material.name.IndexOf("Exterior_mm_rotor1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsSeatPaintMaterial(Material material) =>
        material.name.IndexOf("Tela_Int", StringComparison.OrdinalIgnoreCase) >= 0;

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
        material.name.IndexOf("Exterior_mm_chassis1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorPrimaryPaintMaterial(Material material) =>
        material.name.IndexOf("Exterior_mm_cab1", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorSecondaryPaintMaterial(Material material) =>
        material.name.IndexOf("material", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsInteriorDarkPaintMaterial(Material material) =>
        material.name.IndexOf("CARBON", StringComparison.OrdinalIgnoreCase) >= 0;

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
                    var transparent = KoenigseggJeskoMaterials.IsTransparentMaterial(source);
                    var kind = transparent ? "Transparent" : "Opaque";
                    var materialIndex = transparent
                        ? transparentMaterialIndex++
                        : opaqueMaterialIndex++;
                    var assetName =
                        $"Koenigsegg{kind}_{materialIndex:D2}_{SanitizeAssetName(source.name)}";
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

        manifest.ModId = "KoenigseggJesko";
        manifest.DisplayName = "Koenigsegg Jesko";
        manifest.Author = "Dudeldups";
        manifest.Version = "0.1.0";
        manifest.AssetBundleName = "koenigseggjesko.unity3d";
        manifest.ModAssembly = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(AssemblyPath);
        manifest.LocalesFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(LocalesPath);
        manifest.DependenciesFolder = null;
        manifest.EnumsFile = null;
        manifest.TargetPlatforms = ModTargetPlatforms.Windows;

        if (manifest.ModAssembly == null || manifest.LocalesFolder == null)
            throw new InvalidOperationException("Koenigsegg manifest references could not be assigned.");
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
                if (current.name.IndexOf("WHEEL_mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("TYRE_mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("ROTOR_mm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.IndexOf("BRAKE_CALIPER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    current.name.StartsWith("KoenigseggWheel", StringComparison.Ordinal))
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

    private static bool TryGetKoenigseggRendererBounds(Transform root, out Bounds bounds)
    {
        var found = false;
        bounds = default;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!KoenigseggJeskoMaterials.IsKoenigseggRenderer(renderer.transform))
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

public sealed class KoenigseggJeskoImpactVerificationProbe : MonoBehaviour
{
    public bool Contacted;
    public Vector3 RelativeVelocity;
    public Vector3 Normal;
    public float Impulse;
    private void OnCollisionEnter(Collision collision)
    {
        Contacted = true;
        RelativeVelocity = collision.relativeVelocity;
        Normal = collision.GetContact(0).normal;
        Impulse = collision.impulse.magnitude;
    }
}
