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

[assembly: RegisterModClass(typeof(AudiR8Mod))]
[assembly: RegisterModClass(typeof(AudiR8MainMenuPrefabRegistration))]
[assembly: RegisterModClass(typeof(AudiR8CityPrefabRegistration))]

internal static class AudiR8Diagnostics
{
    // Release defaults. Focused diagnostics remain available for a future
    // regression, but are opt-in and require this global flag as well.
    internal static bool DebugEnabled { get; set; } = false;
    internal static bool AutoParkingDebugEnabled { get; set; } = false;

    internal static void AutoParkingInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AutoParkingDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void AutoParkingWarn(ModContext? context, string message) =>
        context?.Logger.Warn(message);

    internal static bool LightingDebugEnabled { get; set; } = false;
    internal static bool AudioDebugEnabled { get; set; } = false;
    internal static bool CrashAudioDebugEnabled { get; set; } = false;
    internal static void CrashAudioInfo(ModContext? context, string message)
    {
        if (DebugEnabled && CrashAudioDebugEnabled) context?.Logger.Info(message);
    }
    internal static bool WashDebugEnabled { get; set; } = false;
    internal static bool StripesDebugEnabled { get; set; } = false;
    internal static void StripesInfo(ModContext? context, string message)
    {
        if (DebugEnabled && StripesDebugEnabled) context?.Logger.Info(message);
    }
    internal static void WashInfo(ModContext? context, string message)
    {
        if (DebugEnabled && WashDebugEnabled) context?.Logger.Info(message);
    }
    internal static void AudioInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AudioDebugEnabled) context?.Logger.Info(message);
    }
    internal static bool DealerEntryDebugEnabled { get; set; } = false;
    internal static bool WarehouseExitDebugEnabled { get; set; } = false;
    internal static bool PaintDebugEnabled { get; set; } = false;
    internal static bool AppearanceDebugEnabled { get; set; } = false;
    internal static bool DrivetrainDebugEnabled { get; set; } = false;
    internal static bool DamageDebugEnabled { get; set; } = false;
    internal static bool CollisionDebugEnabled { get; set; } = false;

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

    internal static void AppearanceInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AppearanceDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void DrivetrainInfo(ModContext? context, string message)
    {
        if (DebugEnabled && DrivetrainDebugEnabled)
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

    internal static void DamageInfo(ModContext? context, string message)
    {
        if (DebugEnabled && DamageDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void CollisionInfo(ModContext? context, string message)
    {
        if (DebugEnabled && CollisionDebugEnabled)
            context?.Logger.Info(message);
    }

}

[ModEntryOnInitializationLoad]
public sealed class AudiR8Mod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "audir8-vehicle:vehicletype_audir8";

    internal const string BundleKey = "AssetBundles/audir8.unity3d";
    private const string VehicleAssetPath =
        "Assets/Mods/Audi_R8/AudiR8.asset";
    internal const string VehiclePrefabPath =
        "Assets/Mods/Audi_R8/AudiR8.prefab";

    private VehicleType? vehicleType;
    private AudiR8Runtime? runtime;
    private AudiR8AutoParkingGuard? autoParkingGuard;
    private AudiR8WorkshopButtons? workshopButtons;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"AudiR8: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"AudiR8: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        var vehiclePrefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"AudiR8: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            return Task.CompletedTask;
        }

        // Resolve the same key used by VehicleHelper before exposing this car
        // to purchases or developer spawns. The cross-bundle fallback can abort
        // while AssetService lazily loads another mod's bundle.
        if (!AudiR8PlayerPrefabRegistration.Bind(context, vehiclePrefab, "initialization-load"))
        {
            vehicleType = null;
            return Task.CompletedTask;
        }

        // Lamp asset readiness must never remove the vehicle from saves/dealers.
        // The loader reports any failure; registration remains independently ready.
        if (!AudiR8LightAssets.Load(bundle, context))
            context.Logger.Warn("AudiR8: lamp assets unavailable; vehicle registration continues.");

        AudiR8RacingStripes.Initialize(context);
        ModdingAPI.RegisterModVehicleType(vehicleType);
        AudiR8Diagnostics.Info(
            context,
            $"AudiR8: registered '{vehicleType.vehicleTypeName}' " +
            $"price={vehicleType.price:0}, maxSpeed={vehicleType.maxSpeed}, " +
            $"enginePower={vehicleType.enginePower:0}.");
        runtime = AudiR8Runtime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab);
        autoParkingGuard = AudiR8AutoParkingGuard.Install(
            runtime.gameObject, context, vehicleType.vehicleTypeName);
        workshopButtons = AudiR8WorkshopButtons.Install(runtime.gameObject, context);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        workshopButtons?.Shutdown();
        workshopButtons = null;
        autoParkingGuard?.Shutdown();
        autoParkingGuard = null;
        runtime?.Shutdown();
        runtime = null;
        AudiR8LightAssets.Clear();
        AudiR8PlayerPrefabRegistration.Release();
        AudiR8RacingStripes.Shutdown();

        if (vehicleType != null)
        {
            AudiR8LuxuryDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }
}

// PrefabHelper clears its cache when leaving a save. Both menu transitions
// and the city-load entrypoint restore the R8 key before vehicle creation.
[ModEntryMainMenu]
public sealed class AudiR8MainMenuPrefabRegistration : IModBigAmbitions
{
    private ModContext? context;
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        context = modContext;
        AudiR8PlayerPrefabRegistration.LoadAndBind(modContext, "main-menu-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (context != null)
            AudiR8PlayerPrefabRegistration.LoadAndBind(context, "main-menu-unload");
        context = null;
        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public sealed class AudiR8CityPrefabRegistration : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        AudiR8PlayerPrefabRegistration.LoadAndBind(context, "city-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync() => Task.CompletedTask;
}

internal static class AudiR8PlayerPrefabRegistration
{
    private const string PrefabName = "Vehicles/PlayerVehicles/audir8";
    private const string CacheKey = "Prefabs/" + PrefabName + ".prefab";
    private static readonly FieldInfo? CacheField = typeof(Helpers.PrefabHelper).GetField(
        "PrefabCache", BindingFlags.Static | BindingFlags.NonPublic);
    private static GameObject? ownedPrefab;
    private static object? previousPrefab;
    private static bool capturedPrevious;

    internal static bool LoadAndBind(ModContext context, string source)
    {
        var bundle = AssetService.GetBundle(context.ModId, AudiR8Mod.BundleKey);
        var prefab = bundle?.LoadAsset<GameObject>(AudiR8Mod.VehiclePrefabPath);
        return Bind(context, prefab, source);
    }

    internal static bool Bind(ModContext context, GameObject? prefab, string source)
    {
        try
        {
            var cache = CacheField?.GetValue(null) as IDictionary;
            if (prefab == null || cache == null)
                throw new InvalidOperationException("R8 player prefab or native cache is unavailable.");
            if (!string.Equals(prefab.name, "AudiR8", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unexpected bundled prefab '{prefab.name}'.");

            if (!capturedPrevious)
            {
                previousPrefab = cache.Contains(CacheKey) ? cache[CacheKey] : null;
                capturedPrevious = true;
            }
            var changed = !ReferenceEquals(cache[CacheKey], prefab);
            cache[CacheKey] = prefab;
            ownedPrefab = prefab;

            // Validate the actual native lookup without creating a vehicle or
            // changing the player's inventory, money or saved car instances.
            if (!ReferenceEquals(Helpers.PrefabHelper.LoadPrefabAssetByName(PrefabName), prefab))
                throw new InvalidOperationException("Native player lookup did not resolve the R8 prefab.");
            if (changed)
                AudiR8Diagnostics.Info(context,
                    $"AudiR8: player prefab bound key='{CacheKey}', source='{source}', lookupVerified=True.");
            return true;
        }
        catch (Exception exception)
        {
            context.Logger.Warn($"AudiR8: player prefab binding failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    internal static void Release()
    {
        var cache = CacheField?.GetValue(null) as IDictionary;
        if (cache != null && ownedPrefab != null && ReferenceEquals(cache[CacheKey], ownedPrefab))
        {
            if (previousPrefab != null)
                cache[CacheKey] = previousPrefab;
            else
                cache.Remove(CacheKey);
        }
        ownedPrefab = null;
        previousPrefab = null;
        capturedPrevious = false;
    }
}

internal static class AudiR8LuxuryDealerStock
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
        if (string.IsNullOrEmpty(contactId))
            return false;

        foreach (var dealerContactId in DealerContactIds)
        {
            if (string.Equals(dealerContactId, contactId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    internal static bool EnsureVehicleAvailable(string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return false;

        if (AllDealersContainVehicle(vehicleName))
            return true;

        if (BusinessLayoutSetHelper.loadingLayouts)
            return false;

        var vanillaStock = GetLuxuryDealerLayoutVehicles();
        if (vanillaStock.Count == 0)
            return false;

        var allDealersReady = true;
        foreach (var dealerContactId in DealerContactIds)
            allDealersReady &= EnsureDealerStock(dealerContactId, vanillaStock, vehicleName);
        return allDealersReady;
    }

    internal static void RemoveVehicle(string vehicleName)
    {
        if (string.IsNullOrWhiteSpace(vehicleName))
            return;

        foreach (var dealerContactId in DealerContactIds)
        {
            if (!ContractItemsForSaleService.TryGetVehiclesForContact(
                    dealerContactId,
                    out List<string> existingStock) ||
                existingStock == null)
            {
                continue;
            }

            var remainingStock = new List<string>();
            foreach (var existingVehicle in existingStock)
            {
                if (!string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                    AddUnique(remainingStock, existingVehicle);
            }

            if (remainingStock.Count == existingStock.Count)
                continue;
            if (remainingStock.Count == 0)
                ContractItemsForSaleService.RemoveContact(dealerContactId);
            else
                ContractItemsForSaleService.SetVehiclesForContact(dealerContactId, remainingStock);
        }
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

    private static List<string> GetLuxuryDealerLayoutVehicles()
    {
        var stock = new List<string>();
        try
        {
            var layoutSet = TryGetLuxuryDealerLayoutSet();
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

    private static BusinessLayoutSet? TryGetLuxuryDealerLayoutSet()
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
        foreach (var dealerContactId in DealerContactIds)
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


