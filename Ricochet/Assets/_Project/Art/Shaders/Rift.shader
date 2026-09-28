// Rift: a crack in a real wall that opens onto a void (CONCEPT sections 3-4). Two passes on the crack mesh:
//  1. Void: the crack's interior is drawn dark and OPAQUE in alpha, so the passthrough compositor shows our void
//     instead of the wall (additive light alone can never darken the real room). Inside: star layers at several
//     depths with parallax (a hole with depth, not a sticker) and a slow nebula swirl.
//  2. Glow: additive white-hot seam and magenta lips with energy crawling along the crack.
// The jagged outline comes from EncounterMeshes.Rift: uv.x across the crack (0..1), uv.y along it. _Open (0..1) fades.
Shader "Ricochet/Rift"
{
    Properties
    {
        _Color ("Glow Color", Color) = (1, 0.25, 0.8, 1)
        _Intensity ("Intensity", Float) = 1.6
        _Open ("Open", Range(0, 1)) = 1
        _VoidColor ("Void", Color) = (0.03, 0.005, 0.07, 1)
        _NebulaColor ("Nebula", Color) = (0.45, 0.08, 0.6, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Intensity;
            float _Open;
            float4 _VoidColor;
            float4 _NebulaColor;
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
            float3 positionWS : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes v)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.uv = v.uv;
            return o;
        }

        float Ends(float2 uv) { return smoothstep(0.0, 0.12, uv.y) * smoothstep(1.0, 0.88, uv.y); }
        ENDHLSL

        Pass
        {
            Name "RiftVoid"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha   // premultiplied: alpha 1 = our void replaces the passthrough wall
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVoid
            #pragma multi_compile_instancing

            float Hash2(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float StarLayer(float2 q, float t, float seed)
            {
                float2 cell = floor(q);
                float h = Hash2(cell + seed);
                float2 centre = cell + 0.5 + (float2(Hash2(cell + seed + 1.7), Hash2(cell + seed + 9.1)) - 0.5) * 0.7;
                float d = length(q - centre);
                float twinkle = 0.55 + 0.45 * sin(t * (1.5 + 3.0 * h) + h * 50.0);
                return step(0.82, h) * saturate(1.0 - d * 4.0) * twinkle;
            }

            half4 FragVoid(Varyings i) : SV_Target
            {
                float across = 1.0 - abs(i.uv.x * 2.0 - 1.0);
                // The void fills the inner crack; the lips (outer third) stay light-only in the glow pass.
                float inside = smoothstep(0.25, 0.55, across) * Ends(i.uv) * saturate(_Open * 1.5);
                if (inside <= 0.001) discard;

                // Rift frame in metres: right/up along the wall, out = toward the room.
                float3 right = normalize(UNITY_MATRIX_M._m00_m10_m20);
                float3 up = normalize(UNITY_MATRIX_M._m01_m11_m21);
                float3 outN = normalize(UNITY_MATRIX_M._m02_m12_m22);
                float3 view = normalize(i.positionWS - GetCameraPositionWS()); // eye -> surface, into the wall
                float2 p = float2(dot(i.positionWS, right), dot(i.positionWS, up));
                float2 slope = float2(dot(view, right), dot(view, up)) / max(-dot(view, outN), 0.2);

                float t = _Time.y;
                float3 rgb = _VoidColor.rgb;
                // Nebula: two slow sine fields, deep behind the stars.
                float2 np = p * 3.0 + slope * 0.9 + float2(t * 0.05, -t * 0.03);
                float neb = sin(np.x * 2.1 + sin(np.y * 1.7 + t * 0.2)) * sin(np.y * 1.3 - t * 0.15);
                rgb += _NebulaColor.rgb * pow(saturate(neb * 0.5 + 0.5), 2.0) * 0.3;
                // Stars at three depths: deeper layers slide further with the view (parallax = depth).
                rgb += float3(0.9, 0.85, 1.0) * StarLayer((p + slope * 0.25) * 40.0, t, 0.0);
                rgb += float3(0.8, 0.7, 1.0) * StarLayer((p + slope * 0.6) * 28.0, t, 3.3) * 0.8;
                rgb += float3(1.0, 0.6, 0.9) * StarLayer((p + slope * 1.2) * 18.0, t, 7.9) * 0.6;
                // Near the lips the void is lit by the rim's magenta.
                rgb += _Color.rgb * pow(1.0 - saturate((across - 0.25) / 0.75), 4.0) * 0.3;
                return half4(rgb * inside, inside);
            }
            ENDHLSL
        }

        Pass
        {
            Name "RiftGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragGlow
            #pragma multi_compile_instancing

            half4 FragGlow(Varyings i) : SV_Target
            {
                float across = 1.0 - abs(i.uv.x * 2.0 - 1.0);           // 1 on the seam, 0 at the jagged edge
                // Light lives on the lips: a band just outside the void, plus a faint seam deep inside.
                float lips = exp(-pow((across - 0.28) / 0.14, 2.0));
                float body = across * across * 0.25;
                float t = _Time.y;
                float crawl = 0.7 + 0.3 * sin(i.uv.y * 23.0 - t * 5.0) * sin(i.uv.y * 9.0 + t * 3.1);
                float shimmer = 0.9 + 0.1 * sin(t * 37.0 + i.uv.y * 61.0);
                float3 rgb = (_Color.rgb * (lips * 1.4 + body) * crawl + lips * lips * 0.6) * shimmer * Ends(i.uv);
                return half4(rgb * _Intensity * _Open, 0.0);
            }
            ENDHLSL
        }
    }
}
