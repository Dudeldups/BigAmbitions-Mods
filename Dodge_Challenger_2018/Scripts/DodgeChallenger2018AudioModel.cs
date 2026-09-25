#nullable enable
using System;

internal static class DodgeChallenger2018AudioModel
{
    internal const float HornLowVolume = .95f;
    internal const float HornHighVolume = .58f;
    internal const float EngineBaseVolume = .68f;
    internal const float EngineThrottleVolume = .22f;
    internal const float PlaybackReferenceRpm = 900f;
    internal const int EngineCycles = 24;

    // The stems share a crank-phase grid. Linear crossfades avoid boosting the
    // correlated combustion content in the middle of the idle/drive transition.
    internal static float IdleVolume(float drivingBlend) =>
        .62f * (1f - Clamp01(drivingBlend));

    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);

    // The recording showed that the smooth body dominated too heavily. Keep
    // the same synchronized HEMI stem, but let the combustion/exhaust edge carry
    // more of the perceived load so it no longer resembles a generic road car.
    internal static float BarkVolume(float throttle) =>
        .18f + .50f * (float)Math.Pow(Clamp01(throttle), .72d);

    internal static float DrivingBlend(float rpm, float idle, float limiter)
    {
        var start = Math.Max(idle + 260f, 1010f);
        var full = Math.Max(start + 1f, idle + 900f);
        return Clamp01((rpm - start) / (full - start));
    }

    internal static float EnginePitch(float rpm, float idle, float limiter)
    {
        // Every WAV has the same length and playback reference. Set THIS SAME
        // value on all three sources, including the currently inaudible ones.
        idle = Math.Max(1f, idle);
        if (float.IsNaN(rpm) || float.IsInfinity(rpm))
            rpm = idle;
        rpm = Math.Max(idle, rpm);
        var start = Math.Max(idle + 260f, 1010f);
        var full = Math.Max(start + 1f, idle + 900f);
        if (rpm >= full)
            return EstablishedDrivePitch(rpm, idle, limiter);

        var blend = Clamp01((rpm - idle) / Math.Max(1f, full - idle));
        var eased = blend * blend * (3f - 2f * blend);
        var idlePitch = idle / PlaybackReferenceRpm;
        return idlePitch +
               (EstablishedDrivePitch(full, idle, limiter) - idlePitch) * eased;
    }

    private static float EstablishedDrivePitch(float rpm, float idle, float limiter)
    {
        var floor = Math.Max(idle, 900f);
        var position = Clamp01((rpm - floor) / Math.Max(1f, limiter - floor));
        return (1800f / PlaybackReferenceRpm) *
               (float)(.68d * Math.Pow(1.45d / .68d, position));
    }

    private static float Clamp01(float value) =>
        Math.Max(0f, Math.Min(1f, value));
}
