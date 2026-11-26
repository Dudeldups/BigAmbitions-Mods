#nullable enable
using System;
using System.Reflection;
using NWH.WheelController3D;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Checks the authored NWH suspension pivot against the actual native spring
/// and wheel-visual update methods at a level, grounded static pose.
/// </summary>
public static class LamborghiniAventadorSuspensionGeometryRegression
{
    private const string PrefabPath = "Assets/Mods/Lamborghini_Aventador/LamborghiniAventador.prefab";
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const float Gravity = 9.81f;

    [MenuItem("Big Ambitions Mods/Lamborghini Aventador/Verify Suspension Geometry")]
    public static void VerifySuspensionGeometry()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new InvalidOperationException("Aventador suspension geometry regression could not load its prefab.");

        var instance = UnityEngine.Object.Instantiate(prefab);
        instance.name = "AventadorSuspensionGeometryRegressionVehicle";
        try
        {
            var wheels = instance.GetComponentsInChildren<WheelController>(true);
            if (wheels.Length != 4)
                throw new InvalidOperationException($"Expected four native wheel controllers, found {wheels.Length}.");

            // Start initializes the native wheel/collider transforms, visual
            // binding, spring length, and private transform cache that the
            // real Step -> UpdateWheelValues path expects.
            foreach (var wheel in wheels)
                InvokeNative(wheel, "Start");

            var body = wheels[0].GetComponentInParent<Rigidbody>();
            if (body == null)
                throw new InvalidOperationException("Aventador suspension geometry regression could not find the native vehicle Rigidbody.");

            var frontZ = float.NegativeInfinity;
            var rearZ = float.PositiveInfinity;
            foreach (var wheel in wheels)
            {
                var localZ = body.transform.InverseTransformPoint(wheel.transform.position).z;
                if (wheel.transform.name.StartsWith("Front", StringComparison.Ordinal))
                    frontZ = Mathf.Max(frontZ, localZ);
                else if (wheel.transform.name.StartsWith("Rear", StringComparison.Ordinal))
                    rearZ = Mathf.Min(rearZ, localZ);
                else
                    throw new InvalidOperationException($"Unexpected wheel controller name '{wheel.transform.name}'.");
            }

            var wheelbase = frontZ - rearZ;
            if (wheelbase < 2f)
                throw new InvalidOperationException($"Unexpected Aventador wheelbase {wheelbase:0.000} m.");

            var gravity = Mathf.Abs(Physics.gravity.y);
            if (gravity < 1f)
                gravity = Gravity;
            var centerOfMassZ = body.centerOfMass.z;
            var frontCornerLoad = body.mass * gravity * (centerOfMassZ - rearZ) / wheelbase * 0.5f;
            var rearCornerLoad = body.mass * gravity * (frontZ - centerOfMassZ) / wheelbase * 0.5f;
            if (frontCornerLoad <= 0f || rearCornerLoad <= 0f)
                throw new InvalidOperationException(
                    $"Static axle loads are invalid: front={frontCornerLoad:0.0} N, rear={rearCornerLoad:0.0} N, COM.z={centerOfMassZ:0.000} m.");

            foreach (var wheel in wheels)
            {
                var isFront = wheel.transform.name.StartsWith("Front", StringComparison.Ordinal);
                var expectedCornerLoad = isFront ? frontCornerLoad : rearCornerLoad;
                var visual = wheel.wheel.visual != null
                    ? wheel.wheel.visual.transform
                    : throw new InvalidOperationException($"Wheel '{wheel.transform.name}' has no rolling visual.");
                var visualCenter = body.transform.InverseTransformPoint(visual.position);
                var expectedAnchorY = visualCenter.y + RestExtension(wheel.spring, expectedCornerLoad);
                AssertNear(wheel.transform.localPosition.y, expectedAnchorY, 0.001f,
                    $"'{wheel.transform.name}' controller pivot must include its static spring extension");

                RunNativeGroundedWheelUpdate(wheel, body, expectedCornerLoad);

                AssertNear(wheel.spring.force, expectedCornerLoad, Mathf.Max(75f, expectedCornerLoad * 0.025f),
                    $"'{wheel.transform.name}' native spring force does not support its static corner load");
                AssertNear(wheel.Load, expectedCornerLoad, Mathf.Max(75f, expectedCornerLoad * 0.025f),
                    $"'{wheel.transform.name}' native wheel load does not match its static corner load");

                var expectedVisual = body.transform.TransformPoint(
                    new Vector3(visualCenter.x, visualCenter.y, visualCenter.z));
                var nativeWheelCenter = wheel.transform.TransformPoint(ReadLocalWheelPosition(wheel));
                AssertNear(nativeWheelCenter.y, expectedVisual.y, 0.001f,
                    $"'{wheel.transform.name}' native wheel center moved away from the authored tire center");
                AssertNear(visual.position.y, expectedVisual.y, 0.001f,
                    $"'{wheel.transform.name}' native visual update buried or lifted the tire center");
            }

            Debug.Log(
                $"Aventador suspension geometry regression passed using native NWH spring/visual updates: " +
                $"front={frontCornerLoad:0} N/corner, rear={rearCornerLoad:0} N/corner, wheelbase={wheelbase:0.000} m.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static float RestExtension(Spring spring, float staticLoad)
    {
        if (spring.maxForce <= 0f || spring.maxLength <= 0f || staticLoad >= spring.maxForce)
            throw new InvalidOperationException(
                $"Spring cannot support static load {staticLoad:0.0} N with maxForce={spring.maxForce:0.0} N.");

        var normalizedLoad = staticLoad / spring.maxForce;
        var curveLoad = spring.forceCurve.Evaluate(normalizedLoad);
        if (Mathf.Abs(curveLoad - normalizedLoad) > 0.001f)
            throw new InvalidOperationException(
                $"Expected the authored linear NWH spring curve, got curve({normalizedLoad:0.000})={curveLoad:0.000}.");

        return spring.maxLength * (1f - normalizedLoad);
    }

    private static void RunNativeGroundedWheelUpdate(
        WheelController wheel,
        Rigidbody body,
        float expectedCornerLoad)
    {
        var spring = wheel.spring;
        var extension = RestExtension(spring, expectedCornerLoad);
        var wantedLength = extension;
        var wantedLocalCenter = -wantedLength;
        var localControllerPosition = wheel.transform.localPosition;
        var visualCenter = body.transform.InverseTransformPoint(wheel.wheel.visual.transform.position);
        var radius = new SerializedObject(wheel).FindProperty("wheel.radius").floatValue;
        var groundYInVehicle = visualCenter.y - radius;
        AssertNear(groundYInVehicle, -LamborghiniAventadorSetup.WheelCenterLowering, 0.001f,
            $"'{wheel.name}' tire bottom does not share the authored ground plane");
        var localGroundY = groundYInVehicle - localControllerPosition.y;

        // Give the native routine the wheel's post-hit state for a flat ground
        // contact at the measured common tire-bottom plane and zero tire slip/
        // vertical velocity. Lowering the hubs places this plane below root Y.
        WriteLocalWheelPosition(wheel, new Vector3(0f, wantedLocalCenter, 0f));
        spring.prevLength = wantedLength;
        spring.compressionVelocity = 0f;
        spring.prevVelocity = 0f;

        SetField(wheel, "_isGrounded", true);
        SetField(wheel, "_dt", Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f);
        SetField(wheel, "_hitLocalPoint", new Vector3(0f, localGroundY, 0f));
        SetField(wheel, "_transformPosition", wheel.transform.position);
        SetField(wheel, "_transformRotation", wheel.transform.rotation);
        SetField(wheel, "_transformUp", wheel.transform.up);
        SetField(wheel, "_transform", wheel.transform);
        SetField(wheel, "_upVector", Vector3.up);
        SetField(wheel, "_forwardVector", Vector3.forward);
        SetField(wheel, "_rightVector", Vector3.right);
        SetField(wheel, "_worldBaseRotation", wheel.transform.rotation);
        SetField(wheel, "_localSteerRotation", Quaternion.identity);
        SetField(wheel, "_localAxleRotation", Quaternion.identity);
        SetField(wheel, "_localDamageRotation", Quaternion.identity);
        SetWheelStateField(wheel, "visualTransform", wheel.wheel.visual.transform);

        var hitField = typeof(WheelController).GetField("wheelHit", InstanceMembers)
            ?? throw new MissingFieldException(typeof(WheelController).FullName, "wheelHit");
        var hit = Activator.CreateInstance(hitField.FieldType)
            ?? throw new InvalidOperationException("Could not create native NWH WheelHit state.");
        hitField.FieldType.GetField("normal", InstanceMembers)?.SetValue(hit, wheel.transform.up);
        hitField.FieldType.GetField("point", InstanceMembers)?.SetValue(
            hit,
            body.transform.TransformPoint(new Vector3(visualCenter.x, groundYInVehicle, visualCenter.z)));
        hitField.SetValue(wheel, hit);

        SetField(wheel, "targetRigidbody", body);
        InvokeNative(wheel, "UpdateSpringAndDamper");
        InvokeNative(wheel, "UpdateWheelValues");
    }

    private static Vector3 ReadLocalWheelPosition(WheelController wheel)
    {
        var wheelField = typeof(WheelController).GetField("wheel", InstanceMembers)
            ?? throw new MissingFieldException(typeof(WheelController).FullName, "wheel");
        var wheelState = wheelField.GetValue(wheel)
            ?? throw new InvalidOperationException($"Native wheel state missing on '{wheel.name}'.");
        var localPositionField = wheelState.GetType().GetField("localPosition", InstanceMembers)
            ?? throw new MissingFieldException(wheelState.GetType().FullName, "localPosition");
        return (Vector3)localPositionField.GetValue(wheelState)!;
    }

    private static void WriteLocalWheelPosition(WheelController wheel, Vector3 position)
    {
        var wheelField = typeof(WheelController).GetField("wheel", InstanceMembers)
            ?? throw new MissingFieldException(typeof(WheelController).FullName, "wheel");
        var wheelState = wheelField.GetValue(wheel)
            ?? throw new InvalidOperationException($"Native wheel state missing on '{wheel.name}'.");
        var localPositionField = wheelState.GetType().GetField("localPosition", InstanceMembers)
            ?? throw new MissingFieldException(wheelState.GetType().FullName, "localPosition");
        localPositionField.SetValue(wheelState, position);
        wheelField.SetValue(wheel, wheelState);
    }

    private static void SetWheelStateField(WheelController wheel, string fieldName, object value)
    {
        var wheelField = typeof(WheelController).GetField("wheel", InstanceMembers)
            ?? throw new MissingFieldException(typeof(WheelController).FullName, "wheel");
        var wheelState = wheelField.GetValue(wheel)
            ?? throw new InvalidOperationException($"Native wheel state missing on '{wheel.name}'.");
        var field = wheelState.GetType().GetField(fieldName, InstanceMembers)
            ?? throw new MissingFieldException(wheelState.GetType().FullName, fieldName);
        field.SetValue(wheelState, value);
        wheelField.SetValue(wheel, wheelState);
    }

    private static void InvokeNative(WheelController wheel, string methodName)
    {
        var method = typeof(WheelController).GetMethod(methodName, InstanceMembers)
            ?? throw new MissingMethodException(typeof(WheelController).FullName, methodName);
        method.Invoke(wheel, null);
    }

    private static void SetField(WheelController wheel, string fieldName, object value)
    {
        var field = typeof(WheelController).GetField(fieldName, InstanceMembers)
            ?? throw new MissingFieldException(typeof(WheelController).FullName, fieldName);
        field.SetValue(wheel, value);
    }

    private static void AssertNear(float actual, float expected, float tolerance, string message)
    {
        if (float.IsNaN(actual) || float.IsInfinity(actual) || Mathf.Abs(actual - expected) > tolerance)
            throw new InvalidOperationException(
                $"Aventador suspension geometry regression {message}: expected={expected:0.0000}, " +
                $"actual={actual:0.0000}, tolerance={tolerance:0.0000}.");
    }
}
