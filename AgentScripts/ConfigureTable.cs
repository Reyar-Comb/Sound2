using System;
using Sound2.LevelEditing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class ConfigureTable
{
    public static object Main()
    {
        if (Application.isPlaying) throw new Exception("Exit Play mode before authoring.");
        const string materialPath = "Assets/Materials/TablePreview.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "TablePreview", color = new Color(.48f, .29f, .14f) };
            material.SetFloat("_Smoothness", .25f);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        const string prefabPath = "Assets/Prefabs/ResizableTable.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                root = new GameObject("ResizableTable"); SceneManager.MoveGameObjectToScene(root, scene);
                Transform Part(string name)
                {
                    var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = name;
                    part.transform.SetParent(root.transform, false);
                    part.GetComponent<Renderer>().sharedMaterial = material;
                    return part.transform;
                }
                var top = Part("Tabletop");
                var legs = new[] { Part("Leg Front Left"), Part("Leg Front Right"), Part("Leg Back Left"), Part("Leg Back Right") };
                root.AddComponent<ResizableTable>().SetParts(top, legs);
                var prop = root.AddComponent<LevelProp>();
                prop.gameplayMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BlackBox/PureBlack.mat");
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { if (root != null) Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
        }
        FixMugAnchor(); AssetDatabase.SaveAssets();
        return new { table = prefabPath, mugAnchor = "Cup centre, 0.21m above base" };
    }

    static void FixMugAnchor()
    {
        const string path = "Assets/Prefabs/PickupGlassMug.prefab";
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var item = root.GetComponent<PickupItem>();
            if (item.interactionAnchor != null) return;
            var anchor = new GameObject("Interaction Anchor").transform;
            anchor.SetParent(root.transform, false); anchor.localPosition = new Vector3(0, .21f, 0);
            item.interactionAnchor = anchor;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
