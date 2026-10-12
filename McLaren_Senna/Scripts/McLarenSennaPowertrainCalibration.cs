#nullable enable
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

// Engine values are crankshaft values; NWH compensates engineLossPercent at
// full throttle, so a separate native modifier accounts for driveline losses.
[AddComponentMenu("")]
public sealed class McLarenSennaPowertrainCalibration : MonoBehaviour
{
    public const float DrivelineEfficiency = .85f;
    // Clean 11 October CSV: 2.849 s to 100 / 6.955 s to 200.
    // A 2.22% native force correction approaches 2.8 / 6.8 s; not physical loss.
    public const float GameForceCalibration = .828f;
    public const float LaunchTorqueFloor = .65f;
    public const float HighSpeedTorqueFloor = .494f;
    public const float UpshiftRpm = 8000f;
    public const float DownshiftRpm = 4500f;
    public const float PostShiftBan = .18f;
    // Native field is an angle in degrees despite VehicleType naming it turnRadius.
    public const int SteeringAngle = 37;
    public const float HeadlightForwardOffset = .12f;
    public const float BrakeTorque = 3380f;
    public const float LowSpeedBrakeTorque = 2140f;
    private PhysicsVehicle? physics;

    public static AnimationCurve CreatePowerCurve()
    {
        // McLaren: 800 Nm at 5,500..6,700 RPM, 588 kW at 7,250 RPM.
        // Below the published plateau these are calibrated turbo spool/idle
        // values, not a claim that McLaren published a complete dyno trace.
        var rpm = new[] { 0f, 1100f, 2200f, 3500f, 4500f, 5500f, 6700f, 7250f, 8500f };
        var kw = new[] { 0f, 14.7f, 110f, 238f, 358f, 800f * 5500f * Mathf.PI / 30000f,
            800f * 6700f * Mathf.PI / 30000f, 588f, 523.32f };
        var keys = new Keyframe[rpm.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            var incoming = i == 0 ? 0f : (kw[i] - kw[i-1]) / (rpm[i] - rpm[i-1]) * 8500f / 588f;
            var outgoing = i == keys.Length-1 ? 0f : (kw[i+1] - kw[i]) / (rpm[i+1] - rpm[i]) * 8500f / 588f;
            keys[i] = new Keyframe(rpm[i] / 8500f, kw[i] / 588f, incoming, outgoing);
        }
        return new AnimationCurve(keys);
    }

    internal void Initialize(PhysicsVehicle controller)
    {
        Detach();
        physics = controller;
        Attach();
    }
    // Native brakes call this during their physics step. The speed-squared
    // response approximates the Senna's additional aero braking at high speed;
    // it is calibrated from the CSV, not a new game-state polling loop.
    public static float BrakeMultiplier(float speedKph)
    {
        var aeroBlend = Mathf.Clamp01((speedKph * speedKph - 10000f) / 30000f);
        return Mathf.Lerp(LowSpeedBrakeTorque / BrakeTorque, 1f, aeroBlend);
    }
    private float Braking() => BrakeMultiplier(physics != null ? physics.Speed * 3.6f : 0f);
    // Gross output includes compensation for 25% internal engine losses.
    // Solve (1 + loss) * modifier - loss = desired net efficiency.
    public static float LaunchTorqueMultiplier(float speedKph, float throttle, int gear)
    {
        if (gear <= 0) return 1f;
        // Empirical forward launch torque management: ease the traction-limited
        // 1.4 g launch, restore full calibrated output above 120 km/h. Keep gentle
        // throttle, idle, neutral and reverse on the existing engine calibration.
        var speedBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((speedKph - 80f) / 40f));
        var loadBlend = Mathf.Clamp01((throttle - .6f) / .4f);
        return Mathf.Lerp(1f, Mathf.Lerp(LaunchTorqueFloor, 1f, speedBlend), loadBlend);
    }
    // 12 October CSV: 200..300 takes 11.366 s versus the factory 10.7 s interval.
    // Empirical resistance-equivalent native torque envelope, separate from
    // the crankshaft curve; .494 fit predicts 17.49 s, pending an installed drive.
    public static float HighSpeedTorqueMultiplier(float speedKph, int gear)
    {
        if (gear <= 0 || speedKph <= 200f) return 1f;
        var blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((speedKph*speedKph - 40000f) / 50000f));
        return Mathf.Lerp(1f, HighSpeedTorqueFloor, blend);
    }
    private float Efficiency()
    {
        var launch = physics != null && physics.powertrain.transmission.Gear > 0
            ? LaunchTorqueMultiplier(physics.Speed * 3.6f, physics.input.Throttle, physics.powertrain.transmission.Gear) : 1f;
        var highSpeed = physics != null
            ? HighSpeedTorqueMultiplier(physics.Speed * 3.6f, physics.powertrain.transmission.Gear) : 1f;
        return (DrivelineEfficiency * GameForceCalibration * launch * highSpeed + .25f) / 1.25f;
    }
    private void Attach()
    {
        if (physics != null && !physics.powertrain.engine.powerModifiers.Contains(Efficiency))
            physics.powertrain.engine.powerModifiers.Add(Efficiency);
        if (physics != null && !physics.brakes.brakeTorqueModifiers.Contains(Braking))
            physics.brakes.brakeTorqueModifiers.Add(Braking);
    }
    private void Detach()
    {
        if (physics != null)
        {
            physics.powertrain.engine.powerModifiers.Remove(Efficiency);
            physics.brakes.brakeTorqueModifiers.Remove(Braking);
        }
    }
    private void OnEnable() => Attach();
    private void OnDisable() => Detach();
    internal void Release() { Detach(); physics = null; }
    private void OnDestroy() => Release();
}
