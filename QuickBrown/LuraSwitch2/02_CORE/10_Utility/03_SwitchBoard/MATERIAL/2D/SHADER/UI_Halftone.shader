// Made with Amplify Shader Editor v1.9.8
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "UI_Halftone"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        _DotAmountXY("DotAmountXY", Vector) = (20,20,0,0)
        _DotScale("DotScale", Float) = 0
        [Header(Noise)]_NoiseGlobalScale("NoiseGlobalScale", Float) = 3
        _circleScale_Noise("circleScale_Noise", Float) = 1
        _NoiseScaleXY("NoiseScaleXY", Vector) = (1,1,0,0)
        _NoiseScrollSpeedXY("NoiseScrollSpeedXY", Vector) = (1,1,0,0)
        [Header(Gradient)]_Y_ScaleGradient("Y_ScaleGradient", Float) = 0
        [Header(Gradient)]_X_ScaleGradient("X_ScaleGradient", Float) = 0
        [Header(Option)]_Sharpness("Sharpness", Float) = 1
        _Rotator("Rotator", Float) = 0
        _Float0("Float 0", Float) = 1
        _UVScale("UVScale", Vector) = (1,1,0,0)

    }

    SubShader
    {
		LOD 0

        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }

        Stencil
        {
        	Ref [_Stencil]
        	ReadMask [_StencilReadMask]
        	WriteMask [_StencilWriteMask]
        	Comp [_StencilComp]
        	Pass [_StencilOp]
        }


        Cull Back
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        
        Pass
        {
            Name "Default"
        CGPROGRAM
            #define ASE_VERSION 19800

            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityShaderVariables.cginc"
            #define ASE_NEEDS_FRAG_COLOR


            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord  : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float4  mask : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
                float4 ase_texcoord3 : TEXCOORD3;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            uniform float2 _UVScale;
            uniform float _Rotator;
            uniform float2 _DotAmountXY;
            uniform float _DotScale;
            uniform float2 _NoiseScrollSpeedXY;
            uniform float2 _NoiseScaleXY;
            uniform float _NoiseGlobalScale;
            uniform float _circleScale_Noise;
            uniform float _Y_ScaleGradient;
            uniform float _X_ScaleGradient;
            uniform float _Sharpness;
            uniform float _Float0;
            uniform float unity_FogDensity;
            float3 mod2D289( float3 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
            float2 mod2D289( float2 x ) { return x - floor( x * ( 1.0 / 289.0 ) ) * 289.0; }
            float3 permute( float3 x ) { return mod2D289( ( ( x * 34.0 ) + 1.0 ) * x ); }
            float snoise( float2 v )
            {
            	const float4 C = float4( 0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439 );
            	float2 i = floor( v + dot( v, C.yy ) );
            	float2 x0 = v - i + dot( i, C.xx );
            	float2 i1;
            	i1 = ( x0.x > x0.y ) ? float2( 1.0, 0.0 ) : float2( 0.0, 1.0 );
            	float4 x12 = x0.xyxy + C.xxzz;
            	x12.xy -= i1;
            	i = mod2D289( i );
            	float3 p = permute( permute( i.y + float3( 0.0, i1.y, 1.0 ) ) + i.x + float3( 0.0, i1.x, 1.0 ) );
            	float3 m = max( 0.5 - float3( dot( x0, x0 ), dot( x12.xy, x12.xy ), dot( x12.zw, x12.zw ) ), 0.0 );
            	m = m * m;
            	m = m * m;
            	float3 x = 2.0 * frac( p * C.www ) - 1.0;
            	float3 h = abs( x ) - 0.5;
            	float3 ox = floor( x + 0.5 );
            	float3 a0 = x - ox;
            	m *= 1.79284291400159 - 0.85373472095314 * ( a0 * a0 + h * h );
            	float3 g;
            	g.x = a0.x * x0.x + h.x * x0.y;
            	g.yz = a0.yz * x12.xz + h.yz * x12.yw;
            	return 130.0 * dot( m, g );
            }
            

            
            v2f vert(appdata_t v )
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 ase_positionWS = mul(unity_ObjectToWorld, float4( (v.vertex).xyz, 1 )).xyz;
                OUT.ase_texcoord3.xyz = ase_positionWS;
                
                
                //setting value to unused interpolator channels and avoid initialization warnings
                OUT.ase_texcoord3.w = 0;

                v.vertex.xyz +=  float3( 0, 0, 0 ) ;

                float4 vPosition = UnityObjectToClipPos(v.vertex);
                OUT.worldPosition = v.vertex;
                OUT.vertex = vPosition;

                float2 pixelSize = vPosition.w;
                pixelSize /= float2(1, 1) * abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));

                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                float2 maskUV = (v.vertex.xy - clampedRect.xy) / (clampedRect.zw - clampedRect.xy);
                OUT.texcoord = v.texcoord;
                OUT.mask = float4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));

                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN ) : SV_Target
            {
                //Round up the alpha color coming from the interpolator (to 1.0/256.0 steps)
                //The incoming alpha could have numerical instability, which makes it very sensible to
                //HDR color transparency blend, when it blends with the world's texture.
                const half alphaPrecision = half(0xff);
                const half invAlphaPrecision = half(1.0/alphaPrecision);
                IN.color.a = round(IN.color.a * alphaPrecision)*invAlphaPrecision;

                float2 texCoord2 = IN.texcoord.xy * _UVScale + float2( 0,0 );
                float cos34 = cos( radians( _Rotator ) );
                float sin34 = sin( radians( _Rotator ) );
                float2 rotator34 = mul( texCoord2 - float2( 0.5,0.5 ) , float2x2( cos34 , -sin34 , sin34 , cos34 )) + float2( 0.5,0.5 );
                float2 temp_output_41_0 = ( rotator34 * _DotAmountXY );
                float simplePerlin2D22 = snoise( ( ( ( ceil( temp_output_41_0 ) / _DotAmountXY ) + ( _Time.y * _NoiseScrollSpeedXY ) ) * _NoiseScaleXY )*_NoiseGlobalScale );
                simplePerlin2D22 = simplePerlin2D22*0.5 + 0.5;
                float2 texCoord48 = IN.texcoord.xy * float2( 1,1 ) + float2( 0,0 );
                float2 texCoord89 = IN.texcoord.xy * float2( 1,1 ) + float2( 0,0 );
                float3 ase_positionWS = IN.ase_texcoord3.xyz;
                float4 lerpResult97 = lerp( ( ( 1.0 - saturate( tanh( ( ( distance( ( frac( temp_output_41_0 ) - float2( 0.5,0.5 ) ) , float2( 0,0 ) ) + ( 1.0 - _DotScale ) + ( simplePerlin2D22 * _circleScale_Noise ) + ( texCoord48.y * _Y_ScaleGradient ) + ( texCoord89.x * _X_ScaleGradient ) ) * ( _Sharpness / ( max( length( ddx( temp_output_41_0 ) ) , length( ddy( temp_output_41_0 ) ) ) * _Float0 ) ) ) ) ) ) * IN.color ) , float4( unity_FogColor.rgb , 0.0 ) , saturate( ( distance( ase_positionWS , _WorldSpaceCameraPos ) * unity_FogDensity ) ));
                

                half4 color = lerpResult97;

                #ifdef UNITY_UI_CLIP_RECT
                half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(IN.mask.xy)) * IN.mask.zw);
                color.a *= m.x * m.y;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                color.rgb *= color.a;

                return color;
            }
        ENDCG
        }
    }
    CustomEditor "ASEMaterialInspector"
	
	Fallback Off
}
/*ASEBEGIN
Version=19800
Node;AmplifyShaderEditor.CommentaryNode;57;-1440,-224;Inherit;False;596;211;OffsetRotate_45;3;34;40;35;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RangedFloatNode;35;-1376,-128;Inherit;False;Property;_Rotator;Rotator;9;0;Create;True;0;0;0;False;0;False;0;45;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.Vector2Node;87;-1504,-432;Inherit;False;Property;_UVScale;UVScale;11;0;Create;True;0;0;0;False;0;False;1,1;1,10;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.CommentaryNode;58;-770,-226;Inherit;False;1220;435;MakeDot;11;41;30;31;10;4;7;8;72;73;75;76;;1,1,1,1;0;0
Node;AmplifyShaderEditor.RadiansOpNode;40;-1216,-128;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;2;-1296,-432;Inherit;False;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RotatorNode;34;-1040,-176;Inherit;False;3;0;FLOAT2;0,0;False;1;FLOAT2;0.5,0.5;False;2;FLOAT;1;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector2Node;10;-720,48;Inherit;False;Property;_DotAmountXY;DotAmountXY;0;0;Create;True;0;0;0;False;0;False;20,20;10,10;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.CommentaryNode;52;-704,320;Inherit;False;1556.465;446.745;Noise;8;33;27;22;25;43;44;29;55;;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;41;-544,-176;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.CommentaryNode;55;-640,400;Inherit;False;484;307;NoiseScroll;3;53;54;28;;1,1,1,1;0;0
Node;AmplifyShaderEditor.Vector2Node;53;-592,560;Inherit;False;Property;_NoiseScrollSpeedXY;NoiseScrollSpeedXY;5;0;Create;True;0;0;0;False;0;False;1,1;0,0;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.SimpleTimeNode;28;-560,464;Inherit;False;1;0;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.CeilOpNode;30;-384,-64;Inherit;False;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;54;-336,480;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;31;-256,32;Inherit;False;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DdxOpNode;72;48,48;Inherit;False;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.DdyOpNode;73;48,128;Inherit;False;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.SimpleAddOpNode;29;-80,368;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.Vector2Node;44;-80,544;Inherit;False;Property;_NoiseScaleXY;NoiseScaleXY;4;0;Create;True;0;0;0;False;0;False;1,1;1,1;0;3;FLOAT2;0;FLOAT;1;FLOAT;2
Node;AmplifyShaderEditor.CommentaryNode;92;910,782;Inherit;False;676;352;Comment;3;89;90;91;;1,1,1,1;0;0
Node;AmplifyShaderEditor.LengthOpNode;75;192,48;Inherit;False;1;0;FLOAT2;0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.LengthOpNode;76;192,128;Inherit;False;1;0;FLOAT2;0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.FractNode;4;-288,-176;Inherit;False;1;0;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.CommentaryNode;51;912,400;Inherit;False;719;353;Y_Mask;3;49;50;48;;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;43;208,368;Inherit;False;2;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT2;0
Node;AmplifyShaderEditor.RangedFloatNode;25;144,592;Inherit;False;Property;_NoiseGlobalScale;NoiseGlobalScale;2;1;[Header];Create;True;1;Noise;0;0;False;0;False;3;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMaxOpNode;77;368,80;Inherit;True;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;80;576,224;Inherit;False;Property;_Float0;Float 0;10;0;Create;True;0;0;0;False;0;False;1;50;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleSubtractOpNode;7;-112,-176;Inherit;False;2;0;FLOAT2;0,0;False;1;FLOAT2;0.5,0.5;False;1;FLOAT2;0
Node;AmplifyShaderEditor.NoiseGeneratorNode;22;400,368;Inherit;True;Simplex2D;True;False;2;0;FLOAT2;0,0;False;1;FLOAT;1;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;27;416,592;Inherit;False;Property;_circleScale_Noise;circleScale_Noise;3;0;Create;True;0;0;0;False;0;False;1;0.2;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;17;1152,240;Inherit;False;Property;_DotScale;DotScale;1;0;Create;True;0;0;0;False;0;False;0;1.35;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;48;928,448;Inherit;True;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;50;1168,608;Inherit;False;Property;_Y_ScaleGradient;Y_ScaleGradient;6;1;[Header];Create;True;1;Gradient;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.TextureCoordinatesNode;89;960,832;Inherit;True;0;-1;2;3;2;SAMPLER2D;;False;0;FLOAT2;1,1;False;1;FLOAT2;0,0;False;5;FLOAT2;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.RangedFloatNode;90;1200,992;Inherit;False;Property;_X_ScaleGradient;X_ScaleGradient;7;1;[Header];Create;True;1;Gradient;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;82;688,80;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.DistanceOpNode;8;80,-176;Inherit;False;2;0;FLOAT2;0,0;False;1;FLOAT2;0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;33;688,368;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;56;1328,240;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;15;736,-32;Inherit;False;Property;_Sharpness;Sharpness;8;1;[Header];Create;True;1;Option;0;0;False;0;False;1;30;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;49;1376,496;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;91;1408,880;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleAddOpNode;16;1504,-128;Inherit;False;5;5;0;FLOAT;0;False;1;FLOAT;0;False;2;FLOAT;0;False;3;FLOAT;0;False;4;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleDivideOpNode;83;944,-32;Inherit;False;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.CommentaryNode;101;2430,478;Inherit;False;964;539;Fog;7;93;94;98;99;95;96;100;;1,1,1,1;0;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;14;1728,-224;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;10;False;1;FLOAT;0
Node;AmplifyShaderEditor.TanhOpNode;13;2000,-176;Inherit;True;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.WorldSpaceCameraPos;93;2480,832;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.WorldPosInputsNode;94;2528,672;Inherit;False;0;4;FLOAT3;0;FLOAT;1;FLOAT;2;FLOAT;3
Node;AmplifyShaderEditor.SaturateNode;69;2224,-80;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.RangedFloatNode;98;2768,896;Inherit;False;Global;unity_FogDensity;unity_FogDensity;5;0;Create;True;0;0;0;False;0;False;0;0;0;0;0;1;FLOAT;0
Node;AmplifyShaderEditor.DistanceOpNode;99;2832,752;Inherit;False;2;0;FLOAT3;0,0,0;False;1;FLOAT3;0,0,0;False;1;FLOAT;0
Node;AmplifyShaderEditor.OneMinusNode;62;2416,-80;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.VertexColorNode;59;2432,128;Inherit;False;0;5;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;95;3072,768;Inherit;False;2;2;0;FLOAT;0;False;1;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.SimpleMultiplyOpNode;86;2608,-80;Inherit;False;2;2;0;FLOAT;0;False;1;COLOR;0,0,0,0;False;1;COLOR;0
Node;AmplifyShaderEditor.SaturateNode;96;3216,752;Inherit;False;1;0;FLOAT;0;False;1;FLOAT;0
Node;AmplifyShaderEditor.ColorNode;100;2800,528;Inherit;False;Global;unity_FogColor;unity_FogColor;5;0;Fetch;True;0;0;0;False;0;False;0,0,0,0;0.4620771,0.7011021,0.9559735,1;True;True;0;6;COLOR;0;FLOAT;1;FLOAT;2;FLOAT;3;FLOAT;4;FLOAT3;5
Node;AmplifyShaderEditor.LerpOp;97;3504,-128;Inherit;False;3;0;COLOR;0,0,0,0;False;1;COLOR;0,0,0,0;False;2;FLOAT;0;False;1;COLOR;0
Node;AmplifyShaderEditor.TemplateMultiPassMasterNode;66;3696,-144;Float;False;True;-1;3;ASEMaterialInspector;0;3;UI_Halftone;5056123faa0c79b47ab6ad7e8bf059a4;True;Default;0;0;Default;2;True;True;3;1;False;;10;False;;3;1;False;;10;False;;False;False;False;False;False;False;False;False;False;False;False;True;True;0;False;;True;True;True;True;True;True;0;True;_ColorMask;False;False;False;False;False;False;False;True;True;0;True;_Stencil;255;True;_StencilReadMask;255;True;_StencilWriteMask;0;True;_StencilComp;0;True;_StencilOp;0;False;;0;False;;0;False;;0;False;;0;False;;0;False;;False;True;2;False;;True;0;True;unity_GUIZTestMode;False;True;5;Queue=Transparent=Queue=0;IgnoreProjector=True;RenderType=Transparent=RenderType;PreviewType=Plane;CanUseSpriteAtlas=True;False;False;0;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;False;True;3;False;0;;0;0;Standard;0;0;1;True;False;;False;0
WireConnection;40;0;35;0
WireConnection;2;0;87;0
WireConnection;34;0;2;0
WireConnection;34;2;40;0
WireConnection;41;0;34;0
WireConnection;41;1;10;0
WireConnection;30;0;41;0
WireConnection;54;0;28;0
WireConnection;54;1;53;0
WireConnection;31;0;30;0
WireConnection;31;1;10;0
WireConnection;72;0;41;0
WireConnection;73;0;41;0
WireConnection;29;0;31;0
WireConnection;29;1;54;0
WireConnection;75;0;72;0
WireConnection;76;0;73;0
WireConnection;4;0;41;0
WireConnection;43;0;29;0
WireConnection;43;1;44;0
WireConnection;77;0;75;0
WireConnection;77;1;76;0
WireConnection;7;0;4;0
WireConnection;22;0;43;0
WireConnection;22;1;25;0
WireConnection;82;0;77;0
WireConnection;82;1;80;0
WireConnection;8;0;7;0
WireConnection;33;0;22;0
WireConnection;33;1;27;0
WireConnection;56;0;17;0
WireConnection;49;0;48;2
WireConnection;49;1;50;0
WireConnection;91;0;89;1
WireConnection;91;1;90;0
WireConnection;16;0;8;0
WireConnection;16;1;56;0
WireConnection;16;2;33;0
WireConnection;16;3;49;0
WireConnection;16;4;91;0
WireConnection;83;0;15;0
WireConnection;83;1;82;0
WireConnection;14;0;16;0
WireConnection;14;1;83;0
WireConnection;13;0;14;0
WireConnection;69;0;13;0
WireConnection;99;0;94;0
WireConnection;99;1;93;0
WireConnection;62;0;69;0
WireConnection;95;0;99;0
WireConnection;95;1;98;0
WireConnection;86;0;62;0
WireConnection;86;1;59;0
WireConnection;96;0;95;0
WireConnection;97;0;86;0
WireConnection;97;1;100;5
WireConnection;97;2;96;0
WireConnection;66;0;97;0
ASEEND*/
//CHKSM=BCEC8F3982B198235F601A9C9384ECA9CD00692A