#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BigAmbitions.DayNightCycle;
using GleyTrafficSystem;
using Helpers;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxTrafficService
    {
        private static readonly FieldInfo? LastTrafficDensityField = typeof(TimeOfDayController).GetField(
            "_lastTrafficDensity",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? SunnyEnvironmentField = typeof(TimeOfDayController).GetField(
            "environmentSettings", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? RainyEnvironmentField = typeof(TimeOfDayController).GetField(
            "environmentSettingsRain", BindingFlags.Instance | BindingFlags.NonPublic);

        private bool originalTrafficStateCaptured;
        private GameManager? capturedTrafficManager;
        private bool originalTrafficSpawning;
        private int originalTrafficDensity = -1;
        private bool trafficDisabledApplied;
        private bool multiplierApplied;
        private bool trafficRefreshRequested;
        private float appliedMultiplier = 1f;
        private VehiclePool? expandedPool;
        private float preparedMultiplier = 1f;
        private readonly Dictionary<CarType, int> originalPoolCounts = new Dictionary<CarType, int>();
        private bool originalParkedCarsStateCaptured;
        private bool originalParkedCarsEnabled;
        private bool parkedCarsDisabledApplied;

        public void ApplyConfiguredBehavior(ModContext context, BigHaxSettings settings)
        {
            PrepareTrafficPoolCapacity(context, settings);
            ApplyTrafficDisabled(context, settings.DisableTraffic);
            ApplyTrafficMultiplier(context, settings);
            ApplyParkedCarsDisabled(context, settings.DisableParkedCars);
        }

        public void PrepareTrafficPoolCapacity(ModContext context, BigHaxSettings settings)
        {
            if (TrafficManager.IsInitialized)
                return;

            var pool = TrafficComponent.Instance?.vehiclePool;
            if (pool == null || pool.trafficCars == null)
                return;
            if (ReferenceEquals(pool, expandedPool) &&
                Mathf.Approximately(preparedMultiplier, settings.AiTrafficMultiplier) &&
                !settings.DisableTraffic)
                return;

            RestoreTrafficPoolCounts();
            if (settings.DisableTraffic || settings.AiTrafficMultiplier <= 1f)
                return;

            expandedPool = pool;
            preparedMultiplier = settings.AiTrafficMultiplier;
            foreach (var carType in pool.trafficCars)
            {
                if (carType == null || carType.nrOfVehicles <= 0)
                    continue;
                originalPoolCounts[carType] = carType.nrOfVehicles;
                carType.nrOfVehicles = Mathf.CeilToInt(carType.nrOfVehicles * settings.AiTrafficMultiplier);
            }

            BigHaxLogger.Info(context,
                "BigHax: prepared AI traffic pool for " + settings.AiTrafficMultiplier.ToString("0.#") +
                "x (" + pool.GetNumberOfVehicles() + " pooled vehicles).");
        }

        public void ReapplyAfterHourlyUpdate(ModContext context, BigHaxSettings settings)
        {
            if (!multiplierApplied || settings.DisableTraffic || settings.AiTrafficMultiplier <= 1f)
                return;
            ApplyTrafficMultiplier(context, settings, force: true);
        }

        public void InvalidateCache()
        {
            trafficRefreshRequested = true;
            if (!ReferenceEquals(capturedTrafficManager, InstanceBehavior<GameManager>.Instance))
            {
                originalTrafficStateCaptured = false;
                capturedTrafficManager = null;
                originalTrafficDensity = -1;
                trafficDisabledApplied = false;
                multiplierApplied = false;
            }
            parkedCarsDisabledApplied = false;
        }

        public void Shutdown()
        {
            RestoreTraffic(null);
            RestoreParkedCars(null);
            RestoreTrafficPoolCounts();
        }

        private void ApplyTrafficMultiplier(ModContext context, BigHaxSettings settings, bool force = false)
        {
            if (settings.DisableTraffic)
                return;

            var requestedMultiplier = settings.AiTrafficMultiplier;
            if (requestedMultiplier <= 1f)
            {
                if (multiplierApplied)
                    RestoreTraffic(context);
                return;
            }
            if (!force && !trafficRefreshRequested && multiplierApplied && Mathf.Approximately(appliedMultiplier, requestedMultiplier))
                return;
            if (!TryGetTrafficState(out var gameManager, out var message) || !TrafficManager.IsInitialized)
            {
                BigHaxLogger.TrafficDiagnostic(context, "AI traffic multiplier waiting for initialized traffic: " + message);
                return;
            }

            CaptureOriginalTrafficState(gameManager);
            var vanillaDensity = ReadVanillaTrafficDensity();
            if (vanillaDensity < 0)
            {
                BigHaxLogger.WarnOnce(context, "traffic-density-unknown",
                    "BigHax: could not read the game's AI traffic density; using 40 as the baseline.");
                vanillaDensity = 40;
            }
            var capacity = InstanceBehavior<CityManager>.Instance?.trafficComponent?.vehiclePool?.GetNumberOfVehicles() ?? 0;
            if (capacity <= 0)
                return;
            var requestedCount = Mathf.RoundToInt(vanillaDensity * requestedMultiplier);
            var target = Mathf.Clamp(requestedCount, 0, capacity);
            gameManager.spawnTraffic = true;
            Manager.SetTrafficDensity(target);
            multiplierApplied = true;
            trafficRefreshRequested = false;
            appliedMultiplier = requestedMultiplier;
            if (target < requestedCount)
                BigHaxLogger.WarnOnce(context, "traffic-pool-capacity",
                    "BigHax: AI traffic was capped at " + capacity + " vehicles; restart the game after increasing the multiplier.");
            if (!force)
                BigHaxLogger.Info(context,
                    "BigHax: AI traffic set to " + requestedMultiplier.ToString("0.#") + "x (" + target + " vehicles).");
            else
                BigHaxLogger.TrafficDiagnostic(context,
                    "Hourly AI traffic refreshed: vanilla=" + vanillaDensity + ", target=" + target + ", capacity=" + capacity + ".");
        }

        private void ApplyTrafficDisabled(ModContext context, bool disable)
        {
            if (!disable)
            {
                if (trafficDisabledApplied)
                    RestoreTraffic(context);
                return;
            }

            if (trafficDisabledApplied && !trafficRefreshRequested)
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
            trafficRefreshRequested = false;
            BigHaxLogger.Info(context, "BigHax: disabled AI vehicle traffic and cleared active traffic vehicles.");
        }

        private void RestoreTraffic(ModContext? context)
        {
            if (!originalTrafficStateCaptured)
                return;

            if (!TryGetTrafficState(out var gameManager, out _))
                return;
            if (!ReferenceEquals(gameManager, capturedTrafficManager))
            {
                originalTrafficStateCaptured = false;
                capturedTrafficManager = null;
                return;
            }

            gameManager.spawnTraffic = originalTrafficSpawning;
            if (originalTrafficSpawning)
            {
                var currentVanillaDensity = ReadVanillaTrafficDensity();
                Manager.SetTrafficDensity(currentVanillaDensity >= 0
                    ? currentVanillaDensity
                    : originalTrafficDensity >= 0 ? originalTrafficDensity : 40);
            }
            else
            {
                Manager.SetTrafficDensity(0);
                Manager.ClearTraffic();
            }

            originalTrafficStateCaptured = false;
            capturedTrafficManager = null;
            trafficDisabledApplied = false;
            multiplierApplied = false;
            trafficRefreshRequested = false;
            appliedMultiplier = 1f;
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
            if (originalTrafficStateCaptured && ReferenceEquals(capturedTrafficManager, gameManager))
                return;

            capturedTrafficManager = gameManager;
            originalTrafficSpawning = gameManager.spawnTraffic;
            originalTrafficDensity = ReadVanillaTrafficDensity();
            originalTrafficStateCaptured = true;
        }

        private static int ReadVanillaTrafficDensity()
        {
            try
            {
                var timeOfDay = InstanceBehavior<GameManager>.Instance?.timeOfDayController;
                if (timeOfDay == null)
                    return -1;
                var sunny = SunnyEnvironmentField?.GetValue(timeOfDay);
                var rainy = RainyEnvironmentField?.GetValue(timeOfDay);
                var sunnyCurve = sunny?.GetType().GetField("numberOfAiVehicles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(sunny) as AnimationCurve;
                var rainyCurve = rainy?.GetType().GetField("numberOfAiVehicles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(rainy) as AnimationCurve;
                var save = SaveGameManager.Current;
                if (sunnyCurve != null && rainyCurve != null && save != null)
                {
                    // TimeOfDayController.GetCurrentHour uses the integer save hour.
                    var hour = save.Hour;
                    var dryValue = sunnyCurve.Evaluate(hour);
                    var wetValue = rainyCurve.Evaluate(hour);
                    var progress = RainHelper.isRaining ? RainHelper.environmentTransitionProgress : 0f;
                    return Mathf.RoundToInt(Mathf.Lerp(dryValue, wetValue, progress));
                }
                return LastTrafficDensityField?.GetValue(timeOfDay) is int density ? density : -1;
            }
            catch
            {
                return -1;
            }
        }

        private void RestoreTrafficPoolCounts()
        {
            foreach (var entry in originalPoolCounts)
                if (entry.Key != null)
                    entry.Key.nrOfVehicles = entry.Value;
            originalPoolCounts.Clear();
            expandedPool = null;
            preparedMultiplier = 1f;
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
