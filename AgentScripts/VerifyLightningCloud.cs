using System;using System.Collections;using System.Collections.Generic;using System.Linq;using UnityEditor;using UnityEngine;

public static class VerifyLightningCloud
{
    [Serializable] public class Report {public bool complete;public List<string> checks=new();public List<string> frames=new();}
    static Report report;static Color32[] pixels;static int width,height;
    static void Check(bool ok,string label)=>report.checks.Add((ok?"PASS: ":"FAIL: ")+label);
    static int Blue()=>pixels.Count(p=>p.b>8 && p.r<3 && p.g<3);
    public static string Main(){System.IO.Directory.CreateDirectory("Logs/PhysicalItems");report=new();UnityEngine.Object.FindAnyObjectByType<SphericalSonar>().StartCoroutine(Run());return "Logs/PhysicalItems/cloud.json";}
    static IEnumerator Capture(string name)
    {
        yield return null;yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();pixels=image.GetPixels32();width=image.width;height=image.height;
        report.frames.Add(name+": "+Blue()+" blue pixels");System.IO.File.WriteAllBytes("Logs/PhysicalItems/"+name+".png",image.EncodeToPNG());UnityEngine.Object.Destroy(image);
    }
    static IEnumerator Run()
    {
        var sonar=UnityEngine.Object.FindAnyObjectByType<SphericalSonar>();var field=sonar.PulseField;var camera=sonar.GetComponent<Camera>();var player=sonar.GetComponentInParent<PlayerController>();
        var motion=player.GetComponent<PlayerCameraMotion>();var capsule=player.GetComponent<CharacterController>();
        var emitters=UnityEngine.Object.FindObjectsByType<SonarObjectEmitter>();var autos=emitters.Select(e=>e.automatic).ToArray();
        var scans=UnityEngine.Object.FindObjectsByType<MonsterScanEmitter>();var scanStates=scans.Select(s=>s.enabled).ToArray();
        bool moving=player.enabled,motionOn=motion.enabled,collision=capsule.enabled,auto=sonar.automatic,near=sonar.proximityRendering,pulses=sonar.sonarRendering,faces=sonar.facePoints,edges=sonar.edgePoints;
        Vector3 position=sonar.transform.position;Quaternion rotation=sonar.transform.rotation;float timeScale=Time.timeScale;
        GameObject root=null,wall=null;
        try
        {
            player.enabled=false;motion.enabled=false;capsule.enabled=false;sonar.automatic=false;
            foreach(var e in emitters)e.automatic=false;foreach(var s in scans)s.enabled=false;
            sonar.facePoints=true;sonar.edgePoints=false;sonar.SetRevealModes(false,true);sonar.transform.SetPositionAndRotation(new Vector3(100,100,100),Quaternion.identity);
            root=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/BallLightning.prefab"));root.transform.position=new Vector3(100,100,102);
            var hazard=root.GetComponent<BallLightning>();hazard.heightRelativeToSpawn=false;hazard.hoverHeight=100;hazard.wanderSpeed=0;hazard.autoCycle=false;hazard.damage=0;
            root.GetComponent<SonarObjectEmitter>().automatic=false;yield return null;yield return null;
            field.ClearPulses();field.EmitFrom(camera.transform.position,4,100000000,10000000,new SonarDistanceSettings());yield return null;yield return null;
            Time.timeScale=0;yield return Capture("cloud-volume");
            Check(Blue()>200 && pixels.All(p=>p.r<3 && p.g<3),"Mesh-free volume renders only blue point samples");
            Check(root.GetComponentsInChildren<MeshRenderer>().Length==0 && root.GetComponentsInChildren<MeshFilter>().Length==0,"No sphere renderer or mesh participates in cloud rendering");
            var front=new HashSet<Vector2Int>();var back=new HashSet<Vector2Int>();
            float pitch=BlackBoxPointCloudFeature.GetDotSpacing(camera)*height/720f;
            for(int i=0;i<hazard.cloudPointCount;i++)
            {
                float y=1-2*(i+.5f)/hazard.cloudPointCount,angle=i*2.39996323f+hazard.CloudPhase;
                float ring=Mathf.Sqrt(1-y*y),radius=Mathf.Pow(Mathf.Repeat((i+.5f)*.754877666f,1),1f/3);
                var local=new Vector3(Mathf.Cos(angle)*ring,y,Mathf.Sin(angle)*ring)*radius*hazard.CloudRadius;
                var screen=camera.WorldToScreenPoint(hazard.Center+local);
                var cell=new Vector2Int(Mathf.FloorToInt(screen.x/pitch),Mathf.FloorToInt((height-screen.y)/pitch));
                if(Vector3.Dot(local,camera.transform.forward)>0)back.Add(cell);else front.Add(cell);
            }
            back.ExceptWith(front);int visibleBack=0;
            foreach(var cell in back)
            {
                int x=Mathf.FloorToInt((cell.x+.5f)*pitch),y=Mathf.FloorToInt(height-(cell.y+.5f)*pitch);
                if(x>=0 && y>=0 && x<width && y<height && pixels[y*width+x].b>8)visibleBack++;
            }
            Check(back.Count>20 && visibleBack>back.Count*.8f,"Back-only projected point cells remain visible ("+visibleBack+" / "+back.Count+")");
            var before=pixels.Select(p=>p.b).ToArray();
            typeof(BallLightning).GetField("<CloudPhase>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(hazard,hazard.CloudPhase+.7f);
            yield return Capture("cloud-rotated");Check(!pixels.Select(p=>p.b).SequenceEqual(before),"Rotating 3D samples changes projected interior structure");
            PlayerUiPointMask.SetLegacyPanel(field,new Rect(0,0,Screen.width,Screen.height),true);
            yield return Capture("cloud-ui-mask");Check(Blue()==0,"Shared UI exclusion masks the complete point cloud");
            PlayerUiPointMask.SetLegacyPanel(field,default,false);
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(100,100,101);wall.transform.localScale=new Vector3(3,3,.1f);
            wall.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");Physics.SyncTransforms();
            yield return Capture("cloud-occluded");Check(Blue()==0,"Opaque world geometry hides the cloud, unlike its own front points");
            wall.SetActive(false);yield return Capture("cloud-wall-removed");Check(Blue()>200,"Removing the wall restores the same cloud");
            field.ClearPulses();yield return Capture("cloud-unrevealed");Check(Blue()==0,"Cloud still obeys the player's proximity/sonar reveal modes");
            Check(!ShaderUtil.ShaderHasError(sonar.renderingSettings.shader),"Volumetric point shader compiles successfully");
        }
        finally
        {
            Time.timeScale=timeScale;PlayerUiPointMask.SetLegacyPanel(field,default,false);field.ClearPulses();
            if(root!=null)UnityEngine.Object.Destroy(root);if(wall!=null)UnityEngine.Object.Destroy(wall);
            sonar.transform.SetPositionAndRotation(position,rotation);motion.enabled=motionOn;capsule.enabled=collision;player.enabled=moving;
            sonar.automatic=auto;sonar.facePoints=faces;sonar.edgePoints=edges;sonar.SetRevealModes(near,pulses);
            for(int i=0;i<emitters.Length;i++)if(emitters[i]!=null)emitters[i].automatic=autos[i];
            for(int i=0;i<scans.Length;i++)if(scans[i]!=null)scans[i].enabled=scanStates[i];
            report.complete=true;System.IO.File.WriteAllText("Logs/PhysicalItems/cloud.json",JsonUtility.ToJson(report,true));
        }
    }
}
