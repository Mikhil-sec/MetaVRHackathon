// SoftParticle: glowing particles with no texture (docs/TECH_GUIDE.md section 4). A soft elliptical falloff with a
// hot core over the particle quad, so stretched billboards read as streaks of light with rounded, fading ends, and
// plain billboards as soft glints. Additive (alpha 0 over passthrough); colour comes from the particle vertex colour.
// _Shape 1 draws flakes instead (Fever's confetti of light): a crisp rounded card with a hot rim and a faint halo.
Shader "Ricochet/SoftParticle"
{
    Properties
    {
        _Intensity ("Intensity", Float) = 1.6
        _Core ("Hot core", Range(0, 2)) = 0.8
        _Shape ("Shape (0 glow, 1 flake)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SoftParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Core;
                float _Shape;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                if (_Shape > 0.5) // uniform branch: one material per mode
                {
                    // A flake: a rounded card (superellipse), crisp at any size, brightest along its rim like a
                    // foil catching light; the particle's width flips it, so it twinkles as it tumbles.
                    float2 q = abs(i.uv - 0.5) * 2.0;
                    float r = sqrt(sqrt(q.x * q.x * q.x * q.x + q.y * q.y * q.y * q.y));
                    float aa = fwidth(r) * 1.2 + 1e-4;
                    float card = 1.0 - smoothstep(0.72 - aa, 0.72 + aa, r);
                    float rim = card * smoothstep(0.35, 0.72, r);
                    float halo = pow(saturate(1.0 - r), 2.0) * 0.3;
                    half3 flake = i.color.rgb * (card * 0.55 + rim * 0.6 + halo) + (card * _Core * 0.25);
                    return half4(flake * i.color.a * _Intensity, 0.0);
                }
                float d = length((i.uv - 0.5) * 2.0);          // 0 centre .. 1 at the quad's inscribed edge
                float glow = pow(saturate(1.0 - d), 1.6);
                float core = pow(saturate(1.0 - d * 1.8), 3.0) * _Core;
                half3 rgb = i.color.rgb * glow + core;          // the core burns toward white
                return half4(rgb * i.color.a * _Intensity, 0.0);
            }
            ENDHLSL
        }
    }
}
