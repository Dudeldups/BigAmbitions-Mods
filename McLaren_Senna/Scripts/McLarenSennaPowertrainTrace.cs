#nullable enable
using BAModAPI;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

// Cache one vehicle and observe native lifecycle events; no state polling.
internal sealed class McLarenSennaPowertrainTrace : MonoBehaviour
{
    private VehicleController? vehicle;
    private PhysicsVehicle? physics;
    private ModContext? context;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        if (physics != null) return;
        vehicle = controller;
        context = modContext;
        physics = controller.GetComponent<PhysicsVehicle>();
        if (physics == null) return;
        physics.powertrain.engine.onStart.AddListener(Started);
        physics.powertrain.engine.onStop.AddListener(Stopped);
        physics.powertrain.transmission.onShift.AddListener(Shifted);
        physics.onEnable.AddListener(Enabled);
        physics.onDisable.AddListener(Disabled);
        Trace("configured");
    }

    private void Started() => Trace("engine-start");
    private void Stopped() => Trace("engine-stop");
    private void Shifted() => Trace("gear-shift");
    private void Enabled() => Trace("physics-enabled");
    private void Disabled() => Trace("physics-disabled");

    private void Trace(string transition)
    {
        if (physics == null || !McLarenSennaDiagnostics.DebugEnabled ||
            !McLarenSennaDiagnostics.DealerEntryDebugEnabled) return;
        var engine = physics.powertrain.engine;
        var transmission = physics.powertrain.transmission;
        McLarenSennaDiagnostics.DealerEntryInfo(context,
            $"McLarenSenna powertrain instance={vehicle?.GetInstanceID()} event={transition} " +
            $"controlled={vehicle?.controlledByPlayer} enabled={physics.enabled} active={physics.powertrain.IsActive} " +
            $"kinematic={physics.vehicleRigidbody?.isKinematic} constraints={physics.vehicleRigidbody?.constraints} " +
            $"running={engine.IsRunning} ignition={engine.ignition} canRun={engine.canRun} " +
            $"stalled={engine.IsStalled} starter={engine.starterActive} rpm={engine.OutputRPM:0} " +
            $"throttle={engine.ThrottlePosition:0.00} gear={transmission.Gear} ratio={transmission.currentGearRatio:0.000} " +
            $"clutch={physics.powertrain.clutch.Engagement:0.00} braking={physics.brakes.IsBraking} " +
            $"drivelineEfficiency={McLarenSennaPowertrainCalibration.DrivelineEfficiency:0.00} " +
            $"upshift={transmission.UpshiftRPM:0} downshift={transmission.DownshiftRPM:0} " +
            $"brakeTorque={physics.brakes.maxTorque * McLarenSennaPowertrainCalibration.BrakeMultiplier(physics.Speed * 3.6f):0}.");
    }

    private void OnDestroy()
    {
        if (physics == null) return;
        physics.powertrain.engine.onStart.RemoveListener(Started);
        physics.powertrain.engine.onStop.RemoveListener(Stopped);
        physics.powertrain.transmission.onShift.RemoveListener(Shifted);
        physics.onEnable.RemoveListener(Enabled);
        physics.onDisable.RemoveListener(Disabled);
        physics = null;
    }
}
