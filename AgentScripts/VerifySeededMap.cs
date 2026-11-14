using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sound2.LevelEditing;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VerifySeededMap
{
    static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static object Plans()
    {
        var rows = new List<object>();
        for (int f = 0; f <= 24; f++)
        {
            var a = SeededFloorPlan.Generate(f); var b = SeededFloorPlan.Generate(f);
            Check(a.Cells.SequenceEqual(b.Cells) && a.Resources.SequenceEqual(b.Resources) && a.Connections.SequenceEqual(b.Connections), "Seed reproducibility failed F" + f);
            Check(a.Rooms.Count == 5 + f * 2 && a.Resources.Count == 6 + f * 3, "Floor growth failed.");
            var walkable = new HashSet<Vector2Int>(a.Cells.Where(c => !a.IsPit(c)));
            var visited = new HashSet<Vector2Int>(); var queue = new Queue<Vector2Int>();
            queue.Enqueue(Vector2Int.zero); visited.Add(Vector2Int.zero);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                foreach (var d in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
                    if (walkable.Contains(p+d) && visited.Add(p+d)) queue.Enqueue(p+d);
            }
            Check(visited.Count == walkable.Count, "Disconnected walkable floor F" + f);
            foreach (var resource in a.Resources)
            {
                var cell = Vector2Int.FloorToInt(resource);
                Check(visited.Contains(cell) && !a.IsPit(cell), "Unreachable resource F" + f);
            }
            if (f == 0 || f == 1 || f == 5 || f == 12 || f == 24)
                rows.Add(new { floor=f, seed=f, rooms=a.Rooms.Count, connections=a.Connections.Count, area=a.Cells.Count, resources=a.Resources.Count });
        }
        var extreme = SeededFloorPlan.Generate(int.MaxValue);
        Check(extreme.Rooms.Count == 53 && extreme.Resources.Count == 78, "Growth limit did not bound huge seeds.");
        return new { passed=true, checkedFloors=25, rows };
    }

    static async Task Tick(int ms=100)
    {
        int frame=Time.frameCount; await Task.Delay(ms);
        Check(Application.isPlaying && Time.frameCount>frame,"Player loop stalled.");
    }
    static void Place(PlayerController controller, Vector3 position)
    {
        var cc=controller.GetComponent<CharacterController>(); cc.enabled=false; controller.transform.position=position; cc.enabled=true;
        Physics.SyncTransforms();
    }
    public static async Task<object> Play()
    {
        Check(Application.isPlaying,"Enter Play mode first.");
        var existing=UnityEngine.Object.FindObjectsByType<PlayerController>();
        var states=existing.Select(p=>p.InputSuspended).ToArray();
        var listeners=UnityEngine.Object.FindObjectsByType<AudioListener>();
        var listenerStates=listeners.Select(l=>l.enabled).ToArray();
        foreach(var listener in listeners) listener.enabled=false;
        foreach(var p in existing) p.InputSuspended=true;
        Scene scene=default;
        try
        {
            scene=EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/SeededMapDemo.unity",new LoadSceneParameters(LoadSceneMode.Additive));
            while (!scene.isLoaded) await Tick(50);
            var roots=scene.GetRootGameObjects();
            var map=roots.Select(r=>r.GetComponent<LevelMap>()).First(m=>m!=null);
            var player=roots.Select(r=>r.GetComponent<PlayerController>()).First(p=>p!=null);
            map.transform.position=new Vector3(1000,0,1000);
            Place(player,new Vector3(1000,1.1f,1000));
            var camera=player.GetComponentInChildren<Camera>(); camera.depth=100;
            var sonar=camera.GetComponent<SphericalSonar>();
            var progression=player.GetComponent<PlayerFloorProgression>();
            var subtitles=player.GetComponent<PlayerSubtitles>();
            await Tick(300);
            Check(progression.CurrentFloor==0 && progression.Map==map && subtitles.FloorText.text=="F0","F0 initialization failed.");
            Check(map.FindFloor(0)!=null && map.FindFloor(1)!=null,"Next floor was not prepared.");
            Check(subtitles.CurrentText.StartsWith("F0"),"F0 entry cue missing.");
            float initialRange=sonar.range;
            int changes=0; progression.onFloorChanged.AddListener(_=>changes++);
            for(int floor=0;floor<3;floor++)
            {
                var source=map.FindFloor(floor); var shaft=source.Exit;
                Check(shaft.HasDestination,"Shaft has no destination.");
                Check(!shaft.CrossedDown(shaft.transform.position-Vector3.up,shaft.transform.position+Vector3.up),"Upward movement counted.");
                Check(!shaft.CrossedDown(shaft.transform.position+Vector3.right*3+Vector3.up,shaft.transform.position+Vector3.right*3-Vector3.up),"Outside-shaft movement counted.");
                Place(player,source.transform.TransformPoint(new Vector3(source.Plan.Exit.x,1.2f,source.Plan.Exit.y)));
                float deadline=Time.realtimeSinceStartup+6;
                while(progression.CurrentFloor==floor && Time.realtimeSinceStartup<deadline) await Tick(70);
                Check(progression.CurrentFloor==floor+1,"Actual gravity descent did not advance F"+floor);
                Check(!progression.TryDescend(shaft),"Repeated shaft crossing double-counted.");
                await Tick(450);
                Check(player.transform.position.y>map.FindFloor(floor+1).transform.position.y+.5f,"Player fell through arrival floor.");
                Check(subtitles.FloorText.text=="F"+(floor+1),"Floor HUD did not update.");
            }
            Check(changes==3 && progression.Difficulty>1 && sonar.range<initialRange,"Floor events/numerical changes failed.");
            Check(map.Floors.Count()==3 && map.FindFloor(0)==null,"Old floor streaming cleanup failed.");

            var item=map.FindFloor(3).GetComponentInChildren<MapResource>().GetComponent<PickupItem>();
            item.transform.position=camera.transform.position+camera.transform.forward*1.4f;
            Physics.SyncTransforms();
            await Tick(400);
            var interaction=player.GetComponent<PlayerInteraction>();
            Check(interaction.FocusedTarget==item && interaction.TryInteractFocused(),"Generated resource cannot be clicked.");
            Check(player.GetComponent<PlayerPickup>().HeldItem==item && item.gameObject.activeInHierarchy && !item.TryInteract(interaction),"Generated prop was not held exactly once.");
            player.GetComponent<PlayerPickup>().TryPlace();
            var second=map.FindFloor(3).GetComponentsInChildren<MapResource>().First();
            second.transform.position=camera.transform.position+camera.transform.forward*1.4f;
            Physics.SyncTransforms(); sonar.dotRadius=2.25f; await Tick(400);
            Check(Mathf.Abs(interaction.EffectiveRingThickness-4.5f)<.01f,"Ring width does not follow rendered dot diameter.");
            var rects=new Vector4[PlayerUiPointMask.Capacity];var options=new Vector4[PlayerUiPointMask.Capacity];
            Check(PlayerUiPointMask.Collect(camera,rects,options)>=2,"Visible UI/ring exclusion zones missing.");
            Check(subtitles.FloorText.font==GameUiTheme.Current.textFont && subtitles.SubtitleText.font==GameUiTheme.Current.textFont,"Provided font not applied.");

            subtitles.fadeTime=.05f;
            subtitles.Replace(new[]{new SubtitleCue("字幕队列一",.1f),new SubtitleCue("字幕队列二",.1f)});
            await Tick(70); Check(subtitles.CurrentText=="字幕队列一","Subtitle queue first item failed.");
            player.InputSuspended=true; string paused=subtitles.CurrentText; await Tick(300);
            Check(subtitles.CurrentText==paused,"Menu did not pause subtitle timing.");
            player.InputSuspended=false; await Tick(500);
            Check(subtitles.CurrentText=="" && subtitles.PendingCount==0,"Subtitle queue did not finish/clear.");
            return new { passed=true, checks=new[]{"F0 + entry cue","3 real gravity shaft descents","upward/outside/duplicate rejection","safe landings","difficulty + sonar values","floor streaming","resource collection","dynamic ring width","UI masks + provided font","subtitle queue + menu pause"} };
        }
        finally
        {
            if(scene.IsValid() && scene.isLoaded) { var unload=SceneManager.UnloadSceneAsync(scene); while(unload!=null && !unload.isDone) await Task.Delay(50); }
            for(int i=0;i<existing.Length;i++) if(existing[i]!=null) existing[i].InputSuspended=states[i];
            for(int i=0;i<listeners.Length;i++) if(listeners[i]!=null) listeners[i].enabled=listenerStates[i];
        }
    }
}
