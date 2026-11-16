using System.Collections.Generic;
using Sound2.LevelEditing;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Opt-in collision/interaction/API trigger. Owns a dedicated original spatial audio pool, not SonarObjectEmitter.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(ImpactSoundPool))]
public sealed class AudioReactiveSonarEmitter : MonoBehaviour
{
    public AudioReactiveClip analysis;
    public AudioReactiveVisualSettings visual = new AudioReactiveVisualSettings();
    public AudioReactiveSonarField field;
    public Transform emissionOrigin;
    [Tooltip("Additional wave origins use the same recording, timing, and Visual settings.")]
    public Transform[] additionalOrigins = System.Array.Empty<Transform>();
    [Tooltip("Also play spatial audio at each additional origin. Set the audio pool capacity to the number of origins.")]
    public bool playAudioAtAdditionalOrigins;
    public Key triggerKey = Key.None;
    public bool loop;
    public bool playOnStart;
    [Range(0, 1)] public float volume = .7f;
    public bool triggerOnCollision;
    [Min(0)] public float minimumImpactSpeed = .5f;
    [Min(0)] public float cooldown = .15f;
    public LayerMask collisionLayers = ~0;
    public int Triggers { get; private set; }
    ImpactSoundPool pool;
    AudioClip assignedClip;
    double nextTrigger;
    bool pendingAutoStart;
    ImpactSoundPool.Playback playback;
    readonly List<ImpactSoundPool.Playback> copyAudio = new();
    LevelLayout level;
    LevelWorld world;
    public bool IsLoopPlaying => loop && playback != null && double.IsPositiveInfinity(playback.ContentEndDspTime);
    void Awake()
    {
        level = GetComponentInParent<LevelLayout>();
        world = GetComponentInParent<LevelWorld>();
    }
    void OnEnable() { if (Application.isPlaying) pendingAutoStart = playOnStart; }
    public ImpactSoundPool AudioPool => pool != null ? pool : (pool = GetComponent<ImpactSoundPool>());

    void Start() => Preload();
    public bool Preload()
    {
        if (analysis == null || !analysis.IsReady) return false;
        var audio = AudioPool;
        // Do not take over a separately authored gameplay pool by accident.
        if (audio.clip != null && audio.clip != analysis.clip && audio.clip != assignedClip) return false;
        audio.clip = analysis.clip; assignedClip = analysis.clip;
        return audio.Preload(loop);
    }
    void Update()
    {
        if (pendingAutoStart && !SonicSpaceStartMenu.BlocksGameplay && analysis != null && analysis.IsReady
            && (level == null || !level.IsPreparing && (world == null || world.CurrentLevel == level)))
            if (TriggerAt(emissionOrigin != null ? emissionOrigin.position : transform.position)) pendingAutoStart = false;
        if (!SonicSpaceStartMenu.BlocksGameplay && Keyboard.current != null && triggerKey != Key.None
            && Keyboard.current[triggerKey].wasPressedThisFrame) Trigger();
    }
    [ContextMenu("Trigger audio reactive sonar (Play mode)")]
    public void Trigger() => TriggerAt(emissionOrigin != null ? emissionOrigin.position : transform.position);
    public void TriggerGold() => TriggerAt(emissionOrigin != null ? emissionOrigin.position : transform.position, 1, true);
    public bool TriggerAt(Vector3 position, float strength = 1) => TriggerAt(position, strength, false);
    bool TriggerAt(Vector3 position, float strength, bool gold)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || AudioSettings.dspTime < nextTrigger || strength <= 0 || !Preload()) return false;
        if (field == null || !field.isActiveAndEnabled) field = AudioReactiveSonarField.GetOrCreate();
        if (loop && IsLoopPlaying) Stop();
        bool hasCopies = additionalOrigins != null && additionalOrigins.Length != 0;
        double? scheduledStart = hasCopies ? AudioSettings.dspTime + Mathf.Max(.3f, AudioPool.preRoll) : null;
        var source = hasCopies && emissionOrigin != null ? emissionOrigin : transform;
        if (!field.Emit(position, analysis, visual, AudioPool, volume, strength, loop, out playback, gold, source, scheduledStart)) return false;
        if (hasCopies) EmitCopies(strength, gold);
        pendingAutoStart = false; nextTrigger = AudioSettings.dspTime + Mathf.Max(0, cooldown); Triggers++; return true;
    }

    void EmitCopies(float strength, bool gold)
    {
        copyAudio.RemoveAll(audio => audio.ContentEndDspTime <= AudioSettings.dspTime);
        foreach (var origin in additionalOrigins)
        {
            if (origin == null || !origin.gameObject.activeInHierarchy) continue;
            field.EmitCopy(origin.position, origin, analysis, visual, strength, loop, gold, playback);
            if (playAudioAtAdditionalOrigins && AudioPool.PlayScheduledAt(origin.position, volume, loop,
                playback.ScheduledDspTime, out var copy, allowSteal: false)) copyAudio.Add(copy);
        }
    }
    void OnCollisionEnter(Collision collision)
    {
        if (!isActiveAndEnabled || !triggerOnCollision || collision.contactCount == 0
            || (collisionLayers.value & (1 << collision.collider.gameObject.layer)) == 0
            || CollisionSonarEmitter.ImpactSpeed(collision) < minimumImpactSpeed) return;
        // Like the legacy ball, emit from the source centre, not exactly on/in the struck wall.
        TriggerAt(emissionOrigin != null ? emissionOrigin.position : transform.position);
    }
    public void Toggle() { if (IsLoopPlaying) Stop(); else Trigger(); }
    public void Stop()
    {
        pendingAutoStart = false;
        playback?.Stop(); playback = null;
        foreach (var copy in copyAudio) copy.Stop();
        copyAudio.Clear();
    }
    void OnDisable() { Stop(); if (pool != null) pool.StopAll(); }
}
