using System.Collections.Generic;
using UnityEngine;

internal sealed class StoryRunProgress : ICollectibleProgress
{
    internal static readonly StoryRunProgress Current = new();
    readonly HashSet<string> collected = new(System.StringComparer.Ordinal);

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
        Current.collected.Clear();
        Current.loadedEditorProgress = false;
    }
#else
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRun() => Current.collected.Clear();
#endif

    void LoadEditorProgress()
    {
#if UNITY_EDITOR
        if (loadedEditorProgress || !Application.isPlaying) return;
        loadedEditorProgress = true;
        var saved = UnityEditor.SessionState.GetString(EditorProgressKey, "");
        collected.UnionWith(saved.Split('\n', System.StringSplitOptions.RemoveEmptyEntries));
#endif
    }
}
