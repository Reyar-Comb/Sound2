using UnityEngine;

internal static class DeveloperMode
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public static bool Enabled { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => Enabled = false;

    public static void Toggle()
    {
        Enabled = !Enabled;
        foreach (var level in Object.FindObjectsByType<Sound2.LevelEditing.LevelLayout>())
            level.SetGameplayPresentation(true);
        foreach (var prop in Object.FindObjectsByType<Sound2.LevelEditing.LevelProp>())
            prop.RefreshGameplayPresentation();
        Debug.Log(Enabled ? "Developer mode enabled (F9)" : "Developer mode disabled (F9)");
    }
#else
    public static bool Enabled => false;
#endif
}
