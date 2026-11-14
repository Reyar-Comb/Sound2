using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class VerifyColoredSonar
{
    [Serializable] public class Report {public bool complete;public List<string> checks=new List<string>();public List<string> frames=new List<string>();}
    static Report report;static Color32[] pixels;
    const string Path="Logs/TeammateIntegration_20261114/Sound2-ColoredSonar.json";
    static void Check(bool pass,string text)=>report.checks.Add((pass?"PASS: ":"FAIL: ")+text);
    static int Lit()=>pixels.Count(p=>Math.Max(p.r,Math.Max(p.g,p.b))>8);
    public static string Main(){System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));report=new Report();UnityEngine.Object.FindAnyObjectByType<SphericalSonar>().StartCoroutine(Run());return Path;}
    static IEnumerator Capture(string name)
    {
        yield return null;yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();pixels=image.GetPixels32();
        report.frames.Add(name+": "+Lit()+" lit pixels");System.IO.File.WriteAllBytes("Logs/TeammateIntegration_20261114/Sound2-Colors-"+name+".png",image.EncodeToPNG());UnityEngine.Object.Destroy(image);
    }
    static IEnumerator Run()
    {
        var s=UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();var field=s.PulseField;var player=s.GetComponentInParent<PlayerController>();var capsule=player.GetComponent<CharacterController>();
        var source=UnityEngine.Object.FindAnyObjectByType<BlackBoxProximitySource>();bool oldStars=source.IsStyleEnabled(BlackBoxProximitySource.PointStyle.Stars);source.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars,false);
        var motion=player.GetComponent<PlayerCameraMotion>();bool motionEnabled=motion!=null && motion.enabled;if(motion!=null)motion.enabled=false;
        var emitters=UnityEngine.Object.FindObjectsByType<SonarObjectEmitter>();var auto=Array.ConvertAll(emitters,e=>e.automatic);
        var scans=UnityEngine.Object.FindObjectsByType<MonsterScanEmitter>();var enabled=Array.ConvertAll(scans,e=>e.enabled);
        var hud=typeof(SphericalSonar).GetField("hud",BindingFlags.NonPublic|BindingFlags.Instance);bool oldHud=(bool)hud.GetValue(s);
        var status=player.GetComponent<PlayerStatusHUD>();bool oldStatus=status!=null&&status.visible;if(status!=null)status.visible=false;
        var position=s.transform.position;var rotation=s.transform.rotation;
        bool oldAuto=s.automatic,near=s.proximityRendering,sonar=s.sonarRendering,faces=s.facePoints,edges=s.edgePoints,moving=player.enabled,collision=capsule.enabled;
        var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.transform.position=new Vector3(100,100,102);cube.transform.localScale=Vector3.one*.8f;
        // Expose two faces so the contour fixture has a real normal discontinuity at any viewport size.
        cube.transform.rotation=Quaternion.Euler(15,25,0);
        // Compare GPU palettes without differing 24/48-ray occlusion sampling on this tiny fixture.
        UnityEngine.Object.Destroy(cube.GetComponent<Collider>());
        cube.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
        GameObject lightningBody=null;
        var origin=new Vector3(100,100,101.1f);var profile=new SonarDistanceSettings();
        var density=new SonarDensitySettings {enabled=true,fadeStartDistance=0,fadeEndDistance=3,falloffPower=8};
        try
        {
            foreach(var e in emitters)e.automatic=false;foreach(var scan in scans)scan.enabled=false;
            s.automatic=false;player.enabled=false;capsule.enabled=false;hud.SetValue(s,false);
            s.SetRevealModes(false,true);s.transform.SetPositionAndRotation(new Vector3(100,100,100),Quaternion.identity);
            // Allow deferred collider destruction and UI/camera state to settle before occlusion sampling.
            yield return null; Physics.SyncTransforms();
            foreach(bool face in new[]{true,false})
            {
                s.facePoints=face;s.edgePoints=!face;string style=face?"faces":"contours";
                field.ClearPulses();field.EmitFrom(origin,3,100000000,10000000,profile);yield return Capture("white-"+style);var white=pixels;
                field.ClearPulses();field.EmitFrom(origin,3,100000000,10000000,SonarPulseKind.Coin,profile);yield return Capture("coin-white-"+style);
                Check(Lit()>0 && pixels.All(p=>Math.Abs(p.r-p.g)<=2 && Math.Abs(p.r-p.b)<=2),"Coin "+style+" are white");
                Check(pixels.Select(p=>p.r).SequenceEqual(white.Select(p=>p.r)),"Coin white "+style+" use exactly the original point positions, radii and density");
                field.ClearPulses();field.EmitFrom(origin,3,100000000,10000000,SonarPulseKind.Lightning,profile);yield return Capture("blue-"+style);
                Check(Lit()>0 && pixels.All(p=>p.r==0 && p.g==0),"Lightning "+style+" are pure blue");
                // HDR targets can have 10 blue bits versus 11 red bits; allow two final 8-bit levels.
                Check(pixels.Select((p,i)=>Math.Abs(p.b-white[i].r)<=2).All(v=>v),"Blue "+style+" match the original lattice, radius and density within two HDR color-quantization levels");
                int full=Lit();var fullPixels=pixels;
                // Sparse contours need a steeper cutoff to exercise removal at small Game view resolutions.
                density.fadeEndDistance=face?3:2f;
                field.ClearPulses();field.EmitFrom(origin,3,100000000,10000000,SonarPulseKind.Lightning,profile,density);yield return Capture("thinned-"+style);
                Check(Lit()>0 && Lit()<full,"Density attenuation removes "+style+" points rather than only dimming them");
                Check(pixels.Where((p,i)=>p.b>8).Select((p)=>p.b).Any() && pixels.Select((p,i)=>p.b==0 || Math.Abs(p.b-fullPixels[i].b)<=2).All(v=>v),"Retained "+style+" keep their original pixel brightness within two HDR quantization levels");
            }
            s.facePoints=s.edgePoints=true;field.ClearPulses();
            field.EmitFrom(origin,3,100000000,10000000,SonarPulseKind.Coin,profile);
            field.EmitFrom(origin,3,100000000,10000000,SonarPulseKind.Lightning,profile);field.EmitFrom(origin,3,100000000,10000000,profile);
            yield return Capture("blue-priority");Check(Lit()>0 && pixels.All(p=>p.r==0 && p.g==0),"Blue remains blue when player and coin white waves overlap");
            field.EmitDangerFrom(origin,3,100000000,10000000,profile);yield return Capture("red-priority");
            Check(Lit()>0 && pixels.All(p=>p.g==0 && p.b==0),"Monster red has priority over blue and coin white");
            density.fadeEndDistance=.4f;field.ClearPulses();field.EmitDangerFrom(origin,3,100000000,10000000,profile,density);yield return Capture("density-cap");
            Check(Lit()==0,"Density cap hides faces and contours completely beyond its endpoint");
            field.ClearPulses();field.EmitFrom(origin,1,.1f,10000000,SonarPulseKind.Coin,profile);yield return Capture("before-arrival");
            Check(Lit()==0,"Coin white wave waits for arrival");
            field.ClearPulses();field.EmitFrom(new Vector3(100,100,99),1,100000000,10000000,SonarPulseKind.Coin,profile);yield return Capture("short-range");
            Check(Lit()==0,"Short coin wave cannot reveal distant geometry");
            field.ClearPulses();field.EmitFrom(origin,3,100000000,.05f,SonarPulseKind.Lightning,profile);yield return new WaitForSeconds(.15f);yield return Capture("expired");
            Check(Lit()==0,"Blue wave expires normally");
            field.ClearPulses();field.EmitFrom(origin,3,100000000,10000000);field.EmitDangerFrom(origin,3,100000000,10000000,profile);
            var before=(Vector4[])Shader.GetGlobalVectorArray("_SonarPulseOrigins").Clone();
            for(int i=0;i<12;i++)field.EmitFrom(origin+Vector3.right*i,3,100000000,10000000,SonarPulseKind.Coin,profile);
            var after=Shader.GetGlobalVectorArray("_SonarPulseOrigins");
            Check(before.Take(SonarPulseField.CoinPulseOffset).SequenceEqual(after.Take(SonarPulseField.CoinPulseOffset)) && after.Length==SonarPulseField.MaxPulses,"Coins use eight reserved slots without replacing player, footsteps or monster scans");
            UnityEngine.Object.Destroy(cube);yield return null;
            lightningBody=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BallLightning.prefab"));
            lightningBody.transform.position=new Vector3(100,100,102);
            var hazard=lightningBody.GetComponent<BallLightning>();hazard.hoverHeight=100;hazard.autoCycle=false;hazard.wanderSpeed=0;
            lightningBody.GetComponent<SonarObjectEmitter>().automatic=false;yield return null;
            field.ClearPulses();field.EmitFrom(origin,3,100000000,10000000,profile);yield return Capture("white-reveals-blue-body");
            Check(Lit()>0 && pixels.All(p=>p.r==0 && p.g==0),"Player white sonar reveals the lightning body in blue, preserving its identity");
            // Merge regression: UI exclusion must mask both MRT grids, including all colored palettes.
            foreach(var kind in new[]{SonarPulseKind.Main,SonarPulseKind.Danger,SonarPulseKind.Coin,SonarPulseKind.Lightning})
            {
                field.ClearPulses();field.EmitFrom(origin,3,100000000,10000000,kind,profile);
                PlayerUiPointMask.SetLegacyPanel(field,new Rect(0,0,Screen.width,Screen.height),true);
                yield return Capture("ui-masked-"+kind);
                Check(Lit()==0,"UI exclusion hides "+kind+" faces and contours");
                PlayerUiPointMask.SetLegacyPanel(field,default,false);
                yield return Capture("ui-restored-"+kind);
                Check(Lit()>0,"Removing UI exclusion restores "+kind+" points");
            }
            Check(ShaderUtil.GetShaderMessages(s.renderingSettings.shader).Length==0,"Colored point shader compiles without errors or warnings");
        }
        finally
        {
            source.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars,oldStars);
            PlayerUiPointMask.SetLegacyPanel(field,default,false);
            s.transform.SetPositionAndRotation(position,rotation);if(motion!=null)motion.enabled=motionEnabled;capsule.enabled=collision;player.enabled=moving;
            s.automatic=oldAuto;s.facePoints=faces;s.edgePoints=edges;s.SetRevealModes(near,sonar);hud.SetValue(s,oldHud);if(status!=null)status.visible=oldStatus;
            for(int i=0;i<emitters.Length;i++)if(emitters[i]!=null)emitters[i].automatic=auto[i];
            for(int i=0;i<scans.Length;i++)scans[i].enabled=enabled[i];field.ClearPulses();if(cube!=null)UnityEngine.Object.Destroy(cube);if(lightningBody!=null)UnityEngine.Object.Destroy(lightningBody);
            report.complete=true;System.IO.File.WriteAllText(Path,JsonUtility.ToJson(report,true));
        }
    }
}
