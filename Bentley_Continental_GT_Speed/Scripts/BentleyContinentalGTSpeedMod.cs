#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using Helpers;
using NWH.VehiclePhysics2.Damage;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(BentleyContinentalGTSpeedMod))]
[assembly: RegisterModClass(typeof(BentleyContinentalGTSpeedMainMenuPrefabRegistration))]
[assembly: RegisterModClass(typeof(BentleyContinentalGTSpeedCityPrefabRegistration))]

internal static class BentleyContinentalGTSpeedDiagnostics
{
    internal static bool DebugEnabled { get; set; } = false;
    internal static bool AutoParkingDebugEnabled { get; set; } = false;
    internal static bool WashDebugEnabled { get; set; } = false;
    internal static bool AppearanceDebugEnabled { get; set; } = false;
    internal static bool LightingDebugEnabled { get; set; } = false;
    internal static bool DrivetrainDebugEnabled { get; set; } = false;
    internal static bool ExitSafetyDebugEnabled { get; set; } = false;
    internal static bool DriverDebugEnabled { get; set; } = false;

    internal static void Info(ModContext? context, string message)
    {
        if (DebugEnabled) context?.Logger.Info(message);
    }

    internal static void PrefabInfo(ModContext? context, string message)
    {
        Info(context, message);
    }

    internal static void AutoParkingInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AutoParkingDebugEnabled)
            context?.Logger.Info("BentleyContinentalGTSpeed auto-parking: " + message);
    }

    internal static void AutoParkingWarn(ModContext? context, string message)
    {
        context?.Logger.Warn("BentleyContinentalGTSpeed auto-parking: " + message);
    }

    internal static void DrivetrainInfo(ModContext? context, string message)
    {
        if (DebugEnabled && DrivetrainDebugEnabled)
            context?.Logger.Info("BentleyContinentalGTSpeed drivetrain: " + message);
    }

    internal static void ExitSafetyInfo(ModContext? context, string message)
    {
        if (DebugEnabled && ExitSafetyDebugEnabled)
            context?.Logger.Info("BentleyContinentalGTSpeed exit-safety: " + message);
    }

    internal static void EnableExitSafetyDiagnostics(ModContext context)
    {
        var config = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "Bentley_Continental_GT_Speed", "Config");
        BentleyContinentalGTSpeedPrivateDriverSupport.DebugEnabled =
            File.Exists(Path.Combine(config, "private-driver-diagnostics.enabled"));
        LightingDebugEnabled = File.Exists(Path.Combine(config, "lighting-diagnostics.enabled"));
        AppearanceDebugEnabled = File.Exists(Path.Combine(config, "appearance-diagnostics.enabled"));
        DrivetrainDebugEnabled = File.Exists(Path.Combine(config, "drivetrain-diagnostics.enabled"));
        WashDebugEnabled = File.Exists(Path.Combine(config, "wash-diagnostics.enabled"));
        BentleyContinentalGTSpeedDamageController.DamageDebugEnabled =
            File.Exists(Path.Combine(config, "damage-diagnostics.enabled"));
        if (BentleyContinentalGTSpeedPrivateDriverSupport.DebugEnabled ||
            LightingDebugEnabled || AppearanceDebugEnabled || DrivetrainDebugEnabled || WashDebugEnabled ||
            BentleyContinentalGTSpeedDamageController.DamageDebugEnabled)
        {
            DebugEnabled = true;
            context.Logger.Info("BentleyContinentalGTSpeed local feature diagnostics enabled.");
        }
        ExitSafetyDebugEnabled = false;
        var marker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "Bentley_Continental_GT_Speed", "Config", "exit-safety-diagnostics.enabled");
        if (!File.Exists(marker))
            return;

        DebugEnabled = true;
        ExitSafetyDebugEnabled = true;
        context.Logger.Info("BentleyContinentalGTSpeed exit-safety diagnostics enabled.");
    }

    internal static void DriverInfo(ModContext? context, string message)
    {
        if (DebugEnabled && DriverDebugEnabled)
            context?.Logger.Info("BentleyContinentalGTSpeed driver: " + message);
    }

    internal static void WashInfo(ModContext? context, string message)
    {
        if (DebugEnabled && WashDebugEnabled)
            context?.Logger.Info("BentleyContinentalGTSpeed wash: " + message);
    }
}

[ModEntryOnInitializationLoad]
public sealed class BentleyContinentalGTSpeedMod : IModBigAmbitions
{
    internal const string VehicleTypeName = "bentleycontinentalgtspeed-vehicle:vehicletype_bentleycontinentalgtspeed";
    internal const string BundleKey = "AssetBundles/bentleycontinentalgtspeed.unity3d";
    internal const string VehicleAssetPath = "Assets/Mods/Bentley_Continental_GT_Speed/BentleyContinentalGTSpeed.asset";
    internal const string VehiclePrefabPath = "Assets/Mods/Bentley_Continental_GT_Speed/BentleyContinentalGTSpeed.prefab";

    private const float DealerPrice = 350000f;
    private const float MaxFuel = 80f;
    private const int MaxCargoCapacity = 2;
    private const int MaxSpeed = 335;
    private const float EnginePower = 575f;
    private const float BrakeForce = 3000f;
    private const int TurnRadius = 36;
    private const float DamageIntensity = 0.80f;

    private VehicleType? registeredType;
    private BentleyContinentalGTSpeedAutoParkingGuard? autoParkingGuard;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        if (registeredType != null)
        {
            BentleyContinentalGTSpeedDiagnostics.Info(
                context,
                "BentleyContinentalGTSpeed: duplicate initialization ignored; vehicle is already registered.");
            return Task.CompletedTask;
        }

        BentleyContinentalGTSpeedDiagnostics.EnableExitSafetyDiagnostics(context);

        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn("BentleyContinentalGTSpeed: missing bundle; vehicle registration skipped.");
            return Task.CompletedTask;
        }

        var type = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (type == null || prefab == null)
        {
            context.Logger.Warn("BentleyContinentalGTSpeed: missing native VehicleType or player prefab; registration skipped.");
            return Task.CompletedTask;
        }

        if (!string.Equals(type.vehicleTypeName, VehicleTypeName, StringComparison.Ordinal))
        {
            context.Logger.Warn("BentleyContinentalGTSpeed: unexpected vehicle identity; registration skipped.");
            return Task.CompletedTask;
        }

        ApplyRuntimeVehicleTypeDefaults(type);

        var controller = prefab.GetComponent<VehicleController>();
        var nwh = prefab.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        var damageHandler = prefab.GetComponentInChildren<DamageHandler>(true);
        if (controller == null || nwh == null || nwh.stateSettings == null || damageHandler == null)
        {
            context.Logger.Warn(
                "BentleyContinentalGTSpeed: missing native controllers, NWH StateSettings, or DamageHandler; registration skipped.");
            return Task.CompletedTask;
        }
        damageHandler.damageIntensity = DamageIntensity;
        BentleyContinentalGTSpeedDiagnostics.Info(
            context,
            $"BentleyContinentalGTSpeed: native damage intensity configured to {damageHandler.damageIntensity:0.##}.");

        BentleyContinentalGTSpeedPrivateDriverSupport.SetContext(context);
        if (!BentleyContinentalGTSpeedPrivateDriverSupport.ConfigurePlayerPrefab(prefab, "mod-load"))
        {
            context.Logger.Warn(
                "BentleyContinentalGTSpeed: vehicle registration skipped because core visual, paint, " +
                "or collider references could not be configured.");
            return Task.CompletedTask;
        }

        BentleyContinentalGTSpeedWashBindings.Configure(controller, context, "mod-load-prefab");

        if (!BentleyContinentalGTSpeedPlayerPrefabRegistration.Bind(context, prefab, "initialization-load"))
            return Task.CompletedTask;

        if (!EnsureVehicleTypeRegistered(context, type, "initialization-load"))
            return Task.CompletedTask;

        registeredType = type;
        BentleyContinentalGTSpeedDiagnostics.Info(context, "BentleyContinentalGTSpeed: native vehicle type registered.");
        var initializedRuntime = BentleyContinentalGTSpeedRuntime.Initialize(
            context, type.vehicleTypeName, prefab);
        runtime = initializedRuntime;
        autoParkingGuard = BentleyContinentalGTSpeedAutoParkingGuard.Install(
            initializedRuntime.gameObject, context, type.vehicleTypeName);
        return Task.CompletedTask;
    }

    private BentleyContinentalGTSpeedRuntime? runtime;

    public Task OnUnloadAsync()
    {
        autoParkingGuard?.Shutdown();
        autoParkingGuard = null;
        runtime?.Shutdown();
        runtime = null;
        if (registeredType != null)
        {
            BentleyContinentalGTSpeedLuxuryDealerStock.RemoveVehicle(registeredType.vehicleTypeName);
            ModdingAPI.UnregisterModVehicleType(registeredType.vehicleTypeName);
            registeredType = null;
        }
        BentleyContinentalGTSpeedPrivateDriverSupport.RemoveVehicle(VehicleTypeName);
        BentleyContinentalGTSpeedPlayerPrefabRegistration.Release();
        return Task.CompletedTask;
    }

    internal static bool EnsureVehicleTypeRegistered(ModContext context, string source)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn(
                $"BentleyContinentalGTSpeed: vehicle bundle '{BundleKey}' unavailable during lifecycle registration source='{source}'.");
            return false;
        }

        var type = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        return EnsureVehicleTypeRegistered(context, type, source);
    }

    private static bool EnsureVehicleTypeRegistered(
        ModContext context, VehicleType? type, string source)
    {
        if (type == null)
        {
            context.Logger.Warn(
                $"BentleyContinentalGTSpeed: VehicleType '{VehicleAssetPath}' unavailable during lifecycle registration source='{source}'.");
            return false;
        }
        if (!string.Equals(type.vehicleTypeName, VehicleTypeName, StringComparison.Ordinal))
        {
            context.Logger.Warn(
                $"BentleyContinentalGTSpeed: unexpected VehicleType identity during lifecycle registration source='{source}'.");
            return false;
        }

        ApplyRuntimeVehicleTypeDefaults(type);
        var wasRegistered = VehicleTypeHelper.GetVehicleType(type.vehicleTypeName) != null &&
                            VehicleTypeHelper.IsModVehicleType(type.vehicleTypeName);
        if (!wasRegistered)
            ModdingAPI.RegisterModVehicleType(type);

        var ready = VehicleTypeHelper.GetVehicleType(type.vehicleTypeName) != null &&
                    VehicleTypeHelper.IsModVehicleType(type.vehicleTypeName);
        if (!ready)
        {
            context.Logger.Warn(
                $"BentleyContinentalGTSpeed: VehicleType registration failed source='{source}'.");
        }
        else if (!wasRegistered)
        {
            BentleyContinentalGTSpeedDiagnostics.Info(
                context,
                $"BentleyContinentalGTSpeed: VehicleType registered source='{source}'.");
        }

        return ready;
    }

    private static void ApplyRuntimeVehicleTypeDefaults(VehicleType type)
    {
        // The installed AssetBundle can predate the editor authoring setup.
        // Keep the runtime catalogue data usable until that bundle is rebuilt.
        type.price = DealerPrice;
        type.maxFuel = MaxFuel;
        type.maxCargoCapacity = MaxCargoCapacity;
        type.maxSpeed = MaxSpeed;
        type.enginePower = EnginePower;
        type.brakeForce = BrakeForce;
        type.turnRadius = TurnRadius;
        type.damageIntensity = DamageIntensity;
        type.fitsHandTruck = false;
        type.fitsFlatbed = false;
        type.autoParkSupported = true;
        type.hasRadio = true;
        type.isLuxuryCar = true;
        type.destinationsThatCanDeliver = 2;
        type.enclosed = true;
    }
}
