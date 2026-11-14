using UnityEngine;

/// <summary>Adds a small visual offset after look/movement and before the interaction ray is evaluated.</summary>
[DefaultExecutionOrder(20)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController), typeof(CharacterController))]
public sealed class PlayerCameraMotion : MonoBehaviour
{
    [Range(0, 1)] public float intensity = 1;
    [Header("Idle breathing")]
    [Min(0)] public float idleHeight = .006f;
    [Min(.5f)] public float idlePeriod = 5.5f;
    [Header("Walking (metres / degrees)")]
    [Min(0)] public float walkHeight = .022f;
    [Min(0)] public float walkSway = .009f;
    [Min(0)] public float walkPitch = .12f;
    [Min(0)] public float walkRoll = .18f;
    [Tooltip("Distance per step. Uses FootstepEmitter.stepDistance when that component is present.")]
    [Min(.1f)] public float stepDistance = 1.5f;
    [Min(.1f)] public float fullMotionSpeed = 4;
    [Header("Transitions")]
    [Min(.02f)] public float blendTime = .22f;
    [Min(.02f)] public float smoothingTime = .12f;
    [Min(.01f)] public float minimumMoveSpeed = .05f;
    [Min(.1f)] public float teleportDistance = 2;

    public Vector3 PositionOffset => offset;
    public Vector3 RotationOffset => angles;
    public float WalkingBlend => walking;

    PlayerController player;
    CharacterController capsule;
    FootstepEmitter footsteps;
    Transform view;
    Vector3 restPosition, previousPosition, offset, offsetVelocity, angles, angleVelocity;
    float idlePhase, stridePhase, walking;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        capsule = GetComponent<CharacterController>();
        footsteps = GetComponent<FootstepEmitter>();
        view = player.ViewTransform;
        if (view == null) { enabled = false; return; }
        restPosition = view.localPosition;
    }

    void OnEnable()
    {
        previousPosition = transform.position;
        idlePhase = stridePhase = walking = 0;
        offset = offsetVelocity = angles = angleVelocity = Vector3.zero;
    }

    void LateUpdate()
    {
        Vector3 displacement = transform.position - previousPosition;
        previousPosition = transform.position;
        if (view == null || Time.deltaTime <= 0) return;
        float dt = Time.deltaTime;
        bool active = player.isActiveAndEnabled && !player.InputSuspended && capsule.enabled;
        bool supported = !player.UsesGravity || capsule.isGrounded;
        bool teleported = displacement.magnitude > Mathf.Max(.1f, teleportDistance);
        float distance = new Vector2(displacement.x, displacement.z).magnitude;
        float speed = distance / dt;
        float targetWalk = active && supported && !teleported && speed > minimumMoveSpeed
            ? Mathf.Clamp01(speed / Mathf.Max(.1f, fullMotionSpeed)) : 0;
        walking = Mathf.Lerp(walking, targetWalk, 1 - Mathf.Exp(-dt / Mathf.Max(.02f, blendTime)));
        if (active && supported)
            idlePhase = Mathf.Repeat(idlePhase + dt * Mathf.PI * 2 / Mathf.Max(.5f, idlePeriod), Mathf.PI * 2);
        if (teleported) stridePhase = 0;
        else if (targetWalk > 0)
        {
            float stride = footsteps != null ? footsteps.stepDistance : stepDistance;
            // One up/down cycle per step; side-to-side sway alternates between the two feet.
            stridePhase = Mathf.Repeat(stridePhase + distance * Mathf.PI / Mathf.Max(.1f, stride), Mathf.PI * 2);
        }
        float strength = active && supported ? Mathf.Clamp01(intensity) : 0;
        float sway = Mathf.Sin(stridePhase), step = Mathf.Sin(stridePhase * 2);
        Vector3 targetOffset = new Vector3(sway * walkSway * walking,
            Mathf.Sin(idlePhase) * idleHeight * (1 - walking) + step * walkHeight * walking, 0) * strength;
        Vector3 targetAngles = new Vector3(step * walkPitch, 0, -sway * walkRoll) * (walking * strength);
        offset = Vector3.SmoothDamp(offset, targetOffset, ref offsetVelocity, Mathf.Max(.02f, smoothingTime), Mathf.Infinity, dt);
        angles = Vector3.SmoothDamp(angles, targetAngles, ref angleVelocity, Mathf.Max(.02f, smoothingTime), Mathf.Infinity, dt);
        view.localPosition = restPosition + offset;
        // Never multiply onto last frame's camera pose: aim and procedural motion cannot accumulate drift.
        view.localRotation = player.AimLocalRotation * Quaternion.Euler(angles);
    }

    void OnDisable()
    {
        if (view == null) return;
        view.localPosition = restPosition;
        view.localRotation = player.AimLocalRotation;
        offset = offsetVelocity = angles = angleVelocity = Vector3.zero;
        walking = 0;
    }
}
