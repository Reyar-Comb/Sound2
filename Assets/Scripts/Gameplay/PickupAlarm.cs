using UnityEngine;

[DefaultExecutionOrder(-10), DisallowMultipleComponent, RequireComponent(typeof(PickupItem), typeof(TickingClock))]
public sealed class PickupAlarm : MonoBehaviour
{
    [Min(0), Tooltip("Armed once by the first successful pickup. Dropping or picking up again does not reset the delay.")]
    public float delayAfterPickup = 5;
    [Min(.05f), Tooltip("响铃期间 AI 噪声的最短间隔；声波由录音振幅驱动。")]
    public float ringInterval = .2f;
    [Min(1)] public float ringRange = 12;
    [Min(0)] public float hearingRange = 16;
    [Tooltip("停针后持续循环的完整响铃录音。需配套 Alarm Analysis；留空时不播放响铃，也不发出响铃声波。")]
    public AudioClip alarmClip;
    [Range(0, 1)] public float alarmVolume = .75f;
    [Min(1)] public float alarmSoundRange = 16;
    [Header("响铃声波（跟随录音振幅）")]
    public AudioReactiveClip alarmAnalysis;
    public AudioReactiveVisualSettings alarmVisual = new() { afterglow = .08f, peakFloor = .04f };
    public bool Armed { get; private set; }
    public bool IsRinging { get; private set; }
    public float Remaining { get; private set; }
    public int Rings { get; private set; }
    PickupItem item;
    TickingClock clock;
    SonarObjectEmitter emitter;
    SpatialAudioEmitter alarmAudio;
    AudioReactiveSonarField.MovingLoop alarmWave;
    double alarmDspStart;
    bool AnalysisReady => alarmAnalysis != null && alarmAnalysis.IsReady && alarmAnalysis.clip == alarmClip;
    float nextRing;
    bool previousRunning;

    void Awake()
    {
        item = GetComponent<PickupItem>();
        clock = GetComponent<TickingClock>();
        emitter = GetComponent<SonarObjectEmitter>();
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
            StartAlarmAudio();
            nextRing = Time.time;
        }
        if (alarmAudio == null || !AnalysisReady) return;
        alarmVisual.range = ringRange;
        if (alarmWave == null || !alarmWave.IsActive)
            alarmWave = AudioReactiveSonarField.GetOrCreate().StartMovingLoop(transform.position,
                alarmAnalysis, alarmVisual, alarmDspStart, transform);
        alarmWave?.Update(transform.position, 1);
        if (Time.time < nextRing || AudioSettings.dspTime < alarmDspStart) return;
        nextRing = Time.time + Mathf.Max(.05f, ringInterval);
        // Silent gaps send no AI noise either. Existing emitted wavefronts still travel naturally.
        if (alarmVisual.MapPeak(alarmAnalysis.Evaluate(alarmAudio.Source.time, 0)) <= 0) return;
        GameplayNoise.Emit(emitter.emissionOrigin != null ? emitter.emissionOrigin.position : transform.position,
            hearingRange, GameplayNoiseKind.Impact, this);
        Rings++;
    }

    void StartAlarmAudio()
    {
        if (alarmClip == null) return;
        if (alarmAudio == null)
        {
            var output = new GameObject("Looping alarm recording");
            output.transform.SetParent(transform, false);
            alarmAudio = output.AddComponent<SpatialAudioEmitter>();
        }
        alarmAudio.settings.distanceMode = SoundDistanceMode.Linear;
        alarmAudio.settings.minDistance = 1;
        alarmAudio.settings.maxDistance = Mathf.Max(1, alarmSoundRange);
        alarmAudio.volume = alarmVolume;
        alarmAudio.ApplyBeforePlayback();
        var source = alarmAudio.Source;
        source.clip = alarmClip;
        source.loop = true;
        alarmDspStart = AudioSettings.dspTime + .1;
        alarmVisual.range = ringRange;
        if (AnalysisReady)
            alarmWave = AudioReactiveSonarField.GetOrCreate().StartMovingLoop(transform.position,
                alarmAnalysis, alarmVisual, alarmDspStart, transform);
        else Debug.LogWarning("闹铃缺少匹配的振幅分析，请配置 Pickup Alarm / Alarm Analysis 并烘焙。", this);
        source.PlayScheduled(alarmDspStart);
    }

    void StopAlarm()
    {
        alarmWave?.Stop(); alarmWave = null;
        if (IsRinging)
        {
            if (alarmAudio != null) alarmAudio.Source.Stop();
            if (!item.IsBroken) clock.running = previousRunning;
        }
        IsRinging = false;
        if (item.IsBroken) { Armed = false; clock.running = false; }
    }
}
