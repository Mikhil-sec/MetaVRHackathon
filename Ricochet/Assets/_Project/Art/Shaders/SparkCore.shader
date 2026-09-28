// SparkCore: the Spark's body (docs/TECH_GUIDE.md section 4). A white-hot centre that falls off to a coloured fresnel
// rim, with a slow procedural plasma swirl so it reads as contained energy rather than a flat disc. Opaque and unlit,
// a few ALU ops, no texture. _Heat (0..1) shifts the rim from cyan toward gold with the chain (set by the Spark).
Shader "Ricochet/SparkCore"
{
    Properties
    {
        _CoreColor ("Core", Color) = (1, 1, 1, 1)
        _RimColor ("Rim (cold)", Color) = (0.25, 0.8, 1, 1)
        _HotRimColor ("Rim (hot)", Color) = (1, 0.65, 0.2, 1)
        _RimPower ("Rim Power", Range(0.5, 6)) = 1.6
        _Swirl ("Plasma swirl", Range(0, 1)) = 0.35
        _Intensity ("Intensity", Float) = 1.25
        _Heat ("Heat", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SparkCore"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _RimColor;
                float4 _HotRimColor;
                float _RimPower;
                float _Swirl;
                float _Intensity;
                float _Heat;
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
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(positionWS);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float ndv = saturate(dot(normalize(i.normalWS), normalize(i.viewWS)));
                float rim = pow(1.0 - ndv, _RimPower);

                // Two drifting sine fields in object space: cheap "plasma" banding that crawls over the surface.
                float t = _Time.y;
                float3 p = i.positionOS * 9.0;
                float swirl = sin(p.x * 1.7 + p.y * 1.1 + t * 3.1) * sin(p.z * 1.9 - p.x * 0.8 + t * 2.3);
                swirl = swirl * 0.5 + 0.5;

                float3 rimColor = lerp(_RimColor.rgb, _HotRimColor.rgb, _Heat);
                float core = pow(ndv, 2.5);
                float3 rgb = lerp(rimColor, _CoreColor.rgb, core);
                rgb += rimColor * rim * 0.6;
                rgb *= 1.0 - _Swirl * (1.0 - core) * (1.0 - swirl) * 0.8;
                return half4(rgb * _Intensity, 1.0);
            }
            ENDHLSL
        }
    }
}
