using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ConfigurePhysicalItems
{
    public static object Main()
    {
        if(Application.isPlaying)throw new InvalidOperationException("Exit Play mode first.");
        var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(active.isDirty)throw new InvalidOperationException("Preserve unsaved scene edits before migrating gameplay assets.");
        const string lightningPath="Assets/Prefabs/BallLightning.prefab";
        var lightningRoot=PrefabUtility.LoadPrefabContents(lightningPath);
        try
        {
            var lightning=lightningRoot.GetComponent<BallLightning>();lightning.autoCycle=true;lightning.cloudPointCount=512;
            foreach(var mesh in lightning.visual.GetComponentsInChildren<MeshRenderer>())UnityEngine.Object.DestroyImmediate(mesh);
            foreach(var mesh in lightning.visual.GetComponentsInChildren<MeshFilter>())UnityEngine.Object.DestroyImmediate(mesh);
            PrefabUtility.SaveAsPrefabAsset(lightningRoot,lightningPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(lightningRoot);}
        int removed=0;
        foreach(var path in new[]{"Assets/Scenes/BaiYu-Test.unity","Assets/Scenes/TeammateSonarHall.unity","Assets/Scenes/SonarHall.unity"})
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);bool opened=!scene.isLoaded;
            if(opened)scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
            if(scene.isDirty)throw new InvalidOperationException("Preserve unsaved edits in "+path);
            foreach(var root in scene.GetRootGameObjects())
            {
                foreach(var wallet in root.GetComponentsInChildren<PlayerWallet>(true)){UnityEngine.Object.DestroyImmediate(wallet);removed++;}
                foreach(var lightning in root.GetComponentsInChildren<BallLightning>(true))
                {lightning.autoCycle=true;PrefabUtility.RecordPrefabInstancePropertyModifications(lightning);}
            }
            EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            if(opened)EditorSceneManager.CloseScene(scene,true);
        }
        int cues=0;
        foreach(string guid in AssetDatabase.FindAssets("t:FloorProgressionProfile"))
        {
            var profile=AssetDatabase.LoadAssetAtPath<FloorProgressionProfile>(AssetDatabase.GUIDToAssetPath(guid));
            profile.defaultEntryText=profile.defaultEntryText.Replace("资源","物品");
            foreach(var rule in profile.overrides)foreach(var cue in rule.entrySubtitles)
            {
                if(cue.text.Contains("棱晶") || cue.text.Contains("收集") || cue.text.Contains("金币靠近"))
                    cue.text="普通球、棱晶和方块均可拿起；左键抛出，右键轻放。";
                else if(cue.text.Contains("WASD"))cue.text="WASD 移动，鼠标转向；瞄准物品左键拿起，持物时左键抛出、右键放下。";
                else if(cue.text.Contains("切换闪电") || cue.text.Contains("开关"))cue.text="球状闪电会自行切换平静与活跃状态，注意避让。";
                else cue.text=cue.text.Replace("资源","物品");
                cues++;
            }
            EditorUtility.SetDirty(profile);
        }
        AssetDatabase.SaveAssets();UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
        return new {removedWalletComponents=removed,updatedCues=cues,scene=active.path};
    }
}
