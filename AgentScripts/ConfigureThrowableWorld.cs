using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ConfigureThrowableWorld
{
    public static object Main()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
        for(int i=0;i<SceneManager.sceneCount;i++)if(SceneManager.GetSceneAt(i).isDirty)throw new InvalidOperationException("Preserve unsaved scenes first.");
        var active=SceneManager.GetActiveScene();int removed=0,configured=0;var changed=new List<string>();
        foreach(string guid in AssetDatabase.FindAssets("t:Prefab",new[]{"Assets"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);if(path=="Assets/Prefabs/Coin.prefab")continue;
            var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if(asset.GetComponentsInChildren<PickupItem>(true).Length==0)continue;
            var root=PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach(var item in root.GetComponentsInChildren<PickupItem>(true)){item.EnsureImpactFeedback();configured++;}
                PrefabUtility.SaveAsPrefabAsset(root,path);changed.Add(path);
            }
            finally{PrefabUtility.UnloadPrefabContents(root);}
        }
        foreach(string guid in AssetDatabase.FindAssets("t:Scene",new[]{"Assets"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);var scene=SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            try
            {
                var roots=scene.GetRootGameObjects();bool dirty=false;
                foreach(var coin in roots.SelectMany(r=>r.GetComponentsInChildren<CoinPickup>(true)).ToArray())
                {
                    var parent=coin.transform.parent;UnityEngine.Object.DestroyImmediate(coin.gameObject);removed++;dirty=true;
                    if(parent!=null && parent.name=="Coins" && parent.childCount==0)UnityEngine.Object.DestroyImmediate(parent.gameObject);
                }
                foreach(var item in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PickupItem>(true)))
                {
                    item.EnsureImpactFeedback();EditorUtility.SetDirty(item.GetComponent<SonarObjectEmitter>());
                    if(PrefabUtility.IsPartOfPrefabInstance(item))PrefabUtility.RecordPrefabInstancePropertyModifications(item.GetComponent<SonarObjectEmitter>());
                    configured++;dirty=true;
                }
                // Re-serialize the test setup after retiring its coin prefab field.
                if(roots.Any(r=>r!=null && r.GetComponentInChildren<BaiYuTestScene>(true)!=null))dirty=true;
                if(dirty){EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);changed.Add(path);}
            }
            finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
        }
        foreach(string guid in AssetDatabase.FindAssets("t:FloorProgressionProfile"))
        {
            var profile=AssetDatabase.LoadAssetAtPath<FloorProgressionProfile>(AssetDatabase.GUIDToAssetPath(guid));bool dirty=false;
            foreach(var rule in profile.overrides)foreach(var cue in rule.entrySubtitles)
            {
                if(!cue.text.Contains("金币") && !cue.text.Contains("硬币"))continue;
                cue.text="普通球、棱晶和方块均可拿起；左键抛出，右键轻放。物品撞击会发出声波，吸引附近怪物。";dirty=true;
            }
            if(dirty){EditorUtility.SetDirty(profile);AssetDatabase.SaveAssetIfDirty(profile);}
        }
        bool deleted=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Coin.prefab")==null || AssetDatabase.DeleteAsset("Assets/Prefabs/Coin.prefab");
        SceneManager.SetActiveScene(active);
        return new{removedCoins=removed,configuredThrowables=configured,coinPrefabRemoved=deleted,changed};
    }
}
