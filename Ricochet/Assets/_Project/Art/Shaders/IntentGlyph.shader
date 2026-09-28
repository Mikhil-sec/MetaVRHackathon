// IntentGlyph: the creature's next move as an icon, not a word (CONCEPT section 4, zero text). One quad, the shape is
// a signed distance field computed here, so it is crisp at any distance with no texture: 0 = attack (three claw
// slashes), 1 = guard (a shield), 2 = hex (an eye). A thin bright outline, a faint fill and a soft glow; additive
// over passthrough. _Kind, _Color and _Intensity arrive via MaterialPropertyBlock.
Shader "Ricochet/IntentGlyph"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.35, 0.22, 1)
        _Intensity ("Intensity", Float) = 1.4
        _Kind ("Kind (0 attack, 1 guard, 2 hex)", Float) = 0
        _Stroke ("Stroke", Range(0.01, 0.2)) = 0.075
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "IntentGlyph"
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
                float _Kind;
                float _Stroke;
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

            float Segment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }

            // Three curved claw slashes, tapering at both ends: a filled shape (distance to a thickened stroke).
            float Claw(float2 p)
            {
                float d = 1e5;
                for (int k = -1; k <= 1; k++)
                {
                    float2 q = p - float2(k * 0.34, 0.0);
                    q.x += 0.12 * q.y * q.y;                      // a slight curve
                    float2 a = float2(0.2, 0.72), b = float2(-0.2, -0.72);
                    float2 pa = q - a, ba = b - a;
                    float h = saturate(dot(pa, ba) / dot(ba, ba));
                    float w = 0.13 * sin(h * 3.14159) + 0.015;    // fat in the middle, sharp at the ends
                    d = min(d, length(pa - ba * h) - w);
                }
                return d;
            }

            // A heater shield: two big circles meeting at a point below, cut flat on top.
            float Shield(float2 p)
            {
                float r = 1.25;
                float d = max(length(p - float2(0.62, 0.5)) - r, length(p - float2(-0.62, 0.5)) - r);
                return max(d, p.y - 0.72);
            }

            // An almond eye (two circles' lens) with a round pupil.
            float EyeOutline(float2 p)
            {
                return max(length(p - float2(0.0, -0.62)) - 1.0, length(p - float2(0.0, 0.62)) - 1.0);
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;                   // -1..1
                float kind = _Kind;
                float fill, line_;
                float aa;
                if (kind < 0.5)
                {
                    float d = Claw(p);
                    aa = fwidth(d) + 1e-4;
                    fill = smoothstep(aa, -aa, d);
                    line_ = fill;                                 // the claw is a solid mark
                    fill = 0.0;
                }
                else if (kind < 1.5)
                {
                    float d = Shield(p);
                    aa = fwidth(d) + 1e-4;
                    line_ = smoothstep(aa, -aa, abs(d) - _Stroke);
                    fill = smoothstep(aa, -aa, d) * 0.22;
                    // A central ridge makes it read as a shield, not a badge.
                    float ridge = Segment(p, float2(0.0, 0.55), float2(0.0, -0.55));
                    line_ = max(line_, smoothstep(aa, -aa, ridge - _Stroke * 0.6) * step(d, 0.0));
                }
                else
                {
                    float d = EyeOutline(p);
                    aa = fwidth(d) + 1e-4;
                    line_ = smoothstep(aa, -aa, abs(d) - _Stroke);
                    float pupil = length(p) - 0.2;
                    line_ = max(line_, smoothstep(aa, -aa, pupil));
                    fill = smoothstep(aa, -aa, d) * 0.18;
                }
                // A soft radial haze that is fully gone before the quad edge (no visible box).
                float haze = exp(-length(p) * 3.5) * 0.12 * saturate(1.0 - length(p));
                return half4(_Color.rgb * (line_ + fill + haze) * _Intensity, 0.0);
            }
            ENDHLSL
        }
    }
}
