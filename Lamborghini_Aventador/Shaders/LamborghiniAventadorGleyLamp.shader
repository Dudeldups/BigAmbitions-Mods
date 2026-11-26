Shader "LamborghiniAventador/GleyTrafficLamp"
{
    Properties
    {
        _LampMode ("Lamp Mode", Float) = 0
        _LampColor ("Lamp Color", Color) = (1, 1, 1, 1)
        _LampIntensity ("Lamp Intensity", Float) = 1
        IsBlinkerOn ("Gley Blinker Phase", Float) = 0
        IsLeftBlinkerOn ("Gley Left Blinker", Float) = 0
        IsRightBlinkerOn ("Gley Right Blinker", Float) = 0
        FrontLight ("Gley Front Light", Float) = 0
        BrakeLight ("Gley Brake Light", Float) = 0
        ReverseLight ("Gley Reverse Light", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+1"
        }

        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float _LampMode;
            float4 _LampColor;
            float _LampIntensity;
            float IsBlinkerOn;
            float IsLeftBlinkerOn;
            float IsRightBlinkerOn;
            float FrontLight;
            float BrakeLight;
            float ReverseLight;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float active = 0.0;
                if (_LampMode < 0.5)
                    active = FrontLight;
                else if (_LampMode < 1.5)
                    active = FrontLight;
                else if (_LampMode < 2.5)
                    active = FrontLight * (1.0 - saturate(BrakeLight));
                else if (_LampMode < 3.5)
                    active = BrakeLight;
                else if (_LampMode < 4.5)
                    active = ReverseLight;
                else if (_LampMode < 5.5)
                    active = IsBlinkerOn * IsLeftBlinkerOn;
                else
                    active = IsBlinkerOn * IsRightBlinkerOn;

                clip(active - 0.5);
                return float4(_LampColor.rgb * _LampIntensity, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
