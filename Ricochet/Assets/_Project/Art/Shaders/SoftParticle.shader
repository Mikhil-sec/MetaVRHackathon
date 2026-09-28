// SoftParticle: glowing particles with no texture (docs/TECH_GUIDE.md section 4). A soft elliptical falloff with a
// hot core over the particle quad, so stretched billboards read as streaks of light with rounded, fading ends, and
// plain billboards as soft glints. Additive (alpha 0 over passthrough); colour comes from the particle vertex colour.
Shader "Ricochet/SoftParticle"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1.6
        _Core ("Hot core", Range(0, 2)) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SoftParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Core;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
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
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float d = length((i.uv - 0.5) * 2.0);          // 0 centre .. 1 at the quad's inscribed edge
                float glow = pow(saturate(1.0 - d), 1.6);
                float core = pow(saturate(1.0 - d * 1.8), 3.0) * _Core;
                half3 rgb = i.color.rgb * glow + core;          // the core burns toward white
                return half4(rgb * i.color.a * _Intensity, 0.0);
            }
            ENDHLSL
        }
    }
}
