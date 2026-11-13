using System;
using UnityEngine;

/// <summary>Reusable visual distance response, captured independently by each emitted pulse.</summary>
[Serializable]
public sealed class SonarDistanceSettings
{
    public bool enabled;
    [Min(0)] public float fadeStartDistance = .5f;
    [Min(.01f)] public float fadeEndDistance = 3;
    [Range(.1f, 8)] public float falloffPower = 2;
    [Range(0, 1)] public float pulseStrength = .65f;

    public Vector4 Pack(float range)
    {
        if (!enabled) return new Vector4(0, range, 0, 1);
        float end = Mathf.Clamp(fadeEndDistance, .01f, Mathf.Max(.01f, range));
        return new Vector4(Mathf.Clamp(fadeStartDistance, 0, end - .001f), end,
            Mathf.Clamp(falloffPower, .1f, 8), Mathf.Clamp01(pulseStrength));
    }
}
