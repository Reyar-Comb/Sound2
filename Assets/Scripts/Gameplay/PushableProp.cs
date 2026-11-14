using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SonarObjectEmitter), typeof(CollisionSonarEmitter))]
public sealed class PushableProp : MonoBehaviour
{
    [Header("Player pushing")]
    [Min(.1f)] public float maximumPushSpeed = 1.8f;
    [Min(1)] public float maximumPushForce = 140;
    [Min(.1f)] public float velocityResponse = 8;
    [Min(0)] public float stoppingResistance = 5;
    [Min(.02f)] public float contactMemory = .12f;

    [Header("Rolling / scraping noise")]
    public AudioClip movementClip;
    [Min(.05f)] public float movementInterval = .4f;
    [Min(.01f)] public float minimumSoundSpeed = .12f;
    [Min(.01f)] public float minimumSoundTravel = .06f;
    [Min(1)] public float movementPulseRange = 5;
    [Min(0)] public float movementHearingRange = 12;
    public int MovementEmissions { get; private set; }
    public int PushRequests { get; private set; }
    public bool Grounded => Time.fixedTime - lastGroundContact <= Time.fixedDeltaTime * 3;

    Rigidbody body;
    SonarObjectEmitter sonar;
    Vector3 desiredVelocity, pushPoint, previousPosition;
    float lastPush = float.NegativeInfinity, lastGroundContact = float.NegativeInfinity;
    float nextMovementSound, travel;

    void Awake() { body = GetComponent<Rigidbody>(); sonar = GetComponent<SonarObjectEmitter>(); }
    void OnEnable()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        previousPosition = body.position;
        lastPush = lastGroundContact = float.NegativeInfinity;
        travel = 0; nextMovementSound = Time.time;
    }

    public void RequestPush(Vector3 velocity, Vector3 point, Vector3 normal)
    {
        if (!isActiveAndEnabled || body == null || body.isKinematic || SonicSpaceStartMenu.BlocksGameplay
            || Mathf.Abs(Vector3.Dot(normal, Vector3.up)) > .5f) return;
        var inward = Vector3.ProjectOnPlane(-normal, Vector3.up).normalized;
        float speed = Mathf.Min(maximumPushSpeed, Vector3.Dot(velocity, inward));
        if (speed <= .01f) return;
        var request = Vector3.ProjectOnPlane(velocity, Vector3.up).normalized * speed;
        if (Time.time > lastPush || request.sqrMagnitude > desiredVelocity.sqrMagnitude)
        {
            desiredVelocity = request;
            pushPoint = point;
        }
        lastPush = Time.time; PushRequests++;
        body.WakeUp();
    }

    void FixedUpdate()
    {
        var displacement = Vector3.ProjectOnPlane(body.position - previousPosition, Vector3.up);
        previousPosition = body.position;
        if (body.isKinematic || SonicSpaceStartMenu.BlocksGameplay)
        { lastPush = float.NegativeInfinity; travel = 0; return; }

        var horizontal = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        if (Grounded)
        {
            if (Time.time - lastPush <= contactMemory)
            {
                var force = Vector3.ClampMagnitude((desiredVelocity - horizontal) * (body.mass * velocityResponse), maximumPushForce);
                var point = pushPoint; point.y = body.worldCenterOfMass.y;
                body.AddForceAtPosition(force, point, ForceMode.Force);
            }
            else body.AddForce(-horizontal * stoppingResistance, ForceMode.Acceleration);
        }

        float distance = displacement.magnitude;
        float speed = distance / Mathf.Max(.001f, Time.fixedDeltaTime);
        if (!Grounded || speed < minimumSoundSpeed || distance > Mathf.Max(.5f, maximumPushSpeed * Time.fixedDeltaTime * 4))
        { travel = 0; return; }
        travel += distance;
        if (travel < minimumSoundTravel || Time.time < nextMovementSound) return;
        sonar.EmitWithClip(movementPulseRange, movementClip);
        var origin = sonar.emissionOrigin != null ? sonar.emissionOrigin.position : body.worldCenterOfMass;
        GameplayNoise.Emit(origin, movementHearingRange, GameplayNoiseKind.Movement, this);
        MovementEmissions++;
        travel = 0; nextMovementSound = Time.time + movementInterval;
    }

    void OnCollisionEnter(Collision collision) => TrackGround(collision);
    void OnCollisionStay(Collision collision) => TrackGround(collision);
    void TrackGround(Collision collision)
    {
        if (!isActiveAndEnabled || collision.collider.GetComponentInParent<PlayerController>() != null) return;
        for (int i = 0; i < collision.contactCount; i++)
            if (Vector3.Dot(collision.GetContact(i).normal, Vector3.up) > .6f)
            { lastGroundContact = Time.fixedTime; return; }
    }
}
