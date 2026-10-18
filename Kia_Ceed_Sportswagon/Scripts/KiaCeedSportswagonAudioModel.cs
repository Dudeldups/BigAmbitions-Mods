#nullable enable
using System;

// Three synthesized bands follow the 1.5-litre four-cylinder firing order.
internal static class KiaCeedSportswagonAudioModel
{
    internal const float IdlePitch = .38f;
    internal const float HornLowVolume = .95f;
    internal const float HornHighVolume = .58f;
    // Original PCM bands measure 0.10 RMS. Match the ordinary vehicle sample
    // reference (~0.30 RMS), then keep the quieter Ceed mix and approved 15% trim.
    internal const float EngineSampleGain = 3f;
    internal const float EngineVolumeScale = .85f;
    internal const float EngineBaseVolume = .288f * EngineVolumeScale;
    internal const float EngineThrottleVolume = .288f * EngineVolumeScale;
    internal static float LoadBlend(float throttle) => Clamp01((throttle - .12f) / .72f);
    internal static float IdleVolume(float drivingBlend) =>
        .396f * EngineVolumeScale * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    // Fade the low-speed idle bed out quickly; the synthesized I4 layers carry
    // the engine character as the revs rise.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .015f * limiter) / Math.Max(1f, .09f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 65f : layer == 1 ? 190f : 390f;
    // A four-stroke inline-four has two evenly spaced firing events per crank
    // revolution: 25 Hz at 750 rpm and about 217 Hz at 6,500 rpm.
    internal static float TargetHz(float normalized)
    {
        var position = Clamp01(normalized);
        return (float)(25d * Math.Pow(217d / 25d, position));
    }
    internal static float Pitch(float normalized, int layer) => TargetHz(normalized) / ReferenceHz(layer);

    internal static float Weight(float normalized, int layer)
    {
        var position = Clamp01(normalized) * 2f;
        var lower = position < 1f ? 0 : 1;
        var blend = position - lower;
        if (layer == lower) return (float)Math.Cos(blend * Math.PI * .5d);
        if (layer == lower + 1) return (float)Math.Sin(blend * Math.PI * .5d) *
            (layer == 2 ? .72f : 1f);
        return 0f;
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}

