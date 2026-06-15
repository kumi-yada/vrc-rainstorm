Shader "Custom/MobileNightShader"
{
    Properties
    {
        _OverlayColor ("OverlayColor", Color) = (0,0,0,0)
        _Value ("_Value", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay+100" "IsEmissive"="true" }
        LOD 100

        Cull Off
        ZWrite Off
        ZTest Always
        Blend DstColor Zero

        CGPROGRAM
        #pragma target 3.0
        #pragma surface surf Unlit keepalpha addshadow fullforwardshadows nofog

        struct Input
        {
            half filler;
        };

        uniform float4 _OverlayColor;
        uniform float _Value;

        inline half4 LightingUnlit(SurfaceOutput s, half3 lightDir, half atten)
        {
            return half4(0, 0, 0, s.Alpha);
        }

        void surf(Input i, inout SurfaceOutput o)
        {
            float t = saturate(_Value);
            float4 overlay = float4(saturate(_OverlayColor.rgb), 1);
            float4 blendColor = lerp(float4(1, 1, 1, 1), overlay, t);
            o.Emission = blendColor.rgb;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
