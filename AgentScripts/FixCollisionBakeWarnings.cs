using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FixCollisionBakeWarnings
{
    static Dictionary<(Mesh mesh,bool convex),HashSet<string>> FindUses()
    {
        var uses=new Dictionary<(Mesh,bool),HashSet<string>>();
        void Collect(GameObject root,string owner)
        {
            foreach(var c in root.GetComponentsInChildren<MeshCollider>(true))
            {
                if(c.sharedMesh==null)continue;
                var key=(c.sharedMesh,c.convex);
                if(!uses.TryGetValue(key,out var owners))uses[key]=owners=new();
                owners.Add(owner);
            }
        }
        var paths=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray();
        foreach(var path in paths)
        {
            var scene=SceneManager.GetSceneByPath(path); bool opened=!scene.IsValid()||!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try { foreach(var root in scene.GetRootGameObjects())Collect(root,path); }
            finally { if(opened)EditorSceneManager.CloseScene(scene,true); }
        }
        var resources=AssetDatabase.GetAllAssetPaths().Where(p=>p.StartsWith("Assets/")&&p.Contains("/Resources/")).ToArray();
        foreach(var path in AssetDatabase.GetDependencies(paths.Concat(resources).ToArray(),true))
        {
            if(!path.EndsWith(".prefab"))continue;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(prefab!=null)Collect(prefab,path);
        }
        return uses;
    }
    public static object Audit()
    {
        var missing=FindUses().Where(x=>AssetDatabase.GetAssetPath(x.Key.mesh).StartsWith("Assets/") && !x.Key.mesh.HasPreBakeCollisionMesh(x.Key.convex))
            .Select(x=>new {path=AssetDatabase.GetAssetPath(x.Key.mesh),mesh=x.Key.mesh.name,x.Key.convex,owners=x.Value.ToArray()}).ToArray();
        Directory.CreateDirectory("Logs/StartingRoomEndings");
        File.WriteAllText("Logs/StartingRoomEndings/collision-bake-audit.json",Newtonsoft.Json.JsonConvert.SerializeObject(missing,Newtonsoft.Json.Formatting.Indented));
        return new {count=missing.Length,files=missing.Select(m=>m.path).Distinct().ToArray()};
    }
    public static object Fix()
    {
        if(Application.isPlaying)throw new Exception("Edit mode only.");
        var missing=FindUses().Where(x=>AssetDatabase.GetAssetPath(x.Key.mesh).StartsWith("Assets/") && !x.Key.mesh.HasPreBakeCollisionMesh(x.Key.convex)).ToArray();
        var reimport=new HashSet<ModelImporter>();
        var assets=new HashSet<string>();
        foreach(var entry in missing)
        {
            var mesh=entry.Key.mesh;string path=AssetDatabase.GetAssetPath(mesh);
            if(!path.StartsWith("Assets/"))throw new Exception("Unexpected non-project mesh: "+path);
            if(AssetImporter.GetAtPath(path) is ModelImporter importer)
            { importer.SetPreBakeCollisionMesh(entry.Key.convex,true); reimport.Add(importer); }
            else { mesh.SetPreBakeCollisionMesh(entry.Key.convex,true); EditorUtility.SetDirty(mesh); assets.Add(path); }
        }
        foreach(var path in assets)AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path));
        foreach(var importer in reimport)importer.SaveAndReimport();
        var remaining=FindUses().Where(x=>AssetDatabase.GetAssetPath(x.Key.mesh).StartsWith("Assets/") && !x.Key.mesh.HasPreBakeCollisionMesh(x.Key.convex)).Count();
        if(remaining!=0)throw new Exception("Unfixed mesh bake flags: "+remaining);
        return new {fixedMeshModes=missing.Length,assets=assets.Count,importers=reimport.Count,remaining};
    }
}
