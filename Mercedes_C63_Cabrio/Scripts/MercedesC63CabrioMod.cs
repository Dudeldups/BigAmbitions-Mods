#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem;
using Helpers;
using Blueprints;
using BusinessLayoutSets;
using Services;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(MercedesC63CabrioMod))]
[assembly: RegisterModClass(typeof(MercedesC63CabrioMainMenuPrefabRegistration))]
[assembly: RegisterModClass(typeof(MercedesC63CabrioCityPrefabRegistration))]

internal static class MercedesC63CabrioDiagnostics
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
    internal static bool AutoParkingDebugEnabled { get; set; } = false;

    internal static void AutoParkingInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AutoParkingDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void AutoParkingWarn(ModContext? context, string message) =>
        context?.Logger.Warn(message);

    internal static bool NpcLightingDebugEnabled { get; set; } = false;
    internal static bool NpcDriverDebugEnabled { get; set; } = false;
    internal static bool DealerEntryDebugEnabled { get; set; } = false;
    internal static bool WarehouseExitDebugEnabled { get; set; } = false;
    internal static bool PaintDebugEnabled { get; set; } = false;
    internal static bool DamageDebugEnabled { get; set; } = false;
    internal static bool LoadRecoveryDebugEnabled { get; set; } = false;
    internal static bool AudioDebugEnabled { get; set; } = false;
    internal static void AudioInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AudioDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static bool PerformanceDebugEnabled { get; set; } = false;
    internal static bool PerformanceEnabled => DebugEnabled && PerformanceDebugEnabled;
    internal static void PerformanceInfo(ModContext? context, string message)
    {
        if (!PerformanceEnabled) return;
        if (context != null) context.Logger.Info(message);
        else Debug.Log(message);
    }

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
}

[ModEntryOnInitializationLoad]
public sealed class MercedesC63CabrioMod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "mercedes-c63-cabrio:vehicletype_mercedes_c63_cabrio";

    internal const string BundleKey = "AssetBundles/mercedes_c63_cabrio.unity3d";
    private const string VehicleAssetPath =
        "Assets/Mods/Mercedes_C63_Cabrio/MercedesC63Cabrio.asset";
    internal const string VehiclePrefabPath =
        "Assets/Mods/Mercedes_C63_Cabrio/Mercedes.prefab";

    private bool vehicleTypeRegistered;
    private VehicleType? vehicleType;
    private MercedesC63CabrioRuntime? runtime;
    private MercedesC63CabrioAutoParkingGuard? autoParkingGuard;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        if (vehicleTypeRegistered)
            return Task.CompletedTask;

        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"MercedesC63Cabrio: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"MercedesC63Cabrio: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        var vehiclePrefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"MercedesC63Cabrio: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            return Task.CompletedTask;
        }

        // Native spawning uses GetIdWithoutType(), which returns only the second
        // underscore-delimited segment. Preserve the saved type ID and ensure
        // the matching file is resolvable before exposing a paid dealer contract.
        var nativePrefabName = "Vehicles/PlayerVehicles/" +
                               vehicleType.vehicleTypeName.GetIdWithoutType();
        if (!string.Equals(vehicleType.vehicleTypeName, VehicleTypeName, StringComparison.Ordinal) ||
            !string.Equals(vehicleType.vehicleTypeName.GetIdWithoutType(), "mercedes", StringComparison.Ordinal) ||
            vehiclePrefab.GetComponent<CarController>() == null)
        {
            context.Logger.Warn(
                $"MercedesC63Cabrio: refusing vehicle registration: native prefab '{nativePrefabName}' " +
                $"has invalid vehicle data or no Mercedes car controller.");
            vehicleType = null;
            return Task.CompletedTask;
        }

        if (!MercedesC63CabrioPlayerPrefabRegistration.Bind(context, vehiclePrefab, "initialization-load"))
        {
            vehicleType = null;
            return Task.CompletedTask;
        }

        vehicleTypeRegistered = ModdingAPI.RegisterModVehicleType(vehicleType);
        if (!vehicleTypeRegistered)
        {
            MercedesC63CabrioPlayerPrefabRegistration.Release();
            context.Logger.Warn($"MercedesC63Cabrio: vehicle type registration failed: '{vehicleType.vehicleTypeName}'.");
            vehicleType = null;
            return Task.CompletedTask;
        }
        MercedesC63CabrioPlayerPrefabRegistration.Enabled = true;
        MercedesC63CabrioDiagnostics.DealerEntryInfo(
            context, $"MercedesC63Cabrio: native spawn binding ready type='{vehicleType.vehicleTypeName}', prefab='{nativePrefabName}'.");
        MercedesC63CabrioDiagnostics.Info(
            context,
            $"MercedesC63Cabrio: registered '{vehicleType.vehicleTypeName}' " +
            $"price={vehicleType.price:0}, maxSpeed={vehicleType.maxSpeed}, " +
            $"enginePower={vehicleType.enginePower:0}.");
        runtime = MercedesC63CabrioRuntime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab);
        autoParkingGuard = MercedesC63CabrioAutoParkingGuard.Install(
            runtime.gameObject, context, vehicleType.vehicleTypeName);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        autoParkingGuard?.Shutdown();
        autoParkingGuard = null;
        runtime?.Shutdown();
        runtime = null;
        MercedesC63CabrioPlayerPrefabRegistration.Release();

        if (vehicleTypeRegistered && vehicleType != null)
        {
            MercedesC63CabrioLuxuryDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
            vehicleTypeRegistered = false;
        }

        return Task.CompletedTask;
    }
}

// The native cache is cleared on game unload. Bind at each native loading
// boundary, before saved vehicles or dealer purchases need a prefab lookup.
[ModEntryMainMenu]
public sealed class MercedesC63CabrioMainMenuPrefabRegistration : IModBigAmbitions
{
    private ModContext? context;
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        context = modContext;
        MercedesC63CabrioPlayerPrefabRegistration.LoadAndBind(modContext, "main-menu-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (context != null)
            MercedesC63CabrioPlayerPrefabRegistration.LoadAndBind(context, "main-menu-unload");
        context = null;
        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public sealed class MercedesC63CabrioCityPrefabRegistration : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();
    public Task OnLoadAsync(ModContext context)
    {
        MercedesC63CabrioPlayerPrefabRegistration.LoadAndBind(context, "city-load");
        return Task.CompletedTask;
    }
    public Task OnUnloadAsync() => Task.CompletedTask;
}

internal static class MercedesC63CabrioPlayerPrefabRegistration
{
    private const string CacheKey = "Prefabs/Vehicles/PlayerVehicles/mercedes.prefab";
    private static readonly FieldInfo? CacheField = typeof(PrefabHelper).GetField(
        "PrefabCache", BindingFlags.Static | BindingFlags.NonPublic);
    private static GameObject? ownedPrefab;
    private static UnityEngine.Object? previousPrefab;
    private static bool previousEntryExisted;
    private static bool capturedPrevious;
    internal static bool Enabled { get; set; }

    internal static bool LoadAndBind(ModContext context, string source)
    {
        if (!Enabled)
            return false;

        var bundle = AssetService.GetBundle(context.ModId, MercedesC63CabrioMod.BundleKey);
        var prefab = bundle?.LoadAsset<GameObject>(MercedesC63CabrioMod.VehiclePrefabPath);
        return Bind(context, prefab, source);
    }

    internal static bool Bind(ModContext? context, GameObject? prefab, string source)
    {
        try
        {
            var cache = CacheField?.GetValue(null) as Dictionary<string, UnityEngine.Object>;
            if (prefab == null || prefab.GetComponent<CarController>() == null || cache == null)
                throw new InvalidOperationException("Mercedes player prefab or native cache is unavailable.");
            var existingEntry = cache.TryGetValue(CacheKey, out var existing);
            if (existing != null && existing != prefab)
                throw new InvalidOperationException($"Native Mercedes key is already owned by '{existing.name}'.");
            if (!capturedPrevious)
            {
                previousEntryExisted = existingEntry;
                previousPrefab = existing;
                capturedPrevious = true;
            }
            var changed = existing != prefab;
            cache[CacheKey] = prefab;
            ownedPrefab = prefab;
            // Cache hit exercises the exact native lookup without enumerating
            // or lazily loading other mods' bundles during initialization.
            if (PrefabHelper.LoadPrefabAssetByName("Vehicles/PlayerVehicles/mercedes") != prefab)
                throw new InvalidOperationException("Native purchase lookup did not resolve the Mercedes prefab.");
            if (changed)
                MercedesC63CabrioDiagnostics.DealerEntryInfo(context,
                    $"MercedesC63Cabrio: player prefab bound key='{CacheKey}' source='{source}'.");
            return true;
        }
        catch (Exception exception)
        {
            context?.Logger.Warn($"MercedesC63Cabrio: player prefab binding failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    internal static void Release()
    {
        Enabled = false;
        var cache = CacheField?.GetValue(null) as Dictionary<string, UnityEngine.Object>;
        if (cache != null && ownedPrefab != null &&
            cache.TryGetValue(CacheKey, out var current) && current == ownedPrefab)
        {
            if (previousEntryExisted)
                cache[CacheKey] = previousPrefab!;
            else
                cache.Remove(CacheKey);
        }
        ownedPrefab = null;
        previousPrefab = null;
        previousEntryExisted = false;
        capturedPrevious = false;
    }
}

internal static class MercedesC63CabrioLuxuryDealerStock
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

