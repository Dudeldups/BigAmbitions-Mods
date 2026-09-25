#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using UnityEngine;

namespace BigHax
{
    internal sealed class BigHaxCustomerTrafficService
    {
        private const float AdditionalPromotionBoostPerMultiplierStep = 0.25f;

        private static readonly MethodInfo MemberwiseCloneMethod =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly Type? CustomerEntriesHelperType = FindType("AI.Customers.CustomerEntries.CustomerEntriesHelper");
        private static readonly MethodInfo? UpdateAllCustomerEntriesMethod =
            CustomerEntriesHelperType?.GetMethod("UpdateCustomerEntriesForAllPlayerBusinesses", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo? UpdateCustomerEntriesForPlayerBusinessMethod =
            CustomerEntriesHelperType?.GetMethod("UpdateCustomerEntriesForPlayerBusiness", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo? GetEntriesByAddressMethod =
            CustomerEntriesHelperType?.GetMethod("GetEntriesByAddress", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo? ShouldEntriesBeCreatedMethod =
            CustomerEntriesHelperType?.GetMethod("ShouldEntriesBeCreated", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly Type? TimeHelperType = FindType("TimeHelper");
        private static readonly MethodInfo? GetDayOfWeekMethod =
            TimeHelperType?.GetMethod("GetDayOfWeek", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);

        private static readonly Type? CustomerEntryType = FindType("AI.Customers.CustomerEntries.CustomerEntry");
        private static readonly Type? OrderType = FindType("Order");
        private static readonly Type? TimestampType = FindType("BigAmbitions.DayNightCycle.Timestamp");

        private static readonly FieldInfo? CustomerEntrySpawnTimeField = CustomerEntryType?.GetField("spawnTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? CustomerEntryCompletedField = CustomerEntryType?.GetField("completed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? CustomerEntryOrderField = CustomerEntryType?.GetField("order", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? OrderEntriesField = OrderType?.GetField("entries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? OrderCompletedField = OrderType?.GetField("completed", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? OrderCustomerDemandTypesField = OrderType?.GetField("customerDemandTypes", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? TimestampDayField = TimestampType?.GetField("Day", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? TimestampHourField = TimestampType?.GetField("Hour", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo? TimestampMinuteField = TimestampType?.GetField("Minute", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly Dictionary<string, int> originalCustomerCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> originalCustomerCapacityBusinessTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> lastAppliedCustomerCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> lastKnownShouldCreateEntries = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> lastKnownBusinessTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> lastKnownAvailableProductCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pendingScheduleBusinessKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool hasAppliedCustomTraffic;
        private bool loggedInactiveMultiplier;
        private bool loggedUnavailableHooks;
        private object? lastAppliedSaveGame;
        private float? originalBaseCustomerPromotionMultiplier;
        private float lastAppliedMultiplier = 1f;

        public void InvalidateCache()
        {
            // Additive scene streaming does not replace the active save or its business
            // registrations. Preserve discovery state here; clearing it made every existing
            // business look new and triggered a targeted schedule rebuild for all of them.
            // A genuinely different save is detected by reference in ApplyConfiguredTraffic,
            // and its full rebuild replaces these collections safely.
        }

        public void ApplyConfiguredTraffic(ModContext context, BigHaxSettings settings, bool forceRefresh)
        {
            var multiplier = Mathf.Max(1f, settings.CustomerTrafficMultiplier);

            if (multiplier <= 1f)
            {
                if (hasAppliedCustomTraffic)
                    RestoreVanillaTraffic(context);

                hasAppliedCustomTraffic = false;
                lastAppliedMultiplier = multiplier;
                if (!loggedInactiveMultiplier)
                {
                    loggedInactiveMultiplier = true;
                    BigHaxLogger.Diagnostic(
                        "Freeze diagnostic/customer traffic inactive: multiplier=1, no polling or schedule refresh work will run.");
                }
                return;
            }

            loggedInactiveMultiplier = false;

            if (!CanUseCustomerEntries())
            {
                if (!loggedUnavailableHooks)
                {
                    loggedUnavailableHooks = true;
                    BigHaxLogger.Diagnostic(
                        "Freeze diagnostic/customer traffic hooks unavailable: updateAll=" +
                        (UpdateAllCustomerEntriesMethod != null) +
                        ", updateOne=" + (UpdateCustomerEntriesForPlayerBusinessMethod != null) +
                        ", getDayOfWeek=" + (GetDayOfWeekMethod != null) +
                        ", getEntries=" + (GetEntriesByAddressMethod != null) +
                        ", shouldCreate=" + (ShouldEntriesBeCreatedMethod != null) + ".");
                }
                BigHaxLogger.WarnOnce(context, "missing-customer-entries-hooks", "BigHax: customer traffic hooks are unavailable in this game build.");
                return;
            }

            loggedUnavailableHooks = false;

            // During the mod's load callback, Big Ambitions 1.0 may have loaded
            // the save data but not initialized TimeHelper yet. Calling the game's
            // customer refresh at that stage throws and used to permanently disable
            // this service before the player could use the option.
            var currentSaveGame = SaveGameManager.Current;
            if (currentSaveGame?.gameVariables == null)
                return;

            var saveGameChanged = !ReferenceEquals(lastAppliedSaveGame, currentSaveGame);
            if (!hasAppliedCustomTraffic ||
                saveGameChanged ||
                !Mathf.Approximately(multiplier, lastAppliedMultiplier))
            {
                RebuildAndApplyTraffic(context, multiplier);
                return;
            }

            if (forceRefresh)
            {
                BigHaxLogger.Diagnostic(
                    "Freeze diagnostic/customer traffic skipped duplicate all-business rebuild: multiplier=" +
                    multiplier + ", sameSave=True.");
            }

            ApplyPromotionBoost(multiplier);
            DiscoverActivatedBusinesses(context, multiplier);
            TryApplyPendingBusinessSchedules(context, multiplier);
        }

        public void RestoreOriginalState(ModContext? context)
        {
            // The game disposes customer-entry dictionaries before unloading mods. Rebuilding
            // entries at this point calls into already-cleared game state and throws in 1.0.
            RestoreVanillaTraffic(context, refreshEntries: false);
            hasAppliedCustomTraffic = false;
            lastAppliedSaveGame = null;
            lastAppliedMultiplier = 1f;
        }

        private void RebuildAndApplyTraffic(ModContext? context, float multiplier)
        {
            if (!TryUpdateAllCustomerEntries(context))
                return;

            ApplyPromotionBoost(multiplier);
            ApplyTrafficAfterVanillaRefresh(
                context,
                multiplier,
                out var appliedBusinessCount,
                out var waitingForInitializationCount,
                out var clonedBusinessCount);

            hasAppliedCustomTraffic = true;
            lastAppliedSaveGame = SaveGameManager.Current;
            lastAppliedMultiplier = multiplier;

            BigHaxLogger.Diagnostic(
                "Freeze diagnostic/customer traffic initial rebuild completed: multiplier=" + multiplier +
                ", businesses=" + appliedBusinessCount +
                ", waitingForCapacity=" + waitingForInitializationCount +
                ", clonedBusinesses=" + clonedBusinessCount +
                ", pendingTargetedRefreshes=" + pendingScheduleBusinessKeys.Count + ".");

            BigHaxLogger.Info(
                context,
                $"BigHax: applied customer traffic multiplier x{multiplier} to player businesses. businesses={appliedBusinessCount}, waitingForInitialization={waitingForInitializationCount}, clonedEntryBusinesses={clonedBusinessCount}.");
        }

        private void RestoreVanillaTraffic(ModContext? context, bool refreshEntries = true)
        {
            RestorePromotionBoost();
            if (refreshEntries)
                TryUpdateAllCustomerEntries(context);

            foreach (var registration in GetPlayerBusinessRegistrations())
            {
                var key = GetRegistrationKey(registration);
                if (originalCustomerCapacities.TryGetValue(key, out var originalCapacity))
                    registration.customerCapacity = originalCapacity;
            }

            lastAppliedCustomerCapacities.Clear();
            lastKnownShouldCreateEntries.Clear();
            lastKnownBusinessTypes.Clear();
            lastKnownAvailableProductCounts.Clear();
            pendingScheduleBusinessKeys.Clear();
            BigHaxLogger.Info(context, "BigHax: restored vanilla customer traffic for player businesses.");
        }

        private void DiscoverActivatedBusinesses(ModContext? context, float multiplier)
        {
            var registrations = GetPlayerBusinessRegistrations();
            PruneRemovedBusinessState(registrations);

            foreach (var registration in registrations)
            {
                var key = GetRegistrationKey(registration);
                var businessType = GetBusinessType(registration);
                var availableProductCount = registration.cachedAvailableProducts?.Count ?? 0;
                var isNewRegistration = !lastKnownBusinessTypes.TryGetValue(key, out var previousBusinessType);
                var businessTypeChanged = !isNewRegistration &&
                                          !string.Equals(previousBusinessType, businessType, StringComparison.Ordinal);
                var capacityChangedSinceLastApply =
                    lastAppliedCustomerCapacities.TryGetValue(key, out var lastAppliedCapacity) &&
                    registration.customerCapacity != lastAppliedCapacity;
                var baselineBecameAvailable =
                    !originalCustomerCapacities.ContainsKey(key) &&
                    registration.customerCapacity > 0;

                var shouldCreateEntries = ShouldEntriesBeCreated(context, registration);
                var becameEligibleForEntries =
                    lastKnownShouldCreateEntries.TryGetValue(key, out var previouslyShouldCreateEntries) &&
                    !previouslyShouldCreateEntries &&
                    shouldCreateEntries;
                var productsBecameAvailable =
                    lastKnownAvailableProductCounts.TryGetValue(key, out var previousProductCount) &&
                    previousProductCount == 0 &&
                    availableProductCount > 0;

                if (businessTypeChanged)
                    ResetBusinessBaseline(key);
                else if (capacityChangedSinceLastApply && registration.customerCapacity <= 0)
                    ResetBusinessBaseline(key);

                lastKnownBusinessTypes[key] = businessType;
                lastKnownShouldCreateEntries[key] = shouldCreateEntries;
                lastKnownAvailableProductCounts[key] = availableProductCount;

                var scheduleRefreshRequired =
                    isNewRegistration ||
                    businessTypeChanged ||
                    baselineBecameAvailable ||
                    becameEligibleForEntries ||
                    productsBecameAvailable;

                if (!scheduleRefreshRequired && !capacityChangedSinceLastApply)
                {
                    continue;
                }

                // A newly created business can temporarily report customerCapacity == 0.
                // Never capture or re-apply that transient value as its permanent vanilla
                // baseline. The pending refresh will retry after vanilla has finished setup.
                var capacityReady = ApplyCustomerCapacity(registration, key, multiplier, afterVanillaRefresh: false);
                if (!scheduleRefreshRequired)
                {
                    BigHaxLogger.Diagnostic(
                        "Freeze diagnostic/customer traffic reapplied capacity without rebuilding schedule: business=" +
                        key + ", type=" + businessType + ", capacityReady=" + capacityReady + ".");
                    continue;
                }

                if (capacityReady && shouldCreateEntries)
                {
                    pendingScheduleBusinessKeys.Add(key);
                    BigHaxLogger.Diagnostic(
                        "Freeze diagnostic/customer traffic queued one targeted refresh: business=" + key +
                        ", type=" + businessType +
                        ", products=" + availableProductCount +
                        ", reason=" + GetRefreshReason(
                            isNewRegistration,
                            businessTypeChanged,
                            baselineBecameAvailable,
                            becameEligibleForEntries,
                            productsBecameAvailable) + ".");
                }
            }
        }

        private void TryApplyPendingBusinessSchedules(ModContext? context, float multiplier)
        {
            if (pendingScheduleBusinessKeys.Count == 0)
                return;

            var registrationsByKey = new Dictionary<string, BuildingRegistration>(StringComparer.OrdinalIgnoreCase);
            foreach (var registration in GetPlayerBusinessRegistrations())
                registrationsByKey[GetRegistrationKey(registration)] = registration;

            var pendingKeys = new List<string>(pendingScheduleBusinessKeys);
            pendingScheduleBusinessKeys.Clear();

            foreach (var key in pendingKeys)
            {
                if (!registrationsByKey.TryGetValue(key, out var registration) ||
                    !ShouldEntriesBeCreated(context, registration) ||
                    !ApplyCustomerCapacity(registration, key, multiplier, afterVanillaRefresh: false))
                {
                    BigHaxLogger.Diagnostic(
                        "Freeze diagnostic/customer traffic discarded targeted refresh: business=" + key +
                        ", registrationReady=" + registrationsByKey.ContainsKey(key) + ".");
                    continue;
                }

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var updated = TryUpdateCustomerEntriesForPlayerBusiness(registration);
                var entries = updated
                    ? GetBusinessCustomerEntries(context, registration, "after targeted refresh")
                    : null;
                var originalEntryCount = entries?.Count ?? 0;
                if (entries != null && entries.Count > 0)
                    MultiplyEntries(entries, multiplier);

                stopwatch.Stop();
                BigHaxLogger.Diagnostic(
                    "Freeze diagnostic/customer traffic targeted refresh completed: business=" + key +
                    ", updated=" + updated +
                    ", vanillaEntries=" + originalEntryCount +
                    ", multipliedEntries=" + (entries?.Count ?? 0) +
                    ", elapsedMs=" + stopwatch.ElapsedMilliseconds +
                    ", willRetry=False.");
            }
        }

        private void ApplyTrafficAfterVanillaRefresh(
            ModContext? context,
            float multiplier,
            out int appliedBusinessCount,
            out int waitingForInitializationCount,
            out int clonedBusinessCount)
        {
            appliedBusinessCount = 0;
            waitingForInitializationCount = 0;
            clonedBusinessCount = 0;

            var registrations = GetPlayerBusinessRegistrations();
            PruneRemovedBusinessState(registrations);

            pendingScheduleBusinessKeys.Clear();
            lastKnownShouldCreateEntries.Clear();
            lastKnownBusinessTypes.Clear();

            foreach (var registration in registrations)
            {
                var key = GetRegistrationKey(registration);
                lastKnownBusinessTypes[key] = GetBusinessType(registration);
                lastKnownAvailableProductCounts[key] = registration.cachedAvailableProducts?.Count ?? 0;

                var capacityReady = ApplyCustomerCapacity(registration, key, multiplier, afterVanillaRefresh: true);
                if (capacityReady)
                    appliedBusinessCount++;

                var shouldCreateEntries = ShouldEntriesBeCreated(context, registration);
                lastKnownShouldCreateEntries[key] = shouldCreateEntries;
                if (!shouldCreateEntries)
                {
                    // Some player-owned locations never create customer schedules. Do not
                    // refresh those forever. If this is a real customer business that is
                    // still initializing, the poller will notice either its first positive
                    // capacity or a later false -> true ShouldEntriesBeCreated transition.
                    continue;
                }

                var entries = GetBusinessCustomerEntries(context, registration, "after vanilla refresh");
                if (entries == null || entries.Count == 0)
                {
                    // An empty schedule is valid when a business is closed or has no
                    // customers remaining today. Retrying it forever caused a complete
                    // all-business rebuild every ten seconds and recurring multi-second stalls.
                    if (!capacityReady)
                        waitingForInitializationCount++;
                    continue;
                }

                var originalEntryCount = entries.Count;
                MultiplyEntries(entries, multiplier);
                if (entries.Count > originalEntryCount)
                    clonedBusinessCount++;

                if (!capacityReady)
                    waitingForInitializationCount++;
            }
        }

        private bool ApplyCustomerCapacity(
            BuildingRegistration registration,
            string key,
            float multiplier,
            bool afterVanillaRefresh)
        {
            var businessType = GetBusinessType(registration);
            var currentCapacity = registration.customerCapacity;

            if (originalCustomerCapacityBusinessTypes.TryGetValue(key, out var capturedBusinessType) &&
                !string.Equals(capturedBusinessType, businessType, StringComparison.Ordinal))
            {
                ResetBusinessBaseline(key);
            }

            var hasBaseCapacity = originalCustomerCapacities.TryGetValue(key, out var baseCapacity);
            var hasLastAppliedCapacity = lastAppliedCustomerCapacities.TryGetValue(key, out var lastAppliedCapacity);

            if (currentCapacity <= 0)
            {
                // Zero is a valid transient state while a newly opened business is being
                // initialized. If vanilla reset a previously patched registration to zero,
                // discard the old baseline as well so the new business can learn its own
                // real capacity once it becomes available.
                if (!hasBaseCapacity || (hasLastAppliedCapacity && currentCapacity != lastAppliedCapacity))
                    ResetBusinessBaseline(key);

                lastAppliedCustomerCapacities.Remove(key);
                return false;
            }

            var shouldRefreshBaseCapacity =
                !hasBaseCapacity ||
                (hasLastAppliedCapacity && currentCapacity != lastAppliedCapacity) ||
                (afterVanillaRefresh && !hasLastAppliedCapacity);

            if (shouldRefreshBaseCapacity)
            {
                baseCapacity = currentCapacity;
                originalCustomerCapacities[key] = baseCapacity;
                originalCustomerCapacityBusinessTypes[key] = businessType;
            }

            if (baseCapacity <= 0)
                return false;

            var desiredCapacity = Mathf.Max(baseCapacity, Mathf.CeilToInt(baseCapacity * multiplier));
            registration.customerCapacity = desiredCapacity;
            lastAppliedCustomerCapacities[key] = desiredCapacity;
            return true;
        }

        private void PruneRemovedBusinessState(List<BuildingRegistration> registrations)
        {
            // During scene transitions the save can briefly expose no player-business
            // registrations. Do not throw away known vanilla baselines during that window.
            if (registrations.Count == 0)
                return;

            var activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var registration in registrations)
                activeKeys.Add(GetRegistrationKey(registration));

            var trackedKeys = new List<string>(originalCustomerCapacities.Keys);
            foreach (var key in trackedKeys)
            {
                if (!activeKeys.Contains(key))
                    ResetBusinessBaseline(key);
            }

            var knownKeys = new List<string>(lastKnownBusinessTypes.Keys);
            foreach (var key in knownKeys)
            {
                if (!activeKeys.Contains(key))
                    lastKnownBusinessTypes.Remove(key);
            }

            var shouldCreateKeys = new List<string>(lastKnownShouldCreateEntries.Keys);
            foreach (var key in shouldCreateKeys)
            {
                if (!activeKeys.Contains(key))
                    lastKnownShouldCreateEntries.Remove(key);
            }

            var productCountKeys = new List<string>(lastKnownAvailableProductCounts.Keys);
            foreach (var key in productCountKeys)
            {
                if (!activeKeys.Contains(key))
                    lastKnownAvailableProductCounts.Remove(key);
            }

            pendingScheduleBusinessKeys.RemoveWhere(key => !activeKeys.Contains(key));
        }

        private void ResetBusinessBaseline(string key)
        {
            originalCustomerCapacities.Remove(key);
            originalCustomerCapacityBusinessTypes.Remove(key);
            lastAppliedCustomerCapacities.Remove(key);
            lastKnownShouldCreateEntries.Remove(key);
            lastKnownAvailableProductCounts.Remove(key);
            pendingScheduleBusinessKeys.Remove(key);
        }

        private static string GetRefreshReason(
            bool isNewRegistration,
            bool businessTypeChanged,
            bool baselineBecameAvailable,
            bool becameEligible,
            bool productsBecameAvailable)
        {
            if (businessTypeChanged) return "business-type-changed";
            if (becameEligible) return "requirements-became-ready";
            if (productsBecameAvailable) return "products-became-ready";
            if (baselineBecameAvailable) return "capacity-became-ready";
            return isNewRegistration ? "new-registration" : "state-changed";
        }

        private static string GetBusinessType(BuildingRegistration registration)
        {
            return registration.businessTypeName ?? string.Empty;
        }

        private void ApplyPromotionBoost(float multiplier)
        {
            var gameVariables = SaveGameManager.Current?.gameVariables;
            if (gameVariables == null)
                return;

            if (!originalBaseCustomerPromotionMultiplier.HasValue)
                originalBaseCustomerPromotionMultiplier = gameVariables.baseCustomerPromotionMultiplier;

            var targetMultiplier = 1f + (multiplier - 1f) * AdditionalPromotionBoostPerMultiplierStep;
            var targetValue = originalBaseCustomerPromotionMultiplier.Value * targetMultiplier;

            if (!Mathf.Approximately(gameVariables.baseCustomerPromotionMultiplier, targetValue))
                gameVariables.baseCustomerPromotionMultiplier = targetValue;
        }

        private void RestorePromotionBoost()
        {
            var gameVariables = SaveGameManager.Current?.gameVariables;
            if (gameVariables == null || !originalBaseCustomerPromotionMultiplier.HasValue)
                return;

            gameVariables.baseCustomerPromotionMultiplier = originalBaseCustomerPromotionMultiplier.Value;
        }

        private static bool CanUseCustomerEntries()
        {
            return UpdateAllCustomerEntriesMethod != null &&
                   UpdateCustomerEntriesForPlayerBusinessMethod != null &&
                   GetDayOfWeekMethod != null &&
                   GetEntriesByAddressMethod != null &&
                   CustomerEntrySpawnTimeField != null &&
                   CustomerEntryCompletedField != null &&
                   CustomerEntryOrderField != null &&
                   OrderEntriesField != null &&
                   OrderCompletedField != null &&
                   OrderCustomerDemandTypesField != null &&
                   TimestampDayField != null &&
                   TimestampHourField != null &&
                   TimestampMinuteField != null &&
                   TimestampType != null;
        }

        private static void MultiplyEntries(IList entries, float multiplier)
        {
            if (multiplier <= 1f || entries.Count == 0)
                return;

            var originalEntries = new object[entries.Count];
            for (var i = 0; i < entries.Count; i++)
                originalEntries[i] = entries[i]!;

            var wholeCopies = Mathf.FloorToInt(multiplier) - 1;
            for (var duplicateIndex = 1; duplicateIndex <= wholeCopies; duplicateIndex++)
            {
                foreach (var sourceEntry in originalEntries)
                {
                    var clone = CloneCustomerEntry(sourceEntry, duplicateIndex);
                    if (clone != null)
                        entries.Add(clone);
                }
            }

            var targetCount = Mathf.CeilToInt(originalEntries.Length * multiplier);
            var partialClonesNeeded = Mathf.Max(0, targetCount - entries.Count);
            for (var index = 0; index < partialClonesNeeded && index < originalEntries.Length; index++)
            {
                var clone = CloneCustomerEntry(originalEntries[index], wholeCopies + 1);
                if (clone != null)
                    entries.Add(clone);
            }
        }

        private static object? CloneCustomerEntry(object sourceEntry, int duplicateIndex)
        {
            var clone = MemberwiseCloneMethod.Invoke(sourceEntry, null);
            if (clone == null || CustomerEntrySpawnTimeField == null || CustomerEntryOrderField == null)
                return null;

            var sourceTimestamp = CustomerEntrySpawnTimeField.GetValue(sourceEntry);
            if (sourceTimestamp != null)
                CustomerEntrySpawnTimeField.SetValue(clone, CloneTimestamp(sourceTimestamp, duplicateIndex * 0.1f));

            CustomerEntryCompletedField?.SetValue(clone, false);

            var sourceOrder = CustomerEntryOrderField.GetValue(sourceEntry);
            if (sourceOrder != null)
                CustomerEntryOrderField.SetValue(clone, CloneOrder(sourceOrder));

            return clone;
        }

        private static object? CloneOrder(object sourceOrder)
        {
            var clone = MemberwiseCloneMethod.Invoke(sourceOrder, null);
            if (clone == null)
                return null;

            OrderCompletedField?.SetValue(clone, false);

            if (OrderEntriesField?.GetValue(sourceOrder) is IList sourceEntries)
            {
                var clonedEntries = CreateEmptyList(OrderEntriesField.FieldType);
                foreach (var sourceEntry in sourceEntries)
                {
                    if (sourceEntry != null)
                        clonedEntries.Add(MemberwiseCloneMethod.Invoke(sourceEntry, null));
                }

                OrderEntriesField.SetValue(clone, clonedEntries);
            }

            if (OrderCustomerDemandTypesField?.GetValue(sourceOrder) is IList sourceDemandTypes)
            {
                var clonedDemandTypes = CreateEmptyList(OrderCustomerDemandTypesField.FieldType);
                foreach (var demandType in sourceDemandTypes)
                    clonedDemandTypes.Add(demandType);

                OrderCustomerDemandTypesField.SetValue(clone, clonedDemandTypes);
            }

            return clone;
        }

        private static object CloneTimestamp(object sourceTimestamp, float minuteOffset)
        {
            var clone = Activator.CreateInstance(TimestampType!);
            var day = (int)(TimestampDayField!.GetValue(sourceTimestamp) ?? 0);
            var hour = (int)(TimestampHourField!.GetValue(sourceTimestamp) ?? 0);
            var minute = Convert.ToSingle(TimestampMinuteField!.GetValue(sourceTimestamp) ?? 0f);

            TimestampDayField.SetValue(clone, day);
            TimestampHourField.SetValue(clone, hour);
            TimestampMinuteField.SetValue(clone, Mathf.Min(59.9f, minute + minuteOffset));
            return clone!;
        }

        private static IList CreateEmptyList(Type listType)
        {
            return (IList)Activator.CreateInstance(listType)!;
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

        private static bool ShouldEntriesBeCreated(ModContext? context, BuildingRegistration registration)
        {
            if (ShouldEntriesBeCreatedMethod == null)
                return true;

            try
            {
                return (bool)(ShouldEntriesBeCreatedMethod.Invoke(null, new object[] { registration }) ?? false);
            }
            catch
            {
                return true;
            }
        }

        private static IList? GetBusinessCustomerEntries(ModContext? context, BuildingRegistration registration, string phase)
        {
            if (GetEntriesByAddressMethod == null)
                return null;

            try
            {
                return GetEntriesByAddressMethod.Invoke(null, new object[] { registration.Address }) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryUpdateAllCustomerEntries(ModContext? context)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                UpdateAllCustomerEntriesMethod?.Invoke(null, null);
                stopwatch.Stop();
                BigHaxLogger.Diagnostic(
                    "Freeze diagnostic/customer traffic all-business rebuild completed: elapsedMs=" +
                    stopwatch.ElapsedMilliseconds + ".");
                return true;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                BigHaxLogger.DiagnosticException("Freeze diagnostic/customer traffic all-business rebuild", exception);
                return false;
            }
        }

        private static bool TryUpdateCustomerEntriesForPlayerBusiness(BuildingRegistration registration)
        {
            if (UpdateCustomerEntriesForPlayerBusinessMethod == null || GetDayOfWeekMethod == null)
                return false;

            try
            {
                var dayOfWeek = GetDayOfWeekMethod.Invoke(null, null);
                if (dayOfWeek == null)
                    return false;

                UpdateCustomerEntriesForPlayerBusinessMethod.Invoke(null, new[] { (object)registration, dayOfWeek });
                return true;
            }
            catch (Exception exception)
            {
                BigHaxLogger.DiagnosticException(
                    "Freeze diagnostic/customer traffic targeted rebuild for " + GetRegistrationKey(registration),
                    exception);
                return false;
            }
        }

        private static string GetRegistrationKey(BuildingRegistration registration)
        {
            return registration.StreetName + "|" + registration.StreetNumber;
        }

        private static Type? FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var exact = assembly.GetType(typeName, false);
                if (exact != null)
                    return exact;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = Array.FindAll(exception.Types, type => type != null)!;
                }

                foreach (var type in types)
                {
                    if (type == null)
                        continue;

                    if (type.FullName == typeName ||
                        type.Name == typeName ||
                        (type.FullName?.EndsWith("." + typeName, StringComparison.Ordinal) ?? false))
                    {
                        return type;
                    }
                }
            }

            return null;
        }
    }
}
