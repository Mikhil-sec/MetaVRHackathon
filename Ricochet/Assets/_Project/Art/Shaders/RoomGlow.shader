// RoomGlow: the game's light on your real room (docs/TECH_GUIDE.md section 3).
// Drawn on the MRUK EffectMesh. Passthrough composites premultiplied (out = src.rgb + (1 - src.a) * passthrough),
// so on device the base is transparent (a = 0) and the glow is purely additive over the real wall.
// It also writes depth, so real walls and furniture occlude virtual content behind them.
// On desktop, the global _RoomBaseColor gives the same mesh an opaque, lightly shaded preview.
Shader "Ricochet/RoomGlow"
{
    Properties { }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "RoomGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_GLOWS 8

            // Globals, written each frame by Ricochet.Gameplay.RoomGlow. Unused slots have zero color.
            float4 _GlowSources[MAX_GLOWS]; // xyz = world position, w = radius (m)
            float4 _GlowColors[MAX_GLOWS];  // rgb = color * intensity
            float4 _RoomBaseColor;          // (0,0,0,0) on device; opaque grey for the desktop preview

            CBUFFER_START(UnityPerMaterial)
                float _Unused; // SRP Batcher needs a per-material buffer
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                half3 glow = 0;
                [unroll]
                for (int k = 0; k < MAX_GLOWS; k++)
                {
                    float3 d = _GlowSources[k].xyz - i.positionWS;
                    float dist = length(d);
                    float fall = saturate(1.0 - dist / max(_GlowSources[k].w, 1e-3));
                    fall *= fall;
                    // Surfaces facing the light catch more of it; a little wraps around so nothing goes flat.
                    float facing = saturate(dot(n, d / max(dist, 1e-4)) * 0.75 + 0.25);
                    glow += _GlowColors[k].rgb * (fall * facing);
                }

                float shade = 0.65 + 0.35 * saturate(dot(n, normalize(float3(0.3, 0.8, 0.5))));
                half3 baseRgb = _RoomBaseColor.rgb * shade * _RoomBaseColor.a;
                return half4(baseRgb + glow, _RoomBaseColor.a);
            }
            ENDHLSL
        }
    }
}
