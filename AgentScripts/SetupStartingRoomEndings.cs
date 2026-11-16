using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;

public static class SetupStartingRoomEndings
{
    public static object Run()
    {
        if (Application.isPlaying) throw new Exception("Run in Edit Mode.");
        var opening = UnityEngine.Object.FindAnyObjectByType<SoundShellOpening>();
        if (opening == null || opening.startingRoom == null) throw new Exception("Open SoundAndShell first.");
        var scene = opening.gameObject.scene;
        if (scene.path != "Assets/Scenes/SoundAndShell.unity") throw new Exception("Wrong scene.");
        Directory.CreateDirectory("Logs/StartingRoomEndings");
        const string backup = "Logs/StartingRoomEndings/before.unity";
        if (!File.Exists(backup)) EditorSceneManager.SaveScene(scene, backup, true);
        var bed = opening.startingRoom.GetComponentsInChildren<LevelProp>(true)
            .Single(p => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p).EndsWith("/Bed.prefab"));
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("配置初始房间床与结局");
        var interaction = bed.GetComponent<StartingRoomBed>() ?? Undo.AddComponent<StartingRoomBed>(bed.gameObject);
        var anchor = bed.transform.Find("Ending interaction anchor");
        if (anchor == null)
        {
            var go = new GameObject("Ending interaction anchor");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "床交互位置");
            Undo.SetTransformParent(go.transform, bed.transform, "床交互位置");
            anchor = go.transform; anchor.localPosition = new Vector3(0, .7f, 0); anchor.localRotation = Quaternion.identity;
        }
        Undo.RecordObject(interaction, "床交互位置"); interaction.interactionAnchor = anchor;
        PrefabUtility.RecordPrefabInstancePropertyModifications(interaction);
        var whiteBox = opening.startingRoom.transform.Find("White Box");
        var window = whiteBox.GetComponent<StartingRoomWindowVines>() ?? Undo.AddComponent<StartingRoomWindowVines>(whiteBox.gameObject);
        var left = whiteBox.Find("Window Frame Left"); var right = whiteBox.Find("Window Frame Right");
        var top = whiteBox.Find("Window Frame Top"); var bottom = whiteBox.Find("Window Frame Sill");
        if (left == null || right == null || top == null || bottom == null) throw new Exception("Missing window frame.");
        Undo.RecordObject(window, "窗藤蔓覆盖区域");
        window.windowCenter = new Vector3((left.localPosition.x+right.localPosition.x)*.5f,
            (top.localPosition.y+bottom.localPosition.y)*.5f, (left.localPosition.z+right.localPosition.z)*.5f);
        window.windowSize = new Vector3(Mathf.Abs(right.localPosition.x-left.localPosition.x)+.3f,
            Mathf.Abs(top.localPosition.y-bottom.localPosition.y)+.3f, .3f);
        PrefabUtility.RecordPrefabInstancePropertyModifications(window);
        Undo.RecordObject(opening, "结局卡牌");
        opening.hermitCard = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Cards/9-the-hermit.png");
        opening.hangedManCard = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Cards/12-the-hanged-man.png");
        if (opening.hermitCard == null || opening.hangedManCard == null) throw new Exception("Missing ending cards.");
        PrefabUtility.RecordPrefabInstancePropertyModifications(opening);
        EditorSceneManager.MarkSceneDirty(scene); Undo.CollapseUndoOperations(group);
        EditorSceneManager.SaveScene(scene);
        var result = new { scene = scene.path, bed = bed.transform.localPosition.ToString(), rotation=bed.transform.localEulerAngles.ToString(),
            hermit=opening.hermitCard.name,hangedMan=opening.hangedManCard.name, windowCenter=window.windowCenter.ToString(), windowSize=window.windowSize.ToString() };
        File.WriteAllText("Logs/StartingRoomEndings/setup.json", Newtonsoft.Json.JsonConvert.SerializeObject(result));
        return result;
    }

    public static object PreparePlay()
    {
        const string path="Assets/__EndingVerification.unity";
        if (File.Exists(path)) throw new Exception("Temporary scene already exists.");
        var scene=SceneManager.GetActiveScene();
        if (scene.isDirty) throw new Exception("Finish saving ending configuration first.");
        SessionState.SetString("EndingVerification.StartScene", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.SaveScene(scene,path,true); AssetDatabase.ImportAsset(path);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        return path;
    }

    public static object Cleanup()
    {
        if(Application.isPlaying)throw new Exception("Stop Play Mode first.");
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString("EndingVerification.StartScene",""));
        AssetDatabase.DeleteAsset("Assets/__EndingVerification.unity");
        return new { scene=SceneManager.GetActiveScene().path, dirty=SceneManager.GetActiveScene().isDirty };
    }
}
