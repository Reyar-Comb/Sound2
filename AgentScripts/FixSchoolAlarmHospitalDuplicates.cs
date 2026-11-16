using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sound2.LevelEditing;
using Object=UnityEngine.Object;

public static class FixSchoolAlarmHospitalDuplicates
{
 const string Dir="Logs/PropInvestigation/Fix";
 const string Hospital="Assets/Levels/Prefabs/LevelHospital.prefab";
 const string School="Assets/Levels/Prefabs/LevelSchoolWorld.prefab";
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static string Node(Transform t,Transform root)=>t==root?"/":Node(t.parent,root)+t.name+"["+t.parent.Cast<Transform>().Take(t.GetSiblingIndex()).Count(x=>x.name==t.name)+"]/";
 static string Ref(Object o,Transform root){if(o==null)return "null";var t=o is GameObject g?g.transform:(o as Component)?.transform;if(t!=null&&(t==root||t.IsChildOf(root)))return Node(t,root)+(o is Component c?c.GetType().FullName+"#"+Array.IndexOf(t.GetComponents(c.GetType()),c):"GameObject");if(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string guid,out long id))return guid+":"+id;return o.GetType().FullName+":"+o.name;}
 static void Normalize(JToken token){if(token is JObject o&&(o.ContainsKey("instanceID")||o.ContainsKey("fileID"))){o.RemoveAll();o["reference"]="see-refs";return;}foreach(var child in token.Children().ToArray())Normalize(child);}
 static SortedDictionary<string,string> State(GameObject root,bool ignorePlacement=false){var result=new SortedDictionary<string,string>();foreach(var t in root.GetComponentsInChildren<Transform>(true))foreach(var obj in new Object[]{t.gameObject}.Concat(t.GetComponents<Component>())){
  Check(obj!=null,"Missing script on "+t.name);var json=JObject.Parse(EditorJsonUtility.ToJson(obj));Normalize(json);var body=(JObject)json.Properties().Single().Value;
  if(ignorePlacement&&t==root.transform){if(obj is GameObject)body.Remove("m_Name");if(obj is Transform)foreach(var key in new[]{"m_LocalPosition","m_LocalRotation","m_LocalScale","m_LocalEulerAnglesHint"})body.Remove(key);}
  var refs=new JObject();using(var so=new SerializedObject(obj)){var p=so.GetIterator();while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&!new[]{"m_CorrespondingSourceObject","m_PrefabInstance","m_PrefabAsset","m_Father"}.Contains(p.propertyPath)&&!p.propertyPath.StartsWith("m_Children."))refs[p.propertyPath]=Ref(p.objectReferenceValue,root.transform);}
  result[Ref(obj,root.transform)]=json.ToString(Formatting.None)+refs.ToString(Formatting.None);
 }return result;}
 static HashSet<Object> Objects(IEnumerable<GameObject> roots)=>roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).SelectMany(t=>new Object[]{t.gameObject}.Concat(t.GetComponents<Component>())).ToHashSet();
 static void References(IEnumerable<GameObject> roots,HashSet<Object> removed){foreach(var c in roots.SelectMany(r=>r.GetComponentsInChildren<Component>(true))){if(c==null||removed.Contains(c))continue;using var so=new SerializedObject(c);var p=so.GetIterator();while(p.Next(true))if(p.propertyType==SerializedPropertyType.ObjectReference&&!p.propertyPath.StartsWith("m_Children.")&&removed.Contains(p.objectReferenceValue))throw new Exception("Incoming reference: "+c.name+"."+p.propertyPath);}}
 static List<(GameObject keep,GameObject remove)> Pairs(GameObject root){var props=root.GetComponentsInChildren<PushableProp>(true).OrderBy(p=>p.name,StringComparer.Ordinal).ToArray();var pairs=new List<(GameObject,GameObject)>();var removed=new HashSet<GameObject>();for(int i=0;i<props.Length;i++){if(removed.Contains(props[i].gameObject))continue;for(int j=i+1;j<props.Length;j++){
  var a=props[i].transform;var b=props[j].transform;if(removed.Contains(b.gameObject))continue;
  if(Vector3.Distance(a.position,b.position)<.001f&&Quaternion.Angle(a.rotation,b.rotation)<.1f&&Vector3.Distance(a.lossyScale,b.lossyScale)<.001f&&PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(a.gameObject)==PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(b.gameObject)){
   var sa=State(a.gameObject,true);var sb=State(b.gameObject,true);Check(sa.SequenceEqual(sb),"Duplicate has distinct content: "+a.name+" / "+b.name+" diff: "+string.Join(",",sa.Keys.Union(sb.Keys).Where(k=>sa.GetValueOrDefault(k)!=sb.GetValueOrDefault(k))));pairs.Add((a.gameObject,b.gameObject));removed.Add(b.gameObject);
  }
 }}return pairs;}
 public static object Run(bool apply=false){
  Check(!Application.isPlaying&&PrefabStageUtility.GetCurrentPrefabStage()==null,"Requires Edit Mode outside Prefab Stage");Directory.CreateDirectory(Dir);
  var scene=SceneManager.GetSceneByPath("Assets/Scenes/SoundAndShell.unity");Check(scene.isLoaded&&!scene.isDirty,"Expected clean loaded main scene");
  var world=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LevelWorld>(true)).Single();
  var hospital=world.levels.Single(l=>l.name.Contains("医院"));var school=world.levels.Single(l=>l.name.Contains("学校"));
  Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(hospital.gameObject)==Hospital,"Hospital source mismatch");Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(school.gameObject)==School,"School source mismatch");
  string route=JsonConvert.SerializeObject(world.connections.Select(c=>new{source=c.source.GetEntityId(),destination=c.destination.GetEntityId(),c.travelSeconds,c.bidirectional}));
  var livePairs=Pairs(hospital.gameObject);Check(livePairs.Count==18,"Expected 18 duplicates");References(scene.GetRootGameObjects(),Objects(livePairs.Select(p=>p.remove)));
  var standalone=EditorSceneManager.OpenScene("Assets/Levels/LevelHospital.unity",OpenSceneMode.Additive);
  try{
   var standaloneRoot=standalone.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<LevelLayout>(true)).Single();var standalonePairs=Pairs(standaloneRoot.gameObject);Check(standalonePairs.Count==18,"Standalone duplicate count mismatch");References(standalone.GetRootGameObjects(),Objects(standalonePairs.Select(p=>p.remove)));
   var h=PrefabUtility.LoadPrefabContents(Hospital);var s=PrefabUtility.LoadPrefabContents(School);
   try{
    var pairs=Pairs(h);Check(pairs.Count==18,"Source duplicate count mismatch");References(new[]{h},Objects(pairs.Select(p=>p.remove)));
    var names=pairs.Select(p=>p.remove.name).ToHashSet();var before=h.transform.Find("Props").Cast<Transform>().Where(t=>!names.Contains(t.name)).ToDictionary(t=>t.name,t=>State(t.gameObject));
    var alarm=s.GetComponentInChildren<PickupAlarm>(true);Check(Mathf.Abs(alarm.transform.localPosition.y-.05597342f)<.0001f,"Alarm height changed since investigation");
    var report=new{apply,removed=pairs.Select(p=>new{keep=p.keep.name,remove=p.remove.name}).ToArray(),incomingReferences=0,beforePushable=h.GetComponentsInChildren<PushableProp>(true).Length,afterPushable=134,alarmBeforeY=alarm.transform.localPosition.y,alarmAfterY=.421f};
    if(apply){
     foreach(var path in new[]{Hospital,School,scene.path,standalone.path}){var backup=Dir+"/before-"+Path.GetFileName(path);Check(!File.Exists(backup),"Backup already exists: "+backup);File.Copy(path,backup);}
     foreach(var pair in pairs)Object.DestroyImmediate(pair.remove);
     foreach(Transform t in h.transform.Find("Props"))Check(before[t.name].SequenceEqual(State(t.gameObject)),"Survivor changed: "+t.name);
     var p=alarm.transform.localPosition;p.y=.421f;alarm.transform.localPosition=p;PrefabUtility.RecordPrefabInstancePropertyModifications(alarm.transform);
     PrefabUtility.SaveAsPrefabAsset(h,Hospital,out bool savedH);Check(savedH,"Hospital save failed");PrefabUtility.SaveAsPrefabAsset(s,School,out bool savedS);Check(savedS,"School save failed");
     Check(route==JsonConvert.SerializeObject(world.connections.Select(c=>new{source=c.source.GetEntityId(),destination=c.destination.GetEntityId(),c.travelSeconds,c.bidirectional})),"Elevator connections changed");
     File.WriteAllText(Dir+"/survivors.json",JsonConvert.SerializeObject(before));
    }
    File.WriteAllText(Dir+(apply?"/applied.json":"/preflight.json"),JsonConvert.SerializeObject(report,Formatting.Indented));return report;
   }finally{PrefabUtility.UnloadPrefabContents(h);PrefabUtility.UnloadPrefabContents(s);}
  }finally{EditorSceneManager.CloseScene(standalone,true);}
 }
 public static object Verify(){
  var roots=new[]{Hospital,School}.Select(PrefabUtility.LoadPrefabContents).ToArray();
  try{
   var h=roots[0];var s=roots[1];var before=JsonConvert.DeserializeObject<Dictionary<string,SortedDictionary<string,string>>>(File.ReadAllText(Dir+"/survivors.json"));
   Check(Pairs(h).Count==0,"Duplicates remain");Check(h.GetComponentsInChildren<PushableProp>(true).Length==134,"Unexpected prop count");Check(h.transform.Find("Props").childCount==before.Count,"Unexpected surviving child count");foreach(Transform t in h.transform.Find("Props"))Check(before[t.name].SequenceEqual(State(t.gameObject)),"Reload changed survivor: "+t.name);
   Check(Mathf.Abs(s.GetComponentInChildren<PickupAlarm>(true).transform.localPosition.y-.421f)<.000001f,"Reloaded alarm mismatch");
   var world=Object.FindAnyObjectByType<LevelWorld>();var liveH=world.levels.Single(l=>l.name.Contains("医院"));var liveS=world.levels.Single(l=>l.name.Contains("学校"));Check(liveH.GetComponentsInChildren<PushableProp>(true).Length==134&&Pairs(liveH.gameObject).Count==0,"Main scene hospital propagation mismatch");Check(Mathf.Abs(liveS.GetComponentInChildren<PickupAlarm>(true).transform.localPosition.y-.421f)<.000001f,"Main scene alarm propagation mismatch");foreach(var c in world.connections)Check(world.TryDestination(c.source,out var d,out _)&&d==c.destination,"Broken elevator route");
   var result=new{savedAndReloaded=true,unchangedSurvivors=before.Count,pushables=134,duplicatePairs=0,alarmY=.421f,mainSceneUpdated=true,validConnections=world.connections.Count};File.WriteAllText(Dir+"/verification.json",JsonConvert.SerializeObject(result,Formatting.Indented));return result;
  }finally{foreach(var root in roots)PrefabUtility.UnloadPrefabContents(root);}
 }
}

