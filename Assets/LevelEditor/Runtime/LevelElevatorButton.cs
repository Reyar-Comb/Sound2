namespace Sound2.LevelEditing
{
    public sealed class LevelElevatorButton : InteractableObject
    {
        public LevelElevator elevator;
        public bool inside;
        public override bool AllowWhileHolding => true;
        public override string InteractionHint => inside ? "乘坐电梯" : "打开电梯";
        public override bool CanInteract(PlayerInteraction player) => base.CanInteract(player) && elevator!=null && elevator.CanUse(player,inside);
        protected override void OnInteract(PlayerInteraction player) => elevator.Use(player,inside);
    }
}
