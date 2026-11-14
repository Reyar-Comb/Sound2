using System;
using UnityEngine;

/// <summary>Nested thinning of the shared ordered lattice, separate from brightness attenuation.</summary>
[Serializable]
public sealed class SonarDensitySettings
{
    public bool enabled;
    [Min(0)] public float fadeStartDistance = 6;
    [Min(.01f)] public float fadeEndDistance = 10;
    [Range(.1f,8)] public float falloffPower = 1;
    public Vector4 Pack(float range)
    {
        if (!enabled) return Vector4.zero;
        float end = Mathf.Clamp(fadeEndDistance,.01f,Mathf.Max(.01f,range));
        return new Vector4(Mathf.Clamp(fadeStartDistance,0,end-.001f),end,Mathf.Clamp(falloffPower,.1f,8),1);
    }
}
