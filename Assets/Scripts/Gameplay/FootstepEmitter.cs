using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>Converts actual horizontal travel into discrete steps; no input or character movement ownership.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerController), typeof(CharacterController))]
public sealed class FootstepEmitter : MonoBehaviour
{
    public SonarPulseField pulseField;
    public bool emissionEnabled = true;
    [Header("Step timing")]
    [Min(.1f)] public float stepDistance = 1.5f;
    [Min(.05f)] public float minimumInterval = .18f;
    [Min(0)] public float minimumSpeed = .05f;
    [Tooltip("Enable for a grounded controller. Leave off for the current free vertical movement prototype.")]
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
    [Header("Optional recorded audio (silent until assigned)")]
    public AudioSource audioSource;
    public AudioClip[] clips;
    [Range(0, 1)] public float volume = .4f;
    public UnityEvent<Vector3> onStep = new UnityEvent<Vector3>();
    public event Action<Vector3> Stepped;
    public int Steps { get; private set; }
    public Vector3 LastOrigin { get; private set; }

    PlayerController player;
    CharacterController controller;
    Vector3 previousPosition;
    float distanceSinceStep, nextStepTime;
    int clipIndex;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        controller = GetComponent<CharacterController>();
    }
    void OnEnable() => ResetStride();
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

    void LateUpdate()
    {
        Vector3 position = transform.position;
        Vector3 displacement = position - previousPosition;
        previousPosition = position;
        if (!emissionEnabled || player == null || !player.isActiveAndEnabled || player.InputSuspended
            || !controller.enabled || displacement.magnitude > Mathf.Max(.1f, teleportDistance)
            || (requireGrounded && !controller.isGrounded))
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
        Vector3 basePosition = transform.TransformPoint(controller.center)
            - transform.up * (controller.height * Mathf.Abs(transform.lossyScale.y) * .5f);
        LastOrigin = basePosition + Vector3.up * Mathf.Max(.01f, originHeight);
        if (pulseField == null) pulseField = GetComponentInChildren<SonarPulseField>();
        if (pulseField != null)
            pulseField.EmitFootstepFrom(LastOrigin, range, waveSpeed, afterglow, distanceSettings);
        if (audioSource != null && clips != null && clips.Length > 0)
        {
            var clip = clips[clipIndex++ % clips.Length];
            if (clip != null) audioSource.PlayOneShot(clip, volume);
        }
        Steps++;
        Stepped?.Invoke(LastOrigin);
        onStep.Invoke(LastOrigin);
    }
}
