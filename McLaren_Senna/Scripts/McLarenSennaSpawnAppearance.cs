#nullable enable
using System;
using UnityEngine;

// Native spawn assigns the ordered VehicleInstance and remaps shaders after
// Awake. Start is the first render-safe point with those authoritative inputs.
[AddComponentMenu("")]
[DisallowMultipleComponent]
public sealed class McLarenSennaSpawnAppearance : MonoBehaviour
{
    private void Start() => ApplySpawnAppearance();

    internal void ApplySpawnAppearance()
    {
        var vehicle = GetComponent<VehicleController>();
        if (vehicle?.vehicleInstance == null || !string.Equals(
                vehicle.vehicleInstance.vehicleTypeName,
                McLarenSennaMod.VehicleTypeName, StringComparison.Ordinal))
            return;

        var context = McLarenSennaRuntime.SpawnContext;
        var calipers = GetComponent<McLarenSennaCaliperController>();
        if (calipers == null) calipers = gameObject.AddComponent<McLarenSennaCaliperController>();
        calipers.Initialize(vehicle, context);
        var materials = GetComponent<McLarenSennaMaterialController>();
        if (materials == null) materials = gameObject.AddComponent<McLarenSennaMaterialController>();
        materials.Initialize(context);
        var glass = GetComponent<McLarenSennaGlassController>();
        if (glass == null) glass = gameObject.AddComponent<McLarenSennaGlassController>();
        glass.Initialize(context);
        var paint = GetComponent<McLarenSennaPaintController>();
        if (paint == null) paint = gameObject.AddComponent<McLarenSennaPaintController>();
        paint.Initialize(vehicle, context);
        McLarenSennaDiagnostics.PaintInfo(context,
            $"McLarenSenna paint: spawn instance={vehicle.GetInstanceID()}, " +
            $"savedColor='{vehicle.vehicleInstance.vehicleColorName}', " +
            $"controlled={vehicle.controlledByPlayer}, applied={paint.HasAppliedColor}.");
    }
}
