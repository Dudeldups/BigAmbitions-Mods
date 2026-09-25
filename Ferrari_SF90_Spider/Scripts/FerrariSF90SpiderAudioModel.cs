#nullable enable
using System;

// V24 keeps the accepted single audible engine source. The waveform uses the
// same simple additive-synthesis family as the proven Porsche/Lamborghini mods,
// but with an SF90-specific flat-plane V8 harmonic balance and a lower acoustic
// target range. RPM still comes directly from NWH.
internal static class FerrariSF90SpiderAudioModel
{
    internal const float EngineBaseVolume = .55f;
    internal const float EngineThrottleVolume = .40f;
    internal const float HornLowVolume = 1.00f;
    internal const float HornHighVolume = .72f;
    internal const float EngineReferenceHz = 96f;

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float TargetHz(float normalized)
    {
        var n = Clamp01(normalized);
        // Keep the perceived fundamental deeper than V22 while preserving a
        // clear, sporty rise through the SF90's upper rev range.
        var target = (float)(44d * Math.Pow(190d / 44d, n));
        var top = Clamp01((n - .72f) / .28f);
        top = top * top * (3f - 2f * top);
        return target * (1f - .08f * top);
    }

    internal static float EnginePitch(float normalized) =>
        Clamp(TargetHz(normalized) / EngineReferenceHz, .42f, 2.10f);

    internal static float EngineVolume(float throttle, float normalized)
    {
        var rpmPresence = .88f + .12f * Clamp01(normalized);
        return (EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle)) * rpmPresence;
    }

    internal static float EngineLowPass(float normalized, float throttle) =>
        2700f + 2900f * Clamp01(normalized) + 550f * Clamp01(throttle);

    internal static float EngineDistortion(float throttle) =>
        .004f + .022f * Clamp01(throttle);

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
}
