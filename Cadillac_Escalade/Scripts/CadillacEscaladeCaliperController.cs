#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using NWH.WheelController3D;
using UnityEngine;

[DefaultExecutionOrder(1000)]
internal sealed class CadillacEscaladeCaliperController : MonoBehaviour
{
    private static readonly string[,] BindingNames =
    {
        { "CadillacFixedCaliperFrontLeft", "FrontLeft_WheelController" },
        { "CadillacFixedCaliperFrontRight", "FrontRight_WheelController" },
        { "CadillacFixedCaliperRearLeft", "RearLeft_WheelController" },
        { "CadillacFixedCaliperRearRight", "RearRight_WheelController" },
    };

    private readonly List<CaliperBinding> bindings = new List<CaliperBinding>(4);
    private VehicleController? vehicle;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        bindings.Clear();

        for (var index = 0; index < BindingNames.GetLength(0); index++)
        {
            var pivotName = BindingNames[index, 0];
            var controllerName = BindingNames[index, 1];
            var pivot = FindTransform(controller.transform, pivotName) ??
                        throw new InvalidOperationException($"Caliper pivot '{pivotName}' is missing.");
            var controllerTransform = FindTransform(controller.transform, controllerName) ??
                                      throw new InvalidOperationException(
                                          $"Wheel controller '{controllerName}' is missing.");
            var wheelController = controllerTransform.GetComponent<WheelController>() ??
                                  throw new InvalidOperationException(
                                      $"Wheel controller component on '{controllerName}' is missing.");
            bindings.Add(new CaliperBinding(pivot, wheelController));
        }

        ApplyBindings();
        CadillacEscaladeDiagnostics.Info(modContext,
            $"CadillacEscalade steering brake visuals ready vehicle={controller.GetInstanceID()}, " +
            $"calipers={bindings.Count}, rotors=rolling-wheel-children.");
    }

    private void LateUpdate()
    {
        if (vehicle != null && bindings.Count == 4)
            ApplyBindings();
    }

    private void ApplyBindings()
    {
        foreach (var binding in bindings)
        {
            // NWH calculates these basis vectors from its steering, camber and
            // suspension pose before applying axle spin to the rolling visual.
            var wheel = binding.Controller.wheel;
            if (wheel.forward.sqrMagnitude < 0.5f || wheel.up.sqrMagnitude < 0.5f)
                continue;
            binding.Pivot.SetPositionAndRotation(
                binding.Controller.WheelPosition,
                Quaternion.LookRotation(wheel.forward, wheel.up));
        }
    }

    private static Transform? FindTransform(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform;
        return null;
    }

    private sealed class CaliperBinding
    {
        public CaliperBinding(Transform pivot, WheelController controller)
        {
            Pivot = pivot;
            Controller = controller;
        }

        public Transform Pivot { get; }
        public WheelController Controller { get; }
    }
}
