#nullable enable
#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MobileVeterinarian.Editor
{
    public static class MobileVeterinarianAssetBuilder
    {
        private const string ModRoot = "Assets/Mods/MobileVeterinarian";
        private const string ModelPath = ModRoot + "/Models/doctor.glb";
        private const string DoctorPrefabPath = ModRoot + "/Doctor.prefab";
        private const string ManifestPath = ModRoot + "/ModManifest.asset";
        private const string AssemblyPath = ModRoot + "/MobileVeterinarian.asmdef";
        private const string LocalesPath = ModRoot + "/Locales";
        private const string BundleName = "mobileveterinarian";
        private const string BundleVariant = "unity3d";
        private const float TargetHeight = 1.78f;

        [MenuItem("Big Ambitions/Mobile Veterinarian/Build Doctor Assets")]
        public static void BuildAll()
        {
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            CreateOrUpdateDoctorPrefab();
            UpdateManifest();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            BuildWindowsAssetBundle();
            ValidateWindowsAssetBundle();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("Mobile Veterinarian: doctor prefab and Windows AssetBundle built successfully.");
        }

        private static void CreateOrUpdateDoctorPrefab()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)
                ?? throw new InvalidOperationException($"Doctor GLB did not import as a GameObject: {ModelPath}");

            var root = new GameObject("MobileVeterinarianDoctor");
            try
            {
                var visual = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject;
                if (visual == null)
                    visual = Object.Instantiate(source, root.transform);

                if (PrefabUtility.IsPartOfPrefabInstance(visual))
                {
                    PrefabUtility.UnpackPrefabInstance(
                        visual,
                        PrefabUnpackMode.Completely,
                        InteractionMode.AutomatedAction);
                }

                visual.name = "DoctorModel";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                BakeTreatmentPose(visual);
                NormalizeVisual(visual);

                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(collider);
                foreach (var rigidbody in root.GetComponentsInChildren<Rigidbody>(true))
                    Object.DestroyImmediate(rigidbody);
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                    Object.DestroyImmediate(camera);
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                    Object.DestroyImmediate(light);
                foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                    Object.DestroyImmediate(animator);

                var saved = PrefabUtility.SaveAsPrefabAsset(root, DoctorPrefabPath);
                if (saved == null)
                    throw new InvalidOperationException($"Doctor prefab could not be saved: {DoctorPrefabPath}");

                var bounds = CalculateRendererBounds(saved);
                var head = saved.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(transform => transform.name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0);
                Debug.Log(
                    $"Mobile Veterinarian: doctor prefab prepared renderers={saved.GetComponentsInChildren<Renderer>(true).Length} " +
                    $"height={bounds.size.y:F2} head='{head?.name ?? "<missing>"}'.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void NormalizeVisual(GameObject visual)
        {
            var bounds = CalculateRendererBounds(visual);
            if (bounds.size.y <= 0.001f)
                throw new InvalidOperationException("Doctor model has no usable renderer bounds.");

            var scale = TargetHeight / bounds.size.y;
            visual.transform.localScale *= scale;
            bounds = CalculateRendererBounds(visual);
            visual.transform.localPosition += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
        }

        private static void BakeTreatmentPose(GameObject visual)
        {
            var poseClip = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));
            if (poseClip == null)
                throw new InvalidOperationException("Doctor model has no animation clip from which to bake its treatment pose.");

            var leftUpperArm = FindBone(visual, "Bip001 L UpperArm");
            var leftForearm = FindBone(visual, "Bip001 L Forearm");
            var rightUpperArm = FindBone(visual, "Bip001 R UpperArm");
            var rightForearm = FindBone(visual, "Bip001 R Forearm");
            var sampleCount = Mathf.Max(2, Mathf.CeilToInt(poseClip.length * 30f));
            var bestTime = 0f;
            var bestScore = float.NegativeInfinity;

            for (var index = 0; index <= sampleCount; index++)
            {
                var time = poseClip.length * index / sampleCount;
                poseClip.SampleAnimation(visual, time);

                // Prefer frames where both elbows are below their shoulders. This rejects
                // the imported bind/T-pose while preserving the authored notebook pose.
                var score = (leftUpperArm.position.y - leftForearm.position.y) +
                            (rightUpperArm.position.y - rightForearm.position.y);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTime = time;
                }
            }

            poseClip.SampleAnimation(visual, bestTime);
            Debug.Log(
                $"Mobile Veterinarian: baked doctor treatment pose clip='{poseClip.name}' " +
                $"time={bestTime:F2}s elbowDropScore={bestScore:F3}.");
        }

        private static Transform FindBone(GameObject visual, string nameFragment)
        {
            return visual.GetComponentsInChildren<Transform>(true)
                       .FirstOrDefault(transform =>
                           transform.name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? throw new InvalidOperationException($"Doctor model is missing bone '{nameFragment}'.");
        }

        private static Bounds CalculateRendererBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null && renderer.enabled)
                .ToArray();
            if (renderers.Length == 0)
                return default;

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static void UpdateManifest()
        {
            var manifest = AssetDatabase.LoadMainAssetAtPath(ManifestPath)
                ?? throw new InvalidOperationException($"Mod manifest was not found: {ManifestPath}");
            var serialized = new SerializedObject(manifest);
            SetString(serialized, "ModId", "MobileVeterinarian");
            SetString(serialized, "DisplayName", "Mobile Veterinarian");
            SetString(serialized, "AssetBundleName", BundleName + "." + BundleVariant);
            SetObject(serialized, "ModAssembly", AssetDatabase.LoadMainAssetAtPath(AssemblyPath));
            SetObject(serialized, "LocalesFolder", AssetDatabase.LoadMainAssetAtPath(LocalesPath));
            SetInt(serialized, "TargetPlatforms", 1);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manifest);
        }

        private static void BuildWindowsAssetBundle()
        {
            var importer = AssetImporter.GetAtPath(DoctorPrefabPath)
                ?? throw new InvalidOperationException($"No importer exists for '{DoctorPrefabPath}'.");
            importer.SetAssetBundleNameAndVariant(BundleName, BundleVariant);
            importer.SaveAndReimport();

            var outputDirectory = Path.Combine(ModRoot, "AssetBundles", "Windows");
            Directory.CreateDirectory(outputDirectory);
            var build = new AssetBundleBuild
            {
                assetBundleName = BundleName,
                assetBundleVariant = BundleVariant,
                assetNames = new[] { DoctorPrefabPath }
            };

            var result = BuildPipeline.BuildAssetBundles(
                outputDirectory,
                new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64);
            if (result == null)
                throw new InvalidOperationException("Unity returned no AssetBundleManifest for Mobile Veterinarian.");
        }

        private static void ValidateWindowsAssetBundle()
        {
            var relativePath = ModRoot + "/AssetBundles/Windows/" + BundleName + "." + BundleVariant;
            var absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
            var bundle = AssetBundle.LoadFromFile(absolutePath)
                ?? throw new InvalidOperationException($"Doctor AssetBundle could not be loaded: {absolutePath}");
            try
            {
                var prefab = bundle.LoadAsset<GameObject>(DoctorPrefabPath)
                    ?? throw new InvalidOperationException("Doctor AssetBundle is missing its prefab.");
                if (!prefab.GetComponentsInChildren<Renderer>(true).Any())
                    throw new InvalidOperationException("Bundled doctor prefab has no renderer.");
                if (!prefab.GetComponentsInChildren<Transform>(true)
                        .Any(transform => transform.name.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    throw new InvalidOperationException("Bundled doctor prefab has no identifiable head bone.");
                }
            }
            finally
            {
                bundle.Unload(true);
            }
        }

        private static void SetString(SerializedObject serialized, string name, string value)
        {
            var property = serialized.FindProperty(name)
                ?? throw new InvalidOperationException($"Manifest property '{name}' was not found.");
            property.stringValue = value;
        }

        private static void SetInt(SerializedObject serialized, string name, int value)
        {
            var property = serialized.FindProperty(name)
                ?? throw new InvalidOperationException($"Manifest property '{name}' was not found.");
            property.intValue = value;
        }

        private static void SetObject(SerializedObject serialized, string name, Object? value)
        {
            var property = serialized.FindProperty(name)
                ?? throw new InvalidOperationException($"Manifest property '{name}' was not found.");
            property.objectReferenceValue = value;
        }
    }
}
#endif
