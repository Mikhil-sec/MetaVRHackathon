// HandLight: the player's hand mesh (docs/TECH_GUIDE.md section 6).
// One pass: it writes depth, so a hand in front of a crystal or the Spark hides it as a real hand would, and draws a
// hand of light over passthrough: a cool fresnel rim and a faint glassy fill, premultiplied.
// On device the real hands show through passthrough, so HandLook sets _ColorMask to 0 there (occlusion only);
// in the Editor (simulator, trailer renders) the light hand stands in for the real one.
// It fades out and clips within ~15-25 cm of the eye: a hand that close is a near-plane smear, not a hand.
Shader "Ricochet/HandLight"
{
    Properties
    {
        _RimColor ("Rim Color", Color) = (0.55, 0.9, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.6
        _RimIntensity ("Rim Intensity", Float) = 1.1
        _FillColor ("Fill Color", Color) = (0.18, 0.35, 0.5, 1)
        _FillAlpha ("Fill Alpha", Range(0, 1)) = 0.14
        _NearFade ("Near Fade (start, end m)", Vector) = (0.14, 0.26, 0, 0)
        [Enum(None,0,RGBA,15)] _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _RimColor;
            float _RimPower;
            float _RimIntensity;
            float4 _FillColor;
            float _FillAlpha;
            float4 _NearFade;
            float _ColorMask;
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
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes v)
        {
            Varyings o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            return o;
        }

        float Near(float3 positionWS)
        {
            float d = distance(positionWS, GetCameraPositionWS());
            return saturate((d - _NearFade.x) / max(1e-3, _NearFade.y - _NearFade.x));
        }
        ENDHLSL

        Pass
        {
            Name "HandLight"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            ColorMask [_ColorMask]
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragLight
            half4 FragLight(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float fresnel = pow(1.0 - saturate(dot(n, v)), _RimPower);
                float near = Near(i.positionWS);
                clip(near - 0.02); // no depth either when this close: nothing is hidden behind a smear
                float a = _FillAlpha * near;
                float3 rgb = (_FillColor.rgb * _FillAlpha + _RimColor.rgb * fresnel * _RimIntensity) * near;
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
