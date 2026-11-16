using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Newtonsoft.Json;
using Object=UnityEngine.Object;

public static class VerifyGroceryPerformance
{
    const string Dir="Logs/GroceryFix/";
    static LevelWorld World=>Object.FindAnyObjectByType<LevelWorld>();
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static async Task Until(Func<bool> predicate,int timeout=120000)
    {double end=EditorApplication.timeSinceStartup+timeout/1000.0;while(!predicate()){if(EditorApplication.timeSinceStartup>end)throw new Exception("Timed out");await Task.Delay(20);}}
    static void Save(string name,object value)=>File.WriteAllText(Dir+name+".json",JsonConvert.SerializeObject(value,Formatting.Indented));
    public static object Authored()
    {
        var rows=new List<object>();
        foreach(var path in new[]{"Assets/Scenes/SoundAndShell.unity","Assets/Levels/LevelGrocery.unity"})
        {
            var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try{
                var level=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelLayout>(true)).First(l=>l.name.Contains("超市")||l.name=="Editable Level");
                var items=level.GetComponentsInChildren<PickupItem>(true);
                int duplicates=items.GroupBy(i=>level.transform.InverseTransformPoint(i.transform.position).ToString("F4")).Count(g=>g.Count()>1);
                Check(items.Length==309&&duplicates==0,"Unexpected Grocery item count/duplicates: "+path);
                rows.Add(new{path,items=items.Length,duplicates,lamps=items.Count(i=>i.name.StartsWith("PickupDeskLamp"))});
            }finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
        var broken=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/WineGlassBroken.prefab");
        var bodies=broken.GetComponentsInChildren<Rigidbody>(true);
        Check(bodies.Length==25,"Wine glass must have exactly 25 bodies");
        Check(broken.GetComponentsInChildren<MeshRenderer>(true).Length==49,"Glass silhouette must retain all 49 visual pieces");
        Check(broken.GetComponentsInChildren<Collider>(true).All(c=>c.GetComponentInParent<Rigidbody>(true)!=null),"All shards need a body");
        Check(broken.GetComponentsInChildren<Transform>(true).All(t=>t.gameObject.layer==PropFragments.Layer),"Fragment layers preserved");
        Save("authored",new{rows,fragmentBodies=bodies.Length,fragmentRenderers=49,totalMass=bodies.Sum(b=>b.mass)});
        return new{rows,fragmentBodies=bodies.Length};
    }
    public static async Task<object> Tables()
    {
        var scene=EditorSceneManager.OpenScene("Assets/Levels/LevelWedding.unity",OpenSceneMode.Additive);
        GameObject fixture=null;
        try{
            var tables=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<DrapedRingTable>(true)).ToArray();
            fixture=new GameObject("Table cache verification");fixture.SetActive(false);SceneManager.MoveGameObjectToScene(fixture,scene);
            var results=new List<object>();
            foreach(var source in tables)
            {
                var visual=source.GetComponent<MeshFilter>().sharedMesh;var collision=source.GetComponent<MeshCollider>().sharedMesh;
                Check(AssetDatabase.Contains(visual)&&AssetDatabase.Contains(collision),"Table must have baked mesh assets");
                var clone=Object.Instantiate(source,fixture.transform,false);
                var watch=System.Diagnostics.Stopwatch.StartNew();fixture.SetActive(true);await Task.Delay(100);fixture.SetActive(false);
                Check(clone.GetComponent<MeshFilter>().sharedMesh==visual&&clone.GetComponent<MeshCollider>().sharedMesh==collision,"Clone activation must reuse saved meshes");
                fixture.SetActive(true);await Task.Delay(100);fixture.SetActive(false);
                Check(clone.GetComponent<MeshFilter>().sharedMesh==visual,"Reactivation must reuse saved meshes");
                clone.foldDepth=Mathf.Approximately(clone.foldDepth,.02f)?.03f:.02f;clone.Rebuild();
                var changed=clone.GetComponent<MeshFilter>().sharedMesh;
                Check(changed!=visual,"Parameter change must rebuild");
                Check(source.GetComponent<MeshFilter>().sharedMesh==visual,"Editing clone must not mutate shared source");
                fixture.SetActive(true);await Task.Delay(100);fixture.SetActive(false);
                Check(clone.GetComponent<MeshFilter>().sharedMesh==changed,"Generated mesh must survive reactivation");
                results.Add(new{source.name,reusesStored=true,reusesGenerated=true,parameterInvalidation=true});
                Object.DestroyImmediate(clone.gameObject);
            }
            Save("tables",results);return results;
        }finally{if(fixture!=null)Object.DestroyImmediate(fixture);EditorSceneManager.CloseScene(scene,true);}
    }
    public static async Task<object> Stage(bool held)
    {
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();if(menu!=null&&!menu.HasStarted)menu.BeginGame();
        await Until(()=>!SonicSpaceStartMenu.BlocksGameplay);
        var w=World;
        typeof(LevelWorld).GetMethod("Activate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(w,new object[]{w.levels[3]});
        var source=w.connections[3].source;
        source.Use(w.player.GetComponent<PlayerInteraction>(),false);
        await Until(()=>source.DoorOpen>.999f);
        w.player.Teleport(source.transform.TransformPoint(new Vector3(0,1.1f,0)));
        if(held)
        {
            var alarm=w.CurrentLevel.GetComponentsInChildren<PickupAlarm>().First();var item=alarm.GetComponent<PickupItem>();var picker=w.player.GetComponent<PlayerPickup>();
            Check(item.TryPickUp(picker,picker.holdPoint),"Cannot hold alarm");
            typeof(PlayerPickup).GetField("heldItem",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(picker,item);
        }
        await Task.Delay(500);
        return new{ready=true,held=w.player.GetComponent<PlayerPickup>().HeldItem?.name};
    }
    public static async Task<object> Enter(string label)
    {
        var w=World;var probe=new GroceryPerformanceProbe();probe.Initialize(w.levels[4]);probe.watch=true;
        Check(w.Travel(w.connections[3].source,w.player),"Travel refused");
        await Until(()=>!w.Travelling);
        int arrivalFrame=Time.frameCount;
        await Task.Delay(16000);probe.Stop();
        var result=probe.Result(label,arrivalFrame);
        Save(label,result);return result;
    }
    public static async Task<object> Reset()
    {
        var w=World;var previous=w.CurrentLevel;var items=previous.GetComponentsInChildren<PickupItem>();
        string removed=items.First(i=>i.name.StartsWith("PickupDeskLamp")).name;
        Object.Destroy(items.First(i=>i.name==removed).gameObject);await Task.Delay(100);
        w.StartCoroutine(w.ResetCurrentLevel());await Until(()=>w.CurrentLevel!=previous);
        await Until(()=>!w.CurrentLevel.IsPreparing&&w.CurrentLevel.GetComponent<RuntimePatrolNavigation>().Ready);
        await Task.Delay(2000);
        Check(w.CurrentLevel.GetComponentsInChildren<PickupItem>().Length==309,"Reset must restore 309 items");
        Check(w.CurrentLevel.GetComponentsInChildren<PickupItem>().Any(i=>i.name==removed),"Destroyed lamp not restored");
        Check(w.ArrivalCheckpoint.Level==w.CurrentLevel&&w.connections[3].destination==w.ArrivalCheckpoint,"Arrival references not remapped");
        var result=new{passed=true,items=309,restored=removed,arrivalRemapped=true};Save("reset",result);return result;
    }
    public static object PrepareStandalone()
    {
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Levels/LevelGrocery.unity");
        EditorApplication.playModeStateChanged+=StandaloneStarted;
        return "Standalone observer armed before physics";
    }
    public static object PrepareMain()
    {
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SoundAndShell.unity");
        return "Main scene selected for Play Mode";
    }
    static void StandaloneStarted(PlayModeStateChange state)
    {
        if(state!=PlayModeStateChange.EnteredPlayMode)return;
        EditorApplication.playModeStateChanged-=StandaloneStarted;
        var level=Object.FindAnyObjectByType<LevelLayout>();
        var probe=new GroceryPerformanceProbe();probe.Initialize(level);probe.watch=true;
        AppDomain.CurrentDomain.SetData("GroceryProbe",probe);
    }
    public static async Task<object> Standalone()
    {
        var probe=AppDomain.CurrentDomain.GetData("GroceryProbe");
        await Task.Delay(16000);probe.GetType().GetMethod("Stop").Invoke(probe,null);
        var result=probe.GetType().GetMethod("Result").Invoke(probe,new object[]{"standalone",0});Save("standalone",result);return result;
    }
    public static async Task<object> BreakGlass()
    {
        var root=new GameObject("Controlled glass fracture test");root.transform.position=new Vector3(5000,5,5000);
        try{
            var glass=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/PickupGlassMug.prefab"),root.transform);
            var item=glass.GetComponent<PickupItem>();
            typeof(PickupItem).GetMethod("BreakAt",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(item,new object[]{glass.transform.position,Vector3.up});
            var remains=item.SpawnedRemains;int count=remains.GetComponentsInChildren<Rigidbody>().Length;
            Check(count==25,"Runtime break must spawn exactly 25 bodies");
            Check(remains.GetComponentsInChildren<Collider>().All(c=>c.attachedRigidbody!=null),"Runtime shard attachment");
            await Task.Delay(200);Object.Destroy(remains);var result=new{passed=true,bodies=count};Save("glass",result);return result;
        }finally{Object.Destroy(root);}
    }
}
public sealed class GroceryPerformanceProbe
{
    LevelLayout level;Transform[] lamps;Vector3[] positions;
    public int breaks;public float maxTilt,maxDisplacement;public bool watch;
    public readonly List<object> frames=new();
    public void Initialize(LevelLayout value)
    {
        level=value;var items=level.GetComponentsInChildren<PickupItem>(true);
        lamps=items.Where(i=>i.name.StartsWith("PickupDeskLamp")).Select(i=>i.transform).ToArray();positions=lamps.Select(t=>t.position).ToArray();
        foreach(var item in items)item.Broken+=()=>breaks++;
        EditorApplication.update+=Tick;
    }
    int lastFrame=-1;
    public void Stop(){watch=false;EditorApplication.update-=Tick;}
    void Tick()
    {
        if(!Application.isPlaying){Stop();return;}
        if(!watch||lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        for(int i=0;i<lamps.Length;i++)if(lamps[i]!=null&&lamps[i].gameObject.activeInHierarchy)
        {maxTilt=Mathf.Max(maxTilt,Vector3.Angle(Vector3.up,lamps[i].up));maxDisplacement=Mathf.Max(maxDisplacement,Vector3.Distance(positions[i],lamps[i].position));}
        frames.Add(new{frame=Time.frameCount,ms=Time.unscaledDeltaTime*1000});
    }
    public object Result(string label,int arrivalFrame)=>new{label,arrivalFrame,breaks,lamps=lamps.Length,maxTilt,maxDisplacement,items=level.GetComponentsInChildren<PickupItem>().Length,bodies=level.GetComponentsInChildren<Rigidbody>().Length,collisionEmissions=level.GetComponentsInChildren<CollisionSonarEmitter>().Sum(e=>e.Emissions),frames};
}
