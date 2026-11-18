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
    [Min(0)] public float revealMusicTime = 21.33f;
    [Min(0)] public float introBlackoutTime = 5.33f;
    public bool loopFromReveal = true;
    [Min(0)] public float loopStartTime = 21.33f;
    [Tooltip("0 表示音频末尾。"), Min(0)] public float loopEndTime;
    [Min(0)] public float revealWithoutMusic = 2;
    [Min(.1f)] public float revealWaveSpeed = 70;
    [Min(.05f)] public float revealWaveAfterglow = 3.5f;
    [Tooltip("0 表示根据大厅尺寸计算。"), Min(0)] public float revealWaveRange;
    [Min(0)] public float beforeCombatSeconds = 5;

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

    [Header("演出音效（可选）")]
    public AudioClip chargeSound;
    public AudioClip impactSound;
    public AudioClip laserSound;
    public AudioClip revealSound;

    [Header("胜利演出")]
    [Min(.1f)] public float victoryWaveSpeed = 70;
    [Min(.05f)] public float victoryWaveAfterglow = 9;
    [Min(0)] public float victoryWaveLead = .65f;
    [Min(.1f)] public float victoryHeight = 6;
    [Min(0)] public float victoryRiseSeconds = 1.8f;
    [Min(0)] public float unfoldSeconds = 2.8f;
    [Min(.1f)] public float unfoldedCellSize = 1.9f;
    [Min(0)] public float unfoldedHoldSeconds = .7f;
    [Min(0)] public float fadeWhiteSeconds = 3;
    public UnityEvent onVictory = new();

    public EncounterState State { get; private set; }
    public float RemainingSeconds => State == EncounterState.Fighting && fightEndsAt.HasValue
        ? Mathf.Max(0, fightEndsAt.Value - Time.time) : 0;
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
    Coroutine sequence;
    Image screen;
    bool inputLocked, originalInputLock, resetRequested;
    float? fightEndsAt;
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
        memoryGate = level.GetComponentInChildren<BossMemoryGate>(true);
        music = new BossMusic(gameObject);
        effectsAudio = transform.Find("Boss spatial effects")?.GetComponent<SpatialAudioEmitter>();
        if (effectsAudio == null)
        {
            var effects = new GameObject("Boss spatial effects");
            effects.transform.SetParent(transform, false);
            effectsAudio = effects.AddComponent<SpatialAudioEmitter>();
        }
        ResetEncounter();
        return true;
    }

    void Update()
    {
        if (!EnsureRuntime()) return;
        if (resetRequested) ResetEncounter();
        music.Tick();
        if (State == EncounterState.Won || State == EncounterState.Ending) return;

        if (player == null) FindPlayer();
        if (player == null || capsule == null || vitality == null || vitality.IsDead || !capsule.enabled) return;
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
        StopSequence();
        ClearAttacks();
        sonar?.ClearPulses();
        effectsAudio.Source.Stop();
        sequence = StartCoroutine(PlayVictory());
    }

    IEnumerator PlayEncounter(bool startAtReveal = false)
    {
        StoryRunProgress.Current.StartBoss();
        CloseArenaEntrance();
        if (startAtReveal) player.FacePoint(cube.transform.position);
        float cutoff = battleMusic != null ? (loopEndTime > 0 ? Mathf.Min(loopEndTime, battleMusic.length) : battleMusic.length) : 0;
        float reveal = Mathf.Min(revealMusicTime, cutoff);
        bool playingMusic = music.Play(battleMusic, musicVolume, loopFromReveal ? revealMusicTime : loopStartTime,
            loopEndTime, startAtReveal ? reveal : 0);
        if (!startAtReveal)
        {
            State = EncounterState.Intro;
            yield return PlayIntro(playingMusic, reveal);
        }

        State = EncounterState.Reveal;
        if (memoryGate != null) memoryGate.gameObject.SetActive(false);
        HideScreen();
        cube.enabled = true;
        EmitReveal(revealWaveSpeed, revealWaveAfterglow);
        UnlockInput();
        yield return new WaitForSeconds(beforeCombatSeconds);
        yield return Fight();
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
        float revealAt = playingMusic ? reveal : fade + revealWithoutMusic;
        while (Elapsed() < revealAt) yield return null;
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
            switch (CurrentSkill)
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
        PlayEffect(chargeSound);
        yield return new WaitForSeconds(summonWindup);
        if (summonPrefab == null || navigation == null || !navigation.Ready) yield break;
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
        PlayEffect(chargeSound);
        var mark = DrawMarker(target, impactRadius);
        yield return new WaitForSeconds(slamWindup);
        yield return Fly(target, slamCenterHeight, slamSeconds, accelerating: true);
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
        PlayEffect(chargeSound);
        yield return new WaitForSeconds(sweepWindup);
        Vector2 from = ToArena(transform.position);
        Vector2 direction = (ToArena(capsule.bounds.center) - from).normalized;
        if (direction.sqrMagnitude < .5f) direction = Vector2.up;
        Vector2 destination = ClampBoss(from + direction * sweepTravel);
        yield return Fly(destination, hoverHeight, sweepSeconds);
        PlayEffect(impactSound);
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
        EmitReveal(victoryWaveSpeed, victoryWaveAfterglow);
        yield return new WaitForSeconds(victoryWaveLead);
        Vector2 viewer = ToArena(player.ViewTransform.position);
        Vector2 destination = viewer.sqrMagnitude < 42
            ? ClampBoss(viewer + (viewer.sqrMagnitude > .1f ? -viewer.normalized : Vector2.up) * 7)
            : Vector2.zero;
        yield return Fly(destination, victoryHeight, victoryRiseSeconds);
        player.FacePoint(cube.transform.position);
        for (float elapsed = 0; elapsed < unfoldSeconds; elapsed += Time.deltaTime)
        {
            cube.SetUnfold(Mathf.SmoothStep(0, 1, elapsed / unfoldSeconds), unfoldedCellSize);
            yield return null;
        }
        cube.SetUnfold(1, unfoldedCellSize);
        yield return new WaitForSeconds(unfoldedHoldSeconds);
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

    void EmitReveal(float speed, float afterglow)
    {
        PlayEffect(revealSound);
        float arenaHeight = room.Height(level);
        float range = revealWaveRange > 0 ? revealWaveRange : Mathf.Sqrt(Radius * Radius + arenaHeight * arenaHeight) + 2;
        if (sonar != null) sonar.EmitDangerFrom(ToWorld(Vector2.zero, Mathf.Min(hoverHeight, arenaHeight * .5f)), range, speed, afterglow, null,
            source: cube != null ? cube.transform : null);
    }

    void PlayEffect(AudioClip clip)
    {
        if (clip != null) effectsAudio.PlayOneShot(clip);
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
    }

    void UnlockInput()
    {
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
        waves.Clear();
        foreach (var entry in summons) RemoveSummon(entry.enemy);
        summons.Clear();
        while (attackLines.Count > 0) RemoveLine(attackLines[^1]);
        CurrentSkill = null;
    }

    public void ResetEncounter()
    {
        resetRequested = false;
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
