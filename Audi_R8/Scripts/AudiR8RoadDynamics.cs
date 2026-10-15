#nullable enable
using System;
using System.Collections;
using BAModAPI;
using NWH.VehiclePhysics2.Powertrain;
using NWH.WheelController3D;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

// Calibrates actuator force against the 2026-10-12 telemetry, not road speed.
// RPM, the 456 kW engine curve, real ratios and mass remain authoritative.
internal sealed class AudiR8RoadDynamics : MonoBehaviour
{
    private const float AirDensity = 1.225f;
    private const float DragArea = .7236f; // Audi Cd=.36, frontal area=2.01 m².
    internal const float ReverseLimitKph = 40f; // Gameplay limit, not an Audi specification.
    internal static float ReversePropulsionScale(float reverseSpeedMps) =>
        1f - Mathf.InverseLerp(37f / 3.6f, ReverseLimitKph / 3.6f, reverseSpeedMps);
    private WheelController[] wheels = Array.Empty<WheelController>();
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private EngineComponent.CalculateTorque? originalTorque, calibratedTorque;
    private ModContext? context;
    private Coroutine? bindingCoroutine;

    internal void Initialize(PhysicsVehicle controller, ModContext? modContext)
    {
        if (physics != null) return;
        physics = controller; body = controller.GetComponent<Rigidbody>(); context = modContext;
        wheels = controller.GetComponentsInChildren<WheelController>(true);
        controller.brakes.onBrakesActivate.AddListener(OnBraking);
        controller.brakes.onBrakesDeactivate.AddListener(OnBrakeReleased);
        SetGrip(controller.brakes.IsBraking);
        controller.onVehicleInitialized.AddListener(ScheduleBinding);
        controller.onEnable.AddListener(ScheduleBinding);
        ScheduleBinding();
    }

    private void ScheduleBinding()
    {
        if (!isActiveAndEnabled) return;
        if (bindingCoroutine != null) StopCoroutine(bindingCoroutine);
        bindingCoroutine = StartCoroutine(BindAfterNativeLifecycle());
    }
    private IEnumerator BindAfterNativeLifecycle()
    {
        // Native VC_Initialize assigns the engine delegate. Bind after every
        // initialization/activation callback has finished, never from a poll.
        yield return null;
        bindingCoroutine = null;
        BindTorque();
        if (physics != null) SetGrip(physics.brakes.IsBraking);
    }

    private void OnBraking() => SetGrip(true);
    private void OnBrakeReleased() => SetGrip(false);
    private void SetGrip(bool braking)
    {
        foreach (var wheel in wheels)
        {
            var front = physics!.transform.InverseTransformPoint(wheel.transform.position).z > 0f;
            // The game's brake target deliberately exceeds the real launch
            // adhesion. Keep that allowance confined to an active brake event.
            wheel.forwardFriction.grip = braking ? (front ? .56f : .58f) : (front ? .40f : .415f);
        }
    }

    private void BindTorque()
    {
        if (physics == null) return;
        if (physics.powertrain.engine.calculateTorqueDelegate == calibratedTorque && calibratedTorque != null) return;
        originalTorque = physics.powertrain.engine.calculateTorqueDelegate;
        if (originalTorque == null)
        { context?.Logger.Warn("AudiR8: native engine torque delegate missing; road-force calibration not bound."); return; }
        calibratedTorque ??= CalibrateTorque;
        physics.powertrain.engine.calculateTorqueDelegate = calibratedTorque;
        AudiR8Diagnostics.DrivetrainInfo(context,
            $"AudiR8 road dynamics vehicle={physics.GetInstanceID()}: torque calibration bound after native lifecycle; gear 1/2/3-6/7 scales=.90/.88/.80/.90, CdA={DragArea}, braking target=26.4m from 100km/h.");
    }

    private float CalibrateTorque(float angularVelocity, float deltaTime)
    {
        var torque = originalTorque!(angularVelocity, deltaTime);
        var engine = physics!.powertrain.engine;
        // Preserve start/idle regulation and coast/engine braking. This changes
        // propulsion force only; it never writes velocity, gear or driver input.
        var gear = physics.powertrain.transmission.Gear;
        if (gear < 0 && torque > 0f && engine.IsRunning && !engine.starterActive && body != null)
            return torque * .90f * ReversePropulsionScale(
                Mathf.Max(0f, -Vector3.Dot(body.velocity, transform.forward)));
        var deliveredScale = gear <= 1 ? .90f : gear == 2 ? .88f : gear >= 7 ? .90f : .80f;
        return torque > 0f && engine.IsRunning && !engine.starterActive &&
               physics.input.InputSwappedThrottle > .25f ? torque * deliveredScale : torque;
    }

    private void FixedUpdate()
    {
        if (body == null || body.isKinematic) return;
        var speed = body.velocity.magnitude;
        if (speed < .5f) return;
        var drag = .5f * AirDensity * DragArea * speed * speed;
        body.AddForce(-body.velocity / speed * drag, ForceMode.Force);
        // A smooth opposing force also handles overshoot/downhill while reverse
        // propulsion is requested. Do not rewrite velocity or brake forward gears.
        if (physics != null && physics.powertrain.transmission.Gear < 0 &&
            physics.powertrain.engine.IsRunning && physics.input.InputSwappedThrottle > .1f)
        {
            var excess = -Vector3.Dot(body.velocity, transform.forward) - ReverseLimitKph / 3.6f;
            if (excess > 0f)
                body.AddForce(transform.forward * body.mass * Mathf.Min(4f, excess / .25f), ForceMode.Force);
        }
    }

    private void OnDestroy()
    {
        if (physics == null) return;
        physics.onVehicleInitialized.RemoveListener(ScheduleBinding);
        physics.onEnable.RemoveListener(ScheduleBinding);
        physics.brakes.onBrakesActivate.RemoveListener(OnBraking);
        physics.brakes.onBrakesDeactivate.RemoveListener(OnBrakeReleased);
        var engine = physics.powertrain.engine;
        if (engine.calculateTorqueDelegate == calibratedTorque)
            engine.calculateTorqueDelegate = originalTorque;
    }
}
