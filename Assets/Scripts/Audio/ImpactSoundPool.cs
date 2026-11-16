using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    public bool initializeOnStart = true;
    public bool prewarmAcrossFrames;
    public int Sounds { get; private set; }
    public float LastDistance { get; private set; }
    public int VoiceCount => voices.Count;
    readonly List<SpatialAudioEmitter> voices = new List<SpatialAudioEmitter>();
    readonly List<double> reservedUntil = new List<double>();
    readonly List<Playback> tracked = new List<Playback>();
    float preparedPreRoll, preparedTail, preparedLeadSeconds;

    /// <summary>Optional DSP clock/stop ticket. Only the tracked overload allocates a ticket.</summary>
    public sealed class Playback
    {
        internal ImpactSoundPool owner;
        public AudioSource Source { get; internal set; }
        public double ScheduledDspTime { get; internal set; }
        public double ContentDspTime { get; internal set; }
        public double ContentEndDspTime { get; internal set; }
        public bool WasInterrupted { get; internal set; }
        internal void End(double time)
        {
            if (time < ContentEndDspTime) { ContentEndDspTime = System.Math.Max(ContentDspTime, time); WasInterrupted = true; }
        }
        public void Stop() { if (owner != null) owner.StopPlayback(this); }
    }
    Transform root;
    AudioClip preparedClip, preparedFrom;
    int stealIndex;

    int TargetVoiceCount => Mathf.Clamp(capacity, 1, 64);

    void Start()
    {
        if (initializeOnStart && !prewarmAcrossFrames) Initialize();
    }

    public void Initialize()
    {
        while (voices.Count < TargetVoiceCount) AddVoice();
    }

    void AddVoice()
    {
        if (root == null)
        {
            root = new GameObject(name + " Impact Audio Pool").transform;
            if (root.gameObject.scene != gameObject.scene) SceneManager.MoveGameObjectToScene(root.gameObject, gameObject.scene);
        }
        var go = new GameObject("Impact Voice " + voices.Count);
        go.transform.SetParent(root);
        var voice = go.AddComponent<SpatialAudioEmitter>();
        voice.settings = settings;
        voice.listener = listener;
        voice.volume = 0;
        voice.Apply();
        voices.Add(voice);
        reservedUntil.Add(0);
        tracked.Add(null);
    }

    void PrepareClip()
    {
        if (preparedFrom == clip && Mathf.Approximately(preparedPreRoll, preRoll) && Mathf.Approximately(preparedTail, tailSeconds)) return;
        // Stop voices before disposing a clip they may still reference.
        StopAll();
        if (preparedClip != null) Destroy(preparedClip);
        preparedFrom = null;
        preparedLeadSeconds = 0;
        preparedClip = null;
        if (clip == null) return;
        int lead = Mathf.CeilToInt(Mathf.Max(0, preRoll) * clip.frequency);
        int frames = lead + clip.samples + Mathf.CeilToInt(Mathf.Max(0, tailSeconds) * clip.frequency);
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
        preparedFrom = clip; preparedPreRoll = preRoll; preparedTail = tailSeconds;
        preparedLeadSeconds = (float)lead / clip.frequency;
    }

    public bool Preload(bool looping = false)
    {
        if (clip == null) return false;
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        if (clip.loadState != AudioDataLoadState.Loaded) return false;
        if (prewarmAcrossFrames && voices.Count < TargetVoiceCount) return false;
        Initialize(); if (looping) return true; PrepareClip(); return preparedClip != null;
    }

    public bool PlayAt(Vector3 position, float volume = 1) => PlayInternal(position, volume, false, false, out _);
    public bool PlayAt(Vector3 position, float volume, out Playback playback) => PlayInternal(position, volume, true, false, out playback);

    public bool PlayLoopAt(Vector3 position, float volume, out Playback playback) => PlayInternal(position, volume, true, true, out playback);

    internal bool PlayScheduledAt(Vector3 position, float volume, bool looping, double start, out Playback playback,
        bool allowSteal = true) => PlayInternal(position, volume, true, looping, out playback, start, allowSteal);

    bool PlayInternal(Vector3 position, float volume, bool track, bool looping, out Playback playback,
        double? scheduledStart = null, bool allowSteal = true)
    {
        playback = null;
        if (!isActiveAndEnabled || clip == null) return false;
        Initialize();
        if (!looping) PrepareClip();
        if (track && !looping && preparedClip == null) return false;
        int index = -1;
        for (int i = 0; i < voices.Count; i++)
            if (AudioSettings.dspTime >= reservedUntil[i] && !voices[i].Source.isPlaying) { index = i; break; }
        if (index < 0)
        {
            if (!allowSteal) return false;
            index = stealIndex++ % voices.Count;
        }
        tracked[index]?.End(AudioSettings.dspTime);
        tracked[index] = null;
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
        source.clip = looping ? clip : (preparedClip != null ? preparedClip : clip);
        source.loop = looping;
        source.pitch = 1;
        voice.ApplyBeforePlayback();
        LastDistance = voice.CurrentDistance;
        voice.SteamSource.distanceAttenuationValue = 1 / Mathf.Max(1, LastDistance);
        double start = scheduledStart ?? (AudioSettings.dspTime + Mathf.Max(0, preRoll));
        if (track)
        {
            playback = new Playback { owner = this, Source = source, ScheduledDspTime = start,
                ContentDspTime = start + (looping ? 0 : preparedLeadSeconds),
                ContentEndDspTime = looping ? double.PositiveInfinity : start + preparedLeadSeconds + clip.length };
            tracked[index] = playback;
        }
        reservedUntil[index] = looping ? double.PositiveInfinity : start + source.clip.length;
        source.PlayScheduled(start);
        Sounds++;
        return true;
    }

    // Allows replacement of the settings object and listener at runtime.
    void LateUpdate()
    {
        if (prewarmAcrossFrames && (initializeOnStart || clip != null) && voices.Count < TargetVoiceCount) AddVoice();
        foreach (var voice in voices)
        {
            voice.settings = settings;
            voice.listener = listener;
        }
    }

    void StopPlayback(Playback playback)
    {
        for (int i = 0; i < tracked.Count; i++)
            if (ReferenceEquals(tracked[i], playback))
            {
                playback.End(AudioSettings.dspTime); voices[i].Source.Stop(); reservedUntil[i] = 0; tracked[i] = null; return;
            }
    }

    public void StopAll()
    {
        for (int i = 0; i < voices.Count; i++)
        {
            tracked[i]?.End(AudioSettings.dspTime); tracked[i] = null;
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
