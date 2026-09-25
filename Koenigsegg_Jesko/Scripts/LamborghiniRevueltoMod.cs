#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using Services;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(KoenigseggJeskoMod))]

[ModEntryOnInitializationLoad]
public sealed class KoenigseggJeskoMod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "koenigseggjesko-vehicle:vehicletype_koenigseggjesko";

    private const string BundleKey = "AssetBundles/koenigseggjesko.unity3d";
    private const string VehicleAssetPath =
        "Assets/Mods/Koenigsegg_Jesko/KoenigseggJesko.asset";
    private const string VehiclePrefabPath =
        "Assets/Mods/Koenigsegg_Jesko/KoenigseggJesko.prefab";

    private VehicleType? vehicleType;
    private KoenigseggJeskoRuntime? runtime;
    private KoenigseggJeskoAutoParkingGuard? autoParkingGuard;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        KoenigseggJeskoDiagnostics.LoadCollisionSetting(context);
        KoenigseggJeskoDiagnostics.LoadNpcColorSetting(context);
        KoenigseggJeskoDiagnostics.LoadPerformanceSetting(context);
        var loadStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"KoenigseggJesko: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        var bundleReadyAt = System.Diagnostics.Stopwatch.GetTimestamp();
        vehicleType = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        if (vehicleType == null)
        {
            context.Logger.Warn(
                $"KoenigseggJesko: failed to load vehicle type '{VehicleAssetPath}'.");
            return Task.CompletedTask;
        }

        var typeReadyAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var vehiclePrefab = bundle.LoadAsset<UnityEngine.GameObject>(VehiclePrefabPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"KoenigseggJesko: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            return Task.CompletedTask;
        }

        var prefabReadyAt = System.Diagnostics.Stopwatch.GetTimestamp();
        ModdingAPI.RegisterModVehicleType(vehicleType);
        KoenigseggJeskoDiagnostics.Info(context,
            $"KoenigseggJesko: registered '{vehicleType.vehicleTypeName}' " +
            $"price={vehicleType.price:0}, maxSpeed={vehicleType.maxSpeed}, " +
            $"enginePower={vehicleType.enginePower:0}.");
        runtime = KoenigseggJeskoRuntime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab);
        autoParkingGuard = KoenigseggJeskoAutoParkingGuard.Install(
            runtime.gameObject, context, vehicleType.vehicleTypeName);
        KoenigseggJeskoDiagnostics.PerformanceInfo(context,
            $"Jesko performance mod-load bundle={KoenigseggJeskoDiagnostics.Milliseconds(loadStartedAt, bundleReadyAt):0.0}ms " +
            $"type={KoenigseggJeskoDiagnostics.Milliseconds(bundleReadyAt, typeReadyAt):0.0}ms " +
            $"prefab={KoenigseggJeskoDiagnostics.Milliseconds(typeReadyAt, prefabReadyAt):0.0}ms " +
            $"register-runtime={KoenigseggJeskoDiagnostics.Milliseconds(prefabReadyAt, System.Diagnostics.Stopwatch.GetTimestamp()):0.0}ms.");
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
            KoenigseggJeskoLuxuryDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }
}

internal static class KoenigseggJeskoDiagnostics
{
    // Repaint and warehouse-transition validation are complete. Keep release
    // logging quiet while warnings and errors remain available for failures.
    internal static bool DebugEnabled { get; set; } = false;
    internal static bool AutoParkingDebugEnabled { get; set; } = false;

    internal static void AutoParkingInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AutoParkingDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void AutoParkingWarn(ModContext? context, string message) =>
        context?.Logger.Warn(message);

    internal static bool NpcTrafficDebugEnabled { get; set; } = false;
    internal static bool NpcLightingDebugEnabled { get; set; } = false;
    internal static bool NpcDriverDebugEnabled { get; set; } = false;
    internal static bool PaintDebugEnabled { get; set; } = false;
    internal static bool WarehouseTransitionDebugEnabled { get; set; } = false;
    internal static bool CollisionDebugEnabled { get; set; } = false;
    internal static bool PerformanceDebugEnabled { get; set; } = false;
    internal static bool NpcColorDebugEnabled { get; set; } = false;
    internal static bool TelemetryEnabled { get; set; } = false;
    private static ModContext? logContext;

    internal static void LoadCollisionSetting(ModContext context)
    {
        logContext = context;
        DebugEnabled = CollisionDebugEnabled = false;
        try
        {
            var assemblyDirectory = Path.GetDirectoryName(typeof(KoenigseggJeskoMod).Assembly.Location);
            if (string.IsNullOrEmpty(assemblyDirectory)) return;
            var settingPath = Path.Combine(assemblyDirectory, "Config", "CollisionDiagnostics.txt");
            if (!File.Exists(settingPath)) return;
            var setting = File.ReadAllText(settingPath).Trim();
            if (bool.TryParse(setting, out var enabled))
            {
                DebugEnabled = CollisionDebugEnabled = enabled;
                if (enabled)
                    context.Logger.Info("KoenigseggJesko collision diagnostics enabled for this session.");
            }
            else
                context.Logger.Warn($"KoenigseggJesko: invalid CollisionDiagnostics.txt value '{setting}'; expected true or false.");
        }
        catch (Exception exception)
        {
            context.Logger.Warn($"KoenigseggJesko: could not read collision diagnostics setting: {exception.Message}");
        }
    }

    internal static void LoadNpcColorSetting(ModContext context)
    {
        NpcColorDebugEnabled = false;
        try
        {
            var assemblyDirectory = Path.GetDirectoryName(typeof(KoenigseggJeskoMod).Assembly.Location);
            if (string.IsNullOrEmpty(assemblyDirectory)) return;
            var settingPath = Path.Combine(assemblyDirectory, "Config", "NpcColorDiagnostics.txt");
            if (!File.Exists(settingPath)) return;
            var setting = File.ReadAllText(settingPath).Trim();
            if (bool.TryParse(setting, out var enabled))
            {
                NpcColorDebugEnabled = enabled;
                DebugEnabled |= enabled;
                if (enabled)
                    context.Logger.Info("KoenigseggJesko NPC color diagnostics enabled for this session.");
            }
            else
                context.Logger.Warn($"KoenigseggJesko: invalid NpcColorDiagnostics.txt value '{setting}'; expected true or false.");
        }
        catch (Exception exception)
        {
            context.Logger.Warn($"KoenigseggJesko: could not read NPC color diagnostics setting: {exception.Message}");
        }
    }

    internal static void LoadPerformanceSetting(ModContext context)
    {
        PerformanceDebugEnabled = false;
        try
        {
            var assemblyDirectory = Path.GetDirectoryName(typeof(KoenigseggJeskoMod).Assembly.Location);
            if (string.IsNullOrEmpty(assemblyDirectory)) return;
            var configDirectory = Path.Combine(assemblyDirectory, "Config");
            var localSettingPath = Path.Combine(configDirectory, "PerformanceDiagnostics.local.txt");
            var settingPath = File.Exists(localSettingPath)
                ? localSettingPath
                : Path.Combine(configDirectory, "PerformanceDiagnostics.txt");
            if (!File.Exists(settingPath)) return;
            var setting = File.ReadAllText(settingPath).Trim();
            if (!bool.TryParse(setting, out var enabled))
            {
                context.Logger.Warn("KoenigseggJesko: invalid PerformanceDiagnostics.txt value; expected true or false.");
                return;
            }
            PerformanceDebugEnabled = enabled;
            DebugEnabled |= enabled;
            if (enabled)
                context.Logger.Info("Jesko performance diagnostics enabled for this session (10-minute frame sample).");
        }
        catch (Exception exception)
        {
            context.Logger.Warn($"KoenigseggJesko: could not read performance diagnostics setting: {exception.Message}");
        }
    }

    internal static double Milliseconds(long start, long end) =>
        (end - start) * 1000d / System.Diagnostics.Stopwatch.Frequency;

    internal static void PerformanceInfo(ModContext? context, string message)
    {
        if (DebugEnabled && PerformanceDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void NpcColorInfo(string message)
    {
        if (DebugEnabled && NpcColorDebugEnabled)
            logContext?.Logger.Info(message);
    }

    internal static void Warn(string message) => logContext?.Logger.Warn(message);

    internal static void Info(ModContext? context, string message)
    {
        if (DebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void PaintInfo(ModContext? context, string message)
    {
        if (PaintDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void WarehouseInfo(ModContext? context, string message)
    {
        if (WarehouseTransitionDebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void CollisionInfo(ModContext? context, string message)
    {
        if (DebugEnabled && CollisionDebugEnabled)
            context?.Logger.Info(message);
    }
}

internal static class KoenigseggJeskoLuxuryDealerStock
{
    private const string TargetBusinessTypeName = "ba:businesstype_cardealership";
    private const string TargetBuildingSize = "ba:buildingsize_m";
    private const int TargetBuildingVersion = 1;
    private const string TargetLayoutName = "MurrayHillCarDealershipLuxury";
    private const string TargetLayoutKey =
        "ba:businesstype_cardealership|ba:buildingsize_m|1|murrayhillcardealershipluxury";

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
                return false;
        }
        return true;
    }

    private static bool ContainsVehicle(IEnumerable<string> stock, string vehicleName)
    {
        foreach (var existingVehicle in stock)
            if (string.Equals(existingVehicle, vehicleName, StringComparison.Ordinal))
                return true;
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
