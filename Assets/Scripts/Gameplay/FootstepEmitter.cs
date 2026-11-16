using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Converts actual horizontal travel into discrete steps; no input or character movement ownership.</summary>
[DisallowMultipleComponent]
public sealed class FootstepEmitter : MonoBehaviour
{
    public SonarPulseField pulseField;
    public bool emissionEnabled = true;
    [Header("Step timing")]
    [Min(.1f)] public float stepDistance = 1.5f;
    [Min(.05f)] public float minimumInterval = .18f;
    [Min(0)] public float minimumSpeed = .05f;
    [Tooltip("Require ground contact in free-flight mode too. Gravity-based players always suppress airborne steps.")]
    public bool requireGrounded;
    [Tooltip("Larger single-frame displacement resets the stride instead of emitting teleport footsteps.")]
    [Min(.1f)] public float teleportDistance = 2;
    [Header("Footstep pulse")]
    [Min(1)] public float range = 3;
    [Min(.1f)] public float waveSpeed = 8;
    [Min(.05f)] public float afterglow = .3f;
    [Tooltip("Height above the capsule base to avoid emitting from inside the floor.")]
    [Min(.01f)] public float originHeight = .12f;
    public SonarDistanceSettings distanceSettings = new SonarDistanceSettings {
        enabled = true, fadeStartDistance = .4f, fadeEndDistance = 3,
        falloffPower = 2, pulseStrength = .6f
    };
    [Header("AI hearing")]
    [Min(0)] public float hearingRange = 6;
    [Header("Footstep audio")]
    public bool soundEnabled = true;
    [Tooltip("Explicit audio output. No sound is played if this or the clip is unassigned.")]
    public AudioSource audioSource;
    [Tooltip("Single-step recordings played in list order, one per footstep pulse. Empty slots are skipped.")]
    public AudioClip[] clips;
    [Range(0, 1)] public float volume = .4f;
    [Header("Landing (shared footstep pulse channel)")]
    [Tooltip("Optional landing recording. When empty, uses the next clip in the footstep list.")]
    public AudioClip landingClip;
    [Min(1), Tooltip("Scales landing audio volume relative to a normal footstep.")]
    public float landingVolumeMultiplier = 1.35f;
    [Min(1), Tooltip("Scales landing pulse range and AI hearing range relative to footsteps.")]
    public float landingMultiplier = 2;
    public UnityEvent<Vector3> onStep = new UnityEvent<Vector3>();
    public UnityEvent<Vector3> onLand = new UnityEvent<Vector3>();
    public event Action<Vector3> Stepped;
    public event Action<Vector3> Landed;
    public int Steps { get; private set; }
    public int Landings { get; private set; }
    public Vector3 LastOrigin { get; private set; }

    PlayerController player;
    BouncePlaytest legacyPlayer;
    CharacterController controller;
    Vector3 previousPosition;
    float distanceSinceStep, nextStepTime;
    int clipIndex;
    int landingFrame = -1;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        legacyPlayer = GetComponent<BouncePlaytest>();
        controller = GetComponent<CharacterController>();
    }
    void OnEnable()
    {
        ResetStride();
        if (player != null) player.Landed += EmitLanding;
    }
    void OnDisable()
    {
        if (player != null) player.Landed -= EmitLanding;
    }
    void Start()
    {
        if (pulseField == null) pulseField = GetComponentInChildren<SonarPulseField>();
    }
    public void ResetStride()
    {
        previousPosition = transform.position;
        distanceSinceStep = 0;
        nextStepTime = Time.time;
    }

    public void RemapMotionHistory(Vector3 origin, Vector3 destination, Quaternion rotation)
    {
        previousPosition = destination + rotation * (previousPosition - origin);
    }

    void LateUpdate()
    {
        Vector3 position = transform.position;
        Vector3 displacement = position - previousPosition;
        previousPosition = position;
        bool movingPlayer = player != null ? player.isActiveAndEnabled && !player.InputSuspended
            : legacyPlayer != null && legacyPlayer.isActiveAndEnabled && legacyPlayer.InputCaptured;
        if (!emissionEnabled || !movingPlayer
            || (controller != null && !controller.enabled) || displacement.magnitude > Mathf.Max(.1f, teleportDistance)
            || landingFrame == Time.frameCount
            || ((requireGrounded || (player != null && player.UsesGravity)) && (controller == null || !controller.isGrounded)))
        {
            distanceSinceStep = 0;
            return;
        }
        float horizontalDistance = new Vector2(displacement.x, displacement.z).magnitude;
        if (horizontalDistance <= minimumSpeed * Mathf.Max(Time.deltaTime, .0001f)) return;
        float stride = Mathf.Max(.1f, stepDistance);
        distanceSinceStep = Mathf.Min(distanceSinceStep + horizontalDistance, stride * 2);
        if (distanceSinceStep < stride || Time.time < nextStepTime) return;
        distanceSinceStep %= stride; // One step per frame; never catch up with a burst after a hitch.
        nextStepTime = Time.time + Mathf.Max(.05f, minimumInterval);
        EmitStep();
    }

    void EmitStep()
    {
        LastOrigin = FootPosition();
        EmitPulse(1, distanceSettings);
        if (soundEnabled) PlayStepSound();
        GameplayNoise.Emit(LastOrigin, hearingRange, GameplayNoiseKind.Footstep, this);
        Steps++;
        Stepped?.Invoke(LastOrigin);
        onStep.Invoke(LastOrigin);
    }

    Vector3 FootPosition()
    {
        Vector3 basePosition = controller != null ? transform.TransformPoint(controller.center)
            - Vector3.up * (controller.height * Mathf.Abs(transform.lossyScale.y) * .5f)
            : transform.position - Vector3.up * 1.6f; // Legacy free camera has no capsule.
        return basePosition + Vector3.up * Mathf.Max(.01f, originHeight);
    }

    void EmitPulse(float multiplier, SonarDistanceSettings settings)
    {
        if (pulseField == null) pulseField = GetComponentInChildren<SonarPulseField>();
        if (pulseField != null)
            pulseField.EmitFootstepFrom(LastOrigin, range * multiplier, waveSpeed, afterglow, settings, transform);
    }

    void EmitLanding(float impactSpeed)
    {
        if (!emissionEnabled) return;
        ResetStride();
        landingFrame = Time.frameCount; // A landing must not also emit a normal stride this frame.
        LastOrigin = FootPosition();
        float multiplier = Mathf.Max(1, landingMultiplier);
        SonarDistanceSettings landingDistance = distanceSettings == null ? null : new SonarDistanceSettings {
            enabled = distanceSettings.enabled,
            fadeStartDistance = distanceSettings.fadeStartDistance * multiplier,
            fadeEndDistance = distanceSettings.fadeEndDistance * multiplier,
            falloffPower = distanceSettings.falloffPower,
            pulseStrength = Mathf.Clamp01(distanceSettings.pulseStrength * multiplier)
        };
        EmitPulse(multiplier, landingDistance);
        if (soundEnabled)
            PlayFootstepSound(landingClip, Mathf.Clamp01(volume * Mathf.Max(1, landingVolumeMultiplier)));
        GameplayNoise.Emit(LastOrigin, hearingRange * multiplier, GameplayNoiseKind.Impact, this);
        Landings++;
        Landed?.Invoke(LastOrigin);
        onLand.Invoke(LastOrigin);
    }

    void PlayStepSound() => PlayFootstepSound(null, volume);

    void PlayFootstepSound(AudioClip clip, float gain)
    {
        if (audioSource == null) return;
        if (clips != null)
            for (int i = 0; i < clips.Length && clip == null; i++)
            {
                clip = clips[clipIndex % clips.Length];
                clipIndex = (clipIndex + 1) % clips.Length;
            }
        if (clip != null) audioSource.PlayOneShot(clip, gain);
    }
}
