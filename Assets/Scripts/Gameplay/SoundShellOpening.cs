using System.Collections;
using Sound2.LevelEditing;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Starting-room tarot endings share one card presentation and return to the title.</summary>
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
    public Texture2D worldCard;
    [Tooltip("可选牌背。留空时使用深色细框牌背。")]
    public Texture2D cardBackTexture;
    public StartingRoomMusic roomMusic = new();
    public EndingCreditsAsset endingCredits;
    public SonicSpaceStartMenu endingTitle;
    [Min(.1f)] public float fallSeconds = 1.5f;
    [Min(0)] public float cardFadeInSeconds = .45f;
    [Min(0)] public float cardBackHoldSeconds = .25f;
    [UnityEngine.Serialization.FormerlySerializedAs("cardRushSeconds"), Min(.1f)]
    public float cardFlipSeconds = 1.6f;
    [Min(.1f)] public float returnFadeSeconds = 1.1f;
    [Min(.1f), Tooltip("Boss 胜利回到初始房间后，白屏淡出的时间。")]
    public float bossReturnFadeSeconds = 2;
    [Header("逃避返回初始房间")]
    [Min(0)] public float escapeFadeOutSeconds = .8f;
    [Min(0)] public float escapeFadeInSeconds = 1.2f;
    public enum Phase { Room, Falling, Covering, CardFlip, AwaitingReturn, Returning, ExitMusic }
    public Phase CurrentPhase { get; private set; }
    public bool InStartingRoom => world != null && world.CurrentLevel == startingRoom;
    public Texture2D WindowCard => StoryRunProgress.Current.DiedAfterBossStarted ? hangedManCard : foolCard;
    public Texture CurrentCard => cardFace;
    public bool CanUseBed(PlayerController actor) => isActiveAndEnabled && actor == player && player != null
        && StoryRunProgress.Current.EnteredBossRoom && !StoryRunProgress.Current.DefeatedBoss && InStartingRoom && CurrentPhase == Phase.Room
        && !SonicSpaceStartMenu.BlocksGameplay && !player.InputSuspended && !player.TransportLocked
        && hermitCard != null && player.GetComponent<PlayerVitality>()?.IsDead != true;

    public bool TryUseBed(PlayerController actor)
    {
        if (!CanUseBed(actor)) return false;
        sequence = StartCoroutine(ShowCard(hermitCard));
        return true;
    }

    public bool TryUseVictoryExit(PlayerController actor)
    {
        if (!CanUseRoom(actor) || !StoryRunProgress.Current.DefeatedBoss || worldCard == null) return false;
        sequence = StartCoroutine(PlayWorldEnding());
        return true;
    }

    IEnumerator PlayWorldEnding()
    {
        CurrentPhase = Phase.ExitMusic;
        AcquireInput();
        // Warm C before starting the shared title/credits timeline.
        var clip = roomMusic.versionC;
        if (clip != null)
        {
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            while (clip.loadState == AudioDataLoadState.Loading) yield return null;
        }
        roomMusic.PlayFinale();
        player.GetComponent<PlayerPickup>()?.ClearForRespawn();
        StorySubtitles.Clear();
        if (endingCredits == null) endingCredits = Resources.Load<EndingCreditsAsset>("WorldEndingCredits");
        if (endingTitle == null) endingTitle = FindAnyObjectByType<SonicSpaceStartMenu>(FindObjectsInactive.Include);
        canvas.enabled = true;
        float initialCoverAlpha = black.color.a;
        yield return Animate(endingCredits != null ? endingCredits.whiteFadeSeconds : 3,
            t => black.color = new Color(1, 1, 1, Mathf.Lerp(initialCoverAlpha, 1, Mathf.SmoothStep(0, 1, t))));
        black.color = Color.white;
        bool titleReady = endingTitle != null && endingTitle.BeginEndingCredits(endingCredits);
        if (titleReady)
        {
            yield return Animate(endingTitle.EndingTitleSeconds, _ => endingTitle.RenderEndingCredits(0));
            yield return Animate(endingCredits.scrollSeconds, t => endingTitle.RenderEndingCredits(t));
            yield return Animate(endingCredits.lastLineHoldSeconds, _ => endingTitle.RenderEndingCredits(1));
        }
        else Debug.LogError("第四结局缺少字幕 Asset 或标题动画引用。", this);
        float fadeSeconds = endingCredits != null ? endingCredits.fadeToBlackSeconds : 4;
        roomMusic.FadeOut(fadeSeconds, fadeSeconds);
        yield return Animate(fadeSeconds, t => {
            black.color = Color.Lerp(Color.white, Color.black, Mathf.SmoothStep(0, 1, t));
            if (titleReady) endingTitle.RenderEndingCredits(1);
        });
        black.color = Color.black;
        // Finish at exact silence before the card, even when a frame straddles the fade boundary.
        roomMusic.Stop();
        if (titleReady) endingTitle.EndEndingCredits();
        yield return ShowCard(worldCard);
    }

    bool CanUseRoom(PlayerController actor) => isActiveAndEnabled && actor == player && player != null
        && InStartingRoom && CurrentPhase == Phase.Room && !SonicSpaceStartMenu.BlocksGameplay
        && !player.InputSuspended && !player.TransportLocked && player.GetComponent<PlayerVitality>()?.IsDead != true;

    public bool TryReturnAfterBossEscape(PlayerController actor)
    {
        if (!isActiveAndEnabled || actor == null || actor != player || sequence != null || world == null
            || world.Travelling || world.CurrentLevel == startingRoom || player.InputSuspended || player.TransportLocked
            || SonicSpaceStartMenu.BlocksGameplay) return false;
        CurrentPhase = Phase.Returning;
        AcquireInput();
        card.gameObject.SetActive(false);
        black.color = new Color(0, 0, 0, 0); canvas.enabled = true;
        sequence = StartCoroutine(ReturnAfterBossEscape());
        return true;
    }

    IEnumerator ReturnAfterBossEscape()
    {
        yield return null;
        StorySubtitles.Clear();
        roomMusic.FadeOut(escapeFadeOutSeconds, escapeFadeOutSeconds);
        yield return Animate(escapeFadeOutSeconds,
            t => black.color = new Color(0, 0, 0, Mathf.SmoothStep(0, 1, t)));
        yield return new WaitForEndOfFrame();
        player.GetComponent<PlayerPickup>()?.ClearForRespawn();
        if (world.ReturnToStart(player))
        {
            yield return world.ResetCurrentLevel();
            player.Teleport(spawn.position); player.FaceDirection(spawn.forward);
            player.GetComponent<PlayerFloorProgression>()?.ResetTraversalSample();
            player.GetComponent<FootstepEmitter>()?.ResetStride();
            Physics.SyncTransforms();
            roomMusic.PlayRoom(false);
        }
        else Debug.LogError("逃避过渡无法返回初始房间。", this);
        yield return Animate(escapeFadeInSeconds,
            t => black.color = new Color(0, 0, 0, 1 - Mathf.SmoothStep(0, 1, t)));
        yield return new WaitForEndOfFrame();
        canvas.enabled = false; ReleaseInput(); fallenFor = 0; CurrentPhase = Phase.Room; sequence = null;
    }

    public bool TryEnterFourthEndingForTest()
    {
        if (!isActiveAndEnabled || sequence != null || world == null || world.Travelling
            || player == null || spawn == null || startingRoom == null || player.TransportLocked
            || player.DeathSequenceLocked || player.OpeningSequenceLocked || SonicSpaceStartMenu.BlocksGameplay) return false;
        world.CurrentLevel?.GetComponentInChildren<LevelArenaBoss>()?.ResetEncounter();
        StoryRunProgress.Current.CompleteBoss();
        return TryReturnAfterBossVictory();
    }

    public bool TryReturnAfterBossVictory()
    {
        if (!isActiveAndEnabled || !StoryRunProgress.Current.BossReturnPending || sequence != null
            || world == null || world.Travelling || player == null) return false;
        // This component lives on the player camera, so the transfer survives disabling the Boss level.
        CurrentPhase = Phase.Returning;
        previousSuspended = player.InputSuspended; ownsInput = true;
        player.OpeningSequenceLocked = true; player.SetInputCaptured(false);
        card.gameObject.SetActive(false);
        black.color = Color.white; canvas.enabled = true;
        sequence = StartCoroutine(ReturnAfterBossVictory());
        return true;
    }

    void Start()
    {
        // Also handles entering the campaign after winning in the standalone Boss scene.
        if (StoryRunProgress.Current.BossReturnPending) TryReturnAfterBossVictory();
    }

    IEnumerator ReturnAfterBossVictory()
    {
        yield return null;
        StorySubtitles.Clear();
        player.GetComponent<PlayerPickup>()?.ClearForRespawn();
        if (!world.ReturnToStart(player))
        {
            Debug.LogError("Boss victory could not return to the starting room.", this);
            canvas.enabled = false; ReleaseInput(); CurrentPhase = Phase.Room; sequence = null;
            yield break;
        }
        // Restore the closed door and original room even if the player visited it earlier.
        yield return world.ResetCurrentLevel();
        player.Teleport(spawn.position); player.FaceDirection(spawn.forward);
        player.GetComponent<PlayerFloorProgression>()?.ResetTraversalSample();
        player.GetComponent<FootstepEmitter>()?.ResetStride();
        Physics.SyncTransforms();
        yield return null;
        roomMusic.PlayRoom(true);
        yield return Animate(bossReturnFadeSeconds, t => black.color = new Color(1, 1, 1, 1 - Mathf.SmoothStep(0, 1, t)));
        yield return new WaitForEndOfFrame();
        canvas.enabled = false; ReleaseInput(); fallenFor = 0; CurrentPhase = Phase.Room; sequence = null;
        StoryRunProgress.Current.CompleteBossReturn();
    }

    Canvas canvas;
    UnityEngine.UI.Image black;
    UnityEngine.UI.RawImage card;
    Texture2D cardFace;
    RectTransform cardBackPattern;
    CanvasGroup cardOpacity;
    Coroutine sequence;
    float fallenFor;
    bool ownsInput, previousSuspended, returnArmed;
    bool musicInRoom;

    void Awake()
    {
        if (player == null || world == null || startingRoom == null || spawn == null || foolCard == null)
        { Debug.LogError("Sound & Shell opening requires player, world, room, spawn and Fool card.", this); enabled = false; return; }
        roomMusic.Initialize(transform);
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
        cardFace = foolCard;
        cardOpacity = card.gameObject.AddComponent<CanvasGroup>();
        cardBackPattern = MakeCardBackPattern(card.transform);
        card.rectTransform.anchorMin = card.rectTransform.anchorMax = new Vector2(.5f, .5f);
        card.rectTransform.pivot = new Vector2(.5f, .5f);
        card.rectTransform.anchoredPosition = Vector2.zero;
        FitCard();
        card.gameObject.SetActive(false); canvas.enabled = false;
    }

    void LateUpdate() => FitCard();

    void FitCard()
    {
        if (card == null || cardFace == null) return;
        // The front face owns the size while the back is showing, including on window resize.
        float aspect = (float)cardFace.width / cardFace.height;
        float availableWidth = 720f * Mathf.Max(1, Screen.width) / Mathf.Max(1, Screen.height);
        float height = Mathf.Min(620, Mathf.Max(1, availableWidth - 48) / aspect);
        card.rectTransform.sizeDelta = new Vector2(height * aspect, height);
    }

    static T MakeGraphic<T>(string name, Transform parent) where T : UnityEngine.UI.Graphic
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var graphic = go.AddComponent<T>(); graphic.raycastTarget = false; return graphic;
    }

    static RectTransform MakeCardBackPattern(Transform parent)
    {
        var root = new GameObject("Card back frame", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        void Line(string name, Vector2 from, Vector2 to)
        {
            var line = MakeGraphic<UnityEngine.UI.Image>(name, root);
            line.color = new Color(.56f, .51f, .39f);
            line.rectTransform.anchorMin = from; line.rectTransform.anchorMax = to;
            line.rectTransform.offsetMin = line.rectTransform.offsetMax = Vector2.zero;
        }
        Line("Left", new Vector2(.07f, .05f), new Vector2(.074f, .95f));
        Line("Right", new Vector2(.926f, .05f), new Vector2(.93f, .95f));
        Line("Bottom", new Vector2(.07f, .05f), new Vector2(.93f, .053f));
        Line("Top", new Vector2(.07f, .947f), new Vector2(.93f, .95f));
        var seal = MakeGraphic<UnityEngine.UI.Image>("Seal", root);
        seal.color = new Color(.56f, .51f, .39f);
        seal.rectTransform.anchorMin = seal.rectTransform.anchorMax = new Vector2(.5f, .5f);
        seal.rectTransform.sizeDelta = new Vector2(18, 18);
        seal.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
        root.gameObject.SetActive(false);
        return root;
    }

    void SetCardFlip(float angle)
    {
        bool back = angle > 90;
        card.texture = back ? (cardBackTexture != null ? cardBackTexture : Texture2D.whiteTexture) : cardFace;
        card.uvRect = back ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1);
        float light = Mathf.Lerp(.75f, 1, Mathf.Abs(Mathf.Cos(angle * Mathf.Deg2Rad)));
        card.color = back && cardBackTexture == null ? new Color(.10f, .11f, .14f) * light : new Color(light, light, light, 1);
        // Keep opacity on the CanvasGroup; the default back's colour multiplication must not dim its alpha.
        var color = card.color; color.a = 1; card.color = color;
        cardBackPattern.gameObject.SetActive(back && cardBackTexture == null);
        card.rectTransform.localRotation = Quaternion.Euler(0, angle, 0);
    }

    void Update()
    {
        roomMusic.Tick(Time.deltaTime);
        if (musicInRoom != InStartingRoom)
        {
            musicInRoom = InStartingRoom;
            if (!musicInRoom) roomMusic.FadeOut();
            else if (CurrentPhase != Phase.Returning) roomMusic.PlayRoom(StoryRunProgress.Current.DefeatedBoss);
        }
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
        cardFace = texture;
        card.texture = texture;
        // New presentation fields may be uninitialized after an editor script reload during play.
        if (cardOpacity == null) cardOpacity = card.GetComponent<CanvasGroup>() ?? card.gameObject.AddComponent<CanvasGroup>();
        if (cardBackPattern == null) cardBackPattern = MakeCardBackPattern(card.transform);
        card.gameObject.name = texture.name;
        FitCard();
        AcquireInput();
        roomMusic.FadeOut();
        player.GetComponent<PlayerPickup>()?.ClearForRespawn();
        canvas.enabled = true;
        Color coverStart = black.color;
        yield return Animate(.3f, t => black.color = Color.Lerp(coverStart, Color.black, Mathf.SmoothStep(0,1,t)));
        CurrentPhase = Phase.CardFlip;
        card.rectTransform.localScale = Vector3.one;
        card.rectTransform.anchoredPosition = Vector2.zero;
        SetCardFlip(180);
        cardOpacity.alpha = 0;
        card.gameObject.SetActive(true);
        yield return Animate(cardFadeInSeconds, t => cardOpacity.alpha = t * t * t * (t * (t * 6 - 15) + 10));
        yield return Animate(cardBackHoldSeconds, _ => { });
        yield return Animate(cardFlipSeconds, t => {
            // Quintic easing gives a quiet start and finish, with no overshoot or scale rush.
            float eased = t * t * t * (t * (t * 6 - 15) + 10);
            SetCardFlip(Mathf.Lerp(180, 0, eased));
        });
        SetCardFlip(0);
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
        roomMusic.FadeOut(returnFadeSeconds, returnFadeSeconds);
        yield return Animate(.25f, t => card.color = new Color(1,1,1,1-t));
        card.gameObject.SetActive(false);
        yield return Animate(returnFadeSeconds, _ => black.color = Color.black);
        roomMusic.Stop();
        player.GetComponent<PlayerPickup>()?.ClearForRespawn();
        StorySubtitles.Clear();
        DeveloperMode.ResetForNewGame();
        // Reload the title and scene objects while keeping the current run's story progress.
        yield return new WaitForEndOfFrame();
        string path = world.gameObject.scene.path;
        if (string.IsNullOrEmpty(path)) path = "Assets/Scenes/SoundAndShell.unity";
#if UNITY_EDITOR
        var loading = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path,
            new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
        var loading = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(path, UnityEngine.SceneManagement.LoadSceneMode.Single);
#endif
        if (loading != null) yield return loading;
        else
        {
            Debug.LogError("结局后无法重新加载开屏场景。", this);
            card.gameObject.SetActive(true); card.color = Color.white;
            CurrentPhase = Phase.AwaitingReturn; sequence = null;
        }
    }

    void AcquireInput()
    {
        if (ownsInput) return;
        previousSuspended = player.InputSuspended; ownsInput = true;
        player.OpeningSequenceLocked = true; player.SetInputCaptured(false);
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
        roomMusic.Stop(); musicInRoom = false;
        endingTitle?.EndEndingCredits();
        if (canvas != null) canvas.enabled = false;
        fallenFor = 0; CurrentPhase = Phase.Room;
    }
    void OnDestroy() { if (canvas != null) Destroy(canvas.gameObject); }
}
