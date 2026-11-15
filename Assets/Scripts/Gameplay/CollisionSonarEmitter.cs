using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Triggers the existing object sonar on impacts, independently of pickup behaviour.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SonarObjectEmitter))]
public sealed class CollisionSonarEmitter : MonoBehaviour
{
    [Tooltip("Minimum relative speed into the contact surface, in metres per second.")]
    [Min(0f)] public float minimumImpactSpeed = .5f;
    [Tooltip("Minimum time between successful collision emissions.")]
    [Min(0f)] public float emissionInterval = .15f;
    public LayerMask collisionLayers = ~0;

    [Header("Impact range")]
    [Tooltip("Extra metres of sonar range per m/s above Minimum Impact Speed. The base range is on SonarObjectEmitter.")]
    [Min(0f)] public float rangePerSpeed = 2f;
    [Tooltip("Maximum collision sonar range, in metres. Never reduces the configured base range.")]
    [Min(1f)] public float maximumRange = 36f;

    [Header("AI hearing")]
    [Tooltip("Audible range at the base impact range; louder impacts scale this proportionally.")]
    [Min(0)] public float hearingRange = 20;

    [Header("普通撞击声")]
    [InspectorName("撞击录音"), Tooltip("留空时使用原有 SonarCueSource。粉碎录音在 PickupItem 上配置。")]
    public AudioClip impactClip;
    [InspectorName("撞击音量"), Range(0, 1), Tooltip("乘以 SonarObjectEmitter 的 Volume。")]
    public float impactVolume = 1;
    [InspectorName("落地音效冷却"), Min(0), Tooltip("首次落地后只抑制后续地面接触的撞击声音（秒）。0 关闭。")]
    public float landingAudioCooldown = .4f;

    public int Emissions { get; private set; }
    public float LastImpactSpeed { get; private set; }
    public float LastEmissionRange { get; private set; }

    SonarObjectEmitter sonar;
    float nextEmission;
    float nextLandingAudio;

    void Reset()
    {
        // Newly authored collision emitters should not also ping on a timer or keyboard input.
        var objectSonar = GetComponent<SonarObjectEmitter>();
        if (objectSonar == null) return;
        objectSonar.automatic = false;
        objectSonar.toggleKey = Key.None;
    }

    void Awake()
    {
        sonar = GetComponent<SonarObjectEmitter>();
        // Runtime-added components do not receive the editor's Reset callback.
        if (GetComponent<PickupItem>() != null) sonar.toggleKey = Key.None;
    }

    public float RangeForImpact(float impactSpeed)
    {
        float threshold = Mathf.Max(0, minimumImpactSpeed);
        if (impactSpeed < threshold) return 0;
        var source = sonar != null ? sonar : GetComponent<SonarObjectEmitter>();
        float baseRange = source != null ? Mathf.Max(1, source.range) : 1;
        float extraSpeed = Mathf.Max(0, impactSpeed - threshold);
        return Mathf.Min(baseRange + extraSpeed * Mathf.Max(0, rangePerSpeed), Mathf.Max(baseRange, maximumRange));
    }

    void OnCollisionEnter(Collision collision)
    {
        // Unity can send collision messages to disabled behaviours too.
        if (!isActiveAndEnabled || sonar == null || collision.contactCount == 0) return;
        float impactSpeed = ImpactSpeed(collision, out var impactNormal);
        if (TryGetComponent<PickupItem>(out var pickup) && pickup.TryBreak(collision, impactSpeed)) return;
        if (Time.time < nextEmission
            || (collisionLayers.value & (1 << collision.collider.gameObject.layer)) == 0) return;
        if ((TryGetComponent<PickupItem>(out var item) && item.IsHeld)
            || collision.collider.GetComponentInParent<PlayerController>() != null) return;

        if (impactSpeed < minimumImpactSpeed) return;

        float pulseRange = RangeForImpact(impactSpeed);
        EmitImpact(pulseRange, impactNormal);
        // AI hearing is a gameplay event, not contingent on a renderer or audio source being ready.
        GameplayNoise.Emit(collision.GetContact(0).point, hearingRange * pulseRange / Mathf.Max(1, sonar.range),
            GameplayNoiseKind.Impact, this);
        LastImpactSpeed = impactSpeed;
        LastEmissionRange = pulseRange;
        nextEmission = Time.time + emissionInterval;
        Emissions++;
    }

    void EmitImpact(float pulseRange, Vector3 impactNormal)
    {
        // Use the strongest contact so a glancing floor contact does not mute a wall hit.
        bool landing = landingAudioCooldown > 0 && Vector3.Dot(impactNormal, Vector3.up) >= .65f;
        if (landing && Time.time < nextLandingAudio)
        {
            sonar.EmitWithClip(pulseRange, null);
            return;
        }

        bool emitted = impactClip != null ? sonar.EmitWithClip(pulseRange, impactClip, impactVolume) : sonar.EmitWithRange(pulseRange);
        if (landing && emitted && sonar.playSound) nextLandingAudio = Time.time + landingAudioCooldown;
    }

    public static float ImpactSpeed(Collision collision) => ImpactSpeed(collision, out _);

    static float ImpactSpeed(Collision collision, out Vector3 impactNormal)
    {
        impactNormal = Vector3.zero;
        float speed = 0;
        Vector3 velocity = collision.relativeVelocity;
        for (int i = 0; i < collision.contactCount; i++)
        {
            Vector3 normal = collision.GetContact(i).normal;
            float contactSpeed = Mathf.Abs(Vector3.Dot(velocity, normal));
            if (contactSpeed <= speed) continue;
            speed = contactSpeed;
            impactNormal = normal;
        }
        return speed;
    }
}
