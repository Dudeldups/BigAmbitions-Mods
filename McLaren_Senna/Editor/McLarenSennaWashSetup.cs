#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class McLarenSennaWashSetup
{
    private const string Root = "Assets/Mods/McLaren_Senna";
    private const string EffectName = "McLarenWashEffects";
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Author(GameObject root)
    {
        var car = root.GetComponent<CarController>();
        if (car == null) throw new InvalidOperationException("Senna wash needs its native CarController.");
        var effect = root.transform.Find(EffectName)?.gameObject;
        if (effect == null)
        {
            effect = new GameObject(EffectName) { layer = root.layer };
            effect.transform.SetParent(root.transform, false);
        }
        effect.SetActive(false);
        effect.transform.localPosition = new Vector3(0f, .65f, 0f);
        var particles = effect.GetComponent<ParticleSystem>();
        if (particles == null) particles = effect.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = .5f;
        main.startSpeed = .35f;
        main.startSize = .075f;
        main.startColor = Color.white;
        main.gravityModifier = .35f;
        main.maxParticles = 96;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 120f;
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1.9f, .65f, 4.3f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = PrepareMistMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var serialized = new SerializedObject(car);
        serialized.FindProperty("washVfx").objectReferenceValue = effect;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        VerifyBindings(root);
    }

    private static Material PrepareMistMaterial()
    {
        var texturePath = Root + "/Models/GeneratedMaterials/McLarenWashMistTexture.asset";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
        {
            texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "McLarenWashMistTexture", wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var radius = new Vector2((x+.5f)/16f-1f, (y+.5f)/16f-1f).magnitude;
                    var alpha = Mathf.Pow(Mathf.Clamp01(1f-radius), 2f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            texture.Apply();
            AssetDatabase.CreateAsset(texture, texturePath);
        }
        var materialPath = Root + "/Models/GeneratedMaterials/McLarenWashMist.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("HDRP/Unlit") ?? throw new InvalidOperationException("HDRP wash shader missing.")) { name = "McLarenWashMist" };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetTexture("_UnlitColorMap", texture);
        material.SetColor("_UnlitColor", new Color(.8f, .92f, 1f, .45f));
        material.SetFloat("_SurfaceType", 1f);
        material.SetFloat("_BlendMode", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_AlphaSrcBlend", (float)BlendMode.One);
        material.SetFloat("_AlphaDstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_BLENDMODE_ALPHA");
        material.SetShaderPassEnabled("TransparentDepthPrepass", false);
        material.SetShaderPassEnabled("TransparentDepthPostpass", false);
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    public static void VerifyBindings(GameObject root)
    {
        var car = root.GetComponent<CarController>();
        if (car == null) throw new InvalidOperationException("Senna native wash controller is missing.");
        var serialized = new SerializedObject(car);
        var effects = serialized.FindProperty("washVfx")?.objectReferenceValue as GameObject;
        var audio = serialized.FindProperty("washAudioSource")?.objectReferenceValue as AudioSource;
        if (effects == null || audio == null || !effects.transform.IsChildOf(root.transform) || effects.activeSelf ||
            serialized.FindProperty("washSoundLoop")?.objectReferenceValue == null || serialized.FindProperty("washSoundEnd")?.objectReferenceValue == null)
            throw new InvalidOperationException("Senna native wash audio/VFX bindings are incomplete or active while parked.");
        var particles = effects.GetComponent<ParticleSystem>();
        var material = effects.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;
        if (particles == null || !particles.main.playOnAwake || material == null || material.shader.name != "HDRP/Unlit" ||
            material.GetTexture("_UnlitColorMap") == null || material.GetFloat("_SurfaceType") != 1f)
            throw new InvalidOperationException("Senna target-owned HDRP wash effect is missing.");
        Debug.Log("Senna wash bindings verified: inactive owned HDRP mist, native audio source and loop/end clips.");
    }

    // Execute the actual game's IEnumerator in an isolated Editor fixture.
    // No real player, money, save, or UI operation is involved.
    public static void VerifyNativeWashLifecycle()
    {
        var root = PrefabUtility.LoadPrefabContents(Root + "/McLarenSenna.prefab");
        var managerHost = new GameObject("SennaWashFixtureManager");
        var playerHost = new GameObject("SennaWashFixturePlayer");
        var gameAssembly = typeof(CarController).Assembly;
        var managerType = gameAssembly.GetType("GameManager", true)!;
        var playerType = gameAssembly.GetType("PlayerController", true)!;
        var lifecycle = managerType.BaseType!.Assembly.GetType("Extensions.SingletonLifecycle", true)!;
        var staticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var getter = lifecycle.GetMethod("GetSingleton", staticMembers)!.MakeGenericMethod(managerType);
        var register = lifecycle.GetMethod("RegisterSingleton", staticMembers)!;
        var unregister = lifecycle.GetMethod("UnregisterSingleton", staticMembers)!;
        var previousManager = getter.Invoke(null, null);
        var wasMissing = (bool)lifecycle.GetMethod("IsTypeMissing", staticMembers)!.Invoke(null, new object[] { managerType });
        Component? manager = null;
        try
        {
            VerifyBindings(root);
            manager = managerHost.AddComponent(managerType);
            var player = playerHost.AddComponent(playerType);
            managerType.GetField("playerController", Members)!.SetValue(manager, player);
            register.Invoke(null, new object[] { managerType, manager });
            lifecycle.GetMethod("UnmarkMissingType", staticMembers)!.Invoke(null, new object[] { managerType });
            var car = root.GetComponent<CarController>();
            car.vehicleInstance = new VehicleInstance("mclarensenna-vehicle:vehicletype_mclarensenna") { dirtiness = .8f };
            var body = root.GetComponent<Rigidbody>();
            typeof(VehicleController).GetField("_rigidbody", Members)!.SetValue(car, body);
            var effectsField = typeof(CarController).GetField("washVfx", Members)!;
            var effects = (GameObject)effectsField.GetValue(car);
            effectsField.SetValue(car, null);
            var bind = typeof(McLarenSennaWashBindings).GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)!;
            bind.Invoke(null, new object?[] { car, null });
            bind.Invoke(null, new object?[] { car, null });
            if ((GameObject)effectsField.GetValue(car) != effects || root.GetComponentsInChildren<Transform>(true).Count(t => t.name == EffectName) != 1)
                throw new InvalidOperationException("Runtime wash rebind duplicated or lost the authored effect.");
            var audio = (AudioSource)typeof(CarController).GetField("washAudioSource", Members)!.GetValue(car);
            var end = (AudioClip)typeof(CarController).GetField("washSoundEnd", Members)!.GetValue(car);
            var blockers = (HashSet<VehicleNavigationBlocker>)playerType.GetField("_activeVehicleNavigationBlockers", Members)!.GetValue(player);
            var wash = typeof(CarController).GetMethod("WashCoroutine", Members)!;
            // Reproduce the user's exact failure before validating the correction.
            effectsField.SetValue(car, null);
            var broken = (IEnumerator)wash.Invoke(car, new object[] { 1f });
            var reproduced = false;
            try { broken.MoveNext(); } catch (NullReferenceException) { reproduced = true; }
            if (!reproduced || !car.isWashing || !audio.loop || !blockers.Contains(VehicleNavigationBlocker.WashingVehicle) || body.constraints != RigidbodyConstraints.FreezeAll)
                throw new InvalidOperationException("Missing wash VFX did not reproduce the native frozen-loop failure.");
            audio.Stop(); audio.loop = false; car.isWashing = false; car.SetFreeze(false);
            playerType.GetMethod("UnsetVehicleNavigationBlocker", Members)!.Invoke(player, new object[] { VehicleNavigationBlocker.WashingVehicle });
            effectsField.SetValue(car, effects);
            for (var pass = 0; pass < 2; pass++)
            {
                var routine = (IEnumerator)wash.Invoke(car, new object[] { 1f });
                if (!routine.MoveNext() || !car.isWashing || !effects.activeSelf || !audio.loop || body.constraints != RigidbodyConstraints.FreezeAll ||
                    !blockers.Contains(VehicleNavigationBlocker.WashingVehicle)) throw new InvalidOperationException("Native Senna wash did not enter its expected state.");
                var startTime = routine.GetType().GetFields(Members).Single(f => f.Name.Contains("startTime") && f.FieldType == typeof(float));
                startTime.SetValue(routine, Time.time-.9999f);
                if (!routine.MoveNext() || car.vehicleInstance.dirtiness > .001f) throw new InvalidOperationException("Native wash did not clean saved dirtiness.");
                startTime.SetValue(routine, Time.time-2f);
                if (routine.MoveNext() || car.isWashing || effects.activeSelf || audio.loop || audio.clip != end ||
                    body.constraints != RigidbodyConstraints.None || blockers.Contains(VehicleNavigationBlocker.WashingVehicle))
                    throw new InvalidOperationException("Native Senna wash failed to release loop/VFX/freeze/navigation blocker.");
            }
            Debug.Log("Senna native wash lifecycle verified: original null-VFX failure reproduced, two washes clean saved dirtiness, switch to ending clip, stop loop/effects and release Rigidbody/navigation blocker.");
        }
        finally
        {
            if (manager != null) unregister.Invoke(null, new object[] { managerType, manager });
            if (previousManager != null) register.Invoke(null, new[] { (object)managerType, previousManager });
            if (wasMissing) lifecycle.GetMethod("MarkMissingType", staticMembers)!.Invoke(null, new object[] { managerType });
            UnityEngine.Object.DestroyImmediate(managerHost);
            UnityEngine.Object.DestroyImmediate(playerHost);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
