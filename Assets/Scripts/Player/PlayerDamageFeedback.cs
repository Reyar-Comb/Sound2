using System.Collections.Generic;
using UnityEngine;

/// <summary>Screen-space, eight-direction dotted branches matching MonsterVines. Owns its runtime resources.</summary>
[DisallowMultipleComponent]
public sealed class PlayerDamageFeedback : MonoBehaviour
{
    [Min(.05f), InspectorName("死亡藤蔓铺满时间（秒）")] public float engulfSeconds = .2f;
    [Min(0), InspectorName("死亡满屏停留（秒）")] public float deathHoldSeconds = 1.5f;
    [Min(.05f), InspectorName("藤蔓消退与渐白时间（秒）")] public float dissolveSeconds = 1.8f;
    [Min(0), InspectorName("苏醒全白停留（秒）")] public float whiteHoldSeconds = .35f;
    [Min(.05f), InspectorName("苏醒渐显时间（秒）")] public float wakeSeconds = 2.2f;
    [Range(.08f, .4f), InspectorName("受伤藤蔓边缘宽度")] public float woundBorder = .24f;
    [Min(.08f), InspectorName("藤蔓生成间隔（秒）")] public float vineSpawnInterval = .26f;
    [Range(1, 6), InspectorName("每次新生藤蔓数量")] public int vineSpawnCount = 2;
    [Min(.5f), InspectorName("藤蔓存活时间（秒）")] public float vineLifetime = 2.8f;
    [Min(.005f), InspectorName("藤蔓逐点生长间隔（秒）")] public float vineStepSeconds = .027f;
    [Range(120, 1800), InspectorName("死亡铺满藤蔓数量")] public int deathVineCount = 1000;
    [Range(0, .8f), InspectorName("消退阶段渐白延迟比例")] public float whiteningDelay = 0;
    [Min(.01f), InspectorName("背景变白时间（秒）")] public float whiteningSeconds = .1f;

    [Header("死亡音效（均只播放一次）")]
    [InspectorName("死亡录音")] public AudioClip deathClip;
    [Range(0, 1), InspectorName("死亡音量")] public float deathVolume = .85f;
    [Min(0), InspectorName("死亡音效开始（秒）"), Tooltip("从死亡红色藤蔓开始算，0 为立即播放。")]
    public float deathAudioDelay;
    [InspectorName("耳鸣录音")] public AudioClip tinnitusClip;
    [Range(0, 1), InspectorName("耳鸣音量")] public float tinnitusVolume = .6f;
    [Min(0), InspectorName("耳鸣开始（秒）"), Tooltip("从死亡红色藤蔓开始算，不是从死亡录音结束算。")]
    public float tinnitusDelay = 1.2f;
    [Min(0), InspectorName("环境声渐弱时间（秒）"), Tooltip("从耳鸣触发到环境声完全静音。")]
    public float worldFadeOutSeconds = .6f;
    [Min(0), InspectorName("环境声恢复时间（秒）"), Tooltip("白屏完全退去后，环境声恢复到死亡前音量的时间。")]
    public float worldFadeInSeconds = .6f;

    public bool SequenceActive { get; private set; }
    public float WhiteAmount { get; private set; }
    public float Growth { get; private set; }
    public float Retreat { get; private set; }
    public Canvas OverlayCanvas => canvas;
    PlayerVitality vitality;
    Canvas canvas;
    Material material;
    Texture2D branches;
    const int Width = 320, Height = 180;
    struct VinePoint { public int index; public float born, lifetime, brightness, retireAt; }
    readonly List<VinePoint> points = new(65536);
    readonly Color32[] pixels = new Color32[Width * Height];
    System.Random random;
    float vineClock, nextSpawn, nextRaster;
    bool vinesRunning;
    int deathRoots;
    AudioSource deathSource, tinnitusSource;
    double deathAudioStart;
    float tinnitusFade;
    bool deathAudioActive, deathPlayed, tinnitusPlayed;
    static PlayerDamageFeedback worldAudioOwner;
    float originalWorldVolume, cueMasterVolume, worldVolumeAtRecovery;
    double worldFadeStartedAt;
    bool recoveringWorldAudio, wakeComplete;

    void Awake()
    {
        vitality = GetComponent<PlayerVitality>();
        var shader = Resources.Load<Shader>("PlayerDamageVines");
        if (shader == null) { Debug.LogError("Missing PlayerDamageVines shader.", this); enabled = false; return; }
        random = new System.Random(System.Guid.NewGuid().GetHashCode());
        branches = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
        { name = "Living damage vines", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        branches.SetPixels32(pixels); branches.Apply(false, false);
        material = new Material(shader) { name = "Player damage vines (runtime)" };
        var root = new GameObject("PlayerDamageOverlay", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 31999;
        var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 1;
        var panel = new GameObject("Red vines and white awakening", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        panel.transform.SetParent(root.transform, false);
        var image = panel.GetComponent<UnityEngine.UI.RawImage>();
        image.raycastTarget = false;
        image.material = material;
        image.texture = branches;
        image.rectTransform.anchorMin = Vector2.zero;
        image.rectTransform.anchorMax = Vector2.one;
        image.rectTransform.offsetMin = image.rectTransform.offsetMax = Vector2.zero;
        Clear();
    }

    void LateUpdate()
    {
        UpdateDeathAudio();
        UpdateWorldAudio();
        if (material == null) return;
        if (SequenceActive)
        {
            UpdateVines(Time.unscaledDeltaTime);
            return;
        }
        bool wounded = vitality != null && vitality.IsWounded;
        canvas.enabled = wounded && !SonicSpaceStartMenu.BlocksGameplay;
        if (!wounded) { ResetVines(); return; }
        if (SonicSpaceStartMenu.BlocksGameplay) return;
        // Fade inside the ten-second window, so at expiry both the risk and red edges are gone.
        float opacity = Mathf.Clamp01(vitality.WoundRemaining / .5f);
        SetVisual(woundBorder, 0, 0, 0, opacity);
        UpdateVines(Time.deltaTime);
    }

    public void BeginDeath()
    {
        StartVines();
        SequenceActive = true;
        deathRoots = 0;
        StopDeathAudio();
        RestoreWorldAudio();
        deathAudioStart = Time.unscaledTimeAsDouble;
        deathAudioActive = isActiveAndEnabled;
        deathPlayed = tinnitusPlayed = false;
        tinnitusFade = 1;
        wakeComplete = false;
        cueMasterVolume = worldAudioOwner != null ? worldAudioOwner.originalWorldVolume : AudioListener.volume;
        // Fresh branches grow throughout the advancing death front, without converging on one centre.
        ShowEngulf(0);
        UpdateDeathAudio(); // Zero delay plays on the same frame as the red overlay starts.
    }
    public void ShowEngulf(float progress)
    {
        float t = Mathf.Clamp01(progress);
        int target = Mathf.RoundToInt(Mathf.Lerp(14, Mathf.Clamp(deathVineCount, 120, 1800), t));
        if (target > deathRoots) { SpawnBurst(target - deathRoots, true); deathRoots = target; }
        SetVisual(Mathf.Lerp(woundBorder, 1.12f, t), 0, 0, t, 1);
    }
    public void ShowDissolve(float progress)
    {
        float t = Mathf.Clamp01(progress);
        // Line retirement and background luminance have separate envelopes.
        float elapsed = (t - Mathf.Clamp(whiteningDelay, 0, .8f)) * Mathf.Max(.05f, dissolveSeconds);
        float white = Mathf.SmoothStep(0, 1, elapsed / Mathf.Max(.01f, whiteningSeconds));
        SetVisual(1.12f, Mathf.SmoothStep(0, 1, t) * 1.12f, white, 1, 1);
    }
    public void ShowWake(float progress)
    {
        tinnitusFade = 1 - Mathf.Clamp01(progress);
        wakeComplete = tinnitusFade <= 0;
        SetVisual(0, 1.12f, tinnitusFade, 0, 0);
    }

    void UpdateDeathAudio()
    {
        if (!deathAudioActive) return;
        double elapsed = Time.unscaledTimeAsDouble - deathAudioStart;
        if (!deathPlayed && elapsed >= Mathf.Max(0, deathAudioDelay))
        {
            deathPlayed = true;
            PlayDeathClip(ref deathSource, deathClip, deathVolume);
        }
        if (!tinnitusPlayed && elapsed >= Mathf.Max(0, tinnitusDelay))
        {
            tinnitusPlayed = true;
            if (tinnitusFade > 0 && tinnitusClip != null)
            {
                PlayDeathClip(ref tinnitusSource, tinnitusClip, tinnitusVolume * tinnitusFade);
                BeginWorldFadeOut();
            }
        }
        if (deathSource != null) deathSource.volume = cueMasterVolume * Mathf.Clamp01(deathVolume);
        if (tinnitusSource != null) tinnitusSource.volume = cueMasterVolume * Mathf.Clamp01(tinnitusVolume) * tinnitusFade;
    }

    void PlayDeathClip(ref AudioSource source, AudioClip clip, float volume)
    {
        if (clip == null) return;
        if (source == null)
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0; // Player feedback, independent of world/monster audio.
            source.ignoreListenerVolume = true; // Ignore the temporary duck, but keep the saved master level.
            source.bypassReverbZones = true;
        }
        source.loop = false;
        source.volume = cueMasterVolume * Mathf.Clamp01(volume);
        source.clip = clip;
        source.Play();
    }

    void StopDeathAudio()
    {
        deathAudioActive = false;
        if (deathSource != null) deathSource.Stop();
        if (tinnitusSource != null) tinnitusSource.Stop();
    }

    void BeginWorldFadeOut()
    {
        // There is one listener volume, even when a test temporarily instantiates a second player.
        if (worldAudioOwner != null) return;
        worldAudioOwner = this;
        originalWorldVolume = AudioListener.volume;
        worldFadeStartedAt = Time.unscaledTimeAsDouble;
        recoveringWorldAudio = false;
    }

    void UpdateWorldAudio()
    {
        if (worldAudioOwner != this) return;
        float seconds = recoveringWorldAudio ? worldFadeInSeconds : worldFadeOutSeconds;
        float t = seconds <= 0 ? 1 : Mathf.Clamp01((float)(Time.unscaledTimeAsDouble - worldFadeStartedAt) / seconds);
        AudioListener.volume = recoveringWorldAudio
            ? Mathf.SmoothStep(worldVolumeAtRecovery, originalWorldVolume, t)
            : Mathf.SmoothStep(originalWorldVolume, 0, t);
        if (recoveringWorldAudio && t >= 1) RestoreWorldAudio();
    }

    void RestoreWorldAudio()
    {
        if (worldAudioOwner != this) return;
        AudioListener.volume = originalWorldVolume;
        worldAudioOwner = null;
        recoveringWorldAudio = false;
    }
    void SetVisual(float growth, float retreat, float white, float backdrop, float opacity)
    {
        Growth = growth; Retreat = retreat; WhiteAmount = white;
        if (material == null) return;
        canvas.enabled = true;
        material.SetFloat("_Growth", growth);
        material.SetFloat("_White", white);
        material.SetFloat("_Backdrop", backdrop);
        material.SetFloat("_Opacity", opacity);
    }
    public void Clear()
    {
        StopDeathAudio();
        if (worldAudioOwner == this && SequenceActive && wakeComplete && isActiveAndEnabled)
        {
            recoveringWorldAudio = true;
            worldVolumeAtRecovery = AudioListener.volume;
            worldFadeStartedAt = Time.unscaledTimeAsDouble;
            UpdateWorldAudio();
        }
        else RestoreWorldAudio();
        SequenceActive = false; Growth = Retreat = WhiteAmount = 0;
        ResetVines();
        if (canvas != null) canvas.enabled = false;
    }
    void OnDisable() { StopDeathAudio(); RestoreWorldAudio(); if (canvas != null) canvas.enabled = false; }
    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
        if (material != null) Destroy(material);
        if (branches != null) Destroy(branches);
        if (deathSource != null) Destroy(deathSource);
        if (tinnitusSource != null) Destroy(tinnitusSource);
    }

    void ResetVines()
    {
        if (!vinesRunning) return;
        points.Clear();
        deathRoots = 0;
        vinesRunning = false;
    }
    void StartVines()
    {
        random ??= new System.Random(System.Guid.NewGuid().GetHashCode());
        if (vinesRunning) return;
        vinesRunning = true;
        vineClock = nextSpawn = nextRaster = 0;
        SpawnBurst(8, false);
    }
    void UpdateVines(float deltaTime)
    {
        StartVines();
        vineClock += deltaTime;
        if (Growth > 0 && Retreat <= 0 && vineClock >= nextSpawn)
        {
            SpawnBurst(Mathf.Clamp(vineSpawnCount, 1, 6) * (SequenceActive ? 4 : 1), SequenceActive);
            nextSpawn = vineClock + Mathf.Max(.08f, vineSpawnInterval) * Range(.7f, 1.3f);
        }
        if (vineClock < nextRaster) return;
        nextRaster = vineClock + 1f / 30;
        System.Array.Clear(pixels, 0, pixels.Length);
        for (int i = points.Count - 1; i >= 0; i--)
        {
            var point = points[i];
            float age = vineClock - point.born;
            if (!SequenceActive && age > point.lifetime) { points.RemoveAt(i); continue; }
            if (age <= 0) continue;
            // Same staggered birth and long tail fade as the world-space MonsterVines.
            float alpha = Mathf.SmoothStep(0, 1, age / (SequenceActive ? Mathf.Clamp(engulfSeconds * .15f, .01f, .07f) : .18f));
            if (SequenceActive)
            {
                // Keep the dense field until retirement: centre vines must not die of age before outer vines.
                float retired = Mathf.SmoothStep(0, 1, (Retreat / 1.12f - point.retireAt) / .15f);
                alpha *= (1 - retired) * (.92f + .08f * Mathf.Sin(age * 5 + point.brightness * 19));
            }
            else alpha *= 1 - Mathf.SmoothStep(0, 1, (age - point.lifetime * .45f) / (point.lifetime * .55f));
            byte brightness = (byte)Mathf.RoundToInt(255 * alpha * point.brightness);
            if (brightness > pixels[point.index].r) pixels[point.index] = new Color32(brightness, 0, 0, 255);
        }
        branches.SetPixels32(pixels); branches.Apply(false, false);
    }
    float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());
    void SpawnBurst(int count, bool fullscreen)
    {
        for (int root = 0; root < count; root++)
        {
            Vector2 start;
            float angle;
            if (fullscreen)
            {
                start = new Vector2(Range(0, Width - 1), Range(0, Height - 1));
                angle = Range(0, Mathf.PI * 2);
            }
            else
            {
                int edge = random.Next(4);
                float inset = Range(0, 3);
                start = edge < 2 ? new Vector2(edge == 0 ? inset : Width - 1 - inset, Range(0, Height - 1))
                    : new Vector2(Range(0, Width - 1), edge == 2 ? inset : Height - 1 - inset);
                angle = (edge == 0 ? 0 : edge == 1 ? Mathf.PI : edge == 2 ? Mathf.PI * .5f : -Mathf.PI * .5f)
                    + Range(-1.05f, 1.05f);
            }
            int steps = fullscreen ? random.Next(22, 48) : Mathf.RoundToInt(woundBorder * Height * .5f * Range(.65f, 1.45f));
            float depth = 2 * Mathf.Min(Mathf.Min(start.x / Width, 1 - start.x / Width), Mathf.Min(start.y / Height, 1 - start.y / Height));
            float retireAt = Mathf.Clamp(.78f * depth + Range(-.035f, .035f), 0, .84f);
            float stepSeconds = fullscreen ? Mathf.Min(vineStepSeconds, Mathf.Max(.05f, engulfSeconds) / 200) : vineStepSeconds;
            Grow(start, angle, steps, 1, vineClock + Range(0, fullscreen ? .025f : .12f), Range(.7f, 1),
                Mathf.Max(.5f, vineLifetime) * Range(.8f, 1.2f), retireAt, stepSeconds);
        }
        if (points.Count > 120000) points.RemoveRange(0, points.Count - 120000);
    }
    void Grow(Vector2 position, float angle, int steps, int depth, float born, float brightness, float lifetime, float retireAt, float stepSeconds)
    {
        float turn = Mathf.PI * .25f;
        angle = Mathf.Round(angle / turn) * turn;
        int bendAt = random.Next(6, 16), branchAt = random.Next(5, Mathf.Max(6, steps - 3));
        for (int i = 0; i < steps; i++)
        {
            int x = Mathf.RoundToInt(position.x), y = Mathf.RoundToInt(position.y);
            if (x < 0 || x >= Width || y < 0 || y >= Height) break;
            float pointBirth = born + i * Mathf.Max(.001f, stepSeconds);
            points.Add(new VinePoint { index = y * Width + x, born = pointBirth, lifetime = lifetime, brightness = brightness, retireAt = retireAt });
            if (i == bendAt) angle += (random.Next(2) == 0 ? -1 : 1) * turn;
            if (depth > 0 && i == branchAt)
            {
                float branchAngle = angle + (random.Next(2) == 0 ? -1 : 1) * turn * random.Next(1, 3);
                Grow(position, branchAngle, random.Next(6, 15), depth - 1, pointBirth, brightness * .78f, lifetime, retireAt, stepSeconds);
            }
            position += new Vector2(Mathf.Round(Mathf.Cos(angle)), Mathf.Round(Mathf.Sin(angle)));
        }
    }
}
