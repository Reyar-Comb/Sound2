using UnityEngine;

/// <summary>Short emission cue, independent of sonar visibility. Accepts a recorded clip or generates the original ping.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public sealed class SonarCueSource : MonoBehaviour
{
    public AudioSource output;
    public AudioClip clip;
    [Tooltip("Use the original sonar ping when no clip is assigned. Disable for recording-only props.")]
    public bool generateDefaultPing = true;
    AudioClip generatedClip;

    public void Play()
    {
        if (!isActiveAndEnabled) return;
        if (clip == null && !generateDefaultPing) return;
        if (output == null) output = GetComponent<AudioSource>();
        if (clip == null && generatedClip == null) generatedClip = ProceduralAudioClips.CreateSonarPing();
        var spatial = output.GetComponent<SpatialAudioEmitter>();
        if (spatial != null && spatial.Source == output) spatial.PlayOneShot(clip != null ? clip : generatedClip);
        else output.PlayOneShot(clip != null ? clip : generatedClip);
    }

    void OnDestroy() { if (generatedClip != null) Destroy(generatedClip); }
}
