using UnityEngine;

// Two alternating sources keep the intro-to-loop transition on the DSP clock.
internal sealed class BossMusic
{
    readonly AudioSource[] sources;
    double start, nextLoop, loopDuration;
    int loopSample, nextSource;
    bool playing;

    public double Elapsed => AudioSettings.dspTime - start;

    public BossMusic(GameObject owner)
    {
        sources = new[] { owner.AddComponent<AudioSource>(), owner.AddComponent<AudioSource>() };
        foreach (var source in sources)
        {
            source.playOnAwake = false;
            source.spatialBlend = 0;
        }
    }

    public bool Play(AudioClip clip, float volume, float loopStart, float loopEnd)
    {
        Stop();
        if (clip == null || clip.samples <= 1 || clip.frequency <= 0) return false;
        int end = loopEnd > 0 ? Mathf.Clamp(Mathf.RoundToInt(loopEnd * clip.frequency), 2, clip.samples) : clip.samples;
        loopSample = Mathf.Clamp(Mathf.RoundToInt(loopStart * clip.frequency), 0, end - 1);
        // An invalid or extremely short loop falls back to the full configured track.
        if ((double)(end - loopSample) / clip.frequency < .1) loopSample = 0;
        loopDuration = (double)(end - loopSample) / clip.frequency;
        if (loopDuration < .1) return false;
        foreach (var source in sources)
        {
            source.clip = clip;
            source.volume = volume;
        }
        start = AudioSettings.dspTime + .15;
        nextLoop = start + (double)end / clip.frequency;
        Schedule(sources[0], 0, start, nextLoop);
        nextSource = 1;
        playing = true;
        return true;
    }

    public void Tick()
    {
        if (!playing) return;
        double now = AudioSettings.dspTime;
        if (nextLoop - now > System.Math.Min(.5, loopDuration * .5)) return;
        // If a long frame missed the scheduling window, resume on the next available DSP boundary.
        double begin = System.Math.Max(nextLoop, now + .02);
        nextLoop = begin + loopDuration;
        Schedule(sources[nextSource], loopSample, begin, nextLoop);
        nextSource = 1 - nextSource;
    }

    static void Schedule(AudioSource source, int sample, double from, double to)
    {
        source.timeSamples = sample;
        source.PlayScheduled(from);
        source.SetScheduledEndTime(to);
    }

    public void Stop()
    {
        playing = false;
        foreach (var source in sources) source.Stop();
    }
}
