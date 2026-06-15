// 透明背景用シェーダー（ミラー表示制御機能付き）
Shader "QuickBrown/LQ_Back"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _BackCulling("Back Culling", Int) = 1
        [ToggleUI(MIRROR_VISIBLE)] _MirrorVisible("Mirror Visible", Float) = 0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Opaque" 
            "Queue"="Geometry"
            "IgnoreProjector"="True"
        }
        
        ColorMask RGBA
        Cull [_BackCulling]
        
        Pass
        {
            CGPROGRAM
            #pragma vertex BackgroundVertex
            #pragma fragment BackgroundFragment
            
            #include "UnityCG.cginc"
            
            struct InputVertex {
                float4 position : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct OutputVertex {
                float4 position : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            fixed4 _BackgroundTint;
            float _MirrorVisible;
            
            // ミラーカメラ検出機能
            bool DetectMirrorRendering()
            {
                // VRChatミラー環境の判定処理
                return !(unity_CameraProjection[2][0] == 0.f || unity_CameraProjection[2][1] == 0.f);
            }
            
            OutputVertex BackgroundVertex (InputVertex input)
            {
                OutputVertex output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.position);
                if (_MirrorVisible == 1 & (unity_CameraProjection[2][0] == 0.f || unity_CameraProjection[2][1] == 0.f)){
                    output.position = -1;
                }
                UNITY_TRANSFER_FOG(output,output.position);
                return output;
            }
            
            fixed4 BackgroundFragment (OutputVertex input) : COLOR
            {
                fixed4 finalColor = _BackgroundTint;
                return finalColor;
            }
            ENDCG
        }
    }
    
    FallBack "Diffuse"
}