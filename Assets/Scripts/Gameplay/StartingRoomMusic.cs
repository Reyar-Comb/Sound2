using System;
using UnityEngine;

/// <summary>Persistent, non-spatial room score; independent sources crossfade without restarting the outgoing clip.</summary>
[Serializable]
public sealed class StartingRoomMusic
{
    [Header("初始房间 BGM（A、B 循环，C 单次）")]
    [Tooltip("初次进入游戏、普通结局返回房间时播放。循环。")]
    public AudioClip versionA;
    [Tooltip("Boss 胜利返回房间、世界牌结局返回房间时播放。循环。")]
    public AudioClip versionB;
    [Tooltip("战后走出房门后单次播放。片尾字幕演出结束时统一淡出，不循环。")]
    public AudioClip versionC;
    [Range(0, 1)] public float volumeA = .7f;
    [Range(0, 1)] public float volumeB = .7f;
    [Range(0, 1)] public float volumeC = .7f;
    [Min(0), Tooltip("进入房间时 BGM 从无声淡入的秒数。")]
    public float fadeInSeconds = 2;
    [Min(0), Tooltip("B 到 C 的交叉淡化秒数。C 从开头播放，B 同时淡出。")]
    public float crossfadeSeconds = 2;
    [Min(0), Tooltip("离开房间、出现塔罗牌时的淡出秒数。")]
    public float fadeOutSeconds = 1;

    [Header("第四结局环境音（开门时开始）")]
    public AudioClip endingAmbience;
    public bool loopAmbience = true;
    [Range(0, 1)] public float ambienceVolume = .5f;
    [Min(0)] public float ambienceFadeInSeconds = 2;
    [Min(0)] public float ambienceFadeOutSeconds = 1;

    [NonSerialized] AudioSource[] tracks;
    [NonSerialized] AudioSource ambience;
    [NonSerialized] float[] starts, targets;
    [NonSerialized] float transitionTime, transitionDuration, ambienceStart, ambienceTarget, ambienceTime, ambienceDuration;
    [NonSerialized] int activeTrack;
    [NonSerialized] bool fadingOut;

    internal System.Collections.Generic.IEnumerable<AudioSource> Sources
    {
        get
        {
            if (tracks != null) foreach (var track in tracks) yield return track;
            if (ambience != null) yield return ambience;
        }
    }

    internal void Initialize(Transform owner)
    {
        if (tracks != null) return;
        tracks = new[] { MakeSource(owner, "Starting room BGM 1"), MakeSource(owner, "Starting room BGM 2") };
        ambience = MakeSource(owner, "World ending ambience");
        starts = new float[2]; targets = new float[2];
    }

    static AudioSource MakeSource(Transform owner, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(owner, false);
        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false; source.spatialBlend = 0;
        source.bypassReverbZones = true; source.volume = 0;
        return source;
    }

    internal void PlayRoom(bool victory)
    {
        FadeAmbience(0, ambienceFadeOutSeconds);
        Switch(victory ? versionB : versionA, victory ? volumeB : volumeA, true, fadeInSeconds);
    }

    internal void PlayFinale()
    {
        Switch(versionC, volumeC, false, crossfadeSeconds);
    }

    internal void BeginEndingAmbience()
    {
        if (ambience == null || endingAmbience == null || ambience.isPlaying) return;
        ambience.Stop(); ambience.clip = endingAmbience; ambience.loop = loopAmbience; ambience.volume = 0;
        ambience.Play();
        FadeAmbience(ambienceVolume, ambienceFadeInSeconds);
    }

    internal bool FinalePlaying => tracks != null && tracks[activeTrack].isPlaying;

    void Switch(AudioClip clip, float volume, bool loop, float seconds)
    {
        if (tracks == null) return;
        int incoming = 1 - activeTrack;
        tracks[incoming].Stop(); tracks[incoming].clip = clip;
        tracks[incoming].loop = loop; tracks[incoming].volume = 0;
        if (clip != null) tracks[incoming].Play();
        activeTrack = incoming;
        fadingOut = false;
        for (int i = 0; i < tracks.Length; i++)
        { starts[i] = tracks[i].volume; targets[i] = i == activeTrack && clip != null ? volume : 0; }
        transitionTime = 0; transitionDuration = seconds;
    }

    internal void FadeOut()
        => FadeOut(fadeOutSeconds, ambienceFadeOutSeconds);

    internal void FadeOut(float seconds, float ambienceSeconds)
    {
        if (tracks == null) return;
        fadingOut = true;
        for (int i = 0; i < tracks.Length; i++) { starts[i] = tracks[i].volume; targets[i] = 0; }
        transitionTime = 0; transitionDuration = seconds;
        FadeAmbience(0, ambienceSeconds);
    }

    void FadeAmbience(float volume, float seconds)
    {
        if (ambience == null) return;
        ambienceStart = ambience.volume; ambienceTarget = volume; ambienceTime = 0; ambienceDuration = seconds;
    }

    internal void Tick(float deltaTime)
    {
        if (tracks == null || AudioListener.pause) return;
        transitionTime += deltaTime; ambienceTime += deltaTime;
        float t = transitionDuration <= 0 ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(transitionTime / transitionDuration));
        for (int i = 0; i < tracks.Length; i++)
        {
            tracks[i].volume = Mathf.Lerp(starts[i], targets[i], t);
            if (t >= 1 && targets[i] == 0 && (i != activeTrack || fadingOut)) tracks[i].Stop();
        }
        float a = ambienceDuration <= 0 ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(ambienceTime / ambienceDuration));
        ambience.volume = Mathf.Lerp(ambienceStart, ambienceTarget, a);
        if (a >= 1 && ambienceTarget == 0) ambience.Stop();
    }

    internal void Stop()
    {
        if (tracks == null) return;
        foreach (var track in tracks) if (track != null) track.Stop();
        if (ambience != null) ambience.Stop();
    }
}
