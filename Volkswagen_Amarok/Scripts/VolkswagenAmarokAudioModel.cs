#nullable enable
using System;

// Recording references are acoustic calibration values, not measured engine RPM.
internal static class VolkswagenAmarokAudioModel
{
    internal const float IdlePitch = .78f;
    internal const float HornLowVolume = .82f;
    internal const float HornHighVolume = .46f;
    internal const float EngineBaseVolume = .50f;
    internal const float EngineThrottleVolume = .51f;
    internal const float CrackleIdleVolume = 0f;
    internal const float CrackleLoadVolume = 0f;
    internal static float LoadBlend(float throttle) => Clamp01((throttle - .12f) / .72f);
    internal static float IdleVolume(float drivingBlend) =>
        .51f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    // Fade the inherited low-speed idle bed out quickly; the synthesized
    // V6 TDI layers carry the audible engine character.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .015f * limiter) / Math.Max(1f, .09f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 48f : layer == 1 ? 95f : 165f;
    // Keep the turbo-diesel V6 TDI distinct from the deeper idle bed
    // without pitching the synthesized combustion layers into a toy-like register.
    internal static float TargetHz(float normalized)
    {
        var position = Clamp01(normalized);
        return (float)(36d * Math.Pow(210d / 36d, position));
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

