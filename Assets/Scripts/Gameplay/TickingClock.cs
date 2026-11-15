using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent, RequireComponent(typeof(SonarObjectEmitter))]
public sealed class TickingClock : MonoBehaviour
{
    [System.Serializable]
    private sealed class TickRecording
    {
        [InspectorName("录音")] public AudioClip clip;
        [InspectorName("音量"), Range(0, 1)] public float volume = 1;
    }

    public Transform longHand;
    public Transform shortHand;
    [Min(.05f), Tooltip("Seconds between mechanical ticks. Each tick advances the long hand by 30 degrees.")]
    public float tickInterval = 1;
    public bool running = true;
    [Range(0, 11)] public int initialLongStep;
    [Range(0, 11)] public int initialShortStep = 3;
    [Header("滴答声")]
    [SerializeField, InspectorName("滴答录音列表"), Tooltip("每次滴答只播放下一条有效录音，播完列表后回到开头。每条音量乘以 SonarObjectEmitter 的 Volume。")]
    private TickRecording[] tickRecordings = System.Array.Empty<TickRecording>();
    public int LongStep { get; private set; }
    public int ShortStep { get; private set; }
    public int Ticks { get; private set; }
    SonarObjectEmitter emitter;
    float elapsed;
    int nextTickRecording;

    void Awake()
    {
        emitter = GetComponent<SonarObjectEmitter>();
        emitter.automatic = false;
        emitter.toggleKey = Key.None;
        LongStep = Mathf.Clamp(initialLongStep, 0, 11);
        ShortStep = Mathf.Clamp(initialShortStep, 0, 11);
        ApplyHands();
    }

    void OnEnable() => elapsed = 0;

    void Update()
    {
        if (!running || SonicSpaceStartMenu.BlocksGameplay) return;
        elapsed += Time.deltaTime;
        float interval = Mathf.Max(.05f, tickInterval);
        while (elapsed >= interval)
        {
            elapsed -= interval;
            Tick();
        }
    }

    [ContextMenu("Tick once (Play mode)")]
    public void Tick()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        LongStep = (LongStep + 1) % 12;
        if (LongStep == 0) ShortStep = (ShortStep + 1) % 12;
        Ticks++;
        ApplyHands();
        var recording = NextTickRecording();
        if (recording == null) emitter.EmitNow();
        else emitter.EmitWithClip(emitter.range, recording.clip, recording.volume);
    }

    private TickRecording NextTickRecording()
    {
        int count = tickRecordings.Length;
        if (count == 0) return null;
        nextTickRecording %= count;
        for (int i = 0; i < count; i++)
        {
            var recording = tickRecordings[nextTickRecording];
            nextTickRecording = (nextTickRecording + 1) % count;
            if (recording != null && recording.clip != null) return recording;
        }
        return null;
    }

    void ApplyHands()
    {
        if (longHand != null) longHand.localRotation = Quaternion.Euler(0, 0, 30 * LongStep);
        if (shortHand != null) shortHand.localRotation = Quaternion.Euler(0, 0, 30 * ShortStep);
    }
}
