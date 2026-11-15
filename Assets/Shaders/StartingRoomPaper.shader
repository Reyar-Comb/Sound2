Shader "Sound2/Starting Room Paper"
{
    Properties { _BaseColor("Paper", Color)=(1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 local : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            Varyings Vert(Attributes v)
            { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.local=v.positionOS.xyz; return o; }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 edge=(.5-abs(i.local))/max(fwidth(i.local),.00001);
                float second=min(max(edge.x,edge.y),min(max(edge.y,edge.z),max(edge.x,edge.z)));
                float2 p=i.positionCS.xy*720/_ScreenParams.y;
                float circle=1-smoothstep(.65,1.3,length(frac(p/5.5)*5.5-2.75));
                float ink=(1-smoothstep(.5,2.5,second))*circle*.7;
                return half4(_BaseColor.rgb*(1-ink),1);
            }
            ENDHLSL
        }
    }
}
