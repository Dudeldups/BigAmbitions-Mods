#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BAModAPI;
using Helpers;
using PlayerActivity;
using UnityEngine;

[AddComponentMenu("")]
[DefaultExecutionOrder(-1000)]
public sealed class DodgeRam1500TRXNativeActivities : MonoBehaviour
{
    private static readonly FieldInfo? Config = FindConfig();
    private static FieldInfo? FindConfig()
    {
        for (var type = typeof(SleepEnvironment); type != null; type = type.BaseType)
        {
            var field = type.GetField("config", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }
    private static CarController? template;
    private static ModContext? context;
    internal static void SetContext(ModContext value) => context = value;
    internal static void Shutdown() { template = null; context = null; }
    private void Awake() => EnsureSleep(GetComponent<VehicleController>(), context);

    // This component is already on the owned prefab. Spawn assigns its saved
    // instance after Awake; one frame after Start also lets native CarFeatures
    // finish. This is a single spawn handoff, never a discovery poll.
    internal static event Action<VehicleController>? PlayerInstanceReady;
    private IEnumerator Start()
    {
        yield return null;
        var vehicle = GetComponent<VehicleController>();
        if (vehicle?.vehicleInstance != null) PlayerInstanceReady?.Invoke(vehicle);
    }

    internal static bool EnsureSleep(VehicleController? vehicle, ModContext? modContext)
    {
        if (vehicle == null) return false;
        try
        {
            if (Config == null) throw new InvalidOperationException("Native sleep configuration field unavailable.");
            object environment = vehicle.sleepEnvironment;
            if (Config.GetValue(environment) is UnityEngine.Object existing && existing != null) return true;
            if (template == null)
                template = PrefabHelper.LoadPrefabAssetByName("Vehicles/PlayerVehicles/HonzaMimic")?.GetComponent<CarController>();
            var config = template == null ? null : Config.GetValue(template.sleepEnvironment) as UnityEngine.Object;
            if (config == null || template!.sleepEnvironment.SleepEnvironmentType != SleepEnvironmentType.Car)
                throw new InvalidOperationException("Native Car sleep configuration unavailable.");
            Config.SetValue(environment, config);
            vehicle.sleepEnvironment = (SleepEnvironment)environment;
            DodgeRam1500TRXDiagnostics.SleepInfo(modContext, $"DodgeRam1500TRX sleep vehicle={vehicle.GetInstanceID()}: assigned native Car configuration before saved-instance checks.");
            return true;
        }
        catch (Exception exception)
        {
            if (modContext != null) modContext.Logger.Warn($"DodgeRam1500TRX sleep vehicle={vehicle.GetInstanceID()}: {exception.GetBaseException().Message}");
            else Debug.LogWarning($"DodgeRam1500TRX sleep: {exception.GetBaseException().Message}");
            return false;
        }
    }
}
