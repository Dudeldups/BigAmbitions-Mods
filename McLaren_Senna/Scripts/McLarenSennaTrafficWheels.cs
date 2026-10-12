#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using GleyTrafficSystem;
using UnityEngine;

internal static class McLarenSennaTrafficWheels
{
    private const string HubPrefix = "McLarenTrafficHub_";

    internal static bool Configure(GameObject root, ModContext? context)
    {
        var vehicle = root.GetComponent<VehicleComponent>();
        if (vehicle == null || vehicle.allWheels == null || vehicle.allWheels.Length != 4)
            return Fail(context, "native four-wheel array unavailable");

        var plans = new List<WheelPlan>(4);
        var corners = new HashSet<string>(StringComparer.Ordinal);
        var alreadyBound = 0;
        foreach (var wheel in vehicle.allWheels)
        {
            if (wheel?.wheelGraphics == null || wheel.wheelTransform == null ||
                (wheel.wheelPosition != Wheel.WheelPosition.Front && wheel.wheelPosition != Wheel.WheelPosition.Back))
                return Fail(context, "native wheel transforms/axle unavailable");
            var front = wheel.wheelPosition == Wheel.WheelPosition.Front;
            var left = root.transform.InverseTransformPoint(wheel.wheelGraphics.position).x < 0f;
            var corner = (front ? "Front" : "Rear") + (left ? "Left" : "Right");
            if (!corners.Add(corner)) return Fail(context, "duplicate native corner " + corner);
            if (wheel.wheelGraphics.name == HubPrefix + corner)
            {
                alreadyBound++;
                continue;
            }
            var visual = Find(root.transform, "McLarenWheel" + corner);
            if (visual == null || !TryBounds(root.transform, visual, out var bounds))
                return Fail(context, "wheel geometry unavailable: " + corner);
            var radius = bounds.extents.y;
            if (radius < .25f || radius > .45f)
                return Fail(context, $"invalid measured radius {radius:0.00000}: {corner}");
            plans.Add(new WheelPlan(wheel, visual, corner, bounds.center, radius));
        }
        if (alreadyBound == 4) return true;
        if (alreadyBound != 0) return Fail(context, "partially bound native wheel rig");

        var frontZ = 0f;
        var rearZ = 0f;
        foreach (var plan in plans)
        {
            var hub = new GameObject(HubPrefix + plan.Corner).transform;
            hub.SetParent(root.transform, false);
            hub.localPosition = plan.Center;
            // A separate centered hub preserves the imported visual's axle,
            // reflection/rotation and scale. Gley alone moves/rotates this hub.
            plan.Visual.SetParent(hub, true);
            plan.Wheel.wheelGraphics = hub;
            var origin = root.transform.InverseTransformPoint(plan.Wheel.wheelTransform.position);
            origin.x = plan.Center.x;
            origin.z = plan.Center.z;
            plan.Wheel.wheelTransform.position = root.transform.TransformPoint(origin);
            plan.Wheel.wheelRadius = plan.Radius;
            plan.Wheel.wheelCircumference = 2f * Mathf.PI * plan.Radius;
            plan.Wheel.raycastLength = plan.Radius + plan.Wheel.maxSuspension;
            if (plan.Wheel.wheelPosition == Wheel.WheelPosition.Front) frontZ += plan.Center.z;
            else rearZ += plan.Center.z;
            McLarenSennaDiagnostics.TrafficInfo($"McLarenSenna NPC wheel: corner={plan.Corner}, " +
                $"center={plan.Center}, radius={plan.Radius:0.00000}, origin={origin}, graphics='{hub.name}'.");
        }
        vehicle.wheelDistance = Mathf.Abs((frontZ - rearZ) * .5f);
        return true;
    }

    private static bool Fail(ModContext? context, string reason)
    {
        var message = "McLarenSenna: NPC wheel preparation failed: " + reason;
        if (context != null) context.Logger.Warn(message); else Debug.LogWarning(message);
        return false;
    }

    private static Transform? Find(Transform root, string name)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            if (node.name == name) return node;
        return null;
    }

    private static bool TryBounds(Transform root, Transform visual, out Bounds bounds)
    {
        bounds = default;
        var found = false;
        foreach (var filter in visual.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var local = filter.sharedMesh.bounds;
            for (var index = 0; index < 8; index++)
            {
                var point = local.center + Vector3.Scale(local.extents,
                    new Vector3((index & 1) == 0 ? -1f : 1f, (index & 2) == 0 ? -1f : 1f, (index & 4) == 0 ? -1f : 1f));
                point = root.InverseTransformPoint(filter.transform.TransformPoint(point));
                if (found) bounds.Encapsulate(point); else { bounds = new Bounds(point, Vector3.zero); found = true; }
            }
        }
        return found;
    }

    private readonly struct WheelPlan
    {
        internal WheelPlan(Wheel wheel, Transform visual, string corner, Vector3 center, float radius)
        { Wheel = wheel; Visual = visual; Corner = corner; Center = center; Radius = radius; }
        internal readonly Wheel Wheel;
        internal readonly Transform Visual;
        internal readonly string Corner;
        internal readonly Vector3 Center;
        internal readonly float Radius;
    }
}
