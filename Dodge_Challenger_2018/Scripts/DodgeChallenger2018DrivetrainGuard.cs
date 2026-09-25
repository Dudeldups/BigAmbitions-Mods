#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[AddComponentMenu("")]
public sealed class DodgeChallenger2018DrivetrainGuard : MonoBehaviour
{
    private const float MinimumHealthyRpm = 250f;
    private const float InvalidStateDelay = 0.35f;
    private const float MaximumRecoverySpeedMps = 4f;
    private const float InitialArmDelay = 2.0f;
    private const float RecoveryCooldown = 1.5f;

    // NWH slipTorque is clutch capacity, not the desired engine torque.
    // The current 755-kW power curve produces about 1,322 Nm (diagnostics
    // 2026-09-21). The legacy 1,100-Nm setup cannot transmit that through a
    // fully engaged clutch. 1,650 Nm provides ~25% healthy-clutch headroom
    // without using an effectively rigid/infinite-capacity clutch.
    // This is a simulation calibration, not an OEM gearbox torque rating.
    internal const float ClutchCapacityNm = 1650f;

    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;
    private Rigidbody? body;
    private float armedAt;
    private float invalidSince = -1f;
    private float nextRecoveryAllowed;
    private bool recovering;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        physics = controller.GetComponent<PhysicsVehicle>() ??
                  controller.GetComponentInChildren<PhysicsVehicle>(true);
        body = controller.GetComponent<Rigidbody>() ??
               controller.GetComponentInParent<Rigidbody>();

        // Runtime.TryConfigureVehicle calls Initialize immediately AFTER its
        // legacy ConfigurePowertrain pass. Apply the final clutch capacity here
        // once for every owned/dealer-spawned/reloaded Challenger. Do not write
        // it from FixedUpdate, and do not reset damage or command any RPM/gear.
        if (physics == null)
            throw new InvalidOperationException("Challenger NWH drivetrain is missing.");
        var previousCapacity = ApplyClutchCapacity(physics.powertrain);
        DodgeChallenger2018Diagnostics.Info(
            context,
            $"DodgeChallenger2018 CLUTCH_CAPACITY_V1 vehicle={controller.GetInstanceID()}: " +
            $"previous={previousCapacity:0.0}Nm, applied={ClutchCapacityNm:0.0}Nm, " +
            "readbackVerified=true, nativeAutomaticRetained=true, " +
            "enginePowerAndGearingUnchanged=true.");

        armedAt = Time.unscaledTime + InitialArmDelay;
        invalidSince = -1f;
    }

    // Required reflected members, like the existing runtime configuration, but
    // never silently ignore an unsupported game API. Reflection happens only
    // during initialization; the physics hot path remains unchanged.
    internal static float ApplyClutchCapacity(object powertrain)
    {
        if (powertrain == null)
            throw new ArgumentNullException(nameof(powertrain));
        var clutchMember = RequireMember(powertrain.GetType(), "clutch");
        var clutch = ReadMember(clutchMember, powertrain) ??
            throw new InvalidOperationException("Challenger NWH clutch is missing.");
        if (clutch.GetType().IsValueType)
            throw new InvalidOperationException("Unexpected value-type NWH clutch; capacity was not changed.");
        var capacityMember = RequireMember(clutch.GetType(), "slipTorque");
        if (ReadMember(capacityMember, clutch) is not float previous)
            throw new InvalidOperationException("NWH clutch.slipTorque is not a Single.");
        if (float.IsNaN(previous) || float.IsInfinity(previous) || previous < 0f)
            throw new InvalidOperationException("NWH clutch.slipTorque has an invalid initial value.");

        if (capacityMember is FieldInfo field && !field.IsInitOnly)
            field.SetValue(clutch, ClutchCapacityNm);
        else if (capacityMember is PropertyInfo property && property.GetSetMethod(true) != null)
            property.SetValue(clutch, ClutchCapacityNm, null);
        else
            throw new InvalidOperationException("NWH clutch.slipTorque is not writable.");

        if (ReadMember(capacityMember, clutch) is not float applied ||
            float.IsNaN(applied) || float.IsInfinity(applied) ||
            Math.Abs(applied - ClutchCapacityNm) > 0.01f)
        {
            throw new InvalidOperationException("NWH clutch capacity readback failed.");
        }
        return previous;
    }

    private static MemberInfo RequireMember(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var field = current.GetField(name, flags);
            if (field != null) return field;
            var property = current.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0 &&
                property.GetGetMethod(true) != null) return property;
        }
        throw new MissingMemberException(type.FullName, name);
    }

    private static object? ReadMember(MemberInfo member, object target) =>
        member is FieldInfo field ? field.GetValue(target) :
        ((PropertyInfo)member).GetValue(target, null);

    private void FixedUpdate()
    {
        if (recovering || vehicle == null || physics == null ||
            !vehicle.controlledByPlayer || !physics.enabled ||
            Time.unscaledTime < armedAt || Time.unscaledTime < nextRecoveryAllowed)
        {
            invalidSince = -1f;
            return;
        }

        var engine = physics.powertrain.engine;
        if (engine == null || !engine.canRun || !engine.ignition)
        {
            invalidSince = -1f;
            return;
        }

        var rpm = engine.RPMPercent * engine.revLimiterRPM;
        var transmission = physics.powertrain.transmission;

        var invalidRunningState = engine.IsRunning && rpm < MinimumHealthyRpm;
        if (!invalidRunningState)
        {
            invalidSince = -1f;
            return;
        }

        var speed = body != null ? body.velocity.magnitude : 0f;
        if (speed > MaximumRecoverySpeedMps)
        {
            invalidSince = -1f;
            return;
        }

        if (invalidSince < 0f)
        {
            invalidSince = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime - invalidSince < InvalidStateDelay)
            return;

        StartCoroutine(RecoverDrivetrain(rpm, speed));
    }

    private IEnumerator RecoverDrivetrain(float observedRpm, float observedSpeed)
    {
        recovering = true;
        invalidSince = -1f;
        nextRecoveryAllowed = Time.unscaledTime + RecoveryCooldown;

        var engine = physics!.powertrain.engine;
        var transmission = physics.powertrain.transmission;

        DodgeChallenger2018Diagnostics.DealerEntryInfo(
            context,
            $"DodgeChallenger2018 drivetrain recovery: invalid running state " +
            $"rpm={observedRpm:0}, speed={observedSpeed:0.00}mps, " +
            $"gear={transmission.Gear}, ratio={transmission.currentGearRatio:0.000}.");

        transmission.ShiftInto(0, true);
        transmission.currentGearRatio = 0f;
        yield return new WaitForSecondsRealtime(0.08f);

        engine.StopEngine();
        yield return new WaitForSecondsRealtime(0.08f);

        if (vehicle == null || !vehicle.controlledByPlayer)
        {
            recovering = false;
            yield break;
        }

        engine.StartEngine();
        yield return new WaitForSecondsRealtime(0.55f);

        if (vehicle != null && vehicle.controlledByPlayer)
        {
            transmission.ShiftInto(1, true);
            yield return new WaitForFixedUpdate();
            body?.WakeUp();

            var recoveredRpm = engine.RPMPercent * engine.revLimiterRPM;
            DodgeChallenger2018Diagnostics.DealerEntryInfo(
                context,
                $"DodgeChallenger2018 drivetrain recovery completed " +
                $"running={engine.IsRunning}, ignition={engine.ignition}, " +
                $"rpm={recoveredRpm:0}, gear={transmission.Gear}, " +
                $"ratio={transmission.currentGearRatio:0.000}.");
        }

        recovering = false;
    }
}
