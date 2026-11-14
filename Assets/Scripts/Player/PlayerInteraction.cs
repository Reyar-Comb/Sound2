using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>One focus decision drives both the world-anchored HUD hint and mouse interaction.</summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerController), typeof(PlayerPickup))]
public sealed class PlayerInteraction : MonoBehaviour
{
    public Camera viewCamera;
    [Header("Focus (uses PlayerPickup's range collider and layer masks)")]
    [Range(1f, 30f)] public float aimAngle = 8f;
    [Range(0f, 5f)] public float focusHysteresis = 1.5f;
    [Header("HUD (pixels at 720p)")]
    [Min(.5f)] public float crosshairRadius = 2f;
    [Min(0f)] public float pointClearRadius = 14f;
    [Min(.1f)] public float pointClearFeather = 6f;
    [Min(3f)] public float ringRadius = 18f;
    [Min(.5f)] public float ringThickness = 2f;
    [Min(.05f)] public float revealTime = .28f;
    [Min(.05f)] public float hideTime = .18f;

    public InteractableObject FocusedTarget { get; private set; }
    public float RingProgress => progress;
    public PlayerPickup Pickup => pickup;
    public bool HudVisible => isActiveAndEnabled && controller != null && controller.isActiveAndEnabled
        && !controller.InputSuspended && viewCamera != null && viewCamera.isActiveAndEnabled
        && viewCamera.targetTexture == null;
    // Per-camera property block; other cameras and Scene view never inherit the clear zone.
    public Vector4 PointClearZone => HudVisible
        ? new Vector4(.5f, .5f, Mathf.Max(pointClearRadius, crosshairRadius + 2), pointClearFeather) : Vector4.zero;

    PlayerController controller;
    PlayerPickup pickup;
    Canvas hud;
    RectTransform hudRect;
    ReticleGraphic dot, ring;
    InteractableObject shownTarget;
    float progress;
    Collider[] candidates = new Collider[32];
    RaycastHit[] hits = new RaycastHit[16];

    void Awake()
    {
        controller = GetComponent<PlayerController>();
        pickup = GetComponent<PlayerPickup>();
        if (viewCamera == null) viewCamera = GetComponentInChildren<Camera>();
        var go = new GameObject("InteractionHUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        go.transform.SetParent(transform, false);
        hud = go.GetComponent<Canvas>();
        hud.renderMode = RenderMode.ScreenSpaceOverlay;
        hud.sortingOrder = 20;
        hudRect = go.GetComponent<RectTransform>();
        var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1;
        ring = CreateGraphic("InteractionRing");
        dot = CreateGraphic("CrosshairDot");
        ring.gameObject.SetActive(false);
    }

    ReticleGraphic CreateGraphic(string objectName)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(ReticleGraphic));
        go.transform.SetParent(hud.transform, false);
        var graphic = go.GetComponent<ReticleGraphic>();
        graphic.raycastTarget = false;
        graphic.rectTransform.sizeDelta = Vector2.one * 64;
        return graphic;
    }

    void LateUpdate()
    {
        hud.enabled = HudVisible;
        if (!HudVisible)
        {
            ClearFocus();
            return;
        }
        Place(dot, viewCamera.pixelRect.center);
        dot.SetShape(crosshairRadius, crosshairRadius * 2, 1);
        RefreshFocus();
        UpdateRing();
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame
            && !(Keyboard.current != null && pickup.interactionKey != Key.None
                && Keyboard.current[pickup.interactionKey].wasPressedThisFrame))
        {
            if (pickup.HeldItem != null) pickup.TryThrow();
            else TryInteractFocused();
        }
    }

    public void RefreshFocus()
    {
        if (!HudVisible || !pickup.isActiveAndEnabled || pickup.HeldItem != null || !pickup.TryGetRange(out var center, out float radius))
        {
            FocusedTarget = null;
            return;
        }
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(center, radius, candidates, pickup.pickupLayers, QueryTriggerInteraction.Ignore)) == candidates.Length)
            Array.Resize(ref candidates, candidates.Length * 2);
        InteractableObject best = null;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var target = candidates[i].GetComponentInParent<InteractableObject>();
            if (target == null || target.transform.IsChildOf(transform) || !target.CanInteract(this)) continue;
            Vector3 viewport = viewCamera.WorldToViewportPoint(target.InteractionPosition);
            if (viewport.z <= viewCamera.nearClipPlane || viewport.x <= 0 || viewport.x >= 1 || viewport.y <= 0 || viewport.y >= 1) continue;
            float angle = Vector3.Angle(viewCamera.transform.forward, target.InteractionPosition - viewCamera.transform.position);
            bool current = target == FocusedTarget;
            if (angle > aimAngle + (current ? focusHysteresis : 0)) continue;
            float score = angle - (current ? focusHysteresis : 0);
            if (score >= bestScore || !HasLineOfSight(target)) continue;
            best = target;
            bestScore = score;
        }
        FocusedTarget = best;
    }

    bool HasLineOfSight(InteractableObject target)
    {
        Vector3 origin = viewCamera.transform.position, delta = target.InteractionPosition - origin;
        int count;
        while ((count = Physics.RaycastNonAlloc(origin, delta.normalized, hits, delta.magnitude,
            pickup.obstacleLayers, QueryTriggerInteraction.Ignore)) == hits.Length)
            Array.Resize(ref hits, hits.Length * 2);
        for (int i = 0; i < count; i++)
            if (!hits[i].transform.IsChildOf(transform) && hits[i].collider.GetComponentInParent<InteractableObject>() != target) return false;
        return true;
    }

    public bool TryInteractFocused()
    {
        RefreshFocus(); // Revalidate distance, availability and occlusion at the moment of the click.
        if (FocusedTarget == null || shownTarget != FocusedTarget || progress <= .05f) return false;
        bool interacted = FocusedTarget.TryInteract(this);
        RefreshFocus();
        return interacted;
    }

    void UpdateRing()
    {
        if (shownTarget == null || !shownTarget.CanInteract(this) || !HasLineOfSight(shownTarget))
        {
            shownTarget = null;
            progress = 0;
        }
        if (shownTarget == null) shownTarget = FocusedTarget;
        bool reveal = shownTarget != null && shownTarget == FocusedTarget;
        progress = Mathf.MoveTowards(progress, reveal ? 1 : 0, Time.unscaledDeltaTime / (reveal ? revealTime : hideTime));
        if (progress <= 0)
        {
            shownTarget = FocusedTarget;
            ring.gameObject.SetActive(false);
            return;
        }
        if (shownTarget == null) { ring.gameObject.SetActive(false); return; }
        Vector3 screen = viewCamera.WorldToScreenPoint(shownTarget.InteractionPosition);
        ring.gameObject.SetActive(screen.z > viewCamera.nearClipPlane && viewCamera.pixelRect.Contains(screen));
        Place(ring, screen);
        // Quintic smootherstep has zero speed and acceleration at both ends.
        float eased = progress * progress * progress * (progress * (progress * 6 - 15) + 10);
        ring.SetShape(Mathf.Lerp(crosshairRadius, ringRadius, eased), ringThickness, eased);
    }

    void Place(ReticleGraphic graphic, Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(hudRect, screen, null, out var local);
        graphic.rectTransform.anchoredPosition = local;
    }

    void ClearFocus()
    {
        FocusedTarget = shownTarget = null;
        progress = 0;
        if (ring != null) ring.gameObject.SetActive(false);
    }

    void OnDisable()
    {
        ClearFocus();
        if (hud != null) hud.enabled = false;
    }

    void OnDestroy() { if (hud != null) Destroy(hud.gameObject); }
}
