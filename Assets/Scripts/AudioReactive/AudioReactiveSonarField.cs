using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

/// <summary>Audio-wave slots with cached source-to-surface occlusion.</summary>
[DefaultExecutionOrder(200), DisallowMultipleComponent]
public sealed class AudioReactiveSonarField : MonoBehaviour
{
    public const int Capacity = 16;
    static AudioReactiveSonarField instance;
    static readonly ProfilerMarker triggerMarker = new ProfilerMarker("AudioSonar.Trigger");
    static readonly ProfilerMarker updateMarker = new ProfilerMarker("AudioSonar.Update");

    public sealed class MovingLoop
    {
        internal MovingLoop() { }
        internal Vector3 position;
        internal float gain;
        internal double start;
        public bool IsActive { get; internal set; } = true;
        public void Update(Vector3 origin, float envelope) { position = origin; gain = Mathf.Clamp01(envelope); }
        public void Stop() => IsActive = false;
    }

    sealed class Voice
    {
        public ImpactSoundPool.Playback playback;
        public MovingLoop moving;
        public AudioReactiveVisualSettings settings;
        public AudioReactiveClip clip;
        public List<(StoryCollectible item, float distance)> collectibles;
        public float range, speed, glow, duration;
        public Vector3 position, blockerOrigin;
        public float occlusionRange;
        public bool highQuality;
        public Transform source;
        public double nextOcclusion;
        public Vector4 parameters, data, falloff, density;
        public double Start => moving != null ? moving.start : playback.ContentDspTime;
        public double ContentEnd => moving != null
            ? (moving.IsActive ? double.PositiveInfinity : double.NegativeInfinity) : playback.ContentEndDspTime;
        public double End => ContentEnd + range / speed + glow;
    }
    readonly Voice[] voices = new Voice[Capacity];
    readonly Vector4[] origins = new Vector4[Capacity], blockerOrigins = new Vector4[Capacity], parameters = new Vector4[Capacity], levels = new Vector4[Capacity],
        data = new Vector4[Capacity], falloffs = new Vector4[Capacity], densities = new Vector4[Capacity];
    readonly float[] pixels = new float[AudioReactiveClip.TextureWidth];
    Texture2DArray analysis, blockers;
    Texture2D upload;
    SonarPulseField sampler;
    int nextMovingSlot;
    public int ActiveSourceCount { get; private set; }
    public static bool HasGoldWaves => instance != null && instance.hasGold;
    bool hasGold;
    public int Emissions { get; private set; }
    public int OcclusionCaptures { get; private set; }
    public int LastRayCount { get; private set; }
    public int Replacements { get; private set; }
    public bool HasResources => analysis != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { instance = null; Shader.SetGlobalFloat("_AudioSonarActive", 0); }
    public static AudioReactiveSonarField GetOrCreate()
    {
        if (instance != null && instance.isActiveAndEnabled) return instance;
        return new GameObject("Audio Reactive Sonar (runtime)").AddComponent<AudioReactiveSonarField>();
    }
    void OnEnable()
    {
        if (instance != null && instance != this && instance.isActiveAndEnabled)
        { Debug.LogWarning("Only one audio-reactive wave field may publish globals in a scene.", this); enabled = false; return; }
        instance = this; Shader.SetGlobalFloat("_AudioSonarActive", 0);
    }
    void EnsureResources()
    {
        if (analysis != null) return;
        analysis = new Texture2DArray(AudioReactiveClip.TextureWidth, 1, Capacity, TextureFormat.RFloat, false, true)
        { name = "Audio sonar quantized analysis", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        upload = new Texture2D(AudioReactiveClip.TextureWidth, 1, TextureFormat.RFloat, false, true)
        { name = "Audio sonar analysis upload", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        blockers = new Texture2DArray(48, 48, Capacity * 6, TextureFormat.RGBAFloat, false, true)
        { name = "Audio sonar independent blockers", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        Shader.SetGlobalTexture("_AudioSonarAnalysis", analysis);
        Shader.SetGlobalTexture("_AudioSonarBlockers", blockers);
    }
    public bool Emit(Vector3 position, AudioReactiveClip clip, AudioReactiveVisualSettings settings, ImpactSoundPool pool, float volume, float strength, bool looping, out ImpactSoundPool.Playback playback, bool gold = false, Transform source = null, double? scheduledStart = null)
    {
        playback = null;
        if (!Application.isPlaying || !isActiveAndEnabled || clip == null || !clip.IsReady || settings == null || strength <= 0
            || pool == null || !pool.isActiveAndEnabled || pool.clip != clip.clip || !pool.Preload(looping)) return false;
        using var scope = triggerMarker.Auto();
        if (!TryPrepareVoice(position, clip, settings, strength, looping, source, out int slot, out var voice)) return false;
        bool played = scheduledStart.HasValue ? pool.PlayScheduledAt(position, volume, looping, scheduledStart.Value, out playback)
            : looping ? pool.PlayLoopAt(position, volume, out playback) : pool.PlayAt(position, volume, out playback);
        if (!played) { voices[slot] = null; Refresh(); return false; }
        voice.playback = playback;
        if (gold) voice.parameters.w = 2;
        PublishVoice(slot, voice);
        return true;
    }

    internal bool EmitCopy(Vector3 position, Transform source, AudioReactiveClip clip, AudioReactiveVisualSettings settings,
        float strength, bool looping, bool gold, ImpactSoundPool.Playback sharedPlayback)
    {
        if (!TryPrepareVoice(position, clip, settings, strength, looping, source, out int slot, out var voice, false)) return false;
        voice.playback = sharedPlayback;
        if (gold) voice.parameters.w = 2;
        PublishVoice(slot, voice);
        return true;
    }

    public MovingLoop StartMovingLoop(Vector3 position, AudioReactiveClip clip, AudioReactiveVisualSettings settings, double dspStart, Transform source = null)
    {
        using var scope = triggerMarker.Auto();
        if (!TryPrepareVoice(position, clip, settings, 1, true, source, out int slot, out var voice)) return null;
        voice.moving = new MovingLoop { position = position, start = dspStart };
        voice.nextOcclusion = AudioSettings.dspTime + .25;
        PublishVoice(slot, voice);
        return voice.moving;
    }

    bool TryPrepareVoice(Vector3 position, AudioReactiveClip clip, AudioReactiveVisualSettings settings, float strength, bool looping, Transform source,
        out int slot, out Voice voice, bool allowReplacement = true)
    {
        slot = -1;
        voice = null;
        if (!Application.isPlaying || !isActiveAndEnabled || clip == null || !clip.IsReady || settings == null || strength <= 0) return false;
        if (sampler == null || !sampler.isActiveAndEnabled) sampler = FindAnyObjectByType<SonarPulseField>();
        if (sampler == null || !sampler.isActiveAndEnabled) return false;
        EnsureResources(); double now = AudioSettings.dspTime;
        double earliest = double.PositiveInfinity;
        for (int i = 0; i < Capacity; i++)
        {
            if (voices[i] == null || voices[i].End <= now) { slot = i; break; }
            if (allowReplacement && voices[i].End < earliest) { earliest = voices[i].End; slot = i; }
        }
        if (slot < 0) return false; // All slots contain continuous sources: never silently hide a running radio.
        float range = Mathf.Max(1, settings.range), speed = Mathf.Max(.1f, settings.waveSpeed);
        Vector4 falloff = settings.distance != null ? settings.distance.Pack(range) : new Vector4(0, range, 0, 1);
        Vector4 density = settings.density != null ? settings.density.Pack(range) : Vector4.zero;
        float effective = falloff.z > 0 ? Mathf.Min(range, falloff.y) : range;
        if (density.w > .5f) effective = Mathf.Min(effective, density.y);
        if (!CaptureOcclusion(slot, position, effective, settings.highQualityOcclusion, source)) return false;
        clip.PackTexture(pixels); upload.SetPixelData(pixels, 0); upload.Apply(false);
        Graphics.CopyTexture(upload, 0, 0, analysis, slot, 0);
        falloff.w *= Mathf.Max(0, strength);
        voice = new Voice {
            settings = settings, clip = clip, position = position, blockerOrigin = position, range = range, speed = speed, duration = clip.Duration,
            occlusionRange = effective, highQuality = settings.highQualityOcclusion, source = source,
            glow = Mathf.Clamp(settings.afterglow, 0, 2),
            parameters = new Vector4(range, speed, Mathf.Clamp(settings.afterglow, 0, 2), 1),
            data = new Vector4(clip.FrameCount, clip.FrameRate, clip.Duration, looping ? clip.Duration : 0), falloff = falloff, density = density
        };
        CaptureCollectiblePaths(voice);
        return true;
    }

    void CaptureCollectiblePaths(Voice voice)
    {
        // Reuse the blocker query's source and ignore list before the next wave samples.
        voice.collectibles?.Clear();
        foreach (var item in StoryCollectible.Active)
        {
            if (item == null || item.Collected || item.gameObject.scene != sampler.gameObject.scene) continue;
            float distance = item.ScanDistance(sampler, voice.blockerOrigin, voice.occlusionRange);
            if (float.IsPositiveInfinity(distance)) continue;
            voice.collectibles ??= new();
            voice.collectibles.Add((item, distance));
        }
    }

    static void RevealCollectibles(Voice voice, double now, Vector3 origin)
    {
        if (voice.collectibles == null || now < voice.Start) return;
        float elapsed = (float)(now - voice.Start);
        float contentLength = (float)(voice.ContentEnd - voice.Start);
        float sourceGain = voice.falloff.w * (voice.moving?.gain ?? 1);
        foreach (var (item, sampledDistance) in voice.collectibles)
        {
            if (item == null || !item.isActiveAndEnabled || item.Collected) continue;
            float distance = sampledDistance;
            if (voice.moving != null)
            {
                Vector3 point = item.InteractionPosition;
                distance += Vector3.Distance(point, origin) - Vector3.Distance(point, voice.blockerOrigin);
            }
            if (distance >= voice.occlusionRange) continue;
            float age = elapsed - Mathf.Max(0, distance) / voice.speed;
            // Match the peak and afterglow samples used by AudioSonarTrail in the shader.
            float peak = ReadPeak(voice, age, contentLength);
            if (voice.glow > .0001f)
            {
                peak = Mathf.Max(peak, ReadPeak(voice, age - voice.glow * .5f, contentLength) * .55f);
                peak = Mathf.Max(peak, ReadPeak(voice, age - voice.glow, contentLength) * .15f);
            }
            float distanceGain = voice.falloff.z > 0
                ? Mathf.Pow(1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(voice.falloff.x, voice.falloff.y, distance)), voice.falloff.z)
                : 1;
            if (voice.settings.MapPeak(peak) * distanceGain * sourceGain >= .0001f) item.RevealFromSonar();
        }
    }

    static float ReadPeak(Voice voice, float time, float contentLength)
    {
        if (time < 0 || time >= contentLength) return 0;
        return voice.clip.Evaluate(voice.data.w > 0 ? time % voice.duration : time, 0);
    }

    bool CaptureOcclusion(int slot, Vector3 position, float range, bool highQuality, Transform source)
    {
        if (!sampler.CaptureBlockingPlanes(position, range, blockers, slot * 6, highQuality, source)) return false;
        OcclusionCaptures++;
        LastRayCount = highQuality ? 13824 : 3456;
        return true;
    }

    void PublishVoice(int slot, Voice voice)
    {
        if (voices[slot] != null && voices[slot].End > AudioSettings.dspTime) Replacements++;
        voices[slot] = voice;
        Emissions++;
        Refresh();
    }

    void LateUpdate()
    {
        if (sampler != null && sampler.isActiveAndEnabled) RefreshMovingOcclusion(AudioSettings.dspTime);
        Refresh();
    }

    void RefreshMovingOcclusion(double now)
    {
        // At most one moving occlusion refresh per frame across all active carts.
        for (int offset = 0; offset < Capacity; offset++)
        {
            int slot = (nextMovingSlot + offset) % Capacity;
            var voice = voices[slot];
            if (voice?.moving == null || !voice.moving.IsActive || voice.moving.gain <= 0 || now < voice.nextOcclusion
                || (voice.moving.position - voice.blockerOrigin).sqrMagnitude < .75f * .75f) continue;
            voice.nextOcclusion = now + .25;
            if (CaptureOcclusion(slot, voice.moving.position, voice.occlusionRange, voice.highQuality, voice.source))
            {
                voice.blockerOrigin = voice.moving.position;
                CaptureCollectiblePaths(voice);
            }
            nextMovingSlot = (slot + 1) % Capacity;
            return;
        }
    }

    public void Refresh()
    {
        if (!isActiveAndEnabled || instance != this) return;
        using var scope = updateMarker.Auto();
        double now = AudioSettings.dspTime; int active = 0, slotCount = 0;
        hasGold = false;
        for (int i = 0; i < Capacity; i++)
        {
            var voice = voices[i];
            if (voice == null || now > voice.End) { voices[i] = null; parameters[i] = Vector4.zero; continue; }
            active++; slotCount = i + 1;
            var p = voice.moving != null ? voice.moving.position : voice.position;
            RevealCollectibles(voice, now, p);
            origins[i] = new Vector4(p.x, p.y, p.z, (float)(now - voice.Start));
            blockerOrigins[i] = voice.blockerOrigin;
            parameters[i] = voice.parameters; levels[i] = voice.settings.PackPeakLevels(); data[i] = voice.data;
            hasGold |= voice.parameters.w > 1.5f;
            data[i].z = voice.data.w > 0 ? Mathf.Min(1e20f, (float)(voice.ContentEnd - voice.Start))
                : Mathf.Min(voice.duration, (float)(voice.ContentEnd - voice.Start));
            falloffs[i] = voice.falloff; densities[i] = voice.density;
            if (voice.moving != null) falloffs[i].w *= voice.moving.gain;
        }
        ActiveSourceCount = active;
        Shader.SetGlobalFloat("_AudioSonarActive", active > 0 ? 1 : 0);
        Shader.SetGlobalInt("_AudioSonarSlotCount", slotCount);
        if (active == 0) return;
        // The original property names retain their four-element allocation until Unity exits.
        Shader.SetGlobalVectorArray("_AudioSonarOriginsBySource", origins);
        Shader.SetGlobalVectorArray("_AudioSonarBlockerOriginsBySource", blockerOrigins);
        Shader.SetGlobalVectorArray("_AudioSonarParametersBySource", parameters);
        Shader.SetGlobalVectorArray("_AudioSonarLevelsBySource", levels);
        Shader.SetGlobalVectorArray("_AudioSonarDataBySource", data);
        Shader.SetGlobalVectorArray("_AudioSonarFalloffsBySource", falloffs);
        Shader.SetGlobalVectorArray("_AudioSonarDensitiesBySource", densities);
    }
    public void ClearVisuals()
    {
        for (int i = 0; i < Capacity; i++) { voices[i]?.moving?.Stop(); voices[i] = null; }
        ActiveSourceCount = 0;
        hasGold = false;
        if (instance == this) Shader.SetGlobalFloat("_AudioSonarActive", 0);
    }
    void OnDisable()
    {
        ClearVisuals(); if (instance == this)
        {
            instance = null; Shader.SetGlobalTexture("_AudioSonarAnalysis", null); Shader.SetGlobalTexture("_AudioSonarBlockers", null);
        }
        if (analysis != null) Destroy(analysis); if (blockers != null) Destroy(blockers); if (upload != null) Destroy(upload);
        analysis = null; blockers = null; upload = null;
    }
}
