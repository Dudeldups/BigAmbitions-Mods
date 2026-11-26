#nullable enable
using System;

internal static class LamborghiniAventadorAudioModel
{
    // Feature diagnostics are intentionally independent from the mod-wide
    // debug switch and remain disabled in release builds.
    internal static bool AudioDebugEnabled { get; set; } = false;

    internal const float IdleBaseVolume = .48f;
    internal const float EngineBaseVolume = .60f;
    internal const float EngineThrottleVolume = .58f;
    // Apply once to both idle and layered engine voices on every mixer route.
    // This is a roughly 3.7 dB reduction while preserving the existing blend.
    internal const float EngineMasterGain = .65f;
    // Keep the dedicated engine layers in their near field. With logarithmic
    // rolloff, the inherited 1 m minimum attenuates them quickly beyond 1 m.
    internal const float EngineMinDistance = 5f;
    internal const float HornLowVolume = .64f;
    internal const float HornHighVolume = .42f;

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .015f * limiter) / Math.Max(1f, .09f * limiter));

    internal static float LoadBlend(float throttle) =>
        Clamp01((throttle - .12f) / .72f);

    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);

    internal static float IdleVolume(float drivingBlend) =>
        IdleBaseVolume * (float)Math.Sqrt(1f - Clamp01(drivingBlend));

    internal static float TargetHz(float normalized) =>
        (float)(75d * Math.Pow(300d / 75d, Clamp01(normalized)));

    internal static float ReferenceHz(int layer) => layer == 0 ? 100f : layer == 1 ? 350f : 750f;

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
