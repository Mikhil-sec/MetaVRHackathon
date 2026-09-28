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
            UNITY_INSTANCING_BUFFER_END(Props)

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
                float facet = 0.12 * saturate(n.y * 0.5 + 0.5);
                float3 rimColor = lerp(_RimColor.rgb, tint.rgb, 0.6);
                float3 rgb = _InkColor.rgb + rimColor * (fresnel * 1.6 + facet);
                rgb = lerp(rgb, float3(1.0, 0.9, 1.0) * 1.4, flash);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
