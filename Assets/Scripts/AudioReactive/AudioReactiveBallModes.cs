using UnityEngine;
using UnityEngine.InputSystem;

public enum AudioReactiveBallMode { ManualWave, AudioDriven }

/// <summary>Scene-local A/B switch. Physics/pickup stay unchanged; exactly one collision-wave path is enabled.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(CollisionSonarEmitter), typeof(AudioReactiveSonarEmitter))]
public sealed class AudioReactiveBallModes : MonoBehaviour
{
    public AudioReactiveBallMode mode;
    public Key switchKey = Key.B;
    CollisionSonarEmitter legacy;
    AudioReactiveSonarEmitter audio;
    AudioReactiveBallMode applied;
    bool initialized;
    void Awake() { legacy = GetComponent<CollisionSonarEmitter>(); audio = GetComponent<AudioReactiveSonarEmitter>(); ApplyMode(); }
    void Start() => ApplyMode();
    void Update()
    {
        if (!SonicSpaceStartMenu.BlocksGameplay && Keyboard.current != null && switchKey != Key.None && Keyboard.current[switchKey].wasPressedThisFrame)
            mode = mode == AudioReactiveBallMode.ManualWave ? AudioReactiveBallMode.AudioDriven : AudioReactiveBallMode.ManualWave;
        if (!initialized || applied != mode) ApplyMode();
    }
    public void ApplyMode()
    {
        if (legacy == null) legacy = GetComponent<CollisionSonarEmitter>();
        if (audio == null) audio = GetComponent<AudioReactiveSonarEmitter>();
        bool driven = mode == AudioReactiveBallMode.AudioDriven;
        legacy.enabled = !driven; audio.triggerOnCollision = true; audio.loop = false; audio.playOnStart = false;
        audio.AudioPool.enabled = driven; audio.enabled = driven;
        // With a selected recording both modes use that same dry clip; only visual generation differs.
        var cue = GetComponent<SonarCueSource>();
        if (cue != null && audio.analysis != null && audio.analysis.clip != null) cue.clip = audio.analysis.clip;
        initialized = true; applied = mode;
    }
}
