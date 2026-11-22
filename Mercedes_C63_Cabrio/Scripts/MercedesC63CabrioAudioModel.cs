#nullable enable
using System;

// Accepted Audi RS6R V8 acoustic mapping; local copied clips, no mod dependency.
internal static class MercedesC63CabrioAudioModel
{
    internal const float HornLowVolume = .95f;
    internal const float HornHighVolume = .58f;
    internal const float IdleBaseVolume = .60f; // idle only: -3.99 dB from .95
    internal const float EngineBaseVolume = .504f;
    internal const float EngineThrottleVolume = .280f;
    internal static float LoadBlend(float throttle) => Clamp01((throttle - .15f) / .65f);
    internal static float PlaybackVolume(float drivingBlend, float throttle) =>
        EngineVolume(throttle) * (float)Math.Sqrt(Clamp01(drivingBlend));
    internal static float IdleVolume(float drivingBlend) =>
        IdleBaseVolume * (float)Math.Sqrt(1f-Clamp01(drivingBlend));
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    // Match the accepted Audi idle-to-driving transition with independently held clips.
    internal static float DrivingBlend(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle - .04f * limiter) / Math.Max(1f, .18f * limiter));

    internal static float Normalize(float rpm, float idle, float limiter) =>
        Clamp01((rpm - idle) / Math.Max(1f, limiter - idle));

    internal static float ReferenceHz(int layer) => layer == 0 ? 96f : layer == 1 ? 176f : 320f;
    // These reference frequencies describe the recorded bank/exhaust texture,
    // not measured RPM. Keep the upper playback shift bounded to avoid a whine.
    internal static float TargetHz(float normalized) =>
        (float)(80d * Math.Pow(180d / 80d, Clamp01(normalized)));
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

