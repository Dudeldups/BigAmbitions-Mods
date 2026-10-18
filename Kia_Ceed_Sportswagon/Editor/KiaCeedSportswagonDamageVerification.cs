#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

// Run with Unity -executeMethod KiaCeedSportswagonDamageVerification.Verify
// -kiaBundleRoot <absolute mod AssetBundles/Windows directory>.
public static class KiaCeedSportswagonDamageVerification
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static void Verify()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, "-kiaBundleRoot");
        Require(index >= 0 && index + 1 < arguments.Length, "Missing -kiaBundleRoot.");
        var bundleRoot = arguments[index + 1];
        var bodyBundle = AssetBundle.LoadFromFile(System.IO.Path.Combine(bundleRoot, "kiaceedsportswagon.unity3d"));
        var lightBundle = AssetBundle.LoadFromFile(System.IO.Path.Combine(bundleRoot, "kiaceedsportswagonlights.unity3d"));
        Require(bodyBundle != null && lightBundle != null, "Could not load shipped Kia bundles.");
        GameObject? instance = null;
        GameObject? cachedInstance = null;
        GameObject? host = null;
        try
        {
            var prefab = bodyBundle!.LoadAsset<GameObject>("Assets/Mods/Kia_Ceed_Sportswagon/KiaCeedSportswagon.prefab");
            var lightPrefab = lightBundle!.LoadAsset<GameObject>(
                "Assets/Mods/Kia_Ceed_Sportswagon/Models/GeneratedLights/KiaCeedSWLightOverlays.glb");
            var bindingTimer = System.Diagnostics.Stopwatch.StartNew();
            instance = UnityEngine.Object.Instantiate(prefab);
            instance.SetActive(false);
            VerifyPlayerFeatures(instance);
            VerifyPlayerFit(instance);
            var binder = typeof(KiaCeedSportswagonRuntime).Assembly.GetType("KiaCeedSportswagonLightOverlayBinder", true)!;
            binder.GetMethod("Attach", BindingFlags.Static | Members)!.Invoke(null,
                new object?[] { instance, lightPrefab, null });
            host = new GameObject("KiaDamageVerification");
            var runtime = host.AddComponent<KiaCeedSportswagonRuntime>();
            var configure = typeof(KiaCeedSportswagonRuntime).GetMethod("ConfigureVisualDamage", Members)!;
            configure.Invoke(runtime, new object[] {
                instance.GetComponent(configure.GetParameters()[0].ParameterType) });
            var coldBindingMilliseconds = bindingTimer.Elapsed.TotalMilliseconds;
            var controller = instance.GetComponent<KiaCeedSportswagonVisualDamageController>();
            Require(controller != null, "Visual damage controller was not configured.");
            var type = controller!.GetType();
            var originals = Read<Dictionary<MeshFilter, Vector3[]>>(type, controller, "originalVertices");
            var current = Read<Dictionary<MeshFilter, Vector3[]>>(type, controller, "currentVertices");
            var matrices = Read<Dictionary<MeshFilter, Matrix4x4>>(type, controller, "filterToVehicleMatrices");
            var panels = Read<IDictionary>(type, controller, "panelVertexAnchors");
            var overlays = Read<IDictionary>(type, controller, "lampOverlayVertexAnchors");
            var legacy = instance.GetComponents<MonoBehaviour>().Single(c => c != null && c.GetType().Name == "VehicleDeformationController");
            var legacyType = legacy.GetType();
            Require(((Array)legacyType.GetField("meshFilters", Members)!.GetValue(legacy)).Length == 0 &&
                ((Array)legacyType.GetField("originalMeshes", Members)!.GetValue(legacy)).Length == 0,
                "Native repair still has inconsistent legacy mesh/baseline ownership.");
            var car = instance.GetComponent(configure.GetParameters()[0].ParameterType);
            var vehicleField = car.GetType().GetField("vehicleInstance", Members)!;
            vehicleField.SetValue(car, Activator.CreateInstance(vehicleField.FieldType,
                new object[] { "kiaceedsportswagon-vehicle:vehicletype_kiaceedsportswagon" }));
            legacyType.GetMethod("Reset", Members)!.Invoke(legacy, null);
            Require(overlays.Count == 12, $"Expected all 12 Blender overlays, got {overlays.Count}.");
            foreach (DictionaryEntry pair in overlays)
                Require(((Array)pair.Value).Length == originals[(MeshFilter)pair.Key].Length,
                    $"Incomplete overlay ownership: {((MeshFilter)pair.Key).name}.");
            var plate = originals.Keys.Single(f => f.name == "plate1_plate_0");
            Require(panels.Contains(plate) && ((Array)panels[plate]).Length == originals[plate].Length,
                "License plate does not fully follow the painted body.");
            var frontDetails = 0;
            var rearDetails = 0;
            foreach (var pair in originals.Where(p => p.Key.GetComponent<Renderer>().sharedMaterials.Any(m =>
                         m != null && (m.name.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       m.name.IndexOf("aluminiumbrushed", StringComparison.OrdinalIgnoreCase) >= 0))))
            {
                var indices = new HashSet<int>();
                if (panels.Contains(pair.Key))
                    foreach (var anchor in (Array)panels[pair.Key])
                        indices.Add((int)anchor.GetType().GetProperty("InteriorVertexIndex", Members)!.GetValue(anchor));
                for (var vertex = 0; vertex < pair.Value.Length; vertex++)
                {
                    var point = matrices[pair.Key].MultiplyPoint3x4(pair.Value[vertex]);
                    if (Mathf.Abs(point.x) > 0.35f || point.y < 0.3f || point.y > 1.4f || Mathf.Abs(point.z) < 1.7f)
                        continue;
                    Require(indices.Contains(vertex), $"Unbound central metal/emblem vertex: {pair.Key.name}:{vertex} at {point}.");
                    if (point.z > 0f) frontDetails++; else rearDetails++;
                }
            }
            Require(frontDetails > 0 && rearDetails > 0,
                $"Front/rear central emblem geometry was not exercised: {frontDetails}/{rearDetails}.");

            var paintPredicate = type.GetMethod("IsPaintedBodyTailgateTarget", BindingFlags.Static | Members)!;
            VerifyFrontCoverage(type, originals, matrices, panels, paintPredicate);
            VerifyContactDeformation(type, controller, originals, current, matrices, panels, overlays, true);
            VerifyContactDeformation(type, controller, originals, current, matrices, panels, overlays, false);
            VerifyLifecycleBindings(instance, controller, originals, current);
            ResetMeshes(originals, current);

            bindingTimer.Restart();
            cachedInstance = UnityEngine.Object.Instantiate(prefab);
            cachedInstance.SetActive(false);
            VerifyPlayerFit(cachedInstance);
            binder.GetMethod("Attach", BindingFlags.Static | Members)!.Invoke(null,
                new object?[] { cachedInstance, lightPrefab, null });
            configure.Invoke(runtime, new object[] {
                cachedInstance.GetComponent(configure.GetParameters()[0].ParameterType) });
            var cachedBindingMilliseconds = bindingTimer.Elapsed.TotalMilliseconds;
            var cachedController = cachedInstance.GetComponent<KiaCeedSportswagonVisualDamageController>();
            Require(Read<bool>(type, cachedController, "anchorLayoutCacheHit"),
                "A second identical shipped prefab did not reuse its immutable anchor layout.");
            var cachedCurrent = Read<Dictionary<MeshFilter, Vector3[]>>(type, cachedController, "currentVertices");
            foreach (var field in new[] { "panelVertexAnchors", "lampOverlayVertexAnchors" })
            {
                var cachedBindings = Read<IDictionary>(type, cachedController, field);
                foreach (DictionaryEntry pair in cachedBindings)
                foreach (var anchor in (Array)pair.Value)
                {
                    var source = (MeshFilter)anchor.GetType().GetProperty("ShellFilter", Members)!.GetValue(anchor);
                    Require(cachedCurrent.ContainsKey(source) && !current.ContainsKey(source),
                        "Cached anchor retained another vehicle's MeshFilter.");
                }
            }
            Debug.Log($"KIA_DAMAGE_BINDING_TIMING: full prefab clone/overlay attachment/mesh clone/binding " +
                $"cold={coldBindingMilliseconds:0.0}ms cached={cachedBindingMilliseconds:0.0}ms.");

            foreach (var pair in originals)
            {
                if (!(bool)paintPredicate.Invoke(null, new object[] { pair.Key }))
                    continue;
                var matrix = matrices[pair.Key];
                for (var vertex = 0; vertex < pair.Value.Length; vertex++)
                {
                    var point = matrix.MultiplyPoint3x4(pair.Value[vertex]);
                    // Exercise both end limits with a spatially varying field.
                    var delta = point.z < -1.3f
                        ? Vector3.forward * (0.27f * Mathf.Clamp01((-point.z - 1.3f) / 0.9f))
                        : point.z > 1.3f
                            ? Vector3.back * (0.153f * Mathf.Clamp01((point.z - 1.3f) / 0.9f))
                            : Vector3.zero;
                    current[pair.Key][vertex] = pair.Value[vertex] + matrix.inverse.MultiplyVector(delta);
                }
            }
            var apply = type.GetMethod("ApplyPanelAnchors", Members)!;
            apply.Invoke(controller, new object[] { panels });
            apply.Invoke(controller, new object[] { overlays });
            var checkedVertices = VerifyDeltas(panels, originals, current, matrices) +
                                  VerifyDeltas(overlays, originals, current, matrices);
            var firstPass = current.ToDictionary(p => p.Key, p => p.Value.ToArray());
            apply.Invoke(controller, new object[] { panels });
            apply.Invoke(controller, new object[] { overlays });
            foreach (var pair in current)
                Require(pair.Value.SequenceEqual(firstPass[pair.Key]), "Repeated anchor transfer accumulates deformation.");
            Debug.Log($"KIA_DAMAGE_VERIFIED: {checkedVertices} body/detail/overlay deltas preserve original spacing; " +
                $"12 complete lamp overlays, complete plate, central front/rear chrome vertices={frontDetails}/{rearDetails}, " +
                "front/rear maximum dents, repeat transfer is idempotent.");
        }
        finally
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (cachedInstance != null) UnityEngine.Object.DestroyImmediate(cachedInstance);
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            bodyBundle?.Unload(true);
            lightBundle?.Unload(true);
        }
    }

    private static void VerifyPlayerFit(GameObject instance)
    {
        var runtime = typeof(KiaCeedSportswagonRuntime);
        runtime.GetMethod("ConfigureWheelControllers", BindingFlags.Static | Members)!.Invoke(null, new object[] { instance });
        foreach (var corner in new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" })
        {
            var wheel = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == corner + "_WheelController");
            var expected = corner.StartsWith("Front", StringComparison.Ordinal) ? 1.43f : -1.2225f;
            Require(Mathf.Abs(wheel.localPosition.z - expected) < 0.00001f, "Player wheel longitudinal alignment mismatch.");
            var graphic = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "KiaCeedSportswagonWheel" + corner);
            Require(Mathf.Abs(graphic.localPosition.z - expected) < 0.00001f, "Player wheel graphic longitudinal alignment mismatch.");
        }
        var visual = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "KiaCeedSportswagonVisual");
        var rear = visual.InverseTransformPoint(instance.transform.TransformPoint(new Vector3(0f, 0f, -2.3025f)));
        var front = visual.InverseTransformPoint(instance.transform.TransformPoint(new Vector3(0f, 0f, 2.3025f)));
        runtime.GetMethod("ConfigureFrontBodyPitch", BindingFlags.Static | Members)!.Invoke(null, new object[] { instance });
        Require(Mathf.Abs(instance.transform.InverseTransformPoint(visual.TransformPoint(rear)).y + 0.0025f) < 0.00001f,
            "Rear body drop is not 2.5 mm.");
        Require(Mathf.Abs(instance.transform.InverseTransformPoint(visual.TransformPoint(front)).y - 0.020f) < 0.00001f,
            "Front body height changed.");
        Debug.Log("KIA_PLAYER_FIT_VERIFIED: player wheels forward 2 cm front/1.75 cm rear from original tune, rear body down 2.5 mm, front height retained.");
    }

    private static void VerifyPlayerFeatures(GameObject instance)
    {
        var assembly = typeof(KiaCeedSportswagonRuntime).Assembly;
        var sleep = assembly.GetType("KiaCeedSportswagonSleepBindings", true)!;
        var configure = sleep.GetMethod("Configure", BindingFlags.Static | Members)!;
        var car = instance.GetComponent(configure.GetParameters()[0].ParameterType);
        var vehicleTypeField = car.GetType().GetField("vehicleType", Members)!;
        var vehicleType = vehicleTypeField.GetValue(car);
        typeof(KiaCeedSportswagonMod).GetMethod("ConfigureVehicleTypeFeatures", BindingFlags.Static | Members)!
            .Invoke(null, new[] { vehicleType });
        Require(!(bool)vehicleType.GetType().GetField("autoParkSupported", Members)!.GetValue(vehicleType),
            "Native auto-parking flag is still enabled.");
        Require(typeof(KiaCeedSportswagonMod).GetField("autoParkingGuard", Members) == null,
            "Kia auto-parking helper still has an owner.");

        var cachedConfig = sleep.GetField("nativeCarConfig", BindingFlags.Static | Members)!;
        var fixture = ScriptableObject.CreateInstance(cachedConfig.FieldType);
        var environmentType = fixture.GetType().GetField("sleepEnvironmentType", Members)!;
        environmentType.SetValue(fixture, Enum.ToObject(environmentType.FieldType, 1));
        var savedField = car.GetType().GetField("vehicleInstance", Members)!;
        var environmentField = car.GetType().GetField("sleepEnvironment", Members)!;
        var previousSaved = savedField.GetValue(car);
        var previousEnvironment = environmentField.GetValue(car);
        try
        {
            cachedConfig.SetValue(null, fixture);
            savedField.SetValue(car, null);
            Require((bool)configure.Invoke(null, new object?[] { car, null }), "Sleep setup required a saved vehicle instance.");
            var environment = environmentField.GetValue(car);
            var configField = (FieldInfo)sleep.GetField("ConfigField", BindingFlags.Static | Members)!.GetValue(null);
            Require(ReferenceEquals(configField.GetValue(environment), fixture), "Native Car config was not bound.");
            configure.Invoke(null, new object?[] { car, null });
            Require(ReferenceEquals(environmentField.GetValue(car), environment), "Sleep setup is not idempotent.");
        }
        finally
        {
            savedField.SetValue(car, previousSaved);
            environmentField.SetValue(car, previousEnvironment);
            sleep.GetMethod("Release", BindingFlags.Static | Members)!.Invoke(null, null);
            UnityEngine.Object.DestroyImmediate(fixture);
        }
        var audio = assembly.GetType("KiaCeedSportswagonAudioModel", true)!;
        var engineVolume = audio.GetMethod("EngineVolume", BindingFlags.Static | Members)!;
        foreach (var throttle in new[] { 0f, 0.5f, 1f })
            Require(Mathf.Abs((float)engineVolume.Invoke(null, new object[] { throttle }) -
                (0.288f + 0.288f * throttle) * 0.85f) < 0.000001f, "Engine volume reduction is not 15 percent.");
        Require(Mathf.Abs((float)audio.GetMethod("IdleVolume", BindingFlags.Static | Members)!.Invoke(null, new object[] { 0f }) -
            0.396f * 0.85f) < 0.000001f, "Idle volume reduction is not 15 percent.");
        Debug.Log("KIA_PLAYER_FEATURES_VERIFIED: auto-parking disabled, Car sleep config binds without saved instance and is idempotent, idle/drive audio gains 85 percent.");
    }

    private static int creationNotifications;
    private static void ObserveCreation(object vehicle) => creationNotifications++;

    private static void VerifyLifecycleBindings(GameObject instance, Component controller,
        Dictionary<MeshFilter, Vector3[]> originals, Dictionary<MeshFilter, Vector3[]> current)
    {
        var type = controller.GetType();
        Require(typeof(KiaCeedSportswagonRuntime).GetMethod("Update", Members) == null &&
            type.GetMethod("Update", Members) == null &&
            typeof(KiaCeedSportswagonRuntime).Assembly.GetType("KiaCeedSportswagonAutoParkingGuard", true)!
                .GetMethod("LateUpdate", Members) == null, "Lifecycle polling callback still exists.");
        var carField = type.GetField("vehicle", Members)!;
        var globalType = carField.FieldType.Assembly.GetType("GlobalEvents", true)!;
        var changed = globalType.GetField("onVehicleVariablesChanged", BindingFlags.Static | Members)!;
        var previousListeners = changed.GetValue(null);
        var created = typeof(KiaCeedSportswagonCreationNotifier).GetField("Created", BindingFlags.Static | Members)!;
        var previousCreationListeners = created.GetValue(null);
        var enable = type.GetMethod("OnEnable", Members)!;
        var disable = type.GetMethod("OnDisable", Members)!;
        try
        {
            changed.SetValue(null, null);
            enable.Invoke(controller, null);
            enable.Invoke(controller, null);
            Require(((Delegate)changed.GetValue(null)).GetInvocationList().Length == 1,
                "Repeated enable duplicated repair subscription.");
            type.GetField("previousDamage", Members)!.SetValue(controller, 0.5f);
            ((Delegate)changed.GetValue(null)).DynamicInvoke();
            foreach (var pair in originals)
            {
                Require(pair.Value.SequenceEqual(current[pair.Key]), "Repair event did not restore owned vertices.");
                Require(pair.Value.SequenceEqual(pair.Key.sharedMesh.vertices), "Repair event did not restore rendered mesh.");
            }
            disable.Invoke(controller, null);
            Require(changed.GetValue(null) == null, "Repair subscription survived disable.");

            creationNotifications = 0;
            created.SetValue(null, Delegate.CreateDelegate(created.FieldType,
                typeof(KiaCeedSportswagonDamageVerification).GetMethod("ObserveCreation", BindingFlags.Static | Members)!));
            var notifier = instance.AddComponent<KiaCeedSportswagonCreationNotifier>();
            var start = notifier.GetType().GetMethod("Start", Members)!;
            start.Invoke(notifier, null);
            start.Invoke(notifier, null);
            Require(creationNotifications == 1, "Creation lifecycle notification was not idempotent.");
            Debug.Log("KIA_LIFECYCLE_VERIFIED: creation callback once, repair event restores all owned meshes, enable subscription idempotent, disable unsubscribes, three polling callbacks absent.");
        }
        finally
        {
            disable.Invoke(controller, null);
            changed.SetValue(null, previousListeners);
            created.SetValue(null, previousCreationListeners);
        }
    }

    private static void VerifyFrontCoverage(Type type, Dictionary<MeshFilter, Vector3[]> originals,
        Dictionary<MeshFilter, Matrix4x4> matrices, IDictionary panels, MethodInfo paintPredicate)
    {
        var rearOnly = type.GetMethod("IsRearInteriorShellFollower", BindingFlags.Static | Members)!;
        var mixedFront = type.GetMethod("IsMixedFrontExteriorShellFollower", BindingFlags.Static | Members)!;
        var missing = new List<string>();
        var total = 0;
        foreach (var pair in originals)
        {
            if ((bool)paintPredicate.Invoke(null, new object[] { pair.Key }) ||
                pair.Key.name.StartsWith("KiaCeedSportswagonLightOverlay_", StringComparison.Ordinal) ||
                ((bool)rearOnly.Invoke(null, new object[] { pair.Key }) &&
                 !(bool)mixedFront.Invoke(null, new object[] { pair.Key })))
                continue;
            var bound = new HashSet<int>();
            if (panels.Contains(pair.Key))
                foreach (var anchor in (Array)panels[pair.Key])
                    bound.Add((int)anchor.GetType().GetProperty("InteriorVertexIndex", Members)!.GetValue(anchor));
            var front = 0;
            var mapped = 0;
            for (var index = 0; index < pair.Value.Length; index++)
            {
                if (matrices[pair.Key].MultiplyPoint3x4(pair.Value[index]).z < 1.6f)
                    continue;
                front++;
                if (bound.Contains(index)) mapped++;
            }
            if (front == 0) continue;
            total += front;
            Debug.Log($"KIA_DAMAGE_FRONT_COVERAGE: mesh='{pair.Key.name}' mapped={mapped}/{front} missing={front - mapped}.");
            if (mapped != front) missing.Add($"{pair.Key.name}:{front - mapped}");
        }
        Require(total > 0 && missing.Count == 0,
            "Incomplete front-end shell ownership: " + string.Join(", ", missing));
    }

    private static void VerifyContactDeformation(Type type, Component controller,
        Dictionary<MeshFilter, Vector3[]> originals, Dictionary<MeshFilter, Vector3[]> current,
        Dictionary<MeshFilter, Matrix4x4> matrices, IDictionary panels, IDictionary overlays, bool front)
    {
        ResetMeshes(originals, current);
        var root = controller.transform;
        var z = front ? 2.25f : -2.25f;
        var normal = front ? Vector3.back : Vector3.forward;
        var contacts = new[]
        {
            CreateContact(root.TransformPoint(new Vector3(0f, 0.48f, z)), root.TransformDirection(normal)),
            CreateContact(root.TransformPoint(new Vector3(-0.58f, 0.65f, z)), root.TransformDirection(normal)),
            CreateContact(root.TransformPoint(new Vector3(0.58f, 0.65f, z)), root.TransformDirection(normal)),
        };
        type.GetMethod("DeformContacts", Members)!.Invoke(controller,
            new object?[] { contacts, contacts.Length, 40f, null });
        var moved = 0;
        var consoleMoved = 0;
        var overlaysMoved = 0;
        foreach (var pair in current)
        {
            var baseline = originals[pair.Key];
            for (var index = 0; index < baseline.Length; index++)
            {
                var point = matrices[pair.Key].MultiplyPoint3x4(baseline[index]);
                if ((front && point.z < 1.6f) || (!front && point.z > -1.6f) ||
                    (pair.Value[index] - baseline[index]).sqrMagnitude < 0.00000001f)
                    continue;
                moved++;
                if (pair.Key.name.Contains("Centre_Console_selfillumshiny__interior")) consoleMoved++;
                if (pair.Key.name.StartsWith("KiaCeedSportswagonLightOverlay_", StringComparison.Ordinal)) overlaysMoved++;
            }
        }
        Require(moved > 0 && consoleMoved > 0 && overlaysMoved > 0,
            $"Actual {(front ? "front" : "rear")} contact left body/fascia/lamps unchanged: " +
            $"moved={moved}, console={consoleMoved}, overlays={overlaysMoved}.");
        VerifyDeltas(panels, originals, current, matrices);
        VerifyDeltas(overlays, originals, current, matrices);
        Debug.Log($"KIA_DAMAGE_CONTACT_VERIFIED: {(front ? "front" : "rear")} actual contact deformation " +
            $"moved={moved} blackpaint={consoleMoved} lampOverlays={overlaysMoved}; master deltas agree.");
    }

    private static ContactPoint CreateContact(Vector3 point, Vector3 normal)
    {
        object contact = default(ContactPoint);
        typeof(ContactPoint).GetField("m_Point", Members)!.SetValue(contact, point);
        typeof(ContactPoint).GetField("m_Normal", Members)!.SetValue(contact, normal);
        return (ContactPoint)contact;
    }

    private static void ResetMeshes(Dictionary<MeshFilter, Vector3[]> originals,
        Dictionary<MeshFilter, Vector3[]> current)
    {
        foreach (var pair in originals)
        {
            Array.Copy(pair.Value, current[pair.Key], pair.Value.Length);
            pair.Key.sharedMesh.vertices = current[pair.Key];
            pair.Key.sharedMesh.RecalculateBounds();
        }
    }

    private static int VerifyDeltas(IDictionary bindings, Dictionary<MeshFilter, Vector3[]> originals,
        Dictionary<MeshFilter, Vector3[]> current, Dictionary<MeshFilter, Matrix4x4> matrices)
    {
        var count = 0;
        foreach (DictionaryEntry pair in bindings)
        {
            var follower = (MeshFilter)pair.Key;
            foreach (var anchor in (Array)pair.Value)
            {
                var type = anchor.GetType();
                var targetIndex = (int)type.GetProperty("InteriorVertexIndex", Members)!.GetValue(anchor);
                var master = (MeshFilter)type.GetProperty("ShellFilter", Members)!.GetValue(anchor);
                var sourceIndex = (int)type.GetProperty("ShellVertexIndex", Members)!.GetValue(anchor);
                var expected = matrices[master].MultiplyVector(current[master][sourceIndex] - originals[master][sourceIndex]);
                var actual = matrices[follower].MultiplyVector(current[follower][targetIndex] - originals[follower][targetIndex]);
                Require(Vector3.Distance(expected, actual) < 0.00001f,
                    $"Detail detached from master: {follower.name}, vertex {targetIndex}.");
                count++;
            }
        }
        return count;
    }

    private static T Read<T>(Type type, object instance, string field) =>
        (T)type.GetField(field, Members)!.GetValue(instance);

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
