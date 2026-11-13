// Arrival/fade mask only. All contour detection, dot sizing and drawing stay
// in the teammate's BlackBoxScreenPoints shader.
float _SonarActive, _SonarMaskSize;
float4 _SonarOrigins[4], _SonarParameters[4];
TEXTURE2D_ARRAY(_SonarBlockingPlanes);

float SonarVisibility(float3 positionWS)
{
    float brightness = 0;
    [unroll] for (int i = 0; i < 4; i++)
    {
        float4 settings = _SonarParameters[i];
        if (settings.w < .5) continue;
        float3 delta = positionWS - _SonarOrigins[i].xyz;
        float distance = length(delta);
        if (distance > settings.x || distance < .0001) continue;
        float age = _PointAnimationTime - _SonarOrigins[i].w - distance / settings.y;
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
        brightness = max(brightness, fade * fade);
    }
    return brightness;
}
