#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

internal enum BMWM4G82ShiftPopEvent
{
    None,
    ThrottleLift,
    Upshift,
    Downshift,
}

// Detects driver lifts and genuine transmission changes, then schedules short
// bounded bursts. Driver throttle is used so the automatic shift-cut cannot
// masquerade as an overrun event.
internal sealed class BMWM4G82ShiftPopGate
{
    private readonly System.Random random = new System.Random();
    private readonly Queue<ThrottlePoint> history = new Queue<ThrottlePoint>();

    private struct ThrottlePoint
    {
        internal double Time;
        internal float Throttle;
    }

    private bool valid;
    private bool liftArmed;
    private int previousGear;
    private float previousRpm;
    private float previousThrottle;
    private double previousTime;
    private double loadSince;
    private double lastHistoryTime;
    private double nextPop;
    private double cooldownUntil;
    private double downshiftUntil;
    private float downshiftBaseRpm;
    private float downshiftPeakRpm;
    private int remaining;
    private BMWM4G82ShiftPopEvent burstEvent;

    internal float Intensity { get; private set; }

    internal void Reset()
    {
        valid = false;
        liftArmed = false;
        history.Clear();
        previousGear = 0;
        previousRpm = 0f;
        previousThrottle = 0f;
        previousTime = 0d;
        loadSince = double.NaN;
        lastHistoryTime = double.NegativeInfinity;
        nextPop = double.PositiveInfinity;
        cooldownUntil = 0d;
        downshiftUntil = double.PositiveInfinity;
        downshiftBaseRpm = 0f;
        downshiftPeakRpm = 0f;
        remaining = 0;
        burstEvent = BMWM4G82ShiftPopEvent.None;
        Intensity = 0f;
    }

    internal BMWM4G82ShiftPopEvent Sample(
        bool active,
        double time,
        float rpm,
        float throttle,
        int gear,
        float speedKph)
    {
        if (!active || gear <= 0)
        {
            Reset();
            return BMWM4G82ShiftPopEvent.None;
        }

        throttle = Mathf.Clamp01(throttle);
        var continuous = valid && time >= previousTime && time - previousTime <= .5d;
        if (!continuous)
            Reset();
        while (history.Count > 0 && time - history.Peek().Time > .20d)
            history.Dequeue();

        if (throttle >= .48f)
        {
            if (double.IsNaN(loadSince))
                loadSince = time;
            if (time - loadSince >= .10d)
                liftArmed = true;
        }
        else
        {
            loadSince = double.NaN;
        }

        if (continuous && gear < previousGear)
        {
            downshiftBaseRpm = previousRpm;
            downshiftPeakRpm = rpm;
            downshiftUntil = time + .18d;
        }
        else if (continuous && gear > previousGear)
        {
            downshiftUntil = double.PositiveInfinity;
        }

        if (!double.IsPositiveInfinity(downshiftUntil))
        {
            downshiftPeakRpm = Math.Max(downshiftPeakRpm, rpm);
            if (time >= downshiftUntil)
            {
                var flare = Math.Max(0f, downshiftPeakRpm - downshiftBaseRpm);
                var strength = Smooth(240f, 1450f, flare) *
                               Smooth(1750f, 6100f, downshiftPeakRpm) *
                               Smooth(7f, 32f, speedKph);
                if (strength > .12f)
                    Schedule(
                        BMWM4G82ShiftPopEvent.Downshift,
                        time,
                        .58f + .42f * strength,
                        strength > .72f ? 2 : 1);
                downshiftUntil = double.PositiveInfinity;
            }
        }

        if (continuous && liftArmed && throttle <= .18f && previousThrottle > .22f)
        {
            liftArmed = false;
            var peak = new ThrottlePoint { Time = time, Throttle = throttle };
            foreach (var point in history)
                if (point.Throttle > peak.Throttle) peak = point;
            var drop = peak.Throttle - throttle;
            var rate = drop / (float)Math.Max(.035d, time - peak.Time);
            var abrupt = Smooth(.34f, .80f, drop) * Smooth(1.8f, 5.6f, rate);
            var strength = abrupt * Smooth(1650f, 5700f, rpm) * LiftGearFactor(gear, rpm);
            if (strength > .14f && random.NextDouble() < .45d + .50d * strength)
                Schedule(
                    BMWM4G82ShiftPopEvent.ThrottleLift,
                    time,
                    .55f + .42f * strength,
                    strength > .62f ? 2 : 1);
        }
        else if (continuous && liftArmed &&
                 (previousGear == 1 || previousGear == 2) && gear == previousGear + 1)
        {
            // Make the characteristic low-gear shift crack reliable. The sound
            // variant, pitch and possible second report still vary per event.
            var strength = Smooth(2500f, 6500f, previousRpm) *
                           Smooth(.30f, .88f, previousThrottle) *
                           Smooth(5f, 28f, speedKph);
            if (strength > .10f)
                Schedule(
                    BMWM4G82ShiftPopEvent.Upshift,
                    time,
                    .66f + .34f * strength,
                    previousGear == 1 && strength > .48f ? 2 : 1);
        }

        if (history.Count == 0 || time - lastHistoryTime >= .01d)
        {
            history.Enqueue(new ThrottlePoint { Time = time, Throttle = throttle });
            lastHistoryTime = time;
        }
        previousGear = gear;
        previousRpm = rpm;
        previousThrottle = throttle;
        previousTime = time;
        valid = true;

        if (rpm < 1350f ||
            (throttle > .42f && burstEvent == BMWM4G82ShiftPopEvent.ThrottleLift))
            remaining = 0;
        if (remaining <= 0 || time < nextPop)
            return BMWM4G82ShiftPopEvent.None;

        remaining--;
        nextPop = time + (burstEvent == BMWM4G82ShiftPopEvent.Upshift ? .075d : .105d);
        return burstEvent;
    }

    private void Schedule(
        BMWM4G82ShiftPopEvent popEvent,
        double time,
        float intensity,
        int count)
    {
        if (time < cooldownUntil || remaining > 0)
            return;
        burstEvent = popEvent;
        Intensity = Mathf.Clamp01(intensity);
        remaining = Math.Max(1, count);
        nextPop = time + (popEvent == BMWM4G82ShiftPopEvent.Upshift ? .018d :
            popEvent == BMWM4G82ShiftPopEvent.Downshift ? .032d : .045d);
        cooldownUntil = time + (popEvent == BMWM4G82ShiftPopEvent.Upshift ? .38d :
            popEvent == BMWM4G82ShiftPopEvent.Downshift ? .55d : .65d);
    }

    private static float LiftGearFactor(int gear, float rpm) =>
        gear <= 2 ? 1f :
        gear == 3 ? .88f :
        gear == 4 ? .44f + .38f * Smooth(3900f, 6100f, rpm) :
        .08f + .60f * Smooth(4400f, 6600f, rpm);

    private static float Smooth(float low, float high, float value)
    {
        var normalized = Mathf.Clamp01((value - low) / Math.Max(.0001f, high - low));
        return normalized * normalized * (3f - 2f * normalized);
    }
}
