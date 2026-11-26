#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using GleyTrafficSystem;
using Helpers;
using UnityEditor;
using UnityEngine;
using Vehicles.VehicleTypes;

/// <summary>Regression coverage for registering traffic before Gley allocates its live vehicle list.</summary>
public static class LamborghiniAventadorTrafficInitializationRegression
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const string OwnedCacheKey = "Prefabs/Vehicles/lamborghiniaventador.prefab";
    private const string OwnedCarTypeName = "LamborghiniAventadorPrivateDriver";

    [MenuItem("Big Ambitions Mods/Lamborghini Aventador/Verify Pre-Allocation Traffic Registration")]
    public static void VerifyPreAllocationRegistration()
    {
        var assembly = typeof(LamborghiniAventadorMod).Assembly;
        var support = assembly.GetType("LamborghiniAventadorTrafficSupport", true)!;
        var patch = assembly.GetType("LamborghiniAventadorTrafficInitializationPatch", true)!;
        var currentPrefab = support.GetProperty("PlayerPrefab", Statics)!;
        var trafficPrefab = support.GetField("customAiPrefab", Statics)!;
        if (currentPrefab.GetValue(null) != null || trafficPrefab.GetValue(null) != null)
            throw new InvalidOperationException("Aventador traffic regression requires a fresh mod state.");

        var targetMethod = patch.GetField("InitializeMethod", Statics)!.GetValue(null) as MethodInfo;
        if (targetMethod == null)
            throw new InvalidOperationException("Aventador traffic regression could not resolve Gley's exact initialization method.");
        var parameters = targetMethod.GetParameters();
        if (targetMethod.DeclaringType != typeof(TrafficManager) || parameters.Length != 11 ||
            parameters[0].ParameterType != typeof(Transform[]) ||
            parameters[2].ParameterType != typeof(VehiclePool) || targetMethod.ReturnType != typeof(void))
            throw new InvalidOperationException("Aventador hook no longer targets the VehiclePool-taking Gley initialization overload.");

        var prefix = patch.GetMethod("BeforeTrafficManagerInitialize", Statics)!;
        var prefixParameters = prefix.GetParameters();
        if (!prefix.IsStatic || prefix.ReturnType != typeof(void) || prefixParameters.Length != 1 ||
            prefixParameters[0].ParameterType != typeof(object[]))
            throw new InvalidOperationException("Aventador Harmony prefix signature is incompatible with the selected Gley initializer.");

        var install = patch.GetMethod("Install", Statics)!;
        var installParameters = install.GetParameters();
        if (!install.IsStatic || installParameters.Length != 1 || installParameters[0].ParameterType.FullName != "BAModAPI.ModContext")
            throw new InvalidOperationException("Aventador traffic hook installation no longer accepts the mod context used by OnLoad.");

        VerifyProductionAiPrefabConstructionWithoutGameVehicleController(support);

        var cacheField = typeof(PrefabHelper).GetField("PrefabCache", Statics)!;
        var cache = cacheField.GetValue(null) as IDictionary;
        if (cache == null)
            throw new InvalidOperationException("Aventador traffic regression could not access the native prefab cache.");

        var previousTraffic = TrafficComponent.Instance;
        var managerField = typeof(TrafficManager).GetField("instance", Statics)!;
        var previousManager = managerField.GetValue(null);
        var initializedField = typeof(TrafficManager).GetField("initialized", Members)!;
        var wasCachePresent = cache.Contains(OwnedCacheKey);
        var previousCachedPrefab = wasCachePresent ? cache[OwnedCacheKey] : null;
        var root = new GameObject("AventadorTrafficInitializationRegression");
        var testPlayerPrefab = new GameObject("AventadorTrafficRegressionPlayerPrefab");
        testPlayerPrefab.SetActive(false);
        var testAiPrefab = new GameObject("AventadorTrafficRegressionAiPrefab");
        testAiPrefab.SetActive(false);
        var pool = ScriptableObject.CreateInstance<VehiclePool>();
        var initializedPool = ScriptableObject.CreateInstance<VehiclePool>();
        var replacementPool = ScriptableObject.CreateInstance<VehiclePool>();
        var initializedArray = Array.Empty<CarType>();
        var replacementArray = Array.Empty<CarType>();
        TrafficManager? manager = null;

        void Require(bool success, string message)
        {
            if (!success) throw new InvalidOperationException(message);
        }

        try
        {
            var activeField = support.GetField("active", Statics)!;
            var playerPrefabField = support.GetField("playerPrefab", Statics)!;
            var previousCacheCapturedField = support.GetField("previousCacheCaptured", Statics)!;
            var modifiedPoolField = support.GetField("modifiedVehiclePool", Statics)!;
            var carTypeField = support.GetField("customCarType", Statics)!;
            var ownsPoolField = support.GetField("ownsPoolEntry", Statics)!;

            activeField.SetValue(null, true);
            playerPrefabField.SetValue(null, testPlayerPrefab);
            trafficPrefab.SetValue(null, testAiPrefab);
            previousCacheCapturedField.SetValue(null, false);
            modifiedPoolField.SetValue(null, null);
            carTypeField.SetValue(null, null);
            ownsPoolField.SetValue(null, false);

            TrafficComponent.Instance = null;
            var earlyPrepare = support.GetMethod("PrepareTrafficPool", Statics, null,
                new[] { typeof(GameObject), typeof(string) }, null)!;
            Require(!(bool)earlyPrepare.Invoke(null, new object[] { testPlayerPrefab, "fixture-scene-loaded" })!,
                "Scene-loaded attempt unexpectedly succeeded before TrafficComponent supplied a pool.");

            var stock = new CarType { name = "AventadorFixtureStock", nrOfVehicles = 1, canBeAiDriven = true };
            var foreign = new CarType { name = "AventadorFixtureOtherMod", nrOfVehicles = 1, canBeAiDriven = true };
            pool.trafficCars = new[] { stock, foreign };
            manager = root.AddComponent<TrafficManager>();
            managerField.SetValue(null, manager);
            initializedField.SetValue(manager, false);

            // Harmony invokes this prefix immediately before the verified native
            // initializer body, which then passes this pool to TrafficVehicles.Initialize.
            prefix.Invoke(null, new object[] { new object[] { new Transform[1], 2, pool } });
            var registered = Array.Find(pool.trafficCars, car => car != null && car.name == OwnedCarTypeName);
            Require(registered != null && ReferenceEquals(registered.vehiclePrefab, testAiPrefab),
                "Gley's initialization prefix did not append the owned car type to its exact pool argument.");
            Require(pool.trafficCars.Length == 3 && ReferenceEquals(pool.trafficCars[0], stock) &&
                    ReferenceEquals(pool.trafficCars[1], foreign),
                "Pre-allocation registration changed stock or another mod's pool entries.");
            Require(TrafficComponent.Instance == null,
                "Regression did not exercise registration through the initializer's VehiclePool argument.");

            prefix.Invoke(null, new object[] { new object[] { new Transform[1], 2, pool } });
            Require(pool.trafficCars.Length == 3 &&
                    Array.FindAll(pool.trafficCars, car => car != null && car.name == OwnedCarTypeName).Length == 1,
                "Repeated Gley initialization prefix appended a duplicate Aventador traffic entry.");

            initializedPool.trafficCars = new[]
            {
                new CarType { name = "AventadorInitializedStock", nrOfVehicles = 1, canBeAiDriven = true },
                new CarType { name = "AventadorInitializedOtherMod", nrOfVehicles = 1, canBeAiDriven = true }
            };
            initializedArray = initializedPool.trafficCars;
            initializedField.SetValue(manager, true);
            prefix.Invoke(null, new object[] { new object[] { new Transform[1], 2, initializedPool } });
            Require(ReferenceEquals(initializedPool.trafficCars, initializedArray) && initializedPool.trafficCars.Length == 2,
                "Registration mutated a replacement pool after TrafficManager was initialized.");

            replacementPool.trafficCars = new[]
            {
                new CarType { name = "AventadorReplacementStock", nrOfVehicles = 1, canBeAiDriven = true },
                new CarType { name = "AventadorReplacementOtherMod", nrOfVehicles = 1, canBeAiDriven = true }
            };
            replacementArray = replacementPool.trafficCars;
            initializedField.SetValue(manager, false);
            prefix.Invoke(null, new object[] { new object[] { new Transform[1], 2, replacementPool } });
            var replacementOwned = Array.Find(replacementPool.trafficCars,
                car => car != null && car.name == OwnedCarTypeName);
            Require(replacementOwned != null && ReferenceEquals(replacementOwned.vehiclePrefab, testAiPrefab),
                "A different replacement pool was not prepared while TrafficManager was uninitialized.");
            Require(replacementPool.trafficCars.Length == replacementArray.Length + 1 &&
                    ReferenceEquals(replacementPool.trafficCars[0], replacementArray[0]) &&
                    ReferenceEquals(replacementPool.trafficCars[1], replacementArray[1]),
                "Preparing a replacement pool changed stock or another mod's entries.");

            // Keep cache restoration under fixture control while making RemoveVehicle
            // exercise only its production pool ownership cleanup.
            trafficPrefab.SetValue(null, null);
            support.GetMethod("RemoveVehicle", Statics)!.Invoke(null, new object[] { "" });
            Require(pool.trafficCars.Length == 2 && ReferenceEquals(pool.trafficCars[0], stock) &&
                    ReferenceEquals(pool.trafficCars[1], foreign),
                "Unload did not remove the owned entry from the original pool while preserving stock and foreign entries.");
            Require(replacementPool.trafficCars.Length == replacementArray.Length &&
                    ReferenceEquals(replacementPool.trafficCars[0], replacementArray[0]) &&
                    ReferenceEquals(replacementPool.trafficCars[1], replacementArray[1]),
                "Unload did not remove the owned entry from the replacement pool while preserving stock and foreign entries.");
            Debug.Log("Aventador traffic regression passed: exact initializer target/prefix signatures validated; registration appended once before allocation, preserved other entries, refused to mutate a missing entry after initialization, prepared a different pool before initialization, and unload removed only owned entries from both pools.");
        }
        finally
        {
            trafficPrefab.SetValue(null, null);
            support.GetMethod("RemoveVehicle", Statics)!.Invoke(null, new object[] { "" });
            TrafficComponent.Instance = previousTraffic;
            managerField.SetValue(null, previousManager);
            if (cache.Contains(OwnedCacheKey)) cache.Remove(OwnedCacheKey);
            if (wasCachePresent) cache[OwnedCacheKey] = previousCachedPrefab;
            if (manager != null) initializedField.SetValue(manager, false);
            pool.trafficCars = Array.Empty<CarType>();
            initializedPool.trafficCars = initializedArray;
            replacementPool.trafficCars = replacementArray;
            UnityEngine.Object.DestroyImmediate(pool);
            UnityEngine.Object.DestroyImmediate(initializedPool);
            UnityEngine.Object.DestroyImmediate(replacementPool);
            UnityEngine.Object.DestroyImmediate(testAiPrefab);
            UnityEngine.Object.DestroyImmediate(testPlayerPrefab);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void VerifyProductionAiPrefabConstructionWithoutGameVehicleController(Type support)
    {
        var factoryField = support.GetField("AiTemplateFactoryForTests", Statics);
        if (factoryField == null)
            throw new InvalidOperationException("Aventador traffic regression cannot inject a native Gley template fixture.");

        var previousFactory = factoryField.GetValue(null);
        var player = new GameObject("AventadorTrafficPrefabConstructionPlayer");
        var template = new GameObject("AventadorTrafficPrefabConstructionGleyTemplate");
        player.SetActive(false);
        template.SetActive(false);
        GameObject? createdPrefab = null;
        Material? paintMaterial = null;
        Data.VehicleColors.VehicleColor? color = null;

        try
        {
            var body = new GameObject("AventadorBody").transform;
            body.SetParent(player.transform, false);
            var paintNode = new GameObject("RED_PAINT").transform;
            paintNode.SetParent(body, false);
            AddPrimitive(paintNode, "Object_56", Vector3.zero, new Vector3(1.5f, 0.8f, 4.2f));
            var collider = new GameObject("BodyCollider");
            collider.transform.SetParent(player.transform, false);
            collider.AddComponent<BoxCollider>().size = new Vector3(1.8f, 0.8f, 4.5f);
            var seat = new GameObject("DriverSeatAnchor");
            seat.transform.SetParent(player.transform, false);
            seat.transform.localPosition = new Vector3(0f, 0.7f, -0.2f);
            var steeringWheel = new GameObject("Steering_wheel");
            steeringWheel.transform.SetParent(player.transform, false);
            steeringWheel.transform.localPosition = new Vector3(-0.32f, 0.95f, 0.62f);

            var nativeWheelPositions = new[]
            {
                new Vector3(-0.8f, 0f, 1.4f), new Vector3(0.8f, 0f, 1.4f),
                new Vector3(-0.8f, 0f, -1.4f), new Vector3(0.8f, 0f, -1.4f)
            };
            var wheelNames = new[] { "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" };
            var caliperNames = new[] { "Caliper_FL", "Caliper_FR", "Caliper_BL", "Caliper_BR" };
            for (var index = 0; index < nativeWheelPositions.Length; index++)
            {
                AddPrimitive(player.transform, wheelNames[index], nativeWheelPositions[index],
                    new Vector3(0.52f, 0.66f, 0.24f));
                AddPrimitive(player.transform, caliperNames[index], nativeWheelPositions[index],
                    new Vector3(0.16f, 0.22f, 0.12f));
            }

            var gley = AddNativeTemplateComponent(template);
            var allWheelsProperty = typeof(VehicleComponent).GetProperty("allWheels", Members);
            var allWheelsField = typeof(VehicleComponent).GetField("allWheels", Members);
            var arrayType = allWheelsProperty?.PropertyType ?? allWheelsField?.FieldType;
            if (arrayType == null || !arrayType.IsArray)
                throw new InvalidOperationException("Aventador regression could not inspect Gley's native allWheels array in ExternalPlugins.dll.");

            var wheelArray = Array.CreateInstance(arrayType.GetElementType()!, 4);
            for (var index = 0; index < 4; index++)
            {
                var wheel = Activator.CreateInstance(arrayType.GetElementType()!, nonPublic: true);
                if (wheel == null)
                    throw new InvalidOperationException("Aventador regression could not create a Gley wheel fixture.");
                var graphics = new GameObject("NativeWheelGraphics_" + index).transform;
                graphics.SetParent(template.transform, false);
                graphics.localPosition = nativeWheelPositions[index];
                var wheelTransform = new GameObject("NativeWheelTransform_" + index).transform;
                wheelTransform.SetParent(template.transform, false);
                wheelTransform.localPosition = nativeWheelPositions[index];
                SetMember(wheel, "wheelGraphics", graphics);
                SetMember(wheel, "wheelTransform", wheelTransform);
                SetMember(wheel, "wheelPosition", Enum.Parse(
                    wheel.GetType().GetNestedType("WheelPosition", BindingFlags.Public | BindingFlags.NonPublic) ??
                    typeof(Wheel.WheelPosition), index < 2 ? "Front" : "Back"));
                SetMember(wheel, "maxSuspension", 0.2f);
                wheelArray.SetValue(wheel, index);
            }
            if (allWheelsProperty?.CanWrite == true)
                allWheelsProperty.SetValue(gley, wheelArray);
            else if (allWheelsField != null)
                allWheelsField.SetValue(gley, wheelArray);
            else
                throw new InvalidOperationException("Aventador regression cannot populate Gley's native allWheels array.");

            var factory = new Func<GameObject?>(() => template);
            factoryField.SetValue(null, factory);
            var create = support.GetMethod("CreateAiPrefab", Statics, null,
                new[] { typeof(GameObject) }, null)!;
            createdPrefab = create.Invoke(null, new object[] { player }) as GameObject;
            if (createdPrefab == null)
                throw new InvalidOperationException("Production CreateAiPrefab rejected a Gley template with four wheels and no player VehicleController.");
            if (createdPrefab.GetComponent<VehicleController>() != null ||
                createdPrefab.GetComponent<VehicleComponent>() == null ||
                createdPrefab.GetComponent<VehicleComponent>().allWheels?.Length != 4)
                throw new InvalidOperationException("Production Aventador traffic prefab does not preserve the native Gley-only controller contract.");
            var npcSeat = Find(createdPrefab.transform, "NpcDriverSeatAnchor");
            var trafficSteeringWheel = Find(createdPrefab.transform, "Steering_wheel");
            if (npcSeat == null || trafficSteeringWheel == null ||
                Vector3.Distance(npcSeat.localPosition, seat.transform.localPosition) > 0.001f ||
                Vector3.Distance(trafficSteeringWheel.localPosition, steeringWheel.transform.localPosition) > 0.001f)
                throw new InvalidOperationException("Production Aventador traffic prefab did not copy both NPC seat and static steering anchors into the controller-less Gley prefab.");
            var initializerType = support.Assembly.GetType(
                "LamborghiniAventadorTrafficCloneInitializer", throwOnError: true)!;
            if (createdPrefab.GetComponent(initializerType) == null)
                throw new InvalidOperationException("Production Aventador traffic prefab omitted its per-clone visual initializer.");

            var shader = Shader.Find("HDRP/Lit") ?? Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("Aventador regression cannot create a RED_PAINT material for the ambient paint check.");
            paintMaterial = new Material(shader) { name = "RED_PAINT" };
            var bodyRenderer = createdPrefab.GetComponentsInChildren<Renderer>(true)
                .FirstOrDefault(renderer => renderer.name == "Object_56");
            if (bodyRenderer == null)
                throw new InvalidOperationException("Production Aventador traffic prefab did not copy its body renderer.");
            paintMaterial.name = "ImportedBodyPaintSource";
            bodyRenderer.sharedMaterial = paintMaterial;
            color = ScriptableObject.CreateInstance<Data.VehicleColors.VehicleColor>();
            color.name = "AventadorTrafficRegressionColor";
            color.tint = Color.red;
            var paintType = support.Assembly.GetType("LamborghiniAventadorPaintController", throwOnError: true)!;
            var paint = createdPrefab.GetComponent(paintType) ?? createdPrefab.AddComponent(paintType);
            var initializePaint = paintType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                {
                    var parameters = method.GetParameters();
                    return method.Name == "InitializeForAmbientTraffic" && parameters.Length == 2 &&
                           parameters[0].ParameterType == typeof(Data.VehicleColors.VehicleColor);
                });
            initializePaint?.Invoke(paint, new object?[] { color, null });
            var appliedColor = paintType.GetProperty("HasAppliedColor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var paintApplied = appliedColor?.GetValue(paint) is bool didApply && didApply;
            if (initializePaint == null || !paintApplied)
                throw new InvalidOperationException("Aventador ambient paint did not apply its explicit color through a renamed material beneath the RED_PAINT node without a player VehicleController.");

            Debug.Log("Aventador traffic regression passed: production CreateAiPrefab built from a Gley VehicleComponent/four-wheel template without a game VehicleController, retained clone visual initialization, copied the NPC seat and static steering anchors, and applied explicit ambient paint through a renamed material under RED_PAINT.");
        }
        finally
        {
            factoryField.SetValue(null, previousFactory);
            if (createdPrefab != null) UnityEngine.Object.DestroyImmediate(createdPrefab);
            if (color != null) UnityEngine.Object.DestroyImmediate(color);
            if (paintMaterial != null) UnityEngine.Object.DestroyImmediate(paintMaterial);
            UnityEngine.Object.DestroyImmediate(template);
            UnityEngine.Object.DestroyImmediate(player);
        }
    }

    private static VehicleComponent AddNativeTemplateComponent(GameObject template)
    {
        // The native editor callback assumes authored wheel data already exists.
        // Our transient fixture must add the component before populating it.
        // Consume only that exact callback's known null reference; every other
        // exception and all production factory calls keep the normal logger.
        var previous = Debug.unityLogger.logHandler;
        try
        {
            Debug.unityLogger.logHandler = new NativeTemplateValidationLogHandler(previous);
            return template.AddComponent<VehicleComponent>();
        }
        finally { Debug.unityLogger.logHandler = previous; }
    }

    private sealed class NativeTemplateValidationLogHandler : ILogHandler
    {
        private readonly ILogHandler previous;
        internal NativeTemplateValidationLogHandler(ILogHandler previous) => this.previous = previous;
        public void LogException(Exception exception, UnityEngine.Object context)
        {
            if (exception is NullReferenceException && exception.StackTrace != null &&
                exception.StackTrace.Contains("GleyTrafficSystem.VehicleComponent.OnValidate"))
                return;
            previous.LogException(exception, context);
        }
        public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args) =>
            previous.LogFormat(type, context, format, args);
    }

    private static Transform AddPrimitive(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        primitive.name = name;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = position;
        primitive.transform.localScale = scale;
        return primitive.transform;
    }

    private static Transform? Find(Transform root, string name)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(child.name, name, StringComparison.Ordinal)) return child;
        return null;
    }

    private static void SetMember(object target, string name, object value)
    {
        var type = target.GetType();
        var field = type.GetField(name, Members);
        if (field != null && field.FieldType.IsInstanceOfType(value))
        {
            field.SetValue(target, value);
            return;
        }
        var property = type.GetProperty(name, Members);
        if (property?.CanWrite == true && property.PropertyType.IsInstanceOfType(value))
        {
            property.SetValue(target, value);
            return;
        }
        throw new InvalidOperationException($"Aventador regression could not set native Gley wheel member '{name}'.");
    }
}
