// Streak: a comet tail of light (docs/TECH_GUIDE.md section 4, fake glow). An axial billboard: the quad's object X
// axis is the direction of travel and its X scale the tail length, and the quad turns about that axis to face the
// eye, so the streak reads from any angle. The object origin is the head; the tail tapers and fades behind it.
// Purely additive (alpha stays 0, so over passthrough it only adds light). Paired with a Halo billboard at the head.
Shader "Ricochet/Streak"
{
    Properties
    {
        _Color ("Color", Color) = (0.62, 0.45, 1, 1)
        _Intensity ("Intensity", Float) = 1.5
        _Core ("Hot Core", Range(0, 2)) = 0.9
        _Taper ("Tail Width", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Streak"
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
                float _Core;
                float _Taper;
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
                float3 headWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 axisM = float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x);
                float len = length(axisM);
                float3 axis = axisM / max(len, 1e-5);
                float width = length(float3(UNITY_MATRIX_M[0].y, UNITY_MATRIX_M[1].y, UNITY_MATRIX_M[2].y));
                // Side vector perpendicular to the travel and to the view: the quad faces this eye about its axis.
                float3 toEye = GetCameraPositionWS() - headWS;
                float3 side = cross(axis, toEye);
                float s = length(side);
                side = s > 1e-5 ? side / s : float3(0, 1, 0);
                float u = v.positionOS.x + 0.5; // 0 at the tail end, 1 at the head
                float3 p = headWS - axis * ((1.0 - u) * len) + side * (v.positionOS.y * width * lerp(_Taper, 1.0, u));
                o.positionCS = TransformWorldToHClip(p);
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float u = i.uv.x;
                float w = (i.uv.y - 0.5) * 2.0;
                // A soft glow with a thin white-hot core, brightest at the head, fading to nothing at the tail.
                float across = exp(-w * w * 5.0) * 0.55 + exp(-w * w * 45.0) * _Core;
                float along = u * u * (3.0 - 2.0 * u);
                return half4(_Color.rgb * (across * along * _Intensity), 0.0);
            }
            ENDHLSL
        }
    }
}
