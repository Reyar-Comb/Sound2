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
    [Tooltip("Set to None when another component triggers EmitNow().")]
    public Key toggleKey = Key.C;
    public bool automatic = true;
    [Min(.2f)] public float interval = 3;
    [Min(0)] public float firstDelay = .6f;
    [Min(1)] public float range = 12;
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
    AudioSource source;
    SpatialAudioEmitter emitter;
    SonarCueSource cue;
    float nextEmission;

    void Start()
    {
        if (pulseField == null && sonar != null) pulseField = sonar.PulseField;
        if (pulseField == null) pulseField = FindAnyObjectByType<SonarPulseField>();
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
        emitter.settings.maxDistance = 16;
        emitter.volume = volume;
        emitter.Apply();
        nextEmission = Time.time + firstDelay;
    }

    void Update()
    {
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

    [ContextMenu("Emit sonar now (Play mode)")]
    public void EmitNow()
    {
        if (!Application.isPlaying || pulseField == null || source == null) return;
        emitter.volume = volume;
        emitter.Apply();
        bool emitted = pulseField.EmitFrom(transform.position, range, waveSpeed, afterglow, distanceSettings);
        if (emitted)
        {
            if (playSound) cue.Play();
            LastOrigin = transform.position;
            Emissions++;
        }
    }
}
