using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Sound2.LevelEditing;
using Object = UnityEngine.Object;

// Diagnostic harness only. No saved scene or gameplay settings are changed.
public static class ProfileWeddingJourney
{
    const string Dir = "Logs/WeddingJourney";
    static LevelWorld World => Object.FindAnyObjectByType<LevelWorld>();
    static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent)+"/"+t.name;
    static void Log(object value) { Directory.CreateDirectory(Dir); File.AppendAllText(Dir+"/events.jsonl",JsonConvert.SerializeObject(value)+"\n"); }
    public static object Snapshot(string label)
    {
        var w=World;
        var result=new {label,frame=Time.frameCount,time=Time.time,level=w.CurrentLevel.name,w.Travelling,
            position=w.player.transform.position.ToString(),held=w.player.GetComponent<PlayerPickup>().HeldItem?.name,
            levels=w.levels.Select(l=>new {l.name,active=l.gameObject.activeInHierarchy,l.IsPreparing,
                items=l.GetComponentsInChildren<PickupItem>().Length,
                bodies=l.GetComponentsInChildren<Rigidbody>().Length,
                collisionEmissions=l.GetComponentsInChildren<CollisionSonarEmitter>().Sum(c=>c.Emissions),
                remains=l.GetComponentsInChildren<Transform>().Count(t=>t.name.Contains("Broken") || t.name.Contains("Fragment"))}).ToArray(),
            sonar=Object.FindObjectsByType<SonarPulseField>().Select(s=>new{s.Emissions,s.LastRayCount}).ToArray()};
        Log(result);return result;
    }
    public static object Begin()
    {
        Directory.CreateDirectory(Dir);
        var w=World;
        string run=SessionState.GetString("WeddingJourney.Run","baseline");
        foreach(var l in w.levels) foreach(var item in l.GetComponentsInChildren<PickupItem>(true))
        {
            var original=item.transform.position;var path=PathOf(item.transform);
            item.Broken+=()=>Log(new {kind="break",run,frame=Time.frameCount,time=Time.time,level=l.name,l.IsPreparing,
                current=w.CurrentLevel.name,w.Travelling,path,initial=original.ToString(),position=item.transform.position.ToString(),
                velocity=item.GetComponent<Rigidbody>().linearVelocity.ToString(),item.breakSpeed,
                clip=item.breakClip?.name,item.breakOnlyWhenThrown});
        }
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if(SonicSpaceStartMenu.BlocksGameplay)menu.BeginGame();
        return Snapshot("begin");
    }
    static async Task Until(Func<bool> condition)
    {
        double end=EditorApplication.timeSinceStartup+90;
        while(!condition()){if(EditorApplication.timeSinceStartup>end)throw new Exception("Journey timeout");await Task.Delay(20);}
    }
    static ElevatorConnection Next() => World.connections.First(c=>c.source.Level==World.CurrentLevel);
    public static async Task<object> Stage()
    {
        var w=World;var source=Next().source;
        source.Use(w.player.GetComponent<PlayerInteraction>(),false);
        await Until(()=>source.DoorOpen>.999f);
        w.player.Teleport(source.transform.TransformPoint(new Vector3(0,1.1f,0)));
        return Snapshot("staged");
    }
    public static async Task<object> Travel()
    {
        var w=World;var source=Next().source;
        Log(new {kind="travel-start",frame=Time.frameCount,time=Time.time,from=w.CurrentLevel.name,to=Next().destination.Level.name});
        source.Use(w.player.GetComponent<PlayerInteraction>(),true);
        if(!w.Travelling)throw new Exception("Travel rejected");
        await Until(()=>!w.Travelling);
        await Task.Delay(2000);
        return Snapshot("arrived");
    }
    public static async Task<object> StageAndTravel() { await Stage(); return await Travel(); }
    public static object Hold()
    {
        var w=World;var picker=w.player.GetComponent<PlayerPickup>();
        var alarm=w.CurrentLevel.GetComponentsInChildren<PickupAlarm>().First();
        var item=alarm.GetComponent<PickupItem>();
        var body=item.GetComponent<Rigidbody>();body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;
        body.position=picker.viewTransform.position+picker.viewTransform.forward*.65f;item.transform.position=body.position;
        var collectible=item.GetComponent<StoryCollectible>();
        if(collectible!=null)throw new Exception("Collectible needs normal discovery first");
        Physics.SyncTransforms();if(!picker.TryPickUp(item))throw new Exception("Pickup failed");
        return Snapshot("hold-alarm");
    }
    public static object WatchWedding()
    {
        var level=World.levels.First(l=>l.name.Contains("婚礼"));
        foreach(var item in level.GetComponentsInChildren<PickupItem>(true))item.gameObject.AddComponent<JourneyCollisionProbe>();
        return level.GetComponentsInChildren<PickupItem>(true).Length;
    }
    public static async Task<object> Walk(float seconds, bool jump)
    {
        var p=World.player;var settings=UnityEngine.InputSystem.InputSystem.settings;
        var background=settings.backgroundBehavior;var editorInput=settings.editorInputBehaviorInPlayMode;
        var old=UnityEngine.InputSystem.Keyboard.current;
        var keyboard=old ?? UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
        var before=p.transform.position;int frame=Time.frameCount;
        try
        {
            settings.backgroundBehavior=UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode=UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            keyboard.MakeCurrent();p.SetInputCaptured(true);
            p.FaceDirection(World.ArrivalCheckpoint.transform.forward);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,jump
                ? new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W,UnityEngine.InputSystem.Key.Space)
                : new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
            await Task.Delay((int)(seconds*1000));
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());
            await Task.Delay(120);
        }
        finally
        {
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());
            if(old==null)UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            if(old!=null&&old.added)old.MakeCurrent();
            settings.backgroundBehavior=background;settings.editorInputBehaviorInPlayMode=editorInput;
        }
        var result=new {kind="walk",level=World.CurrentLevel.name,jump,frames=Time.frameCount-frame,
            distance=Vector3.Distance(before,p.transform.position),from=before.ToString(),to=p.transform.position.ToString(),
            leftCabin=!World.ArrivalCheckpoint.Contains(p.transform.position)};Log(result);return result;
    }
    public static object Alarm(bool enabled)
    {
        var a=World.player.GetComponent<PlayerPickup>().HeldItem.GetComponent<PickupAlarm>();
        a.enabled=enabled;a.GetComponent<TickingClock>().running=enabled;
        return new {a.enabled,a.IsRinging,a.Rings,a.ringInterval,a.alarmClip};
    }
    public static object AuditTemplates()
    {
        var entries=(System.Collections.IDictionary)typeof(LevelWorld).GetField("initialLevels",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(World);
        var results=new System.Collections.Generic.List<object>();
        foreach(System.Collections.DictionaryEntry e in entries)
        {
            var level=(LevelLayout)e.Value.GetType().GetField("template").GetValue(e.Value);
            var items=level.GetComponentsInChildren<PickupItem>(true);
            results.Add(new {level=((LevelLayout)e.Key).name,items=items.Length,
                duplicateGroups=items.GroupBy(p=>level.transform.InverseTransformPoint(p.transform.position).ToString("F4"))
                    .Where(g=>g.Count()>1).Select(g=>new {position=g.Key,count=g.Count(),names=g.Select(p=>p.name).ToArray()}).ToArray(),
                breakables=items.GroupBy(p=>p.brokenPrefab).Select(g=>new {prefab=g.Key!=null?g.Key.name:null,count=g.Count(),fragments=g.Key!=null?g.Key.GetComponentsInChildren<Rigidbody>(true).Length:0}).ToArray()});
        }
        File.WriteAllText(Dir+"/authored-audit.json",JsonConvert.SerializeObject(results,Formatting.Indented));return results;
    }
    public static async Task<object> PrepareMarket(bool removeDuplicates)
    {
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if(menu!=null)await Until(()=>menu.IntroReady);
        SessionState.SetString("WeddingJourney.Run",removeDuplicates?"dedup-no-alarm":"original-no-alarm");
        Begin();await Until(()=>!SonicSpaceStartMenu.BlocksGameplay);
        var w=World;var market=w.levels[4];int disabled=0;
        if(removeDuplicates)
            foreach(var group in market.GetComponentsInChildren<PickupItem>(true).GroupBy(p=>market.transform.InverseTransformPoint(p.transform.position).ToString("F4")))
                foreach(var item in group.Skip(1)){item.gameObject.SetActive(false);disabled++;}
        typeof(LevelWorld).GetMethod("Activate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(w,new object[]{w.levels[3]});
        w.player.Teleport(w.connections[3].source.transform.TransformPoint(new Vector3(0,1.1f,0)));
        await Stage();
        Log(new {kind="experiment",run=SessionState.GetString("WeddingJourney.Run",""),disabled,held=w.player.GetComponent<PlayerPickup>().HeldItem!=null});
        return new {disabled,level=w.CurrentLevel.name,ready=true};
    }
    public static async Task<object> DriveBoss(float seconds)
    {
        var boss=Object.FindAnyObjectByType<LevelArenaBoss>();var p=World.player;var health=p.GetComponent<PlayerVitality>();
        var room=boss.level.FindRoom(boss.arenaRoomId);
        var center=boss.level.transform.TransformPoint(new Vector3(room.center.x,0,room.center.y));
        var keyboard=UnityEngine.InputSystem.Keyboard.current;var settings=UnityEngine.InputSystem.InputSystem.settings;
        var bg=settings.backgroundBehavior;var ed=settings.editorInputBehaviorInPlayMode;
        settings.backgroundBehavior=UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode=UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        double end=EditorApplication.timeSinceStartup+seconds;string previous="";
        try
        {
            while(EditorApplication.timeSinceStartup<end)
            {
                string state=boss.State+"/"+boss.CurrentSkill+"/"+health.Health;
                if(state!=previous){Log(new {kind="boss",frame=Time.frameCount,time=Time.time,state,boss.RemainingSeconds,position=p.transform.position.ToString()});previous=state;}
                if(boss.State==LevelArenaBoss.EncounterState.Won||health.IsDead)break;
                if(!p.InputSuspended)
                {
                    var delta=p.transform.position-center;
                    float angle=Mathf.Atan2(delta.z,delta.x)+.38f;
                    var target=center+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*12;
                    p.FaceDirection(target-p.transform.position);
                    bool jump=false;
                    var waves=(System.Collections.IEnumerable)typeof(LevelArenaBoss).GetField("waves",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(boss);
                    foreach(var wave in waves)
                    {
                        var wt=wave.GetType();var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                        var origin=(Vector2)wt.GetField("origin",flags).GetValue(wave);
                        var forward=(Vector2)wt.GetField("forward",flags).GetValue(wave);
                        var settingsWave=(BossWaveSettings)wt.GetField("settings",flags).GetValue(wave);
                        float travelled=(float)wt.GetField("distance",flags).GetValue(wave);
                        Vector2 local=boss.level.Local(p.transform.position)-room.center-origin;
                        float gap=(forward.sqrMagnitude>.5f?Vector2.Dot(local,forward):local.magnitude)-travelled;
                        if(gap>-.8f && gap<settingsWave.speed*.28f+settingsWave.thickness*.5f+.4f)jump=true;
                    }
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,jump
                        ?new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W,UnityEngine.InputSystem.Key.Space)
                        :new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
                }
                await Task.Delay(30);
            }
        }
        finally
        {
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());
            settings.backgroundBehavior=bg;settings.editorInputBehaviorInPlayMode=ed;
        }
        return new {boss.State,boss.RemainingSeconds,health.Health,health.IsDead};
    }
}

public sealed class JourneyCollisionProbe : MonoBehaviour
{
    void OnCollisionEnter(Collision c)
    {
        float speed=CollisionSonarEmitter.ImpactSpeed(c);
        if(speed<.5f)return;
        var level=GetComponentInParent<LevelLayout>();
        File.AppendAllText("Logs/WeddingJourney/contacts.jsonl",JsonConvert.SerializeObject(new {
            frame=Time.frameCount,time=Time.time,name,other=c.collider.name,otherParent=c.collider.transform.parent?.name,
            speed,relative=c.relativeVelocity.ToString(),impulse=c.impulse.ToString(),position=transform.position.ToString(),
            contact=c.contactCount>0?c.GetContact(0).point.ToString():"",preparing=level!=null&&level.IsPreparing})+"\n");
    }
}
