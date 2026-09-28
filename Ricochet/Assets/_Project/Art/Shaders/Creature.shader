// Creature: "ink and light" (CONCEPT section 6). An opaque ink body that reads as a silhouette over passthrough,
// a magenta fresnel rim, and a slow vertex wobble so it feels alive. Per-renderer _Flash (hurt: white-hot) and
// _Tint (the telegraph color of its next move) come through the instancing buffer. Unlit cost.
Shader "Ricochet/Creature"
{
    Properties
    {
        _InkColor ("Ink", Color) = (0.035, 0.012, 0.06, 1)
        _RimColor ("Rim", Color) = (1, 0.2, 0.75, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _Wobble ("Wobble (m)", Float) = 0.012
        _Tint ("Tint", Color) = (1, 0.2, 0.75, 1)
        _Flash ("Flash", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Creature"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _InkColor;
                float4 _RimColor;
                float _RimPower;
                float _Wobble;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
                UNITY_DEFINE_INSTANCED_PROP(float, _Flash)
                UNITY_DEFINE_INSTANCED_PROP(float, _Energy)
            UNITY_INSTANCING_BUFFER_END(Props)

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // One layer of stars "inside" the body: cells on a direction that parallaxes with the view.
            float Stars(float3 dir, float density, float t)
            {
                float3 q = dir * density;
                float3 cell = floor(q);
                float h = Hash(cell);
                float3 centre = cell + 0.5 + (float3(Hash(cell + 3.1), Hash(cell + 7.7), Hash(cell + 1.3)) - 0.5) * 0.6;
                float d = length(q - centre);
                float twinkle = 0.6 + 0.4 * sin(t * (2.0 + 3.0 * h) + h * 40.0);
                return step(0.8, h) * saturate(1.0 - d * 2.8) * twinkle;
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float3 posOS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Slow, organic breathing: displacement along the normal from a few sliding sines.
                float3 p = v.positionOS.xyz;
                float t = _Time.y;
                float w = sin(p.y * 9.0 + t * 2.3) * 0.5 + sin(p.x * 7.0 - t * 1.7) * 0.3 + sin(p.z * 11.0 + t * 3.1) * 0.2;
                float3 worldScale = float3(length(UNITY_MATRIX_M[0].xyz), length(UNITY_MATRIX_M[1].xyz), length(UNITY_MATRIX_M[2].xyz));
                p += v.normalOS * (w * _Wobble / max(worldScale.x, 1e-4));
                float3 posWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(posWS);
                o.posOS = v.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
                float flash = UNITY_ACCESS_INSTANCED_PROP(Props, _Flash);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float facing = saturate(dot(n, v));
                float fresnel = pow(1.0 - facing, _RimPower);
                // Facets catch a little of the rim color so the silhouette has form, not a flat hole.
                float facet = 0.05 * saturate(n.y * 0.5 + 0.5);
                float energy = UNITY_ACCESS_INSTANCED_PROP(Props, _Energy);
                // Keep the magenta identity; the intent only colours the edge, and more so as it winds up.
                float3 rimColor = lerp(_RimColor.rgb, tint.rgb, 0.35 + 0.4 * energy);
                float3 rgb = _InkColor.rgb + rimColor * (fresnel * 1.3 + facet);

                // The void inside: a star field seen through the ink, strongest face-on (it sits "behind" the rim).
                // Pure view direction (no facet normal): a window into space, seamless across the facets.
                float t = _Time.y;
                float3 dirOS = normalize(mul((float3x3)UNITY_MATRIX_I_M, -v));
                float stars = Stars(dirOS, 9.0, t) + Stars(dirOS + 0.37, 15.0, t) * 0.7 + Stars(dirOS - 0.61, 24.0, t) * 0.45;
                rgb += float3(0.85, 0.8, 1.0) * stars * facing * 1.3;

                // Rings of light rising through the body; they quicken and brighten as it winds up.
                float band = frac(i.posOS.y * 3.2 - t * (0.35 + 1.4 * energy));
                band = pow(1.0 - abs(band * 2.0 - 1.0), 10.0);
                rgb += tint.rgb * band * (0.18 + 0.9 * energy) * (0.3 + 0.7 * facing);
                rgb = lerp(rgb, float3(1.0, 0.9, 1.0) * 1.4, flash);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
