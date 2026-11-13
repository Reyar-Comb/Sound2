using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Render a flat wall with contour drawing disabled, seen from outside the proximity range.
public static class VerifySonarFaces
{
    [Serializable] public class Report {
        public bool complete;
        public List<string> checks = new List<string>();
        public List<string> frames = new List<string>();
    }
    const string Path = "/tmp/Sound2-SonarFaces.json";
    static Report report;
    static Color32[] pixels;
    static long Energy() => pixels.Sum(p => (long)p.r);
    static void Check(bool pass, string text) => report.checks.Add((pass ? "PASS: " : "FAIL: ") + text);
    public static string Main() {
        if (!Application.isPlaying) throw new Exception("Enter Play mode first.");
        report = new Report();
        UnityEngine.Object.FindAnyObjectByType<SphericalSonar>().StartCoroutine(Run());
        return Path;
    }
    static IEnumerator Capture(string label) {
        yield return null; yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture(); pixels = image.GetPixels32();
        report.frames.Add(label + ": " + Energy() + " summed brightness, " + pixels.Count(p => p.r > 8) + " lit pixels");
        System.IO.File.WriteAllBytes("/tmp/Sound2-SonarFaces-" + label + ".png", image.EncodeToPNG());
        UnityEngine.Object.Destroy(image);
    }
    static IEnumerator Run() {
        var s = UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();
        var player = s.GetComponentInParent<PlayerController>();
        var controller = player.GetComponent<CharacterController>();
        var emitters = UnityEngine.Object.FindObjectsByType<SonarObjectEmitter>();
        var automatic = Array.ConvertAll(emitters, e => e.automatic);
        bool auto = s.automatic, near = s.proximityRendering, sonar = s.sonarRendering;
        bool faces = s.facePoints, edges = s.edgePoints, moving = player.enabled, collision = controller.enabled;
        var position = s.transform.position; var rotation = s.transform.rotation;
        var hud = typeof(SphericalSonar).GetField("hud", BindingFlags.Instance | BindingFlags.NonPublic);
        bool oldHud = (bool)hud.GetValue(s);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Temporary sonar face wall";
        wall.transform.position = new Vector3(100,100,100);
        wall.transform.localScale = new Vector3(10,10,.1f);
        wall.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        var profile = new SonarDistanceSettings {enabled=true, fadeStartDistance=.4f, fadeEndDistance=3, falloffPower=2, pulseStrength=.6f};
        var origin = new Vector3(100,100,99);
        try {
            s.automatic = false; foreach(var e in emitters) e.automatic = false;
            player.enabled = false; controller.enabled = false; hud.SetValue(s,false);
            s.facePoints = true; s.edgePoints = false;
            s.transform.SetPositionAndRotation(new Vector3(100,100,93.95f),Quaternion.identity);
            s.SetRevealModes(false,true); s.ClearPulses();
            yield return Capture("no-wave"); Check(Energy()==0,"No pulse leaves the flat wall dark");
            s.PulseField.EmitFrom(origin,3,100000,10000,profile);
            yield return Capture("main-face"); Check(Energy()>0,"Main pulse reveals face dots with contours disabled, viewed from 6m away");
            long main = Energy();
            s.ClearPulses(); s.PulseField.EmitFootstepFrom(origin,3,100000,10000,profile);
            yield return Capture("foot-face"); Check(Energy()>0,"Footstep slots also reveal face dots outside the player's proximity circle");
            s.facePoints = false;
            yield return Capture("faces-disabled"); Check(Energy()==0,"Face toggle still hides all face dots independently");
            s.facePoints = true; s.ClearPulses();
            s.PulseField.EmitFootstepFrom(new Vector3(100,100,96),12,100000,10000,profile);
            yield return Capture("beyond-source-cap"); Check(Energy()==0,"Face reveal respects the source's 3m distance cap");
            s.PulseField.EmitFrom(origin,3,100000,10000,profile);
            yield return Capture("mixed"); Check(Energy()==main,"A distant pulse does not suppress an independent nearby pulse");
            s.ClearPulses(); s.PulseField.EmitFootstepFrom(origin,3,100000,.1f,profile);
            yield return new WaitForSeconds(.2f);
            yield return Capture("expired"); Check(Energy()==0,"Face dots disappear after the footstep afterglow");
            s.ClearPulses(); s.PulseField.EmitFrom(origin,3,.1f,10000,profile);
            yield return Capture("before-arrival"); Check(Energy()==0,"Face dots remain hidden until the wave reaches the surface");
            s.ClearPulses(); s.SetRevealModes(true,false);
            yield return Capture("proximity-far"); Check(Energy()==0,"Original proximity mode keeps its distance limit");
            s.SetRevealModes(false,true);
            s.PulseField.EmitFrom(new Vector3(100,100,95),12,100000,10000);
            yield return Capture("long-range"); Check(Energy()>0,"Unattenuated long-range waves reveal faces beyond 3m from their source");
            s.ClearPulses(); s.SetRevealModes(true,false);
            var nearOrigin = new Vector3(100,100,99.35f);
            s.transform.SetPositionAndRotation(nearOrigin,Quaternion.identity);
            yield return Capture("original-density"); var referencePixels = pixels;
            s.SetRevealModes(false,true);
            s.PulseField.EmitFrom(nearOrigin,3,100000,10000);
            yield return Capture("shared-density");
            Check(pixels.SequenceEqual(referencePixels),"At matching origin and range, pulse faces exactly match the original core/ring density image");
            var cam = s.GetComponent<Camera>();
            var center = cam.WorldToScreenPoint(new Vector3(100,100,99.95f));
            float pixelsPerMetre = Mathf.Abs(cam.WorldToScreenPoint(new Vector3(101,100,99.95f)).x-center.x);
            int coreArea=0, ringArea=0, coreLit=0, ringLit=0;
            for(int y=0;y<Screen.height;y++) for(int x=0;x<Screen.width;x++) {
                float radius = Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(center.x,center.y))/pixelsPerMetre;
                bool lit = pixels[y*Screen.width+x].r>8;
                if(radius<.15f) {coreArea++;if(lit)coreLit++;}
                if(radius>.25f && radius<.30f) {ringArea++;if(lit)ringLit++;}
            }
            float coreDensity=(float)coreLit/Mathf.Max(1,coreArea), ringDensity=(float)ringLit/Mathf.Max(1,ringArea);
            report.frames.Add("core density="+coreDensity+", outer ring density="+ringDensity);
            Check(coreDensity>ringDensity*1.4f && ringLit>0,"Core is measurably denser than the outer ring, rather than merely brighter");
            // Exercise the actual foot-height geometry: a floor, low pulse origin, eye-height viewer.
            s.ClearPulses();
            wall.transform.SetPositionAndRotation(new Vector3(100,100,100),Quaternion.Euler(90,0,0));
            s.transform.SetPositionAndRotation(new Vector3(100,101.6f,100),Quaternion.Euler(90,0,0));
            var footOrigin = new Vector3(100,100.17f,100);
            s.PulseField.EmitFootstepFrom(footOrigin,3,100000,10000,profile);
            yield return Capture("foot-floor-rings");
            center=cam.WorldToScreenPoint(new Vector3(100,100.05f,100));
            pixelsPerMetre=Mathf.Abs(cam.WorldToScreenPoint(new Vector3(101,100.05f,100)).x-center.x);
            coreArea=ringArea=coreLit=ringLit=0;
            for(int y=0;y<Screen.height;y++) for(int x=0;x<Screen.width;x++) {
                float radius=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(center.x,center.y))/pixelsPerMetre;
                bool lit=pixels[y*Screen.width+x].r>8;
                if(radius<.20f) {coreArea++;if(lit)coreLit++;}
                if(radius>.55f && radius<.70f) {ringArea++;if(lit)ringLit++;}
            }
            coreDensity=(float)coreLit/Mathf.Max(1,coreArea); ringDensity=(float)ringLit/Mathf.Max(1,ringArea);
            report.frames.Add("foot floor core density="+coreDensity+", ring density="+ringDensity);
            Check(coreDensity>ringDensity*2 && ringLit>0,"Foot-height pulse on the floor has dense nearby points and progressively sparse outer points");
            Check(ShaderUtil.GetShaderMessages(s.renderingSettings.shader).Length==0,"Shader compiles without warnings or errors");
        } finally {
            s.transform.SetPositionAndRotation(position,rotation);
            controller.enabled=collision; player.enabled=moving;
            s.facePoints=faces; s.edgePoints=edges; s.SetRevealModes(near,sonar);
            s.automatic=auto; for(int i=0;i<emitters.Length;i++) emitters[i].automatic=automatic[i];
            hud.SetValue(s,oldHud); s.ClearPulses(); UnityEngine.Object.Destroy(wall);
            report.complete=true; System.IO.File.WriteAllText(Path,JsonUtility.ToJson(report,true));
        }
    }
}
