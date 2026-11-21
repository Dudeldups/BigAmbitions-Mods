#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BAModAPI;
using BigAmbitions.InputSystem;
using NWH.VehiclePhysics2.Effects;
using UnityEngine;
using UnityEngine.InputSystem;

internal sealed class BentleyContinentalGTSpeedLightingController : MonoBehaviour
{
    private VehicleController? vehicle;
    private BentleyContinentalGTSpeedLampRig? rig;
    private ModContext? context;
    private NWH.VehiclePhysics2.VehicleController? physics;
    private object? blinker;
    private Light[] beams=Array.Empty<Light>();
    private readonly List<Behaviour> beamData=new();
    private readonly List<(VehicleLight owner,LightSource source)> bindings=new();
    private readonly List<InputAction> actions=new();
    private MaterialPropertyBlock? block;
    private bool driving,main,high,stop,reverse,left,right,phase,ready;
    private Coroutine? deferred, lifecycle;
    private int lastState = -1;
    private static readonly BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;

    internal static BentleyContinentalGTSpeedLightingController? Configure(VehicleController car,ModContext? context)
    {
        var rig=car.GetComponentInChildren<BentleyContinentalGTSpeedLampRig>(true);
        if(rig==null){context?.Logger.Warn("Bentley lighting: authored emitter rig is missing; rebuild the bundle.");return null;}
        var controller=car.GetComponent<BentleyContinentalGTSpeedLightingController>()??car.gameObject.AddComponent<BentleyContinentalGTSpeedLightingController>();
        controller.vehicle=car;controller.rig=rig;controller.context=context;
        if(!controller.ready){
            controller.physics=car.GetComponent<NWH.VehiclePhysics2.VehicleController>();
            foreach(var c in car.GetComponentsInChildren<MonoBehaviour>(true))if(c!=null&&c.GetType().FullName=="Vehicles.Components.VehicleBlinker"){controller.blinker=c;break;}
            var lamps=new List<Light>();
            foreach(var lamp in car.GetComponentsInChildren<Light>(true))if(lamp.name=="Spotlights"||lamp.name=="BentleyRoadBeamRight")
            {
                lamps.Add(lamp);foreach(var data in lamp.GetComponents<MonoBehaviour>())if(data is Behaviour behaviour&&data.GetType().FullName=="UnityEngine.Rendering.HighDefinition.HDAdditionalLightData")controller.beamData.Add(behaviour);
            }
            controller.beams=lamps.ToArray();
            rig.emitters.enabled=rig.playerEmitters.Length==0;
            if(rig.playerEmitters.Length!=4)context?.Logger.Warn("Bentley lighting: source-skinned emitters missing; install the matching rebuilt bundles.");
            foreach(var emitter in rig.playerEmitters)if(emitter!=null)emitter.enabled=true;
            controller.ready=true;controller.Subscribe();
            var material = rig.emitters.sharedMaterial;
            if (material == null || material.shader == null || !material.shader.isSupported)
                context?.Logger.Warn($"Bentley lighting vehicle={car.GetInstanceID()}: emitter shader is missing or unsupported on {SystemInfo.graphicsDeviceType}.");
            if (BentleyContinentalGTSpeedDiagnostics.DebugEnabled && BentleyContinentalGTSpeedDiagnostics.LightingDebugEnabled)
                context?.Logger.Info($"Bentley lamp render binding vehicle={car.GetInstanceID()} shader='{material?.shader?.name}' supported={material?.shader?.isSupported} emitterEnabled={rig.emitters.enabled} active={rig.emitters.gameObject.activeInHierarchy} bounds={rig.emitters.bounds} roadBeams={controller.beams.Length} sourceSkinnedEmitters={rig.playerEmitters.Length}.");
        }
        controller.ReadState();return controller;
    }
    private void Bind(VehicleLight? owner,Action<bool> apply)
    {
        if(owner==null)return;
        var source=new LightSource();
        source.onLightTurnedOn.AddListener(()=>apply(true));source.onLightTurnedOff.AddListener(()=>apply(false));
        owner.lightSources.Add(source);bindings.Add((owner,source));
    }
    private void Subscribe()
    {
        if(vehicle==null||bindings.Count!=0)return;
        var lights=physics?.effectsManager.lightsManager;
        if(lights==null){context?.Logger.Warn("Bentley lighting: native light-state callbacks missing.");return;}
        Bind(lights.lowBeamLights,v=>{main=v;Apply();});
        Bind(Read(lights,"highBeamLights") as VehicleLight,v=>{high=v;Apply();});
        Bind(lights.brakeLights,v=>{stop=v;Apply();});Bind(lights.reverseLights,v=>{reverse=v;Apply();});
        physics!.onVehicleInitialized.AddListener(NativeReinitialized);physics.onEnable.AddListener(NativeReinitialized);
        GlobalEvents.onEnterVehicle+=Entered;GlobalEvents.onExitVehicle+=Exited;GlobalEvents.onVehicleVariablesChanged+=ReadState;
        var map=typeof(InputActionHelper).GetField("PlayerInputActionMap",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(null) as Dictionary<PlayerAction,InputAction>;
        if(map!=null)foreach(var pair in map)
        {
            var name=pair.Key.ToString();
            if(name.IndexOf("Blinker",StringComparison.OrdinalIgnoreCase)<0&&name.IndexOf("Hazard",StringComparison.OrdinalIgnoreCase)<0&&name.IndexOf("WarningLight",StringComparison.OrdinalIgnoreCase)<0)continue;
            if(!actions.Contains(pair.Value)){pair.Value.performed+=IndicatorInput;actions.Add(pair.Value);}
        }
    }
    private void NativeReinitialized()
    {
        if(!isActiveAndEnabled)return;
        if(lifecycle!=null)StopCoroutine(lifecycle);
        lifecycle=StartCoroutine(RebindAfterNativeLifecycle());
    }
    private IEnumerator RebindAfterNativeLifecycle()
    {
        yield return null;lifecycle=null;
        ShutdownBindings();Subscribe();ReadState();
    }    private void Entered(VehicleController car){if(car==vehicle)ReadState();}
    private void Exited(VehicleController car){if(car!=vehicle)return;driving=main=high=stop=reverse=left=right=phase=false;Apply();}
    private void IndicatorInput(InputAction.CallbackContext _){if(deferred!=null)StopCoroutine(deferred);deferred=StartCoroutine(AfterInput());}
    private IEnumerator AfterInput(){yield return null;deferred=null;ReadState();}
    private void ReadState()
    {
        if(vehicle==null)return;
        driving=vehicle.controlledByPlayer;
        main=Read(vehicle,"ShouldLightsBeOn") is bool on&&on;
        var native=physics;
        high=Read(native?.effectsManager.lightsManager,"highBeamLights") is VehicleLight highOwner&&highOwner.On;
        stop=native!=null&&native.brakes.IsBraking;reverse=native!=null&&native.powertrain.transmission.Gear<0;
        left=Read(blinker,"_isLeftBlinkerOn") is bool l&&l;right=Read(blinker,"_isRightBlinkerOn") is bool r&&r;
        Apply();
    }
    private void Update()
    {
        // Only animates cached indicator requests; no state/input discovery.
        if(!driving||(!left&&!right))return;
        var next=Mathf.Repeat(Time.unscaledTime,.84f)<.42f;
        if(next!=phase){phase=next;Apply();}
    }
    private void Apply()
    {
        if(rig?.emitters==null)return;
        WriteEmitterState(rig.emitters);
        foreach(var emitter in rig.playerEmitters)if(emitter!=null)WriteEmitterState(emitter);
        ApplyRoadLights();
    }
    private void WriteEmitterState(Renderer emitter)
    {
        block ??= new MaterialPropertyBlock();
        emitter.GetPropertyBlock(block);
        block.SetFloat("_DrlActive",driving?1:0);
        block.SetFloat("_FrontLightStrength",driving&&(main||high)?1:0);
        block.SetFloat("_BackLightStrength",driving?(stop?3:main?1:0):0);
        block.SetFloat("_HighBeam",driving&&high?1:0);
        block.SetFloat("_ReverseLightStrength",driving&&reverse?1:0);
        block.SetFloat("_IsLeftBlinkerOn",driving&&left?1:0);block.SetFloat("_IsRightBlinkerOn",driving&&right?1:0);
        block.SetFloat("_IsBlinkerOn",driving&&phase?1:0);
        emitter.SetPropertyBlock(block);
    }
    private void ApplyRoadLights()
    {
        if(rig?.emitters==null||block==null)return;
        var beamOn=driving&&(main||high);
        foreach(var beam in beams)if(beam!=null){beam.enabled=beamOn;beam.range=high?80:55;}
        foreach(var data in beamData)if(data!=null)data.enabled=beamOn;
        var state=(driving?1:0)|(main?2:0)|(high?4:0)|(stop?8:0)|(reverse?16:0)|(left?32:0)|(right?64:0);
        if(state!=lastState&&BentleyContinentalGTSpeedDiagnostics.DebugEnabled&&BentleyContinentalGTSpeedDiagnostics.LightingDebugEnabled)
            context?.Logger.Info($"Bentley lighting vehicle={vehicle?.GetInstanceID()} driving={driving} main={main} high={high} stop={stop} reverse={reverse} left={left} right={right} nativeBindings={bindings.Count} emitterEnabled={rig.emitters.enabled} active={rig.emitters.gameObject.activeInHierarchy} drl={block.GetFloat("_DrlActive")} front={block.GetFloat("_FrontLightStrength")} rear={block.GetFloat("_BackLightStrength")} reverseOutput={block.GetFloat("_ReverseLightStrength")}.");
        lastState=state;
    }
    private void OnEnable(){if(ready){Subscribe();ReadState();}}
    private void OnDisable()=>ShutdownBindings();
    private void OnDestroy()=>ShutdownBindings();
    internal void Shutdown(){ShutdownBindings();ready=false;}
    private void ShutdownBindings()
    {
        if(physics!=null){physics.onVehicleInitialized.RemoveListener(NativeReinitialized);physics.onEnable.RemoveListener(NativeReinitialized);}
        if(lifecycle!=null){StopCoroutine(lifecycle);lifecycle=null;}
        GlobalEvents.onEnterVehicle-=Entered;GlobalEvents.onExitVehicle-=Exited;GlobalEvents.onVehicleVariablesChanged-=ReadState;
        foreach(var a in actions)a.performed-=IndicatorInput;actions.Clear();
        foreach(var b in bindings){b.owner.lightSources.Remove(b.source);b.source.onLightTurnedOn.RemoveAllListeners();b.source.onLightTurnedOff.RemoveAllListeners();}bindings.Clear();
        if(deferred!=null){StopCoroutine(deferred);deferred=null;}
        driving=main=high=stop=reverse=left=right=phase=false;Apply();
    }
    private static object? Read(object? value,string name)=>value?.GetType().GetField(name,Members)?.GetValue(value)??value?.GetType().GetProperty(name,Members)?.GetValue(value);
}
