// Scrim: a soft, dark rounded card behind world text (reward cards, banners), so words read over a bright real room.
// Premultiplied: it outputs a faint tint and an alpha, so over passthrough it dims what is behind it (the same path
// the rift void uses to replace the wall). No texture; the card's aspect comes from the object scale, so corners stay
// round at any size. Drawn just before the other transparents, so glyphs, glow and text land on top of it.
Shader "Ricochet/Scrim"
{
    Properties
    {
        _Color ("Tint (premultiplied, keep it dark)", Color) = (0.035, 0.015, 0.07, 1)
        _Intensity ("Opacity", Range(0, 1)) = 0.55
        // Keep radius + softness <= 0.5, so the fade ends inside the quad.
        _Radius ("Corner radius (fraction of height)", Range(0, 0.5)) = 0.2
        _Softness ("Edge softness (fraction of height)", Range(0.01, 0.5)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Scrim"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
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
                float _Softness;
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
                float aspect : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                float w = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float h = length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y));
                o.aspect = w / max(h, 1e-5);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // Card space in units of half the height: x spans +/-aspect, y spans +/-1.
                float2 p = (i.uv - 0.5) * 2.0 * float2(i.aspect, 1.0);
                float soft = _Softness * 2.0;
                float r = _Radius * 2.0;
                // The opaque core shrinks by the softness, so the fade ends exactly at the quad edge (no visible box).
                float2 b = float2(i.aspect, 1.0) - soft - r;
                float2 q = abs(p) - max(b, 0.0);
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
                float a = 1.0 - smoothstep(0.0, soft, d);
                a = a * a * (3.0 - 2.0 * a) * _Intensity;
                return half4(_Color.rgb * a, a);
            }
            ENDHLSL
        }
    }
}
