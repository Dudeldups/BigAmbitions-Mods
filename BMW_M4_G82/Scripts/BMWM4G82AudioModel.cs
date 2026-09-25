#nullable enable
using System;

// Recording references are acoustic calibration values, not measured engine RPM.
internal static class BMWM4G82AudioModel
{
    internal const float IdlePitch = 1f;
    internal const float HornLowVolume = .92f;
    internal const float HornHighVolume = .56f;
    internal const float EngineBaseVolume = .484f;
    internal const float EngineThrottleVolume = .517f;
    internal const float ShiftPopVolume = 1.232f;
    // Use a substantial but incomplete load blend. The BMW remains distinct
    // from the Lamborghini's fully loaded V12 crossfade while gaining its
    // smoother transition away from the dry/coast layers.
    internal static float LoadBlend(float throttle) =>
        .72f * Clamp01((throttle - .13f) / .74f);
    internal static float IdleVolume(float drivingBlend) => .32f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    // Fade the inherited Car idle bed out before it can interfere with the
    // inline-six layers through the lower driving range.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .02f * limiter) / Math.Max(1f, .12f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 80f : layer == 1 ? 190f : 380f;
    // The generated layers already contain upper combustion harmonics. Keep the
    // playback fundamentals in the lower inline-six register so load sounds
    // growling rather than like a small high-speed electric motor. The full
    // curve is pitched ten percent below the previous mapping while retaining
    // its RPM response and gain staging.
    internal static float TargetHz(float normalized) =>
        (float)(52.2d * Math.Pow(261d / 52.2d, Clamp01(normalized)));
    internal static float Pitch(float normalized, int layer) => TargetHz(normalized) / ReferenceHz(layer);

    internal static float Weight(float normalized, int layer)
    {
        var position = Clamp01(normalized) * 2f;
        var lower = position < 1f ? 0 : 1;
        var blend = position - lower;
        if (layer == lower) return (float)Math.Cos(blend * Math.PI * .5d);
        if (layer == lower + 1) return (float)Math.Sin(blend * Math.PI * .5d);
        return 0f;
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}
