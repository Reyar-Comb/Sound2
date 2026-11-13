using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;

public static class VerifyHallHumAndCurve
{
    [Serializable] public class Report
    {
        public bool complete;
        public List<string> checks = new List<string>();
        public float humMinRms = 1, humMaxRms;
        public float[] impactPeaks = new float[3];
        public float[] editedCurvePeaks = new float[2];
    }
    static Report report;
    const string Path = "/tmp/Sound2-HallHumAndCurve.json";
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
        return "Started hum and impact curve tests; report " + Path;
    }
    static IEnumerator Run(BouncePlaytest p)
    {
        var h = p.hallHum;
        bool reverb = p.reverb, spatial = p.spatialize, occlusion = p.simulateOcclusion;
        bool enabled = h.humEnabled, enhanced = p.enhancedAttenuation;
        float lfo = h.lfoHz, depth = h.lfoDepth;
        var curve = p.impactDistanceCurve;
        p.ClearBalls(); p.SetReverb(false); p.SetSpatialization(false); p.simulateOcclusion = false;
        var source = h.GetComponent<AudioSource>();
        Check(Vector3.Distance(h.transform.position, new Vector3(0, 1.2f, 0)) < 0.001f && h.IsPlaying,
            "Fixed center emitter is playing at (0,1.2,0)");
        h.humEnabled = true; h.lfoHz = 1; h.lfoDepth = 1;
        yield return new WaitForSeconds(0.4f);
        float end = Time.time + 2.2f;
        var buffer = new float[2048];
        while (Time.time < end)
        {
            AudioListener.GetOutputData(buffer, 0);
            double sum = 0;
            foreach (float v in buffer) sum += v * v;
            float rms = (float)Math.Sqrt(sum / buffer.Length);
            report.humMinRms = Mathf.Min(report.humMinRms, rms);
            report.humMaxRms = Mathf.Max(report.humMaxRms, rms);
            yield return null;
        }
        Check(report.humMaxRms > 0.01f && report.humMaxRms > report.humMinRms * 3,
            "Actual listener signal contains a continuous hum with amplitude LFO");
        float humVolume = source.volume;
        p.enhancedAttenuation = true;
        p.impactDistanceCurve = AnimationCurve.Constant(0, 20, 0.25f);
        yield return new WaitForSeconds(0.1f);
        Check(Mathf.Abs(p.ImpactDistanceGain(6) - 0.25f) < 0.001f, "Editing the impact curve immediately changes impact gain");
        Check(Mathf.Abs(p.DistanceGain(6) - Mathf.Pow(10, -p.attenuationDbPerMeter * 5 / 20)) < 0.001f,
            "Flight tone retains its independent exponential gain");
        Check(!h.GetComponent<SteamAudio.SteamAudioSource>().distanceAttenuation && Mathf.Abs(source.volume - humVolume) < 0.001f,
            "Impact curve edits leave the fixed hum attenuation and volume unchanged");
        h.humEnabled = false;
        yield return new WaitForSeconds(0.5f);
        Check(source.volume < 0.0001f, "Hum switch smoothly mutes the source");
        p.impactDistanceCurve = curve;
        float[] distances = { 1, 6, 12 };
        for (int i = 0; i < distances.Length; i++)
            yield return MeasureImpact(p, distances[i], v => report.impactPeaks[i] = v);
        Check(report.impactPeaks[0] > report.impactPeaks[1] * 4 && report.impactPeaks[1] > report.impactPeaks[2] * 4 && report.impactPeaks[2] > 0,
            "Actual impact audio has clear distance contrast at 1m, 6m, and 12m");
        p.impactDistanceCurve = AnimationCurve.Constant(0, 20, 0.25f);
        for (int i = 0; i < 2; i++)
            yield return MeasureImpact(p, i == 0 ? 1 : 6, v => report.editedCurvePeaks[i] = v);
        float ratio = report.editedCurvePeaks[0] / Mathf.Max(0.000001f, report.editedCurvePeaks[1]);
        Check(ratio > 0.8f && ratio < 1.2f && report.editedCurvePeaks[1] > 0,
            "Flat custom curve produces matching actual impact levels at 1m and 6m (no hidden inverse-distance multiplier)");
        p.enhancedAttenuation = false;
        Check(Mathf.Abs(p.ImpactDistanceGain(6) - 1f / 6) < 0.001f, "Switch 3 off restores physical impact distance gain");
        p.impactDistanceCurve = curve; p.enhancedAttenuation = enhanced;
        p.simulateOcclusion = occlusion; p.SetSpatialization(spatial); p.SetReverb(reverb);
        h.humEnabled = enabled; h.lfoHz = lfo; h.lfoDepth = depth;
        p.ClearBalls();
        report.complete = true;
        System.IO.File.WriteAllText(Path, JsonUtility.ToJson(report, true));
        Debug.Log("Hum and impact curve verification complete: " + Path);
    }
    static IEnumerator MeasureImpact(BouncePlaytest p, float distance, Action<float> result)
    {
        p.ClearBalls(); yield return new WaitForSeconds(0.3f);
        typeof(BouncePlaytest).GetMethod("PlayImpact", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(p, new object[] { p.transform.position + Vector3.forward * distance, 0.7f });
        float peak = 0, end = Time.time + 0.45f;
        var buffer = new float[1024];
        while (Time.time < end)
        {
            AudioListener.GetOutputData(buffer, 0);
            foreach (float value in buffer) peak = Mathf.Max(peak, Mathf.Abs(value));
            yield return null;
        }
        result(peak);
    }
}
