using TMPro;
using UnityEngine;

[CreateAssetMenu(menuName = "Sound2/Game UI Theme")]
public sealed class GameUiTheme : ScriptableObject
{
    public TMP_FontAsset textFont;
    public Font legacyFont;
    [Min(0)] public float pointPadding = 6;
    [Min(.1f)] public float pointFeather = 6;
    static GameUiTheme current;
    static float nextFontRefresh;
    public static GameUiTheme Current => current != null ? current : current = Resources.Load<GameUiTheme>("GameUiTheme");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() { current = null; nextFontRefresh = 0; }

    public static void ApplySceneFonts()
    {
        if (Time.unscaledTime < nextFontRefresh) return;
        nextFontRefresh = Time.unscaledTime + .25f;
        var theme = Current;
        if (theme == null) return;
        if (theme.textFont != null)
            foreach (var text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include))
                if (text.font != theme.textFont) text.font = theme.textFont;
        if (theme.legacyFont != null)
            foreach (var text in Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsInactive.Include))
                if (text.font != theme.legacyFont) text.font = theme.legacyFont;
    }
}
