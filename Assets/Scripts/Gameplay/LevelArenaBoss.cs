using System;
using System.Collections;
using System.Collections.Generic;
using Sound2.LevelEditing;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class LevelArenaBoss : MonoBehaviour
{
    public enum EncounterState { Waiting, Intro, Reveal, Fighting, Ending, Won }
    public enum Skill { Summon, SlamAndLaser, TripleSlam, Sweep }

    [Serializable]
    public sealed class MusicStage
    {
        public string label;
        [Min(0), InspectorName("开始时间（音乐秒数）")] public float startMusicTime;
        [Min(0), InspectorName("阶段首次出招前游走（秒）")] public float firstSkillDelay = 2;
        [Min(0), InspectorName("技能之间游走（秒）")] public float skillInterval = 2;
        [InspectorName("本阶段技能（依次各执行一次）")] public Skill[] skills;
        [InspectorName("进入阶段事件")] public UnityEvent onEnter = new();
    }

    [Header("场景引用")]
    public LevelLayout level;
    public HypercubePointCloud cube;
    public RuntimePatrolNavigation navigation;
    public LevelEnemy summonPrefab;
    public Material effectMaterial;
    public string arenaRoomId = "f8-boss-arena";
    public LayerMask laserLayers = Physics.DefaultRaycastLayers;

    [Header("音乐与开场（秒）")]
    public AudioClip battleMusic;
    [Range(0, 1)] public float musicVolume = .85f;
    [Min(0)] public float revealMusicTime = 16;
    [Min(0)] public float introBlackoutTime = 5.33f;
    [Min(0)] public float revealWithoutMusic = 2;
    [Min(.1f)] public float revealWaveSpeed = 70;
    [Min(.05f)] public float revealWaveAfterglow = 3.5f;
    [Tooltip("0 表示根据大厅尺寸计算。"), Min(0)] public float revealWaveRange;
    [Min(0)] public float beforeCombatSeconds = 5;

    [Header("音乐阶段（BGM 单次播放，指定时间结束战斗）")]
    [InspectorName("按音乐时间划分战斗阶段"), Tooltip("开启时，覆盖下面的生存秒数、轮数和 Rotation Order。")]
    public bool useMusicStages = true;
    [UnityEngine.Serialization.FormerlySerializedAs("fallbackMusicDuration")]
    [InspectorName("战斗结束时间（音乐秒数）"), Min(.1f),
     Tooltip("默认 101.333333 秒，即 1:41.333。到点立即清除攻击并进入胜利演出，音乐继续播放 outro，白屏时淡出。没有音频时也按此时间模拟。")]
    public float combatEndMusicTime = 304f / 3;
    [InspectorName("技能收尾预留时间"), Min(0), Tooltip("新技能预计无法在阶段末尾前完成时，改为游走等待，避免越过下一阶段的音乐节拍。")]
    public float stageSafetySeconds = .2f;
    [Tooltip("开始时间填音乐的绝对秒数，按时间递增；最后一阶段结束时间使用上面的战斗结束时间，与音频文件总长度无关。")]
    public MusicStage[] musicStages =
    {
        new() { label = "第一阶段（不含三连砸）", startMusicTime = 16,
            skills = new[] { Skill.SlamAndLaser, Skill.Summon, Skill.Sweep } },
        new() { label = "第二阶段", startMusicTime = 112f / 3,
            skills = new[] { Skill.SlamAndLaser, Skill.Summon, Skill.TripleSlam, Skill.Sweep } },
        new() { label = "第三阶段", startMusicTime = 176f / 3,
            skills = new[] { Skill.SlamAndLaser, Skill.Summon, Skill.TripleSlam, Skill.Sweep } },
        new() { label = "第四阶段", startMusicTime = 80,
            skills = new[] { Skill.SlamAndLaser, Skill.Summon, Skill.TripleSlam, Skill.Sweep } }
    };

    [Header("战斗与移动")]
    [Min(.1f)] public float survivalSeconds = 120;
    [InspectorName("按完整攻击轮数判定胜利"), Tooltip("启用后，胜利由完整轮数决定。最后一轮结束后，再经过一个 Between Skills 间隔进入胜利演出。")]
    public bool useAttackRoundsForVictory;
    [Min(1), InspectorName("胜利所需轮数"), Tooltip("按 Rotation Order 从头到尾执行完一遍算一轮。")]
    public int roundsToSurvive = 3;
    [Min(1)] public float skillDamage = 50;
    [Min(.1f)] public float hoverHeight = 4.5f;
    [Min(0)] public float moveSpeed = 5;
    [Min(0)] public float wallMargin = .5f;
    [Min(0)] public float roomEntryInset = .7f;
    [Min(0)] public float betweenSkills = 2.25f;
    public Skill[] rotationOrder = { Skill.Summon, Skill.SlamAndLaser, Skill.TripleSlam, Skill.Sweep };

    [Header("召唤")]
    [Min(1)] public int summonCount = 3;
    [Min(.1f)] public float summonLifetime = 14;
    [Min(.1f)] public float summonDistance = 5;
    [Min(0)] public float summonWindup = .6f;
    [Min(0), InspectorName("召唤物移速倍率"), Tooltip("生成时乘以召唤物预制体的巡逻和调查速度。1 为原速。")]
    public float summonSpeedMultiplier = 1f;

    [Header("下砸与激光（秒、米）")]
    [Min(0)] public float diveTravelSeconds = .35f;
    [Min(.1f)] public float diveHeight = 6;
    [Min(0)] public float slamWindup = .65f;
    [Min(0)] public float slamSeconds = .22f;
    [Min(.1f)] public float slamCenterHeight = 2;
    [Min(.1f)] public float impactRadius = 2;
    [Min(0)] public float liftSeconds = .45f;
    [Min(0)] public float tripleSlamInterval = .25f;
    [Min(0)] public float laserDelay = 1;
    [Min(0)] public float laserLookback = 1;
    [Min(.1f)] public float laserRange = 55;
    [Min(.02f)] public float laserWidth = .2f;
    [Tooltip("持续发射期间，每帧追踪 laserLookback 秒前的玩家位置。"), Min(.01f)] public float laserVisibleSeconds = 1;
    [Tooltip("激光和下砸预警的点间距；越小越密集（米）。"), Min(.005f)] public float attackPointSpacing = .035f;
    [Tooltip("下砸圆形预警的点云带宽（米）。"), Min(.02f)] public float impactWarningWidth = .18f;
    public BossWaveSettings slamWave = new();

    [Header("横扫")]
    [Min(0)] public float sweepWindup = .7f;
    [Min(0)] public float sweepSeconds = .35f;
    [Min(0)] public float sweepTravel = 2;
    [Min(0), InspectorName("横扫表面点云密度倍率"), Tooltip("1 为原密度，调大可让横扫经过的表面显示更多点。每次发出横扫时生效。")]
    public float sweepPointDensityMultiplier = 1f;
    public BossWaveSettings sweepWave = new();

    [Header("技能音效（可选，音源跟随 Boss）")]
    [InspectorName("下砸完整音效（含蓄力）")]
    public AudioClip slamSound;
    [InspectorName("下砸音效内的落地时间（秒）"), Min(0),
     Tooltip("例如 0.5：音效播放到 0.5 秒时落地。自动倒推播放时机，包含下落耗时；单次下砸和三连砸共用。")]
    public float slamSoundImpactTime = .5f;
    [InspectorName("下砸完整音效音量"), Range(0, 1)] public float slamSoundVolume = 1;
    [InspectorName("召唤蓄力音效")] public AudioClip summonChargeSound;
    [InspectorName("召唤生成音效")] public AudioClip summonSound;
    [InspectorName("横扫蓄力音效")] public AudioClip sweepChargeSound;
    [InspectorName("横扫声波释放音效")] public AudioClip sweepSound;
    [InspectorName("激光发射音效")] public AudioClip laserSound;
    [InspectorName("亮相声波音效")] public AudioClip revealSound;
    [InspectorName("胜利声波音效")] public AudioClip victorySound;
    [InspectorName("技能音效总音量"), Range(0, 1)] public float skillSoundVolume = 1;
    public SpatialAudioSettings skillSoundSettings = new()
    {
        distanceMode = SoundDistanceMode.Linear, minDistance = 3, maxDistance = 55
    };

    [Header("通用备用音效（可选）")]
    [Tooltip("没有指定技能的独立蓄力音效时使用；下砸已有完整音效时不叠加。")]
    public AudioClip chargeSound;
    [Tooltip("下砸落地时的额外音效；不需要叠加时留空。横扫未指定独立音效时也使用此项。")]
    public AudioClip impactSound;

    [Header("胜利演出")]
    [InspectorName("胜利视角转向时间（秒）"), Min(0),
     Tooltip("玩家位置保持不变，自动转头抬头跟随升空的超立方体；转向和升空完成后展开。0 表示立即转向。")]
    public float victoryLookSeconds = 1.25f;
    [Min(.1f)] public float victoryWaveSpeed = 70;
    [Min(.05f)] public float victoryWaveAfterglow = 9;
    [Min(0)] public float victoryWaveLead = .65f;
    [Min(.1f)] public float victoryHeight = 6;
    [Min(0)] public float victoryRiseSeconds = 1.8f;
    [Min(0)] public float unfoldSeconds = 2.8f;
    [Min(.1f)] public float unfoldedCellSize = 1.9f;
    [Min(0)] public float unfoldedHoldSeconds = .7f;
    [InspectorName("展开后收缩时间"), Min(0)] public float victoryShrinkSeconds = 4;
    [InspectorName("收缩后的比例"), Range(.001f, .2f)] public float victoryShrinkScale = .015f;
    [Header("胜利戒指（拾取后才回房间）")]
    public GameObject victoryRingPrefab;
    [Min(.1f)] public float victoryRingScale = 3;
    [Min(.05f)] public float ringDropSeconds = 1;
    [InspectorName("戒指首次落地反弹高度"), Min(0)] public float ringBounceHeight = .18f;
    [InspectorName("戒指反弹重力"), Min(.1f)] public float ringBounceGravity = 9.81f;
    [InspectorName("戒指下落侧倾角度")] public float ringFallSideAngle = 75;
    [InspectorName("戒指下落旋转速度（度/秒）")] public float ringFallSpin = 110;
    public AudioClip ringLandingSound;
    [Range(0, 1)] public float ringLandingVolume = 1;
    [Min(.1f)] public float ringWaveSpeed = 180;
    [Tooltip("0 自动计算覆盖全场的距离。"), Min(0)] public float ringWaveRange;
    [Min(.05f)] public float ringWaveAfterglow = 6;
    [Tooltip("全场声波余辉消失后，再等待多久解锁玩家。"), Min(0)] public float ringUnlockDelay = .5f;
    [Min(.1f)] public float ringBlinkSeconds = 1.4f;
    [Range(.05f, 1)] public float ringBlinkMinimum = .25f;
    [Min(0)] public float fadeWhiteSeconds = 3;
    public UnityEvent onVictory = new();

    public EncounterState State { get; private set; }
    public float RemainingSeconds => State != EncounterState.Fighting ? 0 : useMusicStages
        ? Mathf.Max(0, (float)(EncounterDuration - EncounterTime))
        : fightEndsAt.HasValue ? Mathf.Max(0, fightEndsAt.Value - Time.time) : 0;
    public int CurrentMusicStage { get; private set; }
    public Skill? CurrentSkill { get; private set; }
    internal Canvas OverlayCanvas { get; private set; }
    internal float Radius { get; private set; }
    internal float FloorY => ToWorld(Vector2.zero, 0).y;

    readonly List<BossGroundWave> waves = new();
    readonly List<(GameObject enemy, float expires)> summons = new();
    readonly List<(float time, Vector3 position)> aimHistory = new();
    readonly List<LineRenderer> attackLines = new();
    LevelWorld world;
    LevelRoom room;
    GameObject entranceSeal;
    BossMemoryGate memoryGate;
    PlayerController player;
    PlayerVitality vitality;
    CharacterController capsule;
    SonarPulseField sonar;
    BossMusic music;
    SpatialAudioEmitter effectsAudio;
    readonly SpatialAudioEmitter[] slamAudio = new SpatialAudioEmitter[3];
    int nextSlamVoice;
    Coroutine sequence;
    Image screen;
    bool inputLocked, originalInputLock, resetRequested;
    float? fightEndsAt;
    bool encounterHasMusic, encounterClockStarted;
    double fallbackClockStart;
    bool victoryLookActive;
    Vector2 victoryLookFrom;
    float victoryLookStartedAt;
    Transform victoryLookTarget;
    bool victoryLookHorizontal;
    Vector3 cubeRestScale;
    BossVictoryRing victoryRing;
    bool awaitingVictoryRing, victoryRingCollected;
    double EncounterTime => encounterHasMusic ? music.Elapsed : Time.timeAsDouble - fallbackClockStart;
    double EncounterDuration => Mathf.Max(.1f, combatEndMusicTime);
    Vector2 previousTarget;
    float previousFeetY;

    void Awake()
    {
        if (EnsureRuntime()) return;
        Debug.LogError("Boss arena requires a circular room and a hypercube.", this);
        enabled = false;
    }

    bool EnsureRuntime()
    {
        if (music != null && room != null) return true;
        if (level == null) level = GetComponentInParent<LevelLayout>();
        room = level != null ? level.FindRoom(arenaRoomId) : null;
        if (room == null || cube == null) return false;

        Radius = room.size.x * .5f;
        cubeRestScale = cube.transform.localScale;
        memoryGate = level.GetComponentInChildren<BossMemoryGate>(true);
        music = new BossMusic(gameObject);
        effectsAudio = transform.Find("Boss spatial effects")?.GetComponent<SpatialAudioEmitter>();
        if (effectsAudio == null)
        {
            var effects = new GameObject("Boss spatial effects");
            effects.transform.SetParent(transform, false);
            effectsAudio = effects.AddComponent<SpatialAudioEmitter>();
        }
        if (slamSound != null && slamSound.loadState == AudioDataLoadState.Unloaded) slamSound.LoadAudioData();
        ResetEncounter();
        return true;
    }

    void Update()
    {
        if (!EnsureRuntime()) return;
        if (resetRequested) ResetEncounter();
        if (State == EncounterState.Won || State == EncounterState.Ending) return;

        if (player == null) FindPlayer();
        if (player == null || capsule == null || vitality == null || vitality.IsDead || !capsule.enabled) return;
        if (useMusicStages && encounterClockStarted && State is EncounterState.Reveal or EncounterState.Fighting)
        {
            if (EncounterTime >= EncounterDuration)
            {
                BeginVictory();
                return;
            }
            UpdateMusicStage();
        }
        var target = capsule.bounds;
        Vector2 local = ToArena(target.center);

        if (State == EncounterState.Waiting)
        {
            bool canStart = !SonicSpaceStartMenu.BlocksGameplay && !player.InputSuspended && !player.TransportLocked
                && (world == null || (world.CurrentLevel == level && !world.Travelling));
            if (DeveloperMode.Available && canStart && Keyboard.current?.f8Key.wasPressedThisFrame == true)
            {
                StorySubtitles.Clear();
                sequence = StartCoroutine(PlayEncounter());
                return;
            }
            float entryRadius = Mathf.Max(.1f, Radius - roomEntryInset);
            if (memoryGate == null && canStart
                && local.sqrMagnitude < entryRadius * entryRadius
                && target.min.y >= FloorY - .3f && target.min.y < FloorY + 2)
                sequence = StartCoroutine(PlayEncounter());
        }
        else if (State == EncounterState.Fighting)
        {
            if (Time.time >= fightEndsAt)
            {
                BeginVictory();
                return;
            }
            RecordAim(target.center);
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                if (!waves[i].Advance(Time.deltaTime, target, !vitality.IsDead, previousTarget, previousFeetY))
                    waves.RemoveAt(i);
            }
            for (int i = summons.Count - 1; i >= 0; i--)
            {
                if (summons[i].enemy != null && Time.time < summons[i].expires) continue;
                RemoveSummon(summons[i].enemy);
                summons.RemoveAt(i);
            }
        }
        previousTarget = local;
        previousFeetY = target.min.y;
    }

    void LateUpdate()
    {
        if (!victoryLookActive || State != EncounterState.Ending || player == null || cube == null) return;
        float t = victoryLookSeconds > 0 ? Mathf.Clamp01((Time.time - victoryLookStartedAt) / victoryLookSeconds) : 1;
        // Smooth acceleration/deceleration, using the shortest yaw arc. Continue following
        // the moving cube after the turn so it stays framed throughout rise and unfold.
        float smooth = t * t * t * (t * (t * 6 - 15) + 10);
        Vector3 lookAt = victoryLookTarget != null ? victoryLookTarget.position : cube.transform.position;
        if (victoryLookHorizontal) lookAt.y = player.ViewTransform.position.y;
        player.FacePoint(lookAt, victoryLookFrom, smooth);
        if (Application.isFocused && !SonicSpaceStartMenu.BlocksGameplay)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void FindPlayer()
    {
        world = level.GetComponentInParent<LevelWorld>();
        if (world == null)
            foreach (var candidate in FindObjectsByType<LevelWorld>())
                if (candidate.levels.Contains(level)) { world = candidate; break; }
        player = world != null ? world.player : FindAnyObjectByType<PlayerController>();
        if (player == null || player.gameObject.scene != gameObject.scene) { player = null; return; }
        if (world == null) StoryRunProgress.Current.EnterBossRoom();
        capsule = player.GetComponent<CharacterController>();
        vitality = player.GetComponent<PlayerVitality>();
        sonar = player.GetComponentInChildren<SonarPulseField>(true);
        if (vitality != null) vitality.Died += RequestReset;
    }

    internal bool TryStartFromGate(PlayerController actor)
    {
        if (!EnsureRuntime()) return false;
        if (player == null) FindPlayer();
        if (memoryGate == null || !memoryGate.Ready || actor != player || player == null
            || player.InputSuspended || player.TransportLocked || State != EncounterState.Waiting) return false;
        sequence = StartCoroutine(PlayEncounter());
        return true;
    }

    bool CanUseEncounterCommand(PlayerController actor)
    {
        if (!isActiveAndEnabled || !EnsureRuntime()) return false;
        if (player == null) FindPlayer();
        return actor != null && actor == player && vitality != null && !vitality.IsDead && capsule != null && capsule.enabled
            && !actor.TransportLocked && !actor.DeathSequenceLocked && !actor.OpeningSequenceLocked
            && !SonicSpaceStartMenu.BlocksGameplay && (world == null || !world.Travelling && world.CurrentLevel == level);
    }

    internal bool TryStartFromConsole(PlayerController actor)
    {
        if (!CanUseEncounterCommand(actor) || State is not (EncounterState.Waiting or EncounterState.Intro)
            || actor.EncounterInputLocked && !inputLocked) return false;
        if (State == EncounterState.Intro)
        {
            StopSequence();
            sonar?.ClearPulses();
        }
        StorySubtitles.Clear();
        sequence = StartCoroutine(PlayEncounter(startAtReveal: true));
        return true;
    }

    internal bool TryStartStageFromConsole(PlayerController actor, int stageNumber)
    {
        int index = stageNumber - 1;
        if (!CanUseEncounterCommand(actor) || State is EncounterState.Ending or EncounterState.Won
            || actor.EncounterInputLocked && !inputLocked || !useMusicStages || musicStages == null
            || index < 0 || index >= musicStages.Length || musicStages[index] == null
            || musicStages[index].startMusicTime >= EncounterDuration) return false;

        // Start a clean stage preview, preserving collected memories and the player's health.
        ResetEncounter();
        StorySubtitles.Clear();
        sequence = StartCoroutine(PlayEncounter(startAtReveal: true, startAtStage: index));
        return true;
    }

    internal bool TrySkipToVictory(PlayerController actor)
    {
        if (!CanUseEncounterCommand(actor) || State is EncounterState.Ending or EncounterState.Won) return false;
        resetRequested = false;
        StorySubtitles.Clear();
        StoryRunProgress.Current.StartBoss();
        HideScreen();
        cube.enabled = true;
        BeginVictory();
        return true;
    }

    void BeginVictory()
    {
        var viewSonar = player.GetComponentInChildren<SphericalSonar>(true);
        if (viewSonar != null) viewSonar.SetRevealModes(viewSonar.proximityRendering, true);
        StopSequence();
        ClearAttacks();
        sonar?.ClearPulses();
        effectsAudio.Source.Stop();
        sequence = StartCoroutine(PlayVictory());
    }

    IEnumerator PlayEncounter(bool startAtReveal = false, int startAtStage = -1)
    {
        var viewSonar = player.GetComponentInChildren<SphericalSonar>(true);
        if (viewSonar != null) viewSonar.SetRevealModes(viewSonar.proximityRendering, true);
        State = EncounterState.Intro;
        encounterClockStarted = false;
        CurrentMusicStage = 0;
        StoryRunProgress.Current.StartBoss();
        CloseArenaEntrance();
        if (startAtReveal) player.FacePoint(cube.transform.position);
        if (battleMusic != null)
        {
            if (battleMusic.loadState == AudioDataLoadState.Unloaded) battleMusic.LoadAudioData();
            while (battleMusic.loadState == AudioDataLoadState.Loading) yield return null;
        }
        float cutoff = battleMusic != null && battleMusic.loadState == AudioDataLoadState.Loaded
            ? battleMusic.length : (float)EncounterDuration;
        float reveal = Mathf.Min(revealMusicTime, cutoff);
        float startOffset = startAtStage >= 0 ? musicStages[startAtStage].startMusicTime : startAtReveal ? reveal : 0;
        bool playingMusic = music.Play(battleMusic, musicVolume, startOffset);
        encounterHasMusic = playingMusic;
        fallbackClockStart = Time.timeAsDouble - startOffset;
        encounterClockStarted = true;
        if (!startAtReveal)
        {
            State = EncounterState.Intro;
            yield return PlayIntro(playingMusic, reveal);
        }
        else
        {
            // Wait for the scheduled audio to start before firing the target stage's events.
            while (EncounterTime < startOffset) yield return null;
        }

        State = EncounterState.Reveal;
        if (memoryGate != null) memoryGate.gameObject.SetActive(false);
        HideScreen();
        cube.enabled = true;
        if (startAtStage < 0) EmitReveal(revealWaveSpeed, revealWaveAfterglow);
        UnlockInput();
        if (useMusicStages)
        {
            UpdateMusicStage();
            // This hold belongs to stage one, rather than shifting every later stage.
            double combatAt = startAtStage >= 0 ? startOffset : reveal + Mathf.Max(0, beforeCombatSeconds);
            while (EncounterTime < combatAt) yield return null;
            yield return FightMusicStages();
        }
        else
        {
            yield return new WaitForSeconds(beforeCombatSeconds);
            yield return Fight();
        }
    }

    void CloseArenaEntrance()
    {
        // Debug starts can come from the elevator or from inside the doorway.
        Vector2 local = ToArena(player.transform.position);
        float clearance = Mathf.Max(roomEntryInset, capsule.radius + capsule.skinWidth);
        if (local.magnitude > Radius - clearance)
        {
            float pivotAboveFeet = player.transform.position.y - capsule.bounds.min.y;
            player.Teleport(ToWorld(local.normalized * Radius * .7f, 0) + Vector3.up * (pivotAboveFeet + .05f));
        }
        if (entranceSeal == null) entranceSeal = BossArenaEntrance.Build(level, room);
        entranceSeal.SetActive(true);
        Physics.SyncTransforms();
    }

    IEnumerator PlayIntro(bool playingMusic, float reveal)
    {
        float fade = memoryGate == null ? 0 : (playingMusic ? Mathf.Min(introBlackoutTime, reveal) : introBlackoutTime);
        float start = Time.time;
        double Elapsed() => playingMusic ? music.Elapsed : Time.time - start;
        if (fade > 0)
        {
            float range = Mathf.Max(2, Radius * 2);
            sonar?.EmitFrom(memoryGate.startButton.InteractionPosition, range, revealWaveSpeed, fade);
            while (Elapsed() < fade)
            {
                ShowScreen(new Color(0, 0, 0, Mathf.SmoothStep(0, 1, Mathf.Clamp01((float)Elapsed() / fade))));
                yield return null;
            }
        }
        ShowScreen(Color.black);
        LockInput();
        player.FacePoint(cube.transform.position);
        float revealAt = useMusicStages || playingMusic ? reveal : fade + revealWithoutMusic;
        while (Elapsed() < revealAt) yield return null;
    }

    void UpdateMusicStage()
    {
        int active = 0;
        for (int i = 0; i < (musicStages?.Length ?? 0); i++)
            if (musicStages[i] != null && EncounterTime >= musicStages[i].startMusicTime) active = i + 1;
        if (active == CurrentMusicStage) return;
        CurrentMusicStage = active;
        if (active > 0) musicStages[active - 1].onEnter?.Invoke();
    }

    IEnumerator FightMusicStages()
    {
        State = EncounterState.Fighting;
        fightEndsAt = null;
        for (int i = 0; i < (musicStages?.Length ?? 0); i++)
        {
            var stage = musicStages[i];
            if (stage == null) continue;
            double end = EncounterDuration;
            for (int next = i + 1; next < musicStages.Length; next++)
                if (musicStages[next] != null)
                {
                    end = Math.Min(end, musicStages[next].startMusicTime);
                    break;
                }
            if (EncounterTime >= end) continue;
            yield return RoamUntilMusicTime(Math.Min(stage.startMusicTime, end));
            for (int skill = 0; skill < (stage.skills?.Length ?? 0); skill++)
            {
                double delay = Mathf.Max(0, skill == 0 ? stage.firstSkillDelay : stage.skillInterval);
                yield return RoamUntilMusicTime(Math.Min(EncounterTime + delay, end));
                Skill attack = stage.skills[skill];
                // Keep the next phase on the music clock, even after timing edits or a slow frame.
                // Reserve a few frames per skill's coroutine transitions as well as the user margin.
                double reserve = Mathf.Max(0, stageSafetySeconds) + EstimatedSkillFrames(attack) * Time.unscaledDeltaTime;
                if (EncounterTime + EstimatedSkillSeconds(attack) + reserve > end)
                {
                    Debug.LogWarning($"Boss music stage '{stage.label}': not enough time for {attack}; waiting for the next music stage. Shorten timings or adjust the stage skills.", this);
                    break;
                }
                CurrentSkill = attack;
                yield return PerformSkill(attack);
                CurrentSkill = null;
            }
            yield return RoamUntilMusicTime(end);
        }
        yield return RoamUntilMusicTime(EncounterDuration);
    }

    float EstimatedSkillSeconds(Skill skill)
    {
        float slam = Mathf.Max(0, diveTravelSeconds) + (slamSound != null
            ? Mathf.Max(Mathf.Max(0, slamWindup) + Mathf.Max(0, slamSeconds), Mathf.Max(0, slamSoundImpactTime) + .1f)
            : Mathf.Max(0, slamWindup) + Mathf.Max(0, slamSeconds));
        return skill switch
        {
            Skill.Summon => Mathf.Max(0, summonWindup),
            Skill.SlamAndLaser => slam + Mathf.Max(0, laserDelay) + Mathf.Max(.01f, laserVisibleSeconds) + Mathf.Max(0, liftSeconds),
            Skill.TripleSlam => 3 * (slam + Mathf.Max(0, liftSeconds)) + 2 * Mathf.Max(0, tripleSlamInterval),
            Skill.Sweep => Mathf.Max(0, sweepWindup) + Mathf.Max(0, sweepSeconds),
            _ => 0
        };
    }

    static int EstimatedSkillFrames(Skill skill) => skill switch
    {
        Skill.TripleSlam => 14,
        Skill.SlamAndLaser => 7,
        _ => 3
    };

    IEnumerator RoamUntilMusicTime(double end)
    {
        Vector2 destination = ClampBoss(UnityEngine.Random.insideUnitCircle * Radius);
        while (EncounterTime < end)
        {
            Vector2 point = Vector2.MoveTowards(ToArena(transform.position), destination, moveSpeed * Time.deltaTime);
            transform.position = ToWorld(point, SafeHeight(hoverHeight));
            if ((point - destination).sqrMagnitude < .04f)
                destination = ClampBoss(UnityEngine.Random.insideUnitCircle * Radius);
            yield return null;
        }
    }

    IEnumerator PerformSkill(Skill skill)
    {
        switch (skill)
        {
            case Skill.Summon: yield return Summon(); break;
            case Skill.SlamAndLaser:
                yield return Slam();
                yield return new WaitForSeconds(laserDelay);
                yield return FireLaser();
                yield return Fly(ToArena(transform.position), hoverHeight, liftSeconds);
                break;
            case Skill.TripleSlam:
                for (int i = 0; i < 3; i++)
                {
                    yield return Slam();
                    yield return Fly(ToArena(transform.position), hoverHeight, liftSeconds);
                    if (i < 2) yield return new WaitForSeconds(tripleSlamInterval);
                }
                break;
            case Skill.Sweep: yield return Sweep(); break;
        }
    }

    IEnumerator Fight()
    {
        State = EncounterState.Fighting;
        int targetRounds = useAttackRoundsForVictory ? Mathf.Max(1, roundsToSurvive) : 0;
        fightEndsAt = targetRounds > 0 ? null : Time.time + Mathf.Max(.1f, survivalSeconds);
        int completedRounds = 0;
        int next = 0;
        while (true)
        {
            yield return Roam(betweenSkills);
            var order = rotationOrder;
            if (order == null || order.Length == 0) { next = 0; yield return null; continue; }
            next %= order.Length;
            CurrentSkill = order[next++];
            yield return PerformSkill(CurrentSkill.Value);
            CurrentSkill = null;
            if (next < order.Length) continue;
            next = 0;
            if (targetRounds == 0 || ++completedRounds < targetRounds) continue;

            // Keep the encounter active during the final interval so remaining hazards can still hit.
            fightEndsAt = Time.time + betweenSkills;
            yield return Roam(betweenSkills);
            yield break;
        }
    }

    IEnumerator Roam(float seconds)
    {
        float end = Time.time + seconds;
        Vector2 destination = ClampBoss(UnityEngine.Random.insideUnitCircle * Radius);
        while (Time.time < end)
        {
            Vector2 point = Vector2.MoveTowards(ToArena(transform.position), destination, moveSpeed * Time.deltaTime);
            transform.position = ToWorld(point, SafeHeight(hoverHeight));
            if ((point - destination).sqrMagnitude < .04f)
                destination = ClampBoss(UnityEngine.Random.insideUnitCircle * Radius);
            yield return null;
        }
    }

    IEnumerator Summon()
    {
        PlayEffect(summonChargeSound != null ? summonChargeSound : chargeSound);
        yield return new WaitForSeconds(summonWindup);
        if (summonPrefab == null || navigation == null || !navigation.Ready) yield break;
        PlayEffect(summonSound);
        Vector2 center = ToArena(transform.position);
        for (int i = 0; i < summonCount; i++)
        {
            float angle = i * (2 * Mathf.PI / summonCount);
            Vector2 point = Vector2.ClampMagnitude(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * summonDistance, Radius - 1);
            var enemy = Instantiate(summonPrefab, ToWorld(point, 0), level.transform.rotation, level.transform);
            enemy.actor.navigation = navigation;
            enemy.actor.patrolSpeed *= summonSpeedMultiplier;
            enemy.actor.investigateSpeed *= summonSpeedMultiplier;
            enemy.actor.exactContactDamage = skillDamage;
            enemy.actor.InvestigateOnSpawn(capsule.bounds.center);
            summons.Add((enemy.gameObject, Time.time + summonLifetime));
        }
    }

    IEnumerator Slam()
    {
        Vector2 target = ClampBoss(ToArena(capsule.bounds.center));
        yield return Fly(target, diveHeight, diveTravelSeconds);
        // Load before establishing the clock: a background load must not shift the impact cue.
        AudioClip recording = slamSound;
        if (recording != null)
        {
            if (recording.loadState == AudioDataLoadState.Unloaded) recording.LoadAudioData();
            while (recording.loadState == AudioDataLoadState.Loading) yield return null;
            if (recording.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogWarning("Boss slam sound could not load; using the fallback charge sound.", this);
                recording = null;
            }
        }
        var mark = DrawMarker(target, impactRadius);
        if (recording != null)
        {
            float fallSeconds = Mathf.Max(0, slamSeconds);
            double cueTime = Mathf.Max(0, slamSoundImpactTime);
            // Keep the existing windup/fall duration, extending the windup only if the
            // recording needs more lead time. Schedule with a safe audio preparation margin.
            var voice = GetSlamVoice();
            voice.settings = skillSoundSettings;
            voice.volume = skillSoundVolume * slamSoundVolume;
            voice.ApplyBeforePlayback();
            var source = voice.Source;
            source.Stop();
            source.clip = recording;
            source.loop = false;
            source.pitch = 1;
            source.timeSamples = 0;
            double impactAt = AudioSettings.dspTime + Math.Max(Mathf.Max(0, slamWindup) + fallSeconds, cueTime + .1);
            source.PlayScheduled(impactAt - cueTime);

            Vector3 from = transform.position;
            Vector3 landing = ToWorld(target, SafeHeight(slamCenterHeight));
            double fallAt = impactAt - fallSeconds;
            // Animation and the impact event share the scheduled audio's DSP clock.
            // Rendering/hit detection reaches this timestamp on the next rendered frame.
            while (AudioSettings.dspTime < impactAt)
            {
                float t = fallSeconds > 0 ? Mathf.Clamp01((float)((AudioSettings.dspTime - fallAt) / fallSeconds)) : 0;
                transform.position = Vector3.Lerp(from, landing, t * t);
                yield return null;
            }
            transform.position = landing;
        }
        else
        {
            PlayEffect(chargeSound);
            yield return new WaitForSeconds(slamWindup);
            yield return Fly(target, slamCenterHeight, slamSeconds, accelerating: true);
        }
        RemoveLine(mark);
        PlayEffect(impactSound);
        var bounds = capsule.bounds;
        if (InArena(ToArena(bounds.center)) && bounds.min.y <= FloorY + SafeHeight(slamCenterHeight) + BossExtent
            && (ToArena(bounds.center) - target).magnitude <= impactRadius + bounds.extents.x)
            DamagePlayer();
        AddWave(slamWave, target, Vector2.zero);
    }

    IEnumerator FireLaser()
    {
        var line = CreateLine("Boss laser", effectMaterial, new Color(1, .03f, .015f), laserWidth);
        attackLines.Add(line);
        PlayEffect(laserSound);
        bool damaged = false;
        float endsAt = Time.time + Mathf.Max(.01f, laserVisibleSeconds);
        var points = new MaterialPropertyBlock();
        while (Time.time < endsAt && State == EncounterState.Fighting && vitality != null && !vitality.IsDead)
        {
            Vector3 origin = transform.position;
            Vector3 direction = (AimAt(Time.time - laserLookback) - origin).normalized;
            Vector3 end = origin + direction * laserRange;
            if (direction.sqrMagnitude > .5f && Physics.Raycast(origin, direction, out var hit,
                laserRange, laserLayers, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                // A sustained beam is one attack, including when the player re-enters it.
                if (!damaged && hit.collider.GetComponentInParent<PlayerVitality>() == vitality)
                {
                    damaged = true;
                    DamagePlayer();
                }
            }
            line.SetPosition(0, origin);
            line.SetPosition(1, end);
            SetAttackPoints(line, points, Vector3.Distance(origin, end), laserWidth);
            yield return null;
        }
        RemoveLine(line);
    }

    IEnumerator Sweep()
    {
        PlayEffect(sweepChargeSound != null ? sweepChargeSound : chargeSound);
        yield return new WaitForSeconds(sweepWindup);
        Vector2 from = ToArena(transform.position);
        Vector2 direction = (ToArena(capsule.bounds.center) - from).normalized;
        if (direction.sqrMagnitude < .5f) direction = Vector2.up;
        Vector2 destination = ClampBoss(from + direction * sweepTravel);
        yield return Fly(destination, hoverHeight, sweepSeconds);
        PlayEffect(sweepSound != null ? sweepSound : impactSound);
        AddWave(sweepWave, destination, direction, sweepPointDensityMultiplier);
    }

    void AddWave(BossWaveSettings settings, Vector2 point, Vector2 direction, float pointDensityMultiplier = 1f)
    {
        Vector3 origin = ToWorld(point, 0);
        Vector3 forward = (ToWorld(point + direction, 0) - origin).normalized;
        waves.Add(new BossGroundWave(this, settings, point, direction));
        sonar?.EmitBossGroundWave(origin, ToWorld(Vector2.zero, 0), Radius, forward, settings, pointDensityMultiplier);
    }

    IEnumerator Fly(Vector2 point, float y, float seconds, bool accelerating = false)
    {
        Vector3 from = transform.position;
        Vector3 to = ToWorld(point, SafeHeight(y));
        for (float time = 0; time < seconds; time += Time.deltaTime)
        {
            float t = time / seconds;
            transform.position = Vector3.Lerp(from, to, accelerating ? t * t : Mathf.SmoothStep(0, 1, t));
            yield return null;
        }
        transform.position = to;
    }

    float BossExtent => cube.radius * Mathf.Max(Mathf.Abs(cube.transform.lossyScale.x),
        Mathf.Abs(cube.transform.lossyScale.y), Mathf.Abs(cube.transform.lossyScale.z));

    float SafeHeight(float height) => Mathf.Clamp(height, BossExtent, Mathf.Max(BossExtent, room.Height(level) - BossExtent));
    Vector2 ClampBoss(Vector2 point) => Vector2.ClampMagnitude(point, Mathf.Max(0, Radius - BossExtent - wallMargin));
    internal Vector2 ToArena(Vector3 position) => level.Local(position) - room.center;
    internal Vector3 ToWorld(Vector2 point, float height) => level.World(room.center + point, height);
    internal bool InArena(Vector2 point) => point.sqrMagnitude <= Radius * Radius;
    internal void DamagePlayer()
    {
        if (State == EncounterState.Fighting && vitality != null && !vitality.IsDead)
            vitality.TakeExactDamage(skillDamage);
    }

    void RecordAim(Vector3 position)
    {
        float now = Time.time;
        if (aimHistory.Count > 0 && now <= aimHistory[^1].time) return;
        aimHistory.Add((now, position));
        float before = now - Mathf.Max(laserLookback, .1f) - 1;
        if (aimHistory.Count > 2 && aimHistory[1].time < before)
        {
            int remove = 1;
            while (remove + 1 < aimHistory.Count && aimHistory[remove + 1].time < before) remove++;
            aimHistory.RemoveRange(0, remove);
        }
    }

    Vector3 AimAt(float time)
    {
        for (int i = aimHistory.Count - 1; i > 0; i--)
        {
            if (time < aimHistory[i - 1].time) continue;
            var a = aimHistory[i - 1];
            var b = aimHistory[i];
            return Vector3.Lerp(a.position, b.position, Mathf.InverseLerp(a.time, b.time, time));
        }
        return aimHistory.Count != 0 ? aimHistory[0].position : capsule.bounds.center;
    }

    IEnumerator PlayVictory()
    {
        State = EncounterState.Ending;
        if (memoryGate != null) memoryGate.gameObject.SetActive(false);
        LockInput();
        victoryLookFrom = player.AimAngles;
        victoryLookStartedAt = Time.time;
        victoryLookActive = true;
        victoryLookTarget = cube.transform;
        victoryLookHorizontal = false;
        EmitReveal(victoryWaveSpeed, victoryWaveAfterglow, victorySound);
        yield return new WaitForSeconds(victoryWaveLead);
        Vector2 viewer = ToArena(player.ViewTransform.position);
        Vector2 destination = viewer.sqrMagnitude < 42
            ? ClampBoss(viewer + (viewer.sqrMagnitude > .1f ? -viewer.normalized : Vector2.up) * 7)
            : Vector2.zero;
        yield return Fly(destination, victoryHeight, victoryRiseSeconds);
        while (Time.time < victoryLookStartedAt + Mathf.Max(0, victoryLookSeconds)) yield return null;

        // Turn the existing eight-cell net so its long axis points down, facing the viewer.
        // The player remains in place while their view tracks the airborne Boss.
        Vector3 facing = Vector3.ProjectOnPlane(player.ViewTransform.position - cube.transform.position, Vector3.up);
        if (facing.sqrMagnitude < .001f) facing = -player.transform.forward;
        var netRotation = Quaternion.LookRotation(facing.normalized, Vector3.up) * Quaternion.Euler(0, 0, -90);
        cube.SetNetRotation(Quaternion.Inverse(cube.transform.rotation) * netRotation);
        float ceiling = ToWorld(Vector2.zero, room.Height(level)).y;
        float clearance = Mathf.Min(cube.transform.position.y - FloorY, ceiling - cube.transform.position.y) - .15f;
        float scale = Mathf.Max(.001f, cube.transform.lossyScale.magnitude / Mathf.Sqrt(3));
        float displayCellSize = Mathf.Min(unfoldedCellSize, Mathf.Max(.1f, clearance / (2 * scale)));
        for (float elapsed = 0; elapsed < unfoldSeconds; elapsed += Time.deltaTime)
        {
            cube.SetUnfold(Mathf.SmoothStep(0, 1, elapsed / unfoldSeconds), displayCellSize);
            yield return null;
        }
        cube.SetUnfold(1, displayCellSize);
        yield return new WaitForSeconds(unfoldedHoldSeconds);
        for (float elapsed = 0; elapsed < victoryShrinkSeconds; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0, 1, elapsed / victoryShrinkSeconds);
            cube.transform.localScale = cubeRestScale * Mathf.Lerp(1, victoryShrinkScale, t);
            ShowScreen(new Color(0, 0, 0, t));
            yield return null;
        }
        cube.transform.localScale = cubeRestScale * victoryShrinkScale;
        cube.enabled = false;
        sonar?.ClearPulses();
        ShowScreen(Color.black);
        if (victoryRingPrefab == null)
        {
            Debug.LogError("Boss victory requires the Ring prefab.", this);
            ResetEncounter();
            yield break;
        }
        Vector3 ringStart = cube.transform.position;
        var ringObject = Instantiate(victoryRingPrefab, ringStart, level.transform.rotation, level.transform);
        ringObject.name = "Boss victory ring";
        ringObject.transform.localScale *= victoryRingScale;
        foreach (var action in ringObject.GetComponentsInChildren<InteractableObject>(true)) action.interactionEnabled = false;
        var ringColliders = ringObject.GetComponentsInChildren<Collider>(true);
        foreach (var collider in ringColliders) collider.enabled = false;
        foreach (var body in ringObject.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.useGravity = false; }
        victoryRing = ringObject.AddComponent<BossVictoryRing>();
        victoryRing.Initialize(this, ringBlinkSeconds, ringBlinkMinimum);
        // Release mouse look for the visible fall; retain the movement lock until
        // the landing wave fades. The camera no longer follows the ring automatically.
        victoryLookActive = false;
        victoryLookTarget = null;
        player.EncounterLookAllowed = true;
        Vector3 landing = new Vector3(ringStart.x, FloorY + .006f, ringStart.z);
        victoryRing.BeginFall(ringBounceGravity, ringBounceHeight, Mathf.Max(.1f, ringStart.y - FloorY),
            ringDropSeconds, ringFallSideAngle, ringFallSpin);
        Physics.SyncTransforms();
        HideScreen();
        float landingDeadline = Time.time + Mathf.Max(8, ringDropSeconds + 3);
        while (!victoryRing.FirstGroundContact && Time.time < landingDeadline) yield return null;
        if (!victoryRing.FirstGroundContact)
        {
            // Keep the ending usable even if an authored floor collider is missing.
            ringObject.GetComponent<Rigidbody>().isKinematic = true;
            ringObject.transform.position = landing;
        }
        landing = ringObject.transform.position;
        victoryRing.SetLanded();
        if (ringLandingSound != null)
        {
            var sound = ringObject.AddComponent<SpatialAudioEmitter>();
            sound.settings = skillSoundSettings;
            sound.volume = ringLandingVolume;
            sound.PlayOneShot(ringLandingSound);
        }
        float waveRange = ringWaveRange > 0 ? ringWaveRange
            : Mathf.Sqrt(4 * Radius * Radius + room.Height(level) * room.Height(level)) + 2;
        Vector3 waveOrigin = new Vector3(landing.x, victoryRing.WorldBounds.max.y + .05f, landing.z);
        sonar?.EmitFrom(waveOrigin, waveRange, ringWaveSpeed, ringWaveAfterglow,
            distanceSettings: null, source: ringObject.transform);
        float unlockAt = Time.time + waveRange / Mathf.Max(.1f, ringWaveSpeed) + ringWaveAfterglow + ringUnlockDelay;
        // Only this first contact plays sound/sonar. Further contacts and settling
        // are handled by the rigidbody, without replaying landing audio.
        while (Time.time < unlockAt) yield return null;
        victoryLookActive = false;
        awaitingVictoryRing = true;
        UnlockInput();
        while (!victoryRingCollected) yield return null;
        LockInput();
        for (float elapsed = 0; elapsed < fadeWhiteSeconds; elapsed += Time.deltaTime)
        {
            float fade = Mathf.SmoothStep(0, 1, elapsed / fadeWhiteSeconds);
            ShowScreen(new Color(1, 1, 1, fade));
            music.SetGain(1 - fade);
            yield return null;
        }
        ShowScreen(Color.white);
        music.SetGain(0);
        music.Stop();
        State = EncounterState.Won;
        sequence = null;
        StoryRunProgress.Current.CompleteBoss();
        onVictory.Invoke();
        UnlockInput();
        foreach (var opening in FindObjectsByType<SoundShellOpening>())
            if (opening.player == player && opening.world == world && opening.TryReturnAfterBossVictory()) yield break;

        // The independent Boss test scene has no initial room; continue in the campaign.
        const string campaignPath = "Assets/Scenes/SoundAndShell.unity";
#if UNITY_EDITOR
        UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(campaignPath,
            new UnityEngine.SceneManagement.LoadSceneParameters(UnityEngine.SceneManagement.LoadSceneMode.Single));
#else
        UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(campaignPath, UnityEngine.SceneManagement.LoadSceneMode.Single);
#endif
    }

    internal bool CanCollectVictoryRing(PlayerInteraction actor)
        => awaitingVictoryRing && !victoryRingCollected && State == EncounterState.Ending && !inputLocked
            && actor != null && actor.GetComponent<PlayerController>() == player && !player.InputSuspended
            && vitality != null && !vitality.IsDead;

    internal bool TryCollectVictoryRing(PlayerInteraction actor)
    {
        if (!CanCollectVictoryRing(actor)) return false;
        victoryRingCollected = true;
        awaitingVictoryRing = false;
        LockInput();
        return true;
    }

    void EmitReveal(float speed, float afterglow, AudioClip sound = null)
    {
        PlayEffect(sound != null ? sound : revealSound);
        float arenaHeight = room.Height(level);
        float range = revealWaveRange > 0 ? revealWaveRange : Mathf.Sqrt(Radius * Radius + arenaHeight * arenaHeight) + 2;
        if (sonar != null) sonar.EmitDangerFrom(ToWorld(Vector2.zero, Mathf.Min(hoverHeight, arenaHeight * .5f)), range, speed, afterglow, null,
            source: cube != null ? cube.transform : null);
    }

    void PlayEffect(AudioClip clip)
    {
        if (clip == null) return;
        effectsAudio.settings = skillSoundSettings;
        effectsAudio.volume = skillSoundVolume;
        effectsAudio.PlayOneShot(clip);
    }

    SpatialAudioEmitter GetSlamVoice()
    {
        int index = nextSlamVoice++ % slamAudio.Length;
        if (slamAudio[index] != null) return slamAudio[index];
        string label = "Boss slam audio " + index;
        var voice = transform.Find(label)?.GetComponent<SpatialAudioEmitter>();
        if (voice == null)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            voice = go.AddComponent<SpatialAudioEmitter>();
        }
        return slamAudio[index] = voice;
    }

    internal LineRenderer CreateLine(string label, Material material, Color color, float width)
    {
        var go = new GameObject(label);
        go.transform.SetParent(level.transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.startColor = line.endColor = color;
        line.widthMultiplier = width;
        line.positionCount = 2;
        return line;
    }

    LineRenderer DrawMarker(Vector2 point, float radius)
    {
        var line = CreateLine("Boss impact warning", effectMaterial, new Color(1, .04f, .015f), impactWarningWidth);
        line.positionCount = 49;
        float length = 0;
        Vector3 previous = default;
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i * (2 * Mathf.PI / (line.positionCount - 1));
            Vector3 position = ToWorld(Vector2.ClampMagnitude(point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, Radius), .04f);
            line.SetPosition(i, position);
            if (i > 0) length += Vector3.Distance(previous, position);
            previous = position;
        }
        SetAttackPoints(line, new MaterialPropertyBlock(), length, impactWarningWidth);
        attackLines.Add(line);
        return line;
    }

    void SetAttackPoints(LineRenderer line, MaterialPropertyBlock properties, float length, float width)
    {
        float spacing = Mathf.Max(.005f, attackPointSpacing);
        properties.SetFloat("_PointColumns", Mathf.Max(1, length / spacing));
        properties.SetFloat("_PointRows", Mathf.Max(1, width / spacing));
        line.SetPropertyBlock(properties);
    }

    void RemoveLine(LineRenderer line)
    {
        attackLines.Remove(line);
        if (line == null) return;
        line.enabled = false;
        Destroy(line.gameObject);
    }

    void ShowScreen(Color color)
    {
        if (screen == null)
        {
            var root = new GameObject("Boss screen", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            OverlayCanvas = root.GetComponent<Canvas>();
            OverlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            OverlayCanvas.sortingOrder = 32760;
            var image = new GameObject("Cover", typeof(RectTransform), typeof(Image));
            image.transform.SetParent(root.transform, false);
            screen = image.GetComponent<Image>();
            screen.raycastTarget = false;
            screen.rectTransform.anchorMin = Vector2.zero;
            screen.rectTransform.anchorMax = Vector2.one;
            screen.rectTransform.offsetMin = screen.rectTransform.offsetMax = Vector2.zero;
        }
        screen.color = color;
        screen.enabled = true;
    }

    void HideScreen()
    {
        if (screen != null) screen.enabled = false;
    }

    void LockInput()
    {
        if (player == null || inputLocked) return;
        inputLocked = true;
        originalInputLock = player.EncounterInputLocked;
        player.EncounterInputLocked = true;
        player.EncounterLookAllowed = false;
    }

    void UnlockInput()
    {
        if (player != null) player.EncounterLookAllowed = false;
        if (!inputLocked) return;
        inputLocked = false;
        if (player != null)
            player.EncounterInputLocked = originalInputLock;
    }

    void StopSequence()
    {
        if (sequence != null) StopCoroutine(sequence);
        sequence = null;
    }

    static void RemoveSummon(GameObject enemy)
    {
        if (enemy == null) return;
        enemy.SetActive(false);
        Destroy(enemy);
    }

    void ClearAttacks()
    {
        for (int i = 0; i < slamAudio.Length; i++)
        {
            // Recover existing voices after an Editor script reload as well.
            var voice = slamAudio[i] != null ? slamAudio[i] : transform.Find("Boss slam audio " + i)?.GetComponent<SpatialAudioEmitter>();
            if (voice != null) voice.Source.Stop();
        }
        waves.Clear();
        foreach (var entry in summons) RemoveSummon(entry.enemy);
        summons.Clear();
        while (attackLines.Count > 0) RemoveLine(attackLines[^1]);
        CurrentSkill = null;
    }

    public void ResetEncounter()
    {
        resetRequested = false;
        encounterClockStarted = false;
        encounterHasMusic = false;
        victoryLookActive = false;
        victoryLookTarget = null;
        victoryLookHorizontal = false;
        awaitingVictoryRing = victoryRingCollected = false;
        if (victoryRing != null)
        {
            victoryRing.gameObject.SetActive(false);
            Destroy(victoryRing.gameObject);
            victoryRing = null;
        }
        CurrentMusicStage = 0;
        fightEndsAt = null;
        if (State != EncounterState.Waiting) sonar?.ClearPulses();
        StopSequence();
        ClearAttacks();
        music?.Stop();
        if (effectsAudio != null) effectsAudio.Source.Stop();
        aimHistory.Clear();
        HideScreen();
        UnlockInput();
        State = EncounterState.Waiting;
        if (entranceSeal != null) entranceSeal.SetActive(false);
        if (memoryGate != null) memoryGate.gameObject.SetActive(true);
        if (room == null || cube == null) return;
        cube.transform.localScale = cubeRestScale;
        cube.SetNetRotation(Quaternion.identity);
        cube.SetUnfold(0, unfoldedCellSize);
        cube.enabled = false;
        transform.position = ToWorld(Vector2.zero, SafeHeight(hoverHeight));
    }

    void RequestReset() => resetRequested = true;

    void OnDestroy()
    {
        if (entranceSeal != null) Destroy(entranceSeal);
    }

    void OnDisable()
    {
        if (vitality != null) vitality.Died -= RequestReset;
        ResetEncounter();
        player = null;
        vitality = null;
    }
}
