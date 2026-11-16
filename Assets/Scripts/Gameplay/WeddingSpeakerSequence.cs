using Sound2.LevelEditing;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeddingSpeakerSequence : MonoBehaviour
{
    const string CollectibleId = "wedding.speaker";

    [SerializeField] AudioClip loopClip;
    [SerializeField, Range(0, 1)] float volume = .65f;
    [SerializeField] Transform collectibleOrigin;
    [SerializeField, Min(.2f)] float pulseInterval = 1.2f;
    [SerializeField, Min(1)] float pulseRange = 14;
    [SerializeField, Min(.1f)] float waveSpeed = 9;
    [SerializeField, Min(.05f)] float afterglow = .65f;

    LevelLayout level;
    LevelWorld world;
    SonarPulseField field;
    AudioSource leftSource;
    Transform leftOrigin;
    Vector3 goldOriginLocal;
    bool hasGoldOrigin, goldActive;
    float nextPulse;

    void Awake()
    {
        level = GetComponentInParent<LevelLayout>();
        world = GetComponentInParent<LevelWorld>();
        hasGoldOrigin = collectibleOrigin != null;
        if (hasGoldOrigin) goldOriginLocal = transform.InverseTransformPoint(collectibleOrigin.position);
    }

    void OnEnable() => nextPulse = 0;

    void OnDisable()
    {
        if (leftSource != null) leftSource.Stop();
    }

    void Update()
    {
        if (SonicSpaceStartMenu.BlocksGameplay || (level != null && level.IsPreparing)
            || (world != null && world.CurrentLevel != level)) return;

        bool collected = StoryRunProgress.Current.IsCollected(CollectibleId);
        if (collected != goldActive)
        {
            goldActive = collected;
            if (leftSource != null) leftSource.Stop();
            nextPulse = 0;
        }

        if (goldActive ? !hasGoldOrigin : loopClip == null) return;
        if (!goldActive && (leftSource == null || !leftSource.isPlaying)) PlayLeftSpeaker();
        if (Time.time < nextPulse) return;
        if (field == null || !field.isActiveAndEnabled)
            field = world != null ? world.player?.GetComponentInChildren<SonarPulseField>() : FindAnyObjectByType<SonarPulseField>();
        if (field == null) return;

        nextPulse = Time.time + Mathf.Max(.2f, pulseInterval);
        var origin = goldActive ? transform.TransformPoint(goldOriginLocal) : leftOrigin.position;
        var kind = goldActive ? SonarPulseKind.Gold : SonarPulseKind.Main;
        field.EmitFrom(origin, pulseRange, waveSpeed, afterglow, kind, null, source: goldActive ? null : transform);
    }

    void PlayLeftSpeaker()
    {
        if (leftSource == null)
        {
            leftOrigin = new GameObject("Wedding speaker audio").transform;
            var model = transform.Find("Model");
            leftOrigin.SetParent(model != null ? model : transform, false);
            leftOrigin.localPosition = new Vector3(0, 0, .3f);
            var output = leftOrigin.gameObject.AddComponent<SpatialAudioEmitter>();
            output.settings.distanceMode = SoundDistanceMode.Linear;
            output.settings.maxDistance = 18;
            output.volume = volume;
            output.Apply();
            leftSource = output.Source;
            leftSource.loop = true;
        }
        leftSource.clip = loopClip;
        leftSource.Play();
    }
}
