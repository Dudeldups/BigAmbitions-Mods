#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using BigAmbitions.Items;
using Blueprints;
using BusinessLayoutSets;
using Services;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(FerrariSF90SpiderMod))]

[ModEntryOnInitializationLoad]
public sealed class FerrariSF90SpiderMod : IModBigAmbitions
{
    internal const string VehicleTypeName =
        "ferrarisf90spider-vehicle:vehicletype_ferrarisf90spider";

    private const string BundleKey = "AssetBundles/ferrarisf90spider.unity3d";
    private const string VehiclePrefabPath =
        "Assets/Mods/Ferrari_SF90_Spider/FerrariSF90Spider.prefab";

    private VehicleType? vehicleType;
    private FerrariSF90SpiderRuntime? runtime;
    private FerrariSF90SpiderAutoParkingGuard? autoParkingGuard;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn($"FerrariSF90Spider: failed to load bundle '{BundleKey}'.");
            return Task.CompletedTask;
        }

        vehicleType = CreateRuntimeVehicleType(context);
        if (vehicleType == null)
        {
            context.Logger.Warn("FerrariSF90Spider: failed to create runtime VehicleType.");
            return Task.CompletedTask;
        }

        var vehiclePrefab = bundle.LoadAsset<UnityEngine.GameObject>(VehiclePrefabPath);
        if (vehiclePrefab == null)
        {
            context.Logger.Warn(
                $"FerrariSF90Spider: failed to load vehicle prefab '{VehiclePrefabPath}'.");
            UnityEngine.Object.Destroy(vehicleType);
            vehicleType = null;
            return Task.CompletedTask;
        }

        BindRuntimeVehicleTypeToPrefab(vehiclePrefab, vehicleType, context);
        ModdingAPI.RegisterModVehicleType(vehicleType);
        FerrariSF90SpiderDiagnostics.Info(context,
            $"FerrariSF90Spider: registered '{vehicleType.vehicleTypeName}' " +
            $"price={vehicleType.price:0}, maxSpeed={vehicleType.maxSpeed}, " +
            $"enginePower={vehicleType.enginePower:0}.");
        runtime = FerrariSF90SpiderRuntime.Initialize(
            context,
            vehicleType.vehicleTypeName,
            vehiclePrefab);
        autoParkingGuard = FerrariSF90SpiderAutoParkingGuard.Install(
            runtime.gameObject, context, vehicleType.vehicleTypeName);
        return Task.CompletedTask;
    }

    private static VehicleType CreateRuntimeVehicleType(ModContext context)
    {
        try
        {
            var type = UnityEngine.ScriptableObject.CreateInstance<VehicleType>();
            type.name = "FerrariSF90Spider";

            SetRuntimeMember(type, "vehicleTypeName", VehicleTypeName);
            SetRuntimeMember(type, "price", 558000f);
            SetRuntimeMember(type, "itemVersion", string.Empty);
            SetRuntimeMember(type, "maxFuel", 68f);
            SetRuntimeMember(type, "maxCargoCapacity", 2);
            SetRuntimeMember(type, "maxSpeed", 340);
            SetRuntimeMember(type, "enginePower", 735f);
            SetRuntimeMember(type, "brakeForce", 2450f);
            SetRuntimeMember(type, "turnRadius", 25f);
            SetRuntimeMember(type, "damageIntensity", 0.80f);
            SetRuntimeMember(type, "fitsHandTruck", false);
            SetRuntimeMember(type, "fitsFlatbed", false);
            SetRuntimeMember(type, "autoParkSupported", true);
            SetRuntimeMember(type, "taxDeductible", false);
            SetRuntimeMember(type, "hasRadio", true);
            SetRuntimeMember(type, "isLuxuryCar", true);
            SetRuntimeMember(type, "requiredDeliveryDriverSkillValue", 0f);
            SetRuntimeMember(type, "destinationsThatCanDeliver", 2);
            SetRuntimeMember(type, "countsForPersonalGoals", true);
            SetRuntimeMember(type, "spawnInPlayerObject", false);
            SetRuntimeMember(type, "usePedestrianCam", false);
            SetRuntimeMember(type, "autoDestroyAfterMinutes", -1f);
            SetRuntimeMember(type, "enclosed", true);
            SetRuntimeMember(type, "canGetDirty", true);
            SetRuntimeMember(type, "dirtinessTimer", 600f);
            SetRuntimeMember(type, "cleanByRainTimer", 360f);

            return type;
        }
        catch (Exception ex)
        {
            context.Logger.Error(ex);
            return null;
        }
    }

    private static void BindRuntimeVehicleTypeToPrefab(
        UnityEngine.GameObject prefab,
        VehicleType type,
        ModContext context)
    {
        var assigned = 0;
        foreach (var component in prefab.GetComponentsInChildren<UnityEngine.Component>(true))
        {
            if (component == null)
                continue;

            if (TrySetNamedMember(component, "vehicleType", type))
                assigned++;

            var vehicleInstance = GetNamedMember(component, "vehicleInstance");
            if (vehicleInstance != null)
                TrySetNamedMember(vehicleInstance, "vehicleTypeName", VehicleTypeName);
        }

        FerrariSF90SpiderDiagnostics.Info(
            context,
            $"FerrariSF90Spider: bound runtime VehicleType to {assigned} prefab member(s).");
    }

    private static void SetRuntimeMember(object target, string name, object value)
    {
        if (!TrySetNamedMember(target, name, value))
            return;
    }

    private static bool TrySetNamedMember(object target, string name, object value)
    {
        if (target == null)
            return false;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (field != null && !field.IsInitOnly)
            {
                var converted = ConvertRuntimeValue(value, field.FieldType);
                if (converted != null || !field.FieldType.IsValueType)
                {
                    field.SetValue(target, converted);
                    return true;
                }
            }

            var property = type.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (property?.CanWrite == true)
            {
                var converted = ConvertRuntimeValue(value, property.PropertyType);
                if (converted != null || !property.PropertyType.IsValueType)
                {
                    property.SetValue(target, converted, null);
                    return true;
                }
            }
        }

        return false;
    }

    private static object GetNamedMember(object target, string name)
    {
        if (target == null)
            return null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (field != null)
                return field.GetValue(target);

            var property = type.GetProperty(name, flags | BindingFlags.DeclaredOnly);
            if (property?.CanRead == true)
                return property.GetValue(target, null);
        }

        return null;
    }

    private static object ConvertRuntimeValue(object value, Type targetType)
    {
        if (value == null)
            return null;

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (effectiveType.IsInstanceOfType(value))
            return value;

        if (effectiveType.IsEnum)
        {
            if (value is string text)
                return Enum.Parse(effectiveType, text, true);
            return Enum.ToObject(effectiveType, Convert.ToInt32(value, CultureInfo.InvariantCulture));
        }

        try
        {
            return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public Task OnUnloadAsync()
    {
        autoParkingGuard?.Shutdown();
        autoParkingGuard = null;
        runtime?.Shutdown();
        runtime = null;

        if (vehicleType != null)
        {
            FerrariSF90SpiderLuxuryDealerStock.RemoveVehicle(vehicleType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(vehicleType.vehicleTypeName);
            UnityEngine.Object.Destroy(vehicleType);
            vehicleType = null;
        }

        return Task.CompletedTask;
    }
}

internal static class FerrariSF90SpiderDiagnostics
{
    internal static bool NpcTrafficDebugEnabled { get; set; } = false;

    internal static bool TrafficEnabled => DebugEnabled && NpcTrafficDebugEnabled;

    internal static void TrafficInfo(string message)
    {
        if (DebugEnabled && NpcTrafficDebugEnabled)
            UnityEngine.Debug.Log(message);
    }

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

    internal static bool NpcLightingDebugEnabled { get; set; } = false;
    internal static bool NpcDriverDebugEnabled { get; set; } = false;
    internal static bool PaintDebugEnabled { get; set; } = false;
    internal static bool WarehouseTransitionDebugEnabled { get; set; } = false;
    internal static bool TelemetryEnabled { get; set; } = false;

    internal static void Info(ModContext? context, string message)
    {
        if (DebugEnabled)
            context?.Logger.Info(message);
    }

    internal static void TelemetryInfo(ModContext? context, string message)
    {
        if (TelemetryEnabled)
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
}

internal static class FerrariSF90SpiderLuxuryDealerStock
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
