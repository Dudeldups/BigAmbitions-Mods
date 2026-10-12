#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BusinessLayoutSets;
using GleyTrafficSystem;
using GleyUrbanAssets;
using Helpers;
using UnityEditor;
using UnityEngine;

public static class McLarenSennaTrafficSetup
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const string TemplateKey = "Prefabs/Vehicles/AnselmoAF90.prefab";
    private const string OwnedKey = "Prefabs/Vehicles/mclarensenna.prefab";

    public static void VerifyTrafficLifecycle()
    {
        var support = typeof(McLarenSennaRuntime).Assembly.GetType("McLarenSennaPrivateDriverSupport", true)!;
        var cache = (IDictionary)typeof(PrefabHelper).GetField("PrefabCache", Statics)!.GetValue(null);
        var previousTemplate = cache[TemplateKey];
        var previousOwned = cache[OwnedKey];
        var previousTraffic = TrafficComponent.Instance;
        var managerField = typeof(TrafficManager).GetField("instance", Statics)!;
        var previousManager = managerField.GetValue(null);
        var urbanField = typeof(UrbanManager).GetField("urbanManagerInstance", Statics)!;
        var previousUrban = urbanField.GetValue(null);
        var wasLoading = BusinessLayoutSetHelper.loadingLayouts;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mods/McLaren_Senna/McLarenSenna.prefab");
        var root = new GameObject("SennaTrafficLifecycleFixture");
        root.SetActive(false);
        var template = CreateNativeTemplate(root.transform);
        var sentinel = new GameObject("SennaTrafficForeignCacheOwner");
        sentinel.SetActive(false);
        var pool = ScriptableObject.CreateInstance<VehiclePool>();
        GameObject? owned = null;
        TrafficVehicles? vehicles = null;
        TrafficManager? manager = null;
        var release = support.GetMethod("RemoveVehicle", Statics)!;
        void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
        try
        {
            Require(support.GetProperty("TrafficPrefab", Statics)!.GetValue(null) == null, "Traffic fixture requires a fresh process.");
            cache[TemplateKey] = template;
            cache[OwnedKey] = sentinel;
            var stock = new CarType { name = "FixtureStock", vehiclePrefab = template, nrOfVehicles = 1, canBeAiDriven = true };
            var foreign = new CarType { name = "FixtureOtherMod", vehiclePrefab = template, nrOfVehicles = 1, canBeAiDriven = true };
            pool.trafficCars = new[] { stock, foreign };
            var runtime = root.AddComponent<McLarenSennaRuntime>();
            typeof(McLarenSennaRuntime).GetField("playerVehiclePrefab", Members)!.SetValue(runtime, source);
            var lifecycle = typeof(McLarenSennaRuntime).GetMethod("InitializeForLifecycle", Members)!;
            var ready = typeof(McLarenSennaRuntime).GetField("privateDriverPoolReady", Members)!;
            BusinessLayoutSetHelper.loadingLayouts = true;
            TrafficComponent.Instance = null;
            lifecycle.Invoke(runtime, new object[] { "fixture-before-city-awake" });
            Require(!(bool)ready.GetValue(runtime), "Missing traffic component should defer preparation.");

            var traffic = root.AddComponent<TrafficComponent>();
            traffic.vehiclePool = pool;
            TrafficComponent.Instance = traffic;
            manager = root.AddComponent<TrafficManager>();
            managerField.SetValue(null, manager);
            urbanField.SetValue(null, manager);
            lifecycle.Invoke(runtime, new object[] { "fixture-scene-loaded-layouts-still-loading" });
            if (!(bool)ready.GetValue(runtime))
                support.GetMethod("PrepareTrafficPool", Statics)!.Invoke(null, new object[] { source, "fixture-direct-failure-diagnostic" });
            Require((bool)ready.GetValue(runtime), "Dealer layout loading suppressed the pre-init traffic window.");
            owned = (GameObject)support.GetProperty("TrafficPrefab", Statics)!.GetValue(null);
            var ownType = pool.trafficCars.Single(t => t.name == "McLarenSennaPrivateDriver");
            Require(pool.trafficCars.Length == 3 && ReferenceEquals(pool.trafficCars[0], stock) && ReferenceEquals(pool.trafficCars[1], foreign), "Stock/other-mod pool entries changed.");
            Require(ownType.vehiclePrefab == owned && ownType.nrOfVehicles == 2 && ownType.canBeAiDriven && !ownType.canBeRandomlyParked && !owned.activeSelf, "Invalid Senna AI pool entry.");
            lifecycle.Invoke(runtime, new object[] { "fixture-repeat-scene-boundary" });
            Require(pool.trafficCars.Length == 3, "Repeated lifecycle duplicated traffic entries.");
            var prepare = support.GetMethod("PrepareTrafficPool", Statics)!;
            Require((bool)prepare.Invoke(null, new object[] { source, "fixture-repeat-pool" }), "Repeated pool preparation failed.");
            Require(pool.trafficCars.Length == 3 && ReferenceEquals(cache[OwnedKey], owned), "Repeated preparation changed pool/cache identity.");

            // Execute Gley's actual allocator: this is the native list queried
            // by TrafficManager.LoadVehicle when the Private Driver is called.
            vehicles = root.AddComponent<TrafficVehicles>();
            vehicles.Initialize(pool, pool.GetNumberOfVehicles(), 0, 0, 0, 1f, Vector2.one);
            var ownInstances = vehicles.GetVehicleList().Where(v => v.prefab == owned).ToArray();
            Require(ownInstances.Length == 2 && ownInstances.All(v => !v.gameObject.activeInHierarchy), "Native allocator did not create both chauffeur/NPC instances.");
            foreach (var vehicle in ownInstances)
            {
                var collider = vehicle.GetCollider();
                Require(collider != null && collider.enabled && collider.transform.IsChildOf(vehicle.transform) && collider.name == "McLarenSenna NpcBodyCollider", "Native collision cache retained the disabled template body.");
                foreach (var name in new[] { "McLarenVisual", "McLarenDamageBody", "McLarenWheelFrontLeft", "McLarenWheelFrontRight", "McLarenWheelRearLeft", "McLarenWheelRearRight", "McLarenFixedCaliperFrontLeft", "McLarenFixedCaliperFrontRight", "McLarenFixedCaliperRearLeft", "McLarenFixedCaliperRearRight" })
                    Require(vehicle.GetComponentsInChildren<Transform>(true).Count(t => t.name == name) == 1, "Missing/duplicate traffic visual: " + name);
            }
            McLarenSennaTrafficVisualSetup.VerifyWheelsAndPaint(ownInstances);
            typeof(TrafficManager).GetField("initialized", Members)!.SetValue(manager, true);
            Require((bool)prepare.Invoke(null, new object[] { source, "fixture-existing-live-entry" }), "Existing owned entry should remain ready after initialization.");
            pool.trafficCars = new[] { stock, foreign };
            Require(!(bool)prepare.Invoke(null, new object[] { source, "fixture-missing-live-entry" }) && pool.trafficCars.Length == 2, "Preparation appended to an already initialized live pool.");
            ready.SetValue(runtime, false);
            typeof(McLarenSennaRuntime).GetField("privateDriverRegistrationAllowed", Members)!.SetValue(runtime, true);
            Require(!(bool)typeof(McLarenSennaRuntime).GetMethod("EnsurePrivateDriverSupport", Members)!.Invoke(runtime, new object[] { "fixture-no-pool-no-contract" }), "Unavailable pool advertised private-driver eligibility.");
            release.Invoke(null, new object[] { "" });
            Require(pool.trafficCars.Length == 2 && ReferenceEquals(pool.trafficCars[0], stock) && ReferenceEquals(pool.trafficCars[1], foreign) && ReferenceEquals(cache[OwnedKey], sentinel), "Unload damaged foreign entries or failed to restore cache owner.");
            Debug.Log("Senna traffic lifecycle verified: pending layouts cannot block city pool setup, two actual native Gley instances available for chauffeur/NPC lookup, complete wheels/calipers/body, owned active collider cache, repeat setup, no late append/contracts and foreign pool/cache preserved.");
        }
        finally
        {
            if (manager != null) typeof(TrafficManager).GetField("initialized", Members)!.SetValue(manager, false);
            release.Invoke(null, new object[] { "" });
            if (vehicles != null)
            {
                var holder = typeof(TrafficVehicles).GetField("trafficHolder", Members)!.GetValue(vehicles) as Transform;
                if (holder != null) UnityEngine.Object.DestroyImmediate(holder.gameObject);
            }
            if (owned != null) UnityEngine.Object.DestroyImmediate(owned);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(sentinel);
            UnityEngine.Object.DestroyImmediate(pool);
            if (previousTemplate == null) cache.Remove(TemplateKey); else cache[TemplateKey] = previousTemplate;
            if (previousOwned == null) cache.Remove(OwnedKey); else cache[OwnedKey] = previousOwned;
            TrafficComponent.Instance = previousTraffic;
            managerField.SetValue(null, previousManager);
            urbanField.SetValue(null, previousUrban);
            BusinessLayoutSetHelper.loadingLayouts = wasLoading;
        }
    }

    private static GameObject CreateNativeTemplate(Transform parent)
    {
        var root = new GameObject("SennaFixtureNativeAiTemplate");
        root.SetActive(false); root.transform.SetParent(parent, false);
        var body = root.AddComponent<BoxCollider>(); body.size = new Vector3(1.8f, 1f, 4.4f);
        var native = root.AddComponent<McLarenSennaTrafficFixtureVehicle>();
        native.rb = root.GetComponent<Rigidbody>();
        if (native.rb == null) native.rb = root.AddComponent<Rigidbody>();
        native.rb.mass = 1309f;
        native.carHolder = root.transform;
        native.visibilityScript = root.AddComponent<VisibilityScript>();
        native.frontTrigger = new GameObject("FrontTrigger").transform; native.frontTrigger.SetParent(root.transform, false);
        var trigger = new GameObject("TriggerCollider"); trigger.transform.SetParent(native.frontTrigger, false); trigger.AddComponent<BoxCollider>().isTrigger = true;
        native.allWheels = new[] { "FL", "FR", "BL", "BR" }.Select((name, index) =>
        {
            var wheel = new GameObject(name).transform; wheel.SetParent(root.transform, false); wheel.localPosition = new Vector3(index % 2 == 0 ? -.82f : .82f, .35f, index < 2 ? 1.15f : -1.51f);
            return new Wheel { wheelTransform = wheel, wheelGraphics = wheel, wheelPosition = index < 2 ? Wheel.WheelPosition.Front : Wheel.WheelPosition.Back, wheelRadius = .35f, maxSuspension = .06f };
        }).ToArray();
        typeof(VehicleComponent).GetField("allColliders", Members)!.SetValue(native, new Collider[] { body });
        return root;
    }
}

// Suppress only editor authoring validation while references are populated.
// Initialization, allocation, deactivation and collider access use native Gley.
public sealed class McLarenSennaTrafficFixtureVehicle : VehicleComponent
{
    private void OnValidate() { }
}
