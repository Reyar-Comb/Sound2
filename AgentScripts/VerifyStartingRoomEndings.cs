using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Sound2.LevelEditing;
using Object = UnityEngine.Object;

public static class VerifyStartingRoomEndings
{
    static readonly List<string> checks = new();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
    static async Task Until(Func<bool> predicate, int timeout = 30000)
    {
        double end = EditorApplication.timeSinceStartup + timeout / 1000.0;
        while (!predicate()) { if (EditorApplication.timeSinceStartup > end) throw new Exception("Timed out after: " + checks.LastOrDefault()); await Task.Delay(25); }
    }
    static void Activate(LevelWorld world, LevelLayout level) => typeof(LevelWorld)
        .GetMethod("Activate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(world,new object[]{level});
    static StartingRoomBed Bed(LevelWorld world) => world.startingLevel.GetComponentInChildren<StartingRoomBed>(true);
    static StartingRoomWindowVines Window(LevelWorld world) => world.startingLevel.GetComponentInChildren<StartingRoomWindowVines>(true);

    public static async Task<object> InspectWindow()
    {
        var opening=Object.FindAnyObjectByType<SoundShellOpening>();
        opening.ReturnToRoom();
        await Until(()=>opening.CurrentPhase==SoundShellOpening.Phase.Room);
        var window=Window(opening.world); var player=opening.player;
        Check(window.IsOvergrown&&Bed(opening.world).IsUnlocked,"Hanged Man return retains both progression flags");
        player.Teleport(opening.startingRoom.transform.TransformPoint(new Vector3(0,1.05f,-1.5f)));
        typeof(PlayerController).GetMethod("FacePoint",BindingFlags.Instance|BindingFlags.NonPublic)
            .Invoke(player,new object[]{window.transform.TransformPoint(window.windowCenter)});
        await Until(()=>window.Vines!=null&&window.Vines.PointCount>30);
        await Task.Delay(1500);
        Check(!player.InputSuspended,"Hanged Man return restores input");
        return new {passed=true,window.Vines.PointCount,window.IsOvergrown,bed=Bed(opening.world).IsUnlocked};
    }

    public static async Task<object> Run()
    {
        if (!Application.isPlaying) throw new Exception("Run in Play Mode.");
        checks.Clear();
        int firstFrame = Time.frameCount;
        await Until(()=>Time.frameCount > firstFrame + 2);
        Check(true,"Player loop advances");
        var opening=Object.FindAnyObjectByType<SoundShellOpening>();
        var world=opening.world; var player=world.player;
        var actor=player.GetComponent<PlayerInteraction>(); var vitality=player.GetComponent<PlayerVitality>();
        var traversal=player.GetComponent<PlayerFloorProgression>();
        var feedback=player.GetComponent<PlayerDamageFeedback>();
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if(menu!=null&&!menu.HasStarted) { menu.BeginGame(); await Until(()=>menu.HasStarted&&!player.InputSuspended); }
        feedback.engulfSeconds=feedback.dissolveSeconds=feedback.wakeSeconds=.1f;
        feedback.deathHoldSeconds=feedback.whiteHoldSeconds=0;
        Check(!Bed(world).IsUnlocked,"Bed locked at new run");
        Check(Bed(world).GetComponentsInChildren<Renderer>(true).All(r=>!r.enabled),"Initial bed invisible");
        Check(Bed(world).GetComponentsInChildren<Collider>(true).All(c=>!c.enabled),"Initial bed has no collision");
        Check(!Bed(world).TryInteract(actor),"Initial bed interaction rejected");
        Check(!Window(world).IsOvergrown&&Window(world).Vines==null,"Window initially clear");
        Check(vitality.TakeLethalDamage(),"Pre-Boss death accepted");
        await Until(()=>!traversal.IsRespawning&&!vitality.IsDead);
        Check(!Bed(world).IsUnlocked&&!Window(world).IsOvergrown,"Pre-Boss death does not unlock endings");
        Check(opening.WindowCard==opening.foolCard,"Pre-Boss jump selects Fool");
        player.Teleport(world.startingLevel.transform.TransformPoint(new Vector3(0,-2,-7)));
        await Until(()=>opening.CurrentPhase==SoundShellOpening.Phase.AwaitingReturn);
        Check(opening.CurrentCard==opening.foolCard&&player.InputSuspended,"Fool card animation and input lock");
        opening.ReturnToRoom();
        await Until(()=>opening.CurrentPhase==SoundShellOpening.Phase.Room);
        Check(!player.InputSuspended&&world.CurrentLevel==world.startingLevel,"Fool return restores controls and room");

        var bossLevel=world.levels.Single(l=>l.GetComponentInChildren<LevelArenaBoss>(true)!=null);
        // Level activation uses the same committed arrival path as elevator transport;
        // no transport, navigation or content is changed for this ending test.
        Activate(world,bossLevel);
        var gate=bossLevel.GetComponentInChildren<BossMemoryGate>(true);
        player.Teleport(gate.transform.position+Vector3.up*1.2f);
        await Until(()=>bossLevel.GetComponentInChildren<LevelArenaBoss>().State==LevelArenaBoss.EncounterState.Waiting);
        Check(Bed(world).IsUnlocked,"Entering Boss level unlocks bed before starting encounter");
        Check(gate.returnButton.TryInteract(actor),"Existing return button works before boss start");
        Check(Bed(world).GetComponentsInChildren<Renderer>(true).All(r=>r.enabled),"Returning reveals bed");
        Check(Bed(world).GetComponentsInChildren<Collider>(true).All(c=>c.enabled),"Returning enables bed collision");
        Check(!Window(world).IsOvergrown&&opening.WindowCard==opening.foolCard,"Boss visit alone keeps Fool and clear window");
        Check(Bed(world).TryInteract(actor),"Unlocked bed interaction starts Hermit");
        Check(!Bed(world).TryInteract(actor),"Repeated click cannot restart ending");
        await Until(()=>opening.CurrentPhase==SoundShellOpening.Phase.AwaitingReturn);
        Check(opening.CurrentCard==opening.hermitCard,"Hermit uses shared card sequence");
        opening.ReturnToRoom();
        await Until(()=>opening.CurrentPhase==SoundShellOpening.Phase.Room);
        Check(Bed(world).IsUnlocked&&Bed(world).CanInteract(actor),"Hermit return and level clone retain bed unlock");

        Activate(world,bossLevel); player.Teleport(gate.transform.position+Vector3.up*1.2f);
        var progressType=typeof(StoryCollectible).Assembly.GetType("StoryRunProgress");
        var progress=(ICollectibleProgress)progressType.GetField("Current",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
        foreach(var memory in gate.memories)progress.TryCollect(memory.id);
        for(int i=0;i<gate.memories.Length;i++)Check(gate.TryInteract(actor),"Place Boss memory "+i);
        Check(gate.Ready,"Boss start prerequisites intact");
        var boss=bossLevel.GetComponentInChildren<LevelArenaBoss>();
        boss.battleMusic=null; boss.introBlackoutTime=0; boss.revealWithoutMusic=.05f; boss.beforeCombatSeconds=.05f;
        boss.betweenSkills=100; // Keep test death deterministic rather than waiting for a random attack.
        Check(gate.startButton.TryInteract(actor),"Start Boss through existing button");
        await Until(()=>boss.State==LevelArenaBoss.EncounterState.Fighting&&!player.InputSuspended);
        Check(!Window(world).IsOvergrown,"Starting boss without death keeps window clear");
        vitality.RestoreHealth();
        Check(vitality.TakeLethalDamage(),"Post-start death accepted");
        Check(Window(world).IsOvergrown,"Death records Hanged Man eligibility before level reset");
        await Until(()=>!traversal.IsRespawning&&!vitality.IsDead);
        bossLevel=world.CurrentLevel; gate=bossLevel.GetComponentInChildren<BossMemoryGate>();
        Check(gate.returnButton.TryInteract(actor),"Return button works after Boss death and respawn");
        var window=Window(world);
        await Until(()=>window.Vines!=null&&window.Vines.PointCount>20);
        Check(Bed(world).GetComponentInChildren<MonsterVines>(true)==null,"No red vines on bed");
        Check(window.Vines.GetComponent<MonsterVineArea>().contactDamageRadius==0,"Window vines cause no contact damage");
        var supports=window.Vines.GetComponentsInChildren<Collider>(true);
        Check(supports.Length==1&&supports.All(c=>c.isTrigger),"Window aperture has only nonblocking visual support");
        var points=new Vector4[MonsterVines.Capacity];
        await Task.Delay(800);
        int count=window.Vines.Collect(points);
        Check(count>20,"Same monster-vine renderer receives live red points");
        Check(points.Take(count).Any(p=>{
            var local=window.transform.InverseTransformPoint(new Vector3(p.x,p.y,p.z))-window.windowCenter;
            return Mathf.Abs(local.x)<1.5f&&Mathf.Abs(local.y)<1.2f;
        }),"Vines cover the open center of the window");
        Check(opening.WindowCard==opening.hangedManCard,"Post-start death switches window card to Hanged Man");
        player.Teleport(world.startingLevel.transform.TransformPoint(new Vector3(0,-2,-7)));
        await Until(()=>opening.CurrentPhase==SoundShellOpening.Phase.AwaitingReturn);
        Check(opening.CurrentCard==opening.hangedManCard,"Actual fall shows Hanged Man instead of Fool");
        var report=new {passed=true,checks=checks.ToArray(),firstFrame,lastFrame=Time.frameCount,vinePoints=count};
        Directory.CreateDirectory("Logs/StartingRoomEndings");
        File.WriteAllText("Logs/StartingRoomEndings/runtime.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return report;
    }
}
