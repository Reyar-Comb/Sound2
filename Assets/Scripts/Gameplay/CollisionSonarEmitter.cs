using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Triggers the existing object sonar on impacts, independently of pickup behaviour.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SonarObjectEmitter))]
// random
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

    public int Emissions { get; private set; }
    public float LastImpactSpeed { get; private set; }
    public float LastEmissionRange { get; private set; }

    SonarObjectEmitter sonar;
    float nextEmission;

    void Reset()
    {
        // Newly authored collision emitters should not also ping on a timer or keyboard input.
        var objectSonar = GetComponent<SonarObjectEmitter>();
        if (objectSonar == null) return;
        objectSonar.automatic = false;
        objectSonar.toggleKey = Key.None;
    }

    void Awake() => sonar = GetComponent<SonarObjectEmitter>();

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
        if (!isActiveAndEnabled || sonar == null || !sonar.isActiveAndEnabled
            || Time.time < nextEmission || collision.contactCount == 0
            || (collisionLayers.value & (1 << collision.collider.gameObject.layer)) == 0) return;

        float impactSpeed = 0;
        for (int i = 0; i < collision.contactCount; i++)
            impactSpeed = Mathf.Max(impactSpeed,
                Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
        if (impactSpeed < minimumImpactSpeed) return;

        float pulseRange = RangeForImpact(impactSpeed);
        if (!sonar.EmitWithRange(pulseRange)) return;
        GameplayNoise.Emit(collision.GetContact(0).point, hearingRange * pulseRange / Mathf.Max(1, sonar.range),
            GameplayNoiseKind.Impact, this);
        LastImpactSpeed = impactSpeed;
        LastEmissionRange = pulseRange;
        nextEmission = Time.time + emissionInterval;
        Emissions++;
    }
}
