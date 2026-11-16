using UnityEngine;

namespace Sound2.LevelEditing
{
    public sealed class LevelElevatorButton : InteractableObject
    {
        public LevelElevator elevator;
        public bool inside;
        [SerializeField, InspectorName("按钮音效覆盖"), Tooltip("留空时使用电梯根节点的按钮音效。")]
        AudioClip pressSound;
        SpatialAudioEmitter pressAudio;

        public override bool AllowWhileHolding => true;
        public override string InteractionHint => inside ? "乘坐电梯" : "打开电梯";
        public override bool CanInteract(PlayerInteraction player) => base.CanInteract(player) && elevator!=null && elevator.CanUse(player,inside);
        protected override void OnInteract(PlayerInteraction player)
        {
            var clip = pressSound != null ? pressSound : elevator.ButtonPressSound;
            if (clip != null)
            {
                pressAudio ??= LevelElevator.GetOrAddAudioEmitter(transform);
                pressAudio.PlayOneShot(clip);
            }
            elevator.Use(player,inside);
        }
    }
}
