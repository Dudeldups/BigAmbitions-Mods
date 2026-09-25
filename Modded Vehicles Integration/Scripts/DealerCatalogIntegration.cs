#nullable enable
using System;
using System.Collections.Generic;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using Services;
using Vehicles.VehicleTypes;

namespace ModdedVehiclesIntegration
{
    internal sealed class DealerCatalogIntegration
    {
        private const string CityCarsContactId = "City Cars";
        private const float CityCarsFallbackPriceThreshold = 60000f;

        private static readonly string[] DealerContactIds =
        {
            "City Cars",
            "Manhattan Luxury Cars",
            "The Hamptons Axis",
            "General US Trucks"
        };

        private static readonly HashSet<string> LuxuryDealerContactIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "Manhattan Luxury Cars",
            "The Hamptons Axis"
        };

        private readonly Dictionary<string, DealerState> states =
            new Dictionary<string, DealerState>(StringComparer.Ordinal);

        internal void ResetTracking()
        {
            states.Clear();
        }

        internal void Synchronize()
        {
            var save = SaveGameManager.Current;
            if (save?.BuildingRegistrations == null)
                return;

            foreach (var dealerContactId in DealerContactIds)
            {
                var registration = save.BuildingRegistrations.Find(candidate =>
                    candidate != null && string.Equals(candidate.BusinessName, dealerContactId, StringComparison.Ordinal));
                if (registration == null || !TryGetLayoutVehicles(registration, out _))
                    return;
            }

            CaptureExternalChanges();

            var modVehicles = new List<string>();
            foreach (var vehicleTypeName in VehicleTypeHelper.GetVehicleTypeNames())
            {
                if (!string.IsNullOrEmpty(vehicleTypeName) && VehicleTypeHelper.IsModVehicleType(vehicleTypeName))
                    AddUnique(modVehicles, vehicleTypeName);
            }

            var explicitlyRegistered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var state in states.Values)
            {
                foreach (var vehicleTypeName in state.ExternalStock)
                {
                    if (VehicleTypeHelper.IsModVehicleType(vehicleTypeName))
                        explicitlyRegistered.Add(vehicleTypeName);
                }
            }

            var fallbackCityVehicles = new List<string>();
            var fallbackLuxuryVehicles = new List<string>();
            foreach (var vehicleTypeName in modVehicles)
            {
                if (explicitlyRegistered.Contains(vehicleTypeName))
                    continue;

                var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleTypeName);
                if (vehicleType == null)
                    continue;

                if (vehicleType.price < CityCarsFallbackPriceThreshold)
                    fallbackCityVehicles.Add(vehicleTypeName);
                else
                    fallbackLuxuryVehicles.Add(vehicleTypeName);
            }

            fallbackCityVehicles.Sort(StringComparer.Ordinal);
            fallbackLuxuryVehicles.Sort(StringComparer.Ordinal);

            foreach (var dealerContactId in DealerContactIds)
            {
                var state = states[dealerContactId];
                var registeredModStock = new List<string>();
                foreach (var vehicleTypeName in state.ExternalStock)
                {
                    if (VehicleTypeHelper.IsModVehicleType(vehicleTypeName))
                        AddUnique(registeredModStock, vehicleTypeName);
                }

                var desiredStock = new List<string>();
                AddUniqueRange(desiredStock, registeredModStock);
                if (string.Equals(dealerContactId, CityCarsContactId, StringComparison.Ordinal))
                    AddUniqueRange(desiredStock, fallbackCityVehicles);
                else if (LuxuryDealerContactIds.Contains(dealerContactId))
                    AddUniqueRange(desiredStock, fallbackLuxuryVehicles);

                desiredStock.RemoveAll(vehicleTypeName => VehicleTypeHelper.GetVehicleType(vehicleTypeName) == null);

                var hasCurrent = ContractItemsForSaleService.TryGetVehiclesForContact(
                    dealerContactId,
                    out List<string> currentStock);
                if (!hasCurrent || currentStock == null || !SameList(currentStock, desiredStock))
                    ContractItemsForSaleService.SetVehiclesForContact(dealerContactId, desiredStock);

                state.LastApplied = new List<string>(desiredStock);
                state.LastAppliedHadContact = true;
            }
        }

        internal void RestoreExternalCatalogs()
        {
            foreach (var dealerContactId in DealerContactIds)
            {
                if (!states.TryGetValue(dealerContactId, out var state) || !state.Captured)
                    continue;

                if (state.ExternalStock.Count == 0)
                    ContractItemsForSaleService.RemoveContact(dealerContactId);
                else
                    ContractItemsForSaleService.SetVehiclesForContact(dealerContactId, state.ExternalStock);
            }

            states.Clear();
        }

        private void CaptureExternalChanges()
        {
            foreach (var dealerContactId in DealerContactIds)
            {
                var hasCurrent = ContractItemsForSaleService.TryGetVehiclesForContact(
                    dealerContactId,
                    out List<string> currentStock);
                currentStock ??= new List<string>();

                if (!states.TryGetValue(dealerContactId, out var state))
                {
                    state = new DealerState
                    {
                        Captured = true,
                        ExternalStock = hasCurrent ? UniqueCopy(currentStock) : new List<string>()
                    };
                    states.Add(dealerContactId, state);
                    continue;
                }

                if (hasCurrent == state.LastAppliedHadContact && SameList(currentStock, state.LastApplied))
                    continue;

                if (!hasCurrent)
                {
                    state.ExternalStock.Clear();
                    continue;
                }

                foreach (var vehicleTypeName in state.LastApplied)
                {
                    if (!currentStock.Contains(vehicleTypeName))
                        state.ExternalStock.Remove(vehicleTypeName);
                }

                foreach (var vehicleTypeName in currentStock)
                {
                    if (!state.LastApplied.Contains(vehicleTypeName))
                        AddUnique(state.ExternalStock, vehicleTypeName);
                }
            }
        }

        private static bool TryGetLayoutVehicles(BuildingRegistration registration, out List<string> stock)
        {
            stock = new List<string>();
            if (string.IsNullOrEmpty(registration.businessTypeName) || string.IsNullOrEmpty(registration.Layout))
                return false;

            try
            {
                var layout = BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                    registration.businessTypeName,
                    new BuildingSizeInfo(registration),
                    registration.Layout.ToLowerInvariant(),
                    false);
                if (layout?.Items == null)
                    return false;

                foreach (var item in layout.Items)
                {
                    var purchaseSettings = item?.playerItemPurchaserSettings;
                    if (purchaseSettings == null || !purchaseSettings.enabled ||
                        string.IsNullOrEmpty(purchaseSettings.itemName))
                    {
                        continue;
                    }

                    var itemDefinition = ItemsGetter.GetByName(purchaseSettings.itemName);
                    if (itemDefinition != null && !string.IsNullOrEmpty(itemDefinition.vehicleType))
                        AddUnique(stock, itemDefinition.vehicleType);
                }

                return stock.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        private static List<string> UniqueCopy(IEnumerable<string> source)
        {
            var result = new List<string>();
            AddUniqueRange(result, source);
            return result;
        }

        private static void AddUniqueRange(List<string> destination, IEnumerable<string> source)
        {
            foreach (var entry in source)
                AddUnique(destination, entry);
        }

        private static void AddUnique(List<string> destination, string entry)
        {
            if (!string.IsNullOrEmpty(entry) && !destination.Contains(entry))
                destination.Add(entry);
        }

        private static bool SameList(List<string> left, List<string> right)
        {
            if (left.Count != right.Count)
                return false;

            for (var index = 0; index < left.Count; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private sealed class DealerState
        {
            internal bool Captured;
            internal List<string> ExternalStock = new List<string>();
            internal List<string> LastApplied = new List<string>();
            internal bool LastAppliedHadContact;
        }
    }
}
