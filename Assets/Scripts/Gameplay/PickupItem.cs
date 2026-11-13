using System.Collections.Generic;
using UnityEngine;

/// <summary>A reusable rigidbody item with a post-throw pickup lockout.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class PickupItem : MonoBehaviour
{
    [Min(0f)] public float pickupCooldown = 1f;
    [Tooltip("Only wall contacts on these layers can end the pickup cooldown early.")]
    public LayerMask wallLayers = ~0;
    [Tooltip("Maximum absolute dot product with world up for a wall normal. Floors do not unlock pickup.")]
    [Range(0f, 1f)] public float wallNormalMaxUp = .5f;
    public Vector3 heldEulerAngles;

    public bool IsHeld => holder != null;
    public PlayerPickup Holder => holder;
    public bool CanPickUp => isActiveAndEnabled && !IsHeld && Time.time >= pickupAllowedAt;
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
    readonly List<(Collider item, Collider player)> ignoredPairs = new();

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        var owned = new List<Collider>();
        foreach (var candidate in GetComponentsInChildren<Collider>(true))
            if (candidate.attachedRigidbody == body && !candidate.isTrigger) owned.Add(candidate);
        colliders = owned.ToArray();
        colliderEnabled = new bool[colliders.Length];
    }

    public bool TryPickUp(PlayerPickup picker, Transform anchor)
    {
        if (!CanPickUp || picker == null || anchor == null || colliders.Length == 0) return false;
        RestoreIgnoredCollisions(true);
        CollisionRadius = .05f;
        for (int i = 0; i < colliders.Length; i++)
        {
            colliderEnabled[i] = colliders[i].enabled;
            if (colliderEnabled[i])
                CollisionRadius = Mathf.Max(CollisionRadius,
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
        return true;
    }

    public void Release(Vector3 position, Vector3 velocity)
    {
        if (!IsHeld) return;
        var playerColliders = holder.GetComponentsInChildren<Collider>();
        holder = null;
        transform.SetParent(originalParent, true);
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
        pickupAllowedAt = Time.time + pickupCooldown;
        body.linearVelocity = velocity;
        body.angularVelocity = Vector3.zero;
        body.WakeUp();
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

    void OnDisable()
    {
        if (IsHeld) Release(transform.position, Vector3.zero);
        RestoreIgnoredCollisions(true);
    }
}
