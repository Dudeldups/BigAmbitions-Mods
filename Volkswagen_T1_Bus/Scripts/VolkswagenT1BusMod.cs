#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using Services;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(VolkswagenT1BusMod))]

internal static class VolkswagenT1BusDiagnostics
{
    // Release defaults. Focused diagnostics remain available for a future
    // regression, but are opt-in and require this global flag as well.
    internal static bool DebugEnabled { get; set; } = false;
    internal static bool NpcTrafficDebugEnabled { get; set; } = false;
    internal static bool NpcLightingDebugEnabled { get; set; } = false;
    internal static bool NpcDriverDebugEnabled { get; set; } = false;
    internal static bool DealerEntryDebugEnabled { get; set; } = false;
    internal static bool WarehouseExitDebugEnabled { get; set; } = false;
    internal static bool PrivateDriverDebugEnabled { get; set; } = false;
    internal static bool PaintDebugEnabled { get; set; } = false;
    internal static bool DamageDebugEnabled { get; set; } = false;
    internal static bool LoadRecoveryDebugEnabled { get; set; } = false;
    internal static bool AudioDebugEnabled { get; set; } = false;
    internal static bool LightingDebugEnabled { get; set; } = false;
    internal static bool WashDebugEnabled { get; set; } = false;

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
        if (DebugEnabled && DamageDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void LoadRecoveryInfo(ModContext? context, string message)
    {
        if (DebugEnabled && LoadRecoveryDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void AudioInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AudioDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void LightingInfo(ModContext? context, string message)
    {
        if (DebugEnabled && LightingDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void WashInfo(ModContext? context, string message)
    {
        if (DebugEnabled && WashDebugEnabled)
            context?.Logger.Info(message);
    }
}

[ModEntryOnInitializationLoad]
public sealed class VolkswagenT1BusMod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "volkswagent1bus-vehicle:vehicletype_volkswagent1bus";

    private const string BundleKey = "AssetBundles/volkswagen_t1_bus.unity3d";
    private const string VehicleAssetPath =
        "Assets/Mods/Volkswagen_T1_Bus/VolkswagenT1Bus.asset";
    private const string VehiclePrefabPath =
        "Assets/Mods/Volkswagen_T1_Bus/VolkswagenT1Bus.prefab";
    private const string PlayerPrefabCacheKey =
        "Prefabs/Vehicles/PlayerVehicles/volkswagent1bus.prefab";

    private VehicleType? vehicleType;
    private VolkswagenT1BusRuntime? runtime;
    private ModContext? context;
    private GameObject? registeredPlayerPrefab;
    private IDictionary? prefabCache;
    private object? previousCachedPrefab;
    private bool hadPreviousCachedPrefab;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        this.context = context;
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"VolkswagenT1Bus: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"VolkswagenT1Bus: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        var vehiclePrefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"VolkswagenT1Bus: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            return Task.CompletedTask;
        }

        RegisterPlayerPrefab(context, vehiclePrefab);
        ModdingAPI.RegisterModVehicleType(vehicleType);
        VolkswagenT1BusDiagnostics.Info(
            context,
            $"VolkswagenT1Bus: registered '{vehicleType.vehicleTypeName}' " +
            $"price={vehicleType.price:0}, maxSpeed={vehicleType.maxSpeed}, " +
            $"enginePower={vehicleType.enginePower:0}, " +
            $"purchasePrefab='Vehicles/PlayerVehicles/volkswagent1bus'.");
        runtime = VolkswagenT1BusRuntime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        runtime?.Shutdown();
        runtime = null;
        RestorePlayerPrefab();

        if (vehicleType != null)
        {
            VolkswagenT1BusCityCarsDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }

    private void RegisterPlayerPrefab(ModContext modContext, GameObject vehiclePrefab)
    {
        try
        {
            var cacheField = typeof(Helpers.PrefabHelper).GetField(
                "PrefabCache",
                BindingFlags.Static | BindingFlags.NonPublic);
            prefabCache = cacheField?.GetValue(null) as IDictionary;
            if (prefabCache == null)
            {
                modContext.Logger.Warn(
                    $"VolkswagenT1Bus: player prefab cache unavailable for '{PlayerPrefabCacheKey}'.");
                return;
            }

            hadPreviousCachedPrefab = prefabCache.Contains(PlayerPrefabCacheKey);
            previousCachedPrefab = hadPreviousCachedPrefab
                ? prefabCache[PlayerPrefabCacheKey]
                : null;
            prefabCache[PlayerPrefabCacheKey] = vehiclePrefab;
            registeredPlayerPrefab = vehiclePrefab;
            VolkswagenT1BusDiagnostics.Info(
                modContext,
                $"VolkswagenT1Bus: registered player prefab cache key='{PlayerPrefabCacheKey}'.");
        }
        catch (Exception exception)
        {
            modContext.Logger.Warn(
                $"VolkswagenT1Bus: failed to register player prefab '{PlayerPrefabCacheKey}': " +
                exception.Message);
        }
    }

    private void RestorePlayerPrefab()
    {
        try
        {
            if (prefabCache != null && registeredPlayerPrefab != null &&
                prefabCache.Contains(PlayerPrefabCacheKey) &&
                ReferenceEquals(prefabCache[PlayerPrefabCacheKey], registeredPlayerPrefab))
            {
                if (hadPreviousCachedPrefab)
                    prefabCache[PlayerPrefabCacheKey] = previousCachedPrefab;
                else
                    prefabCache.Remove(PlayerPrefabCacheKey);

                VolkswagenT1BusDiagnostics.Info(
                    context,
                    $"VolkswagenT1Bus: restored player prefab cache key='{PlayerPrefabCacheKey}' on unload.");
            }
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                $"VolkswagenT1Bus: failed to restore player prefab cache on unload: " +
                exception.Message);
        }
        finally
        {
            prefabCache = null;
            registeredPlayerPrefab = null;
            previousCachedPrefab = null;
            hadPreviousCachedPrefab = false;
            context = null;
        }
    }
}

internal static class VolkswagenT1BusCityCarsDealerStock
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

