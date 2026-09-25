#nullable enable
using UnityEngine;

public sealed class BMWM4G82SpawnConfigurator : MonoBehaviour
{
    private void Start()
    {
        var vehicle = GetComponent<VehicleController>();
        if (vehicle == null)
        {
            Debug.LogWarning("BMWM4G82: spawn configurator could not find VehicleController.");
            return;
        }

        if (!BMWM4G82Runtime.ConfigureSpawnedVehicle(vehicle))
        {
            Debug.LogWarning(
                $"BMWM4G82: spawn-time configuration unavailable " +
                $"instance={vehicle.GetInstanceID()}, " +
                $"vehicleType='{vehicle.vehicleInstance?.vehicleTypeName ?? "unset"}'.");
        }
    }
}
