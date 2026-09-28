// RiftWeb: the real wall fracturing around the rift (CONCEPT section 4: "a crack of light splits open on your real
// wall"). A wall-aligned quad; hairline fracture lines from a Voronoi edge field (in metres on the wall, so the
// pattern never stretches) spread out to _Spread as the rift tears open and retract as it seals. Embers drift up off
// the crack. Purely additive over passthrough; _Spread and _Open arrive via MaterialPropertyBlock.
Shader "Ricochet/RiftWeb"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.25, 0.8, 1)
        _Intensity ("Intensity", Float) = 1.2
        _Spread ("Spread (m)", Float) = 0.6
        _Open ("Open", Range(0, 1)) = 1
        _Cell ("Fracture cell (m)", Float) = 0.14
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "RiftWeb"
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
                float _Spread;
                float _Open;
                float _Cell;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 local : TEXCOORD0;   // metres from the rift centre, along the wall (x right, y up)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                float3 centre = UNITY_MATRIX_M._m03_m13_m23;
                float3 right = normalize(UNITY_MATRIX_M._m00_m10_m20);
                float3 up = normalize(UNITY_MATRIX_M._m01_m11_m21);
                o.local = float2(dot(ws - centre, right), dot(ws - centre, up));
                return o;
            }

            float2 Hash22(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.xx + q.yz) * q.zy);
            }

            // Distance to the nearest Voronoi edge (F2 - F1), plus the nearest cell's hash for per-shard flicker.
            float2 Voronoi(float2 x)
            {
                float2 n = floor(x), f = frac(x);
                float f1 = 8.0, f2 = 8.0, id = 0.0;
                for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    float2 g = float2(i, j);
                    float2 h = Hash22(n + g);
                    float d = length(g + h - f);
                    if (d < f1) { f2 = f1; f1 = d; id = h.x; }
                    else if (d < f2) f2 = d;
                }
                return float2(f2 - f1, id);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = i.local;
                // Elliptical reach (tall like the crack), growing with _Spread.
                float r = length(p * float2(1.0, 0.62));
                float reach = saturate(1.0 - r / max(_Spread, 1e-3));
                if (reach <= 0.0) discard;

                float t = _Time.y;
                // Fractures radiate from the impact: Voronoi in a polar, log-radial domain turns cell edges into
                // spokes plus a few arcs. The angle runs 0..1 around (seam straight down) over a whole number of
                // cells, so it wraps cleanly; log(r) packs the cells tight at the crack and wide further out.
                float ang = atan2(p.x, -p.y) / 6.2831853 + 0.5;
                float2 q = float2(ang * 11.0, log(r + 0.04) * 2.6);
                float2 v = Voronoi(q);
                float lineW = 0.03 + 0.06 * reach;                          // hairlines thicken near the crack
                float lines = smoothstep(lineW, 0.0, v.x);
                // Break the web up: some shards' edges never crack, so it reads as fractures, not a mesh.
                lines *= step(0.3, v.y);
                float flicker = 0.65 + 0.35 * sin(t * (2.0 + 4.0 * v.y) + v.y * 30.0);
                float web = lines * reach * reach * reach * flicker * 1.6;

                // Embers: sparse dots drifting up and off the crack, fading as they rise.
                float2 ep = float2(p.x * 22.0, p.y * 9.0 - t * 1.6);
                float2 cell = floor(ep);
                float2 h = Hash22(cell);
                float2 c = cell + 0.5 + (h - 0.5) * 0.6;
                float ember = step(0.9, h.x) * saturate(1.0 - length((ep - c) * float2(1.0, 0.45)) * 3.0);
                ember *= saturate(1.0 - abs(p.x) / 0.22) * reach;

                float3 rgb = _Color.rgb * (web * 1.2 + ember * 1.6) + float3(1.0, 0.9, 1.0) * web * web * 0.4;
                return half4(rgb * _Intensity * _Open, 0.0);
            }
            ENDHLSL
        }
    }
}
