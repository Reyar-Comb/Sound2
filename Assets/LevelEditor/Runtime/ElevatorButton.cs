using UnityEngine;

namespace Sound2.LevelEditing
{
    public sealed class ElevatorButton : InteractableObject
    {
        public OneWayElevator elevator;
        public bool inside;
        public override bool AllowWhileHolding => true;
        public override string InteractionHint => inside ? "乘坐电梯上行（不可返回）" : "打开上行电梯";
        public override bool CanInteract(PlayerInteraction player) => base.CanInteract(player) && elevator != null
            && elevator.CanUse(player, inside);
        protected override void OnInteract(PlayerInteraction player) => elevator.Use(player, inside);
    }
}
