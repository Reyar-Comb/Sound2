using UnityEngine;

/// <summary>Shared audio feedback for patrol monsters and red vine areas. Never emits gameplay noise or sonar waves.</summary>
[DefaultExecutionOrder(-10), DisallowMultipleComponent]
public sealed class MonsterAudioEmitter : MonoBehaviour
{
    const string DefaultProfilePath = "MonsterAudio";
    static MonsterAudioProfile defaultProfile;
    static bool loadedDefault;

    [Tooltip("Leave empty to use the default MonsterAudio profile.")]
    public MonsterAudioProfile profile;
    public Transform listener;
    PatrolMonster monster;
    Sound2.LevelEditing.MonsterVineArea vineArea;
    AudioListener cachedListener;
    Vector3 previousPosition;
    bool moving;
    readonly Voice crawl = new Voice("Crawl"), hum = new Voice("Hum"), shout = new Voice("Shout");

    MonsterAudioProfile Profile => profile != null ? profile : GetDefault();
    public bool ReplacesScanSound => isActiveAndEnabled && monster != null && monster.isActiveAndEnabled
        && Profile != null && Profile.replaceScanSound && Profile.HasClips;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetDefault() { defaultProfile = null; loadedDefault = false; }

    static MonsterAudioProfile GetDefault()
    {
        if (!loadedDefault) { defaultProfile = Resources.Load<MonsterAudioProfile>(DefaultProfilePath); loadedDefault = true; }
        return defaultProfile;
    }

    internal static void AttachTo(PatrolMonster actor)
    {
        if (actor.GetComponent<MonsterAudioEmitter>() == null && GetDefault() != null)
            actor.gameObject.AddComponent<MonsterAudioEmitter>();
    }

    void Awake() { monster = GetComponent<PatrolMonster>(); vineArea = GetComponent<Sound2.LevelEditing.MonsterVineArea>(); }
    void OnEnable() { previousPosition = transform.position; moving = false; }
    void OnDisable() => StopVoices();
    void OnDestroy() { crawl.Destroy(); hum.Destroy(); shout.Destroy(); }
    void StopVoices() { crawl.Stop(); hum.Stop(); shout.Stop(); }
    public void ResetMotionAudio() { previousPosition = transform.position; moving = false; StopVoices(); }

    void LateUpdate()
    {
        Vector3 position = transform.position, motion = position - previousPosition;
        previousPosition = position;
        if (AudioListener.pause || Time.deltaTime <= 0) { moving = false; return; }
        var settings = Profile;
        if (monster == null && vineArea == null) { monster = GetComponent<PatrolMonster>(); vineArea = GetComponent<Sound2.LevelEditing.MonsterVineArea>(); }
        bool active = monster != null ? monster.isActiveAndEnabled : vineArea != null && vineArea.isActiveAndEnabled;
        if (settings == null || !active) { StopVoices(); return; }

        if (listener == null && (cachedListener == null || !cachedListener.isActiveAndEnabled))
            cachedListener = FindAnyObjectByType<AudioListener>();
        var ears = listener != null ? listener : cachedListener != null ? cachedListener.transform : null;
        if (ears == null) { StopVoices(); return; }

        float dt = Time.deltaTime;
        bool warped = motion.sqrMagnitude > 2 * 2; // Floor transitions / teleports are not a crawl step.
        if (vineArea == null) motion.y = 0;
        float threshold = Mathf.Max(.01f, settings.movementThreshold) * (moving ? .6f : 1) * dt;
        moving = !warped && motion.sqrMagnitude > threshold * threshold;
        float distance = Vector3.Distance(ears.position, transform.TransformPoint(settings.localOrigin));
        crawl.Tick(this, settings, settings.crawl, ears, distance, moving, dt);
        hum.Tick(this, settings, settings.hum, ears, distance, true, dt);
        shout.Tick(this, settings, settings.shout, ears, distance, true, dt);
    }

    sealed class Voice
    {
        readonly string name;
        SpatialAudioEmitter emitter;
        AudioSource source;
        float gain;
        bool playing, paused;

        public Voice(string name) => this.name = name;

        public void Tick(MonsterAudioEmitter owner, MonsterAudioProfile profile, MonsterAudioProfile.Layer layer,
            Transform ears, float distance, bool enabled, float dt)
        {
            if (layer == null || layer.clip == null) { Stop(); return; }
            float target = enabled && layer.volume > 0 ? layer.GainAt(distance) : 0;
            float fadeSeconds = target > gain ? profile.fadeInSeconds : profile.fadeOutSeconds;
            gain = Mathf.MoveTowards(gain, target, dt / Mathf.Max(.01f, fadeSeconds));
            if (gain <= 0)
            {
                if (playing) { emitter.volume = 0; source.Pause(); playing = false; paused = true; }
                return;
            }

            if (source == null)
            {
                // All voices share one moving world position but never share the scan's AudioSource.
                var anchor = new GameObject("Monster Audio - " + name);
                anchor.transform.SetParent(owner.transform, false);
                anchor.transform.localPosition = profile.localOrigin;
                emitter = anchor.AddComponent<SpatialAudioEmitter>();
                source = emitter.Source;
                source.loop = true;
                // The controller already applies and smooths distance gain, just like HallHumEmitter.
                emitter.settings.distanceMode = SoundDistanceMode.Curve;
                emitter.settings.distanceCurve = null;
            }
            emitter.transform.localPosition = profile.localOrigin;
            emitter.listener = ears;
            emitter.settings.hrtf = profile.hrtf;
            emitter.settings.occlusion = profile.occlusion;
            emitter.settings.reflections = profile.reflections;
            emitter.settings.reflectionMix = profile.reflectionMix;
            emitter.settings.maxDistance = Mathf.Max(1, layer.range);
            emitter.volume = gain * Mathf.Clamp01(layer.volume);
            if (source.clip != layer.clip)
            {
                source.Stop(); source.clip = layer.clip; playing = paused = false;
            }
            if (playing) return;
            emitter.Apply();
            if (paused) source.UnPause(); else source.Play();
            playing = true; paused = false;
        }

        public void Stop()
        {
            gain = 0;
            if (source != null && (playing || paused)) { emitter.volume = 0; source.Stop(); }
            playing = paused = false;
        }

        public void Destroy() { if (emitter != null) Object.Destroy(emitter.gameObject); }
    }
}
