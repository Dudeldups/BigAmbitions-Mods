#nullable enable
using System;

// Synthetic loop calibration: low exhaust orders carry body without a steep whistle.
internal static class DodgeRam1500TRXAudioModel
{
    // Apply gain to PCM once at load: AudioSource.volume itself is capped at one.
    // The quiet synthesized loops retain ample headroom across all band blends.
    internal const float EngineSampleGain = 2f;
    internal const float IdleBaseVolume = .75f;
    // The native one-metre reference heavily attenuates the game's chase camera.
    internal const float EngineMinimumDistance = 4f;
    internal const float IdlePitch = .53f;
    internal const float HornLowVolume = .95f;
    internal const float HornHighVolume = .58f;
    internal const float EngineBaseVolume = .44f;
    internal const float EngineThrottleVolume = .46f;
    internal const float CrackleIdleVolume = .009f;
    internal const float CrackleLoadVolume = .016f;
    internal static float LoadBlend(float throttle) => Clamp01((throttle - .12f) / .72f);
    internal static float IdleVolume(float drivingBlend) =>
        IdleBaseVolume * (float)Math.Sqrt(1f - Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    // Fade the dedicated low-speed idle bed out quickly; the synthesized
    // supercharged V8 layers carry the audible engine character.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .015f * limiter) / Math.Max(1f, .09f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 88f : layer == 1 ? 168f : 312f;
    // Acoustic RPM response is compressed above idle; physical engine RPM,
    // power and shift points remain authoritative and unchanged.
    internal static float TargetHz(float normalized)
    {
        var position = Clamp01(normalized);
        return 46.5f + 270f * position;
    }
    internal static float Pitch(float normalized, int layer) => TargetHz(normalized) / ReferenceHz(layer);

    internal static float Weight(float normalized, int layer)
    {
        var hz = TargetHz(normalized);
        if (hz <= ReferenceHz(0)) return layer == 0 ? 1f : 0f;
        if (hz >= ReferenceHz(2)) return layer == 2 ? 1f : 0f;
        var lower = hz < ReferenceHz(1) ? 0 : 1;
        var blend = (float)(Math.Log(hz / ReferenceHz(lower)) /
                            Math.Log(ReferenceHz(lower + 1) / ReferenceHz(lower)));
        if (layer == lower) return (float)Math.Cos(blend * Math.PI * .5d);
        if (layer == lower + 1) return (float)Math.Sin(blend * Math.PI * .5d);
        return 0f;
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}

