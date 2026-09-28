// Rift: a crack of light in a real wall (CONCEPT sections 3-4). Purely additive over passthrough.
// The jagged outline comes from RiftMesh; uv.x runs across the crack (0..1), uv.y along it.
// A white-hot seam, a magenta body, and a restless flicker that travels along the crack. _Open (0..1) fades it.
Shader "Ricochet/Rift"
{
    Properties
    {
        _Color ("Glow Color", Color) = (1, 0.25, 0.8, 1)
        _Intensity ("Intensity", Float) = 1.6
        _Open ("Open", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Rift"
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
                float _Open;
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float across = 1.0 - abs(i.uv.x * 2.0 - 1.0);           // 1 on the seam, 0 at the jagged edge
                float body = across * across;
                float seam = pow(across, 10.0);
                // Energy crawls along the crack: two sliding waves plus fast shimmer.
                float t = _Time.y;
                float crawl = 0.75 + 0.25 * sin(i.uv.y * 23.0 - t * 5.0) * sin(i.uv.y * 9.0 + t * 3.1);
                float shimmer = 0.9 + 0.1 * sin(t * 37.0 + i.uv.y * 61.0);
                float ends = smoothstep(0.0, 0.12, i.uv.y) * smoothstep(1.0, 0.88, i.uv.y);
                float3 rgb = (_Color.rgb * body * crawl + seam * 1.3) * shimmer * ends * _Intensity * _Open;
                return half4(rgb, 0.0);
            }
            ENDHLSL
        }
    }
}
