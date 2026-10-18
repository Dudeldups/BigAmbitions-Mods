#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

// The exported meshes contain source-local coordinates. Their GLB hierarchy and
// working-file world transforms are deliberately never instantiated into the car.
public static class DodgeRam1500TRXBlenderLights
{
    private const string Root = "Assets/Mods/Dodge_Ram_1500/";
    private const string Prefix = "DodgeRam1500TRX_Lamp_";
    [Serializable] public sealed class Group
    {
        public string name = "", objectName = "";
        public int vertexCount, faceCount;
        public int[] sourceVertices = Array.Empty<int>(), sourceFaces = Array.Empty<int>();
    }
    [Serializable] public sealed class Selection
    {
        public int version, sourceVertexCount, sourceFaceCount;
        public string sourceObjectName = "", productionSha256 = "", blendSha256 = "";
        public Group[] groups = Array.Empty<Group>();
    }
    public sealed class Binding
    {
        public readonly string Lamp, Group, Part;
        public readonly Color Color;
        public readonly float Intensity;
        public bool Rear => Lamp.StartsWith("Tail") || Lamp.StartsWith("Brake") ||
                            Lamp.StartsWith("Reverse") || Lamp.StartsWith("RearSignal");
        public Binding(string lamp, string group, string part, Color color, float intensity)
        { Lamp = lamp; Group = group; Part = part; Color = color; Intensity = intensity; }
    }
    public static readonly Binding[] Bindings = CreateBindings();
    private static Binding[] CreateBindings()
    {
        var result = new List<Binding>();
        var white = new Color(.90f, .95f, 1f);
        var amber = new Color(1f, .48f, .008f);
        var red = new Color(1f, .008f, .002f);
        foreach (var side in new[] { "Left", "Right" })
        {
            var groupSide = side == "Left" ? "left" : "right";
            result.Add(new Binding("Head" + side, "Headlamps", side, white, 6.4f));
            result.Add(new Binding("Drl" + side, "DRL", side, white, 3f));
            result.Add(new Binding("DrlIndicator" + side, "DRL_Indicator_" + groupSide, "All", white, 3f));
            result.Add(new Binding("Signal" + side, "DRL_Indicator_" + groupSide, "All", amber, 4f));
            result.Add(new Binding("Mirror" + side, "Mirror_Indicator_" + groupSide, "All", amber, 3f));
            result.Add(new Binding("Tail" + side, "Rear_Driving_Lights", side, red, 1.1f));
            result.Add(new Binding("Brake" + side, "Brake_Lights", side, red, 2f));
            result.Add(new Binding("Reverse" + side, "Reverse_Lights", side, white, 4.8f));
            result.Add(new Binding("RearSignal" + side, side == "Left" ? "Indicator_RL" : "Indicator_RR", "All", amber, 3f));
        }
        result.Add(new Binding("ThirdBrake", "Brake_Lights", "Center", red, 2f));
        return result.ToArray();
    }

    public static Selection ReadSelection() => JsonUtility.FromJson<Selection>(File.ReadAllText(Root + "Models/DodgeRamLightSelections.json"));
    public static Mesh ReadGroupMesh(Group group)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Models/DodgeRamLightOverlays.glb");
        if (model == null) throw new InvalidOperationException("TRX Blender light overlay GLB missing.");
        var mesh = model.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == group.objectName).sharedMesh;
        if (mesh == null || mesh.triangles.Length != group.faceCount * 3)
            throw new InvalidOperationException("TRX Blender overlay face count differs: " + group.name);
        return mesh;
    }
    private static MeshRenderer FindAnchor(GameObject root, Selection selection) =>
        root.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == selection.sourceObjectName);
    private static string PointKey(Vector3 p) => $"{Mathf.RoundToInt(p.x * 100000f)},{Mathf.RoundToInt(p.y * 100000f)},{Mathf.RoundToInt(p.z * 100000f)}";
    private static string FaceKey(Vector3 a, Vector3 b, Vector3 c) =>
        string.Join("/", new[] { PointKey(a), PointKey(b), PointKey(c) }.OrderBy(v => v, StringComparer.Ordinal));
    private static void ValidateSource(MeshRenderer anchor, Selection selection)
    {
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Root + "Models/2021_ram_1500_trx.glb"))).Replace("-", "").ToLowerInvariant();
        var source = anchor.GetComponent<MeshFilter>().sharedMesh;
        if (selection.version != 1 || selection.groups.Length != 11 || hash != selection.productionSha256 ||
            source.vertexCount != selection.sourceVertexCount || source.triangles.Length != selection.sourceFaceCount * 3)
            throw new InvalidOperationException("TRX Blender selections no longer match production topology.");
        var vertices = source.vertices; var triangles = source.triangles;
        var faces = new HashSet<string>();
        for (var i = 0; i < triangles.Length; i += 3)
            faces.Add(FaceKey(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]));
        foreach (var group in selection.groups)
        {
            var mesh = ReadGroupMesh(group); var v = mesh.vertices; var t = mesh.triangles;
            for (var i = 0; i < t.Length; i += 3)
                if (!faces.Contains(FaceKey(v[t[i]], v[t[i + 1]], v[t[i + 2]])))
                    throw new InvalidOperationException("TRX overlay is not in production mesh local coordinates: " + group.name);
        }
    }
    public static List<int> SelectFaces(GameObject root, MeshRenderer anchor, Mesh mesh, Binding binding)
    {
        var p = mesh.vertices.Select(v => root.transform.InverseTransformPoint(anchor.transform.TransformPoint(v))).ToArray();
        bool InPart(int i) => binding.Part == "All" || (binding.Part == "Center" ? Mathf.Abs(p[i].x) < .25f :
            binding.Part == "Left" ? p[i].x < -.25f : p[i].x > .25f);
        var result = new List<int>(); var triangles = mesh.triangles;
        for (var i = 0; i < triangles.Length; i += 3)
            if (InPart(triangles[i]) && InPart(triangles[i + 1]) && InPart(triangles[i + 2]))
                result.AddRange(new[] { triangles[i], triangles[i + 1], triangles[i + 2] });
        if (result.Count == 0) throw new InvalidOperationException("TRX Blender lamp partition empty: " + binding.Lamp);
        ValidateBounds(root, anchor, mesh, result, binding);
        return result;
    }
    private static void ValidateBounds(GameObject root, MeshRenderer anchor, Mesh mesh, List<int> ids, Binding binding)
    {
        var vertices = mesh.vertices;
        var p = ids.Distinct().Select(i => root.transform.InverseTransformPoint(anchor.transform.TransformPoint(vertices[i]))).ToArray();
        var front = binding.Lamp.StartsWith("Head") || binding.Lamp.StartsWith("Drl") || binding.Lamp.StartsWith("Signal");
        var mirror = binding.Lamp.StartsWith("Mirror");
        if ((front && p.Any(v => v.z < 2.3f)) || (binding.Rear && p.Any(v => v.z > -2.6f)) ||
            (mirror && p.Any(v => Mathf.Abs(v.x) < 1.05f || v.z < .5f || v.z > 1.5f)) ||
            (binding.Lamp.EndsWith("Left") && p.Any(v => v.x >= 0f)) ||
            (binding.Lamp.EndsWith("Right") && p.Any(v => v.x <= 0f)) ||
            (binding.Lamp == "ThirdBrake" && p.Any(v => DodgeRam1500TRXBodyFit.UnrakePoint(v).y < 1.77f)))
            throw new InvalidOperationException("TRX Blender lamp world bounds/side invalid: " + binding.Lamp);
    }
    public static void Configure(GameObject root)
    {
        var selection = ReadSelection(); var anchor = FindAnchor(root, selection);
        ValidateSource(anchor, selection);
        foreach (var binding in Bindings)
        {
            var group = selection.groups.Single(g => g.name == binding.Group);
            var mesh = ReadGroupMesh(group);
            DodgeRam1500TRXDetailAuthoring.AddSelectedLamp(root, anchor, binding.Lamp,
                SelectFaces(root, anchor, mesh, binding), binding.Color, binding.Intensity, binding.Rear, mesh);
        }
        ValidateAuthored(root);
    }
    public static void ValidateAuthored(GameObject root)
    {
        var selection = ReadSelection(); var anchor = FindAnchor(root, selection);
        ValidateSource(anchor, selection);
        foreach (var binding in Bindings)
        {
            var renderer = root.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == Prefix + binding.Lamp);
            var mesh = ReadGroupMesh(selection.groups.Single(g => g.name == binding.Group));
            var chosen = SelectFaces(root, anchor, mesh, binding);
            var actual = renderer.GetComponent<MeshFilter>().sharedMesh;
            var sourceVertices = mesh.vertices; var sourceNormals = mesh.normals;
            var unique = chosen.Distinct().ToArray();
            if (renderer.transform.parent != anchor.transform || renderer.transform.localPosition != Vector3.zero ||
                renderer.transform.localRotation != Quaternion.identity || renderer.transform.localScale != Vector3.one ||
                actual.triangles.Length != chosen.Count || actual.vertexCount != unique.Length)
                throw new InvalidOperationException("TRX Blender overlay identity parenting/count invalid: " + binding.Lamp);
            var actualVertices = actual.vertices;
            for (var i = 0; i < unique.Length; i++)
            {
                var index = unique[i];
                var normal = anchor.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(sourceNormals[index]).normalized;
                var offset = binding.Rear ? root.transform.TransformVector(Vector3.back * .0005f) : normal * .0005f;
                var expected = sourceVertices[index] + anchor.transform.InverseTransformVector(offset);
                if (Vector3.Distance(actualVertices[i], expected) > .00001f)
                    throw new InvalidOperationException("TRX Blender lamp local coordinates changed: " + binding.Lamp);
            }
            Debug.Log($"TRX Blender lamp {binding.Lamp}: group={binding.Group}, faces={chosen.Count / 3}, identityAnchor={anchor.name}, bounds={renderer.bounds}.");
        }
        Debug.Log($"TRX Blender selections passed: sourceVertices={selection.sourceVertexCount}, sourceFaces={selection.sourceFaceCount}, groups=11, overlays={Bindings.Length}, sourceSHA256={selection.blendSha256}.");
    }
    public static void RebuildAndBuild()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var path = Root + "DodgeRam1500TRX.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try { Configure(root); PrefabUtility.SaveAsPrefabAsset(root, path); AssetDatabase.SaveAssets(); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        DodgeRam1500TRXSetup.BuildExistingAndVerify();
    }
}
