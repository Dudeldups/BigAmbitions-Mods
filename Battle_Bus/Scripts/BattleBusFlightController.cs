#nullable enable
using System;
using System.Linq;
using BAModAPI;
using Helpers;
using UnityEngine;

[DefaultExecutionOrder(10000)]
[DisallowMultipleComponent]
public sealed class BattleBusFlightController : MonoBehaviour
{
    public bool IsBurnerOn => piloted && !wrecked;
    public bool IsBurnerBoosted => IsBurnerOn && (climbPressed || (flightMode != FlightMode.Driving && forwardInput > 0f));
    public bool IsDriving => flightMode == FlightMode.Driving;

    private const float ClimbSpeed = 1.25f;
    private const float DescentSpeed = 1.65f;
    private const float LandingSpeed = 0.9f;
    private const float VerticalVelocityGain = 2.2f;
    private const float MaximumClimbAcceleration = 3.2f;
    private const float MaximumLandingAcceleration = 6.0f;
    private const float GroundProbeDistance = 1.65f;
    private const float GroundProbeOriginOffset = 0.05f;
    private const float GroundedClearance = 0.08f;
    private const float LandingContactClearance = 0.14f;
    private const float FlightActivationClearance = 0.30f;
    private const float FlightLandingClearance = 0.50f;
    private const float LandingBrakeClearance = 0.12f;
    private const float GroundedVerticalSpeed = 0.65f;
    private const float AirForwardSpeed = 55f / 3.6f;
    private const float AirReverseSpeed = 5f;
    private const float AirForwardGain = 1.8f;
    private const float AirMaximumAcceleration = 6f;
    private const float AirLateralDamping = 0.65f;
    private const float AirYawRate = 0.75f;
    private const float AirYawGain = 3f;
    private const float AirMaximumYawAcceleration = 2.5f;
    private const float AirBankAngle = 13f;
    private const float AirRollRateGain = 2.5f;
    private const float AirRollRateLimit = 0.8f;
    private const float AirRollAccelerationGain = 3f;
    private const float AirMaximumRollAcceleration = 2f;
    private const float AirPitchGain = 7.5f;
    private const float AirPitchDamping = 3.8f;
    private const float AirMaximumPitchAcceleration = 5f;
    private const float PilotControlGracePeriod = 0.75f;

    [SerializeField] private Transform? flameMotion = null;
    [SerializeField] private Transform[] supportPoints = Array.Empty<Transform>();
    [SerializeField] private float[] supportWheelRadii = Array.Empty<float>();
    [SerializeField] private Transform[] wheelCamberPivots = Array.Empty<Transform>();
    [SerializeField] private Transform[] rearArmPivots = Array.Empty<Transform>();
    [SerializeField] private float[] wheelCamberDirections = Array.Empty<float>();
    [SerializeField] private float flightCamberDegrees = 6f;

    private readonly RaycastHit[] groundHits = new RaycastHit[16];
    private VehicleController? vehicle;
    private Rigidbody? body;
    private ModContext? context;
    private Vector3 flameScale;
    private Quaternion flameRotation;
    private NWH.VehiclePhysics2.Damage.DamageHandler? damageHandler;
    private Quaternion[] flightWheelRotations = Array.Empty<Quaternion>();
    private Quaternion[] rearArmRestRotations = Array.Empty<Quaternion>();
    private Transform[] wheelSpinVisuals = Array.Empty<Transform>();
    private Quaternion[] frozenWheelRotations = Array.Empty<Quaternion>();
    private bool piloted;
    private FlightMode flightMode;
    private bool fullySupported;
    private bool awaitingPilotRelease;
    private bool wheelSpinSuppressed;
    private bool wrecked;
    private float nextSupportCheckTime;
    private float pilotControlGraceEndsAt;
    private bool climbPressed;
    private bool descentPressed;
    private float forwardInput;
    private float yawInput;
    private bool warnedKinematicInput;
    private string loggedInputState = string.Empty;
    private int loggedVerticalInput = -1;

    private enum FlightMode
    {
        Driving,
        TakingOff,
        Flying,
        Landing,
    }

    private void Awake()
    {
        CachePrefabConfiguration();
        // Keep polling the player-control state in case the vehicle entry event
        // arrives late or is missed. Ground support probes are throttled below.
        enabled = true;
    }

    public void CachePrefabConfiguration()
    {
        vehicle ??= GetComponent<VehicleController>();
        body ??= GetComponent<Rigidbody>();
        damageHandler ??= GetComponentInChildren<NWH.VehiclePhysics2.Damage.DamageHandler>(true);
        if (flameMotion != null)
        {
            flameScale = flameMotion.localScale;
            flameRotation = flameMotion.localRotation;
            flameMotion.gameObject.SetActive(piloted && !wrecked);
        }
        if (supportWheelRadii.Length != supportPoints.Length)
            supportWheelRadii = Enumerable.Repeat(0.35f, supportPoints.Length).ToArray();
        flightWheelRotations = new Quaternion[wheelCamberPivots.Length];
        for (var index = 0; index < wheelCamberPivots.Length; index++)
        {
            var pivot = wheelCamberPivots[index];
            if (pivot == null)
                continue;
            var sideSign = index < wheelCamberDirections.Length &&
                           Mathf.Abs(wheelCamberDirections[index]) > 0.5f
                ? wheelCamberDirections[index]
                : pivot.localPosition.x < 0f ? 1f : -1f;
            flightWheelRotations[index] = Quaternion.Euler(0f, 0f, sideSign * flightCamberDegrees);
            pivot.localRotation = Quaternion.identity;
        }
        wheelSpinVisuals = wheelCamberPivots.Select(pivot => pivot != null ? pivot.parent : null)
            .Where(visual => visual != null).Cast<Transform>().ToArray();
        frozenWheelRotations = new Quaternion[wheelSpinVisuals.Length];
        rearArmRestRotations = rearArmPivots.Select(pivot =>
            pivot != null ? pivot.localRotation : Quaternion.identity).ToArray();
    }

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        body = controller.GetComponent<Rigidbody>();
        context = modContext;
        CachePrefabConfiguration();
        if (body == null)
            context?.Logger.Warn($"Battle Bus flight: Rigidbody missing on vehicle={controller.GetInstanceID()}.");
        if (flameMotion == null)
            context?.Logger.Warn($"Battle Bus flame animation: no burner visual assigned on vehicle={controller.GetInstanceID()}.");
        if (wheelSpinVisuals.Length != 4)
            context?.Logger.Warn(
                $"Battle Bus flight: wheel spin control found {wheelSpinVisuals.Length} wheel visuals; " +
                "expected four mounted rolling groups.");
        if (damageHandler == null)
            context?.Logger.Warn($"Battle Bus damage: handler missing on vehicle={controller.GetInstanceID()}.");
        ConfigureDamageForMode();
    }

    internal void SetPiloted(bool value)
    {
        var stateChanged = piloted != value;
        piloted = value;
        if (flameMotion != null)
            flameMotion.gameObject.SetActive(value && !wrecked);
        enabled = true;
        ResetPilotInputs();
        warnedKinematicInput = false;
        SetWheelSpinSuppressed(false);
        if (value)
        {
            awaitingPilotRelease = false;
            pilotControlGraceEndsAt = Time.time + PilotControlGracePeriod;
            flightMode = FlightMode.Driving;
            ConfigureDamageForMode();
            fullySupported = IsFullySupported();
            StraightenWheels();
            loggedVerticalInput = -1;
            loggedInputState = string.Empty;
            if (stateChanged)
                BattleBusDiagnostics.FlightInfo(context,
                    $"Battle Bus flight controls enabled for vehicle={vehicle?.GetInstanceID()}; " +
                    $"controlled={vehicle?.controlledByPlayer}.");
        }
        else
        {
            awaitingPilotRelease = vehicle != null && vehicle.controlledByPlayer;
            pilotControlGraceEndsAt = 0f;
            flightMode = FlightMode.Driving;
            ConfigureDamageForMode();
            loggedVerticalInput = -1;
            loggedInputState = string.Empty;
            if (flameMotion != null)
            {
                flameMotion.localScale = flameScale;
                flameMotion.localRotation = flameRotation;
            }
            fullySupported = IsFullySupported();
            SetWheelSpinSuppressed(false);
            if (fullySupported)
                StraightenWheels();
            if (stateChanged)
                BattleBusDiagnostics.FlightInfo(context,
                    $"Battle Bus flight controls disabled for vehicle={vehicle?.GetInstanceID()}; " +
                    $"controlled={vehicle?.controlledByPlayer}, awaitingRelease={awaitingPilotRelease}.");
        }
    }

    private void Update()
    {
        vehicle ??= GetComponent<VehicleController>();
        body ??= GetComponent<Rigidbody>();
        if (vehicle == null)
            return;

        var isWrecked = vehicle.vehicleInstance != null && vehicle.vehicleInstance.damage >= 0.999f;
        if (wrecked != isWrecked)
        {
            wrecked = isWrecked;
            if (flameMotion != null)
                flameMotion.gameObject.SetActive(piloted && !wrecked);
            flightMode = FlightMode.Driving;
            ConfigureDamageForMode();
            BattleBusDiagnostics.FlightInfo(context,
                $"Battle Bus burner vehicle={vehicle.GetInstanceID()}: " +
                $"{(wrecked ? "extinguished at total damage; gravity restored" : "rearmed after repair")}.");
        }

        var controlledByPlayer = vehicle.controlledByPlayer;
        if (awaitingPilotRelease)
        {
            if (!controlledByPlayer)
                awaitingPilotRelease = false;
        }
        else if (piloted && !controlledByPlayer && Time.time >= pilotControlGraceEndsAt)
            SetPiloted(false);
        else if (!piloted && controlledByPlayer)
            SetPiloted(true);

        if (!piloted)
        {
            ResetPilotInputs();
            return;
        }

        climbPressed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        descentPressed = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        var forward = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        var reverse = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
        var turnLeft = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
        var turnRight = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
        forwardInput = forward == reverse ? 0f : forward ? 1f : -1f;
        yawInput = turnLeft == turnRight ? 0f : turnRight ? 1f : -1f;

        var inputState = $"{(climbPressed ? 1 : 0) - (descentPressed ? 1 : 0)}/" +
                         $"{forwardInput:F0}/{yawInput:F0}";
        if (!string.Equals(inputState, loggedInputState, StringComparison.Ordinal))
        {
            loggedInputState = inputState;
            BattleBusDiagnostics.FlightInfo(context,
                $"Battle Bus flight inputs vehicle={vehicle.GetInstanceID()}: " +
                $"vertical/forward/yaw={inputState}, controlled={controlledByPlayer}, " +
                $"mode={flightMode}, visualSpinFrozen={wheelSpinSuppressed}, " +
                $"speed={body?.velocity.magnitude:F2}m/s, grounded={fullySupported}.");
        }
    }

    private float recoveryUntil;
    internal void BeginWorkshopRecovery(float seconds)
    {
        recoveryUntil = Time.time + seconds;
        ResetPilotInputs();
        flightMode = FlightMode.Driving;
        SetWheelSpinSuppressed(false);
        StraightenWheels();
        ConfigureDamageForMode();
    }

    internal bool TryWorkshopGround(Vector3 candidate, Quaternion rotation, out Vector3 grounded)
    {
        grounded = candidate;
        if (supportPoints.Length != 4) return false;
        var height = float.NegativeInfinity;
        var lowest = float.PositiveInfinity;
        for (var i = 0; i < supportPoints.Length; i++)
        {
            var local = transform.InverseTransformPoint(supportPoints[i].position);
            var offset = rotation * local;
            if (!Physics.Raycast(candidate + offset + Vector3.up * 4f, Vector3.down,
                    out var hit, 9f, LayerHelper.groundLayerMask, QueryTriggerInteraction.Ignore) || hit.normal.y < .85f)
                return false;
            var level = hit.point.y - offset.y + supportWheelRadii[i] + .025f;
            height = Mathf.Max(height, level); lowest = Mathf.Min(lowest, level);
        }
        if (height - lowest > .25f) return false;
        grounded.y = height;
        return true;
    }

    private void FixedUpdate()
    {
        if (vehicle == null || body == null)
            return;

        if (Time.time < recoveryUntil) return;
        if (!piloted)
        {
            if (Time.time < nextSupportCheckTime)
                return;
            nextSupportCheckTime = Time.time + 0.25f;
            fullySupported = IsFullySupported();
            SetWheelSpinSuppressed(false);
            if (fullySupported)
                StraightenWheels();
            return;
        }

        var up = climbPressed;
        var down = descentPressed;
        var verticalInput = up == down ? 0 : up ? 1 : -1;
        if (verticalInput != loggedVerticalInput)
        {
            loggedVerticalInput = verticalInput;
            BattleBusDiagnostics.FlightInfo(context,
                $"Battle Bus vertical input changed vehicle={vehicle.GetInstanceID()}: " +
                $"input={(verticalInput > 0 ? "climb" : verticalInput < 0 ? "descend" : "neutral")}, " +
                $"kinematic={body.isKinematic}, velocityY={body.velocity.y:F2}, " +
                $"supported={fullySupported}.");
        }
        if (body.isKinematic)
        {
            if ((verticalInput != 0 || forwardInput != 0f || yawInput != 0f) && !warnedKinematicInput)
            {
                warnedKinematicInput = true;
                context?.Logger.Warn(
                    $"Battle Bus flight input vehicle={vehicle.GetInstanceID()} was ignored because its Rigidbody is kinematic.");
            }
            return;
        }
        warnedKinematicInput = false;
        if (wrecked)
        {
            // Do not counter gravity or accept lift/thrust after total damage.
            // The vehicle remains physical and can fall to the road naturally.
            SetWheelSpinSuppressed(false);
            return;
        }
        if (!TryMeasureGroundClearance(out var minimumClearance,
                out var maximumClearance, out var probeHits))
        {
            minimumClearance = GroundProbeDistance;
            maximumClearance = GroundProbeDistance;
            probeHits = 0;
        }
        fullySupported = probeHits == supportPoints.Length &&
                         maximumClearance <= GroundedClearance &&
                         Mathf.Abs(body.velocity.y) <= GroundedVerticalSpeed;

        // Re-enter road mode as soon as the tires reach the road. A missed probe
        // or compressed suspension must not leave the rolling and steering visual
        // frozen after flight.
        if (flightMode != FlightMode.Driving && !up &&
            probeHits >= 2 && minimumClearance <= FlightLandingClearance &&
            body.velocity.y <= GroundedVerticalSpeed)
        {
            SetFlightMode(FlightMode.Driving, minimumClearance, maximumClearance);
            SetWheelSpinSuppressed(false);
            StraightenWheels();
            return;
        }

        if (flightMode != FlightMode.Driving)
            StabilizePitch();

        if (flightMode == FlightMode.Driving)
        {
            SetWheelSpinSuppressed(false);
            if (fullySupported)
                StraightenWheels();
            if (!up || down)
                return;

            SetFlightMode(FlightMode.TakingOff, minimumClearance, maximumClearance);
        }

        if (flightMode == FlightMode.TakingOff)
        {
            if (!up || down)
            {
                SetFlightMode(fullySupported ? FlightMode.Driving : FlightMode.Landing,
                    minimumClearance, maximumClearance);
            }
            else if (minimumClearance >= FlightActivationClearance)
            {
                SetFlightMode(FlightMode.Flying, minimumClearance, maximumClearance);
            }
            else
            {
                SetWheelSpinSuppressed(false);
                ApplyVerticalControl(ClimbSpeed, MaximumClimbAcceleration);
                return;
            }
        }

        if (flightMode == FlightMode.Flying)
        {
            if (!up && maximumClearance <= FlightLandingClearance)
                SetFlightMode(FlightMode.Landing, minimumClearance, maximumClearance);
            else
            {
                ApplyAirControl(forwardInput, yawInput);
                ApplyVerticalControl(up == down ? 0f : up ? ClimbSpeed : -DescentSpeed,
                    MaximumClimbAcceleration);
                SetWheelSpinSuppressed(minimumClearance > FlightActivationClearance);
                return;
            }
        }

        if (flightMode == FlightMode.Landing)
        {
            if (up && !down)
            {
                SetFlightMode(minimumClearance >= FlightActivationClearance
                        ? FlightMode.Flying
                        : FlightMode.TakingOff,
                    minimumClearance, maximumClearance);
                if (flightMode == FlightMode.TakingOff)
                {
                    SetWheelSpinSuppressed(false);
                    ApplyVerticalControl(ClimbSpeed, MaximumClimbAcceleration);
                    return;
                }

                ApplyAirControl(forwardInput, yawInput);
                ApplyVerticalControl(ClimbSpeed, MaximumClimbAcceleration);
                SetWheelSpinSuppressed(minimumClearance > FlightActivationClearance);
                return;
            }

            // The four probes can disagree across suspension travel or a curb.
            // Once any axle has reached the road, stop cancelling gravity so
            // the tires can carry the vehicle and the drivetrain can recover.
            if (fullySupported || (probeHits >= 2 &&
                    minimumClearance <= LandingContactClearance &&
                    body.velocity.y <= GroundedVerticalSpeed))
            {
                SetFlightMode(FlightMode.Driving, minimumClearance, maximumClearance);
                SetWheelSpinSuppressed(false);
                StraightenWheels();
                return;
            }

            SetWheelSpinSuppressed(false);
            ApplyVerticalControl(maximumClearance <= LandingBrakeClearance ? 0f : -LandingSpeed,
                MaximumLandingAcceleration);
        }
    }

    private void LateUpdate()
    {
        if (wheelSpinSuppressed)
            for (var index = 0; index < wheelSpinVisuals.Length; index++)
                wheelSpinVisuals[index].localRotation = frozenWheelRotations[index];

        if (flameMotion != null && piloted && !wrecked)
        {
            var thrust = climbPressed ||
                         (flightMode != FlightMode.Driving && forwardInput > 0f);
            var targetScale = flameScale * (thrust ? 1.80f : 1.28f);
            flameMotion.localScale = Vector3.Lerp(flameMotion.localScale,
                targetScale, Mathf.Clamp01(Time.deltaTime * 7f));
        }

        var armsUnloaded = piloted && flightMode != FlightMode.Driving && !fullySupported;
        for (var index = 0; index < rearArmPivots.Length; index++)
        {
            var arm = rearArmPivots[index];
            if (arm == null)
                continue;
            var rest = index < rearArmRestRotations.Length
                ? rearArmRestRotations[index] : Quaternion.identity;
            var angle = armsUnloaded ? 4f : 0f;
            arm.localRotation = Quaternion.Slerp(arm.localRotation,
                rest * Quaternion.Euler(angle, 0f, 0f), Time.deltaTime * 4f);
        }

        if (!piloted)
            return;

        var targetRotation = flightMode == FlightMode.Flying;
        for (var index = 0; index < wheelCamberPivots.Length; index++)
        {
            var pivot = wheelCamberPivots[index];
            if (pivot == null)
                continue;
            var target = targetRotation ? flightWheelRotations[index] : Quaternion.identity;
            pivot.localRotation = Quaternion.Slerp(pivot.localRotation, target, Time.deltaTime * 5f);
        }
    }

    private void StabilizePitch()
    {
        if (body == null)
            return;
        var pitchError = Vector3.Dot(transform.forward, Vector3.up);
        var pitchRate = Vector3.Dot(body.angularVelocity, transform.right);
        var acceleration = Mathf.Clamp(pitchError * AirPitchGain - pitchRate * AirPitchDamping,
            -AirMaximumPitchAcceleration, AirMaximumPitchAcceleration);
        body.AddTorque(transform.right * acceleration, ForceMode.Acceleration);
    }

    private void ApplyAirControl(float throttle, float steering)
    {
        var rigidbody = body;
        if (rigidbody == null)
            return;

        var forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = Vector3.forward;

        var planarVelocity = Vector3.ProjectOnPlane(rigidbody.velocity, Vector3.up);
        var forwardSpeed = Vector3.Dot(planarVelocity, forward);
        if (!Mathf.Approximately(throttle, 0f))
        {
            var targetSpeed = throttle > 0f ? AirForwardSpeed : -AirReverseSpeed;
            var acceleration = Mathf.Clamp((targetSpeed - forwardSpeed) * AirForwardGain,
                -AirMaximumAcceleration, AirMaximumAcceleration);
            rigidbody.AddForce(forward * acceleration, ForceMode.Acceleration);
        }

        var lateralVelocity = planarVelocity - forward * forwardSpeed;
        rigidbody.AddForce(-lateralVelocity * AirLateralDamping, ForceMode.Acceleration);

        var currentYawRate = Vector3.Dot(rigidbody.angularVelocity, Vector3.up);
        var targetYawRate = steering * AirYawRate;
        var yawAcceleration = Mathf.Clamp((targetYawRate - currentYawRate) * AirYawGain,
            -AirMaximumYawAcceleration, AirMaximumYawAcceleration);
        rigidbody.AddTorque(Vector3.up * yawAcceleration, ForceMode.Acceleration);

        var currentBank = Vector3.SignedAngle(Vector3.up, transform.up, transform.forward);
        var targetBank = -steering * AirBankAngle;
        var bankError = Mathf.DeltaAngle(currentBank, targetBank) * Mathf.Deg2Rad;
        var rollRate = Vector3.Dot(rigidbody.angularVelocity, transform.forward);
        var targetRollRate = Mathf.Clamp(bankError * AirRollRateGain,
            -AirRollRateLimit, AirRollRateLimit);
        var rollAcceleration = Mathf.Clamp((targetRollRate - rollRate) * AirRollAccelerationGain,
            -AirMaximumRollAcceleration, AirMaximumRollAcceleration);
        rigidbody.AddTorque(transform.forward * rollAcceleration, ForceMode.Acceleration);
    }

    private void ApplyVerticalControl(float targetSpeed, float maximumAcceleration)
    {
        if (body == null)
            return;

        var gravityMagnitude = Mathf.Max(0f, -Physics.gravity.y);
        var verticalAcceleration = Mathf.Clamp(
            (targetSpeed - body.velocity.y) * VerticalVelocityGain,
            -gravityMagnitude,
            maximumAcceleration);
        body.AddForce(Vector3.up * (gravityMagnitude + verticalAcceleration), ForceMode.Acceleration);
    }

    private void SetFlightMode(FlightMode value, float minimumClearance, float maximumClearance)
    {
        if (flightMode == value)
            return;

        var previous = flightMode;
        flightMode = value;
        ConfigureDamageForMode();
        BattleBusDiagnostics.FlightInfo(context,
            $"Battle Bus flight mode vehicle={vehicle?.GetInstanceID()}: {previous}->{value}, " +
            $"wheelClearance={minimumClearance:F2}..{maximumClearance:F2}m, " +
            $"velocityY={body?.velocity.y:F2}, shift={climbPressed}, ctrl={descentPressed}.");
    }

    private void ConfigureDamageForMode()
    {
        if (damageHandler == null)
            return;
        var airborne = flightMode == FlightMode.Flying || flightMode == FlightMode.Landing;
        damageHandler.damageIntensity = airborne ? 0.26f : 0.68f;
        damageHandler.decelerationThreshold = airborne ? 500f : 350f;
        BattleBusDiagnostics.DamageInfo(context,
            $"Battle Bus damage mode vehicle={vehicle?.GetInstanceID()}: " +
            $"flightMode={flightMode}, intensity={damageHandler.damageIntensity:F2}, " +
            $"threshold={damageHandler.decelerationThreshold:F0}.");
    }

    private bool TryMeasureGroundClearance(out float minimumClearance,
        out float maximumClearance, out int probeHits)
    {
        minimumClearance = float.PositiveInfinity;
        maximumClearance = 0f;
        probeHits = 0;
        if (body == null || supportPoints.Length == 0)
            return false;

        for (var index = 0; index < supportPoints.Length; index++)
        {
            var point = supportPoints[index];
            if (point == null)
            {
                minimumClearance = Mathf.Min(minimumClearance, GroundProbeDistance);
                maximumClearance = Mathf.Max(maximumClearance, GroundProbeDistance);
                continue;
            }

            var origin = point.position + Vector3.up * GroundProbeOriginOffset;
            var count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits,
                GroundProbeDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var nearestGroundDistance = float.PositiveInfinity;
            for (var hitIndex = 0; hitIndex < count; hitIndex++)
            {
                var hit = groundHits[hitIndex];
                if (hit.collider == null || hit.collider.attachedRigidbody == body ||
                    Vector3.Dot(hit.normal, Vector3.up) <= 0.35f)
                    continue;
                nearestGroundDistance = Mathf.Min(nearestGroundDistance, hit.distance);
            }

            var hasHit = !float.IsPositiveInfinity(nearestGroundDistance);
            if (hasHit)
                probeHits++;
            var radius = index < supportWheelRadii.Length
                ? Mathf.Max(0.1f, supportWheelRadii[index])
                : 0.35f;
            var clearance = hasHit
                ? Mathf.Max(0f, nearestGroundDistance - GroundProbeOriginOffset - radius)
                : Mathf.Max(0f, GroundProbeDistance - GroundProbeOriginOffset - radius);
            minimumClearance = Mathf.Min(minimumClearance, clearance);
            maximumClearance = Mathf.Max(maximumClearance, clearance);
        }

        return !float.IsPositiveInfinity(minimumClearance);
    }

    private bool HasAirborneClearance()
    {
        return TryMeasureGroundClearance(out var minimumClearance, out _, out _) &&
               minimumClearance > GroundedClearance;
    }

    private void SetWheelSpinSuppressed(bool value)
    {
        if (wheelSpinSuppressed != value)
        {
            wheelSpinSuppressed = value;
            if (value)
                for (var index = 0; index < wheelSpinVisuals.Length; index++)
                    frozenWheelRotations[index] = wheelSpinVisuals[index].localRotation;
            BattleBusDiagnostics.FlightInfo(context,
                $"Battle Bus wheel spin {(value ? "suppressed" : "restored")} " +
                $"for vehicle={vehicle?.GetInstanceID()}, inAir={value}.");
        }
    }

    private void ResetPilotInputs()
    {
        climbPressed = false;
        descentPressed = false;
        forwardInput = 0f;
        yawInput = 0f;
    }

    private void StraightenWheels()
    {
        foreach (var pivot in wheelCamberPivots)
            if (pivot != null)
                pivot.localRotation = Quaternion.identity;
    }

    private bool IsFullySupported()
    {
        if (body == null ||
            !TryMeasureGroundClearance(out _, out var maximumClearance, out var probeHits))
            return false;
        return probeHits == supportPoints.Length &&
               maximumClearance <= GroundedClearance &&
               Mathf.Abs(body.velocity.y) <= GroundedVerticalSpeed;
    }

}
