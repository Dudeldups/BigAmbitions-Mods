#nullable enable
using System;
using BAModAPI;
using NWH.Common.Vehicles;
using NWH.VehiclePhysics2.Powertrain;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

// Native engine callback: continuous physics, no state discovery or frame polling.
internal sealed class MercedesC63CabrioTractionController : MonoBehaviour
{
    private PhysicsVehicle? physics;
    private WheelUAPI[] drivenWheels = Array.Empty<WheelUAPI>();
    private EngineComponent.PowerModifier? modifier;
    private NWH.VehiclePhysics2.Modules.TCS.TCSModule? nativeTcs;
    private float originalNativeThreshold;
    private ModContext? context;
    private float gain = 1f;
    private bool limiting;
    private float nextDiagnostic;

    internal void Initialize(PhysicsVehicle controller, ModContext? modContext)
    {
        if (physics != null) return;
        physics = controller; context = modContext;
        nativeTcs = controller.GetComponent<NWH.VehiclePhysics2.Modules.TCS.TCSModuleWrapper>()?.module;
        originalNativeThreshold = .18f; // safe original cut threshold on teardown/failure
        var rear = new System.Collections.Generic.List<WheelUAPI>(2);
        foreach (var wheel in physics.powertrain.wheels)
            if (wheel.wheelGroupSelector.index == 1) rear.Add(wheel.wheelUAPI);
        drivenWheels = rear.ToArray();
        if (drivenWheels.Length != 2)
        {
            if (nativeTcs != null) nativeTcs.slipThreshold = originalNativeThreshold;
            context?.Logger.Warn("MercedesC63Cabrio traction: expected two rear driven wheels; original native TCS restored.");
            return;
        }
        modifier = LimitPower;
        if (isActiveAndEnabled)
        {
            physics.powertrain.engine.powerModifiers.Insert(0, modifier);
            if (nativeTcs != null) nativeTcs.slipThreshold = MercedesC63CabrioTuning.TractionSlipThreshold;
        }
        MercedesC63CabrioDiagnostics.Info(context,
            "MercedesC63Cabrio progressive traction initialized: rear wheels=2, slip=0.13..0.32; progressive-only first-gear launch below 3 m/s.");
    }

    private float LimitPower()
    {
        if (physics == null || drivenWheels.Length != 2) return 1f;
        var transmission = physics.powertrain.transmission;
        // The engine invokes modifiers in list order. Set the native threshold
        // before its binary limiter runs, using the same physics sample.
        if (nativeTcs != null)
        {
            var threshold = NativeThreshold(transmission.Gear, physics.LocalForwardVelocity);
            if (nativeTcs.slipThreshold != threshold)
            {
                nativeTcs.slipThreshold = threshold;
                MercedesC63CabrioDiagnostics.PerformanceInfo(context,
                    $"MercedesC63Cabrio traction vehicle={GetInstanceID()} gear={transmission.Gear}, " +
                    $"speed={physics.Speed:0.00} m/s, nativeThreshold={threshold:0.00}.");
            }
        }
        var direction = Mathf.Sign(physics.LocalForwardVelocity);
        var slip = 0f;
        foreach (var wheel in drivenWheels)
            if (wheel.IsGrounded) slip = Mathf.Max(slip, -wheel.LongitudinalSlip * direction);
        var target = transmission.isShifting ? 1f : TargetGain(slip);
        gain = FollowGain(gain, target, Time.fixedDeltaTime);
        var next = gain < .97f;
        if (next != limiting && Time.unscaledTime >= nextDiagnostic)
        {
            limiting = next; nextDiagnostic = Time.unscaledTime + 1f;
            MercedesC63CabrioDiagnostics.PerformanceInfo(context,
                $"MercedesC63Cabrio traction vehicle={GetInstanceID()} limiting={limiting}, slip={slip:0.000}, gain={gain:0.000}.");
        }
        return gain * MercedesC63CabrioTuning.DrivetrainEfficiency;
    }

    internal static float NativeThreshold(int gear, float forwardSpeed) =>
        gear == 1 && forwardSpeed >= 0f && forwardSpeed < 3f
            ? float.PositiveInfinity : MercedesC63CabrioTuning.TractionSlipThreshold;

    internal static float TargetGain(float slip) =>
        Mathf.Lerp(1f, .20f, Mathf.InverseLerp(MercedesC63CabrioTuning.ProgressiveSlipStart,
            MercedesC63CabrioTuning.ProgressiveSlipEnd, slip));
    internal static float FollowGain(float current, float target, float dt) =>
        Mathf.Lerp(current, target, 1f - Mathf.Exp(-Mathf.Max(0f, dt) / (target < current ? .08f : .16f)));

    private void OnEnable()
    {
        gain = 1f; limiting = false;
        if (physics != null && modifier != null && !physics.powertrain.engine.powerModifiers.Contains(modifier))
        {
            physics.powertrain.engine.powerModifiers.Insert(0, modifier);
            if (nativeTcs != null) nativeTcs.slipThreshold = MercedesC63CabrioTuning.TractionSlipThreshold;
        }
    }
    private void OnDestroy() => OnDisable();

    private void OnDisable()
    {
        if (physics != null && modifier != null) physics.powertrain.engine.powerModifiers.Remove(modifier);
        if (nativeTcs != null && (nativeTcs.slipThreshold == MercedesC63CabrioTuning.TractionSlipThreshold ||
            float.IsPositiveInfinity(nativeTcs.slipThreshold)))
            nativeTcs.slipThreshold = originalNativeThreshold;
        gain = 1f; limiting = false;
    }
}
