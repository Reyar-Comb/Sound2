using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;

public static class AuditProjectColliders
{
    public static async Task<object> Run(string label)
    {
        if(Application.isPlaying) throw new Exception("Audit in Edit Mode");
        Directory.CreateDirectory("Logs/CollisionOptimization");
        var paths=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/")&&(p.EndsWith(".prefab")||p.EndsWith(".unity"))).OrderBy(p=>p).ToArray();
        var rows=new List<object>(); var shapes=new Dictionary<Mesh,int>();
        foreach(var path in paths)
        {
            GameObject prefab=null; Scene scene=default; bool opened=false;
            try
            {
                GameObject[] roots;
                if(path.EndsWith(".prefab")) { prefab=PrefabUtility.LoadPrefabContents(path); roots=new[]{prefab}; }
                else { scene=SceneManager.GetSceneByPath(path); opened=!scene.IsValid()||!scene.isLoaded; if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive); roots=scene.GetRootGameObjects(); }
                var colliders=roots.SelectMany(r=>r.GetComponentsInChildren<Collider>(true)).ToArray();
                foreach(var c in colliders.OfType<MeshCollider>()) if(c.sharedMesh!=null) {if(!shapes.ContainsKey(c.sharedMesh))shapes[c.sharedMesh]=0;shapes[c.sharedMesh]++;}
                var owners=colliders.GroupBy(c=>c.GetComponentInParent<Rigidbody>(true)?.transform??c.GetComponentInParent<Sound2.LevelEditing.LevelProp>(true)?.transform??c.transform);
                rows.Add(new {path,nodes=roots.Sum(r=>r.GetComponentsInChildren<Transform>(true).Length),colliders=colliders.Length,mesh=colliders.OfType<MeshCollider>().Count(),
                    convex=colliders.OfType<MeshCollider>().Count(c=>c.convex),triggers=colliders.Count(c=>c.isTrigger),dynamic=colliders.Count(c=>c.GetComponentInParent<Rigidbody>(true)!=null),
                    invalidDynamicMeshes=colliders.OfType<MeshCollider>().Count(c=>!c.convex&&c.GetComponentInParent<Rigidbody>(true) is Rigidbody b&&!b.isKinematic),
                    duplicateBakes=roots.SelectMany(r=>r.GetComponentsInChildren<Sound2.LevelEditing.LevelProp>(true)).Sum(p=>p.GetComponentsInChildren<MeshCollider>(true)
                        .Where(c=>c.sharedMesh!=null&&AssetDatabase.GetAssetPath(c.sharedMesh).StartsWith("Assets/LevelEditor/CollisionMeshes/"))
                        .GroupBy(c=>c.sharedMesh).Count(g=>g.Count()>1)),
                    owners=owners.Where(g=>g.Count()>8).Select(g=>new{name=Name(g.Key),count=g.Count(),mesh=g.OfType<MeshCollider>().Count(),body=g.Key.GetComponent<Rigidbody>()!=null}).OrderByDescending(g=>g.count).ToArray()});
            }
            finally {if(prefab!=null)PrefabUtility.UnloadPrefabContents(prefab);if(opened)EditorSceneManager.CloseScene(scene,true);}
            await Task.Yield();
        }
        var result=new {assets=rows,shapes=shapes.OrderByDescending(g=>g.Value).Select(g=>new{path=AssetDatabase.GetAssetPath(g.Key),g.Key.name,uses=g.Value,vertices=g.Key.vertexCount,triangles=g.Key.triangles.Length/3,bounds=new{center=g.Key.bounds.center.ToString("F5"),size=g.Key.bounds.size.ToString("F5")},box=IsBox(g.Key)}).ToArray()};
        var output="Logs/CollisionOptimization/"+label+"-audit.json";File.WriteAllText(output,JsonConvert.SerializeObject(result,Formatting.Indented));
        return new{output,assets=rows.Count,meshes=shapes.Count};
    }
    static string Name(Transform t)=>t.parent==null?t.name:Name(t.parent)+"/"+t.name;
    public static bool IsBox(Mesh mesh)
    {
        if(!mesh.isReadable)return false;
        var b=mesh.bounds;if(Mathf.Min(b.size.x,b.size.y,b.size.z)<.0001f)return false;
        var corners=new HashSet<int>();float e=Mathf.Max(.000001f,b.size.magnitude*.000001f);
        foreach(var v in mesh.vertices)
        {
            int bits=0;
            for(int i=0;i<3;i++) {if(Mathf.Abs(v[i]-b.min[i])<e){}else if(Mathf.Abs(v[i]-b.max[i])<e)bits|=1<<i;else return false;}
            corners.Add(bits);
        }
        return corners.Count==8;
    }
}
