using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(SonarObjectEmitter))]
public sealed class BossMemoryButton : InteractableObject
{
    public bool startsFight;
    public BossMemoryGate gate;
    SonarObjectEmitter pulse;

    public override string InteractionHint => startsFight ? "开启 Boss 战" : "返回初始房间";

    public override bool CanInteract(PlayerInteraction actor) => base.CanInteract(actor)
        && gate != null && gate.Available && (!startsFight || gate.Ready);

    internal void SetPulse(bool active)
    {
        if (pulse == null) pulse = GetComponent<SonarObjectEmitter>();
        if (pulse.enabled == active) return;
        pulse.enabled = active;
        if (active) pulse.RestartSchedule();
    }

    protected override void OnInteract(PlayerInteraction actor) => gate.Press(actor, startsFight);
}
