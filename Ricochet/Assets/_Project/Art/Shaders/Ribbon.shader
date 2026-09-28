// Ribbon: the Spark's light trail (docs/TECH_GUIDE.md section 4). Purely additive (alpha 0 over passthrough).
// Vertex color carries the taper and the combo heat; the cross-section is a soft glow with a white-hot core,
// computed from uv.y, so there is no texture. Geometry is built by SparkRibbon.
Shader "Ricochet/Ribbon"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1.4
        _Core ("White-hot core", Range(0, 2)) = 0.9
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Ribbon"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Core;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);   // 0 at the edges, 1 on the center line
                float glow = across * across;
                float core = pow(across, 8.0) * _Core * i.color.a; // the core fades faster than the glow
                half3 rgb = i.color.rgb * glow * i.color.a + core;
                return half4(rgb * _Intensity, 0.0);
            }
            ENDHLSL
        }
    }
}
