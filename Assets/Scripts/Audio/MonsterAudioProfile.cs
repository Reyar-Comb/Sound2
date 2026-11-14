using System;
using UnityEngine;

[CreateAssetMenu(menuName = "SONICSPACE/Monster Audio Profile")]
public sealed class MonsterAudioProfile : ScriptableObject
{
    [Serializable]
    public sealed class Layer
    {
        public AudioClip clip;
        [Range(0, 1)] public float volume;
        [Min(0), Tooltip("Full gain up to this distance (metres).")]
        public float fullDistance;
        [Min(.1f), Tooltip("Fades to silence at this distance (metres).")]
        public float range;

        public Layer(float volume, float fullDistance, float range)
        { this.volume = volume; this.fullDistance = fullDistance; this.range = range; }

        public float GainAt(float distance)
        {
            float end = Mathf.Max(.1f, range);
            float start = Mathf.Clamp(fullDistance, 0, end - .01f);
            return 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, end, distance));
        }
    }

    public Layer crawl = new Layer(.35f, 1.5f, 12);
    public Layer hum = new Layer(.42f, 1.2f, 7);
    public Layer shout = new Layer(.28f, .65f, 2.5f);
    [Min(.01f)] public float movementThreshold = .12f;
    [Min(.01f)] public float fadeInSeconds = .08f;
    [Min(.01f)] public float fadeOutSeconds = .20f;
    public Vector3 localOrigin = new Vector3(0, .8f, 0);
    public bool hrtf = true;
    public bool occlusion;
    public bool reflections;
    [Range(0, 2)] public float reflectionMix = .25f;
    public bool replaceScanSound = true;
    public bool HasClips => (crawl?.clip != null) || (hum?.clip != null) || (shout?.clip != null);
}
