using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Public queue for narrative/gameplay cues; floor entries replace obsolete guidance.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController))]
public sealed class PlayerSubtitles : MonoBehaviour
{
    [Min(.01f)] public float fadeTime = .3f;
    [Min(10)] public float subtitleFontSize = 24;
    public TMP_Text SubtitleText => subtitle;
    public TMP_Text FloorText => floorLabel;
    public int PendingCount => queue.Count;
    public string CurrentText => urgent?.text ?? current?.text ?? "";

    readonly Queue<SubtitleCue> queue = new();
    PlayerController controller;
    Canvas canvas;
    TextMeshProUGUI subtitle, floorLabel;
    SubtitleCue current;
    SubtitleCue urgent;
    float urgentElapsed;
    float elapsed;

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        canvas = SubtitleHud.CreateCanvas("NarrativeHUD", 25, transform);
        subtitle = SubtitleHud.CreateSubtitle(canvas.transform, subtitleFontSize);
        subtitle.color = new Color(1, 1, 1, 0);
        floorLabel = SubtitleHud.CreateText("FloorIndicator", canvas.transform, 24);
        floorLabel.alignment = TextAlignmentOptions.TopRight;
        var rect = floorLabel.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-32, -28); rect.sizeDelta = new Vector2(200, 50);
        SetFloor(0);
    }

    public void SetFloor(int floor) { if (floorLabel != null) floorLabel.text = "F" + Mathf.Max(0, floor); }
    public void Enqueue(SubtitleCue cue)
    {
        if (cue == null || string.IsNullOrWhiteSpace(cue.text)) return;
        queue.Enqueue(new SubtitleCue(cue.text, Mathf.Max(.1f, cue.duration)));
    }
    public void Show(string text, float duration = 4) => Enqueue(new SubtitleCue(text, duration));

    // Interaction feedback takes priority without discarding the unfinished floor guidance.
    public void ShowImmediate(string text, float duration = 2)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        urgent = new SubtitleCue(text, Mathf.Max(.1f, duration));
        urgentElapsed = 0;
    }
    public void Replace(IEnumerable<SubtitleCue> cues)
    {
        Clear();
        if (cues != null) foreach (var cue in cues) Enqueue(cue);
    }
    public void Clear()
    {
        queue.Clear(); current = urgent = null; elapsed = urgentElapsed = 0;
        if (subtitle != null) { subtitle.text = ""; subtitle.color = new Color(1, 1, 1, 0); }
    }
    void Update()
    {
        GameUiTheme.ApplySceneFonts();
        bool visible = controller != null && controller.isActiveAndEnabled && !controller.InputSuspended;
        canvas.enabled = visible;
        if (!visible || Time.timeScale <= 0) return;
        if (StorySubtitles.IsPlaying)
        {
            if (!string.IsNullOrEmpty(subtitle.text)) subtitle.text = "";
            return;
        }
        if (urgent == null && current == null && queue.Count > 0)
        {
            current = queue.Dequeue(); elapsed = 0; subtitle.text = current.text;
        }
        var cue = urgent ?? current;
        if (cue == null) return;
        subtitle.text = cue.text;
        if (urgent != null) urgentElapsed += Time.deltaTime; else elapsed += Time.deltaTime;
        float activeElapsed = urgent != null ? urgentElapsed : elapsed;
        float fade = Mathf.Max(.01f, fadeTime), end = cue.duration + fade * 2;
        float alpha = Mathf.SmoothStep(0, 1, Mathf.Min(activeElapsed / fade, (end - activeElapsed) / fade));
        subtitle.color = new Color(1, 1, 1, alpha);
        if (activeElapsed >= end)
        {
            if (urgent != null) urgent = null; else current = null;
            subtitle.text = "";
        }
    }

    void OnDisable() { if (canvas != null) canvas.enabled = false; }
    void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
}
