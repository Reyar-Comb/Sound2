using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Actual GPU pixels prove that danger shares the existing lattice and wins white/red overlaps.
public static class VerifyDangerSonar
{
    [Serializable] public class Report { public bool complete; public List<string> checks=new List<string>(); public List<string> frames=new List<string>(); }
    static Report report; static Color32[] pixels;
    const string Path="/tmp/Sound2-DangerSonar.json";
    static void Check(bool pass,string label)=>report.checks.Add((pass?"PASS: ":"FAIL: ")+label);
    public static string Main() {
        report=new Report(); UnityEngine.Object.FindAnyObjectByType<SphericalSonar>().StartCoroutine(Run()); return Path;
    }
    static IEnumerator Capture(string name) {
        yield return null;yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();pixels=image.GetPixels32();
        report.frames.Add(name+": "+pixels.Count(p=>p.r>8)+" lit, "+pixels.Count(p=>p.r>8 && p.g<p.r*.2f && p.b<p.r*.2f)+" red");
        System.IO.File.WriteAllBytes("/tmp/Sound2-Danger-"+name+".png",image.EncodeToPNG());UnityEngine.Object.Destroy(image);
    }
    static IEnumerator Run() {
        var s=UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();var field=s.PulseField;
        var player=s.GetComponentInParent<PlayerController>();var controller=player.GetComponent<CharacterController>();
        var scans=UnityEngine.Object.FindObjectsByType<MonsterScanEmitter>();var scanEnabled=Array.ConvertAll(scans,e=>e.enabled);
        var objects=UnityEngine.Object.FindObjectsByType<SonarObjectEmitter>();var objectAuto=Array.ConvertAll(objects,e=>e.automatic);
        var interaction=player.GetComponent<PlayerInteraction>();bool interact=interaction!=null && interaction.enabled;
        var position=s.transform.position;var rotation=s.transform.rotation;
        bool auto=s.automatic,near=s.proximityRendering,sonar=s.sonarRendering,faces=s.facePoints,edges=s.edgePoints,moving=player.enabled,collision=controller.enabled;
        var hud=typeof(SphericalSonar).GetField("hud",BindingFlags.Instance|BindingFlags.NonPublic);bool oldHud=(bool)hud.GetValue(s);
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.transform.position=new Vector3(100,100,102);cube.transform.localScale=Vector3.one*.8f;
        cube.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        var origin=new Vector3(100,100,101.1f);
        var profile=new SonarDistanceSettings();
        try {
            foreach(var scan in scans)scan.enabled=false;foreach(var o in objects)o.automatic=false;
            if(interaction!=null)interaction.enabled=false;
            s.automatic=false;player.enabled=false;controller.enabled=false;hud.SetValue(s,false);
            s.SetRevealModes(false,true);s.transform.SetPositionAndRotation(new Vector3(100,100,100),Quaternion.identity);
            foreach(bool faceOnly in new[]{true,false}) {
                s.facePoints=faceOnly;s.edgePoints=!faceOnly;field.ClearPulses();field.EmitFrom(origin,3,100000,10000,profile);
                yield return Capture(faceOnly?"white-faces":"white-contours");var white=pixels;
                field.ClearPulses();field.EmitDangerFrom(origin,3,100000,10000,profile);
                yield return Capture(faceOnly?"red-faces":"red-contours");
                Check(pixels.Any(p=>p.r>8) && pixels.All(p=>p.g==0 && p.b==0),"Danger "+(faceOnly?"faces":"contours")+" are red, not white");
                Check(pixels.Select(p=>p.r).SequenceEqual(white.Select(p=>p.r)),"Danger "+(faceOnly?"faces":"contours")+" exactly match the original white grid placement, size and density");
                field.EmitFrom(origin,3,100000,10000,profile);
                yield return Capture(faceOnly?"mixed-faces":"mixed-contours");
                Check(pixels.All(p=>p.g==0 && p.b==0),"Overlapping white "+(faceOnly?"faces":"contours")+" cannot wash danger dots white");
            }
            s.facePoints=s.edgePoints=true;field.ClearPulses();field.EmitDangerFrom(origin,3,100000,10000,profile);
            yield return Capture("red-both"); var redBoth=pixels;
            field.EmitFrom(origin,3,100000,10000,profile); yield return Capture("mixed-both");
            Check(pixels.All(p=>p.g==0 && p.b==0),"Danger priority works across the separate face and contour grids");
            Check(pixels.Select(p=>p.r).SequenceEqual(redBoth.Select(p=>p.r)),"Overlapping danger face/contour AA retains its original brightness");
            field.ClearPulses();field.EmitDangerFrom(origin,3,.1f,10000,profile);
            yield return Capture("before-arrival");Check(pixels.All(p=>p.r==0),"Danger dots wait for wave arrival");
            profile.enabled=true;profile.fadeStartDistance=.1f;profile.fadeEndDistance=1;profile.pulseStrength=1;
            field.ClearPulses();field.EmitDangerFrom(new Vector3(100,100,99),12,100000,10000,profile);
            yield return Capture("beyond-range");Check(pixels.All(p=>p.r==0),"Danger dots obey independent distance cutoff");
            profile.enabled=false;field.ClearPulses();field.EmitDangerFrom(origin,3,100000,.1f,profile);
            yield return new WaitForSeconds(.2f);yield return Capture("expired");Check(pixels.All(p=>p.r==0),"Danger dots expire after their own afterglow");
            field.ClearPulses();field.EmitFrom(origin,3,100000,10000);field.EmitFootstepFrom(origin,3,100000,10000,profile);
            var before=(Vector4[])Shader.GetGlobalVectorArray("_SonarSceneOrigins").Clone();
            for(int i=0;i<7;i++)field.EmitDangerFrom(new Vector3(110+i,100,100),3,100000,10000,profile);
            var after=Shader.GetGlobalVectorArray("_SonarSceneOrigins");
            Check(before.Take(8).SequenceEqual(after.Take(8)) && after.Length==SonarPulseField.MaxPulses,"Repeated danger scans use four reserved slots without replacing player or footstep waves");
            Check(ShaderUtil.GetShaderMessages(s.renderingSettings.shader).Length==0,"MRT and red composition shader compile without errors or warnings");
        } finally {
            s.transform.SetPositionAndRotation(position,rotation);controller.enabled=collision;player.enabled=moving;
            s.automatic=auto;s.facePoints=faces;s.edgePoints=edges;s.SetRevealModes(near,sonar);hud.SetValue(s,oldHud);
            for(int i=0;i<scans.Length;i++)scans[i].enabled=scanEnabled[i];for(int i=0;i<objects.Length;i++)objects[i].automatic=objectAuto[i];
            if(interaction!=null)interaction.enabled=interact;field.ClearPulses();UnityEngine.Object.Destroy(cube);
            report.complete=true;System.IO.File.WriteAllText(Path,JsonUtility.ToJson(report,true));
        }
    }
}
