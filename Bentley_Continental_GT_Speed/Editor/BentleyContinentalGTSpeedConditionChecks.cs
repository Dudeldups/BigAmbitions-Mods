#nullable enable
using System;
using System.Reflection;
using NWH.VehiclePhysics2.Damage;
using UnityEngine;
using Vehicles.VehicleTypes;

/// <summary>Checks Bentley's native condition-damage tuning without starting the game.</summary>
public static class BentleyContinentalGTSpeedConditionChecks
{
    private const float DamageIntensityTarget = 0.80f;
    private const float NativeDamageThreshold = 200f;
    private const float MaximumNativeDamageIntensity = 0.99f;
    private const float NativeDamageScale = 0.005f;
    private const float FixedDeltaTime = 0.02f;

    public static void Verify(GameObject prefab, VehicleType vehicleType)
    {
        if (prefab == null)
            throw new ArgumentNullException(nameof(prefab));
        if (vehicleType == null)
            throw new ArgumentNullException(nameof(vehicleType));

        var damageHandler = prefab.GetComponentInChildren<DamageHandler>(true);
        if (damageHandler == null)
            throw new InvalidOperationException("Bentley prefab has no native NWH DamageHandler.");

        if (!Mathf.Approximately(vehicleType.damageIntensity, DamageIntensityTarget) ||
            !Mathf.Approximately(damageHandler.damageIntensity, DamageIntensityTarget))
            throw new InvalidOperationException(
                "Bentley VehicleType and NWH DamageHandler must both use damageIntensity=" +
                DamageIntensityTarget.ToString("0.##") + ".");
        if (!damageHandler.enabled || damageHandler.visualOnly || damageHandler.meshDeform ||
            !Mathf.Approximately(damageHandler.decelerationThreshold, NativeDamageThreshold))
            throw new InvalidOperationException(
                "Bentley native condition damage must stay enabled, use the 2 m/s threshold, and leave mesh deformation to the mod controller.");

        var enqueue = typeof(DamageHandler).GetMethod(
            "Enqueue",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(UnityEngine.Collision), typeof(float) },
            modifiers: null);
        if (enqueue == null || enqueue.ReturnType != typeof(bool))
            throw new InvalidOperationException(
                "NWH DamageHandler.Enqueue(Collision, Single) is unavailable; review native damage formula before updating this check.");

        // Installed ExternalPlugins.dll DamageHandler.Enqueue computes
        // impulse / (fixedDeltaTime * mass * 10) * intensity * 0.005.
        // impulse / (fixedDeltaTime * mass) has acceleration units. For a
        // hypothetical impulse equal to mass * bodyDeltaV, the result is
        // bodyDeltaV / fixedDeltaTime * intensity * 0.0005. This validates
        // native arithmetic only: collision impulse is not determined by
        // relative speed alone because the contact solver and other bodies
        // affect it.
        var effectiveIntensity = Mathf.Min(damageHandler.damageIntensity, MaximumNativeDamageIntensity);
        var rawDamagePer10Mps = ComputeRawNativeDamageDelta(10f, effectiveIntensity, FixedDeltaTime);
        var rawDamagePer30Mps = ComputeRawNativeDamageDelta(30f, effectiveIntensity, FixedDeltaTime);
        var damagePer10MpsAtBaselineIntensity = ComputeRawNativeDamageDelta(10f, 0.5f, FixedDeltaTime);
        var cleanFinalDamageAtTenMps = Mathf.Clamp01(rawDamagePer10Mps);
        var finalDamageAtTenMpsFrom742 = Mathf.Clamp01(0.742f + rawDamagePer10Mps);
        var finalDamageAtThirtyMpsFrom742 = Mathf.Clamp01(0.742f + rawDamagePer30Mps);
        if (Mathf.Abs(rawDamagePer10Mps - 0.20f) > 0.000001f ||
            Mathf.Abs(rawDamagePer30Mps - 0.60f) > 0.000001f ||
            Mathf.Abs(damagePer10MpsAtBaselineIntensity - 0.125f) > 0.000001f ||
            Mathf.Abs(cleanFinalDamageAtTenMps - 0.20f) > 0.000001f ||
            Mathf.Abs(finalDamageAtTenMpsFrom742 - 0.942f) > 0.000001f ||
            !Mathf.Approximately(finalDamageAtThirtyMpsFrom742, 1f) ||
            Mathf.Abs(effectiveIntensity / 0.5f - 1.6f) > 0.0001f)
            throw new InvalidOperationException(
                "Bentley native damage scaling no longer matches the audited NWH impulse formula.");

        Debug.Log(
            "Bentley native condition damage: " +
            $"intensity={effectiveIntensity:0.##}, hypothetical 10 m/s body delta-v raw damage={rawDamagePer10Mps:P2} " +
            $"(from clean={cleanFinalDamageAtTenMps:P2}, from .742={finalDamageAtTenMpsFrom742:P2}); " +
            $"30 m/s raw damage={rawDamagePer30Mps:P2} (from .742 clamps to {finalDamageAtThirtyMpsFrom742:P0}) " +
            $"at fixedDeltaTime={FixedDeltaTime:0.###}; native intensity .5 would produce {damagePer10MpsAtBaselineIntensity:P2} " +
            "for the same hypothetical impulse. Reflection confirms the native Enqueue signature; invocation requires a physics-created Collision.");
    }

    private static float ComputeRawNativeDamageDelta(float bodyDeltaVelocity, float intensity, float fixedDeltaTime)
    {
        // The hypothetical impulse is bodyMass * bodyDeltaVelocity, so mass cancels.
        return bodyDeltaVelocity / (fixedDeltaTime * 10f) * intensity * NativeDamageScale;
    }
}
