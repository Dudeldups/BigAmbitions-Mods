#nullable enable
using System;

// Recording references are acoustic calibration values, not measured engine RPM.
internal static class KoenigseggJeskoAudioModel
{
    internal const float IdlePitch = 1f;
    internal const float HornLowVolume = .95f;
    internal const float HornHighVolume = .58f;
    internal const float EngineBaseVolume = .24f;
    internal const float EngineThrottleVolume = .28f;
    internal const float CrackleIdleVolume = .009f;
    internal const float CrackleLoadVolume = .024f;
    internal static float LoadBlend(float throttle) => Clamp01((throttle - .12f) / .72f);
    internal static float IdleVolume(float drivingBlend) => .58f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    // Fade the inherited low-speed idle bed out quickly; the synthesized
    // flat-plane V8 layers carry the audible engine character.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        .12f + .88f * Clamp01((rpm - idle - .015f * limiter) / Math.Max(1f, .09f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 90f : layer == 1 ? 180f : 360f;
    // Keep low and mid RPM brighter than the previous revision, then compress
    // only the top end so 7000-8500 RPM gains growl without sounding shrill.
    internal static float TargetHz(float normalized)
    {
        var position = Clamp01(normalized);
        var target = (float)(58d * Math.Pow(245d / 58d, position));
        var highRpmBlend = Clamp01((position - .68f) / .32f);
        highRpmBlend = highRpmBlend * highRpmBlend * (3f - 2f * highRpmBlend);
        return target * (1f - .30f * highRpmBlend);
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


