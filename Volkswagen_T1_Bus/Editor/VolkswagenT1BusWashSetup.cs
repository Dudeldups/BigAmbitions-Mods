#nullable enable
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class VolkswagenT1BusWashSetup
{
    private const string Root = "Assets/Mods/Volkswagen_T1_Bus";
    private const string EffectName = "VolkswagenT1BusWashEffects";
    private const string GeneratedMaterials = Root + "/Models/GeneratedMaterials";
    private const string TexturePath = GeneratedMaterials + "/VolkswagenT1BusWashMistTexture.asset";
    private const string MaterialPath = GeneratedMaterials + "/VolkswagenT1BusWashMist.mat";

    public static void PrepareAssetsForBundle()
    {
        PrepareMistMaterial();
        AssetDatabase.SaveAssets();
    }

    public static void Author(GameObject root)
    {
        var car = FindCarController(root);
        if (car == null)
            throw new InvalidOperationException("Volkswagen T1 wash needs its native CarController.");

        var effect = root.transform.Find(EffectName)?.gameObject;
        if (effect == null)
        {
            effect = new GameObject(EffectName) { layer = root.layer };
            effect.transform.SetParent(root.transform, false);
        }

        effect.SetActive(false);
        effect.transform.localPosition = new Vector3(0f, .65f, 0f);

        var particles = effect.GetComponent<ParticleSystem>();
        if (particles == null)
            particles = effect.AddComponent<ParticleSystem>();
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
        var washEffect = serialized.FindProperty("washVfx");
        if (washEffect == null)
            throw new InvalidOperationException("Volkswagen T1 native wash VFX field is missing.");
        washEffect.objectReferenceValue = effect;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        VerifyBindings(root);
    }

    private static Material PrepareMistMaterial()
    {
        EnsureGeneratedMaterialsFolder();

        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (texture == null)
        {
            texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
            {
                name = "VolkswagenT1BusWashMistTexture",
                wrapMode = TextureWrapMode.Clamp,
            };
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    var radius = new Vector2((x + .5f) / 16f - 1f, (y + .5f) / 16f - 1f).magnitude;
                    var alpha = Mathf.Pow(Mathf.Clamp01(1f - radius), 2f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            texture.Apply();
            AssetDatabase.CreateAsset(texture, TexturePath);
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("HDRP/Unlit") ??
                                    throw new InvalidOperationException("HDRP wash shader is missing."))
            {
                name = "VolkswagenT1BusWashMist",
            };
            AssetDatabase.CreateAsset(material, MaterialPath);
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

    private static void EnsureGeneratedMaterialsFolder()
    {
        if (AssetDatabase.IsValidFolder(GeneratedMaterials))
            return;

        var modelsFolder = Root + "/Models";
        if (!AssetDatabase.IsValidFolder(modelsFolder))
            throw new InvalidOperationException("Volkswagen T1 Models folder is missing.");
        AssetDatabase.CreateFolder(modelsFolder, "GeneratedMaterials");
    }

    public static void VerifyBindings(GameObject root)
    {
        var car = FindCarController(root);
        if (car == null)
            throw new InvalidOperationException("Volkswagen T1 native wash controller is missing.");

        var serialized = new SerializedObject(car);
        var effectProperty = serialized.FindProperty("washVfx");
        var audioProperty = serialized.FindProperty("washAudioSource");
        var loopProperty = serialized.FindProperty("washSoundLoop");
        var endProperty = serialized.FindProperty("washSoundEnd");
        var effects = effectProperty?.objectReferenceValue as GameObject;
        var audio = audioProperty?.objectReferenceValue as AudioSource;
        if (effects == null || audio == null || !effects.transform.IsChildOf(root.transform) || effects.activeSelf ||
            loopProperty == null || endProperty == null)
        {
            throw new InvalidOperationException(
                "Volkswagen T1 wash audio/VFX bindings are incomplete or active while parked: " +
                $"effect={effects != null}, effectProperty={effectProperty != null}, " +
                $"audio={audio != null}, audioProperty={audioProperty != null}, " +
                $"loopField={loopProperty != null}, endField={endProperty != null}, " +
                $"child={(effects != null && effects.transform.IsChildOf(root.transform))}, " +
                $"active={(effects != null && effects.activeSelf)}.");
        }

        if (loopProperty.objectReferenceValue == null || endProperty.objectReferenceValue == null)
            Debug.LogWarning("Volkswagen T1 wash audio clips are unresolved in the SDK project; native washing remains bound to the authored VFX and AudioSource.");

        var particles = effects.GetComponent<ParticleSystem>();
        var material = effects.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;
        var shaderName = material?.shader?.name ?? "<null>";
        var textureBound = material != null && material.GetTexture("_UnlitColorMap") != null;
        var surfaceType = material != null ? material.GetFloat("_SurfaceType") : -1f;
        if (particles == null || !particles.main.playOnAwake || !particles.main.loop ||
            material == null || shaderName != "HDRP/Unlit" || !textureBound || Mathf.Abs(surfaceType - 1f) > .001f)
        {
            throw new InvalidOperationException(
                "Volkswagen T1 target-owned HDRP wash mist is missing: " +
                $"particles={particles != null}, playOnAwake={(particles != null && particles.main.playOnAwake)}, " +
                $"loop={(particles != null && particles.main.loop)}, material={material != null}, " +
                $"shader='{shaderName}', texture={textureBound}, surfaceType={surfaceType}.");
        }

        Debug.Log("Volkswagen T1 wash bindings verified: inactive HDRP mist plus native audio and wash clips.");
    }

    private static Component? FindCarController(GameObject root)
    {
        foreach (var component in root.GetComponents<Component>())
        {
            if (component != null && string.Equals(component.GetType().Name, "CarController", StringComparison.Ordinal))
                return component;
        }

        return null;
    }
}
