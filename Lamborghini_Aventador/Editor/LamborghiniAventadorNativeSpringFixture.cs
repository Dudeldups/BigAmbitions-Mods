#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NWH.WheelController3D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Exercises the prefab's real NWH wheel Step method against a synthetic ground
/// plane, comparing radius-height anchors with the authored spring-rest anchors.
/// </summary>
public static class LamborghiniAventadorNativeSpringFixture
{
    private const string PrefabPath = "Assets/Mods/Lamborghini_Aventador/LamborghiniAventador.prefab";
    private const float FixedStep = 0.02f;
    private const int SettleSteps = 300;
    private const int DropTailSteps = 50;
    private const float VehicleMassKg = 1770f;
    private const float Gravity = 9.81f;
    private const float FixtureOffset = 1000f;
    private const float GroundPlaneY = 0f;
    private const BindingFlags InstanceMethods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void VerifyNativeSpringRestFixture()
    {
        if (!Application.isBatchMode)
            throw new InvalidOperationException("Run the native physics fixture through the isolated Aventador batch-build workflow.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Aventador native spring fixture could not load its prefab.");

        var groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0)
            throw new InvalidOperationException(
                "Aventador native spring fixture requires the project's Ground layer so NWH uses its actual configured wheel mask.");

        var previousSimulationMode = Physics.simulationMode;
        var previousGravity = Physics.gravity;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            Physics.gravity = new Vector3(0f, -Gravity, 0f);
            var physics = scene.GetPhysicsScene();
            if (!physics.IsValid())
                throw new InvalidOperationException("Aventador native spring fixture could not access the default PhysicsScene.");

            var westGround = CreateGround(scene, groundLayer, -FixtureOffset);
            var eastGround = CreateGround(scene, groundLayer, FixtureOffset);
            Physics.SyncTransforms();

            var expectedWeight = VehicleMassKg * -Physics.gravity.y;

            var originalAnchors = CreateVehicle(prefab, scene,
                new Vector3(-FixtureOffset, GroundPlaneY, 0f), raisedAnchors: false, westGround);
            var restAnchors = CreateVehicle(prefab, scene,
                new Vector3(FixtureOffset, GroundPlaneY + LamborghiniAventadorSetup.WheelCenterLowering, 0f), raisedAnchors: true, eastGround);
            try
            {
                StepNativeWheels(originalAnchors.Wheels, originalAnchors.Ground, requireGroundContact: true);
                StepNativeWheels(restAnchors.Wheels, restAnchors.Ground, requireGroundContact: true);
                Physics.SyncTransforms();

                var originalFirstStep = ReadSpringState(originalAnchors);
                var restFirstStep = ReadSpringState(restAnchors);
                RequireGroundSupport(originalFirstStep, "radius-height anchors at root ground");
                RequireGroundSupport(restFirstStep, "spring-rest anchors at authored equilibrium height");

                var oldTotalForce = originalFirstStep.Sum(item => item.Force);
                var restTotalForce = restFirstStep.Sum(item => item.Force);
                if (oldTotalForce < expectedWeight * 3.5f)
                    throw new InvalidOperationException(
                        $"Aventador native spring fixture did not reproduce the fully compressed baseline: " +
                        $"expected about 86kN, found {oldTotalForce:0.0}N across the four NWH springs. " +
                        FormatSprings(originalFirstStep));
                if (Mathf.Abs(restTotalForce - expectedWeight) > expectedWeight * 0.25f)
                    throw new InvalidOperationException(
                        $"Aventador native spring fixture raised anchors but did not measure near-static support: " +
                        $"expected {expectedWeight:0.0}N ±25%, found {restTotalForce:0.0}N. " +
                        FormatSprings(restFirstStep));

                Debug.Log(
                    $"Aventador native spring first-step comparison passed: " +
                    $"radius-height anchors={oldTotalForce:0.0}N (expected approximately 86kN), " +
                    $"rest anchors={restTotalForce:0.0}N (vehicle weight {expectedWeight:0.0}N); " +
                    $"old=[{FormatSprings(originalFirstStep)}], rest=[{FormatSprings(restFirstStep)}].");
            }
            finally
            {
                DestroyVehicle(originalAnchors);
                DestroyVehicle(restAnchors);
            }

            GroundStartResult oldGroundStart;
            var oldGrounded = CreateVehicle(prefab, scene,
                new Vector3(-FixtureOffset, GroundPlaneY, 0f), raisedAnchors: false, westGround);
            try
            {
                oldGroundStart = SimulateGroundStart(physics, oldGrounded);
                if (oldGroundStart.PeakRootY < 0.05f)
                    throw new InvalidOperationException(
                        $"Aventador native spring fixture did not reproduce baseline launch from root ground: " +
                        $"peak rootY={oldGroundStart.PeakRootY:0.000}m, expected >0.050m.");
            }
            finally
            {
                DestroyVehicle(oldGrounded);
                Physics.SyncTransforms();
            }

            var restGrounded = CreateVehicle(prefab, scene,
                new Vector3(FixtureOffset, GroundPlaneY, 0f), raisedAnchors: true, eastGround);
            GroundStartResult restGroundStart;
            try
            {
                restGroundStart = SimulateGroundStart(physics, restGrounded);
                if (restGroundStart.PeakRootY > 0.03f || Mathf.Abs(restGroundStart.RootY) > 0.02f ||
                    Mathf.Abs(restGroundStart.VelocityY) > 0.05f)
                    throw new InvalidOperationException(
                        $"Aventador native spring fixture raised-anchor root-ground start still launched or failed to settle: " +
                        $"peak rootY={restGroundStart.PeakRootY:0.000}m, final rootY={restGroundStart.RootY:0.000}m, " +
                        $"final velocityY={restGroundStart.VelocityY:0.000}m/s.");
            }
            finally
            {
                DestroyVehicle(restGrounded);
                Physics.SyncTransforms();
            }

            Debug.Log(
                $"Aventador native spring root-ground launch regression passed over {SettleSteps} fixed steps: " +
                $"radius-height anchors peak rootY={oldGroundStart.PeakRootY:0.000}m, " +
                $"rest anchors peak rootY={restGroundStart.PeakRootY:0.000}m, " +
                $"old final rootY={oldGroundStart.RootY:R}m, returnRayHit={oldGroundStart.ReturnRayHitExpectedGround}, " +
                $"returnRayDistance={oldGroundStart.ReturnRayDistance:R}m, " +
                $"rest final rootY={restGroundStart.RootY:R}m, returnRayHit={restGroundStart.ReturnRayHitExpectedGround}, " +
                $"returnRayDistance={restGroundStart.ReturnRayDistance:R}m, " +
                $"velocityY={restGroundStart.VelocityY:0.000}m/s.");

            DropResult oldSettled;
            var oldDrop = CreateVehicle(prefab, scene,
                new Vector3(-FixtureOffset, 0.5f, 0f), raisedAnchors: false, westGround);
            try
            {
                oldSettled = SimulateDrop(physics, oldDrop);
                ValidateSettledDrop("radius-height anchors", oldDrop, oldSettled, expectedWeight);
            }
            finally
            {
                DestroyVehicle(oldDrop);
                Physics.SyncTransforms();
            }

            var restDrop = CreateVehicle(prefab, scene,
                new Vector3(FixtureOffset, 0.5f, 0f), raisedAnchors: true, eastGround);
            DropResult restSettled;
            try
            {
                restSettled = SimulateDrop(physics, restDrop);
                ValidateSettledDrop("raised spring-rest anchors", restDrop, restSettled, expectedWeight);
            }
            finally
            {
                DestroyVehicle(restDrop);
                Physics.SyncTransforms();
            }

            Debug.Log(
                $"Aventador native spring 0.5m drop comparison passed after {SettleSteps} fixed steps each: " +
                $"old rootY={oldSettled.RootY:0.000}, velocityY={oldSettled.VelocityY:0.000}m/s, " +
                $"settledSpringForce={oldSettled.SettledTotalForce:0.0}N, " +
                $"peakSpringForce={oldSettled.PeakTotalForce:0.0}N; " +
                $"rest rootY={restSettled.RootY:0.000}, velocityY={restSettled.VelocityY:0.000}m/s, " +
                $"last{DropTailSteps}steps rootY={restSettled.TailMinRootY:0.000}..{restSettled.TailMaxRootY:0.000}m, " +
                $"max|velocityY|={restSettled.TailMaxAbsVelocityY:0.000}m/s, " +
                $"settledSpringForce={restSettled.SettledTotalForce:0.0}N, " +
                $"peakSpringForce={restSettled.PeakTotalForce:0.0}N, " +
                $"groundContactStep={restSettled.FirstContactStep}.");
        }
        finally
        {
            Physics.gravity = previousGravity;
            Physics.simulationMode = previousSimulationMode;
            if (scene.IsValid())
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    private static Collider CreateGround(Scene scene, int groundLayer, float x)
    {
        var ground = new GameObject($"AventadorNativeSpringFixtureGround_{x:0}");
        SceneManager.MoveGameObjectToScene(ground, scene);
        ground.layer = groundLayer;
        ground.transform.position = new Vector3(x, GroundPlaneY - 0.05f, 0f);
        var collider = ground.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = new Vector3(50f, 0.1f, 50f);
        return collider;
    }

    private static VehicleFixture CreateVehicle(
        GameObject prefab, Scene scene, Vector3 position, bool raisedAnchors, Collider ground)
    {
        var root = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        root.name = raisedAnchors
            ? "AventadorNativeSpringFixture_RestAnchors"
            : "AventadorNativeSpringFixture_RadiusAnchors";
        SceneManager.MoveGameObjectToScene(root, scene);

        var rigidbody = root.GetComponent<Rigidbody>();
        if (rigidbody == null)
            throw new InvalidOperationException("Aventador native spring fixture prefab has no Rigidbody.");
        rigidbody.isKinematic = false;
        rigidbody.useGravity = true;
        rigidbody.mass = VehicleMassKg;
        rigidbody.drag = 0f;
        rigidbody.angularDrag = 1.1f;
        // This bounded editor simulation has no rendered interpolation phase.
        // Disable visual interpolation so native wheel casts observe each
        // simulated Rigidbody pose instead of a stale rendered transform.
        rigidbody.interpolation = RigidbodyInterpolation.None;
        rigidbody.constraints = RigidbodyConstraints.None;
        rigidbody.centerOfMass = new Vector3(0f, 0.1f, -0.08f);
        rigidbody.velocity = Vector3.zero;
        rigidbody.angularVelocity = Vector3.zero;

        var wheels = root.GetComponentsInChildren<WheelController>(true);
        if (wheels.Length != 4)
            throw new InvalidOperationException(
                $"Aventador native spring fixture expected four native WheelControllers, found {wheels.Length}.");

        var allBehaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var behaviour in allBehaviours)
        {
            if (behaviour == null) continue;
            behaviour.enabled = behaviour is WheelController;
        }

        var frontZ = wheels.Where(wheel => wheel.transform.name.StartsWith("Front", StringComparison.Ordinal))
            .Average(wheel => root.transform.InverseTransformPoint(wheel.transform.position).z);
        var rearZ = wheels.Where(wheel => wheel.transform.name.StartsWith("Rear", StringComparison.Ordinal))
            .Average(wheel => root.transform.InverseTransformPoint(wheel.transform.position).z);
        if (frontZ <= rearZ)
            throw new InvalidOperationException(
                $"Aventador native spring fixture could not resolve its axles: frontZ={frontZ:0.000}, rearZ={rearZ:0.000}.");
        var wheelbase = frontZ - rearZ;
        var frontCornerLoad = rigidbody.mass * Gravity *
            (rigidbody.centerOfMass.z - rearZ) / wheelbase * 0.5f;
        var rearCornerLoad = rigidbody.mass * Gravity *
            (frontZ - rigidbody.centerOfMass.z) / wheelbase * 0.5f;

        foreach (var wheel in wheels)
        {
            if (wheel == null || wheel.spring == null || wheel.damper == null)
                throw new InvalidOperationException("Aventador native spring fixture found a wheel without NWH spring/damper state.");

            var isFront = wheel.transform.name.StartsWith("Front", StringComparison.Ordinal);
            var cornerLoad = isFront ? frontCornerLoad : rearCornerLoad;
            var extension = CalculateRestExtension(wheel, cornerLoad);
            var visual = wheel.wheel.visual != null
                ? wheel.wheel.visual.transform
                : throw new InvalidOperationException($"Aventador wheel '{wheel.name}' has no native rolling visual.");
            var visualLocalY = root.transform.InverseTransformPoint(visual.position).y;
            var desiredAnchorY = visualLocalY + (raisedAnchors ? extension : 0f);
            wheel.transform.localPosition = new Vector3(
                wheel.transform.localPosition.x, desiredAnchorY, wheel.transform.localPosition.z);

            var serialized = new SerializedObject(wheel);
            var layerMask = serialized.FindProperty("layerMask");
            var groundLayer = LayerMask.NameToLayer("Ground");
            if (layerMask == null || (layerMask.intValue & (1 << groundLayer)) == 0)
                throw new InvalidOperationException(
                    $"Aventador wheel '{wheel.name}' native ground mask excludes the fixture Ground layer.");

            InitializeNativeWheel(wheel);
        }

        return new VehicleFixture(root, rigidbody, wheels, ground);
    }

    private static void InitializeNativeWheel(WheelController wheel)
    {
        // Editor preview scenes do not enter Unity's play-mode MonoBehaviour lifecycle.
        // Invoke the native component's own setup callbacks so the same private caches
        // used by its real Step method are established before the fixture runs.
        var start = FindParameterlessMethod(wheel.GetType(), "Start")
            ?? throw new MissingMethodException(wheel.GetType().FullName, "Start()");
        InvokeNative(start, wheel, $"NWH WheelController.Start for {wheel.name}");

        // The fixture starts from a static pose (or a controlled free fall), so
        // don't inject a synthetic first-frame damper velocity from the editor
        // object's default spring history.
        wheel.spring.prevLength = wheel.spring.length;
        wheel.spring.compressionVelocity = 0f;
        wheel.spring.prevVelocity = 0f;

        var step = FindParameterlessMethod(wheel.GetType(), "Step");
        if (step == null)
            throw new MissingMethodException(wheel.GetType().FullName, "Step()");
    }

    private static void StepNativeWheels(
        WheelController[] wheels, Collider expectedGround, bool requireGroundContact = false)
    {
        foreach (var wheel in wheels)
        {
            var step = FindParameterlessMethod(wheel.GetType(), "Step")
                ?? throw new MissingMethodException(wheel.GetType().FullName, "Step()");
            InvokeNative(step, wheel, $"NWH WheelController.Step for {wheel.name}");
            VerifyGroundHit(wheel, expectedGround, requireGroundContact);
        }
    }

    private static void VerifyGroundHit(WheelController wheel, Collider expectedGround, bool requireGroundContact)
    {
        var hitField = typeof(WheelController).GetField("wheelHit", InstanceFields)
            ?? throw new MissingFieldException(typeof(WheelController).FullName, "wheelHit");
        var hit = hitField.GetValue(wheel)
            ?? throw new InvalidOperationException($"Native wheelHit state is null for '{wheel.name}' after Step().");
        var colliderField = hit.GetType().GetField("collider", InstanceFields)
            ?? throw new MissingFieldException(hit.GetType().FullName, "collider");
        var hitCollider = colliderField.GetValue(hit) as Collider;
        var groundedField = typeof(WheelController).GetField("_isGrounded", InstanceFields)
            ?? throw new MissingFieldException(typeof(WheelController).FullName, "_isGrounded");
        var grounded = groundedField.GetValue(wheel) is true;
        if (!grounded)
        {
            if (requireGroundContact)
                throw new InvalidOperationException(
                    $"Aventador native spring fixture '{wheel.name}' was airborne during a required ground Step().");
            return;
        }

        if (hitCollider != expectedGround)
            throw new InvalidOperationException(
                $"Aventador native spring fixture '{wheel.name}' native Step hit " +
                $"'{(hitCollider != null ? hitCollider.name : "<none>")}', expected exact fixture ground '{expectedGround.name}'.");
    }

    private static float CalculateRestExtension(WheelController wheel, float cornerLoad)
    {
        if (wheel.spring.maxForce <= 0f || wheel.spring.maxLength <= 0f ||
            cornerLoad <= 0f || cornerLoad >= wheel.spring.maxForce)
            throw new InvalidOperationException(
                $"Aventador wheel '{wheel.name}' spring cannot support static corner load {cornerLoad:0.0}N.");
        var normalizedLoad = cornerLoad / wheel.spring.maxForce;
        var curveLoad = wheel.spring.forceCurve.Evaluate(normalizedLoad);
        if (Mathf.Abs(curveLoad - normalizedLoad) > 0.001f)
            throw new InvalidOperationException(
                $"Aventador wheel '{wheel.name}' spring curve is not linear at static load " +
                $"({normalizedLoad:0.000}->{curveLoad:0.000}); fixture must not assume another curve.");
        return wheel.spring.maxLength * (1f - normalizedLoad);
    }

    private static SpringSample[] ReadSpringState(VehicleFixture fixture)
    {
        return fixture.Wheels.Select((wheel, index) => new SpringSample(
            index,
            wheel.transform.name,
            wheel.spring.length,
            wheel.spring.maxLength,
            wheel.spring.force,
            wheel.spring.maxForce,
            IsGrounded(wheel))).ToArray();
    }

    private static void RequireGroundSupport(SpringSample[] samples, string stage)
    {
        var ungrounded = samples.Where(item => !item.Grounded || item.Force <= 1f).ToArray();
        if (ungrounded.Length > 0)
            throw new InvalidOperationException(
                $"Aventador native spring fixture did not observe native NWH ground support at {stage}; " +
                $"the fixture refuses to pass without real WheelController.Step contact. " +
                FormatSprings(samples));
    }

    private static DropResult SimulateDrop(PhysicsScene physics, VehicleFixture fixture)
    {
        Physics.SyncTransforms();
        var firstContactStep = -1;
        var peakTotalForce = 0f;
        var lowestRootY = fixture.Root.transform.position.y;
        var highestRootYAfterContact = float.NegativeInfinity;
        var tailMinRootY = float.PositiveInfinity;
        var tailMaxRootY = float.NegativeInfinity;
        var tailMaxAbsVelocityY = 0f;
        SpringSample[] settled = Array.Empty<SpringSample>();

        for (var stepIndex = 0; stepIndex < SettleSteps; stepIndex++)
        {
            StepNativeWheels(fixture.Wheels, fixture.Ground, requireGroundContact: false);
            var samples = ReadSpringState(fixture);
            var totalForce = samples.Sum(item => item.Force);
            if (totalForce > peakTotalForce) peakTotalForce = totalForce;
            if (firstContactStep < 0 && samples.Any(item => item.Grounded))
                firstContactStep = stepIndex;

            Physics.SyncTransforms();
            physics.Simulate(FixedStep);
            Physics.SyncTransforms();

            var rootY = fixture.Rigidbody.position.y;
            if (rootY < lowestRootY) lowestRootY = rootY;
            if (firstContactStep >= 0 && rootY > highestRootYAfterContact)
                highestRootYAfterContact = rootY;
            if (stepIndex >= SettleSteps - DropTailSteps)
            {
                tailMinRootY = Mathf.Min(tailMinRootY, rootY);
                tailMaxRootY = Mathf.Max(tailMaxRootY, rootY);
                tailMaxAbsVelocityY = Mathf.Max(tailMaxAbsVelocityY, Mathf.Abs(fixture.Rigidbody.velocity.y));
            }
            settled = samples;
        }

        if (firstContactStep < 0)
            throw new InvalidOperationException(
                $"Aventador native spring fixture drop never produced native NWH ground contact in {SettleSteps} fixed steps.");

        return new DropResult(
            fixture.Rigidbody.position.y,
            fixture.Rigidbody.velocity.y,
            lowestRootY,
            highestRootYAfterContact,
            tailMinRootY,
            tailMaxRootY,
            tailMaxAbsVelocityY,
            firstContactStep,
            peakTotalForce,
            settled.Sum(item => item.Force),
            settled);
    }

    private static GroundStartResult SimulateGroundStart(PhysicsScene physics, VehicleFixture fixture)
    {
        Physics.SyncTransforms();
        var peakRootY = fixture.Rigidbody.position.y;
        var hadGroundContact = false;

        for (var stepIndex = 0; stepIndex < SettleSteps; stepIndex++)
        {
            StepNativeWheels(fixture.Wheels, fixture.Ground, requireGroundContact: false);
            hadGroundContact |= fixture.Wheels.Any(IsGrounded);

            Physics.SyncTransforms();
            physics.Simulate(FixedStep);
            Physics.SyncTransforms();
            peakRootY = Mathf.Max(peakRootY, fixture.Rigidbody.position.y);
        }

        if (!hadGroundContact)
            throw new InvalidOperationException(
                "Aventador native spring fixture root-ground start never produced a native NWH ground hit.");

        var finalRootPosition = fixture.Rigidbody.position;
        var returnRayOrigin = finalRootPosition + Vector3.up * 2.5f;
        var returnRayHitExpectedGround = Physics.Raycast(
            returnRayOrigin, Vector3.down, out var returnRayHit, 2.5f,
            1 << LayerMask.NameToLayer("Ground")) && returnRayHit.collider == fixture.Ground;

        return new GroundStartResult(
            finalRootPosition.y,
            fixture.Rigidbody.velocity.y,
            peakRootY,
            returnRayHitExpectedGround,
            returnRayHitExpectedGround ? returnRayHit.distance : -1f);
    }

    private static void ValidateSettledDrop(string label, VehicleFixture fixture, DropResult result, float expectedWeight)
    {
        RequireGroundSupport(result.SettledSprings, $"after 0.5m drop with {label}");
        if (Mathf.Abs(result.VelocityY) > 0.1f)
            throw new InvalidOperationException(
                $"Aventador native spring fixture did not settle after 0.5m drop with {label}: " +
                $"vertical velocity={result.VelocityY:0.000}m/s, rootY={result.RootY:0.000}, " +
                $"last{DropTailSteps}steps rootY={result.TailMinRootY:0.000}..{result.TailMaxRootY:0.000}m, " +
                $"max|velocityY|={result.TailMaxAbsVelocityY:0.000}m/s, " +
                FormatSprings(result.SettledSprings));
        if (result.TailMaxRootY - result.TailMinRootY > 0.02f || result.TailMaxAbsVelocityY > 0.05f)
            throw new InvalidOperationException(
                $"Aventador native spring fixture did not stabilize in the last {DropTailSteps} steps with {label}: " +
                $"rootY={result.TailMinRootY:0.000}..{result.TailMaxRootY:0.000}m, " +
                $"max|velocityY|={result.TailMaxAbsVelocityY:0.000}m/s.");
        if (Mathf.Abs(result.SettledTotalForce - expectedWeight) > expectedWeight * 0.35f)
            throw new InvalidOperationException(
                $"Aventador native spring fixture settled support is not near vehicle weight with {label}: " +
                $"expected {expectedWeight:0.0}N ±35%, found {result.SettledTotalForce:0.0}N. " +
                FormatSprings(result.SettledSprings));
        if (float.IsNaN(result.HighestRootYAfterContact) || float.IsInfinity(result.HighestRootYAfterContact) ||
            result.HighestRootYAfterContact - result.LowestRootY > 0.9f)
            throw new InvalidOperationException(
                $"Aventador native spring fixture observed an excessive post-contact hop with {label}: " +
                $"root range={result.LowestRootY:0.000}..{result.HighestRootYAfterContact:0.000}m.");
    }

    private static void DestroyVehicle(VehicleFixture fixture)
    {
        if (fixture.Root != null)
            UnityEngine.Object.DestroyImmediate(fixture.Root);
    }

    private static bool IsGrounded(WheelController wheel)
    {
        var field = typeof(WheelController).GetField("_isGrounded", InstanceFields)
            ?? throw new MissingFieldException(typeof(WheelController).FullName, "_isGrounded");
        return field.GetValue(wheel) is true;
    }

    private static MethodInfo? FindParameterlessMethod(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var method = current.GetMethod(name, InstanceMethods | BindingFlags.DeclaredOnly,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            if (method != null) return method;
        }
        return null;
    }

    private static void InvokeNative(MethodInfo method, object instance, string operation)
    {
        try
        {
            method.Invoke(instance, null);
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                $"Aventador native spring fixture could not execute {operation}; native NWH initialization is incomplete.",
                exception.InnerException ?? exception);
        }
    }

    private static string FormatSprings(IEnumerable<SpringSample> samples) =>
        string.Join("; ", samples.Select(item =>
            $"{item.Index}:{item.Name}(length={item.Length:0.000}/{item.MaxLength:0.000},force={item.Force:0.0}/{item.MaxForce:0.0}N)"));

    private sealed class VehicleFixture
    {
        internal VehicleFixture(GameObject root, Rigidbody rigidbody, WheelController[] wheels, Collider ground)
        {
            Root = root;
            Rigidbody = rigidbody;
            Wheels = wheels;
            Ground = ground;
        }

        internal GameObject Root { get; }
        internal Rigidbody Rigidbody { get; }
        internal WheelController[] Wheels { get; }
        internal Collider Ground { get; }
    }

    private readonly struct SpringSample
    {
        internal SpringSample(int index, string name, float length, float maxLength,
            float force, float maxForce, bool grounded)
        {
            Index = index;
            Name = name;
            Length = length;
            MaxLength = maxLength;
            Force = force;
            MaxForce = maxForce;
            Grounded = grounded;
        }

        internal int Index { get; }
        internal string Name { get; }
        internal float Length { get; }
        internal float MaxLength { get; }
        internal float Force { get; }
        internal float MaxForce { get; }
        internal bool Grounded { get; }
    }

    private readonly struct DropResult
    {
        internal DropResult(float rootY, float velocityY, float lowestRootY,
            float highestRootYAfterContact, float tailMinRootY, float tailMaxRootY,
            float tailMaxAbsVelocityY, int firstContactStep, float peakTotalForce,
            float settledTotalForce, SpringSample[] settledSprings)
        {
            RootY = rootY;
            VelocityY = velocityY;
            LowestRootY = lowestRootY;
            HighestRootYAfterContact = highestRootYAfterContact;
            TailMinRootY = tailMinRootY;
            TailMaxRootY = tailMaxRootY;
            TailMaxAbsVelocityY = tailMaxAbsVelocityY;
            FirstContactStep = firstContactStep;
            PeakTotalForce = peakTotalForce;
            SettledTotalForce = settledTotalForce;
            SettledSprings = settledSprings;
        }

        internal float RootY { get; }
        internal float VelocityY { get; }
        internal float LowestRootY { get; }
        internal float HighestRootYAfterContact { get; }
        internal float TailMinRootY { get; }
        internal float TailMaxRootY { get; }
        internal float TailMaxAbsVelocityY { get; }
        internal int FirstContactStep { get; }
        internal float PeakTotalForce { get; }
        internal float SettledTotalForce { get; }
        internal SpringSample[] SettledSprings { get; }
    }

    private readonly struct GroundStartResult
    {
        internal GroundStartResult(float rootY, float velocityY, float peakRootY,
            bool returnRayHitExpectedGround, float returnRayDistance)
        {
            RootY = rootY;
            VelocityY = velocityY;
            PeakRootY = peakRootY;
            ReturnRayHitExpectedGround = returnRayHitExpectedGround;
            ReturnRayDistance = returnRayDistance;
        }

        internal float RootY { get; }
        internal float VelocityY { get; }
        internal float PeakRootY { get; }
        internal bool ReturnRayHitExpectedGround { get; }
        internal float ReturnRayDistance { get; }
    }
}
