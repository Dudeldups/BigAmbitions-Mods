#nullable enable
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Vehicles.VehicleTypes;

/// <summary>Editor regression coverage for cached Aventador caliper pose bindings.</summary>
public static class LamborghiniAventadorCaliperRegression
{
    private const string PrefabPath = "Assets/Mods/Lamborghini_Aventador/LamborghiniAventador.prefab";
    private static readonly string[] WheelNames = { "Wheel_FL", "Wheel_FR", "Wheel_BL", "Wheel_BR" };
    private static readonly string[] CaliperNames = { "Caliper_FL", "Caliper_FR", "Caliper_BL", "Caliper_BR" };

    [MenuItem("Big Ambitions Mods/Lamborghini Aventador/Verify Caliper Steering and Spin")]
    public static void VerifySteeringAndSpinIndependence()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Aventador caliper regression could not load its prefab.");

        var instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "AventadorCaliperRegressionInstance";
        try
        {
            var vehicle = instance.GetComponent<VehicleController>();
            if (vehicle == null)
                throw new InvalidOperationException("Aventador caliper regression prefab has no VehicleController.");

            var controllerType = typeof(LamborghiniAventadorMod).Assembly
                .GetType("LamborghiniAventadorCaliperController", true)!;
            var controller = instance.GetComponent(controllerType) ?? instance.AddComponent(controllerType);
            var initialize = controllerType.GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var applyBindings = controllerType.GetMethod("ApplyBindings", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var wheels = new Transform[WheelNames.Length];
            var calipers = new Transform[CaliperNames.Length];
            for (var index = 0; index < wheels.Length; index++)
            {
                wheels[index] = Find(instance.transform, WheelNames[index]);
                calipers[index] = Find(instance.transform, CaliperNames[index]);
                if (wheels[index] == null || calipers[index] == null)
                    throw new InvalidOperationException(
                        $"Aventador caliper regression is missing corner roots wheel={WheelNames[index]} caliper={CaliperNames[index]}.");
            }

            foreach (var wheel in wheels)
            {
                var axisAlignment = Vector3.Dot(wheel.right, instance.transform.right);
                if (axisAlignment < 0.9f)
                    throw new InvalidOperationException(
                        $"Aventador authored wheel axis points opposite/away from vehicle right: wheel={wheel.name} dot={axisAlignment:0.000}.");
            }

            initialize.Invoke(controller, new object?[] { vehicle, null });
            var geometryPositions = CaptureDirectChildPositions(calipers);
            initialize.Invoke(controller, new object?[] { vehicle, null });
            AssertDirectChildPositions(calipers, geometryPositions, "repeat initialization moved caliper geometry");

            instance.transform.SetPositionAndRotation(
                new Vector3(4.3f, 1.7f, -2.2f), Quaternion.Euler(13f, 47f, -8f));
            var vehicleRotation = instance.transform.rotation;
            var vehicleUp = instance.transform.up;
            var suspensionOffsets = new[] { 0.04f, 0.07f, -0.03f, -0.06f };
            for (var corner = 0; corner < wheels.Length; corner++)
                wheels[corner].position += vehicleUp * suspensionOffsets[corner];

            foreach (var steeringAngle in new[] { 30f, -30f })
            {
                for (var corner = 0; corner < wheels.Length; corner++)
                {
                    var isFront = corner < 2;
                    var cornerSteering = isFront ? steeringAngle : 0f;
                    wheels[corner].rotation = vehicleRotation * Quaternion.Euler(0f, cornerSteering, 0f);
                }

                applyBindings.Invoke(controller, null);
                for (var corner = 0; corner < wheels.Length; corner++)
                {
                    AssertPosition(calipers[corner].position, wheels[corner].position,
                        $"caliper did not follow wheel suspension position corner={CaliperNames[corner]}");
                    var cornerSteering = corner < 2 ? steeringAngle : 0f;
                    var expected = vehicleRotation * Quaternion.Euler(0f, cornerSteering, 0f);
                    AssertRotation(calipers[corner].rotation, expected,
                        $"caliper did not follow steering corner={CaliperNames[corner]} angle={cornerSteering:0}");
                }

                var steeredGeometryPositions = CaptureDirectChildPositions(calipers);
                var steeredGeometryRotations = CaptureDirectChildRotations(calipers);
                initialize.Invoke(controller, new object?[] { vehicle, null });
                AssertDirectChildPositions(calipers, steeredGeometryPositions,
                    $"reinitialization at {steeringAngle:0} degrees steering shifted caliper geometry");
                AssertDirectChildRotations(calipers, steeredGeometryRotations,
                    $"reinitialization at {steeringAngle:0} degrees steering rotated caliper geometry");

                var steeringOnlyRotations = new Quaternion[calipers.Length];
                for (var corner = 0; corner < wheels.Length; corner++)
                {
                    steeringOnlyRotations[corner] = calipers[corner].rotation;
                    var cornerSteering = corner < 2 ? steeringAngle : 0f;
                    wheels[corner].rotation = vehicleRotation * Quaternion.Euler(0f, cornerSteering, 0f) *
                                               Quaternion.AngleAxis(120f, Vector3.right);
                }

                applyBindings.Invoke(controller, null);
                for (var corner = 0; corner < calipers.Length; corner++)
                    AssertRotation(calipers[corner].rotation, steeringOnlyRotations[corner],
                        $"wheel spin rotated caliper corner={CaliperNames[corner]} steering={steeringAngle:0}");
            }

            Debug.Log("Aventador caliper regression passed: repeat init, ±30° steering, suspension travel, yawed vehicle orientation, and 120° wheel spin independence.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(transform.name, name, StringComparison.Ordinal)) return transform;
        throw new InvalidOperationException($"Aventador caliper regression is missing transform '{name}'.");
    }

    private static Vector3[][] CaptureDirectChildPositions(Transform[] pivots)
    {
        var result = new Vector3[pivots.Length][];
        for (var pivotIndex = 0; pivotIndex < pivots.Length; pivotIndex++)
        {
            var pivot = pivots[pivotIndex];
            result[pivotIndex] = new Vector3[pivot.childCount];
            for (var childIndex = 0; childIndex < pivot.childCount; childIndex++)
                result[pivotIndex][childIndex] = pivot.GetChild(childIndex).position;
        }
        return result;
    }

    private static Quaternion[][] CaptureDirectChildRotations(Transform[] pivots)
    {
        var result = new Quaternion[pivots.Length][];
        for (var pivotIndex = 0; pivotIndex < pivots.Length; pivotIndex++)
        {
            var pivot = pivots[pivotIndex];
            result[pivotIndex] = new Quaternion[pivot.childCount];
            for (var childIndex = 0; childIndex < pivot.childCount; childIndex++)
                result[pivotIndex][childIndex] = pivot.GetChild(childIndex).rotation;
        }
        return result;
    }

    private static void AssertDirectChildPositions(Transform[] pivots, Vector3[][] expected, string message)
    {
        for (var pivotIndex = 0; pivotIndex < pivots.Length; pivotIndex++)
        {
            if (pivots[pivotIndex].childCount != expected[pivotIndex].Length)
                throw new InvalidOperationException($"Aventador caliper regression {message}: child count changed.");
            for (var childIndex = 0; childIndex < expected[pivotIndex].Length; childIndex++)
                AssertPosition(pivots[pivotIndex].GetChild(childIndex).position, expected[pivotIndex][childIndex],
                    $"Aventador caliper regression {message}: pivot={pivots[pivotIndex].name} child={childIndex}");
        }
    }

    private static void AssertDirectChildRotations(Transform[] pivots, Quaternion[][] expected, string message)
    {
        for (var pivotIndex = 0; pivotIndex < pivots.Length; pivotIndex++)
        {
            if (pivots[pivotIndex].childCount != expected[pivotIndex].Length)
                throw new InvalidOperationException($"Aventador caliper regression {message}: child count changed.");
            for (var childIndex = 0; childIndex < expected[pivotIndex].Length; childIndex++)
                AssertRotation(pivots[pivotIndex].GetChild(childIndex).rotation, expected[pivotIndex][childIndex],
                    $"Aventador caliper regression {message}: pivot={pivots[pivotIndex].name} child={childIndex}");
        }
    }

    private static void AssertPosition(Vector3 actual, Vector3 expected, string message)
    {
        if (Vector3.Distance(actual, expected) > 0.001f)
            throw new InvalidOperationException($"Aventador caliper regression {message}: expected={expected} actual={actual}.");
    }

    private static void AssertRotation(Quaternion actual, Quaternion expected, string message)
    {
        var difference = Quaternion.Angle(actual, expected);
        if (difference > 0.25f)
            throw new InvalidOperationException($"Aventador caliper regression {message}: difference={difference:0.000}deg.");
    }
}
