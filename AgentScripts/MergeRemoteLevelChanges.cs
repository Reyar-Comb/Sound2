using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEditor.SceneManagement;
using Sound2.LevelEditing;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

// Applies the audited remote object/field delta to the local prefab-backed levels.
// Source scenes are untouched copies of Git objects, imported temporarily for Unity to resolve references.
public static class MergeRemoteLevelChanges
{
    const string Folder="Logs/RemoteMerge";
    const string Review="Assets/__RemoteMergeReview/RemoteWorld.unity";
    static readonly string[] AssetsByLevel={"StartingRoom","LevelSchoolWorld","LevelLibrary","LevelHome","LevelGroceryWorld","LevelWeddingWorld","LevelHospital","LevelBoss"};
    static readonly string[] CollectionAssets={"CollectibleSqueakyToy","CollectibleWaterCup","CollectibleAlarmClock","CollectibleEarmuffs","CollectibleSpeaker","CollectibleHeartMonitor"};
    static readonly Dictionary<string,string> CollectionIds=new() {
        {"school.squeaky-toy","d6b663e0b25ff1949bd1c6eba0a1f795"}, {"library.water-cup","18314272edca1bf449a66f7189639f8f"},
        {"home.alarm-clock","210bc21bc29559f4b98c92e08144c7ed"}, {"supermarket.earmuffs","6db539294fa7fab45adeb43530cab589"},
        {"wedding.speaker","d8e81c0d670ca6044bc16d0cadec21f0"}, {"hospital.heart-monitor","a943dcd2b87e68348a5a8ea8cd92ba9e"}
    };
    static readonly List<object> Events=new();
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static string Relative(string path,string level) { var prefix="Sound & Shell - Level World/"+level; Check(path.StartsWith(prefix),path); return path.Length==prefix.Length?"":path.Substring(prefix.Length+1); }
    static Transform Find(Transform root,string path)=>string.IsNullOrEmpty(path)?root:root.Find(path);
    static void Map(Transform from,Transform to,Dictionary<Object,Object> map)
    {
        map[from.gameObject]=to.gameObject;map[from]=to;
        foreach(var group in from.GetComponents<Component>().Where(c=>c!=null).GroupBy(c=>c.GetType()))
        {
            var targets=to.GetComponents<Component>().Where(c=>c!=null&&c.GetType()==group.Key).ToArray();int i=0;foreach(var c in group){if(i<targets.Length)map[c]=targets[i];i++;}
        }
        var groups=to.Cast<Transform>().GroupBy(t=>t.name).ToDictionary(g=>g.Key,g=>g.ToArray());
        var index=new Dictionary<string,int>();
        foreach(Transform child in from)
        {
            int n=index.GetValueOrDefault(child.name);index[child.name]=n+1;
            if(groups.TryGetValue(child.name,out var matches)&&n<matches.Length)Map(child,matches[n],map);
        }
    }
    static void Remap(Object value,Dictionary<Object,Object> map,Scene source,bool strict)
    {
        using var so=new SerializedObject(value);var p=so.GetIterator();bool changed=false;
        while(p.Next(true))
        {
            if(p.propertyType!=SerializedPropertyType.ObjectReference || p.propertyPath.StartsWith("m_Prefab") || p.propertyPath=="m_CorrespondingSourceObject" || p.propertyPath=="m_GameObject" || p.propertyPath=="m_Father" || p.propertyPath.StartsWith("m_Children") || p.propertyPath.StartsWith("m_Component"))continue;
            var reference=p.objectReferenceValue;if(reference==null||EditorUtility.IsPersistent(reference))continue;
            if(map.TryGetValue(reference,out var target)){p.objectReferenceValue=target;changed=true;continue;}
            var go=reference as GameObject ?? (reference as Component)?.gameObject;
            if(strict && go!=null && go.scene==source)throw new Exception("Unmapped remote reference: "+value.name+"."+p.propertyPath+" -> "+go.name);
        }
        if(changed)so.ApplyModifiedPropertiesWithoutUndo();
    }
    static GameObject CopyAddition(Transform src,Transform parent)
    {
        var c=src.GetComponent<StoryCollectible>();
        if(c==null)return Object.Instantiate(src.gameObject,parent,false);
        Check(CollectionIds.TryGetValue(c.CollectibleId,out var guid),"Unknown collectible "+c.CollectibleId);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));Check(prefab!=null,"Missing collectible prefab "+guid);
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);go.name=src.name;
        var map=new Dictionary<Object,Object>();Map(src,go.transform,map);
        foreach(var t in src.GetComponentsInChildren<Transform>(true))
        {
            if(!map.ContainsKey(t))
            {
                Check(map.ContainsKey(t.parent),"Prefab parent mismatch "+t.name);
                var child=Object.Instantiate(t.gameObject,(Transform)map[t.parent],false);child.name=t.name;Map(t,child.transform,map);
            }
            var target=(Transform)map[t];
            foreach(var group in t.GetComponents<Component>().Where(x=>x!=null).GroupBy(x=>x.GetType()))
                while(target.GetComponents<Component>().Count(c=>c!=null&&c.GetType()==group.Key)<group.Count())target.gameObject.AddComponent(group.Key);
        }
        map.Clear();Map(src,go.transform,map);
        foreach(var t in src.GetComponentsInChildren<Transform>(true))
        {
            Check(map.ContainsKey(t),"Prefab hierarchy mismatch "+src.name+"/"+t.name);
            var target=(Transform)map[t];target.SetLocalPositionAndRotation(t.localPosition,t.localRotation);target.localScale=t.localScale;
            using(var fromTransform=new SerializedObject(t))using(var toTransform=new SerializedObject(target))
            {toTransform.CopyFromSerializedProperty(fromTransform.FindProperty("m_LocalEulerAnglesHint"));toTransform.ApplyModifiedPropertiesWithoutUndo();}
            target.gameObject.layer=t.gameObject.layer;target.gameObject.tag=t.gameObject.tag;target.gameObject.SetActive(t.gameObject.activeSelf);
            foreach(var component in t.GetComponents<Component>().Where(x=>x!=null && !(x is Transform)))
            {
                Check(map.TryGetValue(component,out var copy),"Prefab component mismatch "+t.name+" "+component.GetType());
                using var from=new SerializedObject(component);using var to=new SerializedObject(copy);var p=from.GetIterator();bool enter=true;
                while(p.Next(enter))
                {
                    enter=false;
                    if(p.propertyPath=="m_GameObject"||p.propertyPath=="m_Script"||p.propertyPath=="m_ObjectHideFlags"||p.propertyPath=="m_CorrespondingSourceObject"||p.propertyPath.StartsWith("m_Prefab"))continue;
                    to.CopyFromSerializedProperty(p);
                }
                to.ApplyModifiedPropertiesWithoutUndo();Remap(copy,map,src.gameObject.scene,false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(copy);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);PrefabUtility.RecordPrefabInstancePropertyModifications(target.gameObject);
        }
        return go;
    }
    public static object Apply()=>ApplyFrom(1);
    public static object ApplyFrom(int first)
    {
        Check(!EditorApplication.isPlaying,"Exit play mode");
        Events.Clear();if(first>1)Events.AddRange(JArray.Parse(File.ReadAllText(Folder+"/migration.json")).Cast<object>());
        var audit=JObject.Parse(File.ReadAllText("Logs/LevelSerialization/remote-scene-audit.json"));
        var source=EditorSceneManager.OpenScene(Review,OpenSceneMode.Additive);
        try
        {
            var world=source.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LevelWorld>(true)).Single();
            for(int index=first;index<world.levels.Count;index++)
            {
                var remote=world.levels[index].transform;string prefix="Sound & Shell - Level World/"+remote.name;
                string asset="Assets/Levels/Prefabs/"+AssetsByLevel[index]+".prefab";
                var root=PrefabUtility.LoadPrefabContents(asset);
                try
                {
                    var added=((JArray)audit["added_roots"]).Values<string>().Where(p=>p.StartsWith(prefix+"/")).Distinct().ToArray();
                    var removed=((JArray)audit["removed_roots"]).Values<string>().Where(p=>p.StartsWith(prefix+"/")).Distinct().ToArray();
                    var deferredDeletes=new List<GameObject>();var addedObjects=new List<GameObject>();var replacements=new Dictionary<Object,Object>();
                    foreach(string path in removed)
                    {
                        string rel=Relative(path,remote.name);var old=Find(root.transform,rel);Check(old!=null,"Missing local removal "+asset+":"+rel);
                        deferredDeletes.Add(old.gameObject);old.name+="__merge_old";
                        Events.Add(new{asset,action="remove",path=rel});
                    }
                    foreach(string path in added)
                    {
                        string rel=Relative(path,remote.name);var src=Find(remote,rel);Check(src!=null,"Missing remote addition "+path);
                        Check(Find(root.transform,rel)==null,"Local addition already exists "+rel);
                        int slash=rel.LastIndexOf('/');var parent=slash<0?root.transform:Find(root.transform,rel.Substring(0,slash));Check(parent!=null,"Missing parent "+rel);
                        var clone=CopyAddition(src,parent);clone.name=src.name;
                        clone.transform.localPosition=src.localPosition;clone.transform.localRotation=src.localRotation;clone.transform.localScale=src.localScale;
                        var old=deferredDeletes.FirstOrDefault(g=>g.name==src.name+"__merge_old" && g.transform.parent==parent);
                        if(old!=null){clone.transform.SetSiblingIndex(old.transform.GetSiblingIndex());Map(old.transform,clone.transform,replacements);}
                        addedObjects.Add(clone);Events.Add(new{asset,action="add",path=rel});
                    }
                    // Home's navigation component is the only remote component added to an existing level root.
                    foreach(JObject row in (JArray)audit["added_scripts"])
                    {
                        string path=(string)row["path"];if(path!=prefix)continue;
                        string script=((string)row["script"])?.Replace('\\','/');var type=AssetDatabase.LoadAssetAtPath<MonoScript>(script)?.GetClass();
                        Check(type!=null,"Cannot load script "+script);Check(root.GetComponent(type)==null,"Existing local added component "+script);
                        var c=root.AddComponent(type);EditorUtility.CopySerialized(remote.GetComponent(type),c);Events.Add(new{asset,action="add-component",script});
                    }
                    var map=new Dictionary<Object,Object>();Map(remote,root.transform,map);
                    foreach(JObject row in (JArray)audit["modified"])
                    {
                        string path=(string)row["path"];if(path!=prefix&&!path.StartsWith(prefix+"/"))continue;
                        int kind=(int)row["kind"];if(kind==1)continue; // Edit-time active-level selection is owned by the local aggregate scene.
                        string rel=Relative(path,remote.name);var src=Find(remote,rel);var dst=Find(root.transform,rel);Check(src!=null&&dst!=null,"Modified path not found "+rel);
                        Object from=src,to=dst;
                        if(kind==114){var type=AssetDatabase.LoadAssetAtPath<MonoScript>(((string)row["script"]).Replace('\\','/')).GetClass();from=src.GetComponent(type);to=dst.GetComponent(type);}
                        Check(from!=null&&to!=null,"Missing modified component "+rel);
                        using(var sourceSo=new SerializedObject(from))using(var targetSo=new SerializedObject(to))
                        {
                            foreach(var field in ((JObject)row["fields"]).Properties())
                            {var p=sourceSo.FindProperty(field.Name);Check(p!=null,"Field missing "+field.Name);targetSo.CopyFromSerializedProperty(p);}
                            targetSo.ApplyModifiedPropertiesWithoutUndo();
                        }
                        Remap(to,map,source,true);Events.Add(new{asset,action="fields",path=rel,fields=((JObject)row["fields"]).Properties().Select(p=>p.Name).ToArray()});
                    }
                    foreach(var go in addedObjects)foreach(var c in go.GetComponentsInChildren<Component>(true))if(c!=null)Remap(c,map,source,true);
                    foreach(var c in root.GetComponents<Component>())if(c!=null)Remap(c,map,source,true);
                    foreach(var c in root.GetComponentsInChildren<Component>(true))if(c!=null && !deferredDeletes.Any(d=>c.transform.IsChildOf(d.transform)))Remap(c,replacements,source,false);
                    foreach(var old in deferredDeletes)Object.DestroyImmediate(old);
                    foreach(var go in addedObjects)foreach(var c in go.GetComponentsInChildren<Component>(true))if(c!=null && PrefabUtility.IsPartOfPrefabInstance(c))PrefabUtility.RecordPrefabInstancePropertyModifications(c);
                    Check(root.GetComponentsInChildren<Component>(true).All(c=>c!=null),"Missing script in "+asset);
                    PrefabUtility.SaveAsPrefabAsset(root,asset,out bool saved);Check(saved,"Save failed "+asset);
                    File.WriteAllText(Folder+"/migration.json",Newtonsoft.Json.JsonConvert.SerializeObject(Events,Newtonsoft.Json.Formatting.Indented));
                }
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
            // The standalone grocery has its own authored layout, but received the same collectible replacement.
            string grocery="Assets/Levels/Prefabs/LevelGrocery.prefab";var standalone=PrefabUtility.LoadPrefabContents(grocery);
            try
            {
                var old=standalone.transform.Find("Props/Earmuffs");Check(old!=null,"Standalone earmuffs missing");
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(CollectionIds["supermarket.earmuffs"]));
                var next=(GameObject)PrefabUtility.InstantiatePrefab(prefab,old.parent);
                next.name=old.name;next.transform.SetLocalPositionAndRotation(old.localPosition,old.localRotation);next.transform.localScale=old.localScale;next.transform.SetSiblingIndex(old.GetSiblingIndex());
                var replacements=new Dictionary<Object,Object>();Map(old,next.transform,replacements);
                foreach(var c in standalone.GetComponentsInChildren<Component>(true))if(c!=null)Remap(c,replacements,source,false);
                Object.DestroyImmediate(old.gameObject);PrefabUtility.SaveAsPrefabAsset(standalone,grocery,out bool saved);Check(saved,"Standalone save failed");
                Events.Add(new{asset=grocery,action="replace",path="Props/Earmuffs"});
            }
            finally{PrefabUtility.UnloadPrefabContents(standalone);}
            AssetDatabase.SaveAssets();File.WriteAllText(Folder+"/migration.json",Newtonsoft.Json.JsonConvert.SerializeObject(Events,Newtonsoft.Json.Formatting.Indented));
            return new{operations=Events.Count,report=Folder+"/migration.json"};
        }
        finally{EditorSceneManager.CloseScene(source,true);}
    }
}
