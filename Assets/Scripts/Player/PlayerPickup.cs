using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class PlayerPickup : MonoBehaviour
{
    public Key interactionKey = Key.E;
    [Tooltip("A separate child SphereCollider, with Is Trigger enabled. Edit its radius/center to change pickup range.")]
    public SphereCollider pickupRange;
    public Transform viewTransform;
    [Tooltip("A child of the camera, positioned in the lower-right of the view.")]
    public Transform holdPoint;
    public LayerMask pickupLayers = ~0;
    public LayerMask obstacleLayers = ~0;
    [Min(0f)] public float throwSpeed = 10f;
    [Min(0f)] public float throwUpwardSpeed = 1f;
    [Min(.1f)] public float throwDistance = .9f;

    public PickupItem HeldItem => heldItem != null && heldItem.Holder == this ? heldItem : null;
    PickupItem heldItem;
    PlayerController controller;

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        if (pickupRange == null || pickupRange.transform == transform || !pickupRange.isTrigger
            || viewTransform == null || holdPoint == null)
        {
            Debug.LogError("PlayerPickup needs a separate child trigger SphereCollider, camera and hold point.", this);
            enabled = false;
        }
    }

    void Update()
    {
        if (controller != null && (controller.InputSuspended || !controller.enabled)) return;
        if (Keyboard.current != null && interactionKey != Key.None && Keyboard.current[interactionKey].wasPressedThisFrame)
            Interact();
    }

    public bool Interact()
    {
        if (!isActiveAndEnabled) return false;
        return HeldItem != null ? TryThrow() : TryPickUpNearest();
    }

    public bool TryPickUpNearest()
    {
        if (HeldItem != null || !TryGetRange(out var center, out float radius)) return false;
        PickupItem nearest = null;
        float nearestDistance = float.PositiveInfinity;
        // Query the actual range collider on demand: cooldown expiry and compound colliders cannot leave stale candidates.
        foreach (var candidate in Physics.OverlapSphere(center, radius, pickupLayers, QueryTriggerInteraction.Ignore))
        {
            var item = candidate.attachedRigidbody != null ? candidate.attachedRigidbody.GetComponent<PickupItem>() : null;
            if (item == null || !item.CanPickUp) continue;
            Vector3 target = candidate.ClosestPoint(viewTransform.position);
            float distance = (target - viewTransform.position).sqrMagnitude;
            if (distance >= nearestDistance || IsObstructed(viewTransform.position, target, item)) continue;
            nearest = item;
            nearestDistance = distance;
        }
        if (nearest == null || !nearest.TryPickUp(this, holdPoint)) return false;
        heldItem = nearest;
        return true;
    }

    public bool TryGetRange(out Vector3 center, out float radius)
    {
        center = default;
        radius = 0;
        if (pickupRange == null || !pickupRange.enabled || !pickupRange.gameObject.activeInHierarchy) return false;
        var scale = pickupRange.transform.lossyScale;
        radius = pickupRange.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        center = pickupRange.transform.TransformPoint(pickupRange.center);
        return true;
    }

    // Explicit-target path: a mouse click must never pick a different, merely nearer item.
    public bool TryPickUp(PickupItem item)
    {
        if (!isActiveAndEnabled || HeldItem != null || item == null || !item.CanPickUp
            || !TryGetRange(out var center, out float radius)) return false;
        foreach (var candidate in Physics.OverlapSphere(center, radius, pickupLayers, QueryTriggerInteraction.Ignore))
        {
            if (candidate.attachedRigidbody == null || candidate.attachedRigidbody.GetComponent<PickupItem>() != item) continue;
            if (IsObstructed(viewTransform.position, candidate.ClosestPoint(viewTransform.position), item)) continue;
            if (!item.TryPickUp(this, holdPoint)) return false;
            heldItem = item;
            return true;
        }
        return false;
    }

    bool IsObstructed(Vector3 origin, Vector3 target, PickupItem item)
    {
        Vector3 direction = target - origin;
        foreach (var hit in Physics.RaycastAll(origin, direction.normalized, direction.magnitude, obstacleLayers, QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<PickupItem>() != item) return true;
        return false;
    }

    public bool TryThrow()
    {
        var item = HeldItem;
        if (item == null) return false;
        float radius = item.CollisionRadius;
        Vector3 origin = viewTransform.position;
        Vector3 direction = viewTransform.forward;
        float distance = Mathf.Max(throwDistance, radius + .1f);
        foreach (var hit in Physics.SphereCastAll(origin, radius, direction, distance, obstacleLayers, QueryTriggerInteraction.Ignore))
            if (!hit.collider.transform.IsChildOf(transform)) distance = Mathf.Min(distance, Mathf.Max(0, hit.distance - .02f));
        Vector3 position = origin + direction * distance;
        // Refuse release into geometry rather than spawning the item through a nearby wall.
        foreach (var overlap in Physics.OverlapSphere(position, radius, obstacleLayers, QueryTriggerInteraction.Ignore))
            if (!overlap.transform.IsChildOf(transform)) return false;
        item.Release(position, direction * throwSpeed + Vector3.up * throwUpwardSpeed);
        heldItem = null;
        return true;
    }

    void OnDisable()
    {
        if (HeldItem != null) heldItem.Release(holdPoint.position, Vector3.zero);
        heldItem = null;
    }
}
