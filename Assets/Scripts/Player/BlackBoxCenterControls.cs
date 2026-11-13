using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Small native Unity UI for comparing the three point-cloud centres.</summary>
[RequireComponent(typeof(BlackBoxProximitySource))]
public sealed class BlackBoxCenterControls : MonoBehaviour
{
    private BlackBoxProximitySource source;
    private PlayerController player;
    private bool menuOpen;
    private Font font;
    private GUIStyle panelStyle, titleStyle, buttonStyle, hintStyle;
    private InputActionMap shortcuts;
    private readonly string[] labels = { "1  视线投影点", "2  墙面投影点", "3  两点中点" };

    private void OnEnable()
    {
        source = GetComponent<BlackBoxProximitySource>();
        player = GetComponent<PlayerController>();
        if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
        // Populate all UI sizes before IMGUI caches text geometry in its first frame.
        const string characters = "点云中心 · 1 视线投影点 2 墙面投影点 3 两点中点 点击切换 Tab / Esc 返回移动 解锁鼠标 | 未命中：墙面投影";
        foreach (int size in new[] { 13, 16, 18 }) font.RequestCharactersInTexture(characters, size);
        panelStyle = titleStyle = buttonStyle = hintStyle = null;
        shortcuts = new InputActionMap("Point Cloud Centre Controls");
        shortcuts.AddAction("Gaze", InputActionType.Button, "<Keyboard>/1").performed += _ => source.Mode = BlackBoxProximitySource.CenterMode.Gaze;
        shortcuts.AddAction("Wall", InputActionType.Button, "<Keyboard>/2").performed += _ => source.Mode = BlackBoxProximitySource.CenterMode.WallProjection;
        shortcuts.AddAction("Midpoint", InputActionType.Button, "<Keyboard>/3").performed += _ => source.Mode = BlackBoxProximitySource.CenterMode.Midpoint;
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
        GUI.Box(new Rect(16, 16, 476, 128), GUIContent.none, panelStyle);
        GUI.Label(new Rect(30, 25, 445, 28), "点云中心 · " + labels[(int)source.Mode].Substring(3), titleStyle);
        int selected = GUI.Toolbar(new Rect(30, 61, 448, 34), (int)source.Mode, labels, buttonStyle);
        if (selected != (int)source.Mode) source.Mode = (BlackBoxProximitySource.CenterMode)selected;
        string hint = menuOpen ? "点击切换 · Tab / Esc 返回移动" : "1 / 2 / 3 切换 · Tab 解锁鼠标";
        if (!source.HasGazeHit) hint += "  |  未命中：墙面投影";
        GUI.Label(new Rect(30, 108, 450, 25), hint, hintStyle);
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
