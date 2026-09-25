#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

[DefaultExecutionOrder(1000)]
internal sealed class Porsche911GT3RSCaliperController : MonoBehaviour
{
    private static readonly string[,] BindingNames =
    {
        { "PorscheFixedCaliperFrontLeft", "PorscheWheelFrontLeft" },
        { "PorscheFixedCaliperFrontRight", "PorscheWheelFrontRight" },
        { "PorscheFixedCaliperRearLeft", "PorscheWheelRearLeft" },
        { "PorscheFixedCaliperRearRight", "PorscheWheelRearRight" },
    };

    private readonly List<CaliperBinding> bindings = new List<CaliperBinding>(4);
    private VehicleController? vehicle;
    private ModContext? context;
    private bool warnedAboutInvalidSteering;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
        bindings.Clear();
        warnedAboutInvalidSteering = false;

        for (var index = 0; index < BindingNames.GetLength(0); index++)
        {
            var pivotName = BindingNames[index, 0];
            var wheelName = BindingNames[index, 1];
            var pivot = FindTransform(controller.transform, pivotName) ??
                        throw new InvalidOperationException($"Caliper pivot '{pivotName}' is missing.");
            var wheel = FindTransform(controller.transform, wheelName) ??
                        throw new InvalidOperationException($"Wheel visual '{wheelName}' is missing.");
            CenterPivotWithoutMovingGeometry(pivot, wheel, controller.transform.rotation);
            bindings.Add(new CaliperBinding(pivot, wheel));
        }

        ApplyBindings();
        Porsche911GT3RSDiagnostics.Info(
            context,
            $"Porsche911GT3RS steering calipers ready vehicle={controller.GetInstanceID()}, " +
            $"bindings={bindings.Count}, followsSteeringAndSuspension=true, inheritsWheelSpin=false.");
    }

    private void LateUpdate()
    {
        if (vehicle != null && bindings.Count == 4)
            ApplyBindings();
    }

    private void ApplyBindings()
    {
        var vehicleTransform = vehicle!.transform;
        var vehicleUp = vehicleTransform.up;
        foreach (var binding in bindings)
        {
            // Wheel roll happens around the visual's right axis, so that axis
            // retains steering while discarding spin. Rebuild the caliper pose
            // from it after NWH has updated the visual for this frame.
            var axle = Vector3.ProjectOnPlane(binding.Wheel.right, vehicleUp).normalized;
            if (axle.sqrMagnitude < 0.5f)
                continue;
            var steeringAngle = Vector3.SignedAngle(vehicleTransform.right, axle, vehicleUp);
            if (Mathf.Abs(steeringAngle) > 60f)
            {
                if (!warnedAboutInvalidSteering)
                {
                    warnedAboutInvalidSteering = true;
                    context?.Logger.Warn(
                        $"Porsche911GT3RS steering caliper rejected angle={steeringAngle:0.0}deg " +
                        $"wheel='{binding.Wheel.name}'.");
                }
                continue;
            }

            binding.Pivot.SetPositionAndRotation(
                binding.Wheel.position,
                vehicleTransform.rotation * Quaternion.Euler(0f, steeringAngle, 0f));
        }
    }

    private static void CenterPivotWithoutMovingGeometry(
        Transform pivot,
        Transform wheel,
        Quaternion chassisRotation)
    {
        var childPositions = new Vector3[pivot.childCount];
        var childRotations = new Quaternion[pivot.childCount];
        for (var index = 0; index < pivot.childCount; index++)
        {
            childPositions[index] = pivot.GetChild(index).position;
            childRotations[index] = pivot.GetChild(index).rotation;
        }

        pivot.SetPositionAndRotation(wheel.position, chassisRotation);
        for (var index = 0; index < pivot.childCount; index++)
            pivot.GetChild(index).SetPositionAndRotation(childPositions[index], childRotations[index]);
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        }
        return null;
    }

    private sealed class CaliperBinding
    {
        public CaliperBinding(Transform pivot, Transform wheel)
        {
            Pivot = pivot;
            Wheel = wheel;
        }

        public Transform Pivot { get; }
        public Transform Wheel { get; }
    }
}

