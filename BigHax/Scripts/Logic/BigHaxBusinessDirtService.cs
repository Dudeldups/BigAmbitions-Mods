#nullable enable
using System;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Items;
using Buildings.BuildingTypes.Shared.Dirtiness;
using Extensions;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxBusinessDirtService
    {
        private static readonly MethodInfo? ApplyDirtMethod = typeof(BuildingCleanlinessHelper).GetMethod(
            nameof(BuildingCleanlinessHelper.ApplyDirt),
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(BuildingRegistration), typeof(ItemInstance), typeof(int) },
            null);
        private static readonly MethodInfo? ReplacementMethod = typeof(BigHaxBusinessDirtService).GetMethod(
            nameof(ApplyDirtWithLimit), BindingFlags.NonPublic | BindingFlags.Static);

        private static int limitPercentage = 100;
        private static int diagnosticDirtCalls;
        private static ModContext? activeContext;
        private BigHaxMethodDetour? dirtDetour;
        private int lastAppliedLimit = 100;

        public void ApplyConfiguredLimit(ModContext context, BigHaxSettings settings)
        {
            var requestedLimit = Mathf.Clamp(settings.BusinessDirtLimitPercentage, 0, 100);
            if (requestedLimit == lastAppliedLimit &&
                (requestedLimit == 100 || dirtDetour?.IsApplied == true))
                return;

            if (requestedLimit < 100 && dirtDetour?.IsApplied != true)
            {
                if (ApplyDirtMethod == null || ReplacementMethod == null)
                {
                    BigHaxLogger.WarnOnce(context, "business-dirt-method-missing",
                        "BigHax: business dirt limit could not be enabled because ApplyDirt was not found.");
                    return;
                }

                dirtDetour ??= new BigHaxMethodDetour(ApplyDirtMethod, ReplacementMethod);
                if (!dirtDetour.Apply(out var error))
                {
                    context.Logger.Warn("BigHax: business dirt limit could not be enabled: " + error);
                    return;
                }
            }
            else if (requestedLimit == 100 && dirtDetour?.IsApplied == true && !dirtDetour.Restore(out var error))
            {
                context.Logger.Warn("BigHax: business dirt limit could not be disabled: " + error);
                return;
            }

            limitPercentage = requestedLimit;
            activeContext = context;
            if (requestedLimit < lastAppliedLimit)
                ClampExistingDirt(requestedLimit);
            lastAppliedLimit = requestedLimit;
            diagnosticDirtCalls = 0;
            BigHaxLogger.Info(context, "BigHax: player business dirt limit set to " + requestedLimit + "%.");
        }

        public void Shutdown(ModContext? context)
        {
            if (dirtDetour?.IsApplied == true && !dirtDetour.Restore(out var error))
                context?.Logger.Warn("BigHax: business dirt detour could not be restored: " + error);
            dirtDetour = null;
            limitPercentage = 100;
            activeContext = null;
            lastAppliedLimit = 100;
            diagnosticDirtCalls = 0;
        }

        public void RefreshExistingDirtLimit()
        {
            if (limitPercentage < 100)
                ClampExistingDirt(limitPercentage);
        }

        private static void ClampExistingDirt(int limit)
        {
            var registrations = SaveGameManager.Current?.BuildingRegistrations;
            if (registrations == null)
                return;

            var changed = false;
            foreach (var registration in registrations)
            {
                if (registration == null || !registration.RentedByPlayer || registration.dirtSpots == null)
                    continue;
                foreach (var spot in registration.dirtSpots)
                {
                    if (spot.dirtiness <= limit)
                        continue;
                    spot.dirtiness = limit;
                    changed = true;
                }
            }

            if (!changed)
                return;
            if (BuildingManager.IsInsideBuilding)
                InstanceBehavior<BuildingManager>.Instance?.UpdateDirtinessInCurrentBuilding();
            SaveGameManager.MarkChange();
        }

        // Keep the game's item-to-floor selection and accumulation behavior, changing
        // only the upper bound for dirt in player-rented buildings.
        private static void ApplyDirtWithLimit(BuildingRegistration registration, ItemInstance item, int times)
        {
            if (!registration.RentedByPlayer)
                return;
            if (item.dirtSpotsThatAffects == null)
            {
                Debug.LogWarning("Dirt spots that affects item " + item.itemName + " not found in " + registration.BusinessName);
                return;
            }

            var count = Mathf.CeilToInt(66f * item.dirtSpotsThatAffects.Count / 100f);
            foreach (var spotIndex in item.dirtSpotsThatAffects.GetRandom(count))
            {
                if (registration.dirtSpots == null || spotIndex >= registration.dirtSpots.Count)
                    continue;
                var spot = registration.dirtSpots[spotIndex];
                spot.dirtiness = Mathf.Min(limitPercentage, spot.dirtiness + 1.2f * times);
                if (BuildingManager.IsInsideBuilding &&
                    InstanceBehavior<BuildingManager>.Instance?.buildingRegistration == registration)
                    InstanceBehavior<BuildingManager>.Instance.UpdateDirtinessInSpecificSpot(spotIndex);
            }

            if (BigHaxLogger.DebugEnabled && BigHaxLogger.BusinessDirtDebugEnabled && diagnosticDirtCalls < 10)
            {
                diagnosticDirtCalls++;
                BigHaxLogger.BusinessDirtDiagnostic(activeContext,
                    "ApplyDirt entered: business=" + registration.BusinessName +
                    ", item=" + item.itemName + ", times=" + times +
                    ", limit=" + limitPercentage + "%.");
            }
        }
    }
}
