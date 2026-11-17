using UnityEngine;

/// <summary>Recorded emission cue, independent of sonar visibility. An empty clip is silent.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public sealed class SonarCueSource : MonoBehaviour
{
    public AudioSource output;
    public AudioClip clip;

    public void Play()
    {
        if (!isActiveAndEnabled || clip == null) return;
        if (output == null) output = GetComponent<AudioSource>();
        var spatial = output.GetComponent<SpatialAudioEmitter>();
        if (spatial != null && spatial.Source == output) spatial.PlayOneShot(clip);
        else output.PlayOneShot(clip);
    }

}
