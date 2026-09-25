#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using BAModAPI;
using Helpers;
using UnityEngine;
using Vehicles.VehicleTypes;

namespace BigHax
{
    internal sealed class BigHaxVehicleCapacityService
    {
        private const string VehicleOverridesModDataKey = "big_hax:vehicle_type_caps_v1";

        private readonly Dictionary<string, int> originalCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> originalDeliveryDestinations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> persistedVehicleTypeCapacities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string? lastAppliedSerializedOverrides;
        private string? lastKnownActiveVehicleId;

        public void InvalidateCache()
        {
            lastKnownActiveVehicleId = null;
        }

        public void ApplyConfiguredCapacities(ModContext context, BigHaxSettings settings, bool forceRefresh)
        {
            ApplyFreightTruckDeliveryPlaces(context, settings.FreightTruckT1DeliveryPlaces);

            if (!settings.EnableActiveVehicleCapacityOverride)
            {
                RestorePersistedVehicleTypeCapacities();
                lastKnownActiveVehicleId = SaveGameManager.Current?.ActiveVehicleId;
                return;
            }

            LoadPersistedOverridesFromSave();
            ApplyPersistedVehicleTypeCapacities();

            var activeVehicleId = SaveGameManager.Current?.ActiveVehicleId;
            if (!forceRefresh && string.Equals(lastKnownActiveVehicleId, activeVehicleId, StringComparison.Ordinal))
                return;

            lastKnownActiveVehicleId = activeVehicleId;
            var activeVehicle = FindVehicleControllerById(activeVehicleId);
            if (activeVehicle?.vehicleType == null || string.IsNullOrWhiteSpace(activeVehicle.vehicleType.vehicleTypeName))
                return;

            CaptureOriginalCapacity(activeVehicle.vehicleType);
            if (SetPersistedVehicleTypeCapacity(activeVehicle.vehicleType.vehicleTypeName, BigHaxSettings.ActiveVehicleCapacityOverride))
            {
                BigHaxLogger.Info(
                    context,
                    $"BigHax: saved active vehicle override {activeVehicle.vehicleType.vehicleTypeName} -> {BigHaxSettings.ActiveVehicleCapacityOverride}.");
            }

            activeVehicle.vehicleType.maxCargoCapacity = BigHaxSettings.ActiveVehicleCapacityOverride;
        }

        public void RestoreOriginalCapacities()
        {
            foreach (var vehicleTypeName in persistedVehicleTypeCapacities.Keys)
                RestoreVehicleType(vehicleTypeName);
            RestoreFreightTruckDeliveryPlaces();
            lastKnownActiveVehicleId = null;
        }

        private void ApplyFreightTruckDeliveryPlaces(ModContext context, int displayedDeliveryPlaces)
        {
            var freightTruckType = VehicleTypeHelper.GetVehicleType(BigHaxTargetIds.FreightTruckT1VehicleTypeName);
            if (freightTruckType == null)
            {
                BigHaxLogger.WarnOnce(
                    context,
                    "missing-vehicle-" + BigHaxTargetIds.FreightTruckT1VehicleTypeName,
                    $"BigHax: could not resolve vehicle type '{BigHaxTargetIds.FreightTruckT1VehicleTypeName}'.");
                return;
            }

            CaptureOriginalDeliveryPlaces(freightTruckType);
            freightTruckType.destinationsThatCanDeliver = ConvertDisplayedToRawDeliveryPlaces(displayedDeliveryPlaces);
        }

        private void CaptureOriginalCapacity(VehicleType vehicleType)
        {
            if (string.IsNullOrWhiteSpace(vehicleType.vehicleTypeName) || originalCapacities.ContainsKey(vehicleType.vehicleTypeName))
                return;

            originalCapacities[vehicleType.vehicleTypeName] = vehicleType.maxCargoCapacity;
        }

        private void CaptureOriginalDeliveryPlaces(VehicleType vehicleType)
        {
            if (string.IsNullOrWhiteSpace(vehicleType.vehicleTypeName) || originalDeliveryDestinations.ContainsKey(vehicleType.vehicleTypeName))
                return;

            originalDeliveryDestinations[vehicleType.vehicleTypeName] = vehicleType.destinationsThatCanDeliver;
        }

        private void RestoreVehicleType(string vehicleTypeName)
        {
            if (string.IsNullOrWhiteSpace(vehicleTypeName))
                return;

            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleTypeName);
            if (vehicleType == null)
                return;

            if (originalCapacities.TryGetValue(vehicleTypeName, out var originalCapacity))
                vehicleType.maxCargoCapacity = originalCapacity;
        }

        private void RestoreFreightTruckDeliveryPlaces()
        {
            var vehicleTypeName = BigHaxTargetIds.FreightTruckT1VehicleTypeName;
            var vehicleType = VehicleTypeHelper.GetVehicleType(vehicleTypeName);
            if (vehicleType == null)
                return;

            if (originalDeliveryDestinations.TryGetValue(vehicleTypeName, out var originalDestinations))
                vehicleType.destinationsThatCanDeliver = originalDestinations;
        }

        private static int ConvertDisplayedToRawDeliveryPlaces(int displayedDeliveryPlaces)
        {
            return Mathf.Max(1, Mathf.CeilToInt(displayedDeliveryPlaces / 2f));
        }

        private static VehicleController? FindVehicleControllerById(string? vehicleId)
        {
            if (string.IsNullOrWhiteSpace(vehicleId))
                return null;

            var allPlayerVehicles = VehicleHelper.AllPlayerVehicles;
            if (allPlayerVehicles == null)
                return null;

            foreach (var vehicleController in allPlayerVehicles)
            {
                if (vehicleController?.vehicleInstance != null &&
                    string.Equals(vehicleController.vehicleInstance.id, vehicleId, StringComparison.Ordinal))
                    return vehicleController;
            }

            return null;
        }

        private void ApplyPersistedVehicleTypeCapacities()
        {
            foreach (var pair in persistedVehicleTypeCapacities)
            {
                var vehicleType = VehicleTypeHelper.GetVehicleType(pair.Key);
                if (vehicleType == null)
                    continue;

                CaptureOriginalCapacity(vehicleType);
                if (vehicleType.maxCargoCapacity != pair.Value)
                    vehicleType.maxCargoCapacity = pair.Value;
            }
        }

        private void RestorePersistedVehicleTypeCapacities()
        {
            foreach (var vehicleTypeName in persistedVehicleTypeCapacities.Keys)
                RestoreVehicleType(vehicleTypeName);
        }

        private void LoadPersistedOverridesFromSave()
        {
            var saveGame = SaveGameManager.Current;
            var serialized = string.Empty;
            if (saveGame?.modData != null &&
                saveGame.modData.TryGetValue(VehicleOverridesModDataKey, out var storedValue) &&
                !string.IsNullOrWhiteSpace(storedValue))
            {
                serialized = storedValue;
            }

            if (string.Equals(lastAppliedSerializedOverrides, serialized, StringComparison.Ordinal))
                return;

            persistedVehicleTypeCapacities.Clear();
            if (!string.IsNullOrWhiteSpace(serialized))
            {
                foreach (var entry in serialized.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var separatorIndex = entry.LastIndexOf('=');
                    if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
                        continue;

                    var vehicleTypeName = entry.Substring(0, separatorIndex);
                    var capacityText = entry.Substring(separatorIndex + 1);
                    if (!int.TryParse(capacityText, out var capacity))
                        continue;

                    persistedVehicleTypeCapacities[vehicleTypeName] = capacity;
                }
            }

            lastAppliedSerializedOverrides = serialized;
        }

        private bool SetPersistedVehicleTypeCapacity(string vehicleTypeName, int capacity)
        {
            if (string.IsNullOrWhiteSpace(vehicleTypeName) ||
                persistedVehicleTypeCapacities.TryGetValue(vehicleTypeName, out var existingCapacity) && existingCapacity == capacity)
            {
                return false;
            }

            persistedVehicleTypeCapacities[vehicleTypeName] = capacity;
            SavePersistedOverridesToSave();
            return true;
        }

        private void SavePersistedOverridesToSave()
        {
            var saveGame = SaveGameManager.Current;
            if (saveGame == null)
                return;

            saveGame.modData ??= new Dictionary<string, string>();
            var serialized = SerializePersistedOverrides();
            saveGame.modData[VehicleOverridesModDataKey] = serialized;
            lastAppliedSerializedOverrides = serialized;
        }

        private string SerializePersistedOverrides()
        {
            var builder = new StringBuilder();
            foreach (var pair in persistedVehicleTypeCapacities)
            {
                if (builder.Length > 0)
                    builder.Append(';');

                builder.Append(pair.Key);
                builder.Append('=');
                builder.Append(pair.Value);
            }

            return builder.ToString();
        }
    }
}
