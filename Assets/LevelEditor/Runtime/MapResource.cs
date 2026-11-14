using UnityEngine;
using UnityEngine.Events;
namespace Sound2.LevelEditing
{
    public sealed class MapResource : InteractableObject
    {
        [Min(1)] public int amount = 1;
        public UnityEvent<int> onCollected = new();
        bool collected;
        public override bool CanInteract(PlayerInteraction player) => !collected && base.CanInteract(player)
            && player != null && player.GetComponent<PlayerFloorProgression>() != null;
        public override bool TryInteract(PlayerInteraction player)
        {
            if (!CanInteract(player)) return false;
            collected = true;
            player.GetComponent<PlayerFloorProgression>().CollectResource(amount);
            onInteract.Invoke(player); onCollected.Invoke(amount); gameObject.SetActive(false);
            return true;
        }
    }
}
