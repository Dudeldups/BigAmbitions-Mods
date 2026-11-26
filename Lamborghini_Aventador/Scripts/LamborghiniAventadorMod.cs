#nullable enable
using System;
using System.Threading.Tasks;
using BAModAPI;
using BAModAPI.Services;
using UnityEngine;
using Vehicles.VehicleTypes;

[assembly: RegisterModClass(typeof(LamborghiniAventadorMod))]
[assembly: RegisterModClass(typeof(LamborghiniAventadorMainMenuPrefabRegistration))]
[assembly: RegisterModClass(typeof(LamborghiniAventadorCityPrefabRegistration))]

internal static class LamborghiniAventadorDiagnostics
{
    internal static bool DebugEnabled { get; set; } = false;
    internal static bool PrefabDebugEnabled { get; set; } = false;
    internal static bool DriverDebugEnabled { get; set; } = false;
    internal static bool AutoParkingDebugEnabled { get; set; } = false;
    internal static bool ExitSafetyDebugEnabled { get; set; } = false;
    internal static bool GeometryDebugEnabled { get; set; } = false;
    internal static bool CaliperDebugEnabled { get; set; } = false;
    internal static bool PowertrainDebugEnabled { get; set; } = false;
    internal static bool TeleportDebugEnabled { get; set; } = false;

    internal static void Info(ModContext? context, string message)
    {
        if (DebugEnabled) context?.Logger.Info(message);
    }

    internal static void PrefabInfo(ModContext? context, string message)
    {
        if (DebugEnabled && PrefabDebugEnabled) context?.Logger.Info(message);
    }

    internal static void DriverInfo(ModContext? context, string message)
    {
        if (DebugEnabled && DriverDebugEnabled) context?.Logger.Info(message);
    }

    internal static void AutoParkingInfo(ModContext? context, string message)
    {
        if (DebugEnabled && AutoParkingDebugEnabled) context?.Logger.Info(message);
    }

    internal static void GeometryInfo(ModContext? context, string message)
    {
        if (DebugEnabled && GeometryDebugEnabled) context?.Logger.Info(message);
    }

    internal static void CaliperInfo(ModContext? context, string message)
    {
        if (DebugEnabled && CaliperDebugEnabled) context?.Logger.Info(message);
    }

    internal static void PowertrainInfo(ModContext? context, string message)
    {
        if (DebugEnabled && PowertrainDebugEnabled) context?.Logger.Info(message);
    }

    internal static void TeleportInfo(ModContext? context, string message)
    {
        if (DebugEnabled && TeleportDebugEnabled) context?.Logger.Info(message);
    }

    internal static void ExitSafetyInfo(ModContext? context, string message)
    {
        if (DebugEnabled && ExitSafetyDebugEnabled) context?.Logger.Info(message);
    }
}

[ModEntryOnInitializationLoad]
public sealed class LamborghiniAventadorMod : IModBigAmbitions
{
    internal const string VehicleTypeName = "lamborghiniaventador-vehicle:vehicletype_lamborghiniaventador";
    internal const string BundleKey = "AssetBundles/lamborghiniaventador.unity3d";
    internal const string VehicleAssetPath = "Assets/Mods/Lamborghini_Aventador/LamborghiniAventador.asset";
    internal const string VehiclePrefabPath = "Assets/Mods/Lamborghini_Aventador/LamborghiniAventador.prefab";
    internal const string GleyLampMaterialPath = "Assets/Mods/Lamborghini_Aventador/Shaders/LamborghiniAventadorGleyLamp.mat";

    private VehicleType? registeredType;
    private LamborghiniAventadorVehicleRuntime? runtime;
    private LamborghiniAventadorAutoParkingGuard? autoParkingGuard;
    private LamborghiniAventadorSpawnPaintPatch? spawnPaintPatch;

    public string[] RelativeAssetBundlePaths => new[] { BundleKey };

    public Task OnLoadAsync(ModContext context)
    {
        var bundle = AssetService.GetBundle(context.ModId, BundleKey);
        if (bundle == null)
        {
            context.Logger.Warn("LamborghiniAventador: missing bundle; vehicle registration skipped.");
            return Task.CompletedTask;
        }

        var type = bundle.LoadAsset<VehicleType>(VehicleAssetPath);
        var prefab = bundle.LoadAsset<GameObject>(VehiclePrefabPath);
        if (type == null || prefab == null)
        {
            context.Logger.Warn("LamborghiniAventador: missing native VehicleType or player prefab; registration skipped.");
            return Task.CompletedTask;
        }

        if (!string.Equals(type.vehicleTypeName, VehicleTypeName, StringComparison.Ordinal))
        {
            context.Logger.Warn("LamborghiniAventador: unexpected vehicle identity; registration skipped.");
            return Task.CompletedTask;
        }

        var controller = prefab.GetComponent<VehicleController>();
        var nwh = prefab.GetComponent<NWH.VehiclePhysics2.VehicleController>();
        if (controller == null || nwh == null || nwh.stateSettings == null)
        {
            context.Logger.Warn("LamborghiniAventador: missing native controllers or NWH StateSettings; registration skipped.");
            return Task.CompletedTask;
        }

        if (!LamborghiniAventadorPlayerPrefabRegistration.Bind(context, prefab, "initialization-load"))
            return Task.CompletedTask;

        ModdingAPI.RegisterModVehicleType(type);
        registeredType = type;
        spawnPaintPatch = LamborghiniAventadorSpawnPaintPatch.Install(context);
        runtime = LamborghiniAventadorVehicleRuntime.Initialize(context, type.vehicleTypeName);
        autoParkingGuard = LamborghiniAventadorAutoParkingGuard.Install(context, type.vehicleTypeName);
        LamborghiniAventadorDiagnostics.Info(context, "LamborghiniAventador: native vehicle type registered.");
        return Task.CompletedTask;
    }

    public Task OnUnloadAsync()
    {
        spawnPaintPatch?.Shutdown();
        spawnPaintPatch = null;
        autoParkingGuard?.Shutdown();
        autoParkingGuard = null;
        runtime?.Shutdown();
        runtime = null;
        if (registeredType != null)
        {
            ModdingAPI.UnregisterModVehicleType(registeredType.vehicleTypeName);
            registeredType = null;
        }
        LamborghiniAventadorPlayerPrefabRegistration.Release();
        return Task.CompletedTask;
    }
}
