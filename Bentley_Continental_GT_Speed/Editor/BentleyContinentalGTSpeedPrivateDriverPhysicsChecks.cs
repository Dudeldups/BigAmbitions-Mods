#nullable enable
using System;
using GleyTrafficSystem;
using UnityEngine;

/// <summary>Compares traffic physics before and after fitting the Bentley visuals.</summary>
public static class BentleyContinentalGTSpeedPrivateDriverPhysicsChecks
{
    public static void Verify(GameObject template, GameObject bentley)
    {
        var stock = template.GetComponent<VehicleComponent>();
        var fitted = bentley.GetComponent<VehicleComponent>();
        if (stock.wheelDistance != fitted.wheelDistance)
            throw new InvalidOperationException("Bentley traffic changed the native wheelbase.");
        for (var i = 0; i < stock.allWheels.Length; i++)
        {
            var original = stock.allWheels[i];
            var copy = fitted.allWheels[i];
            if (original.wheelTransform.localPosition != copy.wheelTransform.localPosition ||
                original.wheelRadius != copy.wheelRadius ||
                original.raycastLength != copy.raycastLength ||
                original.maxSuspension != copy.maxSuspension ||
                original.wheelGraphics.name != copy.wheelGraphics.name)
                throw new InvalidOperationException($"Bentley traffic changed the native suspension at corner {i}.");
        }
        var stockBody = template.GetComponent<Rigidbody>();
        var bentleyBody = bentley.GetComponent<Rigidbody>();
        if (stockBody.mass != bentleyBody.mass || stockBody.constraints != bentleyBody.constraints)
            throw new InvalidOperationException("Bentley traffic changed the native rigidbody contract.");

        var stockCollider = template.GetComponentInChildren<MeshCollider>(true);
        var fittedCollider = bentley.GetComponentInChildren<MeshCollider>(true);
        if (stockCollider == null || fittedCollider == null || !fittedCollider.enabled ||
            stockCollider.sharedMesh != fittedCollider.sharedMesh)
            throw new InvalidOperationException("Bentley traffic did not retain the native chassis collider.");
        Debug.Log("Bentley NPC chassis matches the stock Gley wheelbase, suspension and collision geometry.");
    }
}
