using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Unity CLI: script run --file AgentScripts/VerifyAudioComponents.cs, in SonarHall Play mode.
public static class VerifyAudioComponents
{
    [Serializable] public class Report
    {
        public bool complete;
        public List<string> checks = new List<string>();
    }
    static Report report;
    const string ReportPath = "/tmp/Sound2-AudioComponents.json";
    static void Check(bool passed, string message)
    {
        report.checks.Add((passed ? "PASS: " : "FAIL: ") + message);
        System.IO.File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    }
    public static string Main()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play mode.");
        report = new Report();
        UnityEngine.Object.FindAnyObjectByType<SphericalSonar>().StartCoroutine(Run());
        return ReportPath;
    }
    static IEnumerator Run()
    {
        var player = UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();
        var cube = UnityEngine.Object.FindAnyObjectByType<SonarObjectEmitter>();
        bool autoPlayer = player.automatic, autoCube = cube.automatic;
        player.automatic = cube.automatic = false;
        var field = player.PulseField;
        int before = field.Emissions;
        cube.EmitNow();
        Check(field.Emissions == before + 1 && cube.pulseField == field && field.LastOrigin == cube.transform.position,
            "Object emitter shares the extracted pulse field and emits from its own world position");
        before = field.Emissions;
        field.EmitFrom(player.transform.position + Vector3.forward, 12, 7, .7f);
        Check(field.Emissions == before + 1 && field.LastHitCount > 0,
            "Pulse field accepts a visual-only emission without an AudioSource or player input");

        var go = new GameObject("Standalone component verification");
        go.transform.position = player.transform.position + Vector3.right * 6;
        var emitter = go.AddComponent<SpatialAudioEmitter>();
        emitter.listener = player.transform;
        emitter.volume = .8f;
        emitter.settings.distanceMode = SoundDistanceMode.Curve;
        emitter.settings.distanceCurve = AnimationCurve.Constant(0, 20, .25f);
        emitter.Apply();
        Check(Mathf.Abs(emitter.Source.volume - .2f) < .0001f && !emitter.SteamSource.distanceAttenuation,
            "Custom gain is applied exactly once to an independent spatial source");
        emitter.settings.distanceMode = SoundDistanceMode.Physics;
        emitter.Apply();
        Check(Mathf.Abs(emitter.Source.volume - .8f) < .0001f && emitter.SteamSource.distanceAttenuation,
            "Physics mode delegates attenuation to Steam Audio without multiplying source volume");
        emitter.settings.distanceMode = SoundDistanceMode.Linear;
        emitter.settings.minDistance = 1; emitter.settings.maxDistance = 16;
        emitter.Apply();
        Check(Mathf.Abs(emitter.Source.volume - .8f * (1 - 5f / 15)) < .0001f,
            "Linear source gain preserves the cube's original 1–16 metre rolloff");
        var tone = go.AddComponent<ModulatedToneSource>();
        tone.carrierHz = 220; tone.lfoDepth = 0; tone.secondHarmonic = 0;
        tone.Play();
        yield return new WaitForSeconds(.25f);
        var buffer = new float[1024];
        AudioListener.GetOutputData(buffer, 0);
        float peak = 0; foreach (float v in buffer) peak = Mathf.Max(peak, Mathf.Abs(v));
        Check(emitter.Source.isPlaying && emitter.Source.clip.channels == 1 && peak > .00001f,
            "Standalone oscillator and spatial emitter produce a real mono audio signal");
        tone.enabled = false;
        Check(!emitter.Source.isPlaying, "Disabling an oscillator stops its owned playback");
        tone.enabled = true;
        yield return null;
        Check(emitter.Source.isPlaying, "Re-enabling an oscillator resumes its tone");

        var poolGo = new GameObject("Standalone impact pool verification");
        var pool = poolGo.AddComponent<ImpactSoundPool>();
        var clip = ProceduralAudioClips.CreateSonarPing();
        pool.capacity = 2; pool.clip = clip; pool.tailSeconds = .2f; pool.listener = player.transform;
        pool.settings.distanceMode = SoundDistanceMode.Curve;
        pool.settings.distanceCurve = AnimationCurve.Constant(0, 20, .25f);
        pool.settings.reflections = false;
        var a = player.transform.position + Vector3.left * 3;
        var b = player.transform.position + Vector3.forward * 4;
        pool.PlayAt(a, .8f); pool.PlayAt(b, .4f);
        var voices = UnityEngine.Object.FindObjectsByType<SpatialAudioEmitter>();
        SpatialAudioEmitter first = null, second = null;
        foreach (var v in voices)
        {
            if (v == emitter) continue;
            if (v.transform.position == a) first = v;
            if (v.transform.position == b) second = v;
        }
        Check(pool.VoiceCount == 2 && pool.Sounds == 2 && first != null && second != null && first != second,
            "Back-to-back scheduled impacts reserve distinct reusable voices");
        Check(first != null && second != null && Mathf.Abs(first.Source.volume - .2f) < .0001f
            && Mathf.Abs(second.Source.volume - .1f) < .0001f && first.Source.clip.length > clip.length + .2f,
            "Pool applies per-event gain and preserves pre-roll plus reflection-tail silence");
        pool.enabled = false;
        yield return new WaitForSeconds(.1f);
        Check(first != null && second != null && !first.Source.isPlaying && !second.Source.isPlaying,
            "Disabling a pool cancels active and scheduled playback");
        UnityEngine.Object.Destroy(poolGo);
        UnityEngine.Object.Destroy(go);
        UnityEngine.Object.Destroy(clip);
        yield return null;
        Check(first == null && second == null, "Destroying a pool cleans up its detached world-space voices");
        player.automatic = autoPlayer; cube.automatic = autoCube;
        report.complete = true;
        System.IO.File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    }
}
