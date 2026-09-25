#nullable enable
using System;
using System.Collections.Generic;
using Helpers;
using UnityEngine;

// Lives on the Camaro runtime object. No global vehicle/mesh scans.
// A selected-vehicle pointer check covers purchases without an enter event.
[DisallowMultipleComponent]
public sealed class ChevroletCamaro1967DamageTraceCoordinator : MonoBehaviour
{
    private readonly List<ChevroletCamaro1967DamageTrace> owned = new List<ChevroletCamaro1967DamageTrace>();
    private VehicleController? previousSelection;
    private ChevroletCamaro1967DamageTrace? activeTrace;
    private bool failureReported;

    private void OnEnable()
    {
        GlobalEvents.onEnterVehicle += HandleEntered;
        GlobalEvents.onExitVehicle += HandleExited;
        GlobalEvents.onGameUnloaded += HandleUnloaded;
    }

    private void Update()
    {
        var selected = InstanceBehavior<GameManager>.Instance?.selectedVehicle;
        if (selected == previousSelection) return;
        previousSelection = selected;
        Observe(selected);
    }

    private void HandleEntered(VehicleController controller) => Observe(controller);

    private void HandleExited(VehicleController controller)
    {
        if (controller == null || activeTrace == null || activeTrace.gameObject != controller.gameObject) return;
        activeTrace.EndSession("vehicle-exited");
        activeTrace.enabled = false;
        activeTrace = null;
        previousSelection = null;
    }

    private void Observe(VehicleController? controller)
    {
        try
        {
            if (activeTrace != null && (controller == null || activeTrace.gameObject != controller.gameObject))
            {
                activeTrace.EndSession("selection-changed");
                activeTrace.enabled = false;
                activeTrace = null;
            }
            if (controller?.vehicleInstance == null || !string.Equals(controller.vehicleInstance.vehicleTypeName,
                    ChevroletCamaro1967Mod.VehicleTypeName, StringComparison.Ordinal)) return;
            var trace = controller.GetComponent<ChevroletCamaro1967DamageTrace>();
            if (trace == null)
            {
                trace = controller.gameObject.AddComponent<ChevroletCamaro1967DamageTrace>();
                owned.Add(trace);
            }
            trace.Initialize(controller);
            trace.enabled = true;
            activeTrace = trace;
        }
        catch (Exception exception)
        {
            if (failureReported) return;
            failureReported = true;
            Debug.LogWarning("[CamaroDamageTrace] Attach failed; vehicle unchanged: " + exception);
        }
    }

    private void HandleUnloaded()
    {
        foreach (var trace in owned)
        {
            if (trace == null) continue;
            trace.EndSession("game-or-mod-unloaded");
            trace.enabled = false;
            Destroy(trace);
        }
        owned.Clear();
        activeTrace = null;
        previousSelection = null;
    }

    private void OnDisable()
    {
        GlobalEvents.onEnterVehicle -= HandleEntered;
        GlobalEvents.onExitVehicle -= HandleExited;
        GlobalEvents.onGameUnloaded -= HandleUnloaded;
        HandleUnloaded();
    }
}
