using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class SubtitleCue
{
    [TextArea(2, 5)] public string text;
    [Min(.1f)] public float duration = 4;
    public SubtitleCue() { }
    public SubtitleCue(string value, float seconds = 4) { text = value; duration = seconds; }
}

[Serializable]
public sealed class FloorRule
{
    [Min(0)] public int floor;
    [Min(.1f)] public float difficulty = 1;
    [Range(.1f, 2)] public float sonarRangeMultiplier = 1;
    [Range(.1f, 2)] public float sonarAfterglowMultiplier = 1;
    public List<SubtitleCue> entrySubtitles = new();
}

[CreateAssetMenu(menuName = "Sound2/Floor Progression Profile")]
public sealed class FloorProgressionProfile : ScriptableObject
{
    [Min(0)] public float difficultyPerFloor = .15f;
    [Range(.5f, 1)] public float sonarRangePerFloor = .96f;
    [Range(.5f, 1)] public float sonarAfterglowPerFloor = .94f;
    [Range(.1f, 1)] public float minimumSonarMultiplier = .4f;
    public bool applySonarDifficulty = true;
    [TextArea] public string defaultEntryText = "F{floor} · 继续向下。留意脚下，也留意回声。";
    public List<FloorRule> overrides = new();

    public FloorRule Evaluate(int floor)
    {
        floor = Mathf.Max(0, floor);
        foreach (var rule in overrides) if (rule != null && rule.floor == floor) return rule;
        return new FloorRule {
            floor = floor, difficulty = 1 + floor * difficultyPerFloor,
            sonarRangeMultiplier = Mathf.Max(minimumSonarMultiplier, Mathf.Pow(sonarRangePerFloor, floor)),
            sonarAfterglowMultiplier = Mathf.Max(minimumSonarMultiplier, Mathf.Pow(sonarAfterglowPerFloor, floor)),
            entrySubtitles = new List<SubtitleCue> { new(defaultEntryText.Replace("{floor}", floor.ToString())) }
        };
    }
}
