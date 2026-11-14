Shader "Sound2/SonicSpace Point Menu"
{
    Properties { _MainTex ("Title mask", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "ScreenPointLattice.hlsl"
            sampler2D _MainTex;
            float4 _MenuSize; // physical viewport xy, reference viewport zw
            float _GridPitch,_DotRadius,_Transition,_Hover,_OpeningTime,_TitleDotScale,_TitleDensity;
            struct Input {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
            struct Varyings {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};
            Varyings Vert(Input v) {Varyings o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
            float Ease(float t) {t=saturate(t);return t*t*t*(t*(t*6-15)+10);}
            float2 Glyph(float2 referencePixel)
            {
                float2 uv=float2(referencePixel.x/_MenuSize.z,1-referencePixel.y/_MenuSize.w);
                if(any(uv<0) || any(uv>1))return 0;
                return tex2D(_MainTex,uv).rg;
            }
            float CellRandom(uint2 cell,uint salt)
            {
                uint h=cell.x*1597334677u+cell.y*3812015801u+salt*2798796415u;
                h=(h^(h>>16))*2246822519u;h=(h^(h>>13))*3266489917u;h^=h>>16;
                return (h&0x00ffffffu)/16777216.0;
            }
            float SpectrumHeight(float column,float x)
            {
                float normalized=(x-_MenuSize.z*.5)/(_MenuSize.z*.5);
                float spread=lerp(.045,1.3,Ease((_OpeningTime-.08)/2.4));
                float envelope=exp(-pow(abs(normalized)/spread,4));
                float seed=CellRandom(uint2(column,0),7);
                float rhythm=.18+.53*pow(.5+.5*sin(_OpeningTime*(1.7+seed*1.9)+seed*31),2)
                    +.29*pow(.5+.5*sin(_OpeningTime*3.1+column*2.39996),2);
                return 76*Ease((_OpeningTime-.08)/2.6)*envelope*rhythm;
            }
            float MenuInk(float2 source,float aa)
            {
                float pitch=max(1,_GridPitch);
                float2 cell=floor(source/pitch),centre=(cell+.5)*pitch;
                float2 glyph=Glyph(centre);
                float rank=OrderedRank((uint2)max(cell,0));
                float titleRadius=min(pitch*.47,_DotRadius*_TitleDotScale+_Hover*step(.2,glyph.g)*.35);
                float distance=length(source-centre);
                float title=(1-smoothstep(titleRadius-aa,titleRadius+aa,distance))
                    *saturate(max(glyph.r,glyph.g)*2)*(rank<_TitleDensity?1:0);
                // Dilate the actual animated glyphs, not their rectangles: spectrum bars sit behind them.
                float clearance=max(glyph.r,glyph.g);
                [unroll] for(int y=-1;y<=1;y++)[unroll] for(int x=-1;x<=1;x++)
                {
                    float2 neighbor=Glyph(centre+float2(x,y)*7.5);
                    clearance=max(clearance,max(neighbor.r,neighbor.g));
                }
                // One vertical stack of lattice dots followed by two empty columns. No connecting curve.
                float column=floor(cell.x/3);
                float baseline=(floor(_MenuSize.w*.5/pitch)+.5)*pitch;
                float height=SpectrumHeight(column,centre.x);
                float waveBand=(1-step(.5,fmod(cell.x,3)))
                    *(1-smoothstep(height+pitch*.15,height+pitch*.8,abs(centre.y-baseline)));
                float waveRadius=min(pitch*.30,_DotRadius*1.05);
                float wave=(1-smoothstep(waveRadius-aa,waveRadius+aa,distance))*waveBand
                    *(1-smoothstep(.02,.3,clearance))*Ease((_OpeningTime-.08)/.24);
                return max(title,wave);
            }
            float4 Frag(Varyings i):SV_Target
            {
                // Same top-left anchored half-cell screen lattice and 720p scaling as the world.
                float2 p=float2(i.uv.x,1-i.uv.y)*_MenuSize.zw;
                float pitch=max(1,_GridPitch);
                float aa=.70710678*720/_MenuSize.y;
                float2 cell=floor(p/pitch),centre=(cell+.5)*pitch;
                uint2 id=(uint2)max(cell,0);
                float t=_Transition;
                float radial=saturate(length(centre-_MenuSize.zw*.5)/length(_MenuSize.zw*.5));
                // Fixed per-cell timing: a ragged, outward-moving front, never a UV displacement.
                float arrival=max(.015,.025+.56*pow(radial,.9)
                    +(CellRandom(id,11)-.5)*.14+(CellRandom(id/7u,19)-.5)*.06);
                float age=t-arrival;
                float fragment=Ease(age/.065);
                float duration=lerp(.105,.215,CellRandom(id,23));
                float life=saturate((age-.065)/duration);
                uint mode=(uint)(CellRandom(id,31)*3);
                float remaining;
                if(mode==0u)remaining=1-step(1,life); // Hold the full dot, then cut.
                else if(mode==1u)remaining=1-Ease(life); // Soft nonlinear fade.
                else
                {
                    uint beat=(uint)max(0,floor((age-.065)*52+CellRandom(id,41)*5));
                    float blink=step(life*.85,CellRandom(id,beat+53u));
                    remaining=(1-step(1,life))*(life>0?blink:1);
                }
                float grainRadius=lerp(pitch*.70710678+aa,_DotRadius,fragment);
                float grain=1-smoothstep(grainRadius-aa,grainRadius+aa,length(p-centre));
                // Surface -> stationary white dots -> black. Title and spectrum use the exact same coordinates.
                float white=(1-MenuInk(p,aa))*grain*remaining;
                float alpha=1-Ease((t-.86)/.14);
                return float4(white,white,white,alpha);
            }
            ENDHLSL
        }
    }
}
