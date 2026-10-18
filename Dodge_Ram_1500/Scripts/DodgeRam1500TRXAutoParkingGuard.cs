#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BAModAPI;
using BigAmbitions.InputSystem;
using UnityEngine.InputSystem;
using Helpers;
using UnityEngine;
using UnityEngine.UI;

internal sealed class DodgeRam1500TRXAutoParkingGuard : MonoBehaviour
{
    private const float SpotDistance = 0.75f;
    private static readonly FieldInfo? LastAutoParkPositionField =
        typeof(VehicleParkingHelper).GetField("_lastAutoParkPosition",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? HudParkingStateField =
        typeof(UI.ItemPanel.VehicleInfoPanel).GetField("currentParkingState",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private Button? autoParkButton;
    private InputAction? autoParkAction;
    private Coroutine? reconciliation;
    private ModContext? context;
    private string vehicleTypeName = string.Empty;

    internal static DodgeRam1500TRXAutoParkingGuard Install(
        GameObject owner, ModContext context, string vehicleTypeName)
    {
        var guard = owner.GetComponent<DodgeRam1500TRXAutoParkingGuard>() ??
                    owner.AddComponent<DodgeRam1500TRXAutoParkingGuard>();
        guard.context = context;
        guard.vehicleTypeName = vehicleTypeName;
        if (LastAutoParkPositionField == null || HudParkingStateField == null)
            DodgeRam1500TRXDiagnostics.AutoParkingWarn(context,
                "DodgeRam1500TRX auto-parking: required game parking field is unavailable.");
        var marker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "Dodge_Ram_1500", "Config", "auto-parking-diagnostics.enabled");
        if (File.Exists(marker))
        {
            DodgeRam1500TRXDiagnostics.DebugEnabled = true;
            DodgeRam1500TRXDiagnostics.AutoParkingDebugEnabled = true;
            context.Logger.Info("DodgeRam1500TRX auto-parking diagnostics enabled.");
        }
        GlobalEvents.onEnterVehicle -= guard.HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += guard.HandleVehicleEntered;
        GlobalEvents.onGameUnloaded -= guard.HandleGameUnloaded;
        GlobalEvents.onGameUnloaded += guard.HandleGameUnloaded;
        guard.BindButton();
        GlobalEvents.onExitVehicle -= guard.HandleVehicleExited;
        GlobalEvents.onExitVehicle += guard.HandleVehicleExited;
        return guard;
    }

    internal void Shutdown()
    {
        Unsubscribe();
        Destroy(this);
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        HandleGameUnloaded();
    }

    private void HandleGameUnloaded()
    {
        if (autoParkButton != null) autoParkButton.onClick.RemoveListener(HandleAutoParkRequested);
        autoParkButton = null;
        if (autoParkAction != null) autoParkAction.performed -= HandleAutoParkAction;
        autoParkAction = null;
        StopAllCoroutines();
        reconciliation = null;
    }

    private void HandleAutoParkAction(InputAction.CallbackContext action)
    {
        // GameManager invokes ClickAutoPark directly for SpecialInteract. This
        // bypasses Button.onClick; only observe the request, never invoke it twice.
        if (autoParkButton != null && autoParkButton.gameObject.activeInHierarchy &&
            autoParkButton.IsInteractable() && !CityMap.IsOpen &&
            !UI.Smartphone.FullMenu.IsOpen && !UI.Smartphone.Apps.Feedback.Feedback.IsOpen)
            HandleAutoParkRequested();
    }

    private void BindButton()
    {
        if (autoParkAction != null) autoParkAction.performed -= HandleAutoParkAction;
        autoParkAction = null;
        if (InputActionHelper.PlayerInputActionMap.TryGetValue(PlayerAction.SpecialInteract, out var action))
        {
            autoParkAction = action;
            action.performed += HandleAutoParkAction;
        }
        else DodgeRam1500TRXDiagnostics.AutoParkingWarn(context,
            "DodgeRam1500TRX auto-parking: SpecialInteract action unavailable.");
        var button = UI.UIs.Instance?.playerHUD?.itemPanelUI?.autoParkButton;
        if (autoParkButton != null) autoParkButton.onClick.RemoveListener(HandleAutoParkRequested);
        autoParkButton = button;
        if (button != null)
        {
            button.onClick.RemoveListener(HandleAutoParkRequested);
            button.onClick.AddListener(HandleAutoParkRequested);
        }
    }

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        if (IsTarget(vehicle)) StartCoroutine(BindAfterEntry());
    }

    private IEnumerator BindAfterEntry()
    {
        yield return new WaitForEndOfFrame();
        BindButton();
        if (autoParkButton == null)
            DodgeRam1500TRXDiagnostics.AutoParkingWarn(context, "DodgeRam1500TRX auto-parking: HUD button unavailable after vehicle entry.");
    }

    private void HandleAutoParkRequested()
    {
        var vehicle = GameManager.Instance?.selectedVehicle;
        if (!IsTarget(vehicle) || vehicle is not CarController car || !car.controlledByPlayer) return;
        var helper = car.GetComponentInChildren<VehicleParkingHelper>(true);
        if (helper == null)
        {
            DodgeRam1500TRXDiagnostics.AutoParkingWarn(context, "DodgeRam1500TRX auto-parking: native helper unavailable.");
            return;
        }
        if (reconciliation != null) StopCoroutine(reconciliation);
        reconciliation = StartCoroutine(ReconcileAutoPark(car, helper));
        DodgeRam1500TRXDiagnostics.AutoParkingInfo(context, $"DodgeRam1500TRX auto-parking requested vehicle={car.GetInstanceID()}; bounded HUD/save reconciliation scheduled.");
    }

    private IEnumerator ReconcileAutoPark(CarController car, VehicleParkingHelper helper)
    {
        // Native ClickAutoPark starts its 0.4-s fade/teleport/unfade routine first.
        // Observe only this known transition, with a hard three-second cap.
        yield return new WaitForSecondsRealtime(.5f);
        for (var attempt = 0; attempt < 60 && car != null && helper != null; attempt++)
        {
            yield return new WaitForEndOfFrame();
            Reconcile(car, helper, "auto-park", attempt);
            yield return new WaitForSecondsRealtime(.05f);
        }
        reconciliation = null;
    }

    private bool IsTarget(VehicleController? vehicle) =>
        vehicle != null &&
        (string.Equals(vehicle.vehicleInstance?.vehicleTypeName,
             vehicleTypeName, StringComparison.Ordinal) ||
         string.Equals(vehicle.vehicleType?.vehicleTypeName,
             vehicleTypeName, StringComparison.Ordinal));

    private bool DiagnosticsEnabled =>
        DodgeRam1500TRXDiagnostics.DebugEnabled &&
        DodgeRam1500TRXDiagnostics.AutoParkingDebugEnabled;

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

        var helper = car.GetComponentInChildren<VehicleParkingHelper>(true);
        if (helper != null)
        {
            Reconcile(car, helper, "exit", 0);
            // CarController schedules its own exit completion for the next frame.
            yield return null;
            yield return new WaitForEndOfFrame();
            if (car != null && helper != null) Reconcile(car, helper, "exit-complete", 0);
        }
    }

    private void Reconcile(CarController car, VehicleParkingHelper helper, string source, int attempt)
    {
        if (Mathf.Abs(car.CurrentSpeed) >= 1f) return;
        // Auto-park must match its recorded native destination. Ordinary exit
        // parking is also reconciled, but only after all four legal lane probes.
        if (source == "auto-park" &&
            (LastAutoParkPositionField?.GetValue(helper) is not Vector3 spot ||
             !IsAtAutoParkSpot(car.transform.position, spot, car.CurrentSpeed))) return;
        Physics.SyncTransforms();
        if (car.controlledByPlayer && ReferenceEquals(GameManager.Instance?.selectedVehicle, car)) car.UpdateParkingZone();
        var legalWheels = GetParkingWheelState(car, out var neighbourhood, out var wheelChecks);
        var savedBefore = car.vehicleInstance?.parkingState.ToString() ?? "<none>";
        var panel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
        if (legalWheels)
        {
            // Native CarController clears this on movement. Mark the completed
            // stopped transition checked so it cannot overwrite the corrected HUD.
            car.isCheckedForParkingZone = true;
            if (car.vehicleInstance != null)
            {
                car.vehicleInstance.parkingState = ParkingState.Legal;
                car.vehicleInstance.parkingNeighbourhood = neighbourhood;
            }
            var references = InstanceBehavior<GlobalReferences>.Instance;
            if (car.poi != null && references != null)
                car.poi.SetBackground(references.vehiclePOIBackgroundColor);
            if (car.controlledByPlayer && ReferenceEquals(GameManager.Instance?.selectedVehicle, car) &&
                panel != null && panel.isActiveAndEnabled &&
                HudParkingStateField?.GetValue(panel) is not ParkingState.Legal)
                panel.SetParkingZone(ParkingState.Legal, neighbourhood);
        }
        if (!legalWheels && (source.StartsWith("exit", StringComparison.Ordinal) || attempt == 59))
            DodgeRam1500TRXDiagnostics.AutoParkingWarn(context,
                $"DodgeRam1500TRX auto-parking vehicle={car.GetInstanceID()} not fully in a legal zone source={source}; root={car.transform.position}, nativeSpot={LastAutoParkPositionField?.GetValue(helper)}; wheels=[{wheelChecks}].");
        if (DiagnosticsEnabled && (attempt == 0 || attempt == 59))
            DodgeRam1500TRXDiagnostics.AutoParkingInfo(context,
                $"DodgeRam1500TRX auto-parking reconcile source={source} vehicle={car.GetInstanceID()} attempt={attempt + 1} legalWheels={legalWheels} hud={((panel == null) ? null : HudParkingStateField?.GetValue(panel))} savedBefore={savedBefore} savedAfter={car.vehicleInstance?.parkingState}; root={car.transform.position}, nativeSpot={LastAutoParkPositionField?.GetValue(helper)}; wheels=[{wheelChecks}].");
    }

    internal static bool IsAtAutoParkSpot(Vector3 position, Vector3 spot, float speed) =>
        !float.IsNaN(spot.x) && !float.IsInfinity(spot.x) &&
        !float.IsNaN(spot.y) && !float.IsInfinity(spot.y) &&
        !float.IsNaN(spot.z) && !float.IsInfinity(spot.z) &&
        Mathf.Abs(speed) < 1f && Vector2.Distance(new Vector2(position.x, position.z), new Vector2(spot.x, spot.z)) <= SpotDistance &&
        Mathf.Abs(position.y - spot.y) < 1.25f;

    private static ParkingLaneGenerator? SelectParkingLane(Collider[] hits, out bool ambiguous)
    {
        ambiguous = false;
        ParkingLaneGenerator? lane = null;
        foreach (var hit in hits)
        {
            var candidate = hit.GetComponentInParent<ParkingLaneGenerator>(true);
            if (candidate == null) continue;
            if (candidate.isHandicapParking) return candidate;
            if (lane != null && lane.neighbourhood != candidate.neighbourhood) { ambiguous = true; return lane; }
            lane = candidate;
        }
        return lane;
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
                    transform.position - car.transform.up * 0.1f,
                    new Vector3(0.1f, wheel.Radius * 2f, 0.1f),
                    car.transform.rotation, LayerHelper.parkingAreaLayerMask, QueryTriggerInteraction.Collide);
                var lane = SelectParkingLane(hits, out var ambiguous);
                if (ambiguous) legal = false;
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
