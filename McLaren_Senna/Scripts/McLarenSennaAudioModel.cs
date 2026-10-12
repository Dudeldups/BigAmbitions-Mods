#nullable enable
using System;

// Procedurally synthesized V8 carrier frequencies, calibrated to engine speed.
internal static class McLarenSennaAudioModel
{
    internal const float IdlePitch = .68f;
    internal const float HornLowVolume = .95f;
    internal const float HornHighVolume = .58f;
    internal const float EngineBaseVolume = .3872f;
    internal const float EngineThrottleVolume = .4048f;
    internal const float CrackleIdleVolume = .009f;
    internal const float CrackleLoadVolume = .016f;
    internal static float LoadBlend(float throttle) => Clamp01((throttle - .12f) / .72f);
    internal static float IdleVolume(float drivingBlend) =>
        .50f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    // Fade the low-speed V8 idle bed out quickly; the synthesized
    // twin-turbo V8 layers carry the audible engine character.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .015f * limiter) / Math.Max(1f, .09f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 73f : layer == 1 ? 220f : 440f;
    // Auditory calibration: retain the V8 pulse relationship between layers,
    // preserve the validated lower register and soften the range above 6,000 RPM.
    internal static float TargetHz(float normalized)
    {
        var position = Clamp01(normalized);
        const double knee = (6000d - 1100d) / (8500d - 1100d);
        if (position <= knee) return (float)(50d * Math.Pow(320d / 50d, position));
        var kneeHz = 50d * Math.Pow(320d / 50d, knee);
        var progress = (position - knee) / (1d - knee);
        // An exponential roll-off is continuous at the knee and monotonic.
        return (float)(kneeHz + (220d - kneeHz) *
            (1d - Math.Exp(-2d * progress)) / (1d - Math.Exp(-2d)));
    }
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


