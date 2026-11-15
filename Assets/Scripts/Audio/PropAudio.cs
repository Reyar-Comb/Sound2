using UnityEngine;

/// <summary>Optional 3D cues for props that do not already own audio for those triggers.</summary>
[DisallowMultipleComponent]
public sealed class PropAudio : MonoBehaviour
{
    public enum PlaybackMode { Disabled, Periodic, Loop }

    [Header("One-shots")]
    [SerializeField] private AudioClip interactionClip;
    [SerializeField] private AudioClip impactClip;
    [SerializeField, Range(0, 1)] private float impactVolume = 1;
    [SerializeField] private AudioClip breakClip;
    [SerializeField, Range(0, 1)] private float breakVolume = 1;
    [SerializeField, Min(0)] private float minimumImpactSpeed = .6f;
    [SerializeField, Min(0)] private float impactCooldown = .15f;
    [SerializeField] private LayerMask impactLayers = ~0;

    [Header("Playback")]
    [SerializeField] private AudioClip playbackClip;
    [SerializeField] private PlaybackMode mode;
    [SerializeField, Min(.05f)] private float interval = 1;
    [SerializeField] private bool playOnStart;
    [SerializeField] private bool toggleOnInteract;

    [Header("Spatial audio")]
    [SerializeField] private Transform emissionOrigin;
    [SerializeField, Range(0, 1)] private float volume = .65f;
    [SerializeField] private SpatialAudioSettings spatial = new SpatialAudioSettings {
        distanceMode = SoundDistanceMode.Linear, minDistance = .5f, maxDistance = 8
    };

    private SpatialAudioEmitter oneShot;
    private SpatialAudioEmitter loop;
    private InteractableObject[] interactions;
    private float nextImpact;
    private float nextTick;
    private int lastInteractionFrame = -1;
    public bool IsPlaying { get; private set; }

    private void OnEnable()
    {
        interactions = GetComponents<InteractableObject>();
        foreach (var interaction in interactions) interaction.onInteract.AddListener(OnInteraction);
    }

    private void Start()
    {
        if (playOnStart) StartPlayback();
    }

    private void OnDisable()
    {
        foreach (var interaction in interactions)
            if (interaction != null) interaction.onInteract.RemoveListener(OnInteraction);
        StopPlayback();
        if (oneShot != null) oneShot.Source.Stop();
    }

    private void OnInteraction(PlayerInteraction player)
    {
        // The shared player can invoke both an action and a pickup on the same click.
        if (lastInteractionFrame == Time.frameCount) return;
        lastInteractionFrame = Time.frameCount;
        PlayInteraction();
        if (toggleOnInteract) TogglePlayback();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!isActiveAndEnabled || impactClip == null || Time.time < nextImpact
            || (impactLayers.value & (1 << collision.gameObject.layer)) == 0
            || collision.collider.GetComponentInParent<PlayerController>() != null
            || CollisionSonarEmitter.ImpactSpeed(collision) < minimumImpactSpeed) return;
        PlayImpact();
        nextImpact = Time.time + impactCooldown;
    }

    public void PlayInteraction() => PlayOneShot(interactionClip);
    public void PlayImpact() => PlayOneShot(impactClip, impactVolume);
    public void PlayBreak() => PlayOneShot(breakClip, breakVolume);

    public void StartPlayback()
    {
        if (!isActiveAndEnabled || IsPlaying || playbackClip == null || mode == PlaybackMode.Disabled) return;
        if (mode == PlaybackMode.Loop)
        {
            if (loop == null) loop = CreateVoice("Loop audio");
            loop.Apply();
            var source = loop.Source;
            source.clip = playbackClip;
            source.loop = true;
            source.Play();
        }
        else nextTick = Time.time;
        IsPlaying = true;
    }

    public void StopPlayback()
    {
        IsPlaying = false;
        if (loop != null) loop.Source.Stop();
    }

    public void TogglePlayback()
    {
        if (IsPlaying) StopPlayback();
        else StartPlayback();
    }

    private void Update()
    {
        if (!IsPlaying || mode != PlaybackMode.Periodic || Time.time < nextTick || SonicSpaceStartMenu.BlocksGameplay) return;
        PlayOneShot(playbackClip);
        nextTick = Time.time + Mathf.Max(.05f, interval);
    }

    private void PlayOneShot(AudioClip clip, float gain = 1)
    {
        if (clip == null || !isActiveAndEnabled) return;
        if (oneShot == null) oneShot = CreateVoice("One-shot audio");
        oneShot.PlayOneShot(clip, gain);
    }

    private SpatialAudioEmitter CreateVoice(string name)
    {
        var voice = new GameObject(name).AddComponent<SpatialAudioEmitter>();
        voice.transform.SetParent(emissionOrigin != null ? emissionOrigin : transform, false);
        voice.settings = spatial;
        voice.volume = volume;
        return voice;
    }
}
