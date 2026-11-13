using System;
using UnityEngine;
using SteamAudio;
using Vector3 = UnityEngine.Vector3;

public enum SoundDistanceMode { Physics, Linear, Curve, DecibelsPerMetre }

/// <summary>Shared configuration for a single source or every voice in a pool.</summary>
[Serializable]
public sealed class SpatialAudioSettings
{
    public bool hrtf = true;
    public bool occlusion;
    public bool reflections;
    [Range(0, 2)] public float reflectionMix = .65f;
    public SoundDistanceMode distanceMode = SoundDistanceMode.Physics;
    [Min(.01f)] public float minDistance = 1;
    [Min(1)] public float maxDistance = 100;
    [Min(.01f)] public float decibelsPerMetre = 3;
    [Tooltip("X = world distance in metres; Y = gain, clamped to 0–1.")]
    public AnimationCurve distanceCurve = AnimationCurve.Linear(0, 1, 20, 0);
    [Range(0, 1), Tooltip("Extra opposite-ear gain after HRTF. 1 leaves HRTF unchanged.")]
    public float farEarGain = 1;

    public float GainAt(float distance)
    {
        distance = Mathf.Max(0, distance);
        switch (distanceMode)
        {
            case SoundDistanceMode.Curve:
                return EvaluateCurve(distanceCurve, distance);
            case SoundDistanceMode.DecibelsPerMetre:
                return Mathf.Pow(10, -Mathf.Max(.01f, decibelsPerMetre) * Mathf.Max(0, distance - minDistance) / 20);
            case SoundDistanceMode.Linear:
                return 1 - Mathf.InverseLerp(minDistance, Mathf.Max(minDistance + .01f, maxDistance), distance);
            default:
                return Mathf.Min(1, Mathf.Max(.01f, minDistance) / Mathf.Max(.01f, distance));
        }
    }

    public static float EvaluateCurve(AnimationCurve curve, float distance)
    {
        if (curve == null || curve.length == 0) return 1;
        return Mathf.Clamp01(curve.Evaluate(Mathf.Clamp(distance, curve[0].time, curve[curve.length - 1].time)));
    }
}

/// <summary>Owns spatialization and exactly one distance model. Does not choose sounds or gameplay triggers.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public sealed class SpatialAudioEmitter : MonoBehaviour
{
    public SpatialAudioSettings settings = new SpatialAudioSettings();
    [Tooltip("Optional explicit listener. Otherwise uses the active AudioListener.")]
    public Transform listener;
    [Range(0, 1)] public float volume = 1;
    public float CurrentDistance { get; private set; }
    public int FilterChannels => filterChannels;
    public AudioSource Source { get { Initialize(); return source; } }
    public SteamAudioSource SteamSource { get { Initialize(); return steamSource; } }

    AudioSource source;
    SteamAudioSource steamSource;
    AudioListener cachedListener;
    volatile float leftGain = 1, rightGain = 1;
    volatile int filterChannels;
    float smoothLeft = 1, smoothRight = 1;

    void Awake() => Initialize();

    void Initialize()
    {
        if (source != null) return;
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 1;
        source.spatialize = true;
        source.spatializePostEffects = false;
        source.dopplerLevel = 0;
        source.reverbZoneMix = 0;
        source.rolloffMode = AudioRolloffMode.Custom;
        source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0, 1, 1));
        steamSource = GetComponent<SteamAudioSource>();
        if (steamSource == null) steamSource = gameObject.AddComponent<SteamAudioSource>();
    }

    void LateUpdate() => Apply();

    /// <summary>Apply immediately before a transient, or after changing settings.</summary>
    public void Apply()
    {
        Initialize();
        if (listener == null && (cachedListener == null || !cachedListener.isActiveAndEnabled))
            cachedListener = FindAnyObjectByType<AudioListener>();
        Transform ears = listener != null ? listener :
            (cachedListener != null && cachedListener.isActiveAndEnabled ? cachedListener.transform : null);
        CurrentDistance = ears != null ? Vector3.Distance(transform.position, ears.position) : 0;
        source.minDistance = Mathf.Max(.01f, settings.minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + .01f, settings.maxDistance);
        steamSource.directBinaural = settings.hrtf;
        steamSource.applyHRTFToReflections = settings.hrtf;
        steamSource.occlusion = settings.occlusion;
        steamSource.reflections = settings.reflections;
        steamSource.reflectionsType = ReflectionsType.Realtime;
        steamSource.reflectionsMixLevel = settings.reflectionMix;
        // Physics attenuation is computed by Steam Audio. Other modes scale the
        // whole signal, including the wet tail, without a second inverse-distance gain.
        steamSource.distanceAttenuation = settings.distanceMode == SoundDistanceMode.Physics;
        steamSource.distanceAttenuationInput = DistanceAttenuationInput.PhysicsBased;
        source.volume = Mathf.Clamp01(volume) * (steamSource.distanceAttenuation ? 1 : settings.GainAt(CurrentDistance));
        leftGain = rightGain = 1;
        if (ears == null || settings.farEarGain >= 1) return;
        var horizontal = Vector3.ProjectOnPlane(transform.position - ears.position, ears.up);
        float side = horizontal.sqrMagnitude > .0001f ? Vector3.Dot(ears.right, horizontal.normalized) : 0;
        leftGain = side > 0 ? Mathf.Lerp(1, settings.farEarGain, side) : 1;
        rightGain = side < 0 ? Mathf.Lerp(1, settings.farEarGain, -side) : 1;
    }

    public void PlayOneShot(AudioClip clip, float gain = 1)
    {
        if (clip == null || !isActiveAndEnabled) return;
        Apply();
        source.PlayOneShot(clip, Mathf.Clamp01(gain));
    }

    // Audio thread: exchange numeric values only, with no Unity object access.
    void OnAudioFilterRead(float[] data, int channels)
    {
        filterChannels = channels;
        if (channels != 2) return;
        float left = leftGain, right = rightGain;
        for (int i = 0; i < data.Length; i += 2)
        {
            smoothLeft += (left - smoothLeft) * .002f;
            smoothRight += (right - smoothRight) * .002f;
            data[i] *= smoothLeft;
            data[i + 1] *= smoothRight;
        }
    }
}
