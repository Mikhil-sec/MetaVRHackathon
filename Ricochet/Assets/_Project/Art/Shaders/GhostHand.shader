// GhostHand: the onboarding demo's hand of light (CONCEPT section 4, zero-text onboarding). A real hand mesh baked
// from the simulator's tracked hand in two poses: open in POSITION/NORMAL, pinching in TEXCOORD1/TEXCOORD2.
// _Pinch morphs between them in the vertex shader (one mesh, one draw call, no CPU skinning). Pure light: additive,
// no depth, so it never hides the real Spark or sling, and it shows over passthrough on device (unlike HandLight,
// which is depth-only there because the player's real hands are visible).
// Look: a cool fresnel rim, a faint glass fill, and a band of light that runs from the wrist to the fingertips.
Shader "Ricochet/GhostHand"
{
    Properties
    {
        _Color ("Rim Color", Color) = (0.7, 0.95, 1, 1)
        _FillColor ("Fill Color", Color) = (0.25, 0.5, 0.7, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _Fill ("Fill", Range(0, 1)) = 0.12
        _Pinch ("Pinch", Range(0, 1)) = 0
        _Fade ("Fade", Range(0, 1)) = 1
        _Sweep ("Sweep (0..1 wrist to tips, <0 off)", Float) = -1
        _Length ("Hand Length (m, wrist to tips along +Z)", Float) = 0.19
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "GhostHand"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _FillColor;
                float _RimPower;
                float _Fill;
                float _Pinch;
                float _Fade;
                float _Sweep;
                float _Length;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 pinchPos : TEXCOORD1;
                float3 pinchNormal : TEXCOORD2;
                float3 reach : TEXCOORD3; // x: 0 at the wrist .. 1 at the fingertips (baked along the hand)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float reach : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float k = smoothstep(0.0, 1.0, _Pinch);
                float3 p = lerp(v.positionOS.xyz, v.pinchPos, k);
                float3 n = normalize(lerp(v.normalOS, v.pinchNormal, k));
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(n);
                o.reach = v.reach.x;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(i.positionWS));
                float facing = saturate(dot(n, view));
                float rim = pow(1.0 - facing, _RimPower);
                // The band: a soft bright front travelling up the hand, leaving a faint afterglow.
                float band = _Sweep < 0.0 ? 0.0 : exp(-pow((i.reach - _Sweep) * 7.0, 2.0)) * 1.4
                                                  + saturate((_Sweep - i.reach) * 3.0) * 0.15;
                // Fingertips glow a touch more: they are the part that does the gesture.
                float tips = smoothstep(0.75, 1.0, i.reach) * 0.35;
                float3 rgb = _FillColor.rgb * _Fill + _Color.rgb * (rim * (1.0 + tips) + band * (0.35 + rim));
                return half4(rgb * _Fade, 0.0);
            }
            ENDHLSL
        }
    }
}
