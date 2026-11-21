#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BentleyContinentalGTSpeedLightingSetup
{
    private const string Root="Assets/Mods/Bentley_Continental_GT_Speed/Lighting";
    private const string RigName="BentleyLampEmitters";
    [Serializable] private sealed class Map { public Group[] groups=Array.Empty<Group>(); }
    [Serializable] private sealed class Group {
        public string source="",role="",signature="";
        public int vertices;
        public int[] triangles=Array.Empty<int>();
    }
    private static readonly string[] Roles={"FrontDRL","Headlamp","FrontPattern","TailStop","HighStop","Reverse","RearIndicator","Plate","AmberMarker","RedMarker"};

    public static void Author(GameObject vehicle)
    {
        var old=vehicle.transform.Find(RigName);
        if(old!=null)UnityEngine.Object.DestroyImmediate(old.gameObject);
        var map=JsonUtility.FromJson<Map>(AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"/BentleyLampFaces.json").text);
        var positions=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var indices=new List<int>();
        var verified=new HashSet<string>();var sourceTransforms=vehicle.GetComponentsInChildren<Transform>(true);
        foreach(var group in map.groups)
        {
            var source=sourceTransforms.Single(t=>t.name==group.source).GetComponent<SkinnedMeshRenderer>();
            if(source==null||source.sharedMesh==null||source.sharedMesh.vertexCount!=group.vertices)
                throw new InvalidOperationException("Bentley lamp source topology changed: "+group.source);
            var original=source.sharedMesh.GetTriangles(0);
            if(verified.Add(group.source))
            {
                var text="["+string.Join(", ",Enumerable.Range(0,original.Length/3).Select(i=>"["+string.Join(", ",new[]{original[i*3],original[i*3+1],original[i*3+2]}.OrderBy(v=>v))+"]"))+"]";
                using var sha=SHA256.Create();
                var signature=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
                if(signature!=group.signature)throw new InvalidOperationException("Bentley lamp triangle ordering changed: "+group.source);
            }
            var role=Array.IndexOf(Roles,group.role=="MirrorIndicator"?"RearIndicator":group.role);
            if(role<0||group.triangles.Length==0)throw new InvalidOperationException("Empty/unknown Bentley lamp group: "+group.role);
            var baked=new Mesh();source.BakeMesh(baked);
            try
            {
                var verts=baked.vertices;var ns=baked.normals;var remap=new Dictionary<int,int>();
                if(group.role=="Reverse" || group.role=="RearIndicator")VerifyRearRibbon(group,original,verts,source.transform,vehicle.transform);
                foreach(var face in group.triangles)
                {
                    if(face<0||face*3+2>=original.Length)throw new InvalidOperationException("Bentley lamp face out of range");
                    for(var corner=0;corner<3;corner++)
                    {
                        var vi=original[face*3+corner];
                        if(!remap.TryGetValue(vi,out var index))
                        {
                            var normal=vehicle.transform.InverseTransformDirection(source.transform.TransformDirection(ns[vi])).normalized;
                            var point=vehicle.transform.InverseTransformPoint(source.transform.TransformPoint(verts[vi]));
                            index=positions.Count;remap[vi]=index;
                            positions.Add(point+normal*.0005f);normals.Add(normal);
                            uv.Add(new Vector2(role,point.x<0?-1:1));
                        }
                        indices.Add(index);
                    }
                }
                Debug.Log($"Bentley lamp authored source={group.source} role={group.role} faces={group.triangles.Length} vertices={remap.Count}");
            }
            finally {UnityEngine.Object.DestroyImmediate(baked);}
        }
        var mesh = PrepareEmitterMesh("BentleyLampEmittersMesh", Root + "/BentleyLampEmitters.asset");
        mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        var shader=AssetDatabase.LoadAssetAtPath<Shader>(Root+"/BentleyLampEmitters.shader")??throw new InvalidOperationException("Bentley lamp shader missing");
        var player=Save(new Material(shader){name="Bentley_LampEmitters_Prepared"},Root+"/BentleyLampEmitters.mat");
        var npc=new Material(shader){name="Bentley_LampEmittersNpc_Prepared"};npc.SetFloat("_DrlActive",1);
        var npcAsset=Save(npc,Root+"/BentleyLampEmittersNpc.mat");
        var go=new GameObject(RigName);go.transform.SetParent(vehicle.transform,false);
        go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=player;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        var rig=go.AddComponent<BentleyContinentalGTSpeedLampRig>();rig.emitters=renderer;rig.npcMaterial=npcAsset;
        // Player emitters use exactly the source mesh's transform, skin weights and bones.
        // The combined rest-pose mesh is retained exclusively for native Gley traffic.
        rig.playerEmitters=AuthorSkinnedEmitters(vehicle,map.groups,player);
        renderer.enabled=false;
        var front=positions.Max(v=>v.z);
        foreach(var source in vehicle.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if(!source.sharedMaterials.Any(m=>m!=null&&m.name.StartsWith("Bentley_Paint_Prepared",StringComparison.Ordinal)))continue;
            var baked=new Mesh();source.BakeMesh(baked);
            foreach(var v in baked.vertices)front=Mathf.Max(front,vehicle.transform.InverseTransformPoint(source.transform.TransformPoint(v)).z);
            UnityEngine.Object.DestroyImmediate(baked);
        }
        var head=positions.Where((v,i)=>Mathf.RoundToInt(uv[i].x)==1).ToArray();
        var height=head.Average(v=>v.y);
        rig.npcBeamPosition=new Vector3(0,height,front+.07f);
        var sourceBeam=vehicle.GetComponentsInChildren<Light>(true).Single(l=>l.name=="Spotlights");
        var previous=vehicle.GetComponentsInChildren<Transform>(true).SingleOrDefault(t=>t.name=="BentleyRoadBeamRight");
        if(previous!=null)UnityEngine.Object.DestroyImmediate(previous.gameObject);
        var second=UnityEngine.Object.Instantiate(sourceBeam.gameObject,sourceBeam.transform.parent,false);
        second.name="BentleyRoadBeamRight";
        var beams=new[]{sourceBeam,second.GetComponent<Light>()};
        foreach(var beam in beams)
        {
            var left=beam==sourceBeam;
            var side=head.Where(v=>(v.x<0)==left).ToArray();
            beam.transform.position=vehicle.transform.TransformPoint(new Vector3(side.Average(v=>v.x),height,front+.07f));
            beam.transform.rotation=vehicle.transform.rotation*Quaternion.Euler(12,0,0);
            beam.color=new Color(.88f,.94f,1);beam.range=55;beam.spotAngle=70;beam.cookie=null;beam.intensity=90000;
            foreach(var data in beam.GetComponents<MonoBehaviour>())
            {
                if(data.GetType().FullName!="UnityEngine.Rendering.HighDefinition.HDAdditionalLightData")continue;
                // Two lights together retain the donor's 5000 lux at 6 m (180000 cd).
                var so=new SerializedObject(data);so.FindProperty("m_LightUnit").intValue=2;so.FindProperty("m_LuxAtDistance").floatValue=6;so.FindProperty("m_Intensity").floatValue=2500;so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        Verify(vehicle);
    }

    private static SkinnedMeshRenderer[] AuthorSkinnedEmitters(GameObject vehicle,Group[] groups,Material material)
    {
        foreach(var old in vehicle.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r=>r.name.StartsWith(RigName+"_",StringComparison.Ordinal)).ToArray())
            UnityEngine.Object.DestroyImmediate(old.gameObject);
        var result=new List<SkinnedMeshRenderer>();
        foreach(var sourceGroups in groups.GroupBy(g=>g.source))
        {
            var source=vehicle.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.name==sourceGroups.Key);
            var original=source.sharedMesh;var triangles=original.GetTriangles(0);
            var raw=original.vertices;var rawNormals=original.normals;var weights=original.boneWeights;
            var baked=new Mesh();source.BakeMesh(baked);var posed=baked.vertices;
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var bones=new List<BoneWeight>();
            var tags=new List<Vector2>();var sourceIndices=new List<Vector2>();var indices=new List<int>();
            foreach(var group in sourceGroups)
            {
                var role=Array.IndexOf(Roles,group.role=="MirrorIndicator"?"RearIndicator":group.role);
                var remap=new Dictionary<int,int>();
                foreach(var face in group.triangles)
                    for(var corner=0;corner<3;corner++)
                    {
                        var vi=triangles[face*3+corner];
                        if(!remap.TryGetValue(vi,out var index))
                        {
                            index=vertices.Count;remap[vi]=index;
                            vertices.Add(raw[vi]+rawNormals[vi]*.0005f);normals.Add(rawNormals[vi]);bones.Add(weights[vi]);
                            var point=vehicle.transform.InverseTransformPoint(source.transform.TransformPoint(posed[vi]));
                            tags.Add(new Vector2(role,point.x<0?-1:1));
                            sourceIndices.Add(new Vector2(vi,0));
                        }
                        indices.Add(index);
                    }
            }
            UnityEngine.Object.DestroyImmediate(baked);
            var meshName = RigName + "_" + source.name;
            var mesh = PrepareEmitterMesh(meshName, Root + "/" + meshName + ".asset");
            mesh.SetVertices(vertices);mesh.SetNormals(normals);mesh.SetUVs(0,tags);mesh.SetUVs(1,sourceIndices);mesh.SetTriangles(indices,0);
            mesh.boneWeights=bones.ToArray();mesh.bindposes=original.bindposes;mesh.RecalculateBounds();
            var child=new GameObject(mesh.name);child.layer=source.gameObject.layer;child.transform.SetParent(source.transform,false);
            var overlay=child.AddComponent<SkinnedMeshRenderer>();overlay.sharedMesh=mesh;
            EditorUtility.SetDirty(mesh);
            overlay.sharedMaterial=material;overlay.bones=source.bones;overlay.rootBone=source.rootBone;
            overlay.localBounds=source.localBounds;overlay.updateWhenOffscreen=source.updateWhenOffscreen;
            overlay.quality=source.quality;overlay.renderingLayerMask=source.renderingLayerMask;
            overlay.shadowCastingMode=ShadowCastingMode.Off;overlay.receiveShadows=false;result.Add(overlay);
        }
        return result.ToArray();
    }

    private static void VerifyRearRibbon(Group group,int[] indices,Vector3[] vertices,Transform source,Transform vehicle)
    {
        var parent=Enumerable.Range(0,group.triangles.Length).ToArray();
        var shared=new Dictionary<Vector3Int,int>();
        var points=new Vector3[group.triangles.Length][];
        int Find(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
        for(var face=0;face<group.triangles.Length;face++)
        {
            points[face]=new Vector3[3];
            for(var corner=0;corner<3;corner++)
            {
                var p=vehicle.InverseTransformPoint(source.TransformPoint(vertices[indices[group.triangles[face]*3+corner]]));
                points[face][corner]=p;
                var key=new Vector3Int(Mathf.RoundToInt(p.x*100000),Mathf.RoundToInt(p.y*100000),Mathf.RoundToInt(p.z*100000));
                if(shared.TryGetValue(key,out var other))parent[Find(face)]=Find(other);else shared[key]=face;
            }
        }
        var islands = Enumerable.Range(0, points.Length).GroupBy(Find).ToArray();
        var sides = new HashSet<int>();
        foreach (var island in islands)
        {
            var corners = island.SelectMany(i => points[i]).ToArray();
            float left = corners.Min(p => p.x), right = corners.Max(p => p.x);
            if (right - left < .1f || left * right <= 0 || !sides.Add(Math.Sign(left)))
                throw new InvalidOperationException($"Bentley {group.role} must have one continuous lens face on each side.");
        }
        if (sides.Count != 2)
            throw new InvalidOperationException($"Bentley {group.role} is missing a rear lens.");
        Debug.Log($"Bentley {group.role} verified both continuous lens faces ({group.triangles.Length} triangles).");
    }

    private static Mesh PrepareEmitterMesh(string name, string path)
    {
        // CopySerialized can leave the destination Mesh's native vertex buffers in place.
        // Rebuild the existing asset directly to preserve both its GUID and buffer consistency.
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            mesh = new Mesh { name = name };
            AssetDatabase.CreateAsset(mesh, path);
            var importer = AssetImporter.GetAtPath(path);
            importer.assetBundleName = "bentleycontinentalgtspeed";
            importer.assetBundleVariant = "unity3d";
        }
        mesh.Clear(false);
        mesh.indexFormat = IndexFormat.UInt32;
        return mesh;
    }

    private static T Save<T>(T value,string path) where T:UnityEngine.Object
    {
        var authoredName=value.name;
        var existing=AssetDatabase.LoadAssetAtPath<T>(path);
        if(existing==null){AssetDatabase.CreateAsset(value,path);existing=value;}
        else {EditorUtility.CopySerialized(value,existing);UnityEngine.Object.DestroyImmediate(value);EditorUtility.SetDirty(existing);}
        existing.name=authoredName;EditorUtility.SetDirty(existing);
        var importer=AssetImporter.GetAtPath(path);importer.assetBundleName="bentleycontinentalgtspeed";importer.assetBundleVariant="unity3d";
        return existing;
    }

    public static void Verify(GameObject vehicle)
    {
        var rig=vehicle.GetComponentInChildren<BentleyContinentalGTSpeedLampRig>(true);
        if(rig==null||rig.emitters==null||rig.npcMaterial==null)throw new InvalidOperationException("Bentley lamp rig incomplete");
        foreach (var emitterMesh in rig.playerEmitters.Select(skin => skin.sharedMesh)
                     .Append(rig.emitters.GetComponent<MeshFilter>().sharedMesh))
        {
            if (emitterMesh.vertexCount != emitterMesh.GetSubMesh(0).vertexCount)
                throw new InvalidOperationException($"Bentley emitter vertex buffer does not match the generated triangles: {emitterMesh.name}");
        }
        VerifySourceBinding(vehicle,rig);
        var nativeType=typeof(GleyTrafficSystem.VehicleLightsComponent);
        foreach(var pair in new[]{("FrontLight","_FrontLightStrength"),("BrakeLight","_BackLightStrength"),("ReverseLight","_ReverseLightStrength"),("IsBlinkerOn","_IsBlinkerOn"),("IsLeftBlinkerOn","_IsLeftBlinkerOn"),("IsRightBlinkerOn","_IsRightBlinkerOn")})
        {
            var field=nativeType.GetField(pair.Item1,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
            if(field==null||(int)field.GetValue(null)!=Shader.PropertyToID(pair.Item2))throw new InvalidOperationException("Bentley native lamp shader contract changed: "+pair.Item1);
        }
        if(ShaderUtil.ShaderHasError(rig.emitters.sharedMaterial.shader))throw new InvalidOperationException("Bentley emitter shader compilation failed.");
        var mesh=rig.GetComponent<MeshFilter>().sharedMesh;var pts=mesh.vertices;var roles=mesh.uv;
        var counts=new int[Roles.Length];var sides=new int[Roles.Length,2];
        for(var i=0;i<pts.Length;i++){var role=Mathf.RoundToInt(roles[i].x);counts[role]++;sides[role,pts[i].x<0?0:1]++;}
        for(var r=0;r<Roles.Length;r++)if(counts[r]==0||sides[r,0]==0||sides[r,1]==0)throw new InvalidOperationException("Bentley lamp group missing side: "+Roles[r]);
        if(mesh.bounds.size.z<4||mesh.bounds.size.x<1.8f||mesh.bounds.size.z>5.2f)throw new InvalidOperationException("Bentley emitter alignment invalid "+mesh.bounds);
        if(rig.transform.localPosition!=Vector3.zero||rig.transform.localScale!=Vector3.one)throw new InvalidOperationException("Bentley emitter local transform invalid");
        Debug.Log("Bentley emitter geometry verified: roles="+string.Join(",",counts)+", bounds="+mesh.bounds);
    }

    private static void VerifySourceBinding(GameObject vehicle,BentleyContinentalGTSpeedLampRig rig)
    {
        if(rig.playerEmitters.Length!=4||rig.emitters.enabled)
            throw new InvalidOperationException("Bentley player must use four source-skinned emitters, not the traffic rest-pose mesh.");
        var bone=rig.playerEmitters[0].bones[rig.playerEmitters[0].sharedMesh.boneWeights[0].boneIndex0];
        var originalPosition=bone.localPosition;var originalRotation=bone.localRotation;
        var baseline=new Dictionary<SkinnedMeshRenderer,Vector3[]>();
        var maximumPoseMovement=0f;
        try
        {
            for(var pose=0;pose<2;pose++)
            {
                if(pose==1){bone.localPosition+=new Vector3(.025f,.012f,0);bone.localRotation*=Quaternion.Euler(0,0,2);}
                foreach(var overlay in rig.playerEmitters)
                {
                    var source=overlay.transform.parent.GetComponent<SkinnedMeshRenderer>();
                    if(source==null||overlay.transform.localPosition!=Vector3.zero||overlay.transform.localRotation!=Quaternion.identity||
                       overlay.transform.localScale!=Vector3.one||!overlay.bones.SequenceEqual(source.bones)||overlay.rootBone!=source.rootBone)
                        throw new InvalidOperationException("Bentley lamp does not share the exact source transform/skin: "+overlay.name);
                    var original=new Mesh();var emitted=new Mesh();source.BakeMesh(original);overlay.BakeMesh(emitted);
                    try
                    {
                        var vertices=original.vertices;var actual=emitted.vertices;var lookup=overlay.sharedMesh.uv2;
                        if(pose==0)baseline[overlay]=actual.Select(p=>overlay.transform.TransformPoint(p)).ToArray();
                        var maximum=0f;
                        for(var i=0;i<actual.Length;i++)
                        {
                            var expected=source.transform.TransformPoint(vertices[Mathf.RoundToInt(lookup[i].x)]);
                            maximum=Mathf.Max(maximum,Vector3.Distance(expected,overlay.transform.TransformPoint(actual[i])));
                            if(pose==1)maximumPoseMovement=Mathf.Max(maximumPoseMovement,Vector3.Distance(baseline[overlay][i],expected));
                        }
                        if(maximum>.0015f)throw new InvalidOperationException($"Bentley lamp skin alignment error {overlay.name}, pose={pose}, delta={maximum}");
                        Debug.Log($"Bentley lamp source-skin alignment {overlay.name} pose={pose} maxDelta={maximum:0.000000}m.");
                    }
                    finally{UnityEngine.Object.DestroyImmediate(original);UnityEngine.Object.DestroyImmediate(emitted);}
                }
            }
            if(maximumPoseMovement<.01f)throw new InvalidOperationException("Bentley lamp pose regression did not actually move a bound source bone.");
            Debug.Log($"Bentley lamp pose regression: source moved {maximumPoseMovement:0.000000}m while skin-aligned emitter offset stayed below 1.5mm; a root-baked static emitter cannot follow this pose.");
        }
        finally{bone.localPosition=originalPosition;bone.localRotation=originalRotation;}
        foreach(var beam in vehicle.GetComponentsInChildren<Light>(true).Where(l=>l.name=="Spotlights"||l.name=="BentleyRoadBeamRight"))
        {
            var dy=-beam.transform.forward.y;
            var illuminance=beam.intensity*.56f/Mathf.Pow(5*5+.56f*.56f,1.5f);
            if(dy<.15f||illuminance<250)throw new InvalidOperationException("Bentley road beam lacks near-road aim/intensity: "+beam.name);
            Debug.Log($"Bentley road beam {beam.name} intensity={beam.intensity:0}cd groundLuxAt5m={illuminance:0.0} downward={dy:0.000}.");
        }
    }
}
