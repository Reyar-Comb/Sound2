using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class VerifyCoinPoints
{
    [Serializable] public class Frame {public string name;public int lit,colored,inner,outer;public float meanRadius,maxRadius,coverage;}
    [Serializable] public class Report {public bool complete;public List<string> checks=new();public List<Frame> frames=new();public List<string> measurements=new();}
    static Report report;
    static readonly string folder="Logs/CoinPoints";
    static void Check(bool ok,string label)=>report.checks.Add((ok?"PASS: ":"FAIL: ")+label);
    public static string Main()
    {
        System.IO.Directory.CreateDirectory(folder);report=new Report();
        UnityEngine.Object.FindAnyObjectByType<SphericalSonar>().StartCoroutine(Run());return folder+"/report.json";
    }
    static IEnumerator Capture(string name,Camera camera,CoinPickup coin)
    {
        yield return null;yield return new WaitForEndOfFrame();
        var image=ScreenCapture.CaptureScreenshotAsTexture();var pixels=image.GetPixels32();
        var c=camera.WorldToScreenPoint(coin.body.bounds.center);
        float radius=Vector2.Distance(c,camera.WorldToScreenPoint(coin.body.bounds.center+camera.transform.right*.275f));
        var f=new Frame {name=name};float sum=0;
        for(int y=0;y<image.height;y++)for(int x=0;x<image.width;x++)
        {
            var p=pixels[y*image.width+x];if(Math.Max(p.r,Math.Max(p.g,p.b))<=8)continue;
            float r=Vector2.Distance(new Vector2(x,y),c)/radius;
            // Only the isolated coin is in view; record all unexpected pixels as well.
            f.lit++;sum+=r;f.maxRadius=Mathf.Max(f.maxRadius,r);
            if(r<.25f)f.inner++;if(r>.55f)f.outer++;
            if(Math.Abs(p.r-p.g)>2 || Math.Abs(p.r-p.b)>2)f.colored++;
        }
        f.meanRadius=f.lit>0?sum/f.lit:0;f.coverage=f.lit/(Mathf.PI*radius*radius);
        report.frames.Add(f);
        System.IO.File.WriteAllBytes(folder+"/"+name+".png",image.EncodeToPNG());UnityEngine.Object.Destroy(image);
    }
    static IEnumerator Run()
    {
        var sonar=UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();var field=sonar.PulseField;
        var camera=sonar.GetComponent<Camera>();var player=sonar.GetComponentInParent<PlayerController>();
        var motion=player.GetComponent<PlayerCameraMotion>();var capsule=player.GetComponent<CharacterController>();
        var canvases=UnityEngine.Object.FindObjectsByType<Canvas>();var canvasStates=canvases.Select(c=>c.enabled).ToArray();
        var emitters=UnityEngine.Object.FindObjectsByType<SonarObjectEmitter>();var autos=emitters.Select(e=>e.automatic).ToArray();
        var scans=UnityEngine.Object.FindObjectsByType<MonsterScanEmitter>();var scanStates=scans.Select(s=>s.enabled).ToArray();
        var position=sonar.transform.position;var rotation=sonar.transform.rotation;
        bool moving=player.enabled,motionOn=motion.enabled,collision=capsule.enabled,auto=sonar.automatic,near=sonar.proximityRendering,sonarOn=sonar.sonarRendering,faces=sonar.facePoints,edges=sonar.edgePoints;
        var started=typeof(CoinPickup).GetField("revealStarted",BindingFlags.Instance|BindingFlags.NonPublic);
        GameObject root=null;
        try
        {
            foreach(var c in canvases)c.enabled=false;foreach(var e in emitters)e.automatic=false;foreach(var s in scans)s.enabled=false;
            player.enabled=false;motion.enabled=false;capsule.enabled=false;sonar.automatic=false;
            sonar.SetRevealModes(true,true);sonar.facePoints=true;sonar.edgePoints=true;
            sonar.transform.SetPositionAndRotation(new Vector3(100,100,100),Quaternion.identity);
            root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Coin.prefab"));
            root.GetComponent<Rigidbody>().interpolation=RigidbodyInterpolation.None;
            root.GetComponent<Rigidbody>().position=new Vector3(100,100,101);
            root.transform.position=new Vector3(100,100,101);
            var coin=root.GetComponent<CoinPickup>();coin.bobAmplitude=0;coin.spinDegreesPerSecond=0;coin.bodyRevealSeconds=1000;
            var emitter=root.GetComponent<SonarObjectEmitter>();emitter.automatic=false;
            yield return new WaitForSeconds(.8f);field.ClearPulses();
            yield return Capture("hidden",camera,coin);
            Check(report.frames[^1].lit==0,"Hidden coin has no solid surface or premature proximity reveal");
            emitter.EmitNow();field.ClearPulses();
            foreach(float phase in new[]{.08f,.30f,.60f,.92f})
            {
                started.SetValue(coin,Time.time-phase*1000);
                yield return Capture("phase-"+phase.ToString("F2",System.Globalization.CultureInfo.InvariantCulture),camera,coin);
            }
            var a=report.frames[1];var b=report.frames[2];var c2=report.frames[3];var d=report.frames[4];
            Check(a.lit>0 && a.outer==0,"First appearance is confined to the centre");
            Check(b.meanRadius>a.meanRadius && c2.meanRadius>b.meanRadius && d.meanRadius>c2.meanRadius,"Actual rendered wave moves outward across all four phases");
            Check(c2.inner==0 && c2.outer>0 && d.inner==0 && d.outer>0,"Centre fades while outer surface lights up");
            Check(report.frames.Skip(1).All(f=>f.colored==0),"Coin surface points are white, without yellow fill");
            Check(report.frames.Skip(1).All(f=>f.coverage<.15f),"Sparse dots occupy less than 15% of the solid silhouette in every phase");
            started.SetValue(coin,Time.time-.30f*1000);
            field.EmitDangerFrom(coin.visual.position,3,100000000,10000000,new SonarDistanceSettings());
            yield return Capture("external-red-wave",camera,coin);
            Check(report.frames[^1].colored==0 && report.frames[^1].outer==0,"External red/proximity waves cannot light the whole coin or recolor it");
            PlayerUiPointMask.SetLegacyPanel(field,new Rect(0,0,Screen.width,Screen.height),true);
            yield return Capture("ui-mask",camera,coin);Check(report.frames[^1].lit==0,"Coin points respect the shared UI exclusion mask");
            PlayerUiPointMask.SetLegacyPanel(field,default,false);field.ClearPulses();
            coin.bodyRevealSeconds=.55f;emitter.EmitNow();field.ClearPulses();
            int frame=Time.frameCount;yield return new WaitForSeconds(.65f);
            yield return Capture("expired",camera,coin);
            Check(Time.frameCount>frame && !coin.BodyVisible && report.frames[^1].lit==0,"Default real-time wave expires with the game advancing");
            emitter.EmitNow();field.ClearPulses();Check(coin.BodyVisible,"Next pulse restarts the coin reveal");
            Check(!ShaderUtil.ShaderHasError(sonar.renderingSettings.shader),"Point shader compiles successfully");
        }
        finally
        {
            PlayerUiPointMask.SetLegacyPanel(field,default,false);field.ClearPulses();
            if(root!=null)UnityEngine.Object.Destroy(root);
            sonar.transform.SetPositionAndRotation(position,rotation);motion.enabled=motionOn;capsule.enabled=collision;player.enabled=moving;
            sonar.automatic=auto;sonar.facePoints=faces;sonar.edgePoints=edges;sonar.SetRevealModes(near,sonarOn);
            for(int i=0;i<canvases.Length;i++)if(canvases[i]!=null)canvases[i].enabled=canvasStates[i];
            for(int i=0;i<emitters.Length;i++)if(emitters[i]!=null)emitters[i].automatic=autos[i];
            for(int i=0;i<scans.Length;i++)if(scans[i]!=null)scans[i].enabled=scanStates[i];
            report.measurements=report.frames.Select(f=>$"{f.name}: lit={f.lit}, colored={f.colored}, inner={f.inner}, outer={f.outer}, meanRadius={f.meanRadius:F3}, maxRadius={f.maxRadius:F3}, coverage={f.coverage:P2}").ToList();
            report.complete=true;System.IO.File.WriteAllText(folder+"/report.json",JsonUtility.ToJson(report,true));
        }
    }

}
