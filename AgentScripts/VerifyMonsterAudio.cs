// Run with Unity CLI script eval in Play Mode. Loads the test scene additively and never saves changes.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
if (!Application.isPlaying) throw new Exception("Play mode required");
var original=SceneManager.GetActiveScene();
var roots=original.GetRootGameObjects().Where(x=>x.activeSelf).ToArray();
var result=new List<string>();
void Check(bool ok,string label){if(!ok)throw new Exception(label);result.Add(label);}
Scene test=default;
GameObject marker=null, area=null;
try {
    foreach(var go in roots)go.SetActive(false);
    test=EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/TeammateSonarHall.unity",new LoadSceneParameters(LoadSceneMode.Additive));
    for(int i=0;i<30 && !test.isLoaded;i++) await Task.Delay(100);
    if (!test.isLoaded) throw new Exception("Test scene failed to load");
    SceneManager.SetActiveScene(test);
    if(SonicSpaceStartMenu.BlocksGameplay)Object.FindAnyObjectByType<SonicSpaceStartMenu>().BeginGame();
    await Task.Delay(800);
    var actor=test.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<PatrolMonster>()).Single();
    var blue=test.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<BallLightning>(true)).ToArray();
    Check(blue.Length>0&&blue.All(b=>b.GetComponentInChildren<MonsterAudioEmitter>(true)==null),"Blue lightning actors do not receive the red monster audio controller");
    var areaPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RedVineArea.prefab");
    area=Object.Instantiate(areaPrefab,new Vector3(5000,0,5000),Quaternion.identity);
    Check(area.GetComponent<MonsterVines>()!=null&&area.GetComponentInChildren<MonsterAudioEmitter>(true)==null,"A static red vine area is not an audio-emitting monster");
    foreach(var lightning in blue)lightning.enabled=false; // Isolate patrol motion from lightning avoidance during distance checks.
    var agent=actor.GetComponent<NavMeshAgent>();
    var scan=actor.GetComponent<MonsterScanEmitter>();
    var emitter=actor.GetComponent<MonsterAudioEmitter>();
    var profile=Resources.Load<MonsterAudioProfile>("MonsterAudio");
    Check(profile.HasClips && profile.crawl.clip.name=="crawl" && profile.hum.clip.name=="hum" && profile.shout.clip.name=="shout","Resources profile loads the three user clips");
    Check(actor.NavigationReady && emitter!=null && emitter.ReplacesScanSound,"Actual scene monster auto-connects without prefab edits");
    Check(scan.Scans>0 && actor.GetComponent<SpatialAudioEmitter>()==null,"Danger scans remain active while the old scan ping stays off");
    foreach(var l in new[]{profile.crawl,profile.hum,profile.shout})Check(l.GainAt(0)==1 && l.GainAt(l.range)==0 && l.GainAt((l.range+l.fullDistance)/2)>.49f,"Independent attenuation: "+l.clip.name);
    marker=new GameObject("Monster audio test listener override");
    marker.transform.SetParent(actor.transform,false);
    emitter.listener=marker.transform;
    var baseOffset=profile.localOrigin;
    void Distance(float d){ marker.transform.position=actor.transform.TransformPoint(baseOffset)+Vector3.right*d; }
    AudioSource Voice(string name)=>actor.GetComponentsInChildren<AudioSource>().SingleOrDefault(s=>s.clip!=null&&s.clip.name==name);
    bool Audible(string name)=>Voice(name) is AudioSource s && s.isPlaying && s.volume>.001f;
    var from=actor.transform.position;
    Distance(9);await Task.Delay(750);
    Check(Vector3.Distance(from,actor.transform.position)>.15f,"Real navigation patrol has horizontal travel");
    Check(Audible("crawl")&&!Audible("hum")&&!Audible("shout"),"9m while moving: crawl alone");
    agent.isStopped=true;agent.ResetPath();agent.updatePosition=false;
    await Task.Delay(300);
    var stationary=actor.transform.position;
    Distance(4);await Task.Delay(400);
    Check(Vector3.Distance(stationary,actor.transform.position)<.02f,"Stopped navigation produces no horizontal travel");
    Check(!Audible("crawl")&&Audible("hum")&&!Audible("shout"),"4m while stationary: hum alone");
    var hum=Voice("hum");float mid=hum.volume;
    Distance(.5f);await Task.Delay(400);
    Check(!Audible("crawl")&&Audible("hum")&&Audible("shout")&&hum.volume>mid,"0.5m: shout layers over louder hum while stationary");
    foreach(var s in new[]{Voice("crawl"),hum,Voice("shout")})
        Check(s.loop && s.spatialBlend==1 && s.spatialize && s.GetComponent<SpatialAudioEmitter>().settings.hrtf && s.transform.parent==actor.transform && Vector3.Distance(s.transform.localPosition,baseOffset)<.001f,"Loop, 3D and same monster origin: "+s.clip.name);
    Distance(20);await Task.Delay(450);
    Check(!Audible("crawl")&&!Audible("hum")&&!Audible("shout")&&!hum.isPlaying,"Outside range: all silent and hum loop paused");
    int oldSamples=hum.timeSamples;
    Distance(.5f);await Task.Delay(400);
    Check(Voice("hum")==hum&&Audible("hum")&&hum.timeSamples!=oldSamples,"Re-entry resumes existing playback instead of spawning another source");
    Check(!actor.GetComponent<SpatialAudioEmitter>(),"The three layered voices do not overwrite the scan audio source");
    actor.gameObject.SetActive(false);
    Check(!hum.isPlaying && !actor.gameObject.activeInHierarchy,"Deactivating the monster stops its audio");
    System.IO.Directory.CreateDirectory("Logs/MonsterAudio");
    System.IO.File.WriteAllLines("Logs/MonsterAudio/play-checks.txt",result);
    return new {passed=true,checks=result};
} finally {
    if(marker!=null)Object.Destroy(marker);
    if(area!=null)Object.Destroy(area);
    foreach(var go in roots)if(go!=null)go.SetActive(true);
    SceneManager.SetActiveScene(original);
    if(test.IsValid() && test.isLoaded) await SceneManager.UnloadSceneAsync(test);
}
