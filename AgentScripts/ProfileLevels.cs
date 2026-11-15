using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using Object = UnityEngine.Object;

public static class ProfileLevels
{
    static readonly string[] Names = { "LevelHome", "LevelLibrary", "LevelSchool", "LevelWedding" };
    static object Counts(Scene scene) => new {
        objects = scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<Transform>(true).Length),
        renderers = scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<Renderer>(true).Length),
        colliders = scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<Collider>(true).Length),
        props = scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<LevelProp>(true).Length)
    };
    public static string Audit(string label)
    {
        var rows = new List<object>();
        foreach (string name in Names)
        {
            string path = "Assets/Levels/" + name + ".unity";
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            var watch = Stopwatch.StartNew();
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            double ms = watch.Elapsed.TotalMilliseconds;
            try {
                rows.Add(new { name, opened, openMs = ms, counts = Counts(scene),
                    props = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelProp>(true))
                        .GroupBy(p=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p.gameObject))
                        .Select(g=>new {path=g.Key, count=g.Count(), renderers=g.First().GetComponentsInChildren<Renderer>(true).Length,
                            eligible=LevelPropOptimizer.Eligibility(AssetDatabase.LoadAssetAtPath<GameObject>(g.Key))}).ToArray() });
            } finally { if(opened) EditorSceneManager.CloseScene(scene,true); }
        }
        Directory.CreateDirectory("Logs/LevelPerformance");
        string json = JsonConvert.SerializeObject(rows, Formatting.Indented);
        File.WriteAllText("Logs/LevelPerformance/"+label+"-audit.json",json); return json;
    }
    public static string OptimizeAll()
    {
        var reports = new List<object>();
        foreach (string name in Names)
        {
            string path="Assets/Levels/"+name+".unity";
            var scene=SceneManager.GetSceneByPath(path); bool opened=!scene.IsValid() || !scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try {
                var before=Counts(scene);
                var reportsForScene = scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LevelLayout>(true))
                    .Select(LevelPropOptimizer.Optimize).ToArray();
                if(!EditorSceneManager.SaveScene(scene)) throw new Exception("Save failed: "+path);
                reports.Add(new { name,before,after=Counts(scene),reports=reportsForScene });
            } finally {if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
        string json=JsonConvert.SerializeObject(reports,Formatting.Indented);
        File.WriteAllText("Logs/LevelPerformance/migration.json",json);return json;
    }
    public static string BakeCandidates()
    {
        var result=new List<string>();
        foreach(var path in AssetDatabase.GetDependencies(Names.Select(n=>"Assets/Levels/"+n+".unity").ToArray()).Where(p=>p.StartsWith("Assets/Prefabs/") && p.EndsWith(".prefab")))
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(LevelPropOptimizer.Eligibility(source)!=null)continue;
            LevelPropOptimizer.Bake(source,path);result.Add(source.name);
        }
        return string.Join(", ",result);
    }
    public static string VerifyWorkflow()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try {
            var root=new GameObject("Optimization verification");SceneManager.MoveGameObjectToScene(root,scene);
            var layout=root.AddComponent<LevelLayout>();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Chair.prefab");
            var chair=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);chair.transform.SetParent(root.transform);
            chair.transform.localPosition=new Vector3(2,0,3);chair.transform.localRotation=Quaternion.Euler(0,37,0);chair.name="Preserve name";
            PrefabUtility.RecordPrefabInstancePropertyModifications(chair);PrefabUtility.RecordPrefabInstancePropertyModifications(chair.transform);
            var modified=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);modified.transform.SetParent(root.transform);
            modified.transform.GetChild(0).localPosition+=Vector3.up;PrefabUtility.RecordPrefabInstancePropertyModifications(modified.transform.GetChild(0));
            string result=LevelPropOptimizer.Optimize(layout);
            if(chair==null || chair.GetComponentsInChildren<Renderer>().Length!=1 || chair.name!="Preserve name" || chair.transform.localPosition!=new Vector3(2,0,3)
                || Quaternion.Angle(chair.transform.localRotation,Quaternion.Euler(0,37,0))>.001f)throw new Exception("Replacement failed: "+result);
            if(modified.GetComponentsInChildren<Renderer>().Length!=10)throw new Exception("Modified child was lost");
            Undo.PerformUndo();if(chair.GetComponentsInChildren<Renderer>().Length!=10)throw new Exception("Undo failed");
            Undo.PerformRedo();if(chair.GetComponentsInChildren<Renderer>().Length!=1)throw new Exception("Redo failed");
            LevelPropOptimizer.Optimize(layout);if(chair.GetComponentsInChildren<Renderer>().Length!=1)throw new Exception("Idempotence failed");
            foreach(var name in new[]{"WoodenDoor","PickupWaterCup","ResizableTable","LevelElevator","RedVineArea"})
                if(LevelPropOptimizer.Eligibility(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+name+".prefab"))==null)throw new Exception("Unsafe prefab accepted: "+name);
            File.WriteAllText("Logs/LevelPerformance/workflow.txt","PASS root identity, name, pose, child override exclusion, Undo/Redo, repeated optimization, interactive exclusions");
            return "PASS "+result;
        } finally { EditorSceneManager.CloseScene(scene,true); }
    }
    public static string ReloadLibrary(string label)
    {
        var current=SceneManager.GetSceneByPath("Assets/Levels/LevelLibrary.unity");
        if(current.IsValid() && current.isDirty)throw new Exception("Library has unsaved changes; preserve them before timing reload");
        var watch=Stopwatch.StartNew();var scene=EditorSceneManager.OpenScene("Assets/Levels/LevelLibrary.unity",OpenSceneMode.Single);
        double ms=watch.Elapsed.TotalMilliseconds;
        var result=JsonConvert.SerializeObject(new{openMs=ms,counts=Counts(scene)});File.WriteAllText("Logs/LevelPerformance/"+label+"-open.json",result);return result;
    }
    public static string VerifyMeshes()
    {
        var results=new List<object>();
        foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{LevelPropOptimizer.Folder}))
        {
            var baked=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+baked.name+".prefab");
            if(original==null)throw new Exception("Original missing: "+baked.name);
            var scene=EditorSceneManager.NewPreviewScene();
            try {
                var a=(GameObject)PrefabUtility.InstantiatePrefab(original,scene);
                var b=(GameObject)PrefabUtility.InstantiatePrefab(baked,scene);
                var oldRenderers=a.GetComponentsInChildren<MeshRenderer>();var newRenderer=b.GetComponent<MeshRenderer>();
                Bounds bounds=oldRenderers[0].bounds;foreach(var r in oldRenderers)bounds.Encapsulate(r.bounds);
                if((bounds.center-newRenderer.bounds.center).sqrMagnitude>1e-8f || (bounds.size-newRenderer.bounds.size).sqrMagnitude>1e-8f)throw new Exception("Bounds mismatch "+baked.name);
                int triangles=a.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length);
                if(triangles!=b.GetComponent<MeshFilter>().sharedMesh.triangles.Length || triangles!=b.GetComponent<LevelProp>().combinedGameplayMesh.triangles.Length)throw new Exception("Triangles mismatch");
                var oldColliders=a.GetComponentsInChildren<Collider>();var newColliders=b.GetComponentsInChildren<Collider>();
                Physics.SyncTransforms(); int rays=0;
                for(int axis=0;axis<3;axis++)for(int sign=-1;sign<=1;sign+=2)for(int x=0;x<45;x++)for(int y=0;y<45;y++)
                {
                    var origin=bounds.center;origin[axis]+=sign*(bounds.extents[axis]+1);
                    origin[(axis+1)%3]+=Mathf.Lerp(-bounds.extents[(axis+1)%3]-.05f,bounds.extents[(axis+1)%3]+.05f,(x+.37f)/45);
                    origin[(axis+2)%3]+=Mathf.Lerp(-bounds.extents[(axis+2)%3]-.05f,bounds.extents[(axis+2)%3]+.05f,(y+.61f)/45);
                    var direction=Vector3.zero;direction[axis]=-sign;var ray=new Ray(origin,direction);
                    float Distance(Collider[] list) {float result=float.PositiveInfinity;foreach(var c in list)if(c.Raycast(ray,out var hit,100))result=Mathf.Min(result,hit.distance);return result;}
                    float da=Distance(oldColliders),db=Distance(newColliders);
                    if(float.IsInfinity(da)!=float.IsInfinity(db) || !float.IsInfinity(da)&&Mathf.Abs(da-db)>.001f)throw new Exception("Collision mismatch "+baked.name+" ray "+rays+" "+da+" / "+db);
                    rays++;
                }
                results.Add(new {name=baked.name, triangles=triangles/3, rays, originalRenderers=oldRenderers.Length, combinedRenderers=1});
            }finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        string json=JsonConvert.SerializeObject(results,Formatting.Indented);File.WriteAllText("Logs/LevelPerformance/mesh-verification.json",json);return json;
    }
    public static string Runtime()
    {
        var menu=Object.FindAnyObjectByType<SonicSpaceStartMenu>();if(menu!=null && SonicSpaceStartMenu.BlocksGameplay)menu.BeginGame();
        var props=Object.FindObjectsByType<LevelProp>().Where(p=>p.combinedGameplayMesh!=null).ToArray();
        foreach(var prop in props)
            if(prop.GetComponent<MeshFilter>().sharedMesh!=prop.combinedGameplayMesh || prop.GetComponent<MeshRenderer>().sharedMaterials.Length!=1)
                throw new Exception("Gameplay mesh/material failed "+prop.name);
        var sonar=Object.FindAnyObjectByType<SonarPulseField>();
        return JsonConvert.SerializeObject(new {frame=Time.frameCount,optimizedProps=props.Length,counts=Counts(SceneManager.GetActiveScene()),
            camera=Camera.main.transform.position.ToString(), resolution=new[]{Screen.width,Screen.height}, sonarEmissions=sonar.Emissions });
    }
    public static string Visual()
    {
        var reports=new List<object>();
        foreach(string name in new[]{"LibraryBookshelf","SchoolDesk","WallFlowerBasket"})
        {
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+name+".prefab");
            var baked=LevelPropOptimizer.ForPlacement(source);if(baked==source)throw new Exception("Placement cache miss: "+name);
            var preview=new PreviewRenderUtility();
            try {
                var a=Object.Instantiate(source);var b=Object.Instantiate(baked);preview.AddSingleGO(a);preview.AddSingleGO(b);
                // Preview must remain colored even if another task currently owns Play mode.
                a.GetComponent<LevelProp>().enabled=false;b.GetComponent<LevelProp>().enabled=false;
                var renderers=a.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                preview.lights[0].intensity=2.5f;preview.lights[0].transform.rotation=Quaternion.Euler(45,160,0);
                preview.lights[1].intensity=.75f;preview.lights[1].transform.rotation=Quaternion.Euler(20,60,0);
                var camera=preview.camera;camera.cameraType=CameraType.Preview;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(.055f,.07f,.09f);camera.orthographic=true;camera.orthographicSize=Mathf.Max(bounds.size.x,bounds.size.y)*.7f;
                camera.nearClipPlane=.01f;camera.farClipPlane=50;camera.transform.position=bounds.center+new Vector3(.6f,.35f,-1).normalized*10;camera.transform.LookAt(bounds.center);
                Texture2D Render(bool original) {a.SetActive(original);b.SetActive(!original);preview.BeginStaticPreview(new Rect(0,0,512,512));preview.Render(true);return preview.EndStaticPreview();}
                var first=Render(true);var second=Render(false);var pa=first.GetPixels32();var pb=second.GetPixels32();long total=0;int differing=0;
                for(int i=0;i<pa.Length;i++){int difference=Math.Abs(pa[i].r-pb[i].r)+Math.Abs(pa[i].g-pb[i].g)+Math.Abs(pa[i].b-pb[i].b);total+=difference;if(difference>30)differing++;}
                var atlas=new Texture2D(1024,512,TextureFormat.RGB24,false);atlas.SetPixels(0,0,512,512,first.GetPixels());atlas.SetPixels(512,0,512,512,second.GetPixels());atlas.Apply();
                File.WriteAllBytes("Logs/LevelPerformance/"+name+"-comparison.png",atlas.EncodeToPNG());
                Object.DestroyImmediate(atlas);Object.DestroyImmediate(first);Object.DestroyImmediate(second);
                if(differing>pa.Length*.02f)throw new Exception("Visual difference exceeds 2%: "+name+" "+differing);
                reports.Add(new{name,meanChannelDifference=total/(double)(pa.Length*3),differingPixels=differing});
            }finally{preview.Cleanup();}
        }
        var json=JsonConvert.SerializeObject(reports,Formatting.Indented);File.WriteAllText("Logs/LevelPerformance/visual.json",json);return json;
    }
    public static string BeginSonarWorkload()
    {
        if(!Application.isPlaying)throw new Exception("Play mode required");
        var field=Object.FindAnyObjectByType<SonarPulseField>();var samples=new List<double>();var rays=new List<int>();
        int frame=Time.frameCount;double next=EditorApplication.timeSinceStartup;
        EditorApplication.CallbackFunction update=null;
        update=()=>{
            if(!Application.isPlaying || field==null){EditorApplication.update-=update;return;}
            if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.25;
            var timer=Stopwatch.StartNew();field.EmitFrom(Camera.main.transform.position,18,10,2);
            samples.Add(timer.Elapsed.TotalMilliseconds);rays.Add(field.LastRayCount);
            if(samples.Count<24)return;
            EditorApplication.update-=update;
            samples.Sort();File.WriteAllText("Logs/LevelPerformance/sonar-after.json",JsonConvert.SerializeObject(new {
                emissions=samples.Count,framesAdvanced=Time.frameCount-frame,minMs=samples.First(),medianMs=samples[samples.Count/2],maxMs=samples.Last(),rayCounts=rays.Distinct().ToArray()
            },Formatting.Indented));
        };
        EditorApplication.update+=update;return "24 sonar emissions scheduled over six seconds";
    }
}
