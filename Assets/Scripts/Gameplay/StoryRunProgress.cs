using System.Collections.Generic;
using UnityEngine;

internal sealed class StoryRunProgress : ICollectibleProgress
{
    internal static readonly StoryRunProgress Current = new();
    readonly HashSet<string> collected = new(System.StringComparer.Ordinal);
    bool enteredBossRoom, startedBoss, diedAfterBossStarted, defeatedBoss, bossReturnPending;
    public bool EnteredBossRoom { get { LoadEditorProgress(); return enteredBossRoom; } }
    public bool StartedBoss { get { LoadEditorProgress(); return startedBoss; } }
    public bool DiedAfterBossStarted { get { LoadEditorProgress(); return diedAfterBossStarted; } }
    public bool DefeatedBoss { get { LoadEditorProgress(); return defeatedBoss; } }
    public bool BossReturnPending { get { LoadEditorProgress(); return bossReturnPending; } }
    public event System.Action EndingProgressChanged;

    public void EnterBossRoom()
    {
        LoadEditorProgress();
        if (enteredBossRoom) return;
        enteredBossRoom = true; SaveEndingProgress();
    }

    public void StartBoss()
    {
        LoadEditorProgress();
        if (startedBoss) return;
        enteredBossRoom = startedBoss = true; SaveEndingProgress();
    }

    public void RecordPlayerDeath()
    {
        LoadEditorProgress();
        if (!startedBoss || diedAfterBossStarted) return;
        diedAfterBossStarted = true; SaveEndingProgress();
    }

    public void CompleteBoss()
    {
        LoadEditorProgress();
        enteredBossRoom = startedBoss = defeatedBoss = bossReturnPending = true;
        SaveEndingProgress();
    }

    public void CompleteBossReturn()
    {
        LoadEditorProgress();
        bossReturnPending = false;
        SaveEndingProgress();
    }

    void SaveEndingProgress()
    {
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            UnityEditor.SessionState.SetBool(EditorProgressKey + ".EnteredBoss", enteredBossRoom);
            UnityEditor.SessionState.SetBool(EditorProgressKey + ".StartedBoss", startedBoss);
            UnityEditor.SessionState.SetBool(EditorProgressKey + ".BossDeath", diedAfterBossStarted);
            UnityEditor.SessionState.SetBool(EditorProgressKey + ".BossVictory", defeatedBoss);
            UnityEditor.SessionState.SetBool(EditorProgressKey + ".BossReturnPending", bossReturnPending);
        }
#endif
        EndingProgressChanged?.Invoke();
    }

    void ResetRunState()
    {
        collected.Clear();
        enteredBossRoom = startedBoss = diedAfterBossStarted = defeatedBoss = bossReturnPending = false;
    }

    public IReadOnlyCollection<string> CollectedIds
    {
        get
        {
            LoadEditorProgress();
            return collected;
        }
    }

    public bool IsCollected(string id)
    {
        LoadEditorProgress();
        return collected.Contains(id);
    }

    public bool TryCollect(string id)
    {
        LoadEditorProgress();
        if (string.IsNullOrWhiteSpace(id) || !collected.Add(id)) return false;
#if UNITY_EDITOR
        if (Application.isPlaying)
            UnityEditor.SessionState.SetString(EditorProgressKey, string.Join("\n", collected));
#endif
        return true;
    }

#if UNITY_EDITOR
    const string EditorProgressKey = "Sound2.StoryRunProgress";
    bool loadedEditorProgress;

    // A script reload can happen during a run. Clear the recovery data only on a new Play transition.
    [UnityEditor.InitializeOnLoadMethod]
    static void WatchEditorPlayMode() => UnityEditor.EditorApplication.playModeStateChanged += ResetEditorRun;

    static void ResetEditorRun(UnityEditor.PlayModeStateChange state)
    {
        if (state != UnityEditor.PlayModeStateChange.ExitingEditMode) return;
        UnityEditor.SessionState.EraseString(EditorProgressKey);
        UnityEditor.SessionState.EraseBool(EditorProgressKey + ".EnteredBoss");
        UnityEditor.SessionState.EraseBool(EditorProgressKey + ".StartedBoss");
        UnityEditor.SessionState.EraseBool(EditorProgressKey + ".BossDeath");
        UnityEditor.SessionState.EraseBool(EditorProgressKey + ".BossVictory");
        UnityEditor.SessionState.EraseBool(EditorProgressKey + ".BossReturnPending");
        Current.ResetRunState();
        Current.loadedEditorProgress = false;
    }
#else
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRun() => Current.ResetRunState();
#endif

    void LoadEditorProgress()
    {
#if UNITY_EDITOR
        if (loadedEditorProgress || !Application.isPlaying) return;
        loadedEditorProgress = true;
        var saved = UnityEditor.SessionState.GetString(EditorProgressKey, "");
        collected.UnionWith(saved.Split('\n', System.StringSplitOptions.RemoveEmptyEntries));
        enteredBossRoom = UnityEditor.SessionState.GetBool(EditorProgressKey + ".EnteredBoss", false);
        startedBoss = UnityEditor.SessionState.GetBool(EditorProgressKey + ".StartedBoss", false);
        diedAfterBossStarted = UnityEditor.SessionState.GetBool(EditorProgressKey + ".BossDeath", false);
        defeatedBoss = UnityEditor.SessionState.GetBool(EditorProgressKey + ".BossVictory", false);
        bossReturnPending = UnityEditor.SessionState.GetBool(EditorProgressKey + ".BossReturnPending", false);
#endif
    }
}
