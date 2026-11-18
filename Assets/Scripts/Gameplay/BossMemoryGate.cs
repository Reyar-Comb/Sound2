using System;
using Sound2.LevelEditing;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossMemoryGate : InteractableObject
{
    [Serializable]
    public sealed class Memory
    {
        public string id;
        public string title;
        public Transform visual;
        [TextArea(4, 12)] public string text;
    }

    public BossMemoryButton returnButton, startButton;
    public Transform displayRoot;
    [SerializeField] Material displayGameplayMaterial;
    [SerializeField] Material previewMaterial;
    [Tooltip("从左到右的摆放顺序。Title 为调查标题，Text 每换行显示一段。")]
    public Memory[] memories;
    [Min(.1f)] public float lineHoldSeconds = 2.4f;
    [Min(1)] public float charactersPerSecond = StorySubtitles.DefaultCharactersPerSecond;
    [Min(.1f)] public float displaySize = .52f;
    [Min(.1f)] public float slotSpacing = .72f;
    [Header("独立 Boss 场景的逃避过渡")]
    [Min(.1f)] public float escapeFadeOutSeconds = .8f;
    [Min(.1f)] public float escapeFadeInSeconds = 1.2f;

    LevelArenaBoss boss;
    LevelArenaBoss Boss => boss != null ? boss : boss = GetComponentInParent<LevelLayout>()?.GetComponentInChildren<LevelArenaBoss>(true);
    bool leaving;
    public int PlacedCount
    {
        get
        {
            int count = 0;
            for (int slot = 0; slot < (memories?.Length ?? 0); slot++)
                if (IsPlaced(slot)) count++;
            return count;
        }
    }
    public bool Ready => memories != null && memories.Length > 0 && PlacedCount == memories.Length;
    public bool Available => !leaving && (Boss == null || Boss.State == LevelArenaBoss.EncounterState.Waiting);

    public override string InteractionHint
    {
        get
        {
            int total = memories?.Length ?? 0;
            if (Ready) return $"收集品已摆齐（{PlacedCount}/{total}）";
            return $"点击红色预览放置信物（{PlacedCount}/{total}）";
        }
    }

    public override bool AllowWhileHolding => true;

    void Awake()
    {
        // A runtime level snapshot may already contain displays from its source gate.
        if (displayRoot != null)
            foreach (var display in displayRoot.GetComponentsInChildren<BossMemorySlot>(true))
            {
                display.gameObject.SetActive(false);
                Destroy(display.gameObject);
            }
        if (memories != null)
            for (int slot = 0; slot < memories.Length; slot++) CreateDisplay(memories[slot], slot);
        RefreshButtons();
    }

    void Update() => RefreshButtons();
    void OnEnable() => leaving = false;

    void RefreshButtons()
    {
        if (returnButton != null) returnButton.SetPulse(Available);
        if (startButton != null) startButton.SetPulse(Available && Ready);
    }

    // Only the individual memory previews accept placement clicks, never the table body.
    public override bool CanInteract(PlayerInteraction actor) => false;

    internal bool IsPlaced(int slot) => StoryRunProgress.Current.IsMemoryPlaced(memories[slot].id);

    internal void PressMemory(PlayerInteraction actor, int slot, BossMemorySlot display)
    {
        if (!Available || actor == null) return;
        var memory = memories[slot];
        if (IsPlaced(slot))
        {
            StorySubtitles.Clear();
            StorySubtitles.Play(memory.title, lineHoldSeconds, charactersPerSecond);
            foreach (string line in (memory.text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                StorySubtitles.Play(line.Trim(), lineHoldSeconds, charactersPerSecond);
            return;
        }
        if (!StoryRunProgress.Current.TryPlaceMemory(memory.id))
        {
            actor.GetComponent<PlayerSubtitles>()?.ShowImmediate("没有可摆放的信物。请先在关卡中收集，再回到长桌。");
            return;
        }
        display.ShowPlaced();
        RefreshButtons();
    }

    bool CreateDisplay(Memory memory, int slot)
    {
        if (memory.visual == null || displayRoot == null) return false;

        var display = new GameObject(memory.title);
        display.transform.SetParent(displayRoot, false);
        var source = memory.visual;
        var bounds = new Bounds();
        bool hasBounds = false;
        foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || !filter.TryGetComponent<MeshRenderer>(out var renderer) || !renderer.enabled) continue;
            var part = new GameObject(filter.name, typeof(MeshFilter), typeof(MeshRenderer));
            part.transform.SetParent(display.transform, false);
            Matrix4x4 local = source.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            part.transform.SetLocalPositionAndRotation(local.GetColumn(3), local.rotation);
            part.transform.localScale = local.lossyScale;
            part.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            part.GetComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            var meshBounds = filter.sharedMesh.bounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 vertex = local.MultiplyPoint3x4(meshBounds.center + Vector3.Scale(meshBounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                if (hasBounds) bounds.Encapsulate(vertex);
                else { bounds = new Bounds(vertex, Vector3.zero); hasBounds = true; }
            }
        }
        if (!hasBounds) { Destroy(display); return false; }

        var presentation = display.AddComponent<LevelProp>();
        presentation.gameplayMaterial = displayGameplayMaterial;
        presentation.enabled = false;

        float scale = displaySize / Mathf.Max(.01f, bounds.size.x, bounds.size.y, bounds.size.z);
        display.transform.localScale = Vector3.one * scale;
        display.transform.localPosition = new Vector3((slot - (memories.Length - 1) * .5f) * slotSpacing, .015f, 0)
            - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
        var collider = display.AddComponent<BoxCollider>();
        collider.center = bounds.center;
        collider.size = Vector3.Max(bounds.size, Vector3.one * .055f);
        var anchor = new GameObject("Inspect").transform;
        anchor.SetParent(display.transform, false);
        anchor.localPosition = bounds.center;
        var inspect = display.AddComponent<BossMemorySlot>();
        inspect.interactionAnchor = anchor;
        inspect.Initialize(this, slot, presentation, previewMaterial);
        return true;
    }

    internal void Press(PlayerInteraction actor, bool start)
    {
        if (!Available || actor == null) return;
        var player = actor.GetComponent<PlayerController>();
        if (player == null) return;
        if (start)
        {
            if (!Ready || Boss == null || !Boss.TryStartFromGate(player)) return;
            StorySubtitles.Clear();
            RefreshButtons();
            return;
        }

        StorySubtitles.Clear();
        var world = GetComponentInParent<LevelWorld>();
        if (world == null)
            foreach (var candidate in FindObjectsByType<LevelWorld>())
                if (candidate.player == player) { world = candidate; break; }
        if (world != null)
        {
            foreach (var opening in FindObjectsByType<SoundShellOpening>())
                if (opening.world == world && opening.TryReturnAfterBossEscape(player))
                {
                    leaving = true;
                    RefreshButtons();
                    return;
                }
            // A failed or busy transition must not bypass the cover with an instant teleport.
            return;
        }

        if (BossEscapeTransition.Begin(player, escapeFadeOutSeconds, escapeFadeInSeconds))
        {
            leaving = true;
            RefreshButtons();
        }
    }
}
