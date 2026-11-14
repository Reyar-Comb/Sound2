using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(30), DisallowMultipleComponent]
public sealed class PlayerPickup : MonoBehaviour
{
    public Key interactionKey = Key.E;
    [Tooltip("A separate child SphereCollider, with Is Trigger enabled. Edit its radius/center to change pickup range.")]
    public SphereCollider pickupRange;
    public Transform viewTransform;
    [Tooltip("A child of the camera, positioned in the lower-center of the view.")]
    public Transform holdPoint;
    public LayerMask pickupLayers = ~0;
    public LayerMask obstacleLayers = ~0;
    [Min(0f)] public float throwSpeed = 10f;
    [Min(0f)] public float throwUpwardSpeed = 1f;
    [Min(.1f)] public float throwDistance = .9f;
    [Min(.3f)] public float placeDistance = 2.5f;
    [Header("Held inertia")]
    [Min(.01f)] public float rotationLag = .12f;
    [Min(.01f)] public float positionSmoothTime = .06f;
    [Range(0,30)] public float maximumLagAngle = 12;

    public PickupItem HeldItem => heldItem != null && heldItem.Holder == this ? heldItem : null;
    PickupItem heldItem;
    PlayerController controller;
    PickupItem followingItem;
    Quaternion followingRotation;
    Vector3 followingOffset,offsetVelocity,previousViewPosition;

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
        if(Mouse.current!=null && Mouse.current.rightButton.wasPressedThisFrame)
        {
            if(HeldItem!=null && !TryPlace())GetComponent<PlayerSubtitles>()?.ShowImmediate("空间不足，无法放下",1.2f);
            return;
        }
        if (Keyboard.current != null && interactionKey != Key.None && Keyboard.current[interactionKey].wasPressedThisFrame)
            Interact();
    }

    void LateUpdate()
    {
        var item = HeldItem;
        if (item == null){followingItem=null;return;}
        Vector3 localAnchor=Quaternion.Inverse(viewTransform.rotation)*(holdPoint.position-viewTransform.position);
        if(followingItem!=item || Vector3.Distance(previousViewPosition,viewTransform.position)>2.5f)
        {
            followingItem=item;followingRotation=viewTransform.rotation;followingOffset=localAnchor;offsetVelocity=Vector3.zero;
        }
        previousViewPosition=viewTransform.position;
        if(Time.deltaTime>0)
        {
            followingRotation=Quaternion.Slerp(followingRotation,viewTransform.rotation,1-Mathf.Exp(-Time.deltaTime/Mathf.Max(.01f,rotationLag)));
            followingRotation=Quaternion.RotateTowards(viewTransform.rotation,followingRotation,maximumLagAngle);
            Vector3 desired=Quaternion.Inverse(viewTransform.rotation)*followingRotation*localAnchor;
            followingOffset=Vector3.SmoothDamp(followingOffset,desired,ref offsetVelocity,Mathf.Max(.01f,positionSmoothTime),Mathf.Infinity,Time.deltaTime);
        }
        Vector3 origin = viewTransform.position;
        Vector3 delta = viewTransform.rotation*followingOffset;
        float distance = delta.magnitude;
        if (distance > .001f)
            foreach (var hit in Physics.SphereCastAll(origin, item.CollisionRadius, delta / distance,
                distance, obstacleLayers, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(transform))
                    distance = Mathf.Min(distance, Mathf.Max(0, hit.distance - .02f));
        if(distance<delta.magnitude-.001f)
        {
            followingOffset=Quaternion.Inverse(viewTransform.rotation)*(delta.normalized*distance);offsetVelocity=Vector3.zero;
        }
        item.SetHeldPose(origin + delta.normalized * distance,
            followingRotation*Quaternion.Inverse(viewTransform.rotation)*holdPoint.rotation*Quaternion.Euler(item.heldEulerAngles));
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
        if(!ClearReleasePath(item.transform.position,position,radius))return false;
        item.Release(position, direction * throwSpeed + Vector3.up * throwUpwardSpeed);
        heldItem = null;
        return true;
    }

    // Place on the aimed support, or gently set down below the actual held pose. No throwing impulse.
    public bool TryPlace()
    {
        var item=HeldItem;if(item==null)return false;
        float radius=item.CollisionRadius;Vector3 position=item.transform.position;
        bool FindSupport(Vector3 origin,Vector3 direction,out RaycastHit support)
        {
            support=default;float nearest=float.PositiveInfinity;
            foreach(var hit in Physics.RaycastAll(origin,direction,placeDistance,obstacleLayers,QueryTriggerInteraction.Ignore))
            {
                if(hit.transform.IsChildOf(transform) || hit.distance>=nearest)continue;
                support=hit;nearest=hit.distance;
            }
            return nearest<float.PositiveInfinity && Vector3.Dot(support.normal,Vector3.up)>.55f;
        }
        if(FindSupport(viewTransform.position,viewTransform.forward,out var surface)
            || FindSupport(position+Vector3.up*.02f,Vector3.down,out surface))
            position=surface.point+surface.normal*(radius+.025f);
        foreach(var overlap in Physics.OverlapSphere(position,radius,obstacleLayers,QueryTriggerInteraction.Ignore))
            if(!overlap.transform.IsChildOf(transform))return false;
        if(!ClearReleasePath(item.transform.position,position,radius))return false;
        item.Release(position,Vector3.zero,.12f);heldItem=null;followingItem=null;return true;
    }

    bool ClearReleasePath(Vector3 from,Vector3 to,float radius)
    {
        foreach(var overlap in Physics.OverlapSphere(from,radius,obstacleLayers,QueryTriggerInteraction.Ignore))
            if(!overlap.transform.IsChildOf(transform))return false;
        Vector3 delta=to-from;float length=delta.magnitude;if(length<.001f)return true;
        foreach(var hit in Physics.SphereCastAll(from,radius,delta/length,length,obstacleLayers,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(transform) && hit.distance<length-.01f)return false;
        return true;
    }

    void OnDisable()
    {
        if (HeldItem != null) heldItem.Release(heldItem.transform.position, Vector3.zero);
        heldItem = null;
    }
}
