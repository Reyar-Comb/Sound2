using Sound2.LevelEditing;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeddingSpeakerSequence : MonoBehaviour
{
    const string CollectibleId = "wedding.speaker";

    [SerializeField] AudioClip loopClip;
    [SerializeField] AudioReactiveClip loopAnalysis;
    [SerializeField] AudioReactiveVisualSettings loopVisual = new();
    [SerializeField, Range(0, 1)] float volume = .65f;
    [SerializeField] SpatialAudioSettings loopSpatial = new()
    {
        distanceMode = SoundDistanceMode.Linear, minDistance = 3, maxDistance = 45
    };
    [SerializeField] Transform collectibleOrigin;
    [SerializeField] AudioClip collectibleLoopClip;
    [SerializeField] AudioReactiveClip collectibleAnalysis;
    [SerializeField] AudioReactiveVisualSettings collectibleVisual = new();
    [SerializeField, Range(0, 1)] float collectibleVolume = .75f;
    [SerializeField] SpatialAudioSettings collectibleSpatial = new()
    {
        distanceMode = SoundDistanceMode.Linear, minDistance = 1, maxDistance = 15
    };
    [SerializeField, Min(.2f)] float pulseInterval = 1.2f;
    [SerializeField, Min(1)] float pulseRange = 14;
    [SerializeField, Min(.1f)] float waveSpeed = 9;
    [SerializeField, Min(.05f)] float afterglow = .65f;

    LevelLayout level;
    LevelWorld world;
    SonarPulseField field;
    AudioSource leftSource, rightSource;
    SpatialAudioEmitter leftAudio, rightAudio;
    AudioReactiveSonarField.MovingLoop goldWave, whiteWave;
    bool goldActive;
    bool rightScheduled, leftScheduled;
    float nextPulse;

    void Awake()
    {
        level = GetComponentInParent<LevelLayout>();
        world = GetComponentInParent<LevelWorld>();
    }

    void Start()
    {
        if (loopClip != null)
        {
            var model = transform.Find("Model");
            leftSource = PrepareSpeaker("Left wedding speaker audio", model != null ? model : transform,
                new Vector3(0, 0, .273f), loopClip, volume, loopSpatial, out leftAudio);
        }
        if (collectibleLoopClip != null && collectibleOrigin != null)
            rightSource = PrepareSpeaker("Right wedding speaker audio", collectibleOrigin,
                Vector3.zero, collectibleLoopClip, collectibleVolume, collectibleSpatial, out rightAudio);
    }

    void OnEnable() => nextPulse = 0;

    void OnDisable()
    {
        if (leftSource != null) leftSource.Stop();
        if (rightSource != null) rightSource.Stop();
        goldWave?.Stop();
        whiteWave?.Stop();
        goldWave = null;
        whiteWave = null;
        rightScheduled = false;
        leftScheduled = false;
    }

    void Update()
    {
        if (leftAudio != null)
        {
            leftAudio.settings = loopSpatial;
            leftAudio.volume = volume;
        }
        if (rightAudio != null)
        {
            rightAudio.settings = collectibleSpatial;
            rightAudio.volume = collectibleVolume;
        }
        if (SonicSpaceStartMenu.BlocksGameplay || (level != null && level.IsPreparing)
            || (world != null && world.CurrentLevel != level)) return;

        bool collected = StoryRunProgress.Current.IsCollected(CollectibleId);
        if (collected != goldActive)
        {
            goldActive = collected;
            var previous = goldActive ? leftSource : rightSource;
            if (previous != null) previous.Stop();
            goldWave?.Stop();
            whiteWave?.Stop();
            goldWave = null;
            whiteWave = null;
            rightScheduled = false;
            leftScheduled = false;
            nextPulse = Time.time + .15f;
        }

        var active = goldActive ? rightSource : leftSource;
        if (goldActive)
        {
            if (rightSource == null || collectibleAnalysis == null || !collectibleAnalysis.IsReady
                || collectibleAnalysis.clip != rightSource.clip) return;
            if (!rightScheduled)
            {
                goldWave?.Stop();
                double start = AudioSettings.dspTime + .1;
                goldWave = AudioReactiveSonarField.GetOrCreate().StartMovingLoop(collectibleOrigin.position,
                    collectibleAnalysis, collectibleVisual, start, collectibleOrigin.parent, gold: true);
                if (goldWave != null)
                {
                    rightSource.PlayScheduled(start);
                    rightScheduled = true;
                }
            }
            goldWave?.Update(collectibleOrigin.position, 1);
            return;
        }
        if (leftSource != null && loopAnalysis != null && loopAnalysis.IsReady && loopAnalysis.clip == leftSource.clip)
        {
            if (!leftScheduled)
            {
                double start = AudioSettings.dspTime + .1;
                whiteWave = AudioReactiveSonarField.GetOrCreate().StartMovingLoop(leftSource.transform.position,
                    loopAnalysis, loopVisual, start, transform);
                if (whiteWave != null)
                {
                    leftSource.PlayScheduled(start);
                    leftScheduled = true;
                }
            }
            whiteWave?.Update(leftSource.transform.position, 1);
            return;
        }
        if (active != null && !active.isPlaying && active.clip.loadState == AudioDataLoadState.Loaded) active.Play();
        if (goldActive ? collectibleOrigin == null : leftSource == null) return;
        if (Time.time < nextPulse) return;
        if (field == null || !field.isActiveAndEnabled)
            field = world != null ? world.player?.GetComponentInChildren<SonarPulseField>() : FindAnyObjectByType<SonarPulseField>();
        if (field == null) return;

        nextPulse = Time.time + Mathf.Max(.2f, pulseInterval);
        var origin = goldActive ? collectibleOrigin.position : leftSource.transform.position;
        var kind = goldActive ? SonarPulseKind.Gold : SonarPulseKind.Main;
        field.EmitFrom(origin, pulseRange, waveSpeed, afterglow, kind, null, source: goldActive ? collectibleOrigin.parent : transform);
    }

    static AudioSource PrepareSpeaker(string name, Transform parent, Vector3 localPosition, AudioClip clip, float volume,
        SpatialAudioSettings settings, out SpatialAudioEmitter output)
    {
        var origin = new GameObject(name).transform;
        origin.SetParent(parent, false);
        origin.localPosition = localPosition;
        output = origin.gameObject.AddComponent<SpatialAudioEmitter>();
        output.settings = settings;
        output.volume = volume;
        output.Apply();
        var source = output.Source;
        source.clip = clip;
        source.loop = true;
        if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
        return source;
    }
}
