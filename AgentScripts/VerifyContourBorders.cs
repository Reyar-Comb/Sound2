using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

// An unbroken plane has no contour, even at oblique angles and screen borders.
public static class VerifyContourBorders
{
    [Serializable] public class Report { public bool complete; public List<string> checks = new List<string>(); public List<string> frames = new List<string>(); }
    static Report report;
    const string Path = "/tmp/Sound2-ContourBorders.json";
    public static string Main() { report = new Report(); UnityEngine.Object.FindAnyObjectByType<SphericalSonar>().StartCoroutine(Run()); return Path; }
    static IEnumerator Run()
    {
        var s = UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();
        var e = UnityEngine.Object.FindAnyObjectByType<SonarObjectEmitter>();
        var player = s.GetComponentInParent<PlayerController>();
        bool playerEnabled = player.enabled;
        var controller = player.GetComponent<CharacterController>();
        var hud = typeof(SphericalSonar).GetField("hud", BindingFlags.NonPublic | BindingFlags.Instance);
        bool oldHud = (bool)hud.GetValue(s), auto = s.automatic, cubeAuto = e.automatic;
        bool near = s.proximityRendering, sonar = s.sonarRendering, faces = s.facePoints, edges = s.edgePoints, walking = controller.enabled;
        var position = s.transform.position; var rotation = s.transform.rotation;
        var camera = s.GetComponent<Camera>();
        float oldFov = camera.fieldOfView, oldFar = camera.farClipPlane;
        var material = s.renderingSettings;
        var probe = new Material(material);
        probe.SetFloat("_EdgeRevealDistance", 1000);
        probe.SetVector("_EdgeDensityLevels", Vector4.one);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Temporary contour regression plane";
        wall.transform.position = new Vector3(100, 100, 110);
        wall.transform.localScale = new Vector3(1000, 1000, .1f);
        wall.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        try
        {
            s.automatic = e.automatic = false; player.enabled = false; controller.enabled = false;
            camera.fieldOfView = 40; camera.farClipPlane = 500;
            hud.SetValue(s, false); s.facePoints = false; s.edgePoints = true;
            s.renderingSettings = probe; s.ClearPulses(); s.SetRevealModes(true, false);
            foreach (bool pulseMode in new[] { false, true })
            {
                s.SetRevealModes(!pulseMode, pulseMode);
                s.transform.position = new Vector3(100,100,100);
                if (pulseMode)
                {
                    s.PulseField.EmitFrom(s.transform.position, 200, 10000, 60);
                    yield return new WaitForSeconds(.1f);
                }
                string mode = pulseMode ? "sonar" : "proximity";
                foreach (float angle in new[] { 0f, 15f, 30f, 45f, 50f, -15f, -45f })
                {
                    s.transform.SetPositionAndRotation(new Vector3(100,100,100), Quaternion.Euler(0,angle,0));
                    yield return null; yield return new WaitForEndOfFrame();
                    var image = ScreenCapture.CaptureScreenshotAsTexture();
                    var pixels = image.GetPixels32();
                    int count = pixels.Count(c => c.r > 8);
                    int[] columns = new int[image.width];
                    for(int y=0;y<image.height;y++) for(int x=0;x<image.width;x++) if(pixels[y*image.width+x].r>8) columns[x]++;
                    string detail = mode + " yaw " + angle + ": " + count + " pixels; columns " + string.Join(",", columns.Select((n,x)=>new {n,x}).Where(c=>c.n>0).Take(20).Select(c=>c.x+":"+c.n));
                    report.frames.Add(detail);
                    report.checks.Add((count == 0 ? "PASS: " : "FAIL: ") + mode + " unbroken plane has no contour at " + angle);
                    System.IO.File.WriteAllBytes("/tmp/Sound2-ContourBorder-"+mode+"-"+angle+".png",image.EncodeToPNG());
                    UnityEngine.Object.Destroy(image);
                }
            }
            wall.transform.position = new Vector3(100,100,104);
            wall.transform.localScale = Vector3.one * 1.5f;
            wall.transform.rotation = Quaternion.Euler(0,25,0);
            s.transform.SetPositionAndRotation(new Vector3(100,100,100),Quaternion.identity);
            foreach (bool pulseMode in new[] { false, true })
            {
                s.SetRevealModes(!pulseMode,pulseMode);
                s.ClearPulses();
                if(pulseMode)
                {
                    s.PulseField.EmitFrom(s.transform.position, 20, 10000, 60);
                    yield return new WaitForSeconds(.1f);
                }
                yield return null; yield return new WaitForEndOfFrame();
                var image = ScreenCapture.CaptureScreenshotAsTexture();
                int count = image.GetPixels32().Count(c=>c.r>8);
                string mode = pulseMode ? "sonar" : "proximity";
                report.checks.Add((count>0 ? "PASS: " : "FAIL: ")+mode+" real cube edges remain visible: "+count+" pixels");
                System.IO.File.WriteAllBytes("/tmp/Sound2-ContourBorder-"+mode+"-cube.png", image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
            }
        }
        finally
        {
            camera.fieldOfView = oldFov; camera.farClipPlane = oldFar;
            s.renderingSettings = material; s.transform.SetPositionAndRotation(position,rotation);
            controller.enabled = walking; player.enabled = playerEnabled; s.facePoints = faces; s.edgePoints = edges;
            s.SetRevealModes(near,sonar); s.ClearPulses(); s.automatic=auto; e.automatic=cubeAuto;
            hud.SetValue(s,oldHud); UnityEngine.Object.Destroy(wall); UnityEngine.Object.Destroy(probe);
            report.complete=true; System.IO.File.WriteAllText(Path,JsonUtility.ToJson(report,true));
        }
    }
}
