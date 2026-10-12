#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Data.VehicleColors;
using Helpers;
using GleyTrafficSystem;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

public static class McLarenSennaTrafficVisualSetup
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void VerifyWheelsAndPaint(VehicleComponent[] vehicles)
    {
        vehicles[1].transform.SetPositionAndRotation(new Vector3(4f, 0f, 3f), Quaternion.Euler(0f, 37f, 0f));
        foreach (var vehicle in vehicles) VerifyWheels(vehicle);
        VerifyPaint(vehicles);
    }

    private static void VerifyWheels(VehicleComponent vehicle)
    {
        var rig = typeof(McLarenSennaRuntime).Assembly.GetType("McLarenSennaTrafficWheels", true)!;
        var hubs = vehicle.allWheels.Select(w => w.wheelGraphics).ToArray();
        if (!(bool)rig.GetMethod("Configure", Statics)!.Invoke(null, new object?[] { vehicle.gameObject, null }) ||
            !hubs.SequenceEqual(vehicle.allWheels.Select(w => w.wheelGraphics)))
            throw new InvalidOperationException("Repeated NPC wheel setup changed native hub ownership.");
        foreach (var wheel in vehicle.allWheels)
        {
            var expected = wheel.wheelPosition == Wheel.WheelPosition.Front ? .32705f : .3485f;
            if (Mathf.Abs(wheel.wheelRadius - expected) > .002f || !wheel.wheelGraphics.name.StartsWith("McLarenTrafficHub_") ||
                Mathf.Abs(wheel.raycastLength - wheel.wheelRadius - wheel.maxSuspension) > .0001f)
                throw new InvalidOperationException("NPC radius/raycast/graphics differs from measured Senna geometry.");
        }
        var origins = new NativeArray<float3>(vehicle.allWheels.Select(w => (float3)w.wheelTransform.position).ToArray(), Allocator.TempJob);
        var down = new NativeArray<float3>(new[] { new float3(0f, -1f, 0f) }, Allocator.TempJob);
        var rotation = new NativeArray<float>(1, Allocator.TempJob);
        var steering = new NativeArray<float>(1, Allocator.TempJob);
        var radius = new NativeArray<float>(vehicle.allWheels.Select(w => w.wheelRadius).ToArray(), Allocator.TempJob);
        var hitDistance = new NativeArray<float>(vehicle.allWheels.Select(w => w.wheelTransform.position.y).ToArray(), Allocator.TempJob);
        var suspension = new NativeArray<float>(vehicle.allWheels.Select(w => w.maxSuspension).ToArray(), Allocator.TempJob);
        var carIndex = new NativeArray<int>(4, Allocator.TempJob);
        var canSteer = new NativeArray<bool>(vehicle.allWheels.Select(w => w.wheelPosition == Wheel.WheelPosition.Front).ToArray(), Allocator.TempJob);
        var transforms = new TransformAccessArray(hubs);
        try
        {
            var appearanceType = typeof(McLarenSennaRuntime).Assembly.GetType("McLarenSennaPrivateDriverAppearance", true)!;
            var appearance = vehicle.GetComponent(appearanceType);
            for (var pass = 0; pass < 9; pass++)
            {
                rotation[0] = pass * 45f; steering[0] = pass % 2 == 0 ? 12f : -12f;
                var job = new UpdateWheelJob { wheelsOrigin = origins, downDirection = down, wheelRotation = rotation,
                    turnAngle = steering, wheelRadius = radius, raycastDistance = hitDistance, maxSuspension = suspension,
                    carIndex = carIndex, canSteer = canSteer, nrOfCars = 1 };
                job.Schedule(transforms).Complete();
                var centers = hubs.Select(h => h.position).ToArray();
                // Exercise both possible helper creation/activation orders.
                appearanceType.GetMethod("Awake", Members)!.Invoke(appearance, null);
                appearanceType.GetMethod("LateUpdate", Members)!.Invoke(appearance, null);
                for (var index = 0; index < hubs.Length; index++)
                {
                    var wheel = vehicle.allWheels[index];
                    // Renderer.bounds rotates a bounding box, which overstates
                    // a circular tire's extent at 45 degrees. Measure vertices.
                    var lowest = hubs[index].GetComponentsInChildren<MeshFilter>(true)
                        .SelectMany(f => f.sharedMesh.vertices.Select(v => f.transform.TransformPoint(v).y)).Min();
                    if (Vector3.Distance(centers[index], hubs[index].position) > .00001f || Mathf.Abs(lowest) > .008f ||
                        Mathf.Abs(hubs[index].position.y - wheel.wheelRadius) > .0001f)
                        throw new InvalidOperationException($"Native NPC wheel sinks/drifts after helper activation: {hubs[index].name}, lowest={lowest:0.00000}.");
                    var corner = hubs[index].name.Substring("McLarenTrafficHub_".Length);
                    var caliper = vehicle.GetComponentsInChildren<Transform>(true).Single(t => t.name == "McLarenFixedCaliper" + corner);
                    var visual = hubs[index].GetComponentsInChildren<Transform>(true).Single(t => t.name == "McLarenWheel" + corner);
                    var yaw = wheel.wheelPosition == Wheel.WheelPosition.Front ? steering[0] : 0f;
                    if (Vector3.Distance(caliper.position, visual.position) > .0001f ||
                        Quaternion.Angle(caliper.rotation, vehicle.transform.rotation * Quaternion.Euler(0f, yaw, 0f)) > .02f)
                        throw new InvalidOperationException("NPC caliper inherited wheel roll or lost steering/position: " + corner);
                }
            }
            Debug.Log("Senna NPC wheels verified with native UpdateWheelJob: measured front/rear radii, centered native hubs, nine roll/steer poses touch road, repeated helper Awake cannot change wheel height; calipers follow without wheel spin.");
        }
        finally
        {
            transforms.Dispose(); origins.Dispose(); down.Dispose(); rotation.Dispose(); steering.Dispose(); radius.Dispose();
            hitDistance.Dispose(); suspension.Dispose(); carIndex.Dispose(); canSteer.Dispose();
        }
    }

    private static void VerifyPaint(VehicleComponent[] vehicles)
    {
        var host = new GameObject("SennaNpcPaletteFixture");
        var referencesType = typeof(VehicleController).Assembly.GetType("GlobalReferences", true)!;
        var lifecycle = referencesType.BaseType!.Assembly.GetType("Extensions.SingletonLifecycle", true)!;
        var previous = lifecycle.GetMethod("GetSingleton", Statics)!.MakeGenericMethod(referencesType).Invoke(null, null);
        var wasMissing = (bool)lifecycle.GetMethod("IsTypeMissing", Statics)!.Invoke(null, new object[] { referencesType });
        var randomState = UnityEngine.Random.state;
        Component? references = null;
        var colors = new[] { new Color32(18, 230, 55, 255), new Color32(190, 25, 230, 255) }.Select((tint, index) =>
        { var color = ScriptableObject.CreateInstance<VehicleColor>(); color.name = "SennaNpcFixture" + index; color.tint = tint; color.randomWeight = 1f; return color; }).ToArray();
        try
        {
            references = host.AddComponent(referencesType);
            referencesType.GetField("vehicleColors", Members)!.SetValue(references, colors);
            lifecycle.GetMethod("RegisterSingleton", Statics)!.Invoke(null, new object[] { referencesType, references });
            lifecycle.GetMethod("UnmarkMissingType", Statics)!.Invoke(null, new object[] { referencesType });
            var ambient = typeof(McLarenSennaRuntime).Assembly.GetType("McLarenSennaAmbientTrafficAppearance", true)!;
            var paint = typeof(McLarenSennaPaintController);
            var apply = ambient.GetMethod("ApplyAmbientColor", Members)!;
            UnityEngine.Random.InitState(7381);
            var selected = new System.Collections.Generic.HashSet<string>();
            for (var pass = 0; pass < 12; pass++)
            {
                var root = vehicles[pass % vehicles.Length].gameObject;
                // Original fixture template has no live native color: old code
                // left these instances at the imported gray material forever.
                var routine = (IEnumerator)ambient.GetMethod("ApplyNativeTrafficColor", Members)!.Invoke(root.GetComponent(ambient), null);
                if (!routine.MoveNext() || routine.MoveNext()) throw new InvalidOperationException("Ambient paint lifecycle is not a single bounded spawn pass.");
                var controller = root.GetComponent<McLarenSennaPaintController>();
                var colorName = (string)paint.GetField("appliedColorName", Members)!.GetValue(controller);
                selected.Add(colorName);
                CheckPaint(root, colors.Single(c => c.name == colorName).tint);
            }
            if (selected.Count != 2) throw new InvalidOperationException("Seeded native NPC palette did not produce both distinct colors.");
            for (var index = 0; index < vehicles.Length; index++)
            {
                var root = vehicles[index].gameObject;
                if (!(bool)apply.Invoke(root.GetComponent(ambient), new object[] { colors[index] })) throw new InvalidOperationException("NPC color application failed.");
                CheckPaint(root, colors[index].tint);
            }
            CheckPaint(vehicles[0].gameObject, colors[0].tint);
            var chauffeurRoot = vehicles[1].gameObject;
            var chauffeur = chauffeurRoot.AddComponent<PrivateDriverVehicle>();
            chauffeur.vehicleInstance = new VehicleInstance("mclarensenna-vehicle:vehicletype_mclarensenna") { vehicleColorName = colors[1].name };
            var appearance = typeof(McLarenSennaRuntime).Assembly.GetType("McLarenSennaPrivateDriverAppearance", true)!;
            var owner = chauffeurRoot.GetComponent(appearance);
            appearance.GetField("privateDriver", Members)!.SetValue(owner, chauffeur);
            if (!(bool)appearance.GetMethod("RestoreSavedVehicleColor", Members)!.Invoke(owner, new object[] { true }) ||
                (bool)apply.Invoke(chauffeurRoot.GetComponent(ambient), new object[] { colors[0] }))
                throw new InvalidOperationException("Ambient paint overrode a saved chauffeur color.");
            CheckPaint(chauffeurRoot, colors[1].tint);
            Debug.Log("Senna NPC paint verified: absent initial native color, 12 pooled spawn passes from native weighted palette yield two colors, per-instance tint isolation, saved chauffeur color remains authoritative.");
        }
        finally
        {
            UnityEngine.Random.state = randomState;
            if (references != null) lifecycle.GetMethod("UnregisterSingleton", Statics)!.Invoke(null, new object[] { referencesType, references });
            if (previous != null) lifecycle.GetMethod("RegisterSingleton", Statics)!.Invoke(null, new[] { (object)referencesType, previous });
            if (wasMissing) lifecycle.GetMethod("MarkMissingType", Statics)!.Invoke(null, new object[] { referencesType });
            UnityEngine.Object.DestroyImmediate(host);
            foreach (var color in colors) UnityEngine.Object.DestroyImmediate(color);
        }
    }

    private static void CheckPaint(GameObject root, Color32 tint)
    {
        var block = new MaterialPropertyBlock(); var count = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            for (var index = 0; index < renderer.sharedMaterials.Length; index++)
                if (renderer.sharedMaterials[index] != null && renderer.sharedMaterials[index].name.Contains("Paint"))
                {
                    renderer.GetPropertyBlock(block, index);
                    if (Vector4.Distance(block.GetColor("_BaseColor"), (Color)tint) > .001f) throw new InvalidOperationException("NPC body paint mismatch: " + renderer.name);
                    count++;
                }
        if (count == 0) throw new InvalidOperationException("NPC fixture found no paint slots.");
    }
}
