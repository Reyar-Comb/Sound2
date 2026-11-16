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
using Object=UnityEngine.Object;

public static class VerifyRemoteLevelMerge
{
    const string Main="Assets/Scenes/SoundAndShell.unity", Review="Assets/__RemoteMergeReview/RemoteWorld.unity", Folder="Logs/RemoteMerge";
    static readonly List<string> Errors=new();
    static void Check(bool b,string message){if(!b)Errors.Add(message);}
    static string Rel(string path,string level){string prefix="Sound & Shell - Level World/"+level;return path.Length==prefix.Length?"":path.Substring(prefix.Length+1);}
    static Transform Find(Transform root,string rel)=>rel.Length==0?root:root.Find(rel);
    static string Ref(Object o,Transform root)
    {
        if(o==null)return "null";
        if(EditorUtility.IsPersistent(o)){AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string guid,out long id);return guid+":"+id;}
        var t=(o as GameObject)?.transform??(o as Component)?.transform;if(t==null)return o.GetType().Name+":"+o.name;
        string p="";while(t!=null&&t!=root){p="/"+t.name+p;t=t.parent;}
        return (t==null?"EXTERNAL:":"")+p+(o is Component c?"@"+c.GetType().FullName+":"+Array.IndexOf(c.GetComponents<Component>().Where(v=>v!=null&&v.GetType()==c.GetType()).ToArray(),c):"@GameObject");
    }
    static bool Equal(SerializedProperty a,SerializedProperty b,Transform ar,Transform br)
    {
        if(b==null||a.propertyType!=b.propertyType)return false;
        if(a.propertyType==SerializedPropertyType.ObjectReference)return Ref(a.objectReferenceValue,ar)==Ref(b.objectReferenceValue,br);
        if(a.propertyType==SerializedPropertyType.Float)return Math.Abs(a.doubleValue-b.doubleValue)<0.00001;
        return SerializedProperty.DataEquals(a,b);
    }
    static bool Skip(string p)=>p=="m_ObjectHideFlags"||p.StartsWith("m_CorrespondingSourceObject")||p.StartsWith("m_Prefab")||p.StartsWith("m_GameObject")||p.StartsWith("m_Father")||p.StartsWith("m_Children")||p.StartsWith("m_Component")||p=="m_RootOrder"||p.EndsWith(".m_FileID")||p.EndsWith(".m_PathID");
    static void CompareTree(Transform a,Transform b,Transform ar,Transform br,string label)
    {
        Check(b!=null,label+" missing");if(b==null)return;
        Check(a.childCount==b.childCount,label+" children count");
        Check(a.GetComponents<Component>().Length==b.GetComponents<Component>().Length,label+" total component count");
        Check(a.gameObject.activeSelf==b.gameObject.activeSelf,label+" active");Check(a.gameObject.layer==b.gameObject.layer,label+" layer");
        foreach(var group in a.GetComponents<Component>().Where(x=>x!=null).GroupBy(x=>x.GetType()))
        {
            var src=group.ToArray();var dst=b.GetComponents<Component>().Where(c=>c!=null&&c.GetType()==group.Key).ToArray();Check(src.Length==dst.Length,label+" component count "+group.Key);
            for(int n=0;n<Math.Min(src.Length,dst.Length);n++)
            {
                using var aso=new SerializedObject(src[n]);using var bso=new SerializedObject(dst[n]);var p=aso.GetIterator();
                while(p.Next(true))
                {
                    if(Skip(p.propertyPath)||p.propertyType==SerializedPropertyType.Generic)continue;
                    Check(Equal(p,bso.FindProperty(p.propertyPath),ar,br),label+" "+group.Key.Name+"."+p.propertyPath);
                }
            }
        }
        var counts=new Dictionary<string,int>();foreach(Transform t in a)
        {
            int n=counts.GetValueOrDefault(t.name);counts[t.name]=n+1;
            var targets=b.Cast<Transform>().Where(x=>x.name==t.name).ToArray();CompareTree(t,n<targets.Length?targets[n]:null,ar,br,label+"/"+t.name+"#"+n);
        }
    }
    public static object Run()
    {
        Errors.Clear();var audit=JObject.Parse(File.ReadAllText("Logs/LevelSerialization/remote-scene-audit.json"));
        var remoteScene=EditorSceneManager.OpenScene(Review,OpenSceneMode.Additive);
        var main=EditorSceneManager.OpenScene(Main,OpenSceneMode.Additive);
        try
        {
            LevelWorld World(Scene s)=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LevelWorld>(true)).Single();
            var remote=World(remoteScene);var local=World(main);int checkedObjects=0,checkedFields=0;var reverted=new List<string>();
            Check(local.levels.Count==8&&local.connections.Count==7,"Level/connection count");
            for(int i=0;i<7;i++)
            {
                var c=local.connections[i];Check(c.source!=null&&c.destination!=null,"Null route "+i);
                if(c.source!=null&&c.destination!=null)Check(c.source.Level==local.levels[i]&&c.destination.Level==local.levels[i+1]&&!c.bidirectional&&c.travelSeconds==3,"Route "+i);
            }
            Check(local.startingLevel==local.levels[0]&&local.startingPoint!=null&&local.player!=null,"Starting refs");
            for(int i=1;i<8;i++)
            {
                var ar=remote.levels[i].transform;var br=local.levels[i].transform;string prefix="Sound & Shell - Level World/"+ar.name;
                Check(PrefabUtility.IsPartOfPrefabInstance(br),"Level lost prefab "+br.name);
                foreach(string path in ((JArray)audit["added_roots"]).Values<string>().Where(p=>p.StartsWith(prefix+"/")).Distinct())
                {string rel=Rel(path,ar.name);CompareTree(Find(ar,rel),Find(br,rel),ar,br,br.name+"/"+rel);checkedObjects++;}
                foreach(string path in ((JArray)audit["removed_roots"]).Values<string>().Where(p=>p.StartsWith(prefix+"/")))
                {string rel=Rel(path,ar.name);if(Find(ar,rel)==null)Check(Find(br,rel)==null,"Removed remote object still present "+rel);}
                foreach(JObject row in (JArray)audit["modified"])
                {
                    string path=(string)row["path"];if(path!=prefix&&!path.StartsWith(prefix+"/"))continue;
                    int kind=(int)row["kind"];if(kind==1)continue;string rel=Rel(path,ar.name);
                    Object a=Find(ar,rel),b=Find(br,rel);
                    if(kind==114){var type=AssetDatabase.LoadAssetAtPath<MonoScript>(((string)row["script"]).Replace('\\','/')).GetClass();a=((Transform)a).GetComponent(type);b=((Transform)b).GetComponent(type);}
                    using var aso=new SerializedObject(a);using var bso=new SerializedObject(b);
                    foreach(var field in ((JObject)row["fields"]).Properties())
                    {
                        var ap=aso.FindProperty(field.Name);var bp=bso.FindProperty(field.Name);
                        if(!Equal(ap,bp,ar,br)&&bp.prefabOverride)
                        {PrefabUtility.RevertPropertyOverride(bp,InteractionMode.AutomatedAction);bso.Update();bp=bso.FindProperty(field.Name);reverted.Add(path+":"+field.Name);}
                        Check(Equal(ap,bp,ar,br),"Changed field mismatch "+path+":"+field.Name);checkedFields++;
                    }
                }
            }
            var ids=local.levels.SelectMany(l=>l.GetComponentsInChildren<StoryCollectible>(true)).Select(c=>c.CollectibleId).ToArray();
            Check(ids.Length==6&&ids.Distinct().Count()==6,"Expected six unique collectibles, got "+string.Join(",",ids));
            var gate=local.levels[7].GetComponentInChildren<BossMemoryGate>(true);
            Check(gate!=null&&gate.startButton!=null&&gate.returnButton!=null&&gate.displayRoot!=null&&gate.memories.Length==6,"Boss gate references");
            if(gate!=null)Check(gate.memories.All(m=>ids.Contains(m.id)&&m.visual!=null),"Gate memory IDs/visuals");
            var removal=JObject.Parse(File.ReadAllText("Logs/LevelSerialization/grocery-removal.json"));
            foreach(string name in ((JArray)removal["names"]).Values<string>())Check(local.levels[4].transform.Find("Props/"+name)==null,"Deleted grocery prop restored: "+name);
            foreach(var c in main.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Component>(true)))Check(c!=null,"Missing main scene script");
            foreach(string asset in Directory.GetFiles("Assets/Levels/Prefabs","*.prefab"))
                Check(!AssetDatabase.GetDependencies(asset.Replace('\\','/'),true).Any(p=>p.Contains("__RemoteMergeReview")||p.Contains("_Recovery")),"Temporary reference in "+asset);
            if(Errors.Count==0)
            {
                Check(EditorSceneManager.SaveScene(main),"Save main");var before=File.ReadAllBytes(Main);
                EditorSceneManager.MarkSceneDirty(main);EditorSceneManager.SaveScene(main);Check(before.SequenceEqual(File.ReadAllBytes(Main)),"Main save not stable");
            }
            var report=new{passed=Errors.Count==0,checkedObjects,checkedFields,revertedSceneOverrides=reverted,errors=Errors,collectibles=ids,
                levels=local.levels.Select(l=>new{l.name,enemies=l.GetComponentsInChildren<LevelEnemy>(true).Length,prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(l)}).ToArray(),lines=File.ReadLines(Main).Count()};
            File.WriteAllText(Folder+"/verification.json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
            return new{report.passed,checkedObjects,checkedFields,errorCount=Errors.Count,errors=Errors.Take(20).ToArray(),report.lines,report.levels};
        }
        finally{EditorSceneManager.CloseScene(remoteScene,true);EditorSceneManager.CloseScene(main,true);}
    }
}
