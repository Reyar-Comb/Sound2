using Sound2.LevelEditing;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossMemorySlot : InteractableObject
{
    [System.Serializable]
    sealed class MaterialSet { public Material[] materials; }
    [SerializeField, HideInInspector] BossMemoryGate gate;
    [SerializeField, HideInInspector] int slot;
    [SerializeField, HideInInspector] LevelProp presentation;
    [SerializeField, HideInInspector] Renderer[] renderers;
    [SerializeField, HideInInspector] MaterialSet[] originalMaterials;

    public override bool AllowWhileHolding => true;
    public override string InteractionHint => gate == null ? base.InteractionHint :
        (gate.IsPlaced(slot) ? "调查：" : "放置：") + gate.memories[slot].title;

    public override bool CanInteract(PlayerInteraction actor) => base.CanInteract(actor)
        && gate != null && gate.isActiveAndEnabled && gate.Available;

    internal void Initialize(BossMemoryGate owner, int index, LevelProp prop, Material preview)
    {
        gate = owner;
        slot = index;
        presentation = prop;
        renderers = GetComponentsInChildren<Renderer>();
        originalMaterials = new MaterialSet[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            originalMaterials[i] = new MaterialSet { materials = renderers[i].sharedMaterials };
            var materials = new Material[originalMaterials[i].materials.Length];
            for (int j = 0; j < materials.Length; j++) materials[j] = preview;
            renderers[i].sharedMaterials = materials;
        }
        if (gate.IsPlaced(slot)) ShowPlaced();
    }

    internal void ShowPlaced()
    {
        for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterials = originalMaterials[i].materials;
        presentation.enabled = true;
    }

    void OnEnable()
    {
        if (presentation != null && !presentation.enabled && gate != null && gate.IsPlaced(slot))
            ShowPlaced();
    }

    protected override void OnInteract(PlayerInteraction actor) => gate.PressMemory(actor, slot, this);
}
