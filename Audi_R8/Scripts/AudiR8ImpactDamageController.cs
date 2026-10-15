#nullable enable
using BAModAPI;
using BusinessLayoutSets;
using NWH.VehiclePhysics2.Damage;
using UnityEngine;

// Keep saved condition and visual damage attached to a real impact. Native NWH
// accepts total relative speed even for a nearly tangential road contact.
internal sealed class AudiR8ImpactDamageController : MonoBehaviour
{
    private VehicleController? vehicle;
    private DamageHandler? handler;
    private ModContext? context;
    private Rigidbody? body;
    private float threshold, windowUntil, windowBaseline, strongestDamage;
    private int diagnosticCount;

    internal void Initialize(VehicleController controller, DamageHandler damageHandler,
        ModContext? modContext, float thresholdMps)
    {
        vehicle = controller; handler = damageHandler; context = modContext;
        body = controller.GetComponent<Rigidbody>(); threshold = thresholdMps;
        damageHandler.visualOnly = true;
    }

    internal static float ClosingSpeed(Collision collision)
    {
        var closing = 0f;
        for (var i = 0; i < collision.contactCount; i++)
        {
            var contact = collision.GetContact(i);
            // Flat surface support does not damage an upright car. Vertical
            // curb/barrier faces and contacts with another car still qualify.
            var other = collision.collider;
            var layer = other != null ? LayerMask.LayerToName(other.gameObject.layer) : "";
            var supportingSurface = collision.rigidbody == null && contact.normal.y > .65f &&
                (layer == "Ground" || layer == "GroundUnplacable" || layer == "Roads" ||
                 layer == "Crosswalk" || layer == "Sidewalk" || layer == "ParkingArea" ||
                 (other != null && other.name.IndexOf("road", System.StringComparison.OrdinalIgnoreCase) >= 0));
            if (supportingSurface) continue;
            closing = Mathf.Max(closing, NormalClosingSpeed(collision.relativeVelocity, contact.normal));
        }
        return closing;
    }

    internal static float NormalClosingSpeed(Vector3 relativeVelocity, Vector3 normal) =>
        Mathf.Max(0f, Vector3.Dot(relativeVelocity, normal.normalized));

    internal static float ConditionDamage(float closing, float mass, float otherMass,
        float fixedStep, float intensity, float thresholdMps)
    {
        if (closing < thresholdMps || mass <= 0f || intensity <= 0f) return 0f;
        var share = otherMass > 0f ? otherMass / (mass + otherMass) : 1f;
        // Half the previous 50 Hz score. Condition reflects the impact's
        // momentum, not the solver tick frequency; one crash must not become
        // more expensive solely because the physics timestep is shorter.
        return closing * share * Mathf.Clamp(intensity, 0f, .99f) * .0125f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (vehicle?.vehicleInstance == null || handler == null || body == null ||
            collision.contactCount == 0 ||
            (SaveGameManager.Current != null && SaveGameManager.Current.gameVariables.disableVehicleDamage)) return;
        var closing = ClosingSpeed(collision);
        if (closing < threshold)
        {
            if (AudiR8Diagnostics.DebugEnabled && AudiR8Diagnostics.CollisionDebugEnabled && diagnosticCount++ < 12)
                AudiR8Diagnostics.CollisionInfo(context,
                    $"AudiR8 ignored support/tangential contact vehicle={vehicle.GetInstanceID()} other='{collision.collider?.name}' relative={collision.relativeVelocity.magnitude:0.00} closing={closing:0.00}mps.");
            return;
        }

        var current = Mathf.Max(handler.Damage, vehicle.vehicleInstance.damage);
        if (Time.unscaledTime >= windowUntil || current < windowBaseline)
        { windowBaseline = current; strongestDamage = 0f; windowUntil = Time.unscaledTime + .8f; }
        var otherMass = collision.rigidbody != null && !collision.rigidbody.isKinematic
            ? collision.rigidbody.mass : 0f;
        // Score normal momentum/reduced mass once per impact window. Visual
        // deformation is tuned independently and retains its saved contacts.
        var expected = ConditionDamage(closing, body.mass, otherMass, Time.fixedDeltaTime, handler.damageIntensity, threshold);
        strongestDamage = Mathf.Max(strongestDamage, expected);
        var target = Mathf.Clamp01(Mathf.Max(current, windowBaseline + strongestDamage));
        if (target <= current + .0001f) return;
        if (vehicle is CarController car) car.SetDamage(target);
        else { handler.SetDamage(target); vehicle.vehicleInstance.damage = target; }
        GlobalEvents.onVehicleVariablesChanged?.Invoke();
        AudiR8Diagnostics.DamageInfo(context,
            $"AudiR8 scored impact vehicle={vehicle.GetInstanceID()} other='{collision.collider?.name}' closing={closing:0.00}mps baseline={windowBaseline:0.0000} rawImpactDamage={expected:0.0000} conditionDamage={target:0.0000}.");
    }
}
