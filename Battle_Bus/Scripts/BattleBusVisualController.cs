#nullable enable
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class BattleBusVisualController : MonoBehaviour
{
    [SerializeField] private Transform? propeller = null;
    [SerializeField] private MeshFilter[] balloonMeshes=Array.Empty<MeshFilter>();
    [SerializeField] private MeshRenderer[] lamps=Array.Empty<MeshRenderer>();
    private VehicleController? vehicle;
    private Rigidbody? body;
    private BattleBusFlightController? flight;
    private object? brakes;
    private object? blinkers;
    private PropertyInfo? lightsOn;
    private PropertyInfo? braking;
    private MethodInfo? brakingMethod;
    private FieldInfo? left;
    private FieldInfo? right;
    private Mesh[] balloonCopies=Array.Empty<Mesh>();
    private Vector3[][] balloonRest=Array.Empty<Vector3[]>();
    private Vector3[][] balloonWork=Array.Empty<Vector3[]>();
    private Material[] lampMaterials=Array.Empty<Material>();
    private Material? fireMaterial;
    [SerializeField] private Vector3 propellerAxis = new Vector3(0f,-.2588299f,-.9659229f);
    private float inflation=-1f;
    private float balloonBottom;
    private float blinkStart;
    private bool wasBlinking;
    private int lastState=-1;
    private const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;

    public static void ConfigureFireMaterial(Material material, Texture texture)
    {
        material.shaderKeywords=Array.Empty<string>();
        material.SetTexture("_UnlitColorMap",texture);
        material.SetColor("_UnlitColor",new Color(2f,2f,2f,1f));
        material.SetColor("_EmissiveColor",Color.black);
        material.SetFloat("_SurfaceType",1);material.SetFloat("_BlendMode",0);
        material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);material.SetFloat("_DstBlend",(float)BlendMode.One);
        material.SetFloat("_AlphaSrcBlend",(float)BlendMode.One);material.SetFloat("_AlphaDstBlend",(float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite",0);material.SetFloat("_CullMode",0);
        material.SetFloat("_ZTestTransparent",(float)CompareFunction.LessEqual);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType","Transparent");material.renderQueue=3000;
        material.SetShaderPassEnabled("ForwardOnly",true);
        material.SetShaderPassEnabled("DepthForwardOnly",false);
        material.SetShaderPassEnabled("ShadowCaster",false);
        material.SetShaderPassEnabled("MotionVectors",false);
    }
    private void Awake()
    {
        vehicle=GetComponent<VehicleController>();body=GetComponent<Rigidbody>();flight=GetComponent<BattleBusFlightController>();
        lightsOn=typeof(VehicleController).GetProperty("ShouldLightsBeOn",Members);
        foreach(var component in GetComponentsInChildren<MonoBehaviour>(true))
        {
            if(component==null)continue;
            brakes??=component.GetType().GetField("brakes",Members)?.GetValue(component);
            if(component.GetType().FullName=="Vehicles.Components.VehicleBlinker")blinkers=component;
        }
        braking=brakes?.GetType().GetProperty("IsBraking",Members);brakingMethod=brakes?.GetType().GetMethod("IsBraking",Members,null,Type.EmptyTypes,null);
        left=blinkers?.GetType().GetField("_isLeftBlinkerOn",Members);right=blinkers?.GetType().GetField("_isRightBlinkerOn",Members);
        lampMaterials=new Material[lamps.Length];
        var shader=Shader.Find("HDRP/Unlit");
        for(var i=0;i<lamps.Length;i++)
        {
            lampMaterials[i]=new Material(lamps[i].sharedMaterial);
            if(shader!=null)lampMaterials[i].shader=shader;
            lamps[i].sharedMaterial=lampMaterials[i];lamps[i].enabled=false;
        }
        var fire=GetComponentsInChildren<ParticleSystemRenderer>(true).FirstOrDefault(r=>r.name=="BattleBusBalloonFlame");
        if(fire!=null && shader!=null)
        {
            var texture=fire.sharedMaterial.GetTexture("_UnlitColorMap");
            fireMaterial=new Material(shader){name="BattleBusRuntimeFlame"};
            ConfigureFireMaterial(fireMaterial,texture);fire.sharedMaterial=fireMaterial;
        }
        else Debug.LogWarning("Battle Bus: burner renderer or game HDRP/Unlit shader missing.");
        balloonCopies=new Mesh[balloonMeshes.Length];balloonRest=new Vector3[balloonMeshes.Length][];balloonWork=new Vector3[balloonMeshes.Length][];balloonBottom=3.55f;
        for(var i=0;i<balloonMeshes.Length;i++)
        {
            var f=balloonMeshes[i];var mesh=Instantiate(f.sharedMesh);mesh.MarkDynamic();f.sharedMesh=mesh;balloonCopies[i]=mesh;
            balloonRest[i]=mesh.vertices;balloonWork[i]=new Vector3[mesh.vertexCount];

        }
        UpdateBalloon(0f);
        if(lightsOn==null||left==null||right==null)Debug.LogWarning("Battle Bus: one or more vehicle light state bindings are unavailable.");
    }
    private void LateUpdate()
    {
        var speed=body!=null ? body.velocity.magnitude : 0f;
        if(propeller!=null && speed>.05f)propeller.Rotate(propellerAxis,Mathf.Min(1600f,speed*180f)*Time.deltaTime,Space.Self);
        var target=flight!=null && flight.IsBurnerOn ? 1f : 0f;
        var next=Mathf.MoveTowards(inflation,target,Time.deltaTime/(target>inflation?3f:5f));
        if(Mathf.Abs(next-inflation)>.00001f)UpdateBalloon(next);
        var active=vehicle!=null && vehicle.controlledByPlayer;
        var on=active && lightsOn?.GetValue(vehicle) is bool b && b;
        var brake=active && ((braking?.GetValue(brakes) is bool p && p)||(brakingMethod?.Invoke(brakes,null) is bool m&&m));
        var l=active && left?.GetValue(blinkers) is bool lb && lb;var r=active && right?.GetValue(blinkers) is bool rb && rb;
        if((l||r)&&!wasBlinking)blinkStart=Time.unscaledTime;wasBlinking=l||r;
        var flash=Mathf.Repeat(Time.unscaledTime-blinkStart,.84f)<.42f;
        var state=(on?1:0)|(brake?2:0)|(l&&flash?4:0)|(r&&flash?8:0);
        if(state==lastState)return;lastState=state;
        for(var i=0;i<lamps.Length;i++)
        {
            var name=lamps[i].name;
            if(name.EndsWith("Headlamps"))lamps[i].enabled=on;
            else if(name.EndsWith("RearDrivingLight_BrakeLight"))
            {lamps[i].enabled=on||brake;lampMaterials[i].SetColor("_UnlitColor",new Color(.9f,.002f,.001f)*(brake?4f:1f));}
            else lamps[i].enabled=(name.EndsWith("FL")||name.EndsWith("RL")||name.EndsWith("Left")?l:r)&&flash;
        }
        if(BattleBusDiagnostics.DebugEnabled && BattleBusDiagnostics.VisualDebugEnabled)
            Debug.Log($"Battle Bus visuals vehicle={GetInstanceID()} lightState={state} burner={target>0f}.");
    }
    private void UpdateBalloon(float amount)
    {
        inflation=amount;
        for(var i=0;i<balloonMeshes.Length;i++)
        {
            var t=balloonMeshes[i].transform;var rest=balloonRest[i];var work=balloonWork[i];
            for(var v=0;v<rest.Length;v++)
            {
                var p=transform.InverseTransformPoint(t.TransformPoint(rest[v]));
                var height=Mathf.Max(0f,p.y-balloonBottom);if(height<=0f){work[v]=rest[v];continue;}var fold=(1f-amount)*.09f*Mathf.Sin(p.x*16f+p.z*11f);
                p.y=balloonBottom+height*Mathf.Lerp(.10f,1f,amount)+fold*Mathf.Clamp01(height);
                work[v]=t.InverseTransformPoint(transform.TransformPoint(p));
            }
            balloonCopies[i].vertices=work;balloonCopies[i].RecalculateBounds();balloonCopies[i].RecalculateNormals();
        }
    }
    private void OnDisable(){foreach(var lamp in lamps)if(lamp!=null)lamp.enabled=false;lastState=-1;}
    private void OnDestroy(){foreach(var mesh in balloonCopies)if(mesh!=null)Destroy(mesh);foreach(var mat in lampMaterials)if(mat!=null)Destroy(mat);if(fireMaterial!=null)Destroy(fireMaterial);}
}
