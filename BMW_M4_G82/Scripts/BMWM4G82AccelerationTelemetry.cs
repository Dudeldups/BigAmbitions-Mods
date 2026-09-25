#nullable enable
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

internal sealed class BMWM4G82AccelerationTelemetry : MonoBehaviour
{
    private const float StartSpeedKph = 2f;
    private const float StartThrottle = 0.92f;
    private const float AbortThrottle = 0.70f;
    private const float MaximumYawDegrees = 5f;
    private const float MaximumLateralMetres = 3f;
    private const float MaximumRunSeconds = 45f;
    private const float OfficialZeroToHundredSeconds = 3.5f;

    private static readonly float[] MilestonesKph = { 100f, 200f, 300f };

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
    private float zeroToHundred = -1f;
    private int nextMilestone;
    private bool running;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        physics = controller.GetComponent<PhysicsVehicle>();
        body = controller.GetComponent<Rigidbody>() ?? controller.GetComponentInParent<Rigidbody>();
        context = modContext;
        context?.Logger.Info(
            $"BMWM4G82 acceleration telemetry ready vehicle={controller.GetInstanceID()}, " +
            $"official0to100={OfficialZeroToHundredSeconds:0.0}s, " +
            "milestones=100/200/300kmh.");
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
        zeroToHundred = -1f;
        nextMilestone = 0;
        context?.Logger.Info(
            $"BMWM4G82 acceleration run started vehicle={vehicle.GetInstanceID()}, " +
            $"speed={speedKph:0.0}kmh. Hold full throttle on a flat straight.");
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
            if (nextMilestone == 0)
                zeroToHundred = milestoneTime;

            var segment = nextMilestone == 1 && zeroToHundred >= 0f
                ? $", 100to200={milestoneTime - zeroToHundred:0.000}s"
                : string.Empty;
            var benchmark = nextMilestone == 0
                ? $", official={OfficialZeroToHundredSeconds:0.0}s, " +
                  $"delta={milestoneTime - OfficialZeroToHundredSeconds:+0.000;-0.000;0.000}s"
                : string.Empty;
            context?.Logger.Info(
                $"BMWM4G82 acceleration milestone vehicle={vehicle!.GetInstanceID()}, " +
                $"0to{target:0}={milestoneTime:0.000}s{segment}{benchmark}, " +
                $"yaw={maximumYaw:0.00}deg, lateral={maximumLateral:0.00}m, " +
                $"elevation={body!.position.y - startPosition.y:+0.00;-0.00;0.00}m.");
            nextMilestone++;
        }

        if (nextMilestone == MilestonesKph.Length)
            Finish("300kmh-complete", speedKph);
    }

    private void Finish(string reason, float speedKph)
    {
        context?.Logger.Info(
            $"BMWM4G82 acceleration run ended vehicle={vehicle?.GetInstanceID()}, " +
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
