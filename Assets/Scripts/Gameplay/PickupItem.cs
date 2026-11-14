using System.Collections.Generic;
using UnityEngine;

/// <summary>A reusable rigidbody item with a post-throw pickup lockout.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CollisionSonarEmitter))]
public sealed class PickupItem : InteractableObject
{
    [Min(0f)] public float pickupCooldown = 1f;
    [Tooltip("Only wall contacts on these layers can end the pickup cooldown early.")]
    public LayerMask wallLayers = ~0;
    [Tooltip("Maximum absolute dot product with world up for a wall normal. Floors do not unlock pickup.")]
    [Range(0f, 1f)] public float wallNormalMaxUp = .5f;
    public Vector3 heldEulerAngles;

    [Header("Breakage (opt-in)")]
    public bool breakable;
    [Min(.1f)] public float breakSpeed = 7;
    public LayerMask breakLayers = ~0;
    [Tooltip("Physical remnants only: no PickupItem or InteractableObject on any fragment.")]
    public GameObject brokenPrefab;
    [Min(0)] public float fragmentSeparationSpeed = .35f;
    [Min(0), Tooltip("Zero keeps remnants until their scene or floor is unloaded.")]
    public float fragmentLifetime;
    [Min(1)] public float breakPulseRange = 10;
    [Min(0)] public float breakHearingRange = 16;
    public AudioClip breakClip;
    public event System.Action PickedUp;
    public event System.Action Broken;
    public bool IsBroken { get; private set; }
    public GameObject SpawnedRemains { get; private set; }

    public bool IsHeld => holder != null;
    public override string InteractionHint => "拾取";
    public PlayerPickup Holder => holder;
    public bool CanPickUp => isActiveAndEnabled && !IsBroken && !IsHeld && Time.time >= pickupAllowedAt;
    public override bool CanInteract(PlayerInteraction player) => base.CanInteract(player) && CanPickUp
        && player != null && player.Pickup != null && player.Pickup.HeldItem == null;
    public override bool TryInteract(PlayerInteraction player)
    {
        if (!CanInteract(player) || !player.Pickup.TryPickUp(this)) return false;
        onInteract.Invoke(player);
        return true;
    }
    public float CollisionRadius { get; private set; }

    Rigidbody body;
    Collider[] colliders;
    bool[] colliderEnabled;
    PlayerPickup holder;
    Transform originalParent;
    bool previousGravity;
    RigidbodyInterpolation previousInterpolation;
    CollisionDetectionMode previousCollisionMode;
    float pickupAllowedAt;
    bool releasePending;
    readonly List<(Collider item, Collider player)> ignoredPairs = new();

    void Awake()
    {
        // RequireComponent handles new items; this also upgrades already-authored scene/prefab instances.
        EnsureImpactFeedback();
        body = GetComponent<Rigidbody>();
        var owned = new List<Collider>();
        foreach (var candidate in GetComponentsInChildren<Collider>(true))
            if (candidate.attachedRigidbody == body && !candidate.isTrigger) owned.Add(candidate);
        colliders = owned.ToArray();
        colliderEnabled = new bool[colliders.Length];
    }

    public void EnsureImpactFeedback()
    {
        if (GetComponent<CollisionSonarEmitter>() == null) gameObject.AddComponent<CollisionSonarEmitter>();
        var sonar = GetComponent<SonarObjectEmitter>();
        sonar.automatic = false;
        sonar.toggleKey = UnityEngine.InputSystem.Key.None;
    }

    public bool TryPickUp(PlayerPickup picker, Transform anchor)
    {
        if (!CanPickUp || picker == null || anchor == null || colliders.Length == 0) return false;
        RestoreIgnoredCollisions(true);
        Physics.SyncTransforms();
        CollisionRadius = .05f;
        for (int i = 0; i < colliders.Length; i++)
        {
            colliderEnabled[i] = colliders[i].enabled;
            if (!colliderEnabled[i]) continue;
            // A moving/interpolated body's physics bounds can lag its rendered transform.
            // Derive spheres from their shape, not the diagonal of a stale world AABB.
            if (colliders[i] is SphereCollider sphere)
            {
                var scale = sphere.transform.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                CollisionRadius = Mathf.Max(CollisionRadius,
                    Vector3.Distance(transform.position, sphere.transform.TransformPoint(sphere.center)) + radius);
            }
            else CollisionRadius = Mathf.Max(CollisionRadius,
                Vector3.Distance(transform.position, colliders[i].bounds.center) + colliders[i].bounds.extents.magnitude);
        }
        originalParent = transform.parent;
        previousGravity = body.useGravity;
        previousInterpolation = body.interpolation;
        previousCollisionMode = body.collisionDetectionMode;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.collisionDetectionMode = CollisionDetectionMode.Discrete;
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.None;
        foreach (var itemCollider in colliders) itemCollider.enabled = false;
        holder = picker;
        transform.SetParent(anchor, true);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.Euler(heldEulerAngles);
        body.position = transform.position;
        body.rotation = transform.rotation;
        Physics.SyncTransforms();
        PickedUp?.Invoke();
        return true;
    }

    public void SetHeldPose(Vector3 position, Quaternion rotation)
    {
        if (!IsHeld) return;
        transform.SetPositionAndRotation(position, rotation);
        body.position = position;
        body.rotation = rotation;
    }

    public void Release(Vector3 position, Vector3 velocity,float cooldown=-1)
    {
        if (!IsHeld) return;
        if (!Application.isPlaying || Application.exitCancellationToken.IsCancellationRequested) { holder = null; return; }
        if (!gameObject.activeInHierarchy)
        {
            if (!releasePending) ReleaseAfterDeactivation(position, velocity, cooldown);
            return;
        }
        ReleaseNow(position, velocity, cooldown);
    }

    async void ReleaseAfterDeactivation(Vector3 position, Vector3 velocity, float cooldown)
    {
        releasePending = true;
        try
        {
            // Unity forbids reparenting a child while its parent's activation state is changing.
            await Awaitable.NextFrameAsync(Application.exitCancellationToken);
            if (this != null && Application.isPlaying && IsHeld) ReleaseNow(position, velocity, cooldown);
        }
        catch (System.OperationCanceledException) { }
        finally { releasePending = false; }
    }

    void ReleaseNow(Vector3 position, Vector3 velocity, float cooldown)
    {
        var playerColliders = holder.GetComponentsInChildren<Collider>();
        var releaseParent = ReleaseParent();
        holder = null;
        transform.SetParent(releaseParent, true);
        transform.position = position;
        body.position = position;
        body.rotation = transform.rotation;
        body.isKinematic = false;
        body.useGravity = previousGravity;
        body.interpolation = previousInterpolation;
        body.collisionDetectionMode = previousCollisionMode;
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = colliderEnabled[i];
            if (!colliderEnabled[i]) continue;
            foreach (var playerCollider in playerColliders)
            {
                if (playerCollider == null || playerCollider.isTrigger || playerCollider.attachedRigidbody == body) continue;
                if (Physics.GetIgnoreCollision(colliders[i], playerCollider)) continue;
                Physics.IgnoreCollision(colliders[i], playerCollider, true);
                ignoredPairs.Add((colliders[i], playerCollider));
            }
        }
        pickupAllowedAt = Time.time + (cooldown<0?pickupCooldown:cooldown);
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
    }

    Transform ReleaseParent()
    {
        var progress = holder.GetComponent<PlayerFloorProgression>();
        var current = progress != null && progress.Map != null ? progress.Map.FindFloor(progress.CurrentFloor) : null;
        var previous = originalParent != null ? originalParent.GetComponentInParent<Sound2.LevelEditing.SeededFloor>() : null;
        if (current != null && (originalParent == null || !originalParent.gameObject.activeInHierarchy || previous != current))
            return current.transform;
        return originalParent != null && originalParent.gameObject.activeInHierarchy ? originalParent : null;
    }

    void FixedUpdate() => RestoreIgnoredCollisions(false);

    void RestoreIgnoredCollisions(bool force)
    {
        for (int i = ignoredPairs.Count - 1; i >= 0; i--)
        {
            var pair = ignoredPairs[i];
            if (pair.item != null && pair.player != null)
            {
                if (!force && pair.item.bounds.Intersects(pair.player.bounds)) continue;
                Physics.IgnoreCollision(pair.item, pair.player, false);
            }
            ignoredPairs.RemoveAt(i);
        }
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!isActiveAndEnabled || TryBreak(collision, CollisionSonarEmitter.ImpactSpeed(collision))) return;
        if (IsHeld || Time.time >= pickupAllowedAt || collision.collider.GetComponentInParent<PlayerPickup>() != null
            || collision.collider.GetComponentInParent<PickupItem>() != null
            || (wallLayers.value & (1 << collision.gameObject.layer)) == 0) return;
        for (int i = 0; i < collision.contactCount; i++)
            if (Mathf.Abs(Vector3.Dot(collision.GetContact(i).normal, Vector3.up)) <= wallNormalMaxUp)
            {
                pickupAllowedAt = Time.time;
                break;
            }
    }

    public bool TryBreak(Collision collision, float impactSpeed)
    {
        if (IsBroken) return true;
        if (!isActiveAndEnabled || !breakable || IsHeld || brokenPrefab == null || collision.contactCount == 0
            || impactSpeed < Mathf.Max(.1f, breakSpeed)
            || (breakLayers.value & (1 << collision.collider.gameObject.layer)) == 0
            || collision.collider.GetComponentInParent<PlayerController>() != null) return false;
        var contact = collision.GetContact(0);
        BreakAt(contact.point, contact.normal);
        return true;
    }

    void BreakAt(Vector3 point, Vector3 normal)
    {
        IsBroken = true;
        var velocity = body.linearVelocity;
        var angularVelocity = body.angularVelocity;
        var center = body.worldCenterOfMass;
        Broken?.Invoke();
        foreach (var output in GetComponentsInChildren<AudioSource>(true)) output.Stop();
        foreach (var itemCollider in colliders) itemCollider.enabled = false;

        SpawnedRemains = Instantiate(brokenPrefab, transform.position, transform.rotation, transform.parent);
        PropFragments.SetLayer(SpawnedRemains);
        SpawnedRemains.transform.localScale = transform.localScale;
        foreach (Transform part in SpawnedRemains.transform)
        {
            var original = transform.Find(part.name);
            if (original != null)
            {
                part.localPosition = original.localPosition;
                part.localRotation = original.localRotation;
                part.localScale = original.localScale;
            }
        }
        Physics.SyncTransforms();
        foreach (var fragment in SpawnedRemains.GetComponentsInChildren<Rigidbody>())
        {
            Vector3 outward = (fragment.worldCenterOfMass - center).normalized;
            fragment.linearVelocity = velocity + Vector3.Cross(angularVelocity, fragment.worldCenterOfMass - center)
                + (outward + normal) * fragmentSeparationSpeed;
            fragment.angularVelocity = angularVelocity;
            fragment.WakeUp();
        }
        // One break pulse uses the existing field; shards do not have emission or pickup components.
        var sonar = GetComponent<SonarObjectEmitter>();
        var field = sonar != null ? sonar.pulseField : null;
        if (field == null) field = FindAnyObjectByType<SonarPulseField>();
        if (field != null && sonar != null)
            field.EmitFrom(point + normal * .04f, breakPulseRange, sonar.waveSpeed, sonar.afterglow,
                sonar.pulseKind, sonar.distanceSettings, sonar.densitySettings);
        GameplayNoise.Emit(point, breakHearingRange, GameplayNoiseKind.Impact, this);
        if (breakClip != null)
        {
            var output = SpawnedRemains.AddComponent<SpatialAudioEmitter>();
            output.settings.maxDistance = breakHearingRange;
            output.PlayOneShot(breakClip);
        }
        if (fragmentLifetime > 0) Destroy(SpawnedRemains, fragmentLifetime);
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    void OnDisable()
    {
        if (IsHeld) Release(transform.position, Vector3.zero);
        RestoreIgnoredCollisions(true);
    }
}
