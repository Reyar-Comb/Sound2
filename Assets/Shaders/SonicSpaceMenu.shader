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
            float _GridPitch,_DotRadius,_Transition,_OpeningTime,_TitleDotScale,_TitleDensity;
            float _RoomHalfSize,_RoomTanHalfFov,_TitleShimmer,_MenuStarsEnabled;
            float4x4 _RoomView;
            struct Input {float4 vertex:POSITION;float2 uv:TEXCOORD0;};
            struct Varyings {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};
            Varyings Vert(Input v) {Varyings o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
            float Ease(float t) {t=saturate(t);return t*t*t*(t*(t*6-15)+10);}
            float3 Glyph(float2 referencePixel)
            {
                float2 uv=float2(referencePixel.x/_MenuSize.z,1-referencePixel.y/_MenuSize.w);
                if(any(uv<0) || any(uv>1))return 0;
                return tex2D(_MainTex,uv).rgb; // English title mask
            }
            float CellRandom(uint2 cell,uint salt)
            {
                uint h=cell.x*1597334677u+cell.y*3812015801u+salt*2798796415u;
                h=(h^(h>>16))*2246822519u;h=(h^(h>>13))*3266489917u;h^=h>>16;
                return (h&0x00ffffffu)/16777216.0;
            }
            float TitleVisibility(uint2 cell)
            {
                float clock=_OpeningTime/lerp(2.7,6,CellRandom(cell,61))+CellRandom(cell,67)*5;
                float phase=frac(clock),mode=CellRandom(cell,(uint)floor(clock)+79u);
                float visibility=1;
                if(mode<.25 && phase>.35 && phase<.65)
                    visibility=step(.42,CellRandom(cell,(uint)floor(phase*22)+97u));
                else if(mode<.70)visibility=1-pow(sin(phase*3.14159265),8);
                return lerp(1,visibility,saturate(_TitleShimmer));
            }
            float RoomVisibility(float2 centre,uint2 cell)
            {
                if(_RoomHalfSize<=0)return 0;
                float2 ndc=centre/_MenuSize.zw*2-1;ndc.y=-ndc.y;
                float3 eye=float3(0,0,0); // Fixed at the cube's exact centre; mouse input changes rotation only.
                float3 ray=normalize(mul((float3x3)_RoomView,float3(ndc.x*_MenuSize.z/_MenuSize.w*_RoomTanHalfFov,ndc.y*_RoomTanHalfFov,1)));
                // Ray exits a sealed cube; six real planes share their twelve projected edges.
                float3 safeRay=lerp(-1,1,step(0,ray))*max(abs(ray),.00001);
                float3 exits=(sign(safeRay)*_RoomHalfSize-eye)/safeRay;
                float travel=min(exits.x,min(exits.y,exits.z));
                float3 hit=clamp(eye+ray*travel,-_RoomHalfSize,_RoomHalfSize);
                float3 sides=_RoomHalfSize-abs(hit);
                float edgeDistance=min(max(sides.x,sides.y),min(max(sides.x,sides.z),max(sides.y,sides.z)));
                float footprint=travel*(2*_RoomTanHalfFov/720)*max(1,_GridPitch);
                float edge=1-smoothstep(footprint*.45,footprint*1.15,edgeDistance);
                float wave=0;int current=(int)floor(_OpeningTime/2.8);
                [unroll] for(int i=0;i<4;i++)
                {
                    int slot=current-i;if(slot<0)continue;
                    uint2 id=uint2(slot,137);
                    float born=slot*2.8+.65+CellRandom(id,3)*.9;
                    float age=_OpeningTime-born;if(age<0)continue;
                    float3 origin=(float3(CellRandom(id,5),CellRandom(id,7),CellRandom(id,11))*2-1)*_RoomHalfSize*.8;
                    uint face=(uint)(CellRandom(id,13)*6);float side=face<3u?-_RoomHalfSize:_RoomHalfSize;
                    if(face%3u==0u)origin.x=side;else if(face%3u==1u)origin.y=side;else origin.z=side;
                    float arrival=age-distance(hit,origin)/3.2;
                    // The same travel-then-squared-afterglow behavior as the gameplay sonar.
                    float glow=arrival>=0?pow(saturate(1-arrival/1.15),2):0;
                    wave=max(wave,glow);
                }
                wave*=OrderedRank(cell)<.30?1:0;
                return max(edge*.85,wave*.85)*Ease((_OpeningTime-.08)/.85);
            }
            // Jittered cube-map directions: stable in space, independently alive, and snapped to the screen lattice.
            float MenuStars(float2 centre)
            {
                if(_MenuStarsEnabled<.5)return 0;
                float2 ndc=centre/_MenuSize.zw*2-1;ndc.y=-ndc.y;
                float3 ray=normalize(mul((float3x3)_RoomView,float3(ndc.x*_MenuSize.z/_MenuSize.w*_RoomTanHalfFov,ndc.y*_RoomTanHalfFov,1)));
                float3 ar=abs(ray);uint axis=ar.x>ar.y && ar.x>ar.z?0u:ar.y>ar.z?1u:2u;
                float major=axis==0u?ray.x:axis==1u?ray.y:ray.z;
                float2 face=axis==0u?ray.yz:axis==1u?ray.xz:ray.xy;
                float2 tile=(face/abs(major)*.5+.5)*16;
                uint2 cell=(uint2)clamp(floor(tile),0,15);uint salt=axis*2u+(major>0?1u:0u)+211u;
                float clock=_OpeningTime/lerp(4,9,CellRandom(cell,salt+17u))+CellRandom(cell,salt+19u);
                if(CellRandom(cell,salt+(uint)floor(clock)*31u)>.28)return 0;
                float2 samplePoint=((float2(cell)+lerp(.2,.8,float2(CellRandom(cell,salt+3u),CellRandom(cell,salt+7u))))/16*2-1);
                float side=major>0?1:-1;
                float3 direction=axis==0u?float3(side,samplePoint):axis==1u?float3(samplePoint.x,side,samplePoint.y):float3(samplePoint,side);
                float3 local=mul(direction,(float3x3)_RoomView);
                if(local.z<=0)return 0;
                float2 projected=local.xy/(local.z*max(.01,_RoomTanHalfFov));projected.x*=_MenuSize.w/_MenuSize.z;projected.y=-projected.y;
                float2 screen=(projected*.5+.5)*_MenuSize.zw;
                if(any(floor(screen/max(1,_GridPitch))!=floor(centre/max(1,_GridPitch))))return 0;
                float phase=frac(clock);
                return smoothstep(0,.3,phase)*(1-smoothstep(.65,1,phase))*.8*Ease((_OpeningTime-.08)/.85);
            }
            float MenuInk(float2 source,float aa)
            {
                float pitch=max(1,_GridPitch);
                float2 cell=floor(source/pitch),centre=(cell+.5)*pitch;
                uint2 id=(uint2)max(cell,0);
                float3 glyph=Glyph(centre);
                float rank=OrderedRank(id);
                float titleRadius=min(pitch*.47,_DotRadius*_TitleDotScale);
                float distance=length(source-centre);
                float coverage=max(glyph.r,max(glyph.g,glyph.b));
                float animatedCoverage=max(glyph.r*TitleVisibility(id),max(glyph.g,glyph.b));
                float title=(1-smoothstep(titleRadius-aa,titleRadius+aa,distance))
                    *saturate(animatedCoverage*2)*(rank<_TitleDensity?1:0);
                // Even temporarily invisible title dots retain their exclusion area.
                float clearance=coverage;
                [unroll] for(int y=-1;y<=1;y++)[unroll] for(int x=-1;x<=1;x++)
                {
                    float3 neighbor=Glyph(centre+float2(x,y)*7.5);
                    clearance=max(clearance,max(neighbor.r,max(neighbor.g,neighbor.b)));
                }
                float roomRadius=min(pitch*.35,_DotRadius);
                float room=(1-smoothstep(roomRadius-aa,roomRadius+aa,distance))*max(RoomVisibility(centre,id),MenuStars(centre))
                    *(1-smoothstep(.02,.3,clearance));
                return max(title,room);
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
                // Surface -> stationary white dots -> black. Title and room use the exact same coordinates.
                float white=(1-MenuInk(p,aa))*grain*remaining;
                float alpha=1-Ease((t-.86)/.14);
                return float4(white,white,white,alpha);
            }
            ENDHLSL
        }
    }
}
