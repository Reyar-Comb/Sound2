using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Owns the cover across loading the campaign from the independent Boss test scene.</summary>
public sealed class BossEscapeTransition : MonoBehaviour
{
    static BossEscapeTransition active;
    public static bool LoadingCampaign => active != null && active.loadingCampaign;
    Image cover;
    PlayerController passenger;
    bool previousSuspended, previousOpeningLock, loadingCampaign;
    float fadeOut, fadeIn;

    public static bool Begin(PlayerController actor, float outSeconds, float inSeconds)
    {
        if (active != null || actor == null) return false;
        var root = new GameObject("Boss escape scene transition", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        DontDestroyOnLoad(root);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
        active = root.AddComponent<BossEscapeTransition>();
        active.fadeOut = Mathf.Max(.1f, outSeconds); active.fadeIn = Mathf.Max(.1f, inSeconds);
        var overlay = new GameObject("Black cover", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(root.transform, false);
        active.cover = overlay.GetComponent<Image>();
        active.cover.raycastTarget = false; active.cover.color = Color.clear;
        var rect = active.cover.rectTransform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        active.Capture(actor);
        active.StartCoroutine(active.Run());
        return true;
    }

    void Capture(PlayerController actor)
    {
        passenger = actor;
        previousSuspended = actor.InputSuspended; previousOpeningLock = actor.OpeningSequenceLocked;
        actor.OpeningSequenceLocked = true; actor.SetInputCaptured(false);
    }

    IEnumerator Fade(float from, float to, float seconds)
    {
        for (float elapsed = 0; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            cover.color = new Color(0, 0, 0, Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, elapsed / seconds)));
            yield return null;
        }
        cover.color = new Color(0, 0, 0, to);
    }

    IEnumerator Run()
    {
        yield return Fade(0, 1, fadeOut);
        // Render the complete cover before any scene unload callbacks run.
        yield return new WaitForEndOfFrame();
        passenger.GetComponent<PlayerPickup>()?.ClearForRespawn();
        loadingCampaign = true;
        const string campaign = "Assets/Scenes/SoundAndShell.unity";
#if UNITY_EDITOR
        var loading = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(campaign,
            new LoadSceneParameters(LoadSceneMode.Single));
#else
        var loading = SceneManager.LoadSceneAsync(campaign, LoadSceneMode.Single);
#endif
        if (loading != null)
        {
            yield return loading;
            var opening = FindAnyObjectByType<SoundShellOpening>();
            if (opening != null && opening.player != null) Capture(opening.player);
            yield return null;
        }
        else Debug.LogError("独立 Boss 场景无法返回主流程。", this);
        loadingCampaign = false;
        yield return Fade(1, 0, fadeIn);
        yield return new WaitForEndOfFrame();
        Destroy(gameObject);
    }

    void OnDisable()
    {
        if (passenger != null)
        {
            passenger.OpeningSequenceLocked = previousOpeningLock;
            passenger.SetInputCaptured(!previousSuspended);
        }
        if (active == this) active = null;
    }
}
