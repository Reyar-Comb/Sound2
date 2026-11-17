using System.Collections.Generic;
using UnityEngine;

/// <summary>A scene prop that can be investigated after a sonar pulse reaches it.</summary>
[DisallowMultipleComponent]
public sealed class StoryCollectible : InteractableObject
{
    [SerializeField, Tooltip("Unique, stable ID for the future run or save record.")] private string collectibleId;
    [SerializeField] private MonoBehaviour progressProvider;
    [SerializeField] private Transform scanAnchor;
    [SerializeField, Min(.05f)] private float discoveryGlowSeconds = 1.5f;
    [SerializeField] private Renderer[] outlineRenderers;

    [SerializeField] private StorySubtitleTrigger storySubtitles;

    [Header("收集后的发声（均留空时常亮）")]
    [SerializeField] private AudioReactiveSonarEmitter recording;
    [SerializeField] private SonarObjectEmitter pulse;

    internal static readonly List<StoryCollectible> Active = new();
    public string CollectibleId => collectibleId;
    public bool Discovered { get; private set; }
    public bool Collected { get; private set; }
    public override string InteractionHint => sequence != null ? sequence.InteractionHint : "调查";
    internal Renderer[] OutlineRenderers => sequence != null && sequence.StowingMiniature ? sequence.MiniatureRenderers : outlineRenderers;
    internal float SurfaceGold => sequence != null && (sequence.KeepGold || sequence.StowingMiniature) ? 1
        : Collected ? (recording == null && pulse == null && (sequence == null || !sequence.KeepsSceneProp) ? 1 : 0)
        : Mathf.Clamp01((glowUntil - Time.time) / Mathf.Max(.05f, discoveryGlowSeconds));

    ICollectibleProgress progress;
    ICollectibleProgress Progress => progress ??= progressProvider as ICollectibleProgress ?? StoryRunProgress.Current;
    CollectionItemSequence sequence;
    Collider[] scanColliders;
    SonarPulseField scheduledField;
    int scheduledSlot;
    uint scheduledToken;
    float arrivalTime, glowUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => Active.Clear();

    void Awake()
    {
        sequence = GetComponent<CollectionItemSequence>();
        scanColliders = GetComponentsInChildren<Collider>(true);
        if (outlineRenderers == null || outlineRenderers.Length == 0) outlineRenderers = GetComponentsInChildren<Renderer>(true);
        BindProgress(Progress);
    }

    void OnEnable() => Active.Add(this);
    void OnDisable()
    {
        Active.Remove(this);
        scheduledField = null;
        glowUntil = 0;
        if (recording != null) recording.Stop();
    }

    public void BindProgress(ICollectibleProgress provider)
    {
        progress = provider;
        if (progress != null && !string.IsNullOrWhiteSpace(collectibleId) && progress.IsCollected(collectibleId))
        {
            Discovered = Collected = true;
            if (sequence != null && !sequence.KeepsSceneProp) gameObject.SetActive(false);
        }
    }

    internal void ContinueFrom(StoryCollectible source)
    {
        collectibleId = source.collectibleId;
        BindProgress(source.Progress);
        Discovered = true;
    }

    internal void PlayStory()
    {
        if (storySubtitles != null) storySubtitles.Trigger();
    }

    internal static void SchedulePulse(SonarPulseField source, int slot, uint token, Vector3 origin, float range, float speed)
    {
        foreach (var item in Active)
            if (!item.Collected && item.gameObject.scene == source.gameObject.scene)
                item.ScheduleArrival(source, slot, token, origin, range, speed);
    }

    void ScheduleArrival(SonarPulseField source, int slot, uint token, Vector3 origin, float range, float speed)
    {
        float distance = ScanDistance(source, origin, range);
        if (float.IsPositiveInfinity(distance)) return;
        float arrival = Time.time + distance / speed;
        if (scheduledField != null && scheduledField.IsPulseCurrent(scheduledSlot, scheduledToken) && arrivalTime <= arrival) return;
        scheduledField = source;
        scheduledSlot = slot;
        scheduledToken = token;
        arrivalTime = arrival;
    }

    internal float ScanDistance(SonarPulseField source, Vector3 origin, float range)
    {
        float distance = VisibleDistance(source, origin, scanAnchor != null ? scanAnchor.position : InteractionPosition, range);
        if (!float.IsPositiveInfinity(distance)) return distance;
        // A shelf can hide the interaction anchor while leaving another part of the item exposed.
        foreach (var collider in scanColliders)
            if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger)
                distance = Mathf.Min(distance, VisibleDistance(source, origin, collider.bounds.center, range));
        return distance;
    }

    float VisibleDistance(SonarPulseField source, Vector3 origin, Vector3 point, float range)
    {
        Vector3 delta = point - origin;
        float distance = delta.magnitude;
        if (distance > range) return float.PositiveInfinity;
        if (distance > .001f && source.TryGetPulseBlocker(origin, delta / distance, distance, out var hit))
            return hit.collider.transform.IsChildOf(transform) ? hit.distance : float.PositiveInfinity;
        return distance;
    }

    void Update()
    {
        if (scheduledField == null) return;
        if (!scheduledField.IsPulseCurrent(scheduledSlot, scheduledToken)) { scheduledField = null; return; }
        if (Time.time < arrivalTime) return;
        scheduledField = null;
        RevealFromSonar();
    }

    internal void RevealFromSonar()
    {
        if (Collected) return;
        Discovered = true;
        glowUntil = Time.time + Mathf.Max(.05f, discoveryGlowSeconds);
    }

    public override bool CanInteract(PlayerInteraction player) => base.CanInteract(player) && Discovered && !Collected
        && (sequence == null || sequence.CanInteract(player));

    public override bool TryInteract(PlayerInteraction player)
    {
        if (player == null || !CanInteract(player)) return false;
        if (sequence != null && sequence.RequiresShatter) return sequence.TryHold(player);
        if (!string.IsNullOrWhiteSpace(collectibleId) && !Progress.TryCollect(collectibleId))
        {
            Collected = Progress.IsCollected(collectibleId);
            return false;
        }
        return base.TryInteract(player);
    }

    protected override void OnInteract(PlayerInteraction player)
    {
        Collected = true;
        PlayStory();
        if (sequence != null) sequence.Store(player);
        if (recording != null) recording.TriggerGold();
        else if (pulse != null) pulse.EmitGold();
    }
}
