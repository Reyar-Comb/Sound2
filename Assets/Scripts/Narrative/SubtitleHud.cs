using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal static class SubtitleHud
{
    internal static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        if (parent != null) root.transform.SetParent(parent, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1;
        return canvas;
    }

    internal static TextMeshProUGUI CreateSubtitle(Transform parent, float size)
    {
        var text = CreateText("Subtitle", parent, size);
        text.alignment = TextAlignmentOptions.Center;
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(.15f, 0);
        rect.anchorMax = new Vector2(.85f, 0);
        rect.pivot = new Vector2(.5f, 0);
        rect.anchoredPosition = new Vector2(0, 44);
        rect.sizeDelta = new Vector2(0, 100);
        return text;
    }

    internal static TextMeshProUGUI CreateText(string name, Transform parent, float size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        var theme = GameUiTheme.Current;
        if (theme != null && theme.textFont != null) text.font = theme.textFont;
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.raycastTarget = false;
        text.color = Color.white;
        text.richText = false;
        return text;
    }
}
