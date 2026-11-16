using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

public static class CompactLevelSerialization
{
    public const string WorldPath = "Assets/Scenes/SoundAndShell.unity";
    public const string ReportFolder = "Logs/LevelSerialization";
    public static string[] Paths => Directory.GetFiles("Assets/Levels", "*.unity").OrderBy(x => x).Append(WorldPath).Select(x=>x.Replace('\\','/')).ToArray();
    static LevelLayout[] Layouts(Scene scene) => scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LevelLayout>(true)).ToArray();
    const string PrefabFolder = "Assets/Levels/Prefabs";
    static string ObjectKey(Object value)
    {
        if (value == null) return "null";
        if (EditorUtility.IsPersistent(value))
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id);
            return "asset:"+guid+":"+id;
        }
        var t = value is GameObject go ? go.transform : (value as Component)?.transform;
        if (t == null) return "transient:"+value.GetType().FullName+":"+value.name;
        string path = "";
        for(var p=t;p!=null;p=p.parent) path="/"+p.GetSiblingIndex()+":"+p.name+path;
        if (value is Component c) path+="@"+Array.IndexOf(t.GetComponents<Component>(),c)+":"+c.GetType().FullName;
        return path;
    }
    static void Normalize(JToken token)
    {
        if(token is JObject o && o.Count==1 && o.TryGetValue("instanceID",out var id))
        {
            // Resolve references through SerializedObject below; Editor JSON uses legacy instance IDs.
            o.RemoveAll();o["reference"]="see-serialized-references";return;
        }
        foreach(var child in token.Children().ToArray()) Normalize(child);
    }
    static SortedDictionary<string,string> Snapshot(IEnumerable<GameObject> roots)
    {
        var result=new SortedDictionary<string,string>(StringComparer.Ordinal);
        foreach(var t in roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)))
        foreach(var o in new Object[]{t.gameObject}.Concat(t.GetComponents<Component>()))
        {
            if(o==null) throw new Exception("Missing script at "+ObjectKey(t));
            var data=JToken.Parse(EditorJsonUtility.ToJson(o));Normalize(data);
            var refs=new JObject();using(var so=new SerializedObject(o))
            {
                var p=so.GetIterator();while(p.Next(true))
                    if(p.propertyType==SerializedPropertyType.ObjectReference && p.propertyPath!="m_CorrespondingSourceObject" && p.propertyPath!="m_PrefabInstance" && p.propertyPath!="m_PrefabAsset")
                        refs[p.propertyPath]=ObjectKey(p.objectReferenceValue);
            }
            result.Add(ObjectKey(o),data.ToString(Newtonsoft.Json.Formatting.None)+refs.ToString(Newtonsoft.Json.Formatting.None));
        }
        return result;
    }
    static string[] Differences(SortedDictionary<string,string> before,SortedDictionary<string,string> after) =>
        before.Keys.Union(after.Keys).Where(k=>!before.TryGetValue(k,out var a)||!after.TryGetValue(k,out var b)||a!=b).ToArray();
    static void RequireSame(SortedDictionary<string,string> before,IEnumerable<GameObject> roots,string label)
    {
        var after=Snapshot(roots);var differences=Differences(before,after);
        if(differences.Length==0)return;
        File.WriteAllText(ReportFolder+"/difference.json",Newtonsoft.Json.JsonConvert.SerializeObject(differences.Take(30).Select(k=>new {key=k,before=before.GetValueOrDefault(k),after=after.GetValueOrDefault(k)}),Newtonsoft.Json.Formatting.Indented));
        throw new Exception(label+": "+differences.Length+" changed serialized objects; see difference.json");
    }
    static ConvertToPrefabInstanceSettings Settings() => new ConvertToPrefabInstanceSettings {
        objectMatchMode=ObjectMatchMode.ByHierarchy, componentsNotMatchedBecomesOverride=true,
        gameObjectsNotMatchedBecomesOverride=true, recordPropertyOverridesOfMatches=true,
        changeRootNameToAssetName=false,logInfo=false
    };
    static void ConnectNew(GameObject root,string path)
    {
        if(File.Exists(path))throw new Exception("Refuse to overwrite existing asset: "+path);
        PrefabUtility.SaveAsPrefabAssetAndConnect(root,path,InteractionMode.AutomatedAction,out bool success);
        if(!success)throw new Exception("Prefab save failed: "+path);
    }
    public static object CompactStandalone(string path)
    {
        if(EditorApplication.isPlaying)throw new Exception("Exit Play Mode first.");
        var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.IsValid()||!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
        try
        {
            var layout=Layouts(scene).Single();
            // Persist any unsaved authored mesh before it becomes an inherited asset reference.
            LevelBuilder.Persist(layout,path);
            var before=Snapshot(scene.GetRootGameObjects());
            Directory.CreateDirectory(PrefabFolder);AssetDatabase.Refresh();
            string asset=PrefabFolder+"/"+Path.GetFileNameWithoutExtension(path)+".prefab";
            ConnectNew(layout.gameObject,asset);
            RequireSame(before,scene.GetRootGameObjects(),path);
            if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed: "+path);
            RequireSame(before,scene.GetRootGameObjects(),path+" after save");
            File.WriteAllText(ReportFolder+"/"+Path.GetFileNameWithoutExtension(path)+"-snapshot.json",Newtonsoft.Json.JsonConvert.SerializeObject(before));
            return new {path,asset,objects=before.Count,lines=File.ReadLines(path).Count()};
        }
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
    }
    static readonly string[] LevelNames={"StartingRoom","LevelSchool","LevelLibrary","LevelHome","LevelGrocery","LevelWedding","LevelHospital","LevelBoss"};
    public static object CompactWorld()
    {
        if(EditorApplication.isPlaying)throw new Exception("Exit Play Mode first.");
        var scene=SceneManager.GetSceneByPath(WorldPath);if(!scene.IsValid()||!scene.isLoaded)scene=EditorSceneManager.OpenScene(WorldPath,OpenSceneMode.Additive);
        var world=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LevelWorld>(true)).Single();
        var before=Snapshot(scene.GetRootGameObjects());
        File.WriteAllText(ReportFolder+"/world-snapshot.json",Newtonsoft.Json.JsonConvert.SerializeObject(before));
        var report=new List<object>();
        for(int index=0;index<world.levels.Count;index++)
        {
            var layout=world.levels[index];string asset=PrefabFolder+"/"+LevelNames[index]+".prefab";
            var originalLevel=Snapshot(new[]{layout.gameObject});
            File.WriteAllText(ReportFolder+"/progress.txt","Converting "+layout.name);
            var template=AssetDatabase.LoadAssetAtPath<GameObject>(asset);
            int linked=0;var rejected=new List<string>();
            if(template!=null)
            {
                var sourceProps=template.transform.Find("Props");var props=layout.transform.Find("Props");
                var lookup=sourceProps.Cast<Transform>().GroupBy(t=>t.name).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g.Single().gameObject));
                foreach(var group in props.Cast<Transform>().Where(t=>!PrefabUtility.IsPartOfPrefabInstance(t)).Where(t=>lookup.ContainsKey(t.name)).GroupBy(t=>lookup[t.name]).ToArray())
                {
                    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(group.Key);if(prefab==null)continue;
                    var targets=group.Select(t=>t.gameObject).ToArray();var original=Snapshot(targets);
                    Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();
                    PrefabUtility.ConvertToPrefabInstances(targets,prefab,Settings(),InteractionMode.UserAction);
                    if(Differences(original,Snapshot(targets)).Length==0)linked+=targets.Length;
                    else{Undo.RevertAllDownToGroup(undo);RequireSame(original,targets,"Prop rollback");rejected.Add(group.Key);}
                }
                var levelOriginal=Snapshot(new[]{layout.gameObject});
                Undo.IncrementCurrentGroup();int undoLevel=Undo.GetCurrentGroup();
                PrefabUtility.ConvertToPrefabInstance(layout.gameObject,template,Settings(),InteractionMode.UserAction);
                if(Differences(levelOriginal,Snapshot(new[]{layout.gameObject})).Length>0)
                {
                    Undo.RevertAllDownToGroup(undoLevel);RequireSame(levelOriginal,new[]{layout.gameObject},"Level rollback");
                    asset=PrefabFolder+"/"+LevelNames[index]+"World.prefab";
                    ConnectNew(layout.gameObject,asset);
                }
            }
            else ConnectNew(layout.gameObject,asset);
            report.Add(new{level=layout.name,asset,linked,rejected});
            RequireSame(originalLevel,new[]{layout.gameObject},"World after "+layout.name);
            File.WriteAllText(ReportFolder+"/world-conversion.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        }
        if(!EditorSceneManager.SaveScene(scene))throw new Exception("World save failed");
        RequireSame(before,scene.GetRootGameObjects(),"World saved");
        File.WriteAllText(ReportFolder+"/world-conversion.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return report;
    }
    public static object Audit()
    {
        if (EditorApplication.isPlaying) throw new Exception("Exit Play Mode first.");
        Directory.CreateDirectory(ReportFolder+"/Before");
        var active = SceneManager.GetActiveScene();
        var rows = new List<object>();
        foreach (var path in Paths)
        {
            var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var backup = ReportFolder+"/Before/"+Path.GetFileName(path);
                if (!File.Exists(backup))
                {
                    if (scene.isDirty) { if(!EditorSceneManager.SaveScene(scene,backup,true)) throw new Exception("Backup failed: "+path); }
                    else File.Copy(path,backup);
                }
                rows.Add(new { path, scene.isDirty, lines=File.ReadLines(path).Count(), layouts=Layouts(scene).Select(l=>new {
                    l.name, prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(l), objects=l.GetComponentsInChildren<Transform>(true).Length,
                    colliders=l.GetComponentsInChildren<Collider>(true).Length,
                    props=l.transform.Find("Props")?.Cast<Transform>().Select(t=>new {t.name, objects=t.GetComponentsInChildren<Transform>(true).Length, prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t)}).ToArray(),
                    elevators=l.GetComponentsInChildren<LevelElevator>(true).Select(e=>new {e.name,pos=e.transform.localPosition.ToString("R")}).ToArray()
                }).ToArray() });
            }
            finally {if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
        SceneManager.SetActiveScene(active);
        File.WriteAllText(ReportFolder+"/audit-before.json",Newtonsoft.Json.JsonConvert.SerializeObject(rows,Newtonsoft.Json.Formatting.Indented));
        return new {report=ReportFolder+"/audit-before.json",scenes=rows.Count};
    }
    public static object VerifySaved() => VerifyPaths(Paths);
    public static object VerifyWorld() => VerifyPaths(new[]{WorldPath});
    static object VerifyPaths(string[] paths)
    {
        if(EditorApplication.isPlaying)throw new Exception("Exit Play Mode first.");
        var active=SceneManager.GetActiveScene();var results=new List<object>();
        foreach(var path in paths)
        {
            var old=SceneManager.GetSceneByPath(path);
            if(old.IsValid()&&old.isLoaded&&old.isDirty)throw new Exception("Unexpected unsaved changes: "+path);
            if(old.IsValid()&&old.isLoaded)EditorSceneManager.CloseScene(old,true);
            var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            string snapshot=path==WorldPath?"world-snapshot.json":Path.GetFileNameWithoutExtension(path)+"-snapshot.json";
            var before=Newtonsoft.Json.JsonConvert.DeserializeObject<SortedDictionary<string,string>>(File.ReadAllText(ReportFolder+"/"+snapshot));
            RequireSame(before,scene.GetRootGameObjects(),"Reload "+path);
            foreach(var layout in Layouts(scene))
                if(!PrefabUtility.IsPartOfPrefabInstance(layout))throw new Exception("Unlinked level: "+layout.name);
            var worlds=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LevelWorld>(true)).ToArray();
            foreach(var w in worlds)
            {
                if(w.levels.Count!=8||w.connections.Count!=7||w.startingLevel!=w.levels[0])throw new Exception("World routing count changed");
                foreach(var c in w.connections)
                {
                    if(c.source==null||c.destination==null||!w.TryDestination(c.source,out var dest,out var seconds)||dest!=c.destination||seconds!=c.travelSeconds)
                        throw new Exception("Broken route");
                    if(c.source.Level==c.destination.Level||!w.levels.Contains(c.source.Level)||!w.levels.Contains(c.destination.Level))throw new Exception("Invalid endpoint ownership");
                }
            }
            var bytes=File.ReadAllBytes(path);
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            RequireSame(before,scene.GetRootGameObjects(),"Repeat save "+path);
            bool stable=bytes.SequenceEqual(File.ReadAllBytes(path));
            results.Add(new{path,serializedObjects=before.Count,stableSave=stable,lines=File.ReadLines(path).Count(),connections=worlds.Sum(w=>w.connections.Count)});
            if(!stable)throw new Exception("Repeat save modified serialized scene: "+path);
            if(path!=WorldPath)EditorSceneManager.CloseScene(scene,true);
        }
        SceneManager.SetActiveScene(SceneManager.GetSceneByPath(WorldPath));
        File.WriteAllText(ReportFolder+(paths.Length==1?"/verification-world.json":"/verification.json"),Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));return results;
    }
    public static object CompactWorldProps(string path)
    {
        var root=PrefabUtility.LoadPrefabContents(path);var created=new List<string>();bool saved=false;
        try
        {
            var before=Snapshot(new[]{root});var props=root.transform.Find("Props");
            string Shape(Transform t) => string.Join("|",t.GetComponentsInChildren<Transform>(true).Select(x=>(x==t?"root":x.name)+":"+string.Join(",",x.GetComponents<Component>().Select(c=>c.GetType().FullName))));
            var groups=props.Cast<Transform>().Where(t=>!PrefabUtility.IsPartOfPrefabInstance(t))
                .GroupBy(t=>System.Text.RegularExpressions.Regex.Replace(t.name,@" \(\d+\)$","")+"|"+Shape(t))
                .Where(g=>g.Count()>1&&g.Sum(t=>t.GetComponentsInChildren<Transform>(true).Length)>30).ToArray();
            string folder=PrefabFolder+"/Shared";Directory.CreateDirectory(folder);AssetDatabase.Refresh();
            foreach(var group in groups)
            {
                var objects=group.Select(t=>t.gameObject).ToArray();string name=System.Text.RegularExpressions.Regex.Replace(objects[0].name,@" \(\d+\)$","");
                string asset=AssetDatabase.GenerateUniqueAssetPath(folder+"/"+Path.GetFileNameWithoutExtension(path)+"_"+name+".prefab");
                ConnectNew(objects[0],asset);created.Add(asset);
                PrefabUtility.ConvertToPrefabInstances(objects.Skip(1).ToArray(),AssetDatabase.LoadAssetAtPath<GameObject>(asset),Settings(),InteractionMode.AutomatedAction);
            }
            RequireSame(before,new[]{root},"Deduplicated props "+path);
            PrefabUtility.SaveAsPrefabAsset(root,path,out bool success);if(!success)throw new Exception("Save failed "+path);saved=true;
            return new {path,groups=groups.Length,created};
        }
        finally{PrefabUtility.UnloadPrefabContents(root);if(!saved)foreach(var p in created)AssetDatabase.DeleteAsset(p);}
    }
}
