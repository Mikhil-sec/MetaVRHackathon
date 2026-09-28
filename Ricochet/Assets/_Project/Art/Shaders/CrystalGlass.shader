// CrystalGlass: "premium glass" at unlit cost (docs/TECH_GUIDE.md section 4).
// Faceted shading from the flat-shaded mesh, a fresnel rim, and a core that brightens toward the tip.
// _Glow (0..1) drives the lit state: the crystal burns hotter and whiter. Per-instance color and glow
// come from a MaterialPropertyBlock through the instancing buffer.
Shader "Ricochet/CrystalGlass"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.55, 0.35, 1, 1)
        _Glow ("Glow", Range(0, 1)) = 0
        _Scale ("Visual Scale", Float) = 1
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _RimColor ("Rim Color", Color) = (0.85, 0.8, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "CrystalGlass"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _RimPower;
                float4 _RimColor;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _Glow)
                UNITY_DEFINE_INSTANCED_PROP(float, _Scale) // visual-only scale (pop-in, hit punch); colliders stay put
                UNITY_DEFINE_INSTANCED_PROP(float, _Kind)  // 0 normal, 1 gold, 2 prism, 3 bomb, 4 amp (CrystalKind)
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
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float height : TEXCOORD2; // 0 at the base, 1 at the tip
                float phase : TEXCOORD3;  // per-crystal offset so specials never pulse in lockstep
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float scale = UNITY_ACCESS_INSTANCED_PROP(Props, _Scale);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz * scale);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.height = saturate((v.positionOS.y + 0.45) / 1.55); // CrystalMesh spans y -0.45..1.1
                float3 origin = UNITY_MATRIX_M._m03_m13_m23;
                o.phase = frac(dot(origin, float3(3.1, 7.7, 5.3)));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 baseColor = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                float glow = UNITY_ACCESS_INSTANCED_PROP(Props, _Glow);

                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));

                // Facets: a fixed key light from above-front keeps each face a distinct tone.
                float facet = 0.45 + 0.55 * saturate(dot(n, normalize(float3(0.2, 0.9, -0.35))));
                // View-dependent "refraction": faces turned toward the viewer look deeper and darker.
                float depth = saturate(dot(n, v));
                float fresnel = pow(1.0 - depth, _RimPower);

                float3 core = baseColor.rgb * facet * lerp(0.55, 1.15, i.height) * (1.0 - 0.35 * depth);
                float3 rim = lerp(_RimColor.rgb, 1.0.xxx, glow) * fresnel * (0.9 + 1.6 * glow);
                // Lit: the body burns toward a hot white-gold.
                float3 hot = lerp(core, baseColor.rgb * 1.6 + 0.35, glow);
                float3 rgb = hot + rim;

                // Specials: each type has one signature motion so it reads at a glance from the seat.
                float kind = UNITY_ACCESS_INSTANCED_PROP(Props, _Kind);
                float t = _Time.y + i.phase * 10.0;
                if (kind > 0.5 && kind < 1.5)
                {
                    // Gold: bright glints sweeping up the facets, plus a warm inner burn.
                    float sweep = pow(saturate(sin(i.height * 6.0 - t * 2.2)), 24.0);
                    rgb *= float3(1.05, 0.82, 0.5);   // warm the lavender rim so it reads as metal-gold, not cream
                    rgb += float3(1.0, 0.85, 0.5) * (sweep * 1.6 * facet + 0.15);
                }
                else if (kind > 1.5 && kind < 2.5)
                {
                    // Prism: iridescence. Hue shifts with the view angle and height, and slowly drifts.
                    float h = frac(depth * 1.3 + i.height * 0.6 + t * 0.08);
                    float3 hue = saturate(abs(frac(h + float3(0.0, 0.667, 0.333)) * 6.0 - 3.0) - 1.0);
                    rgb = lerp(rgb, hue * (0.6 + 0.8 * facet) + fresnel * 0.8, 0.7 * (1.0 - glow * 0.5));
                }
                else if (kind > 2.5 && kind < 3.5)
                {
                    // Bomb: a fuse. The core throbs hot like a heartbeat.
                    float beat = pow(0.5 + 0.5 * sin(t * 5.0), 6.0);
                    rgb += float3(1.0, 0.45, 0.15) * beat * (1.0 - i.height * 0.5) * 1.2;
                }
                else if (kind > 3.5)
                {
                    // Amp: energy coils climbing the crystal.
                    float coil = pow(abs(sin(i.height * 16.0 - t * 4.0)), 10.0);
                    rgb += float3(0.4, 1.0, 0.9) * coil * 1.1;
                }
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
}
