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

[assembly: RegisterModClass(typeof(KiaCeedSportswagonMod))]
[assembly: RegisterModClass(typeof(KiaCeedSportswagonMainMenuPrefabRegistration))]
[assembly: RegisterModClass(typeof(KiaCeedSportswagonCityPrefabRegistration))]

internal static class KiaCeedSportswagonDiagnostics
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
    internal static bool NativeAudioDebugEnabled { get; set; } = false;
    internal static bool NpcDriverDebugEnabled { get; set; } = false;
    internal static bool DealerEntryDebugEnabled { get; set; } = false;
    internal static bool WarehouseExitDebugEnabled { get; set; } = false;
    internal static bool PaintDebugEnabled { get; set; } = false;
    internal static bool DamageDebugEnabled { get; set; } = false;
    internal static bool WashDebugEnabled { get; set; } = false;
    internal static bool DamageDiagnosticsEnabled => DebugEnabled && DamageDebugEnabled;
    internal static bool PaintDiagnosticsEnabled => DebugEnabled && PaintDebugEnabled;
    internal static bool HighwaySeamDebugEnabled { get; set; } = false;
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

    internal static void DamageInfo(ModContext? context, string message)
    {
        if (DebugEnabled && DamageDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void WashInfo(ModContext? context, string message)
    {
        if (DebugEnabled && WashDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void HighwaySeamInfo(ModContext? context, string message)
    {
        if (DebugEnabled && HighwaySeamDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void LoadRecoveryInfo(ModContext? context, string message)
    {
        if (DebugEnabled && LoadRecoveryDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void NativeAudioInfo(ModContext? context, string message)
    {
        if (DebugEnabled && NativeAudioDebugEnabled)
            context?.Logger.Info(message);
    }
}

[Serializable]
internal sealed class KiaCeedSportswagonDiagnosticsConfig
{
    public bool debugEnabled = false;
    public bool damageDebugEnabled = false;
    public bool washDebugEnabled = false;
    public bool paintDebugEnabled = false;
    public bool npcTrafficDebugEnabled = false;
    public bool nativeAudioDebugEnabled = false;
}

[ModEntryOnInitializationLoad]
public sealed class KiaCeedSportswagonMod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "kiaceedsportswagon-vehicle:vehicletype_kiaceedsportswagon";

    internal const string BundleKey = "AssetBundles/kiaceedsportswagon.unity3d";
    private const string LightOverlayBundleKey =
        "AssetBundles/kiaceedsportswagonlights.unity3d";
    private const string VehicleAssetPath =
        "Assets/Mods/Kia_Ceed_Sportswagon/KiaCeedSportswagon.asset";
    internal const string VehiclePrefabPath =
        "Assets/Mods/Kia_Ceed_Sportswagon/KiaCeedSportswagon.prefab";
    internal const string PlayerPrefabName = "kiaceedsportswagon";
    private const string LightOverlayModelPath =
        "Assets/Mods/Kia_Ceed_Sportswagon/Models/GeneratedLights/KiaCeedSWLightOverlays.glb";

    private VehicleType? vehicleType;
    private KiaCeedSportswagonRuntime? runtime;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey, LightOverlayBundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        LoadDiagnosticsConfiguration(context);

        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"KiaCeedSportswagon: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"KiaCeedSportswagon: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }
        ConfigureVehicleTypeFeatures(vehicleType);

        var vehiclePrefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"KiaCeedSportswagon: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            return Task.CompletedTask;
        }

        if (!KiaCeedSportswagonPlayerPrefabRegistration.Bind(
                context, vehiclePrefab, "initialization-load"))
        {
            vehicleType = null;
            return Task.CompletedTask;
        }

        var lightOverlayBundle = AssetService.GetBundle(context.ModId, LightOverlayBundleKey);
        if (lightOverlayBundle == null)
        {
            context.Logger.Warn(
                $"KiaCeedSportswagon: failed to load light overlay bundle '{LightOverlayBundleKey}'.");
            return Task.CompletedTask;
        }

        var lightOverlayPrefab = lightOverlayBundle.LoadAsset<GameObject>(LightOverlayModelPath);
        if (lightOverlayPrefab == null)
        {
            context.Logger.Warn(
                $"KiaCeedSportswagon: failed to load Blender light overlay model '{LightOverlayModelPath}'.");
            return Task.CompletedTask;
        }

        ModdingAPI.RegisterModVehicleType(vehicleType);
        KiaCeedSportswagonDiagnostics.Info(
            context,
            $"KiaCeedSportswagon: registered '{vehicleType.vehicleTypeName}' " +
            $"price={vehicleType.price:0}, maxSpeed={vehicleType.maxSpeed}, " +
            $"enginePower={vehicleType.enginePower:0}, " +
            $"maxCargoCapacity={vehicleType.maxCargoCapacity:0}.");
        runtime = KiaCeedSportswagonRuntime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab,
            lightOverlayPrefab);
        return Task.CompletedTask;
    }

    internal static void ConfigureVehicleTypeFeatures(VehicleType type)
    {
        type.maxCargoCapacity = 16;
        type.autoParkSupported = false;
    }

    private static void LoadDiagnosticsConfiguration(ModContext context)
    {
        KiaCeedSportswagonDiagnostics.DebugEnabled = false;
        KiaCeedSportswagonDiagnostics.DamageDebugEnabled = false;
        KiaCeedSportswagonDiagnostics.WashDebugEnabled = false;
        KiaCeedSportswagonDiagnostics.PaintDebugEnabled = false;
        KiaCeedSportswagonDiagnostics.NpcTrafficDebugEnabled = false;
        KiaCeedSportswagonDiagnostics.NativeAudioDebugEnabled = false;

        var configPath = Path.Combine(context.ModRootPath, "Config", "diagnostics.json");
        if (!File.Exists(configPath))
            return;

        try
        {
            var config = JsonUtility.FromJson<KiaCeedSportswagonDiagnosticsConfig>(
                File.ReadAllText(configPath));
            if (config == null)
            {
                context.Logger.Warn(
                    $"KiaCeedSportswagon: diagnostics config '{configPath}' was empty or invalid; " +
                    "diagnostics remain disabled.");
                return;
            }

            KiaCeedSportswagonDiagnostics.DebugEnabled = config.debugEnabled;
            KiaCeedSportswagonDiagnostics.DamageDebugEnabled = config.damageDebugEnabled;
            KiaCeedSportswagonDiagnostics.WashDebugEnabled = config.washDebugEnabled;
            KiaCeedSportswagonDiagnostics.NativeAudioDebugEnabled = config.nativeAudioDebugEnabled;
            KiaCeedSportswagonDiagnostics.PaintDebugEnabled = config.paintDebugEnabled;
            KiaCeedSportswagonDiagnostics.NpcTrafficDebugEnabled =
                config.npcTrafficDebugEnabled;
            if (KiaCeedSportswagonDiagnostics.DamageDiagnosticsEnabled)
            {
                context.Logger.Info(
                    "KiaCeedSportswagon: bounded collision and deformation diagnostics enabled " +
                    "by Config/diagnostics.json.");
            }
            if (KiaCeedSportswagonDiagnostics.PaintDiagnosticsEnabled)
            {
                context.Logger.Info(
                    "KiaCeedSportswagon: bounded paint and finish diagnostics enabled " +
                    "by Config/diagnostics.json.");
            }
        }
        catch (Exception exception)
        {
            KiaCeedSportswagonDiagnostics.DebugEnabled = false;
            KiaCeedSportswagonDiagnostics.DamageDebugEnabled = false;
            KiaCeedSportswagonDiagnostics.PaintDebugEnabled = false;
            KiaCeedSportswagonDiagnostics.NpcTrafficDebugEnabled = false;
            KiaCeedSportswagonDiagnostics.NativeAudioDebugEnabled = false;
            context.Logger.Warn(
                $"KiaCeedSportswagon: failed to read diagnostics config " +
                $"'{configPath}' ({exception.GetType().Name}: {exception.Message}); " +
                "diagnostics remain disabled.");
        }
    }

    public Task OnUnloadAsync()
    {
        runtime?.Shutdown();
        runtime = null;
        KiaCeedSportswagonSleepBindings.Release();
        KiaCeedSportswagonPlayerPrefabRegistration.Release();

        if (vehicleType != null)
        {
            KiaCeedSportswagonDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }
}

// The game resolves a mod vehicle's player prefab through PrefabHelper's native
// key. Bind it during initial registration and after each load that can clear
// PrefabHelper's cache, before the city scene starts spawning vehicles.
[ModEntryMainMenu]
public sealed class KiaCeedSportswagonMainMenuPrefabRegistration : IModBigAmbitions
{
    private ModContext? context;
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        context = modContext;
        KiaCeedSportswagonPlayerPrefabRegistration.LoadAndBind(
            modContext, "main-menu-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (context != null)
            KiaCeedSportswagonPlayerPrefabRegistration.LoadAndBind(
                context, "main-menu-unload");
        context = null;
        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public sealed class KiaCeedSportswagonCityPrefabRegistration : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        KiaCeedSportswagonPlayerPrefabRegistration.LoadAndBind(
            modContext, "city-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync() => Task.CompletedTask;
}

internal static class KiaCeedSportswagonPlayerPrefabRegistration
{
    private const string CacheKey =
        "Prefabs/Vehicles/PlayerVehicles/kiaceedsportswagon.prefab";
    private const string PrefabLookupPath =
        "Vehicles/PlayerVehicles/kiaceedsportswagon";
    private static readonly FieldInfo? CacheField = typeof(Helpers.PrefabHelper).GetField(
        "PrefabCache", BindingFlags.Static | BindingFlags.NonPublic);
    private static GameObject? ownedPrefab;
    private static object? previousPrefab;
    private static bool capturedPrevious;

    internal static bool LoadAndBind(ModContext context, string source)
    {
        var bundle = AssetService.GetBundle(
            context.ModId, KiaCeedSportswagonMod.BundleKey);
        var prefab = bundle?.LoadAsset<GameObject>(
            KiaCeedSportswagonMod.VehiclePrefabPath);
        return Bind(context, prefab, source);
    }

    internal static bool Bind(
        ModContext context,
        GameObject? prefab,
        string source)
    {
        try
        {
            var cache = CacheField?.GetValue(null) as IDictionary;
            if (prefab == null || cache == null)
                throw new InvalidOperationException(
                    "Kia player prefab or native prefab cache is unavailable.");

            if (!capturedPrevious)
            {
                previousPrefab = cache.Contains(CacheKey) ? cache[CacheKey] : null;
                capturedPrevious = true;
            }

            if (!string.Equals(
                    prefab.name,
                    KiaCeedSportswagonMod.PlayerPrefabName,
                    StringComparison.Ordinal))
            {
                var bundledName = prefab.name;
                prefab.name = KiaCeedSportswagonMod.PlayerPrefabName;
                KiaCeedSportswagonDiagnostics.Info(
                    context,
                    $"KiaCeedSportswagon: normalized player prefab name " +
                    $"'{bundledName}' to '{prefab.name}' source='{source}'.");
            }

            var changed = !ReferenceEquals(cache[CacheKey], prefab);
            cache[CacheKey] = prefab;
            ownedPrefab = prefab;

            // Verify the same lookup used by player-vehicle spawning, without
            // creating an instance or touching save, money, or dealer state.
            if (!ReferenceEquals(
                    Helpers.PrefabHelper.LoadPrefabAssetByName(PrefabLookupPath),
                    prefab))
            {
                throw new InvalidOperationException(
                    "Native player-vehicle prefab lookup did not resolve the Kia prefab.");
            }

            if (changed)
            {
                KiaCeedSportswagonDiagnostics.Info(
                    context,
                    $"KiaCeedSportswagon: player prefab bound key='{CacheKey}' " +
                    $"source='{source}'.");
            }
            return true;
        }
        catch (Exception exception)
        {
            context.Logger.Warn(
                $"KiaCeedSportswagon: player prefab binding failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    internal static void Release()
    {
        var cache = CacheField?.GetValue(null) as IDictionary;
        if (cache != null && ownedPrefab != null &&
            ReferenceEquals(cache[CacheKey], ownedPrefab))
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

internal static class KiaCeedSportswagonDealerStock
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

