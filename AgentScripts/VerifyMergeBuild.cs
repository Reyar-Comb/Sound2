using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VerifyMergeBuild
{
    static readonly string[] Scenes={"Assets/Scenes/BaiYu-Test.unity","Assets/Scenes/SonarHall.unity","Assets/Scenes/TeammateSonarHall.unity","Assets/Scenes/SampleScene.unity"};
    const string Folder="Logs/MergeValidation_20261114";
    public static object Audit()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
        Directory.CreateDirectory(Folder);var original=SceneManager.GetActiveScene();var checks=new List<object>();var failures=new List<string>();
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Unsaved scene changes must be preserved first.");
        try
        {
            foreach(string path in AssetDatabase.FindAssets("t:Scene",new[]{"Assets/Scenes"}).Select(AssetDatabase.GUIDToAssetPath))
            {
                var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;
                if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                try
                {
                    int missing=0,broken=0;var roots=scene.GetRootGameObjects();
                    foreach(var go in roots.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Select(t=>t.gameObject))
                    {
                        missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                        foreach(var component in go.GetComponents<Component>())
                        {
                            if(component==null)continue;using var serialized=new SerializedObject(component);var property=serialized.GetIterator();
                            while(property.NextVisible(true))if(property.propertyType==SerializedPropertyType.ObjectReference && property.objectReferenceValue==null && property.objectReferenceEntityIdValue!=default)
                            {broken++;failures.Add(path+" / "+go.name+" / "+component.GetType().Name+" / "+property.propertyPath);}
                        }
                    }
                    if(missing>0)failures.Add(path+": "+missing+" missing scripts");
                    int coins=roots.Sum(r=>r.GetComponentsInChildren<CoinPickup>(true).Length),wallets=roots.Sum(r=>r.GetComponentsInChildren<PlayerWallet>(true).Length);
                    if(wallets>0)failures.Add(path+": obsolete wallet still attached");
                    if(coins>0)failures.Add(path+": removed coin objects still present");
                    checks.Add(new {scene=path,missingScripts=missing,brokenReferences=broken,coins,wallets});
                }
                finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
            }
            foreach(var path in new[]{"Assets/Prefabs/BallLightning.prefab"})
            {
                var root=PrefabUtility.LoadPrefabContents(path);
                try{foreach(var t in root.GetComponentsInChildren<Transform>(true))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)failures.Add(path+": missing script");}
                finally{PrefabUtility.UnloadPrefabContents(root);}
            }
        }
        finally{SceneManager.SetActiveScene(original);}
        var result=new {passed=failures.Count==0,checks,failures};File.WriteAllText(Folder+"/audit.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));return result;
    }
    public static object Main()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
        if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone,BuildTarget.StandaloneWindows64))throw new InvalidOperationException("Windows build support is not installed.");
        Directory.CreateDirectory(Folder);
        // Use a per-build scene list. The project's existing Build Settings and packages are unchanged.
        var output="Builds/MergeValidation_20261114/Sound2.exe";
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=Scenes,locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None});
        var summary=report.summary;
        var messages=report.steps.SelectMany(s=>s.messages).Where(m=>m.type==LogType.Error || m.type==LogType.Exception || m.type==LogType.Warning).Select(m=>new {type=m.type.ToString(),m.content}).ToArray();
        var result=new {passed=summary.result==BuildResult.Succeeded,result=summary.result.ToString(),summary.totalErrors,summary.totalWarnings,seconds=summary.totalTime.TotalSeconds,bytes=summary.totalSize,output=Path.GetFullPath(output),scenes=Scenes,messages};
        File.WriteAllText(Folder+"/build.json",Newtonsoft.Json.JsonConvert.SerializeObject(result,Newtonsoft.Json.Formatting.Indented));return result;
    }
}

