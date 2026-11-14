using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object=UnityEngine.Object;

public static class VerifyEightFloorAdventure
{
    static readonly List<string> checks=new();
    static void Check(bool ok,string message){checks.Add((ok?"PASS ":"FAIL ")+message);}
    static async Task Tick(int ms=100){int frame=Time.frameCount;await Task.Delay(ms);if(Time.frameCount<=frame)throw new Exception("Game frames stopped");}
    public static object Edit()
    {
        checks.Clear();var map=Object.FindAnyObjectByType<EightFloorAdventure>();
        Check(map.floors.Length==8 && map.elevators.Length==7,"Eight floors / seven unique adjacent lifts");
        Check(Object.FindObjectsByType<LevelMap>().Length==0 && Object.FindObjectsByType<FloorShaftTransition>().Length==0,"No legacy map or falling transitions in BaiYu-Test");
        var meshPaths=new HashSet<string>();
        foreach(var f in map.floors)
        {
            var layout=f.layout;var degree=layout.rooms.ToDictionary(r=>r.id,r=>0);var visited=new HashSet<string>{layout.rooms[0].id};
            var edges=layout.corridors.Where(c=>layout.FindRoom(c.start.roomId)!=null && layout.FindRoom(c.end.roomId)!=null).ToArray();
            foreach(var c in edges){degree[c.start.roomId]++;degree[c.end.roomId]++;}
            for(int k=0;k<layout.rooms.Count;k++)foreach(var c in edges)
            {if(visited.Contains(c.start.roomId))visited.Add(c.end.roomId);if(visited.Contains(c.end.roomId))visited.Add(c.start.roomId);}
            Check(degree.Values.All(d=>d>=(layout.architecturalPartitions?1:2)) && visited.Count==layout.rooms.Count,$"F{f.number}: every room connected; private rooms allow a single entrance");
            Check(edges.Length-layout.rooms.Count+1>=3,$"F{f.number}: multiple independent loops");
            Check(layout.pits.Count==0,$"F{f.number}: solid walking floor, no pits");
            Check(meshPaths.Add(layout.geometryAsset),$"F{f.number}: owns separate persisted mesh asset");
            Check(f.GetComponentsInChildren<PickupItem>(true).Length>=8,$"F{f.number}: themed physical interactables");
            LevelGeometry.Validate(layout,LevelGeometry.Footprint(layout));
        }
        foreach(var e in map.elevators)
        {
            var destination=map.FindFloor(e.destinationFloor);
            Vector3 arrival=destination.layout.World(destination.entryDock);
            Check(Vector3.Distance(arrival,e.transform.position+Vector3.up*e.rise)<.01f,$"E{e.sourceFloor}: landings align vertically");
        }
        Directory.CreateDirectory("Logs/EightFloorAdventure");
        File.WriteAllLines("Logs/EightFloorAdventure/edit-checks.txt",checks);
        return new{passed=checks.All(s=>s.StartsWith("PASS")),checks=checks.ToArray()};
    }

    public static async Task<object> Play()
    {
        checks.Clear();var errors=new List<string>();
        Application.LogCallback log=(m,s,t)=>{if(t==LogType.Error||t==LogType.Exception||t==LogType.Assert)errors.Add(m);};
        Application.logMessageReceived+=log;
        var map=Object.FindAnyObjectByType<EightFloorAdventure>();var p=map.player;var controller=p.GetComponent<PlayerController>();
        var interaction=p.GetComponent<PlayerInteraction>();var capsule=p.GetComponent<CharacterController>();
        int changed=0;p.FloorChanged+=Count;
        void Count(FloorRule r)=>changed++;
        try
        {
            var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();
            if(menu!=null && !menu.HasStarted){while(!menu.IntroReady)await Tick();menu.BeginGame();while(!menu.HasStarted)await Tick();}
            controller.SetInputCaptured(true);await Tick(300);
            Check(p.CurrentFloor==1 && p.Map==null && p.Adventure==map,"Start at floor 1 with ascending progression");
            for(int n=1;n<=8;n++)
            {
                var floor=map.FindFloor(n);await Tick(200);
                var nav=floor.encounters.GetComponent<RuntimePatrolNavigation>();
                Check(nav.Ready,$"F{n}: runtime navigation built");
                var path=new NavMeshPath();
                bool routes=true;
                Vector3 start=floor.layout.World(floor.layout.rooms[0].center,.1f);
                foreach(var room in floor.layout.rooms)
                {
                    Vector3 target=floor.layout.World(room.center,.1f);
                    bool found=NavMesh.SamplePosition(start,out var a,2,NavMesh.AllAreas) && NavMesh.SamplePosition(target,out var b,2,NavMesh.AllAreas);
                    if(!found || !NavMesh.CalculatePath(start,target,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete){routes=false;checks.Add("DETAIL blocked room "+n+" "+room.name);}
                }
                Check(routes,$"F{n}: all rooms reachable with furniture present");
                Check(p.GetComponent<PlayerSubtitles>().FloorText.text=="F"+n,$"F{n}: floor HUD updated");
                Check(Object.FindObjectsByType<RuntimePatrolNavigation>().Length==1,$"F{n}: only current encounters active");
                if(n==8)break;
                var lift=map.elevators[n-1];
                Check(!p.TryAscend(lift),$"E{n}: cannot skip ride with a floor-change call");
                controller.Teleport(lift.transform.TransformPoint(new Vector3(0,1.05f,4.5f)));
                if(n==1)await View(controller,lift.transform.TransformPoint(new Vector3(0,1.05f,7)),lift.transform.eulerAngles.y+180,0,"elevator-exterior.png");
                controller.Teleport(lift.transform.TransformPoint(new Vector3(0,1.05f,4.5f)));
                var outside=lift.GetComponentsInChildren<ElevatorButton>().First(b=>!b.inside);
                var inside=lift.GetComponentsInChildren<ElevatorButton>().First(b=>b.inside);
                if(n==1)
                {
                    await Aim(controller,outside.InteractionPosition);
                    Check(interaction.FocusedTarget==outside && interaction.TryInteractFocused(),"E1: exterior button reachable through production focus / occlusion / click path");
                    if(lift.State!=OneWayElevator.LiftState.Boarding)throw new Exception("Cannot focus exterior elevator button");
                }
                else Check(outside.TryInteract(interaction),$"E{n}: exterior button opens doors");
                while(lift.DoorOpen<.999f)await Tick();
                Check(!inside.TryInteract(interaction),$"E{n}: rejects ascent while player outside cabin");
                // Walk the real capsule across the generated doorway rather than teleporting through its wall.
                bool walked=true;
                controller.TransportLocked=true;
                for(int k=0;k<28;k++){capsule.Move(lift.transform.forward*-.15f);await Tick(20);}
                controller.TransportLocked=false;
                walked=lift.Contains(p.transform.position);
                Check(walked,$"E{n}: capsule walks through real doorway into cabin");
                await Tick();Check(!p.GetComponent<PlayerSubtitles>().FloorText.enabled,$"E{n}: floor HUD hidden inside cabin");
                if(!walked)throw new Exception("Elevator doorway obstructed: "+n+" position "+lift.transform.InverseTransformPoint(p.transform.position));
                if(n==1)await View(controller,lift.cabin.TransformPoint(new Vector3(0,1.05f,1)),lift.transform.eulerAngles.y+180,9,"elevator-interior.png");
                PickupItem held=null,loose=null;
                if(n==1)
                {
                    var objects=floor.GetComponentsInChildren<PickupItem>().Where(item=>item.CanPickUp).Take(2).ToArray();held=objects[0];loose=objects[1];
                    held.transform.position=lift.cabin.TransformPoint(new Vector3(.5f,.3f,0));
                    held.GetComponent<Rigidbody>().position=held.transform.position;Physics.SyncTransforms();await Tick(200);
                    Check(p.GetComponent<PlayerPickup>().TryPickUp(held),"E1: picks up item before riding");
                    loose.transform.position=lift.cabin.TransformPoint(new Vector3(-1,.4f,-1));loose.GetComponent<Rigidbody>().position=loose.transform.position;Physics.SyncTransforms();await Tick(150);
                }
                if(n==1)
                {
                    await Aim(controller,inside.InteractionPosition);
                    Check(interaction.FocusedTarget==inside && interaction.TryInteractFocused(),"E1: focus and click ascent button while holding an item");
                    if(lift.State!=OneWayElevator.LiftState.Closing)throw new Exception("Cannot focus internal elevator button");
                }
                else Check(inside.TryInteract(interaction),$"E{n}: internal button starts ascent");
                float before=p.transform.position.y;bool moving=false;float end=Time.realtimeSinceStartup+25;
                while(lift.State!=OneWayElevator.LiftState.Arrived && Time.realtimeSinceStartup<end)
                {await Tick();if(p.transform.position.y>before+.5f && p.transform.position.y<before+lift.rise-.5f)moving=true;}
                Check(moving && lift.PermanentlyOpen && p.CurrentFloor==n+1,$"E{n}: continuous physical ascent, one level, doors stay open");
                Check(!inside.TryInteract(interaction) && !outside.TryInteract(interaction) && !p.TryAscend(lift),$"E{n}: no second ascent, reversal, or replay");
                Check(!controller.TransportLocked,$"E{n}: control restored after arrival");
                if(n==1)
                {
                    Check(held!=null && held.IsHeld,"E1: held item survives ascent");
                    Check(loose!=null && loose.transform.IsChildOf(map.FindFloor(2).transform) && !loose.GetComponent<Rigidbody>().isKinematic,"E1: loose cargo arrives on upper floor with physics restored");
                    await View(controller,lift.cabin.TransformPoint(new Vector3(0,1.05f,1)),lift.transform.eulerAngles.y+180,15,"elevator-arrival-cargo.png");
                    Check(p.GetComponent<PlayerPickup>().TryPlace() && held.transform.IsChildOf(map.FindFloor(2).transform),"E1: carried item releases into destination floor");
                }
                controller.TransportLocked=true;
                for(int k=0;k<35;k++){capsule.Move(lift.transform.forward*.15f);await Tick(20);}
                controller.TransportLocked=false;
                await Tick(300);
                Check(Mathf.Abs(p.transform.position.y-map.FindFloor(n+1).transform.position.y-1)<.4f,$"E{n}: exits to supported upper floor without falling");
                if(p.CurrentFloor!=n+1)throw new Exception("Ride did not finish");
            }
            Check(changed==7 && p.CurrentFloor==8,"Exactly seven floor changes, final floor 8");
            await Tick(1500);Check(map.elevators.All(e=>e.PermanentlyOpen),"All seven arrival doors remain permanently open");
        }
        catch(Exception e){errors.Add(e.ToString());}
        finally{p.FloorChanged-=Count;Application.logMessageReceived-=log;}
        Directory.CreateDirectory("Logs/EightFloorAdventure");File.WriteAllLines("Logs/EightFloorAdventure/play-checks.txt",checks.Concat(errors));
        return new{passed=errors.Count==0 && checks.All(s=>s.StartsWith("PASS")),checks=checks.ToArray(),errors,currentFloor=p.CurrentFloor};
    }

    static async Task View(PlayerController player,Vector3 position,float yaw,float pitch,string name)
    {
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var look=(UnityEngine.InputSystem.InputActionReference)typeof(PlayerController).GetField("lookAction",flags).GetValue(player);
        bool wasEnabled=look.action.enabled;look.action.Disable();
        player.Teleport(position);player.TransportLocked=true;player.SetInputCaptured(true);
        typeof(PlayerController).GetField("yaw",flags).SetValue(player,yaw);
        typeof(PlayerController).GetField("pitch",flags).SetValue(player,pitch);
        try
        {
            await Tick(350);Directory.CreateDirectory("Logs/EightFloorAdventure");
            ScreenCapture.CaptureScreenshot("Logs/EightFloorAdventure/"+name);await Tick(250);
        }
        finally{player.TransportLocked=false;if(wasEnabled)look.action.Enable();}
    }
    static async Task Aim(PlayerController player,Vector3 point)
    {
        var view=player.GetComponentInChildren<Camera>();var d=point-view.transform.position;
        var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var look=(UnityEngine.InputSystem.InputActionReference)typeof(PlayerController).GetField("lookAction",flags).GetValue(player);
        bool enabled=look.action.enabled;look.action.Disable();player.TransportLocked=true;
        typeof(PlayerController).GetField("yaw",flags).SetValue(player,Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg);
        typeof(PlayerController).GetField("pitch",flags).SetValue(player,-Mathf.Atan2(d.y,new Vector2(d.x,d.z).magnitude)*Mathf.Rad2Deg);
        await Tick(500);player.TransportLocked=false;if(enabled)look.action.Enable();
    }

    public static object Overviews()
    {
        if(Application.isPlaying)throw new Exception("Edit mode only");
        var map=Object.FindAnyObjectByType<EightFloorAdventure>();Directory.CreateDirectory("Logs/EightFloorAdventure");
        var go=new GameObject("Temporary overview camera",typeof(Camera));var cam=go.GetComponent<Camera>();
        cam.orthographic=true;cam.orthographicSize=57;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.045f,.065f,.08f);
        cam.nearClipPlane=.1f;cam.farClipPlane=200;cam.transform.rotation=Quaternion.Euler(90,0,0);
        var enabled=map.floors.Select(f=>f.gameObject.activeSelf).ToArray();
        try
        {
            foreach(var f in map.floors)
            {
                foreach(var other in map.floors)other.gameObject.SetActive(other==f);
                foreach(var e in map.elevators)e.gameObject.SetActive(false);
                var bounds=f.layout.generatedRoot.transform.Find("Floor").GetComponent<Renderer>().bounds;
                cam.orthographicSize=Mathf.Max(bounds.size.z*.5f,bounds.size.x*.55f)+4;
                cam.transform.position=bounds.center+Vector3.up*100;
                Capture(cam,"Logs/EightFloorAdventure/F"+f.number+"-layout.png",1000,1100);
            }
        }
        finally{for(int i=0;i<map.floors.Length;i++)map.floors[i].gameObject.SetActive(enabled[i]);foreach(var e in map.elevators)e.gameObject.SetActive(true);Object.DestroyImmediate(go);}
        return "Saved eight layout overviews";
    }
    public static object Persistence()
    {
        if(Application.isPlaying)throw new Exception("Edit mode only");checks.Clear();
        var map=Object.FindAnyObjectByType<EightFloorAdventure>();var scene=map.gameObject.scene;
        int totalProps=map.floors.Sum(floor=>floor.transform.Find("Props").childCount);
        var f=map.floors[0];int props=f.transform.Find("Props").childCount;float height=f.layout.wallHeight;
        Undo.IncrementCurrentGroup();Undo.RecordObject(f.layout,"Verify room editing");f.layout.wallHeight+=.2f;
        EditorUtility.SetDirty(f.layout);Undo.FlushUndoRecordObjects();LevelBuilder.Rebuild(f.layout);Undo.PerformUndo();LevelBuilder.Rebuild(f.layout);
        Check(f.layout.wallHeight==height && f.transform.Find("Props").childCount==props,"Layout editing / undo / geometry rebuild preserves props and original layout");
        foreach(var floor in map.floors)LevelBuilder.Persist(floor.layout,scene.path);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        const string copy="Assets/Scenes/EightFloorVerification.unity";
        if(AssetDatabase.LoadAssetAtPath<SceneAsset>(copy)!=null)throw new Exception("Temporary export already exists");
        try
        {
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,copy,true);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(copy);
            var exported=Object.FindAnyObjectByType<EightFloorAdventure>();
            Check(exported.floors.Length==8 && exported.elevators.Length==7 && exported.elevators.All(e=>e.adventure==exported),"Full scene export retains all eight floors and elevator references");
            Check(exported.floors.All(floor=>floor.layout.generatedRoot.GetComponentInChildren<MeshFilter>().sharedMesh!=null),"Export reload retains persistent geometry");
            Check(exported.floors.SelectMany(floor=>floor.transform.Find("Props").Cast<Transform>()).All(t=>PrefabUtility.IsPartOfPrefabInstance(t.gameObject)),"All furniture preserves Prefab links after export reload");
        }
        finally{UnityEditor.SceneManagement.EditorSceneManager.OpenScene(AdventureMapBuilder.ScenePath);AssetDatabase.DeleteAsset(copy);}
        map=Object.FindAnyObjectByType<EightFloorAdventure>();
        Check(map.floors.Sum(floor=>floor.transform.Find("Props").childCount)==totalProps,$"Original scene reload retains {totalProps} furniture / prop instances");
        var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/LevelEditor/Materials/Adventure/ElevatorPaper.mat");
        material.SetColor("_BaseColor",Color.white);EditorUtility.SetDirty(material);AssetDatabase.SaveAssetIfDirty(material);
        var dependencies=new HashSet<string>();
        foreach(var guid in AssetDatabase.FindAssets("t:Scene"))foreach(var path in AssetDatabase.GetDependencies(AssetDatabase.GUIDToAssetPath(guid),true))dependencies.Add(path);
        foreach(var path in AssetDatabase.FindAssets("t:LevelMeshStore",new[]{"Assets/Scenes/BaiYu-Test_Geometry"}).Select(AssetDatabase.GUIDToAssetPath))
            if(!dependencies.Contains(path))AssetDatabase.DeleteAsset(path);
        AdventureMapBuilder.ShowFloor(map,1);
        Directory.CreateDirectory("Logs/EightFloorAdventure");File.WriteAllLines("Logs/EightFloorAdventure/persistence-checks.txt",checks);
        return new{passed=checks.All(s=>s.StartsWith("PASS")),checks=checks.ToArray()};
    }

    public static async Task<object> Visuals()
    {
        var map=Object.FindAnyObjectByType<EightFloorAdventure>();var p=map.player.GetComponent<PlayerController>();
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();
        if(menu!=null && !menu.HasStarted){while(!menu.IntroReady)await Tick();menu.BeginGame();while(!menu.HasStarted)await Tick();}
        var lift=map.elevators[0];var interaction=p.GetComponent<PlayerInteraction>();
        await View(p,lift.transform.TransformPoint(new Vector3(0,1.05f,7)),lift.transform.eulerAngles.y+180,0,"elevator-exterior.png");
        p.Teleport(lift.transform.TransformPoint(new Vector3(0,1.05f,4.5f)));
        lift.GetComponentsInChildren<ElevatorButton>().First(b=>!b.inside).TryInteract(interaction);
        while(lift.DoorOpen<.999f)await Tick();
        await View(p,lift.cabin.TransformPoint(new Vector3(0,1.05f,1)),lift.transform.eulerAngles.y+180,16,"elevator-interior.png");
        bool floorHidden=!p.GetComponent<PlayerSubtitles>().FloorText.enabled;
        var subtitles=p.GetComponent<PlayerSubtitles>();
        subtitles.ShowImmediate("第 1 层 · 家",2);await Tick();
        bool entryHidden=!subtitles.SubtitleText.enabled;
        p.Teleport(lift.cabin.TransformPoint(new Vector3(.4f,1.05f,1)));
        var button=lift.GetComponentsInChildren<ElevatorButton>().First(b=>b.inside);
        await Aim(p,button.InteractionPosition);
        ScreenCapture.CaptureScreenshot("Logs/EightFloorAdventure/elevator-button.png");await Tick(250);
        bool focused=interaction.FocusedTarget==button;
        p.Teleport(map.floors[0].SpawnWorld);p.TransportLocked=false;await Tick();
        return new {exterior="Logs/EightFloorAdventure/elevator-exterior.png",interior="Logs/EightFloorAdventure/elevator-interior.png",buttonFocused=focused,floorHidden,entryHidden,restored=subtitles.FloorText.enabled && subtitles.SubtitleText.enabled,frame=Time.frameCount};
    }
    static void Capture(Camera cam,string file,int width,int height)
    {
        var rt=RenderTexture.GetTemporary(width,height,24);var prev=RenderTexture.active;var target=cam.targetTexture;
        try{cam.targetTexture=rt;cam.Render();cam.Render();RenderTexture.active=rt;var image=new Texture2D(width,height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();File.WriteAllBytes(file,image.EncodeToPNG());Object.DestroyImmediate(image);}
        finally{cam.targetTexture=target;RenderTexture.active=prev;RenderTexture.ReleaseTemporary(rt);}
    }
}

