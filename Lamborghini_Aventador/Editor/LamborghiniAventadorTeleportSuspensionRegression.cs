#nullable enable
using System;
using System.Reflection;
using NWH.WheelController3D;
using UnityEditor;
using UnityEngine;

/// <summary>Focused regression checks for teleport spring-history rebasing.</summary>
public static class LamborghiniAventadorTeleportSuspensionRegression
{
    private const string PrefabPath = "Assets/Mods/Lamborghini_Aventador/LamborghiniAventador.prefab";
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    [MenuItem("Big Ambitions Mods/Lamborghini Aventador/Verify Teleport Suspension Reset")]
    public static void VerifyTeleportSuspensionReset()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Aventador teleport regression could not load its prefab.");

        var instance = UnityEngine.Object.Instantiate(prefab);
        var guardHost = new GameObject("AventadorTeleportSuspensionRegressionGuard");
        try
        {
            var guardType = typeof(LamborghiniAventadorMod).Assembly
                .GetType("LamborghiniAventadorAutoParkingGuard", throwOnError: true)!;
            var guard = guardHost.AddComponent(guardType);
            var currentField = guardType.GetField("current", PrivateStatic)!;
            currentField.SetValue(null, guard);
            guardType.GetMethod("InstallNativeCompletionHook", PrivateInstance)!.Invoke(guard, null);
            if (guardType.GetField("harmony", PrivateInstance)!.GetValue(guard) == null)
                throw new InvalidOperationException(
                    "Aventador teleport regression could not install the runtime Harmony hooks.");

            var pendingField = guardType.GetField("pendingTeleportDampers", PrivateInstance)!;
            var pending = pendingField.GetValue(guard)!;
            var pendingType = pending.GetType();
            var addPending = pendingType.GetMethod("Add")!;
            var arm = new Action<WheelController>(wheel =>
            {
                pendingType.GetMethod("Remove")!.Invoke(pending, new object[] { wheel.damper });
                addPending.Invoke(pending, new object[] { wheel.damper, wheel });
            });
            var stepPostfix = guardType.GetMethod("AfterNativeWheelStep", PrivateStatic)!;
            var damperMethod = typeof(Damper).GetMethod("CalculateDamperForce",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: new[] { typeof(float).MakeByRefType() }, modifiers: null)
                ?? throw new MissingMethodException("Native Damper.CalculateDamperForce(ref float) was not found.");

            var wheels = instance.GetComponentsInChildren<WheelController>(true);
            if (wheels.Length != 4)
                throw new InvalidOperationException(
                    $"Aventador teleport regression expected four wheel controllers, found {wheels.Length}.");

            foreach (var wheel in wheels)
                SetDamperRates(wheel.damper);

            var rebasedWheel = wheels[0];
            rebasedWheel.spring.length = 0.041f;
            rebasedWheel.spring.prevLength = 0.065f;
            rebasedWheel.spring.prevVelocity = 7f;
            rebasedWheel.spring.compressionVelocity = 15f;
            rebasedWheel.spring.force = 1234f;
            arm(rebasedWheel);

            // This invokes the actual NWH method after installing the runtime
            // Harmony patch. The changed ref argument proves the prefix is active.
            var firstArguments = new object?[] { 15f };
            var firstForce = (float)damperMethod.Invoke(rebasedWheel.damper, firstArguments)!;
            var firstVelocity = (float)firstArguments[0]!;
            AssertNear(firstVelocity, 0f, "first post-teleport damper sample was not zeroed");
            AssertNear(firstForce, 0f, "rebased native damper force was not zero");
            AssertNear(rebasedWheel.spring.prevLength, rebasedWheel.spring.length,
                "spring history did not rebase to the first post-teleport length");
            AssertNear(rebasedWheel.spring.prevVelocity, 0f, "previous spring velocity was not cleared");
            AssertNear(rebasedWheel.spring.force, 1234f, "reset changed the physical spring force");

            var secondArguments = new object?[] { 6f };
            var secondForce = (float)damperMethod.Invoke(rebasedWheel.damper, secondArguments)!;
            AssertGreaterThanZero(secondForce, "one-shot reset intercepted a second damper sample");

            // A different wheel remains untouched until it has its own teleport event.
            var otherArguments = new object?[] { 9f };
            var otherForce = (float)damperMethod.Invoke(wheels[1].damper, otherArguments)!;
            AssertGreaterThanZero(otherForce,
                "unmarked wheel did not retain its normal positive damping force");

            // Repeated teleports re-arm that wheel's one-shot reset.
            arm(rebasedWheel);
            var repeatedArguments = new object?[] { 11f };
            var repeatedForce = (float)damperMethod.Invoke(rebasedWheel.damper, repeatedArguments)!;
            AssertNear((float)repeatedArguments[0]!, 0f,
                "a repeated teleport did not re-arm the one-shot reset");
            AssertNear(repeatedForce, 0f, "a repeated teleport retained a damping impulse");

            // Airborne first step: no damper call occurs, so the step postfix must
            // expire the marker and leave the later landing's real velocity alone.
            var airborneWheel = wheels[2];
            airborneWheel.spring.length = 0.052f;
            airborneWheel.spring.prevLength = 0.070f;
            airborneWheel.spring.compressionVelocity = 4f;
            airborneWheel.spring.prevVelocity = 4f;
            arm(airborneWheel);
            airborneWheel.spring.length = 0.058f; // fresh length produced by its first Step
            stepPostfix.Invoke(null, new object?[] { airborneWheel });
            AssertNear(airborneWheel.spring.prevLength, 0.058f,
                "airborne first step did not rebase to its fresh spring length");
            AssertNear(airborneWheel.spring.compressionVelocity, 0f,
                "airborne first step left a false compression velocity");
            var landingArguments = new object?[] { -5f };
            var landingForce = (float)damperMethod.Invoke(airborneWheel.damper, landingArguments)!;
            AssertNear((float)landingArguments[0]!, -5f,
                "airborne expiry suppressed damping on the later physical landing");
            AssertGreaterThanZero(Mathf.Abs(landingForce),
                "airborne expiry suppressed the real landing's rebound damping");

            Debug.Log("Aventador teleport suspension regression passed: grounded one-shot consumption, preserved spring force, unaffected wheel, repeated teleport re-arming, and airborne expiry before real landing.");
        }
        finally
        {
            var guardType = typeof(LamborghiniAventadorMod).Assembly
                .GetType("LamborghiniAventadorAutoParkingGuard", throwOnError: true)!;
            var guard = guardHost.GetComponent(guardType);
            if (guard != null)
                guardType.GetMethod("UninstallNativeCompletionHook", PrivateInstance)?.Invoke(guard, null);
            guardType.GetField("current", PrivateStatic)!.SetValue(null, null);
            UnityEngine.Object.DestroyImmediate(guardHost);
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void SetDamperRates(Damper damper)
    {
        damper.bumpRate = 1f;
        damper.reboundRate = 1f;
        damper.slowBump = 1200f;
        damper.fastBump = 1200f;
        damper.bumpDivisionVelocity = 0.1f;
        damper.slowRebound = 1200f;
        damper.fastRebound = 1200f;
        damper.reboundDivisionVelocity = 0.1f;
    }

    private static void AssertGreaterThanZero(float actual, string message)
    {
        if (float.IsNaN(actual) || float.IsInfinity(actual) || actual <= 0.01f)
            throw new InvalidOperationException(
                $"Aventador teleport regression {message}: expected > 0.01, actual={actual:0.0000}.");
    }

    private static void AssertNear(float actual, float expected, string message)
    {
        if (float.IsNaN(actual) || float.IsInfinity(actual) || Mathf.Abs(actual - expected) > 0.0001f)
            throw new InvalidOperationException(
                $"Aventador teleport regression {message}: expected={expected:0.0000}, actual={actual:0.0000}.");
    }
}
