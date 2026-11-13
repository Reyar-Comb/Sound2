using System.Collections.Generic;
using UnityEngine;

/// <summary>Positioned transient voices with pre-roll and silence for Steam Audio reflection tails.</summary>
[DisallowMultipleComponent]
public sealed class ImpactSoundPool : MonoBehaviour
{
    public AudioClip clip;
    public Transform listener;
    public SpatialAudioSettings settings = new SpatialAudioSettings { reflections = true };
    [Range(1, 64)] public int capacity = 32;
    [Min(0)] public float preRoll = .025f;
    [Min(0)] public float tailSeconds = 2.25f;
    public int Sounds { get; private set; }
    public float LastDistance { get; private set; }
    public int VoiceCount => voices.Count;
    readonly List<SpatialAudioEmitter> voices = new List<SpatialAudioEmitter>();
    readonly List<double> reservedUntil = new List<double>();
    Transform root;
    AudioClip preparedClip, preparedFrom;
    int stealIndex;

    void Start() => Initialize();

    public void Initialize()
    {
        if (root != null) return;
        root = new GameObject(name + " Impact Audio Pool").transform;
        for (int i = 0; i < Mathf.Clamp(capacity, 1, 64); i++)
        {
            var go = new GameObject("Impact Voice " + i);
            go.transform.SetParent(root);
            var voice = go.AddComponent<SpatialAudioEmitter>();
            voice.settings = settings;
            voice.listener = listener;
            voice.volume = 0;
            voice.Apply();
            voices.Add(voice);
            reservedUntil.Add(0);
        }
    }

    void PrepareClip()
    {
        if (preparedFrom == clip) return;
        // Stop voices before disposing a clip they may still reference.
        StopAll();
        if (preparedClip != null) Destroy(preparedClip);
        preparedFrom = clip;
        preparedClip = null;
        if (clip == null) return;
        int lead = Mathf.CeilToInt(preRoll * clip.frequency);
        int frames = lead + clip.samples + Mathf.CeilToInt(tailSeconds * clip.frequency);
        var samples = new float[frames * clip.channels];
        var dry = new float[clip.samples * clip.channels];
        if (!clip.GetData(dry, 0))
        {
            Debug.LogError("Impact clip must support GetData (Decompress On Load).", this);
            return;
        }
        System.Array.Copy(dry, 0, samples, lead * clip.channels, dry.Length);
        preparedClip = AudioClip.Create("Impact with reflection tail", frames, clip.channels, clip.frequency, false);
        preparedClip.SetData(samples, 0);
    }

    public bool PlayAt(Vector3 position, float volume = 1)
    {
        if (!isActiveAndEnabled || clip == null) return false;
        Initialize();
        PrepareClip();
        int index = -1;
        for (int i = 0; i < voices.Count; i++)
            if (AudioSettings.dspTime >= reservedUntil[i] && !voices[i].Source.isPlaying) { index = i; break; }
        if (index < 0) index = stealIndex++ % voices.Count;
        var voice = voices[index];
        var source = voice.Source;
        source.Stop();
        // Reset spatial DSP history before teleporting a reused voice.
        source.spatialize = false;
        source.spatialize = true;
        voice.transform.position = position;
        voice.settings = settings;
        voice.listener = listener;
        voice.volume = volume;
        source.clip = preparedClip != null ? preparedClip : clip;
        source.pitch = 1;
        voice.Apply();
        LastDistance = voice.CurrentDistance;
        voice.SteamSource.distanceAttenuationValue = 1 / Mathf.Max(1, LastDistance);
        double start = AudioSettings.dspTime + preRoll;
        reservedUntil[index] = start + source.clip.length;
        source.PlayScheduled(start);
        Sounds++;
        return true;
    }

    // Allows replacement of the settings object and listener at runtime.
    void LateUpdate()
    {
        foreach (var voice in voices)
        {
            voice.settings = settings;
            voice.listener = listener;
        }
    }

    public void StopAll()
    {
        for (int i = 0; i < voices.Count; i++)
        {
            if (voices[i] != null) voices[i].Source.Stop();
            reservedUntil[i] = 0;
        }
    }
    void OnDisable() => StopAll();
    void OnDestroy()
    {
        StopAll();
        if (root != null) Destroy(root.gameObject);
        if (preparedClip != null) Destroy(preparedClip);
    }
}
