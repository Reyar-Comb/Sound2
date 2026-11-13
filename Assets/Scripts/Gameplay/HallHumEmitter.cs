using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AudioSource))]
public class HallHumEmitter : MonoBehaviour
{
    public BouncePlaytest controls;
    public bool humEnabled = true;
    [Range(0, 1)] public float volume = 0.6f;
    [Range(50, 220)] public float carrierHz = 110;
    [Range(0.1f, 3)] public float lfoHz = 0.6f;
    [Range(0, 1)] public float lfoDepth = 0.4f;
    [Header("Direct hum: independent from impact sounds")]
    public bool roomReflections;
    [Range(0, 1), Tooltip("Additional opposite-ear gain when the hum is directly to one side. 1 = no extra separation.")]
    public float farEarGain = 0.12f;
    [Tooltip("X = distance in metres; Y = hum gain. This curve only affects the fixed hum.")]
    public AnimationCurve distanceCurve = CreateGentleDistanceCurve();
    public float CurrentDistance { get; private set; }
    public int FilterChannels => filterChannels;
    public bool IsPlaying => source != null && source.isPlaying;

    AudioSource source;
    SpatialAudioEmitter emitter;
    ModulatedToneSource tone;
    float smoothedVolume;
    int filterChannels => emitter != null ? emitter.FilterChannels : 0;

    public static AnimationCurve CreateGentleDistanceCurve()
    {
        var keys = new[] { new Keyframe(0, 1), new Keyframe(1, 1), new Keyframe(3, 0.85f),
            new Keyframe(6, 0.65f), new Keyframe(12, 0.4f), new Keyframe(20, 0.18f), new Keyframe(30, 0) };
        for (int i = 0; i < keys.Length; i++)
        {
            if (i > 0) keys[i].inTangent = (keys[i].value - keys[i - 1].value) / (keys[i].time - keys[i - 1].time);
            if (i + 1 < keys.Length) keys[i].outTangent = (keys[i + 1].value - keys[i].value) / (keys[i + 1].time - keys[i].time);
        }
        return new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
    }

    public float DistanceGain(float distance)
    {
        return SpatialAudioSettings.EvaluateCurve(distanceCurve, distance);
    }

    void Start()
    {
        emitter = GetComponent<SpatialAudioEmitter>();
        if (emitter == null) emitter = gameObject.AddComponent<SpatialAudioEmitter>();
        source = emitter.Source;
        source.volume = 0;
        emitter.volume = 0;
        tone = GetComponent<ModulatedToneSource>();
        if (tone == null) tone = gameObject.AddComponent<ModulatedToneSource>();
        ApplySettings();
        tone.Play();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) humEnabled = !humEnabled;
        if (emitter == null) return;
        ApplySettings();
        float target = humEnabled ? Mathf.Clamp01(volume) * DistanceGain(CurrentDistance) : 0;
        smoothedVolume = Mathf.Lerp(smoothedVolume, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime / .04f));
        // Preserve smoothing of the complete distance-dependent gain used by the original hum.
        emitter.volume = smoothedVolume;
        emitter.Apply();
    }

    void ApplySettings()
    {
        emitter.listener = controls != null ? controls.transform : null;
        var settings = emitter.settings;
        settings.hrtf = controls == null || controls.spatialize;
        settings.occlusion = controls != null && controls.simulateOcclusion;
        settings.reflections = roomReflections;
        settings.reflectionMix = roomReflections && (controls == null || controls.reverb)
            ? (controls != null ? controls.reverbLevel : .12f) : 0;
        settings.farEarGain = farEarGain;
        // The adapter smooths its own curve once; the reusable emitter stays at unity gain.
        settings.distanceMode = SoundDistanceMode.Curve;
        settings.distanceCurve = null;
        emitter.Apply();
        CurrentDistance = emitter.CurrentDistance;
        tone.carrierHz = carrierHz;
        tone.lfoHz = lfoHz;
        tone.lfoDepth = lfoDepth;
        tone.PublishParameters();
    }

    void OnDisable() { if (tone != null) tone.Stop(); }
    void OnEnable() { if (tone != null) tone.Play(); }
}
