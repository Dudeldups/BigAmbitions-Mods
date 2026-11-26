#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Vehicles.VehicleTypes;

public static class LamborghiniAventadorAssetChecks
{
    public static void VerifyPaintRefreshIdempotence()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = UnityEngine.Object.Instantiate(prefab);
        var color = ScriptableObject.CreateInstance<Data.VehicleColors.VehicleColor>();
        var second = UnityEngine.Object.Instantiate(prefab);
        var secondColor = ScriptableObject.CreateInstance<Data.VehicleColors.VehicleColor>();
        Material? normalizedInterior = null;
        Material? dealerInterior = null;
        try
        {
            color.name = "Aventador_Paint_Regression";
            color.tint = new Color32(83, 141, 207, 255);
            var vehicle = instance.GetComponent<VehicleController>();
            var serialized = new SerializedObject(vehicle);
            var savedColor = serialized.FindProperty("vehicleInstance.vehicleColorName");
            if (savedColor != null) savedColor.stringValue = string.Empty;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var type = typeof(LamborghiniAventadorSpawnAppearance).Assembly
                .GetType("LamborghiniAventadorPaintController", true)!;
            var glassType = type.Assembly.GetType("LamborghiniAventadorGlassController", true)!;
            var role = Enum.Parse(glassType.GetNestedType("MaterialRole", BindingFlags.NonPublic)!, "Opaque");
            var normalize = glassType.GetMethod("NormalizeRuntimeMaterial", BindingFlags.Static | BindingFlags.NonPublic)!;
            Renderer GetInterior(GameObject target) => target.GetComponentsInChildren<Renderer>(true)
                .Single(item => item.sharedMaterial?.name == "AventadorInteriorAccent");
            var interiorRenderer = GetInterior(instance);
            var secondInterior = GetInterior(second);
            normalizedInterior = new Material(interiorRenderer.sharedMaterial) { name = "AventadorInteriorAccent_RuntimeAppearance" };
            normalize.Invoke(null, new[] { normalizedInterior, role });
            if (normalizedInterior.shader.name != "HDRP/Lit")
                throw new InvalidOperationException("Aventador interior paint test did not normalize the production material.");
            dealerInterior = new Material(normalizedInterior) { name = normalizedInterior.name + " (Instance)" };
            interiorRenderer.sharedMaterial = dealerInterior;
            secondInterior.sharedMaterial = normalizedInterior;

            var paint = instance.AddComponent(type);
            var initialize = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(method => method.Name == "InitializeForAmbientTraffic" && method.GetParameters().Length == 3);
            initialize.Invoke(paint, new object?[] { vehicle, color, null });
            var secondPaint = second.AddComponent(type);
            secondColor.tint = new Color32(166, 220, 35, 255);
            initialize.Invoke(secondPaint, new object?[] { second.GetComponent<VehicleController>(), secondColor, null });
            var svjRenderer = instance.GetComponentsInChildren<MeshRenderer>(true)
                .Single(item => item.name == "Object_58" &&
                    item.sharedMaterials.Any(material => material != null && material.name.StartsWith("PAINT_BLACK")));
            var svjMesh = svjRenderer.GetComponent<MeshFilter>().sharedMesh;
            if (svjMesh.subMeshCount != 2 || svjMesh.GetTriangles(1).Length != 562 * 3 ||
                svjRenderer.sharedMaterials.Length != 2)
                throw new InvalidOperationException("SVJ lettering was not isolated into its own material slot.");
            var svjColorProperty = svjRenderer.sharedMaterials[1].HasProperty("_BaseColor") ? "_BaseColor" : "baseColorFactor";
            var svjBlock = new MaterialPropertyBlock();
            svjRenderer.GetPropertyBlock(svjBlock, 1);
            if (svjBlock.GetColor(svjColorProperty).r > .1f)
                throw new InvalidOperationException("Light paint did not select dark SVJ lettering.");
            color.tint = Color.black;
            type.GetMethod("RefreshColor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(paint, new object[] { false });
            svjRenderer.GetPropertyBlock(svjBlock, 1);
            if (svjBlock.GetColor(svjColorProperty).r < .9f)
                throw new InvalidOperationException("Dark paint did not select light SVJ lettering.");
            color.tint = new Color32(83, 141, 207, 255);
            type.GetMethod("RefreshColor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(paint, new object[] { false });
            var revision = type.GetProperty("AppliedRevision", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var refresh = type.GetMethod("RefreshColor", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var first = (int)revision.GetValue(paint)!;
            if (first != 3) throw new InvalidOperationException("Aventador paint test did not apply and restore its tint.");
            var atlasProperty = Shader.PropertyToID("_BaseColorMap");
            Texture2D ReadInteriorAtlas(Renderer renderer)
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, 0);
                return (Texture2D)block.GetTexture(atlasProperty);
            }

            var blueTrim = ReadInteriorAtlas(interiorRenderer);
            var greenTrim = ReadInteriorAtlas(secondInterior);
            var source = (Texture2D)normalizedInterior.GetTexture(atlasProperty);
            if (blueTrim == null || greenTrim == null || blueTrim == greenTrim || source == blueTrim)
                throw new InvalidOperationException("Aventador interior paint is not isolated per tint.");
            var originalPixels = source.GetPixels32();
            var bluePixels = blueTrim.GetPixels32();
            var greenPixels = greenTrim.GetPixels32();
            var changedAccentPixels = 0;
            var changedCrestPixels = 0;
            for (var index = 0; index < originalPixels.Length; index++)
            {
                var original = originalPixels[index];
                var x = index % source.width;
                var y = source.height - 1 - index / source.width;
                var inHeadrestCrest = x >= 282 && x < 390 && y >= 302 && y < 422;
                var inAccentRegion = inHeadrestCrest || (x >= 291 && x < 312 && y >= 193 && y < 238) ||
                    (x >= 60 && x < 282 && y >= 341 && y < 351) ||
                    (x >= 451 && y >= 452 && y < 463);
                if (original.a != 255 || bluePixels[index].a != 255 || greenPixels[index].a != 255)
                    throw new InvalidOperationException("Aventador interior atlas must stay opaque.");
                if (inAccentRegion && original.r > Mathf.Max(original.g, original.b))
                {
                    if (!bluePixels[index].Equals(greenPixels[index]))
                    {
                        if (inHeadrestCrest) changedCrestPixels++;
                        else changedAccentPixels++;
                    }
                    continue;
                }
                if (!bluePixels[index].Equals(original) || !greenPixels[index].Equals(original))
                    throw new InvalidOperationException("Aventador interior paint changed a fixed atlas texel.");
            }
            if (changedAccentPixels < 1000)
                throw new InvalidOperationException("Aventador interior paint did not tint the authored accents.");
            if (changedCrestPixels < 3000)
                throw new InvalidOperationException("Aventador interior paint did not tint the headrest crest.");

            for (var index = 0; index < 1000; index++) refresh.Invoke(paint, new object[] { false });
            if ((int)revision.GetValue(paint)! != first)
                throw new InvalidOperationException("Aventador paint test rewrote an unchanged renderer property block.");
            var renderer = instance.GetComponentsInChildren<Renderer>(true)
                .First(item => item.sharedMaterials.Any(material => material != null && material.name.Contains("RED_PAINT")));
            var slot = Array.FindIndex(renderer.sharedMaterials, material => material != null && material.name.Contains("RED_PAINT"));
            renderer.SetPropertyBlock(null, slot);
            interiorRenderer.SetPropertyBlock(null, 0);
            refresh.Invoke(paint, new object[] { false });
            if ((int)revision.GetValue(paint)! != first + 1 || ReadInteriorAtlas(interiorRenderer) != blueTrim)
                throw new InvalidOperationException("Aventador paint test failed to restore a cleared renderer property block.");
            color.tint = new Color32(237, 58, 154, 255);
            refresh.Invoke(paint, new object[] { false });
            if (ReadInteriorAtlas(interiorRenderer) == blueTrim || ReadInteriorAtlas(secondInterior) != greenTrim)
                throw new InvalidOperationException("Aventador repaint failed to change only the selected vehicle's trim.");
            Debug.Log($"Aventador paint refresh verified: {changedAccentPixels} accent texels and {changedCrestPixels} crest texels tinted; " +
                "fixed texels and second tint preserved; " +
                "1000 unchanged callbacks without writes, cleared paint blocks restored once.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
            UnityEngine.Object.DestroyImmediate(second);
            UnityEngine.Object.DestroyImmediate(color);
            UnityEngine.Object.DestroyImmediate(secondColor);
            if (dealerInterior != null) UnityEngine.Object.DestroyImmediate(dealerInterior);
            if (normalizedInterior != null) UnityEngine.Object.DestroyImmediate(normalizedInterior);
        }
    }

    private const string Root = "Assets/Mods/Lamborghini_Aventador/";
    private const string PrefabPath = Root + "LamborghiniAventador.prefab";
    private const string WindowsBundlePath = Root + "AssetBundles/Windows/lamborghiniaventador.unity3d";
    private const string TypePath = Root + "LamborghiniAventador.asset";
    private const string ManifestPath = Root + "ModManifest.asset";
    private const string DependenciesPath = Root + "Dependencies";
    private const string GleyLampMaterialPath = Root + "Shaders/LamborghiniAventadorGleyLamp.mat";
    private const string WheelMeshesPath = Root + "Generated/LamborghiniAventadorWheelMeshes.asset";
    private const string EngineLowPath = Root + "Config/Audio/EngineLow.wav";
    private const string RevueltoAudioPath = "Assets/Mods/LamborghiniRevuelto/Config/Audio/";

    [MenuItem("Big Ambitions Mods/Lamborghini Aventador/Verify Auto-Parking Geometry")]
    public static void VerifyAutoParkingGeometry()
    {
        var layer = LayerMask.NameToLayer("ParkingArea");
        if (layer < 0)
            throw new InvalidOperationException("LamborghiniAventador: ParkingArea layer is missing.");

        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            var physics = scene.GetPhysicsScene();
            if (!physics.IsValid())
                throw new InvalidOperationException("LamborghiniAventador: isolated PhysicsScene is unavailable.");

            var lane = new GameObject("SyntheticParkingLane");
            SceneManager.MoveGameObjectToScene(lane, scene);
            lane.layer = layer;
            var laneCollider = lane.AddComponent<BoxCollider>();
            laneCollider.center = new Vector3(0f, 0.1f, 0f);
            laneCollider.size = new Vector3(15.02f, 0.1f, 2.2f);
            Physics.SyncTransforms();

            // Match the installed native wheel probe: wheel position minus 0.1m,
            // radius * 2 vertical half extent, 0.1m horizontal half extents.
            var localWheelPositions = new[]
            {
                new Vector3(-1.26f, 0.331f, 0.86f),
                new Vector3(-1.26f, 0.331f, -0.86f),
                new Vector3(1.44f, 0.355f, 0.84f),
                new Vector3(1.44f, 0.355f, -0.84f),
            };
            var radii = new[] { 0.331f, 0.331f, 0.355f, 0.355f };
            var laneMask = 1 << layer;

            var elevated = EvaluateSyntheticWheelProbes(physics, localWheelPositions,
                radii, 0.74f, laneMask);
            if (IsCandidateLegal(elevated, new[] { false, false, false, false },
                    hasCollision: false))
                throw new InvalidOperationException(
                    "LamborghiniAventador: native root at y=0.74 unexpectedly passed the four wheel probes.");
            if (EvaluateGroundCorrection(new Vector3(0f, 0.74f, 0f),
                    new Vector3(0f, 0.74f, 0f), out var elevatedCandidate) ||
                Vector3.Distance(elevatedCandidate, new Vector3(0f, 0.74f, 0f)) > 0.001f)
                throw new InvalidOperationException(
                    "LamborghiniAventador: matching elevated ground hit would move/sink the native pose.");

            var grounded = EvaluateSyntheticWheelProbes(physics, localWheelPositions,
                radii, 0.1f, laneMask);
            if (!IsCandidateLegal(grounded, new[] { false, false, false, false },
                    hasCollision: false))
                throw new InvalidOperationException(
                    "LamborghiniAventador: measured ground root at y=0.1 failed the four wheel probes.");
            if (!EvaluateGroundCorrection(new Vector3(0f, 0.74f, 0f),
                    new Vector3(0f, 0.1f, 0f), out var correctedCandidate) ||
                Vector3.Distance(correctedCandidate, new Vector3(0f, 0.1f, 0f)) > 0.001f)
                throw new InvalidOperationException(
                    "LamborghiniAventador: measured ground correction did not resolve to y=0.1.");

            var handicap = new[] { false, false, true, false };
            if (IsCandidateLegal(grounded, handicap, hasCollision: false))
                throw new InvalidOperationException(
                    "LamborghiniAventador: candidate preflight accepted a handicap-lane overlap.");
            if (IsCandidateLegal(grounded, new[] { false, false, false, false },
                    hasCollision: true))
                throw new InvalidOperationException(
                    "LamborghiniAventador: candidate preflight accepted a colliding placement.");
            var bodyObject = new GameObject("SyntheticChassis");
            SceneManager.MoveGameObjectToScene(bodyObject, scene);
            var bodyCollider = bodyObject.AddComponent<BoxCollider>();
            bodyCollider.center = new Vector3(0f, 0.38f, 0f);
            bodyCollider.size = new Vector3(1.84f, 0.42f, 4.55f);
            var obstacle = new GameObject("SyntheticObstacle");
            SceneManager.MoveGameObjectToScene(obstacle, scene);
            obstacle.transform.position = new Vector3(0f, 0.48f, 0f);
            var obstacleCollider = obstacle.AddComponent<BoxCollider>();
            obstacleCollider.size = new Vector3(0.3f, 0.3f, 0.3f);
            var hasChassisPenetration = Physics.ComputePenetration(bodyCollider,
                new Vector3(0f, 0.1f, 0f), Quaternion.identity,
                obstacleCollider, obstacle.transform.position, Quaternion.identity,
                out _, out var penetrationDepth);
            if (!hasChassisPenetration || penetrationDepth <= 0f ||
                IsCandidateLegal(grounded, new[] { false, false, false, false },
                    hasCollision: hasChassisPenetration))
                throw new InvalidOperationException(
                    "LamborghiniAventador: overlapping chassis geometry was not rejected.");
            if (InvokeParkingCandidatePredicate(grounded,
                    new[] { 77, 77, 78, 77 },
                    new[] { "synthetic-neighbourhood", "synthetic-neighbourhood",
                        "synthetic-neighbourhood", "synthetic-neighbourhood" },
                    new[] { false, false, false, false }, hasCollision: false) ||
                InvokeParkingCandidatePredicate(grounded,
                    new[] { 77, 77, 77, 77 },
                    new[] { "synthetic-neighbourhood", "synthetic-neighbourhood",
                        "other-neighbourhood", "synthetic-neighbourhood" },
                    new[] { false, false, false, false }, hasCollision: false))
                throw new InvalidOperationException(
                    "LamborghiniAventador: candidate preflight accepted a different lane or neighbourhood.");

            Debug.Log("LamborghiniAventador synthetic Auto-Parking geometry verified: " +
                      "root y=0.74 misses all four native wheel OBBs; root y=0.1 hits the " +
                      "captured regular lane; handicap and collision candidates are rejected.");
        }
        finally
        {
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static bool[] EvaluateSyntheticWheelProbes(PhysicsScene physics,
        Vector3[] localWheelPositions, float[] radii, float rootY, int laneMask)
    {
        var results = new bool[4];
        var overlaps = new Collider[8];
        for (var index = 0; index < results.Length; index++)
        {
            var wheelPosition = localWheelPositions[index] + Vector3.up * rootY;
            var probeCenter = wheelPosition + Vector3.down * 0.1f;
            var count = physics.OverlapBox(probeCenter,
                new Vector3(0.1f, radii[index] * 2f, 0.1f), overlaps,
                Quaternion.identity, laneMask, QueryTriggerInteraction.UseGlobal);
            results[index] = count > 0 && overlaps[0] != null;
            Array.Clear(overlaps, 0, overlaps.Length);
        }
        return results;
    }

    private static bool IsCandidateLegal(bool[] laneHits, bool[] handicapHits,
        bool hasCollision)
    {
        var laneIds = laneHits.Select(hit => hit ? 77 : 0).ToArray();
        var neighbourhoods = laneHits.Select(hit => hit ? "synthetic-neighbourhood" : string.Empty).ToArray();
        return InvokeParkingCandidatePredicate(laneHits, laneIds, neighbourhoods,
            handicapHits, hasCollision);
    }

    private static bool InvokeParkingCandidatePredicate(bool[] laneHits, int[] laneIds,
        string[] neighbourhoods, bool[] handicapHits, bool hasCollision)
    {
        var guard = typeof(LamborghiniAventadorSpawnAppearance).Assembly.GetType(
            "LamborghiniAventadorAutoParkingGuard", throwOnError: true)!;
        var predicate = guard.GetMethod("IsLegalParkingCandidate",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (predicate == null)
            throw new InvalidOperationException(
                "LamborghiniAventador: pure Auto-Parking candidate predicate was not found.");
        return predicate.Invoke(null, new object[]
        {
            laneHits, laneIds, neighbourhoods, handicapHits, 77,
            "synthetic-neighbourhood", hasCollision,
        }) is true;
    }

    private static bool EvaluateGroundCorrection(Vector3 root, Vector3 ground,
        out Vector3 candidate)
    {
        var guard = typeof(LamborghiniAventadorSpawnAppearance).Assembly.GetType(
            "LamborghiniAventadorAutoParkingGuard", throwOnError: true)!;
        var method = guard.GetMethod("TryGetGroundedCandidate",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (method == null)
            throw new InvalidOperationException(
                "LamborghiniAventador: ground candidate helper was not found.");
        var arguments = new object[] { root, ground, Vector3.up, Vector3.zero, 0f };
        var shouldCorrect = method.Invoke(null, arguments) is true;
        candidate = (Vector3)arguments[3];
        return shouldCorrect;
    }

    private enum ValidationMaterialRole
    {
        BodyPaint,
        CabinGlass,
        RedLampLens,
        OrangeLampLens,
        ClearLampLens,
    }

    [MenuItem("Big Ambitions Mods/Scaffold/Verify LamborghiniAventador Assets")]
    public static void Verify()
    {
        var type = AssetDatabase.LoadAssetAtPath<VehicleType>(TypePath);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (type == null || prefab == null)
            throw new InvalidOperationException("LamborghiniAventador: missing VehicleType asset or prefab.");
        VerifyRuntimeDependency();
        VerifyWarehousePlacement(prefab, type);
        if (!string.Equals(type.vehicleTypeName, "lamborghiniaventador-vehicle:vehicletype_lamborghiniaventador", StringComparison.Ordinal))
            throw new InvalidOperationException("LamborghiniAventador: vehicle type identity mismatch.");
        var typeProperties = new SerializedObject(type);
        var autoParkSupported = typeProperties.FindProperty("autoParkSupported");
        if (autoParkSupported?.propertyType != SerializedPropertyType.Boolean || !autoParkSupported.boolValue)
            throw new InvalidOperationException("LamborghiniAventador: vehicle type must declare native Auto-Parking support.");
        var spawnAppearance = prefab.GetComponent<LamborghiniAventadorSpawnAppearance>();
        if (spawnAppearance == null)
            throw new InvalidOperationException("LamborghiniAventador: saved-color spawn initializer is missing from the player prefab.");
        var gleyLampMaterial = AssetDatabase.LoadAssetAtPath<Material>(GleyLampMaterialPath);
        var gleyLampImporter = AssetImporter.GetAtPath(GleyLampMaterialPath);
        if (gleyLampMaterial == null || gleyLampMaterial.shader == null ||
            !string.Equals(gleyLampMaterial.shader.name, "LamborghiniAventador/GleyTrafficLamp", StringComparison.Ordinal) ||
            gleyLampImporter == null || !string.Equals(gleyLampImporter.assetBundleName, "lamborghiniaventador", StringComparison.Ordinal) ||
            !string.Equals(gleyLampImporter.assetBundleVariant, "unity3d", StringComparison.Ordinal))
            throw new InvalidOperationException("LamborghiniAventador: Gley traffic lamp shader/material must be assigned to its Windows and macOS vehicle bundle.");

        var native = prefab.GetComponent<VehicleController>();
        var nwh = prefab.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        if (native == null || nwh == null || nwh.stateSettings == null)
            throw new InvalidOperationException("LamborghiniAventador: missing native controllers or NWH StateSettings.");
        var nativeProperties = new SerializedObject(native);
        var nativeBodyCollider = nativeProperties.FindProperty("vehicleCollider")?.objectReferenceValue as Collider;
        var nativeBody = Find(prefab.transform, "BodyCollider");
        if (nativeBodyCollider is not BoxCollider primaryBodyCollider || nativeBody == null ||
            primaryBodyCollider.transform != nativeBody || !primaryBodyCollider.enabled || primaryBodyCollider.isTrigger ||
            Mathf.Abs(primaryBodyCollider.size.z - 4.55f) > 0.01f)
            throw new InvalidOperationException("LamborghiniAventador: native VehicleController.vehicleCollider must reference the enabled solid chassis box on BodyCollider.");
        VerifyNwhAudio(nwh);
        var powertrainProperties = new SerializedObject(nwh);
        var configuredPower = powertrainProperties.FindProperty("powertrain.engine.maxPower");
        var configuredPowerCurve = powertrainProperties.FindProperty("powertrain.engine.powerCurve");
        var configuredClutchTorque = powertrainProperties.FindProperty("powertrain.clutch.slipTorque");
        var configuredClutchEngagementRPM = powertrainProperties.FindProperty("powertrain.clutch.engagementRPM");
        var configuredClutchThrottleOffset = powertrainProperties.FindProperty("powertrain.clutch.throttleEngagementOffsetRPM");
        var configuredClutchEngagementRange = powertrainProperties.FindProperty("powertrain.clutch.engagementRange");
        var configuredBrakeTorque = powertrainProperties.FindProperty("brakes.maxTorque");
        var configuredDownshift = powertrainProperties.FindProperty("powertrain.transmission._downshiftRPM");
        var configuredUpshift = powertrainProperties.FindProperty("powertrain.transmission._upshiftRPM");
        var aeroWrapper = prefab.GetComponent<NWH.VehiclePhysics2.Modules.Aerodynamics.AerodynamicsModuleWrapper>();
        var aero = aeroWrapper != null ? aeroWrapper.module : null;
        nwh.powertrain.engine.GetPeakTorque(out var nativePeakTorqueNm, out _);
        const float authoredPeakTorqueNm = 720f;
        var expectedClutchTorque = authoredPeakTorqueNm * LamborghiniAventadorSetup.ClutchTorqueSafetyFactor;
        var powerCurve = configuredPowerCurve?.animationCurveValue;
        var torqueCurveBounded = powerCurve != null && powerCurve.length >= 10;
        var sampledPeakTorqueNm = 0f;
        var sampledPeakTorqueRpm = 0f;
        if (powerCurve != null)
        {
            for (var rpm = 1000f; rpm <= 8700f; rpm += 1f)
            {
                var torqueNm = powerCurve.Evaluate(rpm / 8700f) * 566f * 1000f /
                               (rpm * Mathf.PI * 2f / 60f);
                if (torqueNm > sampledPeakTorqueNm)
                {
                    sampledPeakTorqueNm = torqueNm;
                    sampledPeakTorqueRpm = rpm;
                }
                if (torqueNm > 720.5f)
                {
                    torqueCurveBounded = false;
                    break;
                }
            }
        }
        var torqueAtPeakRpm = powerCurve == null ? 0f :
            powerCurve.Evaluate(6750f / 8700f) * 566f * 1000f /
            (6750f * Mathf.PI * 2f / 60f);
        var powerAtRatedRpm = powerCurve == null ? 0f : powerCurve.Evaluate(8500f / 8700f) * 566f;
        var typeEnginePower = typeProperties.FindProperty("enginePower");
        var typeBrakeForce = typeProperties.FindProperty("brakeForce");
        if (configuredPower?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(configuredPower.floatValue - 566f) > 0.5f ||
            !torqueCurveBounded || Mathf.Abs(torqueAtPeakRpm - 720f) > 1f ||
            Mathf.Abs(sampledPeakTorqueNm - authoredPeakTorqueNm) > 1f ||
            Mathf.Abs(sampledPeakTorqueRpm - 6750f) > 1f ||
            nativePeakTorqueNm < 718f || nativePeakTorqueNm > 721f ||
            Mathf.Abs(powerAtRatedRpm - 566f) > 0.5f ||
            configuredClutchTorque?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(configuredClutchTorque.floatValue - expectedClutchTorque) > 0.5f ||
            configuredClutchEngagementRPM?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(configuredClutchEngagementRPM.floatValue - 1300f) > 0.5f ||
            configuredClutchThrottleOffset?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(configuredClutchThrottleOffset.floatValue - 250f) > 0.5f ||
            configuredClutchEngagementRange?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(configuredClutchEngagementRange.floatValue - 500f) > 0.5f ||
            configuredBrakeTorque?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(configuredBrakeTorque.floatValue - 2600f) > 0.5f ||
            configuredDownshift?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(configuredDownshift.floatValue - LamborghiniAventadorSetup.AutomaticDownshiftRpm) > 0.5f ||
            configuredUpshift?.propertyType != SerializedPropertyType.Float ||
            Mathf.Abs(configuredUpshift.floatValue - LamborghiniAventadorSetup.AutomaticUpshiftRpm) > 0.5f ||
            aero == null || !aero.simulateDrag || aero.simulateDownforce ||
            Mathf.Abs(aero.frontalCd - 0.36f) > 0.001f ||
            (aero.dimensions - new Vector3(2.273f, 1.136f, 4.943f)).sqrMagnitude > 1e-6f ||
            typeEnginePower == null || Mathf.Abs(ReadNumber(typeEnginePower) - 566f) > 0.5f ||
            typeBrakeForce == null || Mathf.Abs(ReadNumber(typeBrakeForce) - 2600f) > 0.5f)
            throw new InvalidOperationException("LamborghiniAventador: powertrain must preserve 566 kW at 8,500 rpm, keep the synthetic torque curve at or below 720 Nm with its peak at 6,750 rpm, use a 1.10x peak-torque clutch with headroom above compensated peak torque, apply 0.85 AWD driveline efficiency and the high-load launch fade, use 8,000/4,200 rpm upshift/downshift thresholds, and retain 2,600 brake torque.");

        VerifyPowertrainCalibration(nwh);

        var wheelCount = prefab.GetComponentsInChildren<Component>(true)
            .Count(component => component != null && component.GetType().Name == "WheelController");
        if (wheelCount != 4)
            throw new InvalidOperationException("LamborghiniAventador: expected four wheel controllers, got " + wheelCount);
        foreach (var wheelController in prefab.GetComponentsInChildren<MonoBehaviour>(true)
                     .Where(component => component != null && component.GetType().Name == "WheelController"))
        {
            var wheelSettings = new SerializedObject(wheelController);
            var forwardGrip = wheelSettings.FindProperty("forwardFriction.grip");
            var expectedGrip = wheelController.transform.name.StartsWith("Front", StringComparison.Ordinal)
                ? 0.8f : 1.0f;
            if (forwardGrip?.propertyType != SerializedPropertyType.Float ||
                Mathf.Abs(forwardGrip.floatValue - expectedGrip) > 0.01f)
                throw new InvalidOperationException(
                    $"LamborghiniAventador: wheel '{wheelController.transform.name}' has an invalid longitudinal grip setting.");
        }

        foreach (var wheelName in new[] { "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" })
            if (Find(prefab.transform, wheelName) == null)
                throw new InvalidOperationException("LamborghiniAventador: missing rolling wheel visual " + wheelName);
        foreach (var controllerName in new[] { "FrontLeft_WheelController", "FrontRight_WheelController", "RearLeft_WheelController", "RearRight_WheelController" })
            if (Find(prefab.transform, controllerName) == null)
                throw new InvalidOperationException("LamborghiniAventador: missing native wheel controller transform " + controllerName);
        var frontLeft = Find(prefab.transform, "FrontLeft_WheelController")!;
        var frontRight = Find(prefab.transform, "FrontRight_WheelController")!;
        var rearLeft = Find(prefab.transform, "RearLeft_WheelController")!;
        var rearRight = Find(prefab.transform, "RearRight_WheelController")!;
        if (frontLeft.localPosition.x >= 0f || rearLeft.localPosition.x >= 0f ||
            frontRight.localPosition.x <= 0f || rearRight.localPosition.x <= 0f ||
            frontLeft.localPosition.z <= 0f || frontRight.localPosition.z <= 0f ||
            rearLeft.localPosition.z >= 0f || rearRight.localPosition.z >= 0f)
            throw new InvalidOperationException("LamborghiniAventador: wheel axes are inconsistent; front wheels must be +Z and left controllers -X after source yaw correction.");
        var bodyVisual = Find(prefab.transform, "AventadorBody")!;
        if (Mathf.Abs(bodyVisual.localPosition.y + LamborghiniAventadorSetup.BodyLowering) > 0.0001f)
            throw new InvalidOperationException(
                $"LamborghiniAventador: authored body visual Y={bodyVisual.localPosition.y:F4} must be -{LamborghiniAventadorSetup.BodyLowering:F3} m.");
        VerifyAuthoredWheelVisualCenters(prefab.transform);
        var bodyRenderers = bodyVisual.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer != null && renderer.enabled).ToArray();
        if (bodyRenderers.Length == 0)
            throw new InvalidOperationException("LamborghiniAventador: body visual has no enabled renderers for upright-orientation checks.");
        var bodyBounds = bodyRenderers[0].bounds;
        foreach (var renderer in bodyRenderers.Skip(1)) bodyBounds.Encapsulate(renderer.bounds);
        var wheelY = new[] { frontLeft.position.y, frontRight.position.y, rearLeft.position.y, rearRight.position.y };
        if (wheelY.Average() >= bodyBounds.center.y ||
            wheelY.Any(y => y >= bodyBounds.min.y + bodyBounds.size.y * 0.42f))
            throw new InvalidOperationException($"LamborghiniAventador: wheel mounts must be in the lower part of the rendered body bounds; wheelY=[{string.Join(",", wheelY.Select(y => y.ToString("F3")))}], bodyBounds={bodyBounds}.");
        foreach (var caliperName in new[] { "Caliper_FL", "Caliper_FR", "Caliper_BL", "Caliper_BR" })
            if (Find(prefab.transform, caliperName) == null)
                throw new InvalidOperationException("LamborghiniAventador: missing upright caliper root " + caliperName);
        if (Find(prefab.transform, "AventadorBody") == null)
            throw new InvalidOperationException("LamborghiniAventador: body visual root is missing.");
        foreach (var anchorName in new[] { "NpcDriverSeatAnchor", "Steering_wheel", "DriverSeatAnchor", "CameraAnchor" })
            if (Find(prefab.transform, anchorName) == null)
                throw new InvalidOperationException("LamborghiniAventador: missing authored occupant anchor " + anchorName);
        var npcSeat = Find(prefab.transform, "NpcDriverSeatAnchor")!;
        var driverSeat = Find(prefab.transform, "DriverSeatAnchor")!;
        var steeringWheel = Find(prefab.transform, "Steering_wheel")!;
        var controllerProperties = new SerializedObject(nwh);
        var steering = controllerProperties.FindProperty("steering");
        var steeringReference = steering?.FindPropertyRelative("steeringWheel");
        var wheelbase = controllerProperties.FindProperty("wheelbase");
        if (steeringReference?.propertyType != SerializedPropertyType.ObjectReference ||
            steeringReference.objectReferenceValue != null || wheelbase == null ||
            wheelbase.propertyType != SerializedPropertyType.Float || Mathf.Abs(wheelbase.floatValue - 2.700f) > 0.001f)
            throw new InvalidOperationException("LamborghiniAventador: static steering wheel must stay unbound and NWH wheelbase must match 2.700 m.");
        if (driverSeat.localPosition.x >= -0.05f || Mathf.Abs(driverSeat.localPosition.x) > 0.65f ||
            driverSeat.localPosition.y < 0.3f || driverSeat.localPosition.y > 0.55f ||
            driverSeat.localPosition.z < 0.1f || driverSeat.localPosition.z > 0.9f ||
            Mathf.Abs((npcSeat.localPosition - driverSeat.localPosition).sqrMagnitude) > 0.0001f ||
            steeringWheel.localPosition.x >= -0.05f || driverSeat.localPosition.z >= steeringWheel.localPosition.z)
            throw new InvalidOperationException("LamborghiniAventador: player/NPC seat and static wheel anchors must be on the left-hand-drive side, with the seat behind the wheel.");

        VerifyPaintAndGlass(prefab);
        VerifyWheelMeshCombine(prefab);

        if (nativeBody == null || nativeBody.GetComponents<BoxCollider>().Length == 0 ||
            nativeBody.GetComponents<BoxCollider>().Any(collider => !collider.enabled || collider.isTrigger))
            throw new InvalidOperationException("LamborghiniAventador: BodyCollider must contain enabled solid BoxCollider components.");

        LamborghiniAventadorWashSetup.VerifyBindings(prefab);

        var foreignDependencies = AssetDatabase.GetDependencies(PrefabPath, true)
            .Concat(AssetDatabase.GetDependencies(TypePath, true))
            .Where(dependency => dependency.StartsWith("Assets/Mods/", StringComparison.OrdinalIgnoreCase) &&
                                 !dependency.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(dependency => dependency, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (foreignDependencies.Length > 0)
            throw new InvalidOperationException("LamborghiniAventador: foreign vehicle/mod asset dependencies (" +
                                                foreignDependencies.Length + "): " + string.Join("; ", foreignDependencies));
        Debug.Log("LamborghiniAventador: native asset/ownership checks pass, NOT a gameplay validation.");
    }

    private static void VerifyPowertrainCalibration(NWH.VehiclePhysics2.VehicleController source)
    {
        const float tolerance = 0.001f;
        var calibrationType = typeof(LamborghiniAventadorMod).Assembly
            .GetType("LamborghiniAventadorPowertrainCalibration", true)!;
        var launchMultiplier = calibrationType.GetMethod("LaunchTorqueMultiplier", BindingFlags.Static | BindingFlags.NonPublic)!;
        var modifierCoefficient = calibrationType.GetMethod("PowerModifierCoefficient", BindingFlags.Static | BindingFlags.NonPublic)!;
        float Launch(float speedKph, float throttle, int gear) =>
            (float)launchMultiplier.Invoke(null, new object[] { speedKph, throttle, gear })!;
        float Modifier(float speedKph, float throttle, int gear) =>
            (float)modifierCoefficient.Invoke(null, new object[] { speedKph, throttle, gear })!;

        if (Mathf.Abs(Launch(0f, 1f, 1) - 0.78f) > tolerance ||
            Mathf.Abs(Launch(100f, 1f, 1) - 0.89f) > tolerance ||
            Mathf.Abs(Launch(120f, 1f, 1) - 1f) > tolerance ||
            Mathf.Abs(Launch(0f, 0.59f, 1) - 1f) > tolerance ||
            Mathf.Abs(Launch(0f, 1f, 0) - 1f) > tolerance ||
            Mathf.Abs(Launch(0f, 1f, -1) - 1f) > tolerance ||
            Mathf.Abs(Modifier(120f, 1f, 1) -
                      (0.85f + 0.22f) / 1.22f) > tolerance ||
            Mathf.Abs((Modifier(0f, 1f, 1) * 1.22f - 0.22f) -
                      (0.85f * 0.78f)) > tolerance)
            throw new InvalidOperationException("LamborghiniAventador: launch torque fade changed reverse, neutral, gentle throttle, or its 80-120 km/h blend.");

        var clone = UnityEngine.Object.Instantiate(source.gameObject);
        try
        {
            var controller = clone.GetComponent<NWH.VehiclePhysics2.VehicleController>() ??
                             throw new InvalidOperationException("Aventador powertrain regression clone lost its NWH controller.");
            var modifiers = controller.powertrain.engine.powerModifiers;
            var initialCount = modifiers.Count;
            var calibration = clone.AddComponent(calibrationType);
            var initialize = calibrationType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var attachedModifier = calibrationType.GetProperty("AttachedPowerModifier", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var release = calibrationType.GetMethod("Release", BindingFlags.Instance | BindingFlags.NonPublic)!;
            initialize.Invoke(calibration, new object?[] { controller, null });
            initialize.Invoke(calibration, new object?[] { controller, null });
            var registeredModifier = attachedModifier.GetValue(calibration) as NWH.VehiclePhysics2.Powertrain.EngineComponent.PowerModifier;
            if (modifiers.Count != initialCount + 1 ||
                registeredModifier == null || !modifiers.Contains(registeredModifier))
                throw new InvalidOperationException("LamborghiniAventador: power modifier registration is not idempotent.");

            release.Invoke(calibration, null);
            if (modifiers.Count != initialCount ||
                (registeredModifier != null && modifiers.Contains(registeredModifier)))
                throw new InvalidOperationException("LamborghiniAventador: power modifier release did not remove the registered delegate.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(clone);
        }
    }

    [MenuItem("Big Ambitions Mods/Scaffold/Compare LamborghiniAventador Wheel Bundle")]
    public static void CompareExistingWheelBundleToSource()
    {
        var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (sourcePrefab == null)
            throw new InvalidOperationException("LamborghiniAventador: source prefab is missing for bundle comparison.");
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
            throw new InvalidOperationException("LamborghiniAventador: could not resolve the Unity project root for bundle comparison.");
        var bundlePath = Path.Combine(projectRoot, WindowsBundlePath);
        if (!File.Exists(bundlePath))
            throw new FileNotFoundException("LamborghiniAventador: existing Windows bundle is missing.", bundlePath);

        AssetBundle? bundle = null;
        try
        {
            bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
                throw new InvalidOperationException("LamborghiniAventador: could not open existing Windows bundle.");

            var bundledPrefab = bundle.LoadAsset<GameObject>("LamborghiniAventador") ??
                                bundle.LoadAllAssets<GameObject>().FirstOrDefault(asset => asset.name == "LamborghiniAventador");
            if (bundledPrefab == null)
                throw new InvalidOperationException("LamborghiniAventador: existing bundle contains no LamborghiniAventador prefab.");

            LamborghiniAventadorDamageRegression.VerifyLightingFromBundle(bundledPrefab);
            VerifyWarehousePlacement(bundledPrefab, bundle.LoadAsset<VehicleType>(TypePath));

            var mismatches = 0;
            var diagnosticDirectory = Path.Combine(projectRoot, "obj", "VehicleVerification", "Lamborghini_Aventador", "WheelDiagnostics");
            Directory.CreateDirectory(diagnosticDirectory);
            foreach (var mountName in new[] { "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" })
            {
                var sourceMount = Find(sourcePrefab.transform, mountName);
                var bundleMount = Find(bundledPrefab.transform, mountName);
                var sourceGeometry = sourceMount != null ? Find(sourceMount, "Geometry_" + mountName) : null;
                var bundleGeometry = bundleMount != null ? Find(bundleMount, "Geometry_" + mountName) : null;
                if (sourceGeometry == null || bundleGeometry == null)
                {
                    Debug.LogWarning($"LamborghiniAventador bundle wheel comparison: {mountName} geometry missing; source={sourceGeometry != null}, bundle={bundleGeometry != null}.");
                    mismatches++;
                    continue;
                }

                var sourceFilters = sourceGeometry.GetComponentsInChildren<MeshFilter>(true)
                    .Where(filter => filter.sharedMesh != null).OrderBy(filter => filter.name, StringComparer.Ordinal).ToArray();
                var bundleFilters = bundleGeometry.GetComponentsInChildren<MeshFilter>(true)
                    .Where(filter => filter.sharedMesh != null).OrderBy(filter => filter.name, StringComparer.Ordinal).ToArray();
                var transformMatches = NearlyEqual(sourceGeometry.localPosition, bundleGeometry.localPosition) &&
                                       NearlyEqual(sourceGeometry.localRotation, bundleGeometry.localRotation) &&
                                       NearlyEqual(sourceGeometry.localScale, bundleGeometry.localScale);
                var meshMatches = sourceFilters.Length == 2 && bundleFilters.Length == sourceFilters.Length &&
                                  sourceFilters.Zip(bundleFilters, (source, bundled) =>
                                      string.Equals(source.name, bundled.name, StringComparison.Ordinal) &&
                                      MeshMatches(source.sharedMesh, bundled.sharedMesh)).All(matches => matches);
                var matches = transformMatches && meshMatches;
                if (!matches) mismatches++;
                var side = mountName.EndsWith("L", StringComparison.Ordinal) ? "L" : "R";
                var sourceObj = Path.Combine(diagnosticDirectory, "saved-prefab-" + mountName + ".obj");
                var bundleObj = Path.Combine(diagnosticDirectory, "bundle-" + mountName + ".obj");
                var sourceObjVertices = ExportWheelObj(sourcePrefab.transform, sourceGeometry, sourceObj);
                var bundleObjVertices = ExportWheelObj(bundledPrefab.transform, bundleGeometry, bundleObj);
                Debug.Log($"LamborghiniAventador bundle wheel comparison: {mountName} result={(matches ? "MATCH" : "MISMATCH")}, " +
                          $"transformMatch={transformMatches}, meshMatch={meshMatches}, " +
                          $"sourceRotation={sourceGeometry.localRotation.eulerAngles}, bundleRotation={bundleGeometry.localRotation.eulerAngles}, " +
                          $"sourceForward={sourceGeometry.localRotation * Vector3.forward}, bundleForward={bundleGeometry.localRotation * Vector3.forward}, " +
                          $"sourceBatches={sourceFilters.Length}, bundleBatches={bundleFilters.Length}, " +
                          $"sourceVertices={sourceFilters.Sum(filter => filter.sharedMesh.vertexCount)}, bundleVertices={bundleFilters.Sum(filter => filter.sharedMesh.vertexCount)}, " +
                          $"objRootSpaceVertices={sourceObjVertices}/{bundleObjVertices}, side={side}, sourceObj={sourceObj}, bundleObj={bundleObj}.");
            }

            Debug.Log(mismatches == 0
                ? "LamborghiniAventador existing wheel bundle matches the saved source prefab across all four wheel geometry transforms and mesh data."
                : $"LamborghiniAventador existing wheel bundle differs from the saved source prefab on {mismatches} wheel(s); regenerate the bundle before judging the wheel fix.");
            if (mismatches > 0)
                throw new InvalidOperationException(
                    $"LamborghiniAventador: built wheel geometry differs from the authored prefab on {mismatches} wheel(s).");
        }
        finally
        {
            if (bundle != null) bundle.Unload(true);
        }
    }

    private static void VerifyWarehousePlacement(GameObject prefab, VehicleType type)
    {
        if (type == null || type.maxCargoCapacity != 2)
            throw new InvalidOperationException("Aventador vehicle type must expose exactly two cargo slots.");

        var placement = prefab.GetComponent<MeshCollider>();
        var body = prefab.transform.Find("CarHolder/Colliders/BodyCollider");
        var spawn = body != null ? body.GetComponent<MeshCollider>() : null;
        if (placement == null || spawn == null || placement.enabled || spawn.enabled ||
            placement.isTrigger || spawn.isTrigger || prefab.GetComponentInChildren<MeshCollider>() != placement ||
            placement.sharedMesh == null || spawn.sharedMesh == null)
            throw new InvalidOperationException("Aventador warehouse/native spawn mesh colliders must be present and disabled.");

        var solid = body.GetComponents<BoxCollider>().Single(collider => collider.enabled && !collider.isTrigger && collider.size.z > 4f);
        var placementSize = placement.sharedMesh.bounds.size;
        var spawnSize = spawn.sharedMesh.bounds.size;
        var expectedLength = spawnSize.z + 2f * LamborghiniAventadorSetup.WarehouseClearancePerEnd;
        if (Mathf.Abs(spawnSize.z - solid.size.z) > .001f || Mathf.Abs(placementSize.z - expectedLength) > .001f ||
            Mathf.Abs(spawnSize.x - solid.size.x) > .001f || Mathf.Abs(placementSize.x - spawnSize.x) > .001f)
            throw new InvalidOperationException("Aventador native warehouse placement bounds differ from the fitted chassis and door clearance.");

        VerifyNativeWarehouseOffsets(placement, spawn);
        Debug.Log($"Aventador warehouse placement and cargo verified: cargo={type.maxCargoCapacity}, " +
            $"native spawn bounds={spawnSize:F3}, warehouse bounds={placementSize:F3}.");
    }

    private static void VerifyNativeWarehouseOffsets(MeshCollider placement, MeshCollider fittedBody)
    {
        var host = new GameObject("AventadorWarehouseFixture");
        host.SetActive(false);
        try
        {
            host.transform.SetPositionAndRotation(new Vector3(20f, 0f, -12f), Quaternion.Euler(0f, 57f, 0f));
            var entrance = host.AddComponent<DriveInEntrance>();
            var proximity = host.AddComponent<BoxCollider>();
            proximity.size = new Vector3(3f, 2f, 2f);
            var inside = new GameObject("GarageSpawn").transform;
            inside.SetParent(host.transform, false);
            inside.localPosition = Vector3.back * 2f;
            var settings = new SerializedObject(entrance);
            settings.FindProperty("proximityDetectionCollider").objectReferenceValue = proximity;
            settings.FindProperty("vehicleSpawnPositionInsideGarageDoor").objectReferenceValue = inside;
            settings.ApplyModifiedPropertiesWithoutUndo();

            VerifyReverseWarehouseContact(entrance, proximity);
            var outsideDelta = entrance.GetSpawnPositionInFrontOfGarageDoor(placement) -
                entrance.GetSpawnPositionInFrontOfGarageDoor(fittedBody);
            var insideDelta = entrance.GetSpawnPositionInsideOfGarageDoor(placement) -
                entrance.GetSpawnPositionInsideOfGarageDoor(fittedBody);
            var expected = host.transform.forward * LamborghiniAventadorSetup.WarehouseClearancePerEnd;
            if ((outsideDelta - expected).sqrMagnitude > .000001f || (insideDelta - expected).sqrMagnitude > .000001f)
                throw new InvalidOperationException("Aventador native door placement did not preserve its longitudinal clearance.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void VerifyReverseWarehouseContact(DriveInEntrance entrance, BoxCollider proximity)
    {
        var door = entrance.transform;
        var closedDoor = entrance.gameObject.AddComponent<BoxCollider>();
        closedDoor.center = new Vector3(0f, .5f, -1f);
        closedDoor.size = Vector3.one;

        var city = entrance.gameObject.AddComponent<CityBuildingController>();
        var warehouse = ScriptableObject.CreateInstance<Buildings.Building>();
        warehouse.BuildingType = "ba:buildingtype_warehouse";
        city.building = warehouse;
        try
        {
            var properties = new SerializedObject(entrance);
            properties.FindProperty("garageDoorClosedCollider").objectReferenceValue = closedDoor;
            properties.FindProperty("cityBuildingController").objectReferenceValue = city;
            properties.ApplyModifiedPropertiesWithoutUndo();

            var entryObject = new GameObject("EnterTrigger");
            entryObject.transform.SetParent(door, false);
            var nativeEntry = entryObject.AddComponent<DriveInEntranceEnterTrigger>();
            var triggerProperties = new SerializedObject(nativeEntry);
            triggerProperties.FindProperty("driveInEntrance").objectReferenceValue = entrance;
            triggerProperties.ApplyModifiedPropertiesWithoutUndo();
            var enterCollider = entryObject.AddComponent<BoxCollider>();
            enterCollider.isTrigger = true;

            var unrelated = new GameObject("DriveInEntranceCollider (1)");
            unrelated.transform.SetParent(door, false);
            var unrelatedCollider = unrelated.AddComponent<BoxCollider>();
            var controllerType = typeof(LamborghiniAventadorSpawnAppearance).Assembly
                .GetType("LamborghiniAventadorWarehouseEntryController", true)!;
            const BindingFlags staticMethods = BindingFlags.Static | BindingFlags.NonPublic;
            var findDoor = controllerType.GetMethod("FindEntrance", staticMethods)!;
            var isBarrier = controllerType.GetMethod("IsEntranceBarrier", staticMethods)!;
            var isReverseApproach = controllerType.GetMethod("IsReverseApproach", staticMethods)!;
            DriveInEntrance? DoorFor(Collider collider) => (DriveInEntrance?)findDoor.Invoke(null, new object[] { collider });
            bool Protects(Collider collider) => (bool)isBarrier.Invoke(null, new object[] { collider })!;

            // The game can disable its solid gate during the same physics
            // step as the reported contact. The reference must still match.
            closedDoor.enabled = false;
            if (DoorFor(closedDoor) != entrance || DoorFor(enterCollider) != entrance ||
                DoorFor(proximity) != null || DoorFor(unrelatedCollider) != null ||
                !Protects(closedDoor) || Protects(enterCollider) || Protects(unrelatedCollider))
                throw new InvalidOperationException("Aventador reverse entry did not use the exact serialized warehouse gate and trigger.");

            // Reproduce the scaled pivot and recessed box from the tested
            // Murray Hill warehouse. The car can be behind the pivot, yet
            // still clearly outside the gate along its reverse path.
            door.localScale = new Vector3(3.5f, 3.5f, 3.5f);
            var car = new GameObject("AventadorReverseEntryFixture").transform;
            car.SetParent(door, false);
            car.SetPositionAndRotation(door.position - door.forward * .45f, door.rotation);
            bool Approaches(int gear) => (bool)isReverseApproach.Invoke(null, new object[] { gear, car, closedDoor })!;
            if (!Approaches(-1) || Approaches(1))
                throw new InvalidOperationException("Aventador reverse entry rejected a car outside the recessed gate.");
            car.rotation = Quaternion.LookRotation(-door.forward);
            if (Approaches(-1))
                throw new InvalidOperationException("Aventador reverse entry matched an outgoing vehicle.");
            car.SetPositionAndRotation(door.TransformPoint(Vector3.back * 2f), door.rotation);
            if (Approaches(-1))
                throw new InvalidOperationException("Aventador reverse entry matched a car already inside the gate.");
            door.localScale = Vector3.one;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(warehouse);
        }
        Debug.Log("Aventador reverse warehouse entry: recessed native gate, exact entry trigger and disabled-gate impact ownership verified.");
    }

    private static bool MeshMatches(Mesh? source, Mesh? bundled)
    {
        if (source == null || bundled == null || source.vertexCount != bundled.vertexCount ||
            source.subMeshCount != bundled.subMeshCount)
            return false;

        var sourceVertices = source.vertices;
        var bundleVertices = bundled.vertices;
        var sourceNormals = source.normals;
        var bundleNormals = bundled.normals;
        if (sourceVertices.Length != bundleVertices.Length || sourceNormals.Length != bundleNormals.Length)
            return false;
        for (var i = 0; i < sourceVertices.Length; i++)
            if (!NearlyEqual(sourceVertices[i], bundleVertices[i])) return false;
        for (var i = 0; i < sourceNormals.Length; i++)
            if (!NearlyEqual(sourceNormals[i], bundleNormals[i])) return false;
        for (var subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            var sourceTriangles = source.GetTriangles(subMesh);
            var bundleTriangles = bundled.GetTriangles(subMesh);
            if (!sourceTriangles.SequenceEqual(bundleTriangles)) return false;
        }
        return true;
    }

    private static int ExportWheelObj(Transform root, Transform geometry, string path)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Exported in vehicle-root local space for wheel-face orientation diagnostics.");
        var vertexOffset = 0;
        foreach (var filter in geometry.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;
            builder.Append("o ").AppendLine(filter.gameObject.name);
            foreach (var vertex in mesh.vertices)
            {
                var rootVertex = root.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                builder.Append("v ").Append(rootVertex.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
                    .Append(rootVertex.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(' ')
                    .AppendLine(rootVertex.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }

            var relativeMatrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var reverseWinding = relativeMatrix.determinant < 0f;
            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles) continue;
                var indices = mesh.GetTriangles(subMesh);
                for (var i = 0; i + 2 < indices.Length; i += 3)
                {
                    var a = indices[i] + vertexOffset + 1;
                    var b = indices[i + 1] + vertexOffset + 1;
                    var c = indices[i + 2] + vertexOffset + 1;
                    builder.Append("f ").Append(a).Append(' ');
                    if (reverseWinding) builder.Append(c).Append(' ').Append(b);
                    else builder.Append(b).Append(' ').Append(c);
                    builder.AppendLine();
                }
            }
            vertexOffset += mesh.vertexCount;
        }

        File.WriteAllText(path, builder.ToString());
        return vertexOffset;
    }

    private static bool NearlyEqual(Vector3 a, Vector3 b) => (a - b).sqrMagnitude <= 1e-10f;

    private static bool NearlyEqual(Quaternion a, Quaternion b) =>
        Mathf.Abs(Quaternion.Dot(a, b)) >= 0.999999f;

    private static Transform? Find(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal)) return transform;
        return null;
    }

    private static void VerifyNwhAudio(NWH.VehiclePhysics2.VehicleController nwh)
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        if (string.IsNullOrEmpty(projectRoot))
            throw new InvalidOperationException("LamborghiniAventador: could not resolve the Unity project root for audio verification.");

        foreach (var engineFile in new[]
                 {
                     "EngineLow.wav", "EngineLowLoad.wav", "EngineMid.wav",
                     "EngineMidLoad.wav", "EngineHigh.wav", "EngineHighLoad.wav",
                 })
        {
            var aventadorPath = Path.Combine(projectRoot, Root, "Config", "Audio", engineFile);
            var revueltoPath = Path.Combine(projectRoot, RevueltoAudioPath, engineFile);
            if (!File.Exists(aventadorPath) || !File.Exists(revueltoPath) ||
                !File.ReadAllBytes(aventadorPath).SequenceEqual(File.ReadAllBytes(revueltoPath)))
                throw new InvalidOperationException($"LamborghiniAventador: engine recording '{engineFile}' must remain a byte-identical copy of the Lamborghini Revuelto sound set.");
        }

        var engineLowClip = AssetDatabase.LoadAssetAtPath<AudioClip>(EngineLowPath);
        if (engineLowClip == null || engineLowClip.samples < 44100 || engineLowClip.length < 1f ||
            engineLowClip.channels < 1 || engineLowClip.frequency < 22050)
            throw new InvalidOperationException("LamborghiniAventador: NWH idle must use the locally bundled EngineLow WAV.");
        var serialized = new SerializedObject(nwh);
        var engine = serialized.FindProperty("soundManager.engineRunningComponent.clips");
        if (engine == null || !engine.isArray || engine.arraySize != 1 ||
            engine.GetArrayElementAtIndex(0).objectReferenceValue != engineLowClip)
            throw new InvalidOperationException("LamborghiniAventador: NWH idle must use the same Revuelto EngineLow WAV as the custom idle voice.");
        var donorPaths = new[]
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
        };
        var populated = donorPaths
            .Select(path => new { Path = path, Property = serialized.FindProperty(path) })
            .Where(entry => entry.Property != null && entry.Property.isArray && entry.Property.arraySize > 0)
            .Select(entry => entry.Path + " (" + entry.Property!.arraySize + ")")
            .ToArray();
        var mixer = serialized.FindProperty("soundManager.mixer");
        if (populated.Length > 0 || (mixer != null && mixer.objectReferenceValue != null))
            throw new InvalidOperationException("LamborghiniAventador: donor NWH audio references remain: " +
                                                string.Join("; ", populated.Concat(mixer?.objectReferenceValue != null
                                                    ? new[] { "soundManager.mixer" }
                                                    : Array.Empty<string>())));
    }

    private static void VerifyRuntimeDependency()
    {
        var dependencyFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(DependenciesPath);
        var manifest = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ManifestPath);
        var dependencyReference = manifest != null
            ? new SerializedObject(manifest).FindProperty("DependenciesFolder")?.objectReferenceValue
            : null;
        var harmonyPath = DependenciesPath + "/0Harmony.dll";
        var harmonyLicensePath = DependenciesPath + "/Harmony-LICENSE.txt";
        if (dependencyFolder == null || dependencyReference != dependencyFolder ||
            AssetImporter.GetAtPath(harmonyPath) == null ||
            !File.Exists(Path.Combine(Directory.GetCurrentDirectory(), harmonyLicensePath)) ||
            !File.Exists(Path.Combine(Directory.GetCurrentDirectory(), harmonyPath)))
            throw new InvalidOperationException(
                "LamborghiniAventador: runtime Harmony dependency, its license, or manifest dependency-folder reference is missing.");
    }

    private static void VerifyPaintAndGlass(GameObject prefab)
    {
        var paintSlots = new List<Material>();
        var glassRenderers = new List<Renderer>();
        var materialRoles = new Dictionary<Material, ValidationMaterialRole>();
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null) continue;
            var nodePath = GetNodePath(renderer.transform, prefab.transform);
            var materials = renderer.sharedMaterials;
            var glass = nodePath.IndexOf("Window_Geo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        nodePath.IndexOf("WindowInside_Geo", StringComparison.OrdinalIgnoreCase) >= 0;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (material == null) continue;
                var role = GetMaterialRoleForValidation(material.name);
                if (role.HasValue)
                    materialRoles[material] = role.Value;
                if (material.name.IndexOf("RED_PAINT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nodePath.IndexOf("RED_PAINT", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (!material.HasProperty("_BaseColor") && !material.HasProperty("_Color") &&
                        !material.HasProperty("baseColorFactor"))
                        throw new InvalidOperationException($"LamborghiniAventador: body paint slot '{nodePath}' material '{material.name}' exposes no supported color property; shader='{material.shader?.name}', asset='{AssetDatabase.GetAssetPath(material.shader)}', messages=[{string.Join("; ", ShaderUtil.GetShaderMessages(material.shader).Select(message => message.message))}].");
                    paintSlots.Add(material);
                }

                glass |= material.name.IndexOf("Window_Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         material.name.IndexOf("RED_GLASS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         material.name.IndexOf("ORANGE_GLASS", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (glass) glassRenderers.Add(renderer);
        }

        if (paintSlots.Count != 1)
            throw new InvalidOperationException($"LamborghiniAventador: expected one RED_PAINT material/imported-node slot; found {paintSlots.Count}.");
        if (glassRenderers.Count < 6)
            throw new InvalidOperationException($"LamborghiniAventador: expected at least six explicitly identified cabin-glass renderers; found {glassRenderers.Count}.");
        var lampCover = prefab.GetComponentsInChildren<Renderer>(true)
            .SelectMany(renderer => renderer.sharedMaterials.Select(material => new { Renderer = renderer, Material = material }))
            .Any(entry => entry.Material != null &&
                          entry.Material.name.IndexOf("LightA_Material", StringComparison.OrdinalIgnoreCase) >= 0);
        if (!lampCover)
            throw new InvalidOperationException("LamborghiniAventador: transparent LightA_Material lamp covers were not found in the imported prefab.");

        VerifyNormalizedMaterialState(paintSlots[0], ValidationMaterialRole.BodyPaint);
        foreach (var materialRole in materialRoles)
            VerifyNormalizedMaterialState(materialRole.Key, materialRole.Value);
    }

    private static ValidationMaterialRole? GetMaterialRoleForValidation(string materialName)
    {
        if (materialName.IndexOf("RED_PAINT", StringComparison.OrdinalIgnoreCase) >= 0)
            return ValidationMaterialRole.BodyPaint;
        if (materialName.IndexOf("RED_GLASS", StringComparison.OrdinalIgnoreCase) >= 0)
            return ValidationMaterialRole.RedLampLens;
        if (materialName.IndexOf("ORANGE_GLASS", StringComparison.OrdinalIgnoreCase) >= 0)
            return ValidationMaterialRole.OrangeLampLens;
        if (materialName.IndexOf("LightA_Material", StringComparison.OrdinalIgnoreCase) >= 0)
            return ValidationMaterialRole.ClearLampLens;
        if (materialName.IndexOf("Window_Material", StringComparison.OrdinalIgnoreCase) >= 0)
            return ValidationMaterialRole.CabinGlass;
        return null;
    }

    private static void VerifyNormalizedMaterialState(Material source, ValidationMaterialRole role)
    {
        var material = new Material(source)
        {
            name = "AventadorValidation_" + source.name,
        };
        try
        {
            var controllerType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("LamborghiniAventadorGlassController", false))
                .FirstOrDefault(type => type != null);
            var roleType = controllerType?.GetNestedType("MaterialRole", BindingFlags.Public | BindingFlags.NonPublic);
            var normalizer = controllerType?.GetMethod(
                "NormalizeRuntimeMaterial",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (roleType == null || normalizer == null)
                throw new InvalidOperationException("LamborghiniAventador: appearance material normalizer is unavailable to the editor checks.");
            var runtimeRole = Enum.Parse(roleType, role.ToString());
            normalizer.Invoke(null, new[] { (object)material, runtimeRole });
            if (material.shader == null ||
                !string.Equals(material.shader.name, "HDRP/Lit", StringComparison.Ordinal) &&
                !string.Equals(material.shader.name, "High Definition Render Pipeline/Lit", StringComparison.Ordinal))
                throw new InvalidOperationException($"LamborghiniAventador: normalized material '{source.name}' did not resolve to HDRP/Lit.");
            if (!material.HasProperty("_SurfaceType") || !material.HasProperty("_ZWrite") ||
                !material.HasProperty("_BaseColor"))
                throw new InvalidOperationException($"LamborghiniAventador: normalized material '{source.name}' is missing the HDRP surface, depth, or color properties.");

            var transparent = role != ValidationMaterialRole.BodyPaint;
            var expectedSurface = transparent ? 1f : 0f;
            var expectedZWrite = transparent ? 0f : 1f;
            var expectedQueue = transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            var surface = material.GetFloat("_SurfaceType");
            var zWrite = material.GetFloat("_ZWrite");
            var alpha = material.GetColor("_BaseColor").a;
            var smoothness = material.HasProperty("_Smoothness")
                ? material.GetFloat("_Smoothness") : 0f;
            if (Mathf.Abs(surface - expectedSurface) > 0.01f ||
                Mathf.Abs(zWrite - expectedZWrite) > 0.01f ||
                material.renderQueue != expectedQueue ||
                transparent != (alpha < 0.99f))
                throw new InvalidOperationException(
                    $"LamborghiniAventador: normalized material '{source.name}' has invalid role={role}, " +
                    $"surface={surface}, zWrite={zWrite}, " +
                    $"queue={material.renderQueue}, alpha={alpha}.");

            if (role == ValidationMaterialRole.BodyPaint &&
                (material.GetTexture("_BaseColorMap") != null ||
                 material.HasProperty("_Metallic") && material.GetFloat("_Metallic") > 0.2f))
                throw new InvalidOperationException(
                    $"LamborghiniAventador: body paint material '{source.name}' must be opaque, untextured, and low-metallic.");
            if ((role == ValidationMaterialRole.CabinGlass || role == ValidationMaterialRole.ClearLampLens) &&
                (alpha > (role == ValidationMaterialRole.CabinGlass ? 0.03f : 0.05f) || smoothness > 0.45f))
                throw new InvalidOperationException(
                    $"LamborghiniAventador: transparent material '{source.name}' remains too bright or reflective (alpha={alpha:0.###}, smoothness={smoothness:0.###}).");
            if ((role == ValidationMaterialRole.RedLampLens || role == ValidationMaterialRole.OrangeLampLens) &&
                (alpha > 0.25f || smoothness > 0.55f))
                throw new InvalidOperationException(
                    $"LamborghiniAventador: lamp lens '{source.name}' remains too opaque or reflective (alpha={alpha:0.###}, smoothness={smoothness:0.###}).");
            if (transparent && material.HasProperty("_BaseColorMap") && material.GetTexture("_BaseColorMap") != null)
                throw new InvalidOperationException($"LamborghiniAventador: normalized transparent material '{source.name}' retained its opaque source texture.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static float ReadNumber(SerializedProperty property) =>
        property.propertyType == SerializedPropertyType.Integer ||
        property.propertyType == SerializedPropertyType.Enum
            ? property.intValue
            : property.floatValue;

    private static void VerifyAuthoredWheelVisualCenters(Transform root)
    {
        var expected = new[]
        {
            (Visual: "Wheel_FL", Caliper: "Caliper_FL", Controller: "FrontLeft_WheelController", Front: true, X: -0.860f, Z: 1.250f),
            (Visual: "Wheel_FR", Caliper: "Caliper_FR", Controller: "FrontRight_WheelController", Front: true, X: 0.860f, Z: 1.250f),
            (Visual: "Wheel_BL", Caliper: "Caliper_BL", Controller: "RearLeft_WheelController", Front: false, X: -0.840f, Z: -1.450f),
            (Visual: "Wheel_BR", Caliper: "Caliper_BR", Controller: "RearRight_WheelController", Front: false, X: 0.840f, Z: -1.450f),
        };
        foreach (var item in expected)
        {
            var mount = Find(root, item.Visual) ??
                        throw new InvalidOperationException($"LamborghiniAventador: missing authored wheel visual '{item.Visual}'.");
            var geometry = Find(mount, "Geometry_" + item.Visual) ??
                           throw new InvalidOperationException($"LamborghiniAventador: missing geometry group for '{item.Visual}'.");
            var renderers = geometry.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null && renderer.enabled).ToArray();
            if (renderers.Length == 0)
                throw new InvalidOperationException($"LamborghiniAventador: wheel '{item.Visual}' has no rendered geometry.");

            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            var renderedCenter = root.InverseTransformPoint(bounds.center);
            var mountCenter = root.InverseTransformPoint(mount.position);
            var expectedMount = new Vector3(
                item.X,
                (item.Front ? 0.3305f : 0.35545f) - LamborghiniAventadorSetup.WheelCenterLowering,
                item.Z);
            var expectedVisual = expectedMount;
            if ((mountCenter - expectedMount).sqrMagnitude > 1e-6f ||
                (renderedCenter - expectedVisual).sqrMagnitude > 4e-6f)
                throw new InvalidOperationException(
                    $"LamborghiniAventador: '{item.Visual}' mount={mountCenter}, renderedCenter={renderedCenter}; " +
                    $"expected authored mount={expectedMount} and centered visible geometry={expectedVisual}.");

            var caliper = Find(root, item.Caliper) ??
                          throw new InvalidOperationException($"LamborghiniAventador: missing caliper pivot '{item.Caliper}'.");
            if ((root.InverseTransformPoint(caliper.position) - expectedMount).sqrMagnitude > 1e-6f)
                throw new InvalidOperationException(
                    $"LamborghiniAventador: '{item.Caliper}' pivot must stay centered on relocated wheel hub {expectedMount}.");

            var controller = Find(root, item.Controller) ??
                             throw new InvalidOperationException($"LamborghiniAventador: missing controller '{item.Controller}'.");
            var restLength = LamborghiniAventadorSetup.CalculateSuspensionRestLength(item.Front);
            if (Mathf.Abs(controller.localPosition.y - (mount.localPosition.y + restLength)) > 0.001f)
                throw new InvalidOperationException(
                    $"LamborghiniAventador: '{item.Controller}' no longer preserves its loaded-rest anchor above the relocated wheel mount.");
            var wheelComponent = controller.GetComponents<MonoBehaviour>()
                .FirstOrDefault(component => component != null && component.GetType().Name == "WheelController");
            if (wheelComponent == null)
                throw new InvalidOperationException($"LamborghiniAventador: native wheel component missing on '{item.Controller}'.");
            var wheelSettings = new SerializedObject(wheelComponent);
            var radius = wheelSettings.FindProperty("wheel.radius");
            var expectedRadius = item.Front ? 0.3305f : 0.35545f;
            if (radius?.propertyType != SerializedPropertyType.Float || Mathf.Abs(radius.floatValue - expectedRadius) > 0.0001f)
                throw new InvalidOperationException(
                    $"LamborghiniAventador: '{item.Controller}' changed physical tire radius while lowering visible geometry.");
        }
    }

    private static void VerifyWheelMeshCombine(GameObject prefab)
    {
        var combinedVertices = 0;
        var activeWheelRenderers = 0;
        var sharedMaterialsBySurface = new Dictionary<string, Material>(StringComparer.Ordinal);
        foreach (var mountName in new[] { "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" })
        {
            var mount = Find(prefab.transform, mountName);
            if (mount == null)
                throw new InvalidOperationException("LamborghiniAventador: missing rolling wheel mount " + mountName);
            var geometry = Find(mount, "Geometry_" + mountName);
            if (geometry == null)
                throw new InvalidOperationException("LamborghiniAventador: missing wheel geometry group under " + mountName);
            var expectedYaw = mountName.EndsWith("R", StringComparison.Ordinal) ? 180f : 0f;
            var yawError = Mathf.Abs(Mathf.DeltaAngle(geometry.localRotation.eulerAngles.y, expectedYaw));
            if (yawError > 1f)
                throw new InvalidOperationException($"LamborghiniAventador: {mountName} is missing the static outer-face correction (yaw={geometry.localRotation.eulerAngles.y:0.###}, expected={expectedYaw:0} degrees for mirrored left/right wheel orientation).");

            var renderers = geometry.GetComponentsInChildren<MeshRenderer>(true);
            var batches = renderers.Where(renderer => renderer.name.StartsWith(
                "AventadorWheelBatch_", StringComparison.Ordinal)).ToArray();
            if (batches.Length != 2 || renderers.Length != 2 ||
                geometry.GetComponentsInChildren<Transform>(true).Length != 3)
                throw new InvalidOperationException($"LamborghiniAventador: expected two surface renderers and two child transforms after combining {mountName}; renderers={renderers.Length}, batches={batches.Length}.");

            var seenSurfaces = new HashSet<string>(StringComparer.Ordinal);
            var geometryCenter = geometry.InverseTransformPoint(mount.position);
            var allMeshVertices = batches.SelectMany(batch =>
            {
                var filter = batch.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) return Array.Empty<(Vector3 Position, Vector2 Uv)>();
                var vertices = filter.sharedMesh.vertices;
                var uvs = filter.sharedMesh.uv;
                if (uvs.Length != vertices.Length)
                    throw new InvalidOperationException($"LamborghiniAventador: {batch.name} lost UV0 during mesh combine.");
                return vertices.Select((vertex, index) =>
                {
                    var world = filter.transform.TransformPoint(vertex);
                    return (Position: geometry.InverseTransformPoint(world), Uv: uvs[index]);
                });
            }).ToArray();
            var localMin = allMeshVertices.Select(vertex => vertex.Position)
                .Aggregate(new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity), Vector3.Min);
            var localMax = allMeshVertices.Select(vertex => vertex.Position)
                .Aggregate(new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity), Vector3.Max);
            var localSize = localMax - localMin;
            var axleAxis = localSize.x <= localSize.y && localSize.x <= localSize.z ? 0 :
                localSize.y <= localSize.z ? 1 : 2;
            var outerRadius = allMeshVertices.Select(vertex => RadialDistance(vertex.Position - geometryCenter, axleAxis))
                .DefaultIfEmpty(0f).Max();
            if (outerRadius <= 0.0001f)
                throw new InvalidOperationException($"LamborghiniAventador: {mountName} has no measurable combined wheel radius.");
            foreach (var batch in batches)
            {
                var filter = batch.GetComponent<MeshFilter>();
                var surface = batch.sharedMaterial != null && batch.sharedMaterial.name == "AventadorWheel_Rubber"
                    ? "rubber"
                    : batch.sharedMaterial != null && batch.sharedMaterial.name == "AventadorWheel_Rim"
                        ? "rim" : string.Empty;
                if (!batch.enabled || filter == null || filter.sharedMesh == null || surface.Length == 0 ||
                    !seenSurfaces.Add(surface) || batch.GetComponents<Collider>().Length > 0 ||
                    batch.GetComponents<MonoBehaviour>().Length > 0)
                    throw new InvalidOperationException($"LamborghiniAventador: combined wheel {mountName} lost its distinct rim/rubber mesh or gained an unexpected component.");
                if (Vector3.Distance(batch.bounds.center, mount.position) > 0.03f)
                    throw new InvalidOperationException($"LamborghiniAventador: static face correction moved {mountName} {surface} surface away from its authored wheel center (geometryCenter={batch.bounds.center}, mount={mount.position}).");
                var vertices = filter.sharedMesh.vertices;
                var uvs = filter.sharedMesh.uv;
                var outerCount = 0;
                var tireAtlasCount = 0;
                for (var index = 0; index < vertices.Length; index++)
                {
                    var local = geometry.InverseTransformPoint(filter.transform.TransformPoint(vertices[index]));
                    var radial = RadialDistance(local - geometryCenter, axleAxis);
                    if (radial < outerRadius * 0.80f) continue;
                    outerCount++;
                    if (IsTireAtlasUv(uvs[index])) tireAtlasCount++;
                }
                var outerFraction = outerCount / (float)vertices.Length;
                var chartFraction = outerCount == 0 ? 0f : tireAtlasCount / (float)outerCount;
                var hasTireChart = uvs.Any(IsTireAtlasUv);
                var isTireGeometry = outerFraction >= 0.80f && chartFraction >= 0.80f && hasTireChart;
                if (isTireGeometry != (surface == "rubber"))
                    throw new InvalidOperationException($"LamborghiniAventador: {mountName} {surface} mesh does not match authored tire UV/radial evidence (outerVertices={outerFraction:P0}, tireAtlasVertices={chartFraction:P0}, tireAtlasChart={hasTireChart}).");
                if (surface == "rubber" && (outerFraction < 0.80f || chartFraction < 0.80f || !hasTireChart))
                    throw new InvalidOperationException($"LamborghiniAventador: {mountName} rubber surface lost the source tire atlas or outer-tire vertex coverage.");
                if (sharedMaterialsBySurface.TryGetValue(surface, out var sharedMaterial))
                {
                    if (sharedMaterial != batch.sharedMaterial)
                        throw new InvalidOperationException($"LamborghiniAventador: {surface} wheel material must be shared across all four mounts.");
                }
                else sharedMaterialsBySurface.Add(surface, batch.sharedMaterial);
                activeWheelRenderers++;
                combinedVertices += filter.sharedMesh.vertexCount;
            }
            if (!seenSurfaces.SetEquals(new[] { "rim", "rubber" }))
                throw new InvalidOperationException($"LamborghiniAventador: {mountName} did not retain one rim and one rubber surface.");
        }

        if (combinedVertices < 25000 || activeWheelRenderers != 8 || sharedMaterialsBySurface.Count != 2)
            throw new InvalidOperationException($"LamborghiniAventador: wheel combine did not preserve all four complete rim/rubber assemblies (vertices={combinedVertices}, active={activeWheelRenderers}, surfaces={sharedMaterialsBySurface.Count}).");

        foreach (var surfaceMaterial in sharedMaterialsBySurface)
        {
            var material = surfaceMaterial.Value;
            var normalProperty = material.HasProperty("normalTexture") ? "normalTexture" :
                material.HasProperty("_NormalMap") ? "_NormalMap" : string.Empty;
            var normalMap = normalProperty.Length > 0 ? material.GetTexture(normalProperty) : null;
            var normalScaleProperty = material.HasProperty("normalTexture_scale") ? "normalTexture_scale" :
                material.HasProperty("_NormalScale") ? "_NormalScale" : string.Empty;
            var normalScale = normalScaleProperty.Length > 0 ? material.GetFloat(normalScaleProperty) : 1f;
            if (normalMap == null || float.IsNaN(normalScale) || float.IsInfinity(normalScale) || normalScale <= 0f)
                throw new InvalidOperationException($"LamborghiniAventador: {surfaceMaterial.Key} material lost the GLB tire normal atlas or its usable scale.");
            Debug.Log($"LamborghiniAventador wheel appearance source verified: surface={surfaceMaterial.Key}, material={material.name}, normalAtlas={normalMap.name}, normalScale={normalScale:0.##}.");
        }

        foreach (var caliperName in new[] { "Caliper_FL", "Caliper_FR", "Caliper_BL", "Caliper_BR" })
        {
            var caliper = Find(prefab.transform, caliperName);
            if (caliper == null)
                throw new InvalidOperationException("LamborghiniAventador: missing separate caliper root " + caliperName);
            foreach (var renderer in caliper.GetComponentsInChildren<MeshRenderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!renderer.enabled || filter == null || filter.sharedMesh == null)
                    throw new InvalidOperationException($"LamborghiniAventador: separate caliper renderer '{renderer.name}' was affected by wheel combining.");
            }
        }

        var assetImporter = AssetImporter.GetAtPath(WheelMeshesPath);
        if (assetImporter == null || !string.Equals(assetImporter.assetBundleName, "lamborghiniaventador", StringComparison.Ordinal) ||
            !string.Equals(assetImporter.assetBundleVariant, "unity3d", StringComparison.Ordinal))
            throw new InvalidOperationException("LamborghiniAventador: generated wheel meshes are not assigned to the vehicle bundle.");
        Debug.Log($"LamborghiniAventador wheel renderer combine verified: activeWheelRenderers={activeWheelRenderers}, rimAndRubberMaterials={sharedMaterialsBySurface.Count}, combinedVertices={combinedVertices}.");
    }

    private static bool IsTireAtlasUv(Vector2 uv)
    {
        var wrappedV = uv.y - Mathf.Floor(uv.y);
        var pirelli = uv.x >= 0.76f && uv.x <= 1.01f && wrappedV >= 0.10f && wrappedV <= 0.38f;
        var tread = uv.x >= 0.68f && uv.x <= 1.01f && (wrappedV <= 0.15f || wrappedV >= 0.85f);
        var sidewall = uv.x >= 0.13f && uv.x <= 0.87f && wrappedV >= 0.13f && wrappedV <= 0.87f;
        return pirelli || tread || sidewall;
    }

    private static float RadialDistance(Vector3 delta, int axleAxis) => axleAxis == 0
        ? Mathf.Sqrt(delta.y * delta.y + delta.z * delta.z)
        : axleAxis == 1
            ? Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z)
            : Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y);

    private static string GetNodePath(Transform transform, Transform root)
    {
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
        {
            names.Push(current.name);
            if (current == root) break;
        }
        return string.Join("/", names);
    }
}
