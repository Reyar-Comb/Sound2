using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

/// <summary>Emits from this object's position through the player's existing sonar renderer.</summary>
[RequireComponent(typeof(AudioSource))]
public sealed class SonarObjectEmitter : MonoBehaviour, ISerializationCallbackReceiver
{
    [Tooltip("Legacy player reference; existing scenes keep working.")]
    public SphericalSonar sonar;
    public SonarPulseField pulseField;
    public SonarPulseKind pulseKind = SonarPulseKind.Main;
    [Tooltip("Optional moving visual centre; defaults to this transform.")]
    public Transform emissionOrigin;
    public SonarDensitySettings densitySettings = new SonarDensitySettings();
    public event System.Action Emitted;
    [Tooltip("Set to None when another component triggers EmitNow().")]
    public Key toggleKey = Key.C;
    public bool automatic = true;
    [Min(.2f)] public float interval = 3;
    [Min(0)] public float firstDelay = .6f;
    [Min(1)] public float range = 12;
    [Tooltip("Audible distance at the base sonar range. Scales proportionally for a per-impact range override.")]
    [Min(1)] public float soundRange = 16;
    [Min(.1f)] public float waveSpeed = 7;
    [Min(.05f)] public float afterglow = .7f;
    [Header("Pulse distance attenuation")]
    public SonarDistanceSettings distanceSettings = new SonarDistanceSettings();
    // Preserve existing scene/prefab values while moving the flat fields into a shared configuration.
    [SerializeField, HideInInspector] int distanceSettingsVersion;
    [SerializeField, HideInInspector, FormerlySerializedAs("distanceAttenuation")] bool legacyDistanceAttenuation;
    [SerializeField, HideInInspector, FormerlySerializedAs("fadeStartDistance")] float legacyFadeStart = .5f;
    [SerializeField, HideInInspector, FormerlySerializedAs("fadeEndDistance")] float legacyFadeEnd = 3;
    [SerializeField, HideInInspector, FormerlySerializedAs("falloffPower")] float legacyFalloffPower = 2;
    [SerializeField, HideInInspector, FormerlySerializedAs("pulseStrength")] float legacyStrength = .65f;

    // Compatibility API for scene tools and existing gameplay callers.
    public bool distanceAttenuation { get => distanceSettings.enabled; set => distanceSettings.enabled = value; }
    public float fadeStartDistance { get => distanceSettings.fadeStartDistance; set => distanceSettings.fadeStartDistance = value; }
    public float fadeEndDistance { get => distanceSettings.fadeEndDistance; set => distanceSettings.fadeEndDistance = value; }
    public float falloffPower { get => distanceSettings.falloffPower; set => distanceSettings.falloffPower = value; }
    public float pulseStrength { get => distanceSettings.pulseStrength; set => distanceSettings.pulseStrength = value; }

    public void OnBeforeSerialize() { distanceSettingsVersion = 1; }
    public void OnAfterDeserialize()
    {
        if (distanceSettingsVersion != 0) return;
        distanceSettings = new SonarDistanceSettings {
            enabled = legacyDistanceAttenuation, fadeStartDistance = legacyFadeStart,
            fadeEndDistance = legacyFadeEnd, falloffPower = legacyFalloffPower, pulseStrength = legacyStrength
        };
        distanceSettingsVersion = 1;
    }
    [Header("Emission audio")]
    public bool playSound = true;
    [Range(0, 1)] public float volume = .2f;
    public int Emissions { get; private set; }
    public Vector3 LastOrigin { get; private set; }
    public float LastEmissionRange { get; private set; }
    AudioSource source;
    SpatialAudioEmitter emitter;
    SonarCueSource cue;
    float nextEmission;
    bool initialized;

    void Start()
    {
        Initialize();
        nextEmission = Time.time + firstDelay;
    }

    void Initialize()
    {
        if (pulseField == null && sonar != null) pulseField = sonar.PulseField;
        if (pulseField == null) pulseField = FindAnyObjectByType<SonarPulseField>();
        if (initialized) return;
        emitter = GetComponent<SpatialAudioEmitter>();
        if (emitter == null) emitter = gameObject.AddComponent<SpatialAudioEmitter>();
        source = emitter.Source;
        source.loop = false;
        cue = GetComponent<SonarCueSource>();
        if (cue == null) cue = gameObject.AddComponent<SonarCueSource>();
        cue.output = source;
        emitter.settings.reflections = false;
        emitter.settings.distanceMode = SoundDistanceMode.Linear;
        emitter.settings.minDistance = 1;
        emitter.settings.maxDistance = Mathf.Max(1, soundRange);
        emitter.volume = volume;
        emitter.Apply();
        initialized = true;
    }

    void Update()
    {
        if(SonicSpaceStartMenu.BlocksGameplay)return;
        if (Keyboard.current != null && toggleKey != Key.None && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            automatic = !automatic;
            if (automatic) nextEmission = Time.time;
        }
        if (automatic && Time.time >= nextEmission)
        {
            EmitNow();
            nextEmission = Time.time + Mathf.Max(.2f, interval);
        }
    }

    public void RestartSchedule(float delay = 0) => nextEmission = Time.time + Mathf.Max(0,delay);

    [ContextMenu("Emit sonar now (Play mode)")]
    public void EmitNow() => EmitWithRange(range);

    /// <summary>Emit one pulse with a range override; preserve the authored base range for later emissions.</summary>
    public bool EmitWithRange(float pulseRange)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return false;
        Initialize();
        if (pulseField == null || source == null) return false;
        pulseRange = Mathf.Max(1, pulseRange);
        Vector3 origin = emissionOrigin != null ? emissionOrigin.position : transform.position;
        if (!pulseField.EmitFrom(origin,pulseRange,waveSpeed,afterglow,pulseKind,distanceSettings,densitySettings)) return false;
        emitter.settings.maxDistance = Mathf.Max(1, soundRange) * pulseRange / Mathf.Max(1, range);
        emitter.volume = volume;
        emitter.Apply();
        if (playSound) cue.Play();
        LastOrigin = origin;
        LastEmissionRange = pulseRange;
        Emissions++;
        Emitted?.Invoke();
        return true;
    }
}
