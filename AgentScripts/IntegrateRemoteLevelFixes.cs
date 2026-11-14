using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Sound2.LevelEditing;
using Object = UnityEngine.Object;

// Retains the authored wall-cabinet and cabin-shell fixes from c98da9b after the level migration.
public static class IntegrateRemoteLevelFixes
{
    static readonly string[] Removed = { "Back handrail", "Side ink rail", "Side ink seam", "Back ink edge", "Floor ink seam", "Back panel -1", "Back panel 0", "Back panel 1" };
    static readonly (string name,float before,float after)[] Cabinets = {
        ("餐厅 · WallCabinet",-32.5f,-30.51f), ("储藏室 · WallCabinet",15,16.99f),
        ("洗衣房 · WallCabinet",39.5f,41.49f), ("内庭 · WallCabinet",16.5f,18.49f)
    };
    static void FixCabinets(Transform props)
    {
        foreach (var item in Cabinets)
        {
            var cabinet=props.Find(item.name);
            if(cabinet==null)throw new Exception("Missing authored cabinet: "+item.name);
            var position=cabinet.localPosition;
            if(Mathf.Abs(position.z-item.before)>.001f&&Mathf.Abs(position.z-item.after)>.001f)
                throw new Exception("Unexpected cabinet placement: "+item.name);
            position.z=item.after;cabinet.localPosition=position;
            PrefabUtility.RecordPrefabInstancePropertyModifications(cabinet);
        }
    }
    public static object Main()
    {
        if(Application.isPlaying)throw new Exception("Edit mode required");
        const string elevatorPath="Assets/Prefabs/LevelElevator.prefab";
        var cabin=PrefabUtility.LoadPrefabContents(elevatorPath);int removed=0;
        try
        {
            foreach(var child in cabin.transform.Cast<Transform>().Where(t=>Removed.Contains(t.name)).ToArray())
            {Object.DestroyImmediate(child.gameObject);removed++;}
            PrefabUtility.SaveAsPrefabAsset(cabin,elevatorPath);
        }finally{PrefabUtility.UnloadPrefabContents(cabin);}
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/BaiYu-Test.unity");
        var world=Object.FindAnyObjectByType<LevelWorld>();
        FixCabinets(world.levels.Single(l=>l.name.StartsWith("F1 ")).transform.Find("Props"));
        const string levelPath="Assets/Levels/F1.prefab";
        var level=PrefabUtility.LoadPrefabContents(levelPath);
        try{FixCabinets(level.transform.Find("Props"));PrefabUtility.SaveAsPrefabAsset(level,levelPath);}
        finally{PrefabUtility.UnloadPrefabContents(level);}
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);AssetDatabase.SaveAssets();
        return new{removedCabinDecorations=removed,correctedCabinets=Cabinets.Length};
    }
    public static object Verify()
    {
        var world=Object.FindAnyObjectByType<LevelWorld>();int panels=0;
        foreach(var elevator in world.GetComponentsInChildren<LevelElevator>(true))
        {
            if(elevator.transform.Cast<Transform>().Any(t=>Removed.Contains(t.name)))throw new Exception("Legacy decoration restored");
            if(Application.isPlaying&&elevator.gameObject.activeInHierarchy)
                foreach(var name in new[]{"Roof","Back","Left","Right","Left jamb","Right jamb","Lintel"})
                {
                    var renderer=elevator.transform.Find(name).GetComponent<Renderer>();var properties=new MaterialPropertyBlock();renderer.GetPropertyBlock(properties);
                    if(properties.GetFloat("_CabinEdges")!=1)throw new Exception("Missing shell edge configuration");panels++;
                }
        }
        foreach(var item in Cabinets)
            if(Mathf.Abs(world.levels[0].transform.Find("Props").Find(item.name).localPosition.z-item.after)>.001f)throw new Exception("Wall fix lost");
        return new{cabinets=Cabinets.Length,activePanels=panels};
    }
}
