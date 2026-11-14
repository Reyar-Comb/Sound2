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
    public string CurrentText => current != null ? current.text : "";

    readonly Queue<SubtitleCue> queue = new();
    PlayerController controller;
    Canvas canvas;
    TextMeshProUGUI subtitle, floorLabel;
    SubtitleCue current;
    float elapsed;

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        var root = new GameObject("NarrativeHUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 25;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = 1;
        subtitle = MakeText("Subtitle", root.transform, subtitleFontSize);
        subtitle.alignment = TextAlignmentOptions.Center;
        var rect = subtitle.rectTransform;
        rect.anchorMin = new Vector2(.15f, 0); rect.anchorMax = new Vector2(.85f, 0);
        rect.pivot = new Vector2(.5f, 0); rect.anchoredPosition = new Vector2(0, 44); rect.sizeDelta = new Vector2(0, 100);
        subtitle.color = new Color(1, 1, 1, 0);
        floorLabel = MakeText("FloorIndicator", root.transform, 24);
        floorLabel.alignment = TextAlignmentOptions.TopRight;
        rect = floorLabel.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-32, -28); rect.sizeDelta = new Vector2(200, 50);
        SetFloor(0);
    }

    static TextMeshProUGUI MakeText(string name, Transform parent, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        if (GameUiTheme.Current != null && GameUiTheme.Current.textFont != null) text.font = GameUiTheme.Current.textFont;
        text.fontSize = size; text.enableAutoSizing = false; text.raycastTarget = false;
        text.color = Color.white; text.richText = false;
        return text;
    }

    public void SetFloor(int floor) { if (floorLabel != null) floorLabel.text = "F" + Mathf.Max(0, floor); }
    public void Enqueue(SubtitleCue cue)
    {
        if (cue == null || string.IsNullOrWhiteSpace(cue.text)) return;
        queue.Enqueue(new SubtitleCue(cue.text, Mathf.Max(.1f, cue.duration)));
    }
    public void Show(string text, float duration = 4) => Enqueue(new SubtitleCue(text, duration));
    public void Replace(IEnumerable<SubtitleCue> cues)
    {
        Clear();
        if (cues != null) foreach (var cue in cues) Enqueue(cue);
    }
    public void Clear()
    {
        queue.Clear(); current = null; elapsed = 0;
        if (subtitle != null) { subtitle.text = ""; subtitle.color = new Color(1, 1, 1, 0); }
    }
    void Update()
    {
        GameUiTheme.ApplySceneFonts();
        bool visible = controller != null && controller.isActiveAndEnabled && !controller.InputSuspended;
        canvas.enabled = visible;
        if (!visible || Time.timeScale <= 0) return;
        if (current == null && queue.Count > 0)
        {
            current = queue.Dequeue(); elapsed = 0; subtitle.text = current.text;
        }
        if (current == null) return;
        elapsed += Time.deltaTime;
        float fade = Mathf.Max(.01f, fadeTime), end = current.duration + fade * 2;
        float alpha = Mathf.SmoothStep(0, 1, Mathf.Min(elapsed / fade, (end - elapsed) / fade));
        subtitle.color = new Color(1, 1, 1, alpha);
        if (elapsed >= end) { current = null; subtitle.text = ""; }
    }
    void OnDisable() { if (canvas != null) canvas.enabled = false; }
    void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
}
