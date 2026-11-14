using UnityEngine;
using UnityEngine.Events;

/// <summary>Independent object action. May coexist with PickupItem on the same object.</summary>
public class InteractableObject : MonoBehaviour
{
    [Tooltip("Optional world-space point for aiming and the interaction ring. Defaults to this transform.")]
    public Transform interactionAnchor;
    public bool interactionEnabled = true;
    public string interactionHint = "交互";
    public virtual string InteractionHint => interactionHint;
    public UnityEvent<PlayerInteraction> onInteract = new();

    public Vector3 InteractionPosition => interactionAnchor != null ? interactionAnchor.position : transform.position;
    public virtual bool CanInteract(PlayerInteraction player) => isActiveAndEnabled && interactionEnabled;

    public virtual bool TryInteract(PlayerInteraction player)
    {
        if (player == null || !CanInteract(player)) return false;
        OnInteract(player);
        onInteract.Invoke(player);
        return true;
    }

    /// <summary>Override for the object's own action. The base component intentionally has no effect.</summary>
    protected virtual void OnInteract(PlayerInteraction player) { }
}
