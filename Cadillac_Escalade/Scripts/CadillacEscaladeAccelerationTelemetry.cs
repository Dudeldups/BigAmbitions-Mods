#nullable enable
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

internal sealed class CadillacEscaladeAccelerationTelemetry : MonoBehaviour
{
    private const float StartSpeedKph = 2f;
    private const float StartThrottle = 0.92f;
    private const float AbortThrottle = 0.70f;
    private const float MaximumYawDegrees = 5f;
    private const float MaximumLateralMetres = 3f;
    private const float MaximumRunSeconds = 45f;
    private const float OfficialZeroToHundredSeconds = 7.3f;
    private const float DiagnosticSnapshotSeconds = 2f;

    private static readonly float[] MilestonesKph = { 50f, 100f, 160f };

    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private Rigidbody? body;
    private ModContext? context;
    private Vector3 startPosition;
    private Vector3 startForward;
    private float previousThrottle;
    private float previousSpeedKph;
    private float elapsed;
    private float maximumYaw;
    private float maximumLateral;
    private int nextMilestone;
    private bool running;
    private bool diagnosticSnapshotLogged;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        enabled = CadillacEscaladeDiagnostics.AccelerationTelemetryEnabled;
        if (!enabled)
            return;

        vehicle = controller;
        physics = controller.GetComponent<PhysicsVehicle>();
        body = controller.GetComponent<Rigidbody>() ?? controller.GetComponentInParent<Rigidbody>();
        context = modContext;
        CadillacEscaladeDiagnostics.TelemetryInfo(context,
            $"CadillacEscalade acceleration telemetry ready vehicle={controller.GetInstanceID()}, " +
            $"official0to100={OfficialZeroToHundredSeconds:0.0}s, " +
            "milestones=50/100/160kmh.");
    }

    private void FixedUpdate()
    {
        if (vehicle == null || physics == null || body == null)
            return;

        var throttle = Mathf.Clamp01(physics.input.Throttle);
        var speedKph = body.velocity.magnitude * 3.6f;
        if (!vehicle.controlledByPlayer)
        {
            if (running)
                Finish("driver-exited", speedKph);
            previousThrottle = throttle;
            previousSpeedKph = speedKph;
            return;
        }

        if (!running)
        {
            if (speedKph <= StartSpeedKph &&
                previousThrottle < StartThrottle &&
                throttle >= StartThrottle)
            {
                StartRun(speedKph);
            }

            previousThrottle = throttle;
            previousSpeedKph = speedKph;
            return;
        }

        elapsed += Time.fixedDeltaTime;
        if (!diagnosticSnapshotLogged && elapsed >= DiagnosticSnapshotSeconds)
        {
            diagnosticSnapshotLogged = true;
            LogPowertrainSnapshot("2s", speedKph, throttle);
        }
        var displacement = body.position - startPosition;
        var horizontalDisplacement = Vector3.ProjectOnPlane(displacement, Vector3.up);
        var longitudinal = Vector3.Dot(horizontalDisplacement, startForward);
        var lateral = (horizontalDisplacement - startForward * longitudinal).magnitude;
        var currentForward = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up).normalized;
        var yaw = currentForward.sqrMagnitude > 0.5f
            ? Vector3.Angle(startForward, currentForward)
            : 0f;
        maximumYaw = Mathf.Max(maximumYaw, yaw);
        maximumLateral = Mathf.Max(maximumLateral, lateral);

        if (maximumYaw > MaximumYawDegrees || maximumLateral > MaximumLateralMetres)
        {
            Finish("not-straight", speedKph);
        }
        else if (throttle < AbortThrottle)
        {
            Finish("throttle-released", speedKph);
        }
        else if (elapsed >= MaximumRunSeconds)
        {
            Finish("timeout", speedKph);
        }
        else
        {
            CaptureMilestones(speedKph);
        }

        previousThrottle = throttle;
        previousSpeedKph = speedKph;
    }

    private void StartRun(float speedKph)
    {
        var forward = Vector3.ProjectOnPlane(vehicle!.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.5f)
            return;

        running = true;
        startPosition = body!.position;
        startForward = forward;
        previousSpeedKph = speedKph;
        elapsed = maximumYaw = maximumLateral = 0f;
        nextMilestone = 0;
        diagnosticSnapshotLogged = false;
        CadillacEscaladeDiagnostics.TelemetryInfo(context,
            $"CadillacEscalade acceleration run started vehicle={vehicle.GetInstanceID()}, " +
            $"speed={speedKph:0.0}kmh. Hold full throttle on a flat straight.");
        LogPowertrainSnapshot("start", speedKph, Mathf.Clamp01(physics!.input.Throttle));
    }

    private void CaptureMilestones(float speedKph)
    {
        while (nextMilestone < MilestonesKph.Length && speedKph >= MilestonesKph[nextMilestone])
        {
            var target = MilestonesKph[nextMilestone];
            var span = speedKph - previousSpeedKph;
            var fraction = span > 0.001f
                ? Mathf.Clamp01((target - previousSpeedKph) / span)
                : 1f;
            var milestoneTime = elapsed - Time.fixedDeltaTime + Time.fixedDeltaTime * fraction;
            var benchmark = Mathf.Abs(target - 100f) < 0.1f
                ? $", official={OfficialZeroToHundredSeconds:0.0}s, " +
                  $"delta={milestoneTime - OfficialZeroToHundredSeconds:+0.000;-0.000;0.000}s"
                : string.Empty;
            CadillacEscaladeDiagnostics.TelemetryInfo(context,
                $"CadillacEscalade acceleration milestone vehicle={vehicle!.GetInstanceID()}, " +
                $"0to{target:0}={milestoneTime:0.000}s{benchmark}, " +
                $"yaw={maximumYaw:0.00}deg, lateral={maximumLateral:0.00}m, " +
                $"elevation={body!.position.y - startPosition.y:+0.00;-0.00;0.00}m.");
            nextMilestone++;
        }

        if (nextMilestone == MilestonesKph.Length)
            Finish("160kmh-complete", speedKph);
    }

    private void LogPowertrainSnapshot(string label, float speedKph, float throttle)
    {
        if (physics == null || vehicle == null)
            return;
        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        CadillacEscaladeDiagnostics.TelemetryInfo(context,
            $"CadillacEscalade drivetrain vehicle={vehicle.GetInstanceID()} snapshot={label}, " +
            $"speed={speedKph:0.0}kmh, inputThrottle={throttle:0.00}, " +
            $"engineThrottle={engine.ThrottlePosition:0.00}, " +
            $"running={engine.IsRunning}, ignition={engine.ignition}, canRun={engine.canRun}, " +
            $"rpm={engine.RPMPercent * engine.revLimiterRPM:0}, " +
            $"gear={transmission.Gear}, ratio={transmission.currentGearRatio:0.000}.");
    }

    private void Finish(string reason, float speedKph)
    {
        CadillacEscaladeDiagnostics.TelemetryInfo(context,
            $"CadillacEscalade acceleration run ended vehicle={vehicle?.GetInstanceID()}, " +
            $"reason={reason}, elapsed={elapsed:0.000}s, speed={speedKph:0.0}kmh, " +
            $"milestones={nextMilestone}/{MilestonesKph.Length}, " +
            $"yaw={maximumYaw:0.00}deg, lateral={maximumLateral:0.00}m.");
        running = false;
    }

    private void OnDisable()
    {
        if (running)
            Finish("component-disabled", body == null ? 0f : body.velocity.magnitude * 3.6f);
        previousThrottle = 0f;
    }
}
