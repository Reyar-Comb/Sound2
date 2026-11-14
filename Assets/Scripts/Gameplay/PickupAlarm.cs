using UnityEngine;

[DefaultExecutionOrder(-10), DisallowMultipleComponent, RequireComponent(typeof(PickupItem), typeof(TickingClock), typeof(SonarCueSource))]
public sealed class PickupAlarm : MonoBehaviour
{
    [Min(0), Tooltip("Armed once by the first successful pickup. Dropping or picking up again does not reset the delay.")]
    public float delayAfterPickup = 5;
    [Min(.05f)] public float ringInterval = .2f;
    [Min(1)] public float ringRange = 12;
    [Min(0)] public float hearingRange = 16;
    public AudioClip alarmClip;
    public bool Armed { get; private set; }
    public bool IsRinging { get; private set; }
    public float Remaining { get; private set; }
    public int Rings { get; private set; }
    PickupItem item;
    TickingClock clock;
    SonarObjectEmitter emitter;
    SonarCueSource cue;
    float nextRing;
    bool previousRunning;
    AudioClip previousClip;

    void Awake()
    {
        item = GetComponent<PickupItem>();
        clock = GetComponent<TickingClock>();
        emitter = GetComponent<SonarObjectEmitter>();
        cue = GetComponent<SonarCueSource>();
    }

    void OnEnable()
    {
        item.PickedUp += Arm;
        item.Broken += StopAlarm;
    }

    void OnDisable()
    {
        item.PickedUp -= Arm;
        item.Broken -= StopAlarm;
        StopAlarm();
    }

    void Arm()
    {
        if (Armed || item.IsBroken) return;
        Armed = true;
        Remaining = Mathf.Max(0, delayAfterPickup);
    }

    void Update()
    {
        if (!Armed || item.IsBroken || SonicSpaceStartMenu.BlocksGameplay) return;
        if (!IsRinging)
        {
            Remaining = Mathf.Max(0, Remaining - Time.deltaTime);
            if (Remaining > 0) return;
            previousRunning = clock.running;
            clock.running = false;
            IsRinging = true;
            GetComponent<AudioSource>().Stop();
            if (cue != null) { previousClip = cue.clip; cue.clip = alarmClip; }
            nextRing = Time.time;
        }
        if (Time.time < nextRing) return;
        nextRing = Time.time + Mathf.Max(.05f, ringInterval);
        emitter.EmitWithRange(ringRange);
        GameplayNoise.Emit(emitter.emissionOrigin != null ? emitter.emissionOrigin.position : transform.position,
            hearingRange, GameplayNoiseKind.Impact, this);
        Rings++;
    }

    void StopAlarm()
    {
        if (IsRinging)
        {
            GetComponent<AudioSource>().Stop();
            if (cue != null) cue.clip = previousClip;
            if (!item.IsBroken) clock.running = previousRunning;
        }
        IsRinging = false;
        if (item.IsBroken) { Armed = false; clock.running = false; }
    }
}
