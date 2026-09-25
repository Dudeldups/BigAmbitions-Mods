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

[assembly: RegisterModClass(typeof(ChevroletCamaro1967Mod))]

internal static class ChevroletCamaro1967Diagnostics
{
    internal static bool NpcTrafficDebugEnabled { get; set; } = false;

    internal static bool TrafficEnabled => DebugEnabled && NpcTrafficDebugEnabled;

    internal static void TrafficInfo(string message)
    {
        if (DebugEnabled && NpcTrafficDebugEnabled)
            Debug.Log(message);
    }

    // Release defaults. Focused diagnostics remain available for a future
    // regression, but are opt-in and require this global flag as well.
    internal static bool DebugEnabled { get; set; } = false;
    internal static bool DealerEntryDebugEnabled { get; set; } = false;
    internal static bool WarehouseExitDebugEnabled { get; set; } = false;
    internal static bool PrivateDriverDebugEnabled { get; set; } = false;
    internal static bool PaintDebugEnabled { get; set; } = false;
    // Temporary focused damage investigation; unrelated debug channels stay off.
    internal static bool DamageDebugEnabled { get; set; } = false;
    internal static bool LoadRecoveryDebugEnabled { get; set; } = false;

    internal static void Info(ModContext? context, string message)
    {
        if (DebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void PaintInfo(ModContext? context, string message)
    {
        if (DebugEnabled && PaintDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void DealerEntryInfo(ModContext? context, string message)
    {
        if (DebugEnabled && DealerEntryDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void WarehouseExitInfo(ModContext? context, string message)
    {
        if (DebugEnabled && WarehouseExitDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void PrivateDriverInfo(ModContext? context, string message)
    {
        if (DebugEnabled && PrivateDriverDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void DamageInfo(ModContext? context, string message)
    {
        if (DamageDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void LoadRecoveryInfo(ModContext? context, string message)
    {
        if (DebugEnabled && LoadRecoveryDebugEnabled)
            context?.Logger.Info(message);
    }
}

[ModEntryOnInitializationLoad]
public sealed class ChevroletCamaro1967Mod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "chevroletcamaro1967-vehicle:vehicletype_chevroletcamaro1967";

    private const string BundleKey = "AssetBundles/chevroletcamaro1967.unity3d";
    private const string VehicleAssetPath =
        "Assets/Mods/Chevrolet_Camaro_1967/ChevroletCamaro1967.asset";
    private const string VehiclePrefabPath =
        "Assets/Mods/Chevrolet_Camaro_1967/ChevroletCamaro1967.prefab";

    private VehicleType? vehicleType;
    private ChevroletCamaro1967Runtime? runtime;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"ChevroletCamaro1967: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"ChevroletCamaro1967: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        var vehiclePrefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"ChevroletCamaro1967: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            return Task.CompletedTask;
        }

        ModdingAPI.RegisterModVehicleType(vehicleType);
        ChevroletCamaro1967Diagnostics.Info(
            context,
            $"ChevroletCamaro1967: registered '{vehicleType.vehicleTypeName}' " +
            $"price={vehicleType.price:0}, maxSpeed={vehicleType.maxSpeed}, " +
            $"enginePower={vehicleType.enginePower:0}.");
        runtime = ChevroletCamaro1967Runtime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        runtime?.Shutdown();
        runtime = null;

        if (vehicleType != null)
        {
            ChevroletCamaro1967CityCarsDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }
}

internal static class ChevroletCamaro1967CityCarsDealerStock
{
    private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
    private const string TargetBuildingSize = "ba:buildingsize_d";
    private const int TargetBuildingVersion = 2;
    private const string TargetLayoutName = "GarmentDistrictCarDealershipCheap";
    private const string TargetDealerContactId = "City Cars";

    private static readonly string[] TargetDealerContactIds =
    {
        TargetDealerContactId,
    };

    private static readonly string[] LegacyPremiumDealerContactIds =
    {
        "The Hamptons Axis",
        "Manhattan Luxury Cars",
    };

    private static readonly string[] ManagedDealerContactIds =
    {
        TargetDealerContactId,
        "The Hamptons Axis",
        "Manhattan Luxury Cars",
    };

    internal static bool IsTargetDealer(string? contactId)
    {
        if (string.IsNullOrEmpty(contactId))
            return false;

        return string.Equals(TargetDealerContactId, contactId, StringComparison.Ordinal);
    }

    internal static bool EnsureVehicleAvailable(string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return false;

        if (AllDealersContainVehicle(vehicleName))
        {
            RemoveLegacyPremiumVehicle(vehicleName);
            return true;
        }

        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        RemoveLegacyPremiumVehicle(vehicleName);

        var vanillaStock = GetCityCarsLayoutVehicles();
        if (vanillaStock.Count == 0)
            return false;

        return EnsureDealerStock(TargetDealerContactId, vanillaStock, vehicleName);
    }

    internal static void RemoveVehicle(string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return;

        foreach (var dealerContactId in ManagedDealerContactIds)
            RemoveVehicleFromDealer(dealerContactId, vehicleName);
    }

    private static void RemoveLegacyPremiumVehicle(string vehicleName)
    {
        foreach (var dealerContactId in LegacyPremiumDealerContactIds)
            RemoveVehicleFromDealer(dealerContactId, vehicleName);
    }

    private static void RemoveVehicleFromDealer(string dealerContactId, string vehicleName)
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
        {
            if (!string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                AddUnique(remainingStock, existingVehicle);
        }

        if (remainingStock.Count == existingStock.Count)
            return;
        if (remainingStock.Count == 0)
            ContractItemsForSaleService.RemoveContact(dealerContactId);
        else
            ContractItemsForSaleService.SetVehiclesForContact(dealerContactId, remainingStock);
    }

    private static bool EnsureDealerStock(
        string dealerContactId,
        List<string> vanillaStock,
        string vehicleName)
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
        return true;
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
                if (itemDefinition != null && !string.IsNullOrEmpty(itemDefinition.vehicleType))
                    AddUnique(stock, itemDefinition.vehicleType);
            }
        }
        catch
        {
            // Layout data is transient while a save is loading. The runtime retries.
        }

        return stock;
    }

    private static BusinessLayoutSet? TryGetCityCarsLayoutSet()
    {
        if (BusinessLayoutSetHelper.loadingLayouts)
            return null;

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

    private static void AddUniqueRange(List<string> target, IEnumerable<string> source)
    {
        foreach (var value in source)
            AddUnique(target, value);
    }

    private static void AddUnique(List<string> target, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        foreach (var existing in target)
        {
            if (string.Equals(existing, value, StringComparison.Ordinal))
                return;
        }

        target.Add(value);
    }

    private static bool ContainsVehicle(IEnumerable<string> stock, string vehicleName)
    {
        foreach (var entry in stock)
        {
            if (string.Equals(entry, vehicleName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool SameVehicleList(List<string> left, List<string> right)
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
}

