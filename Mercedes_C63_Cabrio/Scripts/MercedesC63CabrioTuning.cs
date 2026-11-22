using UnityEngine;

// Shared editor/runtime calibration. Real ratios, power and mass are retained.
// Baseline stopping reference and CSV limitations are recorded in Config.
public static class MercedesC63CabrioTuning
{
    public const float FrontWheelForwardOffset = .10f; // previous 97.5 mm + requested 2.5 mm
    public const float RearWheelForwardOffset = .105f; // previous 95 mm + requested 10 mm
    public const float GroundCorrection = .05f;
    public const float InitialSpringLength = .085f;
    public const float FrontBodyDrop = .03f;
    public const float RearBodyDrop = .02f;
    public const float Wheelbase = 2.83972f;
    // Raising contact mounts lets native suspension lower the root by 50 mm
    // without lowering tires through the road or fighting physics each frame.
    public static readonly Quaternion BodyPitch = Quaternion.Euler(
        Mathf.Atan((FrontBodyDrop - RearBodyDrop) / Wheelbase) * Mathf.Rad2Deg, 0f, 0f);
    public static readonly Matrix4x4 BodyStance = Matrix4x4.TRS(
        new Vector3(0f, -(FrontBodyDrop + RearBodyDrop) * .5f, 0f), BodyPitch, Vector3.one);
    public static Vector3 BodyPoint(Vector3 point) => BodyStance.MultiplyPoint3x4(point);
    public const float StockBrakeTorque = 1850f;
    public const float BrakeUpgrade = 1.25f;
    public const float BrakeTorque = StockBrakeTorque * BrakeUpgrade;
    // Crankshaft rating is preserved; calibrated mechanical driveline loss acts at transmission input.
    public const float DrivetrainEfficiency = .85f;
    public const float EngineInertia = .30f;
    // Native N->1 selection is already instantaneous. Couple progressively from
    // idle instead of waiting for a throttle-dependent 1,750 rpm launch threshold.
    public const float ClutchEngagementRpm = 900f;
    public const float ClutchThrottleOffsetRpm = 0f;
    // Spread launch coupling over 1,000 rpm to avoid dragging the engine down
    // immediately after its first free-rev frame (confirmed by the 14:23 CSV).
    public const float ClutchEngagementRange = 1000f;
    public const float UpshiftRpm = 6500f;
    public const float DownshiftRpm = 2600f;
    public const float PostShiftBan = 0f;
    public const float ShiftDuration = .10f;
    public const float VariableShiftIntensity = .35f;
    public const float FrontGrip = 1f;
    public const float RearGrip = 1f;
    public const float TireStiffness = 1f;
    public const float TractionSlipThreshold = .65f; // severe-slip native backstop
    public const float ProgressiveSlipStart = .13f;
    public const float ProgressiveSlipEnd = .32f;
    public const float AbsSlipThreshold = .15f;
}
