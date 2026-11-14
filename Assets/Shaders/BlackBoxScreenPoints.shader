Shader "Sound2/Screen Grid Face And Corner Points"
{
    Properties
    {
        _RevealDistance ("Reveal Distance (metres)", Float) = 3
        _WorldCircleRadius ("Maximum Wall Circle Radius (metres)", Float) = 1.2
        _ScreenDotSpacing ("Densest Square Grid Spacing (pixels at 720p)", Float) = 5.5
        _ScreenDotRadius ("Dot Radius (pixels at 720p)", Float) = 4.125
        _DistanceBands ("Band Ends: Near / Medium / Far / Limit (metres)", Vector) = (0.75, 1.5, 2.25, 3)
        _DensityLevels ("Ring Density Multipliers: Near / Medium / Far / Farthest", Vector) = (1, 0.75, 0.5, 0.3)
        _WorldCoreRadius ("Maximum Wall Core Radius (metres)", Float) = 0.28
        _CoreDensityLevels ("Core Density: Near / Medium / Far / Farthest", Vector) = (1, 0.99, 0.98, 0.97)
        _WorldRadialBandEnds ("Maximum Wall Ring Outer Radii (metres)", Vector) = (0.52, 0.78, 1, 1.2)
        _RadialDensityLevels ("Ring Density: Inner / Middle / Outer / Edge", Vector) = (0.55, 0.22, 0.075, 0.02)
        [Toggle] _FaceEnabled ("Face Points", Float) = 1
        _FaceDensityScale ("Overall Face Density", Range(0,1)) = 0.03
        [Toggle] _EdgeEnabled ("Corner Points", Float) = 1
        _EdgeRevealDistance ("Corner Reveal Distance (metres)", Float) = 3
        _EdgeWidth ("Corner Detection Width (pixels at 720p)", Float) = 7
        _EdgeAngle ("Corner Angle Threshold (degrees)", Range(5,85)) = 35
        _EdgeDensityLevels ("Corner Density: Near / Medium / Far / Farthest", Vector) = (1, 0.65, 0.35, 0.15)
        [Toggle] _TwinkleEnabled ("Gentle Point Twinkle", Float) = 1
        _TwinkleFraction ("Fraction of Points Twinkling at Once", Range(0,0.05)) = 0.005
        _TwinkleDuration ("Point Fade Out and In Duration (seconds)", Float) = 6
        [Toggle] _StarsEnabled ("Sparse Ambient Stars", Float) = 1
        _StarChance ("Star Chance per World Direction", Range(0,1)) = 0.2
        _StarDuration ("Star Fade In and Out Duration (seconds)", Float) = 10
        _StarBrightness ("Star Brightness", Range(0,1)) = 0.65
    }


    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZTest Always ZWrite Off Cull Off
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        float4 _PointViewport, _PointGridSize;
        float4 _CrosshairClearZone; // xy: viewport centre; zw: clear radius/feather at 720p
        int _PlayerUiCount;
        float4 _PlayerUiRects[128], _PlayerUiOptions[128];
        float PlayerUiVisibility(float2 pixel, float radius)
        {
            #if UNITY_UV_STARTS_AT_TOP
                pixel.y=_PointViewport.y-pixel.y;
            #endif
            float2 p=pixel*(720.0/_PointViewport.y);
            float visibility=1;
            [loop] for(int i=0;i<_PlayerUiCount;i++)
            {
                float4 rect=_PlayerUiRects[i], options=_PlayerUiOptions[i];
                float rounded=min(options.z,min(rect.z,rect.w));
                float2 q=abs(p-rect.xy)-rect.zw+rounded;
                float distance=length(max(q,0))+min(max(q.x,q.y),0)-rounded;
                float mask=smoothstep(options.x+radius,options.x+radius+max(.1,options.y),distance);
                visibility=min(visibility,lerp(1,mask,options.w));
            }
            return visibility;
        }
        float CrosshairPointVisibility(float2 center, float radius)
        {
            if(_CrosshairClearZone.z<=0) return 1;
            float distance=length(center-_CrosshairClearZone.xy*_PointViewport.xy*(720.0/_PointViewport.y));
            // Fade whole dots, including their radius: no sliced circles at the clear-zone edge.
            return smoothstep(_CrosshairClearZone.z+radius,
                _CrosshairClearZone.z+radius+max(.1,_CrosshairClearZone.w),distance);
        }
        TEXTURE2D(_PointGrid); TEXTURE2D_X_FLOAT(_PointDepth);
        float _RevealDistance, _WorldCircleRadius, _WorldCoreRadius;
        float _ScreenDotSpacing, _ScreenDotRadius;
        float4 _DistanceBands, _DensityLevels, _CoreDensityLevels, _WorldRadialBandEnds, _RadialDensityLevels;
        float _FaceEnabled, _FaceDensityScale, _EdgeEnabled, _EdgeRevealDistance, _EdgeWidth, _EdgeAngle;
        float4 _EdgeDensityLevels;
        float _TwinkleEnabled, _TwinkleFraction, _TwinkleDuration;
        float _StarsEnabled, _StarChance, _StarDuration, _StarBrightness;
        float _PointAnimationTime;
        float4x4 _StarViewProjection;
        float4 _BlackBoxRevealModes; // x: continuous proximity, y: pulse reveal
        float4 _BlackBoxStyleVisibility;
        float4 _BlackBoxPlayerPosition;
        float _BlackBoxPlayerActive;
        struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
        Varyings Vert(uint vertexID : SV_VertexID)
        {
            Varyings o; UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS=GetFullScreenTriangleVertexPosition(vertexID); return o;
        }
        bool WorldAtPixel(int2 pixel, out float3 p)
        {
            p=0;
            if(any(pixel<0)||any(pixel>=int2(_PointViewport.xy))) return false;
            float z=LOAD_TEXTURE2D_X(_PointDepth, pixel).r;
            #if UNITY_REVERSED_Z
                if(z<0.000001) return false;
            #else
                if(z>0.999999) return false;
                z=lerp(UNITY_NEAR_CLIP_VALUE,1,z);
            #endif
            // Reconstruct the center of the exact texel whose depth was read.
            float2 uv=(float2(pixel)+.5)*_PointViewport.zw;
            p=ComputeWorldSpacePosition(uv,z,UNITY_MATRIX_I_VP);
            return true;
        }
        bool SurfaceAt(float2 uv,out float3 p,out float3 n)
        {
            n=0; p=0;
            if(any(uv<0)||any(uv>=1)) return false;
            int2 pixel=(int2)(uv*_PointViewport.xy);
            if(!WorldAtPixel(pixel,p)) return false;
            float3 l,r,u,d;
            // Integer offsets prevent a +/- one-pixel UV calculation rounding
            // back to the center texel and producing a zero/invalid normal.
            bool vl=WorldAtPixel(pixel+int2(-1,0),l);
            bool vr=WorldAtPixel(pixel+int2(1,0),r);
            bool vd=WorldAtPixel(pixel+int2(0,-1),d);
            bool vu=WorldAtPixel(pixel+int2(0,1),u);
            if(!(vl||vr)||!(vd||vu)) return false;
            float3 dx=vl&&(!vr||dot(p-l,p-l)<dot(r-p,r-p))?p-l:r-p;
            float3 dy=vd&&(!vu||dot(p-d,p-d)<dot(u-p,u-p))?p-d:u-p;
            n=normalize(cross(dx,dy));
            if(dot(n,_WorldSpaceCameraPos-p)<0) n=-n;
            return true;
        }
        // A fixed, periodic Bayer ordering thins one common lattice. No random
        // offsets, no changing pitch, and all density levels are nested subsets.
        float OrderedRank(uint2 cell)
        {
            uint rank=0;
            [unroll] for(uint bit=0;bit<8;bit++)
            {
                uint x=(cell.x>>bit)&1, y=(cell.y>>bit)&1;
                rank|=(((x^y)<<1)|y)<<(2*(7-bit));
            }
            return (rank+0.5)/65536.0;
        }
        float Tier(float distance,float4 density)
        {
            return distance<_DistanceBands.x?density.x:distance<_DistanceBands.y?density.y:
                distance<_DistanceBands.z?density.z:density.w;
        }
        // One density rule for proximity and pulses. A pulse scales the original pattern
        // to its own effective range; the proximity path uses the original scale of one.
        float FaceDensityAt(float3 p,float3 n,float3 source,float patternScale,bool sparseOutside)
        {
            float3 delta=(source-p)/patternScale;
            float signedDistance=dot(delta,n), wallDistance=abs(signedDistance), pointDistance=length(delta);
            float limit=min(_RevealDistance,_DistanceBands.w);
            if(limit<=0||pointDistance>=limit) return 0;
            float3 center=source-n*signedDistance*patternScale;
            float scale=saturate(1-wallDistance/limit);
            float radius=length(p-center)/patternScale;
            // Pulses keep the original sparse outer tier beyond the projection patch.
            // Proximity retains its original hard circle boundary.
            float outside=sparseOutside?saturate(_RadialDensityLevels.w*Tier(pointDistance,_DensityLevels)):0;
            if(scale<=0||radius>=_WorldCircleRadius*scale) return outside;
            if(radius<_WorldCoreRadius*scale) return saturate(Tier(pointDistance,_CoreDensityLevels));
            float4 rings=_WorldRadialBandEnds*scale;
            float radial=radius<rings.x?_RadialDensityLevels.x:radius<rings.y?_RadialDensityLevels.y:
                radius<rings.z?_RadialDensityLevels.z:radius<rings.w?_RadialDensityLevels.w:0;
            return saturate(radial*Tier(pointDistance,_DensityLevels));
        }
        float FaceDensity(float3 p,float3 n)
        {
            return FaceDensityAt(p,n,_BlackBoxPlayerPosition.xyz,1,false);
        }
        #include "SonarRevealMask.hlsl"
        float Corner(float2 uv,float3 p,float3 n)
        {
            float angleLimit=cos(radians(_EdgeAngle));
            float2 offset=_EdgeWidth*(_PointViewport.y/720.0)*_PointViewport.zw;
            float2 directions[4]={float2(1,0),float2(-1,0),float2(0,1),float2(0,-1)};
            float edge=0;
            [unroll] for(int i=0;i<4;i++)
            {
                float2 neighbourUV=uv+directions[i]*offset;
                // The screen border is not a scene edge.
                if(any(neighbourUV<=0)||any(neighbourUV>=1)) continue;
                float3 q,qn;
                if(!SurfaceAt(neighbourUV,q,qn)) { edge=1; continue; }
                // Absolute change handles concave AND convex creases; plane residual
                // additionally finds depth-separated silhouette/occlusion boundaries.
                float planeResidual=abs(dot(q-p,n));
                if(dot(n,qn)<angleLimit || planeResidual>max(.035,length(q-p)*.35)) edge=1;
            }
            return edge;
        }
        uint PointHash(uint value)
        {
            value ^= value >> 16; value *= 0x7feb352du;
            value ^= value >> 15; value *= 0x846ca68bu;
            return value ^ (value >> 16);
        }
        float Random01(uint seed) { return (PointHash(seed) >> 8) * (1.0 / 16777216.0); }
        uint CellSeed(uint2 cell) { return PointHash(cell.x ^ PointHash(cell.y + 0x9e3779b9u)); }
        // Zero value AND zero slope at both ends: a new random event can be
        // selected at a cycle boundary without any visible temporal jump.
        float GentlePulse(float phase)
        {
            return smoothstep(0,.4,phase) * (1-smoothstep(.6,1,phase));
        }
        float PointVisibility(uint2 cell)
        {
            if(_TwinkleEnabled<.5 || _BlackBoxStyleVisibility.w<.5) return 1;
            uint seed=CellSeed(cell);
            float cycle=_PointAnimationTime/max(_TwinkleDuration,2)+Random01(seed);
            uint eventSeed=seed ^ PointHash((uint)floor(cycle)+13579u);
            if(Random01(eventSeed)>=clamp(_TwinkleFraction,0,.05)) return 1;
            return 1-GentlePulse(frac(cycle));
        }
        float AmbientStar(uint id)
        {
            if(_BlackBoxRevealModes.x<.5 || _StarsEnabled<.5 || _BlackBoxStyleVisibility.z<.5) return 0;
            uint seed=PointHash(id)^0x68bc21ebu;
            bool animate=_TwinkleEnabled>.5 && _BlackBoxStyleVisibility.w>.5;
            float cycle=_PointAnimationTime/max(_StarDuration,4)+Random01(seed);
            uint eventSeed=seed^PointHash((animate?(uint)floor(cycle):0u)+97531u);
            if(Random01(eventSeed)>=saturate(_StarChance)) return 0;
            return (animate?GentlePulse(frac(cycle)):1)*saturate(_StarBrightness);
        }
        float4 SampleGrid(Varyings input):SV_Target
        {
            if(_BlackBoxPlayerActive<.5) return 0;
            uint2 cell=(uint2)input.positionCS.xy;
            float pitch=max(_ScreenDotSpacing,1)*_PointViewport.y/720.0;
            float2 uv=(float2(cell)+.5)*pitch*_PointViewport.zw;
            if(any(uv>=1)) return 0;
            float3 p,n;
            bool surface=SurfaceAt(uv,p,n);
            float4 result=0;
            if(_BlackBoxRevealModes.x>.5)
            {
                // Original continuous proximity reveal, including its density gaps.
                float rank=OrderedRank(cell);
                float faceDensity=surface && _FaceEnabled>.5 && _BlackBoxStyleVisibility.x>.5?FaceDensity(p,n):0;
                float distance=surface?length(_BlackBoxPlayerPosition.xyz-p):0;
                float edgeRegion=0;
                if(surface && _EdgeEnabled>.5 && _BlackBoxStyleVisibility.y>.5 && distance<_EdgeRevealDistance)
                    edgeRegion=Corner(uv,p,n);
                float face=rank<faceDensity*saturate(_FaceDensityScale)?1:0;
                float edge=rank<saturate(Tier(distance,_EdgeDensityLevels))?edgeRegion:0;
                float visibility=PointVisibility(cell);
                float occupied=faceDensity>0 || edgeRegion>0?1:0;
                result=float4(face*visibility,edge*visibility,0,occupied);
            }
            if(_BlackBoxRevealModes.y>.5 && _SonarActive>.5 && surface)
            {
                // Same points and contours, gated by pulse arrival and fade.
                float sonarRank=OrderedRank(cell);
                float2 pulseReveal=SonarVisibility(p,n,sonarRank);
                float reveal=pulseReveal.x;
                if(reveal>0)
                {
                    float sonarDistance=length(_BlackBoxPlayerPosition.xyz-p);
                    float sonarFace=_FaceEnabled>.5 && _BlackBoxStyleVisibility.x>.5?pulseReveal.y:0;
                    float contour=_EdgeEnabled>.5 && _BlackBoxStyleVisibility.y>.5
                        && sonarRank<saturate(Tier(sonarDistance,_EdgeDensityLevels))?Corner(uv,p,n)*reveal:0;
                    // Union the two masks. Overlapping dots retain their original brightness and size.
                    result=max(result,float4(sonarFace,contour,0,sonarFace>0 || contour>0?1:0));
                }
            }
            float radius=clamp(_ScreenDotRadius,0,max(_ScreenDotSpacing,1)*.75)+.70710678*720.0/_PointViewport.y;
            result.rg*=PlayerUiVisibility(uv*_PointViewport.xy,radius);
            return result;
        }
        float DrawPoints(Varyings input,int channel)
        {
            float referencePixel=720.0/_PointViewport.y;
            float2 screen=input.positionCS.xy*referencePixel;
            float pitch=max(_ScreenDotSpacing,1);
            int2 cell=(int2)floor(screen/pitch);
            float radius=clamp(_ScreenDotRadius,0,pitch*.75);
            float aa=.70710678*referencePixel;
            float result=0;
            [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
            {
                int2 candidate=cell+int2(x,y);
                if(any(candidate<0)||any(candidate>=int2(_PointGridSize.xy))) continue;
                float decision=LOAD_TEXTURE2D(_PointGrid,candidate)[channel];
                decision*=CrosshairPointVisibility((float2(candidate)+.5)*pitch,radius+aa);
                float distance=length(screen-(float2(candidate)+.5)*pitch);
                result=max(result,decision*(1-smoothstep(radius-aa,radius+aa,distance)));
            }
            return result;
        }
        half4 DrawFaces(Varyings input):SV_Target { float v=DrawPoints(input,0);return half4(v,v,v,0); }
        half4 DrawCorners(Varyings input):SV_Target { float v=DrawPoints(input,1);return half4(v,v,v,0); }
        struct StarVaryings
        {
            float4 positionCS:SV_POSITION;
            float2 offset:TEXCOORD0;
            nointerpolation float brightness:TEXCOORD1;
        };
        StarVaryings StarVert(uint vertexID:SV_VertexID,uint id:SV_InstanceID)
        {
            StarVaryings o;
            o.positionCS=float4(2,2,0,1);o.offset=0;o.brightness=0;
            if(_BlackBoxPlayerActive<.5) return o;
            float brightness=AmbientStar(id);
            if(brightness<=0) return o;
            // Fixed world directions on a sphere; camera rotation moves the
            // projected centers, while camera translation has no parallax.
            float y=1-2*(id+.5)/128.0;
            float angle=id*2.39996323;
            float r=sqrt(saturate(1-y*y));
            float3 direction=float3(cos(angle)*r,y,sin(angle)*r);
            float4 center=mul(_StarViewProjection,float4(direction,0));
            if(center.w<=.0001) return o;
            float2 ndc=center.xy/center.w;
            float2 uv=ndc*.5+.5;
            #if UNITY_UV_STARTS_AT_TOP
                uv.y=1-uv.y;
            #endif
            if(any(uv<0)||any(uv>=1)) return o;
            float pitch=max(_ScreenDotSpacing,1)*_PointViewport.y/720.0;
            int2 cell=(int2)floor(uv*_PointViewport.xy/pitch);
            // Mask a whole star at its center, never crop its circle by a wall.
            if(LOAD_TEXTURE2D(_PointGrid,cell).a>.5) return o;
            float2 corners[6]={float2(-1,-1),float2(-1,1),float2(1,1),
                float2(-1,-1),float2(1,1),float2(1,-1)};
            float radius=clamp(_ScreenDotRadius,0,max(_ScreenDotSpacing,1)*.75);
            float extent=radius+.70710678*720.0/_PointViewport.y;
            o.offset=corners[vertexID]*extent;
            float2 clipOffset=o.offset*(_PointViewport.y/720.0)*_PointViewport.zw*2;
            o.positionCS=float4(ndc+clipOffset,0,1);
            o.brightness=brightness*CrosshairPointVisibility(uv*_PointViewport.xy*(720.0/_PointViewport.y),extent)
                *PlayerUiVisibility(uv*_PointViewport.xy,extent);
            return o;
        }
        half4 DrawStars(StarVaryings input):SV_Target
        {
            float radius=clamp(_ScreenDotRadius,0,max(_ScreenDotSpacing,1)*.75);
            float aa=.70710678*720.0/_PointViewport.y;
            float v=input.brightness*(1-smoothstep(radius-aa,radius+aa,length(input.offset)));
            return half4(v,v,v,0);
        }
        ENDHLSL
        Pass
        {
            Name "SampleGrid"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SampleGrid
            ENDHLSL
        }
        Pass
        {
            Name "FaceDots"
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DrawFaces
            ENDHLSL
        }
        Pass
        {
            Name "CornerDots"
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DrawCorners
            ENDHLSL
        }
        Pass
        {
            Name "AmbientStars"
            Blend One One
            BlendOp Max
            HLSLPROGRAM
            #pragma vertex StarVert
            #pragma fragment DrawStars
            ENDHLSL
        }
    }
}
