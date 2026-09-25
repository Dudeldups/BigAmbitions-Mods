#nullable enable
using UnityEngine;

[DefaultExecutionOrder(1100)]
internal sealed class BMWM4G82SettlingController : MonoBehaviour
{
    private const float SettleDuration = 1.35f;
    private const float MaximumSettlingSpeed = 1.5f;
    private VehicleController? vehicle;
    private Rigidbody? body;
    private bool wasControlled;
    private float settleUntil;

    public void Initialize(VehicleController controller)
    {
        vehicle = controller;
        body = controller.GetComponent<Rigidbody>() ?? controller.GetComponentInParent<Rigidbody>();
        wasControlled = controller.controlledByPlayer;
        settleUntil = Time.unscaledTime + SettleDuration;
    }

    private void FixedUpdate()
    {
        if (vehicle == null || body == null)
            return;

        var controlled = vehicle.controlledByPlayer;
        if (wasControlled && !controlled)
            settleUntil = Time.unscaledTime + SettleDuration;
        wasControlled = controlled;

        if (controlled || body.isKinematic || Time.unscaledTime >= settleUntil)
            return;

        var velocity = body.velocity;
        var horizontalVelocity = Vector3.ProjectOnPlane(velocity, vehicle.transform.up);
        if (horizontalVelocity.sqrMagnitude > MaximumSettlingSpeed * MaximumSettlingSpeed)
            return;

        var verticalVelocity = Vector3.Project(velocity, vehicle.transform.up);
        var verticalSpeed = Vector3.Dot(velocity, vehicle.transform.up);
        if (Mathf.Abs(verticalSpeed) < 0.03f)
            verticalVelocity = Vector3.zero;
        else if (verticalSpeed > 0f)
            verticalVelocity *= 0.20f;
        body.velocity = horizontalVelocity + verticalVelocity;

        var yawVelocity = Vector3.Project(body.angularVelocity, vehicle.transform.up);
        body.angularVelocity = yawVelocity +
                               (body.angularVelocity - yawVelocity) * 0.25f;
    }
}
