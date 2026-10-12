#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BAModAPI;
using Helpers;
using UnityEngine;

internal sealed class McLarenSennaAutoParkingGuard : MonoBehaviour
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
    private Coroutine? reconciliationCoroutine;

    internal static McLarenSennaAutoParkingGuard Install(
        GameObject owner, ModContext context, string vehicleTypeName)
    {
        var guard = owner.GetComponent<McLarenSennaAutoParkingGuard>() ??
                    owner.AddComponent<McLarenSennaAutoParkingGuard>();
        guard.context = context;
        guard.vehicleTypeName = vehicleTypeName;
        if (LastAutoParkPositionField == null || HudParkingStateField == null)
            McLarenSennaDiagnostics.AutoParkingWarn(context,
                "McLarenSenna auto-parking: required game parking field is unavailable.");
        var marker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "McLaren_Senna", "Config", "auto-parking-diagnostics.enabled");
        if (File.Exists(marker))
        {
            McLarenSennaDiagnostics.DebugEnabled = true;
            McLarenSennaDiagnostics.AutoParkingDebugEnabled = true;
            context.Logger.Info("McLarenSenna auto-parking diagnostics enabled.");
        }
        GlobalEvents.onExitVehicle -= guard.HandleVehicleExited;
        GlobalEvents.onExitVehicle += guard.HandleVehicleExited;
        GlobalEvents.onEnterVehicle -= guard.HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += guard.HandleVehicleEntered;
        GlobalEvents.onVehicleVariablesChanged -= guard.HandleVehicleVariablesChanged;
        GlobalEvents.onVehicleVariablesChanged += guard.HandleVehicleVariablesChanged;
        return guard;
    }

    internal void Shutdown()
    {
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
        StopAllCoroutines();
        Destroy(this);
    }

    private void OnDestroy()
    {
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onVehicleVariablesChanged -= HandleVehicleVariablesChanged;
    }

    private bool IsTarget(VehicleController? vehicle) =>
        vehicle != null &&
        (string.Equals(vehicle.vehicleInstance?.vehicleTypeName,
             vehicleTypeName, StringComparison.Ordinal) ||
         string.Equals(vehicle.vehicleType?.vehicleTypeName,
             vehicleTypeName, StringComparison.Ordinal));

    private bool DiagnosticsEnabled =>
        McLarenSennaDiagnostics.DebugEnabled &&
        McLarenSennaDiagnostics.AutoParkingDebugEnabled;

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        QueueReconciliation(vehicle, updateHud: true);
    }

    private void HandleVehicleVariablesChanged()
    {
        var selectedVehicle = GameManager.Instance?.selectedVehicle;
        if (IsTarget(selectedVehicle))
            QueueReconciliation(selectedVehicle!, updateHud: true);
    }

    private void QueueReconciliation(VehicleController vehicle, bool updateHud)
    {
        if (!IsTarget(vehicle) || vehicle is not CarController)
            return;
        if (reconciliationCoroutine != null)
            StopCoroutine(reconciliationCoroutine);
        reconciliationCoroutine = StartCoroutine(
            ReconcileAfterLifecycleEvent(vehicle, updateHud));
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (IsTarget(vehicle))
            QueueReconciliation(vehicle, updateHud: false);
    }

    private IEnumerator ReconcileAfterLifecycleEvent(
        VehicleController vehicle, bool updateHud)
    {
        yield return new WaitForEndOfFrame();
        if (vehicle == null || vehicle is not CarController car)
        {
            reconciliationCoroutine = null;
            yield break;
        }

        Physics.SyncTransforms();
        var helper = vehicle.GetComponentInChildren<VehicleParkingHelper>(true);
        var spot = helper != null &&
            LastAutoParkPositionField?.GetValue(helper) is Vector3 position
            ? position : Vector3.positiveInfinity;
        var distance = Vector3.Distance(vehicle.transform.position, spot);
        if (distance > SpotDistance)
        {
            reconciliationCoroutine = null;
            yield break;
        }

        var legalWheels = GetParkingWheelState(car, out var neighbourhood,
            out var wheelChecks);
        if (updateHud)
        {
            var panel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
            if (panel != null && panel.isActiveAndEnabled)
            {
                car.UpdateParkingZone();
                if (legalWheels &&
                    HudParkingStateField?.GetValue(panel) is not ParkingState.Legal)
                    panel.SetParkingZone(ParkingState.Legal, neighbourhood);
            }

            if (DiagnosticsEnabled)
            {
                var currentPanel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
                var hudState = currentPanel != null
                    ? HudParkingStateField?.GetValue(currentPanel)?.ToString() ?? "<unknown>"
                    : "<unavailable>";
                McLarenSennaDiagnostics.AutoParkingInfo(context,
                    $"McLarenSenna auto-parking event refresh vehicle={vehicle.GetInstanceID()} " +
                    $"speed={car.CurrentSpeed:0.00} " +
                    $"hud={hudState} legalWheels={legalWheels} " +
                    $"root={vehicle.transform.position} wheels=[{wheelChecks}].");
            }
        }

        var savedBefore = vehicle.vehicleInstance?.parkingState.ToString() ?? "<none>";
        if (legalWheels && vehicle.vehicleInstance != null &&
            vehicle.vehicleInstance.parkingState != ParkingState.Legal)
        {
            vehicle.vehicleInstance.parkingState = ParkingState.Legal;
            vehicle.vehicleInstance.parkingNeighbourhood = neighbourhood;
        }
        if (!legalWheels)
            McLarenSennaDiagnostics.AutoParkingWarn(context,
                $"McLarenSenna auto-parking vehicle={vehicle.GetInstanceID()} " +
                $"is not fully inside a legal parking zone after exit; " +
                $"wheels=[{wheelChecks}].");
        if (DiagnosticsEnabled)
            McLarenSennaDiagnostics.AutoParkingInfo(context,
                $"McLarenSenna auto-parking exit vehicle={vehicle.GetInstanceID()} " +
                $"distanceToSpot={distance:0.00}m legalWheels={legalWheels} " +
                $"savedBefore={savedBefore} " +
                $"savedAfter={vehicle.vehicleInstance?.parkingState.ToString() ?? "<none>"} " +
                $"wheels=[{wheelChecks}].");
        reconciliationCoroutine = null;
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

