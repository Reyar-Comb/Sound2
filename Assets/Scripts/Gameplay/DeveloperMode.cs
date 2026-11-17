using UnityEngine;

internal static class DeveloperMode
{
    public static bool Available => Debug.isDebugBuild;
    public static bool Enabled { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => Enabled = false;

    public static void ResetForNewGame() => Reset();

    public static void Toggle()
    {
        if (!Available) return;
        Enabled = !Enabled;
        foreach (var level in Object.FindObjectsByType<Sound2.LevelEditing.LevelLayout>())
            level.SetGameplayPresentation(true);
        foreach (var prop in Object.FindObjectsByType<Sound2.LevelEditing.LevelProp>())
            prop.RefreshGameplayPresentation();
        Debug.Log(Enabled ? "Developer mode enabled (F9)" : "Developer mode disabled (F9)");
    }
}
