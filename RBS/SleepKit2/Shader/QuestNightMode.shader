Shader "RBS/Mobile/NightMode"
{
    Properties
    {
        _Brightness ("Brightness", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent+2000"
        }

        ZTest Always
        ZWrite Off
        Cull Off
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
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            float _Brightness;
            float _VRChatCameraMode;
            float _VRChatMirrorMode;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                if (_VRChatCameraMode != 0 || _VRChatMirrorMode != 0)
                {
                    return fixed4(0, 0, 0, 0);
                }

                float alpha = 1.0 - pow(_Brightness, 2.5);

                return fixed4(0, 0, 0, alpha);
            }
            ENDCG
        }
    }
}
