#nullable enable
using System;

// Recording references are acoustic calibration values, not measured engine RPM.
internal static class CadillacEscaladeAudioModel
{
    internal const float IdlePitch = .86f;
    internal const float IdleBaseVolume = .18f;
    internal const float HornLowVolume = .95f;
    internal const float HornHighVolume = .58f;
    internal const float EngineBaseVolume = .31f;
    internal const float EngineThrottleVolume = .25f;

    internal static float LoadBlend(float throttle) =>
        Clamp01((throttle - .18f) / .68f);

    internal static float IdleVolume(float drivingBlend) =>
        IdleBaseVolume * (float)Math.Sqrt(1f - Clamp01(drivingBlend));

    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);

    // Let the natural donor bed cover idle and creep. The Cadillac layers fade
    // in progressively from roughly 1,000 to 2,200 RPM instead of producing a
    // pulsing synthesized growl immediately off idle.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .06f * limiter) / Math.Max(1f, .20f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) =>
        layer == 0 ? 80f : layer == 1 ? 220f : 480f;

    // Compress the firing-frequency sweep to retain a deep full-size-SUV tone
    // at load. A low-pass filter removes the brittle upper harmonic tail.
    internal static float TargetHz(float normalized) =>
        (float)(42d * Math.Pow(230d / 42d, Clamp01(normalized)));

    internal static float Pitch(float normalized, int layer) =>
        TargetHz(normalized) / ReferenceHz(layer);

    internal static float Weight(float normalized, int layer)
    {
        var position = Clamp01(normalized) * 2f;
        var lower = position < 1f ? 0 : 1;
        var blend = position - lower;
        if (layer == lower)
            return (float)Math.Cos(blend * Math.PI * .5d);
        if (layer == lower + 1)
            return (float)Math.Sin(blend * Math.PI * .5d);
        return 0f;
    }

    private static float Clamp01(float value) =>
        Math.Max(0f, Math.Min(1f, value));
}
