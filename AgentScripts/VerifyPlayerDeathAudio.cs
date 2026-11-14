using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Run via Unity CLI script eval in Play Mode. Uses an isolated instance of the shared Player.
if (!Application.isPlaying) throw new InvalidOperationException("Requires Play Mode");
var checks = new List<string>();
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
async Task Until(Func<bool> predicate, string label)
{
    float timeout = Time.realtimeSinceStartup + 5;
    while (!predicate())
    {
        if (Time.realtimeSinceStartup > timeout) throw new Exception("Timed out: " + label);
        await Task.Delay(16);
    }
}

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
var actor = Object.Instantiate(prefab, new Vector3(6000, 30, 6000), Quaternion.identity);
float originalVolume = AudioListener.volume;
const float baseline = .73f;
try
{
    AudioListener.volume = baseline;
    actor.GetComponent<PlayerController>().enabled = false;
    foreach (var listener in actor.GetComponentsInChildren<AudioListener>()) listener.enabled = false;
    foreach (var camera in actor.GetComponentsInChildren<Camera>()) camera.enabled = false;
    var feedback = actor.GetComponent<PlayerDamageFeedback>();
    AudioSource Voice(AudioClip clip) => actor.GetComponents<AudioSource>().SingleOrDefault(s => s.clip == clip);
    Check(feedback.deathClip != null && feedback.deathClip.name == "death" && feedback.tinnitusClip != null
        && feedback.tinnitusClip.name == "ear", "Prefab binds death and ear recordings");
    Check(Voice(feedback.deathClip) == null && Voice(feedback.tinnitusClip) == null, "No audio starts before death");

    feedback.BeginDeath();
    var death = Voice(feedback.deathClip);
    Check(feedback.SequenceActive && death != null && death.isPlaying && !death.loop && !death.playOnAwake,
        "The default death recording starts with the red overlay and does not loop");
    Check(Voice(feedback.tinnitusClip) == null, "Ear recording does not start with the default death cue");
    Check(AudioListener.volume == baseline, "World sound stays unchanged before tinnitus");
    feedback.Clear();
    Check(!death.isPlaying, "Clearing the sequence stops the death recording");

    feedback.deathAudioDelay = .25f;
    feedback.tinnitusDelay = .5f;
    feedback.BeginDeath();
    Check(!death.isPlaying && Voice(feedback.tinnitusClip) == null, "Positive start offsets defer both playback requests");
    await Until(() => death.isPlaying, "delayed death audio");
    Check(Voice(feedback.tinnitusClip) == null, "Tinnitus does not start at the earlier death offset");
    await Until(() => Voice(feedback.tinnitusClip) is AudioSource s && s.isPlaying, "delayed tinnitus audio");
    var ear = Voice(feedback.tinnitusClip);
    Check(!ear.loop && !ear.playOnAwake && ear.spatialBlend == 0 && death.spatialBlend == 0 && ear != death,
        "Both recordings use independent 2D one-shot voices");
    Check(death.ignoreListenerVolume && ear.ignoreListenerVolume, "Death and ear cues bypass the temporary world fade");
    await Until(() => AudioListener.volume > 0 && AudioListener.volume < baseline, "intermediate world duck gain");
    await Until(() => AudioListener.volume == 0, "world fades to silence");
    Check(ear.isPlaying, "Ear cue continues when world sound reaches silence");
    feedback.deathVolume = .5f;
    feedback.tinnitusVolume = .4f;
    feedback.ShowWake(.5f);
    await Until(() => Mathf.Approximately(ear.volume, baseline * .2f) && Mathf.Approximately(death.volume, baseline * .5f), "live audio volumes");
    Check(feedback.WhiteAmount == .5f, "Tinnitus fades with waking while the death volume remains independent");
    Check(AudioListener.volume == 0, "World stays muted while the white screen is still visible");
    feedback.ShowWake(1);
    feedback.Clear();
    Check(!ear.isPlaying && !death.isPlaying, "Finishing the white-screen recovery stops both voices");
    await Until(() => AudioListener.volume > 0 && AudioListener.volume < baseline, "intermediate world restore gain");
    await Until(() => AudioListener.volume == baseline, "original world gain restored");
    Check(!feedback.SequenceActive, "World sound gradually returns only after the white screen is gone");

    // Simulate a short recording completing during a long recovery: no second request is made.
    feedback.deathAudioDelay = feedback.tinnitusDelay = 0;
    feedback.BeginDeath();
    Check(ear.isPlaying && death.isPlaying, "Each new death rearms both one-shot cues");
    ear.Stop(); death.Stop();
    await Until(() => AudioListener.volume == 0, "world remains muted after short cues finish");
    Check(feedback.SequenceActive && !ear.isPlaying && !death.isPlaying, "Stopped or finished recordings are not retriggered mid-sequence");
    feedback.enabled = false;
    Check(!ear.isPlaying && !death.isPlaying, "Disabling damage feedback stops both voices");
    Check(AudioListener.volume == baseline, "Disabling mid-death immediately restores the original gain");
    return new { passed = true, checks };
}
finally { actor.SetActive(false); Object.Destroy(actor); AudioListener.volume = originalVolume; }
