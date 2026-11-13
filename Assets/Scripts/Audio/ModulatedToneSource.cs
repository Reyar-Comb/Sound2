using System;
using UnityEngine;

/// <summary>A reusable mono oscillator. Combine with SpatialAudioEmitter for a world-space tone.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public sealed class ModulatedToneSource : MonoBehaviour
{
    [Range(20, 2000)] public float carrierHz = 110;
    [Range(0, 20)] public float lfoHz = .6f;
    [Range(0, 1)] public float lfoDepth = .4f;
    [Range(0, 1)] public float amplitude = .28f;
    [Range(0, 1)] public float secondHarmonic = .15f;
    public bool playOnStart = true;
    AudioSource source;
    AudioClip clip;
    int sampleRate;
    double phase, lfoPhase;
    double smoothCarrier, smoothLfo, smoothDepth;
    volatile float requestedCarrier, requestedLfo, requestedDepth, requestedAmplitude, requestedHarmonic;

    void Start() { if (playOnStart) Play(); }
    void Update() => PublishParameters();

    public void Play()
    {
        if (!isActiveAndEnabled) return;
        PublishParameters();
        if (source == null) source = GetComponent<AudioSource>();
        if (clip == null)
        {
            sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate <= 0) sampleRate = 48000;
            smoothCarrier = requestedCarrier; smoothLfo = requestedLfo; smoothDepth = requestedDepth;
            clip = AudioClip.Create("Modulated mono tone", sampleRate, 1, sampleRate, true, FillAudio);
        }
        source.playOnAwake = false;
        source.clip = clip;
        source.loop = true;
        if (!source.isPlaying) source.Play();
    }

    public void Stop() { if (source != null) source.Stop(); }
    public void PublishParameters()
    {
        requestedCarrier = Mathf.Clamp(carrierHz, 20, 2000);
        requestedLfo = Mathf.Clamp(lfoHz, 0, 20);
        requestedDepth = Mathf.Clamp01(lfoDepth);
        requestedAmplitude = Mathf.Clamp01(amplitude);
        requestedHarmonic = Mathf.Clamp01(secondHarmonic);
    }

    void FillAudio(float[] data)
    {
        double carrier = requestedCarrier, lfo = requestedLfo, depth = requestedDepth;
        double gain = requestedAmplitude, harmonic = requestedHarmonic;
        const double tau = Math.PI * 2;
        for (int i = 0; i < data.Length; i++)
        {
            smoothCarrier += (carrier - smoothCarrier) * .001;
            smoothLfo += (lfo - smoothLfo) * .001;
            smoothDepth += (depth - smoothDepth) * .001;
            double envelope = 1 - smoothDepth * .5 + smoothDepth * .5 * Math.Sin(lfoPhase);
            data[i] = (float)(gain * (Math.Sin(phase) + harmonic * Math.Sin(phase * 2)) * envelope);
            phase += tau * smoothCarrier / sampleRate;
            lfoPhase += tau * smoothLfo / sampleRate;
            if (phase >= tau) phase -= tau;
            if (lfoPhase >= tau) lfoPhase -= tau;
        }
    }

    void OnEnable() { if (clip != null && playOnStart) Play(); }
    void OnDisable() => Stop();
    void OnDestroy()
    {
        Stop();
        if (source != null && source.clip == clip) source.clip = null;
        if (clip != null) Destroy(clip);
    }
}
