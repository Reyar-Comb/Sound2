using System;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ConfigureResizableWardCurtain
{
    public static object Main()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before editing the prefab.");
        const string path = "Assets/Prefabs/WardCurtain.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        GameObject cylinder = null, sphere = null;
        try
        {
            cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder); sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var curtain = root.GetComponent<ResizableWardCurtain>() ?? root.AddComponent<ResizableWardCurtain>();
            var model = root.transform.Find("Model").GetComponent<MeshFilter>();
            curtain.Configure(model, cylinder.GetComponent<MeshFilter>().sharedMesh, sphere.GetComponent<MeshFilter>().sharedMesh, root.GetComponent<BoxCollider>());
            var mesh = PropMeshAssets.Store("Assets/Models/Furniture", "WardCurtain", Object.Instantiate(model.sharedMesh));
            curtain.UseStoredMesh(mesh); PrefabUtility.SaveAsPrefabAsset(root, path); AssetDatabase.SaveAssets();
            return new { prefab = path, curtain.length, guid = AssetDatabase.AssetPathToGUID(path) };
        }
        finally
        {
            if (cylinder != null) Object.DestroyImmediate(cylinder); if (sphere != null) Object.DestroyImmediate(sphere);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
