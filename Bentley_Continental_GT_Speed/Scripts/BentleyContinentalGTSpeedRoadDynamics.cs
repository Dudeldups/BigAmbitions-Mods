#nullable enable
using System;
using System.Collections;
using BAModAPI;
using NWH.WheelController3D;
using UnityEngine;
using PhysicsVehicle = NWH.VehiclePhysics2.VehicleController;

/// <summary>
/// Gives the Bentley additional longitudinal tire grip only while its native
/// NWH brakes are active, preserving the authored launch grip when accelerating.
/// </summary>
internal sealed class BentleyContinentalGTSpeedRoadDynamics : MonoBehaviour
{
    private const float BrakingForwardGrip = 0.56f;

    private PhysicsVehicle? physics;
    private ModContext? context;
    private WheelController[] wheels = Array.Empty<WheelController>();
    private float[] baselineForwardGrips = Array.Empty<float>();
    private Coroutine? reapplyCoroutine;
    private bool subscribed;
    private bool shuttingDown;
    private bool? brakingGripApplied;

    internal static BentleyContinentalGTSpeedRoadDynamics? Configure(
        GameObject vehicleRoot,
        ModContext? modContext)
    {
        if (vehicleRoot == null)
            throw new ArgumentNullException(nameof(vehicleRoot));

        var controller = vehicleRoot.GetComponent<PhysicsVehicle>() ??
                         vehicleRoot.GetComponentInChildren<PhysicsVehicle>(true);
        if (controller == null)
        {
            modContext?.Logger.Warn(
                $"BentleyContinentalGTSpeed drivetrain vehicle={vehicleRoot.GetInstanceID()}: " +
                "brake-grip setup could not find the NWH VehicleController.");
            return null;
        }

        var roadDynamics = vehicleRoot.GetComponent<BentleyContinentalGTSpeedRoadDynamics>();
        if (roadDynamics == null)
            roadDynamics = vehicleRoot.AddComponent<BentleyContinentalGTSpeedRoadDynamics>();
        if (roadDynamics.Initialize(controller, modContext))
            return roadDynamics;

        roadDynamics.Shutdown();
        Destroy(roadDynamics);
        return null;
    }

    // The editor physics fixture uses the same setup entry point as player vehicles.
    internal static Component? ConfigureForPhysicsFixture(GameObject vehicleRoot) =>
        Configure(vehicleRoot, null);

    internal bool Initialize(PhysicsVehicle controller, ModContext? modContext)
    {
        if (physics != null && physics != controller)
        {
            Unsubscribe();
            RestoreBaselineGrip("controller-rebind");
            StopReapplyCoroutine();
            wheels = Array.Empty<WheelController>();
            baselineForwardGrips = Array.Empty<float>();
            brakingGripApplied = null;
        }

        physics = controller;
        context = modContext;
        shuttingDown = false;
        if (wheels.Length == 0)
        {
            wheels = controller.GetComponentsInChildren<WheelController>(true);
            if (wheels.Length == 0)
            {
                context?.Logger.Warn(
                    $"BentleyContinentalGTSpeed drivetrain vehicle={controller.GetInstanceID()}: " +
                    "brake-grip setup found no NWH WheelController components.");
                return false;
            }

            baselineForwardGrips = new float[wheels.Length];
            for (var index = 0; index < wheels.Length; index++)
                baselineForwardGrips[index] = wheels[index].forwardFriction.grip;
        }

        Subscribe();
        ScheduleReapply();
        return true;
    }

    private void OnEnable()
    {
        if (physics == null || shuttingDown)
            return;
        Subscribe();
        ScheduleReapply();
    }

    private void OnDisable()
    {
        StopReapplyCoroutine();
        Unsubscribe();
        RestoreBaselineGrip("component-disabled");
    }

    private void OnDestroy() => Shutdown();

    internal void Shutdown()
    {
        if (shuttingDown)
            return;
        shuttingDown = true;
        StopReapplyCoroutine();
        Unsubscribe();
        RestoreBaselineGrip("mod-unload");
        physics = null;
        context = null;
        wheels = Array.Empty<WheelController>();
        baselineForwardGrips = Array.Empty<float>();
    }

    private void Subscribe()
    {
        if (!isActiveAndEnabled || physics == null || subscribed)
            return;

        physics.brakes.onBrakesActivate.RemoveListener(HandleBrakesActivated);
        physics.brakes.onBrakesActivate.AddListener(HandleBrakesActivated);
        physics.brakes.onBrakesDeactivate.RemoveListener(HandleBrakesDeactivated);
        physics.brakes.onBrakesDeactivate.AddListener(HandleBrakesDeactivated);
        physics.onVehicleInitialized.RemoveListener(HandleVehicleInitialized);
        physics.onVehicleInitialized.AddListener(HandleVehicleInitialized);
        physics.onEnable.RemoveListener(HandleVehicleEnabled);
        physics.onEnable.AddListener(HandleVehicleEnabled);
        subscribed = true;
        ApplyGrip(physics.brakes.IsBraking, "initialize");
    }

    private void Unsubscribe()
    {
        if (physics == null || !subscribed)
            return;

        physics.brakes.onBrakesActivate.RemoveListener(HandleBrakesActivated);
        physics.brakes.onBrakesDeactivate.RemoveListener(HandleBrakesDeactivated);
        physics.onVehicleInitialized.RemoveListener(HandleVehicleInitialized);
        physics.onEnable.RemoveListener(HandleVehicleEnabled);
        subscribed = false;
    }

    private void HandleBrakesActivated() => ApplyGrip(true, "native-brakes-activate");

    private void HandleBrakesDeactivated() => ApplyGrip(false, "native-brakes-deactivate");

    private void HandleVehicleInitialized() => ScheduleReapply();

    private void HandleVehicleEnabled() => ScheduleReapply();

    private void ScheduleReapply()
    {
        if (!isActiveAndEnabled || physics == null || shuttingDown)
            return;

        StopReapplyCoroutine();
        reapplyCoroutine = StartCoroutine(ReapplyAfterNativeLifecycle());
    }

    private IEnumerator ReapplyAfterNativeLifecycle()
    {
        // NWH initialization can reset wheel friction after its lifecycle events.
        yield return null;
        reapplyCoroutine = null;
        if (physics == null || shuttingDown || !isActiveAndEnabled)
            yield break;

        ApplyGrip(physics.brakes.IsBraking, "post-native-lifecycle");
    }

    private void StopReapplyCoroutine()
    {
        if (reapplyCoroutine == null)
            return;
        StopCoroutine(reapplyCoroutine);
        reapplyCoroutine = null;
    }

    private void ApplyGrip(bool braking, string reason)
    {
        if (wheels.Length != baselineForwardGrips.Length)
            return;

        for (var index = 0; index < wheels.Length; index++)
        {
            if (wheels[index] == null)
                continue;
            wheels[index].forwardFriction.grip =
                braking ? BrakingForwardGrip : baselineForwardGrips[index];
        }

        if (brakingGripApplied == braking)
            return;

        brakingGripApplied = braking;
        BentleyContinentalGTSpeedDiagnostics.DrivetrainInfo(
            context,
            $"brake grip vehicle={physics?.GetInstanceID()}, braking={braking}, " +
            $"forwardGrip={(braking ? BrakingForwardGrip.ToString("0.00") : "authored baseline")}, " +
            $"wheels={wheels.Length}, source='{reason}'.");
    }

    private void RestoreBaselineGrip(string reason)
    {
        if (wheels.Length != baselineForwardGrips.Length)
            return;
        ApplyGrip(false, reason);
    }
}
