#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Authors target-owned wash effects and binds standard native CarController wash audio.</summary>
public static class LamborghiniAventadorWashSetup
{
    private const string Root = "Assets/Mods/Lamborghini_Aventador";
    private const string GeneratedFolder = Root + "/Models/GeneratedWash";
    private const string EffectName = "AventadorWashEffects";
    private const string LoopPath = Root + "/Config/Audio/CarWashLoop.wav";
    private const string EndPath = Root + "/Config/Audio/CarWash_Ending.wav";
    private const string CanonicalLoopPath = "Assets/Mods/Example-Vehicle/Audio/CarWashLoop.wav";
    private const string CanonicalEndPath = "Assets/Mods/Example-Vehicle/Audio/CarWash_Ending.wav";
    private const string TexturePath = GeneratedFolder + "/AventadorWashMistTexture.asset";
    private const string MaterialPath = GeneratedFolder + "/AventadorWashMist.mat";

    public static void Author(GameObject root)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        EnsureFolder();
        var car = root.GetComponent<CarController>();
        if (car == null) throw new InvalidOperationException("Aventador native wash authoring needs a CarController on the prefab root.");

        var effect = root.transform.Find(EffectName)?.gameObject;
        if (effect == null)
        {
            effect = new GameObject(EffectName) { layer = root.layer };
            effect.transform.SetParent(root.transform, false);
        }
        effect.SetActive(true);
        effect.transform.localPosition = new Vector3(0f, .63f, 0f);
        effect.transform.localRotation = Quaternion.identity;
        effect.transform.localScale = Vector3.one;
        var particles = effect.GetComponent<ParticleSystem>();
        if (particles == null)
            particles = effect.AddComponent<ParticleSystem>();
        if (particles == null || effect.GetComponent<ParticleSystem>() == null)
            throw new InvalidOperationException("Unity did not attach a ParticleSystem to AventadorWashEffects.");
        var main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = .55f;
        main.startSpeed = .22f;
        main.startSize = .07f;
        main.startColor = new Color(.82f, .93f, 1f, .4f);
        main.gravityModifier = .18f;
        main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 95f;
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1.9f, .6f, 4.1f);
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = PrepareMistMaterial();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        effect.SetActive(false);

        var serialized = new SerializedObject(car);
        var audio = Reference(serialized, "washAudioSource") as AudioSource;
        if (audio == null || audio.transform != root.transform)
            throw new InvalidOperationException("Aventador must retain the donor's native root wash AudioSource.");
        SetReference(serialized, "washVfx", effect);
        SetReference(serialized, "washAudioSource", audio);
        SetReference(serialized, "washSoundLoop", LoadNativeClip(LoopPath));
        SetReference(serialized, "washSoundEnd", LoadNativeClip(EndPath));
        audio.playOnAwake = true;
        audio.loop = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(car);
        VerifyBindings(root);
    }

    public static void VerifyBindings(GameObject root)
    {
        var car = root.GetComponent<CarController>();
        if (car == null) throw new InvalidOperationException("Aventador native CarController is missing for wash validation.");
        var serialized = new SerializedObject(car);
        var effect = Reference(serialized, "washVfx") as GameObject;
        var audio = Reference(serialized, "washAudioSource") as AudioSource;
        var loop = Reference(serialized, "washSoundLoop") as AudioClip;
        var end = Reference(serialized, "washSoundEnd") as AudioClip;
        if (effect == null || effect.activeSelf || !effect.transform.IsChildOf(root.transform) ||
            effect.GetComponent<ParticleSystem>() == null || audio == null || audio.transform != root.transform ||
            !audio.enabled || !audio.playOnAwake || audio.mute || audio.volume <= 0f ||
            loop == null || end == null || AssetDatabase.GetAssetPath(loop) != LoopPath || AssetDatabase.GetAssetPath(end) != EndPath)
            throw new InvalidOperationException("Aventador wash effect, donor-root AudioSource or local standard loop/end clips are not bound correctly.");
        if (!MatchesCanonicalClip(LoopPath, CanonicalLoopPath) || !MatchesCanonicalClip(EndPath, CanonicalEndPath))
            throw new InvalidOperationException("Aventador local wash clips must remain byte-identical copies of the standard Example-Vehicle CarWash clips.");
        Debug.Log("Lamborghini Aventador wash bindings verified: native root AudioSource and local byte-identical standard CarWashLoop/CarWash_Ending clips.");
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(GeneratedFolder))
        {
            if (!AssetDatabase.IsValidFolder(Root + "/Models/GeneratedWash"))
                AssetDatabase.CreateFolder(Root + "/Models", "GeneratedWash");
        }
    }

    private static AudioClip LoadNativeClip(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var imported = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (imported == null || imported.samples == 0 || imported.length <= 0f)
            throw new InvalidOperationException("Standard CarWash audio did not import with samples: " + path);
        return imported;
    }

    private static bool MatchesCanonicalClip(string localPath, string canonicalPath) =>
        File.Exists(localPath) && File.Exists(canonicalPath) &&
        File.ReadAllBytes(localPath).SequenceEqual(File.ReadAllBytes(canonicalPath));

    private static Material PrepareMistMaterial()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (texture == null)
        {
            texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "AventadorWashMistTexture", wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
            {
                var radius = new Vector2((x + .5f) / 16f - 1f, (y + .5f) / 16f - 1f).magnitude;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - radius), 2f)));
            }
            texture.Apply();
            AssetDatabase.CreateAsset(texture, TexturePath);
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            var shader = Shader.Find("HDRP/Unlit") ?? throw new InvalidOperationException("HDRP/Unlit shader is unavailable for Aventador wash mist.");
            material = new Material(shader) { name = "AventadorWashMist" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.SetTexture("_UnlitColorMap", texture);
        material.SetColor("_UnlitColor", new Color(.8f, .92f, 1f, .4f));
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

    private static void SetReference(SerializedObject target, string name, UnityEngine.Object value)
    {
        var property = target.FindProperty(name);
        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
            throw new InvalidOperationException("Native CarController wash binding is unavailable: " + name);
        property.objectReferenceValue = value;
    }

    private static UnityEngine.Object? Reference(SerializedObject target, string name) => target.FindProperty(name)?.objectReferenceValue;
}
