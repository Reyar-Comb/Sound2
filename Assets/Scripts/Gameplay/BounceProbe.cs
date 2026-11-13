using UnityEngine;

public class BounceProbe : MonoBehaviour
{
    public BouncePlaytest owner;
    // Zero means time limited; positive values count wall contacts instead.
    public int bounceLimit;
    public float soundScale = 1;
    public SpatialAudioEmitter flightEmitter;
    public AudioSource flightVoice => flightEmitter != null ? flightEmitter.Source : null;
    public int BounceCount { get; private set; }
    public bool Active => !finished;
    float lastImpact = -1;
    float bornAt;
    float finishAt;
    bool finished;

    void Awake() { bornAt = Time.time; }

    void Update()
    {
        if (owner == null) { Destroy(gameObject); return; }
        if (flightVoice == null) return;
        // Short envelopes prevent a hard cut through a nonzero sine sample.
        float envelope = finished ? Mathf.Clamp01((finishAt - Time.time) / 0.02f)
                                  : Mathf.Clamp01((Time.time - bornAt) / 0.02f);
        if (flightEmitter != null)
        {
            flightEmitter.volume = .65f * soundScale * envelope;
            flightEmitter.Apply();
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (finished || owner == null || collision.contactCount == 0 || Time.time - lastImpact < 0.04f) return;
        if (collision.collider.GetComponent<BounceProbe>() != null) return;
        var contact = collision.GetContact(0);
        float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
        lastImpact = Time.time;
        BounceCount++;
        owner.Impact(contact.point + contact.normal * 0.02f, speed, soundScale);
        if (bounceLimit <= 0 || BounceCount < bounceLimit) return;
        finished = true;
        if (flightVoice == null) { Destroy(gameObject); return; }
        GetComponent<Renderer>().enabled = false;
        GetComponent<Collider>().enabled = false;
        var body = GetComponent<Rigidbody>();
        body.linearVelocity = Vector3.zero;
        body.isKinematic = true;
        finishAt = Time.time + 0.02f;
        Destroy(gameObject, 0.02f);
    }
}
