#nullable enable
using System;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxTrafficService
    {
        private static readonly FieldInfo? LastTrafficDensityField = typeof(TimeOfDayController).GetField(
            "_lastTrafficDensity",
            BindingFlags.Instance | BindingFlags.NonPublic);

        private bool originalTrafficStateCaptured;
        private bool originalTrafficSpawning;
        private int originalTrafficDensity = -1;
        private bool trafficDisabledApplied;
        private bool originalParkedCarsStateCaptured;
        private bool originalParkedCarsEnabled;
        private bool parkedCarsDisabledApplied;

        public void ApplyConfiguredBehavior(ModContext context, BigHaxSettings settings)
        {
            ApplyTrafficDisabled(context, settings.DisableTraffic);
            ApplyParkedCarsDisabled(context, settings.DisableParkedCars);
        }

        public void InvalidateCache()
        {
            trafficDisabledApplied = false;
            parkedCarsDisabledApplied = false;
        }

        public void Shutdown()
        {
            RestoreTraffic(null);
            RestoreParkedCars(null);
        }

        private void ApplyTrafficDisabled(ModContext context, bool disable)
        {
            if (!disable)
            {
                RestoreTraffic(context);
                return;
            }

            if (trafficDisabledApplied)
                return;

            if (!TryGetTrafficState(out var gameManager, out var message))
            {
                BigHaxLogger.WarnOnce(context, "traffic-disable-unavailable", "BigHax: " + message);
                return;
            }

            CaptureOriginalTrafficState(gameManager);
            gameManager.spawnTraffic = false;
            Manager.SetTrafficDensity(0);
            Manager.ClearTraffic();
            trafficDisabledApplied = true;
            BigHaxLogger.Info(context, "BigHax: disabled AI vehicle traffic and cleared active traffic vehicles.");
        }

        private void RestoreTraffic(ModContext? context)
        {
            if (!originalTrafficStateCaptured)
                return;

            if (!TryGetTrafficState(out var gameManager, out _))
                return;

            gameManager.spawnTraffic = originalTrafficSpawning;
            if (originalTrafficSpawning)
                Manager.SetTrafficDensity(originalTrafficDensity >= 0 ? originalTrafficDensity : 40);
            else
            {
                Manager.SetTrafficDensity(0);
                Manager.ClearTraffic();
            }

            originalTrafficStateCaptured = false;
            trafficDisabledApplied = false;
            BigHaxLogger.Info(context, "BigHax: restored AI vehicle traffic.");
        }

        private void ApplyParkedCarsDisabled(ModContext context, bool disable)
        {
            if (!disable)
            {
                RestoreParkedCars(context);
                return;
            }

            if (parkedCarsDisabledApplied)
                return;

            if (!originalParkedCarsStateCaptured)
            {
                originalParkedCarsEnabled = ParkingLaneGenerator.spawningActive;
                originalParkedCarsStateCaptured = true;
            }

            ApplyParkedCars(enable: false, out var laneCount);
            parkedCarsDisabledApplied = true;
            BigHaxLogger.Info(context, "BigHax: disabled parked cars and cleared " + laneCount + " parking lanes.");
        }

        private void RestoreParkedCars(ModContext? context)
        {
            if (!originalParkedCarsStateCaptured)
                return;

            if (ParkingLaneGenerator.spawningActive != originalParkedCarsEnabled)
                ApplyParkedCars(originalParkedCarsEnabled, out _);

            originalParkedCarsStateCaptured = false;
            parkedCarsDisabledApplied = false;
            BigHaxLogger.Info(context, "BigHax: restored parked cars.");
        }

        private void CaptureOriginalTrafficState(GameManager gameManager)
        {
            if (originalTrafficStateCaptured)
                return;

            originalTrafficSpawning = gameManager.spawnTraffic;
            originalTrafficDensity = ReadVanillaTrafficDensity();
            originalTrafficStateCaptured = true;
        }

        private static int ReadVanillaTrafficDensity()
        {
            try
            {
                var timeOfDay = InstanceBehavior<GameManager>.Instance?.timeOfDayController;
                return timeOfDay != null && LastTrafficDensityField?.GetValue(timeOfDay) is int density
                    ? density
                    : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static bool TryGetTrafficState(out GameManager gameManager, out string message)
        {
            gameManager = InstanceBehavior<GameManager>.Instance;
            var cityManager = InstanceBehavior<CityManager>.Instance;
            var trafficComponent = cityManager?.trafficComponent;
            if (gameManager == null || trafficComponent == null)
            {
                message = "AI vehicle traffic is unavailable in the current scene.";
                return false;
            }

            message = string.Empty;
            return true;
        }

        private static void ApplyParkedCars(bool enable, out int laneCount)
        {
            ParkingLaneGenerator.spawningActive = enable;
            var lanes = UnityEngine.Object.FindObjectsByType<ParkingLaneGenerator>(FindObjectsSortMode.None);
            laneCount = lanes.Length;
            foreach (var lane in lanes)
            {
                if (lane == null)
                    continue;

                if (enable)
                    lane.Init();
                else
                    lane.CleanupParkedVehicles(true);
            }
        }
    }
}
