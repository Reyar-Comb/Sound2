Shader "Sound2/Player Damage Vines"
{
    Properties { [PerRendererData] _MainTex ("Branches", 2D) = "white" {} }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            sampler2D _MainTex;
            float _Growth, _White, _Backdrop, _Opacity;
            Varyings vert(Attributes v) { Varyings o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            half4 frag(Varyings i) : SV_Target
            {
                float2 grid = float2(320, 180);
                float2 cell = floor(i.uv * grid);
                float branch = tex2D(_MainTex, (cell + .5) / grid).r;
                float2 edge = min(i.uv, 1 - i.uv);
                float depth = 2 * min(edge.x, edge.y);
                float jitter = .035 * sin(cell.x * .073 + sin(cell.y * .11)) + .025 * sin(cell.y * .13 - cell.x * .031);
                // Wound tips end naturally at varied lengths, instead of being cropped into a rectangle.
                float grown = _Backdrop <= 0 ? 1 : 1 - smoothstep(_Growth - .09, _Growth, depth + jitter);
                // Each branch retires in the generated texture, rather than slicing a hole through the field.
                float visible = grown * _Opacity;
                float2 dotUV = frac(i.uv * grid) - .5;
                float circle = 1 - smoothstep(.24, .40, length(dotUV));
                float dot = circle * branch;
                // Red exists only on actual vine points. Gaps remain neutral black/grey/white.
                float redAlpha = saturate(dot * visible);
                float backgroundAlpha = max(saturate(_Backdrop), saturate(_White));
                float3 background = _Backdrop > 0 ? saturate(_White).xxx : float3(1, 1, 1);
                float alpha = redAlpha + backgroundAlpha * (1 - redAlpha);
                float3 rgb = (float3(1, .015, .025) * redAlpha
                    + background * backgroundAlpha * (1 - redAlpha)) / max(.0001, alpha);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
