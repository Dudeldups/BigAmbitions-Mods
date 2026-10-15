#nullable enable
using System.Reflection;
using BAModAPI;
using UnityEngine;

// The native wash coroutine owns progress, audio, navigation blocking and cleanup.
// Bind its effect once at vehicle configuration; never poll washing state.
public static class AudiR8WashBindings
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo? Effect = typeof(CarController).GetField("washVfx", Members);
    private static readonly FieldInfo? Audio = typeof(CarController).GetField("washAudioSource", Members);
    private static readonly FieldInfo? Loop = typeof(CarController).GetField("washSoundLoop", Members);
    private static readonly FieldInfo? End = typeof(CarController).GetField("washSoundEnd", Members);

    internal static void Configure(VehicleController vehicle, ModContext? context)
    {
        if (!(vehicle is CarController car)) return;
        if (Effect == null || Audio == null || Loop == null || End == null)
        {
            context?.Logger.Warn($"AudiR8 wash vehicle={vehicle.GetInstanceID()}: native wash fields are unavailable.");
            return;
        }
        var effects = Effect.GetValue(car) as GameObject;
        if (effects == null)
        {
            effects = car.transform.Find("AudiR8WashEffects")?.gameObject;
            if (effects == null)
            {
                effects = new GameObject("AudiR8WashEffects") { hideFlags = HideFlags.DontSave };
                effects.transform.SetParent(car.transform, false);
                context?.Logger.Warn($"AudiR8 wash vehicle={car.GetInstanceID()}: effect missing; created safe inactive fallback so native washing can finish.");
            }
            effects.SetActive(false);
            Effect.SetValue(car, effects);
        }
        var source = Audio.GetValue(car) as AudioSource;
        var loop = Loop.GetValue(car) as AudioClip;
        var end = End.GetValue(car) as AudioClip;
        if (source == null || loop == null || end == null)
            context?.Logger.Warn($"AudiR8 wash vehicle={car.GetInstanceID()}: native audio bindings incomplete; source={source != null}, loop={loop != null}, end={end != null}.");
        AudiR8Diagnostics.WashInfo(context, $"AudiR8 wash vehicle={car.GetInstanceID()}: native VFX bound={effects != null}, source={source != null}, loop={loop != null}, end={end != null}; native coroutine retains completion/unfreeze ownership.");
    }
}
