#nullable enable
using System;
using BAModAPI;
using Helpers;
using UnityEngine;

/// <summary>
/// Applies appearance after the native spawn has assigned its VehicleInstance.
/// </summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
[DefaultExecutionOrder(5000)]
public sealed class LamborghiniAventadorSpawnAppearance : MonoBehaviour
{
    private bool applied;

    private void Start() => ApplySpawnAppearance();

    internal void ApplySpawnAppearance()
    {
        if (applied)
            return;

        var vehicle = GetComponent<VehicleController>();
        if (vehicle?.vehicleInstance == null || !string.Equals(
                vehicle.vehicleInstance.vehicleTypeName,
                LamborghiniAventadorMod.VehicleTypeName,
                StringComparison.Ordinal))
            return;

        var context = LamborghiniAventadorVehicleRuntime.SpawnContext;
        if (context == null)
            return;

        var glass = GetComponent<LamborghiniAventadorGlassController>() ??
                    gameObject.AddComponent<LamborghiniAventadorGlassController>();
        glass.Initialize(context);

        var paint = LamborghiniAventadorPaintController.Attach(
            vehicle, context, preferSavedColor: true);
        applied = paint.HasResolvedSavedColor;
        LamborghiniAventadorDiagnostics.Info(context,
            $"LamborghiniAventador spawn appearance vehicle={vehicle.GetInstanceID()} " +
            $"savedColor='{vehicle.vehicleInstance.vehicleColorName}' applied={paint.HasAppliedColor} " +
            $"savedColorResolved={paint.HasResolvedSavedColor}.");
        if (!paint.HasResolvedSavedColor)
            context.Logger.Warn(
                $"LamborghiniAventador spawn appearance vehicle={vehicle.GetInstanceID()}: " +
                $"saved color '{vehicle.vehicleInstance.vehicleColorName}' is not available yet; " +
                "the next vehicle lifecycle event will retry the authoritative color.");
    }
}
