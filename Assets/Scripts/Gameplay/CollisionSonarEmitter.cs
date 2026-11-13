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

    public int Emissions { get; private set; }
    public float LastImpactSpeed { get; private set; }

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

        int before = sonar.Emissions;
        sonar.EmitNow();
        if (sonar.Emissions == before) return;
        LastImpactSpeed = impactSpeed;
        nextEmission = Time.time + emissionInterval;
        Emissions++;
    }
}
