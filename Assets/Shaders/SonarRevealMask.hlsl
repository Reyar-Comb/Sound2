// Pulse arrival, distance attenuation and per-source face density. Contour detection,
// ordered point placement, dot sizing and drawing stay in BlackBoxScreenPoints.shader.
float _SonarActive, _SonarMaskSize;
float4 _SonarSceneOrigins[24], _SonarSceneParameters[24];
float4 _SonarDensityFalloffs[24];
float4 _SonarSceneDistanceFalloffs[24]; // start metres, end metres, power (0 = disabled), strength
TEXTURE2D_ARRAY(_SonarBlockingPlanes);

// Each channel uses the same lattice. Priority is applied later, independently for faces and edges.
struct PulseReveals { float2 white, danger, coin, lightning; };
PulseReveals SonarVisibility(float3 positionWS, float3 normalWS, float orderedRank)
{
    PulseReveals brightness; brightness.white=0; brightness.danger=0; brightness.coin=0; brightness.lightning=0;
    [loop] for (int i = 0; i < 24; i++)
    {
        float4 settings = _SonarSceneParameters[i];
        if (settings.w < .5) continue;
        float3 delta = positionWS - _SonarSceneOrigins[i].xyz;
        float distance = length(delta);
        if (distance > settings.x || distance < .0001) continue;
        float4 attenuation = _SonarSceneDistanceFalloffs[i];
        float distanceGain = attenuation.w;
        if (attenuation.z > 0)
        {
            // The cap belongs to this emission's fixed world origin, never to the moving camera.
            if (distance >= attenuation.y) continue;
            distanceGain *= pow(saturate(1 - smoothstep(attenuation.x, attenuation.y, distance)), attenuation.z);
        }
        float4 thinning = _SonarDensityFalloffs[i];
        float densityGain = 1;
        if (thinning.w>.5)
        {
            if (distance>=thinning.y) continue;
            densityGain=pow(saturate(1-smoothstep(thinning.x,thinning.y,distance)),thinning.z);
        }
        float age = _PointAnimationTime - _SonarSceneOrigins[i].w - distance / settings.y;
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
        float fade = 1 - age / settings.z;
        float reveal = distanceGain * fade * fade;
        // Reuse the complete core/ring/distance density rule around this pulse's fixed origin.
        float reach = attenuation.z > 0 ? min(settings.x, attenuation.y) : settings.x;
        if (thinning.w>.5) reach=min(reach,thinning.y);
        float referenceRange = max(.001, min(_RevealDistance, _DistanceBands.w));
        float patternScale = max(.001, reach / referenceRange);
        float density = FaceDensityAt(positionWS, normalWS, _SonarSceneOrigins[i].xyz, patternScale, true)
            * saturate(_FaceDensityScale) * densityGain;
        float2 visibility = float2(orderedRank < densityGain ? reveal : 0, orderedRank < density ? reveal : 0);
        if (i>=20) brightness.lightning=max(brightness.lightning,visibility);
        else if (i>=12) brightness.coin=max(brightness.coin,visibility);
        else if (i>=8) brightness.danger=max(brightness.danger,visibility);
        else brightness.white=max(brightness.white,visibility);
    }
    return brightness;
}
