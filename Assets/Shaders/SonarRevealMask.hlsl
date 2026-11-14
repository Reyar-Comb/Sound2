// Pulse arrival, distance attenuation and per-source face density. Contour detection,
// ordered point placement, dot sizing and drawing stay in BlackBoxScreenPoints.shader.
float _SonarActive, _SonarMaskSize;
// Match SonarPulseField capacities: main 32, footsteps 4, danger 4, coin 8, lightning 4.
#define SONAR_PULSE_COUNT 52
#define SONAR_DANGER_OFFSET 36
#define SONAR_COIN_OFFSET 40
#define SONAR_LIGHTNING_OFFSET 48
float4 _SonarPulseOrigins[SONAR_PULSE_COUNT], _SonarPulseParameters[SONAR_PULSE_COUNT];
float4 _SonarPulseDensityFalloffs[SONAR_PULSE_COUNT];
float4 _SonarPulseDistanceFalloffs[SONAR_PULSE_COUNT]; // start metres, end metres, power (0 = disabled), strength
TEXTURE2D_ARRAY(_SonarBlockingPlanes);

// Shared travel/afterglow envelope, also used by each coin's miniature surface wave.
float PulseAfterglow(float age,float duration)
{
    if(age<0 || age>=duration)return 0;
    float fade=1-age/max(.0001,duration);
    return fade*fade;
}

// Each channel uses the same lattice. Priority is applied later, independently for faces and edges.
struct PulseReveals { float2 white, danger, coin, lightning; };
PulseReveals SonarVisibility(float3 positionWS, float3 normalWS, float orderedRank)
{
    PulseReveals brightness; brightness.white=0; brightness.danger=0; brightness.coin=0; brightness.lightning=0;
    [loop] for (int i = 0; i < SONAR_PULSE_COUNT; i++)
    {
        float4 settings = _SonarPulseParameters[i];
        if (settings.w < .5) continue;
        float elapsed = _PointAnimationTime - _SonarPulseOrigins[i].w;
        if (elapsed < 0 || elapsed >= settings.x / settings.y + settings.z) continue;
        float3 delta = positionWS - _SonarPulseOrigins[i].xyz;
        float distance = length(delta);
        if (distance > settings.x || distance < .0001) continue;
        float4 attenuation = _SonarPulseDistanceFalloffs[i];
        float distanceGain = attenuation.w;
        if (attenuation.z > 0)
        {
            // The cap belongs to this emission's fixed world origin, never to the moving camera.
            if (distance >= attenuation.y) continue;
            distanceGain *= pow(saturate(1 - smoothstep(attenuation.x, attenuation.y, distance)), attenuation.z);
        }
        float4 thinning = _SonarPulseDensityFalloffs[i];
        float densityGain = 1;
        if (thinning.w>.5)
        {
            if (distance>=thinning.y) continue;
            densityGain=pow(saturate(1-smoothstep(thinning.x,thinning.y,distance)),thinning.z);
        }
        float age = elapsed - distance / settings.y;
        if (age < 0 || age >= settings.z) continue;
        float3 magnitude = abs(delta);
        int face;
        float2 uv;
        if (magnitude.x >= magnitude.y && magnitude.x >= magnitude.z)
        { face = delta.x >= 0 ? 0 : 1; uv = delta.zy / magnitude.x; }
        else if (magnitude.y >= magnitude.z)
        { face = delta.y >= 0 ? 2 : 3; uv = delta.xz / magnitude.y; }
        else
        { face = delta.z >= 0 ? 4 : 5; uv = delta.xy / magnitude.z; }
        int2 cell = clamp((int2)((uv * .5 + .5) * _SonarMaskSize), 0, (int)_SonarMaskSize - 1);
        float4 plane = LOAD_TEXTURE2D_ARRAY(_SonarBlockingPlanes, cell, i * 6 + face);
        if (dot(plane.xyz, plane.xyz) > .5 && dot(plane.xyz, delta) < plane.w - .06) continue;
        float reveal = distanceGain * PulseAfterglow(age,settings.z);
        // Reuse the complete core/ring/distance density rule around this pulse's fixed origin.
        float reach = attenuation.z > 0 ? min(settings.x, attenuation.y) : settings.x;
        if (thinning.w>.5) reach=min(reach,thinning.y);
        float referenceRange = max(.001, min(_RevealDistance, _DistanceBands.w));
        float patternScale = max(.001, reach / referenceRange);
        float density = FaceDensityAt(positionWS, normalWS, _SonarPulseOrigins[i].xyz, patternScale, true)
            * saturate(_FaceDensityScale) * densityGain;
        float2 visibility = float2(orderedRank < densityGain ? reveal : 0, orderedRank < density ? reveal : 0);
        if (i>=SONAR_LIGHTNING_OFFSET) brightness.lightning=max(brightness.lightning,visibility);
        else if (i>=SONAR_COIN_OFFSET) brightness.coin=max(brightness.coin,visibility);
        else if (i>=SONAR_DANGER_OFFSET) brightness.danger=max(brightness.danger,visibility);
        else brightness.white=max(brightness.white,visibility);
    }
    return brightness;
}
