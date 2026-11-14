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
    public bool IsLoopPlaying => loop && playback != null && double.IsPositiveInfinity(playback.ContentEndDspTime);
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
        if (pendingAutoStart && !SonicSpaceStartMenu.BlocksGameplay && analysis != null && analysis.IsReady)
            if (TriggerAt(emissionOrigin != null ? emissionOrigin.position : transform.position)) pendingAutoStart = false;
        if (!SonicSpaceStartMenu.BlocksGameplay && Keyboard.current != null && triggerKey != Key.None
            && Keyboard.current[triggerKey].wasPressedThisFrame) Trigger();
    }
    [ContextMenu("Trigger audio reactive sonar (Play mode)")]
    public void Trigger() => TriggerAt(emissionOrigin != null ? emissionOrigin.position : transform.position);
    public bool TriggerAt(Vector3 position, float strength = 1)
    {
        if (!Application.isPlaying || !isActiveAndEnabled || AudioSettings.dspTime < nextTrigger || strength <= 0 || !Preload()) return false;
        if (field == null || !field.isActiveAndEnabled) field = AudioReactiveSonarField.GetOrCreate();
        if (loop && IsLoopPlaying) playback.Stop();
        if (!field.Emit(position, analysis, visual, AudioPool, volume, strength, loop, out playback)) return false;
        pendingAutoStart = false; nextTrigger = AudioSettings.dspTime + Mathf.Max(0, cooldown); Triggers++; return true;
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
    public void Stop() { pendingAutoStart = false; playback?.Stop(); playback = null; }
    void OnDisable() { pendingAutoStart = false; if (pool != null) pool.StopAll(); playback = null; }
}
