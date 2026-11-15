using System;
using UnityEngine;

[Serializable]
public sealed class SubtitleCue
{
    [TextArea(2, 5)] public string text;
    [Min(.1f)] public float duration = 4;
    public SubtitleCue() { }
    public SubtitleCue(string value, float seconds = 4) { text = value; duration = seconds; }
}
