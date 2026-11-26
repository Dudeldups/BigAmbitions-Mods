#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;
using PowerModifier = NWH.VehiclePhysics2.Powertrain.EngineComponent.PowerModifier;

/// <summary>Applies provisional AWD efficiency and telemetry-calibrated launch output without changing the factory engine rating.</summary>
[AddComponentMenu("")]
internal sealed class LamborghiniAventadorPowertrainCalibration : MonoBehaviour
{
    internal const float DrivelineEfficiency = 0.85f;
    internal const float EngineLossPercent = 0.22f;
    internal const float LaunchTorqueFloor = 0.78f;
    internal const float LaunchFadeStartKph = 80f;
    internal const float LaunchFadeEndKph = 120f;

    private static readonly HashSet<LamborghiniAventadorPowertrainCalibration> Instances =
        new HashSet<LamborghiniAventadorPowertrainCalibration>();

    private PhysicsVehicle? vehicle;
    private IList<PowerModifier>? engineModifiers;
    private ModContext? context;
    private PowerModifier? powerModifier;

    internal PowerModifier? AttachedPowerModifier => powerModifier;

    internal bool Initialize(PhysicsVehicle controller, ModContext? modContext)
    {
        Detach();
        vehicle = controller ?? throw new ArgumentNullException(nameof(controller));
        context = modContext;
        engineModifiers = controller.powertrain.engine.powerModifiers;
        if (engineModifiers == null)
            throw new InvalidOperationException("NWH EngineComponent is unavailable.");

        powerModifier = GetPowerModifier;
        Instances.Add(this);
        Attach();
        if (!IsAttached())
            throw new InvalidOperationException("NWH engine rejected the Aventador power modifier.");

        LamborghiniAventadorDiagnostics.PowertrainInfo(
            context,
            $"LamborghiniAventador powertrain calibration attached vehicle={controller.GetInstanceID()} " +
            $"driveline={DrivelineEfficiency:0.###} launchFloor={LaunchTorqueFloor:0.###} " +
            $"launchFade={LaunchFadeStartKph:0}-{LaunchFadeEndKph:0}km/h engineLoss={EngineLossPercent:0.###}.");
        return true;
    }

    internal static float LaunchTorqueMultiplier(float speedKph, float throttle, int gear)
    {
        if (gear <= 0)
            return 1f;

        var speedBlend = Mathf.SmoothStep(
            0f, 1f, Mathf.Clamp01((speedKph - LaunchFadeStartKph) /
                                  (LaunchFadeEndKph - LaunchFadeStartKph)));
        var loadBlend = Mathf.Clamp01((throttle - 0.6f) / 0.4f);
        return Mathf.Lerp(1f, Mathf.Lerp(LaunchTorqueFloor, 1f, speedBlend), loadBlend);
    }

    internal static float PowerModifierCoefficient(float speedKph, float throttle, int gear)
    {
        var desiredWheelEfficiency = DrivelineEfficiency *
                                    LaunchTorqueMultiplier(speedKph, throttle, gear);
        // NWH compensates the engine curve for engineLossPercent at full throttle.
        // Invert that compensation to obtain the requested net driveline efficiency.
        return (desiredWheelEfficiency + EngineLossPercent) / (1f + EngineLossPercent);
    }

    internal static void ReleaseAll()
    {
        foreach (var calibration in Instances.ToArray())
            if (calibration != null)
                calibration.Release();
    }

    private float GetPowerModifier()
    {
        var controller = vehicle;
        if (controller == null)
            return 1f;

        return PowerModifierCoefficient(
            controller.Speed * 3.6f,
            controller.input.Throttle,
            controller.powertrain.transmission.Gear);
    }

    private void Attach()
    {
        if (engineModifiers == null || powerModifier == null)
            return;

        if (!engineModifiers.Contains(powerModifier))
            engineModifiers.Add(powerModifier);
    }

    private void Detach()
    {
        if (engineModifiers != null && powerModifier != null)
            engineModifiers.Remove(powerModifier);
    }

    private bool IsAttached() =>
        engineModifiers != null && powerModifier != null && engineModifiers.Contains(powerModifier);

    private void OnEnable() => Attach();

    private void OnDisable() => Detach();

    internal void Release()
    {
        Detach();
        vehicle = null;
        engineModifiers = null;
        context = null;
        powerModifier = null;
        Instances.Remove(this);
    }

    private void OnDestroy() => Release();
}
