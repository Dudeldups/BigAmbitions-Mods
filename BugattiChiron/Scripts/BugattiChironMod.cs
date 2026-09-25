#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

[assembly: RegisterModClass(typeof(BugattiChironMod))]
[assembly: RegisterModClass(typeof(BugattiChironMainMenuRegistration))]
[assembly: RegisterModClass(typeof(BugattiChironCityRegistration))]

internal static class BugattiChironDiagnostics
{
    private const string WheelDebugMarker = "wheel-diagnostics.enabled";
    private const string AudioDebugMarker = "audio-diagnostics.enabled";
    private const string TrafficRarityDebugMarker = "traffic-rarity-diagnostics.enabled";
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
    internal static bool LoadRecoveryDebugEnabled { get; set; } = false;
    internal static bool WheelDebugEnabled { get; set; } = false;
    internal static bool AudioDebugEnabled { get; set; } = false;
    internal static bool TrafficRarityDebugEnabled { get; set; } = false;

    internal static void EnableLocalDiagnostics(ModContext context)
    {
        var markerPath = Path.Combine(
            Application.persistentDataPath,
            "ModsLocal", "BugattiChiron", "Config", WheelDebugMarker);
        if (File.Exists(markerPath))
        {
            DebugEnabled = true;
            WheelDebugEnabled = true;
            context.Logger.Info(
                "BugattiChiron: local wheel diagnostics enabled; " +
                "logging selected vehicle wheel poses every three seconds.");
        }

        var audioMarkerPath = Path.Combine(
            Application.persistentDataPath,
            "ModsLocal", "BugattiChiron", "Config", AudioDebugMarker);
        if (File.Exists(audioMarkerPath))
        {
            DebugEnabled = true;
            AudioDebugEnabled = true;
            context.Logger.Info("BugattiChiron: local audio diagnostics enabled.");
        }

        var trafficMarkerPath = Path.Combine(
            Application.persistentDataPath,
            "ModsLocal", "BugattiChiron", "Config", TrafficRarityDebugMarker);
        if (File.Exists(trafficMarkerPath))
        {
            DebugEnabled = true;
            TrafficRarityDebugEnabled = true;
            context.Logger.Info("BugattiChiron: local NPC traffic rarity diagnostics enabled.");
        }

    }

    internal static void LoadRecoveryInfo(ModContext? context, string message)
    {
        if (DebugEnabled && LoadRecoveryDebugEnabled)
            context?.Logger.Info(message);
    }
}

[ModEntryOnInitializationLoad]
public sealed class BugattiChironMod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "bugattichiron-vehicle:vehicletype_bugattichiron";

    internal const string BundleKey = "AssetBundles/bugattichiron.unity3d";
    internal const string VehicleAssetPath =
        "Assets/Mods/BugattiChiron/BugattiChiron.asset";
    internal const string VehiclePrefabPath =
        "Assets/Mods/BugattiChiron/BugattiChiron.prefab";

    private VehicleType? vehicleType;
    private BugattiChironRuntime? runtime;
    private BugattiChironAutoParkingGuard? autoParkingGuard;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        BugattiChironDiagnostics.EnableLocalDiagnostics(context);
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"BugattiChiron: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"BugattiChiron: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        var vehiclePrefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"BugattiChiron: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            return Task.CompletedTask;
        }

        vehicleType.autoParkSupported = true;
        vehicleType.maxCargoCapacity = 12;
        if (!BugattiChironVehicleTypeRegistration.EnsureRegistered(vehicleType))
        {
            context.Logger.Warn(
                $"BugattiChiron: failed to register vehicle type '{vehicleType.vehicleTypeName}'.");
        }
        BugattiChironVehicleTypeRegistration.EnsurePlayerPrefabRegistered(
            context,
            vehiclePrefab,
            "initialization-load");

        runtime = BugattiChironRuntime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab);
        autoParkingGuard = BugattiChironAutoParkingGuard.Install(
            runtime.gameObject, context, vehicleType.vehicleTypeName);
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
            BugattiChironLuxuryDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }
}

[ModEntryMainMenu]
public sealed class BugattiChironMainMenuRegistration : IModBigAmbitions
{
    private ModContext? context;
    private VehicleType? vehicleType;
    private GameObject? vehiclePrefab;

    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        context = modContext;
        vehicleType = BugattiChironVehicleTypeRegistration.LoadVehicleType(modContext);
        vehiclePrefab = BugattiChironVehicleTypeRegistration.LoadVehiclePrefab(modContext);
        BugattiChironVehicleTypeRegistration.EnsureRegistered(
            modContext,
            vehicleType,
            "main-menu-load");
        BugattiChironVehicleTypeRegistration.EnsurePlayerPrefabRegistered(
            modContext,
            vehiclePrefab,
            "main-menu-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        // MainMenu is unloaded before the city scenes are loaded. Rebinding here
        // guarantees saved Bugatti instances can resolve their VehicleType from
        // GameManager.Awake onward, without a runtime poll or delayed repair.
        if (context != null)
        {
            vehicleType ??= BugattiChironVehicleTypeRegistration.LoadVehicleType(context);
            BugattiChironVehicleTypeRegistration.EnsureRegistered(
                context,
                vehicleType,
                "main-menu-unload");
            vehiclePrefab ??= BugattiChironVehicleTypeRegistration.LoadVehiclePrefab(context);
            BugattiChironVehicleTypeRegistration.EnsurePlayerPrefabRegistered(
                context,
                vehiclePrefab,
                "main-menu-unload");
        }

        context = null;
        vehicleType = null;
        vehiclePrefab = null;
        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public sealed class BugattiChironCityRegistration : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext context)
    {
        var vehicleType = BugattiChironVehicleTypeRegistration.LoadVehicleType(context);
        BugattiChironVehicleTypeRegistration.EnsureRegistered(
            context,
            vehicleType,
            "city-load-fallback");
        BugattiChironVehicleTypeRegistration.EnsurePlayerPrefabRegistered(
            context,
            BugattiChironVehicleTypeRegistration.LoadVehiclePrefab(context),
            "city-load-fallback");

        return Task.CompletedTask;
    }

    public Task OnUnloadAsync() => Task.CompletedTask;
}

internal static class BugattiChironVehicleTypeRegistration
{
    private const string PlayerPrefabCacheKey =
        "Prefabs/Vehicles/PlayerVehicles/bugattichiron.prefab";

    internal static VehicleType? LoadVehicleType(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BugattiChironMod.BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn(
                $"BugattiChiron: vehicle bundle '{BugattiChironMod.BundleKey}' was unavailable during lifecycle registration.");
            return null;
        }

        var loadedVehicleType = bundle.LoadAsset<VehicleType>(BugattiChironMod.VehicleAssetPath);
        if (loadedVehicleType == null)
        {
            context.Logger.Warn(
                $"BugattiChiron: vehicle type '{BugattiChironMod.VehicleAssetPath}' was unavailable during lifecycle registration.");
            return null;
        }

        loadedVehicleType.autoParkSupported = true;
        loadedVehicleType.maxCargoCapacity = 12;
        return loadedVehicleType;
    }

    internal static GameObject? LoadVehiclePrefab(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BugattiChironMod.BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn(
                $"BugattiChiron: vehicle bundle '{BugattiChironMod.BundleKey}' was unavailable during lifecycle prefab registration.");
            return null;
        }

        var loadedVehiclePrefab = bundle.LoadAsset<GameObject>(BugattiChironMod.VehiclePrefabPath);
        if (loadedVehiclePrefab == null)
        {
            context.Logger.Warn(
                $"BugattiChiron: vehicle prefab '{BugattiChironMod.VehiclePrefabPath}' was unavailable during lifecycle registration.");
        }

        return loadedVehiclePrefab;
    }

    internal static bool EnsurePlayerPrefabRegistered(
        ModContext context,
        GameObject? vehiclePrefab,
        string source)
    {
        if (vehiclePrefab == null)
            return false;

        var cacheField = typeof(Helpers.PrefabHelper).GetField(
            "PrefabCache",
            BindingFlags.Static | BindingFlags.NonPublic);
        var cache = cacheField?.GetValue(null) as IDictionary;
        if (cache == null)
        {
            context.Logger.Warn(
                $"BugattiChiron: player prefab cache unavailable source='{source}'.");
            return false;
        }

        if (!cache.Contains(PlayerPrefabCacheKey) ||
            !ReferenceEquals(cache[PlayerPrefabCacheKey], vehiclePrefab))
        {
            cache[PlayerPrefabCacheKey] = vehiclePrefab;
            context.Logger.Info(
                $"BugattiChiron: player prefab rebound before city activation source='{source}'.");
        }

        return ReferenceEquals(cache[PlayerPrefabCacheKey], vehiclePrefab);
    }

    internal static bool EnsureRegistered(
        ModContext context,
        VehicleType? vehicleType,
        string source)
    {
        if (vehicleType == null)
            return false;

        var wasRegistered = VehicleTypeHelper.GetVehicleType(vehicleType.vehicleTypeName) != null;
        var ready = EnsureRegistered(vehicleType);
        if (!ready)
        {
            context.Logger.Warn(
                $"BugattiChiron: vehicle type registration failed source='{source}'.");
        }
        else if (!wasRegistered)
        {
            context.Logger.Info(
                $"BugattiChiron: vehicle type rebound before city activation source='{source}'.");
        }

        return ready;
    }

    internal static bool EnsureRegistered(VehicleType vehicleType)
    {
        var current = VehicleTypeHelper.GetVehicleType(vehicleType.vehicleTypeName);
        if (current != null && VehicleTypeHelper.IsModVehicleType(vehicleType.vehicleTypeName))
            return true;

        ModdingAPI.RegisterModVehicleType(vehicleType);
        return VehicleTypeHelper.GetVehicleType(vehicleType.vehicleTypeName) != null &&
               VehicleTypeHelper.IsModVehicleType(vehicleType.vehicleTypeName);
    }
}

internal static class BugattiChironLuxuryDealerStock
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

    private static bool ContainsVehicle(IEnumerable<string> stock, string vehicleName)
    {
        foreach (var existingVehicle in stock)
        {
            if (string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                return true;
        }

        return false;
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
