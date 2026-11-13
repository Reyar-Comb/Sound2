using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Run through Unity CLI in SonarHall Play mode. Verifies rendered pixels, not just flags.
public static class VerifyRevealModes
{
    [Serializable] public class Report
    {
        public bool complete;
        public List<string> checks = new List<string>();
        public List<string> frames = new List<string>();
    }
    static Report report;
    const string Path = "/tmp/Sound2-RevealModes.json";
    static void Check(bool passed, string message)
    {
        report.checks.Add((passed ? "PASS: " : "FAIL: ") + message);
        System.IO.File.WriteAllText(Path, JsonUtility.ToJson(report, true));
    }
    public static string Main()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Run in Play mode.");
        report = new Report();
        UnityEngine.Object.FindAnyObjectByType<SphericalSonar>().StartCoroutine(Run());
        return Path;
    }
    static Color32[] pixels;
    static IEnumerator Capture(string label)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        pixels = image.GetPixels32();
        System.IO.File.WriteAllBytes("/tmp/Sound2-Reveal-" + label + ".png", image.EncodeToPNG());
        report.frames.Add(label + ": " + Lit(pixels) + " lit pixels");
        UnityEngine.Object.Destroy(image);
    }
    static int Lit(Color32[] image)
    {
        int count = 0;
        foreach (var c in image) if (Math.Max(c.r, Math.Max(c.g, c.b)) > 8) count++;
        return count;
    }
    static bool Contains(Color32[] union, Color32[] subset)
    {
        if (union.Length != subset.Length) return false;
        for (int i = 0; i < subset.Length; i++)
            if (subset[i].r > 8 && union[i].r <= 8) return false;
        return true;
    }
    static IEnumerator Run()
    {
        var s = UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();
        var e = UnityEngine.Object.FindAnyObjectByType<SonarObjectEmitter>();
        var style = s.GetComponent<BlackBoxProximitySource>();
        var player = s.GetComponentInParent<PlayerController>();
        bool playerEnabled = player.enabled;
        var controller = player.GetComponent<CharacterController>();
        var hud = typeof(SphericalSonar).GetField("hud", BindingFlags.NonPublic | BindingFlags.Instance);
        bool originalHud = (bool)hud.GetValue(s), playerAuto = s.automatic, objectAuto = e.automatic;
        bool near = s.proximityRendering, sonar = s.sonarRendering;
        bool faces = s.facePoints, edges = s.edgePoints, walking = controller.enabled;
        var position = s.transform.position; var rotation = s.transform.rotation;
        try
        {
            hud.SetValue(s, false);
            s.automatic = e.automatic = false;
            s.facePoints = s.edgePoints = true;
            player.enabled = false; controller.enabled = false;
            s.transform.SetPositionAndRotation(new Vector3(.32f, .7f, 1.4f), Quaternion.identity);
            s.ClearPulses();
            s.SetRevealModes(false, false);
            yield return Capture("none");
            int none = Lit(pixels);
            Check(none == 0, "Both reveal switches off: scene contains no visible point pixels");
            s.SetRevealModes(true, false);
            yield return Capture("proximity");
            var proximityPixels = pixels;
            Check(Lit(pixels) > 0, "Proximity alone reveals nearby geometry without any emission");
            s.SetRevealModes(false, true);
            yield return Capture("sonar-empty");
            Check(Lit(pixels) == 0, "Sonar alone remains dark until a pulse arrives");
            s.SetRevealModes(true, true);
            yield return Capture("both-empty");
            Check(Lit(pixels) == Lit(proximityPixels), "Both enabled without a pulse preserves continuous proximity visibility");
            s.SetRevealModes(false, true);
            s.PulseField.EmitFrom(s.transform.position, 40, 10000, 60);
            yield return new WaitForSeconds(.1f);
            yield return Capture("sonar-pulse");
            var sonarPixels = pixels;
            Check(Lit(pixels) > 0, "Sonar alone reveals surfaces after wave arrival");
            s.SetRevealModes(true, true);
            yield return Capture("both-pulse");
            Check(Contains(pixels, proximityPixels) && Contains(pixels, sonarPixels),
                "Combined image contains both proximity and sonar points");
            s.SetRevealModes(false, false);
            yield return Capture("none-live-pulse");
            Check(Lit(pixels) == 0, "Both off hides an already-active pulse without disabling emission");
            int emissions = e.Emissions;
            e.EmitNow();
            Check(e.Emissions == emissions + 1 && !s.proximityRendering && !s.sonarRendering,
                "Object emission remains functional with both visual effects hidden");
            s.SetRevealModes(true, true);
            s.ClearPulses();
            yield return Capture("both-cleared");
            Check(Lit(pixels) == Lit(proximityPixels), "Clearing pulses leaves proximity rendering visible");
            var modes = Shader.GetGlobalVector("_BlackBoxRevealModes");
            Check(modes.x == 1 && modes.y == 1 && style.ProximityRendering && style.SonarRendering,
                "Controller, reusable style component and Shader receive independent mode flags");
        }
        finally
        {
            s.transform.SetPositionAndRotation(position, rotation);
            controller.enabled = walking; player.enabled = playerEnabled;
            s.facePoints = faces; s.edgePoints = edges;
            s.SetRevealModes(near, sonar);
            s.ClearPulses();
            s.automatic = playerAuto; e.automatic = objectAuto;
            hud.SetValue(s, originalHud);
            report.complete = true;
            System.IO.File.WriteAllText(Path, JsonUtility.ToJson(report, true));
        }
    }
}
