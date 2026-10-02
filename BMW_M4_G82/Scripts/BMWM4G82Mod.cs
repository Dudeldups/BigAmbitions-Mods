#nullable enable
using System;
using System.Collections.Generic;
using System.Collections;
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

[assembly: RegisterModClass(typeof(BMWM4G82Mod))]
[assembly: RegisterModClass(typeof(BMWM4G82MainMenuPrefabRegistration))]
[assembly: RegisterModClass(typeof(BMWM4G82CityPrefabRegistration))]

internal static class BMWM4G82Diagnostics
{
    internal static bool NpcTrafficDebugEnabled { get; set; } = false;
    internal static bool NpcLightingDebugEnabled { get; set; } = false;
    internal static bool NpcDriverDebugEnabled { get; set; } = false;

    internal static bool TrafficEnabled => DebugEnabled && NpcTrafficDebugEnabled;

    internal static void TrafficInfo(string message)
    {
        if (DebugEnabled && NpcTrafficDebugEnabled)
            Debug.Log(message);
    }

    internal static bool DebugEnabled { get; set; } = false;
    internal static bool NativeActivitiesDebugEnabled { get; set; } = false;
    internal static bool AutoParkingDebugEnabled { get; set; } = false;

    internal static void AutoParkingInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AutoParkingDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void AutoParkingWarn(ModContext? context, string message) =>
        context?.Logger.Warn(message);

    internal static bool LoadRecoveryDebugEnabled { get; set; } = false;

    internal static void LoadRecoveryInfo(ModContext? context, string message)
    {
        if (DebugEnabled && LoadRecoveryDebugEnabled)
            context?.Logger.Info(message);
    }
}

[ModEntryOnInitializationLoad]
public sealed class BMWM4G82Mod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "bmwm4g82-vehicle:vehicletype_bmwm4g82";

    internal const string BundleKey = "AssetBundles/bmw_m4_g82.unity3d";
    private const string VehicleAssetPath =
        "Assets/Mods/BMW_M4_G82/BMWM4G82.asset";
    internal const string VehiclePrefabAssetPath =
        "Assets/Mods/BMW_M4_G82/BMWM4G82.prefab";
    private const string PurchasePrefabName = "bmwm4g82";
    // This is a finished premium widebody/aero build, not a base M4. Apply the
    // dealer value before registration so every stock and private-driver path
    // uses the same price without mutating a shared saved VehicleType.
    private const float PremiumBuildPrice = 135000f;

    private VehicleType? vehicleType;
    private BMWM4G82Runtime? runtime;
    private BMWM4G82AutoParkingGuard? autoParkingGuard;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        BMWM4G82PrivateDriverSupport.Context = context;
        BMWM4G82NativeActivities.Initialize(context);
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"BMWM4G82: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"BMWM4G82: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        var vehiclePrefab = bundle.LoadAsset<GameObject>(VehiclePrefabAssetPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"BMWM4G82: purchase prefab load failed asset='{VehiclePrefabAssetPath}'. " +
                "Vehicle registration was skipped to prevent purchases that cannot spawn.");
            vehicleType = null;
            return Task.CompletedTask;
        }

        if (!string.Equals(PurchasePrefabName, vehiclePrefab.name, StringComparison.OrdinalIgnoreCase))
        {
            context.Logger.Warn(
                $"BMWM4G82: purchase prefab mismatch lookup='{PurchasePrefabName}' " +
                $"bundledPrefab='{vehiclePrefab.name}'. Vehicle registration was skipped.");
            vehicleType = null;
            return Task.CompletedTask;
        }

        var physicsVehicle = vehiclePrefab.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        if (physicsVehicle == null || physicsVehicle.stateSettings == null)
        {
            context.Logger.Warn(
                "BMWM4G82: bundled prefab has no usable NWH StateSettings. " +
                "Vehicle registration was skipped; rebuild the bundle for this platform.");
            vehicleType = null;
            return Task.CompletedTask;
        }

        var nativeVehicle = vehiclePrefab.GetComponent<VehicleController>();
        if (nativeVehicle != null)
            BMWM4G82NativeActivities.Configure(nativeVehicle, preparePrefab: true);

        if (!BMWM4G82PlayerPrefabRegistration.Bind(context, vehiclePrefab, "initialization-load"))
        {
            vehicleType = null;
            return Task.CompletedTask;
        }

        vehicleType.price = PremiumBuildPrice;
        ModdingAPI.RegisterModVehicleType(vehicleType);
        context.Logger.Info(
            $"BMWM4G82: registered '{vehicleType.vehicleTypeName}' " +
            $"price={vehicleType.price:0}, maxSpeed={vehicleType.maxSpeed}, " +
            $"enginePower={vehicleType.enginePower:0}, " +
            $"purchasePrefab='Vehicles/PlayerVehicles/{PurchasePrefabName}'.");
        runtime = BMWM4G82Runtime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab);
        autoParkingGuard = BMWM4G82AutoParkingGuard.Install(
            runtime.gameObject, context, vehicleType.vehicleTypeName);
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        autoParkingGuard?.Shutdown();
        autoParkingGuard = null;
        runtime?.Shutdown();
        runtime = null;
        BMWM4G82NativeActivities.Shutdown();
        BMWM4G82PlayerPrefabRegistration.Release();

        if (vehicleType != null)
        {
            BMWM4G82LuxuryDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }
}

// PrefabHelper clears its cache when a game unloads. Rebind before city Awake,
// so purchases and saved cars never need the global bundle filename fallback.
[ModEntryMainMenu]
public sealed class BMWM4G82MainMenuPrefabRegistration : IModBigAmbitions
{
    private ModContext? context;
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();

    public Task OnLoadAsync(ModContext modContext)
    {
        context = modContext;
        BMWM4G82PlayerPrefabRegistration.LoadAndBind(modContext, "main-menu-load");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        if (context != null)
            BMWM4G82PlayerPrefabRegistration.LoadAndBind(context, "main-menu-unload");
        context = null;
        return Task.CompletedTask;
    }
}

[ModEntryOnCityLoad]
public sealed class BMWM4G82CityPrefabRegistration : IModBigAmbitions
{
    public string[] RelativeAssetBundlePaths => Array.Empty<string>();
    public Task OnLoadAsync(ModContext context)
    {
        BMWM4G82PlayerPrefabRegistration.LoadAndBind(context, "city-load");
        return Task.CompletedTask;
    }
    public Task OnUnloadAsync() => Task.CompletedTask;
}

internal static class BMWM4G82PlayerPrefabRegistration
{
    private const string CacheKey = "Prefabs/Vehicles/PlayerVehicles/bmwm4g82.prefab";
    private static readonly FieldInfo? CacheField = typeof(Helpers.PrefabHelper).GetField(
        "PrefabCache", BindingFlags.Static | BindingFlags.NonPublic);
    private static GameObject? ownedPrefab;
    private static object? previousPrefab;
    private static bool capturedPrevious;

    internal static bool LoadAndBind(ModContext context, string source)
    {
        var bundle = AssetService.GetBundle(context.ModId, BMWM4G82Mod.BundleKey);
        var prefab = bundle?.LoadAsset<GameObject>(BMWM4G82Mod.VehiclePrefabAssetPath);
        return Bind(context, prefab, source);
    }

    internal static bool Bind(ModContext context, GameObject? prefab, string source)
    {
        try
        {
            var cache = CacheField?.GetValue(null) as IDictionary;
            if (prefab == null || cache == null)
                throw new InvalidOperationException("BMW player prefab or native cache is unavailable.");

            if (!capturedPrevious)
            {
                previousPrefab = cache.Contains(CacheKey) ? cache[CacheKey] : null;
                capturedPrevious = true;
            }
            var changed = !ReferenceEquals(cache[CacheKey], prefab);
            cache[CacheKey] = prefab;
            ownedPrefab = prefab;
            // Exercise the exact purchase lookup after binding, without spawning
            // or touching a dealer contract, money, inventory or save data.
            if (!ReferenceEquals(Helpers.PrefabHelper.LoadPrefabAssetByName(
                "Vehicles/PlayerVehicles/bmwm4g82"), prefab))
                throw new InvalidOperationException("Native purchase lookup did not resolve the BMW prefab.");
            if (changed && BMWM4G82Diagnostics.DebugEnabled)
                context.Logger.Info($"BMWM4G82: player prefab bound key='{CacheKey}' source='{source}'.");
            return true;
        }
        catch (Exception exception)
        {
            context.Logger.Warn($"BMWM4G82: player prefab binding failed source='{source}': " +
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

internal static class BMWM4G82LuxuryDealerStock
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
