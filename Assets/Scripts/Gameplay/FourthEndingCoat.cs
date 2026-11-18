using UnityEngine;

public sealed class FourthEndingCoat : InteractableObject
{
    internal FourthEndingWardrobe wardrobe;
    public override string InteractionHint => "取出戒指";
    public override bool AllowWhileHolding => true;
    public override bool CanInteract(PlayerInteraction actor)
        => base.CanInteract(actor) && wardrobe != null && wardrobe.CanTakeRing;
    protected override void OnInteract(PlayerInteraction actor) => wardrobe.TakeRing(actor);
}
