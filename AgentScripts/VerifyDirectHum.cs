using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

public static class VerifyDirectHum
{
    [Serializable] public class Report
    {
        public bool complete;
        public List<string> checks = new List<string>();
        public float[] frontRms, rightRms, leftRms;
        public float[] distanceRms = new float[3];
    }
    static Report report;
    const string Path = "/tmp/Sound2-DirectHum.json";
    static void Check(bool okay, string text)
    {
        report.checks.Add((okay ? "PASS: " : "FAIL: ") + text);
        System.IO.File.WriteAllText(Path, JsonUtility.ToJson(report, true));
    }
    public static string Main()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play mode.");
        report = new Report();
        var p = UnityEngine.Object.FindAnyObjectByType<BouncePlaytest>();
        p.StartCoroutine(Run(p));
        return "Started direct hum listener measurements; report " + Path;
    }
    static IEnumerator Run(BouncePlaytest p)
    {
        var h = p.hallHum;
        var position = p.transform.position; var rotation = p.transform.rotation;
        bool occlusion = p.simulateOcclusion, spatial = p.spatialize;
        bool active = h.humEnabled; float depth = h.lfoDepth;
        float originalImpactGain = p.ImpactDistanceGain(6);
        var walker = p.GetComponent<CharacterController>(); bool walking = walker.enabled;
        p.ClearBalls(); p.enabled = false; walker.enabled = false;
        p.simulateOcclusion = false; p.spatialize = true; h.humEnabled = true; h.lfoDepth = 0;
        var steam = h.GetComponent<SteamAudio.SteamAudioSource>();
        Check(!steam.reflections && steam.reflectionsMixLevel == 0, "Hum reflections are off even while room reverb is on");
        Check(!steam.distanceAttenuation && h.FilterChannels == 2,
            "Independent gentle curve has no extra inverse-distance gain; ear filter receives stereo");
        yield return Measure(p, h, Vector3.forward, 3, value => report.frontRms = value);
        yield return Measure(p, h, Vector3.right, 3, value => report.rightRms = value);
        yield return Measure(p, h, Vector3.left, 3, value => report.leftRms = value);
        Check(report.frontRms[0] > 0 && report.frontRms[0] / report.frontRms[1] > 0.8f && report.frontRms[0] / report.frontRms[1] < 1.2f,
            "Hum directly ahead remains centered between ears");
        Check(report.rightRms[1] > 0 && report.rightRms[0] / report.rightRms[1] < 0.2f,
            "Source at right: left-ear RMS is less than 20% of right-ear RMS");
        Check(report.leftRms[0] > 0 && report.leftRms[1] / report.leftRms[0] < 0.2f,
            "Source at left: right-ear RMS is less than 20% of left-ear RMS");
        float[] distances = { 1, 6, 12 };
        for (int i = 0; i < distances.Length; i++)
            yield return Measure(p, h, Vector3.forward, distances[i], value => report.distanceRms[i] = (value[0] + value[1]) * 0.5f);
        Check(report.distanceRms[1] / report.distanceRms[0] > 0.5f && report.distanceRms[1] / report.distanceRms[0] < 0.8f,
            "Actual 6m hum level is gently reduced, approximately 65% of the 1m level");
        Check(report.distanceRms[2] / report.distanceRms[0] > 0.3f && report.distanceRms[2] / report.distanceRms[0] < 0.5f,
            "Actual 12m hum level is approximately 40% of the 1m level");
        Check(Mathf.Abs(p.ImpactDistanceGain(6) - originalImpactGain) < 0.0001f,
            "Separate impact distance curve is preserved");
        p.transform.SetPositionAndRotation(position, rotation);
        p.simulateOcclusion = occlusion; p.spatialize = spatial; p.enabled = true; walker.enabled = walking;
        h.humEnabled = active; h.lfoDepth = depth;
        report.complete = true;
        System.IO.File.WriteAllText(Path, JsonUtility.ToJson(report, true));
        Debug.Log("Direct hum verification complete: " + Path);
    }
    static IEnumerator Measure(BouncePlaytest p, HallHumEmitter h, Vector3 towardSource, float distance, Action<float[]> done)
    {
        p.transform.SetPositionAndRotation(h.transform.position - towardSource * distance, Quaternion.identity);
        yield return new WaitForSeconds(0.7f);
        var left = new float[2048]; var right = new float[2048];
        double sumLeft = 0, sumRight = 0; int count = 0;
        float end = Time.time + 0.4f;
        while (Time.time < end)
        {
            AudioListener.GetOutputData(left, 0); AudioListener.GetOutputData(right, 1);
            for (int i = 0; i < left.Length; i++) { sumLeft += left[i] * left[i]; sumRight += right[i] * right[i]; }
            count += left.Length;
            yield return null;
        }
        done(new[] { (float)Math.Sqrt(sumLeft / count), (float)Math.Sqrt(sumRight / count) });
    }
}
