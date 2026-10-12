#nullable enable
using System;

// Pitch is calibrated directly to the supplied recording's 740 RPM metadata.
internal static class VolkswagenT1BusAudioModel
{
    internal const float HornLowVolume = .82f;
    internal const float HornHighVolume = .46f;
    internal const float EngineBaseVolume = .76f;
    internal const float EngineThrottleVolume = .19f;
    internal const float DriveBlendStartRpm = 1250f;
    internal const float DriveBlendEndRpm = 2300f;
    // The supplied drive loop's dominant firing cadence is about 70.6 Hz.
    // A four-stroke four-cylinder engine fires twice per crank revolution,
    // so the loop represents approximately 2,120 RPM at 1x pitch.
    internal const float DriveLoopReferenceRpm = 2120f;
    internal const float DriveHighRpmCompressionExponent = .30f;
    internal static float EngineVolume(float throttle) =>
        EngineBaseVolume + EngineThrottleVolume * Clamp01(throttle);
    internal static float DriveBlend(float rpm)
    {
        var amount = Clamp01((rpm - DriveBlendStartRpm) / (DriveBlendEndRpm - DriveBlendStartRpm));
        return amount * amount * (3f - 2f * amount);
    }

    // The idle loop was resampled to 2x its recorded RPM; at 1x playback it
    // therefore represents 1,480 RPM.
    internal static float EnginePitch(float rpm) => Math.Max(.50f, Math.Min(2.95f, rpm / 1480f));

    // Match the measured engine cadence through the idle-to-drive transition,
    // then compress the upper range so the worn air-cooled engine does not gain
    // the steep, sporty pitch sweep of a proportional 4,300 RPM mapping.
    internal static float DrivePitch(float rpm)
    {
        var transitionPitch = DriveBlendEndRpm / DriveLoopReferenceRpm;
        var pitch = rpm <= DriveBlendEndRpm
            ? rpm / DriveLoopReferenceRpm
            : transitionPitch * (float)Math.Pow(
                rpm / DriveBlendEndRpm,
                DriveHighRpmCompressionExponent);
        return Math.Max(.50f, Math.Min(1.50f, pitch));
    }

    private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
}

