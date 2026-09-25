#nullable enable
using System;

internal static class ChevroletCamaro1967AudioModel
{
    internal const float IdlePitch = .76f;
    internal const float HornLowVolume = .95f;
    internal const float HornHighVolume = .58f;
    internal const float EngineBaseVolume = .170586f;
    internal const float EngineThrottleVolume = .157464f;
    internal const float CrackleIdleVolume = 0f;
    internal const float CrackleLoadVolume = 0f;
    internal static float LoadBlend(float throttle) => Clamp01((throttle - .12f) / .72f);
    internal static float IdleVolume(float drivingBlend) =>
        .84f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    internal static float DrivingBlend(float rpm, float idle, float limiter)
    {
        // Keep the separate burbling idle voice exposed. The previous blend
        // started only ~90 rpm above idle and was fully present by ~1,380 rpm.
        var start = Math.Max(idle + 550f, 0.20f * limiter);
        var full = Math.Max(start + 1f, 0.42f * limiter);
        return Clamp01((rpm - start) / (full - start));
    }
    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));
    internal static float ReferenceHz(int layer) => layer == 0 ? 40f : layer == 1 ? 74f : 122f;
    internal static float TargetHz(float normalized)
    {
        var position = Clamp01(normalized);
        return (float)(36d * Math.Pow(122d / 36d, position));
    }
    internal static float Pitch(float normalized, int layer) => TargetHz(normalized) / ReferenceHz(layer);
    internal static float Weight(float normalized, int layer)
    {
        var position = Clamp01(normalized) * 1.50f;
        var lower = position < 1f ? 0 : 1;
        var blend = position - lower;
        if (layer == lower) return (float)Math.Cos(blend * Math.PI * .5d);
        if (layer == lower + 1) return (float)Math.Sin(blend * Math.PI * .5d);
        return 0f;
    }
    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}
