using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Collects visible player UI bounds once per frame; the grid pass fades whole dots.</summary>
public static class PlayerUiPointMask
{
    public const int Capacity = 128;
    static Canvas[] canvases = System.Array.Empty<Canvas>();
    static float nextDiscovery;
    static readonly Vector3[] corners = new Vector3[4];
    static readonly Dictionary<MonoBehaviour, Rect> legacy = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        canvases = System.Array.Empty<Canvas>(); nextDiscovery = 0; legacy.Clear();
    }

    public static void SetLegacyPanel(MonoBehaviour owner, Rect topLeftRect, bool visible)
    {
        if (visible) legacy[owner] = topLeftRect; else legacy.Remove(owner);
    }

    public static int Collect(Camera camera, Vector4[] rectangles, Vector4[] options)
    {
        if (!Application.isPlaying || camera.cameraType != CameraType.Game || camera.targetTexture != null) return 0;
        if (Time.unscaledTime >= nextDiscovery || canvases.Length == 0)
        {
            canvases = Object.FindObjectsByType<Canvas>();
            nextDiscovery = Time.unscaledTime + .25f;
        }
        int count = 0;
        var theme = GameUiTheme.Current;
        float padding = theme != null ? theme.pointPadding : 6, feather = theme != null ? theme.pointFeather : 6;
        foreach (var canvas in canvases)
        {
            if (canvas == null || !canvas.isActiveAndEnabled) continue;
            var root = canvas.rootCanvas;
            if (!root.isActiveAndEnabled || (root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera != camera)) continue;
            var projection = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : camera;
            var graphics = UnityEngine.UI.GraphicRegistry.GetGraphicsForCanvas(canvas);
            for (int graphicIndex = 0; graphicIndex < graphics.Count; graphicIndex++)
            {
                var graphic = graphics[graphicIndex];
                if (graphic == null || !graphic.isActiveAndEnabled || graphic.canvasRenderer.cull) continue;
                float opacity = graphic.color.a * graphic.canvasRenderer.GetInheritedAlpha();
                if (opacity <= .001f) continue;
                if (graphic is TMP_SubMeshUI) continue; // Covered by the parent text's bounds.
                Rect screen;
                float roundness = 0;
                if (graphic is TMP_Text text)
                {
                    if (theme != null && theme.textFont != null && text.font != theme.textFont) text.font = theme.textFont;
                    if (string.IsNullOrWhiteSpace(text.text)) continue;
                    if (text.havePropertiesChanged) text.ForceMeshUpdate();
                    var bounds = text.textBounds;
                    if (bounds.size.x <= 0 || bounds.size.y <= 0) continue;
                    screen = BoundsOnScreen(text.rectTransform, bounds.min, bounds.max, projection);
                }
                else if (graphic is ReticleGraphic reticle)
                {
                    if (graphic.name == "CrosshairDot") continue; // Dedicated circular reticle zone.
                    float r = reticle.Radius;
                    screen = BoundsOnScreen(reticle.rectTransform, new Vector3(-r, -r), new Vector3(r, r), projection);
                    roundness = Mathf.Min(screen.width, screen.height) * .5f;
                }
                else
                {
                    graphic.rectTransform.GetWorldCorners(corners);
                    screen = ScreenBounds(projection);
                    if (graphic is UnityEngine.UI.Text label && theme != null && theme.legacyFont != null) label.font = theme.legacyFont;
                }
                Add(screen, roundness, opacity, padding, feather, camera, rectangles, options, ref count);
            }
        }
        foreach (var panel in legacy)
            if (panel.Key != null && panel.Key.isActiveAndEnabled)
            {
                Rect r = panel.Value;
                r.y = Screen.height - r.yMax;
                Add(r, 0, 1, padding, feather, camera, rectangles, options, ref count);
            }
        return count;
    }

    static Rect BoundsOnScreen(RectTransform transform, Vector3 min, Vector3 max, Camera camera)
    {
        corners[0] = transform.TransformPoint(new Vector3(min.x, min.y));
        corners[1] = transform.TransformPoint(new Vector3(min.x, max.y));
        corners[2] = transform.TransformPoint(new Vector3(max.x, max.y));
        corners[3] = transform.TransformPoint(new Vector3(max.x, min.y));
        return ScreenBounds(camera);
    }

    static Rect ScreenBounds(Camera camera)
    {
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
        foreach (var corner in corners)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    static void Add(Rect rect, float rounded, float alpha, float padding, float feather, Camera camera,
        Vector4[] zones, Vector4[] options, ref int count)
    {
        Rect viewport = camera.pixelRect;
        if (!rect.Overlaps(viewport) || viewport.height <= 0) return;
        float scale = 720f / viewport.height;
        Vector2 center = (rect.center - viewport.position) * scale, size = rect.size * scale * .5f;
        if (count < Capacity)
        {
            zones[count] = new Vector4(center.x, center.y, size.x, size.y);
            options[count++] = new Vector4(padding, feather, rounded * scale, alpha);
        }
        else
        {
            // Never silently drop UI if a particularly dense menu exceeds the normal budget.
            var last = zones[Capacity - 1];
            Vector2 min = Vector2.Min(new Vector2(last.x - last.z, last.y - last.w), center - size);
            Vector2 max = Vector2.Max(new Vector2(last.x + last.z, last.y + last.w), center + size);
            center = (min + max) * .5f; size = (max - min) * .5f;
            zones[Capacity - 1] = new Vector4(center.x, center.y, size.x, size.y);
            options[Capacity - 1] = new Vector4(padding, feather, 0, 1);
        }
    }
}
