// Ring: a camera-facing procedural ring (docs/TECH_GUIDE.md section 4). Used for the sling's hover ring, the
// "grab me" ripple and the grab flash. Purely additive (alpha 0 over passthrough), no texture.
// _Radius and _Width are in quad units (0.5 = the quad edge). _Segments > 0 cuts the ring into dashes; _Spin turns them.
// Per-frame values arrive via MaterialPropertyBlock (a handful of renderers, so losing SRP batching is fine).
Shader "Ricochet/Ring"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.85, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Radius ("Radius", Range(0, 0.5)) = 0.3
        _Width ("Width", Range(0.001, 0.2)) = 0.02
        _Segments ("Segments (0 = solid)", Float) = 0
        _Gap ("Dash gap", Range(0, 0.9)) = 0.35
        _Spin ("Spin (turns)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Ring"
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
                float _Radius;
                float _Width;
                float _Segments;
                float _Gap;
                float _Spin;
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
                float3 centerVS = TransformWorldToView(TransformObjectToWorld(float3(0, 0, 0)));
                float size = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 cornerVS = centerVS + float3(v.positionOS.xy * size, 0);
                o.positionCS = TransformWViewToHClip(cornerVS);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                float r = length(p);
                float d = (r - _Radius) / _Width;
                // A crisp line plus a wide soft skirt: reads as a lit filament with bloom.
                float line_ = exp(-d * d);
                float skirt = exp(-abs(d) * 0.35) * 0.3;
                float a = line_ + skirt;

                if (_Segments > 0.5)
                {
                    float turn = atan2(p.y, p.x) * (0.5 / PI) + _Spin;
                    float s = frac(turn * _Segments);
                    float half_ = (1.0 - _Gap) * 0.5;
                    float dash = 1.0 - smoothstep(half_ - 0.06, half_, abs(s - 0.5));
                    a *= lerp(0.15, 1.0, dash); // gaps keep a faint glow so the ring never breaks apart
                }

                a *= saturate((0.5 - r) * 12.0); // fade before the quad edge
                return half4(_Color.rgb * (a * _Intensity), 0.0);
            }
            ENDHLSL
        }
    }
}
