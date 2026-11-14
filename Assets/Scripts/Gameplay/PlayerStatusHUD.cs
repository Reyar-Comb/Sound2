using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Read-only player status using the shared font and automatic UI point exclusion.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerVitality))]
public sealed class PlayerStatusHUD : MonoBehaviour
{
    public bool visible = true;
    PlayerVitality vitality;
    PlayerController controller;
    Canvas canvas;
    TextMeshProUGUI label;

    void Awake()
    {
        vitality = GetComponent<PlayerVitality>();
        controller = GetComponent<PlayerController>();
        var root = new GameObject("PlayerStatusHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 25;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1;
        var textObject = new GameObject("Health", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(root.transform, false);
        label = textObject.GetComponent<TextMeshProUGUI>();
        if (GameUiTheme.Current != null && GameUiTheme.Current.textFont != null)
            label.font = GameUiTheme.Current.textFont;
        label.fontSize = 20;
        label.alignment = TextAlignmentOptions.TopRight;
        label.color = Color.white;
        label.raycastTarget = false;
        label.richText = false;
        var rect = label.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-32, -88);
        rect.sizeDelta = new Vector2(240, 140);
    }

    void Update()
    {
        GameUiTheme.ApplySceneFonts();
        canvas.enabled = visible && (controller == null || (controller.isActiveAndEnabled && !controller.InputSuspended));
        label.text = "生命  " + Mathf.CeilToInt(vitality.Health)
            + " / " + Mathf.CeilToInt(vitality.maximumHealth)
            + (vitality.IsDead ? "\n生命值耗尽" : "");
    }

    void OnDisable() { if (canvas != null) canvas.enabled = false; }
    void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
}
