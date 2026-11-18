using System.Collections.Generic;
using UnityEngine;

/// <summary>Final Boss keepsake, drawn with the shared occluded point pass, independent of sonar.</summary>
public sealed class BossVictoryRing : InteractableObject
{
    internal const int Capacity = 768;
    internal static readonly List<BossVictoryRing> Active = new();
    readonly List<(Transform transform, Vector3 position)> points = new();
    Renderer[] model;
    LevelArenaBoss owner;
    bool landed, collected, fallingVisible;
    float landedAt;
    Rigidbody fallBody;
    PhysicsMaterial landingMaterial;
    float fallGravity;
    internal bool FirstGroundContact { get; private set; }

    internal void BeginFall(float gravity, float bounceHeight, float fallHeight, float fallSeconds,
        float sideAngle, float spinDegrees)
    {
        fallingVisible = true;
        RefreshModelVisibility();
        transform.rotation *= Quaternion.Euler(0, 0, sideAngle);
        fallBody = GetComponent<Rigidbody>();
        if (fallBody == null) fallBody = gameObject.AddComponent<Rigidbody>();
        fallGravity = Mathf.Max(.1f, gravity);
        fallBody.isKinematic = false;
        fallBody.useGravity = false;
        fallBody.mass = .03f;
        fallBody.interpolation = RigidbodyInterpolation.Interpolate;
        fallBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        fallBody.linearDamping = .08f;
        fallBody.angularDamping = .65f;
        float seconds = Mathf.Max(.05f, fallSeconds);
        float downward = Mathf.Max(0, fallHeight / seconds - .5f * fallGravity * seconds);
        fallBody.linearVelocity = Vector3.down * downward + transform.right * .12f;
        fallBody.angularVelocity = transform.forward * (spinDegrees * Mathf.Deg2Rad);
        landingMaterial = new PhysicsMaterial("Boss ring landing")
        {
            bounciness = Mathf.Clamp(Mathf.Sqrt(Mathf.Max(0, bounceHeight) / Mathf.Max(.1f, fallHeight)), 0, .45f),
            dynamicFriction = .5f, staticFriction = .65f,
            bounceCombine = PhysicsMaterialCombine.Maximum,
            frictionCombine = PhysicsMaterialCombine.Average
        };
        foreach (var collider in GetComponentsInChildren<Collider>(true))
        {
            collider.sharedMaterial = landingMaterial;
            collider.enabled = true;
        }
    }

    void FixedUpdate()
    {
        if (fallBody != null && !fallBody.isKinematic && !fallBody.IsSleeping())
            fallBody.AddForce(Vector3.down * fallGravity, ForceMode.Acceleration);
    }

    void OnCollisionEnter(Collision collision)
    {
        if (FirstGroundContact) return;
        for (int i = 0; i < collision.contactCount; i++)
            if (collision.GetContact(i).normal.y > .5f) { FirstGroundContact = true; break; }
    }

    void OnDestroy()
    {
        if (landingMaterial != null) Destroy(landingMaterial);
    }
    float blinkSeconds = 1.4f, blinkFloor = .25f;

    public override string InteractionHint => "拾起戒指";
    public override bool AllowWhileHolding => true;
    internal Bounds WorldBounds
    {
        get
        {
            var bounds = new Bounds(transform.position, Vector3.one * .1f);
            if (model != null) foreach (var renderer in model)
                if (renderer != null) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => Active.Clear();
    void OnEnable() { if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable() => Active.Remove(this);

    internal void Initialize(LevelArenaBoss boss, float period, float minimumBrightness)
    {
        owner = boss;
        blinkSeconds = Mathf.Max(.1f, period);
        blinkFloor = Mathf.Clamp01(minimumBrightness);
        model = GetComponentsInChildren<Renderer>(true);
        var anchor = transform.Find("Interaction Anchor");
        if (anchor != null) interactionAnchor = anchor;
        var filters = GetComponentsInChildren<MeshFilter>(true);
        int total = 0;
        foreach (var filter in filters)
            if (filter.sharedMesh != null && filter.sharedMesh.isReadable) total += filter.sharedMesh.vertexCount;
        int step = Mathf.Max(1, Mathf.CeilToInt(total / (float)Capacity));
        foreach (var filter in filters)
        {
            if (filter.sharedMesh == null || !filter.sharedMesh.isReadable) continue;
            var vertices = filter.sharedMesh.vertices;
            for (int i = 0; i < vertices.Length && points.Count < Capacity; i += step)
                points.Add((filter.transform, vertices[i]));
        }
        RefreshModelVisibility();
    }

    void Update() => RefreshModelVisibility();
    void RefreshModelVisibility()
    {
        if (model == null) return;
        foreach (var renderer in model)
            if (renderer != null) renderer.enabled = fallingVisible && DeveloperMode.Enabled;
    }

    internal int Collect(Vector4[] output)
    {
        if (!fallingVisible || collected || !isActiveAndEnabled) return 0;
        float brightness = landed ? Mathf.Lerp(blinkFloor, 1,
            .5f + .5f * Mathf.Cos((Time.time - landedAt) * Mathf.PI * 2 / blinkSeconds)) : 1;
        int count = Mathf.Min(points.Count, output.Length);
        for (int i = 0; i < count; i++)
        {
            Vector3 p = points[i].transform.TransformPoint(points[i].position);
            output[i] = new Vector4(p.x, p.y, p.z, brightness);
        }
        return count;
    }

    internal void SetLanded()
    {
        landed = true;
        landedAt = Time.time;
        RefreshModelVisibility();
    }
    public override bool CanInteract(PlayerInteraction player)
        => landed && !collected && owner != null && owner.CanCollectVictoryRing(player) && base.CanInteract(player);
    protected override void OnInteract(PlayerInteraction player)
    {
        if (!owner.TryCollectVictoryRing(player)) return;
        collected = true;
        gameObject.SetActive(false);
    }
}
