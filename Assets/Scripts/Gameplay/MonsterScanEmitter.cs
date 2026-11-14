using UnityEngine;

/// <summary>Distance-paced danger pulses through the same point lattice as every other sonar source.</summary>
[DisallowMultipleComponent]
public sealed class MonsterScanEmitter : MonoBehaviour
{
    public Transform player;
    public SonarPulseField pulseField;
    [Header("Scan frequency")]
    [Min(0)] public float nearDistance = 2;
    [Min(.1f)] public float farDistance = 16;
    [Min(.2f)] public float nearInterval = .65f;
    [Min(.2f)] public float farInterval = 3.5f;
    [Min(0)] public float firstDelay = .8f;
    [Header("Danger pulse")]
    public Vector3 localOrigin = new Vector3(0,1,0);
    [Min(1)] public float range = 14;
    [Min(.1f)] public float waveSpeed = 10;
    [Min(.05f)] public float afterglow = .8f;
    public SonarDistanceSettings distanceSettings = new SonarDistanceSettings {
        enabled = true, fadeStartDistance = 1, fadeEndDistance = 14, falloffPower = 1, pulseStrength = .9f
    };
    [Header("Point density limit")]
    public SonarDensitySettings densitySettings = new SonarDensitySettings {
        enabled=true, fadeStartDistance=6, fadeEndDistance=10, falloffPower=1
    };
    [Header("Scan sound")]
    public bool playSound = true;
    [Range(0,1)] public float volume = .22f;
    public int Scans { get; private set; }
    public float CurrentInterval => player != null ? IntervalAtDistance(Vector3.Distance(transform.position, player.position)) : farInterval;
    public Vector3 LastOrigin { get; private set; }
    float phase, startsAt;
    SpatialAudioEmitter spatial;
    SonarCueSource cue;
    void OnEnable() { phase = 1; startsAt = Time.time + firstDelay; }
    void Start()
    {
        if (player == null) { var p = FindAnyObjectByType<PlayerController>(); if (p != null) player = p.transform; }
        if (pulseField == null) pulseField = FindAnyObjectByType<SonarPulseField>();
        spatial = GetComponent<SpatialAudioEmitter>();
        if (spatial == null) spatial = gameObject.AddComponent<SpatialAudioEmitter>();
        spatial.settings.reflections = false;
        spatial.settings.distanceMode = SoundDistanceMode.Linear;
        spatial.settings.minDistance = 1; spatial.settings.maxDistance = range;
        spatial.volume = volume; spatial.Apply(); spatial.Source.pitch = .7f;
        cue = GetComponent<SonarCueSource>(); if (cue == null) cue = gameObject.AddComponent<SonarCueSource>();
        cue.output = spatial.Source;
    }
    public float IntervalAtDistance(float distance)
        => Mathf.Lerp(Mathf.Max(.2f,nearInterval), Mathf.Max(nearInterval,farInterval),
            Mathf.InverseLerp(nearDistance,Mathf.Max(nearDistance+.01f,farDistance),distance));
    void Update()
    {
        if (Time.time < startsAt) return;
        // Phase follows the current interval immediately when distance changes.
        phase += Time.deltaTime / Mathf.Max(.2f, CurrentInterval);
        if (phase < 1) return;
        phase %= 1; EmitNow();
    }
    [ContextMenu("Emit danger scan (Play mode)")]
    public bool EmitNow()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return false;
        if (pulseField == null) pulseField = FindAnyObjectByType<SonarPulseField>();
        if (pulseField == null) return false;
        LastOrigin = transform.TransformPoint(localOrigin);
        if (!pulseField.EmitDangerFrom(LastOrigin,range,waveSpeed,afterglow,distanceSettings,densitySettings)) return false;
        if (playSound && cue != null) { spatial.volume = volume; spatial.Apply(); cue.Play(); }
        Scans++; return true;
    }
}
