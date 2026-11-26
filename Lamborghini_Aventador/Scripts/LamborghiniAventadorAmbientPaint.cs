#nullable enable
using System;
using BAModAPI;
using Buildings.BuildingTypes.Special.PrivateDriverService;
using Data.VehicleColors;
using Helpers;
using UnityEngine;

/// <summary>
/// Chooses one game-weighted color for each Gley ambient clone and applies it
/// only to that clone's exact RED_PAINT material slot.
/// </summary>
internal sealed class LamborghiniAventadorAmbientPaint : MonoBehaviour
{
    private static ModContext? defaultContext;

    private ModContext? context;
    private bool hasChosenColor;

    /// <summary>Attach to the inactive Gley traffic prefab; instances choose their color on activation.</summary>
    internal static LamborghiniAventadorAmbientPaint Initialize(GameObject gleyPrefab, ModContext? modContext)
    {
        if (gleyPrefab == null)
            throw new ArgumentNullException(nameof(gleyPrefab));

        defaultContext = modContext;
        var appearance = gleyPrefab.GetComponent<LamborghiniAventadorAmbientPaint>() ??
                         gleyPrefab.AddComponent<LamborghiniAventadorAmbientPaint>();
        appearance.context = modContext;
        return appearance;
    }

    private void OnEnable() => EnsureColorChosen();

    private void EnsureColorChosen()
    {
        if (hasChosenColor || GetComponent<PrivateDriverVehicle>() != null)
            return;

        var vehicle = GetComponent<VehicleController>();

        VehicleColor? selected;
        try
        {
            // VehicleHelper uses the game's registered color weights. Keep the
            // result on this clone for its lifetime; enable/disable does not reroll.
            selected = VehicleHelper.GetRandomVehicleColor();
        }
        catch (Exception exception)
        {
            (context ?? defaultContext)?.Logger.Warn(
                $"LamborghiniAventador ambient paint clone={GetInstanceID()}: " +
                $"weighted color selection failed ({exception.GetType().Name}: {exception.Message}).");
            return;
        }

        if (selected == null)
        {
            (context ?? defaultContext)?.Logger.Warn(
                $"LamborghiniAventador ambient paint clone={GetInstanceID()}: " +
                "game color weighting returned no color; ambient body keeps its source paint.");
            return;
        }

        var paint = GetComponent<LamborghiniAventadorPaintController>() ??
                    gameObject.AddComponent<LamborghiniAventadorPaintController>();
        if (vehicle != null)
            paint.InitializeForAmbientTraffic(vehicle, selected, context ?? defaultContext);
        else
        {
            // Gley's native AnselmoAF90 traffic template has no player-side
            // Vehicles.VehicleTypes.VehicleController. Paint its authored body
            // slot through the same property-block adapter without attaching a
            // fake player controller or changing saved vehicle state.
            paint.InitializeForAmbientTraffic(selected, context ?? defaultContext);
            if (LamborghiniAventadorDiagnostics.DebugEnabled &&
                LamborghiniAventadorPaintController.AppearanceDebugEnabled)
                (context ?? defaultContext)?.Logger.Info(
                    $"LamborghiniAventador ambient paint clone={GetInstanceID()} " +
                    "used native Gley color application without a player vehicle controller.");
        }
        hasChosenColor = paint.HasAppliedColor;

        if (!hasChosenColor)
        {
            (context ?? defaultContext)?.Logger.Warn(
                $"LamborghiniAventador ambient paint clone={GetInstanceID()}: " +
                $"selected weighted color '{selected.name}' but exact body paint was not applied.");
            return;
        }

        if (LamborghiniAventadorDiagnostics.DebugEnabled &&
            LamborghiniAventadorPaintController.AppearanceDebugEnabled)
        {
            (context ?? defaultContext)?.Logger.Info(
                $"LamborghiniAventador ambient paint clone={GetInstanceID()} " +
                $"color='{selected.name}' selectedOnce=true.");
        }
    }
}
