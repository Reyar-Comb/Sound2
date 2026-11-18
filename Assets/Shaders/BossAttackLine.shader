Shader "Sound2/Boss Attack Line"
{
    Properties
    {
        _PointColumns ("Point Columns", Float) = 100
        _PointRows ("Point Rows", Float) = 5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PointColumns;
                float _PointRows;
            CBUFFER_END

            struct Attributes
            {
                float4 position : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = TransformObjectToHClip(input.position.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 grid = input.uv * max(float2(_PointColumns, _PointRows), 1.0);
                float2 cell = floor(grid);
                // Stable slight scatter keeps the dense dots from resembling a solid strip.
                float2 jitter = frac(sin(float2(dot(cell, float2(127.1, 311.7)),
                    dot(cell, float2(269.5, 183.3)))) * 43758.5453);
                float distanceToPoint = length(frac(grid) - .5 - (jitter - .5) * .18);
                float edge = clamp(fwidth(distanceToPoint), .01, .12);
                half coverage = 1 - smoothstep(.32 - edge, .32 + edge, distanceToPoint);
                clip(coverage - .01);
                return half4(input.color.rgb, input.color.a * coverage);
            }
            ENDHLSL
        }
    }
}
