#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxBuildingCustomerCapacityService
    {
        private readonly Dictionary<BuildingSizeData, int[]> originalCapacities = new Dictionary<BuildingSizeData, int[]>();
        private readonly Dictionary<string, int> originalRegistrationCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> originalRegistrationBusinessTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> lastAppliedRegistrationCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<BuildingSizeData> resolvedBuildingSizeData = new List<BuildingSizeData>();
        private float lastAppliedMultiplier = 1f;

        public void InvalidateCache()
        {
            resolvedBuildingSizeData.Clear();
        }

        public void ApplyConfiguredCapacities(ModContext context, BigHaxSettings settings)
        {
            var multiplier = Mathf.Max(1f, settings.BuildingCustomerCapacityMultiplier);
            var buildingSizeDataEntries = ResolveBuildingSizeData();
            if (buildingSizeDataEntries.Count == 0)
            {
                BigHaxLogger.WarnOnce(context, "missing-building-size-data", "BigHax: could not resolve any BuildingSizeData definitions.");
            }

            var changedDefinitions = 0;
            foreach (var buildingSizeData in buildingSizeDataEntries)
            {
                var customerCapacities = buildingSizeData.customerCapacities;
                if (customerCapacities == null || customerCapacities.Length == 0)
                    continue;

                if (!originalCapacities.TryGetValue(buildingSizeData, out var originalValues) ||
                    originalValues.Length != customerCapacities.Length)
                {
                    originalValues = new int[customerCapacities.Length];
                    for (var index = 0; index < customerCapacities.Length; index++)
                        originalValues[index] = customerCapacities[index]?.amount ?? 0;

                    originalCapacities[buildingSizeData] = originalValues;
                }

                for (var index = 0; index < customerCapacities.Length; index++)
                {
                    var customerCapacity = customerCapacities[index];
                    if (customerCapacity == null)
                        continue;

                    var originalAmount = originalValues[index];
                    var targetAmount = originalAmount <= 0 || multiplier <= 1f
                        ? originalAmount
                        : Mathf.Max(originalAmount, Mathf.CeilToInt(originalAmount * multiplier));

                    if (customerCapacity.amount != targetAmount)
                    {
                        customerCapacity.amount = targetAmount;
                        changedDefinitions++;
                    }
                }
            }

            var changedRegistrations = ApplyPlayerBusinessRegistrationCapacities(multiplier);
            if (changedDefinitions > 0 ||
                changedRegistrations > 0 ||
                !Mathf.Approximately(lastAppliedMultiplier, multiplier))
            {
                BigHaxLogger.Info(
                    context,
                    $"BigHax: applied building customer capacity multiplier x{multiplier}. buildingCapacityEntriesChanged={changedDefinitions}, playerBusinessesChanged={changedRegistrations}.");
            }

            lastAppliedMultiplier = multiplier;
        }

        public void RestoreOriginalCapacities()
        {
            foreach (var pair in originalCapacities)
            {
                var buildingSizeData = pair.Key;
                var originalValues = pair.Value;
                var customerCapacities = buildingSizeData.customerCapacities;
                if (customerCapacities == null)
                    continue;

                var max = Mathf.Min(customerCapacities.Length, originalValues.Length);
                for (var index = 0; index < max; index++)
                {
                    if (customerCapacities[index] != null)
                        customerCapacities[index].amount = originalValues[index];
                }
            }

            foreach (var registration in GetPlayerBusinessRegistrations())
            {
                var key = GetRegistrationKey(registration);
                if (originalRegistrationCapacities.TryGetValue(key, out var originalCapacity))
                    registration.customerCapacity = originalCapacity;
            }

            lastAppliedRegistrationCapacities.Clear();
            lastAppliedMultiplier = 1f;
        }

        private int ApplyPlayerBusinessRegistrationCapacities(float multiplier)
        {
            var registrations = GetPlayerBusinessRegistrations();
            PruneRemovedRegistrationState(registrations);

            var changedRegistrations = 0;
            foreach (var registration in registrations)
            {
                var key = GetRegistrationKey(registration);
                var businessType = registration.businessTypeName ?? string.Empty;
                if (originalRegistrationBusinessTypes.TryGetValue(key, out var capturedBusinessType) &&
                    !string.Equals(capturedBusinessType, businessType, StringComparison.Ordinal))
                {
                    ResetRegistrationBaseline(key);
                }

                var currentCapacity = registration.customerCapacity;
                var hasBaseCapacity = originalRegistrationCapacities.TryGetValue(key, out var baseCapacity);
                var hasLastAppliedCapacity = lastAppliedRegistrationCapacities.TryGetValue(key, out var lastAppliedCapacity);
                if (currentCapacity <= 0)
                {
                    if (!hasBaseCapacity || (hasLastAppliedCapacity && currentCapacity != lastAppliedCapacity))
                        ResetRegistrationBaseline(key);

                    lastAppliedRegistrationCapacities.Remove(key);
                    continue;
                }

                if (!hasBaseCapacity || (hasLastAppliedCapacity && currentCapacity != lastAppliedCapacity))
                {
                    baseCapacity = currentCapacity;
                    originalRegistrationCapacities[key] = baseCapacity;
                    originalRegistrationBusinessTypes[key] = businessType;
                }

                if (baseCapacity <= 0)
                    continue;

                var targetCapacity = multiplier <= 1f
                    ? baseCapacity
                    : Mathf.Max(baseCapacity, Mathf.CeilToInt(baseCapacity * multiplier));

                if (registration.customerCapacity != targetCapacity)
                {
                    registration.customerCapacity = targetCapacity;
                    changedRegistrations++;
                }

                lastAppliedRegistrationCapacities[key] = targetCapacity;
            }

            return changedRegistrations;
        }

        private void PruneRemovedRegistrationState(List<BuildingRegistration> registrations)
        {
            if (registrations.Count == 0)
                return;

            var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var registration in registrations)
                activeKeys.Add(GetRegistrationKey(registration));

            var trackedKeys = new List<string>(originalRegistrationCapacities.Keys);
            foreach (var key in trackedKeys)
            {
                if (!activeKeys.Contains(key))
                    ResetRegistrationBaseline(key);
            }
        }

        private void ResetRegistrationBaseline(string key)
        {
            originalRegistrationCapacities.Remove(key);
            originalRegistrationBusinessTypes.Remove(key);
            lastAppliedRegistrationCapacities.Remove(key);
        }

        private static List<BuildingRegistration> GetPlayerBusinessRegistrations()
        {
            var registrations = new List<BuildingRegistration>();
            var saveGame = SaveGameManager.Current;
            if (saveGame?.BuildingRegistrations == null)
                return registrations;

            foreach (var registration in saveGame.BuildingRegistrations)
            {
                if (registration == null ||
                    !registration.RentedByPlayer ||
                    string.IsNullOrWhiteSpace(registration.businessTypeName))
                {
                    continue;
                }

                registrations.Add(registration);
            }

            return registrations;
        }

        private static string GetRegistrationKey(BuildingRegistration registration)
        {
            return registration.StreetName + "|" + registration.StreetNumber;
        }

        private List<BuildingSizeData> ResolveBuildingSizeData()
        {
            if (resolvedBuildingSizeData.Count > 0)
                return resolvedBuildingSizeData;

            foreach (var buildingSizeData in Resources.FindObjectsOfTypeAll<BuildingSizeData>())
            {
                if (buildingSizeData != null && !resolvedBuildingSizeData.Contains(buildingSizeData))
                    resolvedBuildingSizeData.Add(buildingSizeData);
            }

            return resolvedBuildingSizeData;
        }
    }
}
