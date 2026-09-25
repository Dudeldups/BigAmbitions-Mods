Shader "Bigfoot/TransparentWindshield"
{
    Properties
    {
        _Color ("Tint", Color) = (0.78, 0.84, 0.88, 0.2)
        _MainTex ("Windshield Decal Atlas", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                fixed4 atlas = tex2D(_MainTex, input.uv);
                fixed brightness = max(atlas.r, max(atlas.g, atlas.b));
                fixed decalMask = smoothstep(0.45, 0.8, brightness);
                fixed3 color = lerp(_Color.rgb, atlas.rgb, decalMask);
                fixed alpha = lerp(_Color.a, 1.0, decalMask);
                return fixed4(color, alpha);
            }
            ENDCG
        }
    }
}
