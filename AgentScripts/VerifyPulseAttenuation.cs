using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;

// Real rendered images of the same cube; only the fixed emission origin/profile changes.
public static class VerifyPulseAttenuation
{
    [Serializable] public class Report { public bool complete; public List<string> checks=new List<string>(); public List<string> frames=new List<string>(); }
    static Report report;
    static Color32[] pixels;
    const string Path="/tmp/Sound2-PulseAttenuation.json";
    static void Check(bool okay,string text) => report.checks.Add((okay?"PASS: ":"FAIL: ")+text);
    static long Energy() => pixels.Sum(p=>(long)p.r);
    public static string Main()
    {
        if(!Application.isPlaying) throw new InvalidOperationException("Run in Play mode.");
        report=new Report();
        UnityEngine.Object.FindAnyObjectByType<SphericalSonar>().StartCoroutine(Run());
        return Path;
    }
    static IEnumerator Capture(string label)
    {
        yield return null; yield return new WaitForEndOfFrame();
        var image=ScreenCapture.CaptureScreenshotAsTexture(); pixels=image.GetPixels32();
        report.frames.Add(label+": "+Energy()+" summed red values, "+pixels.Count(p=>p.r>0)+" lit pixels");
        System.IO.File.WriteAllBytes("/tmp/Sound2-Attenuation-"+label+".png",image.EncodeToPNG());
        UnityEngine.Object.Destroy(image);
    }
    static IEnumerator Run()
    {
        var s=UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();
        var emitter=UnityEngine.Object.FindAnyObjectByType<SonarObjectEmitter>();
        var player=s.GetComponentInParent<PlayerController>();
        var controller=player.GetComponent<CharacterController>();
        var hud=typeof(SphericalSonar).GetField("hud",BindingFlags.NonPublic|BindingFlags.Instance);
        var position=s.transform.position; var rotation=s.transform.rotation;
        bool playerEnabled=player.enabled, walking=controller.enabled, oldHud=(bool)hud.GetValue(s);
        bool auto=s.automatic, objectAuto=emitter.automatic, near=s.proximityRendering, sonar=s.sonarRendering;
        bool faces=s.facePoints, edges=s.edgePoints, attenuation=s.distanceAttenuation;
        float start=s.fadeStartDistance,end=s.fadeEndDistance,power=s.falloffPower,strength=s.pulseStrength;
        bool objectAttenuation=emitter.distanceAttenuation, objectSound=emitter.playSound;
        float objectStart=emitter.fadeStartDistance,objectEnd=emitter.fadeEndDistance,
            objectPower=emitter.falloffPower,objectStrength=emitter.pulseStrength;
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetPositionAndRotation(new Vector3(100,100,102),Quaternion.identity);
        cube.transform.localScale=Vector3.one*.8f;
        cube.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        var origin=new Vector3(100,100,101.4f);
        try
        {
            player.enabled=false; controller.enabled=false; hud.SetValue(s,false);
            s.automatic=emitter.automatic=false; s.facePoints=false; s.edgePoints=true;
            s.SetRevealModes(false,true);
            s.transform.SetPositionAndRotation(new Vector3(100,100,100),Quaternion.identity);
            s.ClearPulses(); s.PulseField.EmitFrom(origin,20,100000,10000);
            yield return Capture("original"); long original=Energy(); var originalPixels=pixels;
            Check(original>0,"Legacy four-argument emission remains visible");
            s.ClearPulses(); s.PulseField.EmitFrom(origin,20,100000,10000,0,20,0,1);
            yield return Capture("disabled");
            Check(pixels.SequenceEqual(originalPixels),"Disabled attenuation exactly matches the legacy emission image");
            long previous=long.MaxValue;
            foreach(float z in new[]{101.4f,100.4f,99.4f,98f})
            {
                s.ClearPulses(); s.PulseField.EmitFrom(new Vector3(100,100,z),20,100000,10000,.5f,3,2,.65f);
                yield return Capture("source-z-"+z.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture));
                long energy=Energy();
                Check(energy<previous,"Increasing surface distance reduces actual rendered brightness at source z="+z);
                previous=energy;
            }
            Check(Energy()==0,"Beyond 3m fade end is completely black even though propagation range is 20m");
            // Keep this distant pulse alive, then add a legacy pulse to verify profiles do not leak between slots.
            s.PulseField.EmitFrom(origin,20,100000,10000);
            yield return Capture("mixed");
            Check(pixels.SequenceEqual(originalPixels),"A simultaneous legacy pulse keeps its full original image");
            s.ClearPulses(); s.PulseField.EmitFrom(origin,20,100000,10000,.5f,3,2,0);
            yield return Capture("zero-strength"); Check(Energy()==0,"Zero strength produces no reveal");
            s.distanceAttenuation=true; s.fadeStartDistance=.5f; s.fadeEndDistance=3; s.falloffPower=2; s.pulseStrength=.65f;
            s.ClearPulses(); s.EmitFrom(new Vector3(100,100,98),20,100000,10000);
            yield return Capture("player-enabled"); Check(Energy()==0,"Player toggle routes emission through the capped profile");
            s.distanceAttenuation=false; s.ClearPulses(); s.EmitFrom(origin,20,100000,10000);
            yield return Capture("player-disabled"); Check(pixels.SequenceEqual(originalPixels),"Player toggle off restores the original image");
            var footProfile = new SonarDistanceSettings { enabled=true, fadeStartDistance=.4f,
                fadeEndDistance=3, falloffPower=2, pulseStrength=.6f };
            s.ClearPulses(); s.PulseField.EmitFootstepFrom(origin,3,100000,10000,footProfile);
            yield return Capture("footstep-near"); Check(Energy()>0,"Footstep bank actually reveals nearby geometry in the rendered image");
            s.ClearPulses(); s.PulseField.EmitFootstepFrom(new Vector3(100,100,98),20,100000,10000,footProfile);
            yield return Capture("footstep-far"); Check(Energy()==0,"Footstep bank reveals nothing beyond its 3m cap");
            s.ClearPulses(); s.PulseField.EmitFootstepFrom(origin,3,100000,.1f,footProfile);
            yield return new WaitForSeconds(.2f);
            yield return Capture("footstep-expired"); Check(Energy()==0,"Footstep image disappears after its short afterglow");
            Check(Shader.GetGlobalVectorArray("_SonarSceneDistanceFalloffs").Length==SonarPulseField.MaxPulses,"Per-emission profiles match all pulse slots");
            emitter.distanceAttenuation=true; emitter.playSound=false;
            emitter.fadeStartDistance=.4f; emitter.fadeEndDistance=2.5f;
            emitter.falloffPower=3; emitter.pulseStrength=.45f;
            s.ClearPulses(); int emissionsBefore=emitter.Emissions; emitter.EmitNow();
            int slot=Array.FindIndex(Shader.GetGlobalVectorArray("_SonarSceneParameters"),v=>v.w>.5f);
            var profile=Shader.GetGlobalVectorArray("_SonarSceneDistanceFalloffs")[slot];
            Check(emitter.Emissions==emissionsBefore+1 && Vector4.Distance(profile,new Vector4(.4f,2.5f,3,.45f))<.0001f,
                "Object emitter publishes its own independent attenuation profile");
            var objectOrigin=Shader.GetGlobalVectorArray("_SonarSceneOrigins")[slot];
            Check(Vector3.Distance(new Vector3(objectOrigin.x,objectOrigin.y,objectOrigin.z),emitter.transform.position)<.0001f,
                "Object attenuation stays anchored to the object's emission position");
            s.distanceAttenuation=true;
            Check(Shader.GetGlobalVectorArray("_SonarSceneDistanceFalloffs")[slot]==profile,
                "Changing the player switch does not alter an existing object pulse");
            emitter.distanceAttenuation=false; s.ClearPulses(); emitter.EmitNow();
            slot=Array.FindIndex(Shader.GetGlobalVectorArray("_SonarSceneParameters"),v=>v.w>.5f);
            profile=Shader.GetGlobalVectorArray("_SonarSceneDistanceFalloffs")[slot];
            Check(profile.z==0 && profile.w==1,"Object attenuation off preserves original strength independently of player settings");
        }
        finally
        {
            s.transform.SetPositionAndRotation(position,rotation); controller.enabled=walking; player.enabled=playerEnabled;
            s.automatic=auto; emitter.automatic=objectAuto; s.facePoints=faces; s.edgePoints=edges;
            s.distanceAttenuation=attenuation; s.fadeStartDistance=start; s.fadeEndDistance=end; s.falloffPower=power; s.pulseStrength=strength;
            emitter.distanceAttenuation=objectAttenuation; emitter.playSound=objectSound;
            emitter.fadeStartDistance=objectStart; emitter.fadeEndDistance=objectEnd;
            emitter.falloffPower=objectPower; emitter.pulseStrength=objectStrength;
            s.SetRevealModes(near,sonar); s.ClearPulses(); hud.SetValue(s,oldHud); UnityEngine.Object.Destroy(cube);
            report.complete=true; System.IO.File.WriteAllText(Path,JsonUtility.ToJson(report,true));
        }
    }
}
