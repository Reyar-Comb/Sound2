Shader "Sound2/Elevator White Ink"
{
    Properties { _BaseColor("Paper", Color)=(1,1,1,1) _LineWidth("Ink width", Range(0.001,0.1))=0.012 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; float _LineWidth;
            CBUFFER_END
            V vert(A a) { V o; o.positionCS=TransformObjectToHClip(a.positionOS.xyz); o.uv=a.uv; return o; }
            half4 frag(V i):SV_Target
            {
                float2 d=min(i.uv,1-i.uv);
                float2 aa=max(fwidth(i.uv),0.0001);
                float2 edge=smoothstep(_LineWidth-aa,_LineWidth+aa,d);
                return half4(_BaseColor.rgb*min(edge.x,edge.y),1);
            }
            ENDHLSL
        }
    }
}
