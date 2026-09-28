// Tendril: the creature's trailing ink ribbons (CONCEPT section 6: ink-and-light, animated through vertex motion).
// The mesh is a rest pose (EncounterMeshes.Tendrils); the sway is all here, so tendrils cost no CPU.
// Ink at the root, the intent color toward the tip, and pulses of light that travel root to tip along a bright
// centre vein, faster with _Energy (the windup). Opaque, unlit, no texture.
Shader "Ricochet/Tendril"
{
    Properties
    {
        _InkColor ("Ink", Color) = (0.035, 0.012, 0.06, 1)
        _Glow ("Glow (identity)", Color) = (1, 0.2, 0.75, 1)
        _Sway ("Sway (body units at the tip)", Float) = 0.22
        _PulseSpeed ("Pulse speed", Float) = 1.6
        _Tint ("Tint", Color) = (1, 0.2, 0.75, 1)
        _Energy ("Energy", Range(0, 1)) = 0
        _Flash ("Flash", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Tendril"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _InkColor;
                float4 _Glow;
                float _Sway;
                float _PulseSpeed;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
                UNITY_DEFINE_INSTANCED_PROP(float, _Energy)
                UNITY_DEFINE_INSTANCED_PROP(float, _Flash)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float phase : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float energy = UNITY_ACCESS_INSTANCED_PROP(Props, _Energy);
                float along = v.uv.y;
                float phase = v.uv2.x;
                float t = _Time.y * (1.0 + 1.5 * energy);
                // A travelling wave down each tendril: the root holds, the tip whips. Energy makes it thrash.
                float amp = _Sway * along * along * (1.0 + 0.8 * energy);
                float3 p = v.positionOS.xyz;
                p.x += sin(t * 1.7 - along * 4.0 + phase) * amp;
                p.z += cos(t * 1.3 - along * 3.0 + phase * 1.3) * amp * 0.6;
                p.y += sin(t * 2.1 - along * 5.0 + phase) * amp * 0.25;
                o.positionCS = TransformObjectToHClip(p);
                o.uv = v.uv;
                o.phase = phase;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
                float energy = UNITY_ACCESS_INSTANCED_PROP(Props, _Energy);
                float flash = UNITY_ACCESS_INSTANCED_PROP(Props, _Flash);
                float along = i.uv.y;
                float across = 1.0 - abs(i.uv.x * 2.0 - 1.0);     // 1 on the centre line
                float vein = pow(across, 5.0);
                // Pulses of light running root to tip along the vein.
                float pulse = frac(along * 2.5 - _Time.y * _PulseSpeed * (1.0 + 2.0 * energy) + i.phase * 0.16);
                pulse = pow(1.0 - pulse, 6.0);
                float glow = pow(along, 1.4) * 0.9 + vein * (0.35 + 1.4 * pulse) * (0.6 + along) + 0.25 * energy * vein;
                // Magenta along the length; only the tips take the intent colour (more so as it winds up).
                float3 hue = lerp(_Glow.rgb, tint.rgb, saturate(along * along * (0.7 + 0.3 * energy)));
                float3 rgb = _InkColor.rgb + hue * glow;
                rgb = lerp(rgb, float3(1.0, 0.9, 1.0) * 1.4, flash);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
