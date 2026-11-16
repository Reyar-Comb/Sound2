using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Newtonsoft.Json.Linq;
using Sound2.LevelEditing;
using Object=UnityEngine.Object;

public static class CleanupGroceryProps
{
    const string Asset="Assets/Levels/Prefabs/LevelGroceryWorld.prefab";
    const string World="Assets/Scenes/SoundAndShell.unity";
    const string Folder="Logs/LevelSerialization";
    static HashSet<string> Names()=>JObject.Parse(File.ReadAllText(Folder+"/grocery-commit-audit.json"))["model_removed_on_surviving_props"].Values<string>().ToHashSet();
    static string Node(Transform t,Transform root)
    {
        if(t==root)return "/";
        int ordinal=0;for(int i=0;i<t.GetSiblingIndex();i++)if(t.parent.GetChild(i).name==t.name)ordinal++;
        return Node(t.parent,root)+t.name+"["+ordinal+"]/";
    }
    static string Ref(Object o,Transform root)
    {
        if(o==null)return "null";
        var t=o is GameObject g?g.transform:(o as Component)?.transform;
        if(t!=null&&(t==root||t.IsChildOf(root)))
        {
            string key=Node(t,root);
            if(o is Component c)key+=c.GetType().FullName+"#"+Array.IndexOf(t.GetComponents(c.GetType()),c);
            return key;
        }
        if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string guid,out long id))return guid+":"+id;
        return o.GetType().FullName+":"+o.name;
    }
    static void Normalize(JToken token)
    {
        if(token is JObject o&&(o.ContainsKey("instanceID")||o.ContainsKey("fileID")))
        {o.RemoveAll();o["reference"]="see-refs";return;}
        foreach(var child in token.Children().ToArray())Normalize(child);
    }
    static SortedDictionary<string,string> State(GameObject root,bool ignorePlacement=false)
    {
        var result=new SortedDictionary<string,string>();
        foreach(var t in root.GetComponentsInChildren<Transform>(true))
        foreach(var obj in new Object[]{t.gameObject}.Concat(t.GetComponents<Component>()))
        {
            if(obj==null)throw new Exception("Missing script: "+t.name);
            var json=JObject.Parse(EditorJsonUtility.ToJson(obj));Normalize(json);
            var body=(JObject)json.Properties().Single().Value;
            if(ignorePlacement&&t==root.transform)
            {
                if(obj is GameObject)body.Remove("m_Name");
                if(obj is Transform)foreach(var key in new[]{"m_LocalPosition","m_LocalRotation","m_LocalScale","m_LocalEulerAnglesHint"})body.Remove(key);
            }
            var refs=new JObject();using(var so=new SerializedObject(obj))
            {
                var p=so.GetIterator();while(p.Next(true))
                    if(p.propertyType==SerializedPropertyType.ObjectReference&&!new[]{"m_CorrespondingSourceObject","m_PrefabInstance","m_PrefabAsset","m_Father"}.Contains(p.propertyPath)&&!p.propertyPath.StartsWith("m_Children."))
                        refs[p.propertyPath]=Ref(p.objectReferenceValue,root.transform);
            }
            result[Ref(obj,root.transform)]=json.ToString(Newtonsoft.Json.Formatting.None)+refs.ToString(Newtonsoft.Json.Formatting.None);
        }
        return result;
    }
    static HashSet<Object> DeletedObjects(IEnumerable<GameObject> roots)=>roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).SelectMany(t=>new Object[]{t.gameObject}.Concat(t.GetComponents<Component>())).ToHashSet();
    static void CheckReferences(IEnumerable<GameObject> roots,HashSet<Object> removed)
    {
        foreach(var c in roots.SelectMany(r=>r.GetComponentsInChildren<Component>(true)))
        {
            if(c==null||removed.Contains(c))continue;
            using var so=new SerializedObject(c);var p=so.GetIterator();
            while(p.Next(true))
                if(p.propertyType==SerializedPropertyType.ObjectReference&&removed.Contains(p.objectReferenceValue)&&!p.propertyPath.StartsWith("m_Children."))
                    throw new Exception("Incoming reference: "+c.name+"."+p.propertyPath);
        }
    }
    static string Route(LevelWorld w)=>string.Join("\n",w.connections.Select(c=>c.source.Level.name+"/"+c.source.name+" -> "+c.destination.Level.name+"/"+c.destination.name+" "+c.bidirectional+" "+c.travelSeconds));
    public static object Run()
    {
        if(EditorApplication.isPlaying||PrefabStageUtility.GetCurrentPrefabStage()!=null)throw new Exception("Requires edit mode outside Prefab Stage");
        var names=Names();if(names.Count!=68)throw new Exception("Unexpected deletion manifest");
        var scene=SceneManager.GetSceneByPath(World);bool opened=!scene.IsValid()||!scene.isLoaded;
        if(opened)scene=EditorSceneManager.OpenScene(World,OpenSceneMode.Additive);
        var w=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LevelWorld>(true)).Single();
        var live=w.levels.Single(l=>l.name.StartsWith("04 -"));var route=Route(w);
        var sceneTargets=live.transform.Find("Props").Cast<Transform>().Where(t=>names.Contains(t.name)).Select(t=>t.gameObject).ToArray();
        if(sceneTargets.Length!=68)throw new Exception("Main scene no longer matches manifest");
        CheckReferences(scene.GetRootGameObjects(),DeletedObjects(sceneTargets));
        string backup=Folder+"/LevelGroceryWorld-before-removal.prefab";
        if(!File.Exists(backup))File.Copy(Asset,backup);
        var root=PrefabUtility.LoadPrefabContents(Asset);
        var remaining=new Dictionary<string,SortedDictionary<string,string>>();
        object result;
        try
        {
            var props=root.transform.Find("Props");int before=props.childCount;
            var targets=props.Cast<Transform>().Where(t=>names.Contains(t.name)).Select(t=>t.gameObject).ToArray();
            if(targets.Length!=68||targets.Any(g=>g.GetComponentsInChildren<Renderer>(true).Length!=0))throw new Exception("Refuse to remove a visible or unmatched prop");
            var removed=DeletedObjects(targets);CheckReferences(new[]{root},removed);
            foreach(Transform t in props)if(!names.Contains(t.name))remaining.Add(t.name,State(t.gameObject));
            int colliders=targets.Sum(g=>g.GetComponentsInChildren<Collider>(true).Length),objects=targets.Sum(g=>g.GetComponentsInChildren<Transform>(true).Length);
            foreach(var g in targets)Object.DestroyImmediate(g);
            foreach(Transform t in props)if(!remaining[t.name].SequenceEqual(State(t.gameObject)))throw new Exception("Survivor changed: "+t.name);
            PrefabUtility.SaveAsPrefabAsset(root,Asset,out bool saved);if(!saved)throw new Exception("Save failed");
            result=new{asset=Asset,before,removed=68,after=props.childCount,removedObjects=objects,removedColliders=colliders,names=names.OrderBy(n=>n).ToArray()};
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        root=PrefabUtility.LoadPrefabContents(Asset);
        try
        {
            foreach(Transform t in root.transform.Find("Props"))
                if(names.Contains(t.name)||!remaining[t.name].SequenceEqual(State(t.gameObject)))throw new Exception("Reload mismatch: "+t.name);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        if(live.transform.Find("Props").childCount!=174||Route(w)!=route)throw new Exception("Main scene propagation/routing mismatch");
        foreach(var c in w.connections)if(!w.TryDestination(c.source,out var d,out _)||d!=c.destination)throw new Exception("Broken route");
        File.WriteAllText(Folder+"/grocery-removal.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));
        File.WriteAllText(Folder+"/grocery-survivors.json",Newtonsoft.Json.JsonConvert.SerializeObject(remaining));
        if(opened)EditorSceneManager.CloseScene(scene,true);
        return result;
    }
    public static object Differences()
    {
        var root=PrefabUtility.LoadPrefabContents(Asset);var report=new List<object>();
        try
        {
            foreach(Transform t in root.transform.Find("Props"))
            {
                string name=System.Text.RegularExpressions.Regex.Replace(t.name,@" \(\d+\)$","");
                if(!new[]{"EmptyDoubleShelf","SchoolChalkBox","TeddyBear","PickupGlassMug","PickupCeramicVase"}.Contains(name))continue;
                var original=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+name+".prefab");
                var a=State(t.gameObject,true);var b=State(original,true);
                var diffs=a.Keys.Union(b.Keys).Where(k=>!a.TryGetValue(k,out var av)||!b.TryGetValue(k,out var bv)||av!=bv).ToArray();
                report.Add(new{name=t.name,original="Assets/Prefabs/"+name+".prefab",differences=diffs.Select(k=>new{key=k,current=a.GetValueOrDefault(k),source=b.GetValueOrDefault(k)}).ToArray()});
            }
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        File.WriteAllText(Folder+"/grocery-effective-differences.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return new{compared=report.Count,report=Folder+"/grocery-effective-differences.json"};
    }
    public static object ReuseOriginals()
    {
        var root=PrefabUtility.LoadPrefabContents(Asset);int changed=0;
        var files=new HashSet<string>();
        var survivors=Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string,SortedDictionary<string,string>>>(File.ReadAllText(Folder+"/grocery-survivors.json"));
        try
        {
            var groups=root.transform.Find("Props").Cast<Transform>().GroupBy(t=>System.Text.RegularExpressions.Regex.Replace(t.name,@" \(\d+\)$","")).ToArray();
            foreach(var group in groups)
            {
                if(!new[]{"EmptyDoubleShelf","SchoolChalkBox","TeddyBear","PickupGlassMug","PickupCeramicVase"}.Contains(group.Key))continue;
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/"+group.Key+".prefab");
                var canonical=State(prefab,true);var objects=group.Select(t=>t.gameObject).ToArray();
                foreach(var obj in objects)
                {
                    if(!canonical.SequenceEqual(State(obj,true)))throw new Exception("Non-equivalent object: "+obj.name);
                    string old=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(obj);
                    if(!old.StartsWith("Assets/Levels/Prefabs/Shared/"))throw new Exception("Unexpected original source: "+old);
                    files.Add(old);
                }
                PrefabUtility.ReplacePrefabAssetOfPrefabInstances(objects,prefab,new PrefabReplacingSettings {
                    objectMatchMode=ObjectMatchMode.ByHierarchy,prefabOverridesOptions=PrefabOverridesOptions.KeepAllPossibleOverrides,
                    changeRootNameToAssetName=false,logInfo=false
                },InteractionMode.AutomatedAction);
                foreach(var obj in objects)if(!survivors[obj.name].SequenceEqual(State(obj)))throw new Exception("Changed while relinking: "+obj.name);
                changed+=objects.Length;
            }
            PrefabUtility.SaveAsPrefabAsset(root,Asset,out bool saved);if(!saved)throw new Exception("Save failed");
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        root=PrefabUtility.LoadPrefabContents(Asset);
        try
        {
            foreach(Transform t in root.transform.Find("Props"))
                if(!survivors[t.name].SequenceEqual(State(t.gameObject)))throw new Exception("Reload changed survivor: "+t.name);
        }
        finally{PrefabUtility.UnloadPrefabContents(root);}
        var report=new{relinked=changed,obsoletePrefabs=files.OrderBy(p=>p).ToArray()};
        File.WriteAllText(Folder+"/grocery-relinked.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));return report;
    }
}
