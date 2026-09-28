// Halo: fake bloom (docs/TECH_GUIDE.md section 4). A camera-facing quad with a procedural radial falloff,
// purely additive (alpha stays 0, so over passthrough it only adds light). No texture, no post-processing.
// Billboarding happens in the vertex shader, so there is no per-frame script cost.
Shader "Ricochet/Halo"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.85, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Falloff ("Falloff", Range(0.5, 6)) = 2.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Halo"
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
                float4 _Color;
                float _Intensity;
                float _Falloff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Object origin in view space, then offset the corner in view space: always faces the eye.
                float3 centerVS = TransformWorldToView(TransformObjectToWorld(float3(0, 0, 0)));
                float size = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 cornerVS = centerVS + float3(v.positionOS.xy * size, 0);
                o.positionCS = TransformWViewToHClip(cornerVS);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float d = length(i.uv - 0.5) * 2.0;
                float a = pow(saturate(1.0 - d), _Falloff);
                return half4(_Color.rgb * (a * _Intensity), 0.0);
            }
            ENDHLSL
        }
    }
}
