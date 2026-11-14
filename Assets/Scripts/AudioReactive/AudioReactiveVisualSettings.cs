using System;
using UnityEngine;

[Serializable]
public sealed class AudioReactiveVisualSettings
{
    [Min(1)] public float range = 12;
    [Min(.1f)] public float waveSpeed = 7;
    [Range(0, 2)] public float afterglow = .06f;
    [Tooltip("Multiply the dry peak before mapping. Too much gain clips both quiet and loud peaks to white.")]
    [Min(0)] public float gain = 1;
    [Tooltip("Peaks at/below this level stay dark, after Gain. Live while playing.")]
    [Range(0, .99f)] public float peakFloor = .05f;
    [Tooltip("Peaks at/above this level reach full brightness, after Gain. Live while playing.")]
    [Range(.01f, 1)] public float peakCeiling = .8f;
    [Tooltip("Above 1 darkens weak peaks and increases contrast; below 1 lifts quiet peaks. Live while playing.")]
    [Range(.1f, 6)] public float responseExponent = 1.8f;
    public bool highQualityOcclusion;
    public SonarDistanceSettings distance = new SonarDistanceSettings { enabled = true, fadeStartDistance = 1, fadeEndDistance = 12, pulseStrength = 1, falloffPower = 1 };
    public SonarDensitySettings density = new SonarDensitySettings();

    public Vector4 PackPeakLevels() => new Vector4(Mathf.Max(0, gain), Mathf.Clamp(responseExponent, .1f, 6),
        Mathf.Clamp(peakFloor, 0, .99f), Mathf.Max(Mathf.Clamp01(peakCeiling), Mathf.Clamp(peakFloor, 0, .99f) + .001f));

    public float MapPeak(float peak)
    {
        var v = PackPeakLevels();
        return Mathf.Pow(Mathf.Clamp01((peak * v.x - v.z) / (v.w - v.z)), v.y);
    }
}
