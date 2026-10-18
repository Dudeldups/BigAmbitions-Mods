#nullable enable
using System;
using System.Reflection;
using UnityEngine;

public static class KiaCeedSportswagonWashVerification
{
    public static void Verify()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "-kiaBundleRoot");
        if (index < 0) throw new InvalidOperationException("Missing -kiaBundleRoot.");
        var bundle = AssetBundle.LoadFromFile(System.IO.Path.Combine(args[index + 1], "kiaceedsportswagon.unity3d"));
        GameObject? instance = null;
        try
        {
            instance = UnityEngine.Object.Instantiate(bundle.LoadAsset<GameObject>(
                "Assets/Mods/Kia_Ceed_Sportswagon/KiaCeedSportswagon.prefab"));
            instance.SetActive(false);
            var binding = typeof(KiaCeedSportswagonWashBindings);
            var configure = binding.GetMethod("Configure", BindingFlags.NonPublic | BindingFlags.Static)!;
            var car = instance.GetComponent(configure.GetParameters()[0].ParameterType);
            var carType = car.GetType();
            const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
            var effect = carType.GetField("washVfx", fields)!;
            var source = carType.GetField("washAudioSource", fields)!;
            var loop = carType.GetField("washSoundLoop", fields)!;
            var end = carType.GetField("washSoundEnd", fields)!;
            Require(source.GetValue(car) != null && loop.GetValue(car) != null && end.GetValue(car) != null,
                "Shipped prefab audio bindings incomplete.");
            var originalSource = source.GetValue(car);
            effect.SetValue(car, null);
            configure.Invoke(null, new object?[] { car, null });
            var fallback = effect.GetValue(car) as GameObject;
            Require(fallback != null && !fallback.activeSelf && fallback.transform.parent == car.transform,
                "Missing effect fallback not safely bound.");
            configure.Invoke(null, new object?[] { car, null });
            Require(ReferenceEquals(fallback, effect.GetValue(car)) && ReferenceEquals(originalSource, source.GetValue(car)),
                "Repeated configuration changed effects/audio ownership.");
            // Exercise both SetActive calls that previously aborted the native wash coroutine.
            fallback!.SetActive(true);
            fallback.SetActive(false);
            Require(instance.transform.Find("KiaCeedSportswagonWashEffects") == fallback.transform,
                "Fallback hierarchy mismatch.");
            Debug.Log("KIA_WASH_VERIFIED: missing VFX bound, repeated setup idempotent, native audio retained, wash effect start/end calls safe.");
        }
        finally
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            if (bundle != null) bundle.Unload(true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
