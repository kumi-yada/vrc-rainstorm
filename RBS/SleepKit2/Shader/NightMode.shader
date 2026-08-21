Shader "RBS/NightModev3"
{
    Properties
    {
        _Brightness ("Brightness", Range(0, 1)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+2000" }
        ZTest Always
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        GrabPass {}

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
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 grabPos : TEXCOORD1;
            };

            sampler2D _GrabTexture;
            float _Brightness;
            float _VRChatCameraMode;
            float _VRChatMirrorMode;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.grabPos = ComputeGrabScreenPos(o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (_VRChatCameraMode == 0 && _VRChatMirrorMode == 0 && _Brightness < 0.95) {
                    fixed4 grabbedColor = tex2Dproj(_GrabTexture, i.grabPos);
                    
                    float brightnessFactor = pow(_Brightness, 2.5);
                    fixed4 col = fixed4(
                        clamp(grabbedColor.x * brightnessFactor, 0, brightnessFactor),
                        clamp(grabbedColor.y * brightnessFactor, 0, brightnessFactor),
                        clamp(grabbedColor.z * brightnessFactor, 0, brightnessFactor), 1);
                    return col;
                }

                fixed4 col = fixed4(0, 0, 0, 0);
                return col;
            }
            ENDCG
        }
    }
}
