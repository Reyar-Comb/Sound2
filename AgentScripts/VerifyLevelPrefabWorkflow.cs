using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sound2.LevelEditing;
using Sound2.LevelEditing.Editor;
using Object=UnityEngine.Object;

public static class VerifyLevelPrefabWorkflow
{
    const string Folder="Assets/__LevelPrefabWorkflow";
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    public static object Run()
    {
        if(Directory.Exists(Folder))throw new Exception("Test folder already exists");
        if(EditorApplication.isPlaying||PrefabStageUtility.GetCurrentPrefabStage()!=null)throw new Exception("Requires edit mode outside Prefab Stage");
        var active=SceneManager.GetActiveScene();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        LevelDesignerWindow window=null;
        try
        {
            Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
            var root=new GameObject("Editable test level",typeof(LevelLayout));SceneManager.MoveGameObjectToScene(root,scene);
            var layout=root.GetComponent<LevelLayout>();layout.rooms.Add(new LevelRoom());LevelBuilder.Rebuild(layout);
            var props=new GameObject("Props");props.transform.SetParent(root.transform,false);
            var prop=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Chair.prefab"),scene);
            prop.transform.SetParent(props.transform,false);prop.transform.localPosition=new Vector3(2,0,1);
            PrefabUtility.RecordPrefabInstancePropertyModifications(prop.transform);
            root.transform.position=new Vector3(37,2,-11);
            string asset=Folder+"/Level.prefab",scenePath=Folder+"/Authoring.unity";
            LevelBuilder.ExportPrefab(layout,asset);
            Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)==asset,"Export must connect original level");
            Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prop)=="Assets/Prefabs/Chair.prefab","Nested prop link retained");
            Check(root.transform.position==new Vector3(37,2,-11),"Export must preserve placement");
            var generated=layout.generatedRoot;var geometryAsset=layout.geometryAsset;
            Check(generated!=null&&generated.GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh!=null&&AssetDatabase.Contains(f.sharedMesh)),"Persistent geometry");
            bool rejected=false;try{LevelBuilder.Rebuild(layout);}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&layout.generatedRoot==generated,"Scene instance rebuild must reject before mutation");
            EditorSceneManager.SaveScene(scene,scenePath);
            Check(layout.generatedRoot==generated&&layout.geometryAsset==geometryAsset,"Saving instance cannot rebuild or copy geometry");
            Check(!Directory.Exists(Folder+"/Authoring_Geometry"),"Saving instance cannot duplicate mesh assets");
            var sceneBytes=File.ReadAllBytes(scenePath);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            Check(sceneBytes.SequenceEqual(File.ReadAllBytes(scenePath)),"Repeat scene save is stable");
            window=ScriptableObject.CreateInstance<LevelDesignerWindow>();window.Bind(layout);
            var stage=PrefabStageUtility.GetCurrentPrefabStage();Check(stage!=null&&stage.assetPath==asset,"Editor opens instance's source Prefab");
            var edited=stage.prefabContentsRoot.GetComponent<LevelLayout>();edited.rooms[0].size=new Vector2(13,17);
            LevelBuilder.Rebuild(edited);LevelBuilder.Persist(edited,asset);PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot,asset);
            StageUtility.GoToMainStage();
            Check(layout.rooms[0].size==new Vector2(13,17),"Prefab edit propagates to scene instance");
            Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)==asset,"Editing retains root link");
            return new {passed=true,checks=11};
        }
        finally
        {
            if(window!=null)Object.DestroyImmediate(window);
            if(PrefabStageUtility.GetCurrentPrefabStage()!=null)StageUtility.GoToMainStage();
            if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
            if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active);
            AssetDatabase.DeleteAsset(Folder);
        }
    }
}
