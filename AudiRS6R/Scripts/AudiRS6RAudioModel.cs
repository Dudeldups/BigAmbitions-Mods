#nullable enable
using System;

// Recording references are acoustic calibration values, not measured engine RPM.
internal static class AudiRS6RAudioModel
{
    internal const float IdlePitch = 1f;
    internal const float HornVolume = .72f;
    internal const float PopVolume = .48f;
    internal const float PopPitchMin = .96f;
    internal const float PopPitchMax = 1.01f;
    internal static float LoadBlend(float throttle) => Clamp01((throttle - .15f) / .65f);
    internal static float IdleVolume(float drivingBlend) => .24f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    // Restore revision 7's idle-to-driving thresholds (1180..2440 RPM with
    // 900 RPM idle / 7000 RPM limiter). Throttle cannot change idle gain/pitch.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .04f * limiter) / Math.Max(1f, .18f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 96f : layer == 1 ? 176f : 320f;
    internal static float TargetHz(float normalized) => (float)(80d * Math.Pow(180d / 80d, Clamp01(normalized)));
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
