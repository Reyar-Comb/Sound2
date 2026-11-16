using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Sound2.LevelEditing;

public static class VerifyMergedBossGate
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static async Task<object> Run()
    {
        Check(Application.isPlaying,"Play mode required");
        var world=UnityEngine.Object.FindAnyObjectByType<LevelWorld>();var level=world.CurrentLevel;
        var gate=level.GetComponentInChildren<BossMemoryGate>(true);var boss=level.GetComponentInChildren<LevelArenaBoss>(true);
        Check(gate!=null&&boss!=null&&boss.State==LevelArenaBoss.EncounterState.Waiting,"Run after the seven-route journey");
        var type=typeof(StoryCollectible).Assembly.GetType("StoryRunProgress");
        var progress=(ICollectibleProgress)type.GetField("Current",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        var actor=world.player.GetComponent<PlayerInteraction>();
        Check(!gate.Ready,"Gate initially waits for memories");
        foreach(var m in gate.memories)progress.TryCollect(m.id);
        for(int i=0;i<gate.memories.Length;i++)Check(gate.TryInteract(actor),"Place memory "+i);
        Check(gate.Ready&&gate.PlacedCount==6&&gate.displayRoot.childCount==6,"Six memory displays");
        Check(gate.returnButton.TryInteract(actor),"Return button interaction");
        Check(world.CurrentLevel==world.startingLevel&&world.ArrivalCheckpoint==null,"Return reaches starting level");
        Check(gate.memories.All(m=>progress.IsCollected(m.id)),"Return retains collected story IDs");
        // Restore only this test session to the arena to exercise its start button as well.
        typeof(LevelWorld).GetMethod("Activate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(world,new object[]{level});
        world.player.Teleport(gate.transform.position+Vector3.up*1.2f);
        Check(gate.startButton.TryInteract(actor)&&boss.State==LevelArenaBoss.EncounterState.Intro,"Start button begins intro");
        int first=Time.frameCount;double until=EditorApplication.timeSinceStartup+45;
        while(boss.State!=LevelArenaBoss.EncounterState.Fighting)
        {Check(EditorApplication.timeSinceStartup<until,"Boss intro timed out: "+boss.State);await Task.Delay(50);}
        Check(Time.frameCount>first+3,"Frames advancing");
        var result=new{passed=true,memories=6,returnToStart=true,storyProgressRetained=true,startButton=true,state=boss.State.ToString(),firstFrame=first,lastFrame=Time.frameCount};
        File.WriteAllText("Logs/RemoteMerge/boss-runtime.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        return result;
    }
}
