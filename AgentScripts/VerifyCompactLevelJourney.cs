using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using Sound2.LevelEditing;
using Object=UnityEngine.Object;

public static class VerifyCompactLevelJourney
{
    const string Folder="Logs/LevelSerialization";
    static async Task Until(Func<bool> condition,string message)
    {
        double deadline=EditorApplication.timeSinceStartup+150;
        while(!condition()) {if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Timeout: "+message);await Task.Delay(30);}
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static async Task<object> Run()
    {
        Check(Application.isPlaying,"Requires Play Mode");
        int frame=Time.frameCount;await Until(()=>Time.frameCount>=frame+3,"frames advancing");
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();if(menu!=null)menu.BeginGame();
        await Until(()=>!SonicSpaceStartMenu.BlocksGameplay,"start menu");
        var w=Object.FindAnyObjectByType<LevelWorld>();var player=w.player;
        Check(w.CurrentLevel==w.startingLevel,"Correct starting level");
        var results=new List<object>();PickupItem held=null;
        try
        {
            for(int i=0;i<w.connections.Count;i++)
            {
                var c=w.connections[i];var source=c.source;var destination=c.destination;
                File.WriteAllText(Folder+"/journey-progress.txt",i+": "+source.Level.name+" -> "+destination.Level.name);
                Check(source.Level==w.CurrentLevel,"Departure belongs to current level");
                player.SetInputCaptured(true);
                if(source.DoorOpen<.999f)source.Use(player.GetComponent<PlayerInteraction>(),false);
                await Until(()=>source.DoorOpen>.999f,"departure door");
                player.Teleport(source.transform.TransformPoint(new Vector3(0,1.1f,0)));
                {
                    var ball=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"));
                    ball.transform.position=player.GetComponentInChildren<Camera>().transform.position+player.transform.forward*.6f;
                    Physics.SyncTransforms();held=ball.GetComponent<PickupItem>();
                    Check(player.GetComponent<PlayerPickup>().TryPickUp(held),"Pick up cargo before travel");
                }
                var loose=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupBouncyBall.prefab"));
                loose.transform.SetParent(source.Level.transform.Find("Props"),false);
                loose.transform.position=source.transform.TransformPoint(new Vector3(.9f,1,0));
                loose.GetComponent<Rigidbody>().isKinematic=true;Physics.SyncTransforms();
                var loosePosition=loose.transform.position;
                if(i==0)
                {
                    source.Use(player.GetComponent<PlayerInteraction>(),true);Check(w.Travelling,"Cancellation test starts");
                    w.enabled=false;
                    Check(!w.Travelling&&player.GetComponent<PlayerPickup>().HeldItem==held,"Cancelled transfer keeps held item");
                    Check(loose.transform.position==loosePosition,"Cancelled transfer keeps loose object");
                    w.enabled=true;await Until(()=>source.DoorOpen>.999f,"cancelled departure reopens");
                }
                source.Use(player.GetComponent<PlayerInteraction>(),true);
                Check(w.Travelling,"Inside button starts travel");
                await Until(()=>!w.Travelling,"travel finishes");
                Check(w.CurrentLevel==destination.Level&&w.ArrivalCheckpoint==destination,"Correct destination and checkpoint");
                Check(destination.Contains(player.transform.position)&&destination.DoorOpen>.999f&&!player.TransportLocked,"Arrival position, door and input");
                Check(player.GetComponent<PlayerPickup>().HeldItem==null&&held==null,"Committed transfer clears held item");
                Check(loose.transform.IsChildOf(source.Level.transform)&&loose.transform.position==loosePosition&&!loose.activeInHierarchy,"Loose item stays in departure level");
                Object.Destroy(loose);
                Check(w.levels.Count(l=>l.gameObject.activeSelf)==1,"Only current level active");
                foreach(var route in w.connections)Check(w.TryDestination(route.source,out var d,out _)&&d==route.destination,"All links remain valid");
                results.Add(new{index=i,source=source.Level.name,destination=destination.Level.name,frame=Time.frameCount,heldItemCleared=true,looseItemLeftBehind=true});
                File.WriteAllText(Folder+"/journey.json",Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));
            }
            player.GetComponent<PlayerPickup>().ClearForRespawn();held=null;
            var previous=w.CurrentLevel;bool resetDone=false;
            IEnumerator Reset(){yield return w.ResetCurrentLevel();resetDone=true;}
            w.StartCoroutine(Reset());await Until(()=>resetDone,"level reset");
            Check(w.CurrentLevel!=previous&&w.ArrivalCheckpoint.Level==w.CurrentLevel,"Reset remaps arrival checkpoint");
            foreach(var route in w.connections)Check(w.TryDestination(route.source,out var d,out _)&&d==route.destination,"Reset preserves every connection");
            ScreenCapture.CaptureScreenshot(Folder+"/journey-finished.png");
            File.WriteAllText(Folder+"/runtime-summary.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{passed=true,transitions=results.Count,reset=true,itemsDoNotCross=true,cancellationKeepsItems=true,firstFrame=frame,lastFrame=Time.frameCount},Newtonsoft.Json.Formatting.Indented));
            return new{passed=true,transitions=results.Count,reset=true,itemsDoNotCross=true,cancellationKeepsItems=true,firstFrame=frame,lastFrame=Time.frameCount};
        }
        finally {if(held!=null)player.GetComponent<PlayerPickup>().ClearForRespawn();}
    }
}
