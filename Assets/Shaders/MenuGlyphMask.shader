Shader "Sound2/Menu Glyph Mask"
{
    Properties { _MainTex ("Font atlas", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend One One BlendOp Max
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4x4 _MaskProjection;
            struct Input {float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            struct Varyings {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
            Varyings Vert(Input v)
            {
                Varyings o;o.vertex=mul(_MaskProjection,mul(unity_ObjectToWorld,v.vertex));o.uv=v.uv;o.color=v.color;return o;
            }
            float4 Frag(Varyings i):SV_Target
            {
                float sdf=tex2D(_MainTex,i.uv).a;
                return float4(i.color.rgb*i.color.a,i.color.a)*smoothstep(.43,.53,sdf);
            }
            ENDHLSL
        }
    }
}
