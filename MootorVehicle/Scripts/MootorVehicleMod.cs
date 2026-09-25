#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using Services;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(MootorVehicle.MootorVehicleMod))]

namespace MootorVehicle
{
    [ModEntryOnInitializationLoad]
    public sealed class MootorVehicleMod : IModBigAmbitions
    {
        private const string BundleKey = "AssetBundles/mootorvehicle.unity3d";
        private const string VehicleAssetPath = "Assets/Mods/MootorVehicle/MootorVehicle.asset";

        public string[] RelativeAssetBundlePaths => new[] { BundleKey };

        private VehicleType? vehicleType;
        private MootorVehicleRuntime? runtime;

        public Task OnLoadAsync(ModContext context)
        {
            var bundle = AssetService.GetBundle(context.ModId, BundleKey);
            if (bundle == null)
            {
                context.Logger.Warn($"Moo-tor Vehicle: failed to load bundle '{BundleKey}'.");
                return Task.CompletedTask;
            }

            vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
            if (vehicleType == null)
            {
                context.Logger.Warn($"Moo-tor Vehicle: failed to load vehicle type '{VehicleAssetPath}'.");
                return Task.CompletedTask;
            }

            vehicleType.maxSpeed = Mathf.RoundToInt(MootorVehicleFuelController.RegularSpeedLimit);
            vehicleType.enginePower = MootorVehicleFuelController.RegularEnginePower;

            ModdingAPI.RegisterModVehicleType(vehicleType);
            runtime = MootorVehicleRuntime.Initialize(context, vehicleType.vehicleTypeName);
            MootorVehicleDiagnostics.Info(
                context,
                $"Moo-tor Vehicle: registered '{vehicleType.vehicleTypeName}' " +
                $"speed={vehicleType.maxSpeed} power={vehicleType.enginePower}.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Shutdown();
            runtime = null;

            if (vehicleType == null)
                return Task.CompletedTask;

            MootorVehicleDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
            return Task.CompletedTask;
        }
    }

    internal static class MootorVehicleDealerStock
    {
        private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
        private const string TargetBuildingSize = "ba:buildingsize_d";
        private const int TargetBuildingVersion = 2;
        private const string TargetLayoutName = "GarmentDistrictCarDealershipCheap";
        private const string TargetDealerContactId = "City Cars";

        private static readonly string[] TargetDealerContactIds =
        {
            TargetDealerContactId
        };

        private static readonly string[] LegacyLuxuryDealerContactIds =
        {
            "The Hamptons Axis",
            "Manhattan Luxury Cars"
        };

        private static readonly string[] ManagedDealerContactIds =
        {
            TargetDealerContactId,
            "The Hamptons Axis",
            "Manhattan Luxury Cars"
        };

        internal static bool IsTargetDealer(string? contactId)
        {
            if (string.IsNullOrEmpty(contactId))
                return false;

            return string.Equals(TargetDealerContactId, contactId, StringComparison.Ordinal);
        }

        internal static bool EnsureVehicleAvailable(string vehicleName, ModContext? context)
        {
            if (string.IsNullOrWhiteSpace(vehicleName))
                return false;

            if (AllDealersContainVehicle(vehicleName))
                return true;

            if (BusinessLayoutSetHelper.loadingLayouts)
                return false;

            foreach (var legacyDealerContactId in LegacyLuxuryDealerContactIds)
                RemoveVehicleFromDealer(legacyDealerContactId, vehicleName, context);

            var vanillaStock = GetCityCarsLayoutVehicles();
            if (vanillaStock.Count == 0)
                return false;

            return EnsureDealerStock(TargetDealerContactId, vanillaStock, vehicleName, context);
        }

        internal static void RemoveVehicle(string vehicleName)
        {
            if (string.IsNullOrWhiteSpace(vehicleName))
                return;

            foreach (var dealerContactId in ManagedDealerContactIds)
                RemoveVehicleFromDealer(dealerContactId, vehicleName, null);
        }

        private static void RemoveVehicleFromDealer(
            string dealerContactId,
            string vehicleName,
            ModContext? context)
        {
            try
            {
                if (!ContractItemsForSaleService.TryGetVehiclesForContact(
                        dealerContactId,
                        out List<string> existingStock) ||
                    existingStock == null)
                {
                    return;
                }

                var remainingStock = new List<string>();
                foreach (var existingVehicle in existingStock)
                    if (!string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                        AddUnique(remainingStock, existingVehicle);

                if (remainingStock.Count == existingStock.Count)
                    return;

                if (remainingStock.Count == 0)
                    ContractItemsForSaleService.RemoveContact(dealerContactId);
                else
                    ContractItemsForSaleService.SetVehiclesForContact(dealerContactId, remainingStock);

                MootorVehicleDiagnostics.Info(
                    context,
                    $"Moo-tor Vehicle: removed '{vehicleName}' from dealer '{dealerContactId}'.");
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle: could not remove '{vehicleName}' from dealer '{dealerContactId}': " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        private static bool EnsureDealerStock(
            string dealerContactId,
            List<string> vanillaStock,
            string vehicleName,
            ModContext? context)
        {
            try
            {
                var mergedStock = new List<string>();
                var hadExplicitStock = ContractItemsForSaleService.TryGetVehiclesForContact(
                    dealerContactId,
                    out List<string> existingStock);

                if (hadExplicitStock && existingStock != null)
                    AddUniqueRange(mergedStock, existingStock);

                AddUniqueRange(mergedStock, vanillaStock);
                AddUnique(mergedStock, vehicleName);

                if (hadExplicitStock && existingStock != null && SameVehicleList(existingStock, mergedStock))
                    return true;

                ContractItemsForSaleService.SetVehiclesForContact(dealerContactId, mergedStock);
                MootorVehicleDiagnostics.Info(
                    context,
                    $"Moo-tor Vehicle: added '{vehicleName}' to dealer '{dealerContactId}'.");
                return true;
            }
            catch (Exception exception)
            {
                context?.Logger.Warn(
                    $"Moo-tor Vehicle: could not update '{dealerContactId}' vehicle catalog: " +
                    $"{exception.GetType().Name}: {exception.Message}");
                return false;
            }
        }

        private static List<string> GetCityCarsLayoutVehicles()
        {
            var stock = new List<string>();

            try
            {
                var layoutSet = TryGetCityCarsLayoutSet();
                if (layoutSet?.Items == null)
                    return stock;

                foreach (var item in layoutSet.Items)
                {
                    var purchaserSettings = item?.playerItemPurchaserSettings;
                    if (purchaserSettings == null ||
                        !purchaserSettings.enabled ||
                        string.IsNullOrEmpty(purchaserSettings.itemName))
                    {
                        continue;
                    }

                    var itemDefinition = ItemsGetter.GetByName(purchaserSettings.itemName);
                    if (itemDefinition == null || string.IsNullOrEmpty(itemDefinition.vehicleType))
                        continue;

                    AddUnique(stock, itemDefinition.vehicleType);
                }
            }
            catch
            {
                // The layout can be unavailable during a menu/save transition; the runtime retries.
            }

            return stock;
        }

        private static BusinessLayoutSet? TryGetCityCarsLayoutSet()
        {
            return BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                TargetBusinessTypeName,
                new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
                TargetLayoutName.ToLowerInvariant(),
                false);
        }

        private static bool AllDealersContainVehicle(string vehicleName)
        {
            foreach (var dealerContactId in TargetDealerContactIds)
            {
                if (!ContractItemsForSaleService.TryGetVehiclesForContact(
                        dealerContactId,
                        out List<string> existingStock) ||
                    existingStock == null ||
                    !ContainsVehicle(existingStock, vehicleName))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsVehicle(IEnumerable<string> stock, string vehicleName)
        {
            foreach (var existingVehicle in stock)
                if (string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                    return true;

            return false;
        }

        private static void AddUniqueRange(List<string> stock, IEnumerable<string> vehicles)
        {
            foreach (var vehicle in vehicles)
                AddUnique(stock, vehicle);
        }

        private static void AddUnique(List<string> stock, string vehicleName)
        {
            if (string.IsNullOrEmpty(vehicleName))
                return;

            foreach (var existingVehicle in stock)
                if (string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                    return;

            stock.Add(vehicleName);
        }

        private static bool SameVehicleList(List<string> existingStock, List<string> desiredStock)
        {
            if (existingStock.Count != desiredStock.Count)
                return false;

            for (var index = 0; index < existingStock.Count; index++)
                if (!string.Equals(existingStock[index], desiredStock[index], StringComparison.Ordinal))
                    return false;

            return true;
        }
    }

    internal static class MootorVehicleDiagnostics
    {
        // Release builds do not define this symbol, so both the call and message construction are
        // removed by the C# compiler. It can be enabled locally for focused troubleshooting.
        [System.Diagnostics.Conditional("MOOTORVEHICLE_DIAGNOSTICS")]
        internal static void Info(ModContext? context, string message)
        {
            context?.Logger.Info(message);
        }
    }
}
