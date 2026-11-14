using System;
using UnityEngine;

/// <summary>Editor-baked, absolute-level analysis of a dry clip. Peak envelope only: no FFT, bands, RMS or runtime PCM reads.</summary>
[CreateAssetMenu(menuName = "SONICSPACE/Audio Reactive Clip")]
public sealed class AudioReactiveClip : ScriptableObject
{
    public const int Channels = 1, TextureWidth = 4096;
    public const float NoiseFloorDb = -60;
    public AudioClip clip;
    [Range(10, 120)] public int analysisRate = 60;
    [SerializeField, HideInInspector] byte[] data = Array.Empty<byte>();
    [SerializeField, HideInInspector] int frameCount, bakedRate, bakedSamples, bakedFrequency, bakedChannels, bakedRequestedRate;
    [SerializeField, HideInInspector] string sourceHash;
    public int FrameCount => frameCount;
    public int FrameRate => bakedRate;
    public int ByteCount => data != null ? data.Length : 0;
    public string SourceHash => sourceHash;
    public float Duration => bakedFrequency > 0 ? (float)bakedSamples / bakedFrequency : 0;
    public bool IsReady => clip != null && frameCount > 1 && frameCount <= TextureWidth
        && data != null && data.Length == frameCount * Channels && bakedSamples == clip.samples
        && bakedFrequency == clip.frequency && bakedChannels == clip.channels
        && bakedRequestedRate == analysisRate;

    public void SetAnalysis(byte[] bytes, int frames, string hash, int actualRate = 0)
    {
        if (clip == null || bytes == null || frames < 2 || frames > TextureWidth || bytes.Length != frames * Channels)
            throw new ArgumentException("Invalid audio analysis payload.");
        data = bytes; frameCount = frames; bakedRate = actualRate > 0 ? actualRate : analysisRate; bakedRequestedRate = analysisRate;
        bakedSamples = clip.samples; bakedFrequency = clip.frequency; bakedChannels = clip.channels; sourceHash = hash;
    }

    public static byte Quantize(float amplitude)
    {
        if (amplitude <= .001f) return 0;
        float db = 20 * Mathf.Log10(Mathf.Min(1, amplitude));
        return (byte)Mathf.Clamp(Mathf.RoundToInt(1 + 254 * (db - NoiseFloorDb) / -NoiseFloorDb), 1, 255);
    }
    public static float Decode(byte value) => value == 0 ? 0 : Mathf.Pow(10, (NoiseFloorDb + (value - 1) / 254f * -NoiseFloorDb) / 20);
    public float Evaluate(float time, int channel)
    {
        if (!IsReady || time < 0 || time >= Duration || channel < 0 || channel >= Channels) return 0;
        float frame = Mathf.Min(time * bakedRate, frameCount - 1);
        int a = Mathf.FloorToInt(frame), b = Mathf.Min(a + 1, frameCount - 1);
        return Mathf.Lerp(Decode(data[a * Channels + channel]), Decode(data[b * Channels + channel]), frame - a);
    }
    // Migrate previous ten-channel experimental assets without losing a user's selected recording.
    void OnEnable()
    {
        if (data == null || frameCount < 2 || data.Length != frameCount * 10) return;
        var peaks = new byte[frameCount];
        for (int f = 0; f < frameCount; f++) peaks[f] = data[f * 10 + 9];
        data = peaks;
    }
    public void PackTexture(float[] pixels)
    {
        if (!IsReady || pixels.Length != TextureWidth) throw new ArgumentException("Unbaked analysis or wrong texture size.");
        Array.Clear(pixels, 0, pixels.Length);
        // Decode once on upload, not millions of times inside the shader.
        for (int f = 0; f < frameCount; f++) pixels[f] = Decode(data[f]);
    }
}
