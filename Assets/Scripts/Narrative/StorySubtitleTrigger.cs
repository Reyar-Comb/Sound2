using System.Collections.Generic;
using UnityEngine;

/// <summary>Inspector-configured story lines for a gameplay or UI event.</summary>
[AddComponentMenu("Sound2/Story Subtitle Trigger")]
public sealed class StorySubtitleTrigger : MonoBehaviour
{
    [SerializeField, Min(1), Tooltip("每秒显示的字符数。")]
    private float charactersPerSecond = StorySubtitles.DefaultCharactersPerSecond;
    [SerializeField, Tooltip("按列表顺序播放；每行的 Duration 从打字结束后开始计时。")]
    private List<SubtitleCue> lines = new();

    public void Trigger() => StorySubtitles.Play(lines, charactersPerSecond);
}
