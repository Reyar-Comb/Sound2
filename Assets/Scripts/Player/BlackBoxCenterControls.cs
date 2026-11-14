using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

/// <summary>Connects authored scene UI and input-asset actions to point styles.</summary>
[RequireComponent(typeof(BlackBoxProximitySource))]
public sealed class BlackBoxCenterControls : MonoBehaviour
{
    [SerializeField] private CanvasGroup panel;
    [SerializeField] private Toggle[] styleToggles;
    [SerializeField] private TMP_Text hint;
    [SerializeField, TextArea] private string movementHint;
    [SerializeField, TextArea] private string menuHint;
    [SerializeField] private InputSystemUIInputModule uiInputModule;
    [SerializeField] private InputActionReference[] styleShortcuts;
    [SerializeField] private InputActionReference menuAction;
    [SerializeField] private InputActionReference closeAction;

    private BlackBoxProximitySource source;
    private PlayerController player;
    private bool menuOpen;
    public bool MenuOpen => menuOpen;

    private void OnEnable()
    {
        source = GetComponent<BlackBoxProximitySource>();
        player = GetComponent<PlayerController>();
        // Project-wide Input Actions own their lifetime; this component only owns its callbacks.
        foreach (var reference in styleShortcuts)
        {
            reference.action.performed += OnStyleShortcut;
        }
        menuAction.action.performed += OnMenu;
        closeAction.action.performed += OnClose;
        SetMenuOpen(false);
        SyncToggles();
    }

    private void OnStyleShortcut(InputAction.CallbackContext context)
    {
        for (int i = 0; i < styleShortcuts.Length; i++)
            if (context.action == styleShortcuts[i].action)
            {
                var style = (BlackBoxProximitySource.PointStyle)i;
                SetStyle(style, !source.IsStyleEnabled(style));
                break;
            }
    }
    private void OnMenu(InputAction.CallbackContext context) => SetMenuOpen(!menuOpen);
    private void OnClose(InputAction.CallbackContext context) { if (menuOpen) SetMenuOpen(false); }

    // These methods are wired to Toggle.onValueChanged in the saved scene.
    public void SetFacePoints(bool value) => SetStyle(BlackBoxProximitySource.PointStyle.Face, value);
    public void SetEdgePoints(bool value) => SetStyle(BlackBoxProximitySource.PointStyle.Edge, value);
    public void SetStars(bool value) => SetStyle(BlackBoxProximitySource.PointStyle.Stars, value);
    public void SetTwinkle(bool value) => SetStyle(BlackBoxProximitySource.PointStyle.Twinkle, value);

    private void SetStyle(BlackBoxProximitySource.PointStyle style, bool value)
    {
        source.SetStyleEnabled(style, value);
        SyncToggles();
    }
    private void SyncToggles()
    {
        for (int i = 0; i < styleToggles.Length; i++)
            styleToggles[i].SetIsOnWithoutNotify(source.IsStyleEnabled((BlackBoxProximitySource.PointStyle)i));
    }

    public void SetMenuOpen(bool open)
    {
        menuOpen = open;
        if (player != null) player.InputSuspended = open;
        if (panel != null) { panel.alpha = open ? 1 : 0; panel.interactable = open; panel.blocksRaycasts = open; }
        if (uiInputModule != null) uiInputModule.enabled = open;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(open && styleToggles.Length > 0 ? styleToggles[0].gameObject : null);
        if (hint != null) { hint.text = open ? menuHint : movementHint; hint.gameObject.SetActive(open); }
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    private void OnDisable()
    {
        if (styleShortcuts != null)
            foreach (var reference in styleShortcuts)
                if (reference != null && reference.action != null)
                {
                    reference.action.performed -= OnStyleShortcut;
                }
        if (menuAction != null && menuAction.action != null)
        { menuAction.action.performed -= OnMenu; }
        if (closeAction != null && closeAction.action != null)
        { closeAction.action.performed -= OnClose; }
        SetMenuOpen(false);
    }
}
