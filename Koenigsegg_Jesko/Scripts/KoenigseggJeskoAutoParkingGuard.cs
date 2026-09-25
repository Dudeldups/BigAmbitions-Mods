#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;

internal sealed class KoenigseggJeskoAutoParkingGuard : MonoBehaviour
{
    private const float SpotDistance = 0.75f;
    private const float RefreshDuration = 3f;
    private const float RefreshInterval = 0.05f;
    private static readonly FieldInfo? LastAutoParkPositionField =
        typeof(VehicleParkingHelper).GetField("_lastAutoParkPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? HudParkingStateField =
        typeof(UI.ItemPanel.VehicleInfoPanel).GetField("currentParkingState",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private VehicleController? selectedVehicle;
    private VehicleParkingHelper? parkingHelper;
    private Vector3 monitoredSpot = Vector3.positiveInfinity;
    private float refreshUntil;
    private float nextRefresh;
    private int refreshAttempts;

    internal static KoenigseggJeskoAutoParkingGuard Install(
        GameObject owner, ModContext context, string vehicleTypeName)
    {
        var guard = owner.GetComponent<KoenigseggJeskoAutoParkingGuard>() ??
                    owner.AddComponent<KoenigseggJeskoAutoParkingGuard>();
        guard.context = context;
        guard.vehicleTypeName = vehicleTypeName;
        if (LastAutoParkPositionField == null || HudParkingStateField == null)
            KoenigseggJeskoDiagnostics.AutoParkingWarn(context,
                "KoenigseggJesko auto-parking: required game parking field is unavailable.");
        var marker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "Koenigsegg_Jesko", "Config", "auto-parking-diagnostics.enabled");
        if (File.Exists(marker))
        {
            KoenigseggJeskoDiagnostics.DebugEnabled = true;
            KoenigseggJeskoDiagnostics.AutoParkingDebugEnabled = true;
            context.Logger.Info("KoenigseggJesko auto-parking diagnostics enabled.");
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
        KoenigseggJeskoDiagnostics.DebugEnabled &&
        KoenigseggJeskoDiagnostics.AutoParkingDebugEnabled;

    private void LateUpdate()
    {
        var vehicle = GameManager.Instance?.selectedVehicle;
        if (!IsTarget(vehicle) || vehicle is not CarController car)
        {
            selectedVehicle = null;
            parkingHelper = null;
            return;
        }

        if (selectedVehicle != vehicle)
        {
            selectedVehicle = vehicle;
            parkingHelper = vehicle.GetComponentInChildren<VehicleParkingHelper>(true);
            if (parkingHelper == null)
                KoenigseggJeskoDiagnostics.AutoParkingWarn(context,
                    $"KoenigseggJesko auto-parking: vehicle={vehicle.GetInstanceID()} " +
                    "has no VehicleParkingHelper.");
            monitoredSpot = Vector3.positiveInfinity;
            refreshUntil = 0f;
        }
        if (parkingHelper == null ||
            LastAutoParkPositionField?.GetValue(parkingHelper) is not Vector3 spot ||
            Vector3.Distance(vehicle.transform.position, spot) > SpotDistance)
        {
            monitoredSpot = Vector3.positiveInfinity;
            return;
        }
        if (spot != monitoredSpot)
        {
            monitoredSpot = spot;
            refreshAttempts = 0;
            refreshUntil = Time.unscaledTime + RefreshDuration;
            nextRefresh = 0f;
        }
        if (Time.unscaledTime > refreshUntil || Time.unscaledTime < nextRefresh)
            return;
        nextRefresh = Time.unscaledTime + RefreshInterval;

        var panel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
        if (panel == null || !panel.isActiveAndEnabled ||
            HudParkingStateField?.GetValue(panel) is ParkingState.Legal)
            return;

        refreshAttempts++;
        Physics.SyncTransforms();
        car.UpdateParkingZone();
        var legalWheels = GetParkingWheelState(car, out var neighbourhood,
            out var wheelChecks);
        if (legalWheels &&
            HudParkingStateField?.GetValue(panel) is not ParkingState.Legal)
            panel.SetParkingZone(ParkingState.Legal, neighbourhood);

        if (DiagnosticsEnabled &&
            (refreshAttempts == 1 || refreshAttempts % 10 == 0))
            KoenigseggJeskoDiagnostics.AutoParkingInfo(context,
                $"KoenigseggJesko auto-parking refresh vehicle={vehicle.GetInstanceID()} " +
                $"attempt={refreshAttempts} speed={car.CurrentSpeed:0.00} " +
                $"hud={HudParkingStateField?.GetValue(panel)?.ToString() ?? "<unknown>"} " +
                $"legalWheels={legalWheels} root={vehicle.transform.position} " +
                $"wheels=[{wheelChecks}].");
    }

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
            KoenigseggJeskoDiagnostics.AutoParkingWarn(context,
                $"KoenigseggJesko auto-parking vehicle={vehicle.GetInstanceID()} " +
                $"is not fully inside a legal parking zone after exit; " +
                $"wheels=[{wheelChecks}].");
        if (DiagnosticsEnabled)
            KoenigseggJeskoDiagnostics.AutoParkingInfo(context,
                $"KoenigseggJesko auto-parking exit vehicle={vehicle.GetInstanceID()} " +
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
