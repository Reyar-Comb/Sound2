// Pulse arrival, distance attenuation and per-source face density. Contour detection,
// ordered point placement, dot sizing and drawing stay in BlackBoxScreenPoints.shader.
float _SonarActive, _SonarMaskSize;
float4 _SonarPulseOrigins[8], _SonarPulseParameters[8];
float4 _SonarPulseDistanceFalloffs[8]; // start metres, end metres, power (0 = disabled), strength
TEXTURE2D_ARRAY(_SonarBlockingPlanes);

// x: contour reveal; y: face reveal after the existing ordered density selection.
float2 SonarVisibility(float3 positionWS, float3 normalWS, float orderedRank)
{
    float2 brightness = 0;
    [unroll] for (int i = 0; i < 8; i++)
    {
        float4 settings = _SonarPulseParameters[i];
        if (settings.w < .5) continue;
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
        float age = _PointAnimationTime - _SonarPulseOrigins[i].w - distance / settings.y;
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
        float referenceRange = max(.001, min(_RevealDistance, _DistanceBands.w));
        float patternScale = max(.001, reach / referenceRange);
        float density = FaceDensityAt(positionWS, normalWS, _SonarPulseOrigins[i].xyz, patternScale, true)
            * saturate(_FaceDensityScale);
        brightness = max(brightness, float2(reveal, orderedRank < density ? reveal : 0));
    }
    return brightness;
}
