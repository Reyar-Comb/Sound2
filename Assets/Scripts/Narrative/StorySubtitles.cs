using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Queues typewriter subtitles above gameplay and menus independently of player input.</summary>
[AddComponentMenu("")]
public sealed class StorySubtitles : MonoBehaviour
{
    public const float DefaultCharactersPerSecond = 22;
    const int OverlaySortingOrder = 32001;

    static StorySubtitles instance;
    readonly Queue<(string text, float hold, float speed)> pending = new();
    (string text, float hold, float speed) active;
    Canvas canvas;
    TextMeshProUGUI subtitle;
    int characterCount;
    float elapsed;

    public static bool IsPlaying => instance != null && (instance.active.text != null || instance.pending.Count > 0);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetInstance() => instance = null;

    /// <summary>Queues a line. Hold time begins once the entire line is visible.</summary>
    public static void Play(string text, float holdSeconds = 4, float charactersPerSecond = DefaultCharactersPerSecond)
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(text)) return;
        if (instance == null)
        {
            var overlay = SubtitleHud.CreateCanvas("StorySubtitleHUD", OverlaySortingOrder);
            DontDestroyOnLoad(overlay.gameObject);
            instance = overlay.gameObject.AddComponent<StorySubtitles>();
        }

        float hold = float.IsFinite(holdSeconds) ? Mathf.Max(.1f, holdSeconds) : 4;
        float speed = float.IsFinite(charactersPerSecond) ? Mathf.Max(1, charactersPerSecond) : DefaultCharactersPerSecond;
        instance.pending.Enqueue((text, hold, speed));
        instance.enabled = true;
    }

    public static void Play(IEnumerable<SubtitleCue> lines, float charactersPerSecond = DefaultCharactersPerSecond)
    {
        if (lines == null) return;
        foreach (var line in lines)
            if (line != null) Play(line.text, line.duration, charactersPerSecond);
    }

    /// <summary>Dismisses the current story line and any queued story lines.</summary>
    public static void Clear()
    {
        if (instance == null) return;
        instance.pending.Clear();
        instance.active = default;
        instance.Hide();
    }

    void Awake()
    {
        canvas = GetComponent<Canvas>();
        subtitle = SubtitleHud.CreateSubtitle(transform, 24);
        Hide();
    }

    void Update()
    {
        if (active.text == null)
        {
            active = pending.Dequeue();
            elapsed = 0;
            canvas.enabled = true;
            subtitle.text = active.text;
            subtitle.maxVisibleCharacters = 0;
            subtitle.ForceMeshUpdate();
            characterCount = subtitle.textInfo.characterCount;
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        subtitle.maxVisibleCharacters = Mathf.FloorToInt(Mathf.Min(characterCount, elapsed * active.speed));
        if (elapsed < characterCount / active.speed + active.hold) return;
        active = default;
        if (pending.Count == 0) Hide();
    }

    void Hide()
    {
        subtitle.text = "";
        canvas.enabled = false;
        enabled = false;
    }
}
