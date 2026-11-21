#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BentleyContinentalGTSpeedWashSetup
{
    private const string Root = "Assets/Mods/Bentley_Continental_GT_Speed";
    private const string NativeSourceLoopPath = "Assets/Mods/Example-Vehicle/Audio/CarWashLoop.wav";
    private const string NativeSourceEndPath = "Assets/Mods/Example-Vehicle/Audio/CarWash_Ending.wav";
    private const string LoopPath = Root + "/Audio/BentleyWashLoop.wav";
    private const string EndPath = Root + "/Audio/BentleyWashEnd.wav";
    private const string NativeLoopSha256 = "DE43DD6F168078A1CE719A9FBDD1894588CEEAC792ABECEEE322B179AD39C47D";
    private const string NativeEndSha256 = "E4741304B5894608623E4DF69AF0E9A7887F8E9D94C516839D6DD9E8E0C98F02";
    private const float NativeLoopLength = 3.21956253f;
    private const float NativeEndLength = 1.80956244f;
    private const string MaterialFolder = Root + "/Models/GeneratedMaterials";
    private const string EffectName = "BentleyWashEffects";
    private const string AudioSourceName = "BentleyWashAudioSource";
    private const string TexturePath = MaterialFolder + "/BentleyWashMistTexture.asset";
    private const string MaterialPath = MaterialFolder + "/BentleyWashMist.mat";

    public static void Author(GameObject root)
    {
        if (root == null) throw new ArgumentNullException(nameof(root));
        var car = root.GetComponent<CarController>();
        if (car == null) throw new InvalidOperationException("Bentley wash setup requires the native CarController.");

        var loop = EnsureNativeWashClip(NativeSourceLoopPath, LoopPath, NativeLoopSha256,
            "BentleyWashLoop", NativeLoopLength);
        var end = EnsureNativeWashClip(NativeSourceEndPath, EndPath, NativeEndSha256,
            "BentleyWashEnd", NativeEndLength);

        var effect = root.transform.Find(EffectName)?.gameObject;
        if (effect == null)
        {
            effect = new GameObject(EffectName) { layer = root.layer };
            effect.transform.SetParent(root.transform, false);
        }
        effect.SetActive(false);
        effect.transform.localPosition = new Vector3(0f, 0.65f, 0f);
        var particles = effect.GetComponent<ParticleSystem>();
        if (particles == null) particles = effect.AddComponent<ParticleSystem>();
        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = 0.5f;
        main.startSpeed = 0.35f;
        main.startSize = 0.075f;
        main.startColor = Color.white;
        main.gravityModifier = 0.35f;
        main.maxParticles = 96;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        var emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 120f;
        var shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(1.9f, 0.65f, 4.3f);
        var particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        particleRenderer.sharedMaterial = PrepareMistMaterial();
        particleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        particleRenderer.shadowCastingMode = ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;

        var serialized = new SerializedObject(car);
        var effectProperty = serialized.FindProperty("washVfx");
        var audioProperty = serialized.FindProperty("washAudioSource");
        var loopProperty = serialized.FindProperty("washSoundLoop");
        var endProperty = serialized.FindProperty("washSoundEnd");
        if (effectProperty == null || audioProperty == null || loopProperty == null || endProperty == null)
            throw new InvalidOperationException("Bentley native wash fields are missing from CarController.");
        var audio = audioProperty.objectReferenceValue as AudioSource;
        if (audio == null)
        {
            var audioObject = root.transform.Find(AudioSourceName)?.gameObject;
            if (audioObject == null)
            {
                audioObject = new GameObject(AudioSourceName) { layer = root.layer };
                audioObject.transform.SetParent(root.transform, false);
            }
            audio = audioObject.GetComponent<AudioSource>() ?? audioObject.AddComponent<AudioSource>();
        }
        audio.playOnAwake = false;
        audio.pitch = 1f;
        audio.loop = false;
        audio.volume = 1f;
        audio.spatialBlend = 1f;
        audio.dopplerLevel = 1f;
        audio.reverbZoneMix = 1f;
        audio.bypassEffects = false;
        audio.bypassListenerEffects = false;
        audio.bypassReverbZones = false;
        effectProperty.objectReferenceValue = effect;
        audioProperty.objectReferenceValue = audio;
        loopProperty.objectReferenceValue = loop;
        endProperty.objectReferenceValue = end;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(effect);
        VerifyBindings(root);
    }

    public static void VerifyBindings(GameObject root)
    {
        var car = root.GetComponent<CarController>();
        if (car == null) throw new InvalidOperationException("Bentley native wash controller is missing.");
        var serialized = new SerializedObject(car);
        var effect = serialized.FindProperty("washVfx")?.objectReferenceValue as GameObject;
        var audio = serialized.FindProperty("washAudioSource")?.objectReferenceValue as AudioSource;
        var loop = serialized.FindProperty("washSoundLoop")?.objectReferenceValue as AudioClip;
        var end = serialized.FindProperty("washSoundEnd")?.objectReferenceValue as AudioClip;
        if (effect == null || audio == null || loop == null || end == null ||
            !effect.transform.IsChildOf(root.transform) || effect.activeSelf || audio.playOnAwake ||
            audio.pitch != 1f || audio.loop || audio.volume != 1f || audio.spatialBlend != 1f ||
            audio.dopplerLevel != 1f || audio.reverbZoneMix != 1f || audio.bypassEffects ||
            audio.bypassListenerEffects || audio.bypassReverbZones ||
            AssetDatabase.GetAssetPath(loop) != LoopPath || AssetDatabase.GetAssetPath(end) != EndPath ||
            !IsNativeWashClip(loop, NativeSourceLoopPath, "BentleyWashLoop", NativeLoopLength) ||
            !IsNativeWashClip(end, NativeSourceEndPath, "BentleyWashEnd", NativeEndLength) ||
            GetSha256(AbsoluteAssetPath(LoopPath)) != NativeLoopSha256 ||
            GetSha256(AbsoluteAssetPath(EndPath)) != NativeEndSha256)
            throw new InvalidOperationException("Bentley native wash audio/VFX bindings are incomplete or active while parked.");
        var particles = effect.GetComponent<ParticleSystem>();
        var material = effect.GetComponent<ParticleSystemRenderer>()?.sharedMaterial;
        if (particles == null || !particles.main.playOnAwake || material == null ||
            material.shader.name != "HDRP/Unlit" || material.GetTexture("_UnlitColorMap") == null ||
            material.GetFloat("_SurfaceType") != 1f)
            throw new InvalidOperationException("Bentley target-owned HDRP wash effect is missing.");
        Debug.Log(
            $"Bentley wash bindings verified: native PCM loop='{AssetDatabase.GetAssetPath(loop)}' " +
            $"({loop.frequency} Hz/{loop.samples} samples) end='{AssetDatabase.GetAssetPath(end)}' " +
            $"({end.frequency} Hz/{end.samples} samples), source='{audio.name}' pitch={audio.pitch:0.00}, " +
            $"playOnAwake={audio.playOnAwake}, loop={audio.loop}, mixer='{audio.outputAudioMixerGroup?.name ?? "<native Start binding>"}'.");
    }

    private static AudioClip EnsureNativeWashClip(
        string nativeSourcePath, string targetPath, string expectedSha256, string expectedName, float expectedLength)
    {
        var sourceFile = AbsoluteAssetPath(nativeSourcePath);
        var targetFile = AbsoluteAssetPath(targetPath);
        if (File.Exists(sourceFile))
        {
            if (GetSha256(sourceFile) != expectedSha256)
                throw new InvalidOperationException("Native wash source does not match the verified PCM: " + nativeSourcePath);
            if (!File.Exists(targetFile) || GetSha256(targetFile) != expectedSha256)
                File.Copy(sourceFile, targetFile, true);
        }
        else if (!File.Exists(targetFile) || GetSha256(targetFile) != expectedSha256)
            throw new InvalidOperationException(
                "Verified native wash PCM is unavailable in either the source or Bentley asset path: " + targetPath);

        AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
        var targetImporter = AssetImporter.GetAtPath(targetPath) as AudioImporter;
        if (targetImporter == null)
            throw new InvalidOperationException("Could not configure Bentley native wash clip: " + targetPath);
        var sourceImporter = AssetImporter.GetAtPath(nativeSourcePath) as AudioImporter;
        if (sourceImporter != null)
        {
            targetImporter.forceToMono = sourceImporter.forceToMono;
            targetImporter.loadInBackground = sourceImporter.loadInBackground;
            targetImporter.defaultSampleSettings = sourceImporter.defaultSampleSettings;
        }
        targetImporter.assetBundleName = "bentleycontinentalgtspeed";
        targetImporter.assetBundleVariant = "unity3d";
        targetImporter.SaveAndReimport();

        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(targetPath);
        if (!IsNativeWashClip(clip, nativeSourcePath, expectedName, expectedLength) ||
            GetSha256(targetFile) != expectedSha256)
            throw new InvalidOperationException("Bentley wash clip is not the verified native PCM: " + targetPath);
        return clip!;
    }

    private static bool IsNativeWashClip(
        AudioClip? targetClip, string nativeSourcePath, string expectedName, float expectedLength)
    {
        if (targetClip == null || targetClip.name != expectedName || targetClip.frequency <= 0 || targetClip.samples <= 0)
            return false;

        var nativeClip = AssetDatabase.LoadAssetAtPath<AudioClip>(nativeSourcePath);
        var expectedFrequency = nativeClip != null ? nativeClip.frequency : targetClip.frequency;
        var expectedSamples = nativeClip != null ? nativeClip.samples : targetClip.samples;
        var expectedImportedLength = nativeClip != null ? nativeClip.length : expectedLength;
        return targetClip.frequency == expectedFrequency && targetClip.samples == expectedSamples &&
               Mathf.Abs(targetClip.length - expectedImportedLength) <= 0.001f;
    }

    private static string AbsoluteAssetPath(string assetPath) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));

    private static string GetSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", "");
    }

    private static Material PrepareMistMaterial()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        if (texture == null)
        {
            texture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
            {
                name = "BentleyWashMistTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var radius = new Vector2((x + 0.5f) / 16f - 1f, (y + 0.5f) / 16f - 1f).magnitude;
                    var alpha = Mathf.Pow(Mathf.Clamp01(1f - radius), 2f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            texture.Apply();
            AssetDatabase.CreateAsset(texture, TexturePath);
        }
        SetBundle(TexturePath);

        var shader = Shader.Find("HDRP/Unlit") ??
                     AssetDatabase.LoadAssetAtPath<Shader>(
                         "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.shader");
        if (shader == null) throw new InvalidOperationException("HDRP/Unlit shader is unavailable for Bentley wash mist.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "BentleyWashMist" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.shader = shader;
        if (material.HasProperty("_UnlitColorMap")) material.SetTexture("_UnlitColorMap", texture);
        if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", new Color(0.8f, 0.92f, 1f, 0.45f));
        SetFloat(material, "_SurfaceType", 1f);
        SetFloat(material, "_BlendMode", 0f);
        SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
        SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloat(material, "_AlphaSrcBlend", (float)BlendMode.One);
        SetFloat(material, "_AlphaDstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloat(material, "_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_BLENDMODE_ALPHA");
        material.SetShaderPassEnabled("TransparentDepthPrepass", false);
        material.SetShaderPassEnabled("TransparentDepthPostpass", false);
        material.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        SetBundle(MaterialPath);
        return material;
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
    }

    private static void SetBundle(string assetPath)
    {
        var importer = AssetImporter.GetAtPath(assetPath);
        if (importer == null) throw new InvalidOperationException("Could not assign Bentley bundle asset: " + assetPath);
        importer.assetBundleName = "bentleycontinentalgtspeed";
        importer.assetBundleVariant = "unity3d";
        importer.SaveAndReimport();
    }

}
