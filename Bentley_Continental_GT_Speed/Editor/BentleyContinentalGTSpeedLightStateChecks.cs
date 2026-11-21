#nullable enable
using System;
using System.Linq;
using System.Reflection;
using GleyTrafficSystem;
using UnityEngine;

public static class BentleyContinentalGTSpeedLightStateChecks
{
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void VerifyPlayer(GameObject prefab)
    {
        var root=UnityEngine.Object.Instantiate(prefab);root.SetActive(false);
        try
        {
            var type=typeof(BentleyContinentalGTSpeedMod).Assembly.GetType("BentleyContinentalGTSpeedLightingController",true)!;
            var component=root.AddComponent(type);
            var physics=root.GetComponent<NWH.VehiclePhysics2.VehicleController>();
            var rig=root.GetComponentInChildren<BentleyContinentalGTSpeedLampRig>(true);
            Set(type,component,"vehicle",root.GetComponent<VehicleController>());Set(type,component,"physics",physics);Set(type,component,"rig",rig);Set(type,component,"driving",true);
            var beams=root.GetComponentsInChildren<Light>(true).Where(l=>l.name=="Spotlights"||l.name=="BentleyRoadBeamRight").ToArray();
            Set(type,component,"beams",beams);
            var data=(System.Collections.Generic.List<Behaviour>)type.GetField("beamData",Private)!.GetValue(component)!;
            foreach(var beam in beams)foreach(var b in beam.GetComponents<MonoBehaviour>())if(b.GetType().FullName=="UnityEngine.Rendering.HighDefinition.HDAdditionalLightData")data.Add(b);
            var lights=physics.effectsManager.lightsManager;
            var before=lights.lowBeamLights.lightSources.Count;
            type.GetMethod("Subscribe",Private)!.Invoke(component,null);
            lights.lowBeamLights.TurnOff();lights.brakeLights.TurnOff();lights.reverseLights.TurnOff();
            lights.lowBeamLights.TurnOn();
            Assert(rig.emitters,"_FrontLightStrength",1);Assert(rig.emitters,"_BackLightStrength",1);
            foreach(var emitter in rig.playerEmitters){Assert(emitter,"_FrontLightStrength",1);Assert(emitter,"_BackLightStrength",1);}
            if(beams.Length!=2||beams.Any(l=>!l.enabled)||data.Count!=2||data.Any(d=>!d.enabled))throw new InvalidOperationException("Bentley low beam did not enable both HDRP road lights");
            var highField=lights.GetType().GetField("highBeamLights",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            var high=(NWH.VehiclePhysics2.Effects.VehicleLight?)highField?.GetValue(lights);
            if(high==null)throw new InvalidOperationException("Bentley high-beam native light owner unavailable");
            high.TurnOff();high.TurnOn();Assert(rig.emitters,"_HighBeam",1);
            if(beams.Any(l=>l.range!=80))throw new InvalidOperationException("Bentley high-beam range not applied");
            high.TurnOff();Assert(rig.emitters,"_HighBeam",0);
            lights.brakeLights.TurnOn();Assert(rig.emitters,"_BackLightStrength",3);
            lights.reverseLights.TurnOn();Assert(rig.emitters,"_ReverseLightStrength",1);
            foreach(var emitter in rig.playerEmitters){Assert(emitter,"_ReverseLightStrength",1);Assert(emitter,"_BackLightStrength",3);}
            Set(type,component,"left",true);Set(type,component,"right",false);Set(type,component,"phase",true);Apply(type,component);
            Assert(rig.emitters,"_IsLeftBlinkerOn",1);Assert(rig.emitters,"_IsRightBlinkerOn",0);Assert(rig.emitters,"_IsBlinkerOn",1);
            Assert(rig.emitters,"_BackLightStrength",3);Assert(rig.emitters,"_ReverseLightStrength",1);
            Set(type,component,"right",true);Set(type,component,"phase",false);Apply(type,component);
            Assert(rig.emitters,"_IsLeftBlinkerOn",1);Assert(rig.emitters,"_IsRightBlinkerOn",1);Assert(rig.emitters,"_IsBlinkerOn",0);
            type.GetMethod("Shutdown",Private)!.Invoke(component,null);
            if(lights.lowBeamLights.lightSources.Count!=before)throw new InvalidOperationException("Bentley lamp callbacks leaked on shutdown");
            if(beams.Any(l=>l.enabled)||data.Any(d=>d.enabled))throw new InvalidOperationException("Bentley road lights remain on after shutdown");
            Assert(rig.emitters,"_DrlActive",0);Assert(rig.emitters,"_BackLightStrength",0);
            foreach(var emitter in rig.playerEmitters){Assert(emitter,"_DrlActive",0);Assert(emitter,"_BackLightStrength",0);}
            Debug.Log("Bentley player light state checks passed: native low/brake/reverse callbacks, independent indicators/hazards, cleanup.");
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
    public static void VerifyTraffic(GameObject root)
    {
        var lights=root.GetComponent<VehicleLightsComponent>();
        var rig=root.GetComponentInChildren<BentleyContinentalGTSpeedLampRig>(true);
        if(lights==null||rig==null||lights.meshRenderer!=rig.emitters)throw new InvalidOperationException("Bentley NPC lamp renderer not natively bound");
        if(!rig.emitters.enabled||rig.playerEmitters.Length!=0||root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any(r=>r.name.StartsWith("BentleyLampEmitters_",StringComparison.Ordinal)&&r.enabled))
            throw new InvalidOperationException("Bentley NPC has duplicate/player emitters enabled");
        lights.Initialize();lights.SetMainLights(true);lights.SetBrakeLights(false);lights.SetReverseLights(false);lights.UpdateLights(0,true);
        Assert(rig.emitters,"_FrontLightStrength",1);Assert(rig.emitters,"_BackLightStrength",1);
        lights.SetBrakeLights(true);lights.SetReverseLights(true);
        var name=Enum.GetNames(typeof(BlinkType)).First(n=>n.IndexOf("Left",StringComparison.OrdinalIgnoreCase)>=0);
        lights.SetBlinker((BlinkType)Enum.Parse(typeof(BlinkType),name));lights.UpdateLights(0,true);
        Assert(rig.emitters,"_BackLightStrength",3);Assert(rig.emitters,"_ReverseLightStrength",1);
        Assert(rig.emitters,"_IsLeftBlinkerOn",1);Assert(rig.emitters,"_IsRightBlinkerOn",0);Assert(rig.emitters,"_IsBlinkerOn",1);
        lights.UpdateLights(lights.blinkerSpeed*1.1f,true);Assert(rig.emitters,"_IsBlinkerOn",0);
        Debug.Log("Bentley NPC native light state checks passed: main, brake, reverse and indicator phase write the authored emitter renderer.");
    }
    private static void Set(Type type,Component target,string name,object value)=>type.GetField(name,Private)!.SetValue(target,value);
    private static void Apply(Type type,Component target)=>type.GetMethod("Apply",Private)!.Invoke(target,null);
    private static void Assert(Renderer renderer,string name,float expected)
    {
        var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);
        if(Mathf.Abs(block.GetFloat(name)-expected)>.001f)throw new InvalidOperationException($"Bentley light state {name}={block.GetFloat(name)} expected {expected}");
    }
}
