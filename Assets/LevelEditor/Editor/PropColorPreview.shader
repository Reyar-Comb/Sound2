Shader "Hidden/Sound2/PropColorPreview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Cutoff ("Alpha cutoff", Float) = 0
        _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Pass
        {
            ZTest LEqual ZWrite Off Cull [_Cull]
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Cutoff;
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(appdata_base input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.texcoord, _MainTex);
                return output;
            }
            fixed4 frag(Varyings input) : SV_Target
            {
                fixed4 color = tex2D(_MainTex, input.uv) * _Color;
                clip(color.a - _Cutoff);
                // Scene GUI draws after the camera's color conversion.
                #ifndef UNITY_COLORSPACE_GAMMA
                    color.rgb = LinearToGammaSpace(color.rgb);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
