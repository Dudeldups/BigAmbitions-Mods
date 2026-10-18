#nullable enable
using System.Collections;
using System.Reflection;
using BAModAPI;
using NWH.WheelController3D;
using UnityEngine;

[AddComponentMenu("")]
public sealed class DodgeRam1500TRXWashPhysicsGuard : MonoBehaviour
{
    private CarController? car;
    private Rigidbody? body;
    private WheelController[] wheels = System.Array.Empty<WheelController>();
    private bool[] wheelEnabled = System.Array.Empty<bool>();
    private GameObject? effects;
    private GameObject? originalEffect;
    private AudioSource? washSource;
    private AudioSource? originalAudio;
    private ModContext? context;
    private bool ownsEffect, suspended, wasKinematic, wasControlled;
    private Coroutine? resume;
    internal bool IsConfigured => car != null;
    internal bool IsSuspended => suspended;
    internal GameObject? EventEffect => effects;
    internal AudioSource? WashAudio => washSource;

    internal void Initialize(CarController controller, GameObject effect, AudioSource source,
        GameObject? previousEffect, AudioSource? previousAudio, bool effectOwned, ModContext? modContext)
    {
        car = controller;
        body = car.GetComponent<Rigidbody>();
        wheels = car.GetComponentsInChildren<WheelController>(true);
        wheelEnabled = new bool[wheels.Length];
        effects = effect; originalEffect = previousEffect; ownsEffect = effectOwned;
        washSource = source; originalAudio = previousAudio; context = modContext;
        var relay = effect.GetComponent<DodgeRam1500TRXWashEffectEvents>() ?? effect.AddComponent<DodgeRam1500TRXWashEffectEvents>();
        relay.Bind(this);
    }

    internal void BeginWash()
    {
        // Native WashCoroutine has set isWashing and FreezeAll before enabling
        // washVfx. Suspend tire simulation for precisely that frozen interval.
        if (suspended || car == null || !car.isWashing || body == null) return;
        if (resume != null) StopCoroutine(resume);
        resume = null;
        wasKinematic = body.isKinematic;
        wasControlled = car.controlledByPlayer;
        if (!wasKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        for (var i = 0; i < wheels.Length; i++)
        {
            wheelEnabled[i] = wheels[i] != null && wheels[i].enabled;
            if (wheels[i] != null) wheels[i].enabled = false;
        }
        body.isKinematic = true;
        suspended = true;
        DodgeRam1500TRXDiagnostics.WashInfo(context,
            $"DodgeRam1500TRX wash vehicle={car.GetInstanceID()}: suspended {wheels.Length} wheels; native freeze={body.constraints}, position={body.position}.");
    }

    internal void EndWash()
    {
        if (!suspended || resume != null) return;
        if (isActiveAndEnabled) resume = StartCoroutine(ResumeAfterNativeCompletion());
        else RestoreSuspension();
    }

    private IEnumerator ResumeAfterNativeCompletion()
    {
        // Disabling washVfx precedes native ending sound, SetFreeze(false) and
        // navigation unblock. Restore after those statements have completed.
        yield return null;
        RestoreSuspension();
        resume = null;
    }

    internal void RestoreSuspension()
    {
        if (!suspended) return;
        var lostControl = wasControlled && (car == null || !car.controlledByPlayer);
        if (body != null)
        {
            if (!lostControl) body.isKinematic = wasKinematic;
            if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }
        for (var i = 0; i < wheels.Length; i++)
        {
            var wheel = wheels[i];
            if (wheel == null) continue;
            wheel.ResetSimulationState();
            // The frozen interval must not become an artificial damper spike.
            wheel.spring.prevLength = wheel.spring.length;
            wheel.spring.compressionVelocity = wheel.spring.prevVelocity = 0f;
            wheel.spring.force = wheel.damper.force = 0f;
            wheel.enabled = wheelEnabled[i] && !lostControl;
        }
        suspended = false;
        DodgeRam1500TRXDiagnostics.WashInfo(context,
            $"DodgeRam1500TRX wash vehicle={car?.GetInstanceID()}: restored captured wheel/kinematic states; velocity={body?.velocity}, constraints={body?.constraints}, washing={car?.isWashing}.");
    }

    internal void Shutdown()
    {
        StopAllCoroutines(); resume = null;
        RestoreSuspension();
        if (effects != null) effects.GetComponent<DodgeRam1500TRXWashEffectEvents>()?.Unbind(this);
        if (car != null)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var effectField = typeof(CarController).GetField("washVfx", flags);
            var audioField = typeof(CarController).GetField("washAudioSource", flags);
            if (ownsEffect && ReferenceEquals(effectField?.GetValue(car), effects)) effectField!.SetValue(car, originalEffect);
            if (ReferenceEquals(audioField?.GetValue(car), washSource)) audioField!.SetValue(car, originalAudio);
        }
        if (washSource != null) Destroy(washSource.gameObject);
        if (ownsEffect && effects != null) Destroy(effects);
        car = null;
        Destroy(this);
    }

    private void OnDisable()
    {
        StopAllCoroutines(); resume = null;
        RestoreSuspension();
    }
}

internal sealed class DodgeRam1500TRXWashEffectEvents : MonoBehaviour
{
    private DodgeRam1500TRXWashPhysicsGuard? owner;
    internal void Bind(DodgeRam1500TRXWashPhysicsGuard guard) => owner = guard;
    internal void Unbind(DodgeRam1500TRXWashPhysicsGuard guard)
    {
        if (owner != guard) return;
        owner = null;
        Destroy(this);
    }
    private void OnEnable() => owner?.BeginWash();
    private void OnDisable() => owner?.EndWash();
}
