Shader "QuickBrown/CustomMirror"
{
    Properties
    { 
        _MainTex("Mirror Surface (RGB)", 2D) = "white" {}
        
        [HideInInspector] _ReflectionTex0("", 2D) = "white" {}
        [HideInInspector] _ReflectionTex1("", 2D) = "white" {}
        
        [ToggleUI(LQMode)] _LQMode("LQ Mode", Float) = 0
        
        _Alpha("Alpha", Range(0, 1)) = 1  // 0 = 完全透明, 1 = 完全不透明
        _AlphaSecond("Alpha Second", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags{ "RenderType"="Transparent" "Queue"="Transparent+1" "IgnoreProjector"="True"}
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        LOD 100

        Stencil
        {
            Ref 0
            Comp Always
            Pass Keep
            Fail Keep
            ZFail Keep
            ReadMask 255
            WriteMask 255
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_transparent
            #pragma fragment frag_transparent
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityInstancing.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _LQMode;
            float _Alpha;
            float _AlphaSecond;

            sampler2D _ReflectionTex0;
            sampler2D _ReflectionTex1;

            struct appdata_transparent 
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f_transparent
            {
                float2 uv : TEXCOORD0;
                float4 refl : TEXCOORD1;
                float4 pos : SV_POSITION;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f_transparent vert_transparent(appdata_transparent v)
            {
                v2f_transparent o;

                // VRChatインスタンシング設定
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f_transparent, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // 頂点をクリップ空間に変換
                o.pos = UnityObjectToClipPos(v.vertex);
                // テクスチャのスケールとオフセットを適用
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // 鏡反射用のスクリーン座標を計算
                o.refl = ComputeNonStereoScreenPos(o.pos);

                return o;
            }

            half4 frag_transparent(v2f_transparent i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                
                const float kHighlightClipStart = 0.95;
                const float kHighlightWhite = 0.98;

                half4 tex = tex2D(_MainTex, i.uv);
                half4 refl = unity_StereoEyeIndex == 0 ? tex2Dproj(_ReflectionTex0, UNITY_PROJ_COORD(i.refl)) : tex2Dproj(_ReflectionTex1, UNITY_PROJ_COORD(i.refl));

                float4 reflPos = i.refl;
                float2 screen01 = reflPos.xy / reflPos.w;

                float2 screenA = saturate(screen01 + float2(0.25, 0.25));
                float2 screenB = saturate(screen01 + float2(-0.25, -0.25));

                float4 reflPosA = float4(screenA * reflPos.w, reflPos.zw);
                float4 reflPosB = float4(screenB * reflPos.w, reflPos.zw);

                half4 reflA = unity_StereoEyeIndex == 0 ? tex2Dproj(_ReflectionTex0, UNITY_PROJ_COORD(reflPosA)) : tex2Dproj(_ReflectionTex1, UNITY_PROJ_COORD(reflPosA));
                half4 reflB = unity_StereoEyeIndex == 0 ? tex2Dproj(_ReflectionTex0, UNITY_PROJ_COORD(reflPosB)) : tex2Dproj(_ReflectionTex1, UNITY_PROJ_COORD(reflPosB));

                bool isUniformWhite =
                    (refl.r > 0.999 && refl.g > 0.999 && refl.b > 0.999 && refl.a > 0.999) &&
                    (reflA.r > 0.999 && reflA.g > 0.999 && reflA.b > 0.999 && reflA.a > 0.999) &&
                    (reflB.r > 0.999 && reflB.g > 0.999 && reflB.b > 0.999 && reflB.a > 0.999);

                bool isReflectionReady = !isUniformWhite;
                
                if (!isReflectionReady) {
                    refl = tex * half4(0.7, 0.7, 0.7, 0.5);
                } else {
                    if (_LQMode) {
                        refl.a = refl.a > 0 ? 1 : 0;
                    } else {
                        refl.a = 1;
                    }
                    refl *= tex;
                }
                
                refl.a *= (_Alpha * _AlphaSecond);
                
                return refl;
            }

            ENDCG
        }
    }
}
