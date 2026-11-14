// Optional additive audio waves. No extra render target or draw pass; the existing point-grid samples this.
#define AUDIO_SONAR_CAPACITY 4
float _AudioSonarActive;
float4 _AudioSonarOrigins[AUDIO_SONAR_CAPACITY], _AudioSonarParameters[AUDIO_SONAR_CAPACITY];
float4 _AudioSonarLevels[AUDIO_SONAR_CAPACITY], _AudioSonarData[AUDIO_SONAR_CAPACITY];
float4 _AudioSonarFalloffs[AUDIO_SONAR_CAPACITY], _AudioSonarDensities[AUDIO_SONAR_CAPACITY];
TEXTURE2D_ARRAY(_AudioSonarAnalysis);
TEXTURE2D_ARRAY(_AudioSonarBlockers);

// Linear peaks are decoded once at upload; one channel and no frequency-band loop.
float AudioSonarRead(int slot,float time)
{
    float4 data=_AudioSonarData[slot];
    if(time<0 || time>=data.z)return 0;
    if(data.w>0)time=fmod(time,data.w);
    float frame=min(time*data.y,data.x-1);
    int a=(int)floor(frame),b=min(a+1,(int)data.x-1);
    return lerp(LOAD_TEXTURE2D_ARRAY(_AudioSonarAnalysis,int2(a,0),slot).r,
                LOAD_TEXTURE2D_ARRAY(_AudioSonarAnalysis,int2(b,0),slot).r,frac(frame));
}
float AudioSonarTrail(int slot,float time,float glow)
{
    float peak=AudioSonarRead(slot,time);
    if(glow>.0001)
    {
        peak=max(peak,AudioSonarRead(slot,time-glow*.5)*.55);
        peak=max(peak,AudioSonarRead(slot,time-glow)*.15);
    }
    float4 curve=_AudioSonarLevels[slot];
    float value=saturate((peak*curve.x-curve.z)/max(.001,curve.w-curve.z));
    return pow(value,curve.y);
}
float2 AudioReactiveVisibility(float3 positionWS,float3 normalWS,float orderedRank)
{
    float2 brightness=0;
    if(_AudioSonarActive<.5)return brightness;
    [loop]for(int slot=0;slot<AUDIO_SONAR_CAPACITY;slot++)
    {
        float4 settings=_AudioSonarParameters[slot];
        float elapsed=_AudioSonarOrigins[slot].w;
        if(settings.w<.5 || elapsed<0)continue;
        float3 delta=positionWS-_AudioSonarOrigins[slot].xyz;
        float distance=length(delta);
        if(distance<.0001 || distance>settings.x)continue;
        float4 attenuation=_AudioSonarFalloffs[slot],thinning=_AudioSonarDensities[slot];
        float distanceGain=attenuation.w,densityGain=1,reach=settings.x;
        if(attenuation.z>0)
        {
            if(distance>=attenuation.y)continue;
            reach=min(reach,attenuation.y);
            distanceGain*=pow(saturate(1-smoothstep(attenuation.x,attenuation.y,distance)),attenuation.z);
        }
        if(thinning.w>.5)
        {
            if(distance>=thinning.y)continue;
            reach=min(reach,thinning.y);
            densityGain=pow(saturate(1-smoothstep(thinning.x,thinning.y,distance)),thinning.z);
        }
        float age=elapsed-distance/settings.y;
        if(age<0)continue;
        float reveal=AudioSonarTrail(slot,age,settings.z);
        reveal*=distanceGain;
        if(reveal<.0001)continue;
        float3 magnitude=abs(delta);int face;float2 uv;
        if(magnitude.x>=magnitude.y && magnitude.x>=magnitude.z){face=delta.x>=0?0:1;uv=delta.zy/magnitude.x;}
        else if(magnitude.y>=magnitude.z){face=delta.y>=0?2:3;uv=delta.xz/magnitude.y;}
        else{face=delta.z>=0?4:5;uv=delta.xy/magnitude.z;}
        int2 cell=clamp((int2)((uv*.5+.5)*48),0,47);
        float4 plane=LOAD_TEXTURE2D_ARRAY(_AudioSonarBlockers,cell,slot*6+face);
        if(dot(plane.xyz,plane.xyz)>.5 && dot(plane.xyz,delta)<plane.w-.06)continue;
        float referenceRange=max(.001,min(_RevealDistance,_DistanceBands.w));
        float density=FaceDensityAt(positionWS,normalWS,_AudioSonarOrigins[slot].xyz,max(.001,reach/referenceRange),true)
            *saturate(_FaceDensityScale)*densityGain;
        brightness=max(brightness,float2(orderedRank<densityGain?reveal:0,orderedRank<density?reveal:0));
    }
    return brightness;
}
