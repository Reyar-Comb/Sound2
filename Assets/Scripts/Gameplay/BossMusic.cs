using UnityEngine;

// A single scheduled play provides the clock for the entire encounter.
internal sealed class BossMusic
{
    readonly AudioSource[] sources;
    double start;
    float playbackVolume;

    public double Elapsed => AudioSettings.dspTime - start;
    public double Duration { get; private set; }

    public BossMusic(GameObject owner)
    {
        var existing = owner.GetComponents<AudioSource>();
        sources = new AudioSource[2];
        for (int i = 0; i < sources.Length; i++)
            sources[i] = i < existing.Length ? existing[i] : owner.AddComponent<AudioSource>();
        foreach (var source in sources)
        {
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.bypassReverbZones = true;
            source.loop = false;
            source.pitch = 1;
        }
    }

    public bool Play(AudioClip clip, float volume, float startOffset = 0)
    {
        Stop();
        if (clip == null || clip.loadState != AudioDataLoadState.Loaded || clip.samples <= 1 || clip.frequency <= 0) return false;
        Duration = (double)clip.samples / clip.frequency;
        foreach (var source in sources)
        {
            source.clip = clip;
            source.volume = volume;
            source.loop = false;
            source.pitch = 1;
        }
        playbackVolume = volume;
        int firstSample = Mathf.Clamp(Mathf.RoundToInt(startOffset * clip.frequency), 0, clip.samples - 1);
        double playbackStart = AudioSettings.dspTime + .15;
        start = playbackStart - (double)firstSample / clip.frequency;
        sources[0].timeSamples = firstSample;
        sources[0].PlayScheduled(playbackStart);
        sources[0].SetScheduledEndTime(start + Duration);
        return true;
    }

    public void Stop()
    {
        foreach (var source in sources) source.Stop();
    }

    public void SetGain(float gain)
    {
        foreach (var source in sources) source.volume = playbackVolume * Mathf.Clamp01(gain);
    }
}
