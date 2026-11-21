Shader "Bentley/ModelLampEmitters"
{
 Properties {
  _DrlActive("Engine/DRL",Float)=0
  _FrontLightStrength("Main lights",Float)=0
  _BackLightStrength("Tail/stop",Float)=0
  _ReverseLightStrength("Reverse",Float)=0
  _HighBeam("High beam",Float)=0
  _IsLeftBlinkerOn("Left request",Float)=0
  _IsRightBlinkerOn("Right request",Float)=0
  _IsBlinkerOn("Native blink phase",Float)=0
  _BlinkerSpeed("Native blink speed",Float)=0.42
  _BlinkerOffset("Native blink offset",Float)=0
 }
 SubShader {
  Tags { "RenderPipeline"="HDRenderPipeline" "RenderType"="Transparent" "Queue"="Transparent+5" }
  Pass {
   Name "ForwardOnly" Tags { "LightMode"="ForwardOnly" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off ZTest LEqual Cull Off
   HLSLPROGRAM
   #pragma target 4.5
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
   #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
   #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
   CBUFFER_START(UnityPerMaterial)
    float _DrlActive, _FrontLightStrength, _BackLightStrength, _ReverseLightStrength;
    float _HighBeam, _IsLeftBlinkerOn, _IsRightBlinkerOn, _IsBlinkerOn, _BlinkerSpeed, _BlinkerOffset;
   CBUFFER_END
   struct Attributes { float3 positionOS:POSITION; float2 lamp:TEXCOORD0; };
   struct Varyings { float4 positionCS:SV_POSITION; float2 lamp:TEXCOORD0; };
   Varyings Vert(Attributes a) { Varyings o; o.positionCS=TransformWorldToHClip(TransformObjectToWorld(a.positionOS));o.lamp=a.lamp;return o; }
   float4 Frag(Varyings i):SV_Target {
    int role=(int)round(i.lamp.x); bool left=i.lamp.y<0;
    float request=left?_IsLeftBlinkerOn:_IsRightBlinkerOn;
    float flash=(request>0.5&&_IsBlinkerOn>0.5)?1:0;
    float mainOn=_FrontLightStrength>0?1:0;
    float3 white=float3(.82,.93,1), red=float3(1,.001,.0005), amber=float3(1,.30,.002);
    float3 color=0;
    if(role==0) color=request>.5?amber*flash*2.4:white*_DrlActive*2;
    if(role==1) color=white*mainOn*(3+_HighBeam*1.5);
    if(role==2) color=white*_DrlActive*1.2;
    if(role==3) color=red*_BackLightStrength;
    if(role==4) color=red*max(0,_BackLightStrength-1)*1.5;
    if(role==5) color=white*_ReverseLightStrength*2.4;
    if(role==6) color=amber*flash*2.4;
    if(role==7) color=white*mainOn*.8;
    if(role==8) color=amber*mainOn*.6;
    if(role==9) color=red*mainOn*.6;
    float visible=dot(color,color)>0.0001?1:0;
    // Colors are calibrated in pre-exposed display space, like HDRP emission with exposure weight 0.
    // Applying camera exposure again makes them effectively black in daylight.
    return float4(color,visible);
   }
   ENDHLSL
  }
 }
}
