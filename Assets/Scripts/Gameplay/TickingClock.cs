using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent, RequireComponent(typeof(SonarObjectEmitter))]
public sealed class TickingClock : MonoBehaviour
{
    public Transform longHand;
    public Transform shortHand;
    [Min(.05f), Tooltip("Seconds between mechanical ticks. Each tick advances the long hand by 30 degrees.")]
    public float tickInterval = 1;
    public bool running = true;
    [Range(0, 11)] public int initialLongStep;
    [Range(0, 11)] public int initialShortStep = 3;
    public int LongStep { get; private set; }
    public int ShortStep { get; private set; }
    public int Ticks { get; private set; }
    SonarObjectEmitter emitter;
    float elapsed;

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
        emitter.EmitNow();
    }

    void ApplyHands()
    {
        if (longHand != null) longHand.localRotation = Quaternion.Euler(0, 0, 30 * LongStep);
        if (shortHand != null) shortHand.localRotation = Quaternion.Euler(0, 0, 30 * ShortStep);
    }
}
