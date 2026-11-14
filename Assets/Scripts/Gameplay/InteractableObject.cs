using UnityEngine;
using UnityEngine.Events;

/// <summary>Attach to a collider's parent, then wire On Interact or override TryInteract.</summary>
[DisallowMultipleComponent]
public class InteractableObject : MonoBehaviour
{
    [Tooltip("Optional world-space point for aiming and the interaction ring. Defaults to this transform.")]
    public Transform interactionAnchor;
    public bool interactionEnabled = true;
    public UnityEvent<PlayerInteraction> onInteract = new();

    public Vector3 InteractionPosition => interactionAnchor != null ? interactionAnchor.position : transform.position;
    public virtual bool CanInteract(PlayerInteraction player) => isActiveAndEnabled && interactionEnabled;

    public virtual bool TryInteract(PlayerInteraction player)
    {
        if (player == null || !CanInteract(player)) return false;
        onInteract.Invoke(player);
        return true;
    }
}
