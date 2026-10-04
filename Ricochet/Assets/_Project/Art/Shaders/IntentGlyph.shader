// IntentGlyph: the creature's next move as an icon, not a word (CONCEPT section 4, zero text). One quad, the shape is
// a signed distance field computed here, so it is crisp at any distance with no texture: 0 = attack (three claw
// slashes), 1 = guard (a shield), 2 = hex (an eye). The run's rewards and progress use the same quad: 3 splitter,
// 4 bomb spark, 5 ghost, 6 magnet, 7 heavy, 8 relic (a cut gem), 9 mend (a cross), 10 crown (the boss), 11 pip ring,
// 12 pip disc. A thin bright outline, a faint fill and a soft glow; additive over passthrough. _Kind, _Color and _Intensity arrive via MaterialPropertyBlock.
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

            float Box(float2 p, float2 b)
            {
                float2 d = abs(p) - b;
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            // Two barbs closing the segment a->b at b.
            float Arrowhead(float2 p, float2 a, float2 b)
            {
                float2 dir = normalize(b - a), n = float2(-dir.y, dir.x);
                return min(Segment(p, b, b - dir * 0.26 + n * 0.17), Segment(p, b, b - dir * 0.26 - n * 0.17));
            }

            // A four-point sparkle: two crossed thin diamonds (approximate distance, fine for a solid mark).
            float Star4(float2 p, float s)
            {
                float2 q = abs(p);
                float t = s * 0.28;
                return min((q.x / t + q.y / s - 1.0) * t, (q.y / t + q.x / s - 1.0) * t);
            }

            // Exact rhombus distance (Inigo Quilez), half-extents b.
            float Rhombus(float2 p, float2 b)
            {
                p = abs(p);
                float2 pb = b - 2.0 * p;
                float h = clamp((pb.x * b.x - pb.y * b.y) / dot(b, b), -1.0, 1.0);
                float d = length(p - 0.5 * b * float2(1.0 - h, 1.0 + h));
                return d * sign(p.x * b.y + p.y * b.x - b.x * b.y);
            }

            // Distance to a circular arc of radius r, centered on direction ang, spanning +/-halfAperture (radians).
            float ArcAt(float2 p, float ang, float halfAperture, float r)
            {
                float s = sin(1.5708 - ang), c = cos(1.5708 - ang);
                float2 q = float2(c * p.x - s * p.y, s * p.x + c * p.y);   // ang now points along +y
                q.x = abs(q.x);
                float2 sc = float2(sin(halfAperture), cos(halfAperture));
                return (sc.y * q.x > sc.x * q.y) ? length(q - sc * r) : abs(length(q) - r);
            }

            // The reward and progress glyphs (kind 3..22): a stroke distance (line), solid marks, and a faint interior.
            void RewardGlyph(float2 p, float kind, out float line_, out float fill)
            {
                float d = 1e5, inside = 1e5, solid = 1e5;
                float w = _Stroke;
                if (kind < 3.5)
                {
                    // Splitter: one stem forking into three arrows.
                    float2 o = float2(0.0, -0.12);
                    d = Segment(p, float2(0.0, -0.82), o);
                    d = min(d, Segment(p, o, float2(0.0, 0.78)));
                    d = min(d, Segment(p, o, float2(-0.62, 0.55)));
                    d = min(d, Segment(p, o, float2(0.62, 0.55)));
                    d = min(d, Segment(p, float2(0.0, 0.8), float2(-0.19, 0.58)));
                    d = min(d, Segment(p, float2(0.0, 0.8), float2(0.19, 0.58)));
                    d = min(d, Segment(p, float2(-0.64, 0.57), float2(-0.36, 0.58)));
                    d = min(d, Segment(p, float2(-0.64, 0.57), float2(-0.62, 0.29)));
                    d = min(d, Segment(p, float2(0.64, 0.57), float2(0.36, 0.58)));
                    d = min(d, Segment(p, float2(0.64, 0.57), float2(0.62, 0.29)));
                }
                else if (kind < 4.5)
                {
                    // Bomb spark: a round bomb, a fuse, and a four-point spark at its tip.
                    inside = length(p - float2(-0.1, -0.18)) - 0.55;
                    d = abs(inside);
                    d = min(d, Segment(p, float2(0.26, 0.24), float2(0.46, 0.5)));
                    float2 t = p - float2(0.6, 0.68);
                    solid = min(Segment(t, float2(-0.2, 0.0), float2(0.2, 0.0)), Segment(t, float2(0.0, -0.2), float2(0.0, 0.2))) - w * 0.6;
                }
                else if (kind < 5.5)
                {
                    // Ghost: a dome with a wavy hem and two eyes.
                    float2 q = p - float2(0.0, 0.02);
                    float body = min(length(q - float2(0.0, 0.15)) - 0.55, Box(q - float2(0.0, -0.2), float2(0.55, 0.36)));
                    float hem = -0.52 + 0.1 * cos(q.x * 11.0);
                    inside = max(body, hem - q.y);
                    d = abs(inside);
                    solid = min(length(q - float2(-0.2, 0.18)), length(q - float2(0.2, 0.18))) - 0.11;
                }
                else if (kind < 6.5)
                {
                    // Magnet: a chunky horseshoe with bright poles.
                    float2 c = float2(0.0, -0.12);
                    float r = 0.44;
                    float arc = p.y < c.y ? abs(length(p - c) - r) : 1e5;
                    float legs = Segment(float2(abs(p.x), p.y), float2(r, c.y), float2(r, 0.72));
                    float shoe = min(arc, legs) - w * 2.0;
                    inside = shoe;
                    d = abs(shoe);
                    solid = max(shoe, 0.46 - p.y);
                }
                else if (kind < 7.5)
                {
                    // Heavy: a solid ball with speed lines trailing behind it.
                    solid = length(p - float2(0.22, 0.0)) - 0.46;
                    d = Segment(p, float2(-0.9, 0.0), float2(-0.38, 0.0));
                    d = min(d, Segment(p, float2(-0.75, 0.3), float2(-0.32, 0.3)));
                    d = min(d, Segment(p, float2(-0.75, -0.3), float2(-0.32, -0.3)));
                }
                else if (kind < 8.5)
                {
                    // Relic: a cut gem (table, crown facets, pavilion to a point).
                    float2 tl = float2(-0.3, 0.6), tr = float2(0.3, 0.6), gl = float2(-0.62, 0.26), gr = float2(0.62, 0.26), b = float2(0.0, -0.8);
                    d = min(Segment(p, tl, tr), Segment(p, tr, gr));
                    d = min(d, min(Segment(p, gr, b), Segment(p, b, gl)));
                    d = min(d, min(Segment(p, gl, tl), Segment(p, gl, gr)));
                    d = min(d, min(Segment(p, float2(-0.2, 0.26), b), Segment(p, float2(0.2, 0.26), b)));
                    d = min(d, min(Segment(p, tl, float2(-0.2, 0.26)), Segment(p, tr, float2(0.2, 0.26))));
                    // Interior: the outline as half-planes (mirror-symmetric, so fold x).
                    float2 q = float2(abs(p.x), p.y);
                    inside = max(p.y - 0.6, max(dot(q - gr, normalize(float2(1.06, -0.62))), dot(q - tr, normalize(float2(0.34, 0.32)))));
                }
                else if (kind < 9.5)
                {
                    // Mend: a soft cross.
                    inside = min(Box(p, float2(0.17, 0.62)), Box(p, float2(0.62, 0.17))) - 0.05;
                    d = abs(inside);
                }
                else if (kind < 10.5)
                {
                    // Crown: the boss's mark.
                    float2 a = float2(-0.62, -0.42), e = float2(0.62, -0.42);
                    d = Segment(p, a, e);
                    d = min(d, Segment(p, a, float2(-0.72, 0.42)));
                    d = min(d, Segment(p, float2(-0.72, 0.42), float2(-0.32, 0.02)));
                    d = min(d, Segment(p, float2(-0.32, 0.02), float2(0.0, 0.6)));
                    d = min(d, Segment(p, float2(0.0, 0.6), float2(0.32, 0.02)));
                    d = min(d, Segment(p, float2(0.32, 0.02), float2(0.72, 0.42)));
                    d = min(d, Segment(p, float2(0.72, 0.42), e));
                    solid = min(min(length(p - float2(-0.72, 0.5)), length(p - float2(0.72, 0.5))), length(p - float2(0.0, 0.7))) - 0.1;
                    inside = max(p.y - 0.3, max(abs(p.x) - 0.6, -0.42 - p.y));
                }
                else if (kind < 11.5)
                {
                    d = abs(length(p) - 0.55);            // pip ring: an encounter still ahead
                    w *= 1.4;
                }
                else if (kind < 12.5)
                {
                    solid = length(p) - 0.62;               // pip disc: an encounter sealed
                }
                // Relics (kind 13..22): each mark draws its rule.
                else if (kind < 13.5)
                {
                    // Carom: a bank shot zig-zagging between two walls, ending in an arrow.
                    float2 a = float2(-0.78, -0.5), b = float2(-0.3, 0.6), c = float2(0.2, -0.38), e = float2(0.7, 0.55);
                    d = min(Segment(p, a, b), min(Segment(p, b, c), Segment(p, c, e)));
                    d = min(d, Arrowhead(p, c, e));
                    solid = min(Box(p - float2(-0.3, 0.74), float2(0.28, 0.045)), Box(p - float2(0.2, -0.52), float2(0.28, 0.045)));
                    solid = min(solid, length(p - a) - 0.1);
                }
                else if (kind < 14.5)
                {
                    // Skylight: off the ceiling and down into a critical star.
                    // A steep climb, a long glancing fall with an arrow, so it reads as a bounce, not a letter.
                    float2 a = float2(-0.8, -0.72), b = float2(-0.42, 0.6), c = float2(0.3, -0.16);
                    d = min(Segment(p, a, b), Segment(p, b, c));
                    d = min(d, Arrowhead(p, b, c));
                    solid = min(Box(p - float2(0.0, 0.74), float2(0.8, 0.05)), Star4(p - float2(0.6, -0.5), 0.36));
                }
                else if (kind < 15.5)
                {
                    // First Light: a sun rising over the horizon.
                    float2 c = float2(0.0, -0.3);
                    float up = c.y - p.y;                    // < 0 above the horizon
                    float disc = length(p - c) - 0.4;
                    inside = max(disc, up);
                    d = max(abs(disc), up);
                    d = min(d, Segment(p, float2(-0.88, -0.3), float2(0.88, -0.3)));
                    for (int k = 0; k < 5; k++)
                    {
                        float ang = 0.5236 * (k + 1);       // 30..150 degrees
                        float2 r = float2(cos(ang), sin(ang));
                        d = min(d, Segment(p, c + r * 0.56, c + r * 0.84));
                    }
                }
                else if (kind < 16.5)
                {
                    // Gold Rush: three cut gems.
                    float2 g = float2(0.25, 0.36);
                    inside = min(Rhombus(p - float2(0.0, 0.36), g), min(Rhombus(p - float2(-0.46, -0.32), g), Rhombus(p - float2(0.46, -0.32), g)));
                    d = abs(inside);
                }
                else if (kind < 17.5)
                {
                    // Aegis: a shield with a plus (more shield).
                    inside = Shield(p);
                    d = abs(inside);
                    solid = min(Box(p - float2(0.0, 0.08), float2(0.065, 0.3)), Box(p - float2(0.0, 0.08), float2(0.3, 0.065)));
                }
                else if (kind < 18.5)
                {
                    // Thornlight: a ring grown with thorns (hit it and it hits back).
                    d = abs(length(p) - 0.38);
                    for (int k = 0; k < 6; k++)
                    {
                        float ang = 1.0472 * k + 0.5236;
                        float2 r = float2(cos(ang), sin(ang));
                        float2 a = r * 0.38, b = r * 0.9;
                        float2 pa = p - a, ba = b - a;
                        float h = saturate(dot(pa, ba) / dot(ba, ba));
                        solid = min(solid, length(pa - ba * h) - 0.12 * (1.0 - h));
                    }
                }
                else if (kind < 19.5)
                {
                    // Big Bang: a bomb inside two broken shock rings.
                    inside = length(p) - 0.27;
                    solid = length(p) - 0.12;
                    d = abs(inside);
                    for (int k = 0; k < 4; k++)
                    {
                        float ang = 1.5708 * k;
                        d = min(d, ArcAt(p, ang + 0.7854, 0.5, 0.56));
                        d = min(d, ArcAt(p, ang, 0.42, 0.84));
                    }
                }
                else if (kind < 20.5)
                {
                    // Third Eye: an eye with a third, cut gem above it.
                    float2 q = (p - float2(0.0, -0.18)) * 1.08;
                    inside = EyeOutline(q);
                    d = abs(inside);
                    solid = min(length(q) - 0.2, Rhombus(p - float2(0.0, 0.6), float2(0.15, 0.22)));
                }
                else if (kind < 21.5)
                {
                    // Second Wind: a fall that bounces off the floor instead of ending.
                    float2 a = float2(-0.72, 0.6), b = float2(-0.1, -0.46), c = float2(0.2, -0.06), e = float2(0.42, 0.22), f = float2(0.72, 0.42);
                    d = min(Segment(p, a, b), min(Segment(p, b, c), min(Segment(p, c, e), Segment(p, e, f))));
                    d = min(d, Arrowhead(p, e, f));
                    solid = Box(p - float2(0.0, -0.66), float2(0.8, 0.05));
                }
                else if (kind < 22.5)
                {
                    // Resonance: a source ringing out in three waves.
                    float2 c = float2(-0.48, 0.0);
                    solid = length(p - c) - 0.15;
                    d = min(ArcAt(p - c, 0.0, 0.8, 0.38), min(ArcAt(p - c, 0.0, 0.75, 0.68), ArcAt(p - c, 0.0, 0.7, 0.98)));
                }
                else
                {
                    // Ascension: a bold chevron pointing up (one per tier).
                    d = min(Segment(p, float2(-0.62, -0.3), float2(0.0, 0.32)), Segment(p, float2(0.0, 0.32), float2(0.62, -0.3)));
                    w *= 1.8;
                }
                float aa = fwidth(d) + 1e-4;
                float aaS = fwidth(solid) + 1e-4;
                float aaI = fwidth(inside) + 1e-4;
                line_ = max(smoothstep(aa, -aa, d - w), smoothstep(aaS, -aaS, solid));
                fill = smoothstep(aaI, -aaI, inside) * 0.22;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * 2.0;                   // -1..1
                float kind = _Kind;
                float fill, line_;
                float aa;
                if (kind > 2.5)
                {
                    RewardGlyph(p, kind, line_, fill);
                }
                else if (kind < 0.5)
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
