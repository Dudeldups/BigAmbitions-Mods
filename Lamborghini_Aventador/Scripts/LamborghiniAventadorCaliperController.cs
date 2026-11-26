#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

[DefaultExecutionOrder(1000)]
internal sealed class LamborghiniAventadorCaliperController : MonoBehaviour
{
    private static readonly string[,] BindingNames =
    {
        { "Caliper_FL", "Wheel_FL" },
        { "Caliper_FR", "Wheel_FR" },
        { "Caliper_BL", "Wheel_BL" },
        { "Caliper_BR", "Wheel_BR" },
    };

    private readonly List<CaliperBinding> bindings = new List<CaliperBinding>(4);
    private VehicleController? vehicle;
    private ModContext? context;
    private bool warnedAboutInvalidSteering;

    internal void Initialize(VehicleController controller, ModContext? modContext)
    {
        var hasValidBindings = HasValidBindings(controller);
        vehicle = controller;
        context = modContext;

        if (hasValidBindings)
        {
            ApplyBindings();
            LamborghiniAventadorDiagnostics.CaliperInfo(
                context,
                $"LamborghiniAventador calipers reused cached bindings vehicle={controller.GetInstanceID()}, " +
                $"bindings={bindings.Count}, followsSteeringAndSuspension=true, inheritsWheelSpin=false.");
            return;
        }

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
        LamborghiniAventadorDiagnostics.CaliperInfo(
            context,
            $"LamborghiniAventador calipers ready vehicle={controller.GetInstanceID()}, " +
            $"bindings={bindings.Count}, followsSteeringAndSuspension=true, inheritsWheelSpin=false.");
    }

    private bool HasValidBindings(VehicleController controller)
    {
        if (vehicle != controller || bindings.Count != BindingNames.GetLength(0))
            return false;

        for (var index = 0; index < bindings.Count; index++)
        {
            var binding = bindings[index];
            if (binding.Pivot == null || binding.Wheel == null ||
                binding.Pivot.name != BindingNames[index, 0] ||
                binding.Wheel.name != BindingNames[index, 1] ||
                !binding.Pivot.IsChildOf(controller.transform) ||
                !binding.Wheel.IsChildOf(controller.transform))
                return false;
        }

        return true;
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
            // The wheel's right axis tracks steering without the tire's roll.
            // Rebuild from that axis so the caliper follows suspension and
            // steering while staying fixed relative to the chassis.
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
                        $"LamborghiniAventador caliper rejected steering angle={steeringAngle:0.0}deg " +
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
        internal CaliperBinding(Transform pivot, Transform wheel)
        {
            Pivot = pivot;
            Wheel = wheel;
        }

        internal Transform Pivot { get; }
        internal Transform Wheel { get; }
    }
}
