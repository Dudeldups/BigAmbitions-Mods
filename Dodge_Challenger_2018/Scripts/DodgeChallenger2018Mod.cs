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

[assembly: RegisterModClass(typeof(DodgeChallenger2018Mod))]

internal static class DodgeChallenger2018Diagnostics
{
    internal static bool DebugEnabled { get; set; } = false;
    internal static bool DealerEntryDebugEnabled { get; set; } = false;
    internal static bool WarehouseExitDebugEnabled { get; set; } = false;
    internal static bool PaintDebugEnabled { get; set; } = false;
    internal static bool DamageDebugEnabled { get; set; } = false;
    internal static bool LoadRecoveryDebugEnabled { get; set; } = false;
    internal static bool NpcTrafficDebugEnabled { get; set; } = false;
    internal static bool NpcDriverDebugEnabled { get; set; } = false;
    internal static bool AutoParkingDebugEnabled { get; set; } = false;
    internal static bool TrafficEnabled => DebugEnabled && NpcTrafficDebugEnabled;

    internal static void AutoParkingInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AutoParkingDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void AutoParkingWarn(ModContext? context, string message) =>
        context?.Logger.Warn(message);

    internal static void Info(ModContext? context, string message) { if (DebugEnabled) context?.Logger.Info(message); }
    internal static void PaintInfo(ModContext? context, string message) { if (DebugEnabled && PaintDebugEnabled) context?.Logger.Info(message); }
    internal static void DealerEntryInfo(ModContext? context, string message) { if (DebugEnabled && DealerEntryDebugEnabled) context?.Logger.Info(message); }
    internal static void WarehouseExitInfo(ModContext? context, string message) { if (DebugEnabled && WarehouseExitDebugEnabled) context?.Logger.Info(message); }
    internal static void DamageInfo(ModContext? context, string message) { if (DebugEnabled && DamageDebugEnabled) context?.Logger.Info(message); }
    internal static void LoadRecoveryInfo(ModContext? context, string message) { if (DebugEnabled && LoadRecoveryDebugEnabled) context?.Logger.Info(message); }
    internal static void TrafficInfo(string message) { if (TrafficEnabled) Debug.Log(message); }
}

[ModEntryOnInitializationLoad]
public sealed class DodgeChallenger2018Mod : IModBigAmbitions
{
    internal const string VehicleTypeName = "dodgechallenger2018-vehicle:vehicletype_dodgechallenger2018";
    private const string BundleKey = "AssetBundles/dodgechallenger2018.unity3d";
    private const string VehicleAssetPath = "Assets/Mods/Dodge_Challenger_2018/DodgeChallenger2018.asset";
    private const string VehiclePrefabPath = "Assets/Mods/Dodge_Challenger_2018/DodgeChallenger2018.prefab";

    private VehicleType? vehicleType;
    private DodgeChallenger2018Runtime? runtime;
    private DodgeChallenger2018AutoParkingGuard? autoParkingGuard;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"DodgeChallenger2018: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        var vehiclePrefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (vehicleType == null || vehiclePrefab == null)
        {
            context.Logger.Warn("DodgeChallenger2018: VehicleType or prefab is missing from the bundle.");
            return Task.CompletedTask;
        }

        ModdingAPI.RegisterModVehicleType(vehicleType);
        runtime = DodgeChallenger2018Runtime.Initialize(context, vehicleType.vehicleTypeName, vehiclePrefab);
        autoParkingGuard = DodgeChallenger2018AutoParkingGuard.Install(
            runtime.gameObject, context, vehicleType.vehicleTypeName);
        DodgeChallenger2018Diagnostics.Info(context,
            $"DodgeChallenger2018 registered price={vehicleType.price:0} maxSpeed={vehicleType.maxSpeed} enginePower={vehicleType.enginePower:0}.");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        autoParkingGuard?.Shutdown();
        autoParkingGuard = null;
        runtime?.Shutdown();
        runtime = null;
        if (vehicleType != null)
        {
            DodgeChallenger2018LuxuryDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }
        return Task.CompletedTask;
    }
}

internal static class DodgeChallenger2018LuxuryDealerStock
{
    private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
    private const string TargetBuildingSize = "ba:buildingsize_m";
    private const int TargetBuildingVersion = 1;
    private const string TargetLayoutName = "MurrayHillCarDealershipLuxury";

    private static readonly string[] DealerContactIds =
    {
        "The Hamptons Axis",
        "Manhattan Luxury Cars",
    };

    internal static bool IsTargetDealer(string? contactId)
    {
        if (string.IsNullOrEmpty(contactId)) return false;
        foreach (var dealerContactId in DealerContactIds)
            if (string.Equals(dealerContactId, contactId, StringComparison.Ordinal)) return true;
        return false;
    }

    internal static bool EnsureVehicleAvailable(string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName)) return false;
        if (AllDealersContainVehicle(vehicleName)) return true;
        if (BusinessLayoutSetHelper.loadingLayouts) return false;
        var stock = GetLuxuryDealerLayoutVehicles();
        if (stock.Count == 0) return false;
        var ready = true;
        foreach (var id in DealerContactIds) ready &= EnsureDealerStock(id, stock, vehicleName);
        return ready;
    }

    internal static void RemoveVehicle(string vehicleName)
    {
        foreach (var id in DealerContactIds)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(id, out List<string> existing) || existing == null) continue;
            var remaining = new List<string>();
            foreach (var entry in existing) if (!string.Equals(entry, vehicleName, StringComparison.Ordinal)) AddUnique(remaining, entry);
            if (remaining.Count == existing.Count) continue;
            if (remaining.Count == 0) ContractItemsForSaleService.RemoveContact(id);
            else ContractItemsForSaleService.SetVehiclesForContact(id, remaining);
        }
    }

    private static bool EnsureDealerStock(string id, List<string> vanilla, string vehicleName)
    {
        var merged = new List<string>();
        var had = ContractItemsForSaleService.TryGetVehiclesForContact(id, out List<string> existing);
        if (had && existing != null) AddUniqueRange(merged, existing);
        AddUniqueRange(merged, vanilla);
        AddUnique(merged, vehicleName);
        ContractItemsForSaleService.SetVehiclesForContact(id, merged);
        return true;
    }

    private static List<string> GetLuxuryDealerLayoutVehicles()
    {
        var stock = new List<string>();
        try
        {
            var set = BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
                TargetBusinessTypeName,
                new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
                TargetLayoutName.ToLowerInvariant(), false);
            if (set?.Items == null) return stock;
            foreach (var item in set.Items)
            {
                var p = item?.playerItemPurchaserSettings;
                if (p == null || !p.enabled || string.IsNullOrEmpty(p.itemName)) continue;
                var definition = ItemsGetter.GetByName(p.itemName);
                if (definition != null && !string.IsNullOrEmpty(definition.vehicleType)) AddUnique(stock, definition.vehicleType);
            }
        }
        catch { }
        return stock;
    }

    private static bool AllDealersContainVehicle(string vehicleName)
    {
        foreach (var id in DealerContactIds)
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(id, out List<string> stock) ||
                stock == null || !Contains(stock, vehicleName)) return false;
        return true;
    }

    private static void AddUniqueRange(List<string> target, IEnumerable<string> source) { foreach (var value in source) AddUnique(target, value); }
    private static void AddUnique(List<string> target, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        foreach (var existing in target) if (string.Equals(existing, value, StringComparison.Ordinal)) return;
        target.Add(value);
    }
    private static bool Contains(IEnumerable<string> stock, string value)
    {
        foreach (var entry in stock) if (string.Equals(entry, value, StringComparison.Ordinal)) return true;
        return false;
    }
}
