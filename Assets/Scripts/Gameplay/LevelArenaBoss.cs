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
    [Min(0)] public float laserVisibleSeconds = .28f;
    public BossWaveSettings slamWave = new();

    [Header("横扫")]
    [Min(0)] public float sweepWindup = .7f;
    [Min(0)] public float sweepSeconds = .35f;
    [Min(0)] public float sweepTravel = 2;
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
    public float RemainingSeconds => State == EncounterState.Fighting ? Mathf.Max(0, fightEndsAt - Time.time) : 0;
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
    float fightEndsAt;
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
                StopSequence();
                ClearAttacks();
                sonar?.ClearPulses();
                effectsAudio.Source.Stop();
                sequence = StartCoroutine(PlayVictory());
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

    IEnumerator PlayEncounter()
    {
        State = EncounterState.Intro;
        StoryRunProgress.Current.StartBoss();
        float cutoff = battleMusic != null ? (loopEndTime > 0 ? Mathf.Min(loopEndTime, battleMusic.length) : battleMusic.length) : 0;
        float reveal = Mathf.Min(revealMusicTime, cutoff);
        bool playingMusic = music.Play(battleMusic, musicVolume, loopFromReveal ? revealMusicTime : loopStartTime, loopEndTime);
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

        State = EncounterState.Reveal;
        HideScreen();
        cube.enabled = true;
        EmitReveal(revealWaveSpeed, revealWaveAfterglow);
        UnlockInput();
        yield return new WaitForSeconds(beforeCombatSeconds);

        State = EncounterState.Fighting;
        fightEndsAt = Time.time + Mathf.Max(.1f, survivalSeconds);
        int next = 0;
        while (true)
        {
            yield return Roam(betweenSkills);
            if (rotationOrder == null || rotationOrder.Length == 0) { yield return null; continue; }
            CurrentSkill = rotationOrder[next++ % rotationOrder.Length];
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
        Vector3 origin = transform.position;
        Vector3 direction = (AimAt(Time.time - laserLookback) - origin).normalized;
        if (direction.sqrMagnitude < .5f) yield break;
        Vector3 end = origin + direction * laserRange;
        PlayEffect(laserSound);
        if (Physics.Raycast(origin, direction, out var hit, laserRange, laserLayers, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            if (hit.collider.GetComponentInParent<PlayerVitality>() == vitality) DamagePlayer();
        }
        var line = CreateLine("Boss laser", effectMaterial, new Color(1, .03f, .015f), laserWidth);
        attackLines.Add(line);
        line.SetPositions(new[] { origin, end });
        yield return new WaitForSeconds(laserVisibleSeconds);
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
        AddWave(sweepWave, destination, direction);
    }

    void AddWave(BossWaveSettings settings, Vector2 point, Vector2 direction)
    {
        Vector3 origin = ToWorld(point, 0);
        Vector3 forward = (ToWorld(point + direction, 0) - origin).normalized;
        waves.Add(new BossGroundWave(this, settings, point, direction));
        sonar?.EmitBossGroundWave(origin, ToWorld(Vector2.zero, 0), Radius, forward, settings);
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

    float SafeHeight(float height) => Mathf.Clamp(height, BossExtent, Mathf.Max(BossExtent, level.wallHeight - BossExtent));
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
            ShowScreen(new Color(1, 1, 1, Mathf.SmoothStep(0, 1, elapsed / fadeWhiteSeconds)));
            yield return null;
        }
        ShowScreen(Color.white);
        music.Stop();
        State = EncounterState.Won;
        sequence = null;
        onVictory.Invoke();
    }

    void EmitReveal(float speed, float afterglow)
    {
        PlayEffect(revealSound);
        float range = revealWaveRange > 0 ? revealWaveRange : Mathf.Sqrt(Radius * Radius + level.wallHeight * level.wallHeight) + 2;
        if (sonar != null) sonar.EmitDangerFrom(ToWorld(Vector2.zero, Mathf.Min(hoverHeight, level.wallHeight * .5f)), range, speed, afterglow, null,
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
        var line = CreateLine("Boss impact warning", effectMaterial, new Color(.72f, .04f, .015f), .06f);
        line.positionCount = 49;
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i * (2 * Mathf.PI / (line.positionCount - 1));
            line.SetPosition(i, ToWorld(Vector2.ClampMagnitude(point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, Radius), .04f));
        }
        attackLines.Add(line);
        return line;
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
        if (room == null || cube == null) return;
        cube.SetUnfold(0, unfoldedCellSize);
        cube.enabled = false;
        transform.position = ToWorld(Vector2.zero, SafeHeight(hoverHeight));
    }

    void RequestReset() => resetRequested = true;

    void OnDisable()
    {
        if (vitality != null) vitality.Died -= RequestReset;
        ResetEncounter();
        player = null;
        vitality = null;
    }
}
