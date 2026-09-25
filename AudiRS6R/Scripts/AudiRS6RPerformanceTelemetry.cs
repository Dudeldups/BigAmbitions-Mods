#nullable enable
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

[AddComponentMenu("")]
internal sealed class AudiRS6RPerformanceTelemetry : MonoBehaviour
{
    private const float StartSpeedKph = 2f;
    private const float StartThrottle = 0.92f;
    private const float AbortThrottle = 0.70f;
    private const float StartBrake = 0.92f;
    private const float AbortBrake = 0.65f;
    private const float BrakeArmSpeedKph = 105f;
    private const float StopSpeedKph = 2f;
    private const float MaximumYawDegrees = 6f;
    private const float MaximumLateralMetres = 3f;
    private const float MaximumRunSeconds = 45f;
    private const float TargetZeroToHundredSeconds = 3.3f;
    private const float TargetZeroToTwoHundredSeconds = 10.4f;
    private const float TargetHundredToZeroMetres = 37.5f;

    private static readonly float[] AccelerationMilestonesKph = { 100f, 200f };

    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private ModContext? context;
    private Vector3 runStartPosition;
    private Vector3 runForward;
    private Vector3 brakingStartPosition;
    private float previousThrottle;
    private float previousBrake;
    private float previousSpeedKph;
    private float elapsed;
    private float maximumYaw;
    private float maximumLateral;
    private int nextAccelerationMilestone;
    private bool accelerating;
    private bool braking;
    private bool brakingFromHundred;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (vehicle == controller && physics != null && body != null)
            return;

        vehicle = controller;
        physics = controller.GetComponent<PhysicsVehicle>();
        body = controller.GetComponent<Rigidbody>() ?? controller.GetComponentInParent<Rigidbody>();
        context = modContext;
        context?.Logger.Info(
            $"AudiRS6R performance telemetry ready vehicle={controller.GetInstanceID()}, " +
            $"targets=0-100:{TargetZeroToHundredSeconds:0.0}s, " +
            $"0-200:{TargetZeroToTwoHundredSeconds:0.0}s, " +
            $"100-0:{TargetHundredToZeroMetres:0.0}m.");
    }

    private void FixedUpdate()
    {
        if (vehicle == null || physics == null || body == null)
            return;

        var throttle = Mathf.Clamp01(physics.input.Throttle);
        var brake = Mathf.Clamp01(physics.input.Brakes);
        var speedKph = body.velocity.magnitude * 3.6f;
        if (!vehicle.controlledByPlayer)
        {
            FinishActiveRun("driver-exited", speedKph);
            StorePrevious(throttle, brake, speedKph);
            return;
        }

        if (!accelerating && !braking)
        {
            if (speedKph <= StartSpeedKph && previousThrottle < StartThrottle &&
                throttle >= StartThrottle)
            {
                StartAcceleration();
            }
            else if (speedKph >= BrakeArmSpeedKph && previousBrake < StartBrake &&
                     brake >= StartBrake)
            {
                StartBraking();
            }

            StorePrevious(throttle, brake, speedKph);
            return;
        }

        elapsed += Time.fixedDeltaTime;
        UpdateStraightness();
        if (maximumYaw > MaximumYawDegrees || maximumLateral > MaximumLateralMetres)
        {
            FinishActiveRun("not-straight", speedKph);
        }
        else if (elapsed >= MaximumRunSeconds)
        {
            FinishActiveRun("timeout", speedKph);
        }
        else if (accelerating)
        {
            if (throttle < AbortThrottle)
                FinishAcceleration("throttle-released", speedKph);
            else
                CaptureAccelerationMilestones(speedKph);
        }
        else if (brake < AbortBrake)
        {
            FinishBraking("brake-released", speedKph);
        }
        else
        {
            CaptureBraking(speedKph);
        }

        StorePrevious(throttle, brake, speedKph);
    }

    private void StartAcceleration()
    {
        if (!TryStartStraightRun())
            return;
        accelerating = true;
        nextAccelerationMilestone = 0;
        context?.Logger.Info(
            $"AudiRS6R acceleration run started vehicle={vehicle!.GetInstanceID()}; " +
            "hold full throttle on a flat straight.");
    }

    private void CaptureAccelerationMilestones(float speedKph)
    {
        while (nextAccelerationMilestone < AccelerationMilestonesKph.Length &&
               speedKph >= AccelerationMilestonesKph[nextAccelerationMilestone])
        {
            var targetSpeed = AccelerationMilestonesKph[nextAccelerationMilestone];
            var speedSpan = speedKph - previousSpeedKph;
            var fraction = speedSpan > 0.001f
                ? Mathf.Clamp01((targetSpeed - previousSpeedKph) / speedSpan)
                : 1f;
            var milestoneTime = elapsed - Time.fixedDeltaTime + Time.fixedDeltaTime * fraction;
            var targetTime = nextAccelerationMilestone == 0
                ? TargetZeroToHundredSeconds
                : TargetZeroToTwoHundredSeconds;
            context?.Logger.Info(
                $"AudiRS6R acceleration milestone vehicle={vehicle!.GetInstanceID()}, " +
                $"0to{targetSpeed:0}={milestoneTime:0.000}s, target={targetTime:0.0}s, " +
                $"delta={milestoneTime - targetTime:+0.000;-0.000;0.000}s.");
            nextAccelerationMilestone++;
        }

        if (nextAccelerationMilestone == AccelerationMilestonesKph.Length)
            FinishAcceleration("200kmh-complete", speedKph);
    }

    private void StartBraking()
    {
        if (!TryStartStraightRun())
            return;
        braking = true;
        brakingFromHundred = false;
        context?.Logger.Info(
            $"AudiRS6R braking run armed vehicle={vehicle!.GetInstanceID()}, " +
            $"speed={previousSpeedKph:0.0}kmh; keep full brake applied.");
    }

    private void CaptureBraking(float speedKph)
    {
        if (!brakingFromHundred && previousSpeedKph >= 100f && speedKph <= 100f)
        {
            brakingFromHundred = true;
            brakingStartPosition = body!.position;
            elapsed = 0f;
        }

        if (!brakingFromHundred || speedKph > StopSpeedKph)
            return;

        var distance = Vector3.ProjectOnPlane(body!.position - brakingStartPosition, Vector3.up).magnitude;
        context?.Logger.Info(
            $"AudiRS6R braking result vehicle={vehicle!.GetInstanceID()}, " +
            $"100to0={distance:0.00}m in {elapsed:0.000}s, " +
            $"target={TargetHundredToZeroMetres:0.0}m, " +
            $"delta={distance - TargetHundredToZeroMetres:+0.00;-0.00;0.00}m.");
        FinishBraking("stopped", speedKph);
    }

    private bool TryStartStraightRun()
    {
        var forward = Vector3.ProjectOnPlane(vehicle!.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.5f)
            return false;
        runStartPosition = body!.position;
        runForward = forward;
        elapsed = maximumYaw = maximumLateral = 0f;
        return true;
    }

    private void UpdateStraightness()
    {
        var displacement = Vector3.ProjectOnPlane(body!.position - runStartPosition, Vector3.up);
        var longitudinal = Vector3.Dot(displacement, runForward);
        maximumLateral = Mathf.Max(
            maximumLateral,
            (displacement - runForward * longitudinal).magnitude);
        var forward = Vector3.ProjectOnPlane(vehicle!.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude > 0.5f)
            maximumYaw = Mathf.Max(maximumYaw, Vector3.Angle(runForward, forward));
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (accelerating)
            FinishAcceleration("collision", body == null ? 0f : body.velocity.magnitude * 3.6f);
        if (braking)
            FinishBraking("collision", body == null ? 0f : body.velocity.magnitude * 3.6f);
    }

    private void FinishActiveRun(string reason, float speedKph)
    {
        if (accelerating)
            FinishAcceleration(reason, speedKph);
        if (braking)
            FinishBraking(reason, speedKph);
    }

    private void FinishAcceleration(string reason, float speedKph)
    {
        context?.Logger.Info(
            $"AudiRS6R acceleration run ended vehicle={vehicle?.GetInstanceID()}, " +
            $"reason={reason}, elapsed={elapsed:0.000}s, speed={speedKph:0.0}kmh, " +
            $"milestones={nextAccelerationMilestone}/{AccelerationMilestonesKph.Length}.");
        accelerating = false;
    }

    private void FinishBraking(string reason, float speedKph)
    {
        context?.Logger.Info(
            $"AudiRS6R braking run ended vehicle={vehicle?.GetInstanceID()}, " +
            $"reason={reason}, elapsed={elapsed:0.000}s, speed={speedKph:0.0}kmh, " +
            $"crossed100={brakingFromHundred}.");
        braking = false;
        brakingFromHundred = false;
    }

    private void StorePrevious(float throttle, float brake, float speedKph)
    {
        previousThrottle = throttle;
        previousBrake = brake;
        previousSpeedKph = speedKph;
    }

    private void OnDisable()
    {
        FinishActiveRun("component-disabled", body == null ? 0f : body.velocity.magnitude * 3.6f);
        previousThrottle = previousBrake = previousSpeedKph = 0f;
    }
}
