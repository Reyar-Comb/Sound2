using UnityEngine;

/// <summary>Generated clip factories. The caller owns and destroys the returned clip.</summary>
public static class ProceduralAudioClips
{
    public static AudioClip CreateSonarPing()
    {
        const int rate = 48000;
        var data = new float[5760];
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)rate;
            float envelope = Mathf.Min(t / .006f, 1) * Mathf.Pow(1 - i / (float)data.Length, 3);
            data[i] = Mathf.Sin(2 * Mathf.PI * (760 * t - 1800 * t * t)) * envelope;
        }
        var clip = AudioClip.Create("Sonar emission cue", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    public static AudioClip CreateLoopingSine(int frequency)
    {
        const int rate = 48000;
        frequency = Mathf.Clamp(frequency, 20, 2000);
        var samples = new float[rate];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = .3f * Mathf.Sin(2 * Mathf.PI * frequency * i / rate);
        var clip = AudioClip.Create("Flight sine " + frequency, rate, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
