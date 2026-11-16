using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using Newtonsoft.Json;
using Object = UnityEngine.Object;

public static class OptimizeProjectColliders
{
    const string Dir="Logs/CollisionOptimization/";
    public static async Task<object> Run(bool apply, string label = "")
    {
        if(Application.isPlaying)throw new Exception("Edit Mode required");
        var paths=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/")&&(p.EndsWith(".prefab")||p.EndsWith(".unity")))
            .OrderBy(p=>p.EndsWith(".prefab")?0:1).ThenBy(p=>AssetDatabase.GetDependencies(p,true).Length).ThenBy(p=>p).ToArray();
        var original=SceneManager.GetActiveScene();
        if(apply)
        {
            // Preserve the entire pre-migration asset set, including unrelated working edits.
            foreach(var path in paths)
            {
                string backup=Dir+"Backup/"+path;Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if(!File.Exists(backup))File.Copy(path,backup);
                if(File.Exists(path+".meta")&&!File.Exists(backup+".meta"))File.Copy(path+".meta",backup+".meta");
            }
            for(int i=0;i<SceneManager.sceneCount;i++)
            {
                var scene=SceneManager.GetSceneAt(i);
                if(scene.isDirty && !EditorSceneManager.SaveScene(scene,Dir+"Backup/dirty-"+scene.name+".unity",true))throw new Exception("Could not back up dirty scene");
            }
        }
        var rows=new List<object>();
        try
        {
            foreach(var path in paths)
            {
                GameObject prefab=null;Scene scene=default;bool opened=false;
                try
                {
                    GameObject[] roots;
                    if(path.EndsWith(".prefab")){prefab=PrefabUtility.LoadPrefabContents(path);roots=new[]{prefab};}
                    else{scene=SceneManager.GetSceneByPath(path);opened=!scene.IsValid()||!scene.isLoaded;if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);roots=scene.GetRootGameObjects();}
                    var referenced=ConservativeColliderOptimizer.ReferencedColliders(roots);
                    int before=roots.Sum(r=>r.GetComponentsInChildren<Collider>(true).Length);
                    var candidates=roots.SelectMany(r=>r.GetComponentsInChildren<LevelProp>(true))
                        .Where(p=>p.GetComponentsInChildren<Collider>(true).Length>=8)
                        .Select(p=>new{name=p.name,colliders=p.GetComponentsInChildren<Collider>(true).Length,boxes=p.GetComponentsInChildren<BoxCollider>(true).Count(c=>c.enabled&&!c.isTrigger&&!referenced.Contains(c)),skip=ConservativeColliderOptimizer.Ineligible(p)}).ToArray();
                    var changes=new List<ConservativeColliderOptimizer.Change>();
                    if(apply)
                    {
                        foreach(var root in roots)changes.AddRange(ConservativeColliderOptimizer.Optimize(root,referenced));
                        if(changes.Count>0)
                        {
                            if(prefab!=null)PrefabUtility.SaveAsPrefabAsset(prefab,path);
                            else if(!EditorSceneManager.SaveScene(scene))throw new Exception("Save failed: "+path);
                        }
                    }
                    rows.Add(new{path,before,after=roots.Sum(r=>r.GetComponentsInChildren<Collider>(true).Length),candidates,changes});
                }
                finally{if(prefab!=null)PrefabUtility.UnloadPrefabContents(prefab);if(opened)EditorSceneManager.CloseScene(scene,true);}
                await Task.Yield();
            }
            if(apply)AssetDatabase.SaveAssets();
        }
        finally{if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);}
        string output=Dir+label+(apply?"migration":"plan")+".json";
        File.WriteAllText(output,JsonConvert.SerializeObject(rows,Formatting.Indented));return new{output,assets=rows.Count};
    }

    public static object VerifyShapes()
    {
        var scene=EditorSceneManager.NewPreviewScene();var rows=new List<object>();
        try
        {
            foreach(var path in AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/Prefabs/")&&p.EndsWith(".prefab")))
            {
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if(source==null||source.GetComponent<LevelProp>()==null||ConservativeColliderOptimizer.Ineligible(source.GetComponent<LevelProp>())!=null
                    ||source.GetComponentsInChildren<BoxCollider>(true).Length<8)continue;
                var a=Object.Instantiate(source);var b=Object.Instantiate(source);
                SceneManager.MoveGameObjectToScene(a,scene);SceneManager.MoveGameObjectToScene(b,scene);
                try
                {
                    a.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);b.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
                    a.SetActive(true);b.SetActive(true);
                    var before=a.GetComponentsInChildren<Collider>();
                    var changes=ConservativeColliderOptimizer.Optimize(b,ConservativeColliderOptimizer.ReferencedColliders(new[]{b}));
                    if(changes.Count==0)continue;
                    var after=b.GetComponentsInChildren<Collider>();Physics.SyncTransforms();
                    var bounds=before[0].bounds;foreach(var c in before)bounds.Encapsulate(c.bounds);
                    var random=new System.Random(7401);int checkedRays=0;
                    float Cast(Collider[] colliders,Ray ray,float length)
                    {
                        float distance=float.PositiveInfinity;
                        foreach(var c in colliders)if(c.Raycast(ray,out var hit,length))distance=Mathf.Min(distance,hit.distance);
                        return distance;
                    }
                    // External silhouettes, interior shelves and spaces between keyboard keys.
                    for(int i=0;i<4096;i++)
                    {
                        Vector3 v=new Vector3((float)random.NextDouble()*2-1,(float)random.NextDouble()*2-1,(float)random.NextDouble()*2-1);
                        var direction=new Vector3((float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f,(float)random.NextDouble()-.5f).normalized;
                        var origin=bounds.center+Vector3.Scale(bounds.extents*(i%2==0?1.5f:.95f),v);
                        var ray=new Ray(origin,direction);float length=bounds.size.magnitude*4;
                        float x=Cast(before,ray,length),y=Cast(after,ray,length);
                        if(float.IsInfinity(x)!=float.IsInfinity(y)||!float.IsInfinity(x)&&Mathf.Abs(x-y)>.0001f)
                            throw new Exception(path+": collision surface changed at ray "+i+" "+x+" vs "+y);
                        checkedRays++;
                    }
                    if(a.GetComponentsInChildren<Renderer>(true).Length!=b.GetComponentsInChildren<Renderer>(true).Length)throw new Exception("Visual hierarchy changed");
                    rows.Add(new{path,before=before.Length,after=after.Length,checkedRays,visualsPreserved=true});
                }
                finally{Object.DestroyImmediate(a);Object.DestroyImmediate(b);}
            }
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
        File.WriteAllText(Dir+"shape-checks.json",JsonConvert.SerializeObject(rows,Formatting.Indented));return rows;
    }
}
