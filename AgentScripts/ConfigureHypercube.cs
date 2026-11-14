using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ConfigureHypercube
{
    public static object Main()
    {
        if(Application.isPlaying) throw new System.InvalidOperationException("Author in edit mode.");
        const string path="Assets/Prefabs/RedHypercube.prefab";
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/Scenes/test.unity");
        if(!scene.isLoaded) throw new System.InvalidOperationException("Open test before authoring.");
        var root=new GameObject("Red Hypercube");
        try
        {
            var cube=root.AddComponent<HypercubePointCloud>();
            cube.radius=1.48f; cube.loopSeconds=16;
            root.transform.rotation=Quaternion.Euler(12,24,8);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,path);
            var instance=GameObject.Find("Red Hypercube (Scene)");
            if(instance==null) instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);
            instance.name="Red Hypercube (Scene)";
            instance.transform.position=new Vector3(20,1.6f,2.3f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject=instance;
            return new {prefab=path,scene=scene.path,vertices=16,edges=32,points=HypercubePointCloud.PointCount,periodSeconds=16};
        }
        finally {Object.DestroyImmediate(root);}
    }
}
