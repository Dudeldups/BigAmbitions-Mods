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
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(BigfootMonsterTruckMod))]

[ModEntryOnInitializationLoad]
public sealed class BigfootMonsterTruckMod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "bigfootmonstertruck-vehicle:vehicletype_bigfootmonstertruck";

    private const string BundleKey = "AssetBundles/bigfootmonstertruck.unity3d";
    private const string VehicleAssetPath =
        "Assets/Mods/BigfootMonsterTruck/BigfootMonsterTruck.asset";

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    private VehicleType? vehicleType;
    private BigfootMonsterTruckRuntime? runtime;

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"BigfootMonsterTruck: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"BigfootMonsterTruck: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        ModdingAPI.RegisterModVehicleType(vehicleType);
        runtime = BigfootMonsterTruckRuntime.Initialize(context, vehicleType.vehicleTypeName);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        runtime?.Shutdown();
        runtime = null;
        if (vehicleType != null)
        {
            BigfootTruckDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
        }

        vehicleType = null;
        return Task.CompletedTask;
    }
}

internal static class BigfootTruckDealerStock
{
    internal const string DealerContactId = "General US Trucks";
    private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
    private const string TargetBuildingSize = "ba:buildingsize_m";
    private const int TargetBuildingVersion = 1;
    private const string TargetLayoutName = "IndustryCityCarDealershipTrucks";
    internal static bool EnsureVehicleAvailable(string vehicleName, ModContext? context, string source)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return false;

        var merged = new List<string>();
        var hadExplicitStock = ContractItemsForSaleService.TryGetVehiclesForContact(
            DealerContactId,
            out List<string> existingStock);
        if (hadExplicitStock && existingStock != null)
        {
            if (ContainsVehicle(existingStock, vehicleName))
                return true;
            AddUniqueRange(merged, existingStock);
        }

        // GetOrLoadBusinessLayoutSet must not be called while the game's native
        // layout load is still running. The lifecycle coroutine waits for this
        // flag, while event-driven calls simply defer to that initialization.
        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        AddVehiclesFromTruckDealerLayout(merged);
        var vanillaCount = merged.Count;
        AddUnique(merged, vehicleName);

        // Avoid replacing the vanilla catalog with a one-vehicle list if the
        // dealer layout is not loaded yet. Lifecycle retries will try again.
        if (vanillaCount == 0 && !hadExplicitStock)
            return false;

        if (hadExplicitStock && existingStock != null && SameVehicleList(existingStock, merged))
            return true;

        try
        {
            ContractItemsForSaleService.SetVehiclesForContact(DealerContactId, merged);
            return true;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"BigfootMonsterTruck: truck dealer stock failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    internal static void RemoveVehicle(string vehicleName)
    {
        if (!ContractItemsForSaleService.TryGetVehiclesForContact(
                DealerContactId,
                out List<string> existingStock) || existingStock == null)
            return;

        var remaining = new List<string>();
        foreach (var entry in existingStock)
            if (!string.Equals(entry, vehicleName, StringComparison.Ordinal))
                AddUnique(remaining, entry);

        if (remaining.Count == existingStock.Count)
            return;
        if (remaining.Count == 0)
            ContractItemsForSaleService.RemoveContact(DealerContactId);
        else
            ContractItemsForSaleService.SetVehiclesForContact(DealerContactId, remaining);
    }

    private static void AddVehiclesFromTruckDealerLayout(List<string> stock)
    {
        try
        {
            var layout = TryGetTruckDealerLayoutSet();
            if (layout?.Items == null)
                return;

            foreach (var item in layout.Items)
            {
                var purchaser = item?.playerItemPurchaserSettings;
                if (purchaser == null || !purchaser.enabled || string.IsNullOrEmpty(purchaser.itemName))
                    continue;
                var definition = ItemsGetter.GetByName(purchaser.itemName);
                if (definition != null && !string.IsNullOrEmpty(definition.vehicleType))
                    AddUnique(stock, definition.vehicleType);
            }
        }
        catch
        {
            // Layout data is transient during loading; the runtime retries.
        }
    }

    private static BusinessLayoutSet? TryGetTruckDealerLayoutSet()
    {
        return BusinessLayoutSetHelper.GetOrLoadBusinessLayoutSet(
            TargetBusinessTypeName,
            new BuildingSizeInfo(TargetBuildingSize, TargetBuildingVersion),
            TargetLayoutName.ToLowerInvariant(),
            false);
    }

    private static void AddUniqueRange(List<string> target, IEnumerable<string> source)
    {
        foreach (var value in source)
            AddUnique(target, value);
    }

    private static void AddUnique(List<string> target, string value)
    {
        foreach (var existing in target)
            if (string.Equals(existing, value, StringComparison.Ordinal))
                return;
        target.Add(value);
    }

    private static bool ContainsVehicle(IEnumerable<string> stock, string vehicleName)
    {
        foreach (var entry in stock)
            if (string.Equals(entry, vehicleName, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static bool SameVehicleList(List<string> left, List<string> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                return false;
        return true;
    }
}
