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
            float4 _GlowWave;               // xyz = origin, w = current radius (m): a light front sweeping the room
            float4 _GlowWaveColor;          // rgb = color * intensity (0 when idle), a = front width (m)

            // Pocket Arena (Ricochet.Room.PocketArena): the room is a virtual glass chamber, so it needs its own look.
            #define MAX_ARENA_BOXES 4
            float4x4 _ArenaWorldToLocal;            // world -> the seat's floor frame (x right, y up, z forward)
            float4 _ArenaBoxC[MAX_ARENA_BOXES];     // box centres in that frame: [0] the chamber, then its furniture
            float4 _ArenaBoxH[MAX_ARENA_BOXES];     // box half extents
            float4 _ArenaParams;                    // x = box count, y/z = reveal start/end (forward m), w = 1 when live
            float4 _ArenaColor;                     // rgb = seam color * intensity

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

                // The Fever wave: a bright front racing out across the real walls, with a fading afterglow behind it
                // and fine world-space ripples in the band so it reads as energy, not a flat fade.
                float waveDist = length(i.positionWS - _GlowWave.xyz);
                float behind = _GlowWave.w - waveDist;
                float width = max(_GlowWaveColor.a, 1e-3);
                float front = exp(-(behind * behind) / (width * width));
                float after = saturate(behind / (width * 4.0)) * saturate(1.0 - behind / (width * 14.0)) * 0.18 * step(0.0, behind);
                // One crisp front (a faint shimmer, not rings) plus a soft afterglow.
                float shimmer = 0.9 + 0.1 * sin(waveDist * 22.0 - _Time.y * 8.0);
                glow += _GlowWaveColor.rgb * (front * shimmer + after);

                float shade = 0.65 + 0.35 * saturate(dot(n, normalize(float3(0.3, 0.8, 0.5))));
                half alpha = _RoomBaseColor.a;
                half3 baseRgb = _RoomBaseColor.rgb * shade * alpha;

                // Derivatives outside the (uniform) branch, so every compiler accepts them.
                float3 p = mul(_ArenaWorldToLocal, float4(i.positionWS, 1.0)).xyz;
                float px = max(fwidth(p.x), max(fwidth(p.y), fwidth(p.z))) + 1e-5; // one pixel, in metres
                UNITY_BRANCH
                if (_ArenaParams.w > 0.5)
                {
                    float3 nl = abs(mul((float3x3)_ArenaWorldToLocal, n));
                    // Fades in with distance ahead of the eye: a stage in front of you, not a box around you.
                    float reveal = smoothstep(_ArenaParams.y, _ArenaParams.z, p.z);

                    // Seams: on a box face, the distance to the second-nearest face plane is the distance to an edge.
                    float seam = 0.0;
                    [unroll]
                    for (int b = 0; b < MAX_ARENA_BOXES; b++)
                    {
                        float3 d = _ArenaBoxH[b].xyz - abs(p - _ArenaBoxC[b].xyz);
                        float lo = min(d.x, min(d.y, d.z));
                        float hi = max(d.x, max(d.y, d.z));
                        float edge = d.x + d.y + d.z - lo - hi;
                        float onBox = step(abs(lo), 0.015) * step((float)b, _ArenaParams.x - 0.5);
                        float core = 1.0 - smoothstep(0.004, 0.004 + px * 1.5, edge);
                        float halo = exp(-edge / 0.05) * 0.3;
                        seam = max(seam, onBox * (core + halo));
                    }
                    // A slow shimmer travelling along the frame.
                    float shimmer = 1.0 + 0.7 * pow(saturate(sin(dot(p, float3(1.3, 0.9, 1.7)) * 2.0 - _Time.y * 1.4)), 8.0);

                    // A fine grid in the surface plane, faded out where it would alias. The ceiling keeps only a
                    // trace: overhead it crowds the view and frames nothing the player aims at.
                    float3 g = abs(frac(p * 2.0 + 0.5) - 0.5) * 0.5; // m to the nearest 50 cm line, per axis
                    float3 lines = (1.0 - smoothstep(0.0012, 0.0012 + px * 1.5, g)) * (1.0 - nl);
                    float grid = max(lines.x, max(lines.y, lines.z)) * saturate(1.0 - px * 40.0);
                    grid *= lerp(1.0, 0.2, saturate(-n.y * 2.0 - 1.0));

                    // Our light catches the glass: seams flare and the grid lights up near the Spark and pulses.
                    half3 arena = (_ArenaColor.rgb + glow * 1.5) * (seam * shimmer) + (_ArenaColor.rgb * 0.08 + glow * 0.8) * grid;
                    glow = glow * lerp(0.4, 1.0, reveal) + arena * reveal;
                    baseRgb *= reveal;
                    alpha *= reveal;
                }
                return half4(baseRgb + glow, alpha);
            }
            ENDHLSL
        }
    }
}
