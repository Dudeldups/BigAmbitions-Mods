#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class DodgeRam1500TRXReview
{
    private const string PrefabPath = "Assets/Mods/Dodge_Ram_1500/DodgeRam1500TRX.prefab";
    [Serializable] private class Geometry { public string name = ""; public Vector3[] vertices = Array.Empty<Vector3>(); public int[] triangles = Array.Empty<int>(); public Vector2[] uv = Array.Empty<Vector2>(); public string[] materials = Array.Empty<string>(); }
    [Serializable] private class Report { public Geometry[] meshes = Array.Empty<Geometry>(); }
    public static void ExportGeometry()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var report = new Report { meshes = prefab.GetComponentsInChildren<MeshFilter>(true)
            .Where(f => f.sharedMesh != null)
            .Select(f => new Geometry { name = f.name,
                vertices = f.sharedMesh.vertices.Select(v => prefab.transform.InverseTransformPoint(f.transform.TransformPoint(v))).ToArray(),
                triangles = f.sharedMesh.triangles, uv = f.sharedMesh.uv, materials = f.GetComponent<Renderer>()?.sharedMaterials.Select(m => m == null ? "" : m.name).ToArray() ?? Array.Empty<string>() }).ToArray() };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "trx-review-geometry.json"), JsonUtility.ToJson(report));
        Debug.Log("TRX review geometry exported: " + report.meshes.Length);
    }

    public static void AuthorNativeActivitiesAndBuild()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (root.GetComponent<DodgeRam1500TRXNativeActivities>() == null) root.AddComponent<DodgeRam1500TRXNativeActivities>();
            typeof(DodgeRam1500TRXSetup).GetMethod("ConfigureBodyColliders", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { root });
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); AssetDatabase.SaveAssets();
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        DodgeRam1500TRXSetup.BuildExistingAndVerify();
    }

    public static void Validate(GameObject prefab)
    {
        ValidateNativeDrivingDependencies(prefab);
        ValidateTrafficPreparation(prefab);
        ValidateParkingSelection();
        var transforms = prefab.GetComponentsInChildren<Transform>(true);
        var steering = transforms.Single(t => t.name == "DodgeRam1500TRXSteeringWheel");
        var seat = prefab.transform.InverseTransformPoint(steering.position);
        Require(seat.x < -.4f && Mathf.Abs(seat.y - DodgeRam1500TRXBodyFit.RakePoint(new Vector3(-.479f, 1.267f, .90f)).y) < .002f && seat.z > .8f, "upright, left-hand-drive steering anchor");
        Require(steering.GetComponentsInChildren<MeshFilter>(true).Length >= 2, "steering wheel and grips");
        var lamps = prefab.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("DodgeRam1500TRX_Lamp_", StringComparison.Ordinal)).ToArray();
        Require(lamps.Length == DodgeRam1500TRXDetailAuthoring.LampNames.Length, "lamp count");
        foreach (var name in DodgeRam1500TRXDetailAuthoring.LampNames)
        {
            var lamp = lamps.Single(r => r.name == "DodgeRam1500TRX_Lamp_" + name);
            var mesh = lamp.GetComponent<MeshFilter>().sharedMesh;
            Require(mesh != null && mesh.isReadable && mesh.triangles.Length >= 6, name + " readable faces");
            Require(mesh!.vertices.Length == mesh.triangles.Distinct().Count(), name + " compact geometry");
            Require(lamp.sharedMaterial != null && lamp.sharedMaterial.shader.name == "HDRP/Unlit", name + " HDRP emission");
            Require(!lamp.enabled && lamp.transform.localPosition == Vector3.zero && lamp.transform.localRotation == Quaternion.identity && lamp.transform.localScale == Vector3.one, name + " parked state and alignment");
            var positions = mesh.vertices.Select(v => prefab.transform.InverseTransformPoint(lamp.transform.TransformPoint(v))).ToArray();
            if (name == "ThirdBrake") Require(positions.All(v => DodgeRam1500TRXBodyFit.UnrakePoint(v).y > 1.77f), "roof above wheel plane");
            if (name.StartsWith("Reverse")) Require(positions.All(v => Mathf.Abs(v.x) > .83f && v.z < -2.7f), "reverse lamps in rear corners, away from plate");
            if (name.StartsWith("Drl")) Require(positions.All(v => DodgeRam1500TRXBodyFit.UnrakePoint(v).y > .91f), "DRL in headlamp assembly");
            Debug.Log($"TRX review lamp {name}: vertices={mesh.vertexCount}, triangles={mesh.triangles.Length / 3}, vehicleY={positions.Min(v => v.y):F3}..{positions.Max(v => v.y):F3}.");
        }
        DodgeRam1500TRXBlenderLights.ValidateAuthored(prefab);
        foreach (var side in new[] { "Left", "Right" })
        {
            var tail = lamps.Single(r => r.name == "DodgeRam1500TRX_Lamp_Tail" + side).sharedMaterial.GetColor("_UnlitColor");
            var stop = lamps.Single(r => r.name == "DodgeRam1500TRX_Lamp_Brake" + side).sharedMaterial.GetColor("_UnlitColor");
            var turn = lamps.Single(r => r.name == "DodgeRam1500TRX_Lamp_RearSignal" + side).sharedMaterial.GetColor("_UnlitColor");
            Require(tail.r > 0f && tail.r < stop.r * .6f && stop.r > stop.g * 50f,
                "shared tail/brake faces retain dim running and saturated red brake states");
            Require(turn.r > turn.g && turn.g > turn.b * 20f, "separate rear indicators use amber");
        }
        var parkingGuard = typeof(DodgeRam1500TRXRuntime).Assembly.GetType("DodgeRam1500TRXAutoParkingGuard", true);
        var parkingCheck = parkingGuard.GetMethod("IsAtAutoParkSpot", BindingFlags.Static | BindingFlags.NonPublic)!;
        bool AtSpot(Vector3 spot, float speed) => (bool)parkingCheck.Invoke(null, new object[] { Vector3.zero, spot, speed });
        Require(AtSpot(new Vector3(.7f, 0f, 0f), 0f) && !AtSpot(new Vector3(.8f, 0f, 0f), 0f) &&
            !AtSpot(Vector3.zero, 1f) && !AtSpot(new Vector3(float.NaN, 0f, 0f), 0f), "parking safeguard distance/speed/invalid-spot boundaries");
        Debug.Log("TRX rear lamps passed: Blender selections, independent amber indicators, shared dim tail/red brake, exact reverse surfaces.");
        var damageRoot = transforms.Single(t => t.name == "DodgeRam1500TRXDamageBody");
        Require(Vector3.Distance(damageRoot.localPosition, DodgeRam1500TRXBodyFit.RakePoint(new Vector3(0f, -.11f, 0f))) < .0001f && Quaternion.Angle(damageRoot.localRotation, DodgeRam1500TRXBodyFit.RakeRotation) < .001f, "body lowered 11 cm with additional rear rake");
        var frontReference = DodgeRam1500TRXBodyFit.FrontPivot;
        var rearReference = frontReference - Vector3.forward * DodgeRam1500TRXBodyFit.EffectiveWheelbase;
        Require(Vector3.Distance(DodgeRam1500TRXBodyFit.RakePoint(frontReference), frontReference) < .00001f && Mathf.Abs(DodgeRam1500TRXBodyFit.RakePoint(rearReference).y - rearReference.y + .02f) < .00001f, "front axle reference unchanged, rear body down 2 cm");
        foreach (var corner in new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" })
        {
            var axle = corner.StartsWith("Front") ? 1.84275f : -1.84275f;
            var controller = transforms.Single(t => t.name == corner + "_WheelController");
            var wheel = transforms.Single(t => t.name == "DodgeRam1500TRXWheel" + corner);
            var caliper = transforms.Single(t => t.name == "DodgeRam1500TRXFixedCaliper" + corner);
            Require(Mathf.Abs(controller.localPosition.y - .4295f) < .0001f && Mathf.Abs(controller.localPosition.z - axle - (corner.StartsWith("Front") ? .095f : .110f)) < .0001f, corner + " axle correction");
            Require(wheel.localPosition == controller.localPosition && caliper.localPosition == wheel.localPosition, corner + " visual/contact/caliper alignment");
            var caliperMesh = caliper.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            var radialOffset = new Vector2(caliperMesh.bounds.center.y, caliperMesh.bounds.center.z).magnitude;
            Debug.Log($"TRX caliper {corner}: meshCenter={caliperMesh.bounds.center}, radialOffset={radialOffset:F3}, size={caliperMesh.bounds.size}.");
            Require(radialOffset > .08f && radialOffset < .32f, corner + " source caliper offset retained");
        }
        DodgeRam1500TRXSourceMaterials.ValidateAllSourceMaterials();
        ValidatePlayerPrefabRegistration(prefab);
        ValidateMaterials(prefab);
        ValidateGeometry(prefab);
        ValidateAudioMapping();
        // Execute the real runtime repair on an inactive instance and compare its
        // collider/exit configuration with the bundle, catching silent overrides.
        var holder = new GameObject("TRX inactive review"); holder.SetActive(false);
        var instance = UnityEngine.Object.Instantiate(prefab, holder.transform);
        try
        {
            ValidateAudioLifecycle(instance);
            ValidateWashBindings(instance);
            ValidatePowertrain(instance);
            ValidateChassis(instance);
            ValidateSteering(instance);
            ValidateDriverHands(instance);
            ValidatePooledDriverLifecycle(instance);
            ValidateParkingProbes(instance);
            ValidateWarehouseControlRecovery(instance);
            var bodies = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "BodyCollider").GetComponentsInChildren<BoxCollider>(true);
            var centers = bodies.Select(c => c.center).ToArray(); var sizes = bodies.Select(c => c.size).ToArray();
            typeof(DodgeRam1500TRXRuntime).GetMethod("ConfigureBodyColliders", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object?[] { instance, null });
            Require(bodies.Length == 4 && bodies.Select((c, i) => c.center == centers[i] && c.size == sizes[i]).All(x => x), "runtime/prefab collider parity");
            var exits = instance.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Driverside" || t.name == "Passengerside").ToArray();
            var positions = exits.Select(t => t.localPosition).ToArray();
            typeof(DodgeRam1500TRXRuntime).GetMethod("ConfigureExitMarkers", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { instance });
            Require(exits.Length == 2 && exits.Select((t, i) => t.localPosition == positions[i]).All(x => x), "runtime/prefab exit parity");
            var damageSelector = typeof(DodgeRam1500TRXRuntime).GetMethod("IsDeformableExterior", BindingFlags.Static | BindingFlags.NonPublic)!;
            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            var selected = filters.Where(f => (bool)damageSelector.Invoke(null, new object[] { f })).ToArray();
            Require(selected.Count(f => f.name.StartsWith("DodgeRam1500TRX_Lamp_")) == DodgeRam1500TRXDetailAuthoring.LampNames.Length, "damage owns all lamp surfaces");
            Require(selected.Any(f => f.name.Contains("Grille")) && selected.Any(f => f.name.Contains("Badge")), "damage owns grilles and badges");
            Require(!selected.Any(f => f.name.Contains("Interior") || f.name.Contains("Steering") || f.name.Contains("Wheel")), "damage excludes cabin and wheels");
            ValidateLightStates(instance);
            Require(selected.Any(f => f.name.StartsWith("DodgeRam1500TRXExterior_Coloured")) && selected.Any(f => f.name.Contains("Exterior_Window_Geo")), "damage owns plastic bumpers and clear lamp lenses");
            Require(!selected.Any(f => f.name.StartsWith("Window_Geo") && f.name.Contains("Window_Material")), "cabin glazing excluded from damage");
            ValidateGarageTriggerOwnership(instance);
            ValidateSleepBinding(instance);
            ValidateDamageReplay(instance, selected);
            Debug.Log($"TRX review passed: 24 lamp surfaces, upright LHD steering, 4 matching colliders, 2 matching exits, {selected.Length} exterior damage meshes.");
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); }
    }

    private static void ValidateWashBindings(GameObject instance)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var binding = typeof(DodgeRam1500TRXWashBindings);
        var configure = binding.GetMethod("Configure", statics)!;
        var car = instance.GetComponent(configure.GetParameters()[0].ParameterType);
        var carType = car.GetType();
        Type Native(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null)!;
        var vehicleType = Native("VehicleController");
        var managerType = Native("GameManager");
        var playerType = Native("PlayerController");
        var effect = carType.GetField("washVfx", members)!;
        var source = carType.GetField("washAudioSource", members)!;
        var loop = carType.GetField("washSoundLoop", members)!;
        var end = carType.GetField("washSoundEnd", members)!;
        var oldEffect = effect.GetValue(car); var oldSource = source.GetValue(car);
        var oldLoop = loop.GetValue(car); var oldEnd = end.GetValue(car);
        var instanceField = vehicleType.GetField("vehicleInstance", members)!;
        var oldInstance = instanceField.GetValue(car);
        var rigidbodyField = vehicleType.GetField("_rigidbody", members)!;
        var oldBody = rigidbodyField.GetValue(car);
        var body = instance.GetComponent<Rigidbody>();
        var originalKinematic = body.isKinematic; var originalConstraints = body.constraints;
        var lifecycle = managerType.BaseType!.Assembly.GetType("Extensions.SingletonLifecycle", true)!;
        var register = lifecycle.GetMethod("RegisterSingleton", statics)!;
        var unregister = lifecycle.GetMethod("UnregisterSingleton", statics)!;
        var previousManager = lifecycle.GetMethod("GetSingleton", statics)!.MakeGenericMethod(managerType).Invoke(null, null);
        var wasMissing = (bool)lifecycle.GetMethod("IsTypeMissing", statics)!.Invoke(null, new object[] { managerType })!;
        var templateField = binding.GetField("nativeWashTemplate", statics)!;
        var originalTemplate = templateField.GetValue(null);
        var cache = (IDictionary)Native("Helpers.PrefabHelper").GetField("PrefabCache", statics)!.GetValue(null)!;
        const string key = "Prefabs/Vehicles/PlayerVehicles/HonzaMimic.prefab";
        var hadTemplate = cache.Contains(key); var previousTemplate = cache[key];
        var template = UnityEngine.Object.Instantiate(instance, instance.transform.parent);
        template.name = "TRX native wash audio fixture"; template.SetActive(false);
        var nativeCar = template.GetComponent(carType);
        var nativeLoop = AudioClip.Create("native fixture wash loop", 4410, 1, 44100, false);
        var nativeEnd = AudioClip.Create("native fixture wash end", 4410, 1, 44100, false);
        var pcm = Enumerable.Range(0, 4410).Select(i => .1f * Mathf.Sin(i * .13f)).ToArray();
        nativeLoop.SetData(pcm, 0); nativeEnd.SetData(pcm, 0);
        loop.SetValue(nativeCar, nativeLoop); end.SetValue(nativeCar, nativeEnd);
        cache[key] = template; templateField.SetValue(null, null);
        var managerHost = new GameObject("TRX native wash manager fixture"); managerHost.SetActive(false);
        var playerHost = new GameObject("TRX native wash player fixture"); playerHost.SetActive(false);
        Component? manager = null;
        Component? guard = null;
        GameObject? fallback = null;
        GameObject? audioHost = null;
        try
        {
            manager = managerHost.AddComponent(managerType);
            var player = playerHost.AddComponent(playerType);
            managerType.GetField("playerController", members)!.SetValue(manager, player);
            register.Invoke(null, new object[] { managerType, manager });
            lifecycle.GetMethod("UnmarkMissingType", statics)!.Invoke(null, new object[] { managerType });
            instanceField.SetValue(car, Activator.CreateInstance(instanceField.FieldType,
                new object[] { "dodgeram1500trx-vehicle:vehicletype_dodgeram1500trx" }));
            rigidbodyField.SetValue(car, body);
            // Reproduce the actual runtime log: both SDK clips are absent.
            effect.SetValue(car, null); loop.SetValue(car, null); end.SetValue(car, null);
            configure.Invoke(null, new object?[] { car, null });
            fallback = effect.GetValue(car) as GameObject;
            var audio = (AudioSource)source.GetValue(car)!; audioHost = audio.gameObject;
            guard = instance.GetComponent<DodgeRam1500TRXWashPhysicsGuard>();
            var guardType = typeof(DodgeRam1500TRXWashPhysicsGuard);
            Require(fallback != null && !fallback.activeSelf && fallback.transform.parent == car.transform,
                "owned inactive wash effect");
            Require(ReferenceEquals(loop.GetValue(car), nativeLoop) && ReferenceEquals(end.GetValue(car), nativeEnd),
                "missing runtime wash clips resolved from actual native prefab path");
            Require(audio.gameObject.name == "DodgeRam1500TRX_WashAudio" && !audio.playOnAwake &&
                !ReferenceEquals(audio, oldSource) && audio.minDistance >= 4f && audio.maxDistance == 30f && audio.dopplerLevel == 0f,
                "wash audio separated from engine layers and deliberately spatialized");
            configure.Invoke(null, new object?[] { car, null });
            Require(ReferenceEquals(source.GetValue(car), audio), "wash audio rebind idempotent");
            effect.SetValue(car, null); source.SetValue(car, null);
            configure.Invoke(null, new object?[] { car, null });
            Require(ReferenceEquals(effect.GetValue(car), fallback) && ReferenceEquals(source.GetValue(car), audio), "lost native wash fields restored without duplicate objects");
            var wash = carType.GetMethod("WashCoroutine", members)!;
            var blockers = (IEnumerable)playerType.GetField("_activeVehicleNavigationBlockers", members)!.GetValue(player)!;
            bool Blocked() => blockers.Cast<object>().Any(value => value.ToString() == "WashingVehicle");
            // Audio can play in this isolated fixture although the vehicle is
            // intentionally inactive to avoid native game/AI initialization.
            audioHost.transform.SetParent(null, true);
            var wheels = (Array)guardType.GetField("wheels", members)!.GetValue(guard)!;
            Require(wheels.Length == 4, "wash suspension caches four native wheel controllers");
            for (var pass = 0; pass < 3; pass++)
            {
                body.isKinematic = pass == 2; body.constraints = RigidbodyConstraints.None;
                if (pass == 2) ((Behaviour)wheels.GetValue(1)!).enabled = false;
                var wheelStates = wheels.Cast<Behaviour>().Select(w => w.enabled).ToArray();
                var routine = (IEnumerator)wash.Invoke(car, new object[] { 1f })!;
                Require(routine.MoveNext() && (bool)carType.GetField("isWashing", members)!.GetValue(car)! &&
                    fallback!.activeSelf && audio.loop && audio.clip == nativeLoop && body.constraints == RigidbodyConstraints.FreezeAll && Blocked(),
                    "actual native wash enters loop/freeze/navigation block");
                // Inactive Editor fixture does not receive MonoBehaviour callbacks;
                // explicitly deliver the same washVfx enable event once.
                guardType.GetMethod("BeginWash", members)!.Invoke(guard, null);
                Require(body.isKinematic && wheels.Cast<Behaviour>().All(w => !w.enabled), "frozen wash has no active wheel solver");
                var startTime = routine.GetType().GetFields(members).Single(f => f.Name.Contains("startTime") && f.FieldType == typeof(float));
                startTime.SetValue(routine, Time.time - .9999f);
                Require(routine.MoveNext() && (float)instanceField.FieldType.GetField("dirtiness")!.GetValue(instanceField.GetValue(car))! < .001f,
                    "native wash cleans saved dirtiness");
                startTime.SetValue(routine, Time.time - 2f);
                Require(!routine.MoveNext() && !(bool)carType.GetField("isWashing", members)!.GetValue(car)! && !fallback.activeSelf &&
                    !audio.loop && audio.clip == nativeEnd && body.constraints == RigidbodyConstraints.None && !Blocked(),
                    "native wash end clip and freeze/navigation cleanup complete");
                foreach (var wheel in wheels.Cast<Component>())
                {
                    var spring = wheel.GetType().GetField("spring")!.GetValue(wheel)!;
                    spring.GetType().GetField("prevVelocity")!.SetValue(spring, 99f);
                    spring.GetType().GetField("compressionVelocity")!.SetValue(spring, 99f);
                    var damper = wheel.GetType().GetField("damper")!.GetValue(wheel)!;
                    damper.GetType().GetField("force")!.SetValue(damper, 99999f);
                }
                guardType.GetMethod("EndWash", members)!.Invoke(guard, null);
                Require(body.isKinematic == (pass == 2) && wheels.Cast<Behaviour>().Select((w, i) => w.enabled == wheelStates[i]).All(v => v) &&
                    body.velocity.sqrMagnitude < .000001f && body.angularVelocity.sqrMagnitude < .000001f,
                    "completion restores exact wheel/kinematic states with no release velocity");
                foreach (var wheel in wheels.Cast<Component>())
                {
                    var spring = wheel.GetType().GetField("spring")!.GetValue(wheel)!;
                    var damper = wheel.GetType().GetField("damper")!.GetValue(wheel)!;
                    Require((float)spring.GetType().GetField("prevVelocity")!.GetValue(spring)! == 0f &&
                        (float)spring.GetType().GetField("compressionVelocity")!.GetValue(spring)! == 0f &&
                        (float)damper.GetType().GetField("force")!.GetValue(damper)! == 0f,
                        "wash resume clears frozen suspension/damper history");
                }
            }
            Debug.Log("TRX native wash lifecycle passed: runtime missing-clip reproduction, native prefab clip recovery, dedicated sound source, three complete native washes, saved dirt cleared, loop/end audio switch, freeze/navigation release, wheel suspension and zero-velocity resume.");
        }
        finally
        {
            if (guard != null)
            {
                // Immediate fixture cleanup; runtime cleanup destroys owned nodes.
                guard.GetType().GetMethod("RestoreSuspension", members)!.Invoke(guard, null);
                UnityEngine.Object.DestroyImmediate(guard);
            }
            effect.SetValue(car, oldEffect); source.SetValue(car, oldSource); loop.SetValue(car, oldLoop); end.SetValue(car, oldEnd);
            instanceField.SetValue(car, oldInstance); rigidbodyField.SetValue(car, oldBody);
            body.isKinematic = originalKinematic; body.constraints = originalConstraints;
            if (fallback != null) UnityEngine.Object.DestroyImmediate(fallback);
            if (audioHost != null) UnityEngine.Object.DestroyImmediate(audioHost);
            templateField.SetValue(null, originalTemplate);
            if (hadTemplate) cache[key] = previousTemplate; else cache.Remove(key);
            if (manager != null) unregister.Invoke(null, new object[] { managerType, manager });
            if (previousManager != null) register.Invoke(null, new object[] { managerType, previousManager });
            if (wasMissing) lifecycle.GetMethod("MarkMissingType", statics)!.Invoke(null, new object[] { managerType });
            UnityEngine.Object.DestroyImmediate(template); UnityEngine.Object.DestroyImmediate(nativeLoop); UnityEngine.Object.DestroyImmediate(nativeEnd);
            UnityEngine.Object.DestroyImmediate(managerHost); UnityEngine.Object.DestroyImmediate(playerHost);
        }
    }

    private static void ValidateNativeDrivingDependencies(GameObject prefab)
    {
        var physics = prefab.GetComponentsInChildren<MonoBehaviour>(true).Single(c => c != null && c.GetType().FullName == "NWH.VehiclePhysics2.VehicleController");
        var serialized = new SerializedObject(physics);
        Require(serialized.FindProperty("soundManager.mixer").objectReferenceValue != null,
            "native SDK audio mixer reference is bundled");
        var settings = serialized.FindProperty("stateSettings").objectReferenceValue;
        Require(settings != null, "bundled NWH StateSettings: missing settings disable input and powertrain");
        var states = new SerializedObject(settings);
        var lods = states.FindProperty("LODs");
        Require(lods != null && lods.arraySize == 3, "three bundled native physics LODs");
        for (var i = 0; i < lods.arraySize; i++)
            Require(lods.GetArrayElementAtIndex(i).objectReferenceValue != null, "native LOD asset " + i + " included in bundle");
        var definitions = states.FindProperty("definitions");
        foreach (var name in new[] { "Input.VehicleInputHandler", "Powertrain.Powertrain", "Powertrain.EngineComponent", "Powertrain.TransmissionComponent", "Steering", "Brakes" })
        {
            var enabled = false;
            for (var i = 0; i < definitions.arraySize; i++)
            {
                var definition = definitions.GetArrayElementAtIndex(i);
                if (definition.FindPropertyRelative("fullName").stringValue == "NWH.VehiclePhysics2." + name)
                    enabled = definition.FindPropertyRelative("isEnabled").boolValue;
            }
            Require(enabled, name + " enabled by native bundled StateSettings");
        }
        var ground = serialized.FindProperty("groundDetection.groundDetectionPreset").objectReferenceValue;
        Require(ground != null, "bundled ground detection preset");
        var groundData = new SerializedObject(ground);
        Require(groundData.FindProperty("fallbackSurfacePreset").objectReferenceValue != null, "bundled fallback road surface");
        var surfaces = groundData.FindProperty("surfaceMaps");
        Require(surfaces != null && surfaces.arraySize > 0, "native road surface map exists");
        for (var i = 0; i < surfaces.arraySize; i++)
            Require(surfaces.GetArrayElementAtIndex(i).FindPropertyRelative("surfacePreset").objectReferenceValue != null, "bundled native road surface preset");
        var start = (MethodInfo)typeof(DodgeRam1500TRXRuntime).GetField("NativeCarStartEngine", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        Require(start != null && start.Name == "StartEngine" && start.DeclaringType!.Name == "CarController", "native full vehicle start binding, not just engine restart");
        var wheels = prefab.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c != null)
            .Select(c => new SerializedObject(c)).Where(data => data.FindProperty("activeFrictionPreset") != null).ToArray();
        Require(wheels.Length == 4 && wheels.All(data => data.FindProperty("activeFrictionPreset").objectReferenceValue != null), "all four tire friction presets bundled");
        Debug.Log("TRX native driving dependencies passed: bundled StateSettings with enabled input/engine/transmission/steering/brakes, 3 LOD assets, ground and 4 tire friction presets.");
    }

    private static void ValidateTrafficPreparation(GameObject playerPrefab)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var runtimeAssembly = typeof(DodgeRam1500TRXRuntime).Assembly;
        var support = runtimeAssembly.GetType("DodgeRam1500TRXPrivateDriverSupport", true)!;
        var wheelRig = runtimeAssembly.GetType("DodgeRam1500TRXTrafficWheels", true)!;
        Type Native(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("GleyTrafficSystem." + name)).First(t => t != null)!;
        var vehicleType = Native("VehicleComponent"); var wheelType = Native("Wheel"); var carType = Native("CarType");
        var cache = (IDictionary)support.GetMethod("GetPrefabCache", flags)!.Invoke(null, null);
        const string templateKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
        var hadTemplate = cache.Contains(templateKey); var previousTemplate = cache[templateKey];
        var template = new GameObject("TRX inactive native traffic template"); template.SetActive(false);
        var vehicle = template.AddComponent(vehicleType);
        var wheels = Array.CreateInstance(wheelType, 4);
        var corners = new[] { "FrontLeft", "FrontRight", "RearLeft", "RearRight" };
        for (var i = 0; i < 4; i++)
        {
            var wheel = Activator.CreateInstance(wheelType)!;
            var origin = new GameObject("Ray" + corners[i]).transform; origin.SetParent(template.transform, false);
            origin.localPosition = new Vector3(i % 2 == 0 ? -.8f : .8f, .7f, i < 2 ? 1.2f : -1.2f);
            var graphic = new GameObject("Stock" + corners[i]).transform; graphic.SetParent(template.transform, false); graphic.localPosition = origin.localPosition;
            wheelType.GetField("wheelTransform")!.SetValue(wheel, origin);
            wheelType.GetField("wheelGraphics")!.SetValue(wheel, graphic);
            wheelType.GetField("wheelPosition")!.SetValue(wheel, Enum.Parse(wheelType.GetField("wheelPosition")!.FieldType, i < 2 ? "Front" : "Back"));
            wheelType.GetField("maxSuspension")!.SetValue(wheel, .3f);
            wheels.SetValue(wheel, i);
        }
        vehicleType.GetField("allWheels")!.SetValue(vehicle, wheels);
        cache[templateKey] = template;
        GameObject? clone = null;
        var pool = ScriptableObject.CreateInstance(Native("VehiclePool"));
        var arrayField = pool.GetType().GetField("trafficCars")!;
        var foreign = Array.CreateInstance(carType, 2);
        foreign.SetValue(Activator.CreateInstance(carType), 0); foreign.SetValue(Activator.CreateInstance(carType), 1);
        arrayField.SetValue(pool, foreign);
        var prefabField = support.GetField("customAiPrefab", flags)!;
        var poolField = support.GetField("modifiedVehiclePool", flags)!;
        var ownTypeField = support.GetField("customCarType", flags)!;
        var previousOwnedPrefab = prefabField.GetValue(null); var previousPool = poolField.GetValue(null); var previousOwnType = ownTypeField.GetValue(null);
        try
        {
            clone = (GameObject)support.GetMethod("CreateAiPrefab", flags)!.Invoke(null, new object[] { playerPrefab });
            Require(clone != null && !clone.activeSelf, "complete inactive Dodge AI prefab created from native template");
            Require(clone.GetComponentsInChildren<MeshRenderer>(true).Count(r => r.name.StartsWith("DodgeRam1500TRX_Lamp_")) == DodgeRam1500TRXDetailAuthoring.LampNames.Length, "NPC includes all owned light emitters");
            var native = clone.GetComponent(vehicleType);
            var nativeWheels = (Array)vehicleType.GetField("allWheels")!.GetValue(native);
            foreach (var wheel in nativeWheels)
            {
                var graphic = (Transform)wheelType.GetField("wheelGraphics")!.GetValue(wheel);
                var radius = (float)wheelType.GetField("wheelRadius")!.GetValue(wheel);
                Require(graphic.name.StartsWith("DodgeRam1500TRXTrafficHub_") && graphic.childCount == 1 && radius > .4f && radius < .49f, "native NPC job owns centered TRX rolling hub and measured radius");
                Require(Mathf.Abs((float)wheelType.GetField("wheelCircumference")!.GetValue(wheel) - 2f * Mathf.PI * radius) < .00001f, "NPC circumference matches radius");
            }
            var configure = wheelRig.GetMethod("Configure", flags)!;
            Require((bool)configure.Invoke(null, new object?[] { clone, null }), "NPC wheel rebinding idempotent");
            Require(clone.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("DodgeRam1500TRXTrafficHub_")) == 4, "no duplicate NPC hubs");
            prefabField.SetValue(null, clone);
            var register = support.GetMethod("RegisterOwnedPool", flags)!;
            Require((bool)register.Invoke(null, new object[] { pool, false }), "owned traffic type appended before initialization");
            var expanded = (Array)arrayField.GetValue(pool);
            Require(expanded.Length == 3 && ReferenceEquals(expanded.GetValue(0), foreign.GetValue(0)) && ReferenceEquals(expanded.GetValue(1), foreign.GetValue(1)), "stock and foreign pool entries preserved");
            Require((bool)register.Invoke(null, new object[] { pool, true }) && ((Array)arrayField.GetValue(pool)).Length == 3, "existing owned traffic entry rebinds without duplicate after initialization");
            arrayField.SetValue(pool, foreign);
            Require(!(bool)register.Invoke(null, new object[] { pool, true }) && ReferenceEquals(arrayField.GetValue(pool), foreign), "late append rejected without mutating live pool");
            Debug.Log("TRX NPC preparation passed: complete owned AI visuals/lights/colliders, 4 centered native wheel hubs/radii, idempotence, foreign pool preservation, existing-entry rebind and unsafe late-append rejection.");
        }
        finally
        {
            prefabField.SetValue(null, previousOwnedPrefab); poolField.SetValue(null, previousPool); ownTypeField.SetValue(null, previousOwnType);
            if (hadTemplate) cache[templateKey] = previousTemplate; else cache.Remove(templateKey);
            if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            UnityEngine.Object.DestroyImmediate(template); UnityEngine.Object.DestroyImmediate(pool);
        }
    }

    private static void ValidateParkingSelection()
    {
        var guard = typeof(DodgeRam1500TRXRuntime).Assembly.GetType("DodgeRam1500TRXAutoParkingGuard", true)!;
        var select = guard.GetMethod("SelectParkingLane", BindingFlags.Static | BindingFlags.NonPublic)!;
        var laneType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ParkingLaneGenerator")).First(t => t != null)!;
        var holder = new GameObject("TRX inactive lane review"); holder.SetActive(false);
        var unrelated = new GameObject("incidental parking-layer hit"); unrelated.transform.SetParent(holder.transform, false);
        var unrelatedHit = unrelated.AddComponent<BoxCollider>();
        var lanes = new List<Component>(); var colliders = new List<Collider>();
        foreach (var name in new[] { "legal", "other", "handicap" })
        {
            var node = new GameObject(name); node.transform.SetParent(holder.transform, false);
            var lane = node.AddComponent(laneType);
            laneType.GetField("neighbourhood")!.SetValue(lane, name == "other" ? "other-neighbourhood" : "target-neighbourhood");
            laneType.GetField("isHandicapParking")!.SetValue(lane, name == "handicap");
            lanes.Add(lane); colliders.Add(node.AddComponent<BoxCollider>());
        }
        try
        {
            object?[] values = { new Collider[] { unrelatedHit, colliders[0] }, false };
            Require(ReferenceEquals(select.Invoke(null, values), lanes[0]) && !(bool)values[1]!, "parking examines all hits, not incidental first collider");
            values = new object?[] { new Collider[] { colliders[0], colliders[1] }, false };
            select.Invoke(null, values); Require((bool)values[1]!, "mixed neighbourhood rejected");
            values = new object?[] { new Collider[] { colliders[0], colliders[2] }, false };
            Require(ReferenceEquals(select.Invoke(null, values), lanes[2]), "handicap overlap remains invalid");
            Debug.Log("TRX parking selection passed: incidental first hit ignored, mixed/handicap overlaps rejected; root-oriented trigger probes, horizontal spot fit and post-exit auto-park completion retained.");
        }
        finally { UnityEngine.Object.DestroyImmediate(holder); }
    }

    private static void ValidatePooledDriverLifecycle(GameObject instance)
    {
        var type = typeof(DodgeRam1500TRXRuntime).Assembly.GetType("DodgeRam1500TRXDriverController", true)!;
        var driver = instance.AddComponent(type);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var initialize = type.GetMethod("InitializeForAmbientTraffic", flags)!;
        var enable = type.GetMethod("OnEnable", flags)!;
        var disable = type.GetMethod("OnDisable", flags)!;
        var occupied = type.GetField("occupied", flags)!;
        try
        {
            initialize.Invoke(driver, new object?[] { null });
            for (var cycle = 0; cycle < 3; cycle++)
            {
                Require((bool)occupied.GetValue(driver)!, "NPC activation requests seated driver");
                disable.Invoke(driver, null);
                Require(!(bool)occupied.GetValue(driver)!, "NPC pool deactivation removes seated driver");
                enable.Invoke(driver, null);
            }
            var wrapper = typeof(DodgeRam1500TRXRuntime).Assembly.GetType("DodgeRam1500TRXAmbientTrafficDriver", true)!;
            Require(wrapper.GetMethod("OnEnable", flags) != null && wrapper.GetMethod("Start", flags) == null,
                "ambient driver wrapper initializes on every pool activation");
            Debug.Log("TRX NPC driver lifecycle passed: 3 pool disable/enable cycles restore occupancy; native appearance rendering still requires in-game observation.");
        }
        finally { UnityEngine.Object.DestroyImmediate(driver); }
    }

    private static void ValidateParkingProbes(GameObject instance)
    {
        var guard = typeof(DodgeRam1500TRXRuntime).Assembly.GetType("DodgeRam1500TRXAutoParkingGuard", true)!;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var probe = guard.GetMethod("GetParkingWheelState", flags)!;
        var car = instance.GetComponentsInChildren<MonoBehaviour>(true).Single(c => c != null && c.GetType().Name == "CarController");
        var laneType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ParkingLaneGenerator")).First(t => t != null)!;
        var layerType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Helpers.LayerHelper")).First(t => t != null)!;
        var maskField = layerType.GetField("parkingAreaLayerMask")!;
        var oldMask = maskField.GetValue(null);
        var holder = new GameObject("TRX real four-wheel parking probes");
        var lane = holder.AddComponent(laneType);
        laneType.GetField("neighbourhood")!.SetValue(lane, "TRX-review-neighbourhood");
        var contacts = new List<GameObject>();
        try
        {
            maskField.SetValue(null, (LayerMask)(1 << 30));
            foreach (var wheel in instance.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("_WheelController", StringComparison.Ordinal)))
            {
                var node = new GameObject(wheel.name + " legal lane trigger"); node.layer = 30;
                node.transform.SetParent(holder.transform, false);
                node.transform.position = wheel.position - instance.transform.up * .1f;
                var collider = node.AddComponent<BoxCollider>(); collider.size = new Vector3(.4f, .2f, .4f); collider.isTrigger = true;
                contacts.Add(node);
            }
            Require(contacts.Count == 4, "parking matrix uses exact TRX physical wheels");
            Physics.SyncTransforms();
            object?[] args = { car, "", "" };
            Require((bool)probe.Invoke(null, args)! && (string)args[1]! == "TRX-review-neighbourhood", "all four legal trigger probes succeed");
            contacts[0].transform.position += Vector3.right * 3f; Physics.SyncTransforms();
            args = new object?[] { car, "", "" };
            Require(!(bool)probe.Invoke(null, args)!, "one wheel outside lane cannot be legalized");
            contacts[0].transform.position -= Vector3.right * 3f;
            laneType.GetField("isHandicapParking")!.SetValue(lane, true); Physics.SyncTransforms();
            args = new object?[] { car, "", "" };
            Require(!(bool)probe.Invoke(null, args)!, "handicap lane cannot be legalized");
            Require(guard.GetMethod("HandleAutoParkAction", BindingFlags.Instance | BindingFlags.NonPublic) != null, "SpecialInteract callback covers native keyboard bypass");
            Debug.Log("TRX parking probe matrix passed: four actual wheel positions in trigger lanes, out-of-lane/handicap rejection, keyboard callback present.");
        }
        finally { maskField.SetValue(null, oldMask); UnityEngine.Object.DestroyImmediate(holder); Physics.SyncTransforms(); }
    }

    private static void ValidateChassis(GameObject instance)
    {
        var body = instance.GetComponent<Rigidbody>();
        var tensor = new Vector3(8000f, 8400f, 3200f);
        Require((body.centerOfMass - new Vector3(0f, .35f, -.05f)).sqrMagnitude < 1e-8f && (body.inertiaTensor - tensor).sqrMagnitude < .01f, "pickup COM and inertia");
        var massModule = instance.GetComponentsInChildren<MonoBehaviour>(true).Select(c => c == null ? null : new SerializedObject(c)).Single(s => s?.FindProperty("useDefaultInertia") != null)!;
        Require(!massModule.FindProperty("useDefaultInertia").boolValue && massModule.FindProperty("inertiaTensor").vector3Value == tensor && massModule.FindProperty("combinedInertiaTensor").vector3Value == tensor, "native module owns truck inertia");
        typeof(DodgeRam1500TRXRuntime).GetMethod("ConfigureMassProperties", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { instance });
        massModule.Update();
        Require(massModule.FindProperty("inertiaTensor").vector3Value == tensor && massModule.FindProperty("centerOfMass").vector3Value == body.centerOfMass, "runtime mass configuration parity");
        var boxes = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "BodyCollider").GetComponentsInChildren<BoxCollider>(true);
        var underside = instance.transform.InverseTransformPoint(boxes[0].transform.TransformPoint(boxes[0].center - Vector3.up * boxes[0].size.y * .5f)).y;
        var physics = instance.GetComponentsInChildren<MonoBehaviour>(true).Single(c => c != null && c.GetType().FullName == "NWH.VehiclePhysics2.VehicleController");
        var groups = new SerializedObject(physics).FindProperty("powertrain.wheelGroups");
        for (var i = 0; i < groups.arraySize; i++) Require(groups.GetArrayElementAtIndex(i).FindPropertyRelative("antiRollBarForce").floatValue == 12000f, "native axle roll resistance");
        Require(underside >= .08f && body.constraints == RigidbodyConstraints.None, "chassis clears road without frozen rotation");
        Debug.Log("TRX chassis matrix passed: COM .35 m, inertia 8000/8400/3200, native/runtime parity, chassis underside .09 m.");
    }

    private static void ValidateSteering(GameObject instance)
    {
        var controller = instance.GetComponentsInChildren<MonoBehaviour>(true).Single(c => c != null && c.GetType().FullName == "NWH.VehiclePhysics2.VehicleController");
        var data = new SerializedObject(controller);
        var curve = data.FindProperty("steering.speedSensitiveSteeringCurve").animationCurveValue;
        var expected = DodgeRam1500TRXSteeringProfile.CreateSpeedCurve();
        Require(data.FindProperty("steering.maximumSteerAngle").floatValue == 35f && data.FindProperty("steering.degreesPerSecondLimit").floatValue == 160f, "full lock and steering response");
        for (var speed = 0f; speed <= 25f; speed += .1f) Require(Mathf.Abs(curve.Evaluate(speed / 180f) - 1f) < .00001f, "full lock retained through 25 km/h");
        var prior = new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(.3f, .4f, -.6f, -.6f), new Keyframe(1f, .2f, -.1f, .1f));
        var last = 1f;
        for (var speed = 0f; speed <= 190f; speed += .1f)
        {
            var value = curve.Evaluate(speed / 180f);
            Require(value >= .19f && value <= 1.00001f && value <= last + .00001f, "bounded monotonic steering curve");
            if (speed >= 54f) Require(value > prior.Evaluate(speed / 180f), "high speed steering has more available lock");
            Require(Mathf.Abs(value - expected.Evaluate(speed / 180f)) < .00001f, "authored/runtime steering curve parity");
            last = value;
        }
        Require(data.FindProperty("steering.steeringWheel").objectReferenceValue == null, "road wheel steering leaves interior wheel unbound");
        Debug.Log("TRX steering matrix passed: 35 degree lock through 25 km/h, 160 deg/s response, monotonic city transition, increased available lock above 54 km/h, native/runtime parity.");
    }

    private static void ValidateDriverHands(GameObject instance)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var driverType = typeof(DodgeRam1500TRXMod).Assembly.GetType("DodgeRam1500TRXDriverController", true)!;
        var limbType = driverType.GetNestedType("SeatedLimb", BindingFlags.NonPublic)!;
        var driver = instance.AddComponent(driverType);
        var wheel = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "DodgeRam1500TRXSteeringWheel");
        driverType.GetField("vehicleRoot", flags)!.SetValue(driver, instance.transform);
        driverType.GetField("steeringWheel", flags)!.SetValue(driver, wheel);
        var host = new GameObject("TRX synthetic hands"); host.transform.SetParent(instance.transform, false);
        try
        {
            var original = Quaternion.Euler(20f, 15f, 5f);
            var limbs = new List<(object limb, Transform hand, float side)>();
            foreach (var side in new[] { -1f, 1f })
            {
                var upper = new GameObject("upper").transform; upper.SetParent(host.transform, false); upper.localPosition = new Vector3(side * .3f, 1.2f, .4f);
                var lower = new GameObject("lower").transform; lower.SetParent(upper, false); lower.position = instance.transform.TransformPoint(new Vector3(side * .55f, 1.24f, .64f));
                var hand = new GameObject("hand").transform; hand.SetParent(lower, false); hand.position = wheel.position + instance.transform.TransformVector(new Vector3(side * .19f, 0f, -.05f)); hand.rotation = instance.transform.rotation * original;
                var limb = Activator.CreateInstance(limbType, new object[] { upper, lower, hand })!;
                driverType.GetField(side < 0f ? "leftArm" : "rightArm", flags)!.SetValue(driver, limb);
                limbs.Add((limb, hand, side));
            }
            for (var pass = 0; pass < 3; pass++)
            {
                foreach (var limb in limbs) limbType.GetMethod("RestoreAnimationPose")!.Invoke(limb.limb, null);
                driverType.GetMethod("AlignHandsWithWheel", flags)!.Invoke(driver, null);
                foreach (var limb in limbs)
                {
                    var expected = wheel.position + instance.transform.TransformVector(new Vector3(limb.side * .19f, 0f, -.07f));
                    Require(Vector3.Distance(limb.hand.position, expected) < .001f, "hands moved 2 cm backward");
                    Require(Quaternion.Angle(limb.hand.rotation, Quaternion.AngleAxis(limb.side * 20f, instance.transform.up) * instance.transform.rotation * original) < .01f, "hands yaw outward 20 degrees without accumulation");
                }
            }
            Debug.Log("TRX driver matrix passed: hands back 2 cm, outward 20 degrees, three non-accumulating pose evaluations.");
        }
        finally { UnityEngine.Object.DestroyImmediate(host); UnityEngine.Object.DestroyImmediate(driver); }
    }

    private static void ValidatePlayerPrefabRegistration(GameObject prefab)
    {
        const string key = "Prefabs/Vehicles/PlayerVehicles/dodgeram1500trx.prefab";
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var type = typeof(DodgeRam1500TRXMod).Assembly.GetType("DodgeRam1500TRXPlayerPrefabRegistration", true)!;
        var cacheField = (FieldInfo)type.GetField("CacheField", flags)!.GetValue(null);
        var cache = (IDictionary)cacheField.GetValue(null);
        var lookup = cacheField.DeclaringType!.GetMethod("LoadPrefabAssetByName", BindingFlags.Static | BindingFlags.Public)!;
        object? Lookup() => lookup.Invoke(null, new object[] { "Vehicles/PlayerVehicles/dodgeram1500trx" });
        var create = cacheField.DeclaringType.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(m => m.Name == "CreatePrefab" && !m.IsGenericMethod && m.GetParameters().Length == 2);
        var holder = new GameObject("TRX inactive native spawn review"); holder.SetActive(false);
        void Spawn()
        {
            var instance = (GameObject)create.Invoke(null, new object[] { "Vehicles/PlayerVehicles/dodgeram1500trx", holder.transform });
            Require(instance != null && instance != prefab && instance.GetComponent<Rigidbody>() != null &&
                instance.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c != null && c.GetType().Name == "CarController"), "native CreatePrefab constructs Dodge vehicle");
            UnityEngine.Object.DestroyImmediate(instance);
        }
        var originalPresent = cache.Contains(key); var original = originalPresent ? cache[key] : null;
        var other = new GameObject("TRX registration previous owner");
        var foreign = new GameObject("TRX registration replacement owner");
        var unrelated = "Prefabs/TRXReviewUnrelated.prefab";
        var unrelatedPresent = cache.Contains(unrelated); var unrelatedOriginal = unrelatedPresent ? cache[unrelated] : null;
        var bind = type.GetMethod("Bind", flags)!;
        var release = type.GetMethod("Release", flags)!;
        bool Bind(GameObject? value, string source) => (bool)bind.Invoke(null, new object?[] { null, value, source });
        try
        {
            // Only these two test keys change; the game's other cache entries stay intact.
            cache[key] = other; cache[unrelated] = foreign;
            var count = cache.Count;
            Require(Bind(prefab, "review-initialization") && ReferenceEquals(Lookup(), prefab), "native exact spawn lookup");
            Spawn();
            Require(Bind(prefab, "review-repeat") && cache.Count == count, "idempotent prefab binding");
            Require(!Bind(null, "review-missing-asset") && ReferenceEquals(cache[key], prefab), "missing prefab does not corrupt existing binding");
            release.Invoke(null, null);
            Require(ReferenceEquals(cache[key], other), "unload restores previous mapping");
            Require(Bind(prefab, "review-before-cache-reset"), "binding before cache reset");
            cache.Remove(key); // Simulate this key being cleared by native game unload.
            Require(Bind(prefab, "review-city-after-cache-reset") && ReferenceEquals(Lookup(), prefab), "city lookup restored after native cache reset");
            Spawn();
            release.Invoke(null, null);
            Require(!cache.Contains(key), "unload does not restore a stale prior-game mapping");
            Require(Bind(prefab, "review-replacement"), "replacement ownership setup");
            cache[key] = other;
            release.Invoke(null, null);
            Require(ReferenceEquals(cache[key], other) && ReferenceEquals(cache[unrelated], foreign), "unload preserves other owner and unrelated key");
            var registrations = typeof(DodgeRam1500TRXMod).Assembly.GetCustomAttributes(false);
            Require(registrations.Count(a => a.GetType().Name == "RegisterModClassAttribute" || a.GetType().Name == "RegisterModClass") == 3, "all three mod lifecycle registrations present");
            foreach (var pair in new[] { ("DodgeRam1500TRXMod", "ModEntryOnInitializationLoad"), ("DodgeRam1500TRXMainMenuPrefabRegistration", "ModEntryMainMenu"), ("DodgeRam1500TRXCityPrefabRegistration", "ModEntryOnCityLoad") })
                Require(typeof(DodgeRam1500TRXMod).Assembly.GetType(pair.Item1, true)!.GetCustomAttributes(false).Any(a => a.GetType().Name.StartsWith(pair.Item2, StringComparison.Ordinal)), pair.Item1 + " correct lifecycle attribute");
            Debug.Log("TRX prefab registration matrix passed: exact native lookup and CreatePrefab before/after cache reset, repeat binding, missing asset, cache-reset/city rebind, prior-owner restore, foreign-owner preservation, three lifecycle entries.");
        }
        finally
        {
            release.Invoke(null, null);
            if (originalPresent) cache[key] = original; else cache.Remove(key);
            if (unrelatedPresent) cache[unrelated] = unrelatedOriginal; else cache.Remove(unrelated);
            UnityEngine.Object.DestroyImmediate(other); UnityEngine.Object.DestroyImmediate(foreign);
            UnityEngine.Object.DestroyImmediate(holder);
        }
    }

    private static void ValidateMaterials(GameObject prefab)
    {
        var holder = new GameObject("TRX inactive material review"); holder.SetActive(false);
        var a = UnityEngine.Object.Instantiate(prefab, holder.transform);
        var b = UnityEngine.Object.Instantiate(prefab, holder.transform);
        var sources = prefab.GetComponentsInChildren<Renderer>(true).Where(r => DodgeRam1500TRXMaterials.IsDodgeRam1500TRXRenderer(r.transform)).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToArray();
        try
        {
            Require(sources.All(m => m.shader != null && (m.shader.name == "HDRP/Lit" || m.shader.name == "HDRP/Unlit")), "no imported error shaders in bundle");
            var paint = sources.Single(m => m.name.Contains("Paint_Material"));
            Require(paint.GetColor("_BaseColor").b > .4f && paint.GetFloat("_Metallic") > .5f, "original GLB paint data restored");
            Require(sources.Count(m => m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null) >= 15, "original GLB texture maps restored");
            Require(sources.Count(m => m.HasProperty("_NormalMap") && m.GetTexture("_NormalMap") != null) >= 12, "original GLB normals restored");
            foreach (var material in sources.Where(m => m.name.Contains("Paint_Material") || m.name.Contains("Window_Material")))
                Require(material.GetTexture("_BaseColorMap") == null && material.GetTexture("_NormalMap") == null && material.GetTexture("_MaskMap") == null, material.name + " map-free source");
            var badge = sources.Single(m => m.name.Contains("BadgeA"));
            Require(badge.GetFloat("_AlphaCutoffEnable") == 1f && badge.IsKeywordEnabled("_ALPHATEST_ON"), "badge cutout preserved");
            var first = DodgeRam1500TRXMaterials.FixSolidMaterials(a);
            var second = DodgeRam1500TRXMaterials.FixSolidMaterials(b);
            Require(first.OpaqueMaterialsFixed > 0 && first.TransparentMaterialsFixed > 0 && second.OpaqueMaterialsFixed == first.OpaqueMaterialsFixed, "opaque and clear runtime shader binding");
            var aa = a.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null && m.name.Contains("Paint_Material")).First();
            var bb = b.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null && m.name.Contains("Paint_Material")).First();
            var original = paint.GetColor("_BaseColor"); aa.SetColor("_BaseColor", Color.green);
            Require(aa != bb && bb.GetColor("_BaseColor") == original && paint.GetColor("_BaseColor") == original, "two-instance paint isolation");
            foreach (var material in a.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct())
            {
                if (material.name.StartsWith(DodgeRam1500TRXMaterials.RearCabinGlassMarker, StringComparison.Ordinal))
                    Require(material.GetFloat("_SurfaceType") == 1f && material.GetFloat("_ZWrite") == 0f && Mathf.Abs(material.GetColor("_BaseColor").a - .30f) < .0001f && !material.GetShaderPassEnabled("DepthOnly"), "rear tint survives runtime binding");
                else if (material.name.Contains("Window_Material"))
                    Require(material.GetFloat("_SurfaceType") == 1f && material.GetFloat("_ZWrite") == 0f && material.GetColor("_BaseColor").a < .2f &&
                        material.GetTexture("_BaseColorMap") == null && material.GetTexture("_NormalMap") == null && !material.GetShaderPassEnabled("DepthOnly"), "clear cabin without opaque depth pass");
                else if (material.name.Contains("Paint_Material"))
                    Require(material.GetFloat("_SurfaceType") == 0f && material.GetFloat("_ZWrite") == 1f && !material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT") && material.GetColor("_BaseColor").a == 1f, "solid painted body");
            }
            Require(a.GetComponentsInChildren<Renderer>(true).Where(r => DodgeRam1500TRXMaterials.IsDodgeRam1500TRXRenderer(r.transform)).All(r => (r.renderingLayerMask & 0x0000FF00u) == 0u), "vehicle decal layers disabled");
            Require(DodgeRam1500TRXMaterials.FixSolidMaterials(a).OpaqueMaterialsFixed == first.OpaqueMaterialsFixed, "idempotent runtime materials");
            foreach (var lamp in a.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("DodgeRam1500TRX_Lamp_", StringComparison.Ordinal)))
            {
                var material = lamp.sharedMaterial;
                Require(material.shader.name == "HDRP/Unlit" && material.GetColor("_UnlitColor").maxColorComponent > 0f && material.GetColor("_UnlitColor").a == 1f && material.GetShaderPassEnabled("ForwardOnly"), lamp.name + " visible after runtime shader binding");
                Require(material.GetColor("_EmissiveColor") == Color.black, lamp.name + " no duplicate radiance");
            }
            var rearPanes = a.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name.StartsWith(DodgeRam1500TRXMaterials.RearCabinGlassMarker, StringComparison.Ordinal)).ToArray();
            Require(rearPanes.Length == 2, "inner/outer rear glazing independently tinted");
            foreach (var pane in rearPanes)
            {
                var points = pane.sharedMesh.vertices.Select(v => a.transform.InverseTransformPoint(pane.transform.TransformPoint(v))).ToArray();
                Require(points.All(v => v.z < .20f) && points.Any(v => v.x < -.70f) && points.Any(v => v.x > .70f) && points.Any(v => v.z < -.90f && Mathf.Abs(v.x) < .50f), "both rear doors and cab rear window, without front panes");
                var material = pane.GetComponent<Renderer>().sharedMaterial;
                typeof(DodgeRam1500TRXMaterials).GetMethod("RestoreCabinGlassMaterial", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { material });
                Require(Mathf.Abs(material.GetColor("_BaseColor").a - .30f) < .0001f, "rear tint survives cabin glass restore");
            }
            foreach (var pane in a.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name.Contains("Window_Material") && (f.name.StartsWith("Window_Geo", StringComparison.Ordinal) || f.name.StartsWith("WindowInside_Geo", StringComparison.Ordinal))))
                Require(pane.sharedMesh.vertices.All(v => a.transform.InverseTransformPoint(pane.transform.TransformPoint(v)).z >= .20f) && Mathf.Abs(pane.GetComponent<Renderer>().sharedMaterial.GetColor("_BaseColor").a - .14f) < .0001f, "front doors and windshield unchanged: " + pane.name);

            ValidateReloadPaint(a, b);
            Debug.Log($"TRX material matrix passed: HDRP shaders, original maps/colors, badge alpha, opaque={first.OpaqueMaterialsFixed}, clear={first.TransparentMaterialsFixed}, two-instance ownership, idempotence.");
        }
        finally
        {
            // Editor-only cleanup; production OnDestroy releases the same list.
            foreach (var clone in new[] { a, b })
            {
                var controller = clone.GetComponent<DodgeRam1500TRXMaterialController>();
                var owned = (List<Material>)typeof(DodgeRam1500TRXMaterialController).GetField("ownedMaterials", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller);
                foreach (var material in owned) UnityEngine.Object.DestroyImmediate(material);
                owned.Clear();
            }
            UnityEngine.Object.DestroyImmediate(holder);
        }
    }

    private static void ValidateReloadPaint(GameObject first, GameObject second)
    {
        var paintType = typeof(DodgeRam1500TRXPaintController);
        var vehicleType = NativeType("VehicleController"); var carType = NativeType("CarController");
        var featuresType = NativeType("CarFeatures"); var colorType = NativeType("Data.VehicleColors.VehicleColor");
        var referencesType = NativeType("GlobalReferences");
        var lifecycle = referencesType.BaseType!.Assembly.GetType("Extensions.SingletonLifecycle", true)!;
        var register = lifecycle.GetMethod("RegisterSingleton", AllStatics)!;
        var unregister = lifecycle.GetMethod("UnregisterSingleton", AllStatics)!;
        var oldReferences = lifecycle.GetMethod("GetSingleton", AllStatics)!.MakeGenericMethod(referencesType).Invoke(null, null);
        var wasMissing = (bool)lifecycle.GetMethod("IsTypeMissing", AllStatics)!.Invoke(null, new object[] { referencesType });
        var host = new GameObject("TRX paint registry fixture"); host.SetActive(false);
        var registry = host.AddComponent(referencesType);
        var registryColors = referencesType.GetField("vehicleColors", AllMembers)!;
        var tint = colorType.GetField("tint", AllMembers)!;
        ScriptableObject MakeColor(string name, Color32 value)
        {
            var c = ScriptableObject.CreateInstance(colorType); c.name = name; tint.SetValue(c, value); return c;
        }
        var red = MakeColor("fixture_red", new Color32(225, 25, 15, 255));
        var blue = MakeColor("fixture_blue", new Color32(10, 70, 225, 255));
        var black = MakeColor("fixture_provisional_black", new Color32(13, 13, 13, 255));
        void Palette(params ScriptableObject[] colors)
        {
            var array = Array.CreateInstance(colorType, colors.Length);
            for (var i = 0; i < colors.Length; i++) array.SetValue(colors[i], i);
            registryColors.SetValue(registry, array);
        }
        var instanceField = vehicleType.GetField("vehicleInstance", AllMembers)!;
        var featuresField = vehicleType.GetField("<CarFeatures>k__BackingField", AllMembers)!;
        var liveField = featuresType.GetField("<VehicleColor>k__BackingField", AllMembers)!;
        void SetLive(GameObject root, ScriptableObject color) => liveField.SetValue(featuresField.GetValue(root.GetComponent(carType)), color);
        var applied = new List<DodgeRam1500TRXPaintController>();
        Component Prepare(GameObject root, ScriptableObject saved)
        {
            var car = root.GetComponent(carType);
            var data = Activator.CreateInstance(instanceField.FieldType, new object[] { "dodgeram1500trx-vehicle:vehicletype_dodgeram1500trx" })!;
            instanceField.FieldType.GetField("vehicleColorName", AllMembers)!.SetValue(data, saved.name);
            instanceField.SetValue(car, data);
            featuresField.SetValue(car, root.GetComponentsInChildren(featuresType, true).First());
            SetLive(root, black);
            return car;
        }
        DodgeRam1500TRXPaintController Bind(GameObject root, ScriptableObject saved)
        {
            var car = Prepare(root, saved);
            var component = root.AddComponent<DodgeRam1500TRXPaintController>(); applied.Add(component);
            paintType.GetMethod("Initialize", AllMembers)!.Invoke(component, new object?[] { car, null });
            return component;
        }
        (Renderer renderer, int slot)[] Slots(GameObject root) => root.GetComponentsInChildren<Renderer>(true)
            .SelectMany(r => r.sharedMaterials.Select((m, i) => (r, m, i)))
            .Where(x => x.m != null && x.m.name.Contains("RAM_1500TRX_2021Paint_Material"))
            .Select(x => (x.r, x.i)).ToArray();
        void Check(GameObject root, ScriptableObject color)
        {
            var bindings = Slots(root); Require(bindings.Length > 0, "actual bundled Dodge paint bindings");
            var expected = (Color)(Color32)tint.GetValue(color)!; expected.a = 1f;
            foreach (var binding in bindings)
            {
                var block = new MaterialPropertyBlock(); binding.renderer.GetPropertyBlock(block, binding.slot);
                var actual = block.GetColor("_BaseColor");
                Require(Mathf.Abs(actual.r - expected.r) < .00001f && Mathf.Abs(actual.g - expected.g) < .00001f &&
                    Mathf.Abs(actual.b - expected.b) < .00001f && actual.a == 1f, "saved/preview tint on every actual paint slot");
            }
        }
        void Clear(GameObject root) { foreach (var slot in Slots(root)) slot.renderer.SetPropertyBlock(null, slot.slot); }
        void Apply(DodgeRam1500TRXPaintController p) => paintType.GetMethod("ApplyCurrentColor", AllMembers)!.Invoke(p, new object[] { "reload-fixture", false, false });
        var gameEvent = NativeType("GameEvent"); var notify = gameEvent.GetMethod("Invoke", AllStatics)!;
        void Restored() => notify.Invoke(null, new object[] { "vehicle-repainter:color-restored" });
        var sourceMaterials = first.GetComponentsInChildren<Renderer>(true).Concat(second.GetComponentsInChildren<Renderer>(true))
            .SelectMany(r => r.sharedMaterials).Where(m => m != null && m.HasProperty("_BaseColor")).Distinct().ToDictionary(m => m, m => m.GetColor("_BaseColor"));
        try
        {
            Palette(red, blue, black); register.Invoke(null, new object[] { referencesType, registry });
            lifecycle.GetMethod("UnmarkMissingType", AllStatics)!.Invoke(null, new object[] { referencesType });
            // The previous fixture pre-created paint controllers and missed
            // the failing real load order. Start with neither controller and
            // drive the actual runtime/prefab event receivers instead.
            var runtimeHost = new GameObject("TRX cold-load appearance dispatcher"); runtimeHost.SetActive(false);
            var runtime = runtimeHost.AddComponent<DodgeRam1500TRXRuntime>();
            typeof(DodgeRam1500TRXRuntime).GetField("vehicleTypeName", AllMembers)!.SetValue(runtime, "dodgeram1500trx-vehicle:vehicletype_dodgeram1500trx");
            var playerVehicles = (IList)NativeType("Helpers.VehicleHelper").GetField("AllPlayerVehicles", AllStatics)!.GetValue(null);
            var firstCar = Prepare(first, red); var secondCar = Prepare(second, blue);
            var loadingLayouts = NativeType("BusinessLayoutSets.BusinessLayoutSetHelper").GetField("loadingLayouts", AllStatics)!;
            var oldLoadingLayouts = loadingLayouts.GetValue(null);
            loadingLayouts.SetValue(null, true);
            void RemovePaint(GameObject root)
            {
                var old = root.GetComponent<DodgeRam1500TRXPaintController>();
                if (old == null) return;
                paintType.GetMethod("UnsubscribeAppearanceEvents", AllMembers)!.Invoke(old, null);
                UnityEngine.Object.DestroyImmediate(old); Clear(root);
            }
            try
            {
                typeof(DodgeRam1500TRXRuntime).GetMethod("SubscribeEvents", AllMembers)!.Invoke(runtime, null);
                playerVehicles.Add(firstCar); playerVehicles.Add(secondCar);
                Palette(black); SetLive(first, red); SetLive(second, blue);
                Require(first.GetComponent<DodgeRam1500TRXPaintController>() == null && second.GetComponent<DodgeRam1500TRXPaintController>() == null, "cold load begins without any paint controller");
                Restored(); Check(first, red); Check(second, blue);
                Require(first.GetComponent<DodgeRam1500TRXPaintController>() != null && second.GetComponent<DodgeRam1500TRXPaintController>() != null, "unselected restore event creates both missing paint controllers");
                var ownedA = first.GetComponent<DodgeRam1500TRXPaintController>(); Restored();
                Require(first.GetComponent<DodgeRam1500TRXPaintController>() == ownedA && first.GetComponents<DodgeRam1500TRXPaintController>().Length == 1, "restore bootstrap is idempotent");
                RemovePaint(first); RemovePaint(second); playerVehicles.Remove(firstCar); playerVehicles.Remove(secondCar);
                Palette(red, blue, black); SetLive(first, black); SetLive(second, black);
                foreach (var root in new[] { first, second })
                {
                    var relay = root.GetComponent<DodgeRam1500TRXNativeActivities>();
                    Require(relay != null, "owned native-ready relay exists in built prefab");
                    var ready = (IEnumerator)typeof(DodgeRam1500TRXNativeActivities).GetMethod("Start", AllMembers)!.Invoke(relay, null);
                    Require(ready.MoveNext() && ready.Current == null, "spawn handoff defers exactly one native initialization frame");
                    Require(!ready.MoveNext(), "spawn handoff completes without polling");
                }
                Check(first, red); Check(second, blue); // No selection, restore event or registered-list membership.
                RemovePaint(first); RemovePaint(second);
                playerVehicles.Add(firstCar); playerVehicles.Add(secondCar);
                var sceneReady = (IEnumerator)typeof(DodgeRam1500TRXRuntime).GetMethod("InitializeForLifecycle", AllMembers)!.Invoke(runtime, new object[] { "fixture-scene-layout-still-loading" });
                Require(sceneReady.MoveNext() && !sceneReady.MoveNext(), "scene appearance pass completes while layouts/dealer setup remains deferred");
                Check(first, red); Check(second, blue);
                Require((int)typeof(DodgeRam1500TRXRuntime).GetField("configuredVehicleIds", AllMembers)!.FieldType.GetProperty("Count")!.GetValue(
                    typeof(DodgeRam1500TRXRuntime).GetField("configuredVehicleIds", AllMembers)!.GetValue(runtime))! == 0, "appearance readiness does not prematurely configure physics or dealer state");
                RemovePaint(first); RemovePaint(second);
                Debug.Log("TRX cold-load bootstrap passed: restore event before paint controllers exist creates two unselected owners; repeated event is idempotent; bundled one-frame spawn relay initializes even before player-list registration; scene appearance pass works while layout/dealer/physics setup stays deferred.");
            }
            finally
            {
                typeof(DodgeRam1500TRXRuntime).GetMethod("UnsubscribeEvents", AllMembers)!.Invoke(runtime, null);
                playerVehicles.Remove(firstCar); playerVehicles.Remove(secondCar);
                loadingLayouts.SetValue(null, oldLoadingLayouts);
                RemovePaint(first); RemovePaint(second); UnityEngine.Object.DestroyImmediate(runtimeHost);
            }
            Palette(red, blue, black);
            var a = Bind(first, red); var b = Bind(second, blue);
            Require(!first.activeInHierarchy && !second.activeInHierarchy &&
                !(bool)vehicleType.GetField("controlledByPlayer", AllMembers)!.GetValue(first.GetComponent(carType))! &&
                !(bool)vehicleType.GetField("controlledByPlayer", AllMembers)!.GetValue(second.GetComponent(carType))!, "two parked unoccupied cold-load fixtures");
            Check(first, red); Check(second, blue); Apply(a); Apply(b); Check(first, red); Check(second, blue);
            // Optional saved colors absent from the native dealer palette.
            Palette(black); Clear(first); Clear(second);
            SetLive(first, red); SetLive(second, blue); Restored(); Check(first, red); Check(second, blue);
            Clear(first); Clear(second); Restored(); Check(first, red); Check(second, blue);
            SetLive(first, blue); Apply(a); Check(first, blue); Check(second, blue); // Live repaint preview wins after native restore.
            paintType.GetMethod("OnDisable", AllMembers)!.Invoke(a, null); Clear(first); Restored();
            Require(!Slots(first).First().renderer.HasPropertyBlock(), "disabled controller releases color-restore subscription");
            SetLive(first, black); Palette(red, blue, black);
            paintType.GetMethod("OnEnable", AllMembers)!.Invoke(a, null); Check(first, red);
            SetLive(first, red); Apply(a);
            // Legitimately saved black must remain black, not be treated as missing.
            instanceField.FieldType.GetField("vehicleColorName", AllMembers)!.SetValue(instanceField.GetValue(second.GetComponent(carType)), black.name);
            SetLive(second, black); paintType.GetMethod("OnEnable", AllMembers)!.Invoke(b, null); Check(second, black);
            paintType.GetMethod("InitializeForPrivateDriver", AllMembers)!.Invoke(b, new object[] { blue.name, blue });
            Restored(); Check(second, blue); // Explicit traffic/chauffeur color preserved.
            Require(sourceMaterials.All(x => x.Key.GetColor("_BaseColor") == x.Value), "shared/trim materials unchanged by reload paint");
            Require(!typeof(DodgeRam1500TRXMod).Assembly.GetReferencedAssemblies().Any(x => x.Name.IndexOf("Repainter", StringComparison.OrdinalIgnoreCase) >= 0), "no repainter mod assembly dependency");
            Debug.Log("TRX reload paint matrix passed: two inactive unoccupied cars override provisional Black from saved colors; optional palette event without selection, cleared blocks, live preview, disable/re-enable, legitimate saved black, explicit traffic/chauffeur paint and shared-material preservation.");
        }
        finally
        {
            foreach (var component in applied)
            {
                paintType.GetMethod("UnsubscribeAppearanceEvents", AllMembers)!.Invoke(component, null);
                UnityEngine.Object.DestroyImmediate(component);
            }
            unregister.Invoke(null, new object[] { referencesType, registry });
            if (oldReferences != null) register.Invoke(null, new[] { (object)referencesType, oldReferences });
            if (wasMissing) lifecycle.GetMethod("MarkMissingType", AllStatics)!.Invoke(null, new object[] { referencesType });
            foreach (var c in new[] { red, blue, black }) UnityEngine.Object.DestroyImmediate(c);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static float ForeignPowerModifier() => 1f;

    private static void ValidatePowertrain(GameObject instance)
    {
        Require(instance.GetComponent<Rigidbody>().drag == 0f, "no duplicate linear drag beside aerodynamic force");
        var controller = instance.GetComponentsInChildren<MonoBehaviour>(true).Single(c => c != null && c.GetType().FullName == "NWH.VehiclePhysics2.VehicleController");
        var serialized = new SerializedObject(controller);
        var paths = new[] { "powertrain.clutch.creepTorque", "brakes.maxTorque", "powertrain.clutch.slipTorque", "powertrain.engine.inertia", "powertrain.transmission._upshiftRPM", "powertrain.transmission._downshiftRPM", "powertrain.transmission.shiftDuration", "wheelbase", "powertrain.engine.engineLossPercent" };
        var expected = new[] { 0f, DodgeRam1500TRXPowertrainProfile.BrakeTorque, 1100f, .8f, 5800f, 3100f, .22f, 3.6705f, DodgeRam1500TRXPowertrainProfile.NativeEngineLoss };
        for (var i = 0; i < paths.Length; i++) Require(Mathf.Abs(serialized.FindProperty(paths[i]).floatValue - expected[i]) < .001f, paths[i] + " authored value");
        Require(!serialized.FindProperty("powertrain.transmission.variableShiftPoint").boolValue, "bounded automatic shift point");
        Require(serialized.FindProperty("steering.steeringWheel").objectReferenceValue == null, "interior steering wheel remains static");
        var curve = serialized.FindProperty("powertrain.engine.powerCurve").animationCurveValue;
        var peakTorque = 0f; var torqueRpm = 0f; var peakPower = 0f;
        for (var rpm = 700f; rpm <= 6200f; rpm += 1f)
        {
            var power = curve.Evaluate(rpm / 6200f) * 523f;
            var torque = power * 1000f / (rpm * Mathf.PI / 30f);
            if (torque > peakTorque) { peakTorque = torque; torqueRpm = rpm; }
            peakPower = Mathf.Max(peakPower, power);
        }
        Require(Mathf.Abs(peakTorque - 882f) < .2f && Mathf.Abs(torqueRpm - 4800f) < 2f && Mathf.Abs(peakPower - 523f) < .2f, "published power and torque peaks without interpolation overshoot");
        Require(expected[2] > peakTorque * 1.2f, "clutch capacity exceeds peak torque");
        typeof(DodgeRam1500TRXRuntime).GetMethod("ConfigurePowertrain", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { instance });
        serialized.Update();
        Require(Mathf.Abs(serialized.FindProperty("powertrain.transmission._downshiftRPM").floatValue - 3100f) < .001f, "3100 RPM downshift survives runtime setup");
        var gearRatios = serialized.FindProperty("powertrain.transmission.gears");
        for (var i = 3; i < gearRatios.arraySize; i++)
        {
            var projectedRpm = 3100f * gearRatios.GetArrayElementAtIndex(i - 1).floatValue / gearRatios.GetArrayElementAtIndex(i).floatValue;
            Require(projectedRpm < 5800f, "adjacent downshift at 3100 RPM stays below upshift/limiter range");
        }
        Debug.Log("TRX final shift profile passed: authored/runtime downshift 3100 RPM, upshift 5800 RPM, every adjacent forward downshift below upshift and limiter.");
        var loss = instance.GetComponent<DodgeRam1500TRXDrivelineLoss>();
        Require(loss != null && loss.IsBound, "native driveline modifier registered");
        object Member(object value, string name) => value.GetType().GetField(name)!.GetValue(value);
        var nativeEngine = Member(Member(controller, "powertrain"), "engine");
        var modifiers = (IList)Member(nativeEngine, "powerModifiers");
        var beforeCount = modifiers.Count;
        var foreign = Delegate.CreateDelegate(modifiers[0].GetType(), typeof(DodgeRam1500TRXReview).GetMethod(nameof(ForeignPowerModifier), BindingFlags.Static | BindingFlags.NonPublic)!);
        modifiers.Add(foreign);
        typeof(DodgeRam1500TRXRuntime).GetMethod("ConfigurePowertrain", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { instance });
        Require(loss!.IsBound && modifiers.Count == beforeCount + 1 && modifiers.Contains(foreign), "repeat configuration keeps one modifier and preserves foreign callbacks");
        var calculate = typeof(DodgeRam1500TRXDrivelineLoss).GetMethod("CalculateModifier", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Require((float)calculate.Invoke(loss, null) == 1f, "zero throttle preserves engine braking and idle");
        var input = Member(controller, "input");
        var throttle = input.GetType().GetField("_inputSwappedThrottle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        throttle.SetValue(input, 1f);
        Require(Mathf.Abs((float)calculate.Invoke(loss, null) - DodgeRam1500TRXPowertrainProfile.DrivePowerModifier(.57f)) < .00001f, "full-throttle native callback applies propulsion loss");
        throttle.SetValue(input, 0f);
        foreach (var nativeLoss in new[] { 0f, .1f, .57f })
        {
            var net = (1f + nativeLoss) * DodgeRam1500TRXPowertrainProfile.DrivePowerModifier(nativeLoss) - nativeLoss;
            Require(Mathf.Abs(net - .945f) < .00001f, "native gross-power compensation leaves 94.5 percent propulsion");
        }
        var disable = typeof(DodgeRam1500TRXDrivelineLoss).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var enable = typeof(DodgeRam1500TRXDrivelineLoss).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)!;
        disable.Invoke(loss, null);
        Require(!loss.IsBound && modifiers.Count == beforeCount && modifiers.Contains(foreign), "disable removes only owned modifier");
        enable.Invoke(loss, null); enable.Invoke(loss, null);
        Require(loss.IsBound && modifiers.Count == beforeCount + 1, "enable rebinds without duplication");
        typeof(DodgeRam1500TRXDrivelineLoss).GetMethod("Shutdown", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(loss, null);
        Require(!loss.IsBound && modifiers.Contains(foreign), "mod unload removes owned modifier");
        modifiers.Remove(foreign);
        serialized.Update();
        for (var i = 0; i < paths.Length; i++) Require(Mathf.Abs(serialized.FindProperty(paths[i]).floatValue - expected[i]) < .001f, paths[i] + " runtime parity");
        var applied = serialized.FindProperty("powertrain.engine.powerCurve").animationCurveValue;
        for (var i = 0; i < curve.length; i++) Require(applied[i].time == curve[i].time && applied[i].value == curve[i].value && applied[i].inTangent == curve[i].inTangent && applied[i].outTangent == curve[i].outTangent, "runtime power curve parity");
        Require(DodgeRam1500TRXPowertrainProfile.AerodynamicForce(0f) == 0f && Mathf.Abs(DodgeRam1500TRXPowertrainProfile.AerodynamicForce(20f) / DodgeRam1500TRXPowertrainProfile.AerodynamicForce(10f) - 4f) < .00001f, "quadratic drag without launch force");
        Debug.Log($"TRX drivetrain matrix passed: {peakTorque:F1} Nm at {torqueRpm:F0} RPM, {peakPower:F1} kW, clutch 1100 Nm, inertia .8, 5800 RPM shifts, zero creep, 4531.25 Nm brakes, 5.5% propulsion loss, unchanged coasting, callback lifecycle/isolation, quadratic drag, prefab/runtime parity.");
    }

    private static void ValidateWarehouseControlRecovery(GameObject instance)
    {
        var vehicle = instance.GetComponentsInChildren<MonoBehaviour>(true).Single(c => c != null && c.GetType().Name == "CarController");
        var vehicleType = vehicle.GetType();
        while (vehicleType.Name != "VehicleController") vehicleType = vehicleType.BaseType!;
        var body = instance.GetComponent<Rigidbody>();
        var nativeBody = vehicleType.GetField("_rigidbody", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var originalCache = nativeBody.GetValue(vehicle);
        var constraints = body.constraints;
        var release = typeof(DodgeRam1500TRXRuntime).GetMethod("ReleaseWarehouseYaw", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            nativeBody.SetValue(vehicle, body);
            body.constraints = RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
            release.Invoke(null, new object[] { vehicle, body });
            Require(body.constraints == RigidbodyConstraints.None, "native warehouse constraints=96 released through SetFreeze(false)");
            body.constraints = RigidbodyConstraints.FreezeRotationX;
            release.Invoke(null, new object[] { vehicle, body });
            Require(body.constraints == RigidbodyConstraints.FreezeRotationX, "ordinary constraints without yaw lock preserved");
            Debug.Log("TRX warehouse recovery passed: native yaw lock released, ordinary constraints retained.");
        }
        finally { body.constraints = constraints; nativeBody.SetValue(vehicle, originalCache); }
    }

    private static void ValidateGeometry(GameObject prefab)
    {
        foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (mesh == null || renderer == null || !renderer.enabled || !DodgeRam1500TRXMaterials.IsDodgeRam1500TRXRenderer(filter.transform)) continue;
            Require(mesh.isReadable && mesh.normals.Length == mesh.vertexCount, filter.name + " readable normals");
            var determinant = (prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix).determinant;
            Require(determinant > 0f, filter.name + " no reflected renderer transform");
            Require(mesh.normals.All(n => !float.IsNaN(n.x) && n.sqrMagnitude > .5f && n.sqrMagnitude < 1.5f), filter.name + " finite unit normals");
        }
        var body = prefab.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "DodgeRam1500TRXDamageBody").sharedMesh;
        var vertices = body.vertices; var normals = body.normals; var triangles = body.triangles;
        var aligned = new int[2]; var opposed = new int[2];
        for (var i = 0; i < triangles.Length; i += 3)
        {
            var a = triangles[i]; var b = triangles[i + 1]; var c = triangles[i + 2];
            var face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (face.sqrMagnitude < 1e-12f) continue;
            var side = (vertices[a].x + vertices[b].x + vertices[c].x) < 0f ? 0 : 1;
            var agreement = Vector3.Dot(face.normalized, (normals[a] + normals[b] + normals[c]).normalized);
            if (agreement > .1f) aligned[side]++; else if (agreement < -.1f) opposed[side]++;
        }
        for (var side = 0; side < 2; side++)
        {
            Require(aligned[side] > 0 && opposed[side] < aligned[side] * .02f, "body winding and normals agree on side " + side);
            Debug.Log($"TRX body side {side}: normals/winding aligned={aligned[side]}, opposed={opposed[side]}.");
        }
    }

    private static void ValidateAudioMapping()
    {
        var type = typeof(DodgeRam1500TRXRuntime).Assembly.GetType("DodgeRam1500TRXAudioModel", true)!;
        var pitch = type.GetMethod("Pitch", BindingFlags.Static | BindingFlags.NonPublic)!;
        var weight = type.GetMethod("Weight", BindingFlags.Static | BindingFlags.NonPublic)!;
        for (var step = 0; step <= 1000; step++)
        {
            var position = step / 1000f; var energy = 0f;
            for (var layer = 0; layer < 3; layer++)
            {
                var p = (float)pitch.Invoke(null, new object[] { position, layer });
                var w = (float)weight.Invoke(null, new object[] { position, layer });
                Require(!float.IsNaN(p) && !float.IsNaN(w), "finite audio mapping");
                if (w > .01f) Require(p >= .5f && p < 1.91f, "audible layers use bounded pitch");
                energy += w * w;
            }
            Require(Mathf.Abs(energy - 1f) < .0001f, "continuous crossfade without level holes");
        }
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var sampleGain = (float)type.GetField("EngineSampleGain", flags)!.GetRawConstantValue();
        var idleVolume = type.GetMethod("IdleVolume", flags)!;
        var engineVolume = type.GetMethod("EngineVolume", flags)!;
        var wave = type.Assembly.GetType("DodgeRam1500TRXWave", true)!.GetMethod("Load", flags)!;
        float ReadPeak(string name, float gain)
        {
            var clip = (AudioClip)wave.Invoke(null, new object[] {
                "Assets/Mods/Dodge_Ram_1500/Config/Audio/" + name + ".wav", gain });
            try
            {
                var samples = new float[clip.samples];
                Require(clip.GetData(samples, 0), "read amplified engine waveform");
                var peak = samples.Max(v => Mathf.Abs(v));
                Require(peak < 1f, "PCM waveform retains unclipped headroom");
                return peak;
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }
        var enginePeak = 0f;
        foreach (var name in new[] { "EngineLow", "EngineMid", "EngineHigh",
                     "EngineLowLoad", "EngineMidLoad", "EngineHighLoad" })
        {
            var original = ReadPeak(name, 1f);
            var amplified = ReadPeak(name, sampleGain);
            Require(Mathf.Abs(amplified / original - sampleGain) < .0001f,
                "engine PCM gain is applied without clipping");
            enginePeak = Mathf.Max(enginePeak, amplified);
        }
        // Positive gains: two constant-power bands sum to at most sqrt(2).
        // Idle/driving also use a constant-power blend. Bound simultaneous voices
        // conservatively, including both horns and full-scale exhaust texture.
        var idleGain = (float)idleVolume.Invoke(null, new object[] { 0f });
        var engineGain = (float)engineVolume.Invoke(null, new object[] { 1f });
        Require(idleGain > .50f && idleGain <= 1f, "idle gain increases within source bounds");
        for (var step = 0; step <= 100; step++)
        {
            var gain = (float)engineVolume.Invoke(null, new object[] { step / 100f });
            Require(gain > 0f && gain <= 1f, "engine source gain is never Unity-clamped");
        }
        var bound = enginePeak * Mathf.Sqrt(idleGain * idleGain + 2f * engineGain * engineGain)
            + ReadPeak("HornLow", 1f) * (float)type.GetField("HornLowVolume", flags)!.GetRawConstantValue()
            + ReadPeak("HornHigh", 1f) * (float)type.GetField("HornHighVolume", flags)!.GetRawConstantValue()
            + (float)type.GetField("CrackleLoadVolume", flags)!.GetRawConstantValue();
        Require(bound < .9f, "simultaneous engine/idle/horn/crackle retain mix headroom");
        Debug.Log($"TRX audio mapping passed: 1001 RPM positions, bounded audible pitch, continuous three-band crossfade; " +
                  $"PCM gain={sampleGain:0.00}, idle gain={idleGain:0.00}, conservative combined peak={bound:0.000}.");
    }

    private static void ValidateAudioLifecycle(GameObject instance)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = typeof(DodgeRam1500TRXRuntime).Assembly.GetType("DodgeRam1500TRXAudioController", true)!;
        var component = instance.AddComponent(type);
        object? Get(string name) => type.GetField(name, fields)!.GetValue(component);
        void Set(string name, object? value) => type.GetField(name, fields)!.SetValue(component, value);
        var car = instance.GetComponents<MonoBehaviour>().Single(c => c.GetType().Name == "CarController");
        var physics = instance.GetComponents<MonoBehaviour>().Single(c => c.GetType().FullName == "NWH.VehiclePhysics2.VehicleController");
        var sound = physics.GetType().GetField("soundManager")!.GetValue(physics);
        var engine = sound.GetType().GetField("engineRunningComponent")!.GetValue(sound);
        var sourceField = engine.GetType().GetField("source", fields)!;
        var originalSource = sourceField.GetValue(engine);
        var engineHostField = sound.GetType().GetField("engineSourceGO")!;
        var otherHostField = sound.GetType().GetField("otherSourceGO")!;
        var originalEngineHost = engineHostField.GetValue(sound);
        var originalOtherHost = otherHostField.GetValue(sound);
        var nativeHost = new GameObject("TRX audio lifecycle template");
        nativeHost.transform.SetParent(instance.transform, false);
        var nativeSource = nativeHost.AddComponent<AudioSource>();
        var auxiliary = new GameObject("TRX auxiliary routed child");
        auxiliary.transform.SetParent(nativeHost.transform, false);
        var auxiliarySource = auxiliary.AddComponent<AudioSource>();
        try
        {
            sourceField.SetValue(engine, null); engineHostField.SetValue(sound, null); otherHostField.SetValue(sound, null);
            var initialize = type.GetMethod("Initialize")!;
            var contextType = initialize.GetParameters()[1].ParameterType;
            var context = contextType.GetConstructors(fields).Single().Invoke(new object?[] {
                Path.GetFullPath("Assets/Mods/Dodge_Ram_1500"), "Dodge_Ram_1500", null });
            initialize.Invoke(component, new[] { (object)car, context });
            Require((bool)type.GetMethod("TryConfigure", fields)!.Invoke(component, null), "cold parked audio loads successfully");
            Require((bool)Get("usesDirectFallbackMix")!, "cold parked fixture starts in direct fallback");
            var clips = (IList)Get("ownedClips")!;
            Require(clips.Count == 9 && ((AudioClip)clips[0]!).name == "EngineLow", "seven engine/idle plus two horn clips loaded");
            var voices = (AudioSource[])Get("layers")!;
            var idle = (AudioSource)Get("idleSource")!;
            var idleClip = idle.clip;
            var mixer = AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/Mods/Example-Vehicle/Audio/VehicleAudioMixer.mixer");
            var group = mixer.FindMatchingGroups("Engine").First();
            auxiliarySource.outputAudioMixerGroup = group;
            auxiliarySource.minDistance = 2.5f; auxiliarySource.maxDistance = 90f;
            sourceField.SetValue(engine, nativeSource); otherHostField.SetValue(sound, nativeHost);
            // Run the same one-shot handoff used by native onEnable: a late
            // routed auxiliary must beat a present but unrouted engine source.
            void Handoff()
            {
                var routine = (IEnumerator)type.GetMethod("InitializeAfterNativeStart", fields)!.Invoke(component, null);
                Require(routine.MoveNext() && routine.MoveNext() && !routine.MoveNext(), "bounded native audio readiness handoff");
            }
            Handoff();
            Require(!(bool)Get("usesDirectFallbackMix")! && idle.outputAudioMixerGroup == group &&
                voices.All(v => v.outputAudioMixerGroup == group) && Mathf.Approximately(idle.minDistance, 4f) && voices.All(v => Mathf.Approximately(v.minDistance, 4f)) &&
                idle.rolloffMode == AudioRolloffMode.Logarithmic &&
                Mathf.Approximately(((AudioSource)Get("hornSource")!).minDistance, 2.5f),
                "late auxiliary routing retains engine calibration and native horn profile");
            nativeSource.outputAudioMixerGroup = group; nativeSource.minDistance = 1f;
            Handoff();
            Require(ReferenceEquals(Get("native"), nativeSource) && ReferenceEquals(Get("sourceTemplate"), nativeSource) &&
                Mathf.Approximately(idle.minDistance, 4f) && Mathf.Approximately(nativeSource.minDistance, 1f),
                "late native source routing preserves calibrated owned distance without changing native source");
            Require(ReferenceEquals(voices, Get("layers")) && idle.clip == idleClip && clips.Count == 9,
                "rebind retains clips and sources without duplicate engine voices");
            Require((bool)Get("nativeEnableSubscribed")!, "native enable lifecycle subscribed");
            Debug.Log("TRX audio lifecycle passed: nine loaded WAVs, parked fallback, delayed native/auxiliary mixer routing, unchanged clip/source ownership.");
        }
        finally
        {
            sourceField.SetValue(engine, originalSource);
            engineHostField.SetValue(sound, originalEngineHost); otherHostField.SetValue(sound, originalOtherHost);
            var clips = (IList)Get("ownedClips")!;
            foreach (var clip in clips) UnityEngine.Object.DestroyImmediate((AudioClip)clip);
            clips.Clear();
            UnityEngine.Object.DestroyImmediate((AudioClip?)Get("crackleClip")); Set("crackleClip", null);
            var host = (GameObject?)Get("audioHost"); Set("audioHost", null);
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(component);
            UnityEngine.Object.DestroyImmediate(nativeHost);
        }
    }

    private static void ValidateLightStates(GameObject instance)
    {
        var type = typeof(DodgeRam1500TRXRuntime).Assembly.GetType("DodgeRam1500TRXLightingController", true)!;
        var component = instance.AddComponent(type);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        bool running = false, brake = false, left = false, right = false, reverse = false;
        void Set(string name, object value) => type.GetField(name, flags)!.SetValue(component, value);
        Set("npc", true);
        Set("lightsOn", (Func<bool>)(() => running));
        Set("braking", (Func<bool>)(() => brake));
        Set("left", (Func<bool>)(() => left)); Set("right", (Func<bool>)(() => right));
        Set("trafficReverse", (Func<bool>)(() => reverse)); Set("trafficPhase", (Func<int>)(() => 1));
        type.GetMethod("Bind", flags)!.Invoke(component, null);
        void Apply() => type.GetMethod("ApplyState", flags)!.Invoke(component, null);
        bool On(string name) => instance.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "DodgeRam1500TRX_Lamp_" + name).enabled;
        Apply(); Require(On("DrlLeft") && On("DrlRight") && On("DrlIndicatorLeft") && On("DrlIndicatorRight") && !On("HeadLeft") && On("TailLeft") && On("TailRight"), "daylight rear driving emitters in existing chambers");
        running = true; Apply(); Require(On("HeadLeft") && On("HeadRight") && On("TailLeft") && On("TailRight"), "night state");
        brake = true; Apply(); Require(On("BrakeLeft") && On("BrakeRight") && On("ThirdBrake") && !On("TailLeft") && !On("TailRight"), "brake state without competing tail overlays");
        left = true; reverse = true; Apply();
        Require(On("DrlLeft") && On("DrlRight") && !On("DrlIndicatorLeft") && On("DrlIndicatorRight") && On("BrakeLeft") && On("BrakeRight") && On("SignalLeft") && On("RearSignalLeft") && On("ReverseLeft") && On("ReverseRight"), "independent brake/turn/reverse and shared outer DRL handoff");
        left = false; right = true; Apply();
        Require(On("BrakeLeft") && On("BrakeRight") && !On("SignalLeft") && On("SignalRight") &&
            !On("RearSignalLeft") && On("RearSignalRight") && On("DrlIndicatorLeft") && !On("DrlIndicatorRight"),
            "independent right brake/turn/reverse and DRL handoff");
        left = true; Apply(); Require(On("SignalLeft") && On("SignalRight") && On("BrakeLeft") && On("BrakeRight") && On("ThirdBrake") && !On("DrlIndicatorLeft") && !On("DrlIndicatorRight"), "hazards preserve independent brakes and upper DRLs");
        Set("trafficPhase", (Func<int>)(() => 0)); Apply();
        Require(!On("RearSignalLeft") && !On("RearSignalRight") && !On("TailLeft") && !On("TailRight") && On("BrakeLeft") && On("BrakeRight"), "separate indicators dark in off phase while brakes remain on");
        brake = false; Apply(); Require(On("TailLeft") && On("TailRight") && !On("BrakeLeft") && !On("BrakeRight"), "running lights persist through indicator off phase");
        brake = left = right = reverse = false; Apply();
        Require(On("TailLeft") && On("TailRight") && !On("BrakeLeft") && !On("ReverseLeft"), "night running lamps restored after brake/turn/reverse");
        var player = instance.GetComponentsInChildren<MonoBehaviour>(true).Single(c => c != null && c.GetType().Name == "CarController");
        var controlled = player.GetType().GetField("controlledByPlayer", BindingFlags.Instance | BindingFlags.Public)!;
        var originalControl = controlled.GetValue(player);
        try
        {
            Set("npc", false); Set("vehicle", player); Set("gear", (Func<int>)(() => -1));
            controlled.SetValue(player, true); Apply();
            Require(On("TailLeft") && On("TailRight") && On("HeadLeft") && On("ReverseLeft") && On("ReverseRight"), "controlled player night/reverse lamps");
            controlled.SetValue(player, false); Apply();
            Require(instance.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("DodgeRam1500TRX_Lamp_")).All(r => !r.enabled), "uncontrolled player lamps disabled");
        }
        finally { controlled.SetValue(player, originalControl); }
        type.GetMethod("DisableLights", flags)!.Invoke(component, null);
        Require(instance.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.StartsWith("DodgeRam1500TRX_Lamp_")).All(r => !r.enabled), "disabled lamp cleanup");
        UnityEngine.Object.DestroyImmediate(component);
        Debug.Log("TRX lamp state matrix passed: daylight, night, brake, reverse, left/right, hazards/off phase, restored tails, controlled player/uncontrolled cleanup, disable.");
    }
    private static void ValidateDamageReplay(GameObject instance, MeshFilter[] selected)
    {
        var components = instance.GetComponentsInChildren<MonoBehaviour>(true);
        Require(!components.Any(c => c != null && c.GetType().Name == "VehicleDeformationController"), "no competing legacy damage callback");
        var car = components.Single(c => c != null && c.GetType().Name == "CarController");
        var handler = components.Single(c => c != null && c.GetType().Name == "DamageHandler");
        var instanceField = car.GetType().GetField("vehicleInstance")!;
        var data = Activator.CreateInstance(instanceField.FieldType, new object[] { "dodgeram1500trx-vehicle:vehicletype_dodgeram1500trx" })!;
        instanceField.SetValue(car, data);
        var records = (IList)data.GetType().GetField("deformations")!.GetValue(data);
        var recordType = records.GetType().GetGenericArguments()[0];
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetField("decelerationMagnitude")!.SetValue(record, 1500f);
        var pointsField = recordType.GetField("points")!;
        var pointType = pointsField.FieldType.GetElementType()!;
        var point = Activator.CreateInstance(pointType)!;
        void SetVector(string name, Vector3 value)
        {
            var field = pointType.GetField(name)!;
            var conversion = field.FieldType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "op_Implicit" && m.ReturnType == field.FieldType && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Vector3));
            field.SetValue(point, conversion.Invoke(null, new object[] { value }));
        }
        SetVector("point", new Vector3(.8f, 1.1f, 2.7f)); SetVector("normal", Vector3.back);
        pointType.GetField("deformationRandomness")!.SetValue(point, 1f);
        var points = Array.CreateInstance(pointType, 1); points.SetValue(point, 0); pointsField.SetValue(record, points);
        var sources = selected.ToDictionary(f => f, f => f.sharedMesh);
        var body = selected.Single(f => f.name == "DodgeRam1500TRXDamageBody");
        var original = selected.ToDictionary(f => f, f => f.sharedMesh.vertices);
        var rigid = instance.GetComponentsInChildren<MeshFilter>(true).Where(f => !selected.Contains(f) && f.sharedMesh != null).ToDictionary(f => f, f => f.sharedMesh.vertices);
        var type = typeof(DodgeRam1500TRXVisualDamageController);
        var beamHost = new GameObject("DodgeRam1500TRX_LeftHeadlightBeam");
        beamHost.transform.SetParent(instance.transform, false);
        beamHost.transform.localPosition = DodgeRam1500TRXBodyFit.RakePoint(new Vector3(-.72f, 1.00f, 2.72f));
        beamHost.AddComponent<Light>();
        var originalBeam = beamHost.transform.localPosition;
        var controller = instance.AddComponent<DodgeRam1500TRXVisualDamageController>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Require((float)type.GetField("MaximumRearDentDepth", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()! == .21675f &&
            (float)type.GetField("RearDepthPerExcessMps", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()! == .00663f,
            "rear deformation reduced another 15 percent");
        Require((float)type.GetField("MaximumFrontDentDepth", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()! == .2448f &&
            (float)type.GetField("FrontDepthPerExcessMps", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()! == .00816f,
            "front deformation reduced another 20 percent");
        try
        {
            type.GetMethod("Initialize", flags)!.Invoke(controller, new object?[] { car, handler, null, selected.ToList(), 5f });
            Require(selected.All(f => f.sharedMesh != sources[f]), "per-instance damage mesh ownership");
            var working = (IDictionary)type.GetField("currentVertices", flags)!.GetValue(controller)!;
            Require(working.Count == selected.Length && working.Values.Cast<Vector3[]>().All(v => v.Length > 0), "all damage vertex buffers cached before impact");
            foreach (var contact in new[] { new Vector3(0f, .70f, 2.8f), new Vector3(-.85f, .9f, 2.8f), new Vector3(.85f, .9f, 2.8f), new Vector3(0f, .7f, -2.85f), new Vector3(-.85f, 1.1f, -2.8f), new Vector3(.85f, 1.1f, -2.8f) })
            {
                type.GetMethod("RestoreMeshes", flags)!.Invoke(controller, null);
                records.Clear(); SetVector("point", contact); SetVector("normal", contact.z > 0f ? Vector3.back : Vector3.forward);
                records.Add(record);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                type.GetMethod("ReplaySavedDeformations", flags)!.Invoke(controller, null);
                timer.Stop(); Debug.Log($"TRX optimized crash replay: contact={contact}, processing={timer.Elapsed.TotalMilliseconds:0.00}ms.");
                var changed = selected.Where(f => f.sharedMesh.vertices.Where((v, i) => (v - original[f][i]).sqrMagnitude > 1e-10f).Any()).ToArray();
                Require(changed.Contains(body), "regional impact deforms painted shell");
                Require(changed.Any(f => f.name.StartsWith("DodgeRam1500TRXExterior_Coloured")), "regional impact deforms bumper plastic");
                Require(changed.Any(f => f.name.Contains("Exterior_Window_Geo")) && changed.Any(f => f.name.Contains("Light_Geo")), "regional impact deforms clear lenses and housings");
                var functions = contact.z > 0f ? new[] { "Head", "Drl", "Fog" } : new[] { "Tail", "Brake", "Reverse" };
                var sides = Mathf.Abs(contact.x) < .01f ? new[] { "Left", "Right" } : new[] { contact.x < 0f ? "Left" : "Right" };
                foreach (var function in functions)
                    foreach (var side in sides)
                        Require(changed.Any(f => f.name == "DodgeRam1500TRX_Lamp_" + function + side), "regional impact deforms emissive " + function + side);
                foreach (var overlay in changed.Where(f => f.name.StartsWith("DodgeRam1500TRX_Lamp_", StringComparison.Ordinal)))
                {
                    var source = overlay.transform.parent.GetComponent<MeshFilter>();
                    var sourceUV = sources[source].uv;
                    var lookup = Enumerable.Range(0, sourceUV.Length).GroupBy(i => sourceUV[i]).ToDictionary(g => g.Key, g => g.ToArray());
                    var uv = sources[overlay].uv; var overlayVertices = overlay.sharedMesh.vertices; var sourceVertices = source.sharedMesh.vertices;
                    for (var i = 0; i < uv.Length; i++)
                    {
                        var sourceIndex = lookup[uv[i]].OrderBy(j => (original[source][j] - original[overlay][i]).sqrMagnitude).First();
                        Require((original[source][sourceIndex] - original[overlay][i]).magnitude < .001f, "overlay retains exact source face and surface offset");
                        var difference = (overlayVertices[i] - original[overlay][i]) - (sourceVertices[sourceIndex] - original[source][sourceIndex]);
                        Require(overlay.transform.TransformVector(difference).magnitude < .0003f, "emissive surface follows corresponding housing vertex");
                    }
                }
                Require(rigid.All(pair => pair.Key.sharedMesh.vertices.SequenceEqual(pair.Value)), "impact leaves interior, cabin glass, wheels and brakes intact");
                Require(sources.All(pair => pair.Value.vertices.SequenceEqual(original[pair.Key])), "all shared prefab meshes intact");
                if (contact.z > 0f && contact.x <= 0f) Require((beamHost.transform.localPosition - originalBeam).sqrMagnitude > 1e-8f, "headlight beam follows front damage");
                var damaged = selected.ToDictionary(f => f, f => f.sharedMesh.vertices);
                var damagedBeam = beamHost.transform.localPosition;
                type.GetMethod("RestoreMeshes", flags)!.Invoke(controller, null);
                type.GetMethod("ReplaySavedDeformations", flags)!.Invoke(controller, null);
                Require(selected.All(f => f.sharedMesh.vertices.SequenceEqual(damaged[f])) && beamHost.transform.localPosition == damagedBeam, "deterministic complete damage replay");
                foreach (var filter in selected)
                {
                    var bounds = filter.sharedMesh.bounds;
                    Require(filter.sharedMesh.vertices.All(v => bounds.SqrDistance(v) < .000001f), "prepared conservative bounds contain every deformed vertex");
                }
                records.Clear(); type.GetMethod("CheckRepair", flags)!.Invoke(controller, null);
                Require(selected.All(f => f.sharedMesh.vertices.SequenceEqual(original[f])) && beamHost.transform.localPosition == originalBeam, "repair restores every exterior mesh and beam");
                Debug.Log($"TRX regional damage passed: contact={contact}, changed={changed.Length}, all relevant emitters/lenses/bumpers, immutable cabin, replay and full repair.");
            }
            ValidateNumericalImpacts(instance, controller, car, handler, records);
            Debug.Log("TRX damage matrix passed: 6 front/rear/left/right impacts, all exterior meshes owned, immutable shared meshes, complete repair and deterministic reload.");
        }
        finally
        {
            var meshes = (List<Mesh>)type.GetField("runtimeMeshes", flags)!.GetValue(controller);
            foreach (var filter in selected) filter.sharedMesh = sources[filter];
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
            meshes.Clear(); UnityEngine.Object.DestroyImmediate(controller); UnityEngine.Object.DestroyImmediate(beamHost);
        }
    }

    private const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AllStatics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static Type NativeType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null)!;

    private static void ValidateGarageTriggerOwnership(GameObject instance)
    {
        var root = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "BodyCollider");
        var boxes = root.GetComponentsInChildren<BoxCollider>(true);
        var primary = boxes.Single(c => c.CompareTag("Vehicle"));
        Require(boxes.Length == 4 && boxes.All(c => c.enabled && !c.isTrigger), "four physical body colliders retained");
        var supports = boxes.Where(c => c != primary).ToArray();
        Require(supports.All(c => c.CompareTag("VehicleAntiRocketCollider") && c.transform.parent == root &&
            c.transform.localPosition == Vector3.zero && c.transform.localRotation == Quaternion.identity && c.transform.localScale == Vector3.one), "support colliders use native trigger-exclusion tag without changing physical placement");
        var min = primary.center - primary.size * .5f; var max = primary.center + primary.size * .5f;
        foreach (var support in supports)
        {
            var lo = support.center - support.size * .5f; var hi = support.center + support.size * .5f;
            Require(lo.x >= min.x - .0001f && hi.x <= max.x + .0001f && lo.z >= min.z - .0001f && hi.z <= max.z + .0001f, "primary footprint covers entire vehicle until final exit");
        }
        var type = NativeType("Vehicles.Components.GarageDoor");
        var host = new GameObject("TRX native garage-trigger review"); host.SetActive(false);
        var door = host.AddComponent(type);
        var vehicles = (IList)type.GetField("vehiclesInside", AllMembers)!.GetValue(door);
        var layerField = NativeType("Helpers.LayerHelper").GetField("VehiclesLayerIndex", AllStatics)!;
        var oldLayer = layerField.GetValue(null); layerField.SetValue(null, primary.gameObject.layer);
        var enter = type.GetMethod("OnTriggerEnter", AllMembers)!;
        var exit = type.GetMethod("OnTriggerExit", AllMembers)!;
        var car = instance.GetComponent(NativeType("CarController"));
        // Native GetComponentInParent uses active ancestors. Editor callbacks
        // are invoked explicitly; the fixture is active only for this check.
        var parent = instance.transform.parent.gameObject;
        var oldParentActive = parent.activeSelf; var oldActive = instance.activeSelf;
        parent.SetActive(true); instance.SetActive(true);
        try
        {
            for (var pass = 0; pass < 3; pass++)
            {
                foreach (var box in boxes) enter.Invoke(door, new object[] { box });
                Require(vehicles.Count == 1 && vehicles.Contains(car), "native door tracks car once");
                foreach (var box in supports.Reverse()) exit.Invoke(door, new object[] { box });
                Require(vehicles.Count == 1 && vehicles.Contains(car), "early support exits cannot close native blocker");
                exit.Invoke(door, new object[] { primary });
                Require(vehicles.Count == 0, "native door releases car only on complete primary exit");
            }
            Debug.Log("TRX native garage matrix passed: four physical boxes, one full-footprint trigger owner, three compound entry/exit orders; early support exits retain native door occupancy.");
        }
        finally { instance.SetActive(oldActive); parent.SetActive(oldParentActive); layerField.SetValue(null, oldLayer); UnityEngine.Object.DestroyImmediate(host); }
    }

    private static void ValidateSleepBinding(GameObject instance)
    {
        Require(instance.GetComponent<DodgeRam1500TRXNativeActivities>() != null, "sleep binding component serialized in bundle");
        var car = instance.GetComponent(NativeType("CarController"));
        var vehicleType = NativeType("VehicleController");
        var savedField = vehicleType.GetField("vehicleInstance", AllMembers)!;
        var sleepField = vehicleType.GetField("sleepEnvironment", AllMembers)!;
        var oldSaved = savedField.GetValue(car); var oldSleep = sleepField.GetValue(car);
        var configField = (FieldInfo)typeof(DodgeRam1500TRXNativeActivities).GetField("Config", AllStatics)!.GetValue(null);
        var oldConfig = configField.GetValue(oldSleep);
        var config = ScriptableObject.CreateInstance(configField.FieldType);
        var kind = configField.FieldType.GetField("sleepEnvironmentType", AllMembers)!;
        kind.SetValue(config, Enum.Parse(kind.FieldType, "Car"));
        var native = UnityEngine.Object.Instantiate(instance, instance.transform.parent);
        var nativeCar = native.GetComponent(car.GetType());
        var nativeSleep = sleepField.GetValue(nativeCar)!;
        configField.SetValue(nativeSleep, config); sleepField.SetValue(nativeCar, nativeSleep);
        var activity = typeof(DodgeRam1500TRXNativeActivities);
        var template = activity.GetField("template", AllStatics)!; var oldTemplate = template.GetValue(null);
        var ensure = activity.GetMethod("EnsureSleep", AllStatics)!;
        try
        {
            template.SetValue(null, nativeCar); savedField.SetValue(car, null);
            var empty = sleepField.GetValue(car)!; configField.SetValue(empty, null); sleepField.SetValue(car, empty);
            Require((bool)ensure.Invoke(null, new object?[] { car, null })!, "sleep binds without saved VehicleInstance");
            Require(ReferenceEquals(configField.GetValue(sleepField.GetValue(car)), config), "native Car config assigned");
            Require((bool)ensure.Invoke(null, new object?[] { car, null })! && ReferenceEquals(configField.GetValue(sleepField.GetValue(car)), config), "sleep binding preserves valid existing config on repeated entry");
            Require(sleepField.FieldType.GetProperty("SleepEnvironmentType")!.GetValue(sleepField.GetValue(car))!.ToString() == "Car", "native sleep availability resolves Car");
            Debug.Log("TRX native sleep matrix passed: bundled early lifecycle component, config assigned before saved instance exists, native Car environment and idempotent preservation.");
        }
        finally
        {
            configField.SetValue(oldSleep, oldConfig);
            savedField.SetValue(car, oldSaved); sleepField.SetValue(car, oldSleep); template.SetValue(null, oldTemplate);
            UnityEngine.Object.DestroyImmediate(native); UnityEngine.Object.DestroyImmediate(config);
        }
    }

    private static Collision FixtureCollision(Collider own, Collider other, Vector3 point, Vector3 normal, float speed, float impulse)
    {
        // Unity 2022.3 Collision supports legacy managed contacts. Populate its
        // header/pair as well so native Enqueue reads the real impulse/speed.
        var collision = new Collision();
        var headerField = typeof(Collision).GetField("m_Header", AllMembers)!;
        var pairField = typeof(Collision).GetField("m_Pair", AllMembers)!;
        var header = Activator.CreateInstance(headerField.FieldType)!;
        headerField.FieldType.GetField("m_RelativeVelocity", AllMembers)!.SetValue(header, Vector3.forward * speed);
        var pair = Activator.CreateInstance(pairField.FieldType)!;
        pairField.FieldType.GetField("m_ColliderID", AllMembers)!.SetValue(pair, own.GetInstanceID());
        pairField.FieldType.GetField("m_OtherColliderID", AllMembers)!.SetValue(pair, other.GetInstanceID());
        pairField.FieldType.GetField("m_ImpulseSum", AllMembers)!.SetValue(pair, normal * impulse);
        pairField.FieldType.GetField("m_NbPoints", AllMembers)!.SetValue(pair, 1u);
        object contact = new ContactPoint();
        typeof(ContactPoint).GetField("m_Point", AllMembers)!.SetValue(contact, point);
        typeof(ContactPoint).GetField("m_Normal", AllMembers)!.SetValue(contact, normal);
        typeof(ContactPoint).GetField("m_ThisColliderInstanceID", AllMembers)!.SetValue(contact, own.GetInstanceID());
        typeof(ContactPoint).GetField("m_OtherColliderInstanceID", AllMembers)!.SetValue(contact, other.GetInstanceID());
        headerField.SetValue(collision, header); pairField.SetValue(collision, pair);
        typeof(Collision).GetField("m_LegacyContacts", AllMembers)!.SetValue(collision, new[] { (ContactPoint)contact });
        Require(collision.collider == other && collision.contactCount == 1 && Mathf.Abs(collision.impulse.magnitude - impulse) < .01f, "managed collision faithfully exposes native contact and impulse");
        return collision;
    }

    private static void ValidateNumericalImpacts(GameObject instance, Component owner, Component car, Component handler, IList records)
    {
        var native = handler.GetType(); var ownerType = owner.GetType();
        var body = instance.GetComponent<Rigidbody>();
        var rbField = native.GetField("_rigidbody", AllMembers)!; var oldRb = rbField.GetValue(handler);
        rbField.SetValue(handler, body);
        var damage = native.GetProperty("Damage")!;
        var setDamage = native.GetMethod("SetDamage", AllMembers)!;
        var oldDamage = (float)damage.GetValue(handler)!;
        var intensity = native.GetField("damageIntensity", AllMembers)!; var oldIntensity = intensity.GetValue(handler);
        var runtimeIntensity = (float)typeof(DodgeRam1500TRXRuntime).GetField("DamageIntensity", AllStatics)!.GetRawConstantValue()!;
        intensity.SetValue(handler, runtimeIntensity);
        var layers = new[] { "GroundLayerIndex", "GroundUnplacableLayerIndex", "RoadsLayerIndex" }.Select(n => native.GetField(n, AllStatics)!).ToArray();
        var oldLayers = layers.Select(f => f.GetValue(null)).ToArray();
        for (var i = 0; i < layers.Length; i++) layers[i].SetValue(null, 15 + i);
        var saved = car.GetType().GetField("vehicleInstance", AllMembers)!.GetValue(car)!;
        var savedDamage = saved.GetType().GetField("damage", AllMembers)!;
        var impact = ownerType.GetMethod("HandleImpact", AllMembers)!;
        var next = ownerType.GetField("nextCollisionTime", AllMembers)!;
        var enabled = native.GetProperty("enabled", AllMembers)!;
        var host = new GameObject("Streetlight review"); host.SetActive(false); host.layer = 26;
        var other = host.AddComponent<BoxCollider>();
        var own = instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == "BodyCollider").GetComponent<BoxCollider>();
        Collision Hit(float impulse, float speed = 15.473f, Vector3? normal = null) => FixtureCollision(own, other, instance.transform.TransformPoint(new Vector3(0f, .7f, 2.8f)), normal ?? Vector3.back, speed, impulse);
        float Current() => (float)damage.GetValue(handler)!;
        float Expected(float impulse) => impulse / (Time.fixedDeltaTime * body.mass * 10f) * runtimeIntensity * .005f;
        void ResetCooldown() => next.SetValue(owner, 0f);
        try
        {
            records.Clear(); setDamage.Invoke(handler, new object[] { 0f }); savedDamage.SetValue(saved, 0f); ResetCooldown();
            Require(!(bool)enabled.GetValue(handler)!, "only mod owns numerical collision receiver");
            native.GetMethod("OnCollisionEnter", AllMembers)!.Invoke(handler, new object[] { Hit(0f) });
            impact.Invoke(owner, new object[] { Hit(0f) });
            Require(Current() == 0f && records.Count == 0 && (float)next.GetValue(owner)! == 0f, "zero-impulse first callback consumes neither damage nor cooldown");
            impact.Invoke(owner, new object[] { Hit(39317.55f) });
            Require(Mathf.Abs(Current() - Expected(39317.55f)) < .00001f && (float)savedDamage.GetValue(saved)! == Current() && records.Count == 1, "recorded later frontal impulse applies exact native and saved damage once");
            native.GetMethod("OnCollisionEnter", AllMembers)!.Invoke(handler, new object[] { Hit(39317.55f) });
            impact.Invoke(owner, new object[] { Hit(39317.55f) });
            Require(records.Count == 1 && Mathf.Abs(Current() - Expected(39317.55f)) < .00001f, "compound duplicate callback cannot double damage");
            ResetCooldown(); host.name = "Tree Birch (21)"; host.layer = 15;
            impact.Invoke(owner, new object[] { Hit(23842.87f, 54.378f / 3.6f) });
            var sum = Expected(39317.55f) + Expected(23842.87f);
            Require(Mathf.Abs(Current() - sum) < .00001f && records.Count == 2, "recorded excluded-layer tree frontal impact receives native damage");
            ResetCooldown(); host.name = "GroundPlane";
            impact.Invoke(owner, new object[] { Hit(148995f, 15f, Vector3.down) });
            Require(Mathf.Abs(Current() - sum) < .00001f && records.Count == 2, "ground and vertical surface contacts remain excluded");
            host.name = "Tree Birch (21)";
            impact.Invoke(owner, new object[] { Hit(23842.87f, 15f, Vector3.down) });
            Require(records.Count == 2, "tree ground-like vertical contacts remain excluded");
            ResetCooldown(); host.name = "Streetlight review"; host.layer = 26;
            impact.Invoke(owner, new object[] { Hit(10388f, 14.693f / 3.6f) });
            Require(records.Count == 2, "native 18 km/h minimum remains respected");
            ownerType.GetMethod("ReleaseNumericalOwnership", AllMembers)!.Invoke(owner, null);
            Require((bool)enabled.GetValue(handler)!, "unload restores original native damage receiver");
            Debug.Log("TRX native numerical damage matrix passed: CSV zero/later frontal impulse, exact native equation and save sync, duplicate rejection, excluded-layer tree, ground/vertical/low-speed exclusions, ownership release.");
        }
        finally
        {
            records.Clear(); setDamage.Invoke(handler, new object[] { oldDamage }); rbField.SetValue(handler, oldRb); intensity.SetValue(handler, oldIntensity);
            for (var i = 0; i < layers.Length; i++) layers[i].SetValue(null, oldLayers[i]);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    private static void Require(bool condition, string check)
    {
        if (!condition) throw new InvalidOperationException("TRX review failed: " + check);
    }
}
