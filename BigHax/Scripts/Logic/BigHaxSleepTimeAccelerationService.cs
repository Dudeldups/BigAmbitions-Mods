#nullable enable
using System.Reflection;
using BAModAPI;
using Helpers;
using PlayerActivity;
using Timemachine;
using UI;
using UnityEngine;

namespace BigHax
{
    /// <summary>
    /// Speeds up only a BigHax extended bed sleep. It is driven by the game's
    /// time-machine start/end events and does no per-frame or scene-wide work.
    /// </summary>
    internal sealed class BigHaxSleepTimeAccelerationService
    {
        private const float TargetExtendedSleepSeconds = 30f;
        private const float ExtendedBedSleepThresholdMinutes = 24f * 60f;
        private const BindingFlags TimeMachineFieldFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo? TimeSpeedCurveField = typeof(TimeMachine).GetField(
            "timeSpeedCurve",
            TimeMachineFieldFlags);
        private static readonly FieldInfo? UseConstantSpeedField = typeof(TimeMachine).GetField(
            "_useConstantSpeed",
            TimeMachineFieldFlags);
        private static readonly FieldInfo? TimeDistanceField = typeof(TimeMachine).GetField(
            "_timeDistance",
            TimeMachineFieldFlags);
        private static readonly FieldInfo? SleepEnvironmentField = typeof(SleepActivity).GetField(
            "_sleepEnvironment",
            TimeMachineFieldFlags);

        private TimeMachine? acceleratedTimeMachine;
        private AnimationCurve? originalCurve;
        private bool? originalUseConstantSpeed;

        public void HandleTimeMachineStarted(ModContext? context, BigHaxSettings? settings)
        {
            if (settings?.EnableExtendedBedSleep != true)
                return;

            var ui = InstanceBehavior<UIs>.Instance;
            var timeMachine = ui?.timeMachine;
            if (ui?.playerActivityUI?.GetCurrentActivity is not SleepActivity activity ||
                SleepEnvironmentField?.GetValue(activity) is not SleepEnvironment environment ||
                environment.SleepEnvironmentType != SleepEnvironmentType.Bed)
                return;

            if (timeMachine == null || TimeSpeedCurveField == null || UseConstantSpeedField == null || TimeDistanceField == null)
            {
                BigHaxLogger.WarnOnce(context, "sleep-time-machine-fields",
                    "BigHax: extended bed sleep could not accelerate because the game time-machine fields are unavailable.");
                return;
            }

            var timeDistanceMinutes = (float)TimeDistanceField.GetValue(timeMachine);
            if (timeDistanceMinutes <= ExtendedBedSleepThresholdMinutes)
                return;

            var currentCurve = TimeSpeedCurveField.GetValue(timeMachine) as AnimationCurve;
            if (currentCurve == null || acceleratedTimeMachine != null)
                return;

            originalCurve = currentCurve;
            originalUseConstantSpeed = (bool)UseConstantSpeedField.GetValue(timeMachine);
            var speedMinutesPerSecond = timeDistanceMinutes / TargetExtendedSleepSeconds;
            TimeSpeedCurveField.SetValue(timeMachine, CreateAcceleratedCurve(speedMinutesPerSecond));
            UseConstantSpeedField.SetValue(timeMachine, false);
            acceleratedTimeMachine = timeMachine;
            BigHaxLogger.SleepDiagnostic(context,
                "Extended bed time machine accelerated: distanceMinutes=" + timeDistanceMinutes.ToString("0.##") +
                ", speedMinutesPerSecond=" + speedMinutesPerSecond.ToString("0.##") + ".");
        }

        public void RefreshActiveSleep(ModContext? context, BigHaxSettings? settings)
        {
            var timeMachine = InstanceBehavior<UIs>.Instance?.timeMachine;
            if (timeMachine != null && timeMachine.isRunning)
            {
                if (acceleratedTimeMachine == null)
                    HandleTimeMachineStarted(context, settings);
            }
            else if (acceleratedTimeMachine != null)
            {
                RestoreOriginalCurve();
            }
        }

        public void HandleTimeMachineEnded()
        {
            RestoreOriginalCurve();
        }

        public void RestoreOriginalCurve()
        {
            if (acceleratedTimeMachine != null && originalCurve != null && TimeSpeedCurveField != null)
                TimeSpeedCurveField.SetValue(acceleratedTimeMachine, originalCurve);

            if (acceleratedTimeMachine != null && originalUseConstantSpeed.HasValue && UseConstantSpeedField != null)
                UseConstantSpeedField.SetValue(acceleratedTimeMachine, originalUseConstantSpeed.Value);

            acceleratedTimeMachine = null;
            originalCurve = null;
            originalUseConstantSpeed = null;
        }

        private static AnimationCurve CreateAcceleratedCurve(float speedMinutesPerSecond)
        {
            return new AnimationCurve(
                new Keyframe(0f, speedMinutesPerSecond),
                new Keyframe(1f, speedMinutesPerSecond));
        }
    }
}
