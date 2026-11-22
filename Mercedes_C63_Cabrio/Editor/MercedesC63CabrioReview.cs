using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class MercedesC63CabrioReview
{
    public static void VerifyNativePrefabRegistration(GameObject prefab, Vehicles.VehicleTypes.VehicleType vehicleType)
    {
        const string key = "Prefabs/Vehicles/PlayerVehicles/mercedes.prefab";
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var registration = typeof(MercedesC63CabrioMod).Assembly.GetType(
            "MercedesC63CabrioPlayerPrefabRegistration", true);
        var bind = registration.GetMethod("Bind", flags);
        var cache = (Dictionary<string, UnityEngine.Object>)typeof(Helpers.PrefabHelper)
            .GetField("PrefabCache", flags).GetValue(null);
        bool Bind(string source) => (bool)bind.Invoke(null, new object[] { null, prefab, source });
        if (!Bind("initialization-verification") || !Bind("repeated-initialization-verification"))
            throw new InvalidOperationException("Mercedes direct native prefab registration failed.");
        if (Helpers.PrefabHelper.LoadPrefabAssetByName("Vehicles/PlayerVehicles/mercedes") != prefab)
            throw new InvalidOperationException("Mercedes registered native prefab lookup failed.");
        cache.Remove(key); // Only the target key: simulate native unload clearing it.
        if (!Bind("city-load-verification") || cache[key] != prefab)
            throw new InvalidOperationException("Mercedes native prefab was not rebound after cache reset.");
        ReleaseNativePrefabRegistration();
        if (cache.ContainsKey(key))
            throw new InvalidOperationException("Mercedes unload did not release its native cache entry.");

        var foreignOwner = new GameObject("C63VerificationForeignOwner");
        try
        {
            cache[key] = foreignOwner;
            if (Bind("conflict-verification") || cache[key] != foreignOwner)
                throw new InvalidOperationException("Mercedes registration overwrote a foreign cache owner.");
            cache.Remove(key);
            if (!Bind("ownership-verification"))
                throw new InvalidOperationException("Mercedes registration failed after foreign entry removal.");
            cache[key] = foreignOwner;
            ReleaseNativePrefabRegistration();
            if (cache[key] != foreignOwner)
                throw new InvalidOperationException("Mercedes unload removed a later cache owner.");
            cache.Remove(key);
        }
        finally
        {
            if (cache.TryGetValue(key, out var current) && current == foreignOwner)
                cache.Remove(key);
            UnityEngine.Object.DestroyImmediate(foreignOwner);
        }
        if (!Bind("final-native-spawn-verification"))
            throw new InvalidOperationException("Mercedes native prefab final binding failed.");
        if (!BAModAPI.ModdingAPI.RegisterModVehicleType(vehicleType))
            throw new InvalidOperationException("Mercedes native vehicle type registration failed.");
        try
        {
            if (Vehicles.VehicleTypes.VehicleTypeHelper.GetVehicleType(vehicleType.vehicleTypeName) != vehicleType)
                throw new InvalidOperationException("Mercedes type is unavailable to native vehicle lists.");
        }
        finally
        {
            BAModAPI.ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
        }
        Debug.Log("MercedesC63Cabrio registration audit passed: native type lookup, direct prefab binding, " +
                  "idempotency, cache-reset rebind, collision refusal and ownership-safe release.");
    }

    private static void ValidateDrivingFeedback(GameObject prefab)
    {
        foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
        {
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null) continue;
                if (material.name.Contains("cabriophong1SG1") || material.name.Contains("RimBadge"))
                    if (material.GetTexture("_BaseColorMap") == null ||
                        material.GetFloat("_AlphaCutoffEnable") < .5f || !material.IsKeywordEnabled("_ALPHATEST_ON"))
                        throw new InvalidOperationException("C63 badge alpha mask is missing: " + material.name);
                if (renderer.name.Contains("M_LGlass") || material.name.Contains("phong20SG1"))
                    if (material.GetFloat("_SurfaceType") < .5f || material.GetColor("_BaseColor").a > (material.name.Contains("phong15SG1") ? .26f : .06f) ||
                        material.GetFloat("_Metallic") > .001f || material.GetFloat("_ZWrite") != 0f)
                        throw new InvalidOperationException("C63 clear lens is opaque/dark: " + renderer.name);
            }
        }
        var clone = UnityEngine.Object.Instantiate(prefab);
        try
        {
            MercedesC63CabrioMaterials.FixSolidMaterials(clone);
            var steering = clone.GetComponentsInChildren<Transform>(true).First(t => t.name == "C63SteeringWheel");
            var steeringPose = steering.localRotation;
            foreach (var wheel in clone.GetComponentsInChildren<Transform>(true).Where(t =>
                t.name.StartsWith("C63Wheel") && !t.name.Contains("_")))
            {
                var original = wheel.localRotation;
                var filters = wheel.GetComponentsInChildren<MeshFilter>(true);
                if (filters.Length != 4 || filters.Any(f => f.transform.localPosition != Vector3.zero ||
                    f.transform.localScale != Vector3.one || f.transform.localRotation != Quaternion.identity))
                    throw new InvalidOperationException("C63 wheel geometry has inherited transforms: " + wheel.name);
                Bounds baseline = default;
                for (var step = 0; step < 12; step++)
                {
                    wheel.localRotation = Quaternion.Euler(step * 30f, 0f, 0f);
                    var found = false; Bounds bounds = default;
                    foreach (var filter in filters)
                        foreach (var vertex in filter.sharedMesh.vertices)
                        {
                            var point = clone.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                            if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                            else bounds.Encapsulate(point);
                        }
                    if (step == 0) baseline = bounds;
                    if (Vector3.Distance(bounds.center, wheel.localPosition) > .003f ||
                        Mathf.Abs(bounds.size.y - baseline.size.y) > .004f ||
                        Mathf.Abs(bounds.size.z - baseline.size.z) > .004f)
                        throw new InvalidOperationException($"C63 wheel runout at {step * 30} degrees: {wheel.name}, {bounds}.");
                    if (steering.localRotation != steeringPose)
                        throw new InvalidOperationException("C63 steering wheel inherits rolling-wheel rotation.");
                }
                wheel.localRotation = original;
            }
            foreach (var renderer in clone.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.name.Contains("cabriophong1SG1") &&
                        (!material.IsKeywordEnabled("_ALPHATEST_ON") || material.GetFloat("_AlphaCutoffEnable") < .5f))
                        throw new InvalidOperationException("C63 instance initialization lost badge alpha.");
            var audio = typeof(MercedesC63CabrioMod).Assembly.GetType("MercedesC63CabrioAudioModel", true);
            var hz = audio.GetMethod("TargetHz", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = 0f;
            for (var step = 0; step <= 100; step++)
            {
                var current = (float)hz.Invoke(null, new object[] { step / 100f });
                if (current < previous || current < 79.99f || current > 200.01f)
                    throw new InvalidOperationException("C63 lowered V8 pitch curve is invalid.");
                previous = current;
            }
            Debug.Log("C63 driving feedback audit passed: masked badges, transparent neutral lenses, " +
                "12 wheel-spin phases without runout/steering movement, runtime material ownership and native idle and stationary AMG layer pitch curve.");
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }

    private static void ValidateCabinAxlesAndTuning(GameObject prefab)
    {
        foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var m in renderer.sharedMaterials)
            {
                if (m == null) continue;
                if (m.HasProperty("_SurfaceType") && m.GetFloat("_SurfaceType") == 0f &&
                    (!m.GetShaderPassEnabled("DepthOnly") || !m.GetShaderPassEnabled("ShadowCaster")))
                    throw new InvalidOperationException("C63 opaque depth/shadow pass disabled: " + m.name);
                if (m.name.Contains("phong11SG1") &&
                    (m.GetTextureScale("_BaseColorMap") != new Vector2(-128f,-128f) ||
                     m.GetTextureScale("_NormalMap") != new Vector2(-128f,-128f)))
                    throw new InvalidOperationException("C63 authored carbon UV tiling was lost.");
                if (m.name.Contains("phong9SG1") || m.name.Contains("phong2SG1") || m.name.Contains("phong4SG1"))
                {
                    if (!renderer.enabled || m.GetFloat("_AlphaCutoffEnable") != 1f ||
                        m.GetFloat("_SurfaceType") != 0f || m.GetFloat("_DoubleSidedEnable") != 1f ||
                        m.GetFloat("_CullMode") != 0f || !m.IsKeywordEnabled("_DOUBLESIDED_ON"))
                        throw new InvalidOperationException("C63 masked cabin/engine/chassis visibility failed: " + renderer.name);
                }
                if (m.name.Contains("phong12SG1") || m.name.Contains("phong17SG1"))
                    if (m.GetFloat("_DoubleSidedEnable") != 1f || m.GetFloat("_AlphaCutoffEnable") != 1f)
                        throw new InvalidOperationException("C63 perforated grille lost its two-sided alpha mask.");
                if (m.name.StartsWith("C63CarPaint") && m.GetFloat("_CullMode") != 0f)
                    throw new InvalidOperationException("C63 inner body faces are culled.");
            }
        var sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Mods/Mercedes_C63_Cabrio/Models/2018_mercedes_amg_c63_cabriolet.glb");
        var sourceCabin = sourceModel.GetComponentsInChildren<MeshFilter>(true)
            .Single(f => f.name.Contains("M_Interior_SetRT_Max"));
        var cabinTriangles = prefab.GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.name.Contains("M_Interior_SetRT_Max") || f.name == "C63SteeringGeometry")
            .Sum(f => f.sharedMesh.triangles.Length);
        if (cabinTriangles != sourceCabin.sharedMesh.triangles.Length)
            throw new InvalidOperationException("C63 cabin partition lost source triangles.");
        Debug.Log($"C63 cabin topology audit passed: {cabinTriangles/3} source triangles retained in cabin/steering partitions.");
        var physics = prefab.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        var gearbox = new SerializedObject(physics);
        if (Mathf.Abs(gearbox.FindProperty("powertrain.transmission._downshiftRPM").floatValue - MercedesC63CabrioTuning.DownshiftRpm) > .1f ||
            Mathf.Abs(physics.brakes.maxTorque / MercedesC63CabrioTuning.StockBrakeTorque - 1.25f) > .001f)
            throw new InvalidOperationException("C63 runtime/editor shift/brake calibration diverged.");
        var tcs = prefab.GetComponent<NWH.VehiclePhysics2.Modules.TCS.TCSModuleWrapper>();
        var abs = prefab.GetComponent<NWH.VehiclePhysics2.Modules.ABS.ABSModuleWrapper>();
        if (tcs == null || abs == null || tcs.module.slipThreshold != MercedesC63CabrioTuning.TractionSlipThreshold ||
            abs.module.slipThreshold != MercedesC63CabrioTuning.AbsSlipThreshold ||
            !physics.stateSettings.GetDefinition(typeof(NWH.VehiclePhysics2.Modules.TCS.TCSModule).FullName).isEnabled ||
            !physics.stateSettings.GetDefinition(typeof(NWH.VehiclePhysics2.Modules.ABS.ABSModule).FullName).isEnabled)
            throw new InvalidOperationException("C63 native ABS/TCS module/state missing.");
        var traction = typeof(MercedesC63CabrioMod).Assembly.GetType("MercedesC63CabrioTractionController", true);
        var targetGain = traction.GetMethod("TargetGain", BindingFlags.Static | BindingFlags.NonPublic);
        var followGain = traction.GetMethod("FollowGain", BindingFlags.Static | BindingFlags.NonPublic);
        float Target(float slip) => (float)targetGain.Invoke(null, new object[] {slip});
        if (Target(.10f) != 1f || Target(.50f) < .199f || Target(.18f) < .7f ||
            Target(.18f) > .9f || MercedesC63CabrioTuning.TractionSlipThreshold < .5f)
            throw new InvalidOperationException("C63 progressive traction reintroduced a binary power cut.");
        var nativeThreshold = traction.GetMethod("NativeThreshold", BindingFlags.Static | BindingFlags.NonPublic);
        float Threshold(int gear, float speed) => (float)nativeThreshold.Invoke(null, new object[] {gear, speed});
        if (!float.IsPositiveInfinity(Threshold(1, 1.8f)) || Threshold(1, 3f) != .65f ||
            Threshold(-1, -.5f) != .65f || Threshold(2, 1f) != .65f || Threshold(1, -.1f) != .65f)
            throw new InvalidOperationException("C63 binary traction cut is not gated to forward launch.");
        var gain = 1f;
        for (var step=0; step<40; step++)
        {
            var previousGain = gain;
            gain = (float)followGain.Invoke(null, new object[] {gain,Target(step%2==0 ? .22f : .12f),.02f});
            if (gain < .2f || gain > 1f || Mathf.Abs(gain-previousGain) > .15f)
                throw new InvalidOperationException("C63 traction gain jumps rather than smoothing.");
        }
        Debug.Log("C63 progressive traction audit passed: continuous rear-axle gain, bounded response and native severe-slip backstop.");
        if (gearbox.FindProperty("powertrain.transmission.postShiftBan").floatValue != 0f ||
            Mathf.Abs(gearbox.FindProperty("powertrain.transmission.shiftDuration").floatValue-.1f)>.0001f ||
            MercedesC63CabrioTuning.DrivetrainEfficiency!=.85f)
            throw new InvalidOperationException("C63 direction-change/driveline calibration mismatch.");
        var gears = new[] {4.38f,2.86f,1.92f,1.37f,1f,.82f,.73f};
        for (var i = 1; i < gears.Length; i++)
            if (MercedesC63CabrioTuning.UpshiftRpm * gears[i] / gears[i-1] < MercedesC63CabrioTuning.DownshiftRpm + 800f)
                throw new InvalidOperationException("C63 shift thresholds can hunt at gear " + (i + 1));
        foreach (var wc in prefab.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
            if (wc.FrictionPreset == null || wc.FrictionPreset.name != "C63RoadTireFriction" ||
                wc.FrictionPreset.GetPeakSlip() > .2f)
                throw new InvalidOperationException("C63 wheel retained the arcade tire curve.");
        var clone = UnityEngine.Object.Instantiate(prefab);
        var colliderPose = new GameObject("C63AuditWheelColliderPose");
        try
        {
            var clonePhysics = clone.GetComponent<NWH.VehiclePhysics2.VehicleController>();
            var modifiers = clonePhysics.powertrain.engine.powerModifiers;
            var initialCount = modifiers.Count;
            var component = (Behaviour)clone.AddComponent(traction);
            traction.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(component, new object[] {clonePhysics,null});
            if (modifiers.Count != initialCount+1)
                throw new InvalidOperationException("C63 progressive modifier did not bind exactly once.");
            component.enabled = false;
            // Editor-only clone: invoke runtime lifecycle explicitly (not ExecuteAlways).
            traction.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component,null);
            if (modifiers.Count != initialCount ||
                clone.GetComponent<NWH.VehiclePhysics2.Modules.TCS.TCSModuleWrapper>().module.slipThreshold != .18f)
                throw new InvalidOperationException("C63 traction teardown did not restore native safety.");
            component.enabled = true;
            traction.GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component,null);
            if (modifiers.Count != initialCount+1 ||
                clone.GetComponent<NWH.VehiclePhysics2.Modules.TCS.TCSModuleWrapper>().module.slipThreshold != .65f)
                throw new InvalidOperationException("C63 traction re-enable duplicated/missed the modifier.");
            traction.GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component,null);
            UnityEngine.Object.DestroyImmediate(component);
            if (modifiers.Count != initialCount)
                throw new InvalidOperationException("C63 traction destruction leaked engine callback.");
            Debug.Log("C63 traction lifecycle audit passed: one callback, safe disable/re-enable and no leaked modifier.");
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var wheelType = typeof(NWH.WheelController3D.WheelController);
            var update = wheelType.GetMethod("UpdateWheelValues", flags);
            foreach (var wc in clone.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
            {
                var visual = wc.wheel.visual.transform;
                var rim = visual.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name.Contains("M_Rim_Secondary"));
                var b = rim.sharedMesh.bounds;
                if (Mathf.Abs(b.center.y) > .0005f || Mathf.Abs(b.center.z) > .0005f || b.size.x > .030f ||
                    b.center.x * Mathf.Sign(wc.transform.localPosition.x) < .025f)
                    throw new InvalidOperationException("C63 rim axle is eccentric/tilted: " + wc.name + " " + b);
                void Set(string field, object value) => wheelType.GetField(field, flags).SetValue(wc, value);
                Set("_transform", wc.transform); Set("_transformRotation", wc.transform.rotation);
                Set("_upVector", Vector3.up); Set("_rightVector", Vector3.right); Set("_forwardVector", Vector3.forward);
                Set("_dt", 0f); Set("_damage", 0f);
                wc.wheel.visualTransform = visual; wc.wheel.colliderTransform = colliderPose.transform;
                wc.SteerAngle = wc.name.StartsWith("Front") ? 23f : 0f;
                wc.spring.length = .04f;
                for (var phase = 0; phase < 24; phase++)
                {
                    wc.wheel.axleAngle = phase * 15f;
                    update.Invoke(wc, null); // use the installed NWH pose equation, not an assumed roll transform
                    if (Vector3.Distance(visual.position, wc.transform.TransformPoint(new Vector3(0f,-.04f,0f))) > .0001f)
                        throw new InvalidOperationException("C63 native wheel pivot drifted.");
                    var rimCenter = rim.transform.TransformPoint(b.center);
                    var axis = visual.right;
                    var radial = rimCenter - visual.position;
                    radial -= axis * Vector3.Dot(radial, axis);
                    if (radial.magnitude > .0005f)
                        throw new InvalidOperationException("C63 rim precession under native steer/spin.");
                }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(colliderPose); UnityEngine.Object.DestroyImmediate(clone); }
        Debug.Log("C63 regression audit passed: two-sided masked detail atlases and opaque body, perforated grilles, concentric rims, " +
                  "24 native NWH spin/steer/suspension poses per corner, road tire curve, ABS/TCS, safe shift gap and 1.25 brake factor.");
    }

    private static void ValidateStanceAndBadges(GameObject prefab)
    {
        foreach (var wc in prefab.GetComponentsInChildren<NWH.WheelController3D.WheelController>(true))
        {
            var front = wc.name.StartsWith("Front"); var radius = front ? .3306f : .32835f;
            var expectedX = (front ? 1.63576f : 1.59258f)*.5f-.09f;
            if (Mathf.Abs(Mathf.Abs(wc.transform.localPosition.x)-expectedX)>.0001f)
                throw new InvalidOperationException("C63 requested 50mm inward wheel move is missing: " + wc.name);
            var expectedZ = (front ? 1f : -1f) * MercedesC63CabrioTuning.Wheelbase * .5f + (front ? MercedesC63CabrioTuning.FrontWheelForwardOffset : MercedesC63CabrioTuning.RearWheelForwardOffset);
            if (Mathf.Abs(wc.transform.localPosition.z - expectedZ) > .0001f ||
                Mathf.Abs(wc.transform.localPosition.y - radius - .085f) > .0001f)
                throw new InvalidOperationException("C63 revised wheel/ground mount mismatch: " + wc.name);
            var visual = wc.wheel.visual.transform;
            if (Mathf.Abs(visual.localPosition.y - radius) > .0001f ||
                Mathf.Abs(wc.spring.length - MercedesC63CabrioTuning.InitialSpringLength) > .0001f)
                throw new InvalidOperationException("C63 parked wheel floats before native suspension starts: " + wc.name);
        }
        foreach (var point in new[] {new Vector3(0f,0f,1.41986f),new Vector3(0f,0f,-1.41986f)})
        {
            var expected = point.z > 0f ? -.03f : -.02f;
            if (Mathf.Abs(MercedesC63CabrioTuning.BodyPoint(point).y - expected) > .0001f)
                throw new InvalidOperationException("C63 front/rear body drop mismatch.");
        }
        var badge = prefab.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name.Contains("M_Badge_Max"));
        var material = badge.sharedMaterial;
        if (badge.GetComponent<MeshFilter>().sharedMesh.name != "C63BadgeFaces" ||
            material.GetFloat("_Metallic") != .25f || material.GetFloat("_ZTestDepthEqualForOpaque") != 3f ||
            material.GetFloat("_AlphaCutoffPrepass") != .15f || material.GetFloat("_AlphaCutoffPostpass") != .15f)
            throw new InvalidOperationException($"C63 badge visibility/depth setup failed: mesh={badge.GetComponent<MeshFilter>().sharedMesh.name}, metallic={material.GetFloat("_Metallic")}, depth={material.GetFloat("_ZTestDepthEqualForOpaque")}, pre={material.GetFloat("_AlphaCutoffPrepass")}, post={material.GetFloat("_AlphaCutoffPostpass")}.");
        // HDRP's editor validator saves Equal for opaque depth. Our instance
        // initialization must apply the cutout's LessEqual after that validator.
        var instance = UnityEngine.Object.Instantiate(prefab);
        try
        {
            var materialResult = MercedesC63CabrioMaterials.FixSolidMaterials(instance);
            if (materialResult.OpaqueMaterialsFixed == 0)
                throw new InvalidOperationException("C63 runtime did not repair opaque pass coverage.");
            var liveBadge = instance.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name.Contains("M_Badge_Max")).sharedMaterial;
            foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                foreach (var m in renderer.sharedMaterials)
                    if (m != null && m.HasProperty("_SurfaceType") && m.GetFloat("_SurfaceType") == 0f &&
                        (!m.GetShaderPassEnabled("DepthOnly") || !m.GetShaderPassEnabled("ShadowCaster")))
                        throw new InvalidOperationException("C63 runtime disabled opaque depth/shadow: " + m.name);
            if (liveBadge.GetFloat("_ZTestDepthEqualForOpaque") != 4f || liveBadge.GetFloat("_Metallic") != .25f)
                throw new InvalidOperationException("C63 initialized badge lost diffuse/depth visibility.");
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
        var texturePath = AssetDatabase.GetAssetPath(material.GetTexture("_BaseColorMap"));
        // Bundle textures have no editor importer; validate import settings on the source asset.
        if (!string.IsNullOrEmpty(texturePath))
        {
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null || !importer.mipMapsPreserveCoverage ||
                importer.textureCompression != TextureImporterCompression.Uncompressed)
                throw new InvalidOperationException("C63 thin badge coverage is not preserved.");
        }
        Debug.Log("C63 stance/badge audit passed: front/rear cumulative +100/+105 mm wheel Z, retained +50 mm contact mounts, -30/-20 mm front/rear body pose, separated silver badge faces and alpha coverage.");
    }

    public static void ReleaseNativePrefabRegistration()
    {
        typeof(MercedesC63CabrioMod).Assembly.GetType("MercedesC63CabrioPlayerPrefabRegistration", true)
            .GetMethod("Release", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }

    public static void Validate(GameObject prefab)
    {
        foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && (material.name.Contains("phong9SG1") || material.name.Contains("3DWh_c96cb19")))
                    if (!material.HasProperty("_BaseColorMap") || material.GetTexture("_BaseColorMap") == null)
                        throw new InvalidOperationException($"C63 interior/tire texture missing: {material.name}.");
        ValidateDrivingFeedback(prefab);
        ValidateCabinAxlesAndTuning(prefab);
        ValidateStanceAndBadges(prefab);
        var native = prefab.GetComponent<VehicleController>();
        var serialized = new SerializedObject(native);
        foreach (var property in new[] { "sleepEnvironment.config", "washVfx", "washAudioSource", "washSoundLoop", "washSoundEnd" })
            if (serialized.FindProperty(property)?.objectReferenceValue == null)
                throw new InvalidOperationException($"C63 native activity reference missing: {property}.");
        var physics = prefab.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        if (physics == null || physics.steering.steeringWheel == null ||
            physics.steering.steeringWheel.name != "C63SteeringWheel" ||
            physics.steering.steeringWheelTurnRatio != 0f)
            throw new InvalidOperationException("C63 native steering binding missing.");
        var rearDifferential = physics.powertrain.differentials.First(d => d.name == "Rear Differential");
        if (rearDifferential.DifferentialType != NWH.VehiclePhysics2.Powertrain.DifferentialComponent.Type.LimitedSlip)
            throw new InvalidOperationException("C63 mechanical rear limited-slip differential is missing.");
        var curve = physics.powertrain.engine.powerCurve;
        foreach (var rpm in new[] {1750f, 2500f, 3500f, 4500f})
        {
            var torque = curve.Evaluate(rpm / 7000f) * 350f * 9549.3f / rpm;
            if (Mathf.Abs(torque - 650f) > 1f) throw new InvalidOperationException($"C63 torque mismatch at {rpm}: {torque}.");
        }
        foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.enabled && renderer.name != "C63DamageBody")
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && material.name.StartsWith("C63CarPaint"))
                        throw new InvalidOperationException($"C63 body paint leaks to {renderer.name}.");
        var clone = UnityEngine.Object.Instantiate(prefab);
        try
        {
            var assembly = typeof(MercedesC63CabrioMod).Assembly;
            var type = assembly.GetType("MercedesC63CabrioLightingController", true);
            var lighting = clone.AddComponent(type);
            type.GetMethod("Initialize").Invoke(lighting, new object[] { clone.GetComponent<VehicleController>(), null });
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if ((int)type.GetMethod("CountLampOverlays", flags).Invoke(lighting, null) != 8 ||
                (int)type.GetMethod("CountBlinkerOverlays", flags).Invoke(lighting, null) != 4)
                throw new InvalidOperationException("C63 lamp partition count mismatch.");
            var second = UnityEngine.Object.Instantiate(prefab);
            try
            {
                second.transform.SetPositionAndRotation(new Vector3(123.4f, 2.1f, -89.5f), Quaternion.Euler(0, 37, 0));
                var secondLighting = second.AddComponent(type);
                type.GetMethod("Initialize").Invoke(secondLighting, new object[] { second.GetComponent<VehicleController>(), null });
                if ((int)type.GetField("partitionBuilds", flags).GetValue(secondLighting) != 0)
                    throw new InvalidOperationException("C63 moving/spawned instance rebuilt cached lamp topology.");
                var firstParts = clone.GetComponentsInChildren<MeshFilter>(true)
                    .Where(f => f.name.StartsWith("MercedesC63Cabrio_")).ToDictionary(f => f.name, f => f.sharedMesh);
                foreach (var filter in second.GetComponentsInChildren<MeshFilter>(true))
                    if (firstParts.TryGetValue(filter.name, out var shared) && filter.sharedMesh != shared)
                        throw new InvalidOperationException("C63 lamp partition was not shared: " + filter.name);
            }
            finally { UnityEngine.Object.DestroyImmediate(second); }
            Debug.Log("C63 spawn performance audit passed: second translated/rotated vehicle reused all 12 lamp partitions.");
            var names = new HashSet<string>();
            foreach (var filter in clone.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.name.StartsWith("MercedesC63Cabrio_")) continue;
                if (filter.sharedMesh == null || filter.sharedMesh.triangles.Length < 3)
                    throw new InvalidOperationException($"Empty generated lamp: {filter.name}.");
                var b = filter.GetComponent<Renderer>().bounds;
                if (filter.name.Contains("Front") || filter.name.Contains("Daytime") || filter.name.Contains("Projector"))
                    if (b.center.z < 1.8f) throw new InvalidOperationException($"Front lamp on wrong vehicle end: {filter.name}.");
                if (filter.name.Contains("Rear") || filter.name.Contains("Reverse") || filter.name.Contains("Third"))
                    if (b.center.z > -1.6f) throw new InvalidOperationException($"Rear lamp on wrong vehicle end: {filter.name}.");
                if (filter.transform.localPosition != Vector3.zero || filter.transform.localScale != Vector3.one)
                    throw new InvalidOperationException($"Nonidentity lamp transform: {filter.name}.");
                names.Add(filter.name);
            }
            var parts = clone.GetComponentsInChildren<MeshFilter>(true).Where(f => names.Contains(f.name)).ToDictionary(f => f.name, f => f,
                StringComparer.Ordinal);
            MeshFilter Lamp(string suffix) => parts["MercedesC63Cabrio_" + suffix];
            var running = Lamp("RearRunningOutline");
            var brake = Lamp("RearBrakeSignature");
            var third = Lamp("ThirdBrakeLight");
            var reverse = Lamp("ReverseLight");
            var left = Lamp("RearLeftIndicator");
            var right = Lamp("RearRightIndicator");
            var rearState = type.GetMethod("ApplyRearLampState", flags);
            var rear = new[] {running, brake, reverse, left, right, third};
            for (var bits = 0; bits < 32; bits++)
            {
                var states = Enumerable.Range(0, 5).Select(index => (bits & (1 << index)) != 0).ToArray();
                rearState.Invoke(lighting, states.Cast<object>().ToArray());
                for (var index = 0; index < rear.Length; index++)
                    if (rear[index].GetComponent<Renderer>().enabled != states[index == 5 ? 1 : index])
                        throw new InvalidOperationException("C63 rear state mismatch: " + rear[index].name);
            }
            rearState.Invoke(lighting, new object[] {false, false, false, false, false});

            var usedFaces = new HashSet<(Vector3Int, Vector3Int, Vector3Int)>();
            Vector3Int Quantize(Vector3 point) => Vector3Int.RoundToInt(point * 10000f);
            (Vector3[] Vertices, HashSet<Bounds> Islands) CheckGeometry(MeshFilter lamp)
            {
                var toBody = MercedesC63CabrioTuning.BodyStance.inverse * clone.transform.worldToLocalMatrix *
                    lamp.transform.localToWorldMatrix;
                var mesh = lamp.sharedMesh;
                var positions = mesh.vertices.Select(v => MercedesC63CabrioMeshParts.ToLegacyUnposedBodyBounds(
                    new Bounds(toBody.MultiplyPoint3x4(v), Vector3.zero)).center).ToArray();
                var islands = new HashSet<Bounds>();
                MercedesC63CabrioMeshParts.Select(mesh, toBody, bounds =>
                {
                    islands.Add(MercedesC63CabrioMeshParts.ToLegacyUnposedBodyBounds(bounds));
                    return false;
                });
                var triangles = mesh.triangles;
                for (var i = 0; i < triangles.Length; i += 3)
                    if (!usedFaces.Add((Quantize(positions[triangles[i]]), Quantize(positions[triangles[i + 1]]),
                        Quantize(positions[triangles[i + 2]]))))
                        throw new InvalidOperationException("C63 rear lamps share an emitting face: " + lamp.name);
                return (positions, islands);
            }
            var outline = CheckGeometry(running).Islands;
            var upperBrakeIslands = CheckGeometry(brake).Islands;
            var lowerInner = CheckGeometry(reverse).Islands;
            var lowerLeft = CheckGeometry(left).Vertices;
            var lowerRight = CheckGeometry(right).Vertices;
            foreach (var side in new[] {-1f, 1f})
            {
                if (outline.Count(b => b.center.x * side > 0f) < 6 ||
                    !upperBrakeIslands.Any(b => b.center.x * side > .4f && b.center.x * side < .52f && b.center.y > .81f) ||
                    !upperBrakeIslands.Any(b => b.center.x * side > .55f && b.center.y > .81f) ||
                    lowerInner.Count(b => b.center.x * side > .4f && b.center.x * side < .5f && b.center.y < .75f) != 1)
                    throw new InvalidOperationException($"C63 rear lamp sections missing on side {side}: " +
                        $"outline=[{string.Join(", ", outline.Select(b => b.center.ToString("F5")))}], " +
                        $"brake=[{string.Join(", ", upperBrakeIslands.Select(b => b.center.ToString("F5")))}], " +
                        $"reverse=[{string.Join(", ", lowerInner.Select(b => b.center.ToString("F5")))}].");
            }
            if (lowerLeft.Any(v => v.x > -.50f || v.y > .785f) ||
                lowerRight.Any(v => v.x < .50f || v.y > .785f))
                throw new InvalidOperationException("C63 rear indicators extend outside their lower outer inserts.");
            Debug.Log("C63 rear lamp audit passed: independent running outlines, upper brakes, inner reverse, outer indicators and third brake.");
            if (names.Count != 12) throw new InvalidOperationException($"C63 generated lamp meshes: {names.Count}, expected 12.");
            var beams = clone.GetComponentsInChildren<Light>(true).Where(l => l.name.Contains("HeadlightBeam")).ToArray();
            if (beams.Length != 2 || beams.Any(l => l.intensity < 10f))
                throw new InvalidOperationException("C63 HDRP road lights missing or too weak.");
            var damageType = assembly.GetType("MercedesC63CabrioVisualDamageController", true);
            var damage = clone.AddComponent(damageType);
            var select = typeof(MercedesC63CabrioRuntime).GetMethod("IsDeformableExterior", BindingFlags.Static | BindingFlags.NonPublic);
            var filters = clone.GetComponentsInChildren<MeshFilter>(true).Where(f => (bool)select.Invoke(null, new object[] { f })).ToList();
            if (filters.Count < 25 || filters.Any(f => f.name.Contains("Steering") || f.name.Contains("Wheel")))
                throw new InvalidOperationException("C63 exterior damage ownership selection is invalid.");
            var runningSource = running.sharedMesh;
            var runningRest = runningSource.vertices;
            if (running.GetComponent<Renderer>().enabled || rear.Any(lamp => !filters.Contains(lamp)))
                throw new InvalidOperationException("C63 unlit rear lamps must be registered for deformation.");
            damageType.GetMethod("Initialize", flags).Invoke(damage, new object[] {clone.GetComponent<VehicleController>(),
                clone.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true), null, filters, 5f});
            if (!filters.Any(f => f.name.Contains("M_Chassis_Max")) || !filters.Any(f => f.name.Contains("M_Engine_Max")))
                throw new InvalidOperationException("C63 end-region structural supports excluded from damage.");
            var restVertices = filters.ToDictionary(f => f, f => f.sharedMesh.vertices);
            var impact = damageType.GetMethod("ApplyImpact", flags);
            var field = damageType.GetMethod("EndInfluence", BindingFlags.Static | BindingFlags.NonPublic);
            var upper = (float)field.Invoke(null, new object[] {new Vector3(.4f,.7f,.05f),false});
            var lower = (float)field.Invoke(null, new object[] {new Vector3(.4f,0f,.05f),false});
            if (upper < .85f || Mathf.Abs(upper-lower) > .01f)
                throw new InvalidOperationException("C63 upper/lower end pieces do not share a coherent crush field.");
            clone.transform.SetPositionAndRotation(new Vector3(23f,2f,-19f), Quaternion.Euler(0f,71f,0f));
            foreach (var end in new[] {-1f,1f})
            {
                var point = clone.transform.TransformPoint(new Vector3(0f,.35f,end*2.2f));
                var normal = clone.transform.TransformDirection(new Vector3(0f,0f,-end));
                var changed = (int)impact.Invoke(damage, new object[] {new[] {point}, new[] {normal}, 30f});
                if (changed < 6) throw new InvalidOperationException("C63 incomplete front/rear damage coverage: " + changed);
                if (end < 0f)
                {
                    var dented = running.sharedMesh.vertices;
                    for (var side = -1f; side <= 1f; side += 2f)
                        if (!Enumerable.Range(0, dented.Length).Any(index =>
                            side * clone.transform.InverseTransformPoint(running.transform.TransformPoint(runningRest[index])).x > .4f &&
                            running.transform.TransformVector(dented[index] - runningRest[index]).sqrMagnitude > .000001f))
                            throw new InvalidOperationException("C63 rear running outline did not follow the impact on side " + side);
                    if (running.sharedMesh == runningSource || !runningSource.vertices.SequenceEqual(runningRest))
                        throw new InvalidOperationException("C63 rear running outline deformed the shared lamp mesh.");
                }
                for (var repeat=0;repeat<6;repeat++) impact.Invoke(damage,new object[] {new[] {point},new[] {normal},30f});
            }
            foreach (var filter in filters)
            {
                var rest = restVertices[filter]; var dented = filter.sharedMesh.vertices;
                for (var v=0;v<rest.Length;v++)
                    if (filter.transform.TransformVector(dented[v]-rest[v]).magnitude > .2001f)
                        throw new InvalidOperationException("C63 cumulative deformation exceeded its bound.");
                if (filter.name.Contains("M_Chassis_Max") || filter.name.Contains("M_Engine_Max"))
                    for (var v=0;v<rest.Length;v++)
                        if (Mathf.Abs(clone.transform.InverseTransformPoint(filter.transform.TransformPoint(rest[v])).z)<1.35f && dented[v]!=rest[v])
                            throw new InvalidOperationException("C63 structural cabin/floor center was deformed.");
            }
            var cleanBody=prefab.GetComponentsInChildren<MeshFilter>(true).Single(f=>f.name=="C63DamageBody");
            var dentedBody=filters.Single(f=>f.name=="C63DamageBody");
            if (!cleanBody.sharedMesh.vertices.SequenceEqual(restVertices[dentedBody]))
                throw new InvalidOperationException("C63 crash modified a shared prefab/second-instance body mesh.");
            clone.GetComponent<VehicleDeformationController>().Reset();
            foreach (var filter in filters)
            {
                damageType.GetMethod("GetWritableMesh", flags).Invoke(damage, new object[] {filter});
                if (!filter.sharedMesh.vertices.SequenceEqual(restVertices[filter]))
                    throw new InvalidOperationException("C63 regional impact repair failed.");
            }
            Debug.Log("C63 regional damage audit passed: translated/rotated front and rear impacts, support coverage, coherent upper/lower crush, 0.20m end cumulative cap and full repair.");
            var original = filters.ToDictionary(f => f, f => f.sharedMesh.vertices[0]);
            foreach (var filter in filters)
            { var v = filter.sharedMesh.vertices; v[0] += Vector3.one; filter.sharedMesh.vertices = v; }
            clone.GetComponent<VehicleDeformationController>().Reset();
            foreach (var filter in filters)
            {
                if (filter.sharedMesh.vertices[0] != original[filter]) throw new InvalidOperationException("Native C63 repair failed.");
                var restored = (Mesh)damageType.GetMethod("GetWritableMesh", flags).Invoke(damage, new object[] { filter });
                if (restored.vertices[0] != original[filter]) throw new InvalidOperationException("C63 post-repair damage mesh not restored.");
            }
            Debug.Log($"C63 repair audit passed: {filters.Count} exterior meshes restored by native Reset; cabin and wheels excluded.");
            Debug.Log("C63 functional asset audit passed: 12 nonempty lamp surfaces, 2 HDRP beams, steering, paint isolation, 650 Nm curve, sleep and washing.");
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }

    public static void DumpLampAndBadgeGeometry()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mods/Mercedes_C63_Cabrio/Mercedes.prefab");
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.name.Contains("phong14SG1") && !filter.name.Contains("M_Badge_Max")) continue;
            var matrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            var seen = new HashSet<string>();
            MercedesC63CabrioMeshParts.Select(filter.sharedMesh, matrix, actual => {
                var b = MercedesC63CabrioMeshParts.ToLegacyBodyBounds(actual);
                var key = $"{b.center:F5}/{b.size:F5}";
                if (seen.Add(key)) Debug.Log("C63 source part " + filter.name + " " + key);
                return false;
            });
        }
    }

    private sealed class LaunchRoadLoad : NWH.VehiclePhysics2.Powertrain.PowertrainComponent
    {
        internal float angularVelocity;
        internal readonly float massInertia = 1909f * .32835f * .32835f / (4.38f * 3.06f * 4.38f * 3.06f);
        public override float QueryAngularVelocity(float velocity, float inputInertia) => angularVelocity;
        public override float QueryInertia() => .024f;
        public override float ForwardStep(float torque, float inputInertia, float dt)
        {
            var total = massInertia + .024f + inputInertia;
            angularVelocity += torque / total * dt;
            return -torque * massInertia / total;
        }
    }

    public static void VerifyLoadedLaunch()
    {
        // Native engine + native clutch, with a deterministic reflected chassis
        // load in place of the tire/contact simulation. This checks coupled RPM
        // response; it is not a substitute for an in-game traction/launch test.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mods/Mercedes_C63_Cabrio/Mercedes.prefab");
        float Run(float range, float pedal)
        {
            var clone = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var vc = clone.GetComponent<NWH.VehiclePhysics2.VehicleController>();
                var engine = vc.powertrain.engine; var clutch = vc.powertrain.clutch;
                var road = new LaunchRoadLoad();
                var component = typeof(NWH.VehiclePhysics2.Powertrain.PowertrainComponent);
                var vehicle = typeof(NWH.VehiclePhysics2.VehicleComponent).GetField("vehicleController", flags);
                vehicle.SetValue(engine,vc); vehicle.SetValue(clutch,vc);
                component.GetField("_output",flags).SetValue(engine,clutch);
                component.GetField("_output",flags).SetValue(clutch,road);
                var velocity = component.GetField("outputAngularVelocity",flags);
                engine.ignition=true; engine.canRun=true; engine.autoStartOnThrottle=false;
                // Authored forced-induction multiplier is 1: disable only its
                // Time.deltaTime boost envelope in this fixed-step fixture.
                if (engine.forcedInduction.powerGainMultiplier != 1f)
                    throw new InvalidOperationException("C63 launch fixture expects the authored unity boost multiplier.");
                engine.forcedInduction.useForcedInduction=false;
                engine.powerModifiers.Clear(); engine.powerModifiers.Add(()=>MercedesC63CabrioTuning.DrivetrainEfficiency);
                var calculate = typeof(NWH.VehiclePhysics2.Powertrain.EngineComponent).GetMethod("CalculateTorqueICE",flags);
                engine.calculateTorqueDelegate = (NWH.VehiclePhysics2.Powertrain.EngineComponent.CalculateTorque)
                    Delegate.CreateDelegate(typeof(NWH.VehiclePhysics2.Powertrain.EngineComponent.CalculateTorque),engine,calculate);
                velocity.SetValue(engine,768f*Mathf.PI/30f);
                typeof(NWH.VehiclePhysics2.Powertrain.EngineComponent).GetField("_rpmPercent",flags).SetValue(engine,768f/7000f);
                clutch.engagementRange=range;
                var throttle=vc.input.GetType().GetField("_inputSwappedThrottle",flags);
                var forwardVelocity=typeof(NWH.Common.Vehicles.Vehicle).GetField("<LocalForwardVelocity>k__BackingField",flags);
                var peak=768f; var drop=0f;
                for(var step=0;step<50;step++)
                {
                    throttle.SetValue(vc.input,pedal);
                    forwardVelocity.SetValue(vc,road.angularVelocity*.32835f/(4.38f*3.06f));
                    engine.IntegrateDownwards(.02f);
                    var rpm=engine.OutputRPM; peak=Mathf.Max(peak,rpm); drop=Mathf.Max(drop,peak-rpm);
                    if (float.IsNaN(rpm) || rpm<engine.idleRPM*.5f || rpm>engine.revLimiterRPM || engine.revLimiterActive)
                        throw new InvalidOperationException($"C63 loaded launch is unstable: pedal={pedal}, rpm={rpm}.");
                }
                if (road.angularVelocity<=0f || !engine.IsRunning)
                    throw new InvalidOperationException("C63 loaded launch lost drive or stalled.");
                return drop;
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }
        var oldDrop=Run(500f,1f);
        var newDrop=Run(MercedesC63CabrioTuning.ClutchEngagementRange,1f);
        if (oldDrop<40f || newDrop>10f || newDrop>oldDrop*.2f)
            throw new InvalidOperationException($"C63 loaded launch regression: oldDrop={oldDrop}, newDrop={newDrop} rpm.");
        Run(MercedesC63CabrioTuning.ClutchEngagementRange,.75f);
        Run(MercedesC63CabrioTuning.ClutchEngagementRange,.5f);
        Debug.Log($"C63 loaded launch audit passed: native engine/clutch, chassis load, old/new peak RPM dip={oldDrop:0.0}/{newDrop:0.0}; half/three-quarter/full throttle, speed-based native lockup and no stall. In-game traction still requires a drive test.");
    }

    private sealed class LaunchClutchTerminal : NWH.VehiclePhysics2.Powertrain.PowertrainComponent { }

    public static void VerifyNativeLaunchClutch()
    {
        // Replay the CSV's first engaged frame through the installed native clutch.
        // A terminal native component isolates clutch transfer from wheel dynamics.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mods/Mercedes_C63_Cabrio/Mercedes.prefab");
        var clone = UnityEngine.Object.Instantiate(prefab);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            var vc = clone.GetComponent<NWH.VehiclePhysics2.VehicleController>();
            var clutch = vc.powertrain.clutch;
            var engine = vc.powertrain.engine;
            if (clutch.engagementRPM != MercedesC63CabrioTuning.ClutchEngagementRpm ||
                clutch.throttleEngagementOffsetRPM != MercedesC63CabrioTuning.ClutchThrottleOffsetRpm ||
                clutch.engagementRange != MercedesC63CabrioTuning.ClutchEngagementRange ||
                clutch.creepTorque != 0f || clutch.slipTorque != 780f)
                throw new InvalidOperationException("C63 authored launch clutch differs from runtime calibration.");
            var componentType = typeof(NWH.VehiclePhysics2.Powertrain.PowertrainComponent);
            var outputVelocity = componentType.GetField("outputAngularVelocity", fields);
            componentType.GetField("_output", fields).SetValue(clutch,
                new LaunchClutchTerminal());
            typeof(NWH.VehiclePhysics2.VehicleComponent).GetField("vehicleController", fields).SetValue(clutch, vc);
            var throttle = vc.input.GetType().GetField("_inputSwappedThrottle", fields);
            if (clutch.inputNameHash == 0 || clutch.outputNameHash == 0)
                throw new InvalidOperationException("C63 launch clutch is disconnected.");
            vc.powertrain.transmission.isShifting = false;
            float Transfer(float rpm, float pedal)
            {
                throttle.SetValue(vc.input, pedal);
                var angularVelocity = rpm * Mathf.PI / 30f;
                outputVelocity.SetValue(engine, angularVelocity);
                outputVelocity.SetValue(clutch, 0f);
                clutch.QueryAngularVelocity(angularVelocity, .3f);
                clutch.ForwardStep(300f, .3f, .02f);
                return clutch.outputTorque;
            }
            // The previous setup must reproduce the measured open-clutch interval.
            clutch.engagementRPM = 1250f;
            clutch.throttleEngagementOffsetRPM = 500f;
            clutch.engagementRange = 500f;
            if (Transfer(1129.301f, 1f) != 0f || clutch.clutchInput != 0f)
                throw new InvalidOperationException("C63 old launch-delay fixture did not reproduce.");
            Action configure = () => typeof(MercedesC63CabrioRuntime)
                .GetMethod("ConfigurePowertrain", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] {clone});
            configure();
            foreach (var pedal in new[] {.25f, .5f, 1f})
            {
                var torque = Transfer(1129.301f, pedal);
                if (Mathf.Abs(clutch.clutchInput - .229301f) > .001f || torque < 178f)
                    throw new InvalidOperationException($"C63 first-frame launch transfer failed: pedal={pedal}, clutch={clutch.clutchInput}, torque={torque}.");
                if (Transfer(768.062f, pedal) != 0f || clutch.clutchInput != 0f)
                    throw new InvalidOperationException("C63 below-idle clutch safeguard failed.");
            }
            if (Transfer(900f, 0f) != 0f || clutch.creepTorque != 0f)
                throw new InvalidOperationException("C63 stationary no-creep safeguard failed.");
            Transfer(1900f, 1f);
            if (Mathf.Abs(clutch.clutchInput - 1f) > .001f)
                throw new InvalidOperationException("C63 launch clutch does not fully engage at 1,900 rpm.");
            vc.powertrain.transmission.isShifting = true;
            vc.powertrain.transmission.shiftProgress = .5f;
            if (Transfer(3000f, 1f) > .01f)
                throw new InvalidOperationException("C63 native gear-change clutch opening failed.");
            vc.powertrain.transmission.isShifting = false;
            configure(); // Same calibration is reapplied to restored/existing vehicles.
            if (Transfer(1129.301f, 1f) < 178f ||
                clutch.engagementRPM != MercedesC63CabrioTuning.ClutchEngagementRpm ||
                clutch.throttleEngagementOffsetRPM != MercedesC63CabrioTuning.ClutchThrottleOffsetRpm ||
                vc.powertrain.transmission.shiftDuration != MercedesC63CabrioTuning.ShiftDuration)
                throw new InvalidOperationException("C63 repeated launch calibration changed drivetrain behavior.");
            Debug.Log("MercedesC63Cabrio native launch clutch audit passed: old CSV dead zone reproduced, " +
                "new first-frame torque at 1,129 rpm, partial/full throttle, progressive closure, below-idle/no-creep safeguards and repeated runtime configuration.");
        }
        finally { UnityEngine.Object.DestroyImmediate(clone); }
    }

    public static void VerifyNativeImpactDamage() => ValidateNativePercentageDamage();

    private static void ValidateNativePercentageDamage()
    {
        // Replay deterministic contact fixtures through the installed game's real
        // DamageHandler, not a second implementation of its damage calculation.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mods/Mercedes_C63_Cabrio/Mercedes.prefab");
        var clone = UnityEngine.Object.Instantiate(prefab);
        var target = new GameObject("C63NativeDamageContactFixture");
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        try
        {
            var collider = target.AddComponent<CapsuleCollider>();
            collider.radius=.12f; collider.height=4f;
            var handler = clone.GetComponent<NWH.VehiclePhysics2.Damage.DamageHandler>();
            var type = handler.GetType();
            var body = clone.GetComponent<Rigidbody>();
            // Edit-mode instantiation does not call native Awake; it initializes
            // the game layer cache as well as the Rigidbody reference.
            type.GetMethod("Awake", fields).Invoke(handler,null);
            // Isolate overall percentage from unrelated wheel/engine sub-damage.
            type.GetField("_vehicleController", fields).SetValue(handler, null);
            handler.meshDeform = false;
            Action configure = () => typeof(MercedesC63CabrioRuntime)
                .GetMethod("ConfigureNativeDamage", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] {handler});
            var entry = type.GetMethod("OnCollisionEnter", fields);
            var lastTime = type.GetField("lastCollisionTime", fields);
            Action<float> age = seconds => lastTime.SetValue(handler, Time.realtimeSinceStartup-seconds);
            Action<Collision> hit = contact => entry.Invoke(handler, new object[] {contact});
            Action<object,string,object> set = (value,name,data) => value.GetType().GetField(name, fields).SetValue(value,data);
            Collision Contact(float end, float speed, float impulseScale=1f, Vector3? normal=null)
            {
                var contact = new Collision();
                object pair = Activator.CreateInstance(typeof(Collision).Assembly.GetType("UnityEngine.ContactPair",true));
                set(pair,"m_ColliderID",collider.GetInstanceID());
                set(pair,"m_NbPoints",(uint)1);
                set(pair,"m_ImpulseSum",Vector3.forward*end*body.mass*speed*impulseScale);
                object header = Activator.CreateInstance(typeof(Collision).Assembly.GetType("UnityEngine.ContactPairHeader",true));
                set(header,"m_RelativeVelocity",Vector3.forward*end*speed);
                object point = new ContactPoint();
                set(point,"m_Point",clone.transform.TransformPoint(new Vector3(0,.35f,end*2.2f)));
                set(point,"m_Normal",normal??Vector3.back*end);
                set(contact,"m_Pair",pair); set(contact,"m_Header",header);
                set(contact,"m_Flipped",true);
                set(contact,"m_LegacyContacts",new[] {(ContactPoint)point});
                return contact;
            }
            handler.SetDamage(.35f); handler.enabled=false; handler.visualOnly=true;
            configure();
            if (!handler.enabled || handler.visualOnly || handler.Damage!=.35f ||
                handler.decelerationThreshold!=200f || handler.collisionTimeout!=.2f)
                throw new InvalidOperationException("C63 native damage configuration/reset regression.");
            handler.SetDamage(0f); handler.decelerationThreshold=500f;
            age(2f); hit(Contact(1,2.5f));
            if(handler.Damage!=0f) throw new InvalidOperationException("C63 old-threshold regression fixture invalid.");
            configure();
            foreach (var end in new[] {-1f,1f})
                foreach (var speed in new[] {2.5f,4.9f,10f})
                {
                    handler.SetDamage(0f); age(2f); hit(Contact(end,speed));
                    var expected = speed/(Time.fixedDeltaTime*10f)*.99f*.005f;
                    if (Mathf.Abs(handler.Damage-expected)>.0001f)
                        throw new InvalidOperationException($"C63 native percentage failed: end={end}, speed={speed}, damage={handler.Damage}.");
                }
            handler.SetDamage(0f); age(2f); hit(Contact(1,1.5f));
            if (handler.Damage!=0f) throw new InvalidOperationException("C63 parking contact damage filter failed.");
            handler.SetDamage(0f); age(2f); hit(Contact(1,4f));
            var first=handler.Damage;
            age(.1f); hit(Contact(1,4f));
            if (handler.Damage!=first) throw new InvalidOperationException("C63 same-impact cooldown failed.");
            age(.25f); hit(Contact(-1,4f));
            if (handler.Damage<=first) throw new InvalidOperationException("C63 subsequent reverse impact lost percentage damage.");
            foreach (var tag in new[] {"Wheel","ExitTrigger"})
            {
                target.tag=tag; handler.SetDamage(0f); age(2f); hit(Contact(1,10f));
                if(handler.Damage!=0f) throw new InvalidOperationException("C63 native ignored tag damaged car: "+tag);
            }
            target.tag="Untagged";
            foreach (var layer in new[] {"Ground","GroundUnplacable","Roads"})
            {
                target.layer=LayerMask.NameToLayer(layer);
                if(target.layer<0) throw new InvalidOperationException("Native damage layer missing: "+layer);
                handler.SetDamage(0f); age(2f); hit(Contact(-1,10f));
                if(handler.Damage!=0f) throw new InvalidOperationException("C63 native road/ground exclusion failed: "+layer);
            }
            // Real native handler + actual car save/UI method, with a narrow
            // StreetProps collider and independently supplied CCD contact data.
            target.layer=LayerMask.NameToLayer("StreetProps");
            var bridgeType=typeof(MercedesC63CabrioMod).Assembly.GetType("MercedesC63CabrioImpactDamageController",true);
            var bridge=clone.AddComponent(bridgeType);
            var car=clone.GetComponent<CarController>();
            car.vehicleInstance=new VehicleInstance("C63ContactFixture");
            typeof(CarController).GetField("damageHandler",fields).SetValue(car,handler);
            bridgeType.GetMethod("Initialize",fields).Invoke(bridge,new object[] {car,handler,null});
            Action sample = () => bridgeType.GetMethod("FixedUpdate",fields).Invoke(bridge,null);
            Action commit = () => bridgeType.GetMethod("CommitPendingDamage",fields).Invoke(bridge,null);
            Action<Collision,bool> capture = (c,stay) => bridgeType.GetMethod(stay?"OnCollisionStay":"OnCollisionEnter",fields)
                .Invoke(bridge,new object[] {c});
            Action reset = () => {
                bridgeType.GetMethod("OnDisable",fields).Invoke(bridge,null);
                handler.SetDamage(0f); car.vehicleInstance.damage=0f; age(2f);
            };
            foreach(var end in new[] {-1f,1f})
            {
                reset(); body.velocity=Vector3.forward*end*10f; sample();
                var c=Contact(end,10f); hit(c); capture(c,false);
                var native=handler.Damage; commit();
                if(Mathf.Abs(handler.Damage-native)>.00001f)
                    throw new InvalidOperationException("C63 reconciliation doubled native pole damage.");
                reset(); body.velocity=Vector3.forward*end*10f; sample();
                c=Contact(end,10f,0f); hit(c); capture(c,false); commit();
                if(handler.Damage!=0f) throw new InvalidOperationException("C63 zero impulse with no measured loss created damage.");
                // First contact reserved the native cooldown with zero damage.
                // A following solver impulse arrives in Stay, not another Enter.
                capture(Contact(end,10f),true); commit();
                var expected=10f/(Time.fixedDeltaTime*10f)*.99f*.005f;
                if(Mathf.Abs(handler.Damage-expected)>.00001f || car.vehicleInstance.damage!=handler.Damage)
                    throw new InvalidOperationException("C63 late pole impulse did not reach native/save damage.");
                capture(Contact(end,10f),true); commit();
                if(Mathf.Abs(handler.Damage-expected)>.00001f)
                    throw new InvalidOperationException("C63 resting pole contact repeated damage.");
                reset(); body.velocity=Vector3.forward*end*10f; sample(); body.velocity=Vector3.zero;
                c=Contact(end,10f,0f); hit(c); capture(c,false); commit();
                if(Mathf.Abs(handler.Damage-expected)>.00001f || car.vehicleInstance.damage!=handler.Damage)
                    throw new InvalidOperationException("C63 zero-impulse CCD measured stop did not reach native/save damage.");
            }
            reset(); body.velocity=Vector3.forward*10f; sample();
            capture(Contact(1,10f,0f),false);
            bridgeType.GetField("pendingUntil",fields).SetValue(bridge,-1f);
            capture(Contact(1,10f),true); commit();
            if(handler.Damage!=0f) throw new InvalidOperationException("C63 expired pole contact added damage.");
            foreach(var layer in new[] {"Ground","Roads","Sidewalk"})
            {
                reset(); target.layer=LayerMask.NameToLayer(layer);
                body.velocity=Vector3.forward*10f; sample(); body.velocity=Vector3.zero;
                capture(Contact(1,10f,0f),false); commit();
                if(handler.Damage!=0f) throw new InvalidOperationException("C63 reconciliation included non-prop support: "+layer);
            }
            reset(); target.layer=LayerMask.NameToLayer("StreetProps");
            handler.SetDamage(.4f); car.vehicleInstance.damage=.4f;
            body.velocity=Vector3.forward*10f; sample(); body.velocity=Vector3.zero;
            capture(Contact(1,10f,0f),false);
            handler.SetDamage(0f); car.vehicleInstance.damage=0f; commit();
            if(handler.Damage!=0f) throw new InvalidOperationException("C63 pending impact undid repair.");
            foreach(var tag in new[] {"Wheel","ExitTrigger"})
            {
                reset(); target.tag=tag;
                body.velocity=Vector3.forward*10f; sample(); body.velocity=Vector3.zero;
                capture(Contact(1,10f,0f),false); commit();
                if(handler.Damage!=0f) throw new InvalidOperationException("C63 prop reconciliation ignored tag failed: "+tag);
            }
            reset(); target.tag="Untagged";
            body.velocity=Vector3.up*10f; sample(); body.velocity=Vector3.zero;
            capture(Contact(1,10f,0f,Vector3.up),false); commit();
            if(handler.Damage!=0f) throw new InvalidOperationException("C63 prop ground-support normal added damage.");
            Debug.Log("C63 pole reconciliation audit passed: front/reverse native damage unchanged; zero first impulse plus late Stay impulse recovered once; measured CCD stop recovered; no impulse/loss, resting contacts, expired contacts and road/ground/sidewalk excluded; real CarController save/UI synchronization.");
            Debug.Log("C63 native percentage audit passed: front/reverse 2.5/4.9/10 m/s contacts, saved damage preserved, 0.2s cooldown, parking/road/ground/tag exclusions. Contact replay, not an in-game crash test.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(clone);
        }
    }

    private static void ValidateAudioLoading()
    {
        var assembly = typeof(MercedesC63CabrioMod).Assembly;
        var loader = assembly.GetType("MercedesC63CabrioWave",true).GetMethod("Load",BindingFlags.Static|BindingFlags.NonPublic);
        foreach (var name in new[] {"EngineIdle","EngineLow","EngineLowLoad","EngineMid","EngineMidLoad","EngineHigh","EngineHighLoad","HornLow","HornHigh"})
        {
            var path = Path.GetFullPath("Assets/Mods/Mercedes_C63_Cabrio/Config/Audio/"+name+".wav");
            var clip = (AudioClip)loader.Invoke(null,new object[] {path});
            try
            {
                if (name=="EngineIdle" && (clip.channels!=2 || clip.frequency!=48000 || clip.samples!=52918))
                    throw new InvalidOperationException("C63 copied Audi idle format/sample length changed.");
                var samples = new float[clip.samples*clip.channels];
                if (clip.loadState != AudioDataLoadState.Loaded || !clip.GetData(samples,0) || samples.Max(v=>Mathf.Abs(v))<.01f)
                    throw new InvalidOperationException("C63 runtime WAV data not loaded/audible: " + name);
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }
        var mixer=AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/Mods/Mercedes_C63_Cabrio/Audio/VehicleAudioMixer.mixer");
        var shared=mixer.FindMatchingGroups("Engine").Single(g=>g.name=="Engine");
        var model=assembly.GetType("MercedesC63CabrioAudioModel",true);
        var volume=model.GetMethod("PlaybackVolume",BindingFlags.Static|BindingFlags.NonPublic);
        var idleVolume=model.GetMethod("IdleVolume",BindingFlags.Static|BindingFlags.NonPublic);
        if (Mathf.Abs((float)idleVolume.Invoke(null,new object[] {0f})-.60f)>.0001f)
            throw new InvalidOperationException("C63 idle-only gain reduction was not applied.");
        for (var step=0;step<=100;step++)
            foreach(var throttle in new[] {0f,.5f,1f})
            {
                var gain=(float)volume.Invoke(null,new object[] {step/100f,throttle});
                var idle=(float)idleVolume.Invoke(null,new object[] {step/100f});
                var power=gain*gain+idle*idle;
                if(gain<0f || gain>.785f || idle<0f || idle>.601f || power<.25f || power>.615f)
                    throw new InvalidOperationException("C63 copied Audi idle/drive fade has a gap or excessive gain.");
            }
        var host = new GameObject("C63AudioProfileAudit");
        try
        {
            var audioType=assembly.GetType("MercedesC63CabrioAudioController",true);
            var audio=host.AddComponent(audioType);
            const BindingFlags lifecycle=BindingFlags.Instance|BindingFlags.NonPublic;
            var enable=audioType.GetMethod("OnEnable",lifecycle);
            var disable=audioType.GetMethod("OnDisable",lifecycle);
            disable.Invoke(audio,null);
            var enteringBefore=GlobalEvents.onEnterVehicle?.GetInvocationList().Length??0;
            var menuBefore=GlobalEvents.onFullMenuToggle?.GetInvocationList().Length??0;
            audioType.GetField("fullMenuOpen",lifecycle).SetValue(audio,true);
            enable.Invoke(audio,null); enable.Invoke(audio,null);
            if((GlobalEvents.onEnterVehicle?.GetInvocationList().Length??0)!=enteringBefore+1 ||
               (GlobalEvents.onFullMenuToggle?.GetInvocationList().Length??0)!=menuBefore+1 ||
               (bool)audioType.GetField("fullMenuOpen",lifecycle).GetValue(audio))
                throw new InvalidOperationException("C63 audio retained stale menu state or duplicate lifecycle listeners.");
            disable.Invoke(audio,null);
            if((GlobalEvents.onEnterVehicle?.GetInvocationList().Length??0)!=enteringBefore ||
               (GlobalEvents.onFullMenuToggle?.GetInvocationList().Length??0)!=menuBefore)
                throw new InvalidOperationException("C63 audio leaked entry/menu listeners.");
            var source=host.AddComponent<AudioSource>();
            assembly.GetType("MercedesC63CabrioAudioController",true).GetMethod("ConfigureEngineProfile",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[] {source,shared});
            if (source.minDistance!=8f || source.maxDistance!=65f || source.spatialBlend!=1f || source.dopplerLevel!=0f)
                throw new InvalidOperationException("C63 chase-camera audio profile invalid.");
            var saved=host.AddComponent<AudioSource>();
            saved.mute=true; saved.minDistance=1f; saved.maxDistance=500f; saved.spatialBlend=0f;
            assembly.GetType("MercedesC63CabrioAudioController",true).GetMethod("ConfigureEngineProfile",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[] {saved,shared});
            if (saved.outputAudioMixerGroup!=source.outputAudioMixerGroup || saved.mute || source.mute ||
                saved.minDistance!=source.minDistance || saved.maxDistance!=source.maxDistance || saved.rolloffMode!=source.rolloffMode ||
                saved.spatialBlend!=source.spatialBlend || saved.dopplerLevel!=source.dopplerLevel || saved.priority!=source.priority)
                throw new InvalidOperationException("C63 new/saved source profile or parked-mute state diverged.");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
        Debug.Log("C63 audio audit passed: all nine runtime PCM loads (original copied stereo Audi idle plus six drive/load and two horns) contain nonzero sample data; new/saved sources share mixer/profile and ignore parked native mute; min/max=8/65m. Game listening still required.");
    }

    public static void BuildAndRender()
    { MercedesC63CabrioSetup.GenerateAndBuild(); ValidateAudioLoading(); VerifyNativeLaunchClutch(); VerifyLoadedLaunch(); ValidateNativePercentageDamage(); RenderThumbnail(); }

    public static void RenderThumbnail()
    {
        var pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        var qualityPipeline = QualitySettings.renderPipeline;
        var preview = new PreviewRenderUtility();
        var materials = new List<Material>();
        try
        {
            // Built-in preview shading avoids changing the actual HDRP materials.
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mods/Mercedes_C63_Cabrio/Mercedes.prefab");
            var clone = UnityEngine.Object.Instantiate(prefab);
            preview.AddSingleGO(clone);
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled) continue;
                var slots = renderer.sharedMaterials;
                for (var i = 0; i < slots.Length; i++)
                {
                    var source = slots[i]; if (source == null) continue;
                    var transparentPreview = source.HasProperty("_SurfaceType") && source.GetFloat("_SurfaceType") > .5f;
                    var m = new Material(Shader.Find(transparentPreview ? "Standard" : "Hidden/MercedesC63Cabrio/Preview"));
                    m.color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") :
                        source.HasProperty("baseColorFactor") ? source.GetColor("baseColorFactor") : Color.gray;
                    foreach (var key in new[] {"_BaseColorMap", "baseColorTexture", "_MainTex"})
                        if (source.HasProperty(key) && source.GetTexture(key) != null) { m.mainTexture = source.GetTexture(key); break; }
                    m.SetFloat("_Glossiness", 0.35f);
                    if (source.HasProperty("_DoubleSidedEnable") && source.GetFloat("_DoubleSidedEnable") > .5f)
                        m.SetInt("_Cull", 0);
                    if (source.HasProperty("_Metallic")) m.SetFloat("_Metallic", source.GetFloat("_Metallic"));
                    if (source.HasProperty("_AlphaCutoffEnable") && source.GetFloat("_AlphaCutoffEnable") > .5f)
                    {
                        m.SetFloat("_UseAlphaClip", 1); m.SetFloat("_Cutoff", source.GetFloat("_AlphaCutoff"));
                        m.SetOverrideTag("RenderType", "TransparentCutout");
                        m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450;
                    }
                    else if (m.color.a < 0.5f)
                    {
                        m.SetFloat("_Mode", 3); m.SetInt("_SrcBlend", 1); m.SetInt("_DstBlend", 10);
                        m.SetInt("_ZWrite", 0); m.EnableKeyword("_ALPHAPREMULTIPLY_ON"); m.renderQueue = 3000;
                    }
                    slots[i] = m; materials.Add(m);
                }
                renderer.sharedMaterials = slots;
            }
            preview.cameraFieldOfView = 32f;
            preview.camera.transform.position = new Vector3(-5.3f, 3.2f, 6.6f);
            preview.camera.transform.LookAt(new Vector3(0f, 0.65f, 0f));
            preview.camera.nearClipPlane = 0.1f; preview.camera.farClipPlane = 30f;
            preview.camera.clearFlags = CameraClearFlags.Color;
            preview.camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f, 1);
            preview.ambientColor = Color.white * 0.55f;
            preview.lights[0].intensity = 1.3f; preview.lights[0].transform.rotation = Quaternion.Euler(45, 30, 0);
            preview.lights[1].intensity = 0.8f; preview.lights[1].transform.rotation = Quaternion.Euler(320, 200, 0);
            preview.BeginStaticPreview(new Rect(0, 0, 512, 512)); preview.Render();
            var texture = preview.EndStaticPreview();
            EnsureNonemptyRender(texture);
            File.WriteAllBytes("Assets/Mods/Mercedes_C63_Cabrio/thumbnail.png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material != null && renderer.name == "C63DamageBody") material.color = new Color(.62f, .018f, .025f, 1f);
            Directory.CreateDirectory("ReviewOutput");
            var views = new[] {
                ("Front", new Vector3(-4.4f, 2f, 5.6f)),
                ("Rear", new Vector3(-3.7f, 1.8f, -5.7f)),
                ("Side", new Vector3(-7.2f, 1.35f, .1f)),
                ("LowFront", new Vector3(0f, .52f, 5.5f)),
                ("Cabin", new Vector3(3f, 2.4f, -.2f)) };
            foreach (var view in views)
            {
                preview.camera.transform.position = view.Item2;
                preview.camera.transform.LookAt(new Vector3(0f, .65f, 0f));
                preview.BeginStaticPreview(new Rect(0, 0, 1024, 768)); preview.Render();
                var rendered = preview.EndStaticPreview();
                EnsureNonemptyRender(rendered);
                File.WriteAllBytes("ReviewOutput/MercedesC63Red" + view.Item1 + ".png", rendered.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(rendered);
            }
            var damageType=typeof(MercedesC63CabrioMod).Assembly.GetType("MercedesC63CabrioVisualDamageController",true);
            const BindingFlags damageFlags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
            var select=typeof(MercedesC63CabrioRuntime).GetMethod("IsDeformableExterior",BindingFlags.Static|BindingFlags.NonPublic);
            var damageFilters=clone.GetComponentsInChildren<MeshFilter>(true).Where(f=>(bool)select.Invoke(null,new object[] {f})).ToList();
            var damage=clone.AddComponent(damageType);
            damageType.GetMethod("Initialize",damageFlags).Invoke(damage,new object[] {clone.GetComponent<VehicleController>(),
                clone.GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true),null,damageFilters,5f});
            foreach (var end in new[] {1f,-1f})
            {
                var point=clone.transform.TransformPoint(new Vector3(0f,.35f,end*2.2f));
                var normal=clone.transform.TransformDirection(new Vector3(0f,0f,-end));
                damageType.GetMethod("ApplyImpact",damageFlags).Invoke(damage,new object[] {new[] {point},new[] {normal},30f});
                preview.camera.transform.position=end>0f?new Vector3(-3.7f,2f,5f):new Vector3(-3.7f,2f,-5f);
                preview.camera.transform.LookAt(new Vector3(0f,.65f,end*.9f));
                preview.BeginStaticPreview(new Rect(0,0,1024,768)); preview.Render();
                var dent=preview.EndStaticPreview();
                EnsureNonemptyRender(dent);
                File.WriteAllBytes("ReviewOutput/MercedesC63Damage"+(end>0f?"Front":"Rear")+".png",dent.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(dent);
            }
            AssetDatabase.Refresh();
            Debug.Log("C63 thumbnail rendered from the corrected prefab.");
        }
        finally
        {
            preview.Cleanup(); foreach (var m in materials) UnityEngine.Object.DestroyImmediate(m);
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = qualityPipeline;
        }
    }

    private static void EnsureNonemptyRender(Texture2D texture)
    {
        if (texture.GetPixels32().Select(c => ((int)c.r<<16)|((int)c.g<<8)|c.b).Distinct().Take(32).Count()<32)
            throw new InvalidOperationException("C63 preview is blank: use a graphics-enabled Unity batch process.");
    }

    [Serializable] private class Geometry { public string name; public Vector3[] vertices; public int[] triangles; }
    public static void Inspect()
    {
        MercedesC63CabrioSetup.Generate();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mods/Mercedes_C63_Cabrio/Mercedes.prefab");
        Directory.CreateDirectory("ReviewOutput/C63Geometry");
        var i = 0;
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var geometry = new Geometry { name = filter.transform.parent.name + "/" + filter.name,
                vertices = filter.sharedMesh.vertices.Select(v => prefab.transform.InverseTransformPoint(filter.transform.TransformPoint(v))).ToArray(),
                triangles = filter.sharedMesh.triangles };
            File.WriteAllText($"ReviewOutput/C63Geometry/{i++:D2}.json", JsonUtility.ToJson(geometry));
        }
        Debug.Log("C63 geometry review export complete.");
    }
}

