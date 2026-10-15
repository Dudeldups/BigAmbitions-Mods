#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BAModAPI;
using Helpers;
using UI;
using UI.Elements;
using UI.Overlays;
using UnityEngine;
using UnityEngine.SceneManagement;

// Decorate native button lists on entry and exact UI activation transitions.
// Native movement refreshes the complete list; no polling or hierarchy watcher.
[AddComponentMenu("")]
public sealed class AudiR8WorkshopButtons : MonoBehaviour
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo? OverlayField = typeof(OverlayUI).GetField("_currentOverlay", Members);
    private static readonly FieldInfo? StationField = typeof(GasStationOverlay).GetField("_currentStationTrigger", Members);
    private readonly List<GasStationTrigger> stations = new List<GasStationTrigger>();
    private readonly HashSet<string> warnings = new HashSet<string>();
    private ModContext? context;
    private OverlayUI? ui;
    private WorkshopOverlay? activeOverlay;
    private Coroutine? entryHandoff;
    private OverlayUI? observedUi;
    private AudiR8WorkshopOverlayLifecycle? overlayLifecycle;
    internal const string ButtonName = "AudiR8RacingStripes";

    internal static AudiR8WorkshopButtons Install(GameObject root, ModContext context)
    {
        var controller = root.GetComponent<AudiR8WorkshopButtons>() ?? root.AddComponent<AudiR8WorkshopButtons>();
        controller.context = context;
        GlobalEvents.RegisterOnGameLoadedLateCallback(controller.HandleGameLoaded);
        controller.ObserveStations("mod-load");
        return controller;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        GlobalEvents.onGameUnloaded += HandleGameUnloaded;
        GlobalEvents.onEnterVehicle += HandleVehicleEntered;
        GlobalEvents.onExitVehicle += HandleVehicleExited;
        GlobalEvents.onFullMenuToggle += HandleMenuToggle;
        GlobalEvents.onVehicleVariablesChanged += ScheduleEntry;
    }
    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        GlobalEvents.onGameUnloaded -= HandleGameUnloaded;
        GlobalEvents.onEnterVehicle -= HandleVehicleEntered;
        GlobalEvents.onExitVehicle -= HandleVehicleExited;
        GlobalEvents.onFullMenuToggle -= HandleMenuToggle;
        GlobalEvents.onVehicleVariablesChanged -= ScheduleEntry;
        overlayLifecycle?.Unbind();
        StopObserving();
    }
    private void HandleSceneLoaded(Scene _, LoadSceneMode __) => ObserveStations("scene-loaded");
    private void HandleGameLoaded() { if (this != null && context != null) ObserveStations("game-loaded-late"); }
    private void HandleGameUnloaded() { overlayLifecycle?.Unbind(); StopObserving(); }
    private void HandleVehicleEntered(VehicleController _) => ScheduleEntry();
    private void HandleVehicleExited(VehicleController _) { StopHandoff(); ReleaseOverlay(); }
    private void HandleMenuToggle(bool open) { if (!open) ScheduleEntry(); }

    private void ObserveStations(string source)
    {
        if (context == null) return;
        StopObserving();
        if (OverlayField == null || StationField == null)
        {
            WarnOnce("native-fields", "native workshop overlay bindings are unavailable.");
            return;
        }
        ObserveOverlayLifecycle();
        // One discovery on scene/game load, followed by exact trigger callbacks.
        foreach (var station in Resources.FindObjectsOfTypeAll<GasStationTrigger>())
        {
            if (station == null || !station.gameObject.scene.IsValid()) continue;
            station.onEntered += HandleStationEntered;
            station.onExited += HandleStationExited;
            stations.Add(station);
        }
        AudiR8Diagnostics.StripesInfo(context, $"AudiR8 workshop triggers bound source='{source}', count={stations.Count}.");
        ScheduleEntry();
    }
    private void ObserveOverlayLifecycle()
    {
        var overlayUi = InstanceBehavior<UIs>.Instance?.overlayUI;
        if (overlayUi == null) return;
        if (!ReferenceEquals(observedUi, overlayUi)) ReleaseOverlayLifecycle();
        observedUi = overlayUi;
        overlayLifecycle = overlayUi.GetComponent<AudiR8WorkshopOverlayLifecycle>();
        if (overlayLifecycle == null) overlayLifecycle = overlayUi.gameObject.AddComponent<AudiR8WorkshopOverlayLifecycle>();
        // Assignment replaces any old callback; repeated scene/load callbacks
        // cannot subscribe duplicates. Main-menu unload can suspend and rebind it.
        overlayLifecycle.Bind(() => DecorateVisibleOverlay(overlayUi, "overlay-enabled"));
    }
    private void ReleaseOverlayLifecycle()
    {
        if (overlayLifecycle != null)
        {
            overlayLifecycle.Unbind();
            Destroy(overlayLifecycle);
        }
        overlayLifecycle = null;
        observedUi = null;
    }
    private void StopObserving()
    {
        StopHandoff();
        ReleaseOverlay();
        foreach (var station in stations)
        {
            if (station == null) continue;
            station.onEntered -= HandleStationEntered;
            station.onExited -= HandleStationExited;
        }
        stations.Clear();
    }
    private void HandleStationEntered(GasStationTrigger _) => ScheduleEntry();
    private void HandleStationExited(GasStationTrigger station)
    {
        if (activeOverlay == null || !ReferenceEquals(activeOverlay.Station, station)) return;
        if (!IsInBay(activeOverlay.Vehicle, station)) { StopHandoff(); ReleaseOverlay(); }
    }
    private void ScheduleEntry()
    {
        if (context == null || !isActiveAndEnabled) return;
        var vehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (vehicle?.vehicleInstance?.vehicleTypeName != AudiR8Mod.VehicleTypeName) return;
        var station = stations.FirstOrDefault(s => s != null && s.isRepairStation && !s.isTruckGarage && IsInBay(vehicle, s));
        if (station == null) return;
        StopHandoff();
        entryHandoff = StartCoroutine(DecorateAfterEntry(vehicle, station));
    }
    private IEnumerator DecorateAfterEntry(VehicleController vehicle, GasStationTrigger station)
    {
        // A bounded entry handoff: allow native and third-party station callbacks
        // (including their one-frame handoffs) to finish, then decorate once.
        yield return null;
        yield return null;
        entryHandoff = null;
        var overlayUi = InstanceBehavior<UIs>.Instance?.overlayUI;
        if (overlayUi == null || !IsEligible(SaveGameManager.Current, vehicle, station) || !OverlayUI.IsVisible) yield break;
        DecorateVisibleOverlay(overlayUi, "entry-handoff");
    }
    private void DecorateVisibleOverlay(OverlayUI overlayUi, string source)
    {
        if (context == null || !isActiveAndEnabled || !OverlayUI.IsVisible ||
            !ReferenceEquals(InstanceBehavior<UIs>.Instance?.overlayUI, overlayUi)) return;
        var current = OverlayField!.GetValue(overlayUi);
        if (current is WorkshopOverlay existing && ReferenceEquals(existing, activeOverlay)) return;
        if (!(current is GasStationOverlay native) || !(StationField!.GetValue(native) is GasStationTrigger station)) return;
        var vehicle = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        var save = SaveGameManager.Current;
        if (vehicle == null || !IsEligible(save, vehicle, station)) return;
        // The optional repaint UI returns to its original GasStationOverlay.
        // Reuse the existing decoration for that exact car/save/station, or
        // create it once. Do not replace overlayUi.gasStation or re-show the UI.
        if (activeOverlay == null || !ReferenceEquals(activeOverlay.Source, native) ||
            !ReferenceEquals(activeOverlay.Save, save) || !ReferenceEquals(activeOverlay.Vehicle, vehicle) ||
            !ReferenceEquals(activeOverlay.Station, station) || !activeOverlay.IsCurrentTarget())
        {
            ReleaseOverlay();
            activeOverlay = new WorkshopOverlay(this, native, save!, vehicle, station);
        }
        ui = overlayUi;
        OverlayField.SetValue(overlayUi, activeOverlay);
        overlayUi.RefreshButtons();
        AudiR8Diagnostics.StripesInfo(context, $"AudiR8 workshop overlay decorated event='{source}' vehicle='{vehicle.vehicleInstance.id}', source='{native.GetType().FullName}'.");
    }
    private void StopHandoff()
    {
        if (entryHandoff != null) StopCoroutine(entryHandoff);
        entryHandoff = null;
    }
    private static bool IsInBay(VehicleController? vehicle, GasStationTrigger station) =>
        station != null && station.isActiveAndEnabled && vehicle != null && vehicle.vehicleCollider != null &&
        station.IntersectsBounds(vehicle.vehicleCollider.bounds);
    private static bool IsEligible(GameInstance? save, VehicleController vehicle, GasStationTrigger station) =>
        save != null && ReferenceEquals(save, SaveGameManager.Current) &&
        InstanceBehavior<GameManager>.Instance?.selectedVehicle == vehicle && VehicleHelper.IsInsideMotorVehicle() &&
        vehicle.vehicleInstance?.vehicleTypeName == AudiR8Mod.VehicleTypeName && station.isRepairStation && !station.isTruckGarage &&
        IsInBay(vehicle, station) && save.VehicleInstances.Any(v => v.id == vehicle.vehicleInstance.id && v.vehicleTypeName == AudiR8Mod.VehicleTypeName);

    internal static ButtonInfo CreateButton(GameInstance save, string id, bool stopped, Action onClick) =>
        new ButtonInfo(ButtonName,
            AudiR8RacingStripes.GetRacingStripes(save, id) ? "audir8_stripes_remove" : "audir8_stripes_add",
            "blue", onClick, PlayerAction.Confirm, stopped);

    internal static ButtonInfo[] ComposeButtons(ButtonInfo[] source, ButtonInfo stripe, Action<Action> invokeNative)
    {
        var result = new ButtonInfo[source.Length + 1];
        for (var i = 0; i < source.Length; i++)
        {
            var button = source[i];
            // Native actions may hide their own overlay or open a purchase UI.
            // Bridge the identity for the call, preserving their normal lifecycle.
            result[i] = new ButtonInfo(button.name, button.key, button.arguments, button.color,
                () => invokeNative(button.onClick), button.playerAction, button.interactable);
        }
        result[source.Length] = stripe;
        return result;
    }
    private void ReleaseOverlay(bool restoreNative = false)
    {
        if (activeOverlay != null && ui != null && ReferenceEquals(InstanceBehavior<UIs>.Instance?.overlayUI, ui) &&
            ReferenceEquals(OverlayField?.GetValue(ui), activeOverlay))
        {
            var source = activeOverlay.Source;
            OverlayUI.Hide(activeOverlay);
            if (restoreNative && IsEligible(activeOverlay.Save, activeOverlay.Vehicle, activeOverlay.Station)) OverlayUI.Show(source);
        }
        activeOverlay = null;
        ui = null;
    }
    private void WarnOnce(string reason, string message)
    {
        if (warnings.Add(reason)) context?.Logger.Warn($"AudiR8 workshop [{reason}]: {message}");
    }
    private void OnDestroy() => ReleaseOverlayLifecycle();
    internal void Shutdown() { StopHandoff(); ReleaseOverlayLifecycle(); ReleaseOverlay(restoreNative: true); context = null; enabled = false; Destroy(this); }

    private sealed class WorkshopOverlay : GasStationOverlay, IOverlay
    {
        private readonly AudiR8WorkshopButtons owner;
        internal readonly GasStationOverlay Source;
        internal readonly GameInstance Save;
        internal readonly VehicleController Vehicle;
        internal readonly GasStationTrigger Station;
        private readonly string vehicleId;
        internal WorkshopOverlay(AudiR8WorkshopButtons owner, GasStationOverlay source, GameInstance save, VehicleController vehicle, GasStationTrigger station)
        {
            this.owner = owner; Source = source; Save = save; Vehicle = vehicle; Station = station;
            vehicleId = vehicle.vehicleInstance.id;
        }
        Vector3 IOverlay.GetTargetPosition() => ((IOverlay)Source).GetTargetPosition();
        LabelInfo IOverlay.GetFirstLineLabel() => ((IOverlay)Source).GetFirstLineLabel();
        LabelInfo IOverlay.GetSecondLineLeftLabel() => ((IOverlay)Source).GetSecondLineLeftLabel();
        LabelInfo IOverlay.GetSecondLineRightLabel() => ((IOverlay)Source).GetSecondLineRightLabel();
        ButtonInfo[]? IOverlay.GetButtons()
        {
            var native = ((IOverlay)Source).GetButtons();
            if (native == null || !native.Any(b => b.name == "RepairVehicle") || !IsCurrentTarget()) return native;
            return ComposeButtons(native, CreateButton(Save, vehicleId, Mathf.Approximately(Vehicle.CurrentSpeed, 0f), Toggle), InvokeNative);
        }
        internal bool IsCurrentTarget() => owner.context != null && ReferenceEquals(owner.activeOverlay, this) &&
            IsEligible(Save, Vehicle, Station) && Vehicle.vehicleInstance.id == vehicleId && ReferenceEquals(StationField!.GetValue(Source), Station);
        private void Toggle()
        {
            if (!IsCurrentTarget() || owner.ui == null || !ReferenceEquals(OverlayField!.GetValue(owner.ui), this) ||
                !OverlayUI.IsVisible || !Mathf.Approximately(Vehicle.CurrentSpeed, 0f))
            { owner.WarnOnce("stale-action", $"stripe action rejected for vehicle='{vehicleId}'."); return; }
            var enabled = !AudiR8RacingStripes.GetRacingStripes(Save, vehicleId);
            AudiR8RacingStripes.SetRacingStripes(Save, vehicleId, enabled);
            owner.ui.RefreshButtons();
            AudiR8Diagnostics.StripesInfo(owner.context, $"AudiR8 workshop stripes vehicle='{vehicleId}': enabled={enabled}, cost=0.");
        }
        private void InvokeNative(Action action)
        {
            var ui = owner.ui;
            if (ui == null || !ReferenceEquals(OverlayField!.GetValue(ui), this)) return;
            OverlayField.SetValue(ui, Source);
            try { action(); }
            finally
            {
                if (OverlayUI.IsVisible && ReferenceEquals(OverlayField.GetValue(ui), Source) && IsCurrentTarget())
                { OverlayField.SetValue(ui, this); ui.RefreshButtons(); }
            }
        }
    }
}
