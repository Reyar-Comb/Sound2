using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(SonarObjectEmitter))]
public sealed class BossMemoryButton : InteractableObject
{
    public bool startsFight;
    public BossMemoryGate gate;
    SonarObjectEmitter pulse;

    public override string InteractionHint => startsFight ? "直面（需集齐信物）" : "逃避";

    public override bool CanInteract(PlayerInteraction actor) => base.CanInteract(actor)
        && gate != null && gate.Available;

    internal void SetPulse(bool active)
    {
        if (pulse == null) pulse = GetComponent<SonarObjectEmitter>();
        if (pulse.enabled == active) return;
        pulse.enabled = active;
        if (active) pulse.RestartSchedule();
    }

    protected override void OnInteract(PlayerInteraction actor) => gate.Press(actor, startsFight);
}
