#nullable enable
using System;
using System.Reflection;
using BAModAPI;
using Helpers;
using PlayerActivity;
using UnityEngine;

internal static class KiaCeedSportswagonSleepBindings
{
    private static readonly FieldInfo? ConfigField = FindConfigField();
    private static SleepEnvironmentConfig? nativeCarConfig;
    private static bool failureReported;

    internal static bool Configure(VehicleController? vehicle, ModContext? context)
    {
        if (vehicle == null) return false;
        try
        {
            if (ConfigField == null)
                throw new InvalidOperationException("Native sleep config field is unavailable.");
            if (vehicle.sleepEnvironment != null &&
                ConfigField.GetValue(vehicle.sleepEnvironment) is SleepEnvironmentConfig current &&
                current != null && current.sleepEnvironmentType == SleepEnvironmentType.Car)
                return true;
            if (nativeCarConfig == null)
            {
                var donor = PrefabHelper.LoadPrefabAssetByName("Vehicles/PlayerVehicles/HonzaMimic");
                var donorVehicle = donor?.GetComponent<VehicleController>();
                nativeCarConfig = donorVehicle?.sleepEnvironment == null ? null :
                    ConfigField.GetValue(donorVehicle.sleepEnvironment) as SleepEnvironmentConfig;
            }
            if (nativeCarConfig == null || nativeCarConfig.sleepEnvironmentType != SleepEnvironmentType.Car)
                throw new InvalidOperationException("Native HonzaMimic Car sleep configuration is unavailable.");
            var environment = new SleepEnvironment();
            ConfigField.SetValue(environment, nativeCarConfig);
            vehicle.sleepEnvironment = environment;
            KiaCeedSportswagonDiagnostics.Info(context,
                $"KiaCeedSportswagon sleep vehicle={vehicle.GetInstanceID()}: native Car config bound; saved instance={vehicle.vehicleInstance != null}.");
            return true;
        }
        catch (Exception exception)
        {
            if (!failureReported && context != null)
            {
                failureReported = true;
                context.Logger.Warn($"KiaCeedSportswagon sleep binding failed: {exception.GetType().Name}: {exception.Message}");
            }
            return false;
        }
    }

    private static FieldInfo? FindConfigField()
    {
        for (var type = typeof(SleepEnvironment); type != null; type = type.BaseType)
        {
            var field = type.GetField("config", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }

    internal static void Release()
    {
        nativeCarConfig = null;
        failureReported = false;
    }
}
