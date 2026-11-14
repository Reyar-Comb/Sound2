using Unity.Profiling;
using UnityEngine;

/// <summary>Four independent audio-wave slots. A clip uses one occlusion snapshot, never one snapshot per analysis frame.</summary>
[DefaultExecutionOrder(200), DisallowMultipleComponent]
public sealed class AudioReactiveSonarField : MonoBehaviour
{
    public const int Capacity = 4;
    static AudioReactiveSonarField instance;
    static readonly ProfilerMarker triggerMarker = new ProfilerMarker("AudioSonar.Trigger");
    static readonly ProfilerMarker updateMarker = new ProfilerMarker("AudioSonar.Update");
    sealed class Voice
    {
        public ImpactSoundPool.Playback playback;
        public AudioReactiveVisualSettings settings;
        public float range, speed, glow, duration;
        public Vector3 position;
        public Vector4 parameters, data, falloff, density;
        public double End => playback.ContentEndDspTime + range / speed + glow;
    }
    readonly Voice[] voices = new Voice[Capacity];
    readonly Vector4[] origins = new Vector4[Capacity], parameters = new Vector4[Capacity], levels = new Vector4[Capacity],
        data = new Vector4[Capacity], falloffs = new Vector4[Capacity], densities = new Vector4[Capacity];
    readonly float[] pixels = new float[AudioReactiveClip.TextureWidth];
    Texture2DArray analysis, blockers;
    Texture2D upload;
    SonarPulseField sampler;
    public int ActiveSourceCount { get; private set; }
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
    public bool Emit(Vector3 position, AudioReactiveClip clip, AudioReactiveVisualSettings settings, ImpactSoundPool pool, float volume, float strength, bool looping, out ImpactSoundPool.Playback playback)
    {
        playback = null;
        if (!Application.isPlaying || !isActiveAndEnabled || clip == null || !clip.IsReady || settings == null
            || pool == null || !pool.isActiveAndEnabled || pool.clip != clip.clip || strength <= 0) return false;
        using var scope = triggerMarker.Auto();
        if (sampler == null || !sampler.isActiveAndEnabled) sampler = FindAnyObjectByType<SonarPulseField>();
        if (sampler == null || !sampler.isActiveAndEnabled || !pool.Preload(looping)) return false;
        EnsureResources(); double now = AudioSettings.dspTime;
        int slot = -1; double earliest = double.PositiveInfinity;
        for (int i = 0; i < Capacity; i++)
        {
            if (voices[i] == null || voices[i].End <= now) { slot = i; break; }
            if (voices[i].End < earliest) { earliest = voices[i].End; slot = i; }
        }
        if (slot < 0) return false; // All slots contain continuous sources: never silently hide a running radio.
        float range = Mathf.Max(1, settings.range), speed = Mathf.Max(.1f, settings.waveSpeed);
        Vector4 falloff = settings.distance != null ? settings.distance.Pack(range) : new Vector4(0, range, 0, 1);
        Vector4 density = settings.density != null ? settings.density.Pack(range) : Vector4.zero;
        float effective = falloff.z > 0 ? Mathf.Min(range, falloff.y) : range;
        if (density.w > .5f) effective = Mathf.Min(effective, density.y);
        if (!sampler.CaptureBlockingPlanes(position, effective, blockers, slot * 6, settings.highQualityOcclusion)) return false;
        OcclusionCaptures++; LastRayCount = settings.highQualityOcclusion ? 13824 : 3456;
        clip.PackTexture(pixels); upload.SetPixelData(pixels, 0); upload.Apply(false);
        Graphics.CopyTexture(upload, 0, 0, analysis, slot, 0);
        bool played = looping ? pool.PlayLoopAt(position, volume, out playback) : pool.PlayAt(position, volume, out playback);
        if (!played) { voices[slot] = null; Refresh(); return false; }
        if (voices[slot] != null && voices[slot].End > now) Replacements++;
        falloff.w *= Mathf.Max(0, strength);
        voices[slot] = new Voice {
            playback = playback, settings = settings, position = position, range = range, speed = speed, duration = clip.Duration,
            glow = Mathf.Clamp(settings.afterglow, 0, 2),
            parameters = new Vector4(range, speed, Mathf.Clamp(settings.afterglow, 0, 2), 1),
            data = new Vector4(clip.FrameCount, clip.FrameRate, clip.Duration, looping ? clip.Duration : 0), falloff = falloff, density = density
        };
        Emissions++; Refresh(); return true;
    }
    void LateUpdate() => Refresh();
    public void Refresh()
    {
        if (!isActiveAndEnabled || instance != this) return;
        using var scope = updateMarker.Auto();
        double now = AudioSettings.dspTime; int active = 0;
        for (int i = 0; i < Capacity; i++)
        {
            var voice = voices[i];
            if (voice == null || now > voice.End) { voices[i] = null; parameters[i] = Vector4.zero; continue; }
            active++; var p = voice.position;
            origins[i] = new Vector4(p.x, p.y, p.z, (float)(now - voice.playback.ContentDspTime));
            parameters[i] = voice.parameters; levels[i] = voice.settings.PackPeakLevels(); data[i] = voice.data;
            data[i].z = voice.data.w > 0 ? Mathf.Min(1e20f, (float)(voice.playback.ContentEndDspTime - voice.playback.ContentDspTime))
                : Mathf.Min(voice.duration, (float)(voice.playback.ContentEndDspTime - voice.playback.ContentDspTime));
            falloffs[i] = voice.falloff; densities[i] = voice.density;
        }
        ActiveSourceCount = active;
        Shader.SetGlobalFloat("_AudioSonarActive", active > 0 ? 1 : 0);
        if (active == 0) return;
        Shader.SetGlobalVectorArray("_AudioSonarOrigins", origins);
        Shader.SetGlobalVectorArray("_AudioSonarParameters", parameters);
        Shader.SetGlobalVectorArray("_AudioSonarLevels", levels);
        Shader.SetGlobalVectorArray("_AudioSonarData", data);
        Shader.SetGlobalVectorArray("_AudioSonarFalloffs", falloffs);
        Shader.SetGlobalVectorArray("_AudioSonarDensities", densities);
    }
    public void ClearVisuals()
    {
        for (int i = 0; i < Capacity; i++) voices[i] = null;
        ActiveSourceCount = 0;
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
