using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering.Universal;

// Compare the real face image with the teammate's original material at multiple distances.
public static class VerifyFaceCloud
{
    [Serializable] public class Report
    {
        public bool complete;
        public List<string> checks = new List<string>();
        public List<string> frames = new List<string>();
    }
    static Report report;
    static Color32[] pixels;
    const string Path = "/tmp/Sound2-FaceCloud.json";
    static void Check(bool okay, string message)
    {
        report.checks.Add((okay ? "PASS: " : "FAIL: ") + message);
        System.IO.File.WriteAllText(Path, JsonUtility.ToJson(report, true));
    }
    public static string Main()
    {
        report = new Report();
        UnityEngine.Object.FindAnyObjectByType<SphericalSonar>().StartCoroutine(Run());
        return Path;
    }
    static IEnumerator Capture(string name)
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        pixels = image.GetPixels32();
        report.frames.Add(name + ": " + pixels.Count(c => c.r > 8) + " lit pixels");
        System.IO.File.WriteAllBytes("/tmp/Sound2-FaceCloud-" + name + ".png", image.EncodeToPNG());
        UnityEngine.Object.Destroy(image);
    }
    static IEnumerator Run()
    {
        var s = UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();
        var cube = UnityEngine.Object.FindAnyObjectByType<SonarObjectEmitter>();
        var feature = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset")
            .rendererFeatures.OfType<BlackBoxPointCloudFeature>().Single();
        var original = feature.settings;
        var sonarMaterial = s.renderingSettings;
        var position = s.transform.position; var rotation = s.transform.rotation;
        var player = s.GetComponentInParent<PlayerController>();
        bool playerEnabled = player.enabled;
        var controller = player.GetComponent<CharacterController>(); bool walking = controller.enabled;
        float radiusBefore = s.dotRadius;
        bool near = s.proximityRendering, sonar = s.sonarRendering;
        bool faces = s.facePoints, edges = s.edgePoints, auto = s.automatic, cubeAuto = cube.automatic;
        var hud = typeof(SphericalSonar).GetField("hud", BindingFlags.NonPublic | BindingFlags.Instance);
        bool hudBefore = (bool)hud.GetValue(s);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Temporary face cloud comparison wall";
        wall.transform.position = Vector3.one * 100;
        wall.transform.localScale = new Vector3(8, 8, .1f);
        wall.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        try
        {
            s.automatic = cube.automatic = false;
            s.facePoints = true; s.edgePoints = false;
            player.enabled = false; controller.enabled = false;
            hud.SetValue(s, false);
            s.SetRevealModes(true, false);
            s.transform.SetPositionAndRotation(new Vector3(100, 100, 99.95f - .6f), Quaternion.identity);
            feature.settings = sonarMaterial;
            yield return Capture("before-0.6m");
            int before = pixels.Count(c => c.r > 8);
            feature.settings = original;
            int i = 0;
            foreach (float distance in new[] { .6f, 1.3f, 2.4f })
            {
                s.transform.SetPositionAndRotation(new Vector3(100, 100, 99.95f - distance), Quaternion.identity);
                s.renderingSettings = sonarMaterial;
                yield return Capture("restored-" + distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "m");
                var restored = pixels;
                if (i++ == 0) Check(pixels.Count(c => c.r > 8) > before * 2,
                    "Restored face cloud has visibly more coverage than the over-thinned sonar face grid");
                s.renderingSettings = original;
                yield return Capture("reference-" + distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "m");
                Check(pixels.SequenceEqual(restored), "Face image exactly matches the teammate's original material at " + distance + " metres");
            }
            s.renderingSettings = sonarMaterial;
            wall.transform.localScale = new Vector3(.8f, .8f, .1f);
            s.transform.SetPositionAndRotation(new Vector3(100, 100, 99.15f), Quaternion.identity);
            foreach (bool faceOnly in new[] { true, false })
            {
                s.facePoints = faceOnly; s.edgePoints = !faceOnly;
                s.dotRadius = .75f;
                yield return Capture(faceOnly ? "face-small" : "contour-small");
                int small = pixels.Count(c => c.r > 8);
                s.dotRadius = 2f;
                yield return Capture(faceOnly ? "face-large" : "contour-large");
                int large = pixels.Count(c => c.r > 8);
                Check(small > 0 && large > small * 2,
                    (faceOnly ? "Face" : "Contour") + " dots resize through the shared camera radius: " + small + " -> " + large);
            }
            s.dotRadius = radiusBefore;
            s.facePoints = true; s.edgePoints = false;
            wall.transform.localScale = new Vector3(8, 8, .1f);
            s.transform.SetPositionAndRotation(new Vector3(100, 100, 99.35f), Quaternion.identity);
            s.SetRevealModes(false, true);
            s.PulseField.EmitFrom(s.transform.position, 12, 10000, 10);
            yield return new WaitForSeconds(.1f);
            yield return Capture("sonar-face");
            Check(pixels.Any(c => c.r > 8), "Original face cloud remains visible through the sonar arrival mask");
            Check(feature.settings == original && s.renderingSettings == sonarMaterial
                && sonarMaterial.GetFloat("_ScreenDotSpacing") == 9 && sonarMaterial.GetFloat("_ScreenDotRadius") == 1.5f,
                "Original face material and sparse sonar contour parameters are preserved independently");
        }
        finally
        {
            feature.settings = original;
            s.dotRadius = radiusBefore;
            s.renderingSettings = sonarMaterial;
            s.transform.SetPositionAndRotation(position, rotation);
            controller.enabled = walking; player.enabled = playerEnabled;
            s.facePoints = faces; s.edgePoints = edges;
            s.SetRevealModes(near, sonar);
            s.automatic = auto; cube.automatic = cubeAuto;
            s.ClearPulses(); hud.SetValue(s, hudBefore);
            UnityEngine.Object.Destroy(wall);
            report.complete = true;
            System.IO.File.WriteAllText(Path, JsonUtility.ToJson(report, true));
        }
    }
}
