#nullable enable
using System.Reflection;
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

internal sealed class KoenigseggJeskoAccelerationTelemetry : MonoBehaviour
{
    private const float StartSpeedKph = 2f;
    private const float StartThrottle = 0.92f;
    private const float AbortThrottle = 0.70f;
    private const float MaximumYawDegrees = 5f;
    private const float MaximumLateralMetres = 3f;
    private const float MaximumRunSeconds = 45f;
    private const float MaximumBrakingRunSeconds = 20f;
    private const float OfficialZeroToHundredSeconds = 2.5f;

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
    private bool brakingRun;
    private float brakingElapsed;
    private float brakingStartSpeedKph;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        enabled = KoenigseggJeskoDiagnostics.TelemetryEnabled;
        if (!enabled)
            return;

        vehicle = controller;
        physics = controller.GetComponent<PhysicsVehicle>();
        body = controller.GetComponent<Rigidbody>() ?? controller.GetComponentInParent<Rigidbody>();
        context = modContext;
        KoenigseggJeskoDiagnostics.Info(context,
            $"KoenigseggJesko acceleration telemetry ready vehicle={controller.GetInstanceID()}, " +
            $"official0to100={OfficialZeroToHundredSeconds:0.0}s, " +
            "milestones=100/200/300kmh.");
    }

    private void FixedUpdate()
    {
        if (vehicle == null || physics == null || body == null)
            return;

        var throttle = Mathf.Clamp01(physics.input.Throttle);
        var brake = ReadFloatMember(physics.input, "Brake");
        var speedKph = body.velocity.magnitude * 3.6f;
        if (!vehicle.controlledByPlayer)
        {
            if (running)
                Finish("driver-exited", speedKph);
            if (brakingRun)
                FinishBraking("driver-exited", speedKph);
            previousThrottle = throttle;
            previousSpeedKph = speedKph;
            return;
        }

        if (brakingRun)
        {
            UpdateBraking(brake, speedKph);
            previousThrottle = throttle;
            previousSpeedKph = speedKph;
            return;
        }

        if (!running && speedKph >= 100f && brake >= 0.85f && throttle <= 0.35f)
        {
            StartBraking(speedKph);
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
        KoenigseggJeskoDiagnostics.Info(context,
            $"KoenigseggJesko acceleration run started vehicle={vehicle.GetInstanceID()}, " +
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
            KoenigseggJeskoDiagnostics.Info(context,
                $"KoenigseggJesko acceleration milestone vehicle={vehicle!.GetInstanceID()}, " +
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
        KoenigseggJeskoDiagnostics.Info(context,
            $"KoenigseggJesko acceleration run ended vehicle={vehicle?.GetInstanceID()}, " +
            $"reason={reason}, elapsed={elapsed:0.000}s, speed={speedKph:0.0}kmh, " +
            $"milestones={nextMilestone}/{MilestonesKph.Length}, " +
            $"yaw={maximumYaw:0.00}deg, lateral={maximumLateral:0.00}m.");
        running = false;
    }

    private void StartBraking(float speedKph)
    {
        brakingRun = true;
        brakingElapsed = 0f;
        brakingStartSpeedKph = speedKph;
        maximumYaw = maximumLateral = 0f;
        KoenigseggJeskoDiagnostics.Info(context,
            $"KoenigseggJesko braking run started vehicle={vehicle!.GetInstanceID()}, " +
            $"speed={speedKph:0.0}kmh. Hold full brake in a straight line.");
    }

    private void UpdateBraking(float brake, float speedKph)
    {
        brakingElapsed += Time.fixedDeltaTime;
        if (speedKph <= 1f)
        {
            FinishBraking("0kmh-complete", speedKph);
            return;
        }

        if (brake < 0.70f)
        {
            FinishBraking("brake-released", speedKph);
            return;
        }

        if (brakingElapsed >= MaximumBrakingRunSeconds)
            FinishBraking("timeout", speedKph);
    }

    private void FinishBraking(string reason, float speedKph)
    {
        KoenigseggJeskoDiagnostics.Info(context,
            $"KoenigseggJesko braking run ended vehicle={vehicle?.GetInstanceID()}, " +
            $"reason={reason}, startSpeed={brakingStartSpeedKph:0.0}kmh, " +
            $"100to0={brakingElapsed:0.000}s, endSpeed={speedKph:0.0}kmh. " +
            "No official 100-to-0 benchmark was assumed.");
        brakingRun = false;
    }

    private static float ReadFloatMember(object target, string name)
    {
        var type = target.GetType();
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field?.GetValue(target) is float fieldValue)
            return Mathf.Clamp01(fieldValue);
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.GetValue(target) is float propertyValue)
            return Mathf.Clamp01(propertyValue);
        return 0f;
    }

    private void OnDisable()
    {
        if (running)
            Finish("component-disabled", body == null ? 0f : body.velocity.magnitude * 3.6f);
        if (brakingRun)
            FinishBraking("component-disabled", body == null ? 0f : body.velocity.magnitude * 3.6f);
        previousThrottle = 0f;
    }
}


