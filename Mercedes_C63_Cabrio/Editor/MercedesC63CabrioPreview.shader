// Editor-only reference rendering with the same face-culling and mask contract.
Shader "Hidden/MercedesC63Cabrio/Preview"
{
    Properties
    {
        _Color("Color", Color) = (1,1,1,1)
        _MainTex("Albedo", 2D) = "white" {}
        _Glossiness("Smoothness", Range(0,1)) = .35
        _Metallic("Metallic", Range(0,1)) = 0
        _Cutoff("Cutoff", Range(0,1)) = .15
        _UseAlphaClip("Clip", Float) = 0
        _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull [_Cull]
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Glossiness, _Metallic, _Cutoff, _UseAlphaClip;
        struct Input { float2 uv_MainTex; float facing : VFACE; };
        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 color = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            if (_UseAlphaClip > .5) clip(color.a - _Cutoff);
            o.Albedo = color.rgb;
            o.Normal = float3(0,0,IN.facing >= 0 ? 1 : -1);
            o.Metallic = _Metallic; o.Smoothness = _Glossiness; o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
