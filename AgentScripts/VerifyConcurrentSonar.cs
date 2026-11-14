using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static class VerifyConcurrentSonar
{
    public static async Task<object> Main()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required.");
        var roots=SceneManager.GetActiveScene().GetRootGameObjects();var active=roots.Select(r=>r.activeSelf).ToArray();
        GameObject fixture=null;RenderTexture rt=null;Texture2D image=null;
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        try
        {
            foreach(var r in roots)r.SetActive(false);
            fixture=new GameObject("Concurrent sonar verification");Vector3 origin=new Vector3(4000,100,4000);
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab"),origin,Quaternion.identity,fixture.transform);
            player.GetComponent<PlayerController>().InputSuspended=true;
            player.GetComponent<PlayerCameraMotion>().enabled=false;
            var sonar=player.GetComponentInChildren<SphericalSonar>();sonar.automatic=false;sonar.SetRevealModes(false,true);
            var style=player.GetComponentInChildren<BlackBoxProximitySource>();
            style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Stars,false);
            style.SetStyleEnabled(BlackBoxProximitySource.PointStyle.Twinkle,false);
            var camera=player.GetComponentInChildren<Camera>();camera.enabled=false;
            camera.transform.SetPositionAndRotation(origin,Quaternion.identity);
            var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);cube.transform.SetParent(fixture.transform);cube.transform.position=origin+Vector3.forward*2;
            cube.transform.localScale=Vector3.one;cube.transform.rotation=Quaternion.Euler(10,25,0);
            cube.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
            var field=sonar.PulseField;int frame=Time.frameCount;await Task.Delay(120);Check(Time.frameCount>frame,"Player frames advance");
            camera.transform.SetPositionAndRotation(origin,Quaternion.identity);style.PublishPosition();Physics.SyncTransforms();
            rt=new RenderTexture(640,480,24);image=new Texture2D(640,480,TextureFormat.RGB24,false);
            camera.targetTexture=rt;
            Directory.CreateDirectory("Logs/ConcurrentSonar");
            int Capture(string name)
            {
                var previous=RenderTexture.active;
                try {camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,640,480),0,0);image.Apply();File.WriteAllBytes("Logs/ConcurrentSonar/"+name+".png",image.EncodeToPNG());return image.GetPixels32().Count(p=>p.r>8||p.g>8||p.b>8);}
                finally{RenderTexture.active=previous;}
            }
            field.ClearPulses();Check(Capture("dark")==0,"No passive nearby reveal masks the regression");
            field.EmitFrom(origin,5,10000,2);
            await Task.Delay(30);
            var before=Shader.GetGlobalVectorArray("_SonarPulseOrigins");
            int litBefore=Capture("before-tick");Check(litBefore>0,"Existing arrived pulse reveals the cube");
            int replacements=field.ActivePulseReplacements;
            for(int i=0;i<7;i++)field.EmitFrom(origin+Vector3.right*i*.01f,5,7,.5f);
            var after=Shader.GetGlobalVectorArray("_SonarPulseOrigins");
            Check(before[0]==after[0]&&field.ActivePulseReplacements==replacements,"Seven simultaneous ticks retain the still-visible wave");
            Check(Shader.GetGlobalVectorArray("_SonarPulseParameters").Count(p=>p.w>.5f)==8,"All concurrent waves have their own slots");
            int litAfter=Capture("after-tick");Check(litAfter>litBefore*.8f,"GPU points remain visible while new tick waves are travelling");

            var profile=new SonarDistanceSettings();
            field.EmitFootstepFrom(origin,5,7,.5f,profile);field.EmitDangerFrom(origin,5,7,.5f,profile);
            field.EmitFrom(origin,5,7,.5f,SonarPulseKind.Coin,profile);field.EmitFrom(origin,5,7,.5f,SonarPulseKind.Lightning,profile);
            var parameters=Shader.GetGlobalVectorArray("_SonarPulseParameters");
            Check(parameters.Length==SonarPulseField.MaxPulses&&parameters[SonarPulseField.FootstepPulseOffset].w==1&&parameters[SonarPulseField.DangerPulseOffset].w==1&&parameters[SonarPulseField.CoinPulseOffset].w==1&&parameters[SonarPulseField.LightningPulseOffset].w==1,"All reserved color and footstep banks use matching shader offsets");
            Check(SonarPulseField.ActiveColoredPalettes==14,"Colored palette selection still includes danger, coin and lightning");
            // Leave one very long pulse at slot 0 and let all other slots expire.
            field.ClearPulses();field.EmitFrom(origin,1,100000,100);
            for(int i=1;i<SonarPulseField.MainPulseCapacity;i++)field.EmitFrom(origin,1,100000,.05f);
            await Task.Delay(100);
            before=Shader.GetGlobalVectorArray("_SonarPulseOrigins");replacements=field.ActivePulseReplacements;
            field.EmitFrom(origin+Vector3.right,1,100000,.05f);
            after=Shader.GetGlobalVectorArray("_SonarPulseOrigins");
            Check(after[0]==before[0]&&field.ActivePulseReplacements==replacements,"Expired slots are reused before overwriting a long-lived wave");

            field.ClearPulses();replacements=field.ActivePulseReplacements;
            float start=Time.time,nextTick=0,nextAlarm=0;int ticks=0,rings=0;
            while(Time.time-start<3.3f)
            {
                float elapsed=Time.time-start;
                if(elapsed>=nextTick){for(int i=0;i<7;i++)field.EmitFrom(origin+Vector3.right*i*.01f,8,7,.7f);ticks++;nextTick+=1;}
                if(elapsed>=nextAlarm){field.EmitFrom(origin,12,7,.5f);rings++;nextAlarm+=.2f;}
                await Task.Delay(25);
            }
            Check(ticks>=3&&rings>=15&&field.ActivePulseReplacements==replacements,"Seven clocks plus rapid alarm retain active waves across repeated cycles");
            Check(ShaderUtil.GetShaderMessages(sonar.renderingSettings.shader).Length==0,"Shader compiles without warnings or errors");
            return new{passed=true,litBefore,litAfter,ticks,rings,lastSamplingMs=field.LastSamplingMilliseconds,checks};
        }
        finally
        {
            if(fixture!=null){fixture.SetActive(false);Object.Destroy(fixture);}
            if(rt!=null)Object.Destroy(rt);if(image!=null)Object.Destroy(image);
            for(int i=0;i<roots.Length;i++)if(roots[i]!=null)roots[i].SetActive(active[i]);
        }
    }
}
