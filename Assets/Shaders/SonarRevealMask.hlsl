// Pulse arrival, distance attenuation and per-source face density. Contour detection,
// ordered point placement, dot sizing and drawing stay in BlackBoxScreenPoints.shader.
float _SonarActive, _SonarMaskSize, _BossWavesActive;
// Match SonarPulseField capacities: main 32, footsteps 4, danger 32, coin 8, lightning 4.
#define SONAR_PULSE_COUNT 80
#define SONAR_DANGER_OFFSET 36
#define SONAR_COIN_OFFSET 68
#define SONAR_LIGHTNING_OFFSET 76
#define BOSS_WAVE_COUNT 6
float4 _SonarPulseOrigins80[SONAR_PULSE_COUNT], _SonarPulseParameters80[SONAR_PULSE_COUNT];
float4 _SonarPulseDensityFalloffs80[SONAR_PULSE_COUNT];
float4 _SonarBlockerRotations80[SONAR_PULSE_COUNT]; // cos/sin of doorway mapping yaw
float4 _SonarPulseDistanceFalloffs80[SONAR_PULSE_COUNT]; // start metres, end metres, power (0 = disabled), strength
float4 _BossWaveOrigins[BOSS_WAVE_COUNT], _BossWaveShapes[BOSS_WAVE_COUNT];
float4 _BossWaveMotion[BOSS_WAVE_COUNT], _BossWaveArenas[BOSS_WAVE_COUNT];
TEXTURE2D_ARRAY(_SonarBlockingPlanes);

// Shared travel/afterglow envelope, also used by each coin's miniature surface wave.
float PulseAfterglow(float age,float duration)
{
    if(age<0 || age>=duration)return 0;
    float fade=1-age/max(.0001,duration);
    return fade*fade;
}

#include "AudioReactiveSonar.hlsl"

float2 BossGroundWaveVisibility(float3 positionWS, float3 normalWS, float orderedRank)
{
    float2 result = 0;
    [loop] for (int i = 0; i < BOSS_WAVE_COUNT; i++)
    {
        float4 motion = _BossWaveMotion[i];
        if (motion.x <= 0) continue;
        float4 origin = _BossWaveOrigins[i];
        float elapsed = _PointAnimationTime - origin.w;
        if (elapsed < 0 || elapsed >= motion.y / motion.x + motion.z) continue;
        float4 arena = _BossWaveArenas[i];
        float3 delta = positionWS - origin.xyz;
        if (delta.y < -.05 || delta.y > arena.w || distance(positionWS.xz, arena.xy) > arena.z + .06) continue;
        float4 shape = _BossWaveShapes[i];
        float depth = length(delta.xz);
        if (dot(shape.xy, shape.xy) > .5)
        {
            if (abs(dot(delta.xz, float2(shape.y, -shape.x))) > shape.z) continue;
            depth = dot(delta.xz, shape.xy);
        }
        if (depth < -shape.w || depth > motion.y + shape.w) continue;
        float age = elapsed - max(0, depth - shape.w) / motion.x;
        float glow = PulseAfterglow(age, motion.z);
        if (glow <= 0) continue;
        float scale = motion.y / max(.001, min(_RevealDistance, _DistanceBands.w));
        float density = FaceDensityAt(positionWS, normalWS, origin.xyz, scale, true) * saturate(_FaceDensityScale);
        float faceDensity = saturate(density * motion.w);
        result = max(result, float2(glow, orderedRank < faceDensity ? glow : 0));
    }
    return result;
}

// Each channel uses the same lattice. Priority is applied later, independently for faces and edges.
struct PulseReveals { float2 white, danger, coin, lightning, gold; };
PulseReveals SonarVisibility(float3 positionWS, float3 normalWS, float orderedRank)
{
    PulseReveals brightness; brightness.white=0; brightness.danger=0; brightness.coin=0; brightness.lightning=0; brightness.gold=0;
    if(_SonarActive>.5 && _BlackBoxRevealModes.y>.5)
    [loop] for (int i = 0; i < SONAR_PULSE_COUNT; i++)
    {
        float4 settings = _SonarPulseParameters80[i];
        if (settings.w < .5) continue;
        float elapsed = _PointAnimationTime - _SonarPulseOrigins80[i].w;
        if (elapsed < 0 || elapsed >= settings.x / settings.y + settings.z) continue;
        float3 delta = positionWS - _SonarPulseOrigins80[i].xyz;
        float distance = length(delta);
        if (distance > settings.x || distance < .0001) continue;
        float4 attenuation = _SonarPulseDistanceFalloffs80[i];
        float distanceGain = attenuation.w;
        if (attenuation.z > 0)
        {
            // The cap belongs to this emission's fixed world origin, never to the moving camera.
            if (distance >= attenuation.y) continue;
            distanceGain *= pow(saturate(1 - smoothstep(attenuation.x, attenuation.y, distance)), attenuation.z);
        }
        float4 thinning = _SonarPulseDensityFalloffs80[i];
        float densityGain = 1;
        if (thinning.w>.5)
        {
            if (distance>=thinning.y) continue;
            densityGain=pow(saturate(1-smoothstep(thinning.x,thinning.y,distance)),thinning.z);
        }
        float age = elapsed - distance / settings.y;
        if (age < 0 || age >= settings.z) continue;
        // Read the original blocker cubemap in its original frame after a doorway transfer.
        // Distance/fade and world-space density above/below remain in the arrival space.
        float2 basis = _SonarBlockerRotations80[i].xy;
        delta.xz = float2(basis.x * delta.x - basis.y * delta.z, basis.y * delta.x + basis.x * delta.z);
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
        float density = FaceDensityAt(positionWS, normalWS, _SonarPulseOrigins80[i].xyz, patternScale, true)
            * saturate(_FaceDensityScale) * densityGain;
        float2 visibility = float2(orderedRank < densityGain ? reveal : 0, orderedRank < density ? reveal : 0);
        if (i>=SONAR_LIGHTNING_OFFSET) brightness.lightning=max(brightness.lightning,visibility);
        else if (i>=SONAR_COIN_OFFSET)
        {
            if(settings.w>1.5) brightness.gold=max(brightness.gold,visibility);
            else brightness.coin=max(brightness.coin,visibility);
        }
        else if (i>=SONAR_DANGER_OFFSET) brightness.danger=max(brightness.danger,visibility);
        else brightness.white=max(brightness.white,visibility);
    }
    if (_SonarActive > .5 && _BossWavesActive > .5 && _BlackBoxRevealModes.y > .5)
        brightness.danger = max(brightness.danger, BossGroundWaveVisibility(positionWS, normalWS, orderedRank));
    float2 audioGold;
    brightness.white=max(brightness.white,AudioReactiveVisibility(positionWS,normalWS,orderedRank,audioGold));
    brightness.gold=max(brightness.gold,audioGold);
    return brightness;
}
