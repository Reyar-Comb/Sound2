using System.Collections;
using Sound2.LevelEditing;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Starting-room tarot endings share one card/return sequence, without restarting the run.</summary>
[DisallowMultipleComponent]
public sealed class SoundShellOpening : MonoBehaviour
{
    public PlayerController player;
    public LevelWorld world;
    public LevelLayout startingRoom;
    public Transform spawn;
    public Texture2D foolCard;
    public Texture2D hermitCard;
    public Texture2D hangedManCard;
    [Min(.1f)] public float fallSeconds = 1.5f;
    [Min(.1f)] public float cardRushSeconds = .42f;
    [Min(.1f)] public float returnFadeSeconds = 1.1f;
    public enum Phase { Room, Falling, Covering, CardRush, AwaitingReturn, Returning }
    public Phase CurrentPhase { get; private set; }
    public bool InStartingRoom => world != null && world.CurrentLevel == startingRoom;
    public Texture2D WindowCard => StoryRunProgress.Current.DiedAfterBossStarted ? hangedManCard : foolCard;
    public Texture CurrentCard => card != null ? card.texture : null;
    public bool CanUseBed(PlayerController actor) => isActiveAndEnabled && actor == player && player != null
        && StoryRunProgress.Current.EnteredBossRoom && InStartingRoom && CurrentPhase == Phase.Room
        && !SonicSpaceStartMenu.BlocksGameplay && !player.InputSuspended && !player.TransportLocked
        && hermitCard != null && player.GetComponent<PlayerVitality>()?.IsDead != true;

    public bool TryUseBed(PlayerController actor)
    {
        if (!CanUseBed(actor)) return false;
        sequence = StartCoroutine(ShowCard(hermitCard));
        return true;
    }

    Canvas canvas;
    UnityEngine.UI.Image black;
    UnityEngine.UI.RawImage card;
    Coroutine sequence;
    float fallenFor;
    bool ownsInput, previousSuspended, returnArmed;

    void Awake()
    {
        if (player == null || world == null || startingRoom == null || spawn == null || foolCard == null)
        { Debug.LogError("Sound & Shell opening requires player, world, room, spawn and Fool card.", this); enabled = false; return; }
        var root = new GameObject("Tarot ending", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32500;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280,720); scaler.matchWidthOrHeight = 1;
        black = MakeGraphic<UnityEngine.UI.Image>("Black void", root.transform);
        black.rectTransform.anchorMin = Vector2.zero; black.rectTransform.anchorMax = Vector2.one;
        black.rectTransform.offsetMin = black.rectTransform.offsetMax = Vector2.zero;
        black.color = Color.clear;
        card = MakeGraphic<UnityEngine.UI.RawImage>("0 - The Fool", root.transform);
        card.texture = foolCard;
        card.rectTransform.anchorMin = card.rectTransform.anchorMax = new Vector2(.5f, .5f);
        card.rectTransform.pivot = new Vector2(.5f, .5f);
        card.rectTransform.anchoredPosition = Vector2.zero;
        FitCard();
        card.gameObject.SetActive(false); canvas.enabled = false;
    }

    void LateUpdate() => FitCard();

    void FitCard()
    {
        if (card == null || card.texture == null) return;
        // Refit when the window changes size; the rush animation uses uniform scale only.
        float aspect = (float)card.texture.width / card.texture.height;
        float availableWidth = 720f * Mathf.Max(1, Screen.width) / Mathf.Max(1, Screen.height);
        float height = Mathf.Min(620, Mathf.Max(1, availableWidth - 48) / aspect);
        card.rectTransform.sizeDelta = new Vector2(height * aspect, height);
    }

    static T MakeGraphic<T>(string name, Transform parent) where T : UnityEngine.UI.Graphic
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var graphic = go.AddComponent<T>(); graphic.raycastTarget = false; return graphic;
    }

    void Update()
    {
        if (!InStartingRoom || SonicSpaceStartMenu.BlocksGameplay) return;
        if (CurrentPhase == Phase.Room || CurrentPhase == Phase.Falling)
        {
            if (startingRoom.transform.InverseTransformPoint(player.transform.position).y < -.5f)
            {
                CurrentPhase = Phase.Falling; fallenFor += Time.deltaTime;
                if (fallenFor >= fallSeconds && WindowCard != null) sequence = StartCoroutine(ShowCard(WindowCard));
            }
            else { fallenFor = 0; CurrentPhase = Phase.Room; }
        }
        else if (CurrentPhase == Phase.AwaitingReturn)
        {
            if (Mouse.current?.leftButton.isPressed != true) returnArmed = true;
            if (returnArmed && Mouse.current?.leftButton.wasPressedThisFrame == true) ReturnToRoom();
        }
    }

    IEnumerator Animate(float seconds, System.Action<float> apply)
    {
        for (float time = 0; time < seconds; time += Time.unscaledDeltaTime)
        { apply(Mathf.Clamp01(time / seconds)); yield return null; }
        apply(1);
    }

    IEnumerator ShowCard(Texture2D texture)
    {
        CurrentPhase = Phase.Covering;
        card.texture = texture;
        card.gameObject.name = texture.name;
        FitCard();
        previousSuspended = player.InputSuspended; ownsInput = true;
        player.OpeningSequenceLocked = true; player.SetInputCaptured(false);
        player.GetComponent<PlayerPickup>()?.ClearForRespawn();
        canvas.enabled = true;
        yield return Animate(.3f, t => black.color = new Color(0,0,0,Mathf.SmoothStep(0,1,t)));
        CurrentPhase = Phase.CardRush;
        card.gameObject.SetActive(true); card.color = Color.white;
        yield return Animate(cardRushSeconds, t => {
            // Perspective approach: slow in the distance, accelerating sharply towards the lens.
            float scale = .045f / Mathf.Lerp(1, .045f/1.06f, t);
            card.rectTransform.localScale = Vector3.one * scale;
        });
        yield return Animate(.1f, t => card.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.06f,1,t));
        returnArmed = false; CurrentPhase = Phase.AwaitingReturn; sequence = null;
    }

    public void ReturnToRoom()
    {
        if (CurrentPhase != Phase.AwaitingReturn || sequence != null) return;
        sequence = StartCoroutine(Return());
    }

    IEnumerator Return()
    {
        CurrentPhase = Phase.Returning;
        yield return Animate(.25f, t => card.color = new Color(1,1,1,1-t));
        card.gameObject.SetActive(false);
        yield return world.ResetCurrentLevel();
        player.Teleport(spawn.position); player.FaceDirection(spawn.forward);
        player.GetComponent<PlayerFloorProgression>()?.ResetTraversalSample();
        Physics.SyncTransforms();
        yield return Animate(returnFadeSeconds, t => black.color = new Color(0,0,0,1-Mathf.SmoothStep(0,1,t)));
        // Do not pass the returning click to a world interaction on this frame.
        yield return new WaitForEndOfFrame();
        canvas.enabled = false; ReleaseInput(); fallenFor = 0; CurrentPhase = Phase.Room; sequence = null;
    }

    void ReleaseInput()
    {
        if (!ownsInput) return;
        ownsInput = false;
        if (player != null) { player.OpeningSequenceLocked = false; player.SetInputCaptured(!previousSuspended); }
    }
    void OnDisable()
    {
        StopAllCoroutines(); sequence = null; ReleaseInput();
        if (canvas != null) canvas.enabled = false;
        fallenFor = 0; CurrentPhase = Phase.Room;
    }
    void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
}
