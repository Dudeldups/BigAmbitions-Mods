#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using Buildings;
using Dialogs;

namespace ModdedVehiclesIntegration
{
    internal static class DealerServiceIntegration
    {
        private const string VehicleStoreSourceContactId = "General US Trucks";

        private static readonly string[] InteractiveDealerContactIds =
        {
            "City Cars",
            "Manhattan Luxury Cars",
            "The Hamptons Axis"
        };

        private static readonly List<PatchRecord> AppliedPatches = new List<PatchRecord>();
        private static readonly HashSet<string> ReportedProblems = new HashSet<string>(StringComparer.Ordinal);

        internal static void EnsureApplied(ModContext? context)
        {
            var registrations = SaveGameManager.Current?.BuildingRegistrations;
            if (registrations == null)
                return;

            var sourceRegistration = registrations.Find(candidate =>
                candidate != null &&
                string.Equals(candidate.BusinessName, VehicleStoreSourceContactId, StringComparison.Ordinal));
            var sourceService = sourceRegistration?.BuildingCached?.SpecialService;
            if (!(sourceService?.settings is VehicleStoreSettings sourceSettings))
            {
                ReportProblemOnce(
                    "source",
                    "General US Trucks is not ready or has no VehicleStoreSettings; dealer dialog integration is waiting.",
                    context);
                return;
            }

            foreach (var dealerContactId in InteractiveDealerContactIds)
            {
                try
                {
                    var registration = registrations.Find(candidate =>
                        candidate != null &&
                        string.Equals(candidate.BusinessName, dealerContactId, StringComparison.Ordinal));
                    var service = registration?.BuildingCached?.SpecialService;
                    if (registration == null || service == null)
                    {
                        ReportProblemOnce(
                            "dealer:" + dealerContactId,
                            $"dealer '{dealerContactId}' is not ready or has no SpecialService; dialog integration is waiting.",
                            context);
                        continue;
                    }

                    var patch = FindPatch(service);
                    if (patch == null)
                    {
                        patch = new PatchRecord(
                            service,
                            service.dialogType,
                            service.settings,
                            service.contactCategory,
                            service.isBusinessContact);
                        AppliedPatches.Add(patch);
                    }

                    var needsRepair =
                        service.dialogType != CallDialogType.VehicleStoreDialog ||
                        !ReferenceEquals(service.settings, sourceSettings) ||
                        service.contactCategory != sourceService.contactCategory ||
                        service.isBusinessContact != sourceService.isBusinessContact;
                    if (!needsRepair)
                        continue;

                    service.dialogType = CallDialogType.VehicleStoreDialog;
                    service.settings = sourceSettings;
                    service.contactCategory = sourceService.contactCategory;
                    service.isBusinessContact = sourceService.isBusinessContact;
                }
                catch (Exception exception)
                {
                    context?.Logger.Error(exception);
                }
            }
        }

        internal static void Restore()
        {
            for (var index = AppliedPatches.Count - 1; index >= 0; index--)
            {
                var patch = AppliedPatches[index];
                patch.Service.dialogType = patch.OriginalDialogType;
                patch.Service.settings = patch.OriginalSettings;
                patch.Service.contactCategory = patch.OriginalContactCategory;
                patch.Service.isBusinessContact = patch.OriginalIsBusinessContact;
            }

            AppliedPatches.Clear();
            ReportedProblems.Clear();
        }

        internal static bool EnsureDealerReady(string dealerContactId, ModContext? context)
        {
            EnsureApplied(context);

            var registrations = SaveGameManager.Current?.BuildingRegistrations;
            var registration = registrations?.Find(candidate =>
                candidate != null &&
                string.Equals(candidate.BusinessName, dealerContactId, StringComparison.Ordinal));
            var service = registration?.BuildingCached?.SpecialService;
            var ready =
                service != null &&
                service.dialogType == CallDialogType.VehicleStoreDialog &&
                service.settings is VehicleStoreSettings;

            if (!ready)
            {
                ReportProblemOnce(
                    "interaction:" + dealerContactId,
                    $"blocked desk interaction for '{dealerContactId}' because its vehicle-store service is not ready " +
                    $"(registration={registration != null}, building={registration?.BuildingCached != null}, " +
                    $"service={service != null}, dialogType={service?.dialogType.ToString() ?? "<none>"}, " +
                    $"settings={service?.settings?.GetType().Name ?? "<none>"}).",
                    context);
                return false;
            }

            ReportedProblems.Remove("dealer:" + dealerContactId);
            ReportedProblems.Remove("interaction:" + dealerContactId);
            return true;
        }

        private static PatchRecord? FindPatch(SpecialService service)
        {
            foreach (var patch in AppliedPatches)
            {
                if (ReferenceEquals(patch.Service, service))
                    return patch;
            }

            return null;
        }

        private static void ReportProblemOnce(string key, string message, ModContext? context)
        {
            if (ReportedProblems.Add(key))
                context?.Logger.Warn("Modded Vehicles Integration: " + message);
        }

        private sealed class PatchRecord
        {
            internal PatchRecord(
                SpecialService service,
                CallDialogType originalDialogType,
                SpecialServiceSettings originalSettings,
                UI.Smartphone.Apps.Contacts.ContactCategoryName originalContactCategory,
                bool originalIsBusinessContact)
            {
                Service = service;
                OriginalDialogType = originalDialogType;
                OriginalSettings = originalSettings;
                OriginalContactCategory = originalContactCategory;
                OriginalIsBusinessContact = originalIsBusinessContact;
            }

            internal SpecialService Service { get; }
            internal CallDialogType OriginalDialogType { get; }
            internal SpecialServiceSettings OriginalSettings { get; }
            internal UI.Smartphone.Apps.Contacts.ContactCategoryName OriginalContactCategory { get; }
            internal bool OriginalIsBusinessContact { get; }
        }
    }
}
