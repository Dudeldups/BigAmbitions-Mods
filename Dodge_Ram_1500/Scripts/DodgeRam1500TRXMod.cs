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

[assembly: RegisterModClass(typeof(DodgeRam1500TRXMod))]
[assembly: RegisterModClass(typeof(DodgeRam1500TRXMainMenuPrefabRegistration))]
[assembly: RegisterModClass(typeof(DodgeRam1500TRXCityPrefabRegistration))]

internal static class DodgeRam1500TRXDiagnostics
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

    internal static bool LightingDebugEnabled { get; set; } = false;
    internal static bool NpcLightingDebugEnabled { get; set; } = false;
    internal static bool NpcDriverDebugEnabled { get; set; } = false;
    internal static bool DealerEntryDebugEnabled { get; set; } = false;
    internal static bool WarehouseExitDebugEnabled { get; set; } = false;
    internal static bool PaintDebugEnabled { get; set; } = false;
    internal static bool DamageDebugEnabled { get; set; } = false;
    internal static bool WashDebugEnabled { get; set; } = false;
    internal static bool SleepDebugEnabled { get; set; } = false;
    internal static bool CrashAudioDebugEnabled { get; set; } = false;
    internal static void CrashAudioInfo(ModContext? context, string message)
    {
        if (DebugEnabled && CrashAudioDebugEnabled) context?.Logger.Info(message);
    }

    internal static bool AudioDebugEnabled { get; set; } = false;
    internal static void AudioInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AudioDebugEnabled)
            context?.Logger.Info(message);
    }


    internal static void SleepInfo(ModContext? context, string message)
    {
        if (DebugEnabled && SleepDebugEnabled)
            context?.Logger.Info(message);
    }
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

    internal static void LoadRecoveryInfo(ModContext? context, string message)
    {
        if (DebugEnabled && LoadRecoveryDebugEnabled)
            context?.Logger.Info(message);
    }
}

[ModEntryOnInitializationLoad]
public sealed class DodgeRam1500TRXMod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "dodgeram1500trx-vehicle:vehicletype_dodgeram1500trx";

    internal const string BundleKey = "AssetBundles/dodgeram1500trx.unity3d";
    private const string VehicleAssetPath =
        "Assets/Mods/Dodge_Ram_1500/DodgeRam1500TRX.asset";
    internal const string VehiclePrefabPath =
        "Assets/Mods/Dodge_Ram_1500/DodgeRam1500TRX.prefab";

    private VehicleType? vehicleType;
    private DodgeRam1500TRXRuntime? runtime;
    private DodgeRam1500TRXAutoParkingGuard? autoParkingGuard;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"DodgeRam1500TRX: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"DodgeRam1500TRX: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        var vehiclePrefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"DodgeRam1500TRX: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            return Task.CompletedTask;
        }

        if (!DodgeRam1500TRXPlayerPrefabRegistration.Bind(context, vehiclePrefab, "initialization-load"))
        {
            vehicleType = null;
            return Task.CompletedTask;
        }
        if (!ModdingAPI.RegisterModVehicleType(vehicleType))
        {
            context.Logger.Warn("DodgeRam1500TRX: vehicle type registration failed; player prefab binding released.");
            DodgeRam1500TRXPlayerPrefabRegistration.Release();
            vehicleType = null;
            return Task.CompletedTask;
        }
        DodgeRam1500TRXDiagnostics.Info(
            context,
            $"DodgeRam1500TRX: registered '{vehicleType.vehicleTypeName}' " +
            $"price={vehicleType.price:0}, maxSpeed={vehicleType.maxSpeed}, " +
            $"enginePower={vehicleType.enginePower:0}.");
        runtime = DodgeRam1500TRXRuntime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab);
        autoParkingGuard = DodgeRam1500TRXAutoParkingGuard.Install(
            runtime.gameObject, context, vehicleType.vehicleTypeName);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        autoParkingGuard?.Shutdown();
        autoParkingGuard = null;
        runtime?.Shutdown();
        runtime = null;
        DodgeRam1500TRXPlayerPrefabRegistration.Release();

        if (vehicleType != null)
        {
            DodgeRam1500TRXLuxuryDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }
}

// Native PrefabHelper clears its cache at game unload. Rebind at the known
// main-menu/city transitions before saved cars and purchases request the prefab.
[ModEntryMainMenu]
public sealed class DodgeRam1500TRXMainMenuPrefabRegistration : IModBigAmbitions
{
    private ModContext? context;
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        context = modContext;
        DodgeRam1500TRXPlayerPrefabRegistration.LoadAndBind(modContext, "main-menu-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (context != null)
            DodgeRam1500TRXPlayerPrefabRegistration.LoadAndBind(context, "main-menu-unload");
        context = null;
        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public sealed class DodgeRam1500TRXCityPrefabRegistration : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();
    public Task OnLoadAsync(ModContext context)
    {
        DodgeRam1500TRXPlayerPrefabRegistration.LoadAndBind(context, "city-load");
        var bundle = AssetService.GetBundle(context.ModId, DodgeRam1500TRXMod.BundleKey);
        var prefab = bundle?.LoadAsset<GameObject>(DodgeRam1500TRXMod.VehiclePrefabPath);
        if (prefab != null)
        {
            DodgeRam1500TRXPrivateDriverSupport.SetContext(context);
            try { DodgeRam1500TRXPrivateDriverSupport.PrepareTrafficPool(prefab); }
            catch (Exception exception)
            {
                context.Logger.Warn($"DodgeRam1500TRX city-load traffic preparation failed: {exception.GetType().Name}: {exception.Message}");
            }
        }
        return Task.CompletedTask;
    }
    public Task OnUnloadAsync() => Task.CompletedTask;
}

internal static class DodgeRam1500TRXPlayerPrefabRegistration
{
    internal const string PrefabName = "Vehicles/PlayerVehicles/dodgeram1500trx";
    private const string CacheKey = "Prefabs/" + PrefabName + ".prefab";
    private static readonly FieldInfo? CacheField = typeof(Helpers.PrefabHelper).GetField(
        "PrefabCache", BindingFlags.Static | BindingFlags.NonPublic);
    private static GameObject? ownedPrefab;
    private static object? previousPrefab;
    private static bool hadPrevious;
    private static bool capturedPrevious;

    internal static bool LoadAndBind(ModContext context, string source)
    {
        var bundle = AssetService.GetBundle(context.ModId, DodgeRam1500TRXMod.BundleKey);
        var prefab = bundle?.LoadAsset<GameObject>(DodgeRam1500TRXMod.VehiclePrefabPath);
        return Bind(context, prefab, source);
    }

    internal static bool Bind(ModContext? context, GameObject? prefab, string source)
    {
        try
        {
            var cache = CacheField?.GetValue(null) as IDictionary;
            if (prefab == null || cache == null)
                throw new InvalidOperationException("Dodge player prefab or native cache is unavailable.");

            var current = cache.Contains(CacheKey) ? cache[CacheKey] : null;
            // A native cache clear starts a new ownership interval. Never restore
            // a stale mapping from the previously unloaded game on mod unload.
            if (!capturedPrevious || !ReferenceEquals(current, ownedPrefab))
            {
                previousPrefab = current;
                hadPrevious = cache.Contains(CacheKey);
                capturedPrevious = true;
            }
            var changed = !ReferenceEquals(current, prefab);
            cache[CacheKey] = prefab;
            ownedPrefab = prefab;
            // Exercise the exact native developer-spawn/dealer/save lookup.
            if (!ReferenceEquals(Helpers.PrefabHelper.LoadPrefabAssetByName(PrefabName), prefab))
                throw new InvalidOperationException("Native Dodge player prefab lookup did not resolve the bound asset.");
            if (changed)
                DodgeRam1500TRXDiagnostics.Info(context,
                    $"DodgeRam1500TRX: player prefab bound key='{CacheKey}' source='{source}', nativeLookup=True.");
            return true;
        }
        catch (Exception exception)
        {
            var message = $"DodgeRam1500TRX: player prefab binding failed source='{source}': " +
                $"{exception.GetType().Name}: {exception.Message}";
            if (context != null) context.Logger.Warn(message); else Debug.LogWarning(message);
            return false;
        }
    }

    internal static void Release()
    {
        var cache = CacheField?.GetValue(null) as IDictionary;
        if (cache != null && ownedPrefab != null && ReferenceEquals(cache[CacheKey], ownedPrefab))
        {
            if (hadPrevious) cache[CacheKey] = previousPrefab;
            else cache.Remove(CacheKey);
        }
        ownedPrefab = null;
        previousPrefab = null;
        hadPrevious = false;
        capturedPrevious = false;
    }
}

internal static class DodgeRam1500TRXLuxuryDealerStock
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

