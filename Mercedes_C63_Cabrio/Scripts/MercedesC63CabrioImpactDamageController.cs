#nullable enable
using System;
using System.Collections;
using System.Reflection;
using BAModAPI;
using BigAmbitions.SaveSystem;
using NWH.VehiclePhysics2.Damage;
using UnityEngine;

// Physics samples support collision evidence; no state discovery or scene searches.
[AddComponentMenu("")]
internal sealed class MercedesC63CabrioImpactDamageController : MonoBehaviour
{
    private const float ContactWindow = .12f;
    private const float MinimumNormalSpeed = 2f;
    private int streetPropsLayer = -1;
    private Rigidbody? body;
    private CarController? vehicle;
    private DamageHandler? handler;
    private ModContext? context;
    private Action? synchronizeNativeDamage;
    private Vector3 beforeVelocity;
    private float beforeDamage, sampleTime = float.NegativeInfinity;
    private int pendingCollider;
    private float pendingUntil, pendingBaseline, pendingSpeed;
    private float requiredDamage, requiredBaseline;
    private string impactTarget = "";
    private Coroutine? commit;
    private int diagnosticCount;

    internal void Initialize(VehicleController controller, DamageHandler damage, ModContext? modContext)
    {
        if (vehicle != null) return;
        vehicle = controller as CarController;
        body = controller.GetComponent<Rigidbody>();
        handler = damage; context = modContext;
        streetPropsLayer = LayerMask.NameToLayer("StreetProps");
        var synchronize = typeof(CarController).GetMethod("OnVehicleCollision",
            BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (vehicle == null || body == null || synchronize == null)
        {
            context?.Logger.Warn("MercedesC63Cabrio impact reconciliation unavailable: native car/physics/synchronization missing.");
            enabled = false; return;
        }
        synchronizeNativeDamage = (Action)Delegate.CreateDelegate(typeof(Action),vehicle,synchronize);
    }

    private bool DamageAllowed => vehicle?.vehicleInstance != null && handler != null &&
        !(SaveGameManager.Current?.gameVariables.disableVehicleDamage ?? false);

    private void FixedUpdate()
    {
        if (body == null || handler == null) return;
        beforeVelocity = body.velocity;
        beforeDamage = handler.Damage;
        sampleTime = Time.fixedTime;
    }

    private bool Eligible(Collision collision)
    {
        if (!DamageAllowed || body == null || collision.contactCount == 0 ||
            collision.collider == null || collision.collider.isTrigger ||
            collision.collider.gameObject.layer != streetPropsLayer ||
            !DamageHandler.IsCollisionValid(collision)) return false;
        foreach (var tag in handler!.collisionIgnoreTags)
            if (collision.collider.CompareTag(tag)) return false;
        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!Eligible(collision) || Mathf.Abs(Time.fixedTime-sampleTime) > Time.fixedDeltaTime*.5f) return;
        var normal = collision.GetContact(0).normal;
        // Road/curb support is not a pole impact, including colliders mislayered as props.
        if (Mathf.Abs(normal.y) > .5f) return;
        var otherVelocity = collision.rigidbody != null ? collision.rigidbody.velocity : Vector3.zero;
        var closing = Mathf.Max(Mathf.Abs(Vector3.Dot(collision.relativeVelocity,normal)),
            Mathf.Abs(Vector3.Dot(beforeVelocity-otherVelocity,normal)));
        if (closing <= MinimumNormalSpeed) return;
        pendingCollider = collision.collider.GetInstanceID();
        pendingUntil = Time.fixedTime+ContactWindow;
        pendingBaseline = beforeDamage;
        pendingSpeed = closing;
        CaptureContact(collision, normal);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (pendingCollider == 0 || collision.collider == null ||
            collision.collider.GetInstanceID() != pendingCollider) return;
        if (Time.fixedTime > pendingUntil || !Eligible(collision)) { pendingCollider=0; return; }
        CaptureContact(collision,collision.GetContact(0).normal);
    }

    private void CaptureContact(Collision collision, Vector3 normal)
    {
        var impulse = collision.impulse.magnitude;
        var evidence = "contact-impulse";
        if (impulse <= .001f && body != null && Mathf.Abs(Time.fixedTime-sampleTime) <= Time.fixedDeltaTime*.5f)
        {
            // CCD can report a zero-impulse first contact. Only use measured loss
            // from this physics step, projected onto the horizontal contact normal.
            var normalLoss = Mathf.Min(pendingSpeed,Mathf.Abs(Vector3.Dot(beforeVelocity-body.velocity,normal)));
            if (normalLoss < .25f) return;
            impulse = body.mass*normalLoss;
            evidence = "measured-normal-velocity-loss";
        }
        if (body == null || handler == null || impulse <= .001f) return;
        var floor = MinimumDamage(pendingBaseline,impulse,body.mass,Time.fixedDeltaTime,handler.damageIntensity);
        if (floor <= requiredDamage) return;
        requiredDamage = floor; requiredBaseline = pendingBaseline;
        impactTarget = $"{collision.collider.name}, layer={collision.collider.gameObject.layer}, " +
            $"speed={pendingSpeed:0.00}m/s, impulse={impulse:0.0}Ns, evidence={evidence}";
        // Commit after every native collision callback for this frame. No dependency
        // on script execution order, no addition on top of already calculated damage.
        if (commit == null && Application.isPlaying) commit=StartCoroutine(CommitAfterNativeCallbacks());
    }

    internal static float MinimumDamage(float baseline, float impulse, float mass, float step, float intensity) =>
        Mathf.Clamp01(baseline + Mathf.Max(0f,impulse) / (Mathf.Max(1f,mass)*Mathf.Max(.001f,step)*10f) *
            Mathf.Clamp(intensity,0f,.99f)*.005f);

    private IEnumerator CommitAfterNativeCallbacks()
    {
        yield return null;
        commit=null;
        CommitPendingDamage();
    }

    private void CommitPendingDamage()
    {
        var floor=requiredDamage; requiredDamage=0f;
        if (floor <= 0f) return;
        if (!DamageAllowed || handler == null || synchronizeNativeDamage == null ||
            handler.Damage+.00001f < requiredBaseline) return; // A repair invalidates the old impact.
        var native=handler.Damage;
        if (floor > native+.00001f)
        {
            handler.SetDamage(floor);
            // The actual game method updates saved damage, damage-off mode, broken
            // vehicle feedback and the UI event exactly as for a native collision.
            synchronizeNativeDamage();
        }
        if (diagnosticCount++ < 12)
            MercedesC63CabrioDiagnostics.DamageInfo(context,
                $"MercedesC63Cabrio pole impact vehicle={vehicle?.GetInstanceID()}, {impactTarget}, " +
                $"native={native:P2}, minimum={floor:P2}, final={handler.Damage:P2}, saved={vehicle?.vehicleInstance?.damage:P2}.");
        if (handler.Damage >= floor-.00001f) pendingCollider=0;
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision.collider != null && collision.collider.GetInstanceID()==pendingCollider) pendingCollider=0;
    }

    private void OnDisable()
    {
        if (commit != null) StopCoroutine(commit);
        commit=null; pendingCollider=0; requiredDamage=0f; sampleTime=float.NegativeInfinity;
    }
}
