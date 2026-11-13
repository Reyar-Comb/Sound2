using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Independent runtime style switches; face centre stays on wall projection.</summary>
[RequireComponent(typeof(BlackBoxProximitySource))]
public sealed class BlackBoxCenterControls : MonoBehaviour
{
    private BlackBoxProximitySource source;
    private PlayerController player;
    private bool menuOpen;
    private Font font;
    private GUIStyle panelStyle, titleStyle, buttonStyle, hintStyle;
    private InputActionMap shortcuts;
    private readonly string[] labels = { "1  面点阵", "2  边线", "3  星点", "4  随机显隐" };

    private void OnEnable()
    {
        source = GetComponent<BlackBoxProximitySource>();
        player = GetComponent<PlayerController>();
        if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
        // Populate all UI sizes before IMGUI caches text geometry in its first frame.
        const string characters = "面点阵中心：2 墙面投影点（已锁定） 1 面点阵 2 边线 3 星点 4 随机显隐 开 关 点击独立切换 Tab / Esc 返回移动 解锁鼠标";
        foreach (int size in new[] { 13, 16, 18 }) font.RequestCharactersInTexture(characters, size);
        panelStyle = titleStyle = buttonStyle = hintStyle = null;
        shortcuts = new InputActionMap("Point Cloud Style Controls");
        for (int i = 0; i < labels.Length; i++)
        {
            var style = (BlackBoxProximitySource.PointStyle)i;
            shortcuts.AddAction(style.ToString(), InputActionType.Button, "<Keyboard>/" + (i + 1))
                .performed += _ => source.SetStyleEnabled(style, !source.IsStyleEnabled(style));
        }
        shortcuts.AddAction("Menu", InputActionType.Button, "<Keyboard>/tab").performed += _ => SetMenuOpen(!menuOpen);
        shortcuts.AddAction("Close", InputActionType.Button, "<Keyboard>/escape").performed += _ => { if (menuOpen) SetMenuOpen(false); };
        shortcuts.Enable();
    }

    public void SetMenuOpen(bool open)
    {
        menuOpen = open;
        if (player != null) player.InputSuspended = open;
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    private void OnGUI()
    {
        if (source == null) return;
        if (panelStyle == null)
        {
            panelStyle = new GUIStyle(GUI.skin.box);
            titleStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18 };
            buttonStyle = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16 };
            hintStyle = new GUIStyle(GUI.skin.label) { font = font, fontSize = 13 };
        }
        float scale = Mathf.Min(1f, Screen.width / 510f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUI.Box(new Rect(16, 16, 476, 169), GUIContent.none, panelStyle);
        GUI.Label(new Rect(30, 25, 445, 28), "面点阵中心：2 墙面投影点（已锁定）", titleStyle);
        for (int i = 0; i < labels.Length; i++)
        {
            var style = (BlackBoxProximitySource.PointStyle)i;
            bool enabled = source.IsStyleEnabled(style);
            string label = labels[i] + (enabled ? "：开" : "：关");
            bool selected = GUI.Toggle(new Rect(30 + (i % 2) * 228, 61 + (i / 2) * 41, 220, 34),
                enabled, label, buttonStyle);
            if (selected != enabled) source.SetStyleEnabled(style, selected);
        }
        string hint = menuOpen ? "点击独立切换 · Tab / Esc 返回移动" : "1 / 2 / 3 / 4 独立切换 · Tab 解锁鼠标";
        GUI.Label(new Rect(30, 147, 450, 25), hint, hintStyle);
        GUI.matrix = previous;
    }

    private void OnDisable()
    {
        shortcuts?.Dispose();
        shortcuts = null;
        if (player != null) player.InputSuspended = false;
        if (menuOpen && Application.isPlaying && player != null && player.isActiveAndEnabled)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        menuOpen = false;
    }

    private void OnDestroy()
    {
        if (font != null) Destroy(font);
    }
}
