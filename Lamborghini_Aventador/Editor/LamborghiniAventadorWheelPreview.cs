#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>Controlled HDRP material comparison; never an in-game acceptance test.</summary>
public static class LamborghiniAventadorWheelPreview
{
    public static void BuildAndCapture()
    {
        LamborghiniAventadorSetup.GenerateAndBuild();
        Capture();
    }

    public static void Capture()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Wheel preview requires the isolated build script's -Graphics switch.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        const string root = "Assets/Mods/Lamborghini_Aventador/";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(root + "LamborghiniAventador.prefab");
        var car = UnityEngine.Object.Instantiate(prefab);
        var wheel = car.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Geometry_Wheel_FR");
        var rimRenderers = wheel.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in car.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = rimRenderers.Contains(renderer);

        var runtimeType = typeof(LamborghiniAventadorSpawnAppearance).Assembly
            .GetType("LamborghiniAventadorGlassController", true)!;
        var normalizer = runtimeType.GetMethod("NormalizeRuntimeMaterial", BindingFlags.NonPublic | BindingFlags.Static)!;
        var roleType = runtimeType.GetNestedType("MaterialRole", BindingFlags.NonPublic)!;
        foreach (var renderer in rimRenderers)
        {
            var material = new Material(renderer.sharedMaterial);
            var role = material.name.Contains("Rubber") ? "WheelRubber" : "WheelRim";
            normalizer.Invoke(null, new[] { (object)material, Enum.Parse(roleType, role) });
            renderer.sharedMaterial = material;
            Debug.Log($"Aventador wheel preview material {renderer.name}: role={role}, shader={material.shader.name}, base={material.GetColor("_BaseColor")}, metallic={material.GetFloat("_Metallic")}, smoothness={material.GetFloat("_Smoothness")}, normal={material.GetTexture("_NormalMap")?.name}.");
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var triangles = mesh.triangles;
            var matrix = filter.transform.localToWorldMatrix;
            var normalMatrix = matrix.inverse.transpose;
            var opposedArea = 0f;
            var totalArea = 0f;
            for (var index = 0; index < triangles.Length; index += 3)
            {
                var a = triangles[index]; var b = triangles[index + 1]; var c = triangles[index + 2];
                var face = Vector3.Cross(matrix.MultiplyPoint3x4(vertices[b]) - matrix.MultiplyPoint3x4(vertices[a]),
                    matrix.MultiplyPoint3x4(vertices[c]) - matrix.MultiplyPoint3x4(vertices[a]));
                var normal = normalMatrix.MultiplyVector(normals[a] + normals[b] + normals[c]);
                totalArea += face.magnitude;
                if (Vector3.Dot(face, normal) < 0f) opposedArea += face.magnitude;
            }
            Debug.Log($"Aventador wheel normals {renderer.name}: opposedTriangleArea={opposedArea / Mathf.Max(totalArea, 1e-8f):P2}, determinant={matrix.determinant}.");
        }

        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var volume = new GameObject("Preview environment").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
        profile.Add<VisualEnvironment>(true).skyType.Override((int)SkyType.Gradient);
        var sky = profile.Add<GradientSky>(true);
        sky.top.Override(new Color(.25f, .32f, .45f));
        sky.middle.Override(new Color(.65f, .68f, .72f));
        sky.bottom.Override(new Color(.12f, .12f, .12f));
        sky.skyIntensityMode.Override(SkyIntensityMode.Multiplier);
        sky.multiplier.Override(5000f);
        profile.Add<Exposure>(true).fixedExposure.Override(10f);

        var light = new GameObject("Preview sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(35f, -55f, 0f);
        light.gameObject.AddComponent<HDAdditionalLightData>().SetIntensity(20000f, LightUnit.Lux);
        var camera = new GameObject("Preview camera").AddComponent<Camera>();
        camera.gameObject.AddComponent<HDAdditionalCameraData>().volumeLayerMask = ~0;
        var bounds = rimRenderers[0].bounds;
        foreach (var renderer in rimRenderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        camera.transform.position = bounds.center + new Vector3(2.4f, .22f, .12f);
        camera.transform.LookAt(bounds.center);
        camera.fieldOfView = 25f;
        camera.nearClipPlane = .05f;
        var target = new RenderTexture(768, 768, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        target.Create();
        var project = Directory.GetParent(Application.dataPath)!.FullName;
        var output = Path.Combine(project, "obj", "VehicleVerification", "Lamborghini_Aventador", "WheelAppearance");
        Directory.CreateDirectory(output);
        Render(camera, target, Path.Combine(output, "rim-rubber-hdrp.png"));
        var source = AssetDatabase.LoadAllAssetsAtPath(root + "Models/lamborghini_aventador_svj.glb")
            .OfType<Material>().First(m => m.name.Contains("Wheel2A_3D_3DWheel2A_Material"));
        var original = new Material(source);
        normalizer.Invoke(null, new[] { (object)original, Enum.Parse(roleType, "Opaque") });
        foreach (var renderer in rimRenderers) renderer.sharedMaterial = original;
        Render(camera, target, Path.Combine(output, "original-black-hdrp.png"));
        Debug.Log($"Aventador controlled HDRP wheel comparison saved to {output}; device={SystemInfo.graphicsDeviceType}. Not an in-game screenshot.");
    }

    private static void Render(Camera camera, RenderTexture target, string path)
    {
        for (var frame = 0; frame < 8; frame++) camera.Render();
        RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(image);
    }
}
