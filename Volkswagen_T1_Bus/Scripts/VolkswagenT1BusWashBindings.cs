#nullable enable
using System.Reflection;
using BAModAPI;
using UnityEngine;

// The game's native wash coroutine owns cleaning, audio, navigation blocking,
// and cleanup. Bind the authored effect once per configured vehicle instance.
internal static class VolkswagenT1BusWashBindings
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private const string EffectName = "VolkswagenT1BusWashEffects";
    private static readonly FieldInfo? Effect = typeof(CarController).GetField("washVfx", Members);
    private static readonly FieldInfo? Audio = typeof(CarController).GetField("washAudioSource", Members);
    private static readonly FieldInfo? Loop = typeof(CarController).GetField("washSoundLoop", Members);
    private static readonly FieldInfo? End = typeof(CarController).GetField("washSoundEnd", Members);

    internal static void Configure(VehicleController vehicle, ModContext? context)
    {
        if (!(vehicle is CarController car))
            return;

        if (Effect == null || Audio == null || Loop == null || End == null)
        {
            context?.Logger.Warn(
                $"VolkswagenT1Bus wash vehicle={vehicle.GetInstanceID()}: native wash fields are unavailable; " +
                $"effect={Effect != null}, audio={Audio != null}, loop={Loop != null}, end={End != null}.");
            return;
        }

        var effects = Effect.GetValue(car) as GameObject;
        if (effects == null)
        {
            effects = car.transform.Find(EffectName)?.gameObject;
            if (effects == null)
            {
                effects = new GameObject(EffectName) { hideFlags = HideFlags.DontSave };
                effects.transform.SetParent(car.transform, false);
                context?.Logger.Warn(
                    $"VolkswagenT1Bus wash vehicle={car.GetInstanceID()}: authored VFX binding was missing; " +
                    "created an inactive fallback so the native wash can finish.");
            }

            effects.SetActive(false);
            Effect.SetValue(car, effects);
        }

        var source = Audio.GetValue(car) as AudioSource;
        var loop = Loop.GetValue(car) as AudioClip;
        var end = End.GetValue(car) as AudioClip;
        if (source == null || loop == null || end == null)
        {
            context?.Logger.Warn(
                $"VolkswagenT1Bus wash vehicle={car.GetInstanceID()}: native audio bindings incomplete; " +
                $"source={source != null}, loop={loop != null}, end={end != null}.");
        }

        VolkswagenT1BusDiagnostics.WashInfo(
            context,
            $"VolkswagenT1Bus wash vehicle={car.GetInstanceID()}: VFX bound={effects != null}, " +
            $"source={source != null}, loop={loop != null}, end={end != null}; " +
            "native coroutine retains cleaning and cleanup ownership.");
    }
}
