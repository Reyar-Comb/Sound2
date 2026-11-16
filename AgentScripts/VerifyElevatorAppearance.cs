using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Sound2.LevelEditing;
using Newtonsoft.Json;
using Object=UnityEngine.Object;

public static class VerifyElevatorAppearance
{
    const string Dir="Logs/ElevatorAppearance/";
    static LevelWorld World=>Object.FindAnyObjectByType<LevelWorld>();
    static async Task Until(Func<bool> test)
    {double end=EditorApplication.timeSinceStartup+120;while(!test()){if(EditorApplication.timeSinceStartup>end)throw new Exception("Timed out");await Task.Delay(20);}}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    public static object Prepare()
    {
        Directory.CreateDirectory(Dir);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SoundAndShell.unity");
        return true;
    }
    static MaterialPropertyBlock Block(LevelElevator lift)
    {var block=new MaterialPropertyBlock();lift.transform.Find("Back").GetComponent<Renderer>().GetPropertyBlock(block);return block;}
    static object State(LevelElevator lift)
    {var b=Block(lift);return new{level=lift.Level.name,lift=lift.name,lift.DoorOpen,dissolve=b.GetFloat("_ArrivalDissolve"),respawn=b.GetFloat("_RespawnPaper"),edges=b.GetFloat("_CabinEdges"),motion=b.GetVector("_CabinMotion").ToString("F4"),grid=Shader.GetGlobalVector("_ElevatorPointGrid").ToString("F4")};}
    static async Task<object> Journey(int index,string label,bool expected)
    {
        var w=World;var source=w.connections[index].source;
        var p=w.player;p.SetInputCaptured(true);
        source.Use(p.GetComponent<PlayerInteraction>(),false);await Until(()=>source.DoorOpen>.999f);
        p.Teleport(source.transform.TransformPoint(new Vector3(0,1.1f,0)));p.FaceDirection(-source.transform.forward);
        Check(w.Travel(source,p),"Travel refused");
        await Until(()=>Block(source).GetVector("_CabinMotion").y>.85f);
        var state=State(source);ScreenCapture.CaptureScreenshot(Dir+label+"-wave.png");
        int firstFrame=Time.frameCount;await Until(()=>Time.frameCount>=firstFrame+2);
        ScreenCapture.CaptureScreenshot(Dir+label+"-wave-next.png");
        if(expected)Check(Shader.GetGlobalVector("_ElevatorPointGrid").x>0&&Shader.GetGlobalVector("_ElevatorPointGrid").z>0,"Wave grid missing");
        await Until(()=>!w.Travelling);await Task.Delay(1200);
        p.FaceDirection(-w.ArrivalCheckpoint.transform.forward);
        ScreenCapture.CaptureScreenshot(Dir+label+"-arrived.png");await Task.Delay(100);
        return state;
    }
    static async Task<object> Death(string label,bool expected,float dissolve)
    {
        var w=World;var p=w.player;var progression=p.GetComponent<PlayerFloorProgression>();
        var feedback=p.GetComponent<PlayerDamageFeedback>();feedback.engulfSeconds=feedback.dissolveSeconds=feedback.wakeSeconds=.1f;feedback.deathHoldSeconds=feedback.whiteHoldSeconds=0;
        var old=w.CurrentLevel;p.SetInputCaptured(true);
        var health=p.GetComponent<PlayerVitality>();health.RestoreHealth();Check(health.TakeLethalDamage(),"Death rejected");
        await Until(()=>!progression.IsRespawning);await Task.Delay(1000);
        Check(w.CurrentLevel!=old,"Level not reset");
        var lift=w.ArrivalCheckpoint;
        p.FaceDirection(-lift.transform.forward);
        ScreenCapture.CaptureScreenshot(Dir+label+"-reset.png");
        if(expected)Check(Mathf.Approximately(Block(lift).GetFloat("_ArrivalDissolve"),dissolve),"Reset appearance changed");
        return State(lift);
    }
    public static async Task<object> Run(bool expected)
    {
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();if(!menu.HasStarted)menu.BeginGame();await Until(()=>menu.HasStarted);
        Shader.SetGlobalVector("_ElevatorPointGrid",Vector4.zero);
        var first=await Journey(0,expected?"fixed-first":"baseline-first",expected);
        var arrived=State(World.ArrivalCheckpoint);
        var reset=await Death(expected?"fixed-school":"baseline-school",expected,1);
        var result=new{first,arrived,reset};File.WriteAllText(Dir+(expected?"fixed":"baseline")+".json",JsonConvert.SerializeObject(result,Formatting.Indented));
        return result;
    }
    public static async Task<object> NextAndRepeat()
    {
        var second=await Journey(1,"fixed-second",true);
        var reset=await Death("fixed-library",true,1);
        var repeated=await Death("fixed-library-repeat",true,1);
        var result=new{second,reset,repeated};File.WriteAllText(Dir+"next-repeat.json",JsonConvert.SerializeObject(result,Formatting.Indented));return result;
    }
    public static async Task<object> SharedCabin()
    {
        await Journey(2,"fixed-home",true);
        var before=State(World.ArrivalCheckpoint);
        Check(World.TryDestination(World.ArrivalCheckpoint,out _,out _),"Expected shared arrival/departure cabin");
        var after=await Death("fixed-home",true,0);
        Check(World.ArrivalCheckpoint.CanUse(World.player.GetComponent<PlayerInteraction>(),true),"Shared cabin must remain usable");
        var result=new{before,after,usable=true};File.WriteAllText(Dir+"shared-cabin.json",JsonConvert.SerializeObject(result,Formatting.Indented));return result;
    }
}
