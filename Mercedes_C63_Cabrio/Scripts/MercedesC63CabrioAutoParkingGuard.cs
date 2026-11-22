#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;

internal sealed class MercedesC63CabrioAutoParkingGuard : MonoBehaviour
{
    private const float SpotDistance = 0.75f;
    private static readonly FieldInfo? LastAutoParkPositionField =
        typeof(VehicleParkingHelper).GetField("_lastAutoParkPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? HudParkingStateField =
        typeof(UI.ItemPanel.VehicleInfoPanel).GetField("currentParkingState",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private ModContext? context;
    private string vehicleTypeName = string.Empty;

    internal static MercedesC63CabrioAutoParkingGuard Install(
        GameObject owner, ModContext context, string vehicleTypeName)
    {
        var guard = owner.GetComponent<MercedesC63CabrioAutoParkingGuard>() ??
                    owner.AddComponent<MercedesC63CabrioAutoParkingGuard>();
        guard.context = context;
        guard.vehicleTypeName = vehicleTypeName;
        if (LastAutoParkPositionField == null || HudParkingStateField == null)
            MercedesC63CabrioDiagnostics.AutoParkingWarn(context,
                "MercedesC63Cabrio auto-parking: required game parking field is unavailable.");
        var marker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "Mercedes_C63_Cabrio", "Config", "auto-parking-diagnostics.enabled");
        if (File.Exists(marker))
        {
            MercedesC63CabrioDiagnostics.DebugEnabled = true;
            MercedesC63CabrioDiagnostics.AutoParkingDebugEnabled = true;
            context.Logger.Info("MercedesC63Cabrio auto-parking diagnostics enabled.");
        }
        GlobalEvents.onExitVehicle -= guard.HandleVehicleExited;
        GlobalEvents.onExitVehicle += guard.HandleVehicleExited;
        return guard;
    }

    internal void Shutdown()
    {
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        StopAllCoroutines();
        Destroy(this);
    }

    private void OnDestroy()
    {
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
    }

    private bool IsTarget(VehicleController? vehicle) =>
        vehicle != null &&
        (string.Equals(vehicle.vehicleInstance?.vehicleTypeName,
             vehicleTypeName, StringComparison.Ordinal) ||
         string.Equals(vehicle.vehicleType?.vehicleTypeName,
             vehicleTypeName, StringComparison.Ordinal));

    private bool DiagnosticsEnabled =>
        MercedesC63CabrioDiagnostics.DebugEnabled &&
        MercedesC63CabrioDiagnostics.AutoParkingDebugEnabled;

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (IsTarget(vehicle))
            StartCoroutine(ReconcileAfterExit(vehicle));
    }

    private IEnumerator ReconcileAfterExit(VehicleController vehicle)
    {
        yield return new WaitForEndOfFrame();
        if (vehicle == null || vehicle is not CarController car)
            yield break;

        Physics.SyncTransforms();
        var helper = vehicle.GetComponentInChildren<VehicleParkingHelper>(true);
        var spot = helper != null &&
            LastAutoParkPositionField?.GetValue(helper) is Vector3 position
            ? position : Vector3.positiveInfinity;
        var distance = Vector3.Distance(vehicle.transform.position, spot);
        if (distance > SpotDistance)
            yield break;

        var legalWheels = GetParkingWheelState(car, out var neighbourhood,
            out var wheelChecks);
        var savedBefore = vehicle.vehicleInstance?.parkingState.ToString() ?? "<none>";
        if (legalWheels && vehicle.vehicleInstance != null &&
            vehicle.vehicleInstance.parkingState != ParkingState.Legal)
        {
            vehicle.vehicleInstance.parkingState = ParkingState.Legal;
            vehicle.vehicleInstance.parkingNeighbourhood = neighbourhood;
        }
        if (!legalWheels)
            MercedesC63CabrioDiagnostics.AutoParkingWarn(context,
                $"MercedesC63Cabrio auto-parking vehicle={vehicle.GetInstanceID()} " +
                $"is not fully inside a legal parking zone after exit; " +
                $"wheels=[{wheelChecks}].");
        if (DiagnosticsEnabled)
            MercedesC63CabrioDiagnostics.AutoParkingInfo(context,
                $"MercedesC63Cabrio auto-parking exit vehicle={vehicle.GetInstanceID()} " +
                $"distanceToSpot={distance:0.00}m legalWheels={legalWheels} " +
                $"savedBefore={savedBefore} " +
                $"savedAfter={vehicle.vehicleInstance?.parkingState.ToString() ?? "<none>"} " +
                $"wheels=[{wheelChecks}].");
    }

    private static bool GetParkingWheelState(CarController car,
        out string neighbourhood, out string wheelChecks)
    {
        neighbourhood = string.Empty;
        var checks = new List<string>();
        var wheels = car.vehicleController?.powertrain?.wheels;
        var legal = wheels != null && wheels.Count == 4;
        if (wheels != null)
            for (var index = 0; index < wheels.Count; index++)
            {
                var wheel = wheels[index]?.wheelUAPI;
                if (wheel == null)
                {
                    legal = false;
                    checks.Add($"{index}:missing-wheel");
                    continue;
                }
                var transform = wheel.transform;
                var hits = Physics.OverlapBox(
                    transform.position - transform.up * 0.1f,
                    new Vector3(0.1f, wheel.Radius * 2f, 0.1f),
                    transform.rotation, LayerHelper.parkingAreaLayerMask);
                var lane = hits.Length > 0
                    ? hits[0].GetComponentInParent<ParkingLaneGenerator>() : null;
                if (lane == null || lane.isHandicapParking ||
                    (neighbourhood.Length > 0 && neighbourhood != lane.neighbourhood))
                    legal = false;
                if (lane != null)
                    neighbourhood = lane.neighbourhood;
                checks.Add($"{index}:hits={hits.Length},lane={lane?.name ?? "<none>"}," +
                           $"handicap={lane?.isHandicapParking.ToString() ?? "<none>"}");
            }
        wheelChecks = string.Join("; ", checks);
        return legal;
    }
}
