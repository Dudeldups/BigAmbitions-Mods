using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BentleyContinentalGTSpeedLampRenderChecks
{
    public static void RunBatch()
    {
        try { Verify(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void VerifyAndBuildBatch()
    {
        try { Verify(); BentleyContinentalGTSpeedSetup.RepairAndBuildAssetBundlesBatch(); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void Verify()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Lamp pixel checks require a GPU; omit -nographics.");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mods/Bentley_Continental_GT_Speed/Lighting/BentleyLampEmitters.shader");
        var material = new Material(shader);
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-.8f, -.8f, .5f), new Vector3(.8f, -.8f, .5f), new Vector3(.8f, .8f, .5f), new Vector3(-.8f, .8f, .5f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create();
        var exposure = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var pixels = new Texture2D(32, 32, TextureFormat.RGBAFloat, false, true);
        var previous = RenderTexture.active;
        try
        {
            foreach (var multiplier in new[] { 1f, 1f / 32768f })
            {
                exposure.SetPixel(0, 0, new Color(multiplier, 1, 1, 1)); exposure.Apply();
                foreach (var test in new[] {
                    ("DRL", 0, "_DrlActive", 1f),
                    ("low beam", 1, "_FrontLightStrength", 1f),
                    ("front crystal pattern", 2, "_DrlActive", 1f),
                    ("tail", 3, "_BackLightStrength", 1f),
                    ("brake", 3, "_BackLightStrength", 3f),
                    ("high stop", 4, "_BackLightStrength", 3f),
                    ("reverse", 5, "_ReverseLightStrength", 1f),
                    ("indicator", 6, "_IsBlinkerOn", 1f),
                    ("plate", 7, "_FrontLightStrength", 1f),
                    ("amber marker", 8, "_FrontLightStrength", 1f),
                    ("red marker", 9, "_FrontLightStrength", 1f) })
                {
                    mesh.uv = new[] { new Vector2(test.Item2, -1), new Vector2(test.Item2, -1), new Vector2(test.Item2, -1), new Vector2(test.Item2, -1) };
                    var block = new MaterialPropertyBlock(); block.SetFloat(test.Item3, test.Item4);                     block.SetFloat("_IsLeftBlinkerOn", test.Item2 == 6 ? 1 : 0);
                    var on = Draw(mesh, material, block, target, exposure, pixels);
                    Debug.Log($"Bentley lamp pixels {test.Item1} exposure={multiplier:R} rgb={on.ToString("F6")}");
                    if (Mathf.Max(on.r, on.g, on.b) < .5f)
                        throw new InvalidOperationException($"Bentley {test.Item1} becomes invisible at exposure {multiplier:R}: {on}");
                    block.SetFloat(test.Item3, 0); block.SetFloat("_IsLeftBlinkerOn", 0);
                    var off = Draw(mesh, material, block, target, exposure, pixels);
                    if (Mathf.Max(off.r, off.g, off.b) > .001f)
                        throw new InvalidOperationException($"Bentley {test.Item1} stays lit when off: {off}");
                }
            }
            Debug.Log("Bentley lamp GPU pixel checks PASS: every function visible at neutral/daylight exposure and transparent when off.");
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(mesh); UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(exposure); UnityEngine.Object.DestroyImmediate(pixels);
            target.Release(); UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static Color Draw(Mesh mesh, Material material, MaterialPropertyBlock block, RenderTexture target, Texture exposure, Texture2D pixels)
    {
        using (var commands = new CommandBuffer())
        {
            commands.SetRenderTarget(target);
            commands.ClearRenderTarget(true, true, Color.clear);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            var globalsType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEngine.Rendering.HighDefinition.ShaderVariablesGlobal", false)).First(t => t != null);
            var globals = Activator.CreateInstance(globalsType);
            globalsType.GetField("_ViewProjMatrix").SetValue(globals, Matrix4x4.identity);
            globalsType.GetField("_ProbeExposureScale").SetValue(globals, 1f);
            var size = Marshal.SizeOf(globalsType);
            var pointer = Marshal.AllocHGlobal(size);
            int[] words = new int[size / 4];
            try { Marshal.StructureToPtr(globals, pointer, false); Marshal.Copy(pointer, words, 0, words.Length); }
            finally { Marshal.FreeHGlobal(pointer); }
            using var cameraBuffer = new ComputeBuffer(words.Length, 4, ComputeBufferType.Constant);
            cameraBuffer.SetData(words);
            commands.SetGlobalConstantBuffer(cameraBuffer, Shader.PropertyToID("ShaderVariablesGlobal"), 0, size);
            commands.SetGlobalTexture("_ExposureTexture", exposure);
            commands.SetGlobalFloat("_ProbeExposureScale", 1);
            commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0, block);
            Graphics.ExecuteCommandBuffer(commands);
        }
        RenderTexture.active = target;
        pixels.ReadPixels(new Rect(0, 0, 32, 32), 0, 0); pixels.Apply();
        return pixels.GetPixel(16, 16);
    }
}
