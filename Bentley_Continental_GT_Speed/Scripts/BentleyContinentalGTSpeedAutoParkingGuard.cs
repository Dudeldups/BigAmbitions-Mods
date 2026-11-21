#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BAModAPI;
using BigAmbitions.InputSystem;
using Helpers;
using PlayerActivity;
using UI.ItemPanel;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Wraps the native Auto Park button callback for Bentley instances and
/// reconciles parking state when the game's own parking coroutine completes.
/// </summary>
internal sealed class BentleyContinentalGTSpeedAutoParkingGuard : MonoBehaviour
{
    private const float SpotDistance = 0.75f;
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private ModContext? context;
    private string vehicleTypeName = string.Empty;
    private ItemPanelUI? hookedPanel;
    private Button? hookedButton;
    private InputAction? specialInteractAction;
    private UnityAction? replacementListener;
    private object? nativeClickTarget;
    private MethodInfo? nativeClickMethod;
    private readonly List<(int Index, UnityEventCallState State)> originalListenerStates = new();
    private readonly Dictionary<int, Vector3> autoParkSpotsByVehicle = new();
    private readonly Dictionary<int, string> autoParkNeighbourhoodsByVehicle = new();
    private readonly HashSet<int> autoParkOperations = new();
    private bool missingButtonLogged;

    internal static BentleyContinentalGTSpeedAutoParkingGuard Install(
        GameObject owner, ModContext context, string vehicleTypeName)
    {
        var guard = owner.GetComponent<BentleyContinentalGTSpeedAutoParkingGuard>() ??
                    owner.AddComponent<BentleyContinentalGTSpeedAutoParkingGuard>();
        guard.context = context;
        guard.vehicleTypeName = vehicleTypeName;
        var diagnosticMarker = Path.Combine(Application.persistentDataPath,
            "ModsLocal", "Bentley_Continental_GT_Speed", "Config", "auto-parking-diagnostics.enabled");
        if (File.Exists(diagnosticMarker))
        {
            BentleyContinentalGTSpeedDiagnostics.DebugEnabled = true;
            BentleyContinentalGTSpeedDiagnostics.AutoParkingDebugEnabled = true;
            context.Logger.Info("BentleyContinentalGTSpeed auto-parking diagnostics enabled.");
        }
        guard.Subscribe();
        guard.AttachAutoParkButton();
        GlobalEvents.RegisterOnGameLoadedLateCallback(guard.HandleGameLoadedLate);
        BentleyContinentalGTSpeedDiagnostics.AutoParkingInfo(context,
            $"guard installed for vehicleType='{vehicleTypeName}'.");
        return guard;
    }

    internal void Shutdown()
    {
        Unsubscribe();
        DetachAutoParkButton();
        StopAllCoroutines();
        autoParkSpotsByVehicle.Clear();
        autoParkNeighbourhoodsByVehicle.Clear();
        autoParkOperations.Clear();
        Destroy(this);
    }

    private void OnEnable() => Subscribe();

    private void OnDisable() => Unsubscribe();

    private void OnDestroy()
    {
        Unsubscribe();
        DetachAutoParkButton();
    }

    private void Subscribe()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onExitVehicle += HandleVehicleExited;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        GlobalEvents.onFullMenuToggle += HandleFullMenuToggle;
        BindSpecialInteractAction();
    }

    private void Unsubscribe()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onFullMenuToggle -= HandleFullMenuToggle;
        if (specialInteractAction != null)
            specialInteractAction.performed -= HandleSpecialInteract;
        specialInteractAction = null;
    }

    private void HandleSceneLoaded(Scene _, LoadSceneMode __) => AttachAutoParkButton();

    private void HandleVehicleEntered(VehicleController vehicle)
    {
        AttachAutoParkButton();
        if (IsTarget(vehicle) && vehicle is CarController car)
            StartCoroutine(ReconcileAfterEnter(car));
    }

    private void HandleVehicleExited(VehicleController vehicle)
    {
        if (!IsTarget(vehicle) || vehicle is not CarController car ||
            !autoParkSpotsByVehicle.TryGetValue(car.GetInstanceID(), out var spotPosition))
            return;

        StartCoroutine(ReconcileAfterExit(car, spotPosition));
    }

    private void HandleFullMenuToggle(bool _) => AttachAutoParkButton();

    private void HandleGameLoadedLate()
    {
        AttachAutoParkButton();
        BindSpecialInteractAction();
    }

    private void BindSpecialInteractAction()
    {
        if (specialInteractAction != null)
            specialInteractAction.performed -= HandleSpecialInteract;
        specialInteractAction = null;
        if (InputActionHelper.PlayerInputActionMap.TryGetValue(PlayerAction.SpecialInteract, out var action))
        {
            specialInteractAction = action;
            action.performed += HandleSpecialInteract;
        }
        else if (!missingButtonLogged)
        {
            BentleyContinentalGTSpeedDiagnostics.AutoParkingWarn(context,
                "SpecialInteract input action is unavailable; keyboard/gamepad Auto Park is not wrapped yet.");
        }
    }

    private void HandleSpecialInteract(InputAction.CallbackContext _)
    {
        var button = hookedButton ?? UI.UIs.Instance?.playerHUD?.itemPanelUI?.autoParkButton;
        if (button == null || !button.gameObject.activeInHierarchy || !button.IsInteractable() ||
            CityMap.IsOpen || UI.Smartphone.FullMenu.IsOpen || UI.Smartphone.Apps.Feedback.Feedback.IsOpen ||
            !IsTarget(GameManager.Instance?.selectedVehicle))
            return;

        if (GameManager.Instance?.selectedVehicle is CarController selectedCar &&
            Math.Abs(selectedCar.GetCurrentCondition() - 1f) < Mathf.Epsilon)
            return; // Leave the native broken-car notification to GameManager.

        // GameManager reads SpecialInteract in Update and invokes ClickAutoPark
        // directly, bypassing Button.onClick. Consume the available spot now;
        // that later native invocation then sees no spot and exits harmlessly.
        HandleAutoParkClicked();
    }

    private bool IsTarget(VehicleController? vehicle) =>
        vehicle != null &&
        (string.Equals(vehicle.vehicleInstance?.vehicleTypeName,
             vehicleTypeName, StringComparison.Ordinal) ||
         string.Equals(vehicle.vehicleType?.vehicleTypeName,
             vehicleTypeName, StringComparison.Ordinal));

    private void AttachAutoParkButton()
    {
        var panel = UI.UIs.Instance?.playerHUD?.itemPanelUI;
        var button = panel?.autoParkButton;
        if (panel == null || button == null)
        {
            if (!missingButtonLogged)
            {
                missingButtonLogged = true;
                BentleyContinentalGTSpeedDiagnostics.AutoParkingWarn(context,
                    "game Auto Park button is not available on this UI lifecycle callback.");
            }
            return;
        }
        missingButtonLogged = false;
        if (button == hookedButton && panel == hookedPanel)
            return;

        DetachAutoParkButton();
        var click = button.onClick;
        for (var index = 0; index < click.GetPersistentEventCount(); index++)
        {
            if (!string.Equals(click.GetPersistentMethodName(index), "ClickAutoPark", StringComparison.Ordinal))
                continue;
            var target = click.GetPersistentTarget(index);
            if (target == null || target != panel)
                continue;
            var method = target.GetType().GetMethod("ClickAutoPark", InstanceFlags);
            if (method == null || method.GetParameters().Length != 0)
                continue;

            nativeClickTarget = target;
            nativeClickMethod = method;
            originalListenerStates.Add((index, click.GetPersistentListenerState(index)));
            click.SetPersistentListenerState(index, UnityEventCallState.Off);
        }

        if (originalListenerStates.Count == 0)
        {
            BentleyContinentalGTSpeedDiagnostics.AutoParkingWarn(context,
                "native ItemPanelUI.ClickAutoPark callback was not found; parking callback remains unmodified.");
            nativeClickTarget = null;
            nativeClickMethod = null;
            return;
        }

        replacementListener = HandleAutoParkClicked;
        click.AddListener(replacementListener);
        hookedPanel = panel;
        hookedButton = button;
        BentleyContinentalGTSpeedDiagnostics.AutoParkingInfo(context,
            $"wrapped native Auto Park button; native click listeners={originalListenerStates.Count}.");
    }

    private void DetachAutoParkButton()
    {
        if (hookedButton != null)
        {
            if (replacementListener != null)
                hookedButton.onClick.RemoveListener(replacementListener);
            foreach (var entry in originalListenerStates)
                hookedButton.onClick.SetPersistentListenerState(entry.Index, entry.State);
        }
        originalListenerStates.Clear();
        hookedButton = null;
        hookedPanel = null;
        replacementListener = null;
        nativeClickTarget = null;
        nativeClickMethod = null;
    }

    private void HandleAutoParkClicked()
    {
        var game = GameManager.Instance;
        var vehicle = game?.selectedVehicle;
        if (!IsTarget(vehicle))
        {
            InvokeNativeAutoParkClick();
            return;
        }

        if (PlayerActivityUI.IsPanelOpen || game == null ||
            game.playerController == null || game.playerController.VehicleNavigationDisabled ||
            vehicle is not CarController car)
            return;
        if (Math.Abs(car.GetCurrentCondition() - 1f) < Mathf.Epsilon)
        {
            InvokeNativeAutoParkClick();
            return;
        }

        var helper = vehicle.GetComponentInChildren<VehicleParkingHelper>(true);
        if (helper == null)
        {
            BentleyContinentalGTSpeedDiagnostics.AutoParkingWarn(context,
                $"selected Bentley instance={vehicle.GetInstanceID()} has no VehicleParkingHelper.");
            return;
        }

        var spot = helper.availableAutoParkSpot;
        if (spot == null)
        {
            // Preserve the native early-exit behavior when no spot is available.
            StartCoroutine(helper.ParkInClosestSpot());
            return;
        }

        var vehicleId = car.GetInstanceID();
        if (!autoParkOperations.Add(vehicleId))
        {
            BentleyContinentalGTSpeedDiagnostics.AutoParkingInfo(context,
                $"ignored duplicate Auto Park request vehicle={vehicleId} spot={spot.transform.position}; " +
                "the native parking coroutine is already in flight.");
            return;
        }

        var spotPosition = spot.transform.position;
        autoParkSpotsByVehicle[vehicleId] = spotPosition;
        var spotLane = spot.GetComponentInParent<ParkingLaneGenerator>(true);
        autoParkNeighbourhoodsByVehicle[vehicleId] = spotLane?.neighbourhood ?? string.Empty;
        var spotBounds = spot.boxCollider != null
            ? $"center={spot.boxCollider.bounds.center},size={spot.boxCollider.bounds.size}"
            : "<missing>";
        BentleyContinentalGTSpeedDiagnostics.AutoParkingInfo(context,
            $"Auto Park clicked vehicle={vehicleId} root={car.transform.position} " +
            $"spot={spotPosition} spotRotation={spot.transform.parent.rotation.eulerAngles} " +
            $"spotBox={spotBounds} maxVehicleLength={spot.maxVehicleLength:0.000} " +
            $"lane={spotLane?.name ?? "<none>"} " +
            $"neighbourhood='{autoParkNeighbourhoodsByVehicle[vehicleId]}' " +
            $"laneColliders=[{DescribeLaneColliders(spotLane)}].");
        // Unity starts a coroutine immediately and runs it through its first
        // yield. The native routine therefore consumes this spot before the
        // GameManager's later direct ClickAutoPark call can start a duplicate.
        var nativeParking = StartCoroutine(helper.ParkInClosestSpot());
        if (helper.availableAutoParkSpot == spot &&
            Math.Abs(car.GetCurrentCondition() - 1f) >= Mathf.Epsilon)
        {
            BentleyContinentalGTSpeedDiagnostics.AutoParkingWarn(context,
                $"native Auto Park coroutine did not consume spot={spotPosition} synchronously " +
                $"for vehicle={vehicleId}; no second parking sequence was started.");
            // The game's iterator consumes its spot before its first yield. If
            // that contract changes, stop it and fail closed rather than
            // teleporting twice through a custom fade/parking routine.
            if (nativeParking != null)
                StopCoroutine(nativeParking);
            helper.availableAutoParkSpot = null;
            spot.visuals.enabled = false;
            autoParkOperations.Remove(vehicleId);
            return;
        }
        StartCoroutine(ParkAndReconcile(nativeParking, car, spotPosition, vehicleId));
    }

    private void InvokeNativeAutoParkClick()
    {
        try
        {
            nativeClickMethod?.Invoke(nativeClickTarget, null);
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                "BentleyContinentalGTSpeed auto-parking: native click callback failed: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private IEnumerator ParkAndReconcile(
        Coroutine nativeParking, CarController car, Vector3 spotPosition, int vehicleId)
    {
        try
        {
            // Await the game's one native fade/teleport/parking-zone update.
            yield return nativeParking;
            yield return new WaitForEndOfFrame();
            FinishParkingReconcile(car, spotPosition, "auto-park-native-complete", 0);
            // CarController.Update can make one more stopped-vehicle parking
            // check on the next frame. Reconcile after that known transition,
            // then mark a fully validated legal state checked to prevent a
            // later vanilla probe from replacing it.
            yield return null;
            yield return new WaitForEndOfFrame();
            if (car != null)
                FinishParkingReconcile(car, spotPosition, "auto-park-next-frame", 1, true);
        }
        finally
        {
            autoParkOperations.Remove(vehicleId);
        }
    }

    private IEnumerator ReconcileAfterEnter(CarController car)
    {
        // CarController.EnterVehicle runs the native parking probe before the
        // enter event. Recheck once after that transition so a saved, genuinely
        // legal lane is not left illegal by the native direct-component lookup.
        yield return new WaitForEndOfFrame();
        if (car == null || !IsTarget(car) || car.vehicleInstance == null)
            yield break;

        Physics.SyncTransforms();
        var panel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
        var hudBefore = panel != null ? panel.currentParkingState.ToString() : "<unavailable>";
        var savedBefore = car.vehicleInstance.parkingState;
        var useActualWheelCenter = savedBefore == ParkingState.Legal;
        var legalWheels = GetParkingWheelState(car, out var neighbourhood, out var wheelChecks,
            BentleyContinentalGTSpeedDiagnostics.DebugEnabled &&
            BentleyContinentalGTSpeedDiagnostics.AutoParkingDebugEnabled,
            useActualWheelCenter);
        if (legalWheels)
        {
            // This is a geometry-validated correction only: every configured
            // wheel must overlap a non-handicap ParkingLaneGenerator.
            car.vehicleInstance.parkingState = ParkingState.Legal;
            if (!string.IsNullOrWhiteSpace(neighbourhood))
                car.vehicleInstance.parkingNeighbourhood = neighbourhood;
            car.isCheckedForParkingZone = true;
            var references = InstanceBehavior<GlobalReferences>.Instance;
            if (car.poi != null && references != null)
                car.poi.SetBackground(references.vehiclePOIBackgroundColor);
            if (car.controlledByPlayer && ReferenceEquals(GameManager.Instance?.selectedVehicle, car) &&
                panel != null && panel.isActiveAndEnabled && panel.currentParkingState != ParkingState.Legal)
                panel.SetParkingZone(ParkingState.Legal, neighbourhood);
        }

        if (BentleyContinentalGTSpeedDiagnostics.DebugEnabled &&
            BentleyContinentalGTSpeedDiagnostics.AutoParkingDebugEnabled)
            BentleyContinentalGTSpeedDiagnostics.AutoParkingInfo(context,
                $"enter reconciliation vehicle={car.GetInstanceID()} root={car.transform.position} " +
                $"savedBefore={savedBefore} probeCenter={(useActualWheelCenter ? "wheel-position" : "mount")} " +
                $"hudBefore={hudBefore} legalWheels={legalWheels} " +
                $"savedAfter={car.vehicleInstance.parkingState} " +
                $"neighbourhood='{neighbourhood}' wheels=[{wheelChecks}].");
    }

    private IEnumerator ReconcileAfterExit(CarController car, Vector3 spotPosition)
    {
        // VehicleController saves the HUD state before raising onExitVehicle;
        // CarController then completes exit navigation one frame later.
        yield return new WaitForEndOfFrame();
        FinishParkingReconcile(car, spotPosition, "exit", 0);
        yield return null;
        yield return new WaitForEndOfFrame();
        if (car != null)
            FinishParkingReconcile(car, spotPosition, "exit-complete", 1);
        if (car != null && !IsAtAutoParkSpot(car.transform.position, spotPosition, car.CurrentSpeed))
        {
            autoParkSpotsByVehicle.Remove(car.GetInstanceID());
            autoParkNeighbourhoodsByVehicle.Remove(car.GetInstanceID());
        }
    }

    private void FinishParkingReconcile(
        CarController? car, Vector3 spotPosition, string phase, int attempt,
        bool finalAutoParkAttempt = false)
    {
        if (car == null || car.vehicleInstance == null)
            return;

        if (!IsAtAutoParkSpot(car.transform.position, spotPosition, car.CurrentSpeed))
        {
            if (finalAutoParkAttempt)
            {
                var planarDistance = Vector2.Distance(
                    new Vector2(car.transform.position.x, car.transform.position.z),
                    new Vector2(spotPosition.x, spotPosition.z));
                BentleyContinentalGTSpeedDiagnostics.AutoParkingWarn(context,
                    $"Auto Park destination mismatch vehicle={car.GetInstanceID()} phase={phase}; " +
                    $"root={car.transform.position}, rootRotation={car.transform.rotation.eulerAngles}, " +
                    $"spot={spotPosition}, planarDistance={planarDistance:0.000}, " +
                    $"verticalDelta={car.transform.position.y - spotPosition.y:0.000}, " +
                    $"speed={car.CurrentSpeed:0.00}.");
            }
            return;
        }

        var stateBefore = car.vehicleInstance.parkingState;
        Physics.SyncTransforms();
        var panel = UI.UIs.Instance?.playerHUD?.itemPanelUI?.vehicleInfo;
        var hudAtReconcile = panel != null ? panel.currentParkingState : (ParkingState?)null;
        var legalWheels = GetParkingWheelState(car, out var wheelNeighbourhood, out var wheelChecks,
            finalAutoParkAttempt || (BentleyContinentalGTSpeedDiagnostics.DebugEnabled &&
                                     BentleyContinentalGTSpeedDiagnostics.AutoParkingDebugEnabled),
            true);
        autoParkNeighbourhoodsByVehicle.TryGetValue(car.GetInstanceID(), out var parkedNeighbourhood);
        // Vanilla considers a vehicle legal when every wheel is over a
        // non-handicap parking lane; it does not require one neighborhood for
        // all wheels or equality with the AutoParkSpot's lane neighborhood.
        if (legalWheels)
        {
            var neighbourhood = !string.IsNullOrWhiteSpace(wheelNeighbourhood)
                ? wheelNeighbourhood
                : !string.IsNullOrWhiteSpace(parkedNeighbourhood)
                    ? parkedNeighbourhood
                    : car.vehicleInstance.parkingNeighbourhood;
            car.vehicleInstance.parkingState = ParkingState.Legal;
            if (!string.IsNullOrWhiteSpace(neighbourhood))
                car.vehicleInstance.parkingNeighbourhood = neighbourhood;
            car.isCheckedForParkingZone = true;
            var references = InstanceBehavior<GlobalReferences>.Instance;
            if (car.poi != null && references != null)
                car.poi.SetBackground(references.vehiclePOIBackgroundColor);
            if (car.controlledByPlayer && ReferenceEquals(GameManager.Instance?.selectedVehicle, car) &&
                panel != null && panel.isActiveAndEnabled)
                panel.SetParkingZone(ParkingState.Legal, neighbourhood);
        }
        else if (finalAutoParkAttempt)
            BentleyContinentalGTSpeedDiagnostics.AutoParkingWarn(context,
                $"Auto Park geometry rejected vehicle={car.GetInstanceID()} phase={phase} attempt={attempt}; " +
                $"root={car.transform.position}, rootRotation={car.transform.rotation.eulerAngles}, " +
                $"spot={spotPosition}, speed={car.CurrentSpeed:0.00}, " +
                $"savedState={car.vehicleInstance.parkingState}, " +
                $"hudAtReconcile={hudAtReconcile?.ToString() ?? "<unavailable>"}, " +
                $"expectedNeighbourhood='{parkedNeighbourhood}', wheels=[{wheelChecks}].");

        if (attempt == 0 || finalAutoParkAttempt || stateBefore != car.vehicleInstance.parkingState ||
            hudAtReconcile == ParkingState.Illegal)
            BentleyContinentalGTSpeedDiagnostics.AutoParkingInfo(context,
                $"Auto Park reconcile vehicle={car.GetInstanceID()} phase={phase} attempt={attempt} " +
                $"root={car.transform.position} spot={spotPosition} speed={car.CurrentSpeed:0.00} " +
                $"savedBefore={stateBefore} " +
                $"savedAfter={car.vehicleInstance.parkingState} " +
                $"hudAtReconcile={hudAtReconcile?.ToString() ?? "<unavailable>"} " +
                $"legalWheels={legalWheels} expectedNeighbourhood='{parkedNeighbourhood}' " +
                $"wheelNeighbourhood='{wheelNeighbourhood}' wheels=[{wheelChecks}].");
    }

    private static bool IsAtAutoParkSpot(Vector3 position, Vector3 spot, float speed) =>
        !float.IsNaN(spot.x) && !float.IsInfinity(spot.x) &&
        !float.IsNaN(spot.y) && !float.IsInfinity(spot.y) &&
        !float.IsNaN(spot.z) && !float.IsInfinity(spot.z) &&
        Mathf.Abs(speed) < 1f &&
        Vector2.Distance(new Vector2(position.x, position.z), new Vector2(spot.x, spot.z)) <= SpotDistance &&
        Mathf.Abs(position.y - spot.y) < 1.25f;

    private static bool GetParkingWheelState(
        CarController car, out string neighbourhood, out string wheelChecks,
        bool includeColliderDetails = false, bool useActualWheelCenter = false)
    {
        neighbourhood = string.Empty;
        var checks = new List<string>();
        var wheels = car.vehicleController?.powertrain?.wheels;
        var legal = wheels != null && wheels.Count == 4;
        if (wheels != null)
        {
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
                var mountPosition = transform.position;
                var actualWheelCenter = wheel.WheelPosition;
                var wheelPosition = useActualWheelCenter ? actualWheelCenter : mountPosition;
                var springLength = wheel.SpringLength;
                var isGrounded = wheel.IsGrounded;
                // Preserve native box dimensions, rotation, layers and trigger
                // policy. Captured Auto Park uses the physical tire center:
                // NWH's suspension mount can sit above a flat lane collider.
                // Manual/saved-Illegal checks retain the native mount origin.
                var queryCenter = wheelPosition - transform.up * 0.1f;
                var halfExtents = new Vector3(0.1f, wheel.Radius * 2f, 0.1f);
                var hits = Physics.OverlapBox(queryCenter, halfExtents, transform.rotation,
                    LayerHelper.parkingAreaLayerMask);
                var firstHit = hits.Length > 0 ? hits[0] : null;
                var directLane = firstHit != null ? firstHit.GetComponent<ParkingLaneGenerator>() : null;
                var lane = firstHit != null
                    ? firstHit.GetComponentInParent<ParkingLaneGenerator>(true)
                    : null;
                if (lane == null || lane.isHandicapParking)
                    legal = false;
                if (lane != null)
                    neighbourhood = lane.neighbourhood;
                var colliders = includeColliderDetails
                    ? string.Join(" | ", Array.ConvertAll(hits, DescribeParkingCollider))
                    : $"first={firstHit?.name ?? "<none>"}";
                checks.Add($"{index}:{transform.name} probe={(useActualWheelCenter ? "wheel-position" : "mount")} " +
                           $"mount={mountPosition} wheelCenter={actualWheelCenter} " +
                           $"springLength={springLength:0.000} grounded={isGrounded} " +
                           $"queryCenter={queryCenter} " +
                           $"radius={wheel.Radius:0.000} halfExtents={halfExtents} " +
                           $"hits={hits.Length} colliders=[{colliders}] " +
                           $"directLane={directLane?.name ?? "<none>"} " +
                           $"parentLane={lane?.name ?? "<none>"} " +
                           $"neighbourhood='{lane?.neighbourhood ?? string.Empty}' " +
                           $"handicap={lane?.isHandicapParking.ToString() ?? "<none>"}");
            }
        }

        wheelChecks = string.Join("; ", checks);
        return legal;
    }

    private static string DescribeParkingCollider(Collider collider)
    {
        var directLane = collider.GetComponent<ParkingLaneGenerator>();
        var parentLane = collider.GetComponentInParent<ParkingLaneGenerator>(true);
        var path = collider.transform.name;
        for (var parent = collider.transform.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;
        return $"{path}(layer={collider.gameObject.layer}," +
               $"layerName={LayerMask.LayerToName(collider.gameObject.layer)},trigger={collider.isTrigger}," +
               $"boundsCenter={collider.bounds.center},boundsSize={collider.bounds.size}," +
               $"directLane={directLane?.name ?? "<none>"},parentLane={parentLane?.name ?? "<none>"}," +
               $"neighbourhood='{parentLane?.neighbourhood ?? string.Empty}'," +
               $"handicap={parentLane?.isHandicapParking.ToString() ?? "<none>"})";
    }

    private static string DescribeLaneColliders(ParkingLaneGenerator? lane)
    {
        if (lane == null)
            return "<none>";

        var descriptions = new List<string>();
        foreach (var collider in lane.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
                descriptions.Add(DescribeParkingCollider(collider));
        }
        return descriptions.Count > 0 ? string.Join(" | ", descriptions) : "<no-active-colliders>";
    }
}
