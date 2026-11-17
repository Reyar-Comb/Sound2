Shader "Sound2/Boss Memory Preview"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0, 0, 1)
        _Width ("Line width", Range(0.5, 2)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.0
            #pragma vertex Vert
            #pragma geometry Geo
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Vertex { float4 position : SV_POSITION; };
            struct Fragment
            {
                float4 position : SV_POSITION;
                noperspective float3 barycentric : TEXCOORD0;
            };

            Vertex Vert(float3 position : POSITION)
            {
                Vertex output;
                output.position = TransformObjectToHClip(position);
                return output;
            }

            [maxvertexcount(3)]
            void Geo(triangle Vertex input[3], inout TriangleStream<Fragment> stream)
            {
                for (int i = 0; i < 3; i++)
                {
                    Fragment output;
                    output.position = input[i].position;
                    output.barycentric = float3(i == 0, i == 1, i == 2);
                    stream.Append(output);
                }
            }

            half4 Frag(Fragment input) : SV_Target
            {
                float3 edge = smoothstep(0, fwidth(input.barycentric) * _Width, input.barycentric);
                half alpha = 1 - min(edge.x, min(edge.y, edge.z));
                return half4(_Color.rgb, _Color.a * alpha);
            }
            ENDHLSL
        }
    }
}
