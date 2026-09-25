#nullable enable
using System;

// Recording references are acoustic calibration values, not measured engine RPM.
internal static class BugattiChironAudioModel
{
    internal const float IdlePitch = 0.88f;
    internal const float HornVolume = 1f;
    internal const float EngineBaseVolume = 0.44f;
    internal const float EngineThrottleVolume = 0.50f;
    internal const float TurboIdleVolume = 0.002f;
    internal const float TurboLoadVolume = 0.010f;

    internal static float LoadBlend(float throttle) => Clamp01((throttle - 0.03f) / 0.58f);
    internal static float IdleVolume(float drivingBlend) =>
        0.29f * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - 0.012f * limiter) / Math.Max(1f, 0.085f * limiter));
    internal static float TurboVolume(float normalizedRpm, float throttle) =>
        TurboIdleVolume +
        (TurboLoadVolume - TurboIdleVolume) *
        (float)Math.Pow(Clamp01(normalizedRpm), 1.45d) *
        (0.22f + 0.78f * Clamp01(throttle));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 110f : layer == 1 ? 360f : 720f;

    // The W16 fires eight times per crankshaft revolution. Keep the audible
    // mapping substantially below the naturally aspirated V12 range: the
    // Chiron should build a dense, forceful growl rather than an electric buzz.
    internal static float TargetHz(float normalized) =>
        (float)(42d * Math.Pow(145d / 42d, Clamp01(normalized)));
    internal static float Pitch(float normalized, int layer) =>
        TargetHz(normalized) / ReferenceHz(layer);

    internal static float Weight(float normalized, int layer)
    {
        var position = Clamp01(normalized) * 2f;
        var lower = position < 1f ? 0 : 1;
        var blend = position - lower;
        if (layer == lower)
            return (float)Math.Cos(blend * Math.PI * 0.5d);
        if (layer == lower + 1)
            return (float)Math.Sin(blend * Math.PI * 0.5d);
        return 0f;
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}
