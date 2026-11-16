using Sound2.LevelEditing;
using UnityEngine;

/// <summary>Run-gated ending prop. Only the bed in the initial room receives this component.</summary>
[DisallowMultipleComponent]
public sealed class StartingRoomBed : InteractableObject
{
    Renderer[] surfaces;
    Collider[] colliders;
    bool[] originalSurfaces, originalColliders;
    SoundShellOpening opening;
    public bool IsUnlocked => StoryRunProgress.Current.EnteredBossRoom;
    public override string InteractionHint => "躺下休息";
    public override bool AllowWhileHolding => true;

    void Awake()
    {
        surfaces = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        originalSurfaces = new bool[surfaces.Length];
        originalColliders = new bool[colliders.Length];
        for (int i = 0; i < surfaces.Length; i++) originalSurfaces[i] = surfaces[i].enabled;
        for (int i = 0; i < colliders.Length; i++) originalColliders[i] = colliders[i].enabled;
    }

    void OnEnable()
    {
        StoryRunProgress.Current.EndingProgressChanged += RefreshPresentation;
        RefreshPresentation();
    }

    void OnDisable() => StoryRunProgress.Current.EndingProgressChanged -= RefreshPresentation;

    void RefreshPresentation()
    {
        bool visible = IsUnlocked;
        for (int i = 0; i < surfaces.Length; i++)
            if (surfaces[i] != null) surfaces[i].enabled = visible && originalSurfaces[i];
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i] != null) colliders[i].enabled = visible && originalColliders[i];
    }

    SoundShellOpening Opening
    {
        get
        {
            if (opening != null) return opening;
            var level = GetComponentInParent<LevelLayout>();
            foreach (var candidate in FindObjectsByType<SoundShellOpening>())
                if (candidate.startingRoom == level) { opening = candidate; break; }
            return opening;
        }
    }

    public override bool CanInteract(PlayerInteraction actor) => base.CanInteract(actor) && actor != null
        && IsUnlocked && Opening != null && Opening.CanUseBed(actor.GetComponent<PlayerController>());

    protected override void OnInteract(PlayerInteraction actor) => Opening.TryUseBed(actor.GetComponent<PlayerController>());

}
