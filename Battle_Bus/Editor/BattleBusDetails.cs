#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class BattleBusSetup
{
    [Serializable] private sealed class SelectionFile { public Selection[] groups = Array.Empty<Selection>(); }
    [Serializable] private sealed class Selection { public string source = ""; public string name = ""; public int sourceVertexCount; public Vector3[] vertices = Array.Empty<Vector3>(); }
    private static Vector3Int VertexKey(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 1000), Mathf.RoundToInt(p.y * 1000), Mathf.RoundToInt(p.z * 1000));
    private static void ExtractBlenderSelections(Transform model, GameObject root)
    {
        var data = JsonUtility.FromJson<SelectionFile>(System.IO.File.ReadAllText("Assets/Mods/Battle_Bus/Models/BlenderSelections.json"));
        foreach (var group in data.groups)
        {
            var source = FindTransform(model, group.source)?.GetComponent<MeshFilter>() ?? throw new InvalidOperationException("Missing selection source " + group.source);
            var mesh = source.sharedMesh;
            var selected = new HashSet<Vector3Int>(group.vertices.Select(VertexKey));
            var vertices = mesh.vertices;
            var triangles = new List<int>[mesh.subMeshCount];
            var remainder = new List<int>[mesh.subMeshCount];
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
            {
                triangles[sub] = new List<int>(); remainder[sub] = new List<int>();
                var indices = mesh.GetTriangles(sub);
                for (var i = 0; i < indices.Length; i += 3)
                {
                    var chosen = selected.Contains(VertexKey(vertices[indices[i]])) && selected.Contains(VertexKey(vertices[indices[i+1]])) && selected.Contains(VertexKey(vertices[indices[i+2]]));
                    var list = chosen ? triangles[sub] : remainder[sub];
                    list.Add(indices[i]);list.Add(indices[i+1]);list.Add(indices[i+2]);
                }
            }
            var faces = triangles.Sum(t => t.Count) / 3;
            if (faces == 0) throw new InvalidOperationException("Blender selection did not match production coordinates: " + group.name + " first=" + vertices[0] + " selection=" + group.vertices[0]);
            var part = CreateFilteredMesh(mesh, triangles, group.name);
            var obj = new GameObject(group.name == "Propeller" ? "BattleBusPropeller" : "BattleBusLamp_" + group.name) { layer=source.gameObject.layer };
            obj.transform.SetParent(source.transform, false);
            if (group.name == "Propeller")
            {
                // Measured centroid of the 73-vertex hub, not the asymmetric blade bounds.
                var center = new Vector3(-21.113703f,66.672374f,-26.294481f);
                var v = part.vertices; for (var i=0;i<v.Length;i++) v[i]-=center;part.vertices=v;part.RecalculateBounds();obj.transform.localPosition=center;
                source.sharedMesh=SaveGeneratedMesh(CreateFilteredMesh(mesh,remainder,"WithoutPropeller"),GeneratedMeshFolder+"/WithoutPropeller.asset");
            }
            else
            {
                var v=part.vertices;var n=part.normals;
                // Offset by 0.3 mm in vehicle space, keeping the exact source transform.
                for(var i=0;i<v.Length;i++)v[i]+=n[i]*(0.0003f / source.transform.lossyScale.x);
                part.vertices=v;part.RecalculateBounds();
            }
            var path=GeneratedMeshFolder+"/Blender_"+group.name+".asset";
            obj.AddComponent<MeshFilter>().sharedMesh=SaveGeneratedMesh(part,path);SetBundle(path);
            var renderer=obj.AddComponent<MeshRenderer>();renderer.sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;
            if(group.name!="Propeller")
            {
                var color=group.name=="Headlamps" ? new Color(1f,.94f,.8f) : group.name.StartsWith("Indicator") ? new Color(1f,.28f,.005f) : new Color(.9f,.002f,.001f);
                var matPath=GeneratedMaterialFolder+"/Lamp_"+group.name+".mat";
                var mat=AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if(mat==null){mat=new Material(Shader.Find("HDRP/Unlit"));AssetDatabase.CreateAsset(mat,matPath);}
                mat.SetColor("_UnlitColor",color*2f);mat.SetColor("_EmissiveColor",Color.black);mat.SetFloat("_CullMode",0);EditorUtility.SetDirty(mat);SetBundle(matPath);
                renderer.sharedMaterial=mat;renderer.shadowCastingMode=ShadowCastingMode.Off;
            }
            Debug.Log($"Battle Bus Blender selection {group.source}/{group.name}: {faces} faces, world bounds={renderer.bounds}.");
        }
    }
    private static Material CreateCapPaintMaterial(Material source)
    {
        var path=GeneratedMaterialFolder+"/BattleBusPaint_BalloonAtlas.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null){material=new Material(source);AssetDatabase.CreateAsset(material,path);}
        EditorUtility.CopySerialized(source,material);material.name="BattleBusPaint_BalloonAtlas";
        var texture=GetMaterialBaseColorTexture(source) as Texture2D;
        if(texture==null || !TryReadEmbeddedTexturePixels(texture,out var pixels,out var width,out var height))throw new InvalidOperationException("Balloon paint atlas not readable.");
        var texPath=GeneratedMaterialFolder+"/BalloonSourceAtlas.asset";
        var readable=new Texture2D(width,height,TextureFormat.RGBA32,true,false){name="BalloonSourceAtlas",filterMode=FilterMode.Bilinear};
        readable.SetPixels32(pixels);readable.Apply();
        var saved=AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if(saved==null){AssetDatabase.CreateAsset(readable,texPath);saved=readable;}else{EditorUtility.CopySerialized(readable,saved);UnityEngine.Object.DestroyImmediate(readable);}
        material.SetTexture("_BaseColorMap",saved);material.SetColor("_BaseColor",Color.white);EditorUtility.SetDirty(material);EditorUtility.SetDirty(saved);SetBundle(path);SetBundle(texPath);return material;
    }
    private static void CorrectEntryGlass(Transform model, Transform root)
    {
        var glass=FindTransform(model,"Object_18")!.GetComponent<MeshFilter>();
        var mat=glass.GetComponent<MeshRenderer>().sharedMaterial;
        mat.SetTexture("_BaseColorMap",null);mat.SetColor("_BaseColor",new Color(.085f,.09f,.095f,.82f));EditorUtility.SetDirty(mat);
    }
    private static void SeparateBalloonRig(Transform model, Transform root)
    {
        foreach(var name in new[]{"Object_10","Object_12","Object_16","Object_23"})
        {
            var filter=FindTransform(model,name)!.GetComponent<MeshFilter>();var source=filter.sharedMesh;
            var vertices=source.vertices;
            // Own complete rope components, including faces whose centroid lies
            // below the envelope split. Leaving those behind created rigid copies.
            var connected=new UnionFind(vertices.Length);
            for(var sub=0;sub<source.subMeshCount;sub++)
            {
                var faces=source.GetTriangles(sub);
                for(var i=0;i<faces.Length;i+=3){connected.Join(faces[i],faces[i+1]);connected.Join(faces[i+1],faces[i+2]);}
            }
            var moving=new HashSet<int>();
            for(var i=0;i<vertices.Length;i++)
                if(root.InverseTransformPoint(filter.transform.TransformPoint(vertices[i])).y>3.7f)moving.Add(connected.Find(i));
            var upper=new List<int>[source.subMeshCount];var lower=new List<int>[source.subMeshCount];
            for(var sub=0;sub<source.subMeshCount;sub++)
            {
                upper[sub]=new List<int>();lower[sub]=new List<int>();var triangles=source.GetTriangles(sub);
                for(var i=0;i<triangles.Length;i+=3)
                {
                    var target=moving.Contains(connected.Find(triangles[i]))?upper[sub]:lower[sub];for(var n=0;n<3;n++)target.Add(triangles[i+n]);
                }
            }
            if(upper.Sum(t=>t.Count)==0)continue;
            var obj=new GameObject("BattleBusBalloonRig_"+name){layer=filter.gameObject.layer};obj.transform.SetParent(filter.transform,false);
            var path=GeneratedMeshFolder+"/BalloonRig_"+name+".asset";
            obj.AddComponent<MeshFilter>().sharedMesh=SaveGeneratedMesh(CreateFilteredMesh(source,upper,obj.name),path);SetBundle(path);
            obj.AddComponent<MeshRenderer>().sharedMaterials=filter.GetComponent<MeshRenderer>().sharedMaterials;
            if(lower.Sum(t=>t.Count)>0){var lowerPath=GeneratedMeshFolder+"/BelowBalloon_"+name+".asset";filter.sharedMesh=SaveGeneratedMesh(CreateFilteredMesh(source,lower,name),lowerPath);SetBundle(lowerPath);}
            else {filter.sharedMesh=null;filter.GetComponent<MeshRenderer>().enabled=false;}
        }
    }
    private static void ConfigureAddedVisuals(GameObject root, Transform model)
    {
        foreach(var light in root.GetComponentsInChildren<Light>(true))
            if(light.name=="Spotlights")light.transform.position+=root.transform.forward*2.25f;
        foreach(var name in new[]{"FrontLeft_WheelController","FrontRight_WheelController"})
        {
            var wheel=FindTransform(root.transform,name)!;
            FindOrCreateWheelVisual(wheel);
            var cap=GameObject.CreatePrimitive(PrimitiveType.Sphere);cap.name="BattleBusFrontHubCover";
            UnityEngine.Object.DestroyImmediate(cap.GetComponent<Collider>());
            var parent=FindTransform(wheel,name+"_CamberPivot")!;cap.transform.SetParent(parent,false);
            cap.transform.localPosition=new Vector3(name.Contains("Left") ? -.125f : .125f,0,0);
            cap.transform.localScale=new Vector3(.025f,.105f,.105f);
            var path=GeneratedMaterialFolder+"/HubCover.mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(mat==null){mat=new Material(Shader.Find("HDRP/Lit"));AssetDatabase.CreateAsset(mat,path);}
            mat.SetColor("_BaseColor",new Color(.24f,.22f,.12f));mat.SetFloat("_Metallic",.7f);mat.SetFloat("_Smoothness",.35f);EditorUtility.SetDirty(mat);SetBundle(path);cap.GetComponent<MeshRenderer>().sharedMaterial=mat;
        }
        var capMaterial=FindTransform(model,"BattleBusPaintBalloonCap")!.GetComponent<MeshRenderer>().sharedMaterial;
        var fixedPath=GeneratedMaterialFolder+"/BattleBusBalloonAtlas.mat";
        var fixedMaterial=AssetDatabase.LoadAssetAtPath<Material>(fixedPath);
        if(fixedMaterial==null){fixedMaterial=new Material(capMaterial);AssetDatabase.CreateAsset(fixedMaterial,fixedPath);}
        EditorUtility.CopySerialized(capMaterial,fixedMaterial);fixedMaterial.name="BattleBusBalloonAtlas";EditorUtility.SetDirty(fixedMaterial);SetBundle(fixedPath);
        FindTransform(model,"Object_14_OriginalColorBalloon")!.GetComponent<MeshRenderer>().sharedMaterial=fixedMaterial;
        var controller=root.GetComponent<BattleBusVisualController>()??root.AddComponent<BattleBusVisualController>();
        var serialized=new SerializedObject(controller);
        serialized.FindProperty("propeller").objectReferenceValue=FindTransform(model,"BattleBusPropeller");
        serialized.FindProperty("propellerAxis").vector3Value=new Vector3(0f,-.2588299f,-.9659229f);
        SetObjectArray(serialized,"balloonMeshes",model.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.name=="Object_14_OriginalColorBalloon" || f.name=="BattleBusPaintBalloonCap" || f.name.StartsWith("BattleBusBalloonRig_")).Cast<UnityEngine.Object>().ToArray());
        SetObjectArray(serialized,"lamps",model.GetComponentsInChildren<MeshRenderer>(true).Where(r=>r.name.StartsWith("BattleBusLamp_")).Cast<UnityEngine.Object>().ToArray());
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
