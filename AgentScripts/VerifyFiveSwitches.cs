using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

// Run in Play mode through Unity CLI. Results stay outside Assets.
public static class VerifyFiveSwitches
{
    [Serializable] public class Report
    {
        public bool complete;
        public List<string> checks = new List<string>();
        public float[] measuredPeaks = new float[3];
        public float[] expectedGains = new float[3];
    }
    static Report report;
    const string ResultPath = "/tmp/Sound2-FiveSwitches.json";
    static void Check(bool passed, string message)
    {
        report.checks.Add((passed ? "PASS: " : "FAIL: ") + message);
        System.IO.File.WriteAllText(ResultPath, JsonUtility.ToJson(report, true));
    }
    public static string Main()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        var p = UnityEngine.Object.FindAnyObjectByType<BouncePlaytest>();
        report = new Report();
        p.StartCoroutine(Run(p));
        return "Started gameplay verification. Report: " + ResultPath;
    }
    static void Reset(BouncePlaytest p)
    {
        p.ClearBalls();
        p.ringShot = p.sandShot = p.flightSine = false;
        p.bounceCountOnly = true;
        p.maxBounces = 1;
        p.gravity = false;
    }
    static IEnumerator Run(BouncePlaytest p)
    {
        bool originalHum = p.hallHum != null && p.hallHum.humEnabled;
        if (p.hallHum != null) p.hallHum.humEnabled = false;
        float originalLifetime = p.lifetime;
        bool originalSpatial = p.spatialize;
        bool originalOcclusion = p.simulateOcclusion;
        bool originalReverb = p.reverb;
        var walker = p.GetComponent<CharacterController>();
        walker.enabled = false;
        p.transform.position = new Vector3(0, 1.6f, 0);
        p.transform.rotation = Quaternion.Euler(0, 90, 0);
        walker.enabled = true;
        Reset(p);
        p.lifetime = 0.08f;
        yield return new WaitForSeconds(0.3f);
        int impacts = p.Impacts;
        p.Fire();
        yield return new WaitForSeconds(0.2f);
        Check(p.ActiveBalls == 1, "Count-only ball survives the 0.08s lifetime");
        yield return new WaitForSeconds(1.1f);
        Check(p.ActiveBalls == 0 && p.Impacts - impacts == 1, "Count-only limit=1: exactly one wall collision then disappears");

        Reset(p); p.maxBounces = 2; impacts = p.Impacts;
        p.Fire();
        yield return new WaitForSeconds(1.15f);
        Check(p.ActiveBalls == 1 && p.Impacts - impacts == 1, "Limit=2: ball remains after first collision");
        yield return new WaitForSeconds(1.8f);
        Check(p.ActiveBalls == 0 && p.Impacts - impacts == 2, "Limit=2: disappears on second collision");

        Reset(p); p.bounceCountOnly = false; impacts = p.Impacts;
        p.Fire();
        yield return new WaitForSeconds(0.3f);
        Check(p.ActiveBalls == 0 && p.Impacts == impacts, "Turning count-only off restores lifetime expiration");

        Reset(p); p.SetSwitch(5, true); p.SetSwitch(1, true);
        Check(p.ringShot && !p.sandShot, "Enabling ring disables sand fan");
        impacts = p.Impacts; int sounds = p.Sounds;
        p.Fire();
        yield return new WaitForSeconds(0.04f);
        var ring = UnityEngine.Object.FindObjectsByType<BounceProbe>();
        var headings = new HashSet<int>();
        bool horizontal = true;
        foreach (var probe in ring)
        {
            var velocity = probe.GetComponent<Rigidbody>().linearVelocity;
            headings.Add(Mathf.RoundToInt(Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg));
            horizontal &= Mathf.Abs(velocity.y) < 0.01f;
        }
        Check(ring.Length == 24 && headings.Count == 24 && horizontal, "Ring launches 24 distinct horizontal directions");
        yield return new WaitForSeconds(1.3f);
        Check(p.ActiveBalls == 0 && p.Impacts - impacts == 24 && p.Sounds - sounds == 24, "All 24 ring balls hit room walls once and trigger 24 sounds");

        Reset(p); p.SetSwitch(1, true); p.SetSwitch(5, true);
        Check(p.sandShot && !p.ringShot, "Enabling sand fan disables ring");
        impacts = p.Impacts;
        p.Fire();
        yield return new WaitForSeconds(0.04f);
        var sand = UnityEngine.Object.FindObjectsByType<BounceProbe>();
        bool inFan = true;
        foreach (var probe in sand)
        {
            var v = Quaternion.Inverse(p.transform.rotation) * probe.GetComponent<Rigidbody>().linearVelocity.normalized;
            inFan &= v.z > 0 && Mathf.Abs(Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg) <= p.fanAngle / 2 + 0.1f;
        }
        Check(sand.Length == 32 && inFan, "Sand launches 32 small balls inside the forward fan");
        yield return new WaitForSeconds(1.3f);
        Check(p.ActiveBalls == 0 && p.Impacts - impacts == 32, "All sand balls ignore each other and terminate on room geometry");

        Reset(p); p.bounceCountOnly = false; p.flightSine = true; impacts = p.Impacts;
        p.Fire();
        yield return new WaitForSeconds(0.2f);
        var tone = UnityEngine.Object.FindAnyObjectByType<BounceProbe>();
        bool playing = tone != null && tone.flightVoice != null && tone.flightVoice.isPlaying;
        bool sineData = false;
        if (playing)
        {
            var samples = new float[48000]; tone.flightVoice.clip.GetData(samples, 0);
            int crossings = 0;
            for (int i = 1; i < samples.Length; i++) if (samples[i - 1] <= 0 && samples[i] > 0) crossings++;
            sineData = Math.Abs(crossings - 440) <= 1;
        }
        Check(playing && sineData, "Flight audio loops a 440Hz sine and ignores lifetime");
        yield return new WaitForSeconds(1.1f);
        Check(p.ActiveBalls == 0 && UnityEngine.Object.FindAnyObjectByType<BounceProbe>() == null && p.Impacts - impacts == 1,
            "Flight mode removes the ball and its tone after the first collision");

        Reset(p); p.ringShot = true; p.flightSine = true; impacts = p.Impacts;
        p.Fire(); yield return new WaitForSeconds(0.2f);
        Check(p.ActiveBalls == 24, "Ring and continuous flight mode work together");
        yield return new WaitForSeconds(1.2f);
        Check(p.ActiveBalls == 0 && p.Impacts - impacts == 24, "All 24 flight voices are cleaned up after first impact");

        Reset(p); p.enhancedAttenuation = true;
        report.expectedGains = new[] { p.ImpactDistanceGain(1), p.ImpactDistanceGain(6), p.ImpactDistanceGain(12) };
        Check(p.ImpactDistanceGain(1) / p.ImpactDistanceGain(6) > 4 && p.ImpactDistanceGain(6) / p.ImpactDistanceGain(12) > 4,
            "Impact curve has large contrast over both 1-6m and 6-12m");
        p.SetReverb(false); p.SetSpatialization(false); p.simulateOcclusion = false;
        var playImpact = typeof(BouncePlaytest).GetMethod("PlayImpact", BindingFlags.NonPublic | BindingFlags.Instance);
        var buffer = new float[1024];
        float[] distances = { 1, 6, 12 };
        for (int i = 0; i < distances.Length; i++)
        {
            p.ClearBalls(); yield return new WaitForSeconds(0.3f);
            playImpact.Invoke(p, new object[] { p.transform.position + Vector3.forward * distances[i], 0.7f });
            float end = Time.time + 0.45f;
            while (Time.time < end)
            {
                AudioListener.GetOutputData(buffer, 0);
                foreach (float value in buffer) report.measuredPeaks[i] = Mathf.Max(report.measuredPeaks[i], Mathf.Abs(value));
                yield return null;
            }
        }
        Check(report.measuredPeaks[0] > report.measuredPeaks[1] * 4 && report.measuredPeaks[1] > report.measuredPeaks[2] * 4 && report.measuredPeaks[2] > 0,
            "Actual listener output confirms strong attenuation at 1m, 6m, and 12m");
        p.enhancedAttenuation = false;
        Check(Mathf.Abs(p.ImpactDistanceGain(6) - 1f / 6) < 0.0001f, "Switch 3 off restores inverse-distance mode");
        Reset(p); p.lifetime = originalLifetime; p.enhancedAttenuation = true;
        p.simulateOcclusion = originalOcclusion;
        p.SetReverb(originalReverb); p.SetSpatialization(originalSpatial);
        if (p.hallHum != null) p.hallHum.humEnabled = originalHum;
        report.complete = true;
        System.IO.File.WriteAllText(ResultPath, JsonUtility.ToJson(report, true));
        Debug.Log("Five switches verification complete: " + ResultPath);
    }
}
