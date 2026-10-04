// Halo: fake bloom (docs/TECH_GUIDE.md section 4). A camera-facing quad with a procedural radial falloff,
// purely additive (alpha stays 0, so over passthrough it only adds light). No texture, no post-processing.
// Billboarding happens in the vertex shader, so there is no per-frame script cost.
// Optional (all 0 by default, so plain halos are unchanged): _Star draws a four-point star glint, _Twinkle makes
// it flash now and then, _Breathe slowly swells the glow. Each object gets its own phase from its world position.
Shader "Ricochet/Halo"
{
    Properties
    {
        _Color ("Color", Color) = (0.35, 0.85, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Falloff ("Falloff", Range(0.5, 6)) = 2.2
        _Star ("Star Glint", Range(0, 1)) = 0
        _Twinkle ("Twinkle", Range(0, 1)) = 0
        _Breathe ("Breathe", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Halo"
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
                float _Falloff;
                float _Star;
                float _Twinkle;
                float _Breathe;
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
                float level : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // Object origin in view space, then offset the corner in view space: always faces the eye.
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 centerVS = TransformWorldToView(originWS);
                float size = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                // A non-uniform quad stretches the halo across the view (a visor, a mouth); uniform ones are unchanged.
                float stretchY = length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y)) / max(size, 1e-5);
                // Per-object animation (constant across the quad, so it costs four vertices, not pixels).
                float phase = frac(dot(originWS, float3(3.1, 7.7, 5.3)));
                float flash = pow(saturate(sin(_Time.y * 1.7 + phase * 40.0)), 16.0);
                float twinkle = lerp(1.0, 0.08 + flash, _Twinkle);
                float breathe = 1.0 + _Breathe * 0.35 * sin(_Time.y * 1.3 + phase * 6.28);
                o.level = twinkle * breathe;
                size *= lerp(1.0, 0.45 + 0.55 * saturate(flash * 1.5), _Twinkle); // a flash also grows the star
                float3 cornerVS = centerVS + float3(v.positionOS.x * size, v.positionOS.y * size * stretchY, 0);
                o.positionCS = TransformWViewToHClip(cornerVS);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;
                float d = length(p);
                float a = pow(saturate(1.0 - d), _Falloff);
                if (_Star > 0.0)
                {
                    // Four thin rays that taper to the quad's edge, over a small hot core.
                    float2 q = abs(p);
                    float rays = exp(-q.y * 30.0) * saturate(1.0 - q.x) + exp(-q.x * 30.0) * saturate(1.0 - q.y);
                    a = lerp(a, a * 0.5 + rays * rays * 1.4 + pow(saturate(1.0 - d * 3.0), 2.0), _Star);
                }
                return half4(_Color.rgb * (a * _Intensity * i.level), 0.0);
            }
            ENDHLSL
        }
    }
}
